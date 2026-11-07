import { useEffect, useState } from 'react';

/** O botão volta quando passa o `Retry-After`; nada é reenviado sozinho (como no /entrar). */
export function useBlockedUntil() {
  const [until, setUntil] = useState<number | null>(null);
  useEffect(() => {
    if (until === null) return;
    const timer = setTimeout(() => setUntil(null), Math.max(0, until - Date.now()));
    return () => clearTimeout(timer);
  }, [until]);
  return [until !== null, setUntil] as const;
}
