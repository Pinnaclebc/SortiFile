# SortiFile

SortiFile is an autonomous, privacy-first desktop file organization engine built with C# and .NET 9 WPF. It runs locally with Direct3D hardware acceleration, featuring zero browser dependencies, zero web server overhead, and an iOS-inspired user interface.

The application monitors an incoming directory, safely detects newly created or downloaded files, extracts textual summaries and metadata, and leverages either a local Large Language Model via Ollama or a deterministic heuristic engine to organize files into categorized target directories based on folder context descriptions (.context.md).

---

## Features

- Autonomous Directory Observer: FileSystemWatcher background loop monitors incoming downloads with multi-stage file lock detection to prevent conflicts during active writes.
- Dual-Engine Classification: Text and metadata extraction (.docx, .pdf, .txt, .md, images, videos) routed via local Ollama models or heuristic fallback matching .context.md taxonomy prompts.
- Hardware-Accelerated Drag and Drop Canvas: Direct3D-accelerated viewport with immediate visual feedback for ad-hoc manual file organization.
- Dynamic Folder Taxonomy Management: Hierarchical folder explorer with nested subfolder support, color tags, folder locking against automated moves, and in-app prompt editing.
- Zero-Risk Audit History and 1-Click Undo: Full operation audit trail with active disk verification and single-click restoration of any moved file.
- First-Time Guided Setup Wizard: Multi-step interactive onboarding for designating the incoming watch folder, configuring workspace paths, and scaffolding initial taxonomy.

---

## Prerequisites

- Operating System: Windows 10 (version 1809 or higher) or Windows 11 (x64)
- Runtime: .NET 9.0 Desktop Runtime (x64) or use the self-contained executable
- Optional Local AI Backend: Ollama (http://127.0.0.1:11434) with models such as llama3:8b or qwen2.5:7b (the application falls back to built-in heuristic classification when Ollama is offline)

---

## Installation

### Option 1: Standalone Release Executable

1. Navigate to the GitHub Releases section or download `SortiFile.exe` from the `releases/v1.0.0` folder.
2. Place `SortiFile.exe` in a dedicated directory on your system.
3. Launch `SortiFile.exe`. No additional setup or web browser installation is required.

### Option 2: Build from Source

1. Clone the repository:
   ```bash
   git clone https://github.com/Pinnaclebc/SortiFile.git
   cd SortiFile
   ```

2. Ensure .NET 9.0 SDK is installed:
   ```bash
   dotnet --version
   ```

3. Build the WPF application:
   ```bash
   dotnet build ./src-wpf/SortiFileWpf.csproj -c Release
   ```

4. Publish a self-contained single-file executable:
   ```bash
   dotnet publish ./src-wpf/SortiFileWpf.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o ./releases/v1.0.0
   ```

---

## Usage

1. Complete Initial Setup: On first launch, follow the guided wizard to choose your incoming Watch Folder (for example, your browser Downloads folder) and your Workspace destination.
2. Autonomous Organization: Place files in your designated incoming folder. SortiFile detects when writes finish, reads document content, matches it against folder context descriptions, and moves it to the appropriate subfolder.
3. Drag and Drop: Drag any file directly onto the dashboard to inspect and sort it immediately.
4. Customize Taxonomy: Open the Folders tab to view files, add new folders, or edit .context.md prompt descriptions to guide routing behavior.
5. Audit and Undo: If a file is sorted incorrectly, click Undo Last or navigate to Sort History to revert any sorted item back to its origin.

---

## Contributing

Contributions are welcome. To contribute:

1. Fork the repository.
2. Create a feature branch:
   ```bash
   git checkout -b feature/your-feature-name
   ```
3. Commit your changes:
   ```bash
   git commit -m "Add feature description"
   ```
4. Push to the branch:
   ```bash
   git push origin feature/your-feature-name
   ```
5. Open a Pull Request on GitHub.

Please ensure your code builds with zero compiler warnings and maintains privacy-first architecture principles.

---

## License

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.
