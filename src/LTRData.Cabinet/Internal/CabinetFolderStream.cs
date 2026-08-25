using System.IO.Compression;

namespace LTRData.Cabinet.Internal;

/// <summary>Sequentially decodes the CFDATA records in one CFFOLDER.</summary>
internal sealed class CabinetFolderStream : Stream
{
    private const int MaximumDataBlockOutput = 32768;
    private const int MsZipHistorySize = 32768;

    private readonly CabinetArchive archive;
    private readonly CabinetFolder folder;
    private readonly byte dataReserveSize;
    private readonly byte[] output = new byte[MaximumDataBlockOutput];
    private readonly byte[] msZipHistory = new byte[MsZipHistorySize];

    private long nextDataOffset;
    private int blocksRemaining;
    private int outputOffset;
    private int outputCount;
    private long position;
    private bool disposed;

    internal CabinetFolderStream(CabinetArchive archive, CabinetFolder folder)
    {
        this.archive = archive;
        this.folder = folder;
        dataReserveSize = archive.DataReserveSize;
        nextDataOffset = folder.DataOffset;
        blocksRemaining = folder.DataBlockCount;
    }

    public override bool CanRead => !disposed;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateReadArguments(buffer, offset, count);
        ThrowIfDisposed();

        int totalRead = 0;
        while (count > 0)
        {
            if (outputOffset == outputCount && !ReadNextDataBlock())
            {
                break;
            }

            int copy = Math.Min(count, outputCount - outputOffset);
            Buffer.BlockCopy(output, outputOffset, buffer, offset, copy);
            outputOffset += copy;
            offset += copy;
            count -= copy;
            totalRead += copy;
            position += copy;
        }

        return totalRead;
    }

    public override void Flush() { }

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        disposed = true;
        base.Dispose(disposing);
    }

    private bool ReadNextDataBlock()
    {
        if (blocksRemaining == 0)
        {
            return false;
        }

        var header = new byte[8];
        archive.ReadExactly(nextDataOffset, header, 0, header.Length);

        uint expectedChecksum = ReadUInt32(header, 0);
        int compressedSize = ReadUInt16(header, 4);
        int uncompressedSize = ReadUInt16(header, 6);

        if (uncompressedSize == 0)
        {
            throw new NotSupportedException("CFDATA blocks spanning multiple cabinets are not supported.");
        }

        if (uncompressedSize > MaximumDataBlockOutput)
        {
            throw new CabinetFormatException("A CFDATA block expands beyond the CAB format limit.");
        }

        int storedSize = dataReserveSize + compressedSize;
        var stored = new byte[storedSize];
        archive.ReadExactly(nextDataOffset + header.Length, stored, 0, stored.Length);
        nextDataOffset += header.Length + stored.Length;
        blocksRemaining--;

        if (expectedChecksum != 0)
        {
            uint actualChecksum = ComputeChecksum(stored, 0, stored.Length, 0);
            actualChecksum = ComputeChecksum(header, 4, 4, actualChecksum);
            if (actualChecksum != expectedChecksum)
            {
                throw new CabinetFormatException("The CFDATA checksum is invalid.");
            }
        }

        switch (folder.CompressionType)
        {
            case CabinetCompressionType.None:
                DecodeUncompressed(stored, dataReserveSize, compressedSize, uncompressedSize);
                break;

            case CabinetCompressionType.MsZip:
                DecodeMsZip(stored, dataReserveSize, compressedSize, uncompressedSize);
                break;

            case CabinetCompressionType.Lzx:
                throw new NotSupportedException("LZX cabinet decompression is not implemented yet.");

            case CabinetCompressionType.Quantum:
                throw new NotSupportedException("Quantum cabinet decompression is not supported.");

            default:
                throw new CabinetFormatException("The cabinet folder uses an unknown compression type.");
        }

        outputOffset = 0;
        outputCount = uncompressedSize;
        return true;
    }

    private void DecodeUncompressed(byte[] stored, int dataOffset, int compressedSize,
        int uncompressedSize)
    {
        if (compressedSize != uncompressedSize)
        {
            throw new CabinetFormatException("An uncompressed CFDATA block has inconsistent sizes.");
        }

        Buffer.BlockCopy(stored, dataOffset, output, 0, uncompressedSize);
    }

    private void DecodeMsZip(byte[] stored, int dataOffset, int compressedSize,
        int uncompressedSize)
    {
        if (compressedSize < 2 || stored[dataOffset] != (byte)'C' ||
            stored[dataOffset + 1] != (byte)'K')
        {
            throw new CabinetFormatException("An MSZIP CFDATA block has an invalid signature.");
        }

        int deflateSize = compressedSize - 2;
        var input = new byte[5 + MsZipHistorySize + deflateSize];

        // A non-final, byte-aligned DEFLATE stored block seeds the inflater's
        // 32 KiB history. Its output is discarded below. This is equivalent
        // to supplying the CAB MSZIP preset dictionary, which DeflateStream
        // does not expose directly.
        input[0] = 0x00;
        input[1] = 0x00;
        input[2] = 0x80;
        input[3] = 0xFF;
        input[4] = 0x7F;
        Buffer.BlockCopy(msZipHistory, 0, input, 5, MsZipHistorySize);
        Buffer.BlockCopy(stored, dataOffset + 2, input, 5 + MsZipHistorySize, deflateSize);

        using (var inputStream = new MemoryStream(input, writable: false))
        using (var inflater = new DeflateStream(inputStream, CompressionMode.Decompress))
        {
            var discard = new byte[4096];
            ReadExactly(inflater, discard, MsZipHistorySize, discardOutput: true);
            ReadExactly(inflater, output, uncompressedSize, discardOutput: false);

            if (inflater.ReadByte() != -1)
            {
                throw new CabinetFormatException("An MSZIP block expanded beyond its declared size.");
            }
        }

        UpdateMsZipHistory(output, uncompressedSize);
    }

    private static void ReadExactly(Stream source, byte[] buffer, int count,
        bool discardOutput)
    {
        int completed = 0;
        while (completed < count)
        {
            int requested = discardOutput
                ? Math.Min(buffer.Length, count - completed)
                : count - completed;
            int targetOffset = discardOutput ? 0 : completed;
            int read = source.Read(buffer, targetOffset, requested);
            if (read == 0)
            {
                throw new CabinetFormatException("An MSZIP block ended before its declared size.");
            }

            completed += read;
        }
    }

    private void UpdateMsZipHistory(byte[] data, int count)
    {
        if (count >= MsZipHistorySize)
        {
            Buffer.BlockCopy(data, count - MsZipHistorySize, msZipHistory, 0, MsZipHistorySize);
            return;
        }

        Buffer.BlockCopy(msZipHistory, count, msZipHistory, 0, MsZipHistorySize - count);
        Buffer.BlockCopy(data, 0, msZipHistory, MsZipHistorySize - count, count);
    }

    private static uint ComputeChecksum(byte[] data, int offset, int count, uint checksum)
    {
        while (count >= 4)
        {
            checksum ^= ReadUInt32(data, offset);
            offset += 4;
            count -= 4;
        }

        uint trailing = 0;
        if (count >= 1)
        {
            trailing |= data[offset];
        }

        if (count >= 2)
        {
            trailing |= (uint)data[offset + 1] << 8;
        }

        if (count == 3)
        {
            trailing |= (uint)data[offset + 2] << 16;
        }

        return checksum ^ trailing;
    }

    private static ushort ReadUInt16(byte[] buffer, int offset) =>
        (ushort)(buffer[offset] | buffer[offset + 1] << 8);

    private static uint ReadUInt32(byte[] buffer, int offset) =>
        (uint)(buffer[offset] | buffer[offset + 1] << 8 |
            buffer[offset + 2] << 16 | buffer[offset + 3] << 24);

    private static void ValidateReadArguments(byte[] buffer, int offset, int count)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(buffer);
#else
        if (buffer is null)
        {
            throw new ArgumentNullException(nameof(buffer));
        }
#endif

        if (offset < 0 || count < 0 || buffer.Length - offset < count)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }
    }

    private void ThrowIfDisposed()
    {
#if NET7_0_OR_GREATER
        ObjectDisposedException.ThrowIf(disposed, this);
#else
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(CabinetFolderStream));
        }
#endif
    }
}
