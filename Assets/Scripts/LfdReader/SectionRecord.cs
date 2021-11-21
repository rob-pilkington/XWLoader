using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Assets.Scripts.LfdReader
{
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
}
