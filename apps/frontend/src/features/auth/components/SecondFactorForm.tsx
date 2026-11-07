import { useState } from 'react';
import { Alert, Anchor, Button, Group, Stack, Text, TextInput } from '@mantine/core';
import { lockText } from '../entryNotices';
import { INFORME_O_CODIGO, MUITAS_TENTATIVAS, SENHA_SALVA_COM_CODIGO } from '../messages';

// O passo das 2 etapas do quadro `Main` (estados `mfa*`; design.md da change
// entrada-publica-painel, D1 e D2). O código do app (6 dígitos) e o código de recuperação
// (16 caracteres) entram no mesmo campo (#218 D6): o teclado é de texto, e o painel só
// tira os espaços das pontas, porque a normalização é do servidor.

export type SecondFactorNotice =
  | { tipo: 'senha-salva' }
  | { tipo: 'bloqueado'; minutos: number | null }
  | { tipo: 'muitas-tentativas' }
  | { tipo: 'erro'; texto: string }
  | null;

interface SecondFactorFormProps {
  onSubmit: (codigo: string) => void;
  onBack: () => void;
  notice: SecondFactorNotice;
  /** "Código inválido." do servidor; o campo vazio é conferido aqui. */
  codeError: string | null;
  submitting?: boolean;
  disabled?: boolean;
}

export function SecondFactorForm({
  onSubmit,
  onBack,
  notice,
  codeError,
  submitting,
  disabled,
}: SecondFactorFormProps) {
  const [codigo, setCodigo] = useState('');
  const [vazio, setVazio] = useState(false);
  const erro = vazio ? INFORME_O_CODIGO : codeError;

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        const valor = codigo.trim();
        if (!valor) {
          setVazio(true);
          return;
        }
        setVazio(false);
        onSubmit(valor);
      }}
      noValidate
    >
      <Stack gap="md">
        <Stack gap={4} align="center" ta="center">
          <Text fw={600} fz={15}>
            Verificação em duas etapas
          </Text>
          <Text c="dimmed" fz="sm">
            Digite o código de 6 dígitos do seu app autenticador. Sem o celular, use um dos seus
            códigos de recuperação.
          </Text>
        </Stack>

        {notice?.tipo === 'senha-salva' && (
          <Alert color="green" role="status">
            {SENHA_SALVA_COM_CODIGO}
          </Alert>
        )}
        {notice?.tipo === 'bloqueado' && (
          <Alert color="yellow" role="alert">
            {lockText(notice.minutos)}
          </Alert>
        )}
        {notice?.tipo === 'muitas-tentativas' && (
          <Alert color="yellow" role="alert">
            {MUITAS_TENTATIVAS}
          </Alert>
        )}
        {notice?.tipo === 'erro' && (
          <Alert color="red" role="alert">
            {notice.texto}
          </Alert>
        )}

        <TextInput
          label="Código"
          value={codigo}
          onChange={(event) => setCodigo(event.currentTarget.value)}
          inputMode="text"
          autoCapitalize="characters"
          autoComplete="one-time-code"
          spellCheck={false}
          placeholder="000 000"
          autoFocus
          error={erro}
          description="Código de recuperação: 16 letras e números, como XXXX-XXXX-XXXX-XXXX."
          inputWrapperOrder={['label', 'input', 'error', 'description']}
          styles={{
            input: {
              textAlign: 'center',
              letterSpacing: '0.2em',
              fontFamily: 'var(--mantine-font-family-monospace)',
              fontSize: 16,
            },
          }}
        />

        <Button type="submit" fullWidth loading={submitting} disabled={disabled}>
          Verificar
        </Button>

        <Group justify="space-between" gap="sm" wrap="nowrap" fz="xs">
          <Anchor component="button" type="button" fz="xs" onClick={onBack}>
            Voltar
          </Anchor>
          <Text c="dimmed" fz="xs" ta="right">
            Perdeu o celular e os códigos? Fale com o administrador.
          </Text>
        </Group>
      </Stack>
    </form>
  );
}
