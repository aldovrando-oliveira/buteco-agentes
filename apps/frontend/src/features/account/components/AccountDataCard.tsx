import { Anchor, Box, Text } from '@mantine/core';
import { Link } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { getAccount } from '../api/accountApi';
import { formatDate, formatRelative } from '../../../utils/panelDates';
import { ACCOUNT_QUERY_KEY } from '../queryKeys';
import { AccountCard, CardLoadError, CardLoading } from './AccountCard';

// O cartão "Dados" (requisito "Cartão Dados"): do `GET /account`, e não do id token, que só
// muda na renovação seguinte a uma edição do nome (#219). Só leitura: na M1 a pessoa muda
// o nome e o e-mail em Editar usuário, e "Usuários" leva ao detalhe do próprio, pelo `id` do
// `GET /account`, como no quadro `MinhaConta` (design.md da change editar-usuario-painel, D1).
export function AccountDataCard() {
  const query = useQuery({ queryKey: ACCOUNT_QUERY_KEY, queryFn: getAccount });

  return (
    <AccountCard title="Dados">
      {query.isPending ? (
        <CardLoading label="Carregando os dados" />
      ) : query.isError ? (
        <CardLoadError error={query.error} onRetry={() => void query.refetch()} />
      ) : (
        <>
          <Box component="dl" m={0} style={{ display: 'grid', gridTemplateColumns: '160px 1fr', rowGap: 6, columnGap: 16 }}>
            <Text component="dt" size="sm" c="dimmed">Nome</Text>
            <Text component="dd" size="sm" m={0}>{query.data.nome}</Text>
            <Text component="dt" size="sm" c="dimmed">E-mail</Text>
            <Text component="dd" size="sm" m={0} ff="monospace">{query.data.email}</Text>
            <Text component="dt" size="sm" c="dimmed">No painel desde</Text>
            <Text component="dd" size="sm" m={0}>{formatDate(query.data.criadoEm)}</Text>
            <Text component="dt" size="sm" c="dimmed">Último acesso</Text>
            <Text component="dd" size="sm" m={0}>
              {query.data.ultimoAcesso ? formatRelative(query.data.ultimoAcesso) : '—'}
            </Text>
          </Box>
          <Text size="sm" c="dimmed">
            Para mudar o nome ou o e-mail, use{' '}
            <Anchor component={Link} to={`/users/${encodeURIComponent(query.data.id)}`} size="sm">
              Usuários
            </Anchor>
            .
          </Text>
        </>
      )}
    </AccountCard>
  );
}
