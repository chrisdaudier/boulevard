using System.Buffers.Binary;

namespace Boulevard.Protocol.Itch;

/// <summary>Walks a MoldUDP64 packet's message blocks. Usage: foreach (var msg in new MoldUdp64Reader(udpPayload)).</summary>
public readonly ref struct MoldUdp64Reader
{
    private readonly ReadOnlySpan<byte> _payload;

    public MoldUdp64Reader(ReadOnlySpan<byte> udpPayload)
    {
        _payload = udpPayload;
    }

    public MoldUdp64Header Header => MoldUdp64Header.Parse(_payload);

    public Enumerator GetEnumerator() => new(_payload);

    public ref struct Enumerator
    {
        private ReadOnlySpan<byte> _remaining;
        private ushort _messagesLeft;

        internal Enumerator(ReadOnlySpan<byte> udpPayload)
        {
            MoldUdp64Header header = MoldUdp64Header.Parse(udpPayload);
            _messagesLeft = header.MessageCount;
            _remaining = udpPayload[MoldUdp64Header.Size..];
            Current = default;
        }

        public ReadOnlySpan<byte> Current { get; private set; }

        public bool MoveNext()
        {
            if (_messagesLeft == 0 || _remaining.Length < 2)
            {
                return false;
            }

            ushort messageLength = BinaryPrimitives.ReadUInt16BigEndian(_remaining);
            _remaining = _remaining[2..];

            if (_remaining.Length < messageLength)
            {
                return false;
            }

            Current = _remaining[..messageLength];
            _remaining = _remaining[messageLength..];
            _messagesLeft--;
            return true;
        }
    }
}
