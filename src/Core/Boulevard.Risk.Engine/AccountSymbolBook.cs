namespace Boulevard.Risk.Engine;

/// <summary>
/// An account's own resting orders on one symbol, used for self-trade detection. Bounded array,
/// linear scan - deliberately not a hash table or a copy of OrderBook's sorted-level design: an
/// individual account's own resting-order count on a single symbol is naturally tiny (a handful,
/// not the thousands a market-wide book has), so that extra complexity isn't earned here, and
/// there's no ordering/aggregation need - only "does any of my resting orders cross this order."
/// </summary>
internal struct AccountSymbolBook
{
    private RestingOrder[]? _orders;
    private int _count;

    public void Add(long orderId, Side side, uint priceInTicks, uint quantity)
    {
        _orders ??= new RestingOrder[4];

        if (_count == _orders.Length)
        {
            Array.Resize(ref _orders, _orders.Length * 2);
        }

        _orders[_count++] = new RestingOrder { OrderId = orderId, Side = side, PriceInTicks = priceInTicks, Quantity = quantity };
    }

    public void Remove(long orderId)
    {
        if (_orders is null)
        {
            return;
        }

        for (int i = 0; i < _count; i++)
        {
            if (_orders[i].OrderId == orderId)
            {
                // Swap-remove: an account's own resting orders have no meaningful order to
                // preserve, unlike OrderBook's price-level arrays.
                _orders[i] = _orders[_count - 1];
                _count--;
                return;
            }
        }
    }

    /// <summary>
    /// True if an incoming order at <paramref name="incomingPriceInTicks"/> on
    /// <paramref name="incomingSide"/> would cross one of this account's own resting orders on the
    /// opposite side. Equal price counts as crossing - a same-price fill against your own resting
    /// order is exactly the self-trade risk this check exists to prevent, not a near-miss.
    /// </summary>
    public readonly bool CrossesResting(Side incomingSide, uint incomingPriceInTicks)
    {
        if (_orders is null)
        {
            return false;
        }

        for (int i = 0; i < _count; i++)
        {
            RestingOrder resting = _orders[i];
            if (resting.Side == incomingSide)
            {
                continue; // same side can't cross
            }

            if (incomingSide == Side.Buy && resting.PriceInTicks <= incomingPriceInTicks)
            {
                return true; // incoming buy at/above a resting sell
            }

            if (incomingSide == Side.Sell && resting.PriceInTicks >= incomingPriceInTicks)
            {
                return true; // incoming sell at/below a resting buy
            }
        }

        return false;
    }
}
