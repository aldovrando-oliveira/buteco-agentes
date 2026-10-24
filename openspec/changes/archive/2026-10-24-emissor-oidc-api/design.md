## Context

**Estado atual (`c29246c`, 24/10/2026).** A autenticação do `apps/api` é um esquema único, `OperatorTokenAuthenticationHandler`, sobre `ITokenService`, um HMAC próprio sem biblioteca JWT, com a chave `Auth:TokenSigningKey` compartilhada com o `apps/inbox` e o `apps/connectors`. A autorização é a `FallbackPolicy` com `ServiceScopeRequirement`. As rotas anônimas são três (`/health`, `/auth/login` e o AgentCard), classificadas por `AnonymousRouteClassification` e conferidas no boot por `ValidateRouteAuthenticationClassification` (convenção 8). Nada disso muda aqui.

**A evolução** (fila no `02`, decisões nas issues #179–#208) faz do Buteco o próprio emissor de identidade, em passos que deixam o `main` implantável:

1. **#179, esta change:** o emissor passa a existir.
2. **#208:** o `apps/inbox` e o `apps/connectors` aceitam o token dele, convivendo com o HMAC.
3. **#180:** o login passa a emitir pelo emissor.
4. **#182:** o HMAC é removido.

**Restrições que o código impõe e que fixam o desenho** (lidas em `c29246c`):

- **Migration bundle.** O `api-migrate` (`deploy/migrate/entrypoint.sh`) roda o `Program.cs` até o `Build()` para descobrir o `AppDbContext`. Validação que lança **antes** do `Build()` derruba o bundle. É o achado empírico registrado no próprio script, a propósito do `Api:BaseUrl` do `apps/inbox`. Toda validação desta change roda **depois** do `Build()`, no molde de `ValidateConnectorsConfiguration`.
- **Fixtures sobem o host antes de migrar.** `ApiFactoryFixture.InitializeAsync` faz `Services.CreateScope()`, que sobe o host e os `IHostedService`, e **só então** `MigrateAsync()`. Os outros cinco fixtures `WebApplicationFactory<Program>` seguem o mesmo molde. Qualquer leitura de banco no boot quebra a suíte inteira.
- **Fixtures substituem o `DbContextOptions<AppDbContext>`.** Elas registram por conta própria `UseButecoAgentsNpgsql(...)`. Configuração do OpenIddict pendurada no `DbContextOptionsBuilder` (`options.UseOpenIddict()`) **sumiria** nos testes.
- **`apps/api` sobe hoje sem banco.** Nenhuma checagem de boot do `apps/api` faz I/O.

### Achados da fonte do OpenIddict 7.7.2 (tag `7.7.2`, `openiddict-core`, lida em 24/10/2026)

| # | fato | onde |
|---|---|---|
| F1 | Sem nenhum grant nem response type habilitado, o servidor recusa a configuração (`ID0076`). | `OpenIddictServerConfiguration.PostConfigure`, l. 62 |
| F2 | Exige ao menos uma credencial de **cifra** (`ID0085`), mesmo com a cifra do access token desligada, e ao menos uma de assinatura **assimétrica** (`ID0086`). | l. 250–257 |
| F3 | Assina com `SigningCredentials.First()`. A lista é ordenada por `List.Sort` com um comparador que, entre chaves **não X.509**, devolve `0`; entre X.509, prefere a de `NotAfter` mais distante e despreza a de `NotBefore` futuro. O JWKS publica **todas** as `SigningCredentials`. | `Protection.cs` l. 1478–1485; `Configuration` l. 544–571; `Discovery.cs` l. 760 |
| F4 | As opções são lidas por requisição via `IOptionsMonitor<OpenIddictServerOptions>.CurrentValue`. | `OpenIddictServerFactory.cs:38`, `OpenIddictServerDispatcher.cs:90` |
| F5 | Os endpoints são atendidos pelo `OpenIddictServerAspNetCoreHandler`, um `IAuthenticationRequestHandler` invocado pelo `UseAuthentication` **em toda requisição**. Não são `RouteEndpoint`. | `OpenIddictServerAspNetCoreHandler.cs:22,60` |
| F6 | O esquema do OpenIddict não pode ser o padrão do app (`ID0109`). Ele é registrado ao lado do esquema existente. | `OpenIddictServerAspNetCoreConfiguration.cs:55` |
| F7 | Por padrão, requisição não-HTTPS a endpoint do emissor é recusada (`ValidateTransportSecurityRequirement`). Existe `DisableTransportSecurityRequirement()`. | `OpenIddictServerAspNetCoreHandlers.cs:118–143` |
| F8 | `AccessTokenLifetime` padrão é 1 h. | `OpenIddictServerOptions.cs:252` |
| F9 | Existe `ModelBuilder.UseOpenIddict()`, além de `DbContextOptionsBuilder.UseOpenIddict()`. | `OpenIddictEntityFrameworkCoreHelpers.cs:154` |
| F10 | Certificado X.509 de assinatura fora da validade só faz a configuração lançar se **todas** as credenciais de assinatura forem X.509 fora da validade (`ID0088`). O JWKS publica **todas** as `SigningCredentials`, sem filtrar por validade. Leitura da fonte; a tarefa 3.4 prova por teste antes de qualquer código depender disso. | `Configuration` l. 270–275; `Discovery.cs` l. 1168 |

F3 + F5 juntos decidem D5 e D6. O OpenIddict monta as opções na primeira requisição de **qualquer** rota, e sem chave assimétrica essa montagem lança. Então as chaves precisam estar carregadas antes de o `UseAuthentication` rodar, sem I/O no boot.

## Goals / Non-Goals

**Goals:**

- Discovery, JWKS e endpoint de token servidos pelo `apps/api`, com issuer da url pública.
- Chaves assimétricas persistidas, cifradas em repouso, com `kid`, rotação automática sem reinício e retenção da anterior.
- Emissão provada de ponta a ponta nos testes: token JWT com `iss`, `sub`, `aud`, `iat`, `exp` e `kid`, verificável só pelo JWKS.
- Nova forma da convenção 8 para endpoints que não passam pelo roteamento.
- Convivência total: login HMAC, validação HMAC e escopo de subject sem nenhuma mudança.

**Non-Goals:**

- **Aceitar o token do emissor como credencial.** Nem no `apps/api` (é da #180, junto do login) nem no `apps/inbox`/`apps/connectors` (é da #208).
- **Usuário, login com e-mail, MFA, sessão, refresh token** (#180). Aplicação externa e esquema OAuth2 no AgentCard (#186).
- **Limpeza das linhas de token do OpenIddict** (#209, aberta nesta change). Aqui nenhum token é emitido em produção.
- **Rotação da chave de cifra.** Ela não cifra nada nesta change (D4). A rotação passa a importar quando a #180 emitir refresh token, e fica registrada como decisão daquela change.
- **Rota de leitura do emissor e das chaves para a tela Identidade**, que agora é a **#211** (M1 · 7 de 8, antes da #183). Por decisão do dono, em 24/10/2026, a rota fica fora desta change e também fora da #183, porque tela não leva backend (`docs/conventions.md`, "Sequenciamento"). A #179 previa a rota; o desvio é registrado no fechamento (convenção 9).

## Decisions

### D1. OpenIddict 7.7.2, com EF Core 10.0.13 em todo o repositório

Verificado em 24/10/2026, no índice e no catálogo do NuGet (`api.nuget.org/v3`):

- **OpenIddict.AspNetCore e OpenIddict.EntityFrameworkCore 7.7.2**, publicados em 10/10/2026, `licenseExpression` **Apache-2.0**, com grupo de dependência **`net10.0`**. É a última estável; a 8.0 está em `preview.4`, alvo só `net10.0`.
- **Correção na implementação (24/10/2026, tarefa 1.2; convenção 9): o pacote de servidor é `OpenIddict.Server.AspNetCore`, e não o metapacote `OpenIddict.AspNetCore`.** O metapacote traz também o client, o validation, os `*.DataProtection` e o `Client.SystemNetHttp`, nada disso usado aqui. Medido no build, ele causa `NU1605` (`Microsoft.Extensions.Configuration` 10.0.10 contra 10.0.11, pela cadeia `Http.Polly`) e avisos novos de vulnerabilidade em `System.Security.Cryptography.Pkcs`/`.Xml` 10.0.11 (via `AspNetCore.DataProtection`). Com só o servidor, os dois somem, e nenhum `Microsoft.Extensions.*` central precisa subir.
- **Dependências mínimas da 7.7.2 em `net10.0`:** `Microsoft.EntityFrameworkCore.Relational ≥ 10.0.11`, `Microsoft.Extensions.* ≥ 10.0.11` e `Microsoft.IdentityModel.* ≥ 8.19.2`.

O repositório fixa `Microsoft.EntityFrameworkCore*` em **10.0.10**, centralmente. Daí a escolha:

| opção | consequência |
|---|---|
| **7.7.2 + EF Core → 10.0.13** (escolhida) | a versão corrente das duas; a subida é patch da mesma linha e alcança `apps/workers` e `apps/inbox` pelo arquivo central |
| 7.6.1 (27/08/2026, aceita EF 10.0.10) | não toca nos outros apps, mas entra com uma linha já substituída |
| `VersionOverride` só no `apps/api` | dois `AppDbContext` sobre o mesmo schema com EF diferente; a convenção do espelho (`KnowledgeSchemaMirrorTests`) deixa de valer por construção |

Sobem juntos `Microsoft.EntityFrameworkCore`, `.Relational`, `.Design` e `.InMemory` para **10.0.13**. O `Npgsql.EntityFrameworkCore.PostgreSQL` fica em 10.0.3, a última da linha 10. O comentário do `Directory.Packages.props` sobre o Pgvector exige atenção num upgrade de EF Core; a tarefa 1 confere restore e build sem `NU1605`/`NU1608` nos três apps antes de qualquer outra coisa.

### D2. O que esta change emite: `client_credentials`, sem cliente

Pelo F1, o servidor não sobe sem algum fluxo. Entre "um fluxo sem consumidor" e "nenhum servidor", fica o primeiro, com o fluxo de **menor superfície**:

- **Só `AllowClientCredentialsFlow()`.** Nada de authorization code, password, refresh nem device.
- **Nenhum cliente, escopo ou recurso cadastrado.** Nem migration, nem seed. Em produção, todo `POST /connect/token` termina em `invalid_client`.
- **Um handler** do evento `HandleTokenRequestContext`, para `client_credentials`, monta o principal com:
  - `sub = client_id`;
  - escopos concedidos;
  - `aud` = recursos dos escopos, via `IOpenIddictScopeManager.ListResourcesAsync`.

  É o idioma do OpenIddict para client credentials e o que a #186 (aplicações externas) e a #208 (`aud` por app) vão consumir.
- **`DisableAccessTokenEncryption()`.** O access token sai JWT assinado e não cifrado, porque quem valida (a #208) só tem o JWKS. Os outros tipos de token continuam cifrados, como o OpenIddict exige.
- **`AccessTokenLifetime`:** fica o padrão de 1 h (F8). Mudar o padrão sem consumidor seria opção sem cenário (convenção 2).

**Como é testado:** a fixture do emissor cadastra, **depois** do `MigrateAsync`, um escopo `teste.api` com recurso `buteco-teste` e um cliente confidencial com as permissões. O cadastro usa os managers do OpenIddict sobre o host de teste. O teste pede o token, baixa o JWKS e valida com `Microsoft.IdentityModel.JsonWebTokens` (`JsonWebTokenHandler`), exigindo `iss` e `aud`. Um segundo teste afirma que a tabela de aplicações está vazia num banco migrado do zero, que é o guarda do "sem seed".

### D3. Tabelas: as do OpenIddict pelo `ModelBuilder`, com nomes snake_case; `identity_keys` própria

- **Registro no modelo, não nas opções.** `modelBuilder.UseOpenIddict()` vai no `AppDbContext.OnModelCreating` (F9), e não no `DbContextOptionsBuilder`. Assim a substituição de `DbContextOptions` das fixtures não remove as entidades. No servidor: `AddCore().UseEntityFrameworkCore().UseDbContext<AppDbContext>()`.
- **Nomes no padrão da casa.** O `AppDbContext` nomeia tudo em snake_case com `ToTable(...)`, então as quatro tabelas viram `openiddict_applications`, `openiddict_authorizations`, `openiddict_scopes` e `openiddict_tokens`. As colunas ficam como o OpenIddict as declara, porque renomear coluna de entidade de terceiro é manutenção contra cada upgrade, para nada.
- **`identity_keys`, entidade própria**, em `Identity/Keys/IdentityKey.cs`:

  | coluna | conteúdo |
  |---|---|
  | `kid` | texto, chave primária |
  | `usage` | `signing` ou `encryption`, gravado como texto |
  | `algorithm` | `RS256` ou `A256KW` |
  | `protected_material` | `bytea` |
  | `created_at`, `signs_from`, `signs_until`, `validates_until` | `timestamptz` |

  Índice em (`usage`, `validates_until`).
- **Uma migration**, `AddIdentityIssuer`, gerada no `apps/api`. O bundle `api-migrate` passa a criá-las sem mudança no `entrypoint.sh`.
- **`apps/workers` não muda.** O `AppDbContext` dele é espelho **por tabela usada**, não do schema inteiro: mapeia as 14 tabelas que lê e escreve, e os testes de espelho (`KnowledgeSchemaMirrorTests`, `ExecutionMetricsSchemaMirrorTests`, `EmbeddingMetricsSchemaMirrorTests`) comparam tabela a tabela com as próprias migrations. O worker não lê nenhuma tabela de identidade, e mapeá-las seria declaração viva sem consumidor (convenção 25). A única mudança no `apps/workers` é a versão da EF Core (D1).

### D4. Chaves: RSA 2048 para assinar, AES-256 para cifrar, material privado cifrado com AES-256-GCM

- **Assinatura: RSA 2048, RS256.** É o par que todo validador de JWKS aceita sem configuração, e a #208 e os provedores federados da M3/M4 são validadores de terceiros. A rotação de 90 dias limita a exposição de cada chave.
- **Cifra: AES-256 simétrica**, que o comparador prefere (F3). Exigida pelo F2 e sem uso nesta change, porque nenhum token cifrado é emitido. É persistida mesmo assim: chave efêmera seria exatamente a "chave gerada a cada boot" que a evolução proíbe, e a #180 vai cifrar refresh tokens com ela.
- **Em repouso:** o material privado de cada linha é cifrado com AES-256-GCM, nonce de 96 bits por cifra, no formato do `AesGcmMcpCredentialCipher`. A chave vem de **`Identity:SigningKeyEncryptionKey`** (32 bytes em base64), de configuração ou ambiente, **nunca do banco**, e com valor distinto da `Mcp:CredentialEncryptionKey`, como a `Inbox:CredentialEncryptionKey` já faz. Quem obtém só um dump do banco não obtém a chave de assinatura.
- **Cifra própria, não o `AesGcmMcpCredentialCipher` generalizado.** É o segundo consumidor de AES-GCM no `apps/api`, e a convenção 2 espera o terceiro. As duas classes ficam lado a lado, cada uma com a própria chave e a própria mensagem de erro.
- **Chave que não decifra não é substituída.** Uma chave de cifra trocada por engano faria o anel não carregar; gerar chave nova em silêncio invalidaria tudo o que já foi assinado e esconderia o erro de configuração. O emissor fica em `503` (D6), e o log diz qual `kid` não decifrou.

**Alternativa descartada: ASP.NET Core Data Protection persistida no mesmo banco.** As chaves que protegem a chave de assinatura ficariam na mesma tabela que um dump entrega, e a proteção viraria cerimônia.

### D5. Ordem de assinatura: cada chave entra como X.509 auto-assinado em memória

O OpenIddict assina com a primeira credencial depois de uma ordenação que **não desempata** chaves RSA puras (F3). Com duas chaves publicadas (a próxima, ou a anterior em retenção), a que assina dependeria da ordem de um `Sort` instável.

**Decisão:** na carga, cada chave RSA é embrulhada num **certificado X.509 auto-assinado, gerado em memória**, com:

- `NotBefore = signs_from`;
- `NotAfter = validates_until`.

O comparador documentado do próprio OpenIddict passa a fazer a escolha:

- a próxima chave (`NotBefore` futuro) nunca é preferida;
- entre as válidas, assina a de `NotAfter` mais distante, que é sempre a mais nova.

O certificado não é gravado (é derivado da linha a cada carga) e não tem uso fora do processo. O `kid` é explícito, o da linha, e não o thumbprint que o OpenIddict geraria (l. 548–552).

**Alternativa descartada:** reordenar `SigningCredentials` num `IPostConfigureOptions` registrado depois do OpenIddict. Funciona, mas depende da ordem de registro de `PostConfigure`, e um upgrade que mude essa ordem troca a chave que assina sem erro nenhum. O X.509 usa o critério que o OpenIddict declara.

**Guarda:** um teste com duas chaves válidas e uma futura afirma que o `kid` do token é o da mais nova válida, com as chaves cadastradas em ordem invertida. Ele reprova se a carga registrar `RsaSecurityKey` pura (convenção 15: o guarda é exercitado contra o defeito que existe para pegar).

### D6. Anel de chaves em memória, carregado na primeira requisição; `503` até lá

Pelo F5, o OpenIddict monta as opções em toda requisição, e pelo F2 a montagem lança sem chave. As fixtures proíbem I/O no boot. A saída:

- **`IdentityKeyRing` (singleton)** guarda o snapshot das chaves vigentes, decifradas. Um `IConfigureOptions<OpenIddictServerOptions>` só lê esse snapshot. **Nunca consulta o banco dentro da fábrica de opções**, onde uma falha ficaria presa no cache de opções.
- **`IdentityKeyRingGate`, middleware antes do `UseAuthentication`:**
  - enquanto o anel não carregou, chama `EnsureLoadedAsync()` (com semáforo), que lê `identity_keys` e cria a primeira chave de cada uso se não houver nenhuma (D7);
  - se a carga falha, responde **`503` com `Retry-After: 5`**, sem guardar a falha, e a próxima requisição tenta de novo;
  - depois de carregado, o custo por requisição é a leitura de um booleano.
- **Carga em segundo plano logo depois do start** (decisão do dono, 24/10/2026). O `IdentityKeyRotationService` (D7) começa com uma fase de aquecimento:
  - **não bloqueia o boot:** o `StartAsync` só agenda, e o `ExecuteAsync` roda depois de o host subir;
  - **tenta na hora:** chama `EnsureLoadedAsync()` logo depois do start;
  - **repete com intervalo:** se falhar, tenta de novo a cada 5 s, até carregar, e só então passa ao tique horário da rotação;
  - **não guarda falha:** cada tentativa é independente, e uma falha só gera log.

  O gate continua sendo a garantia: se uma requisição chega antes do aquecimento, ela mesma tenta carregar, pelo mesmo `EnsureLoadedAsync` e sob o mesmo semáforo. O aquecimento existe para o primeiro `/health` **raramente** encontrar o anel vazio, não para substituir o gate.
- **Consequência declarada:** com o emissor ligado, o `apps/api` só atende depois de alcançar o banco **uma vez**, e até lá **toda** rota responde `503`, inclusive `/health`. Hoje ele atende rotas sem banco.
  - Em produção, não muda nada: o `migrator` termina antes de o `apps/api` subir (`depends_on: service_completed_successfully`), e o aquecimento carrega o anel antes da primeira sonda.
  - **Efeito no healthcheck do compose:** a imagem do `apps/api` sonda com `curl -f http://localhost:8080/health` (intervalo 10 s, `start-period` 10 s, 3 tentativas), e `curl -f` trata `503` como falha. Se o banco não estiver alcançável, o contêiner fica `unhealthy` em cerca de 40 s, e o `connectors` e o `frontend`, que declaram `depends_on: api: service_healthy`, não sobem. *(Corrigido na implementação, 24/10/2026, tarefa 7.2: o texto original incluía o `inbox`, mas no `docker-compose.prod.yml` ele depende só do `migrator` e sobe; a decisão não muda.)* É o comportamento certo: sem banco o `apps/api` não serve nada. Fica registrado no `docs/deployment.md` para quem ler um `unhealthy` não procurar defeito no emissor.
  - Em `dotnet run` com o Postgres parado, a diferença é a resposta (`503` em vez de `500` na primeira rota que usa banco).
  - Banco que cai **depois** da carga não afeta o emissor, que já tem as chaves em memória.

**Alternativas descartadas:**

- **Carregar no boot, entre o `Build()` e o `Run()`**, como o `ValidateEmbeddingIndexConsistency` do `apps/workers`. Quebra os seis fixtures, que migram depois de subir o host, e muda o `dotnet run` sem banco de "sobe" para "não sobe". Reordenar os seis fixtures é diff grande fora do assunto.
- **Carregar dentro do `IConfigureOptions`.** I/O síncrono na fábrica de opções, com a falha presa no cache.
- **Só o gate, sem aquecimento.** Funciona, mas a primeira sonda do healthcheck pagaria a carga dentro dos 3 s de `timeout`, incluindo a criação das chaves RSA na primeira subida.

### D7. Rotação: verificação a cada hora, sob lock do Postgres, com recarga das opções

**Constantes no código (convenção 2: nenhuma vira opção sem cenário):**

| constante | valor | por quê |
|---|---|---|
| intervalo de rotação | 90 dias | decisão da evolução |
| publicação antecipada | 7 dias | a próxima chave aparece no JWKS uma semana antes de assinar; a #208 fixa o cache do JWKS abaixo disso |
| retenção depois de parar de assinar | 24 h | ≥ `AccessTokenLifetime` (1 h) + intervalo da verificação (1 h), com folga; o boot afirma essa desigualdade (D8) |
| intervalo da verificação | 1 h | |

**`IdentityKeyRotationService`, um `BackgroundService`,** a cada hora:

1. abre transação e chama `pg_advisory_xact_lock(@key)` na forma de **uma chave `bigint`**, com `IdentityKeyRotationPolicy.AdvisoryLockKey = 4779819235982396209`, os bytes ASCII de `BUTECOK1` lidos como inteiro big-endian. O valor fica escrito no código, na constante com este comentário, e neste design. Por que a forma `bigint`:
   - o Postgres mantém espaços de chave **separados** para as duas formas: "identified either by a single 64-bit key value or two 32-bit key values (note that these two key spaces do not overlap)" (documentação do PostgreSQL 18, §9.28.10, conferida em 24/10/2026; o banco é `pgvector/pgvector:pg18`);
   - o `apps/workers` já pega lock no mesmo banco (`buteco_agents`) na forma de dois `int4`: `ConversationContextLock.cs:114`, `pg_advisory_lock(hashtext(agentId), hashtext(contextId))`;
   - a forma `bigint` não colide com aquele espaço por construção, sem depender de nenhum valor de `hashtext`.

   O `apps/api` não pega advisory lock nenhum hoje: o `AgentDelegationCycleDetector` só cita, em comentário, o lock que o worker toma. A variante `_xact_` solta o lock no fim da transação, então não há `unlock` a esquecer;
2. se não há chave de assinatura com `signs_from > agora`, e a ativa tem `signs_until - agora < 7 dias`, cria a próxima, com `signs_from = signs_until` da ativa;
3. não apaga nada: a linha com `validates_until` vencido só sai do anel. Apagar é limpeza sem consumidor, e a linha não ocupa nada que importe;
4. recarrega o snapshot e, **se ele mudou** (conjunto de `kid`s ou qual assina), dispara o `IOptionsChangeTokenSource<OpenIddictServerOptions>` do anel, e o `IOptionsMonitor` reconstrói as opções na próxima requisição (F4).

A troca efetiva de quem assina acontece no primeiro tique depois de `signs_from`, com até 1 h de atraso. É por isso que a retenção conta com o intervalo da verificação. O `EnsureLoadedAsync` da primeira requisição usa o mesmo caminho e o mesmo lock.

**Por que não `OpenIddict.Quartz` ou um scheduler:** o `apps/api` já tem o idioma de `BackgroundService` com intervalo injetável (`KnowledgeIndexingRequestSweepService`, `KnowledgeIndexingRequestSchedule`). O teste usa o mesmo truque, intervalo curto e `FakeTimeProvider`.

### D8. Configuração, emissor ligado/desligado e checagens de boot

**Ligado ou desligado pela url pública.** A decisão é tomada **no registro dos serviços**, só lendo configuração e sem lançar, para o bundle de migration continuar subindo:

- **`PublicUrl:BaseUrl` vazia:** o OpenIddict não é registrado, e o boot loga `Warning`: "Emissor OIDC desligado: PublicUrl:BaseUrl não configurada". É o mesmo tratamento que os endereços A2A recebem (`agente-enderecos-a2a`, D2): sem url pública não existe endereço público, e afirmar um quebrado é pior que declarar a ausência. Inferir o issuer do `Host` da requisição é exatamente o que a casa proíbe para os endereços A2A, porque atrás do proxy o host não é o público.
- **`PublicUrl:BaseUrl` presente:** o issuer é `new Uri(BaseUrl.TrimEnd('/') + "/")`, lido pelo **mesmo** `PublicUrlOptions` que o `AgentA2AAddressBuilder` consome. Não há segunda configuração de url.

**`ValidateIdentityIssuerConfiguration()`**, depois do `Build()`, com o emissor ligado. Derruba o boot se:

- a url pública não for absoluta `http`/`https`, ou tiver query ou fragmento;
- `Identity:SigningKeyEncryptionKey` estiver ausente, fora de base64 ou sem 32 bytes;
- a retenção de 24 h for menor que `AccessTokenLifetime` mais o intervalo da verificação.

**`ValidateIdentityIssuerEndpoints()`**, a nova forma da convenção 8. Os endpoints do OpenIddict não são `RouteEndpoint` (F5), e por isso a `ValidateRouteAuthenticationClassification` não os vê. A checagem:

- aplica os `IConfigureOptions<OpenIddictServerOptions>` **registrados na composição real**, exceto o do anel de chaves, a uma instância nova, sem `PostConfigure`. Assim não precisa de chave nem de banco;
- lê as URIs de **todos** os tipos de endpoint (authorization, token, JWKS, configuration, introspection, revocation, userinfo, end session, device, end-user verification, pushed authorization);
- compara nos dois sentidos com `IdentityIssuerEndpoints.Expected`: `/.well-known/openid-configuration` (`PublicDiscovery`), `/.well-known/jwks` (`PublicDiscovery`) e `/connect/token` (`AuthEntryPoint`), reaproveitando o enum `AnonymousRouteReason`;
- reprova se uma dessas URIs coincidir com o `RawText` de uma rota mapeada.

A checagem tem a forma "sobre a composição", então paga o teste extra que a convenção 8 exige: um teste captura a composição real pela `WebApplicationFactory`, acrescenta um endpoint (introspecção) e afirma que o boot falha. **Remover a chamada do `Program.cs` reprova esse teste.** O teste é verificado reintroduzindo exatamente esse defeito (convenção 15).

**Transporte:** `DisableTransportSecurityRequirement()` (F7). No stack, TLS termina fora (nginx externo/Cloudflare), e o `apps/api` recebe HTTP puro. É a mesma razão por que não há `UseHttpsRedirection` (spec `server-deployment`, "apps/api não redireciona HTTPS internamente no stack"). O risco de servir o emissor por HTTP fica coberto pelo issuer fixado por configuração e pelo `DiscoveryEndpointsFromIssuerHandler` (abaixo): o discovery anuncia o endereço público (`https://...`), nunca o de origem.

**Correção na implementação (24/10/2026, tarefa 5.2; convenção 9, decisão do dono).** A premissa "fixar o issuer basta para o discovery anunciar o endereço público" é **falsa** no OpenIddict 7.7.2. Só o campo `issuer` sai da configuração:

- `OpenIddictServerHandlers.Discovery.cs`, `AttachEndpoints`: cada endpoint é `CreateAbsoluteUri(context.BaseUri, …)`, com `??=` (l. 417 para o JWKS e l. 444 para o token; o mesmo para os demais e para os aliases mTLS);
- `OpenIddictServerAspNetCoreHandlers.cs`, l. 102: o `BaseUri` é `request.Scheme` + `Host` + `PathBase` da requisição;
- no stack, o nginx repassa `Host $host` sobre HTTP puro, então o discovery anunciaria `http://<domínio>/…` sob um issuer `https://<domínio>/`. Medido no teste da 5.2: `jwks_uri` = `http://outro-host.interno:8080/.well-known/jwks`.

Configurar os endpoints como URIs absolutas sob o issuer não resolve. Em `OpenIddictServerHandlers.cs`, `InferEndpointType`, uma URI absoluta só casa quando `Equals(uri, context.RequestUri)`, isto é, com mesmo esquema, host e porta da requisição. Atrás do nginx, nenhum dos três endpoints seria atendido.

**A correção:** `Identity/DiscoveryEndpointsFromIssuerHandler.cs`, um handler de `HandleConfigurationRequestContext` ordenado entre o `AttachIssuer` e o `AttachEndpoints`. Ele troca o `BaseUri` **da transação de discovery** pelo issuer já resolvido. Assim o próprio `AttachEndpoints` monta, a partir do issuer, **todo** endpoint configurado no servidor, e não uma lista mantida aqui: o que uma change futura habilitar (introspecção, userinfo, autorização da #180) sai certo sem trabalho extra, e endpoint sem URI configurada continua fora do documento. A troca não vaza: o casamento da rota já aconteceu, e nada depois dele no discovery usa o `BaseUri`. O teste da 5.2 afirma que todo campo `*_endpoint` e o `jwks_uri` começam pelo issuer, e reprova sem o handler.

Opções descartadas:

- **`ForwardedHeaders` no `apps/api`:** já recusado acima (o host da requisição não é a fonte do endereço público) e não resolveria o esquema, porque o TLS termina fora do stack e o nginx do stack só vê HTTP; dependeria de um `X-Forwarded-Proto` que o proxy externo não garante;
- **mudar a spec para aceitar endereços montados pela requisição:** contradiz a razão de fixar o issuer. Um cliente OIDC que confere `jwks_uri` contra o issuer, ou que segue o `token_endpoint` anunciado, iria a `http://`.

### D9. O esquema existente continua o padrão; o token do emissor não é credencial aqui

`AddAuthentication(OperatorTokenAuthenticationHandler.SchemeName)` não muda. O OpenIddict registra o próprio esquema ao lado (F6), e só ele atende os três endpoints. **Não** se registra `OpenIddict.Validation` no `apps/api`. Por isso um JWT do emissor enviado a `GET /agents` passa pelo handler HMAC, falha a validação e recebe `401`. Há teste para isso (spec `oidc-issuer`), porque é a fronteira entre esta change e a #180.

O pedido ao endpoint de token não carrega `Authorization: Bearer`; o cliente se autentica no corpo, então o handler HMAC não interfere. E o OpenIddict encerra a requisição dentro do `UseAuthentication`, antes da `FallbackPolicy`.

### D10. nginx do stack

`apps/frontend/deploy/nginx.conf` ganha três blocos, todos com o mesmo `proxy_pass` e os mesmos headers do bloco do `apps/api`:

- `location = /.well-known/openid-configuration` e `location = /.well-known/jwks`, **exatos**. `/.well-known/` é prefixo de uso geral na web (ACME, `security.txt`), e nada além dessas duas URIs pertence ao `apps/api`;
- `location ~ ^/(connect)(/|$)`, de prefixo, com o mesmo desvio de navegação real (`Sec-Fetch-Mode: navigate` cai no SPA) que a spec `server-deployment` exige de todo bloco de API.

Sem eles, os três caem no `location /` e devolvem `200` com o HTML do painel. É o defeito que a spec "Nginx do stack roteia por prefixo" nomeia, e o que a #152 registrou para `/sync`. Não existe teste automatizado do `nginx.conf` (nenhum arquivo da suíte o referencia em `c29246c`; o guarda bidirecional é a #57). A verificação é manual no fechamento: `curl` contra o stack local, com a saída registrada no `tasks.md`.

### D11. Revogação da convenção "sem biblioteca JWT", registrada no `01`

A seção "Autenticação" do `01` diz "token stateless assinado com HMAC, sem biblioteca JWT". A frase passa a descrever o estado de **convivência**:

- o HMAC continua emitindo e validando até a #182;
- a convenção de não usar biblioteca JWT é **revogada**, porque emitir OIDC (aqui) e validar token de terceiro (a #208 e os provedores federados) é exatamente o caso em que biblioteca mantida vence implementação própria: rotação, JWKS, discovery e validação de `aud`/`iss`/`exp` são o tipo de código em que defeito vira falha de segurança.

O texto antigo fica citado com a data da revogação, no mesmo formato das correções de status do `02`. O `docs/architecture.md` ganha o emissor no diagrama de autenticação.

## Árvore de pastas proposta

```
apps/api/src/Buteco.Api/
├── Identity/                                   NOVO
│   ├── IdentityIssuerServiceCollectionExtensions.cs   AddIdentityIssuer(config): liga/desliga, OpenIddict, anel, rotação
│   ├── IdentityIssuerOptions.cs                Identity:SigningKeyEncryptionKey
│   ├── IdentityIssuerEndpoints.cs              lista esperada com motivo (D8)
│   ├── IdentityIssuerStartupValidation.cs      ValidateIdentityIssuerConfiguration / ValidateIdentityIssuerEndpoints
│   ├── ClientCredentialsTokenHandler.cs        HandleTokenRequestContext (D2)
│   ├── DiscoveryEndpointsFromIssuerHandler.cs  endpoints do discovery sob o issuer (D8, correção)
│   └── Keys/
│       ├── IdentityKey.cs                      entidade de identity_keys
│       ├── IdentityKeyUsage.cs                 signing | encryption
│       ├── IdentityKeyMaterialCipher.cs        AES-256-GCM do material privado (D4)
│       ├── IdentityKeyFactory.cs               gera RSA 2048 / AES-256 e monta a linha
│       ├── IdentityKeyRing.cs                  snapshot em memória + EnsureLoadedAsync + change token (D6)
│       ├── IdentityKeyRingOptionsConfigurer.cs IConfigureOptions<OpenIddictServerOptions>, X.509 em memória (D5)
│       ├── IdentityKeyRingGate.cs              middleware 503 até carregar (D6)
│       ├── IdentityKeyRotationPolicy.cs        constantes e cálculo de janelas (D7)
│       └── IdentityKeyRotationService.cs       BackgroundService: aquecimento a cada 5 s até carregar (D6), depois tique horário com advisory lock bigint (D7)
├── Infrastructure/
│   ├── AppDbContext.cs                         + DbSet<IdentityKey>, + modelBuilder.UseOpenIddict() com nomes snake_case
│   └── Migrations/<data>_AddIdentityIssuer.cs  NOVO
├── Program.cs                                  AddIdentityIssuer, gate antes de UseAuthentication, duas validações
├── appsettings.json                            "Identity": { "SigningKeyEncryptionKey": "" }
└── appsettings.Development.json                chave de dev documentada como de dev

apps/api/tests/Buteco.Api.Tests/
├── Identity/                                   NOVO
│   ├── IdentityKeyMaterialCipherTests.cs       unidade
│   ├── IdentityKeyRotationPolicyTests.cs       unidade: janelas com FakeTimeProvider
│   ├── IdentityKeyRingSigningOrderTests.cs     unidade: kid da mais nova válida, ordem invertida (D5)
│   ├── IdentityIssuerDiscoveryTests.cs         integração: discovery, JWKS, issuer sem Host
│   ├── IdentityIssuerTokenTests.cs             integração: client_credentials, verificação pelo JWKS, invalid_client, unsupported_grant_type
│   ├── IdentityIssuerCoexistenceTests.cs       integração: HMAC aceito, JWT do emissor recusado em /agents
│   ├── IdentityKeyPersistenceTests.cs          integração: reinício preserva kid, uma chave por uso, material cifrado, chave trocada → 503
│   ├── IdentityKeyRotationTests.cs             integração: publicação antecipada, troca sem reinício, retenção, concorrência
│   ├── IdentityKeyRingGateTests.cs             integração: 503 antes da migração, recupera sem reinício
│   ├── IdentityIssuerDisabledTests.cs          integração: sem url pública (ApiFactoryWithoutPublicUrlFixture), login continua
│   ├── IdentityIssuerStartupValidationTests.cs integração: url inválida, chave ausente, endpoint a mais (composição real)
│   └── IdentityIssuerMigrationTests.cs         integração: tabelas, nomes, sem linha de aplicação
└── Support/
    ├── TestAuthentication.cs                   + Identity:SigningKeyEncryptionKey nos ConfigOverrides
    └── IdentityIssuerTestClient.cs             NOVO: cadastra escopo/cliente de teste depois do MigrateAsync

apps/frontend/deploy/nginx.conf                 + 3 blocos (D10)
Directory.Packages.props                        EF Core 10.0.13; + OpenIddict.Server.AspNetCore, OpenIddict.EntityFrameworkCore 7.7.2
docker-compose.prod.yml                         + Identity__SigningKeyEncryptionKey no apps/api
.env.prod.example                               + IDENTITY_SIGNING_KEY_ENCRYPTION_KEY
docs/configuration.md, docs/deployment.md, docs/architecture.md, 01, 02, CHANGELOG.md
```

Nada vai para `libs/`: há um consumidor só (`apps/api`), e a #208 valida pelo JWKS, por HTTP, sem compartilhar código.

## Risks / Trade-offs

- **[`503` em toda rota até o primeiro acesso ao banco]** → declarado em D6. Em produção o `migrator` precede o `apps/api`, e o aquecimento carrega o anel antes da primeira sonda. Testes afirmam o `503`, a recuperação sem reinício, e o `/health` `200` sem a requisição precisar carregar quando o banco já está migrado. O efeito no healthcheck do compose fica no `docs/deployment.md`.
- **[Subida da EF Core alcança `apps/workers` e `apps/inbox`]** → é patch da linha 10.0. As suítes dos três apps rodam no fechamento contra a baseline de `c29246c`, comparando por nome de teste (convenção 19).
- **[Troca de quem assina depende de recarga de opções em runtime]** → guarda de integração que avança o relógio, roda a verificação e afirma o `kid` novo **no mesmo processo**.
- **[Chave de cifra perdida = todas as chaves perdidas]** → o mesmo regime das credenciais de MCP e de canal. `docs/deployment.md` passa a dizer que trocá-la exige apagar `identity_keys` e aceitar que todo token emitido deixa de validar. Hoje isso não alcança ninguém, porque nada consome o token.
- **[Endpoint de token anônimo exposto sem cliente]** → resposta é sempre `invalid_client`. Sem rate limit aqui, porque não há segredo a adivinhar sem cliente; a #186, que cria clientes, decide o limite.
- **[`DisableTransportSecurityRequirement`]** → o issuer é fixo e público (`https`), e o tráfego interno é a rede do compose, igual ao resto do `apps/api`.
- **[Atraso de até 1 h na troca de chave]** → absorvido pela retenção de 24 h, com a desigualdade afirmada no boot.

## Migration Plan

1. **Deploy:** gerar `IDENTITY_SIGNING_KEY_ENCRYPTION_KEY` (`openssl rand -base64 32`), distinta das outras chaves, e pôr no `.env.prod`. O `migrator` cria as cinco tabelas. O `apps/api` sobe, e o aquecimento (D6) carrega o anel e cria uma chave de cada uso antes da primeira sonda do healthcheck. O nginx do stack passa a rotear os três endereços.
2. **Ordem:** só o `apps/api`, o `migrator` e o `frontend` (por causa do nginx) mudam. `apps/inbox`, `apps/connectors` e `apps/workers` sobem na versão nova pela EF Core, sem mudança de comportamento.
3. **Conferência pós-deploy:** `curl https://<domínio>/.well-known/openid-configuration` e `/.well-known/jwks` devolvem JSON (não HTML), e o login do painel continua funcionando.
4. **Rollback:** a versão anterior ignora as cinco tabelas novas (nenhuma tabela existente muda), e a variável nova é ignorada. Voltar a imagem é suficiente. Apagar as tabelas só se a change for abandonada.

## Open Questions

Nenhuma de produto. As dependências estão verificadas (D1). As decisões de fluxo (D2), de chave (D4, D5) e de rotação (D7) são técnicas e ficam para a revisão deste design.
