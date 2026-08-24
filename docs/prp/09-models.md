# 09 — Models

**Status: ✅ Done for install-it-yourself. Downloading on the user's behalf is
deliberately not built.**

What exists: `ModelDescriptor` with size, digest, version and licence;
`IModelStore` with `Describe`, `ResolvePath`, `StateOf` and `ImportAsync`;
`FileModelStore` reading `models/` in the working folder, verifying size and
SHA-256 before any path is handed out, deleting a file that fails, and importing
through `<name>.partial` so an interrupted copy leaves nothing usable.

Above that: `ModelFeature` and `FeatureModels` name which files each feature
cannot start without — one list, where the face pass and the content pass each
used to keep their own — and **Settings → Optional features** shows every file,
its size, its state and its licence, with the page it comes from and what to do
there. One folder picker installs both features, because `ImportModelsHandler`
matches candidates **by size and then proves them by digest** rather than by
name; upstream ships the content graphs as `visual/model.onnx` and
`textual/model.onnx`, and a renaming step before the first success is where
people give up.

Nothing is gated on a promise: **People is disabled until the face models verify
or the library already holds named people**, and the Library search box narrows
its own placeholder — "Place", then "Name or place", then all three — because
places come from a gazetteer compiled into the executable and never needed a
model at all.

What is not built, and now on purpose: bundling weights in the executable, and
downloading them on the user's behalf. Both were rejected for the same reason —
the face pack is licensed for non-commercial research use only and carries no
licence file, so this app can neither redistribute it nor fetch it silently.
Removal, and a version bump replacing a file in place, are still open.

> **This is now the app's largest gap, and it grew rather than waiting.** When
> this was written it blocked nothing shipped — faces imported their weights by
> hand and that was the only feature involved. [07](07-content-search.md) then
> shipped on the same foundation without even that much: `ImportFaceModelsAsync`
> is the only importer in the app and it names the face pair explicitly, so the
> **four content-search files have no route in but copying them into
> `<working folder>\models\` under exactly the manifest's names.** A fresh install
> has six models in `Missing` and ~1.9 GB between them. Neither half of the rule
> those two PRPs state — "faces bundled, content search fetched" — is true of the
> executable today.
>
> An importer that took a `ModelId` set rather than hard-coding two is the
> cheapest fix here and is most of what the fetched path needs anyway.
>
> The licence gap is the sharper one: `ModelDescriptor.Licence` is populated for
> all six models and **is never shown anywhere**. The InsightFace terms are still
> a thing the user has never been told.

Two features need machine-learning weights on disk — both of them now built. They
were meant to get them in opposite ways, and both need the same answer to "is this
file usable?".

---

## Goal

One way for a model to arrive, be proved intact, and be accounted for — whether
it ships inside the executable or is fetched afterwards.

## Depends on

[00 — Foundation](00-foundation.md) — the working folder owns `models/`.

**Built before [06](06-faces.md).** Its number is later than its turn.

---

## Why it is its own feature

[06](06-faces.md) bundles its weights; [07](07-content-search.md) fetches
600 MB on demand. Written separately, each would invent its own idea of where a
model lives, what proves it whole, and what to do when it is not — and the part
most likely to be silently wrong would exist twice.

There is also a decision here that is genuinely the user's, not plumbing:
InsightFace's pretrained weights are **non-commercial research only and ship
with no licence file**. Bundling them without ever saying so means the user
never learns the terms they are using them under.

---

## What a model is

A manifest entry, not a file in a folder:

```
Id          faces.detect
Version     1
FileName    det_10g.onnx
Bytes       17,000,000
Sha256      <digest>
Origin      Bundled | Fetched(url)
Licence     short name + the text to show once
```

The manifest is the claim. **The hash is the truth** — the same rule the
thumbnail store learned, where a row naming a rendition proved nothing about the
disk. A truncated `.onnx` does not announce itself: it fails deep inside ONNX
Runtime with a message that reads as the model being wrong rather than the file
being half there.

## The two origins

| | Bundled | Fetched |
|---|---|---|
| Used by | [06](06-faces.md) faces | [07](07-content-search.md) content search |
| Size | **182 MiB** (`det_10g` 16.1 + `w600k_r50` 166.3) | **~1.71 GB** (visual 1.13 GiB + text 472 MiB + tokenizer 1.4 MB) |
| Arrives | extracted from the exe on first **use** | downloaded on first **use**, once |
| Offline | always works | needs the network once, then never again |

Both size figures are measured from the manifest rather than estimated, and the
fetched one is **nearly three times** what this table first claimed — the guess was
ViT-B/32's and the export in use is ViT-L/14. Bundling it would take the
executable from ~380 MB to over two gigabytes to serve a feature not everyone
turns on. Fetching the bundled pair would break the promise that the app works
with no internet at all.

Neither column describes the executable today: both arrive by import. See the
status note above.

> **Extraction is on first use, not first run.** [06](06-faces.md) currently
> says first run. That spends 182 MB and a slow launch on a feature the user may
> never open, and a self-contained build already extracts itself once — this
> would be a second copy of the same bytes.

---

## The manifest

**Six entries, not four.** Digests are taken from the files actually in use — a
digest invented rather than measured would be a lie the verifier believes — and
all six are now real, in `ModelManifest.Default`:

| Id | File | Origin | Bytes | Licence |
|---|---|---|---|---|
| `FaceDetection` | `det_10g.onnx` | InsightFace `buffalo_l` | 16,923,827 | non-commercial research |
| `FaceRecognition` | `w600k_r50.onnx` | InsightFace `buffalo_l` | 174,383,860 | non-commercial research |
| `ContentVision` | `clip_vit_l14_visual.onnx` | Immich's ONNX export | 1,216,297,719 | MIT (OpenAI CLIP) |
| `ContentText` | `clip_vit_l14_textual.onnx` | Immich's ONNX export | 495,082,255 | MIT (OpenAI CLIP) |
| `ContentVocabulary` | `clip_vit_l14_vocab.json` | Immich's ONNX export | 862,328 | MIT (OpenAI CLIP) |
| `ContentMerges` | `clip_vit_l14_merges.txt` | Immich's ONNX export | 524,619 | MIT (OpenAI CLIP) |

The last two are not incidental files. A vocabulary that differs from the one the
text encoder was trained on does not fail — it returns a confident vector for the
wrong words, which is the same failure mode as a misaligned face crop, the one
this codebase has already been bitten by. They are useless without each other:
one training run wrote both and they describe one scheme between them.

**Which CLIP is now settled: ViT-L/14, as exported to ONNX by the Immich
project** — not the ViT-B/32 guessed here. That matters beyond the size, because a
different export of the "same" model produces different vectors and everything
already indexed would silently stop matching newly typed text. The licence string
records that Immich's repository declares no licence of its own, so the permission
comes from OpenAI upstream and the packaging is unstated — worth saying rather
than rounding to "MIT" and hoping.

**Still not settled: where from.** A fetched model needs a stable URL that will
still be there in a year. Pin the release, not a branch. This is the last thing
between the manifest and a working `EnsureAsync`.

**The gazetteer is deliberately not in this manifest.** [10](10-location.md)
planned to install its places data through here; at 2.8 MB compressed it is
compiled into the executable instead, so there is nothing to install and nothing a
user could have supplied the wrong version of. This manifest decides whether a
file *the user* supplied can be trusted.

> A model's **version** exists for exactly the vector problem above. Changing
> the export is a version bump, and a version bump invalidates everything
> indexed with the old one — so the manifest says so rather than leaving a
> library quietly returning worse answers.

## How bundled models are carried

182 MB inside a single-file publish is a build decision, not a detail.

- **Not `EmbeddedResource`.** It loads the bytes into the assembly and into
  memory to write them out, on a build that is already slow.
- **`<None>` with `CopyToOutputDirectory`**, carried into the single file by
  `IncludeAllContentForSelfExtract`, and copied to `models/` on first use. The
  runtime already extracts the bundle to a temp directory; this reads from there
  and writes one copy into the working folder, where the digest can be checked
  and where it survives the next release.

The working-folder copy is the one the app uses. That is what makes
"use a file I already have" and a damaged-file repair the same code path for
bundled and fetched models alike.

---

## Rules

- A model is used only after its size and digest match the manifest.
- A download writes to `<name>.partial` and is renamed **only** once the digest
  matches. That one rule makes "downloaded once" true and makes an interrupted
  download self-healing without needing range requests.
- A mismatch deletes the file and says so plainly. It never half-works.
- Nothing is fetched without the user asking. Enabling a feature states the size
  before the click, as every other long job in this app does.
- A model that is in use cannot be removed.
- `models/` is app-owned, so it is refused as a photo source and skipped by the
  crawl — [01](01-photo-sources.md) already covers this.

---

## Behaviour

**Settings → Models.** One row per model: what it is for, its state, its size,
and its licence.

| Model | For | State | |
|---|---|---|---|
| Face detection and recognition | Finding people | Installed · 182 MB | Remove |
| Content search | Finding things by description | Not installed · 606 MB | Install · Use a file I already have |

- **Install** shows the size, then runs as a normal pass: the overlay, a
  determinate bar from the reported length, and a **Stop** that leaves a
  `.partial` rather than a broken model.
- **Use a file I already have** browses to a file and verifies it against the
  manifest before copying it in. This is what serves a machine with no internet,
  a network that blocks the download, or someone who already has the weights.
  It costs almost nothing once the digest exists.
- **Licence** is shown once, before first use, and recorded as accepted.
- A feature whose model is missing says so where the feature is, and offers the
  install — never a dead end.

---

## Contracts

```
ModelId              faces.detect | faces.recognise | search.visual | search.text
ModelDescriptor      Id, Version, FileName, Bytes, Sha256, Origin, Licence
IModelStore          EnsureAsync(id, IProgress<ModelProgress>, ct) -> ModelState
                     ResolvePath(id)
                     Verify(id) -> bool
                     Remove(id) -> bool          (prove gone, as the thumbnail store does)
                     Import(id, sourcePath)      ("use a file I already have")
ModelState           Missing | Installing | Ready | Damaged
IWorkingFolder.ModelsPath   already exists
```

`EnsureAsync` is the whole consumer contract: a feature asks for a model and
either gets a usable path or a reason it cannot have one.

---

## Failure, named

| | |
|---|---|
| No network | Install says so and offers the file-you-already-have path. |
| Stopped part way | `.partial` stays; the next install resumes by starting over, and nothing broken was ever named. |
| Disk full | Reported before the copy, not after — the sizes are known. |
| Digest mismatch | File deleted, state Damaged, one clear line. |
| Working folder copied to another machine | Verified on use, so it either works or reports Damaged. |
| User deletes `models/` by hand | Same as never installed. |
| Version bump | A new version is a new file; the old one is removed only after the new one verifies. |

---

## Acceptance

- [x] A feature with no model offers the install where the feature is, and says
      so in the side nav before it is pressed.
- [x] A truncated or corrupt model is reported as damaged, not run — and is
      deleted, so the next start does not read it again to reach the same answer.
- [x] A model can be supplied from a local file with no network at all.
- [x] The licence is shown beside every feature, before the user is sent to
      fetch anything.
- [x] The app still starts, opens a library and browses with no models present.
      Verified on a library created from nothing: Library and Duplicates work,
      People is disabled and says why, and the search box still searches places.
- [x] Half a feature reads as half, not as nothing: four of five files installed
      names the file still missing rather than starting again.
- [ ] The download states its size before it starts — not applicable while the
      download is the user's own, but the size *to* download is stated.
- [ ] Removing a model in use is refused.
- [ ] A version bump is recognised and the old file is replaced, not shadowed.

---

## Open

Two of the three questions here closed themselves by being answered in code:

| | |
|---|---|
| The exact CLIP variant, precision and export | ✅ ViT-L/14, Immich's ONNX export |
| The digests, taken from the files actually shipped | ✅ all six measured |
| The URL each fetched model comes from, pinned to a release | ✅ both found and checked against the digests above |

**The sources, and the trap in one of them.** The face pack is
`deepinsight/insightface` release `v0.7`, asset `buffalo_l.zip` — a flat archive
of five files, of which this app uses two, under their upstream names. The
content models are `immich-app/ViT-L-14__openai` on Hugging Face, as
`visual/model.onnx`, `textual/model.onnx`, `textual/vocab.json` and
`textual/merges.txt`.

> **That repository is re-uploaded, and its history does not match.** The
> revision pinned in `FeatureCard` is `9b27c6b4`; at the July 2024 revision the
> same two graphs are a few hundred kilobytes different and fail verification
> here with nothing on screen to explain why. The link the app shows is pinned to
> the revision for that reason, and must stay pinned.

A separate `tokenizer.json` is **not** wanted, though that repository ships one.
CLIP's byte-level BPE is fully described by `vocab.json` plus `merges.txt`, and
`tokenizer.json` is the one tokenizer artefact whose bytes changed between
revisions.

Neither blocked faces, which is why [06](06-faces.md) was built first. Nothing
blocks the rest of this feature now except that one URL and the work itself.

---

## Out of scope

Choosing between models at runtime. GPU execution providers. Automatic updates —
a version bump is a release, not a background surprise. Re-indexing after a
version bump, which belongs to whichever feature owns the vectors.
