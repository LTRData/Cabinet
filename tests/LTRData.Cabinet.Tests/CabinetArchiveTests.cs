using System.Buffers.Binary;

namespace LTRData.Cabinet.Tests;

public sealed class CabinetArchiveTests
{
    [Fact]
    public void OpenRejectsInvalidSignature()
    {
        using var stream = new MemoryStream(new byte[36]);
        Assert.Throws<CabinetFormatException>(() => CabinetArchive.Open(stream));
    }

    [Fact]
    public void OpenReadsEmptyCabinetHeader()
    {
        byte[] data = CreateEmptyCabinet();
        using var archive = CabinetArchive.Open(new MemoryStream(data));

        Assert.Equal((uint)data.Length, archive.CabinetSize);
        Assert.Equal(1, archive.MajorVersion);
        Assert.Equal(3, archive.MinorVersion);
        Assert.Empty(archive.Folders);
        Assert.Empty(archive.Files);
    }

    private static byte[] CreateEmptyCabinet()
    {
        var data = new byte[36];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0), 0x4643534D);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), (uint)data.Length);
        data[24] = 3;
        data[25] = 1;
        return data;
    }
}
