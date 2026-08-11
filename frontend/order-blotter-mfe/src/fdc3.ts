import { getAgent, type DesktopAgent, type Context } from "@finos/fdc3";
import type { OrderLogEntry } from "./types";

let agentPromise: Promise<DesktopAgent> | null = null;

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

/**
 * Custom (non-standard) FDC3 context type for a submitted order - matches the type broadcast by
 * oms-order-entry. FDC3 has no built-in "order" context, so this is Boulevard's own namespaced type.
 */
export const ORDER_CONTEXT_TYPE = "blvd.order";

export interface OrderContext extends Context {
  type: typeof ORDER_CONTEXT_TYPE;
  order: OrderLogEntry;
}
