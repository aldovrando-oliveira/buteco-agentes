import type { UserDetail, UserSummary } from '../api/usersApi';

// O `fetch` falso por caminho é o de Minha conta, que é genérico (método e caminho, fila de
// respostas, o `sessionFetch` real por cima). Usuários só acrescenta os dados.
export { fakeAccountApi as fakeUsersApi, problem, type FakeReply } from '../../account/test/fakeAccountApi';

export const ATIVO: UserSummary = {
  id: 'u-ana',
  nome: 'Ana Souza',
  email: 'ana@exemplo.test',
  estado: 'ativo',
  papel: 'admin',
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
  papel: 'leitor',
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

// O detalhe de Editar usuário (change editar-usuario-painel). O `sub` da sessão de teste é
// `PROPRIO_ID`, e `RAFAEL` tem o mesmo nome e e-mail da sessão, para o reconhecimento pelo
// `sub`, e não pelo e-mail, ter caso que o distinga.

export const PROPRIO_ID = 'u-aldo';

export const RAFAEL: UserDetail = {
  id: 'u-rafael',
  nome: 'Rafael Duarte',
  email: 'rafael.duarte@empresa.com',
  estado: 'ativo',
  papel: 'operador',
  formasDeEntrada: ['senha'],
  duasEtapas: true,
  ultimoAcesso: '2026-11-06T21:40:00Z', // ontem, 18:40 em São Paulo
  criadoEm: '2026-08-12T13:00:00Z',
  convite: null,
  desativadoEm: null,
  convidadoPor: { id: PROPRIO_ID, nome: 'Aldovrando Oliveira' },
  sessoes: { ativas: 2, ultimaAtividade: '2026-11-06T21:45:00Z' },
  contasConectadas: [],
};

export const BRUNO: UserDetail = {
  ...RAFAEL,
  id: 'u-bruno',
  nome: 'Bruno Teixeira',
  email: 'bruno.teixeira@empresa.com',
  estado: 'desativado',
  duasEtapas: false,
  ultimoAcesso: '2026-09-19T12:00:00Z',
  criadoEm: '2026-06-03T13:00:00Z',
  desativadoEm: '2026-09-20T13:00:00Z',
  sessoes: { ativas: 0, ultimaAtividade: null },
};

export const PROPRIO: UserDetail = {
  ...RAFAEL,
  id: PROPRIO_ID,
  papel: 'admin',
  nome: 'Aldovrando Oliveira',
  email: 'aldovrando@empresa.com',
  convidadoPor: null,
  sessoes: { ativas: 3, ultimaAtividade: '2026-11-07T17:55:00Z' },
};

export const JOANA_DETALHE: UserDetail = {
  ...CONVIDADA,
  desativadoEm: null,
  convidadoPor: { id: PROPRIO_ID, nome: 'Aldovrando Oliveira' },
  sessoes: { ativas: 0, ultimaAtividade: null },
  contasConectadas: [],
};

export const RESET_LINK =
  'https://buteco.exemplo.test/redefinir-senha#Hq8vN3pT6wYc2LxRm5Kd9Fj1Zs4Gb7Ua0Ve';
