import { Box, Button, Group, Paper, Stack, Text, TextInput, Textarea } from '@mantine/core';
import { useForm } from '@mantine/form';
import type { CreateKnowledgeBaseInput } from '../types/knowledgeBase';

interface KnowledgeBaseFormProps {
  onSubmit: (values: CreateKnowledgeBaseInput) => void;
  onCancel: () => void;
  errors?: Record<string, string>;
  submitting?: boolean;
  initialValues?: CreateKnowledgeBaseInput;
  submitLabel?: string;
}

// Piso de "escreveu alguma coisa": abaixo disto não cabe o assunto MAIS o que
// fica de fora. É aviso, não bloqueio.
//
// O QUE ESTE LIMIAR NÃO MEDE, e a frase existe para impedir que alguém o "melhore"
// subindo o número: comprimento não é a dimensão que decide roteamento entre
// bases. A rodada de medição `0d` mediu sete descrições de 250 a 427 caracteres —
// todas passariam folgadas por qualquer limiar aqui —, e a que canibalizou as
// vizinhas (5 intenções com alvo, 24 consultas recebidas, contra uma base de 28
// intenções e 11 consultas) tinha 421. A dimensão que decide é generalidade, e
// nenhuma regra automática a distingue: o que cabe é a cópia de orientação acima,
// não um limiar novo.
const SHORT_DESCRIPTION_THRESHOLD = 80;

// Medida de leitura dos blocos de prosa. O formulário ocupa a largura da tela,
// mas parágrafo com 1600px de linha não se lê — o protótipo limita a orientação
// a 600px pelo mesmo motivo.
const PROSE_MAX_WIDTH = 620;

function toFormValues(input?: CreateKnowledgeBaseInput): CreateKnowledgeBaseInput {
  return { name: input?.name ?? '', description: input?.description ?? '' };
}

// A descrição não é um campo a mais: é o único campo desta tela cujo leitor é o
// modelo, e não uma pessoa. Por isso ela vive num bloco próprio com borda
// esquerda na cor de destaque, com a orientação de como escrevê-la e o preview
// do que o agente recebe dentro do mesmo bloco — a estrutura do protótipo
// (design.md, D7 e D19).
export function KnowledgeBaseForm({
  onSubmit,
  onCancel,
  errors,
  submitting,
  initialValues,
  submitLabel = 'Criar base',
}: KnowledgeBaseFormProps) {
  const form = useForm<CreateKnowledgeBaseInput>({
    initialValues: toFormValues(initialValues),
    validate: {
      name: (value) => (value.trim() ? null : 'O nome da base de conhecimento é obrigatório.'),
      description: (value) =>
        value.trim()
          ? null
          : 'A descrição da base de conhecimento é obrigatória — é o texto que o agente usa para decidir quando consultá-la.',
    },
  });

  const description = form.values.description;
  const isShort = description.length > 0 && description.length < SHORT_DESCRIPTION_THRESHOLD;
  const descriptionError = errors?.description ?? form.errors.description;

  return (
    // onSubmit envolvido, e não passado direto: form.onSubmit repassa
    // (values, event), e o evento vazaria para quem consome o formulário.
    <form
      onSubmit={form.onSubmit((values) =>
        onSubmit({ name: values.name, description: values.description }),
      )}
    >
      <Stack>
        <Paper withBorder radius="md" p="lg">
          <TextInput
            label="Nome"
            description="Identifica a base nas listas e no vínculo com o agente."
            placeholder="ex.: Políticas de cobrança"
            withAsterisk
            {...form.getInputProps('name')}
            error={errors?.name ?? form.errors.name}
          />
        </Paper>

        <Paper
          withBorder
          radius="md"
          p="lg"
          style={{ borderLeft: '3px solid var(--mantine-primary-color-filled)' }}
          data-testid="description-block"
        >
          <Stack gap="xs">
            <Text fw={600} size="sm">
              Descrição — é o que o modelo lê para decidir se consulta esta base
            </Text>
            {/* A orientação diz o critério, e o critério é DELIMITAÇÃO — não
                tamanho. Medido em `0d`: o agente escolhe entre as bases
                vinculadas comparando as descrições entre si, então a mais geral
                vence as vizinhas, e errar de base não tem recuperação (0 de 31
                na rodada). Escrever "mais" não resolve; escrever "mais
                delimitado" é o que resolve, e é o que esta cópia pede. */}
            <Text size="xs" c="dimmed" maw={PROSE_MAX_WIDTH} style={{ lineHeight: 1.55 }}>
              Não é um resumo para o operador. Durante a conversa, o agente vê apenas o nome e este
              texto, e decide a partir deles se vale abrir a base. Diga{' '}
              <strong>que assunto está aqui</strong>, <strong>em que situação consultar</strong> e{' '}
              <strong>do que esta base não trata</strong> — quando o agente tem mais de uma base,
              ele escolhe comparando as descrições, e a mais genérica atrai as perguntas que eram
              das outras. Evite “documentos diversos”, “materiais da equipe” e afins.
            </Text>

            <Textarea
              aria-label="Descrição"
              placeholder="ex.: Regras de negociação de faturas em atraso: prazos, descontos por faixa de atraso, parcelamento mínimo e o que exige aprovação humana. Consulte antes de prometer qualquer condição de pagamento."
              rows={5}
              resize="vertical"
              withAsterisk
              {...form.getInputProps('description')}
              error={descriptionError}
            />

            {/* Contador sempre visível, como no protótipo — o aviso é o sufixo e
                a cor, não a presença do contador.

                O sufixo fala do TEXTO, nunca da decisão do modelo. A cópia
                anterior dizia "curto demais para o modelo decidir com segurança",
                e prometia o que o comprimento não compra: acima do limiar não há
                segurança nenhuma — a descrição de 421 caracteres de `0d` é a que
                errou mais. Acima do piso o contador não afirma nada, e é
                deliberado: não existe selo de aprovação aqui porque não existe
                critério automático que o sustente (convenção 13). */}
            <Text size="xs" c={isShort ? 'yellow' : 'dimmed'} data-testid="description-char-count">
              {description.length} {description.length === 1 ? 'caractere' : 'caracteres'}
              {isShort && ' · curto demais para caber o assunto e o que fica de fora'}
            </Text>

            {/* O protótipo mostra aqui o identificador `consultar_base`. Ele
                continua NÃO implementado, e o motivo mudou — a etapa 4 já
                aconteceu, então a metade "é decisão da etapa 4" do comentário
                anterior caducou. As três razões que sobraram são independentes, e
                cada uma basta (design.md, D4):

                1. NENHUMA SPEC FIXA O FORMATO. `search_<slug>` é `private const`
                   em apps/workers (Knowledge/Execution/KnowledgeToolSetResolver.cs:20),
                   e openspec/specs/knowledge-tool-execution/spec.md não o menciona
                   — aquela spec exige nomes distintos e ordem determinística
                   "independente do nome da base", e de propósito não fixa o
                   formato. Exibi-lo faria esta tela virar a definição de fato.
                2. NÃO HÁ FONTE REUSÁVEL AQUI. O nome sai de dois passos em C#
                   (Naming/ToolNameSlugifier.cs remove diacríticos e produz
                   kebab-case; Mcp/ToolNameSanitizer.cs troca o resto e trunca em
                   64), e nenhuma rota de apps/api o devolve. Reimplementar em TS
                   seria segunda fonte de verdade — e pular um dos dois passos é o
                   que produziu `Informa__es_Gerais` no censo de nomes.
                3. O NOME CONSTRUÍDO É O PRETENDIDO, NÃO O EFETIVO. O conjunto
                   final passa por Mcp/ToolNameDeduplicator.cs, cuja precedência
                   declarada é MCP → delegação → conhecimento: a tool de
                   conhecimento é SEMPRE a renomeada. A colisão só se resolve na
                   execução, com o conjunto inteiro do agente — que o formulário
                   de UMA base não tem, nem pode ter, porque a base ainda não foi
                   vinculada a agente nenhum.

                A razão 3 é a que não caduca: nenhuma spec nova a resolve. */}
            <Box
              mt="xs"
              p="sm"
              bg="var(--buteco-surface-subtle)"
              style={{
                borderRadius: 'var(--mantine-radius-sm)',
                border: '1px solid var(--mantine-color-default-border)',
              }}
            >
              {/* O rótulo diz o que este bloco É, e não mais do que isso.
                  "Como o agente vê esta base" era verdade quando a 5a-1 o
                  escreveu, e a etapa 4 a tornou falsa: desde
                  apps/workers/.../Knowledge/Execution/KnowledgeToolDescription.cs:52
                  o modelo recebe uma cadeia montada — prefixo que nomeia a base,
                  o texto do operador, e 441 caracteres fixos de como ler o
                  resultado, iguais para toda base. O que está aqui é a parte que
                  o operador escreve, e o rótulo passa a dizer isso.

                  GATILHO: se `KnowledgeToolDescription.Build` deixar de envolver a
                  descrição, a nota abaixo vira falsa e este bloco precisa ser
                  relido. A afirmação é ESTRUTURAL de propósito — reproduzir o
                  texto fixo aqui seria segunda fonte de verdade de uma prosa que
                  vive noutro app e tem guarda própria lá. */}
              <Text
                size="xs"
                fw={600}
                tt="uppercase"
                c="dimmed"
                mb={6}
                style={{ letterSpacing: '0.05em' }}
              >
                Seu texto, dentro do que o agente recebe
              </Text>
              <Text
                size="sm"
                ff="monospace"
                c="var(--mantine-primary-color-filled)"
                data-testid="preview-name"
              >
                {form.values.name.trim() || 'nome da base'}
              </Text>
              <Text
                size="xs"
                ff="monospace"
                c="dimmed"
                mt={4}
                style={{ whiteSpace: 'pre-wrap', lineHeight: 1.6 }}
                data-testid="preview-description"
              >
                {/* "só o nome DESTA BASE", e o qualificador não é preciosismo:
                    desde a etapa 4 o modelo recebe também o bloco fixo, que é
                    igual para toda base e portanto não distingue nenhuma. Sem o
                    qualificador, esta linha e a nota logo abaixo se contradizem
                    na mesma tela. Achado lendo o protótipo contra a spec viva
                    (tasks.md, 9.5) — é o terceiro membro da família do D5, e o
                    menor: este estado é inalcançável em produção, porque a API
                    exige descrição não-vazia. */}
                {description.trim() ||
                  'Sem descrição: o modelo recebe só o nome desta base e decide no chute quando consultar.'}
              </Text>
              {/* `maw` porque isto é PROSA, e a régua da tela é PROSE_MAX_WIDTH.
                  Medido na conferência manual: sem ele a linha saía com 1058px
                  contra os 620px de todo o resto do texto corrido da tela — as
                  duas linhas mono acima ficam largas porque são preview literal
                  do que o modelo recebe, e essa é outra regra. */}
              <Text
                size="xs"
                c="dimmed"
                mt={8}
                maw={PROSE_MAX_WIDTH}
                style={{ lineHeight: 1.55 }}
                data-testid="preview-wrapping-note"
              >
                O sistema envolve este texto: antes, uma linha que nomeia a base; depois, instruções
                fixas de como ler o resultado da busca, iguais para toda base.
              </Text>
            </Box>
          </Stack>
        </Paper>

        {/* Sequenciamento, no mesmo idioma do placeholder do detalhe: diz onde os
            documentos entram e o que a base guarda, sem afirmar contagem. */}
        <Paper
          withBorder
          radius="md"
          p="sm"
          bg="var(--buteco-surface-subtle)"
          data-testid="documents-note"
        >
          <Text size="xs" c="dimmed" maw={PROSE_MAX_WIDTH} style={{ lineHeight: 1.55 }}>
            Documentos são carregados depois de criar a base, na tela de detalhe. Por enquanto só
            markdown: mídia binária não é armazenada, o que fica guardado é o texto.
          </Text>
        </Paper>

        <Group>
          <Button type="submit" loading={submitting}>
            {submitLabel}
          </Button>
          <Button type="button" variant="default" onClick={onCancel}>
            Cancelar
          </Button>
        </Group>
      </Stack>
    </form>
  );
}
