namespace Boulevard.Risk.Engine.Tests;

public class SelfTradePreventionTests
{
    private const int AccountA = 0;
    private const int AccountB = 1;

    private static RiskEngine CreateEngine(out int symbolId)
    {
        var engine = new RiskEngine();
        // No price band configured (0 reference price disables that check) - these tests isolate
        // self-trade prevention specifically.
        symbolId = engine.RegisterSymbol("AAPL", maxOrderSize: 1_000_000, maxDeviationBasisPoints: 0);
        return engine;
    }

    [Fact]
    public void Passes_WhenNoRestingOrdersExist()
    {
        RiskEngine engine = CreateEngine(out int symbolId);

        bool passed = engine.TryCheck(symbolId, AccountA, Side.Buy, priceInTicks: 100, quantity: 10, out RiskCheckResult result);

        Assert.True(passed);
        Assert.Equal(RejectReason.None, result.Reason);
    }

    [Fact]
    public void Rejects_IncomingBuyAtOrAboveOwnRestingSell()
    {
        RiskEngine engine = CreateEngine(out int symbolId);
        engine.AddRestingOrder(symbolId, AccountA, orderId: 1, Side.Sell, priceInTicks: 100, quantity: 10);

        bool passed = engine.TryCheck(symbolId, AccountA, Side.Buy, priceInTicks: 101, quantity: 10, out RiskCheckResult result);

        Assert.False(passed);
        Assert.Equal(RejectReason.SelfTradePrevented, result.Reason);
    }

    [Fact]
    public void Rejects_IncomingSellAtOrBelowOwnRestingBuy()
    {
        RiskEngine engine = CreateEngine(out int symbolId);
        engine.AddRestingOrder(symbolId, AccountA, orderId: 1, Side.Buy, priceInTicks: 100, quantity: 10);

        bool passed = engine.TryCheck(symbolId, AccountA, Side.Sell, priceInTicks: 99, quantity: 10, out RiskCheckResult result);

        Assert.False(passed);
        Assert.Equal(RejectReason.SelfTradePrevented, result.Reason);
    }

    [Fact]
    public void Rejects_AtExactlyEqualPrice()
    {
        // Equal price counts as crossing - a same-price fill against your own resting order is
        // exactly the self-trade risk this check exists to prevent, not a near-miss.
        RiskEngine engine = CreateEngine(out int symbolId);
        engine.AddRestingOrder(symbolId, AccountA, orderId: 1, Side.Sell, priceInTicks: 100, quantity: 10);

        bool passed = engine.TryCheck(symbolId, AccountA, Side.Buy, priceInTicks: 100, quantity: 10, out RiskCheckResult result);

        Assert.False(passed);
        Assert.Equal(RejectReason.SelfTradePrevented, result.Reason);
    }

    [Fact]
    public void Passes_WhenIncomingOrderDoesNotCross()
    {
        RiskEngine engine = CreateEngine(out int symbolId);
        engine.AddRestingOrder(symbolId, AccountA, orderId: 1, Side.Sell, priceInTicks: 100, quantity: 10);

        bool passed = engine.TryCheck(symbolId, AccountA, Side.Buy, priceInTicks: 99, quantity: 10, out RiskCheckResult result);

        Assert.True(passed);
        Assert.Equal(RejectReason.None, result.Reason);
    }

    [Fact]
    public void Passes_WhenRestingOrderIsSameSide()
    {
        RiskEngine engine = CreateEngine(out int symbolId);
        engine.AddRestingOrder(symbolId, AccountA, orderId: 1, Side.Buy, priceInTicks: 100, quantity: 10);

        bool passed = engine.TryCheck(symbolId, AccountA, Side.Buy, priceInTicks: 100, quantity: 10, out RiskCheckResult result);

        Assert.True(passed);
        Assert.Equal(RejectReason.None, result.Reason);
    }

    [Fact]
    public void DoesNotTriggerOnAnotherAccountsCrossingOrder()
    {
        // Proves the check is properly account-scoped, not symbol-wide: AccountB's own resting
        // sell at a crossing price must not reject AccountA's incoming buy.
        RiskEngine engine = CreateEngine(out int symbolId);
        engine.AddRestingOrder(symbolId, AccountB, orderId: 1, Side.Sell, priceInTicks: 100, quantity: 10);

        bool passed = engine.TryCheck(symbolId, AccountA, Side.Buy, priceInTicks: 101, quantity: 10, out RiskCheckResult result);

        Assert.True(passed);
        Assert.Equal(RejectReason.None, result.Reason);
    }

    [Fact]
    public void Passes_AfterRestingOrderIsRemoved()
    {
        RiskEngine engine = CreateEngine(out int symbolId);
        engine.AddRestingOrder(symbolId, AccountA, orderId: 1, Side.Sell, priceInTicks: 100, quantity: 10);
        engine.RemoveRestingOrder(symbolId, AccountA, orderId: 1);

        bool passed = engine.TryCheck(symbolId, AccountA, Side.Buy, priceInTicks: 101, quantity: 10, out RiskCheckResult result);

        Assert.True(passed);
        Assert.Equal(RejectReason.None, result.Reason);
    }
}
