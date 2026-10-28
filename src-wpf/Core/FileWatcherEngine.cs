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
        private readonly ConcurrentQueue<string> _queue = new();
        private readonly AutoResetEvent _wakeSignal = new(false);
        private Thread? _workerThread;
        private bool _running = false;

        public AppConfig Config { get; }
        public HistoryManager History { get; }
        public OllamaRouter Router { get; }
        public Dictionary<string, string> FolderPaths { get; } = new(StringComparer.OrdinalIgnoreCase);

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
        }

        public void EnsureWorkspaceStructure()
        {
            if (Config.RegisteredFolders == null)
                Config.RegisteredFolders = new List<string>();

            // On first run or if registered folders are not yet defined, initialize default Desktop folders
            if (!Config.IsFirstRunCompleted && Config.RegisteredFolders.Count == 0)
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var defaultFolders = new Dictionary<string, string>
                {
                    { "Documents", "General written documents, reports, text files, articles, PDF papers, meeting notes, essays, spreadsheets, and educational or business documentation." },
                    { "Images", "Photographs, graphic designs, screenshots, illustrations, wallpapers, and visual media assets in PNG, JPG, WEBP, or SVG formats." },
                    { "Inbox", "Default staging area for unclassified files, low confidence routing (< 75%), or unknown binary formats requiring manual review." },
                    { "Code_Projects", "Excluded from automated sorting. Contains programming source code, repositories, scripts, and software build artifacts." }
                };

                foreach (var kvp in defaultFolders)
                {
                    string folder = Path.Combine(desktop, kvp.Key);
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

                    if (!Config.RegisteredFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
                    {
                        Config.RegisteredFolders.Add(folder);
                    }
                }
                Config.Save();
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

            string targetDir;
            if (FolderPaths.TryGetValue(routing.TargetFolder, out string? resolved) && Directory.Exists(resolved))
            {
                targetDir = resolved;
            }
            else if (FolderPaths.TryGetValue(Path.GetFileName(routing.TargetFolder), out string? resolvedByName) && Directory.Exists(resolvedByName))
            {
                targetDir = resolvedByName;
            }
            else if (FolderPaths.TryGetValue("Inbox", out string? inboxPath) && Directory.Exists(inboxPath))
            {
                targetDir = inboxPath;
            }
            else
            {
                var matched = Config.RegisteredFolders?.FirstOrDefault(f => Path.GetFileName(f.TrimEnd('\\', '/')).Equals(routing.TargetFolder, StringComparison.OrdinalIgnoreCase));
                if (matched != null && Directory.Exists(matched))
                {
                    targetDir = matched;
                }
                else
                {
                    string ws = Config.ResolvePath(Config.WorkspaceDir);
                    string wsTarget = Path.Combine(ws, routing.TargetFolder);
                    if (Directory.Exists(wsTarget))
                    {
                        targetDir = wsTarget;
                    }
                    else
                    {
                        string desktopInbox = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Inbox");
                        if (!Directory.Exists(desktopInbox)) Directory.CreateDirectory(desktopInbox);
                        targetDir = desktopInbox;
                    }
                }
            }

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
            var rawDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            FolderPaths.Clear();

            var registered = Config.RegisteredFolders ?? new List<string>();

            if (registered.Count > 0)
            {
                foreach (var folderPath in registered)
                {
                    if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)) continue;

                    string rootName = Path.GetFileName(folderPath.TrimEnd('\\', '/'));
                    if (string.IsNullOrEmpty(rootName)) rootName = folderPath;

                    bool isLocked = Config.LockedFolders != null && (Config.LockedFolders.Contains(rootName, StringComparer.OrdinalIgnoreCase) || Config.LockedFolders.Contains(folderPath, StringComparer.OrdinalIgnoreCase));
                    if (!isLocked)
                    {
                        string ctxFile = Path.Combine(folderPath, ".context.md");
                        string ctxFileAlt = Path.Combine(folderPath, "context.md");
                        string desc = $"Files related to {rootName}.";
                        if (File.Exists(ctxFile))
                        {
                            try { desc = File.ReadAllText(ctxFile).Trim(); } catch { }
                        }
                        else if (File.Exists(ctxFileAlt))
                        {
                            try { desc = File.ReadAllText(ctxFileAlt).Trim(); } catch { }
                        }

                        rawDict[rootName] = desc;
                        FolderPaths[rootName] = folderPath;
                    }

                    ScanRegisteredSubContexts(rootName, folderPath, folderPath, rawDict);
                }
            }
            else
            {
                // Fallback for workspaces if registered folders list is empty
                string ws = Config.ResolvePath(Config.WorkspaceDir);
                if (Directory.Exists(ws))
                {
                    ScanContextsRecursive(ws, ws, rawDict);
                }
            }

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

        private void ScanRegisteredSubContexts(string rootName, string baseDir, string currentDir, Dictionary<string, string> dict)
        {
            foreach (var dir in Directory.GetDirectories(currentDir))
            {
                string name = Path.GetFileName(dir);
                if (name.StartsWith(".") || name.Equals("Code_Projects", StringComparison.OrdinalIgnoreCase))
                    continue;

                string subRel = Path.GetRelativePath(baseDir, dir).Replace('\\', '/');
                string key = $"{rootName}/{subRel}";

                if (Config.LockedFolders != null && (Config.LockedFolders.Contains(key, StringComparer.OrdinalIgnoreCase) || Config.LockedFolders.Contains(name, StringComparer.OrdinalIgnoreCase)))
                {
                    continue;
                }

                string ctxFile = Path.Combine(dir, ".context.md");
                string ctxFileAlt = Path.Combine(dir, "context.md");
                string desc = $"Files related to {name}.";
                if (File.Exists(ctxFile))
                {
                    try { desc = File.ReadAllText(ctxFile).Trim(); } catch { }
                }
                else if (File.Exists(ctxFileAlt))
                {
                    try { desc = File.ReadAllText(ctxFileAlt).Trim(); } catch { }
                }

                dict[key] = desc;
                FolderPaths[key] = dir;
                FolderPaths[name] = dir;

                ScanRegisteredSubContexts(rootName, baseDir, dir, dict);
            }
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
                FolderPaths[relPath] = dir;
                FolderPaths[name] = dir;

                ScanContextsRecursive(rootDir, dir, dict);
            }
        }
    }
}

