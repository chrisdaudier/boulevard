namespace Boulevard.Protocol.Fix;

/// <summary>
/// Zero-copy tag=value walker for a FIX message body delimited by SOH (0x01) - the FIX-specific
/// twin of Boulevard.Protocol.Itch's MoldUdp64Reader, adapted for delimiter-scanned text fields
/// instead of length-prefixed binary sub-messages. Each field is decoded by scanning for '=' then
/// SOH rather than reading a fixed offset, since FIX fields are variable-length.
/// </summary>
public readonly ref struct FixMessageReader
{
    private readonly ReadOnlySpan<byte> _payload;

    public FixMessageReader(ReadOnlySpan<byte> payload)
    {
        _payload = payload;
    }

    public Enumerator GetEnumerator() => new(_payload);

    public ref struct Enumerator
    {
        private const byte EqualsByte = (byte)'=';
        private const byte SohByte = 0x01;

        private readonly int _originalLength;
        private ReadOnlySpan<byte> _remaining;

        internal Enumerator(ReadOnlySpan<byte> payload)
        {
            _originalLength = payload.Length;
            _remaining = payload;
            Current = default;
        }

        public FixField Current { get; private set; }

        /// <summary>
        /// Bytes consumed so far - the offset immediately after Current's terminating SOH. Callers
        /// that need byte offsets within the original message (BodyLength/CheckSum validation) read
        /// this once per field rather than re-scanning the message a second time.
        /// </summary>
        public readonly int ConsumedBytes => _originalLength - _remaining.Length;

        public bool MoveNext()
        {
            if (_remaining.IsEmpty)
            {
                return false;
            }

            int equalsIndex = _remaining.IndexOf(EqualsByte);
            if (equalsIndex <= 0)
            {
                return false; // no '=' found, or an empty tag - malformed either way
            }

            if (!FixValueParser.TryParseInt32(_remaining[..equalsIndex], out int tag))
            {
                return false;
            }

            ReadOnlySpan<byte> afterEquals = _remaining[(equalsIndex + 1)..];
            int sohIndex = afterEquals.IndexOf(SohByte);
            if (sohIndex < 0)
            {
                return false; // unterminated final field
            }

            Current = new FixField(tag, afterEquals[..sohIndex]);
            _remaining = afterEquals[(sohIndex + 1)..];
            return true;
        }
    }
}
