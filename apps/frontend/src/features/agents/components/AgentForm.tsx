import { Button, Group, Select, Stack, Textarea, TextInput } from '@mantine/core';
import { useForm } from '@mantine/form';
import type { CreateAgentInput, ProviderCatalogEntry } from '../types/agent';

interface AgentFormProps {
  onSubmit: (values: CreateAgentInput) => void;
  onCancel: () => void;
  providers: ProviderCatalogEntry[];
  errors?: Record<string, string>;
  submitting?: boolean;
  initialValues?: CreateAgentInput;
  submitLabel?: string;
}

interface SelectOption {
  value: string;
  label: string;
  disabled?: boolean;
}

// Injeta uma opção sintética desabilitada quando o valor atual não consta
// nas opções disponíveis (provider/model removido do ambiente depois do
// cadastro do agente) — informativo, mas não re-selecionável. Ver Decision
// 2 do design.md da change frontend-selecao-provider-modelo.
function withStaleOption(options: SelectOption[], currentValue: string): SelectOption[] {
  if (currentValue && !options.some((option) => option.value === currentValue)) {
    return [
      ...options,
      { value: currentValue, label: `${currentValue} (indisponível)`, disabled: true },
    ];
  }
  return options;
}

export function AgentForm({
  onSubmit,
  onCancel,
  providers,
  errors,
  submitting,
  initialValues,
  submitLabel = 'Criar agente',
}: AgentFormProps) {
  const form = useForm<CreateAgentInput>({
    initialValues: initialValues ?? { name: '', instructions: '', provider: '', model: '' },
    validate: {
      name: (value) => (value.trim() ? null : 'O nome do agente é obrigatório.'),
      instructions: (value) =>
        value.trim() ? null : 'As instruções (system prompt) do agente são obrigatórias.',
      provider: (value) => (value.trim() ? null : 'O provedor de LLM do agente é obrigatório.'),
      model: (value) => (value.trim() ? null : 'O modelo do agente é obrigatório.'),
    },
  });

  const providerOptions = withStaleOption(
    providers.map((provider) => ({ value: provider.id, label: provider.id })),
    form.values.provider,
  );

  const selectedProvider = providers.find((provider) => provider.id === form.values.provider);
  const modelOptions = withStaleOption(
    (selectedProvider?.models ?? []).map((model) => ({ value: model, label: model })),
    form.values.model,
  );

  return (
    <form onSubmit={form.onSubmit((values) => onSubmit(values))}>
      <Stack>
        <TextInput
          label="Nome"
          placeholder="Nome do agente"
          withAsterisk
          {...form.getInputProps('name')}
          error={errors?.name ?? form.errors.name}
        />
        <Textarea
          label="Instruções"
          placeholder="System prompt do agente"
          withAsterisk
          rows={12}
          resize="vertical"
          {...form.getInputProps('instructions')}
          error={errors?.instructions ?? form.errors.instructions}
        />
        <Select
          label="Provedor"
          placeholder="Selecione o provedor de LLM"
          withAsterisk
          data={providerOptions}
          value={form.values.provider || null}
          onChange={(value) => {
            form.setFieldValue('provider', value ?? '');
            form.setFieldValue('model', '');
          }}
          error={errors?.provider ?? form.errors.provider}
        />
        <Select
          label="Modelo"
          placeholder="Selecione o modelo"
          withAsterisk
          data={modelOptions}
          value={form.values.model || null}
          onChange={(value) => form.setFieldValue('model', value ?? '')}
          error={errors?.model ?? form.errors.model}
        />
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
