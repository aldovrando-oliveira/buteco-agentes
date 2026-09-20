## Context

### O defeito, e por que ele é de desenho

A exploração `replicas-de-worker` (20/09/2026) mediu, com **quatro** instâncias
de worker, que um ciclo `A→B→A` trava até o timeout de delegação:

```
Probe_CycleAB_A  (4 instâncias — sobra consumidor; timeout de delegação 45s > 30s do Npgsql)
  ### ciclo A→B→A com 4 instâncias: state=Completed em 46,2s
    texto="Delegação não concluiu dentro do tempo limite."
    task ef70c745 agente=A state=Working      ← órfã
  ### tasks órfãs presas em Working: 1
```

O mecanismo: a task de A em profundidade 0 segura
`pg_advisory_lock(hashtext(A), hashtext(ctx))` enquanto espera B; B delega de
volta para A; a task de A em profundidade 2 pede o mesmo lock e bloqueia. É o
único achado da exploração **inerente ao desenho** e não à contagem de
processos — acrescentar réplica não resolve porque o recurso disputado é o
lock, não o consumidor.

### Verificação desta change, fechada antes de projetar e antes de escrever código

**V1 — a task de profundidade 2 ainda fica órfã?** Não, e isso muda o enunciado
do defeito, não a sua existência. `AgentExecutionService.cs:180-200`: a
aquisição do lock tem `try/catch` próprio e o `catch` chama `FailTaskAsync`.
Estourado o `CommandTimeout` de 30 s do Npgsql, a task de profundidade 2 termina
em **`failed`**.

**V2 — o que o Source vê agora.** `AgentDelegationToolSetResolver.cs:205-215`:
a espera detecta qualquer estado terminal, e para um não-`Completed` devolve
`"Delegação não concluída com sucesso (estado final: Failed)."`. Ou seja, o
texto observado pela sonda (`"não concluiu dentro do tempo limite."`, que é o
ramo de `OperationCanceledException`, linha 227) **não é mais o que aparece**, e
o tempo cai do timeout de delegação para os ~30 s do `CommandTimeout`. Quem
restaurar a sonda como está vai vê-la reprovar por texto e ler isso como
regressão. É achado, e é por ele que o guarda muda de camada (D4).

**V3 — o `DelegationDepthLimit` cobre?** Não.
`AgentExecutionService.cs:71` fixa o teto em **5**, e a checagem roda **antes**
da aquisição do lock (linha 123 contra 183) — então um ciclo curto passa pela
checagem e trava no lock. Um ciclo de dois saltos trava em profundidade 2. O
teto só rejeita antes do lock os ciclos longos o bastante para ultrapassá-lo.

**V4 — quem escreve em `agent_delegations`.** Inventário, não leitura de
memória:

```
grep -rn "AgentDelegations\." apps/api/src --exclude-dir=obj --exclude-dir=bin
  → ReplaceAgentDelegationsCommandHandler.cs:52  RemoveRange
  → ReplaceAgentDelegationsCommandHandler.cs:56  Add
```

`apps/workers` só lê (`AgentDelegationToolSetResolver.cs:37`, `AsNoTracking`; a
entidade projetada tem construtor privado e setters privados, sem `Add`/`Remove`
em lugar nenhum). `apps/inbox` não toca a tabela. **O handler é o escritor
único**, e é esse fato que sustenta D3.

**V5 — o cadastro hoje não tem lacuna: tem requisito.**
`openspec/specs/agent-delegation-binding/spec.md:76-96` carrega o requisito
*"Nenhuma detecção de ciclo ou de vínculo bidirecional no cadastro"* com dois
cenários, e `AgentDelegationEndpointsTests.cs:134-166` tem os dois testes verdes
correspondentes. Corrigir o defeito passa por **inverter** requisito, cenários e
testes — não por acrescentar.

**V6 — baseline de `apps/api`** (convenção 19), `HEAD` `1ba84ec`, 20/09/2026:
**317 aprovados / 1 reprovado / 318**. A reprovada é
`AgentDeactivationTests.SendMessage_WithPushNotificationConfig_ForInactiveAgent_NeverPublishesJobOrCallsWebhook`,
com `Assert.Empty() Failure: Collection was not empty`. Os três escopos, medidos
hoje e não citados de registro anterior:

| escopo rodado | resultado |
|---|---|
| suíte inteira | 317/318 — reprova |
| a classe sozinha (`~AgentDeactivationTests`) | 2 aprovados / 1 reprovado de 3 |
| o teste sozinho (`FullyQualifiedName=`) | aprovado |

### Dado

`agent_delegations` tinha **zero** linhas no censo do banco de desenvolvimento,
e o banco do piloto será limpo antes do próximo deploy. Nenhum cenário de ciclo
existe para conferir contra dado real: os guardas semeiam o grafo. Isso não é
obstáculo — é o que impede a change ser escrita contra uma suposição de como os
dados são.

## Goals / Non-Goals

**Goals:**

- Recusar no cadastro (`apps/api`) qualquer conjunto de delegações que feche um
  ciclo de volta ao agente Source, em qualquer comprimento.
- Corrigir a Decision 3 de `ReplaceAgentDelegationsCommandHandler` com a causa
  real (convenção 9).
- Restaurar o cenário de `Probe_CycleAB_A` como guarda, na camada onde a
  correção vive, afirmando que a defesa de cadastro é suficiente.
- Carve do defeito pré-existente de `AgentDeactivationTests` (escopo 2).
- Forma curta da convenção 22 no `01` (escopo 3).

**Non-Goals:**

- **Número de instâncias, compose ou documentação de deploy.** A fórmula
  `N ≥ C × D + 1` da `V3.4` da exploração entra na change de réplicas.
- **Instrumentação de diagnóstico de delegação** — é `delegacao-diagnostico`, a
  change seguinte, e pré-requisito da de réplicas.
- **`DelegationDepthLimit`** fica em 5, sem mudança.
- **Métricas.**
- **Varredura de `PendingDispatch` órfã em `Dispatching`** — item aberto com
  gatilho e posição próprios, em `apps/inbox`.
- **Qualquer mudança em `apps/workers`.** Ver D3.
- **Qualquer mudança em `apps/frontend`.** Ver D8 — é achado sequenciado, não
  escopo puxado (convenção 1).
- **Migration.** A detecção lê `agent_delegations`; não muda schema. (E
  migration de banco sai só de `apps/api`, que é onde a change está — mas não há
  nenhuma.)

## Árvore de pastas proposta

```
apps/api/
├── src/Buteco.Api/
│   └── AgentDelegations/
│       ├── AgentDelegationCycleDetector.cs          ← CRIADO
│       ├── AgentDelegationLookup.cs                   (inalterado)
│       ├── Commands/ReplaceAgentDelegations/
│       │   ├── ReplaceAgentDelegationsCommand.cs      (inalterado)
│       │   ├── ReplaceAgentDelegationsCommandHandler.cs  ← MODIFICADO
│       │   └── ReplaceAgentDelegationsResult.cs       ← MODIFICADO
│       ├── Endpoints/
│       │   └── AgentDelegationEndpoints.cs            ← MODIFICADO
│       ├── Entities/AgentDelegation.cs                (inalterado)
│       └── Requests/ReplaceAgentDelegationsRequest.cs (inalterado)
└── tests/Buteco.Api.Tests/
    ├── AgentDelegationCycleDetectorTests.cs         ← CRIADO (unitário, sem banco)
    ├── AgentDelegationEndpointsTests.cs             ← MODIFICADO
    ├── AgentDeactivationTests.cs                    ← MODIFICADO (escopo 2)
    └── Support/
        └── AgentDelegationSeed.cs                   ← CRIADO (arranjo de G5)

01-ARQUITETURA_E_CONVENCOES.md                       ← MODIFICADO (escopo 3)
openspec/changes/delegacao-ciclo-no-cadastro/
└── specs/agent-delegation-binding/spec.md           ← delta (REMOVED + ADDED)
```

Nada vai para `libs/`: há **um** consumidor e a regra é de domínio de
`apps/api` (convenção 2).

**Correção feita durante a implementação** (convenção 9): esta árvore trazia
`Support/` como *(inalterado)*, e estava errada por descuido de redação, não por
mudança de decisão — a mesma seção "A rodada de guardas" já dizia que G5 precisa
de semeadura direta e já citava `Support/CreatedAtTie.cs` como precedente, e a
task 2.4 já mandava montar o helper. O arquivo é `AgentDelegationSeed.cs`, e ele
está contado na comparação de tamanho abaixo.

## Decisions

### D1 — O alcance da detecção: qualquer caminho que volte à origem

**Decidido:** a operação é recusada quando, no grafo **como ele ficaria depois
do replace**, existir caminho de qualquer comprimento de algum alvo pedido de
volta ao próprio agente Source.

**O critério é a clareza da regra, não o custo da travessia.** A regra é uma
frase: *"um agente não pode estar num ciclo de delegação"*. Ela descreve
exatamente o mecanismo de dano — o lock de `(agente, contexto)` que a própria
cadeia vai redisputar — e não tem caso de borda para explicar.

**Alternativas consideradas:**

- **Manter só o ciclo direto** (o que existe). Recusado: é o defeito. Cobre
  auto-delegação, que é o ciclo de um salto, e deixa passar o de dois, que
  trava idêntico.
- **Ciclo de N saltos com N fixo** (p.ex. 2, "não delegue para quem delega para
  você"). Recusado por ser regra que **parece** proteção sem ser. Um ciclo de
  três saltos autotrava exatamente igual, e uma regra que bloqueia alguns
  travamentos e libera outros é pior que nenhuma, porque quem cadastrou confia
  nela. É a mesma família do guarda que cobre metade do caso e passa verde
  (convenção 15).
- **Recusar qualquer vínculo que crie caminho entre dois agentes já conectados**
  (grafo sem caminhos múltiplos). Recusado por ser mais forte que o necessário:
  um losango `A→B`, `A→C`, `B→D`, `C→D` não tem ciclo nenhum e não disputa lock
  com ninguém. Recusá-lo seria proibir composição legítima por conta de uma
  regra mais fácil de implementar. Há guarda dedicado prendendo isso (G4).

**Onde vive.** `AgentDelegationCycleDetector`, estático, em
`AgentDelegations/`, ao lado de `AgentDelegationLookup` e no mesmo idioma.
**É função pura sobre a lista de arestas** — recebe o grafo e devolve o caminho
que fecha o ciclo, ou `null` —, e não consulta banco. Duas consequências
queridas: o handler faz **uma** consulta (`SELECT SourceAgentId, TargetAgentId
FROM agent_delegations`), e a travessia ganha teste unitário sem
infraestrutura.

**Custo, com o escopo colado (convenção 22):** a consulta lê a tabela inteira de
arestas — duas colunas `uuid`. O número de referência disponível é **zero linhas
em `agent_delegations`, medido no banco de desenvolvimento em 20/09/2026**, e
esse número é sobre **volume de dado**, não sobre latência; citá-lo como
referência de tempo de resposta seria exatamente a ocorrência 3 da convenção 22.
**Gatilho de recalibração:** a primeira medição que encontre `agent_delegations`
na casa das dezenas de milhares de linhas — aí a travessia migra para CTE
recursiva, e quem fizer a medição recalibra.

### D2 — Ciclos que já existam: o `replace` basta, e não há varredura

**Decidido:** nenhuma varredura, nenhuma migration, nenhuma checagem de
startup. A revalidação acontece quando alguém salvar o agente, e isso basta.

**O motivo, em três partes:**

1. **Nenhum ciclo existe para varrer.** Zero linhas em `agent_delegations` no
   banco de desenvolvimento, e o banco do piloto será limpo antes do próximo
   deploy. Uma varredura escrita agora não teria nada para encontrar, e nunca
   teria sido exercitada contra dado real — que é a definição de comando que
   falha em silêncio (convenção 21).
2. **Depois desta change, ciclo novo não entra.** V4: o handler é o escritor
   único de `agent_delegations`.
3. **Um ciclo herdado é sempre desfazível pelas duas pontas.** A travessia roda
   contra o grafo pós-replace: remover a aresta que fecha o ciclo é aceito
   (G5). Quem herdar um ciclo não fica preso — perde só a possibilidade de
   salvar aquele agente sem desfazê-lo, que é a rejeição correta.

**Alternativas consideradas:**

- **Checagem de integridade no startup** (convenção 8 é o padrão da casa para
  registro). Recusada aqui pelos dois desfechos possíveis: recusar subir
  transforma problema de dado em indisponibilidade; só logar acrescenta um aviso
  que ninguém lê, e o `02` já registra que aviso sem destinatário não é
  cobertura. A convenção 8 vale para registro que o processo **precisa** para
  operar; um ciclo herdado não impede a API de subir.
- **Migration que apaga arestas de ciclo.** Recusada: apagar vínculo que o
  operador cadastrou é decisão dele, e uma migration que resolve o ciclo
  escolhendo uma aresta escolhe errado metade das vezes.
- **Endpoint de diagnóstico que lista ciclos existentes.** Recusado por não ter
  gatilho: não há ciclo para listar, e um endpoint sem consumidor é abstração
  prematura (convenção 2).

**O que isto assume, dito para poder ser contrariado:** que o banco do piloto
será de fato limpo antes do deploy. **Contraparte verificável** (convenção 10):
task de inspeção na janela do deploy, contando linhas de `agent_delegations` e
ciclos entre elas. Se vier diferente de zero, é achado, e a decisão de varredura
se reabre com dado.

### D3 — A proteção de runtime NÃO entra: o cadastro basta

**Decidido:** `apps/workers` não muda.

**O motivo é o inventário de V4, e ele é o argumento inteiro:** o único caminho
de escrita em `agent_delegations` no produto é
`ReplaceAgentDelegationsCommandHandler`. Fechado o cadastro, o ciclo deixa de
ter porta de entrada. Defesa em profundidade defende contra uma **segunda**
porta; enquanto não houver, ela defende contra nada e cobra o preço de existir.

**E o residual está nomeado, não varrido para baixo do tapete.** Um ciclo ainda
pode entrar por escrita direta em SQL — é como os próprios guardas de runtime o
produzem. Nesse caso: a cadeia trava no lock, e **desde
`lock-de-contexto-falha-terminal` ela termina visivelmente em `failed` em ~30 s**
(V1) em vez de ficar órfã para sempre. O residual tem dono (quem escreveu o SQL)
e sintoma visível.

**O `DelegationDepthLimit` não é a rede de segurança**, e dizer que é seria
falso (V3): o teto é 5 e a checagem roda antes do lock, então o ciclo de dois
saltos passa por ela e trava. O que o teto cobre é cadeia longa, que é outro
problema.

**Alternativas consideradas:**

- **Detectar ciclo no runtime carregando o caminho na metadata da task.** Hoje
  a metadata carrega só `DelegationDepth`; acrescentar a cadeia de agentes
  percorridos é mudança de formato de fio entre `apps/api` e `apps/workers`
  (convenção 12), com acordo a testar dos dois lados — custo alto para defender
  uma porta que não existe.
- **Consulta de grafo a cada delegação, no `AgentDelegationToolSetResolver`.**
  Recusada: paga travessia por chamada de tool para reafirmar um invariante que
  o cadastro já garante, e a rejeição chegaria ao usuário como resposta
  degradada do LLM em vez de erro de cadastro.
- **`pg_try_advisory_lock` com fallback em vez de `pg_advisory_lock`.**
  Recusada por ser outro assunto: mudaria o comportamento de **toda** disputa de
  lock, inclusive a legítima de duas mensagens do mesmo contato, que é
  justamente o caso que o desenho quer serializar. Entra, se entrar, na change
  de instrumentação, com a medição de quanto tempo uma conversa segura o lock.

**Gatilho de reabertura, observável:** o primeiro segundo escritor de
`agent_delegations` — importação de catálogo, seed de ambiente, endpoint de
cópia de agente. Quem o acrescentar reabre esta decisão.

### D4 — O guarda muda de camada, e é `apps/api` que afirma a suficiência

**Decidido:** `Probe_CycleAB_A` **não** volta como teste de `apps/workers`.

**Por quê.** Convenção 15, segunda forma: *o guarda tem que reprovar no
componente que a correção vai tocar*. A correção é em `apps/api`; um teste de
runtime que semeia o grafo com `SeedAgentDelegationAsync` escreve direto no
banco e **contorna o cadastro** — reprovaria antes e depois da correção,
exatamente o erro registrado em `dedupe-global-nome-de-tool`. E V2 mostra que
ele reprovaria hoje por outro motivo ainda: o texto que ele afirma mudou.

**O que restaura o cenário.** `Probe_CycleAB_A` semeava exatamente duas arestas:
`A→B` e `B→A`. O guarda G1 afirma que a segunda **não pode ser criada pela
API** — é o mesmo cenário, na camada onde ele decide alguma coisa. A suficiência
da defesa de cadastro é a conjunção de G1 com o inventário de V4, e V4 é
afirmação estática: nenhum teste a prova, e é por isso que ela está escrita com
o comando que a mediu e com gatilho de reabertura (D3), em vez de citada de
memória.

### D5 — A mensagem de erro nomeia o caminho, por nome de agente

**Decidido:** o detector devolve o caminho como `IReadOnlyList<Guid>`; o handler
resolve os nomes numa consulta; o endpoint formata `A → B → C → A` no
`ValidationProblem` sob a chave `targetAgentIds`, no mesmo molde das demais
rejeições do endpoint.

**Por quê:** a rejeição precisa dizer **qual** vínculo desfazer, e o vínculo a
desfazer pode estar em outro agente — num ciclo `A→B→C→A` salvo a partir de A, a
aresta `C→A` é que o operador talvez queira remover. Sem o caminho, a mensagem
seria "existe um ciclo", o que manda o operador procurar. Ids na mensagem não
resolvem: quem lê a tela lê nome.

### D6 — Auto-delegação mantém a checagem e a mensagem próprias

A regra geral de D1 já contém a auto-delegação (ciclo de um salto). A checagem
explícita de `ReplaceAgentDelegationsCommandHandler.cs:33-36` **fica**, antes da
travessia, por duas razões: a mensagem *"Um agente não pode delegar para si
mesmo."* é melhor que `A → A` formatado pela regra geral, e o cenário de spec
que a prende continua valendo palavra por palavra. **É a Decision 2 daquele
design, e ela não é contrariada** — só deixa de ser a única.

### D7 — A travessia roda contra o grafo pós-replace

O handler carrega todas as arestas, **remove em memória as de saída do Source** e
acrescenta as pedidas; só então percorre. Se percorresse o grafo atual, uma
edição que *desfaz* um ciclo seria recusada por causa do ciclo que ela está
desfazendo — a operação é `replace`, e ignorar isso transformaria a correção em
armadilha. G5 é o guarda dedicado, e ele **passa em `HEAD`** de propósito: é de
regressão contra a correção errada, mesmo papel de
`UnlockFailingAfterCompletion_LeavesTaskCompleted_WithArtifactPreserved` na
change anterior.

### D8 — A tela de vínculos é achado sequenciado, e a Decision 3 do handler é corrigida

**A tela.** `AgentDelegationsTab.tsx:68-74` responde a qualquer erro com *"Não
foi possível atualizar as delegações do agente. **Tente novamente**."* — o
`ApiError` já carrega `problem.errors`, e a tela o descarta. Para uma recusa por
ciclo isso afirma mais do que o sistema sabe (convenção 13): tentar de novo
nunca vai funcionar. **Convenção 1 decide:** backend antes de UI, e isto é
achado a sequenciar, não escopo a puxar.

- **Gatilho:** cumprido no instante em que esta change subir.
- **Posição:** change própria de `apps/frontend`, **imediatamente depois desta**
  — antes da change de instrumentação de delegação.

O requisito *"Nenhuma detecção de ciclo de delegação na interface"*
(`agent-delegation-binding-ui`) **continua verdadeiro** e não entra no delta: a
interface segue não detectando ciclo, e o cenário afirma o comportamento da
interface, não o desfecho da chamada. É a tela de erro que fica devendo, e é ela
que a change sequenciada corrige.

**A Decision 3 do handler** (`ReplaceAgentDelegationsCommandHandler.cs:29-32`)
passa a registrar a causa real (convenção 9): o que estava escrito como "fora de
escopo desta camada" tinha mecanismo de dano não medido; agora tem, medido com
quatro instâncias de worker, e a camada é esta porque é a única com escritor
único.

### D9 — O escopo 2 (`AgentDeactivationTests`) entra nesta change

**Decidido:** entra aqui, não como change própria sequenciada antes.

**Três motivos, nesta ordem:**

1. **A posição registrada é esta.** O `02` diz *"antes da próxima change que
   tocar `apps/api`"*, e esta é essa change. Se não entrar, a posição registrada
   vira ficção — a família de defeito que o repositório já registra três vezes.
2. **A verificação desta change depende dela.** Toda rodada de guardas aqui lê a
   suíte de `apps/api`, e uma reprovada conhecida no meio é exatamente o ruído
   que a convenção 19 diz custar caro: quem lê o vermelho precisa refazer os
   três passos de discriminação para separar "já estava assim" de "foi a
   change".
3. **Uma change própria custaria um jogo inteiro de artefatos** para uma troca
   de asserção sem mudança de comportamento de produção.

**A correção é por-agente, não `Clear()` entre testes.** Cada teste cria o seu
próprio agente; a asserção passa a ser *"nada foi publicado **para este
agente**"*. Um `Clear()` no início de cada teste também funcionaria hoje, e foi
recusado: ele depende de os testes da classe não rodarem em paralelo, que é uma
propriedade do runner e não do teste — mesma família do guarda cujo critério é
uma ordem que o banco às vezes já produz sozinho (convenção 15, quinta forma). A
asserção por-agente é independente de ordem **por construção**.

`SendMessageProviderRejectionTests` compartilha o formato
(`AgentProviderRejectionFixture` com o mesmo `FakeTaskJobPublisher` acumulando),
mas os seus dois testes só afirmam `Assert.Empty` e nenhum publica — não reprova
hoje. **Fica fora**, porque mexer nele agora seria corrigir um defeito sem
guarda vermelho, e é achado a registrar com gatilho: o primeiro teste que
publique de verdade naquela classe.

## A rodada de guardas (convenção 15)

Cada guarda roda contra `HEAD` **antes** de existir a correção, e o resultado é
registrado com a causa atribuída. A coluna `HEAD` é a **projeção**; a tabela
entregue com os valores medidos é task de implementação.

| # | Guarda | Onde | `HEAD` esperado |
|---|---|---|---|
| G1 | par bidirecional `A→B` + `B→A` é recusado — a forma exata de `Probe_CycleAB_A` | `apps/api`, endpoint | 🔴 (hoje há teste verde afirmando o contrário) |
| G2 | ciclo indireto `A→B→C→A` é recusado | `apps/api`, endpoint | 🔴 (idem) |
| G3 | ciclo de quatro saltos `A→B→C→D→A` é recusado | `apps/api`, endpoint | 🔴 |
| G4 | losango `A→B`, `A→C`, `B→D`, `C→D` continua aceito | `apps/api`, endpoint | 🟢 **de propósito** |
| G5 | remover a aresta que fecha um ciclo herdado é aceito | `apps/api`, endpoint | 🟢 **de propósito** |
| G6 | a recusa por ciclo é atômica: os vínculos anteriores do Source ficam intactos | `apps/api`, endpoint | 🔴 |
| G7 | o 400 nomeia o caminho, por nome de agente | `apps/api`, endpoint | 🔴 |
| G8 | auto-delegação continua com a mensagem própria | `apps/api`, endpoint | 🟢 **de propósito** |
| G9 | o detector acha ciclo de N saltos e devolve o caminho | `apps/api`, unitário | arquivo novo — sem `HEAD` |
| G10 | os três testes de `AgentDeactivationTests` passam com a classe inteira rodando | `apps/api` | 🔴 (2/3 medido hoje) |

**G4, G5 e G8 passam em `HEAD` de propósito, e isso está dito porque é
exatamente o que a convenção 15 avisa que engana.** São guardas de regressão
contra a **correção errada** — detecção forte demais (G4), travessia contra o
grafo pré-replace (G5), regra geral engolindo a mensagem própria (G8) —, não
contra o defeito. Um guarda verde nos dois lados não prova nada sobre o defeito,
e listá-lo sem dizer isso é o que produz cobertura aparente.

**G5 monta o ciclo herdado por semeadura direta no banco nas DUAS rodadas**, a
de `HEAD` e a de depois da correção. Montá-lo pela API funcionaria em `HEAD` e
quebraria depois da correção **por falha de setup** — a montagem passa a devolver
400 —, e G5 ficaria vermelho pelo motivo errado em vez de pela propriedade que
afirma. Semeadura direta é a única forma que produz o mesmo estado nos dois
lados. Precedente na casa: `Support/CreatedAtTie.cs` já usa `ExecuteSqlRawAsync`
para montar estado que a API não produz.

## Projeção de tamanho (convenção 18)

Feita **agora** — depois de ler o código e fechar a verificação, **antes** de
escrevê-lo. A task de fechamento apenas **compara** projetado contra entregue e
registra a divergência; ela não reprojeta.

### A régua desta superfície, medida antes de projetar, com o escopo colado (convenção 22)

- **1 sítio de instanciação** de `ReplaceAgentDelegationsCommandHandler` — a
  própria definição, em `apps/api/src/Buteco.Api/AgentDelegations/Commands/ReplaceAgentDelegations/`.
  Medido em 20/09/2026 contra `HEAD` `1ba84ec`. Contra **13** sítios de
  `AgentExecutionService` em `apps/workers`: Mediator resolve por DI
  (`Program.cs:32`) e os testes chegam pelo endpoint HTTP, nunca pelo construtor.
  **Acrescentar dependência ao handler custa zero arquivos de teste.**
  *Gatilho de recalibração:* o primeiro teste que instancie o handler direto.
- **2 arquivos de teste** chamam `PUT /agents/{id}/delegations` em
  `apps/api/tests/Buteco.Api.Tests/` — `AgentDelegationEndpointsTests.cs` e
  `AgentKnowledgeBindingEndpointsTests.cs:307-322`. Mesmo `HEAD` e data.
  Só o primeiro precisa mudar: o segundo vincula um alvo recém-criado, sem
  ciclo, e segue verde. *Gatilho de recalibração:* novo arquivo de teste que
  chame o endpoint.
- **30 arquivos** usam `ApiFactoryFixture`, e esse número **não** é a régua
  desta change — é o tamanho da suíte, não o alcance da mudança. Está escrito
  para não ser confundido com o de cima, que é o erro que a convenção 22 nomeia.

### Produção (`apps/api/src`)

| arquivo | lógica | comentário | total |
|---|---|---|---|
| `AgentDelegationCycleDetector.cs` (**criado**) | ~30 | ~35 | ~65 |
| `ReplaceAgentDelegationsCommandHandler.cs` | ~12 | ~32 | ~44 |
| `ReplaceAgentDelegationsResult.cs` | ~6 | ~8 | ~14 |
| `AgentDelegationEndpoints.cs` | ~9 | ~4 | ~13 |
| **total** | **~57** | **~79** | **~136** |

1 criado, 3 modificados.

### Teste (`apps/api/tests`)

| arquivo | acrescentadas | removidas |
|---|---|---|
| `AgentDelegationCycleDetectorTests.cs` (**criado**, G9) | ~90 | — |
| `AgentDelegationEndpointsTests.cs` (G1–G8 + 2 testes invertidos + helper de seed) | ~174 | ~20 |
| `AgentDeactivationTests.cs` (G10, escopo 2) | ~20 | ~3 |
| **total** | **~284** | **~23** |

1 criado, 2 modificados.

### Registro

| arquivo | acrescentadas |
|---|---|
| `01-ARQUITETURA_E_CONVENCOES.md` (escopo 3) | ~12 |

### As duas razões, projetadas com o motivo

- **teste:produção ≈ 2,1:1** (~284 : ~136). Bem abaixo do **7:1** de
  `lock-de-contexto-falha-terminal`, e a diferença é estrutural, não de rigor:
  lá cada guarda montava um host de worker e um cenário concorrente com
  Testcontainers; aqui são chamadas HTTP contra uma fixture que já existe.
- **comentário:lógica em produção ≈ 1,4:1** (~79 : ~57). Abaixo do **2,3:1**
  (111:48) da change anterior, e é previsão consciente: aquela entregava **três**
  registros de mecanismo; esta entrega **um** — a Decision 3 corrigida. A regra
  candidata registrada no `02` (*projetar por "linhas de lógica" subestima em
  change cujo entregável inclui registro de decisão ou de mecanismo*) se aplica
  em grau menor, e é por isso que o comentário ainda supera a lógica.

**As duas direções em que esta projeção pode errar**, ditas antes para a
comparação valer alguma coisa:

- **para cima**, se a formatação do caminho por nome (D5) exigir mais que a
  consulta única projetada — p.ex. um caminho que atravesse agente removido
  concorrentemente;
- **para baixo**, se G3 e G9 se sobrepuserem o bastante para que o guarda
  unitário absorva o de quatro saltos, tirando ~25 linhas do arquivo de
  endpoint.

## Comparação: projetado contra entregue (task 6.1)

Medido em 20/09/2026, depois de a implementação fechar. **A projeção acima não
foi mexida** — ela é o artefato comparado, e reescrevê-la invalidaria a medição.

### Produção (`apps/api/src`)

| arquivo | projetado (lógica + comentário) | entregue |
|---|---|---|
| `AgentDelegationCycleDetector.cs` (criado) | ~30 + ~35 = ~65 | **58 + 78 = 136** |
| `ReplaceAgentDelegationsCommandHandler.cs` | ~12 + ~32 = ~44 | **18 + 29 = 47** |
| `ReplaceAgentDelegationsResult.cs` | ~6 + ~8 = ~14 | **8 + 10 = 18** |
| `AgentDelegationEndpoints.cs` | ~9 + ~4 = ~13 | **8 + 4 = 12** |
| **total** | **~57 + ~79 = ~136** | **92 + 121 = 213** |

### Teste (`apps/api/tests`)

| arquivo | projetado | entregue |
|---|---|---|
| `AgentDelegationCycleDetectorTests.cs` (criado) | ~90 | **126** |
| `Support/AgentDelegationSeed.cs` (criado) | ~18, contado dentro do arquivo de endpoint | **51**, arquivo próprio |
| `AgentDelegationEndpointsTests.cs` | ~174 / −20 | **159 / −17** |
| `AgentDeactivationTests.cs` | ~20 / −3 | **29 / −4** |
| **total acrescentadas** | **~284** | **365** |

### As duas razões

| razão | projetada | entregue |
|---|---|---|
| teste : produção | 2,1 : 1 | **1,57 : 1** |
| comentário : lógica, em produção | 1,4 : 1 | **1,32 : 1** |

### A divergência, com a direção e a causa

**Volume: +57% em produção, para cima.** E a causa não é nenhuma das **duas
direções de erro nomeadas de antemão**, que estavam as duas erradas:

- *"para cima, se a formatação do caminho por nome exigir mais que a consulta
  única projetada"* — **não aconteceu**: são 5 linhas e uma consulta, como
  projetado;
- *"para baixo, se G3 e G9 se sobrepuserem"* — **não aconteceu**: os dois
  existem, distintos, e G3 é justamente o que pega a regra de "N saltos fixo".

O desvio inteiro está no detector (~65 projetadas, 136 entregues), e a causa é
**contagem de componentes**: ele virou **duas** funções públicas em vez de uma.
`BuildGraph` foi projetada como ~12 linhas de lógica dentro do handler e saiu de
lá para ser função pura testável isolada — e o handler **não encolheu** em
troca (18 entregues contra 12 projetados), porque o que ficou lá foi a consulta
e a resolução de nomes. Os três registros que o detector carrega (por que
travessia completa, por que não CTE com o número e o gatilho de recalibração
colados, por que pós-replace) foram projetados como um.

**Mistura: acertou.** 1,32:1 entregue contra 1,4:1 projetado. **É a terceira
ocorrência da família "o comentário é o produto", e é de sinal contrário às duas
primeiras** — aqui a regra candidata foi aplicada *antes* de escrever o código e
a dimensão de comentário veio certa. Foi promovida ao `01` (convenção 18) com
essa evidência junto, porque evidência preventiva é mais forte que três falhas.

**A lição operacional, que é o que a próxima projeção usa:** perguntar *quantos
registros de mecanismo a change entrega* projeta a **proporção** bem; o que
continua não sendo projetável de cabeça é a **contagem de componentes**, e é ela
que carrega o volume.

## Risks / Trade-offs

- **[Operador com ciclo cadastrado hoje perde a capacidade de salvar aquele
  agente sem desfazê-lo]** → É a rejeição correta, e a saída existe pelas duas
  pontas (D2.3). Contraparte verificável: G5.
- **[O banco do piloto pode não ser limpo antes do deploy, e D2 depende
  disso]** → Task de inspeção na janela do deploy, contando linhas e ciclos em
  `agent_delegations`. Vindo diferente de zero, a decisão de varredura reabre
  com dado em vez de suposição.
- **[A tela reporta a recusa como "Tente novamente"]** → Real e conhecido
  (D8). Mitigação nesta change: a mensagem do 400 já carrega o caminho, então a
  correção da tela é de apresentação e não precisa de backend novo. Mitigação
  completa: a change de `apps/frontend` imediatamente seguinte.
- **[Detecção forte demais quebrar composição legítima]** → G4, com o losango
  explícito.
- **[Quem restaurar `Probe_CycleAB_A` como está vai lê-lo reprovar por texto e
  chamar de regressão]** → V2 registra a mudança de texto e de tempo, e D4
  registra por que o guarda não volta àquela camada.
- **[O escopo 2 misturado ao escopo 1 tornar o diff ilegível]** → Os três
  escopos estão declarados no `proposal.md`, e nenhum arquivo é tocado por mais
  de um deles: escopo 2 vive inteiro em `AgentDeactivationTests.cs`, escopo 3
  inteiro no `01`.

## Migration Plan

Sem migration de banco e sem passo de deploy próprio: a detecção é validação de
cadastro. O deploy é o normal de `apps/api`.

**Rollback:** reverter o commit restaura o comportamento anterior sem nenhum
efeito sobre dado — nenhuma linha é criada, alterada ou apagada por esta change.

**O que quem opera vai ver:** uma tentativa de salvar delegação que feche ciclo
passa a responder 400 onde antes respondia 200. Com `agent_delegations` em zero
linhas, não há cadastro existente que quebre no deploy.

## Open Questions

- **A tela deve impedir a seleção que fecha ciclo, ou só reportar bem a recusa
  do servidor?** É pergunta de produto e pertence à change de `apps/frontend`
  sequenciada em D8 — detectar no cliente exige que a tela conheça o grafo
  inteiro, que é dado que ela não busca hoje. **Não bloqueia esta change**: o
  backend recusa nos dois desfechos.
