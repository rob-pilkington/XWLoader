using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Assets.Scripts.LfdReader
{
    /// <summary>
    /// Craft record
    /// </summary>
    public class CrftRecord : LfdRecord, ICraft
    {
        /// <summary>
        /// The length of the record.
        /// </summary>
        public short RecordLength { get; protected set; }

        /// <summary>
        /// Number of sections
        /// </summary>
        public byte SectionCount { get; protected set; }

        /// <summary>
        /// Unknown record
        /// </summary>
        public byte UnknownByte { get; protected set; }

        /// <summary>
        /// Unknown data
        /// </summary>
        public byte[] UnknownData { get; protected set; }

        public SortedList<int, short> SectionPositions { get; protected set; }

        public SectionRecord[] Sections { get; protected set; }

        protected override void ReadRecordData(BinaryReader reader)
        {
            var startPosition = reader.BaseStream.Position;

            RecordLength = reader.ReadInt16();
            SectionCount = reader.ReadByte();
            UnknownByte = reader.ReadByte();

            var bytesRead = reader.BaseStream.Position - startPosition;
            var remainingData = reader.ReadBytes((int)(DataLength - bytesRead));

            SectionPositions = new SortedList<int, short>();
            for (var i = 0; i < SectionCount; i++)
                SectionPositions.Add(i, (short)(BitConverter.ToInt16(remainingData, i * 2) + i * 2));

            Sections = new SectionRecord[SectionCount];

            // For the purpose of analysis, I need to know how many bytes could remain
            var sectionPositions = SectionPositions.Values.OrderBy(x => x).ToList();
            for (var i = 0; i < SectionCount; i++)
            {
                var thisPosition = sectionPositions[i];
                var next = i + 1;
                var byteCount = (next < SectionCount ? sectionPositions[next] : remainingData.Length) - thisPosition;

                using var stream = new MemoryStream(remainingData, thisPosition, byteCount);
                using var subreader = new BinaryReader(stream);

                Sections[i] = new SectionRecord();
                Sections[i].Read(subreader, byteCount, false, false);
            }

            UnknownData = new byte[0]; // I thought I had something here before, but the previous code was hardcoded to read 0 bytes
        }
    }
}
