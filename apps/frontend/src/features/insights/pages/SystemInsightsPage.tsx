import { useMemo } from 'react';
import { Alert, Anchor, Grid, Group, Stack, Text, Title } from '@mantine/core';
import { useSearchParams } from 'react-router';
import { TriangleAlert } from 'lucide-react';
import { useSystemInsightsQuery } from '../api/useSystemInsights';
import { useAgentsQuery } from '../../agents/api/useAgents';
import {
  insightsWindow,
  parsePeriod,
  type InsightsPeriod,
} from '../utils/insightsWindow';
import { measuredDays } from '../utils/measuredDays';
import { unknownCaveats } from '../utils/caveatLabels';
import { PeriodPicker } from '../components/PeriodPicker';
import { InsightsKpiGrid } from '../components/InsightsKpiGrid';
import { WeekdayActivityCard } from '../components/WeekdayActivityCard';
import { PeriodHeatmapCard } from '../components/PeriodHeatmapCard';
import { DailyTasksCard } from '../components/DailyTasksCard';
import { ProviderConsumptionCard } from '../components/ProviderConsumptionCard';
import { ConversationModelsCard } from '../components/ConversationModelsCard';
import { AgentConsumptionCard } from '../components/AgentConsumptionCard';
import { FailuresCard } from '../components/FailuresCard';
import { FailureReasonsCard } from '../components/FailureReasonsCard';
import { NonTerminalBanner } from '../components/NonTerminalBanner';
import { PartialMeasurementNotice } from '../components/PartialMeasurementNotice';
import type { SystemInsights } from '../types/systemInsights';
import type { QueryState } from '../utils/metricState';

// A PÁGINA BUSCA E REPASSA; OS COMPONENTES APRESENTAM.
//
// Nenhum componente desta feature importa hook de query — todos recebem props.
// É a regra da casa (convenção 7), e aqui ela também é o que torna os
// cinquenta cenários de apresentação testáveis sem montar um QueryClient.
//
// AS DUAS CONSULTAS SÃO INDEPENDENTES, E NADA DE `isLoading || outro.isLoading`.
//
// A rota agregada serve os números; o catálogo serve só os NOMES dos agentes,
// que ela não devolve. Cada uma tem o seu carregamento, o seu erro e a sua nova
// tentativa. Um `&&` entre as duas faria o catálogo lento segurar a página
// inteira — e a falha dele derrubaria números que já chegaram.
//
// O "MEDINDO DESDE" SAI POR REGIME, JUNTO DO GRUPO QUE CADA UM MEDE.
//
// A resposta traz um MAPA de regimes (dois hoje: `execution` e `embedding`), e
// cada bloco declara a qual pertence. Um "medindo desde" único no cabeçalho
// mentiria sobre pelo menos um deles — o embedding começou um dia depois da
// execução. A página não depende de quantos nem de quais: um regime novo é
// absorvido sem mudança de estrutura.

// O ESQUELETO QUE SUSTENTA A TELA ENQUANTO A CONSULTA NÃO RESPONDEU.
//
// Declarado aqui, e NÃO importado de `../test/systemInsightsFixture` — a
// fixture é duplo de teste e não embarca no bundle. Os dois são parecidos de
// propósito e vivem separados pelo mesmo motivo.
//
// TUDO NULO, NENHUM ZERO: o esqueleto não pode afirmar contagem nenhuma. Ele
// só existe para dar forma aos componentes enquanto `queryState` é `'failed'`
// ou os dados não chegaram — e nesse estado cada valor vira travessão, não o
// que estiver aqui. As contagens `int` do contrato ficam em 0 por serem
// `number`, e é justamente por isso que `queryState` vence sobre o valor em
// `readMetric`.
const CORPO_AUSENTE: SystemInsights = {
  window: { from: '', to: '', timeZone: 'America/Sao_Paulo' },
  regimes: {},
  volume: { regime: 'execution', executedTaskCount: 0, externalOriginTaskCount: 0 },
  temporal: { regime: 'execution', dailySeries: [], byWeekday: [], peakWeekday: null },
  tokens: {
    conversationRegime: 'execution',
    embeddingRegime: 'embedding',
    conversation: { inputTokens: null, outputTokens: null, cachedInputTokens: null },
    embeddingInputTokens: null,
    byAgent: [],
    byProvider: [],
    byModel: [],
    perTask: { average: null, p95: null },
  },
  performance: {
    regime: 'execution',
    taskDuration: { averageMs: null, p95Ms: null, sampleCount: 0 },
    queueTime: { averageMs: null, p95Ms: null, sampleCount: 0 },
    providerCallDuration: { averageMs: null, p95Ms: null, sampleCount: 0 },
    providerCallsPerTask: null,
    nonProviderResidual: { averageMs: null, p95Ms: null, sampleCount: 0 },
    maxObservedDelegationDepth: null,
    caveats: [],
  },
  errors: {
    executionRegime: 'execution',
    indexingRegime: 'embedding',
    rejectionRegime: 'rejection',
    failedCount: 0,
    rejectedCount: 0,
    rejectedAtEntryCount: 0,
    byAgent: [],
    byPhase: [],
    rejectionsByReason: [],
    indexingFailures: [],
    nonTerminal: { openExecutionCount: 0, neverConsumedCount: 0, observedStates: [] },
    caveats: [],
  },
  delegation: { regime: 'execution', pairs: [] },
};

// O NOME DO REGIME → RÓTULO DE OPERADOR, E ELE CARREGA A CONCORDÂNCIA.
//
// Os nomes são chaves de contrato de `MetricsOptions.All` (`execution`,
// `embedding`, `rejection`), nunca texto de operador — e a tela tinha DOIS sítios
// traduzindo, cada um com a sua regra própria: o cabeçalho da página fazia
// `regime === 'execution' ? 'execução medida' : `${regime} medido`` e o
// `regimeNoteFor` fazia `name === embeddingRegime ? 'embedding' : name`. Dois
// sítios, duas regras, e nenhum dos dois cobria o terceiro regime.
//
// **A CONCORDÂNCIA É O MOTIVO DE O MAPA GUARDAR A FRASE E NÃO O SUBSTANTIVO:**
// "execução medid**a**" e "recusa medid**a**" são femininas, "embedding medid**o**"
// é masculino. Um mapa de substantivo obrigaria um segundo mapa de gênero, ou o
// masculino por omissão — que escreveria "recusa medido".
//
// **`embedding` fica em inglês de propósito**, e isso é decisão do dono na tela da
// #52: é o termo que quem opera usa. Não é tradução esquecida, e o guarda negativo
// da página declara a exceção por escrito.
//
// A QUEDA PARA O NOME CRU PERMANECE, e ela é a régua do desconhecido da casa —
// valor não reconhecido aparece, visível, nunca com o rótulo de outro. Ela já
// tinha guarda antes desta change (`regime NOVO é absorvido`, escrito pela #52), e
// continua tendo. O que esta change acrescenta é o guarda do caso OPOSTO: regime
// que a tela CONHECE não sai cru.
const REGIME_LABELS: Record<string, string> = {
  execution: 'execução medida',
  embedding: 'embedding medido',
  rejection: 'recusa medida',
};

function regimeLabel(name: string): string {
  return REGIME_LABELS[name] ?? `${name} medido`;
}

function formatarInstante(iso: string, timeZone: string): string {
  return new Intl.DateTimeFormat('pt-BR', {
    timeZone,
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
  }).format(new Date(iso));
}

export function SystemInsightsPage() {
  // O PERÍODO MORA NO ENDEREÇO, NÃO EM ESTADO LOCAL.
  //
  // Era `useState(DEFAULT_INSIGHTS_PERIOD)`, e o estado local não alcança nem o
  // recarregamento nem o link colado — nem a aba do agente, que é o que fazia o
  // clique único da #67 levar a outra janela de medição em silêncio (#85).
  //
  // As três formas que estavam na mesa, e por que esta: a URL é a ÚNICA que
  // resolve as duas metades. Estado compartilhado (contexto, store) resolve a
  // travessia e perde o refresh; o período só no link do ranking resolve a
  // travessia e perde o refresh também. O mecanismo já é usado nesta casa, pela
  // aba do detalhe do agente.
  //
  // `parsePeriod` CARREGA O CONTRATO — ausência é a forma canônica do padrão,
  // desconhecido cai nela, e o endereço NÃO é reescrito em nenhum dos dois casos.
  // A razão de cada uma dessas três coisas está em `utils/insightsWindow.ts`,
  // junto da função, porque é lá que a próxima pessoa a procura.
  const [searchParams, setSearchParams] = useSearchParams();
  const period = parsePeriod(searchParams.get('period'));

  // ESCREVER SÓ A CHAVE QUE LHE PERTENCE, mesmo com `period` sendo hoje a única
  // chave deste endereço. É a régua que a #96 nomeia e que a D5 corrigiu no
  // detalhe do agente: `setSearchParams({ period })` funcionaria hoje e viraria
  // perda silenciosa no primeiro segundo parâmetro desta página. `prev` é
  // COPIADO, nunca mutado — é a instância que o router entrega.
  const handlePeriodChange = (next: InsightsPeriod) => {
    setSearchParams((prev) => {
      const proximo = new URLSearchParams(prev);
      proximo.set('period', next);
      return proximo;
    });
  };

  const insightsQuery = useSystemInsightsQuery(period);
  const agentsQuery = useAgentsQuery();

  // TRÊS ESTADOS, E O DO MEIO É O QUE FALTAVA.
  //
  // A primeira versão era `isError ? 'failed' : 'ok'`, e enquanto a consulta
  // CORRIA o estado era `'ok'` — o `CORPO_AUSENTE` tem contagens `int` em 0, e
  // a página exibia "Tasks executadas: 0" e "Nenhum consumo por provedor neste
  // período" com a API fora do ar. Cair no estado 2 do `Estados.dc.html` é
  // exatamente o que o quadro 3 proíbe: requisição que não respondeu não é
  // evidência de ausência.
  //
  // Só `isSuccess` autoriza `'ok'`. Não `!isError`: a negação deixa o pendente
  // passar, e foi assim que o defeito entrou.
  const queryState: QueryState = insightsQuery.isSuccess
    ? 'ok'
    : insightsQuery.isError
      ? 'failed'
      : 'loading';
  const insights = insightsQuery.data ?? CORPO_AUSENTE;
  const regimeStartOfPage = insights.regimes[insights.volume.regime] ?? null;

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
    () => respondida ?? { ...janelaLocal, timeZone: CORPO_AUSENTE.window.timeZone },
    [respondida, janelaLocal],
  );

  const measured = useMemo(
    () => measuredDays(janela, insights.temporal.dailySeries),
    [janela, insights.temporal.dailySeries],
  );

  // A nota que vai no CABEÇALHO de um card, e só quando o regime dele DIFERE do
  // de execução — que governa a página e é declarado no cabeçalho dela, ao lado
  // da janela. Um regime que a resposta não declara não vira texto nenhum.
  //
  // Declarada DEPOIS de `insights` e `janela`, que ela usa: antes deles, o
  // compilador do React não conseguia preservar a memoização dos `useMemo`
  // abaixo e pulava a compilação do componente inteiro.
  const regimeNoteFor = (name: string): string | undefined => {
    const inicio = insights.regimes[name];
    if (inicio === undefined || name === insights.volume.regime) {
      return undefined;
    }
    return `${regimeLabel(name)} desde ${formatarInstante(inicio, janela.timeZone)}`;
  };

  const agentNames = useMemo(
    () =>
      agentsQuery.data === undefined
        ? undefined
        : new Map(agentsQuery.data.map((a) => [a.id, a.name])),
    [agentsQuery.data],
  );

  // Códigos que a tela não conhece: aparecem, visíveis, em vez de sumir.
  const desconhecidos = unknownCaveats([
    ...insights.performance.caveats,
    ...insights.errors.caveats,
  ]);

  return (
    <Stack gap="lg" data-testid="pagina-insights">
      <Group justify="space-between" align="flex-start">
        <Stack gap={2}>
          <Title order={1}>Insights</Title>
          {/* A JANELA E O REGIME DE EXECUÇÃO, lado a lado — é onde o
              `Main.dc.html` os põe: "21/08/2026 a 19/09/2026 · medindo desde
              15/07/2026".

              O regime de execução rege volume, série temporal, desempenho e
              tokens de conversa. Ele esteve numa linha SOLTA abaixo da grade de
              KPIs, que era o último rodapé flutuante da tela; o dono pediu para
              tirá-la, e tirá-la sem mais nada deixaria o regime que governa
              quase a página inteira sem declaração nenhuma.

              O REGIME É NOMEADO, e por isso não é o "medindo desde único" que a
              spec proíbe: a proibição existe porque um texto único mentiria
              sobre um dos dois regimes, e este diz de qual fala. O de embedding
              continua nos cabeçalhos dos dois cards em que números dele
              aparecem. */}
          <Text size="xs" c="dimmed" data-testid="janela-do-periodo">
            {formatarInstante(janela.from, janela.timeZone)} a{' '}
            {formatarInstante(janela.to, janela.timeZone)} · horário local ({janela.timeZone})
            {regimeStartOfPage === null ? null : (
              <Text span data-testid={`medindo-desde-${insights.volume.regime}`}>
                {' '}
                · {regimeLabel(insights.volume.regime)} desde{' '}
                {formatarInstante(regimeStartOfPage, janela.timeZone)}
              </Text>
            )}
          </Text>
        </Stack>
        <PeriodPicker value={period} onChange={handlePeriodChange} />
      </Group>

      {insightsQuery.isError ? (
        <Alert
          color="red"
          icon={<TriangleAlert size={18} />}
          title="Não foi possível ler as métricas deste período"
          data-testid="erro-da-consulta"
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
        <Alert color="yellow" title="Avisos não reconhecidos" data-testid="caveats-desconhecidos">
          {desconhecidos.map((c) => (
            <Text key={c.code} ff="monospace" size="xs">
              {c.code}
            </Text>
          ))}
        </Alert>
      ) : null}

      {/* O quadro 1 do `Estados.dc.html` abre a página quando a janela pedida
          começa antes da medição — antes dos números, que é onde ele é lido. */}
      {queryState === 'ok' ? (
        <PartialMeasurementNotice
          measured={measured}
          regimeStart={insights.regimes[insights.temporal.regime] ?? null}
          timeZone={janela.timeZone}
        />
      ) : null}

      <InsightsKpiGrid insights={insights} queryState={queryState} reason={razao} />

      <Grid gap="sm">
        <Grid.Col span={{ base: 12, lg: 6 }}>
            <WeekdayActivityCard
              temporal={insights.temporal}
              measured={measured}
              queryState={queryState}
            />
        </Grid.Col>
        <Grid.Col span={{ base: 12, lg: 6 }}>
          <PeriodHeatmapCard
            measured={measured}
            timeZone={janela.timeZone}
            queryState={queryState}
          />
        </Grid.Col>
        <Grid.Col span={12}>
          <DailyTasksCard measured={measured} queryState={queryState} />
        </Grid.Col>
        <Grid.Col span={{ base: 12, lg: 6 }}>
            <ProviderConsumptionCard
              byProvider={insights.tokens.byProvider}
              queryState={queryState}
              reason={razao}
              regimeNote={regimeNoteFor(insights.tokens.embeddingRegime)}
            />
        </Grid.Col>
        <Grid.Col span={{ base: 12, lg: 6 }}>
          <ConversationModelsCard
            byModel={insights.tokens.byModel}
            queryState={queryState}
            reason={razao}
          />
        </Grid.Col>
        <Grid.Col span={12}>
          <AgentConsumptionCard
            byAgent={insights.tokens.byAgent}
            failuresByAgent={insights.errors.byAgent}
            agentNames={agentNames}
            catalogLoading={agentsQuery.isLoading}
            catalogFailed={agentsQuery.isError}
            onRetryCatalog={() => void agentsQuery.refetch()}
            period={period}
            queryState={queryState}
            reason={razao}
          />
        </Grid.Col>
        <Grid.Col span={{ base: 12, lg: 6 }}>
            <FailuresCard
              errors={insights.errors}
              executedTaskCount={insights.volume.executedTaskCount}
              queryState={queryState}
              reason={razao}
              // O TERCEIRO REGIME DA RESPOSTA, e o único número desta página que o
              // declara. Vai para o quadro da recusa, junto da CONTAGEM; o card de
              // Motivos, que decompõe esse mesmo número, não o repete.
              rejectionRegimeNote={regimeNoteFor(insights.errors.rejectionRegime)}
            />
        </Grid.Col>
        <Grid.Col span={{ base: 12, lg: 6 }}>
            {/* SEM NOTA DE REGIME, e é decisão do dono na conferência: o card
                mostra três populações de três regimes, e nomear um deles no
                cabeçalho afirma que tudo ali é daquele. Ver o cabeçalho do
                componente para as duas alternativas recusadas.

                `regimeNoteFor` continua servindo o card de consumo por provedor e
                o quadro de recusa do card de Falhas — o caminho é comum, e só
                este consumidor sai. */}
            <FailureReasonsCard
              errors={insights.errors}
              queryState={queryState}
              reason={razao}
            />
        </Grid.Col>
      </Grid>

      {/* O AVISO FECHA A PÁGINA, como no `Main.dc.html` — não a abre.
          
          Estava no topo por julgamento do agente de que aviso vai em cima. O
          protótipo diz o contrário, e tem razão: o que ele relata são algumas
          tasks entre as do período, não uma interrupção de serviço. No topo,
          empurrava os números para baixo e tomava a primeira leitura da tela. */}
      <NonTerminalBanner errors={insights.errors} />
    </Stack>
  );
}
