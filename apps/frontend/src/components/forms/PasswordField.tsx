// O único lugar do painel que usa o PasswordInput do Mantine (o import direto é barrado
// pelo `passwordFieldGuard.test.ts` e pelo `no-restricted-imports` do eslint.config.js).
// eslint-disable-next-line no-restricted-imports
import { PasswordInput, type PasswordInputProps } from '@mantine/core';

// O PasswordInput do Mantine não marca o <input> com aria-invalid quando o campo tem
// erro: chama o Input com `withAria: false` e põe só `data-invalid` no <input>
// (@mantine/core/esm/components/PasswordInput/PasswordInput.mjs:159 e :165 na 9.4.2;
// :169 e :175 na 9.7.5, a mais nova em 05/11/2026). O TextInput põe o atributo sozinho
// (Input.mjs:75). As props que sobram chegam ao <input> pelo `...rest` (:171 na 9.4.2),
// então o atributo vai explícito aqui, para todo campo de senha do painel (#170; design.md
// da change entrada-publica-painel, D6).
export function PasswordField(props: PasswordInputProps) {
  return <PasswordInput {...props} aria-invalid={props.error ? true : undefined} />;
}
