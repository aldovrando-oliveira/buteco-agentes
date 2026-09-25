import { Box, Card, Group, Stack, Table, Text } from '@mantine/core';
import type { ProviderTokens } from '../types/systemInsights';
import { MetricValue } from './MetricValue';
import { formatTokens, sumKnown, type QueryState } from '../utils/metricState';

// "Consumo por provedor" — A TABELA QUE CARREGA A DEMONSTRAÇÃO DA CÉLULA VAZIA.
//
// A coluna Embedding chega NULA para os provedores que não fazem esse tipo de
// chamada neste sistema — o `Main.dc.html` já a desenha vazia para `anthropic` e
// `gemini`. Célula vazia ali NÃO diz que o consumo foi zero: diz que aquele
// provedor não atende aquele tipo de chamada, ou não reporta o número. Escrever
// `0` inventaria uma economia que ninguém mediu.
//
// É a irmã sobrevivente da coluna "Cache lido" da tabela de modelos, que saiu
// como lacuna declarada (L3): o requisito da célula vazia é cumprido pela tabela
// que TEM a fonte, e a demonstração não se perde.
//
// O TOTAL SOMA SÓ O CONHECIDO, e fica vazio quando todas as parcelas são nulas —
// `sumKnown`, nunca `(a ?? 0) + (b ?? 0)`.

export interface ProviderConsumptionCardProps {
  byProvider: ProviderTokens[];
  queryState: QueryState;
  reason?: string;
  /** Início do regime, só quando ele DIFERE do de execução (que governa a página). */
  regimeNote?: string;
}

export function ProviderConsumptionCard({
  byProvider,
  queryState,
  reason,
  regimeNote,
}: ProviderConsumptionCardProps) {
  return (
    <Card withBorder padding={0} data-testid="card-consumo-por-provedor">
      <Box px="md" py="sm" bg="var(--buteco-surface-subtle)">
        <Group justify="space-between" align="center" wrap="nowrap">
          <Text size="sm" fw={600}>
            Consumo por provedor
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
        {byProvider.length === 0 && queryState === 'ok' ? (
          <Text size="sm" c="dimmed" data-testid="consumo-por-provedor-vazio">
            Nenhum consumo por provedor neste período.
          </Text>
        ) : (
          <Table data-testid="tabela-provedores">
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Provedor</Table.Th>
                <Table.Th ta="right">Conversa</Table.Th>
                <Table.Th ta="right">Embedding</Table.Th>
                <Table.Th ta="right">Total</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {byProvider.map((linha) => {
                const conversa = sumKnown([
                  linha.conversationInputTokens,
                  linha.conversationOutputTokens,
                ]);
                const total = sumKnown([conversa, linha.embeddingInputTokens]);

                return (
                  <Table.Tr key={linha.provider} data-testid={`provedor-${linha.provider}`}>
                    <Table.Td ff="monospace">{linha.provider}</Table.Td>
                    <Table.Td ta="right">
                      <MetricValue
                        value={conversa}
                        queryState={queryState}
                        reason={reason}
                        format={formatTokens}
                        size="sm"
                        fw={400}
                        data-testid={`provedor-${linha.provider}-conversa`}
                      />
                    </Table.Td>
                    <Table.Td ta="right">
                      <MetricValue
                        value={linha.embeddingInputTokens}
                        queryState={queryState}
                        reason={reason}
                        format={formatTokens}
                        size="sm"
                        fw={400}
                        data-testid={`provedor-${linha.provider}-embedding`}
                      />
                    </Table.Td>
                    <Table.Td ta="right">
                      <MetricValue
                        value={total}
                        queryState={queryState}
                        reason={reason}
                        format={formatTokens}
                        size="sm"
                        fw={500}
                        data-testid={`provedor-${linha.provider}-total`}
                      />
                    </Table.Td>
                  </Table.Tr>
                );
              })}
            </Table.Tbody>
          </Table>
        )}
        {/* O texto é do protótipo, literal. */}
        <Text size="xs" c="dimmed" data-testid="nota-celula-vazia">
          Célula vazia significa que o provedor não atende esse tipo de chamada neste sistema —
          não que o consumo seja zero.
        </Text>
      </Stack>
    </Card>
  );
}
