using System.IO;

namespace Assets.Scripts.LfdReader
{
    public abstract class LfdRecord
    {
        public string DescriptiveName => string.Format("{0} ({1}, {2} bytes", RecordName, RecordType, DataLength);

        public string RecordType => Header.Type;

        public string RecordName => Header.Name;

        public int DataLength => Header.Length;

        protected LfdHeader Header { get; private set; }

        protected void SetHeader(string type, string name, int length)
        {
            Header = new LfdHeader
            {
                Type = type,
                Name = name,
                Length = length
            };
        }

        public void ReadLfdRecord(BinaryReader reader)
        {
            Header = ReadHeader(reader);
            ReadRecordData(reader);
        }

        public void Read(BinaryReader reader, string type, string name, int length)
        {
            SetHeader(type, name, length);

            ReadRecordData(reader);
        }

        public void Read(Stream stream, string type, string name)
        {
            SetHeader(type, name, (int)stream.Length);

            using var reader = new BinaryReader(stream);
            ReadRecordData(reader);
        }

        protected abstract void ReadRecordData(BinaryReader reader);

        protected LfdHeader ReadHeader(BinaryReader reader)
        {
            var recordType = reader.ReadBytes(4);
            var recordName = reader.ReadBytes(8);
            var dataLength = reader.ReadInt32();

            return new LfdHeader
            {
                Type = recordType.ReadString(),
                Name = recordName.ReadString(),
                Length = dataLength
            };
        }
    }
}
