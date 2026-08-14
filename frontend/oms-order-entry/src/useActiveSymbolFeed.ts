import { useEffect, useRef, useState } from "react";
import { connectSymbolFeed, type SymbolFeedConnection } from "./solaceClient";
import type { DepthRow, L2Snapshot } from "./types";

const SOLACE_URL = "ws://localhost:8008";
const SOLACE_VPN = "default";
const SOLACE_USERNAME = "default";
const SOLACE_PASSWORD = "";
const TOPIC_PREFIX = "md/l2/nasdaq/";

// Matches the backend's own publish cadence (Edge.MarketData sweeps every 250ms).
const REFRESH_INTERVAL_MS = 250;

// See useWatchlist.ts for why this exists: every MFE polls on the same 250ms cadence, and without
// a fixed phase offset their independent timers drift into firing at the same instant, spiking
// every open view's render work simultaneously. 125ms (half the period) keeps this app maximally
// out-of-phase with watchlist-mfe's offset-0 timer regardless of drift.
const REFRESH_PHASE_OFFSET_MS = 125;

function toDepthRows(snapshot: L2Snapshot | null): DepthRow[] {
  if (!snapshot) {
    return [];
  }

  const depth = Math.max(snapshot.Bids.length, snapshot.Asks.length);
  const rows: DepthRow[] = [];
  for (let i = 0; i < depth; i++) {
    const bid = snapshot.Bids[i];
    const ask = snapshot.Asks[i];
    rows.push({
      level: i + 1,
      bidShares: bid?.Shares ?? null,
      bidPrice: bid?.Price ?? null,
      askPrice: ask?.Price ?? null,
      askShares: ask?.Shares ?? null,
    });
  }

  return rows;
}

/** L2 depth for exactly one symbol at a time - re-subscribes whenever the active ticker changes. */
export function useActiveSymbolFeed(ticker: string | null) {
  const [rows, setRows] = useState<DepthRow[]>([]);
  const [snapshot, setSnapshot] = useState<L2Snapshot | null>(null);
  const [status, setStatus] = useState("connecting");
  const snapshotRef = useRef<L2Snapshot | null>(null);
  const connectionRef = useRef<SymbolFeedConnection | null>(null);
  const tickerRef = useRef(ticker);
  tickerRef.current = ticker;

  useEffect(() => {
    const connection = connectSymbolFeed(
      { url: SOLACE_URL, vpnName: SOLACE_VPN, userName: SOLACE_USERNAME, password: SOLACE_PASSWORD, topicPrefix: TOPIC_PREFIX },
      (snap) => {
        // A subscription change can leave one stale message from the previous ticker in flight -
        // drop anything that doesn't match the ticker we currently care about.
        if (snap.Ticker === tickerRef.current) {
          snapshotRef.current = snap;
        }
      },
      setStatus,
    );
    connectionRef.current = connection;
    connection.setTickers(tickerRef.current ? [tickerRef.current] : []);

    let interval: ReturnType<typeof setInterval> | undefined;
    const startTimer = setTimeout(() => {
      interval = setInterval(() => {
        setRows(toDepthRows(snapshotRef.current));
        setSnapshot(snapshotRef.current);
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
    snapshotRef.current = null;
    setSnapshot(null);
    setRows([]);
    connectionRef.current?.setTickers(ticker ? [ticker] : []);
  }, [ticker]);

  return { rows, snapshot, status };
}
