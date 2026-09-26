import { Box, Card, Group, Stack, Text } from '@mantine/core';
import type { TemporalInsights } from '../types/systemInsights';
import type { MeasuredDays } from '../utils/measuredDays';
import { formatCount, type QueryState } from '../utils/metricState';

// "Dias da semana com mais atividade" — as SETE linhas, sempre.
//
// A COBERTURA VEM DA SÉRIE DIÁRIA, NÃO DE `byWeekday`.
//
// `byWeekday` omite o dia da semana sem ocorrência — mesmo defeito que a série
// diária tinha antes da #65, e a spec da change que criou a rota NÃO o cobre.
// Então a ausência de um dia em `byWeekday` é ambígua sozinha, e quem desfaz a
// ambiguidade é a série: se o dia da semana OCORRE entre os dias medidos, a
// ausência significa zero medido; se NÃO ocorre, significa não medido, e não há
// zero para dar.
//
// É a única reconstrução que sobra no cliente, e está registrada como item
// aberto no `02` (D2) — a fonte dela continua sendo a série, nunca o regime.
//
// ------------------------------------------------- O CARD NÃO TEM NOTA DE RODAPÉ
//
// Ele teve duas, e as duas saíram por decisão do dono na conferência manual de
// 26/09/2026: *"os gráficos em si são autoexplicativos"*.
//
//   - "Dia da semana sem célula preenchida não ocorreu na faixa medida" — a
//     distinção continua VISÍVEL sem ela: o dia medido e vazio mostra `0`, e o
//     não medido mostra célula vazia. O texto nomeava o que o desenho já separa;
//   - a nota de cenário da aba do agente, que dizia se o padrão semanal era
//     próprio ou de quem aciona. Ela veio dos artboards e saiu junto — a prop
//     `note` que a carregava foi REMOVIDA em vez de ficar sem uso.
//
// **O COMPORTAMENTO NÃO MUDOU**, e é o que os guardas continuam afirmando: dia
// coberto e sem ocorrência sai `0`, dia não coberto sai vazio e **nunca** `0`.
// O que saiu foi texto, não regra — a `system-insights-ui` exige a distinção,
// não a frase.

const WEEKDAY_LABELS = ['Dom', 'Seg', 'Ter', 'Qua', 'Qui', 'Sex', 'Sáb'];

/** Segunda a domingo, como o protótipo desenha. */
const DISPLAY_ORDER = [1, 2, 3, 4, 5, 6, 0];

export interface WeekdayActivityCardProps {
  temporal: TemporalInsights;
  measured: MeasuredDays;
  queryState: QueryState;
}

export function WeekdayActivityCard({
  temporal,
  measured,
  queryState,
}: WeekdayActivityCardProps) {
  const counts = new Map(temporal.byWeekday.map((p) => [p.weekday, p.taskCount]));
  const max = Math.max(0, ...temporal.byWeekday.map((p) => p.taskCount));

  return (
    <Card withBorder padding={0} data-testid="card-dias-da-semana">
      <Box px="md" py="sm" bg="var(--buteco-surface-subtle)">
        <Text size="sm" fw={600}>
          Dias da semana com mais atividade
        </Text>
      </Box>
      <Stack gap="sm" p="md">
        {DISPLAY_ORDER.map((weekday) => {
          const coberto = measured.coveredWeekdays.has(weekday);
          const count = counts.get(weekday);
          // Coberto e ausente de `byWeekday` é ZERO MEDIDO; não coberto é NÃO
          // MEDIDO, e a célula fica vazia.
          const valor = coberto ? (count ?? 0) : null;
          const ehPico =
            temporal.peakWeekday !== null && temporal.peakWeekday === weekday && valor !== null;

          return (
            <Group
              key={weekday}
              gap="sm"
              wrap="nowrap"
              data-testid={`dia-da-semana-${weekday}`}
              data-covered={coberto ? 'true' : 'false'}
              data-peak={ehPico ? 'true' : undefined}
            >
              <Text size="xs" c="dimmed" w={32} style={{ flexShrink: 0 }}>
                {WEEKDAY_LABELS[weekday]}
              </Text>
              <Box
                style={{
                  flexGrow: 1,
                  height: 18,
                  borderRadius: 'var(--mantine-radius-xs)',
                  background: 'var(--buteco-surface-subtle)',
                  overflow: 'hidden',
                }}
              >
                {valor !== null && queryState === 'ok' && max > 0 ? (
                  <Box
                    style={{
                      height: 18,
                      width: `${Math.round((valor / max) * 100)}%`,
                      background: ehPico
                        ? 'var(--mantine-primary-color-filled)'
                        : 'var(--buteco-heat-3)',
                    }}
                  />
                ) : null}
              </Box>
              <Text
                ff="monospace"
                size="xs"
                w={40}
                ta="right"
                style={{ flexShrink: 0 }}
                data-testid={`dia-da-semana-${weekday}-valor`}
                data-metric-state={
                  queryState !== 'ok' ? 'unknown' : valor === null ? 'empty' : valor === 0 ? 'zero' : 'value'
                }
              >
                {queryState !== 'ok'
                  ? '—'
                  : valor === null
                    ? ' '
                    : formatCount(valor)}
              </Text>
            </Group>
          );
        })}

      </Stack>
    </Card>
  );
}
