import { describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MantineProvider } from '@mantine/core';
import type { ComponentProps } from 'react';
import { theme } from '../../../theme';
import { KnowledgeDocumentHistoryTab } from './KnowledgeDocumentHistoryTab';
import { formatEventDay } from '../utils/documentHistory';
import type { KnowledgeDocumentEvent } from '../types/knowledgeDocumentEvent';

type Props = ComponentProps<typeof KnowledgeDocumentHistoryTab>;

function evento(overrides: Partial<KnowledgeDocumentEvent> = {}): KnowledgeDocumentEvent {
  return {
    id: 'e-1',
    documentId: 'doc-1',
    documentTitle: 'Política de trocas',
    type: 'Created',
    contentChanged: null,
    titleChanged: null,
    author: 'operator',
    occurredAt: '2026-09-26T15:32:00Z',
    ...overrides,
  };
}

// Instantes ao meio-dia UTC: o dia é o mesmo em qualquer fuso de -11 a +11, e a
// suíte não fixa o TZ do processo.
const DAY_A = '2026-09-26T12:00:00Z';
const DAY_B = '2026-09-23T12:00:00Z';

function props(overrides: Partial<Props> = {}): Props {
  return {
    events: [],
    firstPageFailed: false,
    hasNextPage: false,
    isFetchingNextPage: false,
    nextPageFailed: false,
    isRefreshing: false,
    refreshFailed: false,
    synced: false,
    onRetry: vi.fn(),
    onLoadMore: vi.fn(),
    onRetryNextPage: vi.fn(),
    onRefresh: vi.fn(),
    ...overrides,
  };
}

function renderTab(overrides: Partial<Props> = {}) {
  const all = props(overrides);
  const view = render(
    <MantineProvider theme={theme}>
      <KnowledgeDocumentHistoryTab {...all} />
    </MantineProvider>,
  );
  const rerender = (next: Partial<Props>) =>
    view.rerender(
      <MantineProvider theme={theme}>
        <KnowledgeDocumentHistoryTab {...all} {...next} />
      </MantineProvider>,
    );
  return { ...all, rerender };
}

const LOADING = 'Carregando histórico…';
const FIRST_PAGE_FAILED = 'Não foi possível carregar o histórico desta base.';
const EMPTY = 'Nenhuma mudança foi registrada nesta base.';
const END = 'Não há eventos mais antigos.';
const NEXT_PAGE_FAILED = 'Não foi possível carregar mais eventos.';
const REFRESH_FAILED = 'Não foi possível atualizar o histórico.';

// Os estados da D4, cada um com a negativa dos outros: carregando, falha e
// vazio não se passam um pelo outro (convenção 13).
describe('estados da primeira página', () => {
  it('carregando não é vazio nem falha, e não oferece Atualizar nem Carregar mais', () => {
    renderTab({ events: undefined });

    expect(screen.getByText(LOADING)).toBeInTheDocument();
    expect(screen.queryByText(EMPTY)).not.toBeInTheDocument();
    expect(screen.queryByText(FIRST_PAGE_FAILED)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Atualizar' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Carregar mais' })).not.toBeInTheDocument();
    expect(screen.queryByRole('listitem')).not.toBeInTheDocument();
  });

  it('falha não é vazio, oferece tentar de novo e não oferece Atualizar', async () => {
    const user = userEvent.setup();
    const { onRetry } = renderTab({ events: undefined, firstPageFailed: true });

    expect(screen.getByText(FIRST_PAGE_FAILED)).toBeInTheDocument();
    expect(screen.queryByText(EMPTY)).not.toBeInTheDocument();
    expect(screen.queryByText(LOADING)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Atualizar' })).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Tentar novamente' }));
    expect(onRetry).toHaveBeenCalledTimes(1);
  });

  it('vazio não é falha, e não oferece Carregar mais', () => {
    renderTab({ events: [] });

    expect(screen.getByText(EMPTY)).toBeInTheDocument();
    expect(screen.queryByText(FIRST_PAGE_FAILED)).not.toBeInTheDocument();
    expect(screen.queryByText(LOADING)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Carregar mais' })).not.toBeInTheDocument();
    expect(screen.queryByText(END)).not.toBeInTheDocument();
  });

  it('fim da lista diz que não há mais antigos, sem Carregar mais', () => {
    renderTab({ events: [evento()], hasNextPage: false });

    expect(screen.getByText(END)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Carregar mais' })).not.toBeInTheDocument();
  });
});

describe('a lista', () => {
  it('um título por dia, e os eventos do dia como itens de lista', () => {
    renderTab({
      events: [
        evento({ id: 'a', occurredAt: DAY_A }),
        evento({ id: 'b', occurredAt: DAY_A }),
        evento({ id: 'c', occurredAt: DAY_B }),
      ],
    });

    const headings = screen.getAllByRole('heading', { level: 3 });
    expect(headings.map((heading) => heading.textContent)).toEqual([
      formatEventDay(DAY_A),
      formatEventDay(DAY_B),
    ]);
    const firstDay = screen.getByRole('region', { name: formatEventDay(DAY_A) });
    expect(within(firstDay).getAllByRole('listitem')).toHaveLength(2);
    const secondDay = screen.getByRole('region', { name: formatEventDay(DAY_B) });
    expect(within(secondDay).getAllByRole('listitem')).toHaveLength(1);
  });

  it('o horário carrega o instante completo para máquina', () => {
    const { container } = render(
      <MantineProvider theme={theme}>
        <KnowledgeDocumentHistoryTab {...props({ events: [evento({ occurredAt: DAY_A })] })} />
      </MantineProvider>,
    );

    const time = container.querySelector('time');
    expect(time).toHaveAttribute('dateTime', DAY_A);
    expect(time?.textContent).toMatch(/^\d{2}:\d{2}$/);
  });

  it('cada tipo, cada detalhe e cada autor', () => {
    renderTab({
      events: [
        evento({ id: 'a', type: 'Created', author: 'operator', documentTitle: 'A' }),
        evento({
          id: 'b',
          type: 'Updated',
          contentChanged: true,
          titleChanged: false,
          author: 'service:connectors',
          documentTitle: 'B',
        }),
        evento({
          id: 'c',
          type: 'Updated',
          contentChanged: false,
          titleChanged: true,
          documentTitle: 'C',
        }),
        evento({
          id: 'd',
          type: 'Updated',
          contentChanged: true,
          titleChanged: true,
          documentTitle: 'D',
        }),
        evento({ id: 'e', type: 'Deleted', documentTitle: 'E' }),
      ],
    });

    const [a, b, c, d, e] = screen.getAllByRole('listitem');
    expect(a).toHaveTextContent('Incluído');
    expect(a).toHaveTextContent('Operador');
    expect(a).not.toHaveTextContent('alterado');
    expect(b).toHaveTextContent('Atualizado');
    expect(b).toHaveTextContent('Conteúdo alterado');
    expect(b).toHaveTextContent('Sincronização');
    expect(c).toHaveTextContent('Título alterado');
    expect(d).toHaveTextContent('Conteúdo e título alterados');
    expect(e).toHaveTextContent('Excluído');
    expect(e).not.toHaveTextContent('alterado');
  });

  it('tipo e autor desconhecidos não exibem rótulo conhecido', () => {
    renderTab({
      events: [
        evento({ type: 'Archived' as KnowledgeDocumentEvent['type'], author: 'service:outro' }),
      ],
    });

    const item = screen.getByRole('listitem');
    expect(item).toHaveTextContent('Archived');
    expect(item).toHaveTextContent('Autor que o painel não reconhece (service:outro)');
    expect(item).not.toHaveTextContent(/Incluído|Atualizado|Excluído/);
    expect(within(item).queryByText('Operador')).not.toBeInTheDocument();
    expect(within(item).queryByText('Sincronização')).not.toBeInTheDocument();
  });

  // O título GRAVADO NO EVENTO, sem link e sem aviso de documento inexistente
  // (D7): renomeado e depois excluído, cada evento mostra o seu.
  it('mostra o título de cada evento, sem link', () => {
    renderTab({
      events: [
        evento({ id: 'b', type: 'Deleted', documentTitle: 'Prazos de entrega' }),
        evento({ id: 'a', type: 'Created', documentTitle: 'Prazos de envio' }),
      ],
    });

    const [deleted, created] = screen.getAllByRole('listitem');
    expect(deleted).toHaveTextContent('Prazos de entrega');
    expect(created).toHaveTextContent('Prazos de envio');
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
  });
});

describe('Carregar mais', () => {
  it('com página seguinte, aparece e chama onLoadMore', async () => {
    const user = userEvent.setup();
    const { onLoadMore } = renderTab({ events: [evento()], hasNextPage: true });

    expect(screen.queryByText(END)).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Carregar mais' }));
    expect(onLoadMore).toHaveBeenCalledTimes(1);
  });

  it('carregando a página seguinte, indica carregamento e não aceita outro clique', async () => {
    const user = userEvent.setup();
    const { onLoadMore } = renderTab({
      events: [evento()],
      hasNextPage: true,
      isFetchingNextPage: true,
    });

    const button = screen.getByRole('button', { name: 'Carregar mais' });
    expect(button).toHaveAttribute('data-loading');
    await user.click(button);
    expect(onLoadMore).not.toHaveBeenCalled();
  });

  it('falha na página seguinte preserva o que foi carregado e não diz que acabou', async () => {
    const user = userEvent.setup();
    const { onRetryNextPage } = renderTab({
      events: [evento({ documentTitle: 'Já carregado' })],
      hasNextPage: true,
      nextPageFailed: true,
    });

    expect(screen.getByText('Já carregado')).toBeInTheDocument();
    expect(screen.getByText(NEXT_PAGE_FAILED)).toBeInTheDocument();
    expect(screen.queryByText(END)).not.toBeInTheDocument();
    expect(screen.queryByText(FIRST_PAGE_FAILED)).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Tentar novamente' }));
    expect(onRetryNextPage).toHaveBeenCalledTimes(1);
  });

  // D8: na última página o botão some; sem mover o foco, ele cairia no body.
  it('depois de carregar a última página, o foco está no primeiro evento acrescentado', async () => {
    const user = userEvent.setup();
    const first = [evento({ id: 'a', occurredAt: DAY_A, documentTitle: 'Primeiro' })];
    const { rerender } = renderTab({ events: first, hasNextPage: true });

    await user.click(screen.getByRole('button', { name: 'Carregar mais' }));
    rerender({
      events: [...first, evento({ id: 'b', occurredAt: DAY_B, documentTitle: 'Acrescentado' })],
      hasNextPage: false,
    });

    const added = screen.getAllByRole('listitem')[1];
    expect(added).toHaveTextContent('Acrescentado');
    expect(added).toHaveFocus();
  });
});

describe('Atualizar', () => {
  it.each([
    ['vazio', { events: [] }],
    ['lista com mais', { events: [evento()], hasNextPage: true }],
    ['fim da lista', { events: [evento()], hasNextPage: false }],
  ] as const)('aparece no estado %s e chama onRefresh', async (_estado, state) => {
    const user = userEvent.setup();
    const { onRefresh } = renderTab({ ...state, events: [...state.events] });

    await user.click(screen.getByRole('button', { name: 'Atualizar' }));
    expect(onRefresh).toHaveBeenCalledTimes(1);
  });

  it('relendo, indica carregamento e a lista continua na tela', () => {
    renderTab({ events: [evento({ documentTitle: 'Na tela' })], isRefreshing: true });

    expect(screen.getByRole('button', { name: 'Atualizar' })).toHaveAttribute('data-loading');
    expect(screen.getByText('Na tela')).toBeInTheDocument();
    expect(screen.queryByText(LOADING)).not.toBeInTheDocument();
  });

  it('a falha da releitura preserva a lista e não é a falha da primeira página', () => {
    renderTab({ events: [evento({ documentTitle: 'Na tela' })], refreshFailed: true });

    expect(screen.getByText('Na tela')).toBeInTheDocument();
    expect(screen.getByText(REFRESH_FAILED)).toBeInTheDocument();
    expect(screen.queryByText(FIRST_PAGE_FAILED)).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Atualizar' })).toBeInTheDocument();
  });
});

describe('notas', () => {
  const START = /O histórico é registrado a partir da implantação desta funcionalidade/;
  const IGNORED = /Arquivos ignorados e falhas de acesso não entram no histórico/;

  it('base manual: só a nota do começo do histórico', () => {
    renderTab({ events: [evento()], synced: false });

    expect(screen.getByText(START)).toBeInTheDocument();
    expect(screen.queryByText(IGNORED)).not.toBeInTheDocument();
  });

  it('base sincronizada: as duas notas', () => {
    renderTab({ events: [evento()], synced: true });

    expect(screen.getByText(START)).toBeInTheDocument();
    expect(screen.getByText(IGNORED)).toBeInTheDocument();
  });

  it.each([
    ['carregando', { events: undefined }],
    ['falha', { events: undefined, firstPageFailed: true }],
    ['vazio', { events: [] }],
  ] as const)('a nota aparece também no estado %s', (_estado, state) => {
    renderTab({ ...state, events: state.events ? [...state.events] : undefined });
    expect(screen.getByText(START)).toBeInTheDocument();
  });
});
