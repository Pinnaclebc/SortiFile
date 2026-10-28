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
        public string TargetLocation { get; set; } = "";
    }

    public class SetupWizardWindow : Window
    {
        private readonly MainWindow _mainWindow;
        private readonly FileWatcherEngine _engine;
        private string _selectedWatchPath;
        private string _defaultFolderLocation;
        private int _currentStep = 1;
        private bool _isSkippedInitialFolders = false;

        // Step container grids (4 Steps: Welcome, Work Folder, Folders & Context, Guide)
        private readonly Grid _step1Panel;
        private readonly Grid _step2Panel;
        private readonly Grid _step3Panel;
        private readonly Grid _step4Panel;

        // Step indicator badges (4 Steps)
        private readonly Border _pillStep1;
        private readonly Border _pillStep2;
        private readonly Border _pillStep3;
        private readonly Border _pillStep4;
        private readonly TextBlock _txtPill1;
        private readonly TextBlock _txtPill2;
        private readonly TextBlock _txtPill3;
        private readonly TextBlock _txtPill4;

        // Step 2 Watch Dir (Work Folder) path display & cards
        private readonly TextBlock _txtSelectedWatchPath;
        private readonly Border _watchCardDefault;
        private readonly Border _watchCardDownloads;
        private readonly Border _watchCardCustom;

        // Step 3 Initial Folders container & list
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
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            _selectedWatchPath = Path.Combine(desktopPath, "Watch_Folder");
            _defaultFolderLocation = desktopPath;

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

            // ==================== STEP PROGRESS INDICATOR PILLS (4 STEPS) ====================
            var stepsBar = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(4),
                Margin = new Thickness(0, 0, 0, 14)
            };
            var stepsGrid = new UniformGrid { Columns = 4 };

            _pillStep1 = CreateStepPill("1. Welcome", 1, out _txtPill1, true);
            _pillStep2 = CreateStepPill("2. Work Folder", 2, out _txtPill2, false);
            _pillStep3 = CreateStepPill("3. Folders", 3, out _txtPill3, false);
            _pillStep4 = CreateStepPill("4. Guide", 4, out _txtPill4, false);

            stepsGrid.Children.Add(_pillStep1);
            stepsGrid.Children.Add(_pillStep2);
            stepsGrid.Children.Add(_pillStep3);
            stepsGrid.Children.Add(_pillStep4);
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

            step1Stack.Children.Add(CreateFeatureCard("👁️", "Autonomous Work Folder", "Monitors your designated incoming folder (e.g. Downloads) and automatically routes newly saved files into the right target folders."));
            step1Stack.Children.Add(CreateFeatureCard("📁", "Flexible Target Folders", "Organize files into customized folders with taxonomy context prompts anywhere on your PC with complete privacy."));
            step1Stack.Children.Add(CreateFeatureCard("🧠", "Smart Content Classification", "Inspects document text, PDF extracts, and media metadata using local LLMs or rule heuristics matching .context.md prompts."));
            step1Stack.Children.Add(CreateFeatureCard("↺", "Zero-Risk 1-Click Undo", "Every move is safely logged in your audit history. You can undo or revert any sorted file at any time."));

            _step1Panel.Children.Add(step1Stack);

            // ==================== STEP 2: CHOOSE WORK FOLDER (INCOMING WATCH DIRECTORY) ====================
            _step2Panel = new Grid { Visibility = Visibility.Collapsed };
            var step2Stack = new StackPanel();

            var s2Title = new TextBlock
            {
                Text = "Choose Your Incoming Work Folder 📂",
                FontSize = 19,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
                Margin = new Thickness(0, 0, 0, 4)
            };
            var s2Desc = new TextBlock
            {
                Text = "Where do you typically save or download files? Select an incoming work folder for SortiFile to monitor. You can change this anytime in Settings.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 17,
                Margin = new Thickness(0, 0, 0, 12)
            };
            step2Stack.Children.Add(s2Title);
            step2Stack.Children.Add(s2Desc);

            string defaultDesktopWatch = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Watch_Folder");
            _watchCardDefault = CreateOptionCard("📁", "Desktop Watch Folder (Recommended)", $"Dedicated 'Watch_Folder' created on your Desktop:\n{defaultDesktopWatch}", true);
            _watchCardDefault.MouseDown += (s, e) => { SelectWatchOption(defaultDesktopWatch, _watchCardDefault); };
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
                    Title = "Select Custom Incoming Work Folder",
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
                Text = "✓ CURRENTLY SELECTED WORK FOLDER:",
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

            // ==================== STEP 3: SETUP INITIAL FOLDERS & CONTEXT ====================
            _step3Panel = new Grid { Visibility = Visibility.Collapsed };
            var step3Stack = new StackPanel();

            var s3Title = new TextBlock
            {
                Text = "Setup Initial Folders & Context 🗂️",
                FontSize = 19,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
                Margin = new Thickness(0, 0, 0, 4)
            };
            var s3Desc = new TextBlock
            {
                Text = "SortiFile categorizes files into target folders according to each folder's .context.md description. You can add new or existing folders, customize their locations, or skip to use curated defaults.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 17,
                Margin = new Thickness(0, 0, 0, 10)
            };
            step3Stack.Children.Add(s3Title);
            step3Stack.Children.Add(s3Desc);

            // Skip banner option card (Whole step is skippable/optional)
            var skipBanner = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xFC, 0xE8)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xFE, 0xF0, 0x8A)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 10)
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
                SetStep(4);
            };
            Grid.SetColumn(btnSkipStep, 1);
            skipGrid.Children.Add(btnSkipStep);
            skipBanner.Child = skipGrid;
            step3Stack.Children.Add(skipBanner);

            // Folders header with Clear All, Add Existing, and Add New buttons
            var foldersHeaderGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            foldersHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            foldersHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _txtFoldersCount = new TextBlock
            {
                Text = "TARGET FOLDERS & CONTEXT DESCRIPTIONS:",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_txtFoldersCount, 0);
            foldersHeaderGrid.Children.Add(_txtFoldersCount);

            var headerButtons = new StackPanel { Orientation = Orientation.Horizontal };

            var btnAddFolder = new Button
            {
                Content = "+ Add New",
                Style = (Style)Application.Current.FindResource("PrimaryBtn"),
                Padding = new Thickness(10, 5, 10, 5),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 6, 0),
                ToolTip = "Add a new target folder with context prompt"
            };
            btnAddFolder.Click += (s, e) => { ShowAddFolderDialog(); };
            headerButtons.Children.Add(btnAddFolder);

            var btnAddExisting = new Button
            {
                Content = "📂 Add Existing",
                Style = (Style)Application.Current.FindResource("ModernBtn"),
                Padding = new Thickness(10, 5, 10, 5),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 6, 0),
                ToolTip = "Add an existing directory from your drive and inspect context"
            };
            btnAddExisting.Click += (s, e) => { ShowAddExistingFolderDialog(); };
            headerButtons.Children.Add(btnAddExisting);

            var btnClearAll = new Button
            {
                Content = "🗑️ Clear All",
                Style = (Style)Application.Current.FindResource("ModernBtn"),
                Padding = new Thickness(10, 5, 10, 5),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)),
                Cursor = Cursors.Hand,
                ToolTip = "Clear all pre-existing folders from list"
            };
            btnClearAll.Click += (s, e) =>
            {
                _initialFolders.Clear();
                _isSkippedInitialFolders = false;
                RenderInitialFolderCards();
            };
            headerButtons.Children.Add(btnClearAll);

            Grid.SetColumn(headerButtons, 1);
            foldersHeaderGrid.Children.Add(headerButtons);
            step3Stack.Children.Add(foldersHeaderGrid);

            // Dynamic folder cards container
            _initialFoldersContainer = new StackPanel();
            step3Stack.Children.Add(_initialFoldersContainer);

            // Initialize default folders list targeting Desktop
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            _initialFolders.Add(new InitialFolderItem
            {
                Name = "Documents",
                Context = "General written documents, reports, text files, articles, PDF papers, meeting notes, essays, spreadsheets, and educational or business documentation.",
                Icon = "📄",
                IsRemovable = true,
                TargetLocation = Path.Combine(desktop, "Documents")
            });
            _initialFolders.Add(new InitialFolderItem
            {
                Name = "Images",
                Context = "Photographs, graphic designs, screenshots, illustrations, wallpapers, and visual media assets in PNG, JPG, WEBP, or SVG formats.",
                Icon = "🖼️",
                IsRemovable = true,
                TargetLocation = Path.Combine(desktop, "Images")
            });
            _initialFolders.Add(new InitialFolderItem
            {
                Name = "Code_Projects",
                Context = "Excluded from automated sorting. Contains programming source code, repositories, scripts, and software build artifacts.",
                Icon = "💻",
                IsRemovable = true,
                TargetLocation = Path.Combine(desktop, "Code_Projects")
            });
            _initialFolders.Add(new InitialFolderItem
            {
                Name = "Inbox",
                Context = "Default staging area for unclassified files, low confidence routing (< 75%), or unknown binary formats requiring manual review.",
                Icon = "📥",
                IsRemovable = false, // Standard fallback
                TargetLocation = Path.Combine(desktop, "Inbox")
            });

            RenderInitialFolderCards();
            _step3Panel.Children.Add(step3Stack);

            // ==================== STEP 4: QUICK TUTORIAL & FINISH ====================
            _step4Panel = new Grid { Visibility = Visibility.Collapsed };
            var step4Stack = new StackPanel();

            var s4Title = new TextBlock
            {
                Text = "You're All Set! 🚀",
                FontSize = 19,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
                Margin = new Thickness(0, 0, 0, 4)
            };
            var s4Desc = new TextBlock
            {
                Text = "Here is a 30-second guide to getting the most out of your automated sorting workflow:",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 17,
                Margin = new Thickness(0, 0, 0, 10)
            };
            step4Stack.Children.Add(s4Title);
            step4Stack.Children.Add(s4Desc);

            step4Stack.Children.Add(CreateFeatureCard("1️⃣", "Drop Files or Download", "Save files to your Work Folder or drag files onto the Dashboard drop area. SortiFile immediately begins routing them."));
            step4Stack.Children.Add(CreateFeatureCard("2️⃣", "Smart Folder Taxonomy", "Switch to the 'Folders' tab to view organized files, create subfolders, adjust color badges, or tweak .context.md prompts to instruct the AI."));
            step4Stack.Children.Add(CreateFeatureCard("3️⃣", "Full Audit History & Undo", "Check the 'Sort History' tab to view reasonings and timestamps. If you ever need to restore a file, click 'Undo Last' or row 'Undo'."));
            step4Stack.Children.Add(CreateFeatureCard("4️⃣", "Custom Lock & Multi-Select", "Shift+Click folders in the tree to bulk lock, recolor, or manage folders with zero Windows system chrome."));

            _step4Panel.Children.Add(step4Stack);

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
                Content = "Next: Choose Work Folder →",
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

            UpdatePill(_pillStep1, _txtPill1, step == 1);
            UpdatePill(_pillStep2, _txtPill2, step == 2);
            UpdatePill(_pillStep3, _txtPill3, step == 3);
            UpdatePill(_pillStep4, _txtPill4, step == 4);

            if (_btnNavBack != null && _btnNavNext != null)
            {
                if (step == 1)
                {
                    _btnNavBack.Content = "Skip Setup";
                    _btnNavNext.Content = "Next: Choose Work Folder →";
                }
                else if (step == 2)
                {
                    _btnNavBack.Content = "← Back";
                    _btnNavNext.Content = "Next: Setup Folders →";
                }
                else if (step == 3)
                {
                    _btnNavBack.Content = "← Back";
                    _btnNavNext.Content = "Next: Quick Guide →";
                }
                else if (step == 4)
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
                _txtFoldersCount.Text = $"TARGET FOLDERS ({_initialFolders.Count}):";
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
                
                var nameLine = new StackPanel { Orientation = Orientation.Horizontal };
                nameLine.Children.Add(new TextBlock
                {
                    Text = item.Name,
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A))
                });

                string locDisplay = !string.IsNullOrWhiteSpace(item.TargetLocation)
                    ? item.TargetLocation
                    : _defaultFolderLocation;

                nameLine.Children.Add(new TextBlock
                {
                    Text = $"  📍 {locDisplay}",
                    FontSize = 10,
                    FontFamily = new FontFamily("Consolas"),
                    Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 220
                });
                sp.Children.Add(nameLine);

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

                var btnLocation = new Button
                {
                    Content = "📍 Location",
                    Style = (Style)Application.Current.FindResource("ModernBtn"),
                    FontSize = 10,
                    Padding = new Thickness(8, 4, 8, 4),
                    Margin = new Thickness(0, 0, 4, 0),
                    Cursor = Cursors.Hand,
                    ToolTip = "Select custom destination path for this folder"
                };
                btnLocation.Click += (s, e) =>
                {
                    var dlg = new OpenFolderDialog
                    {
                        Title = $"Select Target Destination for '{item.Name}'",
                        InitialDirectory = !string.IsNullOrWhiteSpace(item.TargetLocation) ? item.TargetLocation : _defaultFolderLocation
                    };
                    if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FolderName))
                    {
                        item.TargetLocation = dlg.FolderName;
                        _isSkippedInitialFolders = false;
                        RenderInitialFolderCards();
                    }
                };
                actions.Children.Add(btnLocation);

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
                string loc = !string.IsNullOrWhiteSpace(dlg.TargetLocation) ? dlg.TargetLocation : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string fullPath = Path.Combine(loc, name);

                // Validation checker: checks if folder is already in the database/tree
                bool alreadyExists = _initialFolders.Any(f =>
                    f.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(f.TargetLocation) && Path.GetFullPath(f.TargetLocation).TrimEnd('\\', '/').Equals(Path.GetFullPath(fullPath).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)));

                if (alreadyExists)
                {
                    CustomDialog.Show(this, "Folder Already Added", $"A folder named '{name}' is already included in your initial folder setup:\n\n{fullPath}\n\nDuplicate folders cannot be added.");
                    return;
                }

                _initialFolders.Insert(0, new InitialFolderItem
                {
                    Name = name,
                    Context = dlg.ContextPrompt,
                    Icon = "📁",
                    IsRemovable = true,
                    TargetLocation = fullPath
                });
                _isSkippedInitialFolders = false;
                RenderInitialFolderCards();
            }
        }

        private void ShowAddExistingFolderDialog()
        {
            var dlg = new OpenFolderDialog
            {
                Title = "Select Existing Folder to Add to SortiFile",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FolderName))
            {
                string dirPath = Path.GetFullPath(dlg.FolderName).TrimEnd('\\', '/');
                string name = Path.GetFileName(dirPath);
                if (string.IsNullOrWhiteSpace(name)) name = dirPath;

                // Validation checker: checks if folder is already in the database/tree
                bool alreadyExists = _initialFolders.Any(f =>
                    f.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(f.TargetLocation) && Path.GetFullPath(f.TargetLocation).TrimEnd('\\', '/').Equals(dirPath, StringComparison.OrdinalIgnoreCase)));

                if (alreadyExists)
                {
                    CustomDialog.Show(this, "Folder Already Added", $"The folder '{name}' is already in your initial folder setup:\n\n{dirPath}\n\nDuplicate folders cannot be added.");
                    return;
                }

                string ctxPath1 = Path.Combine(dirPath, ".context.md");
                string ctxPath2 = Path.Combine(dirPath, "context.md");
                string existingCtx = "";
                if (File.Exists(ctxPath1))
                {
                    try { existingCtx = File.ReadAllText(ctxPath1); } catch { }
                }
                else if (File.Exists(ctxPath2))
                {
                    try { existingCtx = File.ReadAllText(ctxPath2); } catch { }
                }

                if (string.IsNullOrWhiteSpace(existingCtx))
                {
                    existingCtx = $"# {name} Context\n\nFiles and documents relevant to {name}.\n";
                }

                var editDlg = new ContextEditDialog(name, existingCtx) { Owner = this };
                if (editDlg.ShowDialog() == true)
                {
                    _initialFolders.Add(new InitialFolderItem
                    {
                        Name = name,
                        Context = editDlg.ContextText,
                        Icon = "📁",
                        IsRemovable = true,
                        TargetLocation = dirPath
                    });
                    _isSkippedInitialFolders = false;
                    RenderInitialFolderCards();
                }
            }
        }

        private void FinishSetup()
        {
            try
            {
                _engine.Config.WatchDir = _selectedWatchPath;
                _engine.Config.IsFirstRunCompleted = true;

                if (_engine.Config.RegisteredFolders == null)
                    _engine.Config.RegisteredFolders = new List<string>();

                _engine.Config.RegisteredFolders.Clear();

                if (!_isSkippedInitialFolders && _initialFolders != null && _initialFolders.Count > 0)
                {
                    foreach (var item in _initialFolders)
                    {
                        if (string.IsNullOrWhiteSpace(item.Name)) continue;
                        string folderPath = !string.IsNullOrWhiteSpace(item.TargetLocation)
                            ? item.TargetLocation
                            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), item.Name);

                        if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

                        string ctxFile = Path.Combine(folderPath, ".context.md");
                        string ctxContent = string.IsNullOrWhiteSpace(item.Context)
                            ? $"# {item.Name} Context\n\nFiles and documents relevant to {item.Name}.\n"
                            : (item.Context.StartsWith("#") ? item.Context : $"# {item.Name} Context\n\n{item.Context}\n");
                        try { File.WriteAllText(ctxFile, ctxContent); } catch { }

                        if (!_engine.Config.RegisteredFolders.Contains(folderPath, StringComparer.OrdinalIgnoreCase))
                        {
                            _engine.Config.RegisteredFolders.Add(folderPath);
                        }
                    }
                }
                else
                {
                    _engine.EnsureWorkspaceStructure();
                }

                _engine.Config.Save();
                _engine.RestartWatcher();
                _mainWindow.TxtWatchDir.Text = _selectedWatchPath;
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
        private readonly TextBox _txtLocation;
        private readonly TextBox _txtContext;

        public string FolderName => _txtName.Text.Trim();
        public string TargetLocation => _txtLocation.Text.Trim();
        public string ContextPrompt => _txtContext.Text.Trim();

        public AddInitialFolderDialog()
        {
            Title = "Add Initial Target Folder";
            Width = 480;
            Height = 420;
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
                Text = "Specify the folder name, target destination path on your drive, and mandatory classification context.",
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
                Text = "TARGET DESTINATION LOCATION:",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                Margin = new Thickness(0, 0, 0, 4)
            });

            var locGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            locGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            locGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _txtLocation = new TextBox
            {
                Height = 32,
                FontSize = 11,
                FontFamily = new FontFamily("Consolas"),
                Text = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Padding = new Thickness(8, 5, 8, 5),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_txtLocation, 0);
            locGrid.Children.Add(_txtLocation);

            var btnBrowseLoc = new Button
            {
                Content = "Browse...",
                Style = (Style)Application.Current.FindResource("ModernBtn"),
                FontSize = 10.5,
                Padding = new Thickness(10, 4, 10, 4),
                Height = 32,
                Margin = new Thickness(8, 0, 0, 0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            btnBrowseLoc.Click += (s, e) =>
            {
                var dlg = new OpenFolderDialog
                {
                    Title = "Select Destination Location for New Folder",
                    InitialDirectory = !string.IsNullOrWhiteSpace(_txtLocation.Text) ? _txtLocation.Text : Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                };
                if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FolderName))
                {
                    _txtLocation.Text = dlg.FolderName;
                }
            };
            Grid.SetColumn(btnBrowseLoc, 1);
            locGrid.Children.Add(btnBrowseLoc);
            inputsPanel.Children.Add(locGrid);

            inputsPanel.Children.Add(new TextBlock
            {
                Text = "TAXONOMY DESCRIPTION (.context.md prompt - MANDATORY):",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                Margin = new Thickness(0, 0, 0, 4)
            });

            _txtContext = new TextBox
            {
                Height = 65,
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
                    CustomDialog.Show(this, "Folder Name Required", "Please enter a valid folder name.");
                    return;
                }
                char[] invalidChars = Path.GetInvalidFileNameChars();
                if (name.IndexOfAny(invalidChars) >= 0)
                {
                    CustomDialog.Show(this, "Invalid Name", "Folder name contains invalid characters.");
                    return;
                }
                if (string.IsNullOrWhiteSpace(_txtContext.Text.Trim()))
                {
                    CustomDialog.Show(this, "Context Required", "Please provide a taxonomy description (.context.md is mandatory).");
                    _txtContext.Focus();
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
