// A GRAMÁTICA DOS QUATRO ESTADOS, EM UM PONTO SÓ.
//
// | origem                | estado        | apresentação                 |
// |-----------------------|---------------|------------------------------|
// | número                | `value`       | o número formatado           |
// | `null` / `undefined`  | `empty`       | célula vazia                 |
// | consulta sem resposta | `unknown`     | travessão, com a razão       |
// | `0`                   | `zero`        | zero, escrito                |
//
// NENHUM `?? 0`, NENHUM `|| 0`, NENHUM `Number(x)` EM NENHUM CAMINHO DESTE
// ARQUIVO. É a regra que o resto da tela herda: quem chama não decide nada, só
// entrega `number | null | undefined` e o estado da consulta.
//
// POR QUE `zero` É ESTADO PRÓPRIO E NÃO UM `value` QUALQUER: porque a diferença
// entre "contei e deu zero" e "não há o que dizer" é a coisa toda que as três
// etapas anteriores preservaram ponta a ponta, e é aqui que ela pode ser perdida
// por uma linha. O quadro 4 do `Estados.dc.html` desenha os quatro, e a régua
// dele é: `0` é reservado à contagem feita, e a mais nada.
//
// AS ASSERÇÕES QUE PROTEGEM ISTO SÃO NEGATIVAS (convenção 13): o teste afirma a
// AUSÊNCIA do zero onde a origem é nula, não a presença do vazio. Um teste que
// só afirmasse "renderiza vazio" passaria por acidente se alguém trocasse o
// vazio por outra coisa qualquer que não fosse zero.

// OS TAMANHOS DOS NÚMEROS, MEDIDOS NO `Main.dc.html`.
//
// A escala do tema para em `xl: 16px`, porque o corpo do painel é 13px e a
// escala foi feita para TEXTO. Não existe token para "número de métrica", e a
// primeira versão usou o topo da escala — entregando os números a **73%** do
// desenhado nos KPIs (16px contra 22px) e a **80%** dentro de card (16px contra
// 20px).
//
// Nenhum teste podia pegar: jsdom não faz layout, e `size="xl"` é um valor
// válido. O que estava errado era a ESCOLHA do token, não o uso dele. Pego pelo
// dono na décima segunda rodada, a olho — *"tenho achado a fonte dos números
// proporcionalmente menores do que foi planejado"* —, e conferido no artboard.
//
// Os valores ficam aqui, em px, porque é assim que o artboard os declara: são
// medida, não degrau de escala. Cravá-los no componente espalharia a medição.
export const METRIC_SIZE = {
  /** Os seis cards de destaque: `font-size: 22px; font-weight: 500`. */
  kpi: '22px',
  /** Número dentro de card, como as duas contagens de Falhas: `20px`. */
  card: '20px',
} as const;

export type MetricState = 'value' | 'empty' | 'unknown' | 'zero';

export interface MetricReading {
  state: MetricState;
  /** O texto a exibir. Vazio (`''`) no estado `empty`. */
  text: string;
}

/** Vazio no `empty`; o travessão é decidido pelo componente, com a razão ao lado. */
export const EM_DASH = '—';

// TRÊS ESTADOS, E NÃO DOIS. A primeira versão tinha `'ok' | 'failed'`, e o
// `isPending` do react-query caía em `'ok'` — o esqueleto que sustenta a tela
// tem contagens `int` em 0, e a página exibia "Tasks executadas: 0" e "Nenhuma
// task neste período" ENQUANTO A CONSULTA AINDA CORRIA.
//
// Era exatamente o modo de falha que o quadro 3 do `Estados.dc.html` proíbe por
// escrito — "requisição que não respondeu não é evidência de ausência" — e
// nenhum teste pegou, porque todos cobriam `isError` e nenhum cobria
// `isPending`. Pego na conferência manual de 24/09, com a API derrubada
// (convenção 14: mudança visual só é verificada por olho humano).
//
// `'loading'` e `'failed'` produzem o MESMO estado de apresentação — travessão
// com a razão ao lado —, e continuam separados porque a razão é diferente:
// "consultando" e "não respondeu" não são a mesma frase para quem lê.
export type QueryState = 'ok' | 'loading' | 'failed';

type Nullish = number | null | undefined;

function isKnown(value: Nullish): value is number {
  // `value === 0` precisa passar: a checagem é sobre CONHECIMENTO, não sobre
  // verdade. Um `if (!value)` aqui mandaria o zero medido para o vazio, que é
  // exatamente o colapso que o módulo existe para impedir.
  return typeof value === 'number' && Number.isFinite(value);
}

/**
 * O ponto único de decisão. Recebe o valor cru e o estado da consulta; devolve
 * um dos quatro estados mais o texto já formatado.
 *
 * `format` só é chamado quando o valor é conhecido e diferente de zero — nenhum
 * formatador recebe `null`, e por isso nenhum precisa se defender dele.
 */
export function readMetric(
  value: Nullish,
  queryState: QueryState,
  format: (n: number) => string = formatCount,
): MetricReading {
  // A consulta vem primeiro: sem resposta — falhada OU ainda em curso — não se
  // sabe nada sobre o valor, e qualquer coisa que o campo contenha é resíduo do
  // esqueleto ou de uma resposta anterior.
  if (queryState !== 'ok') {
    return { state: 'unknown', text: EM_DASH };
  }

  if (!isKnown(value)) {
    return { state: 'empty', text: '' };
  }

  if (value === 0) {
    return { state: 'zero', text: '0' };
  }

  return { state: 'value', text: format(value) };
}

/**
 * Soma que preserva o nulo: o total fica VAZIO quando todas as parcelas são
 * nulas, e soma só o conhecido quando alguma não é.
 *
 * É a mesma decisão que o SQL da rota toma com `filter (where … is not null)`, e
 * pela mesma razão: `coalesce(x, 0)` solto faria "ninguém reportou" virar
 * "reportaram zero" na soma.
 */
export function sumKnown(values: Nullish[]): number | null {
  const known = values.filter(isKnown);
  if (known.length === 0) {
    return null;
  }
  return known.reduce((total, n) => total + n, 0);
}

/**
 * Percentual com denominador guardado. Devolve `null` — não `0`, não `NaN` —
 * quando o denominador é zero ou desconhecido.
 */
export function ratio(numerator: Nullish, denominator: Nullish): number | null {
  if (!isKnown(numerator) || !isKnown(denominator) || denominator === 0) {
    return null;
  }
  return numerator / denominator;
}

// ------------------------------------------------------------- Formatação
//
// Tudo em `pt-BR`: separador de milhar por ponto, decimal por vírgula. O
// protótipo escreve "12,4 M" e "341,2 mil", e é esse o alvo.

const ptBR = 'pt-BR';

export function formatCount(n: number): string {
  return new Intl.NumberFormat(ptBR).format(n);
}

/**
 * Token abreviado, como no protótipo: `12,4 M`, `341,2 mil`, `563`.
 *
 * O corte em mil e milhão é do desenho, e o uma casa decimal também — "12,4 M"
 * ocupa a mesma largura de card que "563" e continua legível a 20px.
 */
export function formatTokens(n: number): string {
  const abs = Math.abs(n);
  if (abs >= 1_000_000) {
    return `${new Intl.NumberFormat(ptBR, { maximumFractionDigits: 1 }).format(n / 1_000_000)} M`;
  }
  if (abs >= 1_000) {
    return `${new Intl.NumberFormat(ptBR, { maximumFractionDigits: 1 }).format(n / 1_000)} mil`;
  }
  return formatCount(n);
}

/** Duração em segundos, uma casa: `12,4 s`. A rota entrega milissegundos. */
export function formatDurationMs(ms: number): string {
  return `${new Intl.NumberFormat(ptBR, { maximumFractionDigits: 1 }).format(ms / 1000)} s`;
}

/** Percentual com uma casa: `3,2%`. Recebe a razão, não o valor já multiplicado. */
export function formatRatio(value: number): string {
  return new Intl.NumberFormat(ptBR, {
    style: 'percent',
    maximumFractionDigits: 1,
  }).format(value);
}

/** Média com uma casa, sem unidade: `2,4`. */
export function formatDecimal(n: number): string {
  return new Intl.NumberFormat(ptBR, { maximumFractionDigits: 1 }).format(n);
}
