## Why

Catálogo de bases, índice vetorial, vínculo agente ↔ base e painel estão
entregues, o índice tem conteúdo gravado — e **ninguém consulta**.
`AgentExecutionService` (`apps/workers`) não tem uma única menção a conhecimento,
e o agente recebe exatamente dois conjuntos de tools: MCP e delegação. Vincular
uma base a um agente hoje **não faz nada em tempo de execução**, e o teste manual
da etapa 5a-2 confirmou isso na prática.

É a etapa que destrava a linha inteira: até ela existir, nada do que o operador
carrega na tela de documentos chega a um agente.

## What Changes

- **Um novo resolvedor de tools em `apps/workers`** (`IKnowledgeToolSetResolver`),
  que monta **uma `AITool` por base de conhecimento vinculada ao agente e ativa**.
  A `Description` da base vira a descrição da tool — é por ela que o modelo decide
  se a base é relevante, e é por isso que `apps/api` já a exige não-vazia desde a
  etapa 1.
- **Nome da tool**: `search_<slug do nome da base>`, mesmo slugificador da
  delegação (remove diacríticos), sanitizado e truncado em 64 caracteres pelo
  `ToolNameSanitizer` já existente.
- **A tool executa busca vetorial exata** sobre `knowledge_fragments`, filtrada
  pela base, ordenada por distância de cosseno, com **`k = 5`** e **sem limiar
  nenhum**. Devolve, por resultado, **três campos**: título do documento no
  catálogo, trecho e **distância explícita**. O agente decide a relevância.
- **Base vinculada sem nenhum fragmento indexado é caso próprio**, distinto de
  "a busca não achou nada": a tool diz que a base ainda não tem conteúdo
  indexado, porque afirmar ter procurado onde não havia onde procurar seria o
  sistema afirmando mais do que sabe.
- **O conjunto de tools entregue ao LLM passa de dois para três conjuntos.**
  `ToolNameDeduplicator` ganha o terceiro conjunto e a precedência declarada
  passa a ser MCP → delegação → conhecimento.
- **Nenhuma mudança de schema, de API HTTP ou de UI.** Nenhuma migração.

**Não entra nesta change** (registrado, com posição na fila): a **busca
unificada** entre todas as bases vinculadas num `ORDER BY` só, que a rodada de
medição `0d` mediu como superior fim a fim (77,1% contra 57,8%) mas apenas
**sob equilíbrio de tamanho entre bases**, e que depende do resolvedor, da
consulta e dos guardas que esta change constrói.

## Capabilities

### New Capabilities
- `knowledge-tool-execution`: resolução do conjunto de tools de conhecimento de
  um agente em tempo de execução — uma tool por base vinculada e ativa, o nome e
  a descrição expostos ao modelo, a busca vetorial por distância com `k` fixo e
  sem limiar, a forma do resultado devolvido ao modelo (incluindo a distância
  explícita e o que o resultado não afirma), e o caso da base sem conteúdo
  indexado.

### Modified Capabilities
- `agent-tool-namespace`: os requisitos afirmam hoje que o conjunto final é "a
  união das tools MCP resolvidas com as tools de delegação resolvidas" e que a
  precedência de colisão é MCP > delegação. Passam a cobrir **três** conjuntos,
  com a precedência estendida e a origem `conhecimento` no aviso de renomeação.

## Impact

**`apps/workers` — o único app tocado.**

- Novo: `Knowledge/Execution/IKnowledgeToolSetResolver.cs`,
  `KnowledgeToolSetResolver.cs`, o tipo do resultado da busca e o construtor do
  nome da tool.
- Modificado: `Agents/AgentExecutionService.cs` (resolve o terceiro conjunto e o
  passa ao deduplicador), `Mcp/ToolNameDeduplicator.cs` (terceiro conjunto),
  `Mcp/ToolOrigin.cs` (terceiro valor), `Program.cs` (registro).
- Reusado sem alteração: `IEmbeddingGeneratorResolver` e `EmbeddingOptions` da
  etapa 2a; `ToolNameSanitizer`; o `AppDbContext` e as entidades espelho
  `KnowledgeBase`, `KnowledgeDocument`, `KnowledgeFragment` e
  `AgentKnowledgeBase`, todas já presentes e até hoje sem leitor.

**Custo de teste que a projeção precisa carregar**: injetar uma dependência nova
em `AgentExecutionService` obriga a registrá-la em **todo** harness que o
constrói — **13 arquivos** (12 em `apps/workers/tests`, 1 em
`tests/InboxOrchestratorRoundTrip.Tests`), mais dois duplos nulos novos. São 15
arquivos de custo de DI, contáveis antes e que não são trabalho de teste.

**Suíte**: os testes de conhecimento exigem Postgres real, porque
`AppDbContext` ignora `KnowledgeFragment` fora de Npgsql. A
`WorkerHostCollection` vai de **11 para 12** classes, e o limiar de carga
declarado para a suíte está calibrado para 7 — **recalibrar é tarefa desta
change**, não descoberta dela.

**Fora de escopo, sem mudança**: `apps/api`, `apps/inbox`, `apps/frontend`,
banco de dados, contrato HTTP.
