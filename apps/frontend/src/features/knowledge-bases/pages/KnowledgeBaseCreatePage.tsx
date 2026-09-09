import { useState } from 'react';
import { Stack, Title } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { useNavigate } from 'react-router';
import { BackLink } from '../../../components/layout/BackLink';
import { useCreateKnowledgeBaseMutation } from '../api/useKnowledgeBases';
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

export function KnowledgeBaseCreatePage() {
  const navigate = useNavigate();
  const mutation = useCreateKnowledgeBaseMutation();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string> | undefined>();

  const handleSubmit = (values: CreateKnowledgeBaseInput) => {
    setFieldErrors(undefined);
    mutation.mutate(values, {
      onSuccess: (knowledgeBase) => {
        notifications.show({
          color: 'green',
          title: 'Base cadastrada',
          message: `"${knowledgeBase.name}" foi cadastrada com sucesso.`,
        });
        navigate(`/knowledge-bases/${knowledgeBase.id}`);
      },
      onError: (error) => {
        const validation = fieldErrorsFrom(error);
        if (validation) {
          setFieldErrors(validation);
          return;
        }
        notifications.show({
          color: 'red',
          title: 'Erro ao cadastrar base',
          message: 'Não foi possível cadastrar a base de conhecimento. Tente novamente.',
        });
      },
    });
  };

  return (
    <Stack>
      <BackLink to="/knowledge-bases" label="Bases de conhecimento" />
      <Title order={2}>Nova base de conhecimento</Title>
      <KnowledgeBaseForm
        onSubmit={handleSubmit}
        onCancel={() => navigate('/knowledge-bases')}
        errors={fieldErrors}
        submitting={mutation.isPending}
      />
    </Stack>
  );
}
