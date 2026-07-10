using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

public sealed class NaiveVenueMulticastSimulator
{
    private readonly Socket _multicastSocket;
    private readonly IPEndPoint _multicastEndpoint;
    private readonly Random _marketRandom = new(1337);
    private ulong _sequenceNumber = 0;
    private int _currentPriceInCents = 15000;

    public NaiveVenueMulticastSimulator(string multicastIp, int port)
    {
        _multicastSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _multicastEndpoint = new IPEndPoint(IPAddress.Parse(multicastIp), port);
        _multicastSocket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);
    }

    /// <summary>
    /// Simulates a single execution iteration using legacy heap-allocation techniques.
    /// </summary>
    public void ExecuteSingleIteration()
    {
        _sequenceNumber++;
        int priceDelta = _marketRandom.Next(-5, 6);
        _currentPriceInCents += priceDelta;
        uint size = (uint)(_marketRandom.Next(1, 20) * 100);

        // ALLOCATION 1: Heap allocation for a new class object on every single iteration
        var marketEvent = new NaiveMarketMessage
        {
            MessageType = "A",
            Timestamp = DateTime.UtcNow.Ticks.ToString(), // ALLOCATION 2: String allocation
            SequenceNumber = _sequenceNumber,
            AssetId = 42,
            Shares = size,
            Price = _currentPriceInCents / 100.0 // Boxed or double translation
        };

        // ALLOCATION 3: Massive heap allocation from string serialization encoding
        string jsonPayload = JsonSerializer.Serialize(marketEvent);
        byte[] wireBytes = Encoding.UTF8.GetBytes(jsonPayload); 

        // Network transmission
        _multicastSocket.SendTo(wireBytes, SocketFlags.None, _multicastEndpoint);
    }
}

// Represented as a heap-allocated class object
public class NaiveMarketMessage
{
    public string MessageType { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;
    public ulong SequenceNumber { get; set; }
    public int AssetId { get; set; }
    public uint Shares { get; set; }
    public double Price { get; set; }
}