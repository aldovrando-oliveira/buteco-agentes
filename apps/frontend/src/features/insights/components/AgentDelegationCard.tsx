import { Box, Card, Group, Stack, Text, Tooltip } from '@mantine/core';
import { Info } from 'lucide-react';
import type { AgentDelegationInsights } from '../types/agentInsights';
import { MetricValue } from './MetricValue';
import { formatCount, type QueryState } from '../utils/metricState';
import { caveatsFor } from '../utils/caveatLabels';
import { delegationOutcomeLabel } from '../utils/delegationOutcomeLabels';
import { delegationSides, type DelegationCatalogAgent } from '../utils/delegationRows';

// "Delegação" — A DECISÃO CENTRAL DESTA ABA.
//
// ===========================================================================
// OS DOIS LADOS NÃO SÃO ESPELHO, E A TELA NÃO PODE FAZÊ-LOS PARECER
// ===========================================================================
//
// `delegatesTo` é o que o agente TENTOU; `triggeredBy` é o que de fato RODOU
// nele. Fontes diferentes, perguntas diferentes, e os dois lados da mesma
// relação entre os mesmos dois agentes **podem mostrar números diferentes**.
// Duas causas independentes — resultado que não produz execução, e os dois
// relógios —, e a segunda **não some com período maior**.
//
// ---------------------------------------------------------------------------
// O CAVEAT É ÍCONE COM TOOLTIP, E NÃO PARÁGRAFO — DECISÃO DO DONO (26/09/2026)
// ---------------------------------------------------------------------------
//
// O texto de `delegation-sides-are-not-mirrors` estava como parágrafo ao pé do
// card e saiu de lá na conferência manual: *"os gráficos em si são
// autoexplicativos"*, e a prosa atrapalhava a leitura dos dois lados.
//
// **Ele não podia simplesmente sumir**, e é a diferença entre este e as três
// notas que saíram junto: aquelas eram texto de apoio escrito pela tela; este é
// um código de parcialidade que a ROTA manda, e a spec proíbe descartar em
// silêncio um código cujo número está na superfície. Os dois números que ele
// limita estão logo acima dele.
//
// A forma escolhida cumpre os dois lados: o ícone é **visível** e fica no
// cabeçalho do card, junto dos dois conjuntos que o código qualifica; o texto
// continua **inteiro**, alcançável sem sair da tela. O `title` acompanha o
// `label` porque é ele que sobrevive à leitura por leitor de tela — mesmo
// idioma que `MetricValue` já usa para a razão ao lado do travessão.
//
// O que este card NÃO faz, e cada item é uma forma de transformar resultado
// correto em defeito aparente:
//
//   - não deriva um lado do outro;
//   - não exibe total que some os dois;
//   - não sinaliza a diferença como alerta, aviso ou inconsistência;
//   - não ordena um lado pelo outro.
//
// **Medido em produção, os números hoje BATEM** — a janela não tem nenhum
// `NotStarted` nem nada na fronteira. Bater é possível, não é garantido, e este
// card não pode ser escrito supondo que batem. O guarda que protege isto
// exercita a divergência e **a afirma**: um guarda que afirmasse igualdade
// reprovaria o comportamento correto.
//
// ===========================================================================
// O RESULTADO DISCRIMINADO É ACRÉSCIMO AO ARTBOARD, E A RAZÃO ESTÁ AQUI (D2)
// ===========================================================================
//
// O artboard desenha uma barra por destino, com um número só. A rota devolve
// uma linha por par destino × resultado, e é o resultado que explica a
// divergência: sem ver que houve `NotStarted`, o operador lê dois números
// diferentes e conclui defeito, e o `caveat` vira prosa que ele não liga a
// número nenhum.
//
// A barra e a contagem do artboard ficam; o que entra é a linha de detalhe sob
// elas. **Recusada** a alternativa de uma linha por par destino × resultado:
// multiplicaria as linhas por quatro no pior caso e desmancharia a leitura de
// ranking que o artboard procura.
//
// ===========================================================================
// AS DUAS SEÇÕES EXISTEM SEMPRE — OS QUATRO CENÁRIOS
// ===========================================================================
//
// A nota `cenarios` do `canvas.json`: "uma estrutura, três preenchimentos", e o
// quarto — agente que não delega nem é delegado — é "o mesmo card com as duas
// seções tracejadas". Seção que some faz o operador procurar defeito na
// navegação em vez de ler o fato.
//
// A NOTA DE RODAPÉ QUE EXPLICAVA ISSO EM PROSA SAIU, por decisão do dono na
// conferência manual de 26/09/2026: *"os gráficos em si são autoexplicativos"*.
// Ela dizia que "sem delegação cadastrada" não é o mesmo que um vínculo ocioso.
// **A distinção continua na tela, e é o desenho que a faz**: o vínculo ocioso é
// uma LINHA com `0`; a ausência de cadastro é o quadro TRACEJADO, sem número. O
// texto nomeava o que as duas formas já separam.
//
// E O TEXTO DO TRACEJADO DE "ACIONADO POR" É CONDICIONAL (D14). O artboard
// escreve "Nenhum agente aciona este. Ele recebe pedidos externos." A segunda
// frase só é verdadeira quando houve task de origem externa — no quarto
// cenário ela afirmaria uma origem que não houve.

export interface AgentDelegationCardProps {
  delegation: AgentDelegationInsights;
  agentId: string;
  /** O cadastro de SAÍDA, como o detalhe do agente já o traz. */
  registeredTargets: { id: string; name: string }[];
  /** O catálogo inteiro, ou `undefined` enquanto ele não respondeu. */
  catalog: DelegationCatalogAgent[] | undefined;
  /** Decide a segunda frase do tracejado de "Acionado por". */
  externalOriginTaskCount: number;
  queryState: QueryState;
  reason?: string;
}

const LARGURA_DO_NOME = 186;

function Linha({
  testId,
  nome,
  identificador,
  valor,
  max,
  queryState,
  reason,
  children,
}: {
  testId: string;
  nome: string | null;
  identificador: string;
  valor: number;
  max: number;
  queryState: QueryState;
  reason?: string;
  children?: React.ReactNode;
}) {
  // A barra só existe com a consulta respondida E com um máximo conhecido:
  // desenhar área a partir de nada afirmaria proporção.
  const largura = queryState === 'ok' && max > 0 ? Math.round((valor / max) * 100) : 0;

  return (
    <Stack gap={4} data-testid={testId} data-delegation-row="true">
      <Group gap="sm" wrap="nowrap" align="center">
        <Text
          size="xs"
          w={LARGURA_DO_NOME}
          style={{ flexShrink: 0 }}
          c={nome === null ? 'dimmed' : undefined}
          ff={nome === null ? 'monospace' : undefined}
          data-testid={`${testId}-nome`}
        >
          {/* Sem catálogo, o identificador — a linha NÃO some, porque a
              medição aconteceu e o nome é só a etiqueta dela. */}
          {nome ?? identificador}
        </Text>
        <Box
          style={{
            flexGrow: 1,
            height: 16,
            borderRadius: 'var(--mantine-radius-xs)',
            background: 'var(--buteco-surface-subtle)',
            overflow: 'hidden',
          }}
        >
          {largura > 0 ? (
            // A marca é o que o guarda lê: sem ela, um teste de ausência de
            // barra teria de procurar `width` no estilo, e acertaria as
            // células de nome, que também têm largura.
            <Box
              data-delegation-bar="true"
              style={{
                height: 16,
                width: `${largura}%`,
                background: 'var(--mantine-primary-color-filled)',
              }}
            />
          ) : null}
        </Box>
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
      {children}
    </Stack>
  );
}

function SemVinculo({ testId, children }: { testId: string; children: React.ReactNode }) {
  return (
    <Box
      data-testid={testId}
      data-no-binding="true"
      style={{
        border: '1px dashed var(--mantine-color-default-border)',
        borderRadius: 'var(--mantine-radius-sm)',
        padding: '12px 14px',
      }}
    >
      <Text size="xs" c="dimmed">
        {children}
      </Text>
    </Box>
  );
}

export function AgentDelegationCard({
  delegation,
  agentId,
  registeredTargets,
  catalog,
  externalOriginTaskCount,
  queryState,
  reason,
}: AgentDelegationCardProps) {
  const sides = delegationSides(delegation, agentId, registeredTargets, catalog);
  const caveats = caveatsFor(delegation.caveats, 'delegation-sides', 'agent');

  return (
    <Card withBorder padding={0} data-testid="card-delegacao-do-agente" h="100%">
      <Box px="md" py="sm" bg="var(--buteco-surface-subtle)">
        <Group gap={6} align="center" wrap="nowrap">
          <Text size="sm" fw={600}>
            Delegação
          </Text>
          {caveats.map((c) => (
            <Tooltip key={c.code} label={c.text} multiline w={320} withArrow>
              <Box
                component="span"
                data-testid="delegacao-caveat"
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
      </Box>
      <Stack gap="md" p="md">
        <Stack gap="xs" data-testid="secao-delega-para">
          <Text size="xs" c="dimmed">
            Delega para
          </Text>
          {sides.delegatesTo.length === 0 ? (
            <SemVinculo testId="delega-para-sem-vinculo">
              Sem delegação cadastrada.
            </SemVinculo>
          ) : (
            sides.delegatesTo.map((linha) => (
              <Linha
                key={linha.agentId}
                testId={`delega-para-${linha.agentId}`}
                nome={linha.name}
                identificador={linha.agentId}
                valor={linha.total}
                max={sides.delegatesToMax}
                queryState={queryState}
                reason={reason}
              >
                {/* O RESULTADO DISCRIMINADO. Sem ele, a divergência com o
                    outro lado fica sem causa visível na tela. */}
                {queryState === 'ok' && linha.outcomes.length > 0 ? (
                  <Text
                    size="xs"
                    c="dimmed"
                    pl={LARGURA_DO_NOME}
                    data-testid={`delega-para-${linha.agentId}-resultados`}
                  >
                    {linha.outcomes
                      .map((o) => `${delegationOutcomeLabel(o.outcome).text} ${formatCount(o.count)}`)
                      .join(' · ')}
                  </Text>
                ) : null}
                {queryState === 'ok' && !linha.registered ? (
                  <Text
                    size="xs"
                    c="dimmed"
                    pl={LARGURA_DO_NOME}
                    data-testid={`delega-para-${linha.agentId}-sem-cadastro`}
                  >
                    Vínculo não está mais cadastrado. A medição aconteceu.
                  </Text>
                ) : null}
              </Linha>
            ))
          )}
        </Stack>

        <Stack
          gap="xs"
          pt="md"
          style={{ borderTop: '1px solid var(--mantine-color-default-border)' }}
          data-testid="secao-acionado-por"
        >
          <Text size="xs" c="dimmed">
            Acionado por
          </Text>
          {sides.triggeredBy.length === 0 ? (
            <SemVinculo testId="acionado-por-sem-vinculo">
              {/* A segunda frase é condicional: no quarto cenário ela
                  afirmaria uma origem externa que não houve. */}
              Nenhum agente aciona este.
              {externalOriginTaskCount > 0 ? ' Ele recebe pedidos externos.' : ''}
            </SemVinculo>
          ) : (
            sides.triggeredBy.map((linha) => (
              <Linha
                key={linha.agentId}
                testId={`acionado-por-${linha.agentId}`}
                nome={linha.name}
                identificador={linha.agentId}
                valor={linha.executedCount}
                max={sides.triggeredByMax}
                queryState={queryState}
                reason={reason}
              >
                {queryState === 'ok' && !linha.registered ? (
                  <Text
                    size="xs"
                    c="dimmed"
                    pl={LARGURA_DO_NOME}
                    data-testid={`acionado-por-${linha.agentId}-sem-cadastro`}
                  >
                    Vínculo não está mais cadastrado. A medição aconteceu.
                  </Text>
                ) : null}
              </Linha>
            ))
          )}
        </Stack>
      </Stack>
    </Card>
  );
}
