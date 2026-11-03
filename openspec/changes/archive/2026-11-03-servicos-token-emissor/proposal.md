**Issue:** #235 (passo B da divisão da #182)

## Why

Desde a #234, o `apps/api` e o `apps/connectors` aceitam o token do emissor dos clientes de serviço, mas ninguém o manda ainda. Os três serviços continuam assinando o próprio token HMAC com a `Auth:TokenSigningKey`, a chave que a #182 vai remover.

Este passo troca quem **manda**: cada serviço passa a obter o token de serviço no emissor do `apps/api`. Depois do merge, nenhum serviço manda HMAC, e a remoção (#182, passo D) fica possível depois da migração das fixtures (#236, passo C).

O desenho completo está no design da #182, change `remover-hmac-token-servico`, preservado [num comentário da #182](https://github.com/aldovrando-oliveira/buteco-agentes/issues/182#issuecomment-6593073248) (base `14d4ba9`). Aqui ele é o "design de referência". Esta change implementa:

- as seções D3 (`ServiceAccessTokenSource`), D4 (`ServiceAccessTokenIssuer`), D7 (só o round trip de `tests/`) e D8 (o deploy do passo B);
- o passo B de "Tamanho e divisão".

## What Changes

- **`apps/inbox` e `apps/connectors`: `ServiceAccessTokenSource`** (referência D3). O token passa a vir de `POST {Api:BaseUrl}/connect/token`, pelo endereço interno, com o segredo do cliente que a #234 cadastrou (`buteco-servico-inbox`, `buteco-servico-connectors`) e `scope=buteco-api`.
  - **Cache:** o token serve até faltar 1 min para vencer.
  - **Pedido único:** chamadas simultâneas fazem um pedido só.
  - **Falha do emissor:** com `503`, `invalid_client`, timeout ou o `apps/api` fora do ar, os pedidos falham na hora durante 10 s. A falha sobe como `HttpRequestException`, que cada chamador já trata como "`apps/api` indisponível".
  - **`401` do destino:** descarta o cache, sem repetir a chamada.
- **`apps/api`: `ServiceAccessTokenIssuer` estreito** (referência D4). Gera no próprio processo, pela API de geração do OpenIddict, só o token `buteco-servico-api`, com `aud` `buteco-connectors` e 15 min, sem parâmetro. O primeiro teste prova que o token sai igual ao do `/connect/token` e que o `JwtBearer` do `apps/connectors` o aceita (H6).
- **Os três `ServiceTokenDelegatingHandler`** deixam de assinar HMAC e passam a anexar o token do emissor. Nada muda em quem chama.
- **Configuração nova nos serviços:** `ServiceClient:Secret` no `apps/inbox` (obrigatório) e no `apps/connectors` (obrigatório quando há `Api:BaseUrl`). O boot confere presença, base64 e 32 bytes, sem ecoar o valor.
- **Deploy:** o `inbox` e o `connectors` passam a receber o `SERVICE_CLIENT_SECRET_*` que a #234 já pôs no `.env.prod`. Não há variável nova no `.env.prod`.
- **Testes de `tests/`:** os acordos dos quatro fluxos de serviço (S1 a S4) passam pelo emissor real, e há casos de segredo divergente.
- **O HMAC continua aceito** nos três apps. Só deixa de ser enviado. A chave, o `TokenService` e o seletor saem na #182.

## Capabilities

### New Capabilities

- `service-client-credentials`: como o `apps/inbox` e o `apps/connectors` obtêm, guardam e renovam o token de serviço no emissor (cache, pedido único, falha do emissor, `401` do destino), e como o `apps/api` gera o seu no processo, estreito.

### Modified Capabilities

- `inbox-service-authentication`: o token `service:inbox` deixa de ser assinado pelo `apps/inbox` e passa a vir do emissor, pelo cliente `buteco-servico-inbox`.
- `knowledge-sync-cycle`: a chamada como `service:connectors` passa a levar o token do emissor, sem `Auth:TokenSigningKey` nem token novo a cada requisição.
- `knowledge-sync-folder-validation`: o token `service:api` passa a ser gerado pelo emissor no processo, com cache, e o cenário de ida e volta "chave de assinatura divergente" muda de causa.
- `server-deployment`: os segredos de serviço chegam também ao `inbox` e ao `connectors`. O cenário dos tokens de serviço "nos dois sentidos" deixa de citar a assinatura com a chave comum.

## Impact

- **`apps/api`:**
  - `Identity/ServiceTokens/ServiceAccessTokenIssuer.cs` (novo);
  - `Auth/ServiceTokenDelegatingHandler.cs` reescrito;
  - `Program.cs`;
  - testes de unidade e de integração.
- **`apps/inbox` e `apps/connectors`, uma cópia em cada, sem `libs/` (referência D10):**
  - `Auth/ServiceAccessTokenSource.cs`, `ServiceAccessTokenSchedule.cs`, `ServiceClientOptions.cs` (novos);
  - `ServiceTokenDelegatingHandler.cs` reescrito;
  - a checagem de boot;
  - `Program.cs` (e `Sync/SyncRegistrationExtensions.cs` no connectors);
  - `appsettings.Development.json`;
  - testes, com o duplo do `/connect/token` (`FakeTokenEndpoint`).
- **`tests/`:** `InboxOrchestratorRoundTrip.Tests` e `ApiConnectorsRoundTrip.Tests`. `CrossAppTaskStoreCompatibility.Tests` não muda.
- **Sem mudança:** `apps/workers` e `apps/frontend`. Nenhum pacote novo: o `Microsoft.IdentityModel.JsonWebTokens` e o `OpenIddict.Server` já estão no grafo de quem usa.
- **Deploy:** `docker-compose.prod.yml` (`ServiceClient__Secret` no `inbox` e no `connectors`) e o comentário do `.env.prod.example`. Ordem livre.
- **Docs:** `docs/configuration.md`, `docs/development.md`, `docs/deployment.md` (a seção do passo B, com a conferência), `01` (Autenticação), `02` e `CHANGELOG.md`.
