import { useState } from 'react';
import { Anchor, Button, Group, Stack, Text, TextInput } from '@mantine/core';
import { useForm } from '@mantine/form';
import { PasswordField } from '../../../components/forms/PasswordField';
import type { SessionInput } from '../api/sessionApi';

interface EntrarFormProps {
  onSubmit: (values: SessionInput) => void;
  onIncomplete: () => void;
  /** O e-mail com que a tela abre (vindo de uma página do link). */
  initialEmail?: string;
  submitting?: boolean;
  disabled?: boolean;
}

const SENHA_ID = 'entrar-senha';

// O cartão do quadro `Main` com `federados=false`, estado `inicio` (design.md da change
// entrada-publica-painel). "Esqueceu a senha?" abre na linha o texto que orienta a pedir
// o link ao administrador: não há envio de e-mail na M1 (#181 D1). O rótulo "Senha" é um
// <label> próprio, ao lado do botão, para o botão não entrar no nome acessível do campo.
export function EntrarForm({ onSubmit, onIncomplete, initialEmail, submitting, disabled }: EntrarFormProps) {
  const form = useForm<SessionInput>({ initialValues: { email: initialEmail ?? '', senha: '' } });
  const [esqueceu, setEsqueceu] = useState(false);

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
          autoFocus={!initialEmail}
          {...form.getInputProps('email')}
        />
        <Stack gap={4}>
          <Group justify="space-between" align="baseline" wrap="nowrap">
            <Text component="label" htmlFor={SENHA_ID} fz="sm" fw={500}>
              Senha
            </Text>
            <Anchor
              component="button"
              type="button"
              fz="xs"
              aria-expanded={esqueceu}
              onClick={() => setEsqueceu((aberto) => !aberto)}
            >
              Esqueceu a senha?
            </Anchor>
          </Group>
          <PasswordField
            id={SENHA_ID}
            autoComplete="current-password"
            placeholder="Senha"
            autoFocus={Boolean(initialEmail)}
            {...form.getInputProps('senha')}
          />
        </Stack>
        {esqueceu && (
          <Text
            fz="xs"
            c="dimmed"
            p="xs"
            bg="var(--buteco-surface-subtle)"
            style={{ border: '1px solid var(--mantine-color-default-border)', borderRadius: 'var(--mantine-radius-sm)' }}
          >
            Peça a um administrador do painel um link para criar uma nova senha. O link vale por 24 horas.
          </Text>
        )}
        <Button type="submit" fullWidth loading={submitting} disabled={disabled}>
          Entrar
        </Button>
      </Stack>
    </form>
  );
}
