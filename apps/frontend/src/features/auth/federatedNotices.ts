import type { HttpFailure } from './api/sessionApi';
import { classify404, START_404_TITLES } from './api/federatedApi';
import { MUITAS_TENTATIVAS, SEM_CONEXAO } from './messages';

// Os textos do login e do aceite de convite pelo provedor (design.md da change telas-login-google,
// D1 a D3; quadros `Main`, `LoginRecusado` e `AceitarConvite`, versão 1794626941-64ac; textos fixados
// pelo dono em 14/11/2026, convenção 17).

export const REDIRECIONANDO = (provedor: string) => `Redirecionando para o ${provedor}…`;
export const OU_ENTRE_COM = 'ou entre com';
export const ENTRAR_COM = (provedor: string) => `Entrar com ${provedor}`;
export const ENTRAR_COM_OUTRA_CONTA = (provedor: string) => `Entrar com outra conta ${provedor}`;
export const CONTINUAR_COM = (provedor: string) => `Continuar com ${provedor}`;
export const ENTRE_COM_UMA_CONTA = (provedor: string) => `Entre com uma conta ${provedor}`;
export const A_CONTA_PRECISA_TER = 'A conta precisa ter o e-mail';
export const OU_CRIE_UMA_SENHA = 'ou crie uma senha';
export const ABRA_O_LINK_DE_NOVO = 'Abra o link do convite que você recebeu de novo.';

export const PROVEDOR_INDISPONIVEL =
  'Não foi possível falar com o provedor. Tente de novo em instantes.';
export const FALHA_COM_O_GOOGLE = 'Não foi possível entrar com o Google. Tente de novo.';

/** Os avisos verdes da volta do provedor no /entrar (D1). */
export const GOOGLE_CONFIRMOU_COM_CODIGO =
  'O Google confirmou a sua conta. Falta o código do app autenticador para entrar.';
export const GOOGLE_CONFIRMOU_COM_CADASTRO =
  'O Google confirmou a sua conta. Falta só cadastrar o app autenticador para entrar.';
export const CONVITE_ACEITO_COM_CADASTRO =
  'Convite aceito. Falta cadastrar o app autenticador para entrar.';

export type NoticeColor = 'red' | 'yellow' | 'blue';

export interface FederatedNotice {
  cor: NoticeColor;
  titulo?: string;
  texto: string;
}

/** O aviso de uma resposta recusada do início (login e convite), fora do `404` do link inválido. */
export function startFailureNotice(result: HttpFailure | { kind: 'network' }): FederatedNotice {
  if (result.kind === 'network') return { cor: 'red', texto: SEM_CONEXAO };
  if (result.status === 503) return { cor: 'red', texto: PROVEDOR_INDISPONIVEL };
  if (result.status === 429) return { cor: 'yellow', texto: MUITAS_TENTATIVAS };
  if (result.status === 404 && classify404(result) === 'provider-not-configured') {
    return { cor: 'red', texto: START_404_TITLES.providerNotConfigured };
  }
  return { cor: 'red', texto: FALHA_COM_O_GOOGLE };
}

const FALHA: FederatedNotice = { cor: 'red', texto: FALHA_COM_O_GOOGLE };

/** Os motivos do login em que o botão vira "Entrar com outra conta Google". */
export const LOGIN_OTHER_ACCOUNT = new Set([
  'sem-acesso',
  'email-nao-verificado',
  'dominio-nao-permitido',
]);

/** O aviso de cada motivo de recusa no /entrar (D2); o desconhecido é a falha (convenção 13). */
export function loginRefusalNotice(motivo: string, email: string | null): FederatedNotice {
  switch (motivo) {
    case 'sem-acesso':
      return {
        cor: 'red',
        titulo: 'Conta sem acesso',
        texto:
          email === null
            ? 'Essa conta Google não tem acesso ao Buteco Agentes. O acesso é liberado só por convite do administrador.'
            : `O Google confirmou ${email}, mas essa conta não tem acesso ao Buteco Agentes. O acesso é liberado só por convite do administrador.`,
      };
    case 'convite-pendente':
      return {
        cor: 'blue',
        titulo: 'Você tem um convite pendente',
        texto:
          'Abra o link do convite que você recebeu para ativar o acesso. Se o link expirou, peça um novo ao administrador.',
      };
    case 'nao-conectada':
      return {
        cor: 'yellow',
        titulo: 'Esta conta Google ainda não está conectada',
        texto:
          'Entre com seu e-mail e senha e conecte o Google em Minha conta → Formas de entrada. Depois disso, o botão do Google passa a funcionar.',
      };
    case 'desativado':
      return {
        cor: 'red',
        titulo: 'Acesso desativado',
        texto: 'Seu acesso ao Buteco Agentes foi desativado. Fale com o administrador do painel.',
      };
    case 'email-nao-verificado':
      return {
        cor: 'yellow',
        titulo: 'E-mail não confirmado',
        texto:
          'O Google não confirmou o e-mail desta conta. Entre com outra conta Google ou com e-mail e senha.',
      };
    case 'dominio-nao-permitido':
      return {
        cor: 'red',
        titulo: 'Domínio não permitido',
        texto: 'Esta conta Google não é do domínio permitido neste painel.',
      };
    default:
      return FALHA;
  }
}

/**
 * O aviso de cada motivo de recusa que volta ao convite (D3); o desconhecido é a falha. O
 * `outro-email` leva os dois e-mails; sem eles (a volta sem o token, ou o aviso já lido), a variante
 * sem e-mail.
 */
export function invitationRefusalNotice(
  motivo: string,
  emails: { provedor: string; convite: string } | null,
): FederatedNotice {
  switch (motivo) {
    case 'outro-email':
      return {
        cor: 'red',
        titulo: 'E-mail diferente do convite',
        texto:
          emails === null
            ? 'O Google confirmou outro e-mail, diferente do e-mail deste convite.'
            : `O Google confirmou ${emails.provedor}, mas este convite é para ${emails.convite}. Entre com a conta certa ou crie uma senha abaixo.`,
      };
    case 'email-nao-verificado':
      return {
        cor: 'yellow',
        titulo: 'E-mail não confirmado',
        texto:
          'O Google não confirmou o e-mail desta conta. Entre com outra conta Google ou crie uma senha abaixo.',
      };
    case 'em-uso':
      return {
        cor: 'red',
        titulo: 'Conta Google já conectada',
        texto:
          'Esta conta Google já está conectada a outro usuário do Buteco Agentes. Entre com outra conta ou crie uma senha abaixo.',
      };
    case 'dominio-nao-permitido':
      return {
        cor: 'red',
        titulo: 'Domínio não permitido',
        texto: 'Esta conta Google não é do domínio permitido neste painel.',
      };
    default:
      return FALHA;
  }
}
