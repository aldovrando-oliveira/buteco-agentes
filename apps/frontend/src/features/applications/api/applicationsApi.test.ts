import { describe, expect, it } from 'vitest';
import {
  activateApplication,
  countAgentApplications,
  createApplication,
  deactivateApplication,
  fieldErrors,
  generateSecret,
  getApplication,
  grantAgent,
  listAgentApplications,
  listApplicationAgents,
  listApplications,
  revokeAgent,
  revokeSecret,
  updateAgentOperations,
  updateApplication,
} from './applicationsApi';
import { fakeApplicationsApi, problem } from '../test/fixtures';

// As 14 chamadas contra o contrato das specs `external-applications` (#186) e
// `external-application-agents` (#187): método, caminho e corpo.

describe('applicationsApi', () => {
  it.each([
    ['detalhe', () => getApplication('a1'), 'GET /applications/a1', undefined],
    [
      'PUT',
      () => updateApplication('a1', { nome: 'X', descricao: null }),
      'PUT /applications/a1',
      { nome: 'X', descricao: null },
    ],
    [
      'deactivate',
      () => deactivateApplication('a1'),
      'POST /applications/a1/deactivate',
      undefined,
    ],
    ['activate', () => activateApplication('a1'), 'POST /applications/a1/activate', undefined],
    [
      'secrets',
      () => generateSecret('a1', { senha: 'senha-1' }),
      'POST /applications/a1/secrets',
      { senha: 'senha-1' },
    ],
    [
      'revoke',
      () => revokeSecret('a1', 's1'),
      'POST /applications/a1/secrets/s1/revoke',
      undefined,
    ],
    ['agents', () => listApplicationAgents('a1'), 'GET /applications/a1/agents', undefined],
    [
      'grant',
      () => grantAgent('a1', 'ag1', ['enviar-mensagem']),
      'POST /applications/a1/agents',
      { agenteId: 'ag1', operacoes: ['enviar-mensagem'] },
    ],
    [
      'operações',
      () => updateAgentOperations('a1', 'ag1', ['consultar-task']),
      'PUT /applications/a1/agents/ag1',
      { operacoes: ['consultar-task'] },
    ],
    [
      'lista do agente',
      () => listAgentApplications('ag1'),
      'GET /agents/ag1/applications',
      undefined,
    ],
    [
      'contagem do agente',
      () => countAgentApplications('ag1'),
      'GET /agents/ag1/applications/count',
      undefined,
    ],
  ])('%s: o método, o caminho e o corpo', async (_nome, chamar, rota, corpo) => {
    const api = fakeApplicationsApi({ [rota]: { status: 200, body: {} } });

    await chamar();

    const [pedido] = api.calls(rota);
    expect(pedido).toBeDefined();
    expect(pedido.body).toEqual(corpo);
  });

  it('cria com nome, descrição e senha', async () => {
    const api = fakeApplicationsApi({ 'POST /applications/': { status: 201, body: {} } });

    await createApplication({ nome: 'Portal', descricao: null, senha: 's' });

    expect(api.calls('POST /applications/')[0].body).toEqual({
      nome: 'Portal',
      descricao: null,
      senha: 's',
    });
  });

  it('a lista manda a busca aparada e codificada, e o estado só quando não é "todas"', async () => {
    const api = fakeApplicationsApi({ 'GET /applications/': { status: 200, body: [] } });

    await listApplications({ busca: '  erp & cia ', estado: 'ativas' });
    await listApplications({ busca: '   ', estado: 'todas' });

    const urls = api.fetch.mock.calls.map(([url]) => new URL(url as string, 'http://localhost'));
    const lista = urls.filter((u) => u.pathname === '/applications/');
    expect(lista[0].searchParams.get('busca')).toBe('erp & cia');
    expect(lista[0].searchParams.get('estado')).toBe('ativas');
    expect(lista[1].search).toBe('');
  });

  it('o DELETE com 204 resolve sem corpo', async () => {
    fakeApplicationsApi({ 'DELETE /applications/a1/agents/ag1': { status: 204 } });

    await expect(revokeAgent('a1', 'ag1')).resolves.toBeUndefined();
  });

  it('o 401 da senha chega com o title, sem renovar a sessão', async () => {
    const api = fakeApplicationsApi({
      'POST /applications/a1/secrets': problem(401, 'Senha inválida.'),
    });

    await expect(generateSecret('a1', { senha: 'errada' })).rejects.toMatchObject({
      status: 401,
      title: 'Senha inválida.',
    });
    expect(api.calls('POST /connect/token')).toHaveLength(0);
  });

  it('o 423 traz o Retry-After em segundos', async () => {
    fakeApplicationsApi({
      'POST /applications/': problem(423, 'Conta bloqueada.', { 'Retry-After': '300' }),
    });

    await expect(
      createApplication({ nome: 'X', descricao: null, senha: 's' }),
    ).rejects.toMatchObject({
      status: 423,
      retryAfter: 300,
    });
  });

  it('fieldErrors devolve o primeiro texto de cada campo do 400', async () => {
    fakeApplicationsApi({
      'POST /applications/': {
        status: 400,
        body: {
          title: 'One or more validation errors occurred.',
          errors: { nome: ['Informe o nome.'] },
        },
      },
    });

    const erro = await createApplication({ nome: '', descricao: null, senha: 's' }).catch(
      (e: unknown) => e,
    );

    expect(fieldErrors(erro)).toEqual({ nome: 'Informe o nome.' });
  });
});
