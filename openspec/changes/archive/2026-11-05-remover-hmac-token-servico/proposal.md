**Issue:** #182 (passo D da divisão de 02/11/2026; absorve a #117 e a #122)

## Why

Os três apps ainda **aceitam** o token HMAC assinado com a `Auth:TokenSigningKey` comum. Quem rouba a chave de qualquer processo assina um token de `operator`, e a chave mora no `apps/inbox`, exposto pelos webhooks, e no `apps/connectors`, que guarda a credencial do Google. É o risco aceito na #117, cujo gatilho, a entrada do segundo cliente e de dados mais sensíveis, é a M1.

Desde a #235, nenhum serviço manda HMAC. Desde a #236, nenhum teste de regra depende dele: o que ainda o usa são os 55 casos que provam o próprio HMAC, listados por nome [num comentário da #182](https://github.com/aldovrando-oliveira/buteco-agentes/issues/182#issuecomment-6622921574). Falta a remoção. É o passo D do desenho completo, preservado [num comentário da #182](https://github.com/aldovrando-oliveira/buteco-agentes/issues/182#issuecomment-6593073248) (base `14d4ba9`), aqui chamado de "design de referência".

## What Changes

- **BREAKING (contrato interno):** os três apps deixam de aceitar o token HMAC. Saem, em cada app, o `TokenService`, o `ITokenService`, o `TokenValidationResult`, o `TokenSigningOptions` e o `OperatorTokenAuthenticationHandler`. Saem também o esquema de política `Bearer` e o seletor por forma do token. O `JwtBearer` (`IssuerToken`) passa a ser o único esquema e o padrão (D6 da referência).
- **BREAKING (configuração):** sem `Identity:Issuer`, o `apps/inbox` e o `apps/connectors` não sobem (Q5 da referência). No `apps/connectors`, a checagem de boot que já existe passa a tornar o `Api:BaseUrl` obrigatório também: o emissor exige o endereço interno, por onde vêm as chaves.
- **`401` sem token passa a sair com `WWW-Authenticate: Bearer`** nos três apps (Q3 da referência). No `apps/inbox` e no `apps/connectors`, a requisição sem token continua `401` mesmo antes da primeira obtenção das chaves. Hoje é o HMAC que responde esse caso, e o desafio do emissor responderia `503` (design, D3).
- **Saem a `Auth:TokenSigningKey` e a `AUTH_TOKEN_SIGNING_KEY`** dos `appsettings`, do `docker-compose.prod.yml`, do `.env.example` e do `.env.prod.example`, e sai a checagem de boot da chave do `apps/connectors`. No `.env.prod` do servidor, a variável fica **uma semana** depois do deploy, para o rollback, e o runbook diz quando tirar.
- **Testes:** a lista de 55 casos da #182 aplicada por nome: 46 saem e 7 são reescritos. Os 2 da Q3 saem, com o caso que já prova a falha de boot no lugar. Saem também os helpers HMAC das fixtures e de `tests/`. As fixtures que sobem com o emissor desligado passam a ligá-lo. Dois casos fora da lista dependem do emissor desligado, estado que deixa de existir: um sai e um é reescrito (design, K4 e P3).
- **Documentação reescrita:** a seção "Autenticação" do `01`, o `SECURITY.md` (fecha a #122), o `docs/architecture.md`, o `docs/configuration.md`, o `docs/development.md` e o `docs/deployment.md`, com o runbook deste deploy.
- **Fecha** a #182, a #117 e a #122.

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `issuer-token-validation`: o token do emissor é o único aceito no `apps/inbox` e no `apps/connectors`. Sai o requisito "um formato nunca cai no outro". Sem `Identity:Issuer`, o boot falha. A requisição sem token recebe `401` com `WWW-Authenticate: Bearer`, mesmo sem chaves obtidas.
- `route-authentication`: saem os requisitos do HMAC aceito pelo `apps/inbox` e do `apps/api` "ao lado do HMAC". O `401` da rota inexistente passa a ter `WWW-Authenticate: Bearer`.
- `operator-authentication`: o requisito "token HMAC de operador só é validado" sai. Nenhum HMAC é validado.
- `oidc-issuer`: sai "o emissor convive com a validação HMAC sem mudá-la".
- `connectors-scaffold`: sai "Token validado localmente com a chave compartilhada", e a imagem perde a variável da chave.
- `server-deployment`: a `AUTH_TOKEN_SIGNING_KEY` sai dos segredos compartilhados.
- `api-scaffold`, `inbox-scaffold`, `connectors-scaffold`: o requisito da suíte da #236 ("com o HMAC recusado, reprovam só os casos da lista da #182") perde o termo do HMAC.
- `user-access-links`, `user-account`, `user-management`, `two-factor-totp`: o cenário "HMAC `operator` → `403`" passa a um token de operador sem usuário (P2).
- `service-client-credentials`, `knowledge-sync-folder-validation`: saem as frases "SHALL NOT anexar/assinar token HMAC". O cenário "`service:api` não acessa o `apps/api`" passa ao token do emissor de um cliente fora da tabela.

## Impact

- **`apps/api`:** `Auth/` (5 arquivos saem), `Identity/Validation/IssuerTokenAuthentication.cs`, `Program.cs`, `appsettings.Development.json`, comentários de `CurrentUser`, `KnowledgeDocumentEndpoints` e `OperatorPasswordHasher`.
- **`apps/inbox`:** `Auth/` (5 arquivos saem), `IssuerTokenAuthentication.cs`, `IssuerTokenStartupValidation.cs`, `Program.cs`, `appsettings.Development.json`.
- **`apps/connectors`:** os mesmos 5 arquivos e os mesmos dois de autenticação, a checagem da chave no `Program.cs`, `ConnectorsSubjectAuthorizationHandler` (comentário) e `appsettings.Development.json`.
- **Testes:** os três projetos de teste, o `InboxOrchestratorRoundTrip.Tests` e o `ApiConnectorsRoundTrip.Tests`.
- **Deploy:** `docker-compose.prod.yml`, `.env.example`, `.env.prod.example`. Os três apps sobem em qualquer ordem.
- **Não mudam:** `apps/workers`, `apps/frontend`, `libs/`, o emissor, os clientes de serviço e as tabelas de subjects. Nenhum pacote entra ou sai.
