# Buteco Agentes — Histórico e Status
 
> Este arquivo muda a cada sessão de trabalho nova. Atualizar a seção
> "Status atual" e adicionar à lista de changes aplicadas conforme o
> trabalho avança. O arquivo de arquitetura/convenções (01) é separado e
> muda bem menos.
 
## Status atual
 
A linha de trabalho de autenticação está concluída (`auth-login-e-servico`
aplicada, sincronizada e arquivada). Ela é a **etapa 1 de 3** de uma linha
maior, já planejada, de histórico de conversa por sessão no inbox. Não há
change em andamento nem prompt pendente de revisão. Ver "Próximo passo"
para onde retomar.
 
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
de bug, não capability nova) → `frontend-inbox-catalogo-canais`
 
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
- **Varredura da classe de bug de `ReloadAsync`** (pendência de
  `inbox-fix-concorrencia-orquestrador`) — nunca foi confirmado se o
  mesmo padrão de risco (coleção owned/JSON mutada in-place + re-leitura
  na mesma instância de `DbContext`) existe em outro lugar do código além
  de `InboundMessageOrchestrator`. Já tem plano definido em dois eixos
  (gatilhos de re-leitura × superfícies owned/JSON), com a exigência de
  classificar por escrito cada superfície encontrada, **inclusive as
  seguras**. Agendada para o `/opsx:explore` da etapa 2, por ser
  pré-requisito do desenho de persistência de mensagem. A outra metade da
  pendência (seção de Risks daquele `design.md` possivelmente ainda
  descrevendo o mecanismo abandonado) vai junto.
- **ChatWoot** foi mencionado como alternativa possível na concepção
  inicial do projeto, nunca formalmente descartado — mas a direção
  tomada desde então (canais próprios em `apps/inbox`, arquitetura de
  plugin) é, na prática, a decisão de não usá-lo.
## Próximo passo
 
Linha de trabalho em andamento, de três etapas — histórico de conversa
por sessão visível na UI do inbox:
 
1. ~~`auth-login-e-servico`~~ — **concluída**.
2. **`inbox-mensagens-persistidas`** ← próxima. Entidade `Message` ligada
   a `Session` (tabela relacional própria, **não** estendendo o
   `PendingDispatch` — é a área do bug de concorrência corrigido),
   status de entrega com semântica de sucesso/falha do envio ao provedor
   (não recibos de entrega/leitura), estado de dispatch renderável na
   timeline, e `Contact.DisplayName` capturado do `pushName` (WAHA) e do
   `from.username` (Telegram) — sem ele a lista da etapa 3 mostra número
   cru. Inclui a varredura de `ReloadAsync` como pré-requisito do
   desenho. Retenção de mensagem fica fora de escopo (ver abaixo).
3. `frontend-inbox-sessoes-historico` — `/canais/{id}` ganha abas
   *Sessões* (padrão) e *Configuração* (o card atual, intacto); lista de
   sessões por última atividade; timeline com um check só para enviado e
   ícone de erro com motivo para falha (nunca dois checks — entregue e
   lido são recibos que o desenho escolhido não coleta).
**A registrar quando a etapa 2 for aplicada**: retenção de mensagens
(nada de TTL ou expurgo nessa fatia) entra aqui como item em aberto, com
gatilho — sugestão: revisitar quando o primeiro canal em produção passar
de dezenas de milhares de mensagens, ou quando o backup do
`buteco_inbox` incomodar.