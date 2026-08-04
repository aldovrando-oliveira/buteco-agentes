## Why

Hoje `AgentA2AServerRegistry` constrói o `A2AServer` de cada agente sem
nenhum `AgentCard` — não há forma de um cliente A2A externo descobrir nome,
descrição, skills ou capacidades de um agente antes de decidir invocá-lo. A
change anterior (`backend-agente-description-skills`) já adicionou
`Description`/`Skills` ao agente exatamente para alimentar este card, mas
deixou o mapeamento para o shape do protocolo inteiramente para esta change.
Sem o card, a descoberta externa via A2A está incompleta mesmo com o
`SendMessage`/`GetTask` já funcionando.

## What Changes

- Novo endpoint HTTP `GET /agents/{id}/.well-known/agent-card.json` em
  `apps/api`, irmão do `POST /agents/{id}/a2a` já existente, que monta e
  retorna o `AgentCard` do protocolo A2A a partir dos dados atuais do
  agente (lidos direto do banco a cada requisição, sem cache).
- Mapeamento de `Agent.Skills` (`{ Name, Description? }`) para o
  `AgentSkill` do protocolo (`{ Id, Name, Description, Tags, ... }`),
  incluindo geração de um `Id` estável por skill.
- Nova configuração de URL pública base da API (`PublicUrl:BaseUrl`),
  usada para montar `AgentCard.SupportedInterfaces[].Url`, documentada em
  `.env.example`.
- `AgentCard.Capabilities.Streaming` e `.PushNotifications` fixados em
  `false` (nenhum dos dois implementado).
- Card exposto com `200 OK` para qualquer agente existente, independente de
  `IsActive`/`Provider`/`Model` — mesmo princípio já usado em
  `GET /agents/{id}`. Só `404` quando o `id` não corresponde a nenhum
  agente.

## Capabilities

### New Capabilities
- `a2a-agent-card`: exposição do `AgentCard` do protocolo A2A por agente
  cadastrado, via endpoint HTTP dedicado de descoberta (`.well-known`),
  resolvido a partir do estado atual do agente a cada requisição.

### Modified Capabilities
(nenhuma — `a2a-task-lifecycle` não muda; `SendMessage`/`GetTask` e o
comportamento de `AgentA2AServerRegistry` permanecem exatamente como estão.)

## Impact

- **apps/api**: novo endpoint em `Program.cs` (ou módulo de endpoints
  dedicado, a definir em design.md); nenhuma mudança em
  `AgentA2AServerRegistry.cs`, `RoutingA2ARequestHandler.cs` ou nos
  handlers de `SendMessage`/`GetTask`. Nova classe de opções para a URL
  pública base, seguindo o padrão de `CorsOptions`/`McpCryptoOptions`.
- **apps/workers**: nenhuma mudança — o card não influencia a execução do
  agente pelo LLM.
- **apps/frontend**: nenhuma mudança nesta fatia.
- **Configuração**: nova variável de ambiente (`PublicUrl__BaseUrl` ou
  equivalente) documentada em `.env.example`.
- **Dependências externas**: nenhuma nova — reaproveita `A2A`/
  `A2A.AspNetCore` já referenciados, sem usar `MapWellKnownAgentCard` nem
  `GetExtendedAgentCardAsync` do SDK (ambos incompatíveis com múltiplos
  agentes por host, ver design.md).
