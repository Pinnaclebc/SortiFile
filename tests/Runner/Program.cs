using System;
using System.Collections.Generic;
using System.IO;
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
            Console.WriteLine("  SortiFile Hierarchical Subfolder & Minimal Context (1-3 Words) Test Suite     ");
            Console.WriteLine("================================================================================");

            var config = AppConfig.Load();
            config.WorkspaceDir = Path.GetFullPath("SortiFile_Workspace");
            config.WatchDir = Path.GetFullPath("Watch_Folder");

            string wsDir = config.WorkspaceDir;
            string watchDir = config.WatchDir;

            Console.WriteLine($"Workspace Directory: {wsDir}");
            Console.WriteLine($"Watch Directory:     {watchDir}");
            Console.WriteLine();

            var engine = new FileWatcherEngine(config);

            // Display discovered context hierarchy
            Console.WriteLine("--- Loaded Workspace Hierarchy & Effective Inherited Contexts ---");
            var contexts = engine.LoadContexts();
            foreach (var kvp in contexts)
            {
                string snippet = kvp.Value.Replace("\r", " ").Replace("\n", " ").Replace("  ", " ");
                if (snippet.Length > 90) snippet = snippet.Substring(0, 87) + "...";
                bool isSub = kvp.Key.Contains('/');
                string prefix = isSub ? "  ├── [SUB] " : "  * [ROOT] ";
                Console.WriteLine($"{prefix}{kvp.Key,-36}: {snippet}");
            }
            Console.WriteLine();

            // Prepare Watch_Folder
            Directory.CreateDirectory(watchDir);
            foreach (var f in Directory.GetFiles(watchDir))
            {
                try { File.Delete(f); } catch { }
            }

            var testCases = new List<(string Name, byte[] Content, string ExpectedFolder, string MinimalContextTested, string Description)>
            {
                // 1. Subfolder with minimal context: "aws bills" (2 words)
                (
                    "aws_cloud_monthly_invoice.txt",
                    Encoding.UTF8.GetBytes("INVOICE #INV-2026-98124\nVendor: Amazon Web Services (AWS)\nMonthly EC2 Compute and S3 cloud bill charges: $420.00\nPayment confirmed."),
                    "Invoices_and_Receipts/AWS",
                    "\"aws bills\" (2 words)",
                    "AWS Invoice -> Subfolder Invoices_and_Receipts/AWS"
                ),

                // 2. Subfolder with minimal context: "tax forms" (2 words)
                (
                    "state_tax_return_2026.pdf",
                    CreateMinimalPdf("State and Federal Tax Deduction Return Document for Annual Filing Year 2026"),
                    "Invoices_and_Receipts/Taxes",
                    "\"tax forms\" (2 words)",
                    "Tax Return PDF -> Subfolder Invoices_and_Receipts/Taxes"
                ),

                // 3. General invoice with NO subfolder match -> Stays in Parent Folder!
                (
                    "staples_office_supplies_receipt.txt",
                    Encoding.UTF8.GetBytes("RECEIPT #88129\nStaples Office Supplies\nItems: Printer paper, pens, binder clips\nTotal: $64.20 paid in cash."),
                    "Invoices_and_Receipts",
                    "No subfolder match (stays in parent)",
                    "General Receipt -> Parent Invoices_and_Receipts"
                ),

                // 4. Subfolder with minimal context: "employee policies" (2 words)
                (
                    "employee_benefits_handbook.pdf",
                    CreateMinimalPdf("Corporate Employee Policies and Benefits Handbook Guidelines"),
                    "Documents/HR",
                    "\"employee policies\" (2 words)",
                    "HR Handbook PDF -> Subfolder Documents/HR"
                ),

                // 5. Subfolder with minimal context: "contracts" (1 word!)
                (
                    "non_disclosure_agreement_contract.pdf",
                    CreateMinimalPdf("Mutual Non-Disclosure Agreement and Confidentiality Binding Contract"),
                    "Documents/Legal",
                    "\"contracts\" (1 word)",
                    "NDA Contract PDF -> Subfolder Documents/Legal"
                ),

                // 6. General Document with NO subfolder match -> Stays in Parent Folder!
                (
                    "general_weekly_meeting_notes.txt",
                    Encoding.UTF8.GetBytes("Team Weekly Sync Notes:\nDiscussion on quarterly targets, sprint milestones, and team updates."),
                    "Documents",
                    "No subfolder match (stays in parent)",
                    "General Meeting Notes -> Parent Documents"
                ),

                // 7. Subfolder with minimal context: "pixel art" (2 words)
                (
                    "hero_character_spritesheet.png",
                    CreateFakeBinary("\x89PNG\r\n\x1a\n", 256),
                    "Game_Design_Assets/Sprites",
                    "\"pixel art\" (2 words)",
                    "Character Sprite -> Subfolder Game_Design_Assets/Sprites"
                ),

                // 8. Subfolder with minimal context: "sound effects" (2 words)
                (
                    "laser_cannon_blast_sound.wav",
                    CreateFakeBinary("RIFF\x00\x00\x00\x00WAVEfmt ", 256),
                    "Game_Design_Assets/SFX",
                    "\"sound effects\" (2 words)",
                    "Laser Audio WAV -> Subfolder Game_Design_Assets/SFX"
                ),

                // 9. General Game Design Doc with NO subfolder match -> Stays in Parent Folder!
                (
                    "gameplay_mechanics_physics_blueprint.txt",
                    Encoding.UTF8.GetBytes("Game Design Mechanics Document:\nPlayer double jump friction physics, wall-climb collision boundaries, and camera lerp."),
                    "Game_Design_Assets",
                    "No subfolder match (stays in parent)",
                    "Game Mechanics Doc -> Parent Game_Design_Assets"
                ),

                // 10. Subfolder with minimal context: "how to" (2 words)
                (
                    "how_to_setup_blender_rendering.mp4",
                    CreateFakeBinary("ftypmp42", 512),
                    "Videos_and_Recordings/Tutorials",
                    "\"how to\" (2 words)",
                    "Tutorial Video -> Subfolder Videos_and_Recordings/Tutorials"
                ),

                // 11. General Video with NO subfolder match -> Stays in Parent Folder!
                (
                    "dashcam_highway_recording.mp4",
                    CreateFakeBinary("ftypmp42", 512),
                    "Videos_and_Recordings",
                    "No subfolder match (stays in parent)",
                    "General Highway Video -> Parent Videos_and_Recordings"
                ),

                // 12. Safety Fallback (< 75% threshold)
                (
                    "unlabeled_corrupted_blob.bin",
                    CreateFakeBinary("\x00\xFF\x55\xAA", 128),
                    "Inbox",
                    "N/A (low confidence)",
                    "Corrupted Binary -> Inbox (Safety Guardrail)"
                )
            };

            Console.WriteLine($"--- Ingesting {testCases.Count} Test Cases into Watch_Folder ---");
            foreach (var tc in testCases)
            {
                string filePath = Path.Combine(watchDir, tc.Name);
                File.WriteAllBytes(filePath, tc.Content);
                Console.WriteLine($"  [+] Created: {tc.Name} [{tc.MinimalContextTested}]");
            }
            Console.WriteLine();

            Console.WriteLine("--- Executing FileWatcherEngine Autonomous Routing ---");
            int passCount = 0;

            foreach (var tc in testCases)
            {
                string sourcePath = Path.Combine(watchDir, tc.Name);

                // Run processing through engine
                engine.ProcessSingleFile(sourcePath);

                // Normalize path check
                string expectedRel = tc.ExpectedFolder.Replace('/', Path.DirectorySeparatorChar);
                string expectedDest = Path.Combine(wsDir, expectedRel, tc.Name);
                bool passed = File.Exists(expectedDest);
                if (passed) passCount++;

                var hist = engine.History.LoadHistory();
                var record = hist.Find(r => r.FileName == tc.Name);

                string outcomeLabel = passed ? "[PASS]" : "[FAIL]";
                Console.WriteLine($"{outcomeLabel} {tc.Name}");
                Console.WriteLine($"       Expected: {tc.ExpectedFolder}");
                Console.WriteLine($"       Landed:   {record?.TargetFolder}");
                Console.WriteLine($"       UI Score (Whole %): {record?.ConfidenceScore}% | Backend Precision: {record?.ConfidenceValue:F2}%");
                Console.WriteLine($"       Reason:   {record?.Reasoning}");
                Console.WriteLine();
            }

            Console.WriteLine("================================================================================");
            Console.WriteLine($"  Test Summary: {passCount} of {testCases.Count} Passed ({(passCount * 100 / testCases.Count)}%)");
            Console.WriteLine("================================================================================");
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
