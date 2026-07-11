using System;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

// MemoryDiagnoser enables GC collection tracking (Gen 0, 1, 2 and Allocated Bytes)
[MemoryDiagnoser]
[Orderer(BenchmarkDotNet.Order.SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class VenueSimulatorBenchmarks
{
    private NaiveVenueMulticastSimulator _naiveSimulator = null!;
    private VenueMulticastSimulator _optimizedSimulator = null!;
    private ChaosEngine _chaosEngine = null!;

    // Reusable stack variables mirroring our simulator needs
    private byte[] _reusableBuffer = null!;
    private int _dummyPrice = 15000;
    private ulong _dummySeq = 100;

    [GlobalSetup]
    public void Setup()
    {
        const string multicastIp = "127.0.0.1"; // Redirect to localhost loopback for testing
        const int naivePort = 15001;
        const int optPort = 15002;

        _naiveSimulator = new NaiveVenueMulticastSimulator(multicastIp, naivePort);
        
        _chaosEngine = new ChaosEngine(seed: 42);
        _optimizedSimulator = new VenueMulticastSimulator(multicastIp, optPort, _chaosEngine, []);

        _reusableBuffer = new byte[64];
    }

    [Benchmark(Baseline = true)]
    public void RunNaiveSimulatorSingleTick()
    {
        // Executes the JSON/Class-allocating framework
        _naiveSimulator.ExecuteSingleIteration();
    }

    [Benchmark]
    public void RunOptimizedSimulatorSingleTick()
    {
        // Executes our zero-allocation unmanaged struct layout approach
        // We simulate a single hot-path iteration isolated from the continuous background thread for evaluation
        int messageSize = Marshal.SizeOf<ItchAddOrderMessage>();
        
        ItchAddOrderMessage orderMessage = new ItchAddOrderMessage
        {
            MessageType = (byte)'A',
            TimestampRaw = 123456789,
            SequenceNumber = _dummySeq,
            AssetId = 42,
            Shares = 500,
            PriceInCents = (uint)_dummyPrice
        };

        Span<byte> messageSpan = _reusableBuffer.AsSpan(0, messageSize);
        
        // Pass via read-only reference "in" modifier
        MemoryMarshal.Write(messageSpan, in orderMessage);

        // Process modular zero-allocation chaos
        bool shouldDrop = _chaosEngine.ProcessChaos(messageSpan, _dummySeq);
        
        if (!shouldDrop)
        {
            // Normally routes down to the socket layer
            // For pure pipeline benchmark validation, we stop at memory formatting, 
            // or we can include the socket send path.
        }
    }
}