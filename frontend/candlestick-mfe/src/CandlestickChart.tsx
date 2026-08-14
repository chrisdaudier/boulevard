import { useEffect, useRef } from "react";
import { createChart, CandlestickSeries, type IChartApi, type ISeriesApi, type UTCTimestamp } from "lightweight-charts";
import type { Candle } from "./types";

interface CandlestickChartProps {
  candles: Candle[];
}

function toChartCandle(candle: Candle) {
  return { ...candle, time: candle.time as UTCTimestamp };
}

/**
 * Thin imperative wrapper around lightweight-charts - the library owns its own canvas rendering,
 * so this pushes data into it directly rather than letting React re-render the chart itself.
 *
 * setData() replaces and redraws the *entire* dataset - calling it on every tick (up to 4/second,
 * against a growing up-to-500-candle history) was a real, avoidable rendering cost repeated
 * forever. update() is what lightweight-charts is actually designed for here: it appends a new
 * bar, or patches the existing last bar in place if its `time` is unchanged (exactly the "same
 * bucket, price moved" case from useCandles) - full setData() is only needed once, when the
 * dataset is reset for a new ticker/interval.
 */
export function CandlestickChart({ candles }: CandlestickChartProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const chartRef = useRef<IChartApi | null>(null);
  const seriesRef = useRef<ISeriesApi<"Candlestick"> | null>(null);
  const previousLengthRef = useRef(0);

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
    const series = seriesRef.current;
    if (!series) {
      return;
    }

    if (candles.length === 0) {
      series.setData([]);
    } else if (candles.length < previousLengthRef.current) {
      // A reset (ticker/interval change) always passes through 0 first (see useCandles), so this
      // is just a defensive fallback, not the expected path.
      series.setData(candles.map(toChartCandle));
    } else {
      series.update(toChartCandle(candles[candles.length - 1]));
    }

    previousLengthRef.current = candles.length;
  }, [candles]);

  return <div ref={containerRef} style={{ width: "100%", height: "100%" }} />;
}
