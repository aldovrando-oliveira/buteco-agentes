import { useState } from 'react';
import { Alert, Group, Loader, Stack, Text, Title } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { useNavigate, useParams } from 'react-router';
import { useMcpServerQuery, useUpdateMcpServerMutation } from '../api/useMcpServers';
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

export function McpServerEditPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { data, isLoading, error } = useMcpServerQuery(id!);
  const mutation = useUpdateMcpServerMutation();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string> | undefined>();

  const handleSubmit = (values: CreateMcpServerInput) => {
    setFieldErrors(undefined);
    mutation.mutate(
      { id: id!, input: values },
      {
        onSuccess: (mcpServer) => {
          notifications.show({
            color: 'green',
            title: 'Servidor MCP atualizado',
            message: `"${mcpServer.name}" foi atualizado com sucesso.`,
          });
          navigate(`/mcp-servers/${mcpServer.id}`);
        },
        onError: (mutationError) => {
          const errors = fieldErrorsFrom(mutationError);
          if (errors) {
            setFieldErrors(errors);
            return;
          }
          notifications.show({
            color: 'red',
            title: 'Erro ao atualizar servidor MCP',
            message: 'Não foi possível atualizar o servidor MCP. Tente novamente.',
          });
        },
      },
    );
  };

  if (isLoading) {
    return (
      <Group>
        <Loader size="sm" />
        <Text>Carregando servidor MCP...</Text>
      </Group>
    );
  }

  if (error instanceof ApiError && error.status === 404) {
    return <Alert color="red">Servidor MCP não encontrado.</Alert>;
  }

  if (error || !data) {
    return <Alert color="red">Não foi possível carregar o servidor MCP.</Alert>;
  }

  return (
    <Stack>
      <Title order={2}>Editar servidor MCP</Title>
      <McpServerForm
        onSubmit={handleSubmit}
        onCancel={() => navigate(`/mcp-servers/${id}`)}
        errors={fieldErrors}
        submitting={mutation.isPending}
        mode="edit"
        mcpServerId={data.id}
        submitLabel="Salvar alterações"
        initialValues={{
          name: data.name,
          description: data.description,
          url: data.url,
          authType: data.authType,
        }}
      />
    </Stack>
  );
}
