import { sharedCodeSentence } from './connectorErrors';
import type { KnowledgeBaseIgnoredFile, KnowledgeBaseSyncError } from '../types/knowledgeBase';

// TEXTO DA TELA PARA O CÓDIGO GRAVADO NO ESTADO DA BASE — as tabelas "Falha da
// base" e "Arquivo ignorado" da D4 do design.md da change
// frontend-detalhe-base-sincronizada.
//
// O texto vem do `code`, nunca do `detail` como frase. Do `detail`, só o que a
// tabela declara: o e-mail da conta em `access-denied` de base (o ciclo grava o
// e-mail como detalhe, knowledge-sync-cycle), o motivo em `provider-error`, o
// tipo MIME em `unsupported-type` e o tamanho em bytes em `too-large`.
//
// Onde o código e a frase valem igual no cadastro (#106) e aqui, a frase vem de
// `sharedCodeSentence` (connectorErrors.ts). O que muda é o remédio: no cadastro
// é "tente de novo" ou "escolha outra pasta"; aqui a pasta não pode ser trocada
// e a próxima tentativa é automática.
//
// NENHUM TEXTO CONTÉM `exclu`, `remov` NEM `apag`, inclusive onde a frase diz o
// que NÃO aconteceu. A proibição existe para a tela não mandar excluir a base,
// que é escopo da #136; a garantia da issue ("nada foi excluído") foi reescrita
// como "nenhum documento saiu da base" para caber nela (D4 e C6, aprovado pelo
// mantenedor). Quando a #136 chegar, ela revê esta regra junto com o botão.
//
// AFIRMAÇÕES SOBRE OUTRO APP (convenção 13), cada uma com a origem e o gatilho:
//   - "a cada 5 minutos": SyncSchedulerService.Interval,
//     apps/connectors/src/Buteco.Connectors/Sync/SyncSchedulerService.cs:13.
//     Gatilho: o intervalo da rodada mudar.
//   - "1 MiB": KnowledgeDocumentLimits.MaxContentBytes,
//     apps/api/src/Buteco.Api/KnowledgeDocuments/Options/KnowledgeDocumentLimits.cs:23.
//     Gatilho: o teto mudar. O `detail` traz só o tamanho medido, não o teto.
//   - "Só Google Docs e arquivos .md": MarkdownMimeTypes e GoogleDocMimeType,
//     apps/connectors/src/Buteco.Connectors/Connectors/GoogleDrive/GoogleDriveFolderContentSource.cs:14 e :91.
//     Gatilho: o conector passar a aceitar outro tipo.

const AUTOMATIC = 'na próxima rodada de sincronização (a cada 5 minutos)';

const GUARANTEE =
  'Nenhum documento saiu da base: ela continua respondendo com o conteúdo da última sincronização concluída, que pode estar desatualizado.';

// Sem nenhuma sincronização concluída não existe "conteúdo da última
// sincronização concluída" para prometer (D2, último estado).
const GUARANTEE_NEVER_COMPLETED =
  'Nenhum documento saiu da base, e nenhuma sincronização foi concluída ainda.';

const INSTALLATION = 'Configuração da instalação';

export interface SyncFailureText {
  title: string;
  sentence: string;
  guarantee: string;
  // O que o operador da base pode fazer; `null` quando não há nada a fazer na
  // pasta (a falha é passageira ou é da instalação).
  remedy: string | null;
}

export const SYNC_FAILURE_CODES = [
  'access-denied',
  'folder-trashed',
  'not-a-folder',
  'rate-limited',
  'provider-unavailable',
  'provider-error',
  'api-not-configured',
  'provider-auth-failed',
  'provider-not-configured',
  'sync-api-error',
] as const;

function failureParts(
  code: string,
  detail: string | null,
  providerKey: string | null,
): Omit<SyncFailureText, 'guarantee'> {
  switch (code) {
    case 'access-denied':
      return {
        title: 'Sem acesso à pasta',
        sentence: sharedCodeSentence(code, detail, providerKey) as string,
        remedy:
          'Para resolver, compartilhe a pasta com essa conta como Leitor. Se a pasta deixou de existir no Drive, crie uma nova base com outra pasta: esta base não pode ser apontada para outra pasta.',
      };
    case 'folder-trashed':
      return {
        title: 'Pasta na lixeira',
        sentence: 'A pasta está na lixeira do Drive.',
        remedy: 'Para resolver, restaure a pasta no Drive; a próxima sincronização volta a lê-la.',
      };
    case 'not-a-folder':
      return {
        title: 'A origem não é mais uma pasta',
        sentence: 'O item de origem desta base não é mais uma pasta no Drive.',
        remedy:
          'Esta base não pode ser apontada para outra pasta: para sincronizar outra, crie uma nova base.',
      };
    // Passageira e automática, e SEM falar de acesso, conta nem compartilhamento:
    // cota estourada não é problema de permissão (comentário da #105 na #107).
    case 'rate-limited':
      return {
        title: 'Limite de chamadas do Google',
        sentence: `O Google limitou as chamadas por um momento. A falha é passageira, e a próxima tentativa é automática, ${AUTOMATIC}.`,
        remedy: null,
      };
    case 'provider-unavailable':
      return {
        title: 'O Google não respondeu',
        sentence: `O Google não respondeu durante a sincronização. A falha costuma ser passageira, e a próxima tentativa é automática, ${AUTOMATIC}.`,
        remedy: null,
      };
    case 'provider-error':
      return {
        title: 'O Google recusou a operação',
        sentence: `${sharedCodeSentence(code, detail, providerKey)} A próxima tentativa é automática, ${AUTOMATIC}.`,
        remedy: null,
      };
    case 'api-not-configured':
    case 'provider-auth-failed':
    case 'provider-not-configured':
      return {
        title: INSTALLATION,
        sentence: sharedCodeSentence(code, detail, providerKey) as string,
        remedy: null,
      };
    case 'sync-api-error':
      return {
        title: 'Falha no servidor',
        sentence: `O servidor respondeu de forma inesperada durante a sincronização. A próxima tentativa é automática, ${AUTOMATIC}.`,
        remedy: null,
      };
    // Código que o painel não conhece: texto neutro com o código, nunca o texto
    // de outro código (convenção 13).
    default:
      return {
        title: 'Falha na sincronização',
        sentence: `A sincronização falhou com um código que o painel não reconhece (${code}).`,
        remedy: null,
      };
  }
}

export function syncFailureMessage(
  error: KnowledgeBaseSyncError,
  options: { completedBefore: boolean; providerKey: string | null },
): SyncFailureText {
  return {
    ...failureParts(error.code, error.detail, options.providerKey),
    guarantee: options.completedBefore ? GUARANTEE : GUARANTEE_NEVER_COMPLETED,
  };
}

export const IGNORED_FILE_CODES = [
  'shortcut-not-followed',
  'subfolder-not-synced',
  'unsupported-type',
  'download-blocked',
  'file-not-found',
  'provider-error',
  'provider-unavailable',
  'access-denied',
  'too-large',
  'unsupported-source-type',
  'null-character',
  'empty-content',
] as const;

export function ignoredFileReason(file: Pick<KnowledgeBaseIgnoredFile, 'code' | 'detail'>): string {
  const { code, detail } = file;

  switch (code) {
    case 'shortcut-not-followed':
      return 'É um atalho, e a sincronização não segue atalhos. Coloque o arquivo em si na pasta.';
    case 'subfolder-not-synced':
      return 'É uma subpasta. Só os arquivos da raiz da pasta entram na base.';
    case 'unsupported-type':
      return `Tipo não suportado${detail ? ` (${detail})` : ''}. Só Google Docs e arquivos .md entram na base.`;
    // Por arquivo, nunca falha da base (comentário da etapa 0 na #107).
    case 'download-blocked':
      return 'O download está bloqueado para leitores. Permita o download para leitores neste arquivo, no Drive.';
    // As três seguintes são transitórias e não ficam na memória de recusas do
    // ciclo (knowledge-sync-cycle): a próxima rodada tenta de novo.
    case 'file-not-found':
      return 'O arquivo não foi encontrado na hora do download. Se ele continuar na pasta, a próxima sincronização tenta de novo.';
    case 'provider-error':
      return `O Google recusou a leitura deste arquivo${detail ? ` (${detail})` : ''}. A próxima sincronização tenta de novo.`;
    case 'provider-unavailable':
      return 'O Google não respondeu ao exportar este arquivo. A próxima sincronização tenta de novo.';
    // A permissão negada de um arquivo individual fica no arquivo (#105, D4,
    // "fixado na implementação"); não usa a frase da pasta.
    case 'access-denied':
      return 'A conta de serviço não tem acesso a este arquivo. Compartilhe o arquivo com a conta como Leitor.';
    // As quatro recusas de conteúdo da #120 ficam na memória de recusas enquanto
    // o marcador do arquivo não muda (#105, D14): o texto pede mudar o arquivo, e
    // não "tentar de novo". O tamanho sai como veio, em bytes, sem converter.
    case 'too-large':
      return `Maior que o limite de 1 MiB depois da exportação${detail ? ` (${detail} bytes)` : ''}.`;
    case 'unsupported-source-type':
      return 'O servidor não aceitou o formato enviado pela sincronização. Não depende do arquivo: é falha da integração.';
    case 'null-character':
      return 'O conteúdo exportado tem um caractere nulo, que o servidor não aceita. Corrija o arquivo no Drive.';
    case 'empty-content':
      return 'O arquivo está vazio depois da exportação. Ele entra na base quando tiver conteúdo.';
    default:
      return `Motivo que o painel não reconhece (${code}).`;
  }
}
