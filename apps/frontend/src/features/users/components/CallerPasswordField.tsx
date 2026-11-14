import { TextInput } from '@mantine/core';
import { PasswordField } from '../../../components/forms/PasswordField';
import type { ModoDeConfirmacao } from '../callerPassword';
import { AJUDA_DO_CODIGO } from '../usersTexts';

// "Sua senha": a senha de quem chama, nas ações que transformam um access token roubado em
// acesso permanente (#181, D2 e P10). O `PasswordField` põe o `aria-invalid` no erro (#245 D6).
// No modo código (quem entra só pelo Google; change telas-contas-federadas, D2), o campo do código
// do app autenticador, com o rótulo, a ajuda e o erro de `MinhaConta/confirmar-codigo` (Q6).

interface CallerPasswordFieldProps {
  value: string;
  onChange: (value: string) => void;
  error: string | null;
  /** A ajuda do quadro no modo senha: "Para convidar alguém, confirme que é você.", por exemplo. */
  description: string;
  modo?: ModoDeConfirmacao;
}

export function CallerPasswordField({ value, onChange, error, description, modo = 'senha' }: CallerPasswordFieldProps) {
  if (modo === 'codigo') {
    return (
      <TextInput
        label="Código do app autenticador"
        autoComplete="one-time-code"
        inputMode="numeric"
        description={AJUDA_DO_CODIGO}
        inputWrapperOrder={['label', 'input', 'error', 'description']}
        value={value}
        onChange={(event) => onChange(event.currentTarget.value)}
        error={error ?? undefined}
        maw={360}
      />
    );
  }
  return (
    <PasswordField
      label="Sua senha"
      autoComplete="current-password"
      description={description}
      inputWrapperOrder={['label', 'input', 'error', 'description']}
      value={value}
      onChange={(event) => onChange(event.currentTarget.value)}
      error={error ?? undefined}
      maw={360}
    />
  );
}
