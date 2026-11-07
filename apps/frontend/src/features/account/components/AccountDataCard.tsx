import { Box, Text } from '@mantine/core';
import { useQuery } from '@tanstack/react-query';
import { getAccount } from '../api/accountApi';
import { formatDate, formatRelative } from '../../../utils/panelDates';
import { ACCOUNT_QUERY_KEY } from '../queryKeys';
import { AccountCard, CardLoadError, CardLoading } from './AccountCard';

// O cartão "Dados" (requisito "Cartão Dados"): do `GET /account`, e não do id token, que só
// muda na renovação seguinte a uma edição do nome (#219). Só leitura: na M1 a pessoa muda
// o nome e o e-mail por Usuários (#183), e o texto fica sem link até a tela existir
// (decisão do dono Q3; a #183 o transforma em link).
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
          <Text size="sm" c="dimmed">Para mudar o nome ou o e-mail, use Usuários.</Text>
        </>
      )}
    </AccountCard>
  );
}
