using System.Text;

namespace Boulevard.Protocol.Fix.Tests;

public class FixValueParserTests
{
    [Theory]
    [InlineData("123", true, 123)]
    [InlineData("0", true, 0)]
    [InlineData("", false, 0)]
    [InlineData("12a", false, 0)] // trailing garbage after a valid-looking prefix must be rejected
    [InlineData("-5", true, -5)]
    public void TryParseInt32_HandlesEdgeCases(string text, bool expectedSuccess, int expectedValue)
    {
        bool success = FixValueParser.TryParseInt32(Encoding.ASCII.GetBytes(text), out int result);

        Assert.Equal(expectedSuccess, success);
        if (expectedSuccess)
        {
            Assert.Equal(expectedValue, result);
        }
    }

    [Theory]
    [InlineData("150.25", true, "150.25")]
    [InlineData("0", true, "0")]
    [InlineData("150.25x", false, "0")] // trailing garbage
    [InlineData("abc", false, "0")]
    public void TryParseDecimal_HandlesEdgeCases(string text, bool expectedSuccess, string expectedValue)
    {
        bool success = FixValueParser.TryParseDecimal(Encoding.ASCII.GetBytes(text), out decimal result);

        Assert.Equal(expectedSuccess, success);
        if (expectedSuccess)
        {
            Assert.Equal(decimal.Parse(expectedValue), result);
        }
    }

    [Fact]
    public void TryParseUtcTimestamp_AcceptsValidTimestamp()
    {
        bool success = FixValueParser.TryParseUtcTimestamp(
            Encoding.ASCII.GetBytes("20260815-12:30:00.123"), out DateTime timestamp);

        Assert.True(success);
        Assert.Equal(new DateTime(2026, 8, 15, 12, 30, 0, 123, DateTimeKind.Utc), timestamp);
    }

    [Theory]
    [InlineData("20260815-12:30:00.12")] // one digit short
    [InlineData("2026-08-15 12:30:00.1")] // wrong separators entirely
    [InlineData("20261315-12:30:00.123")] // month 13 doesn't exist
    [InlineData("")]
    public void TryParseUtcTimestamp_RejectsInvalidInput(string text)
    {
        bool success = FixValueParser.TryParseUtcTimestamp(Encoding.ASCII.GetBytes(text), out _);

        Assert.False(success);
    }
}
