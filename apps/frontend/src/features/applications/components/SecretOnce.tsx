import { Alert, Box, Button, Checkbox, CopyButton, Group, Stack, Text } from '@mantine/core';
import { Check, Copy } from 'lucide-react';
import type { ReactNode } from 'react';
import { GUARDEI, SEGREDO_SO_AGORA_DEPOIS } from '../applicationsTexts';

// As peças do segredo mostrado uma vez (design.md da change aplicacoes-externas-painel, D3): o
// campo com "Copiar"/"Copiado" e o aviso com a caixa "Guardei o segredo…". Quem as usa guarda o
// segredo no próprio `useState`, e em nenhum outro lugar.

export function CopyableValue({ label, value }: { label: string; value: string }) {
  return (
    <Stack gap={6}>
      <Text size="sm" fw={500}>
        {label}
      </Text>
      <Group
        gap="xs"
        wrap="nowrap"
        px="sm"
        py={6}
        style={{
          border: '1px solid var(--mantine-color-default-border)',
          borderRadius: 'var(--mantine-radius-sm)',
          background: 'var(--buteco-surface-subtle)',
        }}
      >
        <Text ff="monospace" size="sm" style={{ flex: 1, minWidth: 0, overflowWrap: 'anywhere' }}>
          {value}
        </Text>
        <CopyButton value={value} timeout={2000}>
          {({ copied, copy }) => (
            <Button
              size="compact-sm"
              variant="default"
              onClick={copy}
              aria-label={`Copiar ${label.toLowerCase()}`}
              leftSection={copied ? <Check size={12} /> : <Copy size={12} />}
            >
              {copied ? 'Copiado' : 'Copiar'}
            </Button>
          )}
        </CopyButton>
      </Group>
    </Stack>
  );
}

interface OnlyNowNoticeProps {
  /** O resto do aviso, depois de "…mostrá-lo de novo.". */
  complemento?: string;
}

/** "O segredo aparece **só agora**. O Buteco guarda apenas o hash…" */
export function OnlyNowNotice({ complemento = '' }: OnlyNowNoticeProps) {
  return (
    <Alert color="yellow" role="alert" p="sm">
      <Text size="sm">
        O segredo aparece <b>só agora</b>
        {SEGREDO_SO_AGORA_DEPOIS}
        {complemento}
      </Text>
    </Alert>
  );
}

interface SavedGateProps {
  guardei: boolean;
  onGuardei: (valor: boolean) => void;
  /** O botão que a caixa libera: recebe o `disabled`. */
  children: (desabilitado: boolean) => ReactNode;
}

/** A caixa "Guardei o segredo em um local seguro", que libera o botão de sair do segredo. */
export function SavedGate({ guardei, onGuardei, children }: SavedGateProps) {
  return (
    <Stack gap="sm">
      <Checkbox
        label={GUARDEI}
        checked={guardei}
        onChange={(event) => onGuardei(event.currentTarget.checked)}
      />
      <Box>{children(!guardei)}</Box>
    </Stack>
  );
}
