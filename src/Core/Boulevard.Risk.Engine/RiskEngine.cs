namespace Boulevard.Risk.Engine;

/// <summary>
/// Pre-trade risk checks (fat-finger/price-size bands, duplicate/self-trade prevention) over
/// array-backed per-symbol/per-account state, following Boulevard.MarketData.Engine.OrderBook's
/// convention: no Dictionary/string lookup on the check path, only array indexing by dense ids.
///
/// Symbol strings are resolved to dense ids via RegisterSymbol, a registration-time-only
/// operation mirroring the tickerByLocate-style dictionaries already used at Boulevard.Edge's
/// setup/config layer, not on any hot path. Account ids are assumed to already be dense small
/// integers supplied by the caller (typical for an OMS's own account numbering) - this engine
/// does not register or resolve them, so a caller working from external account identifiers is
/// responsible for its own account-id -> dense-int mapping, the same way it already owns
/// symbol/account resolution for whatever protocol it's bridging (e.g. Boulevard.Protocol.Fix).
///
/// This class takes primitives (symbolId, accountId, side, price, quantity), not a parsed FIX
/// message - keeping it protocol-agnostic, matching MarketData.Engine having zero dependency on
/// Boulevard.Protocol.Itch. Translating a parsed NewOrderSingleMessage into these primitives is a
/// future caller's job (e.g. a session/edge-tier service), not this engine's.
///
/// Not thread-safe by design - like OrderBook, this assumes a single-writer-thread ownership
/// model. A caller needing concurrent access must provide its own external synchronization (the
/// same way Boulevard.Edge.MarketData wraps OrderBook access in its own bookLock rather than
/// OrderBook locking internally).
/// </summary>
public sealed class RiskEngine
{
    private readonly Dictionary<string, int> _symbolIdsByName = [];
    private readonly int _initialAccountCapacity;

    private SymbolRiskState[] _symbols;
    private int _symbolCount;

    // [symbolId][accountId] - both dimensions grow on demand, sized for a modest pod-scale
    // deployment (a handful of accounts trading tens of symbols), not the busiest possible venue -
    // same amortized-growth convention as OrderBook's own tables, not a hard cap.
    private AccountSymbolBook[][] _restingOrders;

    public RiskEngine(int initialSymbolCapacity = 16, int initialAccountCapacity = 8)
    {
        _symbols = new SymbolRiskState[initialSymbolCapacity];
        _restingOrders = new AccountSymbolBook[initialSymbolCapacity][];
        _initialAccountCapacity = initialAccountCapacity;
    }

    /// <summary>
    /// Registration-time only - not part of the hot check path. Idempotent: re-registering an
    /// already-known symbol returns its existing id rather than creating a duplicate.
    /// </summary>
    public int RegisterSymbol(string symbol, uint maxOrderSize, uint maxDeviationBasisPoints, uint initialReferencePriceInTicks = 0)
    {
        if (_symbolIdsByName.TryGetValue(symbol, out int existingId))
        {
            return existingId;
        }

        if (_symbolCount == _symbols.Length)
        {
            Array.Resize(ref _symbols, _symbols.Length * 2);
            Array.Resize(ref _restingOrders, _restingOrders.Length * 2);
        }

        int symbolId = _symbolCount++;
        _symbols[symbolId] = new SymbolRiskState
        {
            MaxOrderSize = maxOrderSize,
            MaxDeviationBasisPoints = maxDeviationBasisPoints,
            ReferencePriceInTicks = initialReferencePriceInTicks
        };
        _restingOrders[symbolId] = new AccountSymbolBook[_initialAccountCapacity];
        _symbolIdsByName[symbol] = symbolId;
        return symbolId;
    }

    /// <summary>Registration-time only. Returns false if <paramref name="symbol"/> was never registered.</summary>
    public bool TryGetSymbolId(string symbol, out int symbolId) => _symbolIdsByName.TryGetValue(symbol, out symbolId);

    /// <summary>
    /// Updates the price-band reference for a symbol - e.g. driven by the last trade or BBO
    /// midpoint from a live book in a real deployment. A single latest value, not a history: this
    /// engine has exactly one static reference per symbol at a time, so there's nothing to keep a
    /// bounded ring of (unlike Boulevard.Edge.MarketData's latencySamplesUs, which exists because
    /// it retains many recent samples for percentile computation - there's no equivalent "many
    /// recent values" need here). If a smoothed/windowed reference price is wanted later, that's
    /// the natural place to add one.
    /// </summary>
    public void UpdateReferencePrice(int symbolId, uint referencePriceInTicks)
    {
        _symbols[symbolId].ReferencePriceInTicks = referencePriceInTicks;
    }

    /// <summary>Registers one of an account's own resting orders, for future self-trade checks against it.</summary>
    public void AddRestingOrder(int symbolId, int accountId, long orderId, Side side, uint priceInTicks, uint quantity)
    {
        GetOrGrowAccountBook(symbolId, accountId).Add(orderId, side, priceInTicks, quantity);
    }

    /// <summary>Removes a resting order (filled/canceled) so it stops being checked against.</summary>
    public void RemoveRestingOrder(int symbolId, int accountId, long orderId)
    {
        GetOrGrowAccountBook(symbolId, accountId).Remove(orderId);
    }

    /// <summary>
    /// The hot check path: array indexing only, no allocation, no locks (see the class-level
    /// remarks on thread-safety). Returns the same value as <c>result.Passed</c>, for
    /// <c>if (engine.TryCheck(...))</c>-style call sites that don't need the reject reason.
    /// </summary>
    public bool TryCheck(int symbolId, int accountId, Side side, uint priceInTicks, uint quantity, out RiskCheckResult result)
    {
        ref SymbolRiskState state = ref _symbols[symbolId];

        if (quantity > state.MaxOrderSize)
        {
            result = RiskCheckResult.Reject(RejectReason.OrderSizeExceeded);
            return false;
        }

        if (state.ReferencePriceInTicks > 0)
        {
            long deviation = Math.Abs((long)priceInTicks - state.ReferencePriceInTicks);
            long deviationBasisPoints = deviation * 10_000 / state.ReferencePriceInTicks;
            if (deviationBasisPoints > state.MaxDeviationBasisPoints)
            {
                result = RiskCheckResult.Reject(RejectReason.PriceBandExceeded);
                return false;
            }
        }

        AccountSymbolBook[] accounts = _restingOrders[symbolId];
        if (accountId < accounts.Length && accounts[accountId].CrossesResting(side, priceInTicks))
        {
            result = RiskCheckResult.Reject(RejectReason.SelfTradePrevented);
            return false;
        }

        result = RiskCheckResult.Pass();
        return true;
    }

    private ref AccountSymbolBook GetOrGrowAccountBook(int symbolId, int accountId)
    {
        AccountSymbolBook[] accounts = _restingOrders[symbolId];
        if (accountId >= accounts.Length)
        {
            int newLength = accounts.Length;
            while (newLength <= accountId)
            {
                newLength *= 2;
            }

            Array.Resize(ref accounts, newLength);
            _restingOrders[symbolId] = accounts;
        }

        return ref accounts[accountId];
    }
}
