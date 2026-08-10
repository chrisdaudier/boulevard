using System.Buffers.Binary;

namespace Boulevard.Protocol.Itch;

/// <summary>
/// ITCH 5.0 "Cross Trade" message, type 'Q' - a summary trade print for an auction cross
/// (opening, closing, halt/IPO, intraday). Unlike 'C', it carries no OrderReferenceNumber, so it
/// cannot be applied to a book directly; the individual participating orders are already removed
/// via their own 'C' (Order Executed With Price) messages. This message exists for tape/print
/// reporting only.
/// </summary>
public readonly ref struct CrossTradeMessage
{
    public const byte MessageType = (byte)'Q';
    private const int WireLength = 40;

    public ushort StockLocate { get; }
    public ushort TrackingNumber { get; }
    public ulong TimestampNanoseconds { get; }
    public ulong Shares { get; }
    public uint CrossPriceRaw { get; }
    public ulong MatchNumber { get; }
    public byte CrossType { get; }

    private CrossTradeMessage(
        ushort stockLocate,
        ushort trackingNumber,
        ulong timestampNanoseconds,
        ulong shares,
        uint crossPriceRaw,
        ulong matchNumber,
        byte crossType)
    {
        StockLocate = stockLocate;
        TrackingNumber = trackingNumber;
        TimestampNanoseconds = timestampNanoseconds;
        Shares = shares;
        CrossPriceRaw = crossPriceRaw;
        MatchNumber = matchNumber;
        CrossType = crossType;
    }

    public static bool TryParse(ReadOnlySpan<byte> data, out CrossTradeMessage message)
    {
        if (data.Length != WireLength || data[0] != MessageType)
        {
            message = default;
            return false;
        }

        message = new CrossTradeMessage(
            stockLocate: BinaryPrimitives.ReadUInt16BigEndian(data.Slice(1, 2)),
            trackingNumber: BinaryPrimitives.ReadUInt16BigEndian(data.Slice(3, 2)),
            timestampNanoseconds: ItchBinary.ReadUInt48BigEndian(data.Slice(5, 6)),
            shares: BinaryPrimitives.ReadUInt64BigEndian(data.Slice(11, 8)),
            crossPriceRaw: BinaryPrimitives.ReadUInt32BigEndian(data.Slice(27, 4)),
            matchNumber: BinaryPrimitives.ReadUInt64BigEndian(data.Slice(31, 8)),
            crossType: data[39]);
        return true;
    }
}
