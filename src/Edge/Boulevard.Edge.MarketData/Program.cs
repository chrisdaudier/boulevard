using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using Boulevard.Edge.MarketData;
using Boulevard.MarketData.Engine;
using Boulevard.Protocol.Itch;

const string MulticastIp = "239.255.0.1";
const int MulticastPort = 1234;
const int ReceiveBufferSize = 2048;

// Socket thread and worker thread run on dedicated OS threads (not the ThreadPool) so each can
// be pinned to its own CPU core on Linux - isolating I/O from processing is the whole point of
// this decoupling, and ThreadPool-serviced completions can't be reliably pinned at all.
const int SocketThreadCore = 0;
const int WorkerThreadCore = 1;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    Console.WriteLine("\n[EDGE] Shutdown signal received (SIGINT).");
    eventArgs.Cancel = true;
    cts.Cancel();
};

// Console.CancelKeyPress only catches SIGINT/Ctrl+C - `docker stop` sends SIGTERM, which
// needs its own handler to shut down gracefully (print final summary) instead of just dying.
using PosixSignalRegistration sigTermRegistration = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
{
    Console.WriteLine("\n[EDGE] Shutdown signal received (SIGTERM).");
    context.Cancel = true;
    cts.Cancel();
});

long packetCount = 0;
long addCount = 0;
long executeCount = 0;
long cancelCount = 0;
long deleteCount = 0;
long replaceCount = 0;
long executeWithPriceCount = 0;
long crossTradeCount = 0;
long otherCount = 0;
long errorCount = 0;
long ringOverflowCount = 0;

// --- Socket-thread-owned state (sequencing/reorder). Guarded by sequenceLock, which exists
// only to synchronize against the reporting thread's periodic cross-thread reads - the socket
// thread itself is the sole mutator. ---
var sequenceLock = new object();
ulong? expectedNextSequence = null;
long sequenceGapCount = 0;
long missingMessageCount = 0;
long duplicateCount = 0;

// A genuine duplicate/retransmission is behind the expected sequence by a handful of numbers
// at most - a jump this large means the publisher restarted its replay from the beginning.
const ulong SessionRestartThreshold = 100_000;

// Reorder tolerance: a datagram that arrives ahead of the expected sequence is held here
// rather than immediately counted as a gap, since jittery delivery (e.g. tc netem) can
// genuinely reorder UDP datagrams without any of them actually being lost. Only if the
// missing range doesn't show up within ReorderTimeout do we give up and count a real gap.
const int ReorderWindowCapacity = 64;
long reorderTimeoutTicks = (long)(0.005 * Stopwatch.Frequency); // 5ms
var pendingBufferPool = new byte[ReorderWindowCapacity][];
for (int i = 0; i < ReorderWindowCapacity; i++)
{
    pendingBufferPool[i] = new byte[ReceiveBufferSize];
}

var freeBufferIndices = new Stack<int>(ReorderWindowCapacity);
for (int i = ReorderWindowCapacity - 1; i >= 0; i--)
{
    freeBufferIndices.Push(i);
}

// Keyed by each buffered packet's starting MoldUDP64 sequence number.
var pending = new SortedDictionary<ulong, (int BufferIndex, int Length, long ReceivedAtTicks)>();

// --- Worker-thread-owned state (ITCH parsing / OrderBook mutation). Guarded by bookLock, same
// reasoning as sequenceLock - the worker thread is the sole mutator. ---
var bookLock = new object();
var books = new Dictionary<ushort, OrderBook>();

// Resolved from ITCH Stock Directory ('R') messages. Message counts drive the "top 200 most
// active" selection for L2 distribution below - tracked for every message type we see (not
// just Add/Execute/Cancel), so it reflects real activity even for message types we don't
// otherwise parse (e.g. Order Delete).
var tickerByLocate = new Dictionary<ushort, string>();
var messageCountByLocate = new Dictionary<ushort, long>();

// Sized to comfortably exceed the ~464K Add/Execute/Cancel messages this capture produces,
// so recording a latency sample never triggers a reallocation on the hot path.
var latencySamplesUs = new List<long>(1_000_000);

// Zero-allocation handoff from the socket thread to the worker thread. Behind an interface
// so a future hand-rolled SPSC ring buffer can replace this without touching either thread's
// loop - this is the only line that would need to change.
// 8192 proved too small under sustained real-time load once the worker thread hit periodic
// stalls (see PublishL2Snapshots) - 65536 gives ~8x the burst headroom at a still-trivial ~134MB.
IDatagramQueue datagramQueue = new ChannelDatagramQueue(capacity: 65536, ReceiveBufferSize);

// Defaults to "let the OS pick the route" (correct in a container, which has exactly one real
// interface). Set MULTICAST_LOCAL_ADDRESS=127.0.0.1 only when running publisher+subscriber as
// two processes on the same host with multiple virtual/bridged adapters (e.g. this Mac's
// en1-en4), where an unpinned interface can cause duplicate delivery of the same datagram.
IPAddress multicastLocalAddress = IPAddress.TryParse(Environment.GetEnvironmentVariable("MULTICAST_LOCAL_ADDRESS"), out IPAddress? parsedAddress)
    ? parsedAddress
    : IPAddress.Any;

var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

// OS default kernel receive buffers are easily overrun by an as-fast-as-possible replay burst
// (observed real packet loss during a >700MB AFAP catch-up pass) - request a much larger buffer;
// the OS clamps to its own ceiling if this exceeds it, so it's safe to ask for more than needed.
socket.ReceiveBufferSize = 16 * 1024 * 1024;

socket.Bind(new IPEndPoint(IPAddress.Any, MulticastPort));
socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(IPAddress.Parse(MulticastIp), multicastLocalAddress));

Console.WriteLine($"[EDGE] Bound to 0.0.0.0:{MulticastPort}, joined multicast group {MulticastIp}");
Console.WriteLine("[EDGE] Waiting for datagrams. Press CTRL+C to exit.\n");

// Local distribution channel to Boulevard.Edge.SolaceGateway - a thin UDP-to-MQTT bridge with
// no ITCH/OrderBook knowledge of its own. Each datagram is a full top-of-book snapshot (not a
// delta), so a dropped one just gets superseded by the next tick - no sequencing needed here,
// unlike the authoritative L3 feed above.
const int L2PublishPort = 5001;
const int L2DepthLevels = 10;
const int L2TrackedSymbolCount = 200;

// Recency weighting for the "most active" ranking: a plain lifetime-cumulative count would let
// a ticker that was only active in an initial burst (e.g. the opening auction) permanently
// dominate the top-200 selection even after it goes quiet. Decaying by 1% every 250ms publish
// tick (~17s half-life without new activity) means the ranking reflects what's trading now.
const int DecayNumerator = 99;
const int DecayDenominator = 100;
var l2PublishSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
var l2PublishEndpoint = new IPEndPoint(IPAddress.Loopback, L2PublishPort);

var socketThread = new Thread(SocketReceiveLoop) { Name = "Edge-Socket", IsBackground = true };
var workerThread = new Thread(WorkerLoop) { Name = "Edge-Worker", IsBackground = true };
socketThread.Start();
workerThread.Start();

var reportingTask = Task.Run(async () =>
{
    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
    while (await timer.WaitForNextTickAsync(cts.Token))
    {
        lock (sequenceLock)
        {
            // Ensures a genuinely lost packet still gets declared a gap even during a lull
            // with no new arrivals to trigger the opportunistic check in HandleIncomingPacket.
            FlushExpiredPending(Stopwatch.GetTimestamp());
        }

        PrintSnapshot(live: true);
    }
}, cts.Token);

var l2PublishTask = Task.Run(async () =>
{
    using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
    while (await timer.WaitForNextTickAsync(cts.Token))
    {
        PublishL2Snapshots();
    }
}, cts.Token);

try
{
    await Task.Delay(Timeout.InfiniteTimeSpan, cts.Token);
}
catch (OperationCanceledException)
{
    // Expected on CTRL+C.
}

try
{
    await reportingTask;
}
catch (OperationCanceledException)
{
    // Expected on CTRL+C.
}

try
{
    await l2PublishTask;
}
catch (OperationCanceledException)
{
    // Expected on CTRL+C.
}

// Disposing the socket unblocks the socket thread's blocking Receive() call with an exception.
socket.Dispose();
socketThread.Join();

// No more datagrams can be enqueued now that the socket thread has stopped - let the worker
// drain whatever's left, then it'll exit once WaitToDequeueAsync reports completion.
datagramQueue.CompleteAdding();
workerThread.Join();

lock (sequenceLock)
{
    // No more datagrams are coming - whatever's still buffered is a genuine gap now.
    FlushExpiredPending(Stopwatch.GetTimestamp(), forceAll: true);
}

Console.WriteLine();
Console.WriteLine("[EDGE] Final summary");
PrintSnapshot(live: false);

datagramQueue.Dispose();
l2PublishSocket.Dispose();

void SocketReceiveLoop()
{
    LinuxThreadAffinity.TryPinCurrentThreadTo(SocketThreadCore, "socket");

    // Disposing the socket from another thread does not reliably interrupt an in-flight
    // blocking Receive() on every platform (observed hanging indefinitely on macOS) - a receive
    // timeout guarantees this loop wakes up periodically to recheck cancellation regardless.
    socket.ReceiveTimeout = 500;

    var receiveBuffer = new byte[ReceiveBufferSize];

    while (!cts.IsCancellationRequested)
    {
        int bytesReceived;
        try
        {
            bytesReceived = socket.Receive(receiveBuffer);
        }
        catch (ObjectDisposedException)
        {
            return; // shutting down
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
        {
            continue; // just a wakeup to recheck cancellation
        }
        catch (SocketException)
        {
            if (cts.IsCancellationRequested)
            {
                return;
            }

            Interlocked.Increment(ref errorCount);
            continue;
        }

        if (bytesReceived == 0)
        {
            continue;
        }

        long receiveTimestamp = Stopwatch.GetTimestamp();
        Interlocked.Increment(ref packetCount);

        ReadOnlySpan<byte> datagram = receiveBuffer.AsSpan(0, bytesReceived);
        if (datagram.Length < MoldUdp64Header.Size)
        {
            continue;
        }

        MoldUdp64Header header = MoldUdp64Header.Parse(datagram);

        lock (sequenceLock)
        {
            HandleIncomingPacket(header, datagram, receiveTimestamp);
        }
    }
}

void WorkerLoop()
{
    LinuxThreadAffinity.TryPinCurrentThreadTo(WorkerThreadCore, "worker");

    try
    {
        // ValueTask's awaiter doesn't support blocking GetResult() before completion (unlike
        // Task<T>) - AsTask() gives this dedicated thread something it can actually block on.
        while (datagramQueue.WaitToDequeueAsync(cts.Token).AsTask().GetAwaiter().GetResult())
        {
            while (datagramQueue.TryDequeue(out int slotIndex, out int length, out long receiveTimestamp))
            {
                ReadOnlySpan<byte> datagram = datagramQueue.GetSlotData(slotIndex, length);

                lock (bookLock)
                {
                    DispatchMessages(datagram, receiveTimestamp);
                }

                datagramQueue.ReleaseSlot(slotIndex);
            }
        }
    }
    catch (OperationCanceledException)
    {
        // Expected on shutdown.
    }
}

// Must be called while holding sequenceLock. Fast path: an in-order packet is handed to the
// worker thread immediately and then any now-contiguous buffered packets are drained. A packet
// ahead of the expected sequence is held in the reorder buffer rather than immediately flagged
// as a gap - it might just be jitter-delayed, not lost.
void HandleIncomingPacket(MoldUdp64Header header, ReadOnlySpan<byte> datagram, long receiveTimestamp)
{
    if (header.MessageCount == 0)
    {
        return; // heartbeat - no messages, no sequence advance
    }

    if (expectedNextSequence.HasValue && header.SequenceNumber < expectedNextSequence.Value)
    {
        ulong behindBy = expectedNextSequence.Value - header.SequenceNumber;
        if (behindBy > SessionRestartThreshold)
        {
            // A real duplicate/retransmission would be behind by a handful of sequence numbers
            // at most. A jump this large means the publisher looped its replay back to the
            // beginning (e.g. --loop) - treat it as a fresh session rather than permanently
            // rejecting everything from here on as "duplicates" of a session that's now over.
            foreach ((int BufferIndex, int Length, long ReceivedAtTicks) entry in pending.Values)
            {
                freeBufferIndices.Push(entry.BufferIndex);
            }

            pending.Clear();
            expectedNextSequence = null;

            // The publisher looped back to the start of its replay - the exact same order
            // lifecycles (same reference numbers, same session) are about to replay from
            // scratch. Without clearing book state here, replaying an Order Replace for an
            // order that was already fully consumed by the end of the prior lap becomes a
            // no-op (its "original" reference no longer exists in this lap's book), silently
            // dropping the replacement and leaving stale liquidity resting forever - the same
            // class of bug the pre-open catch-up replay fixes for a cold start, but
            // self-inflicted at every lap boundary if left unhandled here. tickerByLocate is
            // deliberately left alone - ticker resolution doesn't need to be relearned just
            // because the order-by-order session restarted.
            lock (bookLock)
            {
                books.Clear();
            }

            Console.WriteLine("[EDGE] Detected publisher loop/session restart - cleared OrderBook state for a fresh lap.");
        }
        else
        {
            duplicateCount++;
            return;
        }
    }

    expectedNextSequence ??= header.SequenceNumber;

    if (header.SequenceNumber == expectedNextSequence.Value)
    {
        EnqueueForWorker(datagram, receiveTimestamp);
        expectedNextSequence = header.SequenceNumber + header.MessageCount;
        DrainPending();
        return;
    }

    BufferPending(header, datagram, receiveTimestamp);
    FlushExpiredPending(receiveTimestamp);
}

// Must be called on the socket thread - copies the datagram into a pooled slot and hands it to
// the worker thread. Never blocks: if the queue is full (worker falling behind), the datagram
// is dropped and counted separately from network-level sequence gaps, since sequencing already
// succeeded here - it's specifically the downstream processing that couldn't keep up.
void EnqueueForWorker(ReadOnlySpan<byte> datagram, long receiveTimestamp)
{
    if (!datagramQueue.TryAcquireSlot(out int slotIndex, out Memory<byte> buffer))
    {
        Interlocked.Increment(ref ringOverflowCount);
        return;
    }

    datagram.CopyTo(buffer.Span);

    if (!datagramQueue.TryEnqueue(slotIndex, datagram.Length, receiveTimestamp))
    {
        datagramQueue.ReleaseSlot(slotIndex);
        Interlocked.Increment(ref ringOverflowCount);
    }
}

// Must be called while holding bookLock - this is the worker thread's ITCH parsing + OrderBook
// mutation path, and the natural future home for strategy execution.
void DispatchMessages(ReadOnlySpan<byte> datagram, long receiveTimestamp)
{
    foreach (ReadOnlySpan<byte> message in new MoldUdp64Reader(datagram))
    {
        if (message.Length == 0)
        {
            continue;
        }

        switch (message[0])
        {
            case AddOrderMessage.MessageType when AddOrderMessage.TryParse(message, out AddOrderMessage add):
            {
                addCount++;
                IncrementMessageCount(add.StockLocate);
                OrderBook book = books.TryGetValue(add.StockLocate, out OrderBook existing) ? existing : new OrderBook();
                book.AddOrder(add.OrderReferenceNumber, add.IsBuy ? Side.Buy : Side.Sell, add.PriceRaw, add.Shares);
                books[add.StockLocate] = book;
                RecordLatency(book, receiveTimestamp);
                break;
            }

            case OrderExecutedMessage.MessageType when OrderExecutedMessage.TryParse(message, out OrderExecutedMessage exec):
            {
                executeCount++;
                IncrementMessageCount(exec.StockLocate);
                if (books.TryGetValue(exec.StockLocate, out OrderBook execBook))
                {
                    execBook.Execute(exec.OrderReferenceNumber, exec.ExecutedShares);
                    books[exec.StockLocate] = execBook;
                    RecordLatency(execBook, receiveTimestamp);
                }

                break;
            }

            case OrderCancelMessage.MessageType when OrderCancelMessage.TryParse(message, out OrderCancelMessage cancel):
            {
                cancelCount++;
                IncrementMessageCount(cancel.StockLocate);
                if (books.TryGetValue(cancel.StockLocate, out OrderBook cancelBook))
                {
                    cancelBook.Cancel(cancel.OrderReferenceNumber, cancel.CanceledShares);
                    books[cancel.StockLocate] = cancelBook;
                    RecordLatency(cancelBook, receiveTimestamp);
                }

                break;
            }

            case OrderDeleteMessage.MessageType when OrderDeleteMessage.TryParse(message, out OrderDeleteMessage delete):
            {
                deleteCount++;
                IncrementMessageCount(delete.StockLocate);
                if (books.TryGetValue(delete.StockLocate, out OrderBook deleteBook))
                {
                    deleteBook.Delete(delete.OrderReferenceNumber);
                    books[delete.StockLocate] = deleteBook;
                    RecordLatency(deleteBook, receiveTimestamp);
                }

                break;
            }

            case AddOrderMpidMessage.MessageType when AddOrderMpidMessage.TryParse(message, out AddOrderMpidMessage addMpid):
            {
                addCount++;
                IncrementMessageCount(addMpid.StockLocate);
                OrderBook mpidBook = books.TryGetValue(addMpid.StockLocate, out OrderBook existingMpid) ? existingMpid : new OrderBook();
                mpidBook.AddOrder(addMpid.OrderReferenceNumber, addMpid.IsBuy ? Side.Buy : Side.Sell, addMpid.PriceRaw, addMpid.Shares);
                books[addMpid.StockLocate] = mpidBook;
                RecordLatency(mpidBook, receiveTimestamp);
                break;
            }

            case OrderReplaceMessage.MessageType when OrderReplaceMessage.TryParse(message, out OrderReplaceMessage replace):
            {
                replaceCount++;
                IncrementMessageCount(replace.StockLocate);
                if (books.TryGetValue(replace.StockLocate, out OrderBook replaceBook))
                {
                    replaceBook.Replace(replace.OriginalOrderReferenceNumber, replace.NewOrderReferenceNumber, replace.PriceRaw, replace.Shares);
                    books[replace.StockLocate] = replaceBook;
                    RecordLatency(replaceBook, receiveTimestamp);
                }

                break;
            }

            case OrderExecutedWithPriceMessage.MessageType when OrderExecutedWithPriceMessage.TryParse(message, out OrderExecutedWithPriceMessage execPrice):
            {
                executeWithPriceCount++;
                IncrementMessageCount(execPrice.StockLocate);
                if (books.TryGetValue(execPrice.StockLocate, out OrderBook execPriceBook))
                {
                    execPriceBook.Execute(execPrice.OrderReferenceNumber, execPrice.ExecutedShares);
                    books[execPrice.StockLocate] = execPriceBook;
                    RecordLatency(execPriceBook, receiveTimestamp);
                }

                break;
            }

            case CrossTradeMessage.MessageType when CrossTradeMessage.TryParse(message, out CrossTradeMessage cross):
            {
                crossTradeCount++;
                IncrementMessageCount(cross.StockLocate);
                break;
            }

            case StockDirectoryMessage.MessageType when StockDirectoryMessage.TryParse(message, out StockDirectoryMessage directory):
            {
                IncrementMessageCount(directory.StockLocate);
                tickerByLocate[directory.StockLocate] = System.Text.Encoding.ASCII.GetString(directory.Stock).TrimEnd();
                break;
            }

            default:
                otherCount++;
                if (message.Length >= 3)
                {
                    IncrementMessageCount(BinaryPrimitives.ReadUInt16BigEndian(message.Slice(1, 2)));
                }

                break;
        }
    }
}

// Must be called while holding bookLock.
void IncrementMessageCount(ushort stockLocate)
{
    messageCountByLocate[stockLocate] = messageCountByLocate.GetValueOrDefault(stockLocate) + 1;
}

// Must be called while holding bookLock. Updating values for existing keys while enumerating a
// Dictionary is safe in .NET (only Add/Remove invalidate the enumerator), so this needs no
// intermediate key snapshot/allocation.
void DecayMessageCounts()
{
    foreach (ushort locate in messageCountByLocate.Keys)
    {
        messageCountByLocate[locate] = messageCountByLocate[locate] * DecayNumerator / DecayDenominator;
    }
}

// Must be called while holding sequenceLock - holds an out-of-order datagram in a pooled buffer
// (no per-packet heap allocation) until it's either drained in order or its wait times out.
void BufferPending(MoldUdp64Header header, ReadOnlySpan<byte> datagram, long receivedAtTicks)
{
    if (pending.ContainsKey(header.SequenceNumber))
    {
        duplicateCount++;
        return;
    }

    if (freeBufferIndices.Count == 0)
    {
        ExpireOldestPending();
    }

    int bufferIndex = freeBufferIndices.Pop();
    datagram.CopyTo(pendingBufferPool[bufferIndex]);
    pending[header.SequenceNumber] = (bufferIndex, datagram.Length, receivedAtTicks);
}

// Must be called while holding sequenceLock - processes any buffered packets that are now
// contiguous with expectedNextSequence, advancing it as each one is consumed.
void DrainPending()
{
    while (pending.TryGetValue(expectedNextSequence!.Value, out (int BufferIndex, int Length, long ReceivedAtTicks) entry))
    {
        pending.Remove(expectedNextSequence.Value);
        ReadOnlySpan<byte> bufferedDatagram = pendingBufferPool[entry.BufferIndex].AsSpan(0, entry.Length);
        MoldUdp64Header bufferedHeader = MoldUdp64Header.Parse(bufferedDatagram);
        EnqueueForWorker(bufferedDatagram, entry.ReceivedAtTicks);
        expectedNextSequence = bufferedHeader.SequenceNumber + bufferedHeader.MessageCount;
        freeBufferIndices.Push(entry.BufferIndex);
    }
}

// Must be called while holding sequenceLock - gives up on the oldest buffered entry immediately,
// counting it as a real gap. Used only when the reorder window is full.
void ExpireOldestPending()
{
    KeyValuePair<ulong, (int BufferIndex, int Length, long ReceivedAtTicks)> oldest = pending.First();

    if (oldest.Key > expectedNextSequence!.Value)
    {
        sequenceGapCount++;
        missingMessageCount += (long)(oldest.Key - expectedNextSequence.Value);
    }

    expectedNextSequence = oldest.Key;
    DrainPending();
}

// Must be called while holding sequenceLock - checks whether the earliest buffered packet has
// been waiting long enough that the missing sequence range in front of it should be declared a
// real gap rather than transient reordering. `forceAll: true` flushes everything regardless of
// age (used at shutdown so the final summary doesn't leave stragglers uncounted).
void FlushExpiredPending(long nowTicks, bool forceAll = false)
{
    while (pending.Count > 0)
    {
        KeyValuePair<ulong, (int BufferIndex, int Length, long ReceivedAtTicks)> oldest = pending.First();
        bool expired = forceAll || (nowTicks - oldest.Value.ReceivedAtTicks) > reorderTimeoutTicks;
        if (!expired)
        {
            break;
        }

        if (oldest.Key > expectedNextSequence!.Value)
        {
            sequenceGapCount++;
            missingMessageCount += (long)(oldest.Key - expectedNextSequence.Value);
        }

        expectedNextSequence = oldest.Key;
        DrainPending();
    }
}

// Must be called while holding bookLock - times from socket receipt through GetBbo() completing,
// proving the full pipeline (socket -> ring buffer -> parse -> book update -> BBO recompute)
// stays fast under load.
void RecordLatency(OrderBook book, long receiveTimestamp)
{
    book.GetBbo();
    long elapsedTicks = Stopwatch.GetTimestamp() - receiveTimestamp;
    latencySamplesUs.Add(elapsedTicks * 1_000_000L / Stopwatch.Frequency);
}

// Publishes a full L2 snapshot (not a delta) for the most active resolved-ticker symbols to the
// local distribution channel that Boulevard.Edge.SolaceGateway bridges into Solace. Runs off a
// 250ms timer, entirely outside the mutation path above.
void PublishL2Snapshots()
{
    List<L2SnapshotDto> snapshots;

    lock (bookLock)
    {
        DecayMessageCounts();

        // Ranked by overall message activity, but only among symbols that actually have book
        // state - messageCountByLocate also includes message types (Stock Directory, Order
        // Delete, etc.) that never populate `books`, so ranking without this filter could pick
        // 200 symbols with plenty of *messages* but nothing to actually publish.
        var mostActive = messageCountByLocate
            .Where(kv => tickerByLocate.ContainsKey(kv.Key) && books.ContainsKey(kv.Key))
            .OrderByDescending(kv => kv.Value)
            .Take(L2TrackedSymbolCount);

        DateTime now = DateTime.UtcNow;
        snapshots = new List<L2SnapshotDto>(L2TrackedSymbolCount);

        foreach (KeyValuePair<ushort, long> entry in mostActive)
        {
            if (!books.TryGetValue(entry.Key, out OrderBook book))
            {
                continue;
            }

            // ToDtoLevels copies the price level values out of the book's own arrays - the
            // resulting DTOs hold no references into live book state, so it's safe to serialize
            // and send them after releasing bookLock below.
            snapshots.Add(new L2SnapshotDto(
                tickerByLocate[entry.Key],
                now,
                ToDtoLevels(book.GetBidDepth()),
                ToDtoLevels(book.GetAskDepth())));
        }
    }

    // JSON serialization and socket I/O for up to 200 tickers happen outside bookLock - doing
    // this under the lock previously blocked the worker thread from draining incoming datagrams
    // for the whole 250ms tick, which under sustained load overflowed the datagram queue (dropped
    // datagrams silently, invisible to sequence-gap detection since it never got the chance to see
    // them).
    foreach (L2SnapshotDto snapshot in snapshots)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(snapshot);

        try
        {
            l2PublishSocket.SendTo(payload, l2PublishEndpoint);
        }
        catch (SocketException)
        {
            // Best-effort distribution feed - nothing listening yet just means this tick's
            // snapshot is skipped; the next one (250ms later) supersedes it.
        }
    }
}

static PriceLevelDto[] ToDtoLevels(ReadOnlySpan<PriceLevel> levels)
{
    int count = Math.Min(L2DepthLevels, levels.Length);
    var result = new PriceLevelDto[count];
    for (int i = 0; i < count; i++)
    {
        result[i] = new PriceLevelDto(levels[i].PriceInTicks / 10000.0m, levels[i].AggregateShares);
    }

    return result;
}

(long P50, long P99, long Max) ComputeLatencyPercentiles()
{
    long[] samples;
    lock (bookLock)
    {
        if (latencySamplesUs.Count == 0)
        {
            return (0, 0, 0);
        }

        samples = latencySamplesUs.ToArray();
    }

    Array.Sort(samples);
    long p50 = samples[(int)(samples.Length * 0.50)];
    long p99 = samples[(int)Math.Min(samples.Length - 1, samples.Length * 0.99)];
    return (p50, p99, samples[^1]);
}

void PrintSnapshot(bool live)
{
    if (live)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        Console.WriteLine($"[EDGE {timestamp}] snapshot");
    }

    Console.WriteLine($" -> Datagrams:        {packetCount:N0}");
    Console.WriteLine($" -> Add Order:        {addCount:N0}");
    Console.WriteLine($" -> Order Executed:   {executeCount:N0}");
    Console.WriteLine($" -> Order Cancel:     {cancelCount:N0}");
    Console.WriteLine($" -> Order Delete:     {deleteCount:N0}");
    Console.WriteLine($" -> Order Replace:    {replaceCount:N0}");
    Console.WriteLine($" -> Exec With Price:  {executeWithPriceCount:N0}");
    Console.WriteLine($" -> Cross Trade:      {crossTradeCount:N0}");
    Console.WriteLine($" -> Other ITCH types: {otherCount:N0}");
    Console.WriteLine($" -> Receive errors:   {errorCount:N0}");
    Console.WriteLine($" -> Ring overflow:    {Interlocked.Read(ref ringOverflowCount):N0}");

    long pendingCount;
    lock (sequenceLock)
    {
        Console.WriteLine($" -> Sequence gaps:    {sequenceGapCount:N0} ({missingMessageCount:N0} messages missing)");
        Console.WriteLine($" -> Duplicates:       {duplicateCount:N0}");
        pendingCount = pending.Count;
    }

    (long p50, long p99, long max) = ComputeLatencyPercentiles();
    Console.WriteLine($" -> Socket->BBO latency (us): p50={p50:N0} p99={p99:N0} max={max:N0} (n={latencySamplesUs.Count:N0})");
    Console.WriteLine($" -> Reorder buffered: {pendingCount:N0} (awaiting resequencing)");

    lock (bookLock)
    {
        Console.WriteLine($" -> Distinct symbols: {books.Count:N0}");
        Console.WriteLine($" -> Resolved tickers: {tickerByLocate.Count:N0} (L2 published for top {Math.Min(L2TrackedSymbolCount, tickerByLocate.Count):N0})");
        Console.WriteLine(" -> Busiest BBOs:");

        var busiestFirst = books
            .Select(kv => (StockLocate: kv.Key, Bbo: kv.Value.GetBbo()))
            .OrderByDescending(x => x.Bbo.BidShares + x.Bbo.AskShares)
            .Take(5);

        foreach ((ushort stockLocate, Bbo bbo) in busiestFirst)
        {
            string bid = bbo.BidPriceInTicks.HasValue ? $"${bbo.BidPriceInTicks.Value / 10000.0m:F4} x {bbo.BidShares:N0}" : "-";
            string ask = bbo.AskPriceInTicks.HasValue ? $"${bbo.AskPriceInTicks.Value / 10000.0m:F4} x {bbo.AskShares:N0}" : "-";
            Console.WriteLine($"    Locate {stockLocate,6}: BID {bid,-20} ASK {ask}");
        }
    }

    Console.WriteLine();
}

record PriceLevelDto(decimal Price, long Shares);

record L2SnapshotDto(string Ticker, DateTime TimestampUtc, PriceLevelDto[] Bids, PriceLevelDto[] Asks);
