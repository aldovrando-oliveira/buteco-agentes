// As aplicações externas no fio do apps/api, nos nomes de campo das respostas (specs
// `external-applications`, #186, e `external-application-agents`, #187). Nada aqui é estado de
// tela: o painel lê e manda só estes formatos.

/** Uma das três operações liberáveis, na ordem da tabela do vínculo (#187, D1). */
export type Operacao = 'enviar-mensagem' | 'consultar-task' | 'notificacao-push';

export const OPERACOES: readonly Operacao[] = [
  'enviar-mensagem',
  'consultar-task',
  'notificacao-push',
];

/** O estado do segredo é derivado pelo servidor na leitura (#186, D4); o painel não o recalcula. */
export type EstadoSegredo = 'ativo' | 'substituido' | 'expirado' | 'revogado';

export interface AgenteChip {
  id: string;
  nome: string;
}

/** Uma linha de `GET /applications/`. */
export interface ApplicationSummary {
  id: string;
  nome: string;
  descricao: string | null;
  clientId: string;
  ativa: boolean;
  criadaEm: string;
  desativadaEm: string | null;
  ultimoUso: string | null;
  agentes: AgenteChip[];
}

export interface SecretSummary {
  id: string;
  sufixo: string;
  estado: EstadoSegredo;
  criadoEm: string;
  validoAte: string;
  revogadoEm: string | null;
  ultimoUso: string | null;
}

/**
 * `GET /applications/{id}`: os campos da lista SEM `agentes` (o `ApplicationDetail` do apps/api não
 * os traz; os agentes liberados vêm de `GET /applications/{id}/agents`), mais o endpoint de token e
 * os segredos sem texto. A spec `external-applications` diz "os campos da lista": premissa falsa
 * achada na conferência da #188.
 */
export interface ApplicationDetail extends Omit<ApplicationSummary, 'agentes'> {
  endpointToken: string;
  segredos: SecretSummary[];
}

/** O segredo recém-gerado: o `valor` só existe nas duas respostas de criação. */
export interface CreatedSecret {
  id: string;
  valor: string;
  sufixo: string;
  criadoEm: string;
  validoAte: string;
}

/** O `201` de `POST /applications/`. */
export interface CreatedApplication {
  id: string;
  nome: string;
  descricao: string | null;
  clientId: string;
  ativa: boolean;
  criadaEm: string;
  ultimoUso: string | null;
  endpointToken: string;
  segredo: CreatedSecret;
}

/** Uma linha de `GET /applications/{id}/agents`. */
export interface ApplicationAgent {
  agenteId: string;
  nome: string;
  descricao: string | null;
  ativo: boolean;
  operacoes: Operacao[];
  liberadoEm: string;
}

/** Uma linha de `GET /agents/{id}/applications` (só do Admin). */
export interface AgentApplication {
  id: string;
  nome: string;
  ativa: boolean;
  operacoes: Operacao[];
  ultimoUso: string | null;
}

/** `GET /agents/{id}/applications/count` (Admin, Operador e Leitor). */
export interface AgentApplicationsCount {
  total: number;
  ativas: number;
}

export type EstadoFiltro = 'todas' | 'ativas' | 'desativadas';
