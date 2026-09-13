import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { Logo } from './Logo';
import { theme } from '../../theme';

// A peça renderizada é identificada pelo viewBox, que é o que distingue os três
// arquivos: a cabeça sozinha é 240×224, o símbolo com gravata é 240×320.
const MARK_VIEWBOX = '136 96 240 224';
const SYMBOL_VIEWBOX = '136 96 240 320';
const LOCKUP_VERTICAL_VIEWBOX = '0 0 360 200';

function renderLogo(ui: React.ReactNode) {
  const { container } = render(
    <MantineProvider theme={theme} defaultColorScheme="light">
      {ui}
    </MantineProvider>,
  );

  const svg = container.querySelector('svg');
  if (!svg) throw new Error('o Logo não renderizou nenhum svg');
  return svg;
}

describe('Logo — guarda de legibilidade por tamanho', () => {
  it('respeita o duotone pedido a partir do limiar de 32px', () => {
    const svg = renderLogo(<Logo variant="symbol" size={40} />);

    expect(svg.getAttribute('viewBox')).toBe(SYMBOL_VIEWBOX);
    // As duas cores construindo forma: a tinta virou currentColor e o acento
    // virou o token do tema, pela reescrita do vite.config.ts (design.md, D2).
    const fills = [...svg.querySelectorAll('path')].map((path) => path.getAttribute('fill'));
    expect(fills).toContain('currentColor');
    expect(fills).toContain('var(--mantine-primary-color-filled)');
  });

  it('derruba o duotone para cor única abaixo de 32px, sem exigir nada da chamada', () => {
    const svg = renderLogo(<Logo variant="symbol" size={28} />);

    // A peça continua sendo o símbolo — a gravata não sai —, mas as duas cores
    // dão lugar a uma só, porque a 28px a gravata não constrói forma.
    expect(svg.getAttribute('viewBox')).toBe(SYMBOL_VIEWBOX);
    const fills = new Set([...svg.querySelectorAll('path')].map((p) => p.getAttribute('fill')));
    expect(fills.size).toBe(1);
    expect(fills.has('var(--mantine-primary-color-filled)')).toBe(true);
  });

  it('cai para a cabeça sozinha abaixo de 24px, mesmo com o símbolo pedido', () => {
    const svg = renderLogo(<Logo variant="symbol" size={16} />);

    expect(svg.getAttribute('viewBox')).toBe(MARK_VIEWBOX);
  });

  it('usa a cabeça como padrão, no tamanho do topo da barra lateral', () => {
    const svg = renderLogo(<Logo />);

    expect(svg.getAttribute('viewBox')).toBe(MARK_VIEWBOX);
    expect(svg.getAttribute('height')).toBe('24');
    // Sem largura fixa: a proporção vem do viewBox, senão a peça distorce.
    expect(svg.getAttribute('width')).toBeNull();
  });
});

describe('Logo — lockup vertical', () => {
  it('desenha o nome do produto junto do símbolo', () => {
    const svg = renderLogo(<Logo variant="vertical" size={110} />);

    expect(svg.getAttribute('viewBox')).toBe(LOCKUP_VERTICAL_VIEWBOX);
    expect(svg.querySelector('text')?.textContent).toBe('Buteco Agentes');
  });

  it('pinta o nome na tinta e a gravata no acento, ambos por token', () => {
    const svg = renderLogo(<Logo variant="vertical" size={110} />);

    // O <text> do lockup entra com fill #191a1c no arquivo e sai como
    // currentColor: o nome desenhado troca de cor junto com o tema, igual ao
    // resto da marca (design.md, D2 e D11).
    expect(svg.querySelector('text')?.getAttribute('fill')).toBe('currentColor');
    const fills = [...svg.querySelectorAll('path')].map((path) => path.getAttribute('fill'));
    expect(fills).toContain('currentColor');
    expect(fills).toContain('var(--mantine-primary-color-filled)');
  });

  it('pede a fonte da identidade para o nome desenhado', () => {
    const svg = renderLogo(<Logo variant="vertical" size={110} />);

    // Só resolve porque o SVG é inlineado no DOM e alcança as @font-face de
    // src/fonts.ts, que carrega o peso 600. Via <img> cairia na fonte do
    // sistema (design.md, D1 e D11).
    expect(svg.querySelector('text')?.getAttribute('font-family')).toContain('IBM Plex Sans');
    expect(svg.querySelector('text')?.getAttribute('font-weight')).toBe('600');
  });

  it('dá lugar ao símbolo quando fica pequeno demais para o duotone', () => {
    // A 48px o símbolo dentro do lockup teria 28,8px e a gravata quebraria.
    // Não há lockup de cor única que leia os tokens do tema — os `*-mono-*` do
    // pacote têm #000 e #fff cravados —, então a peça inteira dá lugar ao
    // símbolo, que tem a cadeia de degradação completa.
    const svg = renderLogo(<Logo variant="vertical" size={48} />);

    expect(svg.getAttribute('viewBox')).toBe(SYMBOL_VIEWBOX);
    expect(svg.querySelector('text')).toBeNull();
  });

  it('mantém o lockup no limiar de 54px', () => {
    const svg = renderLogo(<Logo variant="vertical" size={54} />);

    expect(svg.getAttribute('viewBox')).toBe(LOCKUP_VERTICAL_VIEWBOX);
  });
});

describe('Logo — acessibilidade', () => {
  it.each(['symbol', 'vertical'] as const)(
    'a variante %s fica fora da árvore de acessibilidade',
    (variant) => {
      // Vale inclusive para o lockup, que desenha o nome: quem exibe a marca é
      // que fornece o nome acessível, senão o leitor de tela anuncia duas
      // vezes (design.md, D9 e D11).
      const svg = renderLogo(<Logo variant={variant} size={110} />);

      expect(svg.getAttribute('aria-hidden')).toBe('true');
      expect(screen.queryByRole('img')).toBeNull();
      expect(screen.queryByLabelText('Buteco Agentes')).toBeNull();
    },
  );

  it('neutraliza o role e o rótulo que vêm no arquivo de marca', () => {
    const svg = renderLogo(<Logo variant="symbol" size={40} />);

    // Os arquivos de marca vêm com role="img" e aria-label="Buteco Agentes".
    // Ao lado do nome do produto em texto, isso faria o leitor de tela anunciar
    // o nome duas vezes (design.md, D9).
    expect(svg.getAttribute('aria-hidden')).toBe('true');
    expect(svg.getAttribute('role')).toBeNull();
    expect(svg.getAttribute('aria-label')).toBeNull();
    expect(screen.queryByRole('img')).toBeNull();
    expect(screen.queryByLabelText('Buteco Agentes')).toBeNull();
  });

  it('lê a tinta da variável de marca, e não de uma cor cravada', () => {
    const svg = renderLogo(<Logo size={24} />);

    expect(svg.getAttribute('style')).toContain('var(--buteco-brand-ink)');
  });

  it('desenha a cabeça na tinta, e não no acento', () => {
    const svg = renderLogo(<Logo size={24} />);

    // A peça do `mark` é a de tinta (favicon-tinta.svg), não a de acento: no
    // símbolo completo a cabeça é tinta e só a gravata é acento. Se a cabeça
    // viesse da peça de acento, o fill seria o token de destaque e a marca
    // ficaria azul nos dois esquemas, ignorando o --buteco-brand-ink acima
    // (design.md, D10 — regressão observada na conferência visual do escuro).
    const fills = [...svg.querySelectorAll('path')].map((path) => path.getAttribute('fill'));
    expect(fills).toEqual(['currentColor']);
    expect(fills).not.toContain('var(--mantine-primary-color-filled)');
  });
});
