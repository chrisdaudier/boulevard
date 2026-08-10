using System.Buffers.Binary;

namespace Boulevard.Protocol.Itch;

/// <summary>ITCH 5.0 "Stock Directory" message, type 'R' - resolves StockLocate to a real ticker.</summary>
public readonly ref struct StockDirectoryMessage
{
    public const byte MessageType = (byte)'R';
    private const int WireLength = 39;

    public ushort StockLocate { get; }
    public ushort TrackingNumber { get; }
    public ulong TimestampNanoseconds { get; }

    /// <summary>8-byte space-padded ASCII ticker.</summary>
    public ReadOnlySpan<byte> Stock { get; }

    public byte MarketCategory { get; }
    public byte FinancialStatusIndicator { get; }
    public uint RoundLotSize { get; }
    public byte RoundLotsOnly { get; }
    public byte IssueClassification { get; }
    public ReadOnlySpan<byte> IssueSubType { get; }
    public byte Authenticity { get; }
    public byte ShortSaleThresholdIndicator { get; }
    public byte IpoFlag { get; }
    public byte LuldReferencePriceTier { get; }
    public byte EtpFlag { get; }
    public uint EtpLeverageFactor { get; }
    public byte InverseIndicator { get; }

    private StockDirectoryMessage(
        ushort stockLocate,
        ushort trackingNumber,
        ulong timestampNanoseconds,
        ReadOnlySpan<byte> stock,
        byte marketCategory,
        byte financialStatusIndicator,
        uint roundLotSize,
        byte roundLotsOnly,
        byte issueClassification,
        ReadOnlySpan<byte> issueSubType,
        byte authenticity,
        byte shortSaleThresholdIndicator,
        byte ipoFlag,
        byte luldReferencePriceTier,
        byte etpFlag,
        uint etpLeverageFactor,
        byte inverseIndicator)
    {
        StockLocate = stockLocate;
        TrackingNumber = trackingNumber;
        TimestampNanoseconds = timestampNanoseconds;
        Stock = stock;
        MarketCategory = marketCategory;
        FinancialStatusIndicator = financialStatusIndicator;
        RoundLotSize = roundLotSize;
        RoundLotsOnly = roundLotsOnly;
        IssueClassification = issueClassification;
        IssueSubType = issueSubType;
        Authenticity = authenticity;
        ShortSaleThresholdIndicator = shortSaleThresholdIndicator;
        IpoFlag = ipoFlag;
        LuldReferencePriceTier = luldReferencePriceTier;
        EtpFlag = etpFlag;
        EtpLeverageFactor = etpLeverageFactor;
        InverseIndicator = inverseIndicator;
    }

    public static bool TryParse(ReadOnlySpan<byte> data, out StockDirectoryMessage message)
    {
        if (data.Length != WireLength || data[0] != MessageType)
        {
            message = default;
            return false;
        }

        message = new StockDirectoryMessage(
            stockLocate: BinaryPrimitives.ReadUInt16BigEndian(data.Slice(1, 2)),
            trackingNumber: BinaryPrimitives.ReadUInt16BigEndian(data.Slice(3, 2)),
            timestampNanoseconds: ItchBinary.ReadUInt48BigEndian(data.Slice(5, 6)),
            stock: data.Slice(11, 8),
            marketCategory: data[19],
            financialStatusIndicator: data[20],
            roundLotSize: BinaryPrimitives.ReadUInt32BigEndian(data.Slice(21, 4)),
            roundLotsOnly: data[25],
            issueClassification: data[26],
            issueSubType: data.Slice(27, 2),
            authenticity: data[29],
            shortSaleThresholdIndicator: data[30],
            ipoFlag: data[31],
            luldReferencePriceTier: data[32],
            etpFlag: data[33],
            etpLeverageFactor: BinaryPrimitives.ReadUInt32BigEndian(data.Slice(34, 4)),
            inverseIndicator: data[38]);
        return true;
    }
}
