using System.Buffers.Binary;

namespace Boulevard.Protocol.Itch;

/// <summary>
/// ITCH 5.0 "Order Replace" message, type 'U' - atomically retires an existing resting order and
/// adds its replacement (new reference number, new price/size, same side) in one message. Used
/// heavily by market makers continuously refreshing a quote; without handling it, a replaced
/// order's original reference number never receives a Delete/Cancel of its own (the exchange's
/// book already dropped it via the replace), so it would sit resting in a naive book forever.
/// </summary>
public readonly ref struct OrderReplaceMessage
{
    public const byte MessageType = (byte)'U';
    private const int WireLength = 35;

    public ushort StockLocate { get; }
    public ushort TrackingNumber { get; }
    public ulong TimestampNanoseconds { get; }
    public ulong OriginalOrderReferenceNumber { get; }
    public ulong NewOrderReferenceNumber { get; }
    public uint Shares { get; }
    public uint PriceRaw { get; }

    private OrderReplaceMessage(
        ushort stockLocate,
        ushort trackingNumber,
        ulong timestampNanoseconds,
        ulong originalOrderReferenceNumber,
        ulong newOrderReferenceNumber,
        uint shares,
        uint priceRaw)
    {
        StockLocate = stockLocate;
        TrackingNumber = trackingNumber;
        TimestampNanoseconds = timestampNanoseconds;
        OriginalOrderReferenceNumber = originalOrderReferenceNumber;
        NewOrderReferenceNumber = newOrderReferenceNumber;
        Shares = shares;
        PriceRaw = priceRaw;
    }

    public static bool TryParse(ReadOnlySpan<byte> data, out OrderReplaceMessage message)
    {
        if (data.Length != WireLength || data[0] != MessageType)
        {
            message = default;
            return false;
        }

        message = new OrderReplaceMessage(
            stockLocate: BinaryPrimitives.ReadUInt16BigEndian(data.Slice(1, 2)),
            trackingNumber: BinaryPrimitives.ReadUInt16BigEndian(data.Slice(3, 2)),
            timestampNanoseconds: ItchBinary.ReadUInt48BigEndian(data.Slice(5, 6)),
            originalOrderReferenceNumber: BinaryPrimitives.ReadUInt64BigEndian(data.Slice(11, 8)),
            newOrderReferenceNumber: BinaryPrimitives.ReadUInt64BigEndian(data.Slice(19, 8)),
            shares: BinaryPrimitives.ReadUInt32BigEndian(data.Slice(27, 4)),
            priceRaw: BinaryPrimitives.ReadUInt32BigEndian(data.Slice(31, 4)));
        return true;
    }
}
