## Context

Etapa 3 de 5 da linha de bases de conhecimento. A etapa 1 entregou
`KnowledgeBase` e `KnowledgeDocument` em `apps/api`, com espelho de EF Core em
`apps/workers`. Nenhuma base pertence a agente nenhum, e é isso que esta change
resolve.

A change é pequena e tem **dois precedentes fortes no repositório**, o que muda
o caráter do `design.md`: quase nenhuma decisão aqui é invenção — a maioria é
*escolha entre dois precedentes*, e o trabalho foi ler os dois antes de decidir
(convenção 6). Toda decisão abaixo cita o arquivo e a linha que a sustenta.

### O que foi verificado antes de decidir

Nada nesta seção veio de memória; tudo foi lido no código desta árvore, no
commit `515a245`.

**V1 — Os dois precedentes de vínculo, lado a lado.**

| | `AgentMcpServer` | `AgentDelegation` |
|---|---|---|
| Colunas do vínculo | `AgentId`, `McpServerId`, **`AllowedTools` (jsonb)** | `SourceAgentId`, `TargetAgentId` — nada mais |
| Rota | `PUT /agents/{id}/mcp-servers` | `PUT /agents/{id}/delegations` |
| Corpo | `{ mcpServers: [{ mcpServerId, allowedTools }] }` | `{ targetAgentIds: [guid] }` |
| Campo ausente/`null` | 400 com mensagem "envie uma lista vazia para remover" | 400, mesma mensagem |
| Duplicata no payload | `GroupBy(...).Select(g => g.Last())` — **silenciosa** | `.Distinct()` — **silenciosa** |
| Id inválido | 400 listando os ids | 400 listando os ids |
| Agente da URL inexistente | 404 | 404 |
| Validação extra | handshake ao vivo contra o servidor MCP (400/502) | auto-delegação → 400 |
| Estado inativo do outro lado | **não filtra** | **não filtra** (spec tem cenário explícito) |
| Resposta | `AgentResponse` completo | `AgentResponse` completo |
| Result | record neutro, sem `HttpResults` | record neutro, sem `HttpResults` |

Fontes: `apps/api/src/Buteco.Api/AgentMcpBindings/**`,
`apps/api/src/Buteco.Api/AgentDelegations/**`,
`openspec/specs/agent-delegation-binding/spec.md`.

**V2 — Replace-all tudo-ou-nada é o idioma, nos dois.**
`ReplaceAgentMcpServersCommandHandler` e `ReplaceAgentDelegationsCommandHandler`
retornam no **primeiro** erro sem chamar `SaveChangesAsync`; nenhum dos dois tem
add/remove individual. Os XML docs dos dois `Result` dizem a mesma frase:
*"Rejeição sempre atômica: nenhum caso de erro aplica nenhum vínculo do
payload."* Não há motivo registrado para divergir, e esta change não cria um.

**V3 — Estado inativo não é filtrado no vínculo; é filtrado na resolução.**
Nenhum dos dois handlers de `Replace` consulta `IsActive`. O filtro mora em
`apps/workers`: `McpToolSetResolver.cs:52` — `where binding.AgentId == agentId
&& server.IsActive`; e `AgentDelegationToolSetResolver.cs:98,110`, que descarta
source e target inativos. A spec de delegação já tem o cenário
*"Delegar para um agente Target inativo é permitido"*.

**V4 — Auto-vínculo não existe aqui.** `AgentDelegation` precisa rejeitar
auto-delegação porque os dois lados são `Agent`. Aqui os lados são entidades
diferentes (`Agent` × `KnowledgeBase`), então não há caso análogo: o `Result`
desta change **não** tem contraparte de `SelfDelegationRejected`. Isso é dito
em vez de omitido, porque "não se aplica" e "esqueceram" se parecem no código.

**V5 — Como a resposta de agente carrega vínculos hoje, e o custo.**
`AgentResponse` já carrega `McpServers` e `DelegatesTo`. Dois caminhos
distintos, ambos verificados:
- **Por agente**: `AgentMcpServerLookup.GetLinkedMcpServersAsync` e
  `AgentDelegationLookup.GetDelegateTargetsAsync`, cada um uma consulta com
  `Join` + `OrderBy(name)`. Usados por 7 handlers.
- **Em lote**: `ListAgentsQueryHandler` faz **duas** consultas para a listagem
  inteira e agrupa em memória, com o comentário *"Uma única consulta para todos
  os vínculos, em vez de N+1 por agente"*. É esse o caminho que a listagem
  precisa espelhar.

**V6 — Blast radius de um campo novo em `AgentResponse`: 8 sites existentes, 10 pontos no total.**
`AgentResponse.FromEntity` é chamado hoje em `ListAgents`, `GetAgentById`,
`CreateAgent`, `UpdateAgent`, `ActivateAgent`, `DeactivateAgent`,
`ReplaceAgentMcpServers` e `ReplaceAgentDelegations` — **8 sites existentes a
tocar**. O handler novo desta change (`ReplaceAgentKnowledgeBases`) é o **nono**
site de produção, criado e não modificado. O **décimo** ponto é o teste:
`AgentResponseWireFormatTests.cs:16` constrói o record **posicionalmente** e
deixa de compilar quando o campo entra. Nenhum outro teste constrói
`AgentResponse` — os demais desserializam.

**V7 — A visão inversa não é rota de API.** A projeção da etapa 3 dizia
"~2 CQRS (replace + list)". Verificado: **não existe**
`GET /mcp-servers/{id}/agents` (`McpServerEndpoints.cs` mapeia `/`, `/{id}` e
`/{id}/tools`). O card "Agentes que usam este servidor"
(`apps/frontend/src/features/mcp-servers/components/McpServerAgentsCard.tsx`)
recebe a lista de agentes como prop e filtra no cliente por
`agentsUsingServer(agents, mcpServerId)`, com o comentário *"Visão inversa: a
partir do servidor, quem o usa. É leitura — o vínculo continua sendo editado a
partir do agente."*

**V8 — `apps/frontend` não quebra.** `Agent` é interface TypeScript
(`apps/frontend/src/features/agents/types/agent.ts:45`), e não há zod/valibot no
`package.json` — nenhuma validação de schema em runtime. Campo novo na resposta
é aditivo e simplesmente ignorado até a etapa 5b declará-lo.

**V9 — Espelho e migração de `apps/workers`.** Confirmado que a regra da etapa 1
vale igual: `apps/workers/src/Buteco.Workers/Infrastructure/AppDbContext.cs`
espelha a configuração de `apps/api` linha a linha, com XML doc dizendo que as
migrações de lá *"existem só para geração de schema/ferramental do EF Core, não
para serem executadas em runtime"*. A regra operacional está em
`02-HISTORICO_E_STATUS.md` ("Itens em aberto"), com o `42P07: relation "agents"
already exists` já reproduzido.

**V10 — Rotas são autenticadas por padrão.** `RouteAuthenticationExtensions`
exige `AnonymousRouteClassification` em toda rota `AllowAnonymous`, e a única do
`Program.cs` é a de health probe. A rota nova não precisa de nada.

**V11 — Nenhum enum novo atravessa o fio.** O item de vínculo é `{ id, name }`.
Não há acorde de sigla/número/maiúsculas consecutivas em `knowledgeBases` que
faça a política camelCase morder como mordeu em `A2A` → `a2A`. Ainda assim a
chave literal é afirmada por teste sobre o JSON bruto (convenção 12), porque o
custo é uma asserção e o erro é invisível de outra forma.

**V13 — Nome não é único em nenhum catálogo desta base, e a ordenação existente
não desempata.** Verificado no `AppDbContext` de `apps/api`: os únicos
`HasIndex` são `a2a_tasks.ContextId`, `a2a_tasks.State` e
`knowledge_documents.KnowledgeBaseId` — **nenhum índice único de nome**, em
nenhuma entidade. Nenhum handler de criação valida nome duplicado
(`CreateAgentCommandHandler`, `CreateMcpServerCommandHandler`,
`CreateKnowledgeBaseCommandHandler` não consultam por `Name`), e
`knowledge-base-catalog` tem o cenário explícito *"Nome duplicado é permitido"*.
Os quatro sites que ordenam vínculo por nome hoje —
`AgentDelegationLookup.cs:23`, `AgentMcpServerLookup.cs:22`,
`ListAgentsQueryHandler.cs:34` e `:51` — fazem `OrderBy(... .Name)` **sem
desempate**. Consequência e destino em D13.

**V12 — Convenção 11 não se aplica aqui, e isso é dito em vez de omitido.**
Não há acordo entre dois lados nesta change: `apps/workers` não lê a tabela, e
`apps/frontend` não muda. O único acordo entre apps que existiria — o resolvedor
de conhecimento contra o vínculo — é da etapa 4, e é lá que o teste com o
artefato real de um lado contra o outro precisa existir.

## Goals / Non-Goals

**Goals:**

- Persistir o conjunto de bases de conhecimento de cada agente, em `apps/api`,
  com substituição integral e rejeição atômica.
- Expor esse conjunto em **todas** as respostas de agente, com o mesmo nível de
  detalhe dos outros dois vínculos (id + nome), sem N+1 na listagem.
- Manter o espelho de EF Core de `apps/workers` sincronizado, verificado por
  teste de schema contra Postgres real.
- Deixar decidido — e registrado — o comportamento de base inativa, para que a
  etapa 4 o herde em vez de o redecidir.

**Non-Goals:**

- Qualquer tool, resolvedor ou leitura em runtime em `apps/workers`. É a
  etapa 4.
- Qualquer UI. É a etapa 5b.
- `TopK`, limiar de similaridade, seleção de documentos permitidos por vínculo,
  flag "injetar sempre" — nenhuma coluna extra no vínculo (D2).
- Rota inversa `GET /knowledge-bases/{id}/agents` (D8).
- Indexação, fragmentos, embedding — etapa 2, da qual esta change não depende.

## Árvore de pastas proposta

Só o que esta change cria ou toca. `(novo)` / `(modificado)`; migrações geradas
marcadas como tal.

```
apps/api/src/Buteco.Api/
├── AgentKnowledgeBindings/                                   (novo)
│   ├── AgentKnowledgeBaseLookup.cs                           (novo)
│   ├── Commands/
│   │   └── ReplaceAgentKnowledgeBases/
│   │       ├── ReplaceAgentKnowledgeBasesCommand.cs          (novo)
│   │       ├── ReplaceAgentKnowledgeBasesCommandHandler.cs   (novo)
│   │       └── ReplaceAgentKnowledgeBasesResult.cs           (novo)
│   ├── Endpoints/
│   │   └── AgentKnowledgeBindingEndpoints.cs                 (novo)
│   ├── Entities/
│   │   └── AgentKnowledgeBase.cs                             (novo)
│   └── Requests/
│       └── ReplaceAgentKnowledgeBasesRequest.cs              (novo)
├── KnowledgeBases/Responses/
│   └── KnowledgeBaseSummaryResponse.cs                       (novo)
├── Agents/
│   ├── Responses/AgentResponse.cs                            (modificado)
│   ├── Queries/ListAgents/ListAgentsQueryHandler.cs          (modificado)
│   ├── Queries/GetAgentById/GetAgentByIdQueryHandler.cs      (modificado)
│   └── Commands/
│       ├── CreateAgent/CreateAgentCommandHandler.cs          (modificado)
│       ├── UpdateAgent/UpdateAgentCommandHandler.cs          (modificado)
│       ├── ActivateAgent/ActivateAgentCommandHandler.cs      (modificado)
│       └── DeactivateAgent/DeactivateAgentCommandHandler.cs  (modificado)
├── AgentMcpBindings/Commands/ReplaceAgentMcpServers/
│   └── ReplaceAgentMcpServersCommandHandler.cs               (modificado)
├── AgentDelegations/Commands/ReplaceAgentDelegations/
│   └── ReplaceAgentDelegationsCommandHandler.cs              (modificado)
├── Infrastructure/
│   ├── AppDbContext.cs                                       (modificado)
│   └── Migrations/
│       ├── <ts>_AddAgentKnowledgeBaseBinding.cs              (gerado)
│       ├── <ts>_AddAgentKnowledgeBaseBinding.Designer.cs     (gerado)
│       └── AppDbContextModelSnapshot.cs                      (gerado)
└── Program.cs                                                (modificado)

apps/api/tests/Buteco.Api.Tests/
├── AgentKnowledgeBindingEndpointsTests.cs                    (novo)
├── AgentResponseWireFormatTests.cs                           (modificado)
└── RouteAuthenticationTests.cs                               (modificado)

apps/workers/src/Buteco.Workers/
├── Knowledge/Entities/
│   └── AgentKnowledgeBase.cs                                 (novo)
└── Infrastructure/
    ├── AppDbContext.cs                                       (modificado)
    └── Migrations/
        ├── <ts>_AddAgentKnowledgeBaseBinding.cs              (gerado)
        ├── <ts>_AddAgentKnowledgeBaseBinding.Designer.cs     (gerado)
        └── AppDbContextModelSnapshot.cs                      (gerado)

apps/workers/tests/Buteco.Workers.Tests/Knowledge/
└── KnowledgeSchemaMirrorTests.cs                             (modificado)
```

Nada entra em `libs/`. O vínculo é lido por um app só nesta etapa, e mesmo na
etapa 4 o que `apps/workers` precisa é a entidade espelhada, não código
compartilhado — a régua de dois-a-três consumidores reais da convenção 2 não é
atingida, e `AgentMcpServer`/`AgentDelegation` já provam que o espelho manual é
o padrão desta base.

## Decisions

### D1 — Seguir `AgentDelegation`, não `AgentMcpServer`

**Decisão**: o vínculo é ponteiro puro — duas colunas, chave composta, nenhuma
coluna extra —, e o corpo do `PUT` é uma **lista de Guid**, não uma lista de
objetos.

**Por quê**: a pergunta que decide é se o vínculo se parece mais com "servidor
com configuração por vínculo" ou com "ponteiro puro para outra entidade". As
duas colunas extras que existem no repositório existem por um motivo que aqui
não se repete: `AgentMcpServer.AllowedTools` guarda uma seleção que **não é
catálogo persistido em lugar nenhum** (as tools são descobertas ao vivo via
`tools/list`, por isso jsonb e não tabela filha — XML doc da entidade). Bases de
conhecimento são catálogo persistido, com id, e a seleção é o próprio conjunto
de ids. Não sobra nada para uma coluna guardar.

**Alternativa recusada**: seguir `AgentMcpServer` e já deixar o shape composto
(`[{ knowledgeBaseId, ... }]`) "para o dia em que precisar de `TopK`". Recusada
pela convenção 2: opção só existe quando há cenário real de alguém precisar de
outro valor, e o shape composto custa hoje (request extra, mapeamento extra,
resposta com campo vazio permanente) para pagar uma hipótese. Trocar
`[guid]` por `[{...}]` depois é uma migração de contrato, mas é uma que só se
faz se for necessária.

### D2 — Nenhuma coluna extra no vínculo (decisão herdada, não reaberta)

`TopK` e limiar de similaridade ficam **constante em `apps/workers`** até haver
dois consumidores reais querendo valores diferentes (convenção 2, mesmo
raciocínio do TTL de token de serviço). Não há flag "injetar sempre": o acesso é
sob demanda, decidido pelo modelo a partir de `KnowledgeBase.Description` — que
é justamente por isso obrigatória e não vazia desde a etapa 1.

Registrado aqui como decisão fechada em exploração anterior, para que a etapa 4
a encontre com o motivo junto em vez de a redescobrir.

### D3 — Rota `PUT /agents/{id}/knowledge-bases`, corpo `{ knowledgeBaseIds: [...] }`

**Decisão**: nomear a rota pela **entidade do outro lado**, como
`/mcp-servers`, e o campo pelos **ids**, como `targetAgentIds`.

**Por quê**: os dois precedentes divergem no nome da rota, e a divergência tem
causa: `/delegations` nomeia a relação porque `/agents/{id}/agents` seria
ilegível — os dois lados são `Agent`. Aqui os lados são entidades diferentes,
então o obstáculo que criou `/delegations` não existe, e a forma de
`/mcp-servers` se aplica direto. O nome do campo segue o outro eixo: para
vínculo sem atributo próprio o precedente é `targetAgentIds` (lista de Guid),
não `mcpServers` (lista de objetos).

**Alternativa recusada**: `PUT /agents/{id}/knowledge-bindings`. Inventa um
terceiro padrão de nomenclatura para resolver um problema que não existe.

### D4 — Duplicata no payload é deduplicada em silêncio

**Decisão**: `.Distinct()` sobre os ids recebidos, sem erro.

**Por quê**: é o que os **dois** precedentes fazem — `.Distinct()` na delegação,
`GroupBy(...).Last()` no MCP (que existe só porque lá o item é objeto composto e
alguém precisa vencer). Nenhum dos dois rejeita. Um `PUT` idempotente de
substituição integral tem semântica de conjunto; o cliente que envia o mesmo id
duas vezes está pedindo o mesmo estado final, e recusar seria rigor sem
benefício.

**Alternativa recusada**: 400 em duplicata. Divergiria dos dois precedentes sem
motivo registrado, e a UI da etapa 5b (seleção por checkbox) não consegue nem
produzir o caso.

### D5 — Base inexistente → 400; agente inexistente → 404; base inativa → aceita

**Decisão**: exatamente a matriz dos dois precedentes. O agente da URL é o
recurso: não existe → 404. Os ids do corpo são conteúdo do payload: qualquer um
inválido → 400 listando **todos** os inválidos, sem aplicar nada. Base com
`isActive: false` é vínculo válido e é aceita.

**Por quê**, na parte que os precedentes não dizem explicitamente (a inativa):
V3 mostra que o filtro de estado vive na *resolução*, não no cadastro, nos dois
casos. Manter a mesma divisão aqui é o que faz o operador poder desativar uma
base temporariamente sem perder a configuração de todos os agentes que a usam —
e o que faz reativá-la restaurar o comportamento sem reconfigurar nada.

### D6 — "Base inativa não é usada pelo agente" é contrato declarado, não código desta etapa

**Decisão**: a metade "não é usada" **não** entra na spec desta change. Ela vira
*ADDED requirement* da etapa 4, com o filtro `where kb.IsActive` no resolvedor,
no idioma de `McpToolSetResolver.cs:52`.

**Por quê**: nesta etapa não existe resolução — não há nada que consuma o
vínculo, e portanto nenhum gatilho verificável. Requisito que nenhum teste pode
reprovar é exatamente o padrão de falha que a convenção 10 nomeia, e a etapa 1
já enfrentou o mesmo caso: as três garantias de reindexação ficaram **decididas
em `design.md` (D9) e fora da spec**, herdadas pela etapa 2. Esta decisão é a
mesma forma, pelo mesmo motivo.

O que **entra** na spec desta change é a metade que tem gatilho: vincular uma
base inativa responde 200 e o vínculo persiste.

### D7 — `knowledgeBases` entra em `AgentResponse`, com id + nome

**Decisão**: campo novo em `AgentResponse`, item
`KnowledgeBaseSummaryResponse(Id, Name)`, servido pelos 8 sites de construção.
Sem `isActive`, sem `description`, sem contagem de documentos.

**Por quê**: id + nome é o nível de detalhe dos outros dois vínculos
(`McpServerSummaryResponse`, `AgentSummaryResponse`), e a verificação mostrou
que basta: a UI de vínculo MCP tira `isActive` do **catálogo**
(`GET /mcp-servers`, que ela já busca para montar a lista de seleção), não de
`agent.mcpServers` — ver `openspec/specs/agent-mcp-binding-ui/spec.md`,
cenários "Indicador distingue servidor MCP inativo na lista de seleção" e
"Servidor vinculado e inativo explica a consequência". A etapa 5b busca
`GET /knowledge-bases` pelo mesmo motivo e pelo mesmo caminho.

Contagem de documentos fica de fora por outro motivo, também verificado: exigiria
segunda consulta agregada por base, e a etapa 1 já registrou como handoff de UI
(convenção 17) que contagem que exige requisição extra não é assumida em
silêncio.

**Alternativa recusada**: rota própria `GET /agents/{id}/knowledge-bases`.
Obrigaria a etapa 5b a uma requisição a mais para montar a aba, sem nada em
troca — os outros dois vínculos vêm na mesma resposta.

### D8 — Sem rota inversa

**Decisão**: não existe `GET /knowledge-bases/{id}/agents` nesta change.

**Por quê**: V7. O repositório já resolveu esse problema uma vez, no MCP, e a
resposta foi derivar no cliente a partir de `GET /agents` — que passa a carregar
`knowledgeBases`. Criar a rota aqui seria um segundo padrão para a mesma
pergunta, e a etapa 5b nem a usaria se seguisse o precedente da tela irmã.

**Alternativa recusada**: incluir a rota "porque a projeção previa 2 CQRS". A
projeção é rascunho; o código é a fonte. **Se a etapa 5b descobrir que precisa
dela** (por exemplo, porque a tela de base fica pesada filtrando a lista inteira
de agentes), isso é achado a sequenciar — nunca backend improvisado dentro da
change de tela (convenção 1, corolário).

### D9 — Listagem em lote, por-agente nos demais

**Decisão**: `ListAgentsQueryHandler` ganha uma **terceira** consulta em lote,
espelhando as duas que já tem; os outros 7 sites usam
`AgentKnowledgeBaseLookup.GetLinkedKnowledgeBasesAsync`, uma consulta por
agente.

**Por quê**: é a divisão que já existe e o motivo dela está escrito no próprio
handler. Fora da listagem o handler já opera sobre um agente só — a consulta por
agente não é N+1 ali, é 1+1.

### D10 — FK em `Cascade`, chave primária composta

**Decisão**: `HasKey(b => new { b.AgentId, b.KnowledgeBaseId })`, e as duas FKs
com `OnDelete(DeleteBehavior.Cascade)` — igual a `AgentMcpServer` e
`AgentDelegation`.

**Por quê**, incluindo o ponto que parece contradizer a etapa 1: `KnowledgeDocument`
usa `Restrict` para a base **por decisão explícita** (D6 da etapa 1), para que
adicionar exclusão de base um dia seja escolha consciente em vez de sumiço
silencioso de documentos. Isso continua valendo e **não muda** aqui: enquanto
existir um documento, o `Restrict` dele já bloqueia a exclusão da base, então o
`Cascade` do vínculo nunca é alcançado sem que alguém decida antes o destino dos
documentos. Para uma linha de vínculo — que não é conteúdo e não tem valor sem
os dois lados — `Cascade` é o comportamento certo, e é o dos dois precedentes.
Hoje as duas cascatas são inertes: nem `Agent` nem `KnowledgeBase` têm rota de
exclusão.

O comportamento de exclusão é afirmado no teste de espelho de schema, não
deixado implícito.

**Achado da implementação (convenção 9), sem mudança de decisão**: o EF gera
automaticamente um índice `IX_agent_knowledge_bases_KnowledgeBaseId` para a
segunda coluna da chave composta — a PK já cobre `AgentId`, e sem esse índice a
FK para a base ficaria sem suporte. Não estava previsto na árvore de pastas
porque é artefato gerado, não arquivo escrito; sai idêntico nos dois apps.

**Segundo achado, sobre como verificar este guarda**: reintroduzir o defeito
mexendo **só** no `AppDbContext` de `apps/workers` derruba as 21 classes do
teste de espelho em 74 ms — é falha de fixture, não de asserção, porque o EF
barra a migração quando o modelo diverge do snapshot. Para o guarda provar o que
promete, o defeito precisa ser **auto-consistente**: `AppDbContext` + migração +
`AppDbContextModelSnapshot` + o `.Designer.cs` da migração, os quatro. Com isso,
reprova exatamente um teste. Registrado porque quem repetir esta verificação na
etapa 4 vai tropeçar no mesmo lugar.

### D11 — Espelho em `apps/workers` nesta change, com os guardas da etapa 1

**Decisão**: entidade espelhada + mapeamento idêntico + migração equivalente,
com as duas tarefas de guarda repetidas: `grep` confirmando que nenhum caminho
de runtime aplica migração em `apps/workers`, e a proibição de rodar
`dotnet ef database update` a partir de lá.

**Por quê**: V9. A regra vale igual, e ela é operacional — não tem checagem de
startup barata que a substitua (nada em runtime pergunta por migrações
pendentes ali), então o instrumento é registro mais tarefa de guarda, como a
etapa 1 já concluiu.

**Alternativa recusada**: deixar o espelho para a etapa 4, que é quem realmente
lê a tabela. Recusada porque os dois `AppDbContext` divergiriam durante todo o
intervalo, e a etapa 1 já pagou o preço de manter isso sincronizado por
disciplina — quebrar a disciplina uma vez é o suficiente para ela deixar de ser
verdade.

### D12 — Formato de fio afirmado sobre o JSON bruto

**Decisão**: um teste inspeciona o **texto** do JSON da resposta real e afirma a
chave `knowledgeBases`; não há enum novo, e isso é dito explicitamente.

**Por quê**: convenção 12. `knowledgeBases` não tem sigla nem maiúsculas
consecutivas, então a armadilha específica de `A2A` → `a2A` não se aplica — mas
teste que desserializa para `AgentResponse` passa pela mesma política nos dois
sentidos e é cego a qualquer erro de nome, e o custo de não ser cego é uma
asserção.

### D13 — Ordenar por nome **com desempate por identificador**

**Decisão**: `OrderBy(kb => kb.Name).ThenBy(kb => kb.Id)`, no lookup por agente
e no agrupamento em lote da listagem. O desempate entra na spec como requisito,
não como detalhe.

**Por quê**: V13. Nome de base de conhecimento não é único **por requisito**
(`knowledge-base-catalog`, "Nome duplicado é permitido"), então `OrderBy(Name)`
sozinho deixa a ordem entre homônimas a cargo do plano do PostgreSQL: a mesma
requisição pode devolver ordens diferentes sem nada ter mudado no cadastro, e um
teste de ordenação vira candidato a flake no dia em que alguém acrescentar um
caso com nomes iguais.

É a mesma classe de defeito que `dedupe-global-nome-de-tool` (`0a`) acabou de
corrigir em `apps/workers`, e o comentário escrito lá diz a regra:
`McpToolSetResolver.cs:47` — *"sem ele a ordem era a que o Postgres devolvesse
(…). Por `McpServerId`, e não por `Name`, porque o nome é editável"*. Aqui o
nome não é só editável: é não-único por requisito, o que é estritamente pior.

**Diferença que vale registrar, para não superdimensionar**: em `0a` a
não-determinação tinha consequência **semântica** — mudava qual tool mantinha o
nome-base no desempate de dedupe. Aqui a consequência é de **apresentação**: a
ordem de uma lista na resposta. Menor severidade, mesma causa, e a correção
custa uma cláusula.

**Alternativa recusada**: ordenar só por `Id`, como `McpToolSetResolver` faz.
Lá o critério é interno (nada é exibido); aqui o consumidor é a tela da etapa
5b, e ordem alfabética é o que serve ao operador. Nome como critério primário,
id como desempate, é o que atende os dois.

**Sobre os quatro sites pré-existentes**: `AgentDelegationLookup`,
`AgentMcpServerLookup` e as duas agregações de `ListAgentsQueryHandler` têm o
mesmo defeito, e **não são corrigidos aqui**. Convenção 12: defeito pertence a
quem expõe e se corrige em change própria sequenciada, nunca de carona. Fica
registrado como item em aberto no `02-HISTORICO_E_STATUS.md`, com gatilho
(tarefa 8.5). Uma atenuante real, que não muda o destino: nenhuma spec
existente promete ordem para `mcpServers` nem para `delegatesTo` — verificado,
não há a palavra "ordena/ordem/ordenado" em `agent-catalog`,
`agent-mcp-binding` nem `agent-delegation-binding` —, então lá é
não-determinação silenciosa sem contrato violado, enquanto aqui haveria
requisito de spec impossível de cumprir.

## Risks / Trade-offs

- **R1 — Os dois `AppDbContext` divergem no mapeamento do vínculo** (nome de
  tabela, chave composta, comportamento de exclusão), e a divergência só
  apareceria na etapa 4, dentro de outra change.
  → **Contraparte**: `KnowledgeSchemaMirrorTests` (apps/workers) estendido —
  aplica as migrações de `apps/workers` num Postgres limpo e afirma tabela,
  colunas, chave primária composta e as duas FKs com `CASCADE`. É o mesmo
  instrumento que fechou o risco equivalente da etapa 1.

- **R2 — Alguém aplica a migração de `apps/workers` contra banco real** e leva
  `42P07`.
  → **Contraparte**: tarefa de guarda que roda o `grep` por
  `MigrateAsync`/`GetPendingMigrations`/`EnsureCreated` em `apps/workers/src`
  (deve continuar sem resultado) e confere que `deploy/migrate/Dockerfile`
  continua buildando bundle só de `apps/api` e `apps/inbox`.

- **R3 — Um dos 8 sites de `AgentResponse.FromEntity` fica para trás** e alguma
  resposta devolve `knowledgeBases` sempre vazio.
  → **Contraparte**: o compilador pega os 8 (parâmetro posicional obrigatório),
  mas *compilar* só garante que alguém passou **alguma coisa** — passar `[]`
  compila igual. Por isso os cenários de spec cobrem os caminhos onde o erro
  seria silencioso e plausível: o `PUT` de **delegações** e o `PUT` de
  **servidores MCP** devolvendo o `knowledgeBases` correto de um agente que tem
  vínculo de conhecimento. São justamente os dois handlers em que a linha nova é
  cópia mecânica e passa despercebida na revisão.

- **R4 — N+1 na listagem**, se a terceira consulta em lote for escrita como
  chamada ao lookup por agente dentro do `Select`.
  → **Contraparte parcial, e o limite é dito**: há cenário de spec afirmando que
  a listagem devolve o vínculo correto para **vários** agentes ao mesmo tempo,
  o que pega o erro de *correção*; ele **não** reprova por N+1, que é defeito de
  desempenho. Contar consultas exigiria um `DbCommandInterceptor` — não existe
  nenhum no repositório, e introduzir a primeira infraestrutura de contagem de
  query por causa de uma linha de código é desproporcional (convenção 2). O
  risco fica coberto por revisão e pelo comentário já existente no handler, e
  isso é registrado como escolha, não como esquecimento (convenção 10).

- **R5 — A metade não testável de "base inativa"** — que o agente não usa a base
  inativa — fica sem guarda até a etapa 4.
  → **Contraparte**: D6 a registra como *ADDED requirement* herdado pela
  etapa 4, do mesmo jeito que a etapa 1 fez com as três garantias de
  reindexação. O que é testável aqui (vincular base inativa responde 200 e
  persiste) está na spec.

- **R6 — Aceitar base inativa é decisão de produto que pode surpreender o
  operador**: ele vincula, salva, e nada acontece na execução.
  → **Contraparte**: é handoff explícito para a etapa 5b — a aba precisa do
  aviso equivalente ao que a aba de MCP já tem ("Servidor vinculado e inativo
  explica a consequência", `agent-mcp-binding-ui`). Registrado como item de
  handoff no fechamento, não como algo que esta change resolve.

- **R7 — O `ThenBy(kb => kb.Id)` de D13 é removido por alguém que o lê como
  redundante**, e a ordem volta a depender do plano do Postgres — invisível
  enquanto nenhuma base homônima existir no cadastro de teste.
  → **Contraparte**: cenário de spec com **duas bases de mesmo nome** afirmando
  ordem estável entre duas consultas, mais o guarda da tarefa 7.1 (remover o
  `ThenBy` deve reprovar esse cenário e **só** ele — a segunda metade da
  convenção 15: um guarda que reprova o cenário de ordenação alfabética junto
  está afirmando a garantia no lugar errado).

- **Trade-off aceito — vínculo sem `AllowedTools`-análogo**: um agente vinculado
  a uma base tem acesso a **todos** os documentos dela. Quem precisar de
  granularidade menor cria outra base. É a mesma resposta que o catálogo dá para
  "assuntos diferentes", e evita uma segunda dimensão de permissão sem cenário
  real (convenção 2).

## Tamanho projetado (convenção 18 — reprojeção **depois** da verificação)

A faixa de rascunho herdada da etapa 1 era **16-24 arquivos / 900-1400 linhas**
(tabela de reprojeção em `02-HISTORICO_E_STATUS.md`), assumindo "~2 CQRS
(replace + list), 1 grupo de endpoints, espelho, ~20 cenários". Reprojetado por
componente agora que V1-V12 fecharam, só **código** (artefatos OpenSpec fora da
conta), com os custos unitários medidos na etapa 1 (~2 arquivos e ~37 linhas por
operação CQRS, ~19-21 linhas por cenário de teste, ~150 linhas por grupo de
endpoints):

| origem | arquivos | linhas |
|---|---|---|
| CQRS (1 operação: command + handler + result) | 3 | 90-110 |
| Endpoints (1 grupo, 1 rota) | 1 | 55-70 |
| Entidade de vínculo + lookup + request + summary response | 4 | 90-110 |
| Espelho `apps/workers` (entidade) | 1 | 25-30 |
| Modificados: `AppDbContext` ×2, `AgentResponse`, `Program.cs`, 8 handlers | 12 | 60-80 |
| Testes novos (`AgentKnowledgeBindingEndpointsTests`) | 1 | 310-380 |
| Testes modificados (wire format, route auth, espelho de schema) | 3 | 60-90 |
| **Total à mão** | **25** | **690-870** |
| *(migrações geradas ×2 apps, não contam)* | *6* | *~150-200* |

**A verificação mexeu a projeção nas duas direções, e o que vale registrar é a
causa de cada uma — não a direção, que sozinha vira outra heurística cega:**

*(A faixa subiu de 630-810 para 690-870 na revisão da proposta, por três
cenários acrescentados — agente inativo, desempate de ordenação, e o par de
consulta que ele exige. Registrado em vez de reescrito em silêncio: é mais uma
medida de quanto uma revisão acrescenta depois da verificação, e a série da
convenção 18 é feita disso.)*

- **Linhas caíram** (~1150 → ~780 no ponto médio) porque V7 **removeu um
  componente inteiro** da projeção: a rota inversa, que a projeção da etapa 1
  contava como segunda operação CQRS, não existe no precedente. *Causa: a
  projeção conta os componentes que a etapa parece precisar; a verificação pode
  descobrir que o repositório já resolveu um deles de outro jeito.*
- **Arquivos subiram** dentro da faixa. *Causa, e é a reutilizável:* **projeção
  por operação CQRS conta arquivos criados, não modificados**, e o blast radius
  de um record compartilhado é **todo em modificação** — 12 arquivos aqui, 11
  deles tocados por uma a três linhas. A decomposição da etapa 1 já tinha esse
  dado à vista e ninguém o usou como custo unitário: a linha "Modificados" era
  5 arquivos / 301 linhas, e nenhum dos custos por operação CQRS a produz.

O perfil é **reutilizável muito além desta change**: qualquer change que
acrescente um campo a um response usado por N handlers tem contagem de arquivo
dominada por modificação, e é sempre a dimensão que a projeção por componente
subestima.

### Medido no fechamento

| | projetado | entregue |
|---|---|---|
| arquivos à mão | 25 | **25** |
| linhas à mão | 690-870 | **935** |
| *(migração gerada)* | *6 / 150-200* | *6 / 860* |

**Arquivos: exato.** Projetar "modificados" separado, a partir do blast radius
lido no código, funcionou — e os 8 sites saíram com 1 a 3 linhas cada
(3,3,3,3,3,3,2,1), como o perfil previa.

**Linhas: 7,5% acima do topo**, e a diferença está inteira nos testes (537
entregues contra 370-470 projetados; produção veio 398 contra ~320-400, dentro
da faixa). A causa é conhecida e é a mesma da etapa 1: **o custo por cenário de
teste ~19-21 linhas subestima quando o cenário precisa de arranjo próprio.** Os
três cenários mais caros desta change — desempate com ordem de inserção
invertida, listagem com três agentes e dois vínculos cruzados, e os dois PUT
vizinhos com catálogo MCP montado — custam 25-40 linhas cada, não 20.

**A migração gerada errou por 4x** (860 contra 150-200), e o motivo é que o
`.Designer.cs` carrega o snapshot **inteiro** do modelo, não só a tabela nova.
Não afeta o trabalho à mão, mas explica por que o headline do commit (39
arquivos / 2922 linhas) é **3,1x** o trabalho real — a razão mais extrema já
medida nesta base, e a terceira confirmação da primeira metade da convenção 18.

## Migration Plan

Aditivo puro: uma tabela nova, nenhuma tabela ou coluna existente alterada.
Migração aplicada por `apps/api` (a de `apps/workers` é ferramental de
design-time e só roda contra Testcontainer). Rollback é o `Down` gerado —
`DROP TABLE agent_knowledge_bases` —, sem perda de dado de nenhuma outra
entidade. Não há backfill: agentes existentes ficam com o conjunto vazio, que é
o estado correto.

## Open Questions

Nenhuma. As duas incertezas que existiam quando esta etapa foi esboçada — qual
precedente seguir, e se havia rota inversa a construir — foram fechadas por
leitura do código (V1 e V7), não por decisão de produto pendente. O único item
que depende de decisão de produto futura, o aviso de base inativa na tela, está
registrado como handoff da etapa 5b (R6), não como pergunta aberta desta change.
