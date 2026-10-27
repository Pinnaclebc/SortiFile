using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace SortiFileWpf.Models
{
    public class SortRecord : INotifyPropertyChanged
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("timestamp")]
        public string Timestamp { get; set; } = DateTime.Now.ToString("o");

        [JsonPropertyName("file_name")]
        public string FileName { get; set; } = "";

        [JsonPropertyName("original_path")]
        public string OriginalPath { get; set; } = "";

        [JsonPropertyName("destination_path")]
        public string DestinationPath { get; set; } = "";

        [JsonPropertyName("target_folder")]
        public string TargetFolder { get; set; } = "";

        [JsonPropertyName("category")]
        public string Category { get; set; } = "Unknown";

        [JsonPropertyName("confidence_score")]
        public int ConfidenceScore { get; set; } = 0;

        [JsonPropertyName("confidence_value")]
        public double ConfidenceValue { get; set; } = 0.0;

        [JsonPropertyName("reasoning")]
        public string Reasoning { get; set; } = "";

        [JsonPropertyName("engine")]
        public string Engine { get; set; } = "ollama";

        private string _status = "moved";
        [JsonPropertyName("status")]
        public string Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsMoved));
                    OnPropertyChanged(nameof(StatusDisplay));
                }
            }
        }

        [JsonIgnore]
        public bool IsMoved => Status == "moved";

        [JsonIgnore]
        public string StatusDisplay => Status == "moved" ? "✓ Moved" : (Status == "missing" ? "⚠ Missing" : "↺ Reverted");

        [JsonIgnore]
        public string TimeFormatted
        {
            get
            {
                if (DateTime.TryParse(Timestamp, out var dt))
                    return dt.ToString("HH:mm:ss");
                return Timestamp;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}

