## Context

O conjunto de tools que chega ao LLM é montado numa única linha, em
`apps/workers/src/Buteco.Workers/Agents/AgentExecutionService.cs:208`:

```csharp
ChatOptions = new ChatOptions { Instructions = instructionsWithContext, Tools = toolSet.Tools.Concat(delegationTools).ToList() },
```

`toolSet.Tools` vem de `McpToolSetResolver`, `delegationTools` de
`AgentDelegationToolSetResolver`. Cada resolvedor conhece só o seu lado.

Quando dois nomes coincidem, quem decide é `FunctionInvokingChatClient`
(Microsoft.Extensions.AI 10.6.0), decompilado com `ilspycmd`
(convenção 6) — `FunctionInvokingChatClient.FindTool`, linhas 1405-1429 do
decompilado:

```csharp
/// <param name="toolLists">The lists of tools to search. Tools from earlier lists take precedence over tools from later lists if they have the same name.</param>
private static AIFunctionDeclaration? FindTool(string name, params ReadOnlySpan<IList<AITool>?> toolLists)
{
    ...
        foreach (AITool item in list)
        {
            AIFunctionDeclaration val = (AIFunctionDeclaration)(object)((item is AIFunctionDeclaration) ? item : null);
            if (val != null && string.Equals(item.Name, name, StringComparison.Ordinal))
            {
                return val;   // primeiro match ordinal vence
            }
        }
    ...
    return null;
}
```

Primeiro match ordinal vence, sem erro, sem log, sem dedupe — e as duas
declarações vão no payload para o provedor com o mesmo nome. Como o `Concat`
põe MCP antes, a precedência efetiva hoje é **MCP > delegação**, por acidente da
ordem de dois operandos.

Estado atual dos dois espaços de nome:

| origem | construção | dedupe |
|---|---|---|
| MCP | `Sanitize($"{server.Name}__{tool.Name}")` (`McpToolSetResolver.cs:115`) | **nenhum** |
| delegação | `Sanitize($"delegate_to_{Slugify(target.Name)}")` (`AgentDelegationToolSetResolver.cs:48`) | sufixo numérico, **local ao resolvedor** (`usedToolNames`, linha 44) |

`ToolNameSanitizer` (`Mcp/ToolNameSanitizer.cs`) é compartilhado pelos dois,
mapeia todo caractere fora de `[a-zA-Z0-9_-]` para `_`, prefixa `_` quando a
inicial não é letra ASCII nem `_` (escolha do repo, não exigência de provedor —
ver V4),
e **trunca em 64**. Não tem arquivo de teste próprio — `find` por
`*ToolNameSanitizer*` em `apps/workers/tests` volta vazio, embora ele seja o
sítio da truncagem e o ponto compartilhado dos dois resolvedores.

---

## Verificações feitas antes das decisões

Cada uma foi executada contra código real ou decompilação (convenção 6). Os
resultados vêm antes das decisões porque duas delas poderiam derrubar a
abordagem registrada.

### V1 — A renomeação quebra a invocação? **Não.** (a abordagem sobrevive)

Era a verificação capaz de derrubar a change: se o nome exposto fosse chave de
resolução de volta, renomear seria impossível e a saída teria de virar descarte.
Não é o caso, nos dois lados.

**Lado MCP.** `McpClientTool` (ModelContextProtocol.Core 2.0.0, decompilado):

```csharp
public override string Name => _name;                                   // rótulo exposto ao LLM
public Tool ProtocolTool { get; }                                       // a tool remota, intocada

public McpClientTool WithName(string name) =>
    new McpClientTool(_client, ProtocolTool, JsonSerializerOptions, name, _description, _progress, _meta);

// caminho de invocação:
return _client.CallToolAsync(ProtocolTool.Name, arguments, progress, options, cancellationToken);
```

`WithName` copia a instância trocando **só** `_name`; `ProtocolTool` passa
adiante inalterado, e a chamada remota usa `ProtocolTool.Name`. O XML doc do
próprio SDK afirma as duas coisas que precisávamos:

> *"When invoking `InvokeAsync`, the MCP server will still be called with the
> original tool name, so no mapping is required on the server side. This new
> name only affects the value returned from this instance's `Name`."*

e lista, entre os usos previstos de `WithName`:

> *"Preventing name collisions when using tools from multiple sources."*

Ou seja: renomear não é só seguro, é o mecanismo que o SDK oferece exatamente
para este problema. E `WithName` é aplicável mais de uma vez — o resolvedor já
chamou uma, o deduplicador chama outra sobre o resultado, e `ProtocolTool`
sobrevive às duas.

**Lado delegação.** `AgentDelegationToolSetResolver.BuildDelegationTool`
(linhas 63-75) monta a função com `AIFunctionFactory.Create`, e o corpo é uma
função local que **captura** `targetAgentId`, `sourceAgentId`, `contextId`,
`currentDepth` e `messageInstant` por closure. `name:` é só o nome declarado —
`DelegateToTargetAsync` não recebe nem consulta o nome em momento algum. Trocar
o nome não muda para onde a chamada vai.

**Nenhum mapa nome → destino existe para dessincronizar.** `McpToolSet`
(`Mcp/McpToolSet.cs`) guarda exatamente dois campos: `IReadOnlyList<AITool>
Tools` e a lista de conexões para o dispose. Não há dicionário por nome.
`McpToolSetResolver` não guarda nada além disso.

**Nem há consumidor do formato do nome.** As únicas ocorrências de `"__"` em
todo o monorepo são a declaração da constante e o seu uso, ambas em
`McpToolSetResolver.cs` (linhas 19 e 115). Ninguém faz split, parse ou match
sobre o nome exposto.

**Como o roteamento de fato acontece**, para fechar: `FindTool` casa o nome
recebido do provedor contra os nomes das instâncias **presentes na lista** e
invoca a instância encontrada. Se a lista contém a instância renomeada, o nome
renomeado é o que casa e a instância certa é a invocada. Consistente por
construção.

> **Conclusão:** renomear é a saída correta. A decisão registrada não é
> derrubada; ganha fundamento verificado em vez de suposição.

### V2 — Ordem das operações e o sufixo dentro dos 64

`Sanitize` trunca **por último** (`ToolNameSanitizer.cs:31-33`), então a saída
dos dois resolvedores já vem sanitizada e truncada. Um dedupe global aplicado
depois disso está na ordem certa: **sanitizar → truncar → deduplicar**. Um
dedupe aplicado antes da truncagem produziria nomes que voltam a colidir depois
dela — é por isso que a ordem é fixada como requisito, não deixada implícita.

**Achado: o sufixo de dedupe atual estoura o limite.**
`AgentDelegationToolSetResolver.cs:53` faz

```csharp
toolName = $"{baseName}-{suffix}";
```

sem re-truncar. Quando `baseName` já ocupa os 64 caracteres do limite, o
resultado tem 66. É defeito real, presente hoje, alcançável com dois Targets de
`Agent.Name` colidente e longo — o `delegate_to_` consome 12 dos 64, então basta
um slug de ≥52 caracteres, o que um nome de agente descritivo alcança sem
esforço. O deduplicador novo corrige encurtando a base para caber o sufixo, em
vez de concatenar às cegas.

### V3 — Determinismo: **uma das duas consultas não é ordenada**

- `AgentDelegationToolSetResolver.ResolveAsync` (linha 39): `orderby
  delegation.TargetAgentId` — **estável**.
- `McpToolSetResolver.ResolveAsync` (linhas 37-42): **nenhum `orderby`**. A
  ordem é a que o Postgres devolver, e não há garantia de que ela se repita
  entre execuções (plano diferente, `seq scan` vs. `index scan`, ordem física
  alterada por um `UPDATE`).

Sem ordem estável no lado MCP, o desempate do dedupe muda de execução para
execução e o LLM vê nomes diferentes a cada chamada — exatamente o que o
requisito de estabilidade proíbe. Ordenar a consulta de MCP faz parte desta
change, não é melhoria adjacente.

A ordem **dentro** de um servidor vem de `client.ListToolsAsync` e é definida
pelo servidor remoto. Fora do nosso controle e fora do escopo: duas tools do
mesmo servidor MCP não colidem entre si (nomes de tool são únicos por servidor
no protocolo), então essa ordem não participa de nenhum desempate.

### V4 — A truncagem em 64 é limite de quê? **Correto, e agora com fonte primária**

O comentário de `ToolNameSanitizer` atribui o número ao OpenAI, chamando-o de
"o mais restritivo dos três provedores" — afirmação que não sobreviveu à
verificação, ver o fim desta seção. A primeira redação deste design citou um blog
pessoal e uma página de catálogo de erros de terceiro — não são fonte
verificada, e a convenção 6 é exatamente sobre isso ("vale também para nomes de
header, campos de payload e rotas de sistemas externos citados dentro de spec:
requisito errado sobrevive ao archive"). Refeito contra fonte primária:

**Qual superfície do OpenAI importa.** `ChatClientResolver.BuildOpenAi` faz
`client.GetChatClient(model).AsIChatClient()` — **Chat Completions**, não a
Responses API. A distinção decide o número, e a especificação OpenAPI publicada
pelo próprio OpenAI (`openai/openai-openapi`, `openapi.yaml`) traz as duas:

| schema | superfície | limite |
|---|---|---|
| `FunctionObject.name` | Chat Completions — **a que esta stack usa** | *"Must be a-z, A-Z, 0-9, or contain underscores and dashes, with a maximum length of 64."* |
| `FunctionToolParam.name` | Responses API | `maxLength: 128`, `pattern: ^[a-zA-Z0-9_-]+$` |

Então **64 é o número certo para esta stack**, e o conjunto de caracteres do
sanitizador (`[a-zA-Z0-9_-]`) é exatamente o que o schema exige. O comentário
estava certo; o que faltava era a fonte.

**Achado adjacente:** o 64 é propriedade do Chat Completions, não do OpenAI em
geral. Se algum dia `BuildOpenAi` migrar para a Responses API, o limite sobe
para 128 e o sanitizador fica conservador — seguro, mas apertado sem motivo. O
comentário da constante registra isso, para que a migração não herde um limite
sem saber de onde ele veio.

**Gemini** — documento de descoberta oficial da API
(`generativelanguage.googleapis.com/$discovery/rest?version=v1beta`, schema
`FunctionDeclaration.name`): *"Must be a-z, A-Z, 0-9, or contain underscores,
colons, dots, and dashes, with a maximum length of 128."* Superconjunto do
nosso conjunto de caracteres, limite maior. Nota: a fonte primária **não**
declara a regra de caractere inicial que o comentário de `McpToolSetResolver.cs`
afirma (`^[a-zA-Z_]...`). O 128 confere; a âncora inicial é embelezamento não
verificado, e o comentário deve parar de afirmá-la.

**Anthropic — não verificável na fonte primária, e isso fica registrado como
tal.** A página de tool use de `platform.claude.com` não declara limite nem
padrão para `name`, e o tipo `ToolParam` do SDK Python publicado
(`src/anthropic/types/tool_param.py`) documenta `name` apenas como *"Name of the
tool. This is how the tool will be called by the model and in `tool_use`
blocks."* — sem comprimento, sem padrão. O "128 do Anthropic" da primeira
redação deste design **não tem fonte primária e é retirado**.

**O que sobra do argumento do mínimo.** Ele fica verificado em dois dos três
provedores: 64 (OpenAI Chat Completions, primária) contra 128 (Gemini,
primária). 64 é o piso entre esses dois. Se o limite do Anthropic fosse **menor**
que 64, o sanitizador estaria permissivo demais para ele — nada sugere isso, mas
nada verificado o exclui. Registrado como **R8**, com contraparte, em vez de
citar terceiro como autoridade.

**A regra de caractere inicial é escolha do repositório, e passa a ser declarada
como tal.** `ToolNameSanitizer` prefixa `_` quando o nome não começa por letra
ASCII ou `_` (linhas 26-29). **Nenhuma** fonte primária consultada exige isso: o
schema do OpenAI aceita `[a-zA-Z0-9_-]` em qualquer posição, dígito inicial
incluído, e o documento de descoberta do Gemini não declara regra de posição.
Mantém-se porque removê-la mudaria o nome de tools hoje em uso, sem ganho
verificado — e é isso que a spec agora diz, em requisito próprio, em vez de
atribuí-la a um provedor.

O que continua sendo achado: o número vivia só num comentário, sem fonte e sem
nenhum teste que o prendesse. Esta change traz o primeiro (Decisão 6).

### V5 — Os dois espaços de nome se separam mesmo por acidente?

Confirmado, e o acidente é mais frágil do que parece.

`Slugify` faz `Regex.Replace(..., "[^a-z0-9]+", "-")`, então o slug é
`[a-z0-9-]` e **nunca contém `_`**. O nome de delegação é
`delegate_to_` + slug: os únicos `_` estão no prefixo, separados por letras.
Nunca contém `__`. O nome MCP é `{servidor}__{tool}` e **sempre** contém `__`.
Enquanto a truncagem não corta antes do separador, os dois conjuntos não se
tocam — por essa propriedade estrutural, não por desenho.

Mas a colisão realmente alcançável hoje **não é** entre os conjuntos: é
**dentro do conjunto MCP**, que não tem dedupe nenhum. `Sanitize` mapeia todo
caractere fora de `[a-zA-Z0-9_-]` para `_`, então dois `McpServer.Name` que
diferem apenas em pontuação ou espaço colidem trivialmente:

```
"Zendesk MCP" → "Zendesk_MCP__search"
"Zendesk.MCP" → "Zendesk_MCP__search"     ← mesma cadeia, uma sombreia a outra
```

Isso é um operador cadastrando dois servidores com nomes parecidos, não um caso
construído. E o segundo caminho, também intra-MCP: dois nomes de servidor com
≥64 caracteres compartilhando o prefixo de 64 truncam para a mesma cadeia.

**Isso reforça a decisão registrada em vez de enfraquecê-la.** Um prefixo
dedicado por conjunto (`kb__`) manteria o acidente estrutural funcionando entre
os conjuntos e **não corrigiria nenhum dos dois caminhos intra-MCP**, que são
os alcançáveis. O ponto de concatenação corrige os quatro caminhos com um
mecanismo só.

**Achado de spec (convenção 15).** `mcp-tool-execution` já tem o requisito
*"Distinção de tools com nomes iguais entre servidores diferentes"*, que promete
"sem que uma sobrescreva ou oculte a outra". O cenário que o guarda hoje é
`McpToolSetResolverTests` com servidores `"Servidor A"` e `"Servidor B"` — nomes
**diferentes**. O requisito já está violado pelo caso `"Zendesk MCP"` /
`"Zendesk.MCP"` e o guarda passa verde. Quarto caso desta base em que um guarda
verde convive com o defeito que deveria pegar.


### V6 — Como renomear uma tool de delegação já construída? **`DelegatingAIFunction`** (a Decisão 1 sobrevive)

A redação anterior da tarefa 2.4 dizia "reconstrução do `AIFunction`" no lado
delegação. **A objeção contra essa palavra está certa e a tarefa estava errada:**
reconstruir é impossível. `AIFunctionFactory.Create` captura o delegate num
closure com `targetAgentId`, e `AIFunction` não expõe o delegate que o produziu
— não há nada a partir de que reconstruir.

Mas a conclusão de que a Decisão 1 precisaria virar (a) ou (b) **não segue**,
porque existe o wrapper genérico. `Microsoft.Extensions.AI.Abstractions`
publica `DelegatingAIFunction`, decompilado na íntegra:

```csharp
public class DelegatingAIFunction : AIFunction
{
    protected AIFunction InnerFunction { get; }

    public override string Name => InnerFunction.Name;
    public override string Description => InnerFunction.Description;
    public override JsonElement JsonSchema => InnerFunction.JsonSchema;
    public override JsonElement? ReturnJsonSchema => InnerFunction.ReturnJsonSchema;
    public override JsonSerializerOptions JsonSerializerOptions => InnerFunction.JsonSerializerOptions;
    public override MethodInfo? UnderlyingMethod => InnerFunction.UnderlyingMethod;
    public override IReadOnlyDictionary<string, object?> AdditionalProperties => InnerFunction.AdditionalProperties;

    protected DelegatingAIFunction(AIFunction innerFunction) { ... }

    protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        => InnerFunction.InvokeAsync(arguments, cancellationToken);

    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceKey != null || !serviceType.IsInstanceOfType(this))
            return InnerFunction.GetService(serviceType, serviceKey);
        return this;
    }
}
```

Responde exatamente às duas perguntas levantadas:

- **Preserva o schema?** Sim — `JsonSchema`, `ReturnJsonSchema`, `Description`,
  `JsonSerializerOptions`, `UnderlyingMethod` e `AdditionalProperties` são todos
  encaminhados ao inner. Nada se perde; só `Name` é sobrescrito pela subclasse.
- **Preserva a invocação?** Sim, e sem precisar do delegate:
  `InvokeCoreAsync` chama `InnerFunction.InvokeAsync`. O closure com
  `targetAgentId` continua intacto **dentro** do inner. Não há reconstrução —
  há encapsulamento.

Dois detalhes verificados que importam:

1. `AIFunction : AIFunctionDeclaration` (confirmado na decompilação), então a
   subclasse passa o teste `item is AIFunctionDeclaration` de `FindTool`.
2. `GetService` encaminha ao inner quando o tipo pedido não é o wrapper — então
   `FunctionInvokingChatClient.GetService<ApprovalRequiredAIFunction>()` (usado
   no caminho de aprovação, linha 2251 do decompilado) continua enxergando o
   que enxergava. Um wrapper que não encaminhasse `GetService` quebraria isso
   em silêncio.

**Disponibilidade na versão resolvida.** `apps/workers` resolve
`Microsoft.Extensions.AI.Abstractions/10.8.3` (lido de `project.assets.json`,
não do `Directory.Packages.props`, que fixa só `Microsoft.Extensions.AI.OpenAI`).
`DelegatingAIFunction` existe em **todas** as versões presentes nesta máquina —
10.4.1, 10.5.1, 10.6.0, 10.7.0, 10.8.1 e 10.8.3 — então não é aposta em API
recente.

O construtor é `protected`: o uso é uma subclasse mínima nossa
(`RenamedAIFunction : DelegatingAIFunction` com `public override string Name`),
não instanciação direta.

> **Conclusão:** a Decisão 1 permanece. O dedupe continua no ponto de
> concatenação, sobre `IList<AITool>` pronta, sem precisar do delegate nem de
> ordem imposta entre resolvedores. As saídas (a) mapa de nomes e (b) reserva
> prévia ficam **recusadas**: as duas reintroduziriam acoplamento de ordem
> entre chamadas hoje independentes para resolver um problema que o wrapper já
> resolve. A tarefa 2.4 é que estava errada, e foi corrigida.

### V7 — O histórico gravado carrega o nome antigo? **Sim, e é deliberado**

Verificado no framework, decompilando
`Microsoft.Agents.AI.PerServiceCallChatHistoryPersistingChatClient` (1.15.0). O
XML doc interno é explícito sobre persistir conteúdo de chamada de função e
sobre o cuidado de mantê-lo pareado:

> *"This ensures any in-flight `FunctionResultContent` paired with
> previously-persisted `FunctionCallContent` is not orphaned in the persisted
> chat history."*

Ou seja: `FunctionCallContent`/`FunctionResultContent` **entram** no histórico
persistido, com o nome da tool como estava na execução que os gerou. Em
`AgentExecutionService`, esse histórico é serializado por
`SerializeSessionAsync` (linha 238), guardado em `Metadata` sob
`conversationSession`, e restaurado por `DeserializeSessionAsync` na execução
seguinte (linha 315). **A premissa da objeção está certa: depois desta change,
um agente que sofria colisão monta a requisição seguinte com histórico
referenciando o nome antigo e uma lista de tools com o nome novo.**

**O que eu não posso observar aqui, e não vou inventar.** A pergunta seguinte —
se algum provedor rejeita histórico que referencia função ausente da lista atual
— exige uma chamada real. Não há chave de API de provedor nesta sessão (o mesmo
bloqueio de `0b`), então **não observei o comportamento e não o registro**. A
convenção 6 vale nas duas direções: não afirmar o comportamento do provedor a
partir de memória é o mesmo princípio que mandou decompilar o SDK.

**O que dá para afirmar já, e limita o dano.** A exposição é temporalmente
limitada por configuração que existe hoje: `SummarizationTurnThreshold = 10`
(`AgentExecutionService.cs:49`) dispara `CompactionProvider` com
`SummarizationCompactionStrategy`, que substitui os turnos antigos por um resumo
em texto — e resumo em texto não carrega `FunctionCallContent`. O nome antigo,
portanto, sai da janela dentro de ~10 turnos daquele `contextId`, sem
intervenção. Não é mitigação suficiente sozinha (o dano, se existir, é imediato
na próxima execução), mas delimita a duração.

Registrado como **R7**, com a observação do provedor como tarefa própria
bloqueada por chave, e a decisão de fallback já escrita — não como risco vago.


### V8 — Existe algum agente colidente hoje? **Nenhum, no banco de dev**

R7 e R1 só alcançam agente que **hoje** sofre colisão. Isso é consultável sem
chave de provedor nenhuma, então foi consultado antes de decidir o que fazer com
R7. Consulta rodada contra o Postgres de desenvolvimento (`podman`,
`buteco-agents_postgres_1`), reproduzindo em SQL a composição dos dois
resolvedores — `Sanitize(server.Name || '__' || tool)` truncado em 64 para o
lado MCP, `'delegate_to_' || Slugify(target.Name)` para o lado delegação — e
agrupando por `(agent_id, tool_name)` com `having count(*) > 1`:

```sql
WITH mcp AS (
  SELECT b."AgentId" AS agent_id,
         left(regexp_replace(s."Name" || '__' || t.tool, '[^a-zA-Z0-9_-]', '_', 'g'), 64) AS tool_name,
         'mcp:' || s."Name" || '/' || t.tool AS origin
  FROM agent_mcp_servers b
  JOIN mcp_servers s ON s."Id" = b."McpServerId" AND s."IsActive"
  CROSS JOIN LATERAL jsonb_array_elements_text(b.allowed_tools) AS t(tool)
),
deleg AS (
  SELECT d."SourceAgentId",
         left('delegate_to_' || trim(both '-' from regexp_replace(lower(a."Name"), '[^a-z0-9]+', '-', 'g')), 64),
         'delegation:' || a."Name"
  FROM agent_delegations d JOIN agents a ON a."Id" = d."TargetAgentId"
)
SELECT agent_id, tool_name, count(*) AS n, string_agg(origin, ' | ') AS origens
FROM (SELECT * FROM mcp UNION ALL SELECT * FROM deleg) x
GROUP BY agent_id, tool_name HAVING count(*) > 1;
```

```
===== COLISÕES =====
 agent_id | tool_name | n | origens
----------+-----------+---+---------
(0 rows)
```

A consulta é aproximação deliberada num ponto: não remove diacríticos como
`DelegationToolNameSlugifier` faz via `FormD`. Isso a torna **mais** propensa a
reportar colisão que o código real, nunca menos — um falso positivo é aceitável
num censo cuja saída de risco é "parar e olhar", um falso negativo não seria.

Inventário completo por trás desse zero: 5 agentes, 2 `McpServer`, 3 vínculos,
2 delegações → **12 nomes de tool, todos distintos**, o mais longo com 41
caracteres (`delegate_to_especialista-t-cnico-ambiente`), portanto ninguém perto
do limite de 64 e ninguém perto do estouro de sufixo de V2.

**Alcance desta evidência.** É o banco de dev, e **dev é o único ambiente que
existe** — o projeto não tem produção. Então isto não é uma amostra parcial à
espera de confirmação: é o censo completo de todos os agentes reais do sistema, e
é ele que decide que **R7 não tem alvo hoje**. Quando houver produção pela
primeira vez, a mesma consulta entra no checklist de primeiro deploy
(`02-HISTORICO_E_STATUS.md`), ao lado da aplicação da migration
`AddUniqueOpenSessionIndex`, que espera pelo mesmo motivo.

**Achado colateral, e ele reforça V5.** O inventário contém
`Informa__es_Gerais__get_menu_info`: o servidor real chamado *"Informações
Gerais"* sanitiza para `Informa__es_Gerais`, porque `ç` e `õ` viram um `_` cada.
O nome **já contém um `__` que não é o separador**. Ou seja, a propriedade
estrutural que V5 descreveu — "nome MCP sempre contém `__`, nome de delegação
nunca" — vale para detectar, mas o `__` não identifica *onde* o separador está
num nome real desta base. É evidência concreta, tirada de dado de produção-shape
e não de exemplo construído, de que apoiar qualquer coisa no separador é
apoiar-se em acidente — exatamente o argumento contra a alternativa do prefixo
por conjunto na Decisão 1.

---

## Goals / Non-Goals

**Goals:**
- Nenhum par de nomes iguais no conjunto final entregue ao LLM, vindos de
  qualquer combinação das duas origens.
- Toda colisão resolvida por renomeação, com as duas tools sobrevivendo e
  chamáveis.
- Todo nome ≤64 caracteres **depois** do sufixo de dedupe.
- Conjunto de nomes idêntico entre execuções do mesmo cadastro.
- Toda renomeação visível no log, com agente, os dois nomes e as duas origens.
- Primeiro arquivo de teste de `ToolNameSanitizer`.

**Non-Goals:**
- Validar `McpServer.Name` ou `Agent.Name` em `apps/api`. Os dois são texto
  livre por decisão registrada, e a correção pertence a quem expõe o nome ao
  LLM (convenção 12), não a quem o cadastra. Se depois se decidir restringir o
  cadastro, é change própria.
- Mudar o esquema `{servidor}__{tool}` ou `delegate_to_{slug}`. Continuam como
  estão; ganham uma garantia por cima.
- Superfície de UI para colisões. O log é o canal desta change; expor no painel
  é etapa de UI, sequenciada depois (convenção 1).
- Estender o mecanismo para tools de bases de conhecimento. Nada de
  conhecimento existe hoje; quando existir, entra no mesmo ponto sem mudança de
  desenho.

---

## Decisions

### Decisão 1 — Dedupe global no ponto de concatenação

Um `ToolNameDeduplicator` novo é aplicado sobre a união, em
`AgentExecutionService`, no lugar do `Concat` cru:

```csharp
Tools = toolNameDeduplicator.Deduplicate(agent.Id, toolSet.Tools, delegationTools)
```

**Por quê:** é o único ponto do sistema que sabe que os dois conjuntos dividem
namespace. Cada resolvedor, por construção, não sabe — e é essa ignorância que
produziu o defeito.

**Alternativa recusada — prefixo dedicado por conjunto (`kb__`, `mcp__`).**
Mantém funcionando o acidente estrutural descrito em V5, sem convertê-lo em
desenho, e não cobre nenhum dos dois caminhos intra-MCP, que são os
alcançáveis hoje. Trocaria um acidente por outro acidente mais visível.

**Alternativa recusada — dedupe em cada resolvedor com um `HashSet`
compartilhado passado adiante.** Funciona, mas espalha por três arquivos uma
invariante que pertence a um, e cria acoplamento de ordem entre chamadas que
hoje são independentes: `AgentExecutionService` passaria a ter de invocar os
resolvedores numa ordem específica para o dedupe fechar. O ponto de
concatenação já tem os dois resultados prontos.

**Convenção 2 (sem abstração prematura) não é violada:** não é código
compartilhado extraído na expectativa de reuso — é uma unidade nova para uma
responsabilidade que hoje não tem dono, com um consumidor real e imediato.

**Como o deduplicador renomeia o que não construiu** (V6): recebe `IList<AITool>`
pronta e nunca precisa do delegate original. Lado MCP, `McpClientTool.WithName`
(V1). Lado delegação, uma subclasse mínima de
`Microsoft.Extensions.AI.DelegatingAIFunction`, que encaminha `InvokeAsync`,
`JsonSchema`, `Description`, `AdditionalProperties` e `GetService` ao inner e
sobrescreve só `Name`. Reconstruir o `AIFunction` seria impossível — o closure
com `targetAgentId` não é recuperável —, e é por isso que a redação anterior da
tarefa 2.4 estava errada; encapsular não tem esse problema.

### Decisão 2 — Renomear, nunca descartar

Fundamentada em **V1**: o nome é rótulo, não chave. Renomear preserva as duas
tools chamáveis e roteando corretamente; descartar removeria capacidade do
agente por causa de uma coincidência de nome.

**Alternativa recusada — descartar a perdedora com log.** Só seria a saída se
V1 tivesse mostrado que o nome é chave de resolução de volta. Mostrou o
contrário, e o próprio SDK documenta `WithName` como o mecanismo previsto para
colisões entre fontes. Descartar seria perda de função sem necessidade técnica.

**Alternativa recusada — falhar a execução.** Contraria a convenção 4: uma
coincidência de nome de cadastro não deve derrubar a task do operador.

### Decisão 3 — Sufixo numérico, o idioma que já existe, agora global

Reusa exatamente a forma de `AgentDelegationToolSetResolver.cs:50-55` —
`-2`, `-3`, … a partir de 2, primeiro livre vence — em vez de inventar outro
(hash, GUID, contador global). Um operador que já viu `delegate_to_atendimento-2`
vê a mesma forma quando a colisão for com uma tool MCP.

O dedupe local de `AgentDelegationToolSetResolver` **sai**: com o global no
lugar, manter os dois seria dois mecanismos para a mesma invariante, e o local
é o que tem o defeito de estouro de 64 (V2). O comportamento hoje coberto pelo
requisito de delegação continua garantido, agora pelo ponto global — e o
cenário existente na spec de delegação segue passando, o que é a verificação
de que a mudança de dono não perdeu comportamento.

**Alternativa recusada — sufixo de hash do identificador de origem**
(`__a1b2c3`). Estável e sem loop, mas ilegível para o operador e mais caro no
orçamento de 64 caracteres, resolvendo um problema (colisão do próprio sufixo)
que o loop já resolve.

### Decisão 4 — Ordem de operações fixada: sanitizar → truncar → deduplicar, com o sufixo cabendo dentro dos 64

O deduplicador recebe nomes já sanitizados e truncados (V2) e, ao precisar
sufixar, **encurta a base** para o total caber em `MaxToolNameLength`:

```
base = "aaaa…aaa" (64 chars), sufixo "-2" (2 chars)
→ base.Substring(0, 62) + "-2"     // 64, nunca 66
```

O encurtamento pode, em tese, fazer o resultado colidir com um nome já usado;
o loop continua incrementando o sufixo até achar um livre, reaplicando o
encurtamento a cada tentativa. Termina porque cada tentativa consome um valor
novo de sufixo e o conjunto de nomes já usados é finito.

**Alternativa recusada — truncar depois de sufixar.** Cortaria o próprio
sufixo, que é a única coisa que distingue os dois nomes — reintroduzindo a
colisão que o dedupe acabou de resolver.

### Decisão 5 — Precedência declarada: MCP mantém o nome, delegação é renomeada

Mantém a precedência efetiva de hoje (MCP > delegação, por ordem do `Concat`),
mas agora como propriedade declarada do deduplicador e coberta por cenário de
spec, não como consequência da ordem de dois operandos que qualquer
refatoração inverteria sem aviso.

**Por que MCP e não delegação:** uma tool MCP é uma capacidade externa que o
operador cadastrou nominalmente, e o nome `{servidor}__{tool}` carrega
informação que ele reconhece. Um nome de delegação é derivado de `Agent.Name` e
já tem, por desenho, um mecanismo de sufixo que o operador conhece — é a ponta
que já sabe aparecer sufixada. Além disso, preservar a precedência atual mantém
os nomes estáveis para todo agente que hoje não sofre colisão.

**Alternativa recusada — precedência por origem mais "específica" ou por
timestamp de cadastro.** Nenhuma das duas é legível para o operador e as duas
adicionam uma consulta ou um campo só para desempatar.

### Decisão 6 — `ToolNameSanitizerTests` novo, e a truncagem vira contrato testado

O sanitizador é o sítio da truncagem, é compartilhado pelos dois resolvedores,
e hoje não tem teste (V4). O arquivo novo prende as três garantias que ele
existe para dar: conjunto de caracteres, caractere inicial (declarado como
escolha do repo) e limite de 64 — em par
"com item"/"sem item" (convenção 5): nome que precisa de troca e nome que não;
nome que precisa de truncagem e nome exatamente no limite.

Este é também o lugar onde o número 64 passa a ter fonte: o teste referencia
`ToolNameSanitizer.MaxToolNameLength`, e o comentário da constante ganha a
citação do padrão do provedor verificada em V4, em vez de afirmá-lo sem fonte.

### Decisão 7 — Aviso em `LogWarning`, no ponto de concatenação

Nível **warning**, não information nem error: não é operação normal (o operador
tem dois cadastros que colidem e provavelmente não sabe), e não é falha (a
execução segue correta e completa, convenção 4).

O aviso identifica agente, nome pretendido, nome final e a origem de cada lado:

```
"Colisão de nome de tool no agente {AgentId}: '{IntendedName}' já usado por uma tool de {WinnerSource}; a tool de {LoserSource} foi exposta como '{FinalName}'."
```

Sem isso, o sintoma no campo — "cadastrei a tool e o agente chama outra" — não
tem nenhum rastro que o explique, que é a forma exata do defeito de origem.

### Decisão 8 — `orderby` explícito na consulta de `AgentMcpServer`

Segue V3, e adota o idioma que a consulta de delegação já usa (ordenar por
identificador estável). Ordenar por `McpServerId` — e não por `server.Name` —
porque o nome é editável: renomear um servidor não deve reordenar o conjunto e
mudar quem ganha o desempate.

### Decisão 9 — Comparação de nomes é `StringComparison.Ordinal`, explicitamente

O deduplicador compara nomes com `StringComparison.Ordinal`, e o dicionário de
nomes usados é construído com `StringComparer.Ordinal` passado explicitamente no
construtor.

> **Correção durante a implementação (convenção 9).** A primeira redação desta
> decisão dizia "nunca com o default implícito", tratando o default como um
> segundo perigo a par do `OrdinalIgnoreCase`. **Isso está errado.**
> `EqualityComparer<string>.Default` já é ordinal e sensível a caixa, então
> **a comparação já seria correta com o default** — verificado: reintroduzir
> `new Dictionary<string, ToolOrigin>()` sem comparador deixa os 8 testes verdes.
>
> Ou seja: **não havia defeito latente aqui, e esta decisão não corrige um.** O
> `StringComparer.Ordinal` explícito é **documentação de intenção** — ele amarra a
> escolha a `FindTool`, para que trocá-la por um comparador insensível apareça
> como mudança de contrato e não como ajuste de estilo. O perigo real é só um
> (alguém escrever `OrdinalIgnoreCase`), e é esse que o guarda de caixa pega:
> reintroduzi-lo reprova. Deixar a redação anterior de pé daria a entender que a
> change fechou um defeito que não existia, o que é a mesma cobertura aparente
> que a convenção 15 combate — só do lado da narrativa em vez do teste.

**A fonte da escolha é `FindTool`, não preferência.** Ele casa com
`string.Equals(item.Name, name, StringComparison.Ordinal)` (linha 1422 do
decompilado), que é sensível a caixa. Então `Search` e `search` **não** colidem
no runtime que de fato resolve a chamada.

Um deduplicador em `OrdinalIgnoreCase` renomearia pares que não colidem de
verdade — mudando o nome de tool de agente que estava correto, que é a única
classe de dano que R1 se esforça para delimitar. Na direção oposta, um
`FindTool` que um dia passasse a ser insensível com o dedupe preso em `Ordinal`
deixaria colisão real escapar. Nos dois sentidos o acoplamento é real, e por
isso vira comentário no código apontando para `FindTool` como origem, não uma
constante escolhida em silêncio.

É **propriedade observável do conjunto final**, não detalhe interno: duas tools
que diferem só na caixa saem as duas com o nome que os resolvedores produziram.
Por isso entra na spec de `agent-tool-namespace`, não só no teste.

**Alternativa recusada — `OrdinalIgnoreCase` "por segurança", para o caso de
algum provedor tratar nomes de forma insensível.** Seria proteger uma hipótese
não verificada (nenhuma fonte primária consultada em V4 declara
insensibilidade) causando um dano verificado (renomear o que funciona). Se
algum provedor for insensível, isso é verificação a fazer e requisito a
escrever, não um default a adivinhar.

---

## Árvore de pastas

Só `apps/workers`. Nenhuma referência nova entre apps; nada entra em `libs/`
(um único consumidor real — convenção 2).

```
apps/workers/
├── src/Buteco.Workers/
│   ├── Agents/
│   │   └── AgentExecutionService.cs           (M) Concat → Deduplicate
│   ├── AgentDelegations/
│   │   └── AgentDelegationToolSetResolver.cs  (M) dedupe local sai
│   └── Mcp/
│       ├── ToolNameDeduplicator.cs            (+) NOVO
│       ├── RenamedAIFunction.cs               (+) NOVO — subclasse de DelegatingAIFunction (V6)
│       ├── ToolNameSanitizer.cs               (M) truncagem reutilizável + fonte do 64
│       └── McpToolSetResolver.cs              (M) orderby explícito
└── tests/Buteco.Workers.Tests/
    ├── ToolNameSanitizerTests.cs              (+) NOVO
    ├── ToolNameDeduplicatorTests.cs           (+) NOVO
    ├── AgentDelegationExecutionTests.cs       (M) ajuste do dono do dedupe
    ├── Agents/
    │   └── AgentToolNamespaceTests.cs         (+) NOVO — teste de acordo
    └── Mcp/
        └── McpToolSetResolverTests.cs         (M) colisão intra-MCP + ordem
```

`ToolNameDeduplicator` fica em `Mcp/` ao lado de `ToolNameSanitizer`, que já é
o compartilhado dos dois lados (`AgentDelegationToolSetResolver` já importa
`Buteco.Workers.Mcp` por causa dele). Criar uma pasta nova para uma classe
seria a abstração prematura que a convenção 2 recusa.

---

## Estratégia de teste

### Convenção 15 — cada guarda reprova antes de valer

Nenhum dos guardas abaixo é aceito sem ter reprovado contra o defeito real
reintroduzido de propósito. Três guardas desta base passaram verdes com o
defeito presente, e V5 encontrou o quarto. Os defeitos a reintroduzir, um por
guarda, estão enumerados como tarefa própria em `tasks.md` — não como nota.

O mais importante deles: **o guarda de colisão intra-MCP tem de reprovar com o
código de hoje**, `Concat` cru e tudo. Se passar verde antes da correção, o
guarda está errado, não o código.

### Convenção 11 — acordo com artefato real, não forjado

`AgentToolNamespaceTests` monta a colisão fazendo os **dois resolvedores
reais** produzirem o mesmo nome, e não escrevendo dois nomes iguais à mão numa
lista de teste:

- um `McpServer` real, seedado, com `Name` escolhido para que
  `Sanitize($"{Name}__{tool}")` caia exatamente sobre o nome que
  `AgentDelegationToolSetResolver` produz para um `Agent` Target também real e
  seedado;
- `McpToolSetResolver` real contra `FakeMcpServerHttpMessageHandler` (o fake é
  a dependência HTTP externa, permitido pela convenção 5;
  Postgres é o Testcontainers de `WorkerInfrastructureFixture`, real);
- `AgentDelegationToolSetResolver` real contra o mesmo `AppDbContext`.

Um conjunto forjado no teste passaria igual com o comportamento certo e com o
errado — é o modo de falha que a convenção 11 nomeia, e que já mordeu duas
vezes nesta base.

### Convenção 5 — par "com item"/"sem item"

Cada cenário de colisão tem o par sem colisão: conjunto com colisão renomeia e
loga; conjunto sem colisão preserva **todos** os nomes e **não** loga (a
asserção negativa é a que impede um dedupe zeloso demais de renomear o que não
devia). E o par de limite: nome no limite de 64 e nome bem abaixo dele.

---

## Divergências entre este design e a implementação (convenção 9)

Registradas aqui, não só no resumo do chat. Nenhuma mudou uma decisão; três
mudaram **onde** um guarda mora, que é o tipo de erro que a convenção 15 existe
para pegar.

1. **Os guardas de colisão intra-MCP estavam no lugar errado.** As tarefas 1.2 e
   1.3 mandavam escrevê-los em `McpToolSetResolverTests`, afirmando que o
   *resolvedor* produz nomes distintos. Mas a Decisão 1 põe o dedupe no ponto de
   concatenação, então o resolvedor **continua** produzindo nomes colidentes por
   contrato — e esses dois testes reprovavam depois da correção, não antes. O
   guarda foi para `AgentToolNamespaceTests`, no seam de produção, e os dois
   testes no resolvedor viraram caracterização da fronteira de contrato: ele
   compõe e sanitiza, dedupe não é trabalho dele. Mesma correção valeu para
   `DelegationToolNameSlugifierTests`, cujo teste de dedupe local perdeu o objeto
   quando o dedupe saiu de lá (o comportamento que ele garantia está coberto no
   seam, contraparte de R4).

2. **A afirmação de que o `Dictionary`/`HashSet` default é inseguro estava
   errada** — ver a nota na Decisão 9. `EqualityComparer<string>.Default` já é
   ordinal. O guarda de caixa reprova com `OrdinalIgnoreCase` e **não** reprova
   com o default, então a tarefa 2.7 pedia verificação de um defeito que não
   existe.

3. **O guarda de ordem determinística precisou virar dois testes**, e por um
   motivo que o design não previa: ordenação de `uuid` no Postgres é byte a byte,
   enquanto `Guid.CompareTo` no .NET compara os três primeiros grupos como
   inteiros little-endian. Um teste que afirmasse a ordem contra
   `servers.OrderBy(id => id)` reprovaria **mesmo com o `orderby` correto no
   lugar**. A ordem esperada passou a ser lida do próprio banco com
   `ORDER BY "McpServerId"`. O teste de estabilidade entre execuções ficou
   separado, e — como a tarefa 1.4 previa — ele passa sem o `orderby`: ausência
   de ordenação não garante ordem errada, só não garante ordem nenhuma.

4. **Uma expectativa minha sobre `ToolNameSanitizer` estava errada e o primeiro
   teste dele pegou.** `Sanitize(" tool")` produz `_tool`, não `__tool`: a
   substituição de caracteres roda antes da checagem de inicial, então o espaço
   já virou `_` — que é inicial válida — e não recebe um segundo `_`. Registrado
   como caso próprio no teste, porque a ordem interna não é óbvia e é exatamente
   o tipo de coisa que um arquivo de teste ausente deixa passar.

---

## Risks / Trade-offs

Convenção 10 — cada risco com contraparte verificável nomeada.

**[R1] O nome exposto muda para agentes que hoje sofrem a colisão, e um prompt
do operador pode citar o nome antigo.**
→ É a correção, não uma regressão: hoje esse agente já não consegue chamar uma
das duas tools. Contraparte: o cenário *"Conjunto sem nenhuma colisão preserva
todos os nomes"* (`agent-tool-namespace`) prova que **nenhum** agente sem
colisão tem nome alterado, delimitando o alcance da mudança aos agentes já
quebrados. O aviso da Decisão 7 dá ao operador o rastro para atualizar o prompt.

**[R2] O encurtamento da base para caber o sufixo (Decisão 4) pode gerar um nome
que colide com outro já usado, e o loop pode não fechar.**
→ Contraparte: cenário *"Sufixo de dedupe aplicado a um nome já no limite de
64"* (`agent-tool-namespace`) mais um caso em `ToolNameDeduplicatorTests` com
**três** nomes no limite de 64 colidindo entre si — que é o caso em que o
encurtamento acontece três vezes e o loop precisa fechar em nomes distintos e
todos ≤64.

**[R3] Ordenar a consulta de MCP (Decisão 8) muda a ordem em que os servidores
entram no conjunto, e portanto a ordem em que as conexões MCP são abertas e
descartadas.**
→ Contraparte: os cenários existentes de `mcp-tool-execution` que cobrem
degradação por servidor inacessível e ciclo de vida da conexão
(`McpToolSetResolverTests`, `McpToolExecutionEndToEndTests`) continuam valendo
sem alteração; se a ordem importasse para o dispose, eles reprovam. Mais o
cenário novo *"Servidores entram no conjunto na mesma ordem em execuções
repetidas"*.

**[R4] Tirar o dedupe local de `AgentDelegationToolSetResolver` (Decisão 3)
pode perder comportamento hoje garantido, se algum caminho resolver tools de
delegação sem passar pelo ponto de concatenação.**
→ Contraparte verificada: `AgentDelegationToolSetResolver.ResolveAsync` tem um
único chamador de produção, `AgentExecutionService.cs:181`, imediatamente antes
do `Concat` da linha 208 — não há caminho alternativo. Além disso os dois
cenários existentes do requisito *"Nome estável e sem colisão para a tool de
delegação"* seguem no delta spec **sem alteração de texto**, e precisam passar
depois da mudança de dono; se algum reprovar, o comportamento se perdeu.

**[R5] O guarda de colisão pode passar verde com o defeito presente — o modo de
falha mais caro desta base, e o que V5 acabou de encontrar no guarda existente
de `mcp-tool-execution`.**
→ Contraparte: a reintrodução deliberada de cada defeito é tarefa própria e
enumerada em `tasks.md` (convenção 15), com o defeito exato a reintroduzir
nomeado por guarda, e não uma instrução genérica de "verificar que o teste
falha".

**[R6] Um `McpServer` com nome ≥64 caracteres colidindo com uma tool de
delegação é caminho contrived; escrever o cenário pode virar teste de fixture,
não de comportamento.**
→ **Corrigido na implementação (convenção 9): esta redação estava errada.** Ela
dizia que o cenário MCP × delegação seria montado "pelo caminho não contrived, os
dois resolvedores reais produzindo o mesmo nome pela composição normal". Não
existe esse caminho: o slug de delegação nunca contém `__` e o nome MCP sempre
contém, então pela composição normal os dois conjuntos **não podem** colidir. A
truncagem é o único caminho, e o teste de acordo o usa — um `McpServer` cujo nome
composto passa de 64 e, truncado, cai exatamente sobre um nome de delegação.
É contrived como cadastro e o teste diz isso; o que ele exercita e que não é
contrived é o **mecanismo**: a truncagem apagando a separação entre os dois
espaços de nome. O caminho realmente alcançável na prática é intra-MCP, coberto
pelos dois cenários de `mcp-tool-execution` e pelo seam em
`AgentToolNamespaceTests`, e é ele que R5/V5 nomeiam como o defeito de verdade.

**[R7] Histórico já gravado referencia a tool pelo nome antigo.** É o único
risco desta change que alcança agente em produção que **hoje funciona** no
sentido de responder — o agente colidente chamava a tool errada, mas chamava.
V7 confirmou no framework que `FunctionCallContent`/`FunctionResultContent`
entram no histórico persistido e são restaurados na execução seguinte, então a
requisição seguinte mistura histórico com nome antigo e lista de tools com nome
novo. Se algum provedor rejeitar isso, o efeito não é cosmético: a sessão
daquele `contextId` para, sem recuperação além de expirar.

→ **Contraparte: o risco está vazio, e isso está medido — não suposto.** R7 só
alcança agente que **hoje** sofre colisão, e o censo de V8 encontrou **zero**
colisões entre todos os agentes reais do sistema. Não há produção; dev é o único
ambiente, e é onde o censo rodou. Então a change entra sem reescrever nenhum nome
de tool existente, e nenhum histórico persistido passa a citar um nome que saiu da
lista.

A metade que **é** verificável sem chave está verificada: o pipeline local
(`FunctionInvokingChatClient` + `ChatClientAgent`) é transparente a um histórico
que referencia função ausente da lista atual — V7 mais o cenário
`ExecuteAsync_HistoryReferencesToolNoLongerInToolSet_TaskStillCompletes`, que
produz o histórico de verdade (primeira execução chama a tool MCP real, o vínculo
sai do cadastro, segunda execução roda) em vez de forjá-lo. Isso isola a pergunta
que sobra ao provedor.

O risco passa a ser **do futuro, com detector construído**: o aviso de renomeação
da Decisão 7 é exatamente o sinal de que o primeiro cadastro colidente apareceu. A
pergunta aberta (algum provedor rejeita esse histórico?) e a saída desenhada
(limpar o `conversationSession` do `contextId` afetado, change própria) ficam
registradas como item em aberto em `02-HISTORICO_E_STATUS.md`, com gatilho no
primeiro aviso no log. **O que falta ali é observação com chave de provedor, não
análise.**

Esta é a correção de uma incoerência das redações anteriores, que punham a
verificação depois do apply e ao mesmo tempo diziam que a correção seria "change
própria sequenciada antes desta" — mandavam aplicar e corrigir-antes ao mesmo
tempo. A ordem final é: **um censo barato decide se há risco a fechar**, ele veio
limpo, e a pergunta caríssima fica com gatilho em vez de bloquear.

**[R8] O limite de 64 é o piso verificado de dois provedores, não dos três.**
V4 fechou OpenAI (64, Chat Completions) e Gemini (128) em fonte primária;
o limite do Anthropic não está declarado nem na documentação de tool use nem
no tipo `ToolParam` do SDK publicado. Se fosse menor que 64, o sanitizador
estaria permissivo demais para ele.
→ Contraparte: mesma tarefa 4.7 (precisa de chave real) — uma chamada com nome
de 64 caracteres contra o Anthropic responde a pergunta em uma requisição, e a
mensagem de erro real vira a fonte. Até lá, o comentário da constante diz que o
Anthropic não foi verificado, em vez de citar terceiro como autoridade.

---

## Tamanho, reprojetado por componente

Convenção 18. A projeção antiga desta change (10-14 arquivos / 500-800 linhas)
foi feita pelo método que errou por 55% na etapa 1, e **não é usada como
âncora**. Projeção por componente, **só código** — artefatos OpenSpec contados
à parte, porque misturá-los é a primeira das duas formas de engano medidas:

| componente | arquivos | linhas |
|---|---|---|
| `ToolNameDeduplicator` (novo: loop de dedupe `Ordinal`, encurtamento p/ 64, log) | 1 | ~65 |
| `RenamedAIFunction` (novo: subclasse de `DelegatingAIFunction`, só sobrescreve `Name`) | 1 | ~25 |
| `ToolNameSanitizer` (truncagem reutilizável + fonte do limite no comentário) | 1 | ~10 |
| `AgentExecutionService` (ponto de concatenação + injeção do deduplicador) | 1 | ~15 |
| `McpToolSetResolver` (`orderby` + comentário do porquê) | 1 | ~10 |
| `AgentDelegationToolSetResolver` (dedupe local sai) | 1 | ~12 |
| **subtotal código de produção** | **6** | **~137** |
| `ToolNameSanitizerTests` (novo, ~10 casos) | 1 | ~110 |
| `ToolNameDeduplicatorTests` (novo, ~10 casos: três-no-limite de R2, caixa/`Ordinal` de D9) | 1 | ~115 |
| `AgentToolNamespaceTests` (novo: acordo, 5 cenários + histórico de R7 + seeding) | 1 | ~285 |
| `McpToolSetResolverTests` (2 cenários: colisão intra-MCP, ordem) | 1 | ~70 |
| `AgentDelegationExecutionTests` (ajuste do dono do dedupe) | 1 | ~20 |
| **subtotal teste** | **5** | **~600** |
| **total código** | **11** | **~737** |

Artefatos OpenSpec (`proposal.md`, `design.md`, `tasks.md`, 3 delta specs):
6 arquivos, ~600 linhas — **fora** do número acima.

Duas observações honestas sobre esta projeção:

1. A régua "contagem de arquivo é dirigida pelo número de operações CQRS" não
   se aplica: esta change não tem nenhuma operação CQRS. O custo é dirigido por
   **cenário de teste**, e a proporção reflete isso — 83% do trabalho de código
   é teste, contra os 42% da change medida mais pesada em teste. Um defeito de
   invariante em ponto único é exatamente essa forma: pouco código, muito
   cenário.
2. O total cai dentro da faixa da projeção antiga (500-800). Isso **não** valida
   o método antigo: a faixa antiga incluía os artefatos OpenSpec, então o número
   comparável dela era mais baixo, e o acordo aparente vem de erros em direções
   opostas. A coincidência de intervalo é registrada para não ser lida como
   confirmação.
3. **A lição desta projeção não é um percentual, é o momento de fazê-la.** Ela
   subiu de ~642 para ~737 linhas entre a primeira redação e a revisão, sem que o
   escopo mudasse: `RenamedAIFunction` (V6), o cenário de caixa (Decisão 9) e o
   cenário de histórico (R7) simplesmente **não existiam** como itens quando a
   primeira contagem foi feita — a verificação ainda não tinha terminado. A regra
   que isso estabelece é **projetar depois que a verificação fecha, não antes**.
   Registrar isso como "+15%" seria trocar uma heurística cega ("somar 15%") por
   outra, que é exatamente o que a convenção 18 substituiu; a diferença entre as
   duas contagens não é um fator a aplicar, é o custo de ter contado cedo.

---

## Migration Plan

Não há migração. Nenhum esquema de banco muda, nenhum contrato de fio muda,
nenhum dado persistido carrega o nome exposto ao LLM (V1: não há mapa por nome
em lugar nenhum). O nome é recalculado a cada execução, a partir do cadastro.

Rollback é reverter o commit: a execução seguinte volta a montar os nomes pelo
caminho antigo, sem estado residual.

---

## Open Questions

Nenhuma incerteza de **negócio/produto** em aberto, e nenhuma pendência que
bloqueie o apply.

As incertezas técnicas foram fechadas contra código real, decompilação, fonte
primária e consulta ao banco: V1 (renomear não quebra a invocação), V2 (ordem das
operações, e o estouro de 64 já presente), V3 (uma das duas consultas não era
ordenada), V4 (o 64 é do Chat Completions do OpenAI, verificado na especificação
publicada; a regra de caractere inicial é escolha do repo; o limite do Anthropic
não é publicado), V5 (a colisão alcançável é intra-MCP), V6
(`DelegatingAIFunction` renomeia sem o delegate), V7 (o histórico persiste o nome
antigo e o pipeline local é transparente a isso) e V8 (**zero** agentes
colidentes em todo o sistema). Nenhuma derrubou a Decisão 1.

**Duas perguntas seguem sem resposta, nenhuma delas um bloqueio desta change.**
As duas dependem da mesma coisa — uma chave de API de provedor, o mesmo bloqueio
de `0b` — e as duas viram item em aberto com gatilho próprio em
`02-HISTORICO_E_STATUS.md`:

1. **Algum provedor rejeita histórico que referencia função ausente da lista
   atual de tools?** (R7.) Não bloqueia porque V8 mostrou que não há agente
   colidente. Gatilho: o primeiro aviso de renomeação no log.
2. **Qual o limite de nome de função do Anthropic?** (R8.) É pergunta de fonte,
   não de ambiente. Não bloqueia: 64 é o piso dos dois provedores verificados, e
   o nome mais longo do sistema tem 41 caracteres. Gatilho: quando o argumento do
   mínimo entre provedores voltar a ser necessário.
