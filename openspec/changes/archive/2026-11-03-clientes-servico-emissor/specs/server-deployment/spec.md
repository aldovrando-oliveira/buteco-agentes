## ADDED Requirements

### Requirement: Segredos dos clientes de serviço declarados um por serviço
O `docker-compose.prod.yml` SHALL passar ao serviço `api` os segredos `Identity__ServiceClients__Inbox__Secret` e `Identity__ServiceClients__Connectors__Secret`, interpolados das variáveis `SERVICE_CLIENT_SECRET_INBOX` e `SERVICE_CLIENT_SECRET_CONNECTORS` do arquivo de ambiente do host, cada uma com `:?`. O `.env.prod.example` SHALL declarar as duas, com a instrução de gerar cada uma com `openssl rand -base64 32`, distintas entre si e de todas as outras chaves. Nenhum valor SHALL ser copiado em bloco `environment:` sem interpolação.

#### Scenario: Compose sem o segredo não sobe o apps/api
- **WHEN** o `docker compose --env-file .env.prod config` roda com um `.env.prod` sem `SERVICE_CLIENT_SECRET_INBOX`
- **THEN** o comando falha citando `SERVICE_CLIENT_SECRET_INBOX`

#### Scenario: Compose com os segredos entrega os dois ao apps/api
- **WHEN** o `docker compose --env-file .env.prod config` roda com as duas variáveis definidas
- **THEN** o serviço `api` recebe `Identity__ServiceClients__Inbox__Secret` e `Identity__ServiceClients__Connectors__Secret` com os valores do arquivo
