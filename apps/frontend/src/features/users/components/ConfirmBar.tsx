import { Alert, Box, Button, Group, Stack, Text } from '@mantine/core';
import type { FailureNotice } from '../../../utils/failureNotice';

// A confirmação de Editar usuário (o `.confirmbox` do quadro `UsuarioDetalhe`): o texto em
// vermelho, o botão de perigo e "Cancelar", com o aviso da falha logo abaixo. Desativar,
// redefinir a verificação e encerrar as sessões (design.md da change editar-usuario-painel,
// D4, D8 a D10).

interface ConfirmBarProps {
  texto: string;
  confirmLabel: string;
  onConfirm: () => void;
  onCancel: () => void;
  enviando: boolean;
  falha: FailureNotice | null;
  /** Acima dos cartões, a confirmação tem borda e cantos próprios; dentro do cartão, só a borda superior. */
  solta?: boolean;
}

export function ConfirmBar({
  texto,
  confirmLabel,
  onConfirm,
  onCancel,
  enviando,
  falha,
  solta = false,
}: ConfirmBarProps) {
  return (
    <Box
      role="alert"
      px="md"
      py="sm"
      bg="var(--mantine-color-red-light)"
      style={
        solta
          ? {
              border: '1px solid var(--mantine-color-red-light-hover)',
              borderRadius: 'var(--mantine-radius-md)',
            }
          : { borderTop: '1px solid var(--mantine-color-red-light-hover)' }
      }
    >
      <Stack gap="xs">
        <Group justify="space-between" gap="sm">
          <Text size="sm" c="var(--mantine-color-red-light-color)" maw={640}>
            {texto}
          </Text>
          <Group gap="sm" wrap="nowrap">
            <Button size="sm" color="red" loading={enviando} onClick={onConfirm}>
              {confirmLabel}
            </Button>
            <Button size="sm" variant="subtle" color="gray" onClick={onCancel} disabled={enviando}>
              Cancelar
            </Button>
          </Group>
        </Group>
        {falha && (
          <Alert color={falha.cor} p="xs">
            <Text size="sm">{falha.texto}</Text>
          </Alert>
        )}
      </Stack>
    </Box>
  );
}
