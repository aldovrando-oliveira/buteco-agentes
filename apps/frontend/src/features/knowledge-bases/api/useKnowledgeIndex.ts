import { useQuery } from '@tanstack/react-query';
import { listKnowledgeIndexDiagnostics } from './knowledgeIndexApi';
import { shouldPollIndexDiagnostics } from '../utils/indexDiagnostics';
import type { KnowledgeDocumentSummary } from '../types/knowledgeDocument';
import type { KnowledgeIndexProvenance } from '../types/knowledgeIndex';

// CHAVE PRÓPRIA, SEM O PREFIXO DAS BASES, e isso é escolha.
//
// `['knowledge-bases', ...]` faria toda mutação de base invalidar esta consulta
// de graça — e seria errado: criar, editar, ativar ou desativar uma base não
// muda um único fragmento gravado. O que muda a proveniência é a indexação
// terminar, e disso quem cuida é o acompanhamento abaixo.
export const indexDiagnosticsQueryKey = ['knowledge-index', 'diagnostics'] as const;

// Mesmo intervalo da listagem de documentos, e de propósito: as duas consultas
// esperam pelo MESMO evento — o primeiro documento a terminar de indexar.
// Intervalos diferentes fariam a tela mostrar as duas metades do mesmo fato em
// momentos diferentes.
const POLL_INTERVAL_MS = 4000;

// A CONSULTA SÓ EXISTE COM A ABA ATIVA, e o motivo é mais caro que o da 5b.
//
// A rota é `Seq Scan` + `HashAggregate` sobre o heap inteiro de
// `knowledge_fragments` — 0,24–0,82 s com 150.000 fragmentos, medido na VM do
// podman com `shared_buffers` no default de 128 MB (as páginas valem em qualquer
// lugar; os milissegundos, só naquele disco). Buscar isso ao abrir o detalhe
// cobraria a varredura de quem só quer ver a lista de documentos.
//
// O `keepMounted={false}` da barra de abas é o que sustenta esse `enabled`: a
// aba inativa não existe no DOM, então não há observador vivo nem intervalo
// armado por trás dela.
//
// LIMITE DECLARADO, não escondido: um documento que indexe em OUTRA base não
// acorda esta aba, porque a página só conhece os documentos desta. Não é lacuna
// a consertar aqui — `app/queryClient.ts` cria o `QueryClient` sem
// `defaultOptions`, então valem `staleTime: 0` e `refetchOnMount: true`, e
// remontar a página refaz a consulta. A alternativa (acompanhar sempre) compraria
// esse caso pagando a varredura para sempre.
export function useKnowledgeIndexDiagnosticsQuery(options: {
  enabled: boolean;
  documents: KnowledgeDocumentSummary[] | undefined;
}) {
  return useQuery({
    queryKey: indexDiagnosticsQueryKey,
    queryFn: listKnowledgeIndexDiagnostics,
    enabled: options.enabled,
    // A condição mora em função pura exportada, e não numa expressão aqui, pelos
    // dois motivos da convenção 20: é testável sem timer, e o guarda que a
    // protege é pareado — determinístico sobre a opção real, comportamental
    // sobre o efeito.
    refetchInterval: (query) =>
      shouldPollIndexDiagnostics(
        query.state.data as KnowledgeIndexProvenance[] | undefined,
        options.documents,
      )
        ? POLL_INTERVAL_MS
        : false,
  });
}
