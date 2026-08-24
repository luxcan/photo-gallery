# 03 — Thumbnails

**Status: ✅ Done — driven from the UI as "Prepare pictures"**

The one pass that must read every original. Everything visual depends on it.

---

## Goal

Read each original exactly once, and take from it everything that read can
give: a tile for the grid, a preview for viewing, the pixel dimensions, the
capture date, the perceptual hash, and the content hash.

## Depends on

[02 — Scanning](02-scanning.md)

---

## The idea

Decoding is the expensive part. Once the pixels are in memory, a second size is
nearly free — so both renditions come from **one decode**. Otherwise face
detection would later force a second hour-long pass over the originals.

The same argument extends past the two images. Anything obtainable from that one
read is taken while it is in hand, because the alternative is always another
hour:

| Taken from the one read | Because otherwise |
|---|---|
| Tile, 400px | the grid has nothing to draw |
| Preview, 1024px | the photo view and [06](06-faces.md) both need it |
| Width and height | they are free from the header |
| `TakenUtc`, EXIF `DateTimeOriginal` | the gallery sorts by it, and nothing else in the app ever opens the file |
| `PerceptualHash` | [05](05-duplicates.md) needs it, and it comes off pixels already decoded |
| `ContentHash` | [05](05-duplicates.md)'s exact-duplicate answer, and the renditions are named after it |

> **The rule that follows:** if a pass has the bytes, it takes everything the
> bytes can tell it. Leaving a field for later means reading 25 GB again.

### Sizes — measured over 200 photos of the real library

| Rendition | Edge | Quality | Each | Total (11,481) | For |
|---|---|---|---|---|---|
| Tile | 400px | 78 | 21 KB | **0.23 GB** | the gallery grid |
| Preview | 1024px | 82 | 127 KB | **1.39 GB** | one-photo view, face detection |

400px keeps a 200px grid cell crisp at 200% Windows scaling — as large as a tile
gets before the view should be showing the preview instead. Dropping to 320px
would save 0.07 GB and go soft on a high-DPI screen.

> The tile is not what fills the disk. The preview is, by six times.

---

## Behaviour

Driven from **Library → Prepare pictures**. Deliberately a button rather than
something a scan triggers: it is about two hours of reading over the share, and
the app's rule is to say what is about to happen before the click.

- Runs over photos whose **tile is not on disk**, so a stopped pass resumes
  rather than restarting.
- **Parallel reads** — a network share is latency-bound, and one file at a time
  leaves the link mostly idle. Eight at once.
- Progress and a **Stop**; a corrupt or undecodable file is skipped and counted,
  never fatal.
- Results are written every 20 photos, so an interrupted pass keeps what it
  finished and the gallery fills in as it goes rather than at the end.
- Writes to SQLite happen on one thread — it tolerates concurrent readers, not
  concurrent writers.

### The row is a claim; the disk is the truth

What a photo needs is decided by whether its tile exists, not by whether its row
names one. The two disagree more often than they sound like they would: a
working folder can be copied, cleaned or synced without its index.

That is not hypothetical. The developer's own library held **11,481 rows naming
tiles that had all been deleted**. A pass filtering on the column found one photo
to do instead of eleven thousand, and the gallery built on it would have shown
11,481 placeholders.

### Naming, and why not the row id

Renditions are named after the **content hash** of the original — the first 32
hex characters — not the asset id.

An id is a database detail, not a property of the picture. Detaching a source
cascade-deletes its rows, so re-adding the folder renumbers everything and every
previously written file becomes an orphan: 1.6 GB of unreferenced JPEG per
re-add. A content hash produces the same name for the same picture every time,
so a re-run overwrites rather than accumulates, and two byte-identical photos
share one pair of files.

It also fixes the sharding. Ids are sequential and were formatted as eight hex
digits, so every id below 16,777,216 began `00` — all 32,450 files of this
library landed in `thumbs\00\`, the exact thing the shard exists to prevent. A
hash spreads evenly over 256.

> Naming by content does **not** save the re-read on a re-add: knowing a file's
> content hash means reading it. What it saves is the orphans.

### Decoding

Windows' own imaging codecs, via `System.Windows.Media.Imaging`. No native
imaging dependency.

- `DecodePixelWidth` makes the codec produce the reduced image directly — for
  JPEG a scaled DCT decode, so a 4000px original costs a fraction of a full one.
  This single setting is minutes versus hours.
- The file is read into memory in one go, then decoded from there: decoding
  straight off a share issues many small reads and is far slower.
- EXIF orientation is applied, so photos are stored upright.
- **HEIC decodes natively — verified** against a real file on the share. The
  test skips rather than fails where the codec is absent, since that is an
  environment fact.

---

## Contracts

```
IThumbnailGenerator   GenerateAsync(originalPath, ct) → GeneratedThumbnail?
GeneratedThumbnail    Tile, Preview, SourceWidth, SourceHeight,
                      TakenUtc, PerceptualHash, ContentHash
ThumbnailUpdate       AssetId, ThumbnailName, Width, Height,
                      PerceptualHash, TakenUtc, ContentHash
IThumbnailStore       SaveAsync(thumbnail), ImportAsync, Delete,
                      ResolveTilePath, ResolvePreviewPath, Exists
IGalleryReader        GetThumbnailCandidatesAsync
PerceptualHash        FromGreyscale(pixels, width, height)   (pure)
BuildThumbnailsHandler(parallelism, IProgress<ThumbnailProgress>, ct)
```

Stored as `thumbs/a3/a3f1c2d4….jpg` (tile) and `a3f1c2d4…-p.jpg` (preview),
sharded two characters deep because a directory of tens of thousands of files is
slow on Windows and unpleasant to inspect.

`PerceptualHash.FromGreyscale` is pure domain logic with no I/O — it box-averages
any greyscale image to 9×8 and records whether each pixel is brighter than its
right-hand neighbour. Reducing before comparing is what makes the hash
independent of resolution, which is the whole point: a 4000px original and its
400px copy are the same picture and must hash alike.

Dimensions are recorded **after** EXIF rotation is applied. Reading the sensor's
raw numbers reported every portrait phone photo as landscape.

`null` from the generator means "could not decode" — a library of 20,000 files
will contain some that are corrupt, truncated, or in a format with no codec, and
one of those must not stop the pass.

---

## Acceptance

- [x] Both renditions from one decode; the original is never read twice.
- [x] Tile is materially smaller than the preview.
- [x] An undecodable or missing file returns null rather than throwing.
- [x] HEIC decodes, or the test reports the codec is absent.
- [x] Only photos whose tile is missing are processed, so the pass resumes.
- [x] A row naming a tile that is not on disk is redone, not skipped.
- [x] Cancellable, with progress, keeping everything already finished.
- [x] Driven from the UI, with a **Prepare pictures** action.
- [x] The capture date is read from EXIF where the photo carries one.
- [x] A perceptual hash is produced for every photo the pass completes, and the
      same picture at two sizes hashes alike.
- [x] Dimensions are stored as the picture is seen, not as the sensor recorded it.
- [x] Renditions spread across the shards instead of filling one directory.
- [x] Detaching a source reclaims the cached copies nothing else is using.

---

## The import that was removed

A `--import-thumbnails` command once adopted a cache built during exploration,
so that 11,481 previews did not have to be produced again. It has been deleted,
for three reasons that arrived together:

- the cache and its manifest no longer exist on disk;
- it wrote only previews, and readiness is judged by the **tile**, so everything
  it imported was redone on the next pass anyway;
- it named files after the row id while the generator names them after content,
  so the two disagreed about where a rendition lives.

**The app stands on its own generator**, which was always the intent.

---

## Out of scope

Video thumbnails ([08](08-video.md)). Regenerating on quality change. Cleaning
up orphaned files.
