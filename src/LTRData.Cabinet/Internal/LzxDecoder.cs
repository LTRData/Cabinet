namespace LTRData.Cabinet.Internal;

/// <summary>Stateful decoder for the LZX stream carried by one CAB folder.</summary>
internal sealed class LzxDecoder
{
    private const int MaximumFrameSize = 32768;
    private const int MinimumWindowBits = 15;
    private const int MaximumWindowBits = 21;
    private const int LiteralCount = 256;
    private const int PrimaryLengthCount = 8;
    private const int SecondaryLengthCount = 249;

    private static readonly int[] PositionSlotCounts = [30, 32, 34, 36, 38, 42, 50];
    private static readonly int[] ExtraBits = CreateExtraBits();
    private static readonly uint[] PositionBases = CreatePositionBases();

    private readonly byte[] window;
    private readonly int windowMask;
    private readonly int positionSlotCount;
    private readonly byte[] mainLengths;
    private readonly byte[] lengthLengths = new byte[SecondaryLengthCount];
    private readonly byte[] alignedLengths = new byte[8];
    private readonly byte[] preTreeLengths = new byte[20];
    private readonly LzxHuffmanDecoder mainTree;
    private readonly LzxHuffmanDecoder lengthTree = new(SecondaryLengthCount);
    private readonly LzxHuffmanDecoder alignedTree = new(8);
    private readonly LzxHuffmanDecoder preTree = new(20);

    private int windowPosition;
    private long outputPosition;
    private uint repeatedOffset0 = 1;
    private uint repeatedOffset1 = 1;
    private uint repeatedOffset2 = 1;
    private bool headerRead;
    private int intelFileSize;
    private LzxBlockType blockType;
    private int blockLength;
    private int blockRemaining;

    internal LzxDecoder(int windowBits)
    {
        if (windowBits < MinimumWindowBits || windowBits > MaximumWindowBits)
        {
            throw new CabinetFormatException(
                $"The LZX window exponent {windowBits} is outside the CAB range of 15 through 21.");
        }

        int windowSize = 1 << windowBits;
        window = new byte[windowSize];
        windowMask = windowSize - 1;
        positionSlotCount = PositionSlotCounts[windowBits - MinimumWindowBits];
        mainLengths = new byte[LiteralCount + PrimaryLengthCount * positionSlotCount];
        mainTree = new LzxHuffmanDecoder(mainLengths.Length);
    }

    internal void Decompress(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        if (destination.Length <= 0 || destination.Length > MaximumFrameSize)
        {
            throw new CabinetFormatException("An LZX frame has an invalid uncompressed size.");
        }

        var reader = new LzxBitReader(source);
        long frameOffset = outputPosition;
        int destinationOffset = 0;

        if (!headerRead)
        {
            ReadStreamHeader(ref reader);
        }

        while (destinationOffset < destination.Length)
        {
            if (blockRemaining == 0)
            {
                ReadBlockHeader(ref reader);
            }

            int run = Math.Min(blockRemaining, destination.Length - destinationOffset);
            switch (blockType)
            {
                case LzxBlockType.Verbatim:
                case LzxBlockType.AlignedOffset:
                    DecodeCompressed(ref reader, destination, ref destinationOffset, run);
                    break;

                case LzxBlockType.Uncompressed:
                    DecodeUncompressed(ref reader, destination, ref destinationOffset, run);
                    break;

                default:
                    throw InvalidData("The LZX block type is invalid.");
            }

            if (blockRemaining == 0 && blockType == LzxBlockType.Uncompressed &&
                (blockLength & 1) != 0 && !reader.TryReadRawByte(out _))
            {
                throw InvalidData("An odd-sized uncompressed LZX block is missing its padding byte.");
            }
        }

        if (!reader.TryCompleteFrame())
        {
            throw InvalidData("The LZX frame has invalid padding or trailing data.");
        }

        ApplyIntelE8Transform(destination, frameOffset);
    }

    private void ReadStreamHeader(ref LzxBitReader reader)
    {
        uint intelTransform = ReadBits(ref reader, 1);
        if (intelTransform != 0)
        {
            uint high = ReadBits(ref reader, 16);
            uint low = ReadBits(ref reader, 16);
            intelFileSize = unchecked((int)((high << 16) | low));
        }

        headerRead = true;
    }

    private void ReadBlockHeader(ref LzxBitReader reader)
    {
        blockType = (LzxBlockType)ReadBits(ref reader, 3);
        uint highLength = ReadBits(ref reader, 16);
        uint lowLength = ReadBits(ref reader, 8);
        blockLength = checked((int)((highLength << 8) | lowLength));
        if (blockLength == 0)
        {
            throw InvalidData("An LZX block has a zero length.");
        }

        blockRemaining = blockLength;

        switch (blockType)
        {
            case LzxBlockType.AlignedOffset:
                for (int i = 0; i < alignedLengths.Length; i++)
                {
                    alignedLengths[i] = (byte)ReadBits(ref reader, 3);
                }

                if (!alignedTree.Build(alignedLengths, allowEmpty: false))
                {
                    throw InvalidData("The LZX aligned-offset tree is invalid.");
                }

                ReadCompressedBlockTrees(ref reader);
                break;

            case LzxBlockType.Verbatim:
                ReadCompressedBlockTrees(ref reader);
                break;

            case LzxBlockType.Uncompressed:
                if (!reader.TryAlignForUncompressedBlock() ||
                    !reader.TryReadRawUInt32(out repeatedOffset0) ||
                    !reader.TryReadRawUInt32(out repeatedOffset1) ||
                    !reader.TryReadRawUInt32(out repeatedOffset2))
                {
                    throw InvalidData("The uncompressed LZX block header is truncated.");
                }
                break;

            default:
                throw InvalidData($"The LZX block type {(int)blockType} is invalid.");
        }
    }

    private void ReadCompressedBlockTrees(ref LzxBitReader reader)
    {
        ReadLengths(ref reader, mainLengths, 0, LiteralCount);
        ReadLengths(ref reader, mainLengths, LiteralCount, mainLengths.Length);
        if (!mainTree.Build(mainLengths, allowEmpty: false))
        {
            throw InvalidData("The LZX main tree is invalid.");
        }

        ReadLengths(ref reader, lengthLengths, 0, lengthLengths.Length);
        if (!lengthTree.Build(lengthLengths, allowEmpty: true))
        {
            throw InvalidData("The LZX length tree is invalid.");
        }
    }

    private void ReadLengths(ref LzxBitReader reader, byte[] lengths,
        int first, int last)
    {
        for (int i = 0; i < preTreeLengths.Length; i++)
        {
            preTreeLengths[i] = (byte)ReadBits(ref reader, 4);
        }

        if (!preTree.Build(preTreeLengths, allowEmpty: false))
        {
            throw InvalidData("The LZX pre-tree is invalid.");
        }

        int index = first;
        while (index < last)
        {
            int value = DecodeSymbol(ref reader, preTree, "pre-tree");
            switch (value)
            {
                case 17:
                    WriteZeroRun(lengths, ref index, last,
                        4 + (int)ReadBits(ref reader, 4));
                    break;

                case 18:
                    WriteZeroRun(lengths, ref index, last,
                        20 + (int)ReadBits(ref reader, 5));
                    break;

                case 19:
                    int repetitions = 4 + (int)ReadBits(ref reader, 1);
                    if (repetitions > last - index)
                    {
                        throw InvalidData("An LZX code-length run exceeds its tree.");
                    }

                    int delta = DecodeSymbol(ref reader, preTree, "pre-tree");
                    if (delta > 16)
                    {
                        throw InvalidData("An LZX code-length delta is invalid.");
                    }

                    byte repeatedLength = (byte)((17 + lengths[index] - delta) % 17);
                    for (int i = 0; i < repetitions; i++)
                    {
                        lengths[index++] = repeatedLength;
                    }
                    break;

                default:
                    if ((uint)value > 16u)
                    {
                        throw InvalidData("An LZX pre-tree symbol is invalid.");
                    }

                    lengths[index] = (byte)((17 + lengths[index] - value) % 17);
                    index++;
                    break;
            }
        }
    }

    private static void WriteZeroRun(byte[] lengths, ref int index, int last,
        int repetitions)
    {
        if (repetitions > last - index)
        {
            throw InvalidData("An LZX zero run exceeds its tree.");
        }

        Array.Clear(lengths, index, repetitions);
        index += repetitions;
    }

    private void DecodeCompressed(ref LzxBitReader reader, Span<byte> destination,
        ref int destinationOffset, int count)
    {
        int end = destinationOffset + count;
        while (destinationOffset < end)
        {
            int mainElement = DecodeSymbol(ref reader, mainTree, "main tree");
            if (mainElement < LiteralCount)
            {
                WriteByte(destination, ref destinationOffset, (byte)mainElement);
                blockRemaining--;
                continue;
            }

            int match = mainElement - LiteralCount;
            int lengthHeader = match & 7;
            int positionSlot = match >> 3;
            int matchLength = lengthHeader + 2;

            if (lengthHeader == 7)
            {
                matchLength += DecodeSymbol(ref reader, lengthTree, "length tree");
            }

            if (matchLength > blockRemaining || matchLength > end - destinationOffset)
            {
                throw InvalidData("An LZX match crosses a block or frame boundary.");
            }

            uint matchOffset = DecodeMatchOffset(ref reader, positionSlot);
            CopyMatch(destination, ref destinationOffset, matchOffset, matchLength);
            blockRemaining -= matchLength;
        }
    }

    private void DecodeUncompressed(ref LzxBitReader reader, Span<byte> destination,
        ref int destinationOffset, int count)
    {
        Span<byte> output = destination.Slice(destinationOffset, count);
        if (!reader.TryReadRawBytes(output))
        {
            throw InvalidData("An uncompressed LZX block is truncated.");
        }

        CopyToWindow(output);
        destinationOffset += count;
        blockRemaining -= count;
    }

    private uint DecodeMatchOffset(ref LzxBitReader reader, int positionSlot)
    {
        switch (positionSlot)
        {
            case 0:
                return repeatedOffset0;

            case 1:
                uint offset1 = repeatedOffset1;
                repeatedOffset1 = repeatedOffset0;
                repeatedOffset0 = offset1;
                return offset1;

            case 2:
                uint offset2 = repeatedOffset2;
                repeatedOffset2 = repeatedOffset0;
                repeatedOffset0 = offset2;
                return offset2;
        }

        if (positionSlot < 3 || positionSlot >= positionSlotCount)
        {
            throw InvalidData("An LZX match uses an invalid position slot.");
        }

        int extra = ExtraBits[positionSlot];
        uint formattedOffset = PositionBases[positionSlot];

        if (blockType == LzxBlockType.AlignedOffset && extra >= 3)
        {
            if (extra > 3)
            {
                formattedOffset += ReadBits(ref reader, extra - 3) << 3;
            }

            formattedOffset += (uint)DecodeSymbol(ref reader, alignedTree,
                "aligned-offset tree");
        }
        else if (extra != 0)
        {
            formattedOffset += ReadBits(ref reader, extra);
        }

        uint matchOffset = formattedOffset - 2;
        repeatedOffset2 = repeatedOffset1;
        repeatedOffset1 = repeatedOffset0;
        repeatedOffset0 = matchOffset;
        return matchOffset;
    }

    private void CopyMatch(Span<byte> destination, ref int destinationOffset,
        uint matchOffset, int matchLength)
    {
        if (matchOffset == 0 || matchOffset > (uint)window.Length ||
            matchOffset > (ulong)outputPosition)
        {
            throw InvalidData("An LZX match offset refers outside the decoded window.");
        }

        int sourcePosition = (windowPosition - (int)matchOffset) & windowMask;
        for (int i = 0; i < matchLength; i++)
        {
            byte value = window[sourcePosition];
            sourcePosition = (sourcePosition + 1) & windowMask;
            WriteByte(destination, ref destinationOffset, value);
        }
    }

    private void WriteByte(Span<byte> destination, ref int destinationOffset,
        byte value)
    {
        destination[destinationOffset++] = value;
        window[windowPosition] = value;
        windowPosition = (windowPosition + 1) & windowMask;
        outputPosition++;
    }

    private void CopyToWindow(ReadOnlySpan<byte> data)
    {
        int sourceOffset = 0;
        while (sourceOffset < data.Length)
        {
            int copy = Math.Min(data.Length - sourceOffset, window.Length - windowPosition);
            data.Slice(sourceOffset, copy).CopyTo(window.AsSpan(windowPosition, copy));
            sourceOffset += copy;
            windowPosition = (windowPosition + copy) & windowMask;
        }

        outputPosition += data.Length;
    }

    private void ApplyIntelE8Transform(Span<byte> data, long frameOffset)
    {
        if (intelFileSize == 0 || frameOffset >= 0x40000000L || data.Length <= 10)
        {
            return;
        }

        int index = 0;
        int currentPosition = checked((int)frameOffset);
        int end = data.Length - 10;

        while (index < end)
        {
            if (data[index] != 0xE8)
            {
                index++;
                currentPosition++;
                continue;
            }

            int absolute = ReadInt32LittleEndian(data, index + 1);
            if (absolute >= -currentPosition && absolute < intelFileSize)
            {
                int relative = absolute >= 0
                    ? unchecked(absolute - currentPosition)
                    : unchecked(absolute + intelFileSize);
                WriteInt32LittleEndian(data, index + 1, relative);
            }

            index += 5;
            currentPosition += 5;
        }
    }

    private static uint ReadBits(ref LzxBitReader reader, int count)
    {
        if (!reader.TryReadBits(count, out uint value))
        {
            throw InvalidData("The LZX bitstream ended unexpectedly.");
        }

        return value;
    }

    private static int DecodeSymbol(ref LzxBitReader reader,
        LzxHuffmanDecoder tree, string treeName)
    {
        if (!tree.TryDecode(ref reader, out int symbol))
        {
            throw InvalidData($"The LZX {treeName} could not decode a symbol.");
        }

        return symbol;
    }

    private static int[] CreateExtraBits()
    {
        var result = new int[50];
        for (int slot = 4; slot < result.Length; slot++)
        {
            result[slot] = Math.Min(17, (slot - 2) / 2);
        }

        return result;
    }

    private static uint[] CreatePositionBases()
    {
        var result = new uint[50];
        for (int slot = 1; slot < result.Length; slot++)
        {
            result[slot] = result[slot - 1] +
                (1u << ExtraBits[slot - 1]);
        }

        return result;
    }

    private static int ReadInt32LittleEndian(ReadOnlySpan<byte> data, int offset) =>
        data[offset] |
        data[offset + 1] << 8 |
        data[offset + 2] << 16 |
        data[offset + 3] << 24;

    private static void WriteInt32LittleEndian(Span<byte> data, int offset, int value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
        data[offset + 2] = (byte)(value >> 16);
        data[offset + 3] = (byte)(value >> 24);
    }

    private static CabinetFormatException InvalidData(string message) => new(message);

    private enum LzxBlockType
    {
        Invalid = 0,
        Verbatim = 1,
        AlignedOffset = 2,
        Uncompressed = 3,
    }
}
