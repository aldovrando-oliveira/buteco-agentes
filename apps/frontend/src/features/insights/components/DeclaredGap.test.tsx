import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { DeclaredGap } from './DeclaredGap';
import { MetricValue } from './MetricValue';

function renderGap() {
  return render(
    <MantineProvider theme={theme}>
      <DeclaredGap
        label="Cache lido por modelo"
        qualifier="não disponível"
        data-testid="lacuna"
      />
    </MantineProvider>,
  );
}

describe('DeclaredGap — o quadro 6 do Estados.dc.html', () => {
  it('nomeia o que falta, e NÃO argumenta', () => {
    renderGap();

    // Uma linha: rótulo e qualificador. O PORQUÊ pertence à issue — a
    // convenção 23 existe para que ele sobreviva ao archive, e a tela só
    // precisa tornar a ausência visível (decisão do dono, 24/09).
    const lacuna = screen.getByTestId('lacuna');
    expect(lacuna).toHaveTextContent('Cache lido por modelo — não disponível');
    expect(lacuna.querySelectorAll('p')).toHaveLength(1);
  });

  it('NEGATIVO: não renderiza número nenhum', () => {
    renderGap();

    // Lacuna declarada é a ausência de fonte, não um valor. Qualquer algarismo
    // aqui seria um número inventado no lugar do que não existe.
    expect(screen.getByTestId('lacuna').textContent).not.toMatch(/\d/);
  });

  it('NEGATIVO: não renderiza travessão EM POSIÇÃO DE VALOR', () => {
    renderGap();

    // O travessão é "a consulta não respondeu". A lacuna é "este dado não é
    // coletado". Colapsar os dois faria alguém tentar de novo esperando que
    // funcionasse.
    //
    // A asserção é sobre a POSIÇÃO, não sobre o glifo: o quadro 6 do
    // `Estados.dc.html` usa o travessão como SEPARADOR — desenha "Cache de
    // prompt escrito — recusado" —, e o protótipo vence. O que o estado de
    // lacuna não pode ter é um travessão onde caberia um número, que é o que
    // `data-metric-state="unknown"` marca. A primeira versão deste caso proibia
    // o glifo inteiro e reprovava contra o próprio protótipo.
    const lacuna = screen.getByTestId('lacuna');
    expect(lacuna.querySelector('[data-metric-state]')).toBeNull();
    expect(lacuna.querySelector('[data-metric-state="unknown"]')).toBeNull();
  });

  it('o qualificador vem por prop, e não afirma coleta onde falta exposição', () => {
    renderGap();

    const texto = screen.getByTestId('lacuna').textContent ?? '';
    // O qualificador vem POR PROP, e não cravado no componente: só a L1 (motivo
    // da recusa) é de fato "não coletado". Cravá-lo fazia o KPI de chamadas ao
    // provedor dizer "não coletado" sobre `ProviderCall.Purpose`, que É gravado
    // — e a linha seguinte, que dizia isso, contradizia a primeira.
    expect(texto).toContain('não disponível');
    expect(texto).not.toMatch(/falhou|erro|tentar de novo/i);
  });

  it('a variante inline não tem moldura, e continua marcada como lacuna', () => {
    render(
      <MantineProvider theme={theme}>
        <DeclaredGap
          variant="inline"
          label="turno e compactação"
          qualifier="não disponível"
          data-testid="lacuna-inline"
        />
      </MantineProvider>,
    );

    // Onde a lacuna substitui um SUBTÍTULO, ela tem o peso de subtítulo: o
    // protótipo escreve ali "522 de turno · 41 de compactação", do mesmo peso
    // das linhas dos outros cinco cards. A moldura tracejada ficava maior que o
    // próprio número (pego na conferência manual).
    const lacuna = screen.getByTestId('lacuna-inline');
    expect(lacuna).toHaveAttribute('data-gap-variant', 'inline');
    expect(lacuna.getAttribute('style') ?? '').not.toContain('dashed');

    // Mesmo estado, mesma marca: o que muda é o peso, não o significado.
    expect(lacuna).toHaveAttribute('data-declared-gap', 'true');
    expect(lacuna.querySelector('[data-metric-state]')).toBeNull();
    expect(lacuna).toHaveTextContent('turno e compactação');
    expect(lacuna).toHaveTextContent('não disponível');
  });

  it('a variante block mantém a moldura tracejada do quadro 6', () => {
    renderGap();

    const lacuna = screen.getByTestId('lacuna');
    expect(lacuna).toHaveAttribute('data-gap-variant', 'block');
    expect(lacuna.getAttribute('style') ?? '').toContain('dashed');
  });

  it('tem apresentação DISTINTA do travessão na mesma tela', () => {
    render(
      <MantineProvider theme={theme}>
        <DeclaredGap
          label="Sem fonte"
          qualifier="não disponível"
          data-testid="lacuna"
        />
        <MetricValue
          value={null}
          queryState="failed"
          reason="A consulta não respondeu."
          data-testid="travessao"
        />
      </MantineProvider>,
    );

    // Os dois estados convivem na mesma tela (o cenário da spec), e o que os
    // separa é observável: a lacuna se marca por `data-declared-gap`, o
    // travessão por `data-metric-state="unknown"`, e nenhum carrega a marca do
    // outro.
    expect(screen.getByTestId('lacuna')).toHaveAttribute('data-declared-gap', 'true');
    expect(screen.getByTestId('lacuna')).not.toHaveAttribute('data-metric-state');
    expect(screen.getByTestId('travessao')).toHaveAttribute('data-metric-state', 'unknown');
    expect(screen.getByTestId('travessao')).not.toHaveAttribute('data-declared-gap');
  });
});
