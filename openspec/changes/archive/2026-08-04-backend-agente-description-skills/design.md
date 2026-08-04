## Context

`Agent` hoje persiste `Name`, `Instructions`, `IsActive`, `Provider`,
`Model`. A change seguinte (`backend-a2a-agent-card`) precisa montar um
`AgentCard` A2A real, que exige `description` e `skills` publicáveis por
agente. Esta change entrega só o dado — sem nenhum formato ou lógica A2A.

Padrões já estabelecidos no código e reaproveitados aqui:
- `AgentMcpServer.AllowedTools` ([AgentMcpServer.cs](../../../apps/api/src/Buteco.Api/AgentMcpBindings/Entities/AgentMcpServer.cs)):
  lista persistida como coluna `jsonb`, sem tabela filha, com
  `HasConversion` + `ValueComparer` explícito em `AppDbContext`
  ([AppDbContext.cs:78-90](../../../apps/api/src/Buteco.Api/Infrastructure/AppDbContext.cs)).
- `AgentEndpoints.ValidateShape`
  ([AgentEndpoints.cs:100-125](../../../apps/api/src/Buteco.Api/Agents/Endpoints/AgentEndpoints.cs)):
  validação manual de campos obrigatórios no endpoint, retornando
  `ValidationProblem` com chave por campo — sem FluentValidation ou
  equivalente no projeto.
- `AgentMcpBindingEndpoints`
  ([AgentMcpBindingEndpoints.cs:56-58](../../../apps/api/src/Buteco.Api/AgentMcpBindings/Endpoints/AgentMcpBindingEndpoints.cs)):
  chave de erro por item de lista no formato `mcpServers[{id}].allowedTools`.
- `Agent.Provider`/`Agent.Model` nullable
  ([Agent.cs:13-21](../../../apps/api/src/Buteco.Api/Agents/Entities/Agent.cs)):
  nullable por serem migration-artifact — linhas pré-existentes sem
  default seguro, ficam num estado implícito de "precisa de
  reconfiguração". Este NÃO é o motivo da nullability de `Description`
  nesta change (ver Decision 4).
- `McpServer.Description`
  ([McpServer.cs:9](../../../apps/api/src/Buteco.Api/McpServers/Entities/McpServer.cs)):
  campo obrigatório (`string`, não nullable, `IsRequired()` em
  `AppDbContext`). Citado aqui só como contraste — não é precedente para
  `Agent.Description` ser nullable.

## Estrutura de Arquivos

Nenhum arquivo novo além da migration — todos os demais são edições em
arquivos já existentes:

```
apps/api/src/Buteco.Api/
├── Agents/
│   ├── Entities/
│   │   └── Agent.cs                          (editado: +Description, +Skills, +Skill)
│   ├── Commands/
│   │   ├── CreateAgent/
│   │   │   ├── CreateAgentCommand.cs          (editado: +Description, +Skills)
│   │   │   └── CreateAgentCommandHandler.cs   (editado)
│   │   └── UpdateAgent/
│   │       ├── UpdateAgentCommand.cs          (editado: +Description, +Skills)
│   │       └── UpdateAgentCommandHandler.cs   (editado)
│   ├── Requests/
│   │   ├── CreateAgentRequest.cs              (editado: +Description, +Skills)
│   │   └── UpdateAgentRequest.cs              (editado: +Description, +Skills)
│   ├── Responses/
│   │   └── AgentResponse.cs                   (editado: +Description, +Skills)
│   └── Endpoints/
│       └── AgentEndpoints.cs                  (editado: ValidateShape valida Skills)
└── Infrastructure/
    ├── AppDbContext.cs                        (editado: mapeamento jsonb de Skills)
    └── Migrations/
        └── <timestamp>_AddAgentDescriptionAndSkills.cs   (novo)

apps/api/tests/Buteco.Api.Tests/
├── CreateAgentCommandHandlerTests.cs          (editado: casos de Description/Skills)
├── AgentEndpointsTests.cs                     (editado: casos de Description/Skills)
└── AgentDescriptionAndSkillsMigrationTests.cs (novo: agente legado após migration)
```

Sem novo diretório, sem novo projeto, sem conteúdo em `libs/` — a
mudança inteira vive dentro de `Agents/` e `Infrastructure/`, já
existentes em `apps/api`.

## Goals / Non-Goals

**Goals:**
- Persistir `Description` (texto livre, opcional) e `Skills` (lista de
  `{ Name, Description? }`) em `Agent`.
- Aceitar os dois campos em `POST /agents` e `PUT /agents/{id}`, refletidos
  em todas as respostas de agente (`POST`, `GET /agents`,
  `GET /agents/{id}`, `PUT`).
- Validar que cada `Skill` em `Skills` tem `Name` não vazio.
- Manter agentes existentes funcionando sem migração especial:
  `Description` nulo, `Skills` vazio.

**Non-Goals:**
- Nenhuma relação com o protocolo A2A ou `AgentCard` — isso é
  `backend-a2a-agent-card`.
- Nenhuma deduplicação ou unicidade de `Skills`, dentro de um agente ou
  entre agentes.
- Nenhuma mudança em `apps/workers` ou `apps/frontend`.
- Nenhum endpoint dedicado para `Skills` — entra pelos comandos existentes
  de criar/atualizar agente.

## Decisions

### Decision 1: Skills como coluna jsonb em Agent, sem tabela filha

`AgentMcpServer` é uma relação N:N contra `McpServer` — uma entidade
cadastrada separadamente, com identidade própria, reutilizável entre
agentes, consultável por si só (`GET /mcp-servers`). `Skill` não é isso:
é declarado dentro de cada agente, sem catálogo compartilhado por trás.
Dois agentes com uma skill de mesmo `Name`/`Description` não têm nenhuma
relação entre si — não existe a pergunta "quais agentes têm a skill X"
como consulta relacional de primeira classe nesta fatia.

Esse é o mesmo raciocínio já usado para `AgentMcpServer.AllowedTools`
virar `jsonb` em vez de tabela filha (Decision 1 do design.md de
`backend-mcp-selecao-tools`): sem necessidade de FK, sem necessidade de
consulta relacional. `Skills` reaproveita o padrão técnico exato (mesma
estratégia de `HasConversion` + `ValueComparer`), mas é a primeira vez
que esse tipo de dado (lista jsonb) aparece diretamente em `Agent`, não
em uma entidade de vínculo — por isso o registro como Decision própria
aqui, não só herdada por analogia.

**Alternativa descartada**: tabela `AgentSkill` própria (FK para
`Agent`, colunas `Name`/`Description`). Descartada por falta de
necessidade concreta — nenhum requisito desta fatia ou da próxima
(`backend-a2a-agent-card`) precisa consultar skills fora do contexto do
agente que as declara.

### Decision 2: Skills entra em CreateAgentCommand/UpdateAgentCommand, sem endpoint próprio

`ReplaceAgentMcpServers` ganhou `PUT /agents/{id}/mcp-servers` e um
Command dedicado porque a operação precisa: descobrir tools ao vivo via
handshake contra o `McpServer` (`tools/list`), validar a seleção de
`AllowedTools` contra esse resultado, e reportar falha de handshake como
502 atomicamente separado de erro de validação 400 (ver
`AgentMcpBindingEndpoints.cs:63-71`). Nenhuma dessa complexidade existe
para `Skill`: não há handshake, não há validação contra sistema externo,
é texto declarado pelo operador da mesma natureza que `Name` e
`Instructions`.

Por isso `Skills` entra direto em `CreateAgentCommand`/
`UpdateAgentCommand`, ao lado de `Name`/`Instructions`/`Description` —
sem endpoint ou Command dedicado.

**Alternativa descartada**: endpoint próprio (`PUT /agents/{id}/skills`),
espelhando o padrão de MCP. Descartada porque replicaria a forma sem
replicar a razão — a forma dedicada existe em MCP para acomodar uma
validação externa que `Skills` não tem.

### Decision 3: Shape provisório de Skill — `{ Name: string, Description: string? }`

`Name` obrigatório (mesma validação de string obrigatória já usada em
`Name`/`Instructions` de `Agent`, via `ValidateShape` no endpoint).
`Description` opcional.

Este shape é deliberadamente mais simples que o `AgentSkill` do protocolo
A2A, que provavelmente exige campos adicionais (ex. `id`, `tags`,
`examples` — a confirmar contra o SDK real, via `/opsx:explore`, na
change `backend-a2a-agent-card`, não aqui). Esta fatia entrega o dado
mínimo útil para o operador descrever capacidades do agente em texto;
o mapeamento para o shape exigido pelo protocolo A2A — incluindo
qualquer campo adicional, geração de `id` estável, ou transformação de
formato — é responsabilidade inteira da próxima change.

### Decision 4: Agent.Description é nullable — justificativa própria

`Description` fica nullable (`string?`). A razão é que é um campo
genuinamente opcional por natureza: nem todo agente precisa de uma
descrição textual para funcionar, diferente de `Name`/`Instructions`
(sem os quais o agente não tem identidade nem comportamento) ou de
`Provider`/`Model` (sem os quais o agente não consegue processar nenhuma
task).

Isto **não** é o mesmo motivo da nullability de `Provider`/`Model`
([Agent.cs:13-21](../../../apps/api/src/Buteco.Api/Agents/Entities/Agent.cs)):
aqueles são nullable como artefato de migration — linhas que já existiam
antes da coluna existir, sem default seguro e universal possível. Esse
motivo não se aplica a `Description`: nenhum agente existente terá
`Description` de qualquer forma até esta coluna existir, e o default
(`null`) é seguro e universal para todos eles por igual — é o mesmo tipo
de migração aditiva sem drama que `Skills` (`[]`) recebe.

Também não é por analogia com `McpServer.Description`: esse campo é, na
verdade, **obrigatório**
([McpServer.cs:9](../../../apps/api/src/Buteco.Api/McpServers/Entities/McpServer.cs),
`IsRequired()` em `AppDbContext`) — é o único texto identificador de um
`McpServer` dentro do catálogo compartilhado entre agentes, então faz
sentido exigi-lo. `Agent.Description` não carrega essa mesma pressão:
não existe catálogo compartilhado de agentes sendo navegado por
descrição, e o `Name` já cumpre o papel de identificador.

**Alternativa descartada**: `Description` obrigatório, espelhando
`McpServer.Description` como precedente real (validação igual a
`Name`/`Instructions`, 400 se vazio). Descartada porque forçaria todo
operador a escrever uma descrição textual mesmo quando o agente é
autoexplicativo pelo `Name` e pelas `Instructions` já obrigatórias — sem
ganho concreto identificado para esta fatia.

## Risks / Trade-offs

- **[Risco] Shape de `Skill` provisório pode exigir migração de dados na
  próxima change** se o protocolo A2A exigir campos adicionais (ex. `id`
  estável por skill) → **Mitigação**: nenhuma, aceito conscientemente
  (Decision 3). O shape mínimo evita over-engineering especulativo agora;
  qualquer campo novo necessário na próxima change é uma migration
  aditiva simples (mesmo padrão desta), sem quebra do que já existe.
- **[Risco] `ValueComparer` incorreto para `Skills` (lista de objetos, não
  de strings) pode fazer o EF Core não detectar mudanças corretamente no
  change tracking** → **Mitigação**: implementar o `ValueComparer` com
  igualdade estrutural por elemento (mesmo padrão de
  `AllowedTools`, adaptado de `SequenceEqual` sobre `string` para
  `SequenceEqual` sobre o record `Skill`, que já tem igualdade estrutural
  por ser `record`), coberto por teste de atualização substituindo o
  conjunto inteiro.
- **[Trade-off] `Skills` como jsonb sem tabela filha** significa que,
  se uma change futura precisar consultar "quais agentes têm a skill X"
  relacionalmente, será necessário migrar de jsonb para tabela — aceito
  porque nenhum requisito atual ou da próxima change precisa disso
  (mesmo trade-off já aceito para `AllowedTools`).

## Migration Plan

1. Migration EF Core aditiva única: `ALTER TABLE agents ADD COLUMN
   description text NULL; ALTER TABLE agents ADD COLUMN skills jsonb NOT
   NULL DEFAULT '[]'::jsonb;` — segue exatamente o padrão de
   `AddAgentMcpServerAllowedTools` (20260803010705).
2. Sem backfill: agentes existentes recebem `description = NULL` e
   `skills = '[]'` automaticamente via `DEFAULT`.
3. Deploy: migration aplicada antes do binário novo subir (mesmo processo
   já usado nas migrations anteriores do projeto). Sem coordenação
   especial com `apps/workers` — `apps/workers` não lê `agents`
   diretamente (isolamento estrito entre apps).
4. Rollback: `DROP COLUMN` reverte sem perda de dado relevante (colunas
   novas, sem dependentes). Reverter o binário sem reverter a migration é
   seguro — colunas adicionais nullable/com default não quebram código
   antigo que as ignora.

## Open Questions

Nenhuma pergunta de negócio/produto em aberto para esta fatia — o shape
provisório de `Skill` (Decision 3) é uma decisão tomada, não uma
pergunta pendente; sua evolução fica explicitamente para
`backend-a2a-agent-card`.
