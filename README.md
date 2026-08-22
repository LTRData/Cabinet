# LTRData.Cabinet

Managed, cross-platform reading of Microsoft Cabinet (`.cab`) archives.

The library is an independent implementation of the Cabinet format for
`netstandard2.0`, `net48`, and `net8.0`. The initial implementation is focused
on reading single-cabinet archives with uncompressed, MSZIP, and LZX folders.

```csharp
using var input = File.OpenRead("archive.cab");
using var cabinet = CabinetArchive.Open(input);

CabinetFile file = cabinet.Files.First(file => file.Name == "example.txt");
using Stream contents = file.Open();
contents.CopyTo(Console.OpenStandardOutput());
```

Entry streams are forward-only. The owning `CabinetArchive` must remain open
for the lifetime of every entry stream.

## Status

Early development. The public API is not yet stable. Uncompressed and MSZIP
folder data can be read; LZX support is still under development.

## License

MIT
