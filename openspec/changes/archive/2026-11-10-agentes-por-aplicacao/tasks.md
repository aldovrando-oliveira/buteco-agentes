Um guarda marcado **(guarda)** só conta depois de ter reprovado contra o defeito que ele existe para pegar (convenção 15). Para cada um:

1. o `~/.cache/buteco-agents/bin/guarda-187.sh` reintroduz o defeito nomeado. Ele é cópia do `guarda-186.sh`, com o projeto `rt-crossapp` a mais, e com o mesmo lock das suítes. A mutação toca só o trecho nomeado e nunca restaura um diretório ao `HEAD`;
2. o teste roda e reprova;
3. a árvore volta pela cópia feita antes da mutação, com o sha conferido.

A tarefa registra o nome do teste, a mensagem da reprovação e o "voltou: <arquivo>", com o sha igual ao de antes. Uma mutação que quebra a compilação e reprova por "no tests" não vale: é descartada e refeita. As saídas ficam em `~/.cache/buteco-agents/187-guardas/<tarefa>`.

**As suítes** rodam pelo `~/.cache/buteco-agents/bin/suite-187.sh` (`api`, `rt-inbox`, `rt-crossapp`, `rt-connectors`), uma por vez, com o lock, e com o RabbitMQ de pé (`PAUSAR_RABBIT=0`).
- Vale a regra assimétrica: uma rodada verde conta mesmo com a CPU caindo, e uma reprovação só conta com a CPU em 100 do início ao fim.
- Antes de cada rodada, o load de 1 e de 5 min fica abaixo de 8.
- O `rt-inbox` tem a intermitente conhecida da #176: se ela aparecer, o comentário vai na #176. Reprovação válida dela nos dois worktrees não bloqueia; só no da change, é parada.

**A baseline** está em `~/.cache/buteco-agents/baseline-187/`, em `5e23548`, com a árvore limpa: `api` 1.640 + 1 ignorado (CPU mínima 51, verde conta), `rt-inbox` 15/15 (CPU mínima 90), `rt-crossapp` 4/4, `rt-connectors` 12/12, todas `APROVADA`, e o relatório por nome em `nomes.txt` (1.672 linhas). Detalhes no design ("Baseline").

**Parada** (convenção 9):
- **parar** quando uma premissa falsa mudar comportamento ou contrato, ou quando a correção reduzir cobertura;
- **seguir e registrar** quando a correção só acrescentar teste ou cenário para pôr um guarda no lugar certo, ou só tocar comentário ou documentação.

**Nada de commit parcial.** O trabalho fica na árvore até o dono pedir o commit.

**O cancelamento não muda** (decisão do dono, Q1). Nenhuma tarefa toca o `CancelTask` do `apps/api` nem o `apps/workers`; a #264 cuida disso. Esta change só faz o `CancelTask` da aplicação cair no `403` do filtro e corrige o guia.

**Casos que saem ou mudam de asserção**, porque o requisito sai ou muda nesta change (delta specs). Todo outro caso existente fica sem remoção, renome nem afrouxamento:
- `apps/api`, **muda de asserção:** `ExternalApplicationAuthorizationTests.ApplicationToken_OnEveryAuthenticatedRoute_ReturnsForbidden` passa a afirmar `403` em toda rota autenticada **exceto** `POST /agents/{id}/a2a`. Também afirma que a A2A não responde `403` para uma aplicação com vínculo, e que a lista percorrida não é vazia. A asserção atual `Assert.Contains(("POST", "/agents/{id}/a2a"), routes)` (l. 39) continua: a rota segue na lista percorrida, só com outra expectativa. Nome novo: `ApplicationToken_OnEveryAuthenticatedRoute_ExceptTheA2A_ReturnsForbidden`. Requisito: `route-roles`, "O token de aplicação externa é o subject application, sem papel, só na rota A2A" (cenário "403 em toda rota autenticada, exceto a A2A");
- `apps/api`, **muda de asserção:** `ExternalApplicationAuthorizationTests.ApplicationToken_OnTheA2ARoute_IsForbidden_AndPublishesNoJob` (l. 61) passa a esperar `404` para a aplicação sem vínculo, e continua afirmando que nenhum job é publicado. Nome novo: `ApplicationToken_WithoutALink_OnTheA2ARoute_IsNotFound_AndPublishesNoJob`. Requisito: o mesmo (cenário "A2A sem vínculo recusada sem publicar job");
- `apps/api`, **sai:** `RouteDeclarationValidationTests.TheApplicationSubject_MayHaveAnEmptyList_UntilIssue187` (l. 107). O cenário "A lista vazia de application passa, por exceção" sai da spec. Entra no lugar `TheApplicationSubject_WithAnEmptyList_Throws_NamingIt` (cenário "A lista vazia de application derruba o boot"), que é o guarda 2.3;
- `apps/api`, **muda o arranjo, asserção igual:** os casos de `RouteDeclarationValidationTests` que montam uma `ServiceRoutes` própria com `application` vazia (o `NoServices` da l. 21-25, e as l. 78 e 95) passam a dar a `application` uma rota mapeada no host do próprio caso, porque a lista vazia passa a derrubar o boot. São eles: `AnAuthenticatedRouteWithoutDeclaration_Throws_NamingMethodAndPattern`, `ARoleEntryWithoutRoute_Throws_NamingTheEntry`, `AnEmptyRoleList_Throws_NamingTheEntry`, `AServiceRoute_CountsAsDeclared_AndAnAnonymousRouteStaysOutOfBothSenses` e `AnEmptyServiceRouteList_Throws_NamingTheSubject`. Neste último, o `Assert.DoesNotContain("'application' tem a lista", …)` continua. Requisito: `route-roles`, "Toda rota autenticada declara papel ou subject de serviço, conferido no boot";
- `apps/api`, **muda a matriz esperada:** `RoleMatrixTests.Expected` ganha as 6 linhas novas: as 4 de `/applications/{id:guid}/agents…` e `GET /agents/{id:guid}/applications`, só `admin`, e `GET /agents/{id:guid}/applications/count`, com `admin`, `operator` e `reader`. Com elas mudam os resultados de `TheProductionTable_IsTheExpectedMatrix` (mesma asserção) e de `EachRoute_ForEachRole_FollowsTheTable` (18 resultados novos). **Acrescentado no apply (só arranjo, registrado):** o `SendAsync` do guarda ganha a substituição de `{agentId:guid}`. As duas rotas novas com esse parâmetro lançavam "Parâmetro de rota sem valor no guarda da matriz"; a asserção não muda, como na #186 com `{secretId:guid}`. Requisito: `route-roles`, "Matriz de papéis por rota nos três apps";
- `apps/api`, **muda o valor esperado, achado no apply (10.2, registrado):** `ServiceScopeAuthorizationTests.ProductionServiceRouteLists_MatchTheBuiltHostEndpoints` soma as rotas de todas as listas da `ServiceRoutes` e esperava `7` (2 do inbox + 5 do connectors). Com a A2A na lista de `application` são `8`. A asserção continua exata, sem afrouxar, sem renomear e sem sair; o design não o tinha na lista. Requisito: `route-roles`, "O token de aplicação externa é o subject application, sem papel, só na rota A2A";
- `tests/ApiConnectorsRoundTrip.Tests`, **só o comentário:** `ExternalApplicationRoundTripTests` l. 22-23 ("a lista de rotas da aplicação é vazia até a #187"). A asserção (`GET /agents/` → `403`) continua.

Cada um é listado por nome na comparação da 10.3, com o requisito que o mudou. Um caso fora desta lista que precise sair ou afrouxar é parada.

## 0. Preparação

- [x] 0.1 (scripts locais) `~/.cache/buteco-agents/bin/guarda-187.sh`, cópia do `guarda-186.sh` com o projeto `rt-crossapp` (`tests/CrossAppTaskStoreCompatibility.Tests`) a mais, com o lock das suítes. Conferir com uma mutação nula (recusa com "defeito não aplicado") e uma real em `apps/api/src` (o arquivo volta com o mesmo sha).
  **Feito:** `guarda-187.sh` = `guarda-186.sh` + o projeto `rt-crossapp` (diff: o cabeçalho, o uso e uma linha do `case`). Mutação nula: "defeito não aplicado"; real em `Identity/IdentityTokenLifetimes.cs` (comentário a mais): `AdvisoryLockKeysTests` 6/6, voltou com o sha `1c893d96…`. Saídas em `~/.cache/buteco-agents/187-guardas/0.1/`.
- [x] 0.2 Ler de novo, no `A2A`/`A2A.AspNetCore` `1.0.0-preview2` decompilado, e registrar no design qualquer diferença (convenção 6):
  - o `A2AJsonRpcProcessor.ProcessRequestAsync`, o `DeserializeAndValidate` e o `JsonRpcRequest` com o conversor dele: como trata `method` duplicado e propriedade desconhecida;
  - as `A2AJsonUtilities.DefaultOptions`: duplicadas, caixa;
  - o `A2AServer.ResolveContextAsync` (o `contextId` do cliente aceito como está);
  - em que ponto do `SendMessageAsync` a primeira `SaveTaskAsync` acontece, que é o que o dono por valor assíncrono local exige (D3).
  **Feito, sem diferença do design:** (1) O `JsonRpcRequestConverter` lê o corpo com `JsonDocument.ParseValue` e `JsonElement.TryGetProperty("method")`: com `method` duplicado vale o **último**. (2) Nome que não é método A2A (`A2AMethods.IsValidMethod`, com caixa) já falha no parse do SDK com `-32601`. (3) `ResolveContextAsync` aceita o `contextId` do cliente como está (`ClientProvidedContextId = true`). (4) A primeira `SaveTaskAsync` da task nova acontece no fluxo do `SendMessageAsync` (o `Task.Run` do handler herda o contexto de execução). O filtro usa os mesmos tipos e opções; os casos da 3.6 mediram o mesmo comportamento.

## 1. Banco e entidades (apps/api; `external-application-agents`, `a2a-task-lifecycle`)

- [x] 1.1 `ExternalApplications/ExternalApplicationAgent.cs`, com a configuração EF e o `DbSet`: PK `(application_id, agent_id)`; FK para `external_applications` `Restrict` e para `agents` `Cascade`; `operacoes text[]` com `CHECK (cardinality(operacoes) > 0)`; `liberado_em` e `alterado_em`. As três operações numa constante única, com os valores da spec e a ordem da tabela.
  **Feito:** `ExternalApplications/ExternalApplicationAgent.cs` (entidade e `ExternalApplicationOperations`, com a validação), configuração em `AppDbContext`.
- [x] 1.2 `A2ATaskRecord.ApplicationId` (`application_id uuid null`, FK `Restrict`, índice), gravado só pelo construtor. O `Update` não recebe nem toca o campo.
  **Feito:** `A2ATaskRecord.ApplicationId`, parâmetro opcional só no construtor.
- [x] 1.3 Migration `ExternalApplicationAgents` (só acréscimo: tabela e coluna anulável sem default). Conferir o SQL gerado (sem reescrita de `a2a_tasks`) e o `Down`.
  **Feito:** `20261110010620_ExternalApplicationAgents`. SQL conferido: `ALTER TABLE a2a_tasks ADD application_id uuid;` (sem default, sem reescrita), a tabela com o `CHECK`, os dois índices e a FK. O índice de `a2a_tasks` sai sem `CONCURRENTLY` (nota em `docs/deployment.md`). O `Down` apaga a FK, a tabela, o índice e a coluna.
- [x] 1.4 **(guarda)** "Valores repetidos gravados uma vez" e o `CHECK` da lista vazia, gravando direto pelo `DbContext`. **Defeito:** o `CHECK` removido da configuração.
  **Guarda feito:** `TheDatabase_RefusesAnEmptyList_AndRepeatedValuesAreStoredOnce` reprovou ("Assert.IsType() Failure: Value is null / Expected: typeof(DbUpdateException)") com a linha do `CheckConstraint` removida da migration; voltou `Infrastructure/Migrations/20261110010620_ExternalApplicationAgents.cs` (`2c387339…`). **Ajuste do defeito (registrado):** o banco do teste é migrado pela migration, então tirar o `CHECK` só da configuração não muda o banco; o defeito que o guarda pega é o banco sem a restrição.

## 2. A rota A2A aberta para a aplicação e o boot (apps/api; `route-roles`, `route-authentication`, D2.1)

- [x] 2.1 `ServiceRoutes[application] = [("POST", "/agents/{id}/a2a")]`. Saem o `SubjectAllowedEmpty` (`ServiceScopeAuthorizationHandler.cs` l. 39-45) e o `&& subject != …SubjectAllowedEmpty` (`RouteDeclarationValidation.cs` l. 93), com os comentários que os citam (`ServiceScopeAuthorizationHandler.cs` l. 79-84, sobre a lista vazia; `RouteDeclarationValidation.cs` l. 18-19, na lista das checagens). No mesmo passo vêm a 2.2 e a 2.3: a rota nunca fica aberta sem o filtro da seção 3 na mesma árvore.
  **Feito** no mesmo passo do filtro (3.1) e do dono (4.x).
- [x] 2.2 `RouteDeclarationValidationTests`: o arranjo dos cinco casos da lista do topo; sai `TheApplicationSubject_MayHaveAnEmptyList_UntilIssue187`.
  **Feito:** `HostWithTheApplicationRoute` mapeia `POST /aplicacao/a2a` em cada um dos cinco casos, e `NoServices` a declara. Asserções iguais.
- [x] 2.3 **(guarda)** `TheApplicationSubject_WithAnEmptyList_Throws_NamingIt` ("A lista vazia de application derruba o boot"). **Defeito:** a exceção do `application` de volta no `if` da checagem.
  **Guarda feito:** reprovou ("Assert.Throws() Failure: No exception was thrown") com `&& subject != ServiceScopeAuthorizationHandler.ApplicationSubject` de volta no `if`; voltou `Auth/RouteDeclarationValidation.cs` (`3699bbf6…`).
- [x] 2.4 **(guarda)** `ApplicationToken_OnEveryAuthenticatedRoute_ExceptTheA2A_ReturnsForbidden` (lista do topo). **Defeito:** `("GET", "/agents/{id:guid}")` acrescentada à lista de `application`.
  **Guarda feito:** reprovou ("GET /agents/{id:guid}: 404") com a rota na lista de `application`; voltou `Auth/ServiceScopeAuthorizationHandler.cs` (`0ad02320…`).

## 3. O filtro da rota A2A (apps/api; `a2a-task-lifecycle`, `a2a-push-notifications`, D1, D2)

- [x] 3.1 Filtro de endpoint no grupo que o `MapA2A` devolve, só para o subject `application`, antes do SDK. Ordem: o vínculo com a aplicação ativa (uma consulta, sem ler `agents`) → `404` vazio; depois o corpo, desserializado com o `JsonRpcRequest`, o `SendMessageRequest` e as `A2AJsonUtilities.DefaultOptions` do SDK, com buffering e volta ao início; método → operação; push → `notificacao-push`; senão `403` vazio. Corpo ilegível segue para o SDK. O `service:inbox` não entra no filtro.
  **Feito:** `A2A/ApplicationA2AFilter.cs`, posto com `AddEndpointFilter` no grupo do `MapA2A` (`Program.cs`); `A2A/ApplicationCaller.cs` (subject `application` sem id legível é aplicação sem vínculo: `404`, nunca o caminho sem filtro). O filtro funciona com o `MapPost` gerado pelo SDK (medido pelos casos).
- [x] 3.2 **(guarda)** "O 404 não revela se o agente existe": agente inexistente, agente sem vínculo e aplicação desativada, com o token ainda no prazo, comparados byte a byte (status, `Content-Type`, `Content-Length` e corpo), e nenhuma task em `a2a_tasks`. **Defeitos, uma rodada cada:**
  - (a) o filtro consultando `agents` antes do vínculo e devolvendo o `-32600` para o inexistente;
  - (b) o filtro sem a condição de aplicação ativa.
  **Guarda feito, duas rodadas.** (a) Com o filtro lendo `agents` antes e deixando o inexistente seguir para o SDK: `TheNotFound_IsTheSame_…` reprovou ("Expected: NotFound / Actual: OK"); voltou `A2A/ApplicationA2AFilter.cs` (`9bcbbe00…`). (b) Sem a condição de aplicação ativa: reprovou ("Expected: 404  0 '' / Actual: 200 application/json 326 …TASK_STATE_SUBMITTED…"); voltou o mesmo arquivo (`9bcbbe00…`).
- [x] 3.3 `ApplicationToken_WithoutALink_OnTheA2ARoute_IsNotFound_AndPublishesNoJob` (lista do topo).
  **Feito (lista do topo):** `ApplicationToken_WithoutALink_OnTheA2ARoute_IsNotFound_AndPublishesNoJob`.
- [x] 3.4 **(guarda)** A matriz método × operação: para cada um dos 11 métodos do SDK e um nome desconhecido, com o vínculo nas três operações e com cada uma retirada, o status esperado escrito à parte do mapa de produção (a expectativa não é lida do mapa). `CancelTask` de task própria em `submitted` → `403`, e a task continua `submitted`; `ListTasks` → `403`. **Defeitos, uma rodada cada:**
  - (a) `ListTasks` mapeado a `consultar-task`;
  - (b) `CancelTask` mapeado a `enviar-mensagem`.
  **Guarda feito, duas rodadas**, mais o caso "Cancelar está fora das operações" (`CancelTask_IsForbidden_AndTheTaskStaysSubmitted`) e "Listar tasks…" (`ListTasks_IsForbidden_WithNoTaskInTheBody`). (a) `ListTasks` → `consultar-task`: `EachMethod_ForEachSetOfOperations_FollowsTheMatrix` reprovou ("[enviar-mensagem,consultar-task,notificacao-push] ListTasks: esperado 403, veio 200; …"); voltou `A2A/ApplicationA2AFilter.cs` (`9bcbbe00…`). (b) `CancelTask` → `enviar-mensagem`: a matriz reprovou ("… CancelTask: esperado 403, veio 200; …") e o caso do cancelamento também ("Expected: Forbidden / Actual: OK"); voltou o mesmo arquivo (`9bcbbe00…`).
- [x] 3.5 **(guarda)** "Push sem a operação" → `403`, sem task e sem job; "Push com a operação" → o job publicado leva o `pushNotificationConfig`. **Defeito:** o filtro olhando só o `method`, sem a configuração.
  **Guarda feito:** `APushConfig_NeedsThePushOperation_AndWithItTheJobCarriesIt` reprovou ("Expected: Forbidden / Actual: OK") com `CarriesPushConfig` devolvendo sempre `false`; voltou `A2A/ApplicationA2AFilter.cs` (`9bcbbe00…`).
- [x] 3.5b **(guarda)** (pedido do dono, 09/11/2026) "Push sem a operação", cenário de `a2a-push-notifications`: um `SendMessage` com `configuration.pushNotificationConfig`, de uma aplicação com `enviar-mensagem` e sem `notificacao-push`, recebe `403`, nenhuma task é criada e nenhum job é publicado. **Defeito:** o filtro aceitando `pushNotificationConfig` sem a operação de push (o push é detectado, mas a exigência de `notificacao-push` sai do conjunto exigido).
  **Guarda feito:** `APushConfig_WithoutThePushOperation_IsForbidden_WithNoTaskAndNoJob` reprovou ("Expected: Forbidden / Actual: OK") com a exigência de `notificacao-push` trocada por `true` (o push detectado e aceito); voltou `A2A/ApplicationA2AFilter.cs` (`9bcbbe00…`).
- [x] 3.6 **(guarda)** "O que o filtro autoriza é o que o SDK executa" (decisão do dono, Q2). Os casos:
  - `method` duplicado nos dois sentidos (`GetTask` e depois `ListTasks` → `403`, e o `ListTasks` não roda; `ListTasks` e depois `GetTask` → roda o `GetTask`);
  - `gettask` → `403`;
  - `pushNotificationConfig` em `params.message` → a task é criada, e o job não leva push;
  - `pushNotificationConfig` duplicado em `configuration`.

  Cada caso afirma o que o SDK executou (job, push no `TaskJobMessage`, task lida), não só o status do filtro. **Defeito:** o filtro com parser próprio, um `JsonDocument` que lê o **primeiro** `method`.
  **Guarda feito:** `WhatTheFilterAllows_IsWhatTheSdkRuns` reprovou ("Expected: Forbidden / Actual: OK", no `GetTask` seguido de `ListTasks`) com o filtro lendo o **primeiro** `method` por `JsonDocument.EnumerateObject`; voltou `A2A/ApplicationA2AFilter.cs` (`9bcbbe00…`).
- [x] 3.7 "Agente inativo com vínculo rejeita a task" (task `rejected`, métrica `AgentInactive`, sem job), e "Desativar corta a A2A na hora" (`external-applications`), com o vínculo ainda listado.
  **Feito:** `AnInactiveLinkedAgent_RejectsTheTask_WithNoJob` (estado `rejected`, linha `AgentInactive` em `task_rejections`, sem job) e `ADeactivatedApplication_LosesTheA2A_AtOnce_AndKeepsItsAgents`.
- [x] 3.8 "O inbox não passa pela checagem": um agente sem vínculo nenhum, `SendMessage` com o token do inbox → `submitted` e job publicado.
  **Feito:** `TheInbox_DoesNotGoThroughTheCheck`.

## 4. O dono da task e o filtro por dono e agente (apps/api; `a2a-task-lifecycle`, D3)

- [x] 4.1 O dono de escopo: o `RoutingA2ARequestHandler` o põe (o id do `buteco_app` do principal) antes de chamar o `A2AServer`, só para `application`; o `PostgresTaskStore` do `apps/api` o lê na inserção. Comentário com o motivo (D3) e a dependência medida na 0.2.
  **Feito:** `A2A/TaskOwnerScope.cs`; o `RoutingA2ARequestHandler` o põe no `SendMessage` da aplicação, e o `PostgresTaskStore` o lê só na inserção.
- [x] 4.2 **(guarda)** "Dono gravado na criação" (`application_id` = a aplicação) e "A task do inbox fica sem dono". **Defeito:** o store sem ler o dono de escopo.
  **Guarda feito:** `TheApplicationIsTheOwner_AndTheInboxTaskHasNone` reprovou ("Expected: b971bd14-… / Actual: null") com o store gravando `null`; voltou `A2A/PostgresTaskStore.cs` (`31a8e011…`).
- [x] 4.3 No `RoutingA2ARequestHandler`, para `application`: `GetTask` e `SendMessage` com `message.taskId` conferem dono **e** agente da rota, com o mesmo `A2AException` `-32001` "Task '{id}' not found." do SDK.
  **Feito:** `EnsureOwnTaskAsync`, com a mesma `A2AException` `-32001` "Task '{id}' not found." do SDK.
- [x] 4.4 **(guarda)** Task de outra aplicação, task sem dono e task própria em outro agente: as três respostas são iguais byte a byte à de um id inexistente (mesmo `id` do JSON-RPC). A continuação alheia não muda a task nem publica job. **Defeitos, uma rodada cada:**
  - (a) a condição de agente removida (filtro só por dono);
  - (b) a condição de dono removida (filtro só por agente).
  **Guarda feito, duas rodadas.** (a) Sem a condição de agente: `SomeoneElsesTask_AnOwnerlessTask_AndAnOwnTaskOnAnotherAgent_AreNotFound` reprovou ("Strings differ / Expected: …\"error\":{\"code\":-32001… / Actual: …\"result\":{\"id\":…", na task própria em outro agente); voltou `A2A/RoutingA2ARequestHandler.cs` (`e11ed0bf…`). (b) Sem a condição de dono: reprovou da mesma forma, na task de outra aplicação; voltou o mesmo arquivo (`e11ed0bf…`).
- [x] 4.5 **(guarda)** "O inbox lê a task de uma aplicação". **Defeito:** o filtro de dono aplicado a todo subject.
  **Guarda feito:** `TheInbox_ReadsAnApplicationsTask` reprovou ("KeyNotFoundException: The given key was not present in the dictionary.", a resposta veio com `error` em vez de `result`) com o filtro aplicado a todo subject; voltou `A2A/RoutingA2ARequestHandler.cs` (`e11ed0bf…`).
- [x] 4.6 No `RoutingA2ARequestHandler`, para `application`: `contextId` informado só passa se existir task dela, no agente da rota, com esse contexto; senão `A2AException` `-32602` "Contexto não encontrado.", antes de o SDK criar a task.
  **Feito:** `EnsureOwnContextAsync`, `-32602` "Contexto não encontrado." (`RoutingA2ARequestHandler.ContextNotFoundMessage`).
- [x] 4.7 **(guarda)** "Contexto inexistente e contexto alheio dão a mesma resposta" (decisão do dono, Q5): um `contextId` nunca usado e o de uma conversa do inbox no mesmo agente, comparados byte a byte, sem task e sem job. Com eles, "A conversa continua no contexto próprio" (o histórico chega ao job) e "Conversa nova sem contexto". **Defeitos, uma rodada cada:**
  - (a) a checagem só recusando contexto **de outro dono** (o desconhecido passa como conversa nova);
  - (b) a mensagem do alheio diferente da do inexistente.
  **Guarda feito, duas rodadas.** (a) Recusando só o contexto de outro dono: `AnUnknownContext_AndSomeoneElsesContext_GetTheSameAnswer` reprovou ("Expected: 200 … 373 …\"result\":{\"task\"… / Actual: 200 … 86 …\"Contexto não encontrado.\"": o inexistente virou conversa nova); voltou `A2A/RoutingA2ARequestHandler.cs` (`e11ed0bf…`). (b) Mensagem distinta para o alheio: reprovou ("Expected: … \"Contexto não encontrado.\" / Actual: … \"Contexto de outro dono.\""); voltou o mesmo arquivo (`e11ed0bf…`).

## 5. Delegação e o acordo com o apps/workers (`rt-crossapp`; D4)

- [x] 5.1 **(guarda)** (`tests/CrossAppTaskStoreCompatibility.Tests`) "O worker não apaga o dono": a task criada pelo `PostgresTaskStore` real do `apps/api` com dono passa por `working` e `completed` pelo store real do `apps/workers`, e a linha continua com o mesmo `application_id`. **Defeito:** o `Update` do `A2ATaskRecord` do `apps/api` zerando o `ApplicationId`. O worker não mapeia a coluna; a mutação é no lado que mapeia.
  **Guarda feito:** `TaskOwnerCompatibilityTests.TheOwner_SurvivesTheTransitions_OfBothApps` reprovou ("Expected: 1a7128aa-… / Actual: null") com `ApplicationId = null` no `Update` do `A2ATaskRecord` do `apps/api`; voltou `A2A/A2ATaskRecord.cs` (`28b65150…`).
- [x] 5.2 (`rt-crossapp`) "Filha num agente não liberado": uma task inserida pelo store real do `apps/workers`, como a delegação faz, nasce com `application_id` nulo. Pela rota do `apps/api`, a aplicação recebe o erro de task inexistente pela rota do agente da mãe, e `404` pela do agente da filha.
  **Dividida no apply (registrado):** o `rt-crossapp` não sobe host HTTP do `apps/api`, só os dois stores. A parte do acordo fica lá (`TaskOwnerCompatibilityTests.ATaskCreatedByTheWorkers_HasNoOwner_EvenWhenTheParentHasOne`); a parte da rota fica no `apps/api` (`ApplicationA2ARouteTests.AChildTaskOnAnUnlinkedAgent_IsNotReachable`), com a mesma linha sem dono no agente da filha, criada pelo inbox. Só muda o lugar; nenhum cenário sai.
  **Feito (ver a nota acima): `ATaskCreatedByTheWorkers_HasNoOwner_EvenWhenTheParentHasOne` e `AChildTaskOnAnUnlinkedAgent_IsNotReachable`.**

## 6. As rotas do vínculo e do card (apps/api; `external-application-agents`, `route-roles`, D5)

- [x] 6.1 `GET`/`POST /applications/{id:guid}/agents`, `PUT`/`DELETE /applications/{id:guid}/agents/{agentId:guid}`, com o Admin gravado (`CurrentUser`), a ordem de conferência da spec e as mensagens dela. Requests e responses em `ExternalApplications/`.
  **Feito:** `ExternalApplications/ExternalApplicationAgentEndpoints.cs` (as 4 rotas do vínculo e as 2 do card); o `409` também pela chave primária, na corrida de dois Admins.
- [x] 6.2 Os cenários de `external-application-agents`: nasce sem agentes; desativar/reativar mantém; inativo aparece com `ativo` `false`; aplicação inexistente; liberar com duas operações (ordem da tabela); sem operação; `cancelar-task` desconhecida; já liberado (`409`, operações intactas); agente inexistente; agente inativo liberado; trocar operações (e o `SendMessage` seguinte → `403`); retirado entre abrir e salvar (`404`); retirar (`204`, e a A2A seguinte → `404`).
  **Feito:** `ExternalApplicationAgentEndpointsTests` (`ANewApplication_HasNoAgents_AndALinkSurvivesDeactivation`, `InvalidGrants_AreRefusedByField_WithNoLink`, `AnUnknownApplication_IsNotFound`, `AnInactiveAgent_CanBeGranted_AndIsListedAsInactive`, `ChangingAndRemoving_TakeEffectOnTheNextA2ACall`, `AnOperator_CannotGrant`).
- [x] 6.3 **(guarda)** "Já liberado" → `409` com as operações intactas. **Defeito:** o `POST` gravando por cima (upsert).
  **Guarda feito:** `GrantingAgain_IsAConflict_AndKeepsTheOperations` reprovou ("Expected: Conflict / Actual: Created") com o `POST` gravando por cima; voltou `ExternalApplications/ExternalApplicationAgentEndpoints.cs` (`9cc035ea…`).
- [x] 6.3b `GET /applications/` com `agentes: [{ id, nome }]` por aplicação (Q11, aceita; a spec `external-applications`, "Listar aplicações", ganhou o campo e o cenário "Os agentes liberados na lista" no início do apply, porque a Q11 não tinha entrado na delta).
  **Feito:** `ApplicationSummary.Agentes`, numa consulta para a página; `TheList_CarriesTheGrantedAgents_ByName`.
- [x] 6.4 `GET /agents/{id:guid}/applications/count` e `GET /agents/{id:guid}/applications`, na `RoleRoutes` e no `CurrentUser` só a segunda; cenários "Leitor vê só a contagem", "Admin vê a lista", "Agente sem aplicação". A contagem não traz nome nem id; nenhuma das duas traz client ID.
  **Feito:** `TheAgentCard_CountsForEveryRole_AndListsOnlyForTheAdmin` (`{"total":3,"ativas":2}`, `{"total":0,"ativas":0}`, a lista com as ativas antes, sem client ID nem segredo).
- [x] 6.5 **(guarda)** `RoleMatrixTests.Expected` com as 6 linhas (lista do topo) e o cenário "Aplicações com acesso a um agente, por papel". **Defeitos, uma rodada cada:**
  - (a) a lista `GET /agents/{id:guid}/applications` com `Operate`;
  - (b) a contagem só com `admin`.
  **Guarda feito, duas rodadas.** (a) A lista com `Operate`: `EachRoute_ForEachRole_FollowsTheTable(GET /agents/{id:guid}/applications, Operator)` e `TheProductionTable_IsTheExpectedMatrix` reprovaram ("o papel Operator diverge da matriz da spec"); voltou `Auth/RoleRoutes.cs` (`8955a759…`). (b) A contagem só com `admin`: reprovaram os casos do Reader e do Operator na contagem e o da tabela ("o papel Operator diverge…"); voltou o mesmo arquivo (`8955a759…`).
- [x] 6.6 **(guarda)** "Admin rebaixado perde as rotas do vínculo na requisição seguinte" (`route-roles`, "Prazos"). **Defeito:** a chamada do `CurrentUser` removida do `POST /applications/{id}/agents`.
  **Guarda feito:** `ADemotedAdmin_LosesTheLinkRoutes_OnTheNextRequest` reprovou ("Expected: Forbidden / Actual: Created") sem o `CurrentUser` no `POST`; voltou `ExternalApplications/ExternalApplicationAgentEndpoints.cs` (`9cc035ea…`).

## 7. Acordos

- [x] 7.1 (`tests/InboxOrchestratorRoundTrip.Tests`) Com a tabela de vínculos e a coluna presentes, a mensagem do canal percorre inbox → api → worker como hoje, e a task criada pelo `service:inbox` tem `application_id` nulo.
  **Feito:** `RoundTripTests.MessageReceived_TriggersFullRoundTrip_TheInboxTaskHasNoOwner` (task `completed`, `application_id` nulo).
- [x] 7.2 (`tests/ApiConnectorsRoundTrip.Tests`) Só o comentário do `ExternalApplicationRoundTripTests` (l. 22-23), da lista do topo.
  **Feito.**
- [x] 7.3 (`apps/api`, emissor real em processo) De ponta a ponta, sem semeador:
  1. o Admin cria a aplicação e libera o agente A em `enviar-mensagem` e `consultar-task` pelas rotas reais;
  2. a aplicação obtém o token por `client_credentials` e chama `SendMessage` e `GetTask` no agente A;
  3. o `GetTask` devolve a task dela, com o dono gravado;
  4. no agente B, sem vínculo, a mesma chamada recebe `404`.
  **Feito:** `ApplicationA2ARouteTests.EndToEnd_AnApplicationReachesOnlyItsLinkedAgent` (o emissor real do `apps/api` em processo; nenhum semeador).

## 8. Docs

- [x] 8.1 `docs/a2a-integration.md`:
  - "O que a aplicação alcança": os agentes liberados, as três operações, o `404` e o `403` vazios antes do JSON-RPC (inclusive para aplicação desativada, na hora), o "não encontrada" de task de outro dono ou de outro agente, o `-32602` "Contexto não encontrado." e como abrir conversa nova (sem `contextId`), o push dependente da operação e a entrega não reconferida, e a delegação;
  - "A regra mais importante": o `404`/`403` da aplicação como exceção, ao lado do `401`;
  - as notas operacionais "até a #187".
  **Feito:** "O que a aplicação alcança" reescrita (a tabela das operações, o `404`/`403`, task e contexto, o push, o agente inativo, a delegação), a regra do HTTP 4xx com as duas exceções novas, a introdução e as notas operacionais.
- [x] 8.2 `docs/a2a-integration.md`, o cancelamento como é hoje (decisão do dono, Q1), **sem mudar comportamento**:
  - l. 59-62: o `CancelTask` é atendido para o `service:inbox` e grava `TASK_STATE_CANCELED`, mas o worker não interrompe uma task em `working` e a sobrescreve com `completed`/`failed`; só a task ainda não pega pelo worker fica cancelada; para a aplicação, `403`; #264;
  - a linha de `TASK_STATE_CANCELED` na tabela `TaskState` (l. 453): produzido pelo `CancelTask`, terminal só se o worker ainda não pegou a task.
  **Feito:** o parágrafo "Cancelamento (`CancelTask`), como é hoje" depois das l. 59-62, e a linha de `TASK_STATE_CANCELED` na tabela, com a #264; "os 4 estados não produzidos" passou a 3.
- [x] 8.3 `docs/a2a-integration.md` l. 555 ("Não há streaming nem push notification neste endpoint hoje"), premissa 6: só o streaming não existe; o push, sim (seção "Push Notification").
  **Feito.**
- [x] 8.4 Comentário do `PostgresTaskStore.ListTasksAsync` (`apps/api`, l. 57-63), premissa 7: o método é alcançável por JSON-RPC pelo `service:inbox` e fechado para a aplicação (`403`).
  **Feito.**
- [x] 8.5 `01-ARQUITETURA_E_CONVENCOES.md` (Autenticação: o subject `application` só na A2A, o filtro antes do SDK, o dono da task) e `docs/deployment.md` (a migration e a volta atrás para a imagem da #186: `403` em toda rota, vínculos e donos ficam).
  **Feito:** `01` (a lista de rotas de `application`, o boot sem a exceção, o vínculo e o dono da task) e `docs/deployment.md` ("Agentes liberados por aplicação externa (#187)": o índice sem `CONCURRENTLY`, a volta atrás).
- [x] 8.6 Antes do archive: `02-HISTORICO_E_STATUS.md` e `CHANGELOG.md`, com a medição da convenção 18 contra a projeção do design, em colunas separadas para produção, testes, duplos e casos.
  **Feito:** `02-HISTORICO_E_STATUS.md` ("`agentes-por-aplicacao`") e `CHANGELOG.md` (Added e Changed), com a medição em colunas separadas; `scripts/check-docs.py`: OK.
## 9. Lista dos guardas (conferência)

| tarefa | caso | defeito |
|---|---|---|
| 1.4 | valores repetidos uma vez; lista vazia recusada pelo banco | `CHECK` removido |
| 2.3 | A lista vazia de application derruba o boot | a exceção de volta |
| 2.4 | 403 em toda rota, exceto a A2A | `GET /agents/{id:guid}` na lista de `application` |
| 3.2 | O 404 não revela se o agente existe (byte a byte) | (a) `agents` lido antes do vínculo, com `-32600`; (b) sem a condição de ativa |
| 3.4 | matriz método × operação, com `CancelTask` e `ListTasks` | (a) `ListTasks` → `consultar-task`; (b) `CancelTask` → `enviar-mensagem` |
| 3.5 | push sem a operação | o filtro só pelo `method` |
| 3.5b | push sem a operação → `403`, sem task e sem job | o filtro aceitando `pushNotificationConfig` sem `notificacao-push` |
| 3.6 | o que o filtro autoriza é o que o SDK executa | parser próprio lendo o primeiro `method` |
| 4.2 | dono gravado; inbox sem dono | o store sem o dono de escopo |
| 4.4 | task de outro dono, sem dono e de outro agente = inexistente (byte a byte) | (a) sem a condição de agente; (b) sem a de dono |
| 4.5 | o inbox lê a task de uma aplicação | filtro para todo subject |
| 4.7 | contexto inexistente = alheio (byte a byte) | (a) só o alheio recusado; (b) mensagens diferentes |
| 5.1 | o worker não apaga o dono | o `Update` zerando o `ApplicationId` |
| 6.3 | já liberado → `409` | upsert no `POST` |
| 6.5 | matriz de papéis com as 6 rotas | (a) lista com `Operate`; (b) contagem só `admin` |
| 6.6 | Admin rebaixado perde o vínculo na requisição seguinte | sem o `CurrentUser` |

## 10. Fechamento

- [x] 10.1 `dotnet build` sem aviso novo; `openspec validate agentes-por-aplicacao --strict`.
  **Feito:** nenhum aviso novo (18 distintos na baseline e na change, nos quatro projetos de teste); `openspec validate agentes-por-aplicacao --strict`: válido.
- [x] 10.2 Suítes da change pelo `suite-187.sh`, uma por vez, em `~/.cache/buteco-agents/187-fechamento/`.
  **Feito:** sem contêiner do Testcontainers antes de cada suíte, e o `rabbitmq_1` no `podman ps` do início de cada uma. `api`: 1ª rodada (`187-fechamento/`) **reprovada pelo `ProductionServiceRouteLists_MatchTheBuiltHostEndpoints` (7 → 8), corrigida** (o caso que a change mudou; ver a lista do topo; o script a marcou como inconclusiva só por a CPU ter caído a 62, mas a falha não era de tempo); 2ª rodada (`187-fechamento-api2/`) **APROVADA**, 1685 + 1 ignorado, CPU mín. 60. `rt-inbox` 16/16 APROVADA (CPU 100); `rt-crossapp` 6/6 APROVADA (CPU 100); `rt-connectors` 12/12 APROVADA (CPU 100).
- [x] 10.3 Comparação por nome contra a baseline (`nomes-trx.py`, GUID normalizado, multiconjunto). Nenhum caso some nem muda fora da lista do topo, e cada caso novo fica ligado ao requisito.
  **Feito:** `187-fechamento-final/nomes.txt` (1.720): saem só os 3 da lista do topo; entram 51, todos aprovados, por classe no `02`. Nenhum caso fora da lista saiu, mudou de nome ou afrouxou; o único que mudou de valor esperado está na lista do topo, acrescentado no apply.
- [x] 10.4 O design corrigido com toda divergência achada no apply (convenção 9).
  **Feito:** D7 no design (o que o apply mudou).
- [x] 10.5 Conferência com o `apps/api` local num banco descartável, como na #186:
  - o Admin libera um agente pela rota;
  - a aplicação chama pelo `curl`, mostrando o `404`, o `403`, a task própria, a alheia não encontrada e o contexto não encontrado;
  - a imagem de `5e23548` sobre o banco migrado mostra que a aplicação volta a `403` na A2A.

  O banco, o contêiner e a imagem são apagados no fim. Resultado no `02`.
  **Feito:** conferências 1 a 9 (as do pedido do dono de 09/11/2026), com o `apps/api` e o `apps/workers` do worktree, o RabbitMQ de desenvolvimento e o banco descartável `buteco_agents_conf187`; a 9 com a imagem de `5e23548`. Saídas em `~/.cache/buteco-agents/187-conferencia/`; resultado no `02`. O banco, o contêiner e a imagem foram apagados.
