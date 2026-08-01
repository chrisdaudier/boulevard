using System.Buffers.Binary;

namespace Boulevard.Protocol.Itch;

/// <summary>ITCH 5.0 "Order Executed" message, type 'E'.</summary>
public readonly ref struct OrderExecutedMessage
{
    public const byte MessageType = (byte)'E';
    private const int WireLength = 31;

    public ushort StockLocate { get; }
    public ushort TrackingNumber { get; }
    public ulong TimestampNanoseconds { get; }
    public ulong OrderReferenceNumber { get; }
    public uint ExecutedShares { get; }
    public ulong MatchNumber { get; }

    private OrderExecutedMessage(
        ushort stockLocate,
        ushort trackingNumber,
        ulong timestampNanoseconds,
        ulong orderReferenceNumber,
        uint executedShares,
        ulong matchNumber)
    {
        StockLocate = stockLocate;
        TrackingNumber = trackingNumber;
        TimestampNanoseconds = timestampNanoseconds;
        OrderReferenceNumber = orderReferenceNumber;
        ExecutedShares = executedShares;
        MatchNumber = matchNumber;
    }

    public static bool TryParse(ReadOnlySpan<byte> data, out OrderExecutedMessage message)
    {
        if (data.Length != WireLength || data[0] != MessageType)
        {
            message = default;
            return false;
        }

        message = new OrderExecutedMessage(
            stockLocate: BinaryPrimitives.ReadUInt16BigEndian(data.Slice(1, 2)),
            trackingNumber: BinaryPrimitives.ReadUInt16BigEndian(data.Slice(3, 2)),
            timestampNanoseconds: ItchBinary.ReadUInt48BigEndian(data.Slice(5, 6)),
            orderReferenceNumber: BinaryPrimitives.ReadUInt64BigEndian(data.Slice(11, 8)),
            executedShares: BinaryPrimitives.ReadUInt32BigEndian(data.Slice(19, 4)),
            matchNumber: BinaryPrimitives.ReadUInt64BigEndian(data.Slice(23, 8)));
        return true;
    }
}
