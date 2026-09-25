import { Text, Tooltip } from '@mantine/core';
import { readMetric, type QueryState } from '../utils/metricState';

// OS QUATRO ESTADOS, RENDERIZADOS. Um ponto só na tela, como `metricState.ts` é
// um ponto só na lógica.
//
// O TRAVESSÃO NUNCA SAI SOZINHO. Sem a razão ao lado ele é lido como zero — e é
// o modo de falha que o quadro 3 do `Estados.dc.html` existe para proibir, com
// o texto "requisição que não respondeu não é evidência de ausência". Por isso
// `reason` é obrigatório quando `queryState` é `'failed'`, e o componente o
// pendura num Tooltip mais o `title`, que é o que sobrevive à leitura por
// leitor de tela.
//
// A CÉLULA VAZIA É VAZIA DE VERDADE: nem `-`, nem `N/D`, nem `0`. O
// `data-metric-state` é o que os testes leem para afirmar o estado sem depender
// do texto — e é também o que permite a asserção NEGATIVA (não há `0` aqui)
// continuar significando alguma coisa.

/** Espaço inquebrável, por escape — o literal reprova no `no-irregular-whitespace`. */
const NBSP = '\u00a0';

export interface MetricValueProps {
  value: number | null | undefined;
  queryState: QueryState;
  /** Obrigatório na prática quando a consulta falhou: é a razão ao lado do travessão. */
  reason?: string;
  format?: (n: number) => string;
  /** Sufixo exibido ao lado do número. Não aparece no vazio nem no travessão. */
  unit?: string;
  /**
   * Palavra para o ZERO MEDIDO, no lugar do algarismo — "Nenhuma", "Nenhum".
   *
   * O `Main.dc.html` escreve "Nenhuma" na coluna Falhas do consumo por agente,
   * esmaecido, em vez de `0`. É a mesma régua que a spec já pede para card e
   * seção ("o zero por extenso, para que a diferença em relação ao travessão
   * seja legível em palavras e não só em símbolo"), aplicada à célula.
   *
   * O ESTADO CONTINUA SENDO `zero`: muda a palavra, não a classificação. Os
   * guardas negativos seguem valendo.
   */
  zeroLabel?: string;
  size?: string;
  fw?: number;
  /** Cor do valor medido. Não afeta vazio, travessão nem o zero por extenso. */
  c?: string;
  'data-testid'?: string;
}

export function MetricValue({
  value,
  queryState,
  reason,
  format,
  unit,
  zeroLabel,
  size = 'xl',
  fw = 500,
  c,
  'data-testid': testId,
}: MetricValueProps) {
  const { state, text } = readMetric(value, queryState, format);

  const common = {
    ff: 'monospace',
    size,
    fw,
    'data-metric-state': state,
    'data-testid': testId,
  } as const;

  if (state === 'empty') {
    // Vazia, com a linha preservada: o NBSP mantém a altura da célula para
    // que a tabela não pule. Nada de conteúdo visível — é o ponto.
    return (
      <Text {...common} aria-label="sem dado a apresentar">
        {NBSP}
      </Text>
    );
  }

  if (state === 'unknown') {
    const razao = reason ?? 'A consulta não respondeu.';
    return (
      <Tooltip label={razao} multiline w={260}>
        <Text {...common} c="dimmed" title={razao} aria-label={`desconhecido: ${razao}`}>
          {text}
        </Text>
      </Tooltip>
    );
  }

  if (state === 'zero' && zeroLabel !== undefined) {
    return (
      <Text {...common} ff={undefined} c="dimmed">
        {zeroLabel}
      </Text>
    );
  }

  return (
    <Text {...common} c={c}>
      {text}
      {unit ? ` ${unit}` : ''}
    </Text>
  );
}
