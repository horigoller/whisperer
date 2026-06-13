import { NavLink, Navigate, Route, Routes } from "react-router-dom";
import { useAuth } from "./auth";
import { Login } from "./pages/Login";
import { Inbox } from "./pages/Inbox";
import { Conversation } from "./pages/Conversation";
import { Contacts } from "./pages/Contacts";
import { Users } from "./pages/Users";

export function App() {
  const { user, loading } = useAuth();

  if (loading) return <div className="centered">Loading…</div>;
  if (!user) {
    return (
      <Routes>
        <Route path="/login" element={<Login />} />
        <Route path="*" element={<Navigate to="/login" replace />} />
      </Routes>
    );
  }

  return (
    <div className="layout">
      <Sidebar />
      <main className="content">
        <Routes>
          <Route path="/" element={<Inbox />} />
          <Route path="/c/:waId" element={<Conversation />} />
          <Route path="/contacts" element={<Contacts />} />
          <Route path="/users" element={<Users />} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </main>
    </div>
  );
}

function Sidebar() {
  const { user, logout } = useAuth();
  return (
    <nav className="sidebar">
      <div className="brand">Goller's Whisperer</div>
      <NavLink to="/" end>Inbox</NavLink>
      <NavLink to="/contacts">Contacts</NavLink>
      {user?.role === "Admin" && <NavLink to="/users">System users</NavLink>}
      <div className="spacer" />
      <div className="me">{user?.displayName} <span className="role">{user?.role}</span></div>
      <button className="link" onClick={logout}>Log out</button>
    </nav>
  );
}
