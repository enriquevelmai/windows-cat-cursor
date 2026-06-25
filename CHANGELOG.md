# Changelog

All notable changes to this project are documented here. This project follows
[Semantic Versioning](https://semver.org/).

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

[1.0.0]: https://github.com/enriquevelmai/windows-cat-cursor/releases/tag/v1.0.0
