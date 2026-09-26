import { Card, Stack, Text } from '@mantine/core';
import type { ReactNode } from 'react';
import { EM_DASH, readMetric, type QueryState } from '../utils/metricState';

// O CARD DE DESTAQUE E O NÚMERO DENTRO DE SUBTÍTULO — DOIS CONSUMIDORES.
//
// EXTRAÍDO AGORA PORQUE A REPETIÇÃO FOI OBSERVADA, NÃO PREVISTA.
//
// A convenção 2 exige repetição **já observada**, e a change da página do
// sistema registrou por escrito que a aba do agente era consumidor PREVISTO —
// e por isso não extraiu. Ela chegou: `InsightsKpiGrid` (seis cards do
// `Main.dc.html`) e `AgentKpiGrid` (quatro cards dos artboards do agente) são
// os dois consumidores reais, contados antes de extrair, como os três
// componentes visuais compartilhados do painel já tinham sido.
//
// A EXTRAÇÃO É MECÂNICA: nenhum comportamento muda, e o que prova isso é a
// suíte da página do sistema passar **sem alteração nos testes dela**.
//
// O QUE **NÃO** FOI EXTRAÍDO, E POR QUÊ: o grid. `AgentKpiGrid` é componente
// próprio — quatro cards contra seis, com rótulos, subtítulos e fontes
// diferentes, sobre outro tipo de resposta. Parametrizar o grid inteiro
// juntaria duas telas num componente que muda por bandeira, que é o oposto do
// que a convenção 2 procura.

export interface KpiCardProps {
  /**
   * `ReactNode`, e não `string`: a aba do agente põe um ícone de parcialidade
   * ao lado do rótulo em um dos cards, quando o `caveat` limita o número deste
   * card e não tem outro lugar na tela. A página do sistema continua passando
   * texto puro.
   */
  label: ReactNode;
  children: ReactNode;
  footer?: ReactNode;
  testId: string;
}

export function KpiCard({ label, children, footer, testId }: KpiCardProps) {
  return (
    <Card withBorder padding="md" data-testid={testId}>
      <Stack gap={6}>
        <Text size="xs" c="dimmed">
          {label}
        </Text>
        {children}
        {footer}
      </Stack>
    </Card>
  );
}

/**
 * Um número dentro de um subtítulo, com os quatro estados preservados: valor e
 * zero saem escritos, vazio sai VAZIO (não travessão), e travessão só quando a
 * consulta não respondeu.
 *
 * O SUBTÍTULO OBEDECE À MESMA GRAMÁTICA DO NÚMERO. Um `?? '—'` nos subtítulos
 * colapsaria célula vazia em travessão, que são dois estados diferentes: o
 * travessão diz "não sei", o vazio diz "não há o que dizer".
 *
 * `zeroLabel` escreve o zero medido por extenso — "nenhuma", "todas" —, que é
 * como os artboards do agente o escrevem nos subtítulos de origem das tasks. O
 * ESTADO continua sendo `zero`: muda a palavra, não a classificação, e os
 * guardas negativos seguem valendo.
 */
export function Part({
  value,
  queryState,
  format,
  zeroLabel,
}: {
  value: number | null | undefined;
  queryState: QueryState;
  format?: (n: number) => string;
  zeroLabel?: string;
}) {
  const { state, text } = readMetric(value, queryState, format);
  if (state === 'empty') {
    return null;
  }
  if (state === 'zero' && zeroLabel !== undefined) {
    return <>{zeroLabel}</>;
  }
  return <>{state === 'unknown' ? EM_DASH : text}</>;
}
