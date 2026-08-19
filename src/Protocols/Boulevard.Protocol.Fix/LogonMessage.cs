namespace Boulevard.Protocol.Fix;

/// <summary>
/// FIX Logon (MsgType 'A'). Session-level handshake fields only - sequence-number/resend/logout
/// state machine handling is a future concern, not this codec.
///
/// Like Boulevard.Protocol.Itch's message types, this is a `ref struct`: it can't be stored in a
/// class field, boxed, captured in a lambda/iterator that escapes, or held across an `await`.
/// Extract whatever primitives you need immediately if they must outlive this call.
/// </summary>
public readonly ref struct LogonMessage
{
    public static ReadOnlySpan<byte> MsgType => "A"u8;

    public ReadOnlySpan<byte> BeginString { get; }
    public int MsgSeqNum { get; }
    public ReadOnlySpan<byte> SenderCompId { get; }
    public ReadOnlySpan<byte> TargetCompId { get; }
    public DateTime SendingTime { get; }

    public int EncryptMethod { get; }
    public int HeartBtInt { get; }

    private LogonMessage(
        ReadOnlySpan<byte> beginString,
        int msgSeqNum,
        ReadOnlySpan<byte> senderCompId,
        ReadOnlySpan<byte> targetCompId,
        DateTime sendingTime,
        int encryptMethod,
        int heartBtInt)
    {
        BeginString = beginString;
        MsgSeqNum = msgSeqNum;
        SenderCompId = senderCompId;
        TargetCompId = targetCompId;
        SendingTime = sendingTime;
        EncryptMethod = encryptMethod;
        HeartBtInt = heartBtInt;
    }

    public static bool TryParse(ReadOnlySpan<byte> rawMessage, out LogonMessage message)
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
            1u << 8 | // EncryptMethod
            1u << 9;  // HeartBtInt

        uint seenMask = 0;
        ReadOnlySpan<byte> beginString = default;
        int msgSeqNum = 0;
        ReadOnlySpan<byte> senderCompId = default;
        ReadOnlySpan<byte> targetCompId = default;
        DateTime sendingTime = default;
        int encryptMethod = 0;
        int heartBtInt = 0;

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
                case FixTags.EncryptMethod:
                    if (!FixValueParser.TryParseInt32(field.Value, out encryptMethod))
                    {
                        return false;
                    }
                    seenMask |= 1u << 8;
                    break;
                case FixTags.HeartBtInt:
                    if (!FixValueParser.TryParseInt32(field.Value, out heartBtInt))
                    {
                        return false;
                    }
                    seenMask |= 1u << 9;
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

        message = new LogonMessage(beginString, msgSeqNum, senderCompId, targetCompId, sendingTime, encryptMethod, heartBtInt);
        return true;
    }

    /// <summary>Regenerates this message's bytes into <paramref name="buffer"/>, returning the written slice.</summary>
    public Span<byte> WriteTo(Span<byte> buffer)
    {
        FixMessageWriter writer = new(buffer);
        writer.WriteHeader(BeginString, MsgType, MsgSeqNum, SenderCompId, TargetCompId, SendingTime);
        writer.WriteTag(FixTags.EncryptMethod, EncryptMethod);
        writer.WriteTag(FixTags.HeartBtInt, HeartBtInt);
        return writer.Finish();
    }
}
