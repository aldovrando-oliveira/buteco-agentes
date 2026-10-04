# Tarefas — `ciclo-de-sincronizacao` (#105)

**Branch:** `feat/105-ciclo-de-sincronizacao`, criada de `0499161` (`main`
atualizada, sem commits à frente) e atualizada com a `main` em `cb74315` (merge da #108,
PR #140, e da #46, PR #142), sem conflito de código: o `02` e o `CHANGELOG.md`
conflitaram só por acréscimo das duas partes, mantidas inteiras. A ida e volta rodou de
novo contra os handlers de `/sync` alterados pela #108: **7/7**.

**Apps afetados:** `apps/connectors` (código e testes) e
`tests/ApiConnectorsRoundTrip.Tests`. O `apps/api` não muda: só é exercitado pela ida
e volta. Nenhuma tarefa toca `apps/frontend`, `apps/workers` ou `apps/inbox`. Nada de
`docker-compose.prod.yml`, `.env.prod.example`, nginx do stack ou `deployment.md`
(#119).

---

## 1. Antes do código

- [x] 1.1 [`apps/connectors`, `tests/`] Medir a baseline pelo critério do `02`, nunca de
  memória: `dotnet test apps/connectors/Connectors.sln` e
  `dotnet test tests/ApiConnectorsRoundTrip.Tests` em `0499161`, com total, passados,
  ignorados e o `load average`. Registrar inline aqui.

  **Medido em 03/10/2026 20:11 -03, sobre `0499161`** (árvore com a change não
  rastreada e a correção do `docs/README.md`, nenhum código): `apps/connectors`
  **137 aprovados + 1 ignorado / 138** (o ignorado é o teste manual do Google), 9 s;
  `tests/ApiConnectorsRoundTrip.Tests` **4/4**, 4 s. `load average` entre 4,9 e 6,7.
- [x] 1.2 [`tests/`] Ler as respostas reais do `apps/api` que o ciclo consome, num teste
  descartável na fixture da ida e volta (apagado depois): o texto de
  `GET /sync/knowledge-bases`, de `GET .../documents`, do `200` do upsert, do `400`
  `too-large` (com `code` e `contentBytes`), do `400` de forma (sem `code`), do `204`
  da exclusão e do `200` de `sync-results`, todos com um token `service:connectors`.
  Divergência da tabela do Context corrige o `design.md` antes do grupo 2.

  **Lido em 03/10/2026, sonda apagada depois.** Sem divergência das respostas de
  contrato: lista de bases com `id`, `provider`, `folderId`, `folderName`, `folderUrl`,
  `isActive`; referências com `externalRef`, `externalVersion`, `documentId`; upsert
  `200` com `outcome` como string (`Unchanged` medido); `400` `too-large` com `code`,
  `detail`, `contentBytes` e `maxContentBytes` e `application/problem+json`; `400` de
  forma sem `code`; exclusão `204`, e `404` para base inexistente nas duas rotas;
  `sync-results` `200` com a base, `lastFinishedAt` avançando também na falha e
  `failingSince` preenchido nela; `401` sem token. **Acréscimo:** o primeiro upsert
  respondeu `500` com `BrokerUnreachableException`, porque a fixture não tem RabbitMQ e
  o `apps/api` publica a indexação depois de gravar; o documento ficou gravado. Aberta a
  **#138** (defeito do `apps/api`, fora do escopo). O `design.md` registra o caso no
  Context, na D4 (é `sync-api-error`) e na D11 (a fixture ganha um publicador falso).
  Nenhuma decisão mudou.
- [x] 1.3 [`apps/connectors`] Conferir que o `ConnectorsFactory` alcança o valor de
  `Api:BaseUrl` lido pela checagem no boot, pelas options e pelo registro condicional
  do agendamento, que acontece antes do `Build()` (o Program.cs do app lê a chave de
  assinatura antes do `Build()`, e por isso o factory usa `UseSetting`). Se não
  alcançar, a D2 é corrigida com a causa.

  **Conferido no código e nos testes que já existem; a D2 não muda.** O
  `AddGoogleDriveConnector` lê a configuração antes do `Build()` e registra o provedor
  condicionalmente; `ConnectorRegistrationTests.ComposicaoReal_ComCredencial_TemOsTresRegistrosDoGoogle`
  passa a credencial por `UseSetting` e encontra os três registros. É o mesmo caminho
  que o registro condicional do agendamento vai usar. E a precedência sobre o arquivo
  de desenvolvimento já é exercida pela suíte inteira: o `Auth:TokenSigningKey` do
  `appsettings.Development.json` é diferente do de teste, e os tokens de teste
  validam. Que um valor **vazio** posto por `UseSetting` também vence o arquivo fica
  medido na 2.3.

## 2. Configuração e token (`apps/connectors`)

- [x] 2.1 [`apps/connectors`] Testes primeiro: `ApiConfigurationValidationTests` com os
  três estados da D2 sobre a extensão e sobre a composição real (ausente sobe com
  aviso; vazio conta como ausente; `localhost` derruba o boot nomeando `Api:BaseUrl`
  sem ecoar o valor). Ver reprovar.
- [x] 2.2 [`apps/connectors`] `ApiOptions` e `ValidateApiConfiguration` sobre o host
  construído, chamada no `Program.cs` depois dos `Map*`.
- [x] 2.3 [`apps/connectors`] **Antes** de pôr o valor no arquivo de desenvolvimento:
  o `ConnectorsFactory` fixa `Api:BaseUrl` vazio por `UseSetting`. A suíte
  inteira do `apps/connectors` roda sem nenhuma chamada a `localhost:5017`. Só
  depois disso, `Api:BaseUrl` igual a `http://localhost:5017` no
  `appsettings.Development.json` (D2).

  **Medido:** com o valor no arquivo de desenvolvimento e sem o vazio no factory,
  `DefaultFactory_ResolvesApiBaseUrlAsNotConfigured` reprovou com `Api:BaseUrl resolvido
  como 'http://localhost:5017' na composição padrão do factory`; com o vazio por
  `UseSetting`, passa — o vazio vence o arquivo. O `RoundTripFixture` fixa o mesmo vazio.
  A suíte inteira: 150 aprovados + 1 ignorado / 151. **Não é hipotético:** há um
  `apps/api` antigo escutando na 5017 desde 02/10, que receberia as chamadas.
- [x] 2.4 [`apps/connectors`] Teste primeiro, depois o código:
  `ServiceTokenDelegatingHandler` assina `service:connectors` a cada requisição, com
  TTL de 5 min; o teste valida o token com o `TokenService` do app e afirma o `sub`. O
  comentário aponta para os outros dois handlers e para a tabela do `apps/api` (D1).

## 3. Cliente do `apps/api` (`apps/connectors`)

- [x] 3.1 [`apps/connectors`] `FakeSyncApiHandler` em `Support/`: responde às cinco
  rotas de `/sync` com o estado configurado pelo teste, grava cada requisição com o
  cabeçalho `Authorization`, e permite forçar `404`, `409`, `503`, `400` com e sem
  `code`, `401` e falha de rede por rota.
- [x] 3.2 [`apps/connectors`] `SyncApiClient` com `HttpClient` nomeado, `BaseAddress` de
  `Api:BaseUrl`, o handler da 2.4 e limite fixo de 30 s por chamada. Cada resposta vira
  um desfecho tipado (sucesso, conteúdo recusado com `code`, base ausente, contenção,
  fora do contrato, sem resposta), lendo o `code` do **texto** da resposta.
- [x] 3.3 [`apps/connectors`] Testes do cliente contra o handler falso, um por desfecho,
  inclusive `400` sem `code` como fora do contrato e `401` como fora do contrato.

## 4. Ciclo de uma base (`apps/connectors`)

- [x] 4.1 [`apps/connectors`] `FakeConnector` com listagem e markdown configuráveis,
  inclusive lançando `ConnectorFailure` por arquivo (delta de `connector-plugin`), sem
  quebrar os testes que já o usam.
- [x] 4.2 [`apps/connectors`] Testes primeiro, em `KnowledgeBaseSyncCycleTests`, um por
  cenário de `knowledge-sync-cycle` dos requisitos "Ordem do ciclo", "Exclusão",
  "Falha de arquivo", "Cota", "Base inexistente", "apps/api fora do ar" e "Contenção".
  Asserção sobre as requisições gravadas pelo handler falso, inclusive as negativas:
  nenhuma exclusão enviada, nenhum markdown pedido, nenhuma gravação. Ver reprovar.
- [x] 4.3 [`apps/connectors`] `KnowledgeBaseSyncCycle`: descrever, listar, ler
  referências, baixar e enviar o que mudou, excluir o conjunto da D6 depois dos
  upserts, gravar o desfecho. Alcance de cada falha pela tabela da D4; `rate-limited`
  pela D5; `404`/`409` pela D8; `503` pela D7. Todas as chamadas da base dentro do
  `try/catch` (convenção 4).
- [x] 4.4 [`apps/connectors`] `SyncCodes` com `knowledge-base-not-found`,
  `sync-not-configured`, `sync-api-unavailable` e `sync-api-error`, e teste de que todo
  código passa em `ConnectorCodes.IsValid`.

- [x] 4.5 [`apps/connectors`] Testes primeiro, em `RefusalMemoryTests` e em
  `KnowledgeBaseSyncCycleTests`, um por cenário do requisito "Recusa determinística
  não é reenviada com o mesmo marcador" (D14): `too-large` no primeiro ciclo e, no
  segundo com o mesmo marcador, **nenhum** pedido de markdown e **nenhum** upsert para
  o arquivo, com a entrada nos ignorados igual (código e detalhe); marcador novo pede o
  markdown e envia; `provider-unavailable` no primeiro ciclo e pedido de markdown de
  novo no segundo; cada uma das outras três recusas da #120 lembrada; `rate-limited`,
  `provider-error`, `file-not-found` e `503` **não** lembrados; referência que some de
  uma listagem completa e volta com o mesmo marcador é tentada de novo; `404` na base
  apaga as entradas dela; base que sai de `GET /sync/knowledge-bases` tem as entradas
  apagadas no fim da rodada. Ver reprovar.
- [x] 4.6 [`apps/connectors`] `RefusalMemory` (singleton, chave base + referência,
  valor marcador + código + detalhe) e o uso no ciclo: consulta antes de pedir o
  markdown, gravação só nas quatro recusas, invalidação por marcador, limpeza por
  referência ao fim da base com listagem completa, por base no `404` e no fim da
  rodada. A contagem de ignorados vindos da memória vai para o log (D13).

## 5. Rodada, lock e agendamento (`apps/connectors`)

- [x] 5.1 [`apps/connectors`] Testes primeiro: `SyncRoundTests` (bases na ordem da lista;
  `404` numa não interrompe as outras; exceção inesperada numa não interrompe as
  outras; `rate-limited` e `apps/api` sem resposta encerram a rodada; base em
  sincronização é pulada) e `SyncSchedulerServiceTests` com o `ManualTimeProvider`
  (rodada no boot; próxima só depois de 5 min; rodada longa não acumula; sem
  `Api:BaseUrl` nenhuma rodada).
- [x] 5.2 [`apps/connectors`] `SyncInProgress`, `SyncRound` e `SyncSchedulerService`
  (`BackgroundService` com `PeriodicTimer(Interval, TimeProvider)`), registrado só com
  `Api:BaseUrl` configurada. A duração de cada rodada vai para o log. Um teste afirma
  que a composição padrão do `ConnectorsFactory` (com `Api:BaseUrl` vazio, 2.3) não
  registra o `SyncSchedulerService`.

## 6. "Sincronizar agora" (`apps/connectors`)

- [x] 6.1 [`apps/connectors`] Testes primeiro, em `SyncEndpointsTests` pelo
  `ConnectorsFactory`: `202` para base da lista, inclusive inativa; `404`
  `knowledge-base-not-found`; `503` `sync-not-configured` sem chamada de rede; `503`
  `sync-api-unavailable`; `502` `sync-api-error` para `401` do `apps/api`; dois pedidos
  simultâneos com o conector falso segurando a listagem numa barreira → os dois `202` e
  uma listagem só.
- [x] 6.2 [`apps/connectors`] `SyncEndpoints` com
  `POST /connectors/knowledge-bases/{knowledgeBaseId:guid}/sync`; o ciclo em segundo
  plano com o token de parada da aplicação, não o da requisição.
- [x] 6.3 [`apps/connectors`] A rota entra na tabela de subjects só para `operator`; a
  matriz de `SubjectAuthorizationTests` ganha a rota (`service:api`,
  `service:inbox`, `service:connectors` e `qualquer` → `403`), e a checagem de boot nos
  dois sentidos passa na composição real.

  **Testes existentes que mudaram, todos pela rota nova e sem mudar o que cada asserção
  afirma:** `SubjectAuthorizationTests.SubjectXRota` (a matriz ganhou o método HTTP e a
  rota `POST`), `ApiNasRotasDoOperador_Recebe403` (mais uma linha, a rota nova → `403`,
  cenário do delta de `connectors-api`), `RoutePatternTests.RawTextDasRotasDoConector`
  (a lista de padrões ganhou o quarto) e
  `SubjectRouteValidationTests.ComposicaoReal_PassaSemDivergencia` (a contagem da tabela
  passou de 3 para 4). Os dois últimos enumeram as rotas do app e reprovaram com a rota
  nova, como deviam.

## 7. Logs (`apps/connectors`)

- [x] 7.1 [`apps/connectors`] Em `SecretLeakTests`: um ciclo que envia um documento com
  marcador de texto conhecido, com nome de arquivo conhecido, e grava o resultado;
  nenhuma mensagem capturada contém o marcador, o token enviado (lido do handler
  falso), a chave de assinatura nem o nome do arquivo.

  **Feito em `NenhumLogDoCicloContemConteudoTokenChaveOuNomeDeArquivo`**, pela composição
  real (logs de todas as categorias, inclusive o `HttpClient` do `apps/api`), com
  precondição contra vacuidade: 30 linhas de log e 6 tokens procurados. **Visto
  reprovar:** com o nome do arquivo acrescentado ao aviso de contenção do ciclo, reprovou
  com `Assert.DoesNotContain() Failure: Sub-string found ... "Relatorio-Confidencial-Nome-9c2e.md"`;
  desfeito.

## 8. Guardas que precisam falhar contra o defeito (convenção 15)

Cada um aplicado sobre uma cópia, visto reprovar no teste previsto, desfeito, e
conferido por `grep` que não ficou. Registrar aqui o teste e a mensagem.

- [x] 8.1 [`apps/connectors`] Excluir também os ignorados da listagem → reprova
  "Arquivo ignorado que continua na pasta mantém o documento".

  **Reprovou:** `IgnoredFileStillInTheFolder_KeepsTheDocument`, com `Assert.Empty() Failure:
  Collection was not empty ... DELETE .../documents?externalRef=ref-bloqueado`. Desfeito.
- [x] 8.2 [`apps/connectors`] Excluir antes dos upserts, ou com a listagem que falhou →
  reprova "Listagem que falha na segunda página não exclui nada" ou "Cota estourada no
  meio da base".

  **Reprovou nas duas formas.** Exclusão antes dos upserts: 3 testes, entre eles
  `RateLimitedInTheMiddleOfTheBase_DeletesNothing_RecordsFailed_AndStopsTheRound`, com
  `Assert.Empty() Failure: ... DELETE ...?externalRef=ref-sumiu` (também
  `ShapeRefusal_IsNotIgnored...` e `ProviderAuthFailedOnAFile...`). Listagem que falhou
  tratada como vazia: `ListingThatFailsOnALaterPage_DeletesNothing_SendsNothing_AndIsNotSucceeded`
  (`DELETE ...?externalRef=ref-a`) e `FailedListing_DoesNotPruneTheMemory` (`Expected: 1
  Actual: 0`). Desfeito.
- [x] 8.3 [`apps/connectors`] Não gravar nada no `rate-limited` → reprova "Cota estourada
  no meio da base".

  **Reprovou:** `RateLimitedInTheMiddleOfTheBase...` e
  `RateLimitedOnTheFolder_RecordsFailed_AndStopsTheRound`, com `Assert.Single() Failure:
  The collection was empty`. Desfeito.
- [x] 8.4 [`apps/connectors`] Tirar o lock da D9 → reprova "Dois pedidos de sincronização
  simultâneos rodam um ciclo".

  **Reprovou:** `SyncEndpointsTests.TwoSimultaneousRequests_RunOneCycle` (`Expected: 1
  Actual: 2` listagens) e `SyncRoundTests.BaseAlreadySyncing_IsSkipped_AndStaysMarked` (a
  base marcada foi descrita). Desfeito.
- [x] 8.5 [`apps/connectors`] Assinar `operator` no handler → reprova o teste do token
  (2.4) e o da ida e volta (9.3).

  **Reprovou nos dois:** `ServiceTokenDelegatingHandlerTests.EveryRequest_CarriesAFreshServiceConnectorsToken`
  (`Expected: "service:connectors"`, com o token de `operator`) e
  `SyncCycle_TokenOfTheConnectors_IsAcceptedByTheRealApi` (`Assert.All() Failure: 4 out of
  4 items ... ("GET", "/sync/knowledge-bases", 200, ...)`). **Achado:** a primeira redação
  da 9.3 só afirmava "nenhum `401`/`403`", e não reprovaria — o `apps/api` deixa o
  operador passar em tudo (D6 da #102), e as chamadas com o token de `operator`
  responderam `200`. A 9.3 passou a validar o token que chegou ao `apps/api` com o
  `ITokenService` do próprio `apps/api`, e só então reprovou. Desfeito.
- [x] 8.6 [`apps/connectors`] Pôr a recusa de forma (`400` sem `code`) nos ignorados →
  reprova "Recusa de forma não vira ignorado".

  **Reprovou:** `ShapeRefusal_IsNotIgnored_DeletesNothing_AndFailsWithSyncApiError`
  (`DELETE ...?externalRef=ref-sumiu`) e `ServerErrorOnUpsert_FailsWithSyncApiError`.
  A primeira aplicação deste guarda **não foi aplicada** (o trecho de busca não casou) e o
  teste ficou verde por isso; refeita com o trecho exato. Desfeito.
- [x] 8.7 [`apps/connectors`] Retirar a consulta à memória da D14 (o ciclo pede o
  markdown mesmo com a entrada guardada) → reprova "Recusa determinística não é
  baixada de novo com o mesmo marcador".

  **Reprovou:** `TooLarge_SameMarkerNextCycle_IsNotDownloadedNorSent_AndStaysIgnored`
  (`Expected: 1 Actual: 2` pedidos de markdown) e os três casos de
  `OtherDeterministicRefusals_AreRemembered` (`["ref-a", "ref-a"]`). Desfeito.
- [x] 8.8 [`apps/connectors`] Guardar também as falhas transitórias na memória →
  reprova "Falha transitória não é lembrada".


  **Reprovou:** os três casos de `TransientMarkdownFailures_AreNotRemembered` (o markdown
  pedido uma vez só, `["ref-a"]`) e seis de `RefusalMemoryTests.NonDeterministicCode_IsNeverStored`
  (`Expected: 0 Actual: 1`). Desfeito.

  Conferido por `grep` depois dos guardas: nenhum ficou no código.
## 9. Ida e volta (`tests/ApiConnectorsRoundTrip.Tests`)

- [x] 9.1 [`tests/`] `RoundTripFixture`: o `apps/api` usa um publicador de indexação
  falso em memória (1.2, #138). O cliente do `apps/connectors` aponta para o
  `TestServer` do `apps/api`, com `Api:BaseUrl` configurada, e o
  `SyncSchedulerService` sai da composição de teste. `FolderValidationRoundTripTests`
  passa a ser `partial`; os cenários novos ficam em
  `FolderValidationRoundTripTests.SyncCycle.cs`. **Nenhuma classe nova com contêiner**;
  se for preciso uma, parar e pedir autorização.

  **Feito:** `FolderValidationRoundTripTests` é `partial`, os cenários estão em
  `FolderValidationRoundTripTests.SyncCycle.cs`, e nenhuma classe nova sobe contêiner. A
  fixture ganhou o `InMemoryIndexingPublisher` (cópia do duplo do `apps/api`), o sentido
  `apps/connectors` → `apps/api` (`Api:BaseUrl` próprio e o `TestServer` do `apps/api`
  como handler primário) e um gravador das requisições desse sentido, com método,
  caminho, status e token.
- [x] 9.2 [`tests/`] Base `Synced` criada pela rota real; o ciclo roda pelo
  "Sincronizar agora" com o conector falso: cria dois documentos, atualiza um com
  marcador novo, responde `Unchanged` a marcador novo com o mesmo texto e não registra
  evento, exclui o que sumiu, e grava `Succeeded` com os ignorados. Asserções pelo
  `apps/api` real (rotas e banco).
- [x] 9.3 [`tests/`] O token do `apps/connectors` é aceito pela tabela real do
  `apps/api`: nenhuma chamada do ciclo responde `401` nem `403`.
- [x] 9.4 [`tests/`] Um arquivo acima de 1 MiB: a recusa real `too-large` do `apps/api`
  chega aos ignorados gravados com o código e o `contentBytes`, e o documento anterior
  continua. **Um segundo ciclo com o mesmo marcador não envia o upsert de novo** (D14),
  e os ignorados gravados continuam com `too-large`: contado pelas requisições de
  upsert que chegam ao `apps/api` real, e pelo markdown não pedido ao conector falso.
- [x] 9.5 [`tests/`] Os quatro cenários atuais de validação de pasta continuam passando
  sem mudança de asserção.


  **Medido:** 7/7 na ida e volta — os 4 de validação de pasta sem nenhuma mudança e os 3
  novos.
## 10. Verificação manual contra o Drive real

- [x] 10.1 [`apps/connectors`, `apps/api`] Com os dois apps locais, o Postgres de
  desenvolvimento e a service account da etapa 0: criar uma base `Synced` com a pasta
  de teste e pedir "Sincronizar agora"; conferir documentos criados, um Doc editado
  atualizado, um Doc renomeado com evento `Updated` só de título (corrigido na própria
  verificação; ver a nota abaixo), um Doc com o compartilhamento alterado sem evento
  novo, um arquivo mandado para a lixeira
  excluído, um Doc com download bloqueado nos ignorados, e uma pasta não compartilhada
  gravando `Failed` com `access-denied` sem excluir. **Sigilo:** a chave só na variável
  do processo; nenhum id de pasta, e-mail ou resposta crua no repositório; o `02`
  registra só o que foi conferido e o resultado. As bases criadas são apagadas do
  banco local no fim.


  **Feito em 03/10/2026, 21:40–22:02 -03.** Ambiente isolado, porque a stack de
  desenvolvimento não estava de pé e as portas padrão não podiam ser usadas: `apps/api`
  na **5117**, `apps/connectors` na **5137**, e um Postgres (`pgvector/pgvector:pg18`) e um
  RabbitMQ (`rabbitmq:4.3-management`) próprios na **25532** e na **25772**, com o banco
  migrado do zero. O `apps/api` antigo da 5017 não foi tocado. Chave do Google só na
  variável do processo, sem eco. Ações no Drive feitas pelo mantenedor, uma por vez.

  | ação | resultado |
  |---|---|
  | primeira sincronização | `202`; ciclo de 47 s; **5 criados**, 5 eventos `Created`; ignorados atalho (`shortcut-not-followed`), planilha (`unsupported-type`, com o `mimeType`) e subpasta (`subfolder-not-synced`); `Succeeded` |
  | editar o texto de um Doc | 2,5 s; **1 atualizado** (revisão 2, um evento `Updated` com `contentChanged`); os outros intocados |
  | renomear um Doc | `Updated` com `titleChanged: true` e `contentChanged: false`, revisão 1, sem reindexar. **Contrariou a spec** ("renomear não gera evento"): o título é o nome do arquivo (D13). Cenário corrigido com autorização do mantenedor, e coberto na ida e volta (passo 2b da 9.2) |
  | alterar a permissão de um Doc | nenhum evento e nenhuma revisão. **Medido: nenhum marcador mudou** nesse ciclo nem nas duas rodadas periódicas do intervalo — desta vez a permissão não mexeu no `modifiedTime`, ao contrário da P6 da etapa 0. O caminho "marcador novo, mesmo texto → `Unchanged`" apareceu no ciclo do renomear ("inalterados 1") e é coberto pela ida e volta |
  | mandar um `.md` para a lixeira | **1 excluído** (evento `Deleted`), só ele |
  | bloquear o download de um Doc | o Doc entra nos ignorados com `download-blocked` e **continua na base** (revisão 2, sem evento, sem exclusão) |
  | tirar o compartilhamento da pasta | `Failed` com `access-denied` e o e-mail da service account no `detail`; "falhando desde" preenchido; última concluída, ignorados e nome da pasta preservados; **nenhum documento excluído** |
  | desfazer tudo no Drive | `Succeeded`; erro e "falhando desde" limpos; o arquivo restaurado voltou como documento (criado pela rodada periódica); o Doc com o nome original; só os 3 ignorados originais |

  As rodadas periódicas de 5 minutos rodaram durante a verificação, sem conflito com os
  pedidos. **Logs reais (824 linhas):** nenhum nome de arquivo, nenhum trecho da chave
  privada, nenhum e-mail e nenhum token. **Limpeza:** a base apagada por SQL (0 bases, 0
  documentos, 0 eventos), os dois apps derrubados, os dois contêineres removidos, as
  portas livres e o arquivo do token apagado.
## 11. Documentação

- [x] 11.1 [`apps/connectors`] `docs/architecture.md`: o ciclo e a direção nova
  `apps/connectors` → `apps/api`, o `service:connectors` assinado, e a tabela de
  subjects com a rota nova.
- [x] 11.2 [`apps/connectors`] `docs/configuration.md` e `docs/development.md`:
  `Api__BaseUrl` no `apps/connectors`, com os três estados; como rodar o ciclo em
  desenvolvimento. `.env.example`: o bloco do `apps/connectors` documenta
  `Api__BaseUrl` (a variável já existe no bloco do `apps/inbox`, com o mesmo valor).
- [x] 11.3 [`apps/connectors`] `01-ARQUITETURA_E_CONVENCOES.md` (contrato de conector),
  `02-HISTORICO_E_STATUS.md` e `CHANGELOG.md` (`[Unreleased]`), com o que a
  implementação mediu.
- [x] 11.4 [documentação] **#137**, achado da implementação: `docs/README.md` dizia
  "quatro apps" em dois trechos (`:29` e `:46`), lacuna da #103. Corrigidos para cinco,
  citando o `apps/connectors`; `grep -rni "quatro apps\|4 apps" docs/ CONTRIBUTING.md
  README.md` não acha outra ocorrência. O PR fecha a #137 com `Closes #137`, em linha
  própria (convenção 24 do `01`).

## 12. Fechamento

- [x] 12.1 [`apps/connectors`, `tests/`] `dotnet test apps/connectors/Connectors.sln` e
  `dotnet test tests/ApiConnectorsRoundTrip.Tests`, comparados à baseline da 1.1 por
  nome de teste. `dotnet test apps/api/Api.sln` para confirmar que não mudou.

  **Medido em 03/10/2026 22:04–22:06 -03:** `apps/connectors` **261 + 1 ignorado / 262**
  (baseline 137 + 1 / 138): por nome, 139 novos e 15 saídos, e os 15 são os casos de
  `SubjectXRota` renomeados (a teoria ganhou o método); +124 líquidos — 37 do ciclo, 23 do
  cliente, 14 da memória, 13 da configuração, 10 da rota, 8 da rodada, 8 do agendamento, 5
  da matriz de subjects, 3 dos códigos, 1 do conector falso, 1 do token e 1 do log. O único
  não aprovado é o teste manual do Google, ignorado como na baseline. Ida e volta **7/7**
  (baseline 4/4, +3, nenhum saído). `apps/api` **717/717**, idêntico por nome ao
  fechamento da #120.
- [x] 12.2 `python3 scripts/check-docs.py` e
  `openspec validate ciclo-de-sincronizacao --strict`.

  `Integridade da documentação: OK` e `Change 'ciclo-de-sincronizacao' is valid`.
