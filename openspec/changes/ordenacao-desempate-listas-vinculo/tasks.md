Todas as tarefas rodam em **`apps/api`**, exceto as de fechamento (grupo 8), que
mexem só em documentação do repositório. `apps/workers`, `apps/inbox` e
`apps/frontend` não são tocados por nenhuma tarefa.

## 1. Baseline (convenção 19)

- [x] 1.1 (`apps/api`) Rodar a suíte de `apps/api` num `git worktree` limpo em
  `main` **antes** de qualquer edição, e guardar o resultado. Sem esta linha de
  base, nenhuma falha posterior pode ser classificada como pré-existente.
  Nesta máquina (macOS + Podman, sem Docker Desktop) o Testcontainers exige
  **duas** variáveis, não uma — sem qualquer das duas a suíte reprova inteira em
  ~1s e o erro parece falha de teste:
  `export DOCKER_HOST="unix://$(podman machine inspect --format '{{.ConnectionInfo.PodmanSocket.Path}}')"`
  e `export TESTCONTAINERS_RYUK_DISABLED=true`.
- [x] 1.2 (`apps/api`) Registrar quais testes falham na baseline. A falha
  conhecida é o flake de ordem de execução de `AgentDeactivationTests`
  (`AgentDeactivationFixture.TaskJobPublisher` é instância única da classe e
  `PublishedMessages` acumula; passa isolado) — item aberto próprio, não desta
  change. Baseline vermelha por esse motivo remove a hipótese de regressão e
  **só** ela: não promove nenhum outro sintoma a "ambiental".

## 1b. Metade determinística dos guardas (D6, revisto na implementação)

Acrescentado depois da projeção: a conferência da convenção 15 mostrou que o
guarda comportamental de desempate reprova só probabilisticamente (design.md, D6
"Correção feita durante a implementação"). Cada site passa a ter duas metades.

- [x] 1b.1 (`apps/api`) Escrever em `tests/Buteco.Api.Tests/Support/` o helper
  que captura o SQL emitido pelo EF Core (`Executed DbCommand`) durante uma
  requisição HTTP real, via logger da `WebApplicationFactory`. Tem de capturar a
  consulta **de produção**, nunca uma remontada no teste (convenção 11).
- [x] 1b.2 (`apps/api`) Conferir o próprio helper: com o `ThenBy` presente ele
  enxerga o desempate no `ORDER BY`; sem o `ThenBy`, não enxerga. Helper que
  devolve vazio ou não casa a consulta certa deixa todas as asserções verdes.
- [x] 1b.3 (`apps/api`) Conferir que a asserção determinística reprova em
  **todas** as execuções contra o defeito, não na maioria — é o critério de
  aceitação que substitui a reprovação do guarda comportamental (R1).

## 2. Sites de nome em SQL — sites 1 e 2

Os dois mais simples primeiro, porque estabelecem o idioma que os demais copiam.

- [x] 2.1 (`apps/api`) Escrever o guarda de desempate de `delegatesTo` em
  `AgentDelegationEndpointsTests.cs`: agentes-alvo homônimos, vinculados em ordem
  de inserção **oposta** à ordem crescente de `id`, asserção sobre **ordem
  crescente de `id`** (nunca "duas consultas concordam"), cobrindo
  `GET /agents/{id}` e `GET /agents`. Separado do teste de ordenação por nome.
- [x] 2.2 (`apps/api`) Conferir que 2.1 **reprova** contra o código atual, e que
  o teste de ordenação alfabética existente **continua passando** — guarda que
  reprova os dois está afirmando no componente errado (convenção 15, segunda
  metade). *Feito, e foi o que revelou o problema de D6: o de `delegatesTo`
  reprovou, o de `mcpServers` reprovou em 2 de 3 execuções da classe.*
- [x] 2.2b (`apps/api`) Acrescentar a metade determinística (1b) aos guardas de
  `delegatesTo` e `mcpServers`.
- [x] 2.3 (`apps/api`) Acrescentar `.ThenBy(agent => agent.Id)` em
  `AgentDelegations/AgentDelegationLookup.cs:23`, com comentário curto no molde
  do que já existe em `AgentKnowledgeBaseLookup.cs:21-30`. Conferir que 2.1 passa.
- [x] 2.4 (`apps/api`) Remover o `ThenBy` de propósito e conferir que reprova
  **só** 2.1, e nenhum guarda de outro site. Restaurar.
- [x] 2.5 (`apps/api`) Repetir 2.1-2.4 para `mcpServers` em
  `AgentMcpBindingEndpointsTests.cs` e
  `AgentMcpBindings/AgentMcpServerLookup.cs:22`
  (`.ThenBy(joined => joined.McpServer.Id)`).
- [x] 2.6 (`apps/api`) Par "sem empate" (convenção 5) para os dois: lista de
  vínculos com nomes todos distintos, afirmando que a ordem é a do critério
  primário e o desempate não a altera.

## 3. Sites de `CreatedAt` — sites 5, 6, 7 e 8

O arranjo é o custo real aqui (design.md, D7): `CreatedAt` é `private set`
atribuído a `UtcNow` no construtor, então o empate tem de ser forçado escrevendo
no banco depois da criação, via `AppDbContext` resolvido do escopo da fixture.
Precedente idiomático: `AgentEndpointsTests.cs:337,357` e
`McpServerEndpointsTests.cs:132,149`.

- [x] 3.1 (`apps/api`) Escrever o helper de arranjo que força dois ou mais
  registros a compartilhar `CreatedAt`, e conferir que ele realmente produz o
  empate (ler de volta do banco e afirmar a igualdade) — arranjo que
  silenciosamente não empata deixa os quatro guardas do grupo verdes com e sem a
  correção, que é o defeito de guarda da convenção 15.
- [x] 3.2 (`apps/api`) Guarda do site 6 em `AgentEndpointsTests.cs`: agentes com
  `CreatedAt` idêntico e ordem de `id` oposta à de cadastro, asserção sobre ordem
  crescente de `id` entre os empatados, mantida a ordem por `CreatedAt` em
  relação aos demais. Conferir que reprova antes da correção.
- [x] 3.3 (`apps/api`) Acrescentar `.ThenBy(agent => agent.Id)` em
  `Agents/Queries/ListAgents/ListAgentsQueryHandler.cs:21`. Conferir que 3.2
  passa, e que remover o `ThenBy` reprova **só** 3.2.
- [x] 3.4 (`apps/api`) Repetir 3.2-3.3 para o site 7 em
  `McpServerEndpointsTests.cs` e
  `McpServers/Queries/ListMcpServers/ListMcpServersQueryHandler.cs:14`.
- [x] 3.5 (`apps/api`) Repetir 3.2-3.3 para o site 5 em
  `Knowledge/KnowledgeBaseCatalogTests.cs` e
  `KnowledgeBases/Queries/ListKnowledgeBases/ListKnowledgeBasesQueryHandler.cs:20`.
  **Sem trocar o critério para nome** — design.md, D1.
- [x] 3.6 (`apps/api`) Repetir 3.2-3.3 para o site 8 em
  `Knowledge/KnowledgeDocumentCatalogTests.cs` e
  `KnowledgeDocuments/Queries/ListKnowledgeDocuments/ListKnowledgeDocumentsQueryHandler.cs:33`.
- [x] 3.7 (`apps/api`) Par "sem empate" para os quatro catálogos: registros com
  `CreatedAt` distintos, afirmando ordem de cadastro preservada. O par "catálogo
  vazio" já existe (`Knowledge/KnowledgeEmptyCatalogTests.cs`) — conferir que
  cobre e não duplicar.

## 4. Ordenação da listagem vai para o SQL — D3, sites 3 e 4

Por último entre as correções, porque é a que mais mexe no handler mais quente
do app.

- [x] 4.1 (`apps/api`) Escrever o guarda de R2 em
  `AgentMcpBindingEndpointsTests.cs`: dois servidores MCP vinculados com nomes
  que diferem só em caixa e pontuação — o par medido é `suporte-alfa` e
  `Suporte Alfa` —, afirmando que `GET /agents/{id}` e `GET /agents` devolvem
  `mcpServers` na mesma ordem, **e que essa ordem é a da collation do banco**
  (`suporte-alfa` primeiro). Citar no comentário a medição do design.md
  (`postgres:18`, `datcollate=en_US.utf8`, `datlocprovider=c`; .NET/ICU ordena o
  inverso nas três culturas testadas).
- [x] 4.2 (`apps/api`) Conferir que 4.1 **reprova contra o código atual** — é o
  único guarda desta change que reprova por um defeito que o `ThenBy` não
  conserta, e é o que prova que a Verificação 3 do design.md descreve um defeito
  real e não uma teoria.
- [x] 4.3 (`apps/api`) Em `ListAgentsQueryHandler.cs`, mover a ordenação de
  `mcpServers` para a consulta em lote (`.OrderBy(x => x.McpServer.Name)
  .ThenBy(x => x.McpServer.Id)` antes do `ToListAsync`) e **remover** o
  `OrderBy` em memória de dentro do `GroupBy` (linha 36). Comentar no ponto que
  a ordem vem da consulta de propósito, com o motivo — sem isso, um "reparo"
  futuro reintroduz o `OrderBy` em memória e volta a divergir sem quebrar nada
  visível (R2).
- [x] 4.4 (`apps/api`) Mesma correção para `delegatesTo` (linha 53) e para
  `knowledgeBases` (linhas 75-76 — este já tem o desempate desde a etapa 3, mas
  ainda ordena em memória, então cumpre o requisito com o comparador errado).
- [x] 4.5 (`apps/api`) Guarda de R2 para `delegatesTo` em
  `AgentDelegationEndpointsTests.cs` e para `knowledgeBases` em
  `AgentKnowledgeBindingEndpointsTests.cs`, no mesmo molde de 4.1.
- [x] 4.6 (`apps/api`) Conferir que os guardas de desempate dos grupos 2 e 3
  continuam passando, e que
  `KnowledgeBasesWithEqualNames_AreTieBrokenByIdDeterministically`
  (`AgentKnowledgeBindingEndpointsTests.cs:354`) — que já cobria as duas
  superfícies — continua verde sem alteração.
- [x] 4.7 (`apps/api`) Reintroduzir o `OrderBy` em memória de propósito e
  conferir que reprova **os guardas de R2 e só eles**; os guardas de desempate
  por id não devem reprovar, porque a ordem de `Guid` é idêntica nos dois
  comparadores (design.md, Verificação 4).

## 5. Conferência de não-regressão

- [x] 5.1 (`apps/api`) Rodar a suíte inteira de `apps/api` e comparar com a
  baseline do grupo 1. Qualquer falha nova é regressão desta change até prova em
  contrário — não classificar nada como ambiental sem o worktree limpo.
- [x] 5.2 (`apps/api`) Conferir que `AgentResponseWireFormatTests.cs` e os
  cenários existentes de `AgentEndpointsTests.cs` **não precisaram de alteração**
  (R4). Cenário que precise ser reescrito para acomodar a reestruturação de D3 é
  sinal de mudança de comportamento não pretendida — reportar como achado antes
  de alterar o teste.
- [x] 5.3 (`apps/api`) Conferir que nenhuma migração foi gerada e que o
  `AppDbContextModelSnapshot.cs` está intocado — esta change não muda o modelo
  (design.md, D5).

## 6. Spec e `Purpose`

- [ ] 6.1 **Roda no `/opsx:archive`, não aqui** — esta versão do CLI não tem
  `openspec sync` autônomo; o archive é que aplica as deltas. Sincronizar as
  deltas para as specs vivas
  (`api-response-ordering` como capability nova, `agent-knowledge-binding` como
  modificada).
- [ ] 6.2 Conferir que `openspec/specs/api-response-ordering/spec.md` ficou com
  o **`Purpose` real** que a delta traz, e não com o placeholder
  `TBD - defined by change ...`. Se a sincronização escrever o placeholder,
  corrigir o arquivo vivo na mesma passada. Este é o gatilho fixado em
  09/09/2026 (39 de 43 capabilities estão com placeholder porque "escrever depois
  do archive" nunca teve dono), e esta é a primeira change a exercê-lo.
- [ ] 6.3 Conferir que `agent-knowledge-binding` continua com o `Purpose` real
  que já tem — é uma das quatro capabilities que não estão com placeholder, e a
  sincronização de uma delta MODIFIED não deve sobrescrevê-lo.

## 7. Medição (convenção 18, sétima da série)

- [x] 7.1 Medir o diffstat **decomposto** do trabalho: criados × modificados,
  produção × teste, código × `openspec/`. Comparar com a projeção do design.md
  (0 criados; 6 produção + 7 teste modificados; 320-400 linhas de código).
- [x] 7.2 Registrar a razão de headline (diffstat do commit ÷ código à mão) e
  anotar que esta rodada não tem migração, logo não tem `.Designer.cs` com
  snapshot inteiro do modelo — a causa do 3,1x da medição anterior.
- [x] 7.3 Se a contagem errar, registrar **a causa estrutural**, não um fator de
  correção. O ponto sob teste nesta rodada é se o método de contar criados e
  modificados separadamente a partir do blast radius lido no código continua
  acertando num escopo que é 100% modificação. Escopo acrescentado durante a
  implementação entra na conta do entregue, não na do erro de método.

## 8. Fechamento dos achados fora de escopo

Documentação do repositório, nenhum app tocado.

- [x] 8.0a Registrar em `02-HISTORICO_E_STATUS.md` a premissa que D3 introduz
  (`Enumerable.GroupBy` preservando a ordem de origem), com a dependência
  correta (runtime .NET, não EF Core/Npgsql), a citação da documentação, o
  ponteiro para a medição, o gatilho no bump do runtime e o modo de falha real —
  detectável pelos guardas de R2, mas **por probabilidade**. Feito antes do
  apply.
- [x] 8.0b Registrar a causa da premissa errada como acréscimo à **convenção 6**
  em `01-ARQUITETURA_E_CONVENCOES.md` (repositório descrito de memória dentro de
  item aberto; conferir referência não é conferir afirmação), com gatilho de
  reavaliação na segunda ocorrência. Feito antes do apply.
- [x] 8.0c Registrar em `01-ARQUITETURA_E_CONVENCOES.md`, na convenção 15, a
  quinta forma do erro achada aqui: **guarda comportamental cujo critério é uma
  ordem que o banco às vezes já produz sozinho não reprova de forma
  determinística** — ordem de `id` é exatamente isso, porque um index scan pela
  chave primária a emite. Medido nos dois sentidos: o guarda novo de `mcpServers`
  passou com o defeito presente em 1 de 3 execuções da classe, e o guarda
  entregue pela etapa 3 reprovou 4/4 na sua forma de consulta — ou seja, a
  confiabilidade depende do plano, e "reprovou quando eu conferi" não é
  propriedade durável. A saída é parear o cenário comportamental com uma asserção
  determinística sobre o artefato que a correção produz (aqui, o SQL emitido).
- [x] 8.1 Atualizar o item "Ordenação sem desempate, em cinco sites de
  `apps/api`" em `02-HISTORICO_E_STATUS.md`: marcar como fechado por esta change,
  e **corrigir o registro** — eram oito sites, não cinco, e a premissa de que "o
  catálogo de MCP e o de agentes ordenam por nome" estava errada (os quatro
  catálogos ordenam por `CreatedAt`; design.md, D1).
- [x] 8.2 Abrir o item de `apps/inbox` com os seis sites, as linhas, a nota de
  severidade (critérios temporais alimentados por `UtcNow`, logo empate exige
  mesmo microssegundo) e a observação de que dois deles têm promessa de ordem em
  spec viva (`inbox-contact-session`, `inbox-message-history`) — lá a change
  conserta requisito violado, não acrescenta. Gatilho: imediato, change própria
  (design.md, A1).
- [x] 8.3 Registrar `ContactSessionResolver.cs:53` como **não-achado**, com o
  motivo (índice único parcial em `sessions.ContactId WHERE "ClosedAt" IS NULL`,
  `AppDbContext.cs:100-102`, e o `Where` da consulta é exatamente
  `ClosedAt == null`: no máximo uma linha casa), para a próxima varredura não o
  levantar de novo (design.md, A2).
- [x] 8.4 Anotar `A2A/PostgresTaskStore.cs:77` no item de `apps/api`: sem
  desempate **e com `.Take(PageSize)`**, então paginação sobre ordem instável
  pode repetir ou omitir linhas. Gatilho: o primeiro consumidor de
  `ListTasksAsync` em `apps/api` — o mesmo gatilho da lacuna de `AgentId` já
  anotada no comentário do próprio arquivo; as duas se corrigem juntas
  (design.md, A3).
- [x] 8.5 Abrir o item de `apps/frontend`: `knowledgeBaseRows.ts:26-27`
  reordena no cliente com um terceiro comparador (`localeCompare`), enquanto
  `agent-knowledge-binding-ui:62-66` diz que a interface reproduz a ordem da API.
  Depois desta change a API tem uma ordem só, o que torna a reordenação
  desnecessária e o último ponto onde a ordem pode divergir. Gatilho: imediato, e
  é change de `apps/frontend`, nunca de `apps/api` (design.md, A4).
- [x] 8.6 Registrar em `01-ARQUITETURA_E_CONVENCOES.md` o que esta change
  acrescenta à convenção 6: a divergência entre a collation do PostgreSQL e o
  comparador de `string` do .NET é comportamento de dependência que **não** se
  presume, e a medição (par de nomes, versão, provedor de collation) é o que
  torna o guarda possível. Vale registrar junto o resultado negativo útil: a
  crença de que `Guid.CompareTo` compara o primeiro campo com sinal, e portanto
  discordaria do `uuid` do PostgreSQL, **é falsa** no .NET 10 — medido em 100.000
  pares e nos casos de fronteira, zero discordâncias. Sem essa medição o
  desempate por `Id` nas duas superfícies seria uma aposta.
