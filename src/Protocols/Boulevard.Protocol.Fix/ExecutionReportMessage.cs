namespace Boulevard.Protocol.Fix;

/// <summary>
/// FIX ExecutionReport (MsgType '8').
///
/// Like Boulevard.Protocol.Itch's message types, this is a `ref struct`: it can't be stored in a
/// class field, boxed, captured in a lambda/iterator that escapes, or held across an `await`.
/// Extract whatever primitives you need immediately if they must outlive this call.
/// </summary>
public readonly ref struct ExecutionReportMessage
{
    public static ReadOnlySpan<byte> MsgType => "8"u8;

    public ReadOnlySpan<byte> BeginString { get; }
    public int MsgSeqNum { get; }
    public ReadOnlySpan<byte> SenderCompId { get; }
    public ReadOnlySpan<byte> TargetCompId { get; }
    public DateTime SendingTime { get; }

    public ReadOnlySpan<byte> OrderId { get; }
    public ReadOnlySpan<byte> ClOrdId { get; }
    public ReadOnlySpan<byte> ExecId { get; }

    /// <summary>Raw FIX ExecType ASCII char - e.g. '0' = New, 'F' = Trade, '4' = Canceled.</summary>
    public byte ExecType { get; }

    /// <summary>Raw FIX OrdStatus ASCII char - e.g. '0' = New, '1' = PartiallyFilled, '2' = Filled.</summary>
    public byte OrdStatus { get; }

    public ReadOnlySpan<byte> Symbol { get; }

    /// <summary>Raw FIX Side ASCII char - '1' = Buy, '2' = Sell (see IsBuy).</summary>
    public byte Side { get; }

    public decimal LeavesQty { get; }
    public decimal CumQty { get; }
    public decimal AvgPx { get; }

    public bool IsBuy => Side == (byte)'1';

    private ExecutionReportMessage(
        ReadOnlySpan<byte> beginString,
        int msgSeqNum,
        ReadOnlySpan<byte> senderCompId,
        ReadOnlySpan<byte> targetCompId,
        DateTime sendingTime,
        ReadOnlySpan<byte> orderId,
        ReadOnlySpan<byte> clOrdId,
        ReadOnlySpan<byte> execId,
        byte execType,
        byte ordStatus,
        ReadOnlySpan<byte> symbol,
        byte side,
        decimal leavesQty,
        decimal cumQty,
        decimal avgPx)
    {
        BeginString = beginString;
        MsgSeqNum = msgSeqNum;
        SenderCompId = senderCompId;
        TargetCompId = targetCompId;
        SendingTime = sendingTime;
        OrderId = orderId;
        ClOrdId = clOrdId;
        ExecId = execId;
        ExecType = execType;
        OrdStatus = ordStatus;
        Symbol = symbol;
        Side = side;
        LeavesQty = leavesQty;
        CumQty = cumQty;
        AvgPx = avgPx;
    }

    public static bool TryParse(ReadOnlySpan<byte> rawMessage, out ExecutionReportMessage message)
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
            1u << 8 | // OrderId
            1u << 9 | // ClOrdId
            1u << 10 | // ExecId
            1u << 11 | // ExecType
            1u << 12 | // OrdStatus
            1u << 13 | // Symbol
            1u << 14 | // Side
            1u << 15 | // LeavesQty
            1u << 16 | // CumQty
            1u << 17;  // AvgPx

        uint seenMask = 0;
        ReadOnlySpan<byte> beginString = default;
        int msgSeqNum = 0;
        ReadOnlySpan<byte> senderCompId = default;
        ReadOnlySpan<byte> targetCompId = default;
        DateTime sendingTime = default;
        ReadOnlySpan<byte> orderId = default;
        ReadOnlySpan<byte> clOrdId = default;
        ReadOnlySpan<byte> execId = default;
        byte execType = 0;
        byte ordStatus = 0;
        ReadOnlySpan<byte> symbol = default;
        byte side = 0;
        decimal leavesQty = 0;
        decimal cumQty = 0;
        decimal avgPx = 0;

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
                case FixTags.OrderId:
                    orderId = field.Value;
                    seenMask |= 1u << 8;
                    break;
                case FixTags.ClOrdId:
                    clOrdId = field.Value;
                    seenMask |= 1u << 9;
                    break;
                case FixTags.ExecId:
                    execId = field.Value;
                    seenMask |= 1u << 10;
                    break;
                case FixTags.ExecType:
                    if (field.Value.Length != 1)
                    {
                        return false;
                    }
                    execType = field.Value[0];
                    seenMask |= 1u << 11;
                    break;
                case FixTags.OrdStatus:
                    if (field.Value.Length != 1)
                    {
                        return false;
                    }
                    ordStatus = field.Value[0];
                    seenMask |= 1u << 12;
                    break;
                case FixTags.Symbol:
                    symbol = field.Value;
                    seenMask |= 1u << 13;
                    break;
                case FixTags.Side:
                    if (field.Value.Length != 1)
                    {
                        return false;
                    }
                    side = field.Value[0];
                    seenMask |= 1u << 14;
                    break;
                case FixTags.LeavesQty:
                    if (!FixValueParser.TryParseDecimal(field.Value, out leavesQty))
                    {
                        return false;
                    }
                    seenMask |= 1u << 15;
                    break;
                case FixTags.CumQty:
                    if (!FixValueParser.TryParseDecimal(field.Value, out cumQty))
                    {
                        return false;
                    }
                    seenMask |= 1u << 16;
                    break;
                case FixTags.AvgPx:
                    if (!FixValueParser.TryParseDecimal(field.Value, out avgPx))
                    {
                        return false;
                    }
                    seenMask |= 1u << 17;
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

        message = new ExecutionReportMessage(
            beginString, msgSeqNum, senderCompId, targetCompId, sendingTime,
            orderId, clOrdId, execId, execType, ordStatus, symbol, side, leavesQty, cumQty, avgPx);
        return true;
    }

    /// <summary>Regenerates this message's bytes into <paramref name="buffer"/>, returning the written slice.</summary>
    public Span<byte> WriteTo(Span<byte> buffer)
    {
        FixMessageWriter writer = new(buffer);
        writer.WriteHeader(BeginString, MsgType, MsgSeqNum, SenderCompId, TargetCompId, SendingTime);
        writer.WriteTag(FixTags.OrderId, OrderId);
        writer.WriteTag(FixTags.ClOrdId, ClOrdId);
        writer.WriteTag(FixTags.ExecId, ExecId);
        writer.WriteTag(FixTags.ExecType, ExecType);
        writer.WriteTag(FixTags.OrdStatus, OrdStatus);
        writer.WriteTag(FixTags.Symbol, Symbol);
        writer.WriteTag(FixTags.Side, Side);
        writer.WriteTag(FixTags.LeavesQty, LeavesQty);
        writer.WriteTag(FixTags.CumQty, CumQty);
        writer.WriteTag(FixTags.AvgPx, AvgPx);
        return writer.Finish();
    }
}
