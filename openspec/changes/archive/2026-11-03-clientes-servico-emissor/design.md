## Context

**Estado atual (`14d4ba9`, merge do PR #233, 02/11/2026).**

- O `IdentityClientCatalog` do `apps/api` mantém um único cliente, o `buteco-painel` (público, authorization code + PKCE), e os três escopos `buteco-api`, `buteco-inbox` e `buteco-connectors`. Roda no aquecimento e a cada tique da rotação, sob o lock `BUTECOC1` (#180 D3.1, D3.13).
- O `client_credentials` está habilitado desde a #179. O `ClientCredentialsTokenHandler` monta `sub = client_id` e `aud` = recursos dos escopos. Em produção, nenhum cliente o usa.
- Os três apps tiram o subject do token do emissor pelo `client_id`, numa tabela fechada (`IssuerClientSubjects`), hoje só com `buteco-painel` → `operator`.
- Os tokens de serviço ainda são HMAC (inventário S1–S4 do design de referência).

**O desenho completo** é o design da #182 (change `remover-hmac-token-servico`), preservado [num comentário da #182](https://github.com/aldovrando-oliveira/buteco-agentes/issues/182#issuecomment-6593073248), sobre a base `14d4ba9`. Aqui ele é o "design de referência". A divisão em quatro passos e as decisões Q1–Q6 são do dono, de 02/11/2026 ("Decisões do dono", lá). Esta change implementa:

- **D1:** os clientes, com tipo, grant e escopo;
- **D2:** a credencial por segredo, como ele chega ao `apps/api`, a conferência e o boot;
- **D5:** as tabelas de clientes;
- **o passo A de "Tamanho e divisão":** só os receptores, sem trocar nenhum emissor.

Os achados H1–H6 da fonte do OpenIddict 7.7.2 valem aqui e não são repetidos.

### Achados novos da fonte (OpenIddict 7.7.2, tag `950cb13`, lidos em 02/11/2026)

| # | fato | onde |
|---|---|---|
| H7 | `UpdateAsync(application, descriptor)` guarda o segredo atual, aplica o descritor e compara. Segredo diferente vai ao overload que faz o hash. **Descritor sem segredo apaga o segredo** (`SetClientSecretAsync(application, null)`). | `OpenIddict.Core/Managers/OpenIddictApplicationManager.cs` l. 1178–1195, 1205–1222 |
| H8 | `ValidateClientSecretAsync(application, secret)` devolve `false`, com log `Information` do OpenIddict (6160/6161), para cliente sem segredo ou com segredo que não confere. Não lança. | l. 1363–1390 |

O H7 fixa uma regra da sincronização (D2): **toda correção de um cliente de serviço leva o segredo configurado no descritor**. Corrigir uma permissão com o descritor sem segredo deixaria o cliente sem credencial.

**Correção na implementação (02/11/2026, tarefa 3.2; convenção 9).** A consequência acima é falsa para cliente confidencial. Medido no guarda da 3.2: com o descritor sem segredo, o `UpdateAsync` lança `OpenIddictExceptions.ValidationException`, porque a validação do manager recusa cliente confidencial sem segredo nem JWKS (`ID2113`, `OpenIddictApplicationManager.cs` l. 1290–1300, o H2 do design de referência). O apagamento do H7 só vale para cliente público. A transação da sincronização volta inteira, e a do `buteco-painel` falha junto, a cada tique. A regra não muda: o segredo vai sempre no descritor. O guarda continua reprovando, só que pela exceção, em todos os casos da classe.

## Goals / Non-Goals

**Goals:**

- Um token `client_credentials` pedido com o segredo de `buteco-servico-inbox` é aceito nas duas rotas do `service:inbox` e recebe `403` nas do operador. O mesmo vale para `buteco-servico-connectors` nas cinco rotas de `/sync`.
- Um token do emissor com `client_id` `buteco-servico-api` e `aud` `buteco-connectors` é aceito na rota "descrever pasta" do `apps/connectors` e recebe `403` nas do operador.
- Os segredos são obrigatórios e conferidos no boot do `apps/api`, sem ecoar valor. A troca de segredo no `.env` vale na próxima sincronização, sem apagar nada à mão.
- O `main` continua implantável em qualquer ordem: depois do merge, todo fluxo funciona exatamente como antes.

**Non-Goals:**

- **Nenhum emissor troca de token** (#235): o `ServiceTokenDelegatingHandler` dos três continua HMAC. O `ServiceAccessTokenSource` e o `ServiceAccessTokenIssuer` são da #235.
- **Nenhuma fixture troca de token** (#236).
- **Nada sai** (#182): o HMAC, o seletor e a `Auth:TokenSigningKey` continuam.
- **O segredo nos contêineres do `inbox` e do `connectors`**: entra com a #235, junto de quem o usa.

## Decisions

### D1. Os descritores dos clientes de serviço (referência D1)

Em `IdentityClients`, ao lado do `Panel(...)`, um descritor por serviço, fixo no código:

| campo | `buteco-servico-inbox` | `buteco-servico-connectors` |
|---|---|---|
| `ClientType` | `confidential` | `confidential` |
| `ClientSecret` | `Identity:ServiceClients:Inbox:Secret` | `Identity:ServiceClients:Connectors:Secret` |
| `ConsentType` | `implicit` | `implicit` |
| permissões | `ept:token`, `gt:client_credentials`, `scp:buteco-api` | as mesmas |
| `redirect_uris`, `post_logout_redirect_uris`, requisitos | nenhum | nenhum |
| `DisplayName` | "Serviço apps/inbox" | "Serviço apps/connectors" |

- **Nenhum outro escopo.** Um pedido com `scope=buteco-connectors` é recusado antes de existir token. Com isso, a credencial roubada do inbox não alcança o `apps/connectors` nem o `apps/inbox`.

  **Correção na implementação (02/11/2026, tarefa 3.3; convenção 9; decisão do dono).** A primeira versão dizia que a recusa era `invalid_scope`. Medido no teste e conferido na fonte, é **`invalid_request`**, com a descrição `ID2051` ("This client application is not allowed to use the specified scope."). Ela sai em `ValidateScopePermissions`, `OpenIddict.Server/OpenIddictServerHandlers.Exchange.cs` l. 1392–1394 (tag 7.7.2). O `invalid_scope` fica para escopo que não existe. Nada se acrescenta para trocar o código: a spec e o teste esperam `invalid_request`, e o teste afirma o código e a ausência de `access_token`, e não o texto da descrição.
- **`buteco-servico-api` não entra no catálogo.** O token dele é gerado no processo (referência D4, na #235) e nunca passa pelo `/connect/token`.
- **Constantes:** `IdentityClients.InboxServiceClientId` e `ConnectorsServiceClientId`. O `ReservedPrefix` não muda, e o laço de "`buteco-*` não declarado" passa a pular os três declarados.

### D2. O segredo: configuração, sincronização e boot (referência D2)

**Configuração.** `IdentityIssuerOptions` ganha `ServiceClients`, com `Inbox` e `Connectors`, cada um com `Secret`. Fica na mesma seção `Identity`, como a `SigningKeyEncryptionKey`. Variáveis: `Identity__ServiceClients__Inbox__Secret` e `Identity__ServiceClients__Connectors__Secret`.

**Sincronização**, dentro do `SyncAsync` existente, na mesma transação e sob o mesmo lock `BUTECOC1`, para cada cliente de serviço:

1. **Ausente:** `CreateAsync(descritor com o segredo)`. O OpenIddict guarda o hash (H3).
2. **Presente:** `PopulateAsync` num descritor e comparação de tudo **menos** o segredo. Os campos são os mesmos do `DivergesAsync` do painel: tipo, consentimento, nome, tipo de aplicação, JWKS, `Settings`, permissões, requisitos e URIs. Depois, `ValidateClientSecretAsync(cliente, segredo configurado)` (H8).
3. **Divergiu em qualquer um dos dois:** `UpdateAsync(cliente, descritor com o segredo)`. Pelo H7, o segredo vai **sempre** no descritor. Log `Information` com `SyncedEvent`: "cliente X corrigido" ou "segredo do cliente X trocado", nunca o valor.
4. **Custo:** dois PBKDF2 de 10.000 iterações por sincronização (H3). A sincronização roda no aquecimento e a cada hora.

**Isolamento por item (decisão do dono, 02/11/2026, na parada da 3.3).** Antes, um descritor que o OpenIddict recusasse (`ValidationException`) voltava a transação inteira, e o `buteco-painel` deixava de ser corrigido junto. Agora cada escopo e cada cliente é sincronizado com o próprio `try/catch` (convenção 4):

- **savepoint por item.** A falha desfaz só o item, e a transação continua válida no Postgres para os outros;
- **change tracker limpo depois da falha.** O OpenIddict aplica o descritor na entidade rastreada antes de validar. Sem a limpeza, o `SaveChanges` do item seguinte gravaria o item recusado, por exemplo um cliente de serviço sem segredo. Medido no guarda: sem o `ChangeTracker.Clear()`, o inbox recusado foi gravado junto com a correção do connectors e perdeu a credencial;
- **log `Error`** (`IdentityClientCatalogItemFailed`, 1195) com o item (`cliente buteco-servico-inbox`, `escopo buteco-api`), nunca o segredo;
- **o ciclo termina com falha se algum item falhou.** Os que deram certo já estão gravados, e o `SyncAsync` lança no fim. Sem isso, um catálogo que falhasse item a item, como antes da migração, contaria como concluído no aquecimento (#180 D3.13), e só tentaria de novo no tique de uma hora. Medido ao implementar: as fixtures sobem o host antes de migrar, e os seis itens falham no primeiro ciclo.

**A troca de segredo** é, assim, uma "divergência corrigida": muda o `.env.prod`, o `apps/api` sobe, e o aquecimento regrava o hash. Entre o start e a primeira sincronização, o segredo antigo continua valendo. Sem emissor usando o cliente (até a #235), isso não alcança ninguém.

**Boot**, em `ValidateIdentityIssuerConfiguration`, depois do `Build()` e sem banco (convenção 8, forma de configuração). Cada segredo precisa:

- estar presente;
- ser base64 válido;
- ter pelo menos **32 bytes** decodificado;
- ser **distinto do outro**;
- ser **distinto das três chaves de cifra do `apps/api`**: `Identity:SigningKeyEncryptionKey`, `Identity:TwoFactorEncryptionKey` e `Mcp:CredentialEncryptionKey` (decisão do dono, 02/11/2026).

A comparação de igualdade é **por bytes decodificados**, no molde da #218 com a chave das 2 etapas (`IdentityIssuerStartupValidation.cs`, `TwoFactorSecretCipher.ParseKey` contra a de assinatura e `TryDecodeBase64` contra a do MCP). A do MCP só entra se estiver configurada e decodificar, como lá.

O segredo é gerado como as outras chaves da casa (`openssl rand -base64 32`). Dois valores iguais desfariam a separação sem erro nenhum:

- **entre os dois segredos:** quem tivesse um teria a credencial dos dois serviços;
- **entre um segredo e uma chave de cifra:** o segredo, que vive também no contêiner do serviço (#235), seria a chave que decifra as chaves do emissor, os segredos do TOTP ou as credenciais de MCP.

**Mensagens.** A ausência e o formato nomeiam a chave (`Identity:ServiceClients:Inbox:Secret`). A igualdade nomeia **as duas chaves iguais**, no formato da #218: "`Identity:ServiceClients:Inbox:Secret` é igual a `Identity:TwoFactorEncryptionKey`: …". Nenhuma ecoa o valor. Os segredos são obrigatórios mesmo sem `Connectors:BaseUrl` (Q6).

**Por que não reaproveitar o `ParseKey` das chaves de cifra:** o segredo não é chave de cifra. Ele vai ao OpenIddict como texto, e o serviço o manda como texto. Exigir 32 bytes em base64 é só a régua de entropia. Fica um `ServiceClientSecret.Validate(nome, valor)` próprio, pequeno.

### D3. As tabelas de clientes (referência D5)

| app | entrada nova | subject | rotas desse subject (não mudam) |
|---|---|---|---|
| `apps/api` | `buteco-servico-inbox` | `service:inbox` | `POST /agents/{id}/a2a`, `GET /agents/{id:guid}` |
| `apps/api` | `buteco-servico-connectors` | `service:connectors` | as cinco de `/sync/knowledge-bases` |
| `apps/connectors` | `buteco-servico-api` | `service:api` | `GET /connectors/providers/{providerKey}/folder` |

- **`ValidateIssuerClientSubjects` não muda e passa a cobrir as entradas.** Ela confere que todo subject de destino existe na tabela de subjects do app. O `service:inbox` e o `service:connectors` são chaves de `ServiceScopeAuthorizationHandler.ServiceRoutes`, e o `service:api` é chave de `ConnectorsSubjectAuthorizationHandler.SubjectRoutes`.
- **O `apps/inbox` não muda:** nenhum serviço chama rota autenticada dele.
- **Os comentários das três tabelas** deixam de dizer "os clientes de serviço entram aqui (#182)" e passam a descrever o que está nelas.

### D4. Deploy e desenvolvimento

- **`.env.prod.example`:** `SERVICE_CLIENT_SECRET_INBOX` e `SERVICE_CLIENT_SECRET_CONNECTORS`, com a instrução `openssl rand -base64 32`, um por serviço, distintos de todas as outras chaves.
- **`docker-compose.prod.yml`, serviço `api`:** `Identity__ServiceClients__Inbox__Secret: ${SERVICE_CLIENT_SECRET_INBOX:?…}` e a do connectors. O `inbox` e o `connectors` só os recebem na #235 (Non-Goals).
- **`appsettings.Development.json` do `apps/api`:** os dois segredos de desenvolvimento, marcados como de desenvolvimento, como a `Identity:SigningKeyEncryptionKey`. O `dotnet run` continua subindo sem nada novo.
- **Ordem:** qualquer uma. O `api` novo sobe, o catálogo cria os dois clientes, e ninguém pede token por eles ainda. O `connectors` novo tem uma entrada a mais na tabela, que nenhum token usa ainda.
- **Rollback:** voltar a imagem. A versão anterior ignora a configuração nova, e os dois clientes ficam no banco sem efeito. Na sincronização, ela os loga como `buteco-*` não declarados (`Error`), sem mexer neles. Esse log no rollback é esperado e fica no runbook.

### D5. Testes

Os guardas são verificados contra o defeito entre parênteses (convenção 15): o defeito é reintroduzido, o teste reprova, e a árvore volta.

**`apps/api`, unidade:**

| teste | o que afirma | guarda verificado contra |
|---|---|---|
| `IdentityClientCatalogTests` (estendido) | cada descritor de serviço é confidencial, tem só `ept:token`, `gt:client_credentials` e `scp:buteco-api`, e nenhuma URI | descritor com `scp:buteco-connectors` |
| `ServiceClientSecretTests` | ausente, fora de base64, menos de 32 bytes → recusado com o nome da chave e **sem o valor** na mensagem | mensagem que interpola o valor |
| `IssuerPrincipalMappingTests` (estendido) | `buteco-servico-inbox` → `service:inbox`, `buteco-servico-connectors` → `service:connectors`; cliente fora da tabela continua sem `NameIdentifier` | entrada removida da tabela |

**`apps/api`, integração (Testcontainers, `TestServer`):**

| teste | o que afirma | guarda verificado contra |
|---|---|---|
| `IdentityClientCatalogSyncTests` (estendido) | o aquecimento cria o painel, os **dois clientes de serviço** e os três escopos; um cliente de serviço adulterado (escopo a mais, grant a mais) volta ao descritor **e o segredo continua validando**; o segredo trocado na configuração → o antigo recebe `invalid_client` e o novo emite token, depois da sincronização; **um cliente de serviço cujo descritor o OpenIddict recusa não impede, no mesmo ciclo, a correção do `buteco-painel` e do outro cliente de serviço, nem é gravado sem credencial, e o ciclo termina com falha** (isolamento, D2) | correção com o descritor sem segredo (H7); sem o `try/catch` por item; sem a falha no fim do ciclo; sem limpar o change tracker |
| `ServiceClientTokenTests` (novo) | `client_credentials` com cada segredo → token com `aud` `buteco-api` só, `client_id` do cliente, `typ` `at+jwt`; `scope=buteco-connectors` → `invalid_request`, sem token (correção em D1); segredo errado → `invalid_client` | permissão de escopo `buteco-connectors` no descritor |
| `ServiceClientRouteTests` (novo) | o token do `buteco-servico-inbox` → `GET /agents/{id}` `200` e `GET /agents` `403`; o do `buteco-servico-connectors` → `GET /sync/knowledge-bases/` `200`, `GET /agents` `403`; o do inbox em `/sync` → `403` | `IssuerClientSubjects` mapeando pelo `sub` |
| `IdentityIssuerStartupValidationTests` (estendido) | segredo ausente, fora de base64 ou curto → boot falha nomeando a chave; os dois segredos iguais → boot falha nomeando os dois; cada segredo igual a `Identity:SigningKeyEncryptionKey`, a `Identity:TwoFactorEncryptionKey` ou a `Mcp:CredentialEncryptionKey` → boot falha nomeando as duas chaves; nenhuma mensagem contém o valor; composição real com `Program.cs` | remoção da conferência do `ValidateIdentityIssuerConfiguration`; conferência que compara só os dois segredos entre si (o caso das chaves de cifra reprova); mensagem que interpola o valor |

**`apps/connectors`:**

| teste | o que afirma | guarda verificado contra |
|---|---|---|
| `IssuerTokenValidationTests` (estendido) | token do `TestIssuer` com `client_id` `buteco-servico-api` e `aud` `buteco-connectors` → "descrever pasta" não recebe `403`; as três rotas do operador → `403`; o mesmo cliente com `aud` `buteco-api` → `401` | entrada removida da tabela |
| `IssuerTokenStartupValidationTests` | inalterado, e passa com a entrada nova | — |

**`tests/`:** as duas `RoundTripFixture` passam a configurar os dois segredos no `apps/api` real, porque o boot passa a exigi-los. Os 12 casos do `rt-inbox`, os 8 do `rt-connectors` e os 4 do `rt-crossapp` continuam, comparados por nome. O acordo dos fluxos de serviço de ponta a ponta é da #235, quando existir quem emita.

**Suporte de teste:** o `TestAuthentication.ConfigOverrides` do `apps/api` ganha os dois segredos de teste, distintos dos de desenvolvimento.

**Mudança de asserção existente, declarada:** o `IdentityClientCatalogSyncTests.TheWarmupCreates_OnlyThePanelClient_AndTheThreeScopes` afirma "só o painel", e passa a afirmar "o painel e os dois de serviço", com nome novo. É a mudança de requisito da `oidc-issuer` desta change, e não premissa falsa.

**Frontend e workers:** sem mudança de código, sem teste novo.

## Árvore de pastas proposta

```
apps/api/src/Buteco.Api/
├── Identity/
│   ├── Clients/
│   │   ├── IdentityClients.cs                  + InboxServiceClientId, ConnectorsServiceClientId, Service(...) (D1)
│   │   ├── IdentityClientCatalog.cs            + sincronização dos dois, segredo conferido e sempre no descritor (D2)
│   │   └── ServiceClientSecret.cs              NOVO: presença, base64, 32 bytes, sem ecoar valor (D2)
│   ├── IdentityIssuerOptions.cs                + ServiceClients { Inbox, Connectors } { Secret } (D2)
│   ├── IdentityIssuerStartupValidation.cs      + segredos obrigatórios, distintos entre si e das três chaves de cifra (D2)
│   └── Validation/IssuerClientSubjects.cs      + dois clientes de serviço (D3)
└── appsettings.Development.json                + Identity:ServiceClients de desenvolvimento (D4)

apps/api/tests/Buteco.Api.Tests/
├── Support/TestAuthentication.cs               + os dois segredos de teste
└── Identity/
    ├── IdentityClientCatalogTests.cs           estendido
    ├── IdentityClientCatalogSyncTests.cs       estendido (asserção "só o painel" muda, declarada)
    ├── IdentityIssuerStartupValidationTests.cs estendido
    ├── IssuerPrincipalMappingTests.cs          estendido
    ├── ServiceClientSecretTests.cs             NOVO
    ├── ServiceClientTokenTests.cs              NOVO
    └── ServiceClientRouteTests.cs              NOVO

apps/connectors/src/Buteco.Connectors/Auth/IssuerClientSubjects.cs      + buteco-servico-api → service:api (D3)
apps/connectors/tests/Buteco.Connectors.Tests/IssuerTokenValidationTests.cs   estendido

tests/InboxOrchestratorRoundTrip.Tests/Support/RoundTripFixture.cs     + os dois segredos no apps/api
tests/ApiConnectorsRoundTrip.Tests/Support/RoundTripFixture.cs         + os dois segredos no apps/api

docker-compose.prod.yml, .env.prod.example                              (D4)
docs/configuration.md, docs/deployment.md, 02, CHANGELOG.md

```

`apps/inbox`, `apps/workers` e `apps/frontend` não mudam. Nada vai para `libs/`: a mudança é uma tabela em cada app e descritores num app só. Nenhum pacote novo.

## Tamanho

**Âncora decomposta** (convenção 18): o PR #216 (#209), com 273 linhas de produção em 6 arquivos e 979 de teste em 10, é a mudança mais próxima em forma (handler e política do emissor, mais testes de integração do OpenIddict).

| componente | produção | testes | duplos |
|---|---|---|---|
| descritores, constantes e opções | ~60 | ~60 (unidade) | — |
| sincronização dos clientes de serviço, com segredo | ~60 | ~150 | — |
| `ServiceClientSecret` e boot | ~45 | ~120 | — |
| tabelas de clientes (api +2, connectors +1) | ~6 | ~120 | — |
| token e rotas dos clientes de serviço | — | ~200 | — |
| fixtures (`TestAuthentication`, duas `RoundTripFixture`) | — | ~20 | — |
| **total** | **~170** | **~670** | **0** (nenhum duplo novo: o `TestIssuer` do connectors e o `IdentityIssuerTestClient` do api já existem) |

Cabe numa change revisável, menor que a #209. É rascunho de projeção, e o fechamento compara as três colunas.

## Risks / Trade-offs

- **[Correção do cliente com o descritor sem segredo apaga a credencial]** (H7) → o segredo vai sempre no descritor, com guarda verificado contra a correção sem ele.
- **[Segredo trocado só vale depois da sincronização]** → no aquecimento, segundos depois do start. Antes da #235, ninguém usa o cliente.
- **[Rollback loga `Error` de `buteco-*` não declarado]** → é o comportamento desenhado da #180 para cliente que o código não conhece. Fica no runbook como esperado no rollback deste passo.
- **[Os testes que contam clientes mudam]** → a asserção "só o painel" muda com nome novo e motivo declarado (D5).
- **[Segredo em configuração do `apps/api`]** → o `apps/api` já guarda a chave que decifra as chaves de assinatura. O segredo de serviço não aumenta o que um processo do `apps/api` comprometido entrega (referência D2).

## Migration Plan

1. Gerar os dois segredos (`openssl rand -base64 32`, um por serviço) e pôr no `.env.prod`.
2. Subir o `api` e o `connectors`, em qualquer ordem. O `migrator` não muda, porque não há migration: os clientes são dados sincronizados.
3. **Conferência:**
   - o log do catálogo cita "cliente buteco-servico-inbox criado" e "cliente buteco-servico-connectors criado";
   - um `POST /connect/token` com o segredo do inbox, de dentro da rede do compose, devolve token;
   - o painel e os três fluxos de serviço funcionam como antes.
4. **Rollback:** voltar as imagens. Os segredos podem ficar no `.env.prod`, porque a #235 os usa.

## Open Questions

Nenhuma de produto. As decisões que valem para este passo (nomes, forma da credencial, obrigatoriedade dos segredos) são do dono, de 02/11/2026, e estão no design de referência ([comentário da #182](https://github.com/aldovrando-oliveira/buteco-agentes/issues/182#issuecomment-6593073248)), "Decisões do dono". Duas decisões técnicas daqui merecem atenção na revisão, por serem regras novas de configuração:

- **os segredos precisam ser distintos entre si e das três chaves de cifra do `apps/api`** (decisão do dono, 02/11/2026);
- **o piso é de 32 bytes em base64** (D2).
