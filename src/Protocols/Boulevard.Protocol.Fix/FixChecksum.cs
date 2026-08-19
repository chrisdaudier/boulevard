using System.Buffers;
using System.Buffers.Text;

namespace Boulevard.Protocol.Fix;

/// <summary>FIX CheckSum (tag 10): sum of all preceding bytes mod 256, written as 3 zero-padded digits.</summary>
internal static class FixChecksum
{
    public static byte Compute(ReadOnlySpan<byte> bytes)
    {
        byte sum = 0;
        foreach (byte b in bytes)
        {
            sum += b; // a byte accumulator wraps mod 256 on overflow for free - that IS the FIX definition, not a bug
        }

        return sum;
    }

    /// <summary>Writes exactly 3 ASCII digits (zero-padded) to <paramref name="destination"/>.</summary>
    public static bool TryFormat(byte checksum, Span<byte> destination)
    {
        return Utf8Formatter.TryFormat(checksum, destination, out int written, new StandardFormat('D', 3)) && written == 3;
    }
}
