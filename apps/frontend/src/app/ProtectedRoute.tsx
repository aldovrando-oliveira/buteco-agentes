import { Navigate, Outlet, useLocation } from 'react-router';
import { useSession } from '../auth/session';

// Sem o registro da sessão do emissor, a rota vai ao /entrar, guardando a pedida como
// `voltarPara` (spec `operator-login-ui`). O registro é do navegador, e não da aba:
// recarregar e abrir outra aba não pedem login, e quando outra aba sai, esta reage
// pelo evento `storage` (design.md da change painel-entra-pelo-emissor, D2 e D3).
export function ProtectedRoute() {
  const session = useSession();
  const location = useLocation();

  if (session === null) {
    const voltarPara = encodeURIComponent(`${location.pathname}${location.search}`);
    return <Navigate to={`/entrar?voltarPara=${voltarPara}`} replace />;
  }

  return <Outlet />;
}
