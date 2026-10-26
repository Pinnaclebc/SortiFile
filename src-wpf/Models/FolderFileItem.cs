using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SortiFileWpf.Models
{
    public class FolderFileItem : INotifyPropertyChanged
    {
        private string _fileName = "";
        public string FileName
        {
            get => _fileName;
            set { if (_fileName != value) { _fileName = value; OnPropertyChanged(); } }
        }

        public string FullPath { get; set; } = "";
        public string Extension { get; set; } = "";
        public long SizeBytes { get; set; } = 0;
        public DateTime ModifiedDate { get; set; } = DateTime.Now;

        public string SizeDisplay => FormatSize(SizeBytes);
        public string ModifiedDisplay => ModifiedDate.ToString("yyyy-MM-dd HH:mm");

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
                    OnPropertyChanged(nameof(CardBackgroundBrush));
                    OnPropertyChanged(nameof(CardBorderBrush));
                }
            }
        }

        public Brush CardBackgroundBrush => IsSelected
            ? new SolidColorBrush(Color.FromRgb(0xFF, 0xFD, 0xEB)) // Soft warm yellow
            : Brushes.White;

        public Brush CardBorderBrush => IsSelected
            ? new SolidColorBrush(Color.FromRgb(0xFB, 0xC3, 0x05)) // Logo Yellow
            : new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0));

        public bool IsImage
        {
            get
            {
                string ext = (Extension ?? "").ToLowerInvariant().TrimStart('.');
                return ext is "png" or "jpg" or "jpeg" or "gif" or "webp" or "bmp" or "tiff" or "ico";
            }
        }

        public bool IsVideo
        {
            get
            {
                string ext = (Extension ?? "").ToLowerInvariant().TrimStart('.');
                return ext is "mp4" or "mkv" or "avi" or "mov" or "wmv" or "webm" or "flv" or "m4v";
            }
        }

        public bool IsDoc => !IsImage && !IsVideo;

        private ImageSource? _thumbnailSource;
        public ImageSource? ThumbnailSource
        {
            get => _thumbnailSource;
            set
            {
                _thumbnailSource = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasThumbnail));
                OnPropertyChanged(nameof(ThumbnailVisibility));
                OnPropertyChanged(nameof(BackupIconVisibility));
            }
        }

        public bool HasThumbnail => ThumbnailSource != null;
        public Visibility ThumbnailVisibility => HasThumbnail ? Visibility.Visible : Visibility.Collapsed;
        public Visibility BackupIconVisibility => HasThumbnail ? Visibility.Collapsed : Visibility.Visible;

        public Geometry BackupIconGeometry
        {
            get
            {
                if (IsImage)
                    return (Geometry)Application.Current.FindResource("PictureIconGeo");
                if (IsVideo)
                    return (Geometry)Application.Current.FindResource("VideoIconGeo");
                return (Geometry)Application.Current.FindResource("FileDocGeo");
            }
        }

        public Brush BackupIconBrush
        {
            get
            {
                if (IsImage)
                    return new SolidColorBrush(Color.FromRgb(0xFB, 0xC3, 0x05)); // Logo Yellow
                if (IsVideo)
                    return new SolidColorBrush(Color.FromRgb(0xFB, 0xC3, 0x05)); // Logo Yellow
                return new SolidColorBrush(Color.FromRgb(0xFB, 0xC3, 0x05)); // Logo Yellow
            }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
