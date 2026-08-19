using BenchmarkDotNet.Attributes;
using Boulevard.Risk.Engine;

// MemoryDiagnoser enables GC collection/allocation tracking - the "zero-allocation" claim under
// test here, alongside the sub-microsecond latency numbers BenchmarkDotNet reports directly.
[MemoryDiagnoser]
[Orderer(BenchmarkDotNet.Order.SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class RiskEngineBenchmarks
{
    private const int AccountId = 0;

    // A handful of resting orders per account/symbol, matching this engine's own design
    // assumption (see AccountSymbolBook's doc comment) rather than a market-wide order count.
    private const int RestingOrderCountPerAccount = 8;

    private RiskEngine _priceBandPassEngine = null!;
    private RiskEngine _priceBandRejectEngine = null!;
    private RiskEngine _selfTradePassEngine = null!;
    private RiskEngine _selfTradeRejectEngine = null!;

    private int _priceBandPassSymbolId;
    private int _priceBandRejectSymbolId;
    private int _selfTradePassSymbolId;
    private int _selfTradeRejectSymbolId;

    [GlobalSetup]
    public void Setup()
    {
        _priceBandPassEngine = new RiskEngine();
        _priceBandPassSymbolId = _priceBandPassEngine.RegisterSymbol("AAPL", maxOrderSize: 1_000_000, maxDeviationBasisPoints: 500, initialReferencePriceInTicks: 1_000_000);

        _priceBandRejectEngine = new RiskEngine();
        _priceBandRejectSymbolId = _priceBandRejectEngine.RegisterSymbol("AAPL", maxOrderSize: 1_000_000, maxDeviationBasisPoints: 500, initialReferencePriceInTicks: 1_000_000);

        _selfTradePassEngine = new RiskEngine();
        _selfTradePassSymbolId = _selfTradePassEngine.RegisterSymbol("AAPL", maxOrderSize: 1_000_000, maxDeviationBasisPoints: 500, initialReferencePriceInTicks: 1_000_000);
        PopulateNonCrossingRestingOrders(_selfTradePassEngine, _selfTradePassSymbolId);

        _selfTradeRejectEngine = new RiskEngine();
        _selfTradeRejectSymbolId = _selfTradeRejectEngine.RegisterSymbol("AAPL", maxOrderSize: 1_000_000, maxDeviationBasisPoints: 500, initialReferencePriceInTicks: 1_000_000);
        PopulateNonCrossingRestingOrders(_selfTradeRejectEngine, _selfTradeRejectSymbolId);
        _selfTradeRejectEngine.AddRestingOrder(_selfTradeRejectSymbolId, AccountId, orderId: 999, Side.Sell, priceInTicks: 1_000_100, quantity: 100);
    }

    private static void PopulateNonCrossingRestingOrders(RiskEngine engine, int symbolId)
    {
        for (int i = 0; i < RestingOrderCountPerAccount; i++)
        {
            // All resting buys, well below the incoming sell prices these benchmarks send - none
            // of these should ever cross, so the linear scan runs to completion every call.
            engine.AddRestingOrder(symbolId, AccountId, orderId: i, Side.Buy, priceInTicks: 900_000, quantity: 100);
        }
    }

    [Benchmark]
    public bool TryCheck_PriceBandPass() =>
        _priceBandPassEngine.TryCheck(_priceBandPassSymbolId, AccountId, Side.Buy, priceInTicks: 1_010_000, quantity: 100, out _);

    [Benchmark]
    public bool TryCheck_PriceBandReject() =>
        _priceBandRejectEngine.TryCheck(_priceBandRejectSymbolId, AccountId, Side.Buy, priceInTicks: 1_100_000, quantity: 100, out _);

    [Benchmark]
    public bool TryCheck_SelfTradePass() =>
        _selfTradePassEngine.TryCheck(_selfTradePassSymbolId, AccountId, Side.Sell, priceInTicks: 1_000_000, quantity: 100, out _);

    [Benchmark]
    public bool TryCheck_SelfTradeReject() =>
        _selfTradeRejectEngine.TryCheck(_selfTradeRejectSymbolId, AccountId, Side.Buy, priceInTicks: 1_000_200, quantity: 100, out _);
}
