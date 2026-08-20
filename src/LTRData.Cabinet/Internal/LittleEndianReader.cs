using System.Text;

namespace LTRData.Cabinet.Internal;

internal sealed class LittleEndianReader
{
    private readonly Stream stream;
    private readonly byte[] scalar = new byte[4];

    internal LittleEndianReader(Stream stream) => this.stream = stream;

    internal long Position => stream.Position;

    internal byte ReadByte()
    {
        int value = stream.ReadByte();
        if (value < 0) throw UnexpectedEnd();
        return (byte)value;
    }

    internal ushort ReadUInt16()
    {
        ReadExactly(scalar, 0, 2);
        return (ushort)(scalar[0] | scalar[1] << 8);
    }

    internal uint ReadUInt32()
    {
        ReadExactly(scalar, 0, 4);
        return (uint)(scalar[0] | scalar[1] << 8 | scalar[2] << 16 | scalar[3] << 24);
    }

    internal byte[] ReadBytes(int count)
    {
        var buffer = new byte[count];
        ReadExactly(buffer, 0, count);
        return buffer;
    }

    internal string ReadNullTerminatedString(Encoding encoding, int maximumBytes)
    {
        using var bytes = new MemoryStream();
        for (int i = 0; i < maximumBytes; i++)
        {
            byte value = ReadByte();
            if (value == 0) return encoding.GetString(bytes.ToArray());
            bytes.WriteByte(value);
        }

        throw new CabinetFormatException("Cabinet string is not null terminated.");
    }

    private void ReadExactly(byte[] buffer, int offset, int count)
    {
        while (count > 0)
        {
            int read = stream.Read(buffer, offset, count);
            if (read == 0) throw UnexpectedEnd();
            offset += read;
            count -= read;
        }
    }

    private static CabinetFormatException UnexpectedEnd() =>
        new("Unexpected end of cabinet data.");
}
