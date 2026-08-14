# Boulevard

Boulevard is a market-data simulation and distribution platform built for a **multi-manager
hedge fund** operating model: a shared, firm-owned data/infrastructure layer that a large number
of independently-run **strategy pods** plug into, rather than each pod standing up its own feed
handlers and book-building logic. It replays real historical NASDAQ TotalView-ITCH 5.0 exchange
data over real UDP multicast, reconstructs a full L3 order book, and distributes both the raw
feed (for pods that want to build their own book) and a conflated L2 view (for pods and desktop
tools that just need top-of-book/depth) — down to four React micro-frontends (watchlist, order
entry, order blotter, candlestick chart) running inside an OpenFin/HERE Core desktop, wired
together over FDC3.

Nothing here talks to a real exchange. The publisher replays captured pcap data on a loop, which
makes the whole pipeline runnable and demoable on a laptop while remaining structurally identical
to how a real feed handler → book engine → distribution pipeline is built.

## Why this operating model

In a multi-manager platform, dozens of pods each run their own P&L-independent strategies, often
on different tech stacks, different languages, different UI preferences. Two things follow from
that:

- **Market data and book-building should be centralized, not duplicated.** Every pod re-parsing
  ITCH and re-building the same order book is wasted CPU, wasted engineering time, and a source of
  divergent bugs (two independently-written books can disagree about the state of the world in
  subtle ways). Boulevard's Edge tier is the single source of truth for L2 state; everything
  downstream consumes its output rather than re-deriving it.
- **Distribution needs to support both extremes.** A pod's *signal-generating* strategy process
  co-located at the edge cares about tens-of-microseconds and wants the rawest, most direct feed
  it can get (multicast today, shared-memory IPC as the next step — see
  [Roadmap](#roadmap--known-limitations)). A pod's *human trader's* desktop watchlist or order
  ticket cares about a few hundred milliseconds of latency at most, but needs to be interoperable
  across many different MFEs from different teams. Boulevard deliberately uses different transport
  for each: multicast/shared-memory for the hot path, Solace PubSub+ and FDC3 for the human/desktop
  path.

## Why C# / .NET for the backend

This is a deliberate choice for **mid-frequency** pod strategies (decision latency in the
microseconds-to-low-milliseconds range), not for ultra-low-latency market making
(kernel-bypass NICs, FPGAs, nanosecond budgets) — that's a different tool for a different job.
Within the mid-frequency tier, .NET earns its place on engineering economics as much as raw speed:

- **You can write effectively zero-allocation, cache-friendly hot paths without leaving the
  managed runtime.** `Span<T>`/`Memory<T>`, `readonly ref struct` message parsers, `stackalloc`,
  and flat pre-sized arrays get you C-like data layout and allocation behavior for the 10% of the
  codebase that's actually hot, while the other 90% (networking, JSON, config, tooling) gets to be
  normal, safe, boring C#.
- **Memory safety removes an entire bug class** (dangling pointers, buffer overruns, use-after-free)
  that is disproportionately expensive on a shared platform serving many pods — a memory-safety bug
  in a shared Edge process is a firm-wide incident, not a single pod's problem.
- **Hiring pool and iteration speed.** A multi-manager platform's engineering bottleneck is usually
  breadth of integration work (new venues, new protocols, new pod requirements), not
  nanosecond-shaving. C#'s tooling, ecosystem, and hiring pool make that breadth of work faster
  without giving up the ability to be disciplined where it matters.
- **One codebase, dev-to-prod.** The same code runs on macOS for local development and Linux in
  production (containerized — see `docker/`), with CPU affinity pinning active only where the OS
  supports it (`sched_setaffinity` on Linux, a documented no-op on macOS).
- **The numbers back it up for this tier.** See [benchmarks](#benchmarked-hot-path) below —
  low-teens nanoseconds per order-book mutation and low-microsecond socket-to-BBO latency under
  real replay load, measured, not assumed.

## Architecture

```
                        ┌─────────────────────────────┐
  historical ITCH  ───▶ │ Boulevard.Simulators.Nasdaq │  UDP multicast (MoldUDP64), 239.255.0.1:1234
  pcap capture           │ ("the exchange")            │
                        └─────────────────────────────┘
                                       │
                                       ▼
                        ┌─────────────────────────────┐
                        │ Boulevard.Edge.MarketData    │  socket thread ──▶ SPSC channel ──▶ worker thread
                        │  - Boulevard.Protocol.Itch   │  (zero-alloc parse, per-symbol OrderBook mutation)
                        │  - Boulevard.MarketData.Engine│
                        └─────────────────────────────┘
                             │                    │
                 raw L3 multicast          L2 snapshots, top 200 symbols, 250ms
                 (pods that want                  │  UDP loopback :5001
                 their own book)                  ▼
                                       ┌─────────────────────────────┐
                                       │ Boulevard.Edge.SolaceGateway │  thin UDP → MQTT bridge,
                                       │  (no ITCH/book knowledge)    │  no ITCH/OrderBook knowledge
                                       └─────────────────────────────┘
                                                      │  MQTT :1883
                                                      ▼
                                       ┌─────────────────────────────┐
                                       │ Solace PubSub+ (Docker)      │  MQTT ingress, native Web
                                       │                              │  Messaging (WebSocket) egress
                                       └─────────────────────────────┘
                                                      │  WebSocket :8008
                    ┌───────────────┬───────────────────────┼───────────────────────┐
                    ▼               ▼                       ▼                       ▼
       ┌─────────────────┐ ┌──────────────────┐  ┌─────────────────────┐  ┌─────────────────┐
       │ watchlist-mfe    │ │ oms-order-entry  │  │ candlestick-mfe      │  │ order-blotter-mfe│
       │ (AG Grid)        │ │ (order ticket +  │  │ (lightweight-charts, │  │ (AG Grid)        │
       │ :5174            │ │  bid/ask ladder) │  │  midpoint OHLC)       │  │ :5175            │
       │                  │ │ :5173            │  │ :5176                │  │                  │
       └─────────────────┘ └──────────────────┘  └─────────────────────┘  └─────────────────┘
              │ fdc3.instrument     ▲  │ blvd.order (custom context)              ▲
              └─────────────────────┘  └───────────────────────────────────────────┘
                                   all four share one OpenFin / HERE Core desktop container
                                   (frontend/interop/openfin/manifest.json)
```

### Components

| Project | Role |
|---|---|
| `Boulevard.Protocol.Itch` | Zero-allocation ITCH 5.0 message parsers (`readonly ref struct`) — Add Order, Add Order (MPID), Order Executed, Executed With Price, Cancel, Delete, Replace, Cross Trade, Stock Directory. |
| `Boulevard.MarketData.Engine` | Protocol-agnostic L3 order book: open-addressing hash table (Fibonacci hashing, backward-shift deletion) for O(1) order lookup, sorted flat arrays for price levels. `Boulevard.MarketData.Engine.Benchmarks` verifies allocation behavior and latency via BenchmarkDotNet. |
| `Boulevard.Simulators.Nasdaq` | Reads nanosecond-resolution pcap captures (Zstandard-compressed) and republishes them over real UDP multicast using MoldUDP64 framing, paced to the original capture timing (or as-fast-as-possible). Supports chaining multiple capture files and looping for extended/continuous replay. |
| `Boulevard.Edge.MarketData` | The Edge tier. Thread-decoupled: a dedicated socket thread does blocking receive and sequence/reorder handling; a dedicated worker thread drains a bounded `System.Threading.Channels`-based queue, parses messages, and mutates per-symbol `OrderBook` instances. Publishes conflated L2 snapshots for the busiest symbols on a timer, entirely off the mutation hot path. |
| `Boulevard.Edge.SolaceGateway` | A deliberately thin bridge: UDP in, MQTT out. No ITCH or order-book knowledge, so there's exactly one place or business logic can diverge from ground truth — `Edge.MarketData`. |
| `frontend/watchlist-mfe` | React + AG Grid micro-frontend. A user-curated, localStorage-persisted watchlist subscribing per-symbol to Solace, not the full feed. Defaults on first run to the 20 most active tickers measured directly from the six pcap files the demo publisher replays (SPY, QQQ, TSLA, GOOG, GOOGL, AAPL, NVDA, IWM, IVV, TQQQ, VOO, AMD, AMZN, SMH, SPXL, XLY, MSFT, SOXL, XLK, DIA). Selecting a row broadcasts an `fdc3.instrument` context. Also hosts the platform manifest and provider page as static files for desktop interop (see below). |
| `frontend/oms-order-entry` | React micro-frontend, single-column layout: a mock order ticket on top, a combined bid/ask depth ladder (one row per level, both sides side-by-side) below — for exactly one "active" symbol (driven by FDC3 context, defaulting to AAPL, or manual entry). Submitting an order broadcasts a custom `blvd.order` FDC3 context rather than keeping any order history of its own. |
| `frontend/order-blotter-mfe` | React + AG Grid micro-frontend with no state of its own beyond what it's received — records every order broadcast on the `blvd.order` context by `oms-order-entry` on the same channel. |
| `frontend/candlestick-mfe` | React micro-frontend using `lightweight-charts`. Renders a live candlestick chart for the active symbol, built client-side from the BBO midpoint at a selectable interval (5s/10s/30s/1m) — Boulevard doesn't distribute individual trade prints to the frontend, so this is a midpoint-derived proxy for a real trade-based candle, clearly labeled as such in the UI. |
| `frontend/interop/` | OpenFin/HERE Core Platform manifest, FDC3 App Directory entry, and the mkcert-issued local TLS cert used to launch all four MFEs inside one desktop container. |

## How low latency is maintained

- **Zero-allocation hot path.** ITCH messages are parsed into `readonly ref struct`s over a
  `ReadOnlySpan<byte>` — no per-message heap allocation. The order book itself is two flat,
  pre-sized arrays (open-addressing hash table for orders, sorted arrays for price levels) instead
  of `Dictionary`/`SortedDictionary`; depth snapshots are returned as `ReadOnlySpan<PriceLevel>`
  zero-copy views into the book's own backing arrays.
- **Thread decoupling.** The socket-receive thread does nothing but read and hand datagrams to a
  bounded SPSC queue (`ChannelDatagramQueue`, backed by two `System.Threading.Channels`, pooled
  buffer slots, never allocates per datagram). All parsing and book mutation happens on a separate
  worker thread. This means a GC pause or a slow downstream consumer can never stall the socket
  read loop and cause the OS to drop packets.
- **Lock scope is treated as a latency budget, not an implementation detail.** The L2-publishing
  timer used to hold the same lock the worker thread needs for the *entire* JSON-serialize +
  socket-send cycle for up to 200 symbols; under sustained load that starved the worker thread
  badly enough to overflow the datagram queue. The fix: copy the small amount of state needed
  under the lock, then serialize and send outside it. Same discipline applies throughout —
  anything that isn't provably fast doesn't run while a hot-path lock is held.
- **Explicit, measured backpressure instead of implicit failure.** If the worker thread ever falls
  behind, the socket thread does *not* block (which would just move the problem to the OS-level
  receive buffer and manifest as a mysterious packet loss elsewhere) — it drops the datagram and
  increments a counter that's visible in the periodic snapshot log. Loss is a first-class,
  observable signal, not something discovered after the fact from an inconsistent order book.
- **CPU affinity pinning** on Linux (P/Invoke `sched_setaffinity`) for the socket and worker
  threads, to reduce context-switch and cache-locality jitter — a documented no-op on macOS during
  local development.
- **Reorder tolerance without stalling.** MoldUDP64 sequence gaps are held in a small, pooled
  reorder buffer for a bounded window (5ms) rather than blocking the pipeline waiting for a
  possibly-lost packet; a genuine gap is detected and counted, not silently papered over.
- **The distribution tier is explicitly decoupled from the ingestion tier.** L2 snapshots are
  conflated (one full snapshot per symbol every 250ms, not a delta stream) and pushed over a
  separate UDP hop to a bridge process with zero ITCH/book logic. A slow Solace broker or a
  disconnected browser can never propagate backpressure into the L3 ingestion path.

### Benchmarked hot path

`Boulevard.MarketData.Engine.Benchmarks` (BenchmarkDotNet) verifies, rather than assumes, the
above:

| Operation | Latency | Allocations |
|---|---|---|
| `AddOrder` / `Execute` / `Cancel` | ~11 ns | 0 B |
| `GetBbo` | ~0.8 ns | 0 B |

End-to-end, `Edge.MarketData` records socket-receipt-to-BBO-recomputed latency on every message;
under real replay load this holds at p50 ≈ 10–15 µs, comfortably inside the budget for a
mid-frequency pod's own decision loop.

## How systems connect

**Internal (hot path):**
- Publisher → Edge: real UDP multicast, MoldUDP64 framing over ITCH 5.0 — the same wire protocol
  and transport a real exchange feed would use, so nothing about the Edge tier's design has to
  change to point at a real feed later.
- Edge → pods (co-located): pods that need the raw L3 feed subscribe to the same multicast group
  directly. Shared-memory IPC for same-host pod processes is the intended next step for this path
  (see [Roadmap](#roadmap--known-limitations)) — multicast has more latency/jitter than a same-host
  path needs.

**External (distribution / human path):**
- Edge → Solace: conflated L2 snapshots over loopback UDP to a thin bridge process, which
  publishes to Solace PubSub+ over MQTT.
- Solace → desktop: browser/desktop clients (the MFEs) connect directly to Solace over its native
  Web Messaging API (a real WebSocket, not MQTT-over-WebSocket) — no server-side fan-out process in
  the middle.
- MFE ↔ MFE: the four MFEs don't talk to each other directly. They're independently-deployable web
  apps hosted as separate views inside one OpenFin/HERE Core platform window, and they share
  context over **FDC3** — the vendor-neutral desktop interop standard, via `@finos/fdc3`'s
  `getAgent()`/user-channel APIs. `watchlist-mfe` broadcasts the standard `fdc3.instrument`
  context on row click; `oms-order-entry` and `candlestick-mfe` both listen for it to drive their
  "active ticker"; `oms-order-entry` also broadcasts a custom `blvd.order` context (FDC3 has no
  built-in order type) on submit, which `order-blotter-mfe` listens for. This is the same
  mechanism that would let a pod's own, differently-built MFE join the same desktop and
  interoperate with Boulevard's, which is the point in a multi-manager environment where pods do
  not all share one frontend stack.

## Repository layout

```
Boulevard.slnx                     Solution file
src/
  Protocols/Boulevard.Protocol.Itch/        ITCH 5.0 message parsers
  Core/Boulevard.MarketData.Engine/         Order book engine
  Core/Boulevard.MarketData.Engine.Benchmarks/  BenchmarkDotNet suite
  Simulators/Boulevard.Simulators.Nasdaq/   Historical pcap → UDP multicast publisher
  Edge/Boulevard.Edge.MarketData/           L3 ingestion, book maintenance, L2 publishing
  Edge/Boulevard.Edge.SolaceGateway/        UDP L2 → MQTT bridge
frontend/
  watchlist-mfe/                            Ticker watchlist MFE (:5174)
  oms-order-entry/                          Order ticket + bid/ask depth ladder MFE (:5173)
  order-blotter-mfe/                        Order blotter MFE (:5175)
  candlestick-mfe/                          Candlestick chart MFE (:5176)
  interop/                                  OpenFin/HERE Core manifest, FDC3 App Directory, dev TLS cert
docker/                                     Dockerfiles + Containerlab topology for containerized network testing
```

## Prerequisites

| Tool | Used for |
|---|---|
| [.NET 10 SDK](https://dotnet.microsoft.com/download) | All backend services |
| [Node.js](https://nodejs.org/) 20+ and npm | All four frontend MFEs |
| [Docker](https://www.docker.com/) | Running the Solace PubSub+ broker |
| A NASDAQ TotalView-ITCH 5.0 pcap capture set | Market data source. This project was developed against real historical captures (e.g. from [Databento](https://databento.com/)) — nanosecond-resolution, gzip/Zstandard-compressed pcap, one file per ~10-minute window, named `<venue>T<HHMMSS>.pcap.zst`. |
| [mkcert](https://github.com/FiloSottile/mkcert) (`brew install mkcert`) | Only needed for desktop interop — HERE Core's `fins://` launch scheme requires HTTPS and will not accept an untrusted self-signed cert. |
| [OpenFin](https://www.openfin.co/) / [HERE Core](https://www.here.io/) runtime | Only needed for desktop interop testing — see [below](#desktop-interop-openfinhere-core). |

## Running it end-to-end

### 1. Build the backend

```
dotnet build Boulevard.slnx
```

### 2. Start the Solace broker

```
docker run -d --name solace --shm-size=2g \
  -p 1883:1883 -p 8008:8008 -p 8080:8080 \
  -e username_admin_password=admin -e username_admin_globalaccesslevel=admin \
  solace/solace-pubsub-standard
```

Give it 20–30 seconds to finish starting (`docker logs solace` should stop scrolling; MQTT on
`1883` is the fastest thing to poll for).

### 3. Start the Edge tier

On macOS, with more than one active network interface, pin the multicast interface explicitly on
**every** process below (publisher and Edge) so the OS doesn't route multicast somewhere
unexpected and/or deliver duplicate copies:

```
export MULTICAST_LOCAL_ADDRESS=127.0.0.1
```

```
dotnet run --project src/Edge/Boulevard.Edge.MarketData
dotnet run --project src/Edge/Boulevard.Edge.SolaceGateway
```

### 4. Replay market data

```
dotnet run --project src/Simulators/Boulevard.Simulators.Nasdaq -- \
  --speed 1 --minutes 60 --loop \
  /path/to/captures/<venue>T133000.pcap.zst
```

- `--speed 1` paces the replay to real historical timing (`0` = as fast as possible).
- `--minutes N` auto-chains consecutive 10-minute capture files following from the one you gave it.
- `--loop` restarts the whole chained set from the beginning once it finishes, for a
  continuously-running demo.

Ticker names (`StockLocate → symbol`) are only resolved from ITCH Stock Directory (`'R'`) messages,
which are broadcast once early in a real trading session. If you start the replay midday, seed
ticker resolution and full order-book history first with a fast, no-pacing pass over everything
from the start of the session up to your target window (`--speed 0`), *then* start the paced replay
you actually want to watch — otherwise you'll see phantom crossed books from orders whose
lifecycle started before your replay window began.

### 5. Run the frontends

```
cd frontend/watchlist-mfe && npm install && npm run dev       # https://localhost:5174
cd frontend/oms-order-entry && npm install && npm run dev     # https://localhost:5173
cd frontend/order-blotter-mfe && npm install && npm run dev   # https://localhost:5175
cd frontend/candlestick-mfe && npm install && npm run dev     # https://localhost:5176
```

Open any of the four URLs directly in a browser to use it standalone (no desktop container
needed) — every app falls back gracefully to "Interop: standalone" and local-only ticker entry
when no FDC3 desktop agent is present (`order-blotter-mfe` just won't receive anything, since
orders only ever arrive as an FDC3 broadcast).

Each dev server is already configured for HTTPS via the shared mkcert certificate in
`frontend/interop/certs/` (see [Desktop interop](#desktop-interop-openfinhere-core) below to
generate it) — plain HTTP is not supported, so a fresh checkout needs that certificate generated
once before `npm run dev` will start cleanly.

## Desktop interop (OpenFin / HERE Core)

Running all four MFEs inside one desktop container with live FDC3 context sharing needs a bit more
setup than a browser tab, because HERE Core's `fins://` launch scheme fetches the manifest over
HTTPS and will hard-reject an untrusted certificate (no click-through, unlike a browser).

1. **Issue a locally-trusted certificate** (one-time, or whenever the cert expires):
   ```
   mkcert -install                      # installs mkcert's CA — needs your admin password
   cd frontend/interop/certs
   mkcert localhost 127.0.0.1 ::1
   ```
   If `mkcert -install` reports success without prompting for a password, it likely only trusted
   the CA in your *login* keychain, which curl/Safari accept but Chromium-based apps (which is what
   OpenFin/HERE Core is, under the hood) do not. Confirm it's really in the *System* keychain:
   ```
   security find-certificate -c "mkcert" /Library/Keychains/System.keychain
   ```
   If that fails, add it explicitly:
   ```
   sudo security add-trusted-cert -d -r trustRoot -k /Library/Keychains/System.keychain \
     "$(mkcert -CAROOT)/rootCA.pem"
   ```

2. **Start all four MFE dev servers** (step 5 above) — they're already configured to serve over
   HTTPS using the certs generated above (`vite.config.ts` in each app), and `watchlist-mfe` also
   serves the platform manifest and provider page as static files (`public/manifest.json`,
   `public/provider.html`).

3. **Install OpenFin / HERE Core** if you don't already have it — the free
   [`openfin-cli`](https://www.npmjs.com/package/openfin-cli) (`npm install openfin-cli`) will pull
   down the runtime on first launch, no license needed for local dev/testing.

4. **Launch the platform.** This must be run from your own interactive terminal, not a
   tool-invoked/background shell — GUI apps launched from a non-interactive session won't attach a
   visible window to your desktop even though the process runs successfully.
   ```
   open "fins://localhost:5174/manifest.json"
   ```
   You should see one window with four views: `watchlist-mfe` and `oms-order-entry` in narrow
   columns on the left, `candlestick-mfe` and `order-blotter-mfe` stacked in the remaining space on
   the right — each showing `Interop: connected` and a row of colored FDC3 channel dots.

5. **Join the same channel** (click the same colored dot) in all four views, add a ticker to the
   watchlist, and click its row — `oms-order-entry`'s and `candlestick-mfe`'s active ticker should
   update immediately via the `fdc3.instrument` broadcast. Submit an order from `oms-order-entry`
   and it should appear in `order-blotter-mfe` via the custom `blvd.order` broadcast.

If you change `frontend/interop/openfin/manifest.json`, re-copy it to
`frontend/watchlist-mfe/public/manifest.json` (or symlink it) before relaunching, and fully close
the existing platform window first — the platform's fixed `uuid` means a relaunch while a window is
still open just refocuses the old instance instead of picking up the change.

### Advanced: containerized network testing

`docker/` contains Dockerfiles for the publisher/subscriber and a Containerlab topology
(`topology.clab.yml`) for running the publisher and Edge tier as separate containers connected over
a simulated network, for testing behavior under real packet loss/reordering/latency rather than a
local loopback interface.

## Roadmap / known limitations

- **Shared-memory IPC for co-located pods** is the intended low-latency path for same-host pod
  strategy processes that need the raw L3 feed with less latency/jitter than multicast — not yet
  built.
- **The order ticket in `oms-order-entry` is a UI-only mock.** There is no OMS/exchange backend in
  this system; submitting an order validates the form and broadcasts it over FDC3 for
  `order-blotter-mfe` to record. There is no real order routing, and outside a desktop container
  (standalone browser use) a submitted order has nowhere to go and is simply dropped after a local
  confirmation message.
- **`candlestick-mfe`'s candles are BBO-midpoint-derived, not trade-based.** Boulevard doesn't
  distribute individual trade prints (ITCH Order Executed messages are consumed server-side for
  book maintenance but never forwarded to the frontend) — the chart is a reasonable, clearly-labeled
  proxy, not a substitute for real OHLC-from-trades.
- **FDC3 App Directory** (`frontend/interop/fdc3-app-directory.json`) is provided in the standard
  format, but only verified against OpenFin/HERE Core — it has not been validated against
  interop.io's io.Connect Desktop (a separate product, unrelated to the OpenFin→HERE rebrand,
  despite similar naming in this space).
