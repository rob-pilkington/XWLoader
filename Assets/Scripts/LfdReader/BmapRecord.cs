using System.IO;

namespace Assets.Scripts.LfdReader
{
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
}
