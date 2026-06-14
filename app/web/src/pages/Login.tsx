import { useState } from "react";
import { api, ApiError } from "../api";
import { useAuth } from "../auth";

export function Login() {
  const { login } = useAuth();
  const [step, setStep] = useState<"username" | "code">("username");
  const [username, setUsername] = useState("");
  const [challengeId, setChallengeId] = useState("");
  const [code, setCode] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function start(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true); setError(null);
    try {
      const { challengeId } = await api.startLogin(username.trim());
      setChallengeId(challengeId);
      setStep("code");
    } catch {
      setError("Could not start login. Try again.");
    } finally {
      setBusy(false);
    }
  }

  async function verify(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true); setError(null);
    try {
      const { token, user } = await api.verify(challengeId, code.trim());
      login(token, user);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Verification failed.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="centered">
      <form className="card login" onSubmit={step === "username" ? start : verify}>
        <h1>Whisperer</h1>
        {step === "username" ? (
          <>
            <p>Enter your username. We'll send a one-time code to your WhatsApp.</p>
            <input autoFocus placeholder="username" value={username} onChange={(e) => setUsername(e.target.value)} />
            <button disabled={busy || !username.trim()}>Send code</button>
          </>
        ) : (
          <>
            <p>Enter the 6-digit code sent to your WhatsApp.</p>
            <input autoFocus inputMode="numeric" placeholder="123456" value={code} onChange={(e) => setCode(e.target.value)} />
            <button disabled={busy || code.trim().length < 6}>Verify</button>
            <button type="button" className="link" onClick={() => setStep("username")}>Back</button>
          </>
        )}
        {error && <div className="error">{error}</div>}
      </form>
    </div>
  );
}
