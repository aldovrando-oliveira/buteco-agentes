import { Alert, Button, PasswordInput, Stack, TextInput } from '@mantine/core';
import { useForm } from '@mantine/form';
import type { LoginInput } from '../types/auth';

interface LoginFormProps {
  onSubmit: (values: LoginInput) => void;
  submitting?: boolean;
  errorMessage?: string;
}

export function LoginForm({ onSubmit, submitting, errorMessage }: LoginFormProps) {
  const form = useForm<LoginInput>({
    initialValues: { username: '', password: '' },
    validate: {
      username: (value) => (value.trim() ? null : 'O usuário é obrigatório.'),
      password: (value) => (value.trim() ? null : 'A senha é obrigatória.'),
    },
  });

  return (
    <form onSubmit={form.onSubmit((values) => onSubmit(values))}>
      <Stack>
        {errorMessage && <Alert color="red">{errorMessage}</Alert>}
        <TextInput
          label="Usuário"
          placeholder="Usuário"
          withAsterisk
          autoFocus
          {...form.getInputProps('username')}
        />
        <PasswordInput
          label="Senha"
          placeholder="Senha"
          withAsterisk
          {...form.getInputProps('password')}
        />
        <Button type="submit" loading={submitting}>
          Entrar
        </Button>
      </Stack>
    </form>
  );
}
