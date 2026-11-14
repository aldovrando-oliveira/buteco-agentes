import { formatDate } from '../../utils/panelDates';
import type { SigningKey } from './api/identityApi';

// Os textos da Identidade que saem da rota (requisitos "Formato das durações" e "Cartão Chaves
// de assinatura"; design.md da change usuarios-convites-identidade-painel, D6). Nenhuma
// duração é escrita à mão: as constantes do emissor são a fonte (#211).

const DIA = 86_400;
const HORA = 3_600;
const MINUTO = 60;

/** A maior unidade que divide exatamente: "1 dia", "N dias", "N h", "N min", "N s". */
export function formatDuration(segundos: number): string {
  if (segundos > 0 && segundos % DIA === 0) {
    const n = segundos / DIA;
    return n === 1 ? '1 dia' : `${n} dias`;
  }
  if (segundos > 0 && segundos % HORA === 0) return `${segundos / HORA} h`;
  if (segundos > 0 && segundos % MINUTO === 0) return `${segundos / MINUTO} min`;
  return `${segundos} s`;
}

export type KeyTone = 'green' | 'blue' | 'gray' | 'neutral';

/** O selo do estado da chave; valor desconhecido sai cru e neutro (convenção 13). */
export function keyBadge(estado: string): { texto: string; tom: KeyTone } {
  switch (estado) {
    case 'em-uso':
      return { texto: 'Em uso', tom: 'green' };
    case 'proxima':
      return { texto: 'Próxima', tom: 'blue' };
    case 'aposentando':
      return { texto: 'Aposentando', tom: 'gray' };
    default:
      return { texto: estado, tom: 'neutral' };
  }
}

/** A frase do "Próximo passo", com as datas da própria chave, no fuso do navegador. */
export function nextStepText(chave: SigningKey, timeZone?: string): string {
  const d = (iso: string) => formatDate(iso, timeZone);
  switch (chave.estado) {
    case 'em-uso':
      return `Assina desde ${d(chave.assinaDesde)}; é substituída em ${d(chave.assinaAte)}`;
    case 'proxima':
      return `Já publicada; passa a assinar em ${d(chave.assinaDesde)}`;
    case 'aposentando':
      return `Não assina desde ${d(chave.assinaAte)}; sai da publicação em ${d(chave.saiDaPublicacaoEm)}`;
    default:
      return '—';
  }
}

/** "Usado para" do provedor federado: `login` é o login de usuários; outro valor sai cru, neutro (convenção 13). */
export function usageLabel(uso: string): { texto: string; conhecido: boolean } {
  return uso === 'login' ? { texto: 'Login de usuários', conhecido: true } : { texto: uso, conhecido: false };
}

/** A coluna Vínculos: "1 usuário" no singular, "<n> usuários" no resto, inclusive 0 (Q8). */
export function linkCount(vinculos: number): string {
  return vinculos === 1 ? '1 usuário' : `${vinculos} usuários`;
}
