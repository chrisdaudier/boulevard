using System.Diagnostics;
using Boulevard.MarketData.Engine;
using Boulevard.Protocol.Itch;
using Boulevard.Simulators.Nasdaq;
using ZstdSharp;

const string DefaultCapturePath =
    "/Users/chrisdaudier/Downloads/market_data/ny4-xnas-tvitch-a-20230822/ny4-xnas-tvitch-a-20230822T000000.pcap.zst";

string capturePath = args.Length > 0 ? args[0] : DefaultCapturePath;
Console.WriteLine($"[NASDAQ] Reading capture: {capturePath}");

var stopwatch = Stopwatch.StartNew();

long packetCount = 0;
long addCount = 0;
long executeCount = 0;
long cancelCount = 0;
long otherCount = 0;

var books = new Dictionary<ushort, OrderBook>();

using (FileStream fileStream = File.OpenRead(capturePath))
using (var decompressionStream = new DecompressionStream(fileStream))
{
    var pcapReader = new PcapReader(decompressionStream);

    while (pcapReader.TryReadNextPacket(out ReadOnlySpan<byte> frame))
    {
        packetCount++;

        if (!EthernetIpUdp.TryExtractUdpPayload(frame, out ReadOnlySpan<byte> udpPayload)
            || udpPayload.Length < MoldUdp64Header.Size)
        {
            continue;
        }

        foreach (ReadOnlySpan<byte> message in new MoldUdp64Reader(udpPayload))
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
                    book.AddOrder(add.OrderReferenceNumber, add.IsBuy ? Side.Buy : Side.Sell, (int)(add.PriceRaw / 100), add.Shares);
                    books[add.StockLocate] = book;
                    break;
                }

                case OrderExecutedMessage.MessageType when OrderExecutedMessage.TryParse(message, out OrderExecutedMessage exec):
                {
                    executeCount++;
                    if (books.TryGetValue(exec.StockLocate, out OrderBook execBook))
                    {
                        execBook.Execute(exec.OrderReferenceNumber, exec.ExecutedShares);
                        books[exec.StockLocate] = execBook;
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
                    }

                    break;
                }

                default:
                    otherCount++;
                    break;
            }
        }
    }
}

stopwatch.Stop();

Console.WriteLine();
Console.WriteLine("[NASDAQ] Pipeline summary");
Console.WriteLine($" -> Packets read:       {packetCount:N0}");
Console.WriteLine($" -> Add Order:          {addCount:N0}");
Console.WriteLine($" -> Order Executed:     {executeCount:N0}");
Console.WriteLine($" -> Order Cancel:       {cancelCount:N0}");
Console.WriteLine($" -> Other ITCH types:   {otherCount:N0}");
Console.WriteLine($" -> Distinct symbols:   {books.Count:N0}");
Console.WriteLine($" -> Elapsed:            {stopwatch.ElapsedMilliseconds:N0} ms");

Console.WriteLine();
Console.WriteLine("[NASDAQ] Sample BBOs (busiest symbols by resting shares):");

var busiestFirst = books
    .Select(kv => (StockLocate: kv.Key, Bbo: kv.Value.GetBbo()))
    .OrderByDescending(x => x.Bbo.BidShares + x.Bbo.AskShares)
    .Take(5);

foreach ((ushort stockLocate, Bbo bbo) in busiestFirst)
{
    string bid = bbo.BidPriceCents.HasValue ? $"${bbo.BidPriceCents.Value / 100.0:F2} x {bbo.BidShares:N0}" : "-";
    string ask = bbo.AskPriceCents.HasValue ? $"${bbo.AskPriceCents.Value / 100.0:F2} x {bbo.AskShares:N0}" : "-";
    Console.WriteLine($" -> Locate {stockLocate,6}: BID {bid,-20} ASK {ask}");
}
