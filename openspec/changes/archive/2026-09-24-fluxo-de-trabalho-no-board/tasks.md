**Nenhuma tarefa roda dentro de `apps/`.** Esta change não toca `apps/api`,
`apps/workers`, `apps/inbox` nem `apps/frontend` — o escopo inteiro é registro
na raiz do repositório e em `openspec/`. A coluna "onde roda" de cada tarefa é,
por construção, a raiz.

## 0. Antes de escrever

- [x] 0.1 Mover a **#68** para `In progress` no board — **feito em 24/09/2026**,
      na abertura da change, e conferido pelo lado da issue
      (`issue.projectItems` → `In progress`), que C2 torna a única leitura
      confiável. O gatilho é a abertura da change, não o primeiro commit.
- [x] 0.2 Conferir que a `main` continua com o `01-ARQUITETURA_E_CONVENCOES.md`
      terminando na convenção **22** e que
      `feat/52-insights-pagina-do-sistema` continua **não pushada**
      (`git branch -a` não lista `remotes/origin/feat/52-*`). Se qualquer uma
      das duas mudou, a **D1** precisa ser revista antes de qualquer edição.
- [x] 0.3 Reconferir **C2** no momento da escrita — `gh project item-list 3
      --owner aldovrando-oliveira` e o GraphQL de `projectV2(number:3){items}`.
      **Reconferido em 24/09/2026 na aplicação: persiste.** CLI e GraphQL em
      `totalCount: 20` (`#45`–`#64`); #65 `Done`, #66/#67 `Backlog` e #68
      `In progress` visíveis só pelo lado da issue. O texto do `02` diz
      "persiste".

## 1. Trazer a convenção 23 para a `main`

- [x] 1.1 Extrair do commit `d9ba259` da branch `feat/52-insights-pagina-do-sistema`
      as **26 linhas** que ele acrescenta ao `01-ARQUITETURA_E_CONVENCOES.md` e
      colá-las no fim do arquivo na branch desta change, **sem alterar uma
      letra** (`git show d9ba259:01-ARQUITETURA_E_CONVENCOES.md`).
- [x] 1.2 Extrair do mesmo commit as **5 linhas** que ele acrescenta ao bloco
      `context:` de `openspec/config.yaml` e colá-las no mesmo lugar, também
      verbatim.
- [x] 1.3 Conferir por `git diff` que o texto trazido é **idêntico** ao da
      branch da #52 — `diff <(git show d9ba259:01-ARQUITETURA_E_CONVENCOES.md)
      01-ARQUITETURA_E_CONVENCOES.md` deve acusar **só** a convenção 24 como
      diferença, depois que ela existir.

## 2. Escrever a convenção 24 no `01-ARQUITETURA_E_CONVENCOES.md`

- [x] 2.1 Abrir a convenção **24** logo depois da 23, com a forma curta na
      primeira frase — o ciclo de vida da issue é o que a 23 não diz.
- [x] 2.2 A **tabela das cinco colunas**, com os nomes exatamente como o campo
      `Status` os grava (D10): `Backlog`, `Ready`, `In progress`, `In review`,
      `Done`. Colunas da tabela: o que está lá · quem move · quando.
- [x] 2.3 A regra **`archive` antes de `push`** (D2), com **o motivo na mesma
      passagem** e a exceção explícita do dono declarada. Regra sem motivo vira
      ritual, e é essa frase que impede.
- [x] 2.4 O gatilho de `In review` nas **três** condições — change arquivada,
      push feito, PR aberto —, e a razão de serem três: PR aberto cedo demais
      deixa a issue em revisão com a change ainda mutável.
- [x] 2.5 A **consequência sobre o padrão de dois PRs** (C5): o
      `chore/archive-*` deixa de existir; código e specs sincronizadas entram no
      mesmo PR. Citar que o padrão anterior tem cinco ocorrências (#37, #39,
      #41, #42, #70), para a próxima change não o repetir por hábito.
- [x] 2.6 A regra do **`Closes #<issue>`** (D5), com as duas precisões: fecha no
      **merge**, não na aprovação; e **uma issue por PR** — achado que virou
      issue própria entra como `Refs`.
- [x] 2.7 A separação **automático × manual** (C3), em três baldes e não dois:
      automático verificado (a issue fecha no merge pelo `Closes`), automático
      inferido (o cartão vai para `Done` quando a issue fecha — sete workflows
      habilitados, observado na #65), manual (as quatro transições anteriores).
      **Não afirmar a configuração de nenhum workflow**, que o GraphQL não
      expõe.
- [x] 2.8 A **fonte autoritativa da fila** (D3): a ordem mora no
      `02-HISTORICO_E_STATUS.md`; o board diz coluna, não posição. `Priority` e
      `Iteration` vazias **por decisão**, com o motivo (C1 e C2) e com o gatilho
      observável de recalibração escrito junto (convenção 22).
- [x] 2.9 A **bloqueante na mesma coluna, acima da bloqueada** (D4), com o caso
      da #52/#65 como exemplo, e `blocked-by` nativo do GitHub como registro —
      não prosa no corpo da issue.
- [x] 2.10 A convenção de **nome de branch `<tipo>/<numero>-<nome>`** (D6), a
      valer daqui em diante, dizendo que as 20 anteriores ficam como estão.
- [x] 2.11 As **duas perguntas não decididas**, escritas como não decididas:
      change abandonada, e limite de itens em `Ready`.

## 3. `openspec/config.yaml` — a linha que alcança quem não leu o `01`

- [x] 3.1 Acrescentar ao bloco `context:`, depois do parágrafo da convenção 23
      trazido na 1.2, **só o que diz respeito a quem escreve artefato de
      change**: abrir a change → `In progress`; **archive antes de push e de
      PR**; PR com `Closes #<issue>` → `In review`.
- [x] 3.2 Apontar para a convenção **24** do `01-ARQUITETURA_E_CONVENCOES.md`
      pelo número e pelo arquivo, **sem repetir o motivo por extenso** — o
      motivo mora no `01`.
- [x] 3.3 Conferir que o YAML continua válido (`openspec validate --all` roda
      sobre ele) e que o bloco `context:` segue sendo um escalar `|` bem
      formado.

## 4. `02-HISTORICO_E_STATUS.md` — o registro

- [x] 4.1 Abrir a entrada com o cabeçalho no padrão da casa:
      `## fluxo-de-trabalho-no-board — 24/09/2026 · issue #68 · complementa a
      convenção 23`.
- [x] 4.2 Registrar **quando a regra do archive antes do push passou a valer** —
      a partir desta change — e que ela não retroage.
- [x] 4.3 Registrar o **precedente do #69 com a causa** (convenção 9, D7): abriu
      PR com a change ativa, **já mergeado** em 24/09 02:58:50Z, com o #70
      arquivando a change às 03:13:05Z. Não havia regra a violar. E registrar
      que ele **não é caso isolado** — é a quinta ocorrência do padrão de dois
      PRs (C5), com os cinco números.
- [x] 4.4 Registrar o **estado do board conferido**, com a data e o método
      (convenção 22 — número nasce com o estado ao lado): C1 (views e campos
      vazios), C2 (índice travado em 20, medido por CLI **e** GraphQL, com as
      quatro issues presentes pelo lado da issue), C3 (sete workflows
      habilitados, configuração não exposta). **Cada um com o gatilho de
      recalibração escrito.**
- [x] 4.5 Registrar **C4 como achado**: a convenção 23 não estava na `main`, e a
      change arquivada da #65 já a citava. É a convenção 6 na forma que ela
      mesma nomeia — referência que sobrevive ao archive apontando para o que
      não existe.
- [x] 4.6 Registrar o **item aberto do rebase da #52**, com o SHA `d9ba259`
      escrito: o commit precisa descartar a parte de convenção e manter só os
      artefatos da change de frontend. **Gatilho: o próprio rebase da #52.**
- [x] 4.7 Registrar os **não decididos** (Open Questions 1 e 2 do `design.md`),
      como não decididos.
- [x] 4.8 Registrar o **achado de roteamento** (Open Question 3): a change
      arquivada da #65 mandou o refinamento da régua *"o par não é universal"*
      para a #68, e ele é **régua de teste**, não fluxo de board. Esta change
      **não** o executa, e o destino certo fica não decidido — candidatos são a
      convenção 5 e a 10. Instrumento é issue própria (convenção 23).

## 5. Fechar a décima oitava medição da convenção 18

- [x] 5.1 Comparar a projeção da seção *Projeção de tamanho* do `design.md` com
      o medido. **Não revisar a projeção** — só comparar.
- [x] 5.2 Declarar as **três colunas de código em zero** como **resultado**:
      teste escrito à mão **0**, duplo **0**, gerado **0**. É a primeira change
      da série com as três em zero, e o interesse é saber se a coluna de
      registro erra mais quando é a única coluna.
- [x] 5.3 Declarar as **três categorias de guarda em zero** — novo, reforçado e
      **adaptado** —, vazias por construção. `openspec validate --all` e
      `scripts/check-docs.py` são guardas preexistentes e **não modificados**:
      rodá-los não os conta em nenhuma categoria.
- [x] 5.4 Medir as linhas de registro por destino, com `git diff --stat` e
      `wc -l`, e comparar linha a linha com a tabela projetada. **Marcar quais
      linhas da projeção eram medição** (as 26 e as 5 do diff da #52, e o
      `proposal.md`/`design.md` já escritos) e quais eram projeção de verdade —
      a comparação só vale sobre as segundas.
- [x] 5.5 Conferir as **duas direções de erro nomeadas de antemão** no
      `design.md`, e escrever qual aconteceu — ou que nenhuma aconteceu e o
      desvio veio de outro lugar, que já foi o resultado da terceira medição.
- [x] 5.6 Anotar se a régua *"projeção de registro cresce com os achados"* se
      confirmou: as conferências renderam quatro achados fora do enunciado
      (C4–C7) **antes** de projetar; o fechamento diz se rendeu mais depois.

## 6. Verificação

- [x] 6.1 `openspec validate --all` verde.
- [x] 6.2 `python3 scripts/check-docs.py` verde — o `01` é lido por ele, e a
      edição pode quebrar link ou contagem.
- [x] 6.3 Conferir os **nomes das colunas** escritos no `01` contra o campo
      `Status` real: `gh project field-list 3 --owner aldovrando-oliveira`. É a
      asserção da D10, e ela é exatamente do tipo que ninguém abre o arquivo
      para checar (convenção 6).
- [x] 6.4 Conferir que **nenhum arquivo fora da lista da seção 7** aparece em
      `git status`.
- [x] 6.5 Conferir que **nenhuma linha do `CHANGELOG.md` mudou** (D8) — a
      ausência é decisão, e a conferência é o que prova que foi decisão.

## 7. Conferência de escopo — a lista fechada

Montada por **leitura do repositório** em 24/09/2026. Antes de fechar, confirmar
com `git status` que o diff contém **apenas** o que está na primeira tabela.

**Pode ser tocado:**

| caminho | o que muda |
|---|---|
| `01-ARQUITETURA_E_CONVENCOES.md` | convenção 23 trazida verbatim do `d9ba259` + convenção 24 nova |
| `openspec/config.yaml` | bloco `context:` — o parágrafo da 23 (verbatim) + a linha curta da 24 |
| `02-HISTORICO_E_STATUS.md` | a entrada da seção 4 e o fechamento da seção 5 |
| `openspec/changes/fluxo-de-trabalho-no-board/**` | os artefatos desta change |

**Não pode ser tocado, e a razão de cada um:**

| caminho | razão |
|---|---|
| `apps/api/**` · `apps/workers/**` · `apps/inbox/**` · `apps/frontend/**` | nenhuma linha de código; change de registro puro |
| `libs/**` | nada a compartilhar, nada a extrair |
| `tests/**` · `apps/*/tests/**` | nenhum comportamento de sistema muda; as três categorias de guarda são zero por construção |
| `openspec/specs/**` | o delta desta change **não** é sincronizado à mão — quem sincroniza é o `/opsx:archive` |
| `openspec/changes/insights-pagina-do-sistema/**` | é a change da #52, noutra branch; esta change não a edita, só registra o item do rebase |
| `openspec/changes/archive/**` | archive não se reescreve; a citação errada da #65 à convenção 23 é **registrada** no `02`, não corrigida no arquivo arquivado |
| `CHANGELOG.md` | D8 — nada que um usuário ou operador observe mudou |
| `docs/**` | D9 e C8 — não há arquivo de fluxo de trabalho lá, e criar um é escopo novo |
| `CONTRIBUTING.md` | fluxo interno de board não é instrução para contribuidor externo |
| `scripts/check-docs.py` | é guarda preexistente; roda, não muda (convenção 15) |
| `.github/**` | não se liga nem desliga workflow nesta change — o fluxo é documentado, não alterado |
| o board do GitHub | salvo a movimentação da **#68** pelas colunas (tarefas 0.1 e 8.1), nenhum campo é preenchido, nenhuma view é criada (D3) |

## 8. Fechamento — e esta change é a primeira a seguir a regra que escreve

- [x] 8.1 **Nada de push e nada de PR até aqui.** Rodar `/opsx:archive`, com as
      specs sincronizadas, **antes** do primeiro `git push`. Se algo obrigar a
      inverter, é exceção do dono e vai escrita no `02` (D2).
- [ ] 8.2 Depois do archive: push da branch `docs/68-fluxo-de-trabalho-no-board`
      (D6 — o número da issue no nome).
- [ ] 8.3 Abrir o PR com **`Closes #68`** no corpo, e mover a #68 para
      `In review` — as três condições da 2.4 estarão satisfeitas nesta ordem.
- [ ] 8.4 **Não** abrir PR `chore/archive-*` depois (2.5): as specs
      sincronizadas entram neste mesmo PR. Se o hábito puxar para o segundo PR,
      é o sinal de que a 2.5 precisava mesmo estar escrita.
- [ ] 8.5 Conferir depois do merge que a #68 fechou pelo `Closes` e que o cartão
      foi para `Done` **sozinho** — é a verificação empírica da 2.7, e o
      resultado (automático ou manual) volta para o `02`.
