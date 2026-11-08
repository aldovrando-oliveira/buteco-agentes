import { Box, Button, Stack, Text, Title } from '@mantine/core';
import { formatLinkExpiry } from '../../auth/linkExpiry';
import { CopyableLink } from './CopyableLink';

// O link de redefinição, mostrado uma vez, com a forma do quadro `UsuarioDetalhe`
// (`acao=link-gerado`; decisão do dono Q11 da change editar-usuario-painel): sem o e-mail, o
// ícone e o aviso amarelo do painel do convite, e o texto num parágrafo só, com o vencimento da
// resposta (Q5). O link chega por prop do estado da página e não vai a storage, URL,
// `history.state` nem cache: sai com "Concluir", com outra ação e ao desmontar (D7).

interface PasswordResetLinkPanelProps {
  link: string;
  expiraEm: string;
  onDone: () => void;
}

export function PasswordResetLinkPanel({ link, expiraEm, onDone }: PasswordResetLinkPanelProps) {
  return (
    <Box
      role="status"
      aria-labelledby="reset-link-titulo"
      px="md"
      py="md"
      bg="var(--mantine-primary-color-light)"
      style={{ borderTop: '1px solid var(--mantine-color-default-border)' }}
    >
      <Stack gap="sm" maw={720}>
        <Title order={3} id="reset-link-titulo" fz="sm" fw={600}>
          Link para redefinir a senha
        </Title>
        <CopyableLink link={link} />
        <Text size="xs" c="dimmed">
          Envie o link à pessoa pelo canal que preferir. Vale até{' '}
          <b>{formatLinkExpiry(expiraEm)}</b> e funciona uma única vez. O Buteco não mostra este
          link de novo; se ele se perder, gere outro, e o anterior deixa de funcionar.
        </Text>
        <div>
          <Button variant="default" size="sm" onClick={onDone}>
            Concluir
          </Button>
        </div>
      </Stack>
    </Box>
  );
}
