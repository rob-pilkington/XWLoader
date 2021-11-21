using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Assets.Scripts.LfdReader
{
    /// <summary>
    /// Ship records. TIE fighter.
    /// </summary>
    public class ShipRecord : LfdRecord, ICraft
    {
        /// <summary>
        /// The length. Again. Strange.
        /// </summary>
        public short RecordLength { get; protected set; }

        public byte[] MainHeaderData { get; protected set; }

        /// <summary>
        /// Number of sections?
        /// </summary>
        public byte SectionCount { get; protected set; }

        public byte ShadingSetCount { get; protected set; }

        public byte[] UnknownHeaderData { get; protected set; }

        /// <summary>
        /// I guess this is shading data? TODO: split this out better
        /// </summary>
        public IList<byte[]> ShadingData { get; } = new List<byte[]>();

        /// <summary>
        /// Unknown data
        /// </summary>
        public byte[] UnknownData { get; protected set; }

        public SectionRecord[] Sections { get; protected set; }

        public HardpointRecord[][] SectionHardpoints { get; protected set; }

        protected override void ReadRecordData(BinaryReader inputReader)
        {
            var startPosition = inputReader.BaseStream.Position;

            var readLength = inputReader.ReadInt16();

            // If the reported RecordLength (plus 2) doesn't match the actual data length, it's because the file comes in BigEndian (Mac version)
            var expectedLength = (short)(DataLength - 2);
            RecordLength = expectedLength;
            var isLittleEndian = readLength == expectedLength;

            using var reader = BinaryReaderFactory.Instantiate(isLittleEndian, inputReader.BaseStream, leaveOpen: true);

            MainHeaderData = reader.ReadBytes(30);

            SectionCount = reader.ReadByte();
            ShadingSetCount = reader.ReadByte();

            UnknownHeaderData = reader.ReadBytes(2);

            var shadingOffsets = new List<long>(ShadingSetCount);
            var shadingDistances = new List<int>(ShadingSetCount);
            for (var i = 0; i < ShadingSetCount; i++)
            {
                var currentPosition = reader.BaseStream.Position;
                shadingOffsets.Add(reader.ReadInt16() + currentPosition);
                shadingDistances.Add(reader.ReadInt32());
            }

            var extendedSectionInfoStartPosition = new long[SectionCount];
            var extendedSectionInfoSectionType = new short[SectionCount];
            var extendedSectionInfoUnknown1 = new byte[SectionCount][];
            var extendedSectionInfoHardpointCount = new byte[SectionCount];
            var extendedSectionInfoSectionOffset = new short[SectionCount];
            var extendedSectionInfoHardpointOffset = new short[SectionCount];
            var extendedSectionInfoUnknown2 = new byte[SectionCount][];
            for (var i = 0; i < SectionCount; i++)
            {
                extendedSectionInfoStartPosition[i] = reader.BaseStream.Position;
                extendedSectionInfoSectionType[i] = reader.ReadInt16();
                extendedSectionInfoUnknown1[i] = reader.ReadBytes(41);
                extendedSectionInfoHardpointCount[i] = reader.ReadByte();
                extendedSectionInfoSectionOffset[i] = reader.ReadInt16();
                extendedSectionInfoHardpointOffset[i] = reader.ReadInt16();
                extendedSectionInfoUnknown2[i] = reader.ReadBytes(16);
            }

            SectionHardpoints = new HardpointRecord[SectionCount][];
            for (var i = 0; i < SectionCount; i++)
            {
                var hardpointCount = extendedSectionInfoHardpointCount[i];
                SectionHardpoints[i] = new HardpointRecord[hardpointCount];

                if (hardpointCount == 0)
                    continue;

                var expectedPosition = extendedSectionInfoStartPosition[i] + extendedSectionInfoHardpointOffset[i];
                if (reader.BaseStream.Position != expectedPosition)
                    throw new Exception($"{reader.BaseStream.Position} doesn't match expected position {expectedPosition}."); // didn't happen with TIE95

                for (var j = 0; j < hardpointCount; j++)
                {
                    var hardpoint = new HardpointRecord();
                    hardpoint.Read(reader);
                    SectionHardpoints[i][j] = hardpoint;
                }
            }

            UnknownData = reader.ReadBytes((int)(shadingOffsets.Min() - reader.BaseStream.Position));

            for (var i = 0; i < ShadingSetCount; i++)
            {
                var data = reader.ReadBytes(2);

                ShadingData.Add(data);

                var shadingRecordCount = data[1];

                for (var j = 0; j < shadingRecordCount; j++)
                    ShadingData.Add(reader.ReadBytes(16));
            }

            var bytesRead = reader.BaseStream.Position - startPosition;
            var remainingData = reader.ReadBytes((int)(DataLength - bytesRead));

            Sections = new SectionRecord[SectionCount];

            // For the purpose of analysis, I need to know how many bytes could remain
            for (var i = 0; i < SectionCount; i++)
            {
                var thisPosition = GetPosition(i);
                var next = i + 1;
                var byteCount = (next < SectionCount ? GetPosition(next) : remainingData.Length) - thisPosition;

                using var stream = new MemoryStream(remainingData, thisPosition, byteCount);
                using var subreader = BinaryReaderFactory.Instantiate(isLittleEndian, stream);

                Sections[i] = new SectionRecord();
                Sections[i].Read(subreader, byteCount, true, false);
            }

            int GetPosition(int index) => (int)(extendedSectionInfoStartPosition[index] - bytesRead + extendedSectionInfoSectionOffset[index] - startPosition);
        }
    }
}
