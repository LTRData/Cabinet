namespace LTRData.Cabinet;

/// <summary>A file described by a cabinet archive.</summary>
public sealed class CabinetFile
{
    internal CabinetFile(string name, uint length, uint folderOffset,
        ushort folderIndex, DateTime? lastWriteTime, CabinetFileAttributes attributes)
    {
        Name = name;
        Length = length;
        FolderOffset = folderOffset;
        FolderIndex = folderIndex;
        LastWriteTime = lastWriteTime;
        Attributes = attributes;
    }

    public string Name { get; }
    public uint Length { get; }
    public uint FolderOffset { get; }
    public ushort FolderIndex { get; }
    public DateTime? LastWriteTime { get; }
    public CabinetFileAttributes Attributes { get; }

    public bool ContinuesFromPreviousCabinet => FolderIndex is 0xFFFD or 0xFFFF;
    public bool ContinuesToNextCabinet => FolderIndex is 0xFFFE or 0xFFFF;
}
