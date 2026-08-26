namespace LTRData.Cabinet.Internal;

/// <summary>Reads the word-swapped, most-significant-bit-first LZX bitstream.</summary>
internal ref struct LzxBitReader
{
    private readonly ReadOnlySpan<byte> source;
    private int sourceOffset;
    private uint bitBuffer;
    private int bitsAvailable;
    private long bitsConsumed;

    internal LzxBitReader(ReadOnlySpan<byte> source)
    {
        this.source = source;
        sourceOffset = 0;
        bitBuffer = 0;
        bitsAvailable = 0;
        bitsConsumed = 0;
    }

    internal bool TryReadBits(int count, out uint value)
    {
        value = 0;
        if ((uint)count > 16u)
        {
            return false;
        }

        while (bitsAvailable < count)
        {
            if (sourceOffset > source.Length - 2)
            {
                return false;
            }

            uint word = (uint)(source[sourceOffset] | source[sourceOffset + 1] << 8);
            sourceOffset += 2;
            bitBuffer = (bitBuffer << 16) | word;
            bitsAvailable += 16;
        }

        bitsAvailable -= count;
        bitsConsumed += count;

        if (count != 0)
        {
            value = (bitBuffer >> bitsAvailable) & ((1u << count) - 1u);
        }

        return true;
    }

    internal bool TryPeekBits(int count, out uint value)
    {
        var copy = this;
        return copy.TryReadBits(count, out value);
    }

    internal bool TryConsumeBits(int count) => TryReadBits(count, out _);

    /// <summary>
    /// Aligns before the payload of an uncompressed LZX block. The format
    /// consumes between one and sixteen bits here, including a complete word
    /// when the block header already ended on a word boundary.
    /// </summary>
    internal bool TryAlignForUncompressedBlock()
    {
        int count = 16 - (int)(bitsConsumed & 15);
        return TryConsumeBits(count) && bitsAvailable == 0;
    }

    /// <summary>Discards the zero padding at the end of an LZX frame.</summary>
    internal bool TryCompleteFrame()
    {
        int misalignment = (int)(bitsConsumed & 15);
        if (misalignment != 0 && !TryConsumeBits(16 - misalignment))
        {
            return false;
        }

        // Some CAB encoders append one unused 16-bit lookahead word to a
        // frame. It is outside the logical bitstream but included in cbData.
        int bytesRemaining = source.Length - sourceOffset;
        return bitsAvailable == 0 && (bytesRemaining == 0 || bytesRemaining == 2);
    }

    internal bool TryReadRawByte(out byte value)
    {
        if (bitsAvailable != 0 || sourceOffset >= source.Length)
        {
            value = 0;
            return false;
        }

        value = source[sourceOffset++];
        bitsConsumed += 8;
        return true;
    }

    internal bool TryReadRawUInt32(out uint value)
    {
        if (bitsAvailable != 0 || sourceOffset > source.Length - 4)
        {
            value = 0;
            return false;
        }

        value = (uint)(source[sourceOffset] |
            source[sourceOffset + 1] << 8 |
            source[sourceOffset + 2] << 16 |
            source[sourceOffset + 3] << 24);
        sourceOffset += 4;
        bitsConsumed += 32;
        return true;
    }

    internal bool TryReadRawBytes(Span<byte> destination)
    {
        if (bitsAvailable != 0 || sourceOffset > source.Length - destination.Length)
        {
            return false;
        }

        source.Slice(sourceOffset, destination.Length).CopyTo(destination);
        sourceOffset += destination.Length;
        bitsConsumed += destination.Length * 8L;
        return true;
    }
}
