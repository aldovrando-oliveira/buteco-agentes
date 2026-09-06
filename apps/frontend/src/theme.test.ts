import { describe, expect, it } from 'vitest';
import { DEFAULT_THEME } from '@mantine/core';
import { cssVariablesResolver, theme } from './theme';

// Estes testes existem porque a suíte é cega ao que esta change faz: jsdom não
// enxerga cor nem contraste, então nenhum dos outros testes notaria a paleta
// mudar. O que dá para verificar é que os tokens valem o que a spec diz — e o
// erro mais provável aqui é alguém abrir o AJUSTES_VISUAIS.md do handoff e
// "corrigir" as escalas de volta para a tabela de ancoragem errada dele.

function shades(name: 'butecoBlue' | 'green' | 'yellow' | 'red' | 'gray' | 'dark') {
  const tuple = theme.colors?.[name];
  if (!tuple) throw new Error(`escala ${name} não declarada no tema`);
  return tuple;
}

function relativeLuminance(hex: string): number {
  const channel = (pair: string) => {
    const c = parseInt(pair, 16) / 255;
    return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  };
  const h = hex.replace('#', '');
  return (
    0.2126 * channel(h.slice(0, 2)) +
    0.7152 * channel(h.slice(2, 4)) +
    0.0722 * channel(h.slice(4, 6))
  );
}

function contrastRatio(a: string, b: string): number {
  const [light, dark] = [relativeLuminance(a), relativeLuminance(b)].sort((x, y) => y - x);
  return (light + 0.05) / (dark + 0.05);
}

const AA_TEXT = 4.5;

describe('theme — âncoras da identidade visual', () => {
  it('declara a cor de destaque como primária, com o tom certo em cada esquema', () => {
    expect(theme.primaryColor).toBe('butecoBlue');
    expect(theme.primaryShade).toEqual({ light: 6, dark: 4 });
  });

  it('ancora a cor de destaque nos tons que o Mantine lê', () => {
    const blue = shades('butecoBlue');

    expect(blue[6]).toBe('#2a6ecb'); // --ac: variant="filled" e links no claro
    expect(blue[4]).toBe('#6495db'); // --ac clareado: o mesmo papel no escuro
    expect(blue[1]).toBe('#eef3fb'); // --acsf: fundo do item de navegação ativo
  });

  it.each([
    ['green', '#e9f6ee', '#1a7a49', '#4cc38a'],
    ['yellow', '#fbf1de', '#8a5a00', '#e2a63f'],
    ['red', '#fdeceb', '#b02a1f', '#f0766a'],
  ] as const)(
    'ancora %s no tom 1 (fundo claro), 9 (texto claro) e 4 (escuro)',
    (name, background, text, onDark) => {
      const tuple = shades(name);

      // No claro o Mantine lê o tom 1 como fundo de variant="light" e o tom 9
      // como texto — não os tons 0 e 8, como diz a tabela do handoff.
      expect(tuple[1]).toBe(background);
      expect(tuple[9]).toBe(text);
      // No escuro, tanto <Text c="X"> quanto variant="filled" leem o tom 4.
      expect(tuple[4]).toBe(onDark);
    },
  );

  it('ancora os neutros do esquema claro', () => {
    const neutral = shades('gray');

    // O tom 1 é o que o Mantine lê como superfície sutil elevada em quinze
    // lugares: fundo do SegmentedControl, hover de item, linha selecionada,
    // Code. Tem que ser --sf2, mais claro que a página.
    expect(neutral[1]).toBe('#faf9f8'); // --sf2
    expect(neutral[3]).toBe('#e5e5e5'); // --bd: borda de Paper/Card
    expect(neutral[6]).toBe('#686d74'); // --mut: o que c="dimmed" lê no claro
    expect(neutral[9]).toBe('#191a1c'); // --fg: texto principal
  });

  it.each(['--buteco-page-bg', '--buteco-surface-subtle'] as const)(
    'declara %s nos dois esquemas de cor',
    (variavel: `--${string}`) => {
      const resolvido = cssVariablesResolver(DEFAULT_THEME);

      // Papéis cuja cor troca de ponta da escala entre os esquemas não podem
      // sair de um tom fixo da paleta: no claro a superfície sutil é mais clara
      // que o card, no escuro é mais escura. Cravar um tom deixava a faixa de
      // cabeçalho branca no tema escuro.
      expect(resolvido.light?.[variavel]).toBeDefined();
      expect(resolvido.dark?.[variavel]).toBeDefined();
      expect(resolvido.light?.[variavel]).not.toBe(resolvido.dark?.[variavel]);
    },
  );

  it('mantém o fundo da página fora da escala neutra', () => {
    const pageBackground = cssVariablesResolver(DEFAULT_THEME).light?.['--buteco-page-bg'];

    // Regressão observada na conferência visual: enquanto o fundo da página
    // era o próprio gray[1], tudo que o Mantine pinta com esse tom sumia
    // contra a página. Os dois têm que ser valores distintos.
    expect(pageBackground).toBe('#f6f5f3');
    expect(pageBackground).not.toBe(shades('gray')[1]);
    expect(shades('gray').every((shade) => shade !== pageBackground)).toBe(true);
  });

  it('ancora os neutros do esquema escuro, com a superfície no tom 7', () => {
    const neutral = shades('dark');

    expect(neutral[0]).toBe('#e9eaec'); // --fg
    expect(neutral[2]).toBe('#9aa0a8'); // --mut: o que c="dimmed" lê no escuro
    expect(neutral[4]).toBe('#343639'); // borda de Paper/Card
    // Obrigatório: é o que o Mantine lê como --mantine-color-body no escuro, e
    // portanto a cor de Paper, Card, Table e Modal.
    expect(neutral[7]).toBe('#1b1d21'); // --sf
    expect(neutral[9]).toBe('#121316'); // --bg, aplicado ao body em index.css
  });

  it.each(['butecoBlue', 'green', 'yellow', 'red', 'gray', 'dark'] as const)(
    'mantém a rampa de %s monotônica, do mais claro para o mais escuro',
    (name) => {
      const luminances = shades(name).map(relativeLuminance);

      for (let i = 1; i < luminances.length; i += 1) {
        expect(luminances[i]).toBeLessThan(luminances[i - 1]);
      }
    },
  );
});

describe('theme — contraste das combinações em uso', () => {
  it.each(['butecoBlue', 'green', 'yellow', 'red'] as const)(
    'variant="light" de %s é legível no esquema claro',
    (name) => {
      const tuple = shades(name);

      expect(contrastRatio(tuple[9], tuple[1])).toBeGreaterThanOrEqual(AA_TEXT);
    },
  );

  it.each(['butecoBlue', 'green', 'yellow', 'red'] as const)(
    'texto colorido de %s é legível sobre a superfície escura',
    (name) => {
      const surface = shades('dark')[7];

      expect(contrastRatio(shades(name)[4], surface)).toBeGreaterThanOrEqual(AA_TEXT);
    },
  );

  it('texto secundário é legível sobre a página e sobre o card no claro', () => {
    const neutral = shades('gray');

    expect(contrastRatio(neutral[6], '#ffffff')).toBeGreaterThanOrEqual(AA_TEXT);
    expect(contrastRatio(neutral[6], neutral[1])).toBeGreaterThanOrEqual(AA_TEXT);
  });

  it('texto principal e secundário são legíveis sobre a superfície escura', () => {
    const neutral = shades('dark');

    expect(contrastRatio(neutral[0], neutral[7])).toBeGreaterThanOrEqual(AA_TEXT);
    expect(contrastRatio(neutral[2], neutral[7])).toBeGreaterThanOrEqual(AA_TEXT);
  });
});

describe('theme — padrão de componentes', () => {
  it('declara a variante clara e a preservação da caixa como padrão do badge', () => {
    const badge = theme.components?.Badge;

    // Cobre os dezenove badges do painel sem editar nenhuma chamada. A variante
    // clara também é o que faz o badge atingir o contraste mínimo: a preenchida
    // em verde e em âmbar, com texto branco, não atinge.
    expect(badge?.defaultProps).toMatchObject({ variant: 'light' });
    expect(badge?.styles).toMatchObject({ label: { textTransform: 'none' } });
  });

  it.each([
    ['green', 3.89],
    ['yellow', 3.72],
  ])('confirma que a variante preenchida de %s reprovaria com texto branco', (name, esperado) => {
    // O número existe para que a razão do padrão fique verificável, e não só
    // escrita em comentário: se alguém clarear o tom 6 achando que resolve, o
    // teste mostra que continua abaixo do mínimo.
    const preenchido = shades(name as 'green' | 'yellow')[6];

    expect(contrastRatio('#ffffff', preenchido)).toBeCloseTo(esperado, 1);
    expect(contrastRatio('#ffffff', preenchido)).toBeLessThan(AA_TEXT);
  });
});
