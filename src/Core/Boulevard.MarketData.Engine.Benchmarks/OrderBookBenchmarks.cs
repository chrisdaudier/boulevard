using BenchmarkDotNet.Attributes;
using Boulevard.MarketData.Engine;

// MemoryDiagnoser enables GC collection/allocation tracking, the key claim under test here.
[MemoryDiagnoser]
[Orderer(BenchmarkDotNet.Order.SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class OrderBookBenchmarks
{
    private const int PrePopulatedOrderCount = 2_000;
    private const uint BaselineShares = 1_000_000_000; // large enough that repeated Execute/Cancel never fully drains an order within a benchmark run

    private OrderBook _addBook;
    private OrderBook _executeBook;
    private OrderBook _cancelBook;
    private OrderBook _snapshotBook;

    private ulong _addCursor;
    private ulong _executeCursor;
    private ulong _cancelCursor;

    [GlobalSetup]
    public void Setup()
    {
        _addBook = CreatePrePopulatedBook();
        _executeBook = CreatePrePopulatedBook();
        _cancelBook = CreatePrePopulatedBook();
        _snapshotBook = CreatePrePopulatedBook();
    }

    private static OrderBook CreatePrePopulatedBook()
    {
        var book = new OrderBook();
        var random = new Random(1337);

        // Spread across ~5,000 distinct price levels so lookups exercise a realistically sized,
        // non-trivial sorted array rather than a degenerate single-level book.
        for (ulong orderReferenceNumber = 1; orderReferenceNumber <= PrePopulatedOrderCount; orderReferenceNumber++)
        {
            Side side = orderReferenceNumber % 2 == 0 ? Side.Buy : Side.Sell;
            uint priceInTicks = (uint)(1_000_000 + (random.Next(0, 5_000) * 100));
            book.AddOrder(orderReferenceNumber, side, priceInTicks, BaselineShares);
        }

        return book;
    }

    /// <summary>
    /// Measures adding to an already-active price level (the common case - most orders land on
    /// a price that already has resting liquidity). First-time price-level creation additionally
    /// pays for a sorted-array insertion shift, not captured by this steady-state measurement.
    /// </summary>
    [Benchmark]
    public void AddOrder()
    {
        ulong orderReferenceNumber = (_addCursor++ % PrePopulatedOrderCount) + 1;
        Side side = orderReferenceNumber % 2 == 0 ? Side.Buy : Side.Sell;
        _addBook.AddOrder(orderReferenceNumber, side, 1_000_000, 100);
    }

    [Benchmark]
    public void Execute()
    {
        ulong orderReferenceNumber = (_executeCursor++ % PrePopulatedOrderCount) + 1;
        _executeBook.Execute(orderReferenceNumber, 1);
    }

    [Benchmark]
    public void Cancel()
    {
        ulong orderReferenceNumber = (_cancelCursor++ % PrePopulatedOrderCount) + 1;
        _cancelBook.Cancel(orderReferenceNumber, 1);
    }

    [Benchmark]
    public Bbo GetBbo() => _snapshotBook.GetBbo();

    [Benchmark]
    public int GetBidDepth() => _snapshotBook.GetBidDepth().Length;
}
