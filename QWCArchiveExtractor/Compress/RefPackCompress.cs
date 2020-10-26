using System;
using System.Collections.Generic;
using System.IO;

namespace QWCArchiveExtractor
{
	class RefPackCompress : Stream
    {

        #region readonly
        const int WINDOW_SIZE = 0x4000; // Wider window-size crashes QWC
        const int HASH_LEN = 0x4000;
        const int LINK_ENTRY_MAX = 0x40;
		const int WINBUF_LEN = WINDOW_SIZE*4;

		const int TWO_BYTE_LEN = 0xA;
		const int TWO_BYTE_DIST = 0x400;

		const int THREE_BYTE_LEN = 0x43;
		const int THREE_BYTE_DIST = 0x4000;

		const int FOUR_BYTE_LEN = 0x404;
		#endregion

		Dictionary<uint, LinkedList<int>> _linkedHashTable;
		byte[] _slidingWindow;

        int _srcPos = 0;
        int _srcEndPos = 0;
		int _bufEndPos = 0;

		readonly Stream _stream;

		public RefPackCompress(Stream stream) : base()
        {
			_stream = stream;
			if (!_stream.CanWrite) throw new ArgumentException();
			_stream = stream;

			_slidingWindow = new byte[WINBUF_LEN];
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
# endregion

		public override void Write(byte[] buffer, int offset, int count)
        {
			_srcPos = offset;
			_srcEndPos = offset+count;
			WriteHeader();
			offset += ReadAhead(buffer, offset, 2*WINDOW_SIZE);
			
			while(_srcPos < _srcEndPos)
            {
				CompressionSingleStep();
				if(_srcPos + WINDOW_SIZE >= _bufEndPos && _bufEndPos < _srcEndPos)
                {
					offset += ReadAhead(buffer, offset, WINDOW_SIZE);
                }
            }
        }

		
		int ReadAhead(byte[] buffer, int offset, int length)
		{
			if (_bufEndPos + length > _srcEndPos)
            {
				length = _srcEndPos - _bufEndPos;
            }
			
			int idx = _bufEndPos % WINBUF_LEN;
			Array.Copy(buffer, offset, _slidingWindow, idx, length);
			_bufEndPos += length;

			return length;
		}

		void AddSubString(int pos)
        {
			uint hash = GetHash(pos);
			LinkedList<int> list;
			if(!_linkedHashTable.TryGetValue(hash, out list))
            {
				list = (_linkedHashTable[hash] = new LinkedList<int>());
            }

			if(list.Count >= LINK_ENTRY_MAX)
            {
				RemoveSubstring(list.First.Value);
            }
			list.AddLast(new LinkedListNode<int>(pos));
        }

		void RemoveSubstring(int pos)
        {
			uint hash = GetHash(pos);
			LinkedList<int> list;
			if (!_linkedHashTable.TryGetValue(hash, out list) || list.Count == 0)
			{
				return;
			}

			list.RemoveFirst();
		}

		int MatchLength(int pos1, int pos2, int minMatch)
		{
			int score = minMatch;
			int maxScore = Math.Min(_bufEndPos - pos2 - 1, WINDOW_SIZE);
			
			while (score < maxScore && 
				_slidingWindow[(pos1 + score) % WINBUF_LEN] == _slidingWindow[(pos2 + score) % WINBUF_LEN])
            {
				++score;
            }

			return score;
		}

		Tuple<int,int> FindBestMatch(int pos)
        {
			int sameBytes = 2;
            int bestScore = 0;
			int bestPos = -1;

			LinkedList<int> list;
			if (_linkedHashTable.TryGetValue(GetHash(pos), out list) && list.Count != 0)
			{
				for (var node = list.First; node != null; node = node.Next)
				{
                    int score = MatchLength(node.Value, pos, sameBytes);
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

		void WrappedWrite(int offset, int len)
        {
			 for(int i = 0; i < len; ++i)
            {
				_stream.WriteByte(_slidingWindow[(offset + i) % WINBUF_LEN]);
            }
        }

		void CombinedCopy(int srcBegin, int literalDataLen, int refDataStart, int refDataLen)
        {
			while (literalDataLen > 3)
            {
				int numBytes = (int)Math.Min(112, ((uint)literalDataLen & ~3));
				byte command = (byte)(0xE0 + (numBytes >> 2) - 0x01);
				_stream.WriteByte(command);

				WrappedWrite(srcBegin, numBytes);
				srcBegin += numBytes;
				literalDataLen -= numBytes;
				// ++countLiteral;
			}

			int refDataOffset = srcBegin + literalDataLen - refDataStart;
			while (refDataLen > 0 || literalDataLen > 0)
			{
				if (refDataOffset > THREE_BYTE_DIST || refDataLen > THREE_BYTE_LEN)
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

					refDataLen -= numBytes;
					// ++countVLong;
				}
				else if (refDataOffset > TWO_BYTE_DIST || refDataLen > TWO_BYTE_LEN)
				{
					_stream.WriteByte((byte)(0x80 + (refDataLen - 4)));
					_stream.WriteByte((byte)((literalDataLen << 6) + ((refDataOffset - 1) >> 8)));
					_stream.WriteByte((byte)((refDataOffset - 1) & 0xFF));

					refDataLen = 0;
					//++countLong;
				}
				else
				{
					// short copy
					_stream.WriteByte((byte)(((refDataOffset - 1 >> 8) << 5) + ((refDataLen - 3) << 2) + literalDataLen));
					_stream.WriteByte((byte)(refDataOffset - 1 & 0xFF));

					refDataLen = 0;
					//++countShort;
				}

				WrappedWrite(srcBegin, literalDataLen);
				srcBegin += literalDataLen;
				literalDataLen = 0;
			}	
        }
		
		void CopyEnd(int srcBegin, int srcCopyNum)
        {
			while (srcCopyNum > 3)
			{
				int numBytes = Math.Min(112, (srcCopyNum & ~3));
				byte command = (byte)(0xE0 + (numBytes >> 2) - 0x01);
				_stream.WriteByte(command);
				_stream.Write(_slidingWindow, srcBegin, numBytes);
				srcBegin += numBytes;
				srcCopyNum -= numBytes;
				// ++countLiteral;
			}

			_stream.WriteByte((byte)(0xfc + srcCopyNum)); /* end of stream command + 0..3 literal */
			if (srcCopyNum > 0)
			{
				WrappedWrite(srcBegin, srcCopyNum);
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
			while(!IsScoreGood(score, pos-dstCopyStart))
            {
				var bestMatch = FindBestMatch(pos);
				score = bestMatch.Item1;
				dstCopyStart = bestMatch.Item2;

				if(pos >= WINDOW_SIZE)
                {
					RemoveSubstring(pos - WINDOW_SIZE);
                }
				AddSubString(pos);
				++pos;

				if(pos >= _srcPos + 0x70)
                {
					CombinedCopy(_srcPos, pos - _srcPos, _srcPos, 0);
					_srcPos = pos;
					return;
                }

				if(pos < _srcEndPos)
                {
					continue;
                }

				CopyEnd(srcCopyStart, pos - srcCopyStart);
				_srcPos = pos;
				return;
            }
			// Found a string worth copying
			CombinedCopy(srcCopyStart, pos - 1 - srcCopyStart, dstCopyStart, score);
			for(; score > 1; --score)
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

		uint GetHash(int pos)
		{

			uint crc = crcTab[_slidingWindow[pos% WINBUF_LEN]];
			crc = crcTab[(byte)(crc ^ _slidingWindow[(pos + 1) % WINBUF_LEN])] ^ (crc >> 8);
			crc = crcTab[(byte)(crc ^ _slidingWindow[(pos + 2) % WINBUF_LEN])] ^ (crc >> 8);

			return crc;
		}

		static readonly uint[] crcTab = new uint[256]
		{
			0x0000, 0xc0c1, 0xc181, 0x0140, 0xc301, 0x03c0, 0x0280, 0xc241,
			0xc601, 0x06c0, 0x0780, 0xc741, 0x0500, 0xc5c1, 0xc481, 0x0440,
			0xcc01, 0x0cc0, 0x0d80, 0xcd41, 0x0f00, 0xcfc1, 0xce81, 0x0e40,
			0x0a00, 0xcac1, 0xcb81, 0x0b40, 0xc901, 0x09c0, 0x0880, 0xc841,
			0xd801, 0x18c0, 0x1980, 0xd941, 0x1b00, 0xdbc1, 0xda81, 0x1a40,
			0x1e00, 0xdec1, 0xdf81, 0x1f40, 0xdd01, 0x1dc0, 0x1c80, 0xdc41,
			0x1400, 0xd4c1, 0xd581, 0x1540, 0xd701, 0x17c0, 0x1680, 0xd641,
			0xd201, 0x12c0, 0x1380, 0xd341, 0x1100, 0xd1c1, 0xd081, 0x1040,
			0xf001, 0x30c0, 0x3180, 0xf141, 0x3300, 0xf3c1, 0xf281, 0x3240,
			0x3600, 0xf6c1, 0xf781, 0x3740, 0xf501, 0x35c0, 0x3480, 0xf441,
			0x3c00, 0xfcc1, 0xfd81, 0x3d40, 0xff01, 0x3fc0, 0x3e80, 0xfe41,
			0xfa01, 0x3ac0, 0x3b80, 0xfb41, 0x3900, 0xf9c1, 0xf881, 0x3840,
			0x2800, 0xe8c1, 0xe981, 0x2940, 0xeb01, 0x2bc0, 0x2a80, 0xea41,
			0xee01, 0x2ec0, 0x2f80, 0xef41, 0x2d00, 0xedc1, 0xec81, 0x2c40,
			0xe401, 0x24c0, 0x2580, 0xe541, 0x2700, 0xe7c1, 0xe681, 0x2640,
			0x2200, 0xe2c1, 0xe381, 0x2340, 0xe101, 0x21c0, 0x2080, 0xe041,
			0xa001, 0x60c0, 0x6180, 0xa141, 0x6300, 0xa3c1, 0xa281, 0x6240,
			0x6600, 0xa6c1, 0xa781, 0x6740, 0xa501, 0x65c0, 0x6480, 0xa441,
			0x6c00, 0xacc1, 0xad81, 0x6d40, 0xaf01, 0x6fc0, 0x6e80, 0xae41,
			0xaa01, 0x6ac0, 0x6b80, 0xab41, 0x6900, 0xa9c1, 0xa881, 0x6840,
			0x7800, 0xb8c1, 0xb981, 0x7940, 0xbb01, 0x7bc0, 0x7a80, 0xba41,
			0xbe01, 0x7ec0, 0x7f80, 0xbf41, 0x7d00, 0xbdc1, 0xbc81, 0x7c40,
			0xb401, 0x74c0, 0x7580, 0xb541, 0x7700, 0xb7c1, 0xb681, 0x7640,
			0x7200, 0xb2c1, 0xb381, 0x7340, 0xb101, 0x71c0, 0x7080, 0xb041,
			0x5000, 0x90c1, 0x9181, 0x5140, 0x9301, 0x53c0, 0x5280, 0x9241,
			0x9601, 0x56c0, 0x5780, 0x9741, 0x5500, 0x95c1, 0x9481, 0x5440,
			0x9c01, 0x5cc0, 0x5d80, 0x9d41, 0x5f00, 0x9fc1, 0x9e81, 0x5e40,
			0x5a00, 0x9ac1, 0x9b81, 0x5b40, 0x9901, 0x59c0, 0x5880, 0x9841,
			0x8801, 0x48c0, 0x4980, 0x8941, 0x4b00, 0x8bc1, 0x8a81, 0x4a40,
			0x4e00, 0x8ec1, 0x8f81, 0x4f40, 0x8d01, 0x4dc0, 0x4c80, 0x8c41,
			0x4400, 0x84c1, 0x8581, 0x4540, 0x8701, 0x47c0, 0x4680, 0x8641,
			0x8201, 0x42c0, 0x4380, 0x8341, 0x4100, 0x81c1, 0x8081, 0x4040,
		};


	}
}
