namespace Boulevard.Protocol.Fix;

/// <summary>The BeginString (tag 8) values this codec accepts.</summary>
internal static class FixVersion
{
    public static bool IsSupported(ReadOnlySpan<byte> beginString) =>
        beginString.SequenceEqual("FIX.4.2"u8) || beginString.SequenceEqual("FIX.4.4"u8);
}
