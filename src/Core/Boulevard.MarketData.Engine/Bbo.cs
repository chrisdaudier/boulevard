namespace Boulevard.MarketData.Engine;

public readonly struct Bbo
{
    public int? BidPriceCents { get; init; }
    public long BidShares { get; init; }
    public int? AskPriceCents { get; init; }
    public long AskShares { get; init; }
}
