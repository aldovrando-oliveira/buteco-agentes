## Context

A etapa 2a entregou o pipeline de indexação inteiro e nenhuma rota. Esta é a
superfície de operação que faltou, e ela é `apps/api` puro: duas rotas, dois
handlers, um response, um método de entidade, zero migração.

O que vem decidido de trás e **não se redecide aqui**: fila própria
(`knowledge-indexing`), máquina de estados `Pending → Indexing → Indexed |
Failed`, `ContentHash`, `ContentRevision`, `FragmentCount`, `FailureReason`
legível, contador de tentativas, as três garantias de D9 da etapa 1 — e, da
5a-1, a regra de exibição de `fragmentCount`: exibido quando `indexedAt` não é
nulo, **omitido** quando é nulo, nunca zerado.

### Verificações feitas antes das decisões (convenção 6)

Todas lidas no código desta árvore, em `79ad3f1`. Duas delas desmentiram uma
premissa do enunciado, e uma terceira mudou a forma do resumo.

**V1 — `KnowledgeBaseResponse` tem 6 sítios de construção, e quatro são
comandos.** `ListKnowledgeBases`, `GetKnowledgeBaseById`, `CreateKnowledgeBase`,
`UpdateKnowledgeBase`, `ActivateKnowledgeBase`, `DeactivateKnowledgeBase`. O
record é posicional: campo novo quebra a compilação nos seis. **Nenhum sítio em
`tests/`** — os testes desserializam, e por isso passariam calados com campo
zerado. É o material de D1.

**V2 — o molde de agregação em lote de `ListAgentsQueryHandler` serve, e serve
inteiro.** Três agregações, todas `Join` + `GroupBy` + `ToDictionary`, com
`GetValueOrDefault(id, [])` na projeção final — **nunca** chamada por item dentro
do `Select`. O par "sem item" já é resolvido lá pelo `GetValueOrDefault`, e é
exatamente a forma que a base sem documento nenhum pede. Material de D2.

**V3 — o desempate do catálogo não é tocado por esta change.**
`ListKnowledgeBasesQueryHandler` ordena por `CreatedAt` com `ThenBy(Id)`, e o
guarda que o protege é o par completo da convenção 15: comportamental **mais**
`EmittedSqlCapture.AssertOrderByEndsWithTieBreak` sobre o SQL de produção
(`KnowledgeBaseCatalogTests.cs:56`). Como D1 deixa o handler intacto, o guarda
segue de pé sem nenhuma alteração. A pergunta do enunciado — "se a agregação
mudar a forma da consulta, o desempate sobrevive?" — deixa de existir por
construção, e essa é uma consequência de D1 que vale contar entre os seus
argumentos.

**V4 — o caminho do publisher, conferido e não inventado.**
`CreateKnowledgeDocumentCommandHandler` e `UpdateKnowledgeDocumentCommandHandler`
injetam `IKnowledgeIndexingJobPublisher` e chamam
`PublishAsync(new KnowledgeIndexingJobMessage(document.Id,
document.ContentRevision), ct)` **depois** do `SaveChangesAsync`, nunca antes. O
`Attempt` é o terceiro parâmetro do record, com default `1`. A reindexação usa
essa mesma forma, na mesma ordem.

**V5 — o limite de tentativas NÃO mora em `IndexingAttempts`, e isso desmente a
premissa do enunciado.** Em `apps/workers`:

- `KnowledgeIndexingService.cs:116` — `if (message.Attempt <
  KnowledgeIndexingQueues.MaxAttempts)`. O limite viaja **na mensagem**.
- `KnowledgeIndexingService.cs:137` —
  `SetProperty(d => d.IndexingAttempts, message.Attempt)`: **atribuição, não
  incremento**. O consumidor sobrescreve a coluna no início de cada execução.

Logo, uma reindexação que publica `Attempt = 1` ganha três execuções novas
**mesmo que a coluna fique em 3**, e a própria primeira execução a reescreveria
para 1. O enunciado raciocinou que não zerar "tornaria o botão inútil"; lido no
código, não torna. O reset entra assim mesmo, por um motivo que é outro e que é
verificável — D4. Este é o caso da convenção 6 na sua forma mais cara: a
premissa veio de um item escrito de memória sobre o próprio repositório, e teria
produzido a decisão certa pela razão errada, o que é pior porque sobrevive ao
archive sem nunca ser conferido.

**V6 — o consumidor limpa `FailureReason` no sucesso, e só no sucesso.**
`CommitAsync` faz `SetProperty(d => d.FailureReason, (string?)null)` junto com
`Indexed`. Não há nenhum outro caminho em `apps/workers` que a limpe; em
`apps/api`, só `KnowledgeDocument.Update()` quando há conteúdo novo. Uma
reindexação que não limpasse deixaria o motivo antigo visível ao lado de
`Pending` até a execução terminar. Material de D4.

**V7 — a rota literal não colide.** `GET /knowledge-bases/{id:guid}` tem
restrição `:guid`, que já exclui a cadeia `indexing-summary`; e o roteamento do
ASP.NET Core classifica segmento literal acima de segmento de parâmetro de
qualquer forma. Nenhuma ordem de registro precisa ser garantida.

**V8 — os três apoios de teste de que esta change precisa já existem.**
`FakeKnowledgeIndexingJobPublisher` (com `PublishedFor(documentId)`, que é o que
torna afirmável "publicou exatamente uma, com `Attempt = 1`"),
`EmittedSqlCapture` e `KnowledgeTestClient`. Nenhum apoio novo nasce aqui.

### Baseline da convenção 19 — medida antes de qualquer edição

Tomada em `79ad3f1`, árvore limpa, máquina dentro do limiar registrado (load de
1 min **1,52**, nenhum processo alheio ≥ 100%). **A saída completa, não filtrada,
está em `~/.cache/buteco-agents/2b-baseline/api.txt`** — fora do diretório de
sessão, como a terceira parte da convenção 19 exige, e é contra ela que a tarefa
de fechamento compara.

| alvo | resultado | duração | load1 na largada |
|---|---|---|---|
| `apps/api/Api.sln` | **287/288** | 45 s | 1,52 |

Só `apps/api` porque só `apps/api` muda. A **única** reprovação é
`AgentDeactivationTests.SendMessage_WithPushNotificationConfig_ForInactiveAgent_NeverPublishesJobOrCallsWebhook`,
em 80 ms, com `Assert.Empty() Failure: Collection was not empty` — **o mesmo
teste, com a mesma causa, que a baseline da 2a registrou** (`TaskJobPublisher` é
instância única da classe e `PublishedMessages` acumula; passa isolado). Item em
aberto conhecido. **Qualquer outra reprovação no fechamento é desta change.**

## Goals / Non-Goals

**Goals:**

- Dar ao operador um caminho para reindexar um documento cujo conteúdo está
  certo e cuja indexação falhou — hoje não existe nenhum.
- Servir as colunas `Documentos` e `Indexação` e o filtro `Com falha` do
  catálogo com **uma requisição para o conjunto**, não uma por base.
- Deixar `GET /knowledge-bases` exatamente como está, em custo e em contrato.
- Dizer as duas exceções da reindexação **como exceções**, com o escopo da regra
  que elas contornam, para que nenhuma delas seja lida depois como contradição a
  "consertar".

**Non-Goals:**

- Qualquer mudança em `apps/workers`, `apps/inbox` ou `apps/frontend`.
- As telas que consomem isto (**5a-2**), a coluna `Consultada por` (**5b**), o
  diagnóstico do índice (**5c**), a tool de busca (**etapa 4**).
- Reindexar uma base inteira de uma vez. O protótipo não pede, nenhuma tela tem
  o botão, e a fila é compartilhada — enfileirar 500 documentos por um clique é
  uma decisão de produto que ninguém tomou. Item aberto com gatilho.
- Contagem agregada em `KnowledgeBaseResponse` — recusada em D1, não adiada.

## Decisions

### D1 — Rota de resumo própria, e não contagem em `KnowledgeBaseResponse`

`GET /knowledge-bases/indexing-summary`, uma requisição para o conjunto inteiro.

O enunciado pediu que a escolha fosse feita **com a tela na mão**, olhando o que
cada uma de fato precisa. Feito, e as duas telas querem coisas diferentes — que
era, textualmente, o argumento a favor de separar:

- **O catálogo** (`CONHECIMENTO.md`, seção 1) quer contagem **por base**, para
  duas colunas, dois badges e um filtro, sobre a lista inteira.
- **O detalhe** (seção 2) quer "{resumo de indexação}" de **uma** base — e já
  carrega `GET /knowledge-bases/{id}/documents`, que desde a 2a traz
  `indexingStatus` e `fragmentCount` **por documento**. O detalhe deriva o
  subtítulo do que já tem na mão, e **não precisa de nada** desta change. Quem
  decide derivar é a 5a-2; o ponto aqui é que o dado já está servido.

Ou seja: há **um** consumidor do agregado, não dois. Estender o record que as
duas telas compartilham para servir uma delas é o desenho errado.

E o custo de estendê-lo foi medido, não estimado (V1): `KnowledgeBaseResponse`
tem **6 sítios de construção**, quatro deles handlers de comando. Como o record
é posicional, campo novo quebra a compilação nos seis, e os quatro de comando
ficariam com uma escolha ruim entre duas:

- **fazer a agregação** — uma consulta sobre `knowledge_documents` dentro de
  `CreateKnowledgeBase`, que acabou de criar uma base sem documento nenhum, e
  dentro de `Activate`/`Deactivate`, que não têm nada a ver com indexação;
- **devolver zero** — falso em `Update`/`Activate`/`Deactivate`, e a convenção 13
  o proíbe. Note que **nenhum teste pegaria**: V1 mostra que nenhum sítio de
  construção está em `tests/`, e desserializar campo zerado passa calado.

A rota própria custa **zero** desses seis, e tem três consequências que não são
consolo, são propriedade:

1. `GET /knowledge-bases` continua sendo a consulta simples que é hoje. O
   catálogo não fica mais caro para nenhum consumidor que não queira o resumo —
   e há vários: `AgentResponse` e a tela de vínculo leem o catálogo sem nunca
   olhar contagem.
2. **O desempate do catálogo sobrevive por construção** (V3). A pergunta que o
   enunciado levantou — se a agregação mudaria a forma da consulta e derrubaria
   o `ThenBy(Id)` — some, porque o handler não é tocado. O guarda existente,
   comportamental **mais** determinístico sobre o SQL emitido, continua de pé sem
   uma linha de mudança.
3. O resumo vira recurso que a tela pode **repetir sozinho**. A seção 3 do
   protótipo descreve o documento indo de `Pendente` a `Indexado` na tela, sem
   recarregar; a 5a-2 vai repetir a leitura enquanto houver documento não
   terminal. Repetir um agregado barato é diferente de repetir o catálogo
   inteiro a cada intervalo.

*Custo aceito:* o catálogo passa a fazer **duas** requisições em vez de uma. As
duas são para o conjunto inteiro e **nenhuma escala com o número de bases** — o
número de 100+ bases que matou as colunas na 5a-1 não volta a morder. Duas
requisições para o conjunto e N+1 são coisas de classes diferentes, e é a
segunda que a 5a-1 recusou.

*Alternativa recusada — contagem em `KnowledgeBaseSummaryResponse`:* esse record
é o nível de detalhe do **vínculo** (id + name), e a sua própria documentação já
registra que não tem contagem "pelo mesmo motivo que a etapa 1 registrou:
exigiria segunda consulta agregada por base". Ele aparece dentro de
`AgentResponse`, que a listagem de agentes monta para todos os agentes de uma
vez: contagem ali seria agregação na listagem de agentes, que é o pior lugar
possível para ela.

### D2 — Três contagens, e uma linha para **toda** base

`KnowledgeBaseIndexingSummaryResponse(Guid KnowledgeBaseId, int DocumentCount,
int IndexedCount, int FailedCount)`.

A terceira pergunta do enunciado — *o que é "resumo de indexação"?* — respondida
contra o protótipo, campo a campo:

| o que a tela mostra | do que sai |
|---|---|
| `"{n} documentos"` / `"Nenhum"` | `documentCount` |
| `"3 indexados · 1 falhou"` | `indexedCount`, `failedCount` |
| badge `Falha` | `failedCount > 0` |
| badge `Nada indexado` | `documentCount > 0 && indexedCount == 0` |
| coluna em `--wa` com pendente/indexando/falha | `indexedCount < documentCount` |
| filtro `Com falha` | `failedCount > 0` |

**`Pending` e `Indexing` não ganham contagem própria**, e não é economia: é que
o catálogo **não os distingue**. A seção 1 do protótipo pinta a coluna em `--wa`
"quando há pendente/indexando/falha", sem separá-los, e quem separa é o detalhe,
que já tem o estado por documento desde a 2a. Um quarto campo seria campo sem
consumidor — convenção 2. O complemento continua exato por subtração
(`documentCount - indexedCount - failedCount`), que é aritmética, não inferência.

**Toda base do catálogo tem uma linha, inclusive a de zero documentos e a
inativa.** Duas razões, e a primeira é sutil o bastante para merecer estar
escrita:

- **O zero daqui é um zero medido, e é por isso que ele pode ser exibido.** A
  agregação percorreu os documentos daquela base e não achou nenhum.
  `fragmentCount: 0` num documento nunca indexado é outra coisa — é o default de
  uma coluna que ninguém escreveu, e foi por isso que a 5a-1 o proibiu na tela
  (D5, "`0 fragmentos` é defeito do protótipo"). `documentCount: 0` é fato, e
  `"Nenhum"` é verdade. Omitir a base obrigaria o consumidor a interpretar
  ausência, que é justamente o que produz a afirmação sem base.
- A base **inativa** aparece no catálogo (`ListKnowledgeBasesQueryHandler`
  inclui inativas de propósito) e é uma das que mais pede atenção. Filtrar por
  `IsActive` aqui deixaria as colunas em branco exatamente nessas linhas.

Implementação no molde de V2: **um** `GroupBy` sobre `knowledge_documents` com
as contagens condicionais, `ToDictionary` por `KnowledgeBaseId`, e a projeção
final sobre a **lista de bases** com `GetValueOrDefault(id, zero)` — nunca
contagem dentro do `Select`. Duas consultas no total, independentes de N.

A resposta é **lista ordenada**, pelo mesmo critério do catálogo (`CreatedAt`,
`ThenBy(Id)`), porque `api-response-ordering` vale para toda resposta de lista
desta base. O consumidor casa **por id**, não por posição — a ordem existe para
a resposta ser determinística, não para as duas listas se alinharem índice a
índice, o que uma base criada entre as duas requisições quebraria.

### D3 — A rota de reindexação, no idioma da casa

`POST /knowledge-bases/{knowledgeBaseId:guid}/documents/{id:guid}/reindex`.

Verbo e forma copiados de `POST /{id:guid}/activate`, que é o idioma já
estabelecido para "ação sobre um recurso existente". Filtra por
`KnowledgeBaseId` **e** `Id`, nunca só por `Id` — a mesma regra que
`UpdateKnowledgeDocumentCommandHandler` documenta, e pelo mesmo motivo: sem ela
seria possível agir sobre documento de outra base pelo caminho errado. Base ou
documento inexistente dão **404**.

Responde **200 com o `KnowledgeDocumentResponse` atualizado**, não 202. A
mudança de estado que a resposta descreve — `Pending`, contadores limpos —
aconteceu de forma síncrona e completa; o que é assíncrono é a indexação, e o
documento já diz isso no próprio `indexingStatus`. A tela re-renderiza a linha
sem uma segunda leitura. Não há 202 em nenhum lugar deste repositório, e esta
não é a rota para introduzir um.

Sem `Result` próprio: não há caso de validação, só encontrado/não encontrado, e
o handler devolve `KnowledgeDocumentResponse?` como `ActivateKnowledgeBase`
devolve `KnowledgeBaseResponse?`. `UpdateKnowledgeDocumentResult` existe porque
aquele caminho tem validação; este não tem.

A publicação é `PublishAsync(new KnowledgeIndexingJobMessage(document.Id,
document.ContentRevision), ct)` **depois** do `SaveChangesAsync` — a forma exata
de V4, com `Attempt` no default `1`.

### D4 — As duas exceções, ditas como exceções

Reindexar é a **única** entrada que enfileira sem o conteúdo ter mudado. Ela
contorna duas regras vigentes da 2a, e as duas são bypass deliberado com escopo,
não contradição.

**Exceção 1 — à regra "conteúdo idêntico não é reindexado".** A regra da 2a
governa o que uma **atualização** faz, e existe para não gastar embedding em
edição de metadado. A reindexação não é uma atualização: é pedido explícito do
operador, e existe precisamente para o caso em que o conteúdo não mudou e a
indexação falhou. Sem o bypass a rota não teria função nenhuma.

**E `ContentHash` NÃO é tocado** — nem recalculado, nem anulado. Anulá-lo seria
"o jeito de fazer o bypass funcionar" e é armadilha: quebraria o propósito único
de D9 da 2a e faria a **próxima** atualização só de título reindexar sem motivo.
O bypass é do caminho, não do dado.

**`ContentRevision` também não é incrementada.** Ela é o token de descarte do
consumidor e move-se com o texto; o texto não mudou. Incrementá-la descartaria
uma indexação em voo do **mesmo** conteúdo e faria a coluna significar outra
coisa.

**Exceção 2 — ao significado de `IndexingAttempts`.** A 2a fixou "tentativas
sobre a revisão corrente, não sobre a vida do documento", zeradas quando o
conteúdo muda. Aqui o conteúdo não muda, e a leitura literal diria para não
zerar. Zera-se assim mesmo, e o significado é **refinado, não contrariado**: a
contagem é de tentativas **dentro de uma rodada de indexação**, e uma rodada
abre quando chega conteúdo novo **ou quando o operador pede uma reindexação**.

**O motivo do reset não é o que o enunciado supôs, e essa é a parte que
importa.** Por V5, o limite viaja na mensagem e o consumidor **atribui**
`IndexingAttempts = message.Attempt` no início de cada execução: uma reindexação
com `Attempt = 1` teria três execuções novas e reescreveria a coluna para 1
sozinha. Não zerar não travaria botão nenhum.

O que o reset resolve é a **janela entre o `POST` responder e o consumidor pegar
a mensagem**. Nela o documento leria `Pending` ao lado de `indexingAttempts: 3`,
`lastAttemptAt` de ontem e o `failureReason` antigo (V6 confirma que só o
sucesso o limpa) — e a tela da 5a-2 renderizaria o badge `Pendente` colado em
"429 nas três tentativas, a última às 03:14" e na faixa de falha inteira,
contendo o próprio botão que o operador acabou de clicar. É a convenção 13 na
letra: a UI afirmando uma rodada encerrada que acabou. E a janela não é teórica
— `knowledge-indexing` é fila compartilhada e pode ter trabalho na frente.

Portanto a rota grava, em uma operação: `IndexingStatus = Pending`,
`FailureReason = null`, `IndexingAttempts = 0`, `LastAttemptAt = null`. E
**preserva `IndexedAt` e `FragmentCount`** — o conteúdo anterior continua
respondendo até os fragmentos novos serem gravados (garantia 3 de D9 da etapa 1),
e é `IndexedAt` não nulo que mantém `fragmentCount` exibível pela regra que a
5a-1 fixou. O escritor autoritativo do contador continua sendo o consumidor; a
rota só limpa.

O método nasce na entidade, `KnowledgeDocument.RequestReindex()`, ao lado de
`Update()`, porque é a mesma classe de operação — muda estado com invariante — e
porque deixar o handler escrever quatro propriedades soltas é como a regra se
perde.

### D5 — Reindexação é aceita em qualquer estado

Inclusive `Indexing`, `Pending` e documento **nunca** indexado.

O protótipo só **mostra** o botão dentro da faixa de `Falhou`, mas rota não
impõe estado de tela, e o estado pode virar entre a leitura da tela e o `POST`.
Um 409 converteria uma corrida benigna num erro que o operador teria de
interpretar — e não eliminaria a corrida, só a renomearia.

Documento nunca indexado é o caso mais simples e é o par que a convenção 5 pede:
`Pending` que nunca saiu de `Pending` (porque a fila estava parada, ou porque a
mensagem se perdeu) é exatamente o que a rota conserta, e o resultado é o mesmo
— `Pending`, contadores limpos, mensagem publicada.

O custo de reindexar um documento em voo é **uma execução duplicada** sobre
conteúdo idêntico: as duas leem a mesma `ContentRevision` inalterada, produzem
os mesmos fragmentos, e `CommitAsync` substitui o conjunto inteiro em transação
única. A última gravação vence e o estado converge. Ver R4.

### D6 — Base inativa aceita reindexação

Mesma regra que `CreateKnowledgeDocumentCommandHandler` já enuncia: desativar
impede o **uso pelo agente**, não a manutenção do conteúdo. Reindexar os
documentos de uma base antes de reativá-la é exatamente o que um operador faz.
Recusar seria inventar uma regra que o catálogo não tem.

### D7 — Nenhuma capability nova; o requisito negativo vai para o catálogo

Os dois requisitos cabem em capabilities existentes (convenção 2). O resumo vai
para `knowledge-document-indexing`, e não para `knowledge-base-catalog`, porque
cada campo dele é uma contagem de valor da máquina de estados: quem é dono do
significado de `Indexed` e `Failed` tem de ser dono da contagem deles, senão um
valor novo no enum muda um contrato que mora noutra capability.

Mas o catálogo ganha **um requisito**, e ele é negativo: a resposta de base não
carrega contagem, e o resumo vive em recurso próprio. Requisito e não só
decisão de design porque é a coisa que alguém "corrige" sem ver a causa — a 5a-1
registrou D9 em separado de D1 por essa mesma razão, e ali era só uma opção de
filtro.

### D8 — Tamanho projetado por componente, **depois** de fechada a verificação

Convenção 18, nona medição. Projetado **após** V1–V8, criados e modificados
separados, modificados a partir do blast radius lido no código e **em par com os
testes deles**. Só código; artefatos OpenSpec fora.

**Criados — 7 arquivos, ~525 linhas**

| arquivo | linhas | custo unitário aplicado |
|---|---|---|
| `ReindexKnowledgeDocumentCommand.cs` | ~8 | operação CQRS trivial |
| `ReindexKnowledgeDocumentCommandHandler.cs` | ~45 | ~19 l/operação + comentário denso do estilo da casa |
| `GetKnowledgeBaseIndexingSummaryQuery.cs` | ~6 | operação CQRS trivial |
| `GetKnowledgeBaseIndexingSummaryQueryHandler.cs` | ~50 | duas consultas + molde V2 + comentário |
| `KnowledgeBaseIndexingSummaryResponse.cs` | ~26 | record + a regra do zero medido |
| `KnowledgeDocumentReindexTests.cs` | ~200 | 8 cenários × ~25 |
| `KnowledgeBaseIndexingSummaryTests.cs` | ~190 | 7 cenários × ~27 |

Os dois arquivos de teste usam a faixa **alta** (25-40) do refinamento de custo
unitário da convenção 18, não os ~19-21: quase todo cenário aqui precisa de
**arranjo próprio** — três bases com documentos em estados diferentes, uma base
vazia, uma base inativa, documento levado a `Failed` à mão. É exatamente o
perfil que a convenção registra como subestimado pela faixa baixa.

**Modificados — 7 arquivos, ~155 linhas**

| arquivo | linhas | por quê |
|---|---|---|
| `KnowledgeDocument.cs` | ~30 | `RequestReindex()` + o comentário que diz as duas exceções |
| `KnowledgeDocumentEndpoints.cs` | ~25 | `MapPost` + método |
| `KnowledgeBaseEndpoints.cs` | ~20 | `MapGet` + método |
| `KnowledgeTestClient.cs` | ~15 | atalho de arranjo (reindexar, semear N documentos) |
| `KnowledgeRouteAuthenticationTests.cs` | ~25 | as duas rotas novas sob a guarda de autenticação |
| `KnowledgeWireFormatTests.cs` | ~20 | formato de fio do response novo (convenção 12) |
| `KnowledgeBaseCatalogTests.cs` | ~20 | os dois cenários negativos do requisito de `knowledge-base-catalog` |

**Total projetado: 14 arquivos, ~680 linhas.**

E o número que dá sentido a este: **a contagem de modificados é pequena porque
D1 a tornou pequena.** A alternativa de D1 acrescentaria os 6 sítios de
`KnowledgeBaseResponse` — todos em modificação, todos de uma a três linhas, o
perfil que a convenção 18 registra — mais os testes que passariam a ter de
afirmar os campos novos. A escolha de desenho e o custo de arquivo são o mesmo
fato visto de dois lados.

Não se projeta razão de headline aqui: não há migração, e é o `.Designer.cs` de
migração que produziu os 3,1x medidos.

### D9 — O `Purpose` de `knowledge-base-catalog` é escrito aqui, e é o gatilho funcionando pela primeira vez

O item de 09/09 em `02-HISTORICO_E_STATUS.md` registra **39 de 43 capabilities
com `Purpose` placeholder** e fixa o gatilho: *toda change que criar ou
modificar uma spec viva escreve o `Purpose` dela na mesma passada*, antes do
archive. Esta change modifica `knowledge-base-catalog`. O gatilho se aplica, e
os dois argumentos que sustentavam não fazer mutirão não protegem esta omissão:

- o primeiro é que `Purpose` escrito por quem não tocou o código produz prosa
  genérica — e esta change leu os seis sítios de construção da resposta, a
  regra de descrição obrigatória e a ausência deliberada de `MapDelete`;
- o segundo é custo, e é um.

Deixar como pergunta aberta seria reproduzir exatamente o padrão que o item
nomeia: o `4d` do skill escreve o placeholder, ninguém corrige, e o estoque
nunca cai. **A primeira vez que o gatilho encontra dívida vizinha dentro do
próprio escopo é onde ele precisa funcionar**, ou ele não é gatilho.

O `Purpose` vai **na delta**, não direto na spec viva, porque é assim que o
mecanismo consome o estoque: o passo 4c do skill substitui placeholder pelo
`Purpose` da delta e nunca sobrescreve `Purpose` real com placeholder. E o item
de 09/09 registra a metade que falta e que `validate --strict` não pega —
placeholder é texto válido, e as 44 specs passam com 39 deles. Por isso a
conferência do arquivo **vivo**, depois do sync, é tarefa própria (8.2), e não
confiança no skill.

Estoque depois desta change: **38**. Pelo gatilho, não por mutirão — e essa
distinção é o que o registro no `02` precisa carregar, porque é ela que diz se o
mecanismo funciona.

## Risks / Trade-offs

**R1 — A agregação vira N+1 se alguém escrever a contagem dentro do `Select`.**
É o erro que V2 existe para não cometer, e ele passa despercebido porque o
resultado é *correto*, só caro — nenhuma asserção de conteúdo o pega.
→ *Contraparte:* guarda determinístico sobre o SQL **emitido**, com
`EmittedSqlCapture` (que já está na suíte), afirmando que a requisição emite um
número de comandos **independente do número de bases**. Arranjo com 3 bases, uma
delas sem documento. Convenção 11: o SQL é o da requisição real, nunca
remontado no teste. Convenção 15: reintroduzir o defeito (contagem dentro do
`Select`), ver reprovar, e conferir que reprova **no handler que a correção
toca**.

**R2 — A base sem documento nenhum some do resumo.** É o modo de falha natural
do `GroupBy`: ele não emite grupo para quem não tem linha, e projetar sobre os
grupos em vez de sobre as bases perde a linha em silêncio.
→ *Contraparte:* o par "sem item" obrigatório da convenção 5 — base com zero
documentos aparece com `documentCount: 0`, e a asserção é sobre a **presença da
linha**, não sobre o valor.

**R3 — Alguém "corrige" a reindexação anulando `ContentHash`.** É a leitura
plausível de "reindexar apesar do hash", e quebraria o propósito único de D9 da
2a sem quebrar nada visível na hora.
→ *Contraparte:* cenário afirmando que, **depois** de uma reindexação, uma
atualização com o **mesmo** conteúdo continua não enfileirando. Esse guarda
reprova no instante em que o hash é anulado, e é uma asserção **negativa** sobre
o publisher — a forma que `FakeKnowledgeIndexingJobPublisher` foi feito para
suportar.

**R4 — Reindexar documento que já está `Indexing` duplica a execução.**
→ *Contraparte parcial, com a justificativa explícita que a convenção 10
permite.* O que é afirmável em `apps/api` vira cenário: reindexar a partir de
cada estado responde 200 e publica **exatamente uma** mensagem, com
`Attempt = 1`. O entrelaçamento em si **não é observável daqui** — o consumidor
mora em `apps/workers`, que esta change não toca, e forjar o entrelaçamento num
teste de `apps/api` seria o fixture montado pelo próprio teste que a convenção
11 recusa. A convergência não é esperança: ela decorre de duas propriedades já
cobertas por cenário na 2a — a gravação é condicionada a `ContentRevision`
(inalterada aqui, logo as duas execuções gravam) e substitui o conjunto inteiro
em transação única, sobre conteúdo idêntico. Se algum dia a duplicação passar a
custar (chamada de embedding desperdiçada em documento grande), o gatilho é
medível e vira change própria.

**R5 — O resumo passa a filtrar por `IsActive` "para ficar coerente com o
agente".** Deixaria em branco exatamente as linhas que mais pedem atenção.
→ *Contraparte:* cenário — base **inativa** com documentos aparece no resumo com
as contagens reais.

**R6 — O desempate do resumo passa com o defeito presente.** É a quinta forma da
convenção 15: o critério é uma ordem que o Postgres às vezes já produz sozinho,
e conferir o guarda isolado é o jeito enganoso de conferir.
→ *Contraparte:* o par obrigatório — comportamental **mais**
`EmittedSqlCapture.AssertOrderByEndsWithTieBreak` sobre o SQL de produção, que é
o que reprova em 100% das execuções. Molde idêntico ao de
`KnowledgeBaseCatalogTests.cs:56`, com `CreatedAtTie` para forçar o empate.

**R7 — O catálogo passa a fazer duas requisições.** É o custo aceito de D1.
→ *Contraparte:* não é risco de corretude e não tem teste; é trade-off declarado.
O que o torna aceitável é verificável e está em D1: **nenhuma das duas escala
com o número de bases**, então o número de 100+ bases que motivou a recusa da
5a-1 não volta. Se um dia a segunda requisição doer, o caminho é cache ou
`ETag` no recurso próprio — que só existe como opção **porque** ele é próprio.

### Verificação dos guardas, medida (convenção 15)

Os cinco defeitos foram reintroduzidos de propósito e a reprovação observada.
Dois resultados valem mais que o "passou":

**R3 reprovou sozinho, e é a prova de que ele está no componente certo.** Com
`ContentHash = null` dentro de `RequestReindex()`, a suíte inteira de
conhecimento rodou **135/136**, e a única reprovação foi
`AfterReindexing_UpdateWithSameContent_StillDoesNotEnqueue`. Os cenários de
"conteúdo idêntico não é reindexado" da 2a, em
`KnowledgeDocumentIndexingContractTests`, **passaram verdes com o defeito
presente** — eles afirmam sobre o caminho de `Update()`, que o defeito não toca.
É a segunda forma da convenção 15 evitada por construção: o guarda reprova
exatamente onde a correção mora, e não haveria cobertura nenhuma se ele tivesse
sido escrito junto dos cenários vizinhos que *parecem* cobrir a mesma regra.

**R6 reproduziu a quinta forma da convenção 15, com número.** Removido o
`ThenBy(Id)` do handler do resumo, em três execuções da classe:

| guarda | reprovações |
|---|---|
| comportamental (`SummaryWithEqualCreatedAt_IsTieBrokenById`) | **2 de 3** |
| determinístico sobre o SQL (`SummaryQuery_EmitsTieBreakAsLastOrderByTerm`) | **3 de 3** |

Ou seja: o guarda comportamental **passou com o defeito presente** numa das três
— o Postgres devolveu em ordem de id sozinho —, exatamente como a convenção
prevê, e exatamente na proporção medida em `ordenacao-desempate-listas-vinculo`.
O par não é zelo: sem a metade determinística a cobertura aqui seria
probabilística.

Os outros três, para registro: **R1** reprovou só
`SummaryQuery_CostDoesNotGrowWithNumberOfBases` (7 dos 8 passaram), confirmando
que o N+1 é invisível a toda asserção de conteúdo — o resultado sai correto, só
caro. **R2** reprovou `BaseWithNoDocuments_IsPresentWithZeros` e, de quebra, o
guarda de desempate, porque a base sem documento também sumia da ordem. **R5**
reprovou só `InactiveBase_IsPresentWithRealCounts`.

### Correções feitas durante a implementação (convenção 9)

Duas, e nenhuma muda decisão — as duas corrigem a projeção e o registro.

**A projeção de tamanho subestimou os modificados em 98%, e a causa entrou na
convenção 18 como pergunta nova, não como fator.** Ver a nona medição no `02`: o
custo em linha desta change é dominado pela prosa que carrega as decisões
(razão comentário/código de ~12:1 em `RequestReindex()`), e nenhuma projeção por
componente vê isso.

**Um arquivo modificado a mais do que o projetado, e é achado reutilizável.** O
par "sem item" do resumo — nenhuma base cadastrada — não cabia em
`KnowledgeBaseIndexingSummaryTests`, porque a fixture daquela classe tem bases
criadas pelos outros cenários. Foi para `KnowledgeEmptyCatalogTests`, a única
classe com banco garantidamente vazio. A projeção olhou a superfície da feature e
não a isolação de fixture.

**E uma decisão pequena tomada na implementação:** o formato de fio do resumo
ficou em `KnowledgeBaseIndexingSummaryTests`, e não em `KnowledgeWireFormatTests`
como a tarefa 6.2 dizia. Motivo: aquela classe serializa o tipo com as opções web
e não tem fixture, enquanto aqui a asserção é sobre o **texto da resposta HTTP
real**, que é a forma forte da convenção 11 — e a fixture já está paga. Um
ponteiro ficou registrado em `KnowledgeWireFormatTests` para quem for procurar.

## Migration Plan

Não há migração de banco: nenhuma coluna, nenhuma tabela, nenhum índice. Não há
mudança de configuração nem de dependência.

Duas rotas novas, ambas aditivas e autenticadas. `apps/frontend` não conhece
nenhuma das duas até a 5a-2, e `apps/workers` não muda — a mensagem publicada é
a que o consumidor já lê hoje.

Rollback é reverter o commit. Uma mensagem de reindexação que já esteja na fila
no momento do rollback é consumida normalmente: ela é indistinguível de uma
publicada por uma atualização de conteúdo.

## Open Questions

- **Reindexar uma base inteira.** Fora de escopo por Non-Goal, e não por
  esquecimento: nenhuma tela do protótipo tem o botão, e a fila é compartilhada
  com toda indexação — 500 documentos por um clique é decisão de produto que
  ninguém tomou. *Gatilho para virar change:* a primeira vez que um operador
  precisar reindexar mais de um punhado de documentos por causa de uma troca de
  modelo de embedding, que é o cenário real em que isso aparece.
- Nenhuma. O `Purpose` de `knowledge-base-catalog`, que era a única pergunta
  aberta da primeira redação, foi resolvido para dentro do escopo — ver D9.
