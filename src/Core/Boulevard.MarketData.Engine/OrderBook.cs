namespace Boulevard.MarketData.Engine;

/// <summary>
/// Protocol-agnostic L3 (order-by-order) book for a single symbol.
/// Callers resolve which OrderBook instance a message belongs to.
/// </summary>
public struct OrderBook
{
    private sealed class DescendingComparer : IComparer<uint>
    {
        public static readonly DescendingComparer Instance = new();
        public int Compare(uint x, uint y) => y.CompareTo(x);
    }

    private readonly Dictionary<ulong, BookOrder> _ordersByReference;
    private readonly SortedDictionary<uint, long> _bidLevels;
    private readonly SortedDictionary<uint, long> _askLevels;

    public OrderBook()
    {
        _ordersByReference = new Dictionary<ulong, BookOrder>();
        _bidLevels = new SortedDictionary<uint, long>(DescendingComparer.Instance);
        _askLevels = new SortedDictionary<uint, long>();
    }

    public void AddOrder(ulong orderReferenceNumber, Side side, uint priceInTicks, uint shares)
    {
        _ordersByReference[orderReferenceNumber] = new BookOrder
        {
            Side = side,
            PriceInTicks = priceInTicks,
            Shares = shares
        };

        LevelsFor(side)[priceInTicks] = LevelsFor(side).GetValueOrDefault(priceInTicks) + shares;
    }

    public void Execute(ulong orderReferenceNumber, uint executedShares)
    {
        if (!_ordersByReference.TryGetValue(orderReferenceNumber, out BookOrder order))
        {
            return;
        }

        ReduceShares(orderReferenceNumber, order, executedShares);
    }

    public void Cancel(ulong orderReferenceNumber, uint canceledShares)
    {
        if (!_ordersByReference.TryGetValue(orderReferenceNumber, out BookOrder order))
        {
            return;
        }

        ReduceShares(orderReferenceNumber, order, canceledShares);
    }

    public Bbo GetBbo()
    {
        return new Bbo
        {
            BidPriceInTicks = _bidLevels.Count > 0 ? _bidLevels.First().Key : null,
            BidShares = _bidLevels.Count > 0 ? _bidLevels.First().Value : 0,
            AskPriceInTicks = _askLevels.Count > 0 ? _askLevels.First().Key : null,
            AskShares = _askLevels.Count > 0 ? _askLevels.First().Value : 0
        };
    }

    private readonly void ReduceShares(ulong orderReferenceNumber, BookOrder order, uint sharesToRemove)
    {
        var levels = LevelsFor(order.Side);
        long remainingAtLevel = levels.GetValueOrDefault(order.PriceInTicks) - sharesToRemove;

        if (remainingAtLevel <= 0)
        {
            levels.Remove(order.PriceInTicks);
        }
        else
        {
            levels[order.PriceInTicks] = remainingAtLevel;
        }

        if (sharesToRemove >= order.Shares)
        {
            _ordersByReference.Remove(orderReferenceNumber);
        }
        else
        {
            order.Shares -= sharesToRemove;
            _ordersByReference[orderReferenceNumber] = order;
        }
    }

    private readonly SortedDictionary<uint, long> LevelsFor(Side side) => side == Side.Buy ? _bidLevels : _askLevels;
}
