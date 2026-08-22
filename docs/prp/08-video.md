# 08 — Video

**Status: ◐ Posters ship; seeking across a clip does not**

4,743 videos — 91% of the library's bytes, and a real share of its best moments.

---

## Goal

Give videos thumbnails, and make the people in them findable.

## Depends on

[06 — Faces and people](06-faces.md)

---

## Why it is last

| | |
|---|---|
| Videos | 4,743 files, **267 GB** |
| Reading them once at 6.4 MB/s | **~11.6 hours** |
| Photos, for comparison | 11,481 files, 24.8 GB, ~1 hour |

Everything else in the app can be finished in the time one video pass takes. So
it waits — and when it runs, it should run **over Ethernet**, where the same pass
is closer to an hour.

This is a genuine gap, not a dismissal: a lot of what a small child does is
captured as video, and until this ships those moments are unsearchable.

---

## Approach

Extract a few **keyframes** per video, then treat them as photos.

- A frame near the start, one near the middle, one near the end — enough to
  produce a poster image and to catch who is present.
- Keyframes go through the same thumbnail store and the same face pipeline. No
  new model, no new matching logic.
- Only the extraction is new.

### How — answered for the poster, open for the rest

There is no built-in .NET video decoder. The realistic options:

| Option | Trade-off |
|---|---|
| Bundle FFmpeg | Reliable, handles everything, but a large binary and a licence to check |
| Windows Media Foundation | Already present, no bundle, but fiddlier and codec-dependent |
| **Shell thumbnail API** ✅ | Cheapest — Explorer already makes these — but one frame only, no control |

Splitting the two jobs is what happened. The shell API is what ships: it needs
nothing bundled and no codec knowledge, and whatever the machine can already
show a thumbnail for — AVCHD and Matroska included — it can take a poster from.
Measured against real files on the developer's machine: three of four clips gave
a 1024px poster at the right source dimensions, including a portrait phone
video; the fourth was a 156-byte truncated recording and was refused, which the
pass records so it is never opened again.

It gives **one frame, not three**, and no duration — the shell hands back the
picture it has decided represents the file and will not seek. So:

- the poster is done, and that one frame goes through the face pass;
- **finding the people who appear only later in a clip still needs a seeking
  extractor**, which is Media Foundation or FFmpeg behind the same
  `IKeyframeExtractor` port;
- the duration badge shows nothing until then, rather than a made-up figure.

The day that extractor lands, the frames cannot simply be added to the poster's
faces. `IFaceRepository.SaveAsync` *replaces* an asset's faces rather than adding
to them, so three frames each saving against one video would leave only the last
one's; and a box found nine minutes in does not sit anywhere on the poster. The
frames have to be scanned as a set and attributed to the frame they came from.
`VideoKeyframes` already stores each frame with its ordinal and position for
exactly that.

---

## Behaviour

- Videos appear in the gallery with a poster, and a duration badge where the
  length is known. A video with no poster yet is **not** shown — unlike a
  photograph, whose placeholder is filled in minutes later by the pass that
  follows the scan, a video waits on a pass somebody has to choose to start and
  would otherwise sit grey for months. The grid grows as the pass runs.
- People search includes videos once keyframes are indexed.
- The pass is resumable and cancellable like every other, and warns before
  starting that it is the long one. It is asked for on **Photo sources**, beside
  "Scan all folders": this is preparation, and it belongs next to the folders it
  reads.

---

## Acceptance

- [x] Every video has a poster image.
- [x] Keyframes feed the existing face pipeline unchanged. *(The poster does. A
      clip's later frames need the seeking extractor above.)*
- [x] The pass resumes after being stopped.
- [x] The UI is honest about the time. *(It promises no number of minutes in
      advance, because there is none to promise — the pass seeks rather than
      reading each file through, so the 11.6 hours above is an upper bound and
      not an estimate. A dialog said so before starting while this was a button
      of its own; now that it is a phase of a scan, the honesty is the named
      phase, the count of clips left, an estimate measured from the run itself,
      and a Stop that costs nothing.)*
- [x] Videos are visibly distinguished from photos in the grid.

---

## Out of scope

Playback in the app. Editing. Transcoding. Scene detection beyond a few
keyframes.
