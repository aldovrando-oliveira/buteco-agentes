## Context

Etapa **5a-2** da linha de bases de conhecimento — a segunda metade da 5a,
dividida por tamanho durante a reprojeção da etapa 1. Só `apps/frontend`.

A 5a-1 (`frontend-knowledge-base-catalogo`, arquivada em 09/09/2026) entregou o
catálogo, o detalhe, a criação, a edição e o card de agentes que consultam a
base, e deixou no lugar da área de documentos uma nota de sequenciamento. A `2a`
(`knowledge-base-indexacao`) e a `2b` (`knowledge-base-indexacao-operacao`), as
duas de 11/09/2026, entregaram tudo o que faltava do lado do servidor.

### Protótipo: revisão validada e o que foi percorrido

**Anexado em `design/`**: `Buteco Agentes.dc.html` (1.874 linhas), `support.js` e
`CONHECIMENTO.md`. Os três lidos pela conexão com o Claude Design, do projeto
*Sistema Gestão de Agentes*
(`e4f9bbd6-dd31-4ff1-954b-0e686d2eaa54`), e conferidos contra as cópias
anexadas pela 5a-1: **mesmo tamanho em bytes nos três**, e o `.dc.html` confere
linha a linha na faixa 1610-1615 com o mesmo `total_lines` (1874). O protótipo
**não mudou** desde a 5a-1.

**Revisão validada: `CONHECIMENTO.md` revisão 2, de 09/09/2026.** É a mesma que a
5a-1 validou, e continua sendo a especificação das telas das quatro etapas de UI.

`AJUSTES_VISUAIS.md` e `PROMPT_CLAUDE_CODE.md` **não foram reanexados**. Foram
removidos na 5a-1 com o motivo em D15 daquela change: um manda tratar o protótipo
como fonte da identidade visual, falso desde `frontend-tema-identidade-visual`, e
o outro especifica quatro coisas contra decisões já fechadas — entre elas o
`multipart/form-data` que a etapa 1 recusou em D3. Reanexá-los seria reintroduzir
autorização para agir contra decisão fechada.

**A tela navegável foi percorrida, não lida.** Chrome 152 headless dirigido por
CDP, clique real, captura por estado, nos dois esquemas de cor, a 1860px de
largura — a largura em que o operador usa o painel, e não os 1440 que esconderam
o defeito de largura da 5b.

Percorrido, na ordem:

| # | percurso | onde |
|---|---|---|
| 1 | catálogo de bases, 9 bases, 5 colunas, 4 filtros | `/knowledge-bases` |
| 2 | **`Pendente` + `Indexando` + `Indexado` na mesma tabela** | detalhe de *Catálogo de Produtos*, nos primeiros ~600 ms |
| 3 | os mesmos três documentos já terminais | mesma tela, 7 s depois |
| 4 | **`Indexado` ×3 + `Falhou`**, com a faixa de falha, o motivo completo e o botão **Reindexar documento** | detalhe de *Políticas de Cobrança* |
| 5 | modal de adicionar, modo **Subir arquivos**, vazio | *Políticas de Cobrança* |
| 6 | modal de adicionar, modo **Escrever manualmente**, com o select de tipo de origem | idem |
| 7 | modal de adicionar com **3 arquivos reais injetados** (2 aceitos, 1 `.pdf` recusado), títulos pré-preenchidos, rodapé de contagem | idem |
| 8 | modal de **atualizar**, modo manual, conteúdo carregado, aviso e botão | documento *Histórico de campanhas 2025* |
| 9 | os itens 7 e 4 no **esquema escuro** | idem |

**A semente NÃO tem documento nos quatro estados numa mesma base, e dois dos
quatro são transitórios.** Isso é achado do percurso, e muda como se percorre:

- *Políticas de Cobrança* (`k1`) tem `Indexado` ×3 e `Falhou` ×1 — estáveis.
- *Catálogo de Produtos* (`k2`) nasce com `Indexado`, `Indexando` e `Pendente`,
  mas o protótipo chama `runIndex` na montagem para **todo** documento
  não-terminal, e eles avançam sozinhos (~2,4 s para `Indexando`, ~3,2 s para
  `Indexado`). Os dois estados só existem na tela nos primeiros ~5,6 segundos.
- Logo, **nenhuma captura estática do protótipo mostra os quatro juntos**, e uma
  conferência que abra a tela e olhe com calma vê três. Os dois transitórios
  foram capturados com uma navegação que corre direto para `k2` e fotografa em
  ~600 ms (percurso 2).

## Conferência protótipo × código (convenção 17), feita ANTES das decisões

### As três correções herdadas — confirmadas, e uma delas está incompleta

**C1. `0 fragmentos` num documento que falhou — confirmado, e a regra correta é
mais forte do que a registrada.** O protótipo faz
`chunks: st === 'indexed' ? … : st === 'failed' ? '0 fragmentos' : '—'`
(`.dc.html:1612`), e o defeito foi visto na tela no percurso 4.

A correção registrada pela 5a-1 diz "célula vazia": exibir quando `indexedAt` não
for nulo, **omitir** quando for. Isso está certo e é insuficiente. Lido no código
de `apps/workers`, `KnowledgeIndexingService.FailAsync` (`:206-216`) grava
**apenas** `IndexingStatus` e `FailureReason` — não toca `IndexedAt` nem
`FragmentCount` —, e a exclusão dos fragmentos antigos acontece **só dentro** de
`CommitAsync` (`:180-183`), depois de a atualização condicional ter afetado uma
linha. O comentário do método declara a garantia 3 de D9 da etapa 1:
*"o documento continua respondendo com o conteúdo anterior"*.

Consequência para a tela: um documento que **já esteve indexado** e falhou depois
tem `indexedAt` preenchido e `fragmentCount` na contagem anterior, e esses
fragmentos estão vivos no índice. A célula não pode ficar vazia — ela mostra a
contagem anterior. Só o documento que **nunca** foi indexado e falhou omite.
Mesma coisa para `Pending` e `Indexing` vindos de reindexação ou atualização:
`RequestReindex()` e `Update()` também preservam os dois campos.

**O separador é `indexedAt`, e nunca o estado.** É exatamente o que o comentário
de `KnowledgeDocument.FragmentCount` já declara como contrato desde a etapa 1.

**C2. A frase "documentos muito grandes tendem a bater no limite de requisições"
sai — confirmada, com a justificativa corrigida.** A frase está viva no percurso
4, dentro do `failureReason` da semente (`.dc.html:983`). Sai porque é conselho
que o sistema não verifica: nada em `apps/workers` correlaciona tamanho com falha
de embedding, e nenhuma decisão do pipeline usa tamanho.

**A evidência de apoio registrada pela 5a-1 está errada, e a correção do registro
faz parte desta change** (convenção 6: a afirmação em prosa sobre o que o código
faz é a que ninguém abre o arquivo para checar). O registro diz que *"a própria
semente do protótipo aplica essa cópia a um documento pequeno"*. Conferido:

- o documento que carrega a frase é `d4`, *Histórico de campanhas 2025*, com
  `chars: 196400` — **o maior valor de toda a semente**, e não um documento
  pequeno;
- `chars` **nunca chega à tela** na tabela de documentos (varrido: o campo só
  alimenta a simulação de contagem de fragmentos em `.dc.html:1522`), então a
  frase não fica ao lado de tamanho nenhum;
- o que a 5a-1 provavelmente leu é o campo `content` do mesmo `d4`, uma string de
  47 caracteres — que o modal de atualizar exibe como *"47 caracteres de texto"*
  (visto no percurso 8). A semente é internamente inconsistente entre `chars` e
  `content`; nenhuma das duas leituras sustenta "pequeno" como argumento.

A recusa **não depende** dessa evidência: a convenção 13 sozinha a sustenta.

**C3. O upload é leitura de texto no cliente + corpo JSON, não
`multipart/form-data` — confirmado, e o protótipo está do lado certo.** O
`CONHECIMENTO.md` diz em prosa *"No codebase: `multipart/form-data`"* (seção 3), e
o `PROMPT_CLAUDE_CODE.md` removido pedia rota `/documents/upload`, `FormData` e
modificar o `request<T>` de cada feature para omitir o `Content-Type`.

Mas o **código** do protótipo faz o contrário: `readFiles` (`.dc.html:1682-1699`)
usa `new FileReader()` + `readAsText`, valida a extensão no cliente e envia
string. É a D3 da etapa 1 — *"Zero multipart, com gatilho registrado"* — já
implementada. **Só a prosa diverge.** O gatilho para multipart nascer continua
sendo o primeiro tipo de origem binário (PDF), junto com o extrator que precisa
dos bytes.

### As duas correções novas, achadas ao percorrer

**C4. O aviso do modal de atualizar afirma duas coisas falsas.** Visto no
percurso 8, textualmente:

> *"Salvar reindexa o documento: ele volta para pendente e sai das consultas do
> agente até a indexação terminar. Os fragmentos antigos são descartados."*

E o botão primário diz **"Salvar e reindexar"**, sem condição.

Contra o código:

1. **"volta para pendente" é condicional, e o protótipo o afirma
   incondicionalmente.** `KnowledgeDocument.Update()` só volta a `Pending` quando
   o `ContentHash` do texto novo difere do gravado; atualização que só troca o
   título preserva o estado de indexação corrente, e o handler nem publica na
   fila. O protótipo permite editar o título no mesmo modal, então o caso é
   alcançável em um clique.
2. **"sai das consultas do agente até a indexação terminar" é falso.** Os
   fragmentos antigos continuam no índice até a transação de `CommitAsync` os
   substituir — garantia 3 de D9 da etapa 1, com o mecanismo em
   `KnowledgeIndexingService`. O documento **não** sai das consultas em momento
   algum; se a reindexação falhar, os antigos continuam respondendo.
3. **"os fragmentos antigos são descartados" antecipa um efeito que pode não
   ocorrer.** O descarte é atômico com a inserção dos novos, e só acontece no
   sucesso.

É convenção 13 na direção mais cara: a cópia descreve uma consequência pior do
que a real, e um operador que a leia adia uma correção de conteúdo por medo de
derrubar o agente.

**C5. A ordem da lista de arquivos do modal não é a da seleção.** No percurso 7
foram injetados, nessa ordem, `politica-de_reembolso.md`, `horarios internos.txt`
e `manual.pdf`. A lista renderizou **`manual.pdf` primeiro**. A causa está em
`readFiles`: a recusa por extensão é empurrada de forma síncrona e os arquivos
aceitos entram no callback `onload` do `FileReader`. O operador que escolhe seis
arquivos vê uma ordem que não é a dele. Não está na prosa do `CONHECIMENTO.md`.

### Conferência tela a tela: campo, rota, dado derivado, papel visual

| pergunta | resultado |
|---|---|
| Todo campo exibido existe na resposta? | **Sim, com uma exceção que não é exibida** — ver abaixo. `title`, `sourceType`, `indexingStatus`, `indexedAt`, `failureReason`, `fragmentCount` estão em `KnowledgeDocumentSummaryResponse`. |
| Toda ação tem rota? | **Sim.** `POST /`, `GET /`, `GET /{id}`, `PUT /{id}`, `DELETE /{id}` e `POST /{id}/reindex`, todas em `KnowledgeDocumentEndpoints`. |
| Há contagem/métrica/estado derivado que o sistema não coleta (convenção 13)? | **Sim, três** — progresso percentual, contagem zerada em documento nunca indexado, e correlação tamanho↔falha. Os três recusados (C1, C2, e D8 abaixo). |
| Há papel visual que troque de ponta da escala sem variável por esquema (convenção 16)? | **Não, e por construção** — ver D9. |

**Correção deste `design.md`, feita durante a revisão da proposta (convenção 9).**
A linha do diagnóstico acima dizia, na primeira redação, que *"`KnowledgeFragment`
não existe em `apps/api`"*. **É falso** — a entidade existe em
`KnowledgeFragments/Entities/KnowledgeFragment.cs`, com as três colunas de
proveniência. A conclusão não muda (nenhuma rota a expõe, e a 5c continua
precisando de backend), mas a evidência estava errada, e o mecanismo do erro é o
que esta change passou a conferência inteira nomeando: a varredura original só
cobriu `KnowledgeDocuments/` e `KnowledgeBases/`, e a que teria pego o resto
falhou em silêncio no shell — `grep --include=*.cs` sem aspas vira glob do `zsh`,
que devolve *"no matches found"* e **nenhuma linha**. Saída vazia foi lida como
"não existe". Registrado aqui, e não só corrigido, porque é a convenção 6 na
forma mais barata de repetir: **comando que falha em silêncio produz a mesma
saída que ausência real**, e a diferença entre as duas decide uma exclusão de
escopo.

**A exceção: `contentHash` NÃO está no fio.** O enunciado desta change listava
`contentHash` entre os campos que a 2a entregou na resposta de documento.
Conferido — e é o exemplo da convenção 6 sobre não deduzir nome de campo:

- `KnowledgeDocumentResponse` e `KnowledgeDocumentSummaryResponse` **não têm**
  `ContentHash`. A coluna existe na entidade e é usada só pelo `Update()`;
  nenhum response a projeta.
- `KnowledgeWireFormatTests` não menciona `contentHash` em asserção nenhuma.
- Os cinco que **estão** no fio, conferidos no teste que inspeciona o JSON bruto:
  `indexingStatus` (string `"Pending"`/`"Indexing"`/`"Indexed"`/`"Failed"`, nunca
  ordinal), `indexedAt`, `failureReason`, `fragmentCount`, `indexingAttempts`,
  `lastAttemptAt` — camelCase, sem sigla nem maiúscula consecutiva, então sem o
  risco `a2A`/`a2a` que `agente-enderecos-a2a` registrou.

Consequência de desenho, e é o que decide D5: **a tela não pode calcular se o
conteúdo mudou comparando hash** — ela compara o `extractedText` carregado com o
texto editado, como string.

### O corolário da convenção 1: "fora de escopo por dependência" exige conferir

A 5a-1 excluiu à toa o card de agentes por não ter conferido a dependência, e a
conferência manual corrigiu tarde. Aqui as três exclusões foram conferidas no
código **antes** de serem escritas:

| excluído | dependência | conferida como |
|---|---|---|
| Diagnóstico do índice (5c) | provedor, modelo, dimensão e total de fragmentos por base | **ausente como rota.** `KnowledgeFragments/` em `apps/api` tem **um arquivo**: a entidade, com `EmbeddingProvider`, `EmbeddingModel` e `EmbeddingDimensions` — e `apps/api` nunca escreve nela (a tabela nasce ali por razão de deploy, não de domínio). Não há query, handler, response nem endpoint que a projete. É backend a sequenciar, não tela a adiantar. |
| Abas no detalhe | uma segunda aba de verdade | **ausente** enquanto a 5c não existir. Decisão da 5a-1 (D3), mantida. |
| Colunas do catálogo + filtro `Com falha` | contagem por base numa requisição | **PRESENTE** — `GET /knowledge-bases/indexing-summary` existe e devolve `documentCount`/`indexedCount`/`failedCount` por base, uma linha por base do catálogo. Por isso a exclusão **não** é "por dependência": é decisão de sequenciamento, tomada em D1 com o motivo. |

## Goals / Non-Goals

**Goals:**

- Um operador carrega, atualiza, exclui e reindexa documentos de uma base pelo
  painel, sem `curl`.
- A tela mostra o estado de indexação de cada documento e **acompanha a
  transição sozinha**, sem recarregar.
- Quando a indexação falha, o operador lê o motivo **completo** na tela e tem um
  botão para tentar de novo.
- Nenhuma afirmação que o sistema não sustente, com as asserções **negativas**
  na spec (convenção 13/15).

**Non-Goals:**

- Diagnóstico do índice — etapa **5c**.
- Colunas `Documentos`/`Indexação` e filtro `Com falha` no catálogo — D1.
- Busca, filtro ou paginação na tabela de documentos. O protótipo não os tem, e
  o volume declarado é por base, não global.
- Pré-visualização renderizada do markdown. O `ExtractedText` vem inteiro no
  `GET /{id}`, mas o protótipo mostra `textarea` mono e é o que a edição precisa.
- Exibir `indexingAttempts` e `lastAttemptAt` como campos próprios — D10.
- Qualquer mudança em `apps/api` ou `apps/workers`.

## Árvore de pastas proposta

```
apps/frontend/src/
├── components/                      (sem alteração)
│   ├── data/SectionedCard.tsx           reusado
│   ├── data/SectionLabel.tsx            reusado
│   └── layout/DetailHeader.tsx          reusado
└── features/
    └── knowledge-bases/
        ├── api/
        │   ├── knowledgeBasesApi.ts             (sem alteração — exporta request<T> e ApiError)
        │   ├── useKnowledgeBases.ts             (sem alteração)
        │   ├── knowledgeDocumentsApi.ts         NOVO
        │   ├── knowledgeDocumentsApi.test.ts    NOVO
        │   ├── useKnowledgeDocuments.ts         NOVO
        │   └── useKnowledgeDocuments.test.ts    NOVO
        ├── components/
        │   ├── KnowledgeBaseAgentsCard.tsx      (sem alteração)
        │   ├── KnowledgeBaseDescriptionCard.tsx (sem alteração)
        │   ├── KnowledgeBaseDocumentsPlaceholder.tsx       REMOVIDO
        │   ├── KnowledgeBaseDocumentsPlaceholder.test.tsx  REMOVIDO
        │   ├── KnowledgeBaseForm.tsx            (sem alteração)
        │   ├── KnowledgeBaseTable.tsx           (sem alteração — ver D1)
        │   ├── KnowledgeDocumentsCard.tsx       NOVO
        │   ├── KnowledgeDocumentsCard.test.tsx  NOVO
        │   ├── KnowledgeDocumentModal.tsx       NOVO
        │   ├── KnowledgeDocumentModal.test.tsx  NOVO
        │   ├── DocumentFileList.tsx             NOVO
        │   └── DocumentFileList.test.tsx        NOVO
        ├── pages/
        │   ├── KnowledgeBaseDetailPage.tsx      MODIFICADO
        │   ├── KnowledgeBaseDetailPage.test.tsx MODIFICADO
        │   └── (demais páginas sem alteração)
        ├── types/
        │   ├── knowledgeBase.ts                 (sem alteração)
        │   └── knowledgeDocument.ts             NOVO
        └── utils/
            ├── agentUsage.ts                    (sem alteração — reusado na exclusão)
            ├── documentIndexing.ts              NOVO
            ├── documentIndexing.test.ts         NOVO
            ├── documentUpload.ts                NOVO
            └── documentUpload.test.ts           NOVO
```

**Nada em `libs/`.** Esta change é inteiramente de `apps/frontend`, e `libs/`
deste monorepo é para código .NET compartilhado entre `apps/api` e
`apps/workers`. Não há segundo consumidor para nada escrito aqui (convenção 2).

**Nenhuma dependência nova.** Ver D6 — não há versão de runtime, framework ou
biblioteca a fixar nesta change.

## Decisions

### D1 — O resumo por base **não** entra nesta change; vira change própria, com gatilho imediato

A 2b entregou `GET /knowledge-bases/indexing-summary`, que destrava de uma vez as
duas colunas (`Documentos`, `Indexação`) e a quarta opção de filtro (`Com falha`)
que a 5a-1 recusou. A dependência foi **conferida presente** no código, não
julgada.

O argumento que matou as colunas na 5a-1 morreu junto com a rota: lá seria uma
requisição **por base**, contra 100+ bases; agora é **uma** requisição para o
catálogo inteiro, e a docstring de `KnowledgeBaseIndexingSummaryResponse` diz
que o recurso nasceu exatamente para isso.

Mesmo assim fica fora, por três motivos:

1. **É outra tela.** `KnowledgeBaseListPage`/`KnowledgeBaseTable` (o catálogo),
   não `KnowledgeBaseDetailPage` (o detalhe). Nenhum arquivo desta change a
   toca.
2. **Custo real é a conferência manual, não o código.** O código são ~6 arquivos
   em pares; a convenção 14 cobra uma rodada iterativa de conferência **por
   tela**, nos dois esquemas, e a 5a-1 registrou o custo de cada uma das quatro
   que precisou. Juntar duas telas numa change dobra essa parte.
3. **Esta change já é a metade grande de uma change que precisou ser dividida
   por tamanho.** Inflá-la contraria a razão da divisão.

**E o adiamento sai com POSIÇÃO NA FILA, não com gatilho.** *"Gatilho imediato"*
já foi usado nesta jornada e não disparou: o carve de ordenação ficou com
`GATILHO ATUAL: imediato — change própria, a ser proposta`, e só voltou à mesa
porque alguém perguntou. A 5a-3 tem exatamente o mesmo perfil — pequena, com a
dependência pronta, e sem nada que a puxe. **Gatilho sem posição é adiamento
indefinido com outro nome**, e é o terceiro registro desta família nesta base
(depois do gatilho que apontava para uma tela que ninguém planejava, e do limiar
de carga citado depois que o sistema mudou).

A fila da linha de bases de conhecimento, com o que resta e por que nesta ordem:

| # | change | estado da dependência |
|---|---|---|
| 1 | **5a-2 — esta** | pronta |
| 2 | **Etapa 4 — resolvedor de tool de conhecimento** (`apps/workers`) | pronta. **É o próximo passo depois desta**, e não por tamanho: até ela existir, **nada do que o operador carrega nesta tela chega a um agente**. A base, a descrição, o vínculo, o índice e agora o conteúdo existem, e nenhum agente consulta nada. |
| 3 | **5a-3 — colunas e filtro do catálogo** (`apps/frontend`) | pronta e **ociosa** desde a 2b |
| 4 | backend do diagnóstico do índice (`apps/api`) | **não proposta** |
| 5 | **5c — UI do diagnóstico do índice** | bloqueada por #4 |

**A 5a-3 vem antes da 5c**, e a razão é verificável e não é preferência: a 5c
está bloqueada por um passo de backend que **nem foi proposto** (ver a tabela de
exclusões acima — nenhuma rota de `apps/api` projeta a proveniência do índice),
enquanto a dependência da 5a-3 está no ar desde a 2b e não tem consumidor. Pôr a
5a-3 atrás da 5c seria pô-la atrás de duas changes, uma das quais ainda não
existe.

Registro no `02-HISTORICO_E_STATUS.md`: item aberto **com a tabela acima**, e a
mesma tabela no "Próximo passo". O escopo da 5a-3 já está escrito para não
precisar ser reconstruído: duas colunas em `KnowledgeBaseTable`, a quarta opção
de filtro em `KnowledgeBaseListPage`, um `useKnowledgeBaseIndexingSummaryQuery`, e
o requisito `MODIFIED` na spec viva de `knowledge-base-catalog-ui` que hoje
proíbe as colunas.

**Alternativa considerada e recusada: entrar aqui.** Ganharia a economia de uma
rodada de contexto e fecharia a lacuna de uma vez. Perde no ponto 2, que é o
custo que esta base já mediu e o único que não encolhe.

### D2 — Falha parcial no lote: N chamadas independentes, estado por linha, modal que não fecha

A etapa 1 decidiu **N chamadas independentes** ao `POST` unitário, com estado por
item — não rota de lote. O rodapé do protótipo diz *"2 documentos serão criados e
entram como pendentes"* (visto no percurso 7), que promete um resultado de
conjunto que o desenho não sustenta: se a segunda chamada falhar, a primeira
**já criou** um documento. A 5a-1 registrou isso como divergência a corrigir na
tela; é aqui que ela se corrige.

A decisão, em quatro partes:

1. **As N chamadas são sequenciais**, na ordem da lista. Não é sobre carga (N é
   a escolha de um operador, não um lote de sistema): é para que o estado
   parcial seja legível — "as três primeiras entraram, a quarta falhou, a quinta
   não foi tentada" é uma frase verdadeira; com paralelismo, o conjunto que
   entrou é arbitrário.
2. **Cada linha carrega o próprio estado**: `aguardando` → `enviando` →
   `criado` | `falhou (motivo)`. A linha `criado` **deixa de ser enviável** — um
   segundo clique em "Tentar de novo" reenvia só as que faltam, e nunca duplica
   o que já entrou.
3. **O modal não fecha em falha parcial.** Fechar apagaria a única cópia do que
   falhou e por quê. Fecha quando **todas** entraram.
4. **O rodapé não promete conjunto.** Some a frase *"N documentos serão criados e
   entram como pendentes"*; entra uma que diz o desenho: *"Cada arquivo vira um
   documento próprio. Se algum falhar, os que já entraram continuam criados."*
   O rótulo do botão continua contando (`Adicionar 3 documentos`) — contar o que
   será tentado é verdade; afirmar o desfecho não é.

E a listagem é invalidada **a cada criação bem-sucedida**, não ao fim: os
documentos que entraram aparecem na tabela mesmo que o lote termine mal.

**Alternativa considerada e recusada: rota de lote em `apps/api`.** Daria
atomicidade e tornaria a frase do protótipo verdadeira. Recusada pela convenção 1
— backend não nasce dentro de change de tela — e porque a atomicidade é a
resposta errada para o caso: cinco arquivos em que um é inválido, o operador quer
os quatro bons dentro.

### D3 — Polling condicional por `refetchInterval` de função, e é o primeiro do painel

`refetchInterval` do TanStack Query aceita `number | false | ((query) => number |
false | undefined)` — conferido na tipagem instalada
(`@tanstack/query-core/build/modern/_tsup-dts-rollup.d.ts:1675`), não deduzido.

A listagem de documentos usa a forma de **função**: devolve `4000` enquanto
algum documento estiver em `Pending` ou `Indexing`, e `false` quando todos
estiverem terminais. `refetchIntervalInBackground` fica no default (`false`) — o
painel em aba de fundo não precisa acompanhar.

**Conferido se o painel já faz isso: faz polling, mas não condicional.**
`useSessionMessagesQuery` (`features/sessions/api/useSessions.ts:21`) usa
`refetchInterval: 5000`, um número constante, com `enabled` ligado à presença do
`sessionId`. A condição ali é "a timeline está aberta", não "os dados ainda
mudam". **Esta é a primeira consulta do painel cujo intervalo depende do
conteúdo da resposta**, e o padrão nasce aqui: a condição mora numa função pura
exportada (`hasNonTerminalDocument`), testável sem timer e reusável pela faixa de
resumo — não numa expressão embutida no hook.

Os 4 s ficam abaixo dos 5 s da timeline de sessões de propósito: ali a espera é
por uma resposta de agente, que leva segundos; aqui é por um passo de pipeline
que a 2a mede em centenas de milissegundos mais a fila.

**Achado da implementação, e ele custou uma leitura errada antes de sair certo
(convenção 6).** O teste que afirma "o polling para quando o último documento
fica terminal" reprovava com a requisição **tendo acontecido e devolvido o dado
novo** — o `refetch()` retornava `Indexed` e `result.current.data` continuava
`Indexing`, por cinco segundos de `waitFor`.

A causa não é o `refetchInterval`: é o **`notifyOnChangeProps: 'tracked'`**, que é
o default do react-query. O objeto devolvido pelo hook é um **proxy** que registra
quais props foram **lidas**, e o componente de `renderHook` não lê nenhuma — quem
lê é o teste, por `result.current`. Com o primeiro `waitFor` tocando só
`isSuccess`, `data` nunca entrou no conjunto rastreado, e uma mudança **só** em
`data` não provoca re-render. Tocar `data` desde a primeira espera resolve.

**O que vale registrar é o erro de diagnóstico, não o conserto.** A primeira
reprodução isolada mudou **duas coisas de uma vez** — acrescentou o
`refetchInterval` em forma de função *e* deixou de tocar `.data` — e o resultado
foi creditado ao `refetchInterval`. É a forma da convenção 6 que engana mais:
medição correta respondendo à pergunta errada, e ela quase custou abandonar o
recurso da biblioteca por um artefato do teste. O isolamento só vale quando muda
**uma** variável.

**Sem barra de progresso percentual**, e a asserção que protege isso é
**negativa** (convenção 13/15). O sistema conhece o **estado** do documento;
`IndexingAttempts` conta execuções, não fração de trabalho, e não existe
denominador em lugar nenhum.

### D4 — A contagem de fragmentos é governada por `indexedAt`, numa função pura

`fragmentCountLabel(document)` em `utils/documentIndexing.ts` devolve `null`
quando `indexedAt` é nulo e a contagem formatada quando não é — **em qualquer um
dos quatro estados**. Célula vazia quando `null`.

Mora numa função pura, exportada e testada isoladamente, por um motivo concreto:
é a regra que alguém vai "consertar" ao ler a tabela e achar estranha uma coluna
vazia. O comentário que carrega a causa (C1 acima, com o sítio de
`KnowledgeIndexingService.FailAsync`) fica junto da função, no lugar onde a
correção errada seria escrita.

Os seis casos que a spec fixa: `Indexed` exibe; `Pending`/`Failed` **novo** omite;
`Pending`/`Indexing`/`Failed` **com `indexedAt`** exibe a contagem anterior.

**O escopo da asserção negativa foi corrigido na implementação** (convenção 9), e
a correção vale mais que o conserto. A primeira redação do teste afirmava *"nunca
produz `0 fragmentos`, em nenhum estado"* — e reprovou contra a própria
implementação, com razão. A proibição é de **zero com `indexedAt` nulo**, que é o
default de uma coluna que ninguém escreveu; não é proibição de zero em geral.

É a distinção que a docstring de `KnowledgeBaseIndexingSummaryResponse` manda
não confundir — *"o zero daqui é um zero MEDIDO, e é por isso que ele pode ser
exibido... Não confundir os dois zeros"* —, e escrever a negativa larga demais
era justamente confundi-los, dentro da change que existe para separá-los.

E o par `indexedAt` preenchido + contagem zero **não é alcançável**, conferido no
código: `KnowledgeIndexingService` recusa gravar `Indexed` com zero fragmentos
(`fragments.Count == 0` vira `FailAsync`, guarda própria da 2a contra regressão
no fragmentador), e `FailAsync` preserva a contagem anterior, que é maior que
zero. Uma asserção sobre esse par estaria fixando comportamento de um estado que
o sistema não produz — o inverso do defeito, e igualmente sem base.

### D5 — O aviso de reindexação do modal de atualizar é **condicional**, por comparação local de conteúdo

Corrige C4. Três partes:

1. **A condição é `conteúdo carregado !== conteúdo editado`**, comparação de
   string no cliente. Não há alternativa: `contentHash` não está no fio (ver a
   conferência acima). O comentário de `KnowledgeDocument.Update()` diz que, para
   toda linha criada a partir da 2a, o hash e a comparação de string **coincidem**
   — elas só divergem na linha **legada**, anterior à 2a, cujo hash é nulo: ali a
   string diz "não mudou" e o servidor reindexa assim mesmo.
   **A UI erra por omissão nesse caso, nunca por excesso**: ela deixa de avisar
   sobre uma reindexação que vai acontecer. É o lado seguro, e está declarado na
   spec em vez de escondido.
2. **A cópia diz o que o sistema faz — e são DUAS cópias, não uma e silêncio.**
   Remover o que era falso é metade do trabalho; a outra metade é que **o
   comportamento real é melhor que o aviso do protótipo**, e é isso que o
   operador usa para decidir se atualiza agora ou depois. A tela não sabe de
   antemão em qual dos dois casos o operador está, então cobre os dois:

   - **Conteúdo alterado** — *"O conteúdo mudou: o documento volta para pendente
     e será indexado de novo. O conteúdo indexado anteriormente continua
     respondendo até a nova indexação terminar com sucesso — **e se ela falhar,
     ele permanece**."* É a garantia 3 de D9 da etapa 1 chegando à tela, com a
     metade que mais tranquiliza (a falha não derruba o que já respondia), que o
     protótipo não só omitia como invertia.
   - **Conteúdo idêntico** — *"O conteúdo não mudou: salvar **não** reindexa o
     documento, e o estado de indexação continua o mesmo."* Sem essa frase, o
     operador que corrige só o título espera uma reindexação que não vem, vê o
     estado não mudar e **lê isso como defeito**. É a regra do `ContentHash`
     tornada visível; calar sobre ela é a convenção 13 na direção da omissão — a
     tela não afirma nada falso e esconde justamente o que explica o que ele está
     vendo.

3. **O rótulo do botão acompanha.** `Salvar e reindexar` quando o conteúdo mudou;
   `Salvar` quando não mudou. O rótulo incondicional do protótipo afirma um efeito
   que o servidor não vai produzir.

Duas asserções, e as duas importam: a **negativa** (editar só o título não produz
a palavra "reindexar" em lugar nenhum) e a **positiva** (editar só o título
produz a nota de que salvar não reindexa). Só a negativa passaria com a tela
calada, que é o defeito que a segunda cópia existe para impedir.

### D6 — Sem `@mantine/dropzone`; `FileButton` do core + handlers de `drop`

**Conferido: `@mantine/dropzone` não está no `package.json` e não está em
`node_modules`** (`@mantine/` tem `core`, `form`, `hooks`, `notifications`,
`store`). Acrescentá-lo seria dependência nova para um caso que o core já
resolve — e a convenção 2 pede consumidor real, não previsto.

O que se usa: `FileButton` de `@mantine/core` (conferido: tem `multiple`,
`accept` e `resetRef`) dentro de uma área com `onDragOver`/`onDragLeave`/`onDrop`,
que é exatamente a composição do protótipo. A validação de extensão é do cliente
e não some com o `accept` do input — arquivo arrastado não passa por ele.

**Nenhuma versão a fixar nesta change**: não há runtime, framework nem
biblioteca nova. As versões em uso (React 19, Mantine 9, TanStack Query 5,
Vite 8) foram fixadas em changes anteriores e não mudam aqui.

### D7 — A leitura do arquivo usa `file.text()`, não `FileReader` — e isso conserta C5

O protótipo usa `new FileReader()` + `readAsText` com callback `onload`, e é daí
que sai a ordem errada de C5: a recusa por extensão empurra de forma síncrona e o
aceite empurra do callback.

A tela usa `await file.text()` sobre a seleção **em ordem**, o que devolve a lista
na ordem que o operador escolheu, com aceites e recusas intercalados onde devem
estar. Conferido no jsdom instalado: `File` e `File.prototype.text` existem.

A decisão **não** reabre C3: continua sendo leitura de texto no cliente e envio
como string no mesmo corpo JSON. Só troca o mecanismo de leitura por um que não
inverte ordem.

### D13 — As duas entradas de arquivo convergem numa função só, e a convergência é afirmada

O `DataTransfer` não existe no jsdom 29 (medido), e por isso o **gesto** de
arrastar não é reproduzível na suíte. Declarar isso e parar seria aceitar que
metade da lógica de arquivo fica sem teste — e não fica, se as duas entradas
convergirem.

**Desenho:** existe **uma** função de tratamento, `collectFiles(fileList)`,
exportada de `utils/documentUpload.ts`. `DocumentFileList` a chama a partir de um
único `handleFiles`, e as duas entradas são adaptadores finos sobre ele:

- **clique** — `FileButton onChange={handleFiles}`, que já entrega `File[]`;
- **arrastar** — `onDrop={(e) => { e.preventDefault(); handleFiles(Array.from(e.dataTransfer.files)); }}`.

O adaptador de `drop` não tem lógica: extrai e delega.

**A asserção que o jsdom permite, e ela muda o tamanho do risco.**
`@testing-library/dom` trata `dataTransfer` como caso especial no `eventInit`
(conferido em `dist/events.js:72-77`), então `fireEvent.drop(area, { dataTransfer:
{ files: [arquivo] } })` entrega um `File` **real** ao adaptador. O teste de
convergência afirma que o resultado renderizado dessa entrada é **o mesmo** que o
de `userEvent.upload` com os mesmos arquivos.

**O que essa asserção prova e o que não prova**, dito em vez de escondido
(convenção 11): o `dataTransfer` é um objeto forjado no teste, então ele **não**
prova que um navegador real entrega essa forma nesse evento. Prova a
convergência — que os dois caminhos chegam à mesma função com os mesmos dados —,
e isso é o que importa aqui, porque o caminho do clique é exercitado com `File`
real através de um `<input>` real. Com a convergência afirmada, **a lógica está
coberta e só o gesto não está**; sem ela, metade da lógica ficaria com a
conferência manual como única rede.

**O defeito clássico que nem isso pega**, e por isso ele é item nomeado da
conferência manual: `onDragOver` sem `preventDefault()` faz o navegador **nunca
disparar `drop`** e navegar para o arquivo. jsdom não tem essa regra, então a
suíte fica verde com a área de soltar inteiramente morta.

### D8 — Cópia de ajuda: some o conselho sobre tamanho; fica o que o sistema verifica

Corrige C2. A frase sobre documentos grandes não vira nem texto estático de
ajuda. O que **fica** é a nota que o sistema sustenta, porque
`KnowledgeDocumentLimits.MaxContentBytes` é um teto real de 1 MiB validado no
cadastro, e a recusa por extensão é real: *"PDF, DOCX e imagens não são aceitos: a
base guarda texto markdown."*

O `failureReason` é renderizado **como veio**, sem interpretação. A 2a garantiu
que ele é texto legível por operador (`KnowledgeIndexingFailure.Describe`), e esta
é a única cópia de falha que a tela tem. Quando ele vier nulo num documento
`Failed` — alcançável se a gravação da falha tiver sido descartada por revisão —
a faixa diz que o motivo não foi registrado, e não inventa um.

### D9 — Convenção 16: nenhuma variável nova por esquema, e o motivo é verificável

A faixa de falha e a faixa de aviso trocam de ponta da escala entre os esquemas,
que é exatamente o gatilho da convenção 16. **Mesmo assim não nasce variável
nova**, porque o papel já tem um token que troca sozinho: `Alert` do Mantine tem
variante default `light` e resolve a cor por `theme.variantColorResolver`
(conferido em `node_modules/@mantine/core/esm/components/Alert/Alert.mjs:9-13`),
que é ciente do esquema.

O precedente é da 5b: `AgentKnowledgeTab.tsx:176` usa
`<Alert color="yellow" py="xs">` como faixa dentro de `SectionedCard.Row`. A
faixa de falha é a mesma composição com `color="red"`.

**Nenhum tom fixo da escala neutra é usado como fundo** — o guarda estático
`components/data/surfaceTokens.test.ts` já varre isso e continua valendo sem
alteração. A faixa de cabeçalho do card vem de `SectionedCard`, que já lê
`--buteco-surface-subtle`.

### D10 — `indexingAttempts` e `lastAttemptAt` não viram campos próprios na tela

Os dois estão no fio e são dados reais. Ficam fora, e não por esquecimento: a 2a
os criou para **tornar verdadeiro** o texto que o `failureReason` já carrega
(*"429 nas três tentativas, a última às 03:14"*), e é ali que eles aparecem para
o operador. Exibi-los de novo ao lado da faixa seria a mesma informação em duas
superfícies, com risco de discordarem — `failureReason` é gravado num instante e
os contadores em outro.

Gatilho para entrarem: a 5c, se o diagnóstico do índice precisar de uma visão por
tentativa que a faixa de falha não dá.

### D11 — Convenção 7: a página busca, os componentes recebem

`KnowledgeBaseDetailPage` passa a chamar `useKnowledgeDocumentsQuery(id)` e
repassa `documents`, `isLoading` e `error` como prop.
`KnowledgeDocumentsCard`, `KnowledgeDocumentModal` e `DocumentFileList` **não
importam hook de query nem de mutation**. As mutações são disparadas pela página
e chegam aos componentes como callbacks.

O `request<T>`/`ApiError` vem de `knowledgeBasesApi.ts`, da **mesma feature** —
não de um cliente compartilhado (D12 da 5a-1). Documento pertence à feature
`knowledge-bases` por conceito de domínio, não por origem da rota (convenção 7).

### D12 — O placeholder é removido, não esvaziado

`KnowledgeBaseDocumentsPlaceholder` e seu teste saem da árvore. Ele existia para
dizer que a tela **não consulta** documentos; agora consulta. Mantê-lo como
estado vazio seria reaproveitar um componente cuja única razão de existir era a
ausência de outro — e o estado vazio novo diz outra coisa: *"Nenhum documento
nesta base"*, que agora é uma afirmação **verificada**, porque a listagem foi
consultada e voltou vazia.

## Risks / Trade-offs

Cada risco tem contraparte verificável (convenção 10): cenário na spec **e**
teste, ou a justificativa de por que não é testável.

| # | Risco | Mitigação | Contraparte verificável |
|---|---|---|---|
| R1 | Alguém "conserta" a célula de fragmentos vazia escrevendo `0`, reintroduzindo C1 | A regra mora numa função pura com a causa comentada ao lado (D4) | Cenário `Documento que falhou sem nunca ter sido indexado não exibe contagem` + teste que afirma a **ausência** da cadeia `0 fragmentos`, visto reprovando com o ternário do protótipo no lugar (convenção 15) |
| R2 | O polling não para e a tela requisita para sempre | Condição em função pura, testada sem timer (D3) | Cenário `A listagem para de recarregar quando todos os documentos estão terminais` + teste do hook que afirma `refetchInterval` falso com a lista toda terminal, visto reprovando com a forma constante `refetchInterval: 4000` |
| R3 | O operador lê que atualizar tira o documento das consultas e adia uma correção (C4) | Cópia corrigida contra a garantia 3 de D9 da etapa 1, **incluindo que os fragmentos anteriores permanecem se a reindexação falhar** (D5) | Cenário `O aviso diz que o conteúdo anterior continua respondendo, inclusive se a nova indexação falhar`, **positivo**; mais a asserção negativa de que a cópia não afirma saída das consultas nem descarte imediato. Visto reprovando com o aviso do protótipo no lugar |
| R3b | O operador corrige só o título, nada acontece, e ele lê isso como defeito | Cópia própria para o caso de conteúdo idêntico, dizendo que salvar não reindexa (D5) | Cenário **positivo** `Alterar só o título informa que salvar não reindexa` — só a asserção negativa passaria com a tela calada, que é exatamente o defeito |
| R4 | Falha parcial no lote duplica documentos num "tentar de novo" | Estado por linha; linha `criado` deixa de ser enviável (D2) | Cenário `Reenviar depois de falha parcial não recria os documentos que já entraram` + teste que conta as chamadas ao `POST` por arquivo |
| R5 | A linha legada (hash nulo) reindexa sem aviso, porque a comparação local diz "não mudou" | Erro é por omissão, nunca por excesso, e está **declarado** na spec (D5) | Cenário próprio na spec dizendo que o aviso pode faltar nesse caso. **Não testável no frontend**: a distinção mora numa coluna que não sai no fio, e forjar a divergência no teste seria fixture que passa com o comportamento certo e com o errado (convenção 11) |
| R6 | O caminho de arrastar-e-soltar quebra e a suíte não vê | As duas entradas convergem em `collectFiles`, e a convergência é **afirmada** por teste (D13) | **Coberto: a lógica.** Cenário `Arrastar um arquivo e escolhê-lo pelo seletor produzem o mesmo resultado`, com `File` real nos dois caminhos. **Não coberto: o gesto** — jsdom 29 não tem `DataTransfer` nem `DragEvent` (medido), e o `dataTransfer` do teste é forjado, então ele não prova a forma que um navegador real entrega (convenção 11, dito em vez de escondido). Sem a convergência, metade da lógica ficaria sem teste; com ela, o que resta é o gesto |
| R6b | `onDragOver` sem `preventDefault()` mata a área de soltar e a suíte fica verde | Nenhuma mitigação em teste é possível | **Não testável no jsdom, e declarado**: a regra de que o navegador só dispara `drop` depois de um `dragover` com `preventDefault` não existe no jsdom. Entra na conferência manual como item **nomeado** (tarefa 9.4), não como "conferir a área de soltar" genérico |
| R7 | A conferência manual declara convergência cedo, como na 5b | Conferência é tarefa própria e **iterativa**, com dimensões comparadas explicitamente e captura na largura real (1860px) | Tarefa própria em `tasks.md`, com rodadas numeradas e o que cada uma achou; a 5b registrou que comparar **estados** sem comparar **dimensões** deixou passar uma largura errada |
| R8 | A contagem de fragmentos some da tela por o `indexedAt` vir como string vazia em vez de nulo | O tipo declara `indexedAt: string \| null` espelhando `DateTimeOffset?`; a função trata só `null` | Cenário + teste de `fragmentCountLabel` com `null`; o formato do fio está fixado por `KnowledgeWireFormatTests` do lado de `apps/api`, que é onde o defeito pertenceria (convenção 12) |

**Trade-off assumido, não risco:** a tabela de documentos não tem busca nem
paginação. Uma base com centenas de documentos renderiza tudo. É o desenho do
protótipo e o volume declarado é por base; se incomodar, o pedido é `?q=` e
paginação em `GET /knowledge-bases/{id}/documents` — backend, change própria.

## Tamanho projetado (décima medição da convenção 18)

**A pergunta que separa as duas classes, respondida antes de projetar: esta
change entrega CÓDIGO.** As duas decisões que ela carrega (D1, D2) são reais mas
pequenas, e o volume está em componente e teste — perfil da oitava medição (que
acertou linhas), não da nona (que errou 40% porque o entregável era uma decisão
carregada por prosa, a 12:1 de comentário para código). Dois pontos carregam
prosa pesada e estão orçados como tal: `utils/documentIndexing.ts`, onde mora a
regra que alguém tentaria desfazer, e o aviso de D5.

**Projetado depois de fechar a verificação**, com criados e modificados
separados, em pares com o teste.

**Criados — produção**

| arquivo | linhas | âncora |
|---|---|---|
| `types/knowledgeDocument.ts` | ~55 | `knowledgeBase.ts` = 26, para 6 campos; aqui 14 campos + 2 inputs |
| `api/knowledgeDocumentsApi.ts` | ~90 | `knowledgeBasesApi.ts` = 100 com o `request<T>` dentro; aqui 6 funções e o `request<T>` importado |
| `api/useKnowledgeDocuments.ts` | ~120 | `useKnowledgeBases.ts` = 77 (1 query simples + 4 mutações); aqui a query carrega o polling e as mutações têm alvo aninhado |
| `utils/documentIndexing.ts` | ~70 | `agentUsage.ts` = 14; aqui 4 funções + o comentário de D4 |
| `utils/documentUpload.ts` | ~70 | inclui `collectFiles`, o ponto único de convergência de D13 |
| `components/KnowledgeDocumentsCard.tsx` | ~200 | `KnowledgeBaseTable.tsx` = 105 (3 colunas, sem faixa); aqui 4 colunas + faixa de falha + vazio + faixa de resumo |
| `components/KnowledgeDocumentModal.tsx` | ~255 | `KnowledgeBaseForm.tsx` = 183 (2 campos); aqui 2 modos × 2 propósitos + orquestração de D2 + as **duas** cópias de D5 |
| `components/DocumentFileList.tsx` | ~130 | — |
| **subtotal** | **8 arq / ~990 li** | |

**Criados — teste** (o par de cada um acima; `types/` não tem par)

| arquivo | cenários | linhas |
|---|---|---|
| `knowledgeDocumentsApi.test.ts` | 8 | ~180 |
| `useKnowledgeDocuments.test.ts` | 8 | ~230 (timers falsos = arranjo próprio, faixa de 25-40 li/cenário) |
| `documentIndexing.test.ts` | 8 | ~110 |
| `documentUpload.test.ts` | 6 | ~90 |
| `KnowledgeDocumentsCard.test.tsx` | 14 | ~330 |
| `KnowledgeDocumentModal.test.tsx` | 15 | ~425 (`File` real + N chamadas + falha parcial = arranjo próprio; +1 cenário positivo de D5) |
| `DocumentFileList.test.tsx` | 8 | ~200 (+2 de D13: a convergência e o caminho de `drop` com `dataTransfer` forjado) |
| **subtotal** | **67** | **7 arq / ~1.565 li** |

**Modificados** (em pares — a quarta medição errou 3x por contar produção e ser
cega aos testes dela)

| arquivo | linhas |
|---|---|
| `pages/KnowledgeBaseDetailPage.tsx` | ~55 |
| `pages/KnowledgeBaseDetailPage.test.tsx` | ~120 |
| **subtotal** | **2 arq / ~175 li** |

**Removidos:** `KnowledgeBaseDocumentsPlaceholder.tsx` (23) e o teste (41) —
**2 arq / 64 li**.

**Total à mão: 19 arquivos tocados (15 criados, 2 modificados, 2 removidos) /
~2.730 linhas escritas**, artefatos OpenSpec e documentação fora da conta, como
manda a convenção.

**A projeção subiu ~4% na revisão da proposta**, sem mudança de escopo de
produto: a segunda cópia de D5 e a convergência de D13 só existiram depois da
revisão. Vai registrado porque é a terceira ocorrência do que a convenção 18 já
diz — *"projeção feita durante a verificação é rascunho"* —, agora numa forma
nova: a **revisão** também é verificação, e ela ainda acrescenta. O número de
arquivos não se moveu.

**Blast radius lido no código, não estimado.** O que a change alcança fora da
própria feature foi varrido e é **nada**: o único importador de
`KnowledgeBaseDocumentsPlaceholder` é a página de detalhe; nenhum tipo
compartilhado ganha campo obrigatório (o defeito que tocou 21 fixtures na 5a-1
não tem análogo aqui — `knowledgeDocument.ts` nasce isolado e nenhuma fixture
existente o referencia); `routes.tsx` não muda porque os modais vivem sobre o
detalhe; `src/test/setup.ts` não muda porque `File` e `FileReader` existem no
jsdom instalado.

**A faixa dos modificados carrega uma incerteza que a dos criados não tem** — a
quarta medição registrou que só a montagem revela o que um componente
compartilhado **assume**. Citar as duas com a mesma precisão seria falsa
confiança: os 2 modificados são o número que a leitura do código sustenta, e a
montagem pode achar mais.

## Baseline medida (convenção 19)

Medida **antes** de qualquer edição, em `git worktree` limpo — é o que separa "já
estava assim" de "eu quebrei", e é barato.

| | valor |
|---|---|
| commit | `0c4a622` |
| alvo | `apps/frontend` (`npm test`) |
| resultado | **69 arquivos / 617 testes, 617 passando** |
| duração | 78,89 s |
| load na largada | 1,95 |
| saída completa | `~/.cache/buteco-agents/kb-5a2-baseline/frontend-baseline-completo.log` |

**Baseline verde**, então qualquer reprovação no fechamento é regressão desta
change até prova em contrário — não há aqui a ambiguidade da baseline vermelha,
que remove a hipótese de regressão e **só** isso.

A saída foi guardada inteira, sem `grep` e sem `head`: a 2a perdeu o nome do
único teste que reprovou por ter filtrado antes de a rodada terminar, e o evento
não se repetiu. O fechamento (tarefa 10.1) usa o mesmo procedimento de carga.

## Verificação dos guardas (convenção 15) — resultados medidos

Cada defeito foi **reintroduzido de propósito** e o guarda **visto reprovar**;
depois desfeito e visto passar. Nenhum foi deduzido.

| # | defeito reintroduzido | reprovaram | no componente certo? |
|---|---|---|---|
| 8.1 | o ternário do protótipo (`Failed → '0 fragmentos'`) | **5** — 3 na unidade, 2 no componente, incluindo a negativa nas duas camadas | sim |
| 8.2 | decidir por `indexingStatus === 'Indexed'` em vez de `indexedAt` | **6** — as três de "contagem anterior" (`Failed`, `Pending`, `Indexing` com `indexedAt`) nas duas camadas | sim |
| 8.3 | `<Progress>` + "42%" na linha não-terminal | **1**, e só ela | sim |
| 8.4 | `refetchInterval: 4000` constante | **3**, todas sobre *parar* de recarregar; as que ligam o polling seguiram verdes | sim |
| 8.5 | o aviso incondicional do protótipo + rótulo fixo | **4** (ver abaixo) | sim |
| 8.5a | `onDrop` tratando por conta própria em vez de delegar a `collectFiles` | **2**, ambas no bloco de convergência; os 3 testes do seletor seguiram verdes | sim |
| 8.6 | linha `criado` continua enviável | **1**, e só ela | sim |

**8.2 é o que justifica ter reescrito a correção herdada.** O defeito que ele
reintroduz — decidir pelo estado — é exatamente o que a correção registrada pela
5a-1 (*"exibir quando `indexedAt` não é nulo, omitir quando é"*, lida como "célula
vazia") **não** descreveria como erro. Seis testes reprovam nele, e nenhum deles
existiria se a regra tivesse sido implementada como estava escrita.

### 8.5 achou um defeito nos próprios testes: três negativas eram VAZIAS

Na primeira execução de 8.5, com o aviso do protótipo no lugar, reprovaram **3** —
e a negativa *"o aviso não afirma que o documento sai das consultas"* **passou,
com o texto proibido renderizado na tela**.

Causa: o `Modal` do Mantine renderiza em **portal**, fora do `container`
devolvido por `render()`. As quatro negativas do modal liam
`container.textContent`, que **nunca enxerga o conteúdo do modal** — passavam com
e sem o defeito.

É a vacuidade da convenção 15 na forma que a 2a registrou para a checagem de boot
(`SELECT DISTINCT` sobre tabela vazia), agora em teste de componente: *o arranjo
não coloca o sistema no estado que eu penso que coloca*. Corrigido lendo
`document.body`, e com o defeito ainda no lugar as reprovações passaram de **3
para 4** — a negativa virou discriminante.

**As negativas do card não têm o problema e foi conferido, não suposto**: o card
é inline, não vai a portal, e 8.3 reprovou lendo `container.textContent`.

**Destino:** é a segunda vez nesta base que a rodada de guardas devolve um defeito
**no teste** em vez de na produção — a primeira foi em
`knowledge-base-vinculo-agente`, com uma asserção que ordenava `Guid` por valor.
São os dois casos que sustentam o custo desta rodada quando alguém com pressa
quiser cortá-la por parecer cerimônia.

## Conferência manual (convenção 14) — rodadas e o que cada uma achou

Feita com o painel real contra um **stub de `apps/api`**, porque a semente do
protótipo não tem os quatro estados juntos nem os três casos de "contagem
anterior" que só existem depois da 2a. O stub monta o estado **estático**, que é o
que permite olhar com calma — ver a nota sobre a semente no Context.

Chrome 152 headless por CDP, **1860px** (a largura em que o painel é usado, não os
1440 que esconderam o defeito da 5b).

| rodada | o que cobriu | achado |
|---|---|---|
| 1 | detalhe nos **dois esquemas**: quatro estados, as duas faixas de falha, faixa de resumo, contagem anterior nas três formas, card de agentes | **A1, corrigido** |
| 2 | modal de adicionar (dois modos) e de atualizar (dois modos), esquema claro | nenhum de tela; **um defeito de método** (abaixo) |
| 3 | confirmação de exclusão | nenhum — nomeia os dois agentes corretamente |

**A1 — o motivo da falha ocupava a largura toda (~1.580px).** O
`CONHECIMENTO.md` (seção 3) especifica `max-width: 820px` para esse texto, e
**"sem truncar" não é "largura total"**: limitar a medida não corta nada, e uma
linha de 1.580px é ilegível. Corrigido com `maw={820}`, medido depois em 820.

É exatamente a classe de achado que a 5b registrou ter deixado passar: ela
comparou **estados** (lista, vazio, aviso, modal) e **nunca comparou dimensão**.
Comparar largura foi passo explícito aqui, e foi o único passo que produziu
achado.

### O defeito de método: a conferência por CDP é cega a modal do Mantine

Na rodada 2, **nenhum modal abria**. A raiz do `Modal` existia no DOM com
`innerHTML.length === 0`, e a captura mostrava um fantasma semitransparente que
parecia defeito de renderização da tela.

**Desmentido por baseline, e é o que separa "meu" de "ambiental":** o modal de
**desativar base** — código da 5a-1, intocado por esta change, com testes
passando — não abria **do mesmo jeito**. Logo não era desta change.

Causa real, medida: o `Modal` do Mantine usa `FocusTrap`, que exige a página
**focada**. Em headless a página não tem foco, o trap não resolve e o conteúdo
nunca monta. `Emulation.setFocusEmulationEnabled` resolve, e com ele os modais
renderizam normalmente.

**Duas leituras erradas foram descartadas no caminho**, e as duas eram plausíveis:
"é artefato de compositing" (desmentida porque o modal do **protótipo**, no mesmo
navegador, sai sólido) e "o `rAF` está estrangulado" (desmentida medindo `rAF`,
que respondeu `ok 6`). Só a terceira hipótese sobreviveu à medição.

**Destino: isto pertence ao método, não a esta change.** A conferência por CDP que
a 5b estabeleceu **não enxergava modal nenhum** e ninguém tinha percebido, porque
até aqui os modais conferidos tinham passado por captura em transição — os
fantasmas das primeiras rodadas desta change são a mesma coisa que a 5b viu e
leu como "captura durante a animação". Qualquer conferência futura precisa de
`Emulation.setFocusEmulationEnabled`, ou vai declarar convergência sobre um modal
que ela nunca viu.

### O que a conferência automatizada NÃO cobriu, e fica nomeado

- **Os modais no esquema escuro.** O controle de tema do rodapé não foi acionável
  pelo seletor usado nas rodadas 2 e 3; o detalhe foi conferido nos dois
  esquemas, os modais só no claro.
- **O gesto de arrastar-e-soltar**, incluindo o `preventDefault` do `dragover`
  (ver R6b).

Os dois vão para a validação do operador (tarefa 9.7) como itens **nomeados**, e
não como "conferir a tela". A automação reduz o número de rodadas humanas; não
substitui nenhuma — a 5b declarou convergência em quatro rodadas e o usuário
achou uma largura errada na tela dele.

## Fechamento da suíte (convenção 19)

| | baseline | fechamento |
|---|---|---|
| commit | `0c4a622` | árvore de trabalho |
| arquivos / testes | 69 / **617** | 75 / **722** |
| resultado | 617 passando | **722 passando** |
| duração | 78,89 s | 58,93 s |
| load na largada | 1,95 | 4,33 |
| saída completa | `frontend-baseline-completo.log` | `frontend-fechamento-completo.log` |

`npm run lint` limpo; `npm run build` (com `tsc -b`) passa.

**+6 arquivos de teste e +105 testes**: 7 criados menos 1 removido com o
placeholder.

### A primeira tentativa de fechamento foi DESCARTADA, e o motivo fica registrado

A primeira rodada reprovou **3 de 562** e só conseguiu rodar **61 dos 75
arquivos**, com 14 erros de `[vitest-pool]: Failed to start forks worker` e
`Timeout waiting for worker to respond`. Duração: **2.155 s** contra 59 s.

Não foi classificada — foi **descartada**, e a diferença importa. O load na
largada era 3,85 e subiu a **62** durante a rodada, com um teste isolado levando
17 minutos; as três reprovações incluíam arquivos que esta change não toca. Uma
rodada em que o próprio executor não consegue subir workers não é medição de
nada, e ler as três reprovações como achados teria sido inventar defeito a
partir de ruído.

O que sustentou o descarte, e não a leitura: a repetição com a máquina em load
4,33 passou **722/722**, com a suíte inteira rodando.

**É o par que faltava ao registro da 2a.** Lá uma reprovação única ficou como
dúvida residual porque o **nome do teste se perdeu** num `grep`, e o evento não se
repetiu. Aqui a saída foi guardada inteira desde a primeira tentativa, os nomes
estavam lá, a repetição foi feita e a dúvida fechou. O custo de guardar continua
sendo um arquivo; o retorno é esta linha.

### Dívida pré-existente encontrada e NÃO corrigida de carona

`npm run format:check` reprova em dois arquivos —
`features/auth/pages/LoginPage.tsx` e `features/mcp-servers/utils/agentUsage.ts`.

**Conferido contra a baseline, não reconhecido:** os mesmos dois reprovam no
`git worktree` limpo em `0c4a622`, e `git status` confirma que esta change não os
tocou. Fica registrado em vez de corrigido — formatar arquivo de outra feature
dentro de uma change de tela é escopo que ninguém pediu, e o defeito pertence a
quem o introduziu.

## Décima medição da convenção 18 — projetado × entregue

Só **código**; artefatos OpenSpec e documentação fora da conta, como nas nove
anteriores.

| | projetado | entregue | erro |
|---|---|---|---|
| criados — produção | 8 arq / ~990 li | **8 / 1.248** | arquivo **exato**; linhas **+26%** |
| criados — teste | 7 arq / ~1.565 li | **7 / 1.726** | arquivo **exato**; linhas **+10%** |
| criados — total | 15 / ~2.555 | **15 / 2.974** | arquivo **exato**; linhas +16% |
| modificados (em pares) | 2 / ~175 | **2 / 275** | arquivo **exato**; linhas +57% |
| removidos | 2 / 64 | **2 / 64** | **exato** |
| **total à mão** | **19 / ~2.794** | **19 / 3.313** | **arquivo exato; linhas +19%** |

### Contagem de arquivo: quarto acerto seguido, e o primeiro EXATO nas três faixas

19 projetados, 19 entregues — e não por compensação: criados, modificados e
removidos bateram **cada um** separadamente. Depois de `knowledge-base-vinculo-agente`
(25/25), dos 21/21 só nos criados de `frontend-knowledge-base-catalogo`, dos 6/6
e 8/8 de `frontend-agente-aba-conhecimento` e dos 7/7 criados da 2b, o método —
**contar por componente, criados e modificados separados, em pares com o teste, a
partir do blast radius lido no código** — está confirmado por cinco medições e
pode parar de ser tratado como hipótese.

**O que fez os modificados baterem aqui foi uma decisão de higiene, não sorte.** O
`prettier --write` rodado sobre o glob da feature reformatou **8 arquivos** que
esta change não precisava tocar (`KnowledgeBaseEditPage` e teste,
`knowledgeBasesApi`, `useKnowledgeBases.test`, `KnowledgeBaseDescriptionCard` e
teste, `KnowledgeBaseForm` e teste). Conferido arquivo a arquivo: **reflow puro do
prettier, sem mudança semântica**. Todos revertidos, pelo mesmo critério que
deixou `LoginPage.tsx` e `mcp-servers/utils/agentUsage.ts` intocados — formatar
arquivo que a change não altera é escopo que ninguém pediu, e meia correção de
dívida de formatação é pior que nenhuma, porque some da vista.

Sem a reversão a linha "modificados" teria sido **10 arquivos**, e a medição
reportaria um erro de 5x que não era do método de projeção: era ruído de
ferramenta. **Vale como régua: medir depois de limpar o que a ferramenta tocou
sozinha, ou a medição mede o `prettier`.**

### Linhas: +19%, e a causa é a prevista — mas não onde a previsão esperava

A pergunta que o `design.md` respondeu **antes** de projetar era *"esta change
entrega código ou entrega uma decisão?"*, e a resposta foi **código**. O perfil se
confirmou: nada perto dos +40% da nona, cujo entregável era uma decisão carregada
por prosa a 12:1.

**Os dois pontos orçados como prosa pesada ficaram dentro:**
`documentIndexing.ts` saiu 91 contra ~70 (+30%, e o comentário da regra do
`indexedAt` é metade do arquivo, como previsto); o aviso de D5 está dentro de
`KnowledgeDocumentModal.tsx`, que saiu 354 contra ~255.

**Onde a projeção errou mais foi nos modificados: 275 contra ~175 (+57%)**, e a
causa é específica e reutilizável: `KnowledgeBaseDetailPage.tsx` saiu **152 linhas
alteradas** contra ~55 projetadas. A projeção contou "buscar a listagem, repassar,
montar os modais" e foi cega ao que a página precisa **orquestrar**: quatro
mutações com seus toasts de sucesso e erro, dois estados de modal, e a
confirmação de exclusão inteira com a derivação dos agentes. **Página que recebe
N ações de um componente apresentacional paga por ação, não por componente** — é
a mesma forma do erro já registrado (projetar por operação CQRS é cego aos
modificados), um nível acima.

### Escopo acrescentado durante a implementação

Registrado separado, porque medição de método só compara o escopo projetado:

- **`maw={820}` no motivo da falha**, vindo da rodada 1 da conferência manual
  (~6 linhas com o comentário).
- **Três asserções negativas reescritas** para ler `document.body` em vez de
  `container`, vindas da rodada de guardas — o modal do Mantine renderiza em
  portal e as negativas eram vazias (~15 linhas).
- **Dois cenários de teste** que só existiram quando a asserção original se
  mostrou não discriminante.

Nenhum é erro de projeção: são coisas que não existiam quando ela foi feita.

## Open Questions

Nenhuma. As duas decisões que o enunciado pedia estão fechadas em D1 e D2, com o
motivo. As incertezas de dependência foram resolvidas por leitura do código —
não sobrou pergunta de negócio ou de produto em aberto.
