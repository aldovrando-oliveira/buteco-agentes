# Buteco Agentes — Histórico e Status
 
> Este arquivo muda a cada sessão de trabalho nova. Atualizar a seção
> "Status atual" e adicionar à lista de changes aplicadas conforme o
> trabalho avança. O arquivo de arquitetura/convenções (01) é separado e
> muda bem menos.
 
## Status atual
 
A linha de trabalho de histórico de conversa por sessão no inbox está na
**etapa 2 de 3**, concluída: `inbox-mensagens-persistidas` aplicada,
sincronizada e arquivada (etapa 1, `auth-login-e-servico`, já concluída
antes). Não há change em andamento nem prompt pendente de revisão. Ver
"Próximo passo" para onde retomar — etapa 3, UI, é a próxima.
 
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
localização do `PendingDispatch`. Ver
`inbox-push-notification-recepcao-resiliente` — spec de
`inbox-message-orchestration` ganhou requisito novo sobre resiliência do
endpoint receptor à desconexão do chamador; `inbox-message-history` e
`a2a-push-notifications` não mudaram (o defeito 1 era só de
implementação, contra uma spec já correta). Docker Desktop indisponível
neste ambiente — suíte de testes de `apps/inbox` validada com
Testcontainers apontando para o socket da API do Podman
(`DOCKER_HOST` + `TESTCONTAINERS_RYUK_DISABLED=true`, já que o Podman
não roda o Ryuk privilegiado por padrão): 155/155 testes passando.
 
### Autenticação
`auth-login-e-servico`
 
Primeira change de segurança do projeto, e etapa 1 de 3 da linha de
histórico de conversa no inbox (ver "Próximo passo"). Motivada por
antecipação, não por incidente: a etapa 2 faz `apps/inbox` expor conteúdo
real de conversa de usuário final via API, o que muda o perfil de risco de
categoria — autenticação precisava vir antes, não depois.
 
O que entrou:
 
- **Login de operador único** em `apps/api` (`POST /auth/login`),
  credencial via variável de ambiente, senha com PBKDF2. Sem tabela de
  usuários, sem RBAC, sem cadastro, sem recuperação de senha, sem
  OAuth/SSO.
- **Token stateless assinado com HMAC**, sem biblioteca JWT e sem sessão
  em banco — `apps/api` e `apps/inbox` compartilham só a chave de
  assinatura, validando localmente sem chamada de rede entre os processos
  (os dois têm bancos isolados, uma sessão compartilhada exigiria store
  novo). TTL de 30 minutos por padrão, configurável, com o default no
  tipo de Options e não no `appsettings.json`.
- **Enforcement por padrão em toda rota** dos dois apps, com allowlist
  explícita de rotas anônimas em cinco categorias e **checagem de
  integridade bidirecional no startup** — o boot falha tanto se uma rota
  anônima não tiver motivo documentado quanto se um motivo documentado
  não corresponder a rota mapeada. Mesmo molde de
  `ValidateChannelAdapterRegistrations`.
- **Token de serviço** `apps/inbox` → `apps/api`, reaproveitando o mesmo
  mecanismo de assinatura (`sub` distinto, assinado por requisição, TTL
  fixo de 5 min), escopado a exatamente duas rotas — qualquer outra
  responde `403`.
- **`AgentCard` declara `SecuritySchemes`/`SecurityRequirements`** (HTTP
  Bearer): descoberta continua pública, uso passa a exigir credencial, e
  isso fica declarado de forma compatível com a spec A2A em vez de
  acontecer por omissão.
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
 
Etapa 2 de 3 da linha de histórico de conversa no inbox. Entidade `Message`
(`apps/inbox`), tabela relacional própria ligada a `Session` — **não**
estende `PendingDispatch` nem toca sua coleção owned/JSON `Messages`,
exatamente a restrição de desenho que a proposta original exigia.
 
O que entrou:
 
- **Persistência de entrada e saída**: `Message` grava direção, conteúdo,
  tipo de conteúdo (`Text`/`Image`/`Audio`/`Document`, mídia binária fora
  de escopo — só um marcador), instante e, na entrada, o identificador
  externo da mensagem (dedup de webhook reentregue); na saída, status de
  entrega (`Sent`/`Failed`, com motivo) do envio ao provedor via
  `IOutboundMessageSender` — não recibo de entrega/leitura do destinatário
  final.
- **Estado de dispatch espelhado nas mensagens de entrada**
  (`Pending`/`Dispatching`/`Failed`/`Completed`), atualizado nos seis
  pontos reais que mutam ou removem `PendingDispatch`
  (`InboundMessageOrchestrator`, os três desfechos de
  `DebounceSweepService` — reivindicação, rejeição de protocolo A2A,
  rejeição síncrona, esgotamento de tentativas — e
  `PushNotificationEndpoints`), sobrevivendo à remoção da `PendingDispatch`
  correspondente. `Failed` agrupa as três causas de "não haverá resposta"
  sob o mesmo valor, decisão consciente.
- **`Contact.DisplayName`** (nullable), atualizado a cada mensagem de
  entrada — semântica oposta à de `Metadata` (congelado na criação).
  Extraído de `payload._data.Info.PushName` no WAHA (confirmado só via
  discussão da comunidade para o engine GOWS, não pela doc oficial — ver
  Itens em aberto) e de `username`/`first_name` no Telegram.
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
  repo inteiro — interseção real é só `PendingDispatch.Messages`, já
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
`Session` (mesmo padrão já usado para isolar o teste de append de
`PendingDispatch`) em vez de expandir esta change para corrigir um
componente adjacente — registrado como item em aberto abaixo.
 
Revisado antes do apply: seis correções pedidas (contagem de estados de
`DispatchStatus` inconsistente entre `design.md`/`proposal.md`, colisão de
nome entre `DispatchStatus`/`DeliveryStatus`, `ContentType` de saída não
definido, ponto de escrita de `DisplayName` indeciso, cobertura de teste
faltando para mídia, e a consulta de sessões por canal que tinha ficado no
vão entre esta change e a etapa 3). Testes: 153 casos em
`Buteco.Inbox.Tests` + 2 em `InboxOrchestratorRoundTrip.Tests`,
Testcontainers Postgres real, rodados duas vezes para checar
flakiness.
 
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
- **Criação de `Session` sem índice único protegendo contra concorrência**
  (`ContactSessionResolver.FindOrCreateSessionAsync`, gap de
  `inbox-crm-contato-sessao`, exposto por um teste novo de
  `inbox-mensagens-persistidas`) — diferente de `Contact` e
  `PendingDispatch`, nada impede duas chamadas verdadeiramente
  concorrentes para o mesmo `Contact` novo criarem duas `Session`
  distintas dentro da janela de inatividade. Não corrigido nesta change
  (fora do escopo dela); o teste que o expôs foi isolado da corrida de
  criação de `Session` para não ficar intermitente. Gatilho: revisitar se
  aparecer duplicação real de `Session` em produção, ou antes de qualquer
  mudança futura em `ContactSessionResolver`.
- **`WahaWebhookMessagePayload.Data.Info.PushName`** (`DisplayName` do
  WAHA, `inbox-mensagens-persistidas`) — confirmado só via discussão da
  comunidade para o engine GOWS, não pela documentação oficial do WAHA
  (que declara o shape de `_data` como "interno do engine, pode variar").
  Gatilho: confirmar contra uma instância WAHA real de produção assim que
  houver uma disponível; se o campo divergir, `DisplayName` do WAHA
  simplesmente fica sempre nulo até a correção, sem quebrar nada mais.
- **Paginação de `GET /channels/{id}/sessions` e `GET /sessions/{id}/messages`**
  (`frontend-inbox-sessoes-historico`) — nenhuma das duas rotas pagina;
  ambos os handlers fazem `ToListAsync()` direto, sem `Skip`/`Take`, e a
  UI de sessões/timeline carrega a resposta inteira de uma vez. Gatilho:
  sinal real de volume (sessão ou canal com histórico muito longo
  tornando a tela perceptivelmente lenta).
## Próximo passo
 
Linha de trabalho em andamento, de três etapas — histórico de conversa
por sessão visível na UI do inbox:
 
1. ~~`auth-login-e-servico`~~ — **concluída**.
2. ~~`inbox-mensagens-persistidas`~~ — **concluída**. `Message` persistida
   por `Session`, dedup por identificador externo, status de entrega e
   estado de dispatch renderável, `Contact.DisplayName`, e as duas
   consultas (`GET /sessions/{id}/messages`,
   `GET /channels/{id}/sessions`) que a etapa 3 vai consumir — ver detalhe
   em "Changes aplicadas" acima.
3. **`frontend-inbox-sessoes-historico`** ← próxima. `/canais/{id}` ganha
   abas *Sessões* (padrão) e *Configuração* (o card atual, intacto); lista
   de sessões por última atividade (já servida por
   `GET /channels/{id}/sessions`, com prévia da última mensagem); timeline
   com um check só para enviado e ícone de erro com motivo para falha
   (nunca dois checks — entregue e lido são recibos que o desenho
   escolhido não coleta; já servida por `GET /sessions/{id}/messages`).