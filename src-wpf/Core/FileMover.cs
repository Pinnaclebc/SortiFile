using System;
using System.Collections.Generic;
using System.IO;
using SortiFileWpf.Models;

namespace SortiFileWpf.Core
{
    public static class FileMover
    {
        public static string GetUniquePath(string targetDir, string fileName)
        {
            string dest = Path.Combine(targetDir, fileName);
            if (!File.Exists(dest)) return dest;

            string stem = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            int counter = 1;

            while (true)
            {
                string candidate = Path.Combine(targetDir, $"{stem} ({counter}){ext}");
                if (!File.Exists(candidate)) return candidate;
                counter++;
            }
        }

        public static (bool success, string destPath, string? error) MoveFile(string sourcePath, string targetDir)
        {
            try
            {
                if (!File.Exists(sourcePath))
                    return (false, "", $"Source file does not exist: {sourcePath}");

                if (!Directory.Exists(targetDir))
                    Directory.CreateDirectory(targetDir);

                string fileName = Path.GetFileName(sourcePath);
                string uniqueDest = GetUniquePath(targetDir, fileName);

                File.Move(sourcePath, uniqueDest);
                return (true, uniqueDest, null);
            }
            catch (Exception ex)
            {
                return (false, "", ex.Message);
            }
        }

        public static (bool success, string message) UndoMove(SortRecord record)
        {
            try
            {
                if (!File.Exists(record.DestinationPath))
                    return (false, $"File no longer exists at destination: {record.DestinationPath}");

                string origDir = Path.GetDirectoryName(record.OriginalPath) ?? "";
                if (!string.IsNullOrEmpty(origDir) && !Directory.Exists(origDir))
                    Directory.CreateDirectory(origDir);

                string safeOrig = GetUniquePath(origDir, Path.GetFileName(record.OriginalPath));
                File.Move(record.DestinationPath, safeOrig);
                return (true, $"Restored {record.FileName} to {origDir}");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }
}

