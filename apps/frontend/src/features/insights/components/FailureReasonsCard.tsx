import { Box, Card, Group, Stack, Text } from '@mantine/core';
import type { ErrorInsights } from '../types/systemInsights';
import { MetricValue } from './MetricValue';
import { METRIC_SIZE, formatCount, type QueryState } from '../utils/metricState';
import { executionPhaseLabel, indexingFailureLabel } from '../utils/failurePhaseLabels';

// "Motivos" — AS FASES TRADUZIDAS, E A RECUSA COMO LACUNA DECLARADA.
//
// L1 — O MOTIVO DAS RECUSAS NÃO TEM FONTE (#51).
//
// O protótipo desenha a linha "Agente sem provider ou modelo configurado — 5".
// A rota serve `rejectedCount` SEM motivo, e declara isso em
// `rejection-reason-not-collected`. A contagem entra; a CAUSA não é nomeada.
//
// Nomear a causa seria a pior das três opções disponíveis: a linha do protótipo
// é plausível (é de fato uma das causas de recusa), e por isso ninguém
// desconfiaria dela — a tela afirmaria com precisão de número uma causa que
// ninguém mediu. Omitir a linha seria a segunda pior: a soma dos motivos
// deixaria de fechar com a contagem de recusas, sem sintoma.
//
// A ordem é: M29 vira change própria (#51), e nasce DEPOIS desta, sabendo o
// formato que este card pediu.
//
// FASE DESCONHECIDA APARECE NEUTRA, com o valor cru — nunca o rótulo de outra.

export interface FailureReasonsCardProps {
  errors: ErrorInsights;
  queryState: QueryState;
  reason?: string;
  /** Início do regime, só quando ele DIFERE do de execução (que governa a página). */
  regimeNote?: string;
}

export function FailureReasonsCard({
  errors,
  queryState,
  reason,
  regimeNote,
}: FailureReasonsCardProps) {
  const linhas = [
    ...errors.byPhase.map((p) => ({
      chave: `fase-${p.phase}`,
      label: executionPhaseLabel(p.phase),
      count: p.count,
    })),
    ...errors.indexingFailures.map((f) => ({
      chave: `indexacao-${f.outcome}-${f.failurePhase ?? 'sem-fase'}`,
      label: indexingFailureLabel(f.outcome, f.failurePhase),
      count: f.count,
    })),
  ].sort((a, b) => b.count - a.count);

  const semMotivoNemRecusa = linhas.length === 0 && errors.rejectedCount === 0;

  return (
    <Card withBorder padding={0} data-testid="card-motivos">
      <Box px="md" py="sm" bg="var(--buteco-surface-subtle)">
        <Group justify="space-between" align="center" wrap="nowrap">
          <Text size="sm" fw={600}>
            Motivos
          </Text>
          {/* A nota de regime vive NO CABEÇALHO, à direita, no mesmo idioma do
              "máximo · mínimo" do gráfico — e não num rodapé flutuante fora do
              card, que não é idioma de lugar nenhum do painel.
              
              E só aparece onde o regime MUDA: o de execução governa quase toda
              a página e é declarado uma vez, sob os números de destaque.
              Repeti-lo em cada card seria o mesmo ruído noutro lugar. */}
          {regimeNote === undefined ? null : (
            <Text size="xs" c="dimmed" data-testid="nota-de-regime">
              {regimeNote}
            </Text>
          )}
        </Group>
      </Box>
      <Stack gap="sm" p="md">
        {queryState !== 'ok' ? (
          <MetricValue
            value={null}
            queryState={queryState}
            reason={reason}
            size={METRIC_SIZE.card}
            data-testid="motivos-travessao"
          />
        ) : semMotivoNemRecusa ? (
          <Text size="sm" c="dimmed" data-testid="motivos-vazio">
            Nenhuma falha nem recusa neste período.
          </Text>
        ) : (
          <Stack gap="xs">
            {linhas.map((linha) => (
              <Group key={linha.chave} justify="space-between" data-testid={`motivo-${linha.chave}`}>
                <Text
                  size="sm"
                  c={linha.label.unknown ? 'dimmed' : undefined}
                  ff={linha.label.unknown ? 'monospace' : undefined}
                  data-unknown-phase={linha.label.unknown ? 'true' : undefined}
                >
                  {linha.label.text}
                </Text>
                <MetricValue
                  value={linha.count}
                  queryState={queryState}
                  format={formatCount}
                  size="sm"
                  fw={400}
                  data-testid={`motivo-${linha.chave}-contagem`}
                />
              </Group>
            ))}
          </Stack>
        )}

      </Stack>
    </Card>
  );
}
