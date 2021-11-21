using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Assets.Scripts.LfdReader
{
    /// <summary>
    /// Complex (?) record
    /// </summary>
    public class CplxRecord : LfdRecord, ICraft
    {
        /// <summary>
        /// The length. Again. Strange.
        /// </summary>
        public short RecordLength { get; protected set; }

        /// <summary>
        /// Number of sections
        /// </summary>
        public byte SectionCount { get; protected set; }

        /// <summary>
        /// Number of "enhanced records" (16 bytes each; shading info?) before the rest of the data
        /// </summary>
        public byte ShadingRecordCount { get; protected set; }

        /// <summary>
        /// I guess this is shading data?
        /// </summary>
        public IList<byte[]> ShadingData { get; } = new List<byte[]>();

        /// <summary>
        /// Unknown data
        /// </summary>
        public byte[] UnknownData { get; protected set; }

        public SortedList<int, short> SectionPositions { get; protected set; }

        public SectionRecord[] Sections { get; protected set; }

        private readonly bool _hasWrongEndianLineRadius;

        /// <summary>
        /// A CPLX record
        /// </summary>
        /// <param name="hasWrongEndianLineRadius">Set to true for Mac/Windows version of X-Wing, otherwise false</param>
        public CplxRecord(bool hasWrongEndianLineRadius)
        {
            // Other formats don't do this. Likely bad data for just X-Wing Mac that was carried over to the Windows version since they
            // are just the same files.
            _hasWrongEndianLineRadius = hasWrongEndianLineRadius;
        }

        protected override void ReadRecordData(BinaryReader inputReader)
        {
            var startPosition = inputReader.BaseStream.Position;

            var readLength = inputReader.ReadInt16();
            
            // If the reported length (plus 2) doesn't match the actual data length, it's because the file comes in BigEndian (CPLX files in XW98, or Mac version)
            var expectedLength = (short)(DataLength - 2);
            RecordLength = expectedLength;
            var isLittleEndian = readLength == expectedLength;

            using var reader = BinaryReaderFactory.Instantiate(isLittleEndian, inputReader.BaseStream, leaveOpen: true);

            SectionCount = reader.ReadByte();
            ShadingRecordCount = reader.ReadByte();

            for (var i = 0; i < ShadingRecordCount; i++)
                ShadingData.Add(reader.ReadBytes(16));

            var bytesRead = reader.BaseStream.Position - startPosition;
            var remainingData = reader.ReadBytes((int)(DataLength - bytesRead));

            SectionPositions = new SortedList<int, short>();
            for (var i = 0; i < SectionCount; i++)
            {
                var bytes = (new byte[2] { remainingData[i * 2], remainingData[i * 2 + 1] }).InEndianOrder(isLittleEndian);
                SectionPositions.Add(i, (short)(BitConverter.ToInt16(bytes, 0) + i * 2));
            }

            Sections = new SectionRecord[SectionCount];

            // For the purpose of analysis, I need to know how many bytes could remain
            var sectionPositions = SectionPositions.Values.OrderBy(x => x).ToList();
            for (var i = 0; i < SectionCount; i++)
            {
                var thisPosition = sectionPositions[i];
                var next = i + 1;
                var byteCount = (next < SectionCount ? sectionPositions[next] : remainingData.Length) - thisPosition;

                using var stream = new MemoryStream(remainingData, thisPosition, byteCount);
                using var subreader = BinaryReaderFactory.Instantiate(isLittleEndian, stream);

                Sections[i] = new SectionRecord();
                Sections[i].Read(subreader, byteCount, true, _hasWrongEndianLineRadius);
            }

            UnknownData = new byte[0]; // I thought I had something here before, but the previous code was hardcoded to read 0 bytes
        }
    }
}
