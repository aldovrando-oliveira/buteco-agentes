## Context

Carve de defeito pré-existente em `apps/api`, achado durante a revisão da
proposta de `knowledge-base-vinculo-agente` e deixado em aberto por decisão de
sequenciamento (convenção 12). O diagnóstico registrado em
`02-HISTORICO_E_STATUS.md` falava de **cinco sites**. Esta seção fecha a
verificação exigida pela convenção 6 antes de qualquer decisão, e o resultado
divergiu do registro em três pontos.

### Verificação 1 — os cinco sites registrados existem, nas linhas registradas

Conferidos contra a árvore em 09/09/2026 (`main` em `3604bce`). Nenhum foi
corrigido de carona, nenhuma linha estava defasada:

| # | Site | Ordena por | Onde a ordenação roda |
|---|---|---|---|
| 1 | `AgentDelegations/AgentDelegationLookup.cs:23` | `agent.Name` | SQL |
| 2 | `AgentMcpBindings/AgentMcpServerLookup.cs:22` | `joined.McpServer.Name` | SQL |
| 3 | `Agents/Queries/ListAgents/ListAgentsQueryHandler.cs:36` | `binding.McpServer.Name` | **memória** |
| 4 | `Agents/Queries/ListAgents/ListAgentsQueryHandler.cs:53` | `delegation.TargetAgent.Name` | **memória** |
| 5 | `KnowledgeBases/Queries/ListKnowledgeBases/ListKnowledgeBasesQueryHandler.cs:20` | `knowledgeBase.CreatedAt` | SQL |

O molde da correção também está onde o registro diz:
`AgentKnowledgeBindings/AgentKnowledgeBaseLookup.cs:31-32` e
`ListAgentsQueryHandler.cs:75-76` fazem `OrderBy(Name).ThenBy(Id)`, com o
comentário registrando que nome é editável **e** não único.

### Verificação 2 — a varredura por um sexto site achou dois inéditos

Varredura de `OrderBy`/`ThenBy` em `apps/api`, `apps/inbox`, `apps/workers` e
`libs`, excluindo `obj/`, `bin/` e `Migrations/`. Em `apps/api`, três sites a
mais dos cinco registrados alimentam resposta de API sem desempate:

| # | Site | Ordena por | Status no registro |
|---|---|---|---|
| 6 | `Agents/Queries/ListAgents/ListAgentsQueryHandler.cs:21` | `agent.CreatedAt` | já registrado como "sexto a conferir" — **confirmado** |
| 7 | `McpServers/Queries/ListMcpServers/ListMcpServersQueryHandler.cs:14` | `mcpServer.CreatedAt` | **inédito** |
| 8 | `KnowledgeDocuments/Queries/ListKnowledgeDocuments/ListKnowledgeDocumentsQueryHandler.cs:33` | `document.CreatedAt` | **inédito** |

Os sites 7 e 8 são o mesmo defeito, na mesma forma, no mesmo app, a duas linhas
de correção cada. Deixá-los fora produziria um item aberto cujo conteúdo é
"repetir a change que acabou de rodar" — por isso entram, e é por isso que o
escopo é **oito sites**, não cinco. Não é escopo acrescentado depois da
projeção: a projeção da seção final já os conta (convenção 18).

### Verificação 3 — um defeito de classe diferente, que o desempate não conserta

Os sites 3 e 4 ordenam **em memória**, sobre o resultado já materializado da
consulta em lote; seus gêmeos 1 e 2 ordenam **em SQL**. São dois comparadores
diferentes para a mesma lista lógica, e a pergunta "eles concordam?" não é
respondível de memória. Medido nos dois runtimes reais:

- **PostgreSQL 18**, a mesma imagem que as fixtures usam
  (`new PostgreSqlBuilder("postgres:18")`) e que o `docker-compose.yml` usa:
  `datcollate = en_US.utf8`, `datlocprovider = c`, ou seja collation do libc.
- **.NET 10** com ICU, que é o que `Comparer<string>.Default` usa dentro de
  `OrderBy` em LINQ-to-Objects.

Para o par `suporte-alfa` / `Suporte Alfa`:

| Comparador | Ordem |
|---|---|
| PostgreSQL 18 (libc `en_US.utf8`) | `suporte-alfa` , `Suporte Alfa` |
| .NET 10 / ICU (`Invariant`, `pt-BR` e `en-US` — os três iguais) | `Suporte Alfa` , `suporte-alfa` |

**Consequência:** hoje, sem nenhum empate de nome envolvido, `GET /agents` e
`GET /agents/{id}` podem devolver `mcpServers` e `delegatesTo` do mesmo agente
em ordens diferentes. Acrescentar `ThenBy(Id)` não alcança isso, porque a
divergência está no critério **primário**. O mesmo vale para `knowledgeBases`,
que tem o desempate desde a etapa 3 mas ainda ordena em memória na listagem
(`ListAgentsQueryHandler.cs:75-76`) — ou seja, o requisito de ordem que esta
base já tem hoje é cumprido por dois comparadores diferentes nas duas
superfícies que ele nomeia.

Um efeito colateral do mesmo mecanismo: `Comparer<string>.Default` é
**sensível à cultura do processo**. A ordem da listagem depende de
`CultureInfo.CurrentCulture` do container, que não é fixada em nenhum lugar
deste repositório. Para o par medido as três culturas testadas coincidem, então
isso não é o defeito — é a razão de não bastar "escolher uma cultura".

### Verificação 4 — o desempate por `Id` é seguro nas duas superfícies

Se as duas superfícies vão prometer a mesma ordem, o desempate precisa produzir
a mesma ordem em SQL (`uuid` do PostgreSQL) e em memória
(`Comparer<Guid>.Default`). Isso **não** era seguro de assumir: há a crença
comum de que `Guid.CompareTo` compara o primeiro campo como `Int32` **com
sinal**, o que discordaria do `memcmp` do PostgreSQL na metade dos pares
aleatórios. Medido:

- 100.000 pares de `Guid.NewGuid()` (v4): **zero discordâncias** entre
  `Comparer<Guid>.Default` e a ordem byte-a-byte big-endian.
- Casos de fronteira explícitos (`80000000-…` × `7fffffff-…`,
  `…-8000-…` × `…-7fff-…`): concordam.
- PostgreSQL 18 real, `ORDER BY` sobre esses mesmos valores: mesma ordem.

**Conclusão:** desempate por `Id` é o único critério desta change que já é
idêntico nos dois comparadores. É por isso que ele serve de base para os
guardas, e é por isso que o guarda herdado da etapa 3
(`AgentKnowledgeBindingEndpointsTests.cs:354`) hoje passa nas duas superfícies
apesar da divergência da Verificação 3 — ele afirma ordem de **id** entre
homônimas, e nomes idênticos não expõem collation.

### Verificação 5 — nenhuma spec viva promete ordem, com uma exceção

Varredura por `ordem`/`ordena`/`order`/`alfabét`/`desempate` em
`openspec/specs/`:

- `agent-catalog`, `agent-mcp-binding`, `agent-delegation-binding`,
  `mcp-server-catalog`, `knowledge-base-catalog`, `knowledge-document-catalog`:
  **nenhuma promessa de ordem**. Nem sequer a ordenação por `CreatedAt` que os
  quatro catálogos praticam. As specs falam de "conjunto", não de lista
  ordenada. Logo, para os sites 1-8 esta change **acrescenta** requisito; não
  conserta requisito violado.
- `agent-knowledge-binding:137-172`: **única exceção** — já exige nome com
  desempate por id, "tanto na consulta por id quanto na listagem". Esse
  requisito é **apertado** por esta change (qual comparador define "ordem de
  nome"), o que faz dela a única Modified Capability.

## Goals / Non-Goals

**Goals:**

- Nenhuma lista que `apps/api` devolve depende do plano do PostgreSQL para a
  sua ordem: todo critério exposto termina em desempate por identificador.
- A ordem de uma mesma lista é a mesma nas duas superfícies que a servem
  (`GET /agents/{id}` e `GET /agents`), inclusive no critério primário.
- Um guarda por site, verificado contra o defeito real e **no componente que a
  correção toca** (convenção 15, as duas metades).

**Non-Goals:**

- **Trocar critério de ordenação de qualquer lista.** Ver D1.
- **Índice único de nome**, ou validação de nome duplicado. Nome duplicado é
  requisito em `knowledge-base-catalog` ("Nome duplicado é permitido"); esta
  change torna a ordem determinística **com** homônimos, não os proíbe.
- **Ordenação configurável pelo cliente** (`?sort=`). Nenhum consumidor pediu, e
  convenção 2 manda esperar cenário real.
- **Fixar a cultura do processo de `apps/api`.** A correção da Verificação 3
  torna a cultura irrelevante para estas listas, o que é melhor que fixá-la.
- **`apps/inbox`, `apps/workers`, `apps/frontend`.** Ver "Achados fora de
  escopo".
- **Índice novo no banco.** Ver D5.

## Decisions

### D1 — Os catálogos continuam ordenando por `CreatedAt`. A premissa registrada estava errada

O item registrado pede para decidir se `ListKnowledgeBasesQueryHandler` deveria
ordenar por nome, sob a premissa de que *"o catálogo de MCP e o de agentes
ordenam por nome"* e que a divergência seria acidente de implementação.

**A premissa é falsa.** Lido no código:

| Consulta | Ordena por |
|---|---|
| `ListAgentsQueryHandler.cs:21` | `CreatedAt` |
| `ListMcpServersQueryHandler.cs:14` | `CreatedAt` |
| `ListKnowledgeBasesQueryHandler.cs:20` | `CreatedAt` |
| `ListKnowledgeDocumentsQueryHandler.cs:33` | `CreatedAt` |

**Os quatro catálogos de `apps/api` ordenam por `CreatedAt`.** Nenhum ordena por
nome. O que ordena por nome são as três consultas de **vínculo**
(`AgentDelegationLookup`, `AgentMcpServerLookup`, `AgentKnowledgeBaseLookup`) —
ou seja, o padrão da casa é claro e coerente: **catálogo em ordem de cadastro,
vínculo em ordem de leitura**. `ListKnowledgeBasesQueryHandler` não é o
divergente; ele é o quarto caso do padrão, e o próprio comentário de classe diz
isso em voz alta — *"mesmo comportamento de `ListMcpServers`"*.

Não há divergência a explicar, não há acidente a registrar, e a ramificação
"ordenar por nome, com desempate" do item registrado **não se aplica**. A
correção do site 5 é só o desempate.

Isso também **elimina** da change o componente "decidir e migrar o critério do
catálogo de bases" que o item previa — o segundo sentido de erro que a
convenção 18 manda registrar (verificação que elimina componente previsto, não
só acrescenta).

*Alternativa considerada:* normalizar os quatro catálogos para ordem de nome, já
que é a ordem que serve ao operador. Recusada por duas razões: seria mudança de
comportamento observável sem ninguém tendo pedido, e contraria o padrão que os
quatro catálogos já seguem por escolha registrada em comentário. Se algum dia a
tela quiser ordem de nome, ela é decisão de produto da change de tela, não deste
carve.

### D2 — Desempate sempre por `Id`, sempre como último critério, nunca substituindo o primário

`.ThenBy(x => x.Id)` acrescentado, sem tocar o `OrderBy` existente. `Id` é
`Guid` chave primária: único por construção, imutável, e — pela Verificação 4 —
com ordem idêntica nos dois comparadores em jogo.

*Alternativa considerada:* desempatar por `CreatedAt` nos sites de nome. Recusada:
`CreatedAt` também não é único (é o que produz os sites 5-8), então o desempate
herdaria o defeito que veio corrigir.

*Alternativa considerada:* ordenar só por `Id` e deixar a ordenação de
apresentação para o cliente. Recusada: joga para a UI uma responsabilidade que a
API já assumiu em sete lugares, e `agent-knowledge-binding` já promete ordem de
nome na resposta.

### D3 — A ordenação de `mcpServers`, `delegatesTo` e `knowledgeBases` na listagem vai para o SQL

Correção da Verificação 3. Hoje `ListAgentsQueryHandler` materializa cada
consulta em lote sem ordem e ordena depois, dentro do `GroupBy`, em memória. A
correção é ordenar **na consulta**, com `OrderBy(...).ThenBy(...)`, e **remover**
a ordenação em memória — passando a depender de o `GroupBy` preservar a ordem de
origem dentro de cada grupo.

**A premissa que isso introduz, e onde ela está registrada.** A ordem passa a
depender de `Enumerable.GroupBy` preservar a ordem de origem dentro do grupo. É
`System.Linq`, não EF Core — `bindings` vem de `ToListAsync`, então o `GroupBy`
é LINQ-to-Objects sobre uma `List<>` já materializada, e nem EF Core nem Npgsql
participam dessa etapa.

O comportamento **é documentado**: a referência de `Enumerable.GroupBy`
(Microsoft Learn, moniker `net-10.0`, revisão de 2026-07-01, em todas as
sobrecargas) diz *"Elements in a grouping are yielded in the order that the
elements that produced them appear in `source`"*. Ainda assim, convenção 6 não
deixa aceitar leitura de documentação como suficiente quando o código vai
depender disso: verificado em 2.000 tentativas com fonte pré-ordenada e agentes
intercalados, **a ordem de origem foi preservada dentro do grupo em todas**.

Contrato publicado corroborado por medição é classe de risco mais baixa que
comportamento não documentado — mas continua sendo premissa, e por isso tem
item aberto próprio em `02-HISTORICO_E_STATUS.md`, com gatilho no bump do
**runtime .NET** (`global.json` / TFM) e não no de EF Core/Npgsql, que tocam
outro elo da corrente. R2 registra o que os guardas alcançam disso e o que não
alcançam.

Resultado: as duas superfícies passam a usar **o mesmo comparador — o do banco**
— tanto no critério primário quanto no desempate. A cultura do processo deixa de
influenciar a resposta.

*Alternativa considerada:* manter a ordenação em memória e escolher um
comparador explícito que imite o PostgreSQL (`StringComparer.Ordinal` ou uma
cultura fixa). Recusada porque é impossível de fazer fielmente: nenhum
`StringComparer` do .NET reproduz a collation `en_US.utf8` do libc, e uma
aproximação deixaria a divergência menor e mais difícil de notar — pior que a
atual, que ao menos é sistemática.

*Alternativa considerada:* fazer a listagem chamar os três `Lookup` por agente,
reaproveitando as consultas já ordenadas. Recusada: é exatamente o N+1 que os
comentários das três consultas em lote dizem estar evitando.

**Custo aceito:** a ordem passa a vir de um lugar não-óbvio (a consulta, três
blocos acima do `GroupBy`), e um "reparo" bem-intencionado que reintroduza o
`OrderBy` em memória volta a divergir sem quebrar nada visível. Mitigado por
comentário no ponto e por guarda dedicado (R2).

### D4 — Capability nova para a garantia, e não um requisito em cada capability de domínio

Justificada na proposal. O ponto técnico que a sustenta: a decisão de D3 —
**qual comparador é o canônico da API** — é uma decisão única que vale para
todas as listas, e um requisito repetido em seis specs de domínio não tem onde
registrar isso sem ser copiado seis vezes. Mesmo motivo que fez
`agent-tool-namespace` nascer: nenhuma capability existente é dona da união.

`agent-knowledge-binding` é a única Modified porque é a única que já tem
requisito de ordem, e ele fica **impreciso** depois de D3 se não disser qual
comparador vale.

### D5 — Nenhum índice novo

Os `ORDER BY` afetados são sobre tabelas de catálogo com cardinalidade de
dezenas a centenas de linhas, e três dos sites já leem a tabela inteira sem
`WHERE`. Acrescentar `Id` como último termo de ordenação não muda o plano de
forma relevante nessa escala, e criar índice composto por causa disso seria
custo de migração sem cenário. Convenção 2: índice quando houver medição, não
por precaução.

*Contraparte verificável:* nenhuma — é decisão de não fazer. Se a suíte de
integração acusar regressão de tempo, o índice entra com a medição em mão.

### D6 — Formato dos guardas, herdado da etapa 3 e não reinventado

As três decisões de `KnowledgeBasesWithEqualNames_AreTieBrokenByIdDeterministically`
(`AgentKnowledgeBindingEndpointsTests.cs:354`) valem para os oito sites:

1. **A asserção é sobre ordem crescente de identificador**, nunca sobre "duas
   consultas devolvem a mesma ordem". A segunda forma é asserção sobre
   não-determinação: passa com o defeito presente sempre que o plano do
   PostgreSQL calhar de ser estável — o perfil dos quatro guardas que esta base
   já teve de consertar (convenção 15).
2. **O arranjo cria os registros empatados em ordem de inserção oposta à ordem
   de `Id`**, para a ordem "natural" do banco não coincidir por acidente com a
   esperada.
3. **Cada guarda de desempate fica separado do teste de ordenação geral**, para
   que remover o `ThenBy` reprove o de desempate e **só** ele.

E a segunda metade da convenção 15, que a etapa 3 registrou: para cada site,
remover o `ThenBy` de propósito e conferir que reprova **naquele site e só
nele**. Guarda que reprova junto com o de ordenação alfabética está afirmando a
garantia no componente errado.

**Correção feita durante a implementação (convenção 9): o molde da etapa 3 é
necessário e não é suficiente.** Ao conferir que os guardas reprovam contra o
defeito, o de `mcpServers` **não reprovou de forma confiável**:

| Execução contra o código defeituoso | Resultado |
|---|---|
| Teste isolado, 5× | reprova 5/5 |
| Classe inteira, 3× | reprova 2/3 — **passou uma vez** |

A causa é estrutural, não de arranjo, e invalida a suposição implícita deste D6.
O guarda afirma "ordem crescente de `id`"; sem o `ThenBy`, o PostgreSQL **às
vezes já devolve nessa ordem** por conta do plano — um index scan pela chave
primária emite exatamente em ordem de `id`. *"Ordenado por `id` porque o
`ThenBy` existe"* e *"ordenado por `id` porque o plano calhou"* são a mesma
observação, e nenhum arranjo de teste as separa. Aumentar o número de homônimos
não ajuda: se o plano emite ordem de `id`, emite para qualquer quantidade.

**Nenhum guarda puramente comportamental sobre desempate por `id` reprova de
forma determinística.** Isso vale para os oito sites desta change e para o
guarda já entregue pela etapa 3.

Medido também no guarda entregue, reintroduzindo o defeito em
`AgentKnowledgeBaseLookup` e em `ListAgentsQueryHandler`:
`KnowledgeBasesWithEqualNames_AreTieBrokenByIdDeterministically` reprovou
**4/4** — mais robusto que o novo, para aquela forma de consulta. Não é
absolvição: a confiabilidade depende do plano, que varia por consulta e por
povoamento da tabela, então *"reprovou quando eu conferi"* não é propriedade
durável. É a quinta ocorrência da família que a convenção 15 nomeia, numa forma
que a convenção ainda não previa — o guarda não está errado, está **incompleto**.

**Cada site ganha então duas metades, e elas provam coisas diferentes:**

1. **A metade comportamental** — os cenários de ordem crescente de `id` descritos
   acima. É a expressão do requisito, é o que a spec descreve, e é o que pega o
   defeito na maioria das execuções. Fica como está.
2. **A metade determinística** — asserção sobre o **SQL efetivamente emitido**
   pela consulta daquele site, afirmando que o `ORDER BY` carrega o desempate. O
   SQL é capturado do log de `Executed DbCommand` do EF Core durante a
   requisição HTTP real, pelo logger da `WebApplicationFactory`. Reprova no
   instante em que o `ThenBy` sai, **sempre**, e no componente que a correção
   toca.

A metade determinística usa a consulta **de produção**, emitida pelo caminho
real da requisição — não uma consulta remontada no teste com os mesmos
operadores, que passaria igual com o comportamento certo e com o errado
(convenção 11).

*Alternativa considerada:* expor o `IQueryable` de cada lookup para o teste
chamar `ToQueryString()`. Recusada: mexe em seis arquivos de produção só para
viabilizar teste, e a captura do log alcança o mesmo com zero mudança de
produção.

*Alternativa considerada:* aceitar o guarda probabilístico e registrar a
limitação como item aberto. Recusada: a change existe para eliminar exatamente
esse tipo de guarda; entregá-lo aqui seria a mesma dívida com outro nome.

**Limite honesto da metade determinística:** ela afirma sobre a forma da
consulta, não sobre a resposta. Um `ORDER BY` correto que o PostgreSQL não
honrasse passaria — cenário que não existe, mas que delimita o que ela prova. É
por isso que as duas metades ficam, e não uma só.

**Onde couber, os guardas cobrem as duas superfícies** — `GET /agents/{id}` e
`GET /agents` —, como o guarda da etapa 3 já faz.

**Par "sem item" (convenção 5):** cada guarda de empate ganha o par com lista
sem nenhum empate — nome distinto, ou catálogo vazio. Para os catálogos,
`Knowledge/KnowledgeEmptyCatalogTests.cs` já cobre o vazio; o par a acrescentar
é o "sem empate".

### D7 — Os quatro sites de `CreatedAt` precisam de arranjo que force o empate

`CreatedAt` é `private set`, atribuído a `DateTimeOffset.UtcNow` no construtor
da entidade. Dois registros criados por HTTP na sequência não empatam, então um
guarda que só crie dois e afirme a ordem **passa com e sem o `ThenBy`** — o
defeito de guarda que a convenção 15 nomeia.

O arranjo obrigatório é forçar o empate escrevendo `CreatedAt` no banco depois
da criação, via `AppDbContext` resolvido do escopo da fixture. Há precedente
idiomático: `AgentEndpointsTests.cs:337,357`, `McpServerEndpointsTests.cs:132,149`
e outros já alcançam o `AppDbContext` por `scope.ServiceProvider`.

É o que torna esses quatro cenários caros (convenção 18: cenário com arranjo
próprio custa 25-40 linhas, não 19-21) e está contado assim na projeção.

## Risks / Trade-offs

Convenção 10: todo risco com contraparte verificável, ou justificativa explícita
de por que não é testável.

**R1 — Guarda de desempate que passa com o defeito presente.** É o modo de falha
mais provável desta change e já custou quatro correções a esta base.
**Confirmado durante a implementação, e pior do que o previsto**: o guarda
comportamental de `mcpServers` passou com o defeito presente em 1 de 3 execuções
da sua classe, e a causa é estrutural — ordem de `id` é o que o plano do
PostgreSQL às vezes já devolve sozinho (D6, "Correção feita durante a
implementação").
→ *Mitigação:* as duas metades de D6 — o cenário comportamental **mais** a
asserção sobre o SQL emitido, que é a que reprova sempre.
→ *Contraparte verificável:* tarefa própria por site, removendo o `ThenBy` e
exigindo que a metade determinística reprove **em todas** as execuções, não na
maioria. Um site cujo guarda determinístico não reprove não é considerado feito.
A metade comportamental continua exigida, mas a sua reprovação já **não** é o
critério de aceitação — ela é probabilística por construção.

**R2 — A correção de D3 é silenciosamente reversível.** Remover o `OrderBy` em
memória e depender do `GroupBy` deixa a ordem vindo de um ponto distante; quem
reintroduzir a ordenação em memória volta a divergir sem quebrar nenhum teste de
nome, porque os testes de nome existentes usam nomes que as duas collations
ordenam igual.
→ *Contraparte verificável:* guarda dedicado com o par medido na Verificação 3
(`suporte-alfa` / `Suporte Alfa`), afirmando que `GET /agents` e
`GET /agents/{id}` devolvem **a mesma ordem** para esse par. Esse guarda reprova
hoje, antes de qualquer correção, e reprova de novo se a ordenação voltar para a
memória. É o único guarda desta change cuja forma é "as duas superfícies
concordam" em vez de "ordem crescente de id" — e é legítimo aqui, porque não
afirma sobre não-determinação: afirma sobre **dois comparadores conhecidos e
medidos**, com ordem esperada fixa e verificada nos dois runtimes.

**R2b — a premissa de `GroupBy` de D3 não é coberta por guarda determinístico.**
Se `Enumerable.GroupBy` deixasse de preservar a ordem de origem, o efeito
apareceria **só** na listagem: o caminho de `GET /agents/{id}` passa pelos três
`Lookup`, que ordenam em SQL puro e não agrupam nada — o único `GroupBy` em
caminho de leitura de `apps/api` está em `ListAgentsQueryHandler`. As duas
superfícies divergiriam, que é precisamente o que os guardas de R2 afirmam não
acontecer, então eles **reprovam** — ao contrário do que a primeira leitura deste
risco supunha ("as duas estariam erradas juntas").
→ *O que fica descoberto:* eles reprovam **por probabilidade**. Um `GroupBy` que
não preservasse a ordem ainda pode emitir a ordem esperada por acaso — com os
dois itens do guarda de R2, em torno de metade das vezes. É o perfil "aprova ou
reprova por sorte de ordenação" da convenção 15, e nenhum arranjo de teste o
elimina, porque o defeito hipotético não é determinístico.
→ *Contraparte verificável:* refazer a medição de D3 no bump do runtime .NET,
antes de aceitar o bump — registrado como item aberto próprio em
`02-HISTORICO_E_STATUS.md`, com o gatilho, a premissa e o modo de falha.

**R3 — O par de nomes do guarda de R2 pode deixar de divergir** se a imagem do
PostgreSQL, a versão do ICU ou o provedor de collation mudar, transformando o
guarda em teste que passa por coincidência.
→ *Contraparte verificável:* o guarda de R2 afirma a ordem **esperada
concretamente** (a do banco), não só "as duas iguais" — se as collations
convergirem, ele continua correto; se divergirem de outro jeito, ele reprova e
obriga a reconferir. Complementarmente, o guarda cita no comentário a medição e
a versão (`postgres:18`, `datcollate=en_US.utf8`, `datlocprovider=c`).

**R4 — `ListAgentsQueryHandler` é o arquivo mais tocado** (três sites, mais a
reestruturação de três blocos de `GroupBy`), e é o handler mais quente do app.
→ *Contraparte verificável:* os cenários existentes de `AgentEndpointsTests.cs`
e `AgentKnowledgeBindingEndpointsTests.cs` já cobrem a forma da resposta de
`GET /agents` (incluindo `AgentResponseWireFormatTests.cs` para o formato de
fio); nenhum deles deve mudar. Cenário que precise ser reescrito para acomodar a
reestruturação é sinal de mudança de comportamento não pretendida, e vira achado.

**R5 — Classificar como pré-existente uma falha que a change causou**
(convenção 19).
→ *Contraparte verificável:* baseline em `git worktree` limpo **antes** de
qualquer classificação, como primeira tarefa. A falha conhecida de `apps/api` é
o flake de ordem de execução de `AgentDeactivationTests`
(`AgentDeactivationFixture.TaskJobPublisher` é instância única da classe e
`PublishedMessages` acumula; passa isolado) — causa já registrada, item aberto
próprio. Baseline vermelha por esse motivo remove a hipótese de regressão e
**só** ela; não absolve nada nem promove sintoma a "ambiental".

**R6 — Empate de `CreatedAt` é raro em produção**, então os sites 5-8 corrigem
um defeito que talvez nunca tenha se manifestado.
→ *Trade-off aceito, não risco.* Custa duas linhas por site, fecha a classe, e
evita um item aberto cujo conteúdo seria "repetir esta change". A raridade está
registrada aqui para que ninguém leia os guardas de `CreatedAt` como evidência
de incidente.

## Achados fora de escopo

Registrados aqui com evidência e gatilho, para virarem itens em
`02-HISTORICO_E_STATUS.md` no fechamento (tarefa própria). Nenhum entra nesta
change.

**A1 — Seis sites em `apps/inbox`.** App diferente, banco próprio, specs
próprias — convenção 12 manda change própria. E a severidade é outra: os
critérios são todos temporais (`CreatedAt`, `StartedAt`, `LastActivityAt`,
`OccurredAt`) alimentados por `DateTimeOffset.UtcNow`, verificado no `design.md`
de `inbox-instante-mensagem` (*"`Message.OccurredAt` em `apps/inbox` é sempre o
instante de recebimento… os dois adapters chamam `DateTimeOffset.UtcNow` inline
e sequer desserializam o timestamp que WAHA e Telegram enviam"*), então o empate
exige dois eventos no mesmo microssegundo — não é o empate por construção que
nome duplicado produz em `apps/api`.

| Site | Ordena por |
|---|---|
| `Channels/Queries/ListChannels/ListChannelsQueryHandler.cs:19` | `channel.CreatedAt` |
| `Contacts/Queries/ListContacts/ListContactsQueryHandler.cs:14` | `contact.CreatedAt` |
| `Contacts/Queries/GetContactSessions/GetContactSessionsQueryHandler.cs:23` | `session.StartedAt` |
| `Contacts/Queries/GetChannelSessions/GetChannelSessionsQueryHandler.cs:28` | `session.LastActivityAt` |
| `Contacts/Queries/GetChannelSessions/GetChannelSessionsQueryHandler.cs:37` | `message.OccurredAt` (prévia da última mensagem, `FirstOrDefault`) |
| `Messages/Queries/GetSessionMessages/GetSessionMessagesQueryHandler.cs:23` | `message.OccurredAt` |

Dois deles diferem dos oito de `apps/api` numa dimensão que muda a forma da
spec delta: **têm promessa de ordem em spec viva** —
`inbox-contact-session:211,222` ("ordenadas pela mais recente atividade
primeiro") e `inbox-message-history:176,185` (ordem cronológica). Lá a change
**conserta requisito violado** (MODIFIED), não acrescenta (ADDED). Misturar as
duas formas num único carve foi o segundo motivo de deixar `apps/inbox` fora.
*Gatilho: imediato, change própria.*

**A2 — `ContactSessionResolver.cs:53` NÃO é defeito.** A varredura o levantou
(`OrderByDescending(StartedAt).FirstOrDefault()` sem desempate), e ele parece o
caso mais grave de todos porque a consequência seria semântica — qual sessão é
retomada. Mas `apps/inbox` tem índice único **parcial** em
`sessions.ContactId WHERE "ClosedAt" IS NULL`
(`AppDbContext.cs:100-102`), e o `Where` da consulta é exatamente
`ClosedAt == null`: no máximo uma linha casa, e não há empate possível. O
`OrderByDescending` é defensivo e inerte. Registrado como **não-achado** para
que a próxima varredura não o levante de novo.

**A3 — `A2A/PostgresTaskStore.cs:77` em `apps/api`.**
`OrderByDescending(StatusTimestamp)` sem desempate, **e com `.Take(PageSize)`**
— paginação sobre ordem instável pode repetir ou omitir linhas, que é pior que
ordem trocada. Fora de escopo porque o próprio arquivo já registra, no
comentário de `ListTasksAsync`, que a consulta *"não tem nenhum consumidor em
`apps/api` hoje"*, junto de outra lacuna deixada com o mesmo gatilho.
*Gatilho: o primeiro consumidor de `ListTasksAsync` em `apps/api` — o mesmo
gatilho da lacuna de `AgentId` já anotada ali; as duas se corrigem juntas.*

**A4 — Um terceiro comparador, em `apps/frontend`.**
`features/agents/utils/knowledgeBaseRows.ts:26-27` reordena a lista no cliente
com `name.localeCompare(...)` e `id.localeCompare(...)`, enquanto
`agent-knowledge-binding-ui:62-66` diz que a interface *"reproduz a ordem que a
API devolve"*. Reproduzir e recalcular com um terceiro comparador não são a
mesma coisa, e a spec descreve a primeira. Depois de D3 a API passa a ter uma
ordem só, o que torna a reordenação no cliente desnecessária **e** o único ponto
restante onde a ordem pode divergir. *Gatilho: imediato após esta change, em
`apps/frontend` — e é change de `apps/frontend`, nunca de `apps/api`
(convenção 12 na direção inversa).*

## Árvore de pastas

**Nenhuma pasta nova, nenhum arquivo de código novo.** A change é inteiramente
modificação de arquivos existentes — o que é ele mesmo um dado de projeção
(convenção 18: contagem por operação CQRS conta criados e é cega a modificados;
aqui não há criados).

```
apps/api/
├── src/Buteco.Api/
│   ├── AgentDelegations/AgentDelegationLookup.cs              (M) site 1
│   ├── AgentMcpBindings/AgentMcpServerLookup.cs               (M) site 2
│   ├── Agents/Queries/ListAgents/ListAgentsQueryHandler.cs    (M) sites 3, 4, 6 + D3
│   ├── KnowledgeBases/Queries/ListKnowledgeBases/
│   │   └── ListKnowledgeBasesQueryHandler.cs                  (M) site 5
│   ├── McpServers/Queries/ListMcpServers/
│   │   └── ListMcpServersQueryHandler.cs                      (M) site 7
│   └── KnowledgeDocuments/Queries/ListKnowledgeDocuments/
│       └── ListKnowledgeDocumentsQueryHandler.cs              (M) site 8
└── tests/Buteco.Api.Tests/
    ├── AgentDelegationEndpointsTests.cs                       (M) sites 1, 4 + R2
    ├── AgentMcpBindingEndpointsTests.cs                       (M) sites 2, 3 + R2
    ├── AgentEndpointsTests.cs                                 (M) site 6
    ├── AgentKnowledgeBindingEndpointsTests.cs                 (M) D3 em knowledgeBases
    ├── McpServerEndpointsTests.cs                             (M) site 7
    └── Knowledge/
        ├── KnowledgeBaseCatalogTests.cs                       (M) site 5
        └── KnowledgeDocumentCatalogTests.cs                   (M) site 8
```

`apps/frontend` não é tocado: a regra de projeto pede teste unitário no frontend
e no backend, e aqui não há mudança de frontend a testar — a única implicação de
frontend é A4, que é change própria. `apps/workers` e `apps/inbox` não são
tocados.

## Projeção de tamanho

Convenção 18, e obedecendo o que ela manda: **projeção feita depois de a
verificação fechar** (as Verificações 1-5 acima), **criados e modificados
contados separadamente**, **modificados projetados em pares com os testes
deles**, e **só sobre código** — os cinco artefatos `openspec/` ficam fora.

| Grupo | Arquivos | Linhas |
|---|---|---|
| Criados (código) | **0** | 0 |
| Modificados — produção | 6 | 50-70 |
| Modificados — teste | 7 | 270-330 |
| **Total código** | **13** | **320-400** |

Como os números saíram:

- **Produção, 6 arquivos.** Cinco deles levam uma linha de `ThenBy` mais 3-5 de
  comentário (~5 linhas cada, o perfil de 1-3 linhas por site já medido em
  `knowledge-base-vinculo-agente` acrescido do comentário que D3 exige).
  `ListAgentsQueryHandler.cs` é o outlier: três sites mais a reestruturação de
  D3 em três blocos de `GroupBy`, ~30 linhas.
- **Teste, 7 arquivos, ~11 cenários.** Quatro cenários de empate de `CreatedAt`
  a 25-40 linhas (D7 — arranjo próprio que escreve no banco: o custo unitário
  caro que a convenção 18 mandou registrar, medido nos três cenários mais caros
  de `knowledge-base-vinculo-agente`), três de empate de nome a ~30 com as duas
  superfícies, dois guardas de R2 a ~25-30, e os pares "sem empate" a ~12.
- **Nenhum arquivo criado** é a previsão mais falsificável desta projeção: se a
  implementação criar um arquivo de teste novo, a projeção errou, e o motivo
  entra na medição.

**Escopo acrescentado depois da projeção, registrado antes de medir.** A metade
determinística de D6 não existia quando esta projeção foi feita — ela nasceu da
conferência da convenção 15, já dentro da implementação. Acrescenta **1 arquivo
criado** (o helper de captura de SQL, em `tests/.../Support/`) e uma asserção por
site. Pela regra da própria convenção 18, isso entra na conta do **entregue**,
não na do erro de método: a medição de método compara só o escopo que estava
projetado. A previsão de "zero criados" fica registrada como falsificada, com a
causa — verificação de guarda que muda o desenho do guarda — e não como erro de
contagem.

**Sétima medição da série.** O que esta rodada põe à prova, além da contagem: se
o método de "contar criados e modificados separadamente a partir do blast radius
lido no código" continua acertando (acertou em
`knowledge-base-vinculo-agente`, 25 contra 25, e nos criados de
`frontend-knowledge-base-catalogo`, 21 contra 21) num escopo cuja contagem é
**100% modificação** — o caso extremo da própria régua. E a razão de headline
deve ficar baixa desta vez: nenhuma migração, logo nenhum `.Designer.cs` com
snapshot inteiro do modelo, que foi a causa do 3,1x da medição anterior.

## Medição (preenchida no fechamento)

Diffstat decomposto, só código — os cinco artefatos `openspec/` (1.206 linhas) e
as edições de `01`/`02` (252 linhas) ficam fora, como a convenção 18 exige.

| Grupo | Projetado | Entregue | |
|---|---|---|---|
| Criados (código) | **0** / 0 linhas | **2** / 188 linhas | falsificado |
| Modificados — produção | 6 / 50-70 | **6** / +53 −9 | **acertou** |
| Modificados — teste | 7 / 270-330 | **8** / +454 −0 | +1 arquivo, +38% linhas |
| **Total código** | **13** / 320-400 | **16** / **+695 −9** | |

**O que acertou, e é a terceira medição seguida do mesmo método.** A contagem de
**produção** bateu exata — 6 arquivos projetados, 6 entregues — e as linhas
caíram dentro da faixa (53 contra 50-70). O perfil por site também bateu: cinco
sites a 4-7 linhas cada (a linha de `ThenBy` mais o comentário) e
`ListAgentsQueryHandler` como outlier previsto, a +27 −9. Contar modificados a
partir do blast radius lido no código, separado dos criados, acertou de novo.

**O que falhou, e a causa é uma só.** Os três desvios — 2 criados onde a projeção
dizia 0, +1 arquivo de teste, +124 linhas de teste — são **o mesmo item**: a
metade determinística dos guardas (`EmittedSqlCapture`, `CreatedAtTie`, e as 8
asserções de SQL). Ela não existia na projeção porque nasceu **da conferência da
convenção 15**, já dentro da implementação: o guarda comportamental reprovava só
probabilisticamente, e descobrir isso exigia rodar o guarda contra o defeito —
que é, por definição, depois de escrevê-lo.

Pela régua da própria convenção 18 isso entra na conta do **entregue**, não na do
erro de método: a medição de método compara só o escopo que estava projetado, e
nesse recorte a projeção acertou produção exata e errou teste em ~7%
(330 projetadas contra ~354 do escopo original, descontadas as 100 linhas dos dois
arquivos novos e as ~24 das asserções de SQL).

**A lição nova, e ela é de método, não de contagem:** *verificação de guarda pode
mudar o desenho do guarda*, e isso é uma fonte de escopo que a convenção 18 ainda
não nomeava. As fontes já registradas eram descobrir componente novo (erra para
baixo) e descobrir que o repositório já resolveu aquilo (erra para cima). Esta é
uma terceira: **o custo de provar que o guarda funciona pode ser maior que o
custo do guarda.** Aqui foram 188 linhas de infraestrutura de teste para tornar
determinístico o que 8 asserções comportamentais já "afirmavam" — e sem elas a
change teria entregue exatamente o tipo de guarda que existe para eliminar.

**Razão de headline:** não medida por commit ainda, mas previsível e baixa —
sem migração, logo sem `.Designer.cs` carregando o snapshot inteiro do modelo,
que foi a causa do 3,1x da medição anterior. O que infla aqui é `openspec/` mais
documentação (1.458 linhas contra 695 de código), a proporção normal de um carve
pequeno com muito registro.

## Migration Plan

Sem migração de banco, sem mudança de contrato de fio, sem passo de deploy
próprio. Mudança de ordem de itens dentro de listas já existentes, em respostas
cujos campos e tipos não mudam.

Rollback é reverter o commit: nenhum estado persistido muda de forma, então não
há dado a migrar de volta.

Ordem de implementação: baseline (R5) → sites 1 e 2 (SQL, os mais simples, e
estabelecem o idioma) → sites 5, 6, 7, 8 (`CreatedAt`, com o arranjo de D7) →
D3 e sites 3 e 4 (a reestruturação, por último, porque é a que mais mexe no
handler quente) → guarda de R2 → fechamento (`Purpose`, achados).

## Open Questions

Nenhuma. As duas incertezas que o item registrado carregava eram técnicas e
foram fechadas por leitura de código e medição, não por decisão de produto:
o critério do catálogo de bases (D1, premissa registrada estava errada) e a
completude da lista de sites (Verificações 2 e 3).
