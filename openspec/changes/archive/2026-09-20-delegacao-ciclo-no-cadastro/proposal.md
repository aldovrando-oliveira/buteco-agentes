## Why

Um ciclo de delegação `A→B→…→A` autotrava no advisory lock de contexto, e
**réplica nenhuma resolve**: a task de A em profundidade 2 pede
`pg_advisory_lock(hashtext(A), hashtext(ctx))` que a task de A em profundidade 0
está segurando enquanto espera B. Medido na exploração `replicas-de-worker`
(20/09/2026) com **quatro** instâncias de worker — sobra consumidor e a cadeia
trava assim mesmo, porque o achado é do desenho e não da contagem de processos.

`apps/api` recusa auto-delegação
(`ReplaceAgentDelegationsCommandHandler.cs:33-36`), mas o comentário ali mesmo
registra ciclo geral como fora de escopo (Decision 3), e `A→B→A` é aceito no
cadastro — **hoje há dois cenários verdes na spec e dois testes verdes em
`AgentDelegationEndpointsTests` afirmando que o ciclo é permitido.** O defeito
não é uma lacuna: é um requisito escrito.

**O que a change anterior mudou, e o que ela não mudou.** Com
`lock-de-contexto-falha-terminal` aplicada, a task de profundidade 2 não fica
mais órfã em `working` — ela termina em `failed` quando o `CommandTimeout` de
30 s do Npgsql estoura. Verificado no código nesta change:
`AgentExecutionService.cs:186-200` chama `FailTaskAsync` no `catch` próprio da
aquisição. **O defeito continua**: a delegação não acontece, o usuário recebe
uma resposta degradada, e o ciclo segue aceito no cadastro. O que se ganhou foi
observabilidade, não correção.

## What Changes

A change tem **três escopos**, e eles estão declarados porque misturar correção
de teste e promoção de convenção com mudança de comportamento sem dizer é o que
torna um diff ilegível.

### Escopo 1 — detecção de ciclo no cadastro (`apps/api`, comportamento)

- **BREAKING (contrato de API):** `PUT /agents/{id}/delegations` passa a
  responder **400** quando o conjunto enviado fecha um ciclo de delegação de
  qualquer comprimento — qualquer caminho que, a partir de um dos alvos
  pedidos, volte ao próprio agente Source. Hoje responde 200.
- A checagem roda contra o grafo **como ele ficaria depois do replace** (as
  arestas de saída do Source são substituídas antes da travessia), não contra o
  grafo atual: uma edição que **quebra** um ciclo pré-existente continua sendo
  aceita.
- A rejeição continua **atômica**: nenhum vínculo do payload é aplicado, igual
  aos demais casos de erro do handler.
- A mensagem de erro nomeia o **caminho** que fecha o ciclo, não só o fato.
- A recusa de auto-delegação (Decision 2) **permanece com mensagem própria**,
  embora a regra geral já a contenha.
- **Decision 3 do handler é corrigida com a causa real** (convenção 9): o que
  estava registrado como "fora de escopo desta camada" passou a ter mecanismo de
  dano medido.
- **Nenhuma mudança em `apps/workers`.** A decisão de não acrescentar defesa em
  profundidade no runtime está registrada no `design.md`, com o inventário que a
  sustenta.

### Escopo 2 — carve do defeito pré-existente de `AgentDeactivationTests` (`apps/api`, teste)

Item de fila com posição registrada no `02` (*"antes da próxima change que
tocar `apps/api`"*). Esta é essa change.

- As três asserções `Assert.Empty(fixture.TaskJobPublisher.PublishedMessages)`
  passam a ser relativas ao agente do próprio teste, eliminando a dependência de
  ordem de execução dentro da classe.
- **Nenhuma mudança de comportamento de produção neste escopo.**

### Escopo 3 — forma curta da convenção 22 (`01`, registro)

- A convenção 22 ganha no `01-ARQUITETURA_E_CONVENCOES.md` a forma curta
  **"régua citada sem escopo não é régua"**. O `02` já documenta o custo
  acumulado (quatro valores da mesma régua, quatro documentos, nenhum com o
  escopo colado); faltava a regra em forma citável.

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `agent-delegation-binding`: o requisito **"Nenhuma detecção de ciclo ou de
  vínculo bidirecional no cadastro"** é **REMOVIDO** e substituído por um
  requisito de recusa de ciclo em qualquer comprimento, com os dois cenários
  atuais (ciclo indireto permitido, par bidirecional permitido) **invertidos**.
  O requisito de substituição do conjunto ganha o ciclo na lista de causas de
  rejeição atômica.

`agent-delegation-binding-ui` **não** entra: ver Impact.

## Impact

**`apps/api` (produção).** `AgentDelegations/Commands/ReplaceAgentDelegations/`
(handler e result), `AgentDelegations/Endpoints/AgentDelegationEndpoints.cs`, e
um tipo novo para a travessia do grafo. Sem migration: a detecção lê
`agent_delegations`, não muda schema.

**`apps/api` (teste).** `AgentDelegationEndpointsTests.cs` — dois testes verdes
que hoje afirmam o defeito como requisito passam a afirmar a recusa, mais os
guardas novos. `AgentDeactivationTests.cs` — escopo 2.

**`apps/workers`.** Nenhuma. O `DelegationDepthLimit` (5) fica como está, e não
cobre este caso: o ciclo de dois saltos trava em profundidade 2, bem abaixo do
teto.

**`apps/frontend`.** Nenhuma nesta change, e é decisão, não esquecimento
(convenção 1). `AgentDelegationsTab.tsx:68-74` responde a qualquer erro com
*"Não foi possível atualizar as delegações do agente. **Tente novamente**."* —
que para uma recusa permanente por ciclo afirma mais do que o sistema sabe
(convenção 13): tentar de novo nunca vai funcionar. É achado a sequenciar, com
gatilho cumprido e posição registrada no `design.md`.

**Dado.** O banco de desenvolvimento tinha **zero** linhas em
`agent_delegations` no último censo, e o banco do piloto será limpo antes do
próximo deploy — então nenhum cenário de ciclo existe para conferir contra dado
real, e os guardas semeiam o grafo por conta própria. O que isso permite decidir
sobre varredura de ciclos pré-existentes está no `design.md`, com o gatilho de
recalibração colado (convenção 22).

**Baseline de `apps/api`, medida nesta change** contra `HEAD` `1ba84ec`, em
20/09/2026: **317 aprovados / 1 reprovado / 318**, e a única reprovada é
exatamente a do escopo 2.
