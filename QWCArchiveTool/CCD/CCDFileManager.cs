using OpenSage.FileFormats.RefPack;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace QWCArchiveTool
{

    internal class CCDFileManager
    {
        static readonly string FKNL = "FKNL";
        //static readonly string FIL = " FIL";

        private CcdHeader header;
        internal CcdHeader Header { get => header; set => header = value; }
        internal List<CcdFileInfo> FileList { get; set; }

        private string filePath;
        private bool fileDeflated = false;
        private MemoryStream decompStream;

        const uint PaddingLength = 0x217;


        public CCDFileManager(string filePath)
        {
            this.filePath = filePath;

            if (!File.Exists(filePath))
            {
                Console.WriteLine("No file");
                throw new FileNotFoundException();
            }

            decompStream = new MemoryStream();
            header = new CcdHeader();
            FileList = new List<CcdFileInfo>(15);

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
            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
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
                header.uncompressedFolderSize = reader.ReadUInt32();

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
            using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
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

                    FileList.Add(file);
                }
            }
            return 0;
        }

        private int DecompressDirectory()
        {
            uint fileOffset = header.firstFileOffset;

            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                fs.Seek(fileOffset, SeekOrigin.Begin);
                using (RefPackStream rps = new RefPackStream(fs))
                {
                    rps.CopyTo(decompStream);
                }
            }

            fileDeflated = true;
            return 0;
        }

        public void ExtractFile(string filename, string outputPath)
        {
            if (!fileDeflated) return;

            var fileInfo = FileList.Find(fi => fi.Name == filename);
            if (fileInfo.Equals(default(CcdFileInfo))) throw new FileNotFoundException();

            // Files should be small enough to fit in memory...
            byte[] buffer = new byte[fileInfo.Length];

            decompStream.Seek((int)fileInfo.Offset, SeekOrigin.Begin);

            // Likely can be simplified now that this doesn't use a filestream
            int len = 0;
            while (len != buffer.Length)
            {
                len += decompStream.Read(buffer, len, buffer.Length - len);
            }

            using (FileStream fs = new FileStream(outputPath, FileMode.Create))
                fs.Write(buffer, 0, buffer.Length);
        }

        ~CCDFileManager()
        {
            if (fileDeflated && decompStream != null)
                decompStream.Dispose();
        }

        public static void CompressDirectory(string inputDir, string outFile)
        {
            if (!Directory.Exists(inputDir))
                throw new DirectoryNotFoundException();

            // Parse all contents of the directory and  append them to a MemoryStream
            var fileArray = Directory.GetFiles(inputDir);

            if (fileArray.Length == 0)
                throw new FileNotFoundException("Can't compress empty directory");

            var plainStream = new MemoryStream();
            var encodeStream = new MemoryStream();

            CcdHeader header = new CcdHeader
            {
                version = 2,
                fileCount = (uint)fileArray.Length,
                dirOffset = 0x38
            };
            header.dirLen = header.dirOffset + (header.fileCount * 16);

            List<CcdFileInfo> fileInfo = new List<CcdFileInfo>(fileArray.Length);
            uint fileNameOffset = header.dirLen + 1; // +1 for null byte
            uint fileOffset = 0;
            uint deflatedDirLen = 0;

            foreach (var file in fileArray)
            {
                var info = new FileInfo(file);
                CcdFileInfo ccdFileInfo = new CcdFileInfo
                {
                    Name = Path.GetFileName(file),
                    Length = (uint)info.Length,
                    Offset = fileOffset,
                    NameOffset = fileNameOffset
                };

                // Header only allocates 4-bytes for dir length
                deflatedDirLen += (uint)info.Length;

                fileInfo.Add(ccdFileInfo);
                fileOffset += ccdFileInfo.Length;
                fileNameOffset += (uint)ccdFileInfo.Name.Length + 1; // +1 for null byte

                AddFile(file, plainStream);
            }

            header.uncompressedFolderSize = deflatedDirLen;

            using (var rsc = new RefPackCompress(encodeStream))
            {
                plainStream.Position = 0;
                byte[] array = plainStream.GetBuffer();
                rsc.Write(array, 0, array.Length);
            }

            plainStream.Close();

            header.fileDataLen = (uint)encodeStream.Length;

            fileNameOffset += PaddingLength;
            header.firstFileOffset = fileNameOffset;

            using (var fs = new FileStream(outFile, FileMode.Create, FileAccess.Write))
            {
                WriteHeader(header, fs);
                foreach (var file in fileInfo)
                    WriteFileHeader(file, fs);

                fs.WriteByte(0);

                // Write Padding
                fs.Seek(0, SeekOrigin.End);
                byte[] padding = new byte[PaddingLength];
                var random = new Random();
                random.NextBytes(padding);

                fs.Write(padding, 0, padding.Length);

                // Write encoded data
                encodeStream.Seek(0, SeekOrigin.Begin);
                encodeStream.CopyTo(fs);
            }

            encodeStream.Close();
        }

        private static void WriteHeader(CcdHeader header, Stream stream)
        {
            using (var bw = new BinaryWriter(stream, Encoding.ASCII, true))
            {
                bw.Write("FKNL".ToCharArray());
                bw.Write((uint)0x2);
                bw.Write(header.firstFileOffset);
                bw.Write(header.uncompressedFolderSize);
                bw.Write(header.fileDataLen);
                bw.Write(header.fileCount);
                bw.Write((uint)0x1);
                bw.Write(" FIL".ToCharArray());
                bw.Write(header.dirLen);
                bw.Write((uint)0x28);
                bw.Write(header.dirOffset);
                bw.Write(header.dirLen);
                bw.Write(header.fileCount);
                bw.Write((uint)0x0);
            }
        }

        private static void WriteFileHeader(CcdFileInfo fileInfo, Stream stream)
        {
            using (var bw = new BinaryWriter(stream, Encoding.ASCII, true))
            {
                bw.Write(fileInfo.NameOffset);
                bw.Write(fileInfo.Offset);
                bw.Write(fileInfo.Length);
                bw.Write(fileInfo.Length);

                long pos = stream.Position;
                stream.Seek(fileInfo.NameOffset, SeekOrigin.Begin);
                bw.Write(fileInfo.Name.ToCharArray());
                bw.Write((char)0x0);
                stream.Seek(pos, SeekOrigin.Begin);
            }
        }

        private static void AddFile(string filePath, MemoryStream stream)
        {
            using (var fs = new FileStream(filePath, FileMode.Open))
                fs.CopyTo(stream);
        }
    }
}
