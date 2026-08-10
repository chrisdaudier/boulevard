namespace Boulevard.MarketData.Engine;

/// <summary>
/// A single aggregated price level. Deliberately a plain mutable struct (not readonly/init) -
/// it's used both as OrderBook's in-place-mutated internal array element and as the type
/// exposed externally via ReadOnlySpan&lt;PriceLevel&gt;, which already prevents mutation through
/// that view regardless of the element type's own mutability.
/// </summary>
public struct PriceLevel
{
    /// <summary>Price in ITCH ticks (1/10,000th of a dollar).</summary>
    public uint PriceInTicks;
    public long AggregateShares;
}
