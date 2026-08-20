using System.Text;

namespace LTRData.Cabinet.Internal;

internal static class CabinetParser
{
    private const uint Signature = 0x4643534D; // MSCF
    private const ushort ReservePresent = 0x0004;

    internal static ParsedCabinet Parse(Stream stream)
    {
        var reader = new LittleEndianReader(stream);
        if (reader.ReadUInt32() != Signature)
            throw new CabinetFormatException("The stream does not contain an MSCF cabinet.");

        _ = reader.ReadUInt32(); // reserved1
        uint cabinetSize = reader.ReadUInt32();
        _ = reader.ReadUInt32(); // reserved2
        uint fileTableOffset = reader.ReadUInt32();
        _ = reader.ReadUInt32(); // reserved3
        byte minorVersion = reader.ReadByte();
        byte majorVersion = reader.ReadByte();
        ushort folderCount = reader.ReadUInt16();
        ushort fileCount = reader.ReadUInt16();
        ushort flags = reader.ReadUInt16();
        ushort setId = reader.ReadUInt16();
        ushort cabinetIndex = reader.ReadUInt16();

        ushort headerReserveSize = 0;
        byte folderReserveSize = 0;
        byte dataReserveSize = 0;
        byte[] headerReserved = Array.Empty<byte>();
        if ((flags & ReservePresent) != 0)
        {
            headerReserveSize = reader.ReadUInt16();
            folderReserveSize = reader.ReadByte();
            dataReserveSize = reader.ReadByte();
            headerReserved = reader.ReadBytes(headerReserveSize);
        }

        string? previousCabinet = null;
        string? previousDisk = null;
        string? nextCabinet = null;
        string? nextDisk = null;
        var legacy = Encoding.GetEncoding(0);
        if ((flags & 0x0001) != 0)
        {
            previousCabinet = reader.ReadNullTerminatedString(legacy, 65536);
            previousDisk = reader.ReadNullTerminatedString(legacy, 65536);
        }
        if ((flags & 0x0002) != 0)
        {
            nextCabinet = reader.ReadNullTerminatedString(legacy, 65536);
            nextDisk = reader.ReadNullTerminatedString(legacy, 65536);
        }

        var folders = new CabinetFolder[folderCount];
        for (int i = 0; i < folders.Length; i++)
        {
            uint dataOffset = reader.ReadUInt32();
            ushort blockCount = reader.ReadUInt16();
            ushort compression = reader.ReadUInt16();
            var type = (CabinetCompressionType)(compression & 0x000F);
            if (type < CabinetCompressionType.None || type > CabinetCompressionType.Lzx)
                throw new CabinetFormatException($"Unknown cabinet compression type {compression & 0x000F}.");
            folders[i] = new CabinetFolder(i, dataOffset, blockCount, type,
                compression >> 8, reader.ReadBytes(folderReserveSize));
        }

        if (fileTableOffset > cabinetSize || fileTableOffset < reader.Position)
            throw new CabinetFormatException("The cabinet file table offset is invalid.");
        stream.Position = fileTableOffset;

        var files = new CabinetFile[fileCount];
        for (int i = 0; i < files.Length; i++)
        {
            uint length = reader.ReadUInt32();
            uint folderOffset = reader.ReadUInt32();
            ushort folderIndex = reader.ReadUInt16();
            ushort date = reader.ReadUInt16();
            ushort time = reader.ReadUInt16();
            var attributes = (CabinetFileAttributes)reader.ReadUInt16();
            Encoding nameEncoding = (attributes & CabinetFileAttributes.NameIsUtf8) != 0
                ? Encoding.UTF8 : legacy;
            string name = reader.ReadNullTerminatedString(nameEncoding, 65536);
            files[i] = new CabinetFile(name, length, folderOffset, folderIndex,
                DecodeDosDateTime(date, time), attributes);
        }

        return new ParsedCabinet(cabinetSize, majorVersion, minorVersion, flags,
            setId, cabinetIndex, dataReserveSize, headerReserved, previousCabinet,
            previousDisk, nextCabinet, nextDisk, folders, files);
    }

    private static DateTime? DecodeDosDateTime(ushort date, ushort time)
    {
        if (date == 0) return null;
        try
        {
            return new DateTime(1980 + (date >> 9), (date >> 5) & 15, date & 31,
                time >> 11, (time >> 5) & 63, (time & 31) * 2, DateTimeKind.Unspecified);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}

internal sealed class ParsedCabinet
{
    internal ParsedCabinet(uint cabinetSize, byte majorVersion, byte minorVersion,
        ushort flags, ushort setId, ushort cabinetIndex, byte dataReserveSize,
        byte[] reservedData, string? previousCabinet, string? previousDisk,
        string? nextCabinet, string? nextDisk, CabinetFolder[] folders,
        CabinetFile[] files)
    {
        CabinetSize = cabinetSize;
        MajorVersion = majorVersion;
        MinorVersion = minorVersion;
        Flags = flags;
        SetId = setId;
        CabinetIndex = cabinetIndex;
        DataReserveSize = dataReserveSize;
        ReservedData = reservedData;
        PreviousCabinet = previousCabinet;
        PreviousDisk = previousDisk;
        NextCabinet = nextCabinet;
        NextDisk = nextDisk;
        Folders = folders;
        Files = files;
    }

    internal uint CabinetSize { get; }
    internal byte MajorVersion { get; }
    internal byte MinorVersion { get; }
    internal ushort Flags { get; }
    internal ushort SetId { get; }
    internal ushort CabinetIndex { get; }
    internal byte DataReserveSize { get; }
    internal byte[] ReservedData { get; }
    internal string? PreviousCabinet { get; }
    internal string? PreviousDisk { get; }
    internal string? NextCabinet { get; }
    internal string? NextDisk { get; }
    internal CabinetFolder[] Folders { get; }
    internal CabinetFile[] Files { get; }
}
