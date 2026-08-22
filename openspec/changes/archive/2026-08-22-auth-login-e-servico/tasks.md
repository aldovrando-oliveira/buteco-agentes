## 1. apps/api — emissão de token e login do operador

- [x] 1.1 (apps/api) Criar `Auth/TokenSigningOptions.cs`
      (`TokenSigningKey`, `OperatorTokenLifetime` — `TimeSpan`, default
      `00:30:00` fixado como valor inicial da propriedade no próprio
      tipo, não no `appsettings.json`) e `Auth/OperatorCredentialOptions.cs`
      (`OperatorUsername`, `OperatorPasswordHash`), registrados via
      Options pattern a partir da seção `Auth` de configuração.
- [x] 1.2 (apps/api) Implementar `Auth/ITokenService.cs`/`TokenService.cs`:
      emitir token assinado (`sub`, `exp`, HMAC-SHA256) e validar um token
      recebido (assinatura + expiração), usando
      `CryptographicOperations.FixedTimeEquals`.
- [x] 1.3 (apps/api) Implementar verificação de senha do operador via
      `Rfc2898DeriveBytes.Pbkdf2` contra o hash composto em
      `Auth:OperatorPasswordHash` (design.md, Decision 5).
- [x] 1.4 (apps/api) Criar `Auth/Endpoints/AuthEndpoints.cs` —
      `POST /auth/login`: valida usuário/senha, emite token via
      `ITokenService` em caso de sucesso, `401` genérico (mesma mensagem
      para usuário ou senha incorretos) em caso de falha.
- [x] 1.5 (apps/api) Registrar `AuthEndpoints` em `Program.cs`.

## 2. apps/api — enforcement de rotas e checagem de integridade

- [x] 2.1 (apps/api) Criar `Auth/OperatorTokenAuthenticationHandler.cs`
      (`AuthenticationHandler<AuthenticationSchemeOptions>`) lendo
      `Authorization: Bearer`, validando via `ITokenService`, montando
      `ClaimsPrincipal` com o `sub` do token.
- [x] 2.2 (apps/api) Criar `Auth/AnonymousRouteClassification.cs` —
      metadata + enum `AnonymousRouteReason`
      (`HealthProbe`/`AuthEntryPoint`/`PublicDiscovery`).
- [x] 2.3 (apps/api) Registrar `AddAuthentication` (esquema do handler do
      2.1) e `AddAuthorization` com `FallbackPolicy =
      RequireAuthenticatedUser` em `Program.cs`.
- [x] 2.4 (apps/api) Marcar `GET /health`, `POST /auth/login` e
      `GET /agents/{id}/.well-known/agent-card.json` com
      `.AllowAnonymous()` + `AnonymousRouteClassification` (motivo
      correspondente).
- [x] 2.5 (apps/api) Criar `Auth/RouteAuthenticationExtensions.cs` —
      checagem de integridade bidirecional (design.md, Decision 4),
      chamada em `Program.cs` depois de todos os `Map*` e antes de
      `app.Run()`.
- [x] 2.6 (apps/api) Criar `Auth/ServiceScopeAuthorizationHandler.cs` —
      quando `sub == "service:inbox"`, autorizar só
      `POST /agents/{id}/a2a` e `GET /agents/{id}`; qualquer outra rota
      responde `403 Forbidden`. Aplicar a política resultante
      globalmente (ela não afeta requisições com `sub == "operator"`).

## 3. apps/api — AgentCard com esquema de segurança

- [x] 3.1 (apps/api) Atualizar `A2A/AgentCardEndpoints.cs` para preencher
      `AgentCard.SecuritySchemes`/`SecurityRequirements` com um
      `HttpAuthSecurityScheme` (`Scheme: "Bearer"`) (design.md, Decision
      6).

## 4. apps/inbox — token de operador e token de serviço

- [x] 4.1 (apps/inbox) Criar `Auth/TokenSigningOptions.cs` (mesma chave
      `Auth:TokenSigningKey` usada por `apps/api`) e duplicar
      `Auth/ITokenService.cs`/`TokenService.cs` (design.md, Decision 2 —
      duplicação deliberada, sem `libs/`).
- [x] 4.2 (apps/inbox) Criar `Auth/OperatorTokenAuthenticationHandler.cs`
      (mesma lógica de validação de `apps/api`) para autenticar as rotas
      de `apps/inbox`.
- [x] 4.3 (apps/inbox) Criar `Auth/ServiceTokenDelegatingHandler.cs` —
      assina um token `sub: "service:inbox"` com TTL fixo de 5 minutos
      (constante em código, não configurável — design.md, Decision 3) a
      cada requisição de saída; registrar via `AddHttpMessageHandler` nos
      `HttpClient`s nomeados `AgentReferenceValidator` e `A2AClient`
      (`Program.cs`).

## 5. apps/inbox — enforcement de rotas

- [x] 5.1 (apps/inbox) Criar `Auth/AnonymousRouteClassification.cs` —
      enum `AnonymousRouteReason`
      (`HealthProbe`/`ExternalUnauthenticated`/`PreExistingAuthMechanism`).
- [x] 5.2 (apps/inbox) Registrar `AddAuthentication`/`AddAuthorization`
      com `FallbackPolicy = RequireAuthenticatedUser` em `Program.cs`.
- [x] 5.3 (apps/inbox) Marcar `GET /health`,
      `POST /webhooks/{channelId}` e `POST /internal/push-notifications`
      com `.AllowAnonymous()` + `AnonymousRouteClassification` (motivo
      correspondente) — a verificação própria de
      `PushNotificationEndpoints` (token por `PendingDispatch`)
      permanece inalterada.
- [x] 5.4 (apps/inbox) Criar `Auth/RouteAuthenticationExtensions.cs` —
      mesma checagem de integridade bidirecional de `apps/api` (2.5),
      chamada em `Program.cs`.

## 6. apps/frontend — login e módulo de token

- [x] 6.1 (apps/frontend) Criar `src/auth/token.ts` —
      `getToken`/`setToken`/`clearToken` sobre `sessionStorage`, sem
      `fetch` (design.md, Decision 7).
- [x] 6.2 (apps/frontend) Criar feature `features/auth/` — `api/authApi.ts`
      (`request<T>`/`ApiError` próprios, `login()` chamando
      `POST /auth/login`), `api/useAuth.ts` (`useLoginMutation`),
      `types/auth.ts`, `components/LoginForm.tsx`, `pages/LoginPage.tsx`.
- [x] 6.3 (apps/frontend) Criar `app/ProtectedRoute.tsx` — redireciona
      para `/login` quando não houver token em `src/auth/token.ts`.
- [x] 6.4 (apps/frontend) Atualizar `app/router.tsx` — rota pública
      `/login` (`LoginPage`) e as demais rotas envolvidas por
      `ProtectedRoute`.

## 7. apps/frontend — integração das features existentes

- [x] 7.1 (apps/frontend) Atualizar `request<T>` de `agentsApi.ts`,
      `channelsApi.ts` e `mcpServersApi.ts` para anexar `Authorization:
      Bearer <token>` via `getToken()` de `src/auth/token.ts`.
- [x] 7.2 (apps/frontend) Nos mesmos `request<T>`, tratar resposta `401`:
      chamar `clearToken()` e redirecionar para `/login`.

## 8. Testes — apps/api (Testcontainers, infraestrutura real)

- [x] 8.1 (apps/api) `AuthLoginTests.cs` — login com credencial correta
      (200 + token), usuário incorreto (401 genérico), senha incorreta
      (401 genérico, mesma mensagem), e expiração retornada igual a 30
      minutos após a emissão quando `Auth:OperatorTokenLifetime` não é
      configurado.
- [x] 8.2 (apps/api) `RouteAuthenticationTests.cs` — rota autenticada
      (`GET /agents`) rejeita sem token e com token inválido; rota
      anônima (`GET /agents/{id}/.well-known/agent-card.json`) continua
      acessível sem token.
- [x] 8.3 (apps/api) `RouteAuthenticationStartupFailureTests.cs` —
      `RouteAuthenticationExtensions` lança exceção quando há rota
      `AllowAnonymous` sem motivo documentado, e quando há motivo
      documentado sem rota correspondente (teste unitário direto na
      extensão, mesmo estilo de
      `ChannelAdapterRegistrationExtensionsTests.cs`).
- [x] 8.4 (apps/api) `AgentCardSecuritySchemeTests.cs` — `AgentCard`
      retornado inclui `SecuritySchemes`/`SecurityRequirements` com
      `HttpAuthSecurityScheme` Bearer.
- [x] 8.5 (apps/api) Teste cobrindo rejeição do token de serviço fora do
      escopo permitido (`POST /agents` com token `service:inbox` →
      `403`) — `ServiceScopeAuthorizationTests.cs`, que também cobre a
      aceitação dentro do escopo (`GET /agents/{id}`,
      `POST /agents/{id}/a2a`).
- [x] 8.6 (apps/api) Atualizar `Support/ApiFactoryFixture.cs` (e as
      outras cinco fixtures `WebApplicationFactory<Program>` do projeto —
      `A2ATaskLifecycleFixture`, `AgentDeactivationFixture`,
      `AgentProviderRejectionFixture`, `McpConnectionTestFixture`,
      `NoProvidersConfiguredFixture` — todas precisam da mesma injeção de
      config para não quebrar com a auth global; achado durante a
      implementação, não previsto no escopo original desta tarefa) para
      injetar `Auth:TokenSigningKey`/`Auth:OperatorUsername`/
      `Auth:OperatorPasswordHash` de teste via novo helper compartilhado
      `Support/TestAuthentication.cs`, e sobrescrever `ConfigureClient`
      para anexar um token de operador válido por padrão em todo cliente
      criado — testes de negócio pré-existentes continuam passando sem
      precisar saber que auth existe; testes desta change que precisam de
      cenário sem token/token inválido sobrescrevem o header
      explicitamente.

## 9. Testes — apps/inbox (Testcontainers, infraestrutura real)

- [x] 9.1 (apps/inbox) `RouteAuthenticationTests.cs` — rota autenticada
      (`GET /channels`) rejeita sem token e com token inválido; rotas
      anônimas (`POST /webhooks/{channelId}`,
      `POST /internal/push-notifications`) continuam acessíveis sem
      token.
- [x] 9.2 (apps/inbox) `RouteAuthenticationStartupFailureTests.cs` —
      mesmo par de cenários do 8.3, para a extensão de `apps/inbox`.
- [x] 9.3 (apps/inbox) Atualizar `Support/InboxFactoryFixture.cs` (e
      `Support/OrchestrationFactoryFixture.cs`, mesma necessidade
      encontrada em apps/api/8.6) para injetar `Auth:TokenSigningKey` de
      teste com o mesmo valor usado por `Support/TestAuthentication.cs`
      de `apps/api` (8.6) via novo helper equivalente
      `Support/TestAuthentication.cs` deste projeto — pré-requisito da
      tarefa 10.2, que autentica contra `apps/inbox` com um token emitido
      de verdade por `apps/api`.

## 10. Teste cruzado — round-trip com credencial de serviço

- [x] 10.1 (`tests/InboxOrchestratorRoundTrip.Tests`) Atualizar o teste de
      round-trip existente para confirmar que `apps/inbox` autentica
      `SendMessage`/`GET /agents/{id}` contra `apps/api` com o token de
      serviço auto-emitido, e que o round-trip completo continua
      funcionando de ponta a ponta.
- [x] 10.2 (`tests/InboxOrchestratorRoundTrip.Tests`) Novo teste cobrindo
      o Risco 1 de `design.md` (chave de assinatura divergente entre
      processos): faz `POST /auth/login` real contra a instância de
      `apps/api` do teste (mesma `Auth:TokenSigningKey` de 8.6/9.3), pega
      o token retornado na resposta e usa **esse mesmo token** (não um
      token forjado no teste) para acessar uma rota autenticada de
      `apps/inbox` (ex. `GET /channels`), afirmando que a resposta não é
      `401`. Para provar que a validação é local (sem chamada de rede de
      `apps/inbox` a `apps/api` para validar o token), configurar
      `Api:BaseUrl` da instância de `apps/inbox` do teste apontando para
      um host inalcançável antes de fazer a chamada — a aceitação do
      token não pode depender de `apps/inbox` conseguir falar com
      `apps/api`.

## 11. Testes — apps/frontend (Vitest + React Testing Library)

- [x] 11.1 (apps/frontend) Testes de `LoginPage`/`LoginForm` — login
      bem-sucedido armazena token e redireciona; credencial inválida
      exibe erro sem armazenar token; validação de campos obrigatórios.
- [x] 11.2 (apps/frontend) Teste de `ProtectedRoute` — navegação sem
      token redireciona para `/login`, com token renderiza a rota.
- [x] 11.3 (apps/frontend) Teste de tratamento de `401` em um
      `request<T>` existente (`agentsApi.test.ts`) — limpa token e
      redireciona para `/login`; cobre também o anexo/ausência do header
      `Authorization`.
- [x] 11.4 (apps/frontend) Atualizar `router.test.tsx` (token via
      `sessionStorage` no `beforeEach`, novo teste sem token
      redirecionando para `/login`) e `src/test/setup.ts` — achado
      durante a implementação: Node 22+ sombreia `sessionStorage` global
      igual já acontecia com `localStorage` (patch já existente no
      arquivo); sem o mesmo patch para `sessionStorage`,
      `src/auth/token.ts` quebraria em todo teste sob jsdom.

## 12. Configuração

- [x] 12.1 Adicionar `Auth:TokenSigningKey` a
      `apps/api/appsettings.Development.json` e
      `apps/inbox/appsettings.Development.json` (mesmo valor de
      desenvolvimento nos dois, gerado com `openssl rand -base64 32`) e a
      `.env.example`. `Auth:OperatorTokenLifetime` não está presente
      nesses arquivos — omitido, assume o default de 30 minutos
      (`TokenSigningOptions`); documentado em prosa no `README.md`
      (seção "Autenticação") em vez de comentário dentro do
      `appsettings.Development.json` — JSON padrão (o parser de
      configuração do ASP.NET Core) não suporta comentários, então
      "documentar comentada" não é literalmente possível no arquivo;
      ajuste em relação ao texto original da tarefa (convenção 9).
- [x] 12.2 Adicionar `Auth:OperatorUsername`/`Auth:OperatorPasswordHash`
      a `apps/api/appsettings.Development.json` (usuário `operator`,
      senha de dev `changeme`, hash gerado e verificado contra
      `OperatorPasswordHasher.Verify` real) e documentar no `README.md`
      (seção "Autenticação") como gerar o hash — comando `python3` com
      `hashlib.pbkdf2_hmac`, verificado como byte-compatível com
      `Rfc2898DeriveBytes.Pbkdf2` (mesmo algoritmo padrão, mais portátil
      que depender de um projeto .NET descartável só para gerar uma
      credencial).
- [x] 12.3 Adicionar `VITE_API_BASE_URL` (já existente) como origem do
      `POST /auth/login` na documentação de `apps/frontend`, sem
      variável nova.
- [x] 12.4 Atualizar `README.md` — seção de convenções (novo item sobre
      allowlist de rotas anônimas e checagem de integridade) e checklist
      de "como subir cada app" (`apps/api`, `apps/inbox`, `apps/frontend`)
      com o novo pré-requisito de login antes de usar `curl`/frontend
      contra `apps/api`/`apps/inbox`.

## 13. Validação dos artefatos

- [x] 13.1 Rodar `openspec validate auth-login-e-servico --strict` e
      corrigir qualquer erro reportado antes de considerar a change
      pronta para revisão. Resultado: `Change 'auth-login-e-servico' is
      valid`.
