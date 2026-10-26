using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SortiFileWpf.Core;
using SortiFileWpf.Models;

namespace SortiFileWpf
{
    public class InitialFolderItem
    {
        public string Name { get; set; } = "";
        public string Context { get; set; } = "";
        public string Icon { get; set; } = "📁";
        public bool IsRemovable { get; set; } = true;
    }

    public class SetupWizardWindow : Window
    {
        private readonly MainWindow _mainWindow;
        private readonly FileWatcherEngine _engine;
        private string _selectedWatchPath;
        private string _selectedWorkspacePath;
        private int _currentStep = 1;
        private bool _isSkippedInitialFolders = false;

        // Step container grids
        private readonly Grid _step1Panel;
        private readonly Grid _step2Panel;
        private readonly Grid _step3Panel;
        private readonly Grid _step4Panel;
        private readonly Grid _step5Panel;

        // Step indicator badges
        private readonly Border _pillStep1;
        private readonly Border _pillStep2;
        private readonly Border _pillStep3;
        private readonly Border _pillStep4;
        private readonly Border _pillStep5;
        private readonly TextBlock _txtPill1;
        private readonly TextBlock _txtPill2;
        private readonly TextBlock _txtPill3;
        private readonly TextBlock _txtPill4;
        private readonly TextBlock _txtPill5;

        // Step 2 Watch Dir path display & cards
        private readonly TextBlock _txtSelectedWatchPath;
        private readonly Border _watchCardDefault;
        private readonly Border _watchCardDownloads;
        private readonly Border _watchCardCustom;

        // Step 3 Workspace Dir path display & cards
        private readonly TextBlock _txtSelectedWorkspacePath;
        private readonly Border _wsCardDefault;
        private readonly Border _wsCardDocs;
        private readonly Border _wsCardCustom;

        // Step 4 Initial Folders container & list
        private readonly List<InitialFolderItem> _initialFolders = new List<InitialFolderItem>();
        private readonly StackPanel _initialFoldersContainer;
        private readonly TextBlock _txtFoldersCount;

        // Bottom navigation buttons
        private readonly Button _btnNavBack;
        private readonly Button _btnNavNext;

        public SetupWizardWindow(MainWindow mainWindow, FileWatcherEngine engine)
        {
            _mainWindow = mainWindow;
            _engine = engine;
            _selectedWatchPath = _engine.Config.WatchDir;
            _selectedWorkspacePath = _engine.Config.WorkspaceDir;

            Title = "SortiFile - Welcome & Setup Wizard";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Owner = mainWindow;
            ShowInTaskbar = false;
            Width = 650;
            Height = 700;
            FontFamily = (FontFamily)Application.Current.FindResource("IosFontFamily");
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);

            var outerGrid = new Grid { Margin = new Thickness(16, 18, 16, 18) };

            var card = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(26, 20, 26, 20),
                Effect = new DropShadowEffect
                {
                    BlurRadius = 36,
                    ShadowDepth = 6,
                    Direction = 270,
                    Color = Color.FromRgb(0x0F, 0x17, 0x2A),
                    Opacity = 0.22
                }
            };

            var mainStack = new DockPanel { LastChildFill = true };

            // ==================== TOP HEADER: BRANDING & CLOSE BUTTON ====================
            var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var brandStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            var logoBorder = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromRgb(0xFB, 0xC3, 0x05)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xA0, 0x02)),
                BorderThickness = new Thickness(1.5),
                Margin = new Thickness(0, 0, 10, 0),
                ClipToBounds = true
            };
            try
            {
                var logoImg = new Image
                {
                    Source = new BitmapImage(new Uri("pack://application:,,,/Assets/SortiFile_Logo_PNG.png")),
                    Stretch = Stretch.UniformToFill,
                    Width = 34,
                    Height = 34
                };
                logoBorder.Child = logoImg;
            }
            catch { }
            brandStack.Children.Add(logoBorder);

            var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var appTitle = new TextBlock
            {
                Text = "SortiFile Setup Wizard",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A))
            };
            var appSubtitle = new TextBlock
            {
                Text = "Interactive First-Time Configuration & Tutorial",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x78, 0x35, 0x0F))
            };
            titleStack.Children.Add(appTitle);
            titleStack.Children.Add(appSubtitle);
            brandStack.Children.Add(titleStack);
            Grid.SetColumn(brandStack, 0);
            headerGrid.Children.Add(brandStack);

            var btnClose = new Button
            {
                Content = "✕",
                Width = 28,
                Height = 28,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                Background = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = "Close Wizard"
            };
            btnClose.Click += (s, e) => { Close(); };
            Grid.SetColumn(btnClose, 1);
            headerGrid.Children.Add(btnClose);

            DockPanel.SetDock(headerGrid, Dock.Top);
            mainStack.Children.Add(headerGrid);

            // ==================== STEP PROGRESS INDICATOR PILLS (5 STEPS) ====================
            var stepsBar = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(4),
                Margin = new Thickness(0, 0, 0, 14)
            };
            var stepsGrid = new UniformGrid { Columns = 5 };

            _pillStep1 = CreateStepPill("1. Welcome", 1, out _txtPill1, true);
            _pillStep2 = CreateStepPill("2. Watch Dir", 2, out _txtPill2, false);
            _pillStep3 = CreateStepPill("3. Workspace", 3, out _txtPill3, false);
            _pillStep4 = CreateStepPill("4. Folders", 4, out _txtPill4, false);
            _pillStep5 = CreateStepPill("5. Guide", 5, out _txtPill5, false);

            stepsGrid.Children.Add(_pillStep1);
            stepsGrid.Children.Add(_pillStep2);
            stepsGrid.Children.Add(_pillStep3);
            stepsGrid.Children.Add(_pillStep4);
            stepsGrid.Children.Add(_pillStep5);
            stepsBar.Child = stepsGrid;

            DockPanel.SetDock(stepsBar, Dock.Top);
            mainStack.Children.Add(stepsBar);

            // ==================== STEP 1: WELCOME & HOW IT WORKS ====================
            _step1Panel = new Grid { Visibility = Visibility.Visible };
            var step1Stack = new StackPanel();

            var s1Title = new TextBlock
            {
                Text = "Welcome to SortiFile 🗂️",
                FontSize = 19,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
                Margin = new Thickness(0, 0, 0, 4)
            };
            var s1Desc = new TextBlock
            {
                Text = "SortiFile is an autonomous, privacy-first local file organization engine. All operations happen 100% on your machine—no files, text, or metadata ever leave your computer.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 17,
                Margin = new Thickness(0, 0, 0, 10)
            };
            step1Stack.Children.Add(s1Title);
            step1Stack.Children.Add(s1Desc);

            step1Stack.Children.Add(CreateFeatureCard("👁️", "Autonomous Watch Folder", "Monitors your designated incoming folder (e.g. Downloads) and automatically routes newly saved files into the right folder."));
            step1Stack.Children.Add(CreateFeatureCard("🗄️", "Custom Target Workspace", "Direct sorted files into any dedicated library folder or personal drive with complete privacy."));
            step1Stack.Children.Add(CreateFeatureCard("🧠", "Smart Content Classification", "Inspects document text, PDF extracts, and media metadata using local LLMs or rule heuristics matching .context.md prompts."));
            step1Stack.Children.Add(CreateFeatureCard("↺", "Zero-Risk 1-Click Undo", "Every move is safely logged in your audit history. You can undo or revert any sorted file at any time."));

            _step1Panel.Children.Add(step1Stack);

            // ==================== STEP 2: CHOOSE WATCH FOLDER ====================
            _step2Panel = new Grid { Visibility = Visibility.Collapsed };
            var step2Stack = new StackPanel();

            var s2Title = new TextBlock
            {
                Text = "Choose Your Incoming Watch Folder 📂",
                FontSize = 19,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
                Margin = new Thickness(0, 0, 0, 4)
            };
            var s2Desc = new TextBlock
            {
                Text = "Where do you typically save or download files? Select an incoming directory for SortiFile to monitor. You can change this anytime in Settings.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 17,
                Margin = new Thickness(0, 0, 0, 12)
            };
            step2Stack.Children.Add(s2Title);
            step2Stack.Children.Add(s2Desc);

            _watchCardDefault = CreateOptionCard("📁", "Standard Watch Folder (Recommended)", "Dedicated 'Watch_Folder' directory created inside your SortiFile app directory.", true);
            _watchCardDefault.MouseDown += (s, e) => { SelectWatchOption("Watch_Folder", _watchCardDefault); };
            step2Stack.Children.Add(_watchCardDefault);

            string downloadsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            _watchCardDownloads = CreateOptionCard("📥", "Windows Downloads Folder", $"Auto-organize your browser downloads:\n{downloadsDir}", false);
            _watchCardDownloads.MouseDown += (s, e) => { SelectWatchOption(downloadsDir, _watchCardDownloads); };
            step2Stack.Children.Add(_watchCardDownloads);

            _watchCardCustom = CreateOptionCard("🔍", "Choose Custom Directory...", "Select any custom folder on your PC, Desktop, or secondary drive.", false);
            _watchCardCustom.MouseDown += (s, e) =>
            {
                var dlg = new OpenFolderDialog
                {
                    Title = "Select Custom Incoming Watch Folder",
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                };
                if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FolderName))
                {
                    SelectWatchOption(dlg.FolderName, _watchCardCustom);
                }
            };
            step2Stack.Children.Add(_watchCardCustom);

            var watchPathPill = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xFC, 0xE8)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xFE, 0xF0, 0x8A)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 6, 0, 12)
            };
            var watchPathStack = new StackPanel();
            watchPathStack.Children.Add(new TextBlock
            {
                Text = "✓ CURRENTLY SELECTED WATCH DIRECTORY:",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x85, 0x4D, 0x0E)),
                Margin = new Thickness(0, 0, 0, 2)
            });
            _txtSelectedWatchPath = new TextBlock
            {
                Text = _engine.Config.ResolvePath(_selectedWatchPath),
                FontSize = 11,
                FontFamily = new FontFamily("Consolas, Segoe UI Variable Text"),
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            watchPathStack.Children.Add(_txtSelectedWatchPath);
            watchPathPill.Child = watchPathStack;
            step2Stack.Children.Add(watchPathPill);

            _step2Panel.Children.Add(step2Stack);

            // ==================== STEP 3: CHOOSE WORKSPACE DIRECTORY ====================
            _step3Panel = new Grid { Visibility = Visibility.Collapsed };
            var step3Stack = new StackPanel();

            var s3Title = new TextBlock
            {
                Text = "Choose Your Workspace Directory 🗄️",
                FontSize = 19,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
                Margin = new Thickness(0, 0, 0, 4)
            };
            var s3Desc = new TextBlock
            {
                Text = "Where should your organized files and folders live? SortiFile will create categorized folders and sort files into this destination. You can change this anytime in Settings.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 17,
                Margin = new Thickness(0, 0, 0, 12)
            };
            step3Stack.Children.Add(s3Title);
            step3Stack.Children.Add(s3Desc);

            _wsCardDefault = CreateOptionCard("📁", "Standard Workspace Folder (Recommended)", "Dedicated 'SortiFile_Workspace' directory created inside your SortiFile app directory.", true);
            _wsCardDefault.MouseDown += (s, e) => { SelectWorkspaceOption("SortiFile_Workspace", _wsCardDefault); };
            step3Stack.Children.Add(_wsCardDefault);

            string docsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SortiFile_Workspace");
            _wsCardDocs = CreateOptionCard("📄", "Windows Documents Folder", $"Create 'SortiFile_Workspace' inside your personal Documents:\n{docsDir}", false);
            _wsCardDocs.MouseDown += (s, e) => { SelectWorkspaceOption(docsDir, _wsCardDocs); };
            step3Stack.Children.Add(_wsCardDocs);

            _wsCardCustom = CreateOptionCard("🔍", "Choose Custom Directory...", "Select any custom directory or drive on your PC via folder picker.", false);
            _wsCardCustom.MouseDown += (s, e) =>
            {
                var dlg = new OpenFolderDialog
                {
                    Title = "Select Custom Target Workspace Directory",
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                };
                if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FolderName))
                {
                    SelectWorkspaceOption(dlg.FolderName, _wsCardCustom);
                }
            };
            step3Stack.Children.Add(_wsCardCustom);

            var wsPathPill = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xFC, 0xE8)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xFE, 0xF0, 0x8A)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 6, 0, 12)
            };
            var wsPathStack = new StackPanel();
            wsPathStack.Children.Add(new TextBlock
            {
                Text = "✓ CURRENTLY SELECTED WORKSPACE DIRECTORY:",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x85, 0x4D, 0x0E)),
                Margin = new Thickness(0, 0, 0, 2)
            });
            _txtSelectedWorkspacePath = new TextBlock
            {
                Text = _engine.Config.ResolvePath(_selectedWorkspacePath),
                FontSize = 11,
                FontFamily = new FontFamily("Consolas, Segoe UI Variable Text"),
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            wsPathStack.Children.Add(_txtSelectedWorkspacePath);
            wsPathPill.Child = wsPathStack;
            step3Stack.Children.Add(wsPathPill);

            _step3Panel.Children.Add(step3Stack);

            // ==================== STEP 4: SETUP INITIAL FOLDERS & CONTEXT ====================
            _step4Panel = new Grid { Visibility = Visibility.Collapsed };
            var step4Stack = new StackPanel();

            var s4Title = new TextBlock
            {
                Text = "Setup Initial Folders & Context 🗂️",
                FontSize = 19,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
                Margin = new Thickness(0, 0, 0, 4)
            };
            var s4Desc = new TextBlock
            {
                Text = "SortiFile categorizes files into target folders according to each folder's .context.md description. You can customize your initial folders below or skip this step to use curated defaults.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 17,
                Margin = new Thickness(0, 0, 0, 10)
            };
            step4Stack.Children.Add(s4Title);
            step4Stack.Children.Add(s4Desc);

            // Skip banner option card
            var skipBanner = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xFC, 0xE8)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xFE, 0xF0, 0x8A)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 12)
            };
            var skipGrid = new Grid();
            skipGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            skipGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var skipTextStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            skipTextStack.Children.Add(new TextBlock
            {
                Text = "⚡ QUICK START: Use Recommended Default Folders",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x85, 0x4D, 0x0E))
            });
            skipTextStack.Children.Add(new TextBlock
            {
                Text = "Automatically creates Documents, Images, Inbox, and Code_Projects. You can change or add folders anytime.",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x92, 0x40, 0x0E)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            });
            Grid.SetColumn(skipTextStack, 0);
            skipGrid.Children.Add(skipTextStack);

            var btnSkipStep = new Button
            {
                Content = "Skip Step →",
                Style = (Style)Application.Current.FindResource("ModernBtn"),
                Padding = new Thickness(14, 6, 14, 6),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                Margin = new Thickness(10, 0, 0, 0),
                ToolTip = "Skip folder customization and use recommended defaults"
            };
            btnSkipStep.Click += (s, e) =>
            {
                _isSkippedInitialFolders = true;
                SetStep(5);
            };
            Grid.SetColumn(btnSkipStep, 1);
            skipGrid.Children.Add(btnSkipStep);
            skipBanner.Child = skipGrid;
            step4Stack.Children.Add(skipBanner);

            // Folders header & Add Folder button
            var foldersHeaderGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            foldersHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            foldersHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _txtFoldersCount = new TextBlock
            {
                Text = "STARTER TARGET FOLDERS & CONTEXT DESCRIPTIONS:",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_txtFoldersCount, 0);
            foldersHeaderGrid.Children.Add(_txtFoldersCount);

            var btnAddFolder = new Button
            {
                Content = "+ Add Folder",
                Style = (Style)Application.Current.FindResource("PrimaryBtn"),
                Padding = new Thickness(12, 5, 12, 5),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                ToolTip = "Add a new starter target folder and context"
            };
            btnAddFolder.Click += (s, e) =>
            {
                ShowAddFolderDialog();
            };
            Grid.SetColumn(btnAddFolder, 1);
            foldersHeaderGrid.Children.Add(btnAddFolder);
            step4Stack.Children.Add(foldersHeaderGrid);

            // Dynamic folder cards container
            _initialFoldersContainer = new StackPanel();
            step4Stack.Children.Add(_initialFoldersContainer);

            // Initialize default folders list
            _initialFolders.Add(new InitialFolderItem
            {
                Name = "Documents",
                Context = "General written documents, reports, text files, articles, PDF papers, meeting notes, essays, spreadsheets, and educational or business documentation.",
                Icon = "📄",
                IsRemovable = true
            });
            _initialFolders.Add(new InitialFolderItem
            {
                Name = "Images",
                Context = "Photographs, graphic designs, screenshots, illustrations, wallpapers, and visual media assets in PNG, JPG, WEBP, or SVG formats.",
                Icon = "🖼️",
                IsRemovable = true
            });
            _initialFolders.Add(new InitialFolderItem
            {
                Name = "Code_Projects",
                Context = "Excluded from automated sorting. Contains programming source code, repositories, scripts, and software build artifacts.",
                Icon = "💻",
                IsRemovable = true
            });
            _initialFolders.Add(new InitialFolderItem
            {
                Name = "Inbox",
                Context = "Default staging area for unclassified files, low confidence routing (< 75%), or unknown binary formats requiring manual review.",
                Icon = "📥",
                IsRemovable = false // Standard fallback
            });

            RenderInitialFolderCards();
            _step4Panel.Children.Add(step4Stack);

            // ==================== STEP 5: QUICK TUTORIAL & FINISH ====================
            _step5Panel = new Grid { Visibility = Visibility.Collapsed };
            var step5Stack = new StackPanel();

            var s5Title = new TextBlock
            {
                Text = "You're All Set! 🚀",
                FontSize = 19,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
                Margin = new Thickness(0, 0, 0, 4)
            };
            var s5Desc = new TextBlock
            {
                Text = "Here is a 30-second guide to getting the most out of your automated sorting workflow:",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 17,
                Margin = new Thickness(0, 0, 0, 10)
            };
            step5Stack.Children.Add(s5Title);
            step5Stack.Children.Add(s5Desc);

            step5Stack.Children.Add(CreateFeatureCard("1️⃣", "Drop Files or Download", "Save files to your Watch Folder or drag files onto the Dashboard drop area. SortiFile immediately begins routing them."));
            step5Stack.Children.Add(CreateFeatureCard("2️⃣", "Smart Folder Taxonomy", "Switch to the 'Folders' tab to view organized files, create subfolders, adjust color badges, or tweak .context.md prompts to instruct the AI."));
            step5Stack.Children.Add(CreateFeatureCard("3️⃣", "Full Audit History & Undo", "Check the 'Sort History' tab to view reasonings and timestamps. If you ever need to restore a file, click 'Undo Last' or row 'Undo'."));
            step5Stack.Children.Add(CreateFeatureCard("4️⃣", "Custom Lock Feature", "Lock any folder from the context menu to prevent automatic sorting while maintaining full manual drag & drop reordering."));

            _step5Panel.Children.Add(step5Stack);

            // ==================== BOTTOM NAVIGATION BAR (FIXED DOCKED BOTTOM) ====================
            var bottomNav = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            bottomNav.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottomNav.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _btnNavBack = new Button
            {
                Content = "Skip Setup",
                Style = (Style)Application.Current.FindResource("ModernBtn"),
                Padding = new Thickness(18, 9, 18, 9),
                HorizontalAlignment = HorizontalAlignment.Left,
                Cursor = Cursors.Hand
            };
            _btnNavBack.Click += (s, e) =>
            {
                if (_currentStep == 1)
                {
                    _engine.Config.IsFirstRunCompleted = true;
                    _engine.Config.Save();
                    _engine.EnsureWorkspaceStructure();
                    Close();
                }
                else
                {
                    SetStep(_currentStep - 1);
                }
            };

            _btnNavNext = new Button
            {
                Content = "Next: Choose Watch Folder →",
                Style = (Style)Application.Current.FindResource("PrimaryBtn"),
                Padding = new Thickness(22, 9, 22, 9),
                HorizontalAlignment = HorizontalAlignment.Right,
                Cursor = Cursors.Hand
            };
            _btnNavNext.Click += (s, e) =>
            {
                if (_currentStep == 1)
                {
                    SetStep(2);
                }
                else if (_currentStep == 2)
                {
                    SetStep(3);
                }
                else if (_currentStep == 3)
                {
                    SetStep(4);
                }
                else if (_currentStep == 4)
                {
                    SetStep(5);
                }
                else if (_currentStep == 5)
                {
                    FinishSetup();
                }
            };

            Grid.SetColumn(_btnNavBack, 0);
            Grid.SetColumn(_btnNavNext, 1);
            bottomNav.Children.Add(_btnNavBack);
            bottomNav.Children.Add(_btnNavNext);

            DockPanel.SetDock(bottomNav, Dock.Bottom);
            mainStack.Children.Add(bottomNav);

            // Container for panels (fills center with vertical scrolling if needed)
            var contentScrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Focusable = false,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0, 0, 4, 0)
            };
            var contentGrid = new Grid();
            contentGrid.Children.Add(_step1Panel);
            contentGrid.Children.Add(_step2Panel);
            contentGrid.Children.Add(_step3Panel);
            contentGrid.Children.Add(_step4Panel);
            contentGrid.Children.Add(_step5Panel);
            contentScrollViewer.Content = contentGrid;
            mainStack.Children.Add(contentScrollViewer);

            card.Child = mainStack;
            outerGrid.Children.Add(card);
            Content = outerGrid;
        }

        private void SetStep(int step)
        {
            _currentStep = step;
            _step1Panel.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
            _step2Panel.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
            _step3Panel.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
            _step4Panel.Visibility = step == 4 ? Visibility.Visible : Visibility.Collapsed;
            _step5Panel.Visibility = step == 5 ? Visibility.Visible : Visibility.Collapsed;

            UpdatePill(_pillStep1, _txtPill1, step == 1);
            UpdatePill(_pillStep2, _txtPill2, step == 2);
            UpdatePill(_pillStep3, _txtPill3, step == 3);
            UpdatePill(_pillStep4, _txtPill4, step == 4);
            UpdatePill(_pillStep5, _txtPill5, step == 5);

            if (_btnNavBack != null && _btnNavNext != null)
            {
                if (step == 1)
                {
                    _btnNavBack.Content = "Skip Setup";
                    _btnNavNext.Content = "Next: Choose Watch Folder →";
                }
                else if (step == 2)
                {
                    _btnNavBack.Content = "← Back";
                    _btnNavNext.Content = "Next: Choose Workspace →";
                }
                else if (step == 3)
                {
                    _btnNavBack.Content = "← Back";
                    _btnNavNext.Content = "Next: Setup Folders →";
                }
                else if (step == 4)
                {
                    _btnNavBack.Content = "← Back";
                    _btnNavNext.Content = "Next: Quick Guide →";
                }
                else if (step == 5)
                {
                    _btnNavBack.Content = "← Back";
                    _btnNavNext.Content = "🚀 Finish Setup & Launch";
                }
            }
        }

        private void UpdatePill(Border pill, TextBlock txt, bool active)
        {
            if (active)
            {
                pill.Background = new SolidColorBrush(Color.FromRgb(0x45, 0x1A, 0x03));
                txt.Foreground = Brushes.White;
            }
            else
            {
                pill.Background = Brushes.Transparent;
                txt.Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
            }
        }

        private Border CreateStepPill(string label, int stepIndex, out TextBlock txt, bool active)
        {
            txt = new TextBlock
            {
                Text = label,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = active ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B))
            };

            var pill = new Border
            {
                Background = active ? new SolidColorBrush(Color.FromRgb(0x45, 0x1A, 0x03)) : Brushes.Transparent,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 6, 6, 6),
                Margin = new Thickness(2, 0, 2, 0),
                Cursor = Cursors.Hand,
                ToolTip = $"Go to Step {stepIndex}: {label}",
                Child = txt
            };

            pill.MouseDown += (s, e) =>
            {
                SetStep(stepIndex);
            };

            pill.MouseEnter += (s, e) =>
            {
                if (_currentStep != stepIndex)
                {
                    pill.Background = new SolidColorBrush(Color.FromRgb(0xEE, 0xF2, 0xF6));
                }
            };

            pill.MouseLeave += (s, e) =>
            {
                if (_currentStep != stepIndex)
                {
                    pill.Background = Brushes.Transparent;
                }
            };

            return pill;
        }

        private Border CreateFeatureCard(string icon, string title, string text)
        {
            var b = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 6)
            };

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var iconBlock = new TextBlock
            {
                Text = icon,
                FontSize = 16,
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(iconBlock, 0);
            g.Children.Add(iconBlock);

            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A))
            });
            sp.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            });
            Grid.SetColumn(sp, 1);
            g.Children.Add(sp);

            b.Child = g;
            return b;
        }

        private Border CreateOptionCard(string icon, string title, string text, bool selected)
        {
            var b = new Border
            {
                Background = selected ? new SolidColorBrush(Color.FromRgb(0xFF, 0xFD, 0xEB)) : Brushes.White,
                BorderBrush = selected ? new SolidColorBrush(Color.FromRgb(0xFB, 0xC3, 0x05)) : new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                BorderThickness = new Thickness(selected ? 1.5 : 1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 6),
                Cursor = Cursors.Hand
            };

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var iconBlock = new TextBlock
            {
                Text = icon,
                FontSize = 18,
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(iconBlock, 0);
            g.Children.Add(iconBlock);

            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A))
            });
            sp.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            });
            Grid.SetColumn(sp, 1);
            g.Children.Add(sp);

            b.Child = g;
            return b;
        }

        private void SelectWatchOption(string path, Border activeCard)
        {
            _selectedWatchPath = path;
            _txtSelectedWatchPath.Text = _engine.Config.ResolvePath(path);

            ResetOptionCard(_watchCardDefault);
            ResetOptionCard(_watchCardDownloads);
            ResetOptionCard(_watchCardCustom);

            activeCard.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFD, 0xEB));
            activeCard.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFB, 0xC3, 0x05));
            activeCard.BorderThickness = new Thickness(1.5);
        }

        private void SelectWorkspaceOption(string path, Border activeCard)
        {
            _selectedWorkspacePath = path;
            _txtSelectedWorkspacePath.Text = _engine.Config.ResolvePath(path);

            ResetOptionCard(_wsCardDefault);
            ResetOptionCard(_wsCardDocs);
            ResetOptionCard(_wsCardCustom);

            activeCard.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFD, 0xEB));
            activeCard.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFB, 0xC3, 0x05));
            activeCard.BorderThickness = new Thickness(1.5);
        }

        private void ResetOptionCard(Border card)
        {
            card.Background = Brushes.White;
            card.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0));
            card.BorderThickness = new Thickness(1);
        }

        private void RenderInitialFolderCards()
        {
            _initialFoldersContainer.Children.Clear();
            if (_txtFoldersCount != null)
            {
                _txtFoldersCount.Text = $"STARTER TARGET FOLDERS ({_initialFolders.Count}):";
            }

            foreach (var item in _initialFolders)
            {
                var b = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 0, 0, 6)
                };

                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var iconBlock = new TextBlock
                {
                    Text = item.Icon,
                    FontSize = 16,
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(iconBlock, 0);
                g.Children.Add(iconBlock);

                var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                sp.Children.Add(new TextBlock
                {
                    Text = item.Name,
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A))
                });
                sp.Children.Add(new TextBlock
                {
                    Text = item.Context,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                    TextWrapping = TextWrapping.Wrap,
                    MaxHeight = 32,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 2, 0, 0)
                });
                Grid.SetColumn(sp, 1);
                g.Children.Add(sp);

                var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

                var btnEdit = new Button
                {
                    Content = "✏️ Edit Context",
                    Style = (Style)Application.Current.FindResource("ModernBtn"),
                    FontSize = 10,
                    Padding = new Thickness(8, 4, 8, 4),
                    Cursor = Cursors.Hand,
                    ToolTip = "Edit .context.md AI classification prompt"
                };
                btnEdit.Click += (s, e) =>
                {
                    var editDlg = new ContextEditDialog(item.Name, item.Context) { Owner = this };
                    if (editDlg.ShowDialog() == true)
                    {
                        item.Context = editDlg.ContextText;
                        _isSkippedInitialFolders = false;
                        RenderInitialFolderCards();
                    }
                };
                actions.Children.Add(btnEdit);

                if (item.IsRemovable)
                {
                    var btnDelete = new Button
                    {
                        Content = "✕",
                        Style = (Style)Application.Current.FindResource("ModernBtn"),
                        FontSize = 10,
                        Padding = new Thickness(6, 4, 6, 4),
                        Margin = new Thickness(4, 0, 0, 0),
                        Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)),
                        Cursor = Cursors.Hand,
                        ToolTip = "Remove from starter folders"
                    };
                    btnDelete.Click += (s, e) =>
                    {
                        _initialFolders.Remove(item);
                        _isSkippedInitialFolders = false;
                        RenderInitialFolderCards();
                    };
                    actions.Children.Add(btnDelete);
                }

                Grid.SetColumn(actions, 2);
                g.Children.Add(actions);

                b.Child = g;
                _initialFoldersContainer.Children.Add(b);
            }
        }

        private void ShowAddFolderDialog()
        {
            var dlg = new AddInitialFolderDialog { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                string name = dlg.FolderName;
                if (_initialFolders.Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show($"A folder named '{name}' already exists in the list.", "Duplicate Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string ctx = string.IsNullOrWhiteSpace(dlg.ContextPrompt)
                    ? $"Files and documents relevant to {name}."
                    : dlg.ContextPrompt;

                _initialFolders.Insert(0, new InitialFolderItem
                {
                    Name = name,
                    Context = ctx,
                    Icon = "📁",
                    IsRemovable = true
                });
                _isSkippedInitialFolders = false;
                RenderInitialFolderCards();
            }
        }

        private void FinishSetup()
        {
            try
            {
                _engine.Config.WatchDir = _selectedWatchPath;
                _engine.Config.WorkspaceDir = _selectedWorkspacePath;
                _engine.Config.IsFirstRunCompleted = true;
                _engine.Config.Save();

                string ws = _engine.Config.ResolvePath(_selectedWorkspacePath);
                if (!Directory.Exists(ws)) Directory.CreateDirectory(ws);

                if (_isSkippedInitialFolders || _initialFolders == null || _initialFolders.Count == 0)
                {
                    _engine.EnsureWorkspaceStructure();
                }
                else
                {
                    foreach (var item in _initialFolders)
                    {
                        if (string.IsNullOrWhiteSpace(item.Name)) continue;
                        string folderPath = Path.Combine(ws, item.Name);
                        if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

                        string ctxFile = Path.Combine(folderPath, ".context.md");
                        string ctxContent = string.IsNullOrWhiteSpace(item.Context)
                            ? $"# {item.Name} Context\n\nFiles and documents relevant to {item.Name}.\n"
                            : (item.Context.StartsWith("#") ? item.Context : $"# {item.Name} Context\n\n{item.Context}\n");
                        try { File.WriteAllText(ctxFile, ctxContent); } catch { }
                    }

                    // Ensure fallback Inbox always exists
                    string inbox = Path.Combine(ws, "Inbox");
                    if (!Directory.Exists(inbox))
                    {
                        Directory.CreateDirectory(inbox);
                        string inboxCtx = Path.Combine(inbox, ".context.md");
                        if (!File.Exists(inboxCtx))
                        {
                            try { File.WriteAllText(inboxCtx, "# Inbox Context\n\nDefault staging area for unclassified files, low confidence routing (< 75%), or unknown binary formats requiring manual review.\n"); } catch { }
                        }
                    }
                }

                _engine.RestartWatcher();
                _mainWindow.TxtWatchDir.Text = _selectedWatchPath;
                _mainWindow.TxtWorkspaceDir.Text = _selectedWorkspacePath;
                _mainWindow.RefreshFolders();

                string resolved = _engine.Config.ResolvePath(_selectedWatchPath);
                string folderName = string.IsNullOrEmpty(Path.GetFileName(resolved)) ? resolved : Path.GetFileName(resolved);
                _mainWindow.ShowNotificationToast("Setup Completed!", $"SortiFile is now monitoring: {folderName}", "✓", 3500);
            }
            catch { }

            DialogResult = true;
            Close();
        }
    }

    // ==================== MODAL DIALOG: ADD INITIAL TARGET FOLDER ====================
    public class AddInitialFolderDialog : Window
    {
        private readonly TextBox _txtName;
        private readonly TextBox _txtContext;

        public string FolderName => _txtName.Text.Trim();
        public string ContextPrompt => _txtContext.Text.Trim();

        public AddInitialFolderDialog()
        {
            Title = "Add Initial Target Folder";
            Width = 460;
            Height = 350;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));
            FontFamily = (FontFamily)Application.Current.FindResource("IosFontFamily");
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
            ResizeMode = ResizeMode.NoResize;

            var grid = new Grid { Margin = new Thickness(20) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var title = new TextBlock
            {
                Text = "Add New Target Folder 📁",
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
                Margin = new Thickness(0, 0, 0, 4)
            };
            Grid.SetRow(title, 0);
            grid.Children.Add(title);

            var desc = new TextBlock
            {
                Text = "Specify the folder name and description. The local AI classifier uses this description to sort files.",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };
            Grid.SetRow(desc, 1);
            grid.Children.Add(desc);

            var inputsPanel = new StackPanel();

            inputsPanel.Children.Add(new TextBlock
            {
                Text = "FOLDER NAME:",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                Margin = new Thickness(0, 0, 0, 4)
            });

            _txtName = new TextBox
            {
                Height = 32,
                FontSize = 12,
                Padding = new Thickness(8, 5, 8, 5),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)),
                Margin = new Thickness(0, 0, 0, 10)
            };
            inputsPanel.Children.Add(_txtName);

            inputsPanel.Children.Add(new TextBlock
            {
                Text = "TAXONOMY DESCRIPTION (.context.md prompt):",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                Margin = new Thickness(0, 0, 0, 4)
            });

            _txtContext = new TextBox
            {
                Height = 70,
                FontSize = 11,
                Padding = new Thickness(8, 6, 8, 6),
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1))
            };
            inputsPanel.Children.Add(_txtContext);

            Grid.SetRow(inputsPanel, 2);
            grid.Children.Add(inputsPanel);

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };

            var btnCancel = new Button
            {
                Content = "Cancel",
                Style = (Style)Application.Current.FindResource("ModernBtn"),
                Padding = new Thickness(16, 6, 16, 6),
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = Cursors.Hand
            };
            btnCancel.Click += (s, e) => { DialogResult = false; Close(); };
            btnPanel.Children.Add(btnCancel);

            var btnOk = new Button
            {
                Content = "Add Folder",
                Style = (Style)Application.Current.FindResource("PrimaryBtn"),
                Padding = new Thickness(18, 6, 18, 6),
                Cursor = Cursors.Hand
            };
            btnOk.Click += (s, e) =>
            {
                string name = _txtName.Text.Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    MessageBox.Show("Please enter a valid folder name.", "Folder Name Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                char[] invalidChars = Path.GetInvalidFileNameChars();
                if (name.IndexOfAny(invalidChars) >= 0)
                {
                    MessageBox.Show("Folder name contains invalid characters.", "Invalid Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                DialogResult = true;
                Close();
            };
            btnPanel.Children.Add(btnOk);

            Grid.SetRow(btnPanel, 3);
            grid.Children.Add(btnPanel);

            Content = grid;
        }
    }
}
