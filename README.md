# SortiFile 🗂️⚡

> **High-Performance Native 60 FPS Desktop File Sorting Agent (C# / .NET 9 WPF)**

SortiFile is an autonomous, privacy-first desktop file organization engine built with native C# and .NET 9 WPF. It runs natively with Direct3D hardware acceleration at a buttery-smooth 60 FPS, featuring zero browser dependencies, zero web server overhead, and an iOS-inspired minimalist user interface.

It monitors a designated incoming directory, safely detects newly created or downloaded files, extracts textual summaries, and leverages a local LLM via Ollama (`llama3:8b`, `qwen2.5:7b`, etc.) or smart heuristic routing to organize files into semantic target directories based on folder `.context.md` descriptions.

---

## 🌟 Key Features

* **⚡ Native 60 FPS Desktop Experience (C# & WPF)**
  * Direct3D hardware-accelerated rendering with fluid storyboard easing transitions.
  * Completely eliminated UI flickering/blinking using reactive `ObservableCollection<T>` in-place UI bindings.
  * iOS-inspired typography utilizing `Segoe UI Variable Display` and `SF Pro Text`.

* **📥 Native Windows Drag & Drop**
  * Full OLE Windows drag-and-drop integration (`DragDropEffects.Copy`) directly from Windows Desktop or File Explorer.
  * No "Circle with a dash" cursor errors — displays the standard Windows copy cursor (`+`) on hover.
  * Also supports file picker selection via direct click.

* **📁 Complete Folder Management**
  * **View & Copy Full Paths**: Every folder card prominently displays its exact path with a 1-click "Copy Path" button.
  * **Open in File Explorer**: Instantly launch Windows File Explorer to any target folder or watch directory.
  * **Safe Folder Deletion**: Delete unwanted target folders with automated safety migration — any existing files are automatically relocated to `Inbox` before directory removal.
  * **Dynamic Taxonomy Editor**: Edit any folder's `.context.md` prompt directly in the app to refine AI classification.
  * **Add Target Folders**: Dynamically create new target directories with automatic `.context.md` scaffolding.

* **👁️ Observer & Safe Queue Engine**
  * Native `FileSystemWatcher` event loop with a thread-safe concurrent queue.
  * Multi-stage lock detection to prevent conflicts while files are downloading or being written by other apps.
  * Automatic temporary and partial download file protection (`.crdownload`, `.tmp`, `~$*`).

* **📖 Text & Metadata Extraction**
  * Extracts the first 200 words from `.docx` (Office XML extraction), `.pdf`, `.txt`, `.md`, `.csv`, `.tsv`, `.json`, `.xml`, `.log`, and code files.
  * Instant category tagging: `Text/Doc`, `Media/Binary`, or `Archive/Installer`.

* **🧠 AI Routing & Intelligent Fallback**
  * Local LLM routing via Ollama HTTP API (`/api/chat` with forced JSON schema).
  * Strict confidence thresholding (default 75%): low confidence files safely land in `/Inbox`.
  * Built-in Heuristic Fallback engine: works out of the box even when Ollama is offline or uninstalled.

* **🛡️ Collision Avoidance & Undo Engine**
  * Collision-proof file mover (`filename (1).ext`).
  * Action audit log saved to `sort_history.json`.
  * One-click Revert / Undo functionality directly from the UI or CLI.

---

## 📁 Workspace Blueprint

```text
/SortiFile_Workspace
│
├── /Inbox               # Fallback staging area for low confidence (<75%) or unclassified files
│   └── .context.md
├── /Documents           # Reports, notes, papers, text files, PDFs, essays
│   └── .context.md      # Auto-generated taxonomy summary
├── /Images              # Photos, screenshots, illustrations, wallpapers
│   └── .context.md
└── /Code_Projects       # Excluded from automated sorting
    └── .context.md
```

Incoming files can be dropped into `Watch_Folder/` or dragged directly onto the dashboard drop zone.

---

## 🚀 How to Run

### Standalone Executable (No Browser, No Edge, No Webview)
SortiFile is compiled as a self-contained single-file Windows executable:

* **Direct Executable:**
  Double-click `SortiFile.exe` in the root folder:
  `C:\Users\User\OneDrive\Desktop\Coding\Codes\LARP-Max\SortiFile\SortiFile.exe`

* **Or Batch File:**
  Double-click `SortiFile.bat` in the root directory.

---

## 🛠️ Building from Source

To build or modify the C# WPF project:

```powershell
# Navigate to the project root
cd C:\Users\User\OneDrive\Desktop\Coding\Codes\LARP-Max\SortiFile

# Build in Debug mode
dotnet build ./src-wpf/SortiFileWpf.csproj

# Publish standalone single-file executable
dotnet publish ./src-wpf/SortiFileWpf.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o .
```

---

## 📝 Changelog

### v2.9.6
* **🛡️ Real-Time Undo Sync on File Deletion**: Deleting a sorted file (either inside the app or from File Explorer) now immediately marks the action as missing, updates the database, and un-highlights/disables the `Undo Last` and row-level Undo buttons.
* **🔄 Disk-Verified Undo State Engine**: The Undo engine actively verifies physical file presence on disk, preventing phantom highlighted Undo buttons when target files no longer exist.
* **✏️ Synchronized File Rename Tracking**: Renaming a file in the directory viewer keeps its corresponding sort history record synced, preserving valid undo capability after renames.

### v2.9.5
* **🗄️ Workspace Directory in Setup Wizard**: Setup Wizard now includes a dedicated Workspace step allowing users to designate where sorted files and categorized subfolders live (`SortiFile_Workspace`, personal Documents folder, or custom path).
* **🗂️ Starter Folders & AI Context Scaffolding**: Added new interactive wizard step to configure initial target folders and edit their `.context.md` AI classifier prompts directly, with a 1-click option to skip and use curated defaults (`Documents`, `Images`, `Inbox`, `Code_Projects`).
* **↺ Reset Directory Configurations in Settings**: Replaced the redundant setup wizard button in Settings with a direct "Reset Directories to Defaults" action with safety confirmation dialog to quickly revert Watch and Workspace paths.

### v2.9.4
* **📐 Setup Wizard Full Card Layout & Adaptive Auto-Scroll**: Calibrated the setup wizard popup dimensions (`620x690`), optimized vertical margins/padding, and wrapped step panels inside an adaptive `ScrollViewer` so that the bottom cards/boxes are fully rendered with complete borders and never cut off, even on high-DPI scaling displays.
* **⚡ Auto-Dismissing Clean Notification Toasts**: Upgraded floating notification toasts so that completion notices (such as Setup Wizard completion and clipboard file pastes) display clean vector/emoji indicators, hide the progress bar, and automatically dismiss themselves smoothly after 3.5 seconds.
* **📂 Streamlined "Files in Folder" View**: Removed the redundant file count badge from the "Files in Folder" header for a cleaner, modern directory explorer aesthetic.

### v2.9.3
* **💛 Full-Window Solid Yellow Backdrop**: When the setup wizard opens, the entire main window is immersed in brand yellow (`#FBC305`), cleanly concealing the dashboard until setup is completed.
* **🧭 Clickable Steps & Docked Navigation**: Wizard step pills at the top are now fully interactive clickable tabs with hover highlights. Added dedicated "← Back" and "Next →" / "Finish" navigation buttons anchored at the bottom of the popup.
* **🔄 One-Click Reset Utility (`reset_app.py`)**: Added an automated Python script to close any running instances, wipe configuration/history, and reset the first-run state for effortless testing.

### v2.9.2
* **🧙‍♂️ First-Time Setup Wizard & Interactive Tutorial**: Brand new users are now greeted with a custom 3-step setup modal (`SetupWizardWindow`) asking them to configure their incoming Watch Folder (Default app `Watch_Folder`, user's Windows `Downloads` directory, or custom folder picker) and providing a 30-second workflow guide. Existing users can re-launch the wizard on demand from Settings.
* **🖼️ Edge-to-Edge Logo Frame Alignment**: Calibrated the top-left logo frame and inner geometry clipping (`32x32` inner area inside `35x35` border) with zero padding so the logo fills the border edge-to-edge with no awkward gap or border bleeding.

### v2.9.1
* **🖼️ Logo Framed Inside Border**: Corrected inner container padding and geometry clipping so the top-left SortiFile logo sits neatly and symmetrically inside the border frame without edge clipping.
* **🔒 Locked Active Tab Navigation**: The currently selected navigation tab is now disabled and ignores hover transitions to provide clear optical feedback and eliminate redundant click handlers.
* **🛡️ Smart Missing File Undo Handling**: When attempting to undo an action where the target file no longer exists or was moved elsewhere, the undo action is permanently marked and disabled for that item, with an informative user dialog explaining why undo is no longer possible.
* **📂 Clean Folder Overview Card**: Removed the redundant duplicate folder name display on the folder details overview card.
* **🏷️ Streamlined App Title**: Removed the subtitle text "Smart Local File Organizer" from the top-left header for a cleaner, modern look.

### v2.9.0
* **🎨 Neutral Canvas & Deep Amber Active Tab**: Reverted canvas back to clean neutral background (`#F6F8FA`). Active taskbar tab pill restyled to deep amber brown (`#451A03`) with crisp high-contrast white text (`#FFFFFF`) to eliminate clash with the yellow header.
* **✨ Inward Yellow Logo Border**: Eliminated white border ring around top-left logo with matching inward `#FBC305` border outline.
* **📐 Expanded Full-Height Drag & Drop Zone**: Removed the 3 summary metric cards below the drop zone to allow the drag & drop area to utilize full vertical workspace, with real-time sorting progress displayed by the floating progress card.
* **📋 Sort History Table Headers**: Added structured column titles in the Sort History table (`FILE NAME`, `TARGET FOLDER`, `CONFIDENCE`, `AI REASONING / NOTE`, `TIME`, `ACTION`).
* **🔄 Dynamic Undo Button State**: The `Undo Last` button dynamically enables only when reversible actions exist and automatically fades (`Opacity = 0.4`) and disables when there are no undoable moves.
* **🎬 Native MP4 Video Thumbnail Extraction**: Added DirectShow / MediaFoundation frame grabber fallback to `ShellThumbnailHelper`, extracting live frame previews for video files even when third-party players (like VLC) lack Windows Shell thumbnail provider registry extensions.
* **🔗 iOS 26 Share Vector Icon**: Replaced GitHub repository icon with official iOS-26 liquid-glass share vector geometry (`ios-26-share-icon_svgstack_com_41481793017209.svg`).

### v2.8.0
* **🎨 Yellow Taskbar & Canvas Theme**: Top taskbar updated to vibrant brand yellow (`#FBC305`) with a rich golden interactable capsule (`#E5AC00`), contrasting dark text (`#451A03`), and a soft warm yellow canvas (`#FEF9C3`) behind all floating white cards.
* **🖼️ Pure White Drop Graphic & Yellow Fallbacks**: Centered drag-and-drop icon background refined to pure white with shadow; default and fallback preview icons for images, videos, and documents updated to brand yellow (`#FBC305`).
* **🖼️ App & Binary Logo Icon**: Added high-resolution SortiFile logo to top-left bar and embedded multi-resolution icon (`app.ico`) directly into `SortiFile.exe`.
* **🏷️ Simplified Window Title**: Cleaned window title to "SortiFile".
* **✨ Custom Modern Right-Click Menus**: Removed default Windows UI when right clicking; styled with custom liquid-glass menus, warm hover pills, and shortcuts.
* **🛡️ Selection Marquee Fix**: Resolved mouse desync bug where right-clicking and then left-clicking created an unwanted blue box.
* **📂 Active Folder Vertical Bar**: Added prominent golden vertical accent bar and warm background highlight for the selected active folder.

### v2.7.0
* **🖱️ Folder Right-Click Context Menu**: Right-click any folder directly in the sidebar folder tree (or click the triple dots) to open the folder options popup and color palette right at your mouse cursor.
* **🔒 Persistent Collapsed Subfolder State**: Collapsed folders and their sub-hierarchies now remember their closed state across folder deletion, creation, renaming, and real-time workspace updates.
* **📋 In-Folder File Paste & Global Shortcuts**: Paste files and directories copied from File Explorer or SortiFile directly into the active folder using the new toolbar button (`📋 Paste`), folder context menu, or `Ctrl + V` shortcut (with automatic `filename (1).ext` collision handling).
* **⌨️ Integrated Hotkeys**: Added `Ctrl + V` (Paste into active folder), `Ctrl + C` (Copy selected files), `Ctrl + A` (Select all files), `Delete` (Delete selected files), and `F5` (Refresh files and folder tree).

### v2.6.0
* **🎯 Visual Drag & Drop Line Indicator**: Dynamic horizontal placement indicator line showing exact destination location when dragging and reordering folders.
* **🗂️ Drag Box Selection & Ctrl+Click Multi-Select**: Highlight multiple files inside any folder via drag marquee or `Ctrl` + Left Click for batch deletion or copying.
* **🔒 Folder Lock Feature**: One-click locking to exempt folders from auto-organizing while preserving full drag & drop reordering.
* **🖼️ Rounded Media Previews & File Actions**: Shell-extracted image and video frame previews with clipped rounded corners and full File Explorer actions (Open, Reveal, Copy, Rename, Delete).
* **⚙️ Protected Directory Configuration**: Clean read-only path viewers with dedicated folder selection buttons and custom confirmation modals.
* **⚪ Bordered Floating Progress Toast**: Pure white floating toast with high-contrast border and encapsulated progress bar that stands out against light backgrounds.

### v2.5.0
* **⚡ Native Direct3D 60 FPS WPF Engine**: Butter-smooth UI transitions with hardware acceleration and zero browser overhead.
* **🎨 Liquid Glass Visual Theme**: Modern iOS-inspired typography, custom popups, and 8 color swatches.
* **📥 Native Windows File Explorer Drag & Drop**: Full OLE drag-and-drop support with copy cursor.
* **📁 Comprehensive Hierarchy & Taxonomy Management**: Real-time folder tree with file counts, `.context.md` scaffolding, and safe folder migration.

SortiFile automatically detects if Ollama is running at `http://127.0.0.1:11434`.

```bash
# Pull model in Ollama
ollama run llama3:8b
# or
ollama run qwen2.5:7b
```

If Ollama is not active, SortiFile seamlessly switches to its built-in rule and heuristic engine, indicated by the status pill `Ollama: Fallback Mode` in the top header.
