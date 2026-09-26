import { Box, Card, Group, Stack, Table, Text } from '@mantine/core';
import type { ModelTokens } from '../types/agentInsights';
import { MetricValue } from './MetricValue';
import { DeclaredGap } from './DeclaredGap';
import { formatCount, formatTokens, type QueryState } from '../utils/metricState';

// "Modelos que este agente usou".
//
// ===========================================================================
// O RÓTULO MUDA DE SIGNIFICADO NESTE ESCOPO, E A NOTA É O QUE O CARREGA
// ===========================================================================
//
// Na página do sistema, várias linhas aqui significam que **agentes diferentes**
// usam modelos diferentes. Na aba do agente significam que **este** agente
// mudou de configuração dentro da janela — `Provider` e `Model` de
// `task_executions` são SNAPSHOT do agente no início da execução, gravados de
// propósito sem FK para `agents`, para que o consumo de ontem pertença ao
// modelo de ontem.
//
// É uma das quatro métricas que "mudam de significado com a mesma consulta", e
// copiar o rótulo da tela do sistema faria a aba afirmar uma comparação entre
// agentes que ela não pode fazer.
//
// A nota também não afirma o contrário quando há **uma** linha só: um modelo
// único diz que não houve troca DENTRO DA JANELA, e nada sobre o resto.
//
// ===========================================================================
// AS DUAS LACUNAS DO ARTBOARD, COM TRATAMENTOS DIFERENTES
// ===========================================================================
//
// **"Cache lido" é COLUNA sem fonte → sai, sem deixar quadro no lugar.**
// `ModelTokenResponse` é `(Provider, Model, TotalTokens, CallCount)` — não tem
// cache. O único cache deste escopo é `tokens.conversation.cachedInputTokens`,
// que é o total do AGENTE: reparti-lo por modelo inventaria a distribuição.
// É a L3, issue **#66**, e a regra de que coluna sem fonte sai sem anúncio já
// está fixada na spec da página do sistema.
//
// **"Das N chamadas, M são de compactação" é SUBTÍTULO sem fonte → vira lacuna
// declarada, no mesmo peso do subtítulo.** E o qualificador importa:
// `ProviderCall.Purpose` **É GRAVADO** por `apps/workers`; o que falta é a rota
// devolvê-lo. Escrever "não coletada" aqui seria o mesmo erro com o sinal
// trocado que a página do sistema cometeu e corrigiu — numa tela cujo ponto é
// não afirmar o que o sistema não sabe, afirmar que o sistema não sabe o que
// ele sabe manda alguém abrir change de coleta para um campo já gravado.
// É a L2, issue **#66**.
//
// A DEMONSTRAÇÃO DA CÉLULA VAZIA NÃO SE PERDE COM A SAÍDA DO CACHE: ela passa
// para **Tokens**, que é `long?` na mesma tabela — um modelo cujas chamadas não
// reportaram token nenhum chega nulo. `CallCount` é `int` ao lado, e o par na
// MESMA LINHA é o que separa "não reportou" de "contei e deu zero".
//
// A ORDEM É POR TOKENS, como no artboard, com o nulo no fim: um modelo que não
// reportou tokens não é o de menor consumo, é o de consumo desconhecido, e não
// pode disputar posição com os que reportaram.

export interface AgentModelsCardProps {
  byModel: ModelTokens[];
  queryState: QueryState;
  reason?: string;
}

export function AgentModelsCard({ byModel, queryState, reason }: AgentModelsCardProps) {
  const ordenado = [...byModel].sort((a, b) => {
    if (a.totalTokens === null && b.totalTokens === null) return 0;
    if (a.totalTokens === null) return 1;
    if (b.totalTokens === null) return -1;
    return b.totalTokens - a.totalTokens;
  });

  return (
    <Card withBorder padding={0} data-testid="card-modelos-do-agente">
      <Box px="md" py="sm" bg="var(--buteco-surface-subtle)">
        <Text size="sm" fw={600}>
          Modelos que este agente usou
        </Text>
      </Box>
      <Stack gap="sm" p="md">
        {ordenado.length === 0 && queryState === 'ok' ? (
          <Text size="sm" c="dimmed" data-testid="modelos-do-agente-vazio">
            Nenhuma chamada a modelo neste período.
          </Text>
        ) : (
          <Table data-testid="tabela-modelos-do-agente">
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
                  data-testid={`modelo-do-agente-${linha.model}`}
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
                      data-testid={`modelo-do-agente-${linha.model}-chamadas`}
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
                      data-testid={`modelo-do-agente-${linha.model}-tokens`}
                    />
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        )}

        {/* UMA LINHA SÓ, como o artboard. Lá a frase única carrega as duas
            coisas: por que há mais de um modelo, e quantas das chamadas são de
            compactação. A segunda metade não tem fonte (L2, #66), então ela
            entra como lacuna declarada — mas **na mesma linha**, e não num
            parágrafo próprio abaixo. Decisão do dono na conferência manual de
            26/09.

            O `Group` com `wrap` é o que mantém isso como uma linha na largura
            de trabalho e permite a quebra natural em telas estreitas, sem
            virar dois blocos empilhados por construção. */}
        <Group gap={6} align="baseline" wrap="wrap">
          {ordenado.length > 0 && queryState === 'ok' ? (
            <Text size="xs" c="dimmed" data-testid="nota-configuracao-do-agente">
              {ordenado.length > 1
                ? 'São mais de um porque a configuração deste agente mudou dentro do período: cada execução guarda o modelo que usou na hora.'
                : 'Um modelo só no período. O dia em que ele mudar, a linha nova aparece ao lado da antiga.'}
            </Text>
          ) : null}

          <DeclaredGap
            variant="inline"
            label="Separação entre turno e compactação"
            qualifier="não devolvida por esta rota"
            data-testid="modelos-do-agente-lacuna"
          />
        </Group>
      </Stack>
    </Card>
  );
}
