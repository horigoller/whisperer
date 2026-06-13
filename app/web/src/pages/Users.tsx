import { useEffect, useState } from "react";
import { api } from "../api";

interface UserRow { username: string; displayName: string; phoneE164?: string; role: string; }

export function Users() {
  const [users, setUsers] = useState<UserRow[]>([]);
  const [form, setForm] = useState({ username: "", displayName: "", phoneE164: "", role: "agent" });
  const [error, setError] = useState<string | null>(null);

  const load = () => api.users().then((r) => setUsers(r.users as unknown as UserRow[])).catch(() => {});
  useEffect(() => { load(); }, []);

  async function add(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    try {
      await api.addUser(form);
      setForm({ username: "", displayName: "", phoneE164: "", role: "agent" });
      await load();
    } catch {
      setError("Could not add user.");
    }
  }

  async function remove(username: string) {
    if (!confirm(`Remove ${username}?`)) return;
    try { await api.deleteUser(username); await load(); } catch { setError("Could not remove user."); }
  }

  return (
    <div className="list">
      <h2 className="pad">System users</h2>
      <form className="inline-form pad" onSubmit={add}>
        <input placeholder="username" value={form.username} onChange={(e) => setForm({ ...form, username: e.target.value })} />
        <input placeholder="Display name" value={form.displayName} onChange={(e) => setForm({ ...form, displayName: e.target.value })} />
        <input placeholder="+15551234567" value={form.phoneE164} onChange={(e) => setForm({ ...form, phoneE164: e.target.value })} />
        <select value={form.role} onChange={(e) => setForm({ ...form, role: e.target.value })}>
          <option value="agent">agent</option>
          <option value="admin">admin</option>
        </select>
        <button disabled={!form.username.trim() || !form.phoneE164.trim()}>Add user</button>
      </form>
      {error && <div className="error pad">{error}</div>}
      {users.map((u) => (
        <div key={u.username} className="row">
          <div className="row-main">
            <div className="row-title">{u.displayName} <span className="role">{u.role}</span></div>
            <div className="row-sub">{u.username} · {u.phoneE164 ?? ""}</div>
          </div>
          <button className="link danger" onClick={() => remove(u.username)}>Remove</button>
        </div>
      ))}
    </div>
  );
}
