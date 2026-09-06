import { Anchor, Group } from '@mantine/core';
import { ChevronLeft } from 'lucide-react';
import { Link } from 'react-router';

interface BackLinkProps {
  /** Rota de destino. Fixa, e não histórico, para funcionar em acesso direto pela URL. */
  to: string;
  /** Nome do destino, exibido no link. */
  label: string;
}

// Volta para a origem, em todas as telas que não são listagem: detalhe, criação
// e edição. Antes disso, sair de qualquer uma delas dependia do botão do
// navegador ou da barra lateral.
export function BackLink({ to, label }: BackLinkProps) {
  return (
    <Anchor component={Link} to={to} size="xs" c="dimmed" underline="never" w="fit-content">
      <Group gap={4} wrap="nowrap">
        <ChevronLeft size={14} />
        {label}
      </Group>
    </Anchor>
  );
}
