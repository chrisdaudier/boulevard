namespace Boulevard.Risk.Engine.Tests;

public class PriceBandCheckTests
{
    private static RiskEngine CreateEngine(out int symbolId, uint maxOrderSize = 1_000, uint maxDeviationBasisPoints = 500, uint referencePriceInTicks = 1_000_000)
    {
        var engine = new RiskEngine();
        symbolId = engine.RegisterSymbol("AAPL", maxOrderSize, maxDeviationBasisPoints, referencePriceInTicks);
        return engine;
    }

    [Fact]
    public void Passes_WhenWithinBand()
    {
        RiskEngine engine = CreateEngine(out int symbolId);

        bool passed = engine.TryCheck(symbolId, accountId: 0, Side.Buy, priceInTicks: 1_010_000, quantity: 100, out RiskCheckResult result);

        Assert.True(passed);
        Assert.True(result.Passed);
        Assert.Equal(RejectReason.None, result.Reason);
    }

    [Fact]
    public void Rejects_WhenPriceTooFarAboveReference()
    {
        RiskEngine engine = CreateEngine(out int symbolId, maxDeviationBasisPoints: 500); // 5%

        // 1,000,000 + 6% = 1,060,000 - past the 5% band.
        bool passed = engine.TryCheck(symbolId, accountId: 0, Side.Buy, priceInTicks: 1_060_000, quantity: 100, out RiskCheckResult result);

        Assert.False(passed);
        Assert.Equal(RejectReason.PriceBandExceeded, result.Reason);
    }

    [Fact]
    public void Rejects_WhenPriceTooFarBelowReference()
    {
        RiskEngine engine = CreateEngine(out int symbolId, maxDeviationBasisPoints: 500);

        bool passed = engine.TryCheck(symbolId, accountId: 0, Side.Sell, priceInTicks: 940_000, quantity: 100, out RiskCheckResult result);

        Assert.False(passed);
        Assert.Equal(RejectReason.PriceBandExceeded, result.Reason);
    }

    [Fact]
    public void Passes_AtExactlyTheMaxDeviationBoundary()
    {
        RiskEngine engine = CreateEngine(out int symbolId, maxDeviationBasisPoints: 500, referencePriceInTicks: 1_000_000);

        // Exactly 5% above 1,000,000 = 1,050,000 - at the boundary is accepted, not rejected.
        bool passed = engine.TryCheck(symbolId, accountId: 0, Side.Buy, priceInTicks: 1_050_000, quantity: 100, out RiskCheckResult result);

        Assert.True(passed);
        Assert.Equal(RejectReason.None, result.Reason);
    }

    [Fact]
    public void Rejects_JustPastTheMaxDeviationBoundary()
    {
        RiskEngine engine = CreateEngine(out int symbolId, maxDeviationBasisPoints: 500, referencePriceInTicks: 1_000_000);

        bool passed = engine.TryCheck(symbolId, accountId: 0, Side.Buy, priceInTicks: 1_050_100, quantity: 100, out RiskCheckResult result);

        Assert.False(passed);
        Assert.Equal(RejectReason.PriceBandExceeded, result.Reason);
    }

    [Fact]
    public void Rejects_WhenQuantityExceedsMaxOrderSize()
    {
        RiskEngine engine = CreateEngine(out int symbolId, maxOrderSize: 1_000);

        bool passed = engine.TryCheck(symbolId, accountId: 0, Side.Buy, priceInTicks: 1_000_000, quantity: 1_001, out RiskCheckResult result);

        Assert.False(passed);
        Assert.Equal(RejectReason.OrderSizeExceeded, result.Reason);
    }

    [Fact]
    public void Passes_AtExactlyMaxOrderSize()
    {
        RiskEngine engine = CreateEngine(out int symbolId, maxOrderSize: 1_000);

        bool passed = engine.TryCheck(symbolId, accountId: 0, Side.Buy, priceInTicks: 1_000_000, quantity: 1_000, out RiskCheckResult result);

        Assert.True(passed);
        Assert.Equal(RejectReason.None, result.Reason);
    }

    [Fact]
    public void Passes_WhenNoReferencePriceConfiguredYet()
    {
        // ReferencePriceInTicks defaults to 0 until UpdateReferencePrice/RegisterSymbol sets one -
        // the band check is skipped entirely rather than dividing by zero or rejecting everything.
        var engine = new RiskEngine();
        int symbolId = engine.RegisterSymbol("NEWSYMBOL", maxOrderSize: 1_000, maxDeviationBasisPoints: 500);

        bool passed = engine.TryCheck(symbolId, accountId: 0, Side.Buy, priceInTicks: 5_000_000, quantity: 100, out RiskCheckResult result);

        Assert.True(passed);
        Assert.Equal(RejectReason.None, result.Reason);
    }
}
