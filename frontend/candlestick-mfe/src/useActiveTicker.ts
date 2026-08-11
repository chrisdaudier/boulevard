import { useCallback, useEffect, useState } from "react";
import type { Instrument } from "@finos/fdc3";
import { getFdc3Agent } from "./fdc3";

export type ActiveTickerSource = "fdc3" | "manual" | "default";

const DEFAULT_TICKER = "AAPL";

/**
 * The "currently active" ticker for this MFE instance - either the last symbol broadcast by
 * another MFE (e.g. a Watchlist row click) via FDC3's fdc3.instrument context, a manually-typed
 * override (works standalone with no desktop agent connected at all), or DEFAULT_TICKER until
 * either of those occurs.
 */
export function useActiveTicker() {
  const [ticker, setTicker] = useState<string | null>(DEFAULT_TICKER);
  const [source, setSource] = useState<ActiveTickerSource | null>("default");

  useEffect(() => {
    let cancelled = false;
    let listener: { unsubscribe: () => void } | undefined;

    getFdc3Agent()
      .then(async (fdc3) => {
        if (cancelled) {
          return;
        }

        listener = await fdc3.addContextListener("fdc3.instrument", (context) => {
          const instrumentTicker = (context as Instrument).id?.ticker;
          if (instrumentTicker) {
            setTicker(instrumentTicker.toUpperCase());
            setSource("fdc3");
          }
        });
      })
      .catch(() => {
        // No desktop agent connected - manual entry remains the only way to set the active ticker.
      });

    return () => {
      cancelled = true;
      listener?.unsubscribe();
    };
  }, []);

  const setManualTicker = useCallback((rawTicker: string) => {
    const next = rawTicker.trim().toUpperCase();
    if (next) {
      setTicker(next);
      setSource("manual");
    }
  }, []);

  return { ticker, source, setManualTicker };
}
