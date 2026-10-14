## Context

A #98 (`historico-documentos-base`, arquivada em
`openspec/changes/archive/2026-10-02-historico-documentos-base/`) entregou o
histórico no `apps/api` e deixou a tela para esta change. **O contrato que existe**,
lido no código e não na proposta da #98:

| o quê | onde foi lido | o que é |
|---|---|---|
| rota | `KnowledgeDocumentEndpoints.cs:50` | `GET /knowledge-bases/{knowledgeBaseId:guid}/document-events` |
| página | `ListKnowledgeDocumentEventsQueryHandler.cs` (`PageSize = 50`) | 50 fixos, sem parâmetro de tamanho; busca 51 para saber se há próxima |
| corpo | `KnowledgeDocumentEventPageResponse.cs` | `{ "items": [...], "nextCursor": string \| null }`; `nextCursor` nulo **exatamente** na última página |
| cursor | `KnowledgeDocumentEventCursor.cs` | opaco, base64url de 24 bytes; pedido com `?cursor=`; malformado → `400` na chave `cursor` |
| ordem | o mesmo handler | `occurredAt` decrescente, desempate por `id` decrescente; keyset, então evento novo no topo não duplica nem desloca a página seguinte |
| item | `KnowledgeDocumentEventResponse.cs` | `id`, `documentId`, `documentTitle`, `type`, `contentChanged`, `titleChanged`, `author`, `occurredAt` |
| `type` | `KnowledgeDocumentEventType.cs` | string `Created`, `Updated` ou `Deleted` (`JsonStringEnumConverter`) |
| detalhe | `KnowledgeDocumentEvent.cs`, `CHECK` da migração | `contentChanged`/`titleChanged` nulos em `Created` e `Deleted`; não nulos e pelo menos um verdadeiro em `Updated` |
| título | `KnowledgeDocumentEvent.cs` | **snapshot**: o título depois da escrita; na exclusão, o que o documento tinha. Sem FK para o documento |
| autor | `knowledge-document-history`, requisito "Autor é o subject de quem escreveu" | subject do token, verbatim: `operator` ou `service:connectors`. O rótulo é do frontend |
| erros | o mesmo handler | base inexistente `404`; base sem evento `200` com `items` vazio; base inativa lida normalmente |
| fio | `KnowledgeDocumentCatalogTests.cs:653-674` | afirmado sobre o **texto** da resposta |
| retenção | D9 da #98, #109 | sem limite; gatilho em 1 milhão de linhas, na #109 |
| retroativo | D10 da #98 | a migração não cria eventos; documentos anteriores não têm `Created` |

**Lacunas entre a #101 e o contrato: nenhuma que pare a change.** Tudo o que a
issue pede tem campo na rota: tipo, nome, detalhe, autor, instante (para o
agrupamento por dia) e cursor (para o "Carregar mais"). O "nome do arquivo" da
issue é o `documentTitle`, o mesmo nome que a aba `Documentos` mostra; o documento
não tem outro nome no sistema. O que **o protótipo** desenha e o contrato não
tem (título anterior, motivo da exclusão) é correção de protótipo (D10), não
lacuna da issue.

**A tela de hoje** (`KnowledgeBaseDetailPage.tsx`, depois da #99, da #107 e da
#136): cabeçalho com `Editar`, `Ativar`/`Desativar` e `Excluir`; acima da barra, a
descrição, o card de origem (só `Synced`) e os agentes; a barra com `Documentos`
(canônica, sem parâmetro, com contador) e `Diagnóstico do índice`
(`?tab=diagnostico`); `keepMounted={false}`; a consulta do diagnóstico mora na
página com `enabled` pela aba ativa e chega ao componente por prop (convenção 7).
O fim de um "Sincronizar agora" invalida documentos e resumo de indexação
(`KnowledgeBaseDetailPage.tsx:150-156`).

**O protótipo** é o canvas "Bases sincronizadas — protótipo"
(`https://claude.ai/artifact/XzvXSpPRpqaCjNZ45ooAPY`), pranchas **3b** (base
manual, `HistoricoManual.dc.html`) e **4c** (base sincronizada,
`Historico.dc.html`), lidas nesta change pela ferramenta Artifact. O
`canvas.json` não tem notas.

**Não existe paginação no painel.** Nenhum `useInfiniteQuery`, nenhum
"Carregar mais" em `apps/frontend/src`. Esta é a primeira.

## Goals / Non-Goals

**Goals:**

- O operador vê, na aba `Histórico`, o que aconteceu com os documentos da base,
  quem fez e quando, e anda para trás com "Carregar mais".
- Nenhum estado da aba se passa por outro: carregando, falha, vazio, fim da lista
  e falha da página seguinte têm textos próprios.
- A aba funciona igual em base manual e sincronizada, sem depender da
  sincronização.

**Non-Goals:**

- Mudar o `apps/api`. Nenhum campo, filtro, total ou tamanho de página novo.
- Filtro por documento, por tipo, por autor ou por período. Sem rota que o sirva.
- Contador na aba (a rota não tem total).
- Atualização em intervalo do histórico aberto (D6).
- Retenção (#109).

## Decisions

### D1. A aba entra entre `Documentos` e `Diagnóstico do índice`, no desenho das outras duas

- `HISTORY_TAB = 'historico'`, e `parseTab` passa a reconhecer três valores.
  `Documentos` continua canônica sem parâmetro; valor desconhecido continua caindo
  nela sem reescrever o endereço.
- Ordem da barra: `Documentos`, `Histórico`, `Diagnóstico do índice` (issue e
  pranchas 3b/4c). `h={40}` como as outras, **sem** `rightSection`: a rota não tem
  total, e um número de eventos carregados seria a contagem das páginas pedidas,
  não do histórico (convenção 13).
- A consulta mora na **página**, com `enabled: activeTab === HISTORY_TAB`, como a
  do diagnóstico; o componente `KnowledgeDocumentHistoryTab` é apresentacional e
  recebe tudo por prop (convenção 7).
- O painel fica dentro do `Tabs` com `keepMounted={false}`, como os outros.

- *Descartado, histórico dentro da aba `Documentos`:* a própria issue descartou —
  mistura o estado atual com o passado.

### D2. Autor: o rótulo é do painel, o valor desconhecido aparece como veio

| `author` | exibido | por quê |
|---|---|---|
| `operator` | `Operador` | subject emitido pelo login (`AuthEndpoints.cs`, `tokenService.Issue("operator", ...)`) |
| `service:connectors` | `Sincronização` | subject das escritas do `apps/connectors` (requisito de autor de `knowledge-document-history`) |
| outro, não vazio | `Autor que o painel não reconhece (<valor>)` | o mesmo molde do motivo desconhecido de `syncStateMessages.ts` ("Motivo que o painel não reconhece (codigo-novo)") |
| vazio | `Autor não registrado` | não é alcançável hoje (a D6 da #98 falha a escrita antes de gravar autor vazio), mas a tela não o colapsa em `Operador` |

**Operador e sincronização se distinguem pelo texto**, na coluna do autor. Sem
cor nem ícone próprios: a coluna tem dois valores reais, e o texto já os separa
para quem enxerga e para quem usa leitor de tela.

**"Operador que não existe mais" não é um estado deste sistema.** Não há contas
de operador: o subject é um só, `operator`, emitido pelo login com senha única.
O rótulo `Operador` não afirma uma pessoa, afirma o papel, e continua verdadeiro
para todo evento gravado com ele. Se um dia houver operadores com subject próprio,
os subjects novos caem no rótulo neutro com o valor gravado até o painel
aprendê-los, e nenhum vira `Operador` por engano. É a regra do valor de enum
desconhecido (convenção 13): indicador neutro, nunca o indicador de outro valor.

- *Descartado, mapear todo `service:*` para `Sincronização`:* `service:inbox`
  existe e não escreve documento; um prefixo genérico afirmaria a sincronização
  para um autor que não é ela.

### D3. Tipo e detalhe

- `Created` → `Incluído` (verde), `Updated` → `Atualizado` (azul), `Deleted` →
  `Excluído` (cinza), em `Badge` `variant="light"` do Mantine, que troca de ponta
  com o esquema sozinho (convenção 16; correção C5). Ícone `aria-hidden`; o texto
  é o significado.
- Tipo desconhecido → `Badge` `variant="outline"` cinza com o valor recebido. Não
  reaproveita o cinza-claro de `Excluído`, para não parecer um deles.
- Detalhe só em `Updated`, pelos dois booleanos: só conteúdo → `Conteúdo
  alterado`; só título → `Título alterado`; os dois → `Conteúdo e título
  alterados`. `Updated` com os dois falsos ou nulos não é gravável (a `CHECK` da
  #98), e se chegasse a tela mostraria `Atualizado` sem detalhe, sem afirmar o
  que mudou.
- O título é o `documentTitle` do evento, e nunca o título atual do documento: um
  evento antigo de um documento renomeado mostra o nome de então, que é o que
  aconteceu (D7).

### D4. Desconhecido não vira zero nem vazio: cinco estados, cinco textos

| estado | como chega | o que a aba mostra | o que ela NÃO mostra |
|---|---|---|---|
| carregando | primeira página sem resposta | `Loader` e "Carregando histórico…" | lista, texto de vazio, texto de falha, `Carregar mais`, `Atualizar` |
| falha | primeira página com erro (inclusive `404` de base apagada entre a leitura do detalhe e a da aba) | `Alert` vermelho "Não foi possível carregar o histórico desta base." e o botão `Tentar novamente` | texto de vazio, `Atualizar` |
| vazio | `200`, `items` vazio, `nextCursor` nulo | "Nenhuma mudança foi registrada nesta base." e `Atualizar` | falha, `Carregar mais` |
| lista com mais | `nextCursor` não nulo | os grupos, `Carregar mais` e `Atualizar` | "não há eventos mais antigos" |
| fim da lista | `nextCursor` nulo, `items` não vazio | os grupos, "Não há eventos mais antigos." e `Atualizar` | `Carregar mais` |

E dois que só existem depois da primeira página:

- **falha da página seguinte.** Os eventos carregados ficam; abaixo deles, `Alert`
  "Não foi possível carregar mais eventos." com `Tentar novamente`, que repete o
  mesmo cursor. Ela NÃO diz "não há eventos mais antigos": seria afirmar o fim de
  uma lista cuja continuação não foi lida.
- **falha do `Atualizar`.** A lista exibida fica (é a primeira página que já
  estava na tela), e acima dela um `Alert` "Não foi possível atualizar o
  histórico." O próprio `Atualizar` é o tentar de novo. Ela NÃO troca a lista pela
  falha da primeira página: o que está na tela foi lido, só não é o mais novo.

**`Atualizar`** (D6) fica no cabeçalho do card, ao lado de "Mais recentes
primeiro", nos estados vazio, lista com mais e fim da lista, e com `loading`
enquanto a primeira página é relida. Ele não aparece no carregamento (não há o que
atualizar) nem na falha da primeira página, que já tem `Tentar novamente`.

O vazio é o estado **"sei que não existe"** da convenção 13: a rota respondeu e
disse que não há. Ele leva explicação, e a nota da D9 diz por que uma base com
documentos pode ter histórico vazio (documentos anteriores à implantação).

A nota da D9 aparece nos cinco estados: ela descreve o histórico, não a resposta.

Cada estado tem teste com a asserção negativa da coluna da direita.

### D5. As palavras proibidas não alcançam o histórico, e a varredura continua pegando os textos de falha

**Onde a regra está, e por quê.** Requisito "Alerta de falha da sincronização com
o motivo pelo código" de `knowledge-base-sync-state-ui`: nenhum texto de falha da
base, de arquivo ignorado nem do pedido de sincronização contém `exclu`, `remov`
ou `apag`. Ela nasceu na #107 (D4 e C6 de `frontend-detalhe-base-sincronizada`,
decisão do mantenedor em 04/10/2026) para a tela não mandar excluir a base quando
o painel ainda não oferecia a ação. A #136 (D6 de `exclusao-base-painel`) manteve
a regra e trocou o motivo: **uma falha de sincronização nunca se resolve excluindo
a base**; o remédio é na pasta, na instalação ou na próxima rodada, e o alerta
aparece também em base ativa.

**Os guardas de hoje**, todos sobre a **fonte** do texto ou sobre o **card**, e
nenhum sobre a página:

| guarda | sobre o quê |
|---|---|
| `syncStateMessages.test.ts:27-31` e `:199-228` | a saída de `syncFailureMessage`, `ignoredFileReason` e `connectorErrorMessage(..., 'sync-request')`, mais de 40 textos |
| `KnowledgeBaseSyncOriginCard.test.tsx:146-149` | o card de origem renderizado com a base falhando |
| `KnowledgeBaseDetailPage.delete.test.tsx:390-418` | o card de origem na página, base inativa com `access-denied` e o `Excluir` disponível |

**Decisão: a regra não vale para o histórico.** O motivo dela é não oferecer a
exclusão como remédio. O histórico não oferece remédio nenhum: ele registra uma
escrita que o `apps/api` gravou como `Deleted`. A própria issue nomeia o tipo
"excluído", e a aba `Documentos` já usa "Excluir" e "Documento excluído" para a
mesma ação. Trocar o rótulo por um eufemismo ("saiu da base") esconderia o fato
atrás de uma palavra que a tela não usa em nenhum outro lugar para ele, e
estenderia a regra para além do seu motivo.

**Como a varredura continua pegando os textos de falha:**

1. **As varreduras ficam onde estão, sobre as fontes e sobre o card.** Nenhuma
   passa a varrer a página nem o painel de uma aba; por isso o `Excluído` na
   mesma tela não as reprova e elas não precisam de exceção. Uma exceção para o
   histórico dentro da varredura seria o caminho por onde um texto de falha com
   "exclua" passaria.
2. **Os rótulos do histórico moram fora das fontes varridas**, em
   `utils/documentHistory.ts`. `syncStateMessages.ts` e `connectorErrors.ts` não
   importam nada dele, e ele não importa nada deles.
3. **Teste novo na página**, base `Synced` inativa com `access-denied` e a aba
   `Histórico` aberta com um `Deleted`: o painel exibe `Excluído` **e** o card de
   origem, na mesma tela, continua sem `exclu`, `remov` e `apag`. É o cenário que
   prova que as duas regras convivem.
4. **Guarda reverificado (convenção 15):** acrescentar "exclua a base" a um texto
   de `syncFailureMessage` faz a varredura de `syncStateMessages.test.ts` reprovar,
   e acrescentá-lo ao alerta do card faz o teste novo do item 3 reprovar no card.
   Resultado registrado no `tasks.md`.
5. **O alcance fica escrito** no requisito de `knowledge-base-sync-state-ui`
   (delta desta change) e no comentário da constante `FORBIDDEN` de
   `syncStateMessages.test.ts`. O teste não muda; só o comentário.

Os textos próprios da aba (vazio, falha, fim, nota) não falam em excluir nada,
mas não entram numa varredura: não há motivo da regra que os alcance.

- *Descartado, rótulo `Saiu da base` para `Deleted`:* ver acima.
- *Descartado, varredura sobre a página inteira com exceção para o painel do
  histórico:* troca três guardas precisos por um amplo com buraco.

### D6. Paginação e volume

**Consulta.** `useInfiniteQuery` com `initialPageParam: null`,
`queryFn` pedindo `listKnowledgeDocumentEvents(id, pageParam)` e
`getNextPageParam: (page) => page.nextCursor ?? undefined`. A chave é
`['knowledge-document-events', id]`, **fora** do prefixo das bases (abaixo).
`refetchOnWindowFocus: false` e sem `refetchInterval`.

**A chave fica fora do prefixo `['knowledge-bases']`.** Uma invalidação que
alcance uma consulta infinita ativa a relê **inteira**, página por página: é o
custo que esta decisão existe para evitar. Levantamento por `grep` de
`invalidateQueries`, `resetQueries`, `removeQueries`, `refetchQueries` e
`setQueryData` em `apps/frontend/src` (13/10/2026, `7559924`):

| arquivo:linha | operação | chave | exata? | alcançaria `['knowledge-bases', id, 'document-events']`? |
|---|---|---|---|---|
| `knowledge-bases/api/useKnowledgeBases.ts:83` (`cacheUpdatedKnowledgeBase`: criar, editar, ativar, **desativar**) | `invalidateQueries` | `['knowledge-bases']` | **não** | **sim** — desativar pelo cabeçalho, com a aba aberta, releria todas as páginas |
| `knowledge-bases/api/useKnowledgeBases.ts:82` | `setQueryData` | `['knowledge-bases', id]` | — | não (escreve uma chave) |
| `knowledge-bases/api/useKnowledgeDocuments.ts:75` (mutações de documento) | `invalidateQueries` | `['knowledge-bases', id, 'documents']` | não | não (prefixo de documentos) |
| `knowledge-bases/pages/KnowledgeBaseDetailPage.tsx:154` (fim do ciclo) | `invalidateQueries` | `['knowledge-bases', id, 'documents']` | não | não |
| `knowledge-bases/pages/KnowledgeBaseDetailPage.tsx:155`, `:378` | `invalidateQueries` | `['knowledge-bases', 'indexing-summary']` | não | não |
| `knowledge-bases/pages/KnowledgeBaseDetailPage.tsx:216-220` (desmontagem depois do `204`) | `removeQueries` | `['knowledge-bases', id]` exata; `['knowledge-bases', id, 'documents']` | sim; não | não |
| `knowledge-bases/pages/KnowledgeBaseDetailPage.tsx:377`, `:390` (`204` e `404` da exclusão) | `invalidateQueries` | `['knowledge-bases']` | **sim** | não |
| `knowledge-bases/pages/KnowledgeBaseDetailPage.tsx:388` (`409` da exclusão) | `refetch()` da consulta da base | `['knowledge-bases', id]` | — (só aquela consulta) | não |
| `agents`, `channels`, `mcp-servers` (`useAgents.ts`, `useChannels.ts`, `useMcpServers.ts`) | `invalidateQueries`/`setQueryData` | `['agents']`, `['channels']`, `['mcp-servers']` | não | não (outro prefixo) |

Nenhuma outra feature usa chave `['knowledge-bases', ...]`. **Uma** invalidação
não exata alcança o prefixo, e é a do caminho mais comum do cabeçalho (desativar).

- **Escolhida, `['knowledge-document-events', id]`:** nenhuma invalidação de base
  a alcança, e nada precisa mudar nas existentes. É o mesmo raciocínio, e o mesmo
  formato, da chave do diagnóstico (`useKnowledgeIndex.ts:7-13`, "CHAVE PRÓPRIA,
  SEM O PREFIXO DAS BASES"): mudar a base não muda o histórico. Desativar,
  ativar ou editar a base não gera evento de documento (#98, D2: só as três
  escritas de documento gravam).
- *Descartada, dentro do prefixo com todas as invalidações exatas:* exigiria
  tornar exata a de `cacheUpdatedKnowledgeBase`, que hoje relê de propósito a
  listagem **e** o resumo de indexação (`indexingSummaryQueryKey` está sob o
  prefixo e depende dela, comentário em `useKnowledgeBases.ts:30-49`). Seria mudar
  o comportamento de quatro mutações para servir a uma consulta que não depende
  delas, e qualquer invalidação por prefixo escrita depois voltaria a alcançá-la
  em silêncio.

Guardado por teste: com a aba aberta e duas páginas carregadas, desativar a base
pelo cabeçalho não faz nenhuma requisição à rota de eventos (guarda 7.15: a chave
de volta ao prefixo reprova). O mesmo teste cobre o `409` da exclusão (base
inativa, outra aba a reativou), cujo caminho relê só a base; ali a ausência passa
com as duas chaves, e fica como negativa, não como guarda.

A chave fora do prefixo também não é alcançada pela limpeza da desmontagem depois
do `204` (`:216-220`). Não precisa: a base excluída não volta a ser aberta, e a
consulta sem observador sai do cache pelo `gcTime` padrão.

**"Carregar mais" e não rolagem infinita.** O botão é previsível pelo teclado e
pelo leitor de tela, não dispara requisição por rolagem acidental, e é o que a
issue e as pranchas desenham.

**A lista é a concatenação das páginas, agrupada depois** (D7): um dia partido
entre duas páginas vira um grupo só.

**Releitura sempre a partir da primeira página.** Ao entrar na aba, e ao fim de um
"Sincronizar agora" com a aba aberta, o histórico recomeça da primeira página e
descarta as carregadas. O motivo é o custo da alternativa: o refetch de uma
consulta infinita no TanStack Query relê **todas** as páginas em sequência, e um
operador que carregou 20 páginas pagaria 20 requisições a cada volta à aba ou a
cada foco na janela. Recomeçar pela primeira mostra o mais novo, que é o que se
procura ao entrar, e custa uma requisição.

- Entrar na aba: a reentrada começa da primeira página, sem as carregadas na
  visita anterior. O mecanismo (abaixo) é conferido no pacote instalado antes de
  escrever (tarefa 1.3, convenção 6); o comportamento é o do requisito e o do
  teste.
- Fim do "Sincronizar agora": o efeito que já invalida documentos e resumo
  (`finishedAt`) passa a recomeçar o histórico da primeira página. O card de
  origem fica acima da barra, então o operador pode pedir a sincronização com o
  histórico aberto.
- **`Atualizar`**, no cabeçalho do card (D4): o mesmo recomeço, sob comando do
  operador. Volta à primeira página, sem cursor, descartando as carregadas, com
  `loading` no botão enquanto relê. Ao acionar, a tela passa a mostrar só a
  primeira página que já tinha (as seguintes saem na hora), e ela é trocada pela
  primeira página nova quando esta chega. Nunca volta ao estado de carregamento.
- **Sem consulta em intervalo.** O histórico não é processo em andamento
  (convenção 20). Uma rodada periódica da sincronização (a cada 5 minutos,
  `apps/connectors`) que grave eventos com a aba aberta aparece ao acionar
  `Atualizar` ou ao voltar à aba. O botão existe porque sem ele a tela não teria
  como dizer isso nem como resolver sem sair da aba; o requisito escreve as duas
  formas.

**Dois passos para os três recomeços** (entrada na aba, fim do "Sincronizar
agora", `Atualizar`): `trimDocumentEvents()` apara o cache da consulta para a
primeira página (`setQueryData` sobre `pages` e `pageParams`), e
`restartDocumentEvents()` apara e chama
`refetchQueries({ queryKey, type: 'active', exact: true })`. A saída da aba só
apara; o fim do ciclo e o `Atualizar` aparam e relêem.

**Corrigido na conferência visual (R1-1):** a primeira versão cancelava a consulta
antes de aparar, sempre. Com o painel em `<StrictMode>` (`main.tsx`), a limpeza do
efeito de saída roda logo após a montagem, e o cancelamento da busca da primeira
página fazia a entrada na aba pedir duas vezes (três no jsdom). Agora só se
cancela um "Carregar mais" em voo (`fetchMeta.fetchMore`), que é o caso em que
o cancelamento importa: terminado depois do corte, ele regravaria as duas
páginas. A causa foi medida, não deduzida: a releitura na limpeza, sem o
cancelamento, não duplica; o cancelamento incondicional, sim (guarda 7.17). Lido no
`@tanstack/query-core` **5.101.4** instalado (tarefa 1.3):

- `infiniteQueryBehavior.js:70-78`: o refetch relê `oldPages.length` páginas, a
  primeira com `oldPageParams[0]` e as seguintes por `getNextPageParam` sobre o
  resultado novo. **Aparada para uma página, a releitura é de uma requisição, sem
  cursor**; sem aparar, são tantas quantas estiverem carregadas (a premissa da D6,
  confirmada).
- `queryClient.js:126-139`: `resetQueries` volta a consulta ao estado inicial
  (sem `data`) antes de reler. **Corrigido aqui:** a primeira redação admitia
  `resetQueries` para o fim do ciclo, mas ele faria a lista inteira sumir no
  estado de carregamento, e o `Atualizar` precisa que a tela não volte a ele. Por
  isso o mecanismo é um só, o de aparar.
- `query.js:97-101` e `queryObserver.js:457-459`: `refetchQueries` com
  `type: 'active'` só relê consulta com observador **habilitado**, e um observador
  que passa de desabilitado a habilitado busca quando o dado está velho (o padrão
  `staleTime: 0`). Daí a entrada na aba: a página chama `trimDocumentEvents()` quando
  a aba `Histórico` **deixa** de ser a ativa (efeito sobre `activeTab`). Ela só
  apara, sem reler (medido: sair da aba não faz requisição); a reentrada habilita
  o observador, e a busca é de uma página.
- `infiniteQueryObserver.js:40-57`: `isFetchNextPageError`, `isFetchingNextPage`,
  `isRefetching` e `isRefetchError` existem e se excluem por direção; a falha da
  página seguinte e a do `Atualizar` mantêm `data` (`queryObserver.js:331-334`:
  `isLoadingError` só sem dados).

**Volume.** A #109 não existe, e a base do piloto acumula. O que cresce é o
número de páginas que o operador **pede**, não o que a aba carrega sozinha: cada
"Carregar mais" acrescenta no máximo 50 eventos, e o DOM cresce na mesma medida,
por ação do operador. Não há virtualização nem teto de páginas nesta change:
nenhum dos dois tem cenário medido, e não há medida de a partir de quantas linhas
o painel fica lento (convenção 22: não cito número que não medi). O que existe é
o gatilho para rever: se a conferência visual (D12) ou o uso mostrar lentidão
num histórico carregado à mão, a saída é filtro por documento ou por período, que
é rota nova no `apps/api` e issue própria, e não um teto no cliente.

- *Descartado, `page`/`offset` no cliente:* a rota não oferece, e a D7 da #98
  explica por que não deveria.
- *Descartado, reler todas as páginas carregadas:* ver acima.
- *Descartado, `Atualizar` como `refetch()` da consulta inteira:* relê todas as
  páginas carregadas; é o custo da releitura que esta D6 recusa, posto atrás de um
  botão.

### D7. Datas no fuso do navegador, com ano, sem "Hoje" e "Ontem"

- **Fuso:** o do navegador, como todas as datas desta feature
  (`formatSyncInstant`, `syncState.ts:31-41`; `KnowledgeDocumentsCard.tsx:30`). O
  fuso do sistema (`TZ` do `apps/api`, capability `api-system-timezone`) serve a
  agregações feitas **no servidor**, que devolvem o próprio `timeZone`; aqui o
  servidor devolve instantes e não agrupa. Agrupar no fuso do sistema e mostrar o
  horário de "Última sincronização" do card de origem no do navegador poria duas
  regras de fuso na mesma tela.
- **Agrupamento:** função pura `groupEventsByDay(events, timeZone?)`, que monta a
  chave do dia com `Intl.DateTimeFormat` e `timeZone` explícito (o mesmo recurso
  de `measuredDays.ts:54-60`, sem importar da feature `insights`: um consumidor
  aqui não justifica módulo comum, convenção 2). Sem `timeZone`, o do navegador.
  O parâmetro existe para o teste fixar o fuso e atravessar a meia-noite.
- **Formato:** cabeçalho `dd/mm/aaaa` (`pt-BR`, `dateStyle: 'short'`), com ano,
  pelo mesmo motivo da C3 da #107; horário `HH:mm` (`timeStyle: 'short'`).
- **Sem `Hoje` e `Ontem`** (correção C3): o rótulo relativo envelhece com a aba
  aberta atravessando a meia-noite, sem ninguém tocar na tela (convenção 13, a
  afirmação que outra hora torna falsa).
- **Eventos de documentos que já saíram da base:** aparecem como qualquer outro,
  com o título gravado no evento, **sem link** (não há página de documento; a
  aba `Documentos` é uma tabela) e **sem cruzar com a listagem** para marcar
  "documento inexistente". O próprio `Deleted` é o registro de que ele saiu, e o
  cruzamento traria uma segunda requisição com o terceiro estado da convenção 13
  (listagem que não respondeu) para dizer o que o histórico já diz.

### D8. Acessibilidade

- **Barra:** `Tabs` do Mantine, como as outras abas: `role="tab"`,
  `aria-selected`, `aria-controls`, foco itinerante e setas. A ativação pela seta
  depende do padrão do Mantine (`activateTabWithKeyboard`), conferido no pacote
  instalado (tarefa 1.3). Teste: seta para a direita a partir de `Documentos`
  seleciona `Histórico` e muda o endereço.
- **Lista:** cada dia é um `<section>` com título (`<h3>`, com o estilo de rótulo
  do protótipo) e `aria-labelledby`; os eventos do dia são uma `<ul>` de `<li>`. A
  ordem de leitura de cada item é horário, tipo, título, detalhe, autor.
- **Horário:** `<time dateTime={occurredAt}>`, com o instante completo para
  máquina e `HH:mm` para os olhos; o dia já está no título do grupo.
- **Cor:** o tipo está em texto no `Badge`; o ícone é `aria-hidden`.
- **"Carregar mais":** `Button` com `loading` enquanto a página seguinte carrega,
  o que também impede o segundo acionamento. Ao chegar a página, o foco vai para o
  primeiro evento acrescentado (`<li tabIndex={-1}>`). Sem isso, na última página
  o botão some e o foco cai no `body`; e mesmo com o botão presente, quem usa
  leitor de tela continua lendo de onde a lista cresceu.
- **Falhas:** `Alert` (`role="alert"`), com `Tentar novamente` como botão.

### D9. Base manual e base sincronizada

| | `Manual` | `Synced` |
|---|---|---|
| rota, componente, rótulos | os mesmos | os mesmos |
| autores esperados | `Operador` | `Sincronização` (o `apps/api` recusa escrita de documento do operador em base `Synced`) |
| nota do começo do histórico | sim | sim |
| nota de ignorados e falhas de acesso | não | sim |
| releitura ao fim do "Sincronizar agora" | não se aplica | sim (D6) |
| dependência do `apps/connectors` | **nenhuma** | só o "Sincronizar agora", que já existe e não é desta aba |

Os autores esperados não viram regra de tela: cada evento mostra o seu autor, e
uma base que mudou de modo (não existe hoje) mostraria os dois.

**Textos das notas:**

- as duas bases: "O histórico é registrado a partir da implantação desta
  funcionalidade. Documentos incluídos antes disso não têm evento de inclusão."
  Sem data, que o painel não conhece (D10 da #98 fixou o começo, não um dia).
- só `Synced`: "Arquivos ignorados e falhas de acesso não entram no histórico:
  eles aparecem no estado atual da base." (prancha 4c, verbatim).

**Aceite da issue** ("funciona para base manual sem nenhuma dependência da
sincronização"): dois testes no molde dos aceites da #107, com o `fetch`
interceptado, um sem `VITE_CONNECTORS_BASE_URL` e outro com ela, afirmando que a
lista aparece e que **nenhuma** chamada saiu para o `apps/connectors`, depois de
conferir que as do `apps/api` saíram (a ausência não passa por vacuidade).

### D10. Correções de protótipo (convenção 17)

Para a lista viva do `02`.

| # | prancha | o protótipo | o que prevalece | por quê |
|---|---|---|---|---|
| C1 | 3b, 4c | "Título alterado: antes “Prazos de envio”" | `Título alterado` | a rota não devolve o título anterior; deduzi-lo do evento anterior falha quando ele está numa página não carregada ou é anterior à implantação |
| C2 | 4c | `Excluído` com "Removido da pasta" | `Excluído`, sem detalhe | a rota não diz por que a sincronização excluiu (arquivo apagado, movido, ignorado); afirmar "da pasta" é mais do que o sistema sabe (convenção 13) |
| C3 | 3b, 4c | "Hoje — 26/09/2026", "Ontem — 25/09/2026" | `26/09/2026` | D7 |
| C4 | 3b | "Conteúdo substituído" | `Conteúdo alterado` | a palavra da issue, e a mesma nas duas bases |
| C5 | 3b, 4c | badges com cor fixa (`#e3f6ef`, `#e8eef8`, `#f1f0ee`) | `Badge` `light` do Mantine | convenção 16: a paleta da prancha só existe no claro |
| C6 | 4c | só a nota de ignorados em base sincronizada | as duas notas | a de começo do histórico vale para qualquer base |
| C7 | 3b, 4c | só a lista e "Carregar mais" | os estados da D4 | carregando, falha, vazio, fim e falha da página seguinte existem; a prancha não os desenha |
| C8 | 3b, 4c | ícone (+, ↻, −) dentro de cada badge de tipo | badge só com o texto | o painel não tem biblioteca de ícones nem badge com ícone; o tipo já está no texto, e o ícone seria só decorativo (D8) |
| C9 | 3b, 4c | faixa do dia com fundo (`#f1f0ee`), mais clara que a do cabeçalho | rótulo do dia sem fundo, separado por borda | conferência R1-2: com o tom do cabeçalho (`--buteco-surface-subtle`), o primeiro dia grudava na faixa do card; nenhuma variável do tema fica entre os dois tons nos dois esquemas (`--mantine-color-default-hover` é gray-0 no claro, que some contra o branco), e criar uma só para este uso seria variável sem segundo consumidor (convenções 2 e 16) |
| C10 | 3b, 4c | "Mais recentes primeiro" em cinza (`.k`) | cor padrão do texto | conferência R2-1: o `dimmed` sobre a faixa do cabeçalho mede 4,14:1 no claro |

### D11. Onde os testes moram, e os testes existentes que mudam

Todos em `apps/frontend` (Vitest + Testing Library). Nada no `apps/api`.

**Testes novos:**

| arquivo | o que cobre |
|---|---|
| `utils/documentHistory.test.ts` (novo) | rótulos de tipo, detalhe e autor, com os desconhecidos; `groupEventsByDay` em `America/Sao_Paulo` e `UTC` atravessando a meia-noite; dia partido entre páginas; formatos de dia e horário |
| `components/KnowledgeDocumentHistoryTab.test.tsx` (novo) | os estados da D4 com as negativas, `Atualizar` (`onRefresh`, `loading`, onde aparece e onde não) e a falha dele; lista, grupos, `<h3>`/`<ul>`/`<li>`, `<time dateTime>`; "Carregar mais" (`onLoadMore`), `loading`, fim da lista, falha da página seguinte; foco no primeiro evento novo; as duas notas por modo |
| `api/knowledgeDocumentsApi.test.ts` (casos novos) | caminho sem `cursor` e com `cursor` (codificado); o corpo lido de um texto JSON com as chaves e os valores de `type` que `KnowledgeDocumentCatalogTests.cs:653-674` afirma no fio (convenção 12) |
| `api/useKnowledgeDocuments.test.ts` (casos novos) | a consulta infinita: primeira página sem cursor, `hasNextPage` falso com `nextCursor` nulo, segunda página com o cursor da primeira |
| `pages/KnowledgeBaseDetailPage.history.test.tsx` (novo) | aba no endereço, consulta só com a aba ativa, uma requisição ao entrar também em `<StrictMode>` (R1-1), recomeço ao voltar, `Atualizar` com duas páginas, desativar e `409` da exclusão sem requisição de eventos, teclado, ausência de contador, e o cenário da D5 (card de origem limpo com `Excluído` no painel) |
| `pages/KnowledgeBaseDetailPage.sync.test.tsx` (casos novos) | aceites de base manual com e sem `VITE_CONNECTORS_BASE_URL`; releitura ao fim do "Sincronizar agora" com a aba aberta |

**Testes existentes que mudam** (lista fechada, conferida por `grep` de `tab`,
`tablist`, `tabpanel`, `diagnostico` e `Documentos` em todos os `*.test.ts*` de
`apps/frontend/src`, e dos `vi.mock`/`installFetch` dos três arquivos de página):

| arquivo | teste ou trecho | o que muda | por quê |
|---|---|---|---|
| `pages/KnowledgeBaseDetailPage.test.tsx` | `vi.mock('../api/knowledgeDocumentsApi')` e o `beforeEach` | `listKnowledgeDocumentEvents: vi.fn()` no mock, com `mockReset` e uma página vazia por padrão | o `it.each` abaixo passa a abrir a aba `Histórico` |
| `pages/KnowledgeBaseDetailPage.test.tsx` | `apresenta as duas abas, com Documentos ativa por padrão` | vira `apresenta as três abas, nesta ordem, com Documentos ativa por padrão`: `toHaveLength(2)` → `3`, e a ordem dos nomes afirmada | o requisito passa de duas para três abas; é a única asserção existente que muda de valor |
| `pages/KnowledgeBaseDetailPage.test.tsx` | `it.each` de `com a aba %s ativa, os dois cards ficam antes da barra e fora do painel` | ganha a linha `['Histórico', '?tab=historico']` | cenário novo do requisito modificado |
| `pages/KnowledgeBaseDetailPage.test.tsx` | `a aba inativa não está montada` | ganha a negativa do conteúdo do histórico e de `listKnowledgeDocumentEvents` não chamada | cenário modificado |
| `pages/KnowledgeBaseDetailPage.test.tsx` | comentário de `describe('barra de abas')` ("as outras 22 continuam valendo") | reescrito para dizer o que mudou nesta change | convenção 13 aplicada a comentário |
| `pages/KnowledgeBaseDetailPage.sync.test.tsx` | `installFetch` e `renderPage` | `installFetch` ganha a rota `/knowledge-bases/{ID}/document-events` (opção `events`); `renderPage` ganha o parâmetro `search` | sem a rota o interceptor responde `599`; sem `search` não há como abrir a aba |
| `utils/syncStateMessages.test.ts` | comentário da constante `FORBIDDEN` (linhas 27-30) | ganha a frase do alcance (D5) | só o comentário; nenhuma asserção muda |

**Conferidos e que NÃO mudam:** `KnowledgeBaseDetailPage.delete.test.tsx` (não
abre aba e não tem rota a interceptar; o `vi.mock` de documentos mantém o
`listKnowledgeDocumentEvents` real, que não é chamado sem a aba ativa),
`KnowledgeBaseSyncOriginCard.test.tsx`, `KnowledgeIndexDiagnosticsTab.test.tsx`,
`KnowledgeDocumentsCard.test.tsx`, `app/router.test.tsx` (afirma abas do agente,
não da base). Se a implementação achar outro teste que precise mudar, ela **para**
e o acrescenta aqui antes de mudá-lo (convenção 9).

### D12. Conferência visual (convenção 14), no molde da #136

- **Painel:** `npm run dev` deste worktree numa porta livre da faixa
  **59300–59399**, conferida com `lsof -i :<porta>` antes de subir, com
  `VITE_API_BASE_URL` apontando para outra porta da faixa **sem processo**: as
  respostas do `apps/api` são montadas pelo CDP (`Fetch.fulfillRequest`, com CORS
  e o preflight). Portas registradas no `tasks.md` e encerradas ao fim; nenhum
  processo de outra sessão é tocado.
- **Chrome headless por CDP**, claro e escuro, a 1440 px e a ~1860 px.
- **Estados:** base manual com eventos de três dias e "Carregar mais"; base
  sincronizada com as duas notas; os três tipos e os três detalhes; autor
  desconhecido e tipo desconhecido; carregando; falha; vazio; fim da lista; falha
  da página seguinte; o foco depois de "Carregar mais"; e, como régua, as pranchas
  3b e 4c; e o `Atualizar` nos estados vazio, lista e fim, durante a releitura
  (`loading`) e com a falha da releitura.
- **Medido:** alinhamento das colunas (horário, tipo, título, autor), o contraste
  de cada texto e de cada `Badge` nos dois esquemas, a altura da barra (40 px com
  três abas). Iterar até uma rodada sem achado; capturas em `design/capturas/`,
  `<estado>_<esquema>_<largura>.png`.
- **A validação manual pelo mantenedor fica pendente**, e o archive espera por ela.

**Resultado (14/10/2026): três rodadas, a terceira sem achado novo.** 11 estados
× 2 esquemas × 2 larguras = 44 capturas por rodada; as da rodada 3 estão em
`design/capturas/`, com `medidas-r2.json` e `medidas-r3.json`. Portas: Vite em
**59310**, `VITE_API_BASE_URL` em **59311** (sem processo), Chrome headless com CDP
em **59312**; os três encerrados ao fim, faixa conferida vazia. Nenhuma requisição
ficou sem resposta montada. A régua das pranchas 3b e 4c foi o HTML lido (colunas
de 60/128/1fr/150 px), não uma captura delas.

Medido igual nas quatro combinações: barra de abas com **40 px** com as três abas;
colunas do evento em x = 257, 325, 457 e (1287 a 1440 px | 1707 a 1860 px), larguras
56/120/1fr/120, sem rolagem horizontal; foco depois de "Carregar mais" no `<li>` do
primeiro evento novo, com o anel visível. Contraste (rodada 3): títulos 21:1;
horário, detalhe, autor e título do dia 5,21:1 no claro; badges a partir de 4,81:1
(`Incluído`, claro); menor texto novo desta change 4,6:1 (escuro).

| # | o quê | medido | correção |
|---|---|---|---|
| R1-1 | requisições ao entrar na aba | **2** por entrada no painel em dev (StrictMode), e não 1 | `trimDocumentEvents` só cancela "Carregar mais" em voo; teste novo em StrictMode e guarda 7.17. Rodada 2: 1 por entrada |
| R1-2 | faixa do dia | mesmo tom da faixa do cabeçalho; o primeiro dia grudava nela | sem fundo, separado por borda (C9) |
| R2-1 | "Mais recentes primeiro" | **4,14:1** no claro (`dimmed` sobre gray-3) | cor padrão (C10); rodada 3 sem item desta change abaixo de 4,5:1 |

Medido e não corrigido, por ser o padrão da casa e não desta change:

- o rótulo da faixa de cabeçalho de card (`SectionLabel`, `dimmed` sobre
  `--buteco-surface-subtle`) mede **4,14:1** no claro, e é o mesmo de
  "Descrição — texto lido pelo modelo" e de todo card do painel;
- o `Tentar novamente` vermelho `outline` dentro do `Alert` de falha mede
  **4,28:1** no claro, a família do vermelho `outline` já registrada na #155;
- as falhas passam pelas **3 novas tentativas** do `QueryClient` padrão do painel
  (`app/queryClient.ts`, sem `defaultOptions`): o estado de falha aparece depois
  de ~7 s de carregamento, como em toda consulta do painel.

## Testes de aceite

| teste | o que afirma, com a asserção negativa |
|---|---|
| três abas | `Documentos`, `Histórico`, `Diagnóstico do índice`, nessa ordem; `Documentos` selecionada |
| endereço | clicar em `Histórico` leva a `?tab=historico`; abrir `?tab=historico` seleciona a aba |
| consulta só com a aba | na aba `Documentos`, `listKnowledgeDocumentEvents` **não** é chamada |
| teclado | seta para a direita em `Documentos` seleciona `Histórico` |
| sem contador | a aba `Histórico` tem `textContent` igual a `Histórico` com eventos carregados |
| tipos, detalhes, autores | os rótulos da D2 e da D3; tipo e autor desconhecidos **não** exibem rótulo conhecido |
| dia por fuso | `2026-09-26T02:30:00Z` cai em `25/09/2026 23:30` em `America/Sao_Paulo` e em `26/09/2026 02:30` em `UTC` |
| sem relativo | nenhum cabeçalho contém `Hoje` nem `Ontem` |
| carregando / falha / vazio | cada um com o seu texto, e **sem** o texto dos outros dois |
| carregar mais | a segunda requisição leva `cursor=<nextCursor>`; o dia partido fica num grupo só |
| fim da lista | **sem** `Carregar mais`, com "Não há eventos mais antigos." |
| falha da página seguinte | os eventos da primeira página continuam; **sem** "Não há eventos mais antigos."; `Tentar novamente` repete o cursor |
| foco | depois de carregar a última página, o foco está no primeiro evento novo |
| recomeço | voltar à aba chama a rota sem `cursor` e mostra só a primeira página |
| atualizar | com duas páginas carregadas, `Atualizar` chama a rota sem `cursor`, uma vez, e mostra só a primeira página; **não** aparece no carregamento nem na falha da primeira página |
| falha do atualizar | a lista continua, com "Não foi possível atualizar o histórico."; **sem** o texto de falha da primeira página |
| chave fora do prefixo | com duas páginas carregadas, desativar a base pelo cabeçalho e o `409` da exclusão **não** fazem requisição à rota de eventos |
| sincronização | o fim do "Sincronizar agora" com a aba aberta chama a rota sem `cursor` |
| base manual (aceite da issue) | com e sem `VITE_CONNECTORS_BASE_URL`: lista exibida e **nenhuma** chamada ao `apps/connectors` |
| notas | a de ignorados aparece em `Synced` e **não** em `Manual` |
| D5 | painel com `Excluído` e card de origem **sem** `exclu`, `remov`, `apag` |
| guardas (convenção 15) | os da tarefa 7, cada um com o resultado no `tasks.md` |

## Árvore de pastas proposta

```
apps/frontend/src/features/knowledge-bases/
  api/
    knowledgeDocumentsApi.ts                     (listKnowledgeDocumentEvents)
    knowledgeDocumentsApi.test.ts                (casos novos)
    useKnowledgeDocuments.ts                     (documentEventsQueryKey = ['knowledge-document-events', id], useKnowledgeDocumentEventsQuery)
    useKnowledgeDocuments.test.ts                (casos novos)
  types/
    knowledgeDocumentEvent.ts                    (novo — espelho de KnowledgeDocumentEvent*Response)
  utils/
    documentHistory.ts                           (novo — rótulos, agrupamento, formatos; D2, D3, D7)
    documentHistory.test.ts                      (novo)
    syncStateMessages.test.ts                    (só o comentário de FORBIDDEN — D5)
  components/
    KnowledgeDocumentHistoryTab.tsx              (novo — apresentacional, convenção 7)
    KnowledgeDocumentHistoryTab.test.tsx         (novo)
  pages/
    KnowledgeBaseDetailPage.tsx                  (terceira aba, consulta, recomeço, reset no fim do ciclo)
    KnowledgeBaseDetailPage.test.tsx             (os trechos da D11)
    KnowledgeBaseDetailPage.history.test.tsx     (novo)
    KnowledgeBaseDetailPage.sync.test.tsx        (installFetch, renderPage e casos novos — D11)

openspec/changes/frontend-aba-historico/design/capturas/   (D12)

02-HISTORICO_E_STATUS.md   (entrada da change e C1 a C7 em "Correções de protótipo")
CHANGELOG.md               (Added)
```

Nada em `libs/`. Nada em `apps/api`, `apps/workers`, `apps/connectors` nem
`apps/inbox`.

O tipo do evento ganha arquivo próprio, e não entra em `knowledgeDocument.ts`,
porque descreve outra resposta, com outra vida (sobrevive ao documento). A rota e
a consulta ficam nos módulos de documento, porque a rota é aninhada na base como
as de documento e o cliente `request<T>` é o mesmo (convenção 7).

## Risks / Trade-offs

- **[Refetch de consulta infinita relê todas as páginas]** → D6: o histórico
  recomeça da primeira página ao entrar e ao fim do ciclo, e não relê no foco.
  Verificável: o teste de recomeço afirma a chamada sem cursor e só a primeira
  página exibida; a tarefa 1.3 confere o comportamento no pacote.
- **[Evento gravado com a aba aberta não aparece]** → `Atualizar` e a reentrada
  (D6), escritos no requisito; o histórico não é processo em andamento.
  Verificável: o teste "sem consulta em intervalo" afirma a ausência, e os de
  `Atualizar` e de sincronização afirmam a releitura onde ela existe.
- **[Invalidação de base reler todas as páginas]** → chave fora do prefixo
  `['knowledge-bases']` (D6, com o levantamento). Verificável: o teste de
  desativar com a aba aberta, e a guarda 7.15.
- **[Dia errado por fuso]** → `groupEventsByDay` com fuso explícito e teste que
  atravessa a meia-noite nos dois fusos. Verificável: o teste da D7.
- **[Autor ou tipo novo exibido como um conhecido]** → rótulo neutro com o valor,
  e asserção negativa. Verificável: os testes de desconhecido, com o guarda da
  tarefa 7.
- **[A regra das palavras proibidas afrouxar por causa do histórico]** → D5:
  varreduras sobre fontes e card, rótulos fora das fontes, teste da coexistência e
  guarda reverificado. Verificável: tarefa 7.
- **[Falha da página seguinte apagar a lista]** → o estado de erro da página
  seguinte é separado do da primeira (D4). Verificável: o teste da falha da página
  seguinte afirma os eventos ainda exibidos.
- **[Testes existentes que mudam além da lista]** → lista fechada na D11, e a
  implementação para se achar outro. Verificável: a tarefa 1.2 roda a suíte
  inteira antes de tocar código, e a tarefa 8 a roda de novo comparando por nome
  de teste.
- **[Contrato do fio divergir do tipo do frontend]** → o teste do cliente lê um
  texto JSON com as chaves e os valores que o teste de fio do `apps/api` afirma
  (convenção 12). Não é o artefato real produzido pelo `apps/api` (convenção 11):
  o painel não sobe o `apps/api` nos testes. A conferência visual (D12) e a
  validação do mantenedor contra o `apps/api` local são o lado real.

## Migration Plan

Sem migração e sem mudança no `apps/api`. Deploy só do `apps/frontend`; a rota
está em produção desde a #98. Rollback: reverter o código do painel.

## Open Questions

Nenhuma de negócio. As decisões de produto (D2, D4, D5, D7 e as correções da D10)
passam pela revisão do mantenedor antes do `/opsx:apply`.
