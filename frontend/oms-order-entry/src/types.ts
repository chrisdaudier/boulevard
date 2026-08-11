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

/** One row of a side-by-side depth ladder: bid columns on the left, ask columns on the right. */
export interface DepthRow {
  level: number;
  bidShares: number | null;
  bidPrice: number | null;
  askPrice: number | null;
  askShares: number | null;
}

export type OrderSide = "BUY" | "SELL";
export type OrderType = "MARKET" | "LIMIT";

export interface OrderTicket {
  ticker: string;
  side: OrderSide;
  type: OrderType;
  quantity: number;
  price: number | null;
}

export interface OrderLogEntry extends OrderTicket {
  orderId: string;
  submittedAt: string;
  status: "ACCEPTED" | "REJECTED";
  rejectReason?: string;
}
