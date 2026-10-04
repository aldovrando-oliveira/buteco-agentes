import { describe, expect, it } from 'vitest';
import { connectorErrorMessage, providerLabel } from './connectorErrors';
import { ConnectorsError } from '../api/connectorsApi';
import { ApiError } from '../api/knowledgeBasesApi';

const EMAIL = 'buteco-sync@projeto.iam.gserviceaccount.com';
// Marcador no `title`: nenhum texto da tela pode vir dele (design.md, D3).
const SERVER_TITLE = 'TITULO-DO-SERVIDOR';

function connectorsError(status: number, code?: string, detail?: string) {
  return new ConnectorsError('http', 'x', { status, code, detail });
}

function apiError(status: number, problem: Record<string, unknown>) {
  return new ApiError(status, SERVER_TITLE, { title: SERVER_TITLE, status, ...problem });
}

const CREATED_NOTHING = 'Nenhuma base foi criada.';

describe('providerLabel', () => {
  it('nomeia o provedor conhecido', () => {
    expect(providerLabel('google-drive')).toBe('Google Drive');
  });

  it('devolve a própria chave para provedor desconhecido, sem inventar nome', () => {
    expect(providerLabel('onedrive')).toBe('onedrive');
  });
});

describe('connectorErrorMessage — navegação (apps/connectors)', () => {
  const cases: Array<[string, ConnectorsError, RegExp]> = [
    [
      'rede',
      new ConnectorsError('network', 'x'),
      /^Não foi possível falar com o serviço de conectores\. Ele pode estar fora do ar/,
    ],
    [
      'provider-not-configured',
      connectorsError(404, 'provider-not-configured'),
      /O provedor Google Drive não está configurado no serviço de conectores desta instalação\./,
    ],
    ['not-a-folder', connectorsError(422, 'not-a-folder'), /não é uma pasta/],
    ['folder-trashed', connectorsError(422, 'folder-trashed'), /está na lixeira do Drive/],
    [
      'api-not-configured',
      connectorsError(502, 'api-not-configured'),
      /Drive API não está ativada.*configuração da instalação, não da pasta/,
    ],
    [
      'provider-auth-failed',
      connectorsError(502, 'provider-auth-failed'),
      /recusou a credencial da conta de serviço.*configuração da instalação, não da pasta/,
    ],
    [
      'provider-error com detalhe',
      connectorsError(502, 'provider-error', 'bad-request'),
      /O Google recusou a operação \(bad-request\)\./,
    ],
    [
      'provider-error sem detalhe',
      connectorsError(502, 'provider-error'),
      /^O Google recusou a operação\.$/,
    ],
    [
      'provider-unavailable',
      connectorsError(503, 'provider-unavailable'),
      /O Google não respondeu/,
    ],
    ['401', connectorsError(401), /não aceitou a sua sessão/],
    ['403', connectorsError(403), /recusou esta consulta para o operador/],
    ['código desconhecido', connectorsError(422, 'folder-too-deep'), /código `?folder-too-deep`?/],
    ['sem código', connectorsError(500), /status 500/],
  ];

  it.each(cases)('%s', (_name, error, expected) => {
    const message = connectorErrorMessage(error, 'navigation', 'google-drive');
    expect(message).toMatch(expected);
    expect(message).not.toContain(CREATED_NOTHING);
  });

  it('access-denied mostra o e-mail da conta e a instrução de Leitor', () => {
    const message = connectorErrorMessage(
      connectorsError(422, 'access-denied', EMAIL),
      'navigation',
      'google-drive',
    );
    expect(message).toContain(EMAIL);
    expect(message).toMatch(/como Leitor/);
  });

  it('rate-limited diz que é passageira e não fala de acesso', () => {
    const message = connectorErrorMessage(connectorsError(503, 'rate-limited'), 'navigation');
    expect(message).toMatch(/passageira/);
    expect(message).not.toMatch(/acesso/i);
  });
});

describe('connectorErrorMessage — cadastro (apps/api)', () => {
  it('folder-in-use nomeia a base, diz que segue ocupada inativa, e não manda excluir', () => {
    const message = connectorErrorMessage(
      apiError(409, {
        code: 'folder-in-use',
        knowledgeBaseId: 'b-1',
        knowledgeBaseName: 'Políticas internas de RH',
        detail: 'A pasta já é usada pela base ...',
      }),
      'create',
    );
    expect(message).toContain('“Políticas internas de RH”');
    expect(message).toMatch(/inativa/);
    expect(message).not.toMatch(/exclu|remov|apag/i);
    expect(message).toContain(CREATED_NOTHING);
  });

  const cases: Array<[string, ApiError, RegExp]> = [
    [
      'connectors-not-configured',
      apiError(503, { code: 'connectors-not-configured' }),
      /ainda não está ligado ao serviço de conectores.*base manual continua disponível/,
    ],
    [
      'connectors-unavailable',
      apiError(503, { code: 'connectors-unavailable' }),
      /não conseguiu falar com o serviço de conectores para validar a pasta/,
    ],
    [
      'connectors-error',
      apiError(502, { code: 'connectors-error', detail: '401' }),
      /respondeu de forma inesperada ao validar a pasta \(status 401\)/,
    ],
    [
      'provider-not-configured repassado como 422',
      apiError(422, { code: 'provider-not-configured' }),
      /não está configurado no serviço de conectores/,
    ],
    ['not-a-folder', apiError(422, { code: 'not-a-folder' }), /não é uma pasta/],
    ['folder-trashed', apiError(422, { code: 'folder-trashed' }), /lixeira/],
    ['api-not-configured', apiError(502, { code: 'api-not-configured' }), /Drive API/],
    ['provider-auth-failed', apiError(502, { code: 'provider-auth-failed' }), /credencial/],
    ['provider-error', apiError(502, { code: 'provider-error' }), /O Google recusou a operação/],
    ['provider-unavailable', apiError(503, { code: 'provider-unavailable' }), /não respondeu/],
    ['código desconhecido', apiError(422, { code: 'folder-too-deep' }), /folder-too-deep/],
    ['sem código', apiError(500, {}), /status 500/],
  ];

  it.each(cases)('%s', (_name, error, expected) => {
    const message = connectorErrorMessage(error, 'create', 'google-drive');
    expect(message).toMatch(expected);
    expect(message.endsWith(CREATED_NOTHING)).toBe(true);
  });

  it('access-denied mostra o e-mail que veio no detail', () => {
    const message = connectorErrorMessage(
      apiError(422, { code: 'access-denied', detail: EMAIL }),
      'create',
    );
    expect(message).toContain(EMAIL);
    expect(message).toMatch(/como Leitor/);
  });

  it('rate-limited diz que é passageira e não fala de acesso', () => {
    const message = connectorErrorMessage(apiError(503, { code: 'rate-limited' }), 'create');
    expect(message).toMatch(/passageira/);
    expect(message).not.toMatch(/acesso/i);
  });
});

describe('o title do ProblemDetails nunca vira texto da tela', () => {
  const codes = [
    'access-denied',
    'not-a-folder',
    'folder-trashed',
    'provider-not-configured',
    'api-not-configured',
    'provider-auth-failed',
    'provider-error',
    'rate-limited',
    'provider-unavailable',
    'folder-in-use',
    'connectors-not-configured',
    'connectors-unavailable',
    'connectors-error',
    'codigo-novo',
  ];

  it.each(codes)('%s', (code) => {
    const message = connectorErrorMessage(apiError(422, { code }), 'create');
    expect(message).not.toContain(SERVER_TITLE);
  });
});
