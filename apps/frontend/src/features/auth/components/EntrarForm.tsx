import { Button, PasswordInput, Stack, TextInput } from '@mantine/core';
import { useForm } from '@mantine/form';
import type { SessionInput } from '../api/sessionApi';

interface EntrarFormProps {
  onSubmit: (values: SessionInput) => void;
  onIncomplete: () => void;
  submitting?: boolean;
  disabled?: boolean;
}

// O cartão do protótipo "Login — sem provedor federado", sem o "Esqueceu a senha?":
// não há redefinição antes da #181 (design.md da change painel-entra-pelo-emissor,
// D6, decisão do dono P8).
export function EntrarForm({ onSubmit, onIncomplete, submitting, disabled }: EntrarFormProps) {
  const form = useForm<SessionInput>({ initialValues: { email: '', senha: '' } });

  return (
    <form
      onSubmit={form.onSubmit((values) => {
        // Uma mensagem só para os dois campos, como o servidor responde no 400.
        if (!values.email.trim() || !values.senha) {
          onIncomplete();
          return;
        }
        onSubmit({ email: values.email.trim(), senha: values.senha });
      })}
      noValidate
    >
      <Stack gap="sm">
        <TextInput
          label="E-mail"
          type="email"
          autoComplete="username"
          placeholder="voce@empresa.com"
          autoFocus
          {...form.getInputProps('email')}
        />
        <PasswordInput
          label="Senha"
          autoComplete="current-password"
          placeholder="Senha"
          {...form.getInputProps('senha')}
        />
        <Button type="submit" fullWidth loading={submitting} disabled={disabled}>
          Entrar
        </Button>
      </Stack>
    </form>
  );
}
