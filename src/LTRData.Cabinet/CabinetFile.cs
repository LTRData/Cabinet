namespace LTRData.Cabinet;

/// <summary>A file described by a cabinet archive.</summary>
public sealed class CabinetFile
{
    private CabinetArchive? archive;

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

    /// <summary>Opens the uncompressed file contents as a forward-only stream.</summary>
    /// <remarks>The owning <see cref="CabinetArchive"/> must remain open while reading.</remarks>
    public Stream Open()
    {
        if (archive is null)
        {
            throw new InvalidOperationException("The cabinet file is not attached to an archive.");
        }

        return archive.OpenFile(this);
    }

    internal CabinetArchive? Archive => archive;

    internal void Attach(CabinetArchive owner)
    {
        if (archive is not null)
        {
            throw new InvalidOperationException("The cabinet file is already attached to an archive.");
        }

        archive = owner;
    }
}
