## Context

A linha `metricas-de-operacao` tem quatro etapas fechadas: coleta de execução,
coleta de embedding, as duas rotas de agregação e a página de Insights. Esta é a
**quinta**, e existe porque a quarta a nomeou como lacuna: **M29 — o motivo das
recusas de `apps/api` não tem fonte nenhuma.**

Os três sítios de recusa, **reconferidos no código atual** (`5f2f6f8`) e não
herdados do registro de setembro:

| sítio | condição | o que grava hoje |
|---|---|---|
| `EnqueueingAgentHandler.cs:29` | `!agentState.IsActive` | `RejectAsync()` — estado e carimbo, nada mais |
| `:38` | `agentState.Provider is null \|\| agentState.Model is null` | idem |
| `:46` | `!providerCatalogService.IsProviderConfigured(...)` | idem |

Conferido também o que a recusa **não** produz: `task_executions` nasce no consumo
pelo worker (`ExecutionMetricsWriter.OpenAsync`, chamado por
`AgentExecutionService`), e a recusa acontece antes de qualquer publicação no
RabbitMQ. Então a recusa de entrada não tem linha de execução — é o
`caveat` `rejections-missing-from-executions`.

**A ordem foi invertida de propósito pelo dono**, e isso é uma restrição desta
change: a tela é o primeiro consumidor real do contrato, então a coleta nasce
sabendo o formato que o card pediu. A D1 é o resultado dessa leitura.

## Goals / Non-Goals

**Goals**

- Gravar o motivo de **toda** recusa feita por `apps/api`, com vocabulário
  fechado, texto no banco, determinado pela causa no código.
- Servir a contagem e os motivos nas **duas** rotas de agregação, no formato que o
  card de Motivos já consome.
- Cumprir o gatilho pendurado pela `rotas-de-agregacao-agente`: decidir a contagem
  da recusa de entrada **para os dois escopos ao mesmo tempo**.
- Retirar da resposta o `caveat` cuja lacuna deixou de existir, e manter o que
  continua verdadeiro — com o motivo escrito dos dois lados.

**Non-Goals**

- **Não mexer na tela.** Consumir o dado novo é change de seguimento da #52, com
  issue própria. (convenção 1)
- **Não** corrigir as outras lacunas da #52 (#66, #67).
- **Não** tocar `apps/workers`, `apps/inbox`, `apps/frontend`, `libs/` nem
  `stack/nginx.conf`.
- **Não** passar mensagem de recusa ao cliente A2A. A `TaskUpdater.RejectAsync`
  aceita uma `Message` opcional — *"Optional rejection reason"*, conferido no
  pacote instalado (`A2A 1.0.0-preview2`, `A2A.xml`), não de memória — e hoje
  nenhum dos três sítios a passa, então quem chama recebe `rejected` sem razão
  nenhuma. Isso é **outra** mudança: é contrato de protocolo com cliente
  (`apps/inbox`, delegação), não métrica. Vira issue. (D10)
- **Não** derivar a métrica de texto de mensagem, inclusive se essa mensagem
  passar a existir. (D3)

## Árvore de pastas proposta

Só o que esta change cria ou toca; `~` é arquivo modificado.

```
apps/api/
├── src/Buteco.Api/
│   ├── A2A/
│   │   └── EnqueueingAgentHandler.cs                  ~ (4 sítios)
│   ├── RejectionMetrics/                              (pasta nova)
│   │   ├── Entities/
│   │   │   └── TaskRejection.cs
│   │   ├── RejectionMetricsValues.cs
│   │   └── RejectionMetricsWriter.cs
│   ├── Insights/
│   │   ├── MetricsRegimeValidation.cs                 (checagem de boot)
│   │   ├── Queries/GetSystemInsights/GetSystemInsightsQueryHandler.cs   ~
│   │   ├── Queries/GetAgentInsights/GetAgentInsightsQueryHandler.cs     ~
│   │   └── Responses/
│   │       ├── SystemInsightsResponse.cs              ~
│   │       └── AgentInsightsResponse.cs               ~
│   ├── Options/MetricsOptions.cs                      ~ (a terceira constante)
│   ├── Infrastructure/
│   │   ├── AppDbContext.cs                            ~ (DbSet + mapeamento)
│   │   └── Migrations/
│   │       ├── AAAAMMDDHHMMSS_AddTaskRejections.cs            (gerado)
│   │       ├── AAAAMMDDHHMMSS_AddTaskRejections.Designer.cs   (gerado)
│   │       └── AppDbContextModelSnapshot.cs           ~ (gerado)
│   ├── Program.cs                                     ~ (chamada da checagem)
│   └── appsettings.json                               ~ (regime `rejection`)
└── tests/Buteco.Api.Tests/
    ├── RejectionMetricsMigrationTests.cs              (contêiner próprio)
    ├── RejectionMetricsWriterTests.cs                 (sem contêiner)
    ├── MetricsRegimeStartupValidationTests.cs         (sem contêiner)
    ├── SendMessageProviderRejectionTests.cs           ~
    ├── AgentDeactivationTests.cs                      ~
    ├── A2ATaskLifecycleTests.cs                       ~
    ├── InsightsEndpointsTests.cs                      ~
    └── AgentInsightsEndpointsTests.cs                 ~
```

**Nada entra em `libs/`.** `libs/` só recebe o que precisa concordar entre apps, e
o vocabulário de motivo de recusa tem **um** produtor e **um** consumidor, os dois
em `apps/api`. `apps/workers` não recusa na entrada e não lê a tabela nova; pôr o
vocabulário em `libs/` criaria dependência sem consumidor (convenção 2) e um
segundo lugar de onde o valor poderia divergir.

## Decisions

### D1 — O formato saiu da tela, e a divergência com o protótipo está decidida

Lido o componente (`FailureReasonsCard.tsx`) **e** os artboards
(`Main.dc.html`, `Estados.dc.html` do canvas `R9JSCiYw7pBEognkKhiaDh`, abertos
pela ferramenta `Artifact` porque o MCP `claude-design` recusou a conexão).

**O que o card mostra quando tem dado:** lista **rasa**, uma linha por motivo,
rótulo à esquerda e contagem à direita, **ordenada por contagem decrescente**. O
protótipo desenha **cinco** linhas misturando fases de execução e uma causa de
recusa; o componente já monta a lista concatenando **duas** fontes
(`errors.byPhase` e `errors.indexingFailures`) com chaves de origem distintas e
ordenando pelo total. Consequências para o contrato, e são as três decisões de
formato:

1. **O motivo chega como lista própria de valor e contagem**, irmã de
   `byPhase` — **não** dentro de `byPhase`. `byPhase` é `FailurePhase`, cujo
   vocabulário fechado a `agent-execution-metrics` enumera em sete valores; enfiar
   um motivo de recusa ali seria mentir no contrato de outra capability.
2. **Um valor por recusa, sem subcardinalidade.** O card não tem coluna para
   provedor, modelo nem agente na lista de motivos, e a contagem de uma linha do
   protótipo (`5`) é exatamente a contagem de recusas do card vizinho — então a
   soma dos motivos **fecha** com a contagem. É isso que obriga a coluna de motivo
   a ser **obrigatória** e o valor desconhecido a passar cru (a spec do sistema
   escreve as duas).
3. **O rótulo de operador é da tela, não da rota.** A rota serve o valor do
   vocabulário; `failurePhaseLabels.ts` já é onde a tradução mora, e valor
   desconhecido lá renderiza neutro e monoespaçado, nunca o rótulo de outro.

**A divergência, e quem ganha.** O protótipo **nomeia uma** das causas com
contagem — *"Agente sem provider ou modelo configurado — 5"* — e a tela **não
nomeia nenhuma**: o guarda `NEGATIVO: NENHUMA causa é nomeada para as recusas`
afirma a ausência de `sem provider`, `agente inativo` e `modelo configurado` no
card. A tela ganha, e o motivo está escrito no componente: a linha do protótipo é
*plausível* — é de fato uma das causas —, e por isso ninguém desconfiaria dela; a
tela afirmaria com precisão de número uma causa que ninguém mediu.

**E o protótipo nomeia duas causas em outro lugar**, no texto literal do card de
Falhas que a tela copia: *"Recusa é agente inativo ou sem provider e modelo
configurados"*. **A terceira — provedor não configurado no ambiente (`:46`) — não
é nomeada em lugar nenhum**, nem no protótipo nem na tela. Isso não muda nada
aqui, e é achado para a change de seguimento: quando os motivos aparecerem, a
frase passa a enumerar dois de três valores exibidos ao lado dela.

**O que a tela declara sobre a lacuna hoje:** nada. A lacuna em moldura foi
removida na décima rodada de conferência da #52, e
`rejection-reason-not-collected` está classificado `not-on-this-page` em
`caveatLabels.ts:60-77` — **não é renderizado**. O registro da lacuna é a issue
#51, que é esta change. Consequência prática: retirar o `caveat` da resposta **não
apaga nada da tela**, porque a tela não o mostra.

### D2 — Tabela própria `task_rejections`, e as outras duas saídas foram medidas

**A decisão: tabela nova em `apps/api`**, com `TaskId` (chave), `AgentId`,
`Reason` e `RejectedAt`. Sem FK, como as cinco tabelas de métrica que já existem.

O que fechou a decisão **não** foi o gosto por separação: foi um precedente
escrito nesta base, em `AppDbContext.cs:361-376`, que decidiu a mesma pergunta
com a mesma estrutura e **corrigiu a decisão anterior** (convenção 9):

> *"TABELA PRÓPRIA, e não `provider_calls` com TaskId anulável: M11 e M19 são
> visões separadas, então reusar a tabela obrigaria TODA consulta de M11 a M17 a
> filtrar por `Purpose` — e quem esquecesse o filtro receberia um número maior e
> plausível, não um erro."*

**Saída 2 — linha em `task_executions` — recusada, com o custo medido.** Era a
candidata mais alinhada à primeira vista, e há precedente de estado terminal sem
chamada ao provedor (`FailurePhase = DelegationDepthExceeded`, com requisito
próprio na `agent-execution-metrics`). A conferência recusa por três razões, em
ordem de peso:

1. **O raio de alcance é de 22 consultas por rota** — contadas por leitura nos
   dois handlers (24 e 25 ocorrências de `task_executions`, menos as de
   comentário). Delas, as que quebrariam em silêncio: M1/M2 (contagem de tasks
   **executadas** — uma recusa passaria a contar como execução), M6/M7 (série
   diária e dia da semana), M21 (duração média — a recusa entraria com duração
   perto de zero, porque `SubmittedAt` existe e `EndedAt` seria o instante da
   recusa), M22 (tempo de fila), M24 (chamadas por task — a recusa entraria com
   zero chamadas e derrubaria a média), M25 (resíduo), M26 (profundidade) e M32
   (`OpenExecutionCount`). **Nenhuma delas erraria alto ou baixo de forma
   visível**: todas devolveriam número plausível. É literalmente o modo de falha
   que o precedente acima nomeia.
2. **A semântica da tabela é "o que o worker consumiu"**, escrita em dois lugares:
   a spec (*"uma linha por task que consumir"*) e a própria entidade
   (*"Esta entidade vive em `apps/api`, que nunca escreve nela"*). Escrever ali
   faria de `apps/api` um segundo escritor de uma tabela espelhada em
   `apps/workers`, e mudaria o significado da tabela para todo leitor futuro.
3. **Duas das três causas não têm provedor nem modelo** — são a ausência deles —,
   então a linha nasceria com as colunas de snapshot vazias, e M28 as agruparia.

**Saída 1 — coluna em `a2a_tasks` — recusada, por três motivos concretos.**
(a) Quem escreve `a2a_tasks` é o `ITaskStore` do SDK (`PostgresTaskStore`), a
partir de um `AgentTask`; o motivo não está no `AgentTask`, então o handler
precisaria de uma **segunda escrita** na mesma linha, cuja ordem contra a do store
não é garantida — a linha pode não existir ainda quando o handler quiser marcá-la.
(b) `a2a_tasks` **tem FK para `agents` com cascade**: o dia em que houver rota de
exclusão de agente, a história de recusas desaparece junto — o oposto da regra
já escrita para as tabelas de métrica (*"a métrica registra o que aconteceu, e
isso não muda porque o catálogo mudou depois"*). (c) A tabela é **espelhada em
`apps/workers`**, então a coluna custaria duas migrações e um espelho para um dado
que só `apps/api` escreve e só `apps/api` lê.

**O custo aceito da saída escolhida:** uma fonte a mais para a agregação juntar.
É pequeno e conhecido — as duas rotas já leem seis tabelas, e o bloco de erros já
faz `left join` entre `a2a_tasks` e `task_executions` para M32.

**Forma da tabela, com o motivo de cada coluna:**

| coluna | tipo | nulo | por quê |
|---|---|---|---|
| `TaskId` | `text` | não | chave. Uma task recusada é terminal, e o protocolo recusa mensagem nova para task terminal — então não há segunda recusa da mesma task. Chave em `TaskId` é a mesma escolha de `task_executions`, e torna a dupla escrita um erro visível em vez de linha duplicada |
| `AgentId` | `uuid` | não | o recorte do escopo do agente sai daqui, sem junção com o store nem com o catálogo |
| `Reason` | `text` | **não** | vocabulário fechado como texto (convenção 12). Obrigatória porque a linha só nasce num dos pontos que decidem a recusa — e é o que faz a soma dos motivos fechar com a contagem |
| `RejectedAt` | `timestamptz` | não | a janela da agregação. Vem do `TimeProvider` de `apps/api`, não de `now()` no banco, pelo mesmo motivo já registrado na rota do sistema: janela cujo "agora" nasce no banco não é verificável de forma determinística |

Índice em `RejectedAt` (o filtro de janela) e em `AgentId` (o recorte por agente) —
os mesmos dois de `task_executions`, pelas mesmas duas consultas. **Sem FK**, e
não por descuido: para `a2a_tasks` ela criaria a corrida do item (a) acima; para
`agents`, a cascade do item (b).

### D3 — O vocabulário sai da causa no código, é texto, e é fechado

Molde de `ExecutionMetricsValues.FailurePhase`: classe estática de constantes
`string`, gravadas como texto, nunca ordinal. O `remarks` daquele tipo já escreve a
razão, e ela vale igual aqui: a base **já recusou duas vezes** classificar por
texto de mensagem — `KnowledgeIndexingFailure.Describe`, que produz texto de
operador e distingue quatro `throw` por `Message.Contains`, e o próprio
`FailurePhase`.

O ponto que isto protege nesta change: a `RejectAsync` do SDK aceita uma mensagem
de razão (D10). Se ela algum dia for passada, **o motivo continua saindo do sítio
do código** — a mensagem é para o cliente, e é prosa de quem a escreve.

### D4 — Quatro valores, não três, e o quarto é achado desta leitura

O sítio `:29` é `if (!agentState.IsActive)`, e `agentState` vem de:

```csharp
return await dbContext.Agents.AsNoTracking()
    .Where(agent => agent.Id == agentId)
    .Select(agent => new AgentState(agent.IsActive, agent.Provider, agent.Model))
    .FirstOrDefaultAsync(cancellationToken);   // AgentState é record STRUCT
```

`FirstOrDefaultAsync` sobre um `record struct` devolve `default` quando não há
linha — `IsActive = false`. **Agente que não existe entra pelo mesmo `if` que
agente inativo**, e gravar `AgentInactive` para ele afirmaria um estado que
ninguém leu (convenção 13: a proveniência decide, não o tipo do campo).

Os quatro valores, um por causa:

| valor | condição (linha) | recusa (linha) | condição real |
|---|---|---|---|
| `AgentNotFound` | `:27`* | `:29` | nenhuma linha de agente com aquele id |
| `AgentInactive` | `:27` | `:29` | linha existe e `IsActive = false` |
| `ProviderOrModelMissing` | `:36` | `:38` | `Provider` ou `Model` nulos |
| `ProviderNotConfigured` | `:44` | `:46` | provedor sem chave no ambiente de `apps/api` |

As linhas de recusa são as três da tabela do Context; as de condição são o `if`
que decide cada uma, e é nelas que o motivo é escolhido.

\* a distinção custa **uma** mudança: `GetAgentStateAsync` passa a devolver
`AgentState?`, e o `null` vira `AgentNotFound`. Nada mais muda — a task continua
recusada, sem job publicado, no mesmo estado do protocolo.

`ProviderNotConfigured` reusa **de propósito** o nome que
`ProviderValidationOutcome` já dá ao mesmo fato no cadastro: mesmo conceito, mesma
palavra, e um vocabulário só para quem lê os dois.

**Alcançável hoje?** Não por rota: `Agent` não tem exclusão em `apps/api`. É
alcançável por escrita direta no banco, e é assim que o guarda o exerce.

**CORREÇÃO DEPOIS DO APPLY (convenção 9): o guarda mostrou que a leitura estava
pela metade, e a decisão continua a mesma por um motivo melhor.**

Ao escrever o guarda, a primeira conclusão foi que `AgentNotFound` era
**inalcançável**: com o agente apagado, `a2a_tasks` tem FK para `agents`, a task
não chega a ser persistida, e a rota responde
`{"error":{"code":-32603,"message":"An internal error occurred."}}`. Medido, não
suposto.

**O guarda corrigiu a conclusão.** O handler **chega** ao caminho, e a linha de
métrica **é gravada** com `AgentNotFound` — porque `task_rejections` não tem FK,
que é a decisão da D2 pagando no primeiro caso que a exercita. O que não acontece
é a **resposta `rejected` do protocolo**.

São dois fatos, e o caso afirma os dois:
`SendMessage_ForAgentDeletedAfterTheServerWasResolved_RecordsAgentNotFound_ThoughTheProtocolAnswersError`.
O `-32603` virou item aberto com gatilho — é estado que o protocolo tem como
responder e não responde —, e **não** é escopo desta change, que é coleta.

**Decidido pelo dono em 25/09/2026: quatro.** O `default` do `record struct` é
**ausência de leitura**, não inatividade, e gravar `AgentInactive` sobre ele
afirma um estado que ninguém leu — convenção 13 na forma mais direta.

**A alternativa recusada, com o que ela custava:** ficar em três economizaria um
valor de vocabulário e custaria a distinção entre **"o agente está desligado"** e
**"o agente não está lá"** — que são problemas diferentes para quem opera: o
primeiro se resolve reativando, o segundo significa que o cliente está chamando
um endereço A2A cujo agente não existe mais. Colapsados no mesmo valor, a lista
de motivos manda reativar um agente que não há.

### D5 — A contagem nova é campo próprio; `rejectedCount` não muda de conteúdo

**Achado medido:** `rejectedCount` das duas rotas conta
`task_executions.TerminalState = 'Rejected'`, e existe **um único** sítio que grava
esse valor — `AgentExecutionService.cs:236`, `DelegationDepthExceeded`. Ou seja:
hoje aquele número é composto **inteiramente** de recusas por profundidade de
delegação, feitas por `apps/workers`, e **nenhuma** recusa de entrada está nele.
A tela o rotula *"Recusadas na entrada"* e explica, ao lado, as causas de
`apps/api` — nenhuma das quais produz aquele número.

Três saídas, e a escolhida é a terceira:

1. **Somar as duas fontes em `rejectedCount`** — recusada: o número passaria a
   abranger duas janelas de regime diferentes (execução e recusa), e um número só
   sobre dois regimes é exatamente o que o mapa de regimes existe para impedir.
2. **Repropositar `rejectedCount` para a recusa de entrada** — recusada: muda o
   significado de um campo servido sem mudar o nome, e o cliente que já o lê
   passaria a mostrar outra população sem nenhum sintoma.
3. **Campo novo `rejectedAtEntryCount`**, com o regime da coleta de recusa, ao
   lado de `rejectedCount`, que fica com a fonte e o significado que tem — agora
   **documentado pelo que ele conta**. A tela troca de campo na change de
   seguimento; até lá, ela mostra o mesmo número que mostra hoje.

Contrato resultante do bloco de erros, nas duas rotas:

```
errors: {
  executionRegime, indexingRegime, rejectionRegime,   // + 1 campo
  failedCount, rejectedCount,
  rejectedAtEntryCount,                              // + 1 campo
  byAgent | byProviderAndModel,
  byPhase,
  rejectionsByReason: [ { reason, count } ],          // + 1 lista
  indexingFailures (só no sistema),
  nonTerminal,
  caveats                                            // − 1 código
}
```

**O nome `rejectedAtEntryCount` está decidido pelo dono em 25/09/2026**, e o que
o sustenta é o par: ele diz o que conta **e se separa do `rejectedCount`
existente**, que é justamente a confusão que existe hoje — campo antigo contando
só `DelegationDepthExceeded` de `apps/workers` sob o rótulo *"Recusadas na
entrada"*. Um nome que não separasse os dois manteria a confusão com um campo a
mais dentro dela.

Aditivo em campo, subtrativo em `caveat`. O nome de fio de cada campo é afirmado
por guarda que **lê o JSON cru** — round-trip pelo mesmo tipo é cego à política de
nomes (convenção 12), e os nomes aqui não têm sigla, o que não dispensa o guarda.

### D6 — Um `caveat` cai, o outro fica, e a razão de cada um

| código | destino | razão |
|---|---|---|
| `rejection-reason-not-collected` | **sai das duas rotas** | a lacuna que ele descreve deixa de existir. Código de parcialidade que sobrevive à lacuna afirma uma limitação que não há e ensina o cliente a ignorar os outros — a spec do sistema ganha esse requisito por escrito |
| `rejections-missing-from-executions` | **fica, texto inalterado** | continua inteiramente verdadeiro: a recusa de entrada **não** produz linha de execução, **não** entra no percentual de falha (nem no numerador nem no denominador), e **M28 continua parcial por construção** — duas das quatro causas são a ausência de provedor ou modelo, então não há o que agrupar |

A constante `RejectionReasonNotCollected` sai do `InsightsCaveats` junto: constante
que ninguém emite é código morto, e deixá-la ali convida a reemiti-la.

**Do lado do cliente**, o código deixa de chegar e a tela não muda — ele é
`not-on-this-page` e não é renderizado. O mapa de `caveatLabels.ts` continua com a
entrada dele até a change de seguimento; isso não é defeito, é entrada não
alcançada. **A #52 previu exatamente este dia:** *"o dia em que o número que cada
um qualifica entrar na tela, os casos falham e apontam o lugar"*.

### D7 — Terceiro regime, e a checagem de boot que o torna seguro

O mapa de regimes foi feito mapa **para isto**, e está escrito nos dois lugares:
na spec (*"a etapa seguinte da linha acrescentará um terceiro regime e o contrato
precisa absorvê-lo sem mudar de forma"*) e no `MetricsOptions` (*"a etapa 4 da
linha acrescenta um terceiro, e o mapa o absorve sem mudar o contrato"*). Entra
`rejection`, com o instante do **deploy** desta coleta, em configuração — nunca
`min(RejectedAt)`.

**O bloco de erros passa a declarar três regimes**, e não pode eleger um: ele lê
`task_executions` (execução), `knowledge_indexing_attempts` (embedding) e
`task_rejections` (recusa). Um nome só ali daria a três coletas a data de uma.

**E aqui está o defeito que esta change abriria se não fizesse mais nada.**
`RegimeStart` devolve `null` para regime ausente do mapa, e `Later` então
**não recorta nada**: a janela pedida valeria inteira, e o período anterior à
coleta viria com contagem `0` — *"medi e não achei recusa nenhuma"* — em vez de
ausência. Número plausível, em silêncio, que é o modo de falha que o
`appsettings.json` já descreve num comentário longo... e comentário não reprova
nada.

**Decisão: checagem de boot** (convenção 8, e o precedente é a checagem de fuso da
`rotas-de-agregacao-sistema`): no startup, todo regime declarado pelas rotas tem
de ter instante no mapa, ou a inicialização falha nomeando o que falta. Vale para
os três, não só para o novo — e transforma "chave esquecida num ambiente" de
número plausível em falha de boot.

**Custo de espelho medido:** as duas fixtures de Insights fixam os regimes em
configuração de propósito (*"Fuso e regimes FIXADOS ... é o que torna estes
guardas independentes da máquina"*). As duas passam a fixar o terceiro — e **têm
de** fixá-lo, porque herdar o instante do `appsettings.json` (o do piloto,
posterior ao dado semeado de setembro) cortaria a janela e zeraria os guardas
novos. As outras fixtures não precisam de nada: sobem com o `appsettings.json`
real, que ganha a chave.

### D8 — A gravação não lança, e acontece **depois** da recusa

Molde do `ExecutionMetricsWriter`, cujo `remarks` já escreve a regra:
*"NENHUM MÉTODO DESTE TIPO LANÇA (convenção 4) ... Métrica que muda o resultado
do que ela mede não é métrica."*

Duas consequências, as duas com guarda:

- **Ordem:** `await updater.RejectAsync(...)` primeiro, gravação depois. Não há
  ordem de execução em que uma falha na métrica altere o estado em que a task
  terminou nem faça o job ser publicado.
- **Falha:** `LogWarning` com o `TaskId`, e segue. O guarda exercita isso com o
  escritor apontado para um banco inalcançável — **sem contêiner**, que é o que
  torna esse caso um teste de unidade barato em vez de mais uma classe de
  contêiner.

O escritor recebe `IServiceScopeFactory` e resolve o `AppDbContext` por escopo,
como o handler já faz para ler o estado do agente — o `EnqueueingAgentHandler` é
construído por agente pelo registry, fora do contêiner de DI de requisição.

### D9 — Onde os guardas moram, e a mutação

**Regra de colocação:** cada guarda vive na classe cuja fixture já exercita aquele
caminho, para **não** acrescentar classe de contêiner (ver a medição na D11). O
único arquivo novo com contêiner é o de schema, que é o molde da casa
(`ExecutionMetricsMigrationTests`, `EmbeddingMetricsMigrationTests`).

| guarda | onde | contêiner novo |
|---|---|---|
| `AgentInactive` grava motivo | `AgentDeactivationTests` | não |
| `ProviderOrModelMissing` e `ProviderNotConfigured` gravam motivos **distintos** | `SendMessageProviderRejectionTests` | não |
| `AgentNotFound` não vira `AgentInactive` | `SendMessageProviderRejectionTests` | não |
| **par negativo:** task aceita não grava linha | ~~`A2ATaskLifecycleTests`~~ → **`AgentDeactivationTests`** (ver abaixo) | não |
| schema: `Reason` é `text` e `NOT NULL`, sem FK | `RejectionMetricsMigrationTests` | **sim (1)** |
| falha de gravação não muda a recusa | `RejectionMetricsWriterTests` | não |
| regime ausente reprova o boot | `MetricsRegimeStartupValidationTests` | não |
| as duas contagens separadas, os motivos, o motivo desconhecido, o `caveat` que saiu | `InsightsEndpointsTests` | não |
| o mesmo no escopo do agente, mais a igualdade entre escopos | `AgentInsightsEndpointsTests` | não |

**Convenção 15 — cada guarda reprova contra `HEAD` antes de valer.** Os de motivo
reprovam por construção (não existe coluna hoje). Os dois que precisam de atenção
porque poderiam ficar verdes com o defeito:

- **o guarda das três causas distintas**: escrito como "três valores **diferentes**
  entre si", ele ficaria verde se todas gravassem o mesmo valor. Escrito como
  "cada causa grava **o seu** valor nomeado", reprova. É a quinta forma da
  convenção 15 (critério que o acerto e o erro satisfazem juntos);
- **o guarda do `caveat` que saiu**: `DoesNotContain` sobre a lista reprova contra
  `HEAD` hoje, e é o que prova que a retirada aconteceu nas **duas** rotas — hoje
  a rota do agente **não tem nenhum teste** afirmando os `caveats` do bloco de
  erros, e é por isso que o guarda dela é caso novo e não adaptado.

**Mutação, no molde das últimas:** trocar a atribuição de **uma** causa (gravar
`AgentInactive` no sítio de `ProviderOrModelMissing`) e verificar que reprova
**só** o guarda daquela causa — se reprovar o de outra, o guarda está afirmando no
componente errado.

**RESULTADO DA MUTAÇÃO, e ela achou um guarda mal colocado (convenção 9).** Na
primeira execução reprovaram **dois**: o da causa e o da sobrevivência da linha à
exclusão do agente — porque este afirmava o *valor do motivo* como prova de que a
linha existia. Corrigido para afirmar a **existência** da linha; repetida, a
mutação reprova exatamente um. É a segunda forma da convenção 15, e foi a mutação
que a pegou, não a revisão.

**E o par negativo mudou de classe.** Ele ia para `A2ATaskLifecycleTests`, que lê a
mensagem publicada do **RabbitMQ real** — um caso novo publicando ali faria o caso
vizinho ler a mensagem errada, que é a família de flake por ordem que esta base já
registrou. Foi para `AgentDeactivationTests`, onde o publisher é duplo e a asserção
filtra por `AgentId`: independente de ordem por construção.

### D10 — O que fica de fora, com o registro

- **A mensagem de recusa ao cliente A2A.** `RejectAsync(Message?, CancellationToken)`
  existe e nenhum dos três sítios a usa — conferido no `A2A.xml` do pacote
  instalado, `1.0.0-preview2` (convenção 6: SDK de terceiro não se lê de memória).
  Hoje `apps/inbox` e a delegação recebem `rejected` sem razão nenhuma. **Vira
  issue**: é contrato de protocolo com cliente, e a métrica não depende dela.
- **O percentual da recusa.** Continua não existindo, por decisão já registrada da
  #52 (D10 dela): numerador e denominador contariam populações diferentes.
- **A tela.** Change de seguimento, issue própria.
- **Índice composto** `task_rejections(AgentId, RejectedAt)`: não entra. Os dois
  índices simples cobrem as duas consultas, e o precedente da linha é que índice
  sugerido pela forma **não** entra sem medição (os cinco de `task_executions` e
  `embedding_calls` não entraram). Gatilho herdado: remedir o plano com o piloto.

## Projeção (convenção 18, vigésima medição)

**Baselines remedidas hoje, 25/09/2026, sobre `5f2f6f8`** — não herdadas das de
24/09. Estado declarado: `podman ps` com **dois** contêineres de pé
(`buteco-agents_postgres_1`, `buteco-agents_rabbitmq_1`, ambos *healthy*), `waha`
parado; as quatro suítes rodaram **em sequência**, não em paralelo.

| suíte | 24/09 | **25/09** | duração | nota |
|---|---|---|---|---|
| `apps/frontend` | 927 | **1218** (107 arquivos) | 1m57s | +291 são a #52 |
| `apps/api` | 404 | **404** | 3m46s | sem mudança de contagem |
| `apps/workers` | 386 | **386** | 9m21s | sem mudança de contagem |
| `apps/inbox` | 203 | **203, com 3 reprovando** | 1m05s | ver o achado abaixo |

**O salto de `apps/frontend` é a #52 entrando, não anomalia de medição.** De
**927 em 84 arquivos** para **1218 em 107 arquivos**: +291 casos e +23 arquivos, e
eles têm dono — a página de Insights, mergeada em `5f2f6f8` no dia anterior a esta
medição. Sem essa frase ao lado, o número seguinte a citar 927 vai parecer ter
achado uma regressão de contagem; com ela, a baseline de `apps/frontend` para
qualquer change futura é **1218**.

As durações são **todas** maiores que as de 24/09 (api 2m05→3m46, workers
6m48→9m21, inbox 20s→1m05): as quatro rodaram encadeadas, com contêineres
subindo e descendo entre elas. É o estado colado ao número (convenção 22), não
uma medição de desempenho.

### A régua de contenção de `apps/api` estava errada, e a medição diz em que sentido

O `02` registra **31 classes de contêiner** medidas em 23/09/2026, com gatilho de
consulta na **32ª**. Medido hoje pelo mesmo critério do registro (classes com
fixture de contêiner **mais** classes que constroem o seu próprio, excluída
`Support/`): **40**.

E a contagem foi repetida no **commit do registro** (`2d4bc3c`): **40 também**.
Então o número não cresceu — **ele nasceu errado**, por 9 classes, quase certamente
por ter sido contado sem a subpasta `Knowledge/` (11 classes). Consequências:

- **o gatilho da "32ª classe" já estava vencido quando foi escrito**, e ninguém
  foi consultado porque o número dizia 31;
- **a régua recalibrada, com o estado colado:** `apps/api` tem **40 classes de
  contêiner** (36 por fixture + 4 que constroem o seu), medidas em 25/09/2026
  sobre `5f2f6f8`, com a suíte em **404 testes / 3m46s**, contêineres em paralelo
  e **um** por classe;
- **esta change acrescenta uma** (a de schema), e o dono decidiu em 25/09/2026 que
  isso **não bloqueia**.

**E o que o número serve para LER, escrito, porque é o que faltava.** Hoje a
resposta é: **registra o crescimento, e nada mais.** Não há critério, e isso é
resultado legítimo — melhor que um limiar inventado agora, que seria número sem
medição por trás (convenção 22). Ela é consultada em três situações, as mesmas de
antes: suíte lenta; suíte reprovando **em bloco na inicialização de fixture** (o
sintoma de contenção, que parece falha de teste); e a decisão de acrescentar mais
uma classe com contêiner.

**O que a tornaria acionável, nomeado e NÃO decidido**, para que a próxima
medição saiba o que estava na mesa:

| candidato | o que precisaria de medição |
|---|---|
| **limiar por duração**, com a contagem colada | a curva duração × classes em pelo menos três pontos; hoje há **um** (404 / 3m46s / 40 classes) |
| **rodar por grupos acima de N**, no molde da `WorkerHostCollection` | qual N, e se serializar custa mais tempo do que a contenção custa em flake |

**Gatilho de reabertura:** `apps/api` reprovar em bloco na inicialização de
fixture, **ou** a suíte passar de **6 minutos** — o dobro da medida de hoje, que é
a única âncora que existe.

**Menção cruzada, porque é a mesma família:** `apps/workers` mantém a régua
gêmea à mão, e ela **já quebrou duas vezes** pelo mesmo mecanismo — limiar de
carga medido com 7 classes de host, citado depois sobre 11 e sobre 12 (ocorrências
1 e 4 da convenção 22). São duas referências do mesmo tipo, mantidas à mão em dois
apps, e nenhuma das duas tem dono automático. Quem recalibrar uma olha a outra.

É a terceira ocorrência da convenção 19 no mesmo tema (número classificado sem a
baseline), e a primeira em que a correção é *para cima*.

### Cenários da delta, por categoria

**27 cenários**, contados no arquivo, separando o que é novo do que o `MODIFIED`
obriga a copiar:

| categoria | quantos | onde |
|---|---|---|
| **novos** | **21** | 9 em `agent-rejection-metrics`, 8 no sistema, 4 no agente |
| **preexistente editado** | 1 | "Os dois regimes chegam separados" → "Os três" |
| **preexistente copiado** | 5 | os dois `MODIFIED` do sistema e o do agente |

Casos de teste, pelas três categorias que a série acumulou:

| categoria | casos | detalhe |
|---|---|---|
| **novo** | **19** | 4 de motivo por causa + 1 negativo + 2 de schema + 1 de falha de gravação + 2 de boot + 5 no sistema + 4 no agente |
| **reforçado** | 0 | nenhum caso existente ganha asserção sem mudar de veredito |
| **adaptado** | **3** | `Rejections_AreNotPresentedAsIfTheCountWereComplete` (o `Contains` do `caveat` vira `DoesNotContain`), `Regimes_ArriveSeparately_WithTheirOwnStarts` (ganha o terceiro), e as **duas fixtures** de Insights (fixar `Metrics:Regimes:rejection`) — contadas como um item porque é a mesma edição |

**A pergunta que a décima sétima mandou fazer** — *a asserção dos guardas de
`rejectedCount` sobrevive à mudança?* — tem resposta **sim**, e por decisão: a D5
não muda a fonte nem o valor de `rejectedCount`, exatamente para que os guardas de
`rejectedCount` das duas rotas continuem válidos. Se a saída tivesse sido somar as
fontes, esses guardas entrariam como adaptados e a projeção subiria.

### Linhas, nos três níveis, com os duplos à parte

| nível | projeção | como foi contada |
|---|---|---|
| **escrita à mão — produção** | **~170 lógica + ~180 comentário** (~350) | 4 arquivos novos pequenos + 6 modificados; a proporção ~1,05:1 sai da contagem de **registros de mecanismo** que esta change entrega: quatro (a recusa da saída 2 com o número das 22 consultas, a corrida do store em `a2a_tasks`, o quarto valor do vocabulário, e o modo de falha do regime ausente) |
| **escrita à mão — teste** | **~420** | 19 casos novos. Custo unitário ~19-21 linhas por caso, com os 5 casos de rota puxando para 25-40 (arranjo próprio: semear recusas em duas fixtures) |
| **duplos** | **~60** | item próprio, pela memória da décima segunda medição. Aqui é **um só e barato**: o `AppDbContext` apontado para banco inalcançável no teste do escritor. Nenhum guarda desta change simula provedor, e é por isso que o número é uma ordem de grandeza menor que o daquela |
| **gerado** | **~760** | `.Designer.cs` da migração (~735: o snapshot **inteiro**, ancorado nos 709 do `AddEmbeddingMetrics`) + ~25 no `AppDbContextModelSnapshot.cs`. A migração `.cs` em si, ~40 |
| **modificados, em pares** | incluído acima | todo arquivo de produção modificado arrasta o teste dele; os 6 modificados de produção têm 5 arquivos de teste correspondentes já contados |

**Total à mão: ~830 linhas** (350 produção + 420 teste + 60 duplos), em **~18
arquivos** — **7 criados** (4 de produção + 3 de teste, sem contar os 2 gerados da
migração) e **11 modificados** (6 de produção, 5 de teste).

**Gerado é ~48% do diff de código** (~800 de ~1.670), na faixa dos 45% medidos na
etapa 2 e longe do caso extremo de 3,1x — porque aqui a migração cria **uma**
tabela, não duas com FK.

**As duas direções de erro mais prováveis, nomeadas:** (1) para **cima** em teste,
se semear recusas nas duas fixtures exigir tocar o `SeedAsync` de ~100 linhas de
cada uma mais do que as ~10 linhas projetadas; (2) para **baixo** em produção, se
a checagem de boot precisar de mais do que ler o mapa — por exemplo, se a lista de
regimes declarados tiver de sair de um só lugar que hoje não existe. A convenção
avisa que nomear direções não substitui contar componentes, e os componentes estão
contados acima.

## Achados que viram issue (convenção 23)

Nenhum deles é corrigido aqui.

1. **A spec `system-insights-ui` descreve um card que não existe.** O requisito
   *"Motivos de falha, com o motivo da recusa declarado como lacuna"*
   (`openspec/specs/system-insights-ui/spec.md:380-407`) diz que o card apresenta
   a contagem de recusas mais o texto de que o motivo não é coletado; a
   implementação faz o **oposto**, por decisão do dono na décima rodada, e o
   guarda `NEGATIVO: a recusa NÃO vira quadro no card de Motivos` afirma que a
   palavra *recusa* não aparece no card. A spec sincronizada ficou com a versão
   pré-decisão (convenção 9). Precisa ser corrigida junto da change de
   seguimento, que vai reescrever o requisito de qualquer forma.
2. **O texto do card de Falhas enumera duas das quatro causas.** *"Recusa é agente
   inativo ou sem provider e modelo configurados"* — `ProviderNotConfigured` e
   `AgentNotFound` não estão nele, e quando os motivos aparecerem na tela a frase
   vizinha ficará enumerando um subconjunto. Convenção 13, na forma "verdadeira
   quando escrita".
3. **A recusa não carrega razão nenhuma para o cliente A2A** (D10).
4. **`apps/inbox`: 3 casos de `DebounceSweepServiceTests` reprovam na suíte cheia
   e a classe passa isolada** — e os três **não** são o mesmo achado. Medido hoje:
   suíte cheia `203` com 3 reprovando em 1m05s; a classe sozinha **10/10 em 7s**.

   | caso | assinatura | é novo? |
   |---|---|---|
   | `InfrastructureFailure_DuringCandidateQuery_…` | `TimeoutException : Condição não satisfeita a tempo`, do `PollUntil` da própria classe | **não** — é exatamente o flake já registrado no `02`, com a assinatura que ele nomeia |
   | `InfrastructureFailure_ProcessingOneCandidate_…` | idem | **não** |
   | `MessagesWithinWindow_TriggerSingleSendMessage_…` | `Assert.Single() Failure: The collection contained 2 items` — **dois** `SendMessageRequest` onde o caso afirma um | **sim** — caso diferente, assinatura diferente, não está no registro |

   **Então a assinatura antiga não mudou: uma nova apareceu ao lado dela.** Quem
   comparar só a última mensagem com o registro antigo vai ver `Assert.Single`
   onde o registro diz `TimeoutException` e concluir que é outro defeito — ou que
   o antigo sumiu. Não é nem um nem outro: os dois estão na mesma execução, na
   mesma classe, sob contenção.

   **E o registro do `02` já dá a leitura das duas primeiras e ela se confirma:**
   *"a suíte de `apps/inbox` é derrubada pelo resíduo da suíte pesada
   imediatamente anterior"* — aqui a anterior foi `apps/frontend`, e a classe
   sozinha passa. O que o registro **não** cobre é a terceira: um *timeout* sob
   carga é atraso; **um despacho a mais não é atraso**, e se a varredura despachar
   duas vezes porque a janela de debounce venceu duas vezes, isso é forma de
   defeito de produção, não artefato de teste.

   **Não está classificado**, e não pode ser: nem pré-existente nem ambiental
   (convenção 19), e **"passou isolado" não discrimina nada aqui** — a classe
   inteira passa isolada, inclusive as duas já conhecidas. A discriminação exige
   rodar sob **as duas condições de carga**, repetidas: suíte cheia N vezes para
   ver a frequência da terceira, e a classe sozinha N vezes para ver se ela
   aparece descarregada. O gatilho do item antigo continua valendo — *a próxima
   change que tocar `apps/inbox`* —, e **esta não toca**.
5. **A régua de contenção de `apps/api` estava 9 classes abaixo do real**, com o
   gatilho da 32ª já vencido. Recalibrada nesta change (acima); o item de método
   que faltava decidir — limiar por duração ou `ICollectionFixture` — continua
   aberto.

## Risks / Trade-offs

- **A chave `rejection` não é posta no ambiente de um deploy** → a checagem de boot
  reprova a inicialização nomeando o regime (D7). Era o risco que não tinha
  contraparte verificável; agora tem, e o cenário está na spec (convenção 10).
- **O instante do regime é um valor de UM ambiente num arquivo versionado**, o
  mesmo risco já aberto no `02` para `execution` e `embedding`. Esta change
  **não** o fecha, e a checagem de boot não o cobre: chave presente com valor
  errado passa. Fica no item de fila que já existe, agora com três regimes em
  vez de dois.
- **`RejectedAt` não é o mesmo instante de `a2a_tasks.status_timestamp`** (relógios
  diferentes, microssegundos de diferença). Consequência: uma recusa exatamente na
  borda da janela pode entrar num lado e não no outro. Aceito, e é o mesmo
  trade-off de `task_executions.StartedAt` contra o carimbo do protocolo.
- **Escrita a mais no caminho de recusa** — um `INSERT` por recusa. A recusa é o
  caminho raro e já faz uma leitura de banco; o `INSERT` acontece **depois** de a
  resposta do protocolo estar decidida.
- **A soma dos motivos fecha com a contagem só dentro do regime da recusa.** Fora
  dele não há linha, e é o recorte de regime que diz isso — não um `caveat`. Se a
  tela algum dia apresentar as duas coisas na mesma janela sem o recorte, a soma
  não fecha. É por isso que o recorte é do servidor, e a spec já proíbe o cliente
  de reconstruí-lo.

## Migration Plan

1. Migração `AddTaskRejections` em `apps/api` (a única que o `migrator` do compose
   empacota). **Aditiva**: cria tabela, não toca nenhuma existente, não reescreve
   nada — sem `ACCESS EXCLUSIVE` prolongado, diferente do caso de coluna gerada já
   registrado.
2. `appsettings.json` ganha `Metrics:Regimes:rejection`. **O valor é o instante do
   deploy desta coleta**, e é tarefa de quem faz o deploy — não o instante do
   merge.
3. Sem janela de indisponibilidade e sem backfill: recusa anterior à migração não
   tem motivo em lugar nenhum, e o recorte de regime é o que diz isso em vez de
   inventar um valor `Unknown` retroativo.
4. **Rollback:** o `Down` remove a tabela. A resposta volta a não ter os campos
   novos — e é aqui que a ordem importa: se o deploy da API nova ficar de pé com a
   tabela removida, as consultas novas falham. Rollback é dos dois juntos, na ordem
   inversa.

## Perguntas fechadas pelo dono (25/09/2026)

As três nasceram como Open Questions desta change e foram respondidas antes de
qualquer código. Ficam aqui com a resposta e o motivo, porque é a resposta que a
implementação segue — não a pergunta.

1. **Quatro valores de vocabulário, não três.** Três forçaria `AgentInactive`
   sobre agente que não existe; o `default` do `record struct` é ausência de
   leitura, não inatividade. A alternativa recusada e o que ela custava estão na
   **D4**.
2. **`rejectedAtEntryCount` mantém o nome.** Ele diz o que conta e se separa do
   `rejectedCount` existente, que é o par que confunde hoje. Registrado na **D5**.
3. **A 41ª classe de contêiner não bloqueia** — e a régua foi recalibrada de 31
   para 40 **com o que o número serve para ler**, mais os dois candidatos que a
   tornariam acionável e o gatilho de reabertura. Está na seção da projeção.

**Nenhuma pergunta aberta resta nesta change.** O que sobra é fila com issue
própria, na seção de achados — e nenhum deles precisa de resposta para a
implementação começar.
