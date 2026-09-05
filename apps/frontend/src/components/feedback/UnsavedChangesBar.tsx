import { Button, Group, Loader, Paper, Text } from '@mantine/core';

interface UnsavedChangesBarProps {
  message: string;
  saveLabel: string;
  savingMessage?: string;
  saving?: boolean;
  onDiscard: () => void;
  onSave: () => void;
}

// Barra fixa no rodapé, exibida só enquanto há diferença entre o rascunho
// e o estado salvo. Durante o salvamento mostra um indicador sem contagem:
// o PUT é uma requisição única e atômica, e o cliente não recebe nenhum
// sinal intermediário sobre qual servidor está sendo validado (Decision 6
// do design.md da change frontend-agente-detalhe-abas).
export function UnsavedChangesBar({
  message,
  saveLabel,
  savingMessage,
  saving = false,
  onDiscard,
  onSave,
}: UnsavedChangesBarProps) {
  return (
    <Paper
      withBorder
      shadow="md"
      radius={0}
      px="md"
      py="sm"
      pos="fixed"
      bottom={0}
      left={0}
      right={0}
      style={{ zIndex: 200 }}
      role="region"
      aria-label="Alterações não salvas"
      data-testid="unsaved-changes-bar"
    >
      <Group justify="space-between" wrap="nowrap">
        <Group gap="xs" wrap="nowrap">
          {saving && <Loader size="xs" />}
          <Text size="sm">{saving ? (savingMessage ?? 'Salvando…') : message}</Text>
        </Group>
        <Group gap="sm" wrap="nowrap">
          <Button variant="default" onClick={onDiscard} disabled={saving}>
            Descartar
          </Button>
          <Button onClick={onSave} loading={saving}>
            {saveLabel}
          </Button>
        </Group>
      </Group>
    </Paper>
  );
}
