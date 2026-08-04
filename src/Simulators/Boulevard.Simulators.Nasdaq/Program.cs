using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Boulevard.Simulators.Nasdaq;
using ZstdSharp;

const string DefaultCapturePath =
    "/Users/chrisdaudier/Downloads/market_data/ny4-xnas-tvitch-a-20230822/ny4-xnas-tvitch-a-20230822T000000.pcap.zst";

const string MulticastIp = "239.255.0.1";
const int MulticastPort = 1234;

(string capturePath, double speed) = ParseArgs(args);

Console.WriteLine($"[NASDAQ] Reading capture: {capturePath}");
Console.WriteLine($"[NASDAQ] Publishing to {MulticastIp}:{MulticastPort}");
Console.WriteLine(speed <= 0
    ? "[NASDAQ] Replay mode: AFAP (as fast as possible, no pacing)"
    : $"[NASDAQ] Replay mode: paced at {speed}x original capture timing");

var stopwatch = Stopwatch.StartNew();

long packetCount = 0;
long bytesSent = 0;

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

using (FileStream fileStream = File.OpenRead(capturePath))
using (var decompressionStream = new DecompressionStream(fileStream))
{
    var pcapReader = new PcapReader(decompressionStream);
    long? previousCaptureTimestampNs = null;
    long previousSendTicks = 0;

    while (pcapReader.TryReadNextPacket(out ReadOnlySpan<byte> frame, out long captureTimestampNs))
    {
        packetCount++;

        if (speed > 0 && previousCaptureTimestampNs.HasValue)
        {
            long captureDeltaNs = captureTimestampNs - previousCaptureTimestampNs.Value;
            if (captureDeltaNs > 0)
            {
                long targetTicks = previousSendTicks + (long)(captureDeltaNs / speed * Stopwatch.Frequency / 1_000_000_000.0);
                SpinWaitUntil(targetTicks);
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
}

stopwatch.Stop();

Console.WriteLine();
Console.WriteLine("[NASDAQ] Publish summary");
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

static void SpinWaitUntil(long targetStopwatchTicks)
{
    while (true)
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

static (string CapturePath, double Speed) ParseArgs(string[] args)
{
    string? capturePath = null;
    double speed = 1.0;

    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--speed" && i + 1 < args.Length)
        {
            speed = double.Parse(args[++i]);
        }
        else
        {
            capturePath = args[i];
        }
    }

    return (capturePath ?? DefaultCapturePath, speed);
}
