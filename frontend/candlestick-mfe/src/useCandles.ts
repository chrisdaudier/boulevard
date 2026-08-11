import { useEffect, useRef, useState } from "react";
import { connectSymbolFeed, type SymbolFeedConnection } from "./solaceClient";
import type { Candle, L2Snapshot } from "./types";

const SOLACE_URL = "ws://localhost:8008";
const SOLACE_VPN = "default";
const SOLACE_USERNAME = "default";
const SOLACE_PASSWORD = "";
const TOPIC_PREFIX = "md/l2/nasdaq/";

// Bounded so a long-running demo doesn't grow the chart's data set forever.
const MAX_CANDLES = 500;

/**
 * Builds live OHLC candles from the BBO midpoint of the active ticker's L2 snapshots.
 * Boulevard doesn't distribute individual trade prints to the frontend, so this is a
 * midpoint-derived proxy for a real trade-based candle, not a substitute for one - it moves with
 * the quoted market, not with executions.
 */
export function useCandles(ticker: string | null, intervalSeconds: number) {
  const [candles, setCandles] = useState<Candle[]>([]);
  const [status, setStatus] = useState("connecting");
  const candlesRef = useRef<Candle[]>([]);
  const connectionRef = useRef<SymbolFeedConnection | null>(null);
  const tickerRef = useRef(ticker);
  tickerRef.current = ticker;
  const intervalRef = useRef(intervalSeconds);
  intervalRef.current = intervalSeconds;

  useEffect(() => {
    const connection = connectSymbolFeed(
      { url: SOLACE_URL, vpnName: SOLACE_VPN, userName: SOLACE_USERNAME, password: SOLACE_PASSWORD, topicPrefix: TOPIC_PREFIX },
      (snap: L2Snapshot) => {
        if (snap.Ticker !== tickerRef.current) {
          return;
        }

        const bestBid = snap.Bids[0]?.Price;
        const bestAsk = snap.Asks[0]?.Price;
        if (bestBid == null || bestAsk == null) {
          return;
        }

        const midpoint = (bestBid + bestAsk) / 2;
        const bucketSeconds = intervalRef.current;
        const bucketTime = Math.floor(new Date(snap.TimestampUtc).getTime() / 1000 / bucketSeconds) * bucketSeconds;

        const current = candlesRef.current;
        const last = current[current.length - 1];

        if (last && last.time === bucketTime) {
          current[current.length - 1] = {
            ...last,
            high: Math.max(last.high, midpoint),
            low: Math.min(last.low, midpoint),
            close: midpoint,
          };
        } else {
          current.push({ time: bucketTime, open: midpoint, high: midpoint, low: midpoint, close: midpoint });
          if (current.length > MAX_CANDLES) {
            current.shift();
          }
        }

        setCandles([...current]);
      },
      setStatus,
    );
    connectionRef.current = connection;
    connection.setTickers(tickerRef.current ? [tickerRef.current] : []);

    return () => {
      connection.disconnect();
      connectionRef.current = null;
    };
  }, []);

  useEffect(() => {
    candlesRef.current = [];
    setCandles([]);
    connectionRef.current?.setTickers(ticker ? [ticker] : []);
  }, [ticker]);

  useEffect(() => {
    candlesRef.current = [];
    setCandles([]);
  }, [intervalSeconds]);

  return { candles, status };
}
