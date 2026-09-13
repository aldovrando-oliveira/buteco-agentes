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

// Abaixo disto a descrição raramente diz ao modelo quando consultar a base. É
// aviso, não bloqueio.
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
            <Text size="xs" c="dimmed" maw={PROSE_MAX_WIDTH} style={{ lineHeight: 1.55 }}>
              Não é um resumo para o operador. Durante a conversa, o agente vê apenas o nome e este
              texto, e decide a partir deles se vale abrir a base. Diga{' '}
              <strong>que assunto está aqui</strong> e <strong>em que situação consultar</strong>.
              Evite “documentos diversos”, “materiais da equipe” e afins.
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
                a cor, não a presença do contador. */}
            <Text size="xs" c={isShort ? 'yellow' : 'dimmed'} data-testid="description-char-count">
              {description.length} {description.length === 1 ? 'caractere' : 'caracteres'}
              {isShort && ' · curto demais para o modelo decidir com segurança'}
            </Text>

            {/* O protótipo mostra aqui o identificador `consultar_base`. Ele NÃO
                é implementado: nome de tool de base de conhecimento é decisão da
                etapa 4 (resolvedor de tool) e passa pelo ToolNameDeduplicator —
                não existe em spec nenhuma hoje. Exibi-lo afirmaria um nome que o
                sistema não definiu (convenção 13, design.md D19). O que é
                verdade, e fica: o modelo recebe o nome e esta descrição. */}
            <Box
              mt="xs"
              p="sm"
              bg="var(--buteco-surface-subtle)"
              style={{
                borderRadius: 'var(--mantine-radius-sm)',
                border: '1px solid var(--mantine-color-default-border)',
              }}
            >
              <Text
                size="xs"
                fw={600}
                tt="uppercase"
                c="dimmed"
                mb={6}
                style={{ letterSpacing: '0.05em' }}
              >
                Como o agente vê esta base
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
                {description.trim() ||
                  'Sem descrição: o modelo recebe só o nome e decide no chute quando consultar.'}
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
