using System.Numerics;

namespace Boulevard.MarketData.Engine;

internal struct OrderSlot
{
    public ulong OrderReferenceNumber;
    public bool IsOccupied;
    public Side Side;
    public uint PriceInTicks;
    public uint Shares;
}

/// <summary>
/// Protocol-agnostic L3 (order-by-order) book for a single symbol.
/// Callers resolve which OrderBook instance a message belongs to.
///
/// Backed by flat, pre-allocated arrays rather than Dictionary/SortedDictionary: an
/// open-addressing hash table (backward-shift deletion, no tombstones) gives O(1) order lookup
/// by OrderReferenceNumber, and two sorted dense arrays (binary search) hold price levels.
/// Both grow (rare, amortized reallocation) rather than being a hard fixed cap.
/// </summary>
public struct OrderBook
{
    private const double MaxLoadFactor = 0.7;
    private const ulong HashMultiplier = 0x9E3779B97F4A7C15UL; // Fibonacci hashing constant

    private OrderSlot[] _orderSlots;
    private int _orderCount;

    private PriceLevel[] _bidLevels; // sorted descending by PriceInTicks - index 0 is best bid
    private int _bidLevelCount;
    private PriceLevel[] _askLevels; // sorted ascending by PriceInTicks - index 0 is best ask
    private int _askLevelCount;

    public OrderBook() : this(initialOrderCapacity: 1024, initialLevelCapacity: 256)
    {
    }

    /// <summary>Sized for a typical symbol, not the busiest one - both tables grow on demand.</summary>
    public OrderBook(int initialOrderCapacity, int initialLevelCapacity)
    {
        _orderSlots = new OrderSlot[(int)BitOperations.RoundUpToPowerOf2((uint)initialOrderCapacity)];
        _orderCount = 0;
        _bidLevels = new PriceLevel[initialLevelCapacity];
        _bidLevelCount = 0;
        _askLevels = new PriceLevel[initialLevelCapacity];
        _askLevelCount = 0;
    }

    public void AddOrder(ulong orderReferenceNumber, Side side, uint priceInTicks, uint shares)
    {
        if (InsertOrder(orderReferenceNumber, side, priceInTicks, shares, out OrderSlot previous))
        {
            // Re-adding a reference number that already existed (e.g. a looped demo replay,
            // since real ITCH data never reuses these within a session) - remove its old
            // contribution first so this is a clean replace, not a duplicate addition stacked
            // on top of what's already resting at that price.
            AdjustLevel(previous.Side, previous.PriceInTicks, -previous.Shares);
        }

        AdjustLevel(side, priceInTicks, shares);
    }

    public void Execute(ulong orderReferenceNumber, uint executedShares)
    {
        if (TryFindOrderSlot(orderReferenceNumber, out int slotIndex))
        {
            ReduceShares(slotIndex, executedShares);
        }
    }

    public void Cancel(ulong orderReferenceNumber, uint canceledShares)
    {
        if (TryFindOrderSlot(orderReferenceNumber, out int slotIndex))
        {
            ReduceShares(slotIndex, canceledShares);
        }
    }

    /// <summary>Removes an order's entire remaining size - unlike Cancel, ITCH's Order Delete carries no shares field.</summary>
    public void Delete(ulong orderReferenceNumber)
    {
        if (TryFindOrderSlot(orderReferenceNumber, out int slotIndex))
        {
            ReduceShares(slotIndex, _orderSlots[slotIndex].Shares);
        }
    }

    /// <summary>
    /// Atomically retires <paramref name="originalOrderReferenceNumber"/> and adds its
    /// replacement under <paramref name="newOrderReferenceNumber"/> at the new price/size, on the
    /// same side as the original (ITCH's Order Replace carries no side field - a replace can't
    /// change it). A no-op if the original isn't resting, matching Delete/Cancel/Execute's
    /// safe-ignore behavior for orders outside this book's known history.
    /// </summary>
    public void Replace(ulong originalOrderReferenceNumber, ulong newOrderReferenceNumber, uint newPriceInTicks, uint newShares)
    {
        if (TryFindOrderSlot(originalOrderReferenceNumber, out int slotIndex))
        {
            Side side = _orderSlots[slotIndex].Side;
            ReduceShares(slotIndex, _orderSlots[slotIndex].Shares);
            AddOrder(newOrderReferenceNumber, side, newPriceInTicks, newShares);
        }
    }

    public readonly Bbo GetBbo()
    {
        return new Bbo
        {
            BidPriceInTicks = _bidLevelCount > 0 ? _bidLevels[0].PriceInTicks : null,
            BidShares = _bidLevelCount > 0 ? _bidLevels[0].AggregateShares : 0,
            AskPriceInTicks = _askLevelCount > 0 ? _askLevels[0].PriceInTicks : null,
            AskShares = _askLevelCount > 0 ? _askLevels[0].AggregateShares : 0
        };
    }

    /// <summary>Zero-copy view over resting bid levels, best-first. Valid until the next mutation.</summary>
    public readonly ReadOnlySpan<PriceLevel> GetBidDepth() => _bidLevels.AsSpan(0, _bidLevelCount);

    /// <summary>Zero-copy view over resting ask levels, best-first. Valid until the next mutation.</summary>
    public readonly ReadOnlySpan<PriceLevel> GetAskDepth() => _askLevels.AsSpan(0, _askLevelCount);

    private void ReduceShares(int slotIndex, uint sharesToRemove)
    {
        ref OrderSlot order = ref _orderSlots[slotIndex];
        AdjustLevel(order.Side, order.PriceInTicks, -sharesToRemove);

        if (sharesToRemove >= order.Shares)
        {
            RemoveOrderSlot(slotIndex);
        }
        else
        {
            order.Shares -= sharesToRemove;
        }
    }

    // ---- Order lookup: open addressing, linear probing, backward-shift deletion ----

    /// <summary>Returns true if this replaced an already-occupied slot (with its prior state in <paramref name="previous"/>).</summary>
    private bool InsertOrder(ulong orderReferenceNumber, Side side, uint priceInTicks, uint shares, out OrderSlot previous)
    {
        if (_orderCount + 1 > _orderSlots.Length * MaxLoadFactor)
        {
            GrowOrderTable();
        }

        int mask = _orderSlots.Length - 1;
        int index = HashIndex(orderReferenceNumber, _orderSlots.Length);

        while (_orderSlots[index].IsOccupied)
        {
            if (_orderSlots[index].OrderReferenceNumber == orderReferenceNumber)
            {
                break; // re-adding the same reference number - overwrite in place
            }

            index = (index + 1) & mask;
        }

        bool wasOccupied = _orderSlots[index].IsOccupied;
        previous = _orderSlots[index];

        _orderSlots[index] = new OrderSlot
        {
            OrderReferenceNumber = orderReferenceNumber,
            IsOccupied = true,
            Side = side,
            PriceInTicks = priceInTicks,
            Shares = shares
        };

        if (!wasOccupied)
        {
            _orderCount++;
        }

        return wasOccupied;
    }

    private readonly bool TryFindOrderSlot(ulong orderReferenceNumber, out int slotIndex)
    {
        int mask = _orderSlots.Length - 1;
        int index = HashIndex(orderReferenceNumber, _orderSlots.Length);

        while (_orderSlots[index].IsOccupied)
        {
            if (_orderSlots[index].OrderReferenceNumber == orderReferenceNumber)
            {
                slotIndex = index;
                return true;
            }

            index = (index + 1) & mask;
        }

        slotIndex = -1;
        return false;
    }

    /// <summary>
    /// Backward-shift deletion: after clearing the slot, walk forward through the probe sequence
    /// shifting any entry whose ideal position is at-or-before the gap back into it. Leaves no
    /// tombstones, so a later lookup miss is detected the instant it hits a truly-empty slot.
    /// </summary>
    private void RemoveOrderSlot(int slotIndex)
    {
        int mask = _orderSlots.Length - 1;
        _orderSlots[slotIndex] = default;
        _orderCount--;

        int gap = slotIndex;
        int probe = (gap + 1) & mask;

        while (_orderSlots[probe].IsOccupied)
        {
            int idealIndex = HashIndex(_orderSlots[probe].OrderReferenceNumber, _orderSlots.Length);
            int distanceToGap = (gap - idealIndex) & mask;
            int distanceToProbe = (probe - idealIndex) & mask;

            if (distanceToGap <= distanceToProbe)
            {
                _orderSlots[gap] = _orderSlots[probe];
                _orderSlots[probe] = default;
                gap = probe;
            }

            probe = (probe + 1) & mask;
        }
    }

    private void GrowOrderTable()
    {
        OrderSlot[] oldSlots = _orderSlots;
        _orderSlots = new OrderSlot[oldSlots.Length * 2];
        _orderCount = 0;

        foreach (OrderSlot slot in oldSlots)
        {
            if (slot.IsOccupied)
            {
                InsertOrderRaw(slot);
            }
        }
    }

    private void InsertOrderRaw(OrderSlot slot)
    {
        int mask = _orderSlots.Length - 1;
        int index = HashIndex(slot.OrderReferenceNumber, _orderSlots.Length);

        while (_orderSlots[index].IsOccupied)
        {
            index = (index + 1) & mask;
        }

        _orderSlots[index] = slot;
        _orderCount++;
    }

    private static int HashIndex(ulong key, int capacity)
    {
        ulong hash = key * HashMultiplier;
        hash ^= hash >> 32;
        return (int)(hash & (uint)(capacity - 1));
    }

    // ---- Price levels: sorted dense arrays, binary search ----

    private void AdjustLevel(Side side, uint priceInTicks, long shareDelta)
    {
        if (side == Side.Buy)
        {
            AdjustLevelArray(ref _bidLevels, ref _bidLevelCount, priceInTicks, shareDelta, descending: true);
        }
        else
        {
            AdjustLevelArray(ref _askLevels, ref _askLevelCount, priceInTicks, shareDelta, descending: false);
        }
    }

    private static void AdjustLevelArray(ref PriceLevel[] levels, ref int count, uint priceInTicks, long shareDelta, bool descending)
    {
        int index = BinarySearchLevel(levels, count, priceInTicks, descending);
        if (index >= 0)
        {
            long remaining = levels[index].AggregateShares + shareDelta;
            if (remaining <= 0)
            {
                RemoveLevelAt(levels, ref count, index);
            }
            else
            {
                levels[index].AggregateShares = remaining;
            }

            return;
        }

        if (shareDelta <= 0)
        {
            return; // defensive: reducing a level that doesn't exist - nothing to do
        }

        InsertLevelAt(ref levels, ref count, ~index, priceInTicks, shareDelta);
    }

    private static int BinarySearchLevel(PriceLevel[] levels, int count, uint priceInTicks, bool descending)
    {
        int lo = 0;
        int hi = count - 1;

        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2;
            int comparison = levels[mid].PriceInTicks.CompareTo(priceInTicks);
            if (descending)
            {
                comparison = -comparison;
            }

            if (comparison == 0)
            {
                return mid;
            }

            if (comparison < 0)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return ~lo;
    }

    private static void RemoveLevelAt(PriceLevel[] levels, ref int count, int index)
    {
        int tailLength = count - index - 1;
        if (tailLength > 0)
        {
            Array.Copy(levels, index + 1, levels, index, tailLength);
        }

        count--;
    }

    private static void InsertLevelAt(ref PriceLevel[] levels, ref int count, int index, uint priceInTicks, long shares)
    {
        if (count == levels.Length)
        {
            Array.Resize(ref levels, levels.Length * 2);
        }

        int tailLength = count - index;
        if (tailLength > 0)
        {
            Array.Copy(levels, index, levels, index + 1, tailLength);
        }

        levels[index] = new PriceLevel { PriceInTicks = priceInTicks, AggregateShares = shares };
        count++;
    }
}
