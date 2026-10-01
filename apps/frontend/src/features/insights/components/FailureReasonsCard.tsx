import { Box, Card, Group, Stack, Text } from '@mantine/core';
import type { ErrorInsights } from '../types/systemInsights';
import { MetricValue } from './MetricValue';
import { METRIC_SIZE, formatCount, type QueryState } from '../utils/metricState';
import { executionPhaseLabel, indexingFailureLabel } from '../utils/failurePhaseLabels';
import { rejectionReasonLabel } from '../utils/rejectionReasonLabels';

// "Motivos" — DUAS POPULAÇÕES, DOIS GRUPOS, UM CARD.
//
// ===========================================================================
// A L1 FECHOU, E ESTE CABEÇALHO FOI REESCRITO INTEIRO
// ===========================================================================
//
// Ele dizia, até a #75: *"L1 — o motivo das recusas não tem fonte (#51). O
// protótipo desenha a linha 'Agente sem provider ou modelo configurado — 5'. A
// rota serve `rejectedCount` SEM motivo, e declara isso em
// `rejection-reason-not-collected`. A contagem entra; a CAUSA não é nomeada."*
//
// **A #51 fechou a L1.** `apps/api` passa a gravar o motivo de toda recusa feita
// antes de qualquer execução, e a rota serve `rejectionsByReason` — vocabulário
// fechado de quatro valores, ordenado por contagem — mais
// `rejectedAtEntryCount`. O `caveat` `rejection-reason-not-collected` saiu dos dois
// handlers, porque código de parcialidade que sobrevive à lacuna afirma limitação
// que não há.
//
// O raciocínio que o cabeçalho antigo registrava **não** foi apagado: ele previa
// que *"nomear a causa seria a pior das três opções disponíveis"* e que *"omitir a
// linha seria a segunda pior, porque a soma dos motivos deixaria de fechar com a
// contagem de recusas, sem sintoma"*. As duas continuam valendo, e agora com fonte:
// a causa é nomeada só onde a resposta a declarou, e nenhum motivo recebido é
// omitido — ver os guardas negativos do `.test.tsx`.
//
// ===========================================================================
// POR QUE OS MOTIVOS ENTRAM AQUI, E NÃO NA ABA DO AGENTE (D1)
// ===========================================================================
//
// A `insights-aba-do-agente` tirou a recusa de entrada e os motivos do
// `AgentFailuresCard`, pelo critério escrito na D10 daquela change: *"métrica
// servida e não desenhada não ganha elemento novo"*. O artboard do agente não tem
// elemento para eles.
//
// **Nesta tela o artboard TEM o elemento, e sempre teve.** O `Main.dc.html` desenha
// neste card a linha *"Agente sem provider ou modelo configurado — 5"*, com o mesmo
// `5` que o card de Falhas mostra em *"Recusadas na entrada"*. Não se cria nada
// aqui: se preenche o que o artboard tem e estava vazio por falta de fonte.
//
// **As duas decisões opostas saem da MESMA régua**, e é isso que a torna régua: a
// condição avaliada em duas telas dá respostas diferentes porque os artboards são
// diferentes. Se fosse gosto, teria dado a mesma resposta nas duas.
//
// ===========================================================================
// MAS NÃO COMO SEXTA LINHA DA LISTA RASA — DIVERGÊNCIA REGISTRADA (D2)
// ===========================================================================
//
// O card do `Main.dc.html` é **uma lista ordenada única**, sem cabeçalho de grupo e
// sem separador:
//
//     Tempo de resposta do provedor esgotado        6
//     Agente sem provider ou modelo configurado     5   ← recusa de entrada
//     Servidor MCP indisponível                     3
//     Provedor recusou por limite de uso            3
//     Falha de indexação de conhecimento            2
//
// **Ela mistura as duas populações, e o artboard não sabia que eram duas.** Ele é de
// 19/09; a #51, que separou a recusa de entrada da recusa com linha de execução,
// mergeou em 26/09. Hoje o card VIZINHO carrega um texto literal do próprio
// protótipo dizendo *"somar os dois esconde qual dos dois problemas existe"* — e a
// lista rasa é o convite exato a somar: quatro linhas de uma população e uma da
// outra, sem nada que as distinga, ordenadas juntas. Quem lê soma de cima para
// baixo.
//
// **A nota `dados` do `canvas.json` sustenta a leitura**, com as palavras do autor:
// *"Dados de exemplo, não medições."* Três das cinco linhas não são valor de
// vocabulário nenhum — `ExecutionMetricsValues.FailurePhase` tem sete, e nenhum é
// *"Servidor MCP indisponível"*. O artboard desenha a FORMA (rótulo à esquerda,
// contagem à direita, linhas separadas por borda), e é a forma que o card segue.
//
// **A forma dos grupos é idioma da casa, com precedente do mesmo tipo:** o
// `AgentFailuresCard` divide o card em dois grupos contra uma tabela única do
// artboard do agente, e a D9 de lá dá a razão — *"cruzá-las no cliente inventaria a
// junção"*. Aqui o argumento é mais forte: lá eram duas agregações sobre a mesma
// população; aqui são duas populações.
//
// **Convenção 17, e ela está no `canvas.json`**, na nota `estados`, do autor:
// *"contrariar o protótipo é resultado legítimo e vira registro, com gatilho para
// voltar — não implementação silenciosa"*.
//
// **GATILHO DE VOLTA:** o dono olhar a tela com os dois grupos e preferir a lista
// rasa. Aí a divergência cai — e o que muda com ela é o texto do card de Falhas,
// que passa a ser o que contradiz a tela.
//
// ===========================================================================
// SÃO DOIS GRUPOS, E NÃO TRÊS
// ===========================================================================
//
// Fases de execução e falhas de indexação seguem na **lista concatenada e ordenada
// por contagem** que já existia: são a mesma população — coisas que falharam —, e é
// assim que o dono a conferiu. O que a change separa é a outra população.
//
// ===========================================================================
// ESTE CARD NÃO DECLARA REGIME NENHUM — DECISÃO DO DONO NA CONFERÊNCIA
// ===========================================================================
//
// Ele declarava: a nota de regime de **indexação** vivia na barra de título, e a D7
// desta change a tinha movido para o cabeçalho do grupo de falha. **O lugar nunca
// foi o problema — o TEXTO era.**
//
// O card mostra **três populações de três regimes**: fase de falha de execução
// (`execution`, 22/09), falha de indexação (`embedding`, 23/09) e motivo de recusa
// de entrada (`rejection`, 26/09). **Nomear um deles no cabeçalho afirma que tudo
// ali é daquele, quando dois terços não são** — e é a mesma família do defeito que
// esta change corrige no card vizinho: declarar o regime de uma população como se
// fosse de outra.
//
// **NOTA AUSENTE É MELHOR QUE NOTA ERRADA**, e a informação não se perde: o card de
// Falhas ao lado declara "recusa medida desde…" no quadro dela, a página declara o
// de execução no cabeçalho, e o mapa de regimes chega inteiro na resposta.
//
// As duas alternativas, recusadas com o motivo:
//
//   - **a data mais antiga sem nomear o regime** — seria verdade no sentido de que
//     nada ali foi medido antes dela, mas sugeriria que um motivo de recusa de
//     23/09 poderia existir, e ele não poderia;
//   - **uma nota por grupo** — correto, e é o que a página faz noutros cards, mas
//     acrescenta três linhas ao card que acabou de ganhar dois grupos. **Fica como
//     a saída registrada** para quando alguém precisar, e tem issue com gatilho.
//
// **E o regime da RECUSA nunca foi declarado aqui** — está no quadro do card de
// Falhas, junto da CONTAGEM que este grupo decompõe. Declará-lo nos dois seria a
// mesma data em dois cards vizinhos, e é a duplicação que o dono já apontou uma vez,
// no `caveat` de recusa da aba do agente. Pelo mesmo motivo a contagem total não é
// repetida aqui.
//
// FASE E MOTIVO DESCONHECIDOS APARECEM NEUTROS, com o valor cru — nunca o rótulo de
// outro. No motivo isso custa mais que na fase: a coluna é obrigatória em
// `task_rejections`, então a soma dos motivos fecha EXATAMENTE com
// `rejectedAtEntryCount`, e omitir um quebra a soma sem sintoma na tela.

export interface FailureReasonsCardProps {
  errors: ErrorInsights;
  queryState: QueryState;
  reason?: string;
}

// Sem `nota`: o parâmetro saiu junto com a decisão acima. Deixá-lo aqui sem
// consumidor seria a forma que esta change já registrou duas vezes (#83 e #89) —
// declaração viva que o compilador não acusa porque não custa nada a ninguém.
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

function Linha({
  testId,
  label,
  count,
  queryState,
  unknownAttr,
}: {
  testId: string;
  label: { text: string; unknown: boolean };
  count: number;
  queryState: QueryState;
  /** O atributo que marca o valor cru. Difere por população, de propósito. */
  unknownAttr: 'data-unknown-phase' | 'data-unknown-reason';
}) {
  return (
    <Group justify="space-between" data-testid={testId} {...{ [unknownAttr]: label.unknown ? 'true' : undefined }}>
      <Text
        size="sm"
        c={label.unknown ? 'dimmed' : undefined}
        ff={label.unknown ? 'monospace' : undefined}
      >
        {label.text}
      </Text>
      <MetricValue
        value={count}
        queryState={queryState}
        format={formatCount}
        size="sm"
        fw={400}
        data-testid={`${testId}-contagem`}
      />
    </Group>
  );
}

export function FailureReasonsCard({
  errors,
  queryState,
  reason,
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

  // Nenhum motivo NÃO é o mesmo que nenhuma recusa, e é por isso que o grupo
  // depende da LISTA e não da contagem: com recusa e sem motivo, abrir um grupo
  // vazio seria um elemento cuja única função é falar do que ele não mostra — e
  // nomear ali a causa plausível é o defeito que o guarda negativo impede.
  const temMotivoDeRecusa = errors.rejectionsByReason.length > 0;
  const semNadaAListar = linhas.length === 0 && !temMotivoDeRecusa;

  return (
    <Card withBorder padding={0} data-testid="card-motivos">
      <Box px="md" py="sm" bg="var(--buteco-surface-subtle)">
        <Text size="sm" fw={600}>
          Motivos
        </Text>
      </Box>
      <Stack gap="md" p="md">
        {queryState !== 'ok' ? (
          <MetricValue
            value={null}
            queryState={queryState}
            reason={reason}
            size={METRIC_SIZE.card}
            data-testid="motivos-travessao"
          />
        ) : semNadaAListar ? (
          <Text size="sm" c="dimmed" data-testid="motivos-vazio">
            {/* O CARD TEM QUATRO ESTADOS, E O TEXTO VAZIO COBRE DOIS DELES.
                Os quatro: (1) há falha — grupo de falha, sem texto; (2) há recusa
                com motivo — grupo de recusa, sem texto; (3) nada de nada; (4) há
                recusa e a resposta não trouxe o motivo dela.

                O texto único antigo — "Nenhuma falha nem recusa neste período" —
                cobria (3) e (4) com a mesma frase, e no (4) ela é **falsa na segunda
                metade**: houve recusa, e o que falta é o motivo. Convenção 13 na
                forma mais direta: a tela não afirma ausência de uma medição que
                existe. */}
            {errors.rejectedAtEntryCount > 0
              ? 'Nenhuma falha no período. Houve recusa na entrada, e a resposta não trouxe o motivo dela.'
              : 'Nenhuma falha nem recusa neste período.'}
          </Text>
        ) : (
          <>
            {linhas.length > 0 ? (
              <Grupo titulo="Por que as execuções falharam" testId="grupo-motivos-de-falha">
                {linhas.map((linha) => (
                  <Linha
                    key={linha.chave}
                    testId={`motivo-${linha.chave}`}
                    label={linha.label}
                    count={linha.count}
                    queryState={queryState}
                    unknownAttr="data-unknown-phase"
                  />
                ))}
              </Grupo>
            ) : null}

            {temMotivoDeRecusa ? (
              <Grupo titulo="Por que as tasks foram recusadas na entrada" testId="grupo-motivos-de-recusa">
                {errors.rejectionsByReason.map((m) => (
                  <Linha
                    key={m.reason}
                    testId={`motivo-recusa-${m.reason}`}
                    label={rejectionReasonLabel(m.reason)}
                    count={m.count}
                    queryState={queryState}
                    unknownAttr="data-unknown-reason"
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
