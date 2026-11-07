# frontend-password-field Specification

## Purpose
Diz como o `apps/frontend` mostra um campo de senha (#170): sempre pelo `PasswordField` de `components/forms`, que marca `aria-invalid` no `<input>` quando há erro, e as duas guardas que impedem o `PasswordInput` do Mantine usado direto (o teste estático na suíte e a regra `no-restricted-imports` do ESLint).

## Requirements
### Requirement: Campo de senha com erro marca aria-invalid
Todo campo de senha do `apps/frontend` SHALL ser o `PasswordField` de `components/forms`. Com erro de validação, o `<input>` dele SHALL ter `aria-invalid="true"`. Sem erro, o atributo SHALL estar ausente. A mensagem de erro SHALL continuar ligada ao campo por `aria-describedby`, como o `PasswordInput` do Mantine já faz.

Os campos de senha de hoje SHALL usá-lo: o "Senha" do `/entrar`, o token do WAHA, o token do bot do Telegram e a credencial do servidor MCP, além dos campos de senha das páginas do link (`panel-access-link-pages`).

#### Scenario: Com erro, o input é anunciado como inválido
- **WHEN** um `PasswordField` recebe `error="O token de autenticação é obrigatório."`
- **THEN** o `<input>` tem `aria-invalid="true"` e o `aria-describedby` aponta para o elemento com a mensagem

#### Scenario: Sem erro, sem o atributo
- **WHEN** um `PasswordField` não recebe `error`
- **THEN** o `<input>` não tem o atributo `aria-invalid`

#### Scenario: O token do WAHA obrigatório é anunciado como inválido
- **WHEN** o formulário de canal WAHA é enviado com o token vazio
- **THEN** o `<input>` "Token de autenticação" tem `aria-invalid="true"`

#### Scenario: O token do bot do Telegram obrigatório é anunciado como inválido
- **WHEN** o formulário de canal Telegram é enviado com o token vazio
- **THEN** o `<input>` "Token do bot" tem `aria-invalid="true"`

#### Scenario: A credencial do servidor MCP continua anunciada como inválida
- **WHEN** o formulário de servidor MCP é enviado com a credencial obrigatória vazia
- **THEN** o `<input>` da credencial tem `aria-invalid="true"`

#### Scenario: A senha do /entrar é um PasswordField
- **WHEN** o `/entrar` é aberto
- **THEN** o campo "Senha" é renderizado pelo `PasswordField`

### Requirement: O PasswordInput do Mantine não é usado fora do PasswordField
Nenhum arquivo de `apps/frontend/src` fora de `components/forms/PasswordField.tsx` SHALL importar `PasswordInput` de `@mantine/core`. Um teste estático da suíte `frontend` SHALL reprovar quando isso acontece, apontando o arquivo. O `eslint.config.js` SHALL ter a mesma restrição em `no-restricted-imports`, com o `PasswordField` fora dela.

#### Scenario: Um import direto reprova a suíte
- **WHEN** um arquivo de `src/` fora do `PasswordField` tem `import { PasswordInput } from '@mantine/core'`
- **THEN** o teste estático reprova e nomeia esse arquivo

#### Scenario: O lint também recusa
- **WHEN** o ESLint roda sobre um arquivo de `src/` fora do `PasswordField` que importa `PasswordInput` de `@mantine/core`
- **THEN** o ESLint reporta `no-restricted-imports` nesse arquivo

