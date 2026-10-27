# SortiFile

> **High-Performance Native 60 FPS Desktop File Sorting Agent (C# / .NET 9 WPF)**

SortiFile is an autonomous, privacy-first desktop file organization engine built with native C# and .NET 9 WPF. It runs natively with Direct3D hardware acceleration at a buttery-smooth 60 FPS, featuring zero browser dependencies, zero web server overhead, and an iOS-inspired minimalist user interface.

It monitors a designated incoming directory, safely detects newly created or downloaded files, extracts textual summaries, and leverages a local LLM via Ollama (`llama3:8b`, `qwen2.5:7b`, etc.) or smart heuristic routing to organize files into semantic target directories based on folder `.context.md` descriptions.

---

## Key Features

* **Native 60 FPS Desktop Experience (C# & WPF)**
  * Direct3D hardware-accelerated rendering with fluid storyboard easing transitions.
  * Completely eliminated UI flickering/blinking using reactive `ObservableCollection<T>` in-place UI bindings.
  * iOS-inspired typography utilizing `Segoe UI Variable Display` and `SF Pro Text`.

* **Native Windows Drag & Drop**
  * Full OLE Windows drag-and-drop integration (`DragDropEffects.Copy`) directly from Windows Desktop or File Explorer.
  * No "Circle with a dash" cursor errors — displays the standard Windows copy cursor (`+`) on hover.
  * Also supports file picker selection via direct click.

* **Complete Folder Management**
  * **View & Copy Full Paths**: Every folder card prominently displays its exact path with a 1-click "Copy Path" button.
  * **Open in File Explorer**: Instantly launch Windows File Explorer to any target folder or watch directory.
  * **Safe Folder Deletion**: Delete unwanted target folders with automated safety migration — any existing files are automatically relocated to `Inbox` before directory removal.
  * **Dynamic Taxonomy Editor**: Edit any folder's `.context.md` prompt directly in the app to refine AI classification.
  * **Add Target Folders**: Dynamically create new target directories with automatic `.context.md` scaffolding.

* **Observer & Safe Queue Engine**
  * Native `FileSystemWatcher` event loop with a thread-safe concurrent queue.
  * Multi-stage lock detection to prevent conflicts while files are downloading or being written by other apps.
  * Automatic temporary and partial download file protection (`.crdownload`, `.tmp`, `~$*`).

* **Text & Metadata Extraction**
  * Extracts the first 200 words from `.docx` (Office XML extraction), `.pdf`, `.txt`, `.md`, `.csv`, `.tsv`, `.json`, `.xml`, `.log`, and code files.
  * Instant category tagging: `Text/Doc`, `Media/Binary`, or `Archive/Installer`.

* **AI Routing & Intelligent Fallback**
  * Local LLM routing via Ollama HTTP API (`/api/chat` with forced JSON schema).
  * Strict confidence thresholding (default 75%): low confidence files safely land in `/Inbox`.
  * Built-in Heuristic Fallback engine: works out of the box even when Ollama is offline or uninstalled.

* **Collision Avoidance & Undo Engine**
  * Collision-proof file mover (`filename (1).ext`).
  * Action audit log saved to `sort_history.json`.
  * One-click Revert / Undo functionality directly from the UI or CLI.

---

## Workspace Blueprint

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

## How to Run

### Standalone Executable (No Browser, No Edge, No Webview)
SortiFile is compiled as a self-contained single-file Windows executable:

* **Direct Executable:**
  Download the Windows release, extract it, and double-click `SortiFile.exe` in the extracted folder.

* **Or Batch File:**
  Double-click `SortiFile.bat` in the root directory.

---

## Building from Source

To build or modify the C# WPF project:

```powershell
# Navigate to the cloned repository
cd SortiFile

# Build in Debug mode
dotnet build ./src-wpf/SortiFileWpf.csproj

# Publish standalone single-file executable
dotnet publish ./src-wpf/SortiFileWpf.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o .
```
