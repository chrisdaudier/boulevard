using System.Buffers.Binary;

namespace Boulevard.Protocol.Itch;

/// <summary>ITCH 5.0 "Add Order (No MPID Attribution)" message, type 'A'.</summary>
public readonly ref struct AddOrderMessage
{
    public const byte MessageType = (byte)'A';
    private const int WireLength = 36;

    public ushort StockLocate { get; }
    public ushort TrackingNumber { get; }
    public ulong TimestampNanoseconds { get; }
    public ulong OrderReferenceNumber { get; }
    public byte BuySellIndicator { get; }
    public uint Shares { get; }
    public ReadOnlySpan<byte> Stock { get; }

    /// <summary>Raw wire price, implied 4 decimal places (e.g. 150000 = $15.0000).</summary>
    public uint PriceRaw { get; }

    public bool IsBuy => BuySellIndicator == (byte)'B';

    private AddOrderMessage(
        ushort stockLocate,
        ushort trackingNumber,
        ulong timestampNanoseconds,
        ulong orderReferenceNumber,
        byte buySellIndicator,
        uint shares,
        ReadOnlySpan<byte> stock,
        uint priceRaw)
    {
        StockLocate = stockLocate;
        TrackingNumber = trackingNumber;
        TimestampNanoseconds = timestampNanoseconds;
        OrderReferenceNumber = orderReferenceNumber;
        BuySellIndicator = buySellIndicator;
        Shares = shares;
        Stock = stock;
        PriceRaw = priceRaw;
    }

    public static bool TryParse(ReadOnlySpan<byte> data, out AddOrderMessage message)
    {
        if (data.Length != WireLength || data[0] != MessageType)
        {
            message = default;
            return false;
        }

        message = new AddOrderMessage(
            stockLocate: BinaryPrimitives.ReadUInt16BigEndian(data.Slice(1, 2)),
            trackingNumber: BinaryPrimitives.ReadUInt16BigEndian(data.Slice(3, 2)),
            timestampNanoseconds: ItchBinary.ReadUInt48BigEndian(data.Slice(5, 6)),
            orderReferenceNumber: BinaryPrimitives.ReadUInt64BigEndian(data.Slice(11, 8)),
            buySellIndicator: data[19],
            shares: BinaryPrimitives.ReadUInt32BigEndian(data.Slice(20, 4)),
            stock: data.Slice(24, 8),
            priceRaw: BinaryPrimitives.ReadUInt32BigEndian(data.Slice(32, 4)));
        return true;
    }
}
