import type { KnowledgeBase, KnowledgeBaseSyncState } from '../types/knowledgeBase';

// ESTADO DA SINCRONIZAÇÃO DE UMA BASE, lido de `syncState` (design.md da change
// frontend-detalhe-base-sincronizada, D2). Quatro estados, e o primeiro é o que
// alguém vai querer pintar de vermelho:
//
//   never-synced            — nenhum ciclo foi gravado. NÃO é falha: a base
//                             espera a primeira rodada. Tom neutro, sem alerta.
//   up-to-date              — o último ciclo gravado foi um sucesso.
//   failing                 — falhando, com uma sincronização concluída antes.
//   failing-never-completed — falhando, sem nunca ter concluído: o nome da pasta
//                             é o do cadastro, e não o de uma sincronização.
export type SyncStatus = 'never-synced' | 'up-to-date' | 'failing' | 'failing-never-completed';

// "Falhando" é `failingSince !== null`, e só isso. O apps/api grava e limpa
// `lastError` e `failingSince` juntos (knowledge-sync-service-api); um erro sem
// "falhando desde" não é alcançável, e quando aparecer cai em "em dia", porque o
// predicado é UM SÓ — o mesmo do filtro "Com falha" da listagem (D6).
export function syncStatus(state: KnowledgeBaseSyncState): SyncStatus {
  if (state.failingSince !== null) {
    return state.lastCompletedAt !== null ? 'failing' : 'failing-never-completed';
  }

  return state.lastCompletedAt !== null ? 'up-to-date' : 'never-synced';
}

export function isSyncFailing(knowledgeBase: Pick<KnowledgeBase, 'syncState'>): boolean {
  return knowledgeBase.syncState !== null && knowledgeBase.syncState.failingSince !== null;
}

// Data COM ano e hora, no fuso do navegador, nas duas telas (listagem e
// detalhe). A prancha 1 escreve "25/09, 09:10" sem ano, e uma falha de outro ano
// seria lida como recente (correção de protótipo C3).
const INSTANT_FORMAT = new Intl.DateTimeFormat('pt-BR', {
  dateStyle: 'short',
  timeStyle: 'short',
});

export function formatSyncInstant(iso: string): string {
  return INSTANT_FORMAT.format(new Date(iso));
}

// ACOMPANHAMENTO DO "SINCRONIZAR AGORA" (D5). A rota do apps/connectors responde
// 202 na hora e roda o ciclo em segundo plano; o único sinal de fim é o apps/api
// gravar um resultado, que muda `lastFinishedAt` em sucesso E em falha.
//
// `baseline` é o `lastFinishedAt` RELIDO imediatamente antes do pedido, e não o
// do cache: com o detalhe aberto há minutos, uma rodada periódica pode ter
// terminado desde a última leitura, e o fim dela seria lido como o do pedido.
//
// A comparação é por IGUALDADE com a linha de base, e não por "depois do
// clique": o instante do clique é do relógio do navegador, `lastFinishedAt` é do
// relógio do apps/api, e qualquer diferença entre os dois acusaria o fim cedo
// demais ou nunca.
export interface SyncRequest {
  baseline: string | null;
  requestedAt: number;
}

// 4 s: o mesmo intervalo das outras duas consultas periódicas do detalhe
// (useKnowledgeDocuments.ts, useKnowledgeIndex.ts).
export const SYNC_POLL_INTERVAL_MS = 4000;

// 5 min: o intervalo da rodada do apps/connectors
// (apps/connectors/src/Buteco.Connectors/Sync/SyncSchedulerService.cs:13). Depois
// dele a próxima rodada já teria gravado, e esperar mais não muda o que a tela
// sabe. O LIMITE EXISTE porque há caminhos do ciclo que não gravam nada — base com
// 404 no meio do ciclo, apps/api sem resposta, exceção inesperada (#105, D4 e D8)
// — e sem ele a tela esperaria para sempre com o botão desabilitado.
// Gatilho: o intervalo da rodada mudar.
export const SYNC_WAIT_LIMIT_MS = 5 * 60 * 1000;

export type SyncRequestPhase = 'waiting' | 'finished' | 'timed-out';

// A mudança vence o limite: um resultado gravado nunca é lido como "sem
// resultado". Sem a base em mãos (consulta em voo), continua esperando.
export function syncRequestPhase(
  request: SyncRequest,
  knowledgeBase: Pick<KnowledgeBase, 'syncState'> | undefined,
  now: number,
): SyncRequestPhase {
  const current = knowledgeBase?.syncState?.lastFinishedAt;
  if (current !== undefined && current !== request.baseline) {
    return 'finished';
  }

  return now - request.requestedAt >= SYNC_WAIT_LIMIT_MS ? 'timed-out' : 'waiting';
}

// A condição da consulta periódica, em função pura (convenção 20): o intervalo
// enquanto o pedido espera, `false` em qualquer outro caso.
export function syncWaitInterval(
  request: SyncRequest | null,
  knowledgeBase: Pick<KnowledgeBase, 'syncState'> | undefined,
  now: number,
): number | false {
  if (request === null) {
    return false;
  }

  return syncRequestPhase(request, knowledgeBase, now) === 'waiting'
    ? SYNC_POLL_INTERVAL_MS
    : false;
}
