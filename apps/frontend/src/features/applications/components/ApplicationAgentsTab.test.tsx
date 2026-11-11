import { describe, expect, it } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import {
  CATALOGO,
  COBRANCA_INATIVA,
  CRM,
  PEDIDOS,
  VAREJO,
  detalhe,
  fakeApplicationsApi,
  problem,
} from '../test/fixtures';
import { renderApplications } from '../test/renderApplications';
import type { ApplicationAgent } from '../types';

// Spec `panel-external-application-agents`: a aba Agentes (quadros `AplicacaoDetalhe`,
// `AplicacaoDetalheAdicionar` e `AplicacaoDetalheEditar`, prop `agentesExtra`, versão 66). Os
// casos marcados GUARDA são os da seção 8 do tasks.md.

const AGENTES = 'GET /applications/app-portal/agents';
const LIBERAR = 'POST /applications/app-portal/agents';

function abrir(
  grade: ApplicationAgent[] | ApplicationAgent[][],
  extra: Record<string, unknown> = {},
  app = detalhe(),
) {
  const api = fakeApplicationsApi({
    'GET /applications/app-portal': { status: 200, body: app },
    [AGENTES]: Array.isArray(grade[0])
      ? (grade as ApplicationAgent[][]).map((g) => ({ status: 200, body: g }))
      : { status: 200, body: grade },
    'GET /agents': { status: 200, body: CATALOGO },
    ...(extra as Record<string, never>),
  });
  const view = renderApplications('/applications/app-portal');
  return { api, ...view };
}

const linhaDe = (nome: string) =>
  screen.getByRole('link', { name: nome }).closest('tr') as HTMLElement;

describe('ApplicationAgentsTab', () => {
  it('a grade do quadro', async () => {
    abrir([VAREJO, PEDIDOS]);

    const pedidos = (await screen.findByRole('link', { name: 'Triagem de Pedidos' })).closest(
      'tr',
    ) as HTMLElement;
    expect(within(pedidos).getAllByText('Permitido')).toHaveLength(2);
    expect(within(pedidos).getAllByText('Não permitido')).toHaveLength(1);
    expect(within(pedidos).getByText('Abre e classifica pedidos de revenda')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Atendimento Varejo' })).toHaveAttribute(
      'href',
      '/agents/ag-varejo',
    );
    expect(
      screen.getByText('Agentes alcançados por delegação não precisam ser liberados.'),
    ).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /Agentes/ })).toHaveTextContent('2');
  });

  it('Agente inativo na grade', async () => {
    abrir([COBRANCA_INATIVA]);

    const linha = (await screen.findByRole('link', { name: 'Cobrança' })).closest(
      'tr',
    ) as HTMLElement;
    expect(within(linha).getByText('Inativo')).toBeInTheDocument();
    expect(within(linha).getAllByText('Permitido')).toHaveLength(1);
    expect(within(linha).getAllByText('Não permitido')).toHaveLength(2);
  });

  it('Aplicação recém-criada', async () => {
    abrir([]);

    expect(
      await screen.findByText(
        'Nenhum agente liberado. Enquanto a lista estiver vazia, toda chamada A2A desta aplicação é recusada.',
      ),
    ).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.getByRole('tab', { name: /Agentes/ })).toHaveTextContent('0'),
    );
  });

  it('Falha da grade', async () => {
    fakeApplicationsApi({
      'GET /applications/app-portal': { status: 200, body: detalhe() },
      [AGENTES]: problem(500, 'Erro.'),
    });
    renderApplications('/applications/app-portal');

    expect(await screen.findByText('Não foi possível carregar os agentes.')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Portal do Parceiro' })).toBeInTheDocument();
  });

  it('Adicionar com as operações de início', async () => {
    const novo: ApplicationAgent = {
      ...VAREJO,
      agenteId: 'ag-suporte',
      nome: 'Suporte Técnico',
      descricao: 'Diagnóstico de primeiro nível',
      operacoes: ['enviar-mensagem', 'consultar-task'],
    };
    const user = userEvent.setup();
    const { api } = abrir(
      [
        [VAREJO, PEDIDOS],
        [VAREJO, novo, PEDIDOS],
      ],
      { [LIBERAR]: { status: 201, body: novo } },
    );

    await user.click(await screen.findByRole('button', { name: 'Adicionar agente' }));
    expect(
      screen.getByRole('heading', { name: 'Novo agente para esta aplicação' }),
    ).toBeInTheDocument();
    const seletor = await screen.findByLabelText('Agente');
    await waitFor(() => expect(within(seletor).getAllByRole('option')).toHaveLength(3));
    expect(
      within(seletor).queryByRole('option', { name: 'Atendimento Varejo' }),
    ).not.toBeInTheDocument();
    expect(within(seletor).getByRole('option', { name: 'Cobrança (inativo)' })).toBeInTheDocument();
    expect(screen.getByLabelText('Enviar mensagem')).toBeChecked();
    expect(screen.getByLabelText('Consultar task')).toBeChecked();
    expect(screen.getByLabelText('Notificação push')).not.toBeChecked();

    await user.selectOptions(seletor, 'ag-suporte');
    expect(screen.getByText('Diagnóstico de primeiro nível')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Adicionar' }));

    expect(await screen.findByRole('link', { name: 'Suporte Técnico' })).toBeInTheDocument();
    expect(api.calls(LIBERAR)[0].body).toEqual({
      agenteId: 'ag-suporte',
      operacoes: ['enviar-mensagem', 'consultar-task'],
    });
    expect(
      screen.queryByRole('heading', { name: 'Novo agente para esta aplicação' }),
    ).not.toBeInTheDocument();
  });

  it('o agente inativo avisa, e sem operação não adiciona', async () => {
    const user = userEvent.setup();
    abrir([VAREJO]);

    await user.click(await screen.findByRole('button', { name: 'Adicionar agente' }));
    await user.selectOptions(await screen.findByLabelText('Agente'), 'ag-cobranca');

    expect(
      screen.getByText(
        'Este agente está inativo. Ele fica liberado, mas as tasks que a aplicação abrir nele terminam recusadas até ele ser ativado.',
      ),
    ).toBeInTheDocument();
    await user.click(screen.getByLabelText('Enviar mensagem'));
    await user.click(screen.getByLabelText('Consultar task'));

    expect(screen.getByText('Marque ao menos uma operação.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Adicionar' })).toBeDisabled();
  });

  it('Já liberado por outro Admin', async () => {
    const outro: ApplicationAgent = { ...VAREJO, agenteId: 'ag-suporte', nome: 'Suporte Técnico' };
    const user = userEvent.setup();
    abrir([[VAREJO], [VAREJO, outro]], {
      [LIBERAR]: problem(409, 'Este agente já está liberado para a aplicação.'),
    });

    await user.click(await screen.findByRole('button', { name: 'Adicionar agente' }));
    await user.selectOptions(await screen.findByLabelText('Agente'), 'ag-suporte');
    await user.click(screen.getByRole('button', { name: 'Adicionar' }));

    // O `title` do servidor ("…para a aplicação."), e não o do quadro ("…para esta aplicação.").
    expect(
      await screen.findByText('Este agente já está liberado para a aplicação.'),
    ).toBeInTheDocument();
    expect(await screen.findByRole('link', { name: 'Suporte Técnico' })).toBeInTheDocument();
    expect(screen.getByLabelText('Agente')).toHaveValue('');
    expect(
      within(screen.getByLabelText('Agente')).queryByRole('option', { name: 'Suporte Técnico' }),
    ).not.toBeInTheDocument();
  });

  it('O agente sumiu', async () => {
    const user = userEvent.setup();
    const { api } = abrir([VAREJO], {
      [LIBERAR]: {
        status: 400,
        body: { title: 'Erros.', errors: { agenteId: ['Agente não encontrado.'] } },
      },
    });

    await user.click(await screen.findByRole('button', { name: 'Adicionar agente' }));
    await user.selectOptions(await screen.findByLabelText('Agente'), 'ag-suporte');
    await user.click(screen.getByRole('button', { name: 'Adicionar' }));

    expect(await screen.findByText('Agente não encontrado.')).toBeInTheDocument();
    await waitFor(() => expect(api.calls('GET /agents').length).toBeGreaterThan(1));
  });

  it('Nenhum agente disponível', async () => {
    const user = userEvent.setup();
    const todos = CATALOGO.map((a) => ({ ...VAREJO, agenteId: a.id, nome: a.name }));
    abrir(todos);

    await user.click(await screen.findByRole('button', { name: 'Adicionar agente' }));

    expect(
      await screen.findByText('Todos os agentes já estão liberados para esta aplicação.'),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Adicionar' })).toBeDisabled();
  });

  it('editar troca só a linha e salva as operações na ordem do vínculo', async () => {
    const user = userEvent.setup();
    const { api } = abrir(
      [
        [VAREJO, PEDIDOS],
        [VAREJO, { ...PEDIDOS, operacoes: ['consultar-task', 'notificacao-push'] }],
      ],
      {
        'PUT /applications/app-portal/agents/ag-pedidos': { status: 200, body: PEDIDOS },
      },
    );

    await user.click(
      await screen.findByRole('button', { name: 'Editar operações de Triagem de Pedidos' }),
    );
    const linha = linhaDe('Triagem de Pedidos');
    await user.click(within(linha).getByLabelText('Notificação push em Triagem de Pedidos'));
    await user.click(within(linha).getByLabelText('Enviar mensagem em Triagem de Pedidos'));
    await user.click(within(linha).getByRole('button', { name: 'Salvar' }));

    await waitFor(() =>
      expect(api.calls('PUT /applications/app-portal/agents/ag-pedidos')).toHaveLength(1),
    );
    expect(api.calls('PUT /applications/app-portal/agents/ag-pedidos')[0].body).toEqual({
      operacoes: ['consultar-task', 'notificacao-push'],
    });
    expect(within(linhaDe('Atendimento Varejo')).queryByRole('checkbox')).not.toBeInTheDocument();
  });

  it('Sem operação não salva', async () => {
    const user = userEvent.setup();
    const { api } = abrir([PEDIDOS]);

    await user.click(
      await screen.findByRole('button', { name: 'Editar operações de Triagem de Pedidos' }),
    );
    const linha = linhaDe('Triagem de Pedidos');
    await user.click(within(linha).getByLabelText('Enviar mensagem em Triagem de Pedidos'));
    await user.click(within(linha).getByLabelText('Consultar task em Triagem de Pedidos'));

    expect(within(linha).getByText('Marque ao menos uma operação')).toBeInTheDocument();
    expect(within(linha).getByRole('button', { name: 'Salvar' })).toBeDisabled();
    expect(api.calls('PUT /applications/app-portal/agents/ag-pedidos')).toHaveLength(0);
  });

  it('Vínculo retirado por outro Admin', async () => {
    const user = userEvent.setup();
    abrir([[VAREJO, PEDIDOS], [PEDIDOS]], {
      'PUT /applications/app-portal/agents/ag-varejo': problem(
        404,
        'Agente não liberado para esta aplicação.',
      ),
    });

    await user.click(
      await screen.findByRole('button', { name: 'Editar operações de Atendimento Varejo' }),
    );
    await user.click(within(linhaDe('Atendimento Varejo')).getByRole('button', { name: 'Salvar' }));

    expect(
      await screen.findByText('Agente não liberado para esta aplicação. A lista foi atualizada.'),
    ).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.queryByRole('link', { name: 'Atendimento Varejo' })).not.toBeInTheDocument(),
    );
  });

  // GUARDA (tasks.md 8.5). Reprova com "Remover" chamando o `DELETE` direto.
  it('Remover pede confirmação', async () => {
    const user = userEvent.setup();
    const { api } = abrir([[VAREJO, PEDIDOS], [PEDIDOS]], {
      'DELETE /applications/app-portal/agents/ag-varejo': { status: 204 },
    });

    await user.click(await screen.findByRole('button', { name: 'Remover Atendimento Varejo' }));

    const faixa = screen
      .getByText(
        'Remover o acesso da aplicação a Atendimento Varejo? A próxima chamada dela a este agente é recusada; as tasks já abertas não mudam.',
      )
      .closest('[role="alert"]') as HTMLElement;
    expect(api.calls('DELETE /applications/app-portal/agents/ag-varejo')).toHaveLength(0);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    await user.click(within(faixa).getByRole('button', { name: 'Remover' }));

    await waitFor(() =>
      expect(screen.queryByRole('link', { name: 'Atendimento Varejo' })).not.toBeInTheDocument(),
    );
    expect(api.calls('DELETE /applications/app-portal/agents/ag-varejo')).toHaveLength(1);
  });

  // GUARDA (tasks.md 8.6, o caso próprio da Q9). Reprova com o `404` tratado como as outras falhas.
  it('Já retirado por outro Admin', async () => {
    const user = userEvent.setup();
    const { api } = abrir([[VAREJO, PEDIDOS], [PEDIDOS]], {
      'DELETE /applications/app-portal/agents/ag-varejo': problem(
        404,
        'Agente não liberado para esta aplicação.',
      ),
    });

    await user.click(await screen.findByRole('button', { name: 'Remover Atendimento Varejo' }));
    const faixa = screen
      .getByText(/^Remover o acesso da aplicação a Atendimento Varejo\?/)
      .closest('[role="alert"]') as HTMLElement;
    await user.click(within(faixa).getByRole('button', { name: 'Remover' }));

    await waitFor(() =>
      expect(screen.queryByRole('link', { name: 'Atendimento Varejo' })).not.toBeInTheDocument(),
    );
    expect(screen.queryByText('Agente não liberado para esta aplicação.')).not.toBeInTheDocument();
    expect(api.calls(AGENTES).length).toBeGreaterThan(1);
  });

  it('Editar uma linha bloqueia o resto', async () => {
    const user = userEvent.setup();
    abrir([VAREJO, PEDIDOS]);

    await user.click(
      await screen.findByRole('button', { name: 'Editar operações de Atendimento Varejo' }),
    );

    expect(screen.getByRole('button', { name: 'Adicionar agente' })).toBeDisabled();
    expect(
      screen.getByRole('button', { name: 'Editar operações de Triagem de Pedidos' }),
    ).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Remover Triagem de Pedidos' })).toBeDisabled();
  });

  it('adicionar bloqueia as linhas', async () => {
    const user = userEvent.setup();
    abrir([VAREJO]);

    await user.click(await screen.findByRole('button', { name: 'Adicionar agente' }));

    expect(
      screen.getByRole('button', { name: 'Editar operações de Atendimento Varejo' }),
    ).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Remover Atendimento Varejo' })).toBeDisabled();
  });

  it('Desativada continua editável', async () => {
    abrir([VAREJO], {}, detalhe({ ...CRM, id: 'app-portal', nome: 'Portal do Parceiro' }));

    expect(
      await screen.findByText(
        'Aplicação desativada: as chamadas A2A dela são recusadas. Os agentes liberados ficam guardados e voltam a valer ao reativar.',
      ),
    ).toBeInTheDocument();
    expect(await screen.findByRole('button', { name: 'Adicionar agente' })).toBeEnabled();
    expect(
      screen.getByRole('button', { name: 'Editar operações de Atendimento Varejo' }),
    ).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Remover Atendimento Varejo' })).toBeEnabled();
  });
});
