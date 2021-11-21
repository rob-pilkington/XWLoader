namespace Assets.Scripts.LfdReader
{
    public interface ICraft
    {
        string RecordType { get; }
        string RecordName { get; }
        SectionRecord[] Sections { get; }
    }
}
