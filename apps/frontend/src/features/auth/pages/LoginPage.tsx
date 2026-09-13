import { useState } from 'react';
import { Center, Paper, Stack, Title, VisuallyHidden } from '@mantine/core';
import { Logo } from '../../../components/brand/Logo';
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
          {/* A única tela com espaço vertical para o lockup, que é a
              assinatura da marca: símbolo e nome com o espaçamento óptico e a
              escala tipográfica desenhados no pacote, e não empilhados aqui.
              O nome vem desenhado, então o <Title> guarda só a semântica de
              cabeçalho e o nome acessível — anunciado uma vez, porque o lockup
              é decorativo (design.md da change frontend-marca-visual, D11). */}
          <Stack align="center" gap={0}>
            <Title order={2}>
              <Logo variant="vertical" size={110} />
              <VisuallyHidden>Buteco Agentes</VisuallyHidden>
            </Title>
          </Stack>
          <LoginForm
            onSubmit={handleSubmit}
            submitting={mutation.isPending}
            errorMessage={errorMessage}
          />
        </Stack>
      </Paper>
    </Center>
  );
}
