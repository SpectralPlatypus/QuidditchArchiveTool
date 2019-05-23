using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace QWCArchiveExtractor
{
    class Program
    {
        static void Main(string[] args)
        {
            bool verbose = false;

            if (args.Length < 1)
            {
                Console.WriteLine("Missing argument: CCD file path");
                return;
            }

            if (args.Length > 1 && Array.Exists(args, s => s.Equals("-v")))
            {
                verbose = true;
            }

            var fileInfo = new FileInfo(args[0]);
            if (!fileInfo.Exists)
            {
                Console.WriteLine($"Unable to locate file: {fileInfo.Name}!");
                return;
            }

            try
            {
                string dirName = Path.GetFileNameWithoutExtension(fileInfo.Name);
                if (!Directory.Exists(dirName))
                    Directory.CreateDirectory(dirName);

                var ccdFile = new CCDFileManager(fileInfo.FullName);
                if (verbose)
                {
                    Console.WriteLine("=== Archive Header ===");
                    Console.WriteLine(ccdFile.Header + "\n");
                }

                foreach (CcdFileInfo cfi in ccdFile.FileList)
                {
                    Console.WriteLine($"Extracting: {cfi.Name}");
                    if (verbose)
                    {
                        Console.WriteLine("File Header:\n" + cfi);
                    }

                    string outputPath = Path.Combine(dirName, cfi.Name);
                    ccdFile.ExtractFile(cfi.Name, outputPath);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error Processing file => {fileInfo.Name}: {e.Message}");
                return;
            }

            Console.WriteLine("Operations complete!");
        }
    }
}
