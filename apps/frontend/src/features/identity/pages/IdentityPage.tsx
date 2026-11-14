import { Badge, Box, Button, Group, Skeleton, Stack, Text, Title } from '@mantine/core';
import { useQuery } from '@tanstack/react-query';
import { SectionedCard } from '../../../components/data/SectionedCard';
import { getIdentity, IDENTITY_QUERY_KEY } from '../api/identityApi';
import { IssuerCard } from '../components/IssuerCard';
import { SigningKeysCard } from '../components/SigningKeysCard';
import { FederatedProvidersCard } from '../components/FederatedProvidersCard';
import { retryWhileFailing } from '../retryWhileFailing';
import { AccessDenied } from '../../../components/feedback/AccessDenied';
import { is403 } from '../../../utils/failureNotice';

// A página Identidade (requisitos de `panel-identity-page`; design.md da change
// usuarios-convites-identidade-painel, D6). O `retry` é o do QueryClient do painel (até 3
// tentativas, com o `Retry-After` no `503`); depois dele, o cartão de erro, e o polling
// condicional enquanto o erro durar (decisão do dono Q9; convenção 20).

export function IdentityPage() {
  const identity = useQuery({ queryKey: IDENTITY_QUERY_KEY, queryFn: getIdentity, refetchInterval: retryWhileFailing });

  // O `403` é o papel, também o do Admin rebaixado com o token de antes (spec `panel-roles`).
  if (identity.isError && is403(identity.error)) return <AccessDenied />;

  return (
    <Stack>
      <Stack gap={2}>
        <Title order={2}>Identidade</Title>
        <Text size="sm" c="dimmed">
          O Buteco emite os tokens de usuários, aplicações e módulos. Provedores federados são opcionais: sem nenhum, o
          painel funciona só com usuários locais.
        </Text>
      </Stack>

      {identity.isSuccess ? (
        <>
          <IssuerCard emissor={identity.data.emissor} />
          <SigningKeysCard chaves={identity.data.chavesDeAssinatura} />
        </>
      ) : identity.isError ? (
        <Box role="alert">
        <SectionedCard
          title="Emissor do Buteco"
          action={
            <Badge color="yellow" variant="light" tt="none">
              Indisponível
            </Badge>
          }
        >
          <Group justify="space-between" px="md" py="md" gap="sm">
            <Text size="sm" c="dimmed">
              Não foi possível carregar os dados do emissor agora. O painel tenta de novo sozinho; se continuar, confira se
              o api está no ar.
            </Text>
            <Button variant="default" size="sm" onClick={() => void identity.refetch()} loading={identity.isFetching}>
              Tentar de novo
            </Button>
          </Group>
        </SectionedCard>
        </Box>
      ) : (
        <Stack gap="xs" aria-busy="true" aria-label="Carregando os dados do emissor">
          <Skeleton height={14} width="60%" />
          <Skeleton height={14} width="40%" />
        </Stack>
      )}

      {/* Fora do ramo do `GET /identity`: a consulta é própria, e o erro de uma não esconde a outra (D7). */}
      <FederatedProvidersCard />
    </Stack>
  );
}
