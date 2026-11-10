## Context

A #186 entregou a aplicação externa sem alcance: o subject `application` tem lista vazia na
`ServiceRoutes` (`ServiceScopeAuthorizationHandler.cs` l. 85), a checagem de boot aceita essa lista
vazia por exceção nomeada (`SubjectAllowedEmpty`, l. 45; `RouteDeclarationValidation.cs` l. 93), e o
token da aplicação recebe `403` em toda rota. O claim `buteco_app` (GUID da aplicação) já chega ao
principal do `apps/api`. Esta change abre `POST /agents/{id}/a2a` para a aplicação com o limite por
agente e por operação, e grava o dono de cada task.

### O código em `5e23548` (main depois da #186)

- **A rota.** `app.MapA2A(RoutingA2ARequestHandler, "/agents/{id}/a2a")` (`Program.cs` l. 258) usa a
  sobrecarga que mapeia **só** o `POST` JSON-RPC (A2A.AspNetCore `1.0.0-preview2`,
  `A2ARouteBuilderExtensions.MapA2A`); as rotas REST do SDK (`/message:send`, `/tasks/{id}` …) não
  são mapeadas. O `RoutingA2ARequestHandler` resolve o `A2AServer` do agente pelo `id` da rota e
  repassa os 11 métodos; agente inexistente → `A2AException` `-32600` "Agente '{id}' não encontrado."
- **O despacho do SDK** (`A2AJsonRpcProcessor`): `SendMessage`, `GetTask`, `ListTasks`, `CancelTask`,
  `CreateTaskPushNotificationConfig`, `GetTaskPushNotificationConfig`,
  `ListTaskPushNotificationConfig`, `DeleteTaskPushNotificationConfig`, `GetExtendedAgentCard` (resposta
  única), `SendStreamingMessage` e `SubscribeToTask` (SSE). Outro nome → `-32601`.
- **O que cada um faz aqui** (decompilado o `A2AServer`):
  - `SendMessage`: cria a task, o `EnqueueingAgentHandler` publica o job; `configuration.
    pushNotificationConfig` vai no `TaskJobMessage`, e o `apps/workers` entrega o webhook.
    `message.taskId` continua uma task existente (lida pelo id, **sem filtro de agente**); `contextId`
    faz o worker carregar o histórico das tasks `completed` do mesmo agente e contexto.
  - `GetTask`: lê `a2a_tasks` pelo id, **sem filtro de agente** (`PostgresTaskStore.cs` l. 15-17).
  - `CancelTask`: grava `TASK_STATE_CANCELED` no store (o `EnqueueingAgentHandler` não sobrescreve o
    `CancelAsync` padrão). O worker só respeita isso se ainda não pegou o job (guarda de task terminal,
    `AgentExecutionService.cs` l. 150); se a task já está `working`, ele a termina e sobrescreve o
    `canceled` com `completed`/`failed`.
  - `ListTasks`: lista `a2a_tasks` de **todos** os agentes por contexto/estado (`PostgresTaskStore.cs`
    l. 57-63).
  - Os quatro `*TaskPushNotificationConfig`: sempre `-32003` "Push notifications not supported."
    (`A2AServer` l. 1409-1429). `GetExtendedAgentCard`: sempre `-32007`.
  - `SendStreamingMessage` e `SubscribeToTask`: funcionam pelo notificador **do processo**; o worker é
    outro processo, então o stream só vê o `submitted`. O card declara `streaming: false`.
- **`a2a_tasks`** (`task_id`, `agent_id` FK com cascade, `context_id`, `state`, `status_timestamp`,
  `payload`) é escrita pelo `apps/api` (criação) e pelo `apps/workers` (transições, e a **criação da
  task filha** da delegação, `AgentDelegationToolSetResolver.cs` l. 133-145). Cada app tem o seu
  `AppDbContext`; o schema é migrado só pelo `apps/api`.
- **Métricas.** `task_executions` (worker) marca `Origin` `External` ou `Delegation`; `task_rejections`
  (api) grava a recusa do `SendMessage`. O Insights lê essas duas e `a2a_tasks` só por `agent_id`/
  `state` (`GetAgentInsightsQueryHandler.cs` l. 657, `GetSystemInsightsQueryHandler.cs` l. 673).

## Goals / Non-Goals

**Goals:** a rota A2A aberta à aplicação só nos agentes e operações liberados; o dono da task e o
filtro; as rotas do Admin e do card do agente; a retirada da exceção da checagem de boot.

**Non-Goals:** cancelamento real (Q1); streaming; a entrega do push reconferida no worker (Q3);
"último uso por agente"; registro das recusas do filtro (não existe fonte, ver g); as telas (#188).

## Decisions

### D1. (a) As operações

Dos 11 métodos que a rota atende, **três viram operações liberáveis**:

| operação (`operacoes[]`) | o que libera | métodos |
|---|---|---|
| `enviar-mensagem` | criar task, continuar conversa ou task própria | `SendMessage` |
| `consultar-task` | ler o estado e a resposta de task própria | `GetTask` |
| `notificacao-push` | registrar o webhook da task | `configuration.pushNotificationConfig` no `SendMessage` |

- **Push (decisão em aberto da issue): a operação cobre configurar; a entrega é consequência.** Neste
  sistema só se configura push dentro do `SendMessage` (os métodos dedicados respondem `-32003`), e a
  entrega é feita pelo `apps/workers` no estado terminal. `SendMessage` com `pushNotificationConfig` e
  sem a operação → `403` na chamada inteira, sem task. A entrega **não** é reconferida: uma task criada
  com push liberado entrega mesmo que o Admin retire a operação depois (vale para as tasks em voo, no
  máximo até o fim delas). *Alternativa descartada:* reconferir na entrega — o worker teria de ler o
  vínculo (tabela do domínio do `apps/api`, contrato novo entre os dois apps e suíte `workers` na
  conta) para fechar uma janela de uma task. *Outra descartada:* ignorar o `pushNotificationConfig`
  em silêncio — a aplicação acharia que será avisada. (Q3)
- **Cancelar task fica fora (decisão do dono, Q1; era a parada da j.2).** O `CancelTask` de hoje
  promete o que não cumpre: "interrompe uma execução" é falso para task `working`. Liberar a operação
  daria à aplicação um `canceled` que vira `completed` depois. São três operações, e o `CancelTask`
  cai na regra do método fora das operações (`403`, abaixo). O cancelamento real é a #264 (Backlog),
  que acrescenta a quarta operação. Esta change **não muda** o comportamento do cancelamento; só
  corrige o `docs/a2a-integration.md` (l. 59-62 e a linha de `TASK_STATE_CANCELED`, l. 453) para
  dizer o que acontece hoje. *Alternativa descartada:* oferecer só para task `submitted` — a corrida
  entre a checagem e o worker pegar o job deixa o mesmo `canceled` sobrescrito.
- **Método fora das operações** (`CancelTask`, `ListTasks`, `SendStreamingMessage`, `SubscribeToTask`,
  os quatro de push config, `GetExtendedAgentCard`, nome desconhecido): **`403` vazio** para a
  aplicação, mesmo com vínculo. `ListTasks` vazaria tasks de todo mundo; os outros não funcionam ou
  não fazem parte do contrato. *Alternativa descartada:* deixar o SDK responder (`-32601`, `-32003`,
  `-32007`, `CancelTask` funcionando) — o `ListTasks` sozinho já impede.

### D2. (b) Onde e como a checagem acontece

1. **A rota abre para `application`**: `("POST", "/agents/{id}/a2a")` entra na lista dele na
   `ServiceRoutes`, e no mesmo commit saem `SubjectAllowedEmpty` e o `if` dele em
   `RouteDeclarationValidation` (a lista vazia de qualquer subject volta a derrubar o boot, sem exceção).
   O caso `ApplicationToken_OnEveryAuthenticatedRoute_ReturnsForbidden` passa a afirmar `403` em toda
   rota **exceto** a A2A (como o comentário da #186 diz), e a A2A ganha os casos desta change.
2. **Um filtro de endpoint na rota A2A, só para o subject `application`, antes do SDK.** Ele lê o
   corpo com o **mesmo tipo e as mesmas opções do SDK** (`JsonRpcRequest`,
   `A2AJsonUtilities.DefaultOptions`, com buffering e volta ao início), para não haver divergência de
   parse entre o que o filtro autoriza e o que o SDK executa (ex.: `method` duplicado). Na ordem:
   1. aplicação ativa **e** vínculo com o agente da rota (uma consulta: `external_application_agents`
      junto de `external_applications.ativa`); falha → **`404` vazio**;
   2. método → operação (D1); fora das três, ou operação não liberada, ou `pushNotificationConfig`
      sem `notificacao-push` → **`403` vazio**;
   3. corpo ilegível (sem `method`, JSON inválido): segue para o SDK, que responde o erro JSON-RPC de
      parse — o mesmo corpo, lido pelo mesmo código, então nada passa sem método.

   **O que o filtro autoriza é o que o SDK executa (guarda do dono, Q2).** O filtro não tem parser
   próprio: usa o `JsonRpcRequest` e o `SendMessageRequest` do SDK com as `A2AJsonUtilities.
   DefaultOptions`, e só decide depois de desserializar o objeto inteiro, como o SDK. Os casos do
   guarda: `method` duplicado nos dois sentidos (`"GetTask"` e depois `"ListTasks"` com
   `consultar-task` liberado → `403`, e o `ListTasks` não roda; `"ListTasks"` e depois `"GetTask"` →
   passa, e roda o `GetTask`), o nome com caixa trocada (`"gettask"`), `pushNotificationConfig`
   duplicado ou dentro de `params.message` em vez de `params.configuration`, e o corpo lido duas vezes
   (o SDK recebe o corpo do início, inteiro). Em cada caso, o guarda afirma o que o SDK de fato
   executou (job publicado, push no `TaskJobMessage`, task lida), não só o status do filtro.
   *Formato:* HTTP vazio, como o `401`/`403` da autenticação que o `docs/a2a-integration.md` já
   descreve como "antes do JSON-RPC", e como a decisão da issue (404/403). *Alternativa descartada:*
   erro JSON-RPC com HTTP 200 — o A2A não define código de "proibido", e inventar um `-320xx` próprio
   é contrato novo que nenhum cliente A2A conhece. (Q2)
3. **O `404` não revela se o agente existe.** Para o subject `application`, agente inexistente, agente
   sem vínculo e aplicação desativada dão a **mesma** resposta: `404`, corpo vazio, sem cabeçalho a
   mais. O passo 1 acontece antes de qualquer leitura de `agents`, e a aplicação nunca chega ao `-32600`
   "Agente não encontrado." do `RoutingA2ARequestHandler`. Guarda: os três casos comparados byte a byte.
   O `service:inbox` continua recebendo o `-32600` de hoje.
4. **Estados:**
   - **Aplicação desativada:** os vínculos ficam (reativar os devolve); a chamada A2A dá `404` **na
     hora**, mesmo com o token ainda no prazo de 5 min, porque o passo 1 lê `ativa` do banco. Melhora o
     "até 5 minutos" da #186 para a A2A (as outras rotas já são `403`). (Q6)
   - **Agente desativado:** o vínculo fica; a chamada passa no filtro e o `SendMessage` termina
     `TASK_STATE_REJECTED`, como para o inbox, com a métrica `AgentInactive`; o `GetTask` das tasks
     próprias continua. *Alternativa descartada:* `404` — esconderia um estado que o Admin resolve
     reativando, e trataria a aplicação diferente do inbox. O texto dos quadros muda (f). (Q7)
   - **Agente apagado:** não existe exclusão de agente (nenhum `MapDelete` em `/agents`). A FK do
     vínculo para `agents` leva `ON DELETE CASCADE`, como `a2a_tasks`, para o dia em que existir.

### D3. (c) O dono da task

- **Onde:** coluna `a2a_tasks.application_id uuid null`, FK para `external_applications` (`RESTRICT`;
  aplicação não se exclui), índice. Gravada **só na inserção**, pelo `PostgresTaskStore` do `apps/api`,
  a partir de um dono de escopo que o `RoutingA2ARequestHandler` põe antes de chamar o `A2AServer`
  (valor assíncrono local, como o `IHttpContextAccessor`, que atravessa o `Task.Run` do SDK). O
  `Update` não toca nela; o `apps/workers` não a mapeia, então as transições dele também não.
- **Tasks do `service:inbox`:** `application_id` nulo. O filtro só existe para o subject
  `application`: o inbox continua sem filtro, exatamente como hoje (`GetTask` de qualquer task).
- **Tasks anteriores à change (decisão em aberto):** ficam com `application_id` nulo, que é o mesmo
  "do Buteco" das tasks do inbox. **Recomendação: nenhuma aplicação as vê; o inbox continua vendo
  todas.** Antes desta change nenhuma aplicação externa conseguia criar task (a #186 dava `403`), então
  toda task sem dono é do Buteco por construção. *Alternativa descartada:* visíveis a qualquer
  aplicação — daria a uma aplicação liberada em `consultar-task` a leitura de qualquer conversa
  antiga do inbox pelo id. (Q4)
- **O filtro, para o subject `application`:** a task tem de ter `application_id` = a aplicação **e**
  `agent_id` = o agente da rota. O agente entra porque hoje o `GetTask` ignora a rota: sem ele, uma
  aplicação com `consultar-task` só no agente A leria pela rota de A as tasks dela no agente B. Onde:
  - `GetTask`: senão, `-32001` com a mensagem do SDK, `Task '{id}' not found.` — idêntica à de task
    inexistente;
  - `SendMessage` com `message.taskId` (continuação): a mesma regra e a mesma resposta;
  - `SendMessage` com `contextId` (decisão do dono, Q5 com ajuste): **hoje, um `contextId` que não
    existe vira conversa nova com o id que o cliente mandou** — o `A2AServer.ResolveContextAsync` só
    lê a task quando vem `message.taskId`; o `contextId` é aceito como está
    (`ClientProvidedContextId = true`), e o worker carrega o histórico das `completed` daquele agente e
    contexto, se houver. Por isso "de outro dono" e "inexistente" não podem ter respostas diferentes.
    Para a aplicação, **um `contextId` só é aceito se existir uma task dela, neste agente, com esse
    contexto.** Qualquer outro — inexistente, de outra aplicação, do Buteco, ou dela em outro agente —
    recebe `-32602` "Contexto não encontrado.", sem task e sem job; a conversa nova se abre **sem**
    `contextId` (o SDK gera um). Guarda: o corpo da resposta do inexistente e o do alheio comparados
    byte a byte (com o mesmo `id` do JSON-RPC), como no `404`. *Alternativa descartada:* aceitar o
    `contextId` alheio — o worker montaria o histórico com a conversa de outro dono.
  - `message.taskId` com `contextId` de outra task: os dois são conferidos, cada um pela sua regra.
  - cancelar e push por task não existem para a aplicação (D1), então não há o que filtrar ali.

### D4. (d) A delegação não herda a restrição

A task filha é criada pelo `apps/workers`, que não conhece a coluna: nasce com `application_id` nulo,
como o inbox. O agente alvo não precisa estar liberado para a aplicação (é o "Agentes alcançados por
delegação não precisam ser liberados." do quadro), e a filha não leva push (`TaskJobMessage` sem
config, l. 145). A aplicação **não** consulta a filha pelo id: dono nulo e outro agente dão "não
encontrada"; a resposta da filha chega à aplicação dentro da resposta da task mãe. *Alternativa
descartada:* gravar a aplicação na filha — exigiria o worker escrever a coluna (contrato novo, suíte
`workers`), e abriria à aplicação tasks de agentes que ela não tem liberados.

### D5. (e) As rotas de vínculo e o card do agente

Todas do Admin com o papel **gravado** (`CurrentUser`, como `/applications/*`), na `RoleRoutes`:

| rota | papéis | resposta |
|---|---|---|
| `GET /applications/{id:guid}/agents` | A | `200` `[{ agenteId, nome, descricao, ativo, operacoes, liberadoEm }]` por nome; `404` "Aplicação não encontrada." |
| `POST /applications/{id:guid}/agents` | A | `{ agenteId, operacoes }` → `201` com o item; `404` aplicação; `400` campo `agenteId` "Agente não encontrado."; `400` campo `operacoes` "Marque ao menos uma operação." ou "Operação desconhecida: x."; `409` "Este agente já está liberado para a aplicação." |
| `PUT /applications/{id:guid}/agents/{agentId:guid}` | A | `{ operacoes }` → `200` com o item; `404` "Agente não liberado para esta aplicação."; `400` como acima |
| `DELETE /applications/{id:guid}/agents/{agentId:guid}` | A | `204`; `404` como acima |
| `GET /agents/{id:guid}/applications/count` | A O L | `200` `{ total, ativas }`; `404` agente |
| `GET /agents/{id:guid}/applications` | A | `200` `[{ id, nome, ativa, operacoes, ultimoUso }]`, ativas antes, por nome |

- Aplicação desativada e agente inativo podem ser liberados e editados (o quadro mostra os dois). Sem
  senha: liberar não gera credencial, como editar na #186. (Q10)
- **Card "Aplicações com acesso": duas rotas.** A contagem é uma entrada A O L e a lista é outra, só A;
  cada uma é uma linha da `RoleRoutes` e uma linha da matriz da spec, e o guarda da matriz já percorre
  as duas com os três papéis. *Alternativa descartada:* uma rota com resposta por papel — a matriz só
  afirma "403 ou não", e uma lista que vazasse para o Leitor passaria no guarda; precisaria de um guarda
  próprio para cobrir o que a tabela não vê. (Q8)
- `ultimoUso` da lista é o da aplicação (#186), não "última chamada a este agente", que é dado novo
  (não pedido aqui). `ativas` serve o "Usado por N aplicações externas ativas (M no total)" e o
  rodapé "Desativar este agente afeta N aplicações ativas" da versão 63. (Q9, Q13)

### D6. Testes e guardas

Cada guarda com o defeito que o reprova, conferido no apply (convenção 15):

| guarda | defeito |
|---|---|
| `404` idêntico, byte a byte, para agente inexistente, sem vínculo e aplicação desativada | o filtro consultar `agents` antes do vínculo, ou responder `-32600` |
| matriz da A2A: cada método × com/sem a operação | um método a mais no mapa, ou `ListTasks` liberado |
| push sem a operação → `403` e nenhuma task | o filtro olhar só o `method` |
| `GetTask` de task de outra aplicação, do Buteco (nula) e de outro agente → `-32001` igual a inexistente | filtro só por dono, ou só por agente |
| continuação (`message.taskId`) alheia recusada | o filtro só no `GetTask` |
| `contextId` inexistente e alheio: o mesmo `-32602`, byte a byte; a conversa nova sem `contextId` passa | o filtro aceitar `contextId` desconhecido, ou ter mensagem distinta para o alheio |
| o que o filtro autoriza é o que o SDK executa: `method` duplicado nos dois sentidos, caixa trocada, push fora de `configuration` | um parser próprio no filtro (ex.: `JsonDocument` lendo o primeiro `method`) |
| `CancelTask` da aplicação → `403`, e a task não muda | o `CancelTask` no mapa de operações |
| inbox: `GetTask` de task de aplicação e de task sem dono continuam (`rt-inbox`) | o filtro aplicado a todo subject |
| `application_id` preservado depois das transições do worker (`rt-crossapp`) | o `Update` da api ou do worker zerar a coluna |
| a filha da delegação nasce sem dono | a coluna herdada da mãe |
| boot: lista vazia de `application` derruba | a exceção esquecida |
| matriz de papéis com as seis rotas novas | contagem sem o Leitor, lista com o Operador |

### D7. O que o apply mudou (convenção 9, 10/11/2026)

Nada muda comportamento ou contrato decidido; tudo foi registrado no `tasks.md`:

1. **Premissa que caiu: a Q11 não tinha entrado na delta.** Ela estava aceita, mas os chips "Agentes liberados" do
   `GET /applications/` não estavam em nenhuma spec nem tarefa. No início do apply entraram o campo
   `agentes: [{ id, nome }]` na "Listar aplicações" (`external-applications`), com o cenário "Os
   agentes liberados na lista", e a tarefa 6.3b. Só acréscimo.
2. **O defeito do guarda 1.4.** O banco dos testes é migrado pela migration: tirar o `CHECK` só da
   configuração EF não muda o banco, e o guarda não teria o que pegar. O defeito usado é a linha do
   `CheckConstraint` fora da migration (o banco sem a restrição), e o guarda reprovou com ele.
3. **A 5.2 foi dividida.** O `rt-crossapp` não sobe host HTTP do `apps/api`. A parte do acordo (a
   task criada pelo store real do `apps/workers` nasce sem dono) ficou lá, e a parte da rota ficou no
   `apps/api`, com a mesma linha sem dono no agente da filha.
4. **O guarda da matriz de papéis** ganhou a substituição de `{agentId:guid}` no `SendAsync`, como a
   #186 fez com `{secretId:guid}`. É só arranjo, sem asserção nova nem afrouxada.
5. **O subject `application` sem id legível é aplicação, não chamador sem filtro**
   (`ApplicationCaller`): o filtro lhe dá o `404` do agente sem vínculo. O emissor sempre põe o
   `buteco_app`, então o caso não acontece, mas ler "sem id, logo não é aplicação" abriria a rota.
6. **O nome que não é método A2A** (inclusive com a caixa trocada) falha no próprio parse do SDK
   com `-32601`; o filtro, que usa o mesmo parse, o trata como método fora das operações (`403`),
   como a spec diz. Medido na 0.2 e pelo caso `gettask`.
7. **A 1ª rodada da `api` (10.2) foi reprovada pelo `ProductionServiceRouteLists_MatchTheBuiltHostEndpoints`
   (7 → 8), corrigida.** O caso soma as rotas das listas sem papel, e a A2A de `application` é a
   8ª; ele não estava na lista do topo e entrou nela, com a asserção exata (decisão do dono,
   10/11/2026). A 2ª rodada foi aprovada.
8. **O índice de `a2a_tasks.application_id`** é criado pela migration sem `CONCURRENTLY` e trava as
   escritas na tabela enquanto é construído; a nota está no `docs/deployment.md`. A coluna anulável
   sem default não reescreve a tabela.

## Risks / Trade-offs

- **O dono por valor assíncrono local** depende de a inserção da task acontecer no fluxo da chamada
  do `SendMessage` (é o caso no `A2AServer` desta versão; o guarda da D6 reprova se mudar).
- **Ler o corpo duas vezes** custa uma desserialização a mais por chamada de aplicação; o inbox não
  paga.
- **Retirar a operação push não corta a entrega das tasks já criadas** (D1).

## (f) Os quadros do protótipo

*A lista abaixo é a da versão `1794270084-ee7d`, como foi apresentada. O dono corrigiu os quadros na
versão 63 (`1794271196-4a59`); ver "A versão 63", logo depois da lista.*

Canvas `claude.ai/artifact/H7dPBZafiSkdKd1hqesPZ6`, versão `1794270084-ee7d`, lido em 10/11/2026 pela
ferramenta Artifact. **serve** = esta change entrega o dado; **#188** = estado só de tela, com o
contrato nas specs daqui; **não cumpre** = o quadro promete o que o backend não faz.

**`AplicacaoDetalhe`, aba Agentes (`estadoAgentes=lista`, `recemCriada`)**

| estado | o que o quadro mostra | #187 |
|---|---|---|
| lista | aba "Agentes" com contador | serve (`GET /applications/{id}/agents`, tamanho da lista) |
| lista | colunas Agente (nome, descrição), **Enviar mensagem, Consultar task, Cancelar task, Notificação push**, Ações | serve três; **não cumpre "Cancelar task"** (D1, Q1): tirar a coluna |
| lista | ✓ / — por operação | serve (`operacoes`) |
| lista | nome do agente com link para o AgenteDetalhe | serve (`agenteId`) |
| lista | agente inativo na lista | **sem quadro**: o item traz `ativo`; o selo é da #188 |
| lista | "Agentes alcançados por delegação não precisam ser liberados." | serve (D4) |
| `recemCriada` | "Nenhum agente liberado. Enquanto a lista estiver vazia, toda chamada A2A desta aplicação é recusada." | serve; a recusa é `404` (D2) |
| lista | Remover | serve (`DELETE`); a confirmação é **#188** |
| — | aplicação desativada na aba Agentes | **sem quadro**: o vínculo fica e a chamada dá `404` (D2.4) |
| script | aba "Atividade" (`stTasks` 182, `stRecusas` 7, lista de recusas com `pushNotificationConfig`/`tasks/get`) | **não cumpre**, e não aparece no quadro (sem marcação): nenhuma fonte de tasks por aplicação nem de recusas do filtro; os nomes `tasks/get` e `pushNotificationConfig` não são métodos daqui. Remover do script |

**`AplicacaoDetalheAdicionar` (`estadoAgentes=adicionando`)**

| estado | o que o quadro mostra | #187 |
|---|---|---|
| adicionando | seletor "Selecione um agente…" com os agentes ainda não liberados, "(inativo)" no nome | serve (`GET /agents/` menos `GET /applications/{id}/agents`) |
| adicionando | descrição do agente escolhido | serve |
| adicionando | "O que a aplicação pode fazer com este agente", quatro opções com descrição; marcadas por padrão Enviar mensagem e Consultar task | serve três; **"Cancelar task — Interrompe uma execução" não cumpre** (D1); o padrão é de tela |
| adicionando | "Este agente está inativo. Ele fica liberado, mas a aplicação recebe "agente não encontrado" até ele ser ativado." | metade: fica liberado, sim; **não cumpre "agente não encontrado"**: a task termina rejeitada (D2.4). Texto proposto: "Este agente está inativo. Ele fica liberado, mas as mensagens da aplicação são recusadas até ele ser ativado." |
| adicionando | "Marque ao menos uma operação." | serve (`400` `operacoes`) |
| — | agente já liberado (`409`), agente apagado entre abrir e salvar (`400` `agenteId`) | **#188** |

**`AplicacaoDetalheEditar` (`estadoAgentes=editando`)**

| estado | o que o quadro mostra | #187 |
|---|---|---|
| editando | caixas por operação na linha, Salvar/Cancelar, "Adicionar agente" desabilitado | serve (`PUT`) |
| editando | "Marque ao menos uma operação" e Salvar desabilitado | serve (`400`) |
| editando | as quatro colunas | **Cancelar task não cumpre** (D1) |
| — | vínculo retirado por outro Admin entre abrir e salvar (`404`) | **#188** |

**`AgenteDetalhe`, card "Aplicações com acesso"**

| estado | o que o quadro mostra | #187 |
|---|---|---|
| Admin | lista: nome (link), selo "Desativada", operações por extenso, último uso ("há 3 min", "Nunca usada") | serve (`GET /agents/{id}/applications`); o último uso é **o da aplicação**, não o deste agente (Q13) |
| Admin | "Gerenciar" → lista de aplicações | tela só |
| todos | **o quadro não tem a versão de Operador/Leitor** (só a contagem) | **sem quadro**: serve `GET …/applications/count`; desenhar na #188 |
| Admin | "Desativar este agente interrompe as chamadas de **2 aplicações ativas**." | serve (`ativas`) |
| Admin | "Elas passam a receber "agente não encontrado"." | **não cumpre** (D2.4): texto proposto "As mensagens delas passam a ser recusadas." |
| Admin | operações "Enviar mensagem · Consultar task · Notificação push" | serve |
| — | agente sem nenhuma aplicação | **sem quadro** (#188) |
| card "Endereços A2A" | Execução, AgentCard, "OAuth2 · emissor do Buteco" | já servido (#186) |

**Fora da lista pedida, mas prometido à #187 na (h) da #186:** a coluna "Agentes liberados" com chips
do `AplicacoesLista` e o rodapé "as desativadas continuam listadas porque são autoras de tasks e
métricas". A autoria de task passa a ser verdade (D3); de métricas, não (g). Os chips: Q11.

### A versão 63 (`1794271196-4a59`, relida em 10/11/2026)

Conferido nos arquivos, não só no registro do dono:

- **`AplicacaoDetalhe`, `AplicacaoDetalheAdicionar`, `AplicacaoDetalheEditar`:** três colunas e três
  opções (Enviar mensagem, Consultar task, Notificação push), nenhuma ocorrência de "Cancelar task".
  O agente inativo: "Este agente está inativo. Ele fica liberado, mas as tasks que a aplicação abrir
  nele terminam recusadas até ele ser ativado." — cumpre (D2.4).
- **`AgenteDetalhe`, com a prop `papel`:** o Admin vê a lista (nome com link, selo "Desativada",
  operações, "aplicação usada há …"/"aplicação nunca usada") e "Gerenciar" — `GET /agents/{id}/
  applications` com o `ultimoUso` da aplicação (Q13); Operador e Leitor veem só "Usado por **2
  aplicações externas ativas** (3 no total).", sem nomes nem link — `GET …/applications/count` com
  `{ total, ativas }` (Q9). O rodapé: "Desativar este agente afeta **2 aplicações ativas**: as tasks
  que elas abrirem nele passam a terminar recusadas." — cumpre (`ativas`, D2.4).
- **A aba Atividade e as recusas** continuam só no script de `AplicacaoDetalhe`: ficam fora, são da
  #199 (aguardando gatilho).
- **Sem quadro, para a #188, com o contrato nas specs desta change:** o agente inativo na lista
  (`ativo` no item), a aplicação desativada na aba Agentes (o vínculo fica; a A2A dá `404`), o `409`
  de agente já liberado, o `404` de vínculo retirado entre abrir e salvar, o agente sem nenhuma
  aplicação (`{ total: 0, ativas: 0 }` e lista vazia).

## (g) Métricas e Insights

As tasks de aplicação entram nas métricas de hoje **sem distinção**: o worker grava `Origin`
`External` (não é delegação), igual às do inbox; as recusas do `SendMessage` (agente inativo, sem
provedor) vão para `task_rejections` como hoje. Nada quebra com o dono novo: nenhuma consulta do
Insights lista as colunas de `a2a_tasks` com `*`, e as duas que a leem filtram por `agent_id`/`state`.
**Nada muda nas métricas.** O que não existe e os quadros sugerem: contagem por aplicação, e as recusas
do filtro (`404`/`403`/"não encontrada"), que acontecem antes de existir task e não são gravadas.

## (h) Tamanho (convenção 18)

Âncoras, só código: **#186** (medida, `71046f6..7095350`) produção +1.298 / −33 (20 arquivos), testes
+1.369 (11 arquivos), **51 casos novos**, docs +231; **#184** produção +943 / −77, testes +1.813 /
−28, duplos +265 / −39, 59 casos.

| item | linhas (prod.) |
|---|---|
| entidade do vínculo, coluna do dono, configuração EF | ~90 |
| operações e mapa método → operação | ~50 |
| filtro da rota (corpo pelo tipo do SDK, vínculo + ativa, 404/403) | ~150 |
| dono no store e filtro por dono/agente (GetTask, continuação, contexto) | ~140 |
| 6 rotas, serviço, requests/responses | ~360 (6 × ~60) |
| `ServiceRoutes`, `RoleRoutes`, boot sem a exceção, `Program.cs` | ~40 |
| **produção** | **~830** (faixa 700–1.000), ~35 % comentário |

Testes: **~44 casos** (filtro 10, dono 9, rotas 14, card 3, boot 2, matriz 1 alterada, #186 2
alterados, rt-inbox 1, rt-crossapp 1, delegação 1), ~35 linhas por caso → **~1.550**; **duplos ~120**
(semeador de vínculo; o cliente de token de aplicação já existe da #186). Docs ~+200. Migration fora.
**~2.700 no total: cabe numa change**, um pouco menor que a #186. Se o dono preferir dividir, sem nada
meio-aberto: **187a** — filtro, dono e vínculo (semeado nos testes), com a rota aberta; **187b** — as
seis rotas. A 187a sozinha em produção não libera nada (nenhum vínculo é criável), e a aplicação passa
de `403` a `404`. Recomendo não dividir (Q12).

## (i) Notas de deploy

- **Migration:** `CREATE TABLE external_application_agents` (PK `(application_id, agent_id)`, FK para
  `external_applications` `RESTRICT` e para `agents` `CASCADE`, `operacoes text[]` com
  `CHECK (cardinality(operacoes) > 0)`, `liberado_em`, `alterado_em`) e `ALTER TABLE a2a_tasks ADD
  COLUMN application_id uuid NULL` + FK + índice. Coluna anulável sem default: só metadado no
  Postgres, sem reescrever a tabela. Ordem de sempre (`docs/deployment.md` §2).
- **Imagem antiga com o schema novo:** o `apps/api` da #186 e o `apps/workers` ignoram a coluna (o EF
  só escreve o que mapeia), e as inserções deles gravam nulo.
- **Volta atrás para a imagem da #186: confirmado** — em `5e23548`, `ServiceRoutes[application]` é `[]`
  (`ServiceScopeAuthorizationHandler.cs` l. 85) e o handler só dá `Succeed` para rota da lista
  (l. 111-117): o token de aplicação volta a `403` em toda rota, inclusive a A2A. Vínculos e donos
  ficam no banco, sem uso, e voltam a valer num novo deploy da #187. Não precisa reverter a migration.
- **#187 antes da #188: pode.** Sem as telas, o vínculo só se cria chamando a rota com o token do
  painel de um Admin; a aplicação nasce sem agentes, então em produção ela só troca o `403` por `404`
  na A2A. Nada muda para o inbox nem para o painel.

## (j) Premissas falsas (convenção 9)

1. **Corpo da #187, l. 7:** `message/send`, `tasks/get`, `tasks/cancel` — nomes do A2A 0.3. O SDK
   `1.0.0-preview2` atende `SendMessage`, `GetTask`, `CancelTask` (`A2AJsonRpcProcessor`, o `switch`
   do despacho), como a #186 constatou. Só nome: **não é parada.**
2. **Corpo, l. 7 e l. 9: `tasks/cancel` como operação liberável, e "filtro em get/cancel/push".** O
   cancelamento não existe de verdade: o `CancelTask` grava `canceled` no store da api, e o worker o
   sobrescreve se a task já está `working` (Context). O `docs/a2a-integration.md` l. 59-62 diz que
   cancelamento "não faz parte do contrato suportado", e a tabela `TaskState` (l. 453) diz que `CANCELED` nunca
   é produzido — também falsa: o `service:inbox` consegue produzi-lo hoje. **Muda contrato** (a lista
   de operações da issue e a coluna dos quadros): **parada — Q1.**
3. **Corpo, l. 9: "push" como operação filtrada por task.** Não há push por task para filtrar: os
   quatro métodos de push config respondem sempre `-32003` (`A2AServer` l. 1409-1429), e o push só se
   configura no `SendMessage`. A forma da operação é a decisão em aberto da issue (Q3): **não é
   parada.**
4. **Corpo, l. 8: "Checagem no handler A2A".** O `404` e o `403` saem de um filtro na rota, antes do
   SDK (D2); só o "não encontrada" fica no handler. Lugar, não comportamento: **não é parada.**
5. **Comentário de 09/11, item 2:** nomeia só `ApplicationToken_OnEveryAuthenticatedRoute_
   ReturnsForbidden`. Há um segundo guarda da A2A, `ApplicationToken_OnTheA2ARoute_IsForbidden_
   AndPublishesNoJob` (`ExternalApplicationAuthorizationTests.cs` l. 61), que muda de `403` para `404`
   ("sem vínculo, nenhum job"), mantendo a asserção do job; e o comentário do acordo
   `ExternalApplicationRoundTripTests.cs` l. 22-23 ("vazia até a #187"), cuja asserção (`GET /agents/`
   = `403`) continua. Troca asserção sem reduzir cobertura: **não é parada**, registrada.
6. **`docs/a2a-integration.md` l. 555:** "Não há streaming nem push notification neste endpoint hoje" —
   falsa desde a `a2a-push-notifications`; o próprio documento descreve o push. Doc: corrigida aqui.
7. **`PostgresTaskStore.cs` (api) l. 57-63:** "`ListTasksAsync` não tem nenhum consumidor em
   `apps/api`" — o método é alcançável por JSON-RPC por qualquer chamador autorizado na rota (hoje o
   inbox). Para a aplicação ele fica fechado (D1); comentário corrigido aqui.
8. **Quadros `AgenteDetalhe` e `AplicacaoDetalheAdicionar`:** "agente não encontrado" para agente
   inativo — o `SendMessage` termina `rejected` (`EnqueueingAgentHandler.cs` l. 44-49). Para corrigir
   no quadro (f).

**Só a 2 muda contrato**, e foi parada. **Decidida pelo dono em 09/11/2026 (Q1):** "Cancelar task"
fica fora; a issue da #187 teve o corpo atualizado (a lista de operações, com a nota) e o comentário
da decisão; o cancelamento real é a #264. A 6 e a correção das l. 59-62 e 453 do guia entram nesta
change (`tasks.md`, seção 8); a 8 foi corrigida no quadro, na versão 63.

## Baseline

`5e23548`, árvore limpa (`../buteco-agentes-187-baseline`), `suite-187.sh`, uma suíte por vez com o
lock, `buteco-agents_postgres_1` e `buteco-agents_rabbitmq_1` no `podman ps` do início de cada uma.
Saída em `~/.cache/buteco-agents/baseline-187/` (relatório por nome: `nomes.txt`, 1.672 resultados).

| suíte | aprovados | ignorados | falhas | duração | CPU mínima | veredito |
|---|---|---|---|---|---|---|
| `api` | 1.640 | 1 (`IdentityTokenPruneQueryPlanTests.FourQueries_With150kTokens_StayUnder100Ms`, `Skip` de sempre) | 0 | 448 s | 51 | APROVADA |
| `rt-inbox` | 15 | 0 | 0 | 73 s | 90 | APROVADA |
| `rt-crossapp` | 4 | 0 | 0 | 21 s | 100 | APROVADA |
| `rt-connectors` | 12 | 0 | 0 | 38 s | 100 | APROVADA |

A CPU abaixo de 100 no `api` e no `rt-inbox` não invalida: verde conta (regra assimétrica).

## Decisões do dono (09/11/2026)

- **Q1 (a parada):** "Cancelar task" fica fora da #187. Três operações; o `CancelTask` da aplicação
  cai na regra do método fora das operações (`403`). Issue do cancelamento real: #264, no Backlog,
  sem milestone. A change corrige só a documentação do cancelamento (o guia, l. 59-62 e 453), sem
  mudar o comportamento. O corpo da #187 foi atualizado e comentado.
- **Protótipo, versão 63:** ver "A versão 63" na (f).
- **Q2 aceita**, com o guarda de que o que o filtro autoriza é o que o SDK executa (D2, D6).
- **Q5 com ajuste:** a resposta não revela que um `contextId` existe e é de outro dono. Um
  `contextId` desconhecido hoje vira conversa nova; para a aplicação, só é aceito o de uma task dela
  no mesmo agente, e qualquer outro recebe o mesmo `-32602` "Contexto não encontrado." (D3), com o
  guarda byte a byte.
- **O `404` que não revela o agente:** o guarda byte a byte entre agente inexistente, sem vínculo e
  aplicação desativada fica (D2.3).
- **Q3, Q4, Q6 a Q14:** aceitas como recomendadas.

## Decisões (as recomendações, aceitas)

| # | decisão | recomendação |
|---|---|---|
| Q1 | "Cancelar task" (j.2, era parada) | **decidida pelo dono:** fora; três operações; `CancelTask` → `403`; #264; só a doc muda |
| Q2 | formato do `404`/`403` | HTTP vazio, antes do JSON-RPC, por filtro que lê o corpo com o tipo do SDK |
| Q3 | push: configurar ou também entregar | a operação cobre configurar (no `SendMessage`); sem ela, `403` na chamada inteira; a entrega não é reconferida |
| Q4 | tasks sem dono (anteriores, inbox, filhas) | nenhuma aplicação as vê; o inbox vê todas, como hoje |
| Q5 | `contextId` usado por outro dono no mesmo agente | **com o ajuste do dono:** só o `contextId` de task da própria aplicação no mesmo agente passa; inexistente e alheio recebem o mesmo `-32602` "Contexto não encontrado." |
| Q6 | aplicação desativada com token no prazo | `404` na hora na A2A (lê `ativa` no banco) |
| Q7 | agente inativo com vínculo | como o inbox: task `rejected`; corrigir o texto dos quadros |
| Q8 | card "Aplicações com acesso" | duas rotas: `…/applications/count` (A O L) e `…/applications` (A) |
| Q9 | o que a contagem conta | `{ total, ativas }` |
| Q10 | senha para liberar/alterar/retirar | não pedir |
| Q11 | chips "Agentes liberados" no `AplicacoesLista` | servir `agentes: [{ id, nome }]` no `GET /applications/` |
| Q12 | tamanho | uma change |
| Q13 | "último uso" no card do agente | o da aplicação; por agente fica fora |
| Q14 | suítes | `api`, `rt-inbox`, `rt-crossapp`, `rt-connectors` |
