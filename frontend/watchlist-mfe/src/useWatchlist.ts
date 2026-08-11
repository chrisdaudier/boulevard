import { useCallback, useEffect, useRef, useState } from "react";
import { connectWatchlist, type WatchlistConnection } from "./solaceClient";
import type { L2Snapshot, WatchlistRow } from "./types";

const SOLACE_URL = "ws://localhost:8008";
const SOLACE_VPN = "default";
const SOLACE_USERNAME = "default";
const SOLACE_PASSWORD = "";
const TOPIC_PREFIX = "md/l2/nasdaq/";
const STORAGE_KEY = "boulevard.watchlist.tickers";

// Matches the backend's own publish cadence (Edge.MarketData sweeps every 250ms) - no point
// redrawing the grid more often than the data can actually change.
const REFRESH_INTERVAL_MS = 250;

const DEFAULT_TICKERS = ["AAPL", "MSFT", "NVDA", "GOOG", "AMZN"];

function loadStoredTickers(): string[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (raw === null) {
      return DEFAULT_TICKERS; // first-ever load, nothing saved yet
    }

    const parsed = JSON.parse(raw) as unknown;
    return Array.isArray(parsed) ? parsed.filter((t): t is string => typeof t === "string") : DEFAULT_TICKERS;
  } catch {
    return DEFAULT_TICKERS;
  }
}

function toRow(ticker: string, snapshot: L2Snapshot | undefined): WatchlistRow {
  const bestBid = snapshot?.Bids[0];
  const bestAsk = snapshot?.Asks[0];

  return {
    ticker,
    bidPrice: bestBid?.Price ?? null,
    bidShares: bestBid?.Shares ?? null,
    askPrice: bestAsk?.Price ?? null,
    askShares: bestAsk?.Shares ?? null,
    spread: bestBid && bestAsk ? bestAsk.Price - bestBid.Price : null,
    updatedUtc: snapshot?.TimestampUtc ?? null,
  };
}

export function useWatchlist() {
  const [tickers, setTickers] = useState<string[]>(loadStoredTickers);
  const [rows, setRows] = useState<WatchlistRow[]>([]);
  const [status, setStatus] = useState("connecting");
  const snapshotsRef = useRef(new Map<string, L2Snapshot>());
  const connectionRef = useRef<WatchlistConnection | null>(null);
  const tickersRef = useRef(tickers);
  tickersRef.current = tickers;

  useEffect(() => {
    const connection = connectWatchlist(
      { url: SOLACE_URL, vpnName: SOLACE_VPN, userName: SOLACE_USERNAME, password: SOLACE_PASSWORD, topicPrefix: TOPIC_PREFIX },
      (snapshot) => snapshotsRef.current.set(snapshot.Ticker, snapshot),
      setStatus,
    );
    connectionRef.current = connection;
    connection.setTickers(tickersRef.current);

    const interval = setInterval(() => {
      setRows(tickersRef.current.map((ticker) => toRow(ticker, snapshotsRef.current.get(ticker))));
    }, REFRESH_INTERVAL_MS);

    return () => {
      clearInterval(interval);
      connection.disconnect();
      connectionRef.current = null;
    };
  }, []);

  useEffect(() => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(tickers));
    connectionRef.current?.setTickers(tickers);
    setRows(tickers.map((ticker) => toRow(ticker, snapshotsRef.current.get(ticker))));
  }, [tickers]);

  const addTicker = useCallback((rawTicker: string) => {
    const ticker = rawTicker.trim().toUpperCase();
    if (!ticker) {
      return;
    }

    setTickers((current) => (current.includes(ticker) ? current : [...current, ticker]));
  }, []);

  const removeTicker = useCallback((ticker: string) => {
    snapshotsRef.current.delete(ticker);
    setTickers((current) => current.filter((t) => t !== ticker));
  }, []);

  return { rows, status, addTicker, removeTicker };
}
