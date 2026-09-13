import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MantineProvider } from '@mantine/core';
import { Notifications } from '@mantine/notifications';
import { RouterProvider, createMemoryRouter } from 'react-router';
import { theme } from '../../../theme';
import { KnowledgeBaseDetailPage } from './KnowledgeBaseDetailPage';
import {
  ApiError,
  activateKnowledgeBase,
  deactivateKnowledgeBase,
  getKnowledgeBase,
} from '../api/knowledgeBasesApi';
import { listAgents } from '../../agents/api/agentsApi';
import { listKnowledgeIndexDiagnostics } from '../api/knowledgeIndexApi';
import {
  deleteKnowledgeDocument,
  listKnowledgeDocuments,
  reindexKnowledgeDocument,
} from '../api/knowledgeDocumentsApi';
import type { KnowledgeBase } from '../types/knowledgeBase';
import type { KnowledgeDocumentSummary } from '../types/knowledgeDocument';
import type { Agent } from '../../agents/types/agent';
import type { KnowledgeIndexProvenance } from '../types/knowledgeIndex';

vi.mock('../api/knowledgeBasesApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/knowledgeBasesApi')>();
  return {
    ...actual,
    getKnowledgeBase: vi.fn(),
    activateKnowledgeBase: vi.fn(),
    deactivateKnowledgeBase: vi.fn(),
  };
});

vi.mock('../../agents/api/agentsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../agents/api/agentsApi')>();
  return { ...actual, listAgents: vi.fn() };
});

vi.mock('../api/knowledgeIndexApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/knowledgeIndexApi')>();
  return { ...actual, listKnowledgeIndexDiagnostics: vi.fn() };
});

vi.mock('../api/knowledgeDocumentsApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/knowledgeDocumentsApi')>();
  return {
    ...actual,
    listKnowledgeDocuments: vi.fn(),
    getKnowledgeDocument: vi.fn(),
    deleteKnowledgeDocument: vi.fn(),
    reindexKnowledgeDocument: vi.fn(),
  };
});

function agent(overrides: Partial<Agent>): Agent {
  return {
    id: 'agent-1',
    name: 'Atendimento Financeiro',
    instructions: 'Você é um atendente.',
    isActive: true,
    provider: 'openai',
    model: 'gpt-5.6-sol',
    description: null,
    skills: [],
    createdAt: '2026-09-01T00:00:00Z',
    updatedAt: '2026-09-01T00:00:00Z',
    mcpServers: [],
    delegatesTo: [],
    knowledgeBases: [],
    a2a: null,
    ...overrides,
  };
}

const knowledgeBase: KnowledgeBase = {
  id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
  name: 'Políticas de Cobrança',
  description: 'Regras de negociação, prazos e faixas de desconto.',
  isActive: true,
  createdAt: '2026-09-01T10:00:00Z',
  updatedAt: '2026-09-02T11:00:00Z',
};

function documento(overrides: Partial<KnowledgeDocumentSummary> = {}): KnowledgeDocumentSummary {
  return {
    id: 'doc-1',
    knowledgeBaseId: knowledgeBase.id,
    title: 'Faixas de atraso e descontos',
    sourceType: 'markdown',
    contentLengthBytes: 8420,
    indexingStatus: 'Indexed',
    indexedAt: '2026-09-02T03:14:00Z',
    failureReason: null,
    contentRevision: 1,
    fragmentCount: 14,
    indexingAttempts: 1,
    lastAttemptAt: '2026-09-02T03:14:00Z',
    createdAt: '2026-09-01T00:00:00Z',
    updatedAt: '2026-09-02T00:00:00Z',
    ...overrides,
  };
}

// Devolve o router para que os testes de aba possam afirmar o ENDEREÇO, e não só
// o DOM — a aba ativa vive na URL, e é isso que torna o link compartilhável.
function renderPage(search = '') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const router = createMemoryRouter(
    [
      { path: '/knowledge-bases/:id', element: <KnowledgeBaseDetailPage /> },
      { path: '/knowledge-bases', element: <p>listagem de bases</p> },
      { path: '/knowledge-bases/:id/edit', element: <p>edição de base</p> },
    ],
    { initialEntries: [`/knowledge-bases/${knowledgeBase.id}${search}`] },
  );

  render(
    <MantineProvider theme={theme}>
      <QueryClientProvider client={queryClient}>
        <Notifications />
        <RouterProvider router={router} />
      </QueryClientProvider>
    </MantineProvider>,
  );

  return router;
}

function tab(name: RegExp) {
  return screen.getByRole('tab', { name });
}

describe('KnowledgeBaseDetailPage', () => {
  beforeEach(() => {
    vi.mocked(getKnowledgeBase).mockReset();
    vi.mocked(activateKnowledgeBase).mockReset();
    vi.mocked(deactivateKnowledgeBase).mockReset();
    vi.mocked(listAgents).mockReset();
    vi.mocked(listAgents).mockResolvedValue([]);
    vi.mocked(listKnowledgeDocuments).mockReset();
    vi.mocked(listKnowledgeDocuments).mockResolvedValue([]);
    vi.mocked(deleteKnowledgeDocument).mockReset();
    vi.mocked(deleteKnowledgeDocument).mockResolvedValue(undefined);
    vi.mocked(reindexKnowledgeDocument).mockReset();
    vi.mocked(listKnowledgeIndexDiagnostics).mockReset();
    vi.mocked(listKnowledgeIndexDiagnostics).mockResolvedValue([]);
  });

  it('exibe indicador de carregamento enquanto a base não chega', () => {
    vi.mocked(getKnowledgeBase).mockReturnValue(new Promise(() => {}));

    renderPage();

    expect(screen.getByText('Carregando base de conhecimento...')).toBeInTheDocument();
  });

  it('exibe nome e badge de estado', async () => {
    vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);

    renderPage();

    expect(await screen.findByRole('heading', { name: knowledgeBase.name })).toBeInTheDocument();
    expect(screen.getByText('Ativa')).toBeInTheDocument();
  });

  // O protótipo não tem datas nesta tela — "Criado em" aparece só no detalhe do
  // agente e no do servidor MCP. O card que existia aqui era acréscimo meu, sem
  // decisão registrada (design.md, D20).
  it('não exibe card de datas', async () => {
    vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);

    renderPage();

    await screen.findByRole('heading', { name: knowledgeBase.name });
    expect(screen.queryByText('Criado em')).not.toBeInTheDocument();
    expect(screen.queryByText('Atualizado em')).not.toBeInTheDocument();
  });

  it('lista os agentes que consultam a base', async () => {
    vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);
    vi.mocked(listAgents).mockResolvedValue([
      agent({ id: 'a1', knowledgeBases: [{ id: knowledgeBase.id, name: knowledgeBase.name }] }),
    ]);

    renderPage();

    expect(await screen.findByRole('link', { name: 'Atendimento Financeiro' })).toHaveAttribute(
      'href',
      '/agents/a1',
    );
  });

  it('informa quando nenhum agente consulta a base', async () => {
    vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);

    renderPage();

    expect(await screen.findByTestId('agents-empty')).toHaveTextContent(
      'Nenhum agente consulta esta base.',
    );
  });

  it('falha ao carregar agentes não impede o detalhe de renderizar', async () => {
    vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);
    vi.mocked(listAgents).mockRejectedValue(new Error('falha de rede'));

    renderPage();

    expect(await screen.findByRole('heading', { name: knowledgeBase.name })).toBeInTheDocument();
    expect(await screen.findByTestId('agents-unavailable')).toBeInTheDocument();
  });

  it('oferece volta para a listagem de conhecimento', async () => {
    vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);

    renderPage();

    expect(await screen.findByRole('link', { name: /bases de conhecimento/i })).toHaveAttribute(
      'href',
      '/knowledge-bases',
    );
  });

  it('oferece Editar apontando para a edição da base', async () => {
    vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);

    renderPage();

    expect(await screen.findByRole('link', { name: 'Editar' })).toHaveAttribute(
      'href',
      `/knowledge-bases/${knowledgeBase.id}/edit`,
    );
  });

  it('exibe a descrição em card próprio', async () => {
    vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);

    renderPage();

    expect(await screen.findByTestId('knowledge-base-description')).toHaveTextContent(
      knowledgeBase.description,
    );
    expect(screen.getByText(/texto lido pelo modelo/i)).toBeInTheDocument();
  });

  // A descrição NÃO é o subtítulo do cabeçalho: como subtítulo ela lê como texto
  // decorativo, e ela é campo de runtime (design.md, D7). Sem `showDescription`
  // falso, o DetailHeader renderizaria "Sem descrição." aqui — que afirmaria o
  // contrário do que a API garante.
  it('não usa a descrição como subtítulo do cabeçalho', async () => {
    vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);

    renderPage();

    const heading = await screen.findByRole('heading', { name: knowledgeBase.name });
    expect(screen.getAllByText(knowledgeBase.description)).toHaveLength(1);
    expect(heading.parentElement?.parentElement?.textContent).not.toContain(
      knowledgeBase.description,
    );
  });

  it('não afirma ausência de descrição no cabeçalho', async () => {
    vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);

    renderPage();

    await screen.findByRole('heading', { name: knowledgeBase.name });
    expect(screen.queryByText('Sem descrição.')).not.toBeInTheDocument();
  });

  // A nota de sequenciamento da 5a-1 saiu: a tela consulta os documentos, então
  // o estado vazio passa a ser afirmação VERIFICADA, e não suposição.
  it('exibe a área de documentos com a listagem da base', async () => {
    vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);
    vi.mocked(listKnowledgeDocuments).mockResolvedValue([documento()]);

    renderPage();

    expect(await screen.findByText('Faixas de atraso e descontos')).toBeInTheDocument();
    expect(screen.getByText('14 fragmentos')).toBeInTheDocument();
    expect(screen.queryByTestId('documents-placeholder')).not.toBeInTheDocument();
  });

  it('base sem documento exibe estado vazio verificado', async () => {
    vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);
    vi.mocked(listKnowledgeDocuments).mockResolvedValue([]);

    renderPage();

    expect(await screen.findByTestId('documents-empty')).toHaveTextContent(
      'Nenhum documento nesta base.',
    );
  });

  it('reindexar chama a rota de reindexação do documento', async () => {
    const user = userEvent.setup();
    vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);
    vi.mocked(listKnowledgeDocuments).mockResolvedValue([
      documento({ indexingStatus: 'Failed', indexedAt: null, failureReason: '429 do provedor.' }),
    ]);
    vi.mocked(reindexKnowledgeDocument).mockResolvedValue({
      ...documento({ indexingStatus: 'Pending' }),
      extractedText: '',
    });

    renderPage();

    await user.click(await screen.findByRole('button', { name: 'Reindexar documento' }));

    expect(reindexKnowledgeDocument).toHaveBeenCalledWith(knowledgeBase.id, 'doc-1');
  });

  it('excluir documento passa por confirmação e nomeia os agentes afetados', async () => {
    const user = userEvent.setup();
    vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);
    vi.mocked(listKnowledgeDocuments).mockResolvedValue([documento()]);
    vi.mocked(listAgents).mockResolvedValue([
      agent({ knowledgeBases: [{ id: knowledgeBase.id, name: knowledgeBase.name }] }),
    ]);

    renderPage();

    await user.click(await screen.findByRole('button', { name: 'Excluir' }));

    expect(await screen.findByTestId('delete-document-affected-agents')).toHaveTextContent(
      'Atendimento Financeiro',
    );
    expect(deleteKnowledgeDocument).not.toHaveBeenCalled();

    await user.click(screen.getByRole('button', { name: 'Excluir documento' }));

    expect(deleteKnowledgeDocument).toHaveBeenCalledWith(knowledgeBase.id, 'doc-1');
  });

  it('cancelar a confirmação não exclui o documento', async () => {
    const user = userEvent.setup();
    vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);
    vi.mocked(listKnowledgeDocuments).mockResolvedValue([documento()]);

    renderPage();

    await user.click(await screen.findByRole('button', { name: 'Excluir' }));
    await user.click(await screen.findByRole('button', { name: 'Cancelar' }));

    expect(deleteKnowledgeDocument).not.toHaveBeenCalled();
  });

  // A ASSERÇÃO QUE MORREU AQUI era a negativa da 5a-1: "não apresenta controle de
  // abas", com o gatilho registrado de que ela cairia quando existisse a segunda
  // aba de verdade. A segunda chegou, e o gatilho foi exercido — não removido por
  // conveniência. Ela é a ÚNICA asserção existente desta suíte que mudou; as
  // outras 22 continuam valendo palavra por palavra, porque `Documentos` é a aba
  // default e a tela default é a que era.
  describe('barra de abas', () => {
    it('apresenta as duas abas, com Documentos ativa por padrão', async () => {
      vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);

      renderPage();

      expect(await screen.findByRole('tab', { name: /documentos/i })).toHaveAttribute(
        'aria-selected',
        'true',
      );
      expect(tab(/diagnóstico do índice/i)).toBeInTheDocument();
      expect(screen.getAllByRole('tab')).toHaveLength(2);
    });

    it('a aba selecionada vai para o endereço', async () => {
      const user = userEvent.setup();
      vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);

      const router = renderPage();
      await screen.findByRole('tab', { name: /documentos/i });

      await user.click(tab(/diagnóstico do índice/i));

      await waitFor(() => expect(router.state.location.search).toBe('?tab=diagnostico'));
    });

    it('abrir o endereço da aba de diagnóstico abre a aba de diagnóstico', async () => {
      vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);

      renderPage('?tab=diagnostico');

      expect(await screen.findByRole('tab', { name: /diagnóstico do índice/i })).toHaveAttribute(
        'aria-selected',
        'true',
      );
    });

    it('a aba canônica volta ao endereço sem parâmetro', async () => {
      const user = userEvent.setup();
      vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);

      const router = renderPage('?tab=diagnostico');
      await screen.findByRole('tab', { name: /documentos/i });

      await user.click(tab(/documentos/i));

      await waitFor(() => expect(router.state.location.search).toBe(''));
    });

    it('aba desconhecida cai na primeira, sem reescrever o endereço', async () => {
      vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);

      const router = renderPage('?tab=inexistente');

      expect(await screen.findByRole('tab', { name: /documentos/i })).toHaveAttribute(
        'aria-selected',
        'true',
      );
      expect(router.state.location.search).toBe('?tab=inexistente');
    });

    // `keepMounted={false}`: a aba inativa não existe no DOM. É o que sustenta o
    // `enabled` da consulta de proveniência.
    it('a aba inativa não está montada', async () => {
      vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);
      vi.mocked(listKnowledgeDocuments).mockResolvedValue([documento()]);

      renderPage();

      await screen.findByRole('tab', { name: /documentos/i });
      expect(screen.queryByTestId('diagnostics-readonly-note')).not.toBeInTheDocument();
      expect(screen.queryByText('Volume desta base')).not.toBeInTheDocument();
    });

    it('não busca a proveniência com a aba de documentos ativa', async () => {
      vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);
      vi.mocked(listKnowledgeDocuments).mockResolvedValue([documento()]);

      renderPage();

      await screen.findByRole('tab', { name: /documentos/i });
      await screen.findByText(documento().title);
      expect(listKnowledgeIndexDiagnostics).not.toHaveBeenCalled();
    });

    it('busca a proveniência ao entrar na aba de diagnóstico', async () => {
      const provenance: KnowledgeIndexProvenance[] = [
        {
          provider: 'openai',
          model: 'qwen-qwen3-embedding-8b',
          dimensions: 4096,
          fragmentCount: 3,
        },
      ];
      vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);
      vi.mocked(listKnowledgeIndexDiagnostics).mockResolvedValue(provenance);

      renderPage('?tab=diagnostico');

      expect(await screen.findByText('qwen-qwen3-embedding-8b')).toBeInTheDocument();
      expect(listKnowledgeIndexDiagnostics).toHaveBeenCalled();
    });

    // O contador não afirma zero enquanto a listagem não respondeu: a contagem
    // vem de uma segunda requisição, e `0` durante o carregamento seria uma
    // contagem que ninguém fez (design.md, D4).
    it('não exibe contador enquanto a listagem de documentos não respondeu', async () => {
      vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);
      vi.mocked(listKnowledgeDocuments).mockReturnValue(new Promise(() => {}));

      renderPage();

      const documentos = await screen.findByRole('tab', { name: /documentos/i });
      expect(within(documentos).queryByText('0')).not.toBeInTheDocument();
      expect(documentos.textContent).toBe('Documentos');
    });

    it('não exibe contador com a base sem documento', async () => {
      vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);
      vi.mocked(listKnowledgeDocuments).mockResolvedValue([]);

      renderPage();

      await screen.findByTestId('documents-empty');
      expect(tab(/documentos/i).textContent).toBe('Documentos');
    });

    it('exibe o contador quando a listagem traz documentos', async () => {
      vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);
      vi.mocked(listKnowledgeDocuments).mockResolvedValue([
        documento({ id: 'a' }),
        documento({ id: 'b' }),
      ]);

      renderPage();

      await screen.findByRole('tab', { name: /documentos/i });
      await waitFor(() => expect(within(tab(/documentos/i)).getByText('2')).toBeInTheDocument());
    });
  });

  it('desativar passa por confirmação antes de qualquer requisição', async () => {
    const user = userEvent.setup();
    vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);

    renderPage();
    await user.click(await screen.findByRole('button', { name: 'Desativar' }));

    // O texto "Confirmar desativação" é ao mesmo tempo título do modal e rótulo
    // do botão, então a asserção é sobre o diálogo e a cópia dele.
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText(/deixarão de consultá-la/i)).toBeInTheDocument();
    expect(deactivateKnowledgeBase).not.toHaveBeenCalled();
  });

  it('desativa só após confirmar', async () => {
    const user = userEvent.setup();
    vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);
    vi.mocked(deactivateKnowledgeBase).mockResolvedValue({ ...knowledgeBase, isActive: false });

    renderPage();
    await user.click(await screen.findByRole('button', { name: 'Desativar' }));
    const dialog = await screen.findByRole('dialog');
    await user.click(within(dialog).getByRole('button', { name: 'Confirmar desativação' }));

    expect(deactivateKnowledgeBase).toHaveBeenCalledWith(knowledgeBase.id);
  });

  it('cancelar a confirmação não desativa', async () => {
    const user = userEvent.setup();
    vi.mocked(getKnowledgeBase).mockResolvedValue(knowledgeBase);

    renderPage();
    await user.click(await screen.findByRole('button', { name: 'Desativar' }));
    const dialog = await screen.findByRole('dialog');
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar' }));

    expect(deactivateKnowledgeBase).not.toHaveBeenCalled();
  });

  it('ativar é imediato, sem confirmação', async () => {
    const user = userEvent.setup();
    vi.mocked(getKnowledgeBase).mockResolvedValue({ ...knowledgeBase, isActive: false });
    vi.mocked(activateKnowledgeBase).mockResolvedValue(knowledgeBase);

    renderPage();
    await user.click(await screen.findByRole('button', { name: 'Ativar' }));

    expect(activateKnowledgeBase).toHaveBeenCalledWith(knowledgeBase.id);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('informa base não encontrada, com volta para a listagem', async () => {
    vi.mocked(getKnowledgeBase).mockRejectedValue(new ApiError(404, 'Não encontrado'));

    renderPage();

    expect(await screen.findByText('Base de conhecimento não encontrada.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Voltar para as bases' })).toHaveAttribute(
      'href',
      '/knowledge-bases',
    );
  });

  it('informa falha genérica de carregamento', async () => {
    vi.mocked(getKnowledgeBase).mockRejectedValue(new Error('falha de rede'));

    renderPage();

    expect(
      await screen.findByText('Não foi possível carregar a base de conhecimento.'),
    ).toBeInTheDocument();
  });
});
