using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SortiFileWpf.Models
{
    public class QueueItem : INotifyPropertyChanged
    {
        public string FilePath { get; set; } = "";
        public string FileName => System.IO.Path.GetFileName(FilePath);
        public string Extension => System.IO.Path.GetExtension(FilePath).ToUpper().TrimStart('.');

        private string _status = "Queued";
        public string Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusDisplay));
                }
            }
        }

        private string _stage = "1. Lock Check";
        public string Stage
        {
            get => _stage;
            set
            {
                if (_stage != value)
                {
                    _stage = value;
                    OnPropertyChanged();
                }
            }
        }

        public string StatusDisplay => $"{Stage} ({Status})";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}

