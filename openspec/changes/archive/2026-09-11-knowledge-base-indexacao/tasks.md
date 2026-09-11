## 0. Baseline e dependências (convenção 19)

- [x] 0.1 Rodar a suíte inteira em worktree limpo **antes de qualquer edição** e registrar o resultado: `apps/api`, `apps/inbox`, `apps/workers`, `tests/` e `apps/frontend`. Com Podman, exportar `DOCKER_HOST` conforme `docs/development.md#testcontainers-com-podman` — sem isso a suíte de integração falha inteira e a falha **parece defeito de código**. A falha conhecida a esperar é o flake de ordem de execução de `AgentDeactivationTests` (item em aberto registrado); qualquer outra reprovação no baseline é anterior a esta change e precisa ser separada dela por escrito
- [x] 0.2 Acrescentar `Pgvector` 0.3.2 e `Pgvector.EntityFrameworkCore` 0.3.0 a `Directory.Packages.props`, e `PackageReference` em `Buteco.Api.csproj` e `Buteco.Workers.csproj`. **Registrar no comentário** que o pacote declara `net8.0` e `Npgsql.EntityFrameworkCore.PostgreSQL >= 9.0.1`, contra os `net10.0`/10.0.3 deste repositório, e que a compatibilidade foi verificada end-to-end (design V6) — é o risco R8, e a próxima pessoa a fazer upgrade de EF Core precisa saber que este pacote não acompanha o ciclo
- [x] 0.3 Trocar a imagem nos **11** sítios de `new PostgreSqlBuilder("postgres:18")` que precisam da extensão (design V1): 8 em `apps/api/tests`, `WorkerInfrastructureFixture`, `PostgresTaskStoreCompatibilityTests` e `RoundTripFixture._apiAndWorkersPostgres`. **Não tocar** nos 4 que ficam: os três de `apps/inbox/tests` e `RoundTripFixture._inboxPostgres` — trocar lá acoplaria apps deliberadamente isolados. **Não tocar** na linha 280 de `AgentMcpBindingEndpointsTests`, que é comentário sobre collation
- [x] 0.3.1 **Registrar `UseVector()` em todo sítio que constrói o `DbContext` fora de `AddInfrastructure`** — **~38 sítios**, não os 11 da 0.3, e a diferença é de natureza, não de contagem: a 0.3 mede a **imagem** (`PostgreSqlBuilder`), esta mede o **provider** (`UseNpgsql`). A imagem decide se a extensão existe no servidor; o provider decide se o EF sabe mapear o tipo (design, correção da V1). A forma é um ajudante por projeto de teste — `UseButecoAgentsNpgsql` —, **não** a repetição da chamada em cada sítio: repetir não resolveria o modo de falha, instanciaria ele 38 vezes, e o próximo teste escrito esqueceria de novo. **O nome cita o banco de propósito**, para que usá-lo em `apps/inbox` (banco próprio, sem a entidade) fique obviamente errado
- [x] 0.4 Trocar `image: postgres:18` para `pgvector/pgvector:pg18` em `docker-compose.yml` e `docker-compose.prod.yml`, com comentário registrando que **há um servidor só para dois bancos** (`buteco_agents` e `buteco_inbox`), então a troca alcança o servidor que hospeda o banco de `apps/inbox` — inócuo, porque a extensão é por banco, mas a frase precisa existir para não sugerir um isolamento que o compose não tem
- [x] 0.5 Acrescentar a seção `Embedding` ao `.env.example` e ao `appsettings.Development.json` de `apps/workers`, com valores de exemplo. **Não** inventar entrada nova de credencial: a chave e o endpoint vêm da seção `OpenAI` já existente (design D8). **Não** consertar de passagem o `ChatClient__*` obsoleto do `.env` local — é item em aberto com change própria

## 1. Entidade de fragmento e schema (`apps/api`)

- [x] 1.1 `apps/api`: criar `KnowledgeFragments/Entities/KnowledgeFragment.cs` — `Id`, `KnowledgeDocumentId`, `KnowledgeBaseId` (desnormalizado de propósito: a busca da etapa 4 filtra por base, e sem ele toda consulta precisaria de `JOIN`), `Ordinal`, `Text`, `Embedding` (`Pgvector.Vector`), `EmbeddingProvider`, `EmbeddingModel`, `EmbeddingDimensions`, `CreatedAt`. Setters privados e construtor privado sem parâmetros para o EF Core, no molde das entidades da etapa 1
- [x] 1.2 `apps/api`: XML doc na entidade registrando que **as três colunas de proveniência do embedding não são metadado decorativo** — são o lado do índice na checagem bidirecional do boot de `apps/workers` (design D6), e sem elas a checagem não tem contra o que comparar
- [x] 1.3 `apps/api`: mapear no `AppDbContext` — tabela `knowledge_fragments`, `HasPostgresExtension("vector")`, `Embedding` com `HasColumnType("vector(4096)")`, índice em `KnowledgeDocumentId` e índice em `KnowledgeBaseId`. FK para `KnowledgeDocument` com **`OnDelete(DeleteBehavior.Cascade)`** — e comentar por que aqui é `Cascade` enquanto `KnowledgeDocument → KnowledgeBase` é `Restrict` (design D5): fragmento é conteúdo derivado do documento; com `Restrict`, excluir documento indexado passaria a falhar, o que é regressão direta da D6 da etapa 1
- [x] 1.4 `apps/api`: acrescentar a `KnowledgeDocument` as quatro colunas — `ContentHash` (`string?`), `FragmentCount` (`int`), `IndexingAttempts` (`int`), `LastAttemptAt` (`DateTimeOffset?`) — com setters privados e mapeamento no `AppDbContext`. `ContentHash` nasce **nulo** para linhas existentes e nulo significa "nunca indexado sob esta regra"; não há backfill
- [x] 1.5 `apps/api`: alterar `KnowledgeDocument.Update` para calcular `ContentHash` (SHA-256 hex do texto extraído) e **só** voltar para `Pending` quando o hash mudar (design D9, spec "Atualização de documento"). Zerar `IndexingAttempts` quando o conteúdo mudar. **Preservar** `IndexedAt` sempre, como já faz. O incremento de `ContentRevision` continua condicionado a `ExtractedText` ter mudado — não mexer nessa condição
- [x] 1.6 `apps/api`: gerar a migração `AddKnowledgeFragmentIndex` e **conferir o SQL gerado** — deve criar a extensão, a tabela com `vector(4096)`, os dois índices, a FK em `CASCADE`, e as quatro colunas em `knowledge_documents`. Aditivo puro
- [x] 1.7 `apps/api`: registrar no XML doc da migração ou no `design.md` que `CREATE EXTENSION vector` **exige superusuário** (`trusted = f`, verificado — design V8) e que funciona hoje porque o `migrator` conecta como `${POSTGRES_USER}`. É restrição de deploy, e o Testcontainer não a exerce porque roda como superusuário (risco R9, aceito sem contraparte)

## 2. Publicação na fila (`apps/api`)

- [x] 2.1 `apps/api`: criar `Knowledge/Indexing/KnowledgeIndexingJobMessage.cs` — `(Guid KnowledgeDocumentId, int ContentRevision, int Attempt)`. `ContentRevision` viaja na mensagem para que o consumidor saiba **qual** revisão pediu o trabalho; `Attempt` viaja porque o contador de re-execução é do fluxo, não do documento
- [x] 2.2 `apps/api`: criar `IKnowledgeIndexingJobPublisher` e `RabbitMqKnowledgeIndexingJobPublisher`, no molde exato de `RabbitMqTaskJobPublisher` (canal preguiçoso com `SemaphoreSlim`, `BasicProperties { Persistent = true }`, `mandatory: true`). Declarar as **três** filas: `knowledge-indexing`, `knowledge-indexing-wait-60s` e `knowledge-indexing-wait-300s`, as duas últimas com `x-message-ttl` fixo e `x-dead-letter-exchange` de volta para a principal (design D3)
- [x] 2.3 `apps/api`: **TTL fixo por fila, nunca TTL por mensagem** — comentar no código que RabbitMQ só expira mensagem na cabeça da fila, então uma fila única com TTL por mensagem faria a de 300 s segurar a de 60 s atrás. É bloqueio de cabeça de fila, conhecido e silencioso, e é a alternativa recusada em D3
- [x] 2.4 `apps/api`: publicar em `CreateKnowledgeDocumentCommandHandler` — depois do `SaveChangesAsync`, nunca antes, para que não exista mensagem apontando para documento que não foi gravado
- [x] 2.5 `apps/api`: publicar em `UpdateKnowledgeDocumentCommandHandler` **apenas quando o conteúdo mudou** (o `ContentHash` da tarefa 1.5 é quem decide). Atualização que só troca título não enfileira e não gasta embedding
- [x] 2.6 `apps/api`: registrar o publisher no `Program.cs` como singleton, junto do publisher de tarefas já existente

## 3. Exposição nas respostas (`apps/api`)

- [x] 3.1 `apps/api`: acrescentar `FragmentCount`, `IndexingAttempts` e `LastAttemptAt` a `KnowledgeDocumentResponse` e a `KnowledgeDocumentSummaryResponse`, e aos dois `FromEntity`. **Os dois são records posicionais**, então campo novo quebra a compilação em cada site de construção — é o mecanismo que garante que nenhum caminho fique defasado, o mesmo que a etapa 3 usou com `AgentResponse`. Percorrer os sites que quebrarem e conferir cada um, em vez de corrigir mecanicamente
- [x] 3.2 `apps/api`: **não** acrescentar nada a `KnowledgeBaseResponse` nesta change. A contagem de documentos e o resumo de indexação por base são a **2b** — e a forma (campo no response contra rota própria) precisa ser decidida com a tela na mão, porque a 5a-1 registrou que a coluna de documentos exigiria uma requisição por base contra as 100+ bases do handoff. Registrado no `design.md`, Open Question 2
- [x] 3.3 `apps/api`: **nenhuma rota nova**. A rota de reindexação é a 2b. Conferir que nada foi acrescentado a `KnowledgeDocumentEndpoints` nem à allowlist de rotas anônimas

## 4. Chunker (`apps/workers`)

- [x] 4.1 `apps/workers`: criar `Knowledge/Chunking/KnowledgeChunker.cs` com `TARGET_MIN = 900` e `HARD_MAX = 1600` como **constantes nomeadas**, não `Options` vinculada a seção de configuração — idioma de `AgentDelegationToolOptions`. XML doc registrando o motivo (design D7): `0c` provou que o chunker não perde conteúdo, **não** que 900/1600 seja o ótimo, e afirmar em spec um número não medido é o padrão que a convenção 10 nomeia. Gatilho para remedir: corpus real de operador com volume
- [x] 4.2 `apps/workers`: normalização de entrada espelhando o extrator da etapa 1 — remover BOM, normalizar `CRLF`/`CR` para `LF`. Não "limpar" marcação: a etapa 1 decidiu preservá-la (D11) justamente porque a fragmentação depende dos cabeçalhos sobreviverem
- [x] 4.3 `apps/workers`: parser de seções com **captura de preâmbulo** — o texto entre a linha `#` e o primeiro cabeçalho de seção vira seção de caminho vazio, em vez de ser descartado. É o defeito (c) que `0c` isolou, e é o que fazia 4 parágrafos sumirem em silêncio
- [x] 4.4 `apps/workers`: fronteira em **múltiplos níveis** de cabeçalho (`##` a `######`), com pilha de caminho. O chunker de `0b` só reconhecia `##`, e por isso um documento com `#` + `###` produzia **zero** fragmentos
- [x] 4.5 `apps/workers`: **fallback por tamanho quando não há cabeçalho nenhum** — o documento inteiro vira uma seção de caminho vazio e é fragmentado por tamanho. É o defeito (a), e o caso não é hipotético: `.txt` é entrada de primeira classe declarada em `KnowledgeSourceTypes` ("texto puro sem marcação é markdown válido")
- [x] 4.6 `apps/workers`: teto aplicado ao **texto emitido**, com o prefixo de caminho de cabeçalhos dentro do orçamento. O chunker de `0b` concatenava o prefixo depois de medir, e por isso o teto não era teto
- [x] 4.7 `apps/workers`: quebra em cascata — parágrafo, linha, sentença, e em último caso corte de caractere. O nível de **linha** é o que trata tabela markdown e bloco de código, que são um parágrafo só e foram o que estourou o teto em 2.858 caracteres na medição
- [x] 4.8 `apps/workers`: **repetir a linha de cabeçalho e a separadora da tabela em cada pedaço** (design D7, spec própria). Comentar no código a medição que justifica: sem isso a continuação vira laje de linhas sem prosa e sem nomes de coluna, e `0c` mediu um fragmento assim sendo top-1 de **32 de 83 consultas** sem relação com ele. **É mitigação parcial** — cai para 12%, ainda muito acima do ~1% de um índice uniforme (risco R10), e a causa raiz vale para qualquer bloco denso e heterogêneo. Registrar isso no comentário, para que ninguém leia a correção como solução
- [x] 4.9 `apps/workers`: **overlap é zero e não há parâmetro para ligá-lo.** Comentar que é resultado de medição, não parcimônia: `0c` mediu perda de 20 a 23 pontos de R@1 nas duas direções (prefixo e sufixo) e nas duas magnitudes (150 e 300), depois de removidas duas armadilhas de implementação. O gatilho antigo ("revisitar quando a medição mostrar perda em fragmentos de fronteira") está respondido pelo avesso

## 5. Provedor de embedding (`apps/workers`)

- [x] 5.1 `apps/workers`: criar `Options/EmbeddingOptions.cs` — `SectionName = "Embedding"`, com `Provider`, `Model` e `Dimensions`. A credencial e o endpoint **não** entram aqui: vêm de `ChatClientOptions` (seção `OpenAI`), reusados (design D8)
- [x] 5.2 `apps/workers`: criar `IEmbeddingGeneratorResolver` e `EmbeddingGeneratorResolver` no molde de `IChatClientResolver`/`ChatClientResolver` — interface existe para isolar "qual client é construído para qual provedor" sem rede
- [x] 5.3 `apps/workers`: construir por `new OpenAIClient(...).GetEmbeddingClient(modelo).AsIEmbeddingGenerator()`, **sem passar `defaultModelDimensions`**. Comentar por quê (design D1): o gateway aceita `dimensions` e o ignora silenciosamente, então passar o parâmetro produziria metadata afirmando uma dimensão que o vetor não tem
- [x] 5.4 `apps/workers`: **não** usar `LlmProviders`/`ProviderCatalog`, e registrar o motivo no XML doc: `LlmProviders.All` declararia `anthropic` como provedor de embedding, e o assembly não tem nenhum tipo de embedding. O catálogo é de provedores de chat
- [x] 5.5 `apps/workers`: registrar `EmbeddingOptions` e o resolver no `Program.cs`, junto das outras `Options`

## 6. Fila, consumidor e unidade de trabalho (`apps/workers`)

- [x] 6.1 `apps/workers`: criar o espelho de `KnowledgeIndexingJobMessage` e o publisher (usado só para republicar nas filas de espera), no molde do espelho que `apps/workers` já tem de `RabbitMqTaskJobPublisher`
- [x] 6.2 `apps/workers`: criar `KnowledgeIndexingConsumer` como `BackgroundService`, **fila própria `knowledge-indexing`**, nunca `agent-tasks`. Comentar o motivo (design D2): `TaskJobConsumer` roda com `prefetchCount: 1`, e `AgentDelegationConcurrencyTests` existe para provar que isso serializa o consumo — indexação de minutos ali é a mesma classe de bloqueio, não "fila mais lenta"
- [x] 6.3 `apps/workers`: copiar a **forma** do `try/catch` de `TaskJobConsumer.cs:47-61` — o `try` abre **antes** da desserialização e envolve a execução inteira, com a leitura do banco e a chamada ao provedor **dentro** dele. Convenção 4, e a cláusula já mordeu duas vezes nesta base; a chamada ao provedor é justamente a que falha
- [x] 6.4 `apps/workers`: criar `KnowledgeIndexingService` — a unidade de trabalho. Ler o documento e a `ContentRevision` corrente; comparar com a revisão que veio na mensagem e **abandonar sem gravar** se divergirem; transicionar para `Indexing`; fragmentar; gerar embedding em lote (`GenerateAsync(IEnumerable<TInput>, ...)`, batch-nativa); gravar
- [x] 6.5 `apps/workers`: **conferir a dimensão devolvida** contra `EmbeddingOptions.Dimensions` antes de gravar, e falhar a indexação daquele documento se divergirem (spec "Vetor gravado é o vetor cheio do modelo"). O provedor pode aceitar um pedido de dimensão e ignorá-lo — foi medido que aceita
- [x] 6.6 `apps/workers`: gravação em **transação única** — apagar todos os fragmentos do documento, inserir os novos, atualizar o documento para `Indexed` com `IndexedAt`, `FragmentCount` e as três colunas de proveniência nos fragmentos. Tudo ou nada (garantia 2 de D9 da etapa 1)
- [x] 6.7 `apps/workers`: a atualização final do documento é **condicionada**: `WHERE "Id" = @id AND "ContentRevision" = @revisaoLida`, e o código **checa linhas afetadas**. Zero linhas significa "a revisão mudou **ou** o documento sumiu", e nos dois casos o resultado inteiro é descartado sem gravar nada e sem erro visível ao operador (design D5). **Não** assumir que o EF lança: escrever a checagem
- [x] 6.8 `apps/workers`: **recusar gravação de sucesso com zero fragmentos** para documento com conteúdo — termina em `Failed` com motivo legível, nunca em `Indexed` com contagem zerada. **A guarda não é contra documento vazio**, que é inalcançável pela API (design V11/D11): é defesa em profundidade contra **regressão no chunker**, que é o caminho alcançável para a contagem zerada com aparência de sucesso, o pior caso da convenção 13. Escrever isso no comentário, senão a guarda é lida como redundante e removida
- [x] 6.9 `apps/workers`: registrar consumidor e serviço no `Program.cs`, no molde de `AgentExecutionService` + `TaskJobConsumer` (singleton injetado no `BackgroundService`, escopo novo por execução via `IServiceScopeFactory`)

## 7. Falha, tentativas e motivo legível (`apps/workers`)

- [x] 7.1 `apps/workers`: criar o tradutor de falha — `IndexingFailureReason`, que converte a causa em texto **de operador**, descrevendo o que falhou e o que fazer. Nunca nome de tipo, nunca pilha, nunca mensagem de biblioteca repassada. A tela da 5a-2 mostra esse texto **completo, sem truncar**, e é a única cópia de falha que ela tem
- [x] 7.2 `apps/workers`: implementar o retry — **três execuções**, espaçadas por 1 minuto e 5 minutos, republicando na fila de espera correspondente ao número da tentativa (design D3). Uma execução é uma tentativa: `IndexingAttempts` conta execuções, não chamadas HTTP, e é isso que torna verdadeiro o texto "429 nas três tentativas, a última às 03:14"
- [x] 7.3 `apps/workers`: **não** implementar retry da chamada ao provedor dentro da execução. Uma camada só, um contador só, um significado só — comentar isso, porque acrescentar a segunda camada é a mudança "óbvia" que tornaria o contador ambíguo
- [x] 7.4 `apps/workers`: gravar `IndexingAttempts` e `LastAttemptAt` a cada execução, e `Failed` + `FailureReason` ao esgotar as três. **Preservar `IndexedAt` e os fragmentos anteriores** — garantia 3 de D9 da etapa 1
- [x] 7.5 `apps/workers`: `BasicNackAsync(requeue: false)` continua sendo o encerramento da mensagem em todos os caminhos — o reenvio é a republicação explícita na fila de espera, nunca `requeue: true`, que devolveria a mensagem à cabeça da fila sem espaçamento

## 8. Checagem de integridade do índice (`apps/workers`)

- [x] 8.1 `apps/workers`: criar `EmbeddingIndexConsistencyValidation` — `SELECT DISTINCT` sobre `EmbeddingProvider`, `EmbeddingModel` e `EmbeddingDimensions` da tabela de fragmentos, comparado com o declarado em `EmbeddingOptions`. Vazio sobe; uma linha igual sobe; **qualquer outra coisa lança**, inclusive duas combinações distintas, que é corrupção por troca anterior não detectada. Sem bypass, sem modo de tolerância
- [x] 8.2 `apps/workers`: chamar no boot, em `Program.cs`, ao lado de `host.ValidateTimeZoneConfiguration()`. **Registrar no XML doc que esta é a primeira checagem desta base a fazer I/O no boot** e por que o custo é aceitável (design D6/V3): o compose já declara `depends_on: migrator: service_completed_successfully`, e o `migrator` declara `postgres: service_healthy`, então em produção o banco está pronto antes de `apps/workers` subir. Em desenvolvimento a falha vira falha de boot, que é mais visível que falha por mensagem
- [x] 8.3 A mensagem de erro **nomeia o declarado e o encontrado**, nos dois sentidos, no idioma de `ValidateKnowledgeExtractorRegistrations` — que é o molde da convenção 8 nesta base

## 9. Espelho e schema em `apps/workers`

- [x] 9.1 `apps/workers`: criar `Knowledge/Entities/KnowledgeFragment.cs` como espelho de leitura, e acrescentar as quatro colunas novas ao espelho de `KnowledgeDocument`
- [x] 9.2 `apps/workers`: mapear no `AppDbContext` com **exatamente** a mesma configuração de `apps/api`, incluindo `HasPostgresExtension("vector")`, o tipo de coluna e o `Cascade` da FK
- [x] 9.3 `apps/workers`: gerar a migração equivalente e conferir que o SQL bate com o de `apps/api`, inclusive o comportamento de exclusão da FK
- [x] 9.4 `apps/workers`: conferir que a migração nova **não** é aplicada por caminho de runtime nem entra em bundle — `grep` por `MigrateAsync`/`GetPendingMigrations`/`EnsureCreated` em `apps/workers/src` deve continuar sem resultado (a tarefa 8.2 acrescenta I/O no boot, mas **não** migração), e `deploy/migrate/Dockerfile` deve continuar buildando bundle só de `apps/api` e `apps/inbox`
- [x] 9.5 **Nunca rodar `dotnet ef database update` a partir de `apps/workers`** — migração de banco sai só de `apps/api`, e a tabela de fragmentos nasce lá **apesar de `apps/api` não escrever nela** (design D10). É contraintuitivo o bastante para alguém "consertar"; o `42P07` já está reproduzido em `02-HISTORICO_E_STATUS.md`

## 10. Testes do chunker (`apps/workers`, unitários)

- [x] 10.1 `apps/workers`: **I1** — as quatro formas que `0c` mediu, cada uma afirmando ≥1 fragmento e cobertura total dos parágrafos: `.txt` corrido sem cabeçalho nenhum; `#` + parágrafos sem `##`; `#` + `###` pulando o `##`; e o controle com `##`. **As três primeiras reprovam contra o chunker de `0b` com zero fragmentos** — é guarda antes de existir (convenção 15)
- [x] 10.2 `apps/workers`: **I2** — documento com tabela markdown longa e documento com bloco de código longo, afirmando que nenhum fragmento excede o teto **medido no texto emitido, com o prefixo incluído**. Reprova contra o chunker de `0b` com 2.858 caracteres
- [x] 10.3 `apps/workers`: **I3** — cobertura de parágrafo, incluindo o **preâmbulo** entre o `#` e o primeiro `##`. Reprova contra o chunker de `0b`, que descarta 4 parágrafos de preâmbulo nos documentos de medição
- [x] 10.4 `apps/workers`: cabeçalho de tabela repetido — tabela com mais linhas do que cabem num fragmento, afirmando que **todo** pedaço que contenha linhas dela contém também o cabeçalho e a separadora
- [x] 10.5 `apps/workers`: entrada vazia ou só de espaço devolve conjunto vazio — propriedade **do chunker**, e o único caso em que zero é correto nesse nível. **Não** é o teste da guarda do consumidor, que é 12.7
- [x] 10.6 `apps/workers`: **conferir que 10.1 e 10.2 reprovam contra o chunker de `0b`** antes de manter — três documentos com zero fragmentos e um fragmento de 2.858 caracteres são os números que `0c` mediu. Guarda que não se viu reprovar não vale (convenção 15)

## 11. Testes de indexação fim a fim (Testcontainers, convenção 5)

- [x] 11.1 `apps/workers`: documento criado transita `Pending → Indexed`, `IndexedAt` preenchido, `FragmentCount` maior que zero e igual ao número de fragmentos gravados. Par "sem item": documento recém-criado, antes do consumidor, tem `IndexedAt` nulo e `IndexingAttempts` zero
- [x] 11.2 `apps/workers`: reindexação substitui o conjunto inteiro — nenhum fragmento do conteúdo anterior permanece
- [x] 11.3 `apps/workers`: **fragmentos antigos sobrevivem até o sucesso** — entre a atualização e a gravação nova, os fragmentos anteriores continuam gravados e consultáveis (garantia 2 de D9)
- [x] 11.4 `apps/workers`: **falha preserva os antigos** — falha na reindexação deixa `Failed` + `FailureReason`, `IndexedAt` intacto e os fragmentos anteriores gravados; falha na primeira indexação deixa `IndexedAt` **nulo** (garantia 3 de D9). Os dois casos, porque a regra de exibição da contagem depende exatamente dessa distinção
- [x] 11.5 `apps/workers`: falha na gravação não deixa conjunto parcial — nem parte do antigo, nem parte do novo (garantia 1 de D9, transação única)
- [x] 11.6 `apps/api`: `ContentHash` — atualização com o mesmo conteúdo e título diferente **não** volta a `Pending`, **não** enfileira e preserva `IndexedAt`; atualização com conteúdo diferente volta a `Pending` e enfileira; `ContentRevision` continua se comportando como a etapa 1 fixou nos três casos
- [x] 11.7 `apps/api`: estender `KnowledgeWireFormatTests` para os **quatro** valores de `IndexingStatus`, inspecionando o **texto bruto** do JSON (convenção 12 — round-trip pelo mesmo tipo é cego a isso). É a extensão que D10 da etapa 1 pediu nominalmente: até aqui só `"Pending"` conseguia aparecer numa resposta real
- [x] 11.8 `apps/api`: contagem de fragmentos — documento indexado devolve `FragmentCount` igual ao número gravado; documento nunca indexado devolve `IndexedAt` nulo, e o teste afirma que a **regra de exibição** é do consumidor (nunca zerar) citando a spec, sem inventar comportamento de API que não existe
- [x] 11.9 `apps/workers`: estender `KnowledgeSchemaMirrorTests` para a entidade nova e as quatro colunas — o modelo espelhado gera o mesmo schema

## 12. Testes de concorrência e de exclusão (Testcontainers, molde de `AgentDelegationConcurrencyTests`)

- [x] 12.1 `apps/workers`: **documento atualizado durante a indexação** — o conteúdo muda enquanto a indexação da revisão anterior está em andamento, e nenhum fragmento da revisão anterior é gravado. Duas instâncias, cada uma com seu `IServiceScopeFactory` montado do zero, nada compartilhado além de Postgres e RabbitMQ. **Não caminho feliz** (risco R3)
- [x] 12.2 `apps/workers`: **documento excluído durante a indexação** — nenhum fragmento é gravado para o documento excluído, e o descarte acontece por **zero linhas afetadas**, não por exceção (design D5, risco R4)
- [x] 12.3 `apps/api`: **excluir documento indexado tem sucesso** e não deixa fragmento órfão. Este teste reprovaria se a FK fosse `Restrict` — é o guarda que impede a regressão da D6 da etapa 1, e é por isso que ele vive em `apps/api`, onde a exclusão mora
- [x] 12.4 `apps/workers`: retry — falha transitória na primeira execução com sucesso na segunda deixa `Indexed`, `FailureReason` nulo e `IndexingAttempts` refletindo as tentativas; esgotar as três deixa `Failed` com motivo legível, `IndexingAttempts` em três e `LastAttemptAt` preenchido
- [x] 12.5 `apps/workers`: conteúdo novo **zera** `IndexingAttempts` — a contagem é de tentativas sobre a revisão corrente, não sobre a vida do documento
- [x] 12.7 `apps/workers`: **guarda de zero fragmentos** — com um chunker substituído que devolve conjunto vazio para documento com conteúdo, a indexação termina em `Failed` com motivo legível e **não** grava nada. É o teste da tarefa 6.8, separado de 10.5 porque aquele afirma propriedade do chunker e este afirma o comportamento do consumidor diante de um chunker defeituoso
- [x] 12.6 `apps/workers`: `FailureReason` é legível — o teste afirma que o texto **não contém** nome de tipo de exceção nem pilha de chamadas (asserção negativa), além de conter a descrição do problema

## 13. Testes da checagem de integridade (convenção 15, com a quinta forma)

- [x] 13.1 `apps/workers`: índice vazio sobe; índice coerente com o declarado sobe
- [x] 13.2 `apps/workers`: modelo divergente reprova o boot; dimensão divergente reprova o boot; dois modelos distintos no índice reprovam o boot **ainda que um deles seja o declarado**
- [x] 13.3 `apps/workers`: **a quinta forma da convenção 15, e é ela que decide se o guarda vale.** `SELECT DISTINCT` sobre tabela vazia passa por **vacuidade** — um guarda escrito só contra estado limpo fica verde com e sem a implementação. Cada cenário de 13.2 tem de rodar com o índice **povoado**, e a divergência tem de ser reintroduzida de propósito e vista reprovar, nos dois sentidos, antes de manter. Se algum deles não conseguir reprovar com o defeito presente, **o guarda não vale** e a resposta é registrar isso, não mantê-lo
- [x] 13.4 `apps/workers`: teste do resolver de embedding — provedor conhecido constrói o client certo, provedor desconhecido e configuração ausente lançam, no molde de `ChatClientResolverTests`. Sem rede
- [x] 13.5 `apps/workers`: dimensão devolvida diferente da declarada falha a indexação e **não grava fragmento** com dimensão divergente

## 14. Fechamento

- [x] 14.1 Rodar a suíte inteira **guardando a saída completa em arquivo, fora do diretório de sessão** (não filtrar, não resumir — `| grep | head` fecha o cano e mata o produtor), e comparar com o baseline de 0.1 — nenhuma reprovação nova, e o flake conhecido de `AgentDeactivationTests` separado por escrito de qualquer coisa que esta change tenha causado
- [x] 14.2 Subir a stack com `docker compose up` e conferir que `apps/workers` sobe com a checagem de integridade nova, com o índice vazio (o caso do primeiro deploy)
- [x] 14.3 Medir o tamanho entregue por componente, criados e modificados separados, e comparar com a projeção abaixo — **oitava medição da série** (convenção 18). Registrar o resultado em `02-HISTORICO_E_STATUS.md` com a decomposição real, não só o total
- [x] 14.4 Registrar em `02-HISTORICO_E_STATUS.md`: o handoff da **2b** (rota de reindexação e forma do resumo por base, Open Question 2 do `design.md`); a **correção de D7 da etapa 1** pela convenção 9 (o `ContentRevision` cobre o descarte, mas quem impede fragmento órfão é a FK em `Cascade`); e o gatilho do índice ANN (p95 acima de 200 ms) **junto com o que custa exercê-lo** — reescrita de tabela sob `ACCESS EXCLUSIVE`, ~2,4 GB lidos e ~3,5 GB escritos para 150 mil fragmentos, com a saída medida de coluna comum mais `UPDATE` em lotes (design V12/D1)
- [x] 14.5 Sincronizar as specs e arquivar a change

## Projeção por componente (convenção 18)

Só **código**; artefatos OpenSpec fora da conta. Reprojetado **depois** das
verificações, não copiado da exploração — que estimou 40-46 arquivos e
2.100-2.600 linhas para a 2a, antes de três coisas que as verificações
acrescentaram: o trio de publicação em `apps/api` (a exploração o dobrou dentro
de "fila"), o mecanismo de retry com duas filas de espera (que não existia como
decisão), e o número de cenários que a spec fechou em ~62.

| componente | arquivos | linhas |
|---|---|---|
| **Criados — `apps/api`** | 4 | ~210 |
| entidade de fragmento, mensagem, interface e publisher | | |
| **Criados — `apps/workers`** | 13 | ~915 |
| espelho de fragmento, chunker, resolver de embedding + options, mensagem + publisher, consumidor, serviço de indexação, tradutor de falha, checagem de integridade | | |
| **Testes** (10 arquivos novos, ~62 cenários) | 10 | ~1.800 |
| **Modificados** | 24 | ~380 |
| `KnowledgeDocument` ×2, `AppDbContext` ×2, dois responses, dois handlers, `Program.cs` ×2, dois `.csproj`, `Directory.Packages.props`, 11 fixtures, 2 composes, `.env.example`, dois testes existentes | | |
| **Total** | **51** | **~3.305** |

Faixa: **46-56 arquivos / 2.900-3.700 linhas**, fora ~450 linhas de migração
gerada nos dois apps.

**O prêmio de teste está explícito, e é o que a etapa 1 errou por omissão:** os
cenários de concorrência e de transação custam ~45 linhas cada, contra os ~19 da
média medida — `AgentDelegationConcurrencyTests` gastou ~340 linhas em 2
cenários. Dos ~62 cenários, ~15 são desse perfil.
