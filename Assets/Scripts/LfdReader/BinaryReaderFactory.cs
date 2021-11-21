using System.IO;

namespace Assets.Scripts.LfdReader
{
    public static class BinaryReaderFactory
    {
        public static BinaryReader Instantiate(
            bool isLittleEndian,
            Stream stream,
            System.Text.Encoding encoding = null,
            bool leaveOpen = false)
        {
            encoding ??= System.Text.Encoding.Default;

            return isLittleEndian
                ? new BinaryReader(stream, encoding, leaveOpen)
                : new BigEndianBinaryReader(stream, encoding, leaveOpen);
        }
    }
}
