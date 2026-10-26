## Context

**Estado atual (`f216338`, merge do PR #213, 24/10/2026).** O `apps/inbox` e o `apps/connectors` têm um esquema de autenticação só, `OperatorTokenAuthenticationHandler`, sobre um `TokenService` HMAC próprio (cópia do `apps/api`, sem `libs/`), com a `Auth:TokenSigningKey` compartilhada. O token HMAC é `base64url(payload).base64url(assinatura)`: **um** ponto. O handler põe o `sub` em `ClaimTypes.NameIdentifier`, e a autorização decide por ele:

- `apps/inbox`: `FallbackPolicy` com `RequireClaim(NameIdentifier, "operator")` (`SubjectAuthorization`, #116); qualquer outro subject recebe `403`.
- `apps/connectors`: `ConnectorsSubjectAuthorizationHandler`, tabela explícita `subject → (método, padrão)`: `operator` em três rotas, `service:api` em "descrever pasta". Conferida no boot nos dois sentidos (`ValidateConnectorsSubjectRoutes`).

Checagens de boot existentes (convenção 8): `ValidateRouteAuthenticationClassification` nos dois, `ValidateDebounceSessionSerializationMaxWait` no `apps/inbox`, `ValidateConnectorsSubjectRoutes` e `ValidateApiConfiguration` no `apps/connectors`. Os dois já falam com o `apps/api` por `Api:BaseUrl` (`http://api:8080` no `docker-compose.prod.yml`): obrigatório no `apps/inbox` (lança antes do `Build()`), opcional no `apps/connectors` (sem ele o ciclo de sincronização não é registrado).

**O que a #179 deixou** (`openspec/changes/archive/2026-10-24-emissor-oidc-api/`):

- issuer = `PublicUrl:BaseUrl` normalizada com barra final; discovery em `/.well-known/openid-configuration`, JWKS em `/.well-known/jwks`, ambos anônimos e com caminho fixo, conferido no boot (`IdentityIssuerEndpoints.Expected`, D8);
- **correção do discovery (D8):** todo endereço do discovery sai do issuer público. Dentro do compose, o `jwks_uri` anunciado é `https://<domínio>/.well-known/jwks`, que o `apps/inbox` só alcançaria saindo para a internet e voltando pelo proxy;
- só `client_credentials`, sem cliente em produção; `sub = client_id`, `aud` = recursos dos escopos (D2); RS256 com `kid`, access token JWT não cifrado, vida de 1 h;
- rotação a cada 90 dias, próxima chave publicada **7 dias antes** de assinar, anterior validando 24 h depois (D7);
- até o anel carregar, **toda** rota do `apps/api` responde `503`, inclusive o JWKS (D6).

**A decisão que esta change implementa** (02, "Decisões fixadas em 23/10/2026"): convivência em três passos. Aqui os dois apps passam a aceitar o token do emissor e **nada é removido**; a #180 emite o token do operador pelo emissor; a #182 remove o HMAC. A regra "HMAC só para serviço" é da #184, não desta.

### Achados da fonte, conferidos em 24/10/2026

Pacotes, no índice e no catálogo do NuGet (`api.nuget.org/v3`):

| pacote | versão | publicado | licença | dependências em `net10.0` | vulnerabilidade |
|---|---|---|---|---|---|
| `Microsoft.AspNetCore.Authentication.JwtBearer` | **10.0.13** (última estável; 11.0 em `rc.2`) | 13/10/2026 | MIT | `Microsoft.IdentityModel.Protocols.OpenIdConnect ≥ 8.19.2` | nenhuma registrada (nem na 8.19.2 da cadeia) |
| `OpenIddict.Validation.AspNetCore` | 7.7.2 | 10/10/2026 | Apache-2.0 | `OpenIddict.Validation` 7.7.2 | nenhuma |
| `OpenIddict.Validation.SystemNetHttp` | 7.7.2 | 10/10/2026 | Apache-2.0 | `Microsoft.Extensions.Http.Polly` **10.0.11**, `Microsoft.Extensions.Http.Resilience` 10.8.0 | nenhuma |

Medido com `dotnet restore --force` do `Buteco.Connectors.Tests` e do `Buteco.Inbox.Tests` no worktree desta change, revertido em seguida (logs em `~/.cache/buteco-agents/208-baseline/pacotes/`):

- **JwtBearer:** 8 pacotes novos no grafo (só `Microsoft.IdentityModel.*` 8.19.2 e `Microsoft.Bcl.Cryptography`); nenhum `NU1605`/`NU1608`, nenhum aviso de vulnerabilidade novo.
- **OpenIddict validation + SystemNetHttp:** 24 pacotes novos, entre eles `Microsoft.Extensions.Http.Polly` 10.0.11, `Polly` 7.2.4 e `Polly.Extensions.Http` 3.0.0. **É a mesma cadeia `Http.Polly` que deu o `NU1605` no `apps/api`** (D1 da #179). Aqui ela não dispara, porque nenhum projeto do grafo do `apps/inbox` e do `apps/connectors` fixa `Microsoft.Extensions.Configuration` 10.0.10 diretamente; dispararia no primeiro que fixasse. O problema de vulnerabilidade da #179 **não** vem: ele entrava por `AspNetCore.DataProtection`, que só o `OpenIddict.Validation.DataProtection` traz.
- O único `NU1903` nos dois logs é o do `SSH.NET` via Testcontainers, já na baseline e já registrado na #146.

`ConfigurationManager<T>` do `Microsoft.IdentityModel.Protocols` 8.23.0 (tag `8.23.0`, `ConfigurationManager.cs`) e `JwtBearerHandler` (tag `v10.0.13`), lidos em 24/10/2026:

| # | fato | onde |
|---|---|---|
| F1 | Sem configuração ainda, `GetConfigurationAsync` busca **na própria requisição**, sob semáforo; se falha, lança `IDX20803` e **não guarda backoff**: a requisição seguinte busca de novo. N requisições simultâneas com o `apps/api` fora fazem N buscas em fila, cada uma até o timeout. | l. 231–340 |
| F2 | Com configuração, a renovação é em segundo plano: passado o `AutomaticRefreshInterval` (+ até 5% de jitter), a requisição dispara `Task.Run` e usa a configuração atual. **Falha na renovação mantém a anterior**, sem prazo. | l. 316–327, 399–428 |
| F3 | `RequestRefresh()` (chamado em `kid` desconhecido) só busca se passou `RefreshInterval` desde a última, ou na primeira vez; mínimo 1 s. A busca também é em segundo plano: **a requisição que encontrou o `kid` desconhecido falha**, e a seguinte já usa as chaves novas. | l. 492–510, 598 |
| F4 | `AutomaticRefreshInterval` não aceita menos que 5 min. | l. 593 |
| F5 | O `JsonWebTokenHandler` engole a falha de obter a configuração (`IDX10261`, só log) e valida sem chaves: o resultado é falha de assinatura, que o `JwtBearerHandler` transforma em `401`. Sem tratamento, "sem chaves" e "token inválido" são indistinguíveis. | `JsonWebTokenHandler.ValidateToken.cs` l. 565–580 |
| F6 | `JwtBearerHandler` chama `RequestRefresh` em `SecurityTokenSignatureKeyNotFoundException` quando `RefreshOnIssuerKeyNotFound` (padrão `true`). | `JwtBearerHandler.cs`, `RecordTokenValidationError` |
| F7 | Com `Authority`/`MetadataAddress`, o `ConfigurationManager` padrão lê o discovery e **segue o `jwks_uri` anunciado**, que é o público. Aceita um `IConfigurationRetriever` próprio passando `ConfigurationManager` pronto nas opções. | `JwtBearerPostConfigureOptions.cs` l. 31–66 |
| F11 | Com `UseLastKnownGoodConfiguration` (padrão `true`), um token que falha por `kid` ausente é revalidado contra a última configuração que validou algum token, por até `LastKnownGoodLifetime` (1 h): uma chave **retirada** do JWKS continua valendo depois de o cache renovar. | `JsonWebTokenHandler.ValidateToken.cs` l. 630–645 e `TokenUtilities.IsRecoverableConfiguration` (8.19.2) |

**Correção na implementação (25/10/2026, grupo 3; convenção 9).** Os fatos F1 a F7 foram lidos na tag `8.23.0`, mas o restore resolve `Microsoft.IdentityModel.*` **8.19.2**, a mínima declarada (D1). Lido de novo na `8.19.2`: o `ConfigurationManager.cs` difere da `8.23.0` só por um campo que guarda a última exceção do caminho bloqueante, sem efeito no caminho não bloqueante que esta change usa. F1 a F7 valem para a versão resolvida. O F11 foi achado nessa leitura, e não estava no design.

OpenIddict 7.7.2 (tag `7.7.2`, `openiddict-core`):

| # | fato | onde |
|---|---|---|
| F8 | O access token **traz** `client_id` igual a `context.ClientId`, posto pelo próprio `PrepareAccessTokenPrincipal` (RFC 9068), em todo fluxo. O apresentador vai também em `oi_prst`, claim privado do OpenIddict. **Correção na implementação:** a primeira versão deste fato dizia o contrário ("não traz `client_id`"); a leitura tinha parado antes da linha que o põe. Medido no payload real sem nenhum handler nosso e conferido na fonte. | `OpenIddictServerHandlers.cs` l. 3722–3724 (tag 7.7.2); `oi_prst`: l. 3279–3281 e 3631–3640 |
| F9 | O cabeçalho do access token sai com `typ` `at+jwt`. | `Protection.cs` l. 228–232 |
| F10 | `aud` sai como texto quando há um recurso e como array quando há vários. | `Protection.cs`, `AttachTokenMetadata` |

## Goals / Non-Goals

**Goals:**

- Um access token do emissor, com a `aud` do app, é aceito numa rota autenticada do `apps/inbox` e do `apps/connectors`, e um token HMAC continua aceito **exatamente** como hoje (critério da #208).
- Chaves obtidas só pelo endereço interno, sem internet, com o issuer público conferido.
- Os dois apps sobem e atendem HMAC com o `apps/api` fora do ar; o token do emissor tem comportamento definido em cada estado das chaves.
- Token de outro app, de outro issuer, expirado, de chave desconhecida ou com `sub` forjado é recusado.
- Testes sem o `apps/api` real para a lógica de cada app, e um teste de acordo com o `apps/api` real por app (convenção 11).

**Non-Goals:**

- **Remover o HMAC** ou a `Auth:TokenSigningKey` (#182). **Recusar HMAC `operator`** (#184).
- **Usuário, login e o cliente do painel** (#180). Esta change fixa o `client_id` e as audiences que a #180 vai usar, mas não cadastra nada no emissor.
- **Papéis** (#184) e **aplicações externas** (#186).
- **O `apps/api` aceitar o token do emissor** nas próprias rotas (#180).
- **Escopo OAuth como autorização.** O escopo só determina a `aud` (D2 da #179); quem decide rota é a tabela de subjects.

## Decisions

### D1. `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.13, com `ConfigurationManager` próprio

**Escolhido:** o esquema `JwtBearer` do ASP.NET Core, versão 10.0.13, centralizado no `Directory.Packages.props`, referenciado por `apps/inbox` e `apps/connectors`. A cadeia `Microsoft.IdentityModel.*` fica na mínima declarada (8.19.2), sem fixar central, porque nenhuma vulnerabilidade está registrada nela e fixar seria versão sem consumidor (convenção 2).

O `ConfigurationManager<OpenIdConnectConfiguration>` é **montado por nós** e passado em `JwtBearerOptions.ConfigurationManager` (F7), com um `IConfigurationRetriever` próprio (D3). Ficam da biblioteca: a validação (`JsonWebTokenHandler`), o cache, a renovação em segundo plano, a manutenção da última configuração boa e o limite de frequência da busca por `kid` desconhecido (F2, F3, F6).

| opção | por que não |
|---|---|
| `OpenIddict.Validation.AspNetCore` + `SystemNetHttp` 7.7.2 | traz a cadeia `Http.Polly` que a #179 tirou do `apps/api` (medido acima: 24 pacotes, `Polly` 7 e 8 juntos); o retriever dele segue o `jwks_uri` do discovery, então o problema do endereço interno exigiria o mesmo trabalho próprio; e casaria o lado que valida com a biblioteca do lado que emite, quando o JWKS é justamente o contrato que os separa |
| `JsonWebTokenHandler` direto, com cache de JWKS próprio | reimplementar o cache, a renovação e o limite de frequência que o `ConfigurationManager` já tem, e mais um esquema de autenticação próprio; é o tipo de código que a revogação da convenção "sem biblioteca JWT" (D11 da #179) quis evitar |

### D2. Escolha do esquema pela forma do token, sem queda de um no outro

Um esquema de política (`AddPolicyScheme`, nome `Bearer`) vira o padrão de autenticação dos dois apps. O `ForwardDefaultSelector` olha só o `Authorization`:

- `Bearer` com **exatamente dois pontos** (JWS compacto, três segmentos) → esquema `IssuerToken` (JwtBearer);
- qualquer outra coisa, inclusive sem header → `OperatorToken` (HMAC), **sem nenhuma mudança** no handler, no `TokenService` nem nas mensagens.

O seletor decide **antes** de validar, e o esquema escolhido é o único que roda. Um JWT inválido recebe `401` do `IssuerToken` sem passar pelo HMAC; um token HMAC inválido recebe `401` do HMAC sem tocar nas chaves do emissor.

Por que pela contagem de pontos: o HMAC tem um ponto por construção (`TokenService.Issue`), e o `Validate` dele já recusa três segmentos (o segundo pedaço com ponto não decodifica). As duas formas não se sobrepõem, e o seletor não depende de decodificar nada. A alternativa, tentar os dois esquemas em sequência, é exatamente a queda de um no outro que a issue proíbe: um defeito de validação num esquema seria mascarado pelo outro.

O desafio (`401`) e a recusa (`403`) também são encaminhados pelo seletor. Rotas anônimas (`/health`, webhooks, push) não mudam: autenticação que falha não as afeta, porque nenhuma delas desafia.

### D3. Chaves pelo endereço interno; issuer público como valor fixo

**Configuração nova, nos dois apps:** `Identity:Issuer`, o issuer público, normalizado como o `apps/api` normaliza (`TrimEnd('/') + "/"`). No compose de produção, `Identity__Issuer: ${PUBLIC_DOMAIN}`, a mesma variável que vira `PublicUrl__BaseUrl` do `apps/api`.

**`IssuerKeysRetriever : IConfigurationRetriever<OpenIdConnectConfiguration>`**, a cada busca:

1. `GET {Api:BaseUrl}/.well-known/openid-configuration`, pelo `HttpClient` nomeado `IssuerKeys` (timeout de 5 s, o mesmo do `AgentReferenceValidator`);
2. confere o campo `issuer` contra `Identity:Issuer`, por igualdade ordinal. **Diferente:** a busca falha com log de erro que nomeia os dois valores. É o defeito de configuração mais provável desta change (domínio do `.env` diferente entre os apps, ou `PublicUrl:BaseUrl` sem o esquema), e sem isso ele apareceria como "todo token recusado";
3. `GET {Api:BaseUrl}/.well-known/jwks`, pelo mesmo cliente, e monta a configuração com `Issuer = Identity:Issuer` e as chaves desse JWKS.

**O `jwks_uri` do discovery não é seguido.** O caminho do JWKS é fixo pela spec `oidc-issuer` e conferido no boot do `apps/api` (`IdentityIssuerEndpoints.Expected`), então montá-lo sobre o endereço interno não depende de nada que o discovery anuncie. Ler o discovery serve só para conferir o issuer.

O `HttpClient` vem do `IHttpClientFactory` a cada busca, e não fica preso no `ConfigurationManager`: o endereço do contêiner do `apps/api` muda em cada recriação, e o factory renova o handler.

**Desenvolvimento:** `appsettings.Development.json` dos dois com `Identity:Issuer = http://localhost:5017/` e `Api:BaseUrl = http://localhost:5017`, que são o perfil `http` e a url pública de desenvolvimento do `apps/api`. **Testes:** o handler primário do cliente `IssuerKeys` é trocado na fixture (D9), no mesmo molde em que o `ApiConnectorsRoundTrip.Tests` já troca o do ciclo de sincronização. Nenhum dos três ambientes sai para a internet.

### D4. Ciclo de vida das chaves: o app sobe sem o `apps/api`

**No boot:** nenhuma I/O. O esquema é registrado, o `ConfigurationManager` é construído vazio, e o app sobe com o `apps/api` fora do ar. O HMAC não depende de nada disto: o seletor (D2) nunca encaminha a ele um token que precise de chave do emissor.

**`IssuerKeysRefreshService`, um `BackgroundService`**, com os intervalos num `IssuerKeysSchedule` registrado no DI (idioma do `KnowledgeIndexingRequestSchedule`, que os testes substituem). Constantes de produção, sem opção de configuração (convenção 2):

| constante | valor | por quê |
|---|---|---|
| tentativa até a primeira obtenção | 10 s | tira a primeira busca do caminho da requisição, como o aquecimento do D6 da #179 |
| tique depois de obtidas | 10 min | chama `GetConfigurationAsync`, que dispara a renovação quando o cache passou do intervalo automático (F2). Sem o tique, a renovação dependeria de tráfego |
| intervalo automático do `ConfigurationManager` | 1 h | idade máxima do cache, com o `apps/api` respondendo: 1 h + 3 min de jitter + 10 min de tique ≈ **1h15**, abaixo dos 7 dias de publicação antecipada com folga de duas ordens de grandeza |
| intervalo mínimo da busca por `kid` desconhecido | 1 min | `RefreshInterval` (F3): no máximo uma busca por minuto por instância, por mais `kid`s inventados que cheguem |
| espera entre buscas falhas | 10 s | ver abaixo |

Cada iteração do serviço captura a própria falha e só loga (convenção 4); falha de busca é `Warning` com o endereço e o motivo.

**Espera entre buscas falhas.** O `ConfigurationManager` não tem backoff antes da primeira obtenção (F1). O `IssuerKeysRetriever` guarda o instante da última falha e, dentro de 10 s dela, falha **na hora**, sem HTTP. Com o `apps/api` fora, uma rajada de tokens do emissor custa no máximo uma busca a cada 10 s, e cada requisição responde em microssegundos em vez de esperar o timeout.

**Token do emissor enquanto as chaves nunca foram obtidas:** `503 Service Unavailable` com `Retry-After: 10`. O `IssuerKeysRetriever` expõe se alguma busca já deu certo (`IssuerKeysState.Obtained`); no `OnChallenge` do `IssuerToken`, sem chave obtida, o desafio vira `503`. É o que distingue "não sei validar ainda" de "token inválido" (F5): um `401` mandaria o painel da #180 deslogar o usuário por uma indisponibilidade passageira do `apps/api`. Com chave obtida, falha de validação é sempre `401`.

**`apps/api` que cai depois da primeira obtenção:** o cache continua valendo (F2) e os tokens continuam validando. Não há prazo de expiração do cache: a chave só sai do JWKS 24 h depois de parar de assinar, e um token assinado por ela expira em 1 h; manter a chave por mais tempo não aceita token que o emissor não emitiu.

**`kid` desconhecido:** a requisição recebe `401` e dispara a busca (F3, F6); a seguinte já valida. Com a publicação antecipada de 7 dias e o cache de 1h15, isso só acontece fora da operação normal (por exemplo, `identity_keys` apagada no `apps/api`, o caso do `docs/deployment.md` da #179). Declarado como risco.

**Correção na implementação (25/10/2026, tarefas 3.2–3.3; convenção 9).** Quatro pontos que o design não fixava, ou fixava diferente:

- **`UseLastKnownGoodConfiguration = false`** no `ConfigurationManager` (F11). Sem isso, o cenário "Chave rotacionada" da spec não vale: o token da chave retirada continuaria `200` por até 1 h. A manutenção das chaves quando a renovação falha (F2) não depende disto. Guardado por `IssuerKeysLifecycleTests.RotatedKey_*`, visto reprovar com `true`.
- **O log não ecoa o valor de `Api:BaseUrl`.** O design pedia "`Warning` com o endereço e o motivo". O `apps/connectors` nunca ecoa esse valor (`ValidateApiConfiguration`: o endereço pode carregar credencial), e as duas cópias seguem esse cuidado: o log cita a chave `Api:BaseUrl`, o caminho e o motivo. O erro de issuer divergente continua trazendo os dois issuers, que são públicos.
- **Linha `Information` de sucesso**, com os `kid`s obtidos (distintos: o JWKS do OpenIddict traz `n`/`e` e `x5c`, e `GetSigningKeys` devolve uma chave para cada). O design não previa log de sucesso, e a conferência da 9.2 e a pós-deploy dependem dele.
- **JWKS sem chave de assinatura conta como falha de busca**, e `Obtained` continua falso: um JWKS vazio daria `401` a todo token, em vez do `503` de "sem chave".
- **O `Retry-After` vem de `IssuerKeysSchedule.FailedFetchBackoff`** (10 s em produção), o intervalo depois do qual uma nova busca é possível.

**Algoritmo, tipo e relógio:** `ValidAlgorithms = [RS256]` (D4 da #179), `ValidTypes = [at+jwt]` (F9; recusa um id_token do mesmo emissor que a #180 vier a emitir), `RequireExpirationTime`, `RequireSignedTokens`, tolerância de relógio de 30 s em vez dos 5 min padrão (os quatro processos rodam no mesmo host). `MapInboundClaims = false`, para nenhum claim ser renomeado em silêncio (D6).

### D5. Uma `aud` por app

| app | `aud` exigida |
|---|---|
| `apps/inbox` | `buteco-inbox` |
| `apps/connectors` | `buteco-connectors` |
| `apps/api` (#180) | `buteco-api` |

Constante no código de cada app (`IssuerTokenAudience`), não configuração: é contrato entre o emissor e o app, e não varia por ambiente (convenção 2).

**Uma por app, e não uma comum**, porque o critério "token emitido para outro app é recusado" só existe com valores distintos. Com uma `aud` comum, um token de serviço que a #182 emitir para o `apps/api` chamar o `apps/connectors` seria aceito pelo `apps/inbox`, e a única barreira seria a tabela de subjects, que a convenção do `01` diz proteger "contra defeito de código, não contra quem guarda a chave".

**Como isso se encaixa adiante:**

- **painel (#180):** o mesmo token vale para os três apps porque carrega as três audiences. É o idioma de recurso por escopo do D2 da #179: a #180 cadastra um escopo por app (recurso `buteco-api`, `buteco-inbox`, `buteco-connectors`), e o token do painel pede os três. A `aud` vira array (F10), e cada app exige só a sua.
- **serviço (#182):** cada token de serviço pede só o escopo do app que chama. O `service:api` chamando o `apps/connectors` leva só `buteco-connectors`; o mesmo token mandado ao `apps/inbox` recebe `401`.

### D6. O principal do emissor entra pela tabela de clientes, nunca pelo `sub`

Hoje não há usuário (#180) nem aplicação (#186); o único token que o emissor emite é `client_credentials` de cliente cadastrado à mão, com `sub = client_id`. O que o principal **representa aqui** é decidido pelo **cliente**, e só por ele:

- **`apps/api` não muda: o access token já leva `client_id`** (RFC 9068, §2.2), posto pelo OpenIddict em todo fluxo (F8), então o fluxo de usuário da #180 também sai com o claim. Os dois apps leem `client_id`, e não `oi_prst`, que é claim privado do OpenIddict. O `IdentityIssuerTokenTests` do `apps/api` ganha um teste de contrato que afirma `client_id` e `typ` `at+jwt`: um upgrade do OpenIddict que tire o claim reprova ali, e não vira `403` nos dois apps.

  **Correção na implementação (convenção 9):** a primeira versão desta decisão acrescentava um `AccessTokenClientIdHandler` no `apps/api`, a partir do F8 errado. Ao ver o teste reprovar com o registro do handler comentado (convenção 15), ele continuou passando: o payload sem o handler já tinha `"client_id":"cliente-teste"`, e a fonte da 7.7.2 põe o claim em `PrepareAccessTokenPrincipal` (`OpenIddictServerHandlers.cs` l. 3722–3724). O handler foi retirado. O teste ficou como contrato e foi visto reprovar uma vez com um handler temporário, ordenado logo depois do `PrepareAccessTokenPrincipal`, que retirava o claim do `AccessTokenPrincipal`; o handler temporário foi desfeito.
- **Tabela fechada por app, `IssuerClientSubjects`**, `client_id` → subject **já existente** na tabela do app:

  | app | `client_id` | subject |
  |---|---|---|
  | `apps/inbox` | `buteco-painel` | `operator` |
  | `apps/connectors` | `buteco-painel` | `operator` |

- No `OnTokenValidated`, o principal é **reconstruído**: `NameIdentifier` = o subject da tabela, se o `client_id` estiver nela; nenhum `NameIdentifier`, se não estiver. O `sub` e o `client_id` ficam como claims próprios, para log. **`SubjectAuthorization` e `ConnectorsSubjectAuthorizationHandler` não mudam**: recebem um `NameIdentifier` do vocabulário de sempre, ou nenhum, e aí `403`.

**Por que não o `sub`:** o `sub` do emissor é o `client_id` em `client_credentials` e será o id do usuário na #180. Pôr o `sub` em `NameIdentifier` faria um cliente cadastrado com id `operator`, ou um usuário cujo id fosse `operator`, virar o operador. Com a tabela, um token do emissor com `sub` `operator` e cliente fora da tabela recebe `403`, e há teste para isso.

**Por que `buteco-painel → operator` já agora:** é o único consumidor dos dois apps que a M1 traz, e o critério da issue pede um token do emissor **aceito**. Na M1 todo usuário tem o acesso do operador; os papéis são da M2.

**O que fica decidido agora:** o `client_id` do painel é `buteco-painel`, e as audiences são as do D5.

**O que fica para depois:**

- **#180:** cadastrar o cliente `buteco-painel` como **público** (authorization code + PKCE, sem segredo, sem `client_credentials`), para que um token com esse `client_id` só possa vir de um usuário; pedir os três escopos; aceitar o token no `apps/api` com a `aud` `buteco-api`. O teste de acordo da #180 prova os valores dos dois lados.
- **#182:** os clientes de serviço (`service:api` → `apps/connectors`) entram na tabela de clientes do `apps/connectors`, apontando para `service:api`.
- **#184:** o papel no token passa a ser checado por rota, e a entrada `buteco-painel → operator` dá lugar à matriz de papéis. A tabela de clientes continua dizendo de **onde** veio o token.
- **#186:** cliente de aplicação externa não entra em nenhuma das duas tabelas: aplicação externa fala com o `apps/api`, e receber `403` aqui é o comportamento certo.

### D7. Checagens de boot (convenção 8)

**`ValidateIssuerTokenConfiguration()`**, depois do `Build()`, nos dois apps (o bundle de migration do `apps/inbox` roda o `Program.cs` até o `Build()`; D1 da #179):

- **`Identity:Issuer` ausente ou vazio:** o esquema `IssuerToken` não é registrado (decidido no registro, só lendo configuração, sem lançar), o seletor manda tudo para o HMAC, e o boot loga `Warning` "Validação do token do emissor desligada: Identity:Issuer não configurado". É o mesmo tratamento do emissor sem url pública (D8 da #179).
- **presente e não absoluto `http`/`https`, ou com query ou fragmento:** o boot falha nomeando `Identity:Issuer`.
- **presente e `Api:BaseUrl` ausente** (só alcançável no `apps/connectors`, onde ele é opcional): o boot falha nomeando as duas chaves. Sem endereço interno não há de onde ler as chaves.

**`ValidateIssuerClientSubjects()`**, pura, nos dois apps e nos dois sentidos possíveis: todo subject de destino da `IssuerClientSubjects` existe na tabela de subjects do app (`SubjectAuthorization.OperatorSubject` no `apps/inbox`; as chaves de `ConnectorsSubjectAuthorizationHandler.SubjectRoutes` no `apps/connectors`). Ela pega quem renomear um subject ou acrescentar um cliente apontando para subject sem rota. O sentido inverso (todo subject tem cliente) não vale: o HMAC continua com subjects que nenhum cliente usa.

**Correção na implementação (25/10/2026, tarefa 4.6; convenção 9): nos testes, `Identity:Issuer` vai por `UseSetting`.** O `AddIssuerTokenAuthentication` decide o registro lendo a configuração do builder, antes do `Build()`, como o D8 da #179; a `WebApplicationFactory` aplica o `ConfigureAppConfiguration` só no `Build()`. Medido: com o valor só no `ConfigureAppConfiguration`, o registro enxergava o `Identity:Issuer` do `appsettings.Development.json`, e o teste "sem `Identity:Issuer`" recebia `503` em vez de `401`. A premissa do registro vale em produção, onde a configuração do builder é a efetiva. O ajuste é só de suporte de teste, no idioma que o `ConnectorsFactory` e o `ApiFactoryFixture` já usam: `TestAuthentication.UseIssuer` no `apps/inbox` desliga o emissor em toda fixture existente, e o `ConnectorsFactory` fixa `Identity:Issuer` vazio por padrão, ao lado do `Api:BaseUrl`.

**Teste da composição real**, por app: uma `WebApplicationFactory` com `Identity:Issuer = issuer.exemplo.test` (sem esquema) afirma que o boot falha citando `Identity:Issuer`. **Remover a chamada do `Program.cs` reprova esse teste.** O teste é verificado reintroduzindo exatamente esse defeito (convenção 15).

### D8. O princípio do `01`, reescrito

Hoje: "`apps/api` e `apps/inbox` validam **localmente**, compartilhando apenas a chave de assinatura via configuração — nenhuma chamada de rede entre os processos para validar token." Passa a:

> **Nenhuma requisição espera o `apps/api` para ser autenticada.** O token HMAC é validado localmente pela chave compartilhada, como antes. O token do emissor é validado localmente pelas chaves públicas do JWKS, que cada app obtém do `apps/api` pelo endereço interno **fora do caminho da requisição**: em segundo plano depois do start, renovadas a cada hora, e de novo diante de um `kid` desconhecido, no máximo uma vez por minuto. Com o `apps/api` fora do ar, o app sobe e o HMAC funciona; o token do emissor valida com as chaves já obtidas, e recebe `503` se nenhuma foi obtida ainda.

O texto antigo fica citado com a data, no formato das correções do `02`. O mesmo parágrafo passa a nomear o `apps/connectors`, que o texto atual omite.

### D9. Testes de integração sem o `apps/api` real

**`TestIssuer`**, em `Support/` de cada suíte (cópia, sem `libs/`):

- gera chaves RSA 2048 com `kid` próprio; emite access tokens com `JsonWebTokenHandler`, `typ` `at+jwt`, com `iss`, `aud`, `client_id`, `sub`, `exp` e `kid` controláveis;
- serve o discovery e o JWKS por um `HttpMessageHandler` posto como handler primário do cliente `IssuerKeys`; conta as requisições, pode publicar e retirar chaves e pode ficar "fora do ar" (lança `HttpRequestException`) ou responder `503`.

A fixture sobe o app com `Identity:Issuer` e `Api:BaseUrl` do `TestIssuer` e um `IssuerKeysSchedule` de intervalos curtos. No `apps/connectors`, o `Api:BaseUrl` liga o ciclo de sincronização, e o cliente dele recebe o `FakeSyncApiHandler` que a suíte já tem.

Cenários de cada app (na spec `issuer-token-validation`): token válido do painel (`200`); cliente fora da tabela, `sub` `operator` forjado e sem `client_id` (`403`); `aud` de outro app, issuer errado, expirado, `alg` diferente de RS256, `typ` diferente de `at+jwt` (`401`); `kid` desconhecido (`401`, uma busca, e o seguinte `200`); dois `kid`s desconhecidos dentro do intervalo (uma busca só); chave rotacionada (a anterior continua valendo enquanto publicada e é recusada depois de retirada e renovado o cache); `apps/api` fora no boot (app sobe, `/health` `200`, HMAC `200`, emissor `503` com `Retry-After`, e `200` sem reiniciar quando ele volta); `apps/api` fora depois da obtenção (emissor `200`); issuer do discovery diferente do configurado (`503` e log com os dois valores); token com forma de HMAC (nenhuma busca de chave); JWT inválido (o `ITokenService` do HMAC não é chamado); `Identity:Issuer` vazio (emissor desligado, HMAC `200`, aviso no log).

**Teste de acordo com o `apps/api` real (convenção 11)**, um em cada projeto de `tests/`: o `apps/api` sobe com o emissor ligado, a fixture cadastra **depois do `MigrateAsync`** um escopo de teste com recurso `buteco-inbox` (ou `buteco-connectors`) e um cliente confidencial `buteco-painel`, pede o token por `client_credentials` e o manda ao outro app, cujo cliente `IssuerKeys` aponta para o `TestServer` do `apps/api`. Prova, com o artefato real: `typ`, `kid`, `iss` com barra final, `client_id`, `aud` e o caminho interno. O cliente confidencial `buteco-painel` existe só no teste: em produção, a #180 o cadastra público. **Independência:** depois da primeira chamada, o handler do `apps/api` passa a falhar e uma segunda chamada ao outro app continua `200`. Um token só com `buteco-inbox` mandado ao `apps/connectors` recebe `401`.

**Correção na implementação (25/10/2026, grupos 4, 6 e 7; convenção 9).**

- O `apps/connectors` usa o `ManualTimeProvider` que a suíte dele já tem (com timers), e não o `FakeTimeProvider`: o comentário da casa é "sem pacote novo". O `apps/inbox` passou a referenciar `Microsoft.Extensions.TimeProvider.Testing` 10.9.0, já central e já usado pelo `apps/api` e pelo `apps/workers`.
- Os testes de unidade do `IssuerKeysRefreshService` ficam no mesmo arquivo do `IssuerKeysRetrieverTests`, em cada app.
- Os dois fixtures de `tests/` passam a fixar a url pública do `apps/api` (`https://api.exemplo.test`, por `UseSetting` e configuração) e a chave de cifra do emissor, em vez de herdar o `appsettings.Development.json`. O app que valida recebe essa url **sem** a barra final como `Identity:Issuer`, como o compose faz com `PUBLIC_DOMAIN`, e o token chega com ela: o acordo prova a normalização. O cliente `IssuerKeys` passa por um `IssuerKeysGate`, que o teste fecha para simular o `apps/api` inalcançável. Os testes que já existiam nos dois projetos passaram com isso (12/12 e 8/8, cada um com o teste novo).

### D10. Nada em `libs/`

São dois consumidores, com nove arquivos pequenos cada (árvore abaixo), e a maior parte é configuração do `JwtBearer`. A convenção 2 espera 2–3 consumidores **e** código cuja duplicação custe; aqui o que diverge entre os apps é justamente o que importa (a `aud` e a tabela de clientes). E a casa já duplica de propósito o `TokenService` e o `OperatorTokenAuthenticationHandler` entre os apps, porque uma `libs/` de autenticação passaria a ser código comum das fronteiras de segurança de apps que o monorepo isola.

O terceiro consumidor aparece na #180 (o `apps/api` validando o próprio token). É lá que a pergunta volta, com três cópias à vista; fica registrada no `02` como herança para a #180.

## Árvore de pastas proposta

```
apps/inbox/src/Buteco.Inbox/
├── Auth/
│   ├── IssuerTokenAuthentication.cs      NOVO: AddIssuerTokenAuthentication(config): esquema de política Bearer (D2),
│   │                                           JwtBearer IssuerToken, ConfigurationManager próprio, eventos (503, principal)
│   ├── IssuerTokenAudience.cs            NOVO: "buteco-inbox" (D5)
│   ├── IssuerTokenOptions.cs             NOVO: Identity:Issuer
│   ├── IssuerClientSubjects.cs           NOVO: buteco-painel → operator (D6)
│   ├── IssuerKeysRetriever.cs            NOVO: discovery + JWKS internos, issuer conferido, espera entre falhas (D3, D4)
│   ├── IssuerKeysState.cs                NOVO: Obtained (D4)
│   ├── IssuerKeysSchedule.cs             NOVO: constantes de intervalo (D4)
│   ├── IssuerKeysRefreshService.cs       NOVO: BackgroundService de obtenção e tique (D4)
│   ├── IssuerTokenStartupValidation.cs   NOVO: ValidateIssuerTokenConfiguration, ValidateIssuerClientSubjects (D7)
│   ├── OperatorTokenAuthenticationHandler.cs   sem mudança
│   └── SubjectAuthorization.cs                 sem mudança
├── Program.cs                            AddIssuerTokenAuthentication no lugar do AddAuthentication, as duas validações
├── appsettings.json                      "Identity": { "Issuer": "" }
└── appsettings.Development.json          Identity:Issuer de desenvolvimento

apps/inbox/tests/Buteco.Inbox.Tests/
├── Support/
│   ├── TestIssuer.cs                     NOVO: chaves, emissão, handler de discovery/JWKS (D9)
│   └── IssuerTokenFactory.cs             NOVO: WebApplicationFactory com o TestIssuer
├── IssuerTokenSchemeSelectionTests.cs    NOVO: forma do token → esquema, sem queda; HMAC intacto
├── IssuerTokenValidationTests.cs         NOVO: aud, iss, exp, alg, typ, client da tabela, sub forjado
├── IssuerKeysLifecycleTests.cs           NOVO: kid desconhecido, limite de frequência, rotação, apps/api fora, 503, issuer divergente
├── IssuerKeysRetrieverTests.cs           NOVO: unidade: conferência do issuer, espera entre falhas
├── IssuerKeysRefreshServiceTests.cs      NOVO: unidade com FakeTimeProvider: tentativa até obter, tique
└── IssuerTokenStartupValidationTests.cs  NOVO: desligado com aviso, issuer inválido, composição real, tabela de clientes

apps/connectors/src/Buteco.Connectors/
├── Auth/                                 os mesmos nove arquivos novos, com "buteco-connectors" e a tabela do connectors
├── Program.cs                            idem ao apps/inbox
├── appsettings.json                      "Identity": { "Issuer": "" }
└── appsettings.Development.json          Identity:Issuer de desenvolvimento

apps/connectors/tests/Buteco.Connectors.Tests/
├── Support/TestIssuer.cs, Support/ConnectorsFactory.cs (+ variante com o TestIssuer)
└── os mesmos seis arquivos de teste, com as rotas do connectors (operator e service:api)

apps/api/tests/Buteco.Api.Tests/Identity/IdentityIssuerTokenTests.cs   + contrato: client_id e typ at+jwt no token (D6, F8)

tests/InboxOrchestratorRoundTrip.Tests/IssuerTokenRoundTripTests.cs     NOVO: acordo api → inbox (D9)
tests/ApiConnectorsRoundTrip.Tests/IssuerTokenRoundTripTests.cs         NOVO: acordo api → connectors, aud cruzada
tests/*/Support/RoundTripFixture.cs        emissor ligado no apps/api, cliente IssuerKeys apontando para o TestServer

Directory.Packages.props                  + Microsoft.AspNetCore.Authentication.JwtBearer 10.0.13
docker-compose.prod.yml                   + Identity__Issuer no inbox e no connectors
docs/configuration.md, docs/deployment.md, docs/architecture.md, 01, 02, CHANGELOG.md
```

## Risks / Trade-offs

- **[Configuração divergente entre o `Identity:Issuer` dos apps e a `PublicUrl:BaseUrl` do `apps/api`]** → as duas saem da mesma variável (`PUBLIC_DOMAIN`) no compose; a divergência é detectada na obtenção (D3), com log que nomeia os dois valores, e afirmada por teste.
- **[A primeira requisição com `kid` desconhecido recebe `401`]** (F3) → só fora da operação normal (D4); a seguinte valida. Afirmado por teste. A alternativa (o switch `UpdateConfigAsBlocking` do IdentityModel) bloqueia todas as requisições do processo durante a busca, e é global ao processo.
- **[Cache sem prazo com o `apps/api` fora]** → a chave retirada do JWKS só valida token ainda não expirado (1 h), que o emissor emitiu de fato. Não há revogação de chave hoje; quando houver, é ela que muda isto.
- **[`503` para o token do emissor até a primeira obtenção]** → só no intervalo entre o start e a primeira busca bem-sucedida (10 s depois de o `apps/api` responder). Nenhum token do emissor circula até a #180. Afirmado por teste, com a recuperação sem reinício.
- **[`buteco-painel → operator` antes de existir o cliente]** → em produção nenhum cliente é cadastrado até a #180, que o cadastra público (D6). O teste de acordo cadastra um confidencial só no banco do teste.
- **[Nove arquivos copiados entre dois apps]** → D10; o teste de acordo de cada app prova cada cópia contra o emissor real.
- **[Os dois apps dependem do `client_id` que o OpenIddict põe sozinho]** (F8) → o teste de contrato do `apps/api` reprova se um upgrade o tirar; o teste de acordo de cada app (D9) prova o claim com o artefato real.
- **[Tolerância de relógio de 30 s]** → os processos rodam no mesmo host; o relógio atrasado é da VM do Podman, não do host de produção, e a suíte roda tudo no mesmo processo.

## Migration Plan

1. **Deploy:** nenhuma variável nova no `.env.prod`. O `docker-compose.prod.yml` passa `Identity__Issuer: ${PUBLIC_DOMAIN}` ao `inbox` e ao `connectors`. Sem migration.
2. **Ordem:** `inbox` e `connectors`, em qualquer ordem; o `apps/api` não muda em código de produção. Como nenhum token do emissor circula até a #180, qualquer ordem funciona.
3. **Conferência pós-deploy:** o log do `inbox` e do `connectors` não tem o aviso de "validação desligada" nem erro de issuer divergente, e o painel continua funcionando com HMAC.
4. **Rollback:** voltar as três imagens. A variável nova é ignorada pela versão anterior.

## Open Questions

Nenhuma de produto. As versões e licenças estão verificadas (Context); as decisões D2 a D7 são técnicas e ficam para a revisão deste design. Duas delas fixam contrato com a #180 e merecem atenção na revisão: o `client_id` `buteco-painel` e as três audiences (D5, D6).
