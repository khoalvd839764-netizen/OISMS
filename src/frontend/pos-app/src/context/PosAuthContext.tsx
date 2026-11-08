import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react';
import { clearPosSession, getPosAccessToken, getPosGeneration, getPosMe, loginPos, logoutPos } from '../services/posSession';

interface User { id: string; tenantId: string; name: string; email: string; tenantName: string; role: string; }
const normalize = (u: { id: string; tenantId: string; fullName?: string; email: string; tenantName?: string; roles: string[] }): User => ({
  id: u.id, tenantId: u.tenantId, name: u.fullName || u.email, email: u.email, tenantName: u.tenantName || '', role: u.roles[0],
});
const Context = createContext<{ user: User | null; loading: boolean; error: string | null; retry: () => void; login: (email: string, password: string) => Promise<void>; logout: () => Promise<void> } | null>(null);

export function PosAuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);
  useEffect(() => {
    let active = true;
    const expected = getPosGeneration();
    if (!getPosAccessToken()) { setLoading(false); return; }
    setLoading(true); setError(null);
    getPosMe().then(u => { if (active && expected === getPosGeneration()) setUser(normalize(u)); })
      .catch(e => { if (active && expected === getPosGeneration()) setError(e.message); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [attempt]);
  useEffect(() => {
    const ended = () => { setUser(null); setError(null); setLoading(false); };
    window.addEventListener('pos-auth-expired', ended);
    window.addEventListener('pos-session-ended', ended);
    return () => { window.removeEventListener('pos-auth-expired', ended); window.removeEventListener('pos-session-ended', ended); };
  }, []);
  const login = useCallback(async (email: string, password: string) => { setUser(null); setError(null); const u = await loginPos(email, password); setUser(normalize(u)); }, []);
  const logout = useCallback(async () => { setUser(null); setError(null); await logoutPos(); }, []);
  return <Context.Provider value={{ user, loading, error, retry: () => { if (!getPosAccessToken()) clearPosSession(); setAttempt(n => n + 1); }, login, logout }}>{children}</Context.Provider>;
}
export function useAuth() { const value = useContext(Context); if (!value) throw new Error('Thiếu PosAuthProvider.'); return value; }
