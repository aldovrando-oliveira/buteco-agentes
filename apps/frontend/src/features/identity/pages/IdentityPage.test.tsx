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
      screen.getByText('O Buteco emite os tokens de usuários e módulos, e é com estes dados que outros sistemas conferem se um token é legítimo.'),
    ).toBeInTheDocument();
    const emissor = await screen.findByText('Emissor do Buteco');
    const chaves = screen.getByText('Chaves de assinatura');
    expect(emissor.compareDocumentPosition(chaves) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(screen.queryByText('Provedores federados')).not.toBeInTheDocument();
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
