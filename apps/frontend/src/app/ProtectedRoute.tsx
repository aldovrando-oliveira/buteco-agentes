import { Center, Group, Loader, Text } from '@mantine/core';
import { Navigate, Outlet, useLocation } from 'react-router';
import { useLeaving, useSession } from '../auth/session';

// Sem o registro da sessão do emissor, a rota vai ao /entrar, guardando a pedida como
// `voltarPara` (spec `operator-login-ui`). O registro é do navegador, e não da aba:
// recarregar e abrir outra aba não pedem login, e quando outra aba sai, esta reage
// pelo evento `storage` (design.md da change painel-entra-pelo-emissor, D2 e D3).
//
// Exceção: o logout desta aba. O registro sai antes de a navegação ao /connect/logout
// descarregar a página; nesse intervalo a rota mostra só "Saindo…", sem ir ao /entrar
// interno, que aparecia por um instante e consumia o aviso "Senha alterada." antes do
// /entrar de verdade (design.md da change minha-conta-painel, D3; decisão do dono, opção 3).
export function ProtectedRoute() {
  const session = useSession();
  const saindo = useLeaving();
  const location = useLocation();

  if (session === null) {
    if (saindo) {
      return (
        <Center h="100vh" role="status" aria-live="polite">
          <Group gap="sm">
            <Loader size="sm" />
            <Text>Saindo…</Text>
          </Group>
        </Center>
      );
    }
    const voltarPara = encodeURIComponent(`${location.pathname}${location.search}`);
    return <Navigate to={`/entrar?voltarPara=${voltarPara}`} replace />;
  }

  return <Outlet />;
}
