using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SortiFileWpf.Models
{
    public class AppConfig
    {
        [JsonPropertyName("watch_dir")]
        public string WatchDir { get; set; } = "Watch_Folder";

        [JsonPropertyName("workspace_dir")]
        public string WorkspaceDir { get; set; } = "SortiFile_Workspace";

        [JsonPropertyName("confidence_threshold")]
        public int ConfidenceThreshold { get; set; } = 75;

        [JsonPropertyName("ollama_host")]
        public string OllamaHost { get; set; } = "http://127.0.0.1:11434";

        [JsonPropertyName("ollama_model")]
        public string OllamaModel { get; set; } = "llama3:8b";

        [JsonPropertyName("folder_colors")]
        public System.Collections.Generic.Dictionary<string, string> FolderColors { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        [JsonPropertyName("locked_folders")]
        public System.Collections.Generic.List<string> LockedFolders { get; set; } = new();

        [JsonPropertyName("folder_order")]
        public System.Collections.Generic.List<string> FolderOrder { get; set; } = new();

        [JsonPropertyName("is_first_run_completed")]
        public bool IsFirstRunCompleted { get; set; } = false;

        public static string GetConfigPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(baseDir, "config.json");
        }

        public static AppConfig Load()
        {
            try
            {
                string path = GetConfigPath();
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var cfg = JsonSerializer.Deserialize<AppConfig>(json);
                    if (cfg != null) return cfg;
                }
            }
            catch { }
            return new AppConfig();
        }

        public void Save()
        {
            try
            {
                string path = GetConfigPath();
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch { }
        }

        public string ResolvePath(string relativeOrAbs)
        {
            if (Path.IsPathRooted(relativeOrAbs)) return relativeOrAbs;
            return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativeOrAbs));
        }
    }
}

