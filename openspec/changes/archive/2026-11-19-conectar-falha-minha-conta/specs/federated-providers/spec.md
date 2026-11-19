## MODIFIED Requirements

### Requirement: Provedores federados por configuração, por chave neutra
O `apps/api` SHALL ler os provedores federados da seção `Identity:FederatedProviders`, um dicionário cuja chave é escolhida por quem instala. A chave SHALL casar `^[a-z][a-z0-9-]{1,31}$`. Cada entrada SHALL ter `Type`, `ClientId` e `ClientSecret`, e MAY ter `HostedDomain`. Na M3, o único `Type` aceito SHALL ser `google`. Seção ausente ou vazia SHALL ser válida: o login fica só com senha, como antes, e **nada muda para quem não usa provedor**. Sem provedor, o cliente OIDC SHALL continuar registrado, sem nenhuma registração, só para que o retorno de um fluxo começado antes de um reinício leia o state (`federated-login`, "Decisão do login no retorno"). Sem provedor, o `apps/api` SHALL NOT fazer pedido de rede no boot nem depois dele por causa do cliente, SHALL NOT expor endpoint além dos que expunha sem o cliente (o `/auth/federated/callback` já era rota), e a checagem de boot dos endpoints do cliente SHALL passar com o retorno como único caminho do cliente.

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

#### Scenario: Sem a seção, o boot não faz pedido de rede
- **WHEN** o `apps/api` sobe sem `Identity:FederatedProviders`, sem rede de saída
- **THEN** o boot conclui sem nenhum pedido de saída, e nenhum pedido de saída acontece nos pedidos seguintes ao `apps/api` que não começam um fluxo (nenhum começa, sem provedor)

#### Scenario: Sem a seção, os endpoints do host são os mesmos
- **WHEN** o `apps/api` sobe sem `Identity:FederatedProviders`
- **THEN** a lista dos endpoints do host (as rotas mapeadas, com método e padrão, e os caminhos do servidor e do cliente OIDC) é a mesma de antes do cliente sem registração, e os caminhos do cliente OIDC são só `/auth/federated/callback`

#### Scenario: Sem a seção, a checagem de boot do cliente passa
- **WHEN** o `apps/api` sobe sem `Identity:FederatedProviders`
- **THEN** a checagem dos endpoints do emissor confere também os do cliente OIDC, nos dois sentidos, e não reprova

#### Scenario: Um provedor Google
- **WHEN** a seção traz a chave `corporativo` com `Type` `google`, `ClientId` e o segredo pela variável de ambiente
- **THEN** o boot conclui, e o pedido de autorização que o `apps/api` monta para a chave vai a `https://accounts.google.com/` com `scope` `openid email profile`, `prompt=select_account`, `code_challenge_method=S256`, o `client_id` configurado e, se houver `response_mode`, `query`
