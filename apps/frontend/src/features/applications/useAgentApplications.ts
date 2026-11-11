import { useQuery, type UseQueryResult } from '@tanstack/react-query';
import { usePapel } from '../../auth/session';
import { permissoes } from '../../auth/roles';
import { countAgentApplications, listAgentApplications } from './api/applicationsApi';
import { agentApplicationsKey } from './queryKeys';
import type { AgentApplication, AgentApplicationsCount } from './types';

// As consultas do card "Aplicações com acesso" (spec `panel-external-application-agents`). A ROTA
// SAI DO PAPEL DA SESSÃO, antes de qualquer chamada: o Admin lê a lista com nomes; Operador e
// Leitor leem só a contagem e nunca chamam a lista (#187, Q8). O `AgentDetailPage` chama uma vez e
// passa o resultado ao card e ao modal de desativar, que mostram o mesmo N.

export interface AgentApplicationsState {
  admin: boolean;
  lista: UseQueryResult<AgentApplication[]>;
  contagem: UseQueryResult<AgentApplicationsCount>;
  /** As aplicações ativas com acesso; `null` enquanto não há resposta, ou com falha. */
  ativas: number | null;
}

/** As duas consultas do card, uma só habilitada pelo papel; o modal de desativar lê o mesmo `ativas`. */
export function useAgentApplications(agentId: string): AgentApplicationsState {
  const admin = permissoes(usePapel()).gerirAcesso;
  const lista = useQuery({
    queryKey: agentApplicationsKey(agentId, 'lista'),
    queryFn: () => listAgentApplications(agentId),
    enabled: admin,
  });
  const contagem = useQuery({
    queryKey: agentApplicationsKey(agentId, 'contagem'),
    queryFn: () => countAgentApplications(agentId),
    enabled: !admin,
  });
  const ativas = admin
    ? lista.data
      ? lista.data.filter((app) => app.ativa).length
      : null
    : (contagem.data?.ativas ?? null);
  return { admin, lista, contagem, ativas };
}
