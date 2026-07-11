using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

public static class AssetBootstrapLoader
{
    private static readonly HttpClient _httpClient = new();

    public static async Task<AssetBlueprint[]> LoadNasdaqUniverseAsync()
    {
        // Define the assets we want to load for our Nasdaq venue instance
        var targetTickers = new (uint Id, string Symbol)[] 
        {
            (1, "AAPL"),   // Apple
            (2, "MSFT"),   // Microsoft
            (3, "NVDA"),   // NVIDIA
            (4, "AVGO"),   // Broadcom
            (5, "GOOGL"),  // Alphabet (Class A)
            (6, "GOOG"),   // Alphabet (Class C)
            (7, "AMZN"),   // Amazon
            (8, "META"),   // Meta Platforms
            (9, "TSLA"),   // Tesla
            (10, "MU"),    // Micron Technology
            (11, "AMD"),   // Advanced Micro Devices
            (12, "COST"),  // Costco
            (13, "NFLX"),  // Netflix
            (14, "QCOM"),  // Qualcomm
            (15, "ADBE"),  // Adobe
            (16, "INTC"),  // Intel
            (17, "CSCO"),  // Cisco Systems
            (18, "AMAT"),  // Applied Materials
            (19, "TMUS"),  // T-Mobile US
            (20, "TXN"),   // Texas Instruments
            (21, "ISRG"),  // Intuitive Surgical
            (22, "LRCX"),  // Lam Research
            (23, "HON"),   // Honeywell
            (24, "PANW"),  // Palo Alto Networks
            (25, "VRTX"),  // Vertex Pharmaceuticals
            (26, "SNPS"),  // Synopsys
            (27, "CDNS"),  // Cadence Design Systems
            (28, "REGN"),  // Regeneron Pharmaceuticals
            (29, "MDLZ"),  // Mondelez International
            (30, "KLAC"),  // KLA Corporation
            (31, "ADP"),   // Automatic Data Processing
            (32, "BKNG"),  // Booking Holdings
            (33, "SBUX"),  // Starbucks
            (34, "GILD"),  // Gilead Sciences
            (35, "INTU"),  // Intuit
            (36, "ADI"),   // Analog Devices
            (37, "MELI"),  // MercadoLibre
            (38, "PYPL"),  // PayPal Holdings
            (39, "NXPI"),  // NXP Semiconductors
            (40, "CTAS"),  // Cintas
            (41, "MAR"),   // Marriott International
            (42, "ORLY"),  // O'Reilly Automotive
            (43, "WDAY"),  // Workday
            (44, "CRWD"),  // CrowdStrike
            (45, "LULU"),  // Lululemon Athletica
            (46, "MNST"),  // Monster Beverage
            (47, "ADSK"),  // Autodesk
            (48, "CPRT"),  // Copart
            (49, "ROST"),  // Ross Stores
            (50, "PDD"),   // PDD Holdings
            (51, "MCHP"),  // Microchip Technology
            (52, "AEP"),   // American Electric Power
            (53, "DXCM"),  // Dexcom
            (54, "FTNT"),  // Fortinet
            (55, "IDXX"),  // IDEXX Laboratories
            (56, "PCAR"),  // PACCAR
            (57, "FAST"),  // Fastenal
            (58, "PAYX"),  // Paychex
            (59, "ODFL"),  // Old Dominion Freight Line
            (60, "EXC"),   // Exelon
            (61, "XEL"),   // Xcel Energy
            (62, "BKR"),   // Baker Hughes
            (63, "KDP"),   // Keurig Dr Pepper
            (64, "TEAM"),  // Atlassian
            (65, "DDOG"),  // Datadog
            (66, "CEG"),   // Constellation Energy
            (67, "GEHC"),  // GE HealthCare
            (68, "ARM"),   // Arm Holdings
            (69, "ALNY"),  // Alnylam Pharmaceuticals
            (70, "ANSS"),  // Ansys
            (71, "AWK"),   // American Water Works
            (72, "CDW"),   // CDW Corporation
            (73, "CHTR"),  // Charter Communications
            (74, "DASH"),  // DoorDash
            (75, "EA"),    // Electronic Arts
            (76, "FANG"),  // Diamondback Energy
            (77, "ILMN"),  // Illumina
            (78, "MSTR"),  // MicroStrategy
            (79, "KHC"),   // Kraft Heinz
            (80, "LIN"),   // Linde
            (81, "LULUY"), // Lululemon (Alternative/Secondary Listing structure if applicable)
            (82, "MPWR"),  // Monolithic Power Systems
            (83, "MRVL"),  // Marvell Technology
            (84, "ORCL"),  // Oracle
            (85, "PLTR"),  // Palantir Technologies
            (86, "ROP"),   // Roper Technologies
            (87, "TTWO"),  // Take-Two Interactive
            (88, "VRSK"),  // Verisk Analytics
            (89, "WBA"),   // Walgreens Boots Alliance
            (90, "WBD"),   // Warner Bros. Discovery
            (91, "ZS"),    // Zscaler
            (92, "ABNB"),  // Airbnb
            (93, "AXON"),  // Axon Enterprise
            (94, "CCEP"),  // Coca-Cola Europacific Partners
            (95, "SNDK"),  // SanDisk
            (96, "TER"),   // Teradyne
            (97, "STX"),   // Seagate Technology
            (98, "WDC"),   // Western Digital
            (99, "SHOP"),  // Shopify
            (100, "WMT"),  // Walmart
            (101, "SKHY")  // SK Hynix (Newly listed ADR)
        };

        var blueprints = new AssetBlueprint[targetTickers.Length];

        for (int i = 0; i < targetTickers.Length; i++)
        {
            var target = targetTickers[i];
            try
            {
                // Using a reliable, free, unauthenticated public cors-proxy JSON endpoint 
                string url = $"https://query1.finance.yahoo.com/v8/finance/chart/{target.Symbol}?interval=1d&range=1d";
                
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)");
                string jsonString = await _httpClient.GetStringAsync(url);

                using JsonDocument doc = JsonDocument.Parse(jsonString);
                JsonElement root = doc.RootElement;
                
                // Navigate Yahoo Finance's native chart payload structure to find yesterday's close price
                JsonElement resultElement = root.GetProperty("chart").GetProperty("result")[0];
                double closingPrice = resultElement.GetProperty("meta").GetProperty("regularMarketPrice").GetDouble();

                blueprints[i] = new AssetBlueprint(target.Id, target.Symbol, closingPrice);
            }
            catch (Exception ex)
            {
                // Resilient fallback logic if the network fails or the unofficial endpoint shape changes
                Console.WriteLine($"[BOOTSTRAP WARN] Failed to load {target.Symbol} live. Applying static fallback. Error: {ex.Message}");
                double fallbackPrice = target.Symbol == "AAPL" ? 315.00 : 385.00;
                blueprints[i] = new AssetBlueprint(target.Id, target.Symbol, fallbackPrice);
            }
        }

        return blueprints;
    }
}

public readonly struct AssetBlueprint
{
    public uint AssetId { get; init; }
    public string Ticker { get; init; }
    public int StartPriceInCents { get; init; }

    public AssetBlueprint(uint assetId, string ticker, double startingPrice)
    {
        AssetId = assetId;
        Ticker = ticker;
        StartPriceInCents = (int)Math.Round(startingPrice * 100.0);
    }
}