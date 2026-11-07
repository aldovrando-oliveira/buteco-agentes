import { useState, type ReactNode } from 'react';
import { Button, Stack, TextInput } from '@mantine/core';
import { PasswordField } from '../../../components/forms/PasswordField';
import { POLITICA_DE_SENHA, SENHAS_DIFERENTES } from '../messages';
import { passwordFitsPolicy } from '../passwordPolicy';

// O formulário da senha nova dos quadros `AceitarConviteSemProvedor` e `RedefinirSenha`
// (spec `panel-access-link-pages`, "O formulário da senha nova").

interface NewPasswordFormProps {
  email: string;
  passwordLabel: string;
  submitLabel: string;
  /** O aviso entre os campos e o botão (o da redefinição). */
  notice?: ReactNode;
  /** O `title` do `400` do servidor, no primeiro campo. */
  serverPasswordError?: string | null;
  onSubmit: (senha: string) => void;
  submitting?: boolean;
  disabled?: boolean;
}

export function NewPasswordForm({
  email,
  passwordLabel,
  submitLabel,
  notice,
  serverPasswordError,
  onSubmit,
  submitting,
  disabled,
}: NewPasswordFormProps) {
  const [senha, setSenha] = useState('');
  const [repeticao, setRepeticao] = useState('');
  const [foraDaRegra, setForaDaRegra] = useState(false);
  const [diferentes, setDiferentes] = useState(false);

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        const regra = !passwordFitsPolicy(senha);
        const diferente = !regra && senha !== repeticao;
        setForaDaRegra(regra);
        setDiferentes(diferente);
        if (regra || diferente) return;
        onSubmit(senha);
      }}
      noValidate
    >
      <Stack gap="sm">
        {/* E-mail travado, com `autocomplete="username"`, para o gerenciador de senhas
            guardar a senha nova com o usuário certo (#181 D7). */}
        <TextInput label="E-mail" type="email" value={email} readOnly autoComplete="username" />
        <PasswordField
          label={passwordLabel}
          autoComplete="new-password"
          value={senha}
          onChange={(event) => setSenha(event.currentTarget.value)}
          error={foraDaRegra ? POLITICA_DE_SENHA : (serverPasswordError ?? undefined)}
          description="De 12 a 128 caracteres."
          inputWrapperOrder={['label', 'input', 'error', 'description']}
        />
        <PasswordField
          label="Repita a senha"
          autoComplete="new-password"
          value={repeticao}
          onChange={(event) => setRepeticao(event.currentTarget.value)}
          error={diferentes ? SENHAS_DIFERENTES : undefined}
        />
        {notice}
        <Button type="submit" fullWidth loading={submitting} disabled={disabled}>
          {submitLabel}
        </Button>
      </Stack>
    </form>
  );
}
