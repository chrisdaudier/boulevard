namespace Boulevard.Risk.Engine;

/// <summary>One of an account's own resting orders on a symbol, tracked for self-trade prevention.</summary>
internal struct RestingOrder
{
    public long OrderId;
    public Side Side;
    public uint PriceInTicks;
    public uint Quantity;
}
