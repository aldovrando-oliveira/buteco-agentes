import { describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
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

  it('a lacuna tem o PESO DE SUBTÍTULO, e continua marcada como lacuna', () => {
    render(
      <MantineProvider theme={theme}>
        <DeclaredGap
          label="turno e compactação"
          qualifier="não disponível"
          data-testid="lacuna-inline"
        />
      </MantineProvider>,
    );

    // Onde a lacuna substitui um SUBTÍTULO, ela tem o peso de subtítulo: o
    // protótipo escreve ali "522 de turno · 41 de compactação", do mesmo peso
    // das linhas dos outros cinco cards. Contornar o subtítulo fazia o que falta
    // pesar mais que o que existe (pego na conferência manual).
    //
    // ESTE CASO AFIRMAVA `data-gap-variant="inline"`, e o atributo saiu com a
    // #83: com uma forma só ele era constante, e atributo constante não
    // discrimina nada — era a mesma declaração sem consumidor que a change fecha,
    // reposta no DOM. O que ficou no lugar é a ausência do contorno, que é o
    // estado observável mais próximo do que a spec nega.
    const lacuna = screen.getByTestId('lacuna-inline');
    expect(lacuna.getAttribute('style') ?? '').not.toMatch(/border|outline/);

    // Mesmo estado, mesma marca. `data-declared-gap` FICA: ele tem consumidor —
    // é o que separa a lacuna do travessão, e está no requisito.
    expect(lacuna).toHaveAttribute('data-declared-gap', 'true');
    expect(lacuna.querySelector('[data-metric-state]')).toBeNull();
    expect(lacuna).toHaveTextContent('turno e compactação');
    expect(lacuna).toHaveTextContent('não disponível');
  });

  it('NEGATIVO: sem escolher forma, a lacuna vem SEM moldura', () => {
    // O GUARDA CENTRAL DA #83.
    //
    // `renderGap()` não passa forma nenhuma — é o `<DeclaredGap>` que alguém
    // escreve sem pensar —, e o requisito *"Métrica aprovada no protótipo e sem
    // fonte não é inventada"* proíbe o quadro para coluna. Enquanto houvesse uma
    // forma com moldura disponível, e ainda por cima como padrão, o caso proibido
    // era o que saía de graça.
    renderGap();

    const lacuna = screen.getByTestId('lacuna');
    expect(lacuna.getAttribute('style') ?? '').not.toMatch(/border|outline/);
    expect(lacuna).toHaveAttribute('data-declared-gap', 'true');
  });

  it('NEGATIVO: a FONTE do componente não declara contorno nenhum', () => {
    // A VARREDURA, e ela existe porque o guarda de cima não basta.
    //
    // Renderizar sem escolher forma prova o CAMINHO PADRÃO. Não prova que não há
    // outro caminho: uma forma nova, opcional, com moldura, passaria por ele
    // intacta — e a spec proíbe que ela EXISTA, não que seja escolhida.
    //
    // A união de tipo não é varrível em tempo de execução (#89), mas texto de
    // fonte é. Mesma forma de `components/data/surfaceTokens.test.ts`: filtrar as
    // linhas infratoras e comparar com `[]`, para a falha NOMEAR a linha em vez de
    // dizer só "false !== true".
    const fonte = readFileSync(join(import.meta.dirname, 'DeclaredGap.tsx'), 'utf8');

    const infratores = fonte
      .split('\n')
      .map((linha, i) => ({ linha: linha.trim(), numero: i + 1 }))
      // Comentário não é código: este arquivo PRECISA poder contar que houve uma
      // moldura, e contar não é desenhar.
      .filter(({ linha }) => !/^(\/\/|\/\*|\*)/.test(linha))
      .filter(({ linha }) => /\b(border|borderRadius|outline)\s*:/.test(linha))
      .map(({ numero, linha }) => `DeclaredGap.tsx:${numero} → ${linha}`);

    expect(infratores).toEqual([]);
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
