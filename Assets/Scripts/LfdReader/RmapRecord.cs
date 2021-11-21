using System.Collections.Generic;
using System.IO;

namespace Assets.Scripts.LfdReader
{
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
}
