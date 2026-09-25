import { Card, SimpleGrid, Stack, Text } from '@mantine/core';
import type { ReactNode } from 'react';
import type { SystemInsights } from '../types/systemInsights';
import { MetricValue } from './MetricValue';
import { DeclaredGap } from './DeclaredGap';
import {
  EM_DASH,
  METRIC_SIZE,
  formatCount,
  formatDurationMs,
  formatTokens,
  readMetric,
  sumKnown,
  type QueryState,
} from '../utils/metricState';
import { caveatsFor } from '../utils/caveatLabels';

// OS SEIS CARDS DO `Main.dc.html`, NA ORDEM DO ARTBOARD.
//
// Duas decisões visíveis aqui, e as duas são divergência do protótipo:
//
// L2 — "Chamadas ao provedor": o protótipo escreve "522 de turno · 41 de
// compactação". `Purpose` EXISTE na tabela (`ProviderCall.Purpose`) e a rota
// NÃO o expõe. O total sai de `sum(byModel[].callCount)` e o subtítulo vira
// lacuna declarada. A fonte foi escolhida com razão: `callCount` é `int`,
// documentado como "contagem medida — pode ser zero", e a consulta que o produz
// não filtra `Purpose`. Somar contagens não colapsa nada.
// `performance.providerCallDuration.sampleCount` daria o mesmo número hoje e foi
// RECUSADO como fonte: por contrato aquele campo é uma AMOSTRA ("quantas
// entraram no cálculo — não quantas existem"), e a igualdade de hoje é
// incidental.
//
// L5 — "Duração da task (p95)": o protótipo escreve "mediana 4,1 s". A rota
// serve `avg(ms)`, e não existe `percentile_cont(0.5)` nela. O subtítulo diz
// MÉDIA. A palavra é o contrato (D9): chamar média de mediana afirma uma
// propriedade que o número não tem — quem lê "mediana" conclui que metade das
// tasks foi mais rápida, o que uma média com cauda longa não diz.
//
// O SUBTÍTULO OBEDECE À MESMA GRAMÁTICA DO NÚMERO. Um `?? '—'` nos subtítulos
// colapsaria célula vazia em travessão, que são dois estados diferentes: o
// travessão diz "não sei", o vazio diz "não há o que dizer". `Part` abaixo é o
// que mantém a distinção fora do card principal também.

interface KpiCardProps {
  label: string;
  children: ReactNode;
  footer?: ReactNode;
  testId: string;
}

function KpiCard({ label, children, footer, testId }: KpiCardProps) {
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
 */
function Part({
  value,
  queryState,
  format,
}: {
  value: number | null | undefined;
  queryState: QueryState;
  format?: (n: number) => string;
}) {
  const { state, text } = readMetric(value, queryState, format);
  if (state === 'empty') {
    return null;
  }
  return <>{state === 'unknown' ? EM_DASH : text}</>;
}

export interface InsightsKpiGridProps {
  insights: SystemInsights;
  queryState: QueryState;
  reason?: string;
}

export function InsightsKpiGrid({ insights, queryState, reason }: InsightsKpiGridProps) {
  const { volume, tokens, performance } = insights;

  // L2: o total de chamadas sai da soma das linhas de modelo. `callCount` é
  // contagem medida e nunca nula, então a soma é de `number` — e uma lista
  // vazia dá 0, que é zero MEDIDO (nenhum modelo foi chamado), não ausência.
  const providerCalls = tokens.byModel.reduce((total, row) => total + row.callCount, 0);

  const conversationTotal = sumKnown([
    tokens.conversation.inputTokens,
    tokens.conversation.outputTokens,
  ]);

  const duracaoCaveats = caveatsFor(performance.caveats, 'task-duration');

  return (
    <SimpleGrid cols={{ base: 1, sm: 2, lg: 3 }} spacing="sm" data-testid="kpis">
      <KpiCard label="Tasks executadas" testId="kpi-tasks">
        <MetricValue
          value={volume.executedTaskCount}
          queryState={queryState}
          reason={reason}
          format={formatCount}
          size={METRIC_SIZE.kpi}
          data-testid="kpi-tasks-valor"
        />
        <Text size="xs" c="dimmed">
          <Part
            value={volume.externalOriginTaskCount}
            queryState={queryState}
            format={formatCount}
          />{' '}
          de origem externa
        </Text>
      </KpiCard>

      <KpiCard
        label="Chamadas ao provedor"
        testId="kpi-chamadas"
        footer={
          <DeclaredGap
            variant="inline"
            label="turno e compactação"
            qualifier="não disponível"
            data-testid="kpi-chamadas-lacuna"
          />
        }
      >
        <MetricValue
          value={providerCalls}
          queryState={queryState}
          reason={reason}
          format={formatCount}
          size={METRIC_SIZE.kpi}
          data-testid="kpi-chamadas-valor"
        />
      </KpiCard>

      <KpiCard
        label="Duração da task (p95)"
        testId="kpi-duracao"
        footer={duracaoCaveats.map((c) => (
          <Text key={c.code} size="xs" c="dimmed" data-testid="kpi-duracao-caveat">
            {c.text}
          </Text>
        ))}
      >
        <MetricValue
          value={performance.taskDuration.p95Ms}
          queryState={queryState}
          reason={reason}
          format={formatDurationMs}
          size={METRIC_SIZE.kpi}
          data-testid="kpi-duracao-valor"
        />
        {/* MÉDIA, não mediana — a rota serve `avg(ms)` (D9, L5). */}
        <Text size="xs" c="dimmed" data-testid="kpi-duracao-sub">
          média{' '}
          <Part
            value={performance.taskDuration.averageMs}
            queryState={queryState}
            format={formatDurationMs}
          />
        </Text>
      </KpiCard>

      <KpiCard label="Tokens de conversa" testId="kpi-tokens">
        <MetricValue
          value={conversationTotal}
          queryState={queryState}
          reason={reason}
          format={formatTokens}
          size={METRIC_SIZE.kpi}
          data-testid="kpi-tokens-valor"
        />
        <Text size="xs" c="dimmed" data-testid="kpi-tokens-sub">
          <Part
            value={tokens.conversation.inputTokens}
            queryState={queryState}
            format={formatTokens}
          />{' '}
          entrada ·{' '}
          <Part
            value={tokens.conversation.outputTokens}
            queryState={queryState}
            format={formatTokens}
          />{' '}
          saída
        </Text>
      </KpiCard>

      <KpiCard label="Tokens de embedding" testId="kpi-embedding">
        <MetricValue
          value={tokens.embeddingInputTokens}
          queryState={queryState}
          reason={reason}
          format={formatTokens}
          size={METRIC_SIZE.kpi}
          data-testid="kpi-embedding-valor"
        />
        <Text size="xs" c="dimmed">
          indexação e busca em bases
        </Text>
      </KpiCard>

      <KpiCard label="Tokens por task" testId="kpi-tokens-por-task">
        <MetricValue
          value={tokens.perTask.average}
          queryState={queryState}
          reason={reason}
          format={formatTokens}
          size={METRIC_SIZE.kpi}
          data-testid="kpi-tokens-por-task-valor"
        />
        <Text size="xs" c="dimmed" data-testid="kpi-tokens-por-task-sub">
          p95 <Part value={tokens.perTask.p95} queryState={queryState} format={formatTokens} />
        </Text>
      </KpiCard>
    </SimpleGrid>
  );
}
