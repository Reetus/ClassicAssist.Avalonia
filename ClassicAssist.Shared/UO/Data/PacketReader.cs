using System;
using System.IO;
using System.Text;

namespace ClassicAssist.Shared.UO.Data
{
    public class PacketReader( byte[] data, int size, bool fixedSize )
    {
        private int _position = fixedSize ? 1 : 3;

        public long Index => _position;
        public long Size => size;

        public byte[] GetData()
        {
            byte[] copy = new byte[size];
            Buffer.BlockCopy(data, 0, copy, 0, size);
            return copy;
        }

        public static bool IsSafeChar(int c)
        {
            return c >= 0x20 && c < 0xFFFE;
        }

        public bool ReadBoolean()
        {
            if (_position >= size)
            {
                return false;
            }

            return data[_position++] != 0;
        }

        public byte ReadByte()
        {
            if (_position >= size)
            {
                return 0;
            }

            return data[_position++];
        }

        public byte[] ReadByteArray(int length)
        {
            byte[] bytes = new byte[length];
            int available = Math.Min(length, size - _position);

            if (available > 0)
            {
                Buffer.BlockCopy(data, _position, bytes, 0, available);
            }

            _position += length;
            return bytes;
        }

        public short ReadInt16()
        {
            int b0 = _position < size ? data[_position] : 0;
            int b1 = _position + 1 < size ? data[_position + 1] : 0;
            _position += 2;
            return (short)((b0 << 8) | b1);
        }

        public int ReadInt32()
        {
            int b0 = _position < size ? data[_position] : 0;
            int b1 = _position + 1 < size ? data[_position + 1] : 0;
            int b2 = _position + 2 < size ? data[_position + 2] : 0;
            int b3 = _position + 3 < size ? data[_position + 3] : 0;
            _position += 4;
            return (b0 << 24) | (b1 << 16) | (b2 << 8) | b3;
        }

        public sbyte ReadSByte()
        {
            if (_position >= size)
            {
                return 0;
            }

            return (sbyte)data[_position++];
        }

        public string ReadString()
        {
            StringBuilder sb = new StringBuilder();

            int c;

            while (_position + 1 < size && (c = data[_position++]) != 0)
            {
                sb.Append((char)c);
            }

            return sb.ToString();
        }

        public string ReadString(int fixedLength)
        {
            int available = Math.Min(fixedLength, size - _position);

            string result = available > 0
                ? Encoding.ASCII.GetString(data, _position, available).TrimEnd('\0')
                : string.Empty;

            _position += fixedLength;
            return result;
        }

        public string ReadStringSafe()
        {
            throw new NotImplementedException();
        }

        public string ReadStringSafe(int fixedLength)
        {
            StringBuilder output = new StringBuilder();

            int end = _position + fixedLength;

            for (int i = 0; i < fixedLength && _position < size; i++)
            {
                char c = (char)data[_position++];

                if (c == '\0')
                {
                    _position = end;
                    break;
                }

                if (IsSafeChar(c))
                {
                    output.Append(c);
                }
            }

            return output.ToString();
        }

        public ushort ReadUInt16()
        {
            return (ushort)ReadInt16();
        }

        public uint ReadUInt32()
        {
            return (uint)ReadInt32();
        }

        public string ReadUnicodeString()
        {
            StringBuilder sb = new StringBuilder();

            int c;

            while (_position + 1 < size && (c = (data[_position++] << 8) | data[_position++]) != 0)
            {
                sb.Append((char)c);
            }

            return sb.ToString();
        }

        public string ReadUnicodeString(int fixedLength)
        {
            int available = Math.Min(fixedLength, size - _position);

            string result = available > 0
                ? Encoding.Unicode.GetString(data, _position, available)
                : string.Empty;

            _position += fixedLength;
            return result;
        }

        public string ReadUnicodeStringBE(int fixedLength)
        {
            int byteLength = fixedLength * 2;
            int available = Math.Min(byteLength, size - _position);

            string result = available > 0
                ? Encoding.BigEndianUnicode.GetString(data, _position, available)
                : string.Empty;

            _position += byteLength;
            return result;
        }

        public string ReadUnicodeStringLE()
        {
            StringBuilder sb = new StringBuilder();

            int c;

            while (_position + 1 < size && (c = data[_position++] | (data[_position++] << 8)) != 0)
            {
                sb.Append((char)c);
            }

            return sb.ToString();
        }

        public string ReadUnicodeStringLESafe(int fixedLength)
        {
            throw new NotImplementedException();
        }

        public string ReadUnicodeStringLESafe()
        {
            throw new NotImplementedException();
        }

        public string ReadUnicodeStringSafe()
        {
            throw new NotImplementedException();
        }

        public string ReadUnicodeStringSafe(int fixedLength)
        {
            throw new NotImplementedException();
        }

        public long Seek(int offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin:
                    _position = offset;
                    break;
                case SeekOrigin.Current:
                    _position += offset;
                    break;
                case SeekOrigin.End:
                    _position = size + offset;
                    break;
            }

            return _position;
        }
    }
}
