import { describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MantineProvider } from '@mantine/core';
import { RouterProvider, createMemoryRouter } from 'react-router';
import { theme } from '../../../theme';
import { KnowledgeBaseLinkModal } from './KnowledgeBaseLinkModal';
import type { KnowledgeBase } from '../../knowledge-bases/types/knowledgeBase';

function base(
  overrides: Partial<KnowledgeBase> & Pick<KnowledgeBase, 'id' | 'name'>,
): KnowledgeBase {
  return {
    description: 'Descrição da base.',
    isActive: true,
    createdAt: '2026-08-01T00:00:00Z',
    updatedAt: '2026-09-01T00:00:00Z',
    ...overrides,
  };
}

const cardapio = base({
  id: 'k1',
  name: 'Cardápio A La Carte',
  description: 'Pratos, porções e preços.',
});
const rotinas = base({
  id: 'k2',
  name: 'Rotinas Internas',
  description: 'Processos que só o time usa.',
  isActive: false,
});
const cobranca = base({
  id: 'k3',
  name: 'Políticas de Cobrança',
  description: 'Regras de negociação de faturas em atraso.',
});

function renderModal(overrides?: {
  catalog?: KnowledgeBase[];
  selectedIds?: string[];
  onToggle?: (id: string) => void;
  onClose?: () => void;
}) {
  const onToggle = overrides?.onToggle ?? vi.fn();
  const onClose = overrides?.onClose ?? vi.fn();
  const router = createMemoryRouter(
    [
      {
        path: '/agents/:id',
        element: (
          <KnowledgeBaseLinkModal
            opened
            catalog={overrides?.catalog ?? [cardapio, rotinas, cobranca]}
            selectedIds={overrides?.selectedIds ?? []}
            onToggle={onToggle}
            onClose={onClose}
          />
        ),
      },
    ],
    { initialEntries: ['/agents/a1?tab=conhecimento'] },
  );

  render(
    <MantineProvider theme={theme}>
      <RouterProvider router={router} />
    </MantineProvider>,
  );

  return { onToggle, onClose };
}

describe('KnowledgeBaseLinkModal', () => {
  it('lista o catálogo com nome e descrição', () => {
    renderModal();

    expect(screen.getByText('Cardápio A La Carte')).toBeInTheDocument();
    expect(screen.getByText('Pratos, porções e preços.')).toBeInTheDocument();
    expect(screen.getByText('Políticas de Cobrança')).toBeInTheDocument();
  });

  // O protótipo compara com indexOf cru e falha exatamente neste caso —
  // verificado ao vivo no percurso. Todas as buscas do painel normalizam acento.
  it('busca sem acento encontra base acentuada', async () => {
    const user = userEvent.setup();
    renderModal();

    await user.type(screen.getByLabelText('Buscar por nome ou descrição'), 'cardapio');

    expect(screen.getByText('Cardápio A La Carte')).toBeInTheDocument();
    expect(screen.queryByText('Políticas de Cobrança')).not.toBeInTheDocument();
  });

  it('busca alcança a descrição, não só o nome', async () => {
    const user = userEvent.setup();
    renderModal();

    await user.type(screen.getByLabelText('Buscar por nome ou descrição'), 'faturas');

    expect(screen.getByText('Políticas de Cobrança')).toBeInTheDocument();
    expect(screen.queryByText('Cardápio A La Carte')).not.toBeInTheDocument();
  });

  it('alterna o rótulo do controle conforme a base já esteja escolhida', () => {
    renderModal({ selectedIds: [cardapio.id] });

    const escolhida = screen.getByTestId(`link-modal-row-${cardapio.id}`);
    expect(within(escolhida).getByRole('button', { name: 'Vinculada' })).toBeInTheDocument();

    const naoEscolhida = screen.getByTestId(`link-modal-row-${cobranca.id}`);
    expect(within(naoEscolhida).getByRole('button', { name: 'Vincular' })).toBeInTheDocument();
  });

  it('escolher uma base avisa o rascunho e mantém o modal aberto', async () => {
    const user = userEvent.setup();
    const { onToggle, onClose } = renderModal();

    const linha = screen.getByTestId(`link-modal-row-${cobranca.id}`);
    await user.click(within(linha).getByRole('button', { name: 'Vincular' }));

    expect(onToggle).toHaveBeenCalledWith(cobranca.id);
    expect(onClose).not.toHaveBeenCalled();
    expect(screen.getByText('Cardápio A La Carte')).toBeInTheDocument();
  });

  it('marca a base inativa do catálogo', () => {
    renderModal();

    const linha = screen.getByTestId(`link-modal-row-${rotinas.id}`);
    expect(within(linha).getByText('Inativa')).toBeInTheDocument();
  });

  // Par negativo do cenário acima: sem ele, um componente que marcasse TODAS as
  // linhas como inativas passaria no teste anterior.
  it('não marca base ativa como inativa', () => {
    renderModal();

    const linha = screen.getByTestId(`link-modal-row-${cardapio.id}`);
    expect(within(linha).queryByText('Inativa')).not.toBeInTheDocument();
  });

  it('busca sem correspondência e catálogo vazio têm textos distintos', async () => {
    const user = userEvent.setup();
    const { onToggle } = renderModal();

    await user.type(screen.getByLabelText('Buscar por nome ou descrição'), 'zzzz');
    expect(screen.getByTestId('link-modal-empty')).toHaveTextContent(
      'Nenhuma base corresponde à busca.',
    );

    screen.getByRole('button', { name: 'Concluir' });
    expect(onToggle).not.toHaveBeenCalled();
  });

  it('catálogo sem nenhuma base informa cadastro vazio, não busca vazia', () => {
    renderModal({ catalog: [] });

    expect(screen.getByTestId('link-modal-empty')).toHaveTextContent(
      'Nenhuma base cadastrada ainda.',
    );
  });

  it('Concluir apenas fecha, sem gravar nada', async () => {
    const user = userEvent.setup();
    const { onClose, onToggle } = renderModal();

    await user.click(screen.getByRole('button', { name: 'Concluir' }));

    expect(onClose).toHaveBeenCalled();
    expect(onToggle).not.toHaveBeenCalled();
  });

  it('oferece o caminho de criar nova base', () => {
    renderModal();

    expect(screen.getByRole('link', { name: 'Criar nova base' })).toHaveAttribute(
      'href',
      '/knowledge-bases/new',
    );
  });

  // Asserção NEGATIVA, e é ela que sustenta o requisito: KnowledgeBaseResponse
  // não tem contagem nenhuma, e o protótipo mostra "{n} documentos · {k}
  // indexados" aqui. Exibir zero afirmaria que a contagem foi feita e deu zero
  // (convenção 13). A asserção positiva não protegeria contra isso.
  it('a linha não exibe contagem de documentos nem estado de indexação', () => {
    renderModal();

    const linha = screen.getByTestId(`link-modal-row-${cardapio.id}`);
    expect(linha).not.toHaveTextContent(/documento/i);
    expect(linha).not.toHaveTextContent(/indexad/i);
    expect(linha).not.toHaveTextContent(/fragmento/i);
  });
});
