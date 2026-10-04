> **Trilha paralela.** Esta change roda na worktree `../buteco-agentes-46`, branch
> `fix/46-timeout-de-conexao`, a partir de `a7960d9`. A linha principal edita
> `02-HISTORICO_E_STATUS.md` e `CHANGELOG.md`; edições curtas e em entrada nova.
> **Antes de cada execução da suíte:** `podman ps` vazio e `uptime` registrado na
> mesma frase do número, no início **e no fim**. Rodada acima de ~10 min não é
> medição. Variáveis: `DOCKER_HOST` do Podman, `TESTCONTAINERS_RYUK_DISABLED=true`,
> `TZ=America/Sao_Paulo`, `DOTNET_SYSTEM_NET_DISABLEIPV6=1`.
>
> Todo o trabalho é em **`apps/workers`**, salvo os artefatos e a documentação.

## 1. Conferência antes do código (exploração, feita)

- [x] 1.1 Baseline em worktree limpa (`a7960d9`), rodada 1: **390/391**, 39 classes,
  7m08s. `podman ps` vazio e load 1,98 na largada; load 10,59 no fim, com a linha
  principal entrando no meio. A falha
  (`KnowledgeIndexingTests.EachFragmentKeepsTheVectorOfItsOwnText_AcrossTheBatchBoundary`,
  5 → 4 fragmentos) passa 3 de 3 com a classe isolada. Virou a **#130**.
- [x] 1.2 Baseline, rodada 2: **abortada aos 34 min**, testhost a 63% de CPU com
  uma classe de host de pé. Não vale como medição. Virou a **#135**.
- [x] 1.3 Baseline, rodada 3, **a de comparação do fechamento**: **391/391**, 39
  classes, 6m17s, com `--blame-hang-timeout 4m`. `podman ps` vazio no início
  (load 7,38) e no fim (load 3,88). A falha da #130 não voltou sem contenção.
  Contagem por classe em `~/buteco-runs/46/baseline3.classes.txt`.
- [x] 1.4 Inventário das chamadas de saída (critério estreito e largo, linhas
  abertas): 6 pontos, com o handler de cada um conferido por decompilação
  (`System.ClientModel` 1.14.0, `Anthropic` 12.39.0, `Google.GenAI` 1.15.0,
  `ModelContextProtocol.Core` 2.0.0). Tabela no `design.md`.
- [x] 1.5 Reprodução sobre `a7960d9`, contra `10.255.255.1` (SYN sem resposta,
  conferido na rede atual): OpenAI 300,3 s / 4 tentativas, embedding 300,0 s / 4,
  Gemini 75,5 s / 1, MCP 60,1 s, Anthropic 30,3 s / 1, push 5,1 s.
  `~/buteco-runs/46/repro-1836.txt`.
- [x] 1.6 Resposta muda depois de conectar (loopback): OpenAI e embedding 400 s /
  4, Gemini 100,4 s, MCP 60,1 s, Anthropic 30,2 s / 1, push 5,1 s. Virou a
  **#134**. `repro-silent-1843.txt`.
- [x] 1.7 `ConnectTimeout` cobre TLS (3,07 s com TLS mudo) e lança
  `TaskCanceledException`. `repro-tls-*.txt`.
- [x] 1.8 Simulação da correção com C = 5 s e C = 10 s: OpenAI 20,2 / 40,1 s,
  Anthropic 5,2 / 10,1 s, Gemini 5,3 / 10,3 s, MCP 15,1 / 20,1 s.
  `repro-fixed-*.txt`.
- [x] 1.9 Lock: o cancelamento normal no `CommandTimeout` termina em 30,03 s; com
  a conexão de cancelamento muda, em 60,02 s. A corrida da aquisição na borda do
  timeout deixa o lock com o backend ocioso no pool (1–2% no arranjo medido):
  virou a **#132**.

## 2. Guardas primeiro, vermelhos contra o código atual (`apps/workers`, testes)

- [x] 2.1 `tests/Buteco.Workers.Tests/Support/SilentTlsListener.cs`: `TcpListener`
  em loopback, porta efêmera, aceita e nunca escreve; descartável, fecha os
  sockets aceitos.
- [x] 2.2 `tests/Buteco.Workers.Tests/Http/OutboundConnectTimeoutTests.cs`, guardas
  de chat OpenAI e embedding OpenAI pelos resolvedores reais (`BaseUrl` no
  listener): falha em < 30 s (o lock; nasceu em 25 s, ver D5) com `TimeoutException`
  de `ConnectTimeout` na cadeia.
  No embedding, também: a exceção do topo **não** é `OperationCanceledException`
  (D4).
- [x] 2.3 Mesma classe, collection com `DisableParallelization`: guardas de
  Anthropic e Gemini por `ChatClientResolver`, apontados por `ANTHROPIC_BASE_URL`
  e `GOOGLE_GEMINI_BASE_URL` (restaurados no fim): falha em < 10 s com
  `ConnectTimeout` na cadeia.
- [x] 2.4 `Mcp/McpToolSetResolverTests.cs`: guarda com o `IHttpClientFactory`
  montado por `McpTransportFactory.AddHttpClient` (D3) e um `McpServer` vinculado
  apontando para o listener: servidor excluído, nenhuma exceção, em < 30 s (nasceu
  em 20 s, ver D5).
- [x] 2.5 Rodar os cinco contra o código atual e registrar a perna vermelha de
  cada um (tempo e motivo). Saída em `~/buteco-runs/46/guardas-vermelho.txt`.
  **Resultado:** os cinco reprovaram pelo orçamento, ainda esperando conexão: chat
  OpenAI e embedding aos 30 s (limite 25 s), Anthropic e Gemini aos 15 s (limite
  10 s), MCP aos 25 s (limite 20 s). A produção só tinha a extração pura do
  registro do MCP (primeira metade de 3.4), sem o timeout.

## 3. Correção (`apps/workers`, produção)

- [x] 3.1 `src/Buteco.Workers/Http/OutboundConnectTimeout.cs`: a constante de 5 s
  com o motivo (D1) e os três handlers estáticos (D2), com o comentário de quem
  depende do transporte OpenAI ser um por processo.
- [x] 3.2 `Agents/ChatClientResolver.cs`: os três `Build*` recebem o handler do
  seu SDK pelo ponto de injeção da tabela do `design.md`.
- [x] 3.3 `Knowledge/Embedding/EmbeddingGeneratorResolver.cs`:
  `Transport = OutboundConnectTimeout.OpenAiTransport`. Reescrever os comentários
  que citam `HttpClientPipelineTransport.Shared` aqui, em
  `Knowledge/Execution/KnowledgeToolSetResolver.cs` e em
  `EmbeddingMetrics/MeasuredEmbeddingGenerator.cs`.
- [x] 3.4 `Mcp/McpTransportFactory.cs`: `AddHttpClient(IServiceCollection)` com o
  registro de hoje mais o `ConnectTimeout`. `Program.cs` passa a chamá-lo, e o
  registro inline sai.
- [x] 3.5 `Mcp/McpToolSetResolver.cs:78` e `:114`: filtro que separa a parada do
  timeout (D4).

## 4. As duas pernas (convenção 15)

- [x] 4.1 Os cinco guardas verdes com a correção, com tempo registrado: Anthropic
  5,3 s, Gemini 5,4 s, chat OpenAI 20,1 s, embedding 20,3 s (agora medido; no
  design estava por leitura), MCP 15,7 s.
- [x] 4.2 Terceira perna do MCP: com 3.4 e sem 3.5, o guarda reprova **por
  exceção** (a `TaskCanceledException` escapa), não por tempo. **Visto:** reprovou
  aos 16,5 s com `TaskCanceledException` ⊃ `TimeoutException` do `ConnectTimeout`.
- [x] 4.3 Reverter cada parte da correção isoladamente (3.2 por provedor, 3.3,
  3.4) e ver o guarda correspondente reprovar, para garantir que cada um está no
  componente que a correção toca. Saída em `~/buteco-runs/46/guardas-pernas.txt`.
  **Resultado:** em cada uma das cinco reversões, só o guarda daquela parte
  reprovou (chat OpenAI e embedding aos 30 s, Anthropic e Gemini aos 15 s, MCP aos
  25 s), e os outros quatro passaram. **Refeito com os limites de 30 s** (D5): de novo
  só o guarda de cada parte reprovou (OpenAI e MCP aos 35 s), e os cinco passaram
  com a correção.

## 5. Suíte

- [x] 5.1 `dotnet build apps/workers/Workers.sln` sem aviso novo: os avisos são os
  de antes (`NU1903` do `SSH.NET` transitivo e `xUnit1031` em
  `ToolNameDeduplicatorTests.cs:150`, arquivo não tocado).
- [x] 5.2 Suíte de `apps/workers` com `--blame-hang-timeout 4m` (a mesma flag da
  baseline 1.3), contra a baseline 1.3 (391/391), **por classe**: as 39
  classes com o mesmo número, mais a classe nova e os casos novos de
  `McpToolSetResolverTests`. Qualquer diferença fora delas é explicada antes de
  seguir. **Resultado: 396/396, 41 classes, 7m45s.** Por classe, só mudaram as duas
  classes novas (2/2 e 2/2) e `McpToolSetResolverTests` (20 → 21); as outras 38
  iguais à baseline. Na largada (load 4,90) e no fim (4,33), três contêineres
  **ociosos** de outra sessão na VM (`funny_elion`, órfão de Testcontainers, e
  `buteco-105-verify-*`), e **zero** processos de teste de outras sessões nas
  amostras a cada 15 s (`~/buteco-runs/46/final3-amostras.txt`). Duas rodadas antes
  desta não contam: 396/396 em 7m59s (a linha principal subiu contêiner 1,5 min
  depois da largada) e 394/396 em 13m21s (linha principal rodando suíte no meio; as
  duas falhas foram o guarda de embedding aos 25,6 s com o limite antigo de 25 s,
  que levou à mudança do D5, e a #130, comentada lá).

## 6. Fechamento

- [x] 6.1 Abrir a issue `tipo: investigação` (rascunho entregue com o propose):
  esperas de 83,6 s e 103,6 s sem explicação, a #132 como candidata para o "dono
  invisível", o que foi descartado, e a leitura de `pg_locks` com
  `pg_stat_activity` para a próxima ocorrência. **Aberta: #139.**
- [x] 6.2 `/opsx:sync`, conferindo a spec nova requisito a requisito; `Purpose`
  real, não placeholder. Spec principal criada com 3 requisitos e 7 cenários,
  iguais à delta. O cenário "conexão que se estabelece dentro do prazo não é
  afetada" vale por construção e não tem guarda próprio: virou a **#141**.
- [x] 6.3 `/opsx:archive`.
- [x] 6.4 Entrada nova no `02-HISTORICO_E_STATUS.md` e no `CHANGELOG.md`: o que
  mudou, baseline e número final por classe, D1–D6 com o motivo, as issues #130,
  #132, #134 e a de investigação, e o que a investigação descartou.
- [x] 6.5 `docs/architecture.md` ou `docs/configuration.md`, se algum descrever
  timeout de saída do worker (conferir antes de editar). **Conferido: nenhum
  descreve**, e a change não cria configuração. Sem edição.
- [x] 6.6 `openspec validate --all` e `scripts/check-docs.py` limpos (68/68; integridade
  da documentação OK).
