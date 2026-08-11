import { useEffect, useState } from "react";
import { getFdc3Agent, ORDER_CONTEXT_TYPE, type OrderContext } from "./fdc3";
import type { OrderLogEntry } from "./types";

/** Accumulates every order broadcast received over FDC3 - this MFE has no state of its own otherwise. */
export function useOrderBlotter() {
  const [orders, setOrders] = useState<OrderLogEntry[]>([]);

  useEffect(() => {
    let cancelled = false;
    let listener: { unsubscribe: () => void } | undefined;

    getFdc3Agent()
      .then(async (fdc3) => {
        if (cancelled) {
          return;
        }

        listener = await fdc3.addContextListener(ORDER_CONTEXT_TYPE, (context) => {
          const order = (context as OrderContext).order;
          if (order) {
            setOrders((current) => [order, ...current]);
          }
        });
      })
      .catch(() => {
        // No desktop agent connected - nothing to listen to standalone.
      });

    return () => {
      cancelled = true;
      listener?.unsubscribe();
    };
  }, []);

  return orders;
}
