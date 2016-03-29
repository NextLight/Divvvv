namespace Divvvv
{
    // Adobe likes big-endian, but Microsoft does not, so I can't use BinaryReader
    class BoxReader
    {
        private long _pos = 0;
        private readonly byte[] _buffer;

        public BoxReader(byte[] buffer)
        {
            _buffer = buffer;
        }

        public int Position => (int)_pos;

        public byte ReadByte() => _buffer[_pos++];

        public uint ReadUInt32() =>
            (uint) (ReadByte() << 0x18 | ReadByte() << 0x10 | ReadByte() << 0x8 | ReadByte());

        public ulong ReadUInt64() =>
            (ulong) (ReadByte() << 0x38 | ReadByte() << 0x30 | ReadByte() << 0x28 | ReadByte() << 0x20 |
                     ReadByte() << 0x18 | ReadByte() << 0x10 | ReadByte() << 0x08 | ReadByte());

        public void Skip(long n) => _pos += n;

        public void SkipString()
        {
            while (ReadByte() != 0)
            { }
        }

        public void SkipBoxHeader() => Skip(ReadUInt32() != 1 ? 4 : 12);

        public void SkipBox()
        {
            ulong size = ReadUInt32();
            if (size == 0)
            {
                _pos = _buffer.Length;
            }
            else
            {
                Skip(4);
                if (size == 1)
                    size = ReadUInt64();
                Skip((long)size - 8);
            }
        }
    }
}