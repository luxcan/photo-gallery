# 04 — Gallery

**Status: ✅ Built and driven end to end**

Seeing the photos. The first screen where the library stops being a table of
paths and starts being pictures.

Every number below was measured against the real library — 16,225 indexed
assets in `D:\Pictures` — or in a WPF harness on the
target machine. Nothing here is estimated.

---

## Goal

Browse the whole library as pictures, two ways: a flat grid newest-first, and a
folder tree beside a grid. Click any picture to see it large, and move through
the collection from there without coming back.

## Depends on

[03 — Thumbnails](03-thumbnails.md) — without tiles there is nothing to show.

---

## What this feature inherits

Five facts about the code as it stands. Each was measured, and each would cost a
day if met for the first time halfway through the build.

| | Measured | Consequence for 04 |
|---|---|---|
| `TakenUtc` is written by nothing | **0 of 16,225** rows populated | "Newest first by taken date" sorts 100% of the library by file date, not 11%. 03 must extract it. |
| The tiles do not exist | 11,481 rows name a thumbnail; there is **no `thumbs` folder** | The grid must judge readiness by the file, not the column. Otherwise 04 ships 11,482 placeholders. |
| Perceptual hashes are the last exploration artefact | **11,481** stored; the Python cache and manifest are gone from disk | A thumbnail pass over those rows destroys them ([`SqliteAssetRepository.cs:124`](../../src/PhotoGallery.Infrastructure/Persistence/SqliteAssetRepository.cs) sets the hash unconditionally, [`BuildThumbnailsHandler.cs:86`](../../src/PhotoGallery.Application/UseCases/Thumbnails/BuildThumbnailsHandler.cs) always passes `null`). Feature [05](05-duplicates.md) depends on them entirely. |
| Sharding does not shard | `ThumbnailName` is `{assetId:x8}.jpg`, sharded on the first two characters | Every id below 16,777,216 begins `00`, so all 32,450 files land in `thumbs\00\` — the exact thing the shard exists to avoid. |
| `BuildThumbnailsHandler` is not registered | Absent from [`App.xaml.cs`](../../src/PhotoGallery.App/App.xaml.cs) | Nothing can start a pass, which is why 03 is amber. |

**All five are 03's to fix, and 03 is built first.** They are recorded here
because 04 is what makes them visible.

---

## Settled decisions

| Decision | Why |
|---|---|
| **No paging.** One query returns every row; the grid virtualises. | Measured: the whole ordered projection is **20 ms / 2.3 MB**. The same result fetched as 39 pages of 300 is **266 ms** — 13× the work, because each page repeats the scan and rebuilds the sort. Paging costs on both sides. |
| **Rows of N photos in a `ListBox`**, not a `WrapPanel` of photos. | WPF genuinely has no virtualising wrap panel — the only `VirtualizingPanel` subclasses in .NET 9 are `VirtualizingStackPanel`, `DataGridCellsPanel` and `DataGridRowsPresenter`. Making an item a *row* lets the stock panel virtualise it. |
| **Sort by `TakenUtc`, then `ModifiedUtc`**, tie-broken by `Id`. | See below — creation date is actively wrong for this library. |
| **Videos appear in the grid.** | 4,743 of them, 91% of the bytes, and a real share of the best moments. Hiding them is a lie about what the library holds. |
| **Search is a placeholder.** | Real search gets its own PRP and integrates the AI models; it supersedes [07](07-content-search.md). Until then the folder tree is how you find an event. |
| **Click opens a full view**, with `<` and `>`. | 03 already pays for a 1024px preview whose stated purpose is exactly this. A grid you cannot click into is a dead end, and [`PRP.md`](../PRP.md) forbids those. |
| **No migration.** | No new column, no new index. The unpaged sort is 20 ms unindexed; an index would buy nothing measurable. |

---

## Why not creation date

The instruction was "order by taken date, then file created date". Measured over
3,000 photos on the real share, creation date is the day the file was **copied**,
not the day it was taken:

| | Creation date | Modified date |
|---|---|---|
| Distinct days | **13** | **261** |
| Range | 2015-09-25 → 2018-03-18 | 2012-02-22 → 2020-03-30 |
| Later than the other (of 800 sampled) | **797** | 3 |

```
P1070303.JPG   created 2017-04-29 02:16:50   modified 2012-03-14 03:05:36
```

961 files share the creation day 2017-04-29; 843 share 2018-03-15. Windows
preserves last-write across a copy and resets creation to the moment of copying,
so ordering by it would collapse a fourteen-year archive into thirteen piles and
put the oldest photos at the top of "newest first".

**The chain is `TakenUtc` → `ModifiedUtc`.** Modified survived the copies.

> Today `TakenUtc` is null for every row, so the order is *entirely*
> `ModifiedUtc` — which is right for most files and wrong for the ones that were
> edited. The order is provisional until 03's EXIF extraction lands, and the UI
> must not print a confident date it does not have.

---

## Behaviour

### The Library section

The gallery lives in the existing **Library** side-nav section. It is one
screen with a mode switch, not two sections.

### Search — placeholder

A search box sits above the grid, disabled, with placeholder text:

> `Search is coming — for now, browse by folder`

It is present because the layout is designed around it and because the next PRP
fills it. It is disabled rather than absent so nothing about the screen moves
when search arrives.

### View 1 — Photos

Every photo and video in the library, newest first, across all sources. No
folders, no grouping — one continuous grid.

### View 2 — Folders

A folder tree on the left, the grid on the right. Selecting a folder shows what
is in it and everything beneath it.

The tree is per source and mirrors the real structure, with a count on each
folder. Measured: **210 nodes** — 209 top-level folders that hold photos, plus
one nested folder (`20250419 - Kidzania\signs`). 16,188 files sit one level
deep, 37 sit two deep, and none sit at a source root.

> This tree belongs to this one view. It is not the global side bar that was
> deliberately removed in [00](00-foundation.md).

### Switching

The two views share one result set and one scroll position where they can.
Switching to Folders keeps the grid; it just gains a filter.

### The photo view

Clicking any tile opens the picture large, over the content area.

- Draws the **1024px preview**, which [03](03-thumbnails.md) already produces.
- **`<` and `>`** move to the previous and next picture **in the current result
  set** — not just what has been scrolled past. From the last photo of a folder,
  `>` is disabled rather than wrapping.
- **Left and Right arrow keys** do the same. **Escape** closes.
- Shows the file name and the folder it came from. The folder name is where the
  meaning lives in this library (`20200214_Ana Lim Born`), and unlike the date it
  is always true. A date is shown **only** when `TakenUtc` is populated.
- Closing returns to the grid with the photo still in view.

Videos open too, showing the placeholder at preview size and saying that
playback arrives with [08](08-video.md). No dead ends.

### Videos in the grid

Videos appear in date order alongside photos, with a **film badge** in the
corner. Until [08](08-video.md) builds posters they draw the placeholder.

The count line says so plainly, so a grid of grey rectangles is never a mystery:

> `16,225 pictures — 4,743 videos have no preview yet`

### Placeholders, empty and busy

| State | What the user sees |
|---|---|
| No tile file on disk | A neutral cell with a picture glyph. The cell is the same size as every other, so nothing shifts when the real tile arrives. |
| No photos indexed at all | "No pictures yet. Add a folder under Photo sources, then scan it." |
| Nothing in the selected folder | "Nothing in this folder." |
| No tiles built yet | A banner above the grid: "Your pictures have not been prepared yet. Preparing them makes browsing instant." with the **Prepare pictures** action from [03](03-thumbnails.md). |

---

## How the grid renders 16,225 tiles

The original plan was to page the query and accept a non-virtualising
`WrapPanel`. Measured in a WPF harness on the target machine, that does not
hold up.

| Approach | Containers realised | First layout | Visual tree |
|---|---|---|---|
| `WrapPanel`, 300 items | 300 | 129 ms | 5.3 MB |
| `WrapPanel`, 3,000 items | 3,000 | 694 ms | — |
| `WrapPanel`, 11,482 items | 11,482 | **1,685 ms** | **89.9 MB** |
| **`ListBox` of rows of 6** | **30 tiles** | **11.9 ms** | flat |

A `WrapPanel` inside a `ListBox` does not help: setting
`VirtualizingPanel.IsVirtualizing="True"` on one is **silently inert** — the
panel still realised all 11,482 children.

So an item is a **row of N photos**, and the stock `VirtualizingStackPanel`
virtualises rows. Roughly 12 rows are alive at any moment regardless of library
size.

### The two settings that matter

`VirtualizationMode="Recycling"` — the default `Standard` recreates containers
instead of reusing them. `ScrollUnit="Pixel"` — the default `Item` jumps a whole
row per wheel notch.

`CacheLength` and `CacheLengthUnit` already default to one **page** either side,
so setting them changes nothing. They are not the fix they look like.

### Memory — virtualising rows is only half of it

A frozen 400px tile costs **470–520 KB** of unmanaged bitmap — pixels WPF keeps
outside the managed heap, where the GC cannot see them and will not collect
under pressure.

Virtualising the rows keeps the *visual tree* small but does nothing about the
*bitmaps*: a tile decoded once stays decoded for as long as the view model holds
it. Built that way and measured on the real library, the app reached **2,133 MB**
and was climbing toward 16,225 × ~490 KB ≈ **7.6 GB**.

So the decoded pictures follow the viewport: the visible page plus one either
side, and everything outside is released.

| | Measured |
|---|---|
| Grid open, unbounded | **2,133 MB** |
| Grid open, windowed | **190 MB** |
| Ceiling, any library size | 240 tiles ≈ **120 MB** of bitmap |

Coming back to a released tile costs one decode — about a fifth of a
millisecond — so scrolling back up is not noticeably different from scrolling
down.

> **The query is not paged; only the pictures are.** Row data is 140 bytes each,
> so the whole library is 2.3 MB and one fetch of 20 ms. Fetching it as 39 pages
> measured 266 ms, because every page repeats the scan and rebuilds the sort.
> The expensive thing is the bitmap, and that is what the window bounds.

### Preparation follows the same order as the grid

The thumbnail pass takes its work newest-first, matching the sort the gallery
shows. Working in scan order instead meant thousands of pictures were prepared
before any of them was one the user could see — the grid stayed grey while the
counter climbed.

### A rendition's name changes when it is prepared

Renditions are named after the picture's content ([03](03-thumbnails.md)), so
preparing a picture *renames* its files. A grid loaded before a pass therefore
holds names that are about to become wrong, and a tile that kept asking for its
original name would stay blank forever while the file it needed sat on disk
under another one. Tiles re-read their current name whenever the grid refreshes.

### Decoded is not the same as prepared

Because pictures outside the window are released, "has no decoded picture" stops
meaning "has no rendition". The count line asks the disk instead — one existence
check per picture, about a tenth of a second — or it reports almost the whole
library as outstanding.

### Do not set `DecodePixelWidth`

The tile is already 400px **on its longest edge**. A portrait tile is 300×400,
so `DecodePixelWidth=400` *upscales* it to 400×533 — measured **869 KB versus
522 KB** for zero quality gain. Decoding at 200 is worse in a different way: the
binding axis in a square cell is the short edge, so a 200×150 decode is scaled
back up and goes soft at exactly the DPI the 400px tile was sized for.

Decode natively. [`ThumbnailSizes`](../../src/PhotoGallery.Application/Ports/ThumbnailSizes.cs)
already chose the number.

### Resizing

Rows are re-chunked only when the integer column count actually changes — a few
times during a drag, not once per mouse-move (measured 0.5–2.2 ms per
re-layout). Because every row is the same height, scroll position is arithmetic:

```
firstItem = (int)(VerticalOffset / RowHeight) * oldColumns
ScrollToVerticalOffset(firstItem / newColumns * RowHeight)
```

---

## Reading the library

### Loading tiles

Decoding happens off the dispatcher and the result is handed back frozen, using
the marshalling the scan already uses — a `Progress<T>` created on the UI thread.
Measured: `Parallel.ForEachAsync` ran 400 iterations with **0 on the caller**,
and the progress callback ran **400/400 on the owning thread**. No `Dispatcher`
call is needed, which keeps the App project's zero-`Dispatcher` record.

Four things were measured and each decides a line of code:

| Finding | Consequence |
|---|---|
| Decode is **0.20 ms/tile at 4 threads**; 200 tiles ≈ 41 ms | Load a screenful at once. There is no case for loading as containers realise. |
| `OnDemand` + `UriSource` **locks the file** | Use `OnLoad`, or a running thumbnail pass cannot overwrite a tile. |
| A reader with `FileShare.Read` **fails** against a live writer; `FileShare.ReadWrite` succeeds | Open the `FileStream` by hand. `UriSource` does not expose share flags. |
| `IgnoreImageCache` + `StreamSource` throws `ArgumentNullException('key')` | Do not set it. `StreamSource` never enters the Uri-keyed cache, so there is nothing stale to avoid. |

A half-written JPEG **decodes without throwing** — WIC returns a partial image.
So the grid refreshes from the database, where a name appears only after the
file is closed, and never by polling the folder.

> **The catch filter trap.** `System.IO.FileFormatException` — what WIC raises
> for a corrupt or header-truncated JPEG — derives from `FormatException`, *not*
> `IOException`. A filter of `IOException or NotSupportedException` misses it
> entirely, and a tile truncated by an interrupted pass is exactly what a
> resumable feature leaves behind. The filter must include `FormatException` and
> `COMException`.

### Querying

Read-side only, separate from `IAssetRepository`: the gallery wants projected
rows to bind to, not tracked entities. `IGalleryReader` already exists and
already serves 03; this adds to it.

**Ordering.** `COALESCE(TakenUtc, ModifiedUtc) DESC, Id DESC`. SQLite cannot use
`IX_Assets_TakenUtc` for a `COALESCE`, so the plan is a scan plus a temp B-tree —
**20 ms for the whole library**, once. The tie-break is not optional: **1,964
photos share an exact timestamp** with at least one other, the largest tie group
being 43. Without `Id`, order is undefined inside those groups and the viewer's
`>` could revisit a photo.

**Folder filtering** is an ordinal range, not a `LIKE` prefix:

```
RelativePath >= folder + "\"   and   RelativePath < folder + "]"
```

`]` is 0x5D, the immediate successor of `\` 0x5C, so the half-open range is
exactly the subtree. Both traps are real in this library, not hypothetical:
**8 pairs of top-level folders collide by prefix** (`20220201` and
`20220201 - CNY`), which appending the separator excludes; and **46 of 219
folders contain `_`**, a single-character `LIKE` wildcard — unescaped,
`LIKE '%_%'` matches all 16,225 rows. The range predicate sidesteps the question
and seeks `IX_Assets_PhotoSourceId_RelativePath` instead of scanning.

A folder always carries its `PhotoSourceId`. Without it the range would merge
identically named folders across sources.

**The folder tree** is built in memory, not in SQL. SQLite has no path
functions, EF Core cannot translate the `rtrim`/`replace` trick, and the result
would give leaf counts only while the tree needs ancestors rolled up. Measured:
projecting the two columns for 11,482 photos is 439 KB on the wire and **12 ms**
end to end including the roll-up.

Ordinal ordering guarantees every ancestor precedes its descendants, so one
forward pass with a stack builds the tree. It does **not** guarantee a parent is
immediately followed by its children — a sibling can sort between them
(`20220201`, `20220201 - CNY`, `20220201\…`), which is true of 7 folder pairs
here.

### Concurrency

`GalleryDbContext` is scoped and `MainViewModel` is a singleton, so the gallery
opens a scope per query exactly as the scan does. Injecting `IGalleryReader`
into the view model would capture a scoped context in a singleton and throw "a
second operation was started on this context instance" the moment two requests
overlap.

Reads never fail during a thumbnail pass — `Microsoft.Data.Sqlite` applies a
30-second busy timeout, so contention shows up as blocking, not errors. But it
matters which journal mode is in force: with the rollback journal, a paging
reader starved the writer to a **21.8-second single update**; under WAL the same
pair ran at 736 writes/s with an 8 ms worst case. EF Core's migration pipeline
sets WAL and `MigrateAsync` runs on every library open, so every library the app
touches is WAL from first open. Worth knowing, not worth code.

---

## Contracts

```
GalleryQuery      Search?, PhotoSourceId?, FolderPath?, IncludeVideos, Skip, Take
GalleryItem       Id, RelativePath, FileName, FolderPath, ThumbnailName,
                  TakenUtc, ModifiedUtc, Kind
GalleryPage       Items, TotalCount          Empty { get; }
FolderNode        PhotoSourceId, RelativeFolder, Name, ItemCount, Children

IGalleryReader    QueryAsync(GalleryQuery)  → GalleryPage
                  GetFoldersAsync()         → IReadOnlyList<FolderNode>
                  GetPendingThumbnailsAsync (exists)

QueryGalleryHandler   the read use case, one scope per call
```

`Width`/`Height` are deliberately **not** on `GalleryItem`. They are null for
every photo without a thumbnail, can be literal `0` from the import path, and are
stored **pre-rotation** while the tile has EXIF orientation applied — so a
portrait photo's stored aspect is transposed. Square cells need no aspect data,
which is both less code and the only correct choice given that data.

`Skip`/`Take` stay on `GalleryQuery` — unused by the grid, which takes
everything, but the viewer and future search will want them.

---

## Implementation plan

Ordered so each step leaves the build green.

### Application

| File | Change |
|---|---|
| `Ports/GalleryItem.cs` | new record |
| `Ports/GalleryPage.cs` | new record, `Empty { get; }` matching `LibraryCounts.Empty` |
| `Ports/FolderNode.cs` | new record |
| `Ports/GalleryQuery.cs` | `PhotosOnly` → `IncludeVideos`; add `FolderPath` normalisation note |
| `Ports/IGalleryReader.cs` | add `QueryAsync`, `GetFoldersAsync` |
| `UseCases/Gallery/QueryGalleryHandler.cs` | new; validates the query, requires `PhotoSourceId` alongside `FolderPath` |
| `UseCases/Gallery/GetFolderTreeHandler.cs` | new; projects two columns, rolls up in memory |

### Infrastructure

| File | Change |
|---|---|
| `Persistence/SqliteGalleryReader.cs` | implement both methods; ordinal range for folders, `COALESCE` sort, `Id` tie-break |

### App

| File | Change |
|---|---|
| `Gallery/GalleryViewModel.cs` | new; `Rows`, `Columns`, `TotalCount`, `CountSummary`, `EmptyMessage`, `SelectedFolder`, mode switch |
| `Gallery/GalleryTile.cs` | new; one photo, `Image` filled asynchronously |
| `Gallery/GalleryRow.cs` | new; N tiles |
| `Gallery/GalleryLayout.cs` | new; `CellSize`, `RowHeight`, `ColumnsFor(width)` — one place for the geometry |
| `Gallery/PhotoViewModel.cs` | new; the open photo, `Previous`/`Next`/`Close`, `CanGoNext` |
| `Imaging/TileImageLoader.cs` | new; the frozen-`BitmapImage` load, path resolution **inside** its own try |
| `Shell/MainWindow.xaml` | the Library grid beside the Photo sources grid; the viewer overlay |
| `ViewModels/MainViewModel.cs` | expose `Gallery`; `ShowLibrary`; load on first switch to Library |
| `Theme/Controls.xaml` | tile, row, tree and viewer styles beside the existing six converters |
| `App.xaml.cs` | register `QueryGalleryHandler`, `GetFolderTreeHandler` |

New theme keys: none. `Neutral.3` for the placeholder cell and
`Badge.Background`/`Badge.Foreground` for the video badge already exist in both
palettes, so `ThemeParityTests` is untouched.

### Two traps the XAML must avoid

- **`RelativeSource` repoints the binding source to the `Window`, not its
  `DataContext`.** `{Binding ShowLibrary, RelativeSource={RelativeSource
  AncestorType=Window}}` resolves against `Window`, silently fails, and falls
  back to `Visible` — painting the Library grid permanently on top of Photo
  sources. Every existing binding uses the `DataContext.X` form; the new ones
  must too.
- **The overlay must be focusable.** A `Border` or `Grid` is not, so `KeyDown`
  never reaches it: Escape would do nothing and the arrow keys would scroll the
  grid behind. It needs `Focusable="True"` and `Focus()` on open.

### The one number that must not drift

`GalleryLayout.RowHeight` appears in C# and in the XAML cell size, margin and
row height. Measured, a stock `ListBoxItem` container yields **212 px/row**
against the retemplated **208** — over 2,297 rows that is 9,188 px of drift,
breaking both the scrollbar and the resize arithmetic. The XAML binds to
`GalleryLayout` via `x:Static`, and a test asserts the measured row height
matches it.

---

## Acceptance

Written as tests, so a build session can verify itself. xunit, `[Fact]`, real
SQLite in a temp folder — the pattern of `ScanPhotoSourceHandlerTests`.

### `QueryGalleryHandlerTests`

| Test | Given → then |
|---|---|
| `Query_ReturnsNewestFirstByTakenDate` | three photos, middle one with `TakenUtc` newer than the others' `ModifiedUtc` → it comes first |
| `Query_FallsBackToModifiedWhenTakenIsNull` | no `TakenUtc` anywhere → order is `ModifiedUtc` descending |
| `Query_BreaksTiesByIdSoOrderIsStable` | two photos sharing an exact `ModifiedUtc` → higher `Id` first, and two runs agree |
| `Query_IncludesVideosByDefault` | one photo, one video → both returned, video's `Kind` preserved |
| `Query_ExcludesVideosWhenAsked` | `IncludeVideos = false` → photo only |
| `Query_FolderFilterExcludesPrefixSibling` | `20220201` and `20220201 - CNY` both hold photos; filter on `20220201` → only its own |
| `Query_FolderFilterIncludesNestedFolders` | `A\b.jpg` and `A\sub\c.jpg`; filter on `A` → both |
| `Query_FolderFilterTreatsUnderscoreLiterally` | `2015_Ana` and `2015XAna`; filter on `2015_Ana` → only the first |
| `Query_FolderFilterIsScopedToItsSource` | same folder name in two sources → only the requested source's |
| `Query_TotalCountIgnoresPaging` | 10 rows, `Take = 3` → 3 items, `TotalCount` 10 |
| `Query_ReturnsEmptyPageWhenNothingMatches` | filter matching nothing → `Items` empty, `TotalCount` 0, no throw |

### `GetFolderTreeHandlerTests`

| Test | Given → then |
|---|---|
| `Folders_MirrorTheRealStructure` | `A\x.jpg`, `A\sub\y.jpg`, `B\z.jpg` → `A` (with child `sub`) and `B` |
| `Folders_CountIncludesDescendants` | `A\x.jpg` and `A\sub\y.jpg` → `A` counts 2, `sub` counts 1 |
| `Folders_BuildCorrectlyWhenASiblingSortsBetweenParentAndChild` | `20220201`, `20220201 - CNY`, `20220201\sub` → `sub` is a child of `20220201`, not of the sibling |
| `Folders_AreScopedPerSource` | same folder name in two sources → two nodes, each with its own `PhotoSourceId` |
| `Folders_ExcludeFoldersHoldingOnlyVideos` | a folder of videos only → absent when photos-only is asked |

### `TileImageLoaderTests`

| Test | Given → then |
|---|---|
| `Load_ReturnsFrozenImage` | a real 400px JPEG → non-null and `IsFrozen` |
| `Load_ReturnsNullForMissingFile` | a path that does not exist → null, no throw |
| `Load_ReturnsNullForTruncatedHeader` | first 200 bytes of a real tile → null, no throw *(this is the `FormatException` case)* |
| `Load_ReturnsNullForZeroLengthFile` | an empty file → null, no throw |
| `Load_DoesNotLockTheFile` | load, then overwrite the same path → succeeds |
| `Load_SucceedsWhileAnotherProcessHoldsTheFileOpen` | writer holding it with `FileShare.ReadWrite` → still loads |

### `GalleryLayoutTests`

| Test | Given → then |
|---|---|
| `Columns_FitTheAvailableWidth` | 1044 px → 4 columns |
| `Columns_NeverDropBelowOne` | 40 px → 1 |
| `Rows_ChunkItemsInOrder` | 7 items, 3 columns → rows of 3, 3, 1, original order preserved |
| `RowHeight_MatchesTheCellAndItsMargin` | the constants agree, so the scroll arithmetic holds |

### Driven in the app

Verified by running it, not by assertion:

- [x] Library opens on the photos grid, with 20 cells realised out of 16,225.
- [x] Memory stays bounded — 190 MB with the grid open, against 2,133 MB unbounded.
- [x] The folder tree shows real names with counts, and selecting `20200214_Ana Lim Born` filtered the grid to exactly its 139 items.
- [x] Clicking a photo opens it large; `>` and `<` moved from `20221002 - New Water` to `20250419 - Kidzania` without returning to the grid; Escape closed it.
- [x] `<` is disabled on the first photo rather than wrapping.
- [x] Videos appear in date order with a film badge; the count line says how many are not prepared.
- [x] A photo with no rendition shows a placeholder of the same size, not a gap.
- [x] The grid fills from the top while a pass runs.
- [x] Resizing re-flows the rows — measured 5 → 7 → 3 → 5 columns at 1180, 1600, 900 and 1180 wide.
- [x] Both themes read correctly, including the placeholder cells, the hover highlight and the tooltip.

Not reachable, and therefore not a test: **an empty folder**. The tree is built
from the files themselves, so a folder with nothing in it has no node to select.
`EmptyMessage` still covers the library-wide case — no pictures indexed at all.

---

## The numbers that should become settings

These are tuned to one machine and one library, and none of them is a law:

| | Now | Where it belongs |
|---|---|---|
| Pictures kept decoded either side of the viewport | 80 | `LibrarySettings` |
| Tiles decoded at once | 4 | `LibrarySettings` |
| Photos read at once during preparation | 8 | `LibrarySettings` |
| Photos written per batch | 20 | `LibrarySettings` |

`LibrarySettings` already exists and already stores the theme, so it is the
natural home. The rule when they move: **an empty setting takes the value here**,
so a library that has never been tuned behaves exactly as it does today and
nobody has to know these numbers exist.

Deliberately not done yet. They are worth making adjustable once the values have
been lived with, not while the view they govern is still being verified.

---

## Out of scope

Search — its own PRP, with the AI models. Editing, rating, tagging, albums.
Sorting beyond newest-first. Selecting multiple photos. Zoom or pan in the photo
view. Video playback ([08](08-video.md)).
