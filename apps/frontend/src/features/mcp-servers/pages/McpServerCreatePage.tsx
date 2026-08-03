import { useState } from 'react';
import { Stack, Title } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { useNavigate } from 'react-router';
import { useCreateMcpServerMutation } from '../api/useMcpServers';
import { McpServerForm } from '../components/McpServerForm';
import { ApiError } from '../api/mcpServersApi';
import type { CreateMcpServerInput } from '../types/mcpServer';

function fieldErrorsFrom(error: unknown): Record<string, string> | undefined {
  if (error instanceof ApiError && error.status === 400 && error.problem?.errors) {
    return Object.fromEntries(
      Object.entries(error.problem.errors).map(([field, messages]) => [field, messages[0]]),
    );
  }
  return undefined;
}

export function McpServerCreatePage() {
  const navigate = useNavigate();
  const mutation = useCreateMcpServerMutation();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string> | undefined>();

  const handleSubmit = (values: CreateMcpServerInput) => {
    setFieldErrors(undefined);
    mutation.mutate(values, {
      onSuccess: (mcpServer) => {
        notifications.show({
          color: 'green',
          title: 'Servidor MCP cadastrado',
          message: `"${mcpServer.name}" foi cadastrado com sucesso.`,
        });
        navigate(`/mcp-servers/${mcpServer.id}`);
      },
      onError: (error) => {
        const errors = fieldErrorsFrom(error);
        if (errors) {
          setFieldErrors(errors);
          return;
        }
        notifications.show({
          color: 'red',
          title: 'Erro ao cadastrar servidor MCP',
          message: 'Não foi possível cadastrar o servidor MCP. Tente novamente.',
        });
      },
    });
  };

  return (
    <Stack>
      <Title order={2}>Novo servidor MCP</Title>
      <McpServerForm
        onSubmit={handleSubmit}
        onCancel={() => navigate('/mcp-servers')}
        errors={fieldErrors}
        submitting={mutation.isPending}
      />
    </Stack>
  );
}
