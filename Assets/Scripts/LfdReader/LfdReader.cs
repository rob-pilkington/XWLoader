using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Assets.Scripts.LfdReader
{
    public class LfdReader
    {
        public IDictionary<string, LfdRecord> Records { get; private set; } = new Dictionary<string, LfdRecord>();

        public void Read(FileStream fs, bool hasWrongEndianLineRadius)
        {
            using (var reader = new BinaryReader(fs))
            {
                var map = new RmapRecord();

                map.ReadLfdRecord(reader);

                foreach (var entry in map.Entries)
                {
                    LfdRecord record;
                    switch (entry.Type)
                    {
                        case "CRFT":
                            record = new CrftRecord();
                            break;

                        case "CPLX":
                            record = new CplxRecord(hasWrongEndianLineRadius);
                            break;

                        case "SHIP":
                            record = new ShipRecord();
                            break;

                        case "BTMP":
                        case "BMAP":
                        case "XACT":
                            record = new BmapRecord();
                            break;

                        default:
                            throw new NotSupportedException("Unknown record type: " + entry.Type);
                    }

                    Debug.Log($"Reading {entry.Type} {entry.Name}");
                    record.ReadLfdRecord(reader);

                    try
                    {
                        Records.Add(entry.Name, record);
                    }
                    catch (ArgumentException)
                    {
                        // Yeah, just ignore for now
                    }
                }
            }
        }
    }

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

            using (var reader = new BinaryReader(stream))
            {
                ReadRecordData(reader);
            }
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

    /// <summary>
    /// Record Map
    /// </summary>
    public class RmapRecord : LfdRecord
    {
        public List<LfdHeader> Entries { get; private set; } = new List<LfdHeader>();

        protected override void ReadRecordData(BinaryReader reader)
        {
            var start = reader.BaseStream.Position;

            while (reader.BaseStream.Position - start < DataLength)
                Entries.Add(ReadHeader(reader));
        }
    }

    public class LfdHeader
    {
        public string DescriptiveName => string.Format("{0} ({1}, {2} bytes", Name, Type, Length);

        public string Type { get; set; } // 4
        public string Name { get; set; } // 8
        public int Length { get; set; } // 4
    }

    public interface ICraft
    {
        string RecordType { get; }
        string RecordName { get; }
        SectionRecord[] Sections { get; }
    }

    /// <summary>
    /// Craft record
    /// </summary>
    public class CrftRecord : LfdRecord, ICraft
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
                using (var stream = new MemoryStream(remainingData, thisPosition, byteCount))
                using (var subreader = new BinaryReader(stream))
                {
                    Sections[i] = new SectionRecord();
                    Sections[i].Read(subreader, byteCount, false, false);
                }
            }

            UnknownData = new byte[0]; // I thought I had something here before, but the previous code was hardcoded to read 0 bytes
        }
    }

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

            using (var reader = BinaryReaderFactory.Instantiate(isLittleEndian, inputReader.BaseStream, leaveOpen: true))
            {
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
                    using (var stream = new MemoryStream(remainingData, thisPosition, byteCount))
                    using (var subreader = BinaryReaderFactory.Instantiate(isLittleEndian, stream))
                    {
                        Sections[i] = new SectionRecord();
                        Sections[i].Read(subreader, byteCount, true, _hasWrongEndianLineRadius);
                    }
                }

                UnknownData = new byte[0]; // I thought I had something here before, but the previous code was hardcoded to read 0 bytes
            }
        }
    }

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

            using (var reader = BinaryReaderFactory.Instantiate(isLittleEndian, inputReader.BaseStream, leaveOpen: true))
            {
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
                    using (var stream = new MemoryStream(remainingData, thisPosition, byteCount))
                    using (var subreader = BinaryReaderFactory.Instantiate(isLittleEndian, stream))
                    {
                        Sections[i] = new SectionRecord();
                        Sections[i].Read(subreader, byteCount, true, false);
                    }

                    int GetPosition(int index) => (int)(extendedSectionInfoStartPosition[index] - bytesRead + extendedSectionInfoSectionOffset[index] - startPosition);
                }
            }
        }
    }

    public class SectionRecord
    {
        public SortedList<int, short> LodOffsets { get; protected set; } = new SortedList<int, short>();
        public List<LodRecord> LodRecords { get; protected set; } = new List<LodRecord>();

        public void Read(
            BinaryReader reader,
            int? length,
            bool hasVertexNormals,
            bool hasWrongEndianLineRadius)
        {
            int distance = 0;
            while (distance != int.MaxValue)
            {
                distance = reader.ReadInt32();
                var offset = (short)(reader.ReadInt16() + LodOffsets.Count * 6);

                if (!LodOffsets.ContainsKey(distance))
                    LodOffsets.Add(distance, offset);
            }

            var remainingLength = length.HasValue ? length.Value - LodOffsets.Count * 6 : (int?)null;

            // And now it's possible to have duplicate values. Headsplode.
            for (var i = 0; i < LodOffsets.Keys.Count; i++)
            {
                var key = LodOffsets.Keys[i];
                var next = i + 1;
                var nextKey = next < LodOffsets.Keys.Count ? (int?)LodOffsets.Keys[next] : null;
                var nextOffset = nextKey.HasValue ? LodOffsets[nextKey.Value] - LodOffsets.Count * 6 : remainingLength;

                var lodOffset = LodOffsets[key] - LodOffsets.Count * 6;

                var lodLength = nextOffset.HasValue ? nextOffset - lodOffset : null;

                if (!lodLength.HasValue || lodLength > 0)
                {
                    var lodRecord = new LodRecord();
                    lodRecord.Read(reader, lodLength, hasVertexNormals, hasWrongEndianLineRadius);
                    LodRecords.Add(lodRecord);
                }
                else
                {
                    LodRecords.Add(LodRecords.Last());
                }
            }
        }
    }

    public class LodRecord
    {
        public byte[] UnknownBytes { get; protected set; }
        public byte VertexCount { get; protected set; }
        public byte UnknownByte { get; protected set; }
        public byte PolygonCount { get; protected set; }
        public byte[] Colors { get; protected set; }
        public Vector3 BoundingBox1 { get; protected set; }
        public Vector3 BoundingBox2 { get; protected set; }
        public Vector3[] Vertices { get; protected set; }
        public Vector3[] VertexNormals { get; protected set; }
        public Vector3[] Normals { get; protected set; }
        public short[] LinesOffsets { get; protected set; }
        public PolygonLineRecord[] PolygonLineRecords { get; protected set; }
        public byte[][] UnknownPolygonData { get; protected set; }
        public short MarkedPolygonCount { get; protected set; }
        public List<Tuple<byte, short>> PolygonMarkPositions { get; protected set; }
        public Dictionary<byte, MarkRecord[]> MarkRecords { get; protected set; }

        public byte[] RemainingData { get; protected set; }

        public void Read(
            BinaryReader reader,
            int? length,
            bool hasVertexNormals,
            bool hasWrongEndianLineRadius)
        {
            //Debug.Log($"Reading LOD record, length {length}");

            var startingPosition = (int)reader.BaseStream.Position;

            UnknownBytes = reader.ReadBytes(2);

            var emptyDataLoops = 0;
            while (UnknownBytes[0] == 0)
            {
                // TODO: figure out what's going on here
                UnknownBytes = reader.ReadBytes(2);
                emptyDataLoops++;
            }

            if (emptyDataLoops > 0)
                Debug.Log($"Unexpected empty data at start of LOD record. Looped {emptyDataLoops} times (off by {emptyDataLoops * 2} bytes)");

            VertexCount = reader.ReadByte();
            UnknownByte = reader.ReadByte();
            PolygonCount = reader.ReadByte();

            Colors = reader.ReadBytes(PolygonCount);

            var boundingBoxes = new short[6];
            for (var i = 0; i < 6; i++)
                boundingBoxes[i] = reader.ReadInt16();

            BoundingBox1 = new Vector3(boundingBoxes[0], boundingBoxes[1], boundingBoxes[2]);
            BoundingBox2 = new Vector3(boundingBoxes[3], boundingBoxes[4], boundingBoxes[5]);

            Vertices = new Vector3[VertexCount];

            for (var i = 0; i < VertexCount; i++)
            {
                var isLittleEndian = !(reader is BigEndianBinaryReader);
                var xBytes = reader.ReadBytes(2).InEndianOrder(isLittleEndian);
                var yBytes = reader.ReadBytes(2).InEndianOrder(isLittleEndian);
                var zBytes = reader.ReadBytes(2).InEndianOrder(isLittleEndian);

                float x = IsLookback(xBytes) ? Vertices[i - (xBytes[0] >> 1)].x : BitConverter.ToInt16(xBytes, 0);
                float y = IsLookback(yBytes) ? Vertices[i - (yBytes[0] >> 1)].y : BitConverter.ToInt16(yBytes, 0);
                float z = IsLookback(zBytes) ? Vertices[i - (zBytes[0] >> 1)].z : BitConverter.ToInt16(zBytes, 0);

                Vertices[i] = new Vector3(x, y, z);
            }

            if (hasVertexNormals)
            {
                VertexNormals = new Vector3[VertexCount];
                for (var i = 0; i < VertexCount; i++)
                {
                    var x = reader.ReadInt16();
                    var y = reader.ReadInt16();
                    var z = reader.ReadInt16();
                    VertexNormals[i] = new Vector3(x, y, z);
                }
            }
            else
            {
                VertexNormals = new Vector3[0];
            }

            Normals = new Vector3[PolygonCount];
            LinesOffsets = new short[PolygonCount];
            var lineDataPositions = new int[PolygonCount];
            for (var i = 0; i < PolygonCount; i++)
            {
                var position = (int)reader.BaseStream.Position - startingPosition;

                var x = reader.ReadInt16();
                var y = reader.ReadInt16();
                var z = reader.ReadInt16();

                Normals[i] = new Vector3(x, y, z);

                LinesOffsets[i] = reader.ReadInt16();

                // This technically shouldn't be necessary since I have to know how to read the length anyway for the final item.
                // It's possible that the data comes out of order, but I'm not sure how that would affect anything yet.
                // (maybe for the marks?)
                lineDataPositions[i] = position + LinesOffsets[i];
            }

            PolygonLineRecords = new PolygonLineRecord[PolygonCount];
            for (var i = 0; i < PolygonCount; i++)
            {
                var polygonLineRecord = new PolygonLineRecord();
                polygonLineRecord.Read(reader, Vertices, hasWrongEndianLineRadius);
                PolygonLineRecords[i] = polygonLineRecord;
            }

            if (UnknownBytes[0] != 0x81)
            {
                UnknownPolygonData = new byte[PolygonCount][];
                for (var i = 0; i < PolygonCount; i++)
                    UnknownPolygonData[i] = reader.ReadBytes(3);
            }
            else
            {
                UnknownPolygonData = new byte[0][];
            }

            if (reader.BaseStream.Position == reader.BaseStream.Length)
            {
                // TODO: figure out why this happens
                //Debug.Log("We're done? Why?");
                return;
            }

            MarkedPolygonCount = reader.ReadInt16();
            PolygonMarkPositions = new List<Tuple<byte, short>>();
            for (int i = 0; i < MarkedPolygonCount; i++)
            {
                var polygonNumber = reader.ReadByte();
                var offset = reader.ReadInt16();

                // Calculate the position relative to the start of the marking data.
                PolygonMarkPositions.Add(Tuple.Create(polygonNumber, (short)(offset - ((MarkedPolygonCount - i) * 3))));
            }

            MarkRecords = new Dictionary<byte, MarkRecord[]>();

            var startPosition = (int)reader.BaseStream.Position;
            for (var i = 0; i < MarkedPolygonCount; i++)
            {
                // TODO: find a better way to match the mark position
                var markPosition = PolygonMarkPositions.FirstOrDefault(x => x.Item2 == (int)reader.BaseStream.Position - startPosition);
                if (markPosition == null)
                    throw new Exception($"Position mismatch: could not find mark position in list: {(int)reader.BaseStream.Position - startPosition}.");

                var markCount = reader.ReadByte();

                var markColors = reader.ReadBytes(markCount);

                var markRecords = new MarkRecord[markCount];
                MarkRecords.Add(markPosition.Item1, markRecords);

                for (var j = 0; j < markCount; j++)
                {
                    var markRecord = new MarkRecord();

                    markRecord.Read(reader, markColors[j]);

                    markRecords[j] = markRecord;
                }
            }

            var remainingByteCount = length.HasValue ? length.Value - ((int)reader.BaseStream.Position - startingPosition) : 0;

            if (remainingByteCount < 0)
            {
                Debug.Log($"Somehow we have {remainingByteCount} bytes remaining. Expected length: {length}, Actual length: {reader.BaseStream.Length - startingPosition}");
            }

            RemainingData = remainingByteCount > 0 ? reader.ReadBytes(remainingByteCount) : new byte[0];
        }

        bool IsLookback(byte[] bytes) => bytes[1] == 0x7f;
    }

    public class PolygonLineRecord
    {
        public int VertexCount { get; protected set; }
        public bool TwoSidedFlag { get; protected set; }
        public bool ShadeFlag { get; protected set; }

        // For lines
        public short LineRadius { get; protected set; }

        // For polygons:
        public byte UnknownPolygonByte { get; protected set; }
        public byte[] VertexNumbers { get; protected set; }

        public int[] VertexIndices { get; protected set; }
        public Vector3[] Vertices { get; set; }
        public byte[] UnknownData2 { get; protected set; }

        public void Read(BinaryReader reader, Vector3[] vertices, bool hasWrongEndianLineRadius)
        {
            var firstByte = reader.ReadByte();
            var points = firstByte & 0x0f; // Apparently only the second nibble counts?

            TwoSidedFlag = (firstByte & 0x80) != 0;
            ShadeFlag = (firstByte & 0x40) != 0;

            VertexCount = points;

            byte[] vertexIndexList;

            if (points == 2) // special case (originally 0x82)
            {
                // For some reason, the big endian version of the X-Wing still uses
                // little endian just for this field. This doesn't happen in TIE Fighter,
                // so we have to handle this as special case.
                var isLittleEndian = !(reader is BigEndianBinaryReader);

                if (hasWrongEndianLineRadius)
                    isLittleEndian = !isLittleEndian;

                var lineRadiusBytes = reader.ReadBytes(2).InEndianOrder(isLittleEndian);

                LineRadius = BitConverter.ToInt16(lineRadiusBytes, 0);

                vertexIndexList = reader.ReadBytes(2);

                UnknownData2 = reader.ReadBytes(3);

                // Blank the fields not used for lines
                UnknownPolygonByte = 0;
                VertexNumbers = new byte[0];
            }
            else
            {
                UnknownPolygonByte = reader.ReadByte(); // not sure what this is for polygons

                VertexNumbers = new byte[points]; // not really numbers

                vertexIndexList = new byte[points];
                for (var i = 0; i < points; i++)
                {
                    VertexNumbers[i] = reader.ReadByte();
                    vertexIndexList[i] = reader.ReadByte();
                }

                UnknownData2 = reader.ReadBytes(2); // First item repeated. Should be noted as such.

                // Blank the fields not used for polygons
                LineRadius = 0;
            }

            VertexIndices = new int[points];
            Vertices = new Vector3[points];
            for (var i = 0; i < points; i++)
            {
                VertexIndices[i] = vertexIndexList[i];
                Vertices[i] = vertices[vertexIndexList[i]];
            }
        }
    }

    public class MarkRecord
    {
        public byte MarkColor { get; protected set; }
        public byte MarkType { get; protected set; }
        public byte[] SpecialData { get; protected set; }
        public byte[] Data { get; protected set; }

        public void Read(BinaryReader reader, byte color)
        {
            MarkColor = color;

            MarkType = reader.ReadByte();

            SpecialData = new byte[0];

            if (MarkType >= 0x3 && MarkType <= 0x0e) // possibly includes 0x0f?
                Data = reader.ReadBytes(MarkType * 3); // Standard fill polygon with x vertices with a set distance from an edge. 3 bytes per vertex.
            else if (MarkType == 0x02 || (MarkType >= 0x10 && MarkType <= 0xfe))
                Data = reader.ReadBytes(6); // line
            else if (MarkType == 0xff)
            {
                // LOD mark (doesn't render in the original game if the camera distance is outside the threshold)
                SpecialData = reader.ReadBytes(4);

                var subMark = new MarkRecord();
                subMark.Read(reader, color);
                MarkType = subMark.MarkType;
                Data = subMark.Data;
            }
            else
                throw new NotSupportedException($"Unrecognized type: {MarkType:X} ({MarkType})");
        }
    }

    public class HardpointRecord
    {
        public short HardpointType { get; protected set; }
        public Vector3 Position { get; protected set; }
        public byte[] UnknownData { get; protected set; }

        public void Read(BinaryReader reader)
        {
            HardpointType = reader.ReadInt16();
            var x = reader.ReadInt16();
            var y = reader.ReadInt16();
            var z = reader.ReadInt16();
            Position = new Vector3(x, y, z);

            // 5th/6th byte appear to be accumulator count / missile count?
            UnknownData = reader.ReadBytes(8);
        }
    }

    /// <summary>
    /// Other record. Not really doing anything with this, so just read and move on.
    /// </summary>
    public class BmapRecord : LfdRecord
    {
        public byte[] Data { get; protected set; }

        protected override void ReadRecordData(BinaryReader reader)
        {
            Data = reader.ReadBytes(DataLength);
        }
    }

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

    public static class BinaryReaderFactory
    {
        public static BinaryReader Instantiate(
            bool isLittleEndian,
            Stream stream,
            System.Text.Encoding encoding = null,
            bool leaveOpen = false)
        {
            encoding = encoding ?? System.Text.Encoding.Default;

            return isLittleEndian
                       ? new BinaryReader(stream, encoding, leaveOpen)
                       : new BigEndianBinaryReader(stream, encoding, leaveOpen);
        }
    }

    public static class ByteArrayExtensions
    {
        public static byte[] InEndianOrder(this byte[] o, bool isLittleEndian = true) => BitConverter.IsLittleEndian == isLittleEndian ? o : o.Reverse().ToArray();
        public static string ReadString(this byte[] o, long offset = 0)
        {
            var chars = new List<string>(o.Length);
            for (var i = 0; i < o.Length; i++)
            {
                var value = (char)o[i + offset];
                if (value == 0)
                    break;

                chars.Add(value.ToString());
            }

            return string.Concat(chars.ToArray());
        }
    }
}
