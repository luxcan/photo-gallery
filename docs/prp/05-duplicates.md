# 05 — Duplicates

**Status: ✅ Done — detect, review and quarantine. Restore is built and tested but
deliberately unwired; see the acceptance list.**

Twelve years of copying photos between phones, cards and folders leaves a lot of
the same picture.

---

## Goal

Find duplicates, show them side by side, and move the redundant ones somewhere
reversible.

## Depends on

[03 — Thumbnails](03-thumbnails.md) — review needs pictures, and the perceptual
hash comes from the same decode.

---

## What is already known

Measured on the real library during exploration, before the feature existed:

| | Groups | Files | Reclaimable |
|---|---|---|---|
| Exact (byte-identical) | 346 | 362 | **3.27 GB** |
| Near (perceptual) | 926 | 1,219 | **2.25 GB** |

**Those figures counted videos, and the built pass cannot reach them.**
`ContentHash` is taken while a picture's bytes are in memory during preparation,
and a video has no rendition, so it never gets one. What the first cut actually
finds:

| | Sets | Redundant files | Reclaimable |
|---|---|---|---|
| Exact | 230 | 245 | **0.35 GB** |
| Near (distance ≤ 4) | 472 | — | **1.04 GB** |

The exploration read **785 files of 16,225** to find the exact matches — a size
pre-filter removed 97.5% of the work, because two files can only be
byte-identical if they are the same size. The built pass reads **none at all**:
exact detection is a `GROUP BY` over the stored hash and near detection is leader
clustering over the stored dHash. 11,480 photographs in under a second.

---

## Behaviour

Four parts, and the feature is all four — not just the first.

| | |
|---|---|
| **Detect** | Exact by size pre-filter then hash; near by perceptual hash, banded by confidence. |
| **Review** | Pairs side by side from the local tiles, so it is instant. Resolution, size, folder, and which copy is proposed. |
| **Quarantine** | Move, never delete. Redundant copies go to a dated folder mirroring their original subpaths. |
| **Restore** | Because the quarantine preserves each relative path, undo is mechanical. A manifest is written beside it. |

### Which copy survives

The first attempt got this backwards, which is why it is spelled out.

This library stores many photos twice: once in a catch-all month folder
(`20230201`), and again in the folder that names the event
(`20230203 - Chingay`). A naive "shortest path wins" keeps the meaningless one.

**The rule: prefer a descriptively named folder over a bare-date one, then the
shallower path, then ordinal order** so the result is stable between runs.

Measured: that reversed **218 of 362** decisions.

### Two confidence bands, never mixed

- **Exact** — provably the same bytes. Safe to approve in bulk.
- **Near** — needs a human eye. A perceptual hash cannot tell a re-saved copy
  from the next frame of a burst, and those bursts are often photos worth
  keeping.

---

## Contracts

```
DuplicateSet     Kind (Exact|Near), DetectedUtc, IsResolved, Members
DuplicateMember  AssetId, Role (Keeper|Redundant), Distance
KeeperPolicy     IsGenericFolder, ChooseKeeper, ComparePreference   (pure)
PerceptualHash   64-bit; DistanceTo = popcount of XOR
Asset.QuarantinedUtc   set aside, still known
```

`QuarantinedUtc` was not in the first draft of this list and the feature does not
work without it. **A completed scan deletes rows for files that have gone**, which
would have made the quarantine a one-way door: the copy moves out of the library,
the next refresh notices it missing, and the row that knew the way home is gone.
The scan skips quarantined rows and the gallery filters them out.

`KeeperPolicy` is pure domain logic with no I/O — it is the part most worth
testing, and it is already covered.

Near-duplicate grouping is a matrix operation over all hashes, not a nested
loop: comparing all 11,481 took **7 seconds** in the exploration.

---

## Acceptance

- [x] Exact detection opens **no file at all** — better than the size pre-filter
      this asked for, because the digest was already taken during preparation.
      Verified against disk: all 230 exact sets re-hash identically.
- [x] The keeper is the descriptively named copy, and `KeeperPolicy` now compares
      **pixels, then bytes, then** the folder rule. Every quality term ties for
      byte-identical copies, so the folder rule alone still decides them, exactly as
      it did for the 218 of 362 it reversed during exploration; for near sets the
      new terms stop the app keeping a watermarked 4.4 MB re-save over the 6.1 MB
      original.
- [x] Exact and near are presented separately and never mixed. Exact sets arrive
      **ticked**; near sets arrive **unticked with no bulk approve**, because
      `PL1A9921.jpg` and `PL1A9922.jpg` — consecutive frames from a photographer —
      score distance 0. A near group can also be kept whole and is then never
      offered again.
- [x] Review shows both copies as pictures from the local tiles, with resolution,
      size and folder.
- [x] Quarantine moves to `quarantine\<sourceId>\<relativePath>` and writes a
      manifest — **for the person who opens that folder in a year**, not for
      restore. Copy, verify length, then delete: the library is on a share and the
      working folder is local, so every move crosses a volume.
- [x] Restore puts everything back, and needs no manifest — a copy's way home is
      the photo source and relative path its row has held all along. **No button
      calls it**: the user asked for the control to come off the Duplicates
      screen. It is kept and kept tested because restoring by hand works the same
      way, and putting the control back is one line of wiring.
- [x] Nothing is ever deleted **by this feature**. Deleting a photograph outright
      arrived alongside it as a separate, explicitly asked-for action with its own
      warning, and is not something duplicate resolution can reach.

---

## Out of scope

Deleting anything. Automatic resolution without review. Duplicate videos in the
first cut.
