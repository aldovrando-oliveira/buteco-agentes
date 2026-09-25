import { Box, Card, Group, Stack, Text } from '@mantine/core';
import type { MeasuredDays } from '../utils/measuredDays';
import { weekdayOf } from '../utils/measuredDays';
import { HEAT_STEPS, heatStep, heatVariable } from '../utils/heatScale';
import { formatCount, type QueryState } from '../utils/metricState';

// "Calendário do período" — QUATRO ESTADOS DE CÉLULA, e o quarto é a ausência
// de célula.
//
//   medido      → um passo da escala de intensidade (1 a 5)
//   zero medido → o passo 0, próprio, nunca o passo 1
//   não medido  → HACHURA, e nenhum passo da escala
//   fora da janela → sem célula
//
// A HACHURA É O QUE O QUADRO 1 DO `Estados.dc.html` DESENHA, e o texto dele diz
// por quê: "preencher com zero faria a série inventar um período de inatividade
// que nunca existiu". Ela não é um sexto tom — é outra textura, e é isso que a
// torna distinguível do zero medido mesmo em monocromático.
//
// A QUANTIZAÇÃO É DO CLIENTE E LINEAR (`heatScale.ts`). O protótipo crava
// limiares 8/16/24/30, que só valem para os dados de exemplo dele.
//
// A CÉLULA LÊ `var(--buteco-heat-N)`, NUNCA UM TOM. É o que faz a rampa inverter
// entre os esquemas sem um `if (isDark)` aqui dentro (convenção 15).

const WEEKDAY_LABELS = ['Seg', 'Ter', 'Qua', 'Qui', 'Sex', 'Sáb', 'Dom'];

/** A grade do protótipo é por linha de dia da semana, segunda no topo. */
const ROW_ORDER = [1, 2, 3, 4, 5, 6, 0];

const HATCH =
  'repeating-linear-gradient(135deg, transparent, transparent 3px, var(--buteco-surface-subtle) 3px, var(--buteco-surface-subtle) 6px)';

export interface PeriodHeatmapCardProps {
  measured: MeasuredDays;
  timeZone: string;
  queryState: QueryState;
}

export function PeriodHeatmapCard({
  measured,
  timeZone,
  queryState,
}: PeriodHeatmapCardProps) {
  // A grade é preenchida por coluna (uma semana por coluna), e as posições
  // antes do primeiro dia e depois do último ficam SEM CÉLULA — não são dias.
  const primeiro = measured.days[0];
  const offset = primeiro === undefined ? 0 : ROW_ORDER.indexOf(weekdayOf(primeiro.day));

  return (
    <Card withBorder padding={0} data-testid="card-mapa-de-calor">
      <Box px="md" py="sm" bg="var(--buteco-surface-subtle)">
        <Text size="sm" fw={600}>
          Calendário do período
        </Text>
      </Box>
      <Stack gap="md" p="md">
        <Group gap={6} align="flex-start" wrap="nowrap">
          <Stack gap={4}>
            {WEEKDAY_LABELS.map((label) => (
              <Text key={label} size="xs" c="dimmed" h={20} lh="20px">
                {label}
              </Text>
            ))}
          </Stack>
          <Box
            data-testid="mapa-de-calor-grade"
            style={{
              display: 'grid',
              gridTemplateRows: 'repeat(7, 20px)',
              gridAutoFlow: 'column',
              gap: 4,
            }}
          >
            {/* Posições vazias antes do primeiro dia: SEM célula. */}
            {Array.from({ length: offset }, (_, i) => (
              <Box key={`vazio-${i}`} w={20} h={20} data-cell="none" />
            ))}
            {measured.days.map((dia) => {
              const naoMedido = dia.state === 'unmeasured';
              const passo = naoMedido
                ? null
                : heatStep(dia.taskCount as number, measured.maxMeasuredCount);

              const titulo = naoMedido
                ? `${dia.day}: não medido`
                : `${dia.day}: ${formatCount(dia.taskCount as number)} tasks`;

              return (
                <Box
                  key={dia.day}
                  w={20}
                  h={20}
                  title={titulo}
                  aria-label={titulo}
                  data-testid={`celula-${dia.day}`}
                  data-cell={naoMedido ? 'unmeasured' : dia.state === 'measured-zero' ? 'zero' : 'measured'}
                  data-heat-step={passo === null ? undefined : String(passo)}
                  style={{
                    borderRadius: 'var(--mantine-radius-xs)',
                    // Não medido NÃO usa a escala: hachura, e borda tracejada.
                    background:
                      naoMedido || queryState !== 'ok'
                        ? HATCH
                        : heatVariable(passo as (typeof HEAT_STEPS)[number]),
                    border: naoMedido ? '1px dashed var(--mantine-color-default-border)' : undefined,
                    boxSizing: 'border-box',
                  }}
                />
              );
            })}
          </Box>
        </Group>

        <Group gap={6} align="center">
          <Text size="xs" c="dimmed">
            menos
          </Text>
          {HEAT_STEPS.map((step) => (
            <Box
              key={step}
              w={13}
              h={13}
              data-testid={`legenda-passo-${step}`}
              style={{
                borderRadius: 'var(--mantine-radius-xs)',
                background: heatVariable(step),
              }}
            />
          ))}
          <Text size="xs" c="dimmed">
            mais
          </Text>
          <Text size="xs" c="dimmed" ml="auto">
            horário local ({timeZone})
          </Text>
        </Group>

        <Group gap={6} align="center">
          <Box
            w={13}
            h={13}
            data-testid="legenda-nao-medido"
            style={{
              borderRadius: 'var(--mantine-radius-xs)',
              background: HATCH,
              border: '1px dashed var(--mantine-color-default-border)',
              boxSizing: 'border-box',
            }}
          />
          <Text size="xs" c="dimmed">
            hachurado é dia não medido — a coleta não cobria esse dia, e não é dia sem uso
          </Text>
        </Group>
      </Stack>
    </Card>
  );
}
