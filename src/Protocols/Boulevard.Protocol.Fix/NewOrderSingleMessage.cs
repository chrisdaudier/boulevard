namespace Boulevard.Protocol.Fix;

/// <summary>
/// FIX NewOrderSingle (MsgType 'D').
///
/// Like Boulevard.Protocol.Itch's message types, this is a `ref struct`: it can't be stored in a
/// class field, boxed, captured in a lambda/iterator that escapes, or held across an `await`.
/// Extract whatever primitives you need immediately if they must outlive this call.
/// </summary>
public readonly ref struct NewOrderSingleMessage
{
    public static ReadOnlySpan<byte> MsgType => "D"u8;

    public ReadOnlySpan<byte> BeginString { get; }
    public int MsgSeqNum { get; }
    public ReadOnlySpan<byte> SenderCompId { get; }
    public ReadOnlySpan<byte> TargetCompId { get; }
    public DateTime SendingTime { get; }

    public ReadOnlySpan<byte> ClOrdId { get; }
    public ReadOnlySpan<byte> Symbol { get; }

    /// <summary>Raw FIX Side ASCII char - '1' = Buy, '2' = Sell (see IsBuy).</summary>
    public byte Side { get; }

    public decimal OrderQty { get; }

    /// <summary>Raw FIX OrdType ASCII char - '1' = Market, '2' = Limit.</summary>
    public byte OrdType { get; }

    public decimal Price { get; }
    public DateTime TransactTime { get; }

    public bool IsBuy => Side == (byte)'1';

    private NewOrderSingleMessage(
        ReadOnlySpan<byte> beginString,
        int msgSeqNum,
        ReadOnlySpan<byte> senderCompId,
        ReadOnlySpan<byte> targetCompId,
        DateTime sendingTime,
        ReadOnlySpan<byte> clOrdId,
        ReadOnlySpan<byte> symbol,
        byte side,
        decimal orderQty,
        byte ordType,
        decimal price,
        DateTime transactTime)
    {
        BeginString = beginString;
        MsgSeqNum = msgSeqNum;
        SenderCompId = senderCompId;
        TargetCompId = targetCompId;
        SendingTime = sendingTime;
        ClOrdId = clOrdId;
        Symbol = symbol;
        Side = side;
        OrderQty = orderQty;
        OrdType = ordType;
        Price = price;
        TransactTime = transactTime;
    }

    public static bool TryParse(ReadOnlySpan<byte> rawMessage, out NewOrderSingleMessage message)
    {
        message = default;

        const uint requiredMask =
            1u << 0 | // BeginString
            1u << 1 | // BodyLength
            1u << 2 | // MsgType
            1u << 3 | // SenderCompId
            1u << 4 | // TargetCompId
            1u << 5 | // MsgSeqNum
            1u << 6 | // SendingTime
            1u << 7 | // CheckSum
            1u << 8 | // ClOrdId
            1u << 9 | // Symbol
            1u << 10 | // Side
            1u << 11 | // OrderQty
            1u << 12 | // OrdType
            1u << 13 | // Price
            1u << 14;  // TransactTime

        uint seenMask = 0;
        ReadOnlySpan<byte> beginString = default;
        int msgSeqNum = 0;
        ReadOnlySpan<byte> senderCompId = default;
        ReadOnlySpan<byte> targetCompId = default;
        DateTime sendingTime = default;
        ReadOnlySpan<byte> clOrdId = default;
        ReadOnlySpan<byte> symbol = default;
        byte side = 0;
        decimal orderQty = 0;
        byte ordType = 0;
        decimal price = 0;
        DateTime transactTime = default;

        int bodyLengthValue = 0;
        int bodyStart = -1;
        int checksumFieldStart = -1;
        int checksumValue = -1;
        int priorConsumed = 0;

        FixMessageReader.Enumerator enumerator = new FixMessageReader(rawMessage).GetEnumerator();
        while (enumerator.MoveNext())
        {
            FixField field = enumerator.Current;
            switch (field.Tag)
            {
                case FixTags.BeginString:
                    if (!FixVersion.IsSupported(field.Value))
                    {
                        return false;
                    }
                    beginString = field.Value;
                    seenMask |= 1u << 0;
                    break;
                case FixTags.BodyLength:
                    if (!FixValueParser.TryParseInt32(field.Value, out bodyLengthValue))
                    {
                        return false;
                    }
                    bodyStart = enumerator.ConsumedBytes;
                    seenMask |= 1u << 1;
                    break;
                case FixTags.MsgType:
                    if (!field.Value.SequenceEqual(MsgType))
                    {
                        return false;
                    }
                    seenMask |= 1u << 2;
                    break;
                case FixTags.SenderCompId:
                    senderCompId = field.Value;
                    seenMask |= 1u << 3;
                    break;
                case FixTags.TargetCompId:
                    targetCompId = field.Value;
                    seenMask |= 1u << 4;
                    break;
                case FixTags.MsgSeqNum:
                    if (!FixValueParser.TryParseInt32(field.Value, out msgSeqNum))
                    {
                        return false;
                    }
                    seenMask |= 1u << 5;
                    break;
                case FixTags.SendingTime:
                    if (!FixValueParser.TryParseUtcTimestamp(field.Value, out sendingTime))
                    {
                        return false;
                    }
                    seenMask |= 1u << 6;
                    break;
                case FixTags.CheckSum:
                    if (!FixValueParser.TryParseInt32(field.Value, out checksumValue))
                    {
                        return false;
                    }
                    checksumFieldStart = priorConsumed;
                    seenMask |= 1u << 7;
                    break;
                case FixTags.ClOrdId:
                    clOrdId = field.Value;
                    seenMask |= 1u << 8;
                    break;
                case FixTags.Symbol:
                    symbol = field.Value;
                    seenMask |= 1u << 9;
                    break;
                case FixTags.Side:
                    if (field.Value.Length != 1)
                    {
                        return false;
                    }
                    side = field.Value[0];
                    seenMask |= 1u << 10;
                    break;
                case FixTags.OrderQty:
                    if (!FixValueParser.TryParseDecimal(field.Value, out orderQty))
                    {
                        return false;
                    }
                    seenMask |= 1u << 11;
                    break;
                case FixTags.OrdType:
                    if (field.Value.Length != 1)
                    {
                        return false;
                    }
                    ordType = field.Value[0];
                    seenMask |= 1u << 12;
                    break;
                case FixTags.Price:
                    if (!FixValueParser.TryParseDecimal(field.Value, out price))
                    {
                        return false;
                    }
                    seenMask |= 1u << 13;
                    break;
                case FixTags.TransactTime:
                    if (!FixValueParser.TryParseUtcTimestamp(field.Value, out transactTime))
                    {
                        return false;
                    }
                    seenMask |= 1u << 14;
                    break;
            }

            priorConsumed = enumerator.ConsumedBytes;
        }

        if ((seenMask & requiredMask) != requiredMask || bodyStart < 0 || checksumFieldStart < 0)
        {
            return false;
        }

        if (checksumFieldStart - bodyStart != bodyLengthValue)
        {
            return false; // BodyLength doesn't match the actual body
        }

        if (FixChecksum.Compute(rawMessage[..checksumFieldStart]) != (byte)checksumValue)
        {
            return false; // CheckSum doesn't match
        }

        message = new NewOrderSingleMessage(
            beginString, msgSeqNum, senderCompId, targetCompId, sendingTime,
            clOrdId, symbol, side, orderQty, ordType, price, transactTime);
        return true;
    }

    /// <summary>Regenerates this message's bytes into <paramref name="buffer"/>, returning the written slice.</summary>
    public Span<byte> WriteTo(Span<byte> buffer)
    {
        FixMessageWriter writer = new(buffer);
        writer.WriteHeader(BeginString, MsgType, MsgSeqNum, SenderCompId, TargetCompId, SendingTime);
        writer.WriteTag(FixTags.ClOrdId, ClOrdId);
        writer.WriteTag(FixTags.Symbol, Symbol);
        writer.WriteTag(FixTags.Side, Side);
        writer.WriteTag(FixTags.OrderQty, OrderQty);
        writer.WriteTag(FixTags.OrdType, OrdType);
        writer.WriteTag(FixTags.Price, Price);
        writer.WriteUtcTimestampTag(FixTags.TransactTime, TransactTime);
        return writer.Finish();
    }
}
