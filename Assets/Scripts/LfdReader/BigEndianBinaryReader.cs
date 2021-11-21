using System;
using System.IO;

namespace Assets.Scripts.LfdReader
{
    public sealed class BigEndianBinaryReader : BinaryReader
    {
        public BigEndianBinaryReader(Stream stream) : base(stream) { }
        public BigEndianBinaryReader(Stream stream, System.Text.Encoding encoding, bool leaveOpen) : base(stream, encoding, leaveOpen) { }

        public override short ReadInt16() => BitConverter.ToInt16(ReadBytes(2).InEndianOrder(false), 0);

        public override int ReadInt32() => BitConverter.ToInt32(ReadBytes(4).InEndianOrder(false), 0);

        public override long ReadInt64() => BitConverter.ToInt64(ReadBytes(8).InEndianOrder(false), 0);

        public override ushort ReadUInt16() => BitConverter.ToUInt16(ReadBytes(2).InEndianOrder(false), 0);

        public override uint ReadUInt32() => BitConverter.ToUInt32(ReadBytes(4).InEndianOrder(false), 0);

        public override ulong ReadUInt64() => BitConverter.ToUInt64(ReadBytes(8).InEndianOrder(false), 0);
    }
}
