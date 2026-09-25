import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { PartialMeasurementNotice } from './PartialMeasurementNotice';
import { insightsWindowFixture, REGIMES_FIXTURE } from '../test/systemInsightsFixture';
import { measuredDays } from '../utils/measuredDays';
import type { DailyInsightPoint } from '../types/systemInsights';

const janela = insightsWindowFixture();

function renderAviso(serie: DailyInsightPoint[], regimeStart: string | null = REGIMES_FIXTURE.execution) {
  render(
    <MantineProvider theme={theme}>
      <PartialMeasurementNotice
        measured={measuredDays(janela, serie)}
        regimeStart={regimeStart}
        timeZone={janela.timeZone}
      />
    </MantineProvider>,
  );
}

/** A série da rota real: só 22, 23 e 24/09 dentro de uma janela de 10 dias. */
const serie: DailyInsightPoint[] = [
  { day: '2026-09-22', taskCount: 26, tokenCount: null },
  { day: '2026-09-23', taskCount: 0, tokenCount: null },
  { day: '2026-09-24', taskCount: 33, tokenCount: null },
];

describe('PartialMeasurementNotice — o quadro 1 do Estados.dc.html', () => {
  it('conta os dias pedidos, os medidos e os que não existem', () => {
    renderAviso(serie);

    expect(screen.getByTestId('aviso-medicao-parcial-contagem')).toHaveTextContent(
      'Dos 10 dias pedidos, 3 têm medida e 7 não existem',
    );
  });

  it('diz quando a coleta começou, a partir do REGIME', () => {
    renderAviso(serie);

    expect(screen.getByTestId('aviso-medicao-parcial-contagem')).toHaveTextContent(
      'A coleta começou em 22/09/2026.',
    );
  });

  it('A FRASE QUE FAZ O TRABALHO: "não são dias sem uso"', () => {
    // É a distinção que a change inteira defende, dita em quatro palavras e no
    // lugar onde ela é lida primeiro — antes dos números, não depois.
    renderAviso(serie);

    expect(screen.getByTestId('aviso-medicao-parcial')).toHaveTextContent(
      'não são dias sem uso',
    );
  });

  it('o título é o do artboard', () => {
    renderAviso(serie);

    expect(screen.getByTestId('aviso-medicao-parcial')).toHaveTextContent(
      'O período escolhido é maior que a medição',
    );
  });

  it('NEGATIVO: janela toda medida NÃO renderiza o aviso', () => {
    // É o estado do `Main.dc.html`: a série cobre a janela inteira, não há
    // trecho sem medição, e um aviso sobre medição parcial não teria do que
    // falar. Mesma lição da legenda da hachura (5ª rodada): elemento de
    // artboard de ESTADOS só aparece no estado dele.
    const dezDias: DailyInsightPoint[] = Array.from({ length: 10 }, (_, i) => ({
      day: `2026-09-${String(15 + i).padStart(2, '0')}`,
      taskCount: i,
      tokenCount: null,
    }));
    renderAviso(dezDias);

    expect(screen.queryByTestId('aviso-medicao-parcial')).toBeNull();
  });

  it('NEGATIVO: as contagens saem da SÉRIE, não do regime', () => {
    // O corte da D2. Aqui a série contradiz o regime — traz um dia ANTERIOR
    // ao início declarado —, e a contagem segue a série: 4 medidos, 6 não.
    // Se o regime classificasse dia, este caso daria 3 e 7.
    renderAviso([...serie, { day: '2026-09-16', taskCount: 5, tokenCount: null }]);

    expect(screen.getByTestId('aviso-medicao-parcial-contagem')).toHaveTextContent(
      'Dos 10 dias pedidos, 4 têm medida e 6 não existem',
    );
  });

  it('sem regime na resposta, as contagens continuam e só a data some', () => {
    renderAviso(serie, null);

    const texto = screen.getByTestId('aviso-medicao-parcial-contagem').textContent ?? '';
    expect(texto).toContain('Dos 10 dias pedidos, 3 têm medida e 7 não existem');
    expect(texto).not.toContain('A coleta começou');
  });

  it('concorda em número no singular', () => {
    const noveDias: DailyInsightPoint[] = Array.from({ length: 9 }, (_, i) => ({
      day: `2026-09-${String(16 + i).padStart(2, '0')}`,
      taskCount: 1,
      tokenCount: null,
    }));
    renderAviso(noveDias);

    expect(screen.getByTestId('aviso-medicao-parcial-contagem')).toHaveTextContent(
      'Dos 10 dias pedidos, 9 têm medida e 1 não existe',
    );
  });
});
