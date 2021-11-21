namespace Assets.Scripts.LfdReader
{
    public class LfdHeader
    {
        public string DescriptiveName => string.Format("{0} ({1}, {2} bytes", Name, Type, Length);

        public string Type { get; set; } // 4
        public string Name { get; set; } // 8
        public int Length { get; set; } // 4
    }
}
