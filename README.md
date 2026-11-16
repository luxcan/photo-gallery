# Photo Gallery

A Windows desktop app that makes a large personal photo and video collection
searchable: by person, by place, by date, by folder, and by what is in the
picture. It is built for one person's library on their own PC, with the photos
left where they already are (this PC, a USB drive, a network share).

Nothing is uploaded anywhere. Every pass - previews, faces, places, search -
runs on the PC, and the originals are never modified or moved unless you ask.

## What it does

- **Library** - one wall of every photo and video, with filter chips (photos,
  videos, not in an album, nobody named) and a search box for names, places
  and descriptions.
- **People** - finds faces, suggests who they are, and learns as you confirm
  or correct. Every photo of one person is one click away.
- **Places** - reads GPS coordinates and names them from a gazetteer built
  into the app; no online lookup.
- **Search by description** - "birthday cake", "beach at sunset" - matched
  against what the picture shows, not its file name.
- **Albums** - suggests trips and periods from dates and places; keep a
  suggestion, refuse it, or make your own. Collections shelve albums together.
- **Duplicates** - finds exact and near-identical copies, suggests the best of
  a burst, and sets the rest aside in a quarantine folder rather than deleting
  them.
- **Sync** - several laptops in one house share their *decisions* (who is in a
  picture, what an album is called, which previews exist) through one shared
  folder. Each machine keeps its own library; no database is ever opened by two
  machines.
- **Updates** - the app checks this repository's releases and offers a newer
  version when one exists.

## Getting started

1. Download `PhotoGallery.exe` from the latest release. It is a single
   self-contained file: no installer and no .NET runtime needed.
2. Run it and choose a **working folder**. That is where the app keeps its
   index, previews and logs. A folder on the PC's own disk is best.
3. Add one or more **photo sources**, the folders your pictures live in, and
   let the first scan run. The first scan reads every file once; later scans
   only look at what changed.

### Optional features: faces and search by description

These need model files that are not bundled, because of their licences. Get
them yourself, then open **Settings → Optional features** and point the app at
the folder holding them. The app matches the files by size and checks each one
by its SHA-256 digest, so their names do not matter.

| Feature | Files | Size | Licence |
|---|---|---|---|
| Faces and people | InsightFace `buffalo_l`: `det_10g.onnx`, `w600k_r50.onnx`, `2d106det.onnx` | ~196 MB | Non-commercial research use only |
| Search by description | OpenAI CLIP ViT-L/14 ONNX export (visual, textual, vocabulary, merges) | ~1.7 GB | CLIP is MIT; the ONNX export is the Immich project's and states no licence of its own |

Places need no download.

## Building from source

Requirements: Windows, the .NET 10 SDK.

```powershell
dotnet build
dotnet test
```

To install a release build for yourself (a standalone exe under
`%LOCALAPPDATA%\Programs\PhotoGallery` and a Start-menu shortcut), close the app
and run:

```powershell
.\publish.ps1
```

`.\publish.ps1 -Release` also publishes the build as a GitHub release, which is
what installed copies update from. It refuses uncommitted or unpushed work, and
a version that already has a release. The version is `<Version>` in
`Directory.Build.props`.

A debug run opens the same library as the installed app, because both read
`%LOCALAPPDATA%\PhotoGallery\config.json`. Set `PHOTOGALLERY_CONFIG_DIR` to a
throwaway folder to keep a debug run away from your real library.

## Project layout

```
src/
  PhotoGallery.Domain           the rules: assets, faces, people, albums, sync merge
  PhotoGallery.Application      use cases and the ports they need
  PhotoGallery.Infrastructure   SQLite (EF Core), ONNX Runtime, file system, imaging
  PhotoGallery.App              the WPF app, wired together at startup
tests/
  PhotoGallery.Tests            xUnit
tools/
  PhotoGallery.Measure          console harness that times the model passes
docs/
  PRP.md                        product requirements, constraints and settled decisions
  prp/                          one document per feature
```

Clean Architecture: Domain references nothing, Application references only
Domain, Infrastructure implements Application's ports. EF Core migrations are
always created with `dotnet ef migrations add`, never by hand.

Start with [`docs/PRP.md`](docs/PRP.md): it records the measured constraints the
design rests on (a slow network share, a NAS that can only store) and the
decisions already made, with their reasons.
