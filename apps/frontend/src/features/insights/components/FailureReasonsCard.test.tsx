import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { FailureReasonsCard } from './FailureReasonsCard';
import { errorsFixture } from '../test/systemInsightsFixture';
import type { ErrorInsights } from '../types/systemInsights';
import type { QueryState } from '../utils/metricState';
import {
  KNOWN_REJECTION_REASONS,
  rejectionReasonLabel,
} from '../utils/rejectionReasonLabels';

function renderCard(errors: ErrorInsights, queryState: QueryState = 'ok') {
  render(
    <MantineProvider theme={theme}>
      <FailureReasonsCard
        errors={errors}
        queryState={queryState}
        reason="A consulta não respondeu."
      />
    </MantineProvider>,
  );
}

describe('FailureReasonsCard', () => {
  it('cada fase de falha aparece com o rótulo de operador e a contagem', () => {
    renderCard(
      errorsFixture({
        byPhase: [
          { phase: 'AgentRun', count: 6 },
          { phase: 'ToolResolution', count: 3 },
        ],
      }),
    );

    expect(screen.getByTestId('motivo-fase-AgentRun')).toHaveTextContent('Execução do agente');
    expect(screen.getByTestId('motivo-fase-AgentRun-contagem')).toHaveTextContent('6');
    expect(screen.getByTestId('motivo-fase-ToolResolution')).toHaveTextContent(
      'Resolução de ferramentas',
    );
  });

  it('as falhas de indexação entram na mesma lista', () => {
    renderCard(
      errorsFixture({
        indexingFailures: [{ outcome: 'Failed', failurePhase: 'EmbeddingGateway', count: 2 }],
      }),
    );

    expect(screen.getByTestId('motivo-indexacao-Failed-EmbeddingGateway')).toHaveTextContent(
      'Falhou · Chamada ao gateway de embedding',
    );
  });

  it('fase DESCONHECIDA aparece de forma neutra, com o valor cru', () => {
    renderCard(errorsFixture({ byPhase: [{ phase: 'FaseNovaDoWorkers', count: 4 }] }));

    const linha = screen.getByTestId('motivo-fase-FaseNovaDoWorkers');
    expect(linha).toHaveTextContent('FaseNovaDoWorkers');
    // O MARCADOR MUDOU DE ELEMENTO, não de garantia: era um `querySelector`
    // dentro da linha, e passa a ser atributo DA linha. As duas populações usam
    // agora a mesma forma (`data-unknown-phase` / `data-unknown-reason` no
    // elemento que carrega o `data-testid`), e é o que permite ler os dois
    // guardas lado a lado sem ter de conferir de qual nó cada um fala.
    expect(linha).toHaveAttribute('data-unknown-phase', 'true');
  });

  it('NEGATIVO: a fase desconhecida NÃO reaproveita o rótulo de outra fase', () => {
    // Afirmar uma causa errada é pior que não afirmar nenhuma.
    renderCard(errorsFixture({ byPhase: [{ phase: 'FaseNovaDoWorkers', count: 4 }] }));

    const texto = screen.getByTestId('card-motivos').textContent ?? '';
    for (const rotulo of [
      'Execução do agente',
      'Resolução de ferramentas',
      'Carga da sessão',
      'Gravação do resultado',
    ]) {
      expect(texto).not.toContain(rotulo);
    }
  });

  it('a fase desconhecida não é omitida', () => {
    renderCard(errorsFixture({ byPhase: [{ phase: 'FaseNova', count: 4 }] }));

    // Omiti-la faria a soma dos motivos não fechar com a contagem de falhas —
    // sem sintoma, porque ninguém soma à mão.
    expect(screen.getByTestId('motivo-fase-FaseNova-contagem')).toHaveTextContent('4');
  });

  // ------------------------------------------------------------- L1 (#51)

  // OS DOIS GUARDAS QUE ESTAVAM AQUI INVERTERAM, E O SUBSTITUTO É UM (D4).
  //
  // Eram `NEGATIVO: a recusa NÃO vira quadro no card de Motivos` — que afirmava
  // `card.textContent` não casar com `/recusa/i` — e `NEGATIVO: NENHUMA causa é
  // nomeada para as recusas`, que proibia `/sem provider/i`, `/agente inativo/i` e
  // `/modelo configurado/i`. Os dois estavam CERTOS enquanto nenhuma causa era
  // medida, e as três expressões do segundo são exactamente dois dos rótulos que a
  // tela agora apresenta COM fonte.
  //
  // A GARANTIA SOBREVIVE, A FORMA NÃO. A garantia é *"a tela não nomeia causa que
  // ninguém mediu"*; a forma era *"a palavra não aparece"*, e ela funcionava só
  // porque ausência de palavra e ausência de medição coincidiam. A #51 as separou.
  //
  // O substituto é `NEGATIVO: com recusa e SEM motivo, NENHUMA causa é nomeada`,
  // mais abaixo: ele afirma a PROCEDÊNCIA em vez da ausência de palavra, e cobre o
  // estado em que a tentação de preencher com a causa plausível volta. Verificado
  // contra o defeito reintroduzido — ver a rodada de guardas no `02`.
  //
  // E `sem recusa, não há lacuna de recusa` saiu com eles: `motivos-lacuna-recusa`
  // não existe mais em nenhum caminho do componente, e um guarda que afirma a
  // ausência de um testid que ninguém pode produzir não cobre nada. O que ele
  // queria dizer virou `sem recusa, nenhum grupo de motivos de recusa`, abaixo.

  it('sem recusa, nenhum grupo de motivos de recusa é aberto', () => {
    renderCard(
      errorsFixture({
        byPhase: [{ phase: 'AgentRun', count: 6 }],
        rejectedAtEntryCount: 0,
        rejectionsByReason: [],
      }),
    );

    expect(screen.queryByTestId('grupo-motivos-de-recusa')).toBeNull();
    // E o grupo de falha não ganha cabeçalho de grupo sozinho por causa disso: ele
    // aparece porque tem linhas, não porque o outro faltou.
    expect(screen.getByTestId('grupo-motivos-de-falha')).toBeInTheDocument();
  });

  it('sem motivo nenhum, o card diz o zero medido POR EXTENSO', () => {
    renderCard(errorsFixture());

    const vazio = screen.getByTestId('motivos-vazio');
    expect(vazio).toHaveTextContent('Nenhuma falha nem recusa neste período.');
    // Por extenso, e não o algarismo isolado: é o que o distingue do travessão
    // em palavras, e não só em símbolo.
    expect(vazio.textContent).not.toMatch(/^\s*0\s*$/);
  });

  // --------------------------------------- OS MOTIVOS DA RECUSA DE ENTRADA (#75)

  it('cada motivo de recusa aparece com o rótulo de operador e a contagem', () => {
    renderCard(
      errorsFixture({
        rejectedAtEntryCount: 9,
        rejectionsByReason: [
          { reason: 'ProviderOrModelMissing', count: 5 },
          { reason: 'AgentInactive', count: 3 },
          { reason: 'AgentNotFound', count: 1 },
        ],
      }),
    );

    expect(screen.getByTestId('motivo-recusa-ProviderOrModelMissing')).toHaveTextContent(
      'Agente sem provider ou modelo configurado',
    );
    expect(
      screen.getByTestId('motivo-recusa-ProviderOrModelMissing-contagem'),
    ).toHaveTextContent('5');
    expect(screen.getByTestId('motivo-recusa-AgentNotFound')).toHaveTextContent(
      'Agente não encontrado',
    );
  });

  it('os motivos de recusa ficam em GRUPO PRÓPRIO, separados das fases', () => {
    // A DIVERGÊNCIA DO ARTBOARD, e ela é o miolo da change (design.md D2).
    //
    // O `Main.dc.html` desenha o card como UMA lista rasa ordenada por contagem,
    // com a linha 2 sendo o motivo da recusa misturado às fases de execução. Ele
    // foi desenhado antes de a #51 existir, quando "recusa" era um número só — e
    // uma lista em que a linha 1 é fase e a linha 2 é motivo, sem marca nenhuma,
    // convida exactamente à soma que o card vizinho proíbe em texto: "somar os
    // dois esconde qual dos dois problemas existe".
    //
    // Convenção 17, com o gatilho de volta escrito no cabeçalho do componente.
    renderCard(
      errorsFixture({
        byPhase: [{ phase: 'AgentRun', count: 6 }],
        rejectedAtEntryCount: 5,
        rejectionsByReason: [{ reason: 'ProviderOrModelMissing', count: 5 }],
      }),
    );

    const grupoRecusa = screen.getByTestId('grupo-motivos-de-recusa');
    const grupoFalha = screen.getByTestId('grupo-motivos-de-falha');

    // Cada linha mora no seu grupo, e nenhum grupo contém o outro.
    expect(grupoRecusa).toContainElement(screen.getByTestId('motivo-recusa-ProviderOrModelMissing'));
    expect(grupoFalha).toContainElement(screen.getByTestId('motivo-fase-AgentRun'));
    expect(grupoFalha).not.toContainElement(
      screen.getByTestId('motivo-recusa-ProviderOrModelMissing'),
    );
  });

  it('NEGATIVO: nenhum total SOMA as duas populações', () => {
    // 6 + 5 = 11, e é o número que a lista rasa do artboard produziria na cabeça
    // de quem soma de cima para baixo.
    renderCard(
      errorsFixture({
        byPhase: [{ phase: 'AgentRun', count: 6 }],
        rejectedAtEntryCount: 5,
        rejectionsByReason: [{ reason: 'ProviderOrModelMissing', count: 5 }],
      }),
    );

    expect(screen.getByTestId('card-motivos').textContent).not.toContain('11');
  });

  it('a soma dos motivos FECHA com a contagem de recusa de entrada', () => {
    // A coluna de motivo é obrigatória em `task_rejections`, então a soma fecha
    // por construção na fonte. O guarda existe porque a tela pode quebrá-la de um
    // jeito que não dá sintoma: omitindo uma linha.
    const motivos = [
      { reason: 'ProviderOrModelMissing', count: 5 },
      { reason: 'AgentInactive', count: 3 },
      { reason: 'AgentNotFound', count: 1 },
    ];
    renderCard(errorsFixture({ rejectedAtEntryCount: 9, rejectionsByReason: motivos }));

    const apresentadas = motivos.map((m) =>
      Number(screen.getByTestId(`motivo-recusa-${m.reason}-contagem`).textContent),
    );

    expect(apresentadas.reduce((a, b) => a + b, 0)).toBe(9);
  });

  it('motivo DESCONHECIDO aparece, cru e neutro', () => {
    renderCard(
      errorsFixture({
        rejectedAtEntryCount: 4,
        rejectionsByReason: [{ reason: 'QuotaExceeded', count: 4 }],
      }),
    );

    const linha = screen.getByTestId('motivo-recusa-QuotaExceeded');
    expect(linha).toHaveTextContent('QuotaExceeded');
    expect(linha).toHaveAttribute('data-unknown-reason', 'true');
  });

  it('NEGATIVO: o motivo desconhecido NÃO é omitido, e a soma continua fechando', () => {
    // O modo de falha mais caro dos dois: omitir faz a soma deixar de fechar com
    // `rejectedAtEntryCount` SEM NENHUM SINTOMA, porque ninguém soma à mão.
    renderCard(
      errorsFixture({
        rejectedAtEntryCount: 9,
        rejectionsByReason: [
          { reason: 'ProviderOrModelMissing', count: 5 },
          { reason: 'QuotaExceeded', count: 4 },
        ],
      }),
    );

    const conhecida = Number(
      screen.getByTestId('motivo-recusa-ProviderOrModelMissing-contagem').textContent,
    );
    const desconhecida = Number(
      screen.getByTestId('motivo-recusa-QuotaExceeded-contagem').textContent,
    );

    expect(conhecida + desconhecida).toBe(9);
  });

  it('NEGATIVO: o motivo desconhecido não pega o rótulo de nenhum dos quatro', () => {
    renderCard(
      errorsFixture({
        rejectedAtEntryCount: 4,
        rejectionsByReason: [{ reason: 'QuotaExceeded', count: 4 }],
      }),
    );

    const linha = screen.getByTestId('motivo-recusa-QuotaExceeded').textContent ?? '';
    for (const conhecido of KNOWN_REJECTION_REASONS) {
      expect(linha).not.toContain(rejectionReasonLabel(conhecido).text);
    }
  });

  it('NEGATIVO: com recusa e SEM motivo, NENHUMA causa é nomeada', () => {
    // ESTE GUARDA SUBSTITUI OS DOIS QUE ESTA CHANGE INVERTEU, e é o estado em que
    // a tentação de preencher com a causa plausível volta: há recusa, e a fonte
    // não disse por quê.
    //
    // Os dois antigos afirmavam que a palavra "recusa" e as três expressões de
    // causa NÃO apareciam no card. A garantia que eles protegiam — a tela nunca
    // nomeia causa que ninguém mediu — sobrevive inteira; a FORMA não, porque ela
    // funcionava só enquanto ausência de palavra e ausência de medição
    // coincidiam, e a #51 as separou. As três expressões que o antigo proibia
    // (`/sem provider/i`, `/agente inativo/i`, `/modelo configurado/i`) são
    // exactamente dois dos rótulos que a tela agora apresenta COM fonte.
    renderCard(errorsFixture({ rejectedAtEntryCount: 5, rejectionsByReason: [] }));

    const texto = screen.getByTestId('card-motivos').textContent ?? '';
    for (const conhecido of KNOWN_REJECTION_REASONS) {
      expect(texto).not.toContain(rejectionReasonLabel(conhecido).text);
    }
    // E nenhum grupo de motivos de recusa é aberto para ficar vazio.
    expect(screen.queryByTestId('grupo-motivos-de-recusa')).toBeNull();
  });

  it('NEGATIVO: a contagem de recusa não é apresentada NESTE card', () => {
    // O número da recusa vive no card de Falhas, com o seu caveat e o seu regime.
    // Este card apresenta a DECOMPOSIÇÃO dele, e repetir o total aqui seria o
    // mesmo número em dois cards vizinhos.
    renderCard(
      errorsFixture({
        rejectedAtEntryCount: 97,
        rejectionsByReason: [{ reason: 'AgentInactive', count: 97 }],
      }),
    );

    const grupo = screen.getByTestId('grupo-motivos-de-recusa');
    // O 97 aparece UMA vez — como a contagem da linha —, e não também como total.
    const ocorrencias = (grupo.textContent ?? '').split('97').length - 1;
    expect(ocorrencias).toBe(1);
  });

  it('NEGATIVO: o card de Motivos não declara regime NENHUM', () => {
    // ACHADO DA CONFERÊNCIA DO DONO, e a decisão dele é REMOVER.
    //
    // O card mostra TRÊS populações de três regimes — fase de falha de execução
    // (`execution`), falha de indexação (`embedding`) e motivo de recusa de
    // entrada (`rejection`). Nomear um deles no cabeçalho afirma que tudo ali é
    // daquele, quando dois terços não são. **Nota ausente é melhor que nota
    // errada**, e a informação não se perde: o card de Falhas ao lado declara
    // "recusa medida desde…" no quadro dela, e a página declara o de execução.
    //
    // A PRECONDIÇÃO VAI AFIRMADA ANTES DA AUSÊNCIA. Um guarda de ausência por
    // string passa de graça quando o arranjo não produz o estado que ele nega —
    // é a forma que esta change já pegou duas vezes. Aqui o card é montado com
    // as três populações presentes, e isso é afirmado primeiro: é exatamente o
    // estado em que a nota apareceria.
    //
    // E ESTE GUARDA, SOZINHO, NÃO DISCRIMINA — está escrito aqui porque é onde
    // se lê, e a honestidade sobre ele importa: com a prop removida, o
    // componente não TEM como renderizar nota de regime, então o caso passa por
    // construção. Ele vale como trava estrutural (o dia em que alguém
    // reintroduzir a prop, ele reprova), e não como prova da correção.
    //
    // **Quem discrimina é o par na `SystemInsightsPage.test.tsx`** — "o card de
    // Motivos NÃO recebe nota de regime, e o de provedor continua recebendo" —,
    // porque o defeito morava na FIAÇÃO, não no componente: era a página que
    // passava `regimeNote` para cá. Guarda tem de reprovar no componente que a
    // correção toca (convenção 15, segunda forma).
    renderCard(
      errorsFixture({
        byPhase: [{ phase: 'AgentRun', count: 6 }],
        indexingFailures: [{ outcome: 'Failed', failurePhase: 'Chunking', count: 2 }],
        rejectedAtEntryCount: 5,
        rejectionsByReason: [{ reason: 'AgentInactive', count: 5 }],
      }),
    );

    // Precondição: as três populações ESTÃO na tela.
    expect(screen.getByTestId('motivo-fase-AgentRun')).toBeInTheDocument();
    expect(screen.getByTestId('motivo-indexacao-Failed-Chunking')).toBeInTheDocument();
    expect(screen.getByTestId('motivo-recusa-AgentInactive')).toBeInTheDocument();

    // E nenhum nome de regime aparece, nem o texto que os acompanha.
    const texto = screen.getByTestId('card-motivos').textContent ?? '';
    for (const regime of ['execution', 'embedding', 'rejection', 'medido desde', 'medida desde']) {
      expect(texto).not.toContain(regime);
    }
    expect(screen.queryByTestId('nota-de-regime')).toBeNull();
  });

  it('sem falha e COM recusa sem motivo, o texto não afirma que não houve recusa', () => {
    // O QUARTO ESTADO DO TEXTO VAZIO, e o antigo o escondia: ele dizia sempre
    // "Nenhuma falha nem recusa neste período", e com `rejectedAtEntryCount > 0` e
    // `rejectionsByReason` vazio isso é FALSO na segunda metade — houve recusa, e o
    // que falta é o motivo dela.
    //
    // É a convenção 13 na forma mais direta: a tela não afirma ausência de uma
    // medição que existe.
    renderCard(
      errorsFixture({ byPhase: [], rejectedAtEntryCount: 5, rejectionsByReason: [] }),
    );

    const vazio = screen.getByTestId('motivos-vazio');
    expect(vazio).toHaveTextContent('Houve recusa na entrada');
    expect(vazio.textContent).not.toContain('Nenhuma falha nem recusa');
  });

  it('consulta sem resposta põe travessão, e não a frase do zero medido', () => {
    renderCard(errorsFixture({ byPhase: [{ phase: 'AgentRun', count: 6 }] }), 'failed');

    expect(screen.getByTestId('motivos-travessao')).toHaveAttribute(
      'data-metric-state',
      'unknown',
    );
    // O modo de falha a evitar é cair no estado 2: requisição que não respondeu
    // não é evidência de ausência (quadro 3 do `Estados.dc.html`).
    expect(screen.queryByTestId('motivos-vazio')).toBeNull();
    expect(screen.getByTestId('card-motivos').textContent).not.toContain('Nenhuma falha');
  });
});
