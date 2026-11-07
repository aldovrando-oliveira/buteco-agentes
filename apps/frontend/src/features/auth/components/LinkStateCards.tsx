import { Button, Loader, Stack, Text, Title } from '@mantine/core';
import { Link } from 'react-router';
import { Logo } from '../../../components/brand/Logo';

// Os estados das páginas do link que trocam o formulário inteiro: carregando, indisponível
// e link inválido (quadros `AceitarConviteSemProvedor` e `RedefinirSenha`; spec
// `panel-access-link-pages`, "Consulta, carregando, indisponível e link inválido").

export function LoadingLinkCard() {
  return (
    <Stack role="status" align="center" gap="sm" py="lg" ta="center">
      <Loader size="md" />
      <Text c="dimmed" fz="sm">
        Conferindo o link…
      </Text>
    </Stack>
  );
}

export function UnavailableLinkCard({ onRetry }: { onRetry: () => void }) {
  return (
    <Stack role="alert" align="center" gap="sm" ta="center">
      <Title order={1} fz={18} fw={600}>
        Não foi possível abrir o link agora
      </Title>
      <Text c="dimmed" fz="sm">
        O painel está indisponível ou sem conexão. O link continua valendo: tente de novo em alguns segundos.
      </Text>
      <Button fullWidth mt={6} onClick={onRetry}>
        Tentar de novo
      </Button>
    </Stack>
  );
}

/** O texto é o do quadro de cada página, e não o `title` do servidor (decisão do dono, Q5). */
export function InvalidLinkCard({ text }: { text: string }) {
  return (
    <Stack align="center" gap="sm" ta="center">
      <Logo variant="symbol" size={54} />
      <Title order={1} fz={18} fw={600}>
        Este link não vale mais
      </Title>
      <Text c="dimmed" fz="sm">
        {text}
      </Text>
      <Button component={Link} to="/entrar" fullWidth mt={6}>
        Ir para o login
      </Button>
    </Stack>
  );
}
