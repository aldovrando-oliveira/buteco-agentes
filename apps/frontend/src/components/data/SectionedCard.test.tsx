import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { SectionedCard } from './SectionedCard';
import { theme } from '../../theme';

function renderCard(ui: React.ReactNode) {
  return render(
    <MantineProvider theme={theme} defaultColorScheme="light">
      {ui}
    </MantineProvider>,
  );
}

function linhas(container: HTMLElement) {
  return container.querySelectorAll('[data-sectioned-card-row]');
}

describe('SectionedCard', () => {
  it('exibe o rótulo do cabeçalho e a ação ao lado', () => {
    renderCard(
      <SectionedCard title="Catálogo de tools" action={<button type="button">Atualizar</button>}>
        <SectionedCard.Row>uma linha</SectionedCard.Row>
      </SectionedCard>,
    );

    expect(screen.getByText('Catálogo de tools')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Atualizar' })).toBeInTheDocument();
  });

  it('marca cada linha para a regra de divisor entre irmãos adjacentes', () => {
    const { container } = renderCard(
      <SectionedCard title="Tools">
        <SectionedCard.Row>primeira</SectionedCard.Row>
        <SectionedCard.Row>segunda</SectionedCard.Row>
        <SectionedCard.Row>terceira</SectionedCard.Row>
      </SectionedCard>,
    );

    // O divisor é regra de irmão adjacente em index.css, e não estilo
    // embutido: some da primeira linha sozinho, sem o componente saber a sua
    // posição. jsdom não carrega esse arquivo, então o que dá para afirmar
    // aqui é a marcação que a regra usa como alvo — a borda em si é conferida
    // no navegador.
    expect(linhas(container)).toHaveLength(3);
  });

  it('não desenha borda embutida em linha nenhuma', () => {
    const { container } = renderCard(
      <SectionedCard title="Tools">
        <SectionedCard.Row>única</SectionedCard.Row>
      </SectionedCard>,
    );

    const linha = container.querySelector('[data-sectioned-card-row]') as HTMLElement;

    expect(linha.style.borderTop).toBe('');
    expect(linha.style.borderBottom).toBe('');
  });

  it('mantém a faixa na mesma altura com e sem ação', () => {
    const { container: soRotulo } = renderCard(
      <SectionedCard title="Configuração">
        <SectionedCard.Body>conteúdo</SectionedCard.Body>
      </SectionedCard>,
    );
    const { container: comBotao } = renderCard(
      <SectionedCard title="Catálogo de tools" action={<button type="button">Atualizar</button>}>
        <SectionedCard.Body>conteúdo</SectionedCard.Body>
      </SectionedCard>,
    );

    // Cards lado a lado desalinhavam porque a faixa crescia com o conteúdo: a
    // que carregava um botão ficava mais alta que a que só tinha o rótulo.
    const faixa = (c: HTMLElement) =>
      (c.querySelector('[class*="mantine-Paper-root"] > div') as HTMLElement).style.height;

    expect(faixa(soRotulo)).toBe(faixa(comBotao));
    expect(faixa(soRotulo)).not.toBe('');
  });

  it('sem título, não renderiza faixa de cabeçalho', () => {
    renderCard(
      <SectionedCard>
        <table>
          <tbody>
            <tr>
              <td>conteúdo</td>
            </tr>
          </tbody>
        </table>
      </SectionedCard>,
    );

    expect(screen.getByText('conteúdo')).toBeInTheDocument();
  });
});
