using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Microsoft.Win32;
using SortiFileWpf.Core;
using SortiFileWpf.Models;
using Path = System.IO.Path;

namespace SortiFileWpf
{
    public partial class MainWindow : Window
    {
        private readonly FileWatcherEngine _engine;
        private readonly Storyboard _fadeInStoryboard;
        private readonly HashSet<string> _collapsedFolderPaths = new(StringComparer.OrdinalIgnoreCase);
        private FolderInfo? _selectedFolder;
        private FolderInfo? _popupFolder;
        private int _totalBatchCount = 0;
        private int _completedBatchCount = 0;

        public ObservableCollection<QueueItem> QueueItems { get; } = new();
        public ObservableCollection<SortRecord> RecentRecords { get; } = new();
        public ObservableCollection<SortRecord> AllHistoryRecords { get; } = new();
        public ObservableCollection<FolderInfo> FolderItems { get; } = new();
        public ObservableCollection<FolderFileItem> SelectedFolderFiles { get; } = new();
        private FolderInfo? _anchorFolder;
        private List<FolderInfo> _multiSelectedFolders = new();

        public MainWindow()
        {
            InitializeComponent();

            _fadeInStoryboard = (Storyboard)FindResource("FadeInAnim");

            var config = AppConfig.Load();
            _engine = new FileWatcherEngine(config);

            // Bind ObservableCollections to UI controls
            ListFullHistory.ItemsSource = AllHistoryRecords;
            ListFolderTree.ItemsSource = FolderItems;
            ListSelectedFiles.ItemsSource = SelectedFolderFiles;

            // Wire Keyboard Shortcuts for Folders & Files (Ctrl+V, Ctrl+C, Ctrl+A, Del, F5)
            KeyDown += MainWindow_KeyDown;

            // Wire DragOver for DropZone to guarantee the copy cursor '+' on Windows
            DropZone.DragOver += DropZone_DragOver;

            // Wire engine events
            _engine.ItemQueued += OnItemQueued;
            _engine.ItemStatusChanged += OnItemStatusChanged;
            _engine.ItemSorted += OnItemSorted;

            // Wire real-time workspace tree auto-updates when directories are added/changed
            _engine.WorkspaceChanged += () =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    RefreshFolders();
                    UpdateStats();
                });
            };

            // Initialize Settings view values
            InitSettingsView();

            // Load existing sort history & folders
            LoadInitialData();

            // Start engine worker & watcher
            _engine.Start();

            // Check Ollama connection status asynchronously
            CheckOllamaStatusAsync();

            // Wire First-time setup wizard on initial window load
            Loaded += MainWindow_Loaded;
        }

        public void ShowSetupWizard(bool isFirstRun = false)
        {
            try
            {
                if (WizardBackdrop != null)
                {
                    WizardBackdrop.Visibility = Visibility.Visible;
                }

                var wizard = new SetupWizardWindow(this, _engine);
                wizard.ShowDialog();
            }
            finally
            {
                if (WizardBackdrop != null)
                {
                    WizardBackdrop.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (!_engine.Config.IsFirstRunCompleted)
                {
                    ShowSetupWizard(isFirstRun: true);
                }
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void LaunchSetupWizard_Click(object sender, RoutedEventArgs e)
        {
            ShowSetupWizard(isFirstRun: false);
        }

        private void InitSettingsView()
        {
            TxtWatchDir.Text = _engine.Config.WatchDir;
            TxtOllamaHost.Text = _engine.Config.OllamaHost;
            SliderThreshold.Value = _engine.Config.ConfidenceThreshold;
            TxtThresholdVal.Text = $"{_engine.Config.ConfidenceThreshold}%";

            // Set ComboBox selection
            foreach (ComboBoxItem item in ComboModel.Items)
            {
                if (string.Equals(item.Content?.ToString(), _engine.Config.OllamaModel, StringComparison.OrdinalIgnoreCase))
                {
                    item.IsSelected = true;
                    break;
                }
            }
        }

        private void LoadInitialData()
        {
            // History
            var history = _engine.History.LoadHistory();
            AllHistoryRecords.Clear();
            RecentRecords.Clear();

            foreach (var record in history)
            {
                AllHistoryRecords.Add(record);
            }

            foreach (var record in history.Take(12))
            {
                RecentRecords.Add(record);
            }

            // Folders
            RefreshFolders();

            // Stats
            UpdateStats();
        }

        public void RefreshFolders()
        {
            // Preserve collapse states before clearing
            foreach (var item in FolderItems)
            {
                if (!item.IsExpanded)
                {
                    _collapsedFolderPaths.Add(item.FullPath);
                }
                else
                {
                    _collapsedFolderPaths.Remove(item.FullPath);
                }
            }
            _collapsedFolderPaths.RemoveWhere(p => !Directory.Exists(p));

            FolderItems.Clear();

            var defaultColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Documents", "#FBC305" },   // SortiFile Gold (Brand)
                { "Images", "#10B981" },      // Emerald Green
                { "Inbox", "#8B5CF6" },       // Purple
                { "Code_Projects", "#3B82F6" }// Blue
            };

            if (_engine.Config.RegisteredFolders == null)
            {
                _engine.Config.RegisteredFolders = new List<string>();
            }

            var rootDirs = new List<string>();
            foreach (var reg in _engine.Config.RegisteredFolders)
            {
                string resolved = _engine.Config.ResolvePath(reg);
                if (Directory.Exists(resolved) && !rootDirs.Contains(resolved, StringComparer.OrdinalIgnoreCase))
                {
                    rootDirs.Add(resolved);
                }
            }

            // Fallback for existing workspaces if registered folders list is empty
            if (rootDirs.Count == 0 && !string.IsNullOrWhiteSpace(_engine.Config.WorkspaceDir))
            {
                string ws = _engine.Config.ResolvePath(_engine.Config.WorkspaceDir);
                if (Directory.Exists(ws))
                {
                    var legacyDirs = Directory.GetDirectories(ws).Where(d => !Path.GetFileName(d).StartsWith(".")).ToList();
                    foreach (var ld in legacyDirs)
                    {
                        if (!_engine.Config.RegisteredFolders.Contains(ld, StringComparer.OrdinalIgnoreCase))
                        {
                            _engine.Config.RegisteredFolders.Add(ld);
                            rootDirs.Add(ld);
                        }
                    }
                    if (rootDirs.Count > 0) _engine.Config.Save();
                }
            }

            if (_engine.Config.FolderOrder != null && _engine.Config.FolderOrder.Count > 0)
            {
                rootDirs = rootDirs
                    .OrderBy(d =>
                    {
                        string name = Path.GetFileName(d);
                        int idx = _engine.Config.FolderOrder.FindIndex(o => o.Equals(name, StringComparison.OrdinalIgnoreCase) || o.Equals(d, StringComparison.OrdinalIgnoreCase));
                        return idx >= 0 ? idx : 10000;
                    })
                    .ThenBy(d => Path.GetFileName(d))
                    .ToList();
            }
            else
            {
                rootDirs = rootDirs.OrderBy(d => Path.GetFileName(d)).ToList();
            }

            for (int i = 0; i < rootDirs.Count; i++)
            {
                ScanDirectoryRecursive(rootDirs[i], rootDirs[i], 0, null, "", i == rootDirs.Count - 1, defaultColors);
            }

            // Restore selection or select first
            if (FolderItems.Count > 0)
            {
                var target = _selectedFolder != null
                    ? FolderItems.FirstOrDefault(f => f.FullPath.Equals(_selectedFolder.FullPath, StringComparison.OrdinalIgnoreCase))
                    : FolderItems.FirstOrDefault();

                SelectFolder(target ?? FolderItems[0]);
            }
            else
            {
                SelectFolder(null);
            }
        }

        private void ScanDirectoryRecursive(string wsRoot, string currentDir, int depth, FolderInfo? parentInfo, string parentColor, bool isLastSibling, Dictionary<string, string> defaultColors)
        {
            string name = Path.GetFileName(currentDir);
            if (name.StartsWith(".")) return;

            string relPath;
            if (depth == 0)
            {
                relPath = name;
            }
            else
            {
                string subRel = Path.GetRelativePath(wsRoot, currentDir).Replace('\\', '/');
                relPath = $"{Path.GetFileName(wsRoot)}/{subRel}";
            }

            string ctxFile = Path.Combine(currentDir, ".context.md");
            string ctxFileAlt = Path.Combine(currentDir, "context.md");
            string ctxText = "";
            if (File.Exists(ctxFile))
            {
                try { ctxText = File.ReadAllText(ctxFile); } catch { }
            }
            else if (File.Exists(ctxFileAlt))
            {
                try { ctxText = File.ReadAllText(ctxFileAlt); } catch { }
            }

            int count = 0;
            try
            {
                count = Directory.GetFiles(currentDir)
                    .Count(f => !Path.GetFileName(f).Equals(".context.md", StringComparison.OrdinalIgnoreCase) &&
                                !Path.GetFileName(f).Equals("context.md", StringComparison.OrdinalIgnoreCase) &&
                                !Path.GetFileName(f).StartsWith("."));
            }
            catch { }

            // Determine color: stored by relative path, stored by name, default color, or inherit from parent
            string colorHex = "#3B82F6";
            if (_engine.Config.FolderColors != null && _engine.Config.FolderColors.TryGetValue(relPath, out var storedRelColor))
            {
                colorHex = storedRelColor;
            }
            else if (_engine.Config.FolderColors != null && _engine.Config.FolderColors.TryGetValue(name, out var storedNameColor))
            {
                colorHex = storedNameColor;
            }
            else if (defaultColors.TryGetValue(name, out var defColor))
            {
                colorHex = defColor;
            }
            else if (!string.IsNullOrEmpty(parentColor))
            {
                colorHex = parentColor;
            }

            // Determine if folder is locked from auto-organizing
            bool isLocked = false;
            if (_engine.Config.LockedFolders != null)
            {
                isLocked = _engine.Config.LockedFolders.Any(f =>
                    f.Equals(relPath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase) ||
                    f.Equals(name, StringComparison.OrdinalIgnoreCase));
            }

            string fullCurrentPath = Path.GetFullPath(currentDir);
            bool isExpanded = !_collapsedFolderPaths.Contains(fullCurrentPath);

            var folderInfo = new FolderInfo
            {
                Name = name,
                RelativePath = relPath,
                FullPath = fullCurrentPath,
                Context = ctxText,
                FileCount = count,
                ColorHex = colorHex,
                Depth = depth,
                IsLastChild = isLastSibling,
                Parent = parentInfo,
                IsExpanded = isExpanded,
                IsHiddenByParent = parentInfo != null && (!parentInfo.IsExpanded || parentInfo.IsHiddenByParent),
                IsLocked = isLocked
            };

            if (parentInfo != null)
            {
                parentInfo.Children.Add(folderInfo);
                parentInfo.NotifyHasSubFoldersChanged();
            }

            FolderItems.Add(folderInfo);

            // Scan subdirectories
            var subDirs = Directory.GetDirectories(currentDir)
                .Where(d => !Path.GetFileName(d).StartsWith("."))
                .OrderBy(d => Path.GetFileName(d))
                .ToList();

            for (int i = 0; i < subDirs.Count; i++)
            {
                ScanDirectoryRecursive(wsRoot, subDirs[i], depth + 1, folderInfo, colorHex, i == subDirs.Count - 1, defaultColors);
            }
        }

        private void ToggleFolderExpand_Click(object sender, RoutedEventArgs e)
        {
            _draggedFolder = null;
            e.Handled = true;
            if (sender is not Button btn || btn.Tag is not FolderInfo folder) return;

            folder.IsExpanded = !folder.IsExpanded;
            if (folder.IsExpanded)
            {
                _collapsedFolderPaths.Remove(folder.FullPath);
            }
            else
            {
                _collapsedFolderPaths.Add(folder.FullPath);
            }
            UpdateChildrenVisibility(folder, !folder.IsExpanded);
        }

        private void UpdateChildrenVisibility(FolderInfo parent, bool hide)
        {
            foreach (var child in parent.Children)
            {
                child.IsHiddenByParent = hide;
                if (!hide && !child.IsExpanded)
                {
                    UpdateChildrenVisibility(child, true);
                }
                else
                {
                    UpdateChildrenVisibility(child, hide);
                }
            }
        }

        public void SelectFolder(FolderInfo? folder)
        {
            _selectedFolder = folder;
            foreach (var f in FolderItems)
            {
                f.IsSelected = (f == folder);
            }

            if (folder == null)
            {
                TxtOverviewFolderName.Text = "No Folder Selected";
                TxtOverviewCountBadge.Text = "0 files";
                TxtOverviewContext.Text = "No registered folders in the folder tree.";
                BadgeFolderOverview.Background = Brushes.Transparent;
                BtnOverviewDelete.Visibility = Visibility.Collapsed;
                BtnOverviewLock.Content = "Lock";

                TxtSelectedFolderCount.Text = "0 files";
                TxtSelectedFolderPath.Text = "None";

                PanelOverviewColors.Children.Clear();
                SelectedFolderFiles.Clear();
                return;
            }

            TxtOverviewFolderName.Text = folder.Name;
            TxtOverviewCountBadge.Text = $"{folder.FileCount} file{(folder.FileCount == 1 ? "" : "s")}";
            TxtOverviewContext.Text = folder.Context;
            BadgeFolderOverview.Background = folder.ColorBrush;
            BtnOverviewDelete.Visibility = folder.IsDeletable ? Visibility.Visible : Visibility.Collapsed;
            BtnOverviewLock.Content = folder.LockBtnText;

            TxtSelectedFolderCount.Text = $"{folder.FileCount} file{(folder.FileCount == 1 ? "" : "s")}";
            TxtSelectedFolderPath.Text = folder.FullPath;

            PopulateOverviewColors(folder);
            LoadFilesForSelectedFolder();
        }

        private void UpdateFolderAndChildrenColor(FolderInfo targetFolder, string newHex)
        {
            targetFolder.ColorHex = newHex;
            foreach (var child in targetFolder.Children)
            {
                if (_engine.Config.FolderColors == null ||
                    (!_engine.Config.FolderColors.ContainsKey(child.RelativePath) && !_engine.Config.FolderColors.ContainsKey(child.Name)))
                {
                    UpdateFolderAndChildrenColor(child, newHex);
                }
            }
        }

        private void PopulateOverviewColors(FolderInfo folder)
        {
            PanelOverviewColors.Children.Clear();
            var colorOptions = new[]
            {
                ("#FBC305", "SortiFile Gold (Brand)"),
                ("#3B82F6", "Blue"),
                ("#8B5CF6", "Purple"),
                ("#10B981", "Green"),
                ("#F43F5E", "Rose"),
                ("#F59E0B", "Amber"),
                ("#06B6D4", "Cyan"),
                ("#6366F1", "Indigo"),
                ("#64748B", "Slate")
            };

            foreach (var (hex, cName) in colorOptions)
            {
                var circleBorder = new Border
                {
                    Width = 22,
                    Height = 22,
                    CornerRadius = new CornerRadius(11),
                    Background = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!,
                    Margin = new Thickness(0, 0, 6, 4),
                    Cursor = Cursors.Hand,
                    ToolTip = cName
                };

                if (string.Equals(folder.ColorHex, hex, StringComparison.OrdinalIgnoreCase))
                {
                    circleBorder.BorderThickness = new Thickness(2.5);
                    circleBorder.BorderBrush = Brushes.Black;
                }

                circleBorder.MouseDown += (s, ev) =>
                {
                    UpdateFolderAndChildrenColor(folder, hex);
                    BadgeFolderOverview.Background = folder.ColorBrush;

                    if (_engine.Config.FolderColors == null)
                        _engine.Config.FolderColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    _engine.Config.FolderColors[folder.RelativePath] = hex;
                    _engine.Config.FolderColors[folder.Name] = hex;
                    _engine.Config.Save();

                    PopulateOverviewColors(folder);
                };

                PanelOverviewColors.Children.Add(circleBorder);
            }
        }

        private void LoadFilesForSelectedFolder()
        {
            SelectedFolderFiles.Clear();
            _selectedFileItem = null;
            PanelFileActions.Visibility = Visibility.Collapsed;

            if (_selectedFolder == null || !Directory.Exists(_selectedFolder.FullPath))
            {
                TxtEmptyFolderFiles.Visibility = Visibility.Visible;
                return;
            }

            try
            {
                var files = Directory.GetFiles(_selectedFolder.FullPath);
                int validCount = 0;
                var itemsToLoadThumbnails = new List<FolderFileItem>();

                foreach (var file in files)
                {
                    string fileName = Path.GetFileName(file);
                    if (fileName.Equals(".context.md", StringComparison.OrdinalIgnoreCase) || fileName.StartsWith("."))
                        continue;

                    var fi = new FileInfo(file);
                    string ext = fi.Extension.TrimStart('.').ToUpper();
                    if (string.IsNullOrEmpty(ext)) ext = "FILE";

                    var item = new FolderFileItem
                    {
                        FileName = fileName,
                        FullPath = Path.GetFullPath(file),
                        Extension = ext,
                        SizeBytes = fi.Length,
                        ModifiedDate = fi.LastWriteTime
                    };

                    SelectedFolderFiles.Add(item);
                    validCount++;

                    if (item.IsImage || item.IsVideo)
                    {
                        itemsToLoadThumbnails.Add(item);
                    }
                }

                _selectedFolder.FileCount = validCount;
                TxtSelectedFolderCount.Text = $"{validCount} file{(validCount == 1 ? "" : "s")}";
                TxtOverviewCountBadge.Text = $"{validCount} file{(validCount == 1 ? "" : "s")}";
                TxtEmptyFolderFiles.Visibility = validCount == 0 ? Visibility.Visible : Visibility.Collapsed;

                if (itemsToLoadThumbnails.Count > 0)
                {
                    Task.Run(() =>
                    {
                        foreach (var item in itemsToLoadThumbnails)
                        {
                            try
                            {
                                var thumb = ShellThumbnailHelper.GetThumbnail(item.FullPath, 200, 200);
                                if (thumb != null)
                                {
                                    Dispatcher.InvokeAsync(() =>
                                    {
                                        item.ThumbnailSource = thumb;
                                    });
                                }
                            }
                            catch { }
                        }
                    });
                }
            }
            catch
            {
                TxtEmptyFolderFiles.Visibility = Visibility.Visible;
            }
        }

        private void UpdateStats()
        {
            UpdateUndoButtonState();
        }

        private void UpdateUndoButtonState()
        {
            if (BtnUndoLast == null) return;

            bool canUndo = false;
            foreach (var r in AllHistoryRecords)
            {
                if (r.IsMoved)
                {
                    if (!string.IsNullOrEmpty(r.DestinationPath) && File.Exists(r.DestinationPath))
                    {
                        canUndo = true;
                    }
                    else
                    {
                        // Destination file is missing or deleted from disk
                        _engine.History.UpdateStatus(r.Id, "missing");
                        r.Status = "missing";
                    }
                }
            }

            BtnUndoLast.IsEnabled = canUndo;
            BtnUndoLast.Opacity = canUndo ? 1.0 : 0.4;
            BtnUndoLast.Cursor = canUndo ? Cursors.Hand : Cursors.Arrow;
        }

        private void NotifyFilesDeleted(IEnumerable<string> filePaths)
        {
            var pathList = filePaths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            if (pathList.Count == 0) return;

            var pathSet = new HashSet<string>(pathList, StringComparer.OrdinalIgnoreCase);
            var nameSet = new HashSet<string>(pathList.Select(p => Path.GetFileName(p) ?? "").Where(s => !string.IsNullOrEmpty(s)), StringComparer.OrdinalIgnoreCase);

            foreach (var r in AllHistoryRecords)
            {
                if (r.IsMoved)
                {
                    string rFileName = !string.IsNullOrEmpty(r.DestinationPath) ? Path.GetFileName(r.DestinationPath) : r.FileName;
                    if (pathSet.Contains(r.DestinationPath) || nameSet.Contains(rFileName) || nameSet.Contains(r.FileName))
                    {
                        _engine.History.UpdateStatus(r.Id, "missing");
                        r.Status = "missing";
                    }
                }
            }
            UpdateUndoButtonState();
        }

        private async void CheckOllamaStatusAsync()
        {
            bool online = await _engine.Router.IsOllamaOnlineAsync();
            Dispatcher.Invoke(() =>
            {
                if (online)
                {
                    DotOllama.Fill = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)); // Emerald green
                    TxtOllama.Text = $"Ollama: {_engine.Config.OllamaModel}";
                }
                else
                {
                    DotOllama.Fill = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)); // Amber yellow
                    TxtOllama.Text = "Ollama: Fallback Mode";
                }
            });
        }

        // ==================== ENGINE EVENT HANDLERS & BOTTOM PROGRESS TOAST ====================

        private void OnItemQueued(QueueItem item)
        {
            Dispatcher.InvokeAsync(() =>
            {
                QueueItems.Add(item);
                _totalBatchCount++;
                UpdateStats();
                ShowProgressToast($"Processing {_totalBatchCount} file{(_totalBatchCount == 1 ? "" : "s")}...", item.FileName, true);
            });
        }

        private void OnItemStatusChanged(QueueItem item)
        {
            Dispatcher.InvokeAsync(() =>
            {
                var existing = QueueItems.FirstOrDefault(q => q.FilePath == item.FilePath);
                if (existing != null)
                {
                    existing.Stage = item.Stage;
                    existing.Status = item.Status;
                }

                ShowProgressToast($"Sorting files ({_completedBatchCount}/{_totalBatchCount})...", $"{item.FileName} — {item.Stage}", true);
            });
        }

        private void OnItemSorted(SortRecord record)
        {
            Dispatcher.InvokeAsync(() =>
            {
                var existingQueue = QueueItems.FirstOrDefault(q => q.FilePath == record.OriginalPath);
                if (existingQueue != null)
                {
                    QueueItems.Remove(existingQueue);
                }

                _completedBatchCount++;
                RecentRecords.Insert(0, record);
                AllHistoryRecords.Insert(0, record);

                // Update folder file counts
                var targetFolder = FolderItems.FirstOrDefault(f => f.Name.Equals(record.TargetFolder, StringComparison.OrdinalIgnoreCase));
                if (targetFolder != null)
                {
                    targetFolder.FileCount++;
                    if (_selectedFolder == targetFolder)
                    {
                        LoadFilesForSelectedFolder();
                    }
                }

                UpdateStats();

                if (QueueItems.Count == 0)
                {
                    _totalBatchCount = 0;
                    _completedBatchCount = 0;
                    CompleteProgressToast($"Organized {record.FileName} into {record.TargetFolder}");
                }
                else
                {
                    ShowProgressToast($"Sorting files ({_completedBatchCount}/{_totalBatchCount})...", $"Routed {record.FileName} to {record.TargetFolder}", false, (_completedBatchCount * 100) / Math.Max(1, _totalBatchCount));
                }
            });
        }

        private System.Windows.Threading.DispatcherTimer? _toastDismissTimer;

        public void ShowNotificationToast(string title, string detail, string icon = "✓", int autoDismissMs = 3500)
        {
            _toastDismissTimer?.Stop();

            ToastProgress.Visibility = Visibility.Visible;
            TxtToastIcon.Text = icon;
            TxtToastTitle.Text = title;
            TxtToastDetail.Text = detail;

            // Hide the progress bar for simple notifications
            if (BorderToastProgress != null)
            {
                BorderToastProgress.Visibility = Visibility.Collapsed;
            }

            _toastDismissTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(autoDismissMs)
            };
            _toastDismissTimer.Tick += (s, e) =>
            {
                _toastDismissTimer.Stop();
                ToastProgress.Visibility = Visibility.Collapsed;
            };
            _toastDismissTimer.Start();
        }

        public void ShowProgressToast(string title, string detail, bool indeterminate = true, int value = 0)
        {
            _toastDismissTimer?.Stop();

            ToastProgress.Visibility = Visibility.Visible;
            TxtToastIcon.Text = "⚡";
            TxtToastTitle.Text = title;
            TxtToastDetail.Text = detail;

            if (BorderToastProgress != null)
            {
                BorderToastProgress.Visibility = Visibility.Visible;
            }

            ProgressToast.IsIndeterminate = indeterminate;
            if (!indeterminate) ProgressToast.Value = value;
        }

        private async void CompleteProgressToast(string detail)
        {
            TxtToastIcon.Text = "✓";
            TxtToastTitle.Text = "Sorting complete";
            TxtToastDetail.Text = detail;
            ProgressToast.IsIndeterminate = false;
            ProgressToast.Value = 100;

            await Task.Delay(2600);

            if (QueueItems.Count == 0)
            {
                ToastProgress.Visibility = Visibility.Collapsed;
            }
        }

        private void DismissToast_Click(object sender, RoutedEventArgs e)
        {
            _toastDismissTimer?.Stop();
            ToastProgress.Visibility = Visibility.Collapsed;
        }

        // ==================== NAVIGATION (TOP CENTERED SEGMENTED PILL) ====================

        private void Nav_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag == null) return;
            string target = btn.Tag.ToString()!;
            SwitchTab(target);
        }

        private void GoToHistory_Click(object sender, RoutedEventArgs e)
        {
            SwitchTab("History");
        }

        private void SwitchTab(string targetTab)
        {
            // Reset nav buttons styling
            Button[] navButtons = { NavDashboard, NavFolders, NavHistory, NavSettings };
            foreach (var b in navButtons)
            {
                b.Background = Brushes.Transparent;
                b.Foreground = new SolidColorBrush(Color.FromRgb(0x45, 0x1A, 0x03));
                b.Effect = null;
                b.IsEnabled = true;
                b.Cursor = Cursors.Hand;
            }

            // Collapse all views
            ViewDashboard.Visibility = Visibility.Collapsed;
            ViewFolders.Visibility = Visibility.Collapsed;
            ViewHistory.Visibility = Visibility.Collapsed;
            ViewSettings.Visibility = Visibility.Collapsed;

            UIElement activeView = ViewDashboard;
            Button activeButton = NavDashboard;

            switch (targetTab)
            {
                case "Dashboard":
                    activeView = ViewDashboard;
                    activeButton = NavDashboard;
                    break;
                case "Folders":
                    activeView = ViewFolders;
                    activeButton = NavFolders;
                    RefreshFolders();
                    break;
                case "History":
                    activeView = ViewHistory;
                    activeButton = NavHistory;
                    break;
                case "Settings":
                    activeView = ViewSettings;
                    activeButton = NavSettings;
                    break;
            }

            // Highlight active button with soft drop shadow pill in rich dark amber brown
            activeButton.Background = new SolidColorBrush(Color.FromRgb(0x45, 0x1A, 0x03));
            activeButton.Foreground = Brushes.White;
            activeButton.Effect = (DropShadowEffect)FindResource("PillShadow");
            activeButton.IsEnabled = false; // Non-clickable and non-hoverable
            activeButton.Cursor = Cursors.Arrow;

            // Make active view visible and trigger smooth 60 FPS Storyboard transition
            activeView.Visibility = Visibility.Visible;
            _fadeInStoryboard.Begin(activeView as FrameworkElement);
        }

        // ==================== NATIVE DRAG & DROP ====================

        private void DropZone_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                DropZone.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFB, 0xC3, 0x05));
                DropZone.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFD, 0xEB));
                e.Handled = true;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void DropZone_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void DropZone_DragLeave(object sender, DragEventArgs e)
        {
            DropZone.BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1));
            DropZone.Background = Brushes.White;
        }

        private void DropZone_Drop(object sender, DragEventArgs e)
        {
            DropZone.BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1));
            DropZone.Background = Brushes.White;

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[]? files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0)
                {
                    Task.Run(() =>
                    {
                        foreach (var path in files)
                        {
                            if (File.Exists(path))
                            {
                                _engine.Enqueue(path);
                            }
                            else if (Directory.Exists(path))
                            {
                                foreach (var sub in Directory.GetFiles(path, "*.*", SearchOption.AllDirectories))
                                {
                                    _engine.Enqueue(sub);
                                }
                            }
                        }
                    });
                }
            }
        }

        private void DropZone_Click(object sender, MouseButtonEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Multiselect = true,
                Title = "Select Files to Sort with SortiFile"
            };

            if (dlg.ShowDialog() == true)
            {
                Task.Run(() =>
                {
                    foreach (var file in dlg.FileNames)
                    {
                        _engine.Enqueue(file);
                    }
                });
            }
        }

        // ==================== SCAN & UNDO BUTTONS ====================

        private void Scan_Click(object sender, RoutedEventArgs e)
        {
            int found = _engine.ScanWatchDirectory();
            if (found == 0)
            {
                CustomDialog.Show(this, "Scan Incoming", $"No pending files found in Watch Folder:\n{_engine.Config.ResolvePath(_engine.Config.WatchDir)}");
            }
        }

        private void UndoLast_Click(object sender, RoutedEventArgs e)
        {
            SortRecord? lastMoved = null;
            foreach (var r in AllHistoryRecords)
            {
                if (r.IsMoved)
                {
                    if (!string.IsNullOrEmpty(r.DestinationPath) && File.Exists(r.DestinationPath))
                    {
                        lastMoved = r;
                        break;
                    }
                    else
                    {
                        _engine.History.UpdateStatus(r.Id, "missing");
                        r.Status = "missing";
                    }
                }
            }

            UpdateUndoButtonState();

            if (lastMoved == null)
            {
                CustomDialog.Show(this, "Undo", "No reversible sort actions found on disk.");
                return;
            }

            PerformUndo(lastMoved);
        }

        private void UndoItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag == null) return;
            string id = btn.Tag.ToString()!;
            var record = AllHistoryRecords.FirstOrDefault(r => r.Id == id);
            if (record != null)
            {
                PerformUndo(record);
            }
        }

        private void PerformUndo(SortRecord record)
        {
            var (success, msg) = FileMover.UndoMove(record);
            if (success)
            {
                _engine.History.MarkReverted(record.Id);
                record.Status = "reverted";
                RefreshFolders();
                UpdateStats();
                CustomDialog.Show(this, "Undo Successful", msg);
            }
            else
            {
                // Check if file no longer exists at destination
                bool isMissing = !File.Exists(record.DestinationPath);
                if (isMissing)
                {
                    _engine.History.UpdateStatus(record.Id, "missing");
                    record.Status = "missing";
                    UpdateStats();
                    CustomDialog.Show(this, "Undo Not Possible",
                        $"Undo failed: The file '{record.FileName}' was not found at its destination:\n{record.DestinationPath}\n\nSince the file no longer exists, undo is no longer possible. The undo action for this item has been disabled.");
                }
                else
                {
                    CustomDialog.Show(this, "Undo Error", $"Undo failed:\n{msg}");
                }
            }
        }

        // ==================== FOLDER TREE SELECTION & OVERVIEW CONTROL CENTER ====================

        private void FolderItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (e.OriginalSource is DependencyObject dep && FindVisualParent<Button>(dep) != null) return;

            if (sender is FrameworkElement elem && elem.Tag is FolderInfo folder)
            {
                if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
                {
                    var anchor = _anchorFolder ?? _selectedFolder ?? folder;
                    int idx1 = FolderItems.IndexOf(anchor);
                    int idx2 = FolderItems.IndexOf(folder);
                    if (idx1 >= 0 && idx2 >= 0)
                    {
                        int start = Math.Min(idx1, idx2);
                        int end = Math.Max(idx1, idx2);

                        _multiSelectedFolders.Clear();
                        for (int i = 0; i < FolderItems.Count; i++)
                        {
                            bool inRange = (i >= start && i <= end && !FolderItems[i].IsHiddenByParent);
                            FolderItems[i].IsSelected = inRange;
                            if (inRange)
                            {
                                _multiSelectedFolders.Add(FolderItems[i]);
                            }
                        }

                        if (_multiSelectedFolders.Count > 1)
                        {
                            TxtOverviewFolderName.Text = $"{_multiSelectedFolders.Count} Folders Selected";
                            TxtOverviewCountBadge.Text = $"{_multiSelectedFolders.Sum(f => f.FileCount)} files total";
                            TxtOverviewContext.Text = "Multiple folders highlighted. Right-click any highlighted folder to Delete, Lock, or Change Color in bulk.";
                            BtnOverviewDelete.Visibility = Visibility.Visible;
                            BtnOverviewLock.Content = "🔒 Lock / Unlock Selected";
                            return;
                        }
                    }
                }

                _anchorFolder = folder;
                _multiSelectedFolders.Clear();
                _multiSelectedFolders.Add(folder);
                SelectFolder(folder);
            }
        }

        private void FolderItem_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            _draggedFolder = null;
            if (sender is FrameworkElement elem && elem.Tag is FolderInfo folder)
            {
                var selectedList = FolderItems.Where(f => f.IsSelected && !f.IsHiddenByParent).ToList();
                if (selectedList.Count > 1 && selectedList.Contains(folder))
                {
                    OpenMultiFolderOptionsPopup(selectedList, elem);
                    e.Handled = true;
                    return;
                }

                _anchorFolder = folder;
                _multiSelectedFolders.Clear();
                _multiSelectedFolders.Add(folder);
                SelectFolder(folder);
                OpenFolderOptionsPopup(folder, elem, isRightClick: true);
                e.Handled = true;
            }
        }

        private void OpenMultiFolderOptionsPopup(List<FolderInfo> selectedFolders, FrameworkElement targetElem)
        {
            _multiSelectedFolders = selectedFolders;
            TxtMultiPopupTitle.Text = $"{selectedFolders.Count} Folders Selected";

            bool allLocked = selectedFolders.All(f => f.IsLocked);
            TxtMultiLockIcon.Text = allLocked ? "🔓" : "🔒";
            TxtMultiLockText.Text = allLocked ? "Unlock All Selected (Include)" : "Lock All Selected (Exclude)";

            PopulateMultiPopupColors(selectedFolders);

            MultiFolderOptionsPopup.PlacementTarget = targetElem;
            MultiFolderOptionsPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            MultiFolderOptionsPopup.IsOpen = true;
        }

        private void PopulateMultiPopupColors(List<FolderInfo> selectedFolders)
        {
            PanelMultiColorPalette.Children.Clear();
            var colorOptions = new[]
            {
                ("#FBC305", "SortiFile Gold (Brand)"),
                ("#3B82F6", "Blue"),
                ("#8B5CF6", "Purple"),
                ("#10B981", "Green"),
                ("#F43F5E", "Rose"),
                ("#F59E0B", "Amber"),
                ("#06B6D4", "Cyan"),
                ("#6366F1", "Indigo"),
                ("#64748B", "Slate")
            };

            foreach (var (hex, cName) in colorOptions)
            {
                var circleBorder = new Border
                {
                    Width = 20,
                    Height = 20,
                    CornerRadius = new CornerRadius(10),
                    Background = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!,
                    Margin = new Thickness(0, 0, 5, 4),
                    Cursor = Cursors.Hand,
                    ToolTip = cName
                };

                circleBorder.MouseDown += (s, ev) =>
                {
                    MultiFolderOptionsPopup.IsOpen = false;
                    if (_engine.Config.FolderColors == null)
                        _engine.Config.FolderColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var folder in selectedFolders)
                    {
                        UpdateFolderAndChildrenColor(folder, hex);
                        _engine.Config.FolderColors[folder.RelativePath] = hex;
                        _engine.Config.FolderColors[folder.Name] = hex;
                    }

                    _engine.Config.Save();
                    if (_selectedFolder != null && selectedFolders.Contains(_selectedFolder))
                    {
                        BadgeFolderOverview.Background = _selectedFolder.ColorBrush;
                        PopulateOverviewColors(_selectedFolder);
                    }
                    ShowNotificationToast("Colors Updated", $"Updated color theme for {selectedFolders.Count} folders.", "🎨", 3000);
                };

                PanelMultiColorPalette.Children.Add(circleBorder);
            }
        }

        private void MultiPopupToggleLock_Click(object sender, RoutedEventArgs e)
        {
            MultiFolderOptionsPopup.IsOpen = false;
            if (_multiSelectedFolders == null || _multiSelectedFolders.Count == 0) return;

            bool allLocked = _multiSelectedFolders.All(f => f.IsLocked);
            bool targetLockState = !allLocked;

            if (_engine.Config.LockedFolders == null)
                _engine.Config.LockedFolders = new List<string>();

            foreach (var folder in _multiSelectedFolders)
            {
                folder.IsLocked = targetLockState;
                string rel = folder.RelativePath;
                if (targetLockState)
                {
                    if (!_engine.Config.LockedFolders.Contains(rel, StringComparer.OrdinalIgnoreCase))
                        _engine.Config.LockedFolders.Add(rel);
                }
                else
                {
                    _engine.Config.LockedFolders.RemoveAll(x => x.Equals(rel, StringComparison.OrdinalIgnoreCase) || x.Equals(folder.Name, StringComparison.OrdinalIgnoreCase));
                }
            }

            _engine.Config.Save();
            ShowNotificationToast("Lock Status Updated", $"{(targetLockState ? "Locked" : "Unlocked")} {_multiSelectedFolders.Count} folders.", targetLockState ? "🔒" : "🔓", 3000);
        }

        private void MultiPopupDelete_Click(object sender, RoutedEventArgs e)
        {
            MultiFolderOptionsPopup.IsOpen = false;
            if (_multiSelectedFolders == null || _multiSelectedFolders.Count == 0) return;

            var deletable = _multiSelectedFolders.Where(f => !f.Name.Equals("Inbox", StringComparison.OrdinalIgnoreCase)).ToList();
            if (deletable.Count == 0)
            {
                CustomDialog.Show(this, "Cannot Delete", "The Inbox staging folder cannot be deleted.");
                return;
            }

            bool confirm = CustomDialog.Confirm(
                this,
                "Confirm Bulk Folder Deletion",
                $"Are you sure you want to delete these {deletable.Count} folders?\n\nAll contained files will be safely moved to Inbox.",
                isDestructive: true,
                confirmBtnText: "Delete Folders");

            if (!confirm) return;

            try
            {
                string inbox = _engine.FolderPaths.TryGetValue("Inbox", out string? resolvedInbox)
                    ? resolvedInbox
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Inbox");
                if (!Directory.Exists(inbox)) Directory.CreateDirectory(inbox);

                int migratedFiles = 0;
                foreach (var folder in deletable)
                {
                    if (Directory.Exists(folder.FullPath))
                    {
                        var files = Directory.GetFiles(folder.FullPath, "*", SearchOption.AllDirectories)
                            .Where(f => !Path.GetFileName(f).Equals(".context.md", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(f).Equals("context.md", StringComparison.OrdinalIgnoreCase));

                        foreach (var file in files)
                        {
                            var (success, _, _) = FileMover.MoveFile(file, inbox);
                            if (success) migratedFiles++;
                        }

                        try { Directory.Delete(folder.FullPath, true); } catch { }
                    }

                    if (_engine.Config.RegisteredFolders != null)
                    {
                        string normDel = Path.GetFullPath(folder.FullPath).TrimEnd('\\', '/');
                        _engine.Config.RegisteredFolders.RemoveAll(r => Path.GetFullPath(r).TrimEnd('\\', '/').Equals(normDel, StringComparison.OrdinalIgnoreCase));
                    }
                    if (_engine.Config.FolderColors != null)
                    {
                        _engine.Config.FolderColors.Remove(folder.Name);
                        _engine.Config.FolderColors.Remove(folder.RelativePath);
                    }
                    _collapsedFolderPaths.Remove(folder.FullPath);
                }

                _engine.Config.Save();
                _engine.LoadContexts();
                RefreshFolders();
                UpdateStats();
                CustomDialog.Show(this, "Folders Deleted", $"Deleted {deletable.Count} folders. {migratedFiles} files safely preserved in Inbox.");
            }
            catch (Exception ex)
            {
                CustomDialog.Show(this, "Error", $"Failed to delete folders:\n{ex.Message}");
            }
        }

        private void FolderOptions_Click(object sender, RoutedEventArgs e)
        {
            _draggedFolder = null;
            e.Handled = true;
            if (sender is FrameworkElement elem && elem.Tag is FolderInfo folder)
            {
                OpenFolderOptionsPopup(folder, elem, isRightClick: false);
            }
        }

        private void OpenFolderOptionsPopup(FolderInfo folder, FrameworkElement targetElem, bool isRightClick)
        {
            _popupFolder = folder;
            TxtPopupFolderName.Text = folder.Name;
            BtnPopupDelete.Visibility = folder.Name.Equals("Inbox", StringComparison.OrdinalIgnoreCase) ? Visibility.Collapsed : Visibility.Visible;
            SepPopupDelete.Visibility = BtnPopupDelete.Visibility;

            TxtPopupLockIcon.Text = folder.IsLocked ? "🔓" : "🔒";
            TxtPopupLockText.Text = folder.IsLocked ? "Unlock Folder (Include)" : "Lock Folder (Exclude)";

            PopulatePopupColors(folder);

            FolderOptionsPopup.PlacementTarget = targetElem;
            FolderOptionsPopup.Placement = isRightClick
                ? System.Windows.Controls.Primitives.PlacementMode.MousePoint
                : System.Windows.Controls.Primitives.PlacementMode.Bottom;
            FolderOptionsPopup.IsOpen = true;
        }

        private void PopulatePopupColors(FolderInfo folder)
        {
            PanelColorPalette.Children.Clear();
            var colorOptions = new[]
            {
                ("#FBC305", "SortiFile Gold (Brand)"),
                ("#3B82F6", "Blue"),
                ("#8B5CF6", "Purple"),
                ("#10B981", "Green"),
                ("#F43F5E", "Rose"),
                ("#F59E0B", "Amber"),
                ("#06B6D4", "Cyan"),
                ("#6366F1", "Indigo"),
                ("#64748B", "Slate")
            };

            foreach (var (hex, cName) in colorOptions)
            {
                var circleBorder = new Border
                {
                    Width = 20,
                    Height = 20,
                    CornerRadius = new CornerRadius(10),
                    Background = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!,
                    Margin = new Thickness(0, 0, 5, 4),
                    Cursor = Cursors.Hand,
                    ToolTip = cName
                };

                if (string.Equals(folder.ColorHex, hex, StringComparison.OrdinalIgnoreCase))
                {
                    circleBorder.BorderThickness = new Thickness(2);
                    circleBorder.BorderBrush = Brushes.Black;
                }

                circleBorder.MouseDown += (s, ev) =>
                {
                    UpdateFolderAndChildrenColor(folder, hex);
                    if (_selectedFolder == folder)
                    {
                        BadgeFolderOverview.Background = folder.ColorBrush;
                        PopulateOverviewColors(folder);
                    }

                    if (_engine.Config.FolderColors == null)
                        _engine.Config.FolderColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    _engine.Config.FolderColors[folder.RelativePath] = hex;
                    _engine.Config.FolderColors[folder.Name] = hex;
                    _engine.Config.Save();

                    PopulatePopupColors(folder);
                };

                PanelColorPalette.Children.Add(circleBorder);
            }
        }

        private void PopupDuplicate_Click(object sender, RoutedEventArgs e)
        {
            FolderOptionsPopup.IsOpen = false;
            if (_popupFolder != null)
            {
                DuplicateFolder(_popupFolder);
            }
        }

        private void PopupRename_Click(object sender, RoutedEventArgs e)
        {
            FolderOptionsPopup.IsOpen = false;
            if (_popupFolder != null)
            {
                RenameFolder(_popupFolder);
            }
        }

        private void PopupEditContext_Click(object sender, RoutedEventArgs e)
        {
            FolderOptionsPopup.IsOpen = false;
            if (_popupFolder != null)
            {
                SelectFolder(_popupFolder);
                TxtOverviewContext.Focus();
                TxtOverviewContext.SelectAll();
            }
        }

        private void PopupOpenExplorer_Click(object sender, RoutedEventArgs e)
        {
            FolderOptionsPopup.IsOpen = false;
            if (_popupFolder != null)
            {
                OpenInExplorer(_popupFolder.FullPath);
            }
        }

        private void PopupDelete_Click(object sender, RoutedEventArgs e)
        {
            FolderOptionsPopup.IsOpen = false;
            if (_popupFolder != null)
            {
                DeleteFolder(_popupFolder);
            }
        }

        private void PopupToggleLock_Click(object sender, RoutedEventArgs e)
        {
            FolderOptionsPopup.IsOpen = false;
            if (_popupFolder != null)
            {
                ToggleFolderLock(_popupFolder);
            }
        }

        private void OverviewToggleLock_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedFolder != null)
            {
                ToggleFolderLock(_selectedFolder);
            }
        }

        private void ToggleFolderLock(FolderInfo folder)
        {
            folder.IsLocked = !folder.IsLocked;

            if (_engine.Config.LockedFolders == null)
                _engine.Config.LockedFolders = new List<string>();

            string rel = folder.RelativePath.Replace('\\', '/');
            string name = folder.Name;

            if (folder.IsLocked)
            {
                if (!_engine.Config.LockedFolders.Contains(rel, StringComparer.OrdinalIgnoreCase))
                    _engine.Config.LockedFolders.Add(rel);
                if (!_engine.Config.LockedFolders.Contains(name, StringComparer.OrdinalIgnoreCase))
                    _engine.Config.LockedFolders.Add(name);
            }
            else
            {
                _engine.Config.LockedFolders.RemoveAll(x =>
                    x.Equals(rel, StringComparison.OrdinalIgnoreCase) ||
                    x.Equals(name, StringComparison.OrdinalIgnoreCase));
            }

            _engine.Config.Save();

            if (_selectedFolder == folder)
            {
                BtnOverviewLock.Content = folder.LockBtnText;
            }
        }

        // ==================== FOLDER TREE DRAG & DROP REORDERING ====================

        private Point _dragStartPoint;
        private FolderInfo? _draggedFolder;

        private void TreeItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject dep && FindVisualParent<Button>(dep) != null)
            {
                _draggedFolder = null;
                return;
            }

            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift) ||
                Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            {
                _draggedFolder = null;
                return;
            }

            _dragStartPoint = e.GetPosition(null);
            if (sender is FrameworkElement elem && elem.Tag is FolderInfo folder)
            {
                _draggedFolder = folder;
            }
        }

        private void TreeItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _draggedFolder = null;
        }

        private void TreeItem_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                _draggedFolder = null;
                return;
            }

            if (_draggedFolder != null)
            {
                Point currentPoint = e.GetPosition(null);
                Vector diff = _dragStartPoint - currentPoint;
                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    var data = new DataObject("SortiFileFolder", _draggedFolder);
                    DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move);
                    _draggedFolder = null;
                }
            }
        }

        private FolderInfo? _lastDragTarget;

        private void TreeItem_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("SortiFileFolder") && sender is FrameworkElement elem && elem.Tag is FolderInfo targetFolder)
            {
                e.Effects = DragDropEffects.Move;
                e.Handled = true;

                Point pos = e.GetPosition(elem);
                bool isTop = pos.Y < (elem.ActualHeight / 2.0);

                if (_lastDragTarget != null && _lastDragTarget != targetFolder)
                {
                    _lastDragTarget.DragOverTopIndicatorVisibility = Visibility.Collapsed;
                    _lastDragTarget.DragOverBottomIndicatorVisibility = Visibility.Collapsed;
                }

                _lastDragTarget = targetFolder;
                targetFolder.DragOverTopIndicatorVisibility = isTop ? Visibility.Visible : Visibility.Collapsed;
                targetFolder.DragOverBottomIndicatorVisibility = !isTop ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void TreeItem_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.Tag is FolderInfo folder)
            {
                folder.DragOverTopIndicatorVisibility = Visibility.Collapsed;
                folder.DragOverBottomIndicatorVisibility = Visibility.Collapsed;
            }
        }

        private void TreeItem_Drop(object sender, DragEventArgs e)
        {
            if (_lastDragTarget != null)
            {
                _lastDragTarget.DragOverTopIndicatorVisibility = Visibility.Collapsed;
                _lastDragTarget.DragOverBottomIndicatorVisibility = Visibility.Collapsed;
                _lastDragTarget = null;
            }

            if (e.Data.GetDataPresent("SortiFileFolder"))
            {
                var sourceFolder = e.Data.GetData("SortiFileFolder") as FolderInfo;
                if (sender is FrameworkElement elem && elem.Tag is FolderInfo targetFolder && sourceFolder != null && sourceFolder != targetFolder)
                {
                    Point pos = e.GetPosition(elem);
                    bool isTop = pos.Y < (elem.ActualHeight / 2.0);

                    int oldIdx = FolderItems.IndexOf(sourceFolder);
                    int targetIdx = FolderItems.IndexOf(targetFolder);

                    if (oldIdx >= 0 && targetIdx >= 0)
                    {
                        int newIdx = targetIdx;
                        if (!isTop && oldIdx < targetIdx)
                        {
                            newIdx = targetIdx;
                        }
                        else if (!isTop && oldIdx > targetIdx)
                        {
                            newIdx = targetIdx + 1;
                        }
                        else if (isTop && oldIdx < targetIdx)
                        {
                            newIdx = targetIdx - 1;
                        }
                        else if (isTop && oldIdx > targetIdx)
                        {
                            newIdx = targetIdx;
                        }

                        if (newIdx < 0) newIdx = 0;
                        if (newIdx >= FolderItems.Count) newIdx = FolderItems.Count - 1;

                        if (oldIdx != newIdx)
                        {
                            FolderItems.Move(oldIdx, newIdx);

                            // Save order into config
                            _engine.Config.FolderOrder = FolderItems.Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                            _engine.Config.Save();
                        }
                    }
                }
                e.Handled = true;
            }
        }

        private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
        {
            if (child == null) return null;
            DependencyObject? parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            if (parentObject is T parent) return parent;
            return FindVisualParent<T>(parentObject);
        }

        private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild) return typedChild;
                var nested = FindVisualChild<T>(child);
                if (nested != null) return nested;
            }
            return null;
        }

        // ==================== FILE SELECTION & ACTIONS IN LARGE VIEW ====================

        private FolderFileItem? _selectedFileItem;
        private Point _selectionStart;
        private bool _isMarqueeDragging = false;
        private bool _isLeftMouseDown = false;

        private void FilesArea_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            ResetMarquee();
        }

        private void FilesArea_LostMouseCapture(object sender, MouseEventArgs e)
        {
            ResetMarquee();
        }

        private void FilesContextMenu_Closed(object sender, RoutedEventArgs e)
        {
            ResetMarquee();
        }

        private void ResetMarquee()
        {
            _isLeftMouseDown = false;
            _isMarqueeDragging = false;
            if (RubberBand != null)
            {
                RubberBand.Visibility = Visibility.Collapsed;
                RubberBand.Width = 0;
                RubberBand.Height = 0;
            }
            if (FilesSelectionArea != null && FilesSelectionArea.IsMouseCaptured)
            {
                FilesSelectionArea.ReleaseMouseCapture();
            }
        }

        private void FilesArea_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject dep && FindVisualParent<Button>(dep) != null)
                return;

            _isLeftMouseDown = true;
            _selectionStart = e.GetPosition(FilesSelectionArea);
            _isMarqueeDragging = false;
            if (RubberBand != null)
            {
                RubberBand.Visibility = Visibility.Collapsed;
                RubberBand.Width = 0;
                RubberBand.Height = 0;
            }

            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            var clickedCard = (e.OriginalSource is DependencyObject depObj) ? FindVisualParent<Border>(depObj) : null;
            bool isCardClick = clickedCard != null && clickedCard.Tag is FolderFileItem;

            if (!isCardClick && !ctrl)
            {
                foreach (var f in SelectedFolderFiles) f.IsSelected = false;
                _selectedFileItem = null;
                UpdateFileActionToolbar();
            }
        }

        private void FilesArea_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isLeftMouseDown || e.LeftButton != MouseButtonState.Pressed)
            {
                if (_isMarqueeDragging)
                {
                    ResetMarquee();
                }
                return;
            }

            Point cur = e.GetPosition(FilesSelectionArea);
            Vector diff = cur - _selectionStart;

            if (!_isMarqueeDragging && (Math.Abs(diff.X) > 6 || Math.Abs(diff.Y) > 6))
            {
                _isMarqueeDragging = true;
                FilesSelectionArea.CaptureMouse();
                RubberBand.Visibility = Visibility.Visible;
            }

            if (_isMarqueeDragging)
            {
                double x = Math.Min(_selectionStart.X, cur.X);
                double y = Math.Min(_selectionStart.Y, cur.Y);
                double w = Math.Abs(_selectionStart.X - cur.X);
                double h = Math.Abs(_selectionStart.Y - cur.Y);

                Canvas.SetLeft(RubberBand, x);
                Canvas.SetTop(RubberBand, y);
                RubberBand.Width = w;
                RubberBand.Height = h;

                Rect marqueeRect = new Rect(x, y, w, h);
                bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

                for (int i = 0; i < SelectedFolderFiles.Count; i++)
                {
                    var fileItem = SelectedFolderFiles[i];
                    var container = ListSelectedFiles.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                    if (container == null) continue;

                    var cardBorder = FindVisualChild<Border>(container) ?? container;
                    GeneralTransform transform = cardBorder.TransformToVisual(FilesSelectionArea);
                    Rect cardBounds = transform.TransformBounds(new Rect(0, 0, cardBorder.ActualWidth, cardBorder.ActualHeight));

                    bool intersects = marqueeRect.IntersectsWith(cardBounds);
                    if (ctrl)
                    {
                        if (intersects) fileItem.IsSelected = true;
                    }
                    else
                    {
                        fileItem.IsSelected = intersects;
                    }
                }

                UpdateFileActionToolbar();
            }
        }

        private void FilesArea_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isLeftMouseDown = false;
            if (_isMarqueeDragging)
            {
                _isMarqueeDragging = false;
                RubberBand.Visibility = Visibility.Collapsed;
                RubberBand.Width = 0;
                RubberBand.Height = 0;
                if (FilesSelectionArea.IsMouseCaptured)
                    FilesSelectionArea.ReleaseMouseCapture();
                UpdateFileActionToolbar();
            }
        }

        private void FileCard_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_isMarqueeDragging) return;

            if (sender is FrameworkElement elem && elem.Tag is FolderFileItem item)
            {
                bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
                if (ctrl)
                {
                    item.IsSelected = !item.IsSelected;
                }
                else
                {
                    foreach (var f in SelectedFolderFiles)
                    {
                        f.IsSelected = (f == item);
                    }
                }

                _selectedFileItem = item;
                UpdateFileActionToolbar();

                if (e.ClickCount == 2)
                {
                    OpenFile(item.FullPath);
                }
            }
        }

        private void UpdateFileActionToolbar()
        {
            int count = SelectedFolderFiles.Count(f => f.IsSelected);
            if (count == 0)
            {
                PanelFileActions.Visibility = Visibility.Collapsed;
                _selectedFileItem = null;
                return;
            }

            PanelFileActions.Visibility = Visibility.Visible;
            if (count == 1)
            {
                _selectedFileItem = SelectedFolderFiles.First(f => f.IsSelected);
                BtnToolbarOpen.Visibility = Visibility.Visible;
                BtnToolbarReveal.Visibility = Visibility.Visible;
                BtnToolbarCopy.Content = "📋 Copy";
                BtnToolbarRename.Visibility = Visibility.Visible;
                BtnToolbarDelete.Content = "🗑️ Delete";
            }
            else
            {
                _selectedFileItem = null;
                BtnToolbarOpen.Visibility = Visibility.Collapsed;
                BtnToolbarReveal.Visibility = Visibility.Collapsed;
                BtnToolbarCopy.Content = $"📋 Copy All ({count})";
                BtnToolbarRename.Visibility = Visibility.Collapsed;
                BtnToolbarDelete.Content = $"🗑️ Delete ({count})";
            }
        }

        private FolderFileItem? GetFileItemFromSender(object sender)
        {
            if (sender is FrameworkElement elem)
            {
                if (elem.DataContext is FolderFileItem directItem) return directItem;
                if (elem.Tag is FolderFileItem taggedItem) return taggedItem;
            }
            return _selectedFileItem;
        }

        private void OpenFile(string path)
        {
            if (File.Exists(path))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    CustomDialog.Show(this, "Error Opening File", $"Could not open file:\n{ex.Message}");
                }
            }
        }

        private void RevealInExplorer(string path)
        {
            if (File.Exists(path))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{path}\"",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    CustomDialog.Show(this, "Explorer Error", $"Could not reveal file in Explorer:\n{ex.Message}");
                }
            }
        }

        private void CopySelectedFiles()
        {
            var selected = SelectedFolderFiles.Where(f => f.IsSelected).ToList();
            if (selected.Count == 0 && _selectedFileItem != null)
            {
                selected.Add(_selectedFileItem);
            }
            if (selected.Count == 0) return;

            try
            {
                var fileList = new System.Collections.Specialized.StringCollection();
                foreach (var item in selected)
                {
                    if (File.Exists(item.FullPath)) fileList.Add(item.FullPath);
                }

                if (fileList.Count > 0)
                {
                    var dataObj = new DataObject();
                    dataObj.SetFileDropList(fileList);
                    dataObj.SetText(string.Join(Environment.NewLine, fileList.Cast<string>()));
                    Clipboard.SetDataObject(dataObj, true);

                    string msg = selected.Count == 1
                        ? $"File copied to clipboard:\n{Path.GetFileName(selected[0].FullPath)}"
                        : $"{selected.Count} files copied to clipboard.";
                    CustomDialog.Show(this, "Files Copied", $"{msg}\n\nYou can paste them anywhere in File Explorer or other apps.");
                }
            }
            catch (Exception ex)
            {
                CustomDialog.Show(this, "Copy Error", $"Could not copy files to clipboard:\n{ex.Message}");
            }
        }

        private void DeleteSelectedFiles()
        {
            var selected = SelectedFolderFiles.Where(f => f.IsSelected).ToList();
            if (selected.Count == 0 && _selectedFileItem != null)
            {
                selected.Add(_selectedFileItem);
            }
            if (selected.Count == 0) return;

            if (selected.Count == 1)
            {
                DeleteFile(selected[0]);
            }
            else
            {
                bool confirm = CustomDialog.Confirm(
                    this,
                    "Confirm Batch File Deletion",
                    $"Are you sure you want to permanently delete all {selected.Count} selected files?\n\nThis action cannot be undone.",
                    isDestructive: true,
                    confirmBtnText: $"Delete {selected.Count} Files");

                if (confirm)
                {
                    var deletedPaths = new List<string>();
                    foreach (var file in selected)
                    {
                        try
                        {
                            if (File.Exists(file.FullPath))
                            {
                                File.Delete(file.FullPath);
                                deletedPaths.Add(file.FullPath);
                            }
                        }
                        catch { }
                    }
                    if (deletedPaths.Count > 0)
                    {
                        NotifyFilesDeleted(deletedPaths);
                    }
                    LoadFilesForSelectedFolder();
                    UpdateStats();
                }
            }
        }

        private void RenameFile(FolderFileItem item)
        {
            if (!File.Exists(item.FullPath)) return;

            var prompt = new PromptDialog("Rename File", "Enter a new name for the file (including extension):", item.FileName);
            prompt.Owner = this;
            if (prompt.ShowDialog() == true)
            {
                string newName = prompt.InputText.Trim();
                if (string.IsNullOrWhiteSpace(newName) || newName.Equals(item.FileName, StringComparison.OrdinalIgnoreCase))
                    return;

                char[] invalid = Path.GetInvalidFileNameChars();
                if (newName.IndexOfAny(invalid) >= 0)
                {
                    CustomDialog.Show(this, "Invalid File Name", "The specified file name contains invalid characters.");
                    return;
                }

                string dir = Path.GetDirectoryName(item.FullPath)!;
                string newPath = Path.Combine(dir, newName);

                if (File.Exists(newPath))
                {
                    CustomDialog.Show(this, "File Already Exists", $"A file named '{newName}' already exists in this folder.");
                    return;
                }

                try
                {
                    string oldPath = item.FullPath;
                    File.Move(oldPath, newPath);

                    foreach (var r in AllHistoryRecords)
                    {
                        if (r.IsMoved && string.Equals(r.DestinationPath, oldPath, StringComparison.OrdinalIgnoreCase))
                        {
                            r.DestinationPath = newPath;
                            r.FileName = newName;
                            _engine.History.UpdateStatus(r.Id, "moved");
                        }
                    }

                    LoadFilesForSelectedFolder();
                    UpdateStats();
                    CustomDialog.Show(this, "File Renamed", $"File successfully renamed to '{newName}'.");
                }
                catch (Exception ex)
                {
                    CustomDialog.Show(this, "Rename Failed", $"Could not rename file:\n{ex.Message}");
                }
            }
        }

        private void DeleteFile(FolderFileItem item)
        {
            if (!File.Exists(item.FullPath)) return;

            bool confirm = CustomDialog.Confirm(
                this,
                "Confirm File Deletion",
                $"Are you sure you want to permanently delete \"{item.FileName}\"?\n\nThis action cannot be undone.",
                isDestructive: true,
                confirmBtnText: "Delete File");

            if (confirm)
            {
                try
                {
                    string path = item.FullPath;
                    File.Delete(path);
                    NotifyFilesDeleted(new[] { path });
                    LoadFilesForSelectedFolder();
                    UpdateStats();
                }
                catch (Exception ex)
                {
                    CustomDialog.Show(this, "Delete Failed", $"Could not delete file:\n{ex.Message}");
                }
            }
        }

        // Toolbar Action Handlers
        private void ToolbarFileOpen_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedFileItem != null) OpenFile(_selectedFileItem.FullPath);
        }

        private void ToolbarFileReveal_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedFileItem != null) RevealInExplorer(_selectedFileItem.FullPath);
        }

        private void ToolbarFileCopy_Click(object sender, RoutedEventArgs e)
        {
            CopySelectedFiles();
        }

        private void ToolbarFileRename_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedFileItem != null) RenameFile(_selectedFileItem);
        }

        private void ToolbarFileDelete_Click(object sender, RoutedEventArgs e)
        {
            DeleteSelectedFiles();
        }

        // ContextMenu Action Handlers
        private void ContextFileOpen_Click(object sender, RoutedEventArgs e)
        {
            var item = GetFileItemFromSender(sender);
            if (item != null) OpenFile(item.FullPath);
        }

        private void ContextFileReveal_Click(object sender, RoutedEventArgs e)
        {
            var item = GetFileItemFromSender(sender);
            if (item != null) RevealInExplorer(item.FullPath);
        }

        private void ContextFileCopy_Click(object sender, RoutedEventArgs e)
        {
            CopySelectedFiles();
        }

        private void ContextFileRename_Click(object sender, RoutedEventArgs e)
        {
            var item = GetFileItemFromSender(sender);
            if (item != null) RenameFile(item);
        }

        private void ContextFileDelete_Click(object sender, RoutedEventArgs e)
        {
            DeleteSelectedFiles();
        }

        private void ToolbarFilePaste_Click(object sender, RoutedEventArgs e)
        {
            PasteFilesIntoSelectedFolder();
        }

        private void RefreshCurrentFolderFiles_Click(object sender, RoutedEventArgs e)
        {
            LoadFilesForSelectedFolder();
        }

        private void PasteFilesIntoSelectedFolder()
        {
            if (_selectedFolder == null || !Directory.Exists(_selectedFolder.FullPath))
            {
                CustomDialog.Show(this, "No Folder Selected", "Please select a target folder to paste files into.");
                return;
            }

            try
            {
                var filesToPaste = new List<string>();

                if (Clipboard.ContainsFileDropList())
                {
                    var dropList = Clipboard.GetFileDropList();
                    foreach (var path in dropList)
                    {
                        if (!string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path)))
                        {
                            filesToPaste.Add(path);
                        }
                    }
                }
                else if (Clipboard.ContainsText())
                {
                    string text = Clipboard.GetText();
                    var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        string trimmed = line.Trim().Trim('\"');
                        if (File.Exists(trimmed) || Directory.Exists(trimmed))
                        {
                            filesToPaste.Add(trimmed);
                        }
                    }
                }

                if (filesToPaste.Count == 0)
                {
                    CustomDialog.Show(this, "Clipboard Empty", "No files or folders found on clipboard to paste.\n\nCopy some files from File Explorer or SortiFile first (Ctrl+C).");
                    return;
                }

                int copiedCount = 0;
                string destDir = _selectedFolder.FullPath;

                foreach (var sourcePath in filesToPaste)
                {
                    if (File.Exists(sourcePath))
                    {
                        string fileName = Path.GetFileName(sourcePath);
                        string destFilePath = Path.Combine(destDir, fileName);

                        if (File.Exists(destFilePath))
                        {
                            string stem = Path.GetFileNameWithoutExtension(fileName);
                            string ext = Path.GetExtension(fileName);
                            int counter = 1;
                            do
                            {
                                destFilePath = Path.Combine(destDir, $"{stem} ({counter}){ext}");
                                counter++;
                            } while (File.Exists(destFilePath));
                        }

                        File.Copy(sourcePath, destFilePath, overwrite: false);
                        copiedCount++;
                    }
                    else if (Directory.Exists(sourcePath))
                    {
                        string dirName = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                        string destSubDir = Path.Combine(destDir, dirName);

                        if (Directory.Exists(destSubDir))
                        {
                            int counter = 1;
                            do
                            {
                                destSubDir = Path.Combine(destDir, $"{dirName} ({counter})");
                                counter++;
                            } while (Directory.Exists(destSubDir));
                        }

                        CopyDirectoryRecursive(sourcePath, destSubDir);
                        copiedCount++;
                    }
                }

                LoadFilesForSelectedFolder();
                UpdateStats();
                ShowNotificationToast("Files Pasted", $"Pasted {copiedCount} item{(copiedCount == 1 ? "" : "s")} into '{_selectedFolder.Name}'", "📋", 3000);
            }
            catch (Exception ex)
            {
                CustomDialog.Show(this, "Paste Failed", $"Could not paste files:\n{ex.Message}");
            }
        }

        private static void CopyDirectoryRecursive(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string targetFilePath = Path.Combine(targetDir, Path.GetFileName(file));
                File.Copy(file, targetFilePath, true);
            }
            foreach (var subDir in Directory.GetDirectories(sourceDir))
            {
                string targetSubDir = Path.Combine(targetDir, Path.GetFileName(subDir));
                CopyDirectoryRecursive(subDir, targetSubDir);
            }
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.OriginalSource is TextBox) return;

            bool isCtrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

            if (isCtrl && e.Key == Key.V)
            {
                PasteFilesIntoSelectedFolder();
                e.Handled = true;
            }
            else if (isCtrl && e.Key == Key.C)
            {
                CopySelectedFiles();
                e.Handled = true;
            }
            else if (isCtrl && e.Key == Key.A)
            {
                foreach (var f in SelectedFolderFiles)
                {
                    f.IsSelected = true;
                }
                UpdateFileActionToolbar();
                e.Handled = true;
            }
            else if (e.Key == Key.Delete)
            {
                DeleteSelectedFiles();
                e.Handled = true;
            }
            else if (e.Key == Key.F5)
            {
                LoadFilesForSelectedFolder();
                RefreshFolders();
                e.Handled = true;
            }
        }

        // ==================== SETTINGS DIRECTORY SELECTORS ====================

        private void ChangeWatchDir_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog
            {
                Title = "Select New Incoming Watch Directory",
                InitialDirectory = Directory.Exists(_engine.Config.ResolvePath(_engine.Config.WatchDir))
                    ? _engine.Config.ResolvePath(_engine.Config.WatchDir)
                    : Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };

            if (dlg.ShowDialog() == true)
            {
                string chosenPath = dlg.FolderName;
                if (string.IsNullOrWhiteSpace(chosenPath)) return;

                bool confirm = CustomDialog.Confirm(
                    this,
                    "Confirm Watch Directory Change",
                    $"Are you sure you want to change the Watch Folder to:\n\n{chosenPath}\n\nSortiFile will monitor this folder for new files and automatically process them.",
                    confirmBtnText: "Update Watch Folder");

                if (confirm)
                {
                    _engine.Config.WatchDir = chosenPath;
                    TxtWatchDir.Text = chosenPath;
                    _engine.Config.Save();
                    CustomDialog.Show(this, "Directory Updated", $"Incoming Watch Directory updated successfully to:\n{chosenPath}");
                }
            }
        }

        private void ResetDirectories_Click(object sender, RoutedEventArgs e)
        {
            string defaultWatch = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Watch_Folder");
            bool confirm = CustomDialog.Confirm(
                this,
                "Reset Watch Directory",
                "Are you sure you want to reset the incoming watch directory back to factory default?\n\n" +
                $"• Incoming Watch Directory:\n  {defaultWatch}\n\n" +
                "SortiFile will restart monitoring the default incoming folder.",
                isDestructive: false,
                confirmBtnText: "Reset to Default");

            if (confirm)
            {
                try
                {
                    _engine.Config.WatchDir = defaultWatch;
                    _engine.Config.Save();

                    _engine.EnsureWorkspaceStructure();
                    _engine.RestartWatcher();

                    TxtWatchDir.Text = _engine.Config.WatchDir;
                    RefreshFolders();

                    ShowNotificationToast("Watch Directory Reset", "Incoming watch directory reset to default.", "↺", 3500);
                }
                catch (Exception ex)
                {
                    CustomDialog.Show(this, "Reset Error", $"Failed to reset watch directory:\n{ex.Message}");
                }
            }
        }

        private void ResetFolderTree_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ResetFolderTreeDialog { Owner = this };
            if (dlg.ShowDialog() == true && dlg.IsConfirmed)
            {
                try
                {
                    if (_engine.Config.RegisteredFolders != null)
                    {
                        _engine.Config.RegisteredFolders.Clear();
                    }
                    if (_engine.Config.FolderOrder != null) _engine.Config.FolderOrder.Clear();
                    if (_engine.Config.FolderColors != null) _engine.Config.FolderColors.Clear();
                    if (_engine.Config.LockedFolders != null) _engine.Config.LockedFolders.Clear();
                    _engine.Config.Save();

                    _engine.FolderPaths.Clear();
                    _engine.LoadContexts();
                    RefreshFolders();
                    UpdateStats();

                    ShowNotificationToast("Folder Tree Reset", "All registered folders have been removed from the tree. Files on disk remain untouched.", "🗑️", 3500);
                }
                catch (Exception ex)
                {
                    CustomDialog.Show(this, "Reset Error", $"Failed to reset folder tree:\n{ex.Message}");
                }
            }
        }

        private void OverviewOpenExplorer_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedFolder != null)
            {
                OpenInExplorer(_selectedFolder.FullPath);
            }
        }

        private void OverviewDuplicate_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedFolder != null)
            {
                DuplicateFolder(_selectedFolder);
            }
        }

        private void OverviewRename_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedFolder != null)
            {
                RenameFolder(_selectedFolder);
            }
        }

        private void OverviewDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedFolder != null)
            {
                DeleteFolder(_selectedFolder);
            }
        }

        private void OverviewSaveContext_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedFolder == null) return;

            string newContext = TxtOverviewContext.Text.Trim();
            try
            {
                string ctxPath = Path.Combine(_selectedFolder.FullPath, ".context.md");
                File.WriteAllText(ctxPath, newContext);
                _selectedFolder.Context = newContext;
                CustomDialog.Show(this, "Context Saved", $"Taxonomy context updated for '{_selectedFolder.Name}'.\nThe local AI router will now use this description.");
            }
            catch (Exception ex)
            {
                CustomDialog.Show(this, "Error", $"Error saving context:\n{ex.Message}");
            }
        }

        private void DuplicateFolder(FolderInfo folder)
        {
            var prompt = new PromptDialog("Duplicate Folder", $"Enter a name for the duplicated folder:", $"{folder.Name}_Copy");
            prompt.Owner = this;
            if (prompt.ShowDialog() == true)
            {
                string newName = prompt.InputText.Trim();
                if (string.IsNullOrWhiteSpace(newName)) return;

                string parentDir = Path.GetDirectoryName(folder.FullPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string newPath = Path.Combine(parentDir, newName);

                if (Directory.Exists(newPath))
                {
                    CustomDialog.Show(this, "Already Exists", $"Folder '{newName}' already exists.");
                    return;
                }

                try
                {
                    Directory.CreateDirectory(newPath);
                    string ctxPath = Path.Combine(folder.FullPath, ".context.md");
                    string ctxText = File.Exists(ctxPath) ? File.ReadAllText(ctxPath) : $"# {newName} Context\n\nTaxonomy for {newName}.\n";
                    File.WriteAllText(Path.Combine(newPath, ".context.md"), ctxText);

                    // Copy color
                    if (_engine.Config.FolderColors != null)
                    {
                        _engine.Config.FolderColors[newName] = folder.ColorHex;
                        _engine.Config.Save();
                    }

                    if (folder.Parent == null)
                    {
                        if (_engine.Config.RegisteredFolders == null) _engine.Config.RegisteredFolders = new List<string>();
                        if (!_engine.Config.RegisteredFolders.Contains(newPath, StringComparer.OrdinalIgnoreCase))
                        {
                            _engine.Config.RegisteredFolders.Add(newPath);
                            _engine.Config.Save();
                        }
                    }

                    _engine.LoadContexts();
                    RefreshFolders();
                    CustomDialog.Show(this, "Folder Duplicated", $"Folder duplicated successfully as '{newName}'.");
                }
                catch (Exception ex)
                {
                    CustomDialog.Show(this, "Error", $"Failed to duplicate folder:\n{ex.Message}");
                }
            }
        }

        private void RenameFolder(FolderInfo folder)
        {
            if (folder.Name.Equals("Inbox", StringComparison.OrdinalIgnoreCase))
            {
                CustomDialog.Show(this, "Protected Folder", "The 'Inbox' folder is the default fallback staging directory and cannot be renamed.");
                return;
            }

            var prompt = new PromptDialog("Rename Folder", $"Enter a new name for '{folder.Name}':", folder.Name);
            prompt.Owner = this;
            if (prompt.ShowDialog() == true)
            {
                string newName = prompt.InputText.Trim();
                if (string.IsNullOrWhiteSpace(newName) || newName.Equals(folder.Name, StringComparison.OrdinalIgnoreCase))
                    return;

                string parentDir = Path.GetDirectoryName(folder.FullPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string newPath = Path.Combine(parentDir, newName);

                if (Directory.Exists(newPath))
                {
                    CustomDialog.Show(this, "Already Exists", $"A folder named '{newName}' already exists.");
                    return;
                }

                try
                {
                    Directory.Move(folder.FullPath, newPath);

                    if (_collapsedFolderPaths.Remove(folder.FullPath))
                    {
                        _collapsedFolderPaths.Add(newPath);
                    }

                    // Update stored colors if any
                    if (_engine.Config.FolderColors != null && _engine.Config.FolderColors.Remove(folder.Name, out var color))
                    {
                        _engine.Config.FolderColors[newName] = color;
                    }

                    if (folder.Parent == null && _engine.Config.RegisteredFolders != null)
                    {
                        string normOld = Path.GetFullPath(folder.FullPath).TrimEnd('\\', '/');
                        int idx = _engine.Config.RegisteredFolders.FindIndex(r => Path.GetFullPath(r).TrimEnd('\\', '/').Equals(normOld, StringComparison.OrdinalIgnoreCase));
                        if (idx >= 0)
                        {
                            _engine.Config.RegisteredFolders[idx] = newPath;
                        }
                    }
                    _engine.Config.Save();

                    _engine.LoadContexts();
                    RefreshFolders();
                    CustomDialog.Show(this, "Renamed", $"Folder renamed to '{newName}'.");
                }
                catch (Exception ex)
                {
                    CustomDialog.Show(this, "Error", $"Failed to rename folder:\n{ex.Message}");
                }
            }
        }

        private void DeleteFolder(FolderInfo folder)
        {
            if (folder.Name.Equals("Inbox", StringComparison.OrdinalIgnoreCase))
            {
                CustomDialog.Show(this, "Protected Folder", "The 'Inbox' folder is the protected fallback staging area and cannot be deleted.");
                return;
            }

            bool confirm = CustomDialog.Confirm(
                this,
                "Confirm Folder Deletion",
                $"Are you sure you want to delete folder '{folder.Name}'?\n\nAny existing files ({folder.FileCount} file{(folder.FileCount == 1 ? "" : "s")}) will be safely migrated into the 'Inbox' folder before deletion.",
                isDestructive: true,
                confirmBtnText: "Delete Folder");

            if (!confirm) return;

            try
            {
                string inboxDir = _engine.FolderPaths.TryGetValue("Inbox", out string? resolvedInbox)
                    ? resolvedInbox
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Inbox");
                if (!Directory.Exists(inboxDir)) Directory.CreateDirectory(inboxDir);

                // Migrate all non-context files
                if (Directory.Exists(folder.FullPath))
                {
                    foreach (var file in Directory.GetFiles(folder.FullPath))
                    {
                        if (Path.GetFileName(file).Equals(".context.md", StringComparison.OrdinalIgnoreCase) ||
                            Path.GetFileName(file).Equals("context.md", StringComparison.OrdinalIgnoreCase)) continue;
                        FileMover.MoveFile(file, inboxDir);
                    }
                    Directory.Delete(folder.FullPath, true);
                }

                if (_engine.Config.RegisteredFolders != null)
                {
                    string normDel = Path.GetFullPath(folder.FullPath).TrimEnd('\\', '/');
                    _engine.Config.RegisteredFolders.RemoveAll(r => Path.GetFullPath(r).TrimEnd('\\', '/').Equals(normDel, StringComparison.OrdinalIgnoreCase));
                }

                if (_engine.Config.FolderColors != null)
                {
                    _engine.Config.FolderColors.Remove(folder.Name);
                    _engine.Config.FolderColors.Remove(folder.RelativePath);
                }
                _engine.Config.Save();

                _collapsedFolderPaths.Remove(folder.FullPath);

                _engine.LoadContexts();
                RefreshFolders();
                UpdateStats();
                CustomDialog.Show(this, "Folder Deleted", $"Target folder '{folder.Name}' was deleted.\nAll contained files were safely moved to Inbox.");
            }
            catch (Exception ex)
            {
                CustomDialog.Show(this, "Error", $"Error deleting folder:\n{ex.Message}");
            }
        }

        private void CopySelectedPath_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedFolder != null)
            {
                try
                {
                    Clipboard.SetText(_selectedFolder.FullPath);
                    CustomDialog.Show(this, "Path Copied", $"Path copied to clipboard:\n{_selectedFolder.FullPath}");
                }
                catch (Exception ex)
                {
                    CustomDialog.Show(this, "Error", $"Failed to copy path: {ex.Message}");
                }
            }
        }

        private void TogglePath_Click(object sender, RoutedEventArgs e)
        {
            if (BorderSelectedPath.Visibility == Visibility.Visible)
            {
                BorderSelectedPath.Visibility = Visibility.Collapsed;
                BtnTogglePath.Content = "📍 Show Path";
            }
            else
            {
                BorderSelectedPath.Visibility = Visibility.Visible;
                BtnTogglePath.Content = "📍 Hide Path";
            }
        }

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag == null) return;
            string filePath = btn.Tag.ToString()!;
            if (File.Exists(filePath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{filePath}\"",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    CustomDialog.Show(this, "Error", $"Could not open file location:\n{ex.Message}");
                }
            }
        }

        private void OpenInExplorer(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath)) return;
            string resolved = _engine.Config.ResolvePath(fullPath);
            if (!Directory.Exists(resolved))
            {
                try { Directory.CreateDirectory(resolved); } catch { }
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{resolved}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                CustomDialog.Show(this, "Explorer Error", $"Could not open Explorer:\n{ex.Message}");
            }
        }

        private void OpenExplorer_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag == null) return;
            OpenInExplorer(btn.Tag.ToString()!);
        }

        private void ClearSortHistory_Click(object sender, RoutedEventArgs e)
        {
            int undoableCount = AllHistoryRecords.Count(r => r.Status == "moved" && File.Exists(r.DestinationPath));
            string reminder = undoableCount > 0
                ? $"\n\n⚠️ Reminder: There {(undoableCount == 1 ? "is" : "are")} still {undoableCount} action{(undoableCount == 1 ? "" : "s")} possible to undo, which will no longer be available when sorting history is cleared."
                : "\n\nThere are currently no undoable actions in history.";

            bool confirmed = CustomDialog.Confirm(
                this,
                "Clear Sorting History",
                $"Are you sure you want to clear the entire sorting history?{reminder}",
                isDestructive: true,
                confirmBtnText: "Clear History");

            if (confirmed)
            {
                _engine.History.ClearHistory();
                AllHistoryRecords.Clear();
                RecentRecords.Clear();
                UpdateStats();
                ShowNotificationToast("History Cleared", "Sorting history and audit logs have been reset.", "🗑️", 3000);
            }
        }

        private void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            var choiceDlg = new AddFolderChoiceDialog { Owner = this };
            if (choiceDlg.ShowDialog() != true) return;

            string ws = _engine.Config.ResolvePath(_engine.Config.WorkspaceDir);

            if (choiceDlg.IsCreateNew)
            {
                var dlg = new CreateFolderDialog(ws, _selectedFolder) { Owner = this };
                if (dlg.ShowDialog() == true)
                {
                    string folderName = dlg.FolderName;
                    if (string.IsNullOrWhiteSpace(folderName)) return;

                    char[] invalidChars = Path.GetInvalidFileNameChars();
                    if (folderName.IndexOfAny(invalidChars) >= 0)
                    {
                        CustomDialog.Show(this, "Invalid Name", "The folder name contains invalid characters.");
                        return;
                    }

                    string baseDir = !string.IsNullOrWhiteSpace(dlg.CustomLocationPath)
                        ? dlg.CustomLocationPath
                        : ((dlg.CreateAsSubFolder && _selectedFolder != null && Directory.Exists(_selectedFolder.FullPath))
                            ? _selectedFolder.FullPath
                            : Environment.GetFolderPath(Environment.SpecialFolder.Desktop));

                    string targetPath = Path.Combine(baseDir, folderName);

                    if (Directory.Exists(targetPath))
                    {
                        CustomDialog.Show(this, "Already Exists", $"Folder '{folderName}' already exists at this location.");
                        return;
                    }

                    string normTarget = Path.GetFullPath(targetPath).TrimEnd('\\', '/');
                    bool alreadyRegistered = (_engine.Config.RegisteredFolders != null &&
                        _engine.Config.RegisteredFolders.Any(r => Path.GetFullPath(r).TrimEnd('\\', '/').Equals(normTarget, StringComparison.OrdinalIgnoreCase))) ||
                        FolderItems.Any(f => Path.GetFullPath(f.FullPath).TrimEnd('\\', '/').Equals(normTarget, StringComparison.OrdinalIgnoreCase));

                    if (alreadyRegistered)
                    {
                        CustomDialog.Show(this, "Folder Already Added", $"A folder with this path is already registered in SortiFile's folder tree:\n{targetPath}");
                        return;
                    }

                    try
                    {
                        Directory.CreateDirectory(targetPath);
                        string ctxContent = dlg.ContextPrompt;
                        if (!ctxContent.StartsWith("#"))
                        {
                            ctxContent = $"# {folderName} Context\n\n{ctxContent}\n";
                        }
                        File.WriteAllText(Path.Combine(targetPath, ".context.md"), ctxContent);

                        if (!dlg.CreateAsSubFolder)
                        {
                            if (_engine.Config.RegisteredFolders == null)
                                _engine.Config.RegisteredFolders = new List<string>();

                            if (!_engine.Config.RegisteredFolders.Contains(targetPath, StringComparer.OrdinalIgnoreCase))
                            {
                                _engine.Config.RegisteredFolders.Add(targetPath);
                                _engine.Config.Save();
                            }
                        }

                        _engine.LoadContexts();
                        RefreshFolders();
                        UpdateStats();

                        var created = FolderItems.FirstOrDefault(f => f.FullPath.Equals(targetPath, StringComparison.OrdinalIgnoreCase));
                        if (created != null)
                        {
                            SelectFolder(created);
                        }

                        CustomDialog.Show(this, "Folder Created", $"Folder '{folderName}' created successfully.");
                    }
                    catch (Exception ex)
                    {
                        CustomDialog.Show(this, "Error", $"Failed to create folder:\n{ex.Message}");
                    }
                }
            }
            else
            {
                AddExistingFolderWorkflow();
            }
        }

        private void AddExistingFolderWorkflow()
        {
            var dlg = new OpenFolderDialog
            {
                Title = "Select Existing Folder to Add to SortiFile",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };
            if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.FolderName)) return;

            string sourceDir = dlg.FolderName;
            string folderName = Path.GetFileName(sourceDir);
            if (string.IsNullOrWhiteSpace(folderName)) folderName = sourceDir;

            // Validation checker: Check if folder is already registered in the database / folder tree
            string normSource = Path.GetFullPath(sourceDir).TrimEnd('\\', '/');
            bool alreadyInConfig = _engine.Config.RegisteredFolders != null &&
                _engine.Config.RegisteredFolders.Any(r => Path.GetFullPath(r).TrimEnd('\\', '/').Equals(normSource, StringComparison.OrdinalIgnoreCase));
            bool alreadyInTree = FolderItems.Any(f => Path.GetFullPath(f.FullPath).TrimEnd('\\', '/').Equals(normSource, StringComparison.OrdinalIgnoreCase));

            if (alreadyInConfig || alreadyInTree)
            {
                CustomDialog.Show(this, "Folder Already Added",
                    $"The folder '{folderName}' is already registered in SortiFile's folder tree.\n\nPath:\n{sourceDir}");
                return;
            }

            string ctxPath1 = Path.Combine(sourceDir, ".context.md");
            string ctxPath2 = Path.Combine(sourceDir, "context.md");
            string existingCtx = "";
            bool hasDetectedContext = false;

            if (File.Exists(ctxPath1))
            {
                try { existingCtx = File.ReadAllText(ctxPath1); hasDetectedContext = true; } catch { }
            }
            else if (File.Exists(ctxPath2))
            {
                try { existingCtx = File.ReadAllText(ctxPath2); hasDetectedContext = true; } catch { }
            }

            if (string.IsNullOrWhiteSpace(existingCtx))
            {
                existingCtx = $"# {folderName} Context\n\nFiles and documents relevant to {folderName}.\n";
            }

            var editDlg = new ContextEditDialog(folderName, existingCtx, isDetected: hasDetectedContext) { Owner = this };
            if (editDlg.ShowDialog() != true) return;

            try
            {
                string updatedCtx = editDlg.ContextText;
                if (string.IsNullOrWhiteSpace(updatedCtx))
                {
                    updatedCtx = $"# {folderName} Context\n\nFiles and documents relevant to {folderName}.\n";
                }
                File.WriteAllText(ctxPath1, updatedCtx);
            }
            catch { }

            // Subfolder check
            try
            {
                var allSubdirs = Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories);
                var subdirsMissingContext = new List<string>();

                foreach (var sub in allSubdirs)
                {
                    string sc1 = Path.Combine(sub, ".context.md");
                    string sc2 = Path.Combine(sub, "context.md");
                    if (!File.Exists(sc1) && !File.Exists(sc2))
                    {
                        subdirsMissingContext.Add(sub);
                    }
                }

                if (subdirsMissingContext.Count > 0)
                {
                    var subDlg = new SubfolderContextDialog(sourceDir, subdirsMissingContext) { Owner = this };
                    if (subDlg.ShowDialog() == true)
                    {
                        foreach (var kvp in subDlg.ContextResults)
                        {
                            try
                            {
                                string targetCtxFile = Path.Combine(kvp.Key, ".context.md");
                                File.WriteAllText(targetCtxFile, kvp.Value);
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }

            // Directly register existing folder into RegisteredFolders (no copying files)
            if (_engine.Config.RegisteredFolders == null)
            {
                _engine.Config.RegisteredFolders = new List<string>();
            }

            if (!_engine.Config.RegisteredFolders.Contains(sourceDir, StringComparer.OrdinalIgnoreCase))
            {
                _engine.Config.RegisteredFolders.Add(sourceDir);
                _engine.Config.Save();
            }

            _engine.LoadContexts();
            RefreshFolders();
            UpdateStats();

            var added = FolderItems.FirstOrDefault(f => f.FullPath.Equals(sourceDir, StringComparison.OrdinalIgnoreCase));
            if (added != null) SelectFolder(added);

            CustomDialog.Show(this, "Folder Added", $"Folder '{folderName}' and its taxonomy context are now registered in SortiFile.");
        }

        // ==================== CHANGELOG & GITHUB ====================

        private void OpenGithub_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/Pinnaclebc/SortiFile",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                CustomDialog.Show(this, "Error", $"Could not open GitHub link:\n{ex.Message}");
            }
        }

        private void OpenChangelogFile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string localChangelog = "";
                string[] candidates = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "changelog.txt"),
                    Path.GetFullPath("changelog.txt"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "changelog.txt"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "changelog.txt")
                };

                foreach (var path in candidates)
                {
                    if (File.Exists(path))
                    {
                        localChangelog = Path.GetFullPath(path);
                        break;
                    }
                }

                if (string.IsNullOrEmpty(localChangelog))
                {
                    localChangelog = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "changelog.txt");
                    if (!File.Exists(localChangelog))
                    {
                        File.WriteAllText(localChangelog, "SortiFile Changelog\n===================\n\nNo local changelog file found.\n");
                    }
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = localChangelog,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                CustomDialog.Show(this, "Error", $"Could not open local changelog file:\n{ex.Message}");
            }
        }

        // ==================== SETTINGS OPERATIONS & TUTORIAL ====================

        private void ShowOllamaTutorial_Click(object sender, MouseButtonEventArgs e)
        {
            var tutorial = new OllamaTutorialDialog();
            tutorial.Owner = this;
            tutorial.ShowDialog();
        }

        private async void TestOllama_Click(object sender, RoutedEventArgs e)
        {
            string host = TxtOllamaHost.Text.Trim();
            var testRouter = new OllamaRouter(host);
            bool ok = await testRouter.IsOllamaOnlineAsync();

            if (ok)
            {
                DotOllama.Fill = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
                TxtOllama.Text = $"Ollama: Connected";
                CustomDialog.Show(this, "Ollama Online", $"Successfully connected to Ollama server at:\n{host}");
            }
            else
            {
                DotOllama.Fill = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
                TxtOllama.Text = "Ollama: Fallback Mode";
                CustomDialog.Show(this, "Ollama Offline", $"Could not connect to Ollama at {host}.\nSortiFile will use intelligent heuristic routing fallback.");
            }
        }

        private void SliderThreshold_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtThresholdVal != null)
            {
                TxtThresholdVal.Text = $"{(int)SliderThreshold.Value}%";
            }
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            string selectedModel = "llama3:8b";
            if (ComboModel.SelectedItem is ComboBoxItem item && item.Content != null)
            {
                selectedModel = item.Content.ToString()!;
            }

            _engine.Config.WatchDir = TxtWatchDir.Text.Trim();
            _engine.Config.OllamaHost = TxtOllamaHost.Text.Trim();
            _engine.Config.OllamaModel = selectedModel;
            _engine.Config.ConfidenceThreshold = (int)SliderThreshold.Value;

            _engine.Config.Save();

            _engine.Router.Host = _engine.Config.OllamaHost;
            _engine.Router.Model = _engine.Config.OllamaModel;
            _engine.Router.Threshold = _engine.Config.ConfidenceThreshold;

            _engine.RestartWatcher();
            RefreshFolders();
            CheckOllamaStatusAsync();

            CustomDialog.Show(this, "Settings Saved", "Configuration saved successfully. Directory watcher and routing settings updated.");
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _engine.Stop();
        }
    }

    // ==================== UNIVERSAL CUSTOM DIALOG SYSTEM (ZERO WINDOWS GREY CHROME) ====================

    public static class CustomDialog
    {
        public static bool Confirm(Window owner, string title, string message, bool isDestructive = false, string confirmBtnText = "Confirm")
        {
            var dlg = new ConfirmWindow(title, message, isDestructive, confirmBtnText) { Owner = owner };
            return dlg.ShowDialog() == true;
        }

        public static void Show(Window owner, string title, string message)
        {
            var dlg = new MessageWindow(title, message) { Owner = owner };
            dlg.ShowDialog();
        }
    }

    public class ConfirmWindow : Window
    {
        public ConfirmWindow(string title, string message, bool isDestructive = false, string confirmBtnText = "Confirm")
        {
            Title = title;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            FontFamily = (FontFamily)Application.Current.FindResource("IosFontFamily");
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);

            var outerGrid = new Grid { Margin = new Thickness(24) };

            var card = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(24),
                Width = 440
            };
            card.Effect = new DropShadowEffect
            {
                BlurRadius = 32,
                ShadowDepth = 6,
                Direction = 270,
                Color = Color.FromRgb(0x0F, 0x17, 0x2A),
                Opacity = 0.20
            };

            var stack = new StackPanel();

            var titleBlock = new TextBlock
            {
                Text = title,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            stack.Children.Add(titleBlock);

            var msgBlock = new TextBlock
            {
                Text = message,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 18,
                Margin = new Thickness(0, 0, 0, 20)
            };
            stack.Children.Add(msgBlock);

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var btnCancel = new Button
            {
                Content = "Cancel",
                Width = 84,
                Height = 34,
                Margin = new Thickness(0, 0, 8, 0),
                Style = (Style)Application.Current.FindResource("ModernBtn")
            };
            btnCancel.Click += (s, e) => { DialogResult = false; Close(); };
            btnPanel.Children.Add(btnCancel);

            var btnConfirm = new Button
            {
                Content = confirmBtnText,
                MinWidth = 90,
                Height = 34,
                Padding = new Thickness(14, 0, 14, 0),
                Style = (Style)Application.Current.FindResource("PrimaryBtn"),
                IsDefault = true
            };

            if (isDestructive)
            {
                btnConfirm.Background = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)); // Red
            }

            btnConfirm.Click += (s, e) => { DialogResult = true; Close(); };
            btnPanel.Children.Add(btnConfirm);

            stack.Children.Add(btnPanel);
            card.Child = stack;
            outerGrid.Children.Add(card);

            Content = outerGrid;
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) { DialogResult = false; Close(); }
            };
        }
    }

    public class MessageWindow : Window
    {
        public MessageWindow(string title, string message)
        {
            Title = title;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            FontFamily = (FontFamily)Application.Current.FindResource("IosFontFamily");
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);

            var outerGrid = new Grid { Margin = new Thickness(24) };

            var card = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(24),
                Width = 420
            };
            card.Effect = new DropShadowEffect
            {
                BlurRadius = 32,
                ShadowDepth = 6,
                Direction = 270,
                Color = Color.FromRgb(0x0F, 0x17, 0x2A),
                Opacity = 0.20
            };

            var stack = new StackPanel();

            var titleBlock = new TextBlock
            {
                Text = title,
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            stack.Children.Add(titleBlock);

            var msgBlock = new TextBlock
            {
                Text = message,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 18,
                Margin = new Thickness(0, 0, 0, 18)
            };
            stack.Children.Add(msgBlock);

            var btnOk = new Button
            {
                Content = "Got It",
                Width = 90,
                Height = 34,
                Style = (Style)Application.Current.FindResource("PrimaryBtn"),
                HorizontalAlignment = HorizontalAlignment.Right,
                IsDefault = true
            };
            btnOk.Click += (s, e) => Close();
            stack.Children.Add(btnOk);

            card.Child = stack;
            outerGrid.Children.Add(card);

            Content = outerGrid;
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape || e.Key == Key.Enter) Close();
            };
        }
    }

    // ==================== MODERN INPUT PROMPT DIALOG ====================

    public class PromptDialog : Window
    {
        private readonly TextBox _textBox;

        public string InputText => _textBox.Text;

        public PromptDialog(string title, string message, string defaultVal = "")
        {
            Title = title;
            Width = 440;
            Height = 190;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));
            FontFamily = (FontFamily)Application.Current.FindResource("IosFontFamily");
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);

            var grid = new Grid { Margin = new Thickness(20) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var lbl = new TextBlock
            {
                Text = message,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(lbl, 0);
            grid.Children.Add(lbl);

            _textBox = new TextBox
            {
                Text = defaultVal,
                Height = 34,
                FontSize = 13,
                Padding = new Thickness(8, 6, 8, 6),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                Margin = new Thickness(0, 0, 0, 16)
            };
            Grid.SetRow(_textBox, 1);
            grid.Children.Add(_textBox);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var btnCancel = new Button
            {
                Content = "Cancel",
                Width = 80,
                Height = 32,
                Margin = new Thickness(0, 0, 8, 0),
                Style = (Style)Application.Current.FindResource("ModernBtn")
            };
            btnCancel.Click += (s, e) => { DialogResult = false; Close(); };
            buttonPanel.Children.Add(btnCancel);

            var btnOk = new Button
            {
                Content = "Confirm",
                Width = 84,
                Height = 32,
                Style = (Style)Application.Current.FindResource("PrimaryBtn"),
                IsDefault = true
            };
            btnOk.Click += (s, e) => { DialogResult = true; Close(); };
            buttonPanel.Children.Add(btnOk);

            Grid.SetRow(buttonPanel, 2);
            grid.Children.Add(buttonPanel);

            Content = grid;
            Loaded += (s, e) =>
            {
                _textBox.Focus();
                _textBox.SelectAll();
            };
        }
    }

    // ==================== RESET FOLDER TREE CONFIRMATION DIALOG ====================

    public class ResetFolderTreeDialog : Window
    {
        private readonly TextBox _txtInput;
        private readonly Button _btnConfirm;

        public bool IsConfirmed { get; private set; }

        public ResetFolderTreeDialog()
        {
            Title = "Reset Folder Tree";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            FontFamily = (FontFamily)Application.Current.FindResource("IosFontFamily");
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);

            var outerGrid = new Grid { Margin = new Thickness(24) };

            var card = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(24),
                Width = 460
            };
            card.Effect = new DropShadowEffect
            {
                BlurRadius = 32,
                ShadowDepth = 6,
                Direction = 270,
                Color = Color.FromRgb(0x0F, 0x17, 0x2A),
                Opacity = 0.20
            };

            var stack = new StackPanel();

            var titleBlock = new TextBlock
            {
                Text = "Reset Folder Tree",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            stack.Children.Add(titleBlock);

            var msgBlock = new TextBlock
            {
                Text = "This action will remove all registered folders and subfolders from SortiFile's active folder tree and routing taxonomy.\n\n" +
                       "• Your actual folders and files on disk will NOT be deleted.\n" +
                       "• To confirm, please type \"confirm\" in the box below:",
                FontSize = 12.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 18,
                Margin = new Thickness(0, 0, 0, 14)
            };
            stack.Children.Add(msgBlock);

            _txtInput = new TextBox
            {
                Height = 36,
                FontSize = 13,
                Padding = new Thickness(10, 6, 10, 6),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)),
                Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC)),
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
                Margin = new Thickness(0, 0, 0, 20)
            };
            stack.Children.Add(_txtInput);

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var btnCancel = new Button
            {
                Content = "Cancel",
                Width = 84,
                Height = 34,
                Margin = new Thickness(0, 0, 8, 0),
                Style = (Style)Application.Current.FindResource("ModernBtn"),
                Cursor = Cursors.Hand
            };
            btnCancel.Click += (s, e) => { DialogResult = false; Close(); };
            btnPanel.Children.Add(btnCancel);

            _btnConfirm = new Button
            {
                Content = "Reset Tree",
                MinWidth = 100,
                Height = 34,
                Padding = new Thickness(14, 0, 14, 0),
                Style = (Style)Application.Current.FindResource("PrimaryBtn"),
                Background = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)), // Red destructive
                IsEnabled = false,
                Cursor = Cursors.Hand
            };
            _btnConfirm.Click += (s, e) =>
            {
                if (_txtInput.Text.Trim().Equals("confirm", StringComparison.OrdinalIgnoreCase))
                {
                    IsConfirmed = true;
                    DialogResult = true;
                    Close();
                }
            };
            btnPanel.Children.Add(_btnConfirm);

            stack.Children.Add(btnPanel);
            card.Child = stack;
            outerGrid.Children.Add(card);

            _txtInput.TextChanged += (s, e) =>
            {
                _btnConfirm.IsEnabled = _txtInput.Text.Trim().Equals("confirm", StringComparison.OrdinalIgnoreCase);
            };

            _txtInput.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter && _btnConfirm.IsEnabled)
                {
                    _btnConfirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
            };

            Content = outerGrid;
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) { DialogResult = false; Close(); }
            };
            Loaded += (s, e) => _txtInput.Focus();
        }
    }

    // ==================== MODERN ADD FOLDER CHOICE DIALOG ====================

    public class AddFolderChoiceDialog : Window
    {
        public bool IsCreateNew { get; private set; } = true;

        public AddFolderChoiceDialog()
        {
            Title = "Add Folder to SortiFile";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            Width = 490;
            SizeToContent = SizeToContent.Height;
            FontFamily = (FontFamily)Application.Current.FindResource("IosFontFamily");
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);

            var outerGrid = new Grid { Margin = new Thickness(24) };

            var card = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(24),
                Effect = new DropShadowEffect
                {
                    BlurRadius = 32,
                    ShadowDepth = 6,
                    Direction = 270,
                    Color = Color.FromRgb(0x0F, 0x17, 0x2A),
                    Opacity = 0.20
                }
            };

            var stack = new StackPanel();

            var titleBlock = new TextBlock
            {
                Text = "Add Target Folder 📁",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
                Margin = new Thickness(0, 0, 0, 4)
            };
            stack.Children.Add(titleBlock);

            var subtitleBlock = new TextBlock
            {
                Text = "Choose whether to create a new taxonomy folder or register an existing directory from your drive.",
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 16)
            };
            stack.Children.Add(subtitleBlock);

            // Option 1: Create New Folder
            var cardNew = CreateCard("✨", "Create New Folder", "Specify a new folder name, target location, and mandatory .context.md AI classification prompt.");
            cardNew.MouseDown += (s, e) =>
            {
                IsCreateNew = true;
                DialogResult = true;
                Close();
            };
            stack.Children.Add(cardNew);

            // Option 2: Add Existing Folder
            var cardExisting = CreateCard("📂", "Add Existing Folder", "Select an existing folder from your PC. SortiFile will inspect its .context.md and scan all subfolders.");
            cardExisting.MouseDown += (s, e) =>
            {
                IsCreateNew = false;
                DialogResult = true;
                Close();
            };
            stack.Children.Add(cardExisting);

            var btnCancel = new Button
            {
                Content = "Cancel",
                Style = (Style)Application.Current.FindResource("ModernBtn"),
                Padding = new Thickness(16, 7, 16, 7),
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0),
                Cursor = Cursors.Hand
            };
            btnCancel.Click += (s, e) => { DialogResult = false; Close(); };
            stack.Children.Add(btnCancel);

            card.Child = stack;
            outerGrid.Children.Add(card);
            Content = outerGrid;
            KeyDown += (s, e) => { if (e.Key == Key.Escape) { DialogResult = false; Close(); } };
        }

        private Border CreateCard(string icon, string title, string description)
        {
            var b = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = Cursors.Hand
            };

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var iconBlock = new TextBlock
            {
                Text = icon,
                FontSize = 20,
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(iconBlock, 0);
            g.Children.Add(iconBlock);

            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A))
            });
            sp.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            });
            Grid.SetColumn(sp, 1);
            g.Children.Add(sp);

            b.MouseEnter += (s, e) =>
            {
                b.Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xFD, 0xEB));
                b.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFB, 0xC3, 0x05));
            };
            b.MouseLeave += (s, e) =>
            {
                b.Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));
                b.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0));
            };

            b.Child = g;
            return b;
        }
    }

    // ==================== MODERN CREATE FOLDER DIALOG (MANDATORY CONTEXT) ====================

    public class CreateFolderDialog : Window
    {
        private readonly TextBox _textBox;
        private readonly TextBox _txtContext;
        private readonly RadioButton? _rbSub;
        private readonly RadioButton _rbCustom;
        private readonly TextBlock _pathPreview;
        private readonly string _wsRoot;
        private readonly FolderInfo? _selectedFolder;
        private string _customLocationPath = "";

        public string FolderName => _textBox.Text.Trim();
        public string ContextPrompt => _txtContext.Text.Trim();
        public bool CreateAsSubFolder => _rbSub != null && _rbSub.IsChecked == true;
        public string CustomLocationPath => (!CreateAsSubFolder && !string.IsNullOrWhiteSpace(_customLocationPath)) ? _customLocationPath : "";

        public CreateFolderDialog(string wsRoot, FolderInfo? selectedFolder)
        {
            _wsRoot = wsRoot;
            _selectedFolder = selectedFolder;

            Title = "Create New Folder";
            Width = 500;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));
            FontFamily = (FontFamily)Application.Current.FindResource("IosFontFamily");
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);

            var sp = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };

            var titleBlock = new TextBlock
            {
                Text = "Enter New Folder Name:",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
                Margin = new Thickness(0, 0, 0, 6)
            };
            sp.Children.Add(titleBlock);

            _textBox = new TextBox
            {
                Height = 34,
                FontSize = 13,
                Padding = new Thickness(8, 6, 8, 6),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)),
                Background = Brushes.White,
                Margin = new Thickness(0, 0, 0, 12)
            };
            sp.Children.Add(_textBox);

            var locLabel = new TextBlock
            {
                Text = "DESTINATION LOCATION",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                Margin = new Thickness(0, 0, 0, 6)
            };
            sp.Children.Add(locLabel);

            var radioPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };

            if (_selectedFolder != null)
            {
                _rbSub = new RadioButton
                {
                    Content = $"↳ Sub-folder in '{_selectedFolder.Name}'",
                    IsChecked = true,
                    FontSize = 11.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
                    Margin = new Thickness(0, 0, 14, 4),
                    Cursor = Cursors.Hand
                };
                _rbSub.Checked += (s, e) => UpdatePreview();
                radioPanel.Children.Add(_rbSub);
            }

            var customContainer = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 4) };

            _rbCustom = new RadioButton
            {
                Content = _selectedFolder != null ? "📍 Custom Location..." : "📍 Target Location...",
                IsChecked = (_selectedFolder == null),
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand
            };
            _rbCustom.Checked += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(_customLocationPath))
                {
                    ChooseCustomPath();
                }
                UpdatePreview();
            };
            customContainer.Children.Add(_rbCustom);

            var btnBrowseCustom = new Button
            {
                Content = "Browse...",
                FontSize = 10,
                Padding = new Thickness(8, 2, 8, 2),
                Height = 22,
                Cursor = Cursors.Hand,
                Style = (Style)Application.Current.FindResource("ModernBtn"),
                VerticalAlignment = VerticalAlignment.Center
            };
            btnBrowseCustom.Click += (s, e) =>
            {
                _rbCustom.IsChecked = true;
                ChooseCustomPath();
            };
            customContainer.Children.Add(btnBrowseCustom);

            radioPanel.Children.Add(customContainer);
            sp.Children.Add(radioPanel);

            _pathPreview = new TextBlock
            {
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 0, 12)
            };
            sp.Children.Add(_pathPreview);

            // Mandatory .context.md prompt
            var ctxLabel = new TextBlock
            {
                Text = "TAXONOMY DESCRIPTION (.context.md prompt - MANDATORY):",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                Margin = new Thickness(0, 0, 0, 4)
            };
            sp.Children.Add(ctxLabel);

            _txtContext = new TextBox
            {
                Height = 65,
                FontSize = 11,
                Padding = new Thickness(8, 6, 8, 6),
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)),
                Background = Brushes.White,
                Margin = new Thickness(0, 0, 0, 16)
            };
            sp.Children.Add(_txtContext);

            _textBox.TextChanged += (s, e) =>
            {
                UpdatePreview();
                if (string.IsNullOrWhiteSpace(_txtContext.Text) || _txtContext.Text.StartsWith("Files and documents relevant to"))
                {
                    string nm = string.IsNullOrWhiteSpace(_textBox.Text) ? "" : _textBox.Text.Trim();
                    if (!string.IsNullOrWhiteSpace(nm))
                    {
                        _txtContext.Text = $"Files and documents relevant to {nm}.";
                    }
                }
            };
            UpdatePreview();

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var btnCancel = new Button
            {
                Content = "Cancel",
                Width = 84,
                Height = 32,
                Margin = new Thickness(0, 0, 8, 0),
                Style = (Style)Application.Current.FindResource("ModernBtn")
            };
            btnCancel.Click += (s, e) => { DialogResult = false; Close(); };
            buttonPanel.Children.Add(btnCancel);

            var btnOk = new Button
            {
                Content = "Create Folder",
                Width = 110,
                Height = 32,
                Style = (Style)Application.Current.FindResource("PrimaryBtn"),
                IsDefault = true
            };
            btnOk.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(FolderName))
                {
                    CustomDialog.Show(this, "Folder Name Required", "Please enter a valid folder name.");
                    _textBox.Focus();
                    return;
                }
                char[] invalidChars = Path.GetInvalidFileNameChars();
                if (FolderName.IndexOfAny(invalidChars) >= 0)
                {
                    CustomDialog.Show(this, "Invalid Name", "The folder name contains invalid characters.");
                    _textBox.Focus();
                    return;
                }
                if (!CreateAsSubFolder && string.IsNullOrWhiteSpace(_customLocationPath))
                {
                    CustomDialog.Show(this, "Destination Required", "Please choose a destination location for this folder.");
                    ChooseCustomPath();
                    if (string.IsNullOrWhiteSpace(_customLocationPath)) return;
                }
                if (string.IsNullOrWhiteSpace(ContextPrompt))
                {
                    CustomDialog.Show(this, "Context Required", "Please provide a classification context for this folder (.context.md is mandatory).");
                    _txtContext.Focus();
                    return;
                }
                DialogResult = true;
                Close();
            };
            buttonPanel.Children.Add(btnOk);
            sp.Children.Add(buttonPanel);

            Content = sp;
            Loaded += (s, e) => _textBox.Focus();
        }

        private void ChooseCustomPath()
        {
            var pickDlg = new OpenFolderDialog
            {
                Title = "Select Target Folder Location",
                InitialDirectory = !string.IsNullOrWhiteSpace(_customLocationPath) ? _customLocationPath : _wsRoot
            };
            if (pickDlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(pickDlg.FolderName))
            {
                _customLocationPath = pickDlg.FolderName;
                _rbCustom.IsChecked = true;
                UpdatePreview();
            }
            else if (string.IsNullOrWhiteSpace(_customLocationPath))
            {
                if (_rbSub != null)
                {
                    _rbSub.IsChecked = true;
                }
            }
        }

        private void UpdatePreview()
        {
            string name = string.IsNullOrWhiteSpace(_textBox.Text) ? "NewFolder" : _textBox.Text.Trim();
            string parent;

            if (CreateAsSubFolder && _selectedFolder != null)
            {
                parent = _selectedFolder.FullPath;
            }
            else if (!string.IsNullOrWhiteSpace(_customLocationPath))
            {
                parent = _customLocationPath;
            }
            else
            {
                parent = _wsRoot;
            }

            try
            {
                string rel = Path.GetRelativePath(_wsRoot, Path.Combine(parent, name));
                if (!rel.StartsWith("..") && !Path.IsPathRooted(rel))
                {
                    _pathPreview.Text = $"📍 Will create in: {Path.Combine(Path.GetFileName(_wsRoot), rel)}";
                }
                else
                {
                    _pathPreview.Text = $"📍 Will create in: {Path.Combine(parent, name)}";
                }
            }
            catch
            {
                _pathPreview.Text = $"📍 Target: {Path.Combine(parent, name)}";
            }
        }
    }

    // ==================== SUBFOLDERS MISSING CONTEXT POPUP DIALOG ====================

    public class SubfolderContextDialog : Window
    {
        public Dictionary<string, string> ContextResults { get; } = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(string subPath, TextBox txtBox)> _editors = new();

        public SubfolderContextDialog(string rootDir, List<string> missingSubfolders)
        {
            Title = "Subfolders Missing Context";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            Width = 560;
            Height = 520;
            FontFamily = (FontFamily)Application.Current.FindResource("IosFontFamily");
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);

            var outerGrid = new Grid { Margin = new Thickness(20) };

            var card = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(22),
                Effect = new DropShadowEffect
                {
                    BlurRadius = 32,
                    ShadowDepth = 6,
                    Direction = 270,
                    Color = Color.FromRgb(0x0F, 0x17, 0x2A),
                    Opacity = 0.20
                }
            };

            var mainDock = new DockPanel { LastChildFill = true };

            var headerStack = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            headerStack.Children.Add(new TextBlock
            {
                Text = $"Subfolders Missing Context ({missingSubfolders.Count}) 📁",
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A))
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = "The following subfolders do not have a .context.md file. You can enter classification prompts for each, or skip and create placeholder contexts for all.",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            });
            DockPanel.SetDock(headerStack, Dock.Top);
            mainDock.Children.Add(headerStack);

            var btnPanel = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            btnPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            btnPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var btnPlaceholder = new Button
            {
                Content = "⚡ Skip & Add Placeholders",
                Style = (Style)Application.Current.FindResource("ModernBtn"),
                Padding = new Thickness(14, 7, 14, 7),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                ToolTip = "Automatically generate placeholder .context.md files for all subfolders"
            };
            btnPlaceholder.Click += (s, e) =>
            {
                foreach (var sub in missingSubfolders)
                {
                    string subName = Path.GetFileName(sub);
                    ContextResults[sub] = $"# {subName} Context\n\nFiles and documents relevant to {subName}.\n";
                }
                DialogResult = true;
                Close();
            };
            Grid.SetColumn(btnPlaceholder, 0);
            btnPanel.Children.Add(btnPlaceholder);

            var rightActions = new StackPanel { Orientation = Orientation.Horizontal };

            var btnCancel = new Button
            {
                Content = "Cancel",
                Style = (Style)Application.Current.FindResource("ModernBtn"),
                Padding = new Thickness(14, 7, 14, 7),
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = Cursors.Hand
            };
            btnCancel.Click += (s, e) => { DialogResult = false; Close(); };
            rightActions.Children.Add(btnCancel);

            var btnSave = new Button
            {
                Content = "Save Contexts",
                Style = (Style)Application.Current.FindResource("PrimaryBtn"),
                Padding = new Thickness(16, 7, 16, 7),
                Cursor = Cursors.Hand,
                IsDefault = true
            };
            btnSave.Click += (s, e) =>
            {
                foreach (var (subPath, tb) in _editors)
                {
                    string val = tb.Text.Trim();
                    string subName = Path.GetFileName(subPath);
                    if (string.IsNullOrWhiteSpace(val))
                    {
                        val = $"# {subName} Context\n\nFiles and documents relevant to {subName}.\n";
                    }
                    else if (!val.StartsWith("#"))
                    {
                        val = $"# {subName} Context\n\n{val}\n";
                    }
                    ContextResults[subPath] = val;
                }
                DialogResult = true;
                Close();
            };
            rightActions.Children.Add(btnSave);

            Grid.SetColumn(rightActions, 1);
            btnPanel.Children.Add(rightActions);

            DockPanel.SetDock(btnPanel, Dock.Bottom);
            mainDock.Children.Add(btnPanel);

            var sv = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(0, 0, 4, 0)
            };
            var itemsStack = new StackPanel();

            foreach (var sub in missingSubfolders)
            {
                string relPath = "";
                try { relPath = Path.GetRelativePath(rootDir, sub); } catch { relPath = sub; }
                string subName = Path.GetFileName(sub);

                var itemBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 0, 0, 8)
                };

                var itemStack = new StackPanel();
                itemStack.Children.Add(new TextBlock
                {
                    Text = $"📁 {relPath}",
                    FontSize = 11.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
                    Margin = new Thickness(0, 0, 0, 4)
                });

                var tb = new TextBox
                {
                    Text = $"Files and documents relevant to {subName}.",
                    Height = 44,
                    FontSize = 11,
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Padding = new Thickness(8, 4, 8, 4),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)),
                    Background = Brushes.White
                };
                itemStack.Children.Add(tb);
                _editors.Add((sub, tb));

                itemBorder.Child = itemStack;
                itemsStack.Children.Add(itemBorder);
            }

            sv.Content = itemsStack;
            mainDock.Children.Add(sv);

            card.Child = mainDock;
            outerGrid.Children.Add(card);
            Content = outerGrid;
            KeyDown += (s, e) => { if (e.Key == Key.Escape) { DialogResult = false; Close(); } };
        }
    }

    // ==================== MODERN CONTEXT.MD EDITOR DIALOG ====================

    public class ContextEditDialog : Window
    {
        private readonly TextBox _textBox;

        public string ContextText => _textBox.Text;

        public ContextEditDialog(string folderName, string currentContext, bool isDetected = false)
        {
            Title = isDetected ? $"Review & Edit .context.md — {folderName}" : $"Add .context.md — {folderName}";
            Width = 620;
            Height = 440;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));
            FontFamily = (FontFamily)Application.Current.FindResource("IosFontFamily");
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);

            var grid = new Grid { Margin = new Thickness(24) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var titleBlock = new TextBlock
            {
                Text = isDetected ? $"Detected Taxonomy Context for '{folderName}'" : $"Taxonomy Context for '{folderName}'",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
                Margin = new Thickness(0, 0, 0, 4)
            };
            Grid.SetRow(titleBlock, 0);
            grid.Children.Add(titleBlock);

            var descBlock = new TextBlock
            {
                Text = isDetected
                    ? "An existing .context.md was detected. You can review or edit its classification instructions before finalizing."
                    : "No .context.md was detected. Enter a classification prompt below so the local AI engine can route files into this folder.",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                Margin = new Thickness(0, 0, 0, 14)
            };
            Grid.SetRow(descBlock, 1);
            grid.Children.Add(descBlock);

            _textBox = new TextBox
            {
                Text = currentContext,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = (FontFamily)Application.Current.FindResource("IosFontFamily"),
                FontSize = 12,
                Padding = new Thickness(12),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)),
                Background = Brushes.White,
                Margin = new Thickness(0, 0, 0, 16)
            };
            Grid.SetRow(_textBox, 2);
            grid.Children.Add(_textBox);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var btnCancel = new Button
            {
                Content = "Cancel",
                Width = 84,
                Height = 34,
                Margin = new Thickness(0, 0, 8, 0),
                Style = (Style)Application.Current.FindResource("ModernBtn")
            };
            btnCancel.Click += (s, e) => { DialogResult = false; Close(); };
            buttonPanel.Children.Add(btnCancel);

            var btnSave = new Button
            {
                Content = "Save Context",
                Width = 110,
                Height = 34,
                Style = (Style)Application.Current.FindResource("PrimaryBtn"),
                IsDefault = true
            };
            btnSave.Click += (s, e) => { DialogResult = true; Close(); };
            buttonPanel.Children.Add(btnSave);

            Grid.SetRow(buttonPanel, 3);
            grid.Children.Add(buttonPanel);

            Content = grid;
            Loaded += (s, e) => _textBox.Focus();
        }
    }

    // ==================== OLLAMA LOCAL MODEL TUTORIAL MODAL ====================

    public class OllamaTutorialDialog : Window
    {
        public OllamaTutorialDialog()
        {
            Title = "Guide: Install & Configure Local AI (Ollama)";
            Width = 660;
            Height = 540;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));
            FontFamily = (FontFamily)Application.Current.FindResource("IosFontFamily");
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);

            var dock = new DockPanel { Margin = new Thickness(24) };

            // Header
            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
            DockPanel.SetDock(header, Dock.Top);

            var title = new TextBlock
            {
                Text = "🧠 Local AI Installation Guide",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A))
            };
            header.Children.Add(title);

            var sub = new TextBlock
            {
                Text = "Run powerful open-source AI models directly on your Windows PC with 100% privacy.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                Margin = new Thickness(0, 4, 0, 0)
            };
            header.Children.Add(sub);
            dock.Children.Add(header);

            // Bottom OK button
            var btnOk = new Button
            {
                Content = "Got It, Close Guide",
                Height = 36,
                Width = 140,
                Style = (Style)Application.Current.FindResource("PrimaryBtn"),
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            btnOk.Click += (s, e) => Close();
            DockPanel.SetDock(btnOk, Dock.Bottom);
            dock.Children.Add(btnOk);

            // Scrollable Content
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var content = new StackPanel();

            // Step 1
            AddStepCard(content, "1. Download & Install Ollama",
                "Ollama is a lightweight local model runner for Windows. Download the Windows installer from ollama.com and run the setup.",
                "Visit ollama.com ↗", () =>
                {
                    try { Process.Start(new ProcessStartInfo { FileName = "https://ollama.com", UseShellExecute = true }); } catch { }
                });

            // Step 2
            AddStepCard(content, "2. Pull a Recommended Model",
                "Open Windows Terminal or PowerShell and run the following command to download your chosen model:\n\n• For standard PCs (Recommended):\n  ollama run llama3:8b\n\n• For lighter/faster performance:\n  ollama run qwen2.5:7b\n\nOnce download completes, Ollama will run in the background in your system tray.",
                null, null);

            // Step 3
            AddStepCard(content, "3. Connect in SortiFile Settings",
                "1. Keep Ollama Host URL as: http://127.0.0.1:11434\n2. Select your downloaded model in the Target Model dropdown (e.g. llama3:8b).\n3. Click 'Test Connection'. The top status pill will turn emerald green (Ollama: Connected)!\n4. Click 'Save Configuration'.",
                null, null);

            // Note
            var noteBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xEC, 0xFD, 0xF5)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xA7, 0xF3, 0xD0)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 4, 0, 10)
            };
            var noteText = new TextBlock
            {
                Text = "🛡️ Offline Guarantee: If Ollama is ever closed or offline, SortiFile automatically switches to its built-in rule and semantic fallback router, keeping file organization 100% uninterrupted.",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x06, 0x5F, 0x46)),
                TextWrapping = TextWrapping.Wrap
            };
            noteBorder.Child = noteText;
            content.Children.Add(noteBorder);

            scroll.Content = content;
            dock.Children.Add(scroll);

            Content = dock;
        }

        private static void AddStepCard(StackPanel parent, string title, string text, string? actionText, Action? action)
        {
            var b = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 10)
            };

            var sp = new StackPanel();
            var t = new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
                Margin = new Thickness(0, 0, 0, 4)
            };
            sp.Children.Add(t);

            var d = new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 16
            };
            sp.Children.Add(d);

            if (!string.IsNullOrEmpty(actionText) && action != null)
            {
                var btn = new Button
                {
                    Content = actionText,
                    Style = (Style)Application.Current.FindResource("ModernBtn"),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 8, 0, 0),
                    Padding = new Thickness(10, 4, 10, 4),
                    FontSize = 11
                };
                btn.Click += (s, e) => action();
                sp.Children.Add(btn);
            }

            b.Child = sp;
            parent.Children.Add(b);
        }
    }
}
