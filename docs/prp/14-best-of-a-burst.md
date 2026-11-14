# 14 — Best of a burst: suggesting the copy worth keeping

**Status: 🟨 In progress. Phase 1 (measure, change nothing) is done and was
signed off by the user on 14 November 2026 with the decisions recorded below.
Phase 2 (suggest) is built.**

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

## Decisions (signed off 14 November 2026)

Each was put to the user with the phase 1 numbers and settled. They are final.

| Decision | Why |
|---|---|
| Eyes use the landmark calculation (`EyeVerdict`) with the phase 1 thresholds, and the rule is **paired** | Counting Closed faces alone decided 5 sets in phase 1 and suggested a plainly shaken frame in one of them. Paired, eyes decides 1 set and the shaken frame loses on sharpness instead. |
| A dedicated open/closed eye model comes later, as separate work | The landmark verdict catches only 9 of 18 labelled blinks, but it never reads an open face as Closed, so it is safe to ship. The eye step stays behind `IEyeMeasurer` and `EyeVerdict` so the classifier can replace it. |
| **Looking down counts as closed** | Fully lowered lids that the calculation reads as Closed stay Closed. There is no special "down-gaze is Unknown" rule; the paired rule already keeps a lowered gaze from deciding a set unless the same person reads Open in another copy. |
| The blur guard is **dropped** | Phase 1 found no hard limit: shaken frames score 13 to 140, sharp newborn faces 4 to 10, and frames a person calls sharp enough lose to their set's sharpest by up to 5.18 times. |
| Sharpness compares **all** copies, each measured at **one common size** | Comparing only copies of equal pixel count would have kept sharpness away from every re-saved copy. Measuring every preview at 640 px on its longer edge removes the size advantage instead, and a preview smaller than that has no reading, so a re-saved smaller copy can never lose on sharpness. |
| Exposure margin 0.050 on one side's share | Kept as measured. |
| Original over re-saved: a capture date or coordinates beats neither | Kept as measured. Coordinates alone are a signal; "more details" is not. |
| Pixels: more wins | Unchanged from 05. |
| **Bytes are not a rung** (decided 14 November 2026, after phase 2 ran on a copy of the library) | Bytes were first to speak only between copies of one photograph saved twice. Nothing in the library tells those from burst frames: capture times agree to the second within a burst and most copies carry none, and perceptual distance overlaps. Of six sets bytes decided, five were plainly different frames. See "Bytes" below. |
| No rung after pixels | A full tie suggests nothing, and the card says "Nothing to choose between these". |
| Identical sets are never measured and never suggested | The same bytes are the same photograph; the card says "These are the same file". The folder rule is removed entirely. |

---

## The ladder

Copies are compared one rung at a time, in this order. **A rung only speaks
when the difference is clear**: every copy within the rung's margin of the best
goes on to the next rung. When one copy is left it is suggested and the rung is
named; when the last rung still leaves several, nothing is suggested. Every
margin is set from the phase 1 measurement of the real library and held in
`KeeperPolicy`.

| # | Rung | Better copy | Clear when |
|---|---|---|---|
| 1 | Known people's eyes | fewer known people Closed here and Open in another copy still in the running | the counts differ at all |
| 2 | Sharpness | the sharper picture, measured at 640 px | one is more than **1.57** times sharper |
| 3 | Exposure | less blown white or crushed black | one side's share exceeds the lowest in the running by more than **0.050** |
| 4 | Original over re-saved | carries a capture date or coordinates | one does and another has neither |
| 5 | Pixels | more pixels | they differ |

**There is no sixth rung.** If all five tie, no copy is suggested, and the card
says so. The folder rule (named folder, shallower path, path order) is gone:
the ladder is handed the copies' measurements without their paths.

### 1. Known people's eyes

Only **known people** count: a face confirmed as a named person, or matched by
the app to a named person (`AssignmentSource.Confirmed` or `Proposed`), on a
face that is not ignored and a person who is not deleted. A stranger in the
background, a face nobody has named, a rejected or cleared match, and a face
marked **Ignore** never count. In a burst, the same child in the next frame is
nearly always matched correctly, which is why matched faces count and not only
confirmed ones.

For each face, the cached preview is cropped around it, the 106-point landmark
model places the eye outlines, and each eye's openness is the gap between its
lids divided by its width (the eye aspect ratio). `EyeVerdict` turns that into
**Open**, **Closed** or **Unknown**:

| Setting | Value | Evidence (phase 1) |
|---|---|---|
| Closed at most (both eyes) | 0.060 | Of 60 hand-labelled known faces (18 closed, 29 open, 13 can't tell) no open face reads Closed: the nearest open face measures 0.110 on its wider eye. 0.060 keeps out the can't-tell faces just above it: two narrow smiling squints behind glasses (0.076, 0.081) and a downward gaze (0.062). |
| Open at least (both eyes) | 0.15 | Only for display and tuning: Open is never counted on its own. No closed face reaches it (the highest is 0.148). |
| Most turn | 0.40 | A chosen guard, not a measurement: the far eye of a side-on face cannot be read, and nothing labelled lies between 0.357 and 0.467 to pin the number. It costs 144 of 1660 known faces reading Unknown instead of Open. |
| Least confidence | 0.60 | Refuses the three faces at confidence 0 (all profiles) and keeps sleeping babies lying sideways (0.73 to 0.77). |

Confusion at these settings (label to verdict): closed 18 = 9 Closed, 9
Unknown, 0 Open; open 29 = 0 Closed, 13 Unknown, 16 Open; can't tell 13 = 3
Closed, 6 Unknown, 4 Open. Over the whole library: 1306 Open, 342 Unknown, 12
Closed, and all 12 Closed faces are in the labelled sample.

**The rung is paired.** A copy loses on eyes only for a known person who reads
Closed in it and Open in another copy still in the running. Closed against
Unknown is no difference, and nor is a person Closed in every copy. Each copy's
score is how many such paired faces it has; the copies with the fewest go on.
No face is matched across copies beyond "the same person".

A known face is **Unknown**, and never counted, when it is turned well away from
the camera (read from the detector's five points: the eyes' spread against the
nose), when the landmark model is unsure of it, or when one eye is hidden.
Sunglasses, a hand over the face, a profile: Unknown, never Closed. **Looking
down counts as closed**: fully lowered lids that measure shut are Closed, and
the paired rule is what keeps one from deciding a set on its own.

Only 9 of 18 labelled blinks are caught, because three closed faces (children
and newborns) measure 0.135 to 0.148, inside the open range. They read Unknown,
never Open, so the rung is safe; relying on it to catch most blinks needs the
dedicated eye classifier, which is separate work behind the same port.

### 2. Sharpness

Measured on the cached preview, with no model: the variance of the Laplacian (a
standard measure of how much fine edge detail survives), after a very light
blur so that sensor grain does not read as detail.

**Every preview is brought down to 640 px on its longer edge first** (area-
averaging bicubic). Without that, a smaller re-saved copy has a smaller preview
and the same detail packed into fewer pixels reads as sharper. A preview
smaller than 640 px is never enlarged: it has no sharpness reading at all and
the ladder leaves it to the other rungs, so **a re-saved smaller copy never
loses on sharpness**.

Where every copy still in the running shows the same known people, it is
measured over those people's face boxes, pooled by area, because a sharp
background behind a blurred child is not a sharp photograph. Only the people
common to every copy count: pooling every known face made a copy lose for
holding one extra, smaller face (40 copies in phase 1). Otherwise it is
measured over the **most detailed quarter of the picture** (an 8-tile grid,
top quarter by detail), so a frame that is two thirds sky is not averaged into
nothing.

The comparison is a ratio. Frames of one burst show the same scene, so the
scene's texture cancels out and what is left is the shake or the missed focus.

**Choosing the common size.** Measured on the phase 1 pairs at each edge
(largest ratio of a pair that must tie, smallest ratio of a plainly shaken
frame):

| Common edge | Same frame at two sizes (49) | Re-saved copies (3) | Plainly shaken or out of focus (5) |
|---|---|---|---|
| 1024 (no common size) | 1.00 to 1.34 | up to 1.76 | 1.78 to 2.86 |
| 828 (smallest preview in the library) | up to 1.27 | up to 1.63 | 1.69 to 2.31 |
| 768 | up to 1.27 | up to 1.61 | 1.67 to 2.28 |
| **640** | **1.00 to 1.26** | **1.21 to 1.53** | **1.61 to 2.18** |
| 512 | up to 1.26 | up to 1.44 | 1.51 to 1.94 |
| 384 | up to 1.23 | up to 1.41 | 1.26 to 1.76 (crosses) |

640 is below the smallest preview edge that occurs (828), and it is where the
shaken frames stand furthest above both kinds of pair that must tie (about 5%
above the re-saves; 512 is no wider and loses detail; 384 crosses).

**The margin moved from 1.50 to 1.57.** At 640 the two messaging-app re-saves
measure 1.51 and 1.53, so 1.50 would have suggested the smaller re-saved copy
over its original. Through the code that ships (`PreviewQualityReader` and
`KeeperPolicy`) at 640 and 1.57:

- **Plainly shaken or out of focus** (5 pairs): 1.61 to 2.18, all decided.
- **Same frame at two sizes** (49 pairs): 1.00 to 1.26, all tie.
- **Re-saved copies** (3 pairs, messaging-app and GIF-maker re-saves): 1.21 to 1.53, all tie.
- **Softer only when enlarged** (2 pairs): 1.07 to 1.12, both tie.
- **Re-edited copies** (7 pairs, phone edits and HEIC originals beside edited
  JPEGs): 1.00 to 2.44. Six tie. One, a HEIC original beside an edited JPEG of
  it, is decided at 2.44 for the edited copy, and it reads more than 2 times
  sharper at every size from 256 to 1024: the edit sharpened the picture. That
  is a different picture rather than a re-encode, and the one known case where
  sharpness overrides the original. Recorded, not tuned around.

The gap above the margin is narrow (1.53 tie, 1.61 decided). If the eye
classifier work or a later library shows a re-save above 1.57, this rung is
re-measured with the same pairs before anything else changes.

### 3. Exposure

Measured on the cached preview at its own size (a share of pixels does not
change with size): the share of pixels at grey 250 or above (blown white) and
at grey 5 or below (crushed black). A copy is clearly worse when either share
exceeds the lowest in the running by more than **0.050**. Two copies each wrong
a different way would both be thrown out by that, so then the rung ties.

- Plainly too bright or too dark (6 pairs): 0.053 to 0.149, decided.
- Brighter or darker, but not plainly wrong (5 pairs): 0.038 to 0.044, tie.
- Same frame at two sizes (49 pairs): at most 0.031, tie.
- The gap above the margin is narrow.

### 4. Original over re-saved

A copy whose file still carries the camera's details beats one that has lost
them. Messaging apps and some editors strip them when they re-save a
photograph. The signal is what the scan already reads: the capture date
(`Asset.TakenUtc`) or the coordinates. A copy with either beats a copy with
neither.

- In 30 near sets exactly one copy carries details. All 30 were looked at: 29
  are the same frame re-saved; the 30th holds three burst frames and a
  messaging-app copy of one of them, and the rung removes only that copy. It
  never chose between burst frames.
- More details is not better: in 5 sets both copies have a date and only one has
  coordinates, and in 2 of those the copy with coordinates is the smaller one.

### 5. Pixels

More pixels wins. A copy whose size was never recorded counts as none.

### Bytes: not a rung

The first decision was that the bigger file would win, but only between copies
of one photograph saved twice, and that two copies carrying different capture
times were burst frames. It was built that way and run on a copy of the real
library. **421 sets reach the end of the ladder** with two or more copies
left. In 282 the capture times differ. In 139 they do not (59 share a capture
time, 79 have none on either copy, 1 has one on one copy only), and bytes
decided 137 of those.

Those 137 were mostly not re-saves:
- By file name, 25 are one frame number saved twice.
- About 90 carry consecutive frame numbers: 22 share a capture time to the
  second, 67 have none on either copy, and 1 has one on one copy.
- 22 are unclear.

Six of the consecutive-number sets were looked at side by side. Five were
plainly different frames, with poses that changed. The sixth was one
photograph with text added to one copy. Perceptual distance does not separate
the two cases either: copies with the same frame number and consecutive frames
both differ by 1 to 3 bits.

No signal tells one photograph saved twice from two frames of a burst, so
**bytes decide nothing** (user, 14 November 2026). A real re-save is mostly
settled before this point anyway, by the original-over-re-saved and pixels
rungs. `CopyQuality` no longer carries the file's length, so nothing in the
ladder could fall back on it.

### Exact sets

Identical bytes are the same pixels and the same file details, so every rung
would tie. **No exact set is measured and no exact set gets a suggestion.** The
card says "These are the same file" instead of marking one Suggested. All 228
exact sets in the library hold no suggestion.

### The ladder over every set

With the shipping code (common size 640, margin 1.57, paired eyes, no bytes
rung), over the 592 near sets (504 with two or more copies left, 88 holding one
copy after earlier deletions). The second row is the real pass, run through the
app's own handlers on a copy of the library on 14 November 2026: it measured
1,172 photographs on the GPU in 3 min 18 s, and a second pass took a second.

| | Eyes | Sharpness | Exposure | Original | Pixels | None |
|---|---|---|---|---|---|---|
| All 504 sets | 1 | 20 | 2 | 29 | 31 | 421 |
| The 503 on screen | 0 | 20 | 2 | 29 | 31 | 421 |

The one set eyes decides was answered in August; the person kept the copy in
which the eyes read Open.

---

## Behaviour

### On the "Looks the same" screen

When a copy is suggested, its card says which rung decided it:

| Decided by | Suggested copy | A copy that lost |
|---|---|---|
| Eyes | **Suggested: eyes open** | **Mum's eyes are closed**, naming the person; for two or more, **2 people's eyes are closed** |
| Sharpness | **Suggested: sharpest** | **Blurred** |
| Exposure | **Suggested: best exposed** | **Too bright** or **Too dark** |
| Original | **Suggested: original file** | **Re-saved copy** |
| Pixels | **Suggested: largest picture** | the size, as today |
| Nothing | no copy marked | **Nothing to choose between these** on the set |
| Identical set | no copy marked | **These are the same file** on the set |

On the copy's card tile, a known face counted against a copy on eyes gets a
small marker over its box, so the reason can be checked by eye in a second.
Only a copy that lost on eyes is marked: unknown faces, faces closed in every
copy, and a blink in a copy that went on because the eyes tied get no mark. The
full-size inspector has no marker yet; that is still to be built.

The suggestion is worked out when the screen is shown, from the measurements as
they stand, so a renamed person, a face pass or an eye model installed or
removed is reflected without another look for duplicates.

Nothing about ticking changes. Near sets still arrive unticked with no bulk
approve: a suggestion is a suggestion. A set with no suggestion stays on the
screen (it used to be dropped for having no keeper), and its copies are
**undecided** rather than redundant, so nothing could ever set them all aside.

### When it runs

As a step of the duplicates pass (`FindDuplicatesHandler`, run from the "Look
for duplicates" button), after the sets are found, over **only the photographs
in near sets**: about a thousand, not the whole library. The pass runs under
the shared overlay with the step named ("Measuring the copies that look the
same", with a count) and a safe Stop. It is part of looking for duplicates,
never something to switch on.

A photograph's measurements are taken once and kept, and each is saved as soon
as it is measured. Stopping keeps what was measured and leaves the sets as they
were; the next look carries on from there. A photograph is measured again only
when its preview has been written since (a turn, or preparing it again), when
the face pass has found faces with no measurement yet, or when the eye model
has been installed since its faces were measured without it.

It reads cached previews, never the originals, so it costs no trip to the
photo sources and runs with them switched off. One photograph at a time, so the
eye graphs see one inference at a time.

### On other computers

Nothing travels. Every measurement is read from pixels and details every
computer already has, so each one works it out for itself, the way it works out
its own near sets. The eye model is **not** part of the model check that decides
whether two computers may share face pictures; a laptop without it, or with a
different build of it, still syncs face pictures with one that has it.

---

## Phase 1 — measure, change nothing (done)

1. **Eyes.** 60 known faces from the real library's near sets were labelled
   open, closed or can't tell, including children, glasses, side-on faces and
   small faces in group shots, and the user spot-checked a contact sheet of all
   60. Thresholds above.
2. **Sharpness and exposure.** Pairs where one frame is plainly blurred, plainly
   too bright or too dark, and pairs that are the same frame at two sizes or
   re-saved, including scenery, sky, dusk and grain. Margins above; the blur
   guard found no limit and was dropped.
3. **Original over re-saved.** Counted and every case looked at. Above.
4. Every threshold and margin is recorded here with the numbers behind it.
5. **The report** ran the ladder over every near and exact set without changing
   any suggestion and listed every set whose suggestion would change or
   disappear, with both pictures and the numbers.
6. The user looked through the report and signed it off on 14 November 2026
   with the decisions above. The common-size sharpness and the bytes split were
   re-measured for phase 2 with the same harness and pairs.

The report, the label sheet and the pair lists hold the family's own
photographs and names, so they are kept outside the repository and never
committed. The harness (`tools/PhotoGallery.Measure`) is in the repository; it
refuses to write into the library or a git working tree.

**What would still stop this:** if the eye classifier, when it is built, reads
closed eyes as open in the labelled sample, it is the wrong model. If a later
library shows sharpness or exposure deciding a same-frame pair, that rung is
re-measured and, failing that, dropped rather than tuned until it seems to work.

## Phase 2 — suggest (built)

The ladder replaces the old rule in `KeeperPolicy`, a set can have no suggested
copy, the captions and markers are on the review screen, and the measuring step
is part of the duplicates pass.

---

## Model

| | |
|---|---|
| File | `2d106det.onnx`: InsightFace's 106-point 2D landmark model, from the same `buffalo_l` pack as the two face models already in use |
| Size | 5,030,888 bytes; SHA-256 `f001b856447c413801ef5c42091ed0cd516fcd21f2d6b79635b1e733a7109dbf` (measured from the `buffalo_l` pack, and served with the same length and digest by the pinned mirror the other two face files come from) |
| Input | a face crop, 192 x 192, raw 0 to 255 RGB, levelled by the detector's eye roll |
| Output | 106 (x, y) points; the eye rings are points 35 to 42 and 89 to 96, confirmed against the model on real crops |
| Runs on | the GPU through DirectML, like the face models: **one inference at a time per session** (the limit [06](06-faces.md) found the hard way) |
| Licence | the same InsightFace non-commercial research terms as the other two. Shown on its own feature card in Settings; the About credit says the pack is also used to tell whether eyes are open. |

It is **its own optional feature** (`ModelFeature.Eyes`, "Telling whether eyes
are open" in Settings), not a third file the People screen waits for.
`FeatureModels` lists faces as exactly two files; adding this one there would
disable People on every machine that has not imported it. Missing, it turns off
only the eyes rung and its captions - eye states read before it went are not
trusted either - and the rest of the ladder still runs.

Sharpness, exposure and the camera's details need no model.

---

## Contracts

```
FaceEyes            FaceId, State (Open|Closed|Unknown, null = eyes not read),
                    Openness (wider eye, for tuning), Sharpness (face box at
                    the common size), MeasuredUtc      -- one row per face, dropped with it
PreviewQuality      AssetId, ThumbnailName and PreviewWrittenUtc (the preview it
                    was read from), Width, Height, Sharpness, ClippedHigh,
                    ClippedLow, MeasuredUtc            -- measured again when the preview is rewritten
IEyeMeasurer        Measure(previewPath, faces) -> per face measurement
EyeVerdict          the pure rule: aspect ratios + pose -> State; EyeVerdict.Measured holds phase 1's numbers
IPreviewQualityReader  Read(previewPath, faces) -> sharpness at the common size, exposure
IPhotoQualityRepository  candidates to measure, save, CopyQuality per copy (known faces only)
CopyMeasurer        the measuring step: near copies only, resumable, one at a time
CopyQuality         one copy's measurements, as the ladder reads them (no path)
KeeperPolicy        the ladder: Choose(copies) -> KeeperChoice (keeper or none,
                    deciding rung, per copy: rung lost on, exposure fault, closed faces)
DuplicateRole       + Undecided                       -- every copy of a set with no suggestion
DuplicateCopy       + DecidedBy, + ClosedFaces, + ClosedNames, + Exposure, + preview size
ModelId.FaceLandmarks, ModelFeature.Eyes             -- optional, own feature
```

Tables rather than columns on `Face` and `Asset`, because a face row is replaced
whenever the face pass runs again and the measurement belongs to that row; a
cascade on delete keeps the two from disagreeing. The migration is
`AddEyesAndPreviewQuality`, made with `dotnet ef migrations add`.

`EyeVerdict` and the ladder are pure and hold every threshold and margin, so the
whole decision is testable without a model or a picture. The common size is
`PreviewQualityMeter.CommonEdge`.

---

## Acceptance

- [x] Phase 1 report produced from the real library, thresholds and margins
      recorded here with the numbers behind them, and signed off by the user.
- [x] In a burst where one frame has a known face Closed and the other has it
      Open, the Open frame is suggested whatever the sharpness, size or path.
- [x] A stranger's closed eyes, an unnamed face's, a rejected or cleared match's,
      and an Ignored face's never decide anything.
- [x] A known face that is side-on, hidden or unsure is Unknown, and an Unknown
      face never decides anything; Closed against Unknown is no difference.
- [x] A plainly blurred frame loses to a sharp one when the eyes tie; two frames
      within the sharpness margin tie.
- [x] Every copy is measured at the common size; a re-saved smaller copy of the
      same frame loses to the original, and never on sharpness.
- [x] File size decides nothing, so no burst frame is suggested for being the
      bigger file.
- [x] When all five rungs tie, no copy is suggested and the set says so.
- [x] No exact set gets a suggestion, and no exact set is measured.
- [x] The folder rule is gone: no suggestion depends on a folder name, a path's
      depth or path order.
- [x] Eye model missing: the eyes rung and its captions are off, the rest of
      the ladder still runs, and People still opens.
- [x] Each card names the rung that decided it, and captions name known people.
- [ ] The closed marker sits on the right face on the card tile: built and its
      boxes tested, not yet seen on screen in a demo-mode render. Not built in
      the full-size inspector.
- [x] No measurement is written to the sync folder, and a computer without the
      eye model still syncs face pictures with one that has it.
- [x] Runs over near-set photographs only, from cached previews, with the photo
      sources off, and a Stop keeps what was measured (tested on the handler).
- [ ] Shown on the overlay as a named step with Stop beside it: built, not yet
      seen in the running app.
- [x] `EyeVerdict` and every ladder margin covered by tests at each edge.

---

## Out of scope

Smiling, and an overall "nice photo" score: each a candidate for its own piece
of work. The dedicated open/closed eye classifier: next, as separate work behind
`IEyeMeasurer`. Preferring the copy already in an album or with named faces.
Videos. Choosing or deleting anything without the person. Using any of these
measurements anywhere but the "Looks the same" screen.
