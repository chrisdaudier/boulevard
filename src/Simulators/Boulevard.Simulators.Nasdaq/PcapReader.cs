using System.Buffers.Binary;

namespace Boulevard.Simulators.Nasdaq;

/// <summary>Reads classic (non-pcapng) nanosecond-resolution pcap records from a stream.</summary>
public sealed class PcapReader
{
    private const uint NanosecondMagicLittleEndian = 0xa1b23c4d;
    private const int GlobalHeaderSize = 24;
    private const int RecordHeaderSize = 16;

    private readonly Stream _stream;
    private byte[] _packetBuffer = new byte[262144];

    public PcapReader(Stream stream)
    {
        _stream = stream;

        Span<byte> globalHeader = stackalloc byte[GlobalHeaderSize];
        _stream.ReadExactly(globalHeader);

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(globalHeader);
        if (magic != NanosecondMagicLittleEndian)
        {
            throw new NotSupportedException(
                $"Unsupported pcap magic number 0x{magic:x8}; expected nanosecond-resolution little-endian pcap (0x{NanosecondMagicLittleEndian:x8}).");
        }

        uint linkType = BinaryPrimitives.ReadUInt32LittleEndian(globalHeader.Slice(20, 4));
        if (linkType != 1)
        {
            throw new NotSupportedException($"Unsupported pcap link type {linkType}; expected Ethernet (1).");
        }
    }

    public bool TryReadNextPacket(out ReadOnlySpan<byte> packetData)
    {
        int firstByte = _stream.ReadByte();
        if (firstByte == -1)
        {
            packetData = default;
            return false;
        }

        Span<byte> recordHeader = stackalloc byte[RecordHeaderSize];
        recordHeader[0] = (byte)firstByte;
        _stream.ReadExactly(recordHeader[1..]);

        uint inclLen = BinaryPrimitives.ReadUInt32LittleEndian(recordHeader.Slice(8, 4));
        if (_packetBuffer.Length < inclLen)
        {
            _packetBuffer = new byte[inclLen];
        }

        _stream.ReadExactly(_packetBuffer, 0, (int)inclLen);
        packetData = _packetBuffer.AsSpan(0, (int)inclLen);
        return true;
    }
}
