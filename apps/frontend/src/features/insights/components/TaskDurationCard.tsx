import { Box, Card, Group, Stack, Text, Tooltip } from '@mantine/core';
import { Info } from 'lucide-react';
import type { AgentPerformanceInsights } from '../types/agentInsights';
import { MetricValue } from './MetricValue';
import {
  METRIC_SIZE,
  formatDecimal,
  formatDurationMs,
  type QueryState,
} from '../utils/metricState';
import { caveatsFor } from '../utils/caveatLabels';

// "Duração da task" — OS TRÊS QUADROS INTERNOS DO ARTBOARD.
//
// "MEDIANA" VIRA "MÉDIA", E A PALAVRA É O CONTRATO (D3).
//
// O artboard escreve "Mediana 4,4 s". A rota serve `avg(ms)`, e não existe
// `percentile_cont(0.5)` nela — é a lacuna **L5**, registrada na issue **#66**.
// Chamar média de mediana afirma uma propriedade que o número não tem: quem lê
// "mediana" conclui que metade das tasks foi mais rápida, o que uma média com
// cauda longa não diz. É o mesmo precedente que a página do sistema já fixou.
//
// Calcular a mediana no cliente não é opção: a rota devolve o agregado, não as
// durações.
//
// O QUE A DURAÇÃO INCLUI: `EndedAt − SubmittedAt`, ou seja, a fila está
// **dentro** dela. Não é `EndedAt − StartedAt`. Por isso o card ao lado, que
// separa fila de execução, não é redundante com este.
//
// `submitted-at-missing-on-redelivery` FICA AQUI, junto do número que ele
// limita — é a mesma posição que tem na página do sistema, e uma das quatro em
// que as duas superfícies concordam.
//
// **Mas como ÍCONE no cabeçalho, não como parágrafo no rodapé**, por decisão do
// dono na conferência manual de 26/09: o artboard desenha os três quadros e
// nada mais. É a mesma forma que o card de Delegação já usa, e pela mesma
// razão — o código vem da rota, o número que ele limita está aqui, e descartá-lo
// em silêncio é o que a spec proíbe. Ele NÃO está duplicado em outro lugar da
// aba; se estivesse, sairia daqui como saiu do KPI de Taxa de falha.
//
// A frase "Da submissão ao fim, com a fila incluída" saiu junto: era prosa
// escrita pela tela, não conteúdo da rota, e o artboard não a tem.

export interface TaskDurationCardProps {
  performance: AgentPerformanceInsights;
  queryState: QueryState;
  reason?: string;
}

export function TaskDurationCard({
  performance,
  queryState,
  reason,
}: TaskDurationCardProps) {
  const caveats = caveatsFor(performance.caveats, 'task-duration', 'agent');

  const quadros = [
    {
      testId: 'duracao-media',
      // NÃO "Mediana". Ver o cabeçalho.
      label: 'Média',
      value: performance.taskDuration.averageMs,
      format: formatDurationMs,
    },
    {
      testId: 'duracao-p95',
      label: 'p95',
      value: performance.taskDuration.p95Ms,
      format: formatDurationMs,
    },
    {
      testId: 'duracao-chamadas-por-task',
      label: 'Chamadas por task',
      value: performance.providerCallsPerTask,
      format: formatDecimal,
    },
  ];

  return (
    <Card withBorder padding={0} data-testid="card-duracao-da-task" h="100%">
      <Box px="md" py="sm" bg="var(--buteco-surface-subtle)">
        <Group gap={6} align="center" wrap="nowrap">
          <Text size="sm" fw={600}>
            Duração da task
          </Text>
          {caveats.map((c) => (
            <Tooltip key={c.code} label={c.text} multiline w={320} withArrow>
              <Box
                component="span"
                data-testid="duracao-caveat"
                data-caveat-code={c.code}
                title={c.text}
                aria-label={c.text}
                style={{ display: 'inline-flex', color: 'var(--mantine-color-dimmed)' }}
              >
                <Info size={14} aria-hidden="true" />
              </Box>
            </Tooltip>
          ))}
        </Group>
      </Box>
      <Stack gap="sm" p="md">
        {/* Os três quadros de mesmo tamanho, como o artboard os desenha:
            superfície sutil, raio `sm`, `flex-grow: 1`. `align="stretch"` é o
            que os mantém da mesma altura quando um texto quebra. */}
        <Group gap="sm" align="stretch" grow wrap="nowrap">
          {quadros.map((q) => (
            <Box
              key={q.testId}
              data-testid={q.testId}
              style={{
                background: 'var(--buteco-surface-subtle)',
                borderRadius: 'var(--mantine-radius-sm)',
                padding: 12,
              }}
            >
              <Text size="xs" c="dimmed">
                {q.label}
              </Text>
              <MetricValue
                value={q.value}
                queryState={queryState}
                reason={reason}
                format={q.format}
                size={METRIC_SIZE.card}
                data-testid={`${q.testId}-valor`}
              />
            </Box>
          ))}
        </Group>
      </Stack>
    </Card>
  );
}
