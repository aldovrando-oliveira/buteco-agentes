import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { FailuresCard } from './FailuresCard';
import { errorsFixture } from '../test/systemInsightsFixture';
import type { ErrorInsights } from '../types/systemInsights';
import type { QueryState } from '../utils/metricState';

function renderCard(
  errors: ErrorInsights,
  executedTaskCount: number,
  queryState: QueryState = 'ok',
) {
  render(
    <MantineProvider theme={theme}>
      <FailuresCard
        errors={errors}
        executedTaskCount={executedTaskCount}
        queryState={queryState}
        reason="A consulta não respondeu."
      />
    </MantineProvider>,
  );
}

describe('FailuresCard', () => {
  it('as duas contagens aparecem como números distintos', () => {
    renderCard(errorsFixture({ failedCount: 12, rejectedCount: 5 }), 476);

    expect(screen.getByTestId('falhas-contagem')).toHaveTextContent('12');
    expect(screen.getByTestId('recusas-contagem')).toHaveTextContent('5');
    // Em blocos separados: nenhum lugar da tela soma os dois em 17.
    expect(screen.getByTestId('card-falhas').textContent).not.toContain('17');
  });

  it('os dois quadros têm a MESMA largura, e não a do conteúdo', () => {
    // A primeira versão não tinha quadro nenhum: dois blocos de texto soltos,
    // com largura dirigida pelo conteúdo. O da recusa carrega uma frase e
    // ficava o dobro do outro — e o destaque das duas métricas, que é o que o
    // card existe para dar, se perdia.
    renderCard(errorsFixture({ failedCount: 12, rejectedCount: 5 }), 476);

    for (const id of ['bloco-falhas', 'bloco-recusas']) {
      const estilo = screen.getByTestId(id).getAttribute('style') ?? '';
      expect(estilo).toContain('var(--buteco-surface-subtle)');
      expect(estilo).toContain('border-radius');
    }
    // Irmãos diretos do mesmo Group com `grow`: a largura é do container.
    expect(screen.getByTestId('bloco-falhas').parentElement).toBe(
      screen.getByTestId('bloco-recusas').parentElement,
    );
  });

  it('as contagens saem em 20px, como o artboard mede', () => {
    renderCard(errorsFixture({ failedCount: 12, rejectedCount: 5 }), 476);

    for (const id of ['falhas-contagem', 'recusas-contagem']) {
      // 1.25rem = 20px; `--text-fz` é onde o Mantine escreve o tamanho.
      expect(screen.getByTestId(id).getAttribute('style') ?? '').toContain('1.25rem');
    }
  });

  it('as cores separam os dois problemas, como no artboard', () => {
    // Falha em `red`, recusa em `yellow`. Não é decoração: é a mesma distinção
    // que o texto abaixo explica, dita em cor — e por isso as duas contagens
    // não podem sair da mesma cor.
    renderCard(errorsFixture({ failedCount: 12, rejectedCount: 5 }), 476);

    expect(screen.getByTestId('falhas-contagem')).toHaveStyle({
      color: 'var(--mantine-color-red-filled)',
    });
    expect(screen.getByTestId('recusas-contagem')).toHaveStyle({
      color: 'var(--mantine-color-yellow-filled)',
    });
  });

  it('NEGATIVO: a cor não sobrevive ao travessão', () => {
    // Travessão é "não sei". Pintá-lo de vermelho afirmaria gravidade sobre um
    // dado que não chegou.
    renderCard(errorsFixture({ failedCount: 12, rejectedCount: 5 }), 476, 'failed');

    expect(screen.getByTestId('falhas-contagem')).not.toHaveStyle({
      color: 'var(--mantine-color-red-filled)',
    });
  });

  it('a diferença entre os dois é declarada em texto, literal do protótipo', () => {
    renderCard(errorsFixture({ failedCount: 12, rejectedCount: 5 }), 476);

    expect(screen.getByTestId('nota-falha-versus-recusa')).toHaveTextContent(
      'Recusa é agente inativo ou sem provider e modelo configurados: nunca chega a processar. Falha é execução que começou e quebrou. Somar os dois esconde qual dos dois problemas existe.',
    );
  });

  it('a FALHA tem percentual', () => {
    renderCard(errorsFixture({ failedCount: 12, rejectedCount: 5 }), 476);

    expect(screen.getByTestId('falhas-percentual')).toHaveTextContent('2,5% das tasks');
  });

  it('NEGATIVO: a RECUSA não tem percentual, e traz o caveat no lugar', () => {
    // O protótipo escreve "1,1% das tasks" para a recusa. Ela não produz linha
    // de execução, então subconta o denominador — o percentual seria sobre um
    // total que não inclui as próprias recusas.
    renderCard(errorsFixture({ failedCount: 12, rejectedCount: 5 }), 476);

    const bloco = screen.getByTestId('bloco-recusas');
    expect(bloco.textContent).not.toMatch(/%/);
    expect(bloco.textContent).not.toContain('1,1');
    expect(screen.getByTestId('recusas-caveat')).toHaveTextContent(
      /não geram linha de execução/i,
    );
  });

  it('o caveat da recusa fica DENTRO do bloco da recusa', () => {
    renderCard(errorsFixture({ failedCount: 12, rejectedCount: 5 }), 476);

    // Junto do número que ele limita, e não numa lista ao pé da página.
    expect(screen.getByTestId('bloco-recusas')).toContainElement(
      screen.getByTestId('recusas-caveat'),
    );
  });

  it('NEGATIVO: sem tasks executadas, NENHUM percentual e NENHUM NaN', () => {
    renderCard(errorsFixture({ failedCount: 0, rejectedCount: 0 }), 0);

    expect(screen.queryByTestId('falhas-percentual')).toBeNull();
    const texto = screen.getByTestId('card-falhas').textContent ?? '';
    expect(texto).not.toContain('NaN');
    expect(texto).not.toContain('%');
    expect(texto).not.toContain('Infinity');
  });

  it('zero medido nas duas contagens aparece como 0', () => {
    renderCard(errorsFixture({ failedCount: 0, rejectedCount: 0 }), 476);

    expect(screen.getByTestId('falhas-contagem')).toHaveAttribute('data-metric-state', 'zero');
    expect(screen.getByTestId('recusas-contagem')).toHaveAttribute('data-metric-state', 'zero');
  });

  it('zero falhas sobre tasks medidas produz 0% — que é um percentual medido', () => {
    renderCard(errorsFixture({ failedCount: 0, rejectedCount: 0 }), 476);

    expect(screen.getByTestId('falhas-percentual')).toHaveTextContent('0% das tasks');
  });

  it('consulta sem resposta põe travessão nas duas, e nenhum percentual', () => {
    renderCard(errorsFixture({ failedCount: 12, rejectedCount: 5 }), 476, 'failed');

    expect(screen.getByTestId('falhas-contagem')).toHaveAttribute('data-metric-state', 'unknown');
    expect(screen.getByTestId('recusas-contagem')).toHaveAttribute('data-metric-state', 'unknown');
    expect(screen.queryByTestId('falhas-percentual')).toBeNull();
    expect(screen.getByTestId('card-falhas').textContent).not.toContain('12');
  });
});
