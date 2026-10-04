import { ConnectorsError } from '../api/connectorsApi';
import { ApiError } from '../api/knowledgeBasesApi';

// TEXTO DA TELA PARA CADA CÓDIGO, a tabela da D3 do design.md da change
// frontend-cadastro-base-sincronizada. Duas origens de erro, um vocabulário só:
//
//   - "navigation": as rotas do apps/connectors que o painel chama direto
//     (provedores e pastas, connectors-api, D9 da apps-connectors-google-drive);
//   - "create": o `POST /knowledge-bases` do apps/api, que repassa o `code` do
//     apps/connectors como veio (404 e 422 viram 422) e acrescenta os próprios
//     (knowledge-sync-folder-validation, #104). Todo texto deste contexto termina
//     em "Nenhuma base foi criada.".
//
// O texto vem do `code`, NUNCA do `title` (texto do servidor, que muda sem a tela
// saber). Do `detail`, só os dados que a tabela declara: o e-mail em
// `access-denied`, o status em `connectors-error` e o motivo em `provider-error`.
// O nome da base em `folder-in-use` vem da extensão `knowledgeBaseName`.

export type ConnectorErrorContext = 'navigation' | 'create';

// Nome de exibição dos provedores conhecidos. A rota devolve só `key` e
// `accountEmail`; provedor que o painel não conhece aparece pela própria chave,
// sem inventar nome (design.md, D6, C6).
const PROVIDER_LABELS: Record<string, string> = {
  'google-drive': 'Google Drive',
};

export function providerLabel(key: string): string {
  return PROVIDER_LABELS[key] ?? key;
}

const CREATED_NOTHING = 'Nenhuma base foi criada.';

export const CONNECTORS_NETWORK_MESSAGE =
  'Não foi possível falar com o serviço de conectores. Ele pode estar fora do ar; tente de novo em instantes.';

interface NormalizedError {
  network: boolean;
  status?: number;
  code?: string;
  detail?: string;
  knowledgeBaseName?: string;
}

function normalize(error: unknown): NormalizedError {
  if (error instanceof ConnectorsError) {
    const name = error.extensions.knowledgeBaseName;
    return {
      network: error.kind === 'network',
      status: error.status,
      code: error.code,
      detail: error.detail,
      knowledgeBaseName: typeof name === 'string' ? name : undefined,
    };
  }
  if (error instanceof ApiError) {
    return {
      network: false,
      status: error.status,
      code: typeof error.problem?.code === 'string' ? error.problem.code : undefined,
      detail: typeof error.problem?.detail === 'string' ? error.problem.detail : undefined,
      knowledgeBaseName:
        typeof error.problem?.knowledgeBaseName === 'string'
          ? error.problem.knowledgeBaseName
          : undefined,
    };
  }
  return { network: false };
}

function codeMessage(
  { status, code, detail, knowledgeBaseName }: NormalizedError,
  providerKey?: string | null,
): string {
  switch (code) {
    case 'provider-not-configured':
      return `O provedor ${providerKey ? providerLabel(providerKey) : 'escolhido'} não está configurado no serviço de conectores desta instalação.`;
    case 'access-denied':
      return `A conta ${detail ?? 'de serviço'} não tem acesso a esta pasta. Compartilhe a pasta com essa conta como Leitor e tente de novo.`;
    case 'not-a-folder':
      return 'O item escolhido não é uma pasta. Escolha uma pasta.';
    case 'folder-trashed':
      return 'Esta pasta está na lixeira do Drive. Restaure a pasta ou escolha outra.';
    case 'api-not-configured':
      return 'A Drive API não está ativada no projeto da conta de serviço. É configuração da instalação, não da pasta.';
    case 'provider-auth-failed':
      return 'O Google recusou a credencial da conta de serviço. É configuração da instalação, não da pasta.';
    case 'provider-error':
      return detail ? `O Google recusou a operação (${detail}).` : 'O Google recusou a operação.';
    // Passageira, e sem falar de acesso, para não ser lida como problema de
    // permissão (comentário da #105 na #107). No cadastro, a nova tentativa é
    // manual; "a próxima tentativa é automática" é do ciclo, e fica para a #107.
    case 'rate-limited':
      return 'O Google limitou as chamadas por um momento. A falha é passageira: tente de novo em instantes.';
    case 'provider-unavailable':
      return 'O Google não respondeu. Tente de novo em instantes.';
    // Nomeia a base e diz que a pasta segue ocupada com a base inativa, e NÃO
    // manda excluir, remover nem apagar: o painel não oferece a exclusão de base,
    // e a orientação de excluir entra com o botão, na #136, que troca este texto e
    // a asserção negativa dele (design.md, D3).
    case 'folder-in-use':
      return `Esta pasta já é usada pela base “${knowledgeBaseName ?? 'sem nome'}”. Uma pasta alimenta uma base só, e continua ocupada mesmo com a base inativa. Escolha outra pasta.`;
    case 'connectors-not-configured':
      return 'O servidor desta instalação ainda não está ligado ao serviço de conectores. É configuração da instalação; a base manual continua disponível.';
    case 'connectors-unavailable':
      return 'O servidor não conseguiu falar com o serviço de conectores para validar a pasta. Tente de novo em instantes.';
    case 'connectors-error':
      return `O serviço de conectores respondeu de forma inesperada ao validar a pasta (status ${detail ?? status ?? 'desconhecido'}).`;
  }

  if (code) {
    return `Erro inesperado (código ${code}).`;
  }
  // Sem `code`, só a autorização do subject usa 401/403 no apps/connectors
  // (connectors-api, D9 da apps-connectors-google-drive).
  if (status === 401) {
    return 'O serviço de conectores não aceitou a sua sessão. Saia e entre de novo no painel; se continuar, é configuração da instalação.';
  }
  if (status === 403) {
    return 'O serviço de conectores recusou esta consulta para o operador. É configuração da instalação.';
  }
  return `Erro inesperado (status ${status ?? 'desconhecido'}).`;
}

export function connectorErrorMessage(
  error: unknown,
  context: ConnectorErrorContext,
  providerKey?: string | null,
): string {
  const normalized = normalize(error);
  const message = normalized.network
    ? CONNECTORS_NETWORK_MESSAGE
    : codeMessage(normalized, providerKey);
  return context === 'create' ? `${message} ${CREATED_NOTHING}` : message;
}
