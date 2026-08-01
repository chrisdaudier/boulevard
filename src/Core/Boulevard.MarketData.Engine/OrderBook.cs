namespace Boulevard.MarketData.Engine;

/// <summary>
/// Protocol-agnostic L3 (order-by-order) book for a single symbol.
/// Callers resolve which OrderBook instance a message belongs to.
/// </summary>
public struct OrderBook
{
    private sealed class DescendingComparer : IComparer<int>
    {
        public static readonly DescendingComparer Instance = new();
        public int Compare(int x, int y) => y.CompareTo(x);
    }

    private readonly Dictionary<ulong, BookOrder> _ordersByReference;
    private readonly SortedDictionary<int, long> _bidLevels;
    private readonly SortedDictionary<int, long> _askLevels;

    public OrderBook()
    {
        _ordersByReference = new Dictionary<ulong, BookOrder>();
        _bidLevels = new SortedDictionary<int, long>(DescendingComparer.Instance);
        _askLevels = new SortedDictionary<int, long>();
    }

    public void AddOrder(ulong orderReferenceNumber, Side side, int priceCents, uint shares)
    {
        _ordersByReference[orderReferenceNumber] = new BookOrder
        {
            Side = side,
            PriceCents = priceCents,
            Shares = shares
        };

        LevelsFor(side)[priceCents] = LevelsFor(side).GetValueOrDefault(priceCents) + shares;
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
            BidPriceCents = _bidLevels.Count > 0 ? _bidLevels.First().Key : null,
            BidShares = _bidLevels.Count > 0 ? _bidLevels.First().Value : 0,
            AskPriceCents = _askLevels.Count > 0 ? _askLevels.First().Key : null,
            AskShares = _askLevels.Count > 0 ? _askLevels.First().Value : 0
        };
    }

    private void ReduceShares(ulong orderReferenceNumber, BookOrder order, uint sharesToRemove)
    {
        var levels = LevelsFor(order.Side);
        long remainingAtLevel = levels.GetValueOrDefault(order.PriceCents) - sharesToRemove;

        if (remainingAtLevel <= 0)
        {
            levels.Remove(order.PriceCents);
        }
        else
        {
            levels[order.PriceCents] = remainingAtLevel;
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

    private SortedDictionary<int, long> LevelsFor(Side side) => side == Side.Buy ? _bidLevels : _askLevels;
}
