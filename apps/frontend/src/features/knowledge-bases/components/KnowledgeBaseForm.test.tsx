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
  const { unmount } = render(
    <MantineProvider theme={theme}>
      <KnowledgeBaseForm onSubmit={onSubmit} onCancel={onCancel} {...props} />
    </MantineProvider>,
  );
  return { onSubmit, onCancel, unmount };
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

    expect(
      await screen.findByText(/A descrição da base de conhecimento é obrigatória/),
    ).toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('barra descrição só de espaços', async () => {
    const user = userEvent.setup();
    const { onSubmit } = renderForm();

    await user.type(screen.getByLabelText(/Nome/), 'Políticas de Cobrança');
    await user.type(screen.getByLabelText(/Descrição/), '    ');
    await user.click(screen.getByRole('button', { name: 'Criar base' }));

    expect(
      await screen.findByText(/A descrição da base de conhecimento é obrigatória/),
    ).toBeInTheDocument();
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

  // P1 — o critério que `0d` mediu. "Escreva mais" não resolve; "escreva
  // delimitado" resolve, e a orientação anterior só pedia o primeiro.
  it('a orientação pede que o texto diga do que a base NÃO trata', () => {
    renderForm();

    expect(screen.getByTestId('description-block')).toHaveTextContent(
      /do que esta base não trata/i,
    );
  });

  // P2 — a consequência que explica por que delimitar importa: as bases competem
  // entre si no mesmo conjunto de tools, e errar de base não tem recuperação.
  it('a orientação diz que descrição genérica atrai perguntas de outras bases', () => {
    renderForm();

    const bloco = screen.getByTestId('description-block');
    expect(bloco).toHaveTextContent(/escolhe comparando as descrições/i);
    expect(bloco).toHaveTextContent(/a mais genérica atrai as perguntas que eram das outras/i);
  });

  // N2 — o sufixo fala do TEXTO, nunca da decisão do modelo. A cópia anterior
  // ("curto demais para o modelo decidir com segurança") prometia o que o
  // comprimento não compra: `0d` mediu que a descrição que mais errou tinha 421
  // caracteres. Cada negativa é um `it()` próprio (convenção 18, quarta causa).
  it('o sinal de descrição curta não afirma nada sobre a decisão do modelo', async () => {
    const user = userEvent.setup();
    renderForm();

    await user.type(screen.getByLabelText(/Descrição/), 'Regras de cobrança.');

    const contador = screen.getByTestId('description-char-count');
    expect(contador).toHaveTextContent(/curto demais/);
    expect(contador).not.toHaveTextContent(/com segurança/i);
    expect(contador).not.toHaveTextContent(/decidir/i);
  });

  // N3 — o outro estado da mesma recusa: acima do piso o contador não afirma
  // NADA. Não existe selo de aprovação porque não existe critério automático que
  // o sustente.
  it('descrição acima do piso não recebe selo de aprovação', async () => {
    const user = userEvent.setup();
    renderForm();

    await user.type(screen.getByLabelText(/Descrição/), longDescription);

    const contador = screen.getByTestId('description-char-count');
    expect(contador).not.toHaveTextContent(/curto demais/);
    expect(contador).not.toHaveTextContent(/boa|adequada|suficiente|ideal|ótima/i);
  });

  // N4 — nenhum veredito de generalidade. O guarda é comportamental de propósito:
  // duas descrições de MESMO comprimento e generalidade oposta têm de produzir a
  // MESMA devolutiva. Qualquer heurística que alguém acrescente separa as duas e
  // reprova aqui.
  it('não emite veredito de generalidade: mesmo comprimento, mesma devolutiva', async () => {
    const generica =
      'Documentos diversos e materiais gerais da equipe, sem recorte de assunto definido aqui.';
    const especifica =
      'Prazos de reembolso de passagem aérea nacional; não cobre hotéis, seguros nem bagagens.';
    expect(generica).toHaveLength(especifica.length);

    const user = userEvent.setup();
    const { unmount } = renderForm();
    await user.type(screen.getByLabelText(/Descrição/), generica);
    const devolutivaGenerica = screen.getByTestId('description-char-count').textContent;
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    unmount();

    renderForm();
    await user.type(screen.getByLabelText(/Descrição/), especifica);

    expect(screen.getByTestId('description-char-count').textContent).toBe(devolutivaGenerica);
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  // N5 — nenhuma contagem de uso da base. O sistema não coleta consultas por base:
  // não há contador em apps/api, e a comparação que diagnosticaria um base-atrator
  // ("chamadas" contra "perguntas que eram dela") não tem nenhuma das duas metades
  // persistida (design.md, D2). O guarda é sobre o BLOCO inteiro, e não sobre uma
  // cadeia: o único número que pode aparecer ali é a contagem de caracteres.
  it('o bloco da descrição não exibe número nenhum além da contagem de caracteres', () => {
    renderForm();

    const bloco = screen.getByTestId('description-block');
    const contador = screen.getByTestId('description-char-count');
    const semContador = (bloco.textContent ?? '').replace(contador.textContent ?? '', '');

    expect(semContador).not.toMatch(/\d/);
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
      /o modelo recebe só o nome desta base e decide no chute/i,
    );
  });

  // P3 — a afirmação é ESTRUTURAL: que o sistema envolve o texto, não QUAL é o
  // texto que ele acrescenta. Ver N7 para a outra metade.
  it('o preview declara que o sistema envolve o texto em instruções fixas', () => {
    renderForm();

    const nota = screen.getByTestId('preview-wrapping-note');
    expect(nota).toHaveTextContent(/o sistema envolve este texto/i);
    expect(nota).toHaveTextContent(/uma linha que nomeia a base/i);
    expect(nota).toHaveTextContent(/instruções fixas de como ler o resultado/i);
  });

  // N6 — o preview não se intitula como tudo o que o agente recebe. "Como o agente
  // vê esta base" era verdade quando a 5a-1 escreveu e a etapa 4 a tornou falsa:
  // desde KnowledgeToolDescription.cs:52 o modelo recebe prefixo + este texto + 441
  // caracteres fixos. É a convenção 13 sobre uma frase que envelheceu.
  it('o preview não se intitula como tudo o que o agente recebe', () => {
    renderForm();

    expect(screen.queryByText(/como o agente vê esta base/i)).not.toBeInTheDocument();
    expect(screen.getByTestId('description-block')).toHaveTextContent(
      /seu texto, dentro do que o agente recebe/i,
    );
  });

  // N7 — e não reproduz as instruções fixas. Trazê-las para cá seria segunda fonte
  // de verdade de uma prosa que vive em apps/workers e tem guarda própria lá (a que
  // proíbe valor numérico de corte em KnowledgeToolDescription).
  it('o preview não reproduz as instruções fixas de leitura do resultado', async () => {
    const user = userEvent.setup();
    renderForm();

    await user.type(screen.getByLabelText(/Descrição/), longDescription);

    const bloco = screen.getByTestId('description-block');
    expect(bloco).not.toHaveTextContent(/não filtra por relevância/i);
    expect(bloco).not.toHaveTextContent(/serve para ordenar, não para medir acerto/i);
    expect(bloco).not.toHaveTextContent(/a base não cobre o assunto/i);
  });

  // N1 — asserção negativa (convenção 13). O nome do teste mudou porque o que ele
  // dizia deixou de ser verdade: a etapa 4 DEFINIU o nome (`search_<slug>`,
  // KnowledgeToolSetResolver.cs:20). A asserção é a mesma; o que estava errado era
  // a justificativa.
  //
  // A razão que NÃO caduca é a terceira do design.md (D4): o nome construído é o
  // PRETENDIDO. ToolNameDeduplicator renomeia por colisão com precedência
  // MCP → delegação → conhecimento, então a tool de conhecimento é sempre a
  // renomeada, e a colisão só se resolve na execução — com o conjunto inteiro do
  // agente, que o formulário de UMA base não tem nem pode ter.
  it('não exibe identificador de ferramenta, nem o do protótipo nem o da etapa 4', () => {
    renderForm();

    const bloco = screen.getByTestId('description-block');
    expect(screen.queryByText(/consultar_base/)).not.toBeInTheDocument();
    expect(bloco).not.toHaveTextContent(/consultar_/);
    expect(bloco).not.toHaveTextContent(/search_/);
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

    expect(
      screen.getByText(/identifica a base nas listas e no vínculo com o agente/i),
    ).toBeInTheDocument();
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
    expect(screen.getByText('O nome da base de conhecimento é obrigatório.')).toBeInTheDocument();
  });
});
