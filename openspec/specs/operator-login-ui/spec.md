# operator-login-ui Specification

## Purpose

TBD - defined by change auth-login-e-servico. Update Purpose after archive.
## Requirements
### Requirement: Tela de login do operador
`apps/frontend` SHALL prover a tela `/entrar`, com os campos "E-mail" (`autocomplete="username"`) e "Senha", submetendo as credenciais a `POST /auth/session` do `apps/api`. Formulário com e-mail ou senha vazios SHALL mostrar "Informe e-mail e senha." sem enviar. Em caso de `204`, a tela SHALL seguir o fluxo do emissor (`panel-issuer-session`). Em qualquer outra resposta, a tela SHALL permanecer em `/entrar`, sem gravar sessão, mostrando:

| resposta | texto |
|---|---|
| `400` | o `title` do servidor, ou "Informe e-mail e senha." sem ele |
| `401` | o `title` do servidor ("E-mail ou senha inválidos.") |
| `403` | o `title` do servidor |
| `423` | o aviso `bloqueado`: "Muitas tentativas sem sucesso. Por segurança, o acesso desta conta fica pausado por N minutos.", com N igual ao `Retry-After` em minutos, arredondado para cima |
| `429` | "Muitas tentativas ao mesmo tempo. Tente de novo em instantes." |
| `503` | "O painel está iniciando. Tente de novo em alguns segundos." |
| falha de rede | "Não foi possível falar com o servidor." |

Depois de `429` e `503`, o botão "Entrar" SHALL ficar desabilitado até passar o `Retry-After`. A tela SHALL NOT repetir o envio sozinha.

Com `?motivo=sessao-expirada`, a tela SHALL mostrar o aviso `sessao-expirada`, com `role="status"`: "Sua sessão expirou. Entre novamente para continuar de onde parou.". Com `?motivo=falha-na-entrada`, que o `/entrar/retorno` usa quando o código é recusado, a tela SHALL mostrar o aviso genérico, com `role="alert"`: "Não foi possível concluir a entrada. Entre de novo.".

A tela SHALL NOT mostrar "Esqueceu a senha?" nem o convite ao administrador enquanto não houver redefinição nem convite (#181, #183).

`/login` SHALL redirecionar para `/entrar`, com `replace`.

#### Scenario: Login bem-sucedido segue o fluxo do emissor
- **WHEN** o operador preenche e-mail e senha corretos e submete
- **THEN** o `POST /auth/session` leva `email` e `senha`, responde `204`, e o painel navega ao authorize

#### Scenario: Credencial inválida exibe erro
- **WHEN** o `POST /auth/session` responde `401` com o `title` "E-mail ou senha inválidos."
- **THEN** a tela mostra "E-mail ou senha inválidos.", continua em `/entrar`, e nenhum registro de sessão é gravado

#### Scenario: Conta bloqueada mostra os minutos do Retry-After
- **WHEN** o `POST /auth/session` responde `423` com `Retry-After: 600`
- **THEN** a tela mostra o aviso `bloqueado` com "10 minutos"

#### Scenario: Bloqueio com segundos quebrados arredonda para cima
- **WHEN** o `POST /auth/session` responde `423` com `Retry-After: 61`
- **THEN** a tela mostra "2 minutos"

#### Scenario: Painel iniciando desabilita o botão até o Retry-After
- **WHEN** o `POST /auth/session` responde `503` com `Retry-After: 5`
- **THEN** a tela mostra "O painel está iniciando. Tente de novo em alguns segundos.", e o botão "Entrar" fica desabilitado por 5 segundos

#### Scenario: Sessão expirada mostra o aviso
- **WHEN** `/entrar?motivo=sessao-expirada` é aberto
- **THEN** a tela mostra "Sua sessão expirou. Entre novamente para continuar de onde parou." num elemento com `role="status"`

#### Scenario: Código recusado mostra o aviso genérico
- **WHEN** `/entrar?motivo=falha-na-entrada&voltarPara=%2Fagents` é aberto
- **THEN** a tela mostra "Não foi possível concluir a entrada. Entre de novo." num elemento com `role="alert"`

#### Scenario: Link antigo para /login chega ao /entrar
- **WHEN** alguém navega para `/login`
- **THEN** a URL passa a `/entrar`

### Requirement: Rotas da aplicação exigem sessão ativa
`apps/frontend` SHALL mandar para `/entrar` qualquer navegação para uma rota da aplicação quando não houver o registro da sessão do emissor (`panel-issuer-session`), guardando a rota pedida como `voltarPara`. `/entrar`, `/entrar/retorno` e `/login` SHALL ser as únicas rotas sem sessão.

#### Scenario: Acesso sem sessão redireciona para /entrar
- **WHEN** um usuário sem o registro da sessão navega para qualquer rota da aplicação diferente de `/entrar`, `/entrar/retorno` e `/login`
- **THEN** a interface vai a `/entrar`, sem renderizar o conteúdo da rota original

### Requirement: Resposta 401 encerra a sessão do operador
`apps/frontend` SHALL, ao receber `401 Unauthorized` de `apps/api` ou `apps/inbox` numa chamada autenticada, renovar a sessão uma vez e repetir a chamada. Se a repetição também responder `401`, ou se a renovação responder `invalid_grant`, SHALL apagar o registro da sessão e ir a `/entrar?motivo=sessao-expirada`, guardando a rota atual como `voltarPara`.

#### Scenario: 401 depois da renovação encerra a sessão
- **WHEN** uma chamada de uma feature autenticada (ex. listagem de agentes) recebe `401` de `apps/api`, e a repetição depois da renovação também recebe `401`
- **THEN** a interface apaga o registro da sessão e vai a `/entrar?motivo=sessao-expirada`

#### Scenario: 401 resolvido pela renovação não encerra
- **WHEN** a chamada recebe `401` e a repetição depois da renovação recebe `200`
- **THEN** a tela mostra o dado, e o registro da sessão continua

