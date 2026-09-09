import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MantineProvider } from '@mantine/core';
import { MemoryRouter } from 'react-router';
import { DetailHeader } from './DetailHeader';
import { theme } from '../../theme';

function renderHeader(props: Partial<Parameters<typeof DetailHeader>[0]> = {}) {
  return render(
    <MantineProvider theme={theme} defaultColorScheme="light">
      <MemoryRouter initialEntries={['/mcp-servers/abc-123']}>
        <DetailHeader
          backTo="/mcp-servers"
          backLabel="Servidores MCP"
          title="Servidor Financeiro"
          {...props}
        />
      </MemoryRouter>
    </MantineProvider>,
  );
}

describe('DetailHeader', () => {
  it('leva de volta para a listagem, nomeando-a', () => {
    renderHeader();

    expect(screen.getByRole('link', { name: 'Servidores MCP' })).toHaveAttribute(
      'href',
      '/mcp-servers',
    );
  });

  it('volta por rota fixa, funcionando em acesso direto pela URL', () => {
    // A entrada inicial do histórico é a própria página de detalhe: não há
    // para onde "voltar" no histórico, e o link precisa funcionar mesmo assim.
    renderHeader();

    expect(screen.getByRole('link', { name: 'Servidores MCP' })).toHaveAttribute(
      'href',
      '/mcp-servers',
    );
  });

  it('exibe título, estado, descrição e ações', () => {
    renderHeader({
      status: <span>Ativo</span>,
      description: 'Tools de saldo e extrato.',
      actions: <button type="button">Editar</button>,
    });

    expect(screen.getByRole('heading', { name: 'Servidor Financeiro' })).toBeInTheDocument();
    expect(screen.getByText('Ativo')).toBeInTheDocument();
    expect(screen.getByText('Tools de saldo e extrato.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Editar' })).toBeInTheDocument();
  });

  it('sinaliza a ausência de descrição', () => {
    renderHeader();

    expect(screen.getByText('Sem descrição.')).toBeInTheDocument();
  });

  // Sem a linha, e não com a linha dizendo "Sem descrição.": o detalhe da base
  // de conhecimento tem a descrição em card próprio, e o fallback aqui
  // afirmaria que a base não tem descrição, quando a API a exige não vazia
  // (design.md da change frontend-knowledge-base-catalogo, D7).
  it('omite a linha de descrição quando showDescription é falso', () => {
    renderHeader({ showDescription: false });

    expect(screen.queryByText('Sem descrição.')).not.toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Servidor Financeiro' })).toBeInTheDocument();
  });

  it('omite a linha de descrição mesmo quando há descrição a exibir', () => {
    renderHeader({ showDescription: false, description: 'Tools de saldo e extrato.' });

    expect(screen.queryByText('Tools de saldo e extrato.')).not.toBeInTheDocument();
  });
});
