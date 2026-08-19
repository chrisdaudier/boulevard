namespace Boulevard.Protocol.Fix;

/// <summary>FIX tag numbers used by the message types this codec currently supports.</summary>
internal static class FixTags
{
    // Standard header (every message)
    public const int BeginString = 8;
    public const int BodyLength = 9;
    public const int MsgType = 35;
    public const int SenderCompId = 49;
    public const int TargetCompId = 56;
    public const int MsgSeqNum = 34;
    public const int SendingTime = 52;

    // Standard trailer (every message)
    public const int CheckSum = 10;

    // Logon (A)
    public const int EncryptMethod = 98;
    public const int HeartBtInt = 108;

    // NewOrderSingle (D)
    public const int ClOrdId = 11;
    public const int Symbol = 55;
    public const int Side = 54;
    public const int OrderQty = 38;
    public const int OrdType = 40;
    public const int Price = 44;
    public const int TransactTime = 60;

    // ExecutionReport (8)
    public const int OrderId = 37;
    public const int ExecId = 17;
    public const int ExecType = 150;
    public const int OrdStatus = 39;
    public const int LeavesQty = 151;
    public const int CumQty = 14;
    public const int AvgPx = 6;
}
