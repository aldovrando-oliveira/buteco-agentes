import { PasswordField } from '../../../components/forms/PasswordField';

// "Sua senha": a senha de quem chama, nas ações que transformam um access token roubado em
// acesso permanente (#181, D2 e P10). O `PasswordField` põe o `aria-invalid` no erro (#245 D6).

interface CallerPasswordFieldProps {
  value: string;
  onChange: (value: string) => void;
  error: string | null;
  /** A ajuda do quadro: "Para convidar alguém, confirme que é você.", por exemplo. */
  description: string;
}

export function CallerPasswordField({ value, onChange, error, description }: CallerPasswordFieldProps) {
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
