import solace from "solclientjs";
import type { L2Snapshot } from "./types";

const factoryProps = new solace.SolclientFactoryProperties();
factoryProps.profile = solace.SolclientFactoryProfiles.version10;
solace.SolclientFactory.init(factoryProps);

export interface SolaceConnectionOptions {
  url: string;
  vpnName: string;
  userName: string;
  password: string;
  topicPrefix: string;
}

export interface WatchlistConnection {
  /** Replaces the full set of subscribed tickers, diffing against what's currently subscribed. */
  setTickers: (tickers: string[]) => void;
  disconnect: () => void;
}

/**
 * Connects to Solace PubSub+ over its native Web Messaging API (WebSocket) and keeps topic
 * subscriptions in sync with a caller-supplied ticker list. Unlike the L2 viewer (which
 * subscribes once to the full "md/l2/nasdaq/>" wildcard), a watchlist only cares about a handful
 * of symbols - Boulevard.Edge.SolaceGateway already publishes each snapshot to its own per-ticker
 * topic, so subscribing per-symbol means traffic scales with the watchlist, not with every ticker
 * the backend happens to be tracking.
 */
export function connectWatchlist(
  options: SolaceConnectionOptions,
  onSnapshot: (snapshot: L2Snapshot) => void,
  onStatusChange: (status: string) => void,
): WatchlistConnection {
  const session = solace.SolclientFactory.createSession({
    url: options.url,
    vpnName: options.vpnName,
    userName: options.userName,
    password: options.password,
  });

  let subscribed = new Set<string>();
  let desired = new Set<string>();
  let connected = false;

  const topicFor = (ticker: string) => options.topicPrefix + ticker;

  function reconcile() {
    if (!connected) {
      return;
    }

    for (const ticker of desired) {
      if (!subscribed.has(ticker)) {
        session.subscribe(solace.SolclientFactory.createTopicDestination(topicFor(ticker)), true, topicFor(ticker), 10000);
      }
    }

    for (const ticker of subscribed) {
      if (!desired.has(ticker)) {
        session.unsubscribe(solace.SolclientFactory.createTopicDestination(topicFor(ticker)), true, topicFor(ticker), 10000);
      }
    }

    subscribed = new Set(desired);
  }

  session.on(solace.SessionEventCode.UP_NOTICE, () => {
    connected = true;
    onStatusChange("connected");
    subscribed = new Set(); // fresh session (first connect or reconnect) - nothing is subscribed yet
    reconcile();
  });

  session.on(solace.SessionEventCode.CONNECT_FAILED_ERROR, () => onStatusChange("connect-failed"));
  session.on(solace.SessionEventCode.DISCONNECTED, () => {
    connected = false;
    onStatusChange("disconnected");
  });

  session.on(solace.SessionEventCode.MESSAGE, (message: solace.Message) => {
    const attachment = message.getBinaryAttachment();
    if (!attachment) {
      return;
    }

    const text = typeof attachment === "string" ? attachment : new TextDecoder().decode(attachment);

    try {
      onSnapshot(JSON.parse(text) as L2Snapshot);
    } catch {
      // Ignore malformed payloads - the next snapshot (250ms later) supersedes it anyway.
    }
  });

  session.connect();

  return {
    setTickers(tickers: string[]) {
      desired = new Set(tickers);
      reconcile();
    },
    disconnect() {
      session.disconnect();
    },
  };
}
