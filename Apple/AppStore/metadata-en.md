# App Store listing draft — English (U.S.)

Status: **draft**. Do not enter it in App Store Connect before the owner has reviewed it.

The app's interface is **Turkish only** today. Turkish is the suggested primary language (see `metadata-tr.md`). An English listing is optional; if it is used, the description must say that the interface is in Turkish, as it does below, so that nobody downloads the app expecting English screens.

Every feature below was checked against `Apple/TekstilCADViewer/*.swift` and `Apple/README.md` (16 November 2026, branch `ios/gap-store`). Nothing that is not in the code is claimed. Update this text whenever a feature is added or removed.

Character counts are Unicode characters, as App Store Connect counts them. Each code block is the exact text to paste.

### App name (16/30)

```text
Mono Di Tree CAD
```

### Subtitle (29/30)

```text
Simple pattern drafting in mm
```

### Promotional text (146/170)

Promotional text can be changed without a new version and is not indexed for search.

```text
Add rectangles, lines or tap out your own shape, then type exact sizes in millimetres. No account, no internet: your projects stay on this device.
```

### Description (1976/4000)

```text
Mono Di Tree CAD is a small drawing workshop for simple textile pattern pieces on iPhone and iPad. Add a shape, type its size in millimetres, save the project and share it as a file if you want to.

The app's interface is in Turkish.

DRAW
• Add a rectangle or a line.
• Draw with your finger: tap the corners one after another on a 300 × 300 mm sheet, and leave the shape open or closed. Points can snap to 5 mm steps.
• Try the app straight away with a ready-made two-piece example.

EDIT
• Type a rectangle's width and height, a line's length, a piece's X/Y position and its name in millimetres.
• Edit the coordinates of each corner point.
• Drag a piece to move it (optional 5 mm steps) or nudge it 10 mm left, right, up or down with buttons.
• Rotate by 90 degrees, duplicate, delete.
• Undo and redo the last 50 changes.

VIEW
• Zoom up to 24× with two fingers or with buttons, pan around, and fit the drawing back to the screen in one tap.
• The selected piece's width and height are written on the drawing in millimetres.

SAVE AND SHARE
• Your work is saved automatically as a draft on the device; pick up where you left off from the home screen ("Son çizime devam et").
• Keep projects in the on-device list, rename them or delete them.
• Prepare the drawing as a .tcad file and save it to Files or send it with AirDrop from the Share sheet.

OPEN FILES
• Open a .tcad project file from the Files app (including iCloud Drive) for a read-only preview of its piece outlines. The original file is never changed.
• Simple files drawn in this app can be copied and edited.

PRIVACY
No account, no internet connection, no ads, no tracking. Your drawings stay on your device and go nowhere unless you share them yourself.

GOOD TO KNOW
• The interface is in Turkish.
• This app is for simple outlines. It has no seam allowance, notches, grading or marker making.
• Sizes and drawings on screen are for information; check measurements yourself before cutting or production.
```

### Keywords (95/100)

No space after commas. Words already in the name or subtitle (mono, di, tree, cad, simple, pattern, drafting, mm) are not repeated; Apple indexes them anyway. "marker" is left out on purpose: the app has no marker making.

```text
sewing,tailor,dressmaking,fashion,textile,garment,fabric,sketch,design,millimeter,tcad,clothing
```

### Category suggestion

- Primary: **Graphics & Design**, matching `LSApplicationCategoryType: public.app-category.graphics-design` in `project.yml`.
- Secondary (optional): **Productivity**.

### Age rating answers

App Store Connect's current age rating questionnaire (revised in 2025; ratings 4+, 9+, 13+, 16+, 18+). Answers from reading the code:

| Question group | Answer | Why |
|---|---|---|
| In-app controls (parental controls, age assurance) | No / None | No such feature. |
| Capabilities: unrestricted web access | No | No browser or web view in the app. |
| Capabilities: user-generated content, messaging, chat | No | Nothing is shared between users. The Share button only hands the user's own file to the system Share sheet. |
| Capabilities: advertising | No | No ads. |
| Medical or treatment information, health or wellness topics | None | — |
| Violence (cartoon or realistic), horror or fear themes | None | — |
| Sexual content, nudity, mature themes | None | — |
| Profanity or crude humour | None | — |
| Alcohol, tobacco, drugs | None | — |
| Gambling, simulated gambling, contests, sweepstakes | None | — |
| Guns or other weapons | None | — |

Expected result: **4+**. Question wording in App Store Connect may differ; the owner should fill in the questionnaire personally.

### Support and marketing URLs (to fill in)

- Support URL (required): `https://<to-be-filled>/support` — a page the owner keeps reachable (for example GitHub Pages or a public page of the repository). Do not link to a private repository.
- Marketing URL (optional): `https://<to-be-filled>`
- Privacy Policy URL (required): the public address of `privacy-policy.md` once it is published.
- Copyright line: `2026 <rights holder>` — to be filled in by the owner.

### What's New — 1.03 (draft)

Note: App Store Connect does not ask for this field on the app's **first** App Store version. If 1.03 is the first public version, use this text only in TestFlight's "What to Test". It lists recent changes found in the code on branch `ios/gap-store`; trim it to what changed since the build testers last had.

### What's New 1.03 (432/4000)

```text
• Zoom and pan the drawing with two fingers or with buttons; "Ekrana sığdır" (fit to screen) brings it back.
• The selected piece's size is now written on the drawing in millimetres.
• Pieces can also be nudged 10 mm up and down.
• Rename or delete a project from the recent projects list.
• The corner point editor stays open after "Uygula" (apply) and moves to the next point.
• Clearer save status and warnings for invalid sizes.
```
