# 01 — Photo sources

**Status: ✅ Done**

Where the photos are. A library is folders from anywhere, not one location.

---

## Goal

Add, list and detach the folders a library indexes.

## Depends on

[00 — Foundation](00-foundation.md)

---

## Behaviour

A single screen: heading, explanation, an **add row above the table**, then the
table.

### Add row

`Folder location` label, path box, **Browse...**, **Add**.

Browse only *fills* the box; **Add** commits. That separation exists because the
Windows folder picker mishandles typed UNC paths — the user must be able to
correct it before anything is saved.

Accepts anything fully qualified: `D:\Photos`, `\\server\share`.

### Table

| Folder | Files | Last scanned | |
|---|---|---|---|
| `\\nas\PhotoGallery` | 16,225 | 14 Aug 2026, 18:51 | Scan · Detach |

- One real `ListView`/`GridView`, so header and rows share column definitions.
- `Files` is `—` until scanned, never `0` — nothing counted is not the same as
  counted zero.
- Per-row **Scan** ([02](02-scanning.md)) and **Detach**.
- Counts are loaded on open, so reopening shows the real numbers rather than
  zeros.

### Detach

Removes the source and its indexed rows. **The files themselves are never
touched** — the UI says so.

---

## Rules

A folder is rejected when it:

- is already a source (case-insensitively);
- overlaps an existing source in either direction — nesting would index the same
  files twice;
- is one the app owns: `thumbs`, `models`, `quarantine`, `logs`.

The **working folder root itself is allowed**, because set-up may legitimately
point at a folder that already holds pictures. Only the app's own subfolders are
out, and scanning skips them by the same rule.

---

## Contracts

```
PhotoSource                 Id, Path, AddedUtc, LastScanUtc
Asset.PhotoSourceId         FK; (PhotoSourceId, RelativePath) unique
AddPhotoSourceHandler       validates and persists
RemovePhotoSourceHandler    detaches; cascade removes its assets
IWorkingFolder.IsAppOwned   the skip rule, shared with scanning
```

Paths are stored **relative to their source**, so a share that moves costs one
edit rather than 17,000.

---

## Acceptance

- [x] Several sources of different kinds coexist.
- [x] A typed UNC path is accepted (the picker's failure does not block it).
- [x] Duplicate, overlapping and app-owned folders are refused with a readable
      reason.
- [x] The working folder root is accepted as a source.
- [x] Detach leaves the files alone.
- [x] Counts survive a restart.

---

## Out of scope

Watching folders for changes. Per-source include/exclude rules. Reordering.
