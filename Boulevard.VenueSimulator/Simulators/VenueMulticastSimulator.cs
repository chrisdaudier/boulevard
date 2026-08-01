using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public sealed class VenueMulticastSimulator
{
    private readonly Socket _multicastSocket;
    private readonly IPEndPoint _multicastEndpoint;
    private readonly ChaosEngine _chaosEngine;
    private readonly CancellationTokenSource _cts = new();

    // Hot-path state arrays map directly by internal index positions to maximize CPU cache Locality
    private readonly uint[] _hotAssetIds;
    private readonly int[] _hotPricesInCents;

    private readonly byte[] _sendBuffer = new byte[64]; 
    private readonly Random _marketRandom = new(1337);

    // Volatile thread telemetry metrics
    private long _totalSequenceNumber = 0;
    private long _totalBytesSent = 0;
    private int _currentPriceInCents = 15000; 
    private int _lastSizeTransacted = 0;
    private uint _currentAssetId = 0;

    private readonly ConcurrentDictionary<uint, string> _assetIdToTicker = new();

    public VenueMulticastSimulator(string multicastIp, int port, ChaosEngine chaosEngine, AssetBlueprint[] assets)
    {
        _multicastSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _multicastEndpoint = new IPEndPoint(IPAddress.Parse(multicastIp), port);
        _multicastSocket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);

        _chaosEngine = chaosEngine;
        
        // Flatten blueprints into highly performance-optimized primitive arrays for processing
        _hotAssetIds = new uint[assets.Length];
        _hotPricesInCents = new int[assets.Length];

        for (int i = 0; i < assets.Length; i++)
        {
            _hotAssetIds[i] = assets[i].AssetId;
            _hotPricesInCents[i] = assets[i].StartPriceInCents;
            _assetIdToTicker[assets[i].AssetId] = assets[i].Ticker;
        }
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
    public void Stop() => _cts.Cancel();

    private void StartSimulationLoop()
    {
        long ticksPerMessage = Stopwatch.Frequency / 10_000; 
        long nextMessageTimestamp = Stopwatch.GetTimestamp();
        int messageSize = Marshal.SizeOf<ItchAddOrderMessage>();
        int assetUniverseCount = _hotAssetIds.Length;

        while (!_cts.Token.IsCancellationRequested)
        {
            long currentTimestamp = Stopwatch.GetTimestamp();
            if (currentTimestamp < nextMessageTimestamp)
            {
                Thread.SpinWait(10); 
                continue;
            }

            // Zero Allocation selection logic across our primitive arrays
            int targetIndex = _marketRandom.Next(0, assetUniverseCount);
            _currentAssetId = _hotAssetIds[targetIndex];

            // Market walk generation
            int priceDelta = _marketRandom.Next(-5, 6);
            _currentPriceInCents = Interlocked.Add(ref _hotPricesInCents[targetIndex], priceDelta);

            Interlocked.Add(ref _currentPriceInCents, priceDelta);
            uint size = (uint)(_marketRandom.Next(1, 20) * 100);
            Interlocked.Exchange(ref _lastSizeTransacted, (int)size);

            long currentSeq = Interlocked.Increment(ref _totalSequenceNumber);

            // Populate the unmanaged data payload
            ItchAddOrderMessage orderMessage = new()
            {
                MessageType = (byte)'A',
                TimestampRaw = (ulong)Stopwatch.GetTimestamp(),
                SequenceNumber = (ulong)currentSeq,
                AssetId = _currentAssetId, 
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
            try
            {
                _multicastSocket.SendTo(messageSpan, SocketFlags.None, _multicastEndpoint);
                Interlocked.Add(ref _totalBytesSent, messageSize);
            }
            catch (SocketException)
            {
                // Transient network hiccups (interface flap, sleep/wake, VPN toggle) shouldn't
                // kill the whole simulator thread - just drop this tick and keep going.
            }

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
            uint snapshotAssetId = Interlocked.CompareExchange(ref _currentAssetId, 0, 0);
            
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
            Console.WriteLine($"[{timestamp}] PRICE: ${snapshotPrice:F2} | VOL: {snapshotSize:N0} shares | ASSET ID: {snapshotAssetId:N0} | TICKER: {_assetIdToTicker.GetValueOrDefault(snapshotAssetId)}");
            
            if (snapshotDrops > 0 || snapshotCorruption > 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[{timestamp}] [DEGRADATION ENG] Drops: {snapshotDrops:N0} | Corruptions: {snapshotCorruption:N0}");
                Console.ResetColor();
            }
        }
    }
}


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

public enum ItchMessageType : byte
{
    Timestamp           = (byte)'T',
    SystemEvent         = (byte)'S',
    StockDirectory      = (byte)'R',
    TradingAction       = (byte)'H',
    AddOrder            = (byte)'A',
    AddOrderAttributed  = (byte)'F',
    OrderExecuted       = (byte)'E',
    OrderExecutedPrice  = (byte)'C',
    OrderCancel         = (byte)'X',
    OrderDelete         = (byte)'D',
    OrderReplace        = (byte)'U',
    TradeNonDisplay     = (byte)'P',
    CrossTrade          = (byte)'Q',
    ImbalanceIndicator  = (byte)'I'
}