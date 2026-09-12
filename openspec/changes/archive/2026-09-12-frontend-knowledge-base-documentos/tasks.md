> **Todas as tarefas de código rodam em `apps/frontend`.** Nenhuma tarefa toca
> `apps/api`, `apps/workers`, `apps/inbox` ou `libs/`. As tarefas de
> documentação e de sincronização de spec rodam na raiz do repositório.

## 1. Baseline (convenção 19)

- [x] 1.1 **(raiz)** Criar `git worktree` limpo no `HEAD` atual e rodar ali a
      suíte de `apps/frontend` (`npm test`), **guardando a saída completa em
      arquivo fora do diretório de sessão** — por exemplo
      `~/.cache/buteco-agents/kb-5a2-baseline/frontend-baseline-completo.log`.
      **Nunca canalizar para `grep`/`head` antes de a rodada terminar**: o cano
      fechado mata o produtor por `SIGPIPE` e a rodada não produz nem sucesso nem
      falha, e filtrar antes de saber se há saída apaga justamente o caso de
      erro. Registrar contagem de arquivos e de testes.
- [x] 1.2 **(raiz)** Registrar a baseline no `design.md` desta change (ou num
      apêndice dela), com o commit medido — é o que separa "já estava assim" de
      "eu quebrei" no fechamento.

## 2. Contrato do fio e acesso à API (`apps/frontend`)

- [x] 2.1 **(`apps/frontend`)** Criar
      `features/knowledge-bases/types/knowledgeDocument.ts` espelhando
      `KnowledgeDocumentSummaryResponse` (14 campos) e `KnowledgeDocumentResponse`
      (15 campos, com `extractedText`), mais os inputs de criação e atualização.
      `indexingStatus` é união de string literal
      `'Pending' | 'Indexing' | 'Indexed' | 'Failed'` — **string no fio, nunca
      ordinal** (convenção 12). `indexedAt`, `failureReason` e `lastAttemptAt` são
      `string | null`. **`contentHash` NÃO existe em resposta nenhuma** e não
      entra no tipo — comentar isso no arquivo, porque é o campo que alguém vai
      procurar ao implementar o aviso de atualização (`design.md`, D5).
- [x] 2.2 **(`apps/frontend`)** Criar
      `features/knowledge-bases/api/knowledgeDocumentsApi.ts` com as seis funções
      (`list`, `get`, `create`, `update`, `remove`, `reindex`), importando
      `request<T>`/`ApiError` de `knowledgeBasesApi.ts` da própria feature — sem
      cliente HTTP compartilhado (convenção 7, D12 da 5a-1).
- [x] 2.3 **(`apps/frontend`)** Criar `knowledgeDocumentsApi.test.ts`: as seis
      rotas com o caminho e o método corretos, o corpo JSON de criação e
      atualização, o `204` da exclusão e o erro que vira `ApiError`.

## 3. Regras puras de indexação e de upload (`apps/frontend`)

- [x] 3.1 **(`apps/frontend`)** Criar
      `features/knowledge-bases/utils/documentIndexing.ts` com
      `fragmentCountLabel`, `hasNonTerminalDocument`, `nonTerminalCount` e o
      mapa de rótulo/tom por estado. **`fragmentCountLabel` decide por
      `indexedAt`, nunca pelo estado** — comentar a causa no arquivo, citando
      `KnowledgeIndexingService.FailAsync` (que não toca `IndexedAt` nem
      `FragmentCount`) e `RequestReindex()`/`Update()` (idem). É a regra que
      alguém vai "consertar" ao ver uma célula vazia.
- [x] 3.2 **(`apps/frontend`)** Criar `documentIndexing.test.ts` cobrindo os seis
      casos de `fragmentCountLabel` da spec, incluindo **a asserção negativa de
      que a cadeia `0 fragmentos` nunca é produzida**.
- [x] 3.3 **(`apps/frontend`)** Criar
      `features/knowledge-bases/utils/documentUpload.ts` com
      `titleFromFileName` (extensão removida, `-`/`_` → espaço),
      `isAcceptedExtension` e `collectFiles`, que lê a seleção **em ordem** com
      `await file.text()`. `collectFiles` é o **ponto único de convergência** das
      duas entradas de arquivo (`design.md`, D13) — comentar isso nele, porque é
      o que sustenta a cobertura do caminho de arrastar. Comentar também por que
      não é `FileReader` com callback: o protótipo empurra recusa de forma
      síncrona e aceite no `onload`, e por isso renderiza o arquivo recusado
      antes dos aceitos (D7, C5).
- [x] 3.4 **(`apps/frontend`)** Criar `documentUpload.test.ts`, incluindo o
      cenário de **ordem preservada** com aceite e recusa intercalados.

## 4. Consultas e mutações (`apps/frontend`)

- [x] 4.1 **(`apps/frontend`)** Criar
      `features/knowledge-bases/api/useKnowledgeDocuments.ts` com
      `useKnowledgeDocumentsQuery(knowledgeBaseId)` usando `refetchInterval` na
      **forma de função**, devolvendo `4000` enquanto `hasNonTerminalDocument` e
      `false` quando todos terminais. `refetchIntervalInBackground` fica no
      default. Comentar que é o **primeiro polling condicional do painel** —
      `useSessionMessagesQuery` usa intervalo constante — e que a condição mora
      numa função pura de propósito.
- [x] 4.2 **(`apps/frontend`)** Acrescentar ao mesmo módulo as mutações de
      criar, atualizar, excluir e reindexar, invalidando
      `['knowledge-bases', id, 'documents']` a cada sucesso.
- [x] 4.3 **(`apps/frontend`)** Criar `useKnowledgeDocuments.test.ts`: a consulta,
      o polling **ligado** com documento não-terminal, o polling **desligado** com
      todos terminais, e o par sucesso + erro de cada mutação (convenção 5).

## 5. Tabela de documentos e faixa de falha (`apps/frontend`)

- [x] 5.1 **(`apps/frontend`)** Criar
      `features/knowledge-bases/components/KnowledgeDocumentsCard.tsx` —
      apresentacional, **sem importar hook de query nem de mutation**
      (convenção 7, D11). Recebe `documents`, `isLoading`, `error` e os callbacks
      de ação. Monta sobre `SectionedCard` e reusa `SectionLabel`; a tabela entra
      como filho direto, que já se divide sozinha.
- [x] 5.2 **(`apps/frontend`)** Implementar a faixa de falha como
      `<Alert color="red" py="xs">` dentro da linha, no molde de
      `AgentKnowledgeTab.tsx:176`. **Sem variável nova por esquema**: `Alert` tem
      variante default `light` e resolve por `theme.variantColorResolver`, que é
      ciente do esquema (`design.md`, D9). Nenhum tom fixo da escala neutra como
      fundo — `surfaceTokens.test.ts` já varre isso.
- [x] 5.3 **(`apps/frontend`)** Implementar o estado vazio **verificado**
      ("Nenhum documento nesta base"), o estado de falha da listagem (que não
      vira vazio) e a faixa de resumo de não-terminais.
- [x] 5.4 **(`apps/frontend`)** Criar `KnowledgeDocumentsCard.test.tsx` cobrindo
      os requisitos de listagem, contagem por `indexedAt`, estados e faixa de
      falha — incluindo as negativas: **sem `0 fragmentos`**, **sem progresso
      percentual**, e **sem faixa** em documento que não falhou.

## 6. Modal de adicionar e atualizar (`apps/frontend`)

- [x] 6.1 **(`apps/frontend`)** Criar
      `features/knowledge-bases/components/DocumentFileList.tsx`: área de soltar
      com `FileButton` de `@mantine/core` (`multiple`, `accept`) mais
      `onDragOver`/`onDragLeave`/`onDrop`, e uma linha por arquivo com nome,
      tamanho, campo de título editável, remoção individual e motivo de recusa.
      **Não acrescentar `@mantine/dropzone`** — conferido ausente do
      `package.json` e do `node_modules` (`design.md`, D6). As duas entradas
      chamam **um único** `handleFiles`, que delega a `collectFiles`; o
      `onDrop` é adaptador fino (`preventDefault` + extrai
      `e.dataTransfer.files` + delega) e **não contém lógica** (D13).
- [x] 6.2 **(`apps/frontend`)** Criar `DocumentFileList.test.tsx` usando `File`
      real e `userEvent.upload` para o caminho do seletor.
- [x] 6.2a **(`apps/frontend`)** Acrescentar ao mesmo arquivo o **teste de
      convergência** (`design.md`, D13): `fireEvent.drop(area, { dataTransfer: {
      files: [arquivo] } })` — `@testing-library/dom` trata `dataTransfer` como
      caso especial do `eventInit` (`dist/events.js:72-77`), então o adaptador
      recebe um `File` real — e afirmar que o resultado renderizado é **o mesmo**
      do caminho do seletor com o mesmo arquivo.
      Comentar, no teste, **o que ele prova e o que não prova**: prova que as
      duas entradas chegam à mesma função com os mesmos dados, e **não** prova
      que um navegador real entrega essa forma no evento, porque o `dataTransfer`
      é forjado no teste (convenção 11 — dito, não escondido). Com a convergência
      afirmada, **a lógica está coberta e só o gesto não está**; sem ela, metade
      do tratamento de arquivo ficaria com a conferência manual como única rede.
- [x] 6.3 **(`apps/frontend`)** Criar
      `features/knowledge-bases/components/KnowledgeDocumentModal.tsx` com os dois
      modos e os dois propósitos: adicionar (subir vários / escrever) e atualizar
      (substituir um / escrever com o conteúdo carregado). Tipo de origem como
      campo desabilitado em `markdown`.
- [x] 6.4 **(`apps/frontend`)** Implementar a orquestração de falha parcial
      (`design.md`, D2): N chamadas **sequenciais** na ordem da lista, estado por
      linha (`aguardando` → `enviando` → `criado` | `falhou`), linha `criado` que
      deixa de ser enviável, modal que **não fecha** em falha parcial, e
      invalidação da listagem a cada sucesso. O rodapé **não** promete conjunto.
- [x] 6.5 **(`apps/frontend`)** Implementar as **duas** cópias de efeito do
      salvamento (`design.md`, D5), por comparação local de `extractedText`
      carregado contra o editado — remover o que era falso é só metade do
      trabalho:
      - **conteúdo alterado**: volta para pendente e será indexado de novo, **e o
        conteúdo indexado anteriormente continua respondendo até a nova indexação
        terminar com sucesso — e permanece se ela falhar** (garantia 3 de D9 da
        etapa 1 chegando à tela);
      - **conteúdo idêntico**: salvar **não** reindexa e o estado de indexação
        continua o mesmo — sem essa frase o operador que corrige só o título
        espera uma reindexação que não vem e lê a ausência de mudança como
        defeito.

      Rótulo do botão só menciona reindexar quando o conteúdo mudou. Comentar que
      a comparação é local porque `contentHash` não sai no fio, e que o erro é
      por **omissão** na linha legada.
- [x] 6.6 **(`apps/frontend`)** Criar `KnowledgeDocumentModal.test.tsx` com as
      **positivas** e as **negativas** de D5, porque só as negativas passariam com
      a tela calada:
      - positivo: o aviso de conteúdo alterado diz que o anterior continua
        respondendo **inclusive se a nova indexação falhar**;
      - positivo: alterar só o título **informa que salvar não reindexa**;
      - negativo: alterar só o título não faz a palavra "reindexar" aparecer;
      - negativo: o aviso não afirma saída das consultas nem descarte imediato;
      - negativo: o rodapé do lote não afirma que os documentos serão criados
        como conjunto;
      - negativo: nenhuma cópia relaciona tamanho a falha de indexação.

## 7. Página de detalhe (`apps/frontend`)

- [x] 7.1 **(`apps/frontend`)** Modificar `pages/KnowledgeBaseDetailPage.tsx`:
      buscar a listagem de documentos e repassá-la como prop, montar os dois
      modais e a confirmação de exclusão (reusando `agentsConsultingBase` para
      nomear os agentes afetados), e trocar
      `KnowledgeBaseDocumentsPlaceholder` por `KnowledgeDocumentsCard`.
- [x] 7.2 **(`apps/frontend`)** Atualizar `KnowledgeBaseDetailPage.test.tsx`
      para a área de documentos, a confirmação de exclusão com agentes nomeados e
      o par cancelar/confirmar.
- [x] 7.3 **(`apps/frontend`)** **Remover** da árvore
      `components/KnowledgeBaseDocumentsPlaceholder.tsx` e
      `KnowledgeBaseDocumentsPlaceholder.test.tsx` (`design.md`, D12).

## 8. Verificação dos guardas (convenção 15) — cada um visto REPROVAR

Guarda não verificado é pior que nenhum. Para cada item: escrever o teste,
reintroduzir o defeito real de propósito, **ver reprovar**, desfazer, ver passar.
Checar também que ele reprova **no componente que a correção toca**, e não num
vizinho.

- [x] 8.1 **(`apps/frontend`)** `0 fragmentos`: pôr no lugar o ternário do
      protótipo (`st === 'failed' ? '0 fragmentos' : '—'`) e ver reprovar a
      asserção negativa. Registrar quantos testes reprovam e quais.
- [x] 8.2 **(`apps/frontend`)** Contagem anterior preservada: trocar a condição
      de `indexedAt !== null` por `status === 'Indexed'` e ver reprovarem os três
      cenários de contagem anterior (`Failed`, `Pending` e `Indexing` com
      `indexedAt`). É o defeito que a correção registrada pela 5a-1 **não**
      pegaria.
- [x] 8.3 **(`apps/frontend`)** Progresso percentual: acrescentar um
      `<Progress>` a uma linha não-terminal e ver reprovar a asserção negativa.
- [x] 8.4 **(`apps/frontend`)** Polling que não para: trocar a forma de função por
      `refetchInterval: 4000` constante e ver reprovar o cenário de "todos
      terminais não recarrega" — **e só ele**.
- [x] 8.5 **(`apps/frontend`)** Aviso incondicional de reindexação: pôr no lugar
      o aviso do protótipo (incondicional, afirmando saída das consultas e
      descarte) e ver reprovar **tanto** a negativa quanto as duas positivas de
      D5. Se só as negativas reprovarem, a cópia de conteúdo idêntico não está
      sendo afirmada e o guarda está incompleto.
- [x] 8.5a **(`apps/frontend`)** Convergência das entradas de arquivo: fazer o
      `onDrop` tratar os arquivos por conta própria, em vez de delegar a
      `collectFiles`, e ver reprovar o cenário de convergência — **e só ele**.
      Guarda que reprove também os testes do seletor está afirmando a garantia no
      componente errado (convenção 15, segunda metade).
- [x] 8.6 **(`apps/frontend`)** Falha parcial: fazer a linha `criado` continuar
      enviável e ver reprovar o cenário de reenvio sem duplicar.
- [x] 8.7 **(`apps/frontend`)** Registrar o resultado de cada uma das oito
      verificações acima no `design.md` (quantos reprovaram, quais, e se algum
      reprovou no componente errado). Se algum guarda **não** reprovar, ele está
      no lugar errado e é corrigido antes de seguir.

## 9. Conferência manual (convenção 14) — tarefa própria e ITERATIVA

A suíte roda em jsdom, que não enxerga cor, contraste nem layout. Esta
conferência não é cerimônia: três etapas do redesenho passaram verdes com
defeitos que só a comparação com o protótipo pegou, e a 5b declarou convergência
com uma largura errada visível na tela.

- [x] 9.1 **(`apps/frontend`)** Subir o painel e o protótipo lado a lado, **na
      largura em que o painel é usado (~1860px), não 1440** — a 5b registrou que
      num card de 860 a diferença quase não aparece a 1440 e é gritante a 1860.
- [x] 9.2 **(`apps/frontend`)** Rodada 1, **nos dois esquemas de cor**: tabela com
      os quatro estados (montar o estado no backend local, já que a semente do
      protótipo não tem os quatro juntos — ver `design.md`, Context), faixa de
      falha com motivo longo, faixa de resumo, estado vazio e estado de falha da
      listagem.
- [x] 9.3 **(`apps/frontend`)** Rodada 1, **nos dois esquemas**: modal de
      adicionar nos dois modos, com lista de vários arquivos incluindo um
      recusado; modal de atualizar nos dois modos, com e sem conteúdo alterado;
      confirmação de exclusão.
- [x] 9.4 **(operador — a automação não alcança)** Conferir o **gesto** de arrastar-e-soltar, que é
      o que a suíte não cobre depois da convergência de D13 — a lógica já está
      coberta. Itens **nomeados**, não "conferir a área de soltar":
      - arrastar arquivo aceito, arquivo recusado, e vários de uma vez;
      - **`onDragOver` com `preventDefault()`**: sem ele o navegador nunca
        dispara `drop` e **navega para o arquivo**, com a suíte verde e a área de
        soltar inteiramente morta — jsdom não tem essa regra;
      - o realce visual de `dragover`/`dragleave` nos **dois** esquemas.
- [x] 9.5 **(`apps/frontend`)** Comparar **dimensões**, não só estados: largura do
      card, largura máxima do texto do motivo de falha, largura do modal, altura
      da faixa de cabeçalho. A 5b errou exatamente aqui — comparou lista, vazio,
      aviso e modal, e nunca comparou largura nem altura.
- [x] 9.6 **(`apps/frontend`)** Repetir as rodadas até uma fechar **sem achado
      novo**, registrando o que cada rodada achou e o custo dela. Nota da 5a-1:
      com a identidade já estabelecida, as rodadas acham cópia, estrutura e
      escopo — não cor; isso muda **onde olhar**, não dispensa a rodada de tema.
- [x] 9.7 **(operador)** Validação manual, com dois itens **nomeados** que a
      automação não cobriu (ver `design.md`, "Conferência manual"):
      **(a)** os dois modais no **esquema escuro** — o detalhe foi conferido nos
      dois, os modais só no claro, porque o controle de tema do rodapé não foi
      acionável pelo seletor das rodadas;
      **(b)** o **gesto** de arrastar-e-soltar (tarefa 9.4), incluindo o
      `preventDefault` do `dragover`.
      A automação reduz o número de rodadas humanas; não substitui nenhuma — a 5b
      declarou convergência em quatro rodadas e o usuário achou uma largura
      errada na tela dele.

## 10. Fechamento da suíte (convenção 19)

- [x] 10.1 **(`apps/frontend`)** Rodar a suíte completa de `apps/frontend` com o
      **mesmo procedimento de carga** da baseline (`uptime` antes, nada em
      paralelo), **guardando a saída completa em arquivo** — sem `grep`, sem
      `head`, sem resumo. A 2a perdeu o nome do teste que reprovou por ter
      filtrado a saída, e o evento não se repetiu.
- [x] 10.2 **(`apps/frontend`)** Comparar com a baseline da tarefa 1.1. Qualquer
      reprovação é classificada **contra a baseline**, nunca por reconhecimento —
      e baseline vermelha remove a hipótese de regressão e **só** isso: não
      promove o sintoma a "ambiental" nem dispensa achar a causa.
- [x] 10.3 **(`apps/frontend`)** Rodar `npm run lint`, `npm run format:check` e
      `npm run build` (que roda `tsc -b`).

## 11. Documentação — um artefato por tarefa

A 2a tinha tarefa de fechamento para registrar no `02` e **nenhuma** para
conferir os artefatos de documentação um a um, e deixou três afirmações
**falsas** (não desatualizadas) no `README.md`, em `docs/architecture.md` e no
`CHANGELOG.md`. `scripts/check-docs.py` passou verde o tempo todo: ele verifica
link quebrado, app não documentado e `[Unreleased]`, e não tem como saber que uma
frase virou mentira. Por isso cada artefato tem item próprio, e cada item manda
**ler o que já está escrito** antes de escrever.

- [x] 11.1 **(raiz)** `README.md`: procurar e corrigir toda afirmação sobre
      gestão de documentos de base de conhecimento pelo painel. Conferir em
      particular se alguma frase ainda diz que documentos só entram por API.
- [x] 11.2 **(raiz)** `docs/architecture.md`: descrever a tela de documentos na
      seção de `apps/frontend` e conferir se a descrição do ciclo de indexação
      continua verdadeira com a tela no ar.
- [x] 11.3 **(raiz)** `CHANGELOG.md`: entrada em `[Unreleased]`, em pt-BR,
      nomeando a gestão de documentos na UI, a reindexação pela tela e o
      acompanhamento da transição.
- [x] 11.4 **(raiz)** `01-ARQUITETURA_E_CONVENCOES.md`: registrar o primeiro
      polling condicional do painel como padrão (`design.md`, D3), a forma
      "regra de exibição em função pura com a causa comentada ao lado" (D4), e —
      como acréscimo à convenção 6 — que **comando de varredura que falha em
      silêncio produz a mesma saída que ausência real**. Esta change cometeu o
      erro dentro do próprio `design.md`: `grep --include=*.cs` sem aspas virou
      glob do `zsh`, devolveu *"no matches found"* e nenhuma linha, e a saída
      vazia foi lida como "a entidade não existe em `apps/api`". A conclusão de
      escopo não mudou, a evidência estava errada, e a correção está registrada
      no `design.md` (convenção 9) em vez de só no resumo do chat.
      Registrar também o achado do `notifyOnChangeProps: 'tracked'` do
      react-query (o proxy só re-renderiza para props LIDAS, e teste de hook que
      não toca `data` na primeira espera fica preso no primeiro valor) — e,
      junto, que a primeira reprodução isolada dele mudou **duas** variáveis de
      uma vez e creditou o efeito ao `refetchInterval`. Isolamento só vale
      mudando uma.
- [x] 11.5 **(raiz)** `02-HISTORICO_E_STATUS.md`: registrar a etapa 5a-2, a
      décima medição da convenção 18 (projetado × entregue, criados e modificados
      separados), o resultado da verificação de guardas, e as rodadas de
      conferência manual com o custo de cada uma.
- [x] 11.6 **(raiz)** `02-HISTORICO_E_STATUS.md`, lista de correções de
      protótipo: ela passa de **cinco para sete** (C4 e C5 do `design.md`), e a
      correção `0 fragmentos` é **reescrita** para a regra completa — omitir só
      quando `indexedAt` é nulo, exibir a contagem anterior quando não é.
      **Corrigir também a evidência errada** da correção C2: o registro diz que a
      semente aplica a frase a "um documento pequeno", e o documento é `d4`, com
      `chars: 196400` — o maior da semente —, além de `chars` nunca chegar à
      tela. A recusa continua de pé pela convenção 13; a evidência é que estava
      errada (convenção 6).
- [x] 11.7 **(raiz)** `02-HISTORICO_E_STATUS.md`, itens em aberto: abrir o item
      da etapa **5a-3** (colunas `Documentos`/`Indexação` e filtro `Com falha` no
      catálogo, sobre `GET /knowledge-bases/indexing-summary`) com o escopo já
      escrito e — o ponto desta tarefa — com **posição na fila, não gatilho**.
      *"Gatilho imediato"* já foi usado nesta jornada e não disparou: o carve de
      ordenação ficou com `GATILHO ATUAL: imediato` e só voltou porque alguém
      perguntou; a 5a-3 tem o mesmo perfil (pequena, dependência pronta, nada que
      a puxe). Copiar para o item **e** para o "Próximo passo" a tabela de fila do
      `design.md` (D1):

      **5a-2 (esta) → etapa 4 (resolvedor de tool) → 5a-3 → backend do
      diagnóstico → 5c.**

      Com as duas razões que a ordenam, porque são elas que impedem a fila de ser
      reordenada por conveniência: a **etapa 4** vem primeiro porque até ela
      existir *nada do que o operador carrega nesta tela chega a um agente*; e a
      **5a-3 vem antes da 5c** porque a 5c está bloqueada por um passo de backend
      que ainda não foi proposto, enquanto a dependência da 5a-3 está no ar e
      ociosa desde a 2b.
- [x] 11.7a **(raiz)** `02-HISTORICO_E_STATUS.md`: registrar, como terceira
      ocorrência da família *"referência registrada que não é revisitada"*,
      que **gatilho sem posição na fila é adiamento indefinido com outro nome** —
      junto do gatilho que apontava para uma tela que ninguém planejava e do
      limiar de carga citado depois que a suíte mudou. As três têm o mesmo
      mecanismo; a convenção 18 já previa promover ao `01` na terceira. Avaliar a
      promoção e, se não promover, dizer por quê.
- [x] 11.8 **(raiz)** Rodar `scripts/check-docs.py` — sabendo que ele verifica
      estrutura e não verdade de conteúdo, e que por isso ele **não** substitui
      as tarefas 11.1 a 11.7.

## 12. Sincronização de spec e archive

- [x] 12.1 **(raiz)** Sincronizar as specs vivas: criar
      `openspec/specs/knowledge-document-catalog-ui/` a partir do delta e aplicar
      o `REMOVED`/`MODIFIED` em `openspec/specs/knowledge-base-catalog-ui/`.
- [x] 12.2 **(raiz)** Escrever o **`Purpose` real** da capability nova **antes do
      archive, nesta mesma passada**. O passo `4d` do skill
      `openspec-sync-specs` manda escrever `Purpose ... (can be brief, mark as
      TBD)` e não manda procurar um `Purpose` na delta — os 37 placeholders das
      specs vivas não foram 37 esquecimentos, foram execuções corretas de uma
      instrução errada. Texto a usar, já escrito para não depender do passo 4d:

      > A gestão do **conteúdo** de uma base de conhecimento no painel do
      > operador: listar os documentos de uma base com o estado de indexação de
      > cada um, adicionar documentos por arquivo ou escrevendo à mão, atualizar,
      > excluir e reindexar. Consome as rotas de `knowledge-document-catalog` e a
      > de reindexação de `knowledge-document-indexing` em `apps/api`.
      >
      > A capability existe por um motivo que não é CRUD: antes dela, a base
      > existia, a descrição que o modelo lê existia, o vínculo com o agente
      > existia e o índice existia — e o **conteúdo**, que é a razão de tudo
      > isso, só entrava por `curl`.
      >
      > Duas regras atravessam a capability. A primeira é que a indexação é
      > assíncrona e a tela **acompanha a transição** em vez de pedir recarga:
      > enquanto houver documento não-terminal a listagem se refaz sozinha, e
      > para quando todos chegam a `Indexed` ou `Failed`. A segunda é a de não
      > afirmar o que o sistema não sabe, e aqui ela tem forma própria: a
      > contagem de fragmentos é governada por `indexedAt` e **nunca** por
      > estado, porque documento que falhou ou está sendo reindexado continua
      > respondendo com os fragmentos anteriores — exibir zero ali afirmaria que
      > a indexação rodou e não achou nada. Não há progresso percentual, e
      > nenhuma cópia relaciona tamanho de documento a falha de indexação.

- [x] 12.3 **(raiz)** Conferir que **nenhuma** capability tocada por esta change
      ficou com `Purpose` placeholder, incluindo a modificada.
- [x] 12.4 **(raiz)** Arquivar a change em
      `openspec/changes/archive/AAAA-MM-DD-frontend-knowledge-base-documentos/`,
      **com a pasta `design/` (protótipo, `support.js`, `CONHECIMENTO.md`)
      junto** — é o que a 5a-1 fez, e é o que permite a 5c percorrer a mesma
      revisão sem ir buscá-la de novo.

## 13. Medição (décima da convenção 18)

- [x] 13.1 **(raiz)** Medir o entregue **só de código**, com artefatos OpenSpec,
      documentação e arquivos gerados **fora da conta**, decompondo em
      criados / modificados / removidos e produção / teste.
- [x] 13.2 **(raiz)** Comparar com a projeção do `design.md` (15 criados /
      ~2.555 li; 2 modificados / ~175 li; 2 removidos) e registrar **as duas
      dimensões separadas** — arquivo e linha erram por motivos diferentes, e
      registrar a direção sozinha não serve de nada. Registrar também que a
      projeção subiu ~4% **na revisão da proposta**, sem mudança de escopo de
      produto: a convenção 18 diz que projeção feita durante a verificação é
      rascunho, e aqui a **revisão** também acrescentou. O número de arquivos não
      se moveu nas duas redações.
- [x] 13.3 **(raiz)** Responder, com o número na mão, a pergunta que o
      `design.md` respondeu **antes** de projetar: esta change entregou código ou
      entregou uma decisão? A oitava medição acertou linhas e errou arquivos; a
      nona acertou arquivos e errou linhas em 40% por prosa de decisão. Esta foi
      projetada como change de **código** — registrar se o perfil se confirmou, e
      se os dois pontos orçados como prosa pesada (`documentIndexing.ts` e o
      aviso de D5) ficaram dentro.
- [x] 13.4 **(raiz)** Registrar separadamente qualquer **escopo acrescentado
      durante a implementação** (tipicamente vindo da conferência manual).
      Medição de método só compara o escopo que estava projetado.


## Registro de fechamento — o que o operador validou, e o que NÃO foi coberto

Validação manual feita em 12/09/2026, em navegador real, contra a stack completa
(`apps/api` + `apps/workers` + Postgres + RabbitMQ), não contra o stub da
conferência automatizada.

**Coberto e funcionando:** a gestão de documentos ponta a ponta — subir arquivo
pela tela, o documento entrar como pendente, a indexação rodar em segundo plano
e a listagem acompanhar a transição até `Indexed`. É o caminho principal da
change, e é o que o operador exercitou ao testar as três configurações de agente.

**NÃO coberto, e fica declarado em vez de dado por conferido** — os dois itens
que a automação não alcança e que não foram reportados separadamente:

1. **Os dois modais no esquema escuro.** O detalhe foi conferido nos dois
   esquemas pela rodada automatizada; os modais, só no claro.
2. **O gesto de arrastar-e-soltar**, incluindo o `preventDefault` do `dragover`
   — cuja ausência mata a área de soltar com a suíte verde (R6b).

Nenhum dos dois bloqueia o uso: o caminho do seletor de arquivo está coberto por
teste com `File` real e foi exercitado pelo operador, e a convergência das duas
entradas está afirmada por teste (D13), então a **lógica** do arrastar é a mesma
já coberta — falta o gesto e o realce visual.

**Gatilho para fechar o residual:** a próxima change que tocar
`DocumentFileList` ou `KnowledgeDocumentModal`. Registrado aqui em vez de no
`02` por ser residual de conferência desta change, não item de linha de
trabalho.
