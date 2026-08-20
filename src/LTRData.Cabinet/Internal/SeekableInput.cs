namespace LTRData.Cabinet.Internal;

internal static class SeekableInput
{
    internal static Stream Create(Stream input, CabinetOptions options, out string? temporaryPath)
    {
        temporaryPath = null;
        if (input.CanSeek)
        {
            return input;
        }

        var memory = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
        {
            if (memory.Length + read <= options.MemoryBufferThreshold)
            {
                memory.Write(buffer, 0, read);
                continue;
            }

            temporaryPath = Path.Combine(options.TemporaryDirectory ?? Path.GetTempPath(),
                "ltrcab-" + Guid.NewGuid().ToString("N") + ".tmp");
            var file = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite,
                FileShare.None, 81920, FileOptions.SequentialScan);
            memory.Position = 0;
            memory.CopyTo(file);
            memory.Dispose();
            file.Write(buffer, 0, read);
            input.CopyTo(file);
            file.Position = 0;
            return file;
        }

        memory.Position = 0;
        return memory;
    }

    internal static async Task<(Stream Stream, string? TemporaryPath)> CreateAsync(
        Stream input, CabinetOptions options, CancellationToken cancellationToken)
    {
        if (input.CanSeek)
        {
            return (input, null);
        }

        var memory = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            int read = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                memory.Position = 0;
                return (memory, null);
            }

            if (memory.Length + read <= options.MemoryBufferThreshold)
            {
                await memory.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                continue;
            }

            string path = Path.Combine(options.TemporaryDirectory ?? Path.GetTempPath(),
                "ltrcab-" + Guid.NewGuid().ToString("N") + ".tmp");
            var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite,
                FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            memory.Position = 0;
            await memory.CopyToAsync(file, 81920, cancellationToken).ConfigureAwait(false);
            memory.Dispose();
            await file.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
            await input.CopyToAsync(file, 81920, cancellationToken).ConfigureAwait(false);
            file.Position = 0;
            return (file, path);
        }
    }
}
