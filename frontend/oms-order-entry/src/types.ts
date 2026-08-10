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

export interface L2Row {
  side: "BID" | "ASK";
  level: number;
  price: number;
  shares: number;
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
