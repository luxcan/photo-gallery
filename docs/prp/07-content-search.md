# 07 — Content search

**Status: ✅ Done, and still optional — the library works with the feature off.
The one promise not kept here is the download, which belongs to
[09](09-models.md) and is still unbuilt: the weights arrive by hand today.**

Finding photos by what is in them — "book", "beach", "birthday cake" — with
nothing tagged.

---

## Goal

Type a description, get matching photos.

## Depends on

[03 — Thumbnails](03-thumbnails.md) — runs on the cached previews, costing no
network.

[09 — Models](09-models.md) — the download, its verification and the
"use a file I already have" path all belong there, not here.

---

## Why CLIP rather than object detection

| | Object detector (YOLO) | CLIP |
|---|---|---|
| Vocabulary | fixed ~80 classes | **anything typed** |
| "book" | ✅ (it is in COCO) | ✅ |
| "Ana Lim holding a book" | ❌ | ✅ |
| "birthday cake", "at the beach", "in the snow" | ❌ mostly | ✅ |

CLIP puts images and text in the **same** vector space, so a typed phrase becomes
a vector and the nearest image vectors are the answer. No tagging, no fixed label
list. It is how Immich's smart search works.

Image vectors are small: **ViT-L/14**, so 768 floats a picture — 3 KB each,
11,481 × 768 ≈ **35 MB**. They live in a table of their own rather than on
`Asset`, because the duplicate pass materialises every photo row in the library
and hanging 35 MB off the asset would put all of it behind any query that wanted
a file's length. The faces feature learned that the expensive way.

---

## Shipping: fetched, not bundled

The sizes first guessed here were ViT-B/32's. **The export actually used is
ViT-L/14, and it is nearly three times bigger** — measured from the files in the
manifest on 16 August 2026:

| | Size |
|---|---|
| CLIP visual encoder | 1,216,297,719 B (**1.13 GiB**) |
| CLIP text encoder | 495,082,255 B (472 MiB) |
| Vocabulary and merges | 1.4 MB together |
| **Total** | **~1.71 GB** |

Bundling that would take the executable from ~380 MB to over two gigabytes, to
serve a feature not everyone turns on. The original argument holds; it is simply
nearly three times stronger than when it was written.

> **The rule: face search is bundled, content search is fetched on demand.**

The app ships complete for what it was built to do and works with no internet at
all.

**Neither half of that rule is built yet, and this feature is the worse off of
the two.** Nothing is bundled and nothing is fetched. Faces at least have
**Use model files I have…**; these four files have **no import path at all** and
must be copied into `<working folder>\models\` by hand, under exactly the names
the manifest gives. Both halves belong to [09](09-models.md).

The vocabulary and the merges are manifest entries in their own right, not
incidental files. A tokenizer that differs from the one the text encoder was
trained on does not fail: it returns a confident vector for the wrong words.

---

## Behaviour

- Off until enabled; enabling explains the download and its size
  ([09](09-models.md)). **Not as built** — there is no enabling step and no
  download. The feature is simply on when the four files are in `models\` and
  silently absent when they are not.
- Indexing runs over the previews, resumable like every other pass. It reports
  how much longer it has to run: an hour-long bar with no end in sight is what
  makes somebody stop a pass that was nearly finished.
- **It had a button of its own, and no longer does.** It is a phase of a scan,
  named on the overlay as it runs. Two buttons that between them decided whether
  the library was finished made using this app a procedure to remember — scan,
  then find faces, then find where the photos were taken — with a half-made
  library as the price of forgetting a step. Being expensive is the app's own
  business; the answer to it is to name the phase and let it be stopped.
- Results blend with the existing search box rather than adding a second one.
  Names are still offered as they are typed, because that is one small query over
  a handful of rows; **descriptions are answered on Enter only**, because that runs
  a text encoder against every picture in the library and doing it per keystroke
  would make the box unusable. What the app understood is shown beside the count —
  "Ana Lim · beach" against a line typed as one — so a wrong split is something the
  user can see and correct rather than a mysteriously empty grid.

---

## Acceptance

- [x] Typing a description finds the photographs, untagged. Checked against this
      library before anything was built on top: **"a birthday cake" returns a
      birthday cake, "a book" an open book, "a photo of a beach" a child on sand
      with the sea behind.** The tokenizer is checked against the documented output
      of CLIP's own — "a diagram" is 320 then 22697 — and the preprocessing comes
      from the model's own `preprocess_cfg.json` rather than being guessed.
- [ ] **The download is explicit, sized, and once only.** Not built, and there is
      not even the manual path faces have: [09](09-models.md) never grew
      `EnsureAsync`, and the only importer in the app is
      `ImportFaceModelsAsync`, which handles the face pair and nothing else. The
      1.71 GB is placed in `models\` by hand. This is the one acceptance criterion
      this feature ships without, and it is not this PRP's to close.
- [x] A missing model is not a crash: a typed description falls back to the name
      match the box could always do, and says why.
- [x] Everything still works offline with the feature off — and the suite proves it
      by running that way: **554 pass and the ten tests that need the weights skip
      cleanly** on a machine where `models\` is empty.
- [x] Indexing is resumable and cancellable. **The row's existence is the marker**,
      which faces could not manage: a photograph with no faces is
      indistinguishable from one never examined, but every picture has exactly one
      answer to what it is of. Batched, read by several threads, written by one,
      with the write in a `finally`. Measured **301 ms a photograph at parallelism
      11** — one pass over 11,228 previews is about 56 minutes.
- [x] A person filter is applied **before** the ranking is cut down, not after.
      The best three hundred beaches in twelve years may contain none of her, and
      the search would then answer "no pictures" while holding several. There is a
      test for exactly that.
- [x] An undescribed library says so rather than answering "no matches", and the
      message names what fixes it. *(It named the button until describing became
      a phase of a scan; it now points at scanning the folders.)*

---

## Out of scope

Training or fine-tuning. Sending anything off the machine. Ranking against
face results in one list, at first.
