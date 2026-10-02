## Context

`DeclaredGap` é o componente da **lacuna declarada** — o quadro 6 do
`Estados.dc.html` —, e hoje ele tem duas formas: `block`, a moldura tracejada, e
`inline`, a linha esmaecida no peso de subtítulo.

**Estado medido no `HEAD` (`9709e045`), por varredura pela forma:**

| | |
|---|---|
| sítios que renderizam `<DeclaredGap>` | **2**, os dois com `variant="inline"` |
| | `InsightsKpiGrid.tsx:92`, `AgentModelsCard.tsx:148` |
| `variant` default | **`'block'`** (`DeclaredGap.tsx:78`) |
| casos de teste que exercitam `block` | **1** (`DeclaredGap.test.tsx:97`), verde |
| `DeclaredGap.tsx` | 116 linhas — **64 de comentário, 47 de lógica, 5 vazias** (1,36:1) |

**Critério de contagem, para quem recontar (convenção 22):** os sítios são as
ocorrências de `<DeclaredGap` em `apps/frontend/src`, excluídos os arquivos
`*.test.tsx`; o default é o valor do parâmetro na desestruturação da assinatura.
Contado em 01/10/2026 sobre `9709e045`.

A moldura ficou órfã quando a **décima rodada de conferência da #52** removeu os
três rodapés de lacuna da página do sistema, com a régua do dono: *"Subtítulo vira
lacuna; coluna sai sem deixar quadro."*

## Goals / Non-Goals

**Goals:**

- fechar a **armadilha**: um `<DeclaredGap>` novo sem prop não pode renderizar o
  quadro que a spec proíbe;
- fechar a **classe**, não só a instância — e deixar o registro dela onde ele
  sobrevive ao archive (convenção 23);
- **não mudar a tela.** As duas lacunas que existem hoje saem desta change
  pixel a pixel como entraram.

**Non-Goals:** os da `proposal.md`. Em particular, esta change **lê** a #90 para
decidir a forma e **não implementa** nada dela.

## Decisions

### D1 — A variante sai inteira. Não é só o default que está errado.

A #83 põe duas saídas na mesa, e a diferença entre elas **não é de estilo**.

**Trocar o default para `inline`** desarma a armadilha e **deixa a classe viva**: a
variante continua sem consumidor, o compilador continua sem acusar, e a única coisa
que a mantém detectável é alguém reler o arquivo. É literalmente o estado que a #89
registra como o que não funciona — com o agravante de que o guarda que a cobre
**mantém o código morto vivo e esconde que ele está morto**.

**Remover a variante move a classe para dentro do compilador.** Sem a prop,
`<DeclaredGap variant="block" …>` **não compila**. A forma que hoje não é varrível
por nenhum meio automático passa a ser varrida por `tsc`, de graça, em todo sítio
futuro. É a mesma régua que a convenção 18 já usa para blast radius — *"o `tsc`
enumera de graça"* —, aplicada à prevenção em vez de à projeção.

**Convenção 2 fecha a decisão:** a variante é **previsão**, não repetição
observada, e a régua do frontend é explícita quanto a isso.

**Custo assumido, escrito para não parecer esquecimento:** se um artboard desenhar
o quadro depois, ele é reescrito. São **12 linhas** de JSX com estilo inline, e a
razão de ele ter existido fica na #83, neste `design.md` e no comentário do próprio
componente — ninguém vai reinventá-lo do zero sem saber que já houve um.

### D2 — A #90 foi lida antes de decidir, e ela não pede o `block`

A #90 é a candidata mais próxima a precisar de quadro de rodapé, e o corpo dela diz
o contrário: o trabalho que ela guarda é a **nota de regime por grupo** —
*"recusa medida desde 26/09/2026"* —, e a página **já renderiza essa forma** no card
vizinho, como `Text size="xs" c="dimmed"` dentro do `Stack` do grupo
(`FailuresCard.tsx:186-190`), sem moldura nenhuma.

Os **dois gatilhos** da #90 são *"alguém precisar saber desde quando uma fase de
falha específica é medida"* e *"o `Main.dc.html` ganhar elemento para a nota por
grupo"* — nenhum dos dois é quadro, e o segundo é justamente a condição que a spec
exige para declarar na tela: **elemento do próprio protótipo para carregar a
declaração**. Se o artboard desenhar esse elemento, ele será o que a nota ocupa, não
uma moldura inventada.

**E o achado que a #90 carrega junto** — `ProviderConsumptionCard` com a mesma forma
de declaração de regime — também é texto de cabeçalho, não quadro.

**Conclusão registrada: nada no caminho pede a moldura.** Se algo passar a pedir, a
reabertura é pela #83, que fica com a razão escrita.

### D3 — `data-gap-variant` sai junto, e os guardas ficam mais fortes

Com uma forma só, `data-gap-variant="inline"` é um atributo **constante**: ele não
discrimina nada e não pode reprovar nada. Mantê-lo seria repor, no DOM, exatamente a
classe que esta change fecha no TypeScript — declaração viva sem consumidor.

Os **três** guardas que o afirmam (`DeclaredGap.test.tsx:87`,
`InsightsKpiGrid.test.tsx:68`, `AgentModelsCard.test.tsx:132`) passam a afirmar
**a ausência da moldura**. Isso aplica a régua já acumulada — *a precondição de um
guarda de ausência é o estado observável mais próximo do elemento negado, não o dado
de origem*: `data-gap-variant` é o **nome da escolha**; `dashed` ausente é **a coisa
que a spec nega**. O guarda de `AgentModelsCard.test.tsx:125` é o caso extremo — ele
afirma **só** o nome da variante hoje, e não notaria se a moldura voltasse por outro
caminho.

**`data-declared-gap="true"` fica.** Ele tem consumidor: é o que separa a lacuna do
travessão, está no requisito e em três guardas.

### D4 — A #89 dobra, e a forma dela é recusada por uma mais barata

**Estado conferido:** o membro morto (`'rejection-reason'`) **já saiu** na #75, com a
causa escrita no arquivo. Os **sete** membros atuais de `CaveatPlacement` têm
consumidor — cinco nos dois mapas de posição, `'not-on-this-page'` nos dois e
`'unknown'` em `caveatLabel`. Não há defeito pontual aberto. **O que resta é a
forma**, e é por isso que ela pertence a esta change: a #83 é o caso canônico da
mesma classe, e deixar as duas abertas produz o *"corrigir metade"*.

**A #89 propõe derivar o tipo de um `const` array com `typeof`. Recusado, com o
motivo:** os sete membros carregam **um comentário cada** — *"Nas duas
superfícies"*, *"Só na página do sistema"*, *"Só na aba do agente"* —, e um elemento
de array literal não carrega JSDoc que o editor mostre no ponto de uso. Nesta base
**o registro é o produto** (convenção 18), e a mudança de forma pagaria a varredura
com os sete registros.

**A forma escolhida: um `Record<CaveatPlacement, …>` exaustivo dentro do teste.**

- membro novo na união → **o `Record` fica incompleto e o `tsc` reprova**, obrigando
  quem acrescentou a classificar o membro como posição de render ou não;
- o caso então afirma que **todo membro classificado como posição de render tem pelo
  menos um código apontando para ele**, atravessando `caveatLabel` sobre
  `KNOWN_CAVEAT_CODES` nas duas superfícies — a API já exportada, sem abrir os mapas;
- a união **mantém** os sete comentários.

**Por que o arquivo de teste de hoje não pega isso:** ele já tem as posições em
`as const` (`caveatLabels.test.ts:104`, `:181`), mas são **listas escritas à mão** —
um membro novo na união não as quebra. A diferença entre `as const` e
`Record<Union, …>` é exatamente a exaustividade, e é ela que faz o `tsc` trabalhar.

### D5 — A varredura da terceira ocorrência foi feita, e fechou

A #89 pede *"conferir onde mais a forma existe"*. **Feito, pela forma, em
`apps/frontend/src`.**

**Critério de contagem, para quem recontar (convenção 22):** todo alias de tipo
declarado em `apps/frontend/src`, excluídos os arquivos `*.test.*` — a expressão é
`^\s*(export )?type [A-Za-z0-9_]+ *=`, em 01/10/2026 sobre `9709e045`. **30
aliases.**

**E o critério está escrito largo de propósito, porque o estreito perdeu um.** A
primeira passagem casou só o nome em `[A-Za-z]*` e a abertura da união na mesma
linha; ela devolveu **19** e perdeu o `StatusFilter` de `AgentListPage.tsx:24`, além
de todos os aliases que não são união. **Uma varredura que não enumera o universo
inteiro antes de filtrar não consegue dizer quantos ela deixou de fora** — foi a
comparação das duas passagens que mostrou o buraco, não a leitura da primeira. É a
convenção 22 na forma da sexta ocorrência: o número não envelheceu, **nasceu
errado**, e só a recontagem com o critério largo o acusou.

Decompostos:

| grupo | n | por que não é (ou é) da classe |
|---|---|---|
| **não são união de literal** (`LogoProps`, `Links`, `Nullish`, os quatro `Update*Input`) | 7 | não há membro para ficar órfão |
| **derivadas de valor** (`AgentDetailTab`, os **dois** `StatusFilter`, `KnowledgeBaseDetailTab`, `InsightsPeriod`) | 5 | **já varríveis por construção** — o `typeof` as amarra à lista que existe como valor |
| **uniões de objetos discriminados** (`FileSendState`, `SelectedFile`) | 2 | o membro órfão seria um tipo sem construtor, que o `tsc` acusa no sítio de criação |
| **formato de fio** (`ChannelType`, `KnowledgeIndexingStatus`, os quatro `Message*`, `McpServerAuthType`, `McpConnectionTestFailureReason`) | 8 | os membros vêm do backend; o guarda que vale ali é o de acordo (convenção 11), não este |
| **inventadas pelo frontend** (`CaveatPlacement`, `CaveatSurface`, `MetricState`, `QueryState`, `DayState`, `LogoVariant`, `Mode`, `HeatStep`) | 8 | **são a classe** |

**Nenhum membro de nenhuma das oito está sem consumidor de produção hoje.** Os de
contagem mais baixa foram abertos um a um, porque contagem baixa é onde o membro
coberto só por teste se esconde: `LogoVariant 'symbol'` e `'mark'`
(`Logo.tsx:59-65`), `Mode 'manual'` (`KnowledgeDocumentModal.tsx:60,218,246`),
`DayState 'measured-zero'` (`measuredDays.ts:98`, `PeriodHeatmapCard.tsx:97`).
**Todos com sítio de produção. A saída colada está no `tasks.md`.**

**Não há terceira ocorrência. A #89 fecha com resultado.**

#### E a varredura achou o precedente da casa, que muda a D4

**`HeatStep` já tem a lista como valor**: `HEAT_STEPS: readonly HeatStep[]`
(`heatScale.ts:21`), consumida em produção (`PeriodHeatmapCard.tsx:119`). **A forma
que a #89 propõe já existe nesta base** — ela só nunca foi aplicada a
`CaveatPlacement`.

**E ela é mais fraca do que parece, o que confirma a D4:** `readonly HeatStep[]`
**não obriga a lista a ser exaustiva**. Um membro novo na união compila com o array
intacto, e nada reprova. É declaração paralela, não derivada: varrível, sim;
auto-verificável, não. O `Record<CaveatPlacement, …>` da D4 é o que fecha isso, e
custa menos que a transformação de forma.

#### Achado de passagem, com issue a abrir — e NÃO é escopo desta change

`theme.test.ts:147` **redeclara** `const HEAT_STEPS = [0,1,2,3,4,5] as const`
localmente, em vez de importar o exportado de `heatScale.ts`, e alimenta com ele dois
`it.each` que afirmam as variáveis `--buteco-heat-N` nos dois esquemas. **Se a escala
ganhar um sexto passo, a cópia local continua com seis e os dois casos passam verdes
cobrindo menos** — é a mesma mecânica de camuflagem da #83, por duplicação em vez de
orfandade.

**Não entra aqui:** é outro arquivo, outra feature e outra causa (cópia, não
declaração morta), e corrigi-lo de passagem é exatamente o improviso que a convenção 1
proíbe. **Vira issue própria antes do fechamento** (convenção 23), e a abertura
depende do dono.

### D6 — Os guardas, e qual reprova contra o quê (convenção 15)

| # | guarda | arquivo | reprova contra o `HEAD`? |
|---|---|---|---|
| 1 | **sem escolher forma, a lacuna vem sem moldura** | `DeclaredGap.test.tsx` | **SIM** — no `HEAD` o default é `block` e o estilo tem `dashed`. É o guarda central |
| 2 | **a fonte do componente não declara borda nenhuma** (varredura estática) | `DeclaredGap.test.tsx` | **SIM** — `border: '1px dashed …'` está lá |
| 3 | os dois consumidores continuam renderizando o que renderizam hoje | os dois `*.test.tsx` | **não, e é de propósito** — par positivo, para a simplificação não mudar a tela |
| 4 | todo membro de render de `CaveatPlacement` tem consumidor | `caveatLabels.test.ts` | **SIM, ao reintroduzir `'rejection-reason'`** — que é como ele será verificado |
| 5 | nenhum sítio passa `variant` | **o `tsc`** | **SIM** — depois da remoção, passar a prop não compila |

**O guarda 2 responde ao que a #89 diz não ter solução barata.** A união de tipo não
é varrível em tempo de execução, mas **texto de fonte é** — e esta base já tem o
precedente (`components/data/surfaceTokens.test.ts`, que varre tom fixo de superfície
em todos os componentes). A régua que sai daí: **quando a declaração morta é
textual, a varredura é sobre a fonte; quando ela é de tipo, é sobre a exaustividade
que o compilador sabe checar.** Duas ferramentas, e nenhuma delas é leitura manual.

**A ordem importa, e é a régua já acumulada:** *tirar a prop no mesmo passo em que se
escreve o guarda o faz passar por construção.* Os guardas 1, 2 e 4 são escritos e
rodados **contra o `HEAD`**, vistos reprovar, e só então a remoção acontece.

**A régua da fiação, que a #84/#86 acabou de provar valer:** com os guardas verdes,
tirar o `<DeclaredGap>` de **cada** consumidor e conferir que algum caso reprova.
**Projeção: reprova nos dois** — `InsightsKpiGrid.test.tsx:60` e
`AgentModelsCard.test.tsx:119` buscam a lacuna por `testid`, e a lacuna aqui é JSX
estático, não célula derivada de dado. O buraco da change anterior existia numa
camada que aqui não existe: lá a **página** garantia a prop e nunca afirmava a célula.
**Se algum dos dois passar, é achado e vira guarda** — foi assim que o nono guarda da
change anterior nasceu.

### D7 — Delta só em `system-insights-ui`

`agent-insights-ui` também consome o componente (`AgentModelsCard`), e o requisito de
lá **difere explicitamente** para esta regra: *"o sistema SHALL seguir a regra já
fixada para esta família de telas"*. Escrever a mesma afirmação nos dois criaria
segunda fonte de verdade para uma regra só — o defeito que a convenção 13 nomeia como
*afirmação que outra etapa torna falsa*. **A regra mora onde foi fixada.**

### D8 — A convenção nova, e por que a classe precisa dela

O conteúdo exclusivo da #89 — a **tabela das duas ocorrências** — vai para o `01`
antes de a issue fechar. O que ela registra, e nenhum dos dois casos sozinho diz:

| | #83 | #89 |
|---|---|---|
| o que ficou órfão | a variante `block` do `DeclaredGap` | o membro `'rejection-reason'` de `CaveatPlacement` |
| por que ficou | a #52 removeu os três rodapés | a #51 removeu o único `caveat` que o usava |
| o compilador acusa? | **não** — é o default, compila e renderiza | **não** — união com um membro a mais nunca quebra quem não o menciona |
| teste verde o cobre? | **sim**, e é pior: o guarda mantém o morto vivo | **não**, e é pior de outro jeito: sem teste não há nem o sintoma |

**As duas sobrevivem por motivos OPOSTOS, e é isso que faz a classe.** Ter teste e não
ter teste produzem o mesmo resultado, então nenhuma régua sobre cobertura a pega. O
que a pega é perguntar **quem consome a declaração**, e as duas ferramentas da D6.

### D9 — Convenção 18, vigésima quinta medição: a pergunta é se a régua do comentário inverte

**A vigésima quarta deixou a régua escrita:** *o comentário se projeta contando também
os blocos que a change torna falsos*, porque corrigir com a causa custa mais que
escrever pela primeira vez. **Ela não foi consultada lá** — foi a primeira ocorrência
de não-aplicação da série, e custou +53%. Aqui ela é consultada **antes**.

**Os blocos que esta change torna falsos, contados por leitura:**

1. `DeclaredGap.tsx:47-63` — o bloco `VARIANTE` inteiro (17 linhas), que descreve as
   duas formas e a armadilha;
2. `DeclaredGap.tsx:40` — o terceiro item da lista de **quatro** mecanismos que contêm
   o risco do qualificador *"não disponível"* é *"a moldura tracejada do `block`"*. Ele
   **sai**, e a lista passa a três: a correção precisa dizer que o risco perdeu uma
   perna e por que as outras três bastam. **É o bloco mais fácil de não contar**, porque
   ele não fala de variante — fala de risco;
3. `DeclaredGap.tsx:70` — o comentário da prop.

**A hipótese desta medição, e ela é falsificável: numa change de REMOÇÃO a régua não
inverte — ela se agrava, e por dois motivos que somam.**

- **o numerador cresce**: numa correção, o texto novo descreve um mecanismo que
  continua logo abaixo, então ele pode apontar e ser curto. Numa remoção, o mecanismo
  **não está mais no arquivo**, e o texto carrega-o inteiro — senão a próxima pessoa o
  reintroduz sem saber que já houve um;
- **o denominador encolhe**: a change **tira** lógica em vez de acrescentar.

**Projeção: ~6:1 de comentário para lógica na produção**, contra **4,4:1** da
vigésima quarta (correção) e **1,98:1** da vigésima terceira.

**E uma coisa que esta projeção NÃO faz:** declarar que o erro será menor. A vigésima
quarta errou +53% e o motivo que ela deu para esperar erro menor — *"a anterior errou
por duplo de teste, e aqui não há duplo"* — **estava errado**. Aqui também não há duplo
(componente de apresentação pura e um módulo puro), e **isso não é argumento para nada**.

#### A projeção, por categoria e não só pelo total

A vigésima terceira acertou o total em −1,0% **com as categorias erradas** — inventou
reforçados e esqueceu removidos. Projetadas separadas, com `0` escrito onde é `0`:

| categoria | projetado |
|---|---|
| **novos** | **3** (guarda central, varredura estática da fonte, exaustividade de `CaveatPlacement`) |
| **adaptados** | **3** (`DeclaredGap.test.tsx:73`, `InsightsKpiGrid.test.tsx:56`, `AgentModelsCard.test.tsx:125` — o último também renomeado) |
| **removidos** | **1** (`DeclaredGap.test.tsx:97`, *"a variante block mantém a moldura tracejada"*) |
| **reforçados** | **0** |
| **de fiação** | **0** — e a razão está na D6. Se aparecer, é +1 |
| **negativas novas** | **2** (os guardas 1 e 2 da D6) |
| **semeadura de fixture** | **0** |

**Saldo: +2 casos.** Arquivos de teste: **118 → 118**, nenhum criado.

| linhas (adicionadas, à mão) | projetado |
|---|---|
| criados | **0 arquivos, 0 linhas** |
| modificados — produção (3 arquivos) | **~35**, sendo **~30 de comentário e ~5 de lógica** |
| modificados — teste (4 arquivos) | **~80** |
| **total à mão** | **~115** |

**Linhas REMOVIDAS projetadas: ~55** (16 de lógica em `DeclaredGap.tsx`, 17 do bloco
`VARIANTE`, 2 da prop, 2 nos consumidores, ~12 do caso removido e dos trechos
adaptados). Projetar remoção à parte é novidade desta medição, e é obrigatória numa
change cujo entregável é subtração: contar só adicionadas descreveria metade do
trabalho.

**Baseline: NÃO HERDADA e NÃO FECHADA.** Ver a D10.

### D10 — A baseline foi tentada e ABORTADA. O número não entra.

A última registrada é `apps/frontend` **1411 / 118** sobre o commit da #84/#86. A
régua é remedir em árvore limpa; **a remedição foi tentada em 01/10/2026 e o
resultado é inválido**:

- `podman ps`: **2 contêineres**, os de desenvolvimento —
  `pgvector/pgvector:pg18` e `rabbitmq:4.3-management`, ambos `Up`/`healthy`.
  **Nenhum Testcontainers rodando**;
- `podman machine list`: `podman-machine-default`, `applehv`, 6 CPUs, 6 GiB,
  *Currently running*;
- **a VM estava consumindo 343% de CPU** (`com.apple.Virtualization.VirtualMachine`), e
  o `load average` da máquina de 12 núcleos ficou em **33-37** durante a execução;
- resultado: **duração de 2379 s**, **11 arquivos reprovados / 14 casos**, e **2
  `Unhandled Error` do tipo `[vitest-pool]: Failed to start forks worker` /
  `Timeout waiting for worker to respond`** — falha de ambiente, não de asserção. O
  total observado (**1359**) é menor que o registrado porque os workers de 11 arquivos
  nunca subiram.

**Abortada de propósito**, pela mesma razão que a terceira tentativa da #75: *medição
inválida citada depois vale menos que medição ausente* — é a convenção 22 pelo avesso.
**A remedição é a tarefa 1 do `tasks.md`**, e nenhum número de suíte entra no `02`
antes dela.

## Risks / Trade-offs

- **[A moldura ser necessária depois]** → São 12 linhas de JSX, e a razão sobrevive em
  três lugares (issue #83, este `design.md`, o comentário do componente). A reabertura é
  pela #83. **Conferido contra a candidata mais próxima — a #90 não a pede (D2).**
- **[A simplificação mudar a tela sem ninguém ver]** → O caminho `inline` hoje é
  `fw={400}` e `c='dimmed'` **dentro de ternários**; colapsá-los é onde o erro mora, e
  jsdom não enxerga peso de fonte. Mitigação em duas pernas: o **par positivo** (guarda 3)
  e a **conferência manual nas duas telas, nos dois esquemas de cor** (convenção 14),
  antes do archive.
- **[Os guardas provarem o componente no vácuo]** → a **régua da fiação** da D6,
  rodada nos dois consumidores, com o resultado colado no `tasks.md`.
- **[O guarda 4 nascer verde por construção]** → o membro morto da #89 **já saiu**, então
  o guarda novo passa no primeiro verde. Ele **só vale** depois de `'rejection-reason'`
  ser reintroduzido de propósito e o caso reprovar (convenção 15). Tarefa própria.
- **[A remedição da baseline sair inválida de novo]** → **abortar outra vez**, e
  registrar a ausência com a evidência. Nenhum número de suíte vai para o `02` sem
  `podman ps` e `load average` declarados ao lado dele (convenções 19 e 22).
- **[A varredura da D5 ser lida como "o frontend está limpo"]** → ela é de
  `apps/frontend/src`, pela forma descrita, em 01/10/2026 sobre `9709e045`. **Não diz
  nada sobre os outros três apps**, e o escopo vai colado ao número.

## Migration Plan

Não há migração: nenhum dado, nenhuma rota, nenhum contrato. A ordem de implementação
é a do `tasks.md`, e ela é dirigida pela convenção 15 — guardas antes da remoção.

## Open Questions

Nenhuma. As três decisões que a #83 deixava em aberto estão fechadas na D1 e na D2,
com o motivo escrito; a da #89 está na D4.
