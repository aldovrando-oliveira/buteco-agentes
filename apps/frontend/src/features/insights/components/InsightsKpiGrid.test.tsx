import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { InsightsKpiGrid } from './InsightsKpiGrid';
import {
  systemInsightsFixture,
  tokensFixture,
  volumeFixture,
  performanceFixture,
} from '../test/systemInsightsFixture';
import type { SystemInsights } from '../types/systemInsights';
import { METRIC_SIZE, type QueryState } from '../utils/metricState';

function renderGrid(insights: SystemInsights, queryState: QueryState = 'ok') {
  render(
    <MantineProvider theme={theme}>
      <InsightsKpiGrid
        insights={insights}
        queryState={queryState}
        reason="A consulta não respondeu."
      />
    </MantineProvider>,
  );
}

const modelos = [
  { provider: 'anthropic', model: 'claude-opus-5', totalTokens: 5_000_000, callCount: 312 },
  { provider: 'openai', model: 'gpt-5.6-sol', totalTokens: 3_200_000, callCount: 251 },
];

describe('InsightsKpiGrid — cada card com a sua fonte', () => {
  it('tasks executadas vem de volume', () => {
    renderGrid(systemInsightsFixture({ volume: volumeFixture({ executedTaskCount: 476 }) }));

    expect(screen.getByTestId('kpi-tasks-valor')).toHaveTextContent('476');
  });

  it('o total de chamadas é SOMADO das linhas de modelo', () => {
    // L2: a fonte é `sum(byModel[].callCount)`, e não
    // `providerCallDuration.sampleCount`, que daria o mesmo número hoje mas por
    // contrato é uma AMOSTRA.
    renderGrid(systemInsightsFixture({ tokens: tokensFixture({ byModel: modelos }) }));

    expect(screen.getByTestId('kpi-chamadas-valor')).toHaveTextContent('563');
  });

  it('sem nenhum modelo, o total de chamadas é ZERO MEDIDO', () => {
    renderGrid(systemInsightsFixture());

    const valor = screen.getByTestId('kpi-chamadas-valor');
    expect(valor).toHaveAttribute('data-metric-state', 'zero');
    expect(valor).toHaveTextContent('0');
  });

  it('a lacuna de turno × compactação está presente, como SUBTÍTULO', () => {
    renderGrid(systemInsightsFixture({ tokens: tokensFixture({ byModel: modelos }) }));

    const lacuna = screen.getByTestId('kpi-chamadas-lacuna');
    expect(lacuna).toHaveTextContent(/turno e compactação/i);
    expect(lacuna).toHaveAttribute('data-declared-gap', 'true');

    // O protótipo tem ali uma LINHA DE SUBTÍTULO — "522 de turno · 41 de
    // compactação" —, do mesmo peso das dos outros cinco cards, e a D8 diz que
    // é o subtítulo que vira lacuna. A moldura tracejada do quadro 6 é para
    // lacuna que substitui elemento próprio, no rodapé do card, e ali ela
    // ficava maior que o próprio número (pego pelo dono na conferência manual).
    expect(lacuna).toHaveAttribute('data-gap-variant', 'inline');
    expect(lacuna.getAttribute('style') ?? '').not.toContain('dashed');
  });

  it('a lacuna do KPI ocupa UMA linha, como o subtítulo que ela substitui', () => {
    // O protótipo tem ali UMA linha — "522 de turno · 41 de compactação" —, e a
    // D8 diz "o subtítulo vira a lacuna declarada": o subtítulo, singular.
    //
    // A versão anterior tinha duas, porque `reason` era obrigatória no
    // componente, e o card ficava com quatro linhas onde o protótipo tem três.
    // O PORQUÊ pertence à issue #66, não à tela: a convenção 23 existe para que
    // ele sobreviva ao archive, e a tela só precisa NOMEAR o que falta.
    renderGrid(systemInsightsFixture({ tokens: tokensFixture({ byModel: modelos }) }));

    const lacuna = screen.getByTestId('kpi-chamadas-lacuna');
    expect(lacuna.querySelectorAll('p')).toHaveLength(1);
    expect(lacuna).toHaveTextContent('turno e compactação — não disponível');

    // E o card inteiro fica com as três linhas do protótipo: rótulo, número,
    // subtítulo.
    const card = screen.getByTestId('kpi-chamadas');
    expect(card.querySelectorAll('p')).toHaveLength(3);
  });

  it('o card de chamadas tem o MESMO número de linhas do card ao lado', () => {
    // A régua que o caso acima exerce, dita de outro jeito: a lacuna não pode
    // engordar o card. "Tasks executadas" tem rótulo, número e subtítulo; o de
    // chamadas tem os mesmos três, com o subtítulo declarando o que falta.
    renderGrid(systemInsightsFixture({ tokens: tokensFixture({ byModel: modelos }) }));

    const tasks = screen.getByTestId('kpi-tasks').querySelectorAll('p').length;
    const chamadas = screen.getByTestId('kpi-chamadas').querySelectorAll('p').length;
    expect(chamadas).toBe(tasks);
  });

  it('NEGATIVO: a lacuna do KPI não afirma nada sobre COLETA', () => {
    // O guarda da contradição, que sobreviveu a duas mudanças de texto porque
    // o que ele afirma não é a redação: é que a tela NÃO OPINA sobre coleta.
    //
    // A primeira versão dizia "não coletado" e a linha seguinte dizia "embora
    // ele seja gravado" — duas frases opostas no mesmo quadro.
    // `ProviderCall.Purpose` EXISTE na tabela e é escrito por apps/workers; o
    // que falta é ele chegar aqui. Numa tela cujo ponto é não afirmar o que o
    // sistema não sabe, afirmar que ele não sabe o que sabe é o mesmo erro com
    // o sinal trocado — e pior, porque manda procurar coleta onde não falta.
    renderGrid(systemInsightsFixture({ tokens: tokensFixture({ byModel: modelos }) }));

    const texto = screen.getByTestId('kpi-chamadas-lacuna').textContent ?? '';
    expect(texto).toContain('não disponível');
    expect(texto).not.toMatch(/coletad[ao]|gravad[ao]|registrad[ao]/i);
  });

  it('NEGATIVO: a lacuna não usa vocabulário de servidor nem de nova tentativa', () => {
    // "não servido por esta rota" era a redação anterior: vocabulário de quem
    // escreve o backend numa tela lida por quem OPERA, e que ainda insinuava
    // existir outra rota que serviria — falso para esta lacuna.
    //
    // E o risco da redação atual, declarado quando ela foi escolhida:
    // "disponível" é vizinho de "tente de novo", que é o que o TRAVESSÃO
    // significa. Contido pela marca, pela ausência de ação ao lado, e por este
    // guarda.
    renderGrid(systemInsightsFixture({ tokens: tokensFixture({ byModel: modelos }) }));

    const texto = screen.getByTestId('kpi-chamadas-lacuna').textContent ?? '';
    expect(texto).not.toMatch(/rota|endpoint|servid[ao]|API/i);
    expect(texto).not.toMatch(/falhou|erro|tentar de novo|recarregar/i);
  });

  // --------------------------------------------- L5: A PALAVRA É O CONTRATO

  it('o card de duração diz MÉDIA, e a palavra "mediana" NÃO aparece', () => {
    renderGrid(
      systemInsightsFixture({
        performance: performanceFixture({
          taskDuration: { averageMs: 4100, p95Ms: 12_400, sampleCount: 460 },
        }),
      }),
    );

    expect(screen.getByTestId('kpi-duracao-sub')).toHaveTextContent('média 4,1 s');
    // O guarda negativo: o protótipo escreve "mediana 4,1 s", e a rota não
    // serve mediana. A palavra errada afirmaria que metade das tasks foi mais
    // rápida — o que uma média com cauda longa não diz.
    expect(screen.getByTestId('kpi-duracao').textContent).not.toMatch(/mediana/i);
  });

  it('o caveat da duração aparece junto do número que ele limita', () => {
    renderGrid(systemInsightsFixture());

    expect(screen.getByTestId('kpi-duracao-caveat')).toHaveTextContent(/reentregues/i);
    // E dentro do card da duração, não numa lista solta.
    expect(screen.getByTestId('kpi-duracao')).toContainElement(
      screen.getByTestId('kpi-duracao-caveat'),
    );
  });

  // -------------------------------------------------- OS GUARDAS NEGATIVOS

  it('NEGATIVO: tokens de conversa com UMA parcela nula fica vazio? não — soma o conhecido', () => {
    renderGrid(
      systemInsightsFixture({
        tokens: tokensFixture({
          conversation: { inputTokens: 9_600_000, outputTokens: null, cachedInputTokens: null },
        }),
      }),
    );

    // Soma só o que se sabe: 9,6 M, e não 9,6 M + 0.
    expect(screen.getByTestId('kpi-tokens-valor')).toHaveTextContent('9,6 M');
    expect(screen.getByTestId('kpi-tokens-valor')).toHaveAttribute('data-metric-state', 'value');
  });

  it('NEGATIVO: tokens de conversa com TODAS as parcelas nulas fica vazio, e não 0', () => {
    renderGrid(systemInsightsFixture());

    const valor = screen.getByTestId('kpi-tokens-valor');
    expect(valor).toHaveAttribute('data-metric-state', 'empty');
    expect(valor.textContent).not.toContain('0');
  });

  it('NEGATIVO: o subtítulo com parcela nula não escreve 0 nem travessão', () => {
    renderGrid(
      systemInsightsFixture({
        tokens: tokensFixture({
          conversation: { inputTokens: 9_600_000, outputTokens: null, cachedInputTokens: null },
        }),
      }),
    );

    const sub = screen.getByTestId('kpi-tokens-sub').textContent ?? '';
    expect(sub).toContain('9,6 M entrada');
    // A saída é nula: a posição fica VAZIA. Nem "0", nem "—" — o travessão é
    // "não sei", e aqui a consulta respondeu dizendo que não há o que relatar.
    expect(sub).not.toContain('0 saída');
    expect(sub).not.toContain('— saída');
  });

  it('NEGATIVO: embedding nulo não vira 0', () => {
    renderGrid(systemInsightsFixture());

    const valor = screen.getByTestId('kpi-embedding-valor');
    expect(valor).toHaveAttribute('data-metric-state', 'empty');
    expect(valor.textContent).not.toContain('0');
  });

  it('consulta sem resposta põe travessão em TODOS os cards, e nenhum 0', () => {
    renderGrid(systemInsightsFixture({ volume: volumeFixture({ executedTaskCount: 476 }) }), 'failed');

    for (const id of [
      'kpi-tasks-valor',
      'kpi-chamadas-valor',
      'kpi-duracao-valor',
      'kpi-tokens-valor',
      'kpi-embedding-valor',
      'kpi-tokens-por-task-valor',
    ]) {
      expect(screen.getByTestId(id)).toHaveAttribute('data-metric-state', 'unknown');
      expect(screen.getByTestId(id)).toHaveTextContent('—');
    }

    // Nem o 476 que a fixture traz, nem um 0 no lugar dele: a consulta não
    // respondeu, e o que estiver no objeto é resíduo.
    expect(screen.getByTestId('kpis').textContent).not.toContain('476');
  });

  it('zero medido em tasks executadas aparece como 0, escrito', () => {
    renderGrid(systemInsightsFixture());

    const valor = screen.getByTestId('kpi-tasks-valor');
    expect(valor).toHaveAttribute('data-metric-state', 'zero');
    expect(valor).toHaveTextContent('0');
  });

  it('os números saem no tamanho MEDIDO no artboard, não no topo da escala', () => {
    // A escala do tema para em `xl: 16px` — ela foi feita para TEXTO, num
    // painel de corpo 13px. Os números de métrica são 22px no `Main.dc.html`,
    // e usar `xl` os entregava a 73% do desenhado.
    //
    // Nenhum teste podia pegar antes: jsdom não faz layout, e `size="xl"` é
    // valor válido. O que estava errado era a ESCOLHA do token. Este caso
    // afirma a medida, que é o que o artboard declara.
    renderGrid(systemInsightsFixture({ volume: volumeFixture({ executedTaskCount: 476 }) }));

    expect(METRIC_SIZE.kpi).toBe('22px');
    for (const id of [
      'kpi-tasks-valor',
      'kpi-chamadas-valor',
      'kpi-duracao-valor',
      'kpi-tokens-valor',
      'kpi-embedding-valor',
      'kpi-tokens-por-task-valor',
    ]) {
      // O Mantine escreve o tamanho em `--text-fz`, convertido para rem — a
      // regra `font-size: var(--text-fz)` vive na folha de estilo, que o jsdom
      // não carrega. 1.375rem = 22px.
      expect(screen.getByTestId(id).getAttribute('style') ?? '').toContain('1.375rem');
    }
  });

  it('NEGATIVO: os números não herdam o topo da escala de texto', () => {
    renderGrid(systemInsightsFixture({ volume: volumeFixture({ executedTaskCount: 476 }) }));

    // 16px é `xl`, o maior token de TEXTO do tema — e menor que qualquer
    // número de métrica do artboard.
    // Quando o tamanho vem de um token da escala, o Mantine escreve
    // `var(--mantine-font-size-*)`. Um valor medido em px nunca passa por lá —
    // e é essa a diferença que este caso guarda.
    expect(screen.getByTestId('kpi-tasks-valor').getAttribute('style') ?? '').not.toContain(
      'var(--mantine-font-size',
    );
  });

  it('os seis cards do protótipo estão presentes, na ordem do artboard', () => {
    renderGrid(systemInsightsFixture());

    const rotulos = [
      'Tasks executadas',
      'Chamadas ao provedor',
      'Duração da task (p95)',
      'Tokens de conversa',
      'Tokens de embedding',
      'Tokens por task',
    ];
    const texto = screen.getByTestId('kpis').textContent ?? '';
    let posicao = -1;
    for (const rotulo of rotulos) {
      const proxima = texto.indexOf(rotulo);
      expect(proxima).toBeGreaterThan(posicao);
      posicao = proxima;
    }
  });
});
