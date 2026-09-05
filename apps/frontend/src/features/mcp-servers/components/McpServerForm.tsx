import { useState } from 'react';
import { Button, Group, PasswordInput, Select, Stack, Text, Textarea, TextInput } from '@mantine/core';
import { useForm } from '@mantine/form';
import { ApiError } from '../api/mcpServersApi';
import {
  useTestSavedMcpServerConnectionMutation,
  useTestUnsavedMcpServerConnectionMutation,
} from '../api/useMcpServers';
import { ConnectionTestResultAlert, type ConnectionTestResult } from './ConnectionTestResultAlert';
import type { CreateMcpServerInput, McpServerAuthType } from '../types/mcpServer';

interface McpServerFormValues {
  name: string;
  description: string;
  url: string;
  authType: McpServerAuthType;
  credential: string;
}

interface McpServerFormProps {
  onSubmit: (values: CreateMcpServerInput) => void;
  onCancel: () => void;
  errors?: Record<string, string>;
  submitting?: boolean;
  initialValues?: CreateMcpServerInput;
  submitLabel?: string;
  mode?: 'create' | 'edit';
  // Necessário para testar a conexão do servidor já salvo, no caso em que
  // a credencial fica em branco durante a edição (Decision 6 do design.md
  // da change frontend-mcp-servidor-uso-e-diagnostico).
  mcpServerId?: string;
}

interface TestedConnectionConfig {
  url: string;
  authType: McpServerAuthType;
  credential: string;
}

const authTypeOptions: { value: McpServerAuthType; label: string }[] = [
  { value: 'None', label: 'Nenhuma' },
  { value: 'BearerToken', label: 'Bearer Token' },
];

function toFormValues(input?: CreateMcpServerInput): McpServerFormValues {
  return {
    name: input?.name ?? '',
    description: input?.description ?? '',
    url: input?.url ?? '',
    authType: input?.authType ?? 'None',
    credential: '',
  };
}

export function McpServerForm({
  onSubmit,
  onCancel,
  errors,
  submitting,
  initialValues,
  submitLabel = 'Cadastrar servidor',
  mode = 'create',
  mcpServerId,
}: McpServerFormProps) {
  const isEditing = mode === 'edit';
  const form = useForm<McpServerFormValues>({
    initialValues: toFormValues(initialValues),
    validate: {
      name: (value) => (value.trim() ? null : 'O nome do servidor MCP é obrigatório.'),
      url: (value) => (value.trim() ? null : 'A URL do servidor MCP é obrigatória.'),
      credential: (value, values) =>
        !isEditing && values.authType !== 'None' && !value.trim()
          ? 'A credencial é obrigatória para o tipo de autenticação informado.'
          : null,
    },
  });

  const [testState, setTestState] = useState<
    { config: TestedConnectionConfig; result: ConnectionTestResult } | undefined
  >();
  const testMutation = useTestUnsavedMcpServerConnectionMutation();
  const testSavedMutation = useTestSavedMcpServerConnectionMutation();

  // Em edição, credencial em branco significa "manter a credencial atual".
  // Testar a configuração digitada nesse caso mandaria uma requisição sem
  // credencial nenhuma e falharia por um motivo que não é o do servidor
  // salvo — pior do que não ter teste (Decision 6 do design.md).
  const usesSavedCredential = isEditing && !!mcpServerId && !form.values.credential.trim();

  const currentConfig: TestedConnectionConfig = {
    url: form.values.url,
    authType: form.values.authType,
    credential: form.values.credential,
  };
  const testResult =
    testState &&
    testState.config.url === currentConfig.url &&
    testState.config.authType === currentConfig.authType &&
    testState.config.credential === currentConfig.credential
      ? testState.result
      : undefined;

  const handleTestConnection = () => {
    const config = currentConfig;

    if (!config.url.trim()) {
      form.setFieldError('url', 'Informe a Url antes de testar.');
      return;
    }

    if (usesSavedCredential) {
      testSavedMutation.mutate(mcpServerId!, {
        onSuccess: (result) => {
          setTestState({
            config,
            result: {
              success: result.success,
              failureReason: result.failureReason,
              message: result.message,
              testedAt: new Date(),
            },
          });
        },
        onError: () => {
          setTestState({
            config,
            result: {
              success: false,
              failureReason: null,
              message: 'Não foi possível testar a conexão. Tente novamente.',
              testedAt: new Date(),
            },
          });
        },
      });
      return;
    }

    testMutation.mutate(
      {
        url: config.url,
        authType: config.authType,
        credential: config.credential || undefined,
      },
      {
        onSuccess: (result) => {
          setTestState({
            config,
            result: {
              success: result.success,
              failureReason: result.failureReason,
              message: result.message,
              testedAt: new Date(),
            },
          });
        },
        onError: (error) => {
          if (error instanceof ApiError && error.status === 400 && error.problem?.errors) {
            const message = Object.values(error.problem.errors)
              .map((messages) => messages[0])
              .join(' ');
            setTestState({
              config,
              result: { success: false, failureReason: null, message, testedAt: new Date() },
            });
            return;
          }
          setTestState({
            config,
            result: {
              success: false,
              failureReason: null,
              message: 'Não foi possível testar a conexão. Tente novamente.',
              testedAt: new Date(),
            },
          });
        },
      },
    );
  };

  const handleSubmit = (values: McpServerFormValues) => {
    onSubmit({
      name: values.name,
      description: values.description,
      url: values.url,
      authType: values.authType,
      credential:
        values.authType === 'None' || !values.credential.trim() ? undefined : values.credential,
    });
  };

  return (
    <form onSubmit={form.onSubmit(handleSubmit)}>
      <Stack>
        <TextInput
          label="Nome"
          placeholder="Nome do servidor MCP"
          withAsterisk
          {...form.getInputProps('name')}
          error={errors?.name ?? form.errors.name}
        />
        <Textarea
          label="Descrição"
          placeholder="Descrição do servidor MCP"
          rows={4}
          resize="vertical"
          {...form.getInputProps('description')}
          error={errors?.description ?? form.errors.description}
        />
        <TextInput
          label="Url"
          placeholder="https://mcp.exemplo.com/sse"
          withAsterisk
          {...form.getInputProps('url')}
          error={errors?.url ?? form.errors.url}
        />
        <Select
          label="Tipo de autenticação"
          withAsterisk
          data={authTypeOptions}
          value={form.values.authType}
          onChange={(value) =>
            form.setFieldValue('authType', (value as McpServerAuthType) ?? 'None')
          }
          error={errors?.authType ?? form.errors.authType}
        />
        {form.values.authType !== 'None' && (
          <PasswordInput
            label="Credencial"
            placeholder={
              isEditing ? 'Deixe em branco para manter a credencial atual' : 'Credencial de acesso'
            }
            description={
              isEditing
                ? 'Deixe em branco para manter a credencial atual.'
                : 'Enviada cifrada (AES-GCM) e nunca retorna na API.'
            }
            withAsterisk={!isEditing}
            {...form.getInputProps('credential')}
            error={errors?.credential ?? form.errors.credential}
          />
        )}
        {usesSavedCredential && (
          <Text size="xs" c="dimmed" data-testid="saved-credential-test-note">
            Sem digitar a credencial, o teste usa a credencial salva deste servidor.
          </Text>
        )}
        <ConnectionTestResultAlert result={testResult} />
        <Group>
          <Button type="submit" loading={submitting}>
            {submitLabel}
          </Button>
          <Button type="button" variant="default" onClick={onCancel}>
            Cancelar
          </Button>
          <Button
            type="button"
            variant="outline"
            onClick={handleTestConnection}
            loading={testMutation.isPending || testSavedMutation.isPending}
          >
            Testar conexão
          </Button>
        </Group>
      </Stack>
    </form>
  );
}
