# 12 — Sharing between machines

**Status: ✅ Done — the shared folder half. The direct connection was built,
then removed.**

> **The direct connection is no longer in the app.** Everything below about
> finding each other on the Wi-Fi — the UDP beacon, the TLS link, the six-digit
> pairing code, the Public-network diagnosis — was built, tested and then taken
> out, because this house has one folder every machine already reaches and the
> fallback earned none of the room it took on the screen.
>
> **It is kept in git rather than thrown away**, and the design below is the
> reason it can come back cheaply. To restore it:
>
> ```
> git log --oneline --diff-filter=D -- src/PhotoGallery.Infrastructure/Sharing/PeerListener.cs
> git revert <that commit>
> ```
>
> The removal is one commit and touches nothing the shared folder needs, so the
> revert is close to clean. What it will not restore is anything added to the
> Sharing screen afterwards, so read the conflict rather than forcing it.

The decisions cross through a folder every machine can reach; the small copies
pool, so a new laptop is usable the same evening instead of the following week;
and the face vectors travel, refused by name when the models differ.

The two things worth remembering about the shape of it: the merge is a pure
function of decision sets, so every rule about two machines disagreeing was
argued out against tests rather than against two real network shares; and
answers about photographs a machine has not indexed are held rather than
dropped, which is what makes the order of scanning and sharing impossible to get
wrong.

The app is installed on every laptop in the house. One person spends an evening
naming faces; everybody else's copy knows nothing about it. This is how that
evening's work reaches the other machines — and how it does so without moving a
single photograph.

---

## Goal

Keep the *answers* in step: who is in a picture, what an album is called, which
way up a photograph goes. Nothing else. Every machine keeps its own index, its
own thumbnails and its own vectors, and none of them is authoritative.

## Depends on

[06 — Faces](06-faces.md) for the people, [11 — Collections](11-collections.md)
for the albums, [02 — Scanning](02-scanning.md) for the path that identifies a
photograph, and [03 — Thumbnails](03-thumbnails.md) for the content hash the
pooled pictures are named after.

---

## The measurement that decides the whole shape

Everything in the index is one of three things, and the difference is the entire
design. Measured on this library, 15,823 assets:

| | Size here | Cost to make it again on another machine |
|---|---|---|
| **Decided** — names, confirmations, albums, turns | **469 KB** | cannot be made again at any price |
| **Derived, expensive** — renditions and the facts that came with them, face vectors, content vectors | 2.0 GB + 1.1 MB + 40.5 MB + 34.0 MB | ~1 h reading 24.8 GB over the share, then ~2 h of face detection and ~1 h of describing |
| **Derived, cheap** — places, duplicate sets, proposed albums | 69 rows, 702 sets | seconds, from data already held |
| **Originals** | **291.8 GB** | they are the photographs; they are not derived from anything |

The 469 KB is not an estimate. The payload was built from this library — 9,455
human answers about 5,906 photographs, 15 people, 25 eras, 6 turns — serialised
and compressed: 1.23 MB of JSON, 419 KB gzipped, plus 50 KB of era centroids.

> **Twelve years of naming faces weighs 469 KB.** The index it lives in is
> 139 MB and the pictures it describes are 291.8 GB. That ratio — roughly
> 1 : 300 : 600,000 — is why this feature sends decisions and nothing else by
> default, and why it is worth building at all.

---

## Two ways to reach the other machine, and which one first

The obvious reading of "sync between laptops" is a direct connection: find each
other on the Wi-Fi, open a socket, exchange. That is designed below, because it
is the only answer for two machines that share no storage. It is **not** what
gets built first.

### What this house actually has

This library's photo sources list holds exactly one entry:
`\\192.168.50.103\PhotoGallery`. The family already shares a network folder,
every machine already reaches it, and it is the reason they see the same
photographs at all.

| | Shared folder | Direct connection |
|---|---|---|
| Other laptop must be switched on | **no** | yes |
| Other app must be running | **no** | yes |
| Both people present at once | no | no, but the app must be open on both |
| Firewall prompt | none | on every machine, first run |
| Works on Wi-Fi set to "Public" | **yes** | no — inbound is blocked outright |
| Guest / AP-isolated Wi-Fi | **yes** | no |
| Three or more machines | free | pairwise |
| Code to write | one file written, one file read | discovery, pairing, TLS, framing, a listener |

The deciding row is the first one. **A family will not get two laptops open at
the same moment on purpose.** A design that requires it is a design that gets
used twice and then forgotten, and this app's standing rule is that a feature
must not be a procedure somebody has to remember.

So: a **shared folder** is the mechanism, and the direct connection is the
fallback for a machine that shares no folder with anyone. Both sit behind one
port, `IDecisionExchange`, so the second is additive rather than a rewrite.

### The shared folder

Nominated by the user, once, on the Sharing screen. Each machine writes one file
and reads everybody else's:

```
<shared folder>/
  answers/
    3f2a...8c1d.json.gz        one per machine, rewritten whole
    3f2a...8c1d.json.gz.tmp    written, flushed, then renamed
  thumbs/                      optional; the sharded store, pooled
  vectors/
    3f2a...8c1d.faces.bin      optional
```

Written whole and renamed into place, so a reader never sees half a file. There
is no locking and none is needed: one writer per file, and a reader that catches
a rename sees the old copy and picks up the new one next time.

> **The shared folder and the photo sources must not contain one another, and
> the check runs in both directions.** The pooled thumbnails are `.jpg` files in
> a folder tree; a scan would index them as photographs, and the library would
> grow a second copy of itself every time anybody pressed Refresh.
> `WorkingFolder.IsAppOwned` exists for this class of mistake already — the same
> check, pointed at the sources.
>
> One direction is not enough, and it is the easy mistake to make here. Refusing
> a shared folder inside a source leaves the reverse wide open:
> `AddPhotoSourceHandler` compares a new source against `IsAppOwned` and against
> the *other sources*, and knows nothing about the shared folder — so nominating
> the folder correctly and then adding a source one level above it is permitted,
> and produces exactly the outcome the first half of the rule exists to prevent.

**The screen says who is up to date, because nobody is ever online together.**
The shared folder's whole advantage is that it does not need two laptops on at
once — which also means presence, in the usual sense, is not a thing it can
report. What it can report is recency, and every answers file already carries
the `WrittenUtc` to do it:

> *Mum's laptop — up to date · Dad's laptop — 3 weeks ago · Ana's laptop —
> never shared*

That is the honest form of "is everybody in step?" for a mechanism where being
online is beside the point, and it costs nothing: the timestamp is in the
payload, and reading it is a directory listing. Without it a decision set
written six months ago by a laptop now in a drawer merges exactly like one
written an hour ago, and the user has no way to tell.

### Finding each other directly

For the machine with no folder in common. Answering the question properly even
though it is the second half of the work:

- **A beacon on UDP multicast**, group `239.255.42.7` port `41871` — the
  administratively-scoped local range, so it does not leave the house. The
  packet carries a machine id, a display name, the app and schema versions, the
  TCP port, and the certificate fingerprint below. It carries nothing about the
  library. No dependency: `UdpClient` joins a multicast group in four lines, and
  this ships as one self-contained file where an mDNS package is a download and
  a second thing to be wrong.
- **Listen always, beacon only while the Sharing screen is open.** The other way
  round makes it a two-person job — both screens open at once — and an app that
  announces itself on the family network for ever is not something to put on
  somebody else's laptop. So the person who wants to share opens the screen,
  their machine calls, and the quiet ones answer.
- **The transport is `TcpListener` and `SslStream`**, with a self-signed
  certificate generated once and kept in the working folder. Not `HttpListener`:
  on Windows it will not bind anything but `localhost` without a `netsh urlacl`
  reservation made as administrator, which is an installer, which this app does
  not have.
- **Pairing is a six-digit code**, shown on the machine that is offering and
  typed on the one accepting. Both sides derive a check value from the code and
  *both certificate fingerprints*; a mismatch aborts. That binds the code to the
  channel, so somebody else on the Wi-Fi cannot sit in the middle. Afterwards
  the peer is remembered by fingerprint, and a fingerprint that changes means
  pairing again rather than a silent accept.

**Two things will stop it working and both must be said on screen, not
discovered.** Windows raises a firewall prompt the first time the app listens,
and a network profile set to Public — which is what Windows chooses by default,
and what a great many home networks are left as — blocks inbound traffic
entirely, so discovery finds nothing and the screen has to say why rather than
showing an empty list. Guest and AP-isolated Wi-Fi block machine-to-machine
traffic outright and no setting in this app can change that. In every one of
those cases there is a typed address as the way through, and the shared folder
as the way round.

---

## The precondition: the same sources, or nothing to talk about

Two machines share decisions **about the sources they have in common**, matched
by source root. Everything below rests on that, and it is worth stating as a
precondition rather than discovering as a failure.

It is not a limitation invented to make the design easier — it is what this
house already has. The library's source list holds one entry,
`\\192.168.50.103\PhotoGallery`, and every laptop reads the same one. Where two
machines have nothing in common there is nothing to say, and the screen says
that rather than reporting an empty exchange as a success.

**Per source, not per library**, because the alternative is worse than it looks.
Refusing two machines whose source lists differ at all would mean that a family
member who adds their own phone-dump folder can no longer share anything,
including all the work on the shared drive. Scoping to what overlaps costs a
join and keeps private folders private.

> **Matching roots is not string equality, and must not be built as though it
> were.** `\\192.168.50.103\PhotoGallery` on one laptop and `Z:\` on another are
> the same folder reached two ways, and Windows offers to map that drive letter
> for you — so comparing the text would lock out a family member for doing a
> normal thing, with nothing in the app to undo it. A manifest names its roots,
> obvious pairs are proposed, the user confirms once, and the pairing is
> remembered against the source.
>
> What must never be absorbed silently is the other shape:
> `\\...\PhotoGallery` against `\\...\PhotoGallery\Photos`, where relative paths
> differ by a prefix, every match misses and the exchange looks merely empty.
> `AddPhotoSourceHandler` catches that within one machine and cannot see across
> two, so it is reported here instead.

Once a pair is confirmed the two sources share one `SharedId`, and **that** —
never a root path — is what a decision is scoped by. A root is machine-local
text; putting it in a key would rebuild the drive-letter problem one layer down.

---

## What a photograph is called on the other machine

Row ids are local and meaningless across machines. A decision has to name its
photograph in a way both indexes recognise — and the answer is already in the
app, rather than needing to be invented for this feature.

| | Key | Measured on this library |
|---|---|---|
| Photograph or video | **its path below the source root** | 15,823 of 15,823 distinct — it is the primary key of a scan |
| Face | its photograph's key plus the detector's box | 19,763 of 19,763 distinct; the busiest photograph holds 32 |
| Person | a `Guid` minted when the person is created | — |
| Album made by hand | a `Guid` minted when it is created | — |
| Album the app proposed | its `ProposalKey` — the run of days | — |

**The path, because that is already what the app itself means by "the same
file".** `ScanPhotoSourceHandler` holds what it knows in a dictionary keyed on
the relative path and a set of the paths it saw; length and modified time are
read only to decide whether a file has *changed*, never which file it is. A
photograph moved to another folder is therefore already, today, a row removed
and a row added.

An earlier draft of this document keyed decisions on the content hash instead,
on the grounds that it survives more. It does — and that is the problem.
**Sharing must not have a stronger notion of identity than the scan it rides
on**, or the two disagree about what happened whenever a file moves, and the
divergence is invisible. Matching the scan is worth more than being cleverer
than it.

It also removes a real cost. A content hash is only known *after* a photograph
has been decoded, so a machine that had not yet prepared a picture could not say
what a decision was about; and a video is never hashed at all, because hashing
this library's videos means reading 267 GB at 6.4 MB/s — the eleven hours
[08](08-video.md) exists to avoid. Path identity gives photographs and videos
one rule instead of two, known from the crawl, before anything is opened.

**Length and modified time still travel, as a change detector rather than a
name.** A file re-saved in place keeps its path and gets new bytes: same
photograph, different picture. The decisions still apply — a name on a person
does not stop being true because a file was re-encoded — but its rendition must
not be pooled, because those pixels are no longer what the other machine is
looking at. So a mismatch means *re-derive*, not *no match*, which is exactly
the distinction a content hash could not express.

**The content hash does not disappear; it stops doing the wrong job.** It
remains what a rendition file is named after, which is what lets the pool work
at all. Two identities with two purposes: **the path says which photograph, the
hash says which picture.**

**A face is identified by its box, not by an ordinal.** Two machines running the
same detector over the same rendition produce the same rectangles; ordering is
an artefact of how the results were collected and is not worth trusting. Where
the boxes differ slightly — a different model version, which the guard below
should have caught — the match falls back to the best overlap above half, and
anything below that is a face this machine does not have, so the answer is held.

That rests on something the code does not say out loud and this document
therefore has to. `OnnxFaceScanner` appends no execution provider, so every
machine runs the same CPU graph over a preview this app wrote itself, and the
rectangles are reproducible. **The day a GPU provider is added, they stop
being** — floating-point differences move a box by a pixel and the exact match
silently degrades to the overlap fallback. Recorded under the standing risks
below rather than discovered later.

**A turned photograph moves its own boxes, and the order of the merge is what
saves the key.** `TurnPhotoHandler` rewrites every face's bounds through
`FaceBounds.TurnedClockwise` so they still land on the turned picture. So a box
means nothing on its own: confirm a name, turn the photo, and the box that was
`X` is now `Y`, while the machine that never turned it still holds `X`.

The first draft answered this by carrying the rotation inside every face key and
normalising before matching. It is not needed, and the reason is the rule that a
turn is settled by whoever turned it last:

> **Rotations merge before face answers do.** Once both machines have agreed the
> turn and each has moved its own boxes through `FaceBounds.TurnedClockwise` —
> the same pure arithmetic over the same pre-turn dimensions — the boxes are
> already in the same frame, and the plain `(path, box)` key matches exactly. An
> ordering rule instead of a wider key.

It composes with a machine that has not detected faces yet: it turns nothing,
then detects on the already-turned preview and gets the turned boxes directly.
Two 90° turns and one 180° turn have to land in the same place for this to hold,
which is arithmetic `FaceBounds` already owns and which is worth a test of its
own.

**Two copies of one picture at two paths are two photographs, and a name given
to one does not reach the other.** Under content-hash identity they would have
been one, which sounds better until it is the same rule that makes a moved file
lose its names. This is the price of matching the scan, it is the smaller of the
two prices, and the app already has a feature whose whole job is telling you
about the second copy. It does not arise on this library in any case: no two
photographs here share a hash, and all 702 duplicate sets are perceptual
near-matches rather than exact ones.

---

## What travels, and what conspicuously does not

### It travels

| | Rows here | Why it cannot be re-derived |
|---|---|---|
| People — name, birth year | 15 | somebody typed it |
| Confirmations and rejections | 9,374 | somebody looked at a face and answered |
| Faces marked as nobody | 81 | the same |
| Turns | 6 | the file does not say which way is up |
| Albums made by hand, and their contents | 2 | somebody built them |
| Renames of proposed albums | — | keyed on the `ProposalKey`, because the row is rebuilt |
| Dismissals and per-photograph rejections | 0 | somebody said no |
| Era centroids | 25 (50 KB) | derived — but see below |

**Proposals do not travel.** 1,359 of this library's 10,733 assignments are the
app's own guesses; the other machine will make its own, and better ones, from
the confirmations it has just received. Sending a guess as though it were an
answer is how one wrong proposal becomes permanent across a whole family.

**Era centroids travel although they are derived**, and this is the one
deliberate exception. An era is the mean of the confirmed faces in a stretch of
time, so a machine rebuilds it from what it holds — but it can only hold
confirmations about photographs it can see. Where somebody has named a person in
two hundred pictures and half of them live only on their own laptop, the other
machines rebuild a weaker centroid and propose worse. Fifty kilobytes closes
that. The rule: **eras are rebuilt locally after every merge, exactly as they
are today, and a received centroid is kept only where the rebuild produces
nothing for that person and that stretch of time.** It is a seed, not a fact,
and the first local confirmation in that era replaces it.

### It does not travel

**Places, duplicate sets, perceptual hashes and proposed albums** are all
re-derived in seconds from data the machine already has. The gazetteer is
compiled into the executable, so naming 69 places costs nothing.

**Quarantine does not travel, because the files already do.** Setting a
duplicate aside moves it out of the shared folder into one machine's working
folder; the next scan on every other machine finds it gone and reconciles. A
synced quarantine flag would be a second, slower, less reliable copy of
something the file system has already said.

That reconciliation is where sharing changes an existing risk, and it needs a
rule of its own.

### Deleting is fine. Restoring from quarantine is not.

A deleted photograph takes its names with it on every machine, and that is
correct. The file is gone from the shared source, every scan reaches the same
conclusion independently, and `AssetToRemove` already says the trade is fair:
the names *"describe a photograph that will not be there"*. Nothing needs to be
built for it, and an earlier draft of this document was wrong to imply otherwise.

**Quarantine is the case that is not deletion, and it breaks asymmetrically.**
Setting a duplicate aside moves the file off the shared drive into one machine's
working folder, and the scan on *that* machine deliberately keeps the row:

> *"A copy set aside as redundant is meant to be absent. Its row is the only
> thing that knows how to put it back, so a scan that took it away would make
> the quarantine a one-way door."*

That guard protects only the machine that did it. Everybody else sees a file
that has simply vanished, with no quarantine flag on their row — because
quarantine does not travel — and removes it. Restore later and the person who
quarantined it gets their photograph back with its names intact, while the other
three laptops re-add it as a fresh row that nobody has ever named. **The one-way
door the app went to trouble to prevent is still there; it just moved to the
other machines.**

The fix costs nothing, because the mechanism already exists for the opposite
case:

> **A removal parks that photograph's decisions as held rather than deleting
> them**, keyed by path. If the file comes back, the names come back with it. If
> it never does, a few hundred bytes sit in a table.

This is an answer waiting for its photograph, which is exactly what held
decisions are — the same code, run from the other direction. It also quietly
covers the ordinary accidents that look identical to a deletion at scan time: a
folder moved and moved back, a drive remounted, a tidy-up somebody undoes.

**Originals never travel.** See below.

---

## Merging, and the convention that turned out to be enough

There is no server and no authority. Two machines can disagree, and the merge
has to settle it without asking nine thousand questions.

**Last decision wins, by when it was decided.** That needs a date on every human
answer — and the app very nearly has one already. Its own convention, adopted so
that decisions could be reviewed and undone, is *"a date rather than a flag"*:
`IgnoredUtc` on a face, `QuarantinedUtc` on an asset, `RejectedUtc` on a
collection rejection, `AddedUtc` on a membership. A convention chosen for undo
turns out to be exactly what merging needs. Three things still carry a flag
where they should carry a date, and that is most of this feature's schema cost.

| Disagreement | What happens |
|---|---|
| Two answers about one face | the later one |
| A confirmation against a proposal | **the confirmation, whenever it was made** |
| A confirmation against "nobody" | the later one — both are human answers |
| Two names for one person | the later rename |
| Two people, same name, different ids | kept apart, and offered as a join |
| One photograph in two albums | the later `AddedUtc`, and the app says which album it left |
| An album deleted here and present there | the deletion, if it is later |
| A photograph turned two ways | the later turn |

**A person's answer never loses to the app's guess, whatever the clock says.**
That exception exists because clocks are the weak part of last-write-wins: they
are close enough on NTP-synced laptops for two human answers minutes apart, and
not something to bet a confirmed name on against a machine-generated proposal
that happened to be written later.

**And a clock that is simply wrong has to be caught, because otherwise it wins
for ever.** A laptop whose date is a year ahead — which is an ordinary thing for
a machine that sat in a drawer with a flat battery — stamps every answer it
makes into the future, and quietly overrides everybody else's on every merge
from then on, including answers made long after. Nothing about the result looks
broken; it just always agrees with one machine. So a payload whose decisions are
dated implausibly far ahead of this machine's own clock is refused, and the
screen says which machine and by how much, rather than merging it and being
subtly wrong for a year.

**Two people with the same name are not joined automatically.** Two machines
that each created "Ana" independently produce two ids, and two Anas is a real
thing in a family. But the app can see when they are the same person, using the
similarity it already trusts: after a merge, any two people whose era centroids
agree are offered as a join. Offered, on screen, with faces — not performed.

**Deletions need a tombstone.** Without one, a person deleted here is quietly
restored by the next merge from anybody who still has them. So `DeletedUtc` on a
person and on an album made by hand.

**And a tombstone is kept for ever.** It is the only record that something was
deleted rather than never known, so tidying the table away — an obvious thing to
write for rows that only accumulate — lets a deleted person walk back in from
the next machine that still holds them, and then propagate. Fifteen people is
not a table that needs tidying; the held decisions get an explicit *"no reason
to expire them"* and tombstones need it more, because for them expiry is not
merely wasteful but wrong.

**Unnaming a face is its own answer, not a rejection.** The row has to survive —
a state-based merge cannot tell an absent row from one that was never there — but
routing an unname through `AssignmentSource.Rejected` would borrow a meaning that
is narrower than it looks. `Rejected` is documented as *"Kept so the same wrong
proposal is not made twice"*: it suppresses that person for that face, for good.
Somebody who clears a name because they picked the wrong one, or simply wants to
start again, would find that person could never be proposed there again — on
every machine, after the merge. So unnaming sets a fourth `AssignmentSource`,
`Cleared`, with its own `DecidedUtc`: a human answer that merges like any other
and suppresses nothing.

---

## When the other machine has not scanned yet

This is the question the feature lives or dies on, and there are two different
situations hiding inside it.

### The photographs are there; the index is not

Everybody points at the same shared folder, so the pictures are already on the
other machine's disk — its index just has not been told. The fix is a scan, not
a transfer. Reading a folder's metadata costs 45 seconds for 17,023 files; the
expensive part is renditions and faces, and those are that machine's own work to
do whenever it likes.

**Answers about photographs this machine has not indexed are kept, not
dropped.** They are parked against their key in a `HeldDecision` table and
applied the moment a scan brings that photograph in. This is the single most
important merge rule in the feature, because without it the order of operations
becomes something the user has to get right — scan first, then share, and if you
did it the other way round you silently lost an evening's work. With it, the
order does not matter and cannot be got wrong.

Held decisions are small: a key, a kind and a payload. Nine thousand of them is
about a megabyte, so there is no reason to expire them.

The screen says so plainly rather than merging in silence:

> *3,412 answers are waiting for photographs this library has not indexed yet.
> Scanning will bring them in — about 20 minutes.*

— with one button that scans and then applies them. One action, not a procedure;
but it says what it costs before the click, because a share that quietly starts
a ninety-minute face pass is worse than one that asks.

### The photographs are only on the other machine

Somebody's phone dump is on their own laptop and nowhere else. Their machine has
answers about pictures no other machine can see.

**The answers are still carried and still held.** They cost nothing and they
come good the day those photographs reach the shared folder.

**The photographs themselves are not transferred, and this is a decision rather
than an omission.** Copying originals between family laptops turns a photo
indexer into a file replicator, and this app's founding rule — *the app only
ever reads a source; nothing on disk is moved, renamed or copied* — is what lets
every one of its passes be safe to stop at any moment. It also cannot be done
honestly at this scale: 291.8 GB, 91% of it video, over a 6.4 MB/s link.

What the app does instead is **say what it can see and where the rest is**:

> *Dad's laptop has 812 photographs this library does not, all in
> `2026 Phone Dump`. Put that folder on the shared drive and they will be here
> properly — originals, backed up, at full size.*

One instruction, and it produces a better outcome than any amount of copying:
the photographs end up somewhere everybody can reach, rather than as
half-resolution ghosts on four machines.

---

## Carrying the pictures themselves — previews, never originals

Small previews *do* travel, and this is the second half of the feature rather
than a footnote to it. The arithmetic is one-sided enough to make it the reason
a new laptop is usable the same evening instead of the following week.

| | Size here | Made again locally | Copied over the share |
|---|---|---|---|
| Renditions — tile 400px and preview 1024px | 2.0 GB | ~1 h reading 24.8 GB at 6.4 MB/s, plus the decode | **~5 min** |
| Prepared facts — the rest of what that decode learned | 1.0 MB gz | — comes only from that same read | seconds |
| Keyframe index | 105 KB gz | — | seconds |
| Face vectors | 40.5 MB | ~2 h — 676 ms × 11,080 | seconds |
| Content vectors | 34.0 MB | ~1 h | seconds |

**A new machine can be made complete from about 2.1 GB without opening a single
original.** That is the claim worth building for: roughly four hours of reading
and computing, replaced by five minutes of copying, and the 24.8 GB of
photographs never touched.

### Why renditions pool with no mapping at all

The thumbnail store already names its files after a hash of the original's bytes
rather than a row id — a decision made in [03](03-thumbnails.md) so a source
could be detached and re-added without orphaning 25 GB of work. A name is the
first 32 characters of that digest plus `.jpg`, sharded two deep, with the
1024px preview beside the tile under the same name suffixed `-p`:

```
thumbs/00/0097ef33749c51f1bafca40e39ceee6a.jpg      tile     400px
thumbs/00/0097ef33749c51f1bafca40e39ceee6a-p.jpg    preview 1024px
```

Two machines can therefore pour their thumbnails into one folder and cannot
collide: same name means same bytes means same picture. Copying is "take the
names I do not have", which is idempotent, resumable and stoppable like every
other pass. A picture whose bytes change gets a new hash and so a new name, so
nothing in the pool is ever overwritten and "latest" needs no version at all —
it is simply whichever names are there now.

**That claim is only true of renditions nobody has turned, and the pool has to
enforce it.** `IRenditionTurner` rewrites both files *in place*, under a name
derived from the original's bytes — which the turn does not change. So a
straightened photograph on one machine and the same photograph on another are
one name over two different pictures, and "take the names I do not have" would
hand somebody a sideways tile at random.

Three rules make a turn safe to share, and only the first of them was obvious:

> **1. Only rows with `Rotation == 0` are published. Fetching is
> unconditional.** A machine that has already merged a turn still needs the
> as-generated rendition — it is the only one the pool holds — and turns it
> itself once it has it. Forbidding the fetch too would leave exactly the
> photographs somebody cared enough to straighten falling back to an hour of
> reading originals.
>
> **2. Renditions arrive before turns are applied, and a turn that finds no
> rendition is held rather than lost.** `WindowsRenditionTurner` reads the
> preview off disk; a picture not yet fetched throws, `Turn` answers null, and
> `TurnPhotoHandler` then deliberately records nothing — *"a rendition that
> could not be read leaves the library exactly as it was"*. Right locally, and
> silent disaster here: a fresh machine merges every turn before it owns a
> single rendition, drops all of them, and then publishes its own
> `Rotation == 0` as a competing answer. So a turn with no picture yet waits
> with the held decisions and is applied when that picture lands.
>
> **3. A merged turn moves renditions only. It never writes the original.**
> `TurnPhotoHandler` also calls `IOriginalOrientation.TryTurn`, which opens the
> file `ReadWrite` with `FileShare.None` to write the EXIF tag. That is correct
> when a person clicks Turn on their own machine, and wrong for every machine
> afterwards: four laptops merging one decision would queue for an exclusive
> write on one file on the share, and each that won would change its modified
> time — the change detector — invalidating that photograph's rendition for
> everybody, repeatedly. The person who turned it has already told the file.
> Sharing carries only what the file would not take.

**Two writing rules, both borrowed from passes that already needed them.** Files
are copied into the pool and out of it through a temporary name and renamed into
place, because two machines will fetch the same missing rendition at the same
moment and a third must never read half a JPEG. And the **preview is written
before the tile**, because `IThumbnailStore.Exists` asks only about the tile: a
copy interrupted between the two would otherwise leave a photograph that reports
itself complete and has no preview — which is the file the viewer opens and the
face detector reads. This is the same "poster last" discipline the video pass
already keeps, for the same reason.

### The asymmetry between photographs and videos

**A video's frame names can be worked out; a photograph's cannot.**
`VideoKeyframeIdentity` seeds its digest from the path, the length, the modified
time and the frame ordinal — all of which a machine knows from its own crawl,
for free, before decoding anything. So for 4,743 videos the receiving machine
computes the four names it wants and fetches them.

A photograph's rendition is named after a hash of its bytes, and **the bytes are
exactly what the receiving machine is trying to avoid reading**. It cannot name
the file it wants. That single fact is why the pool needs a manifest.

### The manifest is the whole result of the preparing pass, not a file list

The decode that produces a thumbnail is also the only moment the app learns the
capture date, the dimensions, the GPS coordinates and the perceptual hash — the
reason `GeneratedThumbnail` carries all of them. **A machine that copied the
pictures but not those facts would get a library with no timeline, no places and
no albums**: 9,544 capture dates and 1,709 sets of coordinates missing, and
[11](11-collections.md) with nothing to cluster.

So what is published beside the decisions is every answer that read produced:

```
PreparedFact   Path                           <- the name: what the scan means
               Length, ModifiedUtc            <- the change check, not the name
               ContentHash, ThumbnailName     <- which files to fetch
               Width, Height, TakenUtc, Latitude, Longitude,
               PerceptualHash, Duration, Status
```

Measured on this library: 15,823 rows, 4.20 MB of JSON, **1018 KB gzipped**,
plus 105 KB for the 4,729 keyframe rows. One megabyte to skip an hour.

`Status` rides along so the 12 files that will never decode are not read again
on four more machines, which is the same reason it exists locally.

### What the receiving machine does

For each asset its own crawl has already indexed but not yet prepared, it looks
the path up in the manifest, fetches the two named files into its own thumbnail
store, writes the facts onto its row, and marks it Ready. The generating phase
then finds nothing outstanding and the whole hour is gone.

Two rules keep this safe:

- **A picture is only taken when the file agrees, byte for byte and to the
  second.** The path says which photograph, but the pooled rendition is of
  particular *bytes*, and if this machine's copy has a different length or
  modified time then the picture in the pool is not the picture here. It is
  prepared locally instead. This is the one place the change detector is
  load-bearing rather than advisory: getting it wrong shows the wrong picture,
  silently, and the person looking at it has no way to tell.
- **A rendition is only ever accepted for a row that already exists.** The pool
  never creates an asset. Without that rule the app would grow a new state —
  a photograph it can show but whose original it cannot reach — and every
  screen, the duplicates pass, quarantine, turning and "show in Explorer" would
  each have to learn about it. With it, the pool only ever fills in rows a scan
  from this machine's own sources already made, and nothing else in the app
  changes. Facts about photographs this machine has not indexed are held
  alongside the decisions, exactly as everything else is.

### Vectors, and the model that has to match

Face and content vectors are accepted only from a machine running the same model
files. An embedding is meaningless outside the model that produced it, and a
mismatched one does not fail — it returns a confident answer about the wrong
person. The payload names each model and its file hash; a mismatch refuses the
vectors, accepts the decisions and the renditions, and says which model differs.
`ModelId` and `IModelStore` already carry what is needed to answer that.

**A face vector arrives as a face, or the two hours are not saved.** This is the
one place the pool's own rule — fill in rows, never create them — does not
transfer, and saying so is the difference between the 40.5 MB buying something
and buying nothing. A machine that has never run detection has no `Face` rows at
all, so there is nothing for a vector to attach to; if it does not create them
it runs the full pass anyway and the transfer was decoration.

So, for a photograph this machine has already indexed, received faces **are**
inserted — bounds, score and embedding — and `Asset.FacesDetectedUtc` is stamped
with them. The stamp is not decoration either: `DetectFacesHandler` selects on
it, and rows left null would be read and re-detected on the next scan, which is
the whole two hours coming back. Faces for a photograph this machine has *not*
indexed are held, exactly as answers about it are.

The asset rule and the face rule differ because the questions differ. A
rendition without a row would be a picture whose original the app cannot reach —
a new state every screen would have to learn. A face without a row is just a
face, in a photograph this machine already has, found by the same model over the
same pixels.

### What it costs to keep

The pool grows to the union of everybody's libraries and never shrinks by
itself, because nothing in it is ever overwritten. Against 291.8 GB of
photographs on the same storage, 2.0 GB is not a number worth managing — but
there is a tidy that removes names no machine's manifest claims any more, for
the day somebody detaches a source for good.

### It is offered, with its price, not switched on quietly

The first copy is 2.1 GB and several minutes. That is exactly the kind of thing
this app says before the click rather than after: the screen names the size and
the time, one button starts it, and the answer is remembered. Afterwards each
share carries only what is new.

---

## Rules

- **Originals are never sent.** Not by request, not as an advanced option. The
  400px tile and the 1024px preview travel; the photograph does not.
- **A rendition is only accepted for a row this machine's own scan already
  made.** The pool never creates an asset, so "a picture whose original I cannot
  reach" never becomes a state the rest of the app has to learn.
- **Nothing is written into a photo source, ever.** The shared folder is refused
  if it sits inside one.
- The merge is **state-based, not a log.** Each machine publishes its whole
  decision set — 469 KB here — and every merge is a full reconciliation. There
  are no watermarks to drift, no journal to compact and no history to propagate.
- **A machine publishes everything it holds, not only what it decided itself**,
  and each answer keeps the machine that made it. Over the shared folder this
  changes nothing, because everybody reads everybody's file directly. Over a
  direct connection it is the difference between working and not: if Ana's
  laptop only ever pairs with Dad's, it receives Mum's answers solely because
  Dad's published set carries them. Forwarding what you were told is what makes
  three machines converge without any machinery for it — and it is safe only
  because a re-published answer is settled by when it was decided and by whom,
  not by who handed it over.
- **Merging is idempotent.** Running it twice changes nothing the second time.
- **Merging is resumable and stoppable**, like every other pass. What has been
  applied is applied; what has not is picked up next time.
- **The receiving machine reports what changed**, by count and by kind: names
  gained, answers replaced, albums joined, decisions held. A merge that says
  nothing is a merge nobody can trust or undo.
- **A merge never removes a photograph from a library**, whatever the other
  machine did with its own files.
- A machine is identified by a `Guid` it mints once. Its display name is
  editable and carries no meaning.
- **The UI names no device and no protocol**, per the app's copy rules. "The
  other computers in the house", not "the NAS", not "the peer", not "mDNS".
- The Sharing screen opens by saying what will and will not be sent, before
  anything is nominated.

---

## Contracts

```
MachineIdentity     Id (Guid), Name, AppVersion, SchemaVersion,
                    ModelFingerprints
AssetKey            SharedSourceId + RelativePath - the path the scan
                    reconciles on, scoped by the source the two machines
                    matched. Never the root itself: that is machine-local
                    text, and UNC on one laptop is a drive letter on the next
FaceKey             AssetKey + Bounds, matched after rotations have merged
ContentKey          AssetKey - one description per photograph, so the asset's
                    own key is the whole of it
DecisionSet         Machine, WrittenUtc, People[], Answers[], Turns[],
                    Albums[], Rejections[], Eras[]
PreparedFact        AssetKey, Length, ModifiedUtc,   <- name, then change check
                    ContentHash, ThumbnailName, Width, Height,
                    TakenUtc, Latitude, Longitude, PerceptualHash,
                    Duration, Status
Manifest            Machine, WrittenUtc, SourceRoots[], ModelFingerprints,
                    PreparedFact[], Keyframe[]
HeldDecision        AssetKey, Kind, Payload, FromMachine, DecidedUtc
Peer                MachineId, Name, Fingerprint, LastMergedUtc
                    LastMergedUtc is shown, never consulted. Nothing is
                    fetched or skipped on it - the merge reads the whole
                    state every time, which is the point of state-based

IDecisionExchange   Publish(DecisionSet, ct)
                    Fetch(ct) -> IReadOnlyList<DecisionSet>
IRenditionPool      PublishManifest(Manifest, ct)
                    FetchManifests(ct) -> IReadOnlyList<Manifest>
                    Push(names, ct) / Pull(names, ct)
IPeerDiscovery      Announce(ct) / Listen(ct) -> IAsyncEnumerable<Peer>

PublishDecisionsHandler   (IProgress, ct) -> PublishResult
MergeDecisionsHandler     (DecisionSet[], IProgress, ct) -> MergeResult
ExchangeRenditionsHandler (IProgress, ct) -> RenditionExchangeResult
ApplyHeldDecisionsHandler (ct) -> HeldResult        // runs as a scan phase
```

`IRenditionPool` is separate from `IDecisionExchange` because the two have
different shapes and different costs: decisions are one small document written
whole, renditions are tens of thousands of files copied one at a time and
stopped halfway more often than not. Keeping them apart is also what lets a
machine take the decisions and decline the 2.0 GB.

`ExchangeRenditionsHandler` runs as a phase of the scan, **before** generating,
because its entire purpose is to leave that phase with nothing to do. It is
skipped entirely unless the pool has been turned on — and turning it on is the
one-time offer that names the 2.1 GB and the minutes before anything is copied.

Those two facts have to be read together, or the feature is built wrong in one
of two ways. A phase that pulls gigabytes over the share every time somebody
presses Refresh breaks the app's rule about saying what will happen before the
click; a button that must be pressed after every scan is the procedure this
feature exists to avoid. **The consent is asked once and the work is a phase
thereafter** — which is exactly how the face and video passes already behave
once their models are installed.

`IDecisionExchange` is the seam that lets the shared folder ship first and the
direct connection arrive later without touching the merge. `SharedFolderExchange`
is a file written and files read; `PeerToPeerExchange` is the listener, the
pairing and the framing described above. The merge cannot tell them apart and
must never learn to.

`ApplyHeldDecisionsHandler` runs as a phase of the scan, **after finding faces
and before collections** — not, as it first seemed, straight after indexing. A
held answer names a *face*, and a photograph that has just been indexed has none
yet: swept too early it would find nothing, put the answers back, and leave the
names one whole scan behind the pictures they belong to. Placed after the face
phase it reads what that phase has just written, and collections then get the
names in time to use them for naming. Same dependency-first rule as every other
phase.

### Schema additions

| | |
|---|---|
| `Person.PublicId`, `UpdatedUtc`, `DeletedUtc` | identity across machines, and a tombstone |
| `FaceAssignment.DecidedUtc` | the only human answer in the model with no date on it |
| `AssignmentSource.Cleared` | a fourth value, so unnaming is not a rejection |
| `Collection.PublicId`, `NamedUtc`, `DeletedUtc` | `WasRenamed` is a flag where a date is needed |
| `Asset.RotatedUtc` | six rows today, but a turn is a decision like any other |
| `PhotoSource.SharedId` | the identity a matched pair of roots share, so no key holds a machine-local path |
| `LibrarySettings.MachineId`, `MachineName`, `SharedFolder` | |
| `HeldDecision`, `Peer` | new tables |

Created with `dotnet ef migrations add`, never by hand.

---

## What it costs

Publishing is one pass over decisions the index already holds: 469 KB written.
Merging 9,455 answers is a dictionary lookup per answer against keys that are
already indexed. Neither reads an original, neither needs a model, and neither
touches the share except to write and read one small file.

The rendition pool moves at whatever the shared folder runs at — measured at
6.4 MB/s over Wi-Fi here, so its 2.0 GB is about five minutes, and less on a
cable. It is bounded by the union of everybody's libraries. Every share after
the first carries only the names the machine does not already hold, which for a
family looking at the same photographs is usually nothing at all.

---

## Standing risks and loose ends

| Risk | Standing |
|---|---|
| **Face boxes stop being reproducible.** The `(photograph, box)` key works because `OnnxFaceScanner` appends no execution provider, so every machine runs the same CPU graph. Adding DirectML or CUDA — tempting, with an RTX 4070 in the house — moves boxes by a pixel and shifts embeddings, and nothing fails: matching quietly falls back to overlap, and then to holding answers that should have landed. | Open. If a provider is ever added, the model fingerprint in the manifest must name it too, so machines with different providers refuse each other's vectors rather than mixing them. |
| **The app does write to originals, in one place.** This document quotes *"the app only ever reads a source"* as a founding rule, and `TurnPhotoHandler` has a deliberate exception: it writes the EXIF orientation tag into the file where the file will take it. That changes the file's bytes and its modified time, so its key and its content hash both change and every other machine re-prepares it on the next scan. | Benign, and self-healing. Worth knowing, because it means straightening a photograph costs the pool one re-fetch — and that where the tag *can* be written, the turn reaches the other machines through the file itself and needs no sharing at all. |
| **Settings must stay put.** `LibrarySettings` holds the theme, the gallery cell size, the sort order and the nav state alongside the new machine identity. None of it should travel: they describe how one person looks at their pictures, on one screen. | Named here because "sync the settings row" is the obvious wrong thing to do, and it is one line of code away. |
| **Two libraries are needed to test any of this.** Every rule in this document is about two machines disagreeing, and a single working folder can express none of them. | The merge rules are pure functions of two decision sets and should be written that way, so the interesting half is testable without any I/O. The rest needs a fixture holding two working folders, two contexts and an exchange backed by a temp folder — worth building first rather than after the third merge bug. |
| **A new machine has nothing to share with.** The whole feature assumes a library that exists: a working folder, a source, a scan. The moment the 2.1 GB pool is worth most is the moment a fresh laptop is being set up, and that is precisely when the Sharing screen cannot be reached. | Open. Offering the shared folder during set-up would turn "an evening of scanning" into "five minutes", and is the natural follow-on rather than part of this. |

---

## Acceptance

- [x] A name given on one machine appears on another after one merge, with no
      scan in between, where both have already indexed the photograph.
- [x] Answers about photographs this machine has not indexed are held, reported
      by count, and applied by the next scan.
- [x] Merging twice changes nothing the second time.
- [x] A merge stopped halfway leaves what it applied applied, and finishes on
      the next run.
- [x] Two machines naming the same face differently settle on the later answer.
- [x] A confirmation beats a proposal even when the proposal is newer.
- [x] Two people with the same name and different ids stay apart, and are
      offered as a join.
- [x] A photograph in two albums ends in the later one, and the app says which
      one it left.
- [x] A deleted person does not come back on the next merge.
- [x] A rename of a proposed album survives both a rebuild and a merge.
- [x] Two machines whose source lists overlap in part share decisions about the
      common sources and nothing about the private ones.
- [x] Two machines with no source in common are told so, rather than shown an
      exchange that did nothing and reported success.
- [x] A file re-saved in place keeps its decisions and does not take the pooled
      rendition of the bytes it no longer has.
- [x] Nothing is written inside a photo source, and a shared folder inside one
      is refused with a reason.
- [x] No original is ever sent, requested or received.
- [x] A machine that takes the pool prepares no photographs of its own: after
      the exchange, the generating phase finds nothing outstanding.
- [x] A photograph filled in from the pool carries its capture date, dimensions,
      coordinates and perceptual hash — not just its picture.
- [x] A video's frame *names* are worked out by the receiving machine rather
      than looked up — while its duration, dimensions and keyframe rows still
      come from the manifest, and a clip ends up with a poster either way.
- [x] A rendition whose row this machine has not indexed is not applied, and no
      asset is created for it.
- [x] An exchange stopped halfway leaves every file it copied usable, and
      resumes rather than starting over.
- [x] Running the exchange twice copies nothing the second time.
- [x] A photograph whose bytes changed gets new rendition names rather than
      overwriting the old ones.
- [x] A photograph turned on one machine and not the other is never pooled, and
      both machines end up showing it the same way up.
- [x] A machine that has merged a turn still fetches that photograph's rendition
      from the pool, and turns it itself.
- [x] A turn merged before its rendition arrives is applied when the picture
      lands, not dropped.
- [x] Merging a turn writes no original: the file's modified time on the share
      is unchanged by any number of machines merging it.
- [x] A machine that receives face vectors for photographs it has indexed gains
      those faces and does not detect them again on the next scan.
- [x] Two machines reaching one share by a UNC path and a mapped drive letter
      can be paired, and every decision matches afterwards.
- [x] Adding a photo source that contains the shared folder is refused.
- [x] The Sharing screen shows how recently each machine last shared.
- [x] Ana's laptop, which has only ever paired with Dad's, holds Mum's answers.
- [x] Clearing a name does not stop that person being proposed for that face
      again, on any machine.
- [x] A tombstone is never expired, and a deleted person never walks back in
      from a third machine.
- [x] A name confirmed on a face, and the photograph then turned, still lands on
      that same face when it reaches a machine that never turned it.
- [x] A copy interrupted between the two renditions does not leave a photograph
      reporting itself complete without a preview.
- [x] Two machines fetching the same missing rendition at once cannot leave a
      third reading a partial file.
- [x] Two machines whose sources are rooted at different depths of the same
      share are told they are filed differently, rather than shown an exchange
      that matched nothing and claimed success.
- [x] A photograph quarantined on one machine and later restored comes back with
      its names on every machine, not only the one that set it aside.
- [x] A photograph deleted for good stays gone everywhere, and says nothing
      about it.
- [x] A payload from a machine whose clock is far ahead is refused, naming the
      machine and the skew.
- [x] Held face answers are applied in the same scan that finds the faces, not
      the one after.
- [x] Theme, cell size, sort order and nav state do not travel.
- [x] Taking the decisions while declining the renditions works, and says so.
- [x] Vectors from a machine with different model files are refused, the
      decisions and renditions are still taken, and the screen says which model
      differs.
- [x] Three machines converge, in any order, with no machine designated first.
- [x] The pooled thumbnail folder is never indexed as photographs.
- [x] Discovery blocked by a Public network profile says so, and offers the
      typed address.
- [x] A payload from a newer schema is refused with a plain message rather than
      partially applied.

---

## Out of scope

Syncing originals, in any form, by any option. Showing a photograph whose
original this machine cannot reach — the pool fills in rows, it does not make
them. Renditions at any size other than the two the app already makes. A cloud
service, an account, or anything that leaves the house. Real-time sync — this is
something you do, not something running. Per-person permissions: everybody in
the house sees the same answers. Merging two working folders into one library.
Sharing with somebody who does not have the app. Conflict resolution by asking,
item by item.
