import { formatShortDateTime } from '../../utils/panelDates';
import type { UserSummary } from './api/usersApi';

// Os textos da lista de Usuários que dependem do dado (requisito "Colunas da lista de
// Usuários"; design.md da change usuarios-convites-identidade-painel, D2 e D3). Valor que o
// painel não conhece sai cru e neutro, nunca no verde de sucesso (convenção 13).

export const INFORME_A_SENHA = 'Informe a sua senha.';

// O papel no convite e em "Dados e papel" (quadros `Usuarios` e `UsuarioDetalhe`, entrega `m2`,
// versão 58 do protótipo; change papeis-no-painel, D4). As descrições são as mesmas nos dois
// quadros desde a versão 58 (Q17).
export const DESCRICAO_DO_PAPEL = {
  admin: 'Tudo, inclusive usuários e Identidade.',
  operador: 'Agentes, MCP, conhecimento, canais e histórico de conversas. Não gerencia usuários e não vê Identidade.',
  leitor:
    'Vê agentes, MCP, conhecimento, canais e Insights. Não altera nada, não vê o histórico de conversas nem usa os conectores.',
} as const;
export const PAPEL_SEM_ESCOLHA = 'O que a pessoa pode fazer no painel. Dá para mudar depois em Editar usuário.';
export const ESCOLHA_O_PAPEL = 'Escolha o papel.';

export type Tom = 'green' | 'yellow' | 'gray' | 'neutral';

export interface Selo {
  texto: string;
  tom: Tom;
}

/** O selo da coluna Estado e a linha abaixo dele, com o vencido vindo do servidor. */
export function stateBadge(user: UserSummary, timeZone?: string): Selo & { abaixo?: string } {
  switch (user.estado) {
    case 'ativo':
      return { texto: 'Ativo', tom: 'green' };
    case 'desativado':
      return { texto: 'Desativado', tom: 'gray' };
    case 'convidado': {
      if (!user.convite) return { texto: 'Convite pendente', tom: 'yellow' };
      const quando = formatShortDateTime(user.convite.expiraEm, timeZone);
      return user.convite.vencido
        ? { texto: 'Convite vencido', tom: 'gray', abaixo: `venceu em ${quando}` }
        : { texto: 'Convite pendente', tom: 'yellow', abaixo: `vence em ${quando}` };
    }
    default:
      return { texto: user.estado, tom: 'neutral' };
  }
}

/** A coluna Verificação em 2 etapas: "—" para o convidado, que ainda não tem conta. */
export function twoFactorBadge(user: UserSummary): Selo {
  if (user.estado === 'convidado') return { texto: '—', tom: 'neutral' };
  return user.duasEtapas ? { texto: 'Ativa', tom: 'green' } : { texto: 'Não configurada', tom: 'yellow' };
}

/** Os chips de Formas de entrada; lista vazia é "—" (o convidado). */
export function signInForms(user: UserSummary): { texto: string; conhecido: boolean }[] {
  return user.formasDeEntrada.map((forma) =>
    forma === 'senha' ? { texto: 'Senha', conhecido: true } : { texto: forma, conhecido: false },
  );
}

export const conviteAceitoAoCancelar = (nome: string) =>
  `O convite de ${nome} já foi aceito, então não há o que cancelar. A lista foi atualizada.`;

/** O `409` do novo link (decisão do dono Q7): o texto do quadro `convite-aceito`, ajustado. */
export const conviteAceitoAoRenovar = (nome: string) =>
  `O convite de ${nome} já foi aceito, então não há link a gerar. A lista foi atualizada.`;
/** A confirmação de rebaixar a si mesmo (quadro `UsuarioDetalhe`, `acao=rebaixar-a-si-mesmo`). */
export const DEIXAR_DE_SER_ADMIN =
  'Deixar de ser Admin? Usuários e Identidade saem do seu menu agora, e você vai para Minha conta. Para voltar a ser Admin, outro Admin precisa promover você.';
