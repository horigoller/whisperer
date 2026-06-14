import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import { api, clearToken, getToken, setToken, type SessionUser } from "./api";

interface AuthState {
  user: SessionUser | null;
  wsUrl: string | null;
  loading: boolean;
  login: (token: string, user: SessionUser, wsUrl: string) => void;
  logout: () => void;
}

const AuthContext = createContext<AuthState | undefined>(undefined);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<SessionUser | null>(null);
  const [wsUrl, setWsUrl] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (!getToken()) { setLoading(false); return; }
    api.me()
      .then((r) => { setUser(r.user); setWsUrl(r.wsUrl || null); })
      .catch(() => clearToken())
      .finally(() => setLoading(false));
  }, []);

  const login = (token: string, u: SessionUser, ws: string) => { setToken(token); setUser(u); setWsUrl(ws || null); };
  const logout = () => { api.logout().catch(() => {}); clearToken(); setUser(null); setWsUrl(null); };

  return <AuthContext.Provider value={{ user, wsUrl, loading, login, logout }}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used within AuthProvider");
  return ctx;
}
