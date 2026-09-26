import { Box, Group, SimpleGrid, Text, Tooltip } from '@mantine/core';
import { Info } from 'lucide-react';
import type { AgentInsights } from '../types/agentInsights';
import { MetricValue } from './MetricValue';
import { KpiCard, Part } from './KpiCard';
import { caveatsFor } from '../utils/caveatLabels';
import {
  METRIC_SIZE,
  formatCount,
  formatRatio,
  formatTokens,
  ratio,
  sumKnown,
  type QueryState,
} from '../utils/metricState';
// OS QUATRO CARDS DOS ARTBOARDS DO AGENTE, NA ORDEM DELES.
//
// Quatro, e não os seis do `Main.dc.html`: os três artboards do agente desenham
// tasks executadas, tokens de conversa, tokens por task e taxa de falha. Não há
// "chamadas ao provedor" nem "tokens de embedding" aqui — o que a rota serve e
// o artboard não desenha fica fora, e está registrado (D10).
//
// ---------------------------------------------------------------------------
// O SUBTÍTULO DE ORIGEM ESCREVE O ZERO POR EXTENSO, E O ARTBOARD DECIDE ASSIM
// ---------------------------------------------------------------------------
//
// `Agente-Insights.dc.html` escreve "todas de origem externa · nenhuma por
// delegação"; `Agente-Delegado.dc.html` escreve "nenhuma de origem externa · 65
// por delegação". As duas palavras são o MESMO estado — zero medido —, e a
// escolha entre elas é do outro lado: "todas" cabe quando a outra parcela é
// zero, porque aí esta é o total inteiro.
//
// O estado continua sendo `zero` em `readMetric`: muda a palavra, não a
// classificação, e os guardas negativos seguem valendo.
//
// ---------------------------------------------------------------------------
// A RECUSA DO SUBTÍTULO DE FALHA É A `rejectedCount`, E NÃO A DE ENTRADA (D6)
// ---------------------------------------------------------------------------
//
// São DUAS recusas, de regimes diferentes. A que fica aqui é a que tem linha de
// execução, porque é a população do MESMO regime do percentual logo acima, e é
// dela que o artboard fala ao escrever "nenhuma recusa".
//
// A recusa de ENTRADA (`rejectedAtEntryCount`) e os motivos dela têm regime
// próprio e vão para o card de falhas, com o regime declarado no cabeçalho do
// grupo. Pô-la aqui colocaria um número de outro regime ao lado de um
// percentual que não o inclui, sem lugar para declarar o regime — e somar as
// duas juntaria duas janelas num rótulo só.
//
// ---------------------------------------------------------------------------
// O CAVEAT DE RECUSA ACABOU AQUI, COMO ÍCONE — E O CAMINHO ATÉ AQUI IMPORTA
// ---------------------------------------------------------------------------
//
// `rejections-missing-from-executions` limita `rejectedCount`, que este
// subtítulo apresenta. Ele passou por três posições em três rodadas de
// conferência, e cada mudança teve uma razão distinta:
//
//   1. estava **aqui em prosa, no rodapé** do card, e **também** no card de
//      falhas → o dono apontou o rodapé que o artboard não desenha, e a régua
//      mostrou que era DUPLICAÇÃO. Saiu daqui;
//   2. ficou só no card de falhas, ao lado do grupo de recusa de entrada;
//   3. **o grupo de recusa de entrada saiu daquele card** — ele era um
//      elemento que o artboard não tem, e a D10 proíbe criar um. Com ele foi o
//      caveat, que ficou sem número ao lado.
//
// Então voltou para cá, que é onde o número que ele limita está o tempo todo —
// e como **ícone**, não como prosa, porque o corpo do card é o que o artboard
// desenha: rótulo, número, subtítulo.
//
// O ícone fica ao lado do RÓTULO, e não no rodapé: o rodapé é o que foi
// apontado, e o rótulo é a única parte do card que sobra sem ser número.
//
// ---------------------------------------------------------------------------
// QUANDO NÃO HOUVE FALHA, O CARD INTEIRO MUDA — E É DESENHO, NÃO INVENÇÃO
// ---------------------------------------------------------------------------
//
// `Agente-Delegado.dc.html` desenha o valor como a palavra "Nenhuma",
// esmaecida, e o subtítulo como "em 65 tasks executadas". É o zero medido
// escrito por extenso, no lugar de "0,0%", que seria verdadeiro e ilegível.

export interface AgentKpiGridProps {
  insights: AgentInsights;
  queryState: QueryState;
  reason?: string;
}

export function AgentKpiGrid({ insights, queryState, reason }: AgentKpiGridProps) {
  const { volume, tokens, errors } = insights;

  const conversationTotal = sumKnown([
    tokens.conversation.inputTokens,
    tokens.conversation.outputTokens,
  ]);

  // `ratio` devolve nulo — não `0`, não `NaN` — quando o denominador é zero ou
  // desconhecido: um agente sem nenhuma task não tem taxa de falha, e `0%`
  // afirmaria que ele rodou sem falhar.
  const taxaDeFalha = ratio(errors.failedCount, volume.executedTaskCount);

  const semFalha = queryState === 'ok' && errors.failedCount === 0;

  const recusaCaveats = caveatsFor(errors.caveats, 'rejection-count', 'agent');

  return (
    <SimpleGrid cols={{ base: 1, sm: 2, lg: 4 }} spacing="sm" data-testid="kpis-do-agente">
      <KpiCard label="Tasks executadas" testId="kpi-agente-tasks">
        <MetricValue
          value={volume.executedTaskCount}
          queryState={queryState}
          reason={reason}
          format={formatCount}
          size={METRIC_SIZE.kpi}
          data-testid="kpi-agente-tasks-valor"
        />
        <Text size="xs" c="dimmed" data-testid="kpi-agente-tasks-sub">
          <Part
            value={volume.externalOriginTaskCount}
            queryState={queryState}
            format={formatCount}
            zeroLabel={volume.delegationOriginTaskCount > 0 ? 'nenhuma' : undefined}
          />{' '}
          de origem externa ·{' '}
          <Part
            value={volume.delegationOriginTaskCount}
            queryState={queryState}
            format={formatCount}
            zeroLabel={volume.externalOriginTaskCount > 0 ? 'nenhuma' : undefined}
          />{' '}
          por delegação
        </Text>
      </KpiCard>

      <KpiCard label="Tokens de conversa" testId="kpi-agente-tokens">
        <MetricValue
          value={conversationTotal}
          queryState={queryState}
          reason={reason}
          format={formatTokens}
          size={METRIC_SIZE.kpi}
          data-testid="kpi-agente-tokens-valor"
        />
        <Text size="xs" c="dimmed" data-testid="kpi-agente-tokens-sub">
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

      <KpiCard label="Tokens por task" testId="kpi-agente-tokens-por-task">
        <MetricValue
          value={tokens.perTask.average}
          queryState={queryState}
          reason={reason}
          format={formatTokens}
          size={METRIC_SIZE.kpi}
          data-testid="kpi-agente-tokens-por-task-valor"
        />
        <Text size="xs" c="dimmed" data-testid="kpi-agente-tokens-por-task-sub">
          p95 <Part value={tokens.perTask.p95} queryState={queryState} format={formatTokens} />
        </Text>
      </KpiCard>

      <KpiCard
        label={
          <Group gap={6} align="center" wrap="nowrap" component="span">
            <Text span size="xs" c="dimmed">
              Taxa de falha
            </Text>
            {recusaCaveats.map((c) => (
              <Tooltip key={c.code} label={c.text} multiline w={320} withArrow>
                <Box
                  component="span"
                  data-testid="kpi-agente-falha-caveat"
                  data-caveat-code={c.code}
                  title={c.text}
                  aria-label={c.text}
                  style={{ display: 'inline-flex', color: 'var(--mantine-color-dimmed)' }}
                >
                  <Info size={13} aria-hidden="true" />
                </Box>
              </Tooltip>
            ))}
          </Group>
        }
        testId="kpi-agente-falha"
      >
        <MetricValue
          value={taxaDeFalha}
          queryState={queryState}
          reason={reason}
          format={formatRatio}
          zeroLabel="Nenhuma"
          size={METRIC_SIZE.kpi}
          data-testid="kpi-agente-falha-valor"
        />
        {semFalha ? (
          <Text size="xs" c="dimmed" data-testid="kpi-agente-falha-sub">
            em{' '}
            <Part
              value={volume.executedTaskCount}
              queryState={queryState}
              format={formatCount}
            />{' '}
            tasks executadas
          </Text>
        ) : (
          <Text size="xs" c="dimmed" data-testid="kpi-agente-falha-sub">
            <Part value={errors.failedCount} queryState={queryState} format={formatCount} />{' '}
            falhas ·{' '}
            <Part
              value={errors.rejectedCount}
              queryState={queryState}
              format={formatCount}
              zeroLabel="nenhuma"
            />{' '}
            recusa
          </Text>
        )}
      </KpiCard>
    </SimpleGrid>
  );
}
