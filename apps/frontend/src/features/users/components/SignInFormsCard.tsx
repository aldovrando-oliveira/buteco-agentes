import { Alert, Anchor, Box, Button, Group, Stack, Table, Text, Title } from '@mantine/core';
import { Link } from 'react-router';
import { SectionedCard } from '../../../components/data/SectionedCard';
import { isApiFailure } from '../../../utils/failureNotice';
import { requestPasswordReset, type AccessLink, type UserDetail } from '../api/usersApi';
import { useCallerPassword } from '../callerPassword';
import { CallerPasswordField } from './CallerPasswordField';
import { PasswordResetLinkPanel } from './PasswordResetLinkPanel';

// Formas de entrada e "Redefinir senha" (requisitos "Formas de entrada", "Redefinir senha com
// a senha de quem chama" e "Link de redefinição mostrado uma vez"; design.md da change
// editar-usuario-painel, D6 e D7). Na M1, só a linha Senha. A chamada é direta, sem
// `useMutation`: a resposta traz o link, e o `MutationCache` o guardaria (lição da #246).

export type ResetState =
  { tipo: 'fechado' } | { tipo: 'formulario' } | { tipo: 'link'; acesso: AccessLink };

interface SignInFormsCardProps {
  user: UserDetail;
  proprio: boolean;
  reset: ResetState;
  onOpenReset: () => void;
  onClose: () => void;
  onLink: (acesso: AccessLink) => void;
  /** `409`: o alvo mudou de estado no meio (desativado, convidado); a página relê o detalhe. */
  onStale: () => void;
  /** `404`: o usuário sumiu. */
  onGone: () => void;
}

export function SignInFormsCard({
  user,
  proprio,
  reset,
  onOpenReset,
  onClose,
  onLink,
  onStale,
  onGone,
}: SignInFormsCardProps) {
  const acaoDaSenha = proprio ? (
    <Anchor component={Link} to="/account" size="sm">
      Trocar em Minha conta
    </Anchor>
  ) : user.estado === 'desativado' ? (
    <Text size="sm" c="dimmed">
      Reative para gerar um link
    </Text>
  ) : user.estado === 'ativo' ? (
    <Button size="xs" variant="default" onClick={onOpenReset} disabled={reset.tipo !== 'fechado'}>
      Redefinir senha
    </Button>
  ) : null;

  return (
    <SectionedCard title="Formas de entrada">
      <Table.ScrollContainer minWidth={560}>
        <Table verticalSpacing="sm" horizontalSpacing="md">
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Forma</Table.Th>
              <Table.Th>Identificação</Table.Th>
              <Table.Th>Ações</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {user.formasDeEntrada.map((forma) => (
              <Table.Tr key={forma}>
                <Table.Td fw={600}>{forma === 'senha' ? 'Senha' : forma}</Table.Td>
                <Table.Td ff="monospace" fz="xs">
                  {forma === 'senha' ? user.email : '—'}
                </Table.Td>
                <Table.Td>{forma === 'senha' ? acaoDaSenha : null}</Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>

      {reset.tipo === 'formulario' && (
        <ResetPasswordForm
          user={user}
          onClose={onClose}
          onLink={onLink}
          onStale={onStale}
          onGone={onGone}
        />
      )}
      {reset.tipo === 'link' && (
        <PasswordResetLinkPanel
          link={reset.acesso.link}
          expiraEm={reset.acesso.expiraEm}
          onDone={onClose}
        />
      )}

      <Text
        size="xs"
        c="dimmed"
        px="md"
        py="xs"
        style={{ borderTop: '1px solid var(--mantine-color-default-border)' }}
      >
        Por enquanto, a senha é a única forma de entrada.
      </Text>
    </SectionedCard>
  );
}

function ResetPasswordForm({
  user,
  onClose,
  onLink,
  onStale,
  onGone,
}: Pick<SignInFormsCardProps, 'user' | 'onClose' | 'onLink' | 'onStale' | 'onGone'>) {
  const senha = useCallerPassword();

  const enviar = async () => {
    const result = await senha.enviar(
      (s) => requestPasswordReset(user.id, s),
      (error) => {
        // O `409` (desativado ou convidado no meio) fica no aviso, pelo `title`, e o detalhe é
        // relido. O `404` leva a página a "inexistente", sem aviso.
        if (isApiFailure(error) && error.status === 409) onStale();
        if (isApiFailure(error) && error.status === 404) {
          onGone();
          return true;
        }
        return false;
      },
    );
    if (result) onLink(result);
  };

  return (
    <Box
      component="form"
      aria-labelledby="reset-titulo"
      noValidate
      px="md"
      py="md"
      bg="var(--mantine-primary-color-light)"
      style={{ borderTop: '1px solid var(--mantine-color-default-border)' }}
      onSubmit={(event) => {
        event.preventDefault();
        void enviar();
      }}
    >
      <Stack gap="sm">
        <Title order={3} id="reset-titulo" fz="sm" fw={600}>
          Gerar link para {user.nome} criar uma nova senha
        </Title>
        <Text size="sm" c="dimmed">
          A senha atual continua valendo até a nova ser criada. Quando isso acontecer, a pessoa sai
          de todos os navegadores.
        </Text>
        {senha.aviso && (
          <Alert color={senha.aviso.cor} role="alert" maw={560}>
            {senha.aviso.texto}
          </Alert>
        )}
        <CallerPasswordField
          {...senha.campo}
          description="Para gerar o link, confirme que é você."
        />
        <Group gap="sm">
          <Button type="submit" size="sm" loading={senha.enviando} disabled={senha.bloqueado}>
            Gerar link
          </Button>
          <Button variant="subtle" size="sm" onClick={onClose} disabled={senha.enviando}>
            Cancelar
          </Button>
        </Group>
      </Stack>
    </Box>
  );
}
