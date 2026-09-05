import { Button, Group, Modal, Text } from '@mantine/core';

interface UnsavedChangesModalProps {
  opened: boolean;
  message: string;
  onConfirm: () => void;
  onCancel: () => void;
}

// Diálogo exibido quando a guarda de navegação intercepta uma saída com
// rascunho não salvo. Continuar editando é a ação neutra; sair descarta.
export function UnsavedChangesModal({
  opened,
  message,
  onConfirm,
  onCancel,
}: UnsavedChangesModalProps) {
  return (
    <Modal opened={opened} onClose={onCancel} title="Alterações não salvas" centered>
      <Text size="sm">{message}</Text>
      <Group justify="flex-end" mt="md">
        <Button variant="default" onClick={onCancel}>
          Continuar editando
        </Button>
        <Button color="red" onClick={onConfirm}>
          Sair e descartar
        </Button>
      </Group>
    </Modal>
  );
}
