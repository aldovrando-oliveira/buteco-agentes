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

A linha de trabalho de contexto temporal e de canal está **concluída nas
duas etapas**: `apps-workers-contexto-temporal` (etapa 1) e, dividida em
duas pela revisão da exploração `inbox-contexto-canal-metadata` (perfis de
risco opostos), `inbox-instante-mensagem` (instante da mensagem) e
`inbox-contexto-canal` (contexto de canal — `channelType`/`contactExternalId`,
`DisplayName` deliberadamente fora do prompt). Todas aplicadas. Ver
"Changes aplicadas" abaixo para o que entrou em cada uma e "Próximo passo"
para o que vem a seguir.

`dedupe-global-nome-de-tool` está **aplicada** — carve de defeito pré-existente
de MCP + delegação (nome de tool sombreado em silêncio no conjunto entregue ao
LLM), sequenciada antes da etapa 4 de bases de conhecimento pela convenção 12.
Suíte de `apps/workers` em 168/168; censo de colisão em todos os agentes reais do
sistema deu zero. Duas perguntas ficaram em aberto **com gatilho, sem bloquear**:
o comportamento dos provedores diante de histórico citando função ausente da lista
(R7) e o limite de nome de função do Anthropic (R8) — as duas dependem da mesma
chave de API que `0b` espera. Ver "Itens em aberto".

**Não existe ambiente de produção**: tudo está em desenvolvimento. Isso não é
pendência, é o estado do projeto, e é o que permite fechar verificações como o
censo de colisão contra dev sem ressalva — dev é onde estão os agentes reais. O
que só faz sentido contra dados de produção está agrupado na subseção
"Primeiro deploy em produção (checklist)".

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

A linha de trabalho de **entrega containerizada** está concluída em uma
change: `containerizacao-stack-servidor` — Dockerfile multi-stage por app,
serviço one-shot de migration bundle antes de `apps/api`/`apps/inbox`, nginx
interno servindo o SPA e fazendo proxy, e `docker-compose.prod.yml` na raiz.
O compose de desenvolvimento permanece intocado.

A linha de trabalho de **redesenho do painel** está **concluída nas oito
etapas**, todas aplicadas, sincronizadas e arquivadas. Ela veio de um handoff
de design feito sem acesso ao código, e o trabalho foi tanto implementá-lo
quanto arbitrar onde ele divergia da realidade da API e do próprio sistema.
Ver "Redesenho do painel" abaixo.

O painel deixou de rodar com os defaults da biblioteca de componentes: existe
uma identidade visual declarada em tema, uma casca própria, padrões de card e
de cabeçalho compartilhados, e o protocolo A2A finalmente visível para quem
opera. A suíte do frontend saiu de 341 para 451 testes ao longo dessas oito
etapas.

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

### Espaço de nome de tool (MCP + delegação)
`dedupe-global-nome-de-tool`

Carve de defeito pré-existente das duas linhas acima, sequenciada antes da etapa
4 de bases de conhecimento pelo precedente de `inbox-enums-json-string`,
`crossapp-session-codec-encoder` e `push-notification-config-codec-encoder`
(convenção 12: defeito pertence a quem expõe, e se corrige lá, em change própria
sequenciada antes).

**O defeito.** O conjunto final de tools era `toolSet.Tools.Concat(delegationTools)`
e nenhum dos dois lados sabia que dividia espaço de nome — o lado MCP não
deduplicava nem contra si mesmo. `FunctionInvokingChatClient.FindTool` resolve
pelo primeiro match ordinal e sombreia o resto **em silêncio**, enquanto as duas
declarações vão no payload para o provedor com o mesmo nome. O caminho alcançável
não era MCP × delegação (impossível pela composição normal: o slug de delegação
nunca contém `__` e o nome MCP sempre contém): era **intra-MCP**, com dois
`McpServer.Name` que só diferem em caractere fora de `[a-zA-Z0-9_-]`
(`"Zendesk MCP"` / `"Zendesk.MCP"` → mesma cadeia), ou dois nomes longos
compartilhando o prefixo de 64 depois da truncagem.

**A correção.** `ToolNameDeduplicator` no ponto de concatenação — o único que sabe
que os conjuntos dividem namespace. Renomeia em vez de descartar (verificado:
`McpClientTool.WithName` preserva `ProtocolTool.Name` e a chamada remota, e
`DelegatingAIFunction` encapsula a função de delegação sem precisar do delegate
capturado no closure). Sufixo numérico global no idioma que a delegação já usava,
agora com a base encurtando para caber nos 64. Precedência declarada (MCP mantém o
nome, delegação é a renomeada) em vez de herdada da ordem de dois operandos.
`orderby` explícito por `McpServerId` na consulta de MCP, que não tinha nenhum.
Aviso em `LogWarning` a cada renomeação, com agente, os dois nomes e as duas
origens.

**Defeitos corrigidos de passagem:** o dedupe local da delegação fazia
`$"{baseName}-{suffix}"` sem re-truncar, produzindo 66 caracteres quando a base já
estava nos 64; e `ToolNameSanitizer` — compartilhado pelos dois resolvedores e
sítio da truncagem — não tinha arquivo de teste.

**Fonte do limite de 64, verificada.** `FunctionObject.name` da especificação
OpenAPI publicada pelo OpenAI, superfície **Chat Completions** (a que
`ChatClientResolver.BuildOpenAi` usa). A Responses API do mesmo provedor permite
128; o Gemini declara 128; o Anthropic não publica o seu. Ver os itens em aberto.

Suíte de `apps/workers`: **168/168**. Censo de colisão em todos os agentes reais
do sistema: **zero**.

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
  **Causa confirmada em `knowledge-base-catalogo-documentos` (2026-09-07):**
  não é flake de infraestrutura, é **dependência de ordem dentro da própria
  classe** — `AgentDeactivationFixture.TaskJobPublisher` é instância única da
  classe (`IClassFixture`) e `PublishedMessages` acumula; três testes fazem
  `Assert.Empty` sobre ela e `SendMessage_AfterReactivation_PublishesNormally`
  publica de verdade, então quem rodar depois dele reprova. Isolado, passa.
  Carve próprio pendente — ver "Itens em aberto".
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

### Contexto de canal

`inbox-contexto-canal` — etapa 2 (parte 2 de 2) da linha de contexto
temporal e de canal, fechando a linha inteira. Reaproveita exatamente o
mecanismo de transporte de `inbox-instante-mensagem` (`Message.Metadata`,
sem campo novo em `TaskJobMessage`, sem mudança em `apps/api`) para duas
chaves escalares novas: `channelType` (`Channel.ChannelType`) e
`contactExternalId` (`Contact.ExternalId`).

O que entrou:

- **`apps/inbox`** (`DebounceSweepService.BuildSendMessageRequest`) passa
  a incluir `Message.Metadata["channelType"]` e
  `Message.Metadata["contactExternalId"]`, lidos de `Channel`/`Contact` da
  sessão de origem — duas chaves escalares separadas, nunca agrupadas num
  objeto (mesma disciplina de `messageInstant`, que elimina por construção
  a variante séria do mecanismo do `JsonElement` documentado em
  `push-notification-config-codec-encoder`).
- **`apps/workers`** (`AgentExecutionService`, `ChannelContextBlockBuilder`
  novo) lê as duas chaves da última mensagem do usuário e concatena um
  bloco de contexto de canal às instruções, depois do bloco temporal —
  reaproveitando `TemporalContextBlockBuilder.Concatenate` uma segunda vez
  em vez de estender o builder temporal, que fica intocado. O texto nomeia
  `contactExternalId` pelo que ele é ("identificador do contato atribuído
  pelo canal"), nunca como telefone — WAHA usa dígitos de telefone,
  Telegram usa um inteiro de chat sem relação com telefone.
- **Ausência por campo, não só por par**: metadata nula, chave ausente, ou
  string vazia — ausência silenciosa, sem log (caminho normal, ex. cliente
  A2A externo que não conhece as chaves). Valor presente com tipo JSON
  diferente de string (número, objeto, array, booleano, `null`) — tratado
  como ausência para o bloco, mas com log de nível aviso identificando
  task e campo, mesmo padrão já usado para `messageInstant` ilegível
  (achado durante a revisão dos artefatos: a primeira versão do
  `design.md` tratava os dois casos como um só, silenciosamente).
- **Round-trip real dos três apps** (convenção 11): valor produzido por
  `apps/inbox`, lido de volta como JSON bruto via `GetTask` de `apps/api`,
  confirmando `channelType`/`contactExternalId` sobrevivendo
  byte-identicamente — não round-trip pelo mesmo tipo C#.

**`Contact.DisplayName` (texto livre do usuário final) NÃO entra nas
`Instructions` do agente — decisão de escopo com gatilho, não Non-Goal
esquecido.** Registrada em `design.md` (D1) com os achados que a
sustentam, todos verificados contra o código real, não hipotéticos:

- Separar o que dependeria de texto livre (chamar a pessoa pelo nome) do
  que não depende (`channelType` é fechado pelo adapter; `contactExternalId`
  é atribuído pelo provedor — nenhum dos dois é digitado pela pessoa)
  mostrou que a maior parte do valor prático já chega sem `DisplayName`.
- Sanitização por classe de caractere protege contra ataque estrutural,
  não contra injeção semântica — uma frase inteira feita só de letras e
  espaços passa ilesa por qualquer allowlist; a defesa contra isso seria
  limite de tamanho, não classe de caractere.
- O único delimitador textual do repo
  (`TemporalContextBlockBuilder.NotAUserMessageMarker`) nunca foi testado
  sob conteúdo adversarial — nunca carregou nada além de texto 100%
  gerado pelo sistema.
- O dano alcançável por uma instrução injetada não é hipotético:
  `IMcpToolSetResolver.ResolveAsync` resolve, por agente, qualquer tool de
  qualquer servidor MCP vinculado via `AgentMcpServer.AllowedTools`, **sem
  distinção de leitura/escrita** — um agente de atendimento com tool MCP
  de escrita (criar agendamento, mutar CRM externo) é configuração normal
  do sistema, não um cenário forçado. Delegação estende isso para as
  tools de outro agente.

**Alternativa considerada e não adotada**: usar `DisplayName` fora do
prompt — o sistema monta uma saudação em código (ex. "Olá, {nome}!" como
primeira mensagem de uma sessão nova), sem o modelo nunca ver o texto
livre. Eliminaria o ataque por construção, mas exigiria um caminho de
resposta que não passa pelo LLM (o desenho atual é o agente produzir o
texto inteiro da resposta), perderia naturalidade, e não cabe no
processamento atual de `AgentExecutionService`, que não distingue
"primeira mensagem da sessão" de qualquer outra. Disponível se o produto
decidir que "chamar pelo nome" é essencial o suficiente para justificar
esse desenho separado.

**Achado reutilizável, além desta change**: identificador atribuído pelo
provedor de um canal (`ChannelType`, `ExternalId`) não é a mesma
categoria de dado que texto livre digitado pelo usuário final
(`DisplayName`) — o primeiro pode entrar no contexto de um agente sem
abrir a classe de risco de injeção de prompt que o segundo abre. Vale
para qualquer dado futuro que alguém queira colocar na janela de contexto
do modelo, não só para esta change (ver também item em aberto sobre
`01-ARQUITETURA_E_CONVENCOES.md` abaixo).

**Verificação**: `Buteco.Inbox.Tests` 164/164 (163 antes desta change + 1
novo em `DebounceSweepServiceTests`). `Buteco.Workers.Tests` 117/117 (8
novos em `ChannelContextBlockBuilderTests`, novo; 7 novos em
`ChannelContextMessageTests`, novo). `tests/InboxOrchestratorRoundTrip.Tests`
4/4, incluindo o cenário novo de acordo real de `channelType`/
`contactExternalId` — nenhuma regressão nos três já existentes.

### Entrega containerizada

- `containerizacao-stack-servidor` — Dockerfile multi-stage por app, cada um
  na pasta do próprio app mas com build context na raiz (obrigatório por
  `Directory.Build.props`/`Directory.Packages.props`/`global.json` e pelo
  `ProjectReference` de `apps/api`/`apps/workers` para `libs/ProviderCatalog`).
  Migration bundle como serviço one-shot antes de `apps/api` e `apps/inbox`
  subirem — nunca para `apps/workers`, que só detecta divergência de schema.
  Nginx interno serve o SPA e faz proxy; TLS e domínio público continuam fora
  deste stack.

### Redesenho do painel (handoff Claude Design)

Oito etapas, na ordem em que foram aplicadas. As três primeiras vieram de uma
única proposta do handoff, dividida para não juntar risco de infraestrutura com
risco de interface no mesmo diff.

- `frontend-agente-description-skills` — expõe `description` e `skills` no
  painel e **corrige perda de dados**: a edição enviava `PUT` sem os campos, e
  o endpoint trata ausência como "limpar", então toda edição pelo painel
  apagava silenciosamente o que fora cadastrado via API. Os campos ficaram
  obrigatórios no tipo de entrada para o compilador impedir a regressão.
- `frontend-roteamento-data-router` — migra para `createBrowserRouter` +
  `RouterProvider`, sem nenhuma mudança visível. Necessária porque `useBlocker`,
  único mecanismo de interceptar navegação, estoura fora do data mode.
  Deliberadamente separada da etapa seguinte.
- `frontend-agente-detalhe-abas` — detalhe do agente vira host de três abas
  com a aba ativa na URL; a página separada de vínculo MCP deixa de existir
  (rota antiga sobrevive como redirect). Só a aba ativa fica montada, o que é o
  que sustenta o modelo de rascunho: trocar de aba é navegação, navegação com
  pendência é bloqueada, logo no máximo uma aba tem rascunho vivo.
- `frontend-mcp-servidor-uso-e-diagnostico` — visão inversa do servidor MCP
  (quais agentes o usam, com quais tools), catálogo de tools no detalhe, e
  explicação por `failureReason` em vez da mensagem crua da API. O teste de
  conexão no formulário passou a escolher o endpoint pelo estado da credencial:
  em branco durante edição significa "manter a atual", e antes o teste rodava
  sem credencial nenhuma.
- `frontend-listas-busca-e-colunas` — busca com normalização de acentos, filtro
  por estado, e colunas que respondem as perguntas que levam alguém a abrir um
  agente. Dívida assumida: busca e filtro rodam no cliente porque nenhuma das
  rotas de listagem tem busca ou paginação.
- `frontend-tema-identidade-visual` — o tema deixa de ser `createTheme({})`.
  Paleta, tipografia, raios e sombras derivados do protótipo, com as âncoras
  corrigidas contra o que a biblioteca de fato consome (a tabela do handoff
  estava deslocada em duas das quatro pontas). Esquema de cor passa a seguir o
  sistema operacional.
- `frontend-shell-navegacao-e-icones` — casca unificada numa barra lateral com
  marca, navegação com ícone e o controle de tema no rodapé. Entra a primeira
  biblioteca de ícones do projeto; os três ícones que existiam eram a geometria
  dela transcrita à mão. O rodapé **não** identifica o operador: o login
  devolve só credencial e validade.
- `frontend-acabamento-telas` — padrões compartilhados de card seccionado,
  rótulo de seção e cabeçalho de detalhe; aparência do badge declarada no tema,
  cobrindo os dezenove de uma vez; volta para a listagem em todas as nove telas
  que não são listagem. Um ponto do protótipo foi **recusado**: o tom de rótulo
  que ele pede reprova no contraste mínimo que a própria identidade visual
  exige.
- `agente-enderecos-a2a` — a única etapa que precisou do backend. A resposta de
  agente passa a carregar os endereços A2A, montados no servidor a partir da
  url pública, num ponto único que o card de descoberta também consome. Sem url
  pública configurada, os endereços vêm ausentes em vez de quebrados.

### Bases de conhecimento (linha de trabalho em 5 etapas)

`knowledge-base-catalogo-documentos` — **etapa 1 de 5**, aplicada em
2026-09-07. Catálogo de bases e de documentos em `apps/api`, extração de
markdown, espelho em `apps/workers`. Nada é indexado: documento nasce `Pending`
e permanece `Pending`, porque não existe consumidor — requisito declarado, não
defeito.

Duas rodadas de exploração precederam a proposta e fecharam por **medição**, não
por raciocínio: busca lexical pura (`tsvector`) reprovou com recall@5 de 47-52%
em corpus real de 419 fragmentos mesmo com a query já reescrita como um modelo a
emitiria, e `ts_rank_cd` não é comparável entre consultas — o score do topo de
uma pergunta *sem resposta no corpus* ficou acima do de uma recuperação correta,
então nenhum limiar separa acerto de ruído. Daí indexação ser assíncrona e
`Pending` existir desde a etapa 1.

> **Correção (convenção 9), registrada por `0b` em 2026-09-10:** a conclusão
> escrita aqui como "embedding vencer" está **errada como formulada**. Ela foi
> tirada do negativo do lexical — o lexical reprovou, logo embedding vence — sem
> que nenhum embedding tivesse sido medido. Medidos os três modos no mesmo
> corpus, `nomic-embed-text-v1` (o modelo que o ambiente configura em
> `EMBEDDING_MODEL`) **empata com o lexical** em recall@5 (87,5% nos dois) e
> **perde** em R@1 (62,5% contra 68,8%) e em MRR (0,731 contra 0,801). O que a
> medição sustenta é "**este** embedding vence": `qwen3-embedding-8b` faz 93,8%
> de R@1 e 100% de recall@5. A escolha de indexação assíncrona e de `Pending`
> não muda — o que muda é que ela depende do modelo, e não de embedding como
> categoria.

O que entrou no `01-ARQUITETURA_E_CONVENCOES.md`: as duas entidades, a seção
"Exclusão: catálogo × conteúdo" (o primeiro `MapDelete` do repositório, com o
critério que o sustenta) e a segunda forma da convenção 8 (checagem sobre
`IServiceCollection`, com o teste extra que ela obriga).

#### Tamanho entregue e por que a projeção errou

Projetado 30-40 arquivos / 1600-2200 linhas; entregue **62 arquivos / 2972
linhas à mão** (mais 4 arquivos / 786 linhas de migração gerada). Decomposição
real:

| origem | arquivos | linhas |
|---|---|---|
| CQRS (12 comandos/queries: command + handler + result) | 24 | 450 |
| Endpoints (2 grupos) | 2 | 303 |
| Entidades de domínio | 3 | 222 |
| Extração + checagem de startup | 6 | 221 |
| Requests / Responses / Options | 8 | 124 |
| Espelho `apps/workers` | 3 | 86 |
| **Testes** (11 arquivos, 66 cenários) | 11 | 1265 |
| Modificados (`AppDbContext` ×2, `Program.cs`, …) | 5 | 301 |
| *(migrações geradas, não contam)* | *4* | *786* |

**A projeção não errou por 55% — o método errou de duas formas independentes**,
e por isso não existe "fator de correção" único:

1. **A âncora misturava artefato de spec com código.** `b5df504` (catálogo +
   vínculo de delegação) tem headline de 30 arquivos / 1593 linhas, mas **8
   arquivos / 787 linhas são `openspec/`** — metade das linhas. O código real
   foram 21 arquivos / 592 linhas. Comparar trabalho de código contra um número
   que inclui proposal/design/tasks/specs subestima por construção.
2. **A âncora tinha 1 arquivo de teste.** Esta change tem 11, com 1265 linhas —
   **42% de todo o trabalho manual**. Cobertura de teste varia por uma ordem de
   grandeza entre changes e não é capturada por diffstat de commit.

Além disso, **contagem de arquivo é dirigida pelo número de operações CQRS, não
por complexidade**: os 24 arquivos de CQRS somam 450 linhas — média de 19 linhas
por arquivo. Uma etapa com muitas operações simples produz muitos arquivos
minúsculos; uma etapa com poucas operações e lógica pesada produz poucos
arquivos longos. Projetar as duas com o mesmo fator é o erro.

Âncora certa para esta change era `da31b27` (catálogo + vínculo MCP), cujo
**código** foi 66 arquivos / 2427 linhas — mesmo número de arquivos que
entregamos, com menos linhas por causa de menos teste.

#### Reprojeção das etapas seguintes (método corrigido)

Projetado por componente, só **código** (artefatos OpenSpec fora da conta),
usando os custos unitários medidos acima: ~2 arquivos e ~37 linhas por operação
CQRS, ~19 linhas por cenário de teste, ~150 linhas por grupo de endpoints, ~200
linhas de migração gerada por app.

| etapa | drivers principais | arquivos | linhas |
|---|---|---|---|
| **2 — indexação** | 1 entidade nova, ~2 CQRS, chunker, resolver de embedding, fila + consumidor, checagem de startup bidirecional, `ContentHash`, contagem de fragmentos, ~45 cenários | **30-40** | **2400-3300** |
| **3 — vínculo agente ↔ base** | 1 entidade de vínculo, ~2 CQRS (replace + list), 1 grupo de endpoints, espelho, ~20 cenários | **16-24** | **900-1400** |
| **4 — resolvedor de tool** | 1 interface + 1 impl + registro + dedupe já resolvido em `0a`, ~25 cenários (inclui "não encontrei"), **+ repetição do censo de inventário** | **14-20** | **1200-1800** |
| **5a — UI catálogo + documentos** | 2 páginas, modal de duas abas com `FileReader`, orquestração de N chamadas, estado por linha, confirmação de exclusão, ~40 testes | **40-52** | **3000-4000** |
| **5b — UI vínculo (quarta aba)** | 1 aba, 1-2 componentes, hooks, ~15 testes | **14-20** | **1000-1400** |
| **5c — UI diagnóstico do índice** | 1 tela pequena, ~8 testes | **8-12** | **500-800** |

**Tarefa prevista da etapa 4, herdada de `0a`
(`dedupe-global-nome-de-tool`):** rodar de novo o censo de inventário de nomes de
tool — a consulta de V8 do `design.md` daquela change —, **estendida às tools de
conhecimento**. Não é para validar o dedupe: ele já está validado pelos guardas
que reprovaram antes e passaram depois, e a etapa 4 não introduz colisão por si só
(só um terceiro conjunto no mesmo namespace; colisão depende de cadastro). É para
ter o inventário, e há chance real de colisão nova ali pelo padrão que o censo já
encontrou: o servidor MCP chamado *"Informações Gerais"* produz
`Informa__es_Gerais__get_menu_info`, onde o `__` **não é o separador** — `ç` e `õ`
viram um `_` cada. Uma base de conhecimento com nome equivalente cai no mesmo
padrão, e o `__` deixa de distinguir os conjuntos.

Duas conclusões que mudam decisão, não só número:

- **A etapa 2 continua cabendo numa change.** Ela tem menos operações CQRS que a
  etapa 1 (poucos arquivos) e mais lógica (arquivos longos) — o oposto do perfil
  desta etapa. A projeção antiga (40-55 / 2500-3500) estava mais certa por acaso
  do que por método.
- **A etapa 5a é a que não cabe.** A UI de catálogo MCP sozinha, sem upload
  nenhum, foi 29 arquivos / 3088 linhas (`47c73a0`); a 5a acrescenta modal de
  duas abas, leitura de arquivo no cliente, orquestração de N chamadas com
  estado por item e confirmação de exclusão. **Recomendação: dividir a 5a em
  duas changes** — "catálogo de bases" (lista, criação, edição, ativação) e
  "gestão de documentos" (o modal, a listagem por documento, exclusão). A
  fronteira é limpa: a segunda depende da primeira, e nenhuma das duas fica
  pela metade.

  **A ordem entre as duas é imposta, e a dependência é de navegação, não de
  dado** (conferido no código, não suposto): as rotas de documento são
  aninhadas em `/knowledge-bases/{knowledgeBaseId:guid}/documents`, e os
  handlers só checam que a base **existe** — `KnowledgeDocumentResponse` não
  carrega campo nenhum da base além de `KnowledgeBaseId`. Do lado da API as
  duas superfícies são independentes. Do lado da tela não: para chegar à
  listagem de documentos é preciso de um `knowledgeBaseId`, que vem da tela de
  detalhe da base — entregue pela primeira. Então "gestão de documentos" vem
  **depois** de "catálogo de bases", e a primeira precisa entregar a rota de
  detalhe já navegável, com a área de documentos ausente (não um placeholder
  vazio, que afirmaria que não há documentos — mesma regra da convenção 13 que
  vale para a contagem de fragmentos).

#### Etapa 3 — vínculo agente ↔ base

`knowledge-base-vinculo-agente`, aplicada em 2026-09-08. `AgentKnowledgeBase`
em `apps/api` (`AgentId` + `KnowledgeBaseId`, sem coluna extra),
`PUT /agents/{id}/knowledge-bases` replace-all, `knowledgeBases` em todas as
respostas de agente, espelho em `apps/workers`. Nenhuma tool, nenhum resolvedor,
nenhuma UI. Suítes: `apps/workers` **174/174**; `apps/api` **258/259**, com a
única falha sendo o flake pré-existente de `AgentDeactivationTests` — **e a
classificação foi feita por baseline**, não por reconhecimento: `git worktree`
limpo em `515a245` reprova exatamente o mesmo teste (237/238). Baseline vermelha
remove a hipótese de regressão e só isso; a causa mecânica já estava achada e
está registrada abaixo, então não houve sintoma promovido a "ambiental".

**A verificação anterior à proposta produziu quatro achados que mudaram o
desenho**, todos lidos no código:

1. **`AllowedTools` existe porque tools MCP não são catálogo persistido** — são
   descobertas ao vivo por `tools/list`. Bases têm id, então não sobra nada para
   uma coluna guardar. Isso transformou "vínculo sem colunas extras" de decisão
   herdada em decisão com causa, e é o que a etapa 4 não deve reabrir.
2. **Não existe rota inversa no repositório.** `McpServerEndpoints` mapeia só
   `/`, `/{id}` e `/{id}/tools`; o card "Agentes que usam este servidor" filtra
   `GET /agents` no cliente. Isso **removeu uma operação CQRS inteira** da
   projeção herdada ("replace + list").
3. **Blast radius de 8 sites** de `AgentResponse.FromEntity`, mais o handler
   novo (nono) e `AgentResponseWireFormatTests` (décimo, que constrói o record
   posicionalmente e quebra a compilação).
4. **Estado inativo não é filtrado no vínculo em nenhum precedente** — o filtro
   vive na resolução (`McpToolSetResolver.cs:52`).

**Duas verificações de guarda que valem além desta change** (convenção 15):

- **A convenção 12 foi demonstrada ao vivo.** Com a chave do fio deliberadamente
  errada (`knowledge_bases` em vez de `knowledgeBases`), os **19 testes de
  endpoint que desserializam para `AgentResponse` passaram** e só o que inspeciona
  o texto do JSON reprovou. É a **prova empírica** da cláusula que
  `agente-enderecos-a2a` escreveu a partir do `a2A`/`a2a` — lá a conclusão veio
  de um defeito real encontrado; aqui o defeito foi injetado de propósito e a
  cegueira foi medida. **Destino: é a referência a citar** quando alguém
  questionar por que existe um teste que lê JSON bruto em vez de desserializar,
  em qualquer change futura.
- **Reintroduzir divergência de schema no espelho exige defeito
  auto-consistente.** Mexer só no `AppDbContext` de `apps/workers` derruba as 21
  classes do teste de espelho em 74 ms — falha de fixture, não de asserção,
  porque o EF barra a migração quando o modelo diverge do snapshot. Para o
  guarda provar o que promete é preciso alterar os **quatro**: `AppDbContext`,
  migração, `AppDbContextModelSnapshot` e o `.Designer.cs`. Aí reprova
  exatamente um teste. **Destino: etapa 4**, que repete esse guarda ao estender
  o espelho e tropeça exatamente aqui — e qualquer change futura que mexa no
  modelo espelhado.

**E um achado sobre o próprio método**: a rodada de verificação de guardas
encontrou um defeito **no teste**, não na produção — uma asserção que ordenava
ids por valor e comparava contra a ordem de criação, flake por construção porque
`Guid` é aleatório. Passou na suíte cheia e reprovou na rodada do guarda. A
verificação da convenção 15 não serve só para provar que o guarda pega o defeito
de produção; ela expõe teste mal escrito de graça. **Destino: este registro é o
que sustenta o custo da rodada de guardas nas próximas changes** — sem ele, a
etapa que estiver com pressa corta a verificação por parecer cerimônia, e é
justamente ela que devolveu um defeito que nenhuma outra etapa do processo
pegaria.

##### Tamanho entregue — terceira medição da série da convenção 18

Headline do commit: **39 arquivos / 2922 linhas**. Trabalho real à mão:
**25 arquivos / 935 linhas** — o headline é **3,1x**, a razão mais extrema já
medida nesta base.

| origem | arquivos | linhas |
|---|---|---|
| Produção (10 novos + 11 modificados) | 21 | 398 |
| **Testes** (1 novo + 3 modificados) | 4 | 537 |
| *(migração gerada ×2 apps)* | *6* | *860* |
| *(artefatos OpenSpec + docs)* | *8* | *1127* |

**Projetado 25 arquivos / 690-870 linhas. Arquivos: exato.** É o primeiro acerto
de contagem de arquivo da série, e o método que produziu foi novo: **projetar
"modificados" separado, a partir do blast radius lido no código**, em vez de
derivar tudo do número de operações CQRS. Os 8 sites saíram com 1 a 3 linhas
cada (3,3,3,3,3,3,2,1), como o perfil previa.

**Linhas: 935, 7,5% acima do topo, e a diferença está inteira nos testes** (537
contra 370-470 projetados; produção veio 398, dentro da faixa). Causa: o custo
de ~19-21 linhas por cenário **subestima cenário que precisa de arranjo
próprio** — os três mais caros desta change (desempate com ordem de inserção
invertida, listagem com três agentes e vínculos cruzados, e os dois `PUT`
vizinhos com catálogo MCP montado) custaram 25-40 linhas cada.

**A migração gerada errou por 4x** (860 contra 150-200 projetadas) porque o
`.Designer.cs` carrega o snapshot **inteiro** do modelo, não só a tabela nova.
Não afeta o trabalho à mão — mas é o que explica a razão de 3,1x do headline, e
é a terceira confirmação da primeira metade da convenção 18.

**O que entrou no `01-ARQUITETURA_E_CONVENCOES.md`**: a entidade
`AgentKnowledgeBase` no modelo de domínio, com as quatro consequências
registradas (sem coluna extra e por quê, inativo dos dois lados, `Cascade` vs. o
`Restrict` de `KnowledgeDocument`, desempate de ordenação), e a leitura causal da
convenção 18 acima.

##### Herdado decidido pelas etapas seguintes

- **Etapa 4**: base inativa **não é oferecida ao agente** — filtro
  `where kb.IsActive` no resolvedor, idioma de `McpToolSetResolver.cs:52`.
  Decidido no `design.md` desta change (D6) e deliberadamente **fora da spec**
  dela, por não ter gatilho verificável aqui; entra lá como *ADDED requirement*.
  Mesma forma que a etapa 1 usou para as três garantias de reindexação.
- **Etapa 5b**: (a) precisa do aviso "base vinculada e inativa explica a
  consequência", no molde do cenário equivalente de `agent-mcp-binding-ui` — é a
  contraparte de produto de aceitar vínculo com base inativa; (b) deriva
  "agentes que usam esta base" **no cliente** a partir de `GET /agents`, como
  `McpServerAgentsCard` já faz. Se descobrir que precisa de rota de API, é
  achado a sequenciar, nunca backend improvisado dentro da change de tela
  (convenção 1, corolário).

##### Etapa 5b — aba Conhecimento no detalhe do agente (aplicada em 2026-09-09)

`frontend-agente-aba-conhecimento` — só `apps/frontend`, consumindo
`PUT /agents/{id}/knowledge-bases`, `GET /knowledge-bases` e
`AgentResponse.knowledgeBases`, todos já implantados. Quarta aba do detalhe do
agente (terceira posição, `?tab=conhecimento`), lista das bases vinculadas,
modal de vincular com busca, estado vazio explicativo e aviso de base inativa.
Suíte do frontend passou de **66 arquivos / 574 testes** para **69 / 617**.

**A decisão que muda o desenho, e que contraria o handoff (convenção 17):** o
vínculo usa **rascunho + `UnsavedChangesBar` + guarda de navegação**, como
`AgentToolsTab` e `AgentDelegationsTab`, e não a requisição por linha que a
regra 4 do handoff pedia — nem a saída que o próprio handoff previa ("manter a
UI por ação enviando o conjunto resultante"). Três motivos, o primeiro sendo o
que decide: **feedback por linha sobre operação de conjunto mente sobre o que
falhou** (se o `PUT` falha, falhou a escrita do conjunto e nada mudou no
servidor); ação por linha sob replace-all **perde escrita concorrente**, e o
protótipo deixa isso acontecer — percorrido ao vivo, os botões das outras linhas
seguem habilitados durante a requisição, então dois cliques rápidos produzem dois
`PUT` calculados do mesmo estado anterior; e as duas outras abas de vínculo do
**mesmo detalhe** já usam este idioma.

**O protótipo foi percorrido, não só lido.** Chrome headless dirigido por CDP,
clique real, captura por estado, nos dois esquemas. Dois defeitos do protótipo
saíram daí e **nenhum estava na prosa do `CONHECIMENTO.md`** — ver a lista de
correções de protótipo acima, que passou de três para cinco.

**E a conferência manual declarou convergência cedo demais — refinamento da
convenção 14.** Quatro rodadas fecharam sem achado novo, e o usuário achou, na
tela dele, uma **largura errada**: o container da aba com `maw={860}` quando o
protótipo deixa o card em largura cheia e limita **a descrição** em 620px, as
duas coisas invertidas. Duas causas, as duas viram regra para a próxima etapa de
UI:

- **A conferência comparou estados, nunca dimensões.** Lista, vazio, aviso,
  modal — todos conferidos; largura e altura, nenhuma vez. Comparar dimensão
  contra o protótipo precisa ser passo explícito.
- **O viewport de captura era mais estreito que o do operador** (1440 contra
  ~1860). Num card de 860 a diferença quase não aparece a 1440 e é gritante a
  1860. Capturar pelo menos na largura em que o painel é usado.

O erro de origem foi **reusar o idioma da aba vizinha sem conferir**: as três
abas do detalhe têm larguras **deliberadamente diferentes** no protótipo —
Delegações 620px (lista de checkbox), Ferramentas e Conhecimento em largura
cheia. `AgentDelegationsTab` está fiel; quem copiou errado foi a 5b. É a mesma
forma da convenção 2 ao contrário: reusar padrão observado é certo, reusar sem
conferir que o caso é o mesmo não.

**Validação manual pelo operador: realizada em 09/09/2026**, depois da correção
de largura. Vale como o dado que a convenção 14 pede — e como reforço dela: a
automação de conferência (Chrome via CDP) achou três defeitos reais em quatro
rodadas e ainda assim **declarou convergência com um defeito visível na tela**.
Ela reduz o número de rodadas humanas; não substitui nenhuma.

**Item aberto tocado:** o gatilho do carve de ordenação foi **corrigido**, não
consumido — ver o item correspondente.

##### Etapa 5a-1 — catálogo de bases na UI (aplicada e arquivada em 2026-09-09)

`frontend-knowledge-base-catalogo` — primeira etapa de UI da linha, só
`apps/frontend`, consumindo as seis rotas que a etapa 1 já entregava. Listagem
com busca sem acento e filtro por estado, detalhe, criação e edição, card de
agentes que consultam a base, coluna `Consultada por`, e o quarto item de
navegação. Arquivada em
`openspec/changes/archive/2026-09-09-frontend-knowledge-base-catalogo/`; spec viva
em `openspec/specs/knowledge-base-catalog-ui/` (10 requisitos, 45 cenários).

Entregue: 52 arquivos / 3078 linhas de código (25 criados / 2949; 27 modificados
/ 129). Suíte do frontend passou de 54 arquivos / 451 testes para **66 / 574**.

**Os protótipos do handoff ficaram anexados na change arquivada**, em `design/`:
`Buteco Agentes.dc.html` (protótipo navegável, com `support.js` ao lado),
`CONHECIMENTO.md` (revisão 2 de 09/09/2026 — a especificação válida das telas das
**quatro** etapas de UI, 5a-1, 5a-2, 5b e 5c) e `README-painel.md`. Dois arquivos
do handoff foram **removidos de propósito** e o motivo está em D15: um mandava
tratar o protótipo como fonte da identidade visual (falso desde
`frontend-tema-identidade-visual`) e o outro especificava quatro coisas contra
decisões já fechadas, entre elas o multipart que a etapa 1 recusou.

**Vinte e duas decisões (D1–D22)**, das quais nove são recusas do protótipo
sustentadas pela convenção 13 — a mais consequente sendo que contagem de
fragmentos e nome de tool de base **não existem em campo nenhum**, e a UI não os
inventa. As quatro rodadas de conferência manual estão registradas em D22, com o
custo de cada uma.

##### "Fora de escopo por dependência" sem conferir a dependência (uma ocorrência)

Duas coisas desta change estavam declaradas **fora de escopo** na proposta — o
card "agentes que consultam esta base" e a coluna `Consultada por` — pelo motivo
de dependerem da derivação a partir de `GET /agents`, tratada como etapa
posterior. A conferê​ncia manual mostrou que a dependência **já estava
satisfeita**: `AgentResponse.KnowledgeBases` é populado desde
`knowledge-base-vinculo-agente`, então as duas custam **uma** requisição, não N.
Entraram (D20 e D21), e a segunda não criou arquivo nenhum — reusou o módulo que
a primeira escreveu.

**A decisão de trazê-las está certa. O que interessa registrar é o padrão, e ele
tem dois lados:**

- **A conferência corrigiu uma exclusão errada.** É o inverso da convenção 1: ela
  protege contra backend improvisado dentro de change de tela, e aqui não houve
  backend nenhum — houve uma dependência **julgada pendente sem verificação**. A
  proposta escreveu "depende de X, fica para depois" sem abrir o código para ver
  se X existia.
- **Essa conferência pertence à proposta, não à conferência manual.** Deu certo
  desta vez porque o custo era uma requisição. Se a dependência realmente não
  existisse, a mesma descoberta na mesma hora teria produzido ou uma change
  inchada com backend improvisado, ou trabalho jogado fora.

**Não vira convenção ainda, e o motivo é que o repositório tem exatamente uma
ocorrência — e um contra-exemplo que a delimita.** O caso que parecia o segundo
não é: o card "Protocolo A2A" também foi removido do protótipo por dependência
ausente, mas ali a dependência **estava mesmo ausente** — `AgentResponse` não
tinha os endereços, e `agente-enderecos-a2a` foi proposta para acrescentá-los,
sequenciando backend antes de UI exatamente como a convenção 1 manda. Ou seja: o
repositório tem **um caso certo** (A2A: dependência conferida, ausente,
sequenciada) e **um errado** (esta change: dependência não conferida, presente,
excluída à toa). O contraste é o que dá o formato da regra futura, se ela vier.

**Gatilho para virar corolário da convenção 1:** a segunda ocorrência de exclusão
por dependência não verificada. O corolário candidato, já escrito para não
precisar ser reconstruído: *"fora de escopo por dependência" exige conferir a
dependência no código, do mesmo jeito que "precisa de backend" exige sequenciar.*

##### Quinta medição da convenção 18 (`frontend-agente-aba-conhecimento`)

| | projetado | entregue | erro |
|---|---|---|---|
| criados | 6 arquivos / ~920 linhas | **6 / 1025** | arquivo **exato**; linhas +11,4% |
| modificados | 8 / ~225 | **8 / 302** | arquivo **exato**; linhas +34% |
| total | 14 / ~1145 | **14 / 1327** | arquivo **exato**; linhas +15,9% |

**Terceiro acerto seguido na contagem de arquivo, e o primeiro em que as duas
metades acertam juntas** (25/25 total em `knowledge-base-vinculo-agente`; 21/21
só nos criados em `frontend-knowledge-base-catalogo`, com os modificados errando
3x; agora 6/6 e 8/8). O método está confirmado para contagem de arquivo: contar
por componente, criados e modificados separados, **em pares com o teste**, a
partir do blast radius lido no código.

**O erro de linha está quase todo em teste, e a causa é contagem de cenário, não
custo por cenário.** Projetados 13 + 7 = **20** cenários nos dois arquivos de
componente; entregues 16 + 12 = **28**. O custo unitário se comportou (aba ~22
linhas/cenário, dentro da faixa de ~25 já registrada; modal ~16, mais barato
porque as asserções compartilham um helper de render). A régua de custo está
calibrada; o que errou foi **quantos**.

**Hipótese para a sexta medição, barata de testar:** a spec desta change tem
**28 cenários** — o mesmo número de testes entregues nos dois arquivos de
componente. A correspondência **não é item a item** (os 5 testes de unidade de
`knowledgeBaseRows` não são cenários de spec, e alguns cenários caíram em
`AgentDetailPage.test.tsx`), então o casamento exato dos totais tem componente de
coincidência. Mas a regra candidata é clara: **projetar linhas de teste a partir
da contagem de `#### Scenario:` da spec, não da intuição.**

**E o obstáculo é de ordem, que é o que esta medição acrescenta à convenção.** A
projeção mora no `design.md`, escrito **antes** da spec. A convenção já manda
projetar depois de fechar a verificação; falta dizer que **a spec é o artefato
que fixa a contagem de cenário, e cenário é o que domina o custo de teste**. Ou
se projeta depois da spec, ou se contam os cenários que as decisões do design já
implicam.

**Um desvio isolado, com causa própria:** `useAgents.test.ts` saiu 51 linhas
contra ~25 projetadas (2x). Foi projetado "um cenário de cache" quando o padrão
da casa é o par sucesso + erro — **convenção 5, que já estava escrita**. Custo de
não aplicar régua existente, não de régua faltando.

**Escopo acrescentado durante a implementação** (não entra na conta do erro de
método): scroll interno do modal e duas correções de truncagem, todas vindas da
conferência manual; mais dois cenários de teste que só existiram quando a
asserção original se mostrou não discriminante.

##### Quarta medição da convenção 18 (`frontend-knowledge-base-catalogo`)

| | projetado | entregue | erro |
|---|---|---|---|
| criados | 21 arquivos / ~2310 linhas | **21 / 2364** | arquivo **exato**; linhas +2,3% |
| modificados | 2 / ~20 | **6 / 99** | arquivo 3x; linhas ~5x |
| total | 23 / ~2330 | **27 / 2463** | arquivo +17%; linhas +5,7% |

**Segundo acerto seguido na contagem de criados** — 21 contra 21, depois dos 25
contra 25 de `knowledge-base-vinculo-agente`. O método (contar por componente,
criados e modificados separados, depois de fechar a verificação) está confirmado
nessa metade.

**Duas coisas novas, as duas sobre a metade dos modificados:**

1. **Todo arquivo modificado arrasta o teste dele.** A projeção leu o blast
   radius no código, como a convenção pede, e ainda errou 3x porque contou os
   arquivos de **produção** tocados (`routes.tsx`, `AppShell.tsx`) e foi cega aos
   testes deles. Os três testes modificados somam 60 das 99 linhas alteradas. É a
   mesma forma do erro que a convenção já registra (projetar por operação CQRS é
   cego aos modificados), um nível abaixo. **Projetar modificados em pares.**
2. **Verificação não pega o que a implementação pega.** A convenção manda projetar
   depois de fechar a verificação, e foi feito — dois componentes da projeção só
   existiram por causa dela. Mas os outros 2 arquivos modificados
   (`DetailHeader.tsx` + teste) vieram da **montagem**: o componente
   compartilhado renderiza "Sem descrição." quando a prop é omitida, e o detalhe
   da base precisava de cabeçalho **sem** subtítulo, porque o que o protótipo
   propunha ali depende dos dois dados que a change não tem. Verificação lê o que
   o código expõe; só a montagem revela o que ele **assume**. A lição não é
   "verificar mais" — é que a faixa de modificados carrega uma incerteza que a de
   criados não tem, e citar as duas com a mesma precisão é falsa confiança. Ver
   D17 do `design.md` daquela change.

**Terceira coisa, e é confirmação de algo que a convenção já previa.** O escopo
mudou no meio (o card de agentes entrou por decisão na conferência), e com ele
veio acrescentar `knowledgeBases` ao tipo `Agent` do frontend, **obrigatório**.
Isso tocou **21 arquivos de teste com uma linha cada** — a fixture ganhando
`knowledgeBases: []`. É exatamente o perfil que esta convenção registrou de
`knowledge-base-vinculo-agente` ("12 arquivos modificados, 11 deles por uma a
três linhas", ao acrescentar campo a um response usado por N handlers), agora
repetido no frontend. **Refinamento:** o blast radius de um campo obrigatório num
tipo compartilhado é a contagem de **fixtures**, não a de componentes — nenhum
componente precisou mudar, 21 arquivos de teste precisaram. Quem projetar isso
lendo o código de produção erra por um fator de 20. O `tsc` enumera de graça.

**Entregue total, com o escopo já mudado:** 52 arquivos / 3078 linhas (25 criados
/ 2949; 27 modificados / 129).

**Baseline da suíte do frontend depois desta change:** 66 arquivos / 574 testes
(era 54 / 451).

##### Handoff da etapa 5a-1 (`frontend-knowledge-base-catalogo`, 2026-09-09)

A primeira etapa de UI da linha conferiu o protótipo do handoff de design contra
o código, tela a tela (convenção 17), e o que ela **não pôde implementar** gera
requisito para as etapas seguintes. Os protótipos ficaram anexados em
`openspec/changes/.../frontend-knowledge-base-catalogo/design/` — `CONHECIMENTO.md`
revisão 2 é a especificação válida das telas das quatro etapas.

**Para a etapa 2 (indexação):**

- **`FailureReason` legível por operador**, não exceção crua. O campo já existe
  (`string?`, nos dois responses de documento); o que falta é a decisão de que
  texto ele carrega. A tela de documentos mostra o motivo **completo, sem
  truncar** — é a única cópia de falha que ela tem.
- **Contagem de tentativas e instante da última tentativa persistidos.** O
  protótipo diz "429 nas três tentativas, a última em 01/09/2026 às 03:14", e
  nenhum dos dois existe em `KnowledgeDocument`. É política de retry com contador
  e carimbo, não formatação de tela.
- **Campo de contagem de fragmentos.** Não existe em lugar nenhum da API:
  `grep -ri "fragment\|chunk"` em `apps/api/src` e `apps/workers/src` só acha
  comentários sobre a fragmentação futura. A coluna `Fragmentos` da 5a-2 não tem
  o que exibir em **nenhum** dos quatro estados até a etapa 2 entregar o campo.
- **Rota de reindexação de documento.** `KnowledgeDocumentEndpoints` tem `POST /`,
  `GET /`, `GET /{id}`, `PUT /{id}` e `DELETE /{id}`, mais nada. O botão
  "Reindexar documento" do protótipo não tem rota. É backend, e pela convenção 1
  não nasce dentro de change de tela.

**Para a 5a-1 de dados (a etapa 2 também destrava):**

- **Contagem de documentos e resumo de indexação em `KnowledgeBaseResponse`** (ou
  rota de resumo). Sem isso as colunas `Documentos` e `Indexação` do catálogo e a
  quarta opção do filtro (`Com falha`) não podem existir: hoje os documentos
  vivem em `GET /knowledge-bases/{knowledgeBaseId}/documents`, uma requisição
  **por base**, contra as 100+ bases que o handoff declara como volume real. O
  catálogo entregou duas colunas (Base, Estado) e três opções de filtro, com
  asserção negativa na spec para impedir que alguém as "complete" com zero.

**Correções de protótipo — lista viva, alimenta a revisão 3 do handoff.**

São **cinco**, achadas em duas etapas diferentes. As três primeiras saíram da
5a-1 e valem para a 5a-2; as duas últimas saíram do percurso do protótipo feito
ao propor a 5b (`frontend-agente-aba-conhecimento`) e valem para **qualquer**
etapa que leia o modal de vincular — o protótipo continua sendo a fonte da 5a-2
e da 5c, e defeito que fica só na spec de uma change é reproduzido pela
seguinte.

- **`0 fragmentos` → célula vazia.** O protótipo faz
  `st === 'failed' ? '0 fragmentos' : '—'`, ou seja zera no estado `failed`. Isso
  contraria a spec viva de `knowledge-document-catalog`: informação derivada da
  indexação é exibida quando `indexedAt` não é nulo e **omitida** quando é nulo,
  nunca zerada — zerada afirmaria que a indexação rodou e não achou nada.
- **A frase "documentos muito grandes tendem a bater no limite" sai.** É conselho
  que o sistema não verifica: nada correlaciona tamanho com falha de embedding, e
  a própria semente do protótipo aplica essa cópia a um documento pequeno
  (convenção 13). Não vira nem texto estático de ajuda.
- **O upload é `FileReader` + corpo JSON, não `multipart/form-data`.** O prompt de
  implementação do handoff pedia rota `/documents/upload`, `FormData` e modificar
  o `request<T>` de cada feature para omitir `Content-Type` — contra a **D3 da
  etapa 1, "Zero multipart, com gatilho registrado"**: markdown e `.txt` são
  texto, o cliente lê com `FileReader` e envia string no mesmo corpo JSON.
  Multipart obrigaria a mexer no `Content-Type` fixo de cada `request<T>`, mais
  `IFormFile`, mais validação de tipo binário, mais limite separado.
  **Gatilho para multipart nascer:** o primeiro tipo de origem binário (PDF),
  junto com o extrator que precisa dos bytes — não antes.
- **A busca do modal de vincular normaliza acento.** O protótipo compara com
  `indexOf` cru sobre `nome + descrição`: percorrido ao vivo, `cardapio` devolve
  "Nenhuma base corresponde à busca." e só `Cardápio` acha. Todas as buscas do
  painel usam `matchesSearch` (`utils/searchText.ts`) desde
  `frontend-listas-busca-e-colunas`, inclusive a do catálogo de bases que a
  própria 5a-1 entregou. Regra que o sistema já escreveu vence protótipo
  (convenção 17). Achado no percurso da 5b, corrigido lá (D6).
- **O modal de vincular marca base inativa.** `Rotinas Internas`, a única base
  inativa da semente, aparece na lista de seleção **sem nenhum sinal** de que
  está desativada; o aviso só nasce depois, na linha da lista de vinculadas. O
  operador vincula às cegas uma base que o agente não vai consultar. É
  convenção 13 **na direção da omissão** — a UI não afirma nada falso, mas
  esconde justamente o dado que decide a ação, e `isActive` está no catálogo que
  a tela já busca. Achado no percurso da 5b, corrigido lá (D5).

Os dois últimos vieram de **abrir o protótipo e clicar**, não de ler a
especificação escrita: nenhum dos dois está na prosa do `CONHECIMENTO.md`. Vale
como método, não só como achado — a revisão 2 foi lida inteira na 5a-1 sem que
nenhum aparecesse.

**Para a 5b, o que já está pronto — e o que a 5a-1 já entregou:**

- A dependência declarada ("deriva agentes que usam esta base no cliente a partir
  de `GET /agents`") **já estava satisfeita**: `AgentResponse.KnowledgeBases`
  existe e é populado desde `knowledge-base-vinculo-agente`. O campo **não é
  opcional** — `knowledgeBases?:` com tratamento de `undefined` não se justifica,
  ao contrário de `a2a?:`, porque os dois lados já implantaram.
- **O card "agentes que consultam esta base" saiu da 5b e entrou na 5a-1**,
  decidido na conferência manual: a dependência estava pronta e custava uma
  requisição, não N. Com ele vieram, prontos para a 5b reusar: o campo
  `knowledgeBases` no tipo `Agent` do frontend (que não existia, e cuja
  obrigatoriedade tocou 22 fixtures em 21 arquivos) e
  `features/knowledge-bases/utils/agentUsage.ts`. A 5b herda a derivação pronta e
  fica com a aba, o modal de vincular e as mutações.
- **A coluna `Consultada por` do catálogo continua fora** — usa a mesma
  derivação, mas não foi pedida.
- O vínculo é `PUT /agents/{id}/knowledge-bases`, **substituição do conjunto
  inteiro**. Não há `PUT` nem `DELETE` por base, ao contrário do que o handoff
  supõe. Vale a saída que o próprio handoff previu: manter a UI de ação por
  linha, enviando o conjunto resultante a cada ação.
- A revisão 2 do handoff mudou o desenho: o vínculo é a **quarta aba** do detalhe
  do agente (`?tab=conhecimento`), a lista mostra **só as bases vinculadas**, e
  vincular é modal com busca que não fecha ao vincular. Motivo registrado lá: a
  coluna direita da Visão geral tem quatro cards (o de A2A entrou depois) e
  empurra qualquer seção inline para fora da dobra.

**Dívida assumida, não introduzida:** `GET /knowledge-bases` não tem `?q=` nem
paginação, então busca e filtro do catálogo rodam no cliente sobre a resposta
inteira. Com as 100+ bases que o handoff declara, essa é a primeira lista do
painel em que a ausência incomoda — pedir `?q=` e paginação ao backend, e trocar
a filtragem local por requisição com debounce.

#### Etapa `0b` — head-to-head semântico (2026-09-10)

Rodada de medição, não change: nada em `apps/`, corpus e vetores fora do
repositório, em `~/.cache/buteco-agents/kbtest-0b/`. Corpus de domínio escrito
para a medição — 11 documentos de política de atendimento e cobrança, 42,8 KB,
44 fragmentos pelo chunker previsto (cabeçalho + merge-up até ~900, teto 1600;
média medida 1021 caracteres, 4 fragmentos por documento). 16 queries com alvo e
5 sem alvo nenhum no corpus. **Bar de recall declarado e gravado antes da
primeira chamada de embedding** — ≥60% entregável, 40-59% chunking obrigatório,
<40% redesenho.

**O resultado que fecha a etapa 2:** `qwen3-embedding-8b` (servido pelo mesmo
gateway configurado em `OPENAI_BASE_URL`) faz **R@1 93,8% / recall@5 100% / MRR
0,958**. `nomic-embed-text-v1`, que é o que `EMBEDDING_MODEL` aponta, faz 87,5%
de recall@5 — **empate com o lexical**, e derrota em R@1 (62,5% contra 68,8%) e
MRR. A correção da formulação "embedding vencer" está registrada acima, na
própria linha que a afirmava.

**Dois mecanismos foram descartados por número, não por preferência.** A
**união** lexical ∪ vetorial não acrescenta nada sobre o qwen (100% para os
dois) — só salva o nomic, que é o modelo a não usar; não há razão para construir
híbrido. E a **reescrita de query** piorou *todos* os modos, contrariando os +16
pontos que a medição anterior lhe creditava: o lexical cai de 68,8% para 50,0%
de R@1. Some-se que a reescrita é frágil como etapa — o modelo devolveu
`inadimplencia aumentoaposexigenciaentrada`, palavras fundidas, numa das 21.

**O mecanismo do "não encontrei" está decidido: devolver sempre os k com a
distância explícita, e o agente decide** (convenção 13). Não é default por
empate. `ts_rank_cd` foi confirmado como inservível para limiar — numa pergunta
que o corpus não responde ele devolveu `1,0000`, a pontuação de melhor aparência
possível. As distâncias de cosseno do nomic **sobrepõem** entre queries com e sem
alvo; as do qwen, **sobre estas 5 negativas**, separam — e é só isso que a
amostra sustenta. A queda relativa entre 1º e k-ésimo não separou em nenhum
modelo.

> **Correção (convenção 9), registrada por `0c` em 2026-09-10:** a afirmação que
> estava escrita aqui — "as do qwen **separam**", sem qualificar a amostra — está
> **errada como generalização**, e a causa é a amostra: **5 negativas**. Com 20
> negativas e um índice de 110 fragmentos, o qwen **não separa**: a pior positiva
> fica a 0,5154 e a negativa mais próxima a 0,3865, e **16 das 83 positivas têm
> topo mais longe que a negativa mais próxima**. As médias continuam separando
> (0,3357 contra 0,4742); o que não sobrevive é a folga entre os extremos, que é
> o que um limiar precisa. A ressalva "5 não calibram corte" estava certa e
> ficou curta: com 5 amostras o mínimo do conjunto sem alvo é uma estatística de
> extremo, e ela subiu ao ganhar 15 pontos novos. **Consequência: o item "limiar
> absoluto volta à mesa na etapa 2 com 50-100 negativas reais de log" fecha com
> 20, e fecha contra o limiar** — nenhum corte serve (em 0,42 descarta 10,8% das
> consultas boas para barrar 85% das sem-alvo; em 0,45 descarta 7,2% e deixa
> passar 30%). O mecanismo já decidido — devolver os k com a distância explícita
> e o agente decide — deixa de ser default por empate e passa a ter evidência a
> favor.

**Três medições curtas fecharam a etapa**, todas sobre o cache, sem reembedar o
corpus:

- **`subvector` existe em pgvector 0.8.6**, com overloads para `vector` e
  `halfvec`, e tanto ele quanto `l2_normalize` são `IMMUTABLE`. Verificado na
  extensão instalada: coluna **gerada** `halfvec(3072)` a partir de
  `vector(4096)` é criável, indexável por HNSW, o plano usa o índice, e a
  derivada nasce preenchida e normalizada. **Isso fecha o item de schema:
  guardar `vector(4096)` cheio** e derivar a coluna indexável por SQL quando o
  índice fizer falta — sem chamar o gateway e sem reembedar. Truncar desde o
  primeiro dia só seria obrigatório se a função não existisse.
- **Latência de embedding de query única** — o item de custo/latência que
  continuava aberto, e que bate em toda chamada de tool da etapa 4. Medida no
  regime da consulta (chamada única, não em lote), 84 chamadas por modelo:
  `qwen3-embedding-8b` **p50 37 ms / p95 46 ms**, `nomic-embed-text-v1` p50 36 ms
  / p95 62 ms. Empate no p50, e o modelo maior tem a **cauda mais curta**.
  **Latência não é restrição e não desempata os modelos** — ao lado da segunda
  ida ao LLM, que custa segundos, o embedding da query é ruído. Para consulta
  esparsa há penalidade de primeira chamada no qwen (154-302 ms, acima dos 135 ms
  do máximo quente), mas ela **não cresce com o tempo parado** — 302 ms com 30 s
  de intervalo contra 154 ms com 180 s, o inverso de um cold start de
  carregamento. No nomic os valores cabem dentro da cauda quente. Com uma amostra
  por intervalo não dá para separar cold start de variância; o que se orça é o
  pior caso observado, **~300 ms**, sem lhe atribuir causa. Orçamento da etapa 4:
  ~40 ms típicos, ~50 ms de p95, ~300 ms de pior caso.
- **A terceira distribuição de query — a de produção.** As duas medidas antes
  ("natural", perguntas de humano; "reescrita", reescrita agressiva) não são o
  que a etapa 4 recebe: lá quem formula a query é o modelo chamando a tool. As 21
  queries foram reemitidas por **tool call real**, a partir da `Description` de
  uma base plausível (21 de 21 chamaram a tool). **É achado, não confirmação:** o
  lexical cai *entre* as duas distribuições, como se esperava, mas os **três
  modos vetoriais caem abaixo de ambas**. No qwen o recall@5 sobrevive — segue
  100% — mas **R@1 cai de 93,8% para 75,0%** e o MRR de 0,958 para 0,859. É 75%
  que entra no orçamento da etapa 4, não 93,8%. Em compensação a folga do
  "não encontrei" cresce **dentro desta amostra de 5 negativas**: 0,1053 contra
  0,0154 da distribuição natural. **Não generaliza, e `0c` mediu que não
  sobrevive a 20 negativas** — ver a correção acima.

**Ameaça à validade que precisa acompanhar a tabela de truncagem:** R@1 idêntico
de 512 a 4096 dimensões **não é evidência de que truncar preserva qualidade** —
é evidência de que o benchmark não discrimina nessa faixa, com 44 fragmentos e
recall saturado (top-5 é 11,4% da base). A conclusão pode estar certa, Matryoshka
é real e o qwen3 é treinado com ela, mas **o dado colhido aqui não a sustenta**.
Ler como "não observei perda", nunca como "não há perda". É o argumento mais
forte a favor de guardar 4096 cheio: a dimensão da derivada pode ser revista
depois, com um benchmark que discrimine, sem reembedar.

**Correção do custo de armazenamento.** A projeção registrada — ~60 MB por 7.500
fragmentos — **estava certa**; faltava dizer a que dimensão se referia (1536).
Medidas as três opções com linhas reais em `pgvector/pgvector:pg18`:
`vector(4096)` são **123 MB**, `halfvec(3072)` são **60 MB** e `vector(1536)` são
**60 MB**. E aqui um número que se pretendia corrigir estava errado: **`halfvec`
de 3072 não ocupa 45 MB**. `halfvec` é de 2 bytes por dimensão, então 3072 × 2 =
6.144 bytes é exatamente o mesmo que 1536 × 4 = 6.144 — **payload idêntico**,
truncar para 3072 em meia precisão não economiza nada sobre 1536 em precisão
cheia. Os 45 MB são o payload cru comparado contra um baseline que já incluía
overhead de linha e página, misturando métodos na mesma conta. Corrigido em
`01-ARQUITETURA_E_CONVENCOES.md` e em `docs/architecture.md`. Detalhe colhido
junto: pgvector marca a coluna de vetor como `external`, então **todo vetor vai
para TOAST em qualquer dimensão** — TOAST não distingue as opções.

**Ameaças à validade da rodada inteira, registradas para quem citar os números:**
recall@5 satura (44 fragmentos, top-5 é 11,4% da base — daí três modos empatarem
em 87,5% e só R@1/MRR os separarem); corpus e queries foram escritos pela mesma
mão, o que **infla o lexical** por sobreposição de vocabulário, e explica ele ter
feito 87,5% aqui contra 47-52% no corpus de arquitetura; 5 negativas não calibram
limiar; e 16 queries fazem cada uma valer 6,25 pontos de recall.

Relatório completo, com os gráficos de distribuição:
https://claude.ai/code/artifact/87bcc0a4-05d2-4cda-9965-e84beba10b92

#### Etapa `0c` — chunker corrigido, medido (2026-09-10)

Segunda rodada de medição, não change: nada em `apps/`, corpus e vetores em
`~/.cache/buteco-agents/kbtest-0c/`. Ela existe porque a exploração da etapa 2
reproduziu que **o chunker medido em `0b` não sobrevive a documento real**.

Corpus de 40 documentos e 168 KB — 11 herdados de `0b`, 29 escritos aqui —,
**110 fragmentos** contra os 44 de `0b`: top-5 cai de 11,4% para 4,5% da base.
83 queries com alvo e **20 negativas** (contra 16 e 5). Emissor das queries de
produção: `nvidia-llama-3.3-70b-instruct-fp8` por tool call real, 103 de 103.

**Mudança de método que a comparação exigiu:** o alvo passou a ser **âncora
textual** — substring distintiva da passagem que responde, verificada como
ocorrendo exatamente uma vez em exatamente um documento. O índice de fragmento
que `0b` usava é inutilizável quando o que se compara são chunkers, porque as
fronteiras mudam entre os braços.

##### Os três invariantes, que é onde a rodada é conclusiva

| | chunker de `0b` | corrigido |
|---|---|---|
| **I1** documento não-vazio → ≥1 fragmento | **3 violações de 40** | **0** |
| **I2** nenhum fragmento acima do teto, medido no texto emitido | **2 violações**, pior 2.858 caracteres | **0**, máximo 1.591 |
| **I3** cobertura de parágrafos | **367 de 393 (93,4%)** | **393 de 393 (100%)** |

As três violações de I1 são exatamente as formas previstas: `.txt` corrido sem
cabeçalho, `#` sem `##`, e `#` + `###` pulando o `##`. Dos 26 parágrafos
perdidos em I3, **4 são preâmbulo** entre o `#` e o primeiro `##` — o terceiro
defeito, isolado.

O efeito no recall torna isso concreto: o chunker de `0b` tem **15 queries
inalcançáveis de 83** — 11 nos três documentos que ele descarta inteiros, 4 que
só o preâmbulo responde. Nenhum ajuste de busca as alcança. O corrigido tem
**zero**.

**Estes três vão para a spec da etapa 2a, e só eles.** I1 e I2 são absolutos,
sem faixa; I3 é 100% dos parágrafos, tolerando linha em branco e marcação de
estrutura, nunca parágrafo. Os três já reprovam contra o chunker anterior, com
número — guarda antes de existir, que é o que a convenção 15 pede.

##### Os parâmetros 900/1600 NÃO vão para a spec

Separar isto dos invariantes é decisão, não descuido. Os invariantes são
**propriedades verificáveis** do resultado: nenhum documento sem fragmento,
nenhum fragmento acima do teto, nenhum parágrafo perdido. Já `TARGET_MIN = 900`
e `HARD_MAX = 1600` são **constantes de produto**, e esta rodada não mediu
nenhuma alternativa a elas — não houve braço com 600/1200 nem com 1200/2400. O
que a medição sustenta é que o chunker corrigido não perde conteúdo, não que
900/1600 seja o ótimo.

Afirmar em spec um número que não foi medido é exatamente o padrão que a
convenção 10 nomeia: requisito que passa verde sem provar nada.

**Ficam como constantes nomeadas em `apps/workers`, fora da spec, com o gatilho
registrado: remedir quando houver corpus real de operador com volume.** Pela
convenção 2, a opção de configuração nasce no dia em que alguém precisar de
outro valor — não antes, e não "para o caso de". Referência para comparar
quando esse dia chegar: média de 985 caracteres por fragmento e 2,75 fragmentos
por documento, medidas aqui.

Mesmo idioma do `AgentDelegationToolOptions`, que não é vinculado a seção de
configuração de propósito porque os defaults são constantes de produto.

**O que separa isto dos invariantes, dito de uma vez:** invariante é
**propriedade do chunker** que a spec afirma e o teste reprova; 900/1600 é
**parâmetro que a medição não otimizou**. Misturar os dois faria a spec afirmar
um número tão bem fundamentado quanto um chute.

##### Overlap: rejeitado, e não por margem

Regra declarada antes de medir: adotar só com **ganho ≥ 5 pontos de R@1 sem
perder R@5**. Medido, o overlap **perde 20 a 23 pontos de R@1** — nas duas
direções e nas duas magnitudes.

| estratégia | R@1 | R@3 | R@5 | MRR | R@1 por documento |
|---|---|---|---|---|---|
| corrigido + cabeçalho de tabela repetido | **41,0%** | **71,1%** | 77,1% | **0,583** | **55,4%** |
| corrigido, overlap zero | 36,1% | 69,9% | **80,7%** | 0,551 | 45,8% |
| chunker de `0b` | 18,1% | 38,6% | 47,0% | 0,311 | 37,3% |
| overlap por sufixo, 150 | 16,9% | 31,3% | 33,7% | 0,263 | 34,9% |
| overlap por prefixo, 300 | 15,7% | 41,0% | 45,8% | 0,307 | 34,9% |
| overlap por prefixo, 150 | 13,3% | 38,6% | 42,2% | 0,277 | 34,9% |

Duas armadilhas foram removidas **antes** de concluir, porque mediriam a coisa
errada: a primeira implementação cortava no meio de frase (refeita para
sentenças inteiras) e só testava prefixo (acrescentado o braço de sufixo, que
anexa a cabeça do fragmento seguinte). Os dois consertos mantiveram o
resultado. Mecanismo provável: com fragmentos de ~1.000 caracteres, 150-300
vindos de outra seção são 13-27% de diluição, e o vizinho mais próximo perde
poder de discriminação.

**O gatilho registrado desde a etapa 1 — "revisitar overlap quando a medição de
recall mostrar perda em fragmentos de fronteira" — está respondido pelo avesso:
o overlap é que causa a perda.** Overlap zero deixa de ser escolha por
parcimônia e passa a ser resultado.

##### Fragmento-atrator: defeito novo, e a mitigação é parcial

`0b` não tinha tabela no corpus. Este tem, e apareceu isto: **um único
fragmento foi o top-1 de 32 das 83 queries (38,6%)**. É a continuação da tabela
de códigos de retorno — uma laje de linhas `| 1008 | débito em recuperação
judicial | … |` sem prosa e **sem a linha de cabeçalho da tabela**, que ficou
no fragmento anterior. Sua distância média a todas as queries é 0,3706, contra
mediana 0,5428 do índice. Ele venceu perguntas sobre tom de voz, sobre
empréstimo a pessoa idosa e sobre terceiro autorizado.

Um bloco de muitas entradas curtas e heterogêneas produz vetor perto do
centroide do vocabulário do domínio, e ganha vizinho mais próximo em consultas
com que não tem relação nenhuma.

Mitigação medida — repetir a linha de cabeçalho da tabela em cada pedaço:

| | sem a correção | com a correção |
|---|---|---|
| top-1 do fragmento mais atrator | 32/83 (38,6%) | **10/83 (12,0%)** |
| menor distância média do índice | 0,3706 | **0,4365** |
| R@1 | 36,1% | **41,0%** |
| R@1 por documento | 45,8% | **55,4%** |
| MRR | 0,551 | **0,583** |
| R@5 | 80,7% | 77,1% |

**A mitigação é parcial, e isso precisa ser lido junto com o número:** 12% de
top-1 num índice de 110 fragmentos é **mais de treze vezes** o ~0,9% que um
fragmento teria num índice sem atrator. O fenômeno foi reduzido, não eliminado.
A correção ataca **um sintoma de tabela**; a causa raiz é sobre densidade e
heterogeneidade, não sobre tabelas, então **o defeito volta a valer para
qualquer bloco denso e heterogêneo que não seja tabela** — glossário, lista de
códigos em texto corrido, índice remissivo, tabela de preços em parágrafo.
Nesses, não há cabeçalho para repetir. R@5 ainda piora 3,6 pontos com a
correção.

**E vale registrar por que isto não apareceu antes: `0b` era estruturalmente
incapaz de encontrá-lo.** O corpus dele não tinha nenhuma tabela — 11
documentos de prosa. Não é que a rodada anterior tenha olhado e não visto; é
que o instrumento não continha o caso. É o argumento mais concreto a favor de
corpus de medição cobrir **formas** de documento, e não só assuntos.

**Gatilho para voltar ao assunto:** base real com documento de catálogo, tabela
de códigos ou glossário — e o sintoma a procurar é o mesmo fragmento aparecendo
no topo de consultas sem relação. Se aparecer, o caminho é fragmentar lista por
grupos menores de entradas, não repetir mais cabeçalho.

##### `k` = 5, pela regra declarada antes

R@3 sobre R@5 = 71,1/77,1 = **92,2%**, abaixo dos 95% que a regra exigia para
`k = 3`. Fica **`k = 5`**, com o custo registrado para a etapa 4 revisitar com
log real: os dois resultados a mais compram 6,0 pontos de recall por 67% mais
contexto em toda chamada de tool.

##### A linha do Gemini fecha sem medir

O argumento declarado em `0b` para testá-lo era `output_dimensionality` honrado
de verdade resolver o bloqueio de índice do pgvector sem truncar no cliente.
`subvector` em coluna gerada resolve isso dentro da infra interna. **O motivo
para testar deixou de existir** — não é que o Gemini seja pior, é que a pergunta
que ele responderia já está respondida. Junto com ele fecha a decisão de mandar
corpus de cobrança e de dados cadastrais para uma API externa, que não precisa
mais ser tomada.

##### O bar não foi atingido, e ele não vai ser movido

Bar declarado às 01:57, antes de gerar corpus, escrever chunker ou chamar
embedding: **R@1 ≥ 70%** na distribuição de produção; 60-69% aceitável só com
R@3 ≥ 90%; **abaixo de 60%, a iteração de chunking não está fechada**.

**Medido: 41,0%.** Pela regra declarada, esta rodada **não certifica o recall
do chunker**.

**Por que o bar estava mal calibrado**, escrito aqui em vez de virar ajuste
retroativo: ele foi ancorado nos 75% de R@1 que `0b` mediu na distribuição de
produção, e `0b` mediu sobre 44 fragmentos de 11 documentos tematicamente
distintos, num benchmark que o próprio relatório declarou **saturado**. O corpus
desta rodada foi construído de propósito para ser confundível — os documentos
`35`-`40` cobrem matéria vizinha de `01`-`05`, o `26` é controle temático do
`24`/`25`, os `12`-`22` compartilham vocabulário. **Bar e corpus foram
desenhados na mesma rodada, e o corpus saiu mais difícil do que o bar supôs.**

**A decisão é não mover o bar**, e o motivo é que mover bar depois de ver
número destrói o instrumento: um bar que se ajusta ao resultado não reprova
nada, e as duas rodadas seguintes não teriam como confiar no que a anterior
declarou. O custo dessa disciplina é ter uma rodada que fecha cinco decisões e
reprova na sua própria métrica primária, e esse custo está sendo pago aqui.

**O precedente, formulado para ser citado: bar calibrado contra benchmark
saturado não transfere para benchmark discriminante.** Os 75% de `0b` mediam um
instrumento que já não separava os modos; herdá-los como exigência num
instrumento que separa é comparar duas coisas que não são a mesma escala.

Desdobrado, para a próxima rodada que declarar bar — é para isto que este
parágrafo existe:

1. **Bar e corpus não podem ser desenhados na mesma rodada sem um controle.**
   Se o corpus é novo, o bar precisa ou ser calibrado num corpus já medido, ou
   vir acompanhado de um braço de referência que rode nos dois.
2. **Bar ancorado em número de benchmark saturado não é bar.** Os 75% vieram de
   uma medição cujo próprio relatório dizia que o instrumento não discriminava
   mais; herdar o número sem herdar a ressalva é o erro.
3. **Declarar, junto com o bar, o que o invalida.** Aqui teria bastado escrever
   "este bar pressupõe corpus de dificuldade comparável ao de `0b`; se o corpus
   novo for mais confundível, o bar não é comparável" — e a rodada teria
   reprovado com a leitura certa desde o começo.

##### Achado que não é ameaça: 44,6% das consultas erram até o documento

Estava enterrado na lista de ameaças à validade e é maior do que uma ameaça —
é um dado sobre **onde a dificuldade mora**. A lente por documento pergunta
outra coisa que o R@1 por fragmento: o topo veio do documento que contém a
resposta? Medido no melhor braço, **55,4% sim, 44,6% não**.

A consequência muda o diagnóstico: se quase metade das consultas erra o
**documento**, o problema não é granularidade de fragmento, e **nenhum ajuste
de chunking o resolve** — nem tamanho, nem overlap, nem fronteira de cabeçalho.
É recuperação escolhendo o assunto errado, não o pedaço errado do assunto
certo.

**Gatilho: etapa 4**, e a consequência lá é direta. O agente escolhe **qual
base consultar** antes de qualquer busca, pela `Description` da base, e esse
roteamento entre bases **não tem nenhuma medição** — nem em `0b`, nem aqui.
Errar o documento dentro de uma base em 44,6% dos casos torna a pergunta sobre
roteamento entre bases mais séria, não menos: se a recuperação já confunde
assuntos vizinhos dentro de um corpus homogêneo, escolher entre 100+ bases pela
`Description` é um problema da mesma família e nunca foi olhado.

##### Reranker: hipótese com dado a favor, e escopo novo

Não é possibilidade genérica que se levanta no fim de todo relatório de
recuperação. Tem número atrás: **R@5 de 77,1% contra R@1 de 41,0%** significa
que, em três de cada quatro consultas, a resposta **está entre os cinco** e o
que falha é a **ordenação**. Ordenar melhor um conjunto pequeno já recuperado é
exatamente o que um reranker faz.

Observação colhida junto, que não obriga a nada: `bge-reranker-v2-m3` e
`qwen-qwen3-reranker-8b` já estão servidos pelo mesmo gateway configurado na
variável de ambiente do provedor — não haveria dependência nova de
infraestrutura.

**Fica como item em aberto, com gatilho: se o recall do topo incomodar em uso
real.** É escopo novo — segunda chamada de modelo por consulta, latência,
alçada de custo —, não iteração de chunking, e por isso não entra na etapa 2a
nem justifica segurá-la.

##### Ameaças à validade, para quem citar os números

- **Corpus deliberadamente confundível.** É o que faz R@1 discriminar entre
  estratégias, e é o que derruba o valor absoluto. Uma base real de 40
  documentos sobre assuntos distintos daria número maior sem que nada tivesse
  melhorado. Use as **diferenças entre braços**, não os absolutos.
- **Corpus e queries escritos pela mesma mão** — mesmo viés que `0b` registrou.
  Compartilhado por todos os braços, então não distorce a comparação.
- **Âncora textual é métrica estrita:** 33,7% das queries têm a âncora no rank
  2-3. Mas a lente por documento mostra que **44,6% das consultas erram até o
  documento**, então a dificuldade não é só granularidade.
- **O controle de tamanho de corpus tem 8 queries.** Nas mesmas 8, o índice de
  44 fragmentos dá R@1 50,0% e o de 110 dá 37,5%: direção confirmada,
  magnitude não — cada query vale 12,5 pontos.
- **20 negativas fecham o limiar, não calibram corte.** Bastam para mostrar que
  a separação de `0b` não sobrevive; não bastam para escolher um valor.
- **Metade do corpus escreve número por extenso** (herança de `0b`) e metade em
  dígitos. Não afeta a comparação entre braços vetoriais; afetaria uma
  comparação lexical, que esta rodada não fez.
- **A mais séria: o chunker corrigido foi escrito e medido na mesma rodada,
  sem braço independente que o valide.** Quem escreveu o chunker escreveu o
  corpus, as queries e as âncoras. Os invariantes escapam disso porque são
  verificáveis por inspeção do resultado, e reprovam contra um chunker que a
  rodada não escreveu; o **recall**, não — ele carrega essa ameaça inteira, e é
  mais um motivo para não afirmá-lo em spec.

##### O que a etapa 2a herda

**Seis coisas fechadas**, e nenhum número de recall a afirmar em spec:

1. Os **três invariantes** (I1, I2, I3), com os valores acima — é o que a spec
   afirma e o que o teste reprova.
2. **Overlap zero**, por medição, com o gatilho antigo respondido pelo avesso.
3. **`k` = 5**, com o custo registrado para a etapa 4 revisitar.
4. **Nenhum limiar**, agora com evidência contra a alternativa e não por
   empate — ver a correção de `0b` acima.
5. **Tabela markdown repete o cabeçalho em cada pedaço**, com o resíduo de 12%
   declarado como mitigação parcial.
6. **900 e 1600 como constante em `apps/workers`, com gatilho** — fora da
   spec, pelo motivo da seção própria.

**Aberto, e é o que a etapa 2a NÃO deve tentar fechar:** um número de recall
que autorize afirmar parâmetros de fragmentação; o roteamento entre bases
(etapa 4); e o atrator residual.

A decisão de aceitar o chunker pelos invariantes, deixando os parâmetros fora
da spec, foi tomada com o argumento que a própria rodada formulou: **a medição
provou que o chunker não perde conteúdo; não provou que 900/1600 é o ótimo.**
A alternativa considerada e recusada era remedir num corpus de dificuldade
realista para obter número comparável ao bar — recusada porque um corpus
deliberadamente mais fácil produz número maior sem nada ter melhorado, o que é
calibrar o teste ao bar em vez do contrário.

### Etapa 2a — indexação de documentos (`knowledge-base-indexacao`, 2026-09-11)

O consumidor que a etapa 1 declarou que não existia. Os valores `Indexing`,
`Indexed` e `Failed` do enum, e as colunas `IndexedAt` e `FailureReason`,
nasceram sem escritor lá de propósito; é esta change que os escreve.
`ContentRevision`, criada na etapa 1 e sem consumidor desde então, ganha o dela.

O que entrou: entidade `KnowledgeFragment` com `vector(4096)` e três colunas de
proveniência; fragmentador corrigido; resolvedor de gerador de embedding; fila
própria com política de tentativas; `ContentHash`, `FragmentCount`,
`IndexingAttempts` e `LastAttemptAt` em `KnowledgeDocument`; e a checagem de
integridade do índice no boot — quinto caso da convenção 8, e o primeiro desta
base que faz I/O no boot.

#### Os três mecanismos que "cobre de graça" escondia

A D7 da etapa 1 afirmava que `ContentRevision` "cobre o delete de graça". A
verificação V4 desta change contestou pela leitura do código, e os testes de
concorrência **confirmaram por medição** — reintroduzindo dois defeitos
diferentes e vendo o que cada um derruba:

| defeito reintroduzido | o que reprova |
|---|---|
| removida a condição `ContentRevision == ...` do commit | **só** o teste de atualização — o de exclusão passa, porque a linha já não existe e a atualização afeta zero linhas de qualquer jeito |
| removida a checagem de `linhas afetadas == 0` | **os dois** — sem ela o caso de exclusão segue para o `INSERT` e estoura na chave estrangeira |

São **três** mecanismos, não um: `ContentRevision` cobre a **atualização**; a
checagem de zero linhas cobre a **exclusão**; e quem impede fragmento órfão é a
**chave estrangeira em `Cascade`** — que precisou ser `Cascade` e não `Restrict`,
senão excluir documento indexado passaria a falhar, regressão direta da D6 da
etapa 1.

**A leitura que vale mais que o conserto: "cobre de graça" foi a forma de
afirmação que escondeu três mecanismos sob um nome.** A frase é econômica e soa
como economia de desenho; o que ela economizou foi a distinção. E foram
necessários **dois defeitos reintroduzidos diferentes** para separá-los — um só
teria confirmado a metade que ele exercita e deixado a outra parecendo coberta.

#### Guarda correto reprovando pelo motivo errado

A primeira execução dos testes da checagem de startup reprovou dois de seis. O
guarda estava certo: o defeito era **isolamento de fixture** — o fixture é por
classe, os testes compartilham o mesmo Postgres, e fragmentos de um teste
anterior faziam o seguinte cair no caminho de "mais de uma combinação" em vez do
que ele queria exercitar.

É parente próximo da quinta forma da convenção 15, e vale registrar o par:
**estado contaminado** e **estado insuficiente** produzem o mesmo tipo de
resultado — algo que parece medição e não é. A vacuidade sobre tabela vazia faz o
guarda passar sem exercitar nada; a contaminação entre testes faz ele reprovar
sem que o código esteja errado. Os dois se resolvem pela mesma pergunta: *este
arranjo coloca o sistema no estado que eu penso que coloca?*

#### O que os guardas mediram

Todos foram **vistos reprovar**, não deduzidos:

- **Invariantes do fragmentador** — reimplementado o fragmentador de `0b` e posto
  no lugar: I1 dá **0 fragmentos** onde o atual dá 1; I2 dá um fragmento de
  **7.226 caracteres** contra teto de 1.600 (pior que os 2.858 que `0c` mediu,
  porque aquele corpus tinha tabela menor); I3 descarta o preâmbulo.
- **Guarda de zero fragmentos** — com a guarda, 15 de 15 passam; removida,
  reprovam **exatamente 2**, os dois do arquivo dela. Os 12 invariantes não se
  movem, e a separação é **estrutural**: aqueles instanciam o fragmentador real
  diretamente e nunca veem a substituição.
- **Checagem de integridade** — os quatro cenários de divergência rodam sobre
  índice **povoado**, com um teste afirmando essa precondição.

#### Fechamento: como a suíte foi comparada, e a dúvida que ficou

Baseline e fechamento medidos com o **mesmo procedimento de carga** — `uptime`
antes de cada alvo, espaçamento entre eles, nada em paralelo.

| alvo | baseline | fechamento | delta |
|---|---|---|---|
| `libs/ProviderCatalog.Tests` | 6/6 | 6/6 | — |
| `apps/api` | 275/276 | **287/288** | +12 testes, mesma reprovação |
| `apps/workers` | 174/174 | 211/212 → **212/212** na repetição | +38 testes |
| `apps/inbox` | 164/164 | 164/164 | — |
| `tests/CrossApp…` / `RoundTrip` | 2/2, 4/4 | 2/2, 4/4 | — |
| `apps/frontend` | 617/617 | 617/617 | — |

A única reprovação de `apps/api` é a mesma da baseline: o flake de ordem de
execução de `AgentDeactivationTests`, já registrado como item aberto.

**A reprovação única de `apps/workers` NÃO foi resolvida, e fica registrada como
dúvida residual em vez de fechada.** O que se sabe:

- ela aconteceu **uma vez**, com a carga dentro do limiar (4,26);
- a repetição com a máquina mais descarregada (2,52) passou **212/212**, com a
  saída completa guardada em
  `~/.cache/buteco-agents/kb-2a-fechamento/workers-fechamento-completo.log`;
- o **nome do teste se perdeu**, porque aquela rodada passou por um filtro que só
  capturava o resumo — erro de instrumentação, corrigido na convenção 19 e na
  tarefa de fechamento, mas irrecuperável para este caso;
- a evidência circunstancial aponta contenção: os quatro pares de containers
  novos são desta change, e o perfil (reprovação única, em bloco de classes de
  host) é o que `WorkerHostCollection` documenta.

**O que não dá para afirmar: que foi contenção.** Pode ter sido teste
intermitente escrito por esta change, e sem o nome não há como descartar.
Registrar isso como "ambiental confirmado" seria exatamente o erro que a
convenção 19 nomeia — baseline vermelha não absolve ninguém, e uma reprovação
sem nome absolve menos ainda.

**Por que não se caçou o evento:** forçar carga para reproduzir é perseguir algo
que não se controla, e "não reproduziu em cinco tentativas" não elimina a
dúvida — só a torna mais barata de ignorar. A rodada única com saída completa
foi feita; o valor dela está na próxima vez, não nesta.

#### Oitava medição da convenção 18

Projetado **51 arquivos / ~3.305 linhas** de código; entregue **83 arquivos /
3.963 linhas** de código (artefatos OpenSpec e migração gerada fora da conta,
como manda a convenção).

| categoria | criados | modificados |
|---|---|---|
| produção | 23 arq / 1.770 li | 14 arq / 338 li |
| teste | 11 arq / 1.594 li | 26 arq / 128 li |
| configuração (build, compose, env) | — | 11 arq / 135 li |
| **código** | **34 arq / 3.364 li** | **49 arq / 601 li** |
| *documentação (`.md`)* | *—* | *4 arq / 374 li* |
| *migração gerada* | *4 arq / 1.058 li* | *2 arq / 146 li* |
| *`openspec/`* | *6 arq / 1.247 li* | *—* |

**As linhas erraram por 20%; os arquivos erraram por 63%.** E são erros de
naturezas diferentes, que é o que importa registrar.

**As linhas quase acertaram**, e pelo motivo certo: 3.364 linhas criadas contra
as ~3.270 projetadas para criados. O método por componente funciona quando o
componente é código novo — a projeção tinha o chunker, o consumidor, o serviço,
o resolvedor e os testes, e cada um custou perto do previsto.

**Os arquivos erraram, e as três causas são estruturais, não escala:**

1. **A pergunta errada da V1, medida em dois níveis.** Ela contou
   `PostgreSqlBuilder` — a **imagem** — e concluiu 11 sítios. O que a mudança
   alcançava era `UseNpgsql` — o **provider** —, e depois disso ainda alcançava
   `UseInMemoryDatabase`, que a segunda varredura também não pegou. Duas vezes a
   mesma forma: verificação correta respondendo à pergunta errada, a segunda
   **depois** de a regra sobre ela já estar escrita na convenção 6. Custo: 26
   arquivos de teste modificados que a projeção não tinha.
2. **Sítio e arquivo não são a mesma unidade.** A correção do `UseVector` foram
   ~38 **sítios** em 24 **arquivos** — vários sítios por arquivo. A projeção
   contava sítios e a entrega conta arquivos; comparar os dois números como se
   fossem o mesmo esconde que a estimativa de trabalho estava certa e a de
   arquivos, não.
3. **Blast radius de ligar um caminho que antes não existia.** Ao publicar na
   fila, **12 testes que existiam antes desta change** passaram a reprovar, e o
   conserto (duplo do publisher no fixture) é arquivo que nenhuma projeção por
   componente teria previsto — ele não pertence a componente nenhum da change.

**Não há fator de correção a extrair daqui, e é deliberado não inventá-lo.** As
três causas são sobre *o que a mudança alcança*, não sobre *quanto ela custa*.
Um multiplicador esconderia justamente a pergunta que precisa ser feita antes:
**"o que exatamente esta mudança toca?"** — e ela se responde por varredura, não
por estimativa.

**A documentação foi 374 linhas em 4 arquivos**, quase toda no `01` e no `02`, e
fica fora da conta de código por consistência com as sete medições anteriores —
mas vale dito que ela é ~10% do esforço, e que a convenção 18 nunca a mediu.

#### O limiar de carga envelheceu, e é achado diferente do que o item previa

No fechamento, `apps/workers` reprovou **1 de 212** — e a carga estava **dentro
do limiar declarado**: load 4,26 contra o teto de 5,0. A máquina não estava
suja. **A suíte é que ficou mais pesada por dentro.**

Esta change levou a `WorkerHostCollection` de **7 para 11** classes, todas as
quatro novas subindo host: `KnowledgeIndexingTests`,
`KnowledgeIndexingConcurrencyTests`, `KnowledgeIndexingZeroFragmentGuardTests` e
`EmbeddingIndexConsistencyTests`. Cada uma tem `IClassFixture<WorkerInfrastructureFixture>`,
ou seja um par próprio de containers Postgres + RabbitMQ.

| rodada | classes de host | load na largada | duração | resultado |
|---|---|---|---|---|
| baseline (antes da change) | 7 | — | 5 m 24 s | 174/174 |
| fechamento | 11 | **4,26** | 4 m 57 s | **211/212** |
| repetição | 11 | 2,52 | 3 m 48 s | **212/212** |

**O item aberto previa outra coisa.** Ele dizia que, entrando classe nova na
coleção, *"o sintoma desaparece de novo sem a causa ser tratada"* — ou seja,
esperava que a serialização escondesse o problema. O que aconteceu foi o
contrário: o sintoma **apareceu**, e quem falhou foi o **limiar**, que estava
calibrado para 7 classes e não cobre 11.

**A leitura generalizável, que é o que vale guardar: um limiar de carga externa
não cobre uma suíte que ficou mais pesada por dentro.** São duas dimensões
diferentes, e o número media só uma. Qualquer limiar calibrado contra um estado
do sistema precisa de **gatilho de recalibração quando o sistema muda** — senão
ele continua sendo citado com a autoridade de um número medido, sobre um sistema
que já não é o que foi medido.

**É a segunda ocorrência da mesma família, e a primeira está em `0c`:** *"bar
calibrado contra benchmark saturado não transfere para benchmark
discriminante"*. Agora na forma *"limiar calibrado contra suíte de N classes não
transfere para N+4"*. As duas são o mesmo mecanismo — **referência medida sobre
um estado, citada depois que o estado mudou** —, mas duas ocorrências não fazem
convenção: ficam como dois registros, com **gatilho para promover ao `01` na
terceira**.

#### Handoff para a 2b

- **Rota de reindexação de documento** — a fila e o publisher já existem; falta a
  rota que publica nela.
- **Resumo de indexação por base** — a forma (campo em `KnowledgeBaseResponse`
  contra rota própria) precisa ser decidida **com a tela na mão**: a 5a-1
  registrou que a coluna de documentos exigiria uma requisição por base, contra
  as 100+ bases que o handoff declara.
- **O `FakeKnowledgeIndexingJobPublisher` já está no fixture de `apps/api`** — a
  2b herda a capacidade de afirmar "enfileirou" e "não enfileirou" sem broker.

## Itens em aberto, registrados conscientemente (não esquecidos)

Cada um tem gatilho de quando revisitar:

- **Migração de banco sai só de `apps/api`** — `apps/api` e `apps/workers`
  compartilham o mesmo Postgres e têm migrações próprias que criam as
  **mesmas** tabelas, com IDs diferentes e sem `MigrationsHistoryTable`
  separada. As de `apps/workers` são ferramental de design-time: nada em
  runtime as aplica (o único uso de `Database.` em `apps/workers/src` é o
  lock consultivo de `ConversationContextLock`; não há `MigrateAsync`,
  `GetPendingMigrations` nem `EnsureCreated`), e o `deploy/migrate/Dockerfile`
  builda bundle só de `apps/api` e `apps/inbox`. Elas só rodam contra
  Testcontainer descartável. **Nunca rodar `dotnet ef database update` a
  partir de `apps/workers`**: verificado contra um Postgres limpo, `apps/api`
  aplica com sucesso e `apps/workers` em seguida falha com
  `42P07: relation "agents" already exists`; o `__EFMigrationsHistory` fica
  com apenas os IDs de `apps/api`, porque a tentativa reverte inteira e não
  deixa linha — a falha é ruidosa e não corrompe estado. Registro em vez de
  checagem de startup porque não há checagem barata viável (nada em runtime
  pergunta por migrações pendentes ali). Gatilho: qualquer mudança que toque
  as migrações de `apps/workers`, ou um segundo app passar a compartilhar o
  mesmo banco.
- **~~Ordenação sem desempate, em cinco sites de `apps/api`~~ — FECHADO por
  `ordenacao-desempate-listas-vinculo` (09/09/2026).** O registro abaixo fica
  como estava, com **duas correções que a change teve de fazer nele**:

  1. **Eram oito sites, não cinco.** Os cinco listados existem e estavam nas
     linhas certas; a varredura achou mais três em `apps/api` — o sexto que o
     próprio item mandava conferir (`ListAgentsQueryHandler.cs:21`) e dois
     inéditos: `ListMcpServersQueryHandler.cs:14` e
     `ListKnowledgeDocumentsQueryHandler.cs:33`.
  2. **A premissa sobre os catálogos estava errada.** O item afirma que a
     listagem de bases diverge por ordenar por `CreatedAt`. Não diverge: os
     **quatro** catálogos de `apps/api` ordenam por `CreatedAt`, e quem ordena
     por nome são as três consultas de vínculo. O padrão da casa é catálogo em
     ordem de cadastro, vínculo em ordem de leitura — e o comentário de classe
     de `ListKnowledgeBasesQueryHandler` já dizia isso ("mesmo comportamento de
     `ListMcpServers`"). A "decisão" que o item pedia não existia. Causa
     registrada como acréscimo à convenção 6.

  E a change achou um defeito de **classe diferente**, que o item não
  descrevia e que o desempate não conserta: `GET /agents` ordenava
  `mcpServers`/`delegatesTo`/`knowledgeBases` em memória (comparador do .NET)
  enquanto `GET /agents/{id}` ordena em SQL (collation do Postgres). Os dois
  discordam — medido —, então as duas rotas podiam devolver o mesmo agente em
  ordens diferentes **sem empate nenhum**. Corrigido movendo a ordenação para a
  consulta (design.md, D3).

  Registro original:

- **Ordenação sem desempate, em cinco sites de `apps/api`** — achado durante a
  revisão da proposta de `knowledge-base-vinculo-agente`, pré-existente e não
  introduzido por ela. **Linhas reconferidas em 09/09/2026** — as registradas
  antes (`:34` e `:51`) já estavam defasadas, e um carve que as consumisse iria
  ao lugar errado:

  | # | Site | Ordena por |
  |---|---|---|
  | 1 | `AgentDelegations/AgentDelegationLookup.cs:23` | `agent.Name` |
  | 2 | `AgentMcpBindings/AgentMcpServerLookup.cs:22` | `joined.McpServer.Name` |
  | 3 | `Agents/Queries/ListAgents/ListAgentsQueryHandler.cs:36` | `binding.McpServer.Name` |
  | 4 | `Agents/Queries/ListAgents/ListAgentsQueryHandler.cs:53` | `delegation.TargetAgent.Name` |
  | 5 | `KnowledgeBases/Queries/ListKnowledgeBases/ListKnowledgeBasesQueryHandler.cs:20` | `knowledgeBase.CreatedAt` |

  **Sexto site a conferir junto, de severidade menor:**
  `ListAgentsQueryHandler.cs:21` ordena a listagem inteira por
  `agent.CreatedAt`, que também não é único por construção — mas a colisão exige
  dois agentes criados no mesmo tick.

  **O molde da correção já existe no repositório**, escrito pela etapa 3 e com o
  motivo no código: `AgentKnowledgeBindings/AgentKnowledgeBaseLookup.cs:31-32` e
  `ListAgentsQueryHandler.cs:75-76` fazem `OrderBy(Name).ThenBy(Id)`. O carve é
  estender esse mesmo par aos cinco sites acima.

  **Nome não é único em
  nenhum catálogo desta base**: verificado que o `AppDbContext` de `apps/api`
  não tem nenhum índice único de nome (os únicos `HasIndex` são
  `a2a_tasks.ContextId`, `a2a_tasks.State` e
  `knowledge_documents.KnowledgeBaseId`) e que nenhum handler de criação valida
  nome duplicado. Logo a ordem entre homônimos é a que o plano do PostgreSQL
  devolver, e a mesma requisição pode responder em ordens diferentes sem nada
  ter mudado no cadastro. É a mesma classe de defeito que
  `dedupe-global-nome-de-tool` corrigiu em `McpToolSetResolver.cs:47`, com **duas
  diferenças que baixam a severidade**: lá a consequência era semântica (mudava
  qual tool mantinha o nome-base no dedupe), aqui é de apresentação; e **nenhuma
  spec existente promete ordem** para `mcpServers` ou `delegatesTo` — conferido,
  a palavra não aparece em `agent-catalog`, `agent-mcp-binding` nem
  `agent-delegation-binding` —, então é não-determinação silenciosa, sem
  contrato violado. Não corrigido de carona em
  `knowledge-base-vinculo-agente` (convenção 12: defeito pertence a quem expõe e
  se corrige em change própria); lá o vínculo novo já nasce com
  `.ThenBy(kb => kb.Id)` e com o desempate **na spec**.

  **Gatilho CORRIGIDO em 09/09/2026, ao aplicar a etapa 5b.** O gatilho
  registrado era "a etapa 5b, que renderiza as três listas lado a lado".
  Conferido no código, **a premissa é falsa** e a 5b não disparou nada:

  - **As três listas não ficam lado a lado.** São três abas, e
    `AgentDetailPage` monta com `keepMounted={false}` — o conteúdo das outras
    abas não existe no DOM.
  - **A aba de conhecimento não renderiza nenhuma das duas ordenações
    instáveis.** Ela usa `agent.knowledgeBases`, que **já tem** `ThenBy(Id)`, e
    ordena o rascunho pelo mesmo critério no cliente. Os contadores das outras
    abas são `length`, não ordem.

  Sequenciar um carve de `apps/api` antes de uma change de tela que não depende
  dele seria inflar escopo por gatilho que não disparou. O quinto site apareceu
  nesse mesmo percurso: o modal de vincular da 5b renderiza a ordem de
  `ListKnowledgeBasesQueryHandler` ao operador — e mesmo assim se corrige em
  change própria, não na change de tela (convenção 12: defeito pertence a quem
  expõe).

  **GATILHO ATUAL: imediato — change própria, a ser proposta.** O gatilho
  reescrito em 09/09/2026 ("uma tela que renderize `mcpServers` ou
  `delegatesTo` em lista") apontava para uma tela que ninguém planeja, o que na
  prática é o mesmo que não ter gatilho: item aberto sem gatilho não é
  revisitado. Fica em aberto **por decisão de sequenciamento**, não por falta de
  diagnóstico — o diagnóstico está completo acima.

  **O teste já foi desenhado, e o carve o herda em vez de reinventar.** A etapa 3
  escreveu `KnowledgeBasesWithEqualNames_AreTieBrokenByIdDeterministically` em
  `AgentKnowledgeBindingEndpointsTests.cs:354`, com três decisões que valem para
  os cinco sites:

  - **A asserção é sobre ordem crescente de id, nunca sobre "duas consultas
    devolvem a mesma ordem".** A segunda forma é asserção sobre
    não-determinação: passa com o defeito presente sempre que o plano do
    Postgres calhar de ser estável, que é exatamente o perfil dos guardas que
    esta base já teve de consertar (convenção 15).
  - **A inserção é feita em ordem deliberadamente oposta à de id**, para a ordem
    "natural" do banco não coincidir por acidente com a esperada.
  - **O guarda fica separado do teste de ordenação geral**, para que remover o
    `ThenBy` reprove ESTE e não aquele — guarda que reprova os dois está
    afirmando a garantia no componente errado (convenção 15, segunda metade).

  Cada um dos cinco sites tem duas superfícies a cobrir onde couber, como o
  teste da etapa 3 faz: a rota de item (`GET /agents/{id}`) e a de listagem
  (`GET /agents`).
- **39 das 43 capabilities estão com `Purpose` placeholder** — registrado em
  09/09/2026, ao escrever o `Purpose` de `agent-knowledge-binding-ui` depois do
  archive da 5b. O texto do placeholder é
  `TBD - defined by change <nome>. Update Purpose after archive.`, ou seja
  **carrega a própria instrução do que fazer com ele** — e foi ignorado 39
  vezes. Isso é evidência de que o mecanismo não funciona: "escrever depois do
  archive" é um passo sem dono, que acontece quando a change já foi encerrada e
  ninguém está mais olhando.

  Têm `Purpose` real hoje apenas quatro: `agent-knowledge-binding`,
  `inbox-message-orchestration`, `knowledge-base-catalog-ui` e
  `agent-knowledge-binding-ui`. As duas últimas são as duas etapas mais recentes
  da linha de bases de conhecimento — ou seja, o hábito começou a se formar, e é
  isso que dá o gatilho abaixo em vez de uma change de mutirão.

  **Por que importa:** spec viva sem `Purpose` é spec que a próxima pessoa lê
  sem saber **qual pergunta ela responde**. Os requisitos dizem o que o sistema
  faz; o `Purpose` diz por que a capability existe e o que a distingue das
  vizinhas — que é exatamente o que não se recupera lendo os cenários.

  **Gatilho: toda change que criar ou modificar uma spec viva escreve o
  `Purpose` dela na mesma passada**, antes do archive e não depois. Custa
  minutos, e quem está mexendo na capability é quem sabe responder. Não vira
  change de mutirão: 39 `Purpose` escritos de uma vez por quem não tocou o
  código de nenhuma delas produziria prosa genérica, que é pior que o
  placeholder porque parece preenchido. **Corolário:** o placeholder deixa de
  ser aceitável em spec nova — quem cria a capability escreve o `Purpose`.

- **`AgentDeactivationTests` depende da ordem de execução** — achado durante
  `knowledge-base-catalogo-documentos`, pré-existente e não relacionado a ela.
  `AgentDeactivationFixture.TaskJobPublisher` é uma instância única
  compartilhada pela classe (`IClassFixture`) e `PublishedMessages` acumula;
  três testes afirmam `Assert.Empty(...)` sobre ela, e
  `SendMessage_AfterReactivation_PublishesNormally` publica de verdade. Quem
  rodar depois dele falha. Isolado, cada teste passa. É guarda que aprova ou
  reprova por sorte de ordenação, que é o defeito que a convenção 15 nomeia.
  Gatilho: imediato — carve de defeito pré-existente, no mesmo idioma de
  `inbox-enums-json-string` e dos dois encoders de codec. Correção provável:
  limpar a coleção por teste, ou trocar `Assert.Empty` por contagem relativa.
- **`.env` local com nomes obsoletos de provedor de LLM** — usa
  `ChatClient__BaseUrl`/`ChatClient__ApiKey`/`ChatClient__Model`, mas
  `ChatClientOptions.SectionName` é `"OpenAI"` e `Program.cs` de
  `apps/workers` faz `GetSection("OpenAI")`. O `.env.example` (linhas 57-58) e
  o `docker-compose.prod.yml` (80-81) já usam os nomes certos. As três
  entradas do `.env` local não bindam em nada. Gatilho: imediato, change
  própria pequena.
- **Handoff de UI de bases de conhecimento (etapa 5a)** — três divergências
  do protótipo, todas decididas e a corrigir na tela, não no backend
  (convenção 17): (a) o rodapé do modal promete atomicidade ("2 documentos
  serão criados") que o desenho de N chamadas independentes não sustenta;
  (b) o contador do modal conta **caracteres** enquanto o teto validado é em
  **bytes UTF-8** — o campo exposto é `contentLengthBytes`, e é ele que deve
  ser comparado ao limite; (c) confirmação antes de excluir documento, que é
  a contraparte de produto do risco R6 (a exclusão é real e irreversível, e
  não tem contraparte técnica na etapa 1).
- **Garantias de reindexação herdadas pela etapa 2** — substituição integral
  (nunca diff de fragmento), fragmentos antigos sobrevivendo até a
  reindexação terminar com sucesso, e falha preservando os antigos. Decididas
  em `knowledge-base-catalogo-documentos` (`design.md`, D9) e deixadas **fora**
  da spec daquela change de propósito, por não terem gatilho verificável lá.
  Entram como ADDED requirements da etapa de indexação, com cenário real. A
  etapa 2 não as redecide; se divergir, corrige D9 com a causa (convenção 9).

- **`TARGET_MIN = 900` / `HARD_MAX = 1600` são constante de produto, não
  requisito de spec** — `0c` mediu que o chunker corrigido não perde conteúdo,
  **não** que 900/1600 seja o ótimo: não houve braço com outra configuração de
  tamanho. Vão para o código como constantes nomeadas, no idioma de
  `AgentDelegationToolOptions`; o que a spec da etapa 2a afirma são os três
  invariantes (I1, I2, I3), que são propriedades verificáveis do resultado.
  **Gatilho: remedir quando houver corpus real de operador com volume.** Pela
  convenção 2, a opção de configuração nasce no dia em que alguém precisar de
  outro valor. Referência de comparação: 985 caracteres por fragmento e 2,75
  fragmentos por documento, medidos em `0c`.

- **Fragmento-atrator: mitigado em parte, causa raiz aberta** — `0c` achou um
  fragmento que era top-1 de 32 das 83 queries (38,6%), sem relação com elas:
  uma laje de linhas de tabela sem prosa e sem cabeçalho. Repetir o cabeçalho
  da tabela em cada pedaço derruba para 10/83 (12,0%) — mas **12% num índice de
  110 fragmentos ainda é mais de treze vezes o ~0,9% de um índice sem
  atrator**, e a causa raiz (bloco de entradas curtas e heterogêneas vira vetor
  de centroide) continua valendo para qualquer lista longa. R@5 piora 3,6
  pontos com a correção. **Gatilho:** base real com catálogo, tabela de códigos
  ou glossário — o sintoma a procurar é o mesmo fragmento no topo de consultas
  sem relação. Se aparecer, o caminho é fragmentar lista em grupos menores de
  entradas, não repetir mais cabeçalho.

- **Roteamento entre bases de conhecimento nunca foi medido** — `0c` mediu que
  **44,6% das consultas erram até o documento** dentro de um único corpus
  homogêneo. Na etapa 4 o agente escolhe **qual base consultar** antes de
  qualquer busca, pela `Description` da base, e essa escolha não tem nenhuma
  medição — nem em `0b`, nem em `0c`. Se a recuperação já confunde assuntos
  vizinhos dentro de um corpus, escolher entre as 100+ bases que o handoff
  declara como volume real é problema da mesma família. **Gatilho: etapa 4**,
  ao desenhar a tool — a pergunta a responder é se a `Description` basta para
  rotear, e ela precisa de medição, não de raciocínio.

- **Reranker: hipótese com dado a favor, não levantada por completude** —
  `0c` mediu R@5 de 77,1% contra R@1 de 41,0%: em três de cada quatro
  consultas a resposta está entre os cinco e o que falha é a **ordenação**, que
  é o que um reranker ataca. `bge-reranker-v2-m3` e `qwen-qwen3-reranker-8b` já
  estão servidos pelo mesmo gateway do provedor de embedding, então não haveria
  dependência nova de infraestrutura. **É escopo novo** — segunda chamada de
  modelo por consulta, latência e custo —, não iteração de chunking.
  **Gatilho: se o recall do topo incomodar em uso real.**

- **Recall do chunker não certificado** — `0c` reprovou no próprio bar
  declarado (R@1 41,0% contra os 70% exigidos), e o bar **não foi movido**. O
  diagnóstico registrado é má calibração do bar, não defeito do chunker: os
  invariantes passaram todos e as formas antes inalcançáveis passaram a
  recuperar na média do corpus. **Gatilho:** medir num corpus de dificuldade
  realista (documentos sobre assuntos distintos, sem a confundibilidade que
  `0c` injetou de propósito), com braço de referência que rode nos dois
  corpora — sem esse braço, o número novo não é comparável nem com este nem
  com o de `0b`.

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
- ~~**Testes do frontend falhando de forma pré-existente e não-determinística**
  (`AgentForm`/`ChannelForm`/`McpServerForm.test.tsx` e as páginas de
  criação/edição que os usam — timing de `userEvent` embaralhando texto
  digitado ou perdendo timeout numa navegação)~~ — **resolvido por
  `agente-enderecos-a2a`** (D8 e tarefa 4.6), que o fechou de arrasto por
  estar impedindo verificar a própria change. A causa era **prazo, não
  lógica**: os testes de formulário abrem dropdowns que montam em portal
  depois de uma transição, enquanto 54 arquivos rodam em paralelo, e o
  prazo padrão de **um segundo** das consultas assíncronas do Testing
  Library acabava antes de a opção existir. O prazo das consultas subiu
  para cinco segundos e o do teste para quinze.

  **Este item ficou obsoleto sem ser riscado** desde 06/09, porque a
  correção entrou dentro de uma change de outro assunto. A contagem
  registrada aqui antes (14 numa sessão, 15 na baseline nomeada
  pós-archive) era a assinatura do não-determinismo, e a leitura de que
  não tinha relação com `apps-workers-contexto-temporal` estava certa.

  Confirmado por medição em duas sessões: **cinco execuções completas
  verdes** logo depois da correção (`notes.md` de `agente-enderecos-a2a`)
  e **quatro** em `main` limpo ao propor
  `frontend-knowledge-base-catalogo` — 54 arquivos / 451 testes —, nove no
  total. A medição é confirmação, não a causa: baseline verde não promove
  sintoma a resolvido (convenção 19).
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
  `apps/inbox/tests`. **Mesma família que o item `WorkerHostCollection` de
  `dedupe-global-nome-de-tool`** (abaixo): fixture de teste construindo host mais
  cedo, ou mais vezes, do que devia. Os dois provavelmente são uma change só. O
  terceiro da família — a sensibilidade da suíte de `apps/frontend` a contenção
  de CPU **externa** — está registrado junto do `WorkerHostCollection`, e tem
  mecanismo oposto: ali o recurso é construído pela própria suíte, aqui é
  disputado por outro processo.
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
- **`Contact.DisplayName` fora das `Instructions` do agente**
  (`inbox-contexto-canal`, design.md D1) — decisão de escopo com gatilho,
  não Non-Goal esquecido: o valor que dependeria de texto livre do
  usuário final (chamar a pessoa pelo nome) não compensou o risco de
  injeção de prompt frente à superfície de dano real (ver item seguinte).
  Alternativa registrada e não implementada: saudação montada em código,
  fora do prompt. Gatilho: surgir um mecanismo de defesa que mude essa
  conta (ex. classificação do `DisplayName` antes de liberá-lo, ou
  desenho que tire tools de escrita do alcance de agentes de
  atendimento).
- **`AgentMcpServer.AllowedTools` não distingue tool de leitura de tool de
  escrita** (achado por `inbox-contexto-canal`, design.md D1, ao mapear a
  superfície de dano de uma eventual injeção de prompt) — um agente
  voltado a canal de atendimento pode ter uma tool MCP de escrita
  vinculada (criar agendamento, mutar CRM externo) sem nenhuma
  distinção de risco no cadastro. Não é defeito desta change, é
  característica do sistema desde `backend-mcp-selecao-tools` — só ficou
  nomeada explicitamente agora. Gatilho: qualquer mudança futura que
  aumente a superfície de texto não confiável no contexto do agente (o
  gatilho de `DisplayName` acima é o primeiro candidato), ou pedido de
  produto por controle de risco por tool.
- **`TemporalContextBlockBuilder.NotAUserMessageMarker` nunca foi testado
  sob conteúdo adversarial** (achado por `inbox-contexto-canal`,
  design.md D1/D5) — é dica textual, não fronteira estrutural; só
  carregou texto 100% gerado pelo sistema até agora (bloco temporal,
  bloco de contexto de canal). Gatilho: antes de qualquer mudança que
  coloque texto não controlado pelo sistema em um bloco de contexto
  delimitado por esse marcador (ou por um análogo).

- **A ordenação de `GET /agents` passa a depender de `Enumerable.GroupBy`
  preservar a ordem de origem** — assumido por `ordenacao-desempate-listas-vinculo`
  (design.md, D3), que move a ordenação de `mcpServers`, `delegatesTo` e
  `knowledgeBases` da memória para a consulta em lote e **remove** o `OrderBy`
  em memória de dentro do `GroupBy`. A partir daí a ordem dentro de cada grupo
  é a ordem em que as linhas saíram do PostgreSQL, e o que a preserva é o
  `GroupBy`.

  **É `System.Linq.Enumerable.GroupBy`, não EF Core.** `bindings` vem de
  `ToListAsync`, então o `GroupBy` de `ListAgentsQueryHandler.cs:32,49,66` é
  LINQ-to-Objects sobre uma `List<>` já materializada — EF Core e Npgsql não
  participam dessa etapa. A dependência é do **runtime .NET**
  (`global.json`, hoje `10.0.301`), não dos pacotes de acesso a dados.

  **E o comportamento é documentado**, o que muda a classe de risco: a
  documentação de `Enumerable.GroupBy` (Microsoft Learn, moniker `net-10.0`,
  revisão de 2026-07-01, em todas as sobrecargas) diz *"Elements in a grouping
  are yielded in the order that the elements that produced them appear in
  `source`"*. Não é comportamento não documentado verificado por medição — é
  contrato publicado de API de primeira parte, **corroborado** por medição
  (2.000 tentativas com fonte pré-ordenada e agentes intercalados, ordem
  preservada em todas; design.md desta change). Isso o separa do item de
  `Pgvector.EntityFrameworkCore` da etapa 2, que é comportamento de terceiro
  **sem** contrato publicado — os dois pedem gatilho no bump, mas por motivos e
  com pesos diferentes.

  **Modo de falha, corrigido:** não é invisível para os guardas desta change,
  como parecia. O `GroupBy` só existe no caminho da **listagem**;
  `GET /agents/{id}` passa por `AgentMcpServerLookup`/`AgentDelegationLookup`/
  `AgentKnowledgeBaseLookup`, que ordenam em SQL puro e não agrupam nada
  (conferido: o único `GroupBy` em caminho de leitura de `apps/api` está em
  `ListAgentsQueryHandler`). Logo uma quebra de ordem no `GroupBy` faz as duas
  superfícies **divergirem**, que é exatamente o que os guardas de R2 afirmam
  não acontecer — eles reprovam.

  O que sobra, e é a razão real de registrar: eles reprovam **por
  probabilidade**. Um `GroupBy` que deixasse de preservar a ordem ainda pode
  emitir a ordem esperada por acaso — com os dois itens do guarda de R2, em
  torno de metade das vezes. É o perfil de guarda que aprova ou reprova por
  sorte de ordenação que a convenção 15 nomeia, e nenhum arranjo de teste desta
  change o elimina, porque o defeito hipotético não é determinístico.

  **Gatilho: bump do runtime .NET (`global.json` / TFM)** — refazer a medição
  antes de aceitar o bump; são minutos e o script está descrito no design.md.
  **Segundo gatilho:** qualquer reescrita que mova o agrupamento para o servidor
  (`GroupBy` traduzido por EF Core) ou que volte a ordenar em memória — a
  primeira troca a premissa de lugar, a segunda reintroduz a divergência de
  comparador que D3 foi corrigir.

  **Não é gatilho:** bump de `Microsoft.EntityFrameworkCore` ou
  `Npgsql.EntityFrameworkCore.PostgreSQL`. Esses tocam um elo diferente da
  corrente — se o `ORDER BY` deixar de sobreviver à tradução, a listagem
  diverge da consulta por id e os mesmos guardas de R2 reprovam, com a mesma
  ressalva probabilística.

- **Uma premissa sobre o próprio código entrou num item aberto sem ter sido
  lida** — achado ao propor `ordenacao-desempate-listas-vinculo`. O item de
  ordenação afirmava que *"o catálogo de MCP e o de agentes ordenam por nome"* e
  pedia decidir se a listagem de bases, que ordena por `CreatedAt`, era acidente
  a corrigir. Lido no código: os **quatro** catálogos de `apps/api` ordenam por
  `CreatedAt` (`ListAgents:21`, `ListMcpServers:14`, `ListKnowledgeBases:20`,
  `ListKnowledgeDocuments:33`), e quem ordena por nome são as três consultas de
  vínculo. Não havia divergência, não havia acidente, e a "decisão" pedida não
  existia — a change quase gastou uma seção de `design.md` decidindo sobre uma
  realidade inventada.

  O dano teve duas etapas, e a segunda é a que importa: a premissa errada saiu
  do item para o prompt da change seguinte, que a repetiu como enunciado. Item
  aberto é lido depois, por quem não tem o contexto de quem escreveu, e é aí que
  ele vira instrução.

  **E a conferência que existia não pega isto.** A rodada anterior conferiu as
  linhas do mesmo item e corrigiu `:34`/`:51` para `:36`/`:53` — ou seja, o item
  passou por revisão, e saiu dela com as linhas certas **e** a frase sobre os
  catálogos errada. Conferir referência não é conferir afirmação.

  Registrado como acréscimo à **convenção 6** (ver
  `01-ARQUITETURA_E_CONVENCOES.md`), e não como item próprio, porque a convenção
  já é sobre exatamente este erro em outros alvos e é onde se olha antes de
  escrever. Gatilho de reavaliação, caso o acréscimo não segure: segunda
  ocorrência de premissa sobre código deste repositório propagada de item aberto
  para change.

- **Ordenação sem desempate em `apps/inbox` — seis sites** — achado pela
  varredura de `ordenacao-desempate-listas-vinculo`, que olhou os três apps.
  Não corrigido junto por convenção 12 (app diferente, banco próprio, specs
  próprias) e porque a forma da spec delta é outra — ver abaixo.

  | Site | Ordena por |
  |---|---|
  | `Channels/Queries/ListChannels/ListChannelsQueryHandler.cs:19` | `channel.CreatedAt` |
  | `Contacts/Queries/ListContacts/ListContactsQueryHandler.cs:14` | `contact.CreatedAt` |
  | `Contacts/Queries/GetContactSessions/GetContactSessionsQueryHandler.cs:23` | `session.StartedAt` |
  | `Contacts/Queries/GetChannelSessions/GetChannelSessionsQueryHandler.cs:28` | `session.LastActivityAt` |
  | `Contacts/Queries/GetChannelSessions/GetChannelSessionsQueryHandler.cs:37` | `message.OccurredAt` (prévia da última mensagem, `FirstOrDefault`) |
  | `Messages/Queries/GetSessionMessages/GetSessionMessagesQueryHandler.cs:23` | `message.OccurredAt` |

  **Severidade menor que a de `apps/api`, e a razão é verificável:** todos os
  critérios são temporais e alimentados por `DateTimeOffset.UtcNow`, conferido no
  `design.md` de `inbox-instante-mensagem` (*"`Message.OccurredAt` em
  `apps/inbox` é sempre o instante de recebimento… os dois adapters chamam
  `DateTimeOffset.UtcNow` inline e sequer desserializam o timestamp que WAHA e
  Telegram enviam"*). O empate exige dois eventos no mesmo microssegundo — não é
  o empate por construção que nome duplicado produz em `apps/api`.

  **Dois deles têm promessa de ordem em spec viva**, e isso muda a forma do
  trabalho: `inbox-contact-session:211,222` ("ordenadas pela mais recente
  atividade primeiro") e `inbox-message-history:176,185` (ordem cronológica). Lá
  a change **conserta requisito violado** (MODIFIED), enquanto em `apps/api`
  acrescentou requisito (ADDED). Misturar as duas formas num carve só foi o
  segundo motivo de deixar `apps/inbox` de fora.

  **Herdar os guardas prontos:** o par comportamental + asserção determinística
  sobre o SQL emitido (convenção 15, quinta forma) já existe em
  `apps/api/tests/.../Support/EmittedSqlCapture.cs` — o equivalente em
  `apps/inbox` é cópia adaptada, não desenho novo. Gatilho: imediato, change
  própria.

- **`ContactSessionResolver.cs:53` NÃO é defeito de ordenação** — registrado como
  **não-achado** para a próxima varredura não o levantar de novo. Ele aparece em
  qualquer busca por `OrderBy` sem `ThenBy`
  (`OrderByDescending(StartedAt).FirstOrDefault()`) e parece o caso mais grave de
  todos, porque a consequência seria semântica: qual `Session` é retomada. Mas
  `apps/inbox` tem índice único **parcial** em
  `sessions."ContactId" WHERE "ClosedAt" IS NULL` (`AppDbContext.cs:100-102`) e o
  `Where` da consulta é exatamente `ClosedAt == null` — no máximo uma linha casa,
  e não há empate possível. O `OrderByDescending` é defensivo e inerte.

- **`A2A/PostgresTaskStore.cs:77` em `apps/api` ordena sem desempate, e com
  `.Take()`** — `OrderByDescending(StatusTimestamp)` seguido de
  `.Take(request.PageSize ?? 50)`. Paginação sobre ordem instável pode **repetir
  ou omitir** linhas, que é pior que ordem trocada, e por isso vale mais que os
  oito sites corrigidos apesar de estar fora do escopo deles. Fora de escopo
  porque o próprio arquivo já registra, no comentário de `ListTasksAsync`, que a
  consulta *"não tem nenhum consumidor em `apps/api` hoje"*. Gatilho: **o
  primeiro consumidor de `ListTasksAsync` em `apps/api`** — o mesmo gatilho da
  lacuna de filtro por `AgentId` já anotada ali; as duas se corrigem juntas.

- **Um terceiro comparador de ordem, em `apps/frontend`** —
  `features/agents/utils/knowledgeBaseRows.ts:26-27` reordena a lista no cliente
  com `name.localeCompare(...)` e `id.localeCompare(...)`, enquanto
  `agent-knowledge-binding-ui:62-66` diz que a interface *"reproduz a ordem que a
  API devolve"*. Reproduzir e recalcular com um terceiro comparador não são a
  mesma coisa, e a spec descreve a primeira. Depois de
  `ordenacao-desempate-listas-vinculo` a API passou a ter **uma ordem só** (a da
  collation do banco, nas duas rotas), o que torna a reordenação no cliente
  desnecessária **e** o único ponto restante onde a ordem pode divergir do que a
  spec promete. Gatilho: imediato — e é change de `apps/frontend`, nunca de
  `apps/api` (convenção 12 na direção inversa).

- **A causa dos 39 `Purpose` placeholder está na instrução do skill de
  sincronização, não em esquecimento de quem sincroniza** — achado em 09/09/2026,
  ao exercer pela primeira vez o gatilho de `Purpose` fixado no mesmo dia. O
  passo 4d de `.claude/skills/openspec-sync-specs/SKILL.md` diz, textualmente:

  > **Create new main spec** if capability doesn't exist yet:
  > - Create `openspec/specs/<capability>/spec.md`
  > - **Add Purpose section (can be brief, mark as TBD)**
  > - Add Requirements section with the ADDED requirements

  Ou seja: o skill **manda** escrever o placeholder, e **não manda procurar um
  `Purpose` na delta**. Quem seguir a instrução ao pé da letra produz
  `TBD - defined by change <nome>` — que é exatamente o texto dos 39. Não foram
  39 esquecimentos; foram 39 execuções corretas de uma instrução errada.

  **O que aconteceu nesta change:** o arquivo vivo
  `openspec/specs/api-response-ordering/spec.md` ficou com o `Purpose` real,
  porque a delta o trazia e quem sincronizou leu a delta em vez de seguir o 4d.
  E `agent-knowledge-binding` (delta `MODIFIED`) teve o `Purpose` preservado —
  diff do arquivo vivo contra o snapshot de antes é **puramente aditivo**, 0
  linhas removidas e 25 acrescentadas. Ou seja, **nenhum dos dois modos de falha
  temidos ocorreu.** Mas o primeiro não ocorreu por sorte de quem executou, não
  porque o mecanismo o impeça.

  **Por que isso muda a formulação do gatilho.** O gatilho de 09/09/2026 foi
  fixado assumindo que *escrever o `Purpose` na delta bastaria*. Não basta: a
  delta pode trazer o `Purpose` e a sincronização escrever `TBD` por cima, se
  quem sincroniza seguir o skill literalmente. O gatilho precisa de duas metades:

  1. **escrever o `Purpose` real na delta da capability nova** (a metade já
     fixada), e
  2. **conferir o arquivo vivo depois da sincronização** — `## Purpose` real, sem
     `TBD - defined by change`. `openspec validate --strict` **não pega isso**:
     placeholder é texto válido, e as 44 specs passam com 39 deles.

  **CORRIGIDO NA FERRAMENTA em 09/09/2026.** `.claude/skills/openspec-sync-specs/SKILL.md`
  passou a mandar usar o `## Purpose` da delta quando houver, e só cair no
  placeholder quando a delta não trouxer nenhum — aí avisando no resumo que
  aquela capability ficou devendo um. Não foi uma linha só; a leitura do skill
  inteiro achou mais três coisas, e o porquê de cada uma está abaixo.

  **Confirmação de que o skill era o único produtor dos 39:** o texto exato dos
  39 é `TBD - defined by change ...`, e essa cadeia **não existe** no CLI nem em
  `.claude/`. O CLI tem um template próprio, com redação diferente
  (`TBD - created by archiving change ...`, em
  `dist/core/specs-apply.js`), e **nenhuma** das 44 specs usa essa redação. Ou
  seja: os 39 foram escritos à mão por agentes seguindo o "mark as TBD" do 4d,
  inventando a redação — e o caminho do CLI nunca disparou aqui. Corrigir o 4d
  para de fato para o crescimento.

  **O que mais mudou no skill, além do 4d:**

  1. **Cláusula de `## Purpose` no passo 4c (capability existente).** O skill não
     tinha *nenhuma* noção de que uma delta pode trazer `## Purpose` — o passo 4
     só falava de requisitos. Sem isso, uma delta `MODIFIED` que traga um
     `Purpose` real para substituir um placeholder seria simplesmente ignorada, o
     que **bloquearia o consumo incremental do estoque de 39** — exatamente o
     plano que o gatilho deste item depende. A cláusula diz: placeholder é
     substituído pelo `Purpose` da delta; `Purpose` real nunca é sobrescrito por
     placeholder.
  2. **Passo de verificação novo (passo 5).** O skill terminava em "Show summary",
     que relata a *intenção*, não o que foi escrito. Agora manda reler os
     arquivos vivos e conferir: sem `TBD` onde a delta trazia `Purpose`, sem
     bloco de requisito duplicado, sem resíduo de `## ADDED/MODIFIED Requirements`,
     e diff aditivo para delta `MODIFIED`. São as quatro conferências que
     `ordenacao-desempate-listas-vinculo` fez à mão. `validate --strict` continua
     sendo necessário e não suficiente — as 44 specs passam com 39 placeholders.
  3. **Segundo produtor registrado no próprio skill.** O CLI cria spec viva
     sozinho quando uma change é arquivada **sem** sync prévio, e o fluxo de
     archive oferece "Archive without syncing". O skill não pode corrigir o CLI,
     então registra a redação alternativa para quem for grepar o estoque, e a
     recomendação de sincronizar antes de arquivar.

  **A resposta à suspeita sobre `MODIFIED`:** conferido, e é o contrário do
  temido. O passo 4c opera só sobre requisitos ("Find the requirement in main
  spec") e nunca sobre a seção de `Purpose`, e o guardrail "Preserve existing
  content not mentioned in delta" cobre o resto. O `Purpose` de
  `agent-knowledge-binding` ter sobrevivido é **propriedade da instrução**, não
  do caso — ao contrário do `Purpose` da capability nova, que sobreviveu por
  sorte de execução.

  **O gatilho depois da correção.** A metade (1) — escrever o `Purpose` real na
  delta da capability nova — continua sendo do autor da change, porque só ele
  sabe responder. A metade (2) — conferir o arquivo vivo depois do sync — deixou
  de ser tarefa de quem lembra e virou passo do skill. **O estoque de 39 não foi
  tocado**, de propósito: 39 escritos de uma vez por quem não tocou o código de
  nenhum produz prosa genérica, que é pior que o placeholder porque parece
  preenchido. Segue consumido incrementalmente, uma capability por change que a
  toque — e agora o passo 4c garante que essa substituição de fato acontece.

### Primeiro deploy em produção (checklist)

**Não existe ambiente de produção hoje** — o projeto está todo em
desenvolvimento. Duas verificações da lista acima não são pendências: são coisas
que só fazem sentido contra dados de produção e que, por isso, ficam agrupadas
aqui em vez de espalhadas. Quem fizer o primeiro deploy vai querer ler as duas
juntas.

1. **Aplicar a migration `AddUniqueOpenSessionIndex`** (`inbox-session-indice-unico`),
   com a query de diagnóstico antes de aplicar e o saneamento retroativo que a
   migration já embute. Detalhes completos no item
   "Aplicação em produção da migration `AddUniqueOpenSessionIndex`" acima —
   incluindo a referência de dev (1 contato com 2 sessões abertas, fronteira de
   inatividade legítima) e o que esperar do resultado.
2. **Rodar o censo de colisão de nome de tool** (`dedupe-global-nome-de-tool`,
   `design.md`, V8 — o SQL está lá). Em dev, com todos os agentes reais do
   sistema, o resultado foi **zero colisões**: 12 nomes, todos distintos, o mais
   longo com 41 caracteres. Em produção o cadastro será outro. Se o censo achar
   colisão, **o dedupe já corrige** — as tools deixam de se sombrear —, mas o
   histórico de conversa daquele `contextId` passa a citar um nome que não existe
   mais na lista, que é o item de R7 registrado abaixo. Rodar **antes** do
   primeiro deploy, para saber se R7 tem alvo desde o dia zero em vez de
   descobrir pelo log.

### Abertos pelo redesenho do painel

- **Validar a url pública no startup.** Sem ela configurada, o card de
  descoberta A2A emite endereço relativo como se fosse absoluto, em silêncio —
  a resposta de agente passou a declarar a ausência, mas o card não. Validar no
  startup resolveria de vez, ao custo de mudar o comportamento de inicialização
  de um sistema em uso. Registrado como candidata a change própria no
  `design.md` de `agente-enderecos-a2a`, não enfiado nela.
- ~~**`AgentDeactivationTests.SendMessage_WithPushNotificationConfig_ForInactiveAgent_NeverPublishesJobOrCallsWebhook`**
  ... nunca investigada~~ — **investigada em
  `knowledge-base-catalogo-documentos` (2026-09-07); causa confirmada.** Não é
  ruído de container: é dependência de ordem dentro da classe (fixture com
  `FakeTaskJobPublisher` único e `PublishedMessages` acumulando, contra três
  `Assert.Empty` e um teste que publica de verdade). Isolado, passa. Segue
  aberto porque a **correção** é carve próprio, não porque falta diagnóstico —
  ver "Itens em aberto".
- **Formatação pendente em 18 arquivos do frontend**, todos já assim antes do
  redesenho. Deixados intactos de propósito em todos os commits, para não
  inchar diffs de mudança de comportamento. Duas vezes durante o redesenho um
  `prettier --write` com glob amplo os reformatou por acidente e foi revertido.
- **Densidade compacta/confortável e card "Primeiros passos"** do protótipo
  seguem adiados por decisão de 2026-09-05, nunca propostos.

### Achados de `dedupe-global-nome-de-tool` (2026-09-08)

- **Quarto caso de guarda verde com o defeito presente (convenção 15).** O
  requisito "Distinção de tools com nomes iguais entre servidores diferentes"
  (`mcp-tool-execution`) promete que uma tool não oculte a outra, mas o cenário
  que o guardava usava dois servidores de nomes **diferentes** (`"Servidor A"` /
  `"Servidor B"`). `ToolNameSanitizer` mapeia todo caractere fora de
  `[a-zA-Z0-9_-]` para `_`, então `"Zendesk MCP"` e `"Zendesk.MCP"` produzem o
  mesmo nome — requisito violado, guarda verde. Contador da convenção 15 subiu
  de três para quatro.
- **Segunda forma do mesmo erro (convenção 15): guarda no lugar errado.** Três guardas
  desta change reprovaram contra o defeito **e continuaram reprovando depois da
  correção**, porque afirmavam unicidade dentro de cada resolvedor enquanto a
  correção é global no ponto de concatenação. Reprovar antes não basta: tem de
  reprovar no componente que a correção vai tocar. Registrado na convenção 15.
- **A leitura "pré-existente/ambiental" errou pela terceira vez, e agora virou
  convenção 19.** `TaskJobConsumerTests` reprovando em bloco foi lido como
  "limite pré-existente de contenção de containers" — desmentido por baseline em
  `git worktree` limpo de `6956d79`, que passa 132/132 em paralelo: a 13ª classe
  de host era da própria change. Somado ao `TimeoutException` do
  `InboxOrchestratorRoundTrip` lido como latência do podman e à
  `ObjectDisposedException` de `apps/inbox` lida como corrida de disposal do
  xUnit, são três instâncias medidas — bar suficiente para virar regra
  (convenção 19 no `01-ARQUITETURA_E_CONVENCOES.md`).
  **A convenção carrega as duas metades, porque uma delas veio do próprio
  histórico:** rodar a baseline antes de classificar, **e** não tratar baseline
  vermelha como prova de "ambiental". Foi exatamente isso que a segunda leitura do
  `TimeoutException` (`inbox-instante-mensagem`) fez — bisect em worktree, falha
  idêntica nos dois lados, "causa ambiental confirmada por evidência direta" — e a
  causa real só apareceu na terceira leitura. Baseline verde acusa regressão;
  baseline vermelha não absolve ninguém.
- **`ToolNameSanitizer` não tinha arquivo de teste** apesar de ser compartilhado
  pelos dois resolvedores e ser o sítio da truncagem em 64. O primeiro teste dele
  pegou uma expectativa errada minha na primeira redação (`Sanitize(" tool")` dá
  `_tool`, não `__tool` — a substituição de caracteres roda antes da checagem de
  inicial).
- **O limite de 64 tinha número certo e fonte errada.** Verificado agora em fonte
  primária: `FunctionObject.name` da especificação OpenAPI publicada pelo OpenAI,
  superfície **Chat Completions** (a que `ChatClientResolver.BuildOpenAi` usa).
  A Responses API do mesmo provedor permite 128; o Gemini declara 128; e o
  **Anthropic não publica o seu** — nem nos docs de tool use nem no `ToolParam`
  do SDK. O argumento "mínimo entre os três" está verificado em dois.
- **Defeito de estouro corrigido de passagem:** o dedupe local de
  `AgentDelegationToolSetResolver` fazia `$"{baseName}-{suffix}"` sem
  re-truncar, produzindo 66 caracteres quando a base já estava nos 64.

#### Tamanho entregue vs. projeção por componente (convenção 18)

Diffstat **decomposto**, que é a forma que a convenção 18 exige — o headline
(33 arquivos / +2930) misturaria as três categorias e não serviria de âncora
para nada:

| categoria | projetado | entregue | erro |
|---|---|---|---|
| produção (`apps/workers/src`) | 6 arq. / ~137 | **8 arq. / +285 −24** | +33% arq., +108% linhas |
| teste | 5 arq. / ~600 | **16 arq. / +993 −3** | +220% arq., +65% linhas |
| **subtotal código** | **11 / ~737** | **24 / +1278 −27** | **+118% arq., +73% linhas** |
| artefatos OpenSpec | 6 / ~600 | 7 / +1573 | +162% linhas |
| docs de raiz | não projetado | 2 / +79 −2 | — |

Três causas, todas identificáveis e nenhuma delas "complexidade a mais":

1. **A contagem de arquivos de teste errou por 3x, e por um motivo mecânico que
   a projeção não tinha como ver:** injetar uma dependência nova em
   `AgentExecutionService` obriga a registrá-la em **todo** harness de teste que
   constrói esse serviço — 12 arquivos, 2 linhas cada. Nenhum deles é trabalho
   de teste; são 24 linhas de custo de DI espalhadas. **Régua nova: injetar uma
   dependência num serviço central custa um arquivo por harness que o constrói,
   e isso é contável antes** (`grep -c "AddSingleton<Serviço>"`).
2. **Produção dobrou em linhas por comentário, não por lógica.** `ToolOrigin` e
   `RenamedAIFunction` (52 linhas somadas) não estavam projetados como arquivos
   próprios, e mais da metade das 285 linhas é documentação de decisão com fonte
   primária — o estilo desta base. Projetar por "linhas de lógica" subestima aqui
   por construção.
3. **Os artefatos OpenSpec quase triplicaram** por três rodadas de revisão, cada
   uma acrescentando verificação (V6, V7, V8), correção de fonte e registro de
   divergência. É a categoria mais volátil e a que a convenção 18 já mandava
   contar à parte.

E o dado que confirma a regra da própria convenção 18: a projeção subiu ~15% de
uma rodada de revisão para a outra, e depois errou +73% no código mesmo assim.
**A lição não é um fator de correção — é que projeção feita antes de a
verificação fechar é rascunho.** Aqui a verificação só fechou durante a
implementação (o custo de DI da causa 1 não era visível antes de injetar).

### Abertos por `dedupe-global-nome-de-tool` (2026-09-08)

- **A renomeação por colisão de nome de tool é invisível na UI.** Quando dois
  nomes colidem no conjunto entregue ao LLM, um é renomeado e o aviso vai só para
  o log; o painel continua mostrando o nome cadastrado em `AllowedTools`. É a
  convenção 13 na direção fraca — a UI não mente, mas não sabe. **Gatilho: quando
  a UI de tools for tocada**, exibir o nome efetivo ao lado do pretendido para
  agentes com colisão. Fora daquela change por convenção 1 (backend primeiro, UI
  depois).
- **R7 — histórico gravado citando tool renomeada: risco esvaziado, detector
  construído.** O censo de V8 achou **zero** agentes colidentes, e não há
  produção, então não existe hoje histórico de conversa que possa ser rejeitado.
  A metade local está verificada: o pipeline
  `FunctionInvokingChatClient`/`ChatClientAgent` é transparente a um histórico que
  referencia função ausente da lista atual (V7 + cenário próprio em
  `AgentToolNamespaceTests`, que produz o histórico de verdade em vez de forjá-lo).
  **A pergunta que fica: algum provedor rejeita esse histórico?** Saída já
  desenhada, caso rejeite: limpar o `conversationSession` do `contextId` afetado
  no primeiro encontro de colisão — change própria, não remendo no consumidor.
  **Gatilho: o primeiro aviso de renomeação que aparecer no log** (o aviso da
  Decisão 7 existe justamente para ser esse detector). **O que falta é observação
  com chave de provedor, não análise.**
- **R8 — limite de nome de função do Anthropic não verificado em fonte
  primária.** Nem a documentação de tool use nem o tipo `ToolParam` do SDK
  publicado declaram comprimento ou padrão. Por isso o argumento "64 é o mínimo
  entre os três provedores" está verificado em **dois** (OpenAI Chat Completions
  64, Gemini 128) e o comentário de `ToolNameSanitizer.MaxToolNameLength` diz
  isso em vez de citar terceiro como autoridade. Independe de ambiente — é
  pergunta de fonte. **Gatilho: quando o argumento do mínimo voltar a ser
  necessário** — mudança de limite de algum provedor, provedor novo, ou migração
  de `ChatClientResolver.BuildOpenAi` para a Responses API, onde o limite do
  próprio OpenAI é 128.
- **`WorkerHostCollection` é mitigação, e esconde o que não resolve.** Cada classe
  de teste que sobe um `IHost` completo usa
  `IClassFixture<WorkerInfrastructureFixture>` — **containers próprios** de
  Postgres e RabbitMQ por classe. Medido: `6956d79` passava 132/132 em paralelo;
  a 13ª classe de host (`AgentToolNamespaceTests`) fez `TaskJobConsumerTests`
  reprovar em bloco na inicialização da fixture. A `[CollectionDefinition]` nova
  serializa entre si as 7 classes de host e a suíte volta a 168/168 em paralelo,
  ao custo de **28 s** e — o que importa mais — da **perda do paralelismo como
  detector**: a próxima classe com o mesmo problema entra na coleção e o sintoma
  desaparece de novo sem a causa ser tratada. A correção é uma
  `ICollectionFixture` compartilhando UM par de containers entre todas elas.

  **A forma natural dessa refatoração ficou conhecida em `knowledge-base-indexacao`,
  e vale registrar antes que se perca:** exportar as **`DbContextOptions` prontas
  do fixture** — em vez de a connection string — é o mesmo movimento que
  compartilhar containers, e os dois se fazem juntos. Aquela change precisou
  tocar 24 arquivos de teste para acrescentar `UseVector()` ao provider, e a saída
  possível era um ajudante (`UseButecoAgentsNpgsql`), escolhido por ser aditivo e
  não reescrever arranjo de teste nenhum. O ajudante resolve o modo de falha
  ("o próximo teste esquece"), mas **não** a duplicação: cada sítio continua
  montando as próprias opções. Com `fixture.DbOptions`, o sítio deixa de saber
  que provider existe, e a mudança seguinte de configuração de banco custa um
  arquivo em vez de 24. **É a forma de fazê-la quando a vez chegar, não trabalho
  a antecipar agora.**
  **Mesma família que o item `InboxFactoryFixture.InitializeAsync` acessa
  `Services` antes de migrar** (acima): fixture de teste construindo host mais
  cedo, ou mais vezes, do que devia. Provavelmente uma change só, com os dois.
  **GATILHO VENCIDO em 11/09/2026, por `knowledge-base-indexacao`.** A change
  acrescentou **quatro** classes de host (as de indexação de conhecimento e a de
  integridade do índice), levando a coleção de **7 para 11** — cada uma com par
  próprio de containers. O fechamento reprovou **1 de 212** com a carga **dentro
  do limiar** (4,26 contra teto de 5,0), e a repetição com a máquina mais
  descarregada (2,52) passou 212/212.

  **O que o gatilho revelou não foi o que ele previa.** Este item esperava que a
  serialização escondesse o sintoma; o sintoma apareceu, e quem falhou foi o
  **limiar de carga**, calibrado para 7 classes. Ver "O limiar de carga
  envelheceu" acima, com a tabela das três rodadas — o registro generalizável é
  que limiar de carga **externa** não cobre suíte que engordou **por dentro**.

  **Continua sendo change própria, e provavelmente uma só junto com o
  `InboxFactoryFixture`.** Não foi feita em `knowledge-base-indexacao` porque é
  refatoração de suíte inteira, não trabalho daquela change. **Terceiro item da mesma família, com mecanismo diferente:**
  a sensibilidade da suíte de `apps/frontend` a contenção de CPU externa, logo
  abaixo.
- **A suíte de `apps/frontend` reprova sob contenção de CPU externa, com
  assinatura própria.** Achado ao propor `frontend-agente-aba-conhecimento`
  (5b). Medido, quatro execuções no mesmo commit `2e5f750`, com a carga externa
  anotada **antes** de cada uma:

  | Carga externa antes | Configuração | Resultado |
  |---|---|---|
  | load 14,5; VM do Podman ~289% + `ReportCrash` ~77% | padrão | 21 reprovados |
  | load ~11,7; mesma VM | padrão | 26 reprovados |
  | load ~11,5; mesma VM | `--maxWorkers=3` | 573/574 |
  | load 3,1; sem a VM, nada acima de ~40% | padrão | **574/574, 103 s** |

  **Assinatura:** sempre `Test timed out in 15000ms`, sempre em testes que
  digitam em formulário (`userEvent`), nunca uma asserção falhando. Qualquer
  outro sintoma **não** é este item.

  **A discriminação, para não ser redescoberta do zero:** (1) rodar cada arquivo
  reprovado isolado — se reprovar isolado, é defeito, não contenção; (2) rodar a
  suíte com `--maxWorkers=3`; (3) se `--maxWorkers=3` também reprovar com a
  máquina descarregada, a hipótese caiu e a investigação recomeça.

  **O que a quarta execução corrige na intuição, e é o que mais vale aqui:** a
  suíte **não** é sensível a load alto em si. No paralelismo padrão ela sozinha
  leva o load de 3,1 para **57,6** em 12 núcleos e passa 574/574 assim. O que a
  derruba é **competição externa** por núcleo — ~2,9 núcleos em outro processo
  bastaram. Logo o limiar se mede **antes** de começar, sobre quem mais está na
  máquina; o número durante a execução é a própria suíte trabalhando e não diz
  nada. Limiar declarado na change: load prévio de 1 min **< 5,0** e **nenhum
  processo alheio ≥ 100%** (um núcleo cheio). Verde medido em 3,1-4,2; vermelho
  medido em 11,5-14,5; entre os dois não há medição, e o limiar é conservador
  por escolha.

  **Primeiro exercício do limiar em uso real, em 10/09/2026, ao abrir a etapa
  2a — e ele funcionou:** a baseline da convenção 19 foi tomada com carga prévia
  de **26,04** contra o limiar de 5,0, com a VM do Podman a **464%**. Ela
  reportou **7 reprovações em `apps/workers`** e **12 em `apps/frontend`**.
  Refeita com a máquina descarregada, medindo a carga **antes de cada alvo**:
  **174/174** e **617/617**. Nenhuma das dezenove existia.

  | alvo | sob carga 26,04 | descarregado | duração |
  |---|---|---|---|
  | `apps/workers` | 7 reprovadas / 174 | **174/174** | 17 m 54 s → **5 m 24 s** |
  | `apps/frontend` | 12 reprovadas / 617 | **617/617** | 215 s → **53 s** |

  As três reprovações que deu para nomear eram todas de
  `AgentDelegationExecutionTests` — classe de host —, uma delas gastando **1 m
  10 s** num teste que roda em segundos. É assinatura de *starvation*, não de
  defeito, e é exatamente o que o parágrafo acima previa.

  **Refinamento medido, com a ressalva de que a medição não o isola:** o
  confundidor não vem só de processo alheio; vem também do **resíduo da suíte
  pesada imediatamente anterior** — na rodada inválida, `apps/frontend` correu
  logo depois de 18 minutos de containers. Na rodada válida houve espaçamento
  **e** máquina ociosa, e as duas coisas mudaram juntas: o dado mostra que a
  combinação importa, **não** que o espaçamento sozinho baste. Separar as duas
  exigiria uma terceira rodada, com máquina ociosa e sem espaçamento, que não
  foi feita.

  **A distinção que esta rodada tornou concreta, e que vale mais que os
  números: "baseline vermelha" e "baseline inválida" são coisas diferentes, e a
  convenção 19 as trata de forma oposta.** Vermelha é informação — remove a
  hipótese de que a change seguinte causou aquilo. Inválida não remove nada, e é
  pior que ausente, porque *parece* informação: uma baseline com 19 reprovações
  fantasma teria feito o fechamento da etapa 2a comparar contra ruído, e
  qualquer regressão real teria se escondido no meio. **O procedimento que a
  convenção 19 precisa passar a exigir é medir a carga antes de cada alvo, não
  só antes do primeiro, e registrar o número medido ao lado de cada resultado.**
  Fica como acréscimo proposto à convenção 19, a promover ao `01` quando a
  próxima change o exercer — não se promove convenção com um caso só.

  **Mesma família que `WorkerHostCollection` (acima) e
  `InboxFactoryFixture.InitializeAsync` acessa `Services` antes de migrar** —
  suíte que reprova por **recurso**, não por defeito, e que por isso convida à
  classificação errada de "ambiental". **Mas o mecanismo é o oposto, e a
  distinção é o que torna o cruzamento útil:** naqueles dois a suíte é
  derrubada pelo recurso que **ela mesma** constrói (fixture subindo host cedo
  demais, containers demais por classe), e a correção é no código de teste;
  neste a suíte está correta e é derrubada por um consumidor **externo**, e não
  há o que corrigir — há o que medir antes. Por isso a discriminação também é
  outra: lá, `git worktree` limpo na base; aqui, execução isolada mais
  `--maxWorkers=3`.

  **Gatilho: a primeira vez que a suíte de `apps/frontend` reprovar em bloco com
  essa assinatura** — conferir este item antes de investigar qualquer outra
  coisa. E, se um dia reprovar com a máquina comprovadamente descarregada,
  **este item deixa de explicar** e vira change própria (candidata óbvia: subir
  o `testTimeout`, hoje 15 s, ou reduzir o paralelismo por padrão — nenhuma das
  duas justificada enquanto a máquina ociosa passa 574/574 em 103 s).
- **Repetir o censo de inventário na etapa 4 de bases de conhecimento.** O teste
  integrado com as bases prontas **não valida o dedupe** — a etapa 4 não introduz
  colisão por si só, só um terceiro conjunto no mesmo namespace, e colisão depende
  de cadastro; o dedupe já está validado pelos guardas que reprovaram antes e
  passaram depois. O que vale carregar é o **inventário**: rodar de novo a consulta
  de V8, estendida às tools de conhecimento. E há chance real de colisão nova ali,
  pelo padrão que o próprio censo encontrou: o servidor MCP chamado
  *"Informações Gerais"* produz `Informa__es_Gerais__get_menu_info`, onde o `__`
  **não é o separador** (`ç` e `õ` viram um `_` cada). Uma base de conhecimento
  com nome equivalente cai no mesmo padrão. **Gatilho: a proposta da etapa 4** —
  entra como tarefa prevista dela, não como lembrança.

- **Lacuna dupla na spec da etapa 1: as datas da base não têm requisito nem
  teste.** `KnowledgeBaseResponse` expõe `createdAt` e `updatedAt`
  (`KnowledgeBaseResponse.cs:10-11`, populados por `FromEntity`), mas a spec viva
  de `knowledge-base-catalog` não os menciona em cenário nenhum — os de listagem
  e de consulta por id dizem "id, nome, descrição e `isActive`" — e
  `grep -rn "createdAt\|CreatedAt"` nos testes de conhecimento de `apps/api`
  não devolve nada (`KnowledgeWireFormatTests` cobre `indexingStatus`,
  `sourceType` e `contentLengthBytes`, que são campos de documento).

  Achado ao propor `frontend-knowledge-base-catalogo` (D16), que precisava saber
  se as datas existiam para decidir se o detalhe podia exibi-las: existem, então
  a tela as exibe e a spec da UI as requer. **O risco é a assimetria**: essa
  change passou a ser o primeiro lugar do repositório com requisito sobre as
  datas, e ele está do lado da UI. Se alguém removesse `CreatedAt` do response,
  nenhum teste de `apps/api` reprovaria — só o painel quebraria, por um campo
  que a spec do backend nunca prometeu (convenção 12: contrato entre lados
  inclui o campo, e defeito de formato pertence a quem expõe). **Gatilho:** a
  etapa 2, que já mexe em `knowledge-base-catalog`, ou qualquer change que toque
  `KnowledgeBaseResponse`.

  **Agravado em 09/09/2026, na conferência da 5a-1:** o card de datas saiu do
  detalhe da base (fidelidade ao protótipo, que não tem datas nessa tela), então
  a spec de UI deixou de requerê-las. `createdAt` e `updatedAt` passam a não ter
  **nenhum** requisito nem asserção em lugar nenhum do repositório — dois campos
  no fio que nada promete e nada verifica.

- **Pastas de handoff desaparecendo de `~/Downloads` durante a sessão.**
  Registrado em 09/09/2026, ao propor `frontend-knowledge-base-catalogo`:
  `sistema_gestao/`, `design_handoff_butecando/` e
  `design_handoff_painel_agentes_mcp/` (mais os dois `.zip` correspondentes)
  existiam no primeiro `ls` da sessão e não existiam minutos depois, com só
  leituras tendo sido feitas no intervalo (`ls`, `find`, `grep`, `sed`, `cat`).
  Não teve consequência: a versão viva do projeto Claude Design
  *Sistema Gestão de Agentes*
  (`e4f9bbd6-dd31-4ff1-954b-0e686d2eaa54`) é a autoritativa, e é dela que os
  anexos de `design/` foram tirados — a cópia local era de 05/09 e nem tinha as
  telas de conhecimento. **Não há causa conhecida e não há ação pedida.** Fica
  registrado só para que, se repetir, se saiba que já aconteceu antes e quando.


## Próximo passo

**Concluído nesta sessão**: `dedupe-global-nome-de-tool` (`0a` da fila) —
aplicada, suíte de `apps/workers` em 168/168, censo de colisão zerado. Com ela
fora do caminho, **a etapa 2 da linha de bases de conhecimento (indexação) é o
próximo passo**. As duas rodadas de medição que ela esperava estão fechadas:
`0b` (head-to-head semântico, modelo e schema) e `0c` (chunker corrigido,
invariantes, overlap, `k` e limiar), ambas registradas acima com bar declarado
antes de medir. A exploração da etapa 2 recomendou **dividi-la em duas
changes** — `2a`, o índice (`apps/workers` mais a tabela de fragmentos), e
`2b`, a superfície de operação em `apps/api` (rota de reindexação e resumo por
base), que é o que a etapa 5a-2 bloqueia. **A `2a` está pronta para ser
proposta.**

**Concluído em sessões anteriores**: o redesenho do painel foi fechado nas **oito
etapas**, da identidade visual ao card A2A, mais a change de CORS de
desenvolvimento que a conferência visual exigiu. Todas propostas, aplicadas,
conferidas à mão, sincronizadas, arquivadas e commitadas.

Duas coisas que arrastavam foram fechadas de arrasto na última etapa, por
estarem impedindo verificá-la: a instabilidade da suíte do frontend (prazo
padrão de um segundo das consultas assíncronas, curto para dropdowns em portal
sob paralelismo) e a configuração do Testcontainers com Podman, agora
documentada no README.

Não há linha de trabalho em andamento. Os candidatos registrados estão em
"Itens em aberto" acima — o mais concreto é validar a url pública no startup.

**Anteriormente**: `inbox-contexto-canal` foi explorada, proposta
e aplicada (ver "Contexto de canal" acima), fechando a linha de trabalho
de contexto temporal e de canal nas duas etapas. A exploração fechou duas
das quatro perguntas reforçadas da change (a maior parte do valor não
dependia de texto livre; a superfície de dano de tools MCP de escrita é
real), o que encolheu a change de "quinto contrato de plugin + mitigação
de injeção de prompt" para "duas chaves escalares no mecanismo já
provado por `messageInstant`" — `DisplayName` ficou de fora por decisão,
não a change inteira ficou menor por acaso. Nenhuma regressão encontrada
— suíte completa de `apps/inbox` (164/164) e `apps/workers` (117/117)
verde, incluindo o round-trip real dos três apps com o cenário novo de
`channelType`/`contactExternalId`.

Anteriormente nesta linha: `inbox-instante-mensagem` foi proposta e
aplicada — tocou exatamente a superfície que a baseline abaixo já
sinalizava como sensível (`AgentDelegationToolSetResolver`), sem correção
prévia de baseline ter sido feita antes (diferente do que este parágrafo
recomendava originalmente). Nenhuma regressão nova encontrada por isso —
os testes novos daquela change passaram, e o único item da baseline que
tocava a mesma superfície (`InboxOrchestratorRoundTrip.Tests`, ver abaixo)
continuou com a mesma causa já registrada, não uma nova.

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
  — falha nomeada consistente nas rodadas coletadas. **Causa confirmada
  (2026-09-07): dependência de ordem dentro da classe, não infraestrutura.**
  É o único item desta fila que já tem causa e ainda não tem correção; os
  demais seguem sem diagnóstico.
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
- ~~`apps/frontend` — `AgentForm`/`ChannelForm`/`McpServerForm` e páginas
  de criação/edição, timing de `userEvent`; contagem não-determinística
  entre rodadas (14–15)~~ — **resolvido por `agente-enderecos-a2a`** (D8):
  prazo padrão de um segundo das consultas assíncronas, curto para
  dropdowns em portal sob paralelismo. 9/9 verde somando as duas
  sessões de medição; ver "Itens em aberto" para a leitura completa.

Com `inbox-session-indice-unico` aplicada, `apps/inbox` (`Buteco.Inbox.Tests`)
não tem mais nenhum flake conhecido — 164/164 (160/160 depois de
`inbox-session-indice-unico`, +1 de `inbox-push-notification-decrypt-resiliente`,
+1 desta sessão). O ruído que resta na fila acima é só cross-app: com o item de
`apps/frontend` resolvido por `agente-enderecos-a2a`, `apps/frontend` também não
tem mais flake conhecido — 54 arquivos / 451 testes, 9/9 verde.

A linha de trabalho de contexto temporal e de canal (`apps-workers-contexto-temporal`
→ `inbox-instante-mensagem` → `inbox-contexto-canal`) está **concluída**.
Como aconteceu depois da linha de histórico de conversa por sessão (três
etapas antes desta), não há sucessor já definido para esta linha — os
candidatos abaixo são independentes entre si, sem ordem imposta.

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