# 11 — Collections

**Status: ⬜ Unbuilt — the reason the rest of the second half exists**

Twelve years of photos nobody has time to organise. The app should propose the
groupings itself: *"Genting Trip, 3–5 March"*.

---

## Goal

Offer collections the user never asked for, named well enough to be worth
opening.

## Depends on

[10 — Location](10-location.md) for the place, [06 — Faces](06-faces.md) for the
people. [07](07-content-search.md) is additive, not required.

---

## The clusterer is not a model

Apple and Google build these by grouping on **time and place first**, then
labelling the group with who and what is in it. Nothing here needs weights. The
signals do — faces and content — but the grouping itself is two thresholds and a
sort.

A new collection starts when either gap opens:

| | Threshold | Why |
|---|---|---|
| Time gap | more than **6 hours** with no photos | a night's sleep separates two days of a trip; a lunch does not |
| Distance gap | more than **50 km** from the group's centre | far enough that it is somewhere else, loose enough that a day out stays one place |

Then a group must earn its place:

- at least **8 photos**, or it is a handful of shots rather than an occasion;
- spanning at least **90 minutes**, or a single burst becomes a "collection";
- collections that overlap in time are merged, not offered twice.

> **Use `TakenUtc`, never `CreatedUtc`.** [Asset.cs](../../src/PhotoGallery.Domain/Assets/Asset.cs)
> records the measurement: 3,000 photos spanning eight years carry **13 distinct
> creation days**, one per bulk copy. Clustering on creation time would propose
> thirteen enormous collections named after the days the files were copied.
> A photo with no `TakenUtc` cannot be clustered on time and is left out rather
> than dropped into whichever group it lands beside.

---

## Naming, and the 61% problem

Only about four photos in ten carry coordinates ([10](10-location.md)), so a
collection is often a set of photos with no place at all. The name degrades down
a ladder rather than failing:

| What is known | Name |
|---|---|
| Place + short span | **Genting Trip**, 3–5 March |
| Place + one day | **Genting**, 3 March |
| Place spans several | **Genting and Kuala Lumpur**, 3–7 March |
| No place, people known | **A weekend with Ana Lim**, 3–5 March |
| No place, no people, content known | **Birthday**, 3 March |
| Nothing but the dates | **March 2019**, 42 photos |

A collection is only called a *Trip* when its photos sit more than 50 km from
where that period's photos usually are — otherwise every weekend at home becomes
a trip.

Never invent. If the only honest name is a month and a count, that is the name.

---

## Rules

- Collections are **proposed, never imposed**. Nothing is moved, renamed or
  deleted on disk — the app only ever reads a source
  ([01](01-photo-sources.md)).
- A proposal the user dismisses stays dismissed, and is not offered again after
  the next refresh.
- A user can rename a collection; the app never renames it back.
- Recomputing is idempotent. Adding a folder does not renumber or duplicate
  collections that already exist.
- A collection with no cover picture is not shown. The cover is the photo in it
  with a face if there is one, else the middle photo of the span.
- Building runs after the library changes, resumable and stoppable like every
  other pass.

---

## Contracts

```
Collection            Id, Name, StartUtc, EndUtc, PlaceId?, CoverAssetId,
                      Kind (Trip | Day | Event | Period), IsDismissed, WasRenamed
CollectionMember      CollectionId, AssetId
BuildCollectionsHandler   (IProgress, ct) -> CollectionsResult
```

`Kind` exists so the wording rules above are a property of the row rather than
of a formatting function that has to guess.

---

## What it costs

Nothing to read. Every input is already in the index by the time this runs:
capture dates from [03](03-thumbnails.md), coordinates and places from
[10](10-location.md), people from [06](06-faces.md). Clustering 11,481 rows
sorted by time is one pass in memory.

---

## Acceptance

- [ ] A weekend away with coordinates is proposed as a named trip.
- [ ] A day at home is not called a trip.
- [ ] A group with no coordinates is still proposed, named by people or by date.
- [ ] Photos with no capture date are left out, not misfiled.
- [ ] Dismissing a proposal is remembered.
- [ ] A rename survives a rebuild.
- [ ] Rebuilding after adding a folder does not duplicate existing collections.
- [ ] Nothing on disk is moved or renamed.

---

## Out of scope

Sharing. Slideshows and music. Anniversary or "on this day" resurfacing.
Learning from what the user opens. Collections spanning years — a holiday is an
occasion, not a theme.
