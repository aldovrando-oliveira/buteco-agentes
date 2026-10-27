**Issue:** #209

## Why

O OpenIddict 7.7.2 grava uma linha em `openiddict_tokens` para **cada** token que emite, de qualquer tipo, e uma em `openiddict_authorizations` por sessão interativa, e não apaga nenhuma delas sozinho. Hoje isso não chega a produção, porque o emissor da #179 não tem cliente cadastrado. Passa a valer no primeiro fluxo que emitir token de verdade: o login da #180 (código, access, refresh e id token a cada renovação), os tokens de serviço da #182 e as aplicações externas da #186 (`client_credentials` a cada poucos minutos). Sem limpeza, as duas tabelas crescem sem teto. Esta change precisa estar em `main` antes da #180 e da #186, que ela bloqueia (M1 · 3 de 8, acompanhamento #194).

## What Changes

- **O access token de `client_credentials` deixa de gerar linha.** Um handler do `GenerateTokenContext`, ordenado entre dois handlers internos do OpenIddict, desliga a criação da linha só quando o token é access token **e** o grant é `client_credentials`. Todos os outros tipos e fluxos continuam gravados como hoje. Nenhum consumidor lê essa linha: quem valida o access token (`apps/inbox`, `apps/connectors` e, na #180, o `apps/api`) usa o JWKS, e revogar ou desativar uma aplicação age no cliente, não no token.
- **Limpeza periódica das duas tabelas no `apps/api`:** `IOpenIddictTokenManager.PruneAsync` e depois `IOpenIddictAuthorizationManager.PruneAsync`, a cada 1 hora, removendo só linhas com mais de 14 dias que o próprio OpenIddict considera mortas (vencidas, resgatadas, revogadas, ou autorização sem token). A remoção sai em lotes de 1.000, cada um no próprio comando, sem transação longa.
- **Uma instância por vez:** cada ciclo tenta `pg_try_advisory_xact_lock` numa chave `bigint` própria, diferente da chave da rotação de chaves. A instância que não consegue o lock pula o ciclo.
- **Checagem de boot (convenção 8):** a idade mínima da limpeza precisa ser maior ou igual à duração do refresh token configurada no servidor, ou o boot falha. É o que preserva a detecção de reuso de refresh token que a #180 vai ligar.
- **Sem migration.** As tabelas do OpenIddict já têm o índice que a limpeza e a revogação usam nesta escala; o design mostra a conta, e uma tarefa mede o plano de execução no volume projetado.
- Nenhuma rota, tabela, variável de ambiente ou configuração nova.

## Capabilities

### New Capabilities

- `issuer-token-storage`: o que o emissor grava em `openiddict_tokens` e `openiddict_authorizations`, o que deixa de gravar (access token de `client_credentials`), quando cada linha é removida, a limpeza periódica com lock entre instâncias e a checagem de boot que amarra a idade mínima da limpeza à duração do refresh token.

### Modified Capabilities

Nenhuma. O token emitido, o discovery, o JWKS e as chaves não mudam. A spec `oidc-issuer` não fala de armazenamento, e a `identity-signing-keys` não muda.

## Impact

- **`apps/api`**: `Identity/` ganha a pasta `Storage/` (handler que desliga a linha do access token de `client_credentials`, política e serviço de limpeza); `IdentityIssuerServiceCollectionExtensions` registra os dois; `IdentityIssuerStartupValidation` ganha a desigualdade da idade mínima. Testes novos em `apps/api/tests/Buteco.Api.Tests/Identity/`.
- **`tests/InboxOrchestratorRoundTrip.Tests` e `tests/ApiConnectorsRoundTrip.Tests`**: nenhum código muda, mas emitem `client_credentials` pelo `apps/api` real e entram na baseline e no fechamento.
- **`apps/workers`, `apps/inbox`, `apps/connectors`, `apps/frontend`**: nenhuma mudança. O `AppDbContext` espelho do `apps/workers` não mapeia tabela de identidade, e esta change não tem migration.
- **Dependências**: nenhuma nova. **Não** entra o `OpenIddict.Quartz` (design, D4).
- **Documentação**: `01` (Autenticação), `02`, `docs/architecture.md`, `docs/deployment.md`, `CHANGELOG.md`.
