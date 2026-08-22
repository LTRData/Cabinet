namespace LTRData.Cabinet.Internal;

/// <summary>Limits a decoded folder stream to one CFFILE range.</summary>
internal sealed class CabinetFileStream : Stream
{
    private readonly Stream folderStream;
    private readonly long length;
    private long skipRemaining;
    private long position;
    private bool disposed;

    internal CabinetFileStream(Stream folderStream, uint folderOffset, uint length)
    {
        this.folderStream = folderStream;
        skipRemaining = folderOffset;
        this.length = length;
    }

    public override bool CanRead => !disposed;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => length;

    public override long Position
    {
        get => position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        ThrowIfDisposed();
        SkipToFile();

        int requested = (int)Math.Min(count, length - position);
        if (requested == 0)
        {
            return 0;
        }

        int read = folderStream.Read(buffer, offset, requested);
        if (read == 0)
        {
            throw new CabinetFormatException("The cabinet folder ended before the file was complete.");
        }

        position += read;
        return read;
    }

    public override int ReadByte()
    {
        var buffer = new byte[1];
        return Read(buffer, 0, 1) == 0 ? -1 : buffer[0];
    }

    public override void Flush() { }

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (!disposed && disposing)
        {
            folderStream.Dispose();
        }

        disposed = true;
        base.Dispose(disposing);
    }

    private void SkipToFile()
    {
        if (skipRemaining == 0)
        {
            return;
        }

        var scratch = new byte[81920];
        while (skipRemaining > 0)
        {
            int requested = (int)Math.Min(skipRemaining, scratch.Length);
            int read = folderStream.Read(scratch, 0, requested);
            if (read == 0)
            {
                throw new CabinetFormatException("The cabinet folder ended before the file offset.");
            }

            skipRemaining -= read;
        }
    }

    private static void ValidateBufferArguments(byte[] buffer, int offset, int count)
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
            throw new ObjectDisposedException(nameof(CabinetFileStream));
        }
#endif
    }
}
