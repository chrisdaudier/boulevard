using System.Threading.Channels;

namespace Boulevard.Edge.MarketData;

/// <summary>
/// IDatagramQueue backed by two bounded System.Threading.Channels (SingleReader/SingleWriter):
/// one carrying free slot indices back to the producer, one carrying filled-slot descriptors
/// to the consumer. Both are only ever touched via non-blocking TryRead/TryWrite from the
/// producer side, so the socket thread never stalls.
/// </summary>
internal sealed class ChannelDatagramQueue : IDatagramQueue
{
    private readonly byte[][] _slots;
    private readonly Channel<int> _freeSlots;
    private readonly Channel<(int SlotIndex, int Length, long ReceiveTimestamp)> _filledSlots;

    public ChannelDatagramQueue(int capacity, int slotSize)
    {
        _slots = new byte[capacity][];
        for (int i = 0; i < capacity; i++)
        {
            _slots[i] = new byte[slotSize];
        }

        var channelOptions = new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        };

        _freeSlots = Channel.CreateBounded<int>(channelOptions);
        _filledSlots = Channel.CreateBounded<(int, int, long)>(channelOptions);

        for (int i = 0; i < capacity; i++)
        {
            _freeSlots.Writer.TryWrite(i);
        }
    }

    public bool TryAcquireSlot(out int slotIndex, out Memory<byte> buffer)
    {
        if (_freeSlots.Reader.TryRead(out slotIndex))
        {
            buffer = _slots[slotIndex];
            return true;
        }

        buffer = default;
        return false;
    }

    public bool TryEnqueue(int slotIndex, int length, long receiveTimestamp) =>
        _filledSlots.Writer.TryWrite((slotIndex, length, receiveTimestamp));

    public ValueTask<bool> WaitToDequeueAsync(CancellationToken cancellationToken) =>
        _filledSlots.Reader.WaitToReadAsync(cancellationToken);

    public bool TryDequeue(out int slotIndex, out int length, out long receiveTimestamp)
    {
        if (_filledSlots.Reader.TryRead(out (int SlotIndex, int Length, long ReceiveTimestamp) item))
        {
            slotIndex = item.SlotIndex;
            length = item.Length;
            receiveTimestamp = item.ReceiveTimestamp;
            return true;
        }

        slotIndex = default;
        length = default;
        receiveTimestamp = default;
        return false;
    }

    public ReadOnlySpan<byte> GetSlotData(int slotIndex, int length) => _slots[slotIndex].AsSpan(0, length);

    public void ReleaseSlot(int slotIndex) => _freeSlots.Writer.TryWrite(slotIndex);

    public void CompleteAdding() => _filledSlots.Writer.TryComplete();

    public void Dispose()
    {
        _freeSlots.Writer.TryComplete();
        _filledSlots.Writer.TryComplete();
    }
}
