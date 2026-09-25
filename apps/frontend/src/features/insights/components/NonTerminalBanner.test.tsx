import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { NonTerminalBanner } from './NonTerminalBanner';
import { errorsFixture } from '../test/systemInsightsFixture';
import type { ErrorInsights } from '../types/systemInsights';

function renderBanner(errors: ErrorInsights) {
  return render(
    <MantineProvider theme={theme}>
      <NonTerminalBanner errors={errors} />
    </MantineProvider>,
  );
}

const duasPopulacoes = errorsFixture({
  nonTerminal: {
    openExecutionCount: 1,
    neverConsumedCount: 5,
    observedStates: ['Submitted', 'Working'],
  },
});

describe('NonTerminalBanner', () => {
  it('as duas populações aparecem SEPARADAS, cada uma com a sua causa', () => {
    renderBanner(duasPopulacoes);

    const texto = screen.getByTestId('nao-terminais-populacoes').textContent ?? '';
    expect(texto).toContain('1 com execução aberta');
    expect(texto).toContain('5 nunca consumidas');
    // E cada uma diz a causa, que é o que somar apagaria.
    expect(texto).toContain('não chegaram a um estado final');
    expect(texto).toContain('nenhum worker chegou a pegá-las');
  });

  it('a forma é a do artboard: caixa discreta, sem lista de marcadores', () => {
    // O `Main.dc.html` desenha uma caixa de borda fina sobre a superfície
    // sutil, com o texto em prosa. A primeira versão era um `Alert` PREENCHIDO
    // do Mantine com lista de marcadores — peso de alarme para o que são
    // algumas tasks entre as do período.
    renderBanner(duasPopulacoes);

    const banner = screen.getByTestId('banner-nao-terminais');
    const estilo = banner.getAttribute('style') ?? '';
    expect(estilo).toContain('var(--buteco-surface-subtle)');
    expect(estilo).toContain('1px solid');
    // Prosa, não estrutura: o artboard separa as populações no TEXTO.
    expect(banner.querySelector('ul')).toBeNull();
    expect(banner.querySelector('li')).toBeNull();
  });

  it('NEGATIVO: as duas NÃO são somadas num número só', () => {
    renderBanner(duasPopulacoes);

    // O protótipo escreve "2 tasks". A soma apaga qual dos dois problemas
    // existe: execução aberta aponta para worker travado, nunca consumida para
    // o broker.
    expect(screen.getByTestId('banner-nao-terminais').textContent).not.toMatch(/\b6 tasks\b/);
  });

  it('declara que a contagem é DO INSTANTE da consulta', () => {
    renderBanner(duasPopulacoes);

    expect(screen.getByTestId('nao-terminais-caveat')).toHaveTextContent(
      /instante da consulta/i,
    );
  });

  it('NEGATIVO: NÃO afirma idade nem duração do estado', () => {
    // O protótipo escreve "há mais de 30 dias". A resposta não mede isso —
    // traz duas contagens e os estados observados, e nada sobre quando
    // entraram neles.
    renderBanner(duasPopulacoes);

    const texto = screen.getByTestId('banner-nao-terminais').textContent ?? '';
    expect(texto).not.toMatch(/há mais de/i);
    expect(texto).not.toMatch(/\d+\s*dias/i);
    expect(texto).not.toMatch(/horas|semanas|meses/i);
  });

  it('NEGATIVO: NÃO oferece link nem botão', () => {
    // O protótipo oferece "Ver as tasks". Não existe tela que liste exatamente
    // essas tasks; um link para uma listagem que não filtra por isso faria o
    // operador concluir que o aviso mentiu.
    renderBanner(duasPopulacoes);

    expect(screen.queryByRole('link')).toBeNull();
    expect(screen.queryByRole('button', { name: /ver as tasks/i })).toBeNull();
    expect(screen.getByTestId('banner-nao-terminais').textContent).not.toMatch(/ver as tasks/i);
  });

  it('os estados observados aparecem', () => {
    renderBanner(duasPopulacoes);

    expect(screen.getByTestId('nao-terminais-estados')).toHaveTextContent(
      'Estados observados: Submitted, Working.',
    );
  });

  it('DUAS CONTAGENS ZERO não renderizam banner nenhum', () => {
    renderBanner(errorsFixture());

    // Dois zeros medidos são boa notícia, e um banner permanente dizendo
    // "0 tasks travadas" vira ruído que ninguém lê.
    expect(screen.queryByTestId('banner-nao-terminais')).toBeNull();
    expect(screen.queryByRole('alert')).toBeNull();
    // E nenhuma das duas linhas escapa sozinha.
    expect(screen.queryByTestId('nao-terminais-populacoes')).toBeNull();
  });

  it('uma só população maior que zero já rende o banner, sem a outra linha', () => {
    renderBanner(
      errorsFixture({
        nonTerminal: { openExecutionCount: 0, neverConsumedCount: 3, observedStates: ['Submitted'] },
      }),
    );

    expect(screen.getByTestId('nao-terminais-populacoes')).toBeInTheDocument();
    // A população zerada não vira uma linha dizendo "0 com execução aberta":
    // ela não tem o que avisar.
    expect(screen.queryByTestId('nao-terminais-execucao-aberta')).toBeNull();
  });
});
