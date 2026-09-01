# PhotoGallery — Product Requirements

The product-level document: what this app is, the facts that constrain it, and
the decisions that are settled. Each feature has its own PRP under
[`docs/prp/`](prp/) and is built one at a time.

Numbers quoted here were measured against the real library, not estimated.

---

## 1. What it is

A standalone Windows desktop app that makes a large personal photo collection
searchable — by person, by folder, by date, and later by what is in the picture.

**One user, their own machine, their own photos.** Not a server, not multi-user,
not shared. That single fact justifies most of the architecture below.

It survives [12](prp/12-sharing.md) intact, which is the test of whether it was
the right fact. Sharing between the family's laptops exchanges *decisions* — who
is in a picture, what an album is called — and never opens one database to two
machines. Every copy stays a single-user library that happens to have been told
what another single-user library concluded.

### The library it was built against

| | |
|---|---|
| Files | 17,023 |
| Total size | 291.8 GB |
| Photos | 11,481 (~24.8 GB) |
| Videos | 4,743 (~267 GB — 91% of the bytes) |
| Top-level folders | 219 |
| Photos with EXIF date | 89% |
| Photos with GPS | **17%** (planned around 39%; see [10](prp/10-location.md)) |

Folder names carry real meaning: `20200214_Ana Lim Born`, `20230203 - Chingay`.
Dates *and* subjects. This is unusual, and the app should exploit it rather than
ignore it.

---

## 2. Hard constraints

Physical facts about the target environment. Designs that ignore them fail.

| Constraint | Measured | Consequence |
|---|---|---|
| Network to the share | **6.4 MB/s** (Wi-Fi, 73 Mbps link) | Reading all photos costs ~1 hour; all videos ~11 hours. Read once, cache locally. |
| NAS hardware | Celeron N3050, 2 GB RAM | Cannot host anything. Storage only; all compute on the PC. |
| PC | Core Ultra 9 185H, 32 GB, RTX 4070 | All indexing and ML runs here. |
| Directory metadata | 17,023 files walked in **~45 s** | Metadata passes are cheap; byte passes are not. Separate them. |
| Face detection | **676 ms/photo** CPU, detection+recognition only | A full pass is ~90 min, no GPU required. |
| Windows MAX_PATH | `onnx` install failed at a deep path | Repo and virtual environments must live at short paths. |

> **The governing rule: never read an original twice.** Every expensive pass must
> be resumable, and must produce everything obtainable from that one read.

---

## 3. Settled decisions

Recorded with their reasons so they are not re-litigated.

| Decision | Why |
|---|---|
| Standalone single-file exe, self-contained | No install, no .NET on the target machine. |
| Clean Architecture, four projects | The ONNX face port is the code most likely to be silently wrong; behind a port it can be tested against a reference implementation. |
| SQLite, one file, in the working folder | One user, ~16k assets. All face vectors together are ~37 MB — they load into memory and a similarity search is one matrix multiply. pgvector would be cost without benefit. |
| Thumbnails as **files**, not blobs | 1.6 GB inside SQLite would make the index unwieldy and every backup enormous. The row holds a name. |
| **Many** photo sources, not one | A library is folders from anywhere: this PC, a USB drive, a network share. Never say "the NAS" in the UI. |
| `config.json` in `%APPDATA%` | Only what must be known before a library is located: last working folder, recents, theme. Everything else lives in the working folder. |
| VS Code shell language | Its layout already solves this: mode switcher, main area, output panel for long jobs, status bar. |
| No global side bar | Removed. Each section fills the width; a tree belongs to the one view that needs it. |
| Real `ListView`/`GridView` for tables | Two hand-maintained column lists drifted 60px apart. Header and rows must share one definition. |

### Copy rules

- Plain language, no jargon. "Small previews of your pictures", not "the index".
- Never name a specific device or protocol in the UI.
- Every screen states what happens next; no dead ends.
- Say what is *about* to happen before the click, not after.

---

## 4. Where things live

`<working folder>/`
```
index.db          SQLite: assets, faces, people, places, content vectors,
                  duplicate sets, settings
thumbs/           two renditions per photo, sharded 2 chars deep
  a3/a3f1c2d4.jpg      tile     400px
  a3/a3f1c2d4-p.jpg    preview 1024px
models/           imported by hand today; extraction and download are 09's
                  unbuilt half
quarantine/       duplicates set aside, with a manifest for whoever opens it
logs/             diagnostic.log, off by default, previous run kept beside it
```

`config.json` **beside the executable** — `LastWorkingFolder` and `Theme`, and
nothing else. Written atomically; a corrupt file falls back to defaults.

Beside the exe **and nowhere else**. This ships as one self-contained file, so a
config that travels with it means the app can be moved, copied to a USB stick or
run from a share without losing where its library is — and deleting that one
file gives a genuinely clean start, which is only true while there is no second
copy anywhere to fall back to. A fallback in the user profile was tried and
removed for exactly that reason: it made deleting the config look like it had
worked while the old answer quietly came back.

No recents list. One user with one library does not need a history, and the
set-up screen already prefills the last folder.

Default theme is **Light**.

---

## 5. Feature PRPs

Built one at a time. Each is self-contained: goal, behaviour, contracts,
acceptance criteria.

The table is in **build order**, and the numbers jump: 09 to 11 were written
after the order was set, and two of them are prerequisites of features numbered
before them. A number is a PRP's identity, not its turn.

| # | Feature | Status | PRP |
|---|---|---|---|
| 00 | Foundation — shell, set-up, theme | ✅ Done | [00-foundation.md](prp/00-foundation.md) |
| 01 | Photo sources | ✅ Done | [01-photo-sources.md](prp/01-photo-sources.md) |
| 02 | Scanning | ✅ Done | [02-scanning.md](prp/02-scanning.md) |
| 03 | Thumbnails | ✅ Done | [03-thumbnails.md](prp/03-thumbnails.md) |
| 04 | Gallery | ✅ Done | [04-gallery.md](prp/04-gallery.md) |
| 06 | Faces and people | ✅ Done | [06-faces.md](prp/06-faces.md) |
| 05 | Duplicates | ✅ Done | [05-duplicates.md](prp/05-duplicates.md) |
| 10 | Location — coordinates and place names | ✅ Done | [10-location.md](prp/10-location.md) |
| 07 | Search | ✅ Done — bar its download | [07-content-search.md](prp/07-content-search.md) |
| 11 | Albums — proposed groupings | ⬜ **Next** | [11-albums.md](prp/11-albums.md) |
| 09 | Models — installing, removing and licence | ◐ Half built | [09-models.md](prp/09-models.md) |
| 08 | Video | ◐ Posters ship | [08-video.md](prp/08-video.md) |
| 12 | Sharing between machines | ✅ Done | [12-sharing.md](prp/12-sharing.md) |
| 13 | Collections — shelves of albums | ⬜ Planned | [13-collections.md](prp/13-collections.md) |

**Location before models** deliberately. It needs no weights, it rides a read the
app already does, and it is the one signal the album names cannot fake — an
album with no place is *"March 2019, 42 photos"* rather than
*"Genting Trip"*.

That ordering held, and [07](prp/07-content-search.md) then overtook
[09](prp/09-models.md) too. Both features that need weights now work, and neither
can install them: faces offer "use model files I have…", content search offers
nothing and has its four files copied into `models\` by hand, and a fresh install
has ~1.9 GB to be located before either works. The licences the manifest names are
shown nowhere. **09 is the outstanding debt of everything already shipped**, where
[11](prp/11-albums.md) is new work that is now unblocked and needs no weights
at all.

---

## 6. Standing risks

| Risk | Standing |
|---|---|
| ONNX face port silently wrong | Compared against the Python reference during the port: 32 of 32 faces, mean cosine 0.9990. **That comparison is not in the test suite**, so a subtly wrong alignment would no longer be caught — the first place to look if people start matching wrongly. See [06](prp/06-faces.md). |
| InsightFace weights are non-commercial | Fine for personal use; a blocker if ever sold. Kept behind a port so they can be swapped. |
| Videos | 91% of the bytes. **Half closed.** Every clip can now be given a poster from the same thumbnail Explorer shows, so videos are in the grid and their faces are found — and it seeks rather than reading through, so it costs far less than the ~11 hours a full read would. What is still open is *inside* a clip: the shell gives one frame and no duration, so a person who appears only nine minutes in is still unfindable until a seeking extractor lands. See [08](prp/08-video.md). |
| Wi-Fi | Every pass is ~10× slower than over a cable. Worth plugging in for the big ones. |
| ~~Perceptual hashes are irreplaceable~~ | **Closed.** The stored hashes were the only surviving artefact of the Python exploration and a thumbnail pass overwrote them with null. The preparing pass now computes one from the decode it is already doing, so they are reproducible rather than precious. |
| No model can be installed | Every model arrives by hand, and the four content-search files have no importer at all ([09](prp/09-models.md)). A fresh install cannot find a face or answer a description until ~1.9 GB has been located, and the non-commercial InsightFace terms are named in the manifest and shown nowhere. This is the largest gap between what the PRPs promise and what the executable does. |

---

## 7. Conventions

Follows the repo's `.editorconfig` and Microsoft C# naming. One public type per
file. Domain references nothing; Application references only Domain;
Infrastructure implements Application's ports; the WPF project wires them at
startup and nowhere else.

Migrations are always created with `dotnet ef migrations add` — never by hand.

Comments explain *why*, not what. A comment that restates the code is noise; one
that records a measurement or a rejected alternative is worth keeping.

### Two traps already hit

- **`UseWPF` removes `System.IO` from implicit usings** (its `Path` would clash
  with `System.Windows.Shapes.Path`). Restored in `Directory.Build.targets` —
  not `.props`, because the SDK's removal runs after props is imported.
- **The stock `.gitignore` rule `*.app`** matches the `PhotoGallery.App`
  *directory*, silently excluding the entire UI. Re-included explicitly.
