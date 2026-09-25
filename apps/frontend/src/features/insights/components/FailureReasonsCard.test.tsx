import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { FailureReasonsCard } from './FailureReasonsCard';
import { errorsFixture } from '../test/systemInsightsFixture';
import type { ErrorInsights } from '../types/systemInsights';
import type { QueryState } from '../utils/metricState';

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
    expect(linha.querySelector('[data-unknown-phase="true"]')).not.toBeNull();
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

  it('NEGATIVO: a recusa NÃO vira quadro no card de Motivos', () => {
    // A contagem de recusas vive no card de FALHAS, com o seu caveat. O motivo
    // dela a rota não serve — e o quadro tracejado que o anunciava aqui não
    // existe no protótipo. Removido por decisão do dono; o registro é a #51.
    renderCard(errorsFixture({ rejectedCount: 5 }));

    const card = screen.getByTestId('card-motivos');
    expect(screen.queryByTestId('motivos-lacuna-recusa')).toBeNull();
    expect(card.querySelector('[data-declared-gap]')).toBeNull();
    expect(card.textContent).not.toMatch(/recusa/i);
  });

  it('NEGATIVO: NENHUMA causa é nomeada para as recusas', () => {
    // O protótipo desenha "Agente sem provider ou modelo configurado — 5". É
    // plausível — é de fato uma das causas — e por isso ninguém desconfiaria.
    // A tela afirmaria com precisão de número uma causa que ninguém mediu.
    renderCard(errorsFixture({ rejectedCount: 5 }));

    const texto = screen.getByTestId('card-motivos').textContent ?? '';
    expect(texto).not.toMatch(/sem provider/i);
    expect(texto).not.toMatch(/agente inativo/i);
    expect(texto).not.toMatch(/modelo configurado/i);
  });


  it('sem recusa, não há lacuna de recusa', () => {
    renderCard(errorsFixture({ byPhase: [{ phase: 'AgentRun', count: 6 }], rejectedCount: 0 }));

    expect(screen.queryByTestId('motivos-lacuna-recusa')).toBeNull();
  });

  it('sem motivo nenhum, o card diz o zero medido POR EXTENSO', () => {
    renderCard(errorsFixture());

    const vazio = screen.getByTestId('motivos-vazio');
    expect(vazio).toHaveTextContent('Nenhuma falha nem recusa neste período.');
    // Por extenso, e não o algarismo isolado: é o que o distingue do travessão
    // em palavras, e não só em símbolo.
    expect(vazio.textContent).not.toMatch(/^\s*0\s*$/);
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
