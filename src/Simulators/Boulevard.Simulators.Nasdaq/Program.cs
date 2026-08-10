using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Boulevard.Simulators.Nasdaq;
using ZstdSharp;

const string DefaultCapturePath =
    "/Users/chrisdaudier/Downloads/market_data/ny4-xnas-tvitch-a-20230822/ny4-xnas-tvitch-a-20230822T000000.pcap.zst";

const string MulticastIp = "239.255.0.1";
const int MulticastPort = 1234;

(List<string> capturePaths, double speed, bool loop) = ParseArgs(args);

Console.WriteLine(capturePaths.Count == 1
    ? $"[NASDAQ] Reading capture: {capturePaths[0]}"
    : $"[NASDAQ] Reading {capturePaths.Count} chained captures: {Path.GetFileName(capturePaths[0])} .. {Path.GetFileName(capturePaths[^1])}");
Console.WriteLine($"[NASDAQ] Publishing to {MulticastIp}:{MulticastPort}");
Console.WriteLine(speed <= 0
    ? "[NASDAQ] Replay mode: AFAP (as fast as possible, no pacing)"
    : $"[NASDAQ] Replay mode: paced at {speed}x original capture timing");
if (loop)
{
    Console.WriteLine("[NASDAQ] Looping: will restart from the beginning after each full pass. Press CTRL+C to stop.");
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    Console.WriteLine("\n[NASDAQ] Shutdown signal received (SIGINT).");
    eventArgs.Cancel = true;
    cts.Cancel();
};

using PosixSignalRegistration sigTermRegistration = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
{
    Console.WriteLine("\n[NASDAQ] Shutdown signal received (SIGTERM).");
    context.Cancel = true;
    cts.Cancel();
});

long packetCount = 0;
long bytesSent = 0;
int lapCount = 0;

// Defaults to "let the OS pick the route" (correct in a container, which has exactly one real
// interface). Set MULTICAST_LOCAL_ADDRESS=127.0.0.1 only when running publisher+subscriber as
// two processes on the same host with multiple virtual/bridged adapters (e.g. this Mac's
// en1-en4), where an unpinned interface can cause the receiver to see duplicate deliveries.
IPAddress multicastLocalAddress = IPAddress.TryParse(Environment.GetEnvironmentVariable("MULTICAST_LOCAL_ADDRESS"), out IPAddress? parsedAddress)
    ? parsedAddress
    : IPAddress.Any;

using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);
socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, multicastLocalAddress.GetAddressBytes());
socket.SendBufferSize = 1 << 20;
var multicastEndpoint = new IPEndPoint(IPAddress.Parse(MulticastIp), MulticastPort);

var stopwatch = Stopwatch.StartNew();

do
{
    lapCount++;

    // Shared across every chained file within this lap (reset only when a new lap starts) - the
    // capture timestamps are real Unix-epoch nanoseconds and these files are contiguous slices of
    // one continuous session, so pacing flows across a file boundary exactly like it does across
    // any other pair of consecutive packets, with no artificial gap or reset.
    long? previousCaptureTimestampNs = null;
    long previousSendTicks = 0;

    foreach (string capturePath in capturePaths)
    {
        if (cts.IsCancellationRequested)
        {
            break;
        }

        using FileStream fileStream = File.OpenRead(capturePath);
        using var decompressionStream = new DecompressionStream(fileStream);
        var pcapReader = new PcapReader(decompressionStream);

        while (!cts.IsCancellationRequested && pcapReader.TryReadNextPacket(out ReadOnlySpan<byte> frame, out long captureTimestampNs))
        {
            packetCount++;

            if (speed > 0 && previousCaptureTimestampNs.HasValue)
            {
                long captureDeltaNs = captureTimestampNs - previousCaptureTimestampNs.Value;
                if (captureDeltaNs > 0)
                {
                    long targetTicks = previousSendTicks + (long)(captureDeltaNs / speed * Stopwatch.Frequency / 1_000_000_000.0);
                    SpinWaitUntil(targetTicks, cts.Token);
                }
            }

            previousCaptureTimestampNs = captureTimestampNs;
            previousSendTicks = Stopwatch.GetTimestamp();

            if (!EthernetIpUdp.TryExtractUdpPayload(frame, out ReadOnlySpan<byte> udpPayload))
            {
                continue;
            }

            SendWithBackpressureRetry(udpPayload);
            bytesSent += udpPayload.Length;
        }

        if (capturePaths.Count > 1)
        {
            Console.WriteLine($"[NASDAQ] Finished {Path.GetFileName(capturePath)} ({packetCount:N0} packets, {bytesSent:N0} bytes so far).");
        }
    }

    if (loop && !cts.IsCancellationRequested)
    {
        Console.WriteLine($"[NASDAQ] Lap {lapCount:N0} complete ({packetCount:N0} packets, {bytesSent:N0} bytes so far) - restarting from the beginning.");
    }
}
while (loop && !cts.IsCancellationRequested);

stopwatch.Stop();

Console.WriteLine();
Console.WriteLine("[NASDAQ] Publish summary");
Console.WriteLine($" -> Laps:         {lapCount:N0}");
Console.WriteLine($" -> Packets read: {packetCount:N0}");
Console.WriteLine($" -> Bytes sent:   {bytesSent:N0}");
Console.WriteLine($" -> Elapsed:      {stopwatch.ElapsedMilliseconds:N0} ms");

void SendWithBackpressureRetry(ReadOnlySpan<byte> payload)
{
    while (true)
    {
        try
        {
            socket.SendTo(payload, SocketFlags.None, multicastEndpoint);
            return;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.NoBufferSpaceAvailable)
        {
            // Kernel send buffer is momentarily full (e.g. a microburst); yield and retry
            // rather than dropping the datagram.
            Thread.SpinWait(1000);
        }
    }
}

static void SpinWaitUntil(long targetStopwatchTicks, CancellationToken cancellationToken)
{
    while (!cancellationToken.IsCancellationRequested)
    {
        long remainingTicks = targetStopwatchTicks - Stopwatch.GetTimestamp();
        if (remainingTicks <= 0)
        {
            return;
        }

        double remainingMs = remainingTicks * 1000.0 / Stopwatch.Frequency;
        if (remainingMs > 2)
        {
            Thread.Sleep(1);
        }
        else
        {
            Thread.SpinWait(50);
        }
    }
}

static (List<string> CapturePaths, double Speed, bool Loop) ParseArgs(string[] args)
{
    var explicitPaths = new List<string>();
    double speed = 1.0;
    bool loop = false;
    double? minutes = null;

    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--speed" && i + 1 < args.Length)
        {
            speed = double.Parse(args[++i]);
        }
        else if (args[i] == "--loop")
        {
            loop = true;
        }
        else if (args[i] == "--minutes" && i + 1 < args.Length)
        {
            minutes = double.Parse(args[++i]);
        }
        else
        {
            explicitPaths.Add(args[i]);
        }
    }

    if (explicitPaths.Count == 0)
    {
        explicitPaths.Add(DefaultCapturePath);
    }

    List<string> capturePaths = minutes.HasValue && explicitPaths.Count == 1
        ? ExpandSequentialFiles(explicitPaths[0], minutes.Value)
        : explicitPaths;

    return (capturePaths, speed, loop);
}

/// <summary>
/// Given one capture file named "&lt;prefix&gt;T{HHMMSS}.pcap.zst", finds and appends the
/// following 10-minute-chunked files in the same directory (same naming convention) until at
/// least <paramref name="minutes"/> of capture is queued up, or no next file is found.
/// </summary>
static List<string> ExpandSequentialFiles(string firstFilePath, double minutes)
{
    string directory = Path.GetDirectoryName(Path.GetFullPath(firstFilePath)) ?? ".";
    string fileName = Path.GetFileName(firstFilePath);

    int timeMarkerIndex = fileName.LastIndexOf('T');
    if (timeMarkerIndex < 0 || fileName.Length < timeMarkerIndex + 7)
    {
        throw new FormatException($"Expected a filename like '<prefix>T{{HHMMSS}}.pcap.zst', got '{fileName}'.");
    }

    string prefix = fileName[..timeMarkerIndex];
    string suffix = fileName[(timeMarkerIndex + 7)..];
    TimeSpan current = TimeSpan.ParseExact(fileName.Substring(timeMarkerIndex + 1, 6), "hhmmss", null);

    var files = new List<string> { firstFilePath };
    for (TimeSpan queued = TimeSpan.FromMinutes(10); queued < TimeSpan.FromMinutes(minutes); queued += TimeSpan.FromMinutes(10))
    {
        current += TimeSpan.FromMinutes(10);
        string candidate = Path.Combine(directory, $"{prefix}T{current:hhmmss}{suffix}");
        if (!File.Exists(candidate))
        {
            Console.WriteLine($"[NASDAQ] Stopping auto-chain: no file found at {candidate} (requested {minutes} min, queued {queued.TotalMinutes} min across {files.Count} file(s)).");
            break;
        }

        files.Add(candidate);
    }

    return files;
}
