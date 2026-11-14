import { KNOWN_PROVIDER_TYPES } from '../auth/api/federatedApi';
import type { ConnectedAccount } from './api/accountApi';

// Os textos e as contas das formas de entrada (spec `panel-federated-accounts`; design.md da change
// telas-contas-federadas, D3 a D5; quadros `MinhaConta` e `UsuarioDetalhe`, `entrega=m3`, versão 81).

export interface FormsNotice {
  cor: 'green' | 'red' | 'yellow';
  titulo?: string;
  texto: string;
}

export const AVISO_CONECTADA: FormsNotice = {
  cor: 'green',
  texto: 'Conta Google conectada. Da próxima vez, você também pode entrar com o Google.',
};

/** "Conta Google desconectada." sem a segunda frase: seria falsa para quem não tem senha (Q4). */
export const AVISO_DESCONECTADA: FormsNotice = {
  cor: 'green',
  texto: 'Conta Google desconectada.',
};

export const FALHA_AO_CONECTAR = 'Não foi possível conectar a conta Google. Tente de novo.';

const RECUSAS: Record<string, FormsNotice> = {
  'em-uso': {
    cor: 'red',
    titulo: 'Conta Google já conectada',
    texto:
      'Esta conta Google já está conectada a outro usuário do Buteco Agentes. Escolha outra conta.',
  },
  'ja-conectado': {
    cor: 'yellow',
    titulo: 'Você já tem uma conta Google conectada',
    texto: 'Para trocar de conta, desconecte a atual e conecte a outra.',
  },
  // O motivo sai quando a CONTA muda (o stamp: senha, verificação, desconectar pelo Admin,
  // desativação), e não a sessão do painel (`FederatedLoginEndpoints.cs`, o `ConnectAsync`; Q3).
  'sessao-expirada': {
    cor: 'yellow',
    titulo: 'A conexão expirou',
    texto:
      'A sua conta mudou enquanto você estava no Google: a senha, a verificação ou o acesso. Tente conectar de novo.',
  },
  'dominio-nao-permitido': {
    cor: 'red',
    titulo: 'Domínio não permitido',
    texto: 'Esta conta Google não é do domínio permitido neste painel.',
  },
};

/** O aviso de uma recusa da volta. `falha` e todo motivo fora da tabela são a falha (convenção 13). */
export function refusalNotice(motivo: string): FormsNotice {
  return RECUSAS[motivo] ?? { cor: 'red', texto: FALHA_AO_CONECTAR };
}

/** O rótulo da Forma: o do tipo conhecido ("Google"), ou o tipo cru. */
export function providerLabel(tipo: string): string {
  return KNOWN_PROVIDER_TYPES[tipo] ?? tipo;
}

/**
 * Quantas formas de entrada a pessoa tem, como o backend conta (`FederatedSignInForms.Count`): a
 * senha, e cada conta de provedor AINDA configurado. O vínculo fora da configuração (`disponivel`
 * falso) não conta: ele não entra, e a rota não o remove (Q9). Com 1, a última não sai (Q8).
 */
export function signInFormCount(formasDeEntrada: string[], contas: ConnectedAccount[]): number {
  return (
    (formasDeEntrada.includes('senha') ? 1 : 0) + contas.filter((conta) => conta.disponivel).length
  );
}

export const ULTIMA_FORMA = 'A última forma de entrada não pode ser removida.';
export const NAO_CONECTADA_A_ESSE_PROVEDOR = 'Esta conta não está conectada a esse provedor.';
