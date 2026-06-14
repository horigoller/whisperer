import { useEffect, useState } from "react";
import { api, ApiError } from "../api";
import { useAuth } from "../auth";

export function Login() {
  const { login } = useAuth();
  const [step, setStep] = useState<"username" | "code">("username");
  const [username, setUsername] = useState("");
  const [challengeId, setChallengeId] = useState("");
  const [code, setCode] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [deliveryError, setDeliveryError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  // On the code step, poll whether the code actually delivered (async failures like 131037).
  useEffect(() => {
    if (step !== "code" || !challengeId) return;
    let active = true;
    let tries = 0;
    const check = async () => {
      try {
        const d = await api.codeDelivery(challengeId);
        if (active && d.failed) {
          setDeliveryError(`Couldn't deliver your code${d.errorCode ? ` (${d.errorCode})` : ""}: ${d.errorDetail ?? "WhatsApp rejected the message."}`);
          return; // stop polling once we know it failed
        }
      } catch { /* ignore transient errors */ }
      if (active && ++tries < 8) setTimeout(check, 3000);
    };
    check();
    return () => { active = false; };
  }, [step, challengeId]);

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
            <button type="button" className="link" onClick={() => { setStep("username"); setDeliveryError(null); }}>Back</button>
            {deliveryError && <div className="error">{deliveryError}</div>}
          </>
        )}
        {error && <div className="error">{error}</div>}
      </form>
    </div>
  );
}
