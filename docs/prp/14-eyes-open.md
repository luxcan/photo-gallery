# 14 — Eyes open: suggesting the best of a burst

**Status: 📝 Proposed — not started. Phase 1 (measure, change nothing) must be
signed off before phase 2 (suggest).**

"Looks the same" ([05](05-duplicates.md)) holds two kinds of set that a
perceptual hash cannot tell apart: a photograph and a re-saved copy of it, and
the next frame of a burst. For the first, today's rule is right — more pixels,
then more bytes, keeps the copy that has not been through a second encoder. For
the second it is a coin toss. Every frame of a burst is an original at the same
size, so the suggestion falls through to the folder rule and then to path order,
and the app is as likely to suggest the frame where Jia Le blinked as the one
where she did not.

In a family library, the frame where somebody's eyes are closed is the one
nobody wants to keep.

---

## Goal

In a "Looks the same" set, suggest the copy where the fewest people have their
eyes closed, and say so on the card. The person still chooses; nothing is kept or
removed without them.

## Depends on

- [05 — Duplicates](05-duplicates.md) — the sets, `KeeperPolicy`, and the review
  screen this changes.
- [06 — Faces](06-faces.md) — the faces are already found, on the cached
  previews, with a box and a score each.
- [09 — Models](09-models.md) — one more weights file, proved by size and digest
  before use.

---

## The idea

1. **Trace the eyelids.** For each face on a photograph in a near set, crop the
   face from the cached preview and run a 106-point landmark model over it. The
   points include the outline of each eye.
2. **Measure how open each eye is.** The eye aspect ratio: the average gap
   between upper and lower lid divided by the eye's width. An open eye is tall;
   a closed one is nearly a line. Each face gets **Open**, **Closed** or
   **Unknown**.
3. **Count per copy.** A copy's score is how many of its counted faces are
   Closed. No face is matched across copies — counting is enough, and matching
   faces between two frames is a problem this does not need to solve.
4. **Suggest.** Fewer closed faces wins. A tie falls through to today's rule
   unchanged.

### Which faces count

Only faces a person would notice. A face is **Unknown**, and left out of the
count, when:

- it is too small to judge — below a minimum box width on the preview, set from
  the measurement in phase 1, not guessed;
- it is turned well away from the camera, read from the five points the detector
  already gives (the eyes' spread against the nose);
- the landmark model is unsure of it, or one eye is hidden;
- it has been marked **Ignore** on the People screen — somebody already said it
  does not matter.

Sunglasses, a hand over the face, a profile: Unknown, never Closed. Being wrong
in the direction of "closed" is what would make the app suggest a worse
photograph, so every doubt lands on Unknown.

### Where it sits in the rule

`KeeperPolicy.CompareQuality` becomes, for **near** sets only:

1. fewer Closed faces
2. more pixels
3. more bytes
4. the folder rule (named folder, shallower path, path order)

**Exact sets are untouched.** Identical bytes are the same pixels, so every face
measures the same and the term always ties. It is left out of exact sets on
purpose rather than relied on to tie, so this feature cannot change a single
exact-set decision.

A re-saved, smaller copy of the same frame has the same eyes. Eyes tie, and the
original still wins on pixels, as today.

### When it says nothing

- Every copy has the same number closed — a sleeping baby in every frame, or no
  countable faces at all. Today's rule decides and no eyes reason is shown.
- The model is not installed. The screen works exactly as it does now.

---

## Behaviour

### On the "Looks the same" screen

The card under each copy already says **Suggested**. When eyes decided it:

- the suggested copy: **Suggested — eyes open**
- a copy that lost on eyes: **Jia Le's eyes are closed**, naming confirmed
  people; for faces nobody has named, **1 person's eyes are closed** /
  **2 people's eyes are closed**

In the side-by-side view, a face counted as Closed gets a small marker over its
box, so the reason can be checked by eye in a second. Unknown faces get no mark.

Nothing about ticking changes. Near sets still arrive unticked with no bulk
approve: a suggestion is a suggestion.

### When it runs

As a step of the duplicates pass, after near sets are found, over **only the
photographs in near sets** — about a thousand, not the whole library. A
photograph's eyes are measured once and kept; they are measured again only when
its faces are (a turn, or the face pass replacing them).

It reads cached previews, never the originals, so it costs no trip to the NAS
and runs with the NAS switched off.

### On other computers

Nothing travels. Eye state is read from pixels every computer already has, so
each one works it out for itself, the way it works out its own near sets. It is
**not** part of the model check that decides whether two computers may share
face pictures. A laptop without the eye model still syncs with one that has it.

---

## Phase 1 — measure, change nothing

Before the app suggests anything differently:

1. Hand-label about **60 faces** from the real library's near sets — open,
   closed, can't tell — including children, glasses, side-on faces and small
   faces in group shots. This library is mostly one toddler growing up, and a
   landmark model trained mostly on adults is the most likely way for this to
   be wrong.
2. Run the measurement over the labelled faces. Pick the closed threshold and the
   minimum face size from the results, and record them in this file with the
   numbers that justified them.
3. Run the new rule over every near set **without changing any suggestion**, and
   write a report listing every set where the suggested copy would change: both
   pictures, the counts, and the faces it read as closed.
4. The user looks through a sample of that report. Phase 2 starts only when they
   agree with it.

**What would stop this:** if the landmark model reads closed eyes as open often
enough that the labelled sample shows it, it is the wrong model. The fallback is
a small dedicated open/closed classifier on eye crops instead of landmarks — same
pipeline, different last step — and this PRP is updated before anything is built
on it.

## Phase 2 — suggest

Wire the count into `KeeperPolicy` for near sets, the captions and markers into
the review screen, and the step into the duplicates pass.

---

## Model

| | |
|---|---|
| File | `2d106det.onnx` — InsightFace's 106-point 2D landmark model, from the same `buffalo_l` pack as the two face models already in use |
| Size | about 5 MB; the exact size and SHA-256 go into the manifest when it is first imported |
| Input | a face crop, 192 × 192 |
| Output | 106 (x, y) points; the eye outline indices are confirmed against the model at build, not taken from memory |
| Runs on | the GPU through DirectML, like the face models — **one inference at a time per session** (the limit [06](06-faces.md) found the hard way) |
| Licence | the same InsightFace non-commercial research terms as the other two, which this app still never shows ([09](09-models.md)'s open gap). Adding a third file under those terms makes that gap worth closing in the same piece of work. |

It is **its own optional feature**, not a third file the People screen waits
for. `FeatureModels` lists faces as exactly two files; adding this one there
would disable People on every machine that has not imported it. Missing, it
turns off only the eyes term and its captions.

---

## Contracts

```
FaceEyes            FaceId, State (Open|Closed|Unknown), Openness (float?, for tuning),
                    MeasuredUtc                       -- one row per face, dropped with it
IEyeMeasurer        Measure(previewPath, faces) -> per face Open/Closed/Unknown
EyeVerdict          the pure rule: aspect ratios + face size + pose -> State
KeeperPolicy        CompareQuality(left, right, closedCounts) -- near sets only
DuplicateCopy       + ClosedFaces, + ClosedNames      -- what the card says
ModelId.FaceLandmarks, FeatureModels.Eyes             -- optional, own feature
```

A table rather than columns on `Face`, because a face row is replaced whenever
the face pass runs again and the measurement belongs to that row; a cascade on
delete keeps the two from disagreeing. The migration is made with
`dotnet ef migrations add`.

`EyeVerdict` is pure and holds every threshold, so the whole decision is
testable without a model or a picture.

---

## Acceptance

- [ ] Phase 1 report produced from the real library, thresholds recorded here
      with the labelled-sample numbers behind them, and signed off by the user.
- [ ] In a burst where one frame has a face Closed and the other has it Open,
      the Open frame is suggested whatever the folder or path order says.
- [ ] A re-saved smaller copy of the same frame still loses to the original
      (eyes tie, pixels decide).
- [ ] Every exact set's suggestion is the same as before the feature —
      asserted over the real library's exact sets, not a sample.
- [ ] A face too small, side-on, hidden, unsure or Ignored is Unknown, and an
      Unknown face never decides anything.
- [ ] Every copy with the same number closed: today's rule decides, no eyes
      caption.
- [ ] Model missing: the screen is exactly today's; People still opens.
- [ ] Captions name confirmed people and count the rest, and the closed marker
      sits on the right face in the side-by-side view.
- [ ] Eye state is not written to the sync folder, and a computer without the
      model still syncs face pictures with one that has it.
- [ ] Runs over near-set photographs only, from cached previews, with the NAS
      off.
- [ ] `EyeVerdict` covered by tests at every threshold edge.

---

## Out of scope

The other criteria for choosing a copy, each a candidate for its own piece of
work: sharpness (blur), exposure, the original file over a re-saved one (camera
data still present, no watermark), the copy already in an album or named, and an
overall "nice photo" score. Smiling. Videos. Choosing or deleting anything
without the person. Using eye state anywhere but the "Looks the same" screen.
