// Typed API client. Stores the session token in localStorage and sends it as a Bearer header.

export interface SessionUser { username: string; displayName: string; role: string; }
export interface Conversation {
  waId: string; name: string | null; lastPreview: string | null;
  lastActivityAt: string; windowExpiresAt: string | null; unread: number;
}
export interface ChatMessage {
  waId: string; id: string; direction: "in" | "out"; type: string;
  text: string | null; mediaId: string | null; status: string;
  sentBy: string | null; templateName: string | null;
  errorCode: number | null; errorDetail: string | null; createdAt: string;
}
export interface Contact { waId: string; phoneE164: string; name: string | null; source: string; }
export interface ApprovedTemplate { name: string; language: string | null; category: string | null; }

const TOKEN_KEY = "ww.session";
export const getToken = () => localStorage.getItem(TOKEN_KEY);
export const setToken = (t: string) => localStorage.setItem(TOKEN_KEY, t);
export const clearToken = () => localStorage.removeItem(TOKEN_KEY);

export class ApiError extends Error {
  constructor(public status: number, public body: any) {
    super(body?.error ?? `HTTP ${status}`);
  }
}

async function req<T>(path: string, init: RequestInit = {}): Promise<T> {
  const token = getToken();
  const res = await fetch(`/api${path}`, {
    ...init,
    headers: {
      "content-type": "application/json",
      ...(token ? { authorization: `Bearer ${token}` } : {}),
      ...(init.headers ?? {}),
    },
  });
  const body = res.status === 204 ? null : await res.json().catch(() => null);
  if (!res.ok) throw new ApiError(res.status, body);
  return body as T;
}

export const api = {
  // auth
  startLogin: (username: string) => req<{ challengeId: string }>("/auth/start", { method: "POST", body: JSON.stringify({ username }) }),
  verify: (challengeId: string, code: string) =>
    req<{ token: string; user: SessionUser; wsUrl: string }>("/auth/verify", { method: "POST", body: JSON.stringify({ challengeId, code }) }),
  codeDelivery: (challengeId: string) =>
    req<{ failed: boolean; errorCode: number | null; errorDetail: string | null }>(
      `/auth/delivery?challengeId=${encodeURIComponent(challengeId)}`),
  me: () => req<{ user: SessionUser; wsUrl: string }>("/auth/me"),
  logout: () => req<{ ok: boolean }>("/auth/logout", { method: "POST" }),

  // conversations
  conversations: () => req<{ conversations: Conversation[] }>("/conversations"),
  thread: (waId: string) => req<{ messages: ChatMessage[]; conversation: Conversation | null }>(`/conversations/${waId}/messages`),
  threadSince: (waId: string, after: string) =>
    req<{ messages: ChatMessage[] }>(`/conversations/${waId}/messages?after=${encodeURIComponent(after)}`),
  reply: (waId: string, text: string) => req<{ message: ChatMessage }>(`/conversations/${waId}/reply`, { method: "POST", body: JSON.stringify({ text }) }),
  sendTemplate: (waId: string, templateName: string, languageCode: string, params: string[]) =>
    req<{ message: ChatMessage }>(`/conversations/${waId}/template`, { method: "POST", body: JSON.stringify({ templateName, languageCode, params }) }),

  // contacts
  contacts: () => req<{ contacts: Contact[] }>("/contacts"),
  addContact: (name: string, phoneE164: string) => req<{ contact: Contact }>("/contacts", { method: "POST", body: JSON.stringify({ name, phoneE164 }) }),

  // users
  users: () => req<{ users: SessionUser[] & { phoneE164?: string }[] }>("/users"),
  addUser: (u: { username: string; displayName: string; phoneE164: string; role: string }) =>
    req<{ user: unknown }>("/users", { method: "POST", body: JSON.stringify(u) }),
  deleteUser: (username: string) => req<{ ok: boolean }>(`/users/${username}`, { method: "DELETE" }),

  // templates
  templates: () => req<{ templates: ApprovedTemplate[] }>("/templates"),
};
