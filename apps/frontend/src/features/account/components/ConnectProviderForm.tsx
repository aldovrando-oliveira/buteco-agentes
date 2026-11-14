import { useState } from 'react';
import { Alert, Box, Button, Group, Loader, Stack, Text, Title } from '@mantine/core';
import { authRuntime } from '../../../auth/session';
import { isApiFailure } from '../../../utils/failureNotice';
import { START_404_TITLES } from '../../auth/api/federatedApi';
import { useCallerPassword } from '../../users/callerPassword';
import { CallerPasswordField } from '../../users/components/CallerPasswordField';
import { connectFederated } from '../api/accountApi';
import { FALHA_AO_CONECTAR } from '../accountFederatedTexts';

// O conectar dentro do cartão "Formas de entrada", sem modal (requisito "Conectar a conta do
// provedor"; design.md da change telas-contas-federadas, D3). A confirmação é a de quem chama (o
// `useCallerPassword`, com o modo do D2), e o `200 { url }` leva à autorização do provedor por uma
// navegação de página inteira, como o login da #190.

const PROVEDOR_INDISPONIVEL = 'Não foi possível falar com o provedor. Tente de novo em instantes.';

interface ConnectProviderFormProps {
  chave: string;
  onCancel: () => void;
  /** O `404` "Provedor não configurado.": a lista dos provedores é relida, e a linha some. */
  onNotConfigured: () => void;
}

export function ConnectProviderForm({
  chave,
  onCancel,
  onNotConfigured,
}: ConnectProviderFormProps) {
  const confirmacao = useCallerPassword();
  const [aviso, setAviso] = useState<string | null>(null);
  const [redirecionando, setRedirecionando] = useState(false);

  const continuar = async () => {
    setAviso(null);
    const result = await confirmacao.enviar(
      (c) => connectFederated(chave, c),
      (error) => {
        if (!isApiFailure(error)) return false;
        if (error.status === 404) {
          // Os dois `404` se separam pelo `title` exato, como no login (D1 da #190).
          if (error.title === START_404_TITLES.providerNotConfigured) {
            setAviso(START_404_TITLES.providerNotConfigured);
            onNotConfigured();
          } else {
            setAviso(FALHA_AO_CONECTAR);
          }
          return true;
        }
        if (error.status === 503) {
          setAviso(PROVEDOR_INDISPONIVEL);
          return true;
        }
        return false;
      },
    );
    if (result) {
      setRedirecionando(true);
      authRuntime.assign(result.url);
    }
  };

  if (redirecionando) {
    return (
      <Group role="status" gap="sm" px="md" py="md">
        <Loader size="sm" />
        <Text size="sm">Redirecionando para o Google…</Text>
      </Group>
    );
  }

  return (
    <Box
      component="form"
      aria-labelledby="conectar-titulo"
      noValidate
      px="md"
      py="md"
      bg="var(--mantine-primary-color-light)"
      style={{ borderRadius: 'var(--mantine-radius-md)' }}
      onSubmit={(event) => {
        event.preventDefault();
        void continuar();
      }}
    >
      <Stack gap="sm">
        <Title order={4} id="conectar-titulo" fz="sm" fw={600}>
          Conectar a sua conta Google
        </Title>
        <Text size="sm" c="dimmed">
          Você vai ao Google, escolhe a conta e volta para cá. Depois disso, pode entrar no painel
          também com o Google; a verificação em 2 etapas continua a mesma.
        </Text>
        {aviso && (
          <Alert color="red" role="alert" maw={560}>
            {aviso}
          </Alert>
        )}
        {confirmacao.aviso && (
          <Alert color={confirmacao.aviso.cor} role="alert" maw={560}>
            {confirmacao.aviso.texto}
          </Alert>
        )}
        <CallerPasswordField
          {...confirmacao.campo}
          description="Para conectar, confirme que é você."
        />
        <Group gap="sm">
          <Button
            type="submit"
            size="sm"
            loading={confirmacao.enviando}
            disabled={confirmacao.bloqueado}
          >
            Continuar com Google
          </Button>
          <Button variant="subtle" size="sm" onClick={onCancel} disabled={confirmacao.enviando}>
            Cancelar
          </Button>
        </Group>
      </Stack>
    </Box>
  );
}
