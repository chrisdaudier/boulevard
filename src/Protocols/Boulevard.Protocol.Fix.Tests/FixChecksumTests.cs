using System.Text;

namespace Boulevard.Protocol.Fix.Tests;

public class FixChecksumTests
{
    [Fact]
    public void Compute_SumsBytesAndWrapsMod256()
    {
        // 130 bytes of value 250 sum to 32,500, which mod 256 is 244.
        byte[] bytes = new byte[130];
        Array.Fill(bytes, (byte)250);

        byte checksum = FixChecksum.Compute(bytes);

        Assert.Equal(244, checksum);
    }

    [Fact]
    public void Compute_EmptySpanIsZero()
    {
        Assert.Equal(0, FixChecksum.Compute(ReadOnlySpan<byte>.Empty));
    }

    [Theory]
    [InlineData(0, "000")]
    [InlineData(7, "007")]
    [InlineData(255, "255")]
    public void TryFormat_ZeroPadsToThreeDigits(byte checksum, string expected)
    {
        Span<byte> destination = stackalloc byte[3];

        Assert.True(FixChecksum.TryFormat(checksum, destination));
        Assert.Equal(expected, Encoding.ASCII.GetString(destination));
    }
}
