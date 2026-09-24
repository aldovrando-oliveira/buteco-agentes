## Context

A **convenção 23** fixou que toda change nasce de uma issue. Ela para aí: não
diz quem move a issue pelo board, em que momento, o que a posição dela numa
coluna significa, nem — o ponto que esta change existe para fixar — **em que
momento do fluxo o push e o PR são permitidos**.

Esta é change de **registro puro**. Nenhum código, nenhum app, nenhuma spec de
comportamento de sistema. O entregável é texto em três arquivos, e o produto é
o motivo escrito ao lado de cada regra.

### Conferências — o que foi medido antes de escrever

**Todas em 24/09/2026**, pela API do GitHub (`gh` 
`aldovrando-oliveira/buteco-agentes`, projeto `3`
`PVT_kwHOAVZBpM4Bkfu8`). Convenção 6: o estado do board vem de consulta, não
de memória. Convenção 22: cada número nasce com o estado ao lado e o gatilho de
recalibração.

#### C1 — A ordem em `Ready` não aparece em view nenhuma

| campo | tipo | estado medido |
|---|---|---|
| `Status` | single-select | `Backlog`, `Ready`, `In progress`, `In review`, `Done` |
| `Priority` | single-select | opções `P0`, `P1`, `P2` — **vazia nos 20 itens indexados** |
| `Iteration` | iteration | **vazia nos 20 itens indexados** |

`Current iteration` e `Next iteration` filtram por `iteration:@current`/`@next`
e não mostram nada, porque `Iteration` está vazia. `Prioritized backlog` não
filtra mas ordena por `Priority ASC`, e `Priority` está vazia. **A ordenação
manual gravada não aparece na view sem filtro, e a view sem filtro ordena por um
campo que ninguém preenche.**

**Gatilho de recalibração:** se alguém passar a preencher `Priority` ou
`Iteration` em qualquer item, C1 deixa de valer e a D3 reabre.

#### C2 — O índice do board persiste travado em 20 itens

**Persiste**, e a medição de hoje é mais forte que a da #68, que só tinha o CLI:

| consulta | resultado |
|---|---|
| `gh project item-list 3` | 20 itens, `totalCount: 20` |
| GraphQL `projectV2(number:3){ items(first:100){ totalCount nodes{...} } }` | **20**, `#45`–`#64`, **nenhum arquivado** |
| REST/GraphQL `issue(65..68){ projectItems }` | **os quatro estão lá**, projeto 3, `isArchived: false`, com o `Status` certo: #65 `Done`, #66/#67/#68 `Backlog` |

**O lado de escrita funciona e o de leitura do projeto não.** A #68 já tinha
tentado mudar `Status` por mutação, reposicionar, e apagar e recriar o item do
zero — as três com sucesso, nenhuma registrada na listagem.

**Consequência direta para a documentação:** o board **não** é fonte confiável
da fila, e a convenção não pode mandar ninguém lê-lo como se fosse.

**Gatilho de recalibração:** quando `project.items` devolver mais de 20, ou
quando `#65`–`#68` aparecerem na listagem, C2 caiu e a D3 pode ser revista.

#### C3 — A coluna `Done` existe, e a automação também: sete workflows habilitados

| workflow | habilitado |
|---|---|
| `Item added to project` | sim |
| `Item closed` | sim |
| `Pull request merged` | sim |
| `Pull request linked to issue` | sim |
| `Auto-close issue` | sim |
| `Auto-add to project` | sim |
| `Auto-add sub-issues to project` | sim |

**A configuração de cada workflow — qual `Status` cada um grava — não é exposta
pelo GraphQL**, que devolve só `name`, `number` e `enabled`. O que é observável:
a **#65 terminou em `Done`** depois do merge do #69, que carregava
`Closes #65` no corpo.

**Por isso a documentação separa três coisas**, e não duas: o que é
**automático e verificado** (a issue fecha no merge, pelo `Closes`), o que é
**automático e inferido** (o cartão vai para `Done` quando a issue fecha —
consistente com `Item closed` habilitado e com o que se mediu na #65), e o que é
**manual** (`Backlog` → `Ready` → `In progress` → `In review`). A convenção 13
aplicada à própria documentação: ela não afirma mais do que se mediu.

**Gatilho de recalibração:** se algum dos sete for desabilitado, ou se um cartão
ficar fora de `Done` depois de um merge com `Closes`, C3 caiu.

#### C4 — A convenção 23 não está na `main` (conferência que não estava no enunciado)

O `01-ARQUITETURA_E_CONVENCOES.md` da `main` **termina em 22**. A convenção 23
existe só no commit `d9ba259` da branch `feat/52-insights-pagina-do-sistema`,
que **não foi pushada** — junto de **26 linhas** no `01` e **5 linhas** no
`context:` do `openspec/config.yaml`, medidas por `git diff`.

E a change arquivada da #65 já cita "convenção 23" em duas linhas do `tasks.md`.
**A referência arquivada aponta para uma convenção que não existe no arquivo que
ela nomeia** — exatamente a forma da convenção 6 (requisito errado sobrevive ao
archive).

#### C5 — A regra nova inverte um padrão com cinco precedentes, não um

O enunciado tratava o #69 como o único precedente. Medido por
`gh pr list --state merged`:

| PR | branch | o que era |
|---|---|---|
| #37 | `chore/archive-compactacao-historico` | archive **depois** do PR de código (#36) |
| #39 | `chore/archive-metricas-embedding-coleta` | archive depois do #38 |
| #41 | `chore/archive-rotas-de-agregacao-sistema` | archive depois do #40 |
| #42 | `chore/archive-rotas-de-agregacao-sistema` | idem |
| #70 | `chore/65-archive-serie-diaria-dia-medido-vazio` | archive depois do #69 |

**O PR `chore/archive-*` é o padrão da casa**, com cinco ocorrências. A regra
nova o elimina: com o archive antes do push, código e specs sincronizadas entram
no **mesmo** PR. Isso precisa estar escrito, senão a próxima change abre o
segundo PR por hábito.

#### C6 — O #69 já foi mergeado; não há decisão pendente sobre ele

| evento | quando |
|---|---|
| #69 mergeado (`fix/65-serie-diaria-dia-medido-vazio`, `Closes #65`) | 24/09/2026 02:58:50Z |
| #65 fechada | 24/09/2026 02:58:52Z |
| #70 mergeado (`chore/65-archive-...`, `Refs #65`) | 24/09/2026 03:13:05Z |

O enunciado oferecia duas saídas — arquivar a change antes do merge, ou tratar
como exceção consumada. **A primeira é impossível**: o merge já aconteceu, e o
archive veio 14 minutos depois. Resta registrar.

#### C7 — Nome de branch, e o que o repositório de fato usa

`gh pr list --state merged --limit 30`: das 23 branches mergeadas, **20 são
`<tipo>/<nome>` sem número** (do #17 ao #44). As **três** mais novas carregam o
número: `fix/65-…`, `chore/65-…`, e a branch local não pushada
`feat/52-insights-pagina-do-sistema`.

#### C8 — `docs/` não tem arquivo de fluxo de trabalho

Listado por leitura: `README.md`, `a2a-integration.md`, `architecture.md`,
`configuration.md`, `deployment.md`, `development.md`, `conventions.md`, e o
diretório `marca/`. **Nenhum arquivo de fluxo de trabalho.** O `docs/conventions.md`
é derivado do `01` em sentido único (requisito existente de
`repository-documentation`) e trata de convenção de **código**, não de processo.

#### C9 — `blocked-by` é nativo do GitHub e já está em uso

`GET /repos/.../issues/52/dependencies/blocked_by` devolve a **#65**, e a #65
reporta `issue_dependencies_summary.blocking: 1`. A dependência não é prosa no
corpo: está registrada, e o GitHub mantém a inversa. A convenção pode mandar
usá-la.

## Goals / Non-Goals

**Goals:**

- Documentar o ciclo de vida da issue pelo board, com o **gatilho** de cada
  movimento e com **quem** o executa — pessoa ou workflow.
- Fixar **archive antes de push**, com o motivo escrito junto e com a
  consequência sobre o padrão de dois PRs (C5).
- Reescrever o gatilho de `In review` em **três** condições, não uma.
- Declarar **qual é a fonte autoritativa da fila**, dado que C1 e C2 provam que
  o board não é.
- Fixar a regra do `Closes`, com as duas precisões (fecha no merge; uma issue
  por PR).
- Registrar o precedente do #69 **com a causa** (convenção 9) e o estado do
  board conferido (convenção 22).
- Registrar como **não decidido** o que não foi decidido, em vez de omitir.

**Non-Goals:**

- **Não mexer no fluxo**, só documentá-lo — salvo o que a D3 decide sobre
  ordenação, que era pré-requisito.
- Não tocar `apps/api`, `apps/workers`, `apps/inbox`, `apps/frontend`, `libs/`,
  `deploy/`, `tests/` nem `scripts/`.
- Não reabrir o **conteúdo** da convenção 23. Trazê-la de branch para a `main`
  sem alterar uma letra não é reabrir.
- Não decidir a #66, a #67, nem a ordem da fila em si.
- Não criar arquivo novo em `docs/` (C8).
- Não criar view nova no board nem preencher campo nenhum (D3).

## Decisions

### D1 — A convenção 23 vem para a `main` nesta change, verbatim, e a nova é a 24

**Decisão.** Esta change traz para a `main` as **26 linhas** do `01` e as **5
linhas** do `context:` que hoje só existem no commit `d9ba259` da branch da #52,
**sem alterar uma letra**, e escreve a convenção **24** em cima.

**Por quê.** As alternativas todas custam mais:

| alternativa | o que quebra |
|---|---|
| ramificar da branch da #52 | a #68 só chega à `main` junto com a #52 — e, pela regra nova, **depois do archive** da change de frontend dela. A regra ficaria esperando a change que ela governa |
| escrever 24 na `main`, com buraco no 23 | o `01` passa a ter 22 → 24, e o conflito no fim do arquivo **e** no `context:` do `config.yaml` é garantido no rebase |
| escrever 23 na `main` e renumerar a da #52 para 24 | o `tasks.md` **arquivado** da #65 cita "convenção 23" como sendo a de issue por change. A referência arquivada passaria a apontar para outra regra — convenção 6 na forma exata que ela nomeia |

**Contrapartida, e ela é real:** a branch da #52 passa a carregar um commit
parcialmente duplicado. O `d9ba259` precisa **descartar a parte de convenção** no
rebase e manter só os artefatos da change de frontend. Está escrito como tarefa
e como item aberto no `02`, com o SHA, para não depender de alguém lembrar.

### D2 — `archive` antes de `push`, e o motivo vai escrito junto

**Decisão.** Nenhum push e nenhum PR com a change ativa. `/opsx:archive` feito e
specs sincronizadas são **pré-condição** do push. Exceção só do dono, dita no
momento, e registrada no `02` quando acontecer.

**O motivo, que é a parte que impede a regra de virar ritual:** change ativa é
proposta que ainda pode mudar. PR aberto sobre ela convida revisão de um estado
que não é o final, e o archive é o que move as decisões dos artefatos da change
para as specs vivas. **Revisar antes do archive é revisar o rascunho do
contrato.**

**Alternativa considerada: manter o padrão de dois PRs** (código, depois
`chore/archive-*`), que é o que a casa faz há cinco ocorrências (C5). Recusada
pelo mesmo motivo: no primeiro PR as specs principais ainda não refletem a
decisão, e é sobre elas que a revisão deveria acontecer.

**Consequência a escrever, senão o hábito vence:** com o archive antes do push,
o PR `chore/archive-*` **deixa de existir**. Um PR só, com código e specs
sincronizadas juntos.

### D3 — A ordem mora no `02`; o board é vista, e a convenção diz isso

**Decisão.** A fila ordenada é o `02-HISTORICO_E_STATUS.md`. O board mostra **em
que coluna** cada issue está, não em que posição. `Priority` e `Iteration`
continuam vazias, **por decisão escrita**, não por esquecimento.

**Por quê.** C1 mostra que a ordem manual não é visível em view nenhuma, e C2
mostra que o índice do board nem sequer lista as quatro issues mais novas.
Documentar "a posição na coluna é a ordem de execução" seria descrever mecanismo
que não funciona — convenção 13 aplicada à documentação.

**Alternativas consideradas:**

- **Preencher `Priority`.** Só há três valores (`P0`/`P1`/`P2`) para uma fila de
  20+ itens; dentro de um balde a ordem continua invisível. Resolve a view, não
  o problema.
- **View nova, manual e sem filtro.** Funcionaria para os 20 itens indexados, e
  **não** para os quatro que C2 esconde. E sai do "só documentar".

**O que a convenção manda fazer no lugar:** a ordem da linha de trabalho fica
escrita no `02`, e a dependência entre issues usa `blocked-by` nativo do GitHub
(C9) — que se registra numa ponta e o GitHub mantém a inversa.

**Gatilho de recalibração:** quando C2 cair (o índice destravar), reavaliar se
vale ter a ordem também numa view. A ordem no `02` não deixa de valer por isso —
ela é a que sobrevive ao archive.

### D4 — A bloqueante mora na mesma coluna da bloqueada, acima dela

**Decisão.** Issue que bloqueia outra fica na **mesma coluna** da bloqueada e
**acima** dela, e a relação é registrada por `blocked-by` do GitHub, não só
descrita em prosa.

**O caso que expôs isso:** a #52 ficou em `Ready` marcada como bloqueada
enquanto a #65, que a bloqueava, estava em `Backlog`. Quem pegasse a primeira da
fila travaria no primeiro dia, e o desbloqueio não aparecia em lugar nenhum sem
abrir a issue.

### D5 — `Closes` fecha no merge, e é uma issue por PR

**Decisão.** Todo PR carrega `Closes #<issue>` no corpo, e **uma** — a issue que
originou a change. Achado descoberto dentro da change que virou issue própria
entra como `Refs #N`, nunca como `Closes`.

**As duas precisões que a regra carrega:**

- **`Closes` fecha no merge, não na aprovação.** Aprovar não fecha nada, e não
  move cartão nenhum.
- **Fechar a issue é o que move o cartão para `Done`** — pelo workflow
  `Item closed`, habilitado (C3), e observado na #65. A movimentação para `Done`
  é automática; as quatro anteriores não são.

**Por que uma issue por PR:** achado que virou issue própria é **fila**, não
escopo deste PR. Fechá-lo junto apagaria da fila um trabalho que não foi feito —
o oposto do que a convenção 23 existe para garantir, e a mesma régua da
convenção 1 (achado é sequenciado, não corrigido de improviso).

### D6 — `<tipo>/<numero>-<nome>` fica

**Decisão.** A branch nomeia a issue: `docs/68-fluxo-de-trabalho-no-board`,
`fix/65-serie-diaria-dia-medido-vazio`. Vale daqui em diante; as 20 anteriores
(C7) ficam como estão, e nenhuma é renomeada.

**Por quê.** Fecha o circuito issue → branch → PR → archive sem depender de
alguém lembrar de citar o número. É a mesma razão da convenção 23, aplicada ao
único artefato do ciclo que ainda não a carregava.

### D7 — O precedente do #69 é registrado como consumado, com a causa

**Decisão.** O `02` registra que o #69 abriu PR com a change ativa, que **já foi
mergeado** (C6), e que a regra da D2 passa a valer **a partir desta change**. O
que aconteceu antes dela não é violação retroativa — a regra não existia.

**Por que registrar em vez de omitir** (convenção 9): a mesma sequência vai ser
lida daqui a meses por quem não viu a ordem dos fatos. Sem o registro, o #69
parece a regra sendo quebrada no dia seguinte à sua criação.

**E o registro leva a C5 junto:** o #69/#70 não é caso isolado, é a quinta
ocorrência do padrão que a regra substitui.

### D8 — `CHANGELOG.md` não é tocado, e isso é decisão, não omissão

**Decisão.** Nenhuma linha no `CHANGELOG.md`.

**Por quê.** O `CHANGELOG` registra mudança do produto; esta change não muda
nada que um usuário ou operador observe. A change da #65 atualizou o `CHANGELOG`
porque o corpo de uma rota mudou. Aqui não há corpo, rota, nem comportamento.

**O que continua valendo:** `scripts/check-docs.py` roda como guarda, porque o
`01` é lido por ele e a edição pode quebrar link ou contagem.

### D9 — `docs/` não ganha arquivo novo

**Decisão.** Nada em `docs/`. C8 mediu que não há arquivo de fluxo de trabalho
lá, e o enunciado condicionava a escrita à existência de um.

**Por quê.** `docs/` é derivado do `01` em sentido único, por requisito já
existente de `repository-documentation`, e é escrito para quem nunca viu o
sistema. Fluxo interno de board não é documentação de contribuidor externo —
`CONTRIBUTING.md` cobriria isso se algum dia for para fora, e não é o escopo
desta change.

### D10 — Os nomes das colunas vão escritos como o campo `Status` os grava

**Decisão.** A documentação escreve `In progress` e `In review`, minúsculas na
segunda palavra, porque é assim que o `Status` do projeto 3 os define (C1).

**Por quê.** O enunciado desta change, a #68 e a change arquivada da #65 escrevem
`In Progress` e `In Review`. Nenhuma dessas grafias existe no board. É o tipo de
divergência que nunca causa defeito e sempre causa dúvida de se é a mesma coluna.

## Projeção de tamanho — décima oitava medição da convenção 18

Projeção feita **depois** de fechar as nove conferências, que é o que a
convenção manda: projeção durante a verificação é rascunho.

**Esta é change de registro puro**, e a série tem a régua para exatamente este
caso: *projeção de registro decide o tamanho em change pequena, e é a que mais
erra, porque cresce com os achados*. Esta rodada tem material para testar a
régua — as conferências renderam **quatro** achados que o enunciado não tinha
(C4, C5, C6 e C7 na forma medida), e cada um deles vira parágrafo.

### As três colunas de código, declaradas em zero

| coluna | projetado | por quê |
|---|---|---|
| **teste escrito à mão** | **0** | nenhum arquivo desta change vive em `apps/` ou `tests/` |
| **duplo de teste** | **0** | não há o que dublar; não há código |
| **gerado** | **0** | nenhuma migração, nenhum cliente, nenhum snapshot |

**Declarar as três em zero é o resultado, não a omissão delas.** A décima sétima
medição projetou o duplo em zero e acertou exato, e a linha foi a mais
informativa da tabela; aqui as **três** são zero por construção, e o interesse
da medição é saber se a coluna de registro erra mais quando é a única coluna.

### Guardas em três categorias — novo, reforçado, adaptado

A série ganhou na décima sétima a terceira categoria. **Aqui ela é vazia por
construção**, e as três são declaradas:

| categoria | projetado | por quê |
|---|---|---|
| guarda **novo** | 0 | nenhum comportamento de sistema muda |
| guarda **reforçado** | 0 | idem |
| guarda **adaptado** | 0 | nenhum guarda existente toca dado que esta change mude |

A verificação desta change é `openspec validate --all` e
`scripts/check-docs.py` — guardas que já existem e que esta change **não
modifica**. Rodá-los não os conta em nenhuma das três categorias.

### Registro — a única coluna com conteúdo

| destino | projetado | composição |
|---|---|---|
| `01`, convenção 23 trazida | **26** (exato, medido por `git diff`) | não é projeção: é o diff da branch da #52 |
| `01`, convenção 24 nova | ~95 | tabela de 5 colunas + D2 com motivo + `Closes` + automático×manual + branch + não decididos |
| `openspec/config.yaml` | **5** (exato) + ~7 | o parágrafo da 23 é diff medido; a linha da 24 é projeção |
| `02-HISTORICO_E_STATUS.md` | ~130 | as nove conferências, o precedente do #69 com C5, os não decididos, e o fechamento desta medição |
| artefatos da change | ~875 | `proposal.md` **127** e `design.md` **451** já escritos, **medidos** — não projetados; delta de spec ~95 e `tasks.md` ~200, esses sim projetados |
| **total de registro** | **~1.138**, faixa 1.050–1.300 | — |

**Duas linhas desta tabela são medição e não projeção**, e vão marcadas: o diff
da convenção 23 (26 e 5 linhas) e os dois artefatos já escritos. Misturar as
duas sem dizer qual é qual seria a convenção 22 falhando dentro da própria
projeção que ela governa.

### As duas direções de erro nomeadas de antemão

A convenção 18 diz que nomear direção não substitui contar componentes, e que a
18ª medição vai comparar as duas:

- **para baixo**, se o fechamento descobrir décima conferência — é a forma
  clássica da régua de registro (cresce com os achados), e já aconteceu **quatro
  vezes nesta change** antes mesmo de projetar;
- **para cima** no `02`, se o item de C2 puder ser escrito por referência à #68
  em vez de repetido — o `02` tende a repetir o que a issue já tem.

**A componente que mais provavelmente erra é o `02`**, porque é a única cuja
contagem depende de quantos achados o fechamento acrescenta, e não de estrutura
conhecida. O `01` e o `config.yaml` têm forma fixa; os artefatos da change já
estão quase todos escritos quando esta projeção fecha.

## Risks / Trade-offs

- **[A regra nasce sem o mecanismo que a faria falhar visivelmente]** → Nada
  impede tecnicamente um `git push` com change ativa; a regra vive na
  documentação. Mitigação: a linha no `context:` do `config.yaml` é lida por
  quem abre **toda** change, e o `tasks.md` de cada change carrega a ordem
  archive → push → PR como tarefa. **Não** se propõe hook de git aqui — seria
  código, e esta change não tem código (convenção 15: guarda só vale depois de
  ter falhado contra o defeito real; ainda não falhou sob a regra nova).
- **[A convenção 23 duplicada entre a `main` e a branch da #52]** → Conflito no
  rebase da #52, garantido no fim do `01` e no `context:`. Mitigação: o SHA
  `d9ba259` está escrito na tarefa e no item aberto do `02`, com a instrução de
  descartar a parte de convenção e manter só os artefatos de frontend.
- **[C2 pode cair sozinho e deixar a D3 parecendo excesso de cautela]** →
  Mitigação: o gatilho de recalibração está escrito junto do número (convenção
  22), e a D3 tem razão própria além de C2 — C1 sozinha já basta, e a ordem no
  `02` é a que sobrevive ao archive.
- **[A configuração dos sete workflows não é conferível pela API]** → A
  documentação pode afirmar demais sobre o que é automático. Mitigação: C3
  separa o **verificado** do **inferido**, e escreve qual é qual.
- **[A regra do archive antes do push alonga o ciclo de change grande]** → Uma
  change de frontend com semanas de trabalho fica sem PR até o archive. É o
  custo aceito, e ele é **deliberado**: o PR passa a chegar sobre o contrato
  final. Registrado como trade-off, não como defeito.

## Migration Plan

Não aplicável — nenhum artefato executável, nenhum dado, nenhum deploy. O
rollback é `git revert` de um commit de documentação.

**A única ordem que importa** é a da própria change, e ela é a primeira a seguir
a regra que escreve: os quatro artefatos, `/opsx:apply`, `/opsx:archive`, **e só
então** push e PR.

## Open Questions

Registradas como **não decididas**, o que é resultado — não omissão.

1. **Change abandonada, ou que não vira PR.** A issue vai para `In progress` ao
   abrir a change. Se a change for descartada, a issue volta para `Ready`, volta
   para `Backlog`, ou fica onde está? **Não decidido.** Não há caso real nesta
   base ainda — nenhuma das 23 changes arquivadas foi abandonada —, e decidir
   sem caso seria inventar regra. Vira issue quando a primeira acontecer.
2. **`Ready` tem limite de itens?** O GraphQL de Projects v2 **não expõe** limite
   de coluna: `ProjectV2View` não tem o campo e `ProjectV2ViewConfiguration` só
   traz `visibleFields`. **Conferência de UI, não feita.** Se houver limite e a
   fila o estourar, o que sai é decisão de fila, do dono. Fica na #68.
3. **Onde mora o refinamento da régua "o par não é universal".** A change
   arquivada da #65 escreveu que ele *"vai também para a #68, junto da
   documentação do board"*. **O refinamento é régua de teste** — ausência de
   guarda negativo precisa ser decidida e escrita — e **não tem nada a ver com o
   board**. O roteamento está errado, e esta change **não** o executa: escrever
   régua de teste dentro da convenção do board a esconderia de quem a procura.
   Registrado no `02` como achado a rotear, e a issue própria é o instrumento
   (convenção 23). **Não decidido para onde vai** — candidatos são a convenção 5
   e a 10.
