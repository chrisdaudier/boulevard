import { useMemo } from "react";
import { AgGridReact } from "ag-grid-react";
import { themeQuartz, colorSchemeDark, type ColDef } from "ag-grid-community";
import type { DepthRow } from "./types";

const darkTheme = themeQuartz.withPart(colorSchemeDark);

const numberFmt = (p: { value: number | null }) => p.value?.toLocaleString() ?? "";
const priceFmt = (p: { value: number | null }) => p.value?.toFixed(4) ?? "";

const columnDefs: ColDef<DepthRow>[] = [
  { field: "bidShares", headerName: "Size", width: 100, valueFormatter: numberFmt, cellStyle: { textAlign: "right" } },
  {
    field: "bidPrice",
    headerName: "Bid",
    width: 100,
    valueFormatter: priceFmt,
    cellStyle: { textAlign: "right", background: "rgba(76, 175, 80, 0.12)", color: "#4caf50", fontWeight: 600 },
  },
  {
    field: "askPrice",
    headerName: "Ask",
    width: 100,
    valueFormatter: priceFmt,
    cellStyle: { background: "rgba(229, 115, 115, 0.12)", color: "#e57373", fontWeight: 600 },
  },
  { field: "askShares", headerName: "Size", width: 100, valueFormatter: numberFmt },
];

interface DepthLadderProps {
  rows: DepthRow[];
}

/** A combined bid/ask ladder - one row per depth level, both sides shown tightly side-by-side. */
export function DepthLadder({ rows }: DepthLadderProps) {
  const defaultColDef = useMemo(() => ({ sortable: false, resizable: true }), []);

  if (rows.length === 0) {
    return <p style={{ color: "#888" }}>No depth to show yet.</p>;
  }

  return (
    <div style={{ height: "100%" }}>
      <AgGridReact<DepthRow>
        theme={darkTheme}
        rowData={rows}
        columnDefs={columnDefs}
        defaultColDef={defaultColDef}
        getRowId={(params) => String(params.data.level)}
      />
    </div>
  );
}
