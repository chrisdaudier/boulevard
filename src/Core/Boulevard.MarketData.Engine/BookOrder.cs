namespace Boulevard.MarketData.Engine;

internal struct BookOrder
{
    public Side Side;

    /// <summary>Price in ITCH ticks (1/10,000th of a dollar), matching the wire field's native precision.</summary>
    public uint PriceInTicks;
    public uint Shares;
}
