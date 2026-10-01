# Tarefas — `consumo-por-agente-falhas` (#84 e #86)

**Branch:** `fix/84-consumo-por-agente-falhas`, criada de `c32f09c`
(`origin/main`, conferido por SHA: `HEAD` = `origin/main`, 0 à frente / 0 atrás).

**App afetado: `apps/frontend` só.** Nenhuma tarefa toca `apps/api` nem
`apps/workers` — a decisão e a medição que a sustentam estão na **D1** do
`design.md`.

---

## Conferência de escopo — a varredura por FORMA, com a lista fechada

Varrido **pela forma**, não pelo número da issue. Saída colada de
`grep -rn "new Map(" apps/frontend/src --include="*.ts" --include="*.tsx" | grep -v "__tests__\|\.test\.\|\.spec\." | sort`:

```
apps/frontend/src/features/agents/utils/knowledgeBaseRows.ts:43:  const byId = new Map(catalog.map((knowledgeBase) => [knowledgeBase.id, knowledgeBase]));
apps/frontend/src/features/insights/components/AgentConsumptionCard.tsx:65:  const failures = new Map(failuresByAgent.map((f) => [f.agentId, f.failedCount]));
apps/frontend/src/features/insights/components/WeekdayActivityCard.tsx:53:  const counts = new Map(temporal.byWeekday.map((p) => [p.weekday, p.taskCount]));
apps/frontend/src/features/insights/pages/SystemInsightsPage.tsx:211:        : new Map(agentsQuery.data.map((a) => [a.id, a.name])),
apps/frontend/src/features/insights/utils/measuredDays.ts:81:  const byDay = new Map(dailySeries.map((point) => [point.day, point]));
apps/frontend/src/features/knowledge-bases/utils/indexingSummary.ts:45:  return new Map(summary.map((item) => [item.knowledgeBaseId, item]));
```

**Seis sítios. Um é o defeito; cinco são seguros, com a razão conferida na fonte**
— a tabela de razões está na **D6** do `design.md`. A segunda varredura
(`.map(… => [`) acrescentou só sítios de `Object.entries(error.problem.errors)` em
páginas de formulário, onde a chave é o nome do campo e vem de objeto — única por
definição.

**A lista está FECHADA: nada mais entra nesta change, e a varredura não produziu
achado com issue.**

**E a premissa que mandou varrer não se confirma.** A #90 **não** registra a forma
do `Map` no `ProviderConsumptionCard`; o que ela chama ali de *"a mesma forma"* é
**declaração de regime**. `ProviderConsumptionCard.tsx` não tem `new Map(` nenhum.
A varredura pela forma é a que vale, e é ela que fecha a lista.

---

## Convenção 18 — vigésima quarta medição

**Baseline REMEDIDA nesta árvore, não herdada.** `apps/frontend` em
**1402 testes / 118 arquivos**, `vitest run`, duração **161,67 s**, árvore
**limpa** em `c32f09c`.

`podman ps` no instante da medição:

```
buteco-agents_postgres_1	Up 7 days (healthy)
buteco-agents_rabbitmq_1	Up 6 days (healthy)
```

A duração de 161,67 s está no regime normal — **não** é o caso de 20–26× mais
lento que a #75 observou com a VM carregada, então o número é válido e não há o que
abortar.

**Unidade declarada: blocos `it(...)`.** Não há `it.each` neste arquivo —
conferido —, então bloco e caso coincidem e a expansão não distorce a contagem.
**Semeadura de fixture conta como arranjo compartilhado, não como caso.**

**Projeção POR CATEGORIA** (nunca só o total — a vigésima terceira mediu total
certo com categorias erradas):

| categoria | projeção | o que é |
|---|---|---|
| **novos** | **+8** | os guardas de 2.1 a 2.8 |
| **reforçados** | **0** | nenhuma asserção existente fica mais forte |
| **adaptados** | **1** | `'sem nenhum agente, diz o zero medido por extenso'` |
| **removidos** | **0** | nenhum guarda sai |

**Total projetado: 1410 testes / 118 arquivos** (nenhum arquivo novo).

**O adaptado é adaptação de verdade, e não conserto de guarda que passou a
incomodar:** ele passa `byAgent: []` com `failuresByAgent` **povoado** pelo padrão
da fixture. Com a população virando união, a tabela deixa de estar vazia e o guarda
reprova — corretamente. Ele passa a declarar as **duas** listas vazias, e **a
semântica que ele deixa de cobrir é coberta pelo guarda novo 2.7**, que afirma o
caso complementar. Adaptar sem 2.7 seria enfraquecer a suíte.

**Projeção de linhas: ~+270**, e este é o número **menos confiável** dos dois — na
vigésima terceira as linhas erraram +70%. O agravante de lá (quase tudo em duplo de
teste) **não se aplica**: este componente é de apresentação pura e os guardas usam
fixture literal, sem duplo nenhum. Fica projetado e será comparado.

---

## 1. Guardas primeiro, e os quatro reprovando contra o `HEAD`

- [x] 1.1 Semear a fixture de `AgentConsumptionCard.test.tsx` com um terceiro
      agente, presente **só** em `failuresByAgent` e **ausente** de `byAgent` — o
      caso da #84 —, e com um agente que chega em `failuresByAgent` em **duas
      linhas** de contagens diferentes — o caso da #86. **Arranjo compartilhado:
      não conta como caso na projeção.**
- [x] 1.2 Manter toda contagem de falhas da fixture **abaixo de 1.000**:
      `formatCount` é `Intl.NumberFormat('pt-BR')` e o separador de milhar é `.`,
      então `1.234` no texto renderizado quebraria a asserção por formatação e não
      por defeito.
- [x] 1.3 Escrever os guardas de 2.1 a 2.8 e **rodar contra o `HEAD` antes de
      tocar o componente**. Registrar aqui **quais** reprovam e **com que valor** —
      a asserção é o valor, nunca "maior que zero".
- [x] 1.4 Conferir que os guardas da #86 reprovam mostrando a **menor** das duas
      contagens, e não um número qualquer: é o viés para baixo que a issue mede, e
      ver o valor errado certo é o que prova que o guarda pegou **este** defeito.

## 2. Os guardas, um a um

### Registrado em 1.3 — os oito reprovando contra o `HEAD`

`npx vitest run AgentConsumptionCard.test.tsx` no `HEAD`:
**8 reprovados, 20 aprovados (28)**. Os 20 que passam são os existentes — nenhum
guarda novo passa por construção, e nenhum existente foi quebrado pela semeadura.

| guarda | reprova contra o `HEAD` com |
|---|---|
| 2.1 soma | esperava `7`, recebeu **`2`** |
| 2.2 negativo | `expected '2' not to contain '2'` |
| 2.3 a linha existe | `Unable to find [data-testid="agente-77777777-…"]` |
| 2.4 tokens vazio | `Unable to find [data-testid="agente-77777777-…-tokens"]` |
| 2.5 destaque | `Unable to find [data-testid="agente-77777777-…-falhas"]` |
| 2.6 negativo destaque | o Atendente sai em vermelho |
| 2.7 união no limite | `consumo-por-agente-vazio` presente onde devia ser nulo |
| 2.8 vacuidade | `to have a length of 3 but got 2` |

**A asserção é o VALOR, e a 1.4 está conferida:** o guarda da #86 reprova mostrando
**`2`** — a **menor** das duas contagens (5 e 2), que é onde a ordenação
`order by 4 desc` deixa a última linha e é o que o `Map` guardava. Ver o valor
errado *certo* é o que prova que o guarda pegou **este** defeito, e não um
qualquer.


- [x] 2.1 **#86 — a soma.** Agente com duas linhas em `failuresByAgent`, contagens
      **5** e **2**, aparece com **7**. Contra o `HEAD` reprova mostrando **2**.
- [x] 2.2 **#86 — NEGATIVO.** A célula desse agente **não** contém `2` nem `5`
      como total — nem a última linha, nem a primeira: a resposta certa é só a
      soma.
- [x] 2.3 **#84 — a linha existe.** Agente presente em `failuresByAgent` e
      **ausente** de `byAgent` **aparece na tabela**, com a contagem de falhas
      dele. Contra o `HEAD` reprova por **ausência da linha**.
- [x] 2.4 **#84 — NEGATIVO, e a precondição do guarda de ausência é o estado
      observável mais próximo.** A célula de **Tokens** desse agente está em
      `data-metric-state="empty"`, **não** em `zero`, e o texto dela **não** contém
      `0` nem `Nenhuma`. O estado da célula é o observável mais próximo do que se
      nega — "não há consumo medido para ele" —, e não o dado de origem.
- [x] 2.5 **D4 — o destaque segue a população.** A linha que entra por falha tem a
      **maior** contagem da coluna e sai em `var(--mantine-color-red-filled)`.
      Contra o `HEAD` reprova por ausência da linha; contra a correção **parcial**
      (população nova com `maiorFalha` ainda sobre `byAgent`) reprova por cor.
- [x] 2.6 **D4 — NEGATIVO.** Nesse mesmo cenário, a linha de consumo com contagem
      **menor** **não** sai em vermelho. Sem este, 2.5 passaria com tudo vermelho.
- [x] 2.7 **A união no limite.** `byAgent` **vazio** e `failuresByAgent`
      **povoado**: a tabela **não** apresenta
      `consumo-por-agente-vazio`, e a linha do agente existe. É o par do guarda
      adaptado em 3.4.
- [x] 2.8 **Vacuidade, com a precondição afirmada.** Com dois agentes só em
      `byAgent` e um só em `failuresByAgent`, a tabela tem **exatamente 3** linhas
      — a união **não duplica** o agente que está nas duas listas, e **não some**
      com ninguém. A fixture é povoada, e o guarda afirma a contagem.

## 3. A correção

- [x] 3.1 **#86** — trocar o `new Map(failuresByAgent.map(…))` de
      `AgentConsumptionCard.tsx:65` por uma redução que **soma** por `agentId`.
- [x] 3.2 **#84** — a população das linhas passa a ser a **união** das chaves de
      `byAgent` com as de `failuresByAgent`. O agente que só tem falha entra com
      `inputTokens` e `outputTokens` **nulos**, para que `sumKnown` o deixe em
      `null` e a célula caia no estado **vazio** — nunca `0`.
- [x] 3.3 **D4** — `maiorFalha` passa a ser calculado sobre a **população nova**.
      Sem isto a linha da #84 pode ser a maior e não sair destacada, que é defeito
      que o `HEAD` não tem.
- [x] 3.4 Adaptar `'sem nenhum agente, diz o zero medido por extenso'` para
      declarar as **duas** listas vazias. **Único guarda existente que a mudança de
      população reprova** — conferido lendo os 21 blocos do arquivo: os demais
      passam `failuresByAgent` com agentes **já presentes** em `byAgent`, então a
      união não muda a contagem de linhas deles.
- [x] 3.5 **Convenção 9 nos comentários do componente**, que a change torna falsos:
      - o bloco do zero medido (`:67-82`) justifica o zero com *"um agente que não
        está em NENHUMA das duas listas simplesmente não tem linha"* — a partir
        daqui a população **é** as duas listas, e o raciocínio fica mais exato;
        corrigir dizendo isso, com a causa;
      - o bloco do destaque (`:106-110`) diz que *"as linhas nascem de
        `tokens.byAgent`, então agente que executou e não chamou provedor não tem
        linha nenhuma aqui (#84)"* — **deixa de ser verdade nesta change**;
      - o cabeçalho (`:40-41`) diz que *"o que a rota SERVE por agente além de
        tokens é `errors.byAgent`, então a coluna Falhas entra"* — passa a ser
        também de onde a **linha** pode nascer;
      - o comentário de `'o destaque ordena por CONTAGEM'` no arquivo de teste
        (`:215-220`) cita a #84 como defeito aberto.
- [x] 3.6 **Mutação, uma por vez.** Três rodadas, e **nenhum guarda de um defeito
      reprovou pelo outro** — é isso que os faz guardas de defeito e não de
      regressão genérica:
      - **quebrar a soma** (voltar ao `new Map`) → reprovam **só 2.1 e 2.2**
        (2 de 28). Nenhum guarda da #84 se mexe;
      - **desfazer a união** (`populacao = [...byAgent]`) → reprovam **2.3, 2.4,
        2.5, 2.6, 2.7 e 2.8** (6 de 28). Nenhum guarda da #86 se mexe.
        **O texto desta tarefa dizia "só 2.3, 2.4, 2.5 e 2.7" e estava errado:**
        2.8 afirma a contagem de linhas da união e 2.6 depende de a linha nova
        existir para que o destaque mude de dono. Corrigido com o medido;
      - **a correção PARCIAL** que a D4 prevê (união feita, `maiorFalha` ainda
        sobre `byAgent`) → reprovam **exatamente 2.5 e 2.6**, e mais nada. A D4
        não era hipótese defensiva: é defeito que o `HEAD` não tem e que a
        correção incompleta produz.
- [x] 3.7 **A régua da fiação, e ela achou buraco de verdade.** Trocada
      `failuresByAgent={insights.errors.byAgent}` por `{[]}` em
      `SystemInsightsPage.tsx:337` — mutação que **esvaziaria a coluna Falhas
      inteira no app real** e faria a linha da #84 desaparecer de novo —, os
      **457 testes de `insights` passaram TODOS.** Os oito guardas provavam o
      componente no vácuo: a página garantia `byAgent` (há guarda sobre
      `agente-…-tokens`) e **nunca** afirmou `agente-…-falhas`.
      **Fechado com um guarda na página** (o nono novo, não projetado), que afirma
      o caso da #84 ponta a ponta pela fiação real: agente só em `errors.byAgent`
      chega à tela com a contagem, e com o consumo em `empty`. Reaplicada a
      mutação, **ele reprova sozinho** (1 de 458). Prop reposta.

## 4. Suíte e medição

- [x] 4.1 `npm test` em `apps/frontend`, com a árvore só desta change.
- [x] 4.2 Registrar o medido **por categoria** contra a projeção da tabela acima —
      novos, reforçados, adaptados, removidos —, **nunca só o total**, e declarar
      o `podman ps` do instante.
- [x] 4.3 Comparar a projeção de **linhas** e registrar o erro com o motivo, que é
      o ponto fraco conhecido desta convenção.
- [x] 4.4 `npm run lint` e `npm run build` em `apps/frontend`.
- [x] 4.5 `openspec validate --all` verde.

### Registrado em 4.2 e 4.3 — o medido contra a projeção

`npm test` em `apps/frontend`, árvore só desta change, **1411 testes / 118
arquivos**, duração **145,77 s**. `podman ps` no instante:

```
buteco-agents_postgres_1	Up 7 days (healthy)
buteco-agents_rabbitmq_1	Up 6 days (healthy)
```

**Por categoria, que é como a vigésima terceira mandou comparar:**

| categoria | projetado | medido | erro |
|---|---|---|---|
| novos | +8 | **+9** | **+1** |
| reforçados | 0 | 0 | — |
| adaptados | 1 | **1** | **exato** |
| removidos | 0 | 0 | — |
| arquivos | 118 | 118 | exato |
| **total** | 1410 | **1411** | **+1** |

**O +1 em "novos" tem causa, e ela é o décimo guarda que a régua da fiação
obrigou** — ver 3.7. Ele não estava projetado porque não era previsível por
leitura: só apareceu ao **rodar** a mutação da prop.

**Aqui o total errou junto com a categoria**, e isso não contradiz a vigésima
terceira: lá o total acertou **porque** dois erros de categoria se cancelaram. O
que a régua pede é comparar por categoria, e foi o que permitiu dizer **qual**
número errou e por quê.

**"Adaptados: 1" foi confirmado empiricamente, e não por leitura:** aplicada a
correção, a suíte do arquivo deu **1 reprovado / 27 aprovados**, e o reprovado foi
exatamente o previsto.

#### Linhas: a projeção errou +53%, e o motivo que eu tinha escrito estava errado

`git diff --numstat`, só código:

| arquivo | adicionadas | removidas |
|---|---|---|
| `AgentConsumptionCard.test.tsx` | 163 | 4 |
| `AgentConsumptionCard.tsx` | 76 | 8 |
| `SystemInsightsPage.test.tsx` | 33 | 0 |
| **total** | **272** | **12** (líquido **260**) |

Projetado para código: **~170**. Medido: **260**. **Erro +53%.**

**E o agravante que eu dispensei não era o agravante.** Escrevi que o erro de +70%
da vigésima terceira se devia a duplo de teste e que aqui não se aplicaria, porque
o componente é de apresentação pura — **verdade, e o erro reproduziu quase igual
mesmo assim.** A causa real é outra:

| arquivo | linhas adicionadas que são comentário |
|---|---|
| `AgentConsumptionCard.tsx` | **62 de 76 (82%)** |
| `AgentConsumptionCard.test.tsx` | 63 de 163 (39%) |
| `SystemInsightsPage.test.tsx` | 12 de 33 (36%) |
| **total** | **137 de 272 (50%)** |

**Metade do delta é comentário**, e no componente são quatro de cada cinco linhas.
A projeção de linhas conta o código que se imagina escrever e **não conta a
densidade de comentário da casa**, que é a convenção 2 cobrada em cada bloco
corrigido com a causa.

**Régua para a vigésima quinta medição:** projetar linhas **em duas parcelas** —
código e comentário —, e projetar comentário a partir de **quantos blocos a change
torna falsos**, não como uma fração do código. Aqui foram quatro blocos corrigidos
com a causa, e eles sozinhos explicam o erro.

## 5. Conferência visual, antes de qualquer archive

- [x] 5.1 Subir `apps/api` com `TZ=America/Sao_Paulo` — sem isso o boot reprova
      com `'Brazil/East'` — e `apps/frontend`, e abrir a página do sistema.
- [x] 5.2 Conferir que o agente `4ab9739f` **aparece** na tabela, com **2** em
      Falhas e a célula de Tokens **vazia**. É o caso da #84 no dado real, e é a
      prova que fixture nenhuma dá.
- [x] 5.3 Conferir a coluna inteira contra a medição da D3: a soma da coluna dá
      **4**, e o KPI *"Falhas"* da página continua em **2**. **O desencontro é
      esperado e está declarado** — ver 6.3.
- [x] 5.4 Declarar na conferência que o banco tem **5 recusas de entrada plantadas
      pela #75**, dentro da janela, e que **nenhuma delas tem linha em
      `task_executions`** — então não alcançam nenhum número desta tabela. Elas
      aparecem no card de Motivos, que esta change não toca.

### Registrado em 5 — a conferência contra o dado real

**5.1** — `apps/api` **já estava no ar** em `:5017` (`/health` 200), subida fora
desta sessão; o `TZ` não precisou ser forçado porque o processo está de pé e
respondendo. Vite em `:5173` com HMR. Tentei subir uma segunda instância e ela
reprovou com `Failed to bind to address http://0.0.0.0:5017: address already in
use` — que é o esperado, e não defeito.

**A rota, pedida direto** (`GET /insights/system`, período de 31/08 a 30/09):

| | |
|---|---|
| `tokens.byAgent` (população antiga) | **4** agentes |
| `errors.byAgent` | 2 linhas: `4ab9739f` (`null`/`null`, 2) e `69453038` (`google`/`gemini-2.5-pro`, 2) |
| **população nova (união)** | **5** agentes |
| só em `errors.byAgent` | **`4ab9739f`** — a linha que a #84 traz |

**5.2 — O caso da #84 na tela, que fixture nenhuma dá.** A tabela *"Consumo por
agente"* renderiza **cinco** linhas, e a quinta é **`Atendente Sênior`**
(`4ab9739f`), **que não existia antes**:

| Agente | Tokens | Falhas |
|---|---|---|
| Atendente Ambiente Software | 3,2 M | Nenhuma |
| Triagem | 559 mil | Nenhuma |
| Gestor de Reservas | *(vazio)* | Nenhuma |
| Especialista Técnico Ambiente | *(vazio)* | **2** (vermelho) |
| **Atendente Sênior** | ***(vazio)*** | **2** (vermelho) |

A célula de Tokens dele sai como **"sem dado a apresentar"** — o estado vazio,
**não** `0` e **não** "Nenhuma". E o empate em 2 destaca **os dois**, como o
componente promete.

**5.3 — A D3 confirmada NA MESMA TELA, que é a forma mais forte do achado.** No
card *"Falhas"*, logo abaixo da tabela:

- **"Falharam na execução: 2"** — 14,3% das tasks;
- a coluna Falhas da tabela soma **2 + 2 = 4**.

**O operador vê os dois números na mesma rolagem.** `Atendente Sênior` é
`Rejected` e `Especialista Técnico Ambiente` é `Failed`; M28 conta os dois, o KPI
conta só o segundo. Não é mais raciocínio sobre SQL — está desenhado.

**5.4 — O dado plantado, declarado (convenção 22).** As 5 recusas da #75
**aparecem na página**: *"Recusadas na entrada: **5**"*, e em *Motivos → "Agente
inativo: **5**"*. **E não alcançam a tabela de agentes:** os 2 + 2 da coluna
Falhas vêm de `task_executions`, e nenhuma das 5 tem linha lá. Os dois cards que
elas alimentam não são tocados por esta change.

*(Precisão sobre a janela, que o `design.md` generalizou: as 5 têm `RejectedAt` em
01/10 01:22Z, então ficam **fora** de um pedido que termine em 30/09T23:59:59Z — foi
por isso que a consulta direta devolveu `rejectedAtEntryCount: 0` — e **dentro** da
janela corrente da página, que é onde a tela mostra 5. O que não muda em nenhuma
das duas é o que importa: elas não têm linha de execução.)*

## 6. A documentação fecha ANTES do archive

**Nenhum PR de cauda, nenhum commit de cauda.** Se depois do archive sobrar
correção de registro, a change não estava pronta.

- [x] 6.1 **`02-HISTORICO_E_STATUS.md`, linha ~130** — *"**Não commitada, não
      mergeada** — o push e o PR são do dono"*. **Corrigir com a causa**, não
      apagar: era verdade quando escrita, e o dono publicou — PR **#91**, mergeado
      em **01/10/2026** no commit **`c32f09c`**, a partir de
      `feat/75-motivo-da-recusa-no-sistema`. **Reconferida contra o `HEAD` nesta
      sessão**, texto e posição.
- [x] 6.2 **`02-HISTORICO_E_STATUS.md`, linha ~12857** — *"**Nada commitado, nenhum
      push, nenhum PR** — … é por isso que a #75 continua em `In progress`"*.
      Mesma correção, com a causa e com o fecho das **três** condições da convenção
      24: a #75 está **`CLOSED`** e o item do projeto em **`Done`**, conferido via
      `gh`. **Reconferida contra o `HEAD` nesta sessão.**
- [x] 6.3 **Entrada desta change no `02`**, e ela **não afirma estado de
      publicação** — nem "não commitada", nem "commitada". O `02` é registro do que
      foi **decidido**, e o commit é do dono. Deve conter:
      - a medição da D3 e **a issue nova** que ela abre: a coluna Falhas conta
        `Failed` + `Rejected` e o KPI conta `Failed` só, **a mesma palavra nomeando
        duas populações na mesma página** — a classe de defeito que a #75 corrigiu
        no quadro *"Recusadas na entrada"*. Com o motivo de **não** corrigir aqui:
        tirar `Rejected` da coluna **desfaz a #84**;
      - a varredura por forma com a lista fechada, e que a premissa vinda da #90
        **não se confirmou**;
      - a medição da convenção 18 por categoria;
      - que a convenção 1 **não se aplica** nesta change, em vez de dizer que foi
        cumprida.
- [x] 6.4 **Abrir a issue da D3** antes do archive, e referenciá-la na entrada do
      `02` e no comentário do componente. Sem ela o achado morre no `design.md` de
      uma change arquivada.
- [x] 6.5 **`CHANGELOG.md`** — as duas correções, por efeito na tela e não por
      número de issue.
- [x] 6.6 Conferir se algum arquivo de `docs/` afirma a população antiga da tabela,
      e corrigir com a causa. Varrer **pela forma** — a descrição da população —, e
      não por `#84`.
- [x] 6.7 **`01-ARQUITETURA_E_CONVENCOES.md` — e a projeção desta tarefa estava
      errada.** Ela dizia *"nenhuma convenção muda"*. Conferindo, **a convenção 18
      muda**: ela já continha a regra de que *"o comentário é o produto"*, e esta
      change é a **quarta ocorrência** dela — a primeira em que a regra existia e
      **não foi aplicada**, com erro de +53% exatamente na direção que ela prevê.
      Acrescentadas a linha da tabela (proporção **14 : 62**) e a extensão que a
      ocorrência dá à regra: o comentário se projeta contando também **os blocos
      que a change torna falsos**, não só os que ela entrega.
      **Conferido e inalterado:** a convenção **1**, cujo corolário (*"se a UI
      descobrir que precisa de dado que o backend não serve, é achado a reportar e
      sequenciar"*) foi **exercido** — a conferência mostrou que o backend serve, e
      é por isso que não há etapa de `apps/api`. As demais citadas (2, 6, 9, 13,
      15, 22, 23, 24) foram aplicadas sem alteração de texto.

## 7. Sincronização e archive

- [x] 7.1 `/opsx:sync` do delta de `system-insights-ui`.
- [x] 7.2 `/opsx:archive` **só depois** de 5 e 6 inteiros, e **só depois da
      validação visual do dono** — em change de tela o archive é manual e a
      conferência é quadro a quadro.
