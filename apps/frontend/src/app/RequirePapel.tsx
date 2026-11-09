import { Outlet } from 'react-router';
import { usePapel } from '../auth/session';
import type { Papel } from '../auth/roles';
import { AccessDenied } from '../components/feedback/AccessDenied';

// A guarda de rota por papel (spec `panel-roles`, "Guarda de rota por papel"; design.md da
// change papeis-no-painel, D3, decisão do dono Q3). Com o papel do id token fora da lista, a
// rota mostra a tela de acesso negado sem montar a página, e por isso sem chamar o servidor.
//
// Ela NÃO substitui a recusa do servidor, que continua sendo a autoridade: com o papel na
// lista, a página carrega, e um `403` dela cai no mesmo estado pela carga. A guarda existe só
// para não carregar uma página que o token já diz que será recusada. O papel desconhecido não
// está em nenhuma lista (vale Leitor, D1).

export function RequirePapel({ papeis }: { papeis: readonly Papel[] }) {
  const papel = usePapel();
  if (papel === null || !papeis.includes(papel)) return <AccessDenied />;
  return <Outlet />;
}
