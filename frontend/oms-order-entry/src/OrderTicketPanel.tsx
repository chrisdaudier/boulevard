import { useEffect, useState } from "react";
import type { L2Snapshot, OrderLogEntry, OrderSide, OrderType } from "./types";

interface OrderTicketPanelProps {
  ticker: string | null;
  snapshot: L2Snapshot | null;
}

/**
 * UI-only mock order ticket - there is no OMS/exchange backend in this system, so "submitting"
 * just validates the ticket and appends a client-side accepted order to the log below it.
 */
export function OrderTicketPanel({ ticker, snapshot }: OrderTicketPanelProps) {
  const [side, setSide] = useState<OrderSide>("BUY");
  const [type, setType] = useState<OrderType>("LIMIT");
  const [quantity, setQuantity] = useState("");
  const [price, setPrice] = useState("");
  const [priceTouched, setPriceTouched] = useState(false);
  const [log, setLog] = useState<OrderLogEntry[]>([]);

  const bestBid = snapshot?.Bids[0]?.Price ?? null;
  const bestAsk = snapshot?.Asks[0]?.Price ?? null;

  // Prefill a sensible limit price (the side of the book you'd cross to fill immediately) whenever
  // the side changes or a fresh BBO arrives - but only until the trader edits it by hand.
  useEffect(() => {
    if (priceTouched || type !== "LIMIT") {
      return;
    }

    const defaultPrice = side === "BUY" ? bestAsk : bestBid;
    if (defaultPrice != null) {
      setPrice(defaultPrice.toFixed(4));
    }
  }, [side, type, bestBid, bestAsk, priceTouched]);

  function handleSideChange(nextSide: OrderSide) {
    setSide(nextSide);
    setPriceTouched(false);
  }

  function handleTickerOrTypeReset() {
    setPriceTouched(false);
  }

  const quantityNumber = Number(quantity);
  const priceNumber = Number(price);
  const isQuantityValid = quantity.trim() !== "" && quantityNumber > 0;
  const isPriceValid = type === "MARKET" || (price.trim() !== "" && priceNumber > 0);
  const canSubmit = ticker != null && isQuantityValid && isPriceValid;

  function handleSubmit() {
    if (!canSubmit || !ticker) {
      return;
    }

    const entry: OrderLogEntry = {
      orderId: crypto.randomUUID().slice(0, 8),
      submittedAt: new Date().toISOString(),
      ticker,
      side,
      type,
      quantity: quantityNumber,
      price: type === "LIMIT" ? priceNumber : null,
      status: "ACCEPTED",
    };

    setLog((current) => [entry, ...current]);
    setQuantity("");
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 12, height: "100%" }}>
      <div style={{ border: "1px solid #444", borderRadius: 4, padding: 12 }}>
        <h3 style={{ margin: "0 0 12px" }}>Order Ticket</h3>
        <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
          <label style={{ display: "flex", flexDirection: "column", gap: 4 }}>
            Ticker
            <input value={ticker ?? ""} disabled style={{ padding: "4px 8px", fontFamily: "monospace" }} />
          </label>

          <div style={{ display: "flex", gap: 8 }}>
            <button
              onClick={() => handleSideChange("BUY")}
              style={{
                flex: 1,
                padding: "6px 0",
                cursor: "pointer",
                background: side === "BUY" ? "#1c6e3f" : "#333",
                color: "#e0e0e0",
                border: "1px solid #444",
              }}
            >
              Buy
            </button>
            <button
              onClick={() => handleSideChange("SELL")}
              style={{
                flex: 1,
                padding: "6px 0",
                cursor: "pointer",
                background: side === "SELL" ? "#8c2f2f" : "#333",
                color: "#e0e0e0",
                border: "1px solid #444",
              }}
            >
              Sell
            </button>
          </div>

          <label style={{ display: "flex", flexDirection: "column", gap: 4 }}>
            Type
            <select
              value={type}
              onChange={(e) => {
                setType(e.target.value as OrderType);
                handleTickerOrTypeReset();
              }}
              style={{ padding: "4px 8px" }}
            >
              <option value="LIMIT">Limit</option>
              <option value="MARKET">Market</option>
            </select>
          </label>

          <label style={{ display: "flex", flexDirection: "column", gap: 4 }}>
            Quantity
            <input
              type="number"
              min={1}
              value={quantity}
              onChange={(e) => setQuantity(e.target.value)}
              style={{ padding: "4px 8px" }}
            />
          </label>

          <label style={{ display: "flex", flexDirection: "column", gap: 4 }}>
            Price
            <input
              type="number"
              min={0}
              step="0.0001"
              value={price}
              disabled={type === "MARKET"}
              onChange={(e) => {
                setPrice(e.target.value);
                setPriceTouched(true);
              }}
              style={{ padding: "4px 8px" }}
            />
          </label>

          <button
            onClick={handleSubmit}
            disabled={!canSubmit}
            style={{
              padding: "8px 0",
              marginTop: 4,
              cursor: canSubmit ? "pointer" : "not-allowed",
              background: canSubmit ? "#2d6ca8" : "#333",
              color: "#e0e0e0",
              border: "1px solid #444",
            }}
          >
            Submit {side === "BUY" ? "Buy" : "Sell"} Order
          </button>
          {!ticker && <p style={{ color: "#888", fontSize: 12, margin: 0 }}>Select a ticker to enable order entry.</p>}
        </div>
      </div>

      <div style={{ border: "1px solid #444", borderRadius: 4, padding: 12, flex: 1, overflowY: "auto" }}>
        <h3 style={{ margin: "0 0 12px" }}>Order Log</h3>
        {log.length === 0 ? (
          <p style={{ color: "#888", fontSize: 12 }}>No orders submitted yet.</p>
        ) : (
          <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12, fontFamily: "monospace" }}>
            <thead>
              <tr style={{ textAlign: "left", color: "#888" }}>
                <th>Time</th>
                <th>Ticker</th>
                <th>Side</th>
                <th>Type</th>
                <th>Qty</th>
                <th>Price</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              {log.map((entry) => (
                <tr key={entry.orderId}>
                  <td>{new Date(entry.submittedAt).toLocaleTimeString()}</td>
                  <td>{entry.ticker}</td>
                  <td>{entry.side}</td>
                  <td>{entry.type}</td>
                  <td>{entry.quantity.toLocaleString()}</td>
                  <td>{entry.price?.toFixed(4) ?? "MKT"}</td>
                  <td style={{ color: entry.status === "ACCEPTED" ? "#4caf50" : "#e57373" }}>{entry.status}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
