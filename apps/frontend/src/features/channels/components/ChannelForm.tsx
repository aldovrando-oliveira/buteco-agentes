import { Alert, Button, Group, List, Select, Stack, TextInput } from '@mantine/core';
import { useForm } from '@mantine/form';
import { WahaCredentialFields } from './WahaCredentialFields';
import { TelegramCredentialFields } from './TelegramCredentialFields';
import type { Agent } from '../../agents/types/agent';
import type { ChannelFormValues, ChannelType } from '../types/channel';

const channelTypeOptions: { value: ChannelType; label: string }[] = [
  { value: 'waha', label: 'WAHA' },
  { value: 'telegram', label: 'Telegram' },
];

const channelTypeLabels: Record<ChannelType, string> = {
  waha: 'WAHA',
  telegram: 'Telegram',
};

export interface ChannelFormInitialValues {
  name: string;
  agentId: string;
  channelType: ChannelType;
}

export interface ChannelFormSubmitValues {
  name: string;
  agentId: string;
  channelType: ChannelType;
  credential?: string;
}

interface ChannelFormProps {
  onSubmit: (values: ChannelFormSubmitValues) => void;
  onCancel: () => void;
  agents: Agent[];
  errors?: Record<string, string>;
  credentialErrors?: string[];
  submitting?: boolean;
  initialValues?: ChannelFormInitialValues;
  submitLabel?: string;
  mode?: 'create' | 'edit';
}

function toFormValues(initialValues?: ChannelFormInitialValues): ChannelFormValues {
  return {
    name: initialValues?.name ?? '',
    agentId: initialValues?.agentId ?? '',
    channelType: initialValues?.channelType ?? 'waha',
    // Credencial é write-only — mesmo em edição, nunca pré-preenchida
    // (design.md, Decision 2, mesmo princípio de McpServerForm).
    waha: { serviceUrl: '', sessionName: '', authToken: '' },
    telegram: { botToken: '' },
  };
}

// Regra tudo-ou-nada (design.md, Decision 2): em edição, todos os campos
// vazios preserva a credencial persistida; qualquer campo preenchido exige
// todos os campos obrigatórios do tipo antes de habilitar o submit. Em
// criação, sempre obrigatório.
function validateChannelForm(values: ChannelFormValues, isEditing: boolean) {
  const errors: Record<string, string> = {};

  if (!values.name.trim()) {
    errors.name = 'O nome do canal é obrigatório.';
  }
  if (!values.agentId) {
    errors.agentId = 'O agente responsável pelo canal é obrigatório.';
  }

  if (values.channelType === 'waha') {
    const { serviceUrl, sessionName, authToken } = values.waha;
    const anyFilled = Boolean(serviceUrl.trim() || sessionName.trim() || authToken.trim());
    if (!isEditing || anyFilled) {
      if (!serviceUrl.trim()) errors['waha.serviceUrl'] = 'A URL do serviço é obrigatória.';
      if (!sessionName.trim()) errors['waha.sessionName'] = 'O nome da sessão é obrigatório.';
      if (!authToken.trim()) errors['waha.authToken'] = 'O token de autenticação é obrigatório.';
    }
  } else {
    const { botToken } = values.telegram;
    const anyFilled = Boolean(botToken.trim());
    if (!isEditing || anyFilled) {
      if (!botToken.trim()) errors['telegram.botToken'] = 'O token do bot é obrigatório.';
    }
  }

  return errors;
}

// Chaves em PascalCase, não camelCase — o validador do adapter em
// apps/inbox desserializa este JSON com JsonSerializer.Deserialize<T>
// sem PropertyNameCaseInsensitive (diferente dos parsers de webhook
// externo, que optam por isso de propósito), então precisa bater
// exatamente com as propriedades de WahaCredential/TelegramCredential.
function buildCredential(values: ChannelFormValues): string | undefined {
  if (values.channelType === 'waha') {
    const { serviceUrl, sessionName, authToken } = values.waha;
    if (!serviceUrl.trim() && !sessionName.trim() && !authToken.trim()) {
      return undefined;
    }
    return JSON.stringify({
      ServiceUrl: serviceUrl,
      SessionName: sessionName,
      AuthToken: authToken,
    });
  }

  const { botToken } = values.telegram;
  if (!botToken.trim()) {
    return undefined;
  }
  return JSON.stringify({ BotToken: botToken });
}

function agentOptions(agents: Agent[]) {
  return agents.map((agent) => ({
    value: agent.id,
    label: agent.isActive ? agent.name : `${agent.name} (inativo)`,
  }));
}

export function ChannelForm({
  onSubmit,
  onCancel,
  agents,
  errors,
  credentialErrors,
  submitting,
  initialValues,
  submitLabel = 'Cadastrar canal',
  mode = 'create',
}: ChannelFormProps) {
  const isEditing = mode === 'edit';
  const form = useForm<ChannelFormValues>({
    initialValues: toFormValues(initialValues),
    validate: (values) => validateChannelForm(values, isEditing),
  });

  const handleSubmit = (values: ChannelFormValues) => {
    onSubmit({
      name: values.name,
      agentId: values.agentId,
      channelType: values.channelType,
      credential: buildCredential(values),
    });
  };

  return (
    <form onSubmit={form.onSubmit(handleSubmit)}>
      <Stack>
        <TextInput
          label="Nome"
          placeholder="Nome do canal"
          withAsterisk
          {...form.getInputProps('name')}
          error={errors?.name ?? form.errors.name}
        />
        <Select
          label="Agente responsável"
          placeholder="Selecione o agente responsável"
          withAsterisk
          data={agentOptions(agents)}
          value={form.values.agentId || null}
          onChange={(value) => form.setFieldValue('agentId', value ?? '')}
          error={errors?.agentId ?? form.errors.agentId}
        />

        {isEditing ? (
          // Tipo de canal não é editável — UpdateChannelRequest nem tem esse
          // campo (design.md, Decision 2). Texto fixo, não um Select.
          <TextInput
            label="Tipo de canal"
            value={channelTypeLabels[form.values.channelType]}
            disabled
          />
        ) : (
          <Select
            label="Tipo de canal"
            withAsterisk
            data={channelTypeOptions}
            value={form.values.channelType}
            onChange={(value) => {
              const next = (value as ChannelType) ?? 'waha';
              form.setFieldValue('channelType', next);
              form.setFieldValue('waha', { serviceUrl: '', sessionName: '', authToken: '' });
              form.setFieldValue('telegram', { botToken: '' });
            }}
            error={errors?.channelType}
          />
        )}

        {form.values.channelType === 'waha' ? (
          <WahaCredentialFields form={form} isEditing={isEditing} />
        ) : (
          <TelegramCredentialFields form={form} isEditing={isEditing} />
        )}

        {credentialErrors && credentialErrors.length > 0 && (
          <Alert color="red" title="Erro na credencial">
            <List size="sm">
              {credentialErrors.map((message) => (
                <List.Item key={message}>{message}</List.Item>
              ))}
            </List>
          </Alert>
        )}

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
