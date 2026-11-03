## MODIFIED Requirements

### Requirement: Segredos compartilhados vêm de fonte única
O sistema SHALL garantir que segredos que precisam ser byte-idênticos
entre processos (`Auth__TokenSigningKey` entre `apps/api`, `apps/inbox` e
`apps/connectors`; `Mcp__CredentialEncryptionKey` entre
`apps/api`/`apps/workers`; o segredo de cada cliente de serviço entre o
`apps/api` e o serviço dele) sejam declarados uma única vez num arquivo de
ambiente do host, e injetados nos serviços correspondentes via interpolação de
variável do `docker-compose.prod.yml`, nunca copiados manualmente em blocos
`environment:` distintos.

#### Scenario: Mesma chave de assinatura é usada por apps/api e apps/inbox
- **WHEN** um token de operador é emitido por `apps/api` e validado por
  `apps/inbox`
- **THEN** a validação aceita o token, confirmando que ambos os
  processos carregaram o mesmo valor de `Auth__TokenSigningKey` a partir
  da mesma variável de ambiente do host

#### Scenario: Token de operador emitido pelo apps/api vale no apps/connectors
- **WHEN** um token de operador emitido no login de `apps/api` é usado em
  `GET /connectors/providers` pelo nginx do stack
- **THEN** `apps/connectors` aceita o token e responde `200`

#### Scenario: Tokens de serviço entre apps/api e apps/connectors são aceitos nos dois sentidos
- **WHEN** `apps/api` gera no processo o token do `buteco-servico-api` para
  descrever uma pasta no `apps/connectors` durante o cadastro de base
  sincronizada, e `apps/connectors` obtém no emissor o token do
  `buteco-servico-connectors` para escrever no `apps/api` pelas rotas de `/sync`
- **THEN** nenhum dos dois lados responde `401`: o cadastro responde um resultado
  do conector (e não `502 connectors-error`), e o ciclo grava os documentos

### Requirement: Segredos dos clientes de serviço declarados um por serviço
O `docker-compose.prod.yml` SHALL passar ao serviço `api` os segredos `Identity__ServiceClients__Inbox__Secret` e `Identity__ServiceClients__Connectors__Secret`, interpolados das variáveis `SERVICE_CLIENT_SECRET_INBOX` e `SERVICE_CLIENT_SECRET_CONNECTORS` do arquivo de ambiente do host, cada uma com `:?`. Das mesmas variáveis, também com `:?`, o compose SHALL passar `ServiceClient__Secret` ao serviço `inbox`, com o valor de `SERVICE_CLIENT_SECRET_INBOX`, e ao serviço `connectors`, com o de `SERVICE_CLIENT_SECRET_CONNECTORS`. O `.env.prod.example` SHALL declarar as duas, com a instrução de gerar cada uma com `openssl rand -base64 32`, distintas entre si e de todas as outras chaves, e dizendo que cada uma vai ao `api` e ao serviço dela. Nenhum valor SHALL ser copiado em bloco `environment:` sem interpolação.

#### Scenario: Compose sem o segredo não sobe o apps/api
- **WHEN** o `docker compose --env-file .env.prod config` roda com um `.env.prod` sem `SERVICE_CLIENT_SECRET_INBOX`
- **THEN** o comando falha citando `SERVICE_CLIENT_SECRET_INBOX`

#### Scenario: Compose com os segredos entrega cada um ao apps/api e ao serviço dele
- **WHEN** o `docker compose --env-file .env.prod config` roda com as duas variáveis definidas
- **THEN** o serviço `api` recebe `Identity__ServiceClients__Inbox__Secret` e `Identity__ServiceClients__Connectors__Secret` com os valores do arquivo, o serviço `inbox` recebe `ServiceClient__Secret` com o valor de `SERVICE_CLIENT_SECRET_INBOX`, e o serviço `connectors` recebe `ServiceClient__Secret` com o valor de `SERVICE_CLIENT_SECRET_CONNECTORS`
