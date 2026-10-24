**Issue:** #179
**Entrega:** M1 · 1 de 8 (acompanhamento #194). **Relacionadas:** #208 (o `apps/inbox` e o `apps/connectors` passam a aceitar o token deste emissor), #209 (limpeza das entradas de token, aberta nesta change), #211 (rota de leitura das chaves para a tela Identidade, fora desta change por decisão do dono), #180 (primeiro emissor de token de pessoa), #186 (primeira aplicação cliente), #182 (remove o HMAC), #117.

## Why

O `apps/api` emite e valida um token HMAC próprio, sem biblioteca JWT, assinado com uma chave que os três apps compartilham (`Auth:TokenSigningKey`). Quem obtém essa chave assina qualquer subject, inclusive `operator` (#117). E não há emissor: nada publica uma chave verificável por quem não guarda segredo, então não dá para ter usuários (#180), aplicações externas (#186) nem provedores federados (M3/M4).

Esta change é o **cadastro** da evolução de autenticação. O emissor passa a existir **antes** de qualquer coisa usá-lo. O login atual e toda a validação HMAC continuam exatamente como estão.

## What Changes

- **Servidor OIDC no `apps/api`**, com OpenIddict **7.7.2** (só o servidor: `OpenIddict.Server.AspNetCore`) (Apache-2.0, alvo `net10.0`; verificado em 24/10/2026 no NuGet e na fonte da tag). Expõe três endpoints anônimos: discovery (`/.well-known/openid-configuration`), JWKS (`/.well-known/jwks`) e token (`/connect/token`).
- **Fluxo habilitado: só `client_credentials`, com nenhum cliente cadastrado.** Em produção o endpoint de token recusa toda requisição com `invalid_client`. Os testes cadastram um cliente e um escopo **só dentro da fixture**, para provar que o token sai assinado pela chave persistida, com `kid`, `aud` e `iat`, e que é verificável pelo JWKS publicado. O primeiro cliente real é da #186.
- **Chaves de assinatura assimétricas persistidas** no banco do `apps/api` (RSA, RS256), com a parte privada cifrada em repouso por AES-256-GCM. A chave de cifra vem de uma variável de ambiente nova, `Identity__SigningKeyEncryptionKey`, no mesmo idioma da `Mcp__CredentialEncryptionKey` e com valor distinto dela. **Nunca** se usa chave de desenvolvimento gerada a cada boot.
- **Rotação automática:**
  - a cada 90 dias;
  - a próxima chave é publicada no JWKS 7 dias antes de assinar;
  - a anterior para de assinar e continua só validando por mais 24 h;
  - a rotação acontece sem reiniciar o processo;
  - quando há mais de uma instância, um lock do Postgres impede duas rotações ao mesmo tempo.
- **Issuer vindo de `PublicUrl:BaseUrl`**, o mesmo ponto que monta os endereços A2A (`agente-enderecos-a2a`). Os três casos:
  - sem url pública, o emissor **não sobe** e há um aviso no log (o resto do app segue igual);
  - url pública inválida derruba o boot;
  - emissor ligado sem a chave de cifra também derruba o boot.
- **Nova checagem de boot (convenção 8):** os endpoints do OpenIddict não passam pelo roteamento, e por isso a checagem atual de rotas anônimas não os enxerga. Entra uma lista esperada, com motivo, conferida nos dois sentidos contra as URIs configuradas no servidor.
- **Carga das chaves na primeira requisição, com aquecimento em segundo plano logo depois do start.** Até o anel carregar, toda rota responde `503`, inclusive `/health`. O aquecimento não bloqueia o boot, repete a cada 5 s e não guarda falha.
- **Tabelas novas** no `AppDbContext` do `apps/api`: as quatro do OpenIddict, renomeadas para snake_case, mais `identity_keys`. O `AppDbContext` espelho do `apps/workers` **não** as mapeia.
- **EF Core 10.0.10 → 10.0.13 em todo o repositório.** O OpenIddict.EntityFrameworkCore 7.7.x exige EF Core Relational ≥ 10.0.11, e a versão é central (`Directory.Packages.props`), então a subida alcança o `apps/workers` e o `apps/inbox`.
- **nginx do stack:** ganha blocos exatos para `/.well-known/openid-configuration` e `/.well-known/jwks`, e um bloco de prefixo para `/connect`. Sem eles, os três caem no shell do SPA com `200` e HTML.
- **Revogação da convenção "token stateless sem biblioteca JWT"** no `01`. Emitir OIDC e, a partir da #208, validar token de terceiro é exatamente o caso de usar biblioteca.

## Capabilities

### New Capabilities

- `oidc-issuer`: o emissor OIDC do `apps/api`, com discovery, JWKS, endpoint de token, issuer da url pública, o fluxo habilitado e o que acontece sem url pública.
- `identity-signing-keys`: as chaves de assinatura e de cifra do emissor, cobrindo persistência, cifra em repouso, `kid`, rotação, retenção e publicação.

### Modified Capabilities

- `route-authentication`: acrescenta que os endpoints do emissor são anônimos, classificados com motivo e conferidos no boot pela lista esperada. As rotas e a validação HMAC existentes não mudam.

## Impact

- **`apps/api`:**
  - pasta nova `Identity/`, com as opções do emissor, o anel de chaves, a rotação, o handler de `client_credentials`, o handler que monta os endpoints do discovery sob o issuer (D8, correção) e a checagem de boot;
  - `Program.cs` e `AppDbContext` alterados, mais uma migration;
  - pacotes novos: `OpenIddict.Server.AspNetCore` e `OpenIddict.EntityFrameworkCore` (só o servidor, e não o metapacote `OpenIddict.AspNetCore`; ver D1);
  - testes novos de unidade e de integração.
- **`apps/frontend`:** `deploy/nginx.conf`, com os três blocos novos. Nenhum código de tela muda.
- **`apps/workers` e `apps/inbox`:** só a subida da EF Core, pelo arquivo central. Nenhum código muda, e as duas suítes rodam no fechamento.
- **Stack de deploy:** `Identity__SigningKeyEncryptionKey` entra no `docker-compose.prod.yml` (só no `apps/api`), no `.env` de exemplo e em `docs/configuration.md`. O migration bundle passa a criar as tabelas novas.
- **Documentação:** a seção "Autenticação" do `01` (revogação da convenção, e o emissor convivendo com o HMAC), `docs/architecture.md`, `docs/configuration.md` e `docs/deployment.md` (chave nova, o que acontece sem url pública, e o efeito do `503` no healthcheck do compose), e o `02` com a regra das entregas da evolução e a fila da M1 atualizada.
- **Não muda:**
  - `POST /auth/login` e o token HMAC do operador;
  - a validação HMAC nos três apps;
  - `ServiceScopeAuthorizationHandler`;
  - `apps/inbox` e `apps/connectors`, que só passam a aceitar o token novo na #208;
  - o AgentCard, cujo esquema OAuth2 é da #186.
