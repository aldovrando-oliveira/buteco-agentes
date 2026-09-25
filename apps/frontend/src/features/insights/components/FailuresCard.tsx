import { Box, Card, Group, Stack, Text } from '@mantine/core';
import type { ErrorInsights } from '../types/systemInsights';
import { MetricValue } from './MetricValue';
import {
  METRIC_SIZE,
  formatCount,
  formatRatio,
  ratio,
  readMetric,
  type QueryState,
} from '../utils/metricState';
import { caveatsFor } from '../utils/caveatLabels';

// "Falhas" — DOIS NÚMEROS SEPARADOS, E SÓ UM DELES TEM PERCENTUAL (D10).
//
// A separação é do protótipo, e o texto dele é literal: "Recusa é agente
// inativo ou sem provider e modelo configurados: nunca chega a processar. Falha
// é execução que começou e quebrou. Somar os dois esconde qual dos dois
// problemas existe."
//
// O PERCENTUAL SÓ VALE ONDE NUMERADOR E DENOMINADOR CONTAM A MESMA POPULAÇÃO.
//
// `failedCount` sai de `task_executions`, e `executedTaskCount` também — mesma
// população, percentual legítimo. `rejectedCount` NÃO produz linha de execução
// (é o que `rejections-missing-from-executions` declara), então ele SUBCONTA o
// denominador: "1,1% das tasks" seria um percentual sobre um total que não
// inclui as próprias recusas. O protótipo escreve esse percentual; ele sai, e o
// texto do caveat entra no lugar.
//
// E `ratio` guarda a divisão por zero: sem task executada no período, não há
// percentual nenhum — nem `0%`, nem `NaN`.

export interface FailuresCardProps {
  errors: ErrorInsights;
  executedTaskCount: number;
  queryState: QueryState;
  reason?: string;
}

export function FailuresCard({
  errors,
  executedTaskCount,
  queryState,
  reason,
}: FailuresCardProps) {
  const percentualFalha = ratio(errors.failedCount, executedTaskCount);
  const leituraPercentual = readMetric(percentualFalha, queryState, formatRatio);
  const caveatsRecusa = caveatsFor(errors.caveats, 'rejection-count');

  return (
    <Card withBorder padding={0} data-testid="card-falhas">
      <Box px="md" py="sm" bg="var(--buteco-surface-subtle)">
        <Text size="sm" fw={600}>
          Falhas
        </Text>
      </Box>
      <Stack gap="md" p="md">
        {/* OS DOIS QUADROS, DE MESMO TAMANHO — medidos no `Main.dc.html`:
            `flex-grow: 1`, fundo da superfície sutil, raio `sm`, 12px de
            padding. `align="stretch"` no Group é o que os mantém da mesma
            ALTURA mesmo com conteúdos de tamanhos diferentes.

            A primeira versão não tinha quadro nenhum: eram dois blocos de texto
            soltos, com largura dirigida pelo conteúdo — e por isso o da recusa,
            que carrega uma frase, ficava o dobro do outro. O destaque das duas
            métricas, que é o que o card existe para dar, se perdia.

            AS CORES SÃO DO ARTBOARD e distinguem os dois problemas: falha em
            `red[4]`, recusa em `yellow[4]`. Não são decoração — são a mesma
            separação que o texto abaixo explica, dita em cor. */}
        <Group align="stretch" gap="sm" grow>
          <Box
            data-testid="bloco-falhas"
            style={{
              background: 'var(--buteco-surface-subtle)',
              borderRadius: 'var(--mantine-radius-sm)',
              padding: 12,
            }}
          >
            <Stack gap={2}>
              <Text size="xs" c="dimmed">
                Falharam na execução
              </Text>
              <MetricValue
                value={errors.failedCount}
                queryState={queryState}
                reason={reason}
                format={formatCount}
                size={METRIC_SIZE.card}
                fw={400}
                c="var(--mantine-color-red-filled)"
                data-testid="falhas-contagem"
              />
              {/* Percentual só aqui: mesma população no numerador e no denominador. */}
              {leituraPercentual.state === 'value' || leituraPercentual.state === 'zero' ? (
                <Text size="xs" c="dimmed" data-testid="falhas-percentual">
                  {leituraPercentual.state === 'zero' ? '0%' : leituraPercentual.text} das tasks
                </Text>
              ) : null}
            </Stack>
          </Box>

          <Box
            data-testid="bloco-recusas"
            style={{
              background: 'var(--buteco-surface-subtle)',
              borderRadius: 'var(--mantine-radius-sm)',
              padding: 12,
            }}
          >
            <Stack gap={2}>
              <Text size="xs" c="dimmed">
                Recusadas na entrada
              </Text>
              <MetricValue
                value={errors.rejectedCount}
                queryState={queryState}
                reason={reason}
                format={formatCount}
                size={METRIC_SIZE.card}
                fw={400}
                c="var(--mantine-color-yellow-filled)"
                data-testid="recusas-contagem"
              />
              {/* NO LUGAR DO PERCENTUAL: o caveat que diz por que ele não existe.
                  O artboard escreve "1,1% das tasks" aqui, e a D10 recusa — a
                  recusa não produz linha de execução, então subconta o
                  denominador e o percentual seria sobre um total que não inclui
                  as próprias recusas. */}
              {caveatsRecusa.map((c) => (
                <Text key={c.code} size="xs" c="dimmed" data-testid="recusas-caveat">
                  {c.text}
                </Text>
              ))}
            </Stack>
          </Box>
        </Group>

        {/* Literal do protótipo. */}
        <Text size="xs" c="dimmed" data-testid="nota-falha-versus-recusa">
          Recusa é agente inativo ou sem provider e modelo configurados: nunca chega a processar.
          Falha é execução que começou e quebrou. Somar os dois esconde qual dos dois problemas
          existe.
        </Text>
      </Stack>
    </Card>
  );
}
