## MODIFIED Requirements

### Requirement: Token de serviço service:api nas chamadas ao apps/connectors
Toda requisição do `apps/api` ao `apps/connectors` SHALL levar o access token que o
próprio `apps/api` gera no processo, assinado pelo anel de chaves do emissor, com
`client_id` `buteco-servico-api`, `aud` `buteco-connectors` e validade de 15 minutos,
reaproveitado até faltar 1 minuto para vencer. A tabela de clientes do
`apps/connectors` mapeia `buteco-servico-api` para `service:api`. O `apps/api` SHALL
NOT assinar token HMAC para essa chamada. O subject `service:api` NÃO SHALL ter
acesso a nenhuma rota do `apps/api`.

#### Scenario: Token enviado
- **WHEN** o `apps/api` chama o `apps/connectors` para validar uma pasta
- **THEN** o cabeçalho `Authorization` traz um token `Bearer` do emissor com
  `client_id` `buteco-servico-api`, `aud` `buteco-connectors` e expiração 15 minutos
  depois da emissão

#### Scenario: Token reaproveitado entre requisições
- **WHEN** o `apps/api` faz duas chamadas ao `apps/connectors` dentro da validade do
  token
- **THEN** as duas levam o mesmo token, gerado uma vez

#### Scenario: service:api não acessa o apps/api
- **WHEN** um token validamente assinado com `sub` igual a `service:api` chama
  `GET /knowledge-bases` e `POST /knowledge-bases`
- **THEN** as duas respostas são 403

### Requirement: Ida e volta entre apps/api e apps/connectors reais
A suíte SHALL ter um teste que sobe o `apps/api` e o `apps/connectors` reais, com o
`apps/connectors` validando os tokens do emissor do `apps/api` pelo endereço interno,
o conector falso do `apps/connectors` registrado e nenhuma credencial do Google, e
liga o `HttpClient` do `apps/api` ao servidor de teste do `apps/connectors`, sem rede
e sem chamada ao Google.

#### Scenario: Cadastro pela chamada real
- **WHEN** o operador cria uma base `Synced` com o provedor do conector falso
- **THEN** a API responde HTTP 201 e o banco tem o nome e a URL devolvidos pelo
  conector falso

#### Scenario: Código atravessa os dois apps
- **WHEN** o conector falso falha com `access-denied` e o e-mail da conta
- **THEN** a API responde HTTP 422 com esse código e esse e-mail, e nenhuma base é
  criada

#### Scenario: Provedor não configurado no apps/connectors real
- **WHEN** o operador cria uma base `Synced` com `provider: "google-drive"` num
  `apps/connectors` sem credencial do Google
- **THEN** a API responde HTTP 422 com `code: "provider-not-configured"`

#### Scenario: Chaves de outro emissor no apps/connectors
- **WHEN** o `apps/connectors` obtém as chaves de um emissor que anuncia o mesmo issuer
  do `apps/api`, mas não publica a chave que assina o token gerado no processo
- **THEN** a API responde HTTP 502 com `code: "connectors-error"` e `detail` igual
  a `"401"`, e nenhuma base é criada
