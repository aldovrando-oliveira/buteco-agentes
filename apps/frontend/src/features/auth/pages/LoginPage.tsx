import { useState } from 'react';
import { Center, Paper, Stack, Title } from '@mantine/core';
import { useNavigate } from 'react-router';
import { setToken } from '../../../auth/token';
import { useLoginMutation } from '../api/useAuth';
import { LoginForm } from '../components/LoginForm';
import type { LoginInput } from '../types/auth';

export function LoginPage() {
  const navigate = useNavigate();
  const mutation = useLoginMutation();
  const [errorMessage, setErrorMessage] = useState<string | undefined>();

  const handleSubmit = (values: LoginInput) => {
    setErrorMessage(undefined);
    mutation.mutate(values, {
      onSuccess: (result) => {
        setToken(result.token);
        navigate('/agents', { replace: true });
      },
      onError: () => {
        // Mesma mensagem genérica que apps/api já responde para usuário
        // ou senha incorretos — o frontend não tenta distinguir o campo
        // errado (specs/operator-login-ui/spec.md).
        setErrorMessage('Usuário ou senha inválidos.');
      },
    });
  };

  return (
    <Center h="100vh">
      <Paper withBorder shadow="sm" p="xl" w={360}>
        <Stack>
          <Title order={2}>Buteco Agentes</Title>
          <LoginForm onSubmit={handleSubmit} submitting={mutation.isPending} errorMessage={errorMessage} />
        </Stack>
      </Paper>
    </Center>
  );
}
