import { useMemo, useState } from 'react';
import { Alert, Anchor, Grid, Group, Stack, Text } from '@mantine/core';
import { TriangleAlert } from 'lucide-react';
import { useAgentInsightsQuery } from '../api/useAgentInsights';
import { ApiError } from '../api/insightsApi';
import {
  DEFAULT_INSIGHTS_PERIOD,
  insightsWindow,
  type InsightsPeriod,
} from '../utils/insightsWindow';
import { measuredDays } from '../utils/measuredDays';
import { unknownCaveats } from '../utils/caveatLabels';
import type { QueryState } from '../utils/metricState';
import type { AgentInsights } from '../types/agentInsights';
import type { DelegationCatalogAgent } from '../utils/delegationRows';
import { PeriodPicker } from './PeriodPicker';
import { PartialMeasurementNotice } from './PartialMeasurementNotice';
import { WeekdayActivityCard } from './WeekdayActivityCard';
import { AgentKpiGrid } from './AgentKpiGrid';
import { TimeBreakdownCard } from './TimeBreakdownCard';
import { TaskDurationCard } from './TaskDurationCard';
import { AgentModelsCard } from './AgentModelsCard';
import { AgentDelegationCard } from './AgentDelegationCard';
import { AgentFailuresCard } from './AgentFailuresCard';

// A ABA É DONA DO PERÍODO E DA CONSULTA; OS SETE CARDS SÃO DE APRESENTAÇÃO.
//
// Nenhum card desta aba importa hook de query — todos recebem props, e é o que
// torna os cenários testáveis sem montar um `QueryClient` (convenção 7). A
// consulta fica aqui, e não em `AgentDetailPage`, pelo idioma que a própria
// página já usa: `AgentDelegationsTab` e `AgentKnowledgeTab` guardam as suas
// mutações. Uma página que hospeda cinco abas não tem por que carregar estado
// de período.
//
// O CARREGAMENTO TARDIO SAI DE GRAÇA. O `<Tabs keepMounted={false}>` da página
// garante que este componente só existe no DOM quando a aba está ativa — a
// consulta não dispara enquanto o operador está em outra aba, sem precisar do
// `enabled:` que as abas de ferramentas e conhecimento usam. O guarda de
// `AgentDetailPage.test.tsx` afirma isso, porque é comportamento que se perde
// em silêncio numa mudança de `keepMounted`.
//
// O AGENTE E O CATÁLOGO VÊM POR PROP, e este módulo NÃO importa nada de
// `features/agents`. A seta entre as features é de mão única, e o guarda é a
// própria ausência de import — afirmada por teste, porque é o tipo de
// acoplamento que volta numa refatoração distraída.
//
// ===========================================================================
// OS QUATRO ESTADOS DE RESPOSTA, E O QUARTO É PRÓPRIO DESTA ABA
// ===========================================================================
//
//   `ok`      — a resposta chegou;
//   `loading` — ainda não respondeu;
//   `failed`  — não respondeu, e não vai;
//   `404`     — RESPONDEU, dizendo que o agente não existe.
//
// **`ok` sai só de `isSuccess`, nunca de `!isError`.** Com a negação, o
// pendente passa, e a tela afirma `0` com a API fora do ar — as contagens `int`
// do esqueleto são 0 por serem `number`. Foi assim que o defeito entrou na
// página do sistema, e levou doze rodadas de conferência manual para sair.
//
// **O `404` é resposta, não falha de comunicação** (D17). Ele tem texto próprio
// e **não oferece nova tentativa**: repetir a pergunta não muda a resposta, e
// um botão ao lado convida a tentar para sempre. Na prática ele só aparece se
// as duas rotas discordarem — a página de detalhe já resolveu o agente antes
// de a aba existir, e não há `DELETE /agents/{id}` no repositório —, e o texto
// diz isso em vez de "não encontrado", que o operador leria como erro dele.

/**
 * O ESQUELETO QUE SUSTENTA A ABA ENQUANTO A CONSULTA NÃO RESPONDEU.
 *
 * Declarado aqui, e NÃO importado da fixture: o duplo de teste não embarca no
 * bundle. Os dois são parecidos de propósito e vivem separados pelo mesmo
 * motivo.
 *
 * TUDO NULO, NENHUM ZERO ESCOLHIDO. As contagens `int` do contrato ficam em 0
 * por serem `number`, e é justamente por isso que `queryState` vence sobre o
 * valor em `readMetric`.
 */
function corpoAusente(agentId: string): AgentInsights {
  const semStats = { averageMs: null, p95Ms: null, sampleCount: 0 };
  return {
    agentId,
    window: { from: '', to: '', timeZone: 'America/Sao_Paulo' },
    regimes: {},
    volume: {
      regime: 'execution',
      executedTaskCount: 0,
      externalOriginTaskCount: 0,
      delegationOriginTaskCount: 0,
    },
    temporal: { regime: 'execution', dailySeries: [], byWeekday: [], peakWeekday: null },
    tokens: {
      conversationRegime: 'execution',
      embeddingRegime: 'embedding',
      conversation: { inputTokens: null, outputTokens: null, cachedInputTokens: null },
      searchEmbeddingInputTokens: null,
      byProvider: [],
      byModel: [],
      perTask: { average: null, p95: null },
      caveats: [],
    },
    performance: {
      regime: 'execution',
      taskDuration: { ...semStats },
      queueTime: { ...semStats },
      providerCallDuration: { ...semStats },
      providerCallsPerTask: null,
      nonProviderResidual: { ...semStats },
      maxDepthAtWhichAgentRan: null,
      caveats: [],
    },
    errors: {
      regime: 'execution',
      rejectionRegime: 'rejection',
      failedCount: 0,
      rejectedCount: 0,
      rejectedAtEntryCount: 0,
      byProviderAndModel: [],
      byPhase: [],
      rejectionsByReason: [],
      nonTerminal: { openExecutionCount: 0, neverConsumedCount: 0, observedStates: [] },
      caveats: [],
    },
    delegation: { regime: 'execution', delegatesTo: [], triggeredBy: [], caveats: [] },
  };
}

function formatarInstante(iso: string, timeZone: string): string {
  return new Intl.DateTimeFormat('pt-BR', {
    timeZone,
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
  }).format(new Date(iso));
}

export interface AgentInsightsTabProps {
  agentId: string;
  /** O cadastro de SAÍDA do agente, como o detalhe já o traz. */
  registeredTargets: { id: string; name: string }[];
  /** O catálogo inteiro, ou `undefined` enquanto ele não respondeu. */
  agentsCatalog: DelegationCatalogAgent[] | undefined;
}

export function AgentInsightsTab({
  agentId,
  registeredTargets,
  agentsCatalog,
}: AgentInsightsTabProps) {
  const [period, setPeriod] = useState<InsightsPeriod>(DEFAULT_INSIGHTS_PERIOD);
  const insightsQuery = useAgentInsightsQuery(agentId, period);

  const naoEncontrado =
    insightsQuery.error instanceof ApiError && insightsQuery.error.status === 404;

  // `ok` SÓ de `isSuccess`. Não `!isError`: a negação deixa o pendente passar.
  const queryState: QueryState = insightsQuery.isSuccess
    ? 'ok'
    : insightsQuery.isError
      ? 'failed'
      : 'loading';

  const insights = insightsQuery.data ?? corpoAusente(agentId);

  const razao =
    queryState === 'loading'
      ? 'Consultando as métricas deste período.'
      : insightsQuery.error instanceof Error
        ? insightsQuery.error.message
        : 'A consulta não respondeu.';

  // A janela do cabeçalho é a que a RESPOSTA ecoa, e só cai no cálculo local
  // enquanto ela não chegou — a resposta é a autoridade sobre o que foi
  // consultado.
  const janelaLocal = useMemo(() => insightsWindow(period, new Date()), [period]);
  const respondida = insightsQuery.data?.window;
  const janela = useMemo(
    () => respondida ?? { ...janelaLocal, timeZone: 'America/Sao_Paulo' },
    [respondida, janelaLocal],
  );

  const measured = useMemo(
    () => measuredDays(janela, insights.temporal.dailySeries),
    [janela, insights.temporal.dailySeries],
  );

  const regimeDaAba = insights.regimes[insights.volume.regime] ?? null;

  const desconhecidos = unknownCaveats(
    [
      ...insights.tokens.caveats,
      ...insights.performance.caveats,
      ...insights.errors.caveats,
      ...insights.delegation.caveats,
    ],
    'agent',
  );

  // O AGENTE NÃO EXISTE PARA A ROTA DE MÉTRICAS. Estado próprio, sem número
  // nenhum na tela e sem nova tentativa.
  if (naoEncontrado) {
    return (
      <Alert
        color="red"
        icon={<TriangleAlert size={18} />}
        title="As métricas deste agente não foram encontradas"
        data-testid="agente-nao-encontrado"
      >
        <Text size="sm">
          A consulta de métricas respondeu que este agente não existe, enquanto o cadastro dele
          foi carregado. As duas fontes discordam, e não há número a apresentar.
        </Text>
      </Alert>
    );
  }

  return (
    <Stack gap="lg" data-testid="aba-insights-do-agente">
      <Group justify="space-between" align="flex-start">
        <Text size="xs" c="dimmed" data-testid="janela-do-periodo-do-agente">
          {janela.from === '' ? null : (
            <>
              {formatarInstante(janela.from, janela.timeZone)} a{' '}
              {formatarInstante(janela.to, janela.timeZone)} · horário local ({janela.timeZone})
            </>
          )}
          {regimeDaAba === null ? null : (
            <Text span data-testid={`medindo-desde-${insights.volume.regime}`}>
              {' '}
              · execução medida desde {formatarInstante(regimeDaAba, janela.timeZone)}
            </Text>
          )}
        </Text>
        <PeriodPicker value={period} onChange={setPeriod} />
      </Group>

      {queryState === 'failed' ? (
        <Alert
          color="red"
          icon={<TriangleAlert size={18} />}
          title="Não foi possível ler as métricas deste período"
          data-testid="erro-da-consulta-do-agente"
        >
          <Stack gap={6} align="flex-start">
            <Text size="sm">
              {razao} Não há como dizer quantas tasks rodaram — nem se rodou alguma.
            </Text>
            <Anchor
              component="button"
              type="button"
              size="sm"
              onClick={() => void insightsQuery.refetch()}
            >
              Tentar de novo
            </Anchor>
          </Stack>
        </Alert>
      ) : null}

      {desconhecidos.length > 0 ? (
        <Alert
          color="yellow"
          title="Avisos não reconhecidos"
          data-testid="caveats-desconhecidos-do-agente"
        >
          {desconhecidos.map((c) => (
            <Text key={c.code} ff="monospace" size="xs">
              {c.code}
            </Text>
          ))}
        </Alert>
      ) : null}

      {/* O quadro 1 do `Estados.dc.html` abre a aba quando a janela pedida
          começa antes da medição — antes dos números, que é onde ele é lido. */}
      {queryState === 'ok' ? (
        <PartialMeasurementNotice
          measured={measured}
          regimeStart={insights.regimes[insights.temporal.regime] ?? null}
          timeZone={janela.timeZone}
        />
      ) : null}

      <AgentKpiGrid insights={insights} queryState={queryState} reason={razao} />

      <Grid gap="sm">
        <Grid.Col span={{ base: 12, lg: 7 }}>
          <TimeBreakdownCard
            performance={insights.performance}
            queryState={queryState}
            reason={razao}
          />
        </Grid.Col>
        <Grid.Col span={{ base: 12, lg: 5 }}>
          <TaskDurationCard
            performance={insights.performance}
            queryState={queryState}
            reason={razao}
          />
        </Grid.Col>
        <Grid.Col span={12}>
          <AgentModelsCard
            byModel={insights.tokens.byModel}
            queryState={queryState}
            reason={razao}
          />
        </Grid.Col>
        <Grid.Col span={{ base: 12, lg: 6 }}>
          <WeekdayActivityCard
            temporal={insights.temporal}
            measured={measured}
            queryState={queryState}
          />
        </Grid.Col>
        <Grid.Col span={{ base: 12, lg: 6 }}>
          <AgentDelegationCard
            delegation={insights.delegation}
            agentId={agentId}
            registeredTargets={registeredTargets}
            catalog={agentsCatalog}
            externalOriginTaskCount={insights.volume.externalOriginTaskCount}
            queryState={queryState}
            reason={razao}
          />
        </Grid.Col>
        <Grid.Col span={12}>
          <AgentFailuresCard
            errors={insights.errors}
            executedTaskCount={insights.volume.executedTaskCount}
            queryState={queryState}
            reason={razao}
          />
        </Grid.Col>
      </Grid>
    </Stack>
  );
}
