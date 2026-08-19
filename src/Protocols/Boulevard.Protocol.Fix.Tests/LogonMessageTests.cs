namespace Boulevard.Protocol.Fix.Tests;

public class LogonMessageTests
{
    // Tag order matches FixMessageWriter's own emission order (MsgType, MsgSeqNum, SenderCompId,
    // TargetCompId, SendingTime, then body tags) so the round-trip test compares canonical bytes
    // against canonical bytes - FIX doesn't mandate order beyond the first three/last tags, and
    // this codec's TryParse handles any order via its tag switch either way.
    private const string Body = "35=A|34=1|49=SENDER|56=TARGET|52=20260815-12:30:00.123|98=0|108=30|";

    [Theory]
    [InlineData("FIX.4.2")]
    [InlineData("FIX.4.4")]
    public void RoundTrips_ByteForByte(string beginString)
    {
        byte[] raw = FixTestFixtures.Build(beginString, Body);

        Assert.True(LogonMessage.TryParse(raw, out LogonMessage message));
        Assert.Equal(1, message.MsgSeqNum);
        Assert.Equal(0, message.EncryptMethod);
        Assert.Equal(30, message.HeartBtInt);

        Span<byte> buffer = new byte[512];
        Span<byte> written = message.WriteTo(buffer);
        Assert.Equal(raw, written.ToArray());
    }

    [Fact]
    public void RejectsCorruptedChecksum()
    {
        byte[] raw = FixTestFixtures.Build("FIX.4.2", Body);
        raw[^2] ^= 0xFF; // flip a digit inside the trailing "10=xxx\x01" checksum value

        Assert.False(LogonMessage.TryParse(raw, out _));
    }

    [Fact]
    public void RejectsCorruptedBodyLength()
    {
        byte[] raw = FixTestFixtures.Build("FIX.4.2", Body);
        int bodyLengthDigitIndex = Array.IndexOf(raw, (byte)'9') + 2; // just after "9="
        raw[bodyLengthDigitIndex] += 1;

        Assert.False(LogonMessage.TryParse(raw, out _));
    }

    [Fact]
    public void RejectsWrongMsgType()
    {
        byte[] raw = FixTestFixtures.Build("FIX.4.2", "35=D|49=SENDER|56=TARGET|34=1|52=20260815-12:30:00.123|98=0|108=30|");

        Assert.False(LogonMessage.TryParse(raw, out _));
    }

    [Fact]
    public void RejectsUnsupportedBeginString()
    {
        byte[] raw = FixTestFixtures.Build("FIX.5.0", Body);

        Assert.False(LogonMessage.TryParse(raw, out _));
    }

    [Fact]
    public void RejectsMissingRequiredTag()
    {
        byte[] raw = FixTestFixtures.Build("FIX.4.2", "35=A|49=SENDER|56=TARGET|34=1|52=20260815-12:30:00.123|98=0|");

        Assert.False(LogonMessage.TryParse(raw, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void DoesNotThrowOnTruncatedInput(int length)
    {
        byte[] raw = FixTestFixtures.Build("FIX.4.2", Body);
        byte[] truncated = raw[..Math.Min(length, raw.Length)];

        Exception? exception = Record.Exception(() => LogonMessage.TryParse(truncated, out _));

        Assert.Null(exception);
        Assert.False(LogonMessage.TryParse(truncated, out _));
    }

    [Fact]
    public void DoesNotThrowOnMissingFinalSoh()
    {
        byte[] raw = FixTestFixtures.Build("FIX.4.2", Body);
        byte[] unterminated = raw[..^1]; // drop the trailing SOH after the checksum field

        Exception? exception = Record.Exception(() => LogonMessage.TryParse(unterminated, out _));

        Assert.Null(exception);
        Assert.False(LogonMessage.TryParse(unterminated, out _));
    }
}
