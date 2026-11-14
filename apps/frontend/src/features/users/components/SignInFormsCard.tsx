import { Alert, Anchor, Badge, Box, Button, Group, Stack, Table, Text, Title } from '@mantine/core';
import { Link } from 'react-router';
import { SectionedCard } from '../../../components/data/SectionedCard';
import { isApiFailure, type FailureNotice } from '../../../utils/failureNotice';
import { formatDate, formatRelative } from '../../../utils/panelDates';
import type { ConnectedAccount } from '../../account/api/accountApi';
import { ULTIMA_FORMA, providerLabel, signInFormCount } from '../../account/accountFederatedTexts';
import { requestPasswordReset, type AccessLink, type UserDetail } from '../api/usersApi';
import { useCallerPassword } from '../callerPassword';
import { CallerPasswordField } from './CallerPasswordField';
import { PasswordResetLinkPanel } from './PasswordResetLinkPanel';

// Formas de entrada e "Redefinir senha" (requisitos "Formas de entrada", "Redefinir senha com
// a senha de quem chama" e "Link de redefinição mostrado uma vez"; design.md da change
// editar-usuario-painel, D6 e D7). A chamada é direta, sem `useMutation`: a resposta traz o
// link, e o `MutationCache` o guardaria (lição da #246).
//
// Na M3 (change telas-contas-federadas, D5; quadro `UsuarioDetalhe`, `entrega=m3`, versão 81), as
// contas de provedores de `contasConectadas`, com as regras do cartão de Minha conta (a última forma,
// o vínculo fora da configuração sem ação), e o desconectar pelo Admin, na linha. O Admin só
// desconecta: conectar é da própria pessoa, em Minha conta. No próprio usuário, o link para Minha
// conta, porque o backend recusa o próprio pela rota de Usuários (`403`).

export type ResetState =
  { tipo: 'fechado' } | { tipo: 'formulario' } | { tipo: 'link'; acesso: AccessLink };

/** O desconectar aberto numa linha: a confirmação, ou o `409` da última forma. */
export type DisconnectState = { chave: string; fase: 'confirmar' | '409' } | null;

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
  desconexao: DisconnectState;
  desconectando: boolean;
  /** O aviso do desconectar: o verde do `204`, ou a falha. */
  avisoDesconexao: FailureNotice | { cor: 'green'; texto: string } | null;
  onAskDisconnect: (chave: string) => void;
  onConfirmDisconnect: (chave: string) => void;
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
  desconexao,
  desconectando,
  avisoDesconexao,
  onAskDisconnect,
  onConfirmDisconnect,
}: SignInFormsCardProps) {
  const comSenha = user.formasDeEntrada.includes('senha');
  const unica = signInFormCount(user.formasDeEntrada, user.contasConectadas) <= 1;

  const acaoDaSenha = proprio ? (
    comSenha ? (
      <Anchor component={Link} to="/account" size="sm">
        Trocar em Minha conta
      </Anchor>
    ) : null
  ) : user.estado === 'desativado' ? (
    <Text size="sm" c="dimmed">
      Reative para gerar um link
    </Text>
  ) : user.estado === 'ativo' ? (
    <Button size="xs" variant="default" onClick={onOpenReset} disabled={reset.tipo !== 'fechado'}>
      {comSenha ? 'Redefinir senha' : 'Gerar link de senha'}
    </Button>
  ) : null;

  const acaoDaConta = (conta: ConnectedAccount) => {
    if (!conta.disponivel || conta.chave === null) {
      return (
        <Text size="sm" c="dimmed">
          Sem ação
        </Text>
      );
    }
    if (proprio) {
      return (
        <Anchor component={Link} to="/account" size="sm">
          Desconectar em Minha conta
        </Anchor>
      );
    }
    const chave = conta.chave;
    if (desconexao?.chave === chave && desconexao.fase === 'confirmar') {
      return (
        <Stack gap={6}>
          <Text size="sm">
            Desconectar o {providerLabel(conta.tipo)} de {user.nome}? A pessoa sai de todos os
            navegadores.
          </Text>
          <Group gap="xs">
            <Button
              size="xs"
              color="red"
              loading={desconectando}
              onClick={() => onConfirmDisconnect(chave)}
            >
              Desconectar
            </Button>
            <Button size="xs" variant="subtle" disabled={desconectando} onClick={onClose}>
              Cancelar
            </Button>
          </Group>
        </Stack>
      );
    }
    if (desconexao?.chave === chave && desconexao.fase === '409') {
      return (
        <Group gap="xs" role="alert">
          <Text size="sm">{ULTIMA_FORMA}</Text>
          <Button size="xs" variant="subtle" onClick={onClose}>
            Fechar
          </Button>
        </Group>
      );
    }
    if (unica) {
      return (
        <Group gap="xs">
          <Button size="xs" variant="subtle" color="red" disabled>
            Desconectar
          </Button>
          <Text size="xs" c="dimmed">
            É a única forma de entrada de {user.nome}.
          </Text>
        </Group>
      );
    }
    return (
      <Button size="xs" variant="subtle" color="red" onClick={() => onAskDisconnect(chave)}>
        Desconectar
      </Button>
    );
  };

  return (
    <SectionedCard title="Formas de entrada">
      {avisoDesconexao && (
        <Alert
          color={avisoDesconexao.cor}
          role={avisoDesconexao.cor === 'green' ? 'status' : 'alert'}
          mx="md"
          mt="sm"
        >
          {avisoDesconexao.texto}
        </Alert>
      )}
      <Table.ScrollContainer minWidth={720}>
        <Table verticalSpacing="sm" horizontalSpacing="md">
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Forma</Table.Th>
              <Table.Th>Identificação</Table.Th>
              <Table.Th>Conectada em</Table.Th>
              <Table.Th>Último uso</Table.Th>
              <Table.Th>Ações</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            <Table.Tr>
              <Table.Td fw={600}>Senha</Table.Td>
              {comSenha ? (
                <Table.Td ff="monospace" fz="xs">
                  {user.email}
                </Table.Td>
              ) : (
                <Table.Td>Sem senha</Table.Td>
              )}
              <Table.Td>—</Table.Td>
              <Table.Td>—</Table.Td>
              <Table.Td>{acaoDaSenha}</Table.Td>
            </Table.Tr>
            {user.contasConectadas.map((conta) => {
              const destacada = conta.chave !== null && desconexao?.chave === conta.chave;
              return (
                <Table.Tr
                  key={`${conta.chave ?? 'fora'}-${conta.conectadaEm}`}
                  bg={destacada ? 'var(--mantine-color-red-light)' : undefined}
                >
                  <Table.Td fw={600}>{providerLabel(conta.tipo)}</Table.Td>
                  <Table.Td>
                    <Group gap="xs">
                      <Text ff="monospace" fz="xs">
                        {conta.email ?? '—'}
                      </Text>
                      {!conta.disponivel && (
                        <Badge color="gray" variant="light" tt="none">
                          Provedor fora da configuração
                        </Badge>
                      )}
                    </Group>
                  </Table.Td>
                  <Table.Td>{formatDate(conta.conectadaEm)}</Table.Td>
                  <Table.Td>{conta.ultimoUso ? formatRelative(conta.ultimoUso) : '—'}</Table.Td>
                  <Table.Td>{acaoDaConta(conta)}</Table.Td>
                </Table.Tr>
              );
            })}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>

      {reset.tipo === 'formulario' && (
        <ResetPasswordForm
          user={user}
          semSenha={!comSenha}
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
        Contas de provedores são conectadas pela própria pessoa em Minha conta. Aqui o administrador
        só desconecta. A última forma de entrada não pode ser removida.
      </Text>
    </SectionedCard>
  );
}

function ResetPasswordForm({
  user,
  semSenha,
  onClose,
  onLink,
  onStale,
  onGone,
}: Pick<SignInFormsCardProps, 'user' | 'onClose' | 'onLink' | 'onStale' | 'onGone'> & {
  semSenha: boolean;
}) {
  const senha = useCallerPassword();

  const enviar = async () => {
    const result = await senha.enviar(
      (c) => requestPasswordReset(user.id, c),
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
          {semSenha
            ? `Gerar link para ${user.nome} criar uma senha`
            : `Gerar link para ${user.nome} criar uma nova senha`}
        </Title>
        <Text size="sm" c="dimmed">
          {semSenha
            ? 'Hoje a pessoa entra só pelo Google. Com o link, ela cria uma senha e passa a ter as duas formas de entrada.'
            : 'A senha atual continua valendo até a nova ser criada. Quando isso acontecer, a pessoa sai de todos os navegadores.'}
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
