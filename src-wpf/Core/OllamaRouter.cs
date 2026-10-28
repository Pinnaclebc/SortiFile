using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SortiFileWpf.Core
{
    public class RoutingResult
    {
        public string TargetFolder { get; set; } = "Inbox";
        public double ConfidenceValue { get; set; } = 50.0;
        public int ConfidenceScore
        {
            get => (int)Math.Round(ConfidenceValue);
            set => ConfidenceValue = value;
        }
        public string Reasoning { get; set; } = "";
        public string Engine { get; set; } = "Heuristic Fallback";
    }

    public class OllamaRouter
    {
        private readonly HttpClient _http;
        private string? _resolvedModel = null;
        public string Host { get; set; }
        public string Model { get; set; }
        public int Threshold { get; set; }

        public OllamaRouter(string host = "http://127.0.0.1:11434", string model = "llama3:8b", int threshold = 75)
        {
            Host = host.TrimEnd('/');
            Model = model;
            Threshold = threshold;
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        }

        public async Task<bool> IsOllamaOnlineAsync()
        {
            try
            {
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
                var res = await _http.GetAsync($"{Host}/api/tags", cts.Token);
                if (!res.IsSuccessStatusCode) return false;

                string json = await res.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("models", out var modelsElem) && modelsElem.ValueKind == JsonValueKind.Array)
                {
                    var available = new List<string>();
                    foreach (var m in modelsElem.EnumerateArray())
                    {
                        if (m.TryGetProperty("name", out var n) && n.GetString() is string name)
                        {
                            available.Add(name);
                        }
                    }

                    if (available.Count > 0)
                    {
                        if (available.Any(a => a.Equals(Model, StringComparison.OrdinalIgnoreCase) || a.StartsWith(Model + ":", StringComparison.OrdinalIgnoreCase)))
                        {
                            _resolvedModel = Model;
                        }
                        else
                        {
                            var chatModels = available.Where(a => !a.Contains("embed", StringComparison.OrdinalIgnoreCase) && !a.Contains("bge", StringComparison.OrdinalIgnoreCase)).ToList();
                            _resolvedModel = chatModels.Count > 0 ? chatModels[0] : available[0];
                        }
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<RoutingResult> RouteFileAsync(
            string filePath,
            string snippet,
            Dictionary<string, string> folderContexts,
            string category)
        {
            bool online = await IsOllamaOnlineAsync();
            if (online)
            {
                try
                {
                    var llmRes = await CallOllamaAsync(filePath, snippet, folderContexts);
                    if (llmRes != null)
                    {
                        if (llmRes.ConfidenceScore < Threshold)
                        {
                            llmRes.Reasoning = $"Confidence ({llmRes.ConfidenceScore}%) < Threshold ({Threshold}%). Routed to Inbox. Original: {llmRes.TargetFolder}. {llmRes.Reasoning}";
                            llmRes.TargetFolder = "Inbox";
                        }
                        return llmRes;
                    }
                }
                catch { }
            }

            // Heuristic Fallback
            var fallback = HeuristicRoute(filePath, snippet, folderContexts, category);
            if (fallback.ConfidenceScore < Threshold)
            {
                fallback.Reasoning = $"Confidence ({fallback.ConfidenceScore}%) < Threshold ({Threshold}%). Routed to Inbox. Original: {fallback.TargetFolder}. {fallback.Reasoning}";
                fallback.TargetFolder = "Inbox";
            }
            return fallback;
        }

        private async Task<RoutingResult?> CallOllamaAsync(string filePath, string snippet, Dictionary<string, string> folderContexts)
        {
            var sb = new StringBuilder();
            sb.AppendLine("### Available Target Folders & Contexts:");
            foreach (var kvp in folderContexts)
            {
                sb.AppendLine($"- **{kvp.Key}**: {kvp.Value}");
            }
            sb.AppendLine();
            sb.AppendLine("### File Info:");
            sb.AppendLine($"- Name: {Path.GetFileName(filePath)}");
            sb.AppendLine($"- Extension: {Path.GetExtension(filePath)}");
            sb.AppendLine($"- Snippet: {snippet}");
            sb.AppendLine();
            sb.AppendLine("Determine the single best target folder and your confidence score (0-100).");
            sb.AppendLine("Output strictly valid JSON with keys: 'target_folder', 'confidence_score', and 'reasoning'.");

            string activeModel = _resolvedModel ?? Model;

            var payload = new
            {
                model = activeModel,
                messages = new[]
                {
                    new { role = "system", content = "You are an expert file organizer. Given a file's name, extension, and content snippet, match it to the most relevant target folder based on the folder names and descriptions. If a file relates to a parent category or subcategory (such as invoices, bills, receipts, taxes, videos, or documents), select that folder with high confidence (80-95). Do NOT select 'Inbox' unless the file is truly unclassifiable, empty, or corrupted binary data. Return strictly valid JSON with keys 'target_folder', 'confidence_score' (integer 0-100), and 'reasoning'." },
                    new { role = "user", content = sb.ToString() }
                },
                format = "json",
                stream = false,
                options = new { temperature = 0.1 }
            };

            string jsonBody = JsonSerializer.Serialize(payload);
            using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var res = await _http.PostAsync($"{Host}/api/chat", content);
            if (!res.IsSuccessStatusCode) return null;

            string resJson = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(resJson);
            string msgContent = doc.RootElement.GetProperty("message").GetProperty("content").GetString() ?? "";

            // Parse json from message
            var cleanJson = ExtractJson(msgContent);
            if (cleanJson != null)
            {
                using var parsed = JsonDocument.Parse(cleanJson);
                var root = parsed.RootElement;
                string folder = root.TryGetProperty("target_folder", out var f) ? f.GetString() ?? "Inbox" : "Inbox";

                int conf = 50;
                if (root.TryGetProperty("confidence_score", out var c))
                {
                    if (c.ValueKind == JsonValueKind.Number)
                    {
                        conf = (int)Math.Round(c.GetDouble());
                    }
                    else if (c.ValueKind == JsonValueKind.String && int.TryParse(c.GetString(), out int parsedConf))
                    {
                        conf = parsedConf;
                    }
                }

                string reason = root.TryGetProperty("reasoning", out var r) ? r.GetString() ?? "AI decision" : "AI decision";

                // Normalize folder name
                string matched = MatchFolder(folder, folderContexts.Keys);
                return new RoutingResult
                {
                    TargetFolder = matched,
                    ConfidenceScore = conf,
                    Reasoning = reason,
                    Engine = $"Ollama ({activeModel})"
                };
            }

            return null;
        }

        private static string? ExtractJson(string text)
        {
            var match = Regex.Match(text, @"\{.*?\}", RegexOptions.Singleline);
            return match.Success ? match.Value : null;
        }

        private static string MatchFolder(string candidate, IEnumerable<string> available)
        {
            foreach (var folder in available)
            {
                if (folder.Equals(candidate, StringComparison.OrdinalIgnoreCase)) return folder;
            }
            foreach (var folder in available)
            {
                if (folder.Contains(candidate, StringComparison.OrdinalIgnoreCase) || candidate.Contains(folder, StringComparison.OrdinalIgnoreCase))
                    return folder;
            }
            return "Inbox";
        }

        private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "and", "the", "for", "with", "this", "that", "from", "into", "over", "more",
            "some", "such", "than", "then", "them", "these", "those", "context", "files",
            "documents", "relevant", "related", "assets", "common", "types", "includes", "general",
            "specific", "subcategory", "category"
        };

        private static string NormalizeStem(string w)
        {
            w = w.ToLowerInvariant();
            if (w.EndsWith("ies") && w.Length > 4) return w.Substring(0, w.Length - 3) + "y";
            if (w.EndsWith("es") && w.Length > 3) return w.Substring(0, w.Length - 2);
            if (w.EndsWith("s") && !w.EndsWith("ss") && w.Length > 3) return w.Substring(0, w.Length - 1);
            if (w.EndsWith("ing") && w.Length > 5) return w.Substring(0, w.Length - 3);
            if (w.EndsWith("ed") && w.Length > 4) return w.Substring(0, w.Length - 2);
            return w;
        }

        private static bool WordsMatch(string w1, string w2)
        {
            if (w1.Equals(w2, StringComparison.OrdinalIgnoreCase)) return true;
            if (w1.Length >= 2 && w2.Length >= 2)
            {
                string s1 = NormalizeStem(w1);
                string s2 = NormalizeStem(w2);
                if (s1.Equals(s2, StringComparison.OrdinalIgnoreCase)) return true;
                if ((s1.StartsWith(s2, StringComparison.OrdinalIgnoreCase) || s2.StartsWith(s1, StringComparison.OrdinalIgnoreCase)) &&
                    Math.Min(s1.Length, s2.Length) >= 3)
                    return true;
            }
            return false;
        }

        private static List<string> ExtractWords(string text)
        {
            return Regex.Matches((text ?? "").ToLowerInvariant(), @"\b[a-z0-9]{2,}\b")
                        .Select(m => m.Value)
                        .Where(w => !StopWords.Contains(w))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
        }

        private class CandidateScore
        {
            public string FolderPath { get; set; } = "";
            public double Score { get; set; } = 0.0;
            public int UniqueMatches { get; set; } = 0;
            public int InheritedMatches { get; set; } = 0;
            public bool NameMatch { get; set; } = false;
            public bool FileNameTokenMatch { get; set; } = false;
            public string Reasoning { get; set; } = "";
        }

        private static RoutingResult HeuristicRoute(string filePath, string snippet, Dictionary<string, string> folderContexts, string category)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            string name = Path.GetFileName(filePath).ToLowerInvariant();
            string content = (snippet ?? "").ToLowerInvariant();

            var fileNameWords = ExtractWords(name);
            var contentWords = ExtractWords(name + " " + content);

            bool isVideo = ext is ".mp4" or ".mov" or ".mkv" or ".avi" or ".webm" or ".wmv" or ".flv" or ".m4v";
            bool isAudio = ext is ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" or ".m4a";
            bool isImage = ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".svg" or ".ico" or ".bmp" or ".tiff";
            bool isArchive = category == "Archive/Installer" || ext is ".zip" or ".tar" or ".gz" or ".tgz" or ".7z" or ".rar" or ".bz2" or ".xz" or ".msi" or ".iso";
            bool isDoc = category == "Text/Doc" || ext is ".txt" or ".md" or ".docx" or ".pdf" or ".csv" or ".tsv" or ".json" or ".xlsx" or ".xls" or ".pptx" or ".ppt" or ".rtf" or ".xml" or ".html";

            var candidateScores = new Dictionary<string, CandidateScore>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in folderContexts)
            {
                string folder = kvp.Key.Replace('\\', '/');
                if (folder is "Inbox" or "Code_Projects") continue;

                string folderDesc = kvp.Value ?? "";
                int lastSlash = folder.LastIndexOf('/');
                bool isSubfolder = lastSlash > 0;
                string parentFolder = isSubfolder ? folder.Substring(0, lastSlash) : "";
                string selfName = isSubfolder ? folder.Substring(lastSlash + 1) : folder;

                // Split self-name tokens (e.g. "AWS" or "HR_Policies")
                var selfTokens = selfName.Split(new[] { '_', '-', ' ', '/' }, StringSplitOptions.RemoveEmptyEntries)
                                         .Where(t => t.Length >= 2 && !StopWords.Contains(t))
                                         .ToList();
                bool fileNameTokenMatch = selfTokens.Any(st => fileNameWords.Any(fw => WordsMatch(st, fw)));
                bool selfNameMatch = selfTokens.Any(st => contentWords.Any(cw => WordsMatch(st, cw)));

                // Separate unique subcategory context from inherited parent context
                string inheritedPart = "";
                string uniquePart = folderDesc;
                if (folderDesc.Contains("[Subcategory:"))
                {
                    int idx = folderDesc.IndexOf("[Subcategory:");
                    inheritedPart = folderDesc.Substring(0, idx);
                    uniquePart = folderDesc.Substring(idx);
                }

                var uniqueWords = ExtractWords(selfName + " " + uniquePart);
                var inheritedWords = ExtractWords(inheritedPart);

                int uniqueMatches = uniqueWords.Count(uw => contentWords.Any(cw => WordsMatch(uw, cw)));
                int inheritedMatches = inheritedWords.Count(iw => contentWords.Any(cw => WordsMatch(iw, cw)));

                // Filter out accidental single-word match on academic "paper" if file is just printer supplies
                if (folder.Contains("Paper", StringComparison.OrdinalIgnoreCase) || folder.Contains("Research", StringComparison.OrdinalIgnoreCase))
                {
                    bool hasAcademicContext = contentWords.Any(w => w is "academic" or "research" or "neural" or "transformer" or "learning" or "study" or "model" or "dataset" or "abstract");
                    if (!hasAcademicContext && selfTokens.Any(st => st.Equals("papers", StringComparison.OrdinalIgnoreCase) || st.Equals("paper", StringComparison.OrdinalIgnoreCase)))
                    {
                        selfNameMatch = false;
                        fileNameTokenMatch = false;
                        uniqueMatches = 0;
                    }
                }

                double score = 50.0;
                string reason = "";

                if (isVideo)
                {
                    string combined = (selfName + " " + folderDesc).ToLowerInvariant();
                    bool isVideoFolder = combined.Contains("video") || combined.Contains("movie") || combined.Contains("recording") || combined.Contains("footage") || combined.Contains("clip") || combined.Contains("tutorial");
                    if (isVideoFolder)
                    {
                        score = 88.0 + (fileNameTokenMatch ? 6.0 : (selfNameMatch ? 3.0 : 0.0)) + (uniqueMatches * 3.5);
                        if (isSubfolder && (fileNameTokenMatch || selfNameMatch || uniqueMatches > 0))
                        {
                            score += 5.0; // Subfolder specificity boost for video!
                        }
                        if (combined.Contains("audio") || combined.Contains("podcast")) score -= 50.0;
                        reason = $"Matched video file format ({ext}) to {folder}.";
                    }
                }
                else if (isAudio)
                {
                    string combined = (selfName + " " + folderDesc).ToLowerInvariant();
                    bool isAudioFolder = combined.Contains("audio") || combined.Contains("music") || combined.Contains("sound") || combined.Contains("podcast") || combined.Contains("sfx") || selfName.Equals("sfx", StringComparison.OrdinalIgnoreCase);
                    if (isAudioFolder)
                    {
                        score = 88.0 + (fileNameTokenMatch ? 6.0 : (selfNameMatch ? 3.0 : 0.0)) + (uniqueMatches * 3.5);
                        if (isSubfolder && (fileNameTokenMatch || selfNameMatch || uniqueMatches > 0))
                        {
                            score += 5.0; // Subfolder specificity boost for audio!
                        }
                        if (combined.Contains("video") || combined.Contains("movie")) score -= 50.0;
                        reason = $"Matched audio file format ({ext}) to {folder}.";
                    }
                }
                else if (isArchive)
                {
                    string combined = (selfName + " " + folderDesc).ToLowerInvariant();
                    if (combined.Contains("archive") || combined.Contains("backup") || combined.Contains("zip") || combined.Contains("package") || combined.Contains("installer"))
                    {
                        score = 90.0 + (fileNameTokenMatch ? 5.0 : 0.0) + (uniqueMatches * 2.5);
                        reason = $"Matched compressed archive format ({ext}) to {folder}.";
                    }
                }
                else if (isImage)
                {
                    if (fileNameTokenMatch || selfNameMatch || uniqueMatches > 0)
                    {
                        score = 88.0 + (fileNameTokenMatch ? 6.0 : (selfNameMatch ? 3.0 : 0.0)) + (uniqueMatches * 3.5);
                        if (isSubfolder && (fileNameTokenMatch || uniqueMatches > 0)) score += 4.0;
                        reason = $"Matched image asset context keywords for '{folder}'.";
                    }
                    else if (folder.Equals("Images", StringComparison.OrdinalIgnoreCase))
                    {
                        score = 93.0;
                        reason = $"Matched visual image extension ({ext}) to Images folder.";
                    }
                }
                else if (isDoc)
                {
                    bool isMediaOnly = selfName.Contains("Video", StringComparison.OrdinalIgnoreCase) ||
                                       selfName.Contains("Audio", StringComparison.OrdinalIgnoreCase) ||
                                       selfName.Contains("Images", StringComparison.OrdinalIgnoreCase) ||
                                       selfName.Contains("Archives", StringComparison.OrdinalIgnoreCase);

                    if (fileNameTokenMatch || selfNameMatch || uniqueMatches > 0 || inheritedMatches > 0)
                    {
                        double points = (fileNameTokenMatch ? 9.5 : (selfNameMatch ? 5.0 : 0.0)) + (uniqueMatches * 4.8) + (inheritedMatches * 1.5);
                        score = 80.0 + points;

                        if (isSubfolder && (fileNameTokenMatch || uniqueMatches > 0))
                        {
                            score += 4.5; // Subfolder specificity boost!
                            reason = $"Matched subfolder '{folder}' with {uniqueMatches} specific context keywords.";
                        }
                        else
                        {
                            reason = $"Matched {uniqueMatches + inheritedMatches} taxonomy keywords for '{folder}'.";
                        }
                    }
                    else if (folder.Equals("Documents", StringComparison.OrdinalIgnoreCase) && isDoc)
                    {
                        score = 85.0;
                        reason = "Default document classification to Documents.";
                    }

                    if (isMediaOnly && uniqueMatches < 3 && !fileNameTokenMatch)
                    {
                        score = 40.0; // Prevent documents from falling into video/audio folders
                    }
                }

                // High-precision fractional component to break ties accurately
                double tieBreaker = (uniqueMatches * 0.17) + (inheritedMatches * 0.03) + (selfNameMatch ? 0.08 : 0.0);
                score = Math.Min(98.9, Math.Max(50.0, score + tieBreaker));

                candidateScores[folder] = new CandidateScore
                {
                    FolderPath = folder,
                    Score = score,
                    UniqueMatches = uniqueMatches,
                    InheritedMatches = inheritedMatches,
                    NameMatch = selfNameMatch,
                    FileNameTokenMatch = fileNameTokenMatch,
                    Reasoning = reason
                };
            }

            // Select the highest scoring folder across all candidates
            string bestFolder = "Inbox";
            double bestScore = 50.0;
            string bestReason = "Unrecognized binary or low confidence format routed to Inbox for review.";

            foreach (var kvp in candidateScores)
            {
                if (kvp.Value.Score > bestScore)
                {
                    bestScore = kvp.Value.Score;
                    bestFolder = kvp.Key;
                    bestReason = kvp.Value.Reasoning;
                }
            }

            // HIERARCHICAL SUBFOLDER CHECK:
            // Check if the chosen folder (or parent) has subfolders that match the file's specific context or have higher confidence
            string rootCandidate = bestFolder.Contains('/') ? bestFolder.Substring(0, bestFolder.IndexOf('/')) : bestFolder;
            var subfolders = candidateScores.Keys
                .Where(k => k.StartsWith(rootCandidate + "/", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (subfolders.Count > 0)
            {
                string? winningSubfolder = null;
                double winningSubScore = bestScore;

                foreach (var sub in subfolders)
                {
                    var subCandidate = candidateScores[sub];
                    // Subfolder wins if its score is higher than the parent, or has specific name/filename matches with strong affinity
                    bool isSubfolderStronger = subCandidate.Score > bestScore ||
                                               (subCandidate.NameMatch && subCandidate.FileNameTokenMatch) ||
                                               (subCandidate.UniqueMatches >= 2 && subCandidate.Score >= (bestScore - 1.0));

                    if (isSubfolderStronger)
                    {
                        double effectiveSubScore = Math.Max(subCandidate.Score, bestScore + 0.5);
                        if (effectiveSubScore > winningSubScore || winningSubfolder == null)
                        {
                            winningSubScore = effectiveSubScore;
                            winningSubfolder = sub;
                            bestReason = $"Matched specific subfolder '{sub}' (confidence {effectiveSubScore:F1}%) over parent '{rootCandidate}'.";
                        }
                    }
                }

                if (winningSubfolder != null)
                {
                    bestFolder = winningSubfolder;
                    bestScore = winningSubScore;
                }
            }

            return new RoutingResult
            {
                TargetFolder = bestFolder,
                ConfidenceValue = bestScore,
                Reasoning = bestReason,
                Engine = "Heuristic Fallback"
            };
        }
    }
}

