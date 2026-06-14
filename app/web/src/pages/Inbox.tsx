import { useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { api, type Conversation } from "../api";
import { windowOpen, since } from "../format";
import { useAuth } from "../auth";
import { connectRealtime } from "../realtime";

type Filter = "all" | "unread" | "open";

export function Inbox() {
  const [items, setItems] = useState<Conversation[] | null>(null);
  const [query, setQuery] = useState("");
  const [filter, setFilter] = useState<Filter>("all");
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

  const unreadCount = useMemo(() => (items ?? []).filter((c) => c.unread > 0).length, [items]);

  // Filter client-side: the conversation list is small and already loaded.
  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    return (items ?? []).filter((c) => {
      if (filter === "unread" && !(c.unread > 0)) return false;
      if (filter === "open" && !windowOpen(c.windowExpiresAt)) return false;
      if (!q) return true;
      return `${c.name ?? ""} ${c.waId} ${c.lastPreview ?? ""}`.toLowerCase().includes(q);
    });
  }, [items, query, filter]);

  if (!items) return <div className="muted pad">Loading conversations…</div>;

  return (
    <div className="list">
      <div className="inbox-head">
        <h2>Inbox</h2>
        <div className="search">
          <input
            type="search"
            placeholder="Search name, number or message…"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            aria-label="Search conversations"
          />
        </div>
        <div className="chips">
          <button className={`chip${filter === "all" ? " on" : ""}`} onClick={() => setFilter("all")}>All</button>
          <button className={`chip${filter === "unread" ? " on" : ""}`} onClick={() => setFilter("unread")}>
            Unread{unreadCount > 0 && <span className="chip-count">{unreadCount}</span>}
          </button>
          <button className={`chip${filter === "open" ? " on" : ""}`} onClick={() => setFilter("open")}>Open window</button>
        </div>
      </div>

      {items.length === 0 ? (
        <div className="muted pad">No conversations yet. A customer messaging your number will appear here.</div>
      ) : filtered.length === 0 ? (
        <div className="muted pad">No conversations match your search.</div>
      ) : (
        filtered.map((c) => (
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
        ))
      )}
    </div>
  );
}
