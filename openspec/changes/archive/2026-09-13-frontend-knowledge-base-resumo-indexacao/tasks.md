Todas as tarefas de código rodam em **`apps/frontend`**. Nenhuma toca `apps/api`,
`apps/workers` ou `apps/inbox`. As tarefas de documentação rodam na **raiz** do
repositório.

## 1. Baseline (convenção 19)

- [x] 1.1 **(`apps/frontend`)** Confirmar que nenhum navegador da conferência de
      protótipo está vivo (`pgrep -f remote-debugging-port`) e que o `load` da
      máquina está abaixo de 4 **antes** de medir. A baseline desta change já
      reprovou uma vez por contenção — 6 timeouts de 15 s no mesmo commit limpo,
      com `load` 23,95 e Chrome headless aberto —, e a mesma suíte passou
      722/722 com `load` 3,57. Sem este passo, "pré-existente" e "ambiental" não
      podem ser afirmados.
- [x] 1.2 **(`apps/frontend`)** Rodar `npm test` na árvore limpa e guardar a
      **saída completa** em `~/.cache/buteco-agents/kb-5a3-baseline/`. Registrar
      commit, contagem de arquivos/testes, duração e `load` na largada.
      Referência já medida em `b58e6e8`: 75 arquivos / 722 testes, 116,90 s.

## 2. Contrato do fio e acesso à API (`apps/frontend`)

- [x] 2.1 **(`apps/frontend`)** Acrescentar `KnowledgeBaseIndexingSummary` a
      `types/knowledgeBase.ts`, com os quatro campos do fio conferidos em
      `KnowledgeBaseIndexingSummaryTests.SummaryResponse_UsesCamelCaseFieldNamesOnTheWire`:
      `knowledgeBaseId`, `documentCount`, `indexedCount`, `failedCount`. Nenhum
      opcional — o recurso devolve os quatro sempre. Comentar no tipo **por que o
      zero de `documentCount` é exibível** e o de `fragmentCount` não, apontando a
      distinção para a docstring do response (`design.md`, D4); é a frase que
      impede a etapa seguinte de "consertar" a exibição do zero.
- [x] 2.2 **(`apps/frontend`)** Acrescentar `listKnowledgeBaseIndexingSummary()`
      a `api/knowledgeBasesApi.ts`, usando o `request<T>` **da própria feature** —
      sem cliente HTTP compartilhado (convenção 7). Rota:
      `GET /knowledge-bases/indexing-summary`.
- [x] 2.3 **(`apps/frontend`)** Testes de `knowledgeBasesApi.test.ts` para a
      função nova: caminho feliz com a rota exata, e o par vazio — lista vazia
      devolvida sem erro (convenção 5, "todo caso com item ganha o par sem item").

## 3. Regras puras das duas colunas e do filtro (`apps/frontend`)

Tudo em `utils/indexingSummary.ts`, testável sem montar componente, no molde de
`utils/documentIndexing.ts`. O comentário do arquivo carrega a causa, como lá.

- [x] 3.1 **(`apps/frontend`)** `summaryById(summary)` — monta o `Map` de
      `knowledgeBaseId` → item. É o ponto que garante o casamento por
      identificador de D6; não existe caminho posicional no código.
- [x] 3.2 **(`apps/frontend`)** `documentCountLabel(item)` — `null` quando não há
      item (dado desconhecido), `Nenhum` quando `documentCount` é zero, e a
      contagem com singular/plural quando há documentos. Os dois retornos `null`
      e `'Nenhum'` são estados **diferentes** e o comentário diz por quê.
- [x] 3.3 **(`apps/frontend`)** `indexingParts(item)` — devolve as parcelas a
      exibir: indexados, **em andamento** (por subtração,
      `documentCount − indexedCount − failedCount`) e falhas, omitindo as zeradas.
      Devolve lista vazia quando `documentCount` é zero (célula em branco, D5) e
      `null` quando não há item (dado desconhecido, D3). Nunca produzir parcela
      negativa.
- [x] 3.4 **(`apps/frontend`)** `hasFailure(item)` — `failedCount > 0`, e `false`
      quando não há item.
- [x] 3.5 **(`apps/frontend`)** `effectiveStatusFilter(selected, summaryAvailable)`
      — devolve `todas` quando o filtro selecionado é `com falha` e o resumo não
      está disponível (D7). É a função que impede a listagem de esvaziar
      afirmando ausência de falha.
- [x] 3.6 **(`apps/frontend`)** Testes de `indexingSummary.test.ts`, com o par
      com/sem item em cada função (convenção 5) e o cenário negativo da regra:
      nenhum arranjo de contagens produz as palavras `pendente` ou `indexando`.
      **Este é o guarda de dentro, e não é o que protege a tela** — o guarda que
      reprova contra a reintrodução é o de DOM, na tarefa 5.6. Escrever os dois
      não é redundância: eles pegam defeitos diferentes, e a tabela de D5 diz
      qual pega o quê.

## 4. Consulta do resumo (`apps/frontend`)

- [x] 4.1 **(`apps/frontend`)** `useKnowledgeBaseIndexingSummaryQuery()` em
      `api/useKnowledgeBases.ts`, com chave `['knowledge-bases', 'indexing-summary']`.
      A chave é escolha, não acaso: o prefixo faz toda mutação de base invalidar o
      resumo de graça (D8). **Sem `refetchInterval`** — comentar a recusa do
      polling e o gatilho que a reabriria.
- [x] 4.2 **(`apps/frontend`)** Testes em `useKnowledgeBases.test.ts`: a consulta
      devolve os itens; e a invalidação por prefixo realmente alcança o resumo
      após uma mutação de base — afirmar o mecanismo, não confiar nele.

## 5. Colunas no catálogo (`apps/frontend`)

- [x] 5.1 **(`apps/frontend`)** `KnowledgeBaseTable.tsx`: de três para cinco
      colunas, na ordem do protótipo —
      `Base | Documentos | Indexação | Consultada por | Estado`. O resumo entra
      por **propriedade**, buscado pela página (convenção 7), opcional como
      `agents` já é.
- [x] 5.2 **(`apps/frontend`)** Substituir o comentário do arquivo que hoje
      explica **por que as colunas não existem** pelo que explica os três estados
      de célula (valor, vazia, `—`) e por que o travessão nunca significa zero
      (D3). O comentário atual vira mentira no instante em que a coluna nasce.
- [x] 5.3 **(`apps/frontend`)** Aplicar o tom de erro **apenas** à parcela de
      falha, com a cor semântica `red` — a mesma que `statusPresentation` usa
      para `Failed` na tabela de documentos (D10). A parcela de falha precisa ser
      **elemento próprio** na célula, e não texto concatenado: é o que torna o tom
      observável por teste e é o que a spec exige ao pedir que só ela receba
      alerta. Nenhum `gray[n]`/`dark[n]` literal, nenhuma variável de tema nova
      (D11).
- [x] 5.4 **(`apps/frontend`)** **Não** implementar os badges `Falha` e
      `Nada indexado` na coluna `Estado` (D9). A tarefa existe para que a ausência
      seja decisão registrada e não esquecimento.
- [x] 5.5 **(`apps/frontend`)** Testes de `KnowledgeBaseTable.test.tsx`: base com
      documentos; base com `documentCount` zero (célula de indexação **em
      branco**, e a asserção negativa de que não exibe zero nem `Nenhum
      documento`); base com falha; base **sem linha no resumo** exibindo `—` com
      asserção negativa contra o zero; e o cenário de arranjo próprio de R2 —
      resumo em ordem **diferente** do catálogo, afirmando a ordem exibida **e**
      a contagem por linha.
- [x] 5.6 **(`apps/frontend`)** **A asserção negativa de D5, sobre o DOM.**
      Afirmar que o texto renderizado da célula de indexação **não contém**
      `pendente` nem `indexando`, e que o complemento não terminal aparece como
      **uma** parcela — inclusive com o complemento maior que um, que é o arranjo
      em que a distinção seria tentadora. É este guarda, e não o da tarefa 3.6,
      que reprova quando alguém monta as duas parcelas direto na célula sem tocar
      a função pura (convenção 15, guarda no componente certo).
- [x] 5.7 **(`apps/frontend`)** Testes do tom (requisito novo da spec): a parcela
      de falha carrega a cor semântica de erro; a parcela `em andamento` **não**
      carrega tom de alerta em base sem falha; e a cor é a mesma que
      `statusPresentation` devolve para `Failed`, afirmada contra a função e não
      com a string repetida no teste. jsdom não enxerga cor, e por isso o que se
      afirma é **contrato** — qual elemento recebe qual cor —, que é exatamente o
      que a convenção 14 diz que a suíte pode cobrir.

## 6. Página do catálogo (`apps/frontend`)

- [x] 6.1 **(`apps/frontend`)** `KnowledgeBaseListPage.tsx`: declarar a consulta
      do resumo ao lado da de bases, sem encadeamento nem `enabled` cruzado (D2),
      e repassar por propriedade.
- [x] 6.2 **(`apps/frontend`)** Quarta opção `Com falha` no `SegmentedControl`,
      com `disabled` por item quando o resumo não está disponível — propriedade
      conferida na tipagem instalada (`@mantine/core@9.4.2`,
      `SegmentedControl.d.ts:6-10`). Aplicar `effectiveStatusFilter` na filtragem.
- [x] 6.3 **(`apps/frontend`)** Substituir o comentário de `matchesStatus` que
      hoje justifica as **três** opções: ele cita D9 da 5a-1, e essa causa deixou
      de valer. Escrever o que mudou — o dado, não a regra.
- [x] 6.4 **(`apps/frontend`)** Informar na tela, quando o resumo falhar, que ele
      não pôde ser carregado, sem derrubar a listagem (D2).
- [x] 6.5 **(`apps/frontend`)** Testes de `KnowledgeBaseListPage.test.tsx`. **Ler
      primeiro o `vi.mock('../api/knowledgeBasesApi', …)` existente**: ele é um
      mock **total** (fábrica sem `importOriginal`), então a função nova precisa
      entrar nele ou a página recebe `undefined`. Cenários: colunas preenchidas;
      filtro `Com falha` isolando a base com falha, inclusive inativa; resumo
      indisponível com a opção **desabilitada**; e o arranjo próprio de R3 —
      `Com falha` selecionado **e** resumo ausente, afirmando que a listagem
      **não** esvazia.

## 7. Verificação dos guardas (convenção 15) — cada um visto REPROVAR

Um guarda só vale depois de falhar contra o defeito real, **e no componente que a
correção toca**. Cada item abaixo manda reintroduzir o defeito, ver reprovar,
desfazer e ver passar. Registrar o resultado medido no `design.md`.

- [x] 7.1 **(`apps/frontend`)** Zerar a célula sem linha no resumo (devolver
      `'Nenhum'` em vez de `null` em `documentCountLabel`) e ver reprovar o
      cenário negativo de R1. Conferir que a reprovação acontece na asserção
      negativa, e não por outro motivo.
- [x] 7.2 **(`apps/frontend`)** Trocar o casamento por `Map` por casamento
      **posicional** (índice da linha) e ver reprovar o cenário de R2. Este é o
      guarda de maior risco de passar verde com o defeito: como as duas respostas
      usam hoje o mesmo critério de ordenação, um teste cujo resumo estivesse na
      mesma ordem passaria com o defeito presente — é a quinta forma registrada na
      convenção 15. Conferir que o teste monta o resumo **fora de ordem**.
- [x] 7.3 **(`apps/frontend`)** Remover `effectiveStatusFilter` (deixar o filtro
      `Com falha` atuar com o resumo ausente) e ver a listagem esvaziar,
      reprovando o cenário de R3.
- [x] 7.4 **(`apps/frontend`)** Reintroduzir a distinção **na tabela**, não na
      função pura: montar na célula duas parcelas, `{n} pendente` e
      `{n} indexando`, a partir de um rateio qualquer do complemento, sem tocar
      `indexingSummary.ts`. Registrar as **duas** metades do resultado: a asserção
      de DOM da tarefa 5.6 **reprova**, e o teste da função pura da tarefa 3.6
      **continua verde**. A segunda metade é a evidência de que o guarda precisava
      estar no componente — sem ela, o item fica valendo o mesmo que não ter
      verificado.
- [x] 7.4a **(`apps/frontend`)** Pintar a parcela `em andamento` com a cor de
      erro e ver reprovar o cenário do requisito de tom. **Registrar as duas
      metades, como na 7.4**: qual cenário reprovou, e qual **continuou verde**.
      Os dois cenários de tom vivem na mesma célula, então um guarda que reprove
      pelos dois motivos está afirmando no componente errado — "o guarda pegou" e
      "algum guarda pegou" só se distinguem com a segunda linha escrita.
- [x] 7.5 **(`apps/frontend`)** Reintroduzir um tom fixo da escala neutra **dentro
      de um ternário** em `KnowledgeBaseTable.tsx` e ver `surfaceTokens.test.ts`
      reprovar. O ternário não é capricho: foi exatamente a forma que furou esse
      guarda antes (convenção 15).

## 8. Conferência manual (convenção 14) — tarefa própria e ITERATIVA

A suíte roda em jsdom e não enxerga cor, contraste nem layout. Cada correção muda
o que fica visível, então a conferência é iterativa e cada rodada registra o que
achou.

- [x] 8.1 **(`apps/frontend`)** Subir o painel contra um stub de `apps/api` que
      sirva `GET /knowledge-bases` e `GET /knowledge-bases/indexing-summary` com
      estado **estático** — a semente do protótipo anima os não terminais em
      segundos, e o que se quer olhar com calma é o estado parado.
- [x] 8.2 **(`apps/frontend`)** Percorrer o catálogo nos **dois esquemas de cor**,
      a **1860px**, com estes estados nomeados um a um: base com falha; base só
      com indexados; base com parcela `em andamento`; base com `documentCount`
      zero; base **sem linha no resumo**; resumo indisponível com a opção
      desabilitada; filtro `Com falha` aplicado.
- [x] 8.3 **(`apps/frontend`)** **Comparar dimensão, não só estado.** A largura
      das cinco colunas é o que muda nesta tela, e a 5b registrou ter deixado
      passar um defeito por comparar apenas estados. Conferir que a coluna `Base`
      não encolheu a ponto de truncar nome curto e que as duas colunas novas não
      empurram `Consultada por` para fora.
- [x] 8.4 **(`apps/frontend`)** Repetir até uma rodada não achar nada, e registrar
      no `design.md` o que cada rodada achou — inclusive a rodada vazia.
- [x] 8.5 **(`apps/frontend`)** Nomear explicitamente o que a conferência
      automatizada **não** cobriu, para ir à validação do operador como item
      nomeado e não como "conferir a tela".

## 9. Fechamento da suíte (convenção 19)

- [x] 9.1 **(`apps/frontend`)** **Encerrar o navegador da conferência** antes de
      medir, e esperar o `load` cair. É o passo que a baseline desta change
      provou necessário.
- [x] 9.2 **(`apps/frontend`)** `npm test` com a saída completa guardada, e a
      comparação com a baseline da tarefa 1.2. Qualquer reprovação passa pelo
      `git worktree` limpo antes de receber rótulo — e baseline vermelha remove a
      hipótese de regressão e **só** isso.
- [x] 9.3 **(`apps/frontend`)** `npm run lint`, `npm run format:check` e
      `npm run build` (que roda `tsc -b`).

## 10. Documentação — um artefato por tarefa

Cada item manda **ler o que já está escrito** antes de escrever: o defeito caro
não é a frase desatualizada, é a frase que virou **falsa**, e
`scripts/check-docs.py` não tem como pegá-la.

- [x] 10.1 **(raiz)** `README.md`: procurar e corrigir toda afirmação sobre o que
      o catálogo de bases mostra. Conferir em particular se alguma frase diz que
      contagem de documentos ou estado de indexação só é visível no detalhe.
- [x] 10.2 **(raiz)** `docs/architecture.md`: atualizar a descrição do catálogo
      de bases na seção de `apps/frontend`, nomeando a segunda requisição e o
      fato de o custo não crescer com o número de bases.
- [x] 10.3 **(raiz)** `CHANGELOG.md`: entrada em `[Unreleased]`, em pt-BR,
      nomeando as duas colunas e o filtro por falha.
- [x] 10.4 **(raiz)** `01-ARQUITETURA_E_CONVENCOES.md`: registrar, na convenção
      13, o **par de zeros** — medido versus default de coluna — como o exemplo
      que decide se um zero pode ser exibido; e, na convenção 19, que a ferramenta
      da conferência de protótipo e a suíte disputam a máquina, com a medição
      desta change (23,95 → 6 reprovações; 3,57 → 722 passando).
- [x] 10.5 **(raiz)** `02-HISTORICO_E_STATUS.md`: registrar a etapa 5a-3 e
      atualizar a tabela da fila da linha de bases de conhecimento — resta o
      backend do diagnóstico do índice (não proposto) e a 5c (bloqueada por ele).
- [x] 10.5a **(raiz)** `02-HISTORICO_E_STATUS.md`: **desfazer a carga indevida na
      linha da 5a-3** (D13). A tabela diz que a 5a-3 é onde entram a orientação de
      generalidade da `Description` e o nome efetivo da tool; os dois vivem em
      `KnowledgeBaseForm`, que esta change não abre. Dar a eles **linha própria na
      fila**, com posição — os dois itens abertos têm gatilho apontando para "a
      change que já vai tocar `KnowledgeBaseForm`", e gatilho sem posição é a
      família de defeito que esta base já registrou três vezes. Atualizar também
      os dois itens abertos (`Description` genérica e nome efetivo da tool) para
      apontar para essa linha nova, e não para a 5a-3.
- [x] 10.6 **(raiz)** `02-HISTORICO_E_STATUS.md`, lista viva de correções de
      protótipo: de **oito** para **onze**, acrescentando C9 (a coluna distingue
      pendente de indexando e a rota não), C10 (o badge `Nada indexado` dispara em
      base sem documento e em base com documento indexando) e C11 (a célula
      inteira em tom de aviso mistura progresso com falha). A oitava — a soma de
      fragmentos filtrada por estado — **já foi registrada** ao revisar esta
      proposta, com a distinção de tipo; conferir que continua lá e que nada desta
      change a renumerou. Registrar que C10 e C11 saíram de **dirigir** o
      protótipo, não de ler o `CONHECIMENTO.md` — C10 só aparece depois de ativar
      a base inativa da semente.
- [x] 10.7 **(raiz)** `scripts/check-docs.py` verde antes do PR.

## 11. Sincronização de spec e archive

- [x] 11.1 **(raiz)** Sincronizar o delta com
      `openspec/specs/knowledge-base-catalog-ui/spec.md`, conferindo que os dois
      requisitos `MODIFIED` substituíram os antigos **por inteiro** — requisito
      modificado com conteúdo parcial perde detalhe no archive.
- [x] 11.2 **(raiz)** **Reescrever o `Purpose` da spec viva antes do archive.** O
      parágrafo atual afirma, como segunda regra que atravessa a capability, que
      *"contagem de documentos e estado de indexação não existem em
      `KnowledgeBaseResponse` e não são exibidos — nem como valor, nem como zero,
      nem como travessão"*. Metade disso continua verdadeira (o campo não existe
      no response) e metade vira **falsa** (eles passam a ser exibidos, de outra
      fonte). O `Purpose` novo tem de dizer a regra que sobrevive — a UI não
      afirma o que o sistema não sabe — e o que mudou: existe um recurso próprio
      cujo zero é medido. `Purpose` deixado como está é o registro que a próxima
      etapa lê como requisito.
- [x] 11.3 **(raiz)** Conferir que nenhum link escrito nesta change aponta para
      `openspec/changes/<nome>/` de mudança já arquivada — sempre
      `openspec/changes/archive/AAAA-MM-DD-<nome>/`.

## 12. Medição (convenção 18)

- [x] 12.1 **(raiz)** Comparar o entregue com a projeção do `design.md`
      (11 arquivos: 2 criados e 9 modificados, ~620 linhas), **decomposto** em
      criados e modificados, e só sobre código — nunca contra o headline do
      commit.
- [x] 12.2 **(raiz)** Registrar a causa de qualquer desvio, separando *erro de
      projeção* de *escopo acrescentado durante a implementação* — medição de
      método só compara o escopo que estava projetado. Se a conferência manual
      acrescentar escopo, contabilizá-lo à parte.

---

## Registro de fechamento

### Documentação — o que foi conferido, e o que não tinha o que mudar

| artefato | resultado |
|---|---|
| `README.md` | **Alterado.** Nenhuma afirmação falsa encontrada — nada dizia que contagem ou indexação só apareciam no detalhe. O parágrafo de bases de conhecimento ganhou uma oração descrevendo o que o catálogo passa a mostrar e o filtro por falha. |
| `docs/architecture.md` | **Conferido, nada a mudar.** A tarefa pedia atualizar "a descrição do catálogo de bases na seção de `apps/frontend`" — **essa seção não existe**: `apps/frontend` aparece só como uma linha da tabela de apps, no nível de aplicação, e ela continua correta. Inventar uma descrição por tela aqui seria escopo que ninguém pediu, e criaria uma segunda fonte de verdade para o que a spec viva já descreve. |
| `CHANGELOG.md` | **Alterado.** Quatro itens em `[Unreleased]`, na linha de bases de conhecimento: as duas colunas e o filtro; a não-distinção pendente/indexando e o tom só na falha; o zero medido; e a degradação com o resumo indisponível. |
| `01-ARQUITETURA_E_CONVENCOES.md` | **Alterado.** Convenção 13 ganhou o par de zeros (medido × default de coluna) com a régua da proveniência e o terceiro caso, a ausência de linha. Convenção 19 ganhou a contenção medida entre a ferramenta da conferência e a suíte, com as duas execuções, mais a armadilha do `pkill` por linha de comando. |
| `02-HISTORICO_E_STATUS.md` | **Alterado.** Etapa registrada, fila atualizada com a 5a-3 aplicada, a **5a-4 criada com posição**, e a lista viva de correções de protótipo de oito para onze. |
| `scripts/check-docs.py` | Verde. |

### A linha nova da fila tem posição, não só existência

Os dois itens abertos que a fila pendurava na 5a-3 — orientação de generalidade da
`Description` e nome efetivo da tool — **vivem em `KnowledgeBaseForm`**, que esta
change não abre. Viraram a **5a-4**, linha 3 da fila, e os dois itens foram
reescritos para apontar para ela.

A correção vale por si: um deles dizia *"gatilho com posição: a próxima change que
tocar `KnowledgeBaseForm` — e a 5a-3 é a próxima da fila que mexe nessa feature,
**ainda que noutra tela**"*. "Ainda que noutra tela" era o furo. **"A próxima
change que tocar X" é gatilho, não posição, mesmo quando a frase começa com
"gatilho com posição".** Posição é uma linha na fila.

### O que fica para a validação do operador, nomeado

- A janela de ~10 s entre o resumo falhar e o aviso aparecer (retry padrão do
  `QueryClient`), com olho humano. A medição diz que a tela não mente nesse
  intervalo; se ela *parece* quebrada é julgamento que a automação não faz.
- A largura real do monitor. A conferência mediu a 1860px; as proporções agora são
  relativas, o que reduz o risco, mas a 5b declarou convergência e o usuário achou
  uma largura errada na tela dele.
- `Consultada por` com lista longa de agentes na largura nova — o stub tem um só.

### A reprovação classificada como contenção era REGRESSÃO

Fechado com correção: uma reprovação de `router.test.tsx` foi registrada como
contenção e **não era**. O passo (1) da discriminação da convenção 19 — rodar o
arquivo isolado — mostrou ~1 em 3 com a máquina descarregada, contra 6/6 na
baseline. Causa isolada por medição: `listKnowledgeBaseIndexingSummary` não
entrava no **mock parcial** daquele arquivo, escapava para a rede, tomava 401 do
`apps/api` real e derrubava o teste **seguinte**. Corrigido; `router.test.tsx`
8/8 e a suíte 765/765, com a impressão digital `Not implemented: navigation`
ausente da saída. Detalhe e lições no `design.md`.

Consequência de contagem: `router.test.tsx` passou a ser arquivo desta change —
**12 arquivos entregues contra 11 projetados**, com a régua nova de blast radius
registrada na convenção 18.

### Dívida pré-existente encontrada e NÃO corrigida

`npm run format:check` reprova **na baseline** (`b58e6e8`), em **10** arquivos;
**agora reprova em 8**, remedido com `prettier --check` depois de tudo pronto.

**A conta não é `10 − 6`**, e é por isso que ela parecia errada: dos 6 arquivos
formatados, só **2** estavam entre os 10 (`knowledgeBasesApi.ts` e
`useKnowledgeBases.test.ts`). Os outros 4 são 2 arquivos **novos** e 2 que a
baseline **aprovava** e que passaram a reprovar por causa desta change — dívida
criada e fechada no mesmo ato. Logo **10 − 2 = 8**. A derivação está no
`design.md`, porque número de dívida sem derivação é o que ninguém reconfere.

Os 8 restantes ficam como estavam e estão nomeados. O comando de verificação está
vermelho na `main`, o que treina qualquer pessoa a ignorá-lo — vale item próprio.

