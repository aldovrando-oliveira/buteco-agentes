## Why

`ChatClientResolver.Resolve` constrói um `IChatClient` novo a cada mensagem
processada em `apps/workers`, e esse client nunca é descartado. Nos SDKs de
Gemini e de Anthropic cada client instancia um `HttpClient` próprio — logo, um
pool de conexões próprio —, então **cada mensagem vaza um pool**. O processo
degrada com o tempo de execução e para de responder depois de horas.

Medido em duas instâncias: **~44 descritores de arquivo por mensagem
processada** (322→366 e 323→367), **e eles não voltam**. O stack trace da falha
é `HttpConnectionPool.SendWithVersionDetectionAndRetryAsync` — espera por
conexão do pool, não por resposta do servidor — e estoura em 100 s, que é o
timeout default do `HttpClient` no caminho do Gemini (verificado abaixo).

**Por que agora, e por que antes da etapa 4 da linha de conhecimento.** A etapa
4 será validada manualmente no mesmo caminho de execução que vaza. Depurar a
tool de conhecimento contra um worker que piora a cada mensagem transforma todo
sintoma estranho em dúvida entre "defeito da etapa 4" e "o vazamento chegou".

**Correção de enquadramento, feita antes de virar registro:** o relatório que
originou esta change afirma que o defeito está "afetando produção hoje".
**Não há produção** — está tudo em desenvolvimento, e isso está registrado no
`02-HISTORICO_E_STATUS.md` desde a decisão do censo de colisão. A urgência é
real; o motivo é o ambiente de desenvolvimento onde a etapa 4 será validada.
A conferência de configuração feita para esta proposta reforça o ponto por um
caminho independente: `docker-compose.prod.yml` **não passa**
`Anthropic__ApiKey` nem `Gemini__ApiKey` para nenhum processo (só `OpenAI__*`,
linhas 88-89 e 140-141), então os dois provedores que vazam sequer são
configuráveis no compose de servidor. O vazamento só é alcançável fora do
compose, que é exatamente onde o desenvolvimento roda.

**Esta change não contraria uma decisão registrada — ela dispara o gatilho que
essa decisão escreveu.** A Decision 7 do `design.md` arquivado em
`openspec/changes/archive/2026-08-01-backend-multi-provedor-llm/` diz, textual:
*"nenhuma instância é reaproveitada entre execuções (Non-Goal explícito;
simples de trocar por cache depois, **se perfilamento mostrar necessidade**)"*.
A medição acima é esse perfilamento.

## What Changes

- **`apps/workers`** — `ChatClientResolver` passa a **cachear o `IChatClient`
  por `(provider, model)`** e a devolver sempre a mesma instância para a mesma
  chave, em vez de construir uma por mensagem. O resolver **já é singleton**
  (`Program.cs:34`) e injeta só `IOptions<T>`, então o cache mora nele sem
  nenhuma mudança de tempo de vida e sem o defeito de DI "singleton que depende
  de scoped".
- **`apps/workers`** — **log de duração de cada chamada ao LLM**, via um
  `DelegatingChatClient` fino construído **junto com o client cacheado**, o que
  o posiciona como camada mais interna da cadeia e faz cada linha medir uma
  requisição HTTP ao provedor, não o turno inteiro do agente. É o instrumento
  que teria tornado este diagnóstico barato; sem ele o próximo custa igual.
- **`apps/workers`** — correção de **quatro afirmações hoje falsas no código**
  sobre construção por chamada: a docstring de `IChatClientResolver`, a
  docstring de `ChatClientResolver`, o comentário de `Program.cs:73-75` e — em
  `apps/api` — a docstring de `IMcpConnectionTester`, que cita o resolver de
  `apps/workers` como precedente de "construído por chamada, sem cache entre
  chamadas". Essa última é **edição de comentário apenas**: zero linhas de
  comportamento, zero `ProjectReference`, isolamento entre apps intacto.
- **`docs/`, `README.md`, `CHANGELOG.md`** — conferência artefato a artefato, com
  tarefa própria para cada, incluindo o registro explícito de que **rotação de
  chave de provedor exige reinício do processo**, com gatilho.
- Sem mudança de contrato entre apps, sem migração, sem mudança de API HTTP.

**Não é BREAKING**: nenhum contrato externo muda. A mudança observável é
interna ao processo de `apps/workers`.

## Capabilities

### New Capabilities

Nenhuma. Reutilizar instância de client é mudança de comportamento de uma
capability existente, não capability nova — abrir spec própria aqui seria
abstração prematura (convenção 2).

### Modified Capabilities

- `a2a-task-lifecycle`: o requisito **"Workers processam a task até um estado
  terminal"** hoje tem um cenário que afirma que o worker **constrói** um
  `IChatClient` "para essa execução" (`spec.md:96-101`). Essa frase passa a ser
  falsa: o worker resolve **uma** instância por `(provider, model)` e a
  **reutiliza** entre execuções. O propósito original do cenário — não depender
  de um client único fixo para todos os agentes — continua valendo e fica
  explícito na redação nova. Entram também o cenário de reuso entre mensagens e
  o cenário do log de duração.

## Impact

**Código (`apps/workers`, mais um comentário em `apps/api` — ver a última linha):**

- `src/Buteco.Workers/Agents/ChatClientResolver.cs` — cache e composição do
  wrapper de duração.
- `src/Buteco.Workers/Agents/IChatClientResolver.cs` — docstring falsa.
- `src/Buteco.Workers/Agents/AgentExecutionService.cs` — comentário no sítio de
  resolução (`:171`), declarando que a instância é compartilhada e não deve ser
  descartada. **Nenhuma mudança de comportamento neste arquivo.**
- `src/Buteco.Workers/Program.cs` — comentário falso em `:73-75`.
- Novo: `src/Buteco.Workers/Agents/LlmCallDurationChatClient.cs`.
- Testes: `tests/.../Agents/ChatClientResolverTests.cs`,
  `tests/.../TaskJobConsumerTests.cs`, e o arquivo de teste do wrapper novo.
- `apps/api/src/Buteco.Api/McpServers/Connectivity/IMcpConnectionTester.cs:5-11` —
  **só comentário**, removendo a citação cruzada que esta change torna falsa.
  Nenhuma mudança de comportamento, nenhuma referência de projeto nova.

**A propriedade antiga nunca teve guarda.** Conferido antes de reescrever as
docstrings, para não deletar por engano um guarda que estivesse afirmando o
comportamento removido: não existe, em nenhum dos dois lugares onde poderia estar.
Os cinco testes de `ChatClientResolverTests` afirmam tipo construído e lançamento,
nunca identidade nem contagem de construções. E a suíte vai além da omissão — todo
`Mock<IChatClientResolver>` da base devolve **uma instância fixa** em toda chamada,
nos oito sítios de `apps/workers/tests/` e no round-trip de `tests/`. Os testes
vinham exercitando a semântica cacheada enquanto a produção construía por
mensagem, e ninguém leu isso como divergência. É a explicação de por que um pool
de conexões por mensagem atravessou desde `backend-multi-provedor-llm` sem ser
notado — uma decisão que existia em docstring e em nenhum teste.

**Dependências:** nenhuma nova. `Microsoft.Extensions.AI` (10.6.0, já resolvido
transitivamente) fornece `DelegatingChatClient`; `LoggingChatClient` do mesmo
pacote **não serve** — foi decompilado e não mede duração.

**Risco de ciclo de vida introduzido, nomeado aqui e coberto no `design.md`:**
com o client compartilhado, qualquer descarte passa a afetar todas as mensagens
seguintes daquele `(provider, model)`. Hoje nada o descarta (verificado:
`ChatClientAgent` não é `IDisposable`), mas `DelegatingChatClient.Dispose()`
descarta o `InnerClient` em cascata, então um `using` acrescentado no futuro
sobre o agente ou sobre a cadeia quebraria o processo a partir da segunda
mensagem.

**Fora de escopo, cada um com o motivo:**

- **Timeout e retry da chamada ao LLM.** Exigem decisões sem evidência hoje:
  qual timeout, o que o agente faz ao estourar, se retry faz sentido quando o
  LLM já consumiu tokens. Misturar faria a correção do vazamento esperar por
  uma decisão que não tem base. Change própria.
- **A contradição sobre quantas instâncias de `apps/workers` são suportadas**
  (`docker-compose.prod.yml` proíbe `replicas > 1`; `AgentDelegationConcurrencyTests`
  usa **duas** para provar que a delegação funciona). É achado maior que este
  vazamento e de outra natureza — vai para `02-HISTORICO_E_STATUS.md` como
  exploração própria, não de carona numa correção de vazamento.
- **`Anthropic__ApiKey`/`Gemini__ApiKey` ausentes no `docker-compose.prod.yml`**,
  contra `docs/configuration.md:213-214`, que afirma que essas variáveis chegam
  aos processos. Achado incidental desta conferência; mesmo destino que o item
  acima.
- **`EmbeddingGeneratorResolver`**, que tem a **mesma forma** (constrói por
  chamada, nunca descarta). **Não vaza**, e a razão foi verificada: só suporta
  `openai`, e o SDK da OpenAI usa `HttpClientPipelineTransport.Shared` sobre um
  `HttpClient` **estático**. Fica registrado para que ninguém "corrija" por
  simetria — e é a mesma propriedade que explica por que a indexação rodou o dia
  inteiro sem sintoma enquanto o chat degradava.
- **`format:check` reprovando em quatro arquivos** (anterior a esta change,
  conferido em worktree limpo). Meia correção de dívida de formatação some da
  vista.
