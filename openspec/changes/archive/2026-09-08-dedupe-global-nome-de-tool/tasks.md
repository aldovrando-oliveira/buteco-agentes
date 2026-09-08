Todas as tarefas rodam em **`apps/workers`**. Nenhuma toca `apps/api`,
`apps/inbox`, `apps/frontend` ou `libs/`.

A ordem importa: os guardas do grupo 1 são escritos e vistos **reprovar contra o
código de hoje** antes de qualquer correção (convenção 15). Um guarda escrito
depois da correção não prova nada.

## 0. Antes do apply — censo de colisão (nenhuma dependência, nenhuma chave)

- [x] 0.1 **Executado no único ambiente que existe.** Não há ambiente de
      produção — o projeto está todo em desenvolvimento —, então o censo rodou
      onde estão os agentes reais: o Postgres de dev (`podman`,
      `buteco-agents_postgres_1`), com a consulta de V8 do `design.md`.
      **Resultado: zero colisões.** 5 agentes, 2 `McpServer`, 3 vínculos, 2
      delegações → 12 nomes de tool, todos distintos, o mais longo com 41
      caracteres (`delegate_to_especialista-t-cnico-ambiente`), ninguém perto do
      limite de 64 nem do estouro de sufixo de V2.
      Consequência: **R7 não tem alvo hoje** e a change entra sem reescrever
      nenhum nome de tool existente.
      O gate não desaparece, muda de natureza: quando houver produção pela
      primeira vez, o censo entra no **checklist de primeiro deploy**, ao lado da
      aplicação da migration `AddUniqueOpenSessionIndex` — registrado em
      `02-HISTORICO_E_STATUS.md`, subseção "Primeiro deploy em produção".

## 1. Guardas primeiro — escritos e vistos reprovar contra o código atual

- [x] 1.1 Criar `tests/Buteco.Workers.Tests/ToolNameSanitizerTests.cs` (primeiro
      arquivo de teste de `ToolNameSanitizer`): conjunto de caracteres
      (`[a-zA-Z0-9_-]` preservado, o resto vira `_`), inicial inválida ganha `_`,
      entrada vazia, e o par de limite da convenção 5 — nome bem abaixo de 64
      (intocado), nome exatamente em 64 (intocado), nome acima de 64
      (truncado em `MaxToolNameLength`). Estes passam com o código atual: são
      caracterização do que já funciona, não guardas de defeito.
- [x] 1.2 **Corrigido na implementação** — o guarda mora no seam
      (`AgentToolNamespaceTests`), não no resolvedor: o resolvedor continua
      produzindo nomes colidentes por contrato, então o teste aqui virou
      caracterização dessa fronteira (ver divergência 1 no `design.md`).
      Escrever, em `tests/Buteco.Workers.Tests/Mcp/McpToolSetResolverTests.cs`,
      o cenário de **colisão intra-MCP por sanitização**: dois `McpServer`
      chamados `"Zendesk MCP"` e `"Zendesk.MCP"`, ambos oferecendo e permitindo
      `search`. Rodar contra o código atual e **confirmar que reprova** — as duas
      tools saem com o nome `Zendesk_MCP__search`. Se passar verde, o teste está
      errado; corrigir o teste antes de seguir.
- [x] 1.3 **Mesma correção da 1.2.** Escrever, no mesmo arquivo, o cenário de **colisão intra-MCP por
      truncagem**: dois `McpServer` cujos nomes compartilham os primeiros 64
      caracteres e diferem depois. Rodar e **confirmar que reprova** contra o
      código atual.
- [x] 1.4 **Virou dois testes na implementação** (divergência 3 no `design.md`):
      estabilidade entre execuções, que passa hoje, e ordem por `McpServerId`, que
      reprova — com a ordem esperada lida do banco, porque ordenação de `uuid` no
      Postgres não é a de `Guid.CompareTo`. Escrever, no mesmo arquivo, o cenário de **ordem determinística** da
      resolução (V3 do design). Rodar contra o código atual; registrar o
      resultado — este pode passar por acaso, porque a ausência de `orderby` não
      garante ordem *errada*, só não garante ordem *nenhuma*. Se passar,
      documentar no teste que ele prende a ordem que a Decisão 8 estabelece, e
      confirmar a reprovação removendo o `orderby` depois da tarefa 3.3.
- [x] 1.5 Criar `tests/Buteco.Workers.Tests/Agents/AgentToolNamespaceTests.cs`
      com o cenário de **acordo MCP × delegação** (convenção 11): seedar um
      `Agent` Target real e um `McpServer` real cujo `Name` faça os **dois
      resolvedores reais** produzirem o mesmo nome. Nada de lista de tools
      forjada no teste. Usar `WorkerInfrastructureFixture` (Postgres real) e
      `FakeMcpServerHttpMessageHandler` (dependência HTTP externa, permitido pela
      convenção 5). Rodar contra o código atual e **confirmar que reprova**.
- [x] 1.6 Adicionar, no mesmo arquivo, o par "sem colisão" da convenção 5:
      conjunto cujos nomes MCP e de delegação são todos distintos → **nenhum**
      nome alterado, e **nenhum** aviso registrado (asserção negativa explícita
      sobre o logger, que é a que impede um dedupe zeloso demais). Este passa com
      o código atual.
- [x] 1.7 Criar `tests/Buteco.Workers.Tests/ToolNameDeduplicatorTests.cs` com o
      caso do **estouro de 64** (V2 do design): base de exatamente 64 caracteres
      colidindo, resultado esperado ≤64. Escrever também o caso de R2 — **três**
      nomes de 64 caracteres colidindo entre si, todos distintos ao final e todos
      ≤64. Reproduzir primeiro o defeito atual chamando a lógica de sufixo de
      `AgentDelegationToolSetResolver` como está hoje (`$"{baseName}-{suffix}"`)
      e **confirmar que produz 66 caracteres**, antes de existir o deduplicador.
- [x] 1.8 Acrescentar a `Agents/AgentToolNamespaceTests.cs` o cenário de **R7**:
      montar uma sessão cujo histórico contém `FunctionCallContent` com um nome
      de tool ausente da lista atual, e afirmar que o pipeline local
      (`FunctionInvokingChatClient` + `ChatClientAgent`) não lança nem descarta a
      mensagem antes de chegar ao provedor. **Este não é guarda de defeito e deve
      passar com o código de hoje** — é caracterização, como a 1.1: não depende do
      deduplicador, e o que ele estabelece é que o pipeline local é transparente,
      isolando a pergunta de R7 ao provedor, que é o que a 4.7(a) vai observar.
      Se reprovar hoje, o problema é local e não do provedor, e isso muda R7.
- [x] 1.9 Registrar, no resumo desta tarefa, quais dos guardas 1.2, 1.3, 1.5 e
      1.7 reprovaram e com qual mensagem — esses quatro, e só eles, são os que
      **devem** reprovar aqui. A 1.1 e a 1.8 são caracterização e devem passar; a
      1.4 pode passar por acaso (ver a própria tarefa); a 1.6 é o par "sem
      colisão" e deve passar. Guarda que não reprovou aqui não segue para o grupo
      2 sem ser corrigido.

## 2. `ToolNameDeduplicator`

- [x] 2.1 Criar `src/Buteco.Workers/Mcp/ToolNameDeduplicator.cs`: recebe o
      conjunto MCP e o conjunto de delegação já resolvidos e devolve a lista
      unida com nomes únicos. MCP primeiro, delegação depois (Decisão 5), e
      dentro de cada conjunto a ordem de resolução preservada.
- [x] 2.2 Implementar o sufixo numérico no idioma que
      `AgentDelegationToolSetResolver` já usa — `-2`, `-3`, …, primeiro livre
      vence (Decisão 3).
- [x] 2.3 Implementar o encurtamento da base para o sufixo caber em
      `ToolNameSanitizer.MaxToolNameLength` (Decisão 4), reaplicado a cada
      tentativa do loop. Fazer 1.7 passar.
- [x] 2.4 Criar `src/Buteco.Workers/Mcp/RenamedAIFunction.cs`: subclasse mínima
      de `Microsoft.Extensions.AI.DelegatingAIFunction` (construtor `protected`,
      por isso subclasse e não instanciação) sobrescrevendo **só** `Name`. Não
      sobrescrever `JsonSchema`, `Description`, `AdditionalProperties`,
      `JsonSerializerOptions` nem `GetService` — a classe base já encaminha os
      cinco ao inner (V6), e reimplementar qualquer um deles é como se perde o
      schema ou o `GetService<ApprovalRequiredAIFunction>` do caminho de
      aprovação.
- [x] 2.5 Renomear via `McpClientTool.WithName` no lado MCP (V1 — `ProtocolTool`
      sobrevive, a chamada remota segue usando o nome original) e via
      `RenamedAIFunction` no lado delegação. **Não tentar reconstruir o
      `AIFunction`**: o closure com `targetAgentId` que `AIFunctionFactory.Create`
      capturou não é recuperável, e era isso que a redação anterior desta tarefa
      pedia por engano (ver V6). Confirmar em teste que a tool renomeada continua
      **roteando para o destino certo** — a delegação criando task para o
      `targetAgentId` certo, a MCP chamando o servidor certo —, não só que o nome
      mudou.
- [x] 2.6 Comparar nomes com `StringComparison.Ordinal` e construir o
      dicionário de nomes usados com `StringComparer.Ordinal` **explícito**
      (Decisão 9), com comentário registrando que a fonte da escolha é
      `FunctionInvokingChatClient.FindTool` e não preferência. **Corrigido na
      implementação:** o explícito vale pelo que documenta, não porque o default
      seja inseguro — `EqualityComparer<string>.Default` já é ordinal (ver a nota
      de convenção 9 na Decisão 9 do `design.md`).
- [x] 2.7 Acrescentar a `ToolNameDeduplicatorTests.cs` o caso de **caixa**: duas
      tools chamadas `Search` e `search` **não** são renomeadas e **não** geram
      aviso. Este caso vive aqui, e não no grupo 1, porque o guarda dele não pode
      reprovar contra o código de hoje — `ToolNameDeduplicator` não existe no
      grupo 1, então o teste não compila, quanto mais reprova. A verificação de
      convenção 15 dele é outra: escrever o deduplicador com `OrdinalIgnoreCase`
      **de propósito**, ver reprovar, e só então manter o `Ordinal` explícito da
      2.6. **Verificado:** com `OrdinalIgnoreCase` este guarda reprova; com o
      dicionário default implícito **não** reprova, porque esse default já é
      ordinal — a segunda forma que esta tarefa mandava testar não era um defeito,
      e a Decisão 9 foi corrigida.
- [x] 2.8 Emitir o `LogWarning` da Decisão 7 a cada renomeação, com `AgentId`,
      nome pretendido, nome final e a origem de cada lado. Fazer a asserção
      positiva de log de 1.5 e a negativa de 1.6 passarem.
- [x] 2.9 Expor em `src/Buteco.Workers/Mcp/ToolNameSanitizer.cs` o que o
      deduplicador precisa para truncar, sem duplicar a regra de limite. Reescrever
      o comentário da constante com as fontes **primárias** de V4, e só com elas:
      `FunctionObject.name` da especificação OpenAPI publicada pelo OpenAI
      (*"Must be a-z, A-Z, 0-9, or contain underscores and dashes, with a maximum
      length of 64"*), que é a superfície **Chat Completions** — a que
      `ChatClientResolver.BuildOpenAi` usa via `GetChatClient(model).AsIChatClient()`
      —, anotando que a Responses API do mesmo provedor permite 128, para que uma
      migração futura não herde o limite sem saber de onde veio. Registrar o
      Gemini pelo documento de descoberta oficial (128, e **sem** a regra de
      caractere inicial que o comentário de `McpToolSetResolver.cs` afirma hoje —
      corrigir esse comentário também). Registrar explicitamente que o limite do
      **Anthropic não foi verificado em fonte primária** (R8), em vez de citar
      terceiro como autoridade.

## 3. Ligar no fluxo de execução

- [x] 3.1 Substituir `toolSet.Tools.Concat(delegationTools).ToList()` em
      `src/Buteco.Workers/Agents/AgentExecutionService.cs:208` pela chamada ao
      deduplicador, com o comentário que registra a Decisão 1 e por que o dedupe
      mora aqui e não nos resolvedores.
- [x] 3.2 Registrar `ToolNameDeduplicator` na DI em `Program.cs` e injetá-lo em
      `AgentExecutionService`.
- [x] 3.3 Adicionar `orderby` explícito por `McpServerId` na consulta de
      `AgentMcpServers` em `src/Buteco.Workers/Mcp/McpToolSetResolver.cs:37-42`
      (Decisão 8), com o comentário de por que por identificador e não por nome
      (nome é editável). Fazer 1.4 passar, e confirmar sua reprovação removendo o
      `orderby` temporariamente.
- [x] 3.4 Remover o dedupe local de
      `src/Buteco.Workers/AgentDelegations/AgentDelegationToolSetResolver.cs:44,50-55`
      (Decisão 3), deixando o resolvedor devolver o nome-base. Manter o
      `orderby delegation.TargetAgentId` da linha 39 — é o que dá determinismo ao
      lado de delegação.
- [x] 3.5 Ajustar `tests/Buteco.Workers.Tests/AgentDelegationExecutionTests.cs`
      ao novo dono do dedupe. **Não relaxar as asserções**: os dois cenários do
      requisito "Nome estável e sem colisão para a tool de delegação" seguem no
      delta spec com o mesmo texto e precisam passar pelo caminho novo — é a
      contraparte de R4.

## 4. Fechar os guardas e as contrapartes de risco

- [x] 4.1 Rodar os quatro guardas que reprovaram no grupo 1 — 1.2, 1.3, 1.5 e
      1.7, o mesmo conjunto listado na 1.9 — e confirmar que **todos passam**
      agora. Mais a 2.7, cujo guarda de caixa foi verificado dentro do grupo 2.
- [x] 4.2 Contraparte de R1: confirmar em `AgentToolNamespaceTests` que o
      conjunto sem colisão preserva **todos** os nomes exatamente como os
      resolvedores os produziram (1.6), delimitando o alcance da mudança aos
      agentes que já estavam quebrados.
- [x] 4.3 Contraparte de R3: rodar `Mcp/McpToolSetResolverTests.cs` e
      `Mcp/McpToolExecutionEndToEndTests.cs` inteiros e confirmar que os cenários
      existentes de degradação por servidor inacessível e de ciclo de vida da
      conexão continuam verdes com a consulta ordenada.
- [x] 4.4 Contraparte de R4: confirmar por busca no código que
      `AgentDelegationToolSetResolver.ResolveAsync` continua com um único
      chamador de produção (`AgentExecutionService.cs:181`) — se surgir outro
      caminho, ele fica fora do dedupe global e a Decisão 3 precisa ser revista.
- [x] 4.5 Adicionar o cenário de **estabilidade entre execuções** com colisão
      (`agent-tool-namespace`): resolver duas vezes o mesmo cadastro colidente e
      afirmar que os nomes são idênticos e que **a mesma tool** é a renomeada nas
      duas.
- [x] 4.6 **Resultado final: 168/168 verde no modo paralelo default**, 2 m 38 s,
      mais `tests/InboxOrchestratorRoundTrip.Tests` 4/4. `DOCKER_HOST` do Podman
      **e** `TESTCONTAINERS_RYUK_DISABLED=true` — sem a segunda, toda classe com
      Testcontainers reprova em ~1 s e parece falha de teste.

      Caminho até aí, porque o meio importa: na primeira rodada completa
      `TaskJobConsumerTests` reprovava **em bloco** — as 5 no mesmo instante,
      ~14 s, falha de inicialização de fixture e não de asserção. Serializada
      (`-- xUnit.MaxParallelThreads=1`) a suíte fechava 168/168, o que apontava
      contenção de startup de container. **E o flake é desta change, não
      pré-existente:** baseline em worktree limpa de `6956d79` passa
      **132/132 em paralelo**. A causa é cada classe de host usar
      `IClassFixture<WorkerInfrastructureFixture>`, ou seja containers próprios
      de Postgres e RabbitMQ, e esta change acrescentar a 13ª.
      Correção aplicada: `WorkerHostCollection`, uma `[CollectionDefinition]` que
      serializa entre si as 7 classes de host sem tocar no paralelismo do resto —
      custo de 28 s sobre o baseline, para 36 testes a mais.
      A correção de verdade (`ICollectionFixture` com um par de containers
      compartilhado por todas elas) é refatoração de toda a suíte e change
      própria, registrada em `02-HISTORICO_E_STATUS.md` com gatilho na próxima
      classe de host.
- [ ] 4.7 **Aberta por falta de chave de API de provedor** (mesmo bloqueio de
      `0b`), e **não bloqueia esta change**: a tarefa 0.1 veio limpa, então não há
      agente colidente cujo histórico possa ser rejeitado. As duas verificações
      viram itens em aberto com gatilho próprio em `02-HISTORICO_E_STATUS.md`, não
      pendências desta change:
      (a) **R7** — algum provedor rejeita histórico que referencia função ausente
      da lista atual de tools? Gatilho: o **primeiro aviso de renomeação que
      aparecer no log** (o aviso da Decisão 7 é o detector, e ele existe agora).
      A metade local já está verificada: o pipeline
      `FunctionInvokingChatClient`/`ChatClientAgent` é transparente a esse
      histórico (V7 + cenário em `AgentToolNamespaceTests`). O que falta é
      **observação com chave**, não análise.
      (b) **R8** — qual o limite de nome de função do Anthropic? É pergunta de
      fonte, não de ambiente. Gatilho: quando alguém precisar do argumento do
      mínimo entre provedores — mudança de limite, provedor novo, ou migração
      para a Responses API do OpenAI, onde o limite é 128.
      Os dois saem na mesma sessão que `0b`, porque dependem da mesma chave.

## 5. Registro

- [x] 5.1 Se alguma decisão do `design.md` mudar por achado durante a
      implementação, corrigir o `design.md` para refletir a causa real
      (convenção 9) — não deixar só no resumo do chat.
- [x] 5.2 Registrar em `02-HISTORICO_E_STATUS.md` o achado da convenção 15
      encontrado nesta exploração: o guarda de "Distinção de tools com nomes
      iguais entre servidores diferentes" (`mcp-tool-execution`) passava verde
      com o requisito já violado, porque só cobria servidores de nomes
      diferentes. É o quarto caso desta base, e o contador na convenção 15 do
      `01-ARQUITETURA_E_CONVENCOES.md` precisa passar de três para quatro.
- [x] 5.3 Registrar em `02-HISTORICO_E_STATUS.md` o diffstat **decomposto** da
      change (código de produção / código de teste / artefatos OpenSpec,
      separados) contra a projeção por componente do `design.md` — é o dado que a
      convenção 18 precisa acumular para a próxima projeção, e a única forma de
      medir se o método por componente errou menos que o antigo.

- [x] 5.4 Conferir que o item em aberto de UI (nome efetivo × nome cadastrado
      para agentes com colisão) segue registrado em `02-HISTORICO_E_STATUS.md`
      com o gatilho — foi adicionado na revisão desta proposta, e não é trabalho
      desta change.
