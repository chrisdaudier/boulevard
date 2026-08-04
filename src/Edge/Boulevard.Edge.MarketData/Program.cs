using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Boulevard.MarketData.Engine;
using Boulevard.Protocol.Itch;

const string MulticastIp = "239.255.0.1";
const int MulticastPort = 1234;
const int ReceiveBufferSize = 2048;
// Kept at 1 deliberately: overlapping >1 pending receives lets completion callbacks get
// scheduled out of wire order (thread-pool jitter), which produces false MoldUDP64 sequence
// gaps that aren't real network loss. A single outstanding receive guarantees processing
// order matches delivery order, which matters for the sequence-continuity check.
const int PooledBufferCount = 1;

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
long otherCount = 0;
long errorCount = 0;

ulong? expectedNextSequence = null;
long sequenceGapCount = 0;
long missingMessageCount = 0;
long duplicateCount = 0;

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

// Sized to comfortably exceed the ~464K Add/Execute/Cancel messages this capture produces,
// so recording a latency sample never triggers a reallocation on the hot path.
var latencySamplesUs = new List<long>(1_000_000);

var books = new Dictionary<ushort, OrderBook>();
var bookLock = new object();

// Defaults to "let the OS pick the route" (correct in a container, which has exactly one real
// interface). Set MULTICAST_LOCAL_ADDRESS=127.0.0.1 only when running publisher+subscriber as
// two processes on the same host with multiple virtual/bridged adapters (e.g. this Mac's
// en1-en4), where an unpinned interface can cause duplicate delivery of the same datagram.
IPAddress multicastLocalAddress = IPAddress.TryParse(Environment.GetEnvironmentVariable("MULTICAST_LOCAL_ADDRESS"), out IPAddress? parsedAddress)
    ? parsedAddress
    : IPAddress.Any;

using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
socket.Bind(new IPEndPoint(IPAddress.Any, MulticastPort));
socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(IPAddress.Parse(MulticastIp), multicastLocalAddress));

Console.WriteLine($"[EDGE] Bound to 0.0.0.0:{MulticastPort}, joined multicast group {MulticastIp}");
Console.WriteLine("[EDGE] Waiting for datagrams. Press CTRL+C to exit.\n");

var receiveArgsList = new List<SocketAsyncEventArgs>(PooledBufferCount);
for (int i = 0; i < PooledBufferCount; i++)
{
    var receiveArgs = new SocketAsyncEventArgs();
    receiveArgs.SetBuffer(new byte[ReceiveBufferSize], 0, ReceiveBufferSize);
    receiveArgs.Completed += OnReceiveCompleted;
    receiveArgsList.Add(receiveArgs);
}

foreach (SocketAsyncEventArgs receiveArgs in receiveArgsList)
{
    StartReceive(receiveArgs);
}

var reportingTask = Task.Run(async () =>
{
    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
    while (await timer.WaitForNextTickAsync(cts.Token))
    {
        lock (bookLock)
        {
            // Ensures a genuinely lost packet still gets declared a gap even during a lull
            // with no new arrivals to trigger the opportunistic check in HandleIncomingPacket.
            FlushExpiredPending(Stopwatch.GetTimestamp());
        }

        PrintSnapshot(live: true);
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

foreach (SocketAsyncEventArgs receiveArgs in receiveArgsList)
{
    receiveArgs.Dispose();
}

lock (bookLock)
{
    // No more datagrams are coming - whatever's still buffered is a genuine gap now.
    FlushExpiredPending(Stopwatch.GetTimestamp(), forceAll: true);
}

Console.WriteLine();
Console.WriteLine("[EDGE] Final summary");
PrintSnapshot(live: false);

void StartReceive(SocketAsyncEventArgs receiveArgs)
{
    while (!cts.IsCancellationRequested)
    {
        bool pending;
        try
        {
            pending = socket.ReceiveAsync(receiveArgs);
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        if (pending)
        {
            return;
        }

        ProcessReceive(receiveArgs);
    }
}

void OnReceiveCompleted(object? sender, SocketAsyncEventArgs receiveArgs)
{
    if (cts.IsCancellationRequested)
    {
        return;
    }

    ProcessReceive(receiveArgs);
    StartReceive(receiveArgs);
}

void ProcessReceive(SocketAsyncEventArgs receiveArgs)
{
    if (receiveArgs.SocketError != SocketError.Success || receiveArgs.BytesTransferred == 0)
    {
        Interlocked.Increment(ref errorCount);
        return;
    }

    long receiveTimestamp = Stopwatch.GetTimestamp();
    ReadOnlySpan<byte> datagram = receiveArgs.MemoryBuffer.Span[..receiveArgs.BytesTransferred];
    Interlocked.Increment(ref packetCount);

    if (datagram.Length < MoldUdp64Header.Size)
    {
        return;
    }

    MoldUdp64Header header = MoldUdp64Header.Parse(datagram);

    lock (bookLock)
    {
        HandleIncomingPacket(header, datagram, receiveTimestamp);
    }
}

// Must be called while holding bookLock. Fast path: an in-order packet dispatches immediately
// and then drains any now-contiguous buffered packets. A packet ahead of the expected sequence
// is held in the reorder buffer rather than immediately flagged as a gap - it might just be
// jitter-delayed, not lost.
void HandleIncomingPacket(MoldUdp64Header header, ReadOnlySpan<byte> datagram, long receiveTimestamp)
{
    if (header.MessageCount == 0)
    {
        return; // heartbeat - no messages, no sequence advance
    }

    expectedNextSequence ??= header.SequenceNumber;

    if (header.SequenceNumber < expectedNextSequence.Value)
    {
        duplicateCount++;
        return;
    }

    if (header.SequenceNumber == expectedNextSequence.Value)
    {
        DispatchMessages(datagram, receiveTimestamp);
        expectedNextSequence = header.SequenceNumber + header.MessageCount;
        DrainPending();
        return;
    }

    BufferPending(header, datagram, receiveTimestamp);
    FlushExpiredPending(receiveTimestamp);
}

// Must be called while holding bookLock.
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
                OrderBook book = books.TryGetValue(add.StockLocate, out OrderBook existing) ? existing : new OrderBook();
                book.AddOrder(add.OrderReferenceNumber, add.IsBuy ? Side.Buy : Side.Sell, add.PriceRaw, add.Shares);
                books[add.StockLocate] = book;
                RecordLatency(book, receiveTimestamp);
                break;
            }

            case OrderExecutedMessage.MessageType when OrderExecutedMessage.TryParse(message, out OrderExecutedMessage exec):
            {
                executeCount++;
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
                if (books.TryGetValue(cancel.StockLocate, out OrderBook cancelBook))
                {
                    cancelBook.Cancel(cancel.OrderReferenceNumber, cancel.CanceledShares);
                    books[cancel.StockLocate] = cancelBook;
                    RecordLatency(cancelBook, receiveTimestamp);
                }

                break;
            }

            default:
                otherCount++;
                break;
        }
    }
}

// Must be called while holding bookLock - holds an out-of-order datagram in a pooled buffer
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

// Must be called while holding bookLock - processes any buffered packets that are now
// contiguous with expectedNextSequence, advancing it as each one is consumed.
void DrainPending()
{
    while (pending.TryGetValue(expectedNextSequence!.Value, out (int BufferIndex, int Length, long ReceivedAtTicks) entry))
    {
        pending.Remove(expectedNextSequence.Value);
        ReadOnlySpan<byte> bufferedDatagram = pendingBufferPool[entry.BufferIndex].AsSpan(0, entry.Length);
        MoldUdp64Header bufferedHeader = MoldUdp64Header.Parse(bufferedDatagram);
        DispatchMessages(bufferedDatagram, entry.ReceivedAtTicks);
        expectedNextSequence = bufferedHeader.SequenceNumber + bufferedHeader.MessageCount;
        freeBufferIndices.Push(entry.BufferIndex);
    }
}

// Must be called while holding bookLock - gives up on the oldest buffered entry immediately,
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

// Must be called while holding bookLock - checks whether the earliest buffered packet has been
// waiting long enough that the missing sequence range in front of it should be declared a real
// gap rather than transient reordering. `forceAll: true` flushes everything regardless of age
// (used at shutdown so the final summary doesn't leave stragglers uncounted).
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
// proving the full hot path (parse -> book update -> BBO recompute) stays fast under load.
void RecordLatency(OrderBook book, long receiveTimestamp)
{
    book.GetBbo();
    long elapsedTicks = Stopwatch.GetTimestamp() - receiveTimestamp;
    latencySamplesUs.Add(elapsedTicks * 1_000_000L / Stopwatch.Frequency);
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
    Console.WriteLine($" -> Other ITCH types: {otherCount:N0}");
    Console.WriteLine($" -> Receive errors:   {errorCount:N0}");
    Console.WriteLine($" -> Sequence gaps:    {sequenceGapCount:N0} ({missingMessageCount:N0} messages missing)");
    Console.WriteLine($" -> Duplicates:       {duplicateCount:N0}");

    (long p50, long p99, long max) = ComputeLatencyPercentiles();
    Console.WriteLine($" -> Socket->BBO latency (us): p50={p50:N0} p99={p99:N0} max={max:N0} (n={latencySamplesUs.Count:N0})");

    lock (bookLock)
    {
        Console.WriteLine($" -> Reorder buffered: {pending.Count:N0} (awaiting resequencing)");
        Console.WriteLine($" -> Distinct symbols: {books.Count:N0}");
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
