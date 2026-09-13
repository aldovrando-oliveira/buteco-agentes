## Context

Etapa **5a-3** da linha de bases de conhecimento: as colunas `Documentos` e
`Indexação` e o filtro `Com falha` no catálogo, em `apps/frontend`. O backend
está pronto e **não é tocado**.

É a change que a 5a-1 recusou por falta de dado. A recusa tinha número: preencher
as duas colunas custaria uma requisição **por base**, contra as 100+ bases que o
handoff declara (`CONHECIMENTO.md`, seção 4.2). A 2b resolveu com uma requisição
para o conjunto, e a 5a-2 conferiu a dependência **presente** e adiou com posição
na fila — terceira da linha, atrás só do resolvedor de tool
(`design.md` da 5a-2, D1). O resolvedor foi entregue em
`openspec/changes/archive/2026-09-12-knowledge-tool-resolver/`. A posição chegou.

### Protótipos

`design/` traz os três arquivos que valem, lidos **pela conexão com o Claude
Design** (projeto `Sistema Gestão de Agentes`,
`e4f9bbd6-dd31-4ff1-954b-0e686d2eaa54`), não copiados da change anterior:

| Arquivo | O que é |
|---|---|
| `design/Buteco Agentes.dc.html` | Protótipo navegável, 163.947 bytes, cópia byte-exata do projeto. Abre no navegador com `support.js` ao lado. |
| `design/support.js` | Runtime do protótipo. Sem ele o `.dc.html` não renderiza. |
| `design/CONHECIMENTO.md` | Especificação de design da linha, revisão 2 de 09/09/2026. |

`README-painel.md` não volta: a 5a-1 o anexou pela nota do card A2A, que já foi
consumida, e a 5a-2 já o tinha deixado de fora.

**O protótipo MUDOU desde o arquivamento, e conferir isso é passo, não zelo.** A
cópia arquivada pela 5a-2 tem 152.095 bytes; a do projeto tem **163.947**. Diff
byte a byte das duas: as 169 linhas de diferença são **uma tela nova,
`Playground`**, mais o item de navegação dela e o campo `bases` no cabeçalho do
chat. Da tela do catálogo, a **única** linha alterada é a que lista as flags de
rota, que ganhou `isPlay`. O `support.js` é idêntico (`md5 951ae391…` nos dois) e
o `CONHECIMENTO.md` também. Ou seja: para esta change o protótipo arquivado
serviria — mas isso só se sabe **depois** de comparar, e assumir teria sido
assumir.

## Conferência protótipo × código (convenção 17), feita ANTES das decisões

### As oito correções já registradas: nenhuma toca esta tela

`02-HISTORICO_E_STATUS.md` mantém a lista viva de correções de protótipo.
Conferidas uma a uma contra o catálogo de bases, para não redescobrir nem
reimportar:

| # | correção registrada | tela | toca o catálogo? |
|---|---|---|---|
| 1 | `0 fragmentos` → governado por `indexedAt`, nunca pelo estado | tabela de documentos | **não** — o catálogo não exibe fragmento nenhum |
| 2 | a frase "documentos muito grandes tendem a bater no limite" sai | motivo da falha no detalhe | **não** |
| 3 | upload é leitura de texto + corpo JSON, não `multipart` | modal de documento | **não** |
| 4 | a busca do modal de vincular normaliza acento | modal Vincular base | **não** — a busca do catálogo já usa `matchesSearch` desde a 5a-1 |
| 5 | o modal de vincular marca base inativa | modal Vincular base | **não** |
| 6 | o aviso do modal de atualizar afirma duas coisas falsas | modal de atualizar | **não** |
| 7 | a lista de arquivos do modal não preserva a ordem da seleção | modal de adicionar | **não** |
| 8 | a soma de fragmentos filtra por `status === 'indexed'` e **subconta** | Diagnóstico do índice (5c) | **não** — o catálogo não soma fragmento |

**A oitava é anterior a esta change e de tipo diferente das sete.** Saiu da
exploração do backend do diagnóstico, não desta proposta, e registrá-la foi passo
desta sessão porque a lista viva ainda dizia sete. As sete primeiras são sobre o
que a tela **afirma**; a oitava é sobre o que a tela **calcula errado** — e o modo
de detecção também é outro: as primeiras saem lendo a cópia, esta saiu lendo o
código do mock contra a spec viva. Conferida nos dois lados aqui antes de ser
escrita no `02` (ver a seção seguinte).

**Resultado: zero herdadas para esta tela.** É registro útil por si — a tela do
catálogo nunca tinha sido percorrida com as colunas em pauta, porque na 5a-1 elas
caíram por falta de dado antes de haver o que conferir.

### A oitava correção, conferida nos dois lados

Não é achado desta change e não muda nada do que ela entrega, mas foi conferida
aqui antes de ir para a lista viva — a lista já carregou uma evidência falsa uma
vez, e é caro repetir.

**No protótipo** (`design/Buteco Agentes.dc.html:1667`):

```js
chunks: d.reduce((n, x) => n + (x.status === 'indexed' ? (x.chunks || 0) : 0), 0)
```

A soma é filtrada por **estado**. O consumidor é a aba de diagnóstico,
`{ label: 'Fragmentos no índice', value: String(t.chunks) }` (`:1805`) — ou seja,
a tela da **5c**, que é justamente a change que vai implementar essa soma.

**No código** (`apps/api/src/Buteco.Api/KnowledgeDocuments/Entities/KnowledgeDocument.cs:282`),
`RequestReindex()` altera `IndexingStatus`, `FailureReason`, `IndexingAttempts` e
`LastAttemptAt` — e **não toca** `IndexedAt` nem `FragmentCount`, com a docstring
de `:271` declarando a preservação. Somada à garantia 3 de D9 da etapa 1
(`FailAsync` não apaga fragmento; o descarte é atômico com a inserção, dentro de
`CommitAsync`), a consequência é direta: **um documento que indexou e falhou ao
reindexar continua com fragmentos vivos no índice e some da soma do protótipo.**
A soma subconta o índice real.

**E o protótipo erra a mesma coisa por um segundo caminho**, que vale registrar
junto porque quem consertar só o `reduce` não fecha o buraco: `reindex`
(`:1695`) e a atualização de conteúdo (`:1923`, `:1941`) gravam `chunks: 0` na
hora, enquanto o backend preserva a contagem. O mock descarta o que o sistema
mantém.

É a mesma regra da correção 1 — **o separador é `indexedAt`, nunca o estado** —
aplicada a outro lugar. Que ela reapareça num segundo ponto é o argumento para
ela estar na lista viva em vez de só no `design.md` de uma change.

### O percurso, e as três correções novas

Protótipo dirigido por CDP, Chrome 152 headless, **1860px**, nos **dois
esquemas**, com `Emulation.setFocusEmulationEnabled` — sem ele o modal não monta,
e a 5a-2 registrou que a conferência por CDP era cega a isso.

A semente tem nove bases, e os estados que interessam aparecem: `k1` com
3 indexados e 1 falhou, `k2` com 1 indexado + 1 indexando + 1 pendente, `k3`
inativa e sem documento nenhum. O protótipo **anima** `pending`/`indexing` para
`indexed` no `componentDidMount`, então os estados não terminais só existem nos
primeiros segundos — a captura imediata é obrigatória, e foi feita.

**C9 — a coluna `Indexação` distingue `pendente` de `indexando`, e a rota não.**
Lido na tela, linha `Catálogo de Produtos`, textualmente:
`1 indexado · 1 indexando · 1 pendente`. A fonte é `indexSummary` no protótipo,
que monta quatro parcelas a partir de `kbStats`.

`GET /knowledge-bases/indexing-summary` devolve **três** contagens —
`documentCount`, `indexedCount`, `failedCount` — e a spec viva de
`knowledge-document-indexing` diz por quê, com todas as letras: *"`Pending` e
`Indexing` NÃO ganham contagem própria: o consumidor que precisa distingui-los é
o detalhe da base, que já recebe o estado por documento na listagem de
documentos. O total não terminal permanece exato por subtração."*

Não é lacuna a reportar, é decisão tomada com motivo. **A tela se ajusta ao dado**
(D5).

**C10 — o badge `Nada indexado` dispara em base que não tem o que indexar, e o
protótipo já diz isso em outras duas células da mesma linha.** Achado
**dirigindo**, não lendo: na semente, a única base sem documento (`k3`) é
inativa, e o badge é condicionado a `k.isActive`, então o defeito **não aparece**
sem interagir. Percurso feito: abrir `Rotinas Internas`, acionar `Ativar`, voltar
ao catálogo. A linha resultante, verbatim:

```
Rotinas Internas | Nenhum | Nenhum documento | Cobrança Ativa | Ativa  Nada indexado
```

Três células dizendo a mesma coisa, uma delas em tom de alerta, sobre uma base
que não tem nada para indexar. E o mesmo badge foi visto em `Promoções Vigentes`
enquanto o único documento dela estava **indexando** — ou seja, marcado como
problema exatamente durante o funcionamento normal do pipeline.

**C11 — a célula inteira vai a tom de aviso, misturando progresso com falha.**
`idxFg: (t.failed || t.pending || t.indexing) ? 'var(--wa)' : 'var(--mut)'`.
Conferido nos dois esquemas: `3 indexados · 1 falhou` sai âmbar **por inteiro**,
e `1 indexando` sai âmbar também. Uma falha de indexação e um documento em fila
recebem o mesmo sinal, e dentro da célula com falha a parcela boa fica pintada
como se fosse problema.

### Conferência tela a tela: campo, rota, dado derivado, papel visual

| pergunta | resultado |
|---|---|
| Todo campo exibido existe na resposta? | **Não, um** — a separação `pendente`/`indexando` (C9). Os outros três (`documentCount`, `indexedCount`, `failedCount`) existem. |
| Toda ação tem rota? | **Sim** — nenhuma ação nova; a tela só lê. |
| Há contagem/estado derivado que o sistema não coleta (convenção 13)? | **Sim, dois** — a separação de C9 e a leitura de "problema" em base sem documento (C10). Os dois recusados. |
| Há papel visual que troque de ponta da escala sem variável por esquema (convenção 16)? | **Não, e por construção** — ver D11. |

### Superfície real da API (convenção 6: lido, não deduzido)

`KnowledgeBaseIndexingSummaryResponse`
(`apps/api/src/Buteco.Api/KnowledgeBases/Responses/KnowledgeBaseIndexingSummaryResponse.cs:44`)
é um record posicional de quatro campos: `KnowledgeBaseId`, `DocumentCount`,
`IndexedCount`, `FailedCount`.

O **formato de fio** não foi deduzido da política camelCase: está afirmado por um
teste que inspeciona o **texto** do JSON da resposta HTTP real
(`KnowledgeBaseIndexingSummaryTests.SummaryResponse_UsesCamelCaseFieldNamesOnTheWire`),
que lê `knowledgeBaseId`, `documentCount`, `indexedCount` e `failedCount` e
afirma negativamente que nenhuma variante de caixa aparece no JSON. É exatamente
o guarda que a convenção 12 pede para nome terminado em sigla, e é por isso que o
tipo do cliente pode ser escrito com confiança.

A **ordenação** também está afirmada: o handler ordena por `CreatedAt` com
`ThenBy(Id)`, e há teste capturando o SQL emitido para provar que o `ORDER BY`
termina em desempate. O catálogo usa o mesmo critério. **Isso não autoriza casar
por posição** — ver D6.

## Goals / Non-Goals

**Goals:**

- Um operador abre o catálogo e vê, por linha, se a base tem conteúdo e se ele
  está utilizável, sem entrar em cada base.
- O filtro `Com falha` isola as bases que precisam de atenção.
- Nenhuma célula afirma mais do que as três contagens sustentam, e nenhuma
  afirma ausência quando a consulta não respondeu.
- Toda divergência entre protótipo e código resolvida por decisão explícita, com
  o número da convenção que a sustenta (convenção 17).
- Conferência manual como tarefa própria e iterativa, nos dois esquemas
  (convenção 14).

**Non-Goals:**

- Qualquer mudança em `apps/api`, `apps/workers` ou `apps/inbox`.
- O detalhe da base, a tabela de documentos e os modais — entregues na 5a-2 e
  intocados aqui.
- Diagnóstico do índice (5c), que segue bloqueada por um passo de `apps/api`
  ainda não proposto.
- Extração de componente compartilhado novo. Nada aqui tem os dois ou três
  consumidores reais que a convenção 2 exige.
- Dependência nova de pacote. **Nenhuma** — por isso não há versão a verificar
  nesta change; o único ponto de biblioteca é uma propriedade já instalada,
  conferida em D7.

## Árvore de pastas proposta

Só `apps/frontend`. **Dois arquivos criados**, o resto modificado em pares
produção + teste.

```
apps/frontend/src/features/knowledge-bases/
  types/
    knowledgeBase.ts                    MODIFICADO  + KnowledgeBaseIndexingSummary
  api/
    knowledgeBasesApi.ts                MODIFICADO  + listKnowledgeBaseIndexingSummary
    knowledgeBasesApi.test.ts           MODIFICADO
    useKnowledgeBases.ts                MODIFICADO  + useKnowledgeBaseIndexingSummaryQuery
    useKnowledgeBases.test.ts           MODIFICADO
  utils/
    indexingSummary.ts                  CRIADO      funções puras das duas colunas e do filtro
    indexingSummary.test.ts             CRIADO
  components/
    KnowledgeBaseTable.tsx              MODIFICADO  3 → 5 colunas
    KnowledgeBaseTable.test.tsx         MODIFICADO
  pages/
    KnowledgeBaseListPage.tsx           MODIFICADO  busca o resumo; 4ª opção de filtro
    KnowledgeBaseListPage.test.tsx      MODIFICADO
```

Nada em `libs/`: não há nada a compartilhar entre apps aqui, e a régua da
convenção do repositório exige necessidade real com justificativa — esta change
não a tem e não a inventa.

## Decisions

### D1 — Recurso próprio, consumido como consulta própria

O resumo é uma segunda requisição, e não um campo em `KnowledgeBaseResponse`. Não
é escolha desta change: a spec viva de `knowledge-base-catalog` **proíbe** o
campo, com três razões verificáveis (seis sítios de construção do response, quatro
deles handlers de comando; devolver zero neles seria falso; o catálogo tem
consumidores que nunca olham contagem).

Consequência para o cliente: `listKnowledgeBaseIndexingSummary()` ao lado de
`listKnowledgeBases()`, no mesmo `request<T>` da feature — nunca um cliente HTTP
compartilhado, convenção 7 e D12 da 5a-1.

### D2 — As duas requisições correm em paralelo, e a listagem nunca espera o resumo

As duas consultas são declaradas na página. O TanStack Query as dispara em
paralelo por construção; nenhuma delas é `enabled` pela outra, e nenhuma é
encadeada.

**O que a tela mostra enquanto uma respondeu e a outra não:** a listagem é
governada **só** pela consulta de bases. Enquanto o catálogo carrega, a tela
mostra o carregamento que já mostra hoje. Quando o catálogo chega e o resumo não,
**as linhas aparecem** e as duas colunas ficam no estado "não sei" (D3).

**Se o resumo falhar:** a listagem continua útil sem as colunas. É o idioma que a
5a-1 já escreveu para o card e a coluna de agentes — *"uma requisição que não
respondeu não é evidência de ausência"* —, e é a mesma escolha de
`McpServerListPage`. A alternativa, reprovar a tela inteira porque um dado
acessório faltou, troca uma listagem incompleta por nenhuma listagem.

**Recusado: encadear**, buscando o resumo só depois do catálogo. Ganharia nada —
a rota do resumo não depende de parâmetro nenhum do catálogo — e custaria uma
viagem em série na tela mais visitada da área.

### D3 — Três estados por célula, e o travessão nunca significa zero

Cada uma das duas colunas tem **três** estados, e a distinção é o coração desta
change:

| estado | quando | o que a célula mostra |
|---|---|---|
| valor | há linha de resumo para esta base | a contagem |
| **vazio** | há linha de resumo, e não há nada a dizer (base sem documento, na coluna `Indexação`) | célula em branco |
| **`—`** | não há linha de resumo: a consulta não respondeu, ou o resumo não trouxe esta base | travessão |

É a mesma gramática que a tela já usa: `ConsultedByCell` distingue `—`
(indisponível) de `Nenhum agente` (zero conhecido), e `fragmentCountLabel`
devolve `null` para "deixe a célula vazia — nunca escreve zero, nunca escreve
travessão no lugar de um número que existiria".

**O caso da base sem linha no resumo merece ser dito em separado**, porque é o que
alguém vai "consertar" com zero. A API garante uma linha por base do catálogo, mas
as duas requisições são **independentes**: uma base criada entre elas aparece no
catálogo e não no resumo. Zerar ali afirmaria uma contagem que ninguém fez — o
mesmo defeito de `0 fragmentos`, na tela vizinha. **Ausência de linha vira `—`,
nunca zero.**

### D4 — O zero de `documentCount` é exibível, e a justificativa é herdada, não reinventada

`documentCount: 0` vira `Nenhum`, e isso **não** contradiz a proibição de zerar
contagem. A distinção já está escrita, e esta change a reusa em vez de reabri-la:

- o zero do resumo é **medido** — a agregação percorreu os documentos daquela base
  e não encontrou nenhum, e a projeção final é sobre as **bases**, não sobre os
  grupos, exatamente para que a base sem documento apareça com zeros
  (`GetKnowledgeBaseIndexingSummaryQueryHandler`, e o cenário *"Base sem documento
  nenhum aparece com zeros"* da spec viva);
- o zero de `fragmentCount` em documento nunca indexado é o **default de uma
  coluna que ninguém escreveu**, e por isso a 5a-1 o proibiu na tela.

A docstring de `KnowledgeBaseIndexingSummaryResponse` nomeia os dois e termina com
*"Não confundir os dois zeros"*. A spec desta change repete a distinção porque
ela é a razão de um requisito virar do avesso, e requisito sem razão é o que a
etapa seguinte reverte por engano.

### D5 — `pendente` e `indexando` viram uma parcela só, `em andamento` (correção C9)

A coluna `Indexação` monta as parcelas a partir das três contagens:

- `{indexedCount} indexado(s)`
- `{documentCount − indexedCount − failedCount} em andamento`
- `{failedCount} falhou / falharam`

Parcela em zero é **omitida**, e as presentes são unidas por ` · `. Com
`documentCount` zero a célula fica **vazia** (D3): não há o que indexar, e a
coluna vizinha já diz `Nenhum` — repetir ali seria a redundância que o protótipo
tem e que o percurso mostrou em três células da mesma linha (C10).

**`em andamento`, e não `pendente` nem `indexando`.** A rota não distingue os
dois, por decisão registrada, e escrever qualquer um dos dois nomes afirmaria uma
separação que o dado não carrega — convenção 13 na forma mais literal. O
complemento por subtração é exato, e é a própria spec da 2b que o chama assim.

Duas alternativas descartadas:
- **Pedir um quarto campo à API.** Seria reabrir uma decisão fechada com motivo,
  e pela convenção 1 backend não nasce dentro de change de tela. Se um dia a
  distinção fizer falta, o gatilho é um consumidor que a peça — não existe.
- **Omitir a parcela não terminal.** Deixaria `3 documentos` ao lado de
  `1 indexado`, e o operador teria de subtrair de cabeça para descobrir que dois
  estão em andamento. A subtração é justamente o que a tela deve fazer por ele.

**O guarda desta decisão é negativo, e mora no DOM** (convenções 13 e 15).

A asserção é sobre o que a **célula renderizada não contém**, no molde das
negativas que a 5a-2 usou para `0 fragmentos` e para a barra de progresso
percentual. Não basta afirmar sobre a string que a função pura devolve, e a razão
é a segunda forma da convenção 15 — **guarda no componente errado**:

> O modo de falha real não é alguém mudar `indexingParts`. É alguém "melhorar" a
> coluna montando as duas parcelas **direto na célula**, de um jeito que *pareça*
> distinguir pendente de indexando. Nesse caminho a função pura fica intocada, o
> teste dela continua verde, e a tela passa a afirmar a separação mesmo assim.

Por isso o guarda é duplo, e os dois níveis têm papéis diferentes:

| nível | onde | o que pega |
|---|---|---|
| função pura | `indexingSummary.test.ts` | a regra da subtração e o rótulo escolhido |
| **DOM** | `KnowledgeBaseTable.test.tsx` | a célula renderizada **não** contém `pendente` nem `indexando`, qualquer que seja o arranjo de contagens — **é este que reprova contra a reintrodução** |

A verificação da convenção 15 (tarefa 7.4) reintroduz o defeito **na tabela**, não
na função, e o resultado a registrar tem duas metades: o teste de DOM reprova
**e** o da função pura continua verde. A segunda metade é a evidência de que o
guarda precisava estar no componente.

### D6 — O cruzamento é por identificador, e a ordem exibida é a do catálogo

O resumo vem ordenado pelo mesmo critério do catálogo, com o mesmo desempate.
**O código não pode depender disso.** São duas respostas de duas requisições
independentes, e "as duas ordens coincidem" é uma propriedade verdadeira hoje que
nenhuma das duas specs promete ao consumidor — o comentário do próprio handler já
diz *"o consumidor casa por id, nunca por posição"*.

Decisão: um `Map` de `knowledgeBaseId → item`, montado uma vez, e a renderização
itera o **catálogo**. A ordem da tela é a do catálogo, sempre.

**O guarda é da forma que a convenção 15 pede, e o motivo é o caso registrado em
`ordenacao-desempate-listas-vinculo`**: um guarda cujo critério é uma ordem que a
fonte às vezes já produz sozinha passa verde com o defeito presente. Por isso o
teste monta o resumo **em ordem deliberadamente diferente** da do catálogo, e
afirma duas coisas: que a ordem das linhas é a do catálogo, e que **cada linha
recebeu as contagens da sua própria base**. Casar por posição reprova nas duas.

### D7 — `Com falha` desabilitado quando o resumo não está disponível

O filtro passa a ter quatro opções. A quarta depende de `failedCount > 0`, então
ela só significa alguma coisa com o resumo em mãos. Três comportamentos possíveis
quando ele não está, e cada um afirma algo diferente:

| opção | o que afirma | veredito |
|---|---|---|
| some do controle | "este filtro não existe" | recusado — o controle muda de forma conforme a rede, e a opção existe |
| aparece e não filtra | "nenhuma base tem falha" | **recusado com precedente**: é o "vazio ou inerte" que a 5a-1 já rejeitou em D9 |
| aparece **desabilitada** | "existe, e agora não dá para usar" | **escolhido** |

Desabilitada não é inerte: o controle informa a indisponibilidade em vez de
mentir por omissão. A tela diz, junto às colunas, que o resumo não pôde ser
carregado.

E há o caso de borda que o `disabled` sozinho não cobre: o operador seleciona
`Com falha`, o resumo é invalidado e a nova consulta falha. Por isso o filtro
**efetivo** é uma função pura — com resumo ausente, `Com falha` resolve para
`todas`, nunca para uma lista vazia que afirmaria ausência de falha. A função
mora em `utils/`, testável sem montar componente, como `hasNonTerminalDocument`.

`SegmentedControlItem` aceita `disabled?: boolean` por item — conferido na
tipagem instalada (`@mantine/core@9.4.2`,
`lib/components/SegmentedControl/SegmentedControl.d.ts:6-10`), não suposto.

### D8 — Sem polling no catálogo, e a alternativa foi conferida antes de recusada

A 5a-2 introduziu polling condicional na tabela de documentos, e o complemento não
terminal do resumo permitiria a mesma condição aqui. **Fica de fora**, por dois
motivos de natureza diferente:

1. **Não há espera.** O polling da 5a-2 acompanha uma transição que o operador
   acabou de provocar, na tela em que a provocou. O catálogo é tela de navegação;
   ninguém carrega documento por ele.
2. **A frescura já está resolvida sem polling, e isso foi conferido, não
   presumido.** `queryClient.ts` cria `new QueryClient()` **sem
   `defaultOptions`**, então valem os defaults — `staleTime: 0` e
   `refetchOnMount: true`. O operador que sobe um documento no detalhe e volta ao
   catálogo **remonta** a página, e a consulta do resumo é refeita.

**E por isso também não se acrescenta invalidação do resumo às mutações de
documento.** Seria código para um problema que os defaults já resolvem —
escopo sem gatilho. A chave da consulta é `['knowledge-bases', 'indexing-summary']`,
o que dá de graça a outra metade: toda mutação de base já invalida por prefixo
`['knowledge-bases']`, então criar ou editar base refaz o resumo sem uma linha a
mais.

**Gatilho para o polling nascer aqui**, nomeado para não virar adiamento
indefinido: uma tela de operação que espere transição **no catálogo**. Hoje não
existe.

### D9 — Os badges `Falha` e `Nada indexado` da coluna `Estado` ficam fora (correção C10)

O `CONHECIMENTO.md` põe na coluna `Estado` o badge `Ativa`/`Inativa` **mais**
`Falha` (err) ou `Nada indexado` (warn). Os dois ficam fora, e não por escopo:

- **`Falha` é redundante na mesma linha.** A coluna `Indexação`, a duas células
  de distância, já diz `1 falhou`, e o filtro `Com falha` já isola essas bases. O
  badge repete em tom de alerta o que o texto ao lado afirma — ruído, não sinal.
- **`Nada indexado` está errado nos dois casos em que dispara**, e os dois foram
  **vistos**, não deduzidos: base ativa **sem documento nenhum** (percurso: ativar
  `Rotinas Internas`) recebe alerta por não ter indexado o que não tem; e base
  cujo único documento está **indexando** (`Promoções Vigentes`, captura imediata)
  recebe alerta durante o funcionamento normal do pipeline. Convenção 13 na
  direção de afirmar problema onde não há.

Um sinal de "base ativa e vazia" pode ter valor — mas ele precisaria de redação
própria (*"sem documento"*, neutro, não alerta) e de consumidor que o peça.
Nenhum dos dois existe hoje, e inventá-los aqui seria a antecipação que a
convenção 2 proíbe.

### D10 — Só a parcela de falha carrega tom de alerta (correção C11)

O protótipo pinta a célula **inteira** de âmbar quando há falha **ou** qualquer
não terminal. Decisão: o texto da célula sai no tom padrão, e **apenas a parcela
`{n} falhou`** recebe a cor semântica de erro. `em andamento` é progresso normal,
e não pede atenção.

A cor é `red`, nome de cor semântica do Mantine — **a mesma** que
`statusPresentation` já usa para `Failed` na tabela de documentos. Assim a falha
tem a mesma cor nas duas telas da área, em vez do âmbar do protótipo aqui e do
vermelho lá.

### D11 — Convenção 16: nenhuma variável nova por esquema, nenhum tom fixo

Os papéis visuais desta change são texto de tabela, texto mudo e uma cor
semântica de erro. Nomes de cor semântica do Mantine resolvem por esquema
sozinhos; `--buteco-surface-subtle`, usado no cabeçalho da tabela, já existe
declarado nos dois esquemas desde `2026-09-06-frontend-acabamento-telas`.

**Nenhum `gray[n]`/`dark[n]` literal, nenhuma variável nova.** O guarda estático
`surfaceTokens.test.ts` passa a cobrir os arquivos novos por construção — ele
varre a árvore inteira de `src`. E a convenção 15 pede mais do que vê-lo verde:
ele já passou verde com o defeito presente uma vez, porque a expressão não cobria
valor dentro de ternário. A tarefa de conferência inclui **reintroduzir** um tom
fixo dentro de um ternário num dos arquivos novos e ver o guarda reprovar.

### D12 — Classificação desta change (convenção 18): entrega código

Não é change de decisão. Entrega código de tela, com testes, e a única prosa que
sobrevive a ela é a spec modificada e as três correções de protótipo.

### D13 — A fila atribuiu carga a mais à 5a-3, e ela NÃO entra aqui

Achado ao registrar a oitava correção, lendo a fila no `02-HISTORICO_E_STATUS.md`.
A linha 3 da tabela diz, da 5a-3: *"Ganhou carga: é onde entra a orientação de
`Description` de base e o nome efetivo da tool na tela."*

São dois itens abertos, com gatilho apontando para *"a mesma change que já vai
tocar `KnowledgeBaseForm`"*:

- **orientação de generalidade da descrição**, achado de `0d` — o aviso do
  formulário mede **comprimento**, e a dimensão que decide roteamento é
  **generalidade**; a descrição atratora tinha 421 caracteres e passaria verde
  pelo limiar de 80;
- **nome efetivo da tool**, cujo gatilho disparou quando a etapa 4 definiu
  `search_<slug>` — com a ressalva inteira de que o `ToolNameDeduplicator` pode
  mudar o nome, então o efetivo precisa aparecer ao lado do pretendido.

**Os dois ficam fora desta change, e não por esquecimento.** Duas razões, e a
segunda é a que decide:

1. **É outra tela.** Os dois vivem em `KnowledgeBaseForm` — criação e edição de
   base. Esta change toca `KnowledgeBaseTable` e `KnowledgeBaseListPage`, e não
   abre o formulário. O enunciado desta etapa é o catálogo.
2. **O custo que não encolhe é a conferência manual.** É exatamente o argumento
   com que a 5a-2 recusou juntar esta tela à dela: a convenção 14 cobra uma
   rodada iterativa **por tela**, nos dois esquemas, e juntar formulário e
   catálogo dobra essa parte — a metade cara, não a do código.

**E a consequência de registro importa mais que a recusa.** Um item cujo gatilho
é *"a change que já vai tocar `KnowledgeBaseForm`"* e cuja posição na fila é uma
change que **não** vai tocar o formulário é, na prática, gatilho sem posição — a
família de defeito que esta base já registrou **três** vezes e nomeou
*"adiamento indefinido com outro nome"*. Os dois itens precisam de **linha
própria na fila**, e a tarefa 10.5 passa a exigir isso em vez de só atualizar o
estado da 5a-3.

## Tamanho projetado (convenção 18)

Projetado **depois** que a verificação fechou — protótipo percorrido nos dois
esquemas, tipagem do Mantine lida, formato de fio e ordenação conferidos no
código de `apps/api`, defaults do `QueryClient` lidos. Projeção feita durante a
verificação é rascunho.

**Criados e modificados contados em separado**, que é a metade do método já
confirmada por duas medições (25 contra 25 em `knowledge-base-vinculo-agente`;
21 contra 21 em `frontend-knowledge-base-catalogo`). Modificados projetados **em
pares**, produção + teste, que foi o refinamento que a décima medição cobrou.

**Âncoras decompostas**, medidas nesta árvore (não headline de commit):

| arquivo | linhas | cenários | linhas/cenário |
|---|---|---|---|
| `utils/agentUsage.test.ts` | 65 | 5 | 13,0 |
| `utils/documentIndexing.test.ts` | 199 | 19 | 10,5 |
| `components/KnowledgeBaseTable.test.tsx` | 142 | 8 | 17,8 |
| `pages/KnowledgeBaseListPage.test.tsx` | 240 | 14 | 17,1 |
| `api/useKnowledgeBases.test.ts` | 190 | 11 | 17,3 |

Teste de função pura sai a ~11-13 linhas por cenário; teste que monta componente,
a ~17. Cenário com arranjo próprio custa 25-40, e esta change tem dois deles (o
cruzamento fora de ordem de D6 e o filtro com resumo ausente de D7).

**Criados — 2 arquivos:**

| arquivo | projeção |
|---|---|
| `utils/indexingSummary.ts` | ~85 linhas. Quatro funções puras (rótulo de documentos, parcelas de indexação, predicado de falha, filtro efetivo) mais o comentário que carrega a causa de D3/D5 — o molde é `documentIndexing.ts`, 91 linhas com comentário longo pelo mesmo motivo. |
| `utils/indexingSummary.test.ts` | ~150 linhas / ~13 cenários a 11-13 linhas. |

**Total criado: 2 arquivos, ~235 linhas.**

**Modificados — 9 arquivos, em pares:**

| par | produção | teste |
|---|---|---|
| `types/knowledgeBase.ts` | +12 (interface + comentário do zero medido) | — (tipo não tem teste próprio) |
| `api/knowledgeBasesApi.ts` | +5 | `+~20` / 2 cenários |
| `api/useKnowledgeBases.ts` | +12 | `+~35` / 2 cenários |
| `components/KnowledgeBaseTable.tsx` | +45 (duas células + comentário que substitui o que hoje explica a ausência) | `+~110` / 6 cenários a ~18 |
| `pages/KnowledgeBaseListPage.tsx` | +25 | `+~120` / 6 cenários, dois com arranjo próprio |

**Total modificado: 9 arquivos, ~384 linhas.** (O tipo não tem par de teste; é a
exceção da regra de pares, e está dita.)

**Projeção: 11 arquivos, ~620 linhas de código.** Mais 4 artefatos OpenSpec e 3
anexos de `design/`, que **não** entram nesta conta — é o erro que a convenção 18
nomeia primeiro.

**Direção de erro esperada:** para baixo, se a conferência manual acrescentar
escopo, como aconteceu na 5a-1 (card de agentes e coluna de uso decididos na
conferência valeram +4 criados e +21 modificados). Medição de método só compara o
escopo projetado.

## Baseline medida (convenção 19)

| | valor |
|---|---|
| commit | `b58e6e8` (árvore limpa) |
| comando | `npm test` em `apps/frontend` |
| arquivos / testes | 75 / **722** |
| resultado | **722 passando** |
| duração | 116,90 s |
| load na largada | 3,57 |
| saída completa | `~/.cache/buteco-agents/kb-5a3-baseline/frontend-baseline-completo.log` |

**E a primeira medição foi vermelha, o que vale registrar mais que a verde.** A
primeira execução, no mesmo commit e na mesma árvore, deu **6 reprovações em 4
arquivos**, todas `Test timed out in 15000ms` — e a tentação era escrever
"pré-existente" ou "ambiental". A convenção 19 proíbe as duas sem medir.

Medido: o `load` na largada era **23,95**, e o Chrome headless da conferência do
protótipo ainda estava vivo. Encerrado o navegador e esperado o load cair a 3,57,
a mesma suíte no mesmo commit passou **722/722**. Uma variável mudou, e só uma.

A lição não é "foi ambiental" — é que **a ferramenta da conferência de protótipo
e a suíte de testes disputam a mesma máquina**, e quem rodar as duas na mesma
sessão precisa encerrar a primeira antes de medir a segunda. Vai para a tarefa de
fechamento como passo explícito.

## Verificação dos guardas (convenção 15) — resultados medidos

Cada defeito foi reintroduzido, visto reprovar, e desfeito. Onde o item exigia as
**duas metades**, as duas estão escritas: não basta "algum guarda pegou".

| # | defeito reintroduzido | reprovou | continuou verde |
|---|---|---|---|
| 7.1 | `documentCountLabel` devolve `'Nenhum'` sem item | **3** — a negativa da função pura e as duas de DOM (`travessão e nunca zero`, `travessão para a base que o resumo não trouxe`) | resto da suíte |
| 7.2 | casamento **posicional** no lugar do `Map` | **1**, e exatamente o de arranjo próprio (`casa cada linha com a sua própria base`) | os outros 262 |
| 7.3 | remoção de `effectiveStatusFilter` do uso da página | **1** (`com a opção selecionada e o resumo ausente, não esvazia a listagem`) | os outros 262 |
| 7.4 | distinção `pendente`/`indexando` montada **na célula**, sem tocar a função pura | **4 de DOM**, inclusive a negativa `a célula não contém pendente nem indexando` | **`indexingSummary.test.ts`: 22/22** |
| 7.4a | parcela `em andamento` pintada com a cor de erro | **2**, as duas negativas de tom (`em andamento` e `indexados`) | **a positiva `a parcela de falha carrega a cor semântica de erro`** |
| 7.5 | `bg={… ? 'green.6' : 'gray.3'}` dentro de ternário | **1** — `surfaceTokens.test.ts`, nomeando arquivo e linha | resto da suíte |

**A segunda coluna da 7.4 é o resultado que justifica o guarda duplo.** A função
pura passou **22/22 com o defeito presente**: ela é literalmente cega a ele,
porque o defeito não a atravessa. Sem a asserção de DOM, a reintrodução entraria
com a suíte verde.

**E a da 7.4a é o que separa "o guarda pegou" de "algum guarda pegou".** As duas
asserções de tom vivem na mesma célula; se a positiva também tivesse reprovado, o
guarda estaria afirmando no componente errado — reprovando por estar tudo
vermelho em vez de por `em andamento` estar. Ela ficou verde.

### O contra-guarda da 7.2, e ele é o resultado mais caro desta rodada

A convenção 15 registra uma quinta forma: **guarda cujo critério é uma ordem que
a fonte às vezes já produz sozinha**. Medida aqui, de propósito:

1. defeito posicional presente + resumo montado **fora de ordem** → **reprova**;
2. defeito posicional **ainda presente** + resumo montado **na mesma ordem do
   catálogo** → **passa verde**.

O segundo passo é a medição que importa. As duas respostas usam hoje o mesmo
critério de ordenação com o mesmo desempate, então um fixture "natural" teria
produzido um guarda inútil — verde com o defeito dentro. É por isso que a ordem
invertida do fixture está comentada no teste: quem a "arrumar" desliga o guarda
sem ver nada ficar vermelho.

## Conferência manual (convenção 14) — rodadas e o que cada uma achou

Feita com o painel real contra um **stub estático de `apps/api`** — a semente do
protótipo anima os não terminais em segundos, e o que se quer olhar com calma é o
estado parado. Cinco bases, uma por estado: com falha, só indexados, sem
documento, em andamento, e **ausente do resumo**.

Chrome 152 headless por CDP, **1860px**, com `Emulation.setFocusEmulationEnabled`.

**Duas correções de método antes da primeira rodada, e as duas custaram uma
medição errada cada:**

- **O stub subiu na 5017 e sombreou o `apps/api` real do usuário, que já estava
  no ar.** Os dois ficaram escutando — um em IPv4, outro em IPv6 — e qual
  respondia dependia da família de endereço. Stub movido para a **5099**, com
  `VITE_API_BASE_URL` apontando para lá. Conferência que sombreia o processo real
  não mede a tela: mede um acaso de resolução de nome.
- **`pkill -f "scratchpad/stub/server.mjs"` não casou com nada**, porque o
  processo foi lançado de dentro do diretório e a linha de comando é só
  `node server.mjs`. O stub antigo continuou vivo, o novo falhou ao ligar, e a
  primeira medição do estado "resumo indisponível" saiu **falsa** (HTTP 200 com a
  variável de falha ligada). Encerrar por **porta**, não por padrão de linha de
  comando.

| rodada | o que cobriu | achado |
|---|---|---|
| 1 | catálogo no esquema escuro, cinco estados, **medindo largura de coluna** | **A1, corrigido** |
| 2 | os dois esquemas + filtro `Com falha` aplicado | nenhum de tela; **A2 medido e aceito** |
| 3 | resumo indisponível nos dois esquemas | **nenhum** — rodada de convergência |

**A1 — a distribuição das colunas saiu muito longe da do protótipo, e só a
medição pegou.** Com o layout automático do Mantine, as duas colunas novas
espremeram as existentes. Medido a 1860px (tabela de 1602px):

| coluna | antes da correção | protótipo | depois |
|---|---|---|---|
| Base | **844** | ~572 | 593 |
| Documentos | 131 | 140 | 144 |
| Indexação | 326 | ~358 | 368 |
| **Consultada por** | **196** | ~334 | 336 |
| Estado | 106 | 150 | 160 |

`Base` estava **48% mais larga** que o protótipo e `Consultada por` **41% mais
estreita** — a lista de nomes de agente quebrando em várias linhas ao lado de uma
coluna `Base` quase vazia. Corrigido com `layout="fixed"` e as proporções do
protótipo normalizadas para 100%; depois, todas as cinco dentro de ~4%.

É exatamente a classe que a 5b registrou ter deixado passar por **comparar
estados e nunca dimensão**. Comparar largura foi passo explícito aqui, e foi o
único passo que produziu achado de tela.

**A2 — o aviso de resumo indisponível leva ~10 s para aparecer, e isso é aceito
com o motivo.** Medido: o aviso não está na tela em 1 s, 3 s, 5 s nem 8 s, e está
em 12 s. A causa é o retry padrão do `QueryClient` (três tentativas com recuo
exponencial) — `isError` só fica verdadeiro depois de esgotá-lo.

**Não é defeito, e mudar isso seria pior.** Durante a espera a tela mostra `—` nas
duas colunas e a opção `Com falha` desabilitada: `—` significa "não sei", que é
**literalmente verdade** enquanto a consulta ainda tenta. Inventar um quarto
estado visual de "carregando" contrariaria a gramática de três estados de D3, e
mexer na política de retry é global — atingiria todas as telas do painel por causa
de uma. O alerta do próprio catálogo (`isError` da listagem de bases) já se
comporta assim desde a 5a-1; esta tela é consistente com o que existe.

### O que a conferência automatizada NÃO cobriu, e fica nomeado

Vai para a validação do operador como item **nomeado**, não como "conferir a
tela":

- **A janela de ~10 s de A2 com olho humano.** A medição diz que a tela nunca
  mente nesse intervalo; se ela *parece* quebrada é julgamento que a automação
  não faz.
- **Largura real do monitor do usuário.** A conferência mediu a 1860px; a 5b
  declarou convergência em quatro rodadas e o usuário achou uma largura errada na
  tela dele. As proporções agora são relativas, o que reduz o risco, mas não o
  elimina para telas muito estreitas.
- **Lista longa de agentes em `Consultada por`** com a largura nova. O stub tem um
  agente; o comportamento com quatro ou cinco nomes não foi olhado.

## Fechamento da suíte (convenção 19)

| | baseline | fechamento |
|---|---|---|
| commit | `b58e6e8`, árvore limpa | árvore de trabalho |
| arquivos / testes | 75 / **722** | 76 / **765** |
| resultado | 722 passando | **765 passando** |
| duração | 74,82 s | 66,36 s |
| load na largada | 3,90 | 2,22 |
| saída completa | `~/.cache/buteco-agents/kb-5a3-baseline/frontend-baseline-completo.log` | `…/frontend-fechamento-completo.log` |

`npm run lint` verde. `npm run build` (com `tsc -b`) verde.

### A reprovação que eu classifiquei como contenção era REGRESSÃO desta change

Uma execução do fechamento reprovou **1 teste** — `router.test.tsx > a rota antiga
de gestão do vínculo leva à aba de ferramentas do detalhe` — com `load` **29,63**,
num arquivo que esta change não toca. Registrei como contenção, terceira da
família. **Estava errado**, e a correção é o resultado mais caro desta change.

O que desmentiu foi rodar o **passo de discriminação já registrado** na convenção
19, em vez de parar na baseline: *"rodar cada arquivo reprovado isolado — se
reprovar isolado, é defeito, não contenção"*.

| medição | resultado |
|---|---|
| suíte inteira, `load` 3,84 | 765/765 — **o que me fez classificar errado** |
| `router.test.tsx` isolado, máquina descarregada | **reprova ~1 em 3** |
| o mesmo teste isolado com `-t` | passa sempre |
| `router.test.tsx` na **baseline** (`b58e6e8`), 6 execuções | **6/6 verde** |

Baseline verde acusa regressão, e acusou.

**A causa, isolada por medição e não por hipótese.** O DOM da falha era a **tela de
login**, e a saída trazia `Not implemented: navigation to another Document` — a
impressão digital de `window.location.href = '/login'` no ramo 401 de
`request<T>`. Para achar qual chamada tomava 401, `VITE_API_BASE_URL` foi
apontado para um servidor que **loga e devolve 200**: as três execuções passaram, e
o log trouxe uma linha só —

```
ESCAPOU: GET /knowledge-bases/indexing-summary
```

`router.test.tsx` usa **mock parcial** (`importOriginal` + override) de
`knowledgeBasesApi` e **não stuba `fetch`**. A função que esta change acrescentou
ao módulo não estava no override, escapou para a rede, e o `apps/api` **real
rodando na máquina** devolveu 401 → `clearToken()` → o teste **seguinte** renderizou
o login.

**Corrigido** acrescentando `listKnowledgeBaseIndexingSummary` ao mock, no mesmo
padrão que `listKnowledgeBases` já usava. Depois: `router.test.tsx` **8/8**.

### Por que isso sobreviveu a três suítes verdes, e é o que vale carregar

- **O mock parcial deixa de cobrir o módulo quando o módulo cresce.** É o espelho
  do risco do mock total que a 5a-2 registrou (lá, a função nova vira `undefined`
  e quebra na hora, alto e claro). Aqui ela vira **uma chamada de rede real**, que
  só falha quando há servidor escutando e só atrapalha o teste **seguinte**.
- **A falha é deslocada no tempo e no arquivo.** Quem reprova não é quem faz a
  chamada. Nenhuma leitura do diff levaria a `router.test.tsx`.
- **E ela depende do ambiente de quem roda.** Com o `apps/api` parado, a chamada
  falha de outro jeito e o efeito muda. A suíte verde na máquina de CI não
  provaria nada sobre a máquina do desenvolvedor.

**A lição de método é sobre a classificação, não sobre o mock:** "a suíte inteira
passou depois" é evidência **fraca** para absolver um teste intermitente, porque a
intermitência é justamente o que uma execução verde não distingue. O passo que
decide é o arquivo isolado, repetido — e ele já estava escrito na convenção 19,
esperando ser usado.

### `format:check` já estava vermelho na baseline, e isso foi medido

A varredura de formatação acusou 14 arquivos. **Classificar "pré-existente" de
vista seria exatamente o que a convenção 19 proíbe**, então a baseline foi
medida: `git worktree` limpo em `b58e6e8`, `prettier --check` no mesmo binário →
**10 arquivos já reprovavam antes desta change**, incluindo dois que ela também
toca (`knowledgeBasesApi.ts` e `useKnowledgeBases.test.ts`).

Formatados **só os seis arquivos que esta change cria ou modifica**. Os outros
oito ficam como estavam.

**A aritmética `10 − 6 = 4` não vale aqui, e o motivo é o registro que importa.**
Os seis formatados não são um subconjunto dos dez: eles se dividem em **três**
categorias, e só uma delas sai da lista da baseline.

| categoria | quantos | quais | saem dos 10? |
|---|---|---|---|
| já reprovavam na baseline **e** esta change os modifica | **2** | `knowledgeBasesApi.ts`, `useKnowledgeBases.test.ts` | **sim** |
| a baseline **aprovava**, e passaram a reprovar por causa desta change | 2 | `KnowledgeBaseTable.tsx`, `KnowledgeBaseListPage.test.tsx` | não — dívida que esta change criou e fechou no mesmo ato |
| **arquivos novos**, que não existiam na baseline | 2 | `indexingSummary.ts` e o teste dele | não |

Logo: **10 − 2 = 8**, e a contagem foi **remedida** com `prettier --check` depois
de tudo pronto, não deduzida — devolveu exatamente os oito nomeados abaixo.

A lição não é o número, é a forma: **"formatei 6 dos 10" é uma frase que parece
uma subtração e não é**, porque "os arquivos que a change toca" e "os arquivos que
reprovavam antes" são conjuntos que se cruzam parcialmente. Registro de dívida com
número derivado por subtração de conjuntos que não se contêm é o que ninguém
reconfere.

### Dívida pré-existente encontrada e NÃO corrigida de carona

`format:check` reprova, na baseline, em oito arquivos que esta change não toca:
`auth/pages/LoginPage.tsx`, `KnowledgeBaseDescriptionCard.tsx` e o teste dele,
`KnowledgeBaseForm.tsx` e o teste dele, `KnowledgeBaseEditPage.tsx` e o teste
dele, e `mcp-servers/utils/agentUsage.ts`.

Não são corrigidos aqui: formatar arquivo que a change não altera é escopo que
ninguém pediu, e embutiria num diff de tela um ruído que dificulta a revisão. Fica
registrado para virar item próprio — e vale notar que **o comando de verificação
está vermelho na main**, o que treina qualquer pessoa a ignorá-lo.

## Medição da convenção 18 — projetado × entregue

**Contagem de arquivo: 11 projetados, 12 entregues — e o arquivo a mais é a
lição.** Os criados bateram exatos (2×2); os modificados saíram **10 contra 9**.

O extra é `src/app/router.test.tsx`, que **nenhuma leitura do código de produção
apontaria**: ele entrou porque mocka `knowledgeBasesApi` **parcialmente**, e a
função nova precisava entrar no override — sem isso, escapava para a rede (ver a
seção do fechamento). Não é escopo acrescentado nem erro de estimativa de esforço:
é **blast radius que a projeção não sabia procurar**.

**Régua nova, e ela é do mesmo tipo das duas que a convenção já carrega** — as
outras duas também eram "o blast radius não está onde você está olhando" (fixtures
de teste ao tornar um campo obrigatório; os N handlers que constroem um response
compartilhado):

> **Acrescentar função exportada a um módulo de API arrasta todo arquivo que
> mocke aquele módulo — inclusive em OUTRA feature.** Enumerar com
> `grep -rl "vi.mock(.*<modulo>"` antes de projetar, como o `tsc` enumera as
> fixtures de graça. Custou 1 arquivo / 16 linhas aqui, e teria custado zero de
> esforço para prever.

Com essa correção, o método de contar criados e modificados em separado segue
válido — ele não errou por fator, errou por **não ter varrido uma dimensão do
blast radius**, e a varredura que faltava é um `grep`.

| | projetado | entregue | erro |
|---|---|---|---|
| criados | 2 arquivos / ~235 linhas | 2 / **356** | **+51%** em linha |
| modificados | 9 arquivos / ~384 linhas | **10** / **772** | **+1 arquivo**, +101% em linha |
| **total** | **11 / ~620** | **12 / 1.128** | **+1 arquivo**, +82% em linha |

**A projeção de linha errou quase por dois, e o fator não é a lição.** Decomposto,
o erro tem **três** causas distintas, e somá-las num multiplicador seria o erro
exato que esta convenção existe para não repetir.

### Causa 1 — o número de cenários, não o custo de cada um

O custo unitário estava certo; a **contagem** estava errada.

| arquivo | cenários projetados | entregues | linhas/cenário |
|---|---|---|---|
| `indexingSummary.test.ts` | 13 | **22** | 8,5 (projetado 11-13) |
| `KnowledgeBaseTable.test.tsx` | 6 novos | **11 novos** | ~18 (projetado ~18) |
| `KnowledgeBaseListPage.test.tsx` | 6 novos | **5 novos** | ~25 |

Os cenários por componente saíram a 18 linhas, exatamente a âncora. Os de função
pura saíram a 8,5 — **abaixo** da faixa projetada. O que estourou foi quantos:
**+9 na função pura e +5 no componente**.

E a causa disso é estrutural e reusável: **cada estado da gramática de três
estados (valor, vazio, `—`) multiplica por três os cenários de cada função que a
atravessa**, e a convenção 5 ainda cobra o par com/sem item por cima. Projetar
"13 cenários" para quatro funções que compartilham uma gramática de três estados
era projetar uma função de um estado só. **Contar cenários = funções × estados da
gramática, não funções × 3.**

### Causa 2 — o comentário causal escala com DECISÕES, não com instruções

`utils/indexingSummary.ts`: 168 linhas, **73 de comentário (43%)**. A âncora
`documentIndexing.ts` tem 91 linhas com **46%** de comentário — a *proporção*
estava certa, e foi por ela que a projeção de ~85 linhas saiu.

O que a proporção não captura: **o tamanho absoluto do bloco é dirigido pelo
número de decisões que o arquivo carrega**, não pelo número de funções. Este
arquivo carrega quatro (a gramática de três estados, o zero medido × zero default,
a fusão de pendente/indexando, e o cruzamento por id) contra uma da âncora.

Mesmo padrão em `KnowledgeBaseTable.tsx`: +168 linhas contra +45 projetadas, com
23% de comentário. Os comentários que explodiram são os que registram **por que a
coluna existe agora** e **por que os badges não entram** — prosa que a projeção
tratou como "mais um comentário de componente".

**Régua para a próxima**: projetar o bloco de comentário de arquivo de regra por
**decisão registrada** (~15-20 linhas cada), somado ao corpo, em vez de aplicar
uma proporção à contagem de código.

### Causa 3 — escopo acrescentado na conferência manual, e ele se conta à parte

`layout="fixed"` com as cinco larguras e o comentário que registra a medição
(A1) **não existia na projeção** — o achado só apareceu ao medir a tela. São ~25
linhas em `KnowledgeBaseTable.tsx` mais 3 cenários de tom que nasceram do mesmo
percurso.

**Medição de método só compara o escopo que estava projetado**, e este pedaço fica
fora da conta de erro — é a mesma disciplina que `frontend-knowledge-base-catalogo`
aplicou ao card de agentes.

### O que sobrevive desta medição

- **O método de contagem de arquivo está confirmado pela terceira vez.** Ele não
  precisa de ajuste.
- **A projeção de linha precisa de duas réguas novas**, as duas estruturais:
  cenários = funções × estados da gramática; comentário de arquivo de regra =
  decisões × ~15-20 linhas.
- **Nenhum fator de correção.** As três causas apontam para direções diferentes e
  uma delas (a conferência) nem deveria entrar na conta; multiplicar por 1,8 na
  próxima change produziria uma projeção errada com aparência de calibrada.

## Risks / Trade-offs

Todo risco listado tem contraparte verificável — cenário na spec e teste, ou a
justificativa de por que não é testável (convenção 10).

| # | Risco | Mitigação | Contraparte verificável |
|---|---|---|---|
| R1 | Alguém "completa" a célula ausente com `0` ou `Nenhum`, e a tela passa a afirmar contagem que ninguém fez | D3 e D4 na spec, com os dois zeros nomeados | Cenário **negativo**: base sem linha no resumo **não** exibe zero nem `Nenhum`, exibe `—`. Visto reprovando com o zero no lugar |
| R2 | O cruzamento passa a ser por posição e ninguém percebe, porque as duas ordens coincidem hoje | D6 | Cenário com o resumo **em ordem diferente** do catálogo, afirmando ordem exibida **e** contagem por linha. É o defeito de `ordenacao-desempate-listas-vinculo` na forma da tela |
| R3 | O filtro `Com falha` filtra para o vazio com o resumo ausente, afirmando que nenhuma base falhou | D7: opção desabilitada + filtro efetivo puro | Cenário: resumo indisponível, `Com falha` selecionado, a listagem **não** fica vazia e o controle está desabilitado |
| R4 | A célula volta a escrever `pendente`/`indexando`, separação que a rota não entrega | D5 | Cenário **negativo**: nenhum arranjo de contagens produz as palavras `pendente` ou `indexando` na coluna |
| R5 | Falha do resumo derruba a listagem inteira | D2 | Cenário: consulta de resumo falha, as linhas de base continuam exibidas |
| R6 | Tom fixo da escala neutra entra num dos arquivos novos dentro de ternário, forma que já furou o guarda | D11 | O próprio `surfaceTokens.test.ts`, **visto reprovando** com um tom fixo reintroduzido de propósito dentro de um ternário (tarefa própria) |
| R7 | A conferência declara convergência sem ter olhado o que importa | convenção 14: conferência iterativa, por tela, nos dois esquemas | Tarefa própria, com os estados nomeados um a um — inclusive o de resumo ausente, que **não** aparece sozinho e precisa de arranjo |

## Migration Plan

Não há migração. Nenhum schema, nenhuma rota, nenhum contrato entre apps muda;
`apps/api` já serve a rota há um dia e continua igual. Rollback é reverter o
commit do frontend: o catálogo volta a três colunas e três opções de filtro, e
nenhum outro consumidor percebe.

O requisito de spec que vira do avesso é o único registro a acompanhar, e ele vai
no delta desta change.

## Open Questions

Nenhuma. As três incertezas que o enunciado levantou foram fechadas contra o
código e contra o protótipo percorrido, e não sobrou pergunta de negócio:
composição das duas requisições (D2), comportamento do filtro sem resumo (D7) e
preservação da ordem do catálogo (D6).
