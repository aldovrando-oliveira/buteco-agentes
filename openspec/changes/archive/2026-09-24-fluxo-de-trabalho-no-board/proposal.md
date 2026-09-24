**Issue:** #68 · **Complementa:** convenção 23 (`insights-pagina-do-sistema`, #52)

## Why

A **convenção 23** fixou que toda change nasce de uma issue. Ela diz que a issue
precede a change, e **não diz o que acontece com a issue depois**: quem a move
pelo board, em que momento, o que a posição dela numa coluna significa, e — a
parte que ainda não estava escrita em lugar nenhum — **em que ponto do fluxo o
push e o PR são permitidos**.

**A regra nova do dono responde essa última:** nenhum push e nenhum PR com a
change ativa. A change precisa estar **arquivada** — `/opsx:archive` feito,
specs sincronizadas — para o push acontecer e o PR ser aberto. Salvo exceção
explícita do dono, dita no momento.

**O motivo, porque regra sem motivo vira ritual:** change ativa é proposta que
ainda pode mudar. PR aberto sobre ela convida revisão de um estado que não é o
final, e o archive é justamente o que move as decisões dos artefatos da change
para as specs vivas. Revisar antes disso é revisar o rascunho do contrato.

**E três conferências feitas antes de escrever mudaram o que a documentação pode
afirmar.** As três estão medidas na seção *Conferências* do `design.md`; duas
delas derrubam mecanismo que a documentação ingênua descreveria:

1. **A ordem em `Ready` não aparece em view nenhuma.** `Current iteration` e
   `Next iteration` filtram por `Iteration`, que está vazia nos 20 itens
   indexados; `Prioritized backlog` não filtra mas ordena por `Priority ASC`, e
   `Priority` está vazia em todos. A ordenação manual gravada não aparece na
   view sem filtro, e a view sem filtro ordena por um campo que ninguém
   preenche.
2. **O índice de itens do board persiste travado em 20** (`#45`–`#64`), medido
   de novo em 24/09/2026 pelo CLI **e** pelo GraphQL. As issues #65–#68 **são**
   itens não-arquivados do projeto 3 — `issue.projectItems` devolve o `Status`
   certo de cada uma —, mas `project.items` continua com `totalCount: 20`. O
   lado de escrita funciona; o de leitura do projeto não.
3. **A automação existe, e são sete workflows habilitados** — entre eles
   `Item closed`, `Pull request merged` e `Auto-close issue`. Boa parte do que
   alguém moveria à mão já se move sozinho.

**E a própria convenção 23 não está na `main`.** Ela vive só no commit não
pushado de `feat/52-insights-pagina-do-sistema`, junto do parágrafo dela no
`context:` do `openspec/config.yaml`. O `01` da `main` termina em **22** — e a
change arquivada da #65 já cita "convenção 23" como se ela estivesse lá.

## What Changes

- **A convenção 23 é trazida para a `main`**, verbatim do commit da #52, junto
  do parágrafo dela no `context:` do `config.yaml`. Não é reabertura: é o texto
  já decidido mudando de branch, para que a 24 tenha onde se apoiar e para que a
  referência da change arquivada da #65 pare de apontar para o vazio. A branch
  da #52 descarta a própria cópia no rebase.
- **Convenção 24 — o ciclo de vida da issue pelo board**, com a tabela das cinco
  colunas, o gatilho de cada movimento, e **os nomes reais das colunas**
  (`Backlog`, `Ready`, `In progress`, `In review`, `Done` — minúsculas na
  segunda palavra, conferido no campo `Status` do projeto).
- **A regra `archive` antes de `push`**, com o motivo escrito junto, e o
  **gatilho de `In review` reescrito em três condições**: change arquivada, push
  feito, PR aberto.
- **A consequência que a regra tem sobre o padrão do repositório**: ela **inverte
  um padrão com cinco precedentes** — os PRs #37, #39, #41, #42 e #70 são todos
  `chore/archive-*` abertos **depois** do PR de código. Com archive antes do
  push, esse segundo PR deixa de existir: um PR só, com código e specs
  sincronizadas juntos.
- **A fonte autoritativa da fila passa a ser declarada**: a ordem de execução
  mora no `02-HISTORICO_E_STATUS.md`; o board mostra **em que coluna** cada issue
  está, não em que posição. `Priority` e `Iteration` seguem vazias, por decisão,
  e a decisão fica escrita com o motivo — o índice travado em 20 itens já impede
  o board de ser fonte confiável da fila.
- **A regra do `Closes #<issue>`**, com as duas precisões que ela precisa
  carregar: `Closes` fecha no **merge**, não na aprovação; e **uma issue por PR**
  — achado que virou issue própria entra como `Refs`, não como `Closes`.
- **O que é automático e o que é manual**, separado pelos sete workflows medidos,
  para ninguém mover à mão o que já se move.
- **A convenção de nome de branch `<tipo>/<numero>-<nome>`** fica, a valer daqui
  em diante. As 20 branches anteriores são `<tipo>/<nome>`; as três novas (#52,
  #69, #70) já seguem o formato com número.
- **O precedente do #69 registrado com a causa** (convenção 9), não escondido:
  ele abriu com a change ativa e **já foi mergeado** em 24/09 02:58, com o #70
  arquivando a change às 03:13. Não há decisão a tomar sobre ele — a regra passa
  a valer daqui, e o que aconteceu antes dela não é violação retroativa.
- **Duas perguntas registradas como não decididas**, em vez de omitidas: o
  destino da issue cuja change é abandonada, e se `Ready` tem limite de itens
  (o GraphQL de Projects v2 não expõe limite de coluna — é conferência de UI).

**Nenhum código. Nenhum app tocado. Nenhuma spec de comportamento de sistema.**

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `repository-documentation`: ganha três requisitos **ADDED** — o ciclo de vida
  da issue pelo board documentado ao lado das convenções, a declaração de qual é
  a fonte autoritativa da fila, e o fluxo de trabalho mínimo alcançável pelo
  bloco `context:` do `openspec/config.yaml`. Nenhum requisito existente muda.

## Impact

**Nenhum app.** `apps/api`, `apps/workers`, `apps/inbox` e `apps/frontend` não
são tocados — nenhum arquivo desta change vive dentro de `apps/`. Nenhuma `libs/`
nasce ou muda. Nenhuma migração, nenhuma rota, nenhum teste de aplicação.

| arquivo | o que muda | por que aqui |
|---|---|---|
| `01-ARQUITETURA_E_CONVENCOES.md` | convenção 23 trazida da branch da #52 + convenção 24 nova | é onde as convenções moram, e a 24 só faz sentido encostada na 23 |
| `openspec/config.yaml`, bloco `context:` | o parágrafo da 23 + a linha curta da 24 | alcança quem abre a próxima change sem ter lido o `01` |
| `02-HISTORICO_E_STATUS.md` | quando a regra passou a valer, o precedente do #69, o estado do board conferido, o que ficou não decidido, e o fechamento da 18ª medição da convenção 18 | é o registro histórico, e o board conferido é referência medida (convenção 22) |
| `openspec/changes/fluxo-de-trabalho-no-board/**` | os artefatos desta change | — |

**`docs/` não é tocado**: não há hoje arquivo de fluxo de trabalho em `docs/`
(`README.md`, `a2a-integration.md`, `architecture.md`, `configuration.md`,
`conventions.md`, `deployment.md`, `development.md`, `marca/` — conferido por
leitura em 24/09/2026), e criar um seria escopo novo, não registro.

**Dependência de ordem com a #52**: esta change leva a convenção 23 para a
`main`. Quando a #52 for rebaseada, o commit
`d9ba259 docs(openspec): propõe a página Insights do sistema e fixa a convenção
de issue por change` precisa **descartar a parte de convenção** e manter só os
artefatos da change de frontend. Está escrito como tarefa e como item aberto no
`02`.

**Risco de plataforma herdado, não introduzido**: enquanto o índice de itens do
board não destravar, a leitura do board por `project.items` mente por omissão. A
documentação diz isso explicitamente em vez de descrever um board que funciona.
