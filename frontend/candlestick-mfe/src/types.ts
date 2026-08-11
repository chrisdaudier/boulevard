export interface PriceLevel {
  Price: number;
  Shares: number;
}

export interface L2Snapshot {
  Ticker: string;
  TimestampUtc: string;
  Bids: PriceLevel[];
  Asks: PriceLevel[];
}

/** A single OHLC bar, keyed by its bucket start time (Unix seconds, UTC) - matches lightweight-charts' `time` field. */
export interface Candle {
  time: number;
  open: number;
  high: number;
  low: number;
  close: number;
}
