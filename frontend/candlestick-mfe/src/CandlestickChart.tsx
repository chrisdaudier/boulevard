import { useEffect, useRef } from "react";
import { createChart, CandlestickSeries, type IChartApi, type ISeriesApi, type UTCTimestamp } from "lightweight-charts";
import type { Candle } from "./types";

interface CandlestickChartProps {
  candles: Candle[];
}

/**
 * Thin imperative wrapper around lightweight-charts - the library owns its own canvas rendering,
 * so this pushes data into it via setData() rather than letting React re-render the chart itself.
 */
export function CandlestickChart({ candles }: CandlestickChartProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const chartRef = useRef<IChartApi | null>(null);
  const seriesRef = useRef<ISeriesApi<"Candlestick"> | null>(null);

  useEffect(() => {
    if (!containerRef.current) {
      return;
    }

    const chart = createChart(containerRef.current, {
      layout: { background: { color: "#1e1e1e" }, textColor: "#e0e0e0" },
      grid: { vertLines: { color: "#333" }, horzLines: { color: "#333" } },
      timeScale: { timeVisible: true, secondsVisible: true },
      autoSize: true,
    });

    const series = chart.addSeries(CandlestickSeries, {
      upColor: "#4caf50",
      downColor: "#e57373",
      borderVisible: false,
      wickUpColor: "#4caf50",
      wickDownColor: "#e57373",
    });

    chartRef.current = chart;
    seriesRef.current = series;

    return () => {
      chart.remove();
      chartRef.current = null;
      seriesRef.current = null;
    };
  }, []);

  useEffect(() => {
    // lightweight-charts requires strictly ascending, non-duplicate `time` values.
    seriesRef.current?.setData(candles.map((c) => ({ ...c, time: c.time as UTCTimestamp })));
  }, [candles]);

  return <div ref={containerRef} style={{ width: "100%", height: "100%" }} />;
}
