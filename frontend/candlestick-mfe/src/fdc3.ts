import { getAgent, type DesktopAgent } from "@finos/fdc3";

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
