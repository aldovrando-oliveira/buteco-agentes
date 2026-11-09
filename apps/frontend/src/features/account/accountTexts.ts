// Os textos de Minha conta que dependem de número ou que são do quadro, e não do servidor
// (design.md da change minha-conta-painel, D4 e D6; decisões do dono Q5, Q6 e Q10).

/**
 * O cartão da verificação sem cadastro e sem remoção (quadro `MinhaConta`, versão 55; design.md
 * da change verificacao-2-etapas-obrigatoria, D7).
 */
export const NAO_CONFIGURADA_TEXTO =
  'Este painel passou a exigir a verificação em 2 etapas. Na próxima vez que você entrar, o login pede o cadastro do app autenticador.';
/** Só um Admin redefine a verificação de outro (quadro `MinhaConta`, `m2`; change papeis-no-painel). */
export const TROCOU_DE_CELULAR =
  'Trocou de celular ou perdeu o app? Peça a um Admin do painel que redefina a sua verificação; no próximo login você cadastra o app de novo.';

/** O aviso de poucos códigos: de 1 a 3, com o singular para 1, e o texto próprio para 0 (Q5). */
export function fewCodesText(restantes: number): string | null {
  const gere = 'Gere novos para não ficar sem acesso se perder o celular.';
  if (restantes === 0) return `Não restam códigos de recuperação. ${gere}`;
  if (restantes === 1) return `Resta 1 código de recuperação. ${gere}`;
  if (restantes <= 3) return `Restam ${restantes} códigos de recuperação. ${gere}`;
  return null;
}

export function remainingText(restantes: number): string {
  return restantes === 1 ? '1 código de recuperação restante.' : `${restantes} códigos de recuperação restantes.`;
}

/** A confirmação de "Encerrar as outras": o quadro tem o plural; com uma só, o singular. */
export function confirmOthersText(quantas: number): string {
  return quantas === 1
    ? 'Encerrar a outra sessão? Quem estiver usando esse navegador precisa entrar de novo.'
    : `Encerrar as outras ${quantas} sessões? Quem estiver usando esses navegadores precisa entrar de novo.`;
}

/** O texto do `{ encerradas }` (Q10). */
export function endedText(encerradas: number): string {
  if (encerradas === 0) return 'Nenhuma outra sessão estava aberta.';
  if (encerradas === 1) return '1 sessão encerrada.';
  return `${encerradas} sessões encerradas.`;
}

