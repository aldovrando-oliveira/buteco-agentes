# Buteco Agentes — Histórico e Status

> Este arquivo muda a cada sessão de trabalho nova. Atualizar a seção
> "Status atual" e adicionar à lista de changes aplicadas conforme o
> trabalho avança. O arquivo de arquitetura/convenções (01) é separado e
> muda bem menos.

## Status atual

A linha de trabalho de histórico de conversa por sessão no inbox está
**concluída nas três etapas**: `auth-login-e-servico`,
`inbox-mensagens-persistidas` e `frontend-inbox-sessoes-historico`, mais
`inbox-enums-json-string` (correção de contrato descoberta durante a
etapa 3, aplicada antes dela). Todas aplicadas, sincronizadas e
arquivadas.

A linha de trabalho de contexto temporal e de canal está na **etapa 1 de
2, concluída e arquivada**: `apps-workers-contexto-temporal`. Etapa 2
(`inbox-contexto-canal-metadata`) ainda não proposta — ver "Changes
aplicadas" abaixo para o que entrou e "Próximo passo" para a sequência.

## Changes aplicadas, por linha de trabalho

### Fundação (backend + frontend básico)
`estrutura-base-monorepo` → `backend-agente-a2a-mvp` →
`apps-api-cqrs-mediator` → `apps-api-agent-update-status` →
`frontend-cadastro-agentes` → `frontend-editar-ativar-agente` →
`frontend-melhorias-visuais-agente`

CRUD de agente completo, protocolo A2A (`SendMessage`/`GetTask`), CQRS,
ativar/desativar, UI completa de cadastro.

### Multi-provider de LLM
`backend-multi-provedor-llm` → `frontend-selecao-provider-modelo`

Provider/Model por agente (OpenAI/Anthropic/Gemini), catálogo de
modelos, seleção em cascata na UI.

### Histórico de conversa
`apps-workers-historico-conversa` → `apps-workers-resumo-historico-conversa`

Sessão persistida por `contextId`, limite de histórico nativo, resumo
incremental via `CompactionProvider` quando a conversa cresce.

### Integração MCP (tools externas)
`backend-mcp-catalogo-vinculo` → `backend-mcp-selecao-tools` →
`apps-workers-execucao-mcp` → `frontend-mcp-servidores-catalogo` →
`frontend-agente-vinculo-mcp-tools`

Cadastro de servidor MCP, seleção granular de tools por agente, execução
real (tool call de verdade durante o processamento), UI completa.

### Delegação entre agentes
`backend-agente-delegacao-catalogo-vinculo` →
`apps-workers-delegacao-execucao` → `frontend-agente-delegacoes-secao`

Vínculo unidirecional Source→Target, execução real (um agente chama
outro como tool interna, mesmo banco, sem HTTP externo — decisão
consciente, diferente de um cliente A2A externo de verdade), controle de
profundidade, UI inline no detalhe do agente.

### Protocolo A2A — descoberta e notificação
`backend-agente-description-skills` → `backend-a2a-agent-card` →
`a2a-push-notifications`

`Description`/`Skills` no agente, `AgentCard` exposto por agente
(`.well-known/agent-card.json`), push notification (webhook) fechando o
Non-Goal que ficou pendente desde o MVP.

### Caixas de entrada (linha completa, do scaffold à UI)
`estrutura-base-apps-inbox` → `inbox-catalogo-canais` →
`inbox-crm-contato-sessao` → `inbox-orquestrador-debounce` →
`inbox-adapter-contrato-catalogo` → `inbox-adapter-waha` →
`inbox-adapter-telegram` → `inbox-fix-concorrencia-orquestrador` (correção
de bug, não capability nova) → `frontend-inbox-catalogo-canais` →
`inbox-push-notification-recepcao-resiliente` (correção de bug, não
capability nova)

Scaffold do quarto app → catálogo de canais → CRM (Contact/Session,
fronteira por inatividade) → orquestrador (debounce persistido,
idempotente entre instâncias, round-trip A2A completo) → contrato de
plugin (três obrigatórios + um opcional) → adapter WAHA → adapter
Telegram (com automação e verificação nativa que o WAHA não tem) → UI
completa do catálogo de canais.

**Bug de concorrência real, encontrado e corrigido**: `InboundMessageOrchestrator`
perdia mensagem silenciosamente sob concorrência real (aliasing de
change tracker do EF Core numa coleção owned/JSON após `ReloadAsync`) —
corrigido trocando `ReloadAsync` por detach+rebusca. Achado durante um
teste que só existiu porque foi pedido explicitamente numa revisão
anterior — o teste "óbvio" (o que o bug relatado original pedia) não
teria pego esse segundo bug, mais sutil.

**Dois bugs reais de produção no round-trip de push notification,
encontrados e corrigidos** (reportados via screenshot do inbox):
(1) `PushNotificationEndpoints.ExtractResponseText` lia
`task.Status.Message?.Parts`, mas `AgentExecutionService` (apps/workers)
nunca preenche esse campo no caminho de sucesso — o texto da resposta vai
para `task.Artifacts` (`AddArtifactAsync` + `CompleteAsync()` sem
mensagem final); a resposta nunca era extraída, então nunca era entregue
ao canal, embora a mensagem de entrada ainda virasse `Completed`. O
fixture de teste (`BuildAgentTask`) montava `Status.Message` diretamente,
por isso os testes existentes não pegaram o defeito. (2) `ReceiveAsync`
propagava o `CancellationToken` da própria requisição (ligado a
`HttpContext.RequestAborted`) até o `SaveChangesAsync` final;
`PushNotificationSender` (apps/workers) usa, por decisão deliberada
(`a2a-push-notifications`, Decision 3), um timeout fixo de 5s sem retry —
se o round-trip (chamar o canal + persistir) ultrapassasse esses 5s, o
cliente abortava a conexão, cancelando a persistência local mesmo depois
de a resposta já ter sido entregue com sucesso ao canal (ex. Telegram):
resposta chegava no Telegram, mensagem ficava presa em "Processando"
para sempre. Corrigido trocando para `CancellationToken.None` a partir da
localização do `PendingDispatch`.

### Autenticação
`auth-login-e-servico`

Primeira change de segurança do projeto, e etapa 1 de 3 da linha de
histórico de conversa no inbox. Motivada por antecipação, não por
incidente: a etapa 2 faz `apps/inbox` expor conteúdo real de conversa de
usuário final via API, o que muda o perfil de risco de categoria —
autenticação precisava vir antes, não depois.

O que entrou:

- **Login de operador único** em `apps/api` (`POST /auth/login`),
  credencial via variável de ambiente, senha com PBKDF2. Sem tabela de
  usuários, sem RBAC, sem cadastro, sem recuperação de senha, sem
  OAuth/SSO.
- **Token stateless assinado com HMAC**, sem biblioteca JWT e sem sessão
  em banco — `apps/api` e `apps/inbox` compartilham só a chave de
  assinatura, validando localmente sem chamada de rede entre os processos.
  TTL de 30 minutos por padrão, configurável, com o default no tipo de
  Options e não no `appsettings.json`.
- **Enforcement por padrão em toda rota** dos dois apps, com allowlist
  explícita de rotas anônimas em cinco categorias e **checagem de
  integridade bidirecional no startup**. Mesmo molde de
  `ValidateChannelAdapterRegistrations`.
- **Token de serviço** `apps/inbox` → `apps/api`, mesmo mecanismo de
  assinatura (`sub` distinto, assinado por requisição, TTL fixo de 5 min),
  escopado a exatamente duas rotas — qualquer outra responde `403`.
- **`AgentCard` declara `SecuritySchemes`/`SecurityRequirements`** (HTTP
  Bearer): descoberta continua pública, uso passa a exigir credencial.
- **Frontend**: tela de login, módulo fino de token (`sessionStorage`)
  importado por cada `request<T>` de feature — sem cliente HTTP
  compartilhado, convenção 7 preservada.

**Duas classes de problema pegas na revisão dos artefatos, antes do
apply**: (1) as chaves de configuração estavam grafadas de dois jeitos
diferentes entre `design.md` e `tasks.md`, com as propriedades dos Options
não batendo com as chaves documentadas — falharia no boot; (2) o risco
número 1 do próprio `design.md` (chave de assinatura divergente entre os
dois processos) não tinha cenário nem teste, e o teste que fechou isso
usa o token de fato emitido pela outra API contra um `apps/inbox` com
`apps/api` inalcançável pela rede — provando que a validação é local, em
vez de só afirmá-lo.

### Histórico de mensagens no inbox
`inbox-mensagens-persistidas`

Etapa 2 de 3. Entidade `Message` (`apps/inbox`), tabela relacional própria
ligada a `Session` — **não** estende `PendingDispatch` nem toca sua
coleção owned/JSON `Messages`, exatamente a restrição de desenho que a
proposta original exigia.

O que entrou:

- **Persistência de entrada e saída**: `Message` grava direção, conteúdo,
  tipo de conteúdo (`Text`/`Image`/`Audio`/`Document`, mídia binária fora
  de escopo — só um marcador), instante e, na entrada, o identificador
  externo da mensagem (dedup de webhook reentregue); na saída, status de
  entrega (`Sent`/`Failed`, com motivo) do envio ao provedor via
  `IOutboundMessageSender` — não recibo de entrega/leitura do destinatário
  final. Toda mensagem de saída grava `ContentType = Text`.
- **Estado de dispatch espelhado nas mensagens de entrada**
  (`Pending`/`Dispatching`/`Failed`/`Completed`), atualizado nos seis
  pontos reais que mutam ou removem `PendingDispatch`, sobrevivendo à
  remoção da `PendingDispatch` correspondente. `Failed` agrupa as três
  causas de "não haverá resposta" sob o mesmo valor, decisão consciente.
- **`Contact.DisplayName`** (nullable), atualizado a cada mensagem de
  entrada — semântica oposta à de `Metadata` (congelado na criação).
  Extraído de `payload._data.Info.PushName` no WAHA (confirmado só via
  discussão da comunidade para o engine GOWS — ver Itens em aberto) e de
  `username`/`first_name` no Telegram.
- **Duas consultas novas**: `GET /sessions/{id}/messages` (timeline
  cronológica) e `GET /channels/{id}/sessions` (sessões de um canal por
  última atividade, com `DisplayName`/`ExternalId` do contato e prévia da
  última mensagem) — a segunda não fazia parte do escopo original da
  proposta, entrou depois de uma revisão apontar que a etapa 3 precisa
  dela e ela não existia em lugar nenhum.
- **Pendência de varredura fechada**: o padrão de bug de
  `inbox-fix-concorrencia-orquestrador` (coleção owned/JSON mutada
  in-place + re-leitura na mesma instância de `DbContext`) foi varrido nos
  dois eixos (gatilhos de re-leitura × superfícies owned/JSON) contra o
  repo inteiro, com **classificação escrita de cada superfície, inclusive
  as seguras**. A varredura também estabeleceu que a distinção que importa
  é entre `OwnsMany().ToJson()` (snapshot estrutural por elemento) e
  `HasConversion` + `ValueComparer` (serializa o valor inteiro a cada
  `SaveChanges`) — interseção real é só `PendingDispatch.Messages`, já
  corrigido; nenhum segundo site. A seção *Risks* do `design.md` de
  `inbox-fix-concorrencia-orquestrador`, que ainda citava o mecanismo
  abandonado (`ReloadAsync`), foi corrigida para o mecanismo final
  (detach+rebusca).

**Achado real durante a implementação, fora do escopo desta change**: um
teste de concorrência novo (8 chamadas simultâneas com o mesmo
identificador externo de mensagem) expôs, de forma intermitente, uma
corrida em `ContactSessionResolver.FindOrCreateSessionAsync` —
diferente de `Contact` e `PendingDispatch`, a criação de `Session` não
tem índice único protegendo contra duplicação sob concorrência real
(gap de `inbox-crm-contato-sessao`). O teste foi isolado da criação de
`Session` em vez de expandir esta change para corrigir um componente
adjacente — registrado como item em aberto abaixo.

### Formato de fio dos enums de mensagem
`inbox-enums-json-string`

Change pequena, nascida de um achado da exploração da etapa 3: os quatro
enums expostos em `MessageResponse` (`MessageDirection`,
`MessageDeliveryStatus`, `MessageDispatchStatus`, `MessageContentType`)
trafegavam como **inteiro ordinal**, sem
`[JsonConverter(typeof(JsonStringEnumConverter<T>))]` — divergente do
padrão já estabelecido no repo (`McpServerAuthType`, em `apps/api` e
`apps/workers`).

Tratada como **defeito de `inbox-mensagens-persistidas`** (que expôs
`MessageResponse` sem nunca especificar formato de fio), não como
requisito novo da UI — corrigida na origem, em change própria e
sequenciada antes da etapa 3, em vez de contornada no consumidor com
mapeamento por índice.

Sem migração: `AppDbContext` já mapeava os quatro enums como coluna de
texto (`HasConversion<string>()`) — o gap era só na serialização HTTP.

O teste é o ponto da change: lê a resposta como **JSON bruto**
(`ReadAsStringAsync`/`JsonDocument`), não `ReadFromJsonAsync<MessageResponse>`,
porque round-trip pelo mesmo tipo C# passaria igual com ordinal e com
string. Cobre as duas rotas que expõem esses enums (`MessageDirection`
também sai em `GET /channels/{id}/sessions`, dentro da prévia) e usa uma
sessão com mensagem de entrada **e** de saída — sem as duas direções,
`DeliveryStatus` e `DispatchStatus` viriam nulos e metade das asserções
passaria vazia.

### Sessões e histórico na UI do inbox
`frontend-inbox-sessoes-historico`

Etapa 3 de 3, e fechamento da linha. `/channels/{id}` ganha abas
**Sessões** (padrão) e **Configuração** (o card anterior, intacto), com
`Editar`/`Desativar` acima das abas, no nível do canal.

O que entrou:

- **Lista de sessões** por última atividade, com nome de exibição do
  contato (fallback para `ExternalId` quando nulo), prévia da última
  mensagem e instante. Sessão sem prévia renderiza normalmente — é o
  estado de toda sessão anterior à etapa 2, que não teve backfill.
- **Timeline master-detail** com URL própria
  (`/channels/:id/sessions/:sessionId`), lista preservada ao trocar de
  sessão. Reload e link compartilhável funcionam.
- **Um check só, nunca dois**: mensagem de saída `Sent` renderiza um único
  indicador; `Failed`, ícone de erro com o motivo truncado e acessível. O
  teste afirma explicitamente a **ausência** de um segundo indicador — é o
  que impede a regressão de "parecer com o WhatsApp" no futuro, já que o
  sistema não coleta recibo de entrega nem de leitura.
- **Silêncio legível**: os quatro valores de `DispatchStatus` visíveis e
  distinguíveis na timeline — a motivação original de toda a linha.
- **Valor de enum desconhecido** tem branch explícito nos quatro enums,
  nunca tratado como sucesso silencioso. `Direction` desconhecido tem
  tratamento próprio: renderiza em forma neutra, sem escolher lado e sem
  nenhum dos dois blocos de status, porque cair em `Inbound` ou `Outbound`
  por default renderizaria o bloco de status errado.
- **Polling assimétrico e deliberado**: `refetchInterval` de 5s só na
  timeline aberta (acima do `SweepInterval` de 2s do backend); lista de
  sessões sem polling. O teste fixa as **duas** metades — que a timeline
  reconsulta e que a lista não.

### Contexto temporal e de canal
`apps-workers-contexto-temporal` (etapa 1 de 2 — etapa 2,
`inbox-contexto-canal-metadata`, ainda não proposta)

Um agente não sabia que dia era: nenhum carimbo de tempo chegava à
chamada do LLM. Etapa 1 entrega o mecanismo de montagem e o instante de
processamento, o único espaço que `apps/workers` já tinha disponível —
resolver "amanhã" corretamente sob represamento de fila só fecha na
etapa 2, que vai trazer o instante real da mensagem via metadata A2A.

O que entrou:

- **Bloco de contexto temporal** concatenado às `Instructions` do agente
  a cada execução — instante de processamento em ISO 8601 com offset,
  dia da semana por extenso (sempre pt-BR, calculado em código, nunca
  delegado ao modelo) e regra de precedência em linguagem natural para
  expressões de tempo relativas. Nunca toca `Agent.Instructions` no
  banco, nunca entra no histórico de conversa — montado em memória a
  cada chamada, no único ponto de montagem das instruções.
- Espaço já reservado para o instante da mensagem original (sempre
  ausente nesta etapa) — quando presente, a regra de precedência passa a
  resolver contra ele em vez do instante de processamento, com uma linha
  de defasagem acima de um limiar mínimo.
- **`TimeProvider` como único ponto de acesso a relógio/fuso** em todo
  `apps/workers` — produção usa o relógio do sistema, testes usam um
  relógio controlável. Os dois últimos pontos que ainda liam a hora
  direto do sistema operacional foram fechados.
- **Checagem de integridade no startup, nova**: o processo falha ao
  subir se o fuso resolvido pelo sistema operacional não corresponder
  exatamente ao valor declarado em `TZ` — cobre variável ausente, vazia
  ou com valor que o SO não reconhece (hoje cai em UTC sem aviso). Mesma
  família de `ValidateChannelAdapterRegistrations`/
  `ValidateRouteAuthenticationClassification`, terceiro caso do padrão.

**Duas rodadas de revisão nos artefatos antes do apply**: mecanismo de
teste da checagem de boot verificado por decompilação (`ilspycmd`) e
teste empírico real em container Linux e nativamente em macOS — não
decisão às cegas; achado do prefixo POSIX `:` quebrando a comparação
estrita de fuso, também verificado, não hipotético.

**Achado real na validação, corrigido com a causa registrada (convenção
9)**: a varredura de código por acesso direto a relógio/fuso (pedida em
revisão para verificar, não só afirmar, o isolamento dos testes de boot)
cobriu só `apps/workers` na primeira passada — duas citações posteriores
no `design.md`, ao resumir esse achado, generalizaram para "varredura de
código do repo", escopo que ela nunca teve. A prova não veio de reler o
texto: veio de uma regressão real. A **validação estendida ao monorepo
inteiro** (636 testes, via `podman`/`DOCKER_HOST`, além do que a suíte
própria de `apps/workers` exigia) achou `TimeProvider` faltando em
`tests/InboxOrchestratorRoundTrip.Tests` — um projeto de teste cruzado
entre apps, na raiz do repo, que monta seu próprio host de
`apps/workers` e tinha ficado fora da varredura original. Corrigido na
hora; a varredura foi então estendida a `tests/` na raiz (resultado:
vazio) e as citações de escopo errado, corrigidas nos artefatos.

**Baseline nomeada das falhas pré-existentes (pós-archive, evidência por
`git worktree`, não por contagem nem por inspeção)** — motivo de existir
esta seção: falhas registradas só por contagem (ex. "154/155") não são
checáveis contra uma regressão futura; duas suítes com a mesma contagem
podem estar falhando em testes completamente diferentes. Método: `git
worktree` no commit imediatamente anterior ao apply desta change
(`9fd8a87`) rodando a suíte completa do monorepo, nome de teste por
nome de teste, comparado contra o mesmo conjunto de suítes em HEAD
(`224fd6c`) rodado do jeito normal. Regra: falha nomeada nos dois lados
= pré-existente confirmado; falha só em HEAD = regressão (nenhuma
encontrada); falha só na base = passou a passar (duas ocorrências,
ambas explicadas abaixo, não celebradas sem entender a causa).

- **`apps/api` (`Buteco.Api.Tests`)** — 154/155 nos dois lados, a mesma
  falha nomeada
  (`AgentDeactivationTests.SendMessage_WithPushNotificationConfig_ForInactiveAgent_NeverPublishesJobOrCallsWebhook`).
  Pré-existente confirmado por nome, não por contagem.
- **`apps/inbox` (`Buteco.Inbox.Tests`)** — não-determinístico entre
  execuções: nesta coleta, a base teve 8 falhas nomeadas
  (`WebhookEndpointsTests` × 3, `MessagePersistenceTests` × 5, todas
  `ObjectDisposedException` sobre `IServiceProvider` — corrida de
  disposal do `WebApplicationFactory` entre classes de teste rodando em
  paralelo) e HEAD zerou (155/155). **Correção de uma hipótese anterior
  registrada nesta seção**: nenhuma das duas rodadas reproduziu a
  corrida específica de
  `ContactSessionResolver.FindOrCreateSessionAsync` (item em aberto
  citado abaixo) — o mecanismo observado desta vez é outro (disposal
  race do fixture, não a falta de índice único na criação de
  `Session`). Não é o bug conhecido reproduzindo; é uma segunda fonte de
  flake de isolamento, distinta, na mesma suíte.
- **`apps/workers` (`Buteco.Workers.Tests`)** — base: 30/75 falhas,
  quase todas com a mesma mensagem de erro exata (`Unable to resolve
  service for type 'Buteco.Workers.Notifications.PushNotificationSender'`),
  o gap de DI pré-existente que esta change fechou; uma falha à parte
  (`PushNotificationEndToEndTests.WebhookRespondsWith404_TaskStillCompletesNormally`,
  `Assert.Single() Failure: The collection contained 2 items`) tem causa
  diferente (estado mutável compartilhado no duplo de teste do
  webhook). HEAD: 92/92 limpo — confirma que o apply fechou exatamente
  essas 30, sem sobra.
- **`libs/ProviderCatalog.Tests`** — 6/6 limpo nos dois lados.
- **`tests/CrossAppTaskStoreCompatibility.Tests`** — 0/2 nos dois
  lados, os mesmos dois testes nomeados, mesma mensagem de erro exata
  (`"..."` esperado vs. `\"..."` obtido). Diagnóstico coletado nesta
  baseline (não correção, fora do Non-Goal desta change): divergência
  real de serialização, hipótese inicial de que fosse
  `JsonSerializerOptions` independentes entre `apps/api` e
  `apps/workers`. **Corrigido e a hipótese refinada por
  `crossapp-session-codec-encoder` — ver a subseção logo abaixo**: não é
  divergência entre os dois apps (os dois usam a mesma
  `A2AJsonUtilities.DefaultOptions`, mesma versão do pacote `A2A`); é um
  único método, `ConversationSessionCodec.Encode`, que não usava essas
  opções. Nenhum dado em risco existiu (as duas formas de escape
  decodificam para o mesmo valor); o teste falhava porque compara texto
  bruto, deliberadamente (é o ponto da asserção).
- **`tests/InboxOrchestratorRoundTrip.Tests`** — 1/2 nos dois lados, o
  mesmo teste nomeado
  (`RoundTripTests.MessageReceived_TriggersFullRoundTrip_PushNotificationReceivedWithCorrectPayload`),
  `TimeoutException` a ~21s contra um limite interno de 20s nos dois.
  Reproduz também no commit base, onde `AgentExecutionService`/
  `TaskJobConsumer` ainda nem exigiam `TimeProvider` — descarta código
  desta change como causa. Consistente com latência do `podman`, mas
  não 100% provado (faltaria uma comparação nativa com Docker, não
  disponível neste ambiente).
- **`apps/frontend`** — 15 falhas nesta rodada (não comparado contra a
  base — `apps/frontend` não foi tocado por esta change), família
  `AgentForm`/`ChannelForm`/`McpServerForm` e as páginas de criação/edição
  que os usam — mesmo perfil de timing de `userEvent` já registrado. A
  contagem mudou de 14 (observada duas sessões atrás) para 15 nesta
  rodada — variação em si é evidência adicional de nondeterminismo, não
  uma correção real de estado nem um teste novo quebrando.

Conclusão: nenhuma falha exclusiva de HEAD em nenhuma das sete suítes —
nenhuma regressão desta change, confirmado por nome, não por contagem
nem por inspeção.

### Correção de encoder cross-app
`crossapp-session-codec-encoder` — restauração do primeiro item da
dívida de baseline registrada acima, sequenciada antes de
`inbox-contexto-canal-metadata` (etapa 2) pelo motivo já explicado em
"Próximo passo".

`tests/CrossAppTaskStoreCompatibility.Tests` estava 0/2 desde
2026-08-01. Diagnóstico fechado por execução real contra Postgres
(`podman`/`DOCKER_HOST`), `git bisect` em worktrees isolados e
decompilação de `A2A.dll` — não por leitura de código sozinha (convenção
6):

- **O que falhava**: só a asserção de `Metadata["conversationSession"]`
  em `AssertTasksMatch` (comparação de texto bruto,
  `GetRawText()`); as outras seis asserções da mesma função (`Id`,
  `ContextId`, `Status.State`, contagens e conteúdo de
  `History`/`Artifacts`) sempre passaram.
- **Causa raiz**: `ConversationSessionCodec.Encode`
  (`apps/workers/src/Buteco.Workers/Agents/ConversationSessionCodec.cs`)
  serializava sem `A2AJsonUtilities.DefaultOptions` — cujo `Encoder` é
  `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`, confirmado por
  decompilação — enquanto o resto do pipeline A2A (os dois
  `PostgresTaskStore`, o `PushNotificationSender`) usa essas opções
  consistentemente. Não é divergência entre `apps/api` e
  `apps/workers`: os dois usam a mesma `A2AJsonUtilities.DefaultOptions`,
  mesma versão pinada do pacote `A2A` (`1.0.0-preview2`,
  `Directory.Packages.props`). A comparação falharia mesmo com um único
  app conversando consigo mesmo.
- **Desde quando**: bisect real em worktrees — `7a56376` (antes de
  `apps-workers-historico-conversa` introduzir o cenário) passa 2/2;
  `1470b72` (mesma change, "propaga histórico de conversa",
  2026-08-01), já falha 0/2 — nasceu quebrada no mesmo commit que
  introduziu a asserção. Janela de 24 dias e 11 commits (`1470b72` até
  `f668be5`) tocando a superfície `A2A`/`Infrastructure` dos dois apps
  (MCP, delegação, AgentCard, push notification, auth, contexto
  temporal) sem que isso desse qualquer sinal — teste vermelho não
  distingue asserção nova de regressão real; as outras seis asserções da
  mesma função continuaram cobrindo o acordo de schema o tempo todo.
- **Nenhum dado em risco**: confirmado experimentalmente contra o commit
  pré-correção (`f668be5`) que o valor já gravado em `a2a_tasks` já
  saía no formato relaxado ao ser relido — o bug estava isolado ao valor
  em memória que `Encode` retornava antes de qualquer persistência
  (reescrito pelo `Serialize(task, A2AJsonUtilities.DefaultOptions)` de
  qualquer `PostgresTaskStore` antes de tocar o banco). Nenhuma migração
  de dados foi necessária.
- **Varredura completa** de todo site `JsonSerializer.Serialize`/
  `Deserialize`/`SerializeToElement` sobre payload A2A em `apps/api`,
  `apps/workers` (produção e testes) e `tests/` na raiz — achou um
  segundo site com o mesmo defeito de forma,
  `PushNotificationConfigCodec.Encode`, mais sério e fora de escopo
  desta change; ver "Itens em aberto" abaixo.

`tests/CrossAppTaskStoreCompatibility.Tests` sai da lista de falhas
pré-existentes: 2/2 verde, confirmado após a correção.

## Itens em aberto, registrados conscientemente (não esquecidos)

Cada um tem gatilho de quando revisitar:

- **Revogação antecipada de token** — token stateless não pode ser
  invalidado antes do TTL. Nem troca de senha do operador nem reinício do
  processo derrubam token já emitido. Gatilho: surgir necessidade real de
  encerrar sessão à força (operador removido, credencial vazada) ou mais
  de um operador.
- **Operador único, sem RBAC** — escopo mínimo consciente. Gatilho:
  segunda pessoa precisar de acesso, ou necessidade de distinguir
  permissões.
- **Duplicação de `ITokenService`** entre `apps/api` e `apps/inbox` —
  deliberada (mesma justificativa do `request<T>` duplicado no frontend).
  Gatilho: um terceiro consumidor aparecer.
- **Verificação de autenticidade do webhook do WAHA** — nomeada como
  mitigação futura (`inbox-adapter-waha`, Decision 6), nunca
  implementada. Telegram já resolve isso (via `secret_token`); WAHA não.
  Depois de `auth-login-e-servico` o risco está classificado
  explicitamente em código (`ExternalUnauthenticated` na allowlist), mas
  segue não mitigado.
- **Provisionamento automático da sessão WAHA** — ainda manual (decisão
  consciente, ao contrário do Telegram — ambiguidade real de escopo de
  token no WAHA que não existe no Telegram).
- **Assimetria de `IsActive`** — Telegram bloqueia webhook de entrada de
  canal desativado; a saída (`PushNotificationEndpoints`, qualquer canal)
  não verifica isso ainda.
- **Lock distribuído (Redis/Valkey)** — ainda `pg_advisory_lock`/`xmin`.
  Gatilho: `apps/workers`/`apps/inbox` escalarem pra múltiplas réplicas
  de verdade em produção.
- **Encerramento explícito de sessão** (CRM) — só inatividade automática
  hoje; decisão consciente de escopo mínimo, não esquecimento.
- **Estado da sessão (aberta/encerrada) não é exposto pela API** — a
  fronteira por inatividade é calculada com um timeout que vive só na
  configuração do servidor, então a UI não tem como derivá-lo.
  `GET /channels/{id}/sessions` devolve `LastActivityAt`, mas não o
  estado. Gatilho: a lista de sessões precisar distinguir conversa viva de
  conversa encerrada.
- **Rate limit do Telegram** — sem tratamento; debounce reduz o risco mas
  não elimina.
- **ChatWoot** foi mencionado como alternativa possível na concepção
  inicial do projeto, nunca formalmente descartado — mas a direção
  tomada desde então (canais próprios em `apps/inbox`, arquitetura de
  plugin) é, na prática, a decisão de não usá-lo.
- **Retenção de mensagens** (`Message`, `inbox-mensagens-persistidas`) —
  nenhum TTL ou expurgo implementado; o campo de instante já deixa isso
  trivial no futuro. Gatilho: revisitar quando o primeiro canal em
  produção passar de dezenas de milhares de mensagens, ou quando o backup
  do `buteco_inbox` incomodar.
- **Paginação de `GET /channels/{id}/sessions` e `GET /sessions/{id}/messages`**
  (`frontend-inbox-sessoes-historico`) — nenhuma das duas rotas pagina;
  ambos os handlers fazem `ToListAsync()` direto, sem `Skip`/`Take`, e a
  UI de sessões/timeline carrega a resposta inteira de uma vez. Gatilho:
  sinal real de volume (sessão ou canal com histórico muito longo
  tornando a tela perceptivelmente lenta).
- **Criação de `Session` sem índice único protegendo contra concorrência**
  (`ContactSessionResolver.FindOrCreateSessionAsync`, gap de
  `inbox-crm-contato-sessao`, exposto por um teste novo de
  `inbox-mensagens-persistidas`) — diferente de `Contact` e
  `PendingDispatch`, nada impede duas chamadas verdadeiramente
  concorrentes para o mesmo `Contact` novo criarem duas `Session`
  distintas dentro da janela de inatividade. Gatilho: revisitar se
  aparecer duplicação real de `Session` em produção, ou antes de qualquer
  mudança futura em `ContactSessionResolver`. Nota da baseline nomeada
  pós-archive de `apps-workers-contexto-temporal`: o flake de
  `apps/inbox` observado naquela coleta **não** reproduziu esta corrida
  específica — foi uma corrida diferente (disposal do
  `WebApplicationFactory`). O item aqui continua em aberto por si só,
  sem relação de causa confirmada com aquele flake.
- **`WahaWebhookMessagePayload.Data.Info.PushName`** (`DisplayName` do
  WAHA, `inbox-mensagens-persistidas`) — confirmado só via discussão da
  comunidade para o engine GOWS, não pela documentação oficial do WAHA
  (que declara o shape de `_data` como "interno do engine, pode variar").
  Gatilho: confirmar contra uma instância WAHA real de produção assim que
  houver uma disponível; se o campo divergir, `DisplayName` do WAHA
  simplesmente fica sempre nulo até a correção, sem quebrar nada mais.
- **Instante da mensagem não atravessa a delegação entre agentes**
  (`apps-workers-contexto-temporal`, Non-Goal explícito) — quando a
  etapa 2 (`inbox-contexto-canal-metadata`) passar a preencher esse
  instante, ele não chega ao agente Target sem trabalho adicional:
  Source e Target resolveriam "amanhã" contra dias diferentes, a falha
  que a linha inteira existe para evitar. Custo já dimensionado, para
  não ser redescoberto: `TaskJobMessage` é um `record` posicional
  fechado (`TaskId`, `AgentId`, `ContextId`, `PushNotificationConfig?`),
  sem bag extensível — propagar exige um campo novo (seguro, por ser
  JSON nomeado, com precedente de campo opcional) e tocar dois pontos em
  `AgentDelegationToolSetResolver.DelegateToTargetAsync`/
  `CreateDelegatedTaskAsync`. Gatilho: proposta da etapa 2.
- **Resíduo da Decisão B de `apps-workers-contexto-temporal`** — leitura
  de fuso local por código de framework (provider de logging, Npgsql/EF
  Core, o próprio host) continua fora do alcance de qualquer varredura
  de código deste repo (framework não é código do repo), e a classe de
  teste de boot de `TZ` roda em paralelo com o resto da suíte de
  `apps/workers` sem isolamento de coleção do xUnit — custo de isolar
  considerado maior que o benefício (amarraria os testes de boot ao
  ciclo do `WorkerInfrastructureFixture`/Testcontainers). Gatilho: flake
  intermitente na suíte de integração de `apps/workers` ou nos projetos
  de teste em `tests/` na raiz → olhar os Achados 9/10 e a Decisão B do
  design.md daquela change primeiro, antes de investigar do zero.
- **Varreduras de "acesso direto a relógio/fuso" (ou de padrão
  semelhante) precisam incluir `tests/` na raiz do repo**, não só o app
  modificado — `apps-workers-contexto-temporal` varreu só `apps/workers`
  na primeira passada, e uma regressão real em
  `tests/InboxOrchestratorRoundTrip.Tests` (projeto que monta host de
  `apps/workers` de fora dele) só apareceu na validação estendida do
  apply, não na varredura original. Ver Achado 10 do `design.md` daquela
  change, arquivada.
- **Testes do frontend falhando de forma pré-existente e não-determinística**
  (`AgentForm`/`ChannelForm`/`McpServerForm.test.tsx` e as páginas de
  criação/edição que os usam — timing de `userEvent` embaralhando texto
  digitado ou perdendo timeout numa navegação), achados ao rodar a
  suíte completa do monorepo durante o apply de
  `apps-workers-contexto-temporal`. A contagem variou entre rodadas (14
  numa sessão, 15 na baseline nomeada pós-archive) — não é um número
  fixo, é a assinatura da própria não-determinismo. Sem relação com
  aquela change — `apps/frontend` não foi tocado. Gatilho: qualquer
  trabalho futuro em `apps/frontend`, ou se continuarem vermelhos e
  atrapalharem CI.
- **Timeout de `InboxOrchestratorRoundTrip.Tests
  .MessageReceived_TriggersFullRoundTrip_...` contra `podman`** — falha
  por `TimeoutException` a ~22s contra um limite interno de 20s,
  repetível nas duas tentativas, quando Testcontainers roda contra o
  socket do `podman machine` em vez de Docker nativo (achado durante o
  apply de `apps-workers-contexto-temporal`). Leitura mais provável:
  overhead de rede da VM do podman frente a Docker nativo, para o qual o
  timeout foi calibrado — não regressão de código nesta change. Reforçado
  pela baseline nomeada pós-archive: o mesmo timeout reproduz também no
  commit imediatamente anterior ao apply, onde `AgentExecutionService`/
  `TaskJobConsumer` nem exigiam `TimeProvider` ainda — não prova que é
  especificamente latência do podman (faltaria comparação nativa com
  Docker, indisponível neste ambiente), mas descarta código desta change
  como causa. Gatilho: rodar essa suíte com Docker real disponível para
  confirmar se passa normalmente; se sim, considerar ampliar o timeout
  para tolerar ambientes mais lentos, sem perder o propósito do teste
  (round-trip completo dentro de um tempo razoável). O ajuste não é
  agora — este item é diagnóstico, não uma correção pendente.
- **`PushNotificationConfigCodec.Encode` fora do contrato de
  serialização A2A, com exposição externa confirmada** (achado durante
  a varredura de `crossapp-session-codec-encoder`, mesma classe de bug
  do `ConversationSessionCodec` corrigido naquela change, mas mais
  sério) — `apps/workers/src/Buteco.Workers/Agents/PushNotificationConfigCodec.cs:16-17`
  serializa sem `A2AJsonUtilities.DefaultOptions`, gravando
  `Metadata["pushNotificationConfig"]` com casing errado (`Url`/`Token`
  em vez de `url`/`token`) e nulls explícitos, em produção desde
  2026-08-08. **Confirmado, não potencial**: `GetTask`/`ListTasks`
  (`GET /agents/{id}/a2a`,
  `RoutingA2ARequestHandler.GetTaskAsync`/`ListTasksAsync`,
  `apps/api/src/Buteco.Api/A2A/RoutingA2ARequestHandler.cs:47-56`,
  mapeado em `Program.cs:78`) devolvem a `AgentTask` inteira, `Metadata`
  incluída, sem filtragem, a qualquer cliente do protocolo A2A; nenhum
  código do repo relê essa chave para reformatá-la. Deliberadamente fora
  do escopo de `crossapp-session-codec-encoder` (misturaria um bug de
  escaping benigno com um bug de contrato de wire format com exposição
  externa real). Gatilho: antes de qualquer mudança em
  `apps/workers/.../Notifications/` ou no próprio
  `PushNotificationConfigCodec`; dado que o endpoint já expõe o formato
  errado há mais de duas semanas, a change de correção deixa de ser
  "sucessora eventual" e passa a candidata a prioridade de
  sequenciamento — decisão para quem revisar este item, não decidida
  aqui.

## Próximo passo

**Imediato**: restauração de baseline de testes — **não é change de
capability, é dívida de infraestrutura de teste**, e vem sequenciada
**antes** de `inbox-contexto-canal-metadata` (etapa 2 da linha de
contexto temporal). Motivo do sequenciamento: a etapa 2 vai mexer em
`TaskJobMessage` (record fechado, precisa de campo novo) e em
`AgentDelegationToolSetResolver.DelegateToTargetAsync`/
`CreateDelegatedTaskAsync` — exatamente a superfície que
`InboxOrchestratorRoundTrip.Tests` e os testes cross-app cobrem. Entrar
na etapa 2 com a baseline suja (falhas nomeadas sem correção nem
decisão consciente de não corrigir) é pior do que foi entrar em
`apps-workers-contexto-temporal` do mesmo jeito. Grupos, cada um com o
diagnóstico já coletado na baseline nomeada acima — **nenhuma correção
proposta aqui, só o registro de que precisa de decisão**:

- `apps/inbox`: `WebhookEndpointsTests`/`MessagePersistenceTests` —
  `ObjectDisposedException` sobre `IServiceProvider`, corrida de
  disposal do `WebApplicationFactory` entre classes de teste em
  paralelo. Não-determinístico (zero falhas numa rodada, 8 na outra).
- `apps/api`: `AgentDeactivationTests.SendMessage_WithPushNotificationConfig_ForInactiveAgent_NeverPublishesJobOrCallsWebhook`
  — falha nomeada consistente nas rodadas coletadas.
- ~~`tests/CrossAppTaskStoreCompatibility.Tests`~~ — **resolvido por
  `crossapp-session-codec-encoder`**, ver a subseção "Correção de
  encoder cross-app" acima. 2/2 verde.
- `tests/InboxOrchestratorRoundTrip.Tests` —
  `MessageReceived_TriggersFullRoundTrip_PushNotificationReceivedWithCorrectPayload`,
  timeout de ~21s contra limite de 20s, reproduz no commit anterior ao
  apply de `apps-workers-contexto-temporal`; leitura mais provável é
  latência do `podman` frente a Docker nativo, não confirmada por falta
  de comparação nativa disponível neste ambiente.
- `apps/frontend` — `AgentForm`/`ChannelForm`/`McpServerForm` e páginas
  de criação/edição, timing de `userEvent`; contagem não-determinística
  entre rodadas (14–15).

Etapa 2 da linha de contexto temporal (`inbox-contexto-canal-metadata`)
é o sucessor natural depois disso, ainda não proposta.

A linha de trabalho de histórico de conversa por sessão no inbox fechou
nas três etapas antes desta — foi a primeira vez desde o MVP que não
havia sucessor já definido, até `apps-workers-contexto-temporal` começar
e, agora arquivada, deixar a etapa 2 como sucessora definida de novo.

Candidatos que saem do que já está registrado acima, em nenhuma ordem
particular:

- **Responder pela UI** — hoje a tela de sessões é só leitura. Foi
  Non-Goal explícito da etapa 3 e é o passo seguinte mais óbvio do ponto
  de vista de produto: envolve autoria de mensagem, `IOutboundMessageSender`
  num caminho de saída iniciado pelo operador (e não pelo agente), e
  provavelmente uma distinção na `Message` entre resposta do agente e
  mensagem do humano.
- **Índice único na criação de `Session`** — gap de concorrência já com
  causa, local e teste conhecidos. Change pequena.
- **Verificação de autenticidade do webhook do WAHA** — o único risco de
  segurança nomeado e não mitigado que sobrou.
- **Estado da sessão exposto pela API** — pré-requisito para a lista de
  sessões distinguir conversa viva de encerrada.