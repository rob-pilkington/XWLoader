using System.IO;
using UnityEngine;

namespace Assets.Scripts.LfdReader
{
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
}
