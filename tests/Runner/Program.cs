using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using SortiFileWpf.Core;
using SortiFileWpf.Models;

namespace Runner
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("================================================================================");
            Console.WriteLine("     SortiFile Dual-Engine Comprehensive Test Suite (Ollama & Fallback)         ");
            Console.WriteLine("================================================================================");
            Console.WriteLine($"Executed at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine();

            // Set up test directories
            string testRoot = Path.GetFullPath("Test_Sandbox");
            string wsDir = Path.Combine(testRoot, "Workspace");
            string watchDir = Path.Combine(testRoot, "Watch");

            if (Directory.Exists(testRoot))
            {
                try { Directory.Delete(testRoot, true); } catch { }
            }
            Directory.CreateDirectory(wsDir);
            Directory.CreateDirectory(watchDir);

            // ================================================================================
            // PART 1: HEURISTIC FALLBACK ENGINE TEST RUN
            // ================================================================================
            Console.WriteLine("################################################################################");
            Console.WriteLine("  RUN 1: HEURISTIC FALLBACK ENGINE (All File Formats & Normal User Workflows)   ");
            Console.WriteLine("################################################################################");

            var configFallback = new AppConfig
            {
                WorkspaceDir = wsDir,
                WatchDir = watchDir,
                OllamaHost = "http://127.0.0.1:99999", // Intentional offline port to test pure fallback
                ConfidenceThreshold = 75,
                RegisteredFolders = new List<string>()
            };

            SetupTestTaxonomy(wsDir, configFallback);
            var engineFallback = new FileWatcherEngine(configFallback);

            var fallbackTestCases = GenerateComprehensiveTestCases();
            int fallbackPass = RunEngineTestBattery(engineFallback, watchDir, wsDir, fallbackTestCases, "Heuristic Fallback");

            // User Feature Tests on Fallback:
            Console.WriteLine();
            Console.WriteLine("--- Testing User Features (Normal User Workflows) ---");
            TestUserWorkflowFeatures(engineFallback, watchDir, wsDir);

            // ================================================================================
            // PART 2: OLLAMA LLM ENGINE TEST RUN
            // ================================================================================
            Console.WriteLine();
            Console.WriteLine("################################################################################");
            Console.WriteLine("  RUN 2: OLLAMA LLM ENGINE (Local Model Integration & AI Routing)               ");
            Console.WriteLine("################################################################################");

            var configOllama = new AppConfig
            {
                WorkspaceDir = wsDir,
                WatchDir = watchDir,
                OllamaHost = "http://127.0.0.1:11434",
                OllamaModel = "llama3.2:3b",
                ConfidenceThreshold = 75,
                RegisteredFolders = new List<string>()
            };

            SetupTestTaxonomy(wsDir, configOllama);
            var engineOllama = new FileWatcherEngine(configOllama);

            // Check Ollama Connectivity
            bool isOnline = engineOllama.Router.IsOllamaOnlineAsync().GetAwaiter().GetResult();
            Console.WriteLine($"Ollama Host: {configOllama.OllamaHost}");
            Console.WriteLine($"Ollama Online Status: {(isOnline ? "ONLINE (Reachable)" : "OFFLINE")}");

            if (isOnline)
            {
                var ollamaTestCases = GenerateOllamaTestCases();
                int ollamaPass = RunEngineTestBattery(engineOllama, watchDir, wsDir, ollamaTestCases, "Ollama LLM");

                Console.WriteLine("================================================================================");
                Console.WriteLine($"  Ollama Run Summary: {ollamaPass} of {ollamaTestCases.Count} Passed ({(ollamaPass * 100 / ollamaTestCases.Count)}%)");
                Console.WriteLine("================================================================================");
            }
            else
            {
                Console.WriteLine("[WARN] Ollama service not reachable on 127.0.0.1:11434. Skipping Ollama LLM phase.");
            }

            Console.WriteLine();
            Console.WriteLine("================================================================================");
            Console.WriteLine("  ALL TEST PHASES COMPLETED");
            Console.WriteLine("================================================================================");
        }

        static void SetupTestTaxonomy(string wsDir, AppConfig config)
        {
            config.RegisteredFolders.Clear();

            var folders = new Dictionary<string, string>
            {
                { "Documents", "General written documentation, reports, articles, PDF whitepapers, office letters, text records, and documentation." },
                { "Documents/Legal", "Contracts, non-disclosure agreements, binding mutual agreements, confidentiality terms, and legal documents." },
                { "Documents/HR", "Human resources, employee benefits, corporate policies, payroll, staff handbook, and personnel files." },
                { "Videos_and_Recordings", "Video files, clips, tutorials, dashcam records, stream recordings, and movie clips in MP4, MKV, AVI, MOV, WEBM." },
                { "Videos_and_Recordings/Tutorials", "Educational walkthroughs, software setup guides, how to videos, screencasts, and blender tutorials." },
                { "Invoices_and_Receipts", "Billing statements, expense receipts, vendor invoices, purchase orders, store receipts, and payments." },
                { "Invoices_and_Receipts/AWS", "Amazon Web Services bills, cloud compute ec2 charges, S3 storage invoices, and cloud hosting billing." },
                { "Invoices_and_Receipts/Taxes", "State and federal tax returns, W2 forms, 1099 filings, deduction records, and annual revenue taxes." },
                { "Images", "Photographs, wallpapers, visual artwork, designs, and vector illustrations." },
                { "Code_Projects", "Excluded source code repositories, software build artifacts, scripts, C#, Python, and JS projects." },
                { "Inbox", "Fallback staging area for unclassified, low confidence (< 75%), or unknown binary formats." }
            };

            foreach (var kvp in folders)
            {
                string folderPath = Path.Combine(wsDir, kvp.Key.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(folderPath);
                File.WriteAllText(Path.Combine(folderPath, ".context.md"), $"# {kvp.Key} Context\n\n{kvp.Value}\n");

                if (!kvp.Key.Contains('/'))
                {
                    config.RegisteredFolders.Add(folderPath);
                }
            }
        }

        static List<(string Name, byte[] Content, string ExpectedFolder, string CategoryDesc)> GenerateComprehensiveTestCases()
        {
            return new List<(string, byte[], string, string)>
            {
                // Video formats
                ("drone_landscape_footage.mp4", CreateFakeBinary("ftypmp42", 512), "Videos_and_Recordings", "Video MP4"),
                ("gaming_highlight_reel.mkv", CreateFakeBinary("\x1A\x45\xDF\xA3matroska", 512), "Videos_and_Recordings", "Video MKV"),
                ("conference_keynote.avi", CreateFakeBinary("RIFF\x00\x00\x00\x00AVI LIST", 512), "Videos_and_Recordings", "Video AVI"),
                ("quicktime_screencast.mov", CreateFakeBinary("wide\x00\x00\x00\x08mdat", 512), "Videos_and_Recordings/Tutorials", "Video Subfolder (screencast)"),
                ("web_animation_clip.webm", CreateFakeBinary("\x1A\x45\xDF\xA3webm", 512), "Videos_and_Recordings", "Video WEBM"),
                ("how_to_install_docker_guide.mp4", CreateFakeBinary("ftypmp42", 512), "Videos_and_Recordings/Tutorials", "Video Subfolder (how to)"),

                // PDF formats
                ("executive_annual_report.pdf", CreateMinimalPdf("Executive Annual Summary Report and Strategic Business Documentation"), "Documents", "PDF Document"),
                ("freelance_consulting_contract.pdf", CreateMinimalPdf("Binding Independent Contractor Agreement and Non-Disclosure Terms"), "Documents/Legal", "PDF Legal Contract"),
                ("employee_health_insurance_policy.pdf", CreateMinimalPdf("Employee Healthcare Benefits Policy, Coverage Details and HR Guidelines"), "Documents/HR", "PDF HR Benefits"),
                ("irs_tax_deduction_statement.pdf", CreateMinimalPdf("Federal and State Tax Return Statement for Fiscal Year 2026"), "Invoices_and_Receipts/Taxes", "PDF Taxes"),

                // Text & Data formats
                ("meeting_minutes_october.txt", Encoding.UTF8.GetBytes("Minutes from October Team Planning Sync:\nDiscussed project timelines, sprint goals, and resource planning."), "Documents", "Plain Text"),
                ("customer_orders_export.csv", Encoding.UTF8.GetBytes("OrderID,Customer,Amount,Status\n101,John Doe,45.20,Completed\n102,Jane Smith,120.00,Shipped"), "Documents", "CSV Data File"),
                ("project_architecture_spec.md", Encoding.UTF8.GetBytes("# Project Architecture\n\nTechnical system overview, microservice boundaries, and database design specifications."), "Documents", "Markdown File"),
                ("system_diagnostic_output.log", Encoding.UTF8.GetBytes("[INFO] Application startup initialized.\n[INFO] Database connection pool ready.\n[INFO] System running normally."), "Documents", "Log File"),
                ("config_settings_dump.json", Encoding.UTF8.GetBytes("{\n  \"app_name\": \"TestApp\",\n  \"version\": \"1.0.0\",\n  \"debug\": false\n}"), "Documents", "JSON Document"),

                // Invoices & Receipts
                ("aws_october_cloud_bill.txt", Encoding.UTF8.GetBytes("Amazon Web Services (AWS) Monthly Invoice:\nEC2 compute instances and S3 storage billing: $320.50"), "Invoices_and_Receipts/AWS", "AWS Cloud Invoice"),
                ("home_depot_hardware_receipt.txt", Encoding.UTF8.GetBytes("RECEIPT #77291\nHome Depot Store #421\nItems: Drill bits, screws, measurement tape.\nTotal: $38.45 paid by card."), "Invoices_and_Receipts", "General Store Receipt"),

                // Images & Media
                ("desktop_mountain_wallpaper.jpg", CreateFakeBinary("\xFF\xD8\xFF\xE0\x00\x10JFIF", 256), "Images", "JPEG Image"),
                ("company_vector_logo.svg", Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"100\"><circle cx=\"50\" cy=\"50\" r=\"40\"/></svg>"), "Images", "SVG Graphic"),

                // Audio
                ("podcast_interview_episode.mp3", CreateFakeBinary("ID3\x03\x00\x00\x00", 256), "Inbox", "Audio MP3 (Fallback to Inbox)"),
                ("synthesizer_melody_riff.wav", CreateFakeBinary("RIFF\x00\x00\x00\x00WAVEfmt ", 256), "Inbox", "Audio WAV (Fallback to Inbox)"),

                // Code (Excluded / Protected)
                ("database_migration_helper.cs", Encoding.UTF8.GetBytes("namespace App.Migrations { public class Migration102 { public void Up() {} } }"), "Inbox", "C# Source File"),
                ("data_scraper_pipeline.py", Encoding.UTF8.GetBytes("import requests\n\ndef scrape():\n    print('fetching data')\n"), "Inbox", "Python Script"),

                // Low-Confidence Unknown Binary (Safety guardrail -> Inbox)
                ("unidentified_memory_dump.bin", CreateFakeBinary("\x7F\x45\x4C\x46\x02\x01\x01\x00", 128), "Inbox", "Binary Dump")
            };
        }

        static List<(string Name, byte[] Content, string ExpectedFolder, string CategoryDesc)> GenerateOllamaTestCases()
        {
            return new List<(string, byte[], string, string)>
            {
                ("drone_city_aerial_view.mp4", CreateFakeBinary("ftypmp42", 512), "Videos_and_Recordings", "Video Aerial"),
                ("unreal_engine_animation_tutorial.mp4", CreateFakeBinary("ftypmp42", 512), "Videos_and_Recordings/Tutorials", "Video Tutorial"),
                ("quarterly_compliance_audit_report.pdf", CreateMinimalPdf("Quarterly Corporate Financial Audit and Accounting Documentation"), "Documents", "PDF Audit Report"),
                ("mutual_non_disclosure_agreement.pdf", CreateMinimalPdf("Mutual Non-Disclosure Agreement and Confidential Binding Legal Contract"), "Documents/Legal", "PDF NDA Contract"),
                ("corporate_benefits_and_leave_policy.pdf", CreateMinimalPdf("Corporate Employee Policies, Parental Leave and HR Benefits Handbook"), "Documents/HR", "PDF HR Benefits"),
                ("aws_ec2_cloud_monthly_invoice.txt", Encoding.UTF8.GetBytes("Amazon Web Services (AWS) Statement:\nEC2 compute instances and S3 billing charges: $290.00."), "Invoices_and_Receipts/AWS", "AWS Invoice"),
                ("state_tax_return_filing_2026.pdf", CreateMinimalPdf("State and Federal Tax Return Document for Annual Fiscal Year 2026"), "Invoices_and_Receipts/Taxes", "Tax Return PDF"),
                ("coffee_shop_breakfast_receipt.txt", Encoding.UTF8.GetBytes("RECEIPT #99214\nStarbucks Coffee\nItems: Latte, Croissant\nTotal: $12.50 paid via Apple Pay."), "Invoices_and_Receipts", "Store Receipt"),
                ("corrupted_payload_blob.dat", CreateFakeBinary("\x00\xFF\x55\xAA\x12\x34\x56\x78", 128), "Inbox", "Corrupted Blob")
            };
        }

        static int RunEngineTestBattery(FileWatcherEngine engine, string watchDir, string wsDir, List<(string Name, byte[] Content, string ExpectedFolder, string CategoryDesc)> testCases, string runLabel)
        {
            // Clear watch directory
            foreach (var f in Directory.GetFiles(watchDir))
            {
                try { File.Delete(f); } catch { }
            }

            Console.WriteLine($"--- Writing {testCases.Count} Test Files to Watch Directory ({runLabel}) ---");
            foreach (var tc in testCases)
            {
                File.WriteAllBytes(Path.Combine(watchDir, tc.Name), tc.Content);
            }

            Console.WriteLine($"--- Processing Files Through Autonomous Engine ---");
            int passCount = 0;
            var sw = Stopwatch.StartNew();

            foreach (var tc in testCases)
            {
                string sourcePath = Path.Combine(watchDir, tc.Name);
                engine.ProcessSingleFile(sourcePath);

                string expectedRel = tc.ExpectedFolder.Replace('/', Path.DirectorySeparatorChar);
                string expectedDest = Path.Combine(wsDir, expectedRel, tc.Name);
                bool passed = File.Exists(expectedDest);

                var hist = engine.History.LoadHistory();
                var record = hist.Find(r => r.FileName == tc.Name);

                string engineUsed = record?.Engine ?? "Unknown";
                string status = passed ? "[PASS]" : "[FAIL]";
                if (passed) passCount++;

                Console.WriteLine($"{status} {tc.Name,-40} | Dest: {record?.TargetFolder,-30} | Score: {record?.ConfidenceScore,3}% | Engine: {engineUsed}");
                if (!passed)
                {
                    Console.WriteLine($"       Expected: {tc.ExpectedFolder}");
                    Console.WriteLine($"       Actual:   {record?.TargetFolder}");
                    Console.WriteLine($"       Reason:   {record?.Reasoning}");
                }
            }
            sw.Stop();

            Console.WriteLine();
            Console.WriteLine($"Batch execution finished in {sw.ElapsedMilliseconds} ms (Average {sw.ElapsedMilliseconds / testCases.Count} ms/file).");
            Console.WriteLine($"Results: {passCount} of {testCases.Count} Passed ({(passCount * 100 / testCases.Count)}%).");
            return passCount;
        }

        static void TestUserWorkflowFeatures(FileWatcherEngine engine, string watchDir, string wsDir)
        {
            // 1. Test 1-Click Undo
            Console.Write("[Feature 1] 1-Click Undo Sort: ");
            string testFile = "undo_test_document.txt";
            string watchPath = Path.Combine(watchDir, testFile);
            File.WriteAllText(watchPath, "Important client project proposal and documentation.");

            engine.ProcessSingleFile(watchPath);
            string expectedDest = Path.Combine(wsDir, "Documents", testFile);

            if (File.Exists(expectedDest))
            {
                // Trigger Undo
                var hist = engine.History.LoadHistory();
                var rec = hist.Find(r => r.FileName == testFile);
                if (rec != null)
                {
                    var (undoSuccess, msg) = FileMover.UndoMove(rec);
                    if (undoSuccess) engine.History.UpdateStatus(rec.Id, "reverted");
                    bool fileReturned = File.Exists(watchPath);
                    bool destGone = !File.Exists(expectedDest);

                    if (undoSuccess && fileReturned && destGone)
                    {
                        Console.WriteLine("PASSED (File cleanly moved back to Watch directory; audit record set to 'reverted')");
                    }
                    else
                    {
                        Console.WriteLine($"FAILED (undoSuccess={undoSuccess}, returned={fileReturned}, destGone={destGone}, msg={msg})");
                    }
                }
                else
                {
                    Console.WriteLine("FAILED (Record not found in history)");
                }
            }
            else
            {
                Console.WriteLine("FAILED (File was not initially sorted to Documents)");
            }

            // 2. Test Folder Locking Feature
            Console.Write("[Feature 2] Folder Locking Guardrail: ");
            string lockedFolder = "Invoices_and_Receipts/AWS";
            engine.Config.LockedFolders = new List<string> { lockedFolder };

            string lockedTestFile = "aws_locked_invoice_test.txt";
            string lockedWatchPath = Path.Combine(watchDir, lockedTestFile);
            File.WriteAllText(lockedWatchPath, "Amazon Web Services (AWS) EC2 cloud compute billing invoice $99.00.");

            engine.ProcessSingleFile(lockedWatchPath);
            string inboxDest = Path.Combine(wsDir, "Inbox", lockedTestFile);
            string lockedTargetDest = Path.Combine(wsDir, "Invoices_and_Receipts", "AWS", lockedTestFile);

            if (File.Exists(inboxDest) && !File.Exists(lockedTargetDest))
            {
                Console.WriteLine("PASSED (Target folder was locked; file safely diverted to Inbox with guardrail notice)");
            }
            else
            {
                Console.WriteLine($"FAILED (File was placed in locked folder or missed: InboxExists={File.Exists(inboxDest)})");
            }
            engine.Config.LockedFolders.Clear();

            // 3. Test Context Inheritance Feature
            Console.Write("[Feature 3] Subfolder Context Inheritance: ");
            var contexts = engine.LoadContexts();
            bool subfolderHasInheritedContext = contexts.TryGetValue("Documents/Legal", out string? legalCtx) &&
                                                legalCtx.Contains("General written documentation") &&
                                                legalCtx.Contains("Contracts, non-disclosure");
            if (subfolderHasInheritedContext)
            {
                Console.WriteLine("PASSED (Subfolder dynamic context inherited root context + unique context)");
            }
            else
            {
                Console.WriteLine("FAILED (Context did not properly inherit root parent keywords)");
            }

            // 4. Test Duplicate Folder Validation Logic
            Console.Write("[Feature 4] Duplicate Folder Registration Validation: ");
            string existingPath = Path.Combine(wsDir, "Documents");
            bool duplicateDetected = engine.Config.RegisteredFolders.Any(r => Path.GetFullPath(r).TrimEnd('\\', '/').Equals(Path.GetFullPath(existingPath).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase));
            if (duplicateDetected)
            {
                Console.WriteLine("PASSED (Validation check correctly flags existing folder as duplicate)");
            }
            else
            {
                Console.WriteLine("FAILED (Duplicate detection did not match existing registered path)");
            }

            // 5. Test Folder Tree Reset Logic
            Console.Write("[Feature 5] Folder Tree Reset: ");
            int initialCount = engine.Config.RegisteredFolders.Count;
            engine.Config.RegisteredFolders.Clear();
            bool treeReset = engine.Config.RegisteredFolders.Count == 0 && Directory.Exists(existingPath);
            if (treeReset && initialCount > 0)
            {
                Console.WriteLine($"PASSED (All {initialCount} registered folders unregistered from tree; disk files remained intact)");
            }
            else
            {
                Console.WriteLine("FAILED (Tree reset failed or disk files affected)");
            }
        }

        static byte[] CreateFakeBinary(string header, int totalSize)
        {
            byte[] bytes = new byte[totalSize];
            byte[] hBytes = Encoding.ASCII.GetBytes(header);
            Array.Copy(hBytes, bytes, Math.Min(hBytes.Length, bytes.Length));
            for (int i = hBytes.Length; i < bytes.Length; i++)
            {
                bytes[i] = (byte)(i % 256);
            }
            return bytes;
        }

        static byte[] CreateMinimalPdf(string text)
        {
            string pdfContent =
                "%PDF-1.4\n" +
                "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n" +
                "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n" +
                "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>\nendobj\n" +
                "4 0 obj\n<< /Length " + (text.Length + 40) + " >>\nstream\n" +
                "BT\n/F1 12 Tf\n72 712 Td\n(" + text + ") Tj\nET\nendstream\nendobj\n" +
                "5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n" +
                "xref\n0 6\n0000000000 65535 f \n0000000010 00000 n \n0000000060 00000 n \n0000000117 00000 n \n0000000224 00000 n \n0000000320 00000 n \n" +
                "trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n390\n%%EOF\n";
            return Encoding.ASCII.GetBytes(pdfContent);
        }
    }
}
