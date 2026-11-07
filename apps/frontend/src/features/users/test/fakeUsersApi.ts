import type { UserSummary } from '../api/usersApi';

// O `fetch` falso por caminho é o de Minha conta, que é genérico (método e caminho, fila de
// respostas, o `sessionFetch` real por cima). Usuários só acrescenta os dados.
export { fakeAccountApi as fakeUsersApi, problem, type FakeReply } from '../../account/test/fakeAccountApi';

export const ATIVO: UserSummary = {
  id: 'u-ana',
  nome: 'Ana Souza',
  email: 'ana@exemplo.test',
  estado: 'ativo',
  formasDeEntrada: ['senha'],
  duasEtapas: true,
  ultimoAcesso: '2026-11-06T21:40:00Z', // ontem, 18:40 em São Paulo, com o agora de NOW
  criadoEm: '2026-08-12T13:00:00Z',
  convite: null,
};

export const DESATIVADO: UserSummary = {
  ...ATIVO,
  id: 'u-bruno',
  nome: 'Bruno Teixeira',
  email: 'bruno@exemplo.test',
  estado: 'desativado',
  duasEtapas: false,
  ultimoAcesso: '2026-09-20T12:00:00Z',
};

export const CONVIDADA: UserSummary = {
  id: 'u-joana',
  nome: 'Joana Prado',
  email: 'joana@exemplo.test',
  estado: 'convidado',
  formasDeEntrada: [],
  duasEtapas: false,
  ultimoAcesso: null,
  criadoEm: '2026-11-07T21:02:00Z',
  convite: { expiraEm: '2026-11-10T21:02:00Z', vencido: false },
};

export const VENCIDO: UserSummary = {
  ...CONVIDADA,
  id: 'u-paulo',
  nome: 'Paulo Souza',
  email: 'paulo@exemplo.test',
  convite: { expiraEm: '2026-10-20T12:15:00Z', vencido: true },
};

/** "Agora" dos testes: 07/11/2026, 15:00 em São Paulo. */
export const NOW = new Date('2026-11-07T18:00:00Z');

export const LINK = 'https://buteco.exemplo.test/convite#k7Qp2vX9mRt4LwZ8nB3cY6dF1gH5jK0sQ2wE';
