import { useState } from 'react';
import { Alert, Badge, Button, Group, Stack, Table, Text } from '@mantine/core';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { logout } from '../../../auth/session';
import { ApiError, listSessions, revokeOtherSessions, revokeSession, type AccountSession } from '../api/accountApi';
import { formatRelative } from '../../../utils/panelDates';
import { blockedUntil, failureNotice, type FailureNotice } from '../../../utils/failureNotice';
import { useBlockedUntil } from '../../../hooks/useBlockedUntil';
import { SESSIONS_QUERY_KEY } from '../queryKeys';
import { confirmOthersText, endedText } from '../accountTexts';
import { AccountCard, CardLoadError, CardLoading } from './AccountCard';

// O cartão "Sessões" (requisito "Sessões da própria conta"; design.md da change
// minha-conta-painel, D6). A API não traz rótulo, só datas (#219; IP e navegador são a #221):
// o nome é a posição na lista, na ordem do servidor (`criadaEm` decrescente), e a sessão
// atual é a do campo `atual`, onde quer que esteja (decisão do dono Q4).
//
// "Encerrar e sair" vai pelo logout do emissor, sem confirmação (Q9): ele revoga a
// autorização do hint, que é a mesma da sessão atual, apaga o cookie e leva ao /entrar. A
// rota de revogação deixaria esta aba logada até a renovação seguinte.

type Confirmando = { tipo: 'uma'; sessao: AccountSession; nome: string } | { tipo: 'outras'; quantas: number } | null;

export function AccountSessionsCard() {
  const queryClient = useQueryClient();
  const query = useQuery({ queryKey: SESSIONS_QUERY_KEY, queryFn: listSessions });
  const [confirmando, setConfirmando] = useState<Confirmando>(null);
  const [encerradas, setEncerradas] = useState<string | null>(null);
  const [aviso, setAviso] = useState<FailureNotice | null>(null);
  const [enviando, setEnviando] = useState(false);
  const [saindo, setSaindo] = useState(false);
  const [bloqueado, setBloqueadoAte] = useBlockedUntil();

  const reler = () => queryClient.invalidateQueries({ queryKey: SESSIONS_QUERY_KEY });
  const sessoes = query.data ?? [];
  const outras = sessoes.filter((s) => !s.atual).length;

  const encerrar = async () => {
    if (!confirmando) return;
    setAviso(null);
    setEnviando(true);
    try {
      if (confirmando.tipo === 'uma') {
        await revokeSession(confirmando.sessao.id);
      } else {
        const { encerradas: n } = await revokeOtherSessions();
        setEncerradas(endedText(n));
      }
      setConfirmando(null);
      await reler();
    } catch (error) {
      // `404`: a sessão já tinha saído (em outra aba, por exemplo). Relê, sem erro.
      if (error instanceof ApiError && error.status === 404) {
        setConfirmando(null);
        await reler();
      } else {
        setAviso(failureNotice(error));
        setBloqueadoAte(blockedUntil(error));
      }
    } finally {
      setEnviando(false);
    }
  };

  const sair = async () => {
    setSaindo(true);
    await logout();
  };

  return (
    <AccountCard
      title="Sessões"
      action={
        query.isSuccess &&
        outras > 0 && (
          <Button
            size="xs"
            variant="default"
            onClick={() => {
              setEncerradas(null);
              setAviso(null);
              setConfirmando({ tipo: 'outras', quantas: outras });
            }}
          >
            Encerrar as outras
          </Button>
        )
      }
    >
      {query.isPending ? (
        <CardLoading label="Carregando as sessões" />
      ) : query.isError ? (
        <CardLoadError error={query.error} onRetry={() => void query.refetch()} />
      ) : (
        <Stack gap="sm">
          {aviso && (
            <Alert color={aviso.cor} role="alert">
              {aviso.texto}
            </Alert>
          )}
          {confirmando && (
            <Alert color="red" variant="light" role="alert">
              <Group justify="space-between" wrap="nowrap">
                <Text size="sm">
                  {confirmando.tipo === 'uma'
                    ? `Encerrar a ${confirmando.nome}? Quem estiver usando esse navegador precisa entrar de novo.`
                    : confirmOthersText(confirmando.quantas)}
                </Text>
                <Group gap="xs" wrap="nowrap">
                  <Button size="xs" color="red" loading={enviando} disabled={bloqueado} onClick={() => void encerrar()}>
                    Encerrar
                  </Button>
                  <Button size="xs" variant="subtle" disabled={enviando} onClick={() => setConfirmando(null)}>
                    Cancelar
                  </Button>
                </Group>
              </Group>
            </Alert>
          )}
          {encerradas && (
            <Alert color="green" role="status">
              {encerradas}
            </Alert>
          )}
          <Table>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Sessão</Table.Th>
                <Table.Th>Começou em</Table.Th>
                <Table.Th>Último uso</Table.Th>
                <Table.Th>Ações</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {sessoes.map((sessao, indice) => {
                const nome = `Sessão ${indice + 1}`;
                return (
                  <Table.Tr key={sessao.id}>
                    <Table.Td>
                      <Group gap="xs" wrap="nowrap">
                        <Text size="sm">{nome}</Text>
                        {sessao.atual && (
                          <Badge color="green" variant="light">
                            Esta sessão
                          </Badge>
                        )}
                      </Group>
                    </Table.Td>
                    <Table.Td>
                      <Text size="sm">{formatRelative(sessao.criadaEm)}</Text>
                    </Table.Td>
                    <Table.Td>
                      <Text size="sm">{sessao.atual ? 'agora' : formatRelative(sessao.ultimoUso)}</Text>
                    </Table.Td>
                    <Table.Td>
                      {sessao.atual ? (
                        <Button size="xs" variant="subtle" color="red" loading={saindo} onClick={() => void sair()}>
                          {saindo ? 'Saindo…' : 'Encerrar e sair'}
                        </Button>
                      ) : (
                        <Button
                          size="xs"
                          variant="subtle"
                          color="red"
                          aria-label={`Encerrar a ${nome}`}
                          onClick={() => {
                            setEncerradas(null);
                            setAviso(null);
                            setConfirmando({ tipo: 'uma', sessao, nome });
                          }}
                        >
                          Encerrar
                        </Button>
                      )}
                    </Table.Td>
                  </Table.Tr>
                );
              })}
            </Table.Tbody>
          </Table>
          <Text size="xs" c="dimmed">
            Cada sessão é um navegador em que você entrou. O painel mostra só quando ela começou e quando foi usada
            pela última vez, com precisão de 15 minutos. Encerrar esta sessão leva você ao login.
          </Text>
        </Stack>
      )}
    </AccountCard>
  );
}
