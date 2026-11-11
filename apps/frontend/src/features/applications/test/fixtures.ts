import type {
  AgentApplication,
  ApplicationAgent,
  ApplicationDetail,
  ApplicationSummary,
  CreatedApplication,
  CreatedSecret,
  SecretSummary,
} from '../types';

// As aplicações do quadro `AplicacoesLista` (versão 66), no formato do fio. O "agora" dos testes
// é `NOW`; os últimos usos são relativos a ele.

export {
  fakeAccountApi as fakeApplicationsApi,
  problem,
  type FakeReply,
} from '../../account/test/fakeAccountApi';

export const NOW = new Date('2026-11-09T15:00:00Z');
const antes = (ms: number) => new Date(NOW.getTime() - ms).toISOString();
const MIN = 60_000;
const DIA = 24 * 60 * MIN;

export const PORTAL: ApplicationSummary = {
  id: 'app-portal',
  nome: 'Portal do Parceiro',
  descricao: 'Autoatendimento de revendas',
  clientId: 'btc_app_7kq2m9x4',
  ativa: true,
  // O formato real do apps/api: microssegundos e `+00:00`, e não `Z` (conferência das fixtures, 10/11/2026).
  criadaEm: '2026-10-21T12:00:00.123456+00:00',
  desativadaEm: null,
  ultimoUso: antes(3 * MIN),
  agentes: [
    { id: 'ag-varejo', nome: 'Atendimento Varejo' },
    { id: 'ag-pedidos', nome: 'Triagem de Pedidos' },
  ],
};

export const CHATBOT: ApplicationSummary = {
  id: 'app-chatbot',
  nome: 'Chatbot do site',
  descricao: 'Widget público de dúvidas',
  clientId: 'btc_app_p3w8d1z6',
  ativa: true,
  criadaEm: '2026-10-25T12:00:00Z',
  desativadaEm: null,
  ultimoUso: null,
  agentes: [],
};

export const CRM: ApplicationSummary = {
  id: 'app-crm',
  nome: 'CRM legado',
  descricao: 'Integração descontinuada',
  clientId: 'btc_app_c9r4t2v7',
  ativa: false,
  criadaEm: '2025-05-02T12:00:00Z',
  desativadaEm: '2026-11-08T13:00:00Z',
  ultimoUso: antes(41 * DIA),
  agentes: [{ id: 'ag-varejo', nome: 'Atendimento Varejo' }],
};

export const ENDPOINT = 'https://buteco.exemplo.test/connect/token';

const segredo = (
  id: string,
  sufixo: string,
  estado: SecretSummary['estado'],
  validoAte: string,
  ultimoUso: string | null,
): SecretSummary => ({
  id,
  sufixo,
  estado,
  criadoEm: '2026-10-21T12:00:00Z',
  validoAte,
  revogadoEm: estado === 'revogado' ? '2026-07-10T12:00:00Z' : null,
  ultimoUso,
});

export const ATIVO = segredo('s-ativo', 'a91f', 'ativo', '2027-10-21T12:00:00Z', antes(3 * MIN));
export const SUBSTITUIDO = segredo(
  's-subst',
  '3c07',
  'substituido',
  '2026-10-28T12:00:00Z',
  antes(2 * DIA),
);
export const REVOGADO = segredo(
  's-rev',
  '77d2',
  'revogado',
  '2027-01-10T12:00:00Z',
  antes(120 * DIA),
);
export const EXPIRADO = segredo('s-exp', '5e18', 'expirado', '2025-09-02T12:00:00Z', null);

export function detalhe(
  base: ApplicationSummary = PORTAL,
  segredos: SecretSummary[] = [ATIVO, SUBSTITUIDO, REVOGADO, EXPIRADO],
): ApplicationDetail {
  // O detalhe NÃO traz `agentes` (o `ApplicationDetail` do apps/api, desde a #186; só a lista os
  // traz, desde a #187). A fixture segue o fio, e não a frase da spec `external-applications`, que
  // diz "os campos da lista" (premissa falsa achada na conferência da #188).
  const { agentes: _semAgentes, ...semAgentes } = base;
  void _semAgentes;
  return { ...semAgentes, endpointToken: ENDPOINT, segredos };
}

/** O segredo de teste: o prefixo `btc_sk_` é o que os guardas procuram nos caches e no armazenamento. */
export const VALOR_CRIADO = 'btc_sk_V3f9Qm2LxP8rT6wZ1nK4yH7cJ0aB5dEfGhIjKlM';
export const VALOR_GERADO = 'btc_sk_Q8wN2xR5tL1mK7vB4cZ9yH3jF6dP0sAbCdEfGhIjK';

export const CRIADA: CreatedApplication = {
  id: PORTAL.id,
  nome: PORTAL.nome,
  descricao: PORTAL.descricao,
  clientId: PORTAL.clientId,
  ativa: true,
  criadaEm: NOW.toISOString(),
  ultimoUso: null,
  endpointToken: ENDPOINT,
  segredo: {
    id: 's-novo',
    valor: VALOR_CRIADO,
    sufixo: 'kLmN',
    criadoEm: NOW.toISOString(),
    validoAte: '2027-11-09T15:00:00Z',
  },
};

export const GERADO: CreatedSecret = {
  id: 's-gerado',
  valor: VALOR_GERADO,
  sufixo: 'IjK0',
  criadoEm: NOW.toISOString(),
  validoAte: '2027-11-09T15:00:00Z',
};

// ---------------------------------------------------------------- vínculos

export const VAREJO: ApplicationAgent = {
  agenteId: 'ag-varejo',
  nome: 'Atendimento Varejo',
  descricao: 'Dúvidas de produto, prazo e troca',
  ativo: true,
  operacoes: ['enviar-mensagem', 'consultar-task', 'notificacao-push'],
  liberadoEm: '2026-10-22T12:00:00Z',
};

export const PEDIDOS: ApplicationAgent = {
  agenteId: 'ag-pedidos',
  nome: 'Triagem de Pedidos',
  descricao: 'Abre e classifica pedidos de revenda',
  ativo: true,
  operacoes: ['enviar-mensagem', 'consultar-task'],
  liberadoEm: '2026-10-22T12:00:00Z',
};

export const COBRANCA_INATIVA: ApplicationAgent = {
  agenteId: 'ag-cobranca',
  nome: 'Cobrança',
  descricao: null, // o real manda `null` para agente sem descrição
  ativo: false,
  operacoes: ['enviar-mensagem'],
  liberadoEm: '2026-10-22T12:00:00Z',
};

/** O catálogo de `GET /agents/` (o formato do `Agent` do painel, só com o que o seletor lê). */
export function agenteDoCatalogo(
  id: string,
  name: string,
  description: string | null,
  isActive = true,
) {
  return {
    id,
    name,
    description,
    isActive,
    instructions: '',
    provider: null,
    model: null,
    skills: [],
    createdAt: '2026-10-01T12:00:00Z',
    updatedAt: '2026-10-01T12:00:00Z',
    mcpServers: [],
    delegatesTo: [],
    knowledgeBases: [],
    a2a: null,
  };
}

export const CATALOGO = [
  agenteDoCatalogo('ag-varejo', 'Atendimento Varejo', 'Dúvidas de produto, prazo e troca'),
  agenteDoCatalogo('ag-pedidos', 'Triagem de Pedidos', 'Abre e classifica pedidos de revenda'),
  agenteDoCatalogo('ag-suporte', 'Suporte Técnico', 'Diagnóstico de primeiro nível'),
  agenteDoCatalogo('ag-cobranca', 'Cobrança', 'Segunda via e negociação', false),
];

// ---------------------------------------------------------------- card do agente

export const APPS_DO_AGENTE: AgentApplication[] = [
  {
    id: PORTAL.id,
    nome: PORTAL.nome,
    ativa: true,
    operacoes: ['enviar-mensagem', 'consultar-task', 'notificacao-push'],
    ultimoUso: antes(3 * MIN),
  },
  {
    id: CHATBOT.id,
    nome: CHATBOT.nome,
    ativa: true,
    operacoes: ['enviar-mensagem', 'consultar-task'],
    ultimoUso: null,
  },
  {
    id: CRM.id,
    nome: CRM.nome,
    ativa: false,
    operacoes: ['enviar-mensagem'],
    ultimoUso: antes(41 * DIA),
  },
];
