using System.Buffers.Binary;

namespace Boulevard.Protocol.Itch;

/// <summary>ITCH 5.0 "Order Delete" message, type 'D' - removes an order's full remaining size (no shares field, unlike Cancel).</summary>
public readonly ref struct OrderDeleteMessage
{
    public const byte MessageType = (byte)'D';
    private const int WireLength = 19;

    public ushort StockLocate { get; }
    public ushort TrackingNumber { get; }
    public ulong TimestampNanoseconds { get; }
    public ulong OrderReferenceNumber { get; }

    private OrderDeleteMessage(
        ushort stockLocate,
        ushort trackingNumber,
        ulong timestampNanoseconds,
        ulong orderReferenceNumber)
    {
        StockLocate = stockLocate;
        TrackingNumber = trackingNumber;
        TimestampNanoseconds = timestampNanoseconds;
        OrderReferenceNumber = orderReferenceNumber;
    }

    public static bool TryParse(ReadOnlySpan<byte> data, out OrderDeleteMessage message)
    {
        if (data.Length != WireLength || data[0] != MessageType)
        {
            message = default;
            return false;
        }

        message = new OrderDeleteMessage(
            stockLocate: BinaryPrimitives.ReadUInt16BigEndian(data.Slice(1, 2)),
            trackingNumber: BinaryPrimitives.ReadUInt16BigEndian(data.Slice(3, 2)),
            timestampNanoseconds: ItchBinary.ReadUInt48BigEndian(data.Slice(5, 6)),
            orderReferenceNumber: BinaryPrimitives.ReadUInt64BigEndian(data.Slice(11, 8)));
        return true;
    }
}
