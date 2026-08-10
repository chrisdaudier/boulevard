using System.Buffers.Binary;

namespace Boulevard.Protocol.Itch;

/// <summary>
/// ITCH 5.0 "Order Executed With Price" message, type 'C' - same book effect as a plain Order
/// Executed ('E'), but carries an explicit execution price because it differs from the order's
/// displayed price. This is how auction/cross fills (opening, closing, halt) are reported: every
/// participating order clears at the single cross price, not its own limit price, so crosses are
/// reported via 'C' rather than 'E'.
/// </summary>
public readonly ref struct OrderExecutedWithPriceMessage
{
    public const byte MessageType = (byte)'C';
    private const int WireLength = 36;

    public ushort StockLocate { get; }
    public ushort TrackingNumber { get; }
    public ulong TimestampNanoseconds { get; }
    public ulong OrderReferenceNumber { get; }
    public uint ExecutedShares { get; }
    public ulong MatchNumber { get; }
    public bool Printable { get; }
    public uint ExecutionPriceRaw { get; }

    private OrderExecutedWithPriceMessage(
        ushort stockLocate,
        ushort trackingNumber,
        ulong timestampNanoseconds,
        ulong orderReferenceNumber,
        uint executedShares,
        ulong matchNumber,
        bool printable,
        uint executionPriceRaw)
    {
        StockLocate = stockLocate;
        TrackingNumber = trackingNumber;
        TimestampNanoseconds = timestampNanoseconds;
        OrderReferenceNumber = orderReferenceNumber;
        ExecutedShares = executedShares;
        MatchNumber = matchNumber;
        Printable = printable;
        ExecutionPriceRaw = executionPriceRaw;
    }

    public static bool TryParse(ReadOnlySpan<byte> data, out OrderExecutedWithPriceMessage message)
    {
        if (data.Length != WireLength || data[0] != MessageType)
        {
            message = default;
            return false;
        }

        message = new OrderExecutedWithPriceMessage(
            stockLocate: BinaryPrimitives.ReadUInt16BigEndian(data.Slice(1, 2)),
            trackingNumber: BinaryPrimitives.ReadUInt16BigEndian(data.Slice(3, 2)),
            timestampNanoseconds: ItchBinary.ReadUInt48BigEndian(data.Slice(5, 6)),
            orderReferenceNumber: BinaryPrimitives.ReadUInt64BigEndian(data.Slice(11, 8)),
            executedShares: BinaryPrimitives.ReadUInt32BigEndian(data.Slice(19, 4)),
            matchNumber: BinaryPrimitives.ReadUInt64BigEndian(data.Slice(23, 8)),
            printable: data[31] == (byte)'Y',
            executionPriceRaw: BinaryPrimitives.ReadUInt32BigEndian(data.Slice(32, 4)));
        return true;
    }
}
