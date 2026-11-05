## MODIFIED Requirements

### Requirement: Token de serviço service:api nas chamadas ao apps/connectors
Toda requisição do `apps/api` ao `apps/connectors` SHALL levar o access token que o
próprio `apps/api` gera no processo, assinado pelo anel de chaves do emissor, com
`client_id` `buteco-servico-api`, `aud` `buteco-connectors` e validade de 15 minutos,
reaproveitado até faltar 1 minuto para vencer. A tabela de clientes do
`apps/connectors` mapeia `buteco-servico-api` para `service:api`. O subject
`service:api` NÃO SHALL ter acesso a nenhuma rota do `apps/api`: nenhum cliente da
tabela de clientes do `apps/api` produz esse subject.

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
- **WHEN** um access token do emissor válido, com `aud` `buteco-api` e `client_id`
  fora da tabela de clientes do `apps/api`, chama `GET /knowledge-bases` e
  `POST /knowledge-bases`
- **THEN** as duas respostas são 403
