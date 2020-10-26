using Microsoft.Win32;
using Mono.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace QWCArchiveExtractor
{
    class Program
    {
        static bool verbose = false;
        static void Main(string[] args)
        {
            bool list = false;
            bool showHelp = false;
            string fileName = string.Empty;
            string folderName = string.Empty;
            string searchQuery = string.Empty;
            string gameDir = null;
            string outputDir = string.Empty;
            CCDFileManager ccdFile;

            if(args.Length == 0)
            {
                Console.WriteLine("Missing arguments, type '--help' for more information'");
                return;
            }

            var options = new OptionSet {
            { "e|extract=", "File to extract", n => fileName = n },
            { "c|compress=", "Folder to compress", c => folderName = c },
            { "l", "List file instead of extraction", l => list = (l != null) },
            { "v", "increase debug message verbosity", v => verbose  = (v != null) },
            { "s|search=", "search query through cli files", s => searchQuery = s },
            { "o|out=", "output directory", o => outputDir = o },
            { "d|dir=", "Game Directory, defaults to registry location", d => gameDir = d },
            { "h|help",  "show this message and exit", v => showHelp = v != null },
            };

            List<string> extra;
            try
            {
                extra = options.Parse(args);
            }
            catch (OptionException e)
            {
                Console.Write(e.Message);
                Console.WriteLine("Try '--help' for more information.");
            }

            if(showHelp)
            {
                options.WriteOptionDescriptions(Console.Out);
                return;
            }

            if (fileName != string.Empty && searchQuery != string.Empty && folderName != string.Empty)
            {
                Console.WriteLine("Entry only one of -s, -e or -c options.");
                return;
            }

            if (fileName != string.Empty)
            {
                if (!OpenArchive(fileName, out ccdFile, verbose))
                    return;

                string dirName = "";
                if (!list)
                {
                    if (string.IsNullOrEmpty(outputDir))
                        outputDir = Path.GetDirectoryName(fileName);
                    dirName = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(fileName));
                    if (!Directory.Exists(dirName))
                        Directory.CreateDirectory(dirName);
                    try
                    {
                        ExtractFiles(ccdFile, dirName);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Error Processing file => {fileName}: {e.Message}");
                        return;
                    }
                }
            }
            else if (folderName != string.Empty)
            {
                if (string.IsNullOrEmpty(outputDir))
                {
                    outputDir =
                        Path.Combine(Path.GetDirectoryName(folderName), Path.GetDirectoryName(outputDir) + ".ccd");
                }
                CCDFileManager.CompressDirectory(folderName, outputDir);
            }
            else
            {
                gameDir = gameDir ?? GetRegistryInstallDir();
                if (gameDir == string.Empty)
                {
                    Console.WriteLine("Error: Failed to obtain install directory via registry.");
                    return;
                }

                gameDir = Path.GetFullPath(gameDir);
                if (verbose)
                    Console.WriteLine($"Game Directory: {gameDir}");
                ScanFiles(gameDir, searchQuery, verbose);
            }

            Console.WriteLine("Operations complete!");
            Console.ReadKey();
        }

        private static void ScanFiles(string gameDir, string searchQuery, bool verbose)
        {
            if (!Directory.Exists(gameDir))
            {
                Console.WriteLine("Invalid Game Directory.");
                return;
            }

            string[] archFiles = Directory.GetFiles(gameDir, "*.ccd", SearchOption.AllDirectories);
            string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            foreach (string file in archFiles)
            {
                // Low-tier hack; re-create and delete unique folder for each file
                if (verbose)
                {
                    Console.WriteLine($"Scanning Archive: {file}");
                }
                var dirInfo = Directory.CreateDirectory(tempDir);
                CCDFileManager fileManager;
                if (!OpenArchive(file, out fileManager, false))
                    continue;

                ExtractFiles(fileManager, dirInfo.FullName, "*.cli");
                var cliList = Directory.GetFiles(tempDir);
                foreach (var cli in cliList)
                {
                    if (verbose)
                    {
                        Console.WriteLine($"(*)Scanning File: {Path.GetFileName(cli)}");
                    }
                    List<string> data = SearchCliFile(cli, searchQuery);
                    foreach (var d in data)
                        Console.WriteLine($"Found Command ({Path.GetFileName(file)}/{Path.GetFileName(cli)}): {d}");
                }
                dirInfo.Delete(true);
            }
        }

        private static List<string> SearchCliFile(string cliFile, string searchQuery)
        {
            var col = File.ReadLines(cliFile)
                .Select(s => s.Trim())
                .Where(s => !s.StartsWith("#") && s.Contains(searchQuery))
                .Where(s => !string.IsNullOrEmpty(s));

            return col.ToList();
        }

        private static string GetRegistryInstallDir()
        {
            var subKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\EA GAMES\Harry Potter - Quidditch WC");
            if (subKey != null)
            {
                return (string)subKey.GetValue("Install Dir", string.Empty);
            }
            return string.Empty;
        }

        static bool OpenArchive(string archiveLoc, out CCDFileManager fileManager, bool verbose)
        {
            var fileInfo = new FileInfo(archiveLoc);
            if (!fileInfo.Exists)
            {
                Console.WriteLine($"Unable to locate file: {fileInfo.Name}!");
                fileManager = null;
                return false;
            }

            try
            {
                fileManager = new CCDFileManager(fileInfo.FullName);
                if (verbose)
                {
                    Console.WriteLine("=== Archive Header ===");
                    Console.WriteLine(fileManager.Header + "\n");

                    foreach (CcdFileInfo cfi in fileManager.FileList)
                    {
                        Console.WriteLine("File Header:\n" + cfi);
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine($"ERROR: {e.Message} - {fileInfo.FullName}");
                fileManager = null;
                return false;
            }
        }

        static void ExtractFiles(CCDFileManager fileManager, string outputDir, string mask = "")
        {
            bool skipMask = string.IsNullOrEmpty(mask);
            string maskRegex = "^" + Regex.Escape(mask).Replace("\\*", ".*") + "$";
            foreach (CcdFileInfo cfi in fileManager.FileList)
            {
                if (!skipMask && !Regex.IsMatch(cfi.Name, maskRegex))
                    continue;
                if(verbose) Console.WriteLine($"Extracting: {cfi.Name}");
                string outputPath = Path.Combine(outputDir, cfi.Name);
                fileManager.ExtractFile(cfi.Name, outputPath);
            }
        }
    }
}
