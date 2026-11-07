import { useState } from 'react';
import { Alert, Box, Button, Group, SimpleGrid, Stack, Text, TextInput, Title } from '@mantine/core';
import { isApiFailure } from '../../../utils/failureNotice';
import { ApiError, inviteUser, type InvitationCreated } from '../api/usersApi';
import { useCallerPassword } from '../callerPassword';
import { CallerPasswordField } from './CallerPasswordField';

// "Convidar usuário" dentro do cartão (requisito "Convidar usuário"; design.md da change
// usuarios-convites-identidade-painel, D3). A chamada é direta, sem `useMutation`: a resposta
// traz o link, e o `MutationCache` o guardaria depois de desmontar (D5; lição da #246).

interface InviteFormProps {
  onCreated: (result: InvitationCreated) => void;
  onClose: () => void;
}

interface Erros {
  nome?: string;
  email?: string;
}

export function InviteForm({ onCreated, onClose }: InviteFormProps) {
  const [nome, setNome] = useState('');
  const [email, setEmail] = useState('');
  const [erros, setErros] = useState<Erros>({});
  const senha = useCallerPassword();

  const enviar = async () => {
    setErros({});
    const result = await senha.enviar(
      (s) => inviteUser({ nome, email, senha: s }),
      (error) => {
        if (!isApiFailure(error)) return false;
        if (error.status === 409) {
          setErros({ email: error.title ?? 'Já existe um usuário com este e-mail.' });
          return true;
        }
        if (error.status === 400 && error instanceof ApiError) {
          const campos = error.problem?.errors ?? {};
          setErros({ nome: campos.nome?.[0], email: campos.email?.[0] });
          return true;
        }
        return false;
      },
    );
    if (result) onCreated(result);
  };

  return (
    <Box
      component="form"
      aria-labelledby="convidar-titulo"
      noValidate
      px="md"
      py="md"
      bg="var(--mantine-primary-color-light)"
      style={{ borderBottom: '1px solid var(--mantine-color-default-border)' }}
      onSubmit={(event) => {
        event.preventDefault();
        void enviar();
      }}
    >
      <Stack gap="sm">
        <Title order={3} id="convidar-titulo" fz="sm" fw={600}>
          Convidar usuário
        </Title>
        <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="sm">
          <TextInput label="Nome" value={nome} onChange={(event) => setNome(event.currentTarget.value)} error={erros.nome} />
          <TextInput
            label="E-mail"
            type="email"
            value={email}
            onChange={(event) => setEmail(event.currentTarget.value)}
            error={erros.email}
          />
        </SimpleGrid>
        {senha.aviso && (
          <Alert color={senha.aviso.cor} role="alert" maw={560}>
            {senha.aviso.texto}
          </Alert>
        )}
        <CallerPasswordField {...senha.campo} description="Para convidar alguém, confirme que é você." />
        <Text size="xs" c="dimmed">
          O Buteco gera um link de uso único, válido por 72 horas. Você copia o link e envia para a pessoa pelo canal
          que preferir.
        </Text>
        <Group gap="sm">
          <Button type="submit" size="sm" loading={senha.enviando} disabled={senha.bloqueado}>
            Gerar convite
          </Button>
          <Button variant="subtle" size="sm" onClick={onClose} disabled={senha.enviando}>
            Cancelar
          </Button>
        </Group>
      </Stack>
    </Box>
  );
}
