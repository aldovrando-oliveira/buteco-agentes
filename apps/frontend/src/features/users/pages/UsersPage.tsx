import { useState } from 'react';
import { Box, Button, Group, Loader, SimpleGrid, Stack, Text, Title } from '@mantine/core';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { SectionedCard } from '../../../components/data/SectionedCard';
import { failureNotice, isApiFailure } from '../../../utils/failureNotice';
import { cancelInvitation, listUsers, type AccessLink, type UserSummary } from '../api/usersApi';
import { USERS_QUERY_KEY } from '../queryKeys';
import { conviteAceitoAoCancelar, conviteAceitoAoRenovar } from '../usersTexts';
import { InviteForm } from '../components/InviteForm';
import { RenewInviteForm } from '../components/RenewInviteForm';
import { InviteLinkPanel } from '../components/InviteLinkPanel';
import { UsersTable, type CancelRow } from '../components/UsersTable';

// A página Usuários (requisitos de `panel-users-page`; design.md da change
// usuarios-convites-identidade-painel, D2 a D5). O cartão é uma máquina de estados com um
// painel por vez. O link gerado vive só no estado `painel`: abrir outro painel, "Concluir" ou
// sair da página o descartam, e a lista é relida pelo `GET`, nunca preenchida com a resposta.

type Painel =
  | { tipo: 'nenhum' }
  | { tipo: 'convidar' }
  | { tipo: 'renovar'; user: UserSummary }
  | { tipo: 'link'; titulo: string; email: string; acesso: AccessLink; marcada: string | null };

export function UsersPage() {
  const queryClient = useQueryClient();
  const users = useQuery({ queryKey: USERS_QUERY_KEY, queryFn: listUsers });
  const [painel, setPainel] = useState<Painel>({ tipo: 'nenhum' });
  const [cancelando, setCancelando] = useState<CancelRow | null>(null);
  const [aviso, setAviso] = useState<string | null>(null);

  const reler = () => queryClient.invalidateQueries({ queryKey: USERS_QUERY_KEY });

  const abrir = (proximo: Painel) => {
    setAviso(null);
    setCancelando(null);
    setPainel(proximo);
  };

  const confirmarCancelamento = async (user: UserSummary) => {
    setCancelando({ userId: user.id, enviando: true, aviso: null });
    try {
      await cancelInvitation(user.id);
      setCancelando(null);
    } catch (error) {
      if (isApiFailure(error) && (error.status === 409 || error.status === 404)) {
        setCancelando(null);
        if (error.status === 409) setAviso(conviteAceitoAoCancelar(user.nome));
      } else {
        setCancelando({ userId: user.id, enviando: false, aviso: failureNotice(error) });
        return;
      }
    }
    await reler();
  };

  return (
    <Stack>
      <Stack gap={2}>
        <Title order={2}>Usuários</Title>
        <Text size="sm" c="dimmed">
          Quem pode entrar no painel. O acesso é só por convite.
        </Text>
      </Stack>

      <SectionedCard
        title="Usuários"
        action={
          <Button size="xs" onClick={() => abrir({ tipo: 'convidar' })} disabled={painel.tipo !== 'nenhum' || cancelando !== null}>
            Convidar usuário
          </Button>
        }
      >
        {aviso && (
          <Box px="md" py="sm" role="status" bg="var(--mantine-primary-color-light)" c="var(--mantine-primary-color-light-color)">
            <Text size="sm">{aviso}</Text>
          </Box>
        )}

        {painel.tipo === 'convidar' && (
          <InviteForm
            onClose={() => abrir({ tipo: 'nenhum' })}
            onCreated={(result) => {
              abrir({
                tipo: 'link',
                titulo: `Convite criado para ${result.usuario.nome}`,
                email: result.usuario.email,
                acesso: { link: result.link, expiraEm: result.expiraEm },
                marcada: result.usuario.id,
              });
              void reler();
            }}
          />
        )}

        {painel.tipo === 'renovar' && (
          <RenewInviteForm
            key={painel.user.id}
            user={painel.user}
            onClose={() => abrir({ tipo: 'nenhum' })}
            onRenewed={(acesso) => {
              abrir({
                tipo: 'link',
                titulo: `Novo link gerado para ${painel.user.nome}`,
                email: painel.user.email,
                acesso,
                marcada: null,
              });
              void reler();
            }}
            onGone={(motivo) => {
              const nome = painel.user.nome;
              abrir({ tipo: 'nenhum' });
              if (motivo === 'aceito') setAviso(conviteAceitoAoRenovar(nome));
              void reler();
            }}
          />
        )}

        {painel.tipo === 'link' && (
          <InviteLinkPanel
            titulo={painel.titulo}
            email={painel.email}
            link={painel.acesso.link}
            expiraEm={painel.acesso.expiraEm}
            onDone={() => abrir({ tipo: 'nenhum' })}
          />
        )}

        {users.isPending ? (
          <Group role="status" justify="center" px="md" py="xl">
            <Loader size="sm" />
            <Text size="sm" c="dimmed">
              Carregando os usuários…
            </Text>
          </Group>
        ) : users.isError ? (
          <Group role="alert" justify="space-between" px="md" py="md">
            <Text size="sm" c="red">
              Não foi possível carregar os usuários agora.
            </Text>
            <Button variant="default" size="sm" onClick={() => void users.refetch()}>
              Tentar de novo
            </Button>
          </Group>
        ) : (
          <UsersTable
            users={users.data}
            marcada={painel.tipo === 'link' ? painel.marcada : null}
            cancelando={cancelando}
            onRenew={(user) => abrir({ tipo: 'renovar', user })}
            onAskCancel={(user) => {
              abrir({ tipo: 'nenhum' });
              setCancelando({ userId: user.id, enviando: false, aviso: null });
            }}
            onConfirmCancel={(user) => void confirmarCancelamento(user)}
            onBack={() => setCancelando(null)}
          />
        )}
      </SectionedCard>

      <SimpleGrid cols={{ base: 1, md: 2 }} spacing="md">
        <SectionedCard title="Acesso só por convite">
          <Text size="sm" c="dimmed" px="md" py="sm">
            Ninguém entra sem ter sido convidado aqui. Não existe cadastro aberto nem entrada automática.
          </Text>
        </SectionedCard>
        <SectionedCard title="Senha e recuperação">
          <Text size="sm" c="dimmed" px="md" py="sm">
            Esqueceu a senha: um administrador gera um link de redefinição em Editar usuário. A verificação em 2 etapas é
            ligada por cada pessoa em Minha conta.
          </Text>
        </SectionedCard>
      </SimpleGrid>
    </Stack>
  );
}
