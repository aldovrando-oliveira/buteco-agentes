import { useState } from 'react';
import { Alert, Anchor, Box, Button, Group, Loader, Paper, Stack, Text } from '@mantine/core';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link, Navigate, useParams } from 'react-router';
import { DetailHeader } from '../../../components/layout/DetailHeader';
import { isSameUser, useCurrentUserId } from '../../../auth/session';
import { failureNotice, isApiFailure, type FailureNotice } from '../../../utils/failureNotice';
import {
  activateUser,
  deactivateUser,
  getUser,
  resetTwoFactor,
  revokeUserSessions,
  type AccessLink,
  type UserDetail,
} from '../api/usersApi';
import { USERS_QUERY_KEY, userQueryKey } from '../queryKeys';
import { detailRetry, is404 } from '../detailRetry';
import { stateBadge } from '../usersTexts';
import { confirmDeactivate, detailSubtitle, endedSessions, reactivated } from '../userDetailTexts';
import { ConfirmBar } from '../components/ConfirmBar';
import { Selo } from '../components/Selo';
import { UserDataCard } from '../components/UserDataCard';
import { SignInFormsCard, type ResetState } from '../components/SignInFormsCard';
import { UserTwoFactorCard } from '../components/UserTwoFactorCard';
import { UserSessionsCard } from '../components/UserSessionsCard';

// Editar usuário (`/users/:id`; requisitos de `panel-user-detail-page`; design.md da change
// editar-usuario-painel, D2 a D10). A página guarda UMA ação aberta, como a propriedade `acao` do
// quadro: abrir outra fecha a anterior e descarta o que ela guardava, inclusive o link de
// redefinição, que só existe aqui (nunca no cache, em storage ou na URL). O rascunho de Dados é
// do cartão e não depende dela.

type Acao =
  | { tipo: 'nenhuma' }
  | { tipo: 'desativar' }
  | { tipo: 'redefinir-senha' }
  | { tipo: 'link'; acesso: AccessLink }
  | { tipo: 'redefinir-2fa' }
  | { tipo: 'encerrar-sessoes' };

type Sucesso = { onde: 'pagina' | 'sessoes'; texto: string } | null;

export function UserDetailPage() {
  const id = useParams().id ?? '';
  // Remonta a cada id: o router reaproveita a instância quando só o `:id` muda, e a ação aberta
  // (inclusive um link de redefinição), os avisos e o rascunho de Dados de uma pessoa
  // apareceriam na página de outra (achado da conferência no navegador, tarefa 11.3).
  return <UserDetail key={id} id={id} />;
}

function UserDetail({ id }: { id: string }) {
  const queryClient = useQueryClient();
  const proprio = isSameUser(useCurrentUserId(), id);
  const query = useQuery({
    queryKey: userQueryKey(id),
    queryFn: () => getUser(id),
    retry: detailRetry(queryClient),
  });

  const [acao, setAcao] = useState<Acao>({ tipo: 'nenhuma' });
  const [sucesso, setSucesso] = useState<Sucesso>(null);
  const [falha, setFalha] = useState<FailureNotice | null>(null);
  const [enviando, setEnviando] = useState(false);

  const abrir = (proxima: Acao) => {
    setSucesso(null);
    setFalha(null);
    setAcao(proxima);
  };
  const reler = () => queryClient.invalidateQueries({ queryKey: userQueryKey(id), exact: true });
  const relerLista = () =>
    queryClient.invalidateQueries({ queryKey: USERS_QUERY_KEY, exact: true });

  /** Uma ação direta da página. O `404` relê o detalhe, que leva a "inexistente". */
  async function executar<T>(
    call: () => Promise<T>,
    depois: (result: T) => void | Promise<unknown>,
  ) {
    setFalha(null);
    setEnviando(true);
    try {
      const result = await call();
      await depois(result);
    } catch (error) {
      if (is404(error)) {
        setAcao({ tipo: 'nenhuma' });
        await reler();
      } else {
        setFalha(failureNotice(error));
      }
    } finally {
      setEnviando(false);
    }
  }

  const aplicarDetalhe = (detalhe: UserDetail) => {
    queryClient.setQueryData(userQueryKey(id), detalhe);
    void relerLista();
  };

  if (query.isPending) {
    return (
      <Group role="status" justify="center" py="xl">
        <Loader size="sm" />
        <Text size="sm" c="dimmed">
          Carregando o usuário…
        </Text>
      </Group>
    );
  }

  if (query.isError && is404(query.error)) {
    return (
      <Paper withBorder radius="md" p="lg" role="alert">
        <Stack gap="xs">
          <Text fw={600}>Usuário não encontrado</Text>
          <Text size="sm" c="dimmed">
            O link pode estar errado, ou o convite foi cancelado.
          </Text>
          <Anchor component={Link} to="/users" size="sm">
            Voltar para Usuários
          </Anchor>
        </Stack>
      </Paper>
    );
  }

  if (!query.data) {
    return (
      <Paper withBorder radius="md" px="lg" py="md" role="alert">
        <Group justify="space-between">
          <Text size="sm" c="red">
            Não foi possível carregar o usuário agora.
          </Text>
          <Button variant="default" size="sm" onClick={() => void query.refetch()}>
            Tentar de novo
          </Button>
        </Group>
      </Paper>
    );
  }

  const user = query.data;
  // O convidado não tem detalhe: o convite se gere na lista (decisão do dono Q4).
  if (user.estado === 'convidado') return <Navigate to="/users" replace />;

  const desativado = user.estado === 'desativado';
  const estado = stateBadge(user);
  const reset: ResetState =
    acao.tipo === 'redefinir-senha'
      ? { tipo: 'formulario' }
      : acao.tipo === 'link'
        ? { tipo: 'link', acesso: acao.acesso }
        : { tipo: 'fechado' };

  const desativar = () =>
    executar(
      () => deactivateUser(user.id),
      (detalhe) => {
        aplicarDetalhe(detalhe);
        setAcao({ tipo: 'nenhuma' });
      },
    );

  const reativar = () => {
    abrir({ tipo: 'nenhuma' });
    void executar(
      () => activateUser(user.id),
      (detalhe) => {
        aplicarDetalhe(detalhe);
        setSucesso({ onde: 'pagina', texto: reactivated(detalhe.nome) });
      },
    );
  };

  const redefinirVerificacao = () =>
    executar(
      async () => {
        try {
          await resetTwoFactor(user.id);
        } catch (error) {
          // Já não configurada (outra aba, ou a pessoa removeu): só relê, sem aviso.
          if (!(isApiFailure(error) && error.status === 409)) throw error;
        }
      },
      async () => {
        setAcao({ tipo: 'nenhuma' });
        await reler();
        void relerLista();
      },
    );

  const encerrarSessoes = () =>
    executar(
      () => revokeUserSessions(user.id),
      async ({ encerradas }) => {
        setAcao({ tipo: 'nenhuma' });
        setSucesso({ onde: 'sessoes', texto: endedSessions(encerradas) });
        await reler();
      },
    );

  return (
    <Stack>
      <DetailHeader
        backTo="/users"
        backLabel="Usuários"
        title={user.nome}
        status={
          <Group gap={6}>
            <Selo texto={estado.texto} tom={estado.tom} />
            {proprio && <Selo texto="Você" tom="gray" />}
          </Group>
        }
        description={detailSubtitle(user)}
        actions={
          !proprio && user.estado === 'ativo' ? (
            <Button
              variant="outline"
              color="red"
              onClick={() => abrir({ tipo: 'desativar' })}
              disabled={acao.tipo === 'desativar'}
            >
              Desativar
            </Button>
          ) : desativado ? (
            <Button onClick={reativar} loading={enviando && acao.tipo === 'nenhuma'}>
              Reativar
            </Button>
          ) : undefined
        }
      />

      {sucesso?.onde === 'pagina' && (
        <Box
          role="status"
          px="md"
          py="sm"
          bg="var(--mantine-color-green-light)"
          c="var(--mantine-color-green-light-color)"
          style={{ borderRadius: 'var(--mantine-radius-md)' }}
        >
          <Text size="sm">{sucesso.texto}</Text>
        </Box>
      )}
      {falha && acao.tipo === 'nenhuma' && (
        <Alert color={falha.cor}>
          <Text size="sm">{falha.texto}</Text>
        </Alert>
      )}
      {acao.tipo === 'desativar' && (
        <ConfirmBar
          solta
          texto={confirmDeactivate(user.nome)}
          confirmLabel="Desativar"
          onConfirm={() => void desativar()}
          onCancel={() => abrir({ tipo: 'nenhuma' })}
          enviando={enviando}
          falha={falha}
        />
      )}
      {proprio && (
        <Box
          role="note"
          px="md"
          py="sm"
          bg="var(--mantine-primary-color-light)"
          c="var(--mantine-primary-color-light-color)"
          style={{ borderRadius: 'var(--mantine-radius-md)' }}
        >
          <Text size="sm">
            Este é você. A sua senha, a sua verificação em 2 etapas e as suas sessões ficam em{' '}
            <Anchor component={Link} to="/account" size="sm">
              Minha conta
            </Anchor>
            .
          </Text>
        </Box>
      )}

      <UserDataCard user={user} proprio={proprio} onGone={() => void reler()} />

      <SignInFormsCard
        user={user}
        proprio={proprio}
        reset={reset}
        onOpenReset={() => abrir({ tipo: 'redefinir-senha' })}
        onClose={() => abrir({ tipo: 'nenhuma' })}
        onLink={(acesso) => {
          setAcao({ tipo: 'link', acesso });
          // O `GET` não traz nada do link: o cache recebe só o que já é público.
          void reler();
        }}
        onStale={() => void reler()}
        onGone={() => {
          setAcao({ tipo: 'nenhuma' });
          void reler();
        }}
      />

      <UserTwoFactorCard
        user={user}
        proprio={proprio}
        confirmando={acao.tipo === 'redefinir-2fa'}
        enviando={enviando}
        falha={acao.tipo === 'redefinir-2fa' ? falha : null}
        onAsk={() => abrir({ tipo: 'redefinir-2fa' })}
        onConfirm={() => void redefinirVerificacao()}
        onCancel={() => abrir({ tipo: 'nenhuma' })}
      />

      <UserSessionsCard
        user={user}
        proprio={proprio}
        confirmando={acao.tipo === 'encerrar-sessoes'}
        enviando={enviando}
        falha={acao.tipo === 'encerrar-sessoes' ? falha : null}
        encerradas={sucesso?.onde === 'sessoes' ? sucesso.texto : null}
        onAsk={() => abrir({ tipo: 'encerrar-sessoes' })}
        onConfirm={() => void encerrarSessoes()}
        onCancel={() => abrir({ tipo: 'nenhuma' })}
      />
    </Stack>
  );
}
