## ADDED Requirements

### Requirement: Provedores federados por configuração, por chave neutra
O `apps/api` SHALL ler os provedores federados da seção `Identity:FederatedProviders`, um dicionário cuja chave é escolhida por quem instala. A chave SHALL casar `^[a-z][a-z0-9-]{1,31}$`. Cada entrada SHALL ter `Type`, `ClientId` e `ClientSecret`, e MAY ter `HostedDomain`. Na M3, o único `Type` aceito SHALL ser `google`. Seção ausente ou vazia SHALL ser válida: o login fica só com senha, como antes.

O tipo `google` SHALL fixar, sem campo de configuração:
- o issuer `https://accounts.google.com/`;
- o nome "Google";
- os escopos `openid email profile`;
- `prompt=select_account` no pedido de autorização, e o modo de resposta `query` (o padrão do `code`, que o pedido não precisa repetir), nunca `form_post`;
- o uso: login de usuários.

O `ClientSecret` SHALL ser aceito só por variável de ambiente (`Identity__FederatedProviders__<chave>__ClientSecret`). Mudar a seção SHALL exigir reiniciar o `apps/api`; não SHALL existir cadastro de provedor por rota.

#### Scenario: Sem a seção, o login continua só com senha
- **WHEN** o `apps/api` sobe sem `Identity:FederatedProviders`
- **THEN** o boot conclui, e `GET /auth/federated/providers` responde `200` com `provedores` vazia

#### Scenario: Um provedor Google
- **WHEN** a seção traz a chave `corporativo` com `Type` `google`, `ClientId` e o segredo pela variável de ambiente
- **THEN** o boot conclui, e o pedido de autorização que o `apps/api` monta para a chave vai a `https://accounts.google.com/` com `scope` `openid email profile`, `prompt=select_account`, `code_challenge_method=S256`, o `client_id` configurado e, se houver `response_mode`, `query`

### Requirement: Checagem de boot da seção, com lista fechada
O `apps/api` SHALL conferir a seção depois do `Build()`, sem banco e sem rede, e SHALL falhar o boot com `InvalidOperationException` e a mensagem do design (D3) em cada uma destas inconsistências, e só nelas:

1. chave fora do formato;
2. `Type` ausente;
3. `Type` diferente de `google`;
4. `ClientId` ausente ou em branco;
5. `ClientSecret` ausente ou em branco;
6. dois provedores com o mesmo issuer;
7. `ClientSecret` cujo valor efetivo vem de um arquivo de configuração JSON;
8. campo fora de `Type`, `ClientId`, `ClientSecret` e `HostedDomain`;
9. `HostedDomain` que não é um nome de domínio (com `@`, espaço, esquema ou caminho);
10. `ClientSecret` igual a uma das chaves de cifra ou a um dos segredos de cliente de serviço do `apps/api`.

A mensagem SHALL citar a chave e o campo, e SHALL NOT conter o valor do segredo. Alcançar o discovery do provedor SHALL NOT ser condição de boot.

#### Scenario: Segredo no arquivo JSON derruba o boot
- **WHEN** o `ClientSecret` da chave `corporativo` vem do `appsettings.json`, e não de variável de ambiente
- **THEN** o boot falha com a mensagem que cita `Identity:FederatedProviders:corporativo:ClientSecret` e "variável de ambiente", e a mensagem não contém o valor do segredo

#### Scenario: O mesmo segredo pela variável passa
- **WHEN** o mesmo valor chega por `Identity__FederatedProviders__corporativo__ClientSecret`
- **THEN** o boot conclui

#### Scenario: Dois provedores Google derrubam o boot
- **WHEN** a seção traz as chaves `a` e `b`, as duas com `Type` `google`
- **THEN** o boot falha com a mensagem que cita `a`, `b` e o issuer

#### Scenario: Campo com erro de digitação derruba o boot
- **WHEN** a entrada traz `ClientID` no lugar de `ClientId`
- **THEN** o boot falha com a mensagem que cita `ClientID`

#### Scenario: Cada inconsistência tem o seu caso
- **WHEN** a seção traz, uma de cada vez, cada uma das dez inconsistências
- **THEN** cada boot falha com a mensagem própria dela, e a seção sem nenhuma delas sobe

#### Scenario: Provedor fora do ar não impede o boot
- **WHEN** o `apps/api` sobe com um provedor configurado e sem acesso à rede externa
- **THEN** o boot conclui

### Requirement: Rota só leitura dos provedores para a tela Identidade
`GET /identity/federated-providers` SHALL responder `200` só ao Admin ativo, pelo papel gravado (`CurrentUser`, como `GET /identity`), com `{ provedores: [{ chave, tipo, nome, issuer, usadoPara, clientId, uriDeRetorno, vinculos }] }`, na ordem da chave: `usadoPara` igual a `["login"]`, `uriDeRetorno` igual a `<base do painel>/auth/federated/callback` (a base é `Identity:PanelBaseUrl` ou, sem ela, `PublicUrl:BaseUrl`, como os retornos do `buteco-painel`) e `vinculos` igual ao número de usuários com vínculo no issuer do provedor. A resposta SHALL NOT conter o `ClientSecret` nem o `HostedDomain`. Os outros papéis SHALL receber `403`, e sem token, `401`.

#### Scenario: Admin lê os provedores sem segredo
- **WHEN** um Admin chama a rota com um provedor configurado e um usuário vinculado
- **THEN** a resposta é `200` com a chave, `tipo` `google`, `nome` `Google`, o issuer, o `clientId`, a `uriDeRetorno` e `vinculos` 1, e o corpo não contém o valor do segredo

#### Scenario: Operador e Leitor são recusados
- **WHEN** um Operador e um Leitor chamam a rota
- **THEN** as duas respostas são `403`

### Requirement: Rota anônima dos provedores para o login
`GET /auth/federated/providers` SHALL ser anônima (`AuthEntryPoint`) e SHALL responder `200` com `{ provedores: [{ chave, tipo }] }`, na ordem da chave, sem nenhum outro campo.

#### Scenario: O login lê só chave e tipo
- **WHEN** um cliente sem token chama `GET /auth/federated/providers` com um provedor configurado
- **THEN** a resposta é `200` com `[{ "chave": "corporativo", "tipo": "google" }]`, sem `clientId`, issuer nem segredo
