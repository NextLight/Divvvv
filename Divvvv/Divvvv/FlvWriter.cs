using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Divvvv
{
    class FlvWriter
    {
        private FileStream _fs = null;
        private readonly string _fileName;
        public FlvWriter(string fileName)
        {
            _fileName = fileName;
            var fi = new FileInfo(fileName);
            if (fi.Exists && fi.Length > 25)
            {
                _fs = new FileStream(fileName, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                _fs.Seek(-3, SeekOrigin.End);
                int lastTagSize = ReadInt24();
                _fs.Seek(-4 - lastTagSize, SeekOrigin.End);
                if (ReadByte() == 0x12) // if the last fragment is metadata do not resume
                {
                    _fs.Seek(0, SeekOrigin.Begin);
                }
                else
                {
                    _fs.Seek(13, SeekOrigin.Begin);
                    if (ReadByte() != 0x12) // bad flv file (?)
                    {
                        _fs.Seek(0, SeekOrigin.Begin);
                    }
                    else
                    {
                        Resuming = true;
                        // look for duration in metadata
                        int dataSize = ReadInt24();
                        _fs.Seek(4, SeekOrigin.Current);
                        var duration = new byte[] { 0x64, 0x75, 0x72, 0x61, 0x74, 0x69, 0x6f, 0x6e };
                        for (int _ = 0; _ < dataSize; _++)
                        {
                            int i;
                            for (i = 0; i < duration.Length; i++)
                                if (ReadByte() != duration[i])
                                    break;
                            if (i == duration.Length) // found it
                            {
                                _fs.Seek(1, SeekOrigin.Current);
                                Duration = (int)(ReadDouble() * 1000);
                                break;
                            }
                        }
                        _fs.Seek(0, SeekOrigin.End);
                    }
                }
            }
        }


        public bool Resuming { get; }

        public int Duration { get; }


        public async Task Create(byte[] metadata)
        {
            if (Resuming) // heh
                return;
            _fs?.Close();
            _fs = new FileStream(_fileName, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
            _fs.Seek(0, SeekOrigin.Begin);
            var flvHeader = new byte[] { 0x46, 0x4c, 0x56, 1, 5, 0, 0, 0, 9 };
            await WriteAsync(flvHeader);
            Zero(4);
            WriteByte(0x12);
            WriteInt24(metadata.Length);
            Zero(7);
            await WriteAsync(metadata);
            WriteInt32(metadata.Length + 11);
        }

        public async Task<bool> WriteFragmentAsync(byte[] frag)
        {
            var br = new BoxReader(frag);
            while (br.GetBoxType() != "mdat")
                br.SkipBox();
            br.SkipBoxHeader();

            br.Skip(4);
            int tsFrag = br.ReadByte() << 0x10 | br.ReadByte() << 0x8 | br.ReadByte() | br.ReadByte() << 0x18;
            br.Skip(-8);
            if (tsFrag < GetLastTagTimestamp()) // don't write aleady written fragment.
                return false;
            _fs.Seek(0, SeekOrigin.End);
            await _fs.WriteAsync(frag, br.Position, frag.Length - br.Position);
            return true;
        }

        public void Close() => _fs.Dispose();
        

        private int ReadByte() => _fs.ReadByte();

        private void WriteByte(byte b) => _fs.WriteByte(b);

        private async Task WriteAsync(byte[] array) => await _fs.WriteAsync(array, 0, array.Length);

        private int ReadInt24() => ReadByte() << 0x10 | ReadByte() << 0x8 | ReadByte();

        public double ReadDouble()
        {
            var array = new byte[8];
            _fs.Read(array, 0, 8);
            return BitConverter.ToDouble(array.Reverse().ToArray(), 0); // Again, numbers are stored in big-endian so i need to reverse
        }

        public int GetLastTagTimestamp()
        {
            _fs.Seek(-3, SeekOrigin.End);
            _fs.Seek(-ReadInt24(), SeekOrigin.Current);
            return ReadInt24() | ReadByte() << 0x18; // the 4th byte is the most significant
        } 

        private void WriteInt24(int i)
        {
            WriteByte((byte)(i >> 0x10 & 0xff));
            WriteByte((byte)(i >> 0x8 & 0xff));
            WriteByte((byte)(i & 0xff));
        }

        private void WriteInt32(int i)
        {
            WriteByte((byte)(i >> 0x18 & 0xff));
            WriteByte((byte)(i >> 0x10 & 0xff));
            WriteByte((byte)(i >> 0x8 & 0xff));
            WriteByte((byte)(i & 0xff));
        }

        private void Zero(int n)
        {
            for (int i = 0; i < n; i++)
                WriteByte(0);
        }
    }
}
