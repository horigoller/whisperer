import { useCallback, useEffect, useRef, useState } from "react";
import { useParams } from "react-router-dom";
import { api, ApiError, type ChatMessage, type Conversation as Conv, type ApprovedTemplate } from "../api";
import { clockTime, windowOpen } from "../format";

export function Conversation() {
  const { waId = "" } = useParams();
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [conv, setConv] = useState<Conv | null>(null);
  const [text, setText] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [showTemplate, setShowTemplate] = useState(false);
  const endRef = useRef<HTMLDivElement>(null);
  const lastTsRef = useRef("");

  useEffect(() => { lastTsRef.current = messages.length ? messages[messages.length - 1].createdAt : ""; }, [messages]);

  // Full reload (initial open, after sending, switching contact): also resets unread.
  const load = useCallback(async () => {
    const r = await api.thread(waId);
    setMessages(r.messages);
    setConv(r.conversation);
  }, [waId]);

  // Incremental poll: only fetch messages newer than the last one we have, then append (dedup).
  const poll = useCallback(async () => {
    const after = lastTsRef.current;
    if (!after) { await load(); return; }
    const r = await api.threadSince(waId, after);
    if (r.messages.length === 0) return;
    setMessages((prev) => {
      const seen = new Set(prev.map((m) => m.id));
      const merged = prev.concat(r.messages.filter((m) => !seen.has(m.id)));
      merged.sort((a, b) => a.createdAt.localeCompare(b.createdAt));
      return merged;
    });
  }, [waId, load]);

  useEffect(() => {
    setMessages([]);
    load().catch(() => {});
    const t = setInterval(() => poll().catch(() => {}), 6000);
    return () => clearInterval(t);
  }, [waId, load, poll]);

  useEffect(() => { endRef.current?.scrollIntoView({ behavior: "smooth" }); }, [messages.length]);

  const open = windowOpen(conv?.windowExpiresAt ?? null);

  async function send(e: React.FormEvent) {
    e.preventDefault();
    if (!text.trim()) return;
    setError(null);
    try {
      await api.reply(waId, text.trim());
      setText("");
      await load();
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) setError("The 24h window is closed. Start a template exchange.");
      else setError("Failed to send.");
    }
  }

  return (
    <div className="thread">
      <header className="thread-head">
        <div>{conv?.name ?? `+${waId}`}</div>
        <span className={open ? "pill open" : "pill closed"}>{open ? "window open" : "window closed"}</span>
      </header>

      <div className="messages">
        {messages.map((m) => (
          <div key={m.id} className={`bubble ${m.direction}`}>
            <div className="bubble-text">{m.text ?? (m.mediaId ? `[${m.type}]` : "—")}</div>
            <div className="bubble-meta">
              {clockTime(m.createdAt)}
              {m.direction === "out" && (
                <span className={m.status === "failed" ? "status failed" : "status"}> · {m.status}</span>
              )}
            </div>
            {m.status === "failed" && (m.errorDetail || m.errorCode) && (
              <div className="bubble-error" title={m.errorDetail ?? ""}>
                ⚠ {m.errorCode ? `${m.errorCode}: ` : ""}{m.errorDetail ?? "delivery failed"}
              </div>
            )}
          </div>
        ))}
        <div ref={endRef} />
      </div>

      {error && <div className="error pad">{error}</div>}

      {open ? (
        <form className="composer" onSubmit={send}>
          <input placeholder="Type a reply…" value={text} onChange={(e) => setText(e.target.value)} />
          <button disabled={!text.trim()}>Send</button>
        </form>
      ) : (
        <div className="composer closed-note">
          <span>Outside the 24h window — replies are blocked.</span>
          <button onClick={() => setShowTemplate(true)}>Start template exchange</button>
        </div>
      )}

      {showTemplate && <TemplateDialog waId={waId} onClose={() => setShowTemplate(false)} onSent={() => { setShowTemplate(false); load(); }} />}
    </div>
  );
}

function TemplateDialog({ waId, onClose, onSent }: { waId: string; onClose: () => void; onSent: () => void }) {
  const [templates, setTemplates] = useState<ApprovedTemplate[]>([]);
  const [name, setName] = useState("");
  const [language, setLanguage] = useState("en_US");
  const [params, setParams] = useState("");
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api.templates().then((r) => {
      setTemplates(r.templates);
      if (r.templates[0]) { setName(r.templates[0].name); setLanguage(r.templates[0].language ?? "en_US"); }
    }).catch(() => {});
  }, []);

  async function send(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    try {
      const p = params.split(",").map((s) => s.trim()).filter(Boolean);
      await api.sendTemplate(waId, name, language, p);
      onSent();
    } catch {
      setError("Failed to send template.");
    }
  }

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <form className="card modal" onClick={(e) => e.stopPropagation()} onSubmit={send}>
        <h3>Start template exchange</h3>
        {templates.length === 0 ? (
          <p className="muted">No approved templates yet. Create and approve one in WhatsApp Manager.</p>
        ) : (
          <>
            <label>Template
              <select value={name} onChange={(e) => {
                setName(e.target.value);
                const t = templates.find((x) => x.name === e.target.value);
                if (t?.language) setLanguage(t.language);
              }}>
                {templates.map((t) => <option key={t.name} value={t.name}>{t.name} ({t.category})</option>)}
              </select>
            </label>
            <label>Language <input value={language} onChange={(e) => setLanguage(e.target.value)} /></label>
            <label>Body params (comma-separated) <input value={params} onChange={(e) => setParams(e.target.value)} placeholder="Hori" /></label>
            <button>Send</button>
          </>
        )}
        <button type="button" className="link" onClick={onClose}>Cancel</button>
        {error && <div className="error">{error}</div>}
      </form>
    </div>
  );
}
