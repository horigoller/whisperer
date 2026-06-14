import { useEffect, useState, type ReactNode } from "react";
import { NavLink, Navigate, Route, Routes } from "react-router-dom";
import { useAuth } from "./auth";
import { Login } from "./pages/Login";
import { Inbox } from "./pages/Inbox";
import { Conversation } from "./pages/Conversation";
import { Contacts } from "./pages/Contacts";
import { Users } from "./pages/Users";

const NARROW = "(max-width: 760px)";
const isNarrow = () => window.matchMedia(NARROW).matches;

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

  return <Shell />;
}

function Shell() {
  // Remember the user's choice; default to the collapsed rail on small screens.
  const [collapsed, setCollapsed] = useState<boolean>(() => {
    const saved = localStorage.getItem("ww.sidebar");
    return saved != null ? saved === "1" : isNarrow();
  });

  // Auto-collapse to the rail when shrinking to mobile, auto-expand when growing back.
  useEffect(() => {
    const mq = window.matchMedia(NARROW);
    const onChange = (e: MediaQueryListEvent) => setCollapsed(e.matches);
    mq.addEventListener("change", onChange);
    return () => mq.removeEventListener("change", onChange);
  }, []);

  const toggle = () =>
    setCollapsed((c) => {
      const next = !c;
      localStorage.setItem("ww.sidebar", next ? "1" : "0");
      return next;
    });

  // On mobile an expanded sidebar overlays the content — collapse it back after navigating.
  const closeOnNarrow = () => { if (isNarrow()) setCollapsed(true); };

  return (
    <div className={`layout${collapsed ? " collapsed" : ""}`}>
      <Sidebar onNavigate={closeOnNarrow} onToggle={toggle} collapsed={collapsed} />
      <div className="sidebar-backdrop" onClick={toggle} />
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

function Icon({ children }: { children: ReactNode }) {
  return (
    <svg className="ic" viewBox="0 0 24 24" width="20" height="20" fill="none"
      stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      {children}
    </svg>
  );
}

function Sidebar({ onNavigate, onToggle, collapsed }: { onNavigate: () => void; onToggle: () => void; collapsed: boolean }) {
  const { user, logout } = useAuth();
  return (
    <nav className="sidebar">
      <button className="rail-toggle" onClick={onToggle} title={collapsed ? "Expand" : "Collapse"}
        aria-label={collapsed ? "Expand menu" : "Collapse menu"} aria-expanded={!collapsed}>
        <svg className="ic chevron" viewBox="0 0 24 24" width="18" height="18" fill="none"
          stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <path d="M15 18l-6-6 6-6" />
        </svg>
      </button>

      <div className="brand">
        <img className="brand-logo" src="/icon.svg" alt="" width={28} height={28} />
        <span className="label">Whisperer</span>
      </div>

      <NavLink to="/" end onClick={onNavigate} title="Inbox">
        <Icon><path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" /></Icon>
        <span className="label">Inbox</span>
      </NavLink>
      <NavLink to="/contacts" onClick={onNavigate} title="Contacts">
        <Icon><circle cx="12" cy="8" r="4" /><path d="M4 20c0-3.5 3.6-6 8-6s8 2.5 8 6" /></Icon>
        <span className="label">Contacts</span>
      </NavLink>
      {user?.role === "Admin" && (
        <NavLink to="/users" onClick={onNavigate} title="System users">
          <Icon>
            <circle cx="9" cy="8" r="3.2" /><path d="M3 19c0-3.1 2.7-5 6-5s6 1.9 6 5" />
            <path d="M16 5.2a3.2 3.2 0 0 1 0 6.4" /><path d="M21.5 19c0-2.5-1.5-4.1-3.8-4.7" />
          </Icon>
          <span className="label">System users</span>
        </NavLink>
      )}

      <div className="spacer" />
      <div className="me"><span className="label">{user?.displayName} <span className="role">{user?.role}</span></span></div>
      <button className="link logout" onClick={logout} title="Log out">
        <Icon><path d="M16 17l5-5-5-5" /><path d="M21 12H9" /><path d="M9 3H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h4" /></Icon>
        <span className="label">Log out</span>
      </button>
    </nav>
  );
}
