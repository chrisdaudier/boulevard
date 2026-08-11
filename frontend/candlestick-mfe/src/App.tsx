import { useRef, useState } from "react";
import { useActiveTicker } from "./useActiveTicker";
import { useCandles } from "./useCandles";
import { useFdc3Channels } from "./useFdc3Channels";
import { ChannelSelector } from "./ChannelSelector";
import { CandlestickChart } from "./CandlestickChart";

const INTERVAL_OPTIONS = [
  { label: "5s", seconds: 5 },
  { label: "10s", seconds: 10 },
  { label: "30s", seconds: 30 },
  { label: "1m", seconds: 60 },
];

function App() {
  const { ticker, source, setManualTicker } = useActiveTicker();
  const [intervalSeconds, setIntervalSeconds] = useState(10);
  const { candles, status } = useCandles(ticker, intervalSeconds);
  const { connected, channels, currentChannelId, selectChannel } = useFdc3Channels();
  const inputRef = useRef<HTMLInputElement>(null);

  function handleLoad() {
    if (inputRef.current) {
      setManualTicker(inputRef.current.value);
      inputRef.current.value = "";
    }
  }

  return (
    <div
      style={{
        display: "flex",
        flexDirection: "column",
        height: "100vh",
        padding: 12,
        boxSizing: "border-box",
        background: "#1e1e1e",
        color: "#e0e0e0",
      }}
    >
      <div style={{ display: "flex", flexWrap: "wrap", gap: 8, marginBottom: 12, alignItems: "center" }}>
        <input
          ref={inputRef}
          placeholder="Load ticker (e.g. AAPL)"
          onKeyDown={(e) => e.key === "Enter" && handleLoad()}
          style={{ padding: "4px 8px", fontFamily: "monospace", textTransform: "uppercase" }}
        />
        <button onClick={handleLoad} style={{ cursor: "pointer" }}>
          Load
        </button>
        <select value={intervalSeconds} onChange={(e) => setIntervalSeconds(Number(e.target.value))} style={{ padding: "4px 8px" }}>
          {INTERVAL_OPTIONS.map((opt) => (
            <option key={opt.seconds} value={opt.seconds}>
              {opt.label}
            </option>
          ))}
        </select>
        <p style={{ margin: 0, fontFamily: "monospace", fontSize: 13 }}>
          Active: <strong>{ticker ?? "-"}</strong>
          {source && <span style={{ color: "#888" }}> ({source})</span>} &nbsp;|&nbsp; Solace: <strong>{status}</strong>{" "}
          &nbsp;|&nbsp; Interop: <strong>{connected ? "connected" : "standalone"}</strong>
        </p>
        <div style={{ marginLeft: "auto" }}>
          <ChannelSelector channels={channels} currentChannelId={currentChannelId} onSelect={selectChannel} />
        </div>
      </div>

      <p style={{ margin: "0 0 8px", color: "#888", fontSize: 12 }}>
        Candles are built from the BBO midpoint, not trade prints - Boulevard doesn't distribute individual trades to the frontend.
      </p>

      <div style={{ flex: 1, minHeight: 0 }}>
        {candles.length === 0 ? (
          <p style={{ color: "#888" }}>
            No data yet - broadcast a ticker from the Watchlist MFE, or type one above and click Load.
          </p>
        ) : (
          <CandlestickChart candles={candles} />
        )}
      </div>
    </div>
  );
}

export default App;
