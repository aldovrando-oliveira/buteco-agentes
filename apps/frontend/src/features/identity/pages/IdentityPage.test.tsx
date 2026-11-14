import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MantineProvider } from '@mantine/core';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Link, Route, Routes } from 'react-router';
import { theme } from '../../../theme';
import { signInForTest, signOutForTest } from '../../../test/authFakes';
import { authRuntime } from '../../../auth/session';
import { formatDate } from '../../../utils/panelDates';
import { fakeAccountApi as fakeApi, problem } from '../../account/test/fakeAccountApi';
import type { IdentityOverview, SigningKey } from '../api/identityApi';
import { IdentityPage } from './IdentityPage';

const EM_USO: SigningKey = {
  kid: 'fniY4qt8pBcSxBan9KihTw',
  estado: 'em-uso',
  criadaEm: '2026-10-13T15:00:00Z',
  assinaDesde: '2026-10-20T15:00:00Z',
  assinaAte: '2027-01-18T15:00:00Z',
  saiDaPublicacaoEm: '2027-02-02T15:00:00Z',
};
const APOSENTANDO: SigningKey = {
  kid: 'Jk3PzW8qLm2RtV6yXc1HdQ',
  estado: 'aposentando',
  criadaEm: '2026-07-15T15:00:00Z',
  assinaDesde: '2026-07-22T15:00:00Z',
  assinaAte: '2026-10-20T15:00:00Z',
  saiDaPublicacaoEm: '2026-11-04T15:00:00Z',
};
const PROXIMA: SigningKey = {
  kid: 'Q7rL2mXw9dKpT4vNc8sYbA',
  estado: 'proxima',
  criadaEm: '2027-01-11T15:00:00Z',
  assinaDesde: '2027-01-18T15:00:00Z',
  assinaAte: '2027-04-18T15:00:00Z',
  saiDaPublicacaoEm: '2027-05-03T15:00:00Z',
};

function identity(over: Partial<IdentityOverview['chavesDeAssinatura']> = {}, emissor: Partial<IdentityOverview['emissor']> = {}): IdentityOverview {
  return {
    emissor: {
      issuer: 'https://buteco.exemplo.test/',
      discovery: 'https://buteco.exemplo.test/.well-known/openid-configuration',
      jwks: 'https://buteco.exemplo.test/.well-known/jwks',
      token: 'https://buteco.exemplo.test/connect/token',
      validadeDoTokenSegundos: 900,
      sessaoSemAtividadeSegundos: 1_209_600,
      ...emissor,
    },
    chavesDeAssinatura: {
      intervaloDeTrocaSegundos: 7_776_000,
      prePublicacaoSegundos: 604_800,
      retencaoSegundos: 1_296_000,
      verificacaoSegundos: 3_600,
      chaves: [EM_USO, APOSENTANDO],
      ...over,
    },
  };
}

function renderIdentity() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={['/identity']}>
          <Routes>
            <Route
              path="/identity"
              element={
                <>
                  <Link to="/inventory">sair</Link>
                  <IdentityPage />
                </>
              }
            />
            <Route path="/inventory" element={<p>fora da Identidade</p>} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>
    </MantineProvider>,
  );
  return queryClient;
}

const linhas = () => screen.getAllByRole('row').slice(1);

beforeEach(() => {
  signInForTest();
  vi.spyOn(authRuntime, 'assign').mockImplementation(() => {});
});

afterEach(() => {
  vi.useRealTimers();
  signOutForTest();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe('IdentityPage', () => {
  it('os dois cartões, sem provedores federados', async () => {
    fakeApi({ 'GET /identity': { status: 200, body: identity() } });
    renderIdentity();

    expect(screen.getByRole('heading', { name: 'Identidade', level: 2 })).toBeInTheDocument();
    expect(
      // O subtítulo da M3 (lista do topo da change telas-contas-federadas, Q5).
      screen.getByText('O Buteco emite os tokens de usuários, aplicações e módulos. Provedores federados são opcionais: sem nenhum, o painel funciona só com usuários locais.'),
    ).toBeInTheDocument();
    const emissor = await screen.findByText('Emissor do Buteco');
    const chaves = screen.getByText('Chaves de assinatura');
    expect(emissor.compareDocumentPosition(chaves) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    // Sem provedor configurado (a lista vazia do duplo padrão), o cartão da M3 no estado vazio (D7).
    expect(screen.getByText('Provedores federados')).toBeInTheDocument();
    expect(await screen.findByText('Nenhum provedor federado configurado. O login fica só com e-mail e senha.')).toBeInTheDocument();
  });

  it('carregando', () => {
    fakeApi({ 'GET /identity': () => new Promise(() => {}) });
    renderIdentity();

    expect(screen.getByLabelText('Carregando os dados do emissor')).toHaveAttribute('aria-busy', 'true');
  });

  it('o emissor com os valores de hoje', async () => {
    fakeApi({ 'GET /identity': { status: 200, body: identity() } });
    renderIdentity();

    expect(await screen.findByText('Operando')).toBeInTheDocument();
    expect(screen.getByText('https://buteco.exemplo.test/')).toBeInTheDocument();
    expect(screen.getByText('https://buteco.exemplo.test/connect/token')).toBeInTheDocument();
    expect(screen.getByText('15 min para usuários e módulos')).toBeInTheDocument();
    expect(screen.getByText('Renovada enquanto houver uso; termina depois de 14 dias sem atividade')).toBeInTheDocument();
  });

  it('a validade segue a rota', async () => {
    fakeApi({ 'GET /identity': { status: 200, body: identity({}, { validadeDoTokenSegundos: 600 }) } });
    renderIdentity();

    expect(await screen.findByText('10 min para usuários e módulos')).toBeInTheDocument();
  });

  it('as durações da política', async () => {
    fakeApi({ 'GET /identity': { status: 200, body: identity() } });
    renderIdentity();

    expect(await screen.findByText('Troca automática a cada 90 dias')).toBeInTheDocument();
    const texto = screen.getByText(/O Buteco assina cada token/);
    expect(texto).toHaveTextContent('aparece 7 dias antes');
    expect(texto).toHaveTextContent('por 15 dias depois');
    expect(texto).toHaveTextContent('em até 1 h depois');
  });

  it('a troca segue a rota', async () => {
    fakeApi({ 'GET /identity': { status: 200, body: identity({ intervaloDeTrocaSegundos: 2_592_000 }) } });
    renderIdentity();

    expect(await screen.findByText('Troca automática a cada 30 dias')).toBeInTheDocument();
  });

  it('depois da troca', async () => {
    fakeApi({ 'GET /identity': { status: 200, body: identity() } });
    renderIdentity();

    await screen.findByText('Em uso');
    const [primeira, segunda] = linhas();
    expect(within(primeira).getByText('Em uso')).toBeInTheDocument();
    expect(primeira).toHaveTextContent(`Assina desde ${formatDate(EM_USO.assinaDesde)}; é substituída em ${formatDate(EM_USO.assinaAte)}`);
    expect(primeira).toHaveTextContent(formatDate(EM_USO.criadaEm));
    expect(within(segunda).getByText('Aposentando')).toBeInTheDocument();
    expect(segunda).toHaveTextContent(
      `Não assina desde ${formatDate(APOSENTANDO.assinaAte)}; sai da publicação em ${formatDate(APOSENTANDO.saiDaPublicacaoEm)}`,
    );
  });

  it('antes da troca', async () => {
    fakeApi({ 'GET /identity': { status: 200, body: identity({ chaves: [EM_USO, PROXIMA] }) } });
    renderIdentity();

    await screen.findByText('Próxima');
    expect(linhas()[1]).toHaveTextContent(`Já publicada; passa a assinar em ${formatDate(PROXIMA.assinaDesde)}`);
  });

  it('só a chave em uso', async () => {
    fakeApi({ 'GET /identity': { status: 200, body: identity({ chaves: [EM_USO] }) } });
    renderIdentity();

    await screen.findByText('Em uso');
    expect(linhas()).toHaveLength(1);
  });

  it('a ordem é a da resposta', async () => {
    fakeApi({ 'GET /identity': { status: 200, body: identity({ chaves: [APOSENTANDO, EM_USO] }) } });
    renderIdentity();

    await screen.findByText('Em uso');
    expect(within(linhas()[0]).getByText('Aposentando')).toBeInTheDocument();
  });

  it('estado desconhecido', async () => {
    fakeApi({ 'GET /identity': { status: 200, body: identity({ chaves: [{ ...EM_USO, estado: 'revogada' }] }) } });
    renderIdentity();

    const selo = await screen.findByText('revogada');
    expect(selo.closest('[data-tom]')).toHaveAttribute('data-tom', 'neutral');
    expect(linhas()[0]).toHaveTextContent('—');
  });

  it('indisponível', async () => {
    fakeApi({ 'GET /identity': problem(503, 'Indisponível.') });
    renderIdentity();

    const alerta = await screen.findByRole('alert');
    expect(within(alerta).getByText('Indisponível')).toBeInTheDocument();
    expect(alerta).toHaveTextContent(
      'Não foi possível carregar os dados do emissor agora. O painel tenta de novo sozinho; se continuar, confira se o api está no ar.',
    );
    expect(within(alerta).getByRole('button', { name: 'Tentar de novo' })).toBeInTheDocument();
    expect(screen.queryByText('Operando')).not.toBeInTheDocument();
    expect(screen.queryByText('Chaves de assinatura')).not.toBeInTheDocument();
  });

  it('Tentar de novo chama na hora', async () => {
    const user = userEvent.setup();
    const api = fakeApi({ 'GET /identity': ['rede', { status: 200, body: identity() }] });
    renderIdentity();

    await user.click(await screen.findByRole('button', { name: 'Tentar de novo' }));

    expect(await screen.findByText('Operando')).toBeInTheDocument();
    expect(api.calls('GET /identity')).toHaveLength(2);
  });

  it('tenta de novo sozinha e para no 200', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const api = fakeApi({ 'GET /identity': ['rede', { status: 200, body: identity() }] });
    renderIdentity();

    await screen.findByRole('alert');
    expect(api.calls('GET /identity')).toHaveLength(1);

    await act(() => vi.advanceTimersByTimeAsync(30_000));
    expect(await screen.findByText('Operando')).toBeInTheDocument();
    expect(api.calls('GET /identity')).toHaveLength(2);

    // Depois do 200, nenhuma chamada nova acontece com o passar do tempo.
    await act(() => vi.advanceTimersByTimeAsync(120_000));
    expect(api.calls('GET /identity')).toHaveLength(2);
  });

  it('sair da página para o polling', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    const api = fakeApi({ 'GET /identity': 'rede' });
    renderIdentity();

    await screen.findByRole('alert');
    await user.click(screen.getByRole('link', { name: 'sair' }));
    expect(screen.getByText('fora da Identidade')).toBeInTheDocument();

    await act(() => vi.advanceTimersByTimeAsync(120_000));
    await waitFor(() => expect(api.calls('GET /identity')).toHaveLength(1));
  });
});

// O cartão "Provedores federados" (spec `panel-identity-page`, "Cartão Provedores federados"; design.md
// da change telas-contas-federadas, D7; quadro `Provedores`, `entrega=m3`, prop `federados`, versão 81).
// Os casos marcados GUARDA são os da 10.3 e da 10.4 do tasks.md.

const PROVEDORES = 'GET /identity/federated-providers';
const GOOGLE = {
  chave: 'corporativo',
  tipo: 'google',
  nome: 'Google',
  issuer: 'https://accounts.google.com/',
  usadoPara: ['login'],
  clientId: '412893650127-k3v9.apps.googleusercontent.com',
  uriDeRetorno: 'https://buteco.exemplo.test/auth/federated/callback',
  vinculos: 3,
};
const comProvedores = (...provedores: unknown[]) => ({ status: 200, body: { provedores } });
const cartaoDosProvedores = async () =>
  (await screen.findByText('Provedores federados')).closest('.mantine-Paper-root') as HTMLElement;

describe('Cartão Provedores federados', () => {
  it('A lista', async () => {
    fakeApi({ 'GET /identity': { status: 200, body: identity() }, [PROVEDORES]: comProvedores(GOOGLE) });
    renderIdentity();

    const card = await cartaoDosProvedores();
    const linha = (await within(card).findByText('corporativo')).closest('tr') as HTMLElement;
    expect(within(linha).getByText('Google')).toBeInTheDocument();
    expect(within(linha).getByText('Login de usuários')).toBeInTheDocument();
    expect(within(linha).getByText('https://accounts.google.com/')).toBeInTheDocument();
    expect(within(linha).getByText('412893650127-k3v9.apps.googleusercontent.com')).toBeInTheDocument();
    expect(within(linha).getByText('https://buteco.exemplo.test/auth/federated/callback')).toBeInTheDocument();
    expect(within(linha).getByText('3 usuários')).toBeInTheDocument();
    expect(within(card).getByText('Definidos na configuração do servidor')).toBeInTheDocument();
    expect(
      within(card).getByText(
        'Quem entra pelo Google precisa estar ligado a um usuário do Buteco, pelo aceite de um convite ou conectando a conta em Minha conta. O Google confirma a identidade; o Buteco emite a sessão e pede o próprio código da verificação em 2 etapas. Vínculos é o número de usuários com uma conta conectada a este provedor.',
      ),
    ).toBeInTheDocument();
  });

  // GUARDA 10.4: o plural no 1.
  it('Um vínculo no singular', async () => {
    fakeApi({ 'GET /identity': { status: 200, body: identity() }, [PROVEDORES]: comProvedores({ ...GOOGLE, vinculos: 1 }) });
    renderIdentity();

    expect(await within(await cartaoDosProvedores()).findByText('1 usuário')).toBeInTheDocument();
  });

  it('Nenhum vínculo', async () => {
    fakeApi({ 'GET /identity': { status: 200, body: identity() }, [PROVEDORES]: comProvedores({ ...GOOGLE, vinculos: 0 }) });
    renderIdentity();

    expect(await within(await cartaoDosProvedores()).findByText('0 usuários')).toBeInTheDocument();
  });

  it('Uso desconhecido sai cru', async () => {
    fakeApi({
      'GET /identity': { status: 200, body: identity() },
      [PROVEDORES]: comProvedores({ ...GOOGLE, usadoPara: ['aplicacoes'] }),
    });
    renderIdentity();

    const card = await cartaoDosProvedores();
    expect(await within(card).findByText('aplicacoes')).toBeInTheDocument();
    expect(within(card).queryByText('Login de usuários')).not.toBeInTheDocument();
  });

  it('Carregando os provedores', async () => {
    fakeApi({ 'GET /identity': { status: 200, body: identity() }, [PROVEDORES]: () => new Promise(() => {}) });
    renderIdentity();

    const card = await cartaoDosProvedores();
    expect(within(card).getByRole('status')).toHaveTextContent('Carregando os provedores…');
  });

  it('Erro com Tentar de novo', async () => {
    const user = userEvent.setup();
    const api = fakeApi({ 'GET /identity': { status: 200, body: identity() }, [PROVEDORES]: ['rede', comProvedores(GOOGLE)] });
    renderIdentity();

    const card = await cartaoDosProvedores();
    const alerta = await within(card).findByRole('alert');
    expect(alerta).toHaveTextContent('Não foi possível carregar os provedores federados agora.');
    await user.click(within(alerta).getByRole('button', { name: 'Tentar de novo' }));

    expect(await within(card).findByText('corporativo')).toBeInTheDocument();
    expect(api.calls(PROVEDORES)).toHaveLength(2);
  });

  // GUARDA 10.3: uma consulta só (ou o erro de uma no lugar da página) esconderia o emissor.
  it('O erro dos provedores não derruba o emissor', async () => {
    fakeApi({ 'GET /identity': { status: 200, body: identity() }, [PROVEDORES]: 'rede' });
    renderIdentity();

    expect(await screen.findByText('Operando')).toBeInTheDocument();
    expect(screen.getByText('Chaves de assinatura')).toBeInTheDocument();
    expect(await within(await cartaoDosProvedores()).findByRole('alert')).toHaveTextContent(
      'Não foi possível carregar os provedores federados agora.',
    );
  });

  it('O erro do emissor não derruba os provedores', async () => {
    fakeApi({ 'GET /identity': problem(503, 'Indisponível.'), [PROVEDORES]: comProvedores(GOOGLE) });
    renderIdentity();

    expect(await screen.findByText('Não foi possível carregar os dados do emissor agora. O painel tenta de novo sozinho; se continuar, confira se o api está no ar.')).toBeInTheDocument();
    expect(await within(await cartaoDosProvedores()).findByText('corporativo')).toBeInTheDocument();
  });

  it('Só leitura', async () => {
    fakeApi({ 'GET /identity': { status: 200, body: identity() }, [PROVEDORES]: comProvedores(GOOGLE) });
    renderIdentity();

    const card = await cartaoDosProvedores();
    await within(card).findByText('corporativo');
    expect(within(card).queryByRole('button', { name: 'Adicionar provedor' })).not.toBeInTheDocument();
    expect(within(card).queryByRole('button', { name: 'Editar' })).not.toBeInTheDocument();
    expect(within(card).queryByRole('columnheader', { name: 'Chaves' })).not.toBeInTheDocument();
  });
});

