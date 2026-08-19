using System.Buffers;
using System.Buffers.Text;

namespace Boulevard.Protocol.Fix;

/// <summary>
/// Zero-allocation FIX message generator writing into a caller-owned buffer, with in-place
/// backpatching for BodyLength (tag 9) and CheckSum (tag 10) - mirrors the
/// ArrayBufferWriter/Utf8JsonWriter.Reset() reuse pattern in Boulevard.Edge.MarketData's
/// l2JsonBuffer: one reusable buffer across many writes, no per-message allocation.
///
/// Unlike the read-side ref structs in this project, this one is intentionally not readonly - it
/// owns a mutable write cursor into the caller's buffer.
/// </summary>
public ref struct FixMessageWriter
{
    private const byte EqualsByte = (byte)'=';
    private const byte SohByte = 0x01;

    // Zero-padded digits reserved for tag 9's value - 6 digits covers messages up to 999,999
    // bytes, comfortably beyond anything the message types in this codec produce.
    private const int BodyLengthFieldWidth = 6;

    private readonly Span<byte> _buffer;
    private int _position;
    private int _bodyLengthValueStart;
    private int _bodyStart;

    public FixMessageWriter(Span<byte> buffer)
    {
        _buffer = buffer;
        _position = 0;
        _bodyLengthValueStart = -1;
        _bodyStart = -1;
    }

    /// <summary>Writes the standard header through the reserved BodyLength slot. Call this first.</summary>
    public void WriteHeader(
        ReadOnlySpan<byte> beginString,
        ReadOnlySpan<byte> msgType,
        int msgSeqNum,
        ReadOnlySpan<byte> senderCompId,
        ReadOnlySpan<byte> targetCompId,
        DateTime sendingTime)
    {
        WriteTag(FixTags.BeginString, beginString);

        // Reserve a fixed-width, zero-padded slot for BodyLength so it can be backpatched once the
        // real value is known, without shifting any byte written after it.
        WriteRawTagPrefix(FixTags.BodyLength);
        _bodyLengthValueStart = _position;
        _buffer.Slice(_position, BodyLengthFieldWidth).Fill((byte)'0');
        _position += BodyLengthFieldWidth;
        _buffer[_position++] = SohByte;

        _bodyStart = _position;

        WriteTag(FixTags.MsgType, msgType);
        WriteTag(FixTags.MsgSeqNum, msgSeqNum);
        WriteTag(FixTags.SenderCompId, senderCompId);
        WriteTag(FixTags.TargetCompId, targetCompId);
        WriteUtcTimestampTag(FixTags.SendingTime, sendingTime);
    }

    public void WriteTag(int tag, ReadOnlySpan<byte> value)
    {
        WriteRawTagPrefix(tag);
        value.CopyTo(_buffer[_position..]);
        _position += value.Length;
        _buffer[_position++] = SohByte;
    }

    public void WriteTag(int tag, byte value)
    {
        WriteRawTagPrefix(tag);
        _buffer[_position++] = value;
        _buffer[_position++] = SohByte;
    }

    public void WriteTag(int tag, int value)
    {
        WriteRawTagPrefix(tag);
        Utf8Formatter.TryFormat(value, _buffer[_position..], out int written);
        _position += written;
        _buffer[_position++] = SohByte;
    }

    public void WriteTag(int tag, decimal value)
    {
        WriteRawTagPrefix(tag);
        Utf8Formatter.TryFormat(value, _buffer[_position..], out int written);
        _position += written;
        _buffer[_position++] = SohByte;
    }

    public void WriteUtcTimestampTag(int tag, DateTime value)
    {
        WriteRawTagPrefix(tag);
        WriteUtcTimestampValue(value);
        _buffer[_position++] = SohByte;
    }

    /// <summary>Backpatches BodyLength, appends CheckSum, and returns the fully written message.</summary>
    public Span<byte> Finish()
    {
        int bodyLength = _position - _bodyStart;
        Utf8Formatter.TryFormat(
            bodyLength,
            _buffer.Slice(_bodyLengthValueStart, BodyLengthFieldWidth),
            out _,
            new StandardFormat('D', BodyLengthFieldWidth));

        byte checksum = FixChecksum.Compute(_buffer[.._position]);
        WriteRawTagPrefix(FixTags.CheckSum);
        FixChecksum.TryFormat(checksum, _buffer.Slice(_position, 3));
        _position += 3;
        _buffer[_position++] = SohByte;

        return _buffer[.._position];
    }

    private void WriteRawTagPrefix(int tag)
    {
        Utf8Formatter.TryFormat(tag, _buffer[_position..], out int written);
        _position += written;
        _buffer[_position++] = EqualsByte;
    }

    private void WriteUtcTimestampValue(DateTime value)
    {
        // YYYYMMDD-HH:MM:SS.sss - always exactly 21 bytes.
        Span<byte> destination = _buffer.Slice(_position, 21);
        Utf8Formatter.TryFormat(value.Year, destination[..4], out _, new StandardFormat('D', 4));
        Utf8Formatter.TryFormat(value.Month, destination.Slice(4, 2), out _, new StandardFormat('D', 2));
        Utf8Formatter.TryFormat(value.Day, destination.Slice(6, 2), out _, new StandardFormat('D', 2));
        destination[8] = (byte)'-';
        Utf8Formatter.TryFormat(value.Hour, destination.Slice(9, 2), out _, new StandardFormat('D', 2));
        destination[11] = (byte)':';
        Utf8Formatter.TryFormat(value.Minute, destination.Slice(12, 2), out _, new StandardFormat('D', 2));
        destination[14] = (byte)':';
        Utf8Formatter.TryFormat(value.Second, destination.Slice(15, 2), out _, new StandardFormat('D', 2));
        destination[17] = (byte)'.';
        Utf8Formatter.TryFormat(value.Millisecond, destination.Slice(18, 3), out _, new StandardFormat('D', 3));
        _position += 21;
    }
}
