# 10 — Location

**Status: ✅ Done — and the gazetteer did not arrive through [09](09-models.md)
as planned below. It is compiled into the executable instead; see "Naming".**

Where a photo was taken, and the name of that place. No model is involved.

---

## Goal

Record coordinates from the files that carry them, and turn coordinates into a
place a person would recognise.

## Depends on

[03 — Thumbnails](03-thumbnails.md) — extraction rides the one read that pass
already does, so it costs no extra pass over 24.8 GB.

---

## What is already known

`Asset.Latitude` and `Asset.Longitude` were added on 14 August by the
`AddGpsCoordinates` migration, and for two days nothing wrote them.

The 39% figure this PRP was planned around **was wrong, and the truth is less
than half of it.** Two samples of 200 photographs gave 16.0% and 18.5%; the pass
over the real library settled it:

| | Planned for | Measured |
|---|---|---|
| Photographs carrying coordinates | 39% | **17%** |
| Photographs carrying none | 61% | **83%** |

That ceiling is the single most important fact here, and it is **worse than the
feature was planned around**: location can describe about **one photograph in six**
on this library, not four in ten. Everything built on it has to be useful when it
is absent — not merely not crash.

[11](11-albums.md) inherits this, though not one-for-one. An album is
placeable when **any one** of its photographs carries coordinates, and a
album is at least eight photographs, so the rate that matters there is better
than one in six — but it is not measured, and the name ladder has to carry the
weight either way.

Of the photographs that do carry coordinates, **100% resolved to somewhere within
3.6 km.** The gazetteer's ceiling is not the problem; the cameras are.

---

## Two halves, and only one is obvious

**Extraction** is EXIF parsing. It is nearly free: the prepare pass already
opens every photo once and already pulls the capture date out of the same
metadata. Adding two more fields to that read costs nothing measurable.

> The trap is already recorded in [03](03-thumbnails.md): EXIF sits at a
> different metadata path in HEIC (`/ifd/exif/...`) than in JPEG
> (`/app1/ifd/exif/...`). Getting that wrong silently cost all 864 HEIC photos
> their date *and* their rotation. GPS lives under the same split, and a
> coordinate that silently never appears looks exactly like a camera without
> GPS.

**Naming** is the hard half, and it is a data decision rather than a coding one.
Coordinates are `3.4239, 101.7930`. A person says "Genting".

| | Offline gazetteer | Online lookup |
|---|---|---|
| Works with no internet | ✅ | ❌ |
| Sends the user's coordinates anywhere | never | every unknown place |
| Size | tens of MB for populated places | nothing |
| Accuracy in a city | good to the district | good to the street |
| Rate limits, terms, an API key | none | all three |

**Choose the offline gazetteer.** This app works with no internet by design, the
data is a photo library's whereabouts for twelve years, and district-level
accuracy is all an album name needs.

> **As built, it does not install at all.** The plan above was to treat a places
> dataset as a model — size, licence, verification — and put it through
> [09](09-models.md). Trimming GeoNames' `cities500` to the six columns this app
> reads takes it from 38.8 MB to 9.3 MB, and Brotli takes that to **2.8 MB**,
> which is small enough to compile into the executable. That spends 3.06 MB on the
> exe to delete a manual download step entirely: place names work on a library's
> first run, with an empty working folder and no network. Nothing to verify,
> because there is nothing a user could have supplied the wrong version of.
> The manifest records the deviation and says why. See
> [gazetteer.md](../gazetteer.md); CC BY 4.0 requires the credit, which is in
> About.

---

## Rules

- Coordinates are recorded when the file carries them and left null when it does
  not. Absent is a fact, not a failure.
- A coordinate is never invented, interpolated from neighbours, or guessed from
  a folder name.
- Place names are resolved once and stored, so the gazetteer is not consulted
  again for a photo that already has an answer.
- Reverse geocoding never blocks the crawl. It is a separate step over rows that
  have coordinates and no name yet, resumable like every other pass.
- Nothing leaves the machine.

---

## Behaviour

- Extraction happens inside the existing refresh. **Naming is its own pass**, and
  the plan above that it would "follow in the same pass" was wrong: the preparing
  pass reads an original only when its tile is missing, so in an established
  library where every photograph already has one, it would never open those files
  again and naming would never run.
- Its own pass, but no longer its own button: it is a phase of a scan, running
  straight after the pictures are made. It is the only phase that needs the
  sources, and the crawl has just proved they are reachable — so it goes early,
  where a stop later costs none of it.
- A photo's details show the place name when there is one, and say nothing at
  all when there is not — no "Unknown location" label on 61% of a library.
- Places become a way to browse alongside folders, and a term the search box
  understands.

---

## Contracts

```
Asset.Latitude, Longitude    written by the preparing pass
Asset.PlaceId                null when unknown
Asset.LocationReadUtc        the marker; written only by the locating pass
Place                        Id, Name, Region, Country, Latitude, Longitude
IGeocoder                    Resolve(lat, lon) -> GazetteerPlace?   offline, refuses beyond 30 km
LocatePhotosHandler          the pass; resumable and stoppable
FindPlacesHandler            what the search box asks, place and country scope
IThumbnailGenerator          already reads EXIF; returns Latitude/Longitude too
GeneratedThumbnail           gains Latitude, Longitude
```

`Place` names a **region** rather than a district, which is what GeoNames' admin1
column actually is — the level between the place and the country.

`GeneratedThumbnail` already carries `TakenUtc` out of that read. Two more
nullable doubles beside it is the smallest possible change and keeps EXIF
handling in exactly one place.

---

## Acceptance

- [x] A photograph with GPS gets coordinates on the pass that prepares it, out of
      the read that pass already does.
- [x] A HEIC photograph with GPS gets them too. `ExifQueries` holds both paths for
      every tag it reads, so the HEIC/JPEG split is answered in one place rather
      than per field.
- [x] A photograph without GPS is left null and **is not retried on every pass**.
      `Asset.LocationReadUtc` is the marker, following `FacesDetectedUtc` and for
      its reason: five photographs in six carry no GPS, so a null latitude cannot
      say whether the file was asked and had nothing or was never asked, and
      selecting on it would re-read nine thousand originals over the share for
      ever. "No coordinates" and "too far from anywhere to name" are both settled
      answers.
- [x] Coordinates resolve to a place name with no network — the gazetteer is in
      the executable, so this is true on first run.
- [x] A place name is resolved once, stored, and survives a restart.
- [x] Re-preparing a photograph does not lose its place. `PlaceId` is deliberately
      **not** written by the preparing pass: a cleared cache or a new preview size
      must not cost a photograph its place, and the only thing that clears it is
      the file's bytes actually changing — at which point the coordinates are not
      to be trusted either.
- [x] The library is fully usable with the gazetteer absent — now vacuously, since
      it cannot be absent. A file that will not open is recorded as nothing at all,
      so a share that drops mid-pass costs a retry rather than leaving photographs
      permanently unplaced.
- [x] **Two search scopes, because one is not enough.** The gazetteer names
      populated places, so a dense city resolves to its districts: Hong Kong
      photographs come back as Tsim Sha Tsui and Central, and "hongkong" matched
      nothing at all — it fell through to the description search and returned every
      Asian skyline in the library. A country scope reaches them. Places are matched
      the way a person's name already was, so one line can name a person, a place
      and a description at once, and the screen echoes back what it understood.

---

## Out of scope

A map view. Editing or adding a location by hand. Street-level accuracy.
Timezone inference from coordinates. Deriving a location for the 61% from
anything other than their own metadata.
