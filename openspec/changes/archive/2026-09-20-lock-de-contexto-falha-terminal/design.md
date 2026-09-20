## Context

`AgentExecutionService.ExecuteAsync` tem hoje esta forma:

```
140   task = await ApplyStepAsync(…, updater => updater.StartWorkAsync(…), …)
      ↑ TRANSIÇÃO PARA `working`, JÁ PERSISTIDA (ApplyStepAsync → SaveTaskAsync),
        e ANTERIOR ao lock — é o que fixa o estado de partida da falha (D7)
148   if (task is null) { … return; }                 ← task garantidamente não-nula daqui pra frente
158   await using var contextLock = await ConversationContextLock.AcquireAsync(…)   ← FORA do try
161   try {
          … LoadSession → RunAsync → SerializeSession → ApplyStep(Complete) → push …
301   } catch (Exception ex) {
          … LogError → ApplyStep(Fail) → push …
      }
      ↑ o await using dispara o DisposeAsync AQUI, no fim do método, DEPOIS do catch
```

O `catch` da 301 cobre tudo que acontece **dentro** do `try`. A aquisição do
lock e o descarte dele estão os dois **fora**, e é daí que sai o defeito: uma
exceção em `AcquireAsync` escapa de `ExecuteAsync` inteiro, chega ao `catch` de
`TaskJobConsumer.cs:57-61`, que loga e faz `BasicNackAsync(requeue: false)` — a
task fica em `working`, a mensagem é descartada, e nada mais acontece.

**O que foi medido na exploração `replicas-de-worker` e sustenta este desenho**
(Postgres `pgvector/pgvector:pg18` e RabbitMQ `4.3-management` reais, convenção 6):

| # | Fato | Como |
|---|---|---|
| M1 | `pg_advisory_lock` é lock de **sessão**, distribuído por construção, liberado por morte do backend | sonda `Probe_AdvisoryLockSemantics`, `[1][2][3]` |
| M2 | O Npgsql **não** libera advisory lock ao devolver a conexão ao pool | mesma sonda, `[4] True` / `[5] False` |
| M3 | A aquisição é limitada pelo `CommandTimeout` do Npgsql (30 s default) e lança `NpgsqlException` ⊃ `TimeoutException` | `Probe_LockAcquisitionTimeout` |
| M4 | Quando isso acontece, a task fica em `Working` indefinidamente | mesma sonda, 45 s de observação |
| M5 | O caminho é alcançável com 2 instâncias e 2 tasks do mesmo `(agentId, contextId)`, sem nada artificial | `Probe_SameContextSlowFirst` |
| M6 | `Command Timeout=N` na connection string limita a espera do advisory lock e produz **a mesma exceção**, em 3,2 s com `N=3` | sonda de design desta change |
| M7 | Derrubar o backend dono do lock, localizado pelo próprio par de chaves em `pg_locks`, acha exatamente 1 e faz o `DisposeAsync` lançar `PostgresException` | sonda de design desta change (D8) |
| M8 | Com `Maximum Pool Size=3`, 5 aquisições que falham **sem** descartar o escopo deixam o processo incapaz de qualquer trabalho no banco; **com** o descarte, o trabalho seguinte passa | sonda de design desta change (D9) |
| M9 | Contar `pg_stat_activity` **não** discrimina o vazamento sozinho — conexão devolvida ao pool também é backend vivo | mesma sonda de M8 |

M6 é o que decide a forma dos guardas: dá para exercitar o mecanismo real em
segundos, sem knob de produção e sem tocar a assinatura de nada. M7, M8 e M9 são o
que torna os dois guardas mais caros viáveis — ver D8 e D9.

### Árvore de pastas afetada

Nenhuma pasta nova. Nenhum arquivo de produção criado — só modificados.

```
apps/workers/
├── src/Buteco.Workers/
│   └── Agents/
│       ├── AgentExecutionService.cs          (M) tratamento próprio da aquisição
│       └── ConversationContextLock.cs        (M) descarte na falha + 2 registros de mecanismo
└── tests/Buteco.Workers.Tests/
    └── ConversationContextLockFailureTests.cs (C) os quatro guardas, classe nova
```

`(M)` modificado, `(C)` criado. Nada em `libs/` — esta change não acrescenta
nada compartilhado, e não haveria o que justificar lá: o comportamento é
específico de `apps/workers` e os dois apps não têm `ProjectReference` cruzado.

## Goals / Non-Goals

**Goals:**

- Toda task consumida de `agent-tasks` que transicionou para `working` alcança
  um estado terminal, inclusive quando a serialização por `(agentId, contextId)`
  não pode ser adquirida.
- O caminho de falha da aquisição não vaza recurso — porque esta change o
  transforma de acidente raro em caminho de operação.
- Os dois mecanismos não-óbvios do `ConversationContextLock` (M2 e colisão de
  `hashtext`) ficam escritos onde quem investiga abre primeiro.
- Os guardas reprovam contra o defeito antes da correção existir (convenção 15).

**Non-Goals:**

- Escolher um valor para o limite de espera do lock, ou torná-lo configurável em
  produção (D2).
- Número de instâncias, compose, documentação de deploy.
- Ciclo `A→B→A` (change própria, a seguinte).
- Instrumentação/diagnóstico de delegação, métricas.
- Varredura de `PendingDispatch` órfã em `Dispatching` (D5).

## Decisions

### D1 — Tratamento próprio para a aquisição, e não mover o `await using` para dentro do `try`

A correção óbvia — empurrar o `await using` para dentro do `try` existente, e
deixar o `catch` da 301 cobrir tudo — **está errada, e por um motivo concreto**.

`await using` dentro de um bloco dispara o `DisposeAsync` ao sair do bloco, e
numa exceção o `finally` implícito roda **antes** do `catch` externo. Ou seja: o
`DisposeAsync` de `ConversationContextLock` passaria a ser coberto por aquele
`catch`. E o `DisposeAsync` pode lançar — ele executa
`pg_advisory_unlock` numa conexão que pode ter morrido
(`ConversationContextLock.cs:61-62`).

O cenário de falha é este, e é pior do que o defeito corrigido: o trabalho
termina com sucesso, `ApplyStepAsync` grava a task como `Completed`, o
`DisposeAsync` lança ao soltar o lock, o `catch` pega — e chama
`ApplyStepAsync` com `FailAsync`. `ApplyStepAsync` (`:503-530`) **não tem
guarda de estado terminal**: ele projeta e grava sem checar o estado corrente.
Uma task concluída com sucesso seria sobrescrita como `failed`, e a resposta do
agente perdida junto.

**Escolhido:** a aquisição ganha `try/catch` próprio, e o `await using` fica
exatamente onde está. Forma:

```
ConversationContextLock contextLock;
try
{
    contextLock = await ConversationContextLock.AcquireAsync(…);
}
catch (Exception ex)
{
    … LogError → ApplyStep(Fail) → push → return;
}

await using (contextLock)
{
    try { … } catch { … }        ← bloco existente, intocado
}
```

Custo: a sequência "loga, falha a task, notifica" aparece duas vezes. É
duplicação de três chamadas, e é o preço de não mudar o que o `catch` existente
cobre. A alternativa — extrair um helper `FailTaskAsync` usado pelos dois — é
aceitável e fica a critério da implementação; o que **não** é aceitável é
alargar o alcance do `catch` da 301.

**Alternativa recusada:** pôr guarda de estado terminal em `ApplyStepAsync` e
então mover o `await using` para dentro. Resolveria o mesmo problema e mais
alguns, mas muda o comportamento de **todos** os caminhos que passam por
`ApplyStepAsync` (submit, working, reject, complete, fail), incluindo os de
`AgentDelegationToolSetResolver`. É change própria, com guarda própria, e não
carona nesta.

### D2 — O limite de espera continua sendo o `CommandTimeout` default do Npgsql, e passa a estar escrito

Não entra constante nova nem `IOptions<T>` de produção.

O motivo não é economia: é que **não há como escolher o valor**. Um limite curto
faz falhar uma mensagem que só precisava esperar a anterior terminar — e uma
conversa legitimamente segura o lock por minutos (LLM + tools + uma delegação
que sozinha pode consumir 120 s). Um limite longo não muda nada em relação aos
30 s atuais. Qual dos dois está certo depende de quanto tempo uma conversa
realmente segura o lock, que é exatamente a métrica de operação que **não
existe** (V6 da exploração, change própria). Afirmar um limite cujo mecanismo
não foi estabelecido é o defeito que esta change corrige; escolher um número
aqui seria repeti-lo na forma oposta (convenção 13).

O que muda: hoje **nada** no código diz que a aquisição é limitada. A correção
de D1 só é correta porque ela é — M3. Então o limite passa a estar escrito em
`ConversationContextLock`, nomeando o mecanismo (`CommandTimeout` do Npgsql,
30 s por default, configurável pela connection string) e o que depende dele.

**Consequência registrada:** quem puser `Command Timeout=0` na connection string
volta a ter espera infinita, e o defeito desta change volta em outra forma — a
task fica em `working` porque a aquisição nunca retorna, não porque a exceção
escapou. Fica no comentário, ao lado do mecanismo.

### D3 — `AcquireAsync` descarta o escopo quando a aquisição falha

Hoje:

```csharp
var scope = scopeFactory.CreateScope();
var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
await dbContext.Database.OpenConnectionAsync(cancellationToken);
await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_lock(…)");  ← lança aqui
return new ConversationContextLock(scope, dbContext, …);                              ← nunca chega
```

Se a linha do `pg_advisory_lock` lança, o `IServiceScope` — e com ele o
`AppDbContext` e a conexão Postgres **já aberta** — não são descartados por
ninguém: o objeto que os possui nunca foi construído, e não há `using` nesse
método.

**Por que entra nesta change e não é escopo alargado:** enquanto a falha de
aquisição era uma exceção não tratada que derrubava o processamento da
mensagem, o vazamento era raro e o operador tinha um erro visível. D1 transforma
essa falha em caminho de operação rotineiro — e um vazamento **por ocorrência**.
Com o `Maximum Pool Size` default de 100, cem ocorrências esgotam o pool e o
worker para de falar com o Postgres inteiro. Corrigir D1 sem isto trocaria uma
task presa por uma instância morta. É consequência direta da correção, e por
isso está aqui e não numa fila.

**Escolhido:** `try/catch` em `AcquireAsync` que descarta o escopo e relança.
A exceção original continua subindo sem alteração — D1 depende do tipo e da
mensagem dela para o log.

### D4 — Os guardas encurtam o limite pela connection string, não por código de produção

M6: `Command Timeout=N` na connection string do host de teste limita a espera do
`pg_advisory_lock` e produz **a mesma exceção** que a produção
(`NpgsqlException` ⊃ `TimeoutException`), em 3,2 s com `N=3`.

Isso é o que torna os guardas viáveis: o braço original da sonda observava 45 s
para ver um timeout de 30 s. Com a connection string, o mesmo mecanismo real
roda em segundos.

**E é o que evita a régua da convenção 18 desta superfície.** Recontado em
árvore limpa, **com o escopo colado no número** (convenção 22 — a mesma régua
já apareceu com três valores diferentes em três documentos):

| Medida | Valor | Escopo |
|---|---|---|
| Sítios que **instanciam** `AgentExecutionService` (`new` ou `AddSingleton<>`) | **13** | 12 em `apps/workers/tests/` + 1 em `tests/InboxOrchestratorRoundTrip.Tests/` |
| Definições privadas de `BuildHost` | **13** | `apps/workers/tests/` |
| Produção | 1 | `apps/workers/src/Buteco.Workers/Program.cs:80` |

Os dois "13" são conjuntos **diferentes** e coincidem por acaso: há
`BuildHost` que não instancia `AgentExecutionService`
(`Knowledge/EmbeddingIndexConsistencyTests.cs`) e sítio que instancia sem ter
`BuildHost` (`WorkerTests.cs`).

**Os dois números anteriores estavam errados, cada um por um motivo, e os dois
ficam registrados** porque vão coexistir com este documento: a exploração
`replicas-de-worker` contou **16** `BuildHost` — incluía a própria sonda
descartável, que já não existe; a primeira versão deste `design.md` contou
**15** — somava os 2 de `apps/inbox/tests/`, que é outro app e que uma mudança
no construtor de `AgentExecutionService` nunca tocaria. Nenhum dos dois era
"N"; os dois eram "N num escopo que não era o da afirmação".

Qualquer dependência nova no construtor de `AgentExecutionService` custa os 13
sítios de instanciação antes de custar uma linha de correção. O desenho
escolhido custa **zero** deles: a assinatura de `AgentExecutionService` e de
`ConversationContextLock.AcquireAsync` não mudam.

**Alternativa recusada:** resolver um `IOptions<ConversationContextLockOptions>`
do escopo dentro de `AcquireAsync` (que já resolve `AppDbContext` de lá, então
seria idiomático e também custaria zero dos 13). Recusada não pelo custo, mas
por D2 — não há valor a configurar, e o knob existiria só para o teste. O
precedente citado em `AgentDelegationToolOptions` ("existir como `IOptions<T>`
em vez de `const` serve só para permitir que os testes sobrescrevam") **não se
aplica**, porque ali não havia outro jeito de encurtar um deadlock de 120 s;
aqui há, e é M6.

### D5 — A varredura de `PendingDispatch` órfã NÃO entra aqui

Esta change remove o vazamento que **alimenta** aquele estado: com a task em
`failed`, a push notification dispara e `PushNotificationEndpoints` resolve e
remove o `PendingDispatch` pelo caminho que já existe. O estado residual
continua alcançável por outro mecanismo — a instância morrer entre o commit do
`MarkDispatching` e o `SendMessageAsync` (`DebounceSweepService.cs:100-153`) —, e
esse mecanismo é independente deste defeito.

**Fica de fora por três motivos, nesta ordem:**

1. É outro app (`apps/inbox`), outra capability (`inbox-message-orchestration`),
   outra fixture de Testcontainers. Juntar faria os guardas desta change
   atravessarem dois apps.
2. É outro mecanismo. A rodada da convenção 15 teria de reprovar contra **dois**
   defeitos diferentes, e o da varredura (morte de instância no meio do
   dispatch) não é reproduzido por nada que esta change construa.
3. A correção certa lá não é óbvia — varrer `Dispatching` por idade precisa de
   um limiar, e escolher esse limiar tem o mesmo problema de D2: depende de
   quanto tempo um dispatch legítimo demora, que ninguém mediu.

**Gatilho:** a primeira `PendingDispatch` observada em `Dispatching` por mais
tempo que o máximo plausível de execução de uma task, **depois** desta change
estar em produção — porque só então a observação isola o mecanismo residual, em
vez de reproduzir este defeito.
**Posição:** depois da change de instrumentação (V6 da exploração), que é o que
produz a observação do gatilho. Antes dela não há como distinguir uma
`PendingDispatch` presa por este defeito de uma presa por morte de instância, e
a varredura seria escrita contra um limiar inventado.

**Não coberto por nenhuma das duas:** as linhas **já presas** no piloto. São
dado, não código; esta change não as destrava, e não há migration aqui. Precisam
de inspeção manual quando a change subir — registrado nas tasks.

### D6 — Os dois registros de mecanismo vão para `ConversationContextLock`, não para o histórico

Nenhum dos dois é defeito a corrigir, e os dois somem se ficarem só no `02`:

1. **M2 — o Npgsql não solta advisory lock ao devolver a conexão ao pool.** O
   caminho feliz está coberto pelo unlock explícito de `DisposeAsync`, mas nada
   no arquivo dizia que o desenho **depende** disso. Sem o registro, uma
   refatoração que troque o unlock explícito por "fechar a conexão basta"
   parece equivalente e deixa o lock vivo até o backend morrer.
2. **Colisão de `hashtext`.** `pg_advisory_lock(int4, int4)` sobre
   `hashtext(agentId)`/`hashtext(contextId)`: duas conversas não relacionadas
   podem colidir e serializar entre si. Degradação silenciosa, nunca corrupção —
   e sem registro, uma serialização inexplicada entre conversas distintas não
   tem onde ser reconhecida.

Vão como comentário **no arquivo** e não no `02` porque é lá que quem investiga
qualquer um dos dois sintomas abre primeiro. O `02` recebe o resumo, não o
mecanismo.

### D7 — O estado de partida da falha é `working`, já persistido — conferido, não assumido

O bloco `Context` original deste documento começava na linha 148 e **omitia a
transição para `working`**, o que deixou ambíguo de que estado uma task parte
quando a aquisição falha. Se a transição fosse posterior ao lock, a task iria de
`submitted` direto para `failed`, e a frase "presa em `working`" — que está no
requisito e nos cenários — seria falsa.

**Conferido no código:** `ApplyStepAsync(… StartWorkAsync …)` está em
`AgentExecutionService.cs:140-146`, **antes** da aquisição na 158, e
`ApplyStepAsync` persiste via `SaveTaskAsync` antes de retornar. O comentário da
`:115` já dizia a ordem em palavras — *"checado antes de `StartWorkAsync`/do
lock consultivo"*.

Consequência: a task **está** em `working`, e gravada, quando a aquisição falha.
O cenário, a frase do requisito e a asserção da task 1.2 estavam corretos e
ficam como estão. O que muda é só o `Context` deste documento, que agora mostra
a transição — a omissão era do documento, não do desenho.

### D8 — Como forçar a falha na liberação do lock, sem sincronizar por tempo

O guarda que prende a alternativa recusada em D1 (task 1.4) precisa de
`pg_advisory_unlock` falhando **depois** de a task já ter sido gravada como
`completed`. "No instante certo" é sincronização, e sincronização por tempo é
como se escreve teste instável — esta base já registra a família de suíte
sensível a contenção externa (`WorkerHostCollection`).

**Há forma estável, e o ponto de sincronização é uma chamada, não um relógio.**

O `IChatClient` é resolvido e invocado **dentro** do `try`, e o
`ApplyStepAsync(Complete)` acontece **depois** do `RunAsync` retornar. Então o
mock do `IChatClient` é um ponto de execução com ordenação garantida pela
sequência de chamadas:

```
try {
    …
    RunAsync ──► mock do IChatClient ──► DERRUBA o backend do lock, aqui
    SerializeSessionAsync                 (escopo/conexão próprios, não afetados)
    ApplyStepAsync(Complete) ───────────► grava `completed` (PostgresTaskStore
}                                          abre o PRÓPRIO escopo — não usa a
↑ DisposeAsync ──► pg_advisory_unlock       conexão do lock)
                   na conexão morta ──► LANÇA
```

O backend a derrubar é localizado **pelo próprio par de chaves do lock**, sem
adivinhar pid:

```sql
SELECT pg_terminate_backend(l.pid) FROM pg_locks l
WHERE l.locktype = 'advisory'
  AND l.objsubid = 2                                        -- forma de DUAS chaves
  AND l.classid  = (hashtext(@agentId)::bigint  & 4294967295)
  AND l.objid    = (hashtext(@contextId)::bigint & 4294967295)
```

`objsubid` distingue as duas formas de lock consultivo, e **conferido contra o
Postgres, não assumido**: `pg_advisory_lock(int4, int4)` grava `objsubid = 2`
com uma chave em cada coluna; `pg_advisory_lock(bigint)` grava `objsubid = 1`
com as **metades do mesmo número** em `classid`/`objid`. O sistema só usa a
forma de duas chaves hoje, então o filtro não muda o resultado medido em M7 —
ele existe para o guarda continuar derrubando o backend certo se alguém
introduzir a outra forma depois. Não é precaução teórica: na conferência, um
lock de chave única qualquer apareceu com `classid = 2`, que um par de duas
chaves pode ter por coincidência.

**Medido (M7):** acha exatamente 1 backend, e o `DisposeAsync` seguinte lança
`PostgresException`. Nenhum `Task.Delay`, nenhuma janela, nenhuma dependência de
velocidade da máquina — se o mock rodou, o backend está morto antes de qualquer
coisa depois dele.

Duas propriedades de que o guarda depende, e as duas são do código atual:
`PostgresTaskStore` abre escopo próprio por operação (`:17-18, 29-30`), então
gravar `completed` **não** usa a conexão do lock; e o provider não tem
`EnableRetryOnFailure` (`ButecoNpgsqlOptionsExtensions.cs`), então a conexão
morta lança em vez de reconectar em silêncio. Se qualquer uma das duas mudar,
este guarda para de exercitar o que afirma — fica registrado aqui porque é onde
quem mexer nelas vai olhar.

### D9 — O guarda de vazamento discrimina por esgotamento de pool, com o teto baixado

A forma cara do guarda — mais de 100 aquisições que falham, porque o
`Maximum Pool Size` default é 100 — são cinco minutos de suíte com
`Command Timeout=3`. Recusada pelo custo, não pelo mecanismo.

**Recusada também a alternativa de contar `pg_stat_activity`**, e por medição,
não por preferência: **M9** — conexão devolvida ao pool é um backend vivo
exatamente como uma vazada, então a contagem não distingue as duas. Na sonda ela
só pareceu discriminar (3 contra 1) porque o pool estava capado em 3; o que
discriminava era o teto, não a contagem.

**Escolhido: manter o mecanismo do teto e baixar o teto pela connection string**
— `Maximum Pool Size=3` no host de teste, e 5 aquisições que falham. É o mesmo
lever de D4, já aceito, aplicado a outro parâmetro.

E a asserção fica mais forte do que "vazou", porque vira propriedade visível ao
usuário: sem o descarte de D3, o pool esgota e o worker **não consegue nem
gravar as próprias falhas** — `ApplyStepAsync(Fail)` também precisa de conexão.
As tasks ficam em `working`, que é o defeito original reaparecendo por outra
porta.

**Medido (M8)**, com as duas formas de `AcquireAsync` reproduzidas lado a lado:

| | backends vivos | trabalho legítimo depois das 5 falhas |
|---|---|---|
| sem descartar o escopo (como `HEAD`) | 3 (= o pool inteiro) | **falha** — pool esgotado |
| descartando o escopo (D3) | 1 | **passa** |

Custo: ~10 s, contra os ~5 min da forma recusada.

**Alternativa não escolhida, e por que fica registrada:** espionar o
`IServiceScopeFactory` e afirmar que o escopo criado foi descartado roda em
milissegundos e é mais próxima da correção. Fica como plano B se o guarda acima
se mostrar instável sob contenção do Podman — mas não é a primeira escolha
porque afirma a **implementação** (um escopo foi descartado) e não a
**consequência** (o processo continua funcionando), e é a consequência que
importa quando alguém reescrever `AcquireAsync` de outro jeito.

## Risks / Trade-offs

- **[A duplicação de "loga, falha, notifica" diverge do bloco original com o
  tempo]** → Os dois caminhos gravam o mesmo estado terminal e disparam a mesma
  notificação; um helper compartilhado resolve, e fica a critério da
  implementação. O guarda de D1 afirma o estado terminal, não a forma do código,
  então a divergência apareceria como teste vermelho e não como comportamento
  silencioso.

- **[Falhar a task é pior que esperar mais, para o usuário final]** → É, e é
  escolha consciente. Hoje o usuário não recebe nada e o sistema não sabe: a
  task fica em `working`, o `PendingDispatch` em `Dispatching`, e a conversa
  morre em silêncio. Com `failed`, `apps/inbox` marca a `Message` como `Failed`
  e remove a `PendingDispatch` (`DebounceSweepService`/
  `PushNotificationEndpoints`, caminho já existente) — o operador vê. Trocar
  silêncio por falha visível é a melhoria; encurtar a espera não é objetivo
  desta change (D2).

- **[Os 30 s implícitos continuam sendo um número que ninguém escolheu]** →
  Verdade, e está escrito assim em D2 e no comentário. O que esta change garante
  é que, qualquer que seja o limite, estourá-lo produz estado terminal. O valor
  vira decisão quando houver métrica para decidi-lo.

- **[Os guardas apertam dois parâmetros de conexão (`Command Timeout=N`,
  `Maximum Pool Size=3`) e podem ficar instáveis sob contenção de CPU do
  Podman]** → Os dois limites escolhidos precisam ficar acima de qualquer
  comando EF normal da suíte, e o que os testes observam é **estado terminal da
  task** e **o processo continuar funcionando**, nunca tempo decorrido. Nenhum
  dos guardas sincroniza por relógio: o de D8 ancora no mock do `IChatClient`
  (M7) e os demais em estado persistido. Esta base já registra a família "suíte
  sensível a contenção externa" (`WorkerHostCollection`); se algum destes entrar
  nela, entra com o mesmo tratamento e o mesmo registro, nunca com
  `Thread.Sleep`.

- **[`Maximum Pool Size=3` no host de teste é compartilhado com o
  `TaskJobConsumer` e o `PostgresTaskStore` daquele host]** → É exatamente o
  ponto: é o que faz o vazamento virar falha observável em 5 iterações em vez de
  100. Mas significa que o guarda de D9 não pode ser misturado a outro cenário
  no mesmo host — ele monta o seu, aperta o pool só ali, e nenhum outro guarda
  desta change herda esse aperto.

- **[`Command Timeout=0` reintroduz o defeito em outra forma]** → Não é mitigado
  por código, é registrado (D2). A connection string de produção vem do compose
  e não define `Command Timeout`.

## Migration Plan

Nenhuma migration de schema, nenhum dado transformado, nenhuma variável de
ambiente nova.

- **Deploy:** rebuild e restart de `apps/workers`. Os outros serviços não são
  tocados.
- **Rollback:** reverter o commit e rebuild. Não há estado novo persistido, e
  tasks gravadas como `failed` por esta change continuam legíveis por
  `apps/api`/`apps/inbox` na versão anterior — `failed` já era produzido por
  todos os outros caminhos de falha.
- **Verificação pós-deploy:** nenhuma task nova em `working` por mais tempo que
  o máximo plausível de execução. As linhas **já** presas (tasks em `working`,
  `PendingDispatch` em `Dispatching`) não são corrigidas pelo deploy — ver D5.

## Open Questions

Nenhuma incerteza de produto bloqueia esta change. As duas perguntas reais que a
exploração deixou têm gatilho e posição, e pertencem às changes seguintes:

- **Qual é o tempo máximo plausível que uma conversa segura o lock?** É o que
  transformaria os 30 s de D2 em decisão em vez de default. **Gatilho:** a
  primeira aquisição que falhar em produção — que, depois desta change, é um
  evento visível. **Posição:** entra na change de instrumentação (V6 da
  exploração), como uma das entradas que ela precisa produzir; não é pergunta
  desta change.
- **A varredura de `Dispatching` precisa existir?** Gatilho e posição em D5.
