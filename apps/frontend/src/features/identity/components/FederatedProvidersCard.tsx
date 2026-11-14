import { Badge, Box, Button, Group, Loader, Stack, Table, Text } from '@mantine/core';
import { useQuery } from '@tanstack/react-query';
import { SectionedCard } from '../../../components/data/SectionedCard';
import { FEDERATED_PROVIDERS_QUERY_KEY, getFederatedProviders } from '../api/identityApi';
import { linkCount, usageLabel } from '../identityTexts';

// O cartão "Provedores federados" da Identidade (requisito "Cartão Provedores federados"; design.md da
// change telas-contas-federadas, D7; quadro `Provedores`, `entrega=m3`, versão 81). Só leitura: os
// provedores vêm da configuração do servidor, e não há "Adicionar provedor", "Editar" nem "Chaves" (o
// estado do JWKS do provedor não é servido, Q11).
// Razões do desenho:
// 1. A consulta é própria: o erro dela não esconde o emissor e as chaves, nem o contrário.
// 2. Sem nova tentativa automática: o texto do erro não promete "o painel tenta de novo sozinho", ao
//    contrário do cartão do emissor (convenção 20). Só o "Tentar de novo".
// 3. O `403` não tem ramo aqui: a página já mostra o acesso negado pelo `GET /identity`, que é do
//    mesmo papel.

export function FederatedProvidersCard() {
  const query = useQuery({
    queryKey: FEDERATED_PROVIDERS_QUERY_KEY,
    queryFn: getFederatedProviders,
  });

  return (
    <SectionedCard
      title="Provedores federados"
      action={
        <Text size="xs" c="dimmed">
          Definidos na configuração do servidor
        </Text>
      }
    >
      {query.isPending ? (
        <Group role="status" gap="sm" px="md" py="md">
          <Loader size="sm" />
          <Text size="sm" c="dimmed">
            Carregando os provedores…
          </Text>
        </Group>
      ) : query.isError ? (
        <Group role="alert" justify="space-between" px="md" py="md" gap="sm">
          <Text size="sm" c="dimmed">
            Não foi possível carregar os provedores federados agora.
          </Text>
          <Button
            variant="default"
            size="sm"
            onClick={() => void query.refetch()}
            loading={query.isFetching}
          >
            Tentar de novo
          </Button>
        </Group>
      ) : query.data.length === 0 ? (
        <Text size="sm" px="md" py="md">
          Nenhum provedor federado configurado. O login fica só com e-mail e senha.
        </Text>
      ) : (
        <Table.ScrollContainer minWidth={960}>
          <Table verticalSpacing="sm" horizontalSpacing="md">
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Provedor</Table.Th>
                <Table.Th>Usado para</Table.Th>
                <Table.Th>Issuer</Table.Th>
                <Table.Th>ID do cliente</Table.Th>
                <Table.Th>URI de retorno</Table.Th>
                <Table.Th>Vínculos</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {query.data.map((provedor) => (
                <Table.Tr key={provedor.chave}>
                  <Table.Td>
                    <Stack gap={0}>
                      <Text size="sm" fw={600}>
                        {provedor.nome}
                      </Text>
                      <Text ff="monospace" fz="xs" c="dimmed">
                        {provedor.chave}
                      </Text>
                    </Stack>
                  </Table.Td>
                  <Table.Td>
                    <Group gap={4}>
                      {provedor.usadoPara.map((uso) => {
                        const rotulo = usageLabel(uso);
                        return (
                          <Badge
                            key={uso}
                            color={rotulo.conhecido ? 'blue' : 'gray'}
                            variant={rotulo.conhecido ? 'light' : 'outline'}
                            tt="none"
                          >
                            {rotulo.texto}
                          </Badge>
                        );
                      })}
                    </Group>
                  </Table.Td>
                  <Table.Td ff="monospace" fz="xs">
                    {provedor.issuer}
                  </Table.Td>
                  <Table.Td ff="monospace" fz="xs" style={{ overflowWrap: 'anywhere' }}>
                    {provedor.clientId}
                  </Table.Td>
                  <Table.Td ff="monospace" fz="xs" style={{ overflowWrap: 'anywhere' }}>
                    {provedor.uriDeRetorno}
                  </Table.Td>
                  <Table.Td>{linkCount(provedor.vinculos)}</Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}
      <Box px="md" py="xs" style={{ borderTop: '1px solid var(--mantine-color-default-border)' }}>
        <Text size="xs" c="dimmed">
          Quem entra pelo Google precisa estar ligado a um usuário do Buteco, pelo aceite de um
          convite ou conectando a conta em Minha conta. O Google confirma a identidade; o Buteco
          emite a sessão e pede o próprio código da verificação em 2 etapas. Vínculos é o número de
          usuários com uma conta conectada a este provedor.
        </Text>
      </Box>
    </SectionedCard>
  );
}
