> **Paralelo com a #103.** Esta change roda no worktree
> `../buteco-agents-116`, branch `fix/116-restricao-subject-inbox`. A #103
> edita `docs/architecture.md`, `02-HISTORICO_E_STATUS.md` e `CHANGELOG.md`, e
> esses três arquivos **podem conflitar** no merge; o
> `01-ARQUITETURA_E_CONVENCOES.md` também pode, se a #103 mexer na seção
> Autenticação. Edições curtas e localizadas, em seção nova sempre que possível.
> Não subir o `apps/inbox` nem outro app em modo dev neste worktree (portas da
> #103); os testes usam Testcontainers e não precisam de servidor de pé.
> **Antes de classificar qualquer falha de teste, conferir o `load average`**; com
> a máquina carregada, esperar e refazer.

## 1. Conferência antes do código

- [x] 1.1 `apps/inbox`: conferir no código do ASP.NET Core 10 (fonte ou
  descompilação, convenção 6) que `ClaimsAuthorizationRequirement` compara o
  valor do claim de forma exata e sensível à caixa, e que a falha da política com
  usuário autenticado chega ao `ForbidAsync` do `OperatorTokenAuthenticationHandler`
  e responde `403`. Se divergir, corrigir a D2 do `design.md` antes de seguir
  (convenção 9).
- [x] 1.2 `apps/inbox`: rodar `dotnet test apps/inbox/Inbox.sln` e
  `dotnet test tests/InboxOrchestratorRoundTrip.Tests` antes de qualquer mudança
  e registrar os números (baseline da convenção 19), com o `load average` do
  momento.

## 2. Testes primeiro

- [x] 2.1 `apps/inbox`: criar `tests/Buteco.Inbox.Tests/SubjectAuthorizationTests.cs`
  com `IClassFixture<InboxFactoryFixture>` e um helper que assina token com o
  `ITokenService` do host para um `sub` qualquer.
- [x] 2.2 `apps/inbox`: teoria em `GET /channels` com `service:connectors`,
  `service:inbox` (pela constante `ServiceTokenDelegatingHandler.ServiceSubject`),
  `service:desconhecido` e `Operator`, todos esperando `403`.
- [x] 2.3 `apps/inbox`: varredura de toda rota autenticada mapeada, lida do
  `EndpointDataSource` do host construído (sem `IAllowAnonymous`), com parâmetros
  de rota trocados por `Guid` novo: precondição de conjunto não vazio contendo
  `GET /channels/`; com `service:connectors`, todas `403`, e a falha nomeia o par
  (método, rota); pareada com o operador, que não recebe `401` nem `403` em
  nenhuma.
- [x] 2.4 `apps/inbox`: `POST /channels` com corpo válido e `service:connectors`
  → `403`, e a contagem de canais no banco não muda.
- [x] 2.5 `apps/inbox`: rotas anônimas com token de serviço presente:
  `POST /webhooks/{guid novo}` com `service:connectors` → `404`; `GET /health`
  com `service:inbox` → `200`.
- [x] 2.6 `apps/inbox`: rodar a classe nova contra o código atual e confirmar que
  os testes de `403` reprovam e os de operador e de rota anônima passam (é o
  defeito da #116 observado por execução, não só por leitura).

## 3. Implementação

- [x] 3.1 `apps/inbox`: criar `src/Buteco.Inbox/Auth/SubjectAuthorization.cs` com
  a constante `OperatorSubject = "operator"` e o método que monta a política
  padrão (`RequireAuthenticatedUser().RequireClaim(ClaimTypes.NameIdentifier,
  OperatorSubject)`), com comentário da regra, da duplicação e do par no
  `apps/api` (`ServiceScopeAuthorizationHandler.cs`,
  `ServiceScopeRouteValidation.cs`, `Program.cs:97-102,169`) e do gatilho da D4.
- [x] 3.2 `apps/inbox`: em `src/Buteco.Inbox/Program.cs`, trocar a
  `FallbackPolicy` das linhas 112-113 pela política de `SubjectAuthorization`.
- [x] 3.3 `apps/inbox`: atualizar o comentário de
  `src/Buteco.Inbox/Auth/OperatorTokenAuthenticationHandler.cs:8-12`, que hoje
  diz só que o token de serviço "não é usado nas rotas de entrada": passa a dizer
  que o esquema aceita qualquer subject válido e que a recusa é feita na
  autorização (`SubjectAuthorization`).
- [x] 3.4 `apps/inbox`: manter `tests/Buteco.Inbox.Tests/Support/TestAuthentication.cs:26`
  com o literal `"operator"` (D3); não trocar pela constante.

## 4. Guarda contra o defeito real (convenção 15)

- [x] 4.1 `apps/inbox`: com a correção aplicada, rodar `SubjectAuthorizationTests`
  e `RouteAuthenticationTests` verdes.
- [x] 4.2 `apps/inbox`: reintroduzir a regra antiga (só
  `RequireAuthenticatedUser()` na `FallbackPolicy`), rodar a classe **inteira**
  (não um teste isolado) e registrar aqui quais reprovaram: os de `403` precisam
  reprovar, os de operador e de rota anônima precisam passar. Restaurar a
  correção e rodar de novo.
  **Resultado (03/10/2026):** com a regra antiga, 6 reprovaram —
  `GetChannels_WithSubjectOtherThanOperator_ReturnsForbidden` (`service:connectors`,
  `service:inbox`, `service:desconhecido`, `Operator`),
  `EveryAuthenticatedRoute_WithServiceSubject_ReturnsForbidden` e
  `CreateChannel_WithServiceSubject_ReturnsForbiddenAndCreatesNothing` — e 9
  passaram (operador e rotas anônimas). Restaurado; `git grep` sem resto.
- [x] 4.3 `apps/inbox`: com a correção aplicada, acrescentar temporariamente uma
  rota de teste com `.RequireAuthorization()` sem política (ou equivalente no
  teste) e confirmar que a varredura da 2.3 reprova nomeando essa rota (risco da
  `DefaultPolicy`). Remover a rota temporária.
  **Resultado:** a varredura reprovou com `1 de 13 rotas`, nomeando
  `GET /guarda-temporario-default-policy → 200`. Rota removida; `git grep` sem resto.

## 5. Suítes

- [x] 5.1 `apps/inbox`: `dotnet test apps/inbox/Inbox.sln` verde, com o número
  medido comparado à baseline da 1.2 (a diferença é a classe nova).
- [x] 5.2 `apps/inbox`: `dotnet test tests/InboxOrchestratorRoundTrip.Tests`
  verde, sem mudança no projeto (prova o acordo do `operator` entre o login do
  `apps/api` e a exigência do `apps/inbox`).
- [x] 5.3 `apps/inbox`: conferir o `load average` antes de classificar qualquer
  falha das duas suítes; com a máquina carregada, esperar e refazer, e registrar.

## 6. Documentação (edições curtas e localizadas; podem conflitar com a #103)

- [x] 6.1 `apps/inbox`: `docs/architecture.md`, seção Autenticação: trocar só a
  frase "O `apps/inbox` ainda autoriza qualquer subject válido nas rotas dele
  (issue #116)" pela regra nova: o `apps/inbox` aceita só o subject `operator`
  nas rotas autenticadas, e qualquer outro subject válido recebe `403`; **um
  serviço que precise de rota do `apps/inbox` traz a lista de rotas por subject e
  a checagem de boot da lista, no molde do `apps/api`** (gatilho da D4, que
  precisa sobreviver ao archive, convenção 23); a regra é duplicada, sem `libs/`,
  com o par no `apps/api`.
- [x] 6.2 `apps/inbox`: `01-ARQUITETURA_E_CONVENCOES.md`, seção Autenticação:
  trocar só "o `apps/inbox` ainda não restringe subject (#116)" pela mesma regra,
  curta: só `operator` nas rotas autenticadas, outro subject `403`, e o serviço
  que precisar de rota do `apps/inbox` traz a lista por subject e a checagem de
  boot, no molde do `apps/api`.
- [x] 6.3 `apps/inbox`: `CHANGELOG.md`, `[Unreleased]` → `### Fixed`: uma entrada
  nova no fim da seção, sem reescrever as existentes.
- [x] 6.4 `apps/inbox`: `02-HISTORICO_E_STATUS.md`: seção nova para esta change
  (o que mudou, o que a implementação mediu, resultado do guarda da 4.2), sem
  reescrever trechos existentes; a entrada da #116 em "Issues abertas por esta
  linha" fica como está.
- [x] 6.5 `apps/inbox`: rodar `python3 scripts/check-docs.py` e
  `openspec validate inbox-restricao-de-subject --strict`.

## 7. Revisão de divergência

- [x] 7.1 `apps/inbox`: se qualquer achado da implementação mudou uma decisão
  (D1-D6), corrigir o `design.md` com a causa real antes do archive (convenção 9).
