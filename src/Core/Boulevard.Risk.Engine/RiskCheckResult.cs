namespace Boulevard.Risk.Engine;

/// <summary>
/// Pass/reject outcome of a pre-trade check - a plain struct, not an exception, matching this
/// codebase's TryParse-style non-throwing convention. Deliberately carries no string: formatting a
/// human-readable message from Reason (plus whatever primitives the caller already has - symbol
/// id, price, threshold) is the caller's job, only when it actually needs to log/display a
/// rejection - not part of the check itself, so the reject path stays allocation-free.
/// </summary>
public readonly struct RiskCheckResult
{
    public bool Passed { get; }
    public RejectReason Reason { get; }

    private RiskCheckResult(bool passed, RejectReason reason)
    {
        Passed = passed;
        Reason = reason;
    }

    public static RiskCheckResult Pass() => new(true, RejectReason.None);
    public static RiskCheckResult Reject(RejectReason reason) => new(false, reason);
}
