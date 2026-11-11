import { describe, expect, it } from 'vitest';
import { act, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { CRIADA, VALOR_CRIADO, detalhe, fakeApplicationsApi, problem } from '../test/fixtures';
import { cachesSerializados, renderApplications } from '../test/renderApplications';

// Spec `panel-external-applications`: "Nova aplicação", "Credencial criada, mostrada uma vez" e
// "O segredo vive só no componente que o recebeu" (quadros `AplicacaoNova` e
// `AplicacaoCredencial`, versão 66). Os casos marcados GUARDA são os da seção 4 do tasks.md.

const CRIAR = 'POST /applications/';

async function preencherECriar(
  user: ReturnType<typeof userEvent.setup>,
  { nome = 'Portal do Parceiro', senha = 'senha-certa' } = {},
) {
  if (nome) await user.type(screen.getByLabelText('Nome'), nome);
  if (senha) await user.type(screen.getByLabelText('Sua senha'), senha);
  await user.click(screen.getByRole('button', { name: 'Criar aplicação' }));
}

function rotasDoDetalhe() {
  return {
    [CRIAR]: { status: 201, body: CRIADA },
    'GET /applications/app-portal': { status: 200, body: detalhe() },
    'GET /applications/app-portal/agents': { status: 200, body: [] },
    'GET /applications/': { status: 200, body: [] },
  };
}

describe('ApplicationCreatePage', () => {
  it('o formulário do quadro, sem URL antes de criar', async () => {
    const api = fakeApplicationsApi({});
    renderApplications('/applications/new');

    expect(screen.getByRole('heading', { name: 'Nova aplicação externa' })).toBeInTheDocument();
    expect(
      screen.getByText('Até 80 caracteres, sem repetir o nome de outra aplicação.'),
    ).toBeInTheDocument();
    expect(screen.getByText('Opcional, até 280 caracteres.')).toBeInTheDocument();
    expect(
      screen.getByText(
        'O endereço em que a aplicação pede o token (client credentials) aparece depois de criar.',
      ),
    ).toBeInTheDocument();
    expect(
      screen.getByText('Criar a aplicação gera uma credencial. Confirme com a sua senha.'),
    ).toBeInTheDocument();
    expect(screen.getByText('sem nenhum agente liberado')).toBeInTheDocument();
    // "Sem URL antes de criar": nem o texto, nem a busca do emissor.
    expect(document.body.textContent).not.toContain('connect/token');
    expect(api.requests.some((r) => r.path.endsWith('/identity'))).toBe(false);
  });

  it('Senha vazia não envia', async () => {
    const api = fakeApplicationsApi({});
    const user = userEvent.setup();
    renderApplications('/applications/new');

    await preencherECriar(user, { senha: '' });

    expect(await screen.findByText('Informe a sua senha.')).toBeInTheDocument();
    expect(api.calls(CRIAR)).toHaveLength(0);
  });

  it.each([
    ['', 'Informe o nome.'],
    ['   ', 'Informe o nome.'],
    ['x'.repeat(81), 'O nome tem no máximo 80 caracteres.'],
  ])('o nome "%s" não envia: %s', async (nome, texto) => {
    const api = fakeApplicationsApi({});
    const user = userEvent.setup();
    renderApplications('/applications/new');

    if (nome) await user.type(screen.getByLabelText('Nome'), nome);
    await user.type(screen.getByLabelText('Sua senha'), 's');
    await user.click(screen.getByRole('button', { name: 'Criar aplicação' }));

    expect(await screen.findByText(texto)).toBeInTheDocument();
    expect(api.calls(CRIAR)).toHaveLength(0);
  });

  it('a descrição acima de 280 não envia', async () => {
    const api = fakeApplicationsApi({});
    const user = userEvent.setup();
    renderApplications('/applications/new');

    await user.click(screen.getByLabelText('Descrição'));
    await user.paste('d'.repeat(281));
    await preencherECriar(user);

    expect(
      await screen.findByText('A descrição tem no máximo 280 caracteres.'),
    ).toBeInTheDocument();
    expect(api.calls(CRIAR)).toHaveLength(0);
  });

  it('o corpo leva o nome aparado e a descrição vazia como null', async () => {
    const api = fakeApplicationsApi({ [CRIAR]: { status: 201, body: CRIADA } });
    const user = userEvent.setup();
    renderApplications('/applications/new');

    await preencherECriar(user, { nome: '  Portal do Parceiro  ' });

    await waitFor(() => expect(api.calls(CRIAR)).toHaveLength(1));
    expect(api.calls(CRIAR)[0].body).toEqual({
      nome: 'Portal do Parceiro',
      descricao: null,
      senha: 'senha-certa',
    });
  });

  it('o 400 do servidor cai no campo', async () => {
    fakeApplicationsApi({
      [CRIAR]: {
        status: 400,
        body: {
          title: 'Erros.',
          errors: { descricao: ['A descrição tem no máximo 280 caracteres.'] },
        },
      },
    });
    const user = userEvent.setup();
    renderApplications('/applications/new');

    await preencherECriar(user);

    expect(
      await screen.findByText('A descrição tem no máximo 280 caracteres.'),
    ).toBeInTheDocument();
  });

  it('Nome repetido', async () => {
    fakeApplicationsApi({ [CRIAR]: problem(409, 'Já existe uma aplicação com este nome.') });
    const user = userEvent.setup();
    renderApplications('/applications/new');

    await preencherECriar(user);

    expect(await screen.findByText('Já existe uma aplicação com este nome.')).toBeInTheDocument();
    expect(screen.getByLabelText('Sua senha')).toHaveValue('senha-certa');
  });

  it('Senha errada', async () => {
    const api = fakeApplicationsApi({ [CRIAR]: problem(401, 'Senha inválida.') });
    const user = userEvent.setup();
    renderApplications('/applications/new');

    await preencherECriar(user);

    expect(await screen.findByText('Senha inválida.')).toBeInTheDocument();
    expect(screen.getByLabelText('Sua senha')).toHaveValue('');
    expect(api.calls('POST /connect/token')).toHaveLength(0);
    expect(window.localStorage.getItem('buteco.sessao')).not.toBeNull();
  });

  it('Bloqueado', async () => {
    fakeApplicationsApi({ [CRIAR]: problem(423, 'Conta bloqueada.', { 'Retry-After': '300' }) });
    const user = userEvent.setup();
    renderApplications('/applications/new');

    await preencherECriar(user);

    expect(
      await screen.findByText(
        'Muitas tentativas sem sucesso. Por segurança, a sua conta fica pausada por 5 minutos.',
      ),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Criar aplicação' })).toBeDisabled();
  });

  it('o 403 sem corpo mostra o aviso de ação', async () => {
    fakeApplicationsApi({ [CRIAR]: { status: 403 } });
    const user = userEvent.setup();
    renderApplications('/applications/new');

    await preencherECriar(user);

    expect(await screen.findByText('Você não tem permissão para esta ação.')).toBeInTheDocument();
  });

  it('enviando mostra "Criando…"', async () => {
    fakeApplicationsApi({ [CRIAR]: () => new Promise(() => {}) });
    const user = userEvent.setup();
    renderApplications('/applications/new');

    await preencherECriar(user);

    expect(await screen.findByRole('button', { name: 'Criando…' })).toBeDisabled();
  });

  it('Os três valores com Copiar', async () => {
    fakeApplicationsApi(rotasDoDetalhe());
    const user = userEvent.setup();
    renderApplications('/applications/new');

    await preencherECriar(user);

    expect(await screen.findByRole('heading', { name: 'Aplicação criada' })).toBeInTheDocument();
    expect(screen.getByText('btc_app_7kq2m9x4')).toBeInTheDocument();
    expect(screen.getByText(VALOR_CRIADO)).toBeInTheDocument();
    expect(screen.getByText(CRIADA.endpointToken)).toBeInTheDocument();
    expect(screen.queryByLabelText('Nome')).not.toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Copiar segredo' }));

    expect(await navigator.clipboard.readText()).toBe(VALOR_CRIADO);
    expect(screen.getByRole('button', { name: 'Copiar segredo' })).toHaveTextContent('Copiado');
  });

  // GUARDA (tasks.md 4.8). Reprova com o `disabled` do botão sem a condição da caixa.
  it('Ir para a aplicação só depois da caixa', async () => {
    fakeApplicationsApi(rotasDoDetalhe());
    const user = userEvent.setup();
    const { router } = renderApplications('/applications/new');
    await preencherECriar(user);

    const ir = await screen.findByRole('button', { name: 'Ir para a aplicação' });
    expect(ir).toBeDisabled();
    await user.click(ir);
    expect(router.state.location.pathname).toBe('/applications/new');

    await user.click(screen.getByLabelText('Guardei o segredo em um local seguro'));
    await user.click(screen.getByRole('button', { name: 'Ir para a aplicação' }));

    await waitFor(() => expect(router.state.location.pathname).toBe('/applications/app-portal'));
  });

  it('Sair sem marcar não é bloqueado', async () => {
    fakeApplicationsApi(rotasDoDetalhe());
    const user = userEvent.setup();
    const { router } = renderApplications('/applications/new');
    await preencherECriar(user);
    await screen.findByRole('heading', { name: 'Aplicação criada' });

    await act(() => router.navigate('/users'));

    expect(await screen.findByText('fora de Aplicações')).toBeInTheDocument();
  });

  // GUARDA (tasks.md 4.5). Reprova, em duas rodadas: (a) com a criação por `useMutation`; (b) com
  // `setQueryData` do detalhe com a resposta do `201`.
  it('Nada nos caches do TanStack Query', async () => {
    fakeApplicationsApi(rotasDoDetalhe());
    const user = userEvent.setup();
    const { queryClient } = renderApplications('/applications/new');

    await preencherECriar(user);
    await screen.findByText(VALOR_CRIADO);

    expect(cachesSerializados(queryClient)).not.toContain('btc_sk_');
  });

  // GUARDA (tasks.md 4.6). Reprova com `navigate('/applications/{id}', { state: { segredo } })`.
  it('Nada no histórico nem no armazenamento', async () => {
    fakeApplicationsApi(rotasDoDetalhe());
    const user = userEvent.setup();
    const { router } = renderApplications('/applications/new');
    await preencherECriar(user);
    await screen.findByText(VALOR_CRIADO);

    await user.click(screen.getByLabelText('Guardei o segredo em um local seguro'));
    await user.click(screen.getByRole('button', { name: 'Ir para a aplicação' }));
    await screen.findByRole('heading', { name: 'Portal do Parceiro' });

    expect(JSON.stringify(router.state.location.state ?? null)).not.toContain('btc_sk_');
    expect(JSON.stringify(window.history.state ?? null)).not.toContain('btc_sk_');
    expect(JSON.stringify({ ...window.sessionStorage })).not.toContain('btc_sk_');
    expect(JSON.stringify({ ...window.localStorage })).not.toContain('btc_sk_');
  });

  // GUARDA (tasks.md 4.7). Reprova com o segredo guardado numa variável de módulo e lido na
  // montagem da página.
  it('Voltar não traz o segredo', async () => {
    fakeApplicationsApi(rotasDoDetalhe());
    const user = userEvent.setup();
    const { router } = renderApplications('/applications/new');
    await preencherECriar(user);
    await screen.findByText(VALOR_CRIADO);
    await user.click(screen.getByLabelText('Guardei o segredo em um local seguro'));
    await user.click(screen.getByRole('button', { name: 'Ir para a aplicação' }));
    await screen.findByRole('heading', { name: 'Portal do Parceiro' });

    await act(() => router.navigate(-1));

    expect(await screen.findByLabelText('Nome')).toHaveValue('');
    expect(screen.queryByRole('heading', { name: 'Aplicação criada' })).not.toBeInTheDocument();
    expect(document.body.textContent).not.toContain('btc_sk_');
  });
});
