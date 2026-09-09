## Context

Etapa **5a-1** da linha de bases de conhecimento: o catálogo na UI de
`apps/frontend`. O backend está pronto e não é tocado.

### Protótipos

O handoff vive em `design/`, cobrindo **inclusive as telas que esta change não
implementa**, para que a etapa 2 seja proposta sabendo o que a tela vai exigir
dela. Anexado com curadoria, não por inteiro: dois dos seis arquivos do handoff
estavam superados de um jeito que induz ao erro e foram removidos, com o motivo
em D15 — `design/` sobrevive ao archive e será lido pelas etapas seguintes sem
nenhuma marca de contexto.

| Arquivo | O que é |
|---|---|
| `design/Buteco Agentes.dc.html` | Protótipo navegável, 151.396 bytes, cópia byte-exata do projeto Claude Design `Sistema Gestão de Agentes` (`e4f9bbd6-dd31-4ff1-954b-0e686d2eaa54`). Abre no navegador com `support.js` ao lado. |
| `design/support.js` | Runtime do protótipo. Sem ele o `.dc.html` não renderiza. |
| `design/CONHECIMENTO.md` | Especificação de design da linha, **revisão 2 de 09/09/2026** — a válida. Cobre 5a-1, 5a-2, 5b e 5c. |
| `design/README-painel.md` | Handoff do redesenho já implementado. Vale pelos padrões gerais e pela nota de 09/09/2026 sobre o card A2A, que sustenta a decisão da seção 4.1. |

Telas por nome, para as tarefas referenciarem sem descrição vaga:
**Catálogo de bases**, **Detalhe da base**, **Formulário de base** (5a-1);
**Tabela de documentos**, **Modal de documento** (5a-2);
**Aba Conhecimento do agente**, **Modal Vincular base** (5b);
**Diagnóstico do índice** (5c).

### Verificação prévia (convenção 6)

Lido no código antes de decidir qualquer coisa, não assumido:

| Precisa de | Existe em | Situação |
|---|---|---|
| Card seccionado | `components/data/SectionedCard.tsx` (`.Row`, `.Body`) | reusar |
| Rótulo de seção | `components/data/SectionLabel.tsx` | reusar |
| Cabeçalho de detalhe | `components/layout/DetailHeader.tsx` | reusar |
| Volta para a listagem | `components/layout/BackLink.tsx` | reusar |
| Aparência de badge | declarada no tema (`frontend-acabamento-telas`) | reusar |
| Lista com busca e filtro | `features/mcp-servers/pages/McpServerListPage.tsx` + `McpServerTable.tsx` | molde |
| Query/mutation + cache | `features/mcp-servers/api/useMcpServers.ts` | molde |
| `request<T>` / `ApiError` | `features/mcp-servers/api/mcpServersApi.ts`, importando `auth/token` | molde, **copiar não compartilhar** (D12) |
| Visão inversa de `GET /agents` | `features/mcp-servers/utils/agentUsage.ts` | molde — **não usado nesta etapa** (D2) |
| Guarda de navegação | `hooks/useUnsavedChangesGuard.ts` | **não usar** (D11) |
| Ícones | `lucide-react`, já no `package.json` | reusar |

### Superfície real da API

`KnowledgeBaseResponse` (`apps/api/src/Buteco.Api/KnowledgeBases/Responses/KnowledgeBaseResponse.cs:5`)
tem exatamente seis campos: `Id`, `Name`, `Description`, `IsActive`, `CreatedAt`,
`UpdatedAt`. Seis rotas, sem `DELETE` — o comentário em
`KnowledgeBaseEndpoints.cs:27` registra que base é entidade de catálogo e segue o
padrão `IsActive` de `McpServer`.

`Description` é obrigatória e não vazia na criação **e** na edição
(`ValidateShape`, `KnowledgeBaseEndpoints.cs:122`), e o campo do response é
`string` não anulável.

## Goals / Non-Goals

**Goals:**
- Catálogo, detalhe, criação e edição de base de conhecimento, com a descrição
  apresentada como o texto de runtime que é.
- Toda divergência entre protótipo e código resolvida por decisão explícita,
  com o número da convenção que a sustenta (convenção 17).
- Handoff registrado do que a etapa 2 precisa entregar, antes de ela ser
  proposta.
- Conferência manual como tarefa própria e iterativa, nos dois esquemas de cor
  (convenção 14).

**Non-Goals:**
- Gestão de documentos (5a-2), vínculo no detalhe do agente (5b), diagnóstico do
  índice (5c).
- Qualquer mudança em `apps/api`. Se a tela precisar de dado que o backend não
  serve, isso é achado a reportar e sequenciar, nunca backend de improviso dentro
  de change de tela (convenção 1).
- Extração de componente compartilhado novo. Nada aqui tem os 2-3 consumidores
  reais que a convenção 2 exige.

## Decisions

### D1 — As colunas `Documentos` e `Indexação` do Catálogo de bases não entram

O protótipo (`Buteco Agentes.dc.html`, cabeçalho
`Base | Documentos | Indexação | Consultada por | Estado`) mostra
`"{n} documentos"` e `"3 indexados · 1 falhou"` por linha.

**`KnowledgeBaseResponse` não tem nenhuma contagem.** Documentos vivem em
`GET /knowledge-bases/{knowledgeBaseId}/documents`
(`KnowledgeDocumentEndpoints.cs:18`) — uma requisição **por base**. O próprio
handoff informa volume real de **100+ bases** (`CONHECIMENTO.md`, seção 4.2),
então preencher essas duas colunas custaria 100+ requisições para desenhar uma
lista.

Alternativas descartadas:
- **N+1 no cliente** — inviável no volume que o handoff declara.
- **`"0 documentos"` / `"Nada indexado"` como padrão** — afirmaria que a
  contagem foi feita e deu zero. É exatamente o defeito de `0 fragmentos` (D5),
  e a convenção 13 o proíbe.

**Decisão: as duas colunas ficam fora**, e o que falta vai como handoff para a
etapa 2 (contagem em `KnowledgeBaseResponse`, ou rota de resumo). Sustenta:
convenção 13 (a UI nunca afirma mais do que o sistema sabe) e convenção 1 (o
dado que falta é achado a sequenciar, não backend improvisado aqui).

### D2 — A coluna `Consultada por` fica para a 5b, e a dependência dela já está satisfeita

A coluna é a visão inversa "quais agentes consultam esta base", no molde de
`agentUsage.ts`. Ela é **derivável hoje**: `AgentResponse` já traz
`IReadOnlyList<KnowledgeBaseSummaryResponse> KnowledgeBases`, e
`AgentKnowledgeBindingEndpoints.cs:22` registra explicitamente que não existe
rota inversa porque a derivação no cliente é o padrão da casa.

Custo: **uma** requisição a `GET /agents`, não N.

Ainda assim fica fora, porque a 5b é a etapa da visão inversa e partir dela em
duas telas espalharia a mesma derivação por duas changes antes de haver um
consumidor. Registro o achado que importa: **a dependência que a 5b declarava —
"depende da derivação a partir de `GET /agents`" — já está pronta no backend.**
A 5b não está bloqueada.

### D3 — O Detalhe da base não nasce com abas

O protótipo tem duas abas: `Documentos` (com contador) e `Diagnóstico do índice`.
A primeira é 5a-2, a segunda é 5c. **Nesta etapa nenhuma das duas existe.**

- Nascer aqui com uma aba só: uma barra de abas com um item afirma uma estrutura
  que a tela não tem, e o operador procura a segunda aba que não existe.
- Nascer na 5a-2: ainda seria uma aba só, porque `Diagnóstico do índice` só
  chega na 5c. O problema não se resolve, apenas muda de change.

**Decisão: as abas nascem na 5c, quando existirem duas de verdade.** Até lá o
detalhe é página plana, e a 5a-2 acrescenta os documentos como seção. O custo é
um refactor de layout na 5c — envolver seções existentes em `Tabs` — assumido
por ser menor que exibir estrutura falsa por duas etapas. Sustenta: convenção 13.

### D4 — No lugar dos documentos, uma nota de sequenciamento; nunca um estado vazio

O detalhe sem a área de documentos precisa dizer alguma coisa. `"Nenhum
documento nesta base"` afirmaria que a base foi consultada e está vazia — mesmo
raciocínio de D1 e D5.

**Decisão:** uma linha, no lugar da seção, dizendo que a gestão de documentos
desta base chega na etapa seguinte. Ela é uma afirmação sobre o roadmap do
painel, não sobre o conteúdo da base — não há como confundi-la com contagem.
Silêncio total foi descartado: o operador que abre a base vem justamente pelos
documentos, e nada na tela explicaria a ausência.

### D5 — `0 fragmentos` é defeito do protótipo, e a correção é maior que a relatada

No protótipo:
`chunks: st === 'indexed' ? d.chunks + ' fragmentos' : st === 'failed' ? '0 fragmentos' : '—'`.
Ou seja `pending`/`indexing` → `'—'`, e **`failed` → `'0 fragmentos'`**.

Isso contraria a spec viva de `knowledge-document-catalog`: informação derivada
da indexação é exibida quando `indexedAt` não é nulo e **omitida** quando é
nulo — nunca zerada, que afirmaria que a indexação rodou e não achou nada.

**E a correção é maior do que trocar `failed` por `—`:** não existe contagem de
fragmentos em lugar nenhum da API. `grep -ri "fragment\|chunk"` em
`apps/api/src` e `apps/workers/src` só acha **comentários** sobre a fragmentação
futura (`KnowledgeDocument.cs:26`, `MarkdownSourceExtractor.cs:7`). Nem
`KnowledgeDocumentResponse` nem `KnowledgeDocumentSummaryResponse` têm o campo.
A coluna `Fragmentos` não tem o que exibir em **nenhum** dos quatro estados.

**Decisão:** é tela da 5a-2, mas as duas metades vão para o handoff agora — (a)
correção do protótipo, `failed` → célula vazia; (b) requisito para a etapa 2, o
campo de contagem.

### D6 — O alerta de falha é requisito para a etapa 2, e a última frase sai

O protótipo traz, na semente de dados: *"O provedor de embedding devolveu 429
(rate limit) nas três tentativas de indexação deste documento, a última em
01/09/2026 às 03:14. […] Reindexar volta a enfileirá-lo; documentos muito
grandes tendem a bater no limite."*

Contra o código:
- **`FailureReason` já existe** (`string?`, nos dois responses de documento). Não
  é campo novo — o que falta é ele ser legível por operador em vez de exceção
  crua, e isso é decisão da etapa 2.
- **Contagem de tentativas e instante da última tentativa não existem.** Nenhum
  campo em `KnowledgeDocument`. "Nas três tentativas, a última às 03:14"
  pressupõe política de retry com contador e carimbo persistidos.
- **Não existe rota de reindexação.** `KnowledgeDocumentEndpoints` tem `POST /`,
  `GET /`, `GET /{id}`, `PUT /{id}` e `DELETE /{id}`, mais nada. Pela convenção 1
  essa rota não pode nascer dentro de uma change de tela.

Não é defeito do protótipo — é requisito que ele gera. Vai como handoff.

**A frase final sai.** "Documentos muito grandes tendem a bater no limite" é
conselho que o sistema não verifica: nada correlaciona tamanho com falha de
embedding, e a própria semente do protótipo aplica a mesma cópia a um documento
pequeno. Convenção 13. Não vira nem texto estático de ajuda — sem medição, é
palpite com aparência de diagnóstico.

### D7 — Descrição obrigatória: o backend já impõe, e o callout de descrição ausente não se implementa

A divergência 2 do `CONHECIMENTO.md` diz "se o backend aceitar descrição vazia, a
validação é só do cliente". **Ele não aceita**: `ValidateShape` rejeita nome ou
descrição nulos, vazios ou só de espaços, com 400 e
`ValidationProblemDetails`, na criação e na edição. A validação do cliente é
espelho da do servidor, não invenção dela.

**Consequência que o handoff não previu:** o protótipo tem dois caminhos para
base sem descrição — callout âmbar no detalhe (*"Sem descrição. O modelo recebe
apenas o nome da base…"*) e texto na linha do catálogo (*"Sem descrição — o
modelo não sabe quando consultar."*) — mais o cenário `Rotinas Internas`,
descrito como "inativa, sem descrição, sem documento".

**Esse estado é inalcançável.** Nenhuma rota cria ou deixa uma base com
descrição vazia, e o campo do response é não anulável.

**Decisão: nada disso é implementado.** Código para um estado que a API impede é
código morto que passa a impressão de cobertura. O aviso de contador abaixo de 80
caracteres **fica**, porque descrição curta é alcançável e é conselho sobre o
texto que o modelo lê, não afirmação sobre o sistema.

### D8 — Ícone e posição do item de navegação

O protótipo usa placeholder de letra (`mono: 'K'`), e o `README-painel.md` manda
substituir pelo icon set do painel. O prompt de implementação do handoff — que
saiu de `design/`, ver D15 — sugeria `BookOpen` ou `Library`.

O código usa `lucide-react` com `Bot`, `Server`, `MessagesSquare`
(`AppShell.tsx:10`) — nunca Tabler, ao contrário do que o `README-painel.md`
supõe.

**Decisão:** `BookOpen` do `lucide-react`. Posição: **terceira, antes de
Canais**, como no protótipo (`nav` = Agentes, Servidores MCP, Conhecimento,
Canais). `Library` foi descartado por ler como "coleção de coleções", que é o
catálogo inteiro e não a área.

### D9 — O filtro é `Todas · Ativas · Inativas`, sem `Com falha`

O protótipo oferece quatro opções, a quarta sendo `Com falha`. Ela filtra por
estado de indexação agregado por base — o mesmo dado que D1 elimina. Sem a
coluna `Indexação` não há como computar o filtro, e oferecê-lo vazio ou inerte
seria pior que não oferecê-lo.

**Decisão:** três opções, no molde exato de `frontend-listas-busca-e-colunas`. A
quarta volta quando a etapa 2 entregar a contagem. Consequência direta de D1,
registrada em separado porque é a decisão que alguém tentaria "corrigir" sem ver
a causa.

### D10 — Nenhum papel visual novo, nenhum tom fixo

A convenção 16 registra três ocorrências do mesmo defeito no redesenho: papel
visual apoiado em `gray[n]`/`dark[n]` fixo, que é claro nos dois esquemas ou
escuro nos dois. Os papéis desta etapa — superfície de card, faixa de cabeçalho,
badge de estado, texto mudo, borda esquerda accent da citação da descrição —
**todos já existem** declarados por esquema depois de
`2026-09-06-frontend-acabamento-telas` e `2026-09-06-frontend-tema-identidade-visual`.

**Decisão: nenhuma variável de tema nova, nenhum `gray[n]`/`dark[n]` literal em
componente.** O guarda estático que já varre tons fixos de superfície passa a
cobrir os arquivos novos por construção. Convenção 15: esse guarda já passou
verde com o defeito presente uma vez (a expressão não cobria valor dentro de
ternário), então a tarefa de conferência inclui reintroduzir um tom fixo dentro
de um ternário num dos arquivos novos e ver o guarda reprovar — não basta ele
estar verde.

### D11 — O Formulário de base não recebe guarda de navegação

A pergunta era se o formulário precisa de `useBlocker` no molde do detalhe do
agente. `grep -rln "useUnsavedChangesGuard\|useBlocker"` em
`apps/frontend/src` devolve exatamente dois consumidores de tela:
`AgentToolsTab.tsx` e `AgentDelegationsTab.tsx`.

**Nenhum formulário do painel usa guarda** — nem `AgentForm`, nem
`McpServerForm`, nem `ChannelForm`, nem as páginas de criação e edição que os
usam. O padrão da casa é: guarda protege **rascunho de vínculo dentro de abas**,
onde a troca de aba é mudança só de query string e perde trabalho sem sair da
rota. Formulário tem `Cancelar` explícito e uma única saída.

**Decisão: sem guarda.** Pôr um aqui seria padrão novo em change de tela, não
reuso — e a convenção 2 pede repetição já observada, que não há.

### D12 — `request<T>` próprio, contra a sugestão do handoff

O prompt de implementação do handoff (removido de `design/`, ver D15) sugeria
extrair `request<T>` para `src/api/httpClient.ts` compartilhado. **A convenção 7 proíbe exatamente isso:** cada feature mantém seu
próprio `request<T>`/`ApiError` fino, sem cliente HTTP compartilhado entre
features; a preocupação transversal (token) entra como módulo fino que cada
`request<T>` importa.

**Decisão:** `features/knowledge-bases/api/knowledgeBasesApi.ts` com
`request<T>`/`ApiError` próprios, importando `src/auth/token`. Convenção 7 vence
o handoff, que foi escrito sem acesso ao código — é o caso da regra 4 das quatro
perguntas do próprio handoff ("o padrão do codebase ganha, e eu quero saber qual
foi").

### D13 — O item de flakes estava obsoleto, não resolvido por acaso: a causa já estava registrada

`02-HISTORICO_E_STATUS.md` lista, em "Itens em aberto", flakes
não-determinísticos em `AgentForm`/`ChannelForm`/`McpServerForm` e nas páginas de
criação e edição (timing de `userEvent`, contagem variando entre 14 e 15), com
gatilho "qualquer trabalho futuro em `apps/frontend`". Esta change é esse
gatilho, e a convenção 19 manda rodar a baseline antes de classificar qualquer
coisa.

Rodada em `main` limpo, quatro vezes: **54 arquivos / 451 testes, verde nas
quatro.**

**Mas a medição não é a explicação.** O mesmo arquivo já registra a causa, em
outro ponto: a instabilidade da suíte do frontend foi **fechada de arrasto** por
`2026-09-06-agente-enderecos-a2a`, decisão **D8** e tarefa 4.6 daquela change,
por estar impedindo verificá-la. A causa era prazo, não lógica: os testes de
formulário abrem dropdowns que montam em portal depois de uma transição,
enquanto 54 arquivos rodam em paralelo, e o prazo padrão de **um segundo** das
consultas assíncronas do Testing Library acabava antes de a opção existir. O
prazo das consultas subiu para cinco segundos e o do teste para quinze. O
`notes.md` daquela change registra **cinco execuções completas seguidas, todas
verdes**, depois da correção.

Ou seja: o item em "Itens em aberto" estava **obsoleto** desde 06/09 — ninguém o
riscou quando a correção entrou, porque ela entrou de arrasto em uma change de
outro assunto.

**Decisão: não entra no escopo nem vira carve próprio, e o registro liga os dois
pontos do arquivo.** O item em aberto passa a apontar para D8 de
`agente-enderecos-a2a` como a correção que o resolveu, e as quatro rodadas desta
change entram como **confirmação** — a sexta à nona execução verde contando as
cinco daquela change —, nunca como explicação. Fechar por medição
sem ligar à correção deixaria o histórico dizendo "parou de acontecer", que é
exatamente a forma de registro que a convenção 19 desqualifica: baseline não
promove sintoma a resolvido, e ausência de falha não é causa.

O que a baseline verde dá, e vale carregar para o apply: **qualquer falha da
suíte durante esta change é regressão dela**, não flake pré-existente.

### D14 — As quatro perguntas do handoff, respondidas contra o código

O handoff pedia confirmação de quatro pontos antes de codar. O arquivo que os
listava saiu de `design/` (D15), então as respostas ficam aqui — que é onde elas
precisam estar de qualquer forma, para a 5a-2 e a 5b não as reabrirem:

1. **Contrato do vínculo:** só `PUT /agents/{id}/knowledge-bases`, substituição
   do conjunto inteiro (`AgentKnowledgeBindingEndpoints.cs:21`). Não há `PUT` nem
   `DELETE` por base. Vale a saída que o próprio handoff previu: manter a UI de
   ação por linha, enviando o conjunto resultante a cada ação.
2. **`GET /agents` devolve as bases vinculadas:** sim, `AgentResponse.KnowledgeBases`,
   já entregue e mesclado. **Não é opcional** — o campo está no response desde
   `2026-09-08-knowledge-base-vinculo-agente`, então `knowledgeBases?:` com
   tratamento de `undefined` não se justifica, ao contrário de `a2a?:`.
3. **Upload de arquivo:** não existe. `POST /knowledge-bases/{id}/documents`
   recebe JSON. Não há rota `/upload`, nem `multipart/form-data`, nem
   `/diagnostics`, nem `/reindex`. A 5a-2 entra **só com o modo manual**, ou
   sequencia o upload no backend antes.
4. **Conflitos com o codebase:** D7, D8, D11 e D12.

### D15 — Dois anexos saíram de `design/`, e o motivo fica registrado

`design/` sobrevive ao archive e será lido pelas etapas 5a-2, 5b e 5c sem
nenhuma marca de contexto. Dois dos arquivos do handoff estavam superados de um
jeito que induz ao erro, e um arquivo que precisa de nota de rodapé para **não**
ser seguido é pior que a ausência dele. Removidos, com o motivo aqui em vez de
apagados em silêncio.

**`AJUSTES_VISUAIS.md`** — abre afirmando que `theme.ts` está vazio
(`createTheme({})`), que "não há identidade para convergir" e que "a partir de
agora o protótipo **É** a identidade visual do painel", e manda repintar o
painel inteiro num commit isolado. Era verdade quando foi escrito e ficou falso
quando `2026-09-06-frontend-tema-identidade-visual` foi aplicada. O painel tem
tema, paleta, tipografia, raios e aparência de badge declarados há oito etapas:
a identidade deixou de ser decisão do desenho e virou decisão do sistema (é o
que D10 usa). Mantido em `design/`, o arquivo autorizaria a próxima etapa a
tratar o protótipo como fonte da identidade — exatamente o contrário da
convenção 16.

**`PROMPT_CLAUDE_CODE.md`** — dois motivos. É um prompt de implementação direta,
e quem implementa aqui é o fluxo OpenSpec; o que vale dele (especificação de
tela, cópia, comportamento) já está no `CONHECIMENTO.md`, e as quatro perguntas
que ele pedia confirmar estão respondidas em D14. E ele especifica **quatro
coisas contra decisões já fechadas**, três delas fechadas na etapa 1:

| O que o arquivo pede | Contra o quê |
|---|---|
| Fase 5 com `multipart/form-data`, rota `/documents/upload`, e modificar `request<T>` para omitir `Content-Type` | **D3 da etapa 1**, "Zero multipart, com gatilho registrado": markdown e `.txt` são texto, o cliente lê com `FileReader` e envia string no mesmo corpo JSON |
| `PUT`/`DELETE /agents/{agentId}/knowledge-bases/{kbId}` | O backend tem replace-all de conjunto (D14, ponto 1) |
| `GET /knowledge-bases/{id}/diagnostics` | Não existe nem foi projetado (D14, ponto 3) |
| Extrair `request<T>` para `src/api/httpClient.ts` compartilhado | D12, convenção 7 |

O caso do multipart é o mais consequente, porque a 5a-2 é a etapa que o leria: o
`design.md` da etapa 1 já registra, no ponto em que decide, que "acrescentar o
extrator de PDF leria ali a instrução errada". Vai para o handoff como tarefa
própria (7.4).

**Os três mantidos**: `CONHECIMENTO.md` (revisão 2, a especificação válida das
telas das quatro etapas), `Buteco Agentes.dc.html` e `support.js` (o protótipo
navegável, necessário para a conferência manual da tarefa 6).

**`README-painel.md` também fica**, com ressalva. Ele é o handoff do redesenho
já implementado, e vale por duas coisas que nada mais carrega: os padrões gerais
do painel que a tela de conhecimento reusa, e a nota de 09/09/2026 sobre o card
A2A, que é o que sustenta a seção 4.1 do `CONHECIMENTO.md` (a coluna direita da
Visão geral tem quatro cards, e é isso que empurra qualquer seção inline para
fora da dobra). Diferente dos dois removidos, ele não autoriza nada contra uma
decisão fechada — descreve o que já foi feito. Os dois pontos em que envelheceu
estão marcados no cabeçalho do próprio arquivo: sugere ícones Tabler (o código
usa `lucide-react`, ver D8) e descreve identificação de operador no rodapé da
barra lateral (removida pela divergência 4 do `CONHECIMENTO.md`, porque o login
devolve credencial e validade, não nome nem e-mail).

### D16 — As datas existem no response, e a spec da etapa 1 tem lacuna dupla

Conferido no código, não na tarefa. **As duas datas estão no response:**
`KnowledgeBaseResponse` (linhas 10-11) declara `DateTimeOffset CreatedAt` e
`DateTimeOffset UpdatedAt`, e `FromEntity` as popula. Então a tarefa 1.1 está
certa ao falar de seis campos, e o cenário do detalhe que as exibe **fica**.

Mas a conferência achou mais do que a pergunta pedia. A **spec viva** de
`knowledge-base-catalog` não menciona as datas em lugar nenhum: os cenários de
listagem e de consulta por id dizem "id, nome, descrição e `isActive`". E
`grep -rn "createdAt\|CreatedAt"` nos testes de conhecimento de `apps/api`
(`Knowledge/`, mais `AgentKnowledgeBindingEndpointsTests`) **não devolve nada** —
`KnowledgeWireFormatTests` cobre `indexingStatus`, `sourceType` e
`contentLengthBytes`, que são campos de documento, não as datas da base.

A lacuna é **dupla**: dois campos expostos no fio sem requisito **e** sem
asserção. Não é problema desta change — é da etapa 1 —, e a convenção 1 impede
consertá-lo aqui.

**Decisão: a spec desta change segue como está, e a lacuna vai como item em
aberto** para `02-HISTORICO_E_STATUS.md` (tarefa 7.6).

> **Revisto em D20.** O card de datas saiu do detalhe na conferência, então a
> spec desta change deixou de requerer os dois campos. A lacuna não some — piora:
> `createdAt` e `updatedAt` passam a não ter requisito nem asserção em nenhum
> lugar do repositório. O item em aberto continua, com esse agravante.

O risco concreto que ela cria, e que é o motivo de registrar em vez de ignorar:
esta change passa a ser o **primeiro** lugar do repositório com requisito sobre
as datas, e o requisito está do lado da UI. Se alguém removesse `CreatedAt` ou
`UpdatedAt` de `KnowledgeBaseResponse`, nenhum teste de `apps/api` reprovaria —
só o painel quebraria, e por um campo que a spec do backend nunca prometeu. É
exatamente a forma da convenção 12: contrato entre lados inclui o campo, e
defeito de formato pertence a quem expõe.

### D17 — `DetailHeader` ganhou `showDescription` (achado na implementação)

Registrado por convenção 9: a decisão mudou por achado técnico durante a
implementação, e o `design.md` carrega a causa real em vez de deixá-la só no
resumo da sessão.

D7 decide que a descrição **não** é o subtítulo do cabeçalho. Ao montar o
detalhe, `DetailHeader` mostrou que não tem esse estado: omitir a prop
`description` não omite a linha — ela renderiza o fallback **"Sem descrição."**
(`DetailHeader.tsx`, `{description || 'Sem descrição.'}`). O cabeçalho da base
afirmaria exatamente o que a API impede, que é o defeito de D7 pelo avesso.

As três saídas, e por que a terceira:
- **Passar `description` também** — duplica o texto na tela e o faz parecer
  decorativo. É o que D7 proíbe.
- **Compor o cabeçalho à mão** nesta página — recria título, badge, ações e
  volta que o componente já resolve. A convenção 2 registra que esses três
  padrões saíram de cópias contadas; recopiar seria desfazer isso.
- **`showDescription?: boolean`, default `true`** — os três consumidores
  existentes (`AgentDetailPage`, `McpServerDetailPage`, `ChannelDetailPage`) não
  mudam de comportamento, e dois testes que dependem do fallback
  (`DetailHeader.test.tsx`, `AgentDetailPage.test.tsx`) seguem verdes sem
  edição. Verificado: `grep -rn "Sem descrição"` acha os dois.

A prop distingue "não há descrição a exibir" de "esta página não tem subtítulo",
que é o que o componente conflatava. A segunda existe porque o subtítulo que o
protótipo propõe para esta tela — "{n} agentes consultam esta base · {resumo de
indexação}" — depende dos dois dados que D1 e D2 eliminaram: a página realmente
não tem subtítulo.

**Consequência de tamanho, e é ela que corrige a projeção:** `DetailHeader.tsx` e
`DetailHeader.test.tsx` entraram na conta de modificados, que o design projetava
como 2 arquivos. Ver a medição abaixo.

### D18 — Achados da conferência manual (convenção 9)

Primeira rodada da conferência da tarefa 6, feita pelo operador contra o
protótipo. Registrado aqui porque a conferência mudou o código, e a convenção 9
manda a causa real vir para o `design.md` em vez de ficar no resumo da sessão.

**Falso alarme, e vale registrar para ninguém reabrir:** a lista apareceu "sem
busca e sem filtro". Não é defeito — era o catálogo vazio. O protótipo faz a
mesma coisa: define `kbHasCatalog: list.length > 0` e envolve o bloco de busca e
filtro em `<sc-if value="{{ kbHasCatalog }}">`; com o tweak `catalogoVazio`
ligado, `baseList()` devolve `[]` e os dois somem. A implementação usa
`hasKnowledgeBases`, o mesmo molde de `AgentListPage` e `McpServerListPage`, e o
cenário "não oferece busca nem filtro quando o catálogo está vazio" cobre isso.
Nada mudou.

**Três divergências reais, todas de cópia, todas corrigidas na direção do
protótipo.** As três eram divergência **silenciosa** — eu escrevi cópia própria
onde o protótipo já tinha uma, sem decisão registrada, que é exatamente o que a
convenção 17 chama de errado nos dois sentidos (nem fidelidade cega, nem
implementação silenciosa). Não havia motivo para contrariar:

| onde | protótipo | estava | agora |
|---|---|---|---|
| Título da tela | `<h1>Bases de conhecimento</h1>` | `Conhecimento` | protótipo |
| Subtítulo | `{n} bases · o agente consulta sob demanda, como ferramenta` | `{n} bases cadastradas · consultadas sob demanda pelos agentes vinculados a elas` | protótipo |
| Cópia do vazio | `Uma base guarda documentos markdown que o agente consulta durante a conversa, quando julga que o assunto está ali.` | texto próprio, mais longo, sem citar markdown | protótipo |

A do título é a que mais importa: o protótipo separa de propósito o **rótulo da
navegação** (`Conhecimento`, curto, para a barra lateral) do **título da página**
(`Bases de conhecimento`, preciso). Eu colapsei os dois no rótulo curto. A volta
para a listagem seguiu o mesmo caminho — `← Bases de conhecimento`, como no
protótipo, nas três telas que voltam para ela.

A cópia do vazio era pior do que só diferente: a minha dizia "em vez de só com o
system prompt", que explica o **contraste** com o comportamento sem base; a do
protótipo diz "documentos markdown" e "quando julga que o assunto está ali", que
explica **o que é** e **quando é consultada**. A segunda responde a pergunta de
quem está diante de um catálogo vazio.

**O que a conferência confirma sobre a convenção 14:** a suíte estava verde nas
três divergências e continuou verde depois da correção — cópia de tela é
exatamente o que jsdom não julga. Quatro cenários precisaram de ajuste só porque
citavam a cópia antiga, incluindo dois regexes `/Conhecimento/` que deixaram de
casar por caixa quando o título virou "Bases de conhecimento". Nenhum deles teria
apontado o problema sozinho.

### D19 — O formulário foi remontado na estrutura do protótipo, menos o nome de tool

Segunda rodada da conferência (tarefa 6.2). O formulário estava funcionalmente
certo e **estruturalmente errado**: dois campos soltos num card e um preview
vazio embaixo, contra um protótipo que organiza a tela inteira em torno de uma
ideia — a descrição é o único campo cujo leitor é o modelo.

O que faltava, tudo implementado agora:

| elemento | o que faz |
|---|---|
| Orientação do nome | "Identifica a base nas listas e no vínculo com o agente." — diz que este campo é para pessoas, o que só faz sentido em contraste com o outro |
| **Bloco da descrição com borda esquerda accent** | tira a descrição da lista de campos e a marca como coisa de outra natureza |
| Parágrafo de orientação | "Não é um resumo para o operador… Diga **que assunto está aqui** e **em que situação consultar**. Evite 'documentos diversos'…" — é o que transforma o campo em decisão, e não em legenda |
| Contador de caracteres | sempre visível, com sufixo "· curto demais para o modelo decidir com segurança" e cor de aviso abaixo de 80 |
| Preview **dentro** do bloco | é o que fecha o argumento; solto embaixo, como estava, ele era decoração |
| Fallbacks do preview | "nome da base" e "Sem descrição: o modelo recebe só o nome e decide no chute quando consultar." — o vazio explica a consequência |
| Nota de documentos | "Documentos são carregados depois de criar a base, na tela de detalhe. Por enquanto só markdown…" |

**O que foi recusado: o identificador `consultar_base`.** O protótipo mostra, no
preview, `consultar_base — {nome da base}`, com cara de assinatura de função.
Esse nome **não existe em lugar nenhum**: `grep -rn "consultar_base"` em
`openspec/specs/` e nas duas changes arquivadas da linha não devolve nada. Nome
de tool de base de conhecimento é decisão da **etapa 4** (resolvedor de tool), e
qualquer nome que ela escolha passa pelo `ToolNameDeduplicator` — o mesmo
mecanismo que transforma o servidor MCP *"Informações Gerais"* em
`Informa__es_Gerais__get_menu_info`. Exibir `consultar_base` hoje afirmaria um
nome que o sistema não definiu e que, quando definir, provavelmente não será
esse. Convenção 13, e é a sexta divergência da série que ela decide.

O que ficou no lugar é o que é verdade e já está escrito na entidade: **o modelo
recebe o nome e esta descrição** — a descrição da base "vira a descrição da tool
exposta ao modelo" (`KnowledgeBase.cs`, e a spec viva de
`knowledge-base-catalog`). O preview mostra os dois, em monoespaçada, sem
inventar identificador. Coberto por asserção negativa.

**Largura:** o protótipo limita o formulário a 680px; a implementação ocupa a
largura da tela, aprovado na conferência. Os blocos de prosa ficam limitados a
620px mesmo assim — parágrafo com 1600px de linha não se lê, e o protótipo já
limitava a orientação a 600px por dentro pelo mesmo motivo.

### D20 — Card de agentes entra em escopo; card de datas sai (conferência, terceira rodada)

Duas correções vindas da conferência do detalhe, decididas pelo operador. As
duas eram divergência do protótipo; uma por sequenciamento declarado, outra
minha e silenciosa.

**O card "agentes que consultam esta base" entra.** D2 o deixou fora porque a
proposta original o listava como escopo da 5b. A conferência reabriu a questão
com o dado que D2 já tinha registrado: **a dependência estava pronta**.
`AgentResponse.KnowledgeBases` é populado desde `knowledge-base-vinculo-agente`,
então a derivação custa **uma** requisição a `GET /agents`, não N — o mesmo
padrão de `McpServerAgentsCard`. Não havia obstáculo técnico; havia
sequenciamento, e o sequenciamento foi revisto.

D2 continua valendo para a coluna `Consultada por` do catálogo, que não foi
pedida e segue fora. *(Caiu depois, em D21.)*

**A lição de método está registrada em `02-HISTORICO_E_STATUS.md`**, junto da
change: a proposta declarou algo fora de escopo por dependência **sem conferir a
dependência**, e quem corrigiu foi a conferência manual — tarde. É o inverso da
convenção 1, não um caso dela. Ainda não é convenção: o repositório tem uma
ocorrência deste erro e um contra-exemplo que a delimita (o card A2A, excluído
por dependência que **estava mesmo ausente** e sequenciada corretamente). O
gatilho e o corolário candidato estão escritos lá, para a segunda ocorrência ter
onde se somar.

Custo real, medido: `utils/agentUsage.ts` + `components/KnowledgeBaseAgentsCard.tsx`
+ dois testes, mais o campo `knowledgeBases` no tipo `Agent` do frontend, que
**não existia**. Esse último tem blast radius próprio: obrigatório (não opcional
como `a2a` — não há janela de migração, os dois lados já implantaram, D14 ponto
2), o que quebrou **22 fixtures em 21 arquivos de teste**. Enumerados pelo `tsc`,
corrigidos mecanicamente, todos verdes.

**Duas recusas dentro do card:**

- **A cópia do vazio.** O protótipo diz "Nenhum agente consulta esta base.
  Vincule-a na **Visão geral** de um agente." Duas coisas erradas hoje: a
  **revisão 2 do próprio handoff** moveu o vínculo da Visão geral para uma aba
  (`?tab=conhecimento`, seção 4.1), e essa aba é a **etapa 5b** — não existe
  tela nenhuma para vincular. Mandar o operador a um lugar que não existe afirma
  capacidade que o painel não tem. Ficou: "Nenhum agente consulta esta base. O
  vínculo com agentes chega na próxima etapa.", o mesmo idioma do placeholder de
  documentos (D4). Convenção 13.
- **Falha ao carregar agentes não vira "nenhum agente".** `GET /agents` que não
  respondeu não é evidência de ausência de vínculo. O card diz que não foi
  possível carregar, e o resto do detalhe continua servindo — mesmo tratamento de
  `McpServerAgentsCard`. Coberto por asserção negativa.

**O card de datas sai.** Era acréscimo meu, por consistência com o resto do
painel, e virou requisito na spec sem nunca ter sido decidido contra o protótipo
— divergência silenciosa, a segunda desta série. Medido: `"Criado em"` aparece
**duas vezes no protótipo inteiro**, no detalhe do agente (`aCreated`) e no do
servidor MCP (`svCreated`). **O detalhe da base não tem datas**, e a omissão é
deliberada: quando a 5a-2 trouxer a tabela de documentos, a tela fica densa e as
datas viram ruído. Manter agora para remover depois seria churn.

**Consequência sobre D16, e ela piora o item em aberto em vez de resolvê-lo:**
com o card fora, a spec desta change deixa de requerer `createdAt`/`updatedAt`, e
os dois campos passam a não ter **nenhum** requisito nem asserção em lugar nenhum
do repositório — nem na spec da etapa 1, que os expõe, nem aqui. O item registrado
em `02-HISTORICO_E_STATUS.md` continua válido e fica mais importante: dois campos
no fio que nada promete e nada verifica.

### D21 — A coluna `Consultada por` entra no catálogo

Pedida depois de D20, e é a última parte de D2 a cair. O motivo é o mesmo: a
derivação já existe e custa **uma** requisição a `GET /agents` para a listagem
inteira — não uma por base, que é o que barrou `Documentos` e `Indexação` (D1). A
distinção entre as duas colunas é essa, e é a única que importa: `Consultada por`
sai de um `GET` que já se sabe fazer; as outras duas sairiam de N.

Reusa `utils/agentUsage.ts`, escrito em D20 para o card do detalhe — segundo
consumidor, que é o que a convenção 2 pede antes de considerar um módulo
compartilhado justificado. Custo: ~20 linhas na tabela mais a query na página.

**Divergência do protótipo, deliberada.** O protótipo mostra `—` quando nenhum
agente consulta a base:

```js
agents: ag.length ? ag.map(a => a.name).join(', ') : '—'
```

Ele tem dois estados; a tela real tem **três**, porque `GET /agents` pode falhar.
Reusar `—` para "nenhum" e para "não sei" tornaria os dois indistinguíveis, e a
diferença entre ausência conhecida e dado ausente é exatamente o que a convenção
13 protege. Resolvido no idioma que `McpServerTable` já usa nesta base:

| estado | célula |
|---|---|
| agentes carregados, nenhum consulta | `Nenhum agente` |
| agentes carregados, N consultam | nomes, separados por vírgula |
| catálogo de agentes indisponível | `—` |

Coberto por asserção negativa: com a consulta falhando, a listagem renderiza e
`Nenhum agente` **não** aparece.

### D22 — A conferência manual fechou em quatro rodadas

Registro do que a conferência custou e achou, porque é o dado que torna a
próxima mais barata (convenção 14).

| rodada | o que olhou | achou |
|---|---|---|
| 1 | Catálogo | um falso alarme (busca/filtro escondidos no catálogo vazio — o protótipo faz igual) e **três divergências de cópia**: título da tela, subtítulo e cópia do vazio (D18) |
| 2 | Formulário | **divergência estrutural**: o bloco da descrição, a orientação, o contador e o preview dentro dele. Mais uma recusa: o identificador `consultar_base` (D19) |
| 3 | Detalhe | **card de agentes** entra em escopo, **card de datas** sai (D20); depois a coluna `Consultada por` (D21) |
| 4 | Os dois esquemas de cor, todas as telas | nada |

**A quarta rodada não achar nada é o resultado esperado, e não o inesperado.** A
convenção 16 existe porque papel visual em tom fixo quebra num esquema e funciona
no outro; D10 decidiu, antes de escrever qualquer componente, não introduzir
papel visual novo nenhum — todos os usados já estavam declarados por esquema
desde `frontend-acabamento-telas` e `frontend-tema-identidade-visual`. Somado ao
guarda estático que reprovou contra o defeito real (tarefa 6.3), a rodada de
esquema de cor tinha pouco onde falhar. Vale registrar como evidência de que a
ordem — decidir os papéis visuais antes de codar, e reprovar o guarda antes de
confiar nele — é o que torna a conferência de tema barata.

**O que a conferência achou, achou em cópia, estrutura e escopo — nunca em cor.**
As quatro rodadas produziram nove mudanças de código e cinco decisões novas
(D18–D21), e nenhuma delas era de tom, contraste ou layout. É o oposto do
redesenho do painel, onde três etapas passaram verdes com defeito visual. A
diferença é que lá a identidade estava sendo construída; aqui ela já existia.

## Árvore de pastas

```
apps/frontend/src/
├── app/
│   └── routes.tsx                                    (M) 4 rotas novas
├── components/layout/
│   ├── AppShell.tsx                                  (M) item Conhecimento
│   └── DetailHeader.tsx                              (M) showDescription (D17)
└── features/
    └── knowledge-bases/                              (novo)
        ├── api/
        │   ├── knowledgeBasesApi.ts                  request<T>/ApiError próprios (D12)
        │   ├── knowledgeBasesApi.test.ts
        │   ├── useKnowledgeBases.ts                  queries + mutations
        │   └── useKnowledgeBases.test.ts
        ├── components/
        │   ├── KnowledgeBaseTable.tsx                Catálogo de bases
        │   ├── KnowledgeBaseTable.test.tsx
        │   ├── KnowledgeBaseDescriptionCard.tsx      "TEXTO LIDO PELO MODELO"
        │   ├── KnowledgeBaseDescriptionCard.test.tsx
        │   ├── KnowledgeBaseForm.tsx                 Formulário de base
        │   ├── KnowledgeBaseForm.test.tsx
        │   ├── KnowledgeBaseDocumentsPlaceholder.tsx nota de sequenciamento (D4)
        │   └── KnowledgeBaseDocumentsPlaceholder.test.tsx
        ├── pages/
        │   ├── KnowledgeBaseListPage.tsx
        │   ├── KnowledgeBaseListPage.test.tsx
        │   ├── KnowledgeBaseDetailPage.tsx
        │   ├── KnowledgeBaseDetailPage.test.tsx
        │   ├── KnowledgeBaseCreatePage.tsx
        │   ├── KnowledgeBaseCreatePage.test.tsx
        │   ├── KnowledgeBaseEditPage.tsx
        │   └── KnowledgeBaseEditPage.test.tsx
        └── types/
            └── knowledgeBase.ts
```

Nada em `libs/`: `libs/` é para compartilhamento entre apps do backend, e isto é
uma feature de uma app só.

## Tamanho projetado (convenção 18)

Projetado **por componente e depois de fechar a verificação** — a projeção acima
inclui `KnowledgeBaseDocumentsPlaceholder` e o teste de `knowledgeBasesApi`, que
só existem por causa de D4 e D12, decisões que a verificação produziu.

Criados e modificados contados **separadamente**, com o blast radius lido no
código.

**Criados — 21 arquivos:**

| Componente | Arq. | Linhas | Âncora |
|---|---|---|---|
| `types/knowledgeBase.ts` | 1 | 30 | `types/mcpServer.ts` = 49, com 3 tipos de request a mais |
| `api/` (cliente + hooks) | 2 | 200 | `mcpServersApi.ts` 109 + `useMcpServers.ts` 107, menos teste de conexão |
| `api/` testes | 2 | 330 | `useMcpServers.test.ts` = 345 |
| 4 componentes | 4 | 330 | `McpServerTable` 102, `McpServerForm` 286, `McpServerConfigCard` 67 |
| 4 testes de componente | 4 | 480 | `McpServerForm.test` 370, `McpServerTable.test` 143 |
| 4 páginas | 4 | 300 | `List` 70, `Detail` 206, `Create` 63, `Edit` 94 |
| 4 testes de página | 4 | 640 | `Detail.test` 359, `List.test` 214, `Edit.test` 228 |
| **Total criado** | **21** | **~2310** | |

**Modificados — 2 arquivos, ~20 linhas.** Blast radius lido no código, não
derivado de contagem de operações: `routes.tsx` ganha um bloco de 4 rotas
(~8 linhas, no formato dos três grupos existentes) e `AppShell.tsx` ganha uma
entrada em `navItems` e um import (~3 linhas), mais ajuste do teste de
`AppShell` para o quarto item (~9 linhas). Nenhum record compartilhado é tocado,
então não há o perfil de "N sites de 1 a 3 linhas" que dominou
`knowledge-base-vinculo-agente`.

**Total projetado: 23 arquivos, ~2330 linhas.** Âncora decomposta, nunca
headline: `features/mcp-servers/` completo é 26 arquivos / 3820 linhas, e a
diferença é o que esta etapa não tem — teste de conexão, catálogo de tools e
visão inversa.

Custo unitário aplicado com o refinamento da convenção 18: ~19-21 linhas por
cenário simples, e 25-40 para cenário com arranjo próprio. Os três caros aqui:
busca com normalização de acentos, `Desativar` passando por modal, e o 400 de
descrição vazia mapeado para erro por campo.

### Medido na entrega (quarta medição da série)

**São três causas, e o agregado mente.** Entregue 52 arquivos / 3078 linhas
contra 23 projetados; ler isso como "a projeção errou 2x" produziria um fator de
correção inventado na quinta medição — o erro exato que a convenção 18 existe
para não repetir. As três, separadas: (1) os criados projetados bateram exatos;
(2) 21 modificados a mais, por uma causa estrutural reutilizável; (3) escopo
acrescentado depois da projeção, que ela não podia conter. Registradas também na
convenção 18 do `01-ARQUITETURA_E_CONVENCOES.md`.

**A medição é em duas partes**, porque o escopo mudou por decisão explícita no
meio da implementação (D20 e D21). Comparar o total entregue contra a projeção
original não testaria o método, testaria a estabilidade do escopo.

**Parte 1 — escopo projetado, entregue:**

| | projetado | entregue | erro |
|---|---|---|---|
| criados | 21 arquivos / ~2310 linhas | **21 / 2364** | arquivo **exato**; linhas +2,3% |
| modificados | 2 arquivos / ~20 linhas | **6 / 99** | arquivo **3x**; linhas ~5x |
| subtotal | 23 / ~2330 | **27 / 2463** | arquivo +17%; linhas +5,7% |

**Parte 2 — acrescentado por D20 e D21** (card de agentes e coluna
`Consultada por` entram, card de datas sai), não projetado porque as decisões
vieram depois: **+4 criados / ~585 linhas** e **+21 modificados / ~30 linhas**.

**Entregue total: 52 arquivos / 3078 linhas** (25 criados / 2949, 27 modificados
/ 129).

Vale notar o que a Parte 2 **não** custou: D21 não criou arquivo nenhum. A coluna
reusou `utils/agentUsage.ts`, escrito para o card do detalhe — o segundo consumidor
que a convenção 2 pede antes de considerar um módulo compartilhado justificado
apareceu duas horas depois do primeiro, não em duas changes.

**E os 21 modificados de D20 são o caso que a convenção 18 já previa, repetido no
outro app.** A convenção registra, de `knowledge-base-vinculo-agente`: "12
arquivos modificados, 11 deles por uma a três linhas", causados por acrescentar
um campo a um response usado por N handlers. Aqui aconteceu o mesmo do lado do
frontend: acrescentar `knowledgeBases` ao tipo `Agent`, **obrigatório**, tocou 21
arquivos de teste com **uma linha cada** — `knowledgeBases: []` na fixture.
Enumerados pelo `tsc`, não por leitura.

A régua que a convenção tira daí — "projetar modificados a partir do blast radius
lido no código, não do número de operações" — vale, e ganha um refinamento do
lado do frontend: **o blast radius de um campo obrigatório num tipo compartilhado
é a contagem de fixtures, não a de componentes.** Nenhum componente precisou
mudar; 21 arquivos de teste precisaram. Quem projetar isso lendo o código de
produção erra por um fator de 20.

**A metade que acertou** confirma o método pela segunda vez seguida: contar por
componente, depois de fechar a verificação, acertou os criados **no arquivo** —
21 contra 21, com as linhas 2,3% acima. A medição anterior
(`knowledge-base-vinculo-agente`) acertou 25 contra 25. Duas medições, dois
acertos de contagem de criados.

**A metade que errou tem causa estrutural, e é a mesma de antes um nível abaixo.**
A convenção 18 já registra que projetar por operação CQRS conta arquivos
*criados* e é cega aos *modificados*. Aqui a projeção de modificados foi feita
lendo o blast radius no código, como a convenção pede — e ainda errou 3x, porque
contou os arquivos **de produção** que a mudança toca (`routes.tsx`,
`AppShell.tsx`) e foi cega aos **testes deles**. Os três arquivos de teste
modificados (`AppShell.test.tsx`, `router.test.tsx`, `DetailHeader.test.tsx`)
somam 60 das 99 linhas alteradas.

A régua que sai daí é curta: **todo arquivo modificado arrasta o teste dele.**
Projetar modificados em pares, não em unidades. Aplicada aqui, a projeção teria
sido 4 arquivos (`routes.tsx`, `AppShell.tsx` + os dois testes) — ainda 2 a menos
que os 6, e os 2 que faltam são D17.

**E D17 expõe um limite da convenção 18 que ela ainda não cobria.** A convenção
diz para projetar **depois** de fechar a verificação, porque a verificação
descobre componentes. Foi feito: a verificação produziu o placeholder de D4 e o
teste de D12, e os dois estão na projeção. Mas `DetailHeader` não foi descoberto
pela verificação — foi descoberto pela **implementação**, ao montar a página
contra um componente compartilhado cujo comportamento padrão contrariava uma
decisão já tomada. Verificação lê o que o código expõe; só a montagem revela o
que ele **assume**. Não há como projetar isso, e a lição não é "verificar mais":
é que a faixa de modificados carrega uma incerteza que a de criados não tem, e
citar as duas com a mesma precisão é falsa confiança.

## Risks / Trade-offs

Cada risco com contraparte verificável (convenção 10), e a contraparte diz
**qual** verificação — lembrando que a suíte cobre contrato, não aparência.

- **A UI passa a afirmar contagem que o sistema não coleta**, por alguém
  "completar" as colunas de D1/D9 depois → Cenário na spec afirmando que a
  listagem **não** exibe contagem de documentos nem resumo de indexação, no
  formato de asserção negativa que a convenção 13 pede. Teste: a tabela
  renderizada não contém `/documento/i` nem `/indexad/i`.
- **A descrição volta a parecer decorativa** se alguém a promover a subtítulo do
  cabeçalho → Cenário afirmando que a descrição aparece em card próprio, com o
  rótulo que a identifica como texto lido pelo modelo, e **não** no subtítulo.
  Teste: `DetailHeader` não recebe a descrição como subtítulo.
- **A nota de D4 virar estado vazio** ("Nenhum documento") numa edição futura →
  Cenário fixando a cópia como sequenciamento. Teste: asserção negativa de que a
  tela não contém `/nenhum documento/i`.
- **Papel visual em tom fixo passando o guarda dentro de ternário**, o defeito
  exato que a convenção 15 registra → **Não coberto pela suíte como está.** A
  contraparte é a tarefa 6.3: reintroduzir um tom fixo dentro de um ternário num
  arquivo novo, confirmar que o guarda reprova, e só então remover. Guarda não
  reprovado não conta.
- **Regressão visual invisível à suíte** (convenção 14) → Sem contraparte
  automatizável, e isso é declarado, não omitido: jsdom não enxerga cor,
  contraste nem layout. A contraparte é a conferência manual da tarefa 6, tela a
  tela, nos dois esquemas, iterativa.
- **Busca client-side degradando com 100+ bases**, o volume que o handoff declara
  → Sem contraparte nesta change, por decisão: é dívida assumida, com o pedido de
  `?q=` e paginação registrado como handoff. O que a suíte cobre é a
  normalização de acentos funcionando, não o desempenho.
- **A 5a-2 nascer sem os campos que D5 e D6 exigem** → Contraparte fora da
  suíte: o handoff em `02-HISTORICO_E_STATUS.md`, escrito nesta change (tarefa
  7), antes de a etapa 2 ser proposta.

## Migration Plan

Não há migração. Nenhum schema, nenhum dado, nenhuma rota nova. Feature nova
atrás de rota nova, dentro do grupo protegido — não altera nenhuma tela
existente além do item de navegação. Rollback é reverter o commit.

## Open Questions

Nenhuma incerteza real de negócio em aberto. As três perguntas que o pedido
levantou foram fechadas por decisão, não deixadas abertas: abas do detalhe (D3),
o que aparece no lugar dos documentos (D4) e a dívida de flakes (D13).

Versões: nenhuma dependência nova, então não há versão a fixar. O ícone sai de
`lucide-react`, já no `package.json`.
