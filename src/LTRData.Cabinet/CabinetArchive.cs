using LTRData.Cabinet.Internal;

namespace LTRData.Cabinet;

/// <summary>Represents a parsed Microsoft Cabinet archive.</summary>
public sealed class CabinetArchive : IDisposable
{
    private readonly Stream originalStream;
    private readonly Stream backingStream;
    private readonly bool leaveOpen;
    private readonly string? temporaryPath;
    private bool disposed;

    private CabinetArchive(Stream originalStream, Stream backingStream,
        CabinetOptions options, string? temporaryPath, ParsedCabinet parsed)
    {
        this.originalStream = originalStream;
        this.backingStream = backingStream;
        leaveOpen = options.LeaveOpen;
        this.temporaryPath = temporaryPath;
        CabinetSize = parsed.CabinetSize;
        MajorVersion = parsed.MajorVersion;
        MinorVersion = parsed.MinorVersion;
        Flags = parsed.Flags;
        SetId = parsed.SetId;
        CabinetIndex = parsed.CabinetIndex;
        DataReserveSize = parsed.DataReserveSize;
        ReservedData = parsed.ReservedData;
        PreviousCabinet = parsed.PreviousCabinet;
        PreviousDisk = parsed.PreviousDisk;
        NextCabinet = parsed.NextCabinet;
        NextDisk = parsed.NextDisk;
        Folders = Array.AsReadOnly(parsed.Folders);
        Files = Array.AsReadOnly(parsed.Files);
    }

    public uint CabinetSize { get; }
    public byte MajorVersion { get; }
    public byte MinorVersion { get; }
    public ushort Flags { get; }
    public ushort SetId { get; }
    public ushort CabinetIndex { get; }
    public byte DataReserveSize { get; }
    public ReadOnlyMemory<byte> ReservedData { get; }
    public string? PreviousCabinet { get; }
    public string? PreviousDisk { get; }
    public string? NextCabinet { get; }
    public string? NextDisk { get; }
    public IReadOnlyList<CabinetFolder> Folders { get; }
    public IReadOnlyList<CabinetFile> Files { get; }

    public static CabinetArchive Open(Stream stream, CabinetOptions? options = null)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(stream);
#else
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }
#endif

        if (!stream.CanRead)
        {
            throw new ArgumentException("The stream must be readable.", nameof(stream));
        }

        options ??= new CabinetOptions();
        ValidateOptions(options);

        Stream? backing = null;
        string? path = null;
        try
        {
            backing = SeekableInput.Create(stream, options, out path);
            ParsedCabinet parsed = CabinetParser.Parse(backing);
            return new CabinetArchive(stream, backing, options, path, parsed);
        }
        catch
        {
            if (backing is not null && !ReferenceEquals(backing, stream))
            {
                backing.Dispose();
            }

            DeleteTemporary(path);
            throw;
        }
    }

    public static async Task<CabinetArchive> OpenAsync(Stream stream,
        CabinetOptions? options = null, CancellationToken cancellationToken = default)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(stream);
#else
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }
#endif

        if (!stream.CanRead)
        {
            throw new ArgumentException("The stream must be readable.", nameof(stream));
        }

        options ??= new CabinetOptions();
        ValidateOptions(options);

        Stream? backing = null;
        string? path = null;
        try
        {
            (backing, path) = await SeekableInput.CreateAsync(stream, options, cancellationToken)
                .ConfigureAwait(false);
            ParsedCabinet parsed = CabinetParser.Parse(backing);
            return new CabinetArchive(stream, backing, options, path, parsed);
        }
        catch
        {
            if (backing is not null && !ReferenceEquals(backing, stream))
            {
                backing.Dispose();
            }

            DeleteTemporary(path);
            throw;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (!ReferenceEquals(backingStream, originalStream))
        {
            backingStream.Dispose();
        }

        if (!leaveOpen)
        {
            originalStream.Dispose();
        }

        DeleteTemporary(temporaryPath);
    }

    private static void ValidateOptions(CabinetOptions options)
    {
        if (options.MemoryBufferThreshold < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Invalid memory buffer threshold.");
        }
    }

    private static void DeleteTemporary(string? path)
    {
        if (path is null)
        {
            return;
        }

        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
