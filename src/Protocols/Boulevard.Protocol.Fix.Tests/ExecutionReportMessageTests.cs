namespace Boulevard.Protocol.Fix.Tests;

public class ExecutionReportMessageTests
{
    // Header tag order matches FixMessageWriter's own emission order - see the comment in
    // LogonMessageTests for why.
    private const string Body =
        "35=8|34=3|49=SENDER|56=TARGET|52=20260815-12:30:00.123|" +
        "37=ORDER1|11=CLORD1|17=EXEC1|150=0|39=0|55=AAPL|54=1|151=100|14=0|6=0|";

    [Fact]
    public void RoundTrips_ByteForByte()
    {
        byte[] raw = FixTestFixtures.Build("FIX.4.4", Body);

        Assert.True(ExecutionReportMessage.TryParse(raw, out ExecutionReportMessage message));
        Assert.Equal("ORDER1"u8.ToArray(), message.OrderId.ToArray());
        Assert.Equal("AAPL"u8.ToArray(), message.Symbol.ToArray());
        Assert.Equal(100m, message.LeavesQty);
        Assert.Equal(0m, message.CumQty);

        Span<byte> buffer = new byte[512];
        Span<byte> written = message.WriteTo(buffer);
        Assert.Equal(raw, written.ToArray());
    }

    [Fact]
    public void RejectsCorruptedChecksum()
    {
        byte[] raw = FixTestFixtures.Build("FIX.4.4", Body);
        raw[^2] ^= 0xFF;

        Assert.False(ExecutionReportMessage.TryParse(raw, out _));
    }

    [Fact]
    public void RejectsCorruptedBodyLength()
    {
        byte[] raw = FixTestFixtures.Build("FIX.4.4", Body);
        int bodyLengthDigitIndex = Array.IndexOf(raw, (byte)'9') + 2;
        raw[bodyLengthDigitIndex] += 1;

        Assert.False(ExecutionReportMessage.TryParse(raw, out _));
    }

    [Fact]
    public void RejectsMissingRequiredTag()
    {
        string bodyMissingExecId = Body.Replace("17=EXEC1|", "");
        byte[] raw = FixTestFixtures.Build("FIX.4.4", bodyMissingExecId);

        Assert.False(ExecutionReportMessage.TryParse(raw, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void DoesNotThrowOnTruncatedInput(int length)
    {
        byte[] raw = FixTestFixtures.Build("FIX.4.4", Body);
        byte[] truncated = raw[..Math.Min(length, raw.Length)];

        Exception? exception = Record.Exception(() => ExecutionReportMessage.TryParse(truncated, out _));

        Assert.Null(exception);
        Assert.False(ExecutionReportMessage.TryParse(truncated, out _));
    }
}
