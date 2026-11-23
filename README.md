# Photo Gallery

It started with a simple question from my wife: "Can I see all the photos of
the kids, the way my phone does?" Android and iOS have done this for years,
but our family photos live on a NAS, not in the cloud. So I built it.

Photo Gallery is a Windows desktop app that makes a large photo and video
collection searchable by person, place, date, folder and what is in the
picture. The photos stay where they are: on the PC, a USB drive or a network
share.

Nothing is uploaded. Previews, faces, places and search all run on the PC,
and the originals are never changed or moved unless you ask.

## What it does

- **Library** - every photo and video on one wall, with filter chips (photos,
  videos, not in an album, no faces named) and one search box for names,
  places and descriptions.
- **People** - finds faces, suggests who they are and learns from your
  answers. See every photo of a person in one click, and add a year of birth
  to group them by age.
- **Places** - names each photo's GPS location from a gazetteer built into
  the app, with no online lookup.
- **Search by description** - "birthday cake", "beach at sunset": matched
  against what the picture shows, not its file name.
- **Albums** - suggests trips and periods from dates and places. Keep a
  suggestion, refuse it or make your own; collections group albums together.
- **Duplicates** - finds exact and near-identical copies and suggests which
  shot of a burst to keep, and why. The copies you do not keep go to the
  Recycle Bin, or are deleted permanently on a network share; the app tells
  you which before you confirm.
- **Sync** - laptops in one house stay in step through a shared folder.
  Names, albums, photo rotations and previews travel; the original photos
  never do. A new computer is ignored until you accept it, and changes are
  applied only when you press Sync. Each machine keeps its own library.
- **Updates** - checks this repository's releases and offers a newer version
  when there is one.

## Getting started

1. Download `PhotoGallery.exe` from the latest release. It is a single
   self-contained file: no installer, no .NET runtime.
2. Run it and choose a **working folder** for the app's index, previews and
   logs. A folder on the PC's own disk is best.
3. Add one or more **photo sources** (the folders your photos are in) and let
   the first scan run. It reads every file once; later scans only check what
   changed.

### Optional features: faces and search by description

These need model files that are not bundled, for licence reasons. In
**Settings → Search features**, press **Download** for a feature and save the
files into the folder shown under Step 1, or choose a folder that already has
them. The feature turns on once its files are there. Files are recognised by
size and verified by SHA-256, so their names do not matter.

| Feature | Files | Size | Licence |
|---|---|---|---|
| Faces and people | InsightFace `buffalo_l`: `det_10g.onnx`, `w600k_r50.onnx` | ~191 MB | Non-commercial research use only |
| Eyes open (best of a burst, needs Faces) | InsightFace `buffalo_l`: `2d106det.onnx` | ~5 MB | Non-commercial research use only |
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
