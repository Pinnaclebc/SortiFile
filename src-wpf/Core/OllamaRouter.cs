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
        public int ConfidenceScore { get; set; } = 50;
        public string Reasoning { get; set; } = "";
        public string Engine { get; set; } = "Heuristic Fallback";
    }

    public class OllamaRouter
    {
        private readonly HttpClient _http;
        public string Host { get; set; }
        public string Model { get; set; }
        public int Threshold { get; set; }

        public OllamaRouter(string host = "http://127.0.0.1:11434", string model = "llama3:8b", int threshold = 75)
        {
            Host = host.TrimEnd('/');
            Model = model;
            Threshold = threshold;
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        }

        public async Task<bool> IsOllamaOnlineAsync()
        {
            try
            {
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromMilliseconds(800));
                var res = await _http.GetAsync($"{Host}/api/tags", cts.Token);
                return res.IsSuccessStatusCode;
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

            var payload = new
            {
                model = Model,
                messages = new[]
                {
                    new { role = "system", content = "You are a local file sorting agent. Output strictly valid JSON with keys 'target_folder', 'confidence_score', and 'reasoning'. If unsure, output 'Inbox'." },
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
                int conf = root.TryGetProperty("confidence_score", out var c) ? c.GetInt32() : 50;
                string reason = root.TryGetProperty("reasoning", out var r) ? r.GetString() ?? "AI decision" : "AI decision";

                // Normalize folder name
                string matched = MatchFolder(folder, folderContexts.Keys);
                return new RoutingResult
                {
                    TargetFolder = matched,
                    ConfidenceScore = conf,
                    Reasoning = reason,
                    Engine = $"Ollama ({Model})"
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

        private static RoutingResult HeuristicRoute(string filePath, string snippet, Dictionary<string, string> folderContexts, string category)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            string name = Path.GetFileName(filePath).ToLowerInvariant();
            string content = (snippet ?? "").ToLowerInvariant();

            if (category == "Media/Binary" || ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".svg" or ".ico")
            {
                if (folderContexts.ContainsKey("Images"))
                {
                    return new RoutingResult
                    {
                        TargetFolder = "Images",
                        ConfidenceScore = 93,
                        Reasoning = $"Matched visual image extension ({ext}) to Images folder.",
                        Engine = "Heuristic Fallback"
                    };
                }
            }

            if (category == "Text/Doc" || ext is ".txt" or ".md" or ".docx" or ".pdf" or ".csv" or ".tsv" or ".json")
            {
                string bestFolder = folderContexts.ContainsKey("Documents") ? "Documents" : "Inbox";
                int score = 88;
                string reason = $"Matched document extension ({ext}) to Documents folder.";

                foreach (var kvp in folderContexts)
                {
                    if (kvp.Key is "Inbox" or "Code_Projects") continue;
                    if (name.Contains(kvp.Key.ToLowerInvariant()) || content.Contains(kvp.Key.ToLowerInvariant()))
                    {
                        bestFolder = kvp.Key;
                        score = 95;
                        reason = $"File content or name matches folder '{kvp.Key}'.";
                        break;
                    }
                }

                return new RoutingResult
                {
                    TargetFolder = bestFolder,
                    ConfidenceScore = score,
                    Reasoning = reason,
                    Engine = "Heuristic Fallback"
                };
            }

            return new RoutingResult
            {
                TargetFolder = "Inbox",
                ConfidenceScore = 60,
                Reasoning = "Unrecognized binary or low confidence format routed to Inbox for review.",
                Engine = "Heuristic Fallback"
            };
        }
    }
}

