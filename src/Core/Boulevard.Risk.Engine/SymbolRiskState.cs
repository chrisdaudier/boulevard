namespace Boulevard.Risk.Engine;

/// <summary>
/// Per-symbol fat-finger/price-band configuration and current reference price. Integer ticks
/// throughout (matching Boulevard.MarketData.Engine.OrderBook.PriceInTicks) rather than
/// decimal/double - decimal conversion, if ever needed, belongs at a display/DTO boundary, not on
/// this check path.
/// </summary>
internal struct SymbolRiskState
{
    public uint MaxOrderSize;
    public uint MaxDeviationBasisPoints;
    public uint ReferencePriceInTicks;
}
