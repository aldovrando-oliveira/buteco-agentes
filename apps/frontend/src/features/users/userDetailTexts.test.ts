import { describe, expect, it } from 'vitest';
import { BRUNO, PROPRIO, RAFAEL } from './test/fakeUsersApi';
import { stateBadge } from './usersTexts';
import {
  activeSessionsCount,
  confirmEndSessions,
  detailSubtitle,
  endedSessions,
  sessionsLine,
} from './userDetailTexts';

// Os textos de Editar usuário (requisitos "Cabeçalho de Editar usuário" e "Sessões de outro
// usuário"; design.md da change editar-usuario-painel, D3 e D10). Agora e fuso fixos: 07/11/2026,
// 15:00 em São Paulo.
const NOW = new Date('2026-11-07T18:00:00Z');
const TZ = 'America/Sao_Paulo';

describe('detailSubtitle', () => {
  it('convidado por, com o último acesso no formato de Minha conta', () => {
    expect(detailSubtitle(RAFAEL, NOW, TZ)).toBe(
      'Convidado por Aldovrando Oliveira em 12/08/2026 · último acesso ontem, 18:40',
    );
  });

  it('o desativado mostra a data da desativação, e não o último acesso', () => {
    expect(detailSubtitle(BRUNO, NOW, TZ)).toBe(
      'Convidado por Aldovrando Oliveira em 03/06/2026 · desativado em 20/09/2026',
    );
  });

  it('sem convidadoPor é o primeiro administrador', () => {
    expect(detailSubtitle({ ...PROPRIO, ultimoAcesso: '2026-11-07T17:14:00Z' }, NOW, TZ)).toBe(
      'Primeiro administrador, criado na instalação · último acesso hoje, 14:14',
    );
  });

  it('sem último acesso, só a origem', () => {
    expect(detailSubtitle({ ...RAFAEL, ultimoAcesso: null }, NOW, TZ)).toBe(
      'Convidado por Aldovrando Oliveira em 12/08/2026',
    );
  });
});

describe('o estado desconhecido', () => {
  it('sai cru e neutro, e não em verde', () => {
    expect(stateBadge({ ...RAFAEL, estado: 'suspenso' })).toEqual({ texto: 'suspenso', tom: 'neutral' });
  });
});

describe('sessões', () => {
  it('contagem no plural, no singular e no zero', () => {
    expect(activeSessionsCount(2)).toBe('2 sessões ativas');
    expect(activeSessionsCount(1)).toBe('1 sessão ativa');
    expect(activeSessionsCount(0)).toBe('Nenhuma sessão ativa');
  });

  it('a linha de outro usuário traz o último uso; sem ele, só a contagem', () => {
    expect(sessionsLine(RAFAEL, false, NOW, TZ)).toBe('2 sessões ativas · último uso ontem, 18:45');
    expect(sessionsLine({ ...RAFAEL, sessoes: { ativas: 1, ultimaAtividade: null } }, false, NOW, TZ)).toBe(
      '1 sessão ativa',
    );
  });

  it('as sessões do próprio sem a dedução', () => {
    // Decisão do dono Q12: nem "incluindo esta", nem o último uso.
    expect(sessionsLine(PROPRIO, true, NOW, TZ)).toBe('3 sessões ativas');
    expect(sessionsLine({ ...PROPRIO, sessoes: { ativas: 1, ultimaAtividade: null } }, true, NOW, TZ)).toBe(
      '1 sessão ativa',
    );
  });

  it('a confirmação e o aviso de encerramento, com o singular e o zero', () => {
    expect(confirmEndSessions(2, 'Rafael Duarte')).toBe(
      'Encerrar as 2 sessões de Rafael Duarte? A pessoa precisa entrar de novo em todos os navegadores.',
    );
    expect(confirmEndSessions(1, 'Rafael Duarte')).toBe(
      'Encerrar a sessão de Rafael Duarte? A pessoa precisa entrar de novo.',
    );
    expect(endedSessions(2)).toBe('2 sessões encerradas.');
    expect(endedSessions(1)).toBe('1 sessão encerrada.');
    expect(endedSessions(0)).toBe('Nenhuma sessão estava aberta.');
  });
});
