import { useMemo, useRef } from "react";
import { AgGridReact } from "ag-grid-react";
import { AllCommunityModule, ModuleRegistry, themeQuartz, colorSchemeDark, type ColDef } from "ag-grid-community";
import { useActiveTicker } from "./useActiveTicker";
import { useActiveSymbolFeed } from "./useActiveSymbolFeed";
import { useFdc3Channels } from "./useFdc3Channels";
import { ChannelSelector } from "./ChannelSelector";
import { OrderTicketPanel } from "./OrderTicketPanel";
import type { L2Row } from "./types";

ModuleRegistry.registerModules([AllCommunityModule]);

const darkTheme = themeQuartz.withPart(colorSchemeDark);

const columnDefs: ColDef<L2Row>[] = [
  { field: "side", headerName: "Side", width: 90 },
  { field: "level", headerName: "Level", width: 90 },
  { field: "price", headerName: "Price", width: 110, valueFormatter: (p) => p.value?.toFixed(4) ?? "" },
  { field: "shares", headerName: "Shares", width: 120, valueFormatter: (p) => p.value?.toLocaleString() ?? "" },
];

function App() {
  const { ticker, source, setManualTicker } = useActiveTicker();
  const { rows, snapshot, status } = useActiveSymbolFeed(ticker);
  const { connected, channels, currentChannelId, selectChannel } = useFdc3Channels();
  const inputRef = useRef<HTMLInputElement>(null);
  const defaultColDef = useMemo(() => ({ sortable: true, resizable: true }), []);

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
      <h2 style={{ margin: "0 0 8px" }}>Boulevard OMS Order Entry</h2>
      <div style={{ display: "flex", gap: 8, marginBottom: 12, alignItems: "center" }}>
        <input
          ref={inputRef}
          placeholder="Load ticker (e.g. AAPL)"
          onKeyDown={(e) => e.key === "Enter" && handleLoad()}
          style={{ padding: "4px 8px", fontFamily: "monospace", textTransform: "uppercase" }}
        />
        <button onClick={handleLoad} style={{ cursor: "pointer" }}>
          Load
        </button>
        <p style={{ margin: 0, fontFamily: "monospace" }}>
          Active: <strong>{ticker ?? "-"}</strong>
          {source && <span style={{ color: "#888" }}> ({source})</span>} &nbsp;|&nbsp; Solace: <strong>{status}</strong>{" "}
          &nbsp;|&nbsp; Interop: <strong>{connected ? "connected" : "standalone"}</strong>
        </p>
        <div style={{ marginLeft: "auto" }}>
          <ChannelSelector channels={channels} currentChannelId={currentChannelId} onSelect={selectChannel} />
        </div>
      </div>

      <div style={{ flex: 1, display: "flex", gap: 12, minHeight: 0 }}>
        <div style={{ flex: 2, minWidth: 0 }}>
          {rows.length === 0 ? (
            <p style={{ color: "#888" }}>
              No ticker loaded - broadcast one from the Watchlist MFE, or type a ticker above and click Load.
            </p>
          ) : (
            <AgGridReact<L2Row>
              theme={darkTheme}
              rowData={rows}
              columnDefs={columnDefs}
              defaultColDef={defaultColDef}
              getRowId={(params) => `${params.data.side}-${params.data.level}`}
            />
          )}
        </div>
        <div style={{ flex: 1, minWidth: 280, maxWidth: 360 }}>
          <OrderTicketPanel ticker={ticker} snapshot={snapshot} />
        </div>
      </div>
    </div>
  );
}

export default App;
