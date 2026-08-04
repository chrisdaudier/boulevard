namespace Boulevard.MarketData.Engine;

public readonly struct Bbo
{
    /// <summary>Price in ITCH ticks (1/10,000th of a dollar).</summary>
    public uint? BidPriceInTicks { get; init; }
    public long BidShares { get; init; }

    /// <summary>Price in ITCH ticks (1/10,000th of a dollar).</summary>
    public uint? AskPriceInTicks { get; init; }
    public long AskShares { get; init; }
}
