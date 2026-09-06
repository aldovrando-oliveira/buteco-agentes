import {
  Badge,
  createTheme,
  type CSSVariablesResolver,
  type MantineColorsTuple,
} from '@mantine/core';

// Identidade visual do painel (design.md da change
// frontend-tema-identidade-visual). Os valores vêm do protótipo do handoff do
// Claude Design; a ancoragem NÃO vem da tabela do AJUSTES_VISUAIS.md, que foi
// escrita sem executar o Mantine e erra em qual tom cada papel consome.
//
// O que o Mantine 9 lê de cada tom, verificado em
// @mantine/core/esm/core/MantineProvider/MantineCssVariables/get-css-color-variables.mjs:
//
//   claro   1 = fundo de variant="light"      9 = texto de variant="light"
//           6 = variant="filled" e <Text c="X">
//   escuro  4 = variant="filled" e <Text c="X">
//           0 = texto de variant="light"      (o tom 0 não é usado no claro)
//
// Só os tons marcados abaixo são contrato — src/theme.test.ts os verifica. Os
// demais são preenchimento para manter a rampa monotônica.

// --ac do protótipo no tom 6; o 4 é color-mix(in oklch, #2a6ecb, white 26%)
// calculado, que é como o protótipo clareia o accent no escuro; o 1 é o accent
// a 8% sobre branco, usado no item de navegação ativo (--acsf).
const butecoBlue: MantineColorsTuple = [
  '#f5f8fe',
  '#eef3fb', // 1  --acsf
  '#d6e3f6',
  '#b0c8ee',
  '#6495db', // 4  --ac no escuro
  '#4180d4',
  '#2a6ecb', // 6  --ac
  '#245cad',
  '#1e4d92',
  '#1a3f78', // 9
];

const green: MantineColorsTuple = [
  '#f3faf6',
  '#e9f6ee', // 1  --okbg
  '#cfe9dc',
  '#a0d7bd',
  '#4cc38a', // 4  --ok no escuro
  '#33ab72',
  '#22935c', // 6
  '#1e8853',
  '#1c814e',
  '#1a7a49', // 9  --ok
];

const yellow: MantineColorsTuple = [
  '#fdf8ee',
  '#fbf1de', // 1  --wabg
  '#f4e0bb',
  '#e8c489',
  '#e2a63f', // 4  --wa no escuro
  '#c78c28',
  '#b07a1e', // 6
  '#9d6a13',
  '#93620a',
  '#8a5a00', // 9  --wa
];

const red: MantineColorsTuple = [
  '#fef5f4',
  '#fdeceb', // 1  --dabg
  '#f9d3d0',
  '#f3aea7',
  '#f0766a', // 4  --da no escuro
  '#d94a3b',
  '#cf3a2b', // 6
  '#c23325',
  '#b92e21',
  '#b02a1f', // 9  --da
];

// Neutro quente do protótipo. O tom 3 é a borda de Paper/Card — o Mantine lê
// gray[3] em --paper-border-color, não o default-border.
//
// O fundo da PÁGINA não mora aqui, de propósito. O Mantine lê gray[1] em 15
// lugares como "superfície sutil elevada" — fundo do SegmentedControl, hover
// de item, linha selecionada, Code. Pôr o #f6f5f3 da página nesse tom fazia
// todos esses elementos sumirem contra o próprio fundo que deviam destacar.
// O tom 1 fica sendo --sf2, a superfície sutil, que é mais clara que a
// página; o fundo da página é --buteco-page-bg, definido no resolver abaixo.
const gray: MantineColorsTuple = [
  '#fcfcfb', // 0  hover mais sutil ainda
  '#faf9f8', // 1  --sf2: superfície sutil (SegmentedControl, header, hover)
  '#f1f1f1', //    --bd2
  '#e5e5e5', // 3  --bd
  '#d5d4d2',
  '#8b9097', //    --mut2
  '#686d74', // 6  --mut, o que c="dimmed" lê no claro
  '#4d5157',
  '#33363b',
  '#191a1c', // 9  --fg
];

// dark[] é invertido: 0 é o tom mais claro. O 7 é obrigatório — é o que o
// Mantine lê como --mantine-color-body no escuro, e portanto a cor de Paper,
// Card, Table e Modal. O 9 é o fundo da página, aplicado em index.css.
const dark: MantineColorsTuple = [
  '#e9eaec', // 0  --fg
  '#c8cbd0',
  '#9aa0a8', // 2  --mut, o que c="dimmed" lê no escuro
  '#7d838b', //    --mut2
  '#343639', // 4  borda de Paper
  '#2b2e34',
  '#24272c',
  '#1b1d21', // 7  --sf, a superfície
  '#16181b',
  '#121316', // 9  --bg
];

const sans = "'IBM Plex Sans', system-ui, -apple-system, Segoe UI, Roboto, sans-serif";
const mono = "'IBM Plex Mono', ui-monospace, SFMono-Regular, Menlo, monospace";

export const theme = createTheme({
  colors: { butecoBlue, green, yellow, red, gray, dark },
  primaryColor: 'butecoBlue',
  primaryShade: { light: 6, dark: 4 },

  fontFamily: sans,
  fontFamilyMonospace: mono,

  // Escala densa do protótipo: corpo em 13px, não os 16px do Mantine.
  fontSizes: {
    xs: '11px',
    sm: '12px',
    md: '13px',
    lg: '14px',
    xl: '16px',
  },
  lineHeights: {
    xs: '1.35',
    sm: '1.4',
    md: '1.45',
    lg: '1.5',
    xl: '1.55',
  },

  // Tamanhos no tema, não em cada <Title> (design.md, D6).
  headings: {
    fontFamily: sans,
    fontWeight: '600',
    sizes: {
      h1: { fontSize: '20px', lineHeight: '1.3' },
      h2: { fontSize: '17px', lineHeight: '1.35' },
      h3: { fontSize: '15px', lineHeight: '1.4' },
      h4: { fontSize: '14px', lineHeight: '1.4' },
      h5: { fontSize: '13px', lineHeight: '1.45' },
      h6: { fontSize: '12px', lineHeight: '1.45' },
    },
  },

  radius: {
    xs: '5px',
    sm: '7px',
    md: '9px',
    lg: '11px',
    xl: '14px',
  },
  defaultRadius: 'sm',

  // A aparência do badge é decisão de produto, não de tela: o padrão vive aqui
  // e cobre os dezenove badges do painel sem editar nenhuma chamada.
  //
  // A variante clara não é só estética. A preenchida usa o tom 6 com texto
  // branco, que em verde dá 3,89:1 e em âmbar 3,72:1 — abaixo do mínimo de
  // 4,5:1 que este mesmo tema exige. A clara dá 4,81:1 e 5,29:1.
  //
  // O text-transform em caixa alta e o negrito são defaults do Mantine, não
  // nossos: os rótulos já são escritos em caixa de sentença, e o protótipo os
  // mostra assim (design.md da change frontend-acabamento-telas, D3).
  components: {
    Badge: Badge.extend({
      defaultProps: { variant: 'light' },
      styles: { label: { textTransform: 'none' } },
    }),
  },

  // O protótipo tem uma sombra só, e nenhuma no escuro. As cinco entram na
  // mesma família para que Modal (que usa xl) não destoe do resto.
  shadows: {
    xs: '0 1px 2px rgba(16, 14, 10, 0.05)',
    sm: '0 1px 2px rgba(16, 14, 10, 0.05)',
    md: '0 2px 6px rgba(16, 14, 10, 0.07)',
    lg: '0 4px 14px rgba(16, 14, 10, 0.09)',
    xl: '0 6px 24px rgba(16, 14, 10, 0.14)',
  },
});

// A borda do protótipo é uma só, mas o Mantine usa duas variáveis com tons
// diferentes: Paper/Card leem --paper-border-color (gray[3] no claro, dark[4]
// no escuro) e inputs/botões leem --mantine-color-default-border (gray[4] no
// claro, dark[4] no escuro). No escuro os dois já coincidem; no claro, este
// resolver alinha o default-border ao tom do Paper, em vez de espremer gray[3]
// e gray[4] no mesmo valor e perder um degrau da rampa (design.md, D5).
export const cssVariablesResolver: CSSVariablesResolver = () => ({
  variables: {},
  light: {
    '--mantine-color-default-border': 'var(--mantine-color-gray-3)',
    // Fundo da página (--bg do protótipo). Fora da paleta de propósito: ver o
    // comentário da escala `gray`. Consumido pelo body em index.css.
    '--buteco-page-bg': '#f6f5f3',
    // Superfície sutil (--sf2 do protótipo): faixa de cabeçalho de card e de
    // tabela. Precisa existir como variável, e não como um tom cravado, porque
    // o papel troca de ponta da escala entre os esquemas — no claro é o tom
    // mais claro que a superfície, no escuro é o mais escuro. Cravar `gray[1]`
    // deixava a faixa branca no tema escuro.
    '--buteco-surface-subtle': 'var(--mantine-color-gray-1)',
  },
  dark: {
    '--buteco-page-bg': 'var(--mantine-color-dark-9)',
    '--buteco-surface-subtle': 'var(--mantine-color-dark-6)',
  },
});
