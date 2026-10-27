# Project Rule: Changelog, Dashboard, Versioning & Deployment Standards

This document establishes the mandatory rules and protocols for all future changes made to the SortiFile codebase.

## 1. Dashboard Changelog Card Rule
- Whenever changes are made to the codebase, the **Changelog card** on the main Dashboard (`ViewDashboard` in `MainWindow.xaml`) must be updated.
- **Current Version Scope Only**: The card must only display the current version badge and only list the recent changes for that current version. Historical changelogs must not clutter the dashboard card.
- **Fixed Floating Link**: A small, clickable link (`View full changelog history ↗`) must remain floating and pinned to the bottom of the card (`DockPanel.Dock="Bottom"`), maintaining its position regardless of how many changes are listed in the scroll area.
- Clicking the link opens `https://github.com/Pinnaclebc/SortiFile/blob/main/changelog.txt` in the user's default browser.

## 2. `changelog.txt` Format Rule
- The root `changelog.txt` file maintains the cumulative chronological history of all updates.
- All entries starting from v1.0.0 must follow this standardized single-line summarized format:
  ```text
  [YYYY-MM-DD] [vX.Y.Z] 'Objective' : 'Action'
  ```
- Example:
  ```text
  [2026-10-27] [v1.0.0] 'Official Release' : 'Initial production release with native 60 FPS WPF desktop UI...'
  [2026-10-27] [v1.1.0] 'Hierarchical Subfolders & Multi-Format Support' : 'Added recursive subfolder context inheritance...'
  ```

## 3. Versioning Protocol
- **Standard Changes / Feature Additions**: Increment minor version by **`+0.1.0`** (e.g. `1.0.0` → `1.1.0`).
- **Super Minor Revisions / Small Bugfixes / Tweaks**: Increment patch version by **`+0.0.1`** (e.g. `1.1.0` → `1.1.1`).
- **Major Updates**: Only when explicitly requested by the user, increment major version by **`+1.0.0`** (e.g. `1.x.x` → `2.0.0`).
- Every version update must be synchronized across:
  1. `src-wpf/SortiFileWpf.csproj` (`<Version>`, `<AssemblyVersion>`, `<FileVersion>`, `<InformationalVersion>`)
  2. `src-wpf/MainWindow.xaml` (Changelog card version badge and title)
  3. `sortifile/__init__.py` (`__version__`)
  4. `changelog.txt`

## 4. Local Builds First Rule
- Every time changes are made, update and compile all local builds immediately:
  - Root standalone application: `SortiFile.exe`
  - Release directory standalone binary: `releases/vX.Y.Z/SortiFile.exe`
  - Local compilation targets: `src-wpf/bin/Debug/` and `src-wpf/bin/Release/`

## 5. Remote Repository Confirmation Rule
- **Never push automatically**: Always ask the user for explicit confirmation before pushing commits, tags, or release assets to the remote GitHub repository.

