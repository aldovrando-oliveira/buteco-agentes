import { useState } from 'react';
import { Alert, Group, Loader, Stack, Text, Title } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { useNavigate } from 'react-router';
import { useAgentsQuery } from '../../agents/api/useAgents';
import { useCreateChannelMutation } from '../api/useChannels';
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

export function ChannelCreatePage() {
  const navigate = useNavigate();
  const agentsQuery = useAgentsQuery();
  const mutation = useCreateChannelMutation();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string> | undefined>();
  const [credentialErrors, setCredentialErrors] = useState<string[] | undefined>();
  const [submitError, setSubmitError] = useState<{ title: string; detail?: string } | undefined>();

  const handleSubmit = (values: ChannelFormSubmitValues) => {
    setFieldErrors(undefined);
    setCredentialErrors(undefined);
    setSubmitError(undefined);
    mutation.mutate(
      {
        name: values.name,
        agentId: values.agentId,
        channelType: values.channelType,
        credential: values.credential!,
      },
      {
        onSuccess: (channel) => {
          notifications.show({
            color: 'green',
            title: 'Canal cadastrado',
            message: `"${channel.name}" foi cadastrado com sucesso.`,
          });
          navigate(`/channels/${channel.id}`);
        },
        onError: (error) => {
          const parsed = fieldErrorsFrom(error);
          if (parsed) {
            setFieldErrors(parsed.fieldErrors);
            setCredentialErrors(parsed.credentialErrors);
            return;
          }
          if (error instanceof ApiError && error.status === 502) {
            setSubmitError({
              title: error.problem?.title ?? error.message,
              detail: error.problem?.detail,
            });
            return;
          }
          notifications.show({
            color: 'red',
            title: 'Erro ao cadastrar canal',
            message: 'Não foi possível cadastrar o canal. Tente novamente.',
          });
        },
      },
    );
  };

  if (agentsQuery.isLoading) {
    return (
      <Group>
        <Loader size="sm" />
        <Text>Carregando agentes...</Text>
      </Group>
    );
  }

  if (agentsQuery.isError) {
    return <Alert color="red">Não foi possível carregar os agentes.</Alert>;
  }

  return (
    <Stack>
      <Title order={2}>Novo canal</Title>
      {submitError && (
        <Alert color="red" title={submitError.title}>
          {submitError.detail ?? 'Não foi possível configurar o canal. Tente novamente.'}
        </Alert>
      )}
      <ChannelForm
        onSubmit={handleSubmit}
        onCancel={() => navigate('/channels')}
        agents={agentsQuery.data ?? []}
        errors={fieldErrors}
        credentialErrors={credentialErrors}
        submitting={mutation.isPending}
      />
    </Stack>
  );
}
