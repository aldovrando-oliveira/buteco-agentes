# Tarefas — `criacao-base-sincronizada` (#104)

**Branch:** `feat/104-criacao-base-sincronizada`, criada de `9904999` (`main`
atualizada, sem commits à frente) e atualizada com a `main` em `a7960d9` (merge da
#49, PR #128) por fast-forward, sem conflito de código: só o `02` conflitou, por
acréscimo das duas seções, mantidas inteiras.

**Apps afetados:** `apps/api` (código e testes) e `tests/` (projeto novo de ida e
volta). `apps/connectors` não muda: só é referenciado pelo projeto de `tests/`.
Nenhuma tarefa toca `apps/workers`, `apps/inbox` ou `apps/frontend`. Nada de
`docker-compose.prod.yml`, `.env.prod.example`, nginx do stack ou `deployment.md`
(#119).

---

## 1. Antes do código

- [x] 1.1 [`apps/api`, `tests/`] Medir a baseline, pelo critério do `02` e com
  `git grep`, nunca de memória: número de testes de `apps/api` e
  `tests/InboxOrchestratorRoundTrip.Tests` em `9904999`, e as classes e fontes de
  contêiner do `apps/api` (D9). Registrar inline aqui.
- [x] 1.2 [`apps/connectors`] Ler a resposta real da rota de descrição: subir o
  `ConnectorsFactory` num teste descartável (ou ler
  `ConnectorEndpointsTests`) e conferir, sobre o texto, as chaves `id`, `name`,
  `webUrl` do `200` e `code`, `detail`, `status` do erro (convenção 6: o contrato
  vem do código, não do `design.md` da #103). Divergência corrige a D3 antes de
  seguir.
- [x] 1.3 [`apps/api`] Conferir que `ConfigureAppConfiguration` da
  `WebApplicationFactory` alcança o valor lido por `IOptions<ConnectorsOptions>` e
  pela checagem de boot sobre o host construído (o `apps/connectors` precisou de
  `UseSetting` porque lê antes do `Build()`). Se não alcançar, a D1 é corrigida
  com a causa.

  **Conferido no código:** o `Program.cs` do `apps/api` registra options por
  `Configure<T>(GetSection)`, que é avaliado na resolução, e as checagens de boot
  rodam sobre o host construído. `TimeZoneStartupValidationTests.RealComposition_*`
  já sobrescreve `TZ` por `ConfigureAppConfiguration` e a checagem pós-`Build()` vê o
  valor. **A D1 não muda.** Provado de novo por
  `ConnectorsConfigurationStartupValidationTests.RealComposition_WithInvalidBaseUrl_FailsToStart`
  e pelos testes de cadastro em host derivado.

  **Lido em `ConnectorResponses.cs` e `ConnectorEndpointsTests.cs` (03/10/2026):**
  o `200` é `{"id","name","webUrl"}` com nomes fixados por `JsonPropertyName`
  (`ConnectorEndpointsTests.cs:79` afirma o texto exato); o erro é `ProblemDetails`
  com a extensão `code` e o `status` por natureza da D9 da #103
  (`ConnectorEndpoints.StatusFor`); o `400` de `id` ausente não tem `code`. **Um
  detalhe que a D3 não dizia:** sem detalhe, a chave `detail` é **omitida**, e não
  enviada como `null` (`ConnectorEndpointsTests.cs:134-136`). O cliente trata ausente
  como nulo. **A D3 não muda.**

  **Medido em 03/10/2026, sobre `9904999` (só `openspec/` fora da árvore):**
  `apps/api` **607/607** em 1 min 56 s (`load average` 5,7 no início e 12 durante a
  rodada, com outra sessão rodando um teste filtrado do `apps/workers` num worktree
  à parte; `podman machine` `applehv` 6 CPUs). `tests/InboxOrchestratorRoundTrip.Tests`
  **4/4** em 4 s e `apps/connectors` **137 aprovados + 1 ignorado** (o teste manual do
  Google) **/ 138** em 10 s, os dois com `load average` 8. Nomes guardados em `.trx`
  para comparar no fechamento. Os dois últimos já compilaram o `apps/api` com os
  esqueletos vazios desta change, que nenhum código chamava.

  **Régua de contêineres do `apps/api`**, critério do `02` (classes com fixture de
  contêiner mais classes que constroem o próprio, excluída `Support/`, em todas as
  subpastas), com `git grep` contando também as fixtures aninhadas que derivam de
  uma fixture de contêiner (`AgentInsightsFixture`, `InsightsFixture`):
  **37 `IClassFixture` + 3 pela collection + 4 que constroem o próprio = 44 classes;
  42 fontes**. Igual ao fechamento da #102. A primeira versão do comando deu 42/40
  porque não contava as duas fixtures aninhadas; corrigida antes de registrar.

## 2. Configuração e token de serviço (`apps/api`)

- [x] 2.1 [`apps/api`] `ConnectorsOptions` (`Connectors:BaseUrl`),
  `appsettings.json` com `""` e `appsettings.Development.json` com
  `http://localhost:5037` (D1). Porta conferida em
  `apps/connectors/src/Buteco.Connectors/Properties/launchSettings.json:8`
  (`"applicationUrl": "http://0.0.0.0:5037"`), em 03/10/2026.
- [x] 2.2 [`apps/api`] `ConnectorsConfigurationValidation`: checagem sobre o host
  construído; ausente ou vazio sobe com aviso no log; presente e não `http(s)`
  absoluto falha nomeando a chave sem ecoar o valor. Chamada no `Program.cs` ao
  lado de `ValidateTimeZoneConfiguration`.
- [x] 2.3 [`apps/api`] `ServiceTokenDelegatingHandler` com `ApiSubject =
  "service:api"`, TTL fixo de 5 min, comentário de espelhamento apontando o par do
  `apps/inbox` e a tabela do `apps/connectors` (D4). O comentário do
  `ServiceTokenDelegatingHandler` do `apps/inbox` **não** é editado: a mudança
  fica no `apps/api` e na documentação.
- [x] 2.4 [`apps/api`] `HttpClient` nomeado do `apps/connectors` no
  `Program.cs`: `BaseAddress` só quando configurado, `Timeout` fixo de 35 s com o
  comentário que nomeia os 30 s do `apps/connectors` e os 60 s do nginx (D2), e o
  handler da 2.3.
- [x] 2.5 [`apps/api`, teste sem contêiner] `ServiceTokenDelegatingHandlerTests`:
  `sub` igual a `service:api` lido do token validado, expiração 5 min depois da
  emissão (o `TokenService` usa `DateTimeOffset.UtcNow`, então a asserção é por
  janela entre os instantes antes e depois do envio), e dois envios com dois tokens emitidos.
- [x] 2.6 [`apps/api`, teste sem contêiner]
  `ConnectorsConfigurationStartupValidationTests`: ausente e vazio sobem;
  `connectors:8080` e `ftp://connectors` falham nomeando a chave e sem conter o
  valor.

  **Visto reprovando** contra a checagem vazia (`Assert.Throws() Failure: No
  exception was thrown`, e o aviso ausente), depois verde. O valor relativo do caso
  virou `/caminho-relativo`: o primeiro, `/connectors`, é substring de
  "apps/connectors" na própria mensagem, e o teste reprovava por colisão de texto.

  **Visto reprovando** contra o handler vazio (sem `Authorization`), depois verde.

## 3. Cliente do `apps/connectors` (`apps/api`)

- [x] 3.1 [`apps/api`] `IConnectorsFolderClient` e `ConnectorsFolderClient`:
  monta a URL com `Uri.EscapeDataString`, lê o `200` e confere id, nome e URL
  (D7), e mapeia toda falha pela tabela da D2 e da D3 para um
  `ConnectorsFolderResult` com código, detalhe e status. Cancelamento do chamador
  propaga; só o timeout do `HttpClient` vira `connectors-unavailable`. Log de
  aviso com status e código, sem token.
- [x] 3.2 [`apps/api`] `ConnectorsFailureCodes` com os três códigos próprios, e
  reuso da forma de `SyncCode` para conferir o código recebido.
- [x] 3.3 [`apps/api`, teste sem contêiner] `ConnectorsFolderClientTests`, um
  caso por linha das tabelas da D2 e da D3: os sete códigos da #106 com o status
  esperado e o detalhe repassado, `folder-too-deep` passando, `"Sem acesso"`
  barrado, `400`, `401`, `403`, `500` sem código, `404` sem código, `200` sem
  `name`, `200` com `webUrl` relativa, `200` com id diferente, conexão recusada,
  timeout (com `Timeout` curto no `HttpClient` do teste), e cancelamento do
  chamador propagando. Mais um caso com `folderId` contendo `/` e `?`, afirmando a
  URL escapada.

  **Visto reprovando** contra um cliente que sempre devolvia sucesso: 47 casos
  reprovando por asserção (34 `Assert.False`, 3 `Assert.Equal`...), nenhum por
  exceção do esqueleto. Depois verde: **49/49** nas três classes sem contêiner dos
  grupos 2 e 3.

## 4. Cadastro de base `Synced` (`apps/api`)

- [x] 4.1 [`apps/api`] `CreateKnowledgeBaseRequest` com `Provider` e `FolderId`;
  `KnowledgeBaseEndpoints` valida a forma da D6, inclusive a presença em `Manual`,
  e retira a recusa de `Synced` da #102.
- [x] 4.2 [`apps/api`] `CreateKnowledgeBaseCommand` com provedor e pasta
  opcionais, e `CreateKnowledgeBaseResult` com os desfechos: criada, pasta em uso
  (id e nome da base), falha de validação (status, código, detalhe).
- [x] 4.3 [`apps/api`] Handler na ordem da D6: consulta de pasta em uso, falta de
  configuração, chamada, `KnowledgeBase.CreateSynced` com nome e URL da resposta,
  gravação. Na gravação, `catch` de `UniqueViolation` só pelo `ConstraintName`
  `IX_knowledge_bases_SyncProvider_SyncFolderId`, detach, releitura da vencedora,
  evento de log `1024` (`ConcurrentSyncedKnowledgeBaseCreateRejected`) (D5).
- [x] 4.4 [`apps/api`] Endpoint: `409` com `code: "folder-in-use"`,
  `knowledgeBaseId`, `knowledgeBaseName` e o `detail` da D5; falhas de validação
  com `code` e `detail` na extensão e o status do resultado. Nenhum texto fala em
  excluir, remover ou apagar.
- [x] 4.5 [`apps/api`, teste] `Support/FakeConnectorsHttpMessageHandler`: resposta
  configurável por teste, captura do `Authorization` e de cada URL chamada,
  contador de requisições, barreira opcional que segura N chamadas até todas
  chegarem.
- [x] 4.6 [`apps/api`, teste] `ApiFactoryFixture` força `Connectors:BaseUrl`
  vazio; `KnowledgeBaseCatalogTests.SyncedCreation.cs` (`partial`) com o host
  derivado da D9.
- [x] 4.7 [`apps/api`, teste] Cenários de `knowledge-base-catalog`: cadastro com
  snapshots do `apps/connectors` e corpo com `folderName`/`folderUrl` falsos
  (afirmado pelo banco e pela resposta); `syncState` de "nunca sincronizou";
  `AbC` gravado como veio; `Manual` com `provider` ou `folderId` igual a `""` → `400`, para
  cada campo; `Manual` com `provider: null` e com `folderId: null` → `201`, para cada
  campo; `Synced` sem provedor, sem pasta, com `"../x"`, `"Google Drive"` e
  pasta só de espaços → `400` com o contador do handler falso em zero.
- [x] 4.8 [`apps/api`, teste] Pasta em uso: base ativa e base inativa → `409` com
  id e nome, `detail` com a frase da base inativa, corpo inteiro sem `exclu`,
  `remov` e `apag` em qualquer caixa, nenhuma base nova, contador do handler falso
  em zero; o mesmo `409` sem `Connectors:BaseUrl` e com o handler falso lançando
  `HttpRequestException`; `AbC` e `abc` coexistem.
- [x] 4.9 [`apps/api`, teste] Corrida: barreira de 2 no handler falso, dois `POST`
  simultâneos da mesma pasta; exatamente um `201` e um `409` com o nome da
  vencedora, nenhum `500`, uma base com a pasta no banco, evento `1024` contado
  uma vez. Repetir em laço (20 iterações, pastas distintas) e registrar aqui
  quantas passaram pelo `catch`.
- [x] 4.10 [`apps/api`, teste] Repasse pela rota: cada um dos sete códigos da
  #106 chega com o status e o `detail` da tabela, e nenhuma base é criada (contagem
  no banco antes e depois); `connectors-not-configured` com `GET /knowledge-bases`
  e cadastro `Manual` funcionando no mesmo host; conexão recusada e lentidão (com
  `Timeout` encurtado no host derivado) → `503` `connectors-unavailable`, nunca
  `500`, nenhuma base; `401` → `502` `connectors-error`.
- [x] 4.11 [`apps/api`, teste] Token: o `Authorization` capturado pelo handler
  falso numa chamada pela rota valida com a chave da fixture e tem `sub` igual a
  `service:api`; o cliente da composição real tem `Timeout` de 35 s.
- [x] 4.12 [`apps/api`, teste] `ServiceScopeAuthorizationTests`: token
  `service:api` recebe `403` em `GET /knowledge-bases` e `POST /knowledge-bases`.
- [x] 4.13 [`apps/api`, teste] Retirar de `KnowledgeBaseCatalogTests` o caso
  "Base sincronizada não é criada por esta rota", substituído pelo requisito novo.

  O caso virou `CreateBase_WithUnknownContentMode_IsRefusedAndCreatesNothing`, com
  `Sincronizada` e `synced`; `Synced` saiu, com um comentário apontando o arquivo novo.

  **Medido:** 20 iterações, **20 de 20 pelo `catch` de `UniqueViolation`**, contadas
  pelo evento `1024`. O teste afirma `+1` no contador a cada iteração e 20 no total,
  então a medição faz parte da asserção e não de uma leitura à parte. A barreira de 2
  no handler falso garante que as duas requisições passaram pela consulta antes de
  qualquer gravação.

  **Visto reprovando** contra a rota da #102 (que recusava `Synced` com `400` em
  `contentMode`): 38 casos do grupo 4 reprovando por status ou asserção. Passavam
  antes, e com razão, os dois casos de `null` em base manual (a rota já ignorava o
  campo), o limite de 35 s (composição do grupo 2) e o `403` de `service:api` (recusa
  por padrão da #102). Verde depois: **109/109** em `KnowledgeBaseCatalogTests` e
  `ServiceScopeAuthorizationTests`.

## 5. Guardas que precisam falhar contra o defeito (convenção 15)

Cada guarda é aplicado no código, a suíte do grupo é rodada, o resultado é
registrado aqui, e o guarda é revertido.

**Como foi feito (03/10/2026):** cada guarda aplicado por script sobre uma cópia dos
arquivos, os testes filtrados rodados, e os arquivos restaurados da cópia. Conferido
por `grep` no fim que nenhum dos oito ficou em `apps/api/src`.

- [x] 5.1 [`apps/api`] g1: handler grava `folderName` e `folderUrl` do corpo →
  4.7 reprova.
- [x] 5.2 [`apps/api`] g2: sem o `catch` de `UniqueViolation` → 4.9 reprova com
  `500`.
- [x] 5.3 [`apps/api`] g3: sem a consulta prévia → 4.8 reprova no caso com o
  `apps/connectors` fora do ar (e 4.9 continua verde, pelo índice).
- [x] 5.4 [`apps/api`] g4: mensagem do `409` com "exclua a base" → 4.8 reprova.
- [x] 5.5 [`apps/api`] g5: `Timeout` de 5 s na composição real → 4.11 reprova.
- [x] 5.6 [`apps/api`] g6: handler assina `operator` em vez de `service:api` → 2.5
  e 4.11 reprovam (e 6.3, quando existir).
- [x] 5.7 [`apps/api`] g7: `Connectors:BaseUrl` obrigatório no boot → a suíte
  inteira do `apps/api` reprova na fixture.
- [x] 5.8 [`apps/api`] g8: `401` do `apps/connectors` repassado como `401` → 3.3 e
  4.10 reprovam.

  **Reprovou:** `ConnectorsFolderClientTests.ErrorOutOfContract_IsConnectorsError502WithReceivedStatus(Unauthorized)` e `CreateSynced_ConnectorsRejectsTheToken_Is502AndNever401Nor403(Unauthorized)` reprovaram.

  **Reprovou:** 94 de 94 testes de `ServiceScopeAuthorizationTests` e `KnowledgeDocumentCatalogTests` reprovaram na fixture (`InvalidOperationException: Connectors:BaseUrl não configurado.`), sem executar caso nenhum.

  **Reprovou:** `ServiceTokenDelegatingHandlerTests` (os dois casos) e `CreateSynced_SendsServiceApiTokenToConnectors` reprovaram. O 6.3 ainda não existia.

  **Reprovou:** `RealComposition_ConnectorsClientHasThe35SecondLimit` reprovou.

  **Reprovou:** `CreateSynced_FolderAlreadyUsed_Returns409WithTheBaseThatUsesIt` (os dois casos) e a corrida reprovaram na asserção negativa.

  **Reprovou:** os quatro casos `CreateSynced_FolderAlreadyUsed_*` reprovaram (com o `apps/connectors` fora do ar, sem configuração, e com `RequestCount` diferente de zero nos dois casos de base ativa e inativa); a corrida ficou verde, pelo índice, como previsto.

  **Reprovou:** `CreateSynced_TwoConcurrentCreatesOfTheSameFolder_OneCreatesTheOtherGets409` reprovou com um `500` na resposta do perdedor.

  **Reprovou:** `CreateSynced_StoresFolderNameAndUrlFromConnectors_NotFromTheBody` reprovou (`Expected: "Nome vindo do apps/connectors"`).

## 6. Ida e volta (`tests/`)

Fonte de contêiner nova autorizada pelo mantenedor em 03/10/2026 (D8).

- [x] 6.1 [`tests/`] `tests/ApiConnectorsRoundTrip.Tests`: `.csproj` com
  `ProjectReference` para `Buteco.Api` e `Buteco.Connectors` com `Aliases`, o
  `FakeConnector.cs` do `apps/connectors` por `<Compile Include Link>`, pacotes já
  fixados em `Directory.Packages.props` (nenhuma versão nova); o projeto na lista de
  verificações do `CONTRIBUTING.md` (não há solução na raiz).
- [x] 6.2 [`tests/`] `RoundTripFixture`: um Postgres `pgvector/pgvector:pg18`,
  `apps/api` com `Connectors:BaseUrl` e o handler primário do `HttpClient` nomeado
  apontando para o `TestServer` do `apps/connectors`; `apps/connectors` com o
  conector falso na chave `fake` e sem credencial do Google; a mesma chave de
  assinatura literal nos dois.
- [x] 6.3 [`tests/`] Os quatro cenários de "Ida e volta" da spec
  `knowledge-sync-folder-validation`, o último com um segundo host do
  `apps/connectors` com chave diferente.

  **4/4** na primeira rodada, porque a implementação já existia. **Visto
  reprovando** com o guarda g6 reaplicado (o `apps/api` assinando `operator`): três
  dos quatro casos responderam `502` em vez de `201`/`422`, porque a tabela real do
  `apps/connectors` recusa `operator` na descrição de pasta; o da chave divergente
  continuou `502`, como deve. Guarda desfeito e conferido.

  Credencial do Google vazia por `UseSetting` (o `Program.cs` do `apps/connectors`
  lê antes do `Build()`); `TZ` do `apps/api` com o fuso da máquina, como as fixtures
  dele.

  **Feito.** O `FakeConnector.cs` entra por `<Compile Include Link>`, sem cópia e
  sem mudança no `apps/connectors`. Para ele compilar como está (usa
  `Buteco.Connectors.Connectors` sem alias), o `ProjectReference` do
  `apps/connectors` leva `Aliases` `global,ConnectorsAssembly`; o do `apps/api` fica
  só em `ApiAssembly`, que é o que evita a colisão dos dois `Program`. Nenhum pacote
  novo: todos já fixados em `Directory.Packages.props`.

## 7. Documentação

- [x] 7.1 [docs] `docs/configuration.md`: `Connectors__BaseUrl` na seção do
  `apps/api`, opcional, com os três estados da D1. Nada na seção do stack de
  servidor.
- [x] 7.2 [docs] `.env.example`: `Connectors__BaseUrl=http://localhost:5037`, com
  o comentário dos três estados.
- [x] 7.3 [docs] `docs/architecture.md`: o `apps/api` como cliente do
  `apps/connectors` (descrição de pasta, `service:api`, os 35 s e a cadeia de
  limites), e o par dos `DelegatingHandler`.
- [x] 7.4 [docs] `01-ARQUITETURA_E_CONVENCOES.md`: Autenticação (o `apps/api`
  também assina token de serviço, `service:api`, só para o `apps/connectors`) e
  Contrato de conector (primeiro consumidor da descrição de pasta).
- [x] 7.5 [docs] `02-HISTORICO_E_STATUS.md`: o que a change mediu (baseline e fim
  de testes e contêineres, laço da corrida, guardas) e a recusa
  `connectors-not-configured` em produção até a #119.
- [x] 7.6 [docs] `CHANGELOG.md`, em `[Unreleased]`.
- [x] 7.7 [docs] `python3 scripts/check-docs.py` sem erro.

  **Medido:** `Integridade da documentação: OK`.
- [x] 7.8 [docs] Corrigir a lacuna da **#127**: `apps/connectors/` na árvore de pastas
  e `Connectors.sln` na lista de solutions do `docs/conventions.md`, no bloco que
  esta change já editava. Conferido que a árvore e a lista só existem nesse arquivo
  (`docs/development.md`, `CONTRIBUTING.md` e `README.md` não as têm). O PR fecha a
  #127 com `Closes #127`, decisão do mantenedor em 03/10/2026.
- [x] 7.9 [docs] Ajustar a regra "`Closes #<issue>` no corpo do PR" da convenção 24
  do `01`: achado na fila entra como `Refs #N`, pelo motivo de antes; achado
  corrigido por inteiro no mesmo PR entra como `Closes #N` (caso de origem: #127);
  uma palavra-chave por issue, em linha própria. A regra só aparece no `01`:
  `CONTRIBUTING.md`, `docs/conventions.md` e `.github/PULL_REQUEST_TEMPLATE.md` não
  mencionam `Closes`, `Refs` nem "uma issue".

## 8. Fechamento

- [x] 8.1 [`apps/api`] `dotnet test apps/api/Api.sln` com o total medido contra a
  baseline da 1.1.
- [x] 8.2 [`apps/connectors`] `dotnet test apps/connectors/Connectors.sln`, sem
  mudança esperada.
- [x] 8.3 [`tests/`] `dotnet test tests/ApiConnectorsRoundTrip.Tests` e
  `dotnet test tests/InboxOrchestratorRoundTrip.Tests`.
- [x] 8.4 [`apps/api`, `tests/`] Remedir classes e fontes de contêiner com o
  comando da 1.1; o delta tem de ser zero no `apps/api` e +1 fonte em `tests/`.
- [x] 8.5 [`apps/api`, manual] Com o `apps/connectors` local e uma chave real do
  Google fora do repositório: `POST /knowledge-bases` `Synced` com a pasta de
  teste da etapa 0 (`201`, nome e URL do Drive) e com uma pasta não compartilhada
  (`422` `access-denied` com o e-mail). Sem a chave do Google: `422`
  `provider-not-configured`. Com o `apps/connectors` parado: `503`
  `connectors-unavailable`. Resultado aqui e no `02`.
- [x] 8.6 [docs] Qualquer divergência entre a implementação e o `design.md`
  corrige o `design.md` com a causa (convenção 9).

  **Quatro correções no `design.md`, nenhuma de comportamento especificado:** D1 (a
  recusa sem endereço fica no cliente, não no comando); D3 (o `detail` ausente, não
  nulo, no erro do `apps/connectors`); D8 (o alias global do assembly do
  `apps/connectors`, para o link compilar sem mudança; e o publisher de indexação,
  que não precisou ser trocado na ida e volta).

  **Feito em 03/10/2026, com o `apps/connectors` (5037) e o `apps/api` locais e o
  Postgres de desenvolvimento.** O resultado, item por item, está no `02`. Condições
  do ambiente: o banco local estava duas migrações atrás e foi atualizado antes; a
  porta 5017 estava ocupada por um `apps/api` antigo, não tocado, e o da verificação
  rodou na 5117. A pasta "que a conta não lê" foi um id no formato do Drive fora do
  alcance da conta, porque não havia uma pasta real não compartilhada à mão; para a
  service account os dois casos são o mesmo `404 notFound` (D5 da #103). As bases
  criadas foram apagadas por SQL no banco local, conferido sem sobra.

  **Medido:** `apps/api` **44 classes e 42 fontes**, iguais à 1.1 (delta zero);
  `tests/ApiConnectorsRoundTrip.Tests` uma classe com uma fonte (o Postgres da
  fixture): **+1 em `tests/`**.

  **Medido:** `ApiConnectorsRoundTrip.Tests` **4/4**; `InboxOrchestratorRoundTrip.Tests` 4/4, igual à baseline.

  **Medido:** 137 aprovados + 1 ignorado / 138, igual à baseline.

  **Medido (03/10/2026, 16:35):** **697/697** em 1 min 36 s, `load average` 8,0 no
  início e 19,8 no fim, sem outra sessão de teste. Contra a baseline de 607, por nome
  nos `.trx`: 93 entradas novas e 3 saídas. Das 3, duas são os mesmos casos de
  `contentMode` desconhecido sob o nome novo do método (4.13), e uma é o caso `Synced`,
  retirado com o requisito. 43 em `KnowledgeBaseCatalogTests` (41 novas + 2
  renomeadas), 37 em `ConnectorsFolderClientTests`, 10 em
  `ConnectorsConfigurationStartupValidationTests`, 2 em
  `ServiceTokenDelegatingHandlerTests`, 1 em `ServiceScopeAuthorizationTests`.
