import { useState } from 'react';
import { Alert, Group, Loader, Stack, Text, Title } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { useNavigate } from 'react-router';
import { useCreateAgentMutation } from '../api/useAgents';
import { useProvidersQuery } from '../api/useProviders';
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

export function AgentCreatePage() {
  const navigate = useNavigate();
  const mutation = useCreateAgentMutation();
  const providersQuery = useProvidersQuery();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string> | undefined>();

  const handleSubmit = (values: CreateAgentInput) => {
    setFieldErrors(undefined);
    mutation.mutate(values, {
      onSuccess: (agent) => {
        notifications.show({
          color: 'green',
          title: 'Agente criado',
          message: `"${agent.name}" foi cadastrado com sucesso.`,
        });
        navigate(`/agents/${agent.id}`);
      },
      onError: (error) => {
        const errors = fieldErrorsFrom(error);
        if (errors) {
          setFieldErrors(errors);
          return;
        }
        notifications.show({
          color: 'red',
          title: 'Erro ao criar agente',
          message: 'Não foi possível cadastrar o agente. Tente novamente.',
        });
      },
    });
  };

  if (providersQuery.isLoading) {
    return (
      <Group>
        <Loader size="sm" />
        <Text>Carregando provedores...</Text>
      </Group>
    );
  }

  if (providersQuery.isError) {
    return <Alert color="red">Não foi possível carregar os provedores de LLM.</Alert>;
  }

  if (providersQuery.data?.length === 0) {
    return (
      <Alert color="yellow">
        Nenhum provedor de LLM configurado. Configure ao menos uma variável de ambiente antes de
        cadastrar agentes.
      </Alert>
    );
  }

  return (
    <Stack>
      <Title order={2}>Novo agente</Title>
      <AgentForm
        onSubmit={handleSubmit}
        onCancel={() => navigate('/agents')}
        errors={fieldErrors}
        submitting={mutation.isPending}
        providers={providersQuery.data ?? []}
      />
    </Stack>
  );
}
