import { useRef } from "react";
import { AllCommunityModule, ModuleRegistry } from "ag-grid-community";
import { useActiveTicker } from "./useActiveTicker";
import { useActiveSymbolFeed } from "./useActiveSymbolFeed";
import { useFdc3Channels } from "./useFdc3Channels";
import { ChannelSelector } from "./ChannelSelector";
import { OrderTicketPanel } from "./OrderTicketPanel";
import { DepthLadder } from "./DepthLadder";

ModuleRegistry.registerModules([AllCommunityModule]);

function App() {
  const { ticker, source, setManualTicker } = useActiveTicker();
  const { rows, snapshot, status } = useActiveSymbolFeed(ticker);
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
          style={{ flex: 1, padding: "4px 8px", fontFamily: "monospace", textTransform: "uppercase" }}
        />
        <button onClick={handleLoad} style={{ cursor: "pointer", height: 28 }}>
          Load
        </button>
        <p style={{ margin: 0, fontFamily: "monospace", fontSize: 10 }}>
          Active: <strong>{ticker ?? "-"}</strong>
          {source && <span style={{ color: "#888" }}> ({source})</span>} &nbsp;|&nbsp; Solace: <strong>{status}</strong>{" "}
          &nbsp;|&nbsp; Interop: <strong>{connected ? "connected" : "standalone"}</strong>
        </p>
        <div style={{ marginLeft: "auto", display: "none" }}>
          <ChannelSelector channels={channels} currentChannelId={currentChannelId} onSelect={selectChannel} />
        </div>
      </div>

      <OrderTicketPanel ticker={ticker} snapshot={snapshot} />

      <div style={{ flex: 1, minHeight: 0, marginTop: 12 }}>
        <DepthLadder rows={rows} />
      </div>
    </div>
  );
}

export default App;
