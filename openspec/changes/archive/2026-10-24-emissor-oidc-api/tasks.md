**Base:** `origin/main` em `c29246c` (merge do PR #207), 24/10/2026. Worktree `../buteco-agentes-179`, branch `feat/179-emissor-oidc-api`. Baseline em worktree limpo separado, `../buteco-agentes-179-baseline`, no mesmo commit, sem edição nenhuma.

Saídas completas de baseline e de fechamento ficam em `~/.cache/buteco-agentes/179-baseline/` e `~/.cache/buteco-agentes/179-fechamento/`: `.log` inteiro, `.trx` e estado da máquina colado ao número, nunca filtrado (convenção 19). Variáveis em toda rodada: `DOCKER_HOST` do Podman, `TESTCONTAINERS_RYUK_DISABLED=true`, `DOTNET_SYSTEM_NET_DISABLEIPV6=1` e `TZ=America/Sao_Paulo`.

## 1. Baseline e dependências, antes de tocar código

- [x] 1.1 (`apps/api`, `apps/workers`, `apps/inbox`) Baseline das três suítes de backend no worktree limpo em `c29246c`. São as três que esta change toca: o `apps/api` diretamente, os outros dois pela EF Core central. Registrar aqui o número de cada uma, com load, `CPU_Speed_Limit`, relógio da VM e `podman ps` na mesma frase. Toda falha é investigada antes de ser chamada de pré-existente.

      **Resultado, 24/10/2026, worktree `../buteco-agentes-179-baseline` em `c29246c`, sem edição nenhuma.** Ambiente: VM `podman-machine-default` (applehv, 6 CPUs, 6 GiB) recém-subida, relógio acertado (Mac 03:07:06 UTC, VM 03:07:07); `CPU_Speed_Limit = 100`; **nenhum contêiner** em `podman ps` antes de cada suíte; 12 núcleos. As três rodaram em sequência, cada uma só depois de o load de 1 min cair abaixo de 8.

      | suíte | resultado | parede (vstest) | load na largada → no fim |
      |---|---|---|---|
      | `apps/api` | **800/800** | 1m53s | 7,59 → 17,13 |
      | `apps/workers` | **415/415** | 6m13s | 7,51 → 4,79 |
      | `apps/inbox` | **252/252** | 35s | 4,79 → 5,54 |

      **Rodada anterior do `apps/api`, descartada como baseline e não como evidência:** às 00:03, com load **12,14** na largada (≈ 1× os núcleos, o limite da convenção 19), deu **799/800**. Reprovou `KnowledgeDocumentCatalogTests.SweepService_SurvivesAFailedCycle_AndPublishesInALaterOne_WithoutAnyCall` na `:251` (`Expected: 0, Actual: 1`). **Investigado, não classificado como ambiental:** o teste espera só a publicação (`:240-244`) e afirma o pedido apagado, e o despacho publica antes de apagar (`KnowledgeIndexingRequestDispatcher.cs:179` e `:193`). É uma janela real que a carga alarga. Virou a **#210** (`tipo: teste`), fora desta change. Se reaparecer no fechamento, a comparação é por nome contra esta linha.

      Saídas completas: `~/.cache/buteco-agents/179-baseline/{api,workers,inbox}-2.{log,trx}`, estado em `*-estado-2.txt`; a rodada descartada em `api.{log,trx}` e `api-estado.txt`.
- [x] 1.2 (raiz) `Directory.Packages.props`: `Microsoft.EntityFrameworkCore`, `.Relational`, `.Design` e `.InMemory` de 10.0.10 para **10.0.13**; acrescentar `OpenIddict.AspNetCore` e `OpenIddict.EntityFrameworkCore` **7.7.2** (D1). `dotnet restore` e `dotnet build` de `apps/api`, `apps/workers` e `apps/inbox` sem `NU1605`/`NU1608` e sem aviso novo. Conferir que o Pgvector continua resolvendo (comentário do arquivo).

      **Resultado, 24/10/2026.** EF Core nas quatro em 10.0.13. **Desvio do D1 (convenção 9):** o pacote do servidor é `OpenIddict.Server.AspNetCore` 7.7.2, e não o metapacote `OpenIddict.AspNetCore`. O metapacote traz também client, validation, `*.DataProtection` e `Client.SystemNetHttp`, nada disso usado aqui, e com eles dois defeitos medidos no build:
      - `NU1605` no `Buteco.Api.Tests`: `Microsoft.Extensions.Configuration` 10.0.10 (central) contra 10.0.11, que entra pela cadeia `Client.SystemNetHttp → Http.Polly`;
      - avisos novos `NU1902`/`NU1903` de `System.Security.Cryptography.Pkcs` e `.Xml` 10.0.11, que entram por `Microsoft.AspNetCore.DataProtection` (GHSA-2fmv-rv2c-59x7, GHSA-846f-f764-76mx, GHSA-7hqp-7c5m-4vq6, GHSA-c399-9853-6jmj, GHSA-r955-8q5j-3h83).

      Com só o servidor (`OpenIddict.Server.AspNetCore` + `OpenIddict.EntityFrameworkCore`), os dois somem. Nenhum `Microsoft.Extensions.*` central precisou subir. Build `--no-incremental` dos três apps, comparado com o worktree de baseline: **o mesmo conjunto de avisos** nos três, sem `NU1605`/`NU1608`. O Pgvector continua resolvendo: 0.3.2 e 0.3.0, sobre o Npgsql 10.0.3. Logs em `~/.cache/buteco-agents/179-fechamento/rebuild-*.log`.
- [x] 1.3 (`apps/workers`, `apps/inbox`) Rodar as duas suítes só com a 1.2 aplicada e comparar **por nome de teste** com a 1.1. Divergência para tudo aqui; a subida de EF Core não segue sem estar limpa.

      **Resultado, 24/10/2026, limpo.** VM com relógio acertado (Mac 05:56:56 UTC, VM 05:56:55); `CPU_Speed_Limit = 100`; nenhum contêiner em `podman ps` antes de cada suíte.
      - `apps/workers`: **415/415** em 5m52s, load 7,00 → 5,44;
      - `apps/inbox`: **252/252** em 38s, load 5,44 → 5,83.

      A comparação por nome contra os `.trx` da 1.1 dá o mesmo conjunto de testes e o mesmo resultado em cada um, sem nenhum teste a mais nem a menos. Saídas em `~/.cache/buteco-agents/179-fechamento/{workers,inbox}-ef.{log,trx}` e `*-estado-ef.txt`.

## 2. Persistência (`apps/api`)

- [x] 2.1 `Identity/Keys/IdentityKey.cs` e `IdentityKeyUsage.cs`; `DbSet<IdentityKey>` no `AppDbContext`, tabela `identity_keys` com as colunas do D3, `usage` como texto e índice em (`usage`, `validates_until`).
- [x] 2.2 `modelBuilder.UseOpenIddict()` no `OnModelCreating`, **não** no `DbContextOptionsBuilder` (D3), com `ToTable` snake_case para as quatro entidades.
- [x] 2.3 Migration `AddIdentityIssuer` gerada com `dotnet ef`. Teste `IdentityIssuerMigrationTests`: as cinco tabelas existem com os nomes do D3, `identity_keys` tem as colunas e tipos declarados, e `openiddict_applications` está vazia depois de migrar do zero.

      **Resultado:** a migration `20261024060713_AddIdentityIssuer` só cria as cinco tabelas e seus índices, e o snapshot só muda a `ProductVersion` (10.0.10 → 10.0.13). `IdentityIssuerMigrationTests`: 17/17.
- [x] 2.4 Confirmar que o `AppDbContext` de `apps/workers` não muda e que os três testes de espelho continuam verdes (D3).


      **Resultado:** nenhum arquivo do `apps/workers` mudou; os testes `*SchemaMirrorTests` do `apps/workers` passam, 60/60.
## 3. Chaves (`apps/api`)

- [x] 3.1 `IdentityKeyMaterialCipher` (AES-256-GCM, formato `nonce || ciphertext || tag`) e `IdentityIssuerOptions` (`Identity:SigningKeyEncryptionKey`). Testes de unidade cobrindo:
  - ida e volta;
  - nonce distinto por cifra;
  - chave errada falha;
  - mensagens de erro para chave ausente, fora de base64 e com tamanho errado.
- [x] 3.2 `IdentityKeyFactory`: RSA 2048/RS256 e AES-256, com `kid` aleatório. A linha gravada não contém PKCS#8, PEM nem JWK legível (teste).
- [x] 3.3 `IdentityKeyRotationPolicy`, com as constantes do D7 e o cálculo de `signs_from`, `signs_until` e `validates_until`. Testes de unidade com `FakeTimeProvider`: primeira chave, publicação antecipada a 7 dias, troca, saída do anel depois de 24 h.
- [x] 3.4 **Trava antes da 3.5.** Teste de unidade sobre o OpenIddict 7.7.2 real: montar `OpenIddictServerOptions` pelo builder do servidor, com duas credenciais de assinatura X.509 auto-assinadas, uma válida agora e outra com `NotBefore` no futuro, e afirmar três coisas:
  - a configuração é aceita, sem `ID0088` nem outra exceção;
  - o JWKS publica as duas chaves, com os dois `kid`;
  - um token emitido é assinado pela válida, e nunca pela futura.

  É o fato F10 do design, lido na fonte e ainda não exercitado. **Se o OpenIddict não aceitar, parar aqui e levar a alternativa ao dono antes de seguir.** A candidata é a alternativa descartada do D5: registrar as chaves como `RsaSecurityKey` pura e fixar a ordem num `IPostConfigureOptions` registrado depois do OpenIddict. O JWKS já publica todas as credenciais (F10), então a futura continua publicada sem handler próprio.

      **Resultado, 24/10/2026: o OpenIddict 7.7.2 aceita.** `OpenIddictFutureSigningCertificateTests` (host de teste com o OpenIddict real, sem nenhum tipo de produção):
      - com a válida e a futura (registrada primeiro), as opções montam sem exceção e a primeira credencial é a válida;
      - o JWKS publica os dois `kid`;
      - cinco tokens seguidos saem com o `kid` da válida.

      O lado negativo também está provado: só com a futura, o OpenIddict recusa com o texto do `ID0088`. O F10 vale, e a alternativa não foi necessária.
- [x] 3.5 `IdentityKeyRing` + `IdentityKeyRingOptionsConfigurer`: snapshot em memória, cada chave RSA embrulhada em X.509 auto-assinado em memória (`NotBefore = signs_from`, `NotAfter = validates_until`, `kid` da linha), e a chave AES como credencial de cifra. **Sem acesso a banco dentro do configurer** (D6).
- [x] 3.6 `IdentityKeyRingSigningOrderTests` (unidade): com duas chaves válidas e uma futura, cadastradas em ordem invertida, o `kid` escolhido é o da mais nova válida. **Verificar que reprova** trocando o embrulho X.509 por `RsaSecurityKey` pura, e registrar o resultado aqui (convenção 15).


      **Verificado contra o defeito, 24/10/2026 (convenção 15).** Com `ToSigningKey` devolvendo `RsaSecurityKey` pura no lugar do X.509, o teste reprova: `Expected: "c-ativa" / Actual: "a-anterior"`. Quem assina passa a ser a primeira da ordem de cadastro, que é a chave em retenção. Restaurado o embrulho, o teste passa.
## 4. Emissor (`apps/api`)

- [x] 4.1 `IdentityIssuerServiceCollectionExtensions.AddIdentityIssuer(configuration)`:
  - lê `PublicUrl:BaseUrl` sem lançar; vazia → não registra nada e guarda o motivo para o aviso de boot;
  - presente → `AddCore().UseEntityFrameworkCore().UseDbContext<AppDbContext>()`;
  - `AddServer()`: `SetIssuer`, os três `Set*EndpointUris`, `AllowClientCredentialsFlow()`, `DisableAccessTokenEncryption()` e `UseAspNetCore().DisableTransportSecurityRequirement()`;
  - o anel e a rotação.
- [x] 4.2 `ClientCredentialsTokenHandler` (D2): `sub = client_id`, escopos concedidos, `aud` pelos recursos dos escopos, destinos de claim no access token.
- [x] 4.3 `IdentityKeyRingGate` antes de `UseAuthentication` no `Program.cs`: `503` + `Retry-After: 5` enquanto o anel não carrega, sem guardar a falha. `EnsureLoadedAsync` cria a primeira chave de cada uso sob `pg_advisory_xact_lock`.
- [x] 4.4 `IdentityKeyRotationService` (D6 e D7), um `BackgroundService` em duas fases:
  - **aquecimento:** `EnsureLoadedAsync()` logo depois do start, sem bloquear o boot, repetindo a cada 5 s até carregar, sem guardar falha;
  - **rotação:** tique horário, com intervalo injetável, `pg_advisory_xact_lock` na forma `bigint` com a constante `IdentityKeyRotationPolicy.AdvisoryLockKey = 4779819235982396209` (comentada no código como `BUTECOK1` big-endian, espaço separado do `(int,int)` do `apps/workers`), criação antecipada e recarga com change token só quando o snapshot muda.
- [x] 4.5 `IdentityIssuerStartupValidation`: `ValidateIdentityIssuerConfiguration()` e `ValidateIdentityIssuerEndpoints()` depois do `Build()`, chamadas no `Program.cs` só com o emissor ligado. Aviso de boot quando desligado.
- [x] 4.6 `appsettings.json` com `"Identity": { "SigningKeyEncryptionKey": "" }`; `appsettings.Development.json` com uma chave de desenvolvimento marcada como tal; `TestAuthentication.ConfigOverrides` com a chave de teste.

## 5. Testes de integração (`apps/api`)

- [x] 5.1 `Support/IdentityIssuerTestClient.cs`: cadastra o escopo `teste.api` (recurso `buteco-teste`) e um cliente confidencial **depois** do `MigrateAsync`, só na fixture.
- [x] 5.2 `IdentityIssuerDiscoveryTests`: issuer da configuração com `Host` divergente; `jwks_uri` e `token_endpoint` absolutos sob o issuer; JWKS anônimo, com `kid` e sem parâmetro privado.
  - **Premissa do D8 falsa, corrigida com decisão do dono em 24/10/2026 (convenção 9):** o OpenIddict 7.7.2 monta os endpoints do discovery pelo host e esquema da requisição. Ver D8, "Correção na implementação". Entrou o `DiscoveryEndpointsFromIssuerHandler`.
  - Teste ampliado: com `Host` `outro-host.interno:8080` e esquema `http` contra o issuer `https://api.exemplo.test/`, todo campo terminado em `_endpoint`, inclusive aninhado, e o `jwks_uri` começam pelo issuer.
  - **Verificado que reprova** sem o handler (registro comentado), convenção 15: `'token_endpoint' = 'http://outro-host.interno:8080/connect/token' não está sob o issuer https://api.exemplo.test/` e `'jwks_uri' = 'http://outro-host.interno:8080/.well-known/jwks' não está sob o issuer …`, com 1 de 3 reprovado. Restaurado o registro, 3 de 3.
- [x] 5.3 `IdentityIssuerTokenTests`: token de `client_credentials` validado com `JsonWebTokenHandler` só contra o JWKS baixado, exigindo `iss` e `aud`, e conferindo `sub`, `iat`, `exp` e `kid`; segredo errado → `invalid_client`; `client_id` inexistente → `invalid_client`; `grant_type=password` → `unsupported_grant_type`.
- [x] 5.4 `IdentityIssuerCoexistenceTests`: com o emissor ligado, login HMAC → `200` e `GET /agents` com ele → `200`; JWT do emissor em `GET /agents` → `401`; token de serviço `service:inbox` continua nas rotas dele. Rodar também `AuthLoginTests`, `RouteAuthenticationTests` e `ServiceScopeAuthorizationTests` sem mudança neles.
- [x] 5.5 `IdentityKeyPersistenceTests`: reinício contra o mesmo banco preserva o `kid` e não cria linha; a primeira requisição cria uma chave de cada uso; com chave de cifra trocada → `503` e as mesmas linhas.
- [x] 5.6 `IdentityKeyRotationTests` com `FakeTimeProvider` e intervalo curto: publicação antecipada; troca do `kid` emitido **no mesmo processo**; token da chave anterior valida durante a retenção; `kid` some depois de 24 h; duas verificações concorrentes → uma chave de cada uso.
- [x] 5.7 `IdentityKeyRingGateTests`:
  - host subido antes da migração → `/health` `503` com `Retry-After`; depois de migrar, `/.well-known/jwks` `200` sem reinício;
  - **com o banco migrado antes do primeiro request**, esperar o aquecimento concluir e afirmar `/health` `200` **sem que a requisição tenha executado a carga** (contador de cargas do anel inalterado pela requisição);
  - o aquecimento repete depois de uma falha: banco indisponível na primeira tentativa, disponível na seguinte, e o anel carrega sem nenhuma requisição.
- [x] 5.8 `IdentityIssuerDisabledTests` sobre `ApiFactoryWithoutPublicUrlFixture`: discovery não é `200` com documento; o aviso aparece no log; o login continua `200`.
- [x] 5.9 `IdentityIssuerStartupValidationTests`: url pública sem esquema → o boot falha citando `PublicUrl:BaseUrl`; chave de cifra ausente → falha citando `Identity:SigningKeyEncryptionKey`; composição real com o endpoint de introspecção acrescentado → falha citando a URI. **Verificar que reprova** removendo a chamada de `ValidateIdentityIssuerEndpoints()` do `Program.cs`, e registrar aqui (convenções 8 e 15).
  - **Verificado que reprova**, em 24/10/2026: com `app.ValidateIdentityIssuerEndpoints();` comentada no `Program.cs`, `RealComposition_WithIntrospectionEndpoint_FailsToStart` reprova com `Assert.NotNull() Failure: Value is null` (o boot não falhou), 1 de 3. Restaurada a chamada, 3 de 3.

## 6. Stack de deploy (`apps/frontend`, raiz)

- [x] 6.1 (`apps/frontend`) `deploy/nginx.conf`: os blocos exatos de `/.well-known/openid-configuration` e `/.well-known/jwks`, e o de prefixo `^/(connect)(/|$)`, com o desvio de navegação e os headers do bloco do `apps/api` (D10).
- [x] 6.2 (raiz) `docker-compose.prod.yml`: `Identity__SigningKeyEncryptionKey: ${IDENTITY_SIGNING_KEY_ENCRYPTION_KEY:?...}` só no `apps/api`; `.env.prod.example` com a variável e a instrução de geração.
- [x] 6.3 (stack local) Subir o stack de servidor e conferir com `curl`, registrando a saída aqui:
  - discovery e JWKS devolvem JSON, não HTML;
  - `POST /connect/token` sem cliente devolve `invalid_client`;
  - uma navegação real (`Sec-Fetch-Mode: navigate`) a `/connect/token` cai no SPA;
  - o login do painel continua funcionando.

      **Resultado, 24/10/2026**, stack de servidor em `podman compose -p buteco179` (projeto e volumes próprios), com `api`, `inbox`, `frontend`, `postgres`, `rabbitmq` e o `migrator` construídos da árvore da change, e `.env.prod` local fora do git (`PUBLIC_DOMAIN=http://localhost:18090`, chave de cifra gerada para o teste, operador de teste com hash PBKDF2 gerado na hora).

      **Contorno de ambiente, fora do escopo, virou a #212:** no Podman, o nginx do stack não alcança nenhum backend, nem o bloco `/agents` que já existia, porque o `resolver 127.0.0.11` é o DNS do Docker. Para a verificação, troquei **só a linha do `resolver`** (para `10.89.5.1`, o `nameserver` do contêiner) dentro do contêiner, com `podman cp` e `nginx -s reload`. Os blocos do emissor conferidos são os do repositório, sem mudança.

      ```
      $ curl -s -i http://localhost:18090/.well-known/openid-configuration
      HTTP/1.1 200 OK
      Content-Type: application/json;charset=UTF-8
        issuer: http://localhost:18090/
        jwks_uri: http://localhost:18090/.well-known/jwks
        token_endpoint: http://localhost:18090/connect/token
        grant_types_supported: ['client_credentials']

      $ curl -s -H 'Host: outro-host.interno:8080' http://localhost:18090/.well-known/openid-configuration
        issuer: http://localhost:18090/
        jwks_uri: http://localhost:18090/.well-known/jwks
        token_endpoint: http://localhost:18090/connect/token

      $ curl -s -i http://localhost:18090/.well-known/jwks
      HTTP/1.1 200 OK
      Content-Type: application/json;charset=UTF-8
         {'kty': 'RSA', 'use': 'sig', 'alg': 'RS256', 'kid': '7xIA5pB8H00KGlRs1sw5eQ'} privados: []

      $ curl -s -i -X POST http://localhost:18090/connect/token -d grant_type=client_credentials -d client_id=nao-existe -d client_secret=x
      HTTP/1.1 401 Unauthorized
      Content-Type: application/json;charset=UTF-8
        "error": "invalid_client",
        "error_description": "The specified 'client_id' is invalid.",
        "error_uri": "https://documentation.openiddict.com/errors/ID2052"

      $ curl -s -i -H 'Sec-Fetch-Mode: navigate' http://localhost:18090/connect/token
      HTTP/1.1 200 OK
      Content-Type: text/html
          <title>Buteco Agentes</title>

      $ curl -s -i http://localhost:18090/.well-known/security.txt   (fora do emissor: continua no SPA)
      HTTP/1.1 200 OK
      Content-Type: text/html

      $ curl -s -i -X POST http://localhost:18090/auth/login -H 'Content-Type: application/json' -d '{"username":"operador-179","password":"<senha local>"}'
      HTTP/1.1 200 OK
      Content-Type: application/json; charset=utf-8
        token HMAC recebido: sim, 91 caracteres

      $ curl -s -i http://localhost:18090/agents -H 'Authorization: Bearer <token HMAC>'
      HTTP/1.1 200 OK
      Content-Type: application/json; charset=utf-8

      $ curl -s -i http://localhost:18090/agents   (sem token)
      HTTP/1.1 401 Unauthorized

      $ curl -s -i -H 'Sec-Fetch-Mode: navigate' http://localhost:18090/login
      HTTP/1.1 200 OK
      Content-Type: text/html
          <title>Buteco Agentes</title>
      ```

      - Discovery e JWKS respondem JSON, e não HTML. Com outro `Host`, os endereços continuam os do issuer. O JWKS não tem parâmetro privado.
      - O token sem cliente responde `invalid_client`, e uma navegação real a `/connect/token` cai no SPA. `/.well-known/security.txt`, fora do emissor, continua no SPA.
      - O login do operador dá `200` com o token HMAC, e `GET /agents` com ele dá `200`.
      - O `api` foi recriado no meio da verificação e manteve o mesmo `kid` (`7xIA5pB8H00KGlRs1sw5eQ`): a chave persistiu.

## 7. Documentação

- [x] 7.1 `01-ARQUITETURA_E_CONVENCOES.md`, seção "Autenticação": o emissor convivendo com o HMAC, e a revogação da convenção "sem biblioteca JWT" com data e motivo (D11). Tabela "Os cinco apps": o `apps/api` passa a citar o OpenIddict.
- [x] 7.2 `docs/architecture.md` (emissor no fluxo de autenticação), `docs/configuration.md` (`Identity__SigningKeyEncryptionKey` e o efeito de `PublicUrl__BaseUrl` no emissor) e `docs/deployment.md` (geração da chave, o que acontece ao perdê-la, a conferência pós-deploy, o `503` até o primeiro acesso ao banco e o **efeito no healthcheck do compose**: com o banco inalcançável, `curl -f /health` falha, o `api` fica `unhealthy` em cerca de 40 s, e `connectors` e `frontend` não sobem; o `inbox` não depende da saúde do `api` e sobe, ver a correção no D6).
- [x] 7.3 `CHANGELOG.md`: entrada da change.
- [x] 7.4 `02-HISTORICO_E_STATUS.md`: entrada da change e, junto da fila da evolução, a **regra das entregas** (só a entrega em andamento em `Ready`; entrega concluída → a seguinte vai para `Ready` na ordem da fila e a issue de acompanhamento fecha; `In progress` só ao abrir a change). Atualizar a fila da M1 para a ordem de 8 itens (#179, #208, #209, #180, #181, #182, #211, #183). Registrar também: o desvio da rota de leitura das chaves, que a #179 previa, para a **#211**, fora desta change e fora da #183 por decisão do dono (convenção 9); a #209 e a #210 abertas nesta change; e os números de baseline e fechamento.

## 8. Fechamento

- [x] 8.1 (`apps/api`, `apps/workers`, `apps/inbox`) Suítes completas no worktree da change, com o mesmo protocolo da 1.1, e comparação **por nome** com a baseline. Saída completa em `~/.cache/buteco-agents/179-fechamento/`.

      **Resultado, 24/10/2026**, no worktree da change (árvore completa, com o handler da 5.2), mesmo protocolo da 1.1: relógio da VM acertado antes de cada suíte, `CPU_Speed_Limit = 100`, nenhum contêiner em `podman ps`, cada suíte só com o load de 1 min abaixo de 8.

      | suíte | baseline (1.1) | fechamento | por nome |
      |---|---|---|---|
      | `apps/api` | 800/800, 1m53s | **863/863**, 1m45s (load 2,68 → 12,55) | os 800 da baseline existem e passam; +63 novos, todos em `Buteco.Api.Tests.Identity` |
      | `apps/workers` | 415/415, 6m13s | **415/415**, 5m05s (load 7,63 → 2,55) | idênticos |
      | `apps/inbox` | 252/252, 35s | **252/252**, 34s (load 2,55 → 3,54) | idênticos |

      A falha da #210 não reapareceu. Saídas em `~/.cache/buteco-agents/179-fechamento/{api,workers,inbox}-fech.{log,trx}` e `*-estado-fech.txt`.
- [x] 8.2 `openspec validate --all` e `python3 scripts/check-docs.py` verdes. **24/10/2026:** `openspec validate --all --strict` 75/75 e `check-docs.py` OK.
- [x] 8.3 `/opsx:sync` e `/opsx:archive` só depois de 7.x e 8.1–8.2 (o `02` e o `CHANGELOG` antes do archive).

      **Feito em 24/10/2026**, com o `02` e o `CHANGELOG` já escritos. Sync pelo agente: `oidc-issuer` (6 requisitos) e `identity-signing-keys` (4) criadas com Purpose escrito; `route-authentication` ganhou 1 requisito. `openspec validate --specs --strict`: 76/76. Archive com `--skip-specs`, porque as deltas já estavam aplicadas.
- [ ] 8.4 Push de `feat/179-emissor-oidc-api` e PR com `Closes #179` e `Refs #209`; mover a #179 para `In review`.
- [ ] 8.5 Depois do merge: remover os worktrees `../buteco-agentes-179` e `../buteco-agentes-179-baseline` e a branch local, conferindo cada um antes.
