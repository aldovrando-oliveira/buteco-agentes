## Context

`apps/api` já tem um precedente direto para este problema: o vínculo N:N
`AgentMcpServer` entre `Agent` e `McpServer` (change
`backend-mcp-catalogo-vinculo`, arquivada). Essa change estabeleceu o
padrão que esta reutiliza quase integralmente — tabela relacional própria
para vínculo entre entidades catalogadas, módulo dedicado fora das duas
entidades relacionadas, `PUT` de substituição completa do conjunto, e um
`Lookup` estático compartilhado entre os handlers de `Agents` que montam
`AgentResponse`.

A diferença estrutural desta change é que os dois lados do vínculo são a
*mesma* entidade (`Agent` → `Agent`), não duas entidades distintas. Isso
introduz uma pergunta nova que o precedente de MCP não respondia sozinho —
auto-referência (auto-delegação) — e reabre, precisando de resposta
explícita, uma pergunta que o precedente resolveu apenas para o caso
bipartido (ciclos), porque um grafo de uma entidade para si mesma pode
formar ciclo de qualquer profundidade, inclusive 1.

Investigação prévia (via `/opsx:explore`) leu a implementação real de
`AgentMcpServer`/`AgentMcpBindings` (entidade, `AppDbContext`, Command/
Handler/Result, Endpoints, `AgentResponse`, `AgentMcpServerLookup`, e os 7
pontos de chamada de `AgentResponse.FromEntity`) antes de qualquer decisão
abaixo ser fixada — ver seção Decisions para o raciocínio completo e as
alternativas descartadas.

A execução real — o Worker chamando outro agente via delegação, com
controle de profundidade, detecção de ciclo em runtime, `contextId`
compartilhado, timeout e verificação de concorrência — é uma change futura
e não é tocada aqui.

## Goals / Non-Goals

**Goals:**
- Entidade `AgentDelegation` (vínculo unidirecional `Agent` → `Agent`),
  tabela relacional própria, com FK para `Agent` nos dois lados.
- `PUT /agents/{id}/delegations` substituindo o conjunto completo de
  delegações de saída do agente `id` (Source), mesmo padrão CQRS/
  idempotência/validação já usado em `ReplaceAgentMcpServers`.
- Rejeitar auto-delegação explicitamente na validação (400).
- `AgentResponse` passa a incluir `delegatesTo` (id + name dos agentes-alvo
  vinculados), refletido em todos os endpoints que retornam `AgentResponse`.
- Vínculo a um agente Target inativo é permitido (configurar não é
  bloqueado pelo estado inativo).

**Non-Goals:**
- Nenhuma execução real de delegação (chamada A2A, tool, publisher em
  `apps/workers`) — fica inteiramente para a change de execução seguinte.
- Nenhum controle de profundidade ou detecção de ciclo, indireto
  (A→B→C→A) ou bidirecional (A→B e B→A cadastrados separadamente) — ver
  Decision 3.
- Nenhum endpoint de consulta reversa ("quais agentes delegam para este")
  — estruturalmente possível de consultar mais tarde (mesma tabela,
  direção invertida), sem necessidade concreta agora.
- Nenhuma configuração de profundidade máxima (constante ou por agente) —
  pertence à change de execução.
- Qualquer mudança em `apps/workers` ou `apps/frontend`.

## Decisions

### Decision 1: `AgentDelegation` como tabela relacional própria, não jsonb

`AgentDelegation` é uma tabela relacional com chave composta
`(SourceAgentId, TargetAgentId)` e FK para `Agent` nos dois lados —
herdando exatamente o raciocínio já registrado no Decision 1 do design.md
de `backend-mcp-catalogo-vinculo` para `AgentMcpServer`: os dois lados do
vínculo são entidades catalogadas com identidade própria (`Agent`),
diferente de `Skill` (jsonb em `Agent.Skills`, sem entidade catalogada
referenciada) ou de `AllowedTools` em `AgentMcpServer` (jsonb, porque tools
não são um catálogo relacional persistido em lugar nenhum). Repetir esse
raciocínio aqui, mesmo herdando o padrão, mantém o mesmo princípio
aplicado toda vez que um novo tipo de dado precisa dessa escolha: jsonb
quando não há entidade catalogada do outro lado, tabela relacional quando
há.

No `AppDbContext`: `entity.HasKey(d => new { d.SourceAgentId,
d.TargetAgentId })`, com duas relações `HasOne<Agent>().WithMany()`
distintas — uma via `HasForeignKey(d => d.SourceAgentId)`, outra via
`HasForeignKey(d => d.TargetAgentId)` — ambas `OnDelete(DeleteBehavior.
Cascade)`. O EF Core distingue as duas relações pela propriedade de FK, não
pela navegação (nenhuma das duas tem navegação de volta em `Agent`), o
mesmo mecanismo já usado em `AgentMcpServer` para suas duas FKs — a
diferença de apontar duas vezes para a *mesma* tabela (`Agent`) em vez de
duas tabelas diferentes não introduz nenhuma ambiguidade nova nesse
mecanismo.

**Alternativa não considerada seriamente**: jsonb (ex. um array de
`TargetAgentId` direto em `Agent`). Descartada pelo mesmo motivo que já
descartou essa forma para `AgentMcpServer` — perderia a garantia de
integridade referencial do banco (FK), e a validação de "o Target existe"
teria que ser inteiramente responsabilidade do código da aplicação em vez
de também ser garantida pelo schema.

### Decision 2: Auto-delegação rejeitada explicitamente (400)

`SourceAgentId == TargetAgentId` é rejeitado na validação do handler,
antes de qualquer persistência — mesmo estilo do cheque de
`InvalidMcpServerIds` em `ReplaceAgentMcpServersCommandHandler` (rejeição
atômica, nenhum vínculo do payload aplicado quando há erro).

Auto-delegação é, estruturalmente, um ciclo de profundidade 1. Ela caberia
sob o guarda-chuva geral de "controle de profundidade/ciclo é
responsabilidade da change de execução" (Decision 3), mas é registrada
como caso à parte, com validação nesta camada, porque é um erro de
cadastro trivial de cometer sem nenhuma consequência de runtime em jogo —
um agente apontando para si mesmo não é um grafo válido em nenhum sentido
útil, diferente de um ciclo indireto (Decision 3), que *é* um grafo
individualmente válido em cada vínculo que o compõe.

### Decision 3: Nenhuma detecção de ciclo indireto ou de par bidirecional no cadastro

Nem ciclo indireto (A→B→C→A) nem par bidirecional (A→B e B→A cadastrados
separadamente) são detectados ou bloqueados no momento do vínculo. Cada um
desses vínculos, individualmente, é um `AgentDelegation` perfeitamente
válido (Source existe, Target existe, não são o mesmo agente) — a
propriedade problemática só existe no grafo *agregado*, não em nenhum
registro isolado.

**Alternativa considerada e descartada**: validar ciclo no cadastro,
percorrendo o grafo de delegações existente a cada `PUT`
(busca em profundidade a partir do Target, verificando se alguma cadeia
leva de volta ao Source). Tecnicamente viável, mas descartada por
complexidade desproporcional ao problema que esta camada resolve: cadastro
responde "esse vínculo existe", não "toda cadeia possível de vínculos é
seguro executar". Antecipar a validação de ciclo aqui exigiria decidir,
sem contexto de execução ainda existente, se ciclo é sempre proibido
(mesmo que a execução real nunca chegue a percorrê-lo por outro motivo,
como profundidade máxima) ou se é condicionalmente aceitável — pergunta
que pertence à change de execução, que já vai precisar de lógica de
percurso de grafo para o controle de profundidade em runtime de qualquer
forma. Prevenção de loop infinito é responsabilidade dessa lógica futura,
não do cadastro.

### Decision 4: Vínculo a um agente Target inativo é permitido

Mesmo padrão já estabelecido para `McpServer` inativo em `AgentMcpServer`
(Decision 3 do design.md de `backend-mcp-catalogo-vinculo`): configurar
não é bloqueado pelo estado inativo, só o uso em tempo de execução seria —
e execução não existe nesta change. `ReplaceAgentDelegationsCommandHandler`
valida apenas que o `TargetAgentId` existe (400 se não), sem checar
`IsActive`.

**Registro explícito para a change de execução futura**: assim como já
registrado para `McpServer.IsActive`, a regra "uma delegação não deve ser
executada para um agente Target inativo" é responsabilidade da change de
execução, que precisará checar `Agent.IsActive` do lado Target (e
provavelmente também do lado Source) antes de delegar de fato.

### Decision 5: `PUT /agents/{id}/delegations` com `{ targetAgentIds: [...] }`

Mesma semântica de substituição completa já usada em
`PUT /agents/{id}/mcp-servers`: o corpo define o conjunto inteiro de
delegações de saída do agente `id` (Source), substituindo qualquer vínculo
anterior — idempotente quando o mesmo conjunto é reenviado.

Difere do shape de `ReplaceAgentMcpServersRequest` (`{ mcpServers:
[{mcpServerId, allowedTools}] }`, lista de *objetos*) porque aquele shape
existe para carregar `allowedTools`, um dado por vínculo que
`AgentDelegation` não tem — não há nenhum atributo por vínculo a decidir
nesta change. Sem esse dado extra, uma lista de objetos de um único campo
(`{ targetAgentId }`) seria indireção sem função; `{ targetAgentIds: [Guid,
...] }`, lista de ids nua, é a forma mais direta que ainda comunica
"conjunto completo de Targets" no nome do campo.

`ReplaceAgentDelegationsCommand`/`Handler`/`Result` seguem o mesmo formato
de `ReplaceAgentMcpServersCommand`/`Handler`/`Result`: `Result` é um sinal
neutro sem referência a `Microsoft.AspNetCore.Http.HttpResults`,
distinguindo "agente Source inexistente" (404) de "algum `TargetAgentId`
não existe" (400) de "auto-delegação" (400); rejeição sempre atômica —
nenhum caso de erro aplica nenhum vínculo do payload.

**Alternativa considerada e descartada**: mesma alternativa já descartada
no Decision 4 do design.md de MCP (`POST`/`DELETE` individuais por
vínculo) — descartada aqui pelo mesmo motivo: multiplica endpoints para
uma relação sem dado próprio, e diverge do padrão de "update = substituição
completa" já estabelecido no projeto.

### Decision 6: `AgentResponse.DelegatesTo`, tipo `AgentSummaryResponse`

`AgentResponse` ganha `DelegatesTo: IReadOnlyList<AgentSummaryResponse>` —
os agentes para os quais este agente delega, direção de saída explícita no
nome do campo (não uma lista simétrica; a assimetria do vínculo precisa
ficar óbvia no shape, não só na documentação).

Nível de detalhe (id + name) segue o mesmo princípio do Decision 5 do
design.md de MCP para `McpServerSummaryResponse`, mas o **nome do tipo**
diverge deliberadamente: `McpServerSummaryResponse` carrega `AllowedTools`
— um dado do *vínculo*, não só do `McpServer` — o que justifica nomear o
tipo pelo vínculo teria sentido, mas ainda assim foi nomeado pela entidade
resumida (`McpServer`). Aqui, sem nenhum dado extra por vínculo
(Decision 5), o tipo é ainda mais claramente um resumo de `Agent`, não de
`AgentDelegation` — daí `AgentSummaryResponse(Guid Id, string Name)`, não
`AgentDelegationSummaryResponse`. Reutilizável se um campo simétrico
futuro (ex. `DelegatedBy`, quem delega para este agente) precisar do mesmo
shape.

Refletido em `POST`/`GET`/`PUT /agents` e `GET /agents/{id}` — lista vazia,
nunca nula, quando não há vínculo.

**Nota de blast radius**: `AgentResponse.FromEntity` ganha um terceiro
parâmetro (`agent`, `mcpServers`, `delegatesTo`), tocando os mesmos 7
pontos de chamada que hoje passam `mcpServers`: `CreateAgentCommandHandler`
(passa `[]` fixo, mesmo motivo que já passa `[]` fixo para `mcpServers` —
um agente recém-criado não pode ter delegação, pois o vínculo exige o `id`
já existir), `UpdateAgentCommandHandler`, `ActivateAgentCommandHandler`,
`DeactivateAgentCommandHandler`, `GetAgentByIdQueryHandler`,
`ListAgentsQueryHandler`, e `ReplaceAgentMcpServersCommandHandler` (que
passa a também buscar `delegatesTo` via `AgentDelegationLookup` para poder
montar o `AgentResponse` completo). Simetricamente, o novo
`ReplaceAgentDelegationsCommandHandler` busca `mcpServers` via
`AgentMcpServerLookup` para o mesmo fim.

## Estrutura de pastas proposta

```
apps/api/src/Buteco.Api/
  AgentDelegations/
    Entities/
      AgentDelegation.cs
    Commands/
      ReplaceAgentDelegations/
        ReplaceAgentDelegationsCommand.cs
        ReplaceAgentDelegationsCommandHandler.cs
        ReplaceAgentDelegationsResult.cs
    Endpoints/
      AgentDelegationEndpoints.cs
    Requests/
      ReplaceAgentDelegationsRequest.cs
    AgentDelegationLookup.cs
  Agents/
    Responses/
      AgentSummaryResponse.cs         # novo
      AgentResponse.cs                # alterado: novo campo DelegatesTo
    Queries/
      GetAgentById/GetAgentByIdQueryHandler.cs   # alterado: inclui delegatesTo
      ListAgents/ListAgentsQueryHandler.cs       # alterado: inclui delegatesTo
    Commands/
      CreateAgent/CreateAgentCommandHandler.cs        # alterado: [] fixo
      UpdateAgent/UpdateAgentCommandHandler.cs        # alterado: inclui delegatesTo
      ActivateAgent/ActivateAgentCommandHandler.cs    # alterado: inclui delegatesTo
      DeactivateAgent/DeactivateAgentCommandHandler.cs # alterado: inclui delegatesTo
  AgentMcpBindings/
    Commands/ReplaceAgentMcpServers/
      ReplaceAgentMcpServersCommandHandler.cs   # alterado: inclui delegatesTo
  Infrastructure/
    AppDbContext.cs                   # alterado: DbSet<AgentDelegation>,
                                       # configuração fluente da nova tabela
    Migrations/
      <timestamp>_AddAgentDelegationCatalog.cs
```

`AgentDelegations` fica como módulo próprio (não dentro de `Agents/`),
mesmo raciocínio já aplicado a `AgentMcpBindings`: é, conceitualmente, uma
capability própria (`agent-delegation-binding`), mesmo referenciando só
`Agent` dos dois lados. Nome da pasta é a pluralização direta da entidade
(`AgentDelegation` → `AgentDelegations`), mesmo padrão de `Agents/` e
`McpServers/` — sem a necessidade de desambiguação que levou
`AgentMcpBindings` a um nome capability-oriented em vez de
`AgentMcpServers` (esse nome colidiria com o significado já usado em
`AgentResponse.McpServers`).

Nenhum conteúdo novo em `libs/` — não há necessidade concreta de
compartilhar código entre `apps/api` e `apps/workers` nesta fatia
(`apps/workers` não é tocado).

## Risks / Trade-offs

- **[Trade-off] Nenhuma validação de ciclo no cadastro (Decision 3)
  permite criar grafos de delegação que, se executados sem controle de
  profundidade, causariam loop infinito** → Aceito por design: a mitigação
  real (controle de profundidade em runtime) é responsabilidade explícita
  da change de execução futura, que não pode ser adiada indefinidamente
  simplesmente porque o cadastro não bloqueia o grafo problemático — a
  mesma separação de responsabilidade já usada para `McpServer.IsActive`
  (cadastro permite, execução é quem bloqueia o uso indevido).
- **[Trade-off] `PUT /agents/{id}/delegations` substituindo o conjunto
  inteiro pode causar race condition** se dois clientes lerem o conjunto
  atual e escreverem versões divergentes ao mesmo tempo → Aceito por ora,
  mesma característica já presente em `PUT /agents/{id}/mcp-servers` e em
  `PUT /agents/{id}`, sem controle de concorrência otimista em nenhum lugar
  da API hoje; não é uma regressão introduzida por esta change.
- **[Risco] Crescimento contínuo da assinatura de `AgentResponse.FromEntity`
  a cada novo tipo de vínculo de `Agent`** (hoje 2 parâmetros extras além
  do `Agent`; após esta change, 3) → Aceito por ora: o padrão atual
  (parâmetros posicionais explícitos, sem "builder" ou objeto de
  agregação) ainda é legível a 3 parâmetros; se uma change futura
  introduzir um quarto vínculo, vale reconsiderar um objeto de agregação
  (`AgentRelatedEntities` ou similar) — não antecipado aqui por não haver
  necessidade concreta ainda (regra do projeto: não construir para
  requisito hipotético).

## Migration Plan

Migration EF Core aditiva única (`AddAgentDelegationCatalog`): cria
`agent_delegations`, sem alterar nenhuma tabela existente. `AgentResponse`
ganha um campo novo (`delegatesTo`, sempre presente, lista vazia quando o
agente não delega para nenhum outro) — aditivo no shape JSON, mas marcado
como **BREAKING** no proposal porque consumidores que fazem parsing
estrito precisam tolerar um campo novo. Sem dado a migrar (tabela nova,
vazia). Rollback = reverter a migration (`dotnet ef database update
<migration anterior>`), seguro porque nenhuma tabela existente foi
alterada.

## Open Questions

Nenhuma pergunta de negócio/produto em aberto — as decisões acima cobrem
os pontos que precisavam de investigação antes do proposal, todas
confirmadas contra a implementação real do precedente de MCP durante o
`/opsx:explore`. Pontos de implementação que ficam a critério de quem
implementar (não bloqueiam o apply): nomes exatos de classes internas,
formato exato da mensagem de erro de auto-delegação.
