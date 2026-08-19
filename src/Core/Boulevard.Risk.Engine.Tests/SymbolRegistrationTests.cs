namespace Boulevard.Risk.Engine.Tests;

public class SymbolRegistrationTests
{
    [Fact]
    public void RegisteringSameSymbolTwice_ReturnsSameId()
    {
        var engine = new RiskEngine();

        int firstId = engine.RegisterSymbol("AAPL", maxOrderSize: 1_000, maxDeviationBasisPoints: 500);
        int secondId = engine.RegisterSymbol("AAPL", maxOrderSize: 1_000, maxDeviationBasisPoints: 500);

        Assert.Equal(firstId, secondId);
    }

    [Fact]
    public void RegisteringDifferentSymbols_ReturnsDistinctIds()
    {
        var engine = new RiskEngine();

        int aaplId = engine.RegisterSymbol("AAPL", maxOrderSize: 1_000, maxDeviationBasisPoints: 500);
        int msftId = engine.RegisterSymbol("MSFT", maxOrderSize: 1_000, maxDeviationBasisPoints: 500);

        Assert.NotEqual(aaplId, msftId);
    }

    [Fact]
    public void TryGetSymbolId_FindsARegisteredSymbol()
    {
        var engine = new RiskEngine();
        int registeredId = engine.RegisterSymbol("AAPL", maxOrderSize: 1_000, maxDeviationBasisPoints: 500);

        bool found = engine.TryGetSymbolId("AAPL", out int lookedUpId);

        Assert.True(found);
        Assert.Equal(registeredId, lookedUpId);
    }

    [Fact]
    public void TryGetSymbolId_ReturnsFalseForUnknownSymbol()
    {
        var engine = new RiskEngine();

        bool found = engine.TryGetSymbolId("UNKNOWN", out _);

        Assert.False(found);
    }

    [Fact]
    public void RegisteringManySymbols_GrowsPastInitialCapacity()
    {
        // Default initial capacity is 16 - registering more than that must not throw, proving the
        // amortized-growth path (Array.Resize on both the symbol table and the resting-order
        // jagged array) works correctly.
        var engine = new RiskEngine(initialSymbolCapacity: 2, initialAccountCapacity: 2);

        for (int i = 0; i < 50; i++)
        {
            int symbolId = engine.RegisterSymbol($"SYM{i}", maxOrderSize: 1_000, maxDeviationBasisPoints: 500, initialReferencePriceInTicks: 100);
            engine.AddRestingOrder(symbolId, accountId: 20, orderId: i, Side.Buy, priceInTicks: 100, quantity: 10);

            bool passed = engine.TryCheck(symbolId, accountId: 20, Side.Sell, priceInTicks: 100, quantity: 10, out RiskCheckResult result);
            Assert.False(passed); // crosses the resting buy just added
            Assert.Equal(RejectReason.SelfTradePrevented, result.Reason);
        }
    }
}
