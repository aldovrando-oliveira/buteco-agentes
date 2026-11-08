import { Alert, Box, Button, Group, Stack, Text, ThemeIcon, Title } from '@mantine/core';
import { Check } from 'lucide-react';
import { formatLinkExpiry } from '../../auth/linkExpiry';
import { CopyableLink } from './CopyableLink';

// O link do convite, mostrado uma vez (requisito "Link de convite mostrado uma vez e só na
// memória da tela"; design.md da change usuarios-convites-identidade-painel, D5). O link chega
// por prop do estado do cartão e não vai a storage, URL, `history.state` nem cache: sai com
// "Concluir", com outro painel e ao desmontar a página.

interface InviteLinkPanelProps {
  titulo: string;
  email: string;
  link: string;
  expiraEm: string;
  onDone: () => void;
}

export function InviteLinkPanel({ titulo, email, link, expiraEm, onDone }: InviteLinkPanelProps) {
  return (
    <Box
      role="status"
      aria-labelledby="link-titulo"
      px="md"
      py="md"
      bg="var(--mantine-primary-color-light)"
      style={{ borderBottom: '1px solid var(--mantine-color-default-border)' }}
    >
      <Stack gap="sm">
        <Group gap="sm" align="flex-start" wrap="nowrap">
          <ThemeIcon color="green" variant="light" radius="xl" size={24} aria-hidden="true">
            <Check size={14} />
          </ThemeIcon>
          <Stack gap={2}>
            <Title order={3} id="link-titulo" fz="sm" fw={600}>
              {titulo}
            </Title>
            <Text size="sm" c="dimmed" ff="monospace">
              {email}
            </Text>
          </Stack>
        </Group>
        <Stack gap={6} maw={720}>
          <Text size="sm" fw={500}>
            Link do convite
          </Text>
          <CopyableLink link={link} />
          <Text size="xs" c="dimmed">
            Envie para a pessoa por e-mail, chat ou outro canal. Vale até <b>{formatLinkExpiry(expiraEm)}</b> e funciona
            uma única vez.
          </Text>
        </Stack>
        <Alert color="yellow" maw={720} p="xs">
          <Text size="xs">
            O Buteco não mostra este link de novo. Se ele se perder ou vencer, use <b>Gerar novo link</b> na linha do
            convite; o link anterior deixa de funcionar.
          </Text>
        </Alert>
        <Group>
          <Button variant="default" size="sm" onClick={onDone}>
            Concluir
          </Button>
        </Group>
      </Stack>
    </Box>
  );
}
