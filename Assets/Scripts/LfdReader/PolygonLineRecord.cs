using System;
using System.IO;
using UnityEngine;

namespace Assets.Scripts.LfdReader
{
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
}
