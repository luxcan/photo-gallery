# 14 — Best of a burst: suggesting the copy worth keeping

**Status: 📝 Proposed — not started. Phase 1 (measure, change nothing) must be
signed off before phase 2 (suggest).**

"Looks the same" ([05](05-duplicates.md)) holds two kinds of set that a
perceptual hash cannot tell apart: a photograph and a re-saved copy of it, and
the next frame of a burst. Today's rule (more pixels, then more bytes, then the
folder rule) is reasonable for the first and a coin toss for the second. Every
frame of a burst is an original at the same size, so the suggestion falls
through to the folder rule and then to path order, and the app is as likely to
suggest the blurred frame, or the one where somebody blinked, as the good one.

The folder rule is also being removed. Which folder a copy sits in says nothing
about the picture, and a suggestion it decides is a guess presented as a
judgement.

---

## Goal

In a "Looks the same" set, suggest the copy a person would keep: the one where
the people who matter have their eyes open, that is sharp, that is well exposed,
and that is the original rather than a re-saved copy. Say on the card why it was
suggested. When nothing separates the copies, suggest nothing. The person still
chooses; nothing is kept or removed without them.

## Depends on

- [05 — Duplicates](05-duplicates.md): the sets, `KeeperPolicy`, and the review
  screen this changes.
- [06 — Faces](06-faces.md): the faces are already found, on the cached
  previews, with a box and a score each, and named people are matched to them.
- [09 — Models](09-models.md): one more weights file, proved by size and digest
  before use.

---

## The ladder

Copies are compared one criterion at a time, in this order. **A criterion only
speaks when the difference is clear**: a frame 2% sharper than another is a tie,
not a win, and the comparison moves to the next criterion. Each "clear" margin is
set from the phase 1 measurement and recorded in this file, never guessed.

| # | Criterion | Better copy | Clear when |
|---|---|---|---|
| 1 | Known people's eyes | fewer known faces with eyes Closed | the counts differ at all |
| 2 | Sharpness | the sharper picture | the difference exceeds the margin found in phase 1 |
| 3 | Exposure | less of the picture blown white or crushed black | the difference exceeds the margin found in phase 1 |
| 4 | Original over re-saved | the copy that still carries the camera's details | one has them and the other does not |
| 5 | Pixels | more pixels | they differ |
| 6 | Bytes | more bytes | they differ |

**The blur guard comes before the ladder.** A copy that is *plainly* blurred,
beyond a hard limit found in phase 1 (well past the sharpness margin), cannot be
suggested while the set holds a copy that is not. Without it, a badly shaken
frame with every eye open would beat a crisp frame with one blink, and neither
is a photograph anybody wants suggested. If every copy is plainly blurred, the
guard removes nothing and the ladder decides as usual.

**There is no seventh criterion.** If all six tie, no copy is suggested, and the
card says so. The folder rule (named folder, shallower path, path order) is
removed entirely.

### 1. Known people's eyes

Only **known people** count: a face confirmed as a named person, or matched by
the app to a named person (`AssignmentSource.Confirmed` or `Proposed`). A
stranger in the background, a face nobody has named, a rejected or unnamed
match, and a face marked **Ignore** never count. In a burst, the same child in
the next frame is nearly always matched correctly, which is why matched faces
count and not only confirmed ones.

For each known face, crop it from the cached preview, run a 106-point landmark
model over it, and measure how open each eye is: the eye aspect ratio, the gap
between the upper and lower lid divided by the eye's width. Each face gets
**Open**, **Closed** or **Unknown**. A copy's score is how many of its known
faces are Closed. No face is matched across copies; counting is enough.

A known face is still **Unknown**, and left out of the count, when it is turned
well away from the camera (read from the detector's five points: the eyes'
spread against the nose), when the landmark model is unsure of it, or when one
eye is hidden. Sunglasses, a hand over the face, a profile: Unknown, never
Closed. Being wrong towards "closed" is what would make the app suggest a worse
photograph, so every doubt lands on Unknown.

### 2. Sharpness

Measured on the cached preview, with no model: the variance of the Laplacian (a
standard measure of how much fine edge detail survives). Where the copy has
known faces, it is measured over their boxes, because a sharp background behind
a blurred child is not a sharp photograph.

Without known faces (scenery, or strangers in front of a view) it is measured
over the **most detailed quarter of the picture**: the preview is cut into a
grid of tiles and only the top quarter by detail is scored. A frame that is two
thirds sky would otherwise average the sky's flat zero into the score, and a
shake on the ridge line would hardly move it. The preview is smoothed very
slightly first, so that sensor grain at dusk or indoors, which also reads as
fine detail, does not make a noisier frame look sharper.

The comparison is a **ratio**, not a difference: one frame must be clearly
sharper than the other, by a margin found in phase 1. Frames of one burst show
the same scene, so the scene's own texture cancels out and what is left is the
shake or the missed focus.

The previews are all the same size (`ThumbnailSizes.PreviewEdge`), so the scores
compare across copies of different sizes. Phase 1 checks this holds for a
re-saved copy: the same frame at two sizes must tie here, not have the smaller
one lose on a resampling artefact.

### 3. Exposure

Measured on the cached preview: the share of pixels at the very top or bottom of
the brightness range. A frame taken as the flash fired, or as the camera was
still adjusting, loses here. It is a deliberately narrow test: it catches the
frame that is plainly too bright or too dark, and says nothing about taste.

### 4. Original over re-saved

A copy whose file still carries the camera's details beats one that has lost
them. Messaging apps and some editors strip them when they re-save a photograph,
and a re-saved copy is never better than its original. The signal is what the
scan already reads: the capture date from the file (`Asset.TakenUtc`) and the
coordinates. Phase 1 confirms on the real near sets that this separates
re-saved copies from originals and does not fire between two frames of a burst.

### Exact sets

Identical bytes are the same pixels and the same file details, so every
criterion ties, and **with the folder rule gone, no exact set gets a
suggestion**. That is the honest answer: two byte-identical files are the same
photograph, and either one is the one to keep. The card says "These are the
same file" instead of marking one Suggested. The measurement is skipped for
exact sets rather than relied on to tie.

---

## Behaviour

### On the "Looks the same" screen

When a copy is suggested, its card says which criterion decided it:

| Decided by | Suggested copy | A copy that lost |
|---|---|---|
| Eyes | **Suggested: eyes open** | **Mum's eyes are closed**, naming the person; for two or more, **2 people's eyes are closed** |
| Sharpness | **Suggested: sharpest** | **Blurred** |
| Exposure | **Suggested: best exposed** | **Too bright** or **Too dark** |
| Original | **Suggested: original file** | **Re-saved copy** |
| Pixels | **Suggested: largest picture** | the size, as today |
| Bytes | **Suggested** | nothing extra |
| Nothing | no copy marked | **Nothing to choose between these** on the set |

In the side-by-side view, a known face counted as Closed gets a small marker over
its box, so the reason can be checked by eye in a second. Unknown faces get no
mark.

Nothing about ticking changes. Near sets still arrive unticked with no bulk
approve: a suggestion is a suggestion.

### When it runs

As a step of the duplicates pass (`FindDuplicatesHandler`, run from the "Look
for duplicates" button), after near sets are found, over **only the photographs
in near sets**: about a thousand, not the whole library. The duplicates pass is
not a scan phase today, and this does not make it one.

A photograph's measurements are taken once and kept. Its eyes are measured again
only when its faces are (a turn, or the face pass replacing them); its sharpness
and exposure only when its preview is rewritten. A photograph whose faces have
not been found yet is measured without the eyes criterion and measured again
once they have.

It reads cached previews, never the originals, so it costs no trip to the NAS
and runs with the NAS switched off.

### On other computers

Nothing travels. Every measurement is read from pixels and details every
computer already has, so each one works it out for itself, the way it works out
its own near sets. The eye model is **not** part of the model check that decides
whether two computers may share face pictures; a laptop without it still syncs
with one that has it.

---

## Phase 1 — measure, change nothing

Before the app suggests anything differently:

1. **Eyes.** Label about **60 known faces** from the real library's near sets:
   open, closed, can't tell. Include children, glasses, side-on faces and small
   faces in group shots. This library is mostly one toddler growing up, and a
   landmark model trained mostly on adults is the most likely way for this to be
   wrong. Claude labels them from the crops and the user spot-checks a contact
   sheet of all 60 with the labels on it. Pick the closed threshold from the
   result.
2. **Sharpness and exposure.** From the real near sets, pick pairs where one
   frame is plainly blurred, plainly too bright or too dark, and pairs that are
   the same frame at two sizes. Include scenery with no faces (mostly sky,
   haze, dusk) and frames with grain. Set each margin so the plain cases are
   decided and the same-frame pairs tie. Set the blur guard's hard limit from
   the plainly shaken frames alone, so that no frame a person would call sharp
   enough falls beyond it.
3. **Original over re-saved.** Count, over the real near sets, how often exactly
   one copy carries the camera's details, and look at a sample of those to
   confirm they are re-saved copies and not burst frames.
4. Record every threshold and margin here, with the numbers behind it.
5. **The report.** Run the new ladder over every near and exact set **without
   changing any suggestion**, and list every set whose suggestion would change
   or disappear: both pictures, the criterion that decided, and its numbers.
   Summarise how many sets each criterion decided, and how many end with no
   suggestion.
6. The user looks through the report. Phase 2 starts only when they agree with
   it.

The report and the label sheet hold the family's own photographs and names, so
they are written outside the repository and never committed.

**What would stop this:** if the landmark model reads closed eyes as open often
enough that the labelled sample shows it, it is the wrong model. The fallback is
a small dedicated open/closed classifier on eye crops instead of landmarks (same
pipeline, different last step), and this PRP is updated before anything is built
on it. If sharpness or exposure cannot separate the plain cases from the
same-frame pairs, that criterion is dropped from the ladder rather than tuned
until it seems to work.

## Phase 2 — suggest

Replace the ladder in `KeeperPolicy`, allow a set with no suggested copy, wire
the captions and markers into the review screen, and add the measuring step to
the duplicates pass.

---

## Model

| | |
|---|---|
| File | `2d106det.onnx`: InsightFace's 106-point 2D landmark model, from the same `buffalo_l` pack as the two face models already in use |
| Size | 5,030,888 bytes; SHA-256 `f001b856447c413801ef5c42091ed0cd516fcd21f2d6b79635b1e733a7109dbf` (measured from the `buffalo_l` pack on the development machine) |
| Input | a face crop, 192 × 192 |
| Output | 106 (x, y) points; the eye outline indices are confirmed against the model at build, not taken from memory |
| Runs on | the GPU through DirectML, like the face models: **one inference at a time per session** (the limit [06](06-faces.md) found the hard way) |
| Licence | the same InsightFace non-commercial research terms as the other two. The licence is shown on each feature card in Settings and in the About credits; the credit sentence is extended to say the pack is also used to tell whether eyes are open. |

It is **its own optional feature**, not a third file the People screen waits
for. `FeatureModels` lists faces as exactly two files; adding this one there
would disable People on every machine that has not imported it. Missing, it
turns off only the eyes criterion and its captions; the rest of the ladder still
runs.

Sharpness, exposure and the camera's details need no model.

---

## Contracts

```
FaceEyes            FaceId, State (Open|Closed|Unknown), Openness (float?, for tuning),
                    MeasuredUtc                       -- one row per face, dropped with it
PreviewQuality      AssetId, Sharpness, Clipped (share too bright, share too dark),
                    MeasuredUtc                       -- dropped when the preview is rewritten
IEyeMeasurer        Measure(previewPath, faces) -> per face Open/Closed/Unknown
EyeVerdict          the pure rule: aspect ratios + pose -> State
CopyQuality         one copy's measurements, as the ladder reads them
KeeperPolicy        the ladder: Choose(copies) -> (keeper or none, deciding criterion)
DuplicateCopy       + DecidedBy, + ClosedFaces, + ClosedNames, + Exposure verdict
ModelId.FaceLandmarks, ModelFeature.Eyes             -- optional, own feature
```

Tables rather than columns on `Face` and `Asset`, because a face row is replaced
whenever the face pass runs again and the measurement belongs to that row; a
cascade on delete keeps the two from disagreeing. Migrations are made with
`dotnet ef migrations add`.

`EyeVerdict` and the ladder are pure and hold every threshold and margin, so the
whole decision is testable without a model or a picture.

A set with no suggested copy is new: today `ViewsOfAsync` drops a set without a
keeper. Phase 2 must keep such a set on screen.

---

## Acceptance

- [ ] Phase 1 report produced from the real library, thresholds and margins
      recorded here with the numbers behind them, and signed off by the user.
- [ ] In a burst where one frame has a known face Closed and the other has it
      Open, the Open frame is suggested whatever the sharpness, size or path.
- [ ] A stranger's closed eyes, an unnamed face's, and an Ignored face's never
      decide anything.
- [ ] A known face that is side-on, hidden or unsure is Unknown, and an Unknown
      face never decides anything.
- [ ] A plainly blurred frame loses to a sharp one when the eyes tie; two frames
      within the sharpness margin tie.
- [ ] Blur guard: a plainly blurred frame with every eye open is not suggested
      over a sharp frame with a blink; when every frame is plainly blurred, the
      ladder decides as usual.
- [ ] A re-saved smaller copy of the same frame loses to the original, and never
      on sharpness.
- [ ] When all six criteria tie, no copy is suggested and the set says so.
- [ ] No exact set gets a suggestion, and no exact set is measured.
- [ ] The folder rule is gone: no suggestion depends on a folder name, a path's
      depth or path order.
- [ ] Eye model missing: the eyes criterion and its captions are off, the rest of
      the ladder still runs, and People still opens.
- [ ] Each card names the criterion that decided it, captions name known people,
      and the closed marker sits on the right face in the side-by-side view.
- [ ] No measurement is written to the sync folder, and a computer without the
      eye model still syncs face pictures with one that has it.
- [ ] Runs over near-set photographs only, from cached previews, with the NAS
      off.
- [ ] `EyeVerdict` and every ladder margin covered by tests at each edge.

---

## Out of scope

Smiling, and an overall "nice photo" score: each a candidate for its own piece
of work. Preferring the copy already in an album or with named faces. Videos.
Choosing or deleting anything without the person. Using any of these
measurements anywhere but the "Looks the same" screen.
