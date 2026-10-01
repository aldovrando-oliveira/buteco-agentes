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
// TASKS, TOKENS POR TASK E DURAÇÃO P95 FICAM FORA POR DECISÃO (#67, fechada).
//
// O protótipo desenha as três colunas. Elas NÃO são lacuna: têm fonte, são
// servidas por `GET /insights/agents/{id}` e são apresentadas na aba de Insights
// do agente — a um clique daqui, pelo nome. A decisão foi deixá-las só lá.
//
// A RAZÃO É ARITMÉTICA, e é o que impede de reabrir isto por gosto: as três têm
// DENOMINADORES DIFERENTES. `Tokens` soma sobre as chamadas de provedor;
// `Tokens por task` (M17) divide pelas tasks QUE TÊM chamada; `Tasks` seriam
// todas as execuções; e `Duração p95` (M21) exclui execução com `SubmittedAt`
// nulo. Numa linha de seis colunas o operador multiplica e divide entre elas —
// e nenhuma dessas contas fecha. Medido em 26/09: para um mesmo agente, a aba
// diz 59.468,1 tokens por task e a divisão da linha daria 39.645,4.
//
// Nos cards da aba cada número vem com o `sampleCount` e o subtítulo que dizem
// de que população ele fala. Uma célula de tabela não tem onde carregar isso.
//
// NADA ENTRA NO LUGAR DELAS. O rodapé com a lacuna declarada foi removido na
// décima rodada de conferência da #52 — o quadro tracejado não existe no
// protótipo — e não volta: declarar na tela uma ausência ESCOLHIDA pede desculpa
// por uma decisão. A explicação vive na #67 fechada e no `02`.
//
// O que a rota SERVE por agente além de tokens é `errors.byAgent`, então a
// coluna Falhas entra — ela tem fonte.
//
// **E ESTA COLUNA NÃO SOMA O MESMO QUE O KPI "Falharam na execução" da mesma
// página — #92.** `errors.byAgent` agrupa `TerminalState in ('Failed','Rejected')`
// e o KPI conta `'Failed'` só; `rejectedCount` saiu da página na #75. No dado de
// dev a coluna soma 4 e o KPI diz 2, na mesma rolagem. **Não corrigir aqui tirando
// `Rejected` da coluna:** isso desfaz a #84, porque as falhas do agente que ela
// recupera são todas `Rejected`. A saída é de vocabulário da tela e está na #92.
//
// E DESDE A #84 ELA É FONTE DE LINHA, NÃO SÓ DE COLUNA: a população da tabela é a
// união das duas listas. O parágrafo acima dizia "A TABELA QUE CRUZA DOIS
// CONTRATOS" sobre o cruzamento com o CATÁLOGO; agora há um segundo cruzamento, e
// ele é entre as duas agregações da própria rota.

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
  // A SOMA, E NÃO A ÚLTIMA (#86).
  //
  // `errors.byAgent` é agrupada por `(AgentId, Provider, Model)` — TRÊS colunas —,
  // então um agente que trocou de provedor ou de modelo dentro da janela chega em
  // mais de uma linha. `new Map(lista.map(…))` guardava a ÚLTIMA e descartava as
  // outras em silêncio; e como a rota ordena `count desc`, a última de um mesmo
  // agente é a de MENOR contagem. Não era erro aleatório: era viés para baixo,
  // sempre, com número que continua plausível.
  const failures = failuresByAgent.reduce(
    (acc, f) => acc.set(f.agentId, (acc.get(f.agentId) ?? 0) + f.failedCount),
    new Map<string, number>(),
  );

  // A ÚNICA AUSÊNCIA QUE VIRA ZERO NESTA PÁGINA, E ELA É JUSTIFICADA — não um
  // `?? 0` de conveniência, que a D6 proíbe por nome.
  //
  // `errors.byAgent` é um `group by` sobre as execuções QUE FALHARAM: um agente
  // que aparece em `byAgent` (logo teve consumo medido na janela) e não aparece
  // em `errors.byAgent` teve as falhas dele CONTADAS, e a contagem deu zero. É
  // o mesmo raciocínio que a rota aplica ao dia medido e vazio.
  //
  // A diferença com as outras ausências da tela: aqui a população do
  // denominador é conhecida — o agente está em uma das duas agregações. Um agente
  // que não está em NENHUMA das duas listas simplesmente não tem linha, e é
  // isso que impede o zero de se espalhar para quem não foi medido.
  //
  // **A #84 TORNOU ESTE RACIOCÍNIO MAIS EXATO, não menos:** a frase acima já
  // falava em "nenhuma das duas listas", e antes dela a população era só UMA —
  // então a justificativa descrevia um desenho que o código não tinha. Agora a
  // população É a união das duas, e a condição que o texto sempre afirmou é a que
  // o código executa.
  //
  // O que mudou junto, e precisa ficar dito: um agente que entra pela lista de
  // FALHAS está, por definição, em `errors.byAgent` — então ele nunca cai neste
  // ramo. Quem cai aqui continua sendo só quem tem consumo medido e nenhuma
  // falha contada.
  const failedCountOf = (agentId: string): number => {
    const contado = failures.get(agentId);
    return contado === undefined ? 0 : contado;
  };

  // A POPULAÇÃO DA TABELA É A UNIÃO DAS DUAS LISTAS (#84).
  //
  // Antes as linhas nasciam só de `tokens.byAgent`, que é `provider_calls join
  // task_executions` — então AGENTE QUE FALHOU SEM CHAMAR O PROVEDOR não tinha
  // linha, e as falhas dele sumiam da tela junto com ela. Observado no banco de
  // dev em 30/09: um agente com 2 execuções, as 2 recusadas, nenhuma chamada de
  // provedor, e `(null)`/`(null)` em provedor e modelo — o defeito escondia
  // preferencialmente a falha de CONFIGURAÇÃO.
  //
  // A união resolve POR CONSTRUÇÃO, e não por sorte do dado: `errors.byAgent`
  // (M28) consulta `task_executions` SOZINHA, sem junção a `provider_calls`, então
  // toda execução terminal em `Failed` ou `Rejected` tem linha ali sempre.
  //
  // O QUE A UNIÃO DEIXA DE FORA, DE PROPÓSITO: agente que executou, não chamou
  // provedor e não falhou. A tabela não teria o que mostrar dele — Tokens vazio,
  // Falhas "Nenhuma", e um nome. A coluna que daria sentido a essa linha é
  // `Tasks`, e ela ficou fora por decisão (#67, fechada). GATILHO para reabrir: a
  // primeira coluna de população que entre nesta tabela — e é aí que o campo novo
  // em `GET /insights/system` se justifica, não antes.
  //
  // Agente que entra só pela lista de falhas vem com as parcelas de token NULAS,
  // para que a célula caia no estado VAZIO — ele não teve chamada de provedor, e
  // `0` ali afirmaria que a chamada houve e reportou zero.
  const semConsumoMedido: AgentTokens[] = failuresByAgent
    .map((f) => f.agentId)
    .filter((agentId, i, todos) => todos.indexOf(agentId) === i)
    .filter((agentId) => !byAgent.some((a) => a.agentId === agentId))
    .map((agentId) => ({ agentId, inputTokens: null, outputTokens: null }));

  const populacao = [...byAgent, ...semConsumoMedido];

  // O DESTAQUE DA COLUNA FALHAS — a linha de MAIOR contagem, em vermelho.
  //
  // O `Main.dc.html` pinta um dos números de `red[4]` e deixa os outros em
  // branco, SEM regra declarada. Decidido com o dono em 25/09: destacar o maior
  // valor.
  //
  // **A ressalva está escrita porque ela é real, não para cobrir a escolha:**
  // isto ordena por contagem ABSOLUTA, não por taxa. Um agente com 9 falhas em
  // 1.000 tasks fica marcado e um com 3 em 5 não, e o segundo é o que está pior.
  // A taxa seria a medida certa, e depende de tasks por agente, que a rota do
  // sistema não serve.
  //
  // **É HIPÓTESE, e não medição:** no dado de 26/09 esse cenário não ocorre —
  // os dois agentes com falha estão ambos em 100%, e o que tem linha é o
  // destacado. Fica escrito como raciocínio, que é o que é.
  //
  // **E o critério fica assim MESMO SABENDO DISSO** (#67, fechada): um destaque
  // por taxa com a taxa FORA da tela é pior que este. Hoje o vermelho é
  // verificável pelo olho — é o maior número da coluna. Ranqueando por taxa, o
  // operador veria o 7 marcado e o 2 não, sem nenhum número na tela que
  // explicasse a escolha, e critério invisível é pior que critério grosseiro.
  //
  // **GATILHO para mudar:** o primeiro pedido do dono por TAXA de falha no
  // ranking.
  //
  // *(Este bloco dizia que "o trabalho começa pela POPULAÇÃO da tabela, não por
  // uma coluna: as linhas nascem de `tokens.byAgent`, então agente que executou e
  // não chamou provedor não tem linha nenhuma aqui (#84)". **Era verdade quando
  // foi escrito, e a #84 fez exatamente o que ele mandava fazer primeiro:** a
  // população veio antes da coluna. A frase fica corrigida com a causa, e não
  // apagada — a ORDEM que ela defende continua valendo para quem reabrir o
  // ranking por taxa.)*
  //
  // **E o destaque é calculado sobre a população INTEIRA.** Calculá-lo sobre
  // `byAgent` depois da #84 seria defeito novo: a linha que entrou por falha
  // poderia ter a maior contagem da coluna e não sair em vermelho. No dado de dev
  // isso EMPATA, então o banco não reprovaria — o guarda é de fixture.
  //
  // Empate destaca todos os empatados: escolher um seria arbitrário.
  const maiorFalha = Math.max(0, ...populacao.map((a) => failedCountOf(a.agentId)));

  const linhas = populacao
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
                          // A ABA, E NÃO O DETALHE — e o parâmetro é o que faz
                          // a diferença entre um clique e dois. `parseTab(null)`
                          // cai em "Visão geral" por contrato declarado, então
                          // sem `?tab=insights` o operador que veio do ranking
                          // ainda precisa escolher a aba (#67, caminho 2).
                          to={`/agents/${linha.agentId}?tab=insights`}
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
