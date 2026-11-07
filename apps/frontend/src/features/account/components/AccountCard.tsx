import type { ReactNode } from 'react';
import { useId } from 'react';
import { Alert, Anchor, Group, Paper, Skeleton, Stack, Text, Title } from '@mantine/core';
import { failureText } from '../cardFailure';

// A moldura dos quatro cartões do quadro `MinhaConta`: o título como cabeçalho da seção,
// a ação do cabeçalho (o "Trocar senha", o "Encerrar as outras") e o corpo.

interface AccountCardProps {
  title: string;
  action?: ReactNode;
  children: ReactNode;
}

export function AccountCard({ title, action, children }: AccountCardProps) {
  const id = useId();
  return (
    <Paper component="section" withBorder radius="md" p="lg" aria-labelledby={id}>
      <Stack gap="md">
        <Group justify="space-between" wrap="nowrap">
          <Title order={3} id={id} fz="sm" fw={600}>
            {title}
          </Title>
          {action}
        </Group>
        {children}
      </Stack>
    </Paper>
  );
}

/** O carregamento do `GET` de um cartão. */
export function CardLoading({ label }: { label: string }) {
  return (
    <Stack gap="xs" aria-busy="true" aria-label={label}>
      <Skeleton height={14} width="60%" />
      <Skeleton height={14} width="40%" />
    </Stack>
  );
}

/** O erro do `GET` de um cartão, com "Tentar de novo" (requisito "Página Minha conta em /account"). */
export function CardLoadError({ error, onRetry }: { error: unknown; onRetry: () => void }) {
  return (
    <Alert color="red" role="alert">
      <Text size="sm">
        {failureText(error)}{' '}
        <Anchor component="button" type="button" size="sm" onClick={onRetry}>
          Tentar de novo
        </Anchor>
      </Text>
    </Alert>
  );
}
