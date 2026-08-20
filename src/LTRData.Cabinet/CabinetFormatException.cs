using System.IO;

namespace LTRData.Cabinet;

/// <summary>Thrown when cabinet data is malformed or unsupported.</summary>
public sealed class CabinetFormatException : InvalidDataException
{
    public CabinetFormatException(string message) : base(message) { }

    public CabinetFormatException(string message, Exception innerException)
        : base(message, innerException) { }
}
