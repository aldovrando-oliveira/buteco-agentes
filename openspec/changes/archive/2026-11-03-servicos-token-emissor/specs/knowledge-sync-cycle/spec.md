## MODIFIED Requirements

### Requirement: Chamada ao apps/api como service:connectors
Toda requisição do `apps/connectors` ao `apps/api` SHALL levar o access token de
serviço obtido no emissor do `apps/api` pelo cliente `buteco-servico-connectors`,
que a tabela de clientes do `apps/api` mapeia para `service:connectors`. O
`apps/connectors` SHALL NOT assinar token de serviço próprio. `service:connectors`
NÃO SHALL entrar na tabela de subjects do `apps/connectors`. Com o emissor
indisponível ou o segredo recusado, a chamada SHALL falhar como o `apps/api` fora do
ar, e a rodada SHALL encerrar como na falta de resposta.

#### Scenario: O token enviado é o do cliente de serviço do connectors
- **WHEN** o ciclo faz qualquer chamada ao `apps/api`
- **THEN** o cabeçalho `Authorization` traz um access token do emissor com
  `client_id` `buteco-servico-connectors` e `aud` `buteco-api`

#### Scenario: O apps/api real aceita o token
- **WHEN** o ciclo roda contra o `apps/api` real
- **THEN** nenhuma das chamadas responde `401` nem `403`

#### Scenario: Segredo divergente
- **WHEN** o `apps/connectors` tem um `ServiceClient:Secret` diferente do cadastrado
  no `apps/api` e o ciclo roda contra o `apps/api` real
- **THEN** o emissor responde `invalid_client`, nenhum upsert chega ao `apps/api`, e
  nada é excluído
