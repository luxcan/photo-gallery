# 06 — Faces and people

**Status: ✅ Done**

The reason the app exists: "show me every photo of one person."

---

## Goal

Find faces, group them into people, let each person be named once, then search
by name.

## Depends on

[03 — Thumbnails](03-thumbnails.md) — detection runs on the 1024px previews, so
no original is read again.

[09 — Models](09-models.md) — how these weights arrive, are proved intact and
are accounted for. The sizes below are this feature's facts; the flow is not.

---

## The hard part

**A face model will not recognise a newborn and a ten-year-old as the same
person.**

Embeddings encode adult bone structure. Across childhood a face changes more
than most adults differ from each other, so one stored vector per child fails
badly at the edges, and blind clustering over twelve years produces a scatter of
small unrelated piles rather than two children.

### Why this library can solve it

The folder names are dated *and* named:

```
20200214_Ana Lim Born
20201014_Ana Lim 8th Month
20250214 - Ana Lim 5th Birthday
```

So the app never has to guess. It takes faces from folders that already name a
child, derives the age from the folder date, and stores **one centroid per
person per era** rather than one per person. A candidate face is matched against
the era that fits the photo's own date.

50 of 209 folders name one child. Variants to fold together: `AL`, `AnaLim`, and
a typo'd `Ana Lin`.

**The honest catch:** those folders also contain parents, grandparents and
friends. So the app *proposes* — it shows the recurring face it believes is the
child at each era, and the user confirms or rejects. Roughly ten minutes, once.
Everything after that is automatic, including photos added next year.

There are two children in this library, which is what makes the sibling case
further down the hard one.

### Where that plan broke, measured

Folder names carry the children for two years and then stop. Counted in years of
life rather than calendar years, because what matters is how long the naming
habit lasts after a child arrives, not when they arrived:

| Child | Photos in folders naming them, by year of life |
|---|---|
| Ana Lim | 0: 847 · 1: 726 · 2: 228 · 3: 19 · 4: 8 · 5: 3 · 6: 24 · 8: 5 |
| Ana Reyes | 0: 271 · 2: 22 · 3: 10 · 4: 11 · 5: 5 |

97% of Ana Lim's named-folder photos fall in her first three years, while the
eight years after them hold **6,598 photos with no naming help at all**. Seeding
an era per child from the folder names bootstraps the earliest one and then runs
dry exactly where the library is thickest — each step of a walk forward needs
something to confirm *against*, and past the third year there is nothing.

**So the app groups instead.** Faces are gathered by resemblance in capture-date
order, the recurring groups are offered biggest first, and the user names each
one once. Where an existing person's era matches, their name is offered with it,
so the second group of a child is a single click. Folder names are still used —
but only to show which occasions a group came from, which is how a baby is
recognised, and never to guess a name.

That covers every year, and it registers grandparents and friends too, not only
the two children.

## Nothing here assumes a filing convention

Time comes from each photograph's own EXIF date: 9,882 of 11,482 carry one. For
the rest the folder's date prefix serves (1,544 of the remaining 1,600), and
failing both, the file's own modified time. **Folder names never decide who
anyone is.** A library whose folders are all `New Folder (3)` groups exactly as
well; it simply gets no occasion names beside the crops.

Eras are derived, not declared. A person's confirmed faces are walked in date
order and a new era is cut where their appearance actually drifts — so an adult
ends with one era covering a decade and a child gets several. No calendar
boundary appears anywhere.

---

## Measured

Explored in Python, then measured again from the built C# pass on the real
library.

| | |
|---|---|
| Default InsightFace config | 2,330 ms/photo → 7.4 hours ❌ |
| Detection + recognition only, Python | 676 ms/photo |
| 480px detection | 657 ms but loses 3 of 42 faces — rejected |
| **C# port, one photo at a time** | **595 ms/photo** |
| **C# port, 12 at a time (22 cores)** | **106 ms/photo → ~20 min for 11,237 previews** ✅ |
| GPU required? | **No.** The RTX 4070 is never touched |
| Faces found | ~2.2 per photograph that has any |

The default configuration loads landmark and gender/age models this app never
uses. **The port loads only the detection and recognition graphs** — that is a
3.4× difference for an identical result.

> **The old "~90 min" figure was wrong twice over.** It disagreed with this
> document's own 676 ms — which implies 2.2 hours, not 90 minutes — and it
> assumed one photograph at a time. The work is processor-bound and each graph
> is held to a single thread, so parallelism is the whole lever: measured 595,
> 188, 144 and 106 ms/photo at one, four, eight and twelve at once.

Each session is deliberately kept to one thread and the pass owns the
parallelism, defaulting to half the machine's cores. The two would otherwise
compete with each other.

---

## The riskiest code in the project

ONNX Runtime will happily run `det_10g.onnx` and `w600k_r50.onnx` from C#. What
it will not do is the surrounding arithmetic: SCRFD anchor decoding and
non-maximum suppression, then a five-point similarity transform to align each
face to a 112×112 crop before recognition sees it.

**Get the alignment slightly wrong and nothing throws.** The embeddings look
perfectly reasonable and match the wrong people — the worst kind of bug, because
it reads as the model being imperfect rather than the code being broken.

### What was actually done

The parity comparison **was** run during the port, against the Python reference,
over 20 real previews spanning both orientations and a five-person group shot:

| | |
|---|---|
| Reference faces | 41, of which 32 clear the 32px floor |
| Matched by the C# port | **32 of 32** |
| Cosine similarity | mean **0.9990**, lowest 0.9882 |
| Box corner error | mean 0.48 px, worst 1.58 px |

A wrong alignment scores in the 0.3–0.7 range, so this settles it. The residual
is resampling, not geometry — the lowest scores are on the *largest* faces,
which are the ones scaled down hardest to reach the detector's input.

**That comparison is not in the test suite**, by the owner's decision: it would
mean a committed fixture and a Python environment to regenerate it. What guards
the code instead:

- the similarity transform is closed form and tested against hand-computed
  cases — identity, pure scale, pure translation, three rotations;
- the warp is proved by cutting a crop whose transform is the identity and
  requiring the pixels back unchanged;
- the resampler's half-pixel convention is pinned by an exact expected result;
- embeddings are asserted unit-length and deterministic.

> **The residual risk, named.** None of those would catch an alignment that is
> subtly wrong in a way that is internally consistent. If people ever start
> matching wrongly, restore the reference comparison before looking anywhere
> else.

---

## Searching

The Library's search box searches **names**, and says so. Clicking into it offers
everyone who has been named, biggest first; typing narrows, a name that starts
with what was typed beats one that merely contains it, and Enter takes the top
match. Clearing the box shows everyone again, as does **Show everyone** beside
the count — a filter the user cannot see is a bug report waiting to happen.

It answers on every keystroke, so it never reads an embedding to do it: names and
a distinct-picture count come from two flat queries over a handful of rows.
Typing a name nobody has says so, rather than looking like a box that does not
work.

Searching by what is *in* a picture is [07](07-content-search.md) and needs
another 600 MB of weights. Searching by *who* is in it needs nothing that has not
already been worked out.

## Contracts

```
Face           AssetId, Bounds, DetectScore, Embedding (512 float32)
Person         DisplayName, Eras
PersonEra      FromUtc, ToUtc, Centroid, SampleCount; Covers(date)
FaceAssignment FaceId, PersonId, Source (Proposed|Confirmed|Rejected), Score
FaceEmbedding  SimilarityTo — L2-normalised, so cosine is a dot product
```

Only **Confirmed** assignments feed a centroid. **Rejected** ones are kept so the
same wrong proposal is not made twice.

All embeddings together are ~37 MB: they load into memory and ranking is one
matrix multiply. That is why there is no vector database.

---

## Models

**Not bundled yet.** The verify-and-import half of [09](09-models.md) is built:
`models/` in the working folder is the one place weights are read from, each is
checked against its size and SHA-256 before it is opened, and a feature whose
model is missing offers **Use model files I have...** rather than being a dead
end. Carrying the 182 MB inside the executable is a later build-flag change and
nothing in the code has to move for it.

| | Size |
|---|---|
| `det_10g.onnx` detection | 17 MB |
| `w600k_r50.onnx` recognition | 166 MB |
| **Bundled** | **182 MB** |
| *(full buffalo_l pack, for comparison)* | *326 MB* |

Dropping the three unused models saves 143 MB **and** gives the 3.4× speedup —
one decision, two wins.

> **Licence:** InsightFace's library is MIT, but its pretrained weights are
> non-commercial research only, and no licence file ships with them. Fine for
> personal use; a blocker if this is ever sold. They sit behind `IFaceEmbedder`
> so swapping to permissive weights stays a configuration change.

---

## Acceptance

- [x] The port was compared against the Python reference before it was trusted —
      32 of 32 faces, mean cosine 0.9990. Kept as a record here rather than as a
      test; see the risk above.
- [x] Only detection and recognition graphs are loaded.
- [x] A full pass over the library completes in about 20 minutes on CPU.
- [x] Groups are offered biggest first and named once each; an existing person's
      name is offered where their era matches, so later groups are one click.
- [x] Eras are derived from confirmed faces rather than from the calendar.
- [x] Only confirmed faces feed a centroid; rejections are kept so the same wrong
      proposal is not made twice.
- [x] Searching a name returns photos across every folder, including those whose
      names mention nobody.
- [x] A photograph containing no faces is recorded as examined, so eleven
      thousand previews are not read again on every pass.
- [x] New photos are matched automatically after a later scan.

---

## Out of scope

Recognising faces in video ([08](08-video.md)). Age or expression. Anything
leaving the machine.
