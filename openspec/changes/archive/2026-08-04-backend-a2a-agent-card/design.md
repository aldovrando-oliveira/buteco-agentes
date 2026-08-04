## Context

`AgentA2AServerRegistry` ([AgentA2AServerRegistry.cs](../../../apps/api/src/Buteco.Api/A2A/AgentA2AServerRegistry.cs))
constrói um `A2AServer` por agente (`ConcurrentDictionary<Guid, A2AServer>`),
mas nenhum deles carrega um `AgentCard` — o construtor do SDK não aceita
um. `RoutingA2ARequestHandler` resolve por requisição qual `A2AServer` trata
uma chamada JSON-RPC lendo o route value `id`, porque o SDK assume um único
agente por host (mesma limitação documentada em
`openspec/changes/archive/2026-07-26-backend-agente-a2a-mvp/design.md`).

Esta change adiciona a peça que falta: descoberta externa via `AgentCard`,
consumindo `Description`/`Skills` que a change anterior
(`backend-agente-description-skills`) já persiste em `Agent`, mas deixou
deliberadamente sem mapeamento para o shape do protocolo (`Skill.cs`:
"shape provisório... o mapeamento para o shape exigido pelo protocolo fica
inteiramente para a change backend-a2a-agent-card").

**Investigação feita contra o SDK real** (decompilação de
`A2A`/`A2A.AspNetCore` 1.0.0-preview2 via `ilspycmd`, não apenas o README —
o README mostra um padrão `TaskManager` que não corresponde ao que o
projeto usa; a fonte de verdade foi o IL decompilado):

1. `A2AServer` só tem um construtor:
   `A2AServer(IAgentHandler, ITaskStore, ChannelEventNotifier, ILogger<A2AServer>, A2AServerOptions?)`.
   `A2AServerOptions` só expõe `AutoAppendHistory`. **Não existe** overload
   que aceite `AgentCard`.
2. `A2AServer.GetExtendedAgentCardAsync` (método JSON-RPC
   `GetExtendedAgentCard`) é `virtual`, mas a implementação base sempre
   lança:
   ```csharp
   throw new A2AException("Extended agent card not configured.", A2AErrorCode.ExtendedAgentCardNotConfigured);
   ```
3. `A2ARouteBuilderExtensions.MapWellKnownAgentCard(endpoints, agentCard, path)`
   fecha sobre uma única instância de `AgentCard` capturada no momento do
   registro (`() => Results.Ok(agentCard)`) — sem overload com
   delegate/factory. É por-registro-único, incompatível com N agentes por
   host roteados por `{id}` em runtime.
4. `AgentCard` (protocolo v1.0, não v0.3) não tem mais campo escalar `.Url`
   — foi substituído por `SupportedInterfaces: List<AgentInterface>`, cada
   um com `{ Url, ProtocolBinding, ProtocolVersion, Tenant? }`.
5. `AgentSkill` real: `{ Id, Name, Description, Tags }` obrigatórios
   (`[JsonRequired]`) + `{ Examples, InputModes, OutputModes,
   SecurityRequirements }` opcionais. Mais rico que o `Skill` provisório
   do domínio (`{ Name, Description? }`).

## Goals / Non-Goals

**Goals:**
- Expor um `AgentCard` real, por agente, refletindo o estado atual
  (`Name`, `Description`, `Skills` mapeadas) a cada requisição — sem
  staleness.
- Reaproveitar exatamente o padrão de roteamento customizado já
  estabelecido (`RoutingA2ARequestHandler`/`Program.cs`), em vez de um
  mecanismo paralelo.
- Deixar claro, no código e na configuração, que campos do protocolo
  (`AgentCard.Provider`) e conceitos de domínio homônimos
  (`Agent.Provider`) são coisas diferentes.

**Non-Goals:**
- Nenhuma autenticação ou "extended agent card" diferenciado — não existe
  auth no projeto (`docs/a2a-integration.md`: "Sem autenticação"). O
  método JSON-RPC `GetExtendedAgentCard` continua fora do contrato
  suportado, exatamente como já é hoje para streaming/cancelamento/listar
  tasks/push notification.
- Nenhuma mudança em `apps/workers` — `Description`/`Skills` não
  influenciam a execução do agente pelo LLM, só a descoberta.
- Nenhuma mudança em `apps/frontend` nesta fatia.
- Nenhuma mudança em `AgentA2AServerRegistry.cs`, `RoutingA2ARequestHandler.cs`
  ou no comportamento de `SendMessage`/`GetTask`.
- `capabilities.streaming`/`.pushNotifications` continuam `false` — nenhum
  dos dois implementado (Non-Goal desde `backend-agente-a2a-mvp`).

## Estrutura de Arquivos

```
apps/api/src/Buteco.Api/
├── A2A/
│   └── AgentCardEndpoints.cs          (novo: endpoint GET + montagem do AgentCard)
├── Options/
│   └── PublicUrlOptions.cs            (novo: BaseUrl da API para SupportedInterfaces)
└── Program.cs                         (editado: registra PublicUrlOptions + mapeia o novo GET)

apps/api/tests/Buteco.Api.Tests/
├── AgentCardEndpointTests.cs          (novo: testes de integração do endpoint)
└── Support/
    └── A2ATaskLifecycleFixture.cs     (reaproveitado sem mudança — mesma WebApplicationFactory)

.env.example                           (editado: nova variável PublicUrl__BaseUrl)
apps/api/src/Buteco.Api/appsettings.json          (editado: seção PublicUrl)
apps/api/src/Buteco.Api/appsettings.Development.json (editado: BaseUrl de dev)
```

Nenhum arquivo novo em `libs/` — o card é lido e montado inteiramente
dentro de `apps/api`, sem necessidade de código compartilhado com
`apps/workers` (que nem tem acesso a `Agent` diretamente, por isolamento
estrito entre apps).

## Decisions

### Decision 1: Endpoint HTTP customizado (`MapGet`), não o mecanismo nativo do SDK

Nem `MapWellKnownAgentCard` nem o `GetExtendedAgentCard` via JSON-RPC
servem: o primeiro fecha sobre uma única instância estática de
`AgentCard` no registro (incompatível com N agentes por `{id}` em
runtime); o segundo é hard-coded para lançar
`ExtendedAgentCardNotConfigured` na implementação base do SDK, e mesmo
que fosse sobrescrito via subclasse de `A2AServer`, "extended card"
pressupõe autenticação — que este projeto não tem.

A solução reaproveita o padrão já estabelecido para o JSON-RPC: um
`MapGet` customizado, irmão do `MapA2A` já existente:

```csharp
// Program.cs — depois da linha que mapeia MapA2A
app.MapA2A(app.Services.GetRequiredService<RoutingA2ARequestHandler>(), "/agents/{id}/a2a"); // existente
app.MapAgentCardEndpoint(); // novo — GET /agents/{id}/.well-known/agent-card.json
```

**Path escolhido**: `/agents/{id}/.well-known/agent-card.json`, não
`/agents/{id}/a2a/.well-known/...`. Justificativa: no próprio exemplo
"Getting Started" do SDK, `MapWellKnownAgentCard(agentCard)` é chamado
sem `path` (raiz do app), como irmão de `MapA2A(taskManager, "/echo")` —
ou seja, a convenção do SDK trata o well-known card como localizado ao
lado do path de transporte, não aninhado nele. Aplicando o mesmo
raciocínio ao nosso roteamento por agente, `/agents/{id}` é a
"localização" do agente e `/a2a` é apenas o sub-path do transporte
JSON-RPC dentro dela.

**Alternativas descartadas**:
- Subclassar `A2AServer` e sobrescrever `GetExtendedAgentCardAsync` —
  descartada porque o método é semanticamente "extended" (autenticado) no
  protocolo, e usá-lo como card primário confundiria o contrato para
  qualquer cliente A2A que siga a spec à risca.
- `MapWellKnownAgentCard` com uma instância montada por request dentro do
  próprio `path` (ex. registrar um grupo por agente em runtime) —
  descartada porque endpoints ASP.NET Core são mapeados uma vez no
  startup; não há API do framework para registrar rotas dinamicamente por
  entidade de banco.

### Decision 2: Sem cache, sem staleness — leitura fresca a cada requisição

Como não existe nenhum `AgentCard` "baked" em lugar nenhum do
`A2AServer`/`AgentA2AServerRegistry` (Decision 1), não há nada para
invalidar. O handler do novo endpoint lê o `Agent` fresco via
`AppDbContext` a cada requisição — mesmo princípio já usado em
`EnqueueingAgentHandler.GetAgentStateAsync`:

```csharp
using var scope = scopeFactory.CreateScope();
var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
var agent = await dbContext.Agents.AsNoTracking()
    .FirstOrDefaultAsync(a => a.Id == agentId, cancellationToken);
```

`AgentA2AServerRegistry` não precisa de nenhuma mudança nesta change — o
card não passa por ele. Isso é mais simples do que a alternativa
originalmente cogitada (invalidar/reconstruir entradas do registry ao
atualizar um agente), que se mostrou desnecessária uma vez confirmado que
o SDK nunca ofereceu um lugar para "bakear" o card em primeiro lugar.

Coberto por teste que prova a ausência de staleness ponta a ponta: criar
agente → `GET` card → `PUT` agente com dados diferentes → `GET` card de
novo → confirmar que os campos mudaram.

### Decision 3: `AgentCard.Provider` fica `null` — sem conceito de organização operadora

`AgentCard.Provider` (`AgentProvider? { Organization, Url }`) é conceito
do protocolo completamente diferente de `Agent.Provider` (vendor de LLM:
`openai`/`anthropic`/`gemini`). Este projeto é uma plataforma self-hosted
interna, sem um conceito formal de "organização operadora" hoje — inventar
um nome/URL fixo só para preencher o campo não teria lastro em nenhum
dado real do sistema.

`Provider` é opcional em `AgentCard` (`AgentProvider?`), então a decisão é
simplesmente omiti-lo (`null`) em vez de introduzir configuração nova sem
necessidade concreta.

No código, a montagem do card usa nomes que não colidem com
`Agent.Provider`: o método que constrói o card não declara nenhuma
variável local chamada `provider`/`Provider` fora do próprio
`Agent.Provider` já existente — o campo do protocolo é acessado só como
`AgentCard.Provider` (qualificado), nunca desambiguado por um alias local,
porque a decisão de deixá-lo `null` elimina a necessidade de qualquer
variável intermediária.

**Alternativa descartada**: popular `AgentCard.Provider` com um nome/URL
fixos de organização via nova configuração (ex.
`AgentCard:Organization:Name` + `:Url`). Descartada por enquanto — não há
requisito de produto pedindo essa informação, e adicionar configuração
especulativa contraria o princípio de não construir para necessidades
hipotéticas. Se um requisito futuro precisar disso, é uma migration/config
aditiva simples, sem quebrar o que existe.

### Decision 4: Mapeamento `Skill` → `AgentSkill`

```
AgentSkill.Id          = Slugify(Skill.Name), com dedupe determinístico
AgentSkill.Name        = Skill.Name
AgentSkill.Description = Skill.Description ?? ""
AgentSkill.Tags        = [] (sem dado correspondente hoje)
AgentSkill.Examples/InputModes/OutputModes/SecurityRequirements = null (opcionais, omitidos)
```

`Slugify`: lowercase, espaços e caracteres não alfanuméricos viram `-`,
hífens repetidos colapsados, trim de `-` nas pontas (ex. `"Consulta CEP"`
→ `"consulta-cep"`). Como `Skill.Name` não é validado como único dentro de
um agente hoje, duas skills que gerem o mesmo slug recebem sufixo
numérico determinístico na ordem em que aparecem em `Agent.Skills`
(`consulta-cep`, `consulta-cep-2`, ...) — determinístico porque
`Agent.Skills` é uma lista ordenada persistida como está, não um conjunto.

Um agente sem nenhuma `Skill` cadastrada expõe `Skills: []` no card — já
é o default de `Agent.Skills` ([Agent.cs:34](../../../apps/api/src/Buteco.Api/Agents/Entities/Agent.cs)),
sem branch de erro necessário.

**Alternativas descartadas**:
- `Id` = índice posicional (`skill-0`, `skill-1`, ...) — descartada por
  não ser estável: reordenar `Skills` num `PUT /agents/{id}` (mesmo
  conjunto, ordem diferente) mudaria os `Id`s sem nenhuma mudança de
  conteúdo, o que é pior para um cliente A2A que queira referenciar uma
  skill entre chamadas do que o slug (só muda se o `Name` mudar).
- Exigir unicidade de `Skill.Name` na validação de `Agent` para eliminar a
  necessidade de dedupe de slug — descartada por estar fora do escopo
  desta change (mudaria uma regra de validação que pertence à change
  `backend-agente-description-skills`, já aplicada e arquivada) e por não
  ser necessária: o dedupe determinístico resolve o caso sem exigir essa
  mudança.

### Decision 5: Nova configuração `PublicUrl:BaseUrl` para `SupportedInterfaces`

`Cors:AllowedOrigins` ([CorsOptions.cs](../../../apps/api/src/Buteco.Api/Options/CorsOptions.cs),
[Program.cs:35-37](../../../apps/api/src/Buteco.Api/Program.cs)) controla
quem pode chamar a API (inbound) — direção errada para o que
`AgentCard.SupportedInterfaces[].Url` precisa (a própria URL pública da
API, outbound). Não há nenhuma configuração reaproveitável hoje.

Nova classe de opções, seguindo exatamente o padrão de `CorsOptions`/
`McpCryptoOptions`:

```csharp
// Options/PublicUrlOptions.cs
public sealed class PublicUrlOptions
{
    public const string SectionName = "PublicUrl";
    public string BaseUrl { get; set; } = "";
}
```

Usada para montar:
```csharp
SupportedInterfaces = [new AgentInterface
{
    Url = $"{baseUrl.TrimEnd('/')}/agents/{agent.Id}/a2a",
    ProtocolBinding = "JSONRPC",
    ProtocolVersion = "1.0",
}]
```

Documentada em `.env.example` seguindo o bloco de comentário já usado
para `Mcp__CredentialEncryptionKey` (propósito, formato, default de dev —
`http://localhost:5017` para bater com `launchSettings.json`, citado em
`docs/a2a-integration.md`).

**Alternativa descartada**: derivar a URL em runtime a partir de
`HttpRequest` (`https://{Request.Host}{Request.PathBase}`). Descartada
porque a URL efetiva vista pelo processo ASP.NET Core atrás de um proxy
reverso/load balancer não é necessariamente a URL pública real (host
interno, porta interna) — configuração explícita é mais previsível que
inferência a partir da requisição, mesmo custando uma variável de
ambiente a mais.

### Decision 6: `Capabilities.Streaming`/`.PushNotifications` fixados em `false`

```csharp
Capabilities = new AgentCapabilities { Streaming = false, PushNotifications = false }
```

Ambos são `bool?` sem default do SDK (ficam `null`/unspecified se
omitidos) — setar explicitamente evita qualquer ambiguidade para um
cliente A2A que trate `null` como "desconhecido" em vez de "não
suportado". Reflete o estado real: nenhum dos dois está implementado
(Non-Goal desde `backend-agente-a2a-mvp`).

### Decision 7: Card exposto independente do estado do agente

`GET /agents/{id}` já responde normalmente independente de
`IsActive`/`Provider`/`Model` ([AgentEndpoints.cs](../../../apps/api/src/Buteco.Api/Agents/Endpoints/AgentEndpoints.cs) —
`GetAgentByIdAsync` não tem nenhum branch de estado). O novo endpoint de
card segue o mesmo princípio: `200 OK` com o card sempre que o `id`
corresponde a um agente existente, incluindo agente inativo
(`IsActive = false`) ou no estado "precisa de reconfiguração"
(`Provider`/`Model` nulos). Só `404` quando o `id` não corresponde a
nenhum agente.

Justificativa: descoberta de metadado (o que este agente é/faz) é
logicamente independente de garantia de execução (se uma `SendMessage`
para ele será aceita agora) — o cliente A2A pode legitimamente querer
saber que um agente existe e o que ele oferece antes de tentar invocá-lo,
mesmo que a invocação real seja rejeitada depois.

### Decision 8: Campos estáticos obrigatórios do `AgentCard`

`Version`, `DefaultInputModes`, `DefaultOutputModes` são `[JsonRequired]`
no SDK mas não têm fonte de dado em `Agent` nem foram cobertos pelas
Decisions 1-7 (todas vieram do brief original). Valores fixos:

```csharp
Version = "1.0.0",
DefaultInputModes  = ["text/plain"],
DefaultOutputModes = ["text/plain"],
```

`text/plain` reflete o comportamento real já documentado em
`docs/a2a-integration.md` ("hoje o Buteco Agents só lê partes de texto"
ao montar a chamada ao LLM — partes de arquivo/dado são aceitas pela rota
mas ignoradas). Anunciar qualquer outro modo seria uma promessa que a
API não cumpre. `Version = "1.0.0"` é uma constante do card em si (não de
runtime/dependência) — incrementada manualmente se o shape do card mudar
de forma relevante para um cliente externo; não há requisito de negócio
pedindo versionamento automático nesta fatia.

## Risks / Trade-offs

- **[Risco]** `Skill.Name` sem unicidade validada pode gerar slugs
  duplicados dentro do mesmo agente → **Mitigação**: dedupe determinístico
  por sufixo numérico (Decision 4), coberto por teste dedicado.
- **[Risco]** Configuração `PublicUrl:BaseUrl` errada ou não configurada
  em produção resulta num `AgentCard` com `SupportedInterfaces[].Url`
  inválido/vazio, que um cliente A2A tentaria usar para invocar o agente
  e falharia → **Mitigação**: nenhuma validação de formato nesta fatia
  (mesmo padrão de outras variáveis de ambiente do projeto, como
  `OpenAI__BaseUrl`, que também não são validadas em runtime); documentar
  claramente em `.env.example` com um default de dev funcional.
- **[Trade-off]** Deixar `AgentCard.Provider = null` (Decision 3) significa
  que, se um requisito futuro de produto pedir essa informação, será
  necessário adicionar configuração e revisitar esta decisão — aceito
  conscientemente por não haver necessidade concreta hoje.
- **[Trade-off]** Endpoint HTTP customizado em vez de mecanismo nativo do
  SDK (Decision 1) significa que atualizações futuras do pacote `A2A`
  não vão automaticamente melhorar/mudar o card exposto — aceito porque é
  o mesmo trade-off já aceito para o roteamento JSON-RPC customizado no
  MVP, e o SDK não oferece alternativa compatível com múltiplos agentes
  por host.

## Migration Plan

1. Nenhuma migration de banco — esta change não adiciona nem altera
   colunas; `Description`/`Skills` já existem (`backend-agente-description-skills`).
2. Nova variável de ambiente `PublicUrl__BaseUrl` precisa estar
   configurada antes do binário novo subir em cada ambiente (dev, staging,
   produção) — sem valor default seguro em produção (só em
   `appsettings.Development.json`, apontando para `http://localhost:5017`
   por padrão de dev). Ausência da variável em produção não impede o
   deploy nem quebra `SendMessage`/`GetTask` (que não dependem dela) — só
   resulta num card com URL vazia/relativa até a variável ser configurada.
3. Deploy: endpoint novo, aditivo — não há rota existente sendo
   substituída ou removida. Rollback é reverter o binário sem nenhuma
   coordenação especial com `apps/workers` (isolamento estrito entre
   apps, `apps/workers` não é afetado por esta change).

## Open Questions

Nenhuma pergunta de negócio/produto em aberto. `AgentCard.Provider = null`
(Decision 3) é uma decisão tomada nesta fatia, não uma pergunta pendente —
sua eventual reversão fica para quando (se) houver um requisito real
pedindo essa informação.
