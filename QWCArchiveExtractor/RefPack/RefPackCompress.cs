using System;
using System.Collections.Generic;
using System.IO;

namespace QWCArchiveExtractor
{
    class RefPackCompress : Stream
    {

        #region readonly
        const int WINDOW_SIZE = 0x4000;

        const int HASH_LEN = 0x4000;
        const int LINK_ENTRY_MAX = 0x40;

        const int TWO_BYTE_LEN = 0xA;
        const int TWO_BYTE_DIST = 0x400;

        const int THREE_BYTE_LEN = 0x43;
        const int THREE_BYTE_DIST = 0x4000;

        const int FOUR_BYTE_LEN = 0x404;
        #endregion

        Dictionary<uint, LinkedList<int>> _linkedHashTable;
        RefPackSlidingWindow _slidingWindow;

        int _srcPos = 0;
        int _srcEndPos = 0;

        readonly Stream _stream;

        public RefPackCompress(Stream stream) : base()
        {
            _stream = stream;
            if (!_stream.CanWrite) throw new ArgumentException();
            _stream = stream;

            _slidingWindow = new RefPackSlidingWindow();
            _linkedHashTable = new Dictionary<uint, LinkedList<int>>(HASH_LEN);

        }

        protected override void Dispose(bool disposing)
        {
            //if(true)
            //_stream?.Dispose();
        }

        #region IfImplements
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;

        public override long Length => throw new NotImplementedException();

        public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }
        #endregion

        public override void Write(byte[] buffer, int offset, int count)
        {
            _srcPos = offset;
            _srcEndPos = offset + count;
            WriteHeader();

            offset += _slidingWindow.ReadAhead(buffer, offset, 2 * WINDOW_SIZE, _srcEndPos);

            while (_srcPos < _srcEndPos)
            {
                CompressionSingleStep();

                if (_srcPos + WINDOW_SIZE > _slidingWindow.CurrentPos && _slidingWindow.CurrentPos < _srcEndPos)
                {
                    offset += _slidingWindow.ReadAhead(buffer, offset, WINDOW_SIZE, _srcEndPos);
                }
            }
        }

        void AddSubString(int pos)
        {
            uint hash = _slidingWindow.GetHash(pos);
            LinkedList<int> list;
            if (!_linkedHashTable.TryGetValue(hash, out list))
            {
                list = (_linkedHashTable[hash] = new LinkedList<int>());
            }

            if (list.Count >= LINK_ENTRY_MAX)
            {
                RemoveSubstring(list.First.Value);
            }
            list.AddLast(new LinkedListNode<int>(pos));
        }

        void RemoveSubstring(int pos)
        {
            uint hash = _slidingWindow.GetHash(pos);
            LinkedList<int> list;
            if (!_linkedHashTable.TryGetValue(hash, out list) || list.Count == 0)
            {
                return;
            }

            list.RemoveFirst();
        }

        Tuple<int, int> FindBestMatch(int pos)
        {
            int sameBytes = 2;
            int bestScore = 0;
            int bestPos = -1;

            LinkedList<int> list;
            if (_linkedHashTable.TryGetValue(_slidingWindow.GetHash(pos), out list) && list.Count != 0)
            {
                for (var node = list.First; node != null; node = node.Next)
                {
                    int score = _slidingWindow.MatchLength(node.Value, pos, sameBytes);

                    if (score >= bestScore)
                    {
                        bestScore = score;
                        bestPos = node.Value;
                    }
                }
            }

            return new Tuple<int, int>(bestScore, bestPos);
        }

        bool IsScoreGood(int score, int offset)
        {
            if (score < 3) return false;
            if (score < 4) return (offset <= 0x400);
            if (score < 5) return (offset <= 0x4000);

            return true;
        }

        void CombinedCopy(int srcBegin, int literalDataLen, int refDataStart, int refDataLen)
        {
            while (literalDataLen > 3)
            {
                int numBytes = (int)Math.Min(112, ((uint)literalDataLen & ~3));
                byte command = (byte)(0xE0 + (numBytes >> 2) - 0x01);
                _stream.WriteByte(command);

                _slidingWindow.WriteTo(_stream, srcBegin, numBytes);

                srcBegin += numBytes;
                literalDataLen -= numBytes;
            }

            int refDataOffset = srcBegin + literalDataLen - refDataStart;
            while (refDataLen > 0 || literalDataLen > 0)
            {
                if (refDataOffset > THREE_BYTE_DIST || refDataLen > THREE_BYTE_LEN)
                {
                    refDataLen -= Write4ByteCommand(refDataOffset, refDataLen, literalDataLen);
                }
                else if (refDataOffset > TWO_BYTE_DIST || refDataLen > TWO_BYTE_LEN)
                {
                    refDataLen -= Write3ByteCommand(refDataOffset, refDataLen, literalDataLen);
                }
                else
                {
                    refDataLen -= Write2ByteCommand(refDataOffset, refDataLen, literalDataLen);
                }

                _slidingWindow.WriteTo(_stream, srcBegin, literalDataLen);

                srcBegin += literalDataLen;
                literalDataLen = 0;
            }
        }

        int Write2ByteCommand(int refDataOffset, int refDataLen, int literalDataLen)
        {
            _stream.WriteByte((byte)(((refDataOffset - 1 >> 8) << 5) + ((refDataLen - 3) << 2) + literalDataLen));
            _stream.WriteByte((byte)(refDataOffset - 1 & 0xFF));

            return refDataLen;
        }

        int Write3ByteCommand(int refDataOffset, int refDataLen, int literalDataLen)
        {
            _stream.WriteByte((byte)(0x80 + (refDataLen - 4)));
            _stream.WriteByte((byte)((literalDataLen << 6) + ((refDataOffset - 1) >> 8)));
            _stream.WriteByte((byte)((refDataOffset - 1) & 0xFF));

            return refDataLen;
        }

        int Write4ByteCommand(int refDataOffset, int refDataLen, int literalDataLen)
        {
            /* use large copy */
            int numBytes = refDataLen;
            // Limit is 0x404
            if (numBytes > FOUR_BYTE_LEN)
            {
                numBytes = FOUR_BYTE_LEN;
                // Leave at least 5 bytes
                if (refDataLen - numBytes < 5)
                    numBytes = refDataLen - 5;
            }

            _stream.WriteByte((byte)(0xC0 + ((refDataOffset - 1 >> 16) << 4) + (((numBytes - 5) >> 8) << 2) + literalDataLen));
            _stream.WriteByte((byte)((refDataOffset - 1 >> 8) & 0xFF));
            _stream.WriteByte((byte)(refDataOffset - 1 & 0xFF));
            _stream.WriteByte((byte)(numBytes - 5));

            return numBytes;
        }

        void CopyEnd(int srcBegin, int srcCopyNum)
        {
            while (srcCopyNum > 3)
            {
                int numBytes = Math.Min(112, (srcCopyNum & ~3));
                byte command = (byte)(0xE0 + (numBytes >> 2) - 0x01);
                _stream.WriteByte(command);
                _slidingWindow.WriteTo(_stream, srcBegin, numBytes);

                srcBegin += numBytes;
                srcCopyNum -= numBytes;
            }

            _stream.WriteByte((byte)(0xfc + srcCopyNum)); /* end of stream command + 0..3 literal */
            if (srcCopyNum > 0)
            {
                _slidingWindow.WriteTo(_stream, srcBegin, srcCopyNum);
            }
            //srcBegin += srcCopyNum;
            //srcCopyNum = 0;
        }

        void CompressionSingleStep()
        {
            int pos = _srcPos;
            int srcCopyStart = pos;
            int score = 0;
            int dstCopyStart = -1;
            while (!IsScoreGood(score, pos - dstCopyStart))
            {
                var bestMatch = FindBestMatch(pos);
                score = bestMatch.Item1;
                dstCopyStart = bestMatch.Item2;

                if (pos >= WINDOW_SIZE)
                {
                    RemoveSubstring(pos - WINDOW_SIZE);
                }
                AddSubString(pos);
                ++pos;

                if (pos >= _srcPos + 0x70)
                {
                    CombinedCopy(_srcPos, pos - _srcPos, _srcPos, 0);
                    _srcPos = pos;
                    return;
                }

                if (pos < _srcEndPos)
                {
                    continue;
                }

                CopyEnd(srcCopyStart, pos - srcCopyStart);
                _srcPos = pos;
                return;
            }
            // Found a string worth copying
            CombinedCopy(srcCopyStart, pos - 1 - srcCopyStart, dstCopyStart, score);
            for (; score > 1; --score)
            {
                if (pos >= WINDOW_SIZE) RemoveSubstring(pos - WINDOW_SIZE);
                AddSubString(pos);
                ++pos;
            }
            _srcPos = pos;
        }

        void WriteHeader()
        {
            _stream.WriteByte(0x15);
            _stream.WriteByte(0xFB);

            byte[] size = new byte[] { (byte)(_srcEndPos >> 24), (byte)(_srcEndPos >> 16), (byte)(_srcEndPos >> 8), (byte)(_srcEndPos >> 0) };
            _stream.Write(size, 0, size.Length);
        }
    }

}
