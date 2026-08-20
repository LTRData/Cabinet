namespace LTRData.Cabinet;

/// <summary>A continuously compressed data stream within a cabinet.</summary>
public sealed class CabinetFolder
{
    internal CabinetFolder(int index, uint dataOffset, ushort dataBlockCount,
        CabinetCompressionType compressionType, int compressionParameter,
        byte[] reservedData)
    {
        Index = index;
        DataOffset = dataOffset;
        DataBlockCount = dataBlockCount;
        CompressionType = compressionType;
        CompressionParameter = compressionParameter;
        ReservedData = reservedData;
    }

    public int Index { get; }
    public uint DataOffset { get; }
    public ushort DataBlockCount { get; }
    public CabinetCompressionType CompressionType { get; }

    /// <summary>LZX window exponent or Quantum level/window bits.</summary>
    public int CompressionParameter { get; }

    public ReadOnlyMemory<byte> ReservedData { get; }
}
