import { useEffect, useState } from "react";
import { api, ApiError, type SystemUserRow } from "../api";
import { useAuth } from "../auth";

type Draft = { username: string; displayName: string; phoneE164: string; role: string };
const empty: Draft = { username: "", displayName: "", phoneE164: "", role: "agent" };

export function Users() {
  const { user: me } = useAuth();
  const [users, setUsers] = useState<SystemUserRow[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [editing, setEditing] = useState<{ mode: "add" | "edit"; draft: Draft } | null>(null);

  const load = () => api.users().then((r) => setUsers(r.users)).catch(() => setError("Could not load users."));
  useEffect(() => { load(); }, []);

  async function remove(username: string) {
    if (!confirm(`Remove ${username}? This can't be undone.`)) return;
    setError(null);
    try { await api.deleteUser(username); await load(); }
    catch { setError(`Could not remove ${username}.`); }
  }

  return (
    <div className="list">
      <div className="page-head pad">
        <h2>System users</h2>
        <button className="primary" onClick={() => setEditing({ mode: "add", draft: { ...empty } })}>Add user</button>
      </div>
      {error && <div className="error pad">{error}</div>}

      {users.map((u) => (
        <div key={u.username} className="row">
          <div className="row-main">
            <div className="row-title">
              {u.displayName} <span className="role">{u.role}</span>
              {u.username === me?.username && <span className="role you">you</span>}
            </div>
            <div className="row-sub">{u.username} · {u.phoneE164}</div>
          </div>
          <div className="row-actions">
            <button className="link" onClick={() => setEditing({ mode: "edit", draft: {
              username: u.username, displayName: u.displayName, phoneE164: u.phoneE164, role: u.role,
            } })}>Edit</button>
            <button
              className="link danger"
              disabled={u.username === me?.username}
              title={u.username === me?.username ? "You can't delete yourself" : undefined}
              onClick={() => remove(u.username)}
            >Delete</button>
          </div>
        </div>
      ))}
      {users.length === 0 && !error && <div className="muted pad">No users yet.</div>}

      {editing && (
        <UserDialog
          mode={editing.mode}
          initial={editing.draft}
          onClose={() => setEditing(null)}
          onSaved={() => { setEditing(null); load(); }}
        />
      )}
    </div>
  );
}

function UserDialog({ mode, initial, onClose, onSaved }: {
  mode: "add" | "edit"; initial: Draft; onClose: () => void; onSaved: () => void;
}) {
  const [draft, setDraft] = useState<Draft>(initial);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function save(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true); setError(null);
    try {
      if (mode === "add") await api.addUser(draft);
      else await api.updateUser(draft.username, { displayName: draft.displayName, phoneE164: draft.phoneE164, role: draft.role });
      onSaved();
    } catch (err) {
      setError(err instanceof ApiError && err.status === 400 ? "Check the phone number (use +E.164)." : "Could not save user.");
    } finally {
      setBusy(false);
    }
  }

  const valid = draft.username.trim() && draft.phoneE164.trim();

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <form className="card modal" onClick={(e) => e.stopPropagation()} onSubmit={save}>
        <h3>{mode === "add" ? "Add user" : `Edit ${draft.username}`}</h3>
        <label>Username
          <input
            autoFocus={mode === "add"} placeholder="agent1" value={draft.username}
            disabled={mode === "edit"}
            onChange={(e) => setDraft({ ...draft, username: e.target.value })}
          />
        </label>
        <label>Display name
          <input placeholder="Agent One" value={draft.displayName}
            onChange={(e) => setDraft({ ...draft, displayName: e.target.value })} />
        </label>
        <label>Phone (E.164)
          <input placeholder="+15551234567" value={draft.phoneE164}
            onChange={(e) => setDraft({ ...draft, phoneE164: e.target.value })} />
        </label>
        <label>Role
          <select value={draft.role} onChange={(e) => setDraft({ ...draft, role: e.target.value })}>
            <option value="agent">agent</option>
            <option value="admin">admin</option>
          </select>
        </label>
        {error && <div className="error">{error}</div>}
        <button disabled={busy || !valid}>{mode === "add" ? "Add user" : "Save changes"}</button>
        <button type="button" className="link" onClick={onClose}>Cancel</button>
      </form>
    </div>
  );
}
