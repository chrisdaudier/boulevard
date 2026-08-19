using System.Buffers.Text;

namespace Boulevard.Protocol.Fix;

/// <summary>
/// Zero-allocation ASCII-decimal field decoding off ReadOnlySpan&lt;byte&gt; - the FIX-specific twin
/// of Boulevard.Protocol.Itch's ItchBinary, decoding tag=value text fields instead of big-endian
/// binary ones.
/// </summary>
internal static class FixValueParser
{
    public static bool TryParseInt32(ReadOnlySpan<byte> value, out int result)
    {
        // Utf8Parser stops at the first non-numeric byte rather than failing the whole span, so a
        // short match (trailing garbage after a valid-looking prefix) must be rejected explicitly -
        // it's not sufficient on its own to prove the entire field was numeric.
        return Utf8Parser.TryParse(value, out result, out int bytesConsumed) && bytesConsumed == value.Length;
    }

    public static bool TryParseInt64(ReadOnlySpan<byte> value, out long result)
    {
        return Utf8Parser.TryParse(value, out result, out int bytesConsumed) && bytesConsumed == value.Length;
    }

    public static bool TryParseDecimal(ReadOnlySpan<byte> value, out decimal result)
    {
        return Utf8Parser.TryParse(value, out result, out int bytesConsumed) && bytesConsumed == value.Length;
    }

    /// <summary>FIX UTCTimestamp, millisecond precision: YYYYMMDD-HH:MM:SS.sss, always exactly 21 bytes.</summary>
    public static bool TryParseUtcTimestamp(ReadOnlySpan<byte> value, out DateTime timestamp)
    {
        const int expectedLength = 21;
        if (value.Length != expectedLength
            || value[8] != (byte)'-'
            || value[11] != (byte)':'
            || value[14] != (byte)':'
            || value[17] != (byte)'.')
        {
            timestamp = default;
            return false;
        }

        if (!TryParseInt32(value[..4], out int year)
            || !TryParseInt32(value[4..6], out int month)
            || !TryParseInt32(value[6..8], out int day)
            || !TryParseInt32(value[9..11], out int hour)
            || !TryParseInt32(value[12..14], out int minute)
            || !TryParseInt32(value[15..17], out int second)
            || !TryParseInt32(value[18..21], out int millisecond))
        {
            timestamp = default;
            return false;
        }

        try
        {
            timestamp = new DateTime(year, month, day, hour, minute, second, millisecond, DateTimeKind.Utc);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            // Every sub-field parsed as a digit string fine, but the calendar date doesn't exist
            // (e.g. month 13, day 32) - a data-quality issue, not a precondition violation.
            timestamp = default;
            return false;
        }
    }
}
