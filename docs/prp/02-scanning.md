# 02 — Scanning

**Status: ✅ Done**

Find out what exists, as cheaply as possible.

---

## Goal

Index which media files each source holds, how big they are, and when they
changed — without opening a single one.

## Depends on

[01 — Photo sources](01-photo-sources.md)

---

## The idea

Splitting "what exists" from "what it looks like" is the whole design.

Reading the bytes of 11,481 photos costs about an hour over a 6.4 MB/s link.
Reading the *directory metadata* for all 17,023 files costs 45 seconds. So the
scan does only the cheap half, and every expensive pass ([03](03-thumbnails.md),
[06](06-faces.md)) works from the rows it produces.

The library becomes browsable long before the expensive work finishes.

---

## Behaviour

- **Per folder** — a `Scan` on each table row, so one changed folder need not
  cost a walk of every other.
- **Scan all folders** — runs them in turn.
- **Stop** — cancels; progress and results so far are kept.
- Progress in the status line, a summary per source in the output panel.

### Measured on the real library

| | |
|---|---|
| First scan | 16,225 files in **44.7 s** |
| Re-scan, nothing changed | **39.7 s**, 0 written, 16,225 unchanged |

---

## How it stays cheap

| Technique | Why |
|---|---|
| Never opens a file | Metadata only. This is the whole point. |
| Skips unchanged files | Size + timestamp (±2 s, since some shares round them) match ⇒ untouched. |
| One query, not 17,000 | Existing rows load as a single projected dictionary; the walk decides in memory. |
| Batched writes of 500 | Change tracker cleared each batch, so a long scan does not balloon. |
| Extension filter before I/O | Plus `Thumbs.db`, `desktop.ini`, `.DS_Store` by name. |
| Explicit stack, not `AllDirectories` | That overload aborts the entire enumeration on the first unreadable folder — a certainty on a NAS. |
| Skips `System Volume Information`, `$RECYCLE.BIN`, `@eaDir` | Plus everything `IWorkingFolder.IsAppOwned`. |

---

## Rules

- A file is **changed** when size or timestamp differ. Its derived data
  (thumbnail, hashes) is cleared so later passes redo it.
- A file is **gone** only when a *completed* walk did not see it. A cancelled
  scan removes nothing — it has not proved anything missing.
- `LastScanUtc` is recorded only on completion.

---

## Contracts

```
IMediaFileWalker      Walk(root, ct) → IEnumerable<ScannedFile>   (streaming)
IAssetRepository      GetSignaturesAsync, AddRange, UpdateRange, Remove, counts
AssetSignature        AssetId, Length, ModifiedUtc; Matches(length, modified)
MediaFileTypes        Classify(fileName) → Photo | Video | Unknown
ScanPhotoSourceHandler(sourceId, IProgress<ScanProgress>, ct) → ScanResult
```

Classification is **extension-only** on purpose: sniffing contents would turn a
45-second pass into an hours-long one. Anything misclassified is caught later,
when the file is actually decoded.

---

## Acceptance

- [x] Photos and videos indexed; other files ignored.
- [x] A second scan of an untouched source writes nothing.
- [x] An edited file is noticed and its derived data cleared.
- [x] A deleted file's row is removed.
- [x] A cancelled scan removes nothing.
- [x] The app's own folders are never indexed, even when the working folder is
      itself a source.
- [x] One unreadable folder does not abort the scan.

---

## Out of scope

EXIF extraction (dates, GPS) — belongs with the pass that already opens the
file. Watching for changes. Scheduling.
