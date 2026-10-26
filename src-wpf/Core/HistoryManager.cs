using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SortiFileWpf.Models;

namespace SortiFileWpf.Core
{
    public class HistoryManager
    {
        private readonly string _historyFile;
        private readonly object _lock = new();

        public HistoryManager(string? historyFile = null)
        {
            if (string.IsNullOrEmpty(historyFile))
            {
                _historyFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sort_history.json");
            }
            else
            {
                _historyFile = historyFile;
            }
            EnsureFileExists();
        }

        private void EnsureFileExists()
        {
            lock (_lock)
            {
                if (!File.Exists(_historyFile))
                {
                    try
                    {
                        string dir = Path.GetDirectoryName(_historyFile) ?? "";
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        {
                            Directory.CreateDirectory(dir);
                        }
                        File.WriteAllText(_historyFile, "[]");
                    }
                    catch { }
                }
            }
        }

        public List<SortRecord> LoadHistory()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(_historyFile))
                    {
                        string json = File.ReadAllText(_historyFile);
                        var list = JsonSerializer.Deserialize<List<SortRecord>>(json);
                        if (list != null) return list;
                    }
                }
                catch { }
                return new List<SortRecord>();
            }
        }

        public void RecordMove(SortRecord record)
        {
            lock (_lock)
            {
                var history = LoadHistory();
                history.Insert(0, record);
                SaveHistory(history);
            }
        }

        public bool MarkReverted(string id)
        {
            return UpdateStatus(id, "reverted");
        }

        public bool UpdateStatus(string id, string status)
        {
            lock (_lock)
            {
                var history = LoadHistory();
                var item = history.FirstOrDefault(h => h.Id == id);
                if (item != null)
                {
                    item.Status = status;
                    SaveHistory(history);
                    return true;
                }
                return false;
            }
        }

        private void SaveHistory(List<SortRecord> history)
        {
            try
            {
                string json = JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_historyFile, json);
            }
            catch { }
        }
    }
}

