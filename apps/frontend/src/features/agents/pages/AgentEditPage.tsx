import { useState } from 'react';
import { Alert, Group, Loader, Stack, Text, Title } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { useNavigate, useParams } from 'react-router';
import { useAgentQuery, useUpdateAgentMutation } from '../api/useAgents';
import { AgentForm } from '../components/AgentForm';
import { ApiError } from '../api/agentsApi';
import type { CreateAgentInput } from '../types/agent';

function fieldErrorsFrom(error: unknown): Record<string, string> | undefined {
  if (error instanceof ApiError && error.status === 400 && error.problem?.errors) {
    return Object.fromEntries(
      Object.entries(error.problem.errors).map(([field, messages]) => [field, messages[0]]),
    );
  }
  return undefined;
}

export function AgentEditPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { data, isLoading, error } = useAgentQuery(id!);
  const mutation = useUpdateAgentMutation();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string> | undefined>();

  const handleSubmit = (values: CreateAgentInput) => {
    setFieldErrors(undefined);
    mutation.mutate(
      { id: id!, input: values },
      {
        onSuccess: (agent) => {
          notifications.show({
            color: 'green',
            title: 'Agente atualizado',
            message: `"${agent.name}" foi atualizado com sucesso.`,
          });
          navigate(`/agents/${agent.id}`);
        },
        onError: (mutationError) => {
          const errors = fieldErrorsFrom(mutationError);
          if (errors) {
            setFieldErrors(errors);
            return;
          }
          notifications.show({
            color: 'red',
            title: 'Erro ao atualizar agente',
            message: 'Não foi possível atualizar o agente. Tente novamente.',
          });
        },
      },
    );
  };

  if (isLoading) {
    return (
      <Group>
        <Loader size="sm" />
        <Text>Carregando agente...</Text>
      </Group>
    );
  }

  if (error instanceof ApiError && error.status === 404) {
    return <Alert color="red">Agente não encontrado.</Alert>;
  }

  if (error || !data) {
    return <Alert color="red">Não foi possível carregar o agente.</Alert>;
  }

  return (
    <Stack>
      <Title order={2}>Editar agente</Title>
      <AgentForm
        onSubmit={handleSubmit}
        onCancel={() => navigate(`/agents/${id}`)}
        errors={fieldErrors}
        submitting={mutation.isPending}
        initialValues={{ name: data.name, instructions: data.instructions }}
        submitLabel="Salvar alterações"
      />
    </Stack>
  );
}
