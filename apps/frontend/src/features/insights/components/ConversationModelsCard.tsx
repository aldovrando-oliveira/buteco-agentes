import { Box, Card, Stack, Table, Text } from '@mantine/core';
import type { ModelTokens } from '../types/systemInsights';
import { MetricValue } from './MetricValue';
import { formatCount, formatTokens, type QueryState } from '../utils/metricState';

// "Modelos de conversa".
//
// L3 — A COLUNA "CACHE LIDO" NÃO ENTRA, E É A MAIS PESADA DAS CINCO LACUNAS.
//
// O protótipo a desenha, e ela é DECISÃO DE EXPLORAÇÃO REGISTRADA (`02:4994`):
// "coluna da tabela de modelos, sem número nem card próprio". Mas `byModel` é
// `ModelTokenResponse(Provider, Model, TotalTokens, CallCount)` — não tem cache.
// O único cache que a rota serve é o total global, em
// `conversation.cachedInputTokens`, que é outro nível de agregação: distribuí-lo
// por modelo inventaria a repartição.
//
// A demonstração da célula vazia NÃO se perde com isso — ela continua em
// "Consumo por provedor", na coluna Embedding, que tem a fonte.
//
// A ORDEM É POR TOKENS, que é a do protótipo. A nota diz que a ordem por número
// de chamadas seria outra: o modelo mais chamado não é o que mais consome, e sem
// a nota a tabela parece afirmar as duas coisas ao mesmo tempo.
//
// `totalTokens` é `long?` e `callCount` é `int`: a MESMA LINHA carrega os dois
// estados, e é o par que separa "não reportou" de "contei e deu zero".

export interface ConversationModelsCardProps {
  byModel: ModelTokens[];
  queryState: QueryState;
  reason?: string;
}

export function ConversationModelsCard({
  byModel,
  queryState,
  reason,
}: ConversationModelsCardProps) {
  // Ordem por tokens, com o nulo no fim — um modelo que não reportou tokens não
  // é o de menor consumo, é o de consumo desconhecido, e não pode disputar
  // posição com os que reportaram.
  const ordenado = [...byModel].sort((a, b) => {
    if (a.totalTokens === null && b.totalTokens === null) return 0;
    if (a.totalTokens === null) return 1;
    if (b.totalTokens === null) return -1;
    return b.totalTokens - a.totalTokens;
  });

  return (
    <Card withBorder padding={0} data-testid="card-modelos-de-conversa">
      <Box px="md" py="sm" bg="var(--buteco-surface-subtle)">
        <Text size="sm" fw={600}>
          Modelos de conversa
        </Text>
      </Box>
      <Stack gap="sm" p="md">
        {ordenado.length === 0 && queryState === 'ok' ? (
          <Text size="sm" c="dimmed" data-testid="modelos-vazio">
            Nenhuma chamada a modelo neste período.
          </Text>
        ) : (
          <Table data-testid="tabela-modelos">
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Modelo</Table.Th>
                <Table.Th>Provedor</Table.Th>
                <Table.Th ta="right">Chamadas</Table.Th>
                <Table.Th ta="right">Tokens</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {ordenado.map((linha) => (
                <Table.Tr
                  key={`${linha.provider}/${linha.model}`}
                  data-testid={`modelo-${linha.model}`}
                  data-model-row="true"
                >
                  <Table.Td ff="monospace">{linha.model}</Table.Td>
                  <Table.Td c="dimmed">{linha.provider}</Table.Td>
                  <Table.Td ta="right">
                    <MetricValue
                      value={linha.callCount}
                      queryState={queryState}
                      reason={reason}
                      format={formatCount}
                      size="sm"
                      fw={400}
                      data-testid={`modelo-${linha.model}-chamadas`}
                    />
                  </Table.Td>
                  <Table.Td ta="right">
                    <MetricValue
                      value={linha.totalTokens}
                      queryState={queryState}
                      reason={reason}
                      format={formatTokens}
                      size="sm"
                      fw={400}
                      data-testid={`modelo-${linha.model}-tokens`}
                    />
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        )}

        <Text size="xs" c="dimmed" data-testid="nota-ordenacao">
          Ordenado por tokens. A ordem por número de chamadas seria outra — o modelo mais chamado
          não é necessariamente o que mais consome.
        </Text>

      </Stack>
    </Card>
  );
}
