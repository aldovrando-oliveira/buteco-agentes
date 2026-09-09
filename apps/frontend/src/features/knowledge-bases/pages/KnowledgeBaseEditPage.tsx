import { useState } from 'react';
import { Alert, Group, Loader, Stack, Text, Title } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { useNavigate, useParams } from 'react-router';
import { BackLink } from '../../../components/layout/BackLink';
import {
  useKnowledgeBaseQuery,
  useUpdateKnowledgeBaseMutation,
} from '../api/useKnowledgeBases';
import { KnowledgeBaseForm } from '../components/KnowledgeBaseForm';
import { ApiError } from '../api/knowledgeBasesApi';
import type { CreateKnowledgeBaseInput } from '../types/knowledgeBase';

function fieldErrorsFrom(error: unknown): Record<string, string> | undefined {
  if (error instanceof ApiError && error.status === 400 && error.problem?.errors) {
    return Object.fromEntries(
      Object.entries(error.problem.errors).map(([field, messages]) => [field, messages[0]]),
    );
  }
  return undefined;
}

export function KnowledgeBaseEditPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { data, isLoading, error } = useKnowledgeBaseQuery(id!);
  const mutation = useUpdateKnowledgeBaseMutation();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string> | undefined>();

  const handleSubmit = (values: CreateKnowledgeBaseInput) => {
    setFieldErrors(undefined);
    mutation.mutate(
      { id: id!, input: values },
      {
        onSuccess: (knowledgeBase) => {
          notifications.show({
            color: 'green',
            title: 'Base atualizada',
            message: `"${knowledgeBase.name}" foi atualizada com sucesso.`,
          });
          navigate(`/knowledge-bases/${knowledgeBase.id}`);
        },
        onError: (mutationError) => {
          const validation = fieldErrorsFrom(mutationError);
          if (validation) {
            setFieldErrors(validation);
            return;
          }
          notifications.show({
            color: 'red',
            title: 'Erro ao atualizar base',
            message: 'Não foi possível atualizar a base de conhecimento. Tente novamente.',
          });
        },
      },
    );
  };

  if (isLoading) {
    return (
      <Group>
        <Loader size="sm" />
        <Text>Carregando base de conhecimento...</Text>
      </Group>
    );
  }

  if (error instanceof ApiError && error.status === 404) {
    return <Alert color="red">Base de conhecimento não encontrada.</Alert>;
  }

  if (error || !data) {
    return <Alert color="red">Não foi possível carregar a base de conhecimento.</Alert>;
  }

  return (
    <Stack>
      <BackLink to={`/knowledge-bases/${id}`} label="Voltar à base" />
      <Title order={2}>Editar base de conhecimento</Title>
      <KnowledgeBaseForm
        onSubmit={handleSubmit}
        onCancel={() => navigate(`/knowledge-bases/${id}`)}
        errors={fieldErrors}
        submitting={mutation.isPending}
        submitLabel="Salvar alterações"
        initialValues={{ name: data.name, description: data.description }}
      />
    </Stack>
  );
}
