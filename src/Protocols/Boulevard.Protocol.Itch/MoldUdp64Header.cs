using System.Buffers.Binary;

namespace Boulevard.Protocol.Itch;

public readonly ref struct MoldUdp64Header
{
    public const int Size = 20;

    public ReadOnlySpan<byte> Session { get; }
    public ulong SequenceNumber { get; }
    public ushort MessageCount { get; }

    private MoldUdp64Header(ReadOnlySpan<byte> session, ulong sequenceNumber, ushort messageCount)
    {
        Session = session;
        SequenceNumber = sequenceNumber;
        MessageCount = messageCount;
    }

    public static MoldUdp64Header Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < Size)
        {
            throw new ArgumentException($"MoldUDP64 header requires at least {Size} bytes.", nameof(data));
        }

        return new MoldUdp64Header(
            session: data[..10],
            sequenceNumber: BinaryPrimitives.ReadUInt64BigEndian(data.Slice(10, 8)),
            messageCount: BinaryPrimitives.ReadUInt16BigEndian(data.Slice(18, 2)));
    }
}
