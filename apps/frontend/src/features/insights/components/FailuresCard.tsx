import { Box, Card, Group, Stack, Text } from '@mantine/core';
import type { ErrorInsights } from '../types/systemInsights';
import { MetricValue } from './MetricValue';
import {
  METRIC_SIZE,
  formatCount,
  formatRatio,
  ratio,
  readMetric,
  type QueryState,
} from '../utils/metricState';
import { caveatsFor } from '../utils/caveatLabels';

// "Falhas" — DOIS NÚMEROS SEPARADOS, E SÓ UM DELES TEM PERCENTUAL (D10).
//
// A separação é do protótipo, e o texto dele é quase literal — ver a divergência
// da frase mais abaixo.
//
// ===========================================================================
// SÃO DUAS RECUSAS, E ESTE CARD APRESENTA A DE ENTRADA (#75, D3)
// ===========================================================================
//
// `rejectedAtEntryCount` — recusa feita por `apps/api` ANTES de qualquer
// execução, com regime de medição próprio — é a que casa com o rótulo
// "Recusadas na entrada" que o artboard escreve aqui. O `Main.dc.html` põe `5`
// neste quadro e `5` na linha "Agente sem provider ou modelo configurado" do card
// de Motivos: é o mesmo fato, desenhado duas vezes.
//
// **Até a #75 este quadro lia `rejectedCount`, que é OUTRA população** — a recusa
// COM linha de execução, hoje só a de profundidade de delegação, feita por
// `apps/workers` (`AgentExecutionService.cs:236` é o único sítio que grava
// `TerminalState = 'Rejected'`). Enquanto era o único número de recusa da tela, o
// rótulo era impreciso; com os dois medidos e servidos, ele nomeava um como se
// fosse o outro.
//
// **E `rejectedCount` SAI DA PÁGINA — decisão registrada, não esquecimento.** O
// `Main.dc.html` tem seis KPIs e NENHUM de taxa de falha, então não existe nesta
// tela o elemento que na aba do agente hospeda esse número (lá ele vive no
// subtítulo do KPI `Taxa de falha`, "5 falhas · nenhuma recusa", que o artboard do
// agente desenha). Criar um terceiro quadro seria a D10 outra vez — elemento que o
// artboard não tem — e faria a frase abaixo explicar dois números entre três.
// Ela vai para *servido e não desenhado*, com issue e gatilho.
//
// ===========================================================================
// O CAVEAT NÃO MUDA DE LUGAR, E É POR NÃO PERDER O NÚMERO
// ===========================================================================
//
// `rejections-missing-from-executions` diz "Recusas não geram linha de execução,
// então não entram no percentual de falha". Ele limita o PERCENTUAL DE FALHA, que
// continua na tela — e continua verdadeiro sobre a recusa de entrada, que também
// não gera linha de execução. A posição `rejection-count` fica onde estava.
//
// Na aba do agente o mesmo código andou por três posições em três rodadas; aqui
// não anda, e a razão é essa: o número que ele qualifica não saiu.
//
// O PERCENTUAL SÓ VALE ONDE NUMERADOR E DENOMINADOR CONTAM A MESMA POPULAÇÃO.
//
// `failedCount` sai de `task_executions`, e `executedTaskCount` também — mesma
// população, percentual legítimo. `rejectedCount` NÃO produz linha de execução
// (é o que `rejections-missing-from-executions` declara), então ele SUBCONTA o
// denominador: "1,1% das tasks" seria um percentual sobre um total que não
// inclui as próprias recusas. O protótipo escreve esse percentual; ele sai, e o
// texto do caveat entra no lugar.
//
// E `ratio` guarda a divisão por zero: sem task executada no período, não há
// percentual nenhum — nem `0%`, nem `NaN`.

export interface FailuresCardProps {
  errors: ErrorInsights;
  executedTaskCount: number;
  queryState: QueryState;
  reason?: string;
  /**
   * Início do regime da RECUSA, só quando ele difere do que governa a página.
   *
   * Prop e não derivação interna: o card não conhece o mapa `regimes` nem qual
   * regime governa a página, e é a página que sabe as duas coisas — o mesmo
   * contrato de `regimeNote` no card de Motivos.
   */
  rejectionRegimeNote?: string;
}

export function FailuresCard({
  errors,
  executedTaskCount,
  queryState,
  reason,
  rejectionRegimeNote,
}: FailuresCardProps) {
  const percentualFalha = ratio(errors.failedCount, executedTaskCount);
  const leituraPercentual = readMetric(percentualFalha, queryState, formatRatio);
  const caveatsRecusa = caveatsFor(errors.caveats, 'rejection-count');

  return (
    <Card withBorder padding={0} data-testid="card-falhas">
      <Box px="md" py="sm" bg="var(--buteco-surface-subtle)">
        <Text size="sm" fw={600}>
          Falhas
        </Text>
      </Box>
      <Stack gap="md" p="md">
        {/* OS DOIS QUADROS, DE MESMO TAMANHO — medidos no `Main.dc.html`:
            `flex-grow: 1`, fundo da superfície sutil, raio `sm`, 12px de
            padding. `align="stretch"` no Group é o que os mantém da mesma
            ALTURA mesmo com conteúdos de tamanhos diferentes.

            A primeira versão não tinha quadro nenhum: eram dois blocos de texto
            soltos, com largura dirigida pelo conteúdo — e por isso o da recusa,
            que carrega uma frase, ficava o dobro do outro. O destaque das duas
            métricas, que é o que o card existe para dar, se perdia.

            AS CORES SÃO DO ARTBOARD e distinguem os dois problemas: falha em
            `red[4]`, recusa em `yellow[4]`. Não são decoração — são a mesma
            separação que o texto abaixo explica, dita em cor. */}
        <Group align="stretch" gap="sm" grow>
          <Box
            data-testid="bloco-falhas"
            style={{
              background: 'var(--buteco-surface-subtle)',
              borderRadius: 'var(--mantine-radius-sm)',
              padding: 12,
            }}
          >
            <Stack gap={2}>
              <Text size="xs" c="dimmed">
                Falharam na execução
              </Text>
              <MetricValue
                value={errors.failedCount}
                queryState={queryState}
                reason={reason}
                format={formatCount}
                size={METRIC_SIZE.card}
                fw={400}
                c="var(--mantine-color-red-filled)"
                data-testid="falhas-contagem"
              />
              {/* Percentual só aqui: mesma população no numerador e no denominador. */}
              {leituraPercentual.state === 'value' || leituraPercentual.state === 'zero' ? (
                <Text size="xs" c="dimmed" data-testid="falhas-percentual">
                  {leituraPercentual.state === 'zero' ? '0%' : leituraPercentual.text} das tasks
                </Text>
              ) : null}
            </Stack>
          </Box>

          <Box
            data-testid="bloco-recusas"
            style={{
              background: 'var(--buteco-surface-subtle)',
              borderRadius: 'var(--mantine-radius-sm)',
              padding: 12,
            }}
          >
            <Stack gap={2}>
              <Text size="xs" c="dimmed">
                Recusadas na entrada
              </Text>
              <MetricValue
                value={errors.rejectedAtEntryCount}
                queryState={queryState}
                reason={reason}
                format={formatCount}
                size={METRIC_SIZE.card}
                fw={400}
                c="var(--mantine-color-yellow-filled)"
                data-testid="recusas-contagem"
              />
              {/* NO LUGAR DO PERCENTUAL: o caveat que diz por que ele não existe.
                  O artboard escreve "1,1% das tasks" aqui, e a D10 recusa — a
                  recusa não produz linha de execução, então subconta o
                  denominador e o percentual seria sobre um total que não inclui
                  as próprias recusas. */}
              {caveatsRecusa.map((c) => (
                <Text key={c.code} size="xs" c="dimmed" data-testid="recusas-caveat">
                  {c.text}
                </Text>
              ))}
              {/* O REGIME DELA, e ele fica DENTRO do quadro — não no cabeçalho do
                  card, que qualificaria os dois quadros, e um deles é de outro
                  regime. É a régua já escrita em `caveatLabels.ts` — texto de
                  limitação junto do número limitado — aplicada a regime.

                  A recusa de entrada é o TERCEIRO regime da resposta, e o único
                  número desta página que o declara. */}
              {rejectionRegimeNote === undefined ? null : (
                <Text size="xs" c="dimmed" data-testid="nota-de-regime-rejection">
                  {rejectionRegimeNote}
                </Text>
              )}
            </Stack>
          </Box>
        </Group>

        {/* DIVERGÊNCIA DO LITERAL DO PROTÓTIPO, NA PRIMEIRA ORAÇÃO SÓ.

            O artboard escreve "Recusa é agente inativo ou sem provider e modelo
            configurados", e são DUAS das QUATRO causas do vocabulário —
            `AgentInactive` e `ProviderOrModelMissing`. Faltam
            `ProviderNotConfigured` (provedor sem chave no ambiente) e
            `AgentNotFound` (nenhum agente naquele id).

            Era verdade quando foi escrito: o protótipo é de 19/09 e a #51, que
            coletou o motivo e achou o quarto valor, mergeou em 26/09. É a
            convenção 13 na forma "verdadeira quando escrita, e outra etapa tornou
            falsa" — e com os quatro motivos na lista ao lado a frase passaria a
            enumerar um subconjunto do que a tela mostra, na frente do leitor.

            A ORAÇÃO NÃO ENUMERA AS QUATRO, ELA PARA DE ENUMERAR. Listar as quatro
            aqui duplicaria o card de Motivos, que é onde elas estão com contagem;
            a frase existe para dizer o que SEPARA as duas populações, e é isso que
            ela passa a dizer. As outras duas orações são literais do protótipo.

            Gatilho de volta: o `Main.dc.html` ser atualizado com a frase corrigida
            — aí o literal volta a valer e esta divergência cai. */}
        <Text size="xs" c="dimmed" data-testid="nota-falha-versus-recusa">
          Recusa é task barrada na entrada: nunca chega a processar, e os motivos dela estão ao
          lado. Falha é execução que começou e quebrou. Somar os dois esconde qual dos dois
          problemas existe.
        </Text>
      </Stack>
    </Card>
  );
}
