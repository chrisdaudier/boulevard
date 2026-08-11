export type OrderSide = "BUY" | "SELL";
export type OrderType = "MARKET" | "LIMIT";

export interface OrderLogEntry {
  orderId: string;
  submittedAt: string;
  ticker: string;
  side: OrderSide;
  type: OrderType;
  quantity: number;
  price: number | null;
  status: "ACCEPTED" | "REJECTED";
  rejectReason?: string;
}
