## Context

A tabela *"Consumo por agente"* da página do sistema tem três colunas — Agente,
Tokens, Falhas — e **cruza dois contratos** da mesma resposta:

| fonte | consulta | agrupamento | população |
|---|---|---|---|
| `tokens.byAgent` (M13) | `provider_calls pc join task_executions e` | `group by AgentId` | agente **com chamada de provedor** |
| `errors.byAgent` (M28) | `task_executions` só | `group by AgentId, Provider, Model` | agente **com execução terminal em `Failed` ou `Rejected`** |

As **linhas** nascem da primeira; a **coluna Falhas** é lida da segunda por um
`Map`. Os dois defeitos moram exatamente nessa costura.

### A medição, e o que ela inclui

Banco de dev, **30/09/2026**, janela da rota com `executionFrom` =
**`2026-09-22T04:21:00Z`** (regime `execution` de `appsettings.json`, que é mais
tarde que o pedido de 30 dias e portanto é ele que recorta).

**O dado plantado da #75 está no banco e NÃO alcança nenhum número abaixo**
(convenção 22). São **5 linhas de `task_rejections`**, contra `4ab9739f` e
`69453038`. **Nenhuma das 5 tem linha em `task_executions`** — conferido,
`tem_execucao = f` nas cinco.

*(Sobre a janela, com a precisão que a conferência de 5.4 obrigou: o `RejectedAt`
das cinco é de **01/10 01:22Z**, então elas caem **fora** de um pedido que termine
em 30/09T23:59:59Z — a consulta direta devolveu `rejectedAtEntryCount: 0` — e
**dentro** da janela corrente da página, onde a tela mostra 5. Este parágrafo dizia
"todas dentro da janela", o que era verdade da janela que eu media e não de
qualquer uma; corrigido com a causa.)* Como toda medição desta change lê
`task_executions` e `provider_calls`, as 5 recusas de entrada não entram em
nenhuma delas; elas alimentam `rejectionsByReason` e `rejectedAtEntryCount`, que
esta change não toca. **Medido com elas no banco, e declarado** — limpá-las seria
mexer no dado de conferência de outra change sem necessidade.

**Execuções na janela: 14, de 5 agentes.**

| agente | execuções | `Failed` | `Rejected` | chamou provedor? | tem linha hoje? |
|---|---|---|---|---|---|
| `9e873fc1` | 5 | 0 | 0 | sim | sim |
| `318924ed` | 3 | 0 | 0 | sim | sim |
| `475a81a2` | 2 | 0 | 0 | sim (sem token reportado) | sim |
| `69453038` | 2 | **2** | 0 | sim | sim |
| `4ab9739f` | 2 | 0 | **2** | **não** | **NÃO** |

`errors.byAgent` devolve duas linhas:

| AgentId | Provider | Model | FailedCount | estado |
|---|---|---|---|---|
| `4ab9739f` | `(null)` | `(null)` | 2 | `Rejected` |
| `69453038` | `google` | `gemini-2.5-pro` | 2 | `Failed` |

**O caso da #84 está ativo.** `4ab9739f` é o agente de provedor e modelo nulos —
o defeito esconde preferencialmente a falha de configuração, como a issue previu.

**O caso da #86 NÃO está ativo neste dado:** nenhum agente tem duas linhas em
`errors.byAgent` dentro do regime. As duas linhas da `Triagem` que a #86 mede
(5 + 2 = 7) são de antes do regime e o recorte as remove. **O guarda da #86 é por
isso de fixture, e não de banco** — e ele reprova contra o `HEAD` igual.

## Goals / Non-Goals

**Goals:**

- nenhuma falha que a rota serve desaparece da coluna Falhas, por nenhum dos dois
  caminhos;
- a contagem de um agente é a **soma** das linhas que a rota serve para ele;
- o destaque em vermelho continua verificável pelo olho — é o maior número da
  coluna — **sobre a população nova**;
- a célula de Tokens do agente sem token fica **vazia**, nunca `0`.

**Non-Goals:**

- **não** mudar `apps/api`: nenhuma rota, nenhuma consulta, nenhum tipo de resposta
  (D1);
- **não** adotar a população "todos os agentes com execução na janela" (D2);
- **não** fazer a coluna Falhas fechar com o KPI de falhas da página (D3) — fica
  declarado e vira issue;
- não tocar a #83, a #85, a nota por grupo da #90, a aba do agente nem o card de
  Motivos.

## Decisions

### D1 — A correção é de `apps/frontend` só, e isso foi **medido** antes de decidir

A #84 avisa que *"pode exigir campo novo em `GET /insights/system`"* e manda
conferir antes de decidir o escopo. Conferido: **não exige.**

`errors.byAgent` (M28) consulta **`task_executions` sozinha**, sem junção a
`provider_calls`. Então **todo agente com execução terminal em `Failed` ou
`Rejected` na janela tem linha ali, sempre** — independentemente de ter chamado
provedor. Unir as chaves das duas listas no cliente traz de volta **toda** falha
que a rota serve, **por construção e não por sorte do dado**.

A medição confirma: a união dá os 5 agentes de C — `4ab9739f` entra por
`errors.byAgent` — e a consulta F (*agente com execução, sem chamada de provedor e
sem falha*) devolveu **0 linhas**.

**Alternativa considerada e recusada:** campo novo na rota com a lista de agentes
por execução. Seria a leitura literal do corpo da #84, obrigaria `apps/api` antes
do frontend pela convenção 1, e **não é o que o dano pede** — ver D2.

**Consequência registrada:** como não há trabalho de `apps/api`, a convenção 1
(backend primeiro) não tem o que ordenar aqui. Ela não foi dispensada; ela não se
aplica.

### D2 — A população é a **união das duas listas**, e não "todo agente com execução"

As duas candidatas diferem em um caso só: **agente que executou, não chamou
provedor e não falhou**. A união o deixa de fora; a população ampla o traz.

**Ele fica de fora, de propósito**, por dois motivos:

1. **a tabela não tem o que mostrar dele.** As colunas são Agente, Tokens e
   Falhas. Sem token e sem falha, a linha chegaria com Tokens vazio e Falhas
   *"Nenhuma"* — um nome, e mais nada. A coluna que daria sentido a essa linha é
   `Tasks`, e ela **ficou fora por decisão** (#67, fechada), com a razão
   aritmética escrita no cabeçalho do componente;
2. **nada na tela deixa de fechar por causa dele.** A #84 argumenta com *"a soma
   das tasks das linhas dá 25 contra o KPI de 27"* — e **não existe coluna de
   tasks nesta tabela** para somar. O que precisa fechar aqui é a coluna Falhas,
   e a união a fecha inteira.

**Gatilho para reabrir:** a primeira coluna de população — `Tasks` ou taxa de
falha — que entre nesta tabela. Aí a linha do agente sem token passa a ter
conteúdo, a população ampla passa a ser a certa, e **é aí que o campo novo de
`apps/api` se justifica.** É a mesma ordem que a issue defende — população antes
de coluna —, lida na direção que o dano aponta.

### D3 — ACHADO: a coluna Falhas **não** soma o mesmo que o KPI de falhas, e corrigir isso desfaria a #84

O par foi conferido como a régua pede, e **não fecha**:

| número | fonte | valor medido |
|---|---|---|
| coluna Falhas, depois desta change | `sum(errors.byAgent)` = `Failed` **+** `Rejected` | **4** |
| KPI *"Falhas"* da página | `errors.failedCount` = `Failed` só | **2** |
| `rejectedCount` | `Rejected` | 2 — **fora da página por decisão** (#75, D3) |

**M28 agrupa `TerminalState in ('Failed','Rejected')`; o KPI conta só `Failed`.** O
`rejectedCount` saiu da página na #75 porque o `Main.dc.html` não tem elemento
para ele.

**Antes desta change a coluna mostrava 2 e fechava com o KPI — por coincidência**,
porque as 2 recusas do `4ab9739f` eram justamente as invisíveis. **A correção não
cria o desencontro; ela o põe na tela.** E isso é melhor que a alternativa:

**Alternativa recusada: tirar `Rejected` da coluna para o par fechar.** Ela
**desfaz a #84 no caso medido** — as 2 falhas do `4ab9739f` são **todas**
`Rejected`, então o agente volta a ter zero falhas e a tabela volta a não ter o que
mostrar dele. Fechar a conta apagando a população é a convenção 22.

**O que esta change faz:** mantém `Rejected` na coluna, **declara o desencontro
aqui**, e abre **issue nova** — a decisão entre renomear a coluna, separar em duas,
ou trazer `rejectedCount` de volta à página é de **vocabulário da tela**, pede o
artboard, e é do dono. É a mesma classe de defeito que a #75 corrigiu no quadro
*"Recusadas na entrada"*: **a mesma palavra nomeando duas populações na mesma
página.**

### D4 — O destaque em vermelho se move com a população, e isso é um defeito novo se esquecido

`maiorFalha` é `Math.max(0, ...byAgent.map(...))` — calculado sobre a **lista de
tokens**, que é a população antiga. Trocar só a lista de linhas e deixar esta
expressão como está cria um defeito que não existia: **a linha que a #84 traz pode
ter a maior contagem da coluna e não sair em vermelho.**

No dado medido isso **empata** (`4ab9739f` = 2, `69453038` = 2, e o empate destaca
todos), então **o banco de dev não reprovaria** — e é por isso que o guarda deste
ponto é de fixture, com contagens diferentes.

### D5 — Somar descarta a decomposição, e isso está certo **porque a tela nunca a mostrou**

O agrupamento por `(AgentId, Provider, Model)` existe porque a rota serve a
decomposição por provedor e modelo. A tabela **nunca** a apresentou: ela tem uma
célula por agente. Somar não perde nada que a tela tivesse.

**Fica registrado que a rota serve mais do que esta tela usa** — `errors.byAgent`
é consumido também pela aba do agente (`AgentFailuresCard`, que apresenta
`linha.failedCount` por linha), e **lá a decomposição aparece**. Nenhum consumidor
perde informação com esta mudança, porque a mudança é local a este componente.

### D6 — A varredura por forma fechou, e o que a motivou não se confirmou

Varrido `new Map(` em todo `apps/frontend/src` fora de teste, e a forma
`.map(… => [`. **Seis sítios**, dos quais **cinco são seguros, cada um com a razão
conferida na fonte**:

| sítio | chave | por que a chave é única na fonte |
|---|---|---|
| `AgentConsumptionCard.tsx:65` | `agentId` | **É O DEFEITO** — fonte agrupa por 3 colunas |
| `WeekdayActivityCard.tsx:53` | `weekday` | a chave **é** o `group by 1` (`extract(dow)`) |
| `measuredDays.ts:81` | `day` | a chave **é** o `group by 1` (`d.day::date`) |
| `SystemInsightsPage.tsx:211` | `a.id` | catálogo de agentes, `id` é PK |
| `knowledgeBaseRows.ts:43` | `knowledgeBase.id` | catálogo de bases, `id` é PK |
| `indexingSummary.ts:45` | `knowledgeBaseId` | handler faz `GroupBy(KnowledgeBaseId).ToDictionaryAsync` — uma linha por base |

**Nada mais entra nesta change, e não há achado com issue.**

**E a premissa que mandou varrer não se confirma:** a #90 **não** registra a forma
do `Map` no `ProviderConsumptionCard`. O que ela registra ali como *"a mesma
forma"* é **declaração de regime** — um cabeçalho que nomeia um de dois regimes.
`ProviderConsumptionCard.tsx` **não tem `new Map(` nenhum**. A varredura foi feita
**pela forma** e é ela que vale; o número da issue levava a outro assunto, que é
exatamente a régua da `fechamento-da-l4` funcionando ao contrário.

## Risks / Trade-offs

- **A coluna Falhas passa a mostrar 4 onde mostrava 2, e o KPI continua em 2** →
  declarado na D3, com issue nova. O operador que somar a coluna e comparar com o
  KPI encontra diferença **real**, que existia antes escondida. Mitigação é a
  issue, não texto na tela: inventar nota de rodapé aqui seria elemento que o
  artboard não tem (a D10 da linha de Insights).
- **A linha nova chega com Tokens vazio, e vazio nesta tela significa "não
  medido"** → é o significado certo: o agente **não** teve chamada de provedor, não
  é que a chamada reportou zero. É a mesma distinção que `TokensAsync` preserva ao
  não normalizar `sum()` nulo.
- **`475a81a2` já hoje tem linha com tokens nulos** (consumo na junção, sem token
  reportado) → a célula vazia não é estado novo na tabela; a mudança é só **quem**
  mais pode chegar nele.
- **Guarda da #86 não é reproduzível no banco de dev** → fixture, com a precondição
  afirmada. Está na D anterior e volta no `tasks.md`.

## Open Questions

Nenhuma bloqueante. A pendente é de vocabulário, está na **D3**, e é do dono:
como a página passa a nomear a diferença entre a coluna (que conta `Failed` +
`Rejected`) e o KPI (que conta `Failed`).
