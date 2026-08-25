using System.Buffers.Binary;
using System.Text;
using Xunit;

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

    [Fact]
    public void FileOpenReadsUncompressedRangeAcrossDataBlocks()
    {
        byte[] first = Encoding.ASCII.GetBytes("abcdef");
        byte[] second = Encoding.ASCII.GetBytes("ghijkl");
        byte[] cabinet = CreateCabinet(
            CabinetCompressionType.None,
            [(first, first.Length), (second, second.Length)],
            "range.txt", 4, 7);

        using var archive = CabinetArchive.Open(new MemoryStream(cabinet));
        using Stream file = Assert.Single(archive.Files).Open();

        Assert.True(file.CanRead);
        Assert.False(file.CanSeek);
        Assert.Equal(7L, file.Length);
        Assert.Equal("efghijk", Encoding.ASCII.GetString(ReadAll(file, 2)));
    }

    [Fact]
    public void FileOpenReadsMsZipRangeAcrossDictionaryDependentBlocks()
    {
        const string pattern = "The quick brown fox jumps over the lazy dog.\n";
        byte[] first = RepeatToLength(pattern, 32768);
        byte[] second = RepeatToLength(pattern, 12000);

        // Generated as raw DEFLATE with zlib. The first block uses the CAB
        // initial 32 KiB zero dictionary; the second uses the first block as
        // its preset dictionary. Both include the MSZIP CK signature.
        byte[] firstCompressed = Convert.FromBase64String(
            "Q0vtysEVgjAUALC7U/wJnIYFRCsoYLFYRKeXOXw5J02f4llv5yHakt+PuOYt7nWal8hrKvHaeTx9P3HJ3fEgy7Isy7Isy7Isy7Isy7Isy7Isy7Isy7Isy7Isy7Isy7Isy7Isy7Isy7Isy7Isy7Isy7Isy7Isy7Isy7Isy7Isy7Isy7Isy7Isy7Isy7Isy7Isy7Isy7Isy7Isy7Isy3+afw==");
        byte[] secondCompressed = Convert.FromBase64String(
            "Q0vt0CEBAAAAgKD/r71hoJOwAsMwDMMwDMMwDMMwDMMwDMMwDMMwDMMwDMMwDMMwDMMwDB9w");

        const int fileOffset = 32000;
        const int fileLength = 10000;
        byte[] cabinet = CreateCabinet(
            CabinetCompressionType.MsZip,
            [(firstCompressed, first.Length), (secondCompressed, second.Length)],
            "range.txt", fileOffset, fileLength);

        var folder = new byte[first.Length + second.Length];
        Buffer.BlockCopy(first, 0, folder, 0, first.Length);
        Buffer.BlockCopy(second, 0, folder, first.Length, second.Length);
        var expected = new byte[fileLength];
        Buffer.BlockCopy(folder, fileOffset, expected, 0, fileLength);

        using var archive = CabinetArchive.Open(new MemoryStream(cabinet));
        using Stream file = Assert.Single(archive.Files).Open();

        Assert.Equal(expected, ReadAll(file, 113));
        Assert.Equal(-1, file.ReadByte());
    }

    [Fact]
    public void FileOpenDetectsInvalidDataChecksum()
    {
        byte[] data = Encoding.ASCII.GetBytes("checksum");
        byte[] cabinet = CreateCabinet(
            CabinetCompressionType.None,
            [(data, data.Length)],
            "data.txt", 0, data.Length);
        cabinet[cabinet.Length - 1] ^= 0x80;

        using var archive = CabinetArchive.Open(new MemoryStream(cabinet));
        using Stream file = Assert.Single(archive.Files).Open();

        Assert.Throws<CabinetFormatException>(() => file.ReadByte());
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

    private static byte[] CreateCabinet(
        CabinetCompressionType compressionType,
        IReadOnlyList<(byte[] StoredData, int UncompressedSize)> blocks,
        string fileName,
        int fileOffset,
        int fileLength)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write(0x4643534Du); // MSCF
        writer.Write(0u);
        writer.Write(0u); // cabinet size, patched below
        writer.Write(0u);
        writer.Write(44u); // first CFFILE follows CFHEADER + one CFFOLDER
        writer.Write(0u);
        writer.Write((byte)3);
        writer.Write((byte)1);
        writer.Write((ushort)1);
        writer.Write((ushort)1);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)0);

        writer.Write(0u); // first CFDATA offset, patched below
        writer.Write((ushort)blocks.Count);
        writer.Write((ushort)compressionType);

        writer.Write((uint)fileLength);
        writer.Write((uint)fileOffset);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write(Encoding.ASCII.GetBytes(fileName));
        writer.Write((byte)0);

        uint dataOffset = checked((uint)stream.Position);
        foreach ((byte[] storedData, int uncompressedSize) in blocks)
        {
            if (storedData.Length > ushort.MaxValue || uncompressedSize > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(blocks));
            }

            var sizes = new byte[4];
            BinaryPrimitives.WriteUInt16LittleEndian(sizes, (ushort)storedData.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(sizes.AsSpan(2), (ushort)uncompressedSize);
            uint checksum = ComputeChecksum(storedData, ComputeChecksum(sizes, 0));

            writer.Write(checksum);
            writer.Write((ushort)storedData.Length);
            writer.Write((ushort)uncompressedSize);
            writer.Write(storedData);
        }

        uint cabinetSize = checked((uint)stream.Length);
        stream.Position = 8;
        writer.Write(cabinetSize);
        stream.Position = 36;
        writer.Write(dataOffset);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] RepeatToLength(string value, int length)
    {
        byte[] pattern = Encoding.ASCII.GetBytes(value);
        var result = new byte[length];
        for (int offset = 0; offset < result.Length; offset += pattern.Length)
        {
            Buffer.BlockCopy(pattern, 0, result, offset,
                Math.Min(pattern.Length, result.Length - offset));
        }

        return result;
    }

    private static byte[] ReadAll(Stream stream, int readSize)
    {
        using var output = new MemoryStream();
        var buffer = new byte[readSize];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) != 0)
        {
            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static uint ComputeChecksum(byte[] data, uint checksum)
    {
        int offset = 0;
        int count = data.Length;
        while (count >= 4)
        {
            checksum ^= BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
            offset += 4;
            count -= 4;
        }

        uint trailing = 0;
        for (int i = 0; i < count; i++)
        {
            trailing |= (uint)data[offset + i] << (i * 8);
        }

        return checksum ^ trailing;
    }
}
