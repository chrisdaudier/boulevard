namespace Boulevard.Protocol.Itch;

internal static class ItchBinary
{
    public static ulong ReadUInt48BigEndian(ReadOnlySpan<byte> sixBytes)
    {
        return ((ulong)sixBytes[0] << 40) | ((ulong)sixBytes[1] << 32) | ((ulong)sixBytes[2] << 24)
             | ((ulong)sixBytes[3] << 16) | ((ulong)sixBytes[4] << 8) | sixBytes[5];
    }
}
