import { hasNonTerminalDocument } from './documentIndexing';
import type { KnowledgeDocumentSummary } from '../types/knowledgeDocument';
import type { KnowledgeIndexProvenance } from '../types/knowledgeIndex';

// O ÍNDICE ESTÁ VAZIO É FATO CONHECIDO, NÃO DADO DESCONHECIDO.
//
// A rota responde 200 com lista vazia quando não há fragmento nenhum gravado.
// Isso é uma medição que aconteceu — diferente do travessão desta área do painel,
// que significa "não sei" (ver utils/indexingSummary.ts, a gramática de três
// estados). Quem renderiza o vazio explica; nunca preenche as linhas com
// travessão, e nunca exibe qual modelo SERIA usado, que é configuração de outro
// processo (design.md, D8 e D9).
export function isIndexEmpty(diagnostics: KnowledgeIndexProvenance[] | undefined): boolean {
  return diagnostics !== undefined && diagnostics.length === 0;
}

// Mais de uma combinação é resposta VÁLIDA — 200, dois itens —, e é o estado em
// que esta tela é aberta: `apps/workers` recusa o boot com o índice assim, e
// `apps/api` continua de pé.
export function isIndexCorrupted(diagnostics: KnowledgeIndexProvenance[] | undefined): boolean {
  return (diagnostics?.length ?? 0) > 1;
}

// A CONDIÇÃO DO POLLING TEM DUAS METADES, E AS DUAS SÃO NECESSÁRIAS.
//
// A proveniência muda EXATAMENTE UMA VEZ: quando o primeiro documento termina de
// indexar. Logo:
//
//   acompanhar  ⟺  o índice está vazio  E  esta base tem documento não terminal
//
// A segunda metade é `hasNonTerminalDocument`, a mesma função pura que a etapa
// 5a-2 extraiu para o `refetchInterval` da listagem de documentos (convenção 20)
// — reuso, não invenção.
//
// A primeira metade é o que impede o pior caso, e o custo dela está medido: a
// rota é `Seq Scan` + `HashAggregate` sobre o heap inteiro de
// `knowledge_fragments`, 0,24–0,82 s com 150.000 fragmentos, medido na VM do
// podman com `shared_buffers` no default de 128 MB. As PÁGINAS valem em qualquer
// lugar; os milissegundos, só naquele disco (convenção 22). Com o índice já
// povoado, repetir isso a cada poucos segundos pagaria a varredura por um valor
// que não muda mais.
//
// Mora aqui, e não numa expressão embutida no hook, pelos dois motivos da
// convenção 20: é testável sem timer, e o guarda que a protege é pareado.
export function shouldPollIndexDiagnostics(
  diagnostics: KnowledgeIndexProvenance[] | undefined,
  documents: KnowledgeDocumentSummary[] | undefined,
): boolean {
  return isIndexEmpty(diagnostics) && hasNonTerminalDocument(documents);
}
