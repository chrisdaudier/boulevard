import { getAgent, type DesktopAgent, type Instrument, type Context } from "@finos/fdc3";
import type { OrderLogEntry } from "./types";

let agentPromise: Promise<DesktopAgent> | null = null;

/**
 * Custom (non-standard) FDC3 context type for a submitted order - FDC3 has no built-in "order"
 * context, so this is Boulevard's own namespaced type, broadcast by oms-order-entry and consumed
 * by order-blotter-mfe. Any FDC3-compliant app on the same channel can still choose to listen for
 * it even though it isn't part of the base spec.
 */
export const ORDER_CONTEXT_TYPE = "blvd.order";

export interface OrderContext extends Context {
  type: typeof ORDER_CONTEXT_TYPE;
  order: OrderLogEntry;
}

export function orderToContext(order: OrderLogEntry): OrderContext {
  return { type: ORDER_CONTEXT_TYPE, order };
}

/**
 * Resolves once to the FDC3 DesktopAgent, whether injected by a container (OpenFin, HERE/interop.io,
 * or any other FDC3-compliant desktop) or unavailable (e.g. running standalone in a plain browser
 * tab during development) - callers should always .catch() this, since "no agent" is an expected,
 * non-error outcome outside a container. channelSelector/intentResolver are turned off because each
 * MFE implements its own channel-selector UI rather than relying on a container-provided one.
 */
export function getFdc3Agent(): Promise<DesktopAgent> {
  agentPromise ??= getAgent({ timeoutMs: 2000, channelSelector: false, intentResolver: false });
  return agentPromise;
}

export function tickerToInstrument(ticker: string): Instrument {
  return { type: "fdc3.instrument", id: { ticker } };
}
