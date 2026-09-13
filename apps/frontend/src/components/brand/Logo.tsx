import MarkSvg from '../../assets/brand/favicon-tinta.svg?react';
import SymbolDuotoneSvg from '../../assets/brand/simbolo-duotone.svg?react';
import SymbolAccentSvg from '../../assets/brand/simbolo-acento.svg?react';
import LockupVerticalSvg from '../../assets/brand/lockup-vertical-duotone.svg?react';

type LogoVariant = 'mark' | 'symbol' | 'vertical';

type LogoProps = {
  /**
   * `mark` é só a cabeça, com os olhos maiores — a peça desenhada para os
   * tamanhos pequenos. `symbol` é a peça principal, cabeça e gravata.
   *
   * A cabeça vem da peça de tinta, não da de acento: no símbolo completo a
   * cabeça é tinta e só a gravata é acento, e o `mark` é a cabeça (D10).
   *
   * `vertical` é o lockup — símbolo sobre o nome do produto, com o espaçamento
   * óptico e a escala tipográfica desenhados no pacote de marca. O nome é
   * `<text>` em IBM Plex Sans 600, que resolve porque o SVG é inlineado no DOM
   * e alcança as @font-face de `src/fonts.ts` (D11).
   */
  variant?: LogoVariant;
  /** Altura de renderização em px. */
  size?: number;
};

// Limiares do manual da marca (docs/marca/MANUAL.md). Abaixo de 32px a gravata
// fica com menos de 3px e as duas cores deixam de construir forma; abaixo de
// 24px a gravata sai de vez e o desenho precisa dos olhos maiores.
const DUOTONE_MIN = 32;
const SYMBOL_MIN = 24;

// No lockup vertical o símbolo ocupa 120 das 200 unidades de altura do
// viewBox. Abaixo de 54px de altura total a gravata dele cruza o limiar do
// duotone, e não há lockup de cor única que leia os tokens do tema — os
// `*-mono-*` do pacote são #000 e #fff cravados. Nesse caso o lockup dá lugar
// ao símbolo, que tem a cadeia de degradação completa.
const LOCKUP_SYMBOL_RATIO = 120 / 200;
const VERTICAL_MIN = Math.ceil(DUOTONE_MIN / LOCKUP_SYMBOL_RATIO);

/**
 * A marca do produto. É o único lugar do painel autorizado a desenhar o
 * símbolo — nenhuma tela declara a geometria por conta própria
 * (specs/frontend-brand-identity).
 *
 * A cor não é decisão de quem chama: a tinta vem de `--buteco-brand-ink` e o
 * acento de `--mantine-primary-color-filled`, ambos trocando junto com o
 * esquema de cor. O `vite.config.ts` reescreve as duas cores literais dos SVGs
 * para esses tokens em tempo de build (design.md, D2).
 *
 * Renderiza sempre fora da árvore de acessibilidade — inclusive o lockup, que
 * desenha o nome. Quem exibe a marca é que fornece o nome acessível: ao lado,
 * em texto HTML visível, ou dentro de um <VisuallyHidden> quando o nome já
 * está desenhado. Anunciá-lo duas vezes seria regressão (D9, D11).
 */
export function Logo({ variant = 'mark', size = 24 }: LogoProps) {
  // A guarda vive aqui, e não na decisão de cada tela, porque é a única regra
  // do manual que uma tela viola sem perceber — um `size` reduzido numa lista
  // densa, meses depois, quebra o duotone em silêncio (D8).
  const asked = variant === 'vertical' && size < VERTICAL_MIN ? 'symbol' : variant;
  const piece = asked !== 'vertical' && size < SYMBOL_MIN ? 'mark' : asked;

  const Svg =
    piece === 'vertical'
      ? LockupVerticalSvg
      : piece === 'mark'
        ? MarkSvg
        : size < DUOTONE_MIN
          ? SymbolAccentSvg
          : SymbolDuotoneSvg;

  return (
    <Svg
      height={size}
      width={undefined}
      role={undefined}
      aria-label={undefined}
      aria-hidden
      focusable="false"
      style={{ color: 'var(--buteco-brand-ink)', flexShrink: 0, display: 'block' }}
    />
  );
}
