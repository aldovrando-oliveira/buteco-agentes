**Issue:** #234 (passo A da divisão da #182)

## Why

A #182 remove o HMAC e a chave compartilhada (`Auth:TokenSigningKey`). Antes disso, cada serviço precisa se autenticar com uma credencial só dele. O dono dividiu o trabalho em quatro passos, em 02/11/2026: receptores (esta change), emissores (#235), fixtures (#236) e remoção (#182). Cada passo deixa o `main` implantável, em qualquer ordem de deploy.

Este primeiro passo prepara quem **recebe**:

- o emissor passa a conhecer os clientes de serviço;
- o `apps/api` e o `apps/connectors` passam a aceitar o token deles.

Ninguém manda o token novo ainda. Quando a #235 trocar os emissores, o destino já está pronto.

O desenho completo está no design da #182, change `remover-hmac-token-servico`, preservado [num comentário da #182](https://github.com/aldovrando-oliveira/buteco-agentes/issues/182#issuecomment-6593073248) (base `14d4ba9`). Neste texto, ele é chamado de "design de referência". Esta change implementa as seções D1, D2 e D5 dele e o passo A de "Tamanho e divisão".

## What Changes

- **Dois clientes de serviço no catálogo do emissor (`apps/api`).** O `IdentityClientCatalog` passa a sincronizar `buteco-servico-inbox` e `buteco-servico-connectors`, além do `buteco-painel`:
  - confidenciais, com segredo do cliente (`client_secret_post`);
  - só o grant `client_credentials`, só o endpoint de token, só o escopo `buteco-api`;
  - o OpenIddict guarda só o hash;
  - quando o segredo configurado deixa de validar, a sincronização o regrava.
- **Configuração nova no `apps/api`:** `Identity:ServiceClients:Inbox:Secret` e `Identity:ServiceClients:Connectors:Secret`, obrigatórios no boot (convenção 8). Cada um precisa:
  - ter pelo menos 32 bytes em base64;
  - ser distinto do outro;
  - ser distinto de `Identity:SigningKeyEncryptionKey`, de `Identity:TwoFactorEncryptionKey` e de `Mcp:CredentialEncryptionKey`, como a #218 fez com a chave das 2 etapas.

  A mensagem nomeia as duas chaves iguais e nunca ecoa o valor.
- **Tabelas de clientes (`IssuerClientSubjects`):**
  - `apps/api`: `buteco-servico-inbox` → `service:inbox` e `buteco-servico-connectors` → `service:connectors`;
  - `apps/connectors`: `buteco-servico-api` → `service:api`.

  As tabelas de rotas por subject e as checagens de boot delas não mudam.
- **Deploy:** `SERVICE_CLIENT_SECRET_INBOX` e `SERVICE_CLIENT_SECRET_CONNECTORS` entram no `.env.prod.example` e, com `:?`, no serviço `api` do `docker-compose.prod.yml`. O `inbox` e o `connectors` só os recebem na #235.
- **Sem mudança para quem chama.** Os três serviços continuam assinando e mandando HMAC, que continua aceito. O painel não muda.

## Capabilities

### New Capabilities

Nenhuma. O cliente de token dos serviços (`service-client-credentials`) nasce na #235.

### Modified Capabilities

- `oidc-issuer`: o catálogo deixa de manter "o único cliente `buteco-painel`" e passa a manter também os dois clientes de serviço. Entram os segredos obrigatórios e distintos no boot.
- `route-authentication`: o `apps/api` passa a aceitar o token do emissor dos clientes de serviço, mapeados para `service:inbox` e `service:connectors`, nas rotas que esses subjects já têm.
- `issuer-token-validation`: a tabela de clientes do `apps/connectors` ganha `buteco-servico-api` → `service:api`.
- `server-deployment`: entram os dois segredos de serviço no `.env.prod` e no serviço `api`.

## Impact

- **`apps/api`:**
  - `Identity/Clients/` ganha os descritores de serviço, a sincronização deles e a conferência do segredo;
  - `IdentityIssuerOptions` ganha `ServiceClients`;
  - `IdentityIssuerStartupValidation` passa a conferir os segredos;
  - `Identity/Validation/IssuerClientSubjects.cs` ganha duas entradas;
  - `appsettings.Development.json` recebe os segredos de desenvolvimento;
  - testes de unidade e de integração.
- **`apps/connectors`:** `Auth/IssuerClientSubjects.cs` ganha uma entrada, mais testes.
- **`tests/`:** as duas `RoundTripFixture` sobem o `apps/api` real e passam a configurar os dois segredos. Sem caso novo.
- **Sem mudança:** `apps/inbox`, `apps/workers` e `apps/frontend`. Nenhum pacote novo.
- **Deploy:** `docker-compose.prod.yml` e `.env.prod.example`; `docs/configuration.md` e `docs/deployment.md`.
