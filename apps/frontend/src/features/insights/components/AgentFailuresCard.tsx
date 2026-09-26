import { Box, Card, Group, Stack, Text } from '@mantine/core';
import type { AgentErrorInsights } from '../types/agentInsights';
import { MetricValue } from './MetricValue';
import { formatCount, type QueryState } from '../utils/metricState';
import { executionPhaseLabel } from '../utils/failurePhaseLabels';

// "Por que as falhas aconteceram" — E O TÍTULO TEM DOIS ESTADOS NO PROTÓTIPO.
//
// ===========================================================================
// O TÍTULO NÃO É DIVERGÊNCIA: O ARTBOARD O ESCREVE DE DUAS FORMAS
// ===========================================================================
//
// `Agente-Insights.dc.html`, agente com 5 falhas → **"Por que as 5 falhas
// aconteceram"**.
// `Agente-Delegado.dc.html`, agente sem falha → **"Falhas"**, com o quadro
// tracejado no corpo.
//
// São dois estados do mesmo card, e o card os segue. **A contagem saiu do
// título e foi para a direita do cabeçalho**, por decisão do dono na
// conferência de 26/09 — mesma posição do total em "Onde o tempo foi", e é o
// que faz os dois cabeçalhos da aba se lerem igual.
//
// ===========================================================================
// A RECUSA DE ENTRADA SAIU DAQUI, E EU É QUE TINHA FURADO A MINHA PRÓPRIA RÉGUA
// ===========================================================================
//
// A D6 pôs neste card um grupo com `rejectedAtEntryCount` e
// `rejectionsByReason` — dados que a rota serve desde a #51. **O artboard não
// tem elemento nenhum para eles**, e a D10 da mesma change diz, por escrito,
// que métrica servida e não desenhada **não ganha elemento novo**:
//
//   > "Acrescentar elemento que o artboard não tem é divergência tanto quanto
//   > removê-lo. Criar um quadro novo acrescenta à tela um elemento cuja única
//   > função é falar do que ela não mostra, e o peso dele compete com os
//   > números que ela mostra."
//
// A D6 e a D10 se contradiziam, e quem viu foi o dono, na tela: o rodapé com o
// grupo, o regime e o texto do `caveat` **pesava mais que a tabela de falhas**
// — que é o card inteiro. Exatamente o custo que a D10 nomeia.
//
// **A recusa de entrada passa para a lista de "servido e não desenhado"**, com
// gatilho, junto do embedding, da profundidade e das tasks sem estado terminal.
// O que sobrevive ao archive é a issue, não o parágrafo.
//
// **A contagem de recusa NÃO sumiu da aba:** `rejectedCount` continua no
// subtítulo do KPI `Taxa de falha`, que é onde o artboard a desenha
// ("5 falhas · nenhuma recusa"), e é lá que o `caveat`
// `rejections-missing-from-executions` passou a morar — ele limita **aquele**
// número, e agora é o único lugar da aba onde ele aparece.
//
// ===========================================================================
// DOIS GRUPOS, E NÃO A TABELA ÚNICA DO ARTBOARD (D9, que continua valendo)
// ===========================================================================
//
// O artboard desenha uma tabela com descrição, uma segunda coluna que mistura
// nome de modelo com nome de servidor MCP, e a contagem. A rota serve duas
// agregações INDEPENDENTES sobre a mesma população — `byPhase` e
// `byProviderAndModel` — e **nenhum campo junta fase a provedor/modelo**, nem
// traz servidor MCP. Cruzá-las no cliente inventaria a junção.

export interface AgentFailuresCardProps {
  errors: AgentErrorInsights;
  executedTaskCount: number;
  queryState: QueryState;
  reason?: string;
}

function Grupo({
  titulo,
  testId,
  children,
}: {
  titulo: string;
  testId: string;
  children: React.ReactNode;
}) {
  return (
    <Stack gap={6} data-testid={testId}>
      <Text size="xs" c="dimmed">
        {titulo}
      </Text>
      {children}
    </Stack>
  );
}

function LinhaContagem({
  testId,
  rotulo,
  detalhe,
  valor,
  queryState,
  reason,
  cru,
}: {
  testId: string;
  rotulo: string;
  detalhe?: string;
  valor: number;
  queryState: QueryState;
  reason?: string;
  /** `true` quando o vocabulário não foi reconhecido e o valor sai cru. */
  cru?: boolean;
}) {
  return (
    <Group
      gap="sm"
      wrap="nowrap"
      align="baseline"
      data-testid={testId}
      data-raw-value={cru ? 'true' : undefined}
    >
      <Text size="xs" style={{ flexGrow: 1 }} ff={cru ? 'monospace' : undefined}>
        {rotulo}
      </Text>
      {detalhe === undefined ? null : (
        <Text size="xs" c="dimmed" w={180} ta="right" style={{ flexShrink: 0 }}>
          {detalhe}
        </Text>
      )}
      <MetricValue
        value={valor}
        queryState={queryState}
        reason={reason}
        format={formatCount}
        size="xs"
        fw={400}
        data-testid={`${testId}-valor`}
      />
    </Group>
  );
}

export function AgentFailuresCard({
  errors,
  executedTaskCount,
  queryState,
  reason,
}: AgentFailuresCardProps) {
  const semFalha =
    queryState === 'ok' && errors.byPhase.length === 0 && errors.byProviderAndModel.length === 0;

  // Os dois títulos do artboard. Enquanto a consulta não respondeu, o neutro:
  // afirmar "nenhuma falha" com a API fora do ar é o estado 2 no lugar do 3.
  const titulo =
    queryState === 'ok' && errors.failedCount > 0
      ? 'Por que as falhas aconteceram'
      : 'Falhas';

  return (
    <Card withBorder padding={0} data-testid="card-falhas-do-agente">
      <Box px="md" py="sm" bg="var(--buteco-surface-subtle)">
        <Group justify="space-between" align="center" wrap="nowrap">
          <Text size="sm" fw={600}>
            {titulo}
          </Text>
          <Group gap={4} align="baseline" wrap="nowrap" data-testid="falhas-total">
            <Text size="xs" c="dimmed">
              total de falhas
            </Text>
            <MetricValue
              value={errors.failedCount}
              queryState={queryState}
              reason={reason}
              format={formatCount}
              size="xs"
              fw={400}
              data-testid="falhas-total-valor"
            />
          </Group>
        </Group>
      </Box>

      <Stack gap="md" p="md">
        {semFalha ? (
          <Box
            data-testid="falhas-do-agente-sem-falha"
            data-no-binding="true"
            style={{
              border: '1px dashed var(--mantine-color-default-border)',
              borderRadius: 'var(--mantine-radius-sm)',
              padding: '12px 14px',
            }}
          >
            <Text size="xs" c="dimmed">
              Nenhuma falha no período. As {formatCount(executedTaskCount)} tasks chegaram a
              concluído.
            </Text>
          </Box>
        ) : (
          <>
            {errors.byPhase.length > 0 ? (
              <Grupo titulo="Por fase da execução" testId="grupo-por-fase">
                {errors.byPhase.map((linha) => {
                  const label = executionPhaseLabel(linha.phase);
                  return (
                    <LinhaContagem
                      key={linha.phase}
                      testId={`fase-${linha.phase}`}
                      rotulo={label.text}
                      valor={linha.count}
                      queryState={queryState}
                      reason={reason}
                      cru={label.unknown}
                    />
                  );
                })}
              </Grupo>
            ) : null}

            {errors.byProviderAndModel.length > 0 ? (
              <Grupo titulo="Por provedor e modelo" testId="grupo-por-provedor-e-modelo">
                {errors.byProviderAndModel.map((linha, i) => (
                  <LinhaContagem
                    key={`${linha.provider ?? '?'}/${linha.model ?? '?'}/${i}`}
                    testId={`provedor-modelo-${i}`}
                    // Provedor e modelo são anuláveis na fonte: a execução pode
                    // ter falhado ANTES de resolver qualquer um dos dois, e é
                    // isso que a célula vazia diz.
                    rotulo={linha.model ?? 'modelo não registrado'}
                    detalhe={linha.provider ?? 'provedor não registrado'}
                    valor={linha.failedCount}
                    queryState={queryState}
                    reason={reason}
                  />
                ))}
              </Grupo>
            ) : null}
          </>
        )}
      </Stack>
    </Card>
  );
}
