namespace Boulevard.Risk.Engine;

/// <summary>
/// Deliberately independent of Boulevard.MarketData.Engine's own Side enum - this project has no
/// dependency on MarketData.Engine or on any protocol project (Boulevard.Protocol.Fix included),
/// matching the existing Core-layer convention of staying protocol-agnostic. Duplicating a 2-case
/// enum is cheaper than an artificial cross-project reference just to share it.
/// </summary>
public enum Side
{
    Buy,
    Sell
}
