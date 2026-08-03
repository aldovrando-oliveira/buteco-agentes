import { useState } from 'react';
import { Button, Group, PasswordInput, Select, Stack, Textarea, TextInput } from '@mantine/core';
import { useForm } from '@mantine/form';
import { ApiError } from '../api/mcpServersApi';
import { useTestUnsavedMcpServerConnectionMutation } from '../api/useMcpServers';
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
    testMutation.mutate(
      {
        url: config.url,
        authType: config.authType,
        credential: config.credential || undefined,
      },
      {
        onSuccess: (result) => {
          setTestState({ config, result: { success: result.success, message: result.message } });
        },
        onError: (error) => {
          if (error instanceof ApiError && error.status === 400 && error.problem?.errors) {
            const message = Object.values(error.problem.errors)
              .map((messages) => messages[0])
              .join(' ');
            setTestState({ config, result: { success: false, message } });
            return;
          }
          setTestState({
            config,
            result: {
              success: false,
              message: 'Não foi possível testar a conexão. Tente novamente.',
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
            withAsterisk={!isEditing}
            {...form.getInputProps('credential')}
            error={errors?.credential ?? form.errors.credential}
          />
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
            loading={testMutation.isPending}
          >
            Testar conexão
          </Button>
        </Group>
      </Stack>
    </form>
  );
}
