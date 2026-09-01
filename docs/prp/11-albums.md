# 11 — Albums

**Status: 🟨 In progress — the reason the rest of the second half exists**

Twelve years of photos nobody has time to organise. The app should propose the
groupings itself: *"Genting Trip, 3–5 March"*.

---

## Goal

Offer albums the user never asked for, named well enough to be worth
opening, and let them make their own. A photograph belongs to at most one
album, so opening an album is a complete answer rather than one view of
many.

## Depends on

[10 — Location](10-location.md) for the place, [06 — Faces](06-faces.md) for the
people. [07](07-content-search.md) is additive, not required.

---

## What the signals actually are

Measured on this library rather than assumed, because the first draft of this
document assumed a great deal and two of the assumptions were wrong.

| Signal | Coverage | Consequence |
|---|---|---|
| `TakenUtc` | 9,544 of 15,823 (60%) | 6,279 photographs and **every one of the 4,743 videos** carry no capture date at all |
| Coordinates | 1,709 (11%) | the earlier estimate of "four in ten" was wrong by a factor of four |
| Resolved place | 1,709 (11%) | the same rows; a place is never known without coordinates |

Two things follow. **Distance can refine a grouping but can never make one** —
it is knowable for one photograph in nine, so time carries the feature. And the
naming ladder lands on its lower rungs most of the time, which is why the bottom
rung has to be a name somebody is content to see.

Videos are absent from every proposal until an extractor reads their capture
metadata. That is honest — a video with no date cannot be placed on a timeline —
but it is worth saying on screen rather than leaving as a silence.

---

## The clusterer is not a model

Grouping is time and place first, then labelling with who and what is in it.
Nothing here needs weights: it is two thresholds and a sort.

### Why one gap threshold does not work

The obvious rule — start a new album after a gap of six hours — was
simulated over all 9,544 dated photographs before it was built:

| Gap | Albums | Photos covered | Spanning > 1 day | Longest |
|---|---|---|---|---|
| 6 h | 180 | 4,966 | **0** | 0.7 days |
| 14 h | 230 | 6,111 | 53 | 4.6 days |
| 20 h | 227 | 6,820 | 96 | 8.8 days |
| 24 h | 238 | 7,460 | 132 | 25.5 days |
| 36 h | 231 | 8,174 | 172 | 32.0 days |

At six hours **not one album spans more than a day**, because everybody
sleeps: a night's gap ends the group, so "Genting Trip, 3–5 March" — this
document's own headline example — could never have been produced. Widening the
one threshold until trips appear is worse, not better: by twenty-four hours a
single album swallows twenty-five days.

### Two levels, which is what a trip actually is

1. **A session** is photographs with no gap longer than **6 hours** — a morning
   at the beach, an evening out.
2. **An album** is a run of sessions on **consecutive days**. A day with no
   photographs ends it.
3. A run longer than **21 days** is not an occasion, it is ordinary life. Those
   days are offered as separate day albums instead of one long one.

Measured over the same library, that produces **250 albums, 185 of them
spanning more than one day**, plus the days rescued from the three runs that
were too long. The longest uncapped run here is 63 days, which is exactly what
the cap exists to stop.

The cap was measured rather than chosen. Ten was the first guess, and a genuine
fortnight away came back as eleven separate days; fourteen still broke a
fifteen-day run apart. Twenty-eight splits only one run — but calls a 26-day
stretch an occasion, which it is not.

| Cap | Runs split | Longest occasion kept whole |
|---|---|---|
| 14 days | 5 | 14 days |
| **21 days** | **3** | **20 days** |
| 28 days | 1 | 26 days |

A group must still earn its place: at least **8 photographs**, and at least
**90 minutes** from first to last, or a single burst becomes a "album".

> **Use `TakenUtc`, never `CreatedUtc`.** [Asset.cs](../../src/PhotoGallery.Domain/Assets/Asset.cs)
> records the measurement: 3,000 photos spanning eight years carry **13 distinct
> creation days**, one per bulk copy. Clustering on creation time would propose
> thirteen enormous albums named after the days the files were copied.
> A photo with no `TakenUtc` is left out rather than dropped into whichever group
> it lands beside — it can still be put into an album by hand.

---

## Naming, and the 89% problem

Nine photographs in ten have no place attached, so the name degrades down a
ladder rather than failing:

| What is known | Name |
|---|---|
| Place + short span | **Genting Trip**, 3–5 March |
| Place + one day | **Genting**, 3 March |
| Place spans several | **Genting and Kuala Lumpur**, 3–7 March |
| No place, people known | **A weekend with Ana Lim**, 3–5 March |
| No place, no people, content known | **Birthday**, 3 March |
| Nothing but the dates | **3-5 March 2019**, 42 photos |

An album is only called a *Trip* when its photos sit more than 50 km from
where that period's photos usually are — otherwise every weekend at home becomes
a trip. With coordinates on one photograph in nine, most albums will not be
called trips, and that is the correct outcome rather than a shortfall.

Never invent. If the only honest name is a month and a count, that is the name.

---

## Rules

- Albums are **proposed, never imposed**. Nothing is moved, renamed or
  deleted on disk — the app only ever reads a source
  ([01](01-photo-sources.md)).
- **A photograph belongs to at most one album.** Adding it to a second
  moves it, and the app says which album it came out of. The alternative —
  refusing until the user removes it themselves — turns one action into two for
  a rule they did not ask about.
- **A rejection is remembered, per photograph per album.** Rejecting a
  photograph from *Genting Trip* does not stop it being proposed for a different
  album later, and does not touch the rest of that album. Dismissing a
  whole proposal is remembered the same way.
- **The user can make their own albums**, name them, add photographs to
  them and take them out again. An album made by hand is never rebuilt,
  renamed or removed by a pass.
- A user can rename a proposed album; the app never renames it back.
- Recomputing is idempotent. Adding a folder does not renumber or duplicate
  albums that already exist.
- An album with no cover picture is not shown. The cover is the photo in it
  with the most faces, else the middle photo of the span - left to the middle
  alone, the first covers produced on a real library were a hotel blanket and a
  ceiling.
- Building runs as a phase of the scan, resumable and stoppable like every other
  pass.

---

## Contracts

```
Album                 Id, Name, StartUtc, EndUtc, PlaceId?, CoverAssetId,
                      Kind (Trip | Day | Event | Period),
                      Origin (Proposed | Accepted | Made),
                      ProposalKey?, WasRenamed, BuiltUtc
AlbumMember           AssetId (PRIMARY KEY), AlbumId, AddedUtc
AlbumRejection        AssetId, ProposalKey (composite key), RejectedUtc
BuildAlbumsHandler    (IProgress, ct) -> AlbumsResult
```

`Kind` exists so the wording rules above are a property of the row rather than
of a formatting function that has to guess. `Origin` exists so a pass knows what
it is allowed to touch: it rebuilds what it proposed, leaves an album that
was kept alone except to let later photographs of the same days join it, and
never touches one somebody made.

`AssetId` being the whole primary key of `AlbumMember` **is** the
one-album rule, enforced by the database rather than by whichever handler
happens to remember it.

`ProposalKey` is the run of days — `2019-03-03..2019-03-05` — and it is why
there is no `IsDismissed`. A proposed row is derived: the pass deletes and
reinserts it, so anything remembered against its id would be forgotten on the
next scan. Dismissing therefore deletes the row and records one rejection per
photograph against the span; the next build drops those photographs before the
group is offered, and what is left no longer earns its place. One store for one
decision, and it survives the rebuild.

---

## What it costs

Nothing to read. Every input is already in the index by the time this runs:
capture dates from [03](03-thumbnails.md), coordinates and places from
[10](10-location.md), people from [06](06-faces.md). Clustering 9,544 dated rows
sorted by time is one pass in memory.

---

## Acceptance

- [ ] A weekend away with coordinates is proposed as a named trip.
- [ ] A multi-day trip is one album, not one per day.
- [ ] A run of daily photographs longer than the cap is not offered as a single
      two-month album.
- [ ] A day at home is not called a trip.
- [ ] A group with no coordinates is still proposed, named by people or by date.
- [ ] Photos with no capture date are left out of proposals, not misfiled, and
      can still be added by hand.
- [ ] A photograph added to a second album leaves the first, and the app
      says so.
- [ ] Rejecting a photograph from an album is remembered, and does not stop
      it being proposed elsewhere.
- [ ] Dismissing a proposal is remembered.
- [ ] A rename survives a rebuild.
- [ ] An album made by hand survives a rebuild untouched.
- [ ] Rebuilding after adding a folder does not duplicate existing albums.
- [ ] Nothing on disk is moved or renamed.

---

## Out of scope

Sharing. Slideshows and music. Anniversary or "on this day" resurfacing.
Learning from what the user opens. Albums spanning years — a holiday is an
occasion, not a theme. Reading capture dates out of video containers, which is
what would let videos join an album.
