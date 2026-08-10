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

export interface WatchlistRow {
  ticker: string;
  bidPrice: number | null;
  bidShares: number | null;
  askPrice: number | null;
  askShares: number | null;
  spread: number | null;
  updatedUtc: string | null;
}
