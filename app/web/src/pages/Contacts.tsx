import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { api, type Contact } from "../api";

export function Contacts() {
  const [contacts, setContacts] = useState<Contact[]>([]);
  const [name, setName] = useState("");
  const [phone, setPhone] = useState("");
  const [error, setError] = useState<string | null>(null);
  const nav = useNavigate();

  const load = () => api.contacts().then((r) => setContacts(r.contacts)).catch(() => {});
  useEffect(() => { load(); }, []);

  async function add(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    try {
      const { contact } = await api.addContact(name.trim(), phone.trim());
      setName(""); setPhone("");
      await load();
      nav(`/c/${contact.waId}`);
    } catch {
      setError("Could not add contact. Check the phone number.");
    }
  }

  return (
    <div className="list">
      <h2 className="pad">Contacts</h2>
      <form className="inline-form pad" onSubmit={add}>
        <input placeholder="Name (optional)" value={name} onChange={(e) => setName(e.target.value)} />
        <input placeholder="+15551234567" value={phone} onChange={(e) => setPhone(e.target.value)} />
        <button disabled={!phone.trim()}>Add contact</button>
      </form>
      {error && <div className="error pad">{error}</div>}
      {contacts.map((c) => (
        <div key={c.waId} className="row" onClick={() => nav(`/c/${c.waId}`)} style={{ cursor: "pointer" }}>
          <div className="row-main">
            <div className="row-title">{c.name ?? `+${c.waId}`}</div>
            <div className="row-sub">{c.phoneE164}</div>
          </div>
          <span className="muted">{c.source}</span>
        </div>
      ))}
    </div>
  );
}
