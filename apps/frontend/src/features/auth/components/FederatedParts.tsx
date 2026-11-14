import { Alert, Button, Divider, Loader, Stack, Text } from '@mantine/core';
import type { LoginProvider } from '../api/federatedApi';
import { REDIRECIONANDO, type FederatedNotice } from '../federatedNotices';

// As partes do login e do aceite de convite pelo provedor que o /entrar e o /convite dividem
// (design.md da change telas-login-google, D1 a D3).

/** O aviso do quadro: o título em negrito (quando há) e o texto. */
export function FederatedAlert({ notice }: { notice: FederatedNotice }) {
  return (
    <Alert color={notice.cor} role="alert" title={notice.titulo}>
      {notice.texto}
    </Alert>
  );
}

/** Um botão por provedor, com o rótulo pedido. */
export function ProviderButtons({
  providers,
  label,
  onPick,
  disabled,
}: {
  providers: LoginProvider[];
  label: (provider: LoginProvider) => string;
  onPick: (provider: LoginProvider) => void;
  disabled?: boolean;
}) {
  return (
    <Stack gap="xs">
      {providers.map((provider) => (
        <Button
          key={provider.chave}
          variant="default"
          fullWidth
          onClick={() => onPick(provider)}
          disabled={disabled}
        >
          {label(provider)}
        </Button>
      ))}
    </Stack>
  );
}

/** O separador com o texto no meio ("ou entre com", "ou crie uma senha"). */
export function TextDivider({ label }: { label: string }) {
  return <Divider label={label} labelPosition="center" />;
}

/** "Redirecionando para o Google…", sem "Cancelar" (Q7). */
export function RedirectingNotice({ provedor }: { provedor: string }) {
  return (
    <Stack align="center" gap="sm" py="xs" role="status">
      <Loader size="sm" />
      <Text fw={500}>{REDIRECIONANDO(provedor)}</Text>
    </Stack>
  );
}
