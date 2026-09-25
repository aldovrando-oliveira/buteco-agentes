import { Anchor, Box, Card, Loader, Stack, Table, Text } from '@mantine/core';
import { Link } from 'react-router';
import type { AgentFailures, AgentTokens } from '../types/systemInsights';
import { MetricValue } from './MetricValue';
import { formatCount, formatTokens, sumKnown, type QueryState } from '../utils/metricState';

// "Consumo por agente" — A TABELA QUE CRUZA DOIS CONTRATOS.
//
// A rota agregada devolve `agentId` e NÃO o nome: `AgentTokenResponse(AgentId,
// InputTokens, OutputTokens)`. O nome vem do catálogo (`useAgentsQuery`), que é
// consulta INDEPENDENTE — com o seu próprio carregamento, o seu erro e a sua
// nova tentativa. Catálogo lento não segura os números.
//
// AGENTE FORA DO CATÁLOGO MANTÉM A LINHA, com o identificador no lugar do nome:
// o consumo dele é real, e o que falta é o rótulo, não o número. Sumir com a
// linha faria o total da tabela não fechar com o KPI, sem sintoma.
//
// L4 — TASKS, TOKENS POR TASK E DURAÇÃO P95 NÃO ENTRAM (#67).
//
// O protótipo desenha as três colunas. `byAgent` traz só tokens; as tasks por
// agente e a duração por agente existem na rota do AGENTE, que é outro nível de
// agregação. Preenchê-las com o número de lá seria misturar escopos; com zero,
// seria inventar. As três saem, e a lacuna é declarada no rodapé.
//
// O que a rota SERVE por agente além de tokens é `errors.byAgent`, então a
// coluna Falhas entra — ela tem fonte.

export interface AgentConsumptionCardProps {
  byAgent: AgentTokens[];
  failuresByAgent: AgentFailures[];
  /** `undefined` enquanto o catálogo não respondeu; o número não espera por ele. */
  agentNames: Map<string, string> | undefined;
  catalogLoading: boolean;
  catalogFailed: boolean;
  onRetryCatalog: () => void;
  queryState: QueryState;
  reason?: string;
}

export function AgentConsumptionCard({
  byAgent,
  failuresByAgent,
  agentNames,
  catalogLoading,
  catalogFailed,
  onRetryCatalog,
  queryState,
  reason,
}: AgentConsumptionCardProps) {
  const failures = new Map(failuresByAgent.map((f) => [f.agentId, f.failedCount]));

  // A ÚNICA AUSÊNCIA QUE VIRA ZERO NESTA PÁGINA, E ELA É JUSTIFICADA — não um
  // `?? 0` de conveniência, que a D6 proíbe por nome.
  //
  // `errors.byAgent` é um `group by` sobre as execuções QUE FALHARAM: um agente
  // que aparece em `byAgent` (logo teve consumo medido na janela) e não aparece
  // em `errors.byAgent` teve as falhas dele CONTADAS, e a contagem deu zero. É
  // o mesmo raciocínio que a rota aplica ao dia medido e vazio.
  //
  // A diferença com as outras ausências da tela: aqui a população do
  // denominador é conhecida — o agente está na agregação de tokens. Um agente
  // que não está em NENHUMA das duas listas simplesmente não tem linha, e é
  // isso que impede o zero de se espalhar para quem não foi medido.
  const failedCountOf = (agentId: string): number => {
    const contado = failures.get(agentId);
    return contado === undefined ? 0 : contado;
  };

  // O DESTAQUE DA COLUNA FALHAS — a linha de MAIOR contagem, em vermelho.
  //
  // O `Main.dc.html` pinta um dos números de `red[4]` e deixa os outros em
  // branco, SEM regra declarada. Decidido com o dono em 25/09: destacar o maior
  // valor.
  //
  // **A ressalva está escrita porque ela é real, não para cobrir a escolha:**
  // isto ordena por contagem ABSOLUTA, não por taxa. Um agente com 9 falhas em
  // 1.000 tasks fica marcado, e um com 3 em 5 não — e o segundo é o que está
  // pior. A taxa seria a medida certa, e ela depende de tasks por agente, que
  // é justamente a coluna que a rota não serve (L4, #67).
  //
  // **Então o destaque melhora quando a #67 fechar**, e o gatilho fica escrito:
  // com tasks por agente disponível, esta linha passa a ranquear por taxa.
  //
  // Empate destaca todos os empatados: escolher um seria arbitrário.
  const maiorFalha = Math.max(0, ...byAgent.map((a) => failedCountOf(a.agentId)));

  const linhas = byAgent
    .map((linha) => ({
      ...linha,
      total: sumKnown([linha.inputTokens, linha.outputTokens]),
    }))
    .sort((a, b) => {
      if (a.total === null && b.total === null) return 0;
      if (a.total === null) return 1;
      if (b.total === null) return -1;
      return b.total - a.total;
    });

  return (
    <Card withBorder padding={0} data-testid="card-consumo-por-agente">
      <Box px="md" py="sm" bg="var(--buteco-surface-subtle)">
        <Text size="sm" fw={600}>
          Consumo por agente
        </Text>
      </Box>
      <Stack gap="sm" p="md">
        {linhas.length === 0 && queryState === 'ok' ? (
          <Text size="sm" c="dimmed" data-testid="consumo-por-agente-vazio">
            Nenhum consumo por agente neste período.
          </Text>
        ) : (
          <Table data-testid="tabela-agentes">
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Agente</Table.Th>
                <Table.Th ta="right">Tokens</Table.Th>
                <Table.Th ta="right">Falhas</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {linhas.map((linha) => {
                const nome = agentNames?.get(linha.agentId);
                return (
                  <Table.Tr key={linha.agentId} data-testid={`agente-${linha.agentId}`}>
                    <Table.Td>
                      {catalogLoading ? (
                        <Loader size="xs" data-testid={`agente-${linha.agentId}-carregando`} />
                      ) : (
                        <Anchor
                          component={Link}
                          to={`/agents/${linha.agentId}`}
                          size="sm"
                          data-testid={`agente-${linha.agentId}-nome`}
                          // Fora do catálogo: o identificador abreviado no lugar
                          // do nome, e a linha permanece.
                          ff={nome === undefined ? 'monospace' : undefined}
                        >
                          {nome ?? linha.agentId.slice(0, 8)}
                        </Anchor>
                      )}
                    </Table.Td>
                    <Table.Td ta="right">
                      <MetricValue
                        value={linha.total}
                        queryState={queryState}
                        reason={reason}
                        format={formatTokens}
                        size="sm"
                        fw={400}
                        data-testid={`agente-${linha.agentId}-tokens`}
                      />
                    </Table.Td>
                    <Table.Td ta="right">
                      <MetricValue
                        value={failedCountOf(linha.agentId)}
                        queryState={queryState}
                        reason={reason}
                        format={formatCount}
                        // "Nenhuma", como o artboard escreve — não `0`.
                        zeroLabel="Nenhuma"
                        size="sm"
                        fw={400}
                        c={
                          maiorFalha > 0 && failedCountOf(linha.agentId) === maiorFalha
                            ? 'var(--mantine-color-red-filled)'
                            : undefined
                        }
                        data-testid={`agente-${linha.agentId}-falhas`}
                      />
                    </Table.Td>
                  </Table.Tr>
                );
              })}
            </Table.Tbody>
          </Table>
        )}

        {catalogFailed ? (
          <Text size="xs" c="dimmed" data-testid="catalogo-falhou">
            Não foi possível carregar os nomes dos agentes.{' '}
            <Anchor component="button" type="button" size="xs" onClick={onRetryCatalog}>
              Tentar de novo
            </Anchor>
          </Text>
        ) : null}

      </Stack>
    </Card>
  );
}
