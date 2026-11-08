## MODIFIED Requirements

### Requirement: Depois de criar a senha, a pessoa entra
No `204` da conclusão, a página SHALL chamar `POST /auth/session` com o `email` da consulta e a senha digitada, e:

| resposta do login | o que a página faz |
|---|---|
| `200` com `{ "segundoPasso": "cadastro" }` | navega a `/entrar` com o `state` `{ etapa: "cadastro" }`, e o `/entrar` abre na etapa do cadastro com o aviso "Senha salva. Falta só cadastrar o app autenticador para entrar." (`operator-login-ui`) |
| `200` com `{ "segundoPasso": "totp" }` | navega a `/entrar` com o `state` `{ etapa: "codigo" }`, e o `/entrar` abre no passo do código com o aviso "Senha salva…" (`operator-login-ui`) |
| qualquer outra | navega a `/entrar` com o `state` `{ email, status }`, e o `/entrar` abre com o e-mail preenchido e o aviso daquele status (`operator-login-ui`) |

A senha SHALL ficar só na memória da página, entre a conclusão e o login, e SHALL NOT ir ao `state` do router nem a armazenamento nenhum.

#### Scenario: Aceite seguido do cadastro
- **WHEN** o aceite responde `204` e o `POST /auth/session` com `marcos.lima@empresa.com` e a senha digitada responde `200` com `{ "segundoPasso": "cadastro" }`
- **THEN** a URL passa a `/entrar`, a tela mostra a etapa do cadastro com "Senha salva. Falta só cadastrar o app autenticador para entrar.", e o `state` do router não contém a senha

#### Scenario: Redefinição de quem tem as 2 etapas chega ao passo do código
- **WHEN** a redefinição responde `204` e o `POST /auth/session` responde `200` com `{ "segundoPasso": "totp" }`
- **THEN** a URL passa a `/entrar`, a tela mostra o passo do código com "Senha salva. A sua conta usa verificação em 2 etapas: digite o código para entrar.", e o `state` do router não contém a senha

#### Scenario: Login recusado depois do aceite chega ao /entrar com o e-mail
- **WHEN** o aceite responde `204` e o `POST /auth/session` responde `423` com `Retry-After: 300`
- **THEN** a URL passa a `/entrar`, o campo "E-mail" vem com o e-mail da consulta, e a tela mostra o aviso `bloqueado` com "5 minutos"
