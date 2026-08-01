using System.Buffers.Binary;

namespace Boulevard.Protocol.Itch;

/// <summary>ITCH 5.0 "Order Cancel" message, type 'X'.</summary>
public readonly ref struct OrderCancelMessage
{
    public const byte MessageType = (byte)'X';
    private const int WireLength = 23;

    public ushort StockLocate { get; }
    public ushort TrackingNumber { get; }
    public ulong TimestampNanoseconds { get; }
    public ulong OrderReferenceNumber { get; }
    public uint CanceledShares { get; }

    private OrderCancelMessage(
        ushort stockLocate,
        ushort trackingNumber,
        ulong timestampNanoseconds,
        ulong orderReferenceNumber,
        uint canceledShares)
    {
        StockLocate = stockLocate;
        TrackingNumber = trackingNumber;
        TimestampNanoseconds = timestampNanoseconds;
        OrderReferenceNumber = orderReferenceNumber;
        CanceledShares = canceledShares;
    }

    public static bool TryParse(ReadOnlySpan<byte> data, out OrderCancelMessage message)
    {
        if (data.Length != WireLength || data[0] != MessageType)
        {
            message = default;
            return false;
        }

        message = new OrderCancelMessage(
            stockLocate: BinaryPrimitives.ReadUInt16BigEndian(data.Slice(1, 2)),
            trackingNumber: BinaryPrimitives.ReadUInt16BigEndian(data.Slice(3, 2)),
            timestampNanoseconds: ItchBinary.ReadUInt48BigEndian(data.Slice(5, 6)),
            orderReferenceNumber: BinaryPrimitives.ReadUInt64BigEndian(data.Slice(11, 8)),
            canceledShares: BinaryPrimitives.ReadUInt32BigEndian(data.Slice(19, 4)));
        return true;
    }
}
