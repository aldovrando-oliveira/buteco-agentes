## Context

**O defeito, conferido contra a `main` em `06ce6e5` (03/10/2026).** As linhas
citadas na #116 batem:

- `apps/inbox/src/Buteco.Inbox/Program.cs:112-113`: a `FallbackPolicy` é só
  `RequireAuthenticatedUser()`. Não há requisito de subject, nem
  `IAuthorizationHandler` registrado.
- `apps/inbox/src/Buteco.Inbox/Auth/OperatorTokenAuthenticationHandler.cs:37`:
  o esquema põe o subject em `ClaimTypes.NameIdentifier`, e nada lê esse claim.
- `apps/inbox/src/Buteco.Inbox/Auth/ServiceTokenDelegatingHandler.cs:24`
  (constante na linha 12): o próprio app assina `service:inbox` para chamar o
  `apps/api`, com a mesma chave que ele usa para validar o que recebe.

A única linha da issue que mudou é a do `apps/api`
(`ServiceScopeAuthorizationHandler.cs:29-34`, a regra antiga "quem não é
`service:inbox` passa"): a #102 já a substituiu pela tabela da D6, hoje em
`apps/api/src/Buteco.Api/Auth/ServiceScopeAuthorizationHandler.cs:33-92`.

**As rotas do `apps/inbox`.** Nenhuma usa `.RequireAuthorization()` nem
`[Authorize]` (`git grep` em `apps/inbox/src`), então a `FallbackPolicy` é o que
governa todas as rotas que não são anônimas. São doze autenticadas:

| padrão (`RawText`) | métodos | arquivo |
|---|---|---|
| `/channels/` | `POST`, `GET` | `Channels/Endpoints/ChannelEndpoints.cs:21-22` |
| `/channels/{id:guid}` | `GET`, `PUT` | `ChannelEndpoints.cs:23-24` |
| `/channels/{id:guid}/activate`, `/channels/{id:guid}/deactivate` | `POST` | `ChannelEndpoints.cs:25-26` |
| `/channels/{channelId:guid}/sessions` | `GET` | `Contacts/Endpoints/ChannelSessionEndpoints.cs:12` |
| `/contacts/`, `/contacts/{id:guid}/sessions` | `GET` | `Contacts/Endpoints/ContactEndpoints.cs:15-16` |
| `/sessions/summary` | `GET` | `Contacts/Endpoints/SessionSummaryEndpoints.cs:40` |
| `/sessions/{sessionId:guid}/messages` | `GET` | `Messages/Endpoints/MessageEndpoints.cs:12` |
| `/messages/summary` | `GET` | `Messages/Endpoints/MessageSummaryEndpoints.cs:53` |

E três anônimas, na allowlist de `Program.cs:145-148`: `/health`
(`HealthProbe`, `Program.cs:129-131`), `/webhooks/{channelId:guid}`
(`ExternalUnauthenticated`, `Channels/Webhooks/Endpoints/WebhookEndpoints.cs:15-17`)
e `/internal/push-notifications` (`PreExistingAuthMechanism`, token de
capacidade por `PendingDispatch` no header `X-A2A-Notification-Token`,
`Orchestration/PushNotifications/Endpoints/PushNotificationEndpoints.cs:22-31`).

### Varredura de chamadores

Feita em 03/10/2026, em `06ce6e5`, sobre o repositório inteiro (`apps/` com os
testes de todos os apps, `tests/`, `scripts/`, `deploy/`, `docker-compose*.yml`),
com `git grep` por `.Issue(`, `ServiceSubject`, `"operator"`, `service:`,
`Headers.Authorization`, `AuthenticationHeaderValue`, `"Bearer`,
`X-A2A-Notification-Token`, `INBOX_BASE_URL`, `VITE_INBOX` e pelas URLs e portas
do `apps/inbox`. Todo chamador do `apps/inbox`:

| chamador | arquivo e linha | rota | autenticação | subject |
|---|---|---|---|---|
| frontend, canais | `apps/frontend/src/features/channels/api/channelsApi.ts:26,34` | `/channels/**` | `Authorization: Bearer` com o token do login guardado em `sessionStorage` (`apps/frontend/src/auth/token.ts:10-11`) | `operator` |
| frontend, sessões, mensagens e resumos de período | `apps/frontend/src/features/sessions/api/sessionsApi.ts:38,46` (rotas nas linhas 74, 78, 89, 93) | `/channels/{id}/sessions`, `/sessions/{id}/messages`, `/sessions/summary`, `/messages/summary` | idem | `operator` |
| `apps/workers`, entrega de push notification | `apps/workers/src/Buteco.Workers/Notifications/PushNotificationSender.cs:31-40` | `/internal/push-notifications` (anônima) | `X-A2A-Notification-Token`; o `Authorization` só sai se a config trouxer `Authentication`, e a config que o `apps/inbox` registra só tem `Url` e `Token` (`apps/inbox/src/Buteco.Inbox/Orchestration/DebounceSweepService.cs:201-205`) | nenhum |
| webhooks de canal (Telegram, WAHA) | `apps/inbox/src/Buteco.Inbox/Channels/Webhooks/Endpoints/WebhookEndpoints.cs:15-17` | `/webhooks/{channelId:guid}` (anônima) | a do adapter de cada canal, dentro da rota | nenhum |
| sonda de saúde | `apps/inbox/src/Buteco.Inbox/Program.cs:129-131` | `/health` (anônima) | nenhuma | nenhum |
| testes do `apps/inbox` | `apps/inbox/tests/Buteco.Inbox.Tests/Support/TestAuthentication.cs:26`, anexado por `InboxFactoryFixture.ConfigureClient` | todas | token assinado pelo `ITokenService` do host | `operator` (literal) |
| testes de rota anônima e de token inválido | `apps/inbox/tests/Buteco.Inbox.Tests/RouteAuthenticationTests.cs:36,84` | `/channels`, as três anônimas | sem token ou `token-invalido` | nenhum |
| round-trip entre apps | `tests/InboxOrchestratorRoundTrip.Tests/RoundTripTests.cs:204-212`, token de `RoundTripFixture.LoginAsOperatorAsync` (`Support/RoundTripFixture.cs:273-276`, usuário na linha 90) | `GET /channels` | token do `POST /auth/login` real do `apps/api` | `operator` |

Quem **assina** token com a chave, em todo o repositório: o login do `apps/api`
(`apps/api/src/Buteco.Api/Auth/Endpoints/AuthEndpoints.cs:45`, `operator`), o
`ServiceTokenDelegatingHandler` do `apps/inbox` (`service:inbox`, só em chamadas
de **saída** para o `apps/api`) e os helpers de teste. O `service:connectors`
ainda não é assinado em lugar nenhum da `main`; a #103 vai assiná-lo para chamar
as rotas `/sync` do `apps/api`, não o `apps/inbox`. `scripts/` só tem
`check-docs.py`; `deploy/` só tem o `migrate`, que não monta token.
`apps/workers` não faz chamada HTTP autenticada ao `apps/inbox`.

**Resultado: nenhum chamador legítimo usa subject diferente de `operator` numa
rota autenticada do `apps/inbox`, e nenhum usa token de serviço contra ele.** Não
há chamador a tratar.

## Goals / Non-Goals

**Goals:**

- Só `operator` é autorizado nas rotas autenticadas do `apps/inbox`; qualquer
  outro subject válido recebe `403`.
- As três rotas anônimas continuam como estão, com ou sem token presente.
- O operador continua entrando pelo token que o login do `apps/api` emite, sem
  chamada de rede entre os processos.
- A regra fica provada por teste que reprova com a regra antiga.

**Non-Goals:**

- Chave por serviço ou assinatura assimétrica (#117).
- Qualquer mudança no `apps/api`, no `apps/workers`, no `apps/frontend` ou no
  `apps/connectors` (#103).
- Mudar a autenticação das rotas anônimas.
- Registrar em log o subject recusado. O `403` já é a resposta; log de
  segurança é outra conversa e não tem consumidor hoje.

## Decisions

### D1. Tabela explícita de subjects, com uma linha: `operator`

A regra do `apps/inbox` passa a ser a mesma da D6 da #102, lida para este app:

| subject | rotas autorizadas |
|---|---|
| `operator` | todas as rotas autenticadas |
| qualquer subject de serviço conhecido | só as da sua lista — **hoje nenhum tem lista** |
| qualquer outro, inclusive `service:inbox` e `service:connectors` | nenhuma: `403` |

`service:inbox` cai na última linha. O token que o app assina só serve para o
`apps/api` aceitá-lo; não há rota do `apps/inbox` que o próprio app chame com
ele, e a varredura não achou nenhuma. `service:connectors` cai na mesma linha: a
#103 fala com o `apps/api`, não com o `apps/inbox`.

O `403` sai do esquema existente: o usuário está autenticado e a política
falha, então a autorização chama `ForbidAsync` do
`OperatorTokenAuthenticationHandler`, cujo padrão de
`AuthenticationHandler` responde `403`. Token ausente ou inválido continua
reprovando em `RequireAuthenticatedUser()` e respondendo `401`.

- *Descartado, manter "autenticado passa" até existir um segundo serviço com a
  chave:* a #103 é esse segundo serviço, e o `service:inbox` já é um token de
  serviço assinado com a chave que o `apps/inbox` aceita hoje.
- *Descartado, negar só os subjects de serviço conhecidos (`service:*`):* é a
  forma do defeito que a #102 tirou do `apps/api`: um subject novo, ou com outro
  prefixo, teria o acesso do operador. Recusa por padrão é a regra.

### D2. A regra se escreve como `RequireClaim` na política padrão, sem handler próprio

Com a tabela de serviços vazia, a regra inteira é "o claim
`ClaimTypes.NameIdentifier` vale `operator`". A `FallbackPolicy` passa a ser
`RequireAuthenticatedUser().RequireClaim(ClaimTypes.NameIdentifier,
OperatorSubject)`, montada num arquivo novo,
`apps/inbox/src/Buteco.Inbox/Auth/SubjectAuthorization.cs`, que guarda a
constante `OperatorSubject = "operator"` e o comentário com a regra e o par no
`apps/api`. O `Program.cs` só chama o método.

A comparação de valor do `ClaimsAuthorizationRequirement` precisa ser **exata**
(sensível à caixa): `Operator` não pode passar. Isso se confere no código do
ASP.NET Core 10 antes de confiar (convenção 6), e um teste fixa
(`sub` `Operator` recebe `403`).

**Qual política, medido na implementação:** só a `FallbackPolicy`. O
`Program.cs:112-113` configurava apenas ela; a `DefaultPolicy` ficava no padrão do
framework (`RequireAuthenticatedUser()`), e nenhuma rota do `apps/inbox` usa
`RequireAuthorization` ou `[Authorize]`, com ou sem política nomeada (`git grep` em
`apps/inbox/src`). Então a `FallbackPolicy` é a que governa as doze rotas
autenticadas, e a varredura da D6 confirmou: 12 de 12 respondem `403` a
`service:connectors`. A `DefaultPolicy` não foi igualada (ver Risks); a tarefa 4.3
provou que a varredura pega uma rota que a usasse.

**Conferido no ASP.NET Core 10.0.9 (descompilação):** `ClaimsAuthorizationRequirement`
compara o tipo do claim com `OrdinalIgnoreCase` e o valor com `StringComparer.Ordinal`,
e o `HandleForbiddenAsync` padrão de `AuthenticationHandler<T>` responde `403`. Nada
divergiu desta decisão.

- *Descartado, copiar o `ServiceScopeAuthorizationHandler` do `apps/api` inteiro,
  com a tabela de serviços vazia:* seria um dicionário declarado sem nenhuma
  entrada e um laço que nunca roda. É a convenção 25 (declaração viva sem
  consumidor) nascendo junto, e a checagem de boot de uma lista vazia é a
  vacuidade da convenção 8: passa com e sem a implementação.
- *Descartado, um `AuthorizationHandler` próprio que só compara com
  `operator`:* faz o que o `RequireClaim` já faz, com uma classe a mais para
  testar.

### D3. Sem `libs/`: a regra é duplicada, e o par fica nomeado

O `apps/api` e o `apps/inbox` não compartilham código, e esta change não muda
isso. O que fica duplicado é a constante `operator` e a regra "subject fora da
tabela recebe `403`", como o `TokenService` já é duplicado
(`apps/api/src/Buteco.Api/Auth/TokenService.cs` e
`apps/inbox/src/Buteco.Inbox/Auth/TokenService.cs`). O par no `apps/api`:

- a tabela e a regra: `apps/api/src/Buteco.Api/Auth/ServiceScopeAuthorizationHandler.cs`
  (constantes nas linhas 20-24, lista nas 33-57, regra nas 59-92);
- a checagem de boot da lista: `apps/api/src/Buteco.Api/Auth/ServiceScopeRouteValidation.cs`;
- a ligação: `apps/api/src/Buteco.Api/Program.cs:97-102` e `:169`.

O comentário de `SubjectAuthorization.cs` cita esses caminhos, e o
`docs/architecture.md` registra a duplicação.

**O acordo entre as duas cópias é textual e cruza apps:** o login do `apps/api`
emite `operator` pela constante dele, e o `apps/inbox` passa a exigir `operator`
pela dele. Se uma das duas mudar, o operador perde o `apps/inbox` inteiro. Quem
prova o acordo é o `tests/InboxOrchestratorRoundTrip.Tests`
(`RoundTripTests.cs:204-212`): login real no `apps/api`, `GET /channels` no
`apps/inbox`, `200` esperado (convenção 11, artefato real dos dois lados). Os
testes do `apps/inbox` assinam `operator` como **literal**
(`TestAuthentication.cs:26`) e continuam assim: o literal fixa o valor de fio, e
trocar pela constante deixaria o teste verde com a constante errada.

- *Descartado, extrair a regra e a constante para `libs/`:* dois consumidores de
  uma constante e uma linha de política não pagam uma lib (convenção 2), e a
  regra do monorepo é não compartilhar por padrão.

### D4. Sem checagem de boot agora, e o gatilho para ela

A checagem de boot da D6 da #102 confere a **lista de rotas de cada subject de
serviço** contra os endpoints mapeados. Sem lista, não há o que conferir: a
recusa por padrão não depende de nenhum padrão de rota, então não existe o modo
de falha que a checagem pega (padrão renomeado virando `403` em produção).

**Gatilho:** o primeiro subject de serviço que precisar de uma rota autenticada
do `apps/inbox`. A change que o introduzir traz, juntos, a lista por subject, o
handler no molde do `ServiceScopeAuthorizationHandler` e a checagem no molde do
`ServiceScopeRouteValidation`, com o teste da extensão contra entrada inexistente
e o teste da composição real. **E traz a lição da #102:** a lista casa por
`RoutePattern.RawText`, e `MapGroup("/channels")` com `MapGet("/")` gera
`/channels/`, **com barra final**, que é o padrão real de três das doze rotas
daqui (`POST`/`GET /channels/` e `GET /contacts/`, tabela do Context). Foi o defeito que a
checagem do `apps/api` pegou na primeira execução.

- *Descartado, criar a lista vazia e a checagem agora "para já estar pronto":*
  checagem sobre lista vazia passa sempre (vacuidade da convenção 8), e o código
  dela ficaria sem consumidor (convenção 25). O custo de criá-las no gatilho é
  copiar dois arquivos conhecidos.

### D5. Rotas anônimas ficam como estão

`/health`, `/webhooks/{channelId:guid}` e `/internal/push-notifications`
continuam com `.AllowAnonymous()` e a mesma classificação. Com `IAllowAnonymous`
no endpoint, o middleware de autorização não avalia política nenhuma, então o
`RequireClaim` não as alcança mesmo quando a requisição traz um token de serviço
válido. O teste fixa isso pelo lado observável: token `service:connectors` no
webhook de canal inexistente recebe o `404` da rota, e `service:inbox` no
`/health` recebe `200`. A allowlist de `Program.cs:145-148` e a
`ValidateRouteAuthenticationClassification` não mudam.

- *Descartado, aproveitar a change para exigir token também no webhook:* é mudar
  a forma de autenticação de rota anônima, que passa por revisão própria e está
  fora do escopo da #116.

### D6. Onde os testes moram, e o que cada um prova

Classe nova `apps/inbox/tests/Buteco.Inbox.Tests/SubjectAuthorizationTests.cs`,
com `IClassFixture<InboxFactoryFixture>` (um contêiner Postgres a mais na suíte).
Os tokens de serviço são assinados pelo `ITokenService` do próprio host
(`factory.Services`), com a mesma chave que o app valida: são **validamente
assinados**, e a única diferença para o operador é o `sub`.

- **Subjects recusados em `GET /channels`:** `service:connectors`,
  `service:inbox` (pela constante `ServiceTokenDelegatingHandler.ServiceSubject`,
  o valor que o app realmente assina), `service:desconhecido` e `Operator` →
  `403`.
- **Toda rota autenticada mapeada:** o teste lê os `RouteEndpoint` do
  `EndpointDataSource` do host construído, descarta os que têm
  `IAllowAnonymous`, troca cada parâmetro de rota por um `Guid` novo e manda cada
  par (método, rota) com `service:connectors` → todos `403`. **Contra a
  vacuidade**, o teste afirma antes que o conjunto não é vazio e contém
  `GET /channels/`. **E é pareado** com a mesma varredura usando o token do
  operador, que não pode receber `401` nem `403` em nenhuma: sem o par, um `403`
  vindo de outra causa passaria por recusa de subject. A varredura pega também o
  caso de uma rota futura com `.RequireAuthorization()` sem nome de política, que
  usaria a `DefaultPolicy` e escaparia do `RequireClaim` (ver Risks).
- **Escrita recusada sem efeito:** `POST /channels` com corpo válido e
  `service:connectors` → `403`, e a contagem de canais no banco não muda.
- **Anônimas com token de serviço:** webhook `404`, `/health` `200` (D5). As
  anônimas **sem** token já são cobertas por `RouteAuthenticationTests`
  (`GetHealth_WithoutToken_StaysAnonymous`, `Webhook_WithoutToken_StaysAnonymous`,
  `PushNotification_WithoutToken_StaysAnonymous`), que precisam seguir verdes.
- **Operador:** coberto pela suíte inteira do `apps/inbox` (todo cliente da
  `InboxFactoryFixture` anexa `operator`) e, entre apps, pelo round-trip.

**Guarda contra o defeito real (convenção 15):** com os testes escritos,
reintroduzir a regra antiga (só `RequireAuthenticatedUser()`) e ver reprovarem
os testes de `403`, com os de operador e de rota anônima verdes. O guarda
reprova **no componente que a correção toca** (a política do host, exercida pelo
pipeline HTTP real), e não num handler isolado.

- *Descartado, testar só `GET /channels`:* é o exemplo da issue, mas deixaria as
  outras onze rotas sem asserção e não pegaria rota nova fora da política.
- *Descartado, uma lista de rotas escrita no teste:* uma rota nova ficaria fora
  da lista em silêncio; a varredura sobre o host não depende de alguém lembrar.

### Divergências da implementação

Nenhuma decisão mudou. Três acréscimos, registrados pela convenção 9:

- **Um teste a mais na classe nova:** `PushNotification_WithServiceSubjectAndNoCapabilityToken_IsDecidedByTheRoute`.
  Token `service:connectors` sem `X-A2A-Notification-Token` recebe o `401` da própria
  rota, e não `403`: cobre a terceira rota anônima com token de serviço presente,
  que a D6 deixava só com o caso sem token.
- **A falha da varredura lista todos os pares e o total.** A primeira execução
  vermelha mostrou que `Assert.Empty` trunca a coleção (`···`); a classe passou a
  falhar com `Assert.Fail` e a mensagem completa, e escreve a contagem no output do
  teste.
- **`using Microsoft.AspNetCore.Authorization` saiu do `Program.cs`**, sem uso depois
  que a política passou a vir de `SubjectAuthorization`.

## Árvore de pastas proposta

Só o que muda, dentro do `apps/inbox`:

```
apps/inbox/
├── src/Buteco.Inbox/
│   ├── Program.cs                         # FallbackPolicy vem de SubjectAuthorization
│   └── Auth/
│       ├── AnonymousRouteClassification.cs        (sem mudança)
│       ├── ITokenService.cs                       (sem mudança)
│       ├── OperatorTokenAuthenticationHandler.cs  (comentário: o subject é lido na autorização)
│       ├── RouteAuthenticationExtensions.cs       (sem mudança)
│       ├── ServiceTokenDelegatingHandler.cs       (sem mudança)
│       ├── SubjectAuthorization.cs                # NOVO: OperatorSubject e a política padrão
│       ├── TokenService.cs                        (sem mudança)
│       ├── TokenSigningOptions.cs                 (sem mudança)
│       └── TokenValidationResult.cs               (sem mudança)
└── tests/Buteco.Inbox.Tests/
    ├── RouteAuthenticationTests.cs        (sem mudança; precisa seguir verde)
    ├── SubjectAuthorizationTests.cs       # NOVO
    └── Support/
        ├── InboxFactoryFixture.cs         (sem mudança)
        └── TestAuthentication.cs          (sem mudança; "operator" continua literal)
```

Nada em `libs/`, `apps/api`, `apps/workers`, `apps/frontend` ou
`apps/connectors`. Sem dependência nova: `RequireClaim` e
`ClaimsAuthorizationRequirement` já estão no `Microsoft.AspNetCore.Authorization`
que o app usa.

## Risks / Trade-offs

- **[A constante `operator` diverge entre `apps/api` e `apps/inbox`, e o operador
  perde o `apps/inbox`]** → `tests/InboxOrchestratorRoundTrip.Tests`
  (`RoundTripTests.cs:204-212`) faz login real e espera `200` em `GET /channels`;
  reprova com `403` se as cópias divergirem. Os testes do `apps/inbox` mantêm o
  literal (D3).
- **[Rota nova com `.RequireAuthorization()` sem política usa a `DefaultPolicy`,
  que não tem o `RequireClaim`, e volta a aceitar qualquer subject]** → a
  varredura de toda rota autenticada (D6) reprova nomeando o par (método, rota).
  Não se iguala a `DefaultPolicy` agora: nenhum endpoint a usa, e a linha seria
  configuração sem consumidor.
- **[Comparação do `RequireClaim` não ser exata, e `Operator` ou ` operator`
  passarem]** → conferir o código do `ClaimsAuthorizationRequirement` do ASP.NET
  Core 10 (convenção 6) e o teste com `sub` `Operator` (D2, D6).
- **[Varredura de rotas vazia, verde com e sem a implementação]** → a precondição
  do teste afirma conjunto não vazio contendo `GET /channels/` (D6).
- **[Um `403` por outra causa passar por recusa de subject]** → a varredura é
  pareada com o operador, que não pode receber `401` nem `403` (D6).
- **[Um serviço legítimo futuro precisar de rota do `apps/inbox` e receber
  `403`]** → é o comportamento pretendido até ele entrar na tabela; o gatilho e o
  que a change dele traz estão na D4.
- **[Quem guarda a chave assina `operator`]** → não é coberto por esta change, e
  não é testável aqui: a tabela protege contra defeito de código, não contra
  comprometimento do dono da chave. Fica na #117 (`aguardando gatilho`).
- **[Suíte medida com a máquina carregada pela #103]** → antes de classificar
  qualquer falha, conferir o `load average`; com a máquina carregada, esperar e
  refazer (memória do projeto: timeout de worker vira resultado inválido).

## Migration Plan

Sem migração de dados nem de configuração. Implanta-se o `apps/inbox`; o token
do operador em uso continua valendo. Qualquer token com outro subject passa a
receber `403` nas rotas autenticadas, e a varredura mostra que nenhum chamador
legítimo depende disso. Rollback: reverter o commit do `apps/inbox`.

## Open Questions

Nenhuma. A varredura não achou chamador legítimo com subject diferente de
`operator` em rota autenticada do `apps/inbox`, então não há recusa a decidir.
