import { useState } from 'react';
import { Alert, Badge, Button, Group, Stack, Table, Text } from '@mantine/core';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { failureNotice, isApiFailure, type FailureNotice } from '../../../utils/failureNotice';
import { formatDate, formatRelative } from '../../../utils/panelDates';
import { useLoginProviders } from '../../auth/federatedHooks';
import { disconnectOwnFederated, getAccount, type ConnectedAccount } from '../api/accountApi';
import {
  AVISO_DESCONECTADA,
  NAO_CONECTADA_A_ESSE_PROVEDOR,
  ULTIMA_FORMA,
  providerLabel,
  signInFormCount,
  type FormsNotice,
} from '../accountFederatedTexts';
import { ACCOUNT_QUERY_KEY } from '../queryKeys';
import { AccountCard } from './AccountCard';
import { ConnectProviderForm } from './ConnectProviderForm';

// O cartão "Formas de entrada" de Minha conta (requisitos "Formas de entrada em Minha conta" e
// "Desconectar a própria conta do provedor"; design.md da change telas-contas-federadas, D3; quadro
// `MinhaConta`, `entrega=m3`, versão 81).
//
// QUANDO O CARTÃO APARECE: com a conta lida e um provedor na lista, uma conta conectada (mesmo fora
// da configuração) ou um aviso na tela. Sem provedor e sem conta, a instalação não tem outra forma de
// entrada além da senha, e o rodapé, que fala da conta Google, afirmaria o que ela não tem (convenção
// 13). O AVISO SEGURA O CARTÃO (decisão do dono, na aprovação das specs): a recusa da volta sem conta e
// com a lista falhando, ou o desconectar da única conta com a lista vazia, levariam o aviso junto.
// Com `GET /account` em erro, o cartão não aparece: o cartão Dados mostra o erro da mesma consulta.

const TOM: Record<FormsNotice['cor'], string> = { green: 'green', red: 'red', yellow: 'yellow' };

interface AccountSignInFormsCardProps {
  /** O aviso da volta do conectar, lido uma vez pela página (`useFederatedReturn`). */
  avisoDaVolta: FormsNotice | null;
}

type Linha =
  | { tipo: 'senha' }
  | { tipo: 'conta'; conta: ConnectedAccount }
  | { tipo: 'livre'; chave: string; nome: string };

export function AccountSignInFormsCard({ avisoDaVolta }: AccountSignInFormsCardProps) {
  const queryClient = useQueryClient();
  const conta = useQuery({ queryKey: ACCOUNT_QUERY_KEY, queryFn: getAccount });
  const { providers, reload } = useLoginProviders(true);
  const [aviso, setAviso] = useState<FormsNotice | null>(avisoDaVolta);
  const [conectando, setConectando] = useState<string | null>(null);
  const [confirmando, setConfirmando] = useState<string | null>(null);
  const [erro409, setErro409] = useState<string | null>(null);
  const [falha, setFalha] = useState<FailureNotice | null>(null);
  const [desconectando, setDesconectando] = useState(false);

  if (!conta.data) return null;
  const { formasDeEntrada, contasConectadas, email } = conta.data;
  // O formulário aberto também segura: o `404` "Provedor não configurado." relê a lista, e o aviso dele
  // está no formulário.
  if (
    providers.length === 0 &&
    contasConectadas.length === 0 &&
    aviso === null &&
    conectando === null
  )
    return null;

  const unica = signInFormCount(formasDeEntrada, contasConectadas) <= 1;
  const linhas: Linha[] = [
    ...(formasDeEntrada.includes('senha') ? [{ tipo: 'senha' } as const] : []),
    ...contasConectadas.map((c) => ({ tipo: 'conta' as const, conta: c })),
    ...providers
      .filter((p) => !contasConectadas.some((c) => c.chave === p.chave))
      .map((p) => ({ tipo: 'livre' as const, chave: p.chave, nome: p.nome })),
  ];

  const fecharTudo = () => {
    setConectando(null);
    setConfirmando(null);
    setErro409(null);
    setFalha(null);
  };
  const reler = () => queryClient.invalidateQueries({ queryKey: ACCOUNT_QUERY_KEY, exact: true });

  const desconectar = async (chave: string) => {
    setFalha(null);
    setDesconectando(true);
    try {
      await disconnectOwnFederated(chave);
      setConfirmando(null);
      setAviso(AVISO_DESCONECTADA);
      void reler();
    } catch (error) {
      if (isApiFailure(error) && error.status === 409) {
        setConfirmando(null);
        setErro409(chave);
        void reler();
      } else if (isApiFailure(error) && error.status === 404) {
        setConfirmando(null);
        setAviso({ cor: 'red', texto: error.title ?? NAO_CONECTADA_A_ESSE_PROVEDOR });
        void reler();
      } else {
        setFalha(failureNotice(error));
      }
    } finally {
      setDesconectando(false);
    }
  };

  const acoesDaConta = (c: ConnectedAccount) => {
    if (!c.disponivel || c.chave === null) {
      return (
        <Text size="sm" c="dimmed">
          Sem ação
        </Text>
      );
    }
    const chave = c.chave;
    if (confirmando === chave) {
      return (
        <Stack gap={6}>
          <Text size="sm">
            Desconectar o {providerLabel(c.tipo)} ({c.email ?? '—'})? Você deixa de entrar com essa
            conta.
          </Text>
          <Group gap="xs">
            <Button
              size="xs"
              color="red"
              loading={desconectando}
              onClick={() => void desconectar(chave)}
            >
              Desconectar
            </Button>
            <Button size="xs" variant="subtle" disabled={desconectando} onClick={fecharTudo}>
              Cancelar
            </Button>
          </Group>
        </Stack>
      );
    }
    if (erro409 === chave) {
      return (
        <Group gap="xs" role="alert">
          <Text size="sm">{ULTIMA_FORMA}</Text>
          <Button size="xs" variant="subtle" onClick={fecharTudo}>
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
            É a sua única forma de entrada.
          </Text>
        </Group>
      );
    }
    return (
      <Button
        size="xs"
        variant="subtle"
        color="red"
        onClick={() => {
          fecharTudo();
          setConfirmando(chave);
        }}
      >
        Desconectar
      </Button>
    );
  };

  return (
    <AccountCard title="Formas de entrada">
      {aviso && (
        <Alert
          color={TOM[aviso.cor]}
          role={aviso.cor === 'green' ? 'status' : 'alert'}
          title={aviso.titulo}
        >
          {aviso.texto}
        </Alert>
      )}
      {falha && (
        <Alert color={falha.cor} role="alert">
          {falha.texto}
        </Alert>
      )}
      <Table.ScrollContainer minWidth={640}>
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
            {linhas.map((linha) => {
              if (linha.tipo === 'senha') {
                return (
                  <Table.Tr key="senha">
                    <Table.Td fw={600}>Senha</Table.Td>
                    <Table.Td ff="monospace" fz="xs">
                      {email}
                    </Table.Td>
                    <Table.Td>—</Table.Td>
                    <Table.Td>—</Table.Td>
                    <Table.Td>—</Table.Td>
                  </Table.Tr>
                );
              }
              if (linha.tipo === 'livre') {
                return (
                  <Table.Tr key={`livre-${linha.chave}`}>
                    <Table.Td fw={600}>{linha.nome}</Table.Td>
                    <Table.Td>Não conectada</Table.Td>
                    <Table.Td>—</Table.Td>
                    <Table.Td>—</Table.Td>
                    <Table.Td>
                      {conectando !== linha.chave && (
                        <Button
                          size="xs"
                          onClick={() => {
                            fecharTudo();
                            setConectando(linha.chave);
                          }}
                        >
                          Conectar {linha.nome}
                        </Button>
                      )}
                    </Table.Td>
                  </Table.Tr>
                );
              }
              const c = linha.conta;
              const destacada =
                c.chave !== null && (confirmando === c.chave || erro409 === c.chave);
              return (
                <Table.Tr
                  key={`conta-${c.chave ?? 'fora'}-${c.conectadaEm}`}
                  bg={destacada ? 'var(--mantine-color-red-light)' : undefined}
                >
                  <Table.Td fw={600}>{providerLabel(c.tipo)}</Table.Td>
                  <Table.Td>
                    <Group gap="xs">
                      <Text ff="monospace" fz="xs">
                        {c.email ?? '—'}
                      </Text>
                      {!c.disponivel && (
                        <Badge color="gray" variant="light" tt="none">
                          Provedor fora da configuração
                        </Badge>
                      )}
                    </Group>
                  </Table.Td>
                  <Table.Td>{formatDate(c.conectadaEm)}</Table.Td>
                  <Table.Td>{c.ultimoUso ? formatRelative(c.ultimoUso) : '—'}</Table.Td>
                  <Table.Td>{acoesDaConta(c)}</Table.Td>
                </Table.Tr>
              );
            })}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>
      {conectando && (
        <ConnectProviderForm chave={conectando} onCancel={fecharTudo} onNotConfigured={reload} />
      )}
      <Text size="xs" c="dimmed">
        Cada forma de entrada leva ao mesmo usuário do Buteco. A conta Google só entra se estiver
        conectada aqui ou se você aceitou o convite com ela. A última forma de entrada não pode ser
        removida.
      </Text>
    </AccountCard>
  );
}
