import { Anchor, Center, Paper, Stack, Text, ThemeIcon, Title } from '@mantine/core';
import { Lock } from 'lucide-react';
import { Link } from 'react-router';
import { PRAZO_DO_PAPEL, SEM_ACESSO_PAGINA } from './accessTexts';

/**
 * A tela de acesso negado, no lugar do conteúdo da página e dentro da casca (quadro
 * `AcessoNegado`, `estado=pagina`; spec `panel-roles`). Uma só, para a guarda de rota pelo
 * papel do token e para o `403` na carga da consulta principal de uma página.
 */
export function AccessDenied() {
  return (
    <Center py={80}>
      <Paper withBorder radius="md" p={28} maw={460} role="alert">
        <Stack gap={10} align="center" ta="center">
          <ThemeIcon variant="light" color="gray" size={40} radius="xl" aria-hidden="true">
            <Lock size={20} />
          </ThemeIcon>
          <Title order={1} fz={18} fw={600}>
            {SEM_ACESSO_PAGINA}
          </Title>
          <Text c="dimmed" size="sm">
            {PRAZO_DO_PAPEL}
          </Text>
          <Anchor component={Link} to="/inventory" size="sm" mt={6}>
            Ir para o Inventário
          </Anchor>
        </Stack>
      </Paper>
    </Center>
  );
}
