import { Box, Card, Group, Stack, Text, Tooltip } from '@mantine/core';
import { Info } from 'lucide-react';
import type { AgentPerformanceInsights } from '../types/agentInsights';
import { MetricValue } from './MetricValue';
import { formatDurationMs, type QueryState } from '../utils/metricState';
import { caveatsFor } from '../utils/caveatLabels';

// "Onde o tempo foi" — A RÉGUA EMPILHADA, COM O TOTAL NO CABEÇALHO.
//
// ===========================================================================
// A BARRA FOI RECUSADA PELA D5 E DEVOLVIDA PELO DONO — E O MOTIVO IMPORTA
// ===========================================================================
//
// A D5 recusou a barra com esta medição, lida no SQL da rota:
//
//   | parcela              | o que a rota mede                        | população         |
//   |----------------------|------------------------------------------|-------------------|
//   | Fila                 | `avg(StartedAt − SubmittedAt)`, por exec | sem `SubmittedAt` |
//   | Chamadas ao provedor | `avg(DurationMs)` **por CHAMADA**        | todas as chamadas |
//   | Ferramentas          | `avg((EndedAt−StartedAt) − Σ chamadas)`, | sem `EndedAt`     |
//   |                      | por TASK                                 | nulo              |
//
// As três **não são parcelas da mesma quantidade**, e a soma delas não é a
// duração média da task. A recusa foi por isso.
//
// **O dono devolveu a barra na conferência manual de 26/09, com a medição na
// mesa:** *"entendo que as medições são independentes e por isso somar não
// representa a média da task, mas não tem problema"*. É decisão dele, tomada
// sabendo o que o número é — e é a forma que a convenção 17 prevê: contrariar
// o registro é resultado legítimo quando vira registro, e não implementação
// silenciosa.
//
// **O que a implementação faz para não afirmar mais do que sabe:**
//
//   - o cabeçalho diz **"total das três"**, e não "mediana por task" nem
//     "média por task". O número é a soma das três parcelas, e o rótulo diz
//     exatamente isso. Chamar de mediana afirmaria que metade das tasks foi
//     mais rápida (é a L5, #66); chamar de média por task afirmaria a média que
//     a soma não produz;
//   - **a barra só é desenhada com as três parcelas conhecidas.** Compor área a
//     partir de parcela nula continua proibido pela spec, e continua com
//     guarda: uma faixa de largura zero no lugar do desconhecido afirmaria que
//     aquela etapa levou tempo nenhum.
//
// O que a rota precisaria para a soma ser a duração da task está na **issue
// #81**: `avg(Σ DurationMs por task)` e as três parcelas sobre a mesma
// população.
//
// ===========================================================================
// A TERCEIRA PARCELA VOLTOU A SE CHAMAR "FERRAMENTAS", E O CAVEAT SUBIU
// ===========================================================================
//
// A D4 a tinha renomeado para "Fora do provedor", porque o resíduo inclui
// espera de lock, chamadas MCP e busca vetorial. **O dono devolveu o nome do
// artboard.** O `caveat` `residual-is-not-only-tools` fica ainda mais
// necessário com o nome antigo de volta — ele é exatamente o aviso de que
// "Ferramentas" não é só ferramentas.
//
// Ele virou **ícone no cabeçalho**, a mesma forma do card de Delegação: o
// artboard pede **uma** explicação no rodapé, e ela é a do desenho.
//
// ===========================================================================
// UMA EXPLICAÇÃO SÓ, E É A DO ARTBOARD
// ===========================================================================
//
// *"Ferramentas é o que sobra depois de descontar as chamadas ao provedor da
// duração total — não é medido diretamente."*
//
// A segunda frase do artboard — *"o tempo dos agentes que este aciona está
// dentro destes 4,4 s"* — **não entrou**, e a razão é a mesma que tirou a nota
// de cenário do card de dias da semana na mesma conferência: ela muda com o
// cenário (os três artboards escrevem três frases diferentes), e texto que
// depende de cenário foi o que o dono pediu para sair.

/** As três cores do artboard, resolvidas por esquema pelo próprio Mantine. */
const CORES = {
  fila: 'var(--mantine-color-yellow-filled)',
  provedor: 'var(--mantine-primary-color-filled)',
  ferramentas: 'var(--mantine-color-green-filled)',
} as const;

export interface TimeBreakdownCardProps {
  performance: AgentPerformanceInsights;
  queryState: QueryState;
  reason?: string;
}

export function TimeBreakdownCard({
  performance,
  queryState,
  reason,
}: TimeBreakdownCardProps) {
  const caveats = caveatsFor(performance.caveats, 'residual', 'agent');

  const parcelas = [
    {
      testId: 'tempo-fila',
      label: 'Fila',
      cor: CORES.fila,
      valor: performance.queueTime.averageMs,
    },
    {
      testId: 'tempo-provedor',
      label: 'Chamadas ao provedor',
      cor: CORES.provedor,
      valor: performance.providerCallDuration.averageMs,
    },
    {
      testId: 'tempo-residuo',
      label: 'Ferramentas',
      cor: CORES.ferramentas,
      valor: performance.nonProviderResidual.averageMs,
    },
  ];

  // A SOMA SÓ EXISTE COM AS TRÊS CONHECIDAS. `sumKnown` somaria o que houvesse
  // e chamaria de total — e um "total" que ignora uma parcela desconhecida
  // afirma que ela não pesou nada. Aqui, desconhecido em qualquer uma deixa o
  // total sem valor, e a barra sem desenho.
  const todasConhecidas = parcelas.every((p) => typeof p.valor === 'number');
  const total = todasConhecidas
    ? parcelas.reduce((soma, p) => soma + (p.valor as number), 0)
    : null;
  const podeDesenhar = queryState === 'ok' && total !== null && total > 0;

  return (
    <Card withBorder padding={0} data-testid="card-onde-o-tempo-foi" h="100%">
      <Box px="md" py="sm" bg="var(--buteco-surface-subtle)">
        <Group justify="space-between" align="center" wrap="nowrap">
          <Group gap={6} align="center" wrap="nowrap">
            <Text size="sm" fw={600}>
              Onde o tempo foi
            </Text>
            {caveats.map((c) => (
              <Tooltip key={c.code} label={c.text} multiline w={320} withArrow>
                <Box
                  component="span"
                  data-testid="tempo-residuo-caveat"
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
          <Group gap={4} align="baseline" wrap="nowrap" data-testid="tempo-total">
            <Text size="xs" c="dimmed">
              total das três
            </Text>
            <MetricValue
              value={total}
              queryState={queryState}
              reason={reason}
              format={formatDurationMs}
              size="xs"
              fw={400}
              data-testid="tempo-total-valor"
            />
          </Group>
        </Group>
      </Box>

      <Stack gap="sm" p="md">
        <Box
          data-testid="tempo-barra"
          style={{
            display: 'flex',
            height: 26,
            borderRadius: 'var(--mantine-radius-sm)',
            overflow: 'hidden',
            background: 'var(--buteco-surface-subtle)',
          }}
        >
          {podeDesenhar
            ? parcelas.map((p) => (
                <Box
                  key={p.testId}
                  data-testid={`${p.testId}-faixa`}
                  style={{
                    width: `${((p.valor as number) / (total as number)) * 100}%`,
                    background: p.cor,
                  }}
                />
              ))
            : null}
        </Box>

        <Group gap="lg" wrap="wrap">
          {parcelas.map((p) => (
            <Group key={p.testId} gap={8} align="baseline" wrap="nowrap" data-testid={p.testId}>
              <Box
                style={{
                  width: 10,
                  height: 10,
                  borderRadius: 2,
                  background: p.cor,
                  flexShrink: 0,
                  alignSelf: 'center',
                }}
                aria-hidden="true"
              />
              <Text size="xs">{p.label}</Text>
              <MetricValue
                value={p.valor}
                queryState={queryState}
                reason={reason}
                format={formatDurationMs}
                size="xs"
                fw={400}
                c="dimmed"
                data-testid={`${p.testId}-valor`}
              />
            </Group>
          ))}
        </Group>

        <Text size="xs" c="dimmed" data-testid="tempo-nota">
          Ferramentas é o que sobra depois de descontar as chamadas ao provedor da duração
          total — não é medido diretamente.
        </Text>
      </Stack>
    </Card>
  );
}
