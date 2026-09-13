Todas as tarefas de código rodam em **`apps/frontend`**. Nenhuma toca `apps/api`,
`apps/workers` ou `apps/inbox`. As tarefas de documentação rodam na **raiz** do
repositório.

## 1. Baseline (convenção 19)

- [x] 1.1 **(`apps/frontend`)** Confirmar que **nenhum navegador da conferência
      está vivo** antes de medir, encerrando **por porta**
      (`lsof -ti :9222 | xargs kill`) e não por padrão de linha de comando — na
      5a-3 o `pkill -f` não casou com nada e o processo velho falseou a medição
      seguinte. Esperar o `load` cair abaixo de 4.
- [x] 1.2 **(`apps/frontend`)** Rodar `npm test` na árvore limpa e guardar a
      **saída completa** em `~/.cache/buteco-agents/kb-5c-baseline/` — não
      filtrada, não resumida, e **sem** `| grep … | head`, que fecha o cano e mata
      o produtor por `SIGPIPE`. Registrar commit, contagem de arquivos/testes,
      duração e `load` na largada. Referência já medida em `b58e6e8`: 75 arquivos
      / 722 testes, 116,90 s — **recalibrar este número aqui**, porque a árvore
      mudou desde então (convenção 22).
      **Medido:** commit `853038c`, load 3,83 na largada, **76 arquivos / 765
      testes, 765 passando, 85,00 s**. Saída completa em
      `~/.cache/buteco-agents/kb-5c-baseline/`. Referência recalibrada: os
      116,90 s da 5a-3 valiam para 75 arquivos / 722 testes.

## 2. Contrato do fio e acesso à API (`apps/frontend`)

- [x] 2.1 **(`apps/frontend`)** Criar `types/knowledgeIndex.ts` com
      `KnowledgeIndexProvenance`: `provider`, `model`, `dimensions`,
      `fragmentCount` — os quatro nomes vindos dos **corpos reais** da rota
      (`design.md` de `knowledge-index-diagnostics`, seção "Corpos reais"), não de
      leitura do C#. Nenhum campo opcional. Comentar no tipo: (a) que a
      proveniência é **global**, não da base; (b) por que `fragmentCount` é
      `number` e não precisa de `bigint` (D2); (c) que o nome real do modelo é
      `qwen-qwen3-embedding-8b`, e que `text-embedding-3-small` / 1536 é **mock do
      protótipo**, não contrato.
- [x] 2.2 **(`apps/frontend`)** Criar `api/knowledgeIndexApi.ts` com
      `listKnowledgeIndexDiagnostics()`, importando `request<T>` de
      `knowledgeBasesApi` — o mesmo que `knowledgeDocumentsApi.ts:1` já faz, sem
      cliente HTTP compartilhado entre features (convenção 7). Rota:
      `GET /knowledge-index/diagnostics`. Comentar **por que o módulo é próprio**
      e não uma função a mais em `knowledgeBasesApi` (D1), citando os 6 arquivos
      que mockam aquele módulo parcialmente.
- [x] 2.3 **(`apps/frontend`)** `api/knowledgeIndexApi.test.ts`: rota exata; o
      **par vazio** — `[]` devolvido sem erro (convenção 5); uma combinação; duas
      combinações; e o 401 seguindo o caminho de `request<T>`.

## 3. Regras puras (`apps/frontend`)

- [x] 3.1 **(`apps/frontend`)** Em `utils/documentIndexing.ts` — o arquivo onde a
      regra já mora, com a causa já comentada — acrescentar
      `indexedFragmentTotal(documents)` e `documentsWithFragmentsCount(documents)`,
      as duas com o predicado **`indexedAt !== null`, qualquer que seja o
      estado**. O comentário SHALL dizer que somar por `status === 'Indexed'`
      subconta o índice, e por qual mecanismo (garantia 3 de D9 da etapa 1), e que
      o protótipo erra a mesma coisa por um **segundo** caminho — `chunks: 0`
      gravado no mock em `reindex` e na atualização de conteúdo — que no painel
      real não existe porque a contagem vem da resposta da API.
- [x] 3.2 **(`apps/frontend`)** Testes em `documentIndexing.test.ts`: o par
      com/sem documento, e o caso que a **semente do protótipo não consegue
      produzir** — documento com `indexedAt` preenchido, estado `Failed` e
      `fragmentCount > 0` contando no total.
- [x] 3.3 **(`apps/frontend`)** Criar `utils/indexDiagnostics.ts` com
      `shouldPollIndexDiagnostics(diagnostics, documents)` — devolve verdadeiro
      **só** com a proveniência vazia **e** `hasNonTerminalDocument(documents)`
      verdadeiro, reusando a função pura que a 5a-2 extraiu (convenção 20). O
      comentário carrega o custo que a segunda metade da condição evita: a rota é
      `Seq Scan` + `HashAggregate` sobre o heap, medida em **0,24–0,82 s com
      150.000 fragmentos**, na VM do podman com `shared_buffers` de 128 MB — as
      páginas valem em qualquer lugar, os milissegundos só naquele disco
      (convenção 22).
- [x] 3.4 **(`apps/frontend`)** `utils/indexDiagnostics.test.ts`: as quatro
      combinações da condição (vazio × não terminal), mais o caso de dados ainda
      indefinidos.

## 4. Consulta da proveniência (`apps/frontend`)

- [x] 4.1 **(`apps/frontend`)** Criar `api/useKnowledgeIndex.ts` com
      `useKnowledgeIndexDiagnosticsQuery({ enabled, documents })`: chave
      `['knowledge-index', 'diagnostics']`, `enabled` condicional (D5) e
      `refetchInterval` em **forma de função**, resolvendo por
      `shouldPollIndexDiagnostics`. Comentar o limite declarado de D6 — documento
      indexando em **outra** base não acorda esta aba, e o `staleTime: 0` /
      `refetchOnMount: true` do `QueryClient` resolvem isso ao remontar.
- [x] 4.2 **(`apps/frontend`)** `api/useKnowledgeIndex.test.ts`, com o guarda
      **pareado** (convenção 20): uma asserção **determinística** que resolve a
      opção real contra a query real do cache — `false` com índice povoado,
      intervalo com índice vazio e documento não terminal — e uma
      **comportamental** com timers falsos provando que a requisição se repete.
      **Tocar `result.current.data` na primeira espera**: com
      `notifyOnChangeProps: 'tracked'`, esperar só por `isSuccess` prende `data` no
      primeiro valor e o `waitFor` seguinte estoura o prazo com a requisição tendo
      funcionado.

## 5. A aba (`apps/frontend`)

- [x] 5.1 **(`apps/frontend`)** Criar `components/KnowledgeIndexDiagnosticsTab.tsx`,
      **apresentacional**: recebe `diagnostics`, `isLoading`, `error`, `documents`,
      `documentsError` e um callback para ir à aba de documentos. Não importa hook
      de query nem de mutation (convenção 7).
- [x] 5.2 **(`apps/frontend`)** Grupo **Como o índice foi construído (sistema)**,
      com as cinco renderizações de D11: carregando; `[]`; uma combinação; duas ou
      mais; e **erro**. O rótulo diz `(sistema)` — é a parte que carrega a
      correção de escala (D7).
- [x] 5.3 **(`apps/frontend`)** Estado vazio: **só a explicação**, sem as três
      linhas de travessão (D9, C12), e sem a palavra "nesta base". A frase diz que
      os dados são lidos do índice, não da configuração pretendida — e **não**
      nomeia provedor nem modelo nenhum.
- [x] 5.4 **(`apps/frontend`)** Estado de corrupção: nomear o estado, listar
      **todas** as combinações **na ordem da resposta**, cada uma com a sua
      contagem de fragmentos, e explicar a consequência (vetores incomparáveis,
      busca sem erro, boot da indexação recusado). **Nenhuma** marcada como atual
      ou correta; **nenhuma** ação de reindexação em massa; **nenhuma** afirmação
      sobre o processo de `apps/workers` estar no chão agora.
- [x] 5.5 **(`apps/frontend`)** Grupo **Volume desta base**: documentos com
      fragmentos no índice (`X de Y`), fragmentos desta base, e — só quando maior
      que zero — documentos em falha, com o controle que leva à aba `Documentos`
      (D10, C13). **Sem** motivo de falha e **sem** botão de reindexar aqui.
      Listagem indisponível → indisponibilidade declarada, nunca zeros. A linha da
      contagem de documentos carrega `data-testid` próprio, para que a asserção
      negativa da tarefa 5.8a possa ser **escopada à linha** — a aba inteira
      contém as palavras `indexar` e `indexação` legitimamente, e uma negativa
      sobre o documento todo reprovaria por motivo errado.
- [x] 5.6 **(`apps/frontend`)** Rodapé de uma linha declarando o que o sistema não
      coleta — sem barra de progresso, sem métrica de uso —, e a faixa de somente
      leitura no topo. Os dois textos vêm do protótipo e **estão corretos**:
      conferidos contra `EmbeddingIndexConsistencyValidation` e contra a ausência
      de qualquer coleta de consultas no repositório.
- [x] 5.7 **(`apps/frontend`)** Tons: alerta **só** no que é falha (documentos em
      falha, corrupção), com cor semântica que resolve por esquema — nenhum tom
      fixo da escala neutra em papel que troca de ponta (convenções 13 e 16). O
      precedente da casa para faixa de perigo é `Alert` com variante `light`,
      resolvida por `theme.variantColorResolver`; **nenhuma variável nova por
      esquema** deve nascer aqui sem antes conferir que não há token que já troque.
- [x] 5.8 **(`apps/frontend`)** `KnowledgeIndexDiagnosticsTab.test.tsx`, cobrindo
      os cenários da spec — inclusive os **negativos**: sem nome de provedor no
      vazio; sem travessão no vazio; sem combinação marcada como atual; sem botão
      de reindexar; sem texto de `failureReason`; sem barra de progresso; erro que
      **não** vira vazio.
- [x] 5.8a **(`apps/frontend`)** O guarda do rótulo, **nível de componente**:
      asserção sobre o **texto renderizado** da linha de contagem de documentos,
      negando a palavra do badge de estado `Indexado`. Ler o texto do DOM pelo
      `data-testid` da tarefa 5.5 — **nunca** comparar uma constante do teste com
      a mesma constante do código, que passa verde com o defeito presente. O nível
      de função pura é a tarefa 3.2, e os dois não são redundância: pegam defeitos
      diferentes, como a tabela de D5 da 5a-3 registrou para a distinção
      `pendente`/`indexando`.

## 6. As abas no detalhe (`apps/frontend`)

- [x] 6.1 **(`apps/frontend`)** Em `pages/KnowledgeBaseDetailPage.tsx`, introduzir
      `Tabs` com `keepMounted={false}`, `useSearchParams` e `parseTab` — no
      desenho de `frontend-agente-detalhe-abas`: `documentos` canônica **sem**
      parâmetro, `diagnostico` com, valor desconhecido caindo na primeira **sem
      reescrever o endereço**. Remover o comentário que hoje explica a ausência da
      barra e substituí-lo pela razão de ela existir agora.
- [x] 6.2 **(`apps/frontend`)** Painel `Documentos`: descrição, listagem e card de
      agentes, na ordem atual. Painel `Diagnóstico do índice`: o componente novo,
      com a consulta `enabled` pela aba ativa.
- [x] 6.3 **(`apps/frontend`)** Contador só na aba `Documentos`, e só com a
      listagem respondida e maior que zero (D4). Enquanto carrega, com erro, ou
      com zero documentos: **nenhum** badge.
- [x] 6.4 **(`apps/frontend`)** Atualizar `pages/KnowledgeBaseDetailPage.test.tsx`:
      as duas abas existem; a troca reflete no endereço; aba desconhecida cai na
      primeira sem reescrever; a aba inativa **não está montada**; a proveniência
      **não é buscada** com a aba de documentos ativa; o contador não aparece
      enquanto a listagem não respondeu. Conferir que os testes existentes
      continuam valendo **sem alteração de asserção** — se algum precisar mudar, é
      sinal de que o painel default deixou de ser o que era.
- [x] 6.5 **(`apps/frontend`)** Conferir que **nenhum** dos 6 arquivos que mockam
      `knowledgeBasesApi` parcialmente precisou de override novo — repetir
      `grep -rl "vi.mock(.*knowledgeBasesApi" src/` e `grep -rl
      "vi.mock(.*knowledgeIndexApi" src/` e registrar o resultado. É a terceira
      dimensão de blast radius da convenção 18, e a projeção de D1 depende dela.

## 7. Guardas exercidos contra o defeito real (convenção 15)

Cada item: aplicar a inversão, ver reprovar, **registrar as duas metades** — qual
cenário reprovou e qual **continuou verde** —, desfazer. Resultado medido vai para
o `design.md`.

- [x] 7.1 **(`apps/frontend`)** Trocar o predicado da soma para
      `status === 'Indexed'` e ver reprovar o cenário do documento
      "indexou e falhou". Conferir que o teste da função pura reprova **e** que o
      teste de DOM do grupo de volume reprova — se só um reprovar, o guarda está no
      componente errado (convenção 15, segunda forma).
- [x] 7.1a **(`apps/frontend`)** Devolver o rótulo para `Documentos indexados` —
      **sem tocar** `documentIndexing.ts` — e registrar as **duas** metades: a
      asserção de DOM da tarefa 5.8a **reprova**, e o teste da função pura da
      tarefa 3.2 **continua verde**. A segunda metade é a evidência de que o
      guarda precisava estar no componente; sem ela o item vale o mesmo que não
      ter verificado. Conferir também que a reprovação vem da asserção do rótulo e
      não de outra que calhe de casar com a palavra.
- [x] 7.2 **(`apps/frontend`)** Reordenar as combinações no cliente com
      `localeCompare` e ver reprovar o cenário de ordem. **O arranjo precisa estar
      fora de ordem alfabética**: com a resposta já ordenada, o guarda passa verde
      com o defeito presente — é a quinta forma da convenção 15, e foi assim que o
      guarda de desempate de `mcpServers` passou com o defeito em 1 de 3 execuções.
- [x] 7.3 **(`apps/frontend`)** Renderizar as três linhas com travessão no estado
      vazio e ver reprovar o cenário negativo de D9.
- [x] 7.4 **(`apps/frontend`)** Marcar a primeira combinação como "atual" e ver
      reprovar a asserção negativa de D11. É a regressão bem-intencionada mais
      provável desta tela.
- [x] 7.5 **(`apps/frontend`)** Reintroduzir a lista de falhas completa na aba
      (motivo + botão de reindexar) e ver reprovar as duas asserções negativas de
      D10.
- [x] 7.6 **(`apps/frontend`)** Trocar o gate do vazio para `indexados > 0` da
      base e ver reprovar o cenário "base sem documento com índice povoado exibe a
      proveniência". Este é o guarda que separa a proveniência global da por base.
- [x] 7.7 **(`apps/frontend`)** Fazer o caminho de erro renderizar o texto de
      índice vazio e ver reprovar os dois cenários de D11 — o positivo da
      indisponibilidade e o negativo do vazio.
- [x] 7.8 **(`apps/frontend`)** Remover o `enabled` e ver reprovar o cenário "aba
      inativa não busca"; remover a primeira metade da condição de polling
      (deixando só `hasNonTerminalDocument`) e ver reprovar o cenário
      determinístico "índice povoado não acompanha".

## 8. Conferência manual (convenção 14) — tarefa própria e ITERATIVA

A suíte roda em jsdom e não enxerga cor, contraste nem layout. Cada correção muda
o que fica visível, então a conferência é iterativa e cada rodada registra o que
achou — **inclusive a rodada vazia**.

- [x] 8.1 **(`apps/frontend`)** Subir o painel contra um stub de `apps/api` que
      sirva `GET /knowledge-bases/{id}`, `GET /knowledge-bases/{id}/documents` e
      `GET /knowledge-index/diagnostics` com estado **estático**, mais um modo que
      alterne a proveniência de `[]` para uma combinação, para olhar a transição do
      polling sem depender de indexação real.
- [x] 8.2 **(`apps/frontend`)** Percorrer a aba nos **dois esquemas de cor**, a
      1860px, com estes estados nomeados um a um: índice vazio com base sem
      documento; índice vazio com documento em andamento (polling ativo); uma
      combinação; **duas combinações**; erro da proveniência; erro da listagem de
      documentos; base com documento em falha; base com documento que indexou e
      falhou depois.
- [x] 8.3 **(`apps/frontend`)** **Medir dimensão, não comparar aparência** — o
      instrumento que a 5a-3 acrescentou. Aqui os alvos são: a largura da coluna de
      valor com `qwen-qwen3-embedding-8b` (nome mais longo que o do mock); a linha
      de combinação na corrupção, que carrega quatro valores; e a barra de abas com
      e sem contador, que não pode mudar de altura entre os dois estados.
- [x] 8.4 **(`apps/frontend`)** Percorrer também a aba `Documentos` depois de
      envolvida em `Tabs`: a tabela, a faixa de falha e o card de agentes não podem
      ter mudado de medida nem de espaçamento ao entrar no painel.
- [x] 8.5 **(`apps/frontend`)** Repetir até uma rodada não achar nada, e registrar
      no `design.md` o que cada rodada achou.
- [x] 8.6 **(`apps/frontend`)** Nomear explicitamente o que a conferência **não**
      cobriu, para ir à validação do operador como item nomeado e não como
      "conferir a tela".

## 9. Fechamento da suíte (convenção 19)

- [x] 9.1 **(`apps/frontend`)** **Encerrar o navegador da conferência por porta** e
      esperar o `load` cair **antes de cada execução**, não só da primeira. A
      execução anterior da suíte também é contenção.
- [x] 9.2 **(`apps/frontend`)** `npm test` com a **saída completa guardada em
      arquivo**, e a comparação com a baseline de 1.2.
      **Medido: 80 arquivos / 833 testes, 833 passando, 66,17 s** (execução final,
      pós-prettier), contra a baseline de 76 / 765. +4 arquivos e +68 testes,
      **zero reprovações** — nenhuma classificação de "pré-existente" foi
      necessária. Saída completa em `~/.cache/buteco-agents/kb-5c-fechamento/`. Qualquer reprovação passa
      pelos três passos antes de receber rótulo: **isolar o arquivo** e repetir;
      comparar com `git worktree` limpo; e nunca absolver por "passou na segunda
      vez". Baseline vermelha remove a hipótese de regressão e **só** isso.
- [x] 9.3 **(`apps/frontend`)** `npm run lint`, `npm run format:check` e
      `npm run build` (que roda `tsc -b`).
      **`lint` limpo; `build` verde. `format:check` reprova em 8 arquivos, e os 8
      são PRÉ-EXISTENTES** — conferido em `git worktree` limpo no `853038c`, que
      reprova exatamente os mesmos 8 (convenção 19: "pré-existente" exige a
      baseline, não a impressão). Os arquivos desta change foram formatados e
      estão limpos. **Não corrigidos aqui**: 6 dos 8 são `KnowledgeBaseForm`,
      `KnowledgeBaseEditPage` e `KnowledgeBaseDescriptionCard` com os testes
      deles — exatamente o escopo da **5a-4** —, e os outros 2 (`LoginPage`,
      `mcp-servers/utils/agentUsage.ts`) são de outras linhas. Vira item aberto.

## 10. Documentação — um artefato por tarefa

Cada item manda **ler o que já está escrito** antes de escrever: o defeito caro
não é a frase desatualizada, é a frase que virou **falsa**.

- [x] 10.1 **(raiz)** `README.md`: procurar e corrigir toda afirmação sobre o que o
      detalhe da base mostra. Conferir em particular se alguma frase descreve a
      tela como uma página única, sem abas.
- [x] 10.2 **(raiz)** `docs/architecture.md`: conferir a descrição de
      `apps/frontend`. Na 5a-3 a tarefa equivalente terminou em "conferido, nada a
      mudar" porque não existe seção por tela ali — se continuar assim, **dizer
      isso**, não inventar uma seção nova.
      **Conferido, nada a mudar.** Continua sem seção por tela: `apps/frontend`
      aparece em duas linhas — a nota de que o painel está fora do caminho de
      mensagem (`:61-62`) e a linha da tabela dos quatro apps (`:73`) —, as duas
      no nível de aplicação e as duas corretas depois desta change. Inventar uma
      descrição por tela aqui criaria uma segunda fonte de verdade para o que a
      spec viva já descreve.
- [x] 10.3 **(raiz)** `CHANGELOG.md`: entrada em `[Unreleased]`, em pt-BR, nomeando
      a aba, a proveniência global, a corrupção nomeada e o predicado da soma.
- [x] 10.4 **(raiz)** `01-ARQUITETURA_E_CONVENCOES.md`: conferir se há o que
      registrar. Candidato já identificado — a convenção 13 ganha o **quarto
      símbolo** desta área: além de valor, vazio e travessão, o "sei que não
      existe" do índice vazio, que **não** é travessão. Registrar com o par de
      telas que o distingue.
- [x] 10.5 **(raiz)** `02-HISTORICO_E_STATUS.md`: registrar a etapa 5c, fechar a
      linha de bases de conhecimento na fila (é a última tela prevista) e dizer o
      que **sobra** — a 5a-4 e o item aberto do terceiro comparador em
      `knowledgeBaseRows.ts`, que esta change deliberadamente não tocou.
- [x] 10.6 **(raiz)** `02-HISTORICO_E_STATUS.md`, lista viva de correções de
      protótipo: de **onze** para **treze**, com C12 (o estado vazio renderiza três
      travessões, emprestando o vocabulário de "não sei" para dizer "sei que não
      existe") e C13 (a aba repete a lista de falhas que a tabela de documentos já
      mostra, com motivo e ação). Registrar que **C12 saiu de percorrer** o
      protótipo, e que C8 **não teria saído do percurso** — a semente não consegue
      produzir um documento que indexou e falhou depois. As duas metades do método
      valem escritas juntas.
      **Conferir o número na lista antes de escrever `de onze para treze`.** Já
      conferido ao propor — o `02` diz `São **onze**`, e a decomposição fecha: C8,
      mais as três da 5a-3, mais as duas novas da 5a-2, mais as cinco anteriores;
      as duas entradas que **corrigem** entradas antigas não são itens novos.
      Reconferir mesmo assim, porque essa lista já esteve dessincronizada uma vez
      (dizia sete quando eram oito), e a divergência só apareceu porque alguém
      contou. Se divergir, **corrigir a lista, não o texto desta change**.
- [x] 10.6a **(raiz)** `02-HISTORICO_E_STATUS.md`, junto do parágrafo de método da
      lista de correções: registrar o argumento que sustenta **os dois modos de
      detecção**, porque ele é o que responde a quem propuser cortar um. A frase,
      com o par que a prova: **a semente não consegue produzir o defeito da soma,
      então C8 nunca sairia de percorrer — como C12 nunca sairia de ler.**
      Percorrer não alcança estado que a semente não produz; ler o código do mock
      contra a spec viva não alcança cópia nem layout. O parágrafo que já está lá
      diz metade disso (*"uma lista construída só olhando texto de tela não teria
      achado nenhuma soma errada"*); esta change fecha a outra metade, e as duas
      juntas são a regra.
- [x] 10.7 **(raiz)** `scripts/check-docs.py` verde antes do PR.

## 11. Sincronização de spec e archive

- [x] 11.1 **(raiz)** Sincronizar `specs/knowledge-index-diagnostics-ui/spec.md`
      como spec viva nova, e o delta `MODIFIED` de
      `openspec/specs/knowledge-base-catalog-ui/spec.md`, conferindo que o
      requisito modificado substituiu o antigo **por inteiro** — requisito
      modificado com conteúdo parcial perde detalhe no archive.
- [x] 11.2 **(raiz)** Escrever o `Purpose` da spec viva nova **no arquivo vivo,
      depois do sync**, e não como `TBD`: o passo 4d do skill de sincronização
      manda marcar `TBD` e foi isso que produziu 39 placeholders — são execuções
      corretas de uma instrução errada. O `Purpose` diz a regra que atravessa a
      capability: a proveniência é do sistema, o vazio é fato e não desconhecido, e
      a tela não elege a combinação certa.
- [x] 11.3 **(raiz)** Conferir o `Purpose` de `knowledge-base-catalog-ui` **no
      arquivo vivo, depois do sync** — e não só pela ausência de `TBD`. A pergunta
      é se ele ainda descreve o que a capability cobre **depois** desta change, que
      moveu o detalhe da base para uma estrutura de abas.
- [x] 11.4 **(raiz)** Conferir o `Purpose` de `knowledge-document-catalog-ui` pela
      mesma pergunta: a listagem de documentos passou a viver dentro de uma aba, e
      a capability descreve a gestão do conteúdo da base.
- [x] 11.5 **(raiz)** Conferir que nenhum link escrito nesta change aponta para
      `openspec/changes/<nome>/` de mudança já arquivada — sempre
      `openspec/changes/archive/AAAA-MM-DD-<nome>/`.
- [x] 11.6 **(raiz)** Varrer os artefatos desta change atrás de
      `text-embedding-3-small`, `1536` e da forma curta do nome do modelo. O nome
      real é `qwen-qwen3-embedding-8b`; o do protótipo é mock.

## 12. Medição (convenção 18)

- [x] 12.1 **(raiz)** Comparar o entregue com a projeção do `design.md` — 13
      arquivos de código (9 criados, 4 modificados), 730–1.020 linhas —,
      **decomposto** em criados e modificados, e só sobre código. Nunca contra o
      headline do commit.
- [x] 12.2 **(raiz)** Registrar a causa de qualquer desvio, separando *erro de
      projeção* de *escopo acrescentado durante a implementação*. Registrar em
      particular se a terceira dimensão de blast radius se manteve em **zero**
      arquivos, como D1 projeta — é a primeira vez que essa dimensão é projetada
      **antes** e não descoberta depois.
