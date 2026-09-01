# 13 — Collections of albums

**Status: ⬜ Planned**

Photographs group into occasions ([11](11-albums.md)). Occasions group into
nothing: the albums screen is one wall of cards, and the wall only ever grows.

Measured against the real library today: **3 albums**, holding **441 of 15,805
photographs**. So the wall is not crowded yet — and that is the point at which
to decide what happens when it is. 11's own simulation over the 9,529
photographs that carry a capture date put the occasions at **180 to 230**. That
is the shape of the wall this feature is for: several hundred cards in calendar
order, in which a holiday that took three trips is three cards nobody can see as
one thing.

A collection is what holds them: *Holiday* over *Genting*, *Bali* and *Phuket*.

---

## Goal

One shelf above the album. Make a collection, put albums on it, open it and find
them side by side.

## Depends on

[11 — Albums](11-albums.md), which is the thing being grouped, and the rename
below, which frees the word. Nothing else: a collection reads no pixels, needs
no model, and runs in no pass.

---

## Step 0 — the word

`Collection` in the code is what the screen calls an album. The word this
feature needs is *collection*, and the thing one level down is holding it. So
the rename comes first, in its own commit, with no change in behaviour:
**`Collection` → `Album`** through the domain, the ports, the handlers, the
view model, the XAML binding paths and five tables.

167 files carry the word. That is a large diff to put in front of a feature, and
it buys exactly one thing: afterwards the code and the screen say the same word,
and nobody has to hold two meanings of *collection* in their head at once. The
alternative considered — a group type named `AlbumGroup` whose children are of
type `Collection` — would have made that confusion permanent instead.

Three facts make it a smaller risk than its size suggests.

**The shared file does not change.** `DecisionSet` already carries `Albums`,
`Memberships` and `Rejections`, of types `SharedAlbum`, `AlbumMembership` and
`AlbumRejection`. The sharing layer settled on *album* when it was written; this
rename brings the rest of the code to where that layer already stands. Type
names never reach the JSON — `System.Text.Json` writes property names — so no
version of the file format moves, and two machines on different builds still
agree.

**The table rename has a proven path in this repository.** `Peers` →
`KnownMachines` was written as `RenameTable` plus `RenameIndex` rather than the
drop-and-create that EF scaffolds, and verified by applying it to a copy of the
real library with a seeded row. The same method and the same verification apply
to `Collections` → `Albums`, `CollectionMembers` → `AlbumMembers`,
`CollectionRejections` → `AlbumRejections`, `CollectionRulePeople` →
`AlbumRulePeople` and `CollectionRulePlaces` → `AlbumRulePlaces`. Editing a
scaffolded migration's body is allowed; hand-authoring the file and its designer
is not.

**There is very little in those tables to lose.** 4 album rows, 441 memberships,
4 rejections, 12 rule-people rows and no rule-places. A rename that went wrong
would be caught by a count, and the migration is verified on a copy before it
touches the library.

### Four collisions to settle in the same commit

The sharing layer already owns three of the names the rename wants.
`AlbumRejection` becomes a straight duplicate; `AlbumMembership` and `AlbumMove`
land one letter from `AlbumMember` and `AlbumFileMove`. The travelling forms
take the prefix their own sibling already uses — `SharedAlbum` — and become
`SharedAlbumRejection`, `SharedAlbumMembership` and `SharedAlbumMove`. Invisible
to another machine, for the same reason as above.

The fourth is inside the application layer. `CollectionMoveResult` — what
happened when photographs were put into an album — would have become
`AlbumMoveResult`, which is already taken by what happened when an album's
originals were moved on disk. Two records of that name, in two namespaces, one
letter of context apart. The photographs one becomes `AlbumAddResult`, which is
what its own call site has always been: `AddAsync`.

`docs/prp/11-collections.md` is renamed to `11-albums.md` in the same commit,
with its links, so the documents move with the word.

---

## What a collection is

A shelf, one level deep, holding albums. Not a rule, not a saved search, not a
second kind of album: the albums do the finding, and a collection only says
which of them belong together.

- **An album is on at most one shelf.** A nullable `CollectionId` column on the
  album rather than a join table, so the rule is the schema rather than
  something a handler has to remember — the same reason `AssetId` is the whole
  primary key of the membership table.
- **A collection holds albums, never other collections.** One level is what
  answers *Holiday over three trips*. A second level answers nothing anybody
  asked for and doubles every screen state.
- **Deleting a collection leaves its albums loose**, exactly as deleting an
  album leaves its photographs loose. Nothing on a shelf is destroyed by taking
  the shelf away.
- **No pass ever writes one, so it has no `Origin`.** An album needs that column
  because the clusterer proposes albums and must know what it may touch. Nobody
  proposes a theme; there is no rebuilt form of a collection to protect.
- **A collection is not dated.** 11 put "collections spanning years" out of
  scope on the ground that a holiday is an occasion and not a theme. This is the
  theme, one level up, and its span may be a decade wide — which is why the card
  says how many albums and photographs it holds and says nothing about when.

---

## The screen

A band of collections across the top of the Albums screen, and the wall of
albums that are on no shelf below it. Two questions get two places: *which
shelf* is answered above, *which album* below, and neither has to be read out of
the other's ordering.

The band is drawn only when there is at least one collection, so a library that
never makes one sees the screen it sees today. Collections are ordered by name —
a theme has no place on a calendar, and somebody scanning the band is looking
for a word.

**A collection card is an album card.** The same 180px `AlbumCard`, the cover of
its most recently taken album, the name, and a caption reading `3 albums, 441
photos`. No badge: the band and its heading already say what these are, and a
badge on every card in a row of nothing else stops being a badge — the same
reason only a trip earns one on the wall below.

**Opening one replaces the wall with its albums**, side by side in the wrap
panel the wall already uses, under the collection's name and the back chevron
that is already there. The chevron gains a step: an album opened from inside a
collection goes back to the collection, and the collection back to the screen.
Only the top level draws the band.

**Putting albums on a shelf is one action.** Inside an open collection, *Add
albums* opens a list of every album on no shelf, with a tick against each; tick
as many as belong and press Add once. Eight albums must not be eight trips
through the album Edit panel — and that panel is already a scroller with its
Save button below the fold. Taking one off is the same list with its ticks
already set, so *Add albums* is where both directions live.

The picker is dismissible, so it joins `Dismissible()` in `MainWindow.xaml.cs`
in front of the edit and create panels, or `ModalParityTests` fails. It is a
tick-list rather than a filter-to-one, so it is not the third of the shape
`CollectionPicker` warns about; a *Collection* dropdown added to the album Edit
panel later would be, and the pair should become one type before that lands.

**A suggested album can be ticked, and ticking it keeps it.** Adding a proposal
to a shelf is a person deciding it is worth keeping, so it is accepted on the
way in and leaves the Suggested tab. Asking the user to keep it first and then
come back and find it is the procedure this design exists to avoid. The
Suggested tab itself is unchanged and never shows a band.

---

## Rules

- Nothing here moves, renames or deletes a file. A shelf is a view of the
  library, like the album it holds.
- An album belongs to at most one collection. Putting it on a second shelf takes
  it off the first, and the screen says which — the rule an album already
  follows for a photograph.
- A collection is only ever made, named and emptied by a person.
- Deleting a collection leaves its albums where they were, on no shelf.
- A collection with no albums is shown, not hidden. It is a shelf somebody made
  and has not filled yet, and hiding it would lose the name they typed.
- Two collections may not share a name, so the band never shows the same word
  twice.
- A collection's identity is minted when it is made, and a deleted one leaves a
  tombstone, so it can be shared later without a migration that rewrites rows.

---

## Contracts

```
Collection            Id, PublicId, Name, CreatedUtc, NamedUtc?, DeletedUtc?
Album                 ... + CollectionId?
```

`PublicId`, `NamedUtc` and `DeletedUtc` are carried from the first migration and
read by nothing yet. They are what [12](12-sharing.md) needs from any row that
records a decision: which shelf this is on every machine, whose name is newer,
and a tombstone so a merge from a machine that still holds it does not put it
back. Adding them now costs three columns; adding them later costs a migration
over rows that are already in several libraries.

`CollectionId` on the album, nullable, is the one-shelf rule. It is set to null
rather than cascaded when a collection is tombstoned, which is what leaves the
albums loose. Note that the album already carries a query filter on its own
tombstone and the collection carries one too; EF warns when an optional
navigation crosses a filtered entity, and that warning is the expected one.

---

## What it costs

Nothing to read, and nothing to compute. One small table and one column; the
band is one query when the screen opens, and every cover it draws is a rendition
already on disk. This is the cheapest PRP in the document.

---

## Acceptance

- [ ] The rename lands as its own commit, with the whole suite green and no
      change in behaviour.
- [ ] The rename migration is applied to a copy of the real library first, and
      the four albums, 441 memberships, four rejections and twelve rule-people
      rows are all still there afterwards.
- [ ] A decision file written before the rename is read by the build after it.
- [ ] A collection can be made, named, and appears in the band.
- [ ] Albums are put on a shelf by ticking several and pressing Add once.
- [ ] An album put on a second shelf leaves the first, and the screen says so.
- [ ] Opening a collection shows its albums side by side; opening one of those
      shows its photographs; back goes album, collection, screen.
- [ ] Only albums on no shelf appear on the wall below the band.
- [ ] Ticking a suggested album keeps it, and it leaves the Suggested tab.
- [ ] Deleting a collection leaves every album it held on the wall below.
- [ ] A collection with no albums still shows, and says how to fill it.
- [ ] Two collections cannot be given the same name.
- [ ] The band is absent when there are no collections.
- [ ] Escape closes the album picker before the panels behind it.
- [ ] Nothing on disk is moved or renamed.

---

## Out of scope

Proposing collections. Nobody can measure a theme, and a shelf the app invented
would be the first thing on this screen that is wrong in a way the user cannot
see. Collections inside collections. Dragging a card onto a shelf. Sharing
collections between machines — the identity and the tombstone are minted now so
that it needs no migration when it comes, but the merge rule and the held-answer
path are not written here. A collection as a rule, which is an album's job.
