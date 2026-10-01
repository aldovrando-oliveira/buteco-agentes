import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { FailuresCard } from './FailuresCard';
import { errorsFixture } from '../test/systemInsightsFixture';
import type { ErrorInsights } from '../types/systemInsights';
import type { QueryState } from '../utils/metricState';
import {
  KNOWN_REJECTION_REASONS,
  rejectionReasonLabel,
} from '../utils/rejectionReasonLabels';

function renderCard(
  errors: ErrorInsights,
  executedTaskCount: number,
  queryState: QueryState = 'ok',
  // `null` É COMO O CASO DIZ "SEM NOTA", e não `undefined`.
  //
  // A primeira versão era `rejectionRegimeNote?: string = '…'` e o caso passava
  // `undefined` — que é exactamente o valor que dispara o default do parâmetro. O
  // guarda "sem regime declarado, nenhuma nota aparece" reprovou por isso, e a
  // causa não era o componente. É a mesma família do guarda que não discrimina:
  // o arranjo não montava o estado que o caso diz montar.
  rejectionRegimeNote: string | null = 'recusa medida desde 26/09/2026',
) {
  render(
    <MantineProvider theme={theme}>
      <FailuresCard
        errors={errors}
        executedTaskCount={executedTaskCount}
        queryState={queryState}
        reason="A consulta não respondeu."
        rejectionRegimeNote={rejectionRegimeNote ?? undefined}
      />
    </MantineProvider>,
  );
}

// OS RÓTULOS DE OPERADOR DOS QUATRO MOTIVOS, para o guarda da frase.
//
// Importados do módulo em vez de reescritos: uma cópia literal aqui passaria a
// divergir no dia em que um rótulo mudasse, e o guarda deixaria de cobrir o que
// diz cobrir.
const CAUSAS = KNOWN_REJECTION_REASONS.map((r) => rejectionReasonLabel(r).text.toLowerCase());

describe('FailuresCard', () => {
  it('as duas contagens aparecem como números distintos', () => {
    // A recusa aqui é a DE ENTRADA desde a #75 — ver o caso próprio dela abaixo.
    renderCard(errorsFixture({ failedCount: 12, rejectedAtEntryCount: 5 }), 476);

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
    renderCard(errorsFixture({ failedCount: 12, rejectedAtEntryCount: 5 }), 476);

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
    renderCard(errorsFixture({ failedCount: 12, rejectedAtEntryCount: 5 }), 476);

    for (const id of ['falhas-contagem', 'recusas-contagem']) {
      // 1.25rem = 20px; `--text-fz` é onde o Mantine escreve o tamanho.
      expect(screen.getByTestId(id).getAttribute('style') ?? '').toContain('1.25rem');
    }
  });

  it('as cores separam os dois problemas, como no artboard', () => {
    // Falha em `red`, recusa em `yellow`. Não é decoração: é a mesma distinção
    // que o texto abaixo explica, dita em cor — e por isso as duas contagens
    // não podem sair da mesma cor.
    renderCard(errorsFixture({ failedCount: 12, rejectedAtEntryCount: 5 }), 476);

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
    renderCard(errorsFixture({ failedCount: 12, rejectedAtEntryCount: 5 }), 476, 'failed');

    expect(screen.getByTestId('falhas-contagem')).not.toHaveStyle({
      color: 'var(--mantine-color-red-filled)',
    });
  });

  it('a diferença entre os dois é declarada em texto, e as duas últimas orações são literais do protótipo', () => {
    // O TÍTULO PERDEU O "literal do protótipo" INTEIRO, e isso é o registro.
    //
    // A primeira oração do artboard — "Recusa é agente inativo ou sem provider e
    // modelo configurados" — enumerava DUAS das quatro causas, e a #51 tornou a
    // enumeração falsa. Ela é a única parte que diverge; as outras duas continuam
    // literais, e o caso as afirma palavra por palavra para que a divergência não
    // cresça sem ninguém ver.
    renderCard(errorsFixture({ failedCount: 12, rejectedAtEntryCount: 5 }), 476);

    const frase = screen.getByTestId('nota-falha-versus-recusa');
    expect(frase).toHaveTextContent(
      'Falha é execução que começou e quebrou. Somar os dois esconde qual dos dois problemas existe.',
    );
    // E a primeira oração diz o que separa as duas populações, sem enumerar.
    expect(frase).toHaveTextContent('nunca chega a processar');
  });

  it('a FALHA tem percentual', () => {
    renderCard(errorsFixture({ failedCount: 12, rejectedAtEntryCount: 5 }), 476);

    expect(screen.getByTestId('falhas-percentual')).toHaveTextContent('2,5% das tasks');
  });

  it('NEGATIVO: a RECUSA não tem percentual, e traz o caveat no lugar', () => {
    // O protótipo escreve "1,1% das tasks" para a recusa. Ela não produz linha
    // de execução, então subconta o denominador — o percentual seria sobre um
    // total que não inclui as próprias recusas.
    renderCard(errorsFixture({ failedCount: 12, rejectedAtEntryCount: 5 }), 476);

    const bloco = screen.getByTestId('bloco-recusas');
    expect(bloco.textContent).not.toMatch(/%/);
    expect(bloco.textContent).not.toContain('1,1');
    expect(screen.getByTestId('recusas-caveat')).toHaveTextContent(
      /não geram linha de execução/i,
    );
  });

  it('o caveat da recusa fica DENTRO do bloco da recusa', () => {
    renderCard(errorsFixture({ failedCount: 12, rejectedAtEntryCount: 5 }), 476);

    // Junto do número que ele limita, e não numa lista ao pé da página.
    expect(screen.getByTestId('bloco-recusas')).toContainElement(
      screen.getByTestId('recusas-caveat'),
    );
  });

  it('NEGATIVO: sem tasks executadas, NENHUM percentual e NENHUM NaN', () => {
    renderCard(errorsFixture({ failedCount: 0, rejectedAtEntryCount: 0 }), 0);

    expect(screen.queryByTestId('falhas-percentual')).toBeNull();
    const texto = screen.getByTestId('card-falhas').textContent ?? '';
    expect(texto).not.toContain('NaN');
    expect(texto).not.toContain('%');
    expect(texto).not.toContain('Infinity');
  });

  it('zero medido nas duas contagens aparece como 0', () => {
    renderCard(errorsFixture({ failedCount: 0, rejectedAtEntryCount: 0 }), 476);

    expect(screen.getByTestId('falhas-contagem')).toHaveAttribute('data-metric-state', 'zero');
    expect(screen.getByTestId('recusas-contagem')).toHaveAttribute('data-metric-state', 'zero');
  });

  it('zero falhas sobre tasks medidas produz 0% — que é um percentual medido', () => {
    renderCard(errorsFixture({ failedCount: 0, rejectedAtEntryCount: 0 }), 476);

    expect(screen.getByTestId('falhas-percentual')).toHaveTextContent('0% das tasks');
  });

  // ------------------------------------------- AS DUAS POPULAÇÕES DE RECUSA (#75)

  it('a contagem apresentada é a da recusa DE ENTRADA', () => {
    // OS DOIS VALORES SÃO DIFERENTES DE PROPÓSITO. Um caso que passe os dois com
    // o MESMO número passa igual com o campo certo e com o errado — é a convenção
    // 11 na forma mais barata de acontecer, e é justamente o que os 13 casos
    // acima faziam antes desta change (todos com `rejectedCount: 5` e o de
    // entrada no default 0).
    renderCard(errorsFixture({ failedCount: 12, rejectedCount: 333, rejectedAtEntryCount: 9 }), 476);

    expect(screen.getByTestId('recusas-contagem')).toHaveTextContent('9');
  });

  it('NEGATIVO: o valor da recusa COM linha de execução não aparece na tela', () => {
    // Ela não sai de cena por descuido: o `Main.dc.html` não tem elemento para
    // ela — não há KPI de taxa de falha nesta página —, e o rótulo "Recusadas na
    // entrada" que ela ocupava é da outra população. *Servido e não desenhado*,
    // com issue e gatilho (design.md D3).
    //
    // O VALOR É 333, E A PRIMEIRA ESCOLHA ESTAVA ERRADA. Era 7777, e o guarda
    // PASSOU contra o defeito presente: `formatCount` é `pt-BR`, então 7777 sai na
    // tela como "7.777" e `toContain('7777')` não casa. Guarda não verificado é
    // pior que nenhum — convenção 15, e esta é a forma "a asserção não cobre a
    // forma do caso real", a mesma do varredor de tons dentro de ternário.
    //
    // 333 não é formatado com separador e não colide com dígito de nenhum outro
    // número do card (12, 9, 476, 2,5%).
    renderCard(errorsFixture({ failedCount: 12, rejectedCount: 333, rejectedAtEntryCount: 9 }), 476);

    expect(screen.getByTestId('card-falhas').textContent).not.toContain('333');
  });

  it('o regime da recusa é declarado DENTRO do quadro da recusa', () => {
    // O regime dela difere do que governa a página, e a régua já escrita em
    // `caveatLabels.ts` vale igual para regime: o texto vive junto do número que
    // ele qualifica, não no cabeçalho do card, que qualificaria os dois quadros.
    renderCard(errorsFixture({ failedCount: 12, rejectedCount: 0, rejectedAtEntryCount: 9 }), 476);

    const nota = screen.getByTestId('nota-de-regime-rejection');
    expect(nota).toHaveTextContent('recusa medida desde 26/09/2026');
    expect(screen.getByTestId('bloco-recusas')).toContainElement(nota);
  });

  it('sem regime declarado para a recusa, nenhuma nota aparece', () => {
    // O regime que a resposta não declara não vira texto nenhum — é a mesma regra
    // dos outros cards, e impede uma nota vazia ocupando o lugar do subtítulo.
    renderCard(
      errorsFixture({ failedCount: 12, rejectedAtEntryCount: 9 }),
      476,
      'ok',
      null,
    );

    expect(screen.queryByTestId('nota-de-regime-rejection')).toBeNull();
  });

  it('NEGATIVO: os dois números não aparecem SOMADOS em lugar nenhum', () => {
    // 12 + 9 = 21, e o texto do próprio protótipo diz por que 21 não existe:
    // "somar os dois esconde qual dos dois problemas existe".
    renderCard(errorsFixture({ failedCount: 12, rejectedCount: 0, rejectedAtEntryCount: 9 }), 476);

    expect(screen.getByTestId('card-falhas').textContent).not.toContain('21');
  });

  it('NEGATIVO: a frase não enumera um SUBCONJUNTO das quatro causas', () => {
    // O literal do protótipo é "Recusa é agente inativo ou sem provider e modelo
    // configurados", e são DUAS das quatro — faltam `ProviderNotConfigured` e
    // `AgentNotFound`. Era verdade quando foi escrito, e a #51 tornou falso:
    // convenção 13 na forma "verdadeira quando escrita, e outra etapa tornou
    // falsa".
    //
    // A ASSERÇÃO É ESTRUTURAL, E É ISSO QUE A TORNA DURÁVEL: ou a frase enumera
    // as quatro, ou não enumera nenhuma. Um `not.toMatch(/agente inativo/)` fixaria
    // a escolha de hoje e passaria a mentir se amanhã se decidisse listar as
    // quatro; este reprova contra qualquer subconjunto próprio, que é o defeito.
    renderCard(errorsFixture({ failedCount: 12, rejectedAtEntryCount: 9 }), 476);

    const frase = (screen.getByTestId('nota-falha-versus-recusa').textContent ?? '').toLowerCase();
    const enumeradas = CAUSAS.filter((causa) => frase.includes(causa));

    expect(enumeradas.length === 0 || enumeradas.length === CAUSAS.length).toBe(true);
  });

  it('consulta sem resposta põe travessão nas duas, e nenhum percentual', () => {
    renderCard(errorsFixture({ failedCount: 12, rejectedAtEntryCount: 5 }), 476, 'failed');

    expect(screen.getByTestId('falhas-contagem')).toHaveAttribute('data-metric-state', 'unknown');
    expect(screen.getByTestId('recusas-contagem')).toHaveAttribute('data-metric-state', 'unknown');
    expect(screen.queryByTestId('falhas-percentual')).toBeNull();
    expect(screen.getByTestId('card-falhas').textContent).not.toContain('12');
  });
});
