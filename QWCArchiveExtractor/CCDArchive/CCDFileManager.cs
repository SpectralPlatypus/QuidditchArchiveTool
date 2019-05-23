using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using OpenSage.FileFormats.RefPack;

namespace QWCArchiveExtractor
{

    internal class CCDFileManager
    {
        static readonly string FKNL = "FKNL";
        // static readonly string FIL = " FIL";

        private CcdHeader header;
        internal CcdHeader Header { get => header; set => header = value; }

        private List<CcdFileInfo> fileList;
        internal List<CcdFileInfo> FileList { get => fileList; set => fileList = value; }

        private string filePath;
        private string decompFilePath;
        private bool fileDeflated = false;

        public CCDFileManager(string filePath)
        {
            this.filePath = filePath;
            decompFilePath =
                Path.GetTempPath() + Path.GetFileNameWithoutExtension(filePath) + @"_decomp.cdd";

            if (!File.Exists(filePath))
            {
                Console.WriteLine("No file");
                throw new FileNotFoundException();
            }

            header = new CcdHeader();
            fileList = new List<CcdFileInfo>(15);

            ParseFile();
        }

        public void ParseFile()
        {
            ParseHeader();
            ParseDir();
            DecompressDirectory();
        }

        private void ParseHeader()
        {
            using (FileStream fs = new FileStream(filePath, FileMode.Open))
            using (BinaryReader reader = new BinaryReader(fs))
            {
                byte[] fknl = reader.ReadBytes(4);
                if (Encoding.ASCII.GetString(fknl) != FKNL)
                {
                    Console.WriteLine("FKNL error");
                    throw new InvalidDataException();
                }

                header.version = reader.ReadUInt32();
                header.firstFileOffset = reader.ReadUInt32();

                //Skip unknown bytes
                reader.ReadUInt32();

                header.fileDataLen = reader.ReadUInt32();
                header.fileCount = reader.ReadUInt32();
                reader.ReadUInt32();

                fknl = reader.ReadBytes(4);

                // This doesn't apply to all files, hence commented out
                /*if (Encoding.ASCII.GetString(fknl) != FIL)
                {
                    Console.WriteLine("FIL error");
                    throw new InvalidDataException();
                }*/

                header.dirLen = reader.ReadUInt32();
                // Skip next 4 bytes
                reader.ReadUInt32();

                header.dirOffset = reader.ReadUInt32();
                if (header.dirOffset != 56)
                {
                    throw new InvalidDataException();
                }

                reader.ReadUInt32();
                reader.ReadUInt32();

                if (reader.ReadByte() != 0x0)
                {
                    throw new InvalidDataException();
                }
            }
        }

        private int ParseDir()
        {
            using (FileStream fileStream = new FileStream(filePath, FileMode.Open))
            using (BinaryReader reader = new BinaryReader(fileStream))
            {
                for (int i = 0; i < header.fileCount; ++i)
                {
                    CcdFileInfo file = new CcdFileInfo();
                    fileStream.Seek(header.dirOffset + (i * 16), SeekOrigin.Begin);

                    file.NameOffset = reader.ReadUInt32();
                    file.Offset = reader.ReadUInt32();
                    file.Length = reader.ReadUInt32();
                    if (reader.ReadUInt32() != file.Length)
                    {
                        Console.WriteLine("File length mismatch");
                        throw new InvalidDataException();
                    }

                    // Read the filename from offset
                    fileStream.Seek(file.NameOffset, SeekOrigin.Begin);
                    StringBuilder stringBuilder = new StringBuilder(20);
                    char c;
                    while ((c = reader.ReadChar()) != '\0')
                    {
                        stringBuilder.Append(c);
                    }

                    file.Name = stringBuilder.ToString();

                    fileList.Add(file);
                }
            }
            return 0;
        }

        private int DecompressDirectory()
        {
            uint fileOffset = header.firstFileOffset;
            byte[] buffer;

            using (FileStream fs = new FileStream(filePath, FileMode.Open))
            {
                fs.Seek(fileOffset, SeekOrigin.Begin);
                using (RefPackStream rps = new RefPackStream(fs))
                {
                    buffer = new byte[rps.Length];
                    rps.Read(buffer, 0, buffer.Length);
                }
            }

            using (FileStream fs = new FileStream(decompFilePath, FileMode.Create))
            using (BufferedStream bs = new BufferedStream(fs))
            {
                bs.Write(buffer, 0, buffer.Length);
            }

            fileDeflated = true;
            return 0;
        }

        public void ExtractFile(string filename, string outputPath)
        {
            if (!fileDeflated) return;

            var fileInfo = fileList.Find(fi => fi.Name == filename);
            if (fileInfo.Equals(default(CcdFileInfo))) throw new FileNotFoundException();

            // Files should be small enough to fit in memory...
            byte[] buffer = new byte[fileInfo.Length];

            using (FileStream fs = new FileStream(decompFilePath, FileMode.Open))
            {
                fs.Seek((int)fileInfo.Offset, SeekOrigin.Begin);
                int len = 0;
                while (len != buffer.Length)
                {
                    len += fs.Read(buffer, len, buffer.Length - len);
                }
            }

            using (FileStream fs = new FileStream(outputPath, FileMode.Create))
                fs.Write(buffer, 0, buffer.Length);
        }
    }
}
