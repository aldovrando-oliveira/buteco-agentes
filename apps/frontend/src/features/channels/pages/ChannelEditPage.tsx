import { useState } from 'react';
import { Alert, Group, Loader, Stack, Text, Title } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { useNavigate, useParams } from 'react-router';
import { useAgentsQuery } from '../../agents/api/useAgents';
import { useChannelQuery, useUpdateChannelMutation } from '../api/useChannels';
import { ChannelForm, type ChannelFormSubmitValues } from '../components/ChannelForm';
import { ApiError } from '../api/channelsApi';

interface ParsedFieldErrors {
  fieldErrors?: Record<string, string>;
  credentialErrors?: string[];
}

function fieldErrorsFrom(error: unknown): ParsedFieldErrors | undefined {
  if (error instanceof ApiError && error.status === 400 && error.problem?.errors) {
    const { credential, ...rest } = error.problem.errors;
    return {
      fieldErrors: Object.fromEntries(
        Object.entries(rest).map(([field, messages]) => [field, messages[0]]),
      ),
      credentialErrors: credential,
    };
  }
  return undefined;
}

export function ChannelEditPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { data, isLoading, error } = useChannelQuery(id!);
  const agentsQuery = useAgentsQuery();
  const mutation = useUpdateChannelMutation();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string> | undefined>();
  const [credentialErrors, setCredentialErrors] = useState<string[] | undefined>();
  const [submitError, setSubmitError] = useState<{ title: string; detail?: string } | undefined>();

  const handleSubmit = (values: ChannelFormSubmitValues) => {
    setFieldErrors(undefined);
    setCredentialErrors(undefined);
    setSubmitError(undefined);
    mutation.mutate(
      {
        id: id!,
        input: { name: values.name, agentId: values.agentId, credential: values.credential },
      },
      {
        onSuccess: (channel) => {
          notifications.show({
            color: 'green',
            title: 'Canal atualizado',
            message: `"${channel.name}" foi atualizado com sucesso.`,
          });
          navigate(`/channels/${channel.id}`);
        },
        onError: (mutationError) => {
          const parsed = fieldErrorsFrom(mutationError);
          if (parsed) {
            setFieldErrors(parsed.fieldErrors);
            setCredentialErrors(parsed.credentialErrors);
            return;
          }
          if (mutationError instanceof ApiError && mutationError.status === 502) {
            setSubmitError({
              title: mutationError.problem?.title ?? mutationError.message,
              detail: mutationError.problem?.detail,
            });
            return;
          }
          notifications.show({
            color: 'red',
            title: 'Erro ao atualizar canal',
            message: 'Não foi possível atualizar o canal. Tente novamente.',
          });
        },
      },
    );
  };

  if (isLoading || agentsQuery.isLoading) {
    return (
      <Group>
        <Loader size="sm" />
        <Text>Carregando canal...</Text>
      </Group>
    );
  }

  if (error instanceof ApiError && error.status === 404) {
    return <Alert color="red">Canal não encontrado.</Alert>;
  }

  if (error || !data) {
    return <Alert color="red">Não foi possível carregar o canal.</Alert>;
  }

  if (agentsQuery.isError) {
    return <Alert color="red">Não foi possível carregar os agentes.</Alert>;
  }

  return (
    <Stack>
      <Title order={2}>Editar canal</Title>
      {submitError && (
        <Alert color="red" title={submitError.title}>
          {submitError.detail ?? 'Não foi possível atualizar o canal. Tente novamente.'}
        </Alert>
      )}
      <ChannelForm
        onSubmit={handleSubmit}
        onCancel={() => navigate(`/channels/${id}`)}
        agents={agentsQuery.data ?? []}
        errors={fieldErrors}
        credentialErrors={credentialErrors}
        submitting={mutation.isPending}
        mode="edit"
        submitLabel="Salvar alterações"
        initialValues={{ name: data.name, agentId: data.agentId, channelType: data.channelType }}
      />
    </Stack>
  );
}
