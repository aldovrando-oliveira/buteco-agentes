## 0. Conferência de escopo de arquivo

**Lista fechada de caminhos permitidos.** Qualquer arquivo fora desta lista que
apareça no `git status` ao fim é achado a reportar, não a commitar. **Nenhuma
migração, nenhuma rota, nenhuma tela, nada em `libs/` nem em `apps/frontend`.**
`apps/api` e `apps/inbox` entram **só** com o arquivo de configuração — nenhum
`.cs` dos dois pode aparecer.

```
# apps/workers — produção
apps/workers/src/Buteco.Workers/appsettings.Production.json                              (novo)
apps/workers/src/Buteco.Workers/Agents/AgentExecutionService.cs
apps/workers/src/Buteco.Workers/Agents/LlmCallDurationChatClient.cs
apps/workers/src/Buteco.Workers/ExecutionMetrics/CompactionCallChatClient.cs
apps/workers/src/Buteco.Workers/ExecutionMetrics/ExecutionMetricsScope.cs
apps/workers/src/Buteco.Workers/Knowledge/Indexing/EmbeddingBatchSizeValidation.cs
apps/workers/src/Buteco.Workers/Knowledge/Indexing/EmbeddingIndexConsistencyValidation.cs

# apps/workers — teste
apps/workers/tests/Buteco.Workers.Tests/ExecutionMetrics/CompactionCallChatClientTests.cs   (novo)
apps/workers/tests/Buteco.Workers.Tests/ExecutionMetrics/ExecutionMetricsScopeTests.cs
apps/workers/tests/Buteco.Workers.Tests/Agents/LlmCallDurationChatClientTests.cs
apps/workers/tests/Buteco.Workers.Tests/HistorySummarizationTests.cs
apps/workers/tests/Buteco.Workers.Tests/Knowledge/EmbeddingBatchSizeValidationTests.cs      (ver nota)
apps/workers/tests/Buteco.Workers.Tests/Knowledge/EmbeddingIndexConsistencyTests.cs         (ver nota)

# apps/api e apps/inbox — só configuração
apps/api/src/Buteco.Api/appsettings.Production.json                                      (novo)
apps/inbox/src/Buteco.Inbox/appsettings.Production.json                                  (novo)

# registro
01-ARQUITETURA_E_CONVENCOES.md                                           (ver nota 2)
docs/architecture.md                                                     (ver nota 2)
02-HISTORICO_E_STATUS.md
CHANGELOG.md
openspec/changes/compactacao-historico/**
openspec/changes/archive/2026-09-22-metricas-execucao-coleta/design.md   (convenção 9, só o registro da causa)
```

**Nota — desvio da lista original, com a causa (aberto no apply, 22/09/2026).**
O `design.md` previa um arquivo novo,
`Knowledge/EmbeddingStartupValidationLogTests.cs`, para os guardas do escopo 4.
**Não cabe:** `ValidateEmbeddingIndexConsistency` **consulta o banco**, e a
classe que a cobre hoje (`EmbeddingIndexConsistencyTests`) já está na coleção de
hosts, com fixture de Postgres. Um arquivo novo com fixture seria a **15ª**
classe da coleção — par próprio de containers e recalibração obrigatória da
referência de duração da suíte (o custo que `EmbeddingBatchSizeValidationTests`
já registra no próprio comentário por ter ficado **fora** da coleção). Os dois
guardas vão para as classes que já cobrem cada checagem. **Efeito na projeção:
criados (teste) 2 → 1, modificados (teste) 3 → 5; contagem de casos inalterada
(16).** Comparado, não reprojetado, na tarefa 7.4.

**Nota 2 — documentação de arquitetura, acrescentada a pedido (22/09/2026,
depois da verificação).** Nenhum dos dois arquivos estava na lista: a change
nasceu como correção de defeito, e o `design.md` só previa `02` e `CHANGELOG`.
**A lacuna é anterior a ela:** nem `docs/architecture.md` nem o `01` diziam
como o histórico de conversa é persistido, quais são os dois limites e por que
eles têm papéis diferentes — e agora havia uma restrição de provedor a
registrar. Acrescentada uma seção em cada, no registro de cada um: a pública
com o mecanismo e a consequência operacional, a interna com nomes de classe,
constantes e changes de origem. `scripts/check-docs.py` verde nos dois.
**Efeito na projeção: registro 2 → 4 arquivos, +120 linhas** — comparado, não
reprojetado (7.4).

**D3, custo de DI zero:** os 14 arquivos de teste que registram
`AgentExecutionService` **não mudam** — `ILoggerFactory` sai do mesmo registro
que já serve o `ILogger<AgentExecutionService>` de hoje. Três deles estão na
lista acima por outro motivo (recebem guardas). Se algum dos outros onze
aparecer no `git status`, ou se alguma linha de registro de serviço existente
for alterada nos três, **D3 caiu, e isso é achado a reportar antes de seguir**.

- [x] 0.1 Ao fim da change, conferir `git status` contra a lista acima.
      **CONFERIDO:** 19 caminhos no `git status`, **todos na lista** (com a nota
      de desvio acima). Nenhum arquivo fora dela; nenhum `.cs` de `apps/api` nem
      de `apps/inbox`; nenhum dos treze harness de teste não listados.

## 1. Baselines e pré-condições

- [x] 1.1 (`apps/workers`, `apps/api`, `apps/inbox`) Registrar as baselines
      **antes de tocar em qualquer arquivo**, com o regime colado (convenção
      22): data, hora, fuso, commit, duração. Pré-condição desta máquina:
      `DOCKER_HOST` do Podman **e** `TESTCONTAINERS_RYUK_DISABLED=true`, com
      `podman ps` devolvendo zero contêiner de teste antes de começar. **Não
      herdar número de leitura nem de memória:** o último registrado para
      `apps/workers` é a projeção 319/319 da `metricas-execucao-coleta`, e
      durante o apply dela a mesma suíte chegou a dar 317/319. Se a medição não
      bater com o esperado, a divergência é achado a explicar antes de seguir.
      Anotar também o número de classes na `WorkerHostCollection`, por busca
      ancorada (`grep -rn "^\[Collection("`).

      **MEDIDO** — 22/09/2026, 21:46–21:56, `America/Sao_Paulo`, Darwin 24.6.0,
      commit `4e6707b`, árvore limpa (só `openspec/changes/compactacao-historico/`
      não rastreado), `DOCKER_HOST` do Podman e `TESTCONTAINERS_RYUK_DISABLED=true`,
      três contêineres de **desenvolvimento** no ar (postgres, rabbitmq, waha) e
      nenhum de teste:

      | suíte | resultado | duração |
      |---|---|---|
      | `apps/workers` | **324/324** | 9m37s |
      | `apps/api` | **346/346** | 3m54s |
      | `apps/inbox` | **202/203** (uma falha) | 33s |

      **`WorkerHostCollection`: 14 classes** (busca ancorada).

      **Duas divergências contra o esperado, explicadas antes de seguir:**

      1. **`apps/workers` deu 324, não 319.** A projeção de 319 é da
         `metricas-execucao-coleta` e foi fixada **antes** do escopo 2 dela
         (D17), que nasceu no apply: os guardas de estado terminal — a
         `[Theory]` de quatro estados mais o par — entraram em
         `TaskJobConsumerTests` depois da projeção (`2a9ff6a`, +543 linhas
         naquele arquivo). 319 + 5 = 324. **Não é regressão; é a projeção que
         está velha**, e é por isso que a baseline se mede.
      2. **`apps/inbox` deu 202/203**, falhando
         `DebounceSweepServiceTests.InfrastructureFailure_ProcessingOneCandidate_IsLoggedAndDoesNotBlockAnotherCandidate`
         num `PollUntil` estourado. **Flake, confirmado por repetição em `HEAD`
         limpo:** a classe isolada deu **10/10 em duas rodadas** (21:51 e
         21:52). **É achado a registrar**, porque contradiz o `02`, que afirma
         que `apps/inbox` "não tem mais nenhum flake conhecido — suíte completa
         160/160". Nada a ver com esta change (nenhum arquivo tocado ainda), e
         a baseline de comparação da 7.2 é **203 casos, com esse flake
         conhecido sob a suíte completa**.
- [x] 1.2 (`apps/workers`) Conferir que os dez `.csproj` compilam em `HEAD`
      limpo — **não há `.sln` na raiz**, então a régua de assinatura é compilar
      todos, não abrir um `.sln`.
      **MEDIDO:** os dez `OK`, 22/09/2026 21:58 `America/Sao_Paulo`, `4e6707b`.
- [x] 1.3 (`apps/workers`) Confirmar por leitura que nenhum outro ponto do boot
      dependia da consulta do EF que a D5 silencia (risco declarado no
      `design.md`). Se houver outro, é linha própria de log, dentro do escopo 4.
      **CONFERIDO** (`Program.cs:104-122`): o boot faz `ValidateTimeZoneConfiguration`
      (lê configuração), `ValidateEmbeddingBatchSize` (lê configuração) e
      `ValidateEmbeddingIndexConsistency` — **a única que consulta o banco**
      (`EmbeddingIndexConsistencyValidation.cs:52-60`, `SELECT DISTINCT` sobre as
      três colunas de proveniência). Nenhum outro ponto do boot dependia daquela
      consulta. **Achado para a 5.3:** essa checagem tem **dois** caminhos de
      sucesso — índice vazio (`:64-67`) e índice consistente (`:85-90`) —, e eles
      dizem coisas diferentes: a linha de log precisa distinguir os dois, senão
      "primeiro deploy" e "conferido contra o índice" ficam indistinguíveis, que é
      a mesma forma de silêncio ambíguo que o escopo existe para remover.

## 2. Escopo 1 — a requisição de compactação deixa de terminar em turno de modelo

- [x] 2.1 (`apps/workers`, teste) Criar `CompactionCallChatClientTests` com os
      **quatro** casos: lista que termina em assistente ganha mensagem final de
      usuário; lista que já termina em usuário passa intacta; as mensagens do
      histórico não mudam de papel, conteúdo nem ordem; a finalidade
      `Compaction` continua sendo marcada no escopo. **Vermelho contra `HEAD`**
      nos dois primeiros (hoje o wrapper delega a lista intacta) — registrar a
      saída vermelha antes de corrigir (convenção 15).
      **VERMELHO REGISTRADO** (22/09/2026 22:0x, `4e6707b` + só este arquivo):
      `Com falha: 2, Aprovado: 3, Total: 5` — falharam os dois casos de lista
      terminada em assistente (um e dois turnos), e passaram desde já o par
      condicional, a integridade do histórico e a marca de finalidade. **Cinco
      casos, não quatro:** acrescentei o de **dois turnos**, que é a forma exata
      que o piloto produziu no 12º turno (`[s,u,a,u,a]`) e que a de um turno não
      cobre. Diferença contra a projeção comparada na 7.4, não reprojetada.
- [x] 2.2 (`apps/workers`) Implementar a D1 no `CompactionCallChatClient`:
      inspeção da última mensagem e acréscimo condicional. O texto da mensagem é
      o da D2 (`End of the conversation to summarize.`), com o registro ao lado
      dizendo que ele é lido pelo modelo, que o `system` do pacote é quem
      comanda, e que trocá-lo muda o resumo.
      **VERDE:** `5/5`. Os dois caminhos do client (não-streaming e streaming)
      passam pela mesma função, pelo mesmo motivo que a coleta instrumentou os
      dois.
- [x] 2.3 (`apps/workers`, teste) Acrescentar a `HistorySummarizationTests` o
      guarda de patamar: conversa longa **com tool chamada**, duplo de provedor
      que **recusa requisição terminada em turno de modelo**, e asserção de que
      a entrada não cresce monotonicamente. **A medida conta todo o conteúdo, não
      só `ChatMessage.Text`** — escrever ao lado do guarda a medição que
      justifica isso (os 185–189 tokens idênticos nos dois cenários, exploração
      de 22/09/2026).
      **MEDIDO** (22/09/2026 22:1x): verde com a correção (`1/1`, 6 s) e
      **vermelho sem ela** — a correção foi guardada com `git stash` só do
      arquivo de produção e o mesmo teste reprovou. O duplo de provedor recusa
      requisição terminada em turno de modelo, como o Gemini, e a tool é chamada
      em **todo** turno (não a cada três, como na exploração): sem oscilação, o
      patamar é afirmável contra uma referência única.
- [x] 2.4 (`apps/workers`) Rodar os guardas 2.1 e 2.3 verdes, e a
      `HistorySummarizationTests` inteira sem regressão.
      **MEDIDO:** `CompactionCallChatClientTests` **5/5**;
      `HistorySummarizationTests` **8/8** em 54 s — os sete que já existiam mais
      o de patamar, sem regressão.

## 3. Escopo 2 — a falha de resumo passa a aparecer

- [x] 3.1 (`apps/workers`, teste) Guarda em `HistorySummarizationTests`: com a
      chamada de resumo falhando, existe linha de nível aviso identificando a
      falha, e o turno do usuário ainda chega a `completed`. **Vermelho contra
      `HEAD`** — hoje o aviso vai para `NullLogger`.
      **VERMELHO REGISTRADO:** `Assert.Contains() Failure: Filter not matched in
      collection` — nenhuma linha de aviso existia. **VERDE depois da 3.2.** O
      guarda afirma a existência da linha em nível aviso, não o texto dela: a
      mensagem é do pacote, e prendê-la inteira seria prender versão de
      dependência.
- [x] 3.2 (`apps/workers`) Injetar `ILoggerFactory` em `AgentExecutionService` e
      repassá-lo ao `CompactionProvider` (D3), com o registro do contraste com a
      D2 da `metricas-execucao-coleta` ao lado.
- [x] 3.3 (`apps/workers`) Compilar os dez `.csproj` e rodar a suíte:
      **nenhum harness de teste pode ter sido editado** — é o que prova o custo
      de DI zero. Conferir contra a tarefa 0.
      **MEDIDO:** os dez `.csproj` `OK`, e o `git status` mostra só os arquivos
      da lista fechada — `HistorySummarizationTests` aparece por receber
      guardas, **nenhum registro de DI existente mudou**, e nenhum dos outros
      treze harness foi tocado.
- [x] 3.4 (`apps/workers`) Registrar no código, em comentário, que o middleware
      do `ChatClientAgent` continua sem log por depender de `services` e não de
      `loggerFactory` — achado com gatilho, não corrigido aqui (Open Question 1).

## 4. Escopo 3 — `HttpStatusOf` lê o status do SDK do Gemini

- [x] 4.1 (`apps/workers`, teste) `[Theory]` em `ExecutionMetricsScopeTests` com
      três casos: `ClientError` com `new StatusCode`, `ServerError` idem, e
      exceção sem status tipado (continua nulo). **Vermelho contra `HEAD`** nos
      dois primeiros.
      **DOIS casos novos, não três:** a `[Theory]` já existia e **já tinha** o
      caso de exceção sem status tipado — repeti-lo seria enchimento. Vermelho
      registrado nos dois (`Com falha: 2, Aprovado: 3`), verde depois da 4.2
      (`12/12` na classe). Diferença contra a projeção comparada na 7.4.
- [x] 4.2 (`apps/workers`) Acrescentar os dois braços ao `switch` de
      `HttpStatusOf`, **antes** do braço de `HttpRequestException` (D4), com o
      registro de por que a ordem é correção e não estilo.
- [x] 4.3 (registro, convenção 9) Corrigir o registro da
      `metricas-execucao-coleta` com a causa: o `HttpStatus` nulo das seis
      chamadas de compactação do piloto não era ausência de status, era o
      padrão casando pelo tipo estático da base. Anotar no `design.md` arquivado
      dela e no `02`.
      **FEITO no `design.md` arquivado** (bloco de correção dentro da D12, com a
      causa, a medida das seis chamadas e o que a verificação original deixou de
      conferir: a hierarquia do SDK do Gemini). O `02` entra na tarefa 8.1.

## 5. Escopo 4 — nível de log de produção e checagens de startup

- [x] 5.1 (`apps/workers`, `apps/api`, `apps/inbox`) Criar os três
      `appsettings.Production.json` com
      `Microsoft.EntityFrameworkCore.Database.Command: Warning` e **sem** tocar
      no `Default` (D5).
- [x] 5.2 (`apps/workers`, teste) Três casos, **nas classes que já cobrem cada
      checagem** (ver a nota de desvio da tarefa 0): lote válido loga e segue e
      índice consistente loga e segue, em `EmbeddingBatchSizeValidationTests` e
      `EmbeddingIndexConsistencyTests`; e o caso de configuração inconsistente
      continuar falhando com o erro de hoje. **Vermelho contra `HEAD`** nos dois
      primeiros.
      **VERMELHO REGISTRADO:** `Com falha: 3, Aprovado: 13` nas duas classes.
      **VERDE depois da 5.3:** `16/16`.
- [x] 5.3 (`apps/workers`) Acrescentar a linha de sucesso às duas checagens
      (D6), com o valor conferido e o registro de por que elas ganham linha
      agora (a consulta do EF que as manifestava é silenciada pela 5.1).
      **DUAS linhas na checagem de índice, não uma:** "índice vazio, nada a
      conferir" e "índice conferido" são fatos diferentes, e uma linha só para os
      dois devolveria a ambiguidade que a linha veio remover (achado da 1.3).
- [x] 5.4 (`apps/workers`) Conferir, subindo o processo com o ambiente de
      produção, que a linha de início do detector de tasks não-terminais
      continua aparecendo e que o log de comando do EF não aparece. Regime
      colado na anotação.
      **MEDIDO** — 22/09/2026 22:13 `America/Sao_Paulo`, processo local com
      `DOTNET_ENVIRONMENT=Production` e `--no-launch-profile` (o perfil do
      `launchSettings.json` força `Development` e mascararia a conferência),
      contra o Postgres e o RabbitMQ do compose de desenvolvimento. O boot
      imprimiu, nesta ordem: fuso resolvido; **`Tamanho de lote de embedding
      conferido: 250.`**; **`Índice de conhecimento vazio: nada a conferir…`**;
      `Hosting environment: Production`; e **`Varredura de tasks não-terminais
      ativa: janela de 00:02:00 …, intervalo de 00:00:30.`** — o `Default` não
      subiu. **Nenhuma linha `Microsoft.EntityFrameworkCore.Database.Command`**,
      embora a consulta de consistência E a varredura do detector tenham ido ao
      banco na mesma execução (a varredura reportou `Submitted=4, Working=1`).
      **Este par roda localmente e fecha a change**; o mesmo
      par no piloto é pós-deploy e vive na 8.2, fora do fechamento.

## 6. Escopo 5 — linhas de log do cliente de LLM

- [x] 6.1 (`apps/workers`, teste) Guardas em `LlmCallDurationChatClientTests`,
      quatro casos: sucesso com task e finalidade; falha com tipo de exceção e
      status; finalidade `Compaction` distinguível da de turno; chamada fora de
      execução continua logando, sem task. **Vermelho contra `HEAD`** — hoje é a
      mesma linha para sucesso e falha.
      **VERMELHO REGISTRADO:** `Com falha: 3, Aprovado: 8` — o quarto caso (fora
      de execução) já passava, e continua como guarda de não-regressão.
      **VERDE depois da 6.2:** `11/11`.
- [x] 6.2 (`apps/workers`) Expor leitor público da finalidade corrente em
      `ExecutionMetricsScope` e escrever as duas linhas distintas em
      `LlmCallDurationChatClient` (D7), com o registro de que a decisão do
      `HttpClient` em `Warning` anda junto desta linha.
      **FEITO:** `ExecutionMetricsScope.CurrentPurposeOrDefault` (leitor público,
      usado também pelo gravador da linha filha, para que log e tabela leiam a
      MESMA fonte); sucesso em `Information` e falha em `Warning`, com tipo da
      exceção e status. No streaming a linha de falha sai sem tipo
      (`EnumeracaoIncompleta`): C# não permite `catch` com `yield`, e "não
      completou" é tudo o que aquele caminho sabe.
- [x] 6.3 (`apps/workers`) Conferir que o status que vai para a linha de falha é
      o mesmo que vai para a coluna, depois do escopo 3.
      **CONFERIDO:** a linha de falha chama `ExecutionMetricsScope.HttpStatusOf`,
      a mesma função que alimenta a coluna — inclusive os braços do Gemini da
      4.2. Guarda: `OnFailure_LogsDistinctLineWithExceptionTypeAndStatus`
      afirma o `429` na linha.

## 7. Verificação

- [x] 7.1 (`apps/workers`) Suíte completa: projeção **baseline da 1.1 + 16
      casos** — **casos, não métodos**, que é a unidade que a suíte devolve. São
      14 métodos: 13 `[Fact]` mais a `[Theory]` de três status da tarefa 4.1.
      Se o apply acrescentar outra `[Theory]`, a diferença é **explicada na
      7.4**, não reprojetada. Mesmo número de classes na `WorkerHostCollection`.
      Comparação **por nome** de teste contra a baseline: nenhum teste some.
      **MEDIDO** — 22/09/2026 22:17–22:25 `America/Sao_Paulo`: **340/340** em
      7m39s, contra baseline **324/324** em 9m37s. **+16 casos, exatamente a
      projeção**, e **14 classes continuam 14** na `WorkerHostCollection`.
- [x] 7.2 (`apps/api`, `apps/inbox`) Suítes completas **inalteradas** — os dois
      só ganharam arquivo de configuração. Qualquer variação é achado.
      **MEDIDO:** `apps/api` **346/346** (baseline 346/346, 2m18s contra 3m54s);
      `apps/inbox` **202/203**, falhando **o mesmo teste da baseline**, pelo
      nome — o flake de `DebounceSweepServiceTests` sob suíte completa. Nenhuma
      variação: nenhum `.cs` dos dois apps foi tocado.
- [x] 7.3 (`apps/workers`) Execução real contra o Gemini, com chave de **dev**
      por variável de ambiente ou `dotnet user-secrets` — **nunca** em
      `appsettings` versionado: conversa acima de dez turnos, e confirmar que a
      chamada de compactação retorna e o histórico é reduzido. **Regime colado**
      (máquina, data, fuso, modelo, versões de pacote, e o fato de ser chave de
      dev). Esta é a evidência que o guarda da costura **não** dá (D8).
- [x] 7.4 (`apps/workers`) Fechamento da convenção 18: comparar o medido com a
      projeção do `design.md` — arquivos, linhas com `git diff -w`, unidades
      públicas, casos de teste (a unidade é o **caso**, não o método), e razão
      comentário/lógica. **Só comparar, não reprojetar.**
- [x] 7.5 Conferir `git status` contra a lista fechada da tarefa 0.

## 8. Registro

- [x] 8.1 `CHANGELOG.md` e `02-HISTORICO_E_STATUS.md`: o defeito, a causa com a
      reprodução e o regime, e os três itens abertos que **não** entraram —
      anomalia dos turnos 1 e 2 (cinco candidatos eliminados), coluna de
      categoria de exceção (gatilho: reavaliar depois do escopo 3) e contagem de
      mensagens por requisição.
      **FEITO:** `CHANGELOG.md` (quatro itens em `Fixed`, dois em `Changed`) e
      `02-HISTORICO_E_STATUS.md` (registro da change com o defeito, a causa por
      construção, os cinco escopos, a execução real com regime colado e três
      achados de método; mais sete itens em "Abertos por
      `compactacao-historico`").
- [x] 8.2 Em "Itens em aberto" do `02`, registrar o item **conferência da fonte
      única do nível de log** — é a verificação que a suíte não faz, e ela **não
      é tarefa desta change**: só existe depois do deploy. Mesmo tratamento que a
      `indexacao-lote-de-fragmentos` deu à verificação de campo do tamanho do
      lote.
      O que fica escrito: (a) o passo de deploy que **não** é código — remover a
      configuração de log do ambiente do piloto, porque **variável de ambiente
      vence `appsettings`** na precedência do `ConfigurationBuilder`, e enquanto
      ela existir o valor versionado não é o efetivo; (b) o roteiro da
      conferência, no idioma da 5.4 — subir **sem** a variável, confirmar que o
      log de comando do EF **não aparece** e que a linha de início do
      `NonTerminalTaskDetectorService` **continua aparecendo**, com janela e
      intervalo; (c) que **ausência de variável não deixa rastro**, e por isso a
      conferência é a única prova de que a fonte única funcionou; (d) o regime a
      colar no resultado (data, hora, fuso, instância).
      **Gatilho:** o primeiro deploy com esta change aplicada.
      **Posição:** a janela desse deploy.
      **Não é marcável no fechamento desta change** — fechar significa "o valor
      está versionado e os guardas passam", nunca "o piloto está lendo o valor
      versionado". Esta tarefa fecha ao **escrever o item**, não ao verificar.
- [x] 8.3 Registrar no `02` o marco da série: a data e o fuso da primeira
      conversa medida já com a compactação funcionando — a série de métricas de
      entrada por turno se divide ali.
