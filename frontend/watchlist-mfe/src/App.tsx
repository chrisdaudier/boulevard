import { useMemo, useRef, useState } from "react";
import { AgGridReact } from "ag-grid-react";
import {
  AllCommunityModule,
  ModuleRegistry,
  themeQuartz,
  colorSchemeDark,
  type ColDef,
  type ICellRendererParams,
  type RowClickedEvent,
} from "ag-grid-community";
import { useWatchlist } from "./useWatchlist";
import { useFdc3Channels } from "./useFdc3Channels";
import { ChannelSelector } from "./ChannelSelector";
import { getFdc3Agent, tickerToInstrument } from "./fdc3";
import type { WatchlistRow } from "./types";

ModuleRegistry.registerModules([AllCommunityModule]);

const darkTheme = themeQuartz.withPart(colorSchemeDark);

function App() {
  const { rows, status, addTicker, removeTicker } = useWatchlist();
  const { connected, channels, currentChannelId, selectChannel } = useFdc3Channels();
  const [selectedTicker, setSelectedTicker] = useState<string | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  function handleRowClicked(event: RowClickedEvent<WatchlistRow>) {
    const ticker = event.data?.ticker;
    if (!ticker) {
      return;
    }

    setSelectedTicker(ticker);
    getFdc3Agent()
      .then((fdc3) => fdc3.broadcast(tickerToInstrument(ticker)))
      .catch(() => {
        // No desktop agent connected (e.g. standalone dev testing) - selection still highlights
        // locally, it just doesn't reach another MFE.
      });
  }

  const columnDefs = useMemo<ColDef<WatchlistRow>[]>(
    () => [
      { field: "ticker", headerName: "Ticker", pinned: "left", width: 100 },
      { field: "bidPrice", headerName: "Bid", width: 100, valueFormatter: (p) => p.value?.toFixed(4) ?? "-" },
      { field: "bidShares", headerName: "Bid Size", width: 110, valueFormatter: (p) => p.value?.toLocaleString() ?? "-" },
      { field: "askPrice", headerName: "Ask", width: 100, valueFormatter: (p) => p.value?.toFixed(4) ?? "-" },
      { field: "askShares", headerName: "Ask Size", width: 110, valueFormatter: (p) => p.value?.toLocaleString() ?? "-" },
      { field: "spread", headerName: "Spread", width: 100, valueFormatter: (p) => p.value?.toFixed(4) ?? "-" },
      {
        field: "updatedUtc",
        headerName: "Updated",
        width: 110,
        valueFormatter: (p) => (p.value ? new Date(p.value).toLocaleTimeString() : "-"),
      },
      {
        headerName: "",
        width: 90,
        sortable: false,
        cellRenderer: (params: ICellRendererParams<WatchlistRow>) => (
          <button onClick={() => params.data && removeTicker(params.data.ticker)} style={{ cursor: "pointer" }}>
            Remove
          </button>
        ),
      },
    ],
    [removeTicker],
  );

  const defaultColDef = useMemo(() => ({ sortable: true, resizable: true }), []);

  function handleAdd() {
    if (inputRef.current) {
      addTicker(inputRef.current.value);
      inputRef.current.value = "";
      inputRef.current.focus();
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
      <h2 style={{ margin: "0 0 8px" }}>Boulevard Ticker Watchlist</h2>
      <div style={{ display: "flex", gap: 8, marginBottom: 12 }}>
        <input
          ref={inputRef}
          placeholder="Add ticker (e.g. AAPL)"
          onKeyDown={(e) => e.key === "Enter" && handleAdd()}
          style={{ padding: "4px 8px", fontFamily: "monospace", textTransform: "uppercase" }}
        />
        <button onClick={handleAdd} style={{ cursor: "pointer" }}>
          Add
        </button>
        <p style={{ margin: "4px 0 0 12px", fontFamily: "monospace" }}>
          Solace status: <strong>{status}</strong> &nbsp;|&nbsp; Watching: <strong>{rows.length}</strong> &nbsp;|&nbsp; Interop:{" "}
          <strong>{connected ? "connected" : "standalone"}</strong>
        </p>
        <div style={{ marginLeft: "auto" }}>
          <ChannelSelector channels={channels} currentChannelId={currentChannelId} onSelect={selectChannel} />
        </div>
      </div>
      <p style={{ margin: "0 0 8px", color: "#888", fontSize: 12 }}>
        Click a row to broadcast that ticker to other connected MFEs (e.g. OMS Order Entry).
      </p>
      <div style={{ flex: 1 }}>
        {rows.length === 0 ? (
          <p style={{ color: "#888" }}>No tickers yet - add one above to start watching its BBO.</p>
        ) : (
          <AgGridReact<WatchlistRow>
            theme={darkTheme}
            rowData={rows}
            columnDefs={columnDefs}
            defaultColDef={defaultColDef}
            getRowId={(params) => params.data.ticker}
            onRowClicked={handleRowClicked}
            getRowStyle={(params) =>
              params.data?.ticker === selectedTicker ? { background: "#2d4a63" } : undefined
            }
          />
        )}
      </div>
    </div>
  );
}

export default App;
