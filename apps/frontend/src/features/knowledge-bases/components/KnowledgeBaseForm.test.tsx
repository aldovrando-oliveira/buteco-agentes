import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MantineProvider } from '@mantine/core';
import { theme } from '../../../theme';
import { KnowledgeBaseForm } from './KnowledgeBaseForm';

const longDescription =
  'Regras de negociação, prazos de pagamento, faixas de desconto por perfil e os casos que precisam de aprovação humana.';

function renderForm(props: Partial<Parameters<typeof KnowledgeBaseForm>[0]> = {}) {
  const onSubmit = vi.fn();
  const onCancel = vi.fn();
  render(
    <MantineProvider theme={theme}>
      <KnowledgeBaseForm onSubmit={onSubmit} onCancel={onCancel} {...props} />
    </MantineProvider>,
  );
  return { onSubmit, onCancel };
}

describe('KnowledgeBaseForm', () => {
  it('envia nome e descrição preenchidos', async () => {
    const user = userEvent.setup();
    const { onSubmit } = renderForm();

    await user.type(screen.getByLabelText(/Nome/), 'Políticas de Cobrança');
    await user.type(screen.getByLabelText(/Descrição/), longDescription);
    await user.click(screen.getByRole('button', { name: 'Criar base' }));

    expect(onSubmit).toHaveBeenCalledWith({
      name: 'Políticas de Cobrança',
      description: longDescription,
    });
  });

  it('barra nome vazio no cliente, sem chamar o envio', async () => {
    const user = userEvent.setup();
    const { onSubmit } = renderForm();

    await user.type(screen.getByLabelText(/Descrição/), longDescription);
    await user.click(screen.getByRole('button', { name: 'Criar base' }));

    expect(
      await screen.findByText('O nome da base de conhecimento é obrigatório.'),
    ).toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('barra nome só de espaços', async () => {
    const user = userEvent.setup();
    const { onSubmit } = renderForm();

    await user.type(screen.getByLabelText(/Nome/), '   ');
    await user.type(screen.getByLabelText(/Descrição/), longDescription);
    await user.click(screen.getByRole('button', { name: 'Criar base' }));

    expect(
      await screen.findByText('O nome da base de conhecimento é obrigatório.'),
    ).toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  // Descrição obrigatória é a divergência 2 do handoff, e o backend a impõe:
  // ValidateShape rejeita descrição vazia na criação e na edição (design.md, D7).
  it('barra descrição vazia no cliente, sem chamar o envio', async () => {
    const user = userEvent.setup();
    const { onSubmit } = renderForm();

    await user.type(screen.getByLabelText(/Nome/), 'Políticas de Cobrança');
    await user.click(screen.getByRole('button', { name: 'Criar base' }));

    expect(await screen.findByText(/A descrição da base de conhecimento é obrigatória/)).toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('barra descrição só de espaços', async () => {
    const user = userEvent.setup();
    const { onSubmit } = renderForm();

    await user.type(screen.getByLabelText(/Nome/), 'Políticas de Cobrança');
    await user.type(screen.getByLabelText(/Descrição/), '    ');
    await user.click(screen.getByRole('button', { name: 'Criar base' }));

    expect(await screen.findByText(/A descrição da base de conhecimento é obrigatória/)).toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('conta os caracteres da descrição desde o campo vazio', () => {
    renderForm();

    expect(screen.getByTestId('description-char-count')).toHaveTextContent('0 caracteres');
  });

  it('avisa sobre descrição curta sem bloquear o salvamento', async () => {
    const user = userEvent.setup();
    const { onSubmit } = renderForm();

    await user.type(screen.getByLabelText(/Nome/), 'Políticas de Cobrança');
    await user.type(screen.getByLabelText(/Descrição/), 'Regras de cobrança.');

    expect(screen.getByTestId('description-char-count')).toHaveTextContent(/curto demais/);

    await user.click(screen.getByRole('button', { name: 'Criar base' }));

    expect(onSubmit).toHaveBeenCalled();
  });

  it('não avisa quando a descrição passa do limite curto', async () => {
    const user = userEvent.setup();
    renderForm();

    await user.type(screen.getByLabelText(/Descrição/), longDescription);

    expect(screen.getByTestId('description-char-count')).not.toHaveTextContent(/curto demais/);
  });

  it('não avisa sobre descrição curta quando a descrição está vazia', () => {
    renderForm();

    expect(screen.getByTestId('description-char-count')).not.toHaveTextContent(/curto demais/);
  });

  it('orienta como escrever a descrição, em bloco próprio', () => {
    renderForm();

    const bloco = screen.getByTestId('description-block');
    expect(bloco).toHaveTextContent(/o que o modelo lê para decidir se consulta esta base/i);
    expect(bloco).toHaveTextContent(/não é um resumo para o operador/i);
    expect(bloco).toHaveTextContent(/que assunto está aqui/i);
    expect(bloco).toHaveTextContent(/em que situação consultar/i);
  });

  // O preview vive DENTRO do bloco da descrição, como no protótipo: é o que
  // fecha o argumento de que aquele texto tem outro leitor.
  it('o preview fica dentro do bloco da descrição', () => {
    renderForm();

    expect(screen.getByTestId('description-block')).toContainElement(
      screen.getByTestId('preview-name'),
    );
  });

  it('o preview explica a consequência de não ter descrição', () => {
    renderForm();

    expect(screen.getByTestId('preview-name')).toHaveTextContent('nome da base');
    expect(screen.getByTestId('preview-description')).toHaveTextContent(
      /o modelo recebe só o nome e decide no chute/i,
    );
  });

  // Asserção negativa (convenção 13): o protótipo mostra o identificador
  // `consultar_base`, que não existe em spec nenhuma — nome de tool de base é
  // decisão da etapa 4 e passa pelo ToolNameDeduplicator (design.md, D19).
  it('não afirma um nome de tool que o sistema ainda não definiu', () => {
    renderForm();

    expect(screen.queryByText(/consultar_base/)).not.toBeInTheDocument();
    expect(screen.getByTestId('description-block')).not.toHaveTextContent(/consultar_/);
  });

  it('diz onde os documentos entram, sem afirmar contagem', () => {
    renderForm();

    const nota = screen.getByTestId('documents-note');
    expect(nota).toHaveTextContent(/depois de criar a base, na tela de detalhe/i);
    expect(nota).toHaveTextContent(/só markdown/i);
    expect(screen.queryByText(/nenhum documento/i)).not.toBeInTheDocument();
  });

  it('orienta o campo de nome', () => {
    renderForm();

    expect(screen.getByText(/identifica a base nas listas e no vínculo com o agente/i)).toBeInTheDocument();
  });

  it('exibe erro por campo devolvido pela API, não erro genérico', () => {
    renderForm({ errors: { description: 'A descrição da base de conhecimento é obrigatória.' } });

    const field = screen.getByLabelText(/Descrição/);
    expect(field).toHaveAttribute('aria-invalid', 'true');
    expect(
      screen.getByText('A descrição da base de conhecimento é obrigatória.'),
    ).toBeInTheDocument();
  });

  it('mostra ao operador o que o modelo recebe', async () => {
    const user = userEvent.setup();
    renderForm();

    await user.type(screen.getByLabelText(/Nome/), 'Cardápio');
    await user.type(screen.getByLabelText(/Descrição/), longDescription);

    expect(screen.getByTestId('preview-name')).toHaveTextContent('Cardápio');
    expect(screen.getByTestId('preview-description')).toHaveTextContent(longDescription);
  });

  it('carrega os valores atuais na edição', () => {
    renderForm({
      initialValues: { name: 'Cardápio', description: longDescription },
      submitLabel: 'Salvar alterações',
    });

    expect(screen.getByLabelText(/Nome/)).toHaveValue('Cardápio');
    expect(screen.getByLabelText(/Descrição/)).toHaveValue(longDescription);
    expect(screen.getByRole('button', { name: 'Salvar alterações' })).toBeInTheDocument();
  });

  it('Cancelar avisa quem chamou, sem enviar', async () => {
    const user = userEvent.setup();
    const { onCancel, onSubmit } = renderForm();

    await user.click(screen.getByRole('button', { name: 'Cancelar' }));

    expect(onCancel).toHaveBeenCalled();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('mantém o texto digitado ao exibir erro por campo devolvido pela API', () => {
    renderForm({
      initialValues: { name: 'Cardápio', description: longDescription },
      errors: { name: 'O nome da base de conhecimento é obrigatório.' },
    });

    expect(screen.getByLabelText(/Nome/)).toHaveValue('Cardápio');
    expect(
      screen.getByText('O nome da base de conhecimento é obrigatório.'),
    ).toBeInTheDocument();
  });
});
