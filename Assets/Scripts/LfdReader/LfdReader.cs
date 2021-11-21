using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Assets.Scripts.LfdReader
{
    public class LfdReader
    {
        public IDictionary<string, LfdRecord> Records { get; private set; } = new Dictionary<string, LfdRecord>();

        public void Read(FileStream fs, bool hasWrongEndianLineRadius)
        {
            using var reader = new BinaryReader(fs);

            var map = new RmapRecord();
            map.ReadLfdRecord(reader);

            foreach (var entry in map.Entries)
            {
                LfdRecord record = entry.Type switch
                {
                    "CRFT" => new CrftRecord(),
                    "CPLX" => new CplxRecord(hasWrongEndianLineRadius),
                    "SHIP" => new ShipRecord(),

                    "BTMP" => new BmapRecord(),
                    "BMAP" => new BmapRecord(),
                    "XACT" => new BmapRecord(),

                    _ => throw new NotSupportedException("Unknown record type: " + entry.Type)
                };

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
