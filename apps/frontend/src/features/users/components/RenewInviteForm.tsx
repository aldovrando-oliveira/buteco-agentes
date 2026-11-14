import { Alert, Box, Button, Group, Stack, Text, Title } from '@mantine/core';
import { isApiFailure } from '../../../utils/failureNotice';
import { renewInvitation, type AccessLink, type UserSummary } from '../api/usersApi';
import { useCallerPassword } from '../callerPassword';
import { CallerPasswordField } from './CallerPasswordField';

// "Gerar novo link" (requisito "Gerar novo link de convite"; design.md da change
// usuarios-convites-identidade-painel, D3; decisão do dono Q7). O `409` é o convite aceito no
// meio, e o `404`, o cancelado no meio: os dois fecham o formulário e relêem a lista.

interface RenewInviteFormProps {
  user: UserSummary;
  onRenewed: (link: AccessLink) => void;
  /** O convite saiu entre a lista e o pedido: `aceito` (`409`) ou `sumiu` (`404`). */
  onGone: (motivo: 'aceito' | 'sumiu') => void;
  onClose: () => void;
}

export function RenewInviteForm({ user, onRenewed, onGone, onClose }: RenewInviteFormProps) {
  const senha = useCallerPassword();

  const enviar = async () => {
    const result = await senha.enviar(
      (c) => renewInvitation(user.id, c),
      (error) => {
        if (!isApiFailure(error)) return false;
        if (error.status === 409) onGone('aceito');
        else if (error.status === 404) onGone('sumiu');
        else return false;
        return true;
      },
    );
    if (result) onRenewed(result);
  };

  return (
    <Box
      component="form"
      aria-labelledby="renovar-titulo"
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
        <Title order={3} id="renovar-titulo" fz="sm" fw={600}>
          Gerar novo link para {user.nome}
        </Title>
        <Text size="sm" c="dimmed">
          O link anterior deixa de funcionar na hora, e o novo vale por 72 horas.
        </Text>
        {senha.aviso && (
          <Alert color={senha.aviso.cor} role="alert" maw={560}>
            {senha.aviso.texto}
          </Alert>
        )}
        <CallerPasswordField {...senha.campo} description="Para gerar um link, confirme que é você." />
        <Group gap="sm">
          <Button type="submit" size="sm" loading={senha.enviando} disabled={senha.bloqueado}>
            Gerar novo link
          </Button>
          <Button variant="subtle" size="sm" onClick={onClose} disabled={senha.enviando}>
            Cancelar
          </Button>
        </Group>
      </Stack>
    </Box>
  );
}
