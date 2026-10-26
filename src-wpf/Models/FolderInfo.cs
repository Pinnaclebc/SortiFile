using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace SortiFileWpf.Models
{
    public class FolderInfo : INotifyPropertyChanged
    {
        private string _name = "";
        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsDeletable));
                }
            }
        }

        public string RelativePath { get; set; } = "";
        public string FullPath { get; set; } = "";

        public List<FolderInfo> Children { get; } = new();
        public FolderInfo? Parent { get; set; }

        public bool HasSubFolders => Children.Count > 0;

        private bool _isExpanded = true;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ChevronIcon));
                }
            }
        }

        public string ChevronIcon => IsExpanded ? "▾" : "▸";
        public Visibility ChevronVisibility => HasSubFolders ? Visibility.Visible : Visibility.Hidden;

        private bool _isHiddenByParent = false;
        public bool IsHiddenByParent
        {
            get => _isHiddenByParent;
            set
            {
                if (_isHiddenByParent != value)
                {
                    _isHiddenByParent = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TreeItemVisibility));
                }
            }
        }

        public Visibility TreeItemVisibility => IsHiddenByParent ? Visibility.Collapsed : Visibility.Visible;

        private int _depth = 0;
        public int Depth
        {
            get => _depth;
            set
            {
                if (_depth != value)
                {
                    _depth = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsSubFolder));
                    OnPropertyChanged(nameof(TreeIndentMargin));
                    OnPropertyChanged(nameof(ConnectorVisibility));
                }
            }
        }

        public bool IsSubFolder => Depth > 0;
        public Thickness TreeIndentMargin => new Thickness(Depth * 16, 2, 0, 2);
        public Visibility ConnectorVisibility => IsSubFolder ? Visibility.Visible : Visibility.Collapsed;

        private bool _isLastChild = false;
        public bool IsLastChild
        {
            get => _isLastChild;
            set
            {
                if (_isLastChild != value)
                {
                    _isLastChild = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasSiblingBelowVisibility));
                }
            }
        }

        public Visibility HasSiblingBelowVisibility => IsLastChild ? Visibility.Collapsed : Visibility.Visible;

        private string _context = "";
        public string Context
        {
            get => _context;
            set
            {
                if (_context != value)
                {
                    _context = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _fileCount = 0;
        public int FileCount
        {
            get => _fileCount;
            set
            {
                if (_fileCount != value)
                {
                    _fileCount = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FileCountDisplay));
                    OnPropertyChanged(nameof(CountBadge));
                }
            }
        }

        public string FileCountDisplay => $"{FileCount} file{(FileCount == 1 ? "" : "s")}";
        public FolderInfo()
        {
            UpdateBrushes();
        }

        public string CountBadge => $"{FileCount}";
        public bool IsDeletable => !Name.Equals("Inbox", StringComparison.OrdinalIgnoreCase);

        private bool _isLocked = false;
        public bool IsLocked
        {
            get => _isLocked;
            set
            {
                if (_isLocked != value)
                {
                    _isLocked = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(LockIcon));
                    OnPropertyChanged(nameof(LockVisibility));
                    OnPropertyChanged(nameof(LockTooltip));
                    OnPropertyChanged(nameof(LockBtnText));
                }
            }
        }

        public string LockIcon => IsLocked ? "🔒" : "";
        public Visibility LockVisibility => IsLocked ? Visibility.Visible : Visibility.Collapsed;
        public string LockTooltip => IsLocked
            ? "Folder is Locked: Excluded from auto-organizing (files will not be sorted here). Can still be dragged and reordered."
            : "Folder is Unlocked: Included in auto-organizing.";
        public string LockBtnText => IsLocked ? "🔒 Locked (Excluded)" : "🔓 Lock Folder";

        private Visibility _dragOverTopIndicatorVisibility = Visibility.Collapsed;
        public Visibility DragOverTopIndicatorVisibility
        {
            get => _dragOverTopIndicatorVisibility;
            set
            {
                if (_dragOverTopIndicatorVisibility != value)
                {
                    _dragOverTopIndicatorVisibility = value;
                    OnPropertyChanged();
                }
            }
        }

        private Visibility _dragOverBottomIndicatorVisibility = Visibility.Collapsed;
        public Visibility DragOverBottomIndicatorVisibility
        {
            get => _dragOverBottomIndicatorVisibility;
            set
            {
                if (_dragOverBottomIndicatorVisibility != value)
                {
                    _dragOverBottomIndicatorVisibility = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _colorHex = "#3B82F6"; // Default modern iOS blue
        private SolidColorBrush _colorBrush = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
        private SolidColorBrush _lightColorBrush = new SolidColorBrush(Color.FromArgb(28, 0x3B, 0x82, 0xF6));
        private SolidColorBrush _borderColorBrush = new SolidColorBrush(Color.FromArgb(70, 0x3B, 0x82, 0xF6));

        public string ColorHex
        {
            get => _colorHex;
            set
            {
                if (!string.Equals(_colorHex, value, StringComparison.OrdinalIgnoreCase))
                {
                    _colorHex = value;
                    UpdateBrushes();
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ColorBrush));
                    OnPropertyChanged(nameof(LightColorBrush));
                    OnPropertyChanged(nameof(BorderColorBrush));
                    OnPropertyChanged(nameof(SelectionBorderBrush));
                    OnPropertyChanged(nameof(SelectionBackgroundBrush));
                }
            }
        }

        private void UpdateBrushes()
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(_colorHex);
                _colorBrush = new SolidColorBrush(color);
                _colorBrush.Freeze();
                _lightColorBrush = new SolidColorBrush(Color.FromArgb(28, color.R, color.G, color.B));
                _lightColorBrush.Freeze();
                _borderColorBrush = new SolidColorBrush(Color.FromArgb(70, color.R, color.G, color.B));
                _borderColorBrush.Freeze();
            }
            catch
            {
                _colorBrush = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
                _colorBrush.Freeze();
                _lightColorBrush = new SolidColorBrush(Color.FromArgb(28, 0x3B, 0x82, 0xF6));
                _lightColorBrush.Freeze();
                _borderColorBrush = new SolidColorBrush(Color.FromArgb(70, 0x3B, 0x82, 0xF6));
                _borderColorBrush.Freeze();
            }
        }

        private bool _isSelected = false;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SelectionBorderBrush));
                    OnPropertyChanged(nameof(SelectionBackgroundBrush));
                    OnPropertyChanged(nameof(ActiveBarVisibility));
                }
            }
        }

        public Visibility ActiveBarVisibility => IsSelected ? Visibility.Visible : Visibility.Collapsed;

        public SolidColorBrush ColorBrush => _colorBrush;
        public SolidColorBrush LightColorBrush => _lightColorBrush;
        public SolidColorBrush BorderColorBrush => _borderColorBrush;

        public SolidColorBrush SelectionBorderBrush =>
            IsSelected ? new SolidColorBrush(Color.FromRgb(0xFB, 0xC3, 0x05)) : Brushes.Transparent;

        public SolidColorBrush SelectionBackgroundBrush =>
            IsSelected ? new SolidColorBrush(Color.FromArgb(32, 0xFB, 0xC3, 0x05)) : Brushes.Transparent;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public void NotifyHasSubFoldersChanged()
        {
            OnPropertyChanged(nameof(HasSubFolders));
            OnPropertyChanged(nameof(ChevronVisibility));
            OnPropertyChanged(nameof(ChevronIcon));
        }
    }
}
