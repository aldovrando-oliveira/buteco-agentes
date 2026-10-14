# Tarefas — aba Histórico no detalhe da base (#101)

**Cada tarefa indica o app.** Todo o código roda em `apps/frontend`; a
documentação (grupo 10) roda na raiz e não pertence a app nenhum. Nada em
`apps/api`, `apps/workers`, `apps/connectors`, `apps/inbox` nem `libs/`. **Se a
tela precisar de algo que a rota da #98 não devolve, PARAR e reportar**: nenhum
campo é inventado e o `apps/api` não muda nesta change.

**Worktree:** `/Users/aldovrando/Projetos/buteco-agents-101`, branch
`feat/101-aba-historico`, criada sem upstream de `origin/main` (`7559924`) em
13/10/2026. Confirmar `pwd` e `git branch --show-current` antes de cada grupo.
Nenhum comando git no diretório principal nem em outro worktree; nunca
`checkout`, `switch`, `stash`, `reset`, `clean`, `pull`, `merge` nem `rebase`.
Sem commit, push, PR, sync nem archive: são decisões do mantenedor.

**Máquina e portas.** Conferir `uptime` antes de rodar e de classificar qualquer
falha de teste; com `load average` acima de ~6, esperar e refazer (uma falha de
pool do Vitest com a máquina carregada não é defeito). Portas só na faixa
**59300–59399**, cada uma conferida com `lsof -i :<porta>`; nunca derrubar
processo que esta change não iniciou.

**Regra de cada comportamento:** o teste é escrito e **visto reprovando** contra o
código de antes, e só então o comportamento é implementado. Reprovar por import
inexistente não conta como "visto reprovando" para comportamento: nesses casos o
símbolo nasce com o corpo mínimo que compila (`throw` ou valor vazio), e a
reprovação registrada é a da asserção. Registrar em cada tarefa a mensagem de
reprovação, uma linha.

**Testes existentes que mudam** (lista fechada da D11, e só eles):
`KnowledgeBaseDetailPage.test.tsx` (mock e `beforeEach` de documentos; "apresenta
as duas abas…"; o `it.each` dos cards acima da barra; "a aba inativa não está
montada"; o comentário de `describe('barra de abas')`),
`KnowledgeBaseDetailPage.sync.test.tsx` (`installFetch` e `renderPage`) e
`syncStateMessages.test.ts` (só o comentário de `FORBIDDEN`). Se outro teste
existente precisar mudar, **PARAR**, acrescentá-lo à D11 com o motivo, e só então
mudá-lo.

## 1. Baseline e conferências, antes de tocar código

- [x] 1.1 (`apps/frontend`) `npm ci` neste worktree e a baseline sem nenhuma
      edição: `npm run lint`, `npm run format:check`, `npm run test`,
      `npm run build`, com `uptime` antes de cada um. Registrar aqui cada
      resultado e, no `format:check`, a lista de arquivos que já reprovam (só
      esses contam como pré-existentes).
      Feito em 14/10/2026, em `7559924` sem edição de código, depois de `npm ci`:
      - `npm run lint`: **verde**. Load 8,08 no início.
      - `npm run format:check`: **reprova** em **52 arquivos**, a mesma lista da
        1.1 da #136 (`KnowledgeBaseEditPage.test.tsx` e `KnowledgeBaseEditPage.tsx`
        são os dois de `knowledge-bases`; nenhum é arquivo desta change). Lista em
        `scratchpad/fmt-baseline.txt` da sessão.
      - `npm run test`: **verde** — 133 arquivos, **1724/1724**, 192 s (00:08:23 a
        00:11:35). Load **5,82** no início (esperado cair abaixo de 6: o
        Spotlight indexava o `node_modules` recém-instalado) e 43,06 no fim (a
        própria suíte).
      - `npm run build`: **verde**. Load 35,2 (logo depois da suíte; build não é
        medição de tempo).
- [x] 1.2 (`apps/frontend`) Guardar a lista de **nomes** de teste da baseline
      (`npx vitest run --reporter=json` ou equivalente) em arquivo no scratchpad,
      para a comparação por nome do grupo 8. Registrar aqui o total e o caminho.
      Feito: `vitest run --reporter=json` da própria 1.1; **1724 nomes**
      (arquivo :: nome completo :: estado) em `scratchpad/baseline-names.txt`.
- [x] 1.3 (`apps/frontend`) Conferir no pacote instalado, nunca de memória
      (convenção 6), e registrar arquivo e linha de cada item:
      - `@tanstack/react-query`/`query-core`: `useInfiniteQuery` com
        `initialPageParam`; `getNextPageParam` devolvendo `undefined` deixa
        `hasNextPage` falso; o nome do sinal de falha da página seguinte
        (`isFetchNextPageError` ou equivalente) e que a falha dela **mantém**
        `data`; que o refetch de uma consulta infinita relê todas as páginas (o
        motivo da D6); como aparar a consulta para a primeira página e relê-la
        (`setQueryData` sobre `pages`/`pageParams` seguido de `refetch`, ou
        `resetQueries`), quantas requisições cada forma faz, se a lista continua
        em `data` durante a releitura (o `Atualizar` precisa disso), e como a
        reentrada começa da primeira página com um observador desabilitado vivo
        na página. Escolher o mecanismo da D6 por esse resultado e corrigir a D6
        se ele mudar alguma premissa (convenção 9).
      - `@mantine/core` `Tabs`: o padrão de `activateTabWithKeyboard` e de `loop`
        (a seta seleciona, e não só foca, a aba vizinha?). Se a seta só focar, o
        cenário "A barra se percorre pelo teclado" e o teste mudam para "foca e
        Enter seleciona", e a D8 é corrigida antes de seguir.
      - `@mantine/core` `Badge`: `variant="light"` e `variant="outline"` existem
        e resolvem cor por esquema (convenção 16).
      Lido em 14/10/2026:
      - `@tanstack/query-core` **5.101.4**: `infiniteQueryBehavior.js:70-78` relê
        `oldPages.length` páginas (premissa confirmada); `:113-116` `hasNextPage`
        é `getNextPageParam(...) != null`; `infiniteQueryObserver.js:40-57`
        `isFetchNextPageError`/`isFetchingNextPage`/`isRefetching`/`isRefetchError`;
        `queryObserver.js:331-334` a falha com dados mantém `data`;
        `queryClient.js:126-139` `resetQueries` apaga `data` antes de reler;
        `query.js:97-101` e `queryObserver.js:457-459` refetch `active` só com
        observador habilitado, e habilitar busca com dado velho. **Mecanismo
        escolhido:** aparar para a primeira página + `refetchQueries` ativo, nos
        três recomeços. **Divergência corrigida na D6:** a primeira redação
        admitia `resetQueries`, que faria a lista voltar ao carregamento; a spec
        passou a dizer que o `Atualizar` mostra a primeira página que já tinha.
      - `@mantine/core` **9.4.2**: `Tabs/Tabs.mjs:26-27` `loop: true`,
        `activateTabWithKeyboard: true`; `TabsTab.mjs:53-60` passa
        `activateOnFocus`. A seta **seleciona** a aba vizinha: D8 confirmada.
      - `Badge`: `Badge.mjs:12-28` resolve cor por `theme.variantColorResolver`;
        `default-variant-colors-resolver.mjs` usa `--mantine-color-<cor>-light*`
        e o `outline` por variável, que trocam por esquema (convenção 16).


## 2. Tipo, cliente e consulta

- [x] 2.1 (`apps/frontend`) `types/knowledgeDocumentEvent.ts`: espelho de
      `KnowledgeDocumentEventResponse` e `KnowledgeDocumentEventPageResponse`, com
      o comentário de onde cada campo foi lido (Context do `design.md`). `type`
      como `'Created' | 'Updated' | 'Deleted'`; os rótulos aceitam `string` para o
      desconhecido (D3).
      Feito: `types/knowledgeDocumentEvent.ts`.
- [x] 2.2 (`apps/frontend`) Em `api/knowledgeDocumentsApi.test.ts`, os casos de
      `listKnowledgeDocumentEvents`: sem cursor, o caminho é
      `/knowledge-bases/{id}/document-events` **sem** query string; com cursor, o
      caminho leva `?cursor=` com o valor codificado por `encodeURIComponent`; o
      corpo vem de um **texto** JSON escrito à mão com as chaves e os valores de
      `type` que `KnowledgeDocumentCatalogTests.cs:653-674` afirma no fio, e o
      teste afirma os campos lidos (convenção 12). Ver reprovar com a função
      devolvendo uma página vazia fixa.
      Visto reprovar (stub com página vazia fixa): 3 de 3, `expected null to be
      'AAAA-_bb'` e `fetch` não chamado nos dois de caminho.
- [x] 2.3 (`apps/frontend`) Implementar `listKnowledgeDocumentEvents(knowledgeBaseId,
      cursor)` em `api/knowledgeDocumentsApi.ts`, pelo `request<T>` da feature.
      Ver 2.2 passar.
      Feito; 13/13 no arquivo.
- [x] 2.4 (`apps/frontend`) Em `api/useKnowledgeDocuments.test.ts`, os casos de
      `useKnowledgeDocumentEventsQuery(id, { enabled })`: com `enabled` falso,
      nenhuma chamada; a primeira página sem cursor; `hasNextPage` falso com
      `nextCursor` nulo; `fetchNextPage` pede com o cursor da primeira; nenhuma
      chamada nova depois de avançar o relógio com a primeira página respondida
      (sem `refetchInterval`). Ver reprovar.
      Visto reprovar (stub `enabled: false`, chave no prefixo): 4 de 5 —
      `Target cannot be null or undefined` (sem páginas) e `expected
      'knowledge-bases' not to be 'knowledge-bases'`. "Desabilitada, nenhuma
      chamada" passa com o stub, por ser negativa; a guarda dela é a 7.8.
      Acrescentado o caso de `restartDocumentEvents` (uma requisição, sem cursor).
- [x] 2.5 (`apps/frontend`) Implementar `documentEventsQueryKey`
      (`['knowledge-document-events', id]`, fora do prefixo das bases; D6) e
      `useKnowledgeDocumentEventsQuery` em `api/useKnowledgeDocuments.ts` (D6:
      `initialPageParam: null`, `getNextPageParam`, `refetchOnWindowFocus: false`,
      sem `refetchInterval`). Ver 2.4 passar.
      Feito, com `restartDocumentEvents` (cancela, apara, relê ativa); 20/20.
## 3. Rótulos, agrupamento e formatos (`utils/documentHistory.ts`)

- [x] 3.1 (`apps/frontend`) `documentHistory.test.ts`, tipo e detalhe (D3):
      `Incluído`, `Atualizado`, `Excluído`; tipo desconhecido devolve o valor e a
      marca de desconhecido, e **não** nenhum dos três rótulos; os três detalhes
      de `Updated`; `Created` e `Deleted` sem detalhe; `Updated` com os dois
      falsos sem detalhe. Ver reprovar com as funções devolvendo `''`.
      Visto reprovar (stubs devolvendo `''`/`[]`): 13 de 14, cada um pela
      asserção (`expected { label: '', known: true } to deeply equal …`).
      "Nenhum cabeçalho é relativo" passava no vazio: ganhou `toHaveLength(2)`.
- [x] 3.2 (`apps/frontend`) Implementar `eventTypeLabel` e `eventDetail`. Ver 3.1
      passar.
      Feito junto com 3.4 e 3.6 (mesmo módulo, testes já reprovados).
- [x] 3.3 (`apps/frontend`) `documentHistory.test.ts`, autor (D2): `Operador`,
      `Sincronização`, `service:outro` no texto neutro com o valor e **não** como
      `Sincronização`, vazio como não registrado. Ver reprovar.
      Reprovou no mesmo ciclo da 3.1: `expected '' to be 'Operador'` e afins.
- [x] 3.4 (`apps/frontend`) Implementar `authorLabel`. Ver 3.3 passar.
      Feito; ver 3.2.
- [x] 3.5 (`apps/frontend`) `documentHistory.test.ts`, dia e horário (D7):
      `2026-09-26T02:30:00Z` em `America/Sao_Paulo` cai em `25/09/2026` às
      `23:30`, e em `UTC` em `26/09/2026` às `02:30`; dois dias na ordem da
      entrada; a concatenação de duas páginas com o mesmo dia na fronteira dá um
      grupo só; nenhum cabeçalho contém `Hoje` nem `Ontem`. Ver reprovar.
      Reprovou no mesmo ciclo da 3.1: `expected '' to be '25/09/2026'`,
      `expected [] to deeply equal [ '26/09/2026', … ]`.
- [x] 3.6 (`apps/frontend`) Implementar `groupEventsByDay(events, timeZone?)`,
      `formatEventDay` e `formatEventTime`, com `Intl.DateTimeFormat` e `timeZone`
      explícito. Ver 3.5 passar.
      Feito; 14/14.
## 4. Componente (`components/KnowledgeDocumentHistoryTab.tsx`)

Apresentacional (convenção 7): recebe eventos, estado, `hasNextPage`,
`isFetchingNextPage`, a falha da página seguinte, `onLoadMore`, `onRetry`,
`onRetryNextPage` e o modo da base, e não importa hook de consulta.

- [x] 4.1 (`apps/frontend`) `KnowledgeDocumentHistoryTab.test.tsx`, os estados da
      D4, cada um com a negativa da tabela: carregando, falha (com `Tentar
      novamente` chamando `onRetry`), vazio, fim da lista. Ver reprovar com o
      componente devolvendo `null`.
      **Ciclo único para 4.1 a 4.8:** os 23 testes do componente escritos de uma
      vez e vistos reprovando contra o stub que devolve `null` — 23 de 23, por
      elemento ausente (`Unable to find … role "button" and name "Atualizar"`,
      `… role "listitem"`, `… text: Carregando histórico…`). Depois a
      implementação inteira.
- [x] 4.2 (`apps/frontend`) Implementar os estados. Ver 4.1 passar.
      Ver 4.1.
- [x] 4.3 (`apps/frontend`) Testes da lista (D3, D7, D8): um título (`h3`) por dia
      com a data; os eventos do dia como itens de lista; horário em `<time>` com
      `dateTime` igual ao `occurredAt`; tipo, título, detalhe e autor de cada
      evento; tipo e autor desconhecidos sem rótulo conhecido; nenhum link no
      título. Ver reprovar.
      Ver 4.1.
- [x] 4.4 (`apps/frontend`) Implementar a lista. Ver 4.3 passar.
      Ver 4.1.
- [x] 4.5 (`apps/frontend`) Testes de "Carregar mais" (D4, D6, D8): presente com
      `hasNextPage`, chama `onLoadMore`; com `isFetchingNextPage`, indica
      carregamento e um segundo clique não chama de novo; ausente no fim da lista;
      falha da página seguinte mantém os eventos, mostra o texto e `Tentar
      novamente` (chamando `onRetryNextPage`) e **não** mostra "Não há eventos
      mais antigos."; depois que os eventos crescem e `hasNextPage` vira falso, o
      foco está no primeiro evento acrescentado. Ver reprovar.
      Ver 4.1.
- [x] 4.6 (`apps/frontend`) Implementar "Carregar mais", a falha da página
      seguinte e o foco. Ver 4.5 passar.
      Ver 4.1.
- [x] 4.6a (`apps/frontend`) Testes do `Atualizar` (D4, D6): presente nos estados
      vazio, lista com mais e fim da lista, chamando `onRefresh`; **ausente** no
      carregamento e na falha da primeira página; com a releitura em andamento,
      indica carregamento e a lista continua visível; com a falha da releitura, a
      lista continua, o texto "Não foi possível atualizar o histórico." aparece e
      o texto de falha da primeira página **não**. Ver reprovar.
      Ver 4.1.
- [x] 4.6b (`apps/frontend`) Implementar o `Atualizar` e a falha dele. Ver 4.6a
      passar.
      Ver 4.1.
- [x] 4.7 (`apps/frontend`) Testes das notas (D9): `Manual` com a nota de começo e
      **sem** a de ignorados; `Synced` com as duas; a nota aparece nos estados de
      carregando, falha e vazio. Ver reprovar.
      Ver 4.1.
- [x] 4.8 (`apps/frontend`) Implementar as notas. Ver 4.7 passar.
      Feito; 23/23.
## 5. Página (`pages/KnowledgeBaseDetailPage.tsx`)

- [x] 5.1 (`apps/frontend`) Mudar os testes existentes de
      `KnowledgeBaseDetailPage.test.tsx` listados na D11, exatamente esses:
      `listKnowledgeDocumentEvents` no `vi.mock` e no `beforeEach` (página vazia
      por padrão); "apresenta as três abas, nesta ordem, com Documentos ativa por
      padrão" (`toHaveLength(3)` e a ordem dos nomes); a linha
      `['Histórico', '?tab=historico']` no `it.each` dos cards; em "a aba inativa
      não está montada", a negativa do conteúdo do histórico e de
      `listKnowledgeDocumentEvents` não chamada. Ver reprovar contra a página de
      hoje (duas abas) e registrar quais reprovaram.
      Visto reprovar contra a página de duas abas: "apresenta as três abas…"
      (`toHaveLength(3)`). **A linha `Histórico` do `it.each` passou com a página
      velha**: `?tab=historico` caía em Documentos e os cards continuavam acima
      da barra. Acrescentada ao mesmo `it.each` (da lista da D11) a asserção de
      que a aba do caso é a selecionada; com ela, reprovou. "A aba inativa não
      está montada" passa (negativa); guarda 7.8.
- [x] 5.2 (`apps/frontend`) `KnowledgeBaseDetailPage.history.test.tsx` (novo, no
      molde de `KnowledgeBaseDetailPage.test.tsx`, com `vi.mock` dos módulos de
      API): clicar em `Histórico` leva a `?tab=historico`; abrir `?tab=historico`
      seleciona a aba e lista os eventos; seta para a direita em `Documentos`
      seleciona `Histórico` (forma conferida na 1.3); a aba `Histórico` tem
      `textContent` `Histórico` com eventos carregados; carregar a segunda página,
      ir para `Documentos` e voltar chama a rota sem cursor e mostra só a primeira
      página; base `Synced` **inativa** com `access-denied` e um `Deleted` no
      histórico: o painel exibe `Excluído` e o card de origem **não** contém
      `exclu`, `remov` nem `apag` (D5); com duas páginas carregadas, `Atualizar`
      chama a rota uma vez sem cursor e deixa só a primeira página na tela; com a
      aba aberta e duas páginas carregadas, **desativar** a base pelo cabeçalho
      (confirmando) não faz nenhuma requisição à rota de eventos, e, numa base
      inativa, o `409` `knowledge-base-active` da exclusão (que relê a base) também
      não (D6). Ver reprovar.
      Visto reprovar: 9 de 9 (aba `Histórico` inexistente). Depois da conferência
      (R1-1) ganhou o caso "entrar na aba pede uma vez, também em StrictMode",
      visto reprovar contra o código de então (`got 3 times`), e o caso "voltar à
      aba" passou a zerar o mock **antes** de sair da aba.
- [x] 5.3 (`apps/frontend`) Em `KnowledgeBaseDetailPage.sync.test.tsx`: mudar
      `installFetch` (opção `events` e a rota `/knowledge-bases/{ID}/document-events`)
      e `renderPage` (parâmetro `search`), sem mudar nenhuma asserção existente;
      rodar o arquivo e ver os testes existentes passarem iguais. Depois,
      acrescentar: (a) base `Manual` **sem** `VITE_CONNECTORS_BASE_URL`, aba
      `Histórico`: a lista aparece, a rota de eventos foi chamada, e **nenhuma**
      chamada ao `apps/connectors`; (b) o mesmo **com** a variável; (c) base
      `Synced` com a aba `Histórico` aberta: o fim do "Sincronizar agora" (troca de
      `lastFinishedAt`) chama a rota de eventos de novo, sem cursor. Ver os três
      reprovarem contra a página de hoje (sem a aba); a guarda de (a) é a 7.12.
      Os 12 existentes passaram iguais com os helpers mudados. Os 3 novos
      reprovaram (aba inexistente).
- [x] 5.4 (`apps/frontend`) Implementar na página: `HISTORY_TAB`, `parseTab`, a aba
      entre as duas (sem contador), a consulta com `enabled` pela aba ativa, o
      recomeço da D6 pelo mecanismo escolhido na 1.3, e o painel com
      `KnowledgeDocumentHistoryTab` recebendo tudo por prop. Ver 5.1, 5.2, 5.3 (a)
      e 5.3 (b) passarem; 5.3 (c) continua reprovando.
      Feito. Página: 146/147, só 5.3 (c) reprovando, como previsto.
- [x] 5.5 (`apps/frontend`) No efeito de `finishedAt` da página, resetar a consulta
      do histórico ao lado das invalidações de documentos e resumo (D6). Ver 5.3 (c)
      passar.
      Feito; 15/15 no arquivo de sincronização.
- [x] 5.6 (`apps/frontend`) Comentários: o de `describe('barra de abas')` em
      `KnowledgeBaseDetailPage.test.tsx` diz o que mudou nesta change; o de
      `FORBIDDEN` em `syncStateMessages.test.ts` ganha o alcance da D5; o comentário
      da barra na página (`A BARRA DE ABAS NASCE AQUI…`) ganha a terceira aba.
      Nenhuma asserção muda.
      Feito (os três comentários).
## 6. Conferência do fio contra o `apps/api` local (opcional, se o mantenedor pedir)

- [ ] 6.1 (`apps/frontend`) Só se o mantenedor pedir antes da conferência visual:
      subir o `apps/api` deste worktree numa porta da faixa, com `TZ` e o Postgres
      conforme `docs/development.md`, gravar eventos pelas rotas de documento e
      abrir a aba contra ele. Registrar portas e encerrar ao fim. Sem pedido, a
      tarefa fica desmarcada com essa justificativa.
      **Não pedido** no prompt de 14/10/2026: desmarcada.

## 7. Guardas (convenção 15)

Cada guarda: trocar a implementação como descrito, rodar o teste, **ver reprovar
no teste nomeado**, desfazer, ver passar. Registrar aqui o teste que reprovou e a
mensagem, uma linha cada.

- [x] 7.1 `authorLabel` mapeando todo `service:*` para `Sincronização` → reprova o
      teste de `service:outro` (3.3).
      Reprovou: "autor desconhecido aparece como veio…" (`documentHistory.test.ts`) —
      `expected 'Sincronização' to be 'Autor que o painel não reconhece (ser…'`.
- [x] 7.2 `eventTypeLabel` com `default: 'Atualizado'` → reprova o de tipo
      desconhecido (3.1) e o da lista (4.3).
      Reprovaram: "tipo desconhecido volta com o valor recebido…" — `expected {
      label: 'Atualizado', known: true } to deeply equal { label: 'Archived', known:
      false }`; e "tipo e autor desconhecidos não exibem rótulo conhecido" (componente) —
      `expect(element).toHaveTextContent()`.
- [x] 7.3 `groupEventsByDay` sem `timeZone` no formatador da chave (dia do UTC) →
      reprova o caso `America/Sao_Paulo` (3.5).
      Troca feita: chave do dia formatada sempre em `'UTC'`. Reprovou: "agrupa pelo
      dia do fuso dado" — `expected '26/09/2026' to be '25/09/2026'`.
- [x] 7.4 `groupEventsByDay` comparando o dia com o do primeiro grupo, e não com o
      do último (um dia partido em dois grupos) → reprova o dia partido (3.5).
      **Reformulada:** a forma "agrupar cada página separadamente" não é expressável
      sem mudar o contrato do componente, que recebe a lista já concatenada. A
      guarda reintroduz o mesmo sintoma (um dia em dois grupos) na função dona dele:
      comparar com o PRIMEIRO grupo em vez do último. Reprovou: "um dia partido entre
      duas páginas vira um grupo só" — `expected [ '26/09/2026', '25/09/2026', …(2) ]
      to deeply equal [ '26/09/2026', '25/09/2026', …(1) ]`.
- [x] 7.5 Tratar `events === undefined` como lista vazia → reprova "carregando não
      é vazio" (4.1).
      Troca feita: `events = []` como padrão da prop no componente. Reprovaram:
      "carregando não é vazio nem falha…" — `Unable to find … text: Carregando
      histórico…`; e "falha não é vazio…" — `Unable to find … text: Não foi possível
      carregar o histórico desta base.`
- [x] 7.6 Tratar a falha da página seguinte como fim da lista → reprova "falha na
      página seguinte preserva o que foi carregado" (4.5).
      **Reformulada:** troca feita foi ignorar a falha da página seguinte
      (`nextPageFailed ? (` → `false ? (`). Reprovou: "falha na página seguinte
      preserva o que foi carregado e não diz que acabou" — `Unable to find … text: Não
      foi possível carregar mais eventos.`
- [x] 7.7 Tirar a mudança de foco depois de "Carregar mais" → reprova o teste de
      foco (4.5).
      Troca feita: `.focus()` por `.blur()`. Reprovou: "depois de carregar a última
      página, o foco está no primeiro evento acrescentado" —
      `expect(element).toHaveFocus()`.
- [x] 7.8 `enabled: true` fixo na consulta do histórico → reprova "a aba inativa não
      está montada" (5.1).
      Reprovou: "a aba inativa não está montada" (`KnowledgeBaseDetailPage.test.tsx`) —
      `expected "vi.fn()" to not be called at all, but actually been called 1 times`.
- [x] 7.9 Tirar o recomeço da D6 → reprova o teste de voltar à aba (5.2).
      Troca feita: o efeito de saída da aba sem `restartDocumentEvents`. Reprovou:
      "voltar à aba recomeça da primeira página" — `expected "vi.fn()" to be called 1
      times, but got 2 times` (as duas páginas relidas).
- [x] 7.10 `rightSection` com o número de eventos carregados na aba `Histórico` →
      reprova o teste de ausência de contador (5.2).
      Reprovaram 4, entre eles "a aba Histórico não tem contador…" — `Unable to find
      an accessible element with the role "tab" and name "Histórico"`. O contador
      muda o nome acessível da aba, e é por ele que o teste reprova (antes do
      `textContent`); um contador `aria-hidden` cairia no `textContent`.
- [x] 7.11 Tirar o reset do histórico do efeito de `finishedAt` → reprova 5.3 (c).
      Reprovou: "o fim do ciclo com a aba aberta relê o histórico, sem cursor" —
      `expected 1 to be greater than 1`.
- [x] 7.12 Condicionar o `enabled` do histórico a `connectorsBaseUrl() !== null` →
      reprova 5.3 (a), base manual sem a variável.
      Reprovou: "sem VITE_CONNECTORS_BASE_URL: lista os eventos…" — `Unable to find an
      element with the text: Política de trocas`. O caso "com" a variável passou,
      como esperado.
- [x] 7.13 `refetchInterval: 5000` na consulta do histórico → reprova o caso "sem
      chamada nova" de 2.4.
      Reprovou: "não relê com o tempo" — `expected "vi.fn()" to be called 1 times, but
      got 13 times`.
- [x] 7.14 Acrescentar " Se nada resolver, exclua a base." ao `remedy` de
      `access-denied` em `syncStateMessages.ts` → reprova a varredura de
      `syncStateMessages.test.ts` **e** o teste da D5 em
      `KnowledgeBaseDetailPage.history.test.tsx`, este na asserção do **card**, e
      não na do painel. Registrar as duas reprovações.
      Reprovaram 3: a varredura "nenhum texto de falha … contém exclu, remov ou apag"
      — `expected 'Sem acesso à pasta A conta sync@exemp…' not to match
      /exclu|remov|apag/i`; "access-denied mostra o e-mail…" (texto exato); e o teste
      da D5, "o painel exibe Excluído e o card de origem continua sem…" —
      `expect(element).not.toHaveTextContent()`, na asserção do **card** (a do painel,
      `Excluído`, vem antes e passou).
- [x] 7.15 Chave do histórico de volta para dentro do prefixo
      (`['knowledge-bases', id, 'document-events']`) → reprova o teste de
      desativar com a aba aberta (5.2). O caso do `409` continua passando, e isso
      é esperado (D6): registrar as duas coisas.
      Reprovaram: "desativar a base com duas páginas carregadas não pede eventos" —
      `expected "vi.fn()" to not be called at all, but actually been called 2 times`
      (as duas páginas relidas pela invalidação do prefixo); e o par determinístico
      "a chave fica fora do prefixo das bases" — `expected 'knowledge-bases' not to
      be 'knowledge-bases'`. O teste do `409` passou com a chave no prefixo, como a
      D6 prevê.
- [x] 7.16 `Atualizar` chamando `refetch()` da consulta inteira, sem aparar → reprova
      o teste de `Atualizar` com duas páginas na página (5.2): a rota é chamada com
      cursor e a segunda página continua na tela.
      Reprovou: "Atualizar relê só a primeira página, sem cursor" — `expected
      "vi.fn()" to be called 1 times, but got 2 times`.
- [x] 7.17 (acrescentada na conferência visual, R1-1) `trimDocumentEvents`
      cancelando sempre, e não só um "Carregar mais" em voo → reprova "entrar na aba
      pede uma vez, também em StrictMode" (`KnowledgeBaseDetailPage.history.test.tsx`)
      — `expected "vi.fn()" to be called 1 times, but got 2 times`. Medido também o
      contrário: com a limpeza RELENDO (`restartDocumentEvents`) e sem o
      cancelamento, o teste passa — a causa do pedido duplo é o cancelamento, e não
      a releitura.
- [x] 7.9 reverificada depois da correção R1-1 (o efeito de saída agora chama
      `trimDocumentEvents`): tirar a chamada reprova "voltar à aba recomeça da
      primeira página" — `expected "vi.fn()" to be called 1 times, but got 2 times`.

## 8. Verificação do frontend

- [x] 8.1 (`apps/frontend`) `npm run lint`, `npm run format:check` (só os arquivos
      desta change contam; os da 1.1 são pré-existentes), `npm run test` e
      `npm run build`, com `uptime` antes. Registrar cada resultado.
      Feito em 14/10/2026: `prettier --write` só nos 14 arquivos da change;
      `npm run lint` **verde**; `npm run format:check` reprova na **mesma lista de
      52** da 1.1 (diff vazio), nenhum desta change; `npm run test` **verde**, 136
      arquivos, **1782/1782**, 185 s (00:26:04 a 00:29:09), load **5,80** no início e
      31,03 no fim; `npm run build` **verde**.
- [x] 8.2 (`apps/frontend`) Comparar **por nome** com a lista da 1.2: todo teste da
      baseline continua existindo e passando, salvo os renomeados da D11 (um:
      "apresenta as duas abas…" → "apresenta as três abas…"); os novos são só os
      dos arquivos da D11. Registrar total antes e depois e a diferença por
      arquivo.
      Por nome contra `baseline-names.txt`: **1724 − 1 + 59 = 1782**. Sumiu só
      "apresenta as duas abas, com Documentos ativa por padrão", renomeado (D11).
      Novos por arquivo: `documentHistory.test.ts` 14,
      `KnowledgeDocumentHistoryTab.test.tsx` 23, `KnowledgeBaseDetailPage.history.test.tsx`
      9, `useKnowledgeDocuments.test.ts` 5, `knowledgeDocumentsApi.test.ts` 3,
      `KnowledgeBaseDetailPage.sync.test.tsx` 3, `KnowledgeBaseDetailPage.test.tsx` 2
      (o renomeado e a linha `Histórico` do `it.each`). Nenhum teste fora dos
      arquivos da D11 mudou de nome ou de estado.
      **Refeitas 8.1 e 8.2 depois das correções da conferência visual** (00:58:31 a
      01:00:41, load 3,92 no início e 27,03 no fim): lint **verde**; suíte **verde**,
      136 arquivos, **1783/1783**; por nome, **1724 − 1 + 60**, o 60º sendo "entrar
      na aba pede uma vez, também em StrictMode" (`KnowledgeBaseDetailPage.history.test.tsx`,
      agora 10); `format:check` com a mesma lista de 52; build **verde**.

## 9. Conferência visual (D12)

- [x] 9.1 (`apps/frontend`) Subir o Vite numa porta livre da faixa 59300–59399, com
      `VITE_API_BASE_URL` para outra porta da faixa sem processo. Registrar aqui as
      duas portas e o `lsof` que as mostrou livres.
      Feito em 14/10/2026: `lsof` da faixa vazio antes; Vite em **59310**
      (`VITE_API_BASE_URL=http://localhost:59311`, porta sem processo); Chrome
      headless com CDP em **59312**.
- [x] 9.2 (`apps/frontend`) Chrome headless por CDP, respostas montadas pelo
      `Fetch`, os estados da D12 nos dois esquemas e nas duas larguras, contra as
      pranchas 3b e 4c. Medir o que a D12 lista; iterar até uma rodada sem achado.
      Capturas em `design/capturas/`, achados e correções numa tabela na D12.
      Três rodadas, 44 capturas cada (11 estados × 2 esquemas × 2 larguras), script
      CDP com `Fetch.fulfillRequest` e preflight; nenhuma requisição sem resposta
      montada. Achados e correções na D12: **R1-1** (2 requisições por entrada, em
      StrictMode — causa no cancelamento de `trimDocumentEvents`, corrigida com teste
      novo e guarda 7.17), **R1-2** (faixa do dia grudada no cabeçalho — C9), **R2-1**
      ("Mais recentes primeiro" a 4,14:1 — C10). A rodada 3 não teve achado desta
      change. Medidos e não corrigidos, por serem padrão da casa: `SectionLabel` de
      cabeçalho de card a 4,14:1 e o `outline` vermelho dentro do `Alert` a 4,28:1
      (família da #155). Capturas da rodada 3 e `medidas-r2.json`/`medidas-r3.json`
      em `design/capturas/`.
- [x] 9.3 (`apps/frontend`) Encerrar só os processos que esta change iniciou.
      Registrar o `lsof` da faixa depois.
      Encerrados os três processos iniciados por esta change (PIDs 70246 `npm exec
      vite`, 70272 `vite`, 70247 Chrome headless). `lsof` da faixa 59300–59399
      depois: nenhum processo escutando.
- [x] 9.4 **Validação manual pelo mantenedor** (convenção 14), quadro a quadro. O
      archive espera por ela, e cada rodada que mudar código volta ao grupo 8.
      Ambiente montado em 14/10/2026: Postgres `buteco-101-pg` (59320) e RabbitMQ
      `buteco-101-rmq` (59321) em contêineres próprios, `apps/api` deste worktree em
      59330 (`TZ=America/Sao_Paulo`, migrações aplicadas, `Connectors__BaseUrl` em
      59332 sem processo), painel em 59331. Base manual "Validação #101" com **57
      eventos** criados pela API com o token de operador (14 inclusões, 20
      atualizações só de conteúdo, 10 só de título, 8 dos dois, 5 exclusões): 50 na
      primeira página e 7 na segunda. **Todos no mesmo dia**: a rota não aceita data,
      e `OccurredAt` é `DateTimeOffset.UtcNow` no `apps/api`
      (`KnowledgeDocumentEvent.cs`).
      **Validação manual feita pelo dono em 14/10/2026**, sem ajuste pedido. Ambiente
      encerrado depois do aviso: `apps/api` (PIDs 73496, 73510), Vite (73553) e os
      contêineres `buteco-101-pg` e `buteco-101-rmq` (removidos com volume). Faixa
      59300–59399 conferida vazia; os contêineres do compose do diretório principal
      não foram tocados.

## 10. Documentação (raiz do repositório)

Escrita **antes** de qualquer archive, e entra no PR.

- [x] 10.1 `openspec/specs/knowledge-base-catalog-ui/spec.md`: o `Purpose` diz
      "só a primeira pertence a esta capability", "Quem for mexer numa das duas
      abas" e cita só duas capabilities de aba; passa a citar as três
      (`knowledge-document-history-ui` incluída). Só o `Purpose`, que não está na
      delta.
      Feito: o `Purpose` cita as três capabilities de aba.
- [x] 10.2 `02-HISTORICO_E_STATUS.md`: entrada da change (o contrato consumido, as
      guardas da 7 com o resultado, o que a conferência mediu, as portas) e C1 a C7
      da D10 em "Correções de protótipo".
      Feito: entrada `frontend-aba-historico` no fim do `02`, e a 39ª à 48ª
      correção (C1 a C10) na lista viva, com a contagem para quarenta e oito.
- [x] 10.3 `CHANGELOG.md`, em `[Unreleased]`: `Added` (aba Histórico no detalhe da
      base).
      Feito: `Added`, depois da entrada da #136.
- [x] 10.4 `grep` de "duas abas" e de "Diagnóstico do índice" em `docs/`, `01` e
      `openspec/specs/`; corrigir o que afirmar duas abas no detalhe da base
      (convenção 13), e registrar aqui o que foi achado, mesmo que nada.
      `grep` de "duas abas": `openspec/specs/knowledge-base-catalog-ui/spec.md:397`,
      o cenário que a delta desta change substitui no sync (nada a fazer antes);
      `01:435` e `inbox-channel-catalog-ui` falam de outras telas. "Diagnóstico do
      índice" em `docs/`: nenhuma ocorrência.
- [x] 10.5 `python3 scripts/check-docs.py` verde.
      Verde: "Integridade da documentação: OK".
- [x] 10.6 `openspec validate frontend-aba-historico --strict` verde.
      Verde: "Change 'frontend-aba-historico' is valid".
- [x] 10.7 Parar e relatar. Sem commit, push, PR, sync nem archive: o mantenedor
      decide cada um. Na sincronização, a capability nova precisa de `Purpose`
      escrito à mão (o archive gera um provisório).
      Cumprida: relatado e parado; o archive, com sync, foi pedido pelo dono em
      14/10/2026 (`/opsx:archive`). A 6.1 fica desmarcada: não foi pedida.
