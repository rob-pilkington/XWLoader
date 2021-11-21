using System;
using System.Collections.Generic;
using System.Linq;

namespace Assets.Scripts.LfdReader
{
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
