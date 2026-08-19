namespace Boulevard.Protocol.Fix.Tests;

public class NewOrderSingleMessageTests
{
    // Header tag order matches FixMessageWriter's own emission order - see the comment in
    // LogonMessageTests for why.
    private const string Body =
        "35=D|34=2|49=SENDER|56=TARGET|52=20260815-12:30:00.123|" +
        "11=CLORD1|55=AAPL|54=1|38=100|40=2|44=150.25|60=20260815-12:30:00.000|";

    [Fact]
    public void RoundTrips_ByteForByte()
    {
        byte[] raw = FixTestFixtures.Build("FIX.4.2", Body);

        Assert.True(NewOrderSingleMessage.TryParse(raw, out NewOrderSingleMessage message));
        Assert.Equal("AAPL"u8.ToArray(), message.Symbol.ToArray());
        Assert.True(message.IsBuy);
        Assert.Equal(100m, message.OrderQty);
        Assert.Equal(150.25m, message.Price);

        Span<byte> buffer = new byte[512];
        Span<byte> written = message.WriteTo(buffer);
        Assert.Equal(raw, written.ToArray());
    }

    [Fact]
    public void DecodesSellSide()
    {
        string sellBody = Body.Replace("54=1", "54=2");
        byte[] raw = FixTestFixtures.Build("FIX.4.2", sellBody);

        Assert.True(NewOrderSingleMessage.TryParse(raw, out NewOrderSingleMessage message));
        Assert.False(message.IsBuy);
        Assert.Equal((byte)'2', message.Side);
    }

    [Fact]
    public void RejectsCorruptedChecksum()
    {
        byte[] raw = FixTestFixtures.Build("FIX.4.2", Body);
        raw[^2] ^= 0xFF;

        Assert.False(NewOrderSingleMessage.TryParse(raw, out _));
    }

    [Fact]
    public void RejectsCorruptedBodyLength()
    {
        byte[] raw = FixTestFixtures.Build("FIX.4.2", Body);
        int bodyLengthDigitIndex = Array.IndexOf(raw, (byte)'9') + 2;
        raw[bodyLengthDigitIndex] += 1;

        Assert.False(NewOrderSingleMessage.TryParse(raw, out _));
    }

    [Fact]
    public void RejectsMissingRequiredTag()
    {
        string bodyMissingPrice = Body.Replace("44=150.25|", "");
        byte[] raw = FixTestFixtures.Build("FIX.4.2", bodyMissingPrice);

        Assert.False(NewOrderSingleMessage.TryParse(raw, out _));
    }

    [Fact]
    public void RejectsGarbagePrice()
    {
        string bodyGarbagePrice = Body.Replace("44=150.25|", "44=abc|");
        byte[] raw = FixTestFixtures.Build("FIX.4.2", bodyGarbagePrice);

        Assert.False(NewOrderSingleMessage.TryParse(raw, out _));
    }

    [Fact]
    public void RejectsTrailingGarbageOnNumericField()
    {
        // Utf8Parser stops at the first non-numeric byte rather than failing the whole span - a
        // trailing-garbage price like "150.25x" must still be rejected, not silently truncated.
        string bodyTrailingGarbage = Body.Replace("44=150.25|", "44=150.25x|");
        byte[] raw = FixTestFixtures.Build("FIX.4.2", bodyTrailingGarbage);

        Assert.False(NewOrderSingleMessage.TryParse(raw, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(30)]
    public void DoesNotThrowOnTruncatedInput(int length)
    {
        byte[] raw = FixTestFixtures.Build("FIX.4.2", Body);
        byte[] truncated = raw[..Math.Min(length, raw.Length)];

        Exception? exception = Record.Exception(() => NewOrderSingleMessage.TryParse(truncated, out _));

        Assert.Null(exception);
        Assert.False(NewOrderSingleMessage.TryParse(truncated, out _));
    }
}
