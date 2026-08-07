## 1. Modelo e persistência (apps/api)

- [x] 1.1 Criar entidade `AgentDelegation` em
  `AgentDelegations/Entities/AgentDelegation.cs` (`SourceAgentId`,
  `TargetAgentId`, chave composta, sem propriedade além das duas FKs),
  construtor público, sem setters públicos — mesmo estilo de
  `AgentMcpServer`
- [x] 1.2 Adicionar `DbSet<AgentDelegation>` a `AppDbContext`, com
  configuração fluente da tabela `agent_delegations` em
  `OnModelCreating`: chave composta `(SourceAgentId, TargetAgentId)`, duas
  relações `HasOne<Agent>().WithMany()` distintas (uma via
  `HasForeignKey(d => d.SourceAgentId)`, outra via
  `HasForeignKey(d => d.TargetAgentId)`), `OnDelete(DeleteBehavior.
  Cascade)` nas duas
- [x] 1.3 Gerar migration EF Core `AddAgentDelegationCatalog` e validar
  contra o Postgres de desenvolvimento (`dotnet ef database update`).
  Confirmado: EF Core distingue as duas FKs para `Agent` sem ambiguidade,
  gerando `FK_agent_delegations_agents_SourceAgentId` e
  `FK_agent_delegations_agents_TargetAgentId`, ambas `ON DELETE CASCADE`

## 2. Vínculo de delegação — substituição do conjunto (apps/api)

- [x] 2.1 Criar `AgentDelegationLookup` (mesmo papel de
  `AgentMcpServerLookup`): consulta compartilhada que retorna
  `IReadOnlyList<AgentSummaryResponse>` dos agentes-alvo vinculados como
  delegação de saída de um agente, usada por todos os handlers que montam
  `AgentResponse`
- [x] 2.2 Criar `ReplaceAgentDelegationsCommand`/`Handler`/`Result`
  (`AgentId`, `TargetAgentId[]`): valida que o agente Source existe (404 se
  não), valida que todos os `TargetAgentId` existem (400 se algum não
  existir, independente de estar ativo ou inativo), rejeita
  auto-delegação (`SourceAgentId == TargetAgentId`, 400), substitui o
  conjunto de `AgentDelegation` inteiro (remove os que saíram, adiciona os
  que entraram), rejeição sempre atômica (nenhum caso de erro aplica
  nenhum vínculo do payload). `Result` é um sinal neutro sem referência a
  `Microsoft.AspNetCore.Http.HttpResults`, mesmo formato de
  `ReplaceAgentMcpServersResult`
- [x] 2.3 Criar `ReplaceAgentDelegationsRequest`
  (`{ TargetAgentIds: Guid[]? }`) e mapear
  `PUT /agents/{id}/delegations` em `AgentDelegationEndpoints.cs`,
  seguindo o mesmo padrão de validação de forma + tradução de resultado já
  usado em `AgentMcpBindingEndpoints.cs`
- [x] 2.4 Testes de integração (`WebApplicationFactory` + Testcontainers
  Postgres): vincular conjunto inicial, substituir por conjunto diferente,
  remover todos os vínculos (lista vazia), idempotência ao reenviar o
  mesmo conjunto, rejeitar `TargetAgentId` inexistente (400, sem alterar
  vínculos existentes), rejeitar auto-delegação (400, sem alterar vínculos
  existentes), agente Source inexistente → 404, vincular a agente Target
  inativo é aceito
- [x] 2.5 Teste de integração cobrindo ciclo indireto e par bidirecional
  permitidos: cadastrar A→B, B→C, C→A em chamadas separadas sem nenhuma
  rejeitada; cadastrar A→B e depois B→A sem nenhuma rejeitada

## 3. AgentResponse com delegações de saída (apps/api)

- [x] 3.1 Criar `AgentSummaryResponse(Guid Id, string Name)` em
  `Agents/Responses/AgentSummaryResponse.cs`, com
  `FromEntity(Agent agent)`
- [x] 3.2 Adicionar campo `DelegatesTo: AgentSummaryResponse[]` a
  `AgentResponse`, e um terceiro parâmetro `delegatesTo` a
  `AgentResponse.FromEntity`
- [x] 3.3 Atualizar os 7 pontos de chamada de `AgentResponse.FromEntity`
  para passar `delegatesTo`: `CreateAgentCommandHandler` (`[]` fixo, mesmo
  motivo que já passa `[]` fixo para `mcpServers`), `UpdateAgentCommandHandler`,
  `ActivateAgentCommandHandler`, `DeactivateAgentCommandHandler`,
  `GetAgentByIdQueryHandler`, `ListAgentsQueryHandler` (buscando via
  `AgentDelegationLookup`, em lote para evitar N+1, mesmo espírito do
  agrupamento já usado para `mcpServers` em `ListAgentsQueryHandler`), e
  `ReplaceAgentMcpServersCommandHandler` (passa a também buscar
  `delegatesTo` via `AgentDelegationLookup`)
- [x] 3.4 Atualizar `ReplaceAgentDelegationsCommandHandler` (criado na
  seção 2) para também buscar `mcpServers` via `AgentMcpServerLookup` ao
  montar o `AgentResponse` de retorno
- [x] 3.5 Testes de integração: `GET /agents/{id}` e `GET /agents`
  refletem o conjunto de agentes-alvo vinculados (id + name) como
  `delegatesTo` após `PUT /agents/{id}/delegations`; agente sem vínculo
  retorna `delegatesTo` como lista vazia em ambos os endpoints

## 4. Revisão final (apps/api)

- [x] 4.1 Rodar a suíte completa de `Buteco.Api.Tests` e confirmar que os
  testes existentes de `Agent` e `AgentMcpBinding` continuam passando com
  o novo campo `delegatesTo` na resposta. Suíte completa: **136/136
  aprovados** (12 novos testes de `AgentDelegationEndpointsTests`, mais os
  124 testes pré-existentes, todos passando sem alteração)
- [x] 4.2 Revisar que nenhum código novo referencia `apps/workers` ou
  `apps/frontend`, e que nenhuma mudança foi feita fora de `apps/api`.
  Confirmado via `git status`/`git diff --name-only`: todos os arquivos
  tocados estão em `apps/api/**` ou `openspec/changes/**`; nenhuma
  referência a `Buteco.Workers`/`apps/frontend` em código novo
