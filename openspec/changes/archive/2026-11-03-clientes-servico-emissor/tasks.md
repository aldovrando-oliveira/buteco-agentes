Cada guarda marcado **(guarda)** só conta depois de visto reprovar contra o defeito que existe para pegar (convenção 15). O defeito nomeado é reintroduzido, o teste roda e reprova, e a árvore volta, só no trecho reintroduzido. Fica registrado na tarefa o nome do teste, a mensagem da reprovação e o "voltou: <arquivo>" (guarda reintroduz só o defeito nomeado).

Suítes pelo `~/.cache/buteco-agents/bin/suite-234.sh`, com a regra assimétrica: verde conta mesmo com a CPU caindo, e reprovação só conta com a CPU em 100 do início ao fim. A baseline é `~/.cache/buteco-agents/234-baseline/r1/`, em `14d4ba9`, e a comparação é por nome de caso.

## 1. Descritores e opções (apps/api)

- [x] 1.1 (apps/api) Em `Identity/Clients/IdentityClients.cs`, acrescentar `InboxServiceClientId = "buteco-servico-inbox"`, `ConnectorsServiceClientId = "buteco-servico-connectors"` e `Service(clientId, displayName, secret)`. O descritor sai confidencial, com consentimento implícito, só `ept:token`, `gt:client_credentials` e `scp:buteco-api`, sem URI e sem requisito (design D1).
- [x] 1.2 (apps/api) Em `IdentityIssuerOptions`, acrescentar `ServiceClients` com `Inbox` e `Connectors`, cada um com `Secret`, na seção `Identity` (D2).
- [x] 1.3 (apps/api) `IdentityClientCatalogTests`: cada descritor de serviço é confidencial, tem exatamente as três permissões e nenhuma URI nem requisito. **(guarda)** Contra o descritor do inbox com `scp:buteco-connectors` a mais.
  - **Feito:** guarda visto reprovar: com `scp:buteco-connectors` acrescentado em `IdentityClients.Service`, `ServiceClient_IsConfidential_WithOnlyTokenClientCredentialsAndTheApiScope` reprovou nos 2 casos (`buteco-servico-inbox` e `buteco-servico-connectors`), 2/7 com falha. Voltou: `apps/api/src/Buteco.Api/Identity/Clients/IdentityClients.cs` (cópia de antes do defeito). Com a árvore de volta, 7/7.

## 2. Segredos no boot (apps/api)

- [x] 2.1 (apps/api) `Identity/Clients/ServiceClientSecret.cs`. Recusa ausente ou vazio, fora de base64 e menos de 32 bytes decodificados. A mensagem nomeia a chave e nunca o valor (D2).
- [x] 2.2 (apps/api) Em `ValidateIdentityIssuerConfiguration`, conferir os dois segredos, nesta ordem:
  - o formato (2.1);
  - os dois distintos entre si;
  - cada um distinto de `Identity:SigningKeyEncryptionKey` e de `Identity:TwoFactorEncryptionKey`;
  - cada um distinto de `Mcp:CredentialEncryptionKey`, quando ela estiver configurada e decodificar.

  As comparações são por bytes decodificados, no molde da #218. A mensagem de igualdade nomeia as duas chaves, no formato "`A` é igual a `B`: …", sem o valor. Os segredos são obrigatórios mesmo sem `Connectors:BaseUrl`, e nada lança no registro dos serviços.
  - **Feito:** a conferência de distinção usa os bytes já decodificados da `SigningKeyEncryptionKey` e da `TwoFactorEncryptionKey`, e o `TryDecodeBase64` da do MCP, que passou a ser lido uma vez só para as duas comparações (2 etapas e segredos). Mensagem de igualdade: "`A` é igual a `B`: cada segredo de serviço precisa ser distinto do outro e das chaves de cifra. Gere outro com `openssl rand -base64 32`."
- [x] 2.3 (apps/api) `ServiceClientSecretTests`, unidade: ausente, fora de base64 e 16 bytes, cada um com o nome da chave na mensagem. **(guarda)** Contra uma mensagem que interpola o valor: o teste afirma que o valor configurado não aparece.
  - **Feito:** guarda visto reprovar: com a mensagem de tamanho interpolando o valor (`$"{configurationName} ({configured}) tem …"`), `AShortSecret_IsRefused_NamingTheSetting_WithoutTheValue` reprovou (1/6). Voltou: `apps/api/src/Buteco.Api/Identity/Clients/ServiceClientSecret.cs`, sha igual ao de antes. Script: `~/.cache/buteco-agents/bin/guarda-234.sh`.
- [x] 2.4 (apps/api) `IdentityIssuerStartupValidationTests`, composição real pela `WebApplicationFactory`:
  - segredo ausente, curto ou igual ao outro → o boot falha, nomeando a chave, ou as duas no caso de igualdade;
  - o segredo do inbox igual a cada uma das três chaves de cifra, em três subidas → o boot falha nomeando as duas chaves;
  - nenhuma mensagem contém o valor;
  - com os segredos válidos, sobe.

  **(guarda)** Contra três defeitos, um de cada vez:
  - a chamada da conferência retirada do `ValidateIdentityIssuerConfiguration`;
  - a conferência que compara só os dois segredos entre si: os três casos das chaves de cifra precisam reprovar;
  - a comparação contra a `Mcp:CredentialEncryptionKey` retirada: o caso dela precisa reprovar sozinho.
  - **Feito:** casos novos: `RealComposition_WithoutAServiceClientSecret_FailsNamingTheSetting` (2), `…WithAShortServiceClientSecret…`, `…WithTheTwoServiceClientSecretsEqual…`, `…WithAServiceClientSecretEqualToAnEncryptionKey…` (3); o "com os segredos válidos, sobe" é o `RealComposition_Boots_WithTheRealRoutesAndEndpoints` que já existia, que passa com os segredos de teste. Guardas vistos reprovar, cada um com o arquivo de volta e sha igual: (a) chamada retirada → 7/7 com falha; (b) laço das chaves de cifra desligado (`secrets.Take(0)`) → os 3 casos das chaves reprovam, 4 passam; (c) sem a entrada da `Mcp:CredentialEncryptionKey` → só o caso dela reprova (1/7). Voltou: `apps/api/src/Buteco.Api/Identity/IdentityIssuerStartupValidation.cs`.
- [x] 2.5 (apps/api) `Support/TestAuthentication.ConfigOverrides`: os dois segredos de teste, distintos entre si, das três chaves de teste e dos valores de desenvolvimento. A suíte do `apps/api` sobe inteira com eles.
  - **Feito:** segredos de teste gerados com `openssl rand -base64 32`, como constantes `InboxServiceClientSecret` e `ConnectorsServiceClientSecret`.
- [x] 2.6 (apps/api) `appsettings.Development.json`: os dois segredos de desenvolvimento, marcados como de desenvolvimento e distintos das chaves de desenvolvimento. Conferir que o `dotnet run` sobe com `TZ=America/Sao_Paulo`.
  - **Feito:** com `TZ=America/Sao_Paulo`, `dotnet run --launch-profile http` logou "Emissor OIDC ligado, issuer http://localhost:5017/." e "Now listening on: http://0.0.0.0:5017", sem erro de segredo. `appsettings.json` ganhou a seção `ServiceClients` vazia, como as chaves.

## 3. Sincronização dos clientes de serviço (apps/api)

- [x] 3.1 (apps/api) No `IdentityClientCatalog.SyncAsync`, na mesma transação e sob o lock `BUTECOC1`, para cada cliente de serviço:
  - ausente → `CreateAsync` com o segredo;
  - presente → comparação de todos os campos menos o segredo (os do `DivergesAsync` do painel), e depois `ValidateClientSecretAsync` com o segredo configurado (H8);
  - divergiu em qualquer um dos dois → `UpdateAsync` com o descritor **sempre com o segredo** (H7).

  O log `SyncedEvent` diz "cliente X corrigido" ou "segredo do cliente X trocado", sem valor. O laço de `buteco-*` não declarado passa a pular os três declarados (D2).
  - **Feito:** a conferência de divergência ganhou `compareSecret`: no painel, segredo gravado continua sendo divergência; no de serviço, o segredo é conferido por `ValidateClientSecretAsync`. Os três declarados ficam em `DeclaredClientIds`.
- [x] 3.2 (apps/api) `IdentityClientCatalogSyncTests`, integração:
  - o teste `TheWarmupCreates_OnlyThePanelClient_AndTheThreeScopes` passa a `TheWarmupCreates_ThePanelAndTheServiceClients_AndTheThreeScopes`: os `buteco-*` são exatamente os três, e os de serviço são confidenciais. É mudança de asserção declarada no design (D5), por mudança de requisito, e não premissa falsa;
  - um cliente de serviço adulterado, com escopo e grant a mais, volta ao descritor, e um pedido com o segredo configurado continua emitindo token;
  - com o segredo trocado na configuração, depois do aquecimento, o antigo recebe `invalid_client`, o novo emite token, e o log cita a troca sem os valores;
  - um `buteco-outro` não declarado continua só logado.

  **(guarda)** Contra três defeitos, um de cada vez:
  - a correção com o descritor sem segredo: o cliente fica sem credencial, e o caso do adulterado reprova no pedido de token;
  - a sincronização que só cria e não confere o segredo: o caso do segredo trocado reprova;
  - o log que interpola o segredo.
  - **Feito:** casos: `TheWarmupCreates_ThePanelAndTheServiceClients_AndTheThreeScopes` (renomeado do `…OnlyThePanelClient…`, asserção mudada como declarado: os `buteco-*` são exatamente os três, ignorado o `buteco-outro` que o caso do não declarado cria), `ATamperedServiceClient_GoesBackToTheDescriptor_AndKeepsItsCredential`, `AServiceClientSecretThatNoLongerValidates_IsRewritten` (o "segredo trocado na configuração" é simulado gravando outro segredo no cliente e sincronizando, que é o mesmo estado: hash gravado de um valor, configuração com outro). O `AnUndeclaredButecoClient_IsOnlyLogged` não mudou. Guardas vistos reprovar, cada um com o arquivo de volta e sha igual: (a) descritor sem segredo na correção → **6/6 com falha, por `ValidationException`**, e não por cliente sem credencial: **premissa do H7 falsa na consequência** (o OpenIddict recusa cliente confidencial sem segredo, `ID2113`, e a transação inteira volta). A regra e o guarda não mudam; corrigidos só o comentário do `IdentityClientCatalog`, o da classe de teste e o design (H7, "Correção na implementação"). Pela regra de parada, toca só comentário e documentação: segui e registro. (b) `secretValid = true` → só `AServiceClientSecretThatNoLongerValidates_IsRewritten` reprova (1/6); (c) log com o segredo → o mesmo caso reprova (1/6). Voltou: `apps/api/src/Buteco.Api/Identity/Clients/IdentityClientCatalog.cs`.
  - **Refeito depois da 3.4:** os três guardas, sobre o catálogo com isolamento. (a) descritor sem segredo → 7/7 com falha, porque o ciclo agora lança no fim e todo `SyncAsync` da classe reprova; (b) sem conferir o segredo → 1/7; (c) log com o segredo → 1/7. Cada um com o arquivo de volta e sha igual.
- [x] 3.3 (apps/api) `ServiceClientTokenTests`, integração:
  - `client_credentials` com cada segredo → access token com `client_id` do cliente, `aud` só `buteco-api` e `typ` `at+jwt`, verificado pelo JWKS;
  - `scope=buteco-connectors` → `invalid_request` e nenhum `access_token` (correção na implementação, D1: era `invalid_scope`);
  - segredo errado → `invalid_client`.

  **(guarda)** Contra o descritor do inbox com `scp:buteco-connectors`: o caso do escopo de outro app reprova.
  - **Feito:** casos: `ClientCredentials_IssuesAnApiOnlyToken_ForTheServiceClient` (2), `AnotherAppScope_IsRefused_WithoutToken`, `WrongSecret_IsInvalidClient`, com o apoio `Support/ServiceClientTokens.cs`. **Premissa falsa, parada e decisão do dono (02/11/2026):** o escopo de outro app sai `invalid_request` com a descrição ID2051, e não `invalid_scope` (`OpenIddictServerHandlers.Exchange.cs` l. 1392–1394, tag 7.7.2). Spec, design (D1, "Correção na implementação") e teste corrigidos: o teste afirma o código do erro e a ausência de `access_token`, e não o texto, e foi renomeado de `AnotherAppScope_IsInvalidScope`. Guarda visto reprovar: com `scp:buteco-connectors` no descritor de serviço, `AnotherAppScope_IsRefused_WithoutToken` reprovou (1/4). Voltou: `IdentityClients.cs`, sha igual.

- [x] 3.4 (apps/api) **Isolamento por item na sincronização** (decisão do dono, 02/11/2026, na parada da 3.3). Cada escopo e cada cliente com o próprio `try/catch` (convenção 4): savepoint por item, change tracker limpo depois da falha, log `Error` `IdentityClientCatalogItemFailed` (1195) com o item e sem o segredo. O ciclo termina com falha se algum item falhou, depois de gravar os outros, para o aquecimento repetir (design D2, "Isolamento por item"). Caso: `AServiceClientThatFailsToSync_DoesNotBlockThePanelCorrection_InTheSameCycle`. **(guarda)** Contra três defeitos, um de cada vez: sem o `try/catch` por item; sem a falha no fim do ciclo; sem limpar o change tracker.
  - **Feito:** o `UpdateAsync` do cliente de serviço passou a vir antes da conferência do segredo: com divergência, a correção já regrava o segredo; sem ela, o segredo é conferido. Guardas vistos reprovar, cada um só no caso do isolamento (1/7) e com o arquivo de volta e sha igual: (a) sem o `try/catch` por item; (b) sem a falha no fim do ciclo; (c) sem `ChangeTracker.Clear()`. A primeira versão do caso não pegava o (c): passou 7/7 sem a limpeza. O caso passou a adulterar também o connectors, corrigido depois do inbox no mesmo ciclo, e a afirmar que o inbox continua emitindo token. Assim o (c) reprova. Ganhou também um `finally` que ressincroniza, para um caso que falhe não deixar clientes adulterados para os outros da classe. **Achado ao implementar:** com o isolamento, um ciclo com todos os itens falhando, como antes da migração, terminaria sem erro e contaria como concluído no aquecimento. Daí a falha no fim do ciclo, registrada no design.

## 4. Tabelas de clientes (apps/api, apps/connectors)

- [x] 4.1 (apps/api) `Identity/Validation/IssuerClientSubjects.cs`: `buteco-servico-inbox` → `service:inbox` e `buteco-servico-connectors` → `service:connectors`. O comentário passa a descrever as três entradas (D3).
- [x] 4.2 (apps/api) `IssuerPrincipalMappingTests`: os dois clientes de serviço mapeiam para os subjects deles, e o cliente fora da tabela continua sem `NameIdentifier`. **(guarda)** Contra a entrada do inbox retirada da tabela.
  - **Feito:** caso `AServiceClient_MapsToItsServiceSubject` (2), com `sub` = `client_id`, como no `client_credentials`. Guarda visto reprovar: com a entrada do inbox retirada da tabela, o caso do inbox reprovou (1/5). Voltou: `IssuerClientSubjects.cs`, sha igual.
- [x] 4.3 (apps/api) `ServiceClientRouteTests`, integração, com tokens do `/connect/token` real:
  - inbox: `GET /agents/{id}` de agente semeado → `200`; `GET /agents` → `403`; `GET /sync/knowledge-bases/` → `403`;
  - connectors: `GET /sync/knowledge-bases/` → `200`; `GET /agents` → `403`.

  **(guarda)** Contra o `MapPrincipal` lendo o subject do `sub` em vez do `client_id`: o `sub` é o `client_id` e não está na tabela de subjects, então os casos `200` precisam reprovar com `403`.
  - **Feito:** casos `TheInboxClient_ReadsAnAgent_AndIsForbiddenOutsideItsRoutes` e `TheConnectorsClient_ListsTheSyncedBases_AndIsForbiddenOutsideItsRoutes`. O agente é criado pelo cliente padrão da fixture. Guarda visto reprovar: com o `MapPrincipal` pondo o `sub` como `NameIdentifier`, os 2 casos reprovaram (2/2). Voltou: `IssuerTokenAuthentication.cs`, sha igual. A primeira tentativa do guarda usou uma substituição confusa: também reprovou 2/2, mas não fica como registro.
- [x] 4.4 (apps/connectors) `Auth/IssuerClientSubjects.cs`: `buteco-servico-api` → `service:api` (constante `ApiServiceClientId`, com o mesmo texto do `apps/api` na #235). O comentário perde o "os clientes de serviço (service:api, #182) entram aqui" e passa a descrever as duas entradas.
- [x] 4.5 (apps/connectors) `IssuerTokenValidationTests`, com o `TestIssuer`:
  - `client_id` `buteco-servico-api` e `aud` `buteco-connectors` → "descrever pasta" sem `401` nem `403`; `GET /connectors/providers` → `403`;
  - o mesmo cliente com `aud` `buteco-api` → `401`.

  **(guarda)** Contra a entrada retirada da tabela: o primeiro caso reprova com `403`.
  - **Feito:** casos `ApiServiceClient_OpensDescribeFolder_AndIsForbiddenOnTheOperatorRoutes` (descrever pasta no provedor `fake` da suíte, sem `401` nem `403`; `GET /connectors/providers` e `…/folders` → `403`) e `ApiServiceClient_WithoutTheConnectorsAudience_IsUnauthorized`. Guarda visto reprovar: com a entrada retirada da tabela, o primeiro caso reprovou (1/20). Voltou: `apps/connectors/src/Buteco.Connectors/Auth/IssuerClientSubjects.cs`, sha igual.
- [x] 4.6 (apps/connectors) Conferir que `ValidateIssuerClientSubjects` passa com a entrada nova, e que o `IssuerTokenStartupValidationTests` existente continua verde, sem mudança.
  - **Feito:** `IssuerTokenValidationTests` e `IssuerTokenStartupValidationTests` juntos: 29/29 verdes, e o `IssuerTokenStartupValidationTests` sem mudança.

## 5. tests/ (acordo)

- [x] 5.1 (tests/) As duas `RoundTripFixture` (`InboxOrchestratorRoundTrip.Tests`, `ApiConnectorsRoundTrip.Tests`) passam a configurar os dois segredos no `apps/api` real, porque o boot passa a exigi-los. Nenhum caso novo, nenhuma asserção mudada.
  - **Feito:** **premissa corrigida (só documentação):** o "porque o boot passa a exigi-los" não vale. As fixtures sobem o `apps/api` em `Development` e herdariam os segredos do `appsettings.Development.json`. Os segredos ficam fixos nas fixtures mesmo assim, como a `Identity:SigningKeyEncryptionKey` ao lado (#208 D9, correção): o teste não depende do valor de desenvolvimento. Os mesmos dois valores nas duas fixtures, gerados com `openssl rand -base64 32`.
- [x] 5.2 (tests/) Rodar `rt-inbox`, `rt-connectors` e `rt-crossapp`. Os 12, os 8 e os 4 casos da baseline continuam, com os mesmos nomes.
  - **Feito:** rodada direta (`dotnet test`, fora do `suite-234.sh`), para conferir antes do fechamento: `rt-inbox` 12/12, `rt-connectors` 8/8 e `rt-crossapp` 4/4. A rodada que conta é a da 7.1.

## 6. Deploy e documentação (deploy, docs)

- [x] 6.1 (deploy) `docker-compose.prod.yml`, serviço `api`: `Identity__ServiceClients__Inbox__Secret: ${SERVICE_CLIENT_SECRET_INBOX:?…}` e o do connectors. Nada no `inbox` nem no `connectors` (#235).
- [x] 6.2 (deploy) `.env.prod.example`: as duas variáveis, com `openssl rand -base64 32`, um por serviço, distintas entre si e de todas as outras chaves.
- [x] 6.3 (deploy) Conferência do compose, com a saída registrada nesta tarefa:
  - `docker compose -f docker-compose.prod.yml --env-file <arquivo de teste> config` (ou o equivalente pelo Podman) **sem** `SERVICE_CLIENT_SECRET_INBOX` falha citando a variável;
  - com as duas, o `api` recebe as duas `Identity__ServiceClients__*`.
  - **Feito:** com `podman-compose` (sem `docker` nesta máquina), sobre uma cópia do `.env.prod.example` com valores de teste. Sem `SERVICE_CLIENT_SECRET_INBOX`: `ValueError: required variable SERVICE_CLIENT_SECRET_INBOX is missing a value: defina SERVICE_CLIENT_SECRET_INBOX (openssl rand -base64 32, distinto das outras chaves) — use --env-file .env.prod`, saída 1. Com as duas: `Identity__ServiceClients__Connectors__Secret` e `Identity__ServiceClients__Inbox__Secret` aparecem uma vez cada, no serviço `api`.
- [x] 6.4 (docs) `docs/configuration.md`: as duas chaves do `apps/api`, obrigatórias, com a regra de distinção, e as duas variáveis do `.env.prod`.
  - **Feito:** a linha da tabela do `apps/api` e as duas da tabela do `.env.prod`.
- [x] 6.5 (docs) `docs/deployment.md`: os segredos novos no passo de preparação. O rollback deste passo loga `Error` de `buteco-servico-*` não declarado, e isso é esperado. Os segredos ficam no `.env.prod` para a #235.
  - **Feito:** seção nova "Clientes de serviço do emissor (#234)", com geração, regras, troca, ordem, conferência, falha de item e rollback, e as duas linhas na tabela de variáveis por processo.

## 7. Fechamento

- [x] 7.1 (apps/api, apps/connectors, apps/inbox, tests/) Suítes da change pelo `suite-234.sh`: `api`, `connectors`, `inbox`, `rt-inbox`, `rt-connectors` e `rt-crossapp`. Comparação por nome contra a baseline:
  - nenhum caso some, além da troca de nome declarada em 3.2;
  - os casos novos são listados;
  - rodada `INCONCLUSIVA` se repete;
  - **normalizar o GUID antes de comparar:** cinco casos de `Buteco.Connectors.Tests.SubjectAuthorizationTests.SubjectXRota`, os de `POST /connectors/knowledge-bases/{guid}/sync`, levam um GUID novo no nome a cada rodada. Medido entre as baselines da #182 e da #234, no mesmo commit: `7620166c-…` contra `bc220dab-…`. Comparados sem normalizar, eles aparecem como cinco casos que somem e cinco que surgem.
  - **Feito:** rodada única, todas `APROVADA` (`234-verificacao/r1/`): `apps/api` 1.271 + 1 ignorado, com CPU mínima de 60, verde e válida; `apps/connectors` 317 + 1; `apps/inbox` 302; `rt-inbox` 12; `rt-connectors` 8; `rt-crossapp` 4. Por nome contra a baseline, com o GUID normalizado e comparado como multiconjunto: `apps/api` 1 ausente (a troca de nome declarada na 3.2), 28 novos, 0 mudados; `apps/connectors` 0 ausentes, 2 novos; os outros, iguais. A primeira versão do normalizador não casava o GUID truncado (`…-9a0"···`) e mostrou os 5 casos como ausentes e novos; corrigido. Os dois nomes repetidos do `GoogleServiceAccountKeyTests` (truncamento) são iguais na baseline e na change.
- [x] 7.2 (todos) Medição da convenção 18: produção, testes e duplos medidos contra a projeção do design ("Tamanho"), cada coluna com a própria comparação.
  - **Feito:** tabela no `02`: produção +315/−39 contra ~170 (+85%, o isolamento e o fim com falha, que a projeção não tinha); testes +568/−6 contra ~670 (−15%); duplos e suporte +79 contra 0 (o `ServiceClientTokens`, sem item próprio na projeção).
- [x] 7.3 (docs) `02-HISTORICO_E_STATUS.md`:
  - entrada da change;
  - **a fila nova da M1**, com 15 itens: #234, #235, #236, #182, #211, #183 e #220 depois da #181, e o link do comentário da #182 com o design de referência;
  - a #184 sem o critério (b).

  `CHANGELOG.md`: a entrada. As duas **antes** do archive.
  - **Feito:** entrada da change no fim do `02`, e a fila da M1 com 15 itens e a nota da divisão (#234, #235, #236, #182, e a #184 sem o critério (b)). `CHANGELOG`: em Added, os clientes de serviço; em Changed, o isolamento do catálogo.
- [x] 7.4 (docs) `01-ARQUITETURA_E_CONVENCOES.md`, seção Autenticação: os clientes de serviço do catálogo e as entradas novas das tabelas de clientes. O HMAC continua descrito como em uso até a #182.
  - **Feito:** em "Emissor OIDC", "Clientes e escopos" (os três clientes, o segredo e o isolamento); em "O `apps/api` aceita o próprio token" e em "Validação do token do emissor", as entradas novas das tabelas.
- [x] 7.5 (openspec) `openspec validate clientes-servico-emissor --strict` e o archive com sync das specs. Commit, push e PR só com o pedido do dono.
  - **Feito:** `openspec validate --strict` válido e `scripts/check-docs.py` OK; archive com sync das quatro specs. Commit, push e PR esperam o pedido do dono.
