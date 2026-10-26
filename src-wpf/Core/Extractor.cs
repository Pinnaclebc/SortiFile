using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace SortiFileWpf.Core
{
    public static class Extractor
    {
        private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".md", ".markdown", ".csv", ".tsv", ".json", ".xml", ".html", ".htm", ".rtf", ".log", ".yaml", ".yml", ".ini", ".cfg", ".docx", ".pdf"
        };

        private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".svg", ".ico", ".tiff", ".mp4", ".mov", ".mkv", ".avi", ".mp3", ".wav", ".flac", ".exe", ".bin"
        };

        private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".zip", ".tar", ".gz", ".tgz", ".7z", ".rar", ".bz2", ".xz", ".msi", ".iso", ".dmg"
        };

        private static readonly HashSet<string> TempExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".crdownload", ".tmp", ".part", ".download", ".partial", ".filepart"
        };

        public static bool IsTempOrPartial(string path)
        {
            string ext = Path.GetExtension(path);
            if (TempExtensions.Contains(ext)) return true;
            string name = Path.GetFileName(path);
            if (name.StartsWith("~$") || name.StartsWith(".~")) return true;
            return false;
        }

        public static string ClassifyFile(string path)
        {
            string ext = Path.GetExtension(path);
            if (TextExtensions.Contains(ext)) return "Text/Doc";
            if (MediaExtensions.Contains(ext)) return "Media/Binary";
            if (ArchiveExtensions.Contains(ext)) return "Archive/Installer";
            return "Unknown";
        }

        public static bool IsFileLocked(string filePath)
        {
            if (!File.Exists(filePath)) return true;
            if (IsTempOrPartial(filePath)) return true;

            try
            {
                var fi = new FileInfo(filePath);
                if (fi.Length == 0)
                {
                    using (File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) { }
                    return false;
                }

                using (FileStream stream = fi.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    stream.Close();
                }
                return false;
            }
            catch (IOException)
            {
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
        }

        public static bool WaitUntilUnlocked(string filePath, int maxRetries = 5, int delayMs = 600)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                if (!IsFileLocked(filePath)) return true;
                Thread.Sleep(delayMs);
            }
            return !IsFileLocked(filePath);
        }

        public static string ExtractTextSnippet(string filePath, int maxWords = 200)
        {
            if (!File.Exists(filePath)) return "";
            string ext = Path.GetExtension(filePath).ToLowerInvariant();

            try
            {
                if (ext == ".docx")
                {
                    return ExtractDocx(filePath, maxWords);
                }

                if (ext is ".txt" or ".md" or ".markdown" or ".csv" or ".tsv" or ".json" or ".log" or ".xml" or ".html")
                {
                    using var reader = new StreamReader(filePath, Encoding.UTF8, true);
                    char[] buffer = new char[8192];
                    int read = reader.Read(buffer, 0, buffer.Length);
                    string raw = new string(buffer, 0, read);
                    return TruncateToWords(raw, maxWords);
                }
            }
            catch { }

            string fileName = Path.GetFileNameWithoutExtension(filePath).Replace("_", " ").Replace("-", " ");
            return $"Asset file: {Path.GetFileName(filePath)} (Name tokens: {fileName})";
        }

        private static string ExtractDocx(string filePath, int maxWords)
        {
            try
            {
                using var zip = ZipFile.OpenRead(filePath);
                var entry = zip.GetEntry("word/document.xml");
                if (entry != null)
                {
                    using var stream = entry.Open();
                    using var reader = new StreamReader(stream);
                    string xml = reader.ReadToEnd();
                    string clean = Regex.Replace(xml, "<.*?>", " ");
                    return TruncateToWords(clean, maxWords);
                }
            }
            catch { }
            return "[Word Document Content]";
        }

        private static string TruncateToWords(string text, int maxWords)
        {
            var words = text.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length <= maxWords)
                return string.Join(" ", words);

            return string.Join(" ", words.Take(maxWords)) + "...";
        }
    }
}

