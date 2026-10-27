using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SortiFileWpf.Models;

namespace SortiFileWpf.Core
{
    public class FileWatcherEngine
    {
        private FileSystemWatcher? _watcher;
        private FileSystemWatcher? _workspaceWatcher;
        private readonly ConcurrentQueue<string> _queue = new();
        private readonly AutoResetEvent _wakeSignal = new(false);
        private Thread? _workerThread;
        private bool _running = false;

        public AppConfig Config { get; }
        public HistoryManager History { get; }
        public OllamaRouter Router { get; }

        public event Action<QueueItem>? ItemQueued;
        public event Action<QueueItem>? ItemStatusChanged;
        public event Action<SortRecord>? ItemSorted;
        public event Action? WorkspaceChanged;

        public FileWatcherEngine(AppConfig config)
        {
            Config = config;
            History = new HistoryManager(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sort_history.json"));
            Router = new OllamaRouter(config.OllamaHost, config.OllamaModel, config.ConfidenceThreshold);
        }

        public void Start()
        {
            if (_running) return;
            _running = true;

            EnsureWorkspaceStructure();

            // Start worker thread
            _workerThread = new Thread(ProcessQueueLoop)
            {
                IsBackground = true,
                Name = "SortiFile-EngineWorker"
            };
            _workerThread.Start();

            // Start directory watcher
            RestartWatcher();
        }

        public void Stop()
        {
            _running = false;
            _wakeSignal.Set();
            _watcher?.Dispose();
            _watcher = null;
            _workspaceWatcher?.Dispose();
            _workspaceWatcher = null;
        }

        public void RestartWatcher()
        {
            _watcher?.Dispose();
            string watchPath = Config.ResolvePath(Config.WatchDir);

            if (!Directory.Exists(watchPath))
                Directory.CreateDirectory(watchPath);

            _watcher = new FileSystemWatcher(watchPath)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };

            _watcher.Created += (s, e) => Enqueue(e.FullPath);
            _watcher.Renamed += (s, e) => Enqueue(e.FullPath);

            _workspaceWatcher?.Dispose();
            string wsPath = Config.ResolvePath(Config.WorkspaceDir);
            if (!Directory.Exists(wsPath))
                Directory.CreateDirectory(wsPath);

            _workspaceWatcher = new FileSystemWatcher(wsPath)
            {
                NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName | NotifyFilters.LastWrite,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            _workspaceWatcher.Created += (s, e) => WorkspaceChanged?.Invoke();
            _workspaceWatcher.Deleted += (s, e) => WorkspaceChanged?.Invoke();
            _workspaceWatcher.Renamed += (s, e) => WorkspaceChanged?.Invoke();
        }

        public void EnsureWorkspaceStructure()
        {
            string ws = Config.ResolvePath(Config.WorkspaceDir);
            if (!Directory.Exists(ws)) Directory.CreateDirectory(ws);

            var defaultFolders = new Dictionary<string, string>
            {
                { "Documents", "General written documents, reports, text files, articles, PDF papers, meeting notes, essays, spreadsheets, and educational or business documentation." },
                { "Images", "Photographs, graphic designs, screenshots, illustrations, wallpapers, and visual media assets in PNG, JPG, WEBP, or SVG formats." },
                { "Inbox", "Default staging area for unclassified files, low confidence routing (< 75%), or unknown binary formats requiring manual review." },
                { "Code_Projects", "Excluded from automated sorting. Contains programming source code, repositories, scripts, and software build artifacts." }
            };

            foreach (var kvp in defaultFolders)
            {
                string folder = Path.Combine(ws, kvp.Key);
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

                string ctxFile = Path.Combine(folder, ".context.md");
                if (!File.Exists(ctxFile))
                {
                    try
                    {
                        File.WriteAllText(ctxFile, $"# {kvp.Key} Context\n\n{kvp.Value}\n");
                    }
                    catch { }
                }
            }
        }

        public void Enqueue(string filePath)
        {
            if (Extractor.IsTempOrPartial(filePath)) return;
            if (!File.Exists(filePath)) return;

            _queue.Enqueue(filePath);
            _wakeSignal.Set();

            ItemQueued?.Invoke(new QueueItem
            {
                FilePath = filePath,
                Stage = "1. Queued",
                Status = "Waiting"
            });
        }

        public int ScanWatchDirectory()
        {
            string watchPath = Config.ResolvePath(Config.WatchDir);
            if (!Directory.Exists(watchPath)) return 0;

            int count = 0;
            foreach (var file in Directory.GetFiles(watchPath))
            {
                if (Path.GetFileName(file).StartsWith(".")) continue;
                if (!Extractor.IsTempOrPartial(file))
                {
                    Enqueue(file);
                    count++;
                }
            }
            return count;
        }

        private void ProcessQueueLoop()
        {
            while (_running)
            {
                if (_queue.TryDequeue(out string? filePath))
                {
                    if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) continue;

                    try
                    {
                        ProcessSingleFile(filePath);
                    }
                    catch { }
                }
                else
                {
                    _wakeSignal.WaitOne(600);
                }
            }
        }

        public void ProcessSingleFile(string filePath)
        {
            var item = new QueueItem { FilePath = filePath };

            // 1. Lock Check
            item.Stage = "1. Lock Check";
            item.Status = "Checking";
            ItemStatusChanged?.Invoke(item);

            bool unlocked = Extractor.WaitUntilUnlocked(filePath, 6, 500);
            if (!unlocked)
            {
                item.Status = "Locked / In Use";
                ItemStatusChanged?.Invoke(item);
                return;
            }

            // 2. Reading
            item.Stage = "2. Text Extract";
            item.Status = "Extracting";
            ItemStatusChanged?.Invoke(item);

            string category = Extractor.ClassifyFile(filePath);
            string snippet = Extractor.ExtractTextSnippet(filePath, 200);

            // 3. AI Routing
            item.Stage = "3. AI Routing";
            item.Status = "Analyzing";
            ItemStatusChanged?.Invoke(item);

            var folderContexts = LoadContexts();
            var routing = Router.RouteFileAsync(filePath, snippet, folderContexts, category).GetAwaiter().GetResult();

            // Check if chosen target was locked (fallback to Inbox)
            if (Config.LockedFolders != null && (Config.LockedFolders.Contains(routing.TargetFolder, StringComparer.OrdinalIgnoreCase) || Config.LockedFolders.Contains(Path.GetFileName(routing.TargetFolder), StringComparer.OrdinalIgnoreCase)))
            {
                routing.TargetFolder = "Inbox";
                routing.Reasoning = "Selected target folder is locked from auto-organizing. Safely routed to Inbox.";
            }

            // 4. Moving
            item.Stage = "4. Moving";
            item.Status = $"To {routing.TargetFolder}";
            ItemStatusChanged?.Invoke(item);

            string targetDir = Path.Combine(Config.ResolvePath(Config.WorkspaceDir), routing.TargetFolder);
            var (moved, destPath, err) = FileMover.MoveFile(filePath, targetDir);

            if (moved)
            {
                item.Stage = "Completed";
                item.Status = "Sorted";
                ItemStatusChanged?.Invoke(item);

                var record = new SortRecord
                {
                    FileName = Path.GetFileName(filePath),
                    OriginalPath = Path.GetFullPath(filePath),
                    DestinationPath = Path.GetFullPath(destPath),
                    TargetFolder = routing.TargetFolder,
                    Category = category,
                    ConfidenceScore = routing.ConfidenceScore,
                    ConfidenceValue = routing.ConfidenceValue,
                    Reasoning = routing.Reasoning,
                    Engine = routing.Engine,
                    Status = "moved"
                };

                History.RecordMove(record);
                ItemSorted?.Invoke(record);
            }
            else
            {
                item.Stage = "Failed";
                item.Status = err ?? "Move error";
                ItemStatusChanged?.Invoke(item);
            }
        }

        public Dictionary<string, string> LoadContexts()
        {
            string ws = Config.ResolvePath(Config.WorkspaceDir);
            var rawDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(ws)) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            ScanContextsRecursive(ws, ws, rawDict);

            // Compute dynamic context inheritance for subfolders (ordered by depth so parents resolve first)
            var effectiveDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var sortedKeys = rawDict.Keys.OrderBy(k => k.Count(c => c == '/')).ToList();

            foreach (var key in sortedKeys)
            {
                string rawCtx = rawDict[key];
                int lastSlash = key.LastIndexOf('/');
                if (lastSlash > 0)
                {
                    string parentKey = key.Substring(0, lastSlash);
                    string parentCtx = effectiveDict.TryGetValue(parentKey, out string? pCtx) ? pCtx : "";
                    string subName = key.Substring(lastSlash + 1);

                    // Inherit parent context dynamically + declare subcategory and unique context
                    effectiveDict[key] = string.IsNullOrWhiteSpace(rawCtx) || rawCtx.Equals($"Files related to {subName}.", StringComparison.OrdinalIgnoreCase)
                        ? $"{parentCtx}\n[Subcategory: {subName}]"
                        : $"{parentCtx}\n[Subcategory: {subName}] Specific context: {rawCtx}";
                }
                else
                {
                    effectiveDict[key] = rawCtx;
                }
            }

            if (!effectiveDict.ContainsKey("Inbox")) effectiveDict["Inbox"] = "Fallback staging area for unclassified files.";
            return effectiveDict;
        }

        private void ScanContextsRecursive(string rootDir, string currentDir, Dictionary<string, string> dict)
        {
            foreach (var dir in Directory.GetDirectories(currentDir))
            {
                string name = Path.GetFileName(dir);
                if (name.StartsWith(".") || name.Equals("Code_Projects", StringComparison.OrdinalIgnoreCase))
                    continue;

                string relPath = Path.GetRelativePath(rootDir, dir).Replace('\\', '/');

                // Exclude locked folders from auto-organizing
                if (Config.LockedFolders != null && (Config.LockedFolders.Contains(relPath, StringComparer.OrdinalIgnoreCase) || Config.LockedFolders.Contains(name, StringComparer.OrdinalIgnoreCase)))
                {
                    continue;
                }

                string ctxFile = Path.Combine(dir, ".context.md");
                string desc = $"Files related to {name}.";
                if (File.Exists(ctxFile))
                {
                    try { desc = File.ReadAllText(ctxFile).Trim(); } catch { }
                }
                dict[relPath] = desc;

                ScanContextsRecursive(rootDir, dir, dict);
            }
        }
    }
}

