## Context

`apps/workers` hoje resolve um único `IChatClient` OpenAI-compatible como
singleton no `Program.cs` (`ChatClientOptions` → `OpenAIClient` →
`.GetChatClient(model).AsIChatClient()`), injetado fixo em
`AgentExecutionService`, que o envolve em `new ChatClientAgent(chatClient,
agent.Instructions, agent.Name)` a cada execução (`AgentExecutionService.cs:58`).
`apps/api` não conhece `IChatClient` — só persiste o catálogo de agentes e
publica jobs no RabbitMQ.

Toda a investigação de pacotes abaixo (Decision 1) foi feita lendo a
documentação real dos SDKs em julho de 2026 (NuGet, GitHub, doc oficial da
Anthropic e do Microsoft Agent Framework), não de memória de treinamento —
os nomes de pacote e status (beta/estável) mudam com frequência nesse
ecossistema e duas armadilhas concretas apareceram durante a investigação
(ver Decision 1).

O padrão já estabelecido de "estado de agente sem publicar job" (agente
inativo, `openspec/changes/archive/2026-07-27-apps-api-agent-update-status/`)
é reaproveitado aqui para dois novos casos: agente sem `Provider`/`Model`
configurados e agente cujo `Provider` deixou de estar disponível no
ambiente. O padrão de "handler devolve um sinal neutro, o endpoint decide
o HTTP" da mesma change (`AgentResponse?` nulo → `NotFound`) também é
reaproveitado e estendido para expressar falha de validação de negócio
(ver Decision 4).

## Goals / Non-Goals

**Goals:**
- `Agent` suporta `Provider`/`Model` por agente, validados contra o que está
  disponível por configuração de ambiente no momento do cadastro/edição.
- `GET /providers` reflete exatamente os provedores configurados (nenhum
  hardcode de "todos sempre disponíveis").
- `apps/workers` resolve o `IChatClient` certo por provedor em runtime, sem
  mudar o fluxo de execução do agente (`RunAsync`, streaming/não-streaming).
- `SendMessage` para um agente sem provedor configurável usável é rejeitado
  via protocolo A2A, nunca como falha não tratada nem HTTP customizado.

**Non-Goals:**
- Nenhuma UI (fica para a próxima change).
- Nenhuma consulta dinâmica às APIs de listagem de modelo de cada
  fornecedor — catálogo de modelos é estático/curado no código.
- Nenhum override de base URL/endpoint por agente — só API key por
  provedor, via configuração (OpenAI mantém `BaseUrl` configurável, como já
  é hoje, mas isso não se estende a Anthropic/Gemini nesta fatia).
- Nenhum provedor além de OpenAI, Gemini e Anthropic.
- Nenhuma mudança em streaming/não-streaming da execução já existente.
- Nenhum cache de instância de `IChatClient` entre execuções — construir
  por chamada é aceitável nesta fatia.

## Decisions

### Decision 1 — SDK por provedor

| Provedor | Pacote | Status verificado (jul/2026) | Padrão de uso |
|---|---|---|---|
| OpenAI | `Microsoft.Extensions.AI.OpenAI` | estável, já em uso | inalterado |
| Anthropic | `Anthropic` (oficial, `anthropics/anthropic-sdk-csharp`) | **beta** — aviso explícito de SemVer na doc oficial (`platform.claude.com/docs/en/cli-sdks-libraries/sdks/csharp`): breaking changes podem ocorrer em minor/patch durante o beta | `AnthropicClient client = new(); IChatClient chatClient = client.AsIChatClient(model);` |
| Gemini | `Google.GenAI` (oficial Google) | estável, v1.15.0 confirmado no NuGet | `.AsIChatClient("model")`, mesmo padrão de Anthropic/OpenAI |

Verificar a versão exata de `Anthropic` e `Google.GenAI` no NuGet no
momento da implementação e fixá-la em `Directory.Packages.props` sem
wildcard — os números acima são o estado observado durante esta
investigação, não um valor a copiar sem checar de novo.

**Armadilha encontrada e descartada — não usar `Microsoft.Agents.AI.Anthropic`**:
existe um segundo pacote no ecossistema, `Microsoft.Agents.AI.Anthropic`
(prerelease, conector do próprio Microsoft Agent Framework — `dotnet add
package Microsoft.Agents.AI.Anthropic --prerelease`, documentado em
`learn.microsoft.com/en-us/agent-framework/agents/providers/anthropic`).
Ele **não expõe `IChatClient`**: sua única API é
`AnthropicClient.AsAIAgent(model, name, instructions)`, que devolve um
`AIAgent` já pronto. Isso é incompatível com a arquitetura atual de
`apps/workers`, que constrói o `IChatClient` separadamente e o envolve
manualmente em `ChatClientAgent` (`AgentExecutionService.cs:58`,
`Program.cs:16-26`) — adotar esse pacote exigiria reescrever
`AgentExecutionService` para parar de construir `ChatClientAgent` e usar o
`AIAgent` pronto de cada provedor, o que contradiz o Non-Goal de não mudar
o fluxo de execução existente. Descartado por incompatibilidade
arquitetural, não por preferência.

**Gemini não tem página própria na doc do Microsoft Agent Framework para
.NET** (`learn.microsoft.com/en-us/agent-framework/agents/providers/` lista
Azure OpenAI, OpenAI, Microsoft Foundry, Anthropic, Ollama, Foundry Local,
GitHub Copilot, Copilot Studio, Custom — sem Gemini, ao contrário do SDK em
Go, que tem um `geminiprovider` dedicado). Não existe portanto um pick
"endossado pela Microsoft" entre as opções de Gemini — a escolha abaixo é
por ser o SDK first-party do próprio fornecedor do modelo, mesmo critério
já aplicado a OpenAI e Anthropic.

**Alternativas de Gemini descartadas:**

| Alternativa | Por que foi descartada |
|---|---|
| `Mscc.GenerativeAI.Microsoft` (comunidade, `mscraftsman/generative-ai`, v3.1.0, ~42K downloads, mais madura por preceder o SDK oficial do Google; constrói `new GenerativeAIChatClient(apiKey, model)` diretamente) | Comunidade, não first-party — mesmo critério de preferir o SDK oficial do fornecedor já aplicado a OpenAI e Anthropic. Fica registrada como opção viável de fallback caso `Google.GenAI` se mostre instável em produção. |
| `GeminiDotnet.Extensions.AI` (terceiro, v0.25.0, implementação leve direta de `Microsoft.Extensions.AI.Abstractions`, sem depender do SDK completo do Google) | Mesmo motivo — projeto de terceiro, não first-party. |

**Alternativa de pacote único multi-provedor descartada**: `HSO.Extensions.AI.*`
(`bes3020/MSChatExtensions`, pacotes `HSO.Extensions.AI.GoogleChat`/
`AnthropicChat`/etc., cobrindo Gemini, Anthropic, Mistral, DeepSeek, Qwen,
Grok num único projeto). Descartado com evidência concreta levantada no
GitHub: 1 star, 1 fork, commit único, e o próprio README nota "some issues"
nos testes do provedor Anthropic apesar de se autodeclarar "production
ready". Sinal de manutenção insuficiente para ser dependência de produção,
comparado a três pacotes ativamente publicados e mantidos por provedor
(inclusive dois deles first-party).

### Decision 2 — `libs/ProviderCatalog`: primeiro uso de `libs/`, com escopo deliberadamente mínimo

`libs/` existe desde o início do projeto como conceito reservado
("compartilhamento real só via `libs/` explícita, pequena e versionada,
criada apenas quando houver necessidade concreta" — `README.md:5-8`), mas
nunca foi usada. Esta é a primeira vez.

**O que realmente precisa concordar entre `apps/api` e `apps/workers`**:
apenas a identidade de cada provedor (o valor de string que vira
`Agent.Provider`, aparece em `GET /providers` e é usado no `switch` do
resolver de `apps/workers`) e o nome da seção de configuração que cada
provedor usa para checar se está configurado. Isso é um contrato
cross-process real: se `apps/api` e `apps/workers` definissem esse
mapeamento de forma independente e um dos dois divergisse (ex.: renomear a
seção em um app e esquecer no outro, ou usar `"Gemini"` num lado e
`"GoogleGemini"` no outro), `apps/api` reportaria um provedor como
disponível/configurado enquanto `apps/workers` falharia silenciosamente ao
tentar ler a chave real — um bug de configuração só visível em produção,
via log de falha genérica (ver Decision 5).

**O que deliberadamente NÃO entra em `libs/`**: o catálogo de modelos por
provedor. Só `apps/api` o consome (validação de `Create`/`Update` e
`GET /providers`) — `apps/workers` nunca valida nomes de modelo, só repassa
`agent.Model` já validado para o construtor do client específico do
provedor. Colocar o catálogo de modelos em `libs/` violaria o próprio
critério que justifica a existência de `libs/ProviderCatalog`: só
compartilhar o que precisa concordar entre dois processos. Modelo por
modelo é dado consumido por um único processo.

**Conteúdo de `libs/ProviderCatalog`** (sem I/O, só dados estáticos):

```csharp
namespace Buteco.ProviderCatalog;

public sealed record LlmProviderDefinition(string Id, string ConfigSectionName);

public static class LlmProviders
{
    public const string OpenAi = "openai";
    public const string Anthropic = "anthropic";
    public const string Gemini = "gemini";

    public static readonly IReadOnlyList<LlmProviderDefinition> All =
    [
        new(OpenAi, "ChatClient"),   // nome de seção legado — ver nota de nomenclatura abaixo
        new(Anthropic, "Anthropic"),
        new(Gemini, "Gemini"),
    ];
}

public static class LlmProviderConfigurationExtensions
{
    public static bool IsConfigured(this LlmProviderDefinition provider, IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration[$"{provider.ConfigSectionName}:ApiKey"]);
}
```

`apps/api` usa `IsConfigured` (através de `ProviderCatalogService`, Decision
3/4) para montar `GET /providers` e validar `Create`/`Update` — sem
precisar saber nada sobre `AnthropicClient` ou `GenerativeAIChatClient`.
`apps/workers` usa os mesmos `LlmProviders.*` como identificadores no
`switch` do resolver (Decision 7) — elimina o risco de string mágica
divergente entre os dois apps além do nome da seção de configuração.

**Alternativa descartada**: duplicar o mapeamento em cada app + um teste
cross-app no estilo de `tests/CrossAppTaskStoreCompatibility.Tests` (que já
referencia `Buteco.Api` e `Buteco.Workers` ao mesmo tempo, de propósito, só
para verificar compatibilidade de schema sem código de produção
compartilhado — `README.md:46-50`, Decisão 8 do design.md arquivado em
`openspec/changes/archive/2026-07-26-backend-agente-a2a-mvp/`). Essa
alternativa fecharia o mesmo risco de drift sem criar `libs/`, mantendo o
isolamento de produção 100% intacto. Foi considerada tecnicamente
equivalente em segurança, mas descartada em favor de `libs/ProviderCatalog`
porque o dado compartilhado aqui (identidade de provedor + nome de seção)
é permanentemente idêntico nos dois apps por definição — não são "duas
implementações que devem concordar" (caso do `ITaskStore`, onde cada app
tem sua própria implementação de acesso a dados e um teste de
compatibilidade faz sentido), é literalmente o mesmo dado seco seco. Um
teste cross-app para esse caso seria testar que uma cópia-colada é igual à
outra, o que `libs/` já garante por construção, sem depender de alguém
lembrar de rodar/manter o teste.

### Decision 3 — Catálogo de modelos por provedor: estático, curado em `apps/api`

Lista fixa por provedor no código (`apps/api/src/Buteco.Api/Providers/ProviderModelCatalog.cs`),
sem consulta dinâmica às APIs de "listar modelos" de cada fornecedor.

**Por quê**: consultar a API de cada fornecedor a cada `GET /providers`
introduziria uma chamada de rede extra, sujeita a rate limit e a falha de
disponibilidade, só para popular uma lista de opções — e cada fornecedor
tem seu próprio formato de resposta (IDs, filtros de "modelo depreciado"),
o que tornaria `GET /providers` dependente da disponibilidade dos três
serviços externos simultaneamente.

**Alternativa descartada**: consulta dinâmica em runtime. Rejeitada pelos
motivos acima.

**Risco aceito (não é Non-Goal)**: o catálogo fica desatualizado conforme
os fornecedores lançam modelos novos ou depreciam antigos — corrigido só
por uma alteração de código futura. A lista exata de modelos por provedor
não é fixada aqui; deve ser levantada contra o estado atual de cada
fornecedor no momento da implementação (`tasks.md`), não copiada de
memória de treinamento — mesmo cuidado já aplicado às versões de pacote na
Decision 1.

`ProviderModelCatalog` (o dado estático) é consumido por `ProviderCatalogService`
(Decision 4), que é o único ponto de `apps/api` que combina esse catálogo
com a disponibilidade de ambiente (`LlmProviders`/`IsConfigured`, Decision
2) para responder "esse provider+model está disponível agora?".

### Decision 4 — Validação de disponibilidade de Provider/Model vive no handler, não no endpoint

`apps-api-cqrs-mediator` (arquivada) fixou, na sua Decision 2, que
`AgentEndpoints.cs` só faz validação de **shape** do request (campo
presente/ausente) antes de despachar o `Command` — e rejeitou
explicitamente mover essa validação para o handler, com dois motivos: (1)
nenhum outro caller do comando precisava da mesma regra na época, e (2)
fazer o handler devolver algo no formato de `TypedResults.ValidationProblem`
aproximaria o command handler de detalhes HTTP (`Microsoft.AspNetCore.Http.HttpResults`),
o acoplamento que aquele refactor buscava remover.

"`Provider` está configurado no ambiente agora" e "`Model` consta no
catálogo desse `Provider`" não são validação de shape — dependem de estado
do sistema (variáveis de ambiente, catálogo estático), a mesma categoria
de regra que aquela Decision já mantinha fora do endpoint. Ficam no
handler:

- `AgentEndpoints.CreateAgentAsync`/`UpdateAgentAsync` continuam validando
  só shape (nome, instruções, `provider`, `model` presentes) — nenhuma
  mudança nessa parte, mesmo formato inline de hoje.
- `CreateAgentCommandHandler`/`UpdateAgentCommandHandler` passam a injetar
  `ProviderCatalogService` (Decision 3) e chamar
  `Validate(provider, model)`, que devolve um enum de domínio —
  `ProviderValidationOutcome { Valid, ProviderNotConfigured, ModelUnavailable }`
  — não um tipo do ASP.NET Core. Isso preserva a mesma separação que a
  Decision 2 do `apps-api-cqrs-mediator` protegia: o handler não importa
  `Microsoft.AspNetCore.Http.HttpResults` nem sabe o que é
  `ValidationProblem`.
- `CreateAgentCommand`/`UpdateAgentCommand` passam a devolver
  `CreateAgentResult(AgentResponse? Agent, ProviderValidationOutcome Validation)`
  / `UpdateAgentResult(AgentResponse? Agent, bool Found, ProviderValidationOutcome Validation)`
  em vez de `AgentResponse`/`AgentResponse?` puro. Isso **não** é o tipo
  union genérico que a Decision 2 do `apps-api-cqrs-mediator` rejeitou —
  é a mesma extensão do mecanismo já usado ali para 404: `UpdateAgentCommand`,
  `ActivateAgentCommand` e `DeactivateAgentCommand` (`apps-api-agent-update-status`)
  já devolvem `AgentResponse?`, e o endpoint mapeia `null` para
  `TypedResults.NotFound()` — um sinal neutro que o endpoint interpreta
  como HTTP, sem o handler saber de HTTP. Esta change estende o mesmo
  mecanismo para também expressar "válido, mas rejeitado por regra de
  negócio" (400) — necessário agora porque `UpdateAgentCommand` tem duas
  causas de falha possíveis a distinguir (id inexistente → 404, ou
  provider/model indisponível → 400), então um único `null` não basta mais
  para o endpoint decidir qual HTTP responder.
- `AgentEndpoints.cs` mapeia o resultado: `Found == false` → `NotFound`;
  `ProviderValidationOutcome.ProviderNotConfigured` → `ValidationProblem`
  com erro no campo `provider`; `ModelUnavailable` → `ValidationProblem`
  com erro no campo `model`; `Valid` → `Created`/`Ok` com o `AgentResponse`.
  Só o endpoint conhece `TypedResults`/`ValidationProblem` — handler e os
  tipos de resultado (`CreateAgentResult`/`UpdateAgentResult`/
  `ProviderValidationOutcome`) continuam sem nenhuma referência a
  `Microsoft.AspNetCore.Http.HttpResults`.

**Alternativa descartada**: manter a checagem de disponibilidade dentro do
endpoint (chamando `ProviderCatalogService` diretamente em
`AgentEndpoints.cs`, como um `IsAvailable(provider, model)` booleano antes
de montar o `Command`). Rejeitada porque reintroduziria no endpoint
exatamente o tipo de regra que `apps-api-cqrs-mediator` já havia decidido
manter fora dele (dependente de estado do sistema, não de shape do
request) — o endpoint voltaria a ser responsável por saber o que torna um
agente válido, não só por rota + shape + despacho + mapeamento HTTP.

### Decision 5 — Onde rejeitar `SendMessage` quando o agente não tem um provedor utilizável

Em `EnqueueingAgentHandler.ExecuteAsync` (`apps/api/src/Buteco.Api/A2A/EnqueueingAgentHandler.cs`),
no mesmo ponto e pelo mesmo motivo já usado para `IsActive`
(`apps-api-agent-update-status`, Decision 2): `AgentA2AServerRegistry`
constrói e cacheia o `EnqueueingAgentHandler` **uma única vez por
`agentId`**, num `ConcurrentDictionary` sem invalidação
(`AgentA2AServerRegistry.cs:14-35`) — qualquer checagem feita no momento da
construção ficaria presa ao estado do primeiro `SendMessage`, nunca
reavaliada depois. A checagem precisa ser lida fresca a cada execução,
como `IsActive` já é.

`EnqueueingAgentHandler` passa a rejeitar (`updater.RejectAsync`, sem
publicar job no RabbitMQ) em dois casos novos, além de `IsActive == false`:
1. Agente com `Provider` ou `Model` nulos (estado "precisa de
   reconfiguração", ver Decision 6).
2. Agente com `Provider` preenchido, mas que não está mais configurado no
   ambiente de `apps/api`.

O caso 2 reusa `ProviderCatalogService.IsProviderConfigured(provider)`
(Decision 4) — a mesma classe já usada por `Create`/`Update` e por
`GET /providers` — em vez de `EnqueueingAgentHandler` ler
`LlmProviders`/`IsConfigured` diretamente. Evita duas implementações
independentes de "provider está configurado" dentro do mesmo app.
`ProviderCatalogService` não depende de banco (só `IConfiguration` +
catálogo estático), então é seguro injetá-lo como singleton em
`AgentA2AServerRegistry` (que hoje já recebe `IServiceScopeFactory`,
`ITaskJobPublisher` e `ILoggerFactory` via DI) e repassá-lo na construção
manual de `EnqueueingAgentHandler` em `BuildServer` — mesma forma como
`taskJobPublisher` já é repassado hoje.

**Por que só `IsProviderConfigured(provider)`, não `Validate(provider,
model)` completo — assimetria deliberada com a Decision 4**: Create/Update
validam `Provider` e `Model` juntos porque, naquele momento, é a única
chance de checar os dois contra o catálogo antes de persistir. Em
`SendMessage`, o `Model` do agente já foi validado contra o catálogo no
cadastro/edição (Decision 4) — a única coisa que pode legitimamente mudar
entre esse momento e um `SendMessage` subsequente, sem o agente ser
editado de novo, é a configuração de ambiente do provider (chave
removida); o catálogo de modelos é estático no código (Decision 3), não
muda em runtime. Rechecar `Model` a cada `SendMessage` só protegeria contra
um cenário específico: o catálogo estático sendo alterado por uma mudança
de código futura (ex.: um model removido/renomeado) enquanto agentes já
cadastrados ainda o referenciam — e esse cenário já é coberto, sem código
adicional, pela mesma camada complementar documentada abaixo nesta
Decision: o SDK do provedor rejeitaria o `Model` inválido na chamada real,
capturado pelo `catch (Exception)` já existente em
`AgentExecutionService.ExecuteAsync` — o mesmo mecanismo que já cobre
divergência de configuração entre `apps/api` e `apps/workers`. Adicionar o
recheck de `Model` aqui economizaria uma chamada de rede a menos apenas
nesse cenário raro, ao custo de manter dois caminhos de validação
(`Validate` completo vs. `IsProviderConfigured`) sem ganho de segurança
real — o caso já não é silencioso hoje.

Todos os casos usam o mesmo vocabulário nativo do protocolo A2A
(`TaskState.Rejected`) já validado na change anterior — nenhum HTTP status
novo, nenhuma distinção de mensagem de erro por caso nesta fatia (fica como
possível refinamento futuro se o produto pedir diagnóstico mais específico
para o cliente).

**Camada complementar (não uma alternativa a escolher — já existe, fica só
documentada)**: `apps/api` e `apps/workers` são processos com ambientes
potencialmente distintos (deploys separados). O check acima só reflete o
ambiente de `apps/api` — em tese, `apps/api` pode ter `Anthropic__ApiKey`
configurado (reporta o provedor como disponível, aceita o `SendMessage`,
publica o job) enquanto `apps/workers` não tem a mesma chave (erro de
deploy). Esse caso específico não é coberto pelo Reject em
`EnqueueingAgentHandler` — é coberto, sem nenhum código novo, pelo `catch
(Exception)` genérico já existente em `AgentExecutionService.ExecuteAsync`
(`AgentExecutionService.cs:75-86`), que chama `updater.FailAsync`. O
resolver de `IChatClient` em `apps/workers` (Decision 7) deve **lançar**
(não engolir) quando a configuração de um provedor estiver ausente, para
que esse catch genérico já existente trate o caso — nenhuma lógica de
rejeição nova precisa ser escrita em `apps/workers`. Registrado aqui como
duas camadas complementares: Reject preventivo em `apps/api` (caso comum,
evita publicar job à toa) + Fail já existente em `apps/workers` (rede de
segurança para divergência de ambiente entre os dois processos).

### Decision 6 — Migração dos agentes existentes: `Provider`/`Model` nascem nullable, sem backfill

Diferente de `IsActive` (que teve um default universal seguro, `true`, sem
ambiguidade), não existe um default seguro para `Provider`/`Model` — não é
possível assumir "todos os agentes existentes eram OpenAI" sem confirmação
explícita de quem os cadastrou.

`Agent.Provider` e `Agent.Model` nascem `string?` (nullable). Um agente com
qualquer um dos dois nulo está no estado implícito "precisa de
reconfiguração" — sem uma flag/coluna extra para isso: a ausência de
`Provider`/`Model` já é o sinal, checado no mesmo lugar da Decision 5.
`SendMessage` para esse agente é rejeitado via A2A (mesmo vocabulário já
usado para `IsActive` e provedor indisponível) até que o agente seja
editado via `PUT /agents/{id}` com `Provider`+`Model` válidos — o que, pela
validação de disponibilidade da Decision 4 (usando o catálogo da Decision
3), já garante que só se sai desse estado com uma combinação atualmente
disponível.

Migration EF Core: `provider text null`, `model text null` em `agents`,
sem passo de backfill manual. Projeto está em estágio inicial (migrations
de 2026-07-26 a 2026-07-28, sem dado de produção/seed) — o custo
operacional de qualquer uma das duas abordagens é baixo agora, mas o
precedente de não assumir um default incorreto vale mais do que a
simplicidade de uma constraint `NOT NULL` imediata.

**Alternativa descartada**: `Provider`/`Model` obrigatórios desde já +
passo manual de backfill na migration. Descartada porque exigiria decidir
um valor para linhas existentes sem base real para essa decisão — o exato
problema que motivou não usar um default.

### Decision 7 — Resolução de `IChatClient` em `apps/workers`: fábrica por provedor, sem cache

Novo `IChatClientResolver` substitui o `IChatClient` singleton hoje
registrado em `Program.cs:16-26`:

```csharp
public interface IChatClientResolver
{
    IChatClient Resolve(string provider, string model);
}
```

Implementação faz um `switch` sobre `provider` (comparando contra
`LlmProviders.OpenAi/Anthropic/Gemini` de `libs/ProviderCatalog`),
construindo o client específico a cada chamada — nenhuma instância é
reaproveitada entre execuções (Non-Goal explícito; simples de trocar por
cache depois, se perfilamento mostrar necessidade). Lança
`InvalidOperationException` para um `provider` desconhecido ou para
configuração ausente do provedor solicitado — capturado pelo `catch
(Exception)` já existente em `AgentExecutionService.ExecuteAsync` (Decision
5).

`AgentExecutionService` passa a receber `IChatClientResolver` em vez de
`IChatClient` fixo, e chama `resolver.Resolve(agent.Provider!, agent.Model!)`
dentro de `ExecuteAsync`, antes de montar o `ChatClientAgent` — único ponto
alterado no fluxo hoje existente; `RunAsync`, streaming/não-streaming e o
restante do método não mudam.

A interface (em vez de uma função estática) existe especificamente para
ser testável isolando "qual tipo de client é construído para qual
provedor" sem chamada de rede real — requisito explícito de teste desta
change: um teste de unidade injeta as `Options` de cada provedor e
verifica o tipo do `IChatClient` resultante (ou lança para provedor não
configurado), sem tocar em nenhuma rede.

### Decision 8 — Nomenclatura de configuração por provedor

Segue o padrão já existente (`ChatClientOptions`, `RabbitMqOptions`,
`CorsOptions`, `README.md:98`): uma classe `XxxOptions` por provedor com
`SectionName` próprio, bind via `builder.Configuration.GetSection(...)`,
env vars no formato `Section__Property` (ex.: `Anthropic__ApiKey`,
`Gemini__ApiKey`).

`ChatClientOptions` (OpenAI) mantém o nome de seção `"ChatClient"` sem
renomear — é o único provedor já em produção/uso, e renomear a seção
quebraria qualquer ambiente já configurado sem nenhum ganho funcional.
Isso é uma inconsistência deliberada (os outros dois provedores usam o
próprio nome como seção — `"Anthropic"`, `"Gemini"` — em vez de um nome
genérico como `"ChatClient"`), registrada aqui para não ser lida como
descuido: é compatibilidade retroativa, não o padrão a seguir para
provedores futuros.

Novas classes: `AnthropicOptions` (`ApiKey`) e `GeminiOptions` (`ApiKey`)
em `apps/workers/src/Buteco.Workers/Options/` — só `apps/workers` precisa
delas (constrói os SDK clients); `apps/api` só precisa saber o
`ConfigSectionName` (via `libs/ProviderCatalog`) para checar presença,
nunca lê `ApiKey` de verdade.

## Árvore de pastas proposta

```
libs/
  ProviderCatalog/
    Buteco.ProviderCatalog.csproj
    LlmProviders.cs                    # Decision 2
  ProviderCatalog.Tests/
    Buteco.ProviderCatalog.Tests.csproj
    LlmProvidersTests.cs

apps/api/src/Buteco.Api/
  Agents/
    Entities/Agent.cs                  # + Provider/Model (nullable)
    Commands/CreateAgent/
      CreateAgentCommand.cs            # + Provider/Model
      CreateAgentResult.cs             # Decision 4 — Agent? + ProviderValidationOutcome
      CreateAgentCommandHandler.cs     # + ProviderCatalogService.Validate
    Commands/UpdateAgent/
      UpdateAgentCommand.cs            # + Provider/Model
      UpdateAgentResult.cs             # Decision 4 — Agent? + Found + ProviderValidationOutcome
      UpdateAgentCommandHandler.cs     # + ProviderCatalogService.Validate
    Requests/CreateAgentRequest.cs     # + Provider/Model
    Requests/UpdateAgentRequest.cs     # + Provider/Model
    Responses/AgentResponse.cs         # + Provider/Model (nullable)
    Endpoints/AgentEndpoints.cs        # + validação de shape de Provider/Model
                                        # + mapeia CreateAgentResult/UpdateAgentResult
  Providers/                           # nova feature
    ProviderModelCatalog.cs            # Decision 3 — estático, só apps/api
    ProviderValidationOutcome.cs       # Decision 4 — enum de domínio (sem HttpResults)
    ProviderCatalogService.cs          # GetAvailableProviders() + Validate(provider, model)
                                        # + IsProviderConfigured(provider) — Decision 4/5
    Queries/ListProviders/
      ListProvidersQuery.cs
      ListProvidersQueryHandler.cs
    Endpoints/ProviderEndpoints.cs     # GET /providers
    Responses/ProviderResponse.cs
  A2A/
    EnqueueingAgentHandler.cs          # + checagem de reconfiguração/provider indisponível
    AgentA2AServerRegistry.cs          # + recebe/repassa ProviderCatalogService (Decision 5)
  Infrastructure/Migrations/
    <timestamp>_AddAgentProviderModel.cs
  Buteco.Api.csproj                    # + ProjectReference libs/ProviderCatalog
apps/api/tests/Buteco.Api.Tests/
  Providers/ListProvidersTests.cs
  Agents/CreateAgentProviderValidationTests.cs
  Agents/UpdateAgentProviderValidationTests.cs
  A2A/SendMessageProviderRejectionTests.cs

apps/workers/src/Buteco.Workers/
  Options/
    ChatClientOptions.cs               # inalterado (OpenAI)
    AnthropicOptions.cs                # novo
    GeminiOptions.cs                   # novo
  Agents/
    IChatClientResolver.cs             # Decision 7
    ChatClientResolver.cs
    AgentExecutionService.cs           # + usa IChatClientResolver
  Program.cs                           # + registra Options/resolver por provedor
  Buteco.Workers.csproj                # + ProjectReference libs/ProviderCatalog
                                        # + PackageReference Anthropic, Google.GenAI
apps/workers/tests/Buteco.Workers.Tests/
  Agents/ChatClientResolverTests.cs
```

## Risks / Trade-offs

- **[Risco] `Anthropic` (pacote oficial) está em beta** — breaking changes
  podem chegar em minor/patch conforme o aviso de SemVer da própria doc
  oficial. → Mitigação: isolar o uso do pacote atrás de `ChatClientResolver`
  (única classe que importa `Anthropic`), mesma estratégia já usada para
  isolar o SDK `A2A` (`backend-agente-a2a-mvp/design.md`); fixar versão
  exata sem wildcard; revisar changelog antes de qualquer bump.
- **[Risco] Config drift entre `apps/api` e `apps/workers`** (chave
  presente num processo, ausente no outro) não é impedido pelo Reject em
  `EnqueueingAgentHandler`, que só enxerga o ambiente de `apps/api` →
  Mitigação: `ChatClientResolver` lança quando a config está ausente,
  tratado pelo `catch (Exception)` já existente em
  `AgentExecutionService.ExecuteAsync` (Decision 5) — task termina
  `failed`, não trava nem derruba o worker.
- **[Risco] Catálogo de modelos desatualizado** conforme fornecedores
  lançam/depreciam modelos (Decision 3) → Aceito; corrigido por mudança de
  código futura quando necessário. Nenhuma mitigação automática nesta
  fatia (Non-Goal: sem consulta dinâmica).
- **[Trade-off] `libs/ProviderCatalog` é o primeiro conteúdo real de
  `libs/`** — estabelece precedente para decisões futuras de
  compartilhamento. Mitigado pelo escopo deliberadamente mínimo (Decision
  2): só o dado que é literalmente idêntico por definição nos dois apps,
  não um mecanismo geral de "código compartilhado".
- **[Trade-off] `IChatClient` construído por chamada, sem cache** (Non-Goal
  explícito) — overhead de inicialização do SDK a cada execução de task.
  Aceitável nesta fatia; revisitar só se perfilamento mostrar necessidade
  real.
- **[Trade-off] `UpdateAgentResult` carrega dois sinais de falha (`Found`
  + `ProviderValidationOutcome`) em vez de um único `null`** (Decision 4)
  — mais campos que o padrão anterior de `AgentResponse?`. Aceito: é a
  extensão mínima necessária para o endpoint continuar decidindo 404 vs.
  400 sem o handler conhecer HTTP; `CreateAgentResult` não precisa de
  `Found` (criação não tem esse caso), então só carrega
  `ProviderValidationOutcome`.

## Migration Plan

- Nova migration EF Core em `apps/api`: `provider text null`, `model text
  null` em `agents`. Sem coluna extra para "precisa de reconfiguração" — a
  ausência de `provider`/`model` já é o sinal (Decision 6).
- Sem passo de backfill manual — nenhum dado de produção existe hoje
  (projeto em estágio inicial); agentes cadastrados antes desta change
  ficam no estado "precisa de reconfiguração" até serem editados.
- Sem coordenação com `apps/frontend` — não é tocado nesta change.
- Rollback: migration reversa padrão (remove as duas colunas); nenhuma
  outra tabela depende delas.
