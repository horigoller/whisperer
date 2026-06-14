import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api, type Conversation } from "../api";
import { windowOpen, since } from "../format";
import { useAuth } from "../auth";
import { connectRealtime } from "../realtime";

export function Inbox() {
  const [items, setItems] = useState<Conversation[] | null>(null);
  const { wsUrl } = useAuth();

  useEffect(() => {
    let active = true;
    const load = () => api.conversations().then((r) => active && setItems(r.conversations)).catch(() => {});
    load();

    // Real-time: any message/status event refreshes the (cheap) inbox list.
    const stop = wsUrl ? connectRealtime(wsUrl, () => load()) : undefined;
    // Safety net in case a push is missed.
    const t = setInterval(load, 30000);
    return () => { active = false; clearInterval(t); stop?.(); };
  }, [wsUrl]);

  if (!items) return <div className="muted pad">Loading conversations…</div>;
  if (items.length === 0) return <div className="muted pad">No conversations yet. A customer messaging your number will appear here.</div>;

  return (
    <div className="list">
      <h2 className="pad">Inbox</h2>
      {items.map((c) => (
        <Link key={c.waId} to={`/c/${c.waId}`} className="row">
          <div className="row-main">
            <div className="row-title">
              {c.name ?? `+${c.waId}`}
              {c.unread > 0 && <span className="badge">{c.unread}</span>}
            </div>
            <div className="row-sub">{c.lastPreview ?? "—"}</div>
          </div>
          <div className="row-meta">
            <span className={windowOpen(c.windowExpiresAt) ? "pill open" : "pill closed"}>
              {windowOpen(c.windowExpiresAt) ? "open" : "closed"}
            </span>
            <span className="muted">{since(c.lastActivityAt)}</span>
          </div>
        </Link>
      ))}
    </div>
  );
}
