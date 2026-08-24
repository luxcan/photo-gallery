# 00 — Foundation

**Status: ✅ Done**

The shell, the set-up screen, and the two things that must work before anything
else: knowing where the library lives, and opening it.

---

## Goal

Launch the app, choose a working folder, get a window.

## Depends on

Nothing.

---

## Behaviour

### Set-up — "Pick a working folder"

The only question asked before anything exists, because the app cannot build its
services until it knows where the database goes.

> **Asked once, not every time.** This screen originally appeared on every
> launch with the last folder prefilled. Prefilling is not the same as not
> asking: for one user with one library the answer is always the same, and a
> screen whose answer never changes is a toll booth. The app now reopens the
> remembered library directly.
>
> It still appears on a genuine first run, and whenever the remembered folder no
> longer holds an `index.db` — moved, renamed, or on a drive that is not
> connected — because then the app really does not know where to go. The test is
> the index file rather than the folder existing, or an empty folder of the same
> name would have a second, empty library quietly scaffolded over the top of
> where the real one used to be.
>
> The way back is **Settings → Switch library**, which forgets the remembered
> folder and restarts. It restarts rather than rebuilding in place because every
> service, the database connection and the whole view model are bound to one
> working folder.

- Eyebrow `SET UP`, heading, plain-English explanation of what the folder holds.
- **Folder location** text box — typeable *and* pasteable — plus **Browse...**.
  **Empty on a first run**, with **Continue** disabled until a folder is given.
  No suggested path: any default would be a location baked into the build, and
  the app has no business guessing where someone's pictures ought to live. A
  library that has been opened before is prefilled, because that is an answer
  rather than a guess.
- A hint that updates as you type, so the outcome is visible before the click:

  | Folder state | Hint |
  |---|---|
  | Does not exist | "This folder will be created." |
  | Already a library | "This is already a Photo Gallery library - it will be reopened." |
  | Contains photos | "Photos already in this folder and its subfolders will be added to your library." |
  | Empty | "Photo Gallery will set itself up here. You can add photo folders next." |

- **Cancel** / **Continue**.

> The Windows folder picker will **not** reliably select a typed UNC path — it
> navigates and returns whatever was highlighted. Every path field in the app
> must therefore be typeable, with Browse only *filling* it.

### Then

Create or migrate `index.db` in that folder, open the main window on **Photo
sources**.

### Shell

VS Code's layout, with the activity bar grown into a side nav.

- **Side nav**: Library, People, Duplicates, Photo sources — then Settings and
  About at the foot behind a rule, About last of all. Each row is a 14 DIP
  glyph, its name, and how much it holds.
- **Two widths**: 196 DIP open, 52 DIP folded down to the icons, swapped by the
  chevron at the top of the nav over 180 ms. Folded rows carry their name as a
  tooltip; the 2 DIP accent rail is what marks the open section once the names
  are gone.
- **Folding is remembered** with the library, as the palette and the zoom are. A
  window narrower than 1100 DIP folds the nav on its own and unfolds it again on
  the way back out — that is not a choice and is never stored. The chevron beats
  it either way.
- **Content** fills the rest; no second side bar.
- **Status bar**: source summary and counts. What the app has to say goes to
  `logs\` in the working folder rather than an output panel.

Every section except Photo sources, About and Settings is **disabled until at
least one photo source exists** — a screen with nothing to show should not open.
Disabled rows explain why on hover, folded or not. Detaching the last source
returns to Photo sources.

### Theme

Dark+ and Light+, swapped live. Follows Windows until the user picks a side, then
the choice sticks.

Saved in **two** places on purpose:
- `LibrarySettings.Theme` — the library's own palette, wins once open.
- `config.json` — the pre-database fallback, so the set-up window opens in the
  right theme before any library exists.

---

## Contracts

```
IAppConfigStore     Load, Save, RememberFolder, RememberTheme
IWorkingFolder      Root, DatabasePath, ThumbnailsPath, ModelsPath,
                    QuarantinePath, LogsPath, EnsureCreated, IsAppOwned
ILibraryIndex       MigrateAsync, Get/SaveSettingsAsync, sources, counts
OpenLibraryHandler  → OpenLibraryResult(folder, sources, fileCounts, counts,
                                        theme, cellSize, sortOrder,
                                        navigationCollapsed, wasCreated)
SaveThemeHandler
SaveNavigationCollapsedHandler
```

`config.json` sits beside the executable and holds only `LastWorkingFolder` and
`Theme`. Written atomically via a temp file and a move; a corrupt file falls back
to defaults. There is no second copy anywhere, so deleting it is a clean start.

---

## Acceptance

- [x] First run prompts with an empty box and **Continue** disabled until a
      folder is given; no path is suggested.
- [x] Reopening goes straight to the last library without asking.
- [x] A remembered folder that no longer holds an index sends you back to set-up
      rather than scaffolding an empty library over it.
- [x] Settings names the working folder and offers **Switch library**.
- [x] "Open an existing working folder" refuses a folder with no `index.db`,
      rather than silently scaffolding a library into it.
- [x] Creating into a folder that already holds photos warns instead of
      proceeding silently.
- [x] Theme survives a restart, and the set-up window opens in it.
- [x] Sections other than Photo sources, About and Settings are disabled with no
      sources.
- [x] Both themes define exactly the same resource keys — enforced by a test, so
      a swap can never hit a missing resource.
- [x] The side nav folds and unfolds from its chevron, and the fold survives a
      restart.
- [x] A window under 1100 DIP folds the nav on its own without overwriting a
      deliberate choice, and the chevron overrules it while the window is narrow.
- [x] About names the app, links out to its releases and issues, and carries the
      GeoNames credit. It opens with no photo sources connected.

---

## Out of scope

Multi-library windows. Per-user profiles. Cloud sync.

---

## Notes from the build

Two features were built and then deleted here, which is why the PRPs exist:

- A **second set-up screen** for photo sources — redundant, because the Library
  view already had that exact empty state.
- The **global side bar** — it held nothing that could not sit in the content
  area.
