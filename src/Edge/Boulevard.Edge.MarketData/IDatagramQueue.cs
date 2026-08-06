namespace Boulevard.Edge.MarketData;

/// <summary>
/// Zero-allocation handoff between the socket (producer) thread and the worker (consumer)
/// thread. Deliberately abstracted behind an interface so the backing implementation
/// (currently System.Threading.Channels) can be swapped for a hand-rolled SPSC ring buffer
/// later without touching either thread's loop.
/// </summary>
internal interface IDatagramQueue : IDisposable
{
    /// <summary>Producer side - never blocks. False means the queue is full; caller should drop the datagram.</summary>
    bool TryAcquireSlot(out int slotIndex, out Memory<byte> buffer);

    /// <summary>Producer side - never blocks. False means the caller should release the slot itself.</summary>
    bool TryEnqueue(int slotIndex, int length, long receiveTimestamp);

    /// <summary>Consumer side - suspends (no busy-spin) until an item is available or the queue completes.</summary>
    ValueTask<bool> WaitToDequeueAsync(CancellationToken cancellationToken);

    /// <summary>Consumer side - never blocks.</summary>
    bool TryDequeue(out int slotIndex, out int length, out long receiveTimestamp);

    ReadOnlySpan<byte> GetSlotData(int slotIndex, int length);

    /// <summary>Consumer side - returns a slot to the free pool once its data has been processed.</summary>
    void ReleaseSlot(int slotIndex);

    /// <summary>Producer side - signals no more items will be enqueued, so the consumer can drain and exit.</summary>
    void CompleteAdding();
}
