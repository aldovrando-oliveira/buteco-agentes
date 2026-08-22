## Context

Hoje `apps/api` e `apps/inbox` não têm nenhuma autenticação — decisão
consciente enquanto o pior caso de exposição era cadastro de canal com
credencial write-only (ver `proposal.md`). A fatia planejada em seguida
(histórico de conversa por sessão na UI do inbox) vai expor conteúdo real
de conversa via API, o que muda o perfil de risco. Esta change precisa
resolver autenticação antes disso, sem invadir o escopo dela (nada de
`Message`/`Session` de UI ou status de entrega aqui).

Restrição estrutural que molda todo o design: `apps/api` e `apps/inbox`
são processos e bancos Postgres separados (`buteco_agents` vs
`buteco_inbox`), sem FK entre eles e sem lib de runtime compartilhada
(isolamento entre apps, convenção do projeto). Um "session store"
tradicional — linha de sessão consultada pelos dois processos — exigiria
um mecanismo de compartilhamento novo, maior que qualquer coisa que já
existe na base. Por outro lado, já existe precedente exato para segredo
compartilhado via configuração, sem chamada de rede: a chave de cada
provedor de LLM precisa estar configurada de forma idêntica e
independente em `apps/api` e `apps/workers` (README, seção `apps/workers`).
Este design segue o mesmo precedente para o token de autenticação.

## Goals / Non-Goals

**Goals:**
- Login de operador único (`apps/frontend` → `apps/api`), com token curto
  aceito por `apps/api` e `apps/inbox` sem chamada de rede entre eles.
- `apps/inbox` autenticado perante `apps/api` para os dois únicos
  endpoints que de fato consome (`POST /agents/{id}/a2a`,
  `GET /agents/{id}`), sem poder chamar mais nada com essa credencial.
- Allowlist de rotas anônimas explícita, com checagem de integridade que
  derruba o boot se alguma rota mapeada não estiver classificada.
- `AgentCard` declarando, de forma compatível com a spec A2A, que o
  endpoint A2A do agente exige credencial.

**Non-Goals:**
- Múltiplos usuários, RBAC, cadastro de usuário, recuperação de senha,
  OAuth/SSO — fora de escopo (usuário único via variável de ambiente).
- Revogação antecipada de token antes do TTL expirar (sem logout
  server-side, sem blacklist) — ver Open Questions.
- Qualquer mudança em `Message`/`Session` de UI, histórico de conversa ou
  status de entrega — escopo da próxima fatia, não desta.
- `apps/workers` — confirmado sem endpoint HTTP (Worker Service puro,
  `Host.CreateApplicationBuilder`, sem Kestrel/`Map*`).
- Cliente A2A externo conseguir de fato usar `SendMessage` de um agente
  que descobriu via `AgentCard` — aceito como limitação atual (Decision 6).

## Árvore de pastas (novo/modificado)

### `apps/api`

```
apps/api/src/Buteco.Api/
├── Auth/
│   ├── Endpoints/
│   │   └── AuthEndpoints.cs                    [NOVO] POST /auth/login
│   ├── OperatorCredentialOptions.cs            [NOVO] Options: OperatorUsername, OperatorPasswordHash
│   ├── TokenSigningOptions.cs                  [NOVO] Options: TokenSigningKey, OperatorTokenLifetime
│   ├── ITokenService.cs / TokenService.cs      [NOVO] emite/valida token HMAC (Decision 1)
│   ├── OperatorTokenAuthenticationHandler.cs   [NOVO] AuthenticationHandler<TOptions> (Bearer)
│   ├── ServiceScopeAuthorizationHandler.cs     [NOVO] restringe sub=service:inbox a 2 rotas (Decision 3)
│   ├── AnonymousRouteClassification.cs         [NOVO] metadata + enum de motivo (Decision 4)
│   └── RouteAuthenticationExtensions.cs        [NOVO] checagem de integridade no startup (Decision 4)
├── A2A/
│   └── AgentCardEndpoints.cs                   [MOD]  SecuritySchemes/SecurityRequirements (Decision 6)
├── Agents/Endpoints/AgentEndpoints.cs          [MOD]  .AllowAnonymous() não se aplica; sem mudança de rota
├── McpServers/Endpoints/McpServerEndpoints.cs  [MOD]  idem — cobertos pelo FallbackPolicy, sem mudança de rota
└── Program.cs                                  [MOD]  AddAuthentication/AddAuthorization, RouteAuthenticationExtensions

apps/api/tests/Buteco.Api.Tests/
├── AuthLoginTests.cs                           [NOVO]
├── RouteAuthenticationTests.cs                 [NOVO] rejeita sem token / token inválido, allowlist íntegra
├── RouteAuthenticationStartupFailureTests.cs   [NOVO] boot falha com rota não classificada
├── AgentCardSecuritySchemeTests.cs             [NOVO]
└── Support/ApiFactoryFixture.cs                [MOD]  injeta Auth:TokenSigningKey/OperatorPasswordHash de teste
```

### `apps/inbox`

```
apps/inbox/src/Buteco.Inbox/
├── Auth/
│   ├── TokenSigningOptions.cs                  [NOVO] Options: TokenSigningKey (mesmo valor de apps/api)
│   ├── ITokenService.cs / TokenService.cs      [NOVO] duplica a lógica de apps/api (Decision 2)
│   ├── OperatorTokenAuthenticationHandler.cs   [NOVO] valida token de operador nas rotas de apps/inbox
│   ├── ServiceTokenDelegatingHandler.cs        [NOVO] assina token service:inbox por requisição de saída (Decision 3)
│   ├── AnonymousRouteClassification.cs         [NOVO]
│   └── RouteAuthenticationExtensions.cs        [NOVO]
├── Channels/Endpoints/ChannelEndpoints.cs      [MOD]  cobertas pelo FallbackPolicy, sem mudança de rota
├── Contacts/Endpoints/ContactEndpoints.cs      [MOD]  idem
├── Channels/Webhooks/Endpoints/WebhookEndpoints.cs                    [MOD] .AllowAnonymous() + classificação
├── Orchestration/PushNotifications/Endpoints/PushNotificationEndpoints.cs [MOD] .AllowAnonymous() + classificação
└── Program.cs                                  [MOD]  AddAuthentication/AddAuthorization,
                                                        RouteAuthenticationExtensions,
                                                        AddHttpMessageHandler<ServiceTokenDelegatingHandler>()
                                                        nos HttpClients AgentReferenceValidator/A2AClient

apps/inbox/tests/Buteco.Inbox.Tests/
├── RouteAuthenticationTests.cs                 [NOVO]
├── RouteAuthenticationStartupFailureTests.cs   [NOVO]
└── Support/InboxFactoryFixture.cs              [MOD]  injeta Auth:TokenSigningKey de teste

tests/InboxOrchestratorRoundTrip.Tests/         [MOD]  round-trip passa a exigir o token de serviço
```

### `apps/frontend`

```
apps/frontend/src/
├── auth/
│   └── token.ts                                [NOVO] getToken/setToken/clearToken (sessionStorage) — sem fetch,
│                                                       importado por cada request<T> de feature (convenção 7)
├── features/
│   └── auth/
│       ├── api/authApi.ts                      [NOVO] request<T>/ApiError próprios + login()
│       ├── components/LoginForm.tsx            [NOVO]
│       ├── pages/LoginPage.tsx                 [NOVO]
│       └── types/auth.ts                       [NOVO]
├── app/
│   ├── router.tsx                              [MOD]  rota /login pública + wrapper que redireciona sem token
│   └── ProtectedRoute.tsx                      [NOVO]
└── features/{agents,mcp-servers,channels}/api/*Api.ts  [MOD] anexam Authorization; tratam 401 (clearToken + redirect)
```

Nenhum diretório novo em `libs/` — ver Decision 2.

## Decisions

### Decision 1: Token stateless assinado com HMAC, sem biblioteca JWT, sem sessão em banco

O token é uma string compacta, não um JWT de biblioteca: `base64url(JSON
{"sub","exp"}) + "." + base64url(HMACSHA256(payload, TokenSigningKey))`.
Validação: recomputa o HMAC, compara com `CryptographicOperations.
FixedTimeEquals` (BCL, evita timing attack), confere `exp > now`.

TTL do token de operador: **30 minutos por padrão**, mas configurável —
`Auth:OperatorTokenLifetime` (`TimeSpan`, seção de configuração `Auth`,
propriedade de `TokenSigningOptions` em `apps/api`), mesmo padrão de
opção configurável já usado em `Session:InactivityTimeout`
(`apps/inbox`) e `Debounce:Window`. O default de 30 minutos vive no
**tipo** `TokenSigningOptions` (valor inicial da propriedade
`OperatorTokenLifetime`), não no `appsettings.json` — a chave
`Auth:OperatorTokenLifetime` pode ficar totalmente ausente de
`appsettings`/variáveis de ambiente sem que `TokenService` precise de
nenhum tratamento especial para "não configurado"; um ambiente que
precisar de outro valor só sobrescreve a chave. Essa é a mesma semântica
que os cenários de `operator-authentication`/`spec.md` e a tarefa 8.1 de
`tasks.md` assumem — o teste do default não depende do fixture carregar
nenhum `appsettings.json`. Resolve a Open Question anterior deste
documento — ver Non-Goals sobre revogação antecipada.

**Por que não `Microsoft.AspNetCore.Authentication.JwtBearer`**: JWT
formal traz conceitos (issuer, audience, JWKS, múltiplas chaves,
algoritmos negociáveis) que este projeto não precisa — um único emissor,
uma única chave, dois claims. Adicionar o pacote e sua superfície de
configuração é abstração que a convenção de evitar abstração prematura
não pede aqui. O padrão já usado em `AesGcmChannelCredentialCipher`
(`apps/inbox`) — criptografia crua do BCL, sem lib de terceiro — é o
precedente direto.

**Por que não sessão em banco/Redis**: exigiria um store novo consultado
por dois processos com bancos hoje isolados — o problema estrutural
descrito em Context. Um token stateless assinado resolve isso sem nenhum
mecanismo de compartilhamento novo além do segredo (que já tem
precedente, ver Context).

### Decision 2: Lógica de token duplicada entre `apps/api` e `apps/inbox` — sem `libs/Auth`

`ITokenService`/`TokenService` (emitir + validar) e
`OperatorTokenAuthenticationHandler` são escritos uma vez em cada app, não
extraídos para `libs/`. `libs/` só existe com necessidade real de
compartilhamento e justificativa explícita (convenção do projeto); o
único caso hoje (`libs/ProviderCatalog`) carrega identidade de provedor
que os dois processos *precisam* concordar em runtime. Aqui não há esse
tipo de acoplamento — cada processo só precisa concordar na *chave* (via
config, já resolvido pela Decision 1), não em código. É o mesmo padrão já
aceito no frontend: `agentsApi.ts` e `channelsApi.ts` duplicam
`request<T>`/`ApiError` inteiros, não só uma função de assinatura de ~15
linhas. Se um terceiro consumidor de `ITokenService` aparecer, essa
decisão deve ser revisitada.

### Decision 3: Credencial de serviço `apps/inbox → apps/api` — token de serviço assinado por requisição, mesmo mecanismo do operador

`apps/inbox` não guarda um token de serviço de longa duração: um
`DelegatingHandler` (`ServiceTokenDelegatingHandler`) assina um token novo
(`sub: "service:inbox"`, TTL fixo de **5 minutos**) a cada requisição de
saída, anexado nos dois `HttpClient`s nomeados que já existem
(`AgentReferenceValidator`, `A2AClient`). Custo de assinar é uma
`HMACSHA256` em memória — desprezível; não há handshake, não há cache
para invalidar, não há processo de refresh para acertar.

TTL fixo de 5 minutos, **hardcoded como constante** em
`ServiceTokenDelegatingHandler` — sem opção equivalente a
`Auth:OperatorTokenLifetime`. Diferente do token do operador, esse token
nunca sai do processo de `apps/inbox` nem é observado por um humano: ele
é gerado e consumido dentro de uma única requisição HTTP de saída, então
o único requisito real é sobreviver ao tempo de rede + processamento até
`apps/api` validar a assinatura — 5 minutos é folga generosa para isso,
não um valor que algum ambiente precisaria ajustar. Tornar esse TTL
configurável adicionaria uma opção sem nenhum cenário de uso real
(convenção de evitar abstração/configuração prematura); o TTL do
operador é configurável porque afeta uma decisão de produto real (por
quanto tempo um humano permanece logado), que varia por ambiente/política
— esse token de serviço não tem equivalente.

Em `apps/api`, esse token é aceito pelo mesmo
`OperatorTokenAuthenticationHandler` (mesma verificação de assinatura),
mas o `sub` é usado por um requisito de autorização extra
(`ServiceScopeAuthorizationHandler`): quando `sub == "service:inbox"`, a
requisição só é autorizada se o endpoint atual for exatamente
`POST /agents/{id}/a2a` ou `GET /agents/{id}` — qualquer outra rota
responde `403 Forbidden`, mesmo com um token estruturalmente válido.
Tokens com `sub: "operator"` não passam por essa restrição.

**Alternativa considerada — credencial estática via env var**: mais
simples isoladamente (comparação de igualdade), mas um segundo mecanismo
de autenticação convivendo com o do operador. Como o mecanismo assinado
já é necessário para o operador funcionar nos dois processos sem sessão
compartilhada (Decision 1), reaproveitá-lo aqui custa pouco a mais — um
segundo `sub` e uma checagem de escopo — e mantém um único caminho de
verificação (`ITokenService.Validate`) em vez de dois.

### Decision 4: Allowlist de rotas com 3 categorias e checagem de integridade bidirecional no startup

`AddAuthorization(options => options.FallbackPolicy =
new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())` —
toda rota exige token válido por padrão, em ambos os apps. Rotas
intencionalmente sem token chamam `.AllowAnonymous()` **e** anexam
`.WithMetadata(new AnonymousRouteClassification(reason))`, `reason` sendo
um enum fechado:

| `AnonymousRouteReason` | Rotas |
|---|---|
| `HealthProbe` | `GET /health` (`apps/api`, `apps/inbox`) |
| `AuthEntryPoint` | `POST /auth/login` (`apps/api`) |
| `PublicDiscovery` | `GET /agents/{id}/.well-known/agent-card.json` (`apps/api`) |
| `ExternalUnauthenticated` | `POST /webhooks/{channelId}` (`apps/inbox`) |
| `PreExistingAuthMechanism` | `POST /internal/push-notifications` (`apps/inbox` — token de capacidade por `PendingDispatch`, mecanismo próprio já existente, não tocado por esta change) |

`RouteAuthenticationExtensions` (uma classe por app, mesmo molde de
`ChannelAdapterRegistrationExtensions.ValidateChannelAdapterRegistrations`)
roda depois de todos os `app.Map*` e antes de `app.Run()`, enumerando
`EndpointDataSource.Endpoints` real do host e checando nos dois sentidos:

1. Todo endpoint com metadata `IAllowAnonymous` **precisa** também ter
   `AnonymousRouteClassification` — pega quem chamou `.AllowAnonymous()`
   sem documentar o motivo (abre rota sem querer, sem justificativa).
2. Toda rota listada como esperada-anônima no allowlist de código do app
   **precisa** existir de fato como endpoint mapeado com
   `.AllowAnonymous()` — pega allowlist desatualizada (rota removida ou
   renomeada sem atualizar a lista).

Boot lança `InvalidOperationException` listando cada problema
encontrado, mesmo formato de mensagem de
`ValidateChannelAdapterRegistrations`. Rotas sem `IAllowAnonymous`
metadata são autenticadas pelo `FallbackPolicy` por padrão — não
precisam de uma tag positiva separada, porque a checagem #2 já garante
que nenhuma rota anônima escapou sem ser documentada.

Todas as rotas de negócio existentes (`/agents/*` exceto o card,
`/mcp-servers/*`, `/providers`, `/channels/*`, `/contacts/*`,
`/agents/{id}/a2a`) ficam autenticadas por padrão, sem exigir nenhuma
mudança nos `Endpoints.cs` existentes além do `Program.cs` global.

### Decision 5: Hash de senha do operador via `Rfc2898DeriveBytes.Pbkdf2`, sem pacote novo

`Auth:OperatorPasswordHash` guarda uma string composta
`{iterations}.{saltBase64}.{hashBase64}`, gerada offline (script/comando
único, fora do escopo de código de produção) e colada na variável de
ambiente. No login, `Rfc2898DeriveBytes.Pbkdf2(senhaInformada, salt,
iterations, HashAlgorithmName.SHA256, hashLength)` recomputa o hash e
compara com `CryptographicOperations.FixedTimeEquals`. Mesmo padrão de
"criptografia crua do BCL" da Decision 1 — nenhuma dependência nova em
`Directory.Packages.props`.

`POST /auth/login` responde `401` genérico tanto para usuário incorreto
quanto para senha incorreta, sem distinguir qual campo errou — mesmo
padrão já usado em `PushNotificationEndpoints.ReceiveAsync`
(`apps/inbox`), que não distingue "task desconhecida" de "token
divergente" na resposta.

### Decision 6: `AgentCard` declara `SecuritySchemes`/`SecurityRequirements` (HTTP Bearer)

O pacote `A2A` (1.0.0-preview2, já referenciado) tem suporte nativo a
isso — `AgentCard.SecuritySchemes`/`SecurityRequirements`,
`HttpAuthSecurityScheme` etc., confirmado no XML doc do pacote instalado,
não assumido de memória. `AgentCardEndpoints.GetAgentCardAsync` passa a
preencher `SecuritySchemes: { "bearer": HttpAuthSecurityScheme { Scheme =
"Bearer" } }` e `SecurityRequirements: [{ Schemes: { "bearer": [] } }]`.

Isso resolve a tensão real do protocolo — a descoberta via `AgentCard` é
pública, mas `SendMessage`/`GetTask` exigem token — como uma decisão de
produto documentada e compatível com a spec, não como omissão: um cliente
A2A externo que descobrir o agente vê, de forma padronizada, que precisa
de um Bearer token que hoje não tem como obter (não existe emissão de
credencial para clientes externos nesta fatia). O único consumidor real
de `SendMessage` hoje é `apps/inbox` (interno, token de serviço). Aceito
conscientemente; revisitar quando houver um consumidor A2A externo real
(ex. expor `POST /auth/login` — ou um mecanismo de credencial dedicado —
para esse consumidor).

### Decision 7: Frontend — módulo de token fora de `features/`, feature `auth` própria para a UI de login

`src/auth/token.ts` (`getToken`/`setToken`/`clearToken`, sem `fetch`)
segue o mesmo precedente de `src/theme.ts` — módulo único, fora de
`features/`, importado por múltiplas features. Não é um cliente HTTP
compartilhado (convenção 7 preservada): cada `request<T>` de cada feature
continua próprio, só importa `getToken()`/`clearToken()` deste módulo
para montar o header `Authorization` e reagir a `401`.

A UI de login (`LoginForm`, `LoginPage`, chamada a `POST /auth/login`)
vive em `features/auth/`, com seu próprio `request<T>`/`ApiError`
(convenção 7) — ele não anexa `Authorization` (não há token antes do
login).

Token guardado em `sessionStorage`, não `localStorage`: token de vida
curta, sem necessidade de sobreviver ao fechamento da aba/navegador —
reduz a janela de exposição a XSS sem custo de implementação adicional
(troca de chamada de API, mesma superfície).

## Risks / Trade-offs

- [Risco] `Auth:TokenSigningKey` divergente entre `apps/api` e
  `apps/inbox` faz todo token de operador emitido por um ser rejeitado
  pelo outro → Mitigação: mesmo cuidado operacional já documentado no
  README para as chaves de LLM entre `apps/api`/`apps/workers`; adicionar
  ao `.env.example` como uma única variável usada pelos dois. Coberto por
  teste cruzado dedicado (`tasks.md`, 10.2): login real contra `apps/api`
  seguido de uso do token retornado contra uma rota autenticada de
  `apps/inbox` — não um token forjado no teste com a mesma chave.
- [Risco] Token stateless não pode ser revogado antes do TTL (sem
  logout server-side real — inclusive se a senha do operador for trocada
  e o processo reiniciado, tokens já emitidos continuam válidos até
  expirar) → Mitigação: TTL curto (30 min por padrão, configurável via
  `Auth:OperatorTokenLifetime`, Decision 1) limita a janela; aceito
  conscientemente no escopo mínimo — sem blacklist/revogação antecipada
  nesta fatia (ver Non-Goals).
- [Risco] Duplicação de `ITokenService` entre `apps/api`/`apps/inbox`
  diverge com o tempo (fix em um, não no outro) → Mitigação: aceito
  conscientemente (Decision 2); revisitar se um terceiro consumidor
  aparecer.
- [Risco] Alguém chama `.AllowAnonymous()` numa rota nova sem anexar
  `AnonymousRouteClassification` → Mitigação: é exatamente o que a
  checagem de integridade do Decision 4 pega — precisa de um teste que
  builda o host de verdade (não só compila) para os dois apps.
- [Risco] `ServiceScopeAuthorizationHandler` mal configurado libera
  `service:inbox` para uma rota além das duas previstas → Mitigação:
  teste dedicado cobrindo rejeição de token de serviço contra uma rota
  fora do escopo (ex. `POST /agents`).

## Migration Plan

Sem dado a migrar — não há tabela de usuários nem schema novo além de
configuração. `Auth:TokenSigningKey`, `Auth:OperatorUsername` e
`Auth:OperatorPasswordHash` (este último só em `apps/api`) precisam
existir antes do primeiro boot com auth ativa — boot falha rápido sem
eles, mesmo padrão de `Api:BaseUrl` ausente em `apps/inbox` hoje. Ordem
de rollout sugerida: variáveis de ambiente em `apps/api` e `apps/inbox`
primeiro (mesmo valor de `TokenSigningKey` nos dois), depois deploy dos
dois backends, depois `apps/frontend` com o login habilitado — evita uma
janela em que o frontend manda `Authorization` para um backend que ainda
não entende o header. Rollback é apenas voltar a versão anterior (sem
auth); nenhuma migração de schema para desfazer.

## Open Questions

Nenhuma no momento — o TTL do token de operador (30 min por padrão,
configurável) foi decidido na Decision 1; revogação antecipada
permanece fora de escopo (Non-Goals).
