import { useCallback, useEffect, useRef, useState } from "react";
import { connectWatchlist, type WatchlistConnection } from "./solaceClient";
import type { L2Snapshot, WatchlistRow } from "./types";

const SOLACE_URL = "ws://localhost:8008";
const SOLACE_VPN = "default";
const SOLACE_USERNAME = "default";
const SOLACE_PASSWORD = "";
const TOPIC_PREFIX = "md/l2/nasdaq/";
// Bumped to v2 when the default list changed from 5 hand-picked tickers to the real top 20 -
// anyone with an old saved list under the v1 key gets the new default instead of silently keeping
// stale data that no longer matches what the default is supposed to be.
const STORAGE_KEY = "boulevard.watchlist.tickers.v2";

// Matches the backend's own publish cadence (Edge.MarketData sweeps every 250ms) - no point
// redrawing the grid more often than the data can actually change.
const REFRESH_INTERVAL_MS = 250;

// Every MFE polls on the same 250ms cadence, but with no coordination between them their
// independent setInterval timers drift and periodically land within a few ms of each other -
// when that happens, every open view's renderer process wants to re-render at the same instant,
// spiking CPU demand across all of them simultaneously instead of spreading it out. A fixed phase
// offset (distinct per app) keeps this view's tick roughly out-of-phase with the others regardless
// of drift, so simultaneous-MFE rendering load stays spread across the 250ms window rather than
// clustering. This app fires on-cadence (offset 0); see useActiveSymbolFeed.ts for its counterpart.
const REFRESH_PHASE_OFFSET_MS = 0;

// The 20 most active tickers (by total ITCH message count) across the six pcap files the demo
// publisher actually replays (T133000-T142000, i.e. --minutes 60 from the market-open capture) -
// measured directly from the capture data, not guessed. Most-active first.
const DEFAULT_TICKERS = [
  "SPY",
  "QQQ",
  "TSLA",
  "GOOG",
  "GOOGL",
  "AAPL",
  "NVDA",
  "IWM",
  "IVV",
  "TQQQ",
  "VOO",
  "AMD",
  "AMZN",
  "SMH",
  "SPXL",
  "XLY",
  "MSFT",
  "SOXL",
  "XLK",
  "DIA",
];

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

    let interval: ReturnType<typeof setInterval> | undefined;
    const startTimer = setTimeout(() => {
      interval = setInterval(() => {
        setRows(tickersRef.current.map((ticker) => toRow(ticker, snapshotsRef.current.get(ticker))));
      }, REFRESH_INTERVAL_MS);
    }, REFRESH_PHASE_OFFSET_MS);

    return () => {
      clearTimeout(startTimer);
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
