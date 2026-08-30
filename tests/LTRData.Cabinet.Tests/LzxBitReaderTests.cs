using LTRData.Cabinet.Internal;
using Xunit;

namespace LTRData.Cabinet.Tests;

public sealed class LzxBitReaderTests
{
    [Fact]
    public void ReadsSeventeenBitOffsetFooter()
    {
        byte[] source = [0xFF, 0xFF, 0x00, 0x80];
        var reader = new LzxBitReader(source);

        Assert.True(reader.TryReadBits(17, out uint footer));
        Assert.Equal(0x1FFFFu, footer);
        Assert.True(reader.TryReadBits(15, out uint remaining));
        Assert.Equal(0u, remaining);
    }
}
