namespace LTRData.Cabinet.Internal;

/// <summary>A canonical Huffman decoder for the trees used by LZX.</summary>
internal sealed class LzxHuffmanDecoder
{
    private const int MaximumCodeLength = 16;
    private const int FastLookupBits = 10;

    private readonly int symbolCount;
    private readonly int[] counts = new int[MaximumCodeLength + 1];
    private readonly int[] firstCodes = new int[MaximumCodeLength + 1];
    private readonly int[] firstSymbols = new int[MaximumCodeLength + 1];
    private readonly ushort[] symbols;
    private readonly ushort[] fastSymbols = new ushort[1 << FastLookupBits];
    private readonly byte[] fastLengths = new byte[1 << FastLookupBits];

    private int maximumLength;

    internal LzxHuffmanDecoder(int symbolCount)
    {
        if (symbolCount <= 0 || symbolCount > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(symbolCount));
        }

        this.symbolCount = symbolCount;
        symbols = new ushort[symbolCount];
    }

    internal bool Build(ReadOnlySpan<byte> lengths, bool allowEmpty)
    {
        if (lengths.Length < symbolCount)
        {
            return false;
        }

        Array.Clear(counts, 0, counts.Length);
        Array.Clear(firstCodes, 0, firstCodes.Length);
        Array.Clear(firstSymbols, 0, firstSymbols.Length);
        Array.Clear(fastLengths, 0, fastLengths.Length);
        maximumLength = 0;

        for (int symbol = 0; symbol < symbolCount; symbol++)
        {
            int length = lengths[symbol];
            if (length > MaximumCodeLength)
            {
                return false;
            }

            if (length != 0)
            {
                counts[length]++;
                maximumLength = Math.Max(maximumLength, length);
            }
        }

        if (maximumLength == 0)
        {
            return allowEmpty;
        }

        int unusedCodes = 1;
        for (int length = 1; length <= MaximumCodeLength; length++)
        {
            unusedCodes = (unusedCodes << 1) - counts[length];
            if (unusedCodes < 0)
            {
                return false;
            }
        }

        var nextCodes = new int[MaximumCodeLength + 1];
        var nextSymbols = new int[MaximumCodeLength + 1];
        int code = 0;
        int symbolOffset = 0;

        for (int length = 1; length <= MaximumCodeLength; length++)
        {
            code = (code + counts[length - 1]) << 1;
            firstCodes[length] = code;
            nextCodes[length] = code;
            firstSymbols[length] = symbolOffset;
            nextSymbols[length] = symbolOffset;
            symbolOffset += counts[length];
        }

        for (int symbol = 0; symbol < symbolCount; symbol++)
        {
            int length = lengths[symbol];
            if (length == 0)
            {
                continue;
            }

            int symbolCode = nextCodes[length]++;
            symbols[nextSymbols[length]++] = (ushort)symbol;

            if (length <= FastLookupBits)
            {
                int start = symbolCode << (FastLookupBits - length);
                int entries = 1 << (FastLookupBits - length);
                for (int i = 0; i < entries; i++)
                {
                    fastSymbols[start + i] = (ushort)symbol;
                    fastLengths[start + i] = (byte)length;
                }
            }
        }

        return true;
    }

    internal bool TryDecode(ref LzxBitReader reader, out int symbol)
    {
        symbol = -1;
        if (maximumLength == 0)
        {
            return false;
        }

        if (reader.TryPeekBits(FastLookupBits, out uint lookup))
        {
            int length = fastLengths[(int)lookup];
            if (length != 0)
            {
                if (!reader.TryConsumeBits(length))
                {
                    return false;
                }

                symbol = fastSymbols[(int)lookup];
                return true;
            }
        }

        int code = 0;
        for (int length = 1; length <= maximumLength; length++)
        {
            if (!reader.TryReadBits(1, out uint bit))
            {
                return false;
            }

            code = (code << 1) | (int)bit;
            int offset = code - firstCodes[length];
            if ((uint)offset < (uint)counts[length])
            {
                symbol = symbols[firstSymbols[length] + offset];
                return true;
            }
        }

        return false;
    }
}
