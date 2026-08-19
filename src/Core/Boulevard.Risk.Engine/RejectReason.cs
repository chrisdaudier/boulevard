namespace Boulevard.Risk.Engine;

public enum RejectReason
{
    None,
    OrderSizeExceeded,
    PriceBandExceeded,
    SelfTradePrevented
}
