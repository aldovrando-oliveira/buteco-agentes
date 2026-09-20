## Context

Posição **3** da fila fixada em `02-HISTORICO_E_STATUS.md` → `## Próximo passo`.
As duas anteriores — `lock-de-contexto-falha-terminal` e
`delegacao-ciclo-no-cadastro` — estão aplicadas. O diagnóstico já está fechado
pela exploração `replicas-de-worker` (20/09/2026); esta change **não reabre
diagnóstico**, ela instrumenta.

### O que existe hoje, lido no código e não de memória

| ponto | arquivo:linha | o que carrega |
|---|---|---|
| desistência por timeout | `AgentDelegations/AgentDelegationToolSetResolver.cs:226` | `TargetTaskId`, `Timeout` |
| Target em estado terminal de falha | `AgentDelegationToolSetResolver.cs:213` | `TargetTaskId`, `State` |
| o estado que distinguiria as causas | `AgentDelegationToolSetResolver.cs:203` | `current`, **fora de alcance no `catch`** |

**O `current` de `:203` está declarado dentro do `while`, dentro do `try`.** O
`catch` de `:220` não o enxerga. O valor que decide o diagnóstico existe no laço
e não existe no ponto que registra — é essa a lacuna estrutural, não a falta de
um campo no template da mensagem.

### As duas changes anteriores, conferidas contra a árvore antes de decidir

O prompt desta change pediu para conferir duas premissas. As duas foram lidas no
código, e **as duas mudaram o enunciado**.

**1. A falha de aquisição do lock termina a task em `failed`** — confirmado,
`Agents/AgentExecutionService.cs:186-200`. Mas o terceiro estado observável que
isso cria **não é distinguível pelo Source**, e é preciso dizer por quê:

- os **dois** caminhos de falha chamam o mesmo `FailTaskAsync` (`:373`), que
  chama `updater.FailAsync(cancellationToken: cancellationToken)`;
- decompilado (convenção 6, `ilspycmd` sobre `A2A 1.0.0-preview2`), a assinatura
  é `FailAsync(Message? message = null, CancellationToken = default)` — o
  código não passa mensagem, então **`Status.Message` é `null` nas duas**;
- logo, do lado do Source, `Failed` por contenção de lock e `Failed` por erro de
  provedor são **o mesmo byte**. Nenhum classificador aplicado à task do Target
  consegue separá-los, porque a diferença não está gravada em lugar nenhum.

O que **de fato** os separa é o log que o próprio Target emitiu — *"Falha ao
adquirir o lock de contexto… para a task {TaskId}"* (`:191`) contra *"Falha ao
executar o agente {AgentId} para a task {TaskId}"* (`:346`) — e a chave para
chegar até ele é o `TargetTaskId`. **Consequência de desenho: esta change não
precisa de um classificador de causa de `Failed`; precisa de correlação.** É
por isso que o registro carrega os cinco identificadores, e não um veredito.

**2. Ciclo de delegação deixou de ser cadastrável** — confirmado. E a lista de
causas do ramo `:226` **encolheu**, o que vale registrar porque o contrário
seria a suposição natural. Traçado com as duas changes aplicadas:

- `A(d0)` segura `lock(A, ctx)` e espera `B`; `B(d1)` espera `A(d2)`; `A(d2)`
  bloqueia no mesmo lock, estoura o `CommandTimeout` de 30 s e **termina em
  `Failed`**; `B(d1)` vê `Failed` em ~30 s e sai pelo ramo `:213`; `B` conclui;
  `A(d0)` recebe resposta. **A cadeia inteira resolve em ~30 s e não produz
  expiração nenhuma.**
- Com instâncias de menos, `A(d2)` sequer é consumida e fica `Submitted` —
  que é contenção, e já está na lista.

Ou seja: o ciclo **não acrescenta causa nova** ao `:226`; ele migrou para o
`:213` ou colapsou em contenção. Continua valendo como caso raro a nomear,
alcançável só por escrita direta em `agent_delegations` (residual com gatilho de
reabertura de D3 registrado no `02`).

### Restrições

- Isolamento entre apps: só `apps/workers`.
- Nada em `libs/` — **não há nada a justificar aqui**, porque nada é colocado lá
  (convenção 2: o consumidor em app diferente não existe).
- Sem dependência nova. `PeriodicTimer` e `TimeProvider` são BCL do .NET 10, já
  em uso no monorepo (`Orchestration/DebounceSweepService.cs:27`;
  `Program.cs:33`) — não há versão de biblioteca a fixar nesta change.

## Goals / Non-Goals

**Goals:**

- Tornar o `C` de pico **observável**, como série temporal, para a
  `replicas-de-worker` decidir entre capacidade e o redesenho de retomada.
- Separar, no registro de desistência, **contenção** (Target `Submitted`, nunca
  consumido) de **Target genuinamente lento** (Target `Working`).
- Permitir ligar a falha vista pelo Source ao registro que o Target produziu por
  conta própria, que é o único lugar onde as duas causas de `Failed` se separam.

**Non-Goals:**

- Número de instâncias, `docker-compose*.yml`, `deploy/`, `docs/` de deploy.
- Tabela de métricas, rota de agregação, tela — é a etapa 1 da linha de
  métricas, posição 6 da fila.
- Mudar `AgentDelegationToolOptions.Timeout` ou `PollInterval`.
- `apps/api`, `apps/inbox`, `apps/frontend`, `libs/`.
- O redesenho de retomada (V4).
- Defesa de ciclo em runtime (D3 de `delegacao-ciclo-no-cadastro` permanece).
- Varredura de `PendingDispatch` órfã em `Dispatching` — outro app, outro
  mecanismo, e o item do `02` já lhe deu gatilho e posição.

## Árvore de pastas proposta

Só o que esta change cria ou toca; `…` marca o que continua como está.

```
apps/workers/
├── src/Buteco.Workers/
│   ├── AgentDelegations/
│   │   ├── AgentDelegationToolSetResolver.cs      (M)
│   │   └── IAgentDelegationToolSetResolver.cs     (M)
│   ├── Agents/
│   │   └── AgentExecutionService.cs               (M, 1 linha — :240)
│   ├── Diagnostics/                               (NOVA pasta)
│   │   ├── NonTerminalTaskDetector.cs             (C)  + NonTerminalTaskReport
│   │   └── NonTerminalTaskDetectorService.cs      (C)
│   ├── Options/
│   │   ├── AgentDelegationToolOptions.cs          (…)  janela lida daqui
│   │   └── TaskDiagnosticsOptions.cs              (C)
│   ├── Knowledge/ Mcp/ Messaging/ Notifications/ A2A/ Infrastructure/ Naming/  (…)
│   └── Program.cs                                 (M)
└── tests/Buteco.Workers.Tests/
    ├── AgentDelegationConcurrencyTests.cs         (M — G1..G4)
    ├── Diagnostics/
    │   └── NonTerminalTaskDetectorTests.cs        (C — T1..T8)
    └── Support/
        └── NullAgentDelegationToolSetResolver.cs  (M, 1 linha)
```

Nenhum arquivo fora desta árvore. Nenhuma migration, nenhuma entidade EF nova —
logo `KnowledgeSchemaMirrorTests`, que afirma que os modelos de `apps/api` e
`apps/workers` geram o mesmo schema, fica intocado por construção.

## Decisions

### D1 — O campo é "último estado observado", nunca "o estado na desistência"

**Decidido:** hoistar uma variável fora do `try` que guarde a última leitura
bem-sucedida de `GetTaskAsync` e o instante dela, e registrá-la com esse nome.

`current` não existe no `catch` (ver Context). A correção óbvia — declarar a
variável fora — traz junto uma afirmação que o sistema não sustenta: entre a
última leitura e a desistência cabe um `PollInterval` (1 s) **mais** a chamada
que foi cancelada. Chamar o campo de "estado no instante da desistência" seria
afirmar mais do que o sistema sabe (convenção 13), num registro cujo propósito
inteiro é ser lido como evidência.

**Alternativa recusada — uma releitura fresca dentro do `catch`, com token
próprio.** Daria o estado no instante exato. Recusada por três motivos, nesta
ordem: (1) é uma ida ao banco num caminho de **degradação graciosa**, que por
convenção 4 não pode lançar — precisaria do seu próprio `try/catch` aninhado, e
o ganho não paga; (2) a precisão que ela compra é ≤ 1 s sobre uma janela de
120 s, e a distinção que decide (`Submitted` contra `Working`) é **estrutural**,
não temporal — uma task que virou `Working` no último segundo passou 119 s sem
ser consumida, e classificá-la como contenção é a leitura correta; (3) o
`timeoutCts` já está cancelado, então seria preciso construir um token novo
justamente no caminho de falha.

**O quarto estado tem nome próprio.** Se a tool desistir sem **nenhuma** leitura
bem-sucedida, o registro diz *isso*, e não um estado. É a distinção que a
convenção 13 já custou a aprender numa tela — "não sei" e "sei que não existe"
são coisas diferentes, e gastar o vocabulário de um no outro apaga a distinção
onde ela existe. Aqui: `Submitted` é uma resposta que chegou; "nunca observada"
é a pergunta sem resposta.

### D2 — Classificação por `TaskState`, e `KnowledgeIndexingFailure.Describe` NÃO ganha segundo consumidor

**Decidido:** a classificação desta change é por **`TaskState`** — enum fechado,
emitido como valor estruturado no log, nunca como texto a ser reinterpretado.
`KnowledgeIndexingFailure.Describe` **não é reusado**.

O prompt desta change previa reusá-lo e registrar que seria o segundo consumidor.
Conferido no arquivo, o enunciado não se sustenta, e o motivo é do tipo que a
convenção 6 nomeia como o mais difícil de pegar — a fonte certa, respondendo a
uma pergunta que não era a que decide:

- **Ele é classificador de texto de TELA, não de log.** O XML doc
  (`Knowledge/Indexing/KnowledgeIndexingFailure.cs:5-15`) diz: *"Traduz a causa
  de uma falha em texto **de operador**… A tela de documentos mostra este texto
  **completo, sem truncar**"*. As saídas são desse registro: *"Reindexe o
  documento mais tarde"*, *"se persistir, é caso de suporte técnico"*.
  Emprestá-lo poria "Reindexe o documento" num log de delegação.
- **Não há exceção a classificar no caminho que decide.** O ramo de expiração
  captura um único tipo — `OperationCanceledException`, já filtrado por
  `when (!cancellationToken.IsCancellationRequested)` — de significado
  conhecido. O ramo `:213` não tem exceção nenhuma: tem um `TaskState`.

**O que de fato se reusa é o princípio**, e ele vale exatamente como o prompt o
formulou: *switch por tipo, nunca parse de mensagem*. Aplicado ao input certo,
o tipo é `TaskState`, e o switch é o próprio valor emitido estruturado.

**Consequência para a convenção 2:** como não há segundo consumidor,
`KnowledgeIndexingFailure` continua com um só, e **não há nada a extrair para
`libs/`** — a pergunta se fecha por não ter nascido, não por ter sido pesada.

### D3 — A janela é derivada do timeout de delegação, não escolhida

**Decidido:** a janela é lida em runtime de
`IOptions<AgentDelegationToolOptions>.Value.Timeout` (hoje 120 s). Nenhuma
constante nova, nenhum número novo a defender.

A armadilha aqui é a forma **oposta** da que a change anterior corrigiu: lá, um
limite (30 s) existia sem ter sido escolhido, e a correção foi deixá-lo escrito
e com guarda; aqui a tentação é **escolher** um limite cujo mecanismo ninguém
estabeleceu. A convenção 13 recusa as duas.

**O mecanismo que a derivação estabelece**, e que é a única coisa que a janela
afirma: uma task não-terminal mais velha que **uma espera de delegação inteira**
já sobreviveu à janela em que qualquer Source poderia ainda estar esperando por
ela. Tudo que o detector reporta é, por construção, real. Ele **sub-reporta** —
e essa é a direção certa para o primeiro instrumento.

**E a janela não é um veredito.** Um turno de agente que encadeia várias
chamadas de tool de delegação ultrapassa 120 s legitimamente, e o sistema não
distingue isso de uma task travada. Por isso o registro nomeia **estado, idade e
janela**, e a palavra "travada" não aparece nele. Há guarda para essa negativa
(T6) — é uma recusa, e recusa some na primeira refatoração que "limpa" o teste
se não tiver `it()` próprio.

**Gatilho de recalibração (convenção 22):** nenhum, por construção — a janela
**é** o timeout, lido em runtime, então quem mudar `AgentDelegationToolOptions.Timeout`
move a janela junto e não pode esquecer. É o único desenho aqui em que a
referência não consegue envelhecer separada do estado que a mediu.

**Escopo colado ao número:** 120 s é o default de
`AgentDelegationToolOptions.Timeout`, **não vinculado a seção de configuração**,
o que o torna constante de produto — e é o **timeout de uma espera**, nunca a
duração máxima de uma task, que não tem limite estabelecido.

### D4 — O intervalo de varredura NÃO tem base medida, e isso fica escrito

**Decidido:** `TaskDiagnosticsOptions.SweepInterval`, default **30 s** = um
quarto da janela. É escolha de **resolução**, não limiar de correção, e não há
medição que a sustente.

Dizer isso é o que a convenção 13 pede quando não há base. O que a escolha
compra: um episódio que dure uma janela inteira produz ~4 observações, o
suficiente para separar um pico de uma amostra isolada — que é a única coisa que
a série precisa fazer.

**Recusado ancorar em `DebounceOptions.SweepInterval` (2 s, `apps/inbox`)**, que
é o único intervalo de varredura periódica já medido nesta base. O motivo é
exatamente a ocorrência 3 da convenção 22: aquele número foi escolhido para
**debounce de mensagem de contato**, e usá-lo aqui seria citar um número correto
respondendo a outra pergunta. Dois números sobre "intervalo de varredura"
convivem sem se contradizer até alguém usar um no lugar do outro.

**Gatilho de recalibração:** a primeira leitura da série pela
`replicas-de-worker`. Quem a ler diz se a resolução serviu; se não serviu,
recalibra **naquela change**, que é a que muda o estado (convenção 22).

**Por que existe como `IOptions<T>` e não `const`:** mesmo motivo já registrado
em `AgentDelegationToolOptions` — não é vinculado a seção de configuração, o
default é constante de produto, e o `IOptions<T>` existe para que o guarda possa
encurtar o intervalo sem uma suíte de 30 s por teste. Não é opção de produto
(convenção 2: não há cenário real de alguém precisar de outro valor em
produção).

### D5 — Varredura periódica, em `BackgroundService` próprio

**Decidido:** um `BackgroundService` com `PeriodicTimer`, o **primeiro
componente periódico de `apps/workers`**.

**Achado que muda o enunciado do prompt:** `apps/workers` **não** tem precedente
de trabalho periódico. A fila de indexação é consumidor RabbitMQ orientado a
evento (`Knowledge/Indexing/KnowledgeIndexingConsumer.cs:45` — `ReceivedAsync`,
e `Task.Delay(Timeout.Infinite)` no fim só para manter o serviço vivo). O único
componente orientado a timer do monorepo é `DebounceSweepService`, em
`apps/inbox`, cujo próprio comentário diz *"Único componente orientado a
timer/scheduling do projeto"*. Ele serve de **molde de forma** (PeriodicTimer +
`try/catch` por ciclo + escopo por ciclo), não de precedente de intervalo.

**Alternativa recusada — detecção sob demanda, no momento de cada expiração.**
Seria mais barata: zero serviço novo, zero interação com o startup, zero
duplicação por instância. Recusada por uma propriedade medida do caminho:
**sob contenção sustentada não há "momento de cada expiração" que sirva**. Com
`C ≥ N`, toda instância está dentro de `WaitForTerminalStateAsync`, e a
observação que interessa é justamente a do período em que nada conclui. Pior: a
detecção sob demanda **nunca** enxerga a task que não tem ninguém esperando por
ela — que é exatamente o residual nomeado no `02`.

**E o laço periódico continua girando sob contenção**, o que é o que torna D5
viável e foi conferido no código: o bloqueio do consumidor é `await` puro
(`TaskJobConsumer.cs:52` → `await executionService.ExecuteAsync`, e a espera da
delegação é `await Task.Delay(PollInterval, …)` em `:217`). Nenhuma thread fica
presa, então um `PeriodicTimer` em outro hosted service tica normalmente.
Se em vez de `await` houvesse bloqueio de thread, D5 inteiro cairia.

**O que isso faz com as checagens de startup: nada, e o motivo é estrutural.**
`host.ValidateTimeZoneConfiguration()` e `host.ValidateEmbeddingIndexConsistency()`
rodam sobre o **host construído**, entre `Build()` e `Run()`
(`Program.cs:86-94`); hosted service só começa em `Run()`. Esta change não
acrescenta nenhuma checagem de boot e não muda a ordem das duas. É a forma
`IHost` da convenção 8, não a forma `IServiceCollection` — logo o teste extra
que aquela forma cobra não se aplica.

**O que isso faz com o número de instâncias, e é a parte que engana:** o
detector lê estado **global** (a tabela inteira), então com `N` instâncias saem
`N` linhas **idênticas** por tique. Não é medição `N` vezes: é a mesma medição
`N` vezes. Quem somar as linhas da série superestima por fator `N`. Fica
escrito no comentário do serviço, que é onde quem lê o log vai procurar.
**E é por isso que esta change não fixa a resposta da `replicas-de-worker` por
acidente:** o valor reportado não depende de `N` — só o número de cópias
depende.

### D6 — O detector separa consulta de emissão

**Decidido:** `NonTerminalTaskDetector` (consulta → `NonTerminalTaskReport`) e
`NonTerminalTaskDetectorService` (timer, `try/catch` por ciclo, emissão do log).

O motivo é o guarda, não a estética: a semântica da consulta (quais linhas, como
agrupadas, o par "povoado mas nada envelhecido") precisa de asserção direta, e o
`BackgroundService` precisa de asserção sobre o **ciclo** (continua depois de uma
falha, tica). Convenção 15, segunda forma: um guarda só serve se afirmar a
garantia no componente que a correção toca.

**Contrapartida declarada, porque a convenção 8 já a cobrou nesta base:** em
`apps/workers` os testes montam o host à mão (`BuildHost` em cada classe), não
pelo `Program.cs` — diferente de `apps/api`, onde a `WebApplicationFactory` roda
a composição real. Logo **nenhum teste desta suíte prova que `Program.cs`
registra o serviço**: remover a linha deixa T1..T8 verdes e a produção sem
detector. Não é testável dentro da forma de teste desta suíte, e por isso vira
item explícito de conferência no `tasks.md` em vez de risco sem contraparte
(convenção 10).

### D7 — O `:213` entra junto com o `:226`

**Decidido:** os dois ramos de falha ganham o mesmo registro.

Não é oportunismo: é o ramo `:213` que recebe o `Failed` por contenção de lock
criado pela change anterior — o "terceiro estado observável" do prompt —, e
deixá-lo para trás entregaria um instrumento cego justamente à novidade que
motivou a pergunta. Os dois ramos devolvem falha ao mesmo LLM e pedem a mesma
correlação.

### D8 — Duplicação temporária consciente, com sucessor nomeado

**Decidido:** os dois mecanismos desta change serão **substituídos** pela linha
de métricas (`metricas-execucao-coleta`, posição 6 da fila), que vai gravar
tempo de fila e origem como métrica de verdade. Fica escrito no `design.md` e no
comentário do serviço.

Sem isso, o próximo a ler o código encontra um detector em log e uma tabela de
métricas cobrindo a mesma grandeza e conclui descuido. **Posição do
descarte:** na própria `metricas-execucao-coleta`, que herda a decisão e precisa
reavaliá-la em vez de descobri-la depois.

### D9 — O guarda afirma o campo estruturado, nunca o texto da mensagem

**Decidido:** os guardas G1..G4 capturam o **estado estruturado** do log (os
pares chave/valor), não a string formatada.

A armadilha já mordeu duas vezes nesta linha de trabalho: restaurar um braço de
sonda como está reprova **por texto** quando as duas changes anteriores mudaram
as mensagens e os estados terminais. Um guarda escrito sobre
`"não concluiu dentro do timeout"` continua vermelho depois da correção e passa
a impressão de funcionar. Um guarda escrito sobre a **presença e o valor** da
chave `LastObservedTargetState` reprova contra `HEAD` porque a chave não existe,
e fica verde depois porque ela passa a existir com o valor certo — pela
propriedade, não pela redação.

**Provedor de log capturando estado estruturado:** tipo privado aninhado na
classe de teste, seguindo os dois precedentes já existentes nesta suíte
(`TimeZoneStartupValidationTests`, `TemporalContextMessageInstantTests`) — os
dois capturam só nível e texto, e migrá-los para uma versão estruturada é mexer
em dois arquivos fora do escopo desta change, um deles com história registrada
de flake ligado a provider de log. **Esta será a terceira cópia, o que cumpre o
gatilho de extração da convenção 2** — registrado como item aberto, com
posição: a change que precisar da quarta.

### D10 — Quem responde o `C` é a coluna `Submitted`, e a de `Working` subestima por construção

**Decidido:** a coluna que aproxima o `C` é a contagem de **`Submitted`
envelhecido**. A de `Working` **não** é uma segunda estimativa do mesmo número,
e não pode ser lida como tal.

A série reporta as duas separadas (é o requisito que recusa o total único), e
duas colunas entregues sem regra de leitura fazem quem as receber escolher uma
por conta. A decisão de leitura já está implícita no desenho; o que faltava era
ela estar escrita — e uma decisão de capacidade tomada sobre a coluna errada
nasceria de uma omissão deste documento, não do dado.

**Por que `Submitted` aproxima o `C`.** Sob contenção, as instâncias estão
ocupadas pelos **Sources**, que ficam em `Working` esperando; cada Source deixa
atrás de si um **Target publicado e nunca consumido**, que fica em `Submitted`.
Um Target faminto por Source delegando — e é essa contagem que tem a mesma
cardinalidade do `C`.

**Por que `Working` subestima sistematicamente, e a causa é a mecânica da janela
de D3.** A janela **é** o timeout de delegação, e é exatamente nesse ponto que o
Source desiste. Um Source só entra na contagem de `Working` envelhecido no
intervalo estreito entre desistir e terminar o turno — e esse intervalo é curto
e depende do LLM responder. **Quem olhar `Working` vai ver perto de zero e
concluir que não há contenção precisamente quando há.**

É a mesma família da ocorrência 3 da convenção 22 — dois números sobre a mesma
grandeza aparente ("tasks envelhecidas") respondendo a perguntas diferentes,
convivendo sem se contradizer até alguém citar um no lugar do outro. A diferença
é que aqui as duas nascem no mesmo registro, o que torna a troca mais fácil, não
menos.

**Relação com o risco *"[Uma leitura isolada é lida como o `C` de pico]"*:** são
coisas diferentes e complementares — aquele diz **quantas leituras** (o pico é
da série, nunca de um tique), este diz **qual coluna** de cada leitura. Errar um
dos dois basta para errar o `C`.

**Onde isso fica escrito para quem decide:** não só aqui. A nota de leitura da
série no `02` (task 7.6) carrega as duas regras juntas — o fator `N` das linhas
idênticas e qual coluna responde o `C` —, porque é esse parágrafo que a
`replicas-de-worker` vai ler, e não este `design.md`.

## Projeção (convenção 18) — fechada AQUI, antes de escrever código

**Oitava medição da série.** A sétima acertou a mistura (comentário:lógica
1,32:1 entregue contra 1,4:1 projetado) e errou o volume em +57%, inteiramente
por **contagem de componentes** — dimensão que nenhuma régua cobria. A correção
que esta projeção aplica é a que aquele fechamento nomeou: **contar unidades
públicas, não só linhas**; e **nomear direções de erro não substitui contar**.

### Contagem de componentes (a parte que carrega a projeção)

| | criados | modificados |
|---|---|---|
| produção | **3 arquivos** | **4 arquivos** |
| teste | **1 arquivo** | **2 arquivos** |

**Unidades públicas novas: 4** — `TaskDiagnosticsOptions`,
`NonTerminalTaskDetector`, `NonTerminalTaskReport`,
`NonTerminalTaskDetectorService`. Mais **1 assinatura de interface alterada**
(`IAgentDelegationToolSetResolver.ResolveAsync`, +1 parâmetro) e **2 sítios de
log reescritos**. Zero entidade EF, zero migration, zero rota.

**Blast radius lido no código, não previsto** (a régua: modificados são
dominados por edição de uma a três linhas, e cada dimensão tem um comando que a
enumera de graça):

| o que se acrescenta | quem é arrastado | como foi enumerado |
|---|---|---|
| parâmetro em `ResolveAsync` | interface, implementação, duplo de teste, 1 sítio de chamada | compilação |
| hosted service novo | `Program.cs` | — |
| — | **os 13 `AddSingleton<IAgentDelegationToolSetResolver, …>` dos testes NÃO são arrastados** | `grep`: registram tipo, não assinatura |

Esse último é o resultado **negativo** que mais muda a projeção: a leitura
ingênua contaria 13 arquivos de teste modificados. São **zero**.

> **CORREÇÃO DE FATO, feita durante a implementação (convenção 9). Os números
> projetados acima ficam como estão — são o registro do que foi projetado, e
> editá-los destruiria a medição da oitava rodada.** A tabela de blast radius
> dizia "quatro arquivos" para o parâmetro em `ResolveAsync`. **São cinco.** O
> quinto é `apps/workers/tests/Buteco.Workers.Tests/DelegationToolNameSlugifierTests.cs`,
> com **4 sítios de chamada num só arquivo**, e a causa da omissão é precisa e
> reusável: a enumeração foi feita com `grep` pelo nome da **interface**
> (`IAgentDelegationToolSetResolver`), e aquele teste chama a **classe
> concreta** — `resolver.ResolveAsync(...)` sobre um
> `AgentDelegationToolSetResolver` construído à mão. Nenhum `grep` pelo tipo da
> interface o alcança.
>
> É a mesma família das três dimensões de blast radius já registradas na
> convenção 18, numa forma nova: **grep por interface não enumera consumidor da
> implementação concreta; só a compilação enumera.** O `design.md` dizia que a
> enumeração seria feita "na compilação" — e foi ela que achou o quinto, depois
> de o `grep` ter dado a contagem errada. A régua que vale guardar: quando a
> mudança é de **assinatura**, a contagem por `grep` é estimativa; a contagem por
> compilação é a medição, e as duas não coincidem quando existe teste que
> instancia o tipo concreto.

### Linhas

| | projetado |
|---|---|
| produção, lógica | **~105** |
| produção, comentário | **~205** |
| produção, total | **~310** |
| teste, acrescentadas | **~510** |
| teste : produção | **~1,65 : 1** |
| comentário : lógica | **~1,95 : 1** |
| testes novos | **12** (256 → **268**) |

**A mistura foi projetada contando registros de mecanismo**, que é a dimensão
que a sétima medição provou projetável. Esta change entrega **oito**: (1) o
hoist e a semântica de "último observado"; (2) por que `Describe` não é reusado;
(3) a janela derivada; (4) o intervalo sem base e a recusa de ancorar no
debounce; (5) primeiro serviço periódico — startup e `N` instâncias; (6)
periódico contra sob demanda, com o argumento do `await`; (7) a substituição
pela linha de métricas; (8) `Failed` indistinguível do lado do Source. Contra
três registros em `lock-de-contexto-falha-terminal` (2,3:1) e um em
`delegacao-ciclo-no-cadastro` (1,32:1) — mas estes oito são **curtos**, vários
são recusas de uma razão, cuja unidade medida na 5a-4 é ~26 linhas para três
razões com arquivo e linha.

**Os 12 testes saíram dos cenários do delta de spec, não de uma lista de coisas
a testar** — que é a correção que a 5a-4 deixou: contar **estados observáveis**,
não afirmações. São 4 cenários novos em `agent-delegation-execution` e 8 em
`workers-nonterminal-task-detection`.

**Direções de erro nomeadas — e escritas sabendo que nomeá-las não carrega a
projeção.** A sétima nomeou duas e as duas estavam erradas; o desvio veio de um
terceiro lugar. Estas são registradas como hipóteses a conferir no fechamento,
com a contagem de componentes acima como a parte que de fato sustenta o número:

- **para baixo:** a captura de estado estruturado do log pode exigir mais
  andaime do que os ~35 projetados, porque os dois precedentes desta suíte
  capturam só texto;
- **para cima:** `NonTerminalTaskDetector` pode não precisar de tipo de
  resultado próprio se o relatório couber numa tupla — seriam 3 unidades
  públicas, não 4.

**Regime da medição do fechamento (convenção 22):** o diffstat é **decomposto**
(produção × teste, lógica × comentário, criado × modificado), nunca o headline
do commit, que nesta base já chegou a 3,1x do trabalho à mão. A comparação é
tarefa própria no `tasks.md` e **só compara o escopo que estava projetado** —
escopo acrescentado durante a implementação entra como linha à parte.

## Risks / Trade-offs

- **[O guarda reprova por texto em vez de por propriedade]** → G1..G4 afirmam a
  **chave e o valor estruturados** do log, nunca a string formatada (D9). O
  texto atual foi lido antes de escrever a asserção, e as mensagens das duas
  changes anteriores são a razão pela qual isso não é hipotético.
- **[O guarda reprova antes e depois, por estar no componente errado]** → cada
  guarda roda no componente que a correção toca: G1..G4 sobre o resolvedor real,
  dentro de um host montado como em produção; T1..T5 sobre a consulta; T7/T8
  sobre o ciclo do serviço. Nenhum semeia estado por fora do caminho que decide.
- **[T1..T8 não compilam contra `HEAD`, e "vermelho" ali não significa nada]**
  → declarado: contra `HEAD` eles são marcados `—`, não 🔴. O peso da convenção
  15 é carregado por **G1..G4**, que compilam contra `HEAD` (só observam log) e
  reprovam pela propriedade ausente.
- **[Remover a linha do `Program.cs` deixa tudo verde e a produção sem
  detector]** → não é testável nesta suíte (D6). Vira conferência explícita de
  registro no `tasks.md`, junto da conferência de escopo de arquivo.
- **[A série é lida como `N` medições independentes]** → o valor é global e as
  `N` linhas por tique são idênticas; fica no comentário do serviço e no log de
  início, que carrega janela e intervalo.
- **[Uma leitura isolada é lida como o `C` de pico]** → o pico é da **série**, e
  o registro carrega a janela junto do número para que ele não migre de pergunta
  (convenção 22). A change entrega a série; quem calcula o pico é a
  `replicas-de-worker`. **Par deste risco: D10** — este trata de *quantas
  leituras*, D10 de *qual coluna* de cada leitura; errar um dos dois basta.
- **[A coluna de `Working` é lida como estimativa do `C`]** → ela subestima por
  construção, e o erro é silencioso: mostra perto de zero justamente sob
  contenção. A regra de leitura está em **D10** e vai junto com a série na nota
  do `02` (task 7.6), que é onde a `replicas-de-worker` a encontra.
- **[A varredura derruba o processo ao falhar]** → `try/catch` por ciclo, molde
  de `DebounceSweepService`/`TaskJobConsumer` (convenção 4), com o `try` abrindo
  **antes** da abertura do escopo e da consulta — a chamada que mais
  realisticamente falha é a consulta ao banco, e esta base já teve duas vezes o
  defeito de um `try/catch` correto com a chamada arriscada fora dele. Guarda
  T8.
- **[Volume de log em produção]** → uma linha por tique por instância **só
  quando há achado**; com zero achados a emissão cai para `Debug`. O log de
  início em `Information` é o que torna a ausência de achado distinguível da
  ausência da varredura.
- **[A 14ª classe do `WorkerHostCollection` move o regime da suíte]** →
  recalibrar é tarefa **desta** change (convenção 22), não descoberta da
  seguinte. Tarefa própria no `tasks.md`, com o número medido e o escopo colado.
- **[Medição feita enquanto a anterior ainda desmonta containers]** → cada
  rodada registra a carga na largada; uma rodada com a anterior ainda encerrando
  produziu, nesta base, uma classe inteira vermelha em 12 s, com assinatura de
  falha de fixture. Medição assim **não é medição** e é repetida.

## Migration Plan

Sem migration, sem mudança de schema, sem passo de deploy próprio. O rollback é
reverter o commit: nenhum dado é criado, alterado ou apagado por esta change.

O que muda para quem opera, e precisa estar dito antes de subir: aparecem
linhas de `warn` novas de um componente que não existia. **Elas não indicam
defeito novo** — indicam que passou a haver quem olhe. É a mesma leitura errada
que a change anterior já registrou sobre o primeiro pico de `failed`.

Esta change entra **antes** da limpeza do banco e do deploy (posições fixadas no
`02`), então a primeira série é medida contra o estado atual do piloto.

## Open Questions

- **A série vai ser lida por quem, e em que janela de observação?** A
  `replicas-de-worker` precisa do `C` de **pico** contra tráfego real, e esta
  change não define por quanto tempo observar antes de decidir. É pergunta de
  produto/operação, não técnica, e pertence àquela change — mas se a janela de
  observação for curta demais, o pico medido é um piso e a decisão de capacidade
  herda esse viés.
