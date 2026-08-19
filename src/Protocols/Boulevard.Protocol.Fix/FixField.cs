namespace Boulevard.Protocol.Fix;

/// <summary>
/// One decoded tag=value pair from a FIX message body. A zero-copy view over the source
/// message's bytes - like all ref structs here, it's only valid for as long as the buffer it
/// points into is (see FixMessageReader).
/// </summary>
public readonly ref struct FixField
{
    public int Tag { get; }
    public ReadOnlySpan<byte> Value { get; }

    public FixField(int tag, ReadOnlySpan<byte> value)
    {
        Tag = tag;
        Value = value;
    }
}
