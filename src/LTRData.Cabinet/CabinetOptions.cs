namespace LTRData.Cabinet;

/// <summary>Controls cabinet input ownership and buffering.</summary>
public sealed class CabinetOptions
{
    /// <summary>Whether disposing the archive also disposes the input stream.</summary>
    public bool LeaveOpen { get; set; }

    /// <summary>
    /// Maximum number of bytes retained in memory while making a forward-only
    /// stream seekable. Larger inputs are copied to a temporary file.
    /// </summary>
    public int MemoryBufferThreshold { get; set; } = 4 * 1024 * 1024;

    /// <summary>Optional directory used for temporary backing files.</summary>
    public string? TemporaryDirectory { get; set; }
}
