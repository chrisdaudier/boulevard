using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using MQTTnet;

// Thin bridge: no ITCH/MoldUDP64/OrderBook knowledge at all. Boulevard.Edge.MarketData already
// computes correct, already-throttled L2 snapshots (see its PublishL2Snapshots) - this just
// forwards each one, as-is, from the local UDP distribution channel onto Solace via MQTT, which
// bridges MQTT topics directly into its topic space for the browser frontend to subscribe to.
const int ListenPort = 5001;
const string MqttHost = "localhost";
const int MqttPort = 1883;
const string TopicPrefix = "md/l2/nasdaq/";

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

var mqttFactory = new MqttClientFactory();
using IMqttClient mqttClient = mqttFactory.CreateMqttClient();

MqttClientOptions mqttOptions = new MqttClientOptionsBuilder()
    .WithTcpServer(MqttHost, MqttPort)
    .WithClientId("boulevard-solace-gateway")
    .Build();

await mqttClient.ConnectAsync(mqttOptions, cts.Token);
Console.WriteLine($"[GATEWAY] Connected to MQTT broker at {MqttHost}:{MqttPort}");

using var udpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
udpSocket.Bind(new IPEndPoint(IPAddress.Loopback, ListenPort));
udpSocket.ReceiveTimeout = 500;
Console.WriteLine($"[GATEWAY] Listening for L2 snapshots on 127.0.0.1:{ListenPort}");
Console.WriteLine("[GATEWAY] Press CTRL+C to exit.\n");

long forwardedCount = 0;
long errorCount = 0;
var buffer = new byte[2048];

while (!cts.IsCancellationRequested)
{
    int bytesReceived;
    try
    {
        bytesReceived = udpSocket.Receive(buffer);
    }
    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
    {
        continue;
    }
    catch (SocketException)
    {
        if (cts.IsCancellationRequested)
        {
            break;
        }

        errorCount++;
        continue;
    }

    if (bytesReceived == 0)
    {
        continue;
    }

    try
    {
        using JsonDocument doc = JsonDocument.Parse(buffer.AsMemory(0, bytesReceived));
        string ticker = doc.RootElement.GetProperty("Ticker").GetString() ?? "UNKNOWN";

        MqttApplicationMessage message = new MqttApplicationMessageBuilder()
            .WithTopic(TopicPrefix + ticker)
            .WithPayload(buffer.AsSpan(0, bytesReceived).ToArray())
            .Build();

        await mqttClient.PublishAsync(message, cts.Token);
        forwardedCount++;

        if (forwardedCount % 500 == 0)
        {
            Console.WriteLine($"[GATEWAY] Forwarded {forwardedCount:N0} snapshots to Solace...");
        }
    }
    catch (OperationCanceledException)
    {
        break;
    }
    catch (Exception ex)
    {
        errorCount++;
        Console.WriteLine($"[GATEWAY] Failed to forward snapshot: {ex.Message}");
    }
}

Console.WriteLine($"\n[GATEWAY] Shutting down. Forwarded: {forwardedCount:N0}, Errors: {errorCount:N0}");
await mqttClient.DisconnectAsync();
