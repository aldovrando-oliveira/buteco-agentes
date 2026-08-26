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

A linha de trabalho de contexto temporal e de canal está com a **etapa 1
concluída e arquivada** (`apps-workers-contexto-temporal`) e a **etapa 2
dividida em duas, pela revisão da exploração `inbox-contexto-canal-metadata`**
(perfis de risco opostos — ver seção "Instante da mensagem" abaixo):
`inbox-instante-mensagem` (instante da mensagem, aplicada) e
`inbox-contexto-canal` (contexto de canal — `DisplayName`/`ExternalId`,
risco de injeção de prompt sem mitigação fechada, ainda não proposta). Ver
"Changes aplicadas" abaixo para o que entrou e "Próximo passo" para a
sequência.

`inbox-session-indice-unico` fechou o último item da dívida de baseline
com causa de produção conhecida (`ContactSessionResolver` sem índice
único protegendo `Session` contra concorrência) — sequenciada antes da
etapa 2 pelo mesmo motivo de `crossapp-session-codec-encoder`/
`push-notification-config-codec-encoder`: a etapa 2 mexe no caminho de
entrada de mensagem de `apps/inbox`. Ver "Índice único de Session"
abaixo.

`inbox-push-notification-decrypt-resiliente` corrigiu o terceiro defeito
da mesma família de `DebounceSweepService` (`try/catch` existente, mas a
chamada que mais realisticamente falha posicionada fora dele) e, como
efeito colateral, destravou `tests/InboxOrchestratorRoundTrip.Tests` — a
única verificação de acordo real entre os três apps, parada desde antes
de `inbox-instante-mensagem`. Ver "Correção do decrypt de push
notification" abaixo.

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
  `ObjectDisposedException` sobre `IServiceProvider`) e HEAD zerou
  (155/155). **Correção de uma hipótese anterior registrada nesta
  seção — o mecanismo real não é o que estava escrito aqui.** Não é
  corrida de disposal do `WebApplicationFactory` entre classes de teste
  em paralelo: confirmado contra a documentação do xUnit 2.9.3 que, sem
  nenhum `[Collection]`/`[CollectionDefinition]` no repo, `IClassFixture`
  dá a cada classe seu próprio `WebApplicationFactory`, container e
  `IServiceProvider` totalmente independentes — não existe caminho no
  modelo do xUnit para a disposal de uma classe alcançar outra. Causa
  real, reproduzida e corrigida por `inbox-sweep-service-resiliencia`:
  `DebounceSweepService` (`BackgroundService` real de produção) não
  tratava exceções fora de A2A/transporte na própria consulta de
  candidatos; qualquer soluço transitório de Postgres nessa consulta
  escapava de `ExecuteAsync` e, pelo default do .NET 8+
  (`HostOptions.BackgroundServiceExceptionBehavior = StopHost`, não
  configurado em lugar nenhum do repo), derrubava o processo inteiro —
  reproduzido de verdade (`Npgsql.PostgresException: 57P01`) em 4 de 4
  execuções verificadas com log detalhado, sob a contenção de recursos de
  9 `WebApplicationFactory`+Postgres Testcontainers concorrentes.
  Taxa medida numa rodada de 10 execuções da suíte: 2/10 com esta
  `ObjectDisposedException` (mecanismo agora corrigido). Também
  nenhuma das duas rodadas originais desta baseline reproduziu a corrida
  específica de `ContactSessionResolver.FindOrCreateSessionAsync` — na
  mesma rodada de 10 execuções, esse outro flake
  (`InboundMessageOrchestratorTests`) apareceu à parte, em 3/10, sem
  relação de causa com o defeito do `DebounceSweepService`. **Correção
  (`inbox-session-indice-unico`)**: a causa desse outro flake de 3/10
  está confirmada agora — é exatamente a corrida de
  `ContactSessionResolver.FindOrCreateSessionAsync`, reproduzida de novo
  10 vezes contra Postgres real com a mesma taxa antes da correção (ver
  "Índice único de Session" abaixo). Deixa de ser "sem causa confirmada",
  e deixa de ser uma falha conhecida: corrigida e o mesmo teste
  reproduzido **10/10** depois da correção. `apps/inbox` (`Buteco.Inbox.Tests`)
  não tem mais nenhum flake conhecido — suíte completa **160/160**.
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

### Correção do encoder de push notification

`push-notification-config-codec-encoder` — fecha o item em aberto que
`crossapp-session-codec-encoder` deixou registrado (mesma classe de bug,
mais sério: exposição externa confirmada, não potencial).

`PushNotificationConfigCodec.Encode`
(`apps/workers/src/Buteco.Workers/Agents/PushNotificationConfigCodec.cs`)
serializava sem `A2AJsonUtilities.DefaultOptions`, gravando
`Metadata["pushNotificationConfig"]` com casing PascalCase
(`Id`/`Url`/`Authentication`/`Token`) e nulls explícitos, em produção
desde 2026-08-08. Diferente do bug de `ConversationSessionCodec`: o valor
é um `JsonElement` objeto já materializado, e a re-serialização do
`AgentTask` em `SaveTaskAsync` não reaplica naming policy sobre ele — só
sobre objetos .NET serializados a fresco. Por isso o formato errado
chegava ao disco, não só divergia temporariamente em memória.

Uma rodada de `/opsx:explore` fechou as sete perguntas em aberto por
leitura de código, decompilação de `A2A.dll`, reprodução empírica do
path de produção e consulta à spec A2A na fonte:

- **Formato correto confirmado na spec A2A**: `url`, `token`,
  `authentication`, `id` — camelCase, opcionais omitidos, nunca `null`.
  Bate exatamente com `A2AJsonUtilities.DefaultOptions`.
- **Sem `Decode` interno**: nenhum código do repo relê essa chave como
  `PushNotificationConfig` tipado — só o `Encode` grava, e só o teste
  E2E lia como `JsonElement` bruto (e afirmava o formato errado, agora
  corrigido). Sem gatilho de migração por quebra de leitura interna.
- **Postgres de dev** (`buteco_agents`, não produção): 175 tasks totais,
  37 com a chave, 37/37 no formato errado antes da correção, 0 no
  correto.
- **Nenhum consumidor de `GetTask`/`ListTasks` encontrado no código do
  repo** — `apps/inbox` (único cliente A2A conhecido) só chama
  `SendMessageAsync`. Limitação do método, não garantia: um cliente
  externo autenticado poderia existir fora do repo (a rota é
  autenticada, não anônima).
- **Sem migração das linhas já gravadas** — decisão com gatilho, ver
  "Itens em aberto" abaixo.

Correção: `Encode` passa a usar `A2AJsonUtilities.DefaultOptions`.
`PushNotificationEndToEndTests.cs` corrigido nas duas ocorrências que
afirmavam o formato antigo, com asserção negativa (ausência de
PascalCase e de opcionais ausentes gravados como `null`) estendida
também ao aninhamento `authentication.scheme`/`authentication.credentials`
e a `token`. Suíte completa de `apps/workers/tests/Buteco.Workers.Tests`
verde (92/92) após a correção.

### Índice único de Session

`inbox-session-indice-unico` — fecha o item em aberto "Criação de
`Session` sem índice único protegendo contra concorrência", registrado
desde `inbox-mensagens-persistidas`.

Reproduzido 10 vezes contra Postgres real antes da correção
(`InboundMessageOrchestratorTests.ReceiveMessageAsync_ConcurrentCallsSameSession_ResolveToSinglePendingDispatch`,
8 chamadas concorrentes ao mesmo `Contact` novo): **3 falhas em 10**, com
3, 4 e 8 `Session`/`PendingDispatch` distintas nas três falhas.
**Correção da nota da baseline pós-archive de
`apps-workers-contexto-temporal`** (o item em aberto que este parágrafo
fecha foi removido da lista "Itens em aberto" abaixo — a correção está
aqui): aquela nota registrava que o flake de 3/10 de
`InboundMessageOrchestratorTests` não tinha causa confirmada — a
reprodução desta change confirma que é exatamente esta corrida; deixa de
ser "sem relação de causa" e passa a "causa confirmada".

Cada `Session` duplicada gerava seu próprio `PendingDispatch`
(`DebounceSweepService` despacha cada um independentemente, com seu
próprio `ContextId` A2A), e a resposta de saída é endereçada por
`ContactExternalId` — não por `Session`. Efeito real, não só de banco: o
mesmo chat do usuário final recebia N respostas independentes, cada uma
gerada sem o histórico das outras.

Correção segue o mesmo idioma já usado por `Contact`
(`HasIndex(ChannelId, ExternalId).IsUnique()`) e `PendingDispatch`
(`HasIndex(SessionId).IsUnique().HasFilter(...)`): índice único parcial
`sessions."ContactId"` filtrado por `"ClosedAt" IS NULL` (no máximo uma
`Session` aberta por `Contact`), com `ContactSessionResolver` passando a
escrever `Session.ClosedAt` (coluna existente desde
`inbox-crm-contato-sessao`, nunca escrita até agora) ao detectar
expiração por timeout, e a buscar a sessão aberta filtrando `ClosedAt ==
null` explicitamente — não mais por inferência via "mais recente por
`StartedAt`". Violação do índice tratada com o mesmo `catch
DbUpdateException` + detach + re-busca de `Contact`/`PendingDispatch`
(uma única retentativa, sem loop — o Postgres só libera a exceção ao
perdedor depois que o vencedor já commitou, então a retentativa sempre
encontra a `Session` vencedora já persistida).

Detach diferente do padrão já existente: `InboundMessageOrchestrator
.DetachAddedEntities()` só cobre entidades `Added`, porque seu cenário
não faz `UPDATE` na mesma transação do `INSERT`. Aqui, o `Close()` da
`Session` expirando entra como `Modified` na mesma tentativa que insere
a `Session` nova — sem descartar também o `Modified` na retentativa, o
`Close()` da tentativa perdedora ficaria preso no change tracker e
corromperia o `SaveChangesAsync` seguinte. Achado próprio desta change,
não copiado do padrão existente sem adaptação.

Migration (`AddUniqueOpenSessionIndex`) inclui saneamento retroativo:
toda `Session` histórica exceto a mais recente por `Contact` recebe
`ClosedAt` = `StartedAt` da `Session` seguinte, via `LEAD() OVER
(PARTITION BY "ContactId" ORDER BY "StartedAt")` — sem isso, o índice
único não poderia ser criado (nenhuma `Session` jamais teve `ClosedAt`
escrito). Verificado em dev antes e depois: 1 `Contact` com 2 `Session`
(intervalo de ~2h10, fronteira de inatividade legítima, não artefato de
corrida) → após a migration, 0 `Contact` com mais de uma `Session`
aberta.

Modelo de deploy verificado antes de decidir a sequência de migration:
`apps/inbox` roda como instância única, sem orquestração de deploy no
repo, migration é passo manual e separado do boot (`dotnet ef database
update` antes de subir o processo) — não existe janela em que código
antigo rodaria contra o índice novo, então a change não precisou ser
dividida em duas fases. Isso vale para dev, verificado nesta change —
**aplicar a mesma migration em produção segue em aberto, não executado
nesta sessão** (sem acesso a produção), ver item "Aplicação em produção
da migration `AddUniqueOpenSessionIndex`" logo abaixo.

Verificação pós-correção: suíte completa de `apps/inbox` **160/160**
(157 testes antes desta change + 3 novos — 2 em
`ContactSessionResolverTests`, 1 em `ContactEndpointsTests`, ver seções
acima —, todos verdes), e
`ReceiveMessageAsync_ConcurrentCallsSameSession_ResolveToSinglePendingDispatch`
— o teste que falhava 3/10 — rodado **10/10** contra Postgres real
depois da correção.

### Instante da mensagem

`inbox-instante-mensagem` — etapa 2 (parte 1 de 2) da linha de contexto
temporal e de canal, fechando o caso de uso que originou a linha inteira:
mensagem escrita no dia 21 dizendo "amanhã", processada no dia 22, passa a
resolver para o dia 22, não mais para o dia 23. A exploração
`inbox-contexto-canal-metadata` cobria também contexto de canal
(`DisplayName`/`ExternalId`); a revisão dividiu em duas changes por
perfil de risco oposto — este bloco é plumbing com cuidado de formato
conhecido, o outro (`inbox-contexto-canal`, ainda não proposta) coloca
texto livre do usuário final na janela de contexto do modelo pela
primeira vez, sem mitigação que elimine o risco de injeção de prompt.

O que entrou:

- **`apps/inbox`** (`DebounceSweepService.BuildSendMessageRequest`) passa
  a incluir, em `Message.Metadata["messageInstant"]`, o instante de
  recebimento da última mensagem do buffer (`PendingDispatch
  .LastMessageAt`) — string ISO 8601 com offset (formato de
  arredondamento `"O"`), serializada com `A2AJsonUtilities.DefaultOptions`.
- **`apps/workers`** (`AgentExecutionService.ExtractMessageInstant`) lê
  essa chave da última mensagem de usuário no histórico da task e repassa
  para `TemporalContextBlockBuilder.Build` — a regra de precedência da
  etapa 1 passa a ter, pela primeira vez em produção, um instante de
  mensagem real para resolver contra, incluindo a linha de defasagem
  entre os dois instantes. Ausência (Metadata nulo, chave ausente, ou
  valor ilegível) colapsa para o comportamento da etapa 1, sem falhar a
  task; o terceiro caso emite log de aviso com o identificador da task.
- **Propagação na delegação**: o instante da mensagem do Source passa a
  ser gravado na task criada para o Target
  (`AgentDelegationToolSetResolver.CreateDelegatedTaskAsync`), pela mesma
  chave `Message.Metadata["messageInstant"]` — o Target lê pelo mesmo
  mecanismo de qualquer task, não por um transporte separado. Sem isso,
  Source e Target resolveriam "amanhã" contra dias diferentes quando
  processados em momentos de relógio distintos (confirmado real, não
  hipotético: delegação é assíncrona pela mesma fila `agent-tasks`,
  Source bloqueado aguardando o Target — a etapa 1 já tinha declarado
  isso Non-Goal, com gatilho apontando para esta change).
- **Nenhuma mudança em `apps/api`** — `Message.Metadata` já sobrevivia
  até `a2a_tasks.payload` sem alteração de código; confirmado por
  rastreamento real do pipeline (`EnqueueingAgentHandler.ExecuteAsync` →
  `TaskProjection.Apply` → `PostgresTaskStore.SaveTaskAsync`, serialização
  fresca do `AgentTask`, sem risco de formato). **Nenhuma migração de
  banco.**

**Duas correções de estimativa registradas nesta seção anteriormente,
ambas fechadas agora com a evidência real (convenção 9 — corrigir onde o
achado divergiu, não só no resumo do chat)**:

1. O custo dimensionado no registro da etapa 1 (ver "Itens em aberto",
   entrada removida abaixo) dizia que `TaskJobMessage` precisaria de
   campo novo para propagar o instante. **Não precisou** — o transporte
   final foi via `Message.Metadata` (contrato A2A, não o envelope
   RabbitMQ interno), e o worker já relê a task inteira do store; zero
   mudança em `TaskJobMessage`.
2. O custo de propagar na delegação estava registrado como "tocar dois
   pontos em `DelegateToTargetAsync`/`CreateDelegatedTaskAsync`". O custo
   real foi maior — **4 métodos + 1 assinatura de interface**
   (`IAgentDelegationToolSetResolver.ResolveAsync`,
   `AgentExecutionService.ExecuteAsync`, `BuildDelegationTool`,
   `DelegateToTargetAsync`, `CreateDelegatedTaskAsync`) — porque
   `CreateDelegatedTaskAsync` precisa escrever a chave no `Message` que
   constrói para o Target (o registro original achava que não precisaria,
   por só "montar `AgentTask`/`Metadata`, não o bloco temporal" — leitura
   certa sobre onde `TemporalContextBlockBuilder.Build` é chamado, errada
   sobre o custo total: se o Target lê pelo mesmo mecanismo de qualquer
   task, alguém precisa escrever a chave nele antes).

**Decisão sobre a convenção 12 de `01-ARQUITETURA_E_CONVENCOES.md`**:
avaliado e descartado adicionar um exemplo novo — o mecanismo de
`JsonElement` sobrevivendo à re-serialização sem reaplicar naming policy
já está documentado na seção "AgentCard / protocolo A2A" do mesmo
arquivo (achado por `push-notification-config-codec-encoder`); a escolha
desta change de usar um valor escalar em `messageInstant` é aplicação
desse conhecimento já registrado, não uma lição nova extraída de um
defeito novo.

**Verificação**: `Buteco.Inbox.Tests` e `Buteco.Workers.Tests` verdes com
os testes novos desta change incluídos (11 casos em
`AgentDelegationExecutionTests`, incluindo os 3 novos de propagação; 7 em
`TemporalContextMessageInstantTests`, novo; 2 em
`DebounceMessageInstantTests`, novo). O teste de acordo real entre
`apps/inbox` e `apps/workers` (convenção 11,
`InboxOrchestratorRoundTrip.Tests`) foi implementado e compila, mas
**nunca completou nesta sessão — nem no commit imediatamente anterior ao
apply desta change**. Verificado com o mesmo método do bisect de
`crossapp-session-codec-encoder` (`git worktree` isolado em `a91f0c9`,
mesma sessão/ambiente): com o timeout do teste estendido a 90s dos dois
lados, o commit base falha **identicamente** ao HEAD desta change (mesmo
`TimeoutException`, ~91s nos dois) — não regressão, causa ambiental
confirmada por evidência direta, não por semelhança. Mas isso também
significa que **o formato de fio de `messageInstant` entre os dois apps
nunca foi provado ponta a ponta nesta sessão** — só por asserções
unitárias, que a convenção 11 não aceita como prova de acordo. Ver
"Itens em aberto" (os dois itens novos sobre este teste) para o
detalhamento e o gatilho.

**Atualização (`inbox-push-notification-decrypt-resiliente`)**: essa
leitura também estava errada — não é causa ambiental, é regressão real
de produção introduzida em `9fd8a87` (`Decrypt` de credencial fora do
bloco protegido em `PushNotificationEndpoints.DeliverResponseAsync`),
anterior a `a91f0c9`. Por isso o bisect desta sessão, que comparou só
`a91f0c9`/HEAD, não pegou a regressão: os dois já estavam depois do
commit ruim. Corrigida, e o teste de acordo de convenção 11 citado acima
foi executado com sucesso pela primeira vez. Ver "Correção do decrypt de
push notification" abaixo.

### Correção do decrypt de push notification

`PushNotificationEndpoints.DeliverResponseAsync` (`apps/inbox`) chamava
`credentialCipher.Decrypt(dispatchInfo.EncryptedCredentials)` — e a
consulta que resolve o `Channel` de origem — **fora** do `try/catch` que
existe especificamente para engolir falha de entrega ao canal. Terceiro
defeito da mesma família encontrado pela linha de restauração de
baseline (depois de `DebounceSweepService`,
`inbox-sweep-service-resiliencia`): o `try/catch` certo existe, mas a
chamada que mais realisticamente falha está posicionada fora dele. Se
`Decrypt` lançasse, a exceção escapava do handler inteiro, `apps/inbox`
devolvia 500 não tratado, e o `PendingDispatch` nunca era limpo — a
mensagem do usuário ficava presa em "Processando" para sempre, depois de
a resposta do agente já ter sido gerada (já custou uma chamada de LLM).

Causa raiz e regressão confirmadas por execução real e `git bisect`
(exploração `roundtrip-tres-apps-nao-completa`, não por leitura de
código sozinha — convenção 6): último commit bom `c72c64f` (passa em
1s); primeiro commit ruim `9fd8a87` ("fix(inbox): corrige perda de
resposta do agente na recepção de push notification") — a correção que
fez `ExtractResponseText` efetivamente encontrar a resposta do agente
(antes sempre devolvia `null`) fez `DeliverResponseAsync` executar pela
primeira vez, e expôs o `Decrypt` desprotegido logo atrás.

Corrigido ampliando o bloco `try` já existente para cobrir a consulta de
`dispatchInfo` e o `Decrypt`, mantendo `catch (Exception)` único e amplo
sem lista fechada de tipos (mesmo precedente de
`inbox-sweep-service-resiliencia`, Decisão 3) — ver design.md de
`inbox-push-notification-decrypt-resiliente`.

**Verificação real, não só suíte de unidade**: `Buteco.Inbox.Tests`
163/163 (novo teste incluído,
`ReceiveAsync_CredentialDecryptFails_PersistsOutboundMessageAsFailedWithReasonAndDoesNotFailRequest`,
par do já existente `ReceiveAsync_SenderThrows_...`). E o resultado que
importa mais: **`tests/InboxOrchestratorRoundTrip.Tests` completa agora
— 3/3**, incluindo
`MessageReceived_TriggersFullRoundTrip_TaskCarriesMessageInstantInRawPersistedJson`
(a asserção de convenção 11 de `inbox-instante-mensagem`, nunca
executada com sucesso antes desta change — verificada também em
isolamento). O round-trip completa **sem** a correção do fixture forjado
de `RoundTripTests.CreateChannelAsync` (literal
`"irrelevante-nesta-fatia"`, não ciphertext real) precisar acontecer
antes: nenhuma das asserções dos três testes depende de a entrega ao
canal ter sucesso, só de o `PendingDispatch` ser resolvido (o que a
correção de produção garante mesmo quando `Decrypt` falha) e do estado
da task em `apps/api`. A correção do fixture
(`roundtrip-fixture-credencial-real`) continua válida para quando um
teste futuro precisar afirmar entrega real ao canal, mas deixa de ser
bloqueante para o round-trip completar — não é mais urgente.

**Três leituras do mesmo sintoma, registradas na ordem em que
ocorreram** — a causa real só apareceu na terceira, e as duas primeiras
enganaram por razões diferentes, que valem como lição de método:

1. "`TimeoutException` a ~21-22s contra um limite interno de 20s,
   consistente com latência do `podman`" — leitura mais antiga. Errada:
   o número nunca foi evidência de proximidade do sucesso, era só o
   próprio limite de 20s do teste cortando a espera cedo.
2. "Não completa nem com timeout estendido a 90s, causa ambiental não
   aprofundada" (`inbox-instante-mensagem`) — corrigiu a leitura 1, mas
   ainda errada: o bisect daquela sessão comparou `a91f0c9`/HEAD e leu
   "idêntico nos dois lados" como sinal de causa ambiental. Não era —
   `9fd8a87` é ancestral de `a91f0c9`, então **toda comparação
   base×HEAD daquela sessão já estava dentro da janela quebrada**;
   "reproduz no commit base escolhido" não descarta regressão quando
   esse base já vem depois do commit ruim. Lição de método: um bisect só
   descarta regressão se o commit base for anterior ao intervalo sob
   suspeita, não só "o commit imediatamente anterior a esta change".
3. Regressão real de produção, `9fd8a87`, corrigida por
   `inbox-push-notification-decrypt-resiliente` (esta seção).

**Hipótese do `DebounceSweepService` engolindo exceção silenciosamente —
testada e descartada**: rodado com log detalhado durante a exploração;
nenhuma das duas mensagens de erro de
`DebounceSweepService.ProcessDueDispatchesAsync` ("Falha ao consultar
candidatos elegíveis..."/"Falha ao processar disparo do candidato...")
apareceu no log completo. O sweep disparava normalmente em toda
execução — não reabrir esta hipótese.

**Docker nativo confirmado ausente** neste ambiente (sem binário, sem
`Docker.app`, sem `docker context`) — fecha a pergunta que ficava em
aberto desde os primeiros registros deste item ("faltaria comparação
nativa, não disponível").

**Item novo em aberto, não desta change**: o bloco final e incondicional
de `PushNotificationEndpoints.ReceiveAsync`
(`UpdateMessageDispatchStatusesAsync` + `Remove(pendingDispatch)` +
`SaveChangesAsync`) não tem `try/catch` próprio — uma indisponibilidade
real de Postgres nesse ponto (não uma falha lógica como a corrigida
aqui) ainda deixaria o `PendingDispatch` preso, e diferente de um
`PendingDispatch` `Pending`, um `Dispatching` preso por essa via não é
revisitado pela próxima varredura de `DebounceSweepService` (filtro
`Status == PendingDispatchStatus.Pending`). Pré-existente, mais amplo
que o defeito corrigido aqui (roda para toda push notification aceita).
Gatilho: próxima falha real de Postgres observada afetando `apps/inbox`,
ou próxima change que precisar tocar `ReceiveAsync` por outro motivo.

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
- **Estado da sessão (aberta/encerrada) não é exposto pela API** —
  atualizado por `inbox-session-indice-unico`: o estado agora **é
  derivável** (`Session.ClosedAt` passou a ser escrito de verdade, e já
  está em `SessionResponse.ClosedAt`, retornado por `GET
  /contacts/{id}/sessions`), mas continua sem estar documentado/promovido
  como contrato de produto, e `GET /channels/{id}/sessions` (o que o
  frontend de fato consome) ainda só devolve `LastActivityAt`, sem
  `closedAt`. Gatilho: a lista de sessões do frontend precisar distinguir
  conversa viva de conversa encerrada.
- **Aplicação em produção da migration `AddUniqueOpenSessionIndex`**
  (`inbox-session-indice-unico`) — aplicada e verificada em dev nesta
  change; **não aplicada em produção nesta sessão, sem acesso**. Quatro
  coisas a levar para quando isso acontecer:
  - **Query de diagnóstico** (rodar antes de aplicar, para saber o que
    esperar — ver o próximo ponto):
    ```sql
    SELECT count(*) FROM (
      SELECT "ContactId" FROM sessions WHERE "ClosedAt" IS NULL
      GROUP BY "ContactId" HAVING count(*) > 1
    ) sub;
    ```
  - **Referência de dev**: 1 contato com 2 sessões, de 10 sessões e 9
    contatos distintos no total. Esse caso específico foi analisado e é
    fronteira de inatividade legítima (2h de intervalo contra timeout de
    1h em dev), não artefato da corrida — não há garantia de que
    produção tenha a mesma proporção ou natureza.
  - **Volume maior em produção**: o saneamento (`LEAD() OVER (PARTITION
    BY "ContactId" ORDER BY "StartedAt")`) generaliza para qualquer
    quantidade de sessões por contato — a migração funciona igual — mas
    volume alto muda a **duração** do `UPDATE`, e isso importa porque o
    processo fica parado durante ela (ver ponto seguinte). Rodar a query
    de diagnóstico antes de parar o processo é o ponto de saber o que
    esperar.
  - **A sequência de deploy é a garantia, não um detalhe**: parar o
    processo de `apps/inbox` → `dotnet ef database update` → subir o
    processo já com o código novo, nessa ordem — é isso que garante que
    o código antigo nunca roda contra o índice novo. Se a migration for
    aplicada por outro caminho (processo novo subindo antes, ou a
    migration rodando com o processo antigo ainda respondendo tráfego),
    o índice único passa a rejeitar as inserções que a rotação normal de
    sessão por inatividade faz — `DbUpdateException` não tratada no
    caminho de recepção de webhook, no caminho mais comum do app. O
    `design.md` desta change registra que essa janela não existe hoje
    porque o deploy é de instância única com parada — **estado
    verificado, não garantia permanente**; reavaliar se isso mudar
    (múltiplas instâncias, deploy faseado).
  Gatilho: antes do próximo deploy de `apps/inbox` em produção.
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
- **`WahaWebhookMessagePayload.Data.Info.PushName`** (`DisplayName` do
  WAHA, `inbox-mensagens-persistidas`) — confirmado só via discussão da
  comunidade para o engine GOWS, não pela documentação oficial do WAHA
  (que declara o shape de `_data` como "interno do engine, pode variar").
  Gatilho: confirmar contra uma instância WAHA real de produção assim que
  houver uma disponível; se o campo divergir, `DisplayName` do WAHA
  simplesmente fica sempre nulo até a correção, sem quebrar nada mais.
- **`WahaInboundWebhookHandler`/`TelegramInboundWebhookHandler` chamam
  `DateTimeOffset.UtcNow` direto, fora do `TimeProvider`** (achado por
  `inbox-instante-mensagem`) — mesma regra que o Achado 9 de
  `apps-workers-contexto-temporal` fechou em `apps/workers` (`TimeProvider`
  como único ponto de acesso a relógio/fuso), mostrando que aquela
  varredura era, de fato, só de `apps/workers`, nunca estendida a
  `apps/inbox`. Gatilho: antes de qualquer mudança futura nesses adapters,
  ou se algum teste precisar de relógio determinístico em `apps/inbox`.
- **Nenhum adapter de canal desserializa o timestamp que o provedor
  envia** (achado por `inbox-instante-mensagem`) — `Message.OccurredAt`
  em `apps/inbox` é sempre o instante de recebimento do webhook, nunca o
  declarado por WAHA/Telegram no payload (confirmado: nenhum dos dois
  payloads modela esse campo em C#, embora os dois provedores enviem um).
  Sob atraso do lado do provedor (WAHA fora do ar, reentrega tardia), o
  `messageInstant` que chega a `apps/workers` já nasce defasado, sem
  caminho de código para recuperar o instante real — decisão consciente
  registrada em `inbox-instante-mensagem` (design.md, Riscos), não
  lacuna. Gatilho: se a defasagem por atraso do provedor virar problema
  real observado.
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
- ~~`InboxOrchestratorRoundTrip.Tests.MessageReceived_TriggersFullRoundTrip_...`
  não completa contra `podman`~~ — **resolvido por
  `inbox-push-notification-decrypt-resiliente`**, ver a seção "Correção
  do decrypt de push notification" acima: causa real era regressão de
  produção (`9fd8a87`), não latência/ambiente. Três leituras do sintoma
  registradas lá, na ordem em que ocorreram, com a lição de método de
  cada uma — vale ler antes de reabrir qualquer investigação futura de
  timeout nesta suíte.
- ~~Teste de acordo da convenção 11 de `inbox-instante-mensagem` nunca
  executado com sucesso~~ — **resolvido por
  `inbox-push-notification-decrypt-resiliente`** (efeito colateral da
  correção de produção, não de uma correção de fixture):
  `MessageReceived_TriggersFullRoundTrip_TaskCarriesMessageInstantInRawPersistedJson`
  passou, verificado em isolamento. O formato de fio de
  `Message.Metadata["messageInstant"]` entre `apps/inbox` e
  `apps/workers` está provado ponta a ponta agora, com o valor real de um
  lado consumido pelo outro.
- **`InboxFactoryFixture.InitializeAsync` acessa `Services` antes de
  migrar** (achado por `inbox-sweep-service-resiliencia`, design.md,
  Non-Goals) — chama `Services.CreateScope()` para rodar a migration,
  o que já constrói e inicia o host inteiro (incluindo
  `DebounceSweepService`) antes de a tabela existir. Mesma armadilha que
  `OrchestrationFactoryFixture` já corrigiu (migra com um `AppDbContext`
  isolado, sem tocar `Services` antes) — ver o comentário em
  `OrchestrationFactoryFixture.cs`. Não corrigido em
  `InboxFactoryFixture`, usada pelas 9 classes de teste que
  compartilhavam a `ObjectDisposedException` da baseline. Defeito de
  teste, não de produção — depois de `inbox-sweep-service-resiliencia`
  (que resolve o efeito, `DebounceSweepService` não derruba mais o host),
  este item deixa de ser urgente, mas continua correto de qualquer forma.
  Gatilho: antes ou junto da próxima change que toque
  `apps/inbox/tests`.
- **`TaskJobConsumer` (`apps/workers`) com setup inicial desprotegido**
  (achado por `inbox-sweep-service-resiliencia`, design.md, Decisão 4) —
  a sequência de `CreateConnectionAsync`/`CreateChannelAsync`/
  `QueueDeclareAsync`/`BasicQosAsync`/`BasicConsumeAsync`, toda antes do
  loop de consumo, não tem tratamento de exceção; falha do RabbitMQ
  nesse ponto do boot derruba o host via o mesmo default
  `BackgroundServiceExceptionBehavior.StopHost`. Diferente do defeito
  corrigido em `DebounceSweepService` (risco de disponibilidade no boot,
  não falha recorrente durante operação normal — o processamento de
  cada mensagem já tem `try/catch` próprio). Gatilho: antes de qualquer
  mudança futura em `TaskJobConsumer`, ou se `apps/workers` passar a
  subir em ambiente onde o RabbitMQ pode não estar pronto no boot.
- **Linhas antigas de `a2a_tasks` com `pushNotificationConfig` em formato
  divergente** — decisão consciente de não migrar, registrada em
  `push-notification-config-codec-encoder` (design.md D4). Nada quebra
  (não há `Decode` interno; um leitor futuro é case-insensitive), mas as
  linhas gravadas antes da correção (37/37 em dev, formato PascalCase com
  nulls) convivem indefinidamente com as gravadas depois (camelCase, sem
  opcionais ausentes) — sem job de limpeza/retenção em `a2a_tasks`.
  Gatilho: se aparecer um consumidor real (interno ou externo) que
  precise ler tasks terminais antigas com `pushNotificationConfig`,
  avaliar backfill nesse momento.

## Próximo passo

**Concluído nesta sessão**: `inbox-instante-mensagem` foi proposta e
aplicada (ver "Instante da mensagem" acima) — tocou exatamente a
superfície que a baseline abaixo já sinalizava como sensível
(`AgentDelegationToolSetResolver`), sem correção prévia de baseline ter
sido feita antes (diferente do que este parágrafo recomendava
originalmente). Nenhuma regressão nova encontrada por isso — os testes
novos desta change passaram, e o único item da baseline que tocava a
mesma superfície (`InboxOrchestratorRoundTrip.Tests`, ver abaixo)
continua com a mesma causa já registrada, não uma nova.

Grupos da baseline, cada um com o diagnóstico já coletado — **nenhuma
correção proposta aqui, só o registro de que precisa de decisão**:

- ~~`apps/inbox`: `WebhookEndpointsTests`/`MessagePersistenceTests` —
  `ObjectDisposedException` sobre `IServiceProvider`~~ — **resolvido por
  `inbox-sweep-service-resiliencia`**, ver a correção da entrada na
  baseline nomeada acima. Não era corrida de disposal entre classes; era
  `DebounceSweepService` sem tratamento de falha de infraestrutura na
  consulta de candidatos, derrubando o processo via
  `BackgroundServiceExceptionBehavior.StopHost`.
- `apps/api`: `AgentDeactivationTests.SendMessage_WithPushNotificationConfig_ForInactiveAgent_NeverPublishesJobOrCallsWebhook`
  — falha nomeada consistente nas rodadas coletadas.
- ~~`tests/CrossAppTaskStoreCompatibility.Tests`~~ — **resolvido por
  `crossapp-session-codec-encoder`**, ver a subseção "Correção de
  encoder cross-app" acima. 2/2 verde.
- ~~`tests/InboxOrchestratorRoundTrip.Tests`~~ — **resolvido por
  `inbox-push-notification-decrypt-resiliente`**, ver a seção "Correção
  do decrypt de push notification" acima. 3/3 verde, incluindo a
  asserção de convenção 11 de `inbox-instante-mensagem`, verificada com
  sucesso pela primeira vez. Causa real: regressão de produção
  (`9fd8a87`), não latência de `podman` frente a Docker nativo — as
  leituras anteriores registradas aqui (timeout marginal, depois "causa
  ambiental") estavam erradas; ver as três leituras, na ordem em que
  ocorreram, na seção linkada.
- `InboxFactoryFixture.InitializeAsync` acessa `Services` antes de migrar
  — não corrigido ainda, sem urgência depois de
  `inbox-sweep-service-resiliencia` (ver "Itens em aberto" para o
  gatilho completo).
- `TaskJobConsumer` (`apps/workers`) com setup inicial desprotegido —
  não corrigido ainda (ver "Itens em aberto" para o gatilho completo).
- `apps/frontend` — `AgentForm`/`ChannelForm`/`McpServerForm` e páginas
  de criação/edição, timing de `userEvent`; contagem não-determinística
  entre rodadas (14–15).

Com `inbox-session-indice-unico` aplicada, `apps/inbox` (`Buteco.Inbox.Tests`)
não tem mais nenhum flake conhecido — 160/160 (ver "Índice único de
Session" acima). O ruído que resta na fila acima é só cross-app/frontend.

`inbox-contexto-canal` (contexto de canal — `DisplayName`/`ExternalId`) é
o sucessor natural agora, ainda não proposta. Diferente da etapa anterior,
não é só plumbing: a pergunta de injeção de prompt via `DisplayName` (a
que menos atenção recebeu nas conversas anteriores) precisa de desenho
próprio antes de virar proposta — não repetir o padrão de
`inbox-instante-mensagem`, que pôde ir direto para `/opsx:propose` porque
a exploração já tinha fechado as perguntas de risco.

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