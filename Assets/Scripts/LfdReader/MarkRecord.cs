using System;
using System.IO;

namespace Assets.Scripts.LfdReader
{
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
}
