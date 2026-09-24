## Context

Correção de defeito em `apps/api`, nascida da issue **#65** e bloqueando a
**#52**. O diagnóstico veio de fora: a proposta da etapa 4 leu o contrato para
**consumi-lo**, e foi aí que a metade que falta apareceu.

### O defeito, e o que já estava conferido antes desta change

A spec de `system-insights-aggregation` (linhas 232-234) exige as duas metades:

> *"A série diária SHALL omitir — nunca emitir `0` para — os dias anteriores ao
> início do regime a que a métrica pertence. **Dentro do regime, um dia sem
> ocorrência SHALL emitir `0`.**"*

A consulta de M10 é `group by` sobre as linhas existentes, sem
`generate_series`. Dia medido e vazio some junto com dia não medido.

**Medido contra a rota real** em 23/09/2026 22:22 -03, com `apps/api` local
contra o banco de dev: janela de 24/08 a 23/09, `regimes.execution` em
22/09 01:21 -03 → `dailySeries: []`, `volume.executedTaskCount: 0`. Os 29 dias
anteriores ao regime e os 2 dias medidos e vazios saíram indistinguíveis.

### O que esta change conferiu, e o enunciado não previa

**1. `GET /insights/agents/{id}` tem consulta PRÓPRIA, e o mesmo defeito.**
`GetAgentInsightsQueryHandler.TemporalAsync` (linhas 162-214) tem o seu
`DailyRow`, o seu `WeekdayRow` e o seu `group by`, com o filtro por `AgentId`
acrescentado. Não é chamada compartilhada. O comentário de lá já dizia por quê:
*"não herdado do gêmeo: é consulta nova, e o verde de lá não cobre esta."*
**A correção precisa estar nos dois lugares, e os guardas também.**

**2. A spec do agente tem só a metade NEGATIVA.** `agent-insights-aggregation`
diz *"a série diária SHALL omitir — nunca emitir `0` para — os dias anteriores ao
início do regime"* e **para aí**. A metade positiva nunca foi escrita para aquele
escopo. Então, no escopo do agente, o comportamento de hoje **não é violação de
contrato — é contrato faltando**, e a correção precisa de delta que o crie.

**3. O seed dos guardas JÁ CONTÉM o caso que faltava.** `SeedAsync` insere
execuções nos dias locais **14/09** e **16/09**, com o regime começando em
**01/09**. O dia **15/09** é, desde sempre, um dia medido e vazio dentro do
regime. O guarda do par positivo podia ter sido escrito **sem uma linha de dado
novo**. É o fato mais incômodo do diagnóstico, e é o que decide a projeção da
convenção 18 (ver lá).

**4. `byWeekday` tem o defeito idêntico**, nos dois handlers, e **nenhuma das
duas specs o cobre** — nem a do sistema, nem a do agente.

### Por que os guardas da change A não pegaram

| guarda | afirma | deixa de fora |
|---|---|---|
| `DaysBeforeTheRegime_AreOmitted_NeverEmittedAsZero` | nenhum dia da série é anterior ao regime | **passa trivialmente com a série vazia** — `Assert.All` sobre lista vazia é verde |
| `MeasuredAndEmptyPeriod_ArrivesAsZero_NotAsNull` | o zero medido chega como número | afirma sobre **`volume.executedTaskCount`**, um escalar — **não** sobre `dailySeries` |

O segundo é o mais enganoso: o nome promete exatamente o caso que falta, e o
corpo mede outra coisa. Quem lesse a lista de guardas concluiria que o par estava
coberto.

### O refinamento que esta change acrescenta à régua *"o par não é universal"*

A régua diz: **requisito cujo oposto é inobservável não tem guarda negativo**.
Ela é sobre quando o par **não existe**.

Este caso é o inverso, e ela não o cobria: **o par existia, era exprimível, o
requisito o declarava em texto — e nenhum guarda foi escrito.** A metade negativa
ganhou guarda; a positiva não. E porque a negativa passa por vacuidade sobre
série vazia, o conjunto ficou **verde sem nenhuma das duas metades funcionando**.

**O refinamento:** a ausência do par precisa ser **decidida e escrita**, não
omitida por esquecimento. Quando um requisito tem duas metades, ou as duas têm
cenário, ou o `design.md` diz qual não tem e por quê. Vai para o `02` como item
da série, e para a #68 junto da documentação do board.

## Goals / Non-Goals

**Goals:**

- A série diária emite `0` para todo dia medido e vazio, **nos dois escopos**, e
  continua omitindo o dia anterior ao regime.
- O mapa por dia da semana ganha o mesmo tratamento, com a semântica escrita.
- O dia de pico deixa de ser eleito quando não houve medição — regressão que a
  própria correção introduziria.
- Os limites do intervalo gerado decididos **com o motivo de cada um**, incluindo
  o superior, que o enunciado não previa.
- O par positivo ganha guarda, e o negativo deixa de passar por vacuidade.
- As duas specs passam a declarar o que o código faz.

**Non-Goals:**

- **Nada em `apps/frontend`** — é a #52, que esta desbloqueia. A D2 de lá já está
  escrita contra o comportamento **corrigido**, e a tarefa 0.1 dela confere esta.
- **Nenhuma outra métrica.** Volume, tokens, desempenho, erros e delegação não
  mudam. Só `temporal`.
- **O mapa de regimes não muda** — nem a fonte, nem a forma, nem o clamp de
  `Later()`.
- **As cinco lacunas do protótipo** (#51, #66, #67) não são tocadas.
- **Nenhuma migração, nenhum índice, nenhum `nginx.conf`, nenhum pacote novo.**
- **Não extrair a consulta comum aos dois handlers** — ver D6.
- Não converter as demais consultas para `generate_series`: só as duas temporais
  têm domínio denso a preencher.

## Decisions

### D1 — A correção entra nos **dois** handlers, porque são duas consultas

Conferido no arquivo, não suposto: `GetAgentInsightsQueryHandler` tem o seu
próprio `DailyRow`/`WeekdayRow` e o seu próprio `group by`. Corrigir só o do
sistema deixaria a rota do agente com o defeito **e** com a aparência de
corrigida, já que as duas devolvem `TemporalInsightsResponse`.

**E os guardas também são dois conjuntos.** O comentário do código do agente já
registra a regra — *"o verde de lá não cobre esta"* —, e ela vale igual aqui: um
guarda do par positivo só na suíte do sistema deixaria a rota do agente sem
nenhum.

### D2 — `generate_series` sobre o dia local, e os dois limites com motivo

A série passa a nascer do **domínio de dias**, não das linhas existentes:

```sql
select d.day::date as "Day",
       count(distinct e."TaskId")::int as "TaskCount",
       sum(coalesce(pc."InputTokens", 0) + coalesce(pc."OutputTokens", 0))
           filter (where pc."InputTokens" is not null or pc."OutputTokens" is not null)::bigint
           as "TokenCount"
from generate_series(
         ({executionFrom} at time zone {timeZone})::date,
         ({effectiveTo}   at time zone {timeZone})::date,
         interval '1 day') as d(day)
left join task_executions e
       on (e."StartedAt" at time zone {timeZone})::date = d.day::date
      and e."StartedAt" >= {executionFrom} and e."StartedAt" <= {effectiveTo}
left join provider_calls pc on pc."TaskId" = e."TaskId"
group by 1
order by 1
```

**O limite inferior é `executionFrom`, que já é o recorte do regime.** A função
`Later()` devolve o mais tarde entre o `from` pedido e o início do regime, e é
ela que garante a primeira metade: **gerar dias antes do regime reintroduziria
exatamente o defeito que a metade negativa proíbe**, e seria pior que o de hoje,
porque emitiria `0` em vez de omitir.

**O dia do limite inferior entra inteiro.** O regime pode começar às 01:21, e o
dia local dele é gerado. É coerente com a decisão já tomada na proposta da #52:
hachurar um dia que tem medições reais as esconderia, e um quarto estado visual
para "parcialmente medido" não existe no protótipo. A parcialidade é declarada
pelo próprio `regimes`, que o cliente já recebe.

**`TokenCount` continua nulo no dia medido e vazio**, e isso é requisito, não
efeito colateral: `sum(...) filter (...)` sobre nenhuma linha devolve `NULL`. O
dia diz *"foram zero tasks"* e *"não há token a relatar"* ao mesmo tempo — as
duas coisas são verdade, e são coisas diferentes. O `0` novo é de `TaskCount`,
que é contagem medida.

**Alternativa recusada:** materializar os dias em C# e completar a série depois
da consulta. Recusada pelo mesmo motivo da D5 da change A — toda agregação roda
no banco, e completar em memória é o primeiro passo para o cliente do servidor
virar o servidor do cliente.

### D3 — O limite **superior** é recortado pelo instante atual, e isso não estava no enunciado

O enunciado da issue diz que o intervalo *"termina no `to` pedido"*. **Seguir isso
à letra cria o defeito simétrico:** com `to` no futuro — e a rota aceita, porque
não valida teto —, a correção emitiria `0` para dias que **ainda não
aconteceram**, afirmando medição sobre o futuro.

É a mesma falha que a metade negativa proíbe, virada para o outro lado: lá,
afirmar medição sobre o passado anterior à coleta; aqui, sobre o futuro.

**Decidido:** o limite superior é o **mais cedo** entre o `to` pedido e o
instante atual.

**A fonte do "agora" é `TimeProvider`, injetado**, e não `now()` no SQL. A
alternativa foi explicitamente recusada na D3 da `rotas-de-agregacao-sistema`:
*"uma janela relativa cujo 'agora' nasce no banco não é verificável de forma
determinística — não há como afirmar em teste onde a janela termina."* Os guardas
desta change dependem de saber onde ela termina, e a suíte já registra um
`FakeTimeProvider` com instante fixo em `2026-09-23T12:00:00Z`.

**Custo nomeado:** os dois handlers passam a depender de `TimeProvider`. Ele já
está registrado em `apps/api` desde a change A, então não entra registro novo —
entra um parâmetro de construtor em cada handler.

**Esta decisão contraria a letra do enunciado da #65** e preserva a razão dele
(convenção 9). Registrada aqui, datada, e o comentário do código aponta para cá.

### D4 — `byWeekday` **entra nesta change**, e a semântica da omissão é escrita

O enunciado deixou em aberto entre entrar aqui ou ter issue própria, com o
critério certo: *se a semântica for diferente, merece separação*.

**A semântica é a mesma**, e é isto:

| situação | o que a rota devolve |
|---|---|
| o dia da semana **ocorre** na faixa medida e teve task | a contagem |
| o dia da semana **ocorre** na faixa medida e não teve task | **`0`** |
| o dia da semana **não ocorre** na faixa medida | **ausente** |

A ausência significa a mesma coisa que na série diária — *"não há medição para
este balde"* —, e o `0` significa a mesma coisa — *"mediu e deu zero"*. É um
domínio de sete valores em vez de um domínio de dias, e nada mais.

**Três razões para entrar aqui:**

1. **A correção é a mesma máquina.** O `generate_series` que a série diária
   precisa já produz o conjunto de dias medidos; o dia da semana sai de um
   `extract(dow)` sobre ele. Fazer só metade seria escrever a máquina e usá-la
   uma vez.
2. **Corrigir só metade deixa a tela com dois comportamentos para a mesma
   pergunta.** A proposta da #52 registrou que o cliente ia reconstruir a
   cobertura de dia da semana porque a rota não a dava; essa reconstrução é
   exatamente o que esta change existe para eliminar.
3. **O caso não é hipotético — é o do piloto agora.** O regime de execução
   começou em 22/09. Uma faixa medida de dois ou três dias cobre dois ou três
   dias da semana; os outros quatro ou cinco **nunca foram medidos**, e hoje
   chegam indistinguíveis de dias da semana sem atividade.

**Uma armadilha da forma, que o guarda precisa pegar:** a consulta de hoje usa
`count(*)` sobre `task_executions` direto. Com o `left join` ao domínio de dias,
`count(*)` conta **1** para o dia sem correspondência — todo dia da semana vazio
chegaria com `1`. Tem de virar `count(e."TaskId")`, que ignora nulo. É o tipo de
erro que sai plausível: números pequenos, todos positivos, nenhum sintoma.

**Delta de spec é obrigatório**, e em **ambas** as capabilities: hoje nenhuma das
duas diz nada sobre `byWeekday`, e comportamento não especificado não tem guarda
que o prenda depois do archive — que é a lição desta change inteira.

### D5 — O pico deixa de ser eleito quando o máximo é `0`

**Esta é uma regressão que a própria correção introduziria**, e por isso está
aqui como decisão e não como detalhe.

O código de hoje:

```csharp
DayOfWeek? peak = byWeekday.Count == 0
    ? null
    : byWeekday.MaxBy(point => point.TaskCount)!.Weekday;
```

Hoje `byWeekday` só tem linhas de dias que tiveram task, então `Count == 0`
equivale a "não houve medição" e o `null` sai certo. **Depois da D4, uma faixa
medida sem nenhuma task devolve sete linhas com `0`** — e o `MaxBy` elegeria
**domingo** como dia de pico de um período em que nada aconteceu.

O comentário do próprio código diz por que isso é proibido: *"sem medição não há
pico, e inventar 'domingo' seria afirmar o que não se sabe"*. A correção o
tornaria mentira.

**Decidido:** o pico é nulo quando a lista está vazia **ou** quando a maior
contagem é `0`. As duas condições, não uma — a primeira cobre faixa medida vazia,
a segunda cobre faixa medida sem task.

**Guarda próprio**, e ele reprova contra a implementação ingênua da D4. É o
guarda que só existe porque a correção o tornou necessário, e vale registrar
isso: **a correção de um defeito de convenção 13 quase criou outro.**

### D6 — As duas cópias continuam **copiadas, não extraídas**

Tocar as duas ao mesmo tempo é a tentação óbvia de extrair um método comum. Não
entra.

A regra da casa é repetição **observada**, e ela está observada — são duas. Mas o
motivo pelo qual elas nasceram separadas continua valendo: **são consultas
diferentes**, não a mesma com um parâmetro. A do agente filtra por `AgentId` em
dois pontos, e a do sistema não tem o conceito. Uma extração faria um método com
`Guid?` opcional e dois caminhos internos — que é a forma que esconde a
diferença em vez de compartilhar a semelhança.

**O que esta change faz em vez disso:** o comentário de cada cópia aponta a
outra, como já fazem as entidades de métrica duplicadas entre `apps/api` e
`apps/workers`, e cada uma ganha o guarda que a cobre. **Gatilho para
reconsiderar:** um terceiro escopo temporal.

### D7 — Os dois deltas são diferentes em natureza, e o registro diz por quê

| capability | o que falta hoje | operação |
|---|---|---|
| `system-insights-aggregation` | o texto tem as duas metades; falta o **cenário** da positiva, e falta `byWeekday` e o pico | `MODIFIED` do requisito existente |
| `agent-insights-aggregation` | o texto tem **só a metade negativa**; a positiva nunca foi escrita | `MODIFIED` do requisito existente |

**A distinção importa para quem ler depois.** No escopo do sistema, o código
violava um contrato que existia. No escopo do agente, **não havia contrato a
violar** — e é por isso que ali não bastava corrigir o código: sem o texto, a
próxima leitura não teria como saber que o `0` é obrigatório.

Nenhuma capability nova, nenhum requisito removido. Nenhum campo de resposta
muda de forma: `DailyInsightPoint` e `WeekdayInsightPoint` são os mesmos records,
e o que muda é **quantas linhas** chegam. Nenhum consumidor quebra.

### D8 — Os guardas, e por que o negativo precisa de precondição afirmada

**O par positivo é o guarda central**, e ele **reprova contra `HEAD`**
(convenção 15): janela inteiramente dentro do regime, contendo um dia sem
ocorrência, e a série traz esse dia com `TaskCount: 0`. O seed já serve — 15/09
está entre 14/09 e 16/09 e o regime começa em 01/09.

**O negativo deixa de passar por vacuidade.** `Assert.All` sobre lista vazia é
verde, e foi assim que o guarda de hoje sobreviveu a uma implementação que não
emitia nada. Ele passa a afirmar a precondição — a série **não** está vazia, e
contém os dias esperados — antes de afirmar que nenhum dia é anterior ao regime.
É a convenção 5, e o diagnóstico desta change é a evidência de que sem ela o
guarda não discrimina.

**O par positivo do par negativo**: dia anterior ao regime continua **omitido**,
não zerado. A correção não pode gerar dias antes do início, e este guarda é o que
prende o limite inferior da D2.

**Mutação**, no molde das três últimas changes: quebrar o limite inferior do
intervalo gerado — trocar `executionFrom` por `query.From` — e conferir que
**só** os guardas do regime reprovam, nos dois escopos. Se algum outro reprovar,
ou se nenhum reprovar, o conjunto não está discriminando o que diz discriminar.

**Uma segunda mutação, pela D5:** remover a condição do máximo zero e conferir
que **só** o guarda do pico reprova.

### D9 — O que **não** muda, e está escrito para ninguém "consertar" depois

- **O clamp de regime (`Later`) fica como está.** Ele já faz a metade negativa,
  e é ele que esta change usa como limite inferior.
- **`TokenCount` continua anulável e continua nulo** no dia medido e vazio. Quem
  vier depois e achar que "o dia tem `0` task, então tem `0` token" está
  colapsando duas perguntas diferentes.
- **`MetricsOptions` não muda** — nem o fuso, nem o mapa de regimes.
- **Nenhum índice.** O `generate_series` produz no máximo os dias da janela; o
  `left join` cai no mesmo filtro de range que já usa índice comum. A change A
  mediu isso e a conclusão não muda com um domínio de dias do lado de fora.

## Árvore de arquivos afetados

```
apps/api/
├── src/Buteco.Api/Insights/
│   ├── Queries/GetSystemInsights/GetSystemInsightsQueryHandler.cs   (modificado: TemporalAsync + TimeProvider)
│   ├── Queries/GetAgentInsights/GetAgentInsightsQueryHandler.cs     (modificado: TemporalAsync + TimeProvider)
│   └── Responses/SystemInsightsResponse.cs                          (modificado: XML doc de DailyInsightPoint)
└── tests/Buteco.Api.Tests/
    ├── InsightsEndpointsTests.cs        (modificado: guardas novos + precondição no negativo)
    └── AgentInsightsEndpointsTests.cs   (modificado: os mesmos, para a consulta própria)
```

**Nada em `libs/`** — é correção de consulta dentro de um app.

## Projeção da convenção 18 — décima sétima medição

**Unidade declarada antes:** `#### Scenario:` contados nos arquivos de delta desta
change. Modificado por `git diff -w`. Três níveis separados: escrito à mão,
**duplo**, gerado — e aqui **gerado deve ser 0**.

**Âncoras decompostas** — as duas changes que escreveram este código, que é a
melhor âncora possível porque é o mesmo arquivo e o mesmo autor de estilo:

| âncora | cenários | fonte (`apps/api`) | teste | total | linhas/cenário |
|---|---|---|---|---|---|
| `rotas-de-agregacao-sistema` | 24 | 10 arq, 1.150+ | 5 arq, 681+ | 1.831+ | 76 |
| `rotas-de-agregacao-agente` | 19 | 4 arq, 948+ | 2 arq, 597+ | 1.545+ | 81 |

**A razão das âncoras vai errar para cima, e o motivo é o achado do diagnóstico.**
As duas criaram rotas inteiras: cada cenário vinha acompanhado de consulta nova,
record novo e fixture nova. Esta change **modifica duas consultas e não cria
nenhum dado de teste** — o seed já tem o dia-buraco, o `FakeTimeProvider` já tem
instante fixo, os dois arquivos de guarda já existem. Herdar 78 linhas/cenário
projetaria o dobro do que esta change pode custar.

**Projeção por componente:**

| componente | arquivos | linhas |
|---|---|---|
| `TemporalAsync` do sistema (consulta diária, dia da semana, pico, `TimeProvider`) | 1 | 90 |
| `TemporalAsync` do agente (o mesmo, com o filtro e o comentário que aponta o gêmeo) | 1 | 80 |
| XML doc de `DailyInsightPoint` | 1 | 10 |
| **fonte, subtotal** | **3** | **180** |
| guardas do escopo do sistema (5 novos + 1 reforçado) | 1 | 165 |
| guardas do escopo do agente (os mesmos 6) | 1 | 155 |
| **duplo** — dado de teste novo | **0** | **0** |
| **teste, subtotal** | **2** | **320** |
| **gerado** | **0** | **0** |
| **total `apps/api`** | **5 arquivos** | **≈500 linhas** |

**Faixa: 400 a 650 linhas.**

**O duplo é zero, e é a linha mais informativa da tabela.** A décima segunda
medição mostrou que a infraestrutura de duplo é o que estoura a projeção quando
não tem item próprio — ali foram 304 linhas de `IChatClient` com estado. Aqui o
item existe e vale **zero**, porque o dado do caso que faltava **já estava no
seed desde a change A**. O guarda ausente não custava dado nenhum; custava
alguém ter escrito o par.

**Cenários projetados: ≈12** — ≈6 no delta de `system-insights-aggregation` e
≈6 no de `agent-insights-aggregation`.

**Projeção de registro, que é a que decide o tamanho em change pequena:**
`proposal.md` ≈ 90 linhas, `design.md` ≈ 400, os dois deltas ≈ 180, `tasks.md`
≈ 130 → **≈800 linhas de `openspec/`, contra ≈500 de código.** O registro é o
maior lado desta change, e isso é o esperado: o custo dela não está em escrever
`generate_series`, está em deixar escrito por que o par faltou e o que impede o
próximo.

**O fechamento só compara.** Nenhum número desta seção é revisado durante o apply.

## Baselines — remedidas, não herdadas

Medidas nesta máquina em 24/09/2026, sobre `0b189fa` (`fix/65-serie-diaria-dia-medido-vazio`
ainda sem commit), **uma suíte por vez**:

| suíte | resultado | duração |
|---|---|---|
| `apps/api` | **391/391** | 1m29s |
| `apps/inbox` | **203/203** | 18s |
| `apps/workers` | **386/386** | 6m16s |
| `apps/frontend` | **927/927**, 84 arquivos | 1m14s |

**Estado do `podman ps` no momento da medição**, declarado em vez de afirmado
como zero: três containers de pé — `buteco-agents_postgres_1`,
`buteco-agents_rabbitmq_1` e `buteco-agents_waha_1`. As suítes de backend rodaram
com `DOCKER_HOST` do Podman e `TESTCONTAINERS_RYUK_DISABLED=true`.

**As quatro bateram com os últimos registrados** (`apps/api` 391,
`apps/workers` 386, `apps/inbox` 203, `apps/frontend` 927) — e baterem **não**
dispensa a medição, que é o ponto da convenção 22.

**O flake de `apps/inbox` está caracterizado, e esta rodada o confirma.** Na
medição de 23/09, com quatro suítes disputando a máquina,
`DebounceSweepServiceTests.InfrastructureFailure_DuringCandidateQuery_IsLoggedAndSweepRecoversNextCycle`
estourou o `PollUntil` por `TimeoutException` e a suíte fechou em 202/203.
Rodando **sozinha**, hoje e ontem, fecha em 203/203. É contenção, não defeito —
e é por isso que as baselines desta change foram medidas uma por vez.

## Risks / Trade-offs

| risco | mitigação |
|---|---|
| **A correção introduz o pico falso** (D5) — sete linhas de `0` e o `MaxBy` elegendo domingo. É o risco mais provável, porque a implementação ingênua o produz. | Guarda próprio, que reprova contra a D4 sem a D5, mais a mutação que remove a condição e confere que **só** ele reprova. |
| **`count(*)` vira `1` no dia da semana vazio** com o `left join` (D4). Sai plausível: números pequenos, todos positivos, nenhum sintoma. | `count(e."TaskId")`, e o guarda do dia da semana coberto e vazio afirma **`0`**, não "algum número". |
| **`to` no futuro emite `0` para dias que não aconteceram** (D3) — o defeito simétrico, criado pela própria correção. | Recorte por `TimeProvider`, com guarda que pede janela terminando depois do instante fixo da fixture e afirma que a série para no dia de hoje. |
| **Corrigir só um dos dois handlers**, deixando a rota do agente com o defeito e aparência de corrigida. | D1: a correção e os guardas entram nos dois, e a lista fechada do `tasks.md` nomeia os dois arquivos. |
| **A #52 foi escrita contra o comportamento corrigido** e não contra o de hoje. Se esta change não fechar antes, a tela implementa contra uma rota que não cumpre o que a D2 de lá assume. | A tarefa **0.1** da #52 é conferir esta change antes de qualquer código, e a #65 está registrada como `blocked-by` dela no GitHub. |
| **O volume da série cresce**: uma janela de 90 dias passa de N pontos para até 90. | É o contrato que a spec sempre exigiu. 90 objetos de três campos não é volume; e a alternativa é o cliente não conseguir distinguir dois estados, que é o custo que esta change existe para não pagar. |
| **O `generate_series` sobre janela muito longa** com `to` distante no futuro geraria muitos dias. | A D3 recorta pelo instante atual, então o domínio nunca passa do hoje. O teto de intervalo continua sendo o item aberto da change A, com o gatilho de 2 s já disparado e registrado. |

## Open Questions

Nenhuma incerteza de negócio em aberto.

Duas decisões desta change **contrariam ou estendem o enunciado da #65**, e as
duas estão registradas com a causa, reversíveis por decisão do dono sem
retrabalho de estrutura: a **D3** (o limite superior recortado pelo instante
atual, onde o enunciado dizia `to` pedido) e a **D4** (`byWeekday` entra aqui, em
vez de issue própria).
