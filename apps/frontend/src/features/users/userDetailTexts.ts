import { formatDate, formatRelative } from '../../utils/panelDates';
import type { UserDetail } from './api/usersApi';

// Os textos de Editar usuário que dependem do dado (requisitos de `panel-user-detail-page`;
// design.md da change editar-usuario-painel, D3, D8 e D10). Os parâmetros de agora e de fuso
// existem para o teste os fixar.

/** "Convidado por … em dd/mm/aaaa · último acesso …", ou só a origem quando não há o resto. */
export function detailSubtitle(
  user: UserDetail,
  now: Date = new Date(),
  timeZone?: string,
): string {
  const origem = user.convidadoPor
    ? `Convidado por ${user.convidadoPor.nome} em ${formatDate(user.criadoEm, timeZone)}`
    : // Sem convite é o usuário criado na instalação. Com papéis, ele pode ter sido rebaixado, e
      // "administrador" diria um papel que talvez não seja o dele (change papeis-no-painel, D4).
      'Criado na instalação';
  let estado: string | null = null;
  if (user.estado === 'desativado') {
    estado = user.desativadoEm ? `desativado em ${formatDate(user.desativadoEm, timeZone)}` : null;
  } else if (user.ultimoAcesso) {
    estado = `último acesso ${formatRelative(user.ultimoAcesso, now, timeZone)}`;
  }
  return estado ? `${origem} · ${estado}` : origem;
}

/** "N sessões ativas", "1 sessão ativa" ou "Nenhuma sessão ativa". */
export function activeSessionsCount(ativas: number): string {
  if (ativas === 0) return 'Nenhuma sessão ativa';
  return ativas === 1 ? '1 sessão ativa' : `${ativas} sessões ativas`;
}

/**
 * A linha das sessões. Para o próprio, só a contagem: sem o último uso e sem "incluindo esta",
 * que pode ser falso com a sessão atual já revogada e o access token ainda valendo (decisão do
 * dono Q12).
 */
export function sessionsLine(
  user: UserDetail,
  proprio: boolean,
  now: Date = new Date(),
  timeZone?: string,
): string {
  const contagem = activeSessionsCount(user.sessoes.ativas);
  if (proprio || user.sessoes.ativas === 0 || !user.sessoes.ultimaAtividade) return contagem;
  return `${contagem} · último uso ${formatRelative(user.sessoes.ultimaAtividade, now, timeZone)}`;
}

export const confirmEndSessions = (ativas: number, nome: string) =>
  ativas === 1
    ? `Encerrar a sessão de ${nome}? A pessoa precisa entrar de novo.`
    : `Encerrar as ${ativas} sessões de ${nome}? A pessoa precisa entrar de novo em todos os navegadores.`;

/** O aviso depois de `POST …/sessions/revoke`, no molde da #246 (Q10) para o zero. */
export function endedSessions(encerradas: number): string {
  if (encerradas === 0) return 'Nenhuma sessão estava aberta.';
  return encerradas === 1 ? '1 sessão encerrada.' : `${encerradas} sessões encerradas.`;
}

export const confirmDeactivate = (nome: string) =>
  `Desativar ${nome}? A pessoa sai de todos os navegadores na hora e não consegue mais entrar. Você pode reativar depois, e ela vai precisar entrar de novo.`;

export const reactivated = (nome: string) =>
  `Reativação concluída. ${nome} pode entrar de novo com a senha que já tinha.`;

export const confirmResetTwoFactor = (nome: string) =>
  `Redefinir a verificação de ${nome}? O app autenticador e os códigos de recuperação deixam de valer, e a pessoa sai de todos os navegadores. No próximo login, ela cadastra o app autenticador de novo.`;
