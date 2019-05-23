using System.Text;

namespace QWCArchiveExtractor
{
    struct CcdHeader
    {
        public uint version;
        public uint firstFileOffset;
        public uint fileDataLen;
        public uint fileCount;
        public uint dirLen;
        public uint dirOffset;

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder(50);
            sb.AppendLine($"Version: {version}");
            sb.AppendLine($"First File Offset: {firstFileOffset:X}");
            sb.AppendLine($"File Data Length: {fileDataLen:X}");
            sb.AppendLine($"File Count: {fileCount}");
            sb.AppendLine($"Directory Length: {dirLen:X}");
            sb.Append($"Directory Offset: {dirOffset:X}");

            return sb.ToString();
        }
    }

    struct CcdFileInfo
    {
        public string Name;
        public uint NameOffset;
        public uint Offset;
        public uint Length;

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder(30);
            sb.AppendLine($"File Name: {Name}");
            sb.AppendLine($"File Name Offset: {NameOffset:X}");
            sb.AppendLine($"File Offset: {Offset}");
            sb.AppendLine($"File Length: {Length}");
            return sb.ToString();
        }
    }
}
