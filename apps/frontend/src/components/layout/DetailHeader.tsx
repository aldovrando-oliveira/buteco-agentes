import { Group, Stack, Text, Title } from '@mantine/core';
import type { ReactNode } from 'react';
import { BackLink } from './BackLink';

interface DetailHeaderProps {
  /** Rota da listagem de origem. */
  backTo: string;
  /** Nome da listagem, exibido no link de volta. */
  backLabel: string;
  title: string;
  /** Badge de estado, exibido ao lado do título. */
  status?: ReactNode;
  description?: string;
  /** Ações do registro, alinhadas à direita. */
  actions?: ReactNode;
}

// Cabeçalho das páginas de detalhe. As três repetiam a mesma estrutura de
// título, estado, descrição e ações, e nenhuma tinha volta para a listagem:
// voltar dependia do botão do navegador ou da barra lateral. O link é uma rota
// fixa, e não histórico, para funcionar também em acesso direto pela URL
// (design.md da change frontend-acabamento-telas, D4).
export function DetailHeader({
  backTo,
  backLabel,
  title,
  status,
  description,
  actions,
}: DetailHeaderProps) {
  return (
    <Stack gap={4}>
      <BackLink to={backTo} label={backLabel} />
      <Group justify="space-between" align="flex-start" wrap="nowrap">
        <Stack gap={4}>
          <Group gap="xs">
            <Title order={2}>{title}</Title>
            {status}
          </Group>
          <Text size="sm" c={description ? undefined : 'dimmed'}>
            {description || 'Sem descrição.'}
          </Text>
        </Stack>
        {actions && (
          <Group wrap="nowrap" gap="sm">
            {actions}
          </Group>
        )}
      </Group>
    </Stack>
  );
}
