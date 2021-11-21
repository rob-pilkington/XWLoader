using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Assets.Scripts.LfdReader
{
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
}
