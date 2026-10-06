# Changelog

All notable changes to this project are documented here. This project follows
[Semantic Versioning](https://semver.org/).

## [2.0.0] - 2026-10-06

A full redesign of the cursors, the app and the workflow.

### Cursors & icons
- **Redrawn cat**: fluffier head with cheeks, coloured iris eyes with slit
  pupils and highlights, lighter muzzle, tabby stripes / Siamese mask / Tuxedo
  bib / Calico patches per coat. Silhouettes now have a single clean outline.
- **Sharper pointer tip**: the face's left ear is long and pointed and sits
  exactly on the hotspot, so pointing is as precise as a normal arrow.
- **New pointer style**: *Arrow + cat* — a Windows-style arrow with a small cat
  perched on it, for people who prefer a conventional tip.
- **Two new coats**: **Calico** and **Tuxedo** (7 in total).
- Arrows, beams and the crosshair keep a fixed dark outline so dark cats stay
  visible on white backgrounds; the sleeping cat's z-z-z got a white halo.
- **Bolder resize / move / precision / up pointers**: thicker shafts, larger
  arrowheads and heavier edges so they stay legible at 32 px.
- **Text cursor redrawn**: a bold I-beam with a small cat sitting on top
  (the old ear-tips were too thin to read at real size).
- Animated cursors now ship 32/48/64 px frames (crisp when enlarged).
- New **app icon**: the orange cat on a warm rounded badge that reads at 16 px.

### App
- **New interface** built from custom controls: rounded cards, numbered steps,
  big coat tiles, a status pill and a one-line status bar. Follows the Windows
  **light/dark** app theme (including the title bar).
- **Live preview** of all 15 pointers for the chosen coat, animations included;
  **hover a pointer to try it** as your real cursor before applying.
- **Pointer style** (Cat face / Arrow + cat) and **pointer size** (Normal, Large,
  Larger, Huge — the same setting as Windows Settings) in the window.
- **Picture dialog** rewritten: drag & drop, click-to-set click point with a
  crosshair marker, real-size 32/48 px preview with the click point marked,
  pointer picker with icons, ICO input supported.
- Fully keyboard-accessible (Tab, arrows, Enter/Space) with focus rings.
- `--screenshot <png>` (with `--light`/`--dark`/`--custom`) renders the window to
  a file for documentation.

### Scripts
- `Apply-CatCursor.ps1`: new coats and `-Style Face|Arrow`.
- `Set-CustomCursor.ps1`: `-Hotspot Custom -HotspotX/-HotspotY` fractions;
  custom cursors are written to `%LOCALAPPDATA%\CatCursor\custom`.

### Fixed
- Custom-cursor hotspot now lands on the last pixel for Center/Bottom-edge
  positions instead of one past the image.

## [1.0.0] - 2026-06-25

First public release.

### Features
- Themes the **whole Windows cursor set** (15 pointer roles) as matching cats.
- **5 colour variants**: Orange, Black, Grey, White, Siamese.
- **Animated** busy and loading cursors (`.ani`): a sleeping cat with drifting
  z-z-z and a spinner cat.
- **Make your own cursor** from any picture (PNG/JPG/BMP/GIF), in the app or via
  `Set-CustomCursor.ps1`.
- Single **self-contained `.exe`** — all cursors are embedded as a compressed
  resource; no install, no admin rights, per-user only.
- Polished GUI: app icon, header with a live colour preview, colour dropdown
  with swatches, current-theme detection, DPI-aware crisp rendering, and a
  one-click restore.
- PowerShell scripts: `Apply-CatCursor.ps1`, `Revert-CatCursor.ps1`,
  `Set-CustomCursor.ps1`.

[2.0.0]: https://github.com/enriquevelmai/windows-cat-cursor/releases/tag/v2.0.0
[1.0.0]: https://github.com/enriquevelmai/windows-cat-cursor/releases/tag/v1.0.0
