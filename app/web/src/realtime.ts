import type { ChatMessage } from "./api";
import { getToken } from "./api";

export interface RealtimeEvent {
  type: "message" | "status";
  waId: string;
  message?: ChatMessage;
  messageId?: string;
  status?: string;
  errorCode?: number | null;
  errorDetail?: string | null;
}

/**
 * Opens a reconnecting WebSocket to the realtime API and invokes onEvent for each pushed event.
 * Returns a disposer. Auth is the session JWT passed as ?token= (browsers can't set WS headers).
 */
export function connectRealtime(wsUrl: string, onEvent: (e: RealtimeEvent) => void): () => void {
  let socket: WebSocket | null = null;
  let closed = false;
  let attempt = 0;
  let reconnectTimer: ReturnType<typeof setTimeout> | undefined;

  const open = () => {
    if (closed) return;
    const token = getToken();
    if (!token) return; // not logged in
    socket = new WebSocket(`${wsUrl}?token=${encodeURIComponent(token)}`);

    socket.onopen = () => { attempt = 0; };
    socket.onmessage = (ev) => {
      try { onEvent(JSON.parse(ev.data) as RealtimeEvent); } catch { /* ignore non-JSON */ }
    };
    socket.onclose = () => {
      if (closed) return;
      const delay = Math.min(1000 * 2 ** attempt++, 30000); // capped exponential backoff
      reconnectTimer = setTimeout(open, delay);
    };
    socket.onerror = () => socket?.close();
  };

  open();
  return () => {
    closed = true;
    if (reconnectTimer) clearTimeout(reconnectTimer);
    socket?.close();
  };
}
