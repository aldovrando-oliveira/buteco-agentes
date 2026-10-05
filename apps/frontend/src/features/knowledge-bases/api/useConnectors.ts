import { useMutation, useQuery } from '@tanstack/react-query';
import {
  listConnectorFolders,
  listConnectorProviders,
  requestKnowledgeBaseSync,
} from './connectorsApi';

// `retry: false` nas duas consultas, e não nos defaults: o QueryClient do app
// (app/queryClient.ts) não tem `defaultOptions`, então valeriam três novas
// tentativas com espera crescente, e o seletor ficaria segundos "carregando" um
// erro que já chegou. O operador tem "Tentar de novo" e "Recarregar"
// (frontend-cadastro-base-sincronizada, D2).

// `enabled` é obrigatório: quem chama decide quando o apps/connectors pode ser
// consultado (só com a origem Sincronizada marcada e o endereço configurado).
export function useConnectorProvidersQuery(options: { enabled: boolean }) {
  return useQuery({
    queryKey: ['connectors', 'providers'],
    queryFn: listConnectorProviders,
    enabled: options.enabled,
    retry: false,
  });
}

// Uma consulta por nível: `parentId` nulo é o nível de cima.
export function useConnectorFoldersQuery(
  providerKey: string | null,
  parentId: string | null,
  options: { enabled: boolean },
) {
  return useQuery({
    queryKey: ['connectors', providerKey, 'folders', parentId],
    queryFn: () => listConnectorFolders(providerKey as string, parentId),
    enabled: options.enabled && providerKey !== null,
    retry: false,
  });
}

// "Sincronizar agora". Sem `retry`: mutações não repetem por padrão, e uma nova
// tentativa automática dispararia um segundo pedido sem o operador saber. O
// acompanhamento do ciclo é da página (frontend-detalhe-base-sincronizada, D5).
export function useRequestKnowledgeBaseSyncMutation() {
  return useMutation({
    mutationFn: (knowledgeBaseId: string) => requestKnowledgeBaseSync(knowledgeBaseId),
  });
}
