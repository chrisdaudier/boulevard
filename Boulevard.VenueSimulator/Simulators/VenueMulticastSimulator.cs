using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public sealed class ChaosEngine
{
    private readonly Random _chaosRandom;
    private long _droppedPacketCount;
    private long _corruptedPacketCount;

    public long DroppedPacketCount => Interlocked.Read(ref _droppedPacketCount);
    public long CorruptedPacketCount => Interlocked.Read(ref _corruptedPacketCount);

    public ChaosEngine(int seed = 42)
    {
        _chaosRandom = new Random(seed);
    }

    /// <summary>
    /// Evaluates and applies simulation anomalies. 
    /// Returns true if the packet should be dropped entirely.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)] // Tells JIT to inline this block to eliminate method-call overhead
    public bool ProcessChaos(Span<byte> messageSpan, ulong currentSeq)
    {
        int chaosRoll = _chaosRandom.Next(0, 1000); // 0.1% granularity

        // 1. 1% Packet Drop Simulation
        if (chaosRoll < 10)
        {
            Interlocked.Increment(ref _droppedPacketCount);
            return true; 
        }

        // 2. 0.5% Payload Sequence Corruption Simulation
        if (chaosRoll >= 10 && chaosRoll < 15)
        {
            Interlocked.Increment(ref _corruptedPacketCount);
            // Intentionally overwrite the sequence layout bytes inside the wire payload
            System.Runtime.InteropServices.MemoryMarshal.Write(messageSpan.Slice(9, 8), in currentSeq);
        }

        return false;
    }
}

public sealed class VenueMulticastSimulator
{
    private readonly Socket _multicastSocket;
    private readonly IPEndPoint _multicastEndpoint;
    private readonly ChaosEngine _chaosEngine;
    private readonly CancellationTokenSource _cts = new();
    
    private readonly byte[] _sendBuffer = new byte[64]; 
    private readonly Random _marketRandom = new(1337);

    // Volatile thread telemetry metrics
    private long _totalSequenceNumber = 0;
    private long _totalBytesSent = 0;
    private int _currentPriceInCents = 15000; 
    private int _lastSizeTransacted = 0;

    public VenueMulticastSimulator(string multicastIp, int port, ChaosEngine chaosEngine)
    {
        _multicastSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _multicastEndpoint = new IPEndPoint(IPAddress.Parse(multicastIp), port);
        _multicastSocket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);
        
        _chaosEngine = chaosEngine;
    }

    public void Start()
    {
        Task.Factory.StartNew(() => RunReportingConsoleLoop(_cts.Token), 
            _cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        Thread simulationThread = new Thread(StartSimulationLoop)
        {
            Name = "VenueSim-HotPath",
            Priority = ThreadPriority.Highest,
            IsBackground = true
        };
        simulationThread.Start();
    }

    private void StartSimulationLoop()
    {
        long ticksPerMessage = Stopwatch.Frequency / 10_000; 
        long nextMessageTimestamp = Stopwatch.GetTimestamp();
        int messageSize = Marshal.SizeOf<ItchAddOrderMessage>();

        while (!_cts.Token.IsCancellationRequested)
        {
            long currentTimestamp = Stopwatch.GetTimestamp();
            if (currentTimestamp < nextMessageTimestamp)
            {
                Thread.SpinWait(10); 
                continue;
            }

            // Market walk generation
            int priceDelta = _marketRandom.Next(-5, 6);
            Interlocked.Add(ref _currentPriceInCents, priceDelta);
            uint size = (uint)(_marketRandom.Next(1, 20) * 100);
            Interlocked.Exchange(ref _lastSizeTransacted, (int)size);

            long currentSeq = Interlocked.Increment(ref _totalSequenceNumber);

            // Populate the unmanaged data payload
            ItchAddOrderMessage orderMessage = new ItchAddOrderMessage
            {
                MessageType = (byte)'A',
                TimestampRaw = (ulong)Stopwatch.GetTimestamp(),
                SequenceNumber = (ulong)currentSeq,
                AssetId = 42, 
                Shares = size,
                PriceInCents = (uint)_currentPriceInCents
            };

            Span<byte> messageSpan = _sendBuffer.AsSpan(0, messageSize);
            
            // Note the use of "in orderMessage" - passes by reference as a read-only variable 
            MemoryMarshal.Write(messageSpan, in orderMessage);

            // =========================================================================
            // DELEGATED CHAOS ENGINE INJECTION (Zero-Allocation, Inlined by JIT Compiler)
            // =========================================================================
            bool shouldDrop = _chaosEngine.ProcessChaos(messageSpan, (ulong)currentSeq);
            if (shouldDrop)
            {
                nextMessageTimestamp += ticksPerMessage;
                continue; 
            }

            // High-Speed Network Transmission
            _multicastSocket.SendTo(messageSpan, SocketFlags.None, _multicastEndpoint);
            Interlocked.Add(ref _totalBytesSent, messageSize);

            nextMessageTimestamp += ticksPerMessage;
        }
    }

    private async Task RunReportingConsoleLoop(CancellationToken token)
    {
        Console.Clear();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        long lastSeq = 0;

        while (await timer.WaitForNextTickAsync(token))
        {
            long snapshotSeq = Interlocked.Read(ref _totalSequenceNumber);
            long snapshotBytes = Interlocked.Read(ref _totalBytesSent);
            double snapshotPrice = Interlocked.CompareExchange(ref _currentPriceInCents, 0, 0) / 100.0;
            int snapshotSize = Interlocked.CompareExchange(ref _lastSizeTransacted, 0, 0);
            
            // Read metrics seamlessly from our decoupled chaos container
            long snapshotDrops = _chaosEngine.DroppedPacketCount;
            long snapshotCorruption = _chaosEngine.CorruptedPacketCount;

            long actualRateInWindow = snapshotSeq - lastSeq;
            lastSeq = snapshotSeq;

            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"================================================================================");
            Console.ResetColor();
            Console.WriteLine($"[{timestamp}] SEQ: {snapshotSeq:N0} | SPEED: {actualRateInWindow:N0} msg/sec | DATA: {(snapshotBytes / 1024.0 / 1024.0):F2} MB");
            Console.WriteLine($"[{timestamp}] PRICE: ${snapshotPrice:F2} | VOL: {snapshotSize:N0} shares");
            
            if (snapshotDrops > 0 || snapshotCorruption > 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[{timestamp}] [DEGRADATION ENG] Drops: {snapshotDrops:N0} | Corruptions: {snapshotCorruption:N0}");
                Console.ResetColor();
            }
        }
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ItchAddOrderMessage
{
    public byte MessageType;       
    public ulong TimestampRaw;     
    public ulong SequenceNumber;   
    public uint AssetId;           
    public uint Shares;            
    public uint PriceInCents;      
}