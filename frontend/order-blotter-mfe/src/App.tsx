import { useMemo } from "react";
import { AgGridReact } from "ag-grid-react";
import { AllCommunityModule, ModuleRegistry, themeQuartz, colorSchemeDark, type ColDef } from "ag-grid-community";
import { useOrderBlotter } from "./useOrderBlotter";
import { useFdc3Channels } from "./useFdc3Channels";
import { ChannelSelector } from "./ChannelSelector";
import type { OrderLogEntry } from "./types";

ModuleRegistry.registerModules([AllCommunityModule]);

const darkTheme = themeQuartz.withPart(colorSchemeDark);

const columnDefs: ColDef<OrderLogEntry>[] = [
  {
    field: "submittedAt",
    headerName: "Time",
    width: 110,
    valueFormatter: (p) => (p.value ? new Date(p.value).toLocaleTimeString() : ""),
  },
  { field: "ticker", headerName: "Ticker", width: 100 },
  { field: "side", headerName: "Side", width: 90 },
  { field: "type", headerName: "Type", width: 90 },
  { field: "quantity", headerName: "Qty", width: 100, valueFormatter: (p) => p.value?.toLocaleString() ?? "" },
  { field: "price", headerName: "Price", width: 100, valueFormatter: (p) => p.value?.toFixed(4) ?? "MKT" },
  {
    field: "status",
    headerName: "Status",
    width: 110,
    cellStyle: (p) => ({ color: p.value === "ACCEPTED" ? "#4caf50" : "#e57373" }),
  },
];

function App() {
  const orders = useOrderBlotter();
  const { connected, channels, currentChannelId, selectChannel } = useFdc3Channels();
  const defaultColDef = useMemo(() => ({ sortable: true, resizable: true }), []);

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
      <div style={{ display: "flex", gap: 8, marginBottom: 12, alignItems: "center" }}>
        <p style={{ margin: 0, fontFamily: "monospace" }}>
          Interop: <strong>{connected ? "connected" : "standalone"}</strong> &nbsp;|&nbsp; Orders: <strong>{orders.length}</strong>
        </p>
        <div style={{ marginLeft: "auto" }}>
          <ChannelSelector channels={channels} currentChannelId={currentChannelId} onSelect={selectChannel} />
        </div>
      </div>

      <div style={{ flex: 1, minHeight: 0 }}>
        {orders.length === 0 ? (
          <p style={{ color: "#888" }}>
            No orders yet - submit one from OMS Order Entry (both apps must be on the same FDC3 channel).
          </p>
        ) : (
          <AgGridReact<OrderLogEntry>
            theme={darkTheme}
            rowData={orders}
            columnDefs={columnDefs}
            defaultColDef={defaultColDef}
            getRowId={(params) => params.data.orderId}
          />
        )}
      </div>
    </div>
  );
}

export default App;
