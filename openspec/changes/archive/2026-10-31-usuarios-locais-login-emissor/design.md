## Context

**Estado atual (`3bb8236`, merge do PR #216, 27/10/2026).** Três peças da M1 já estão em `main`:

- **#179, emissor OIDC no `apps/api`:**
  - OpenIddict 7.7.2, só o servidor;
  - discovery, JWKS e `/connect/token`;
  - só `client_credentials`, sem cliente em produção;
  - anel de chaves em memória, com `503` até carregar;
  - lista esperada de endpoints conferida no boot;
  - `DiscoveryEndpointsFromIssuerHandler` monta todo endpoint do discovery a partir do issuer.
- **#208, validação pelo JWKS:**
  - o `apps/inbox` e o `apps/connectors` aceitam o token do emissor pelo JWKS, com `aud` por app;
  - o principal sai do `client_id`, por tabela fechada (`buteco-painel` → `operator`);
  - o esquema é escolhido pela forma do token, ao lado do HMAC.
- **#209, armazenamento e limpeza das entradas de token:**
  - o access token de `client_credentials` não grava linha;
  - a limpeza roda de hora em hora;
  - o boot falha se a idade mínima da limpeza (14 dias) for menor que a duração do refresh token.

**O login de hoje:**
- `POST /auth/login` compara `Auth:OperatorUsername` e verifica `Auth:OperatorPasswordHash`, que é PBKDF2-HMAC-SHA256 com 100.000 iterações gerado offline (`docs/development.md`);
- emite um HMAC `operator` de 30 minutos;
- o painel guarda o token em `sessionStorage` (`src/auth/token.ts`), e cada uma das 7 cópias do `request<T>` o anexa e manda para `/login` no `401`;
- o painel chama o `apps/api` direto em `VITE_API_BASE_URL`, com CORS (`Cors:AllowedOrigins`), e o `vite.config.ts` não tem proxy;
- em produção, tudo sai pela mesma origem, pelo nginx do stack.

**Heranças registradas na #180,** no corpo, nos comentários, nos `02` da #208 e da #209 e nos designs arquivados. Cada uma tem decisão abaixo e está marcada com `(herança)`:

| herança | de onde | decisão |
|---|---|---|
| cliente público `buteco-painel`: code + PKCE, sem segredo, sem `client_credentials` | #208 D6 | D3.1 |
| três escopos com recursos `buteco-api`, `buteco-inbox` e `buteco-connectors`; o painel pede os três | #208 D5 | D3.2 |
| o `apps/api` aceita o próprio token com `aud` `buteco-api`; a pergunta do `libs/` volta | #208 D10 | D3.3 |
| `PublicUrl:BaseUrl` obrigatória, e como fica o desenvolvimento | #180, comentário de 24/10 | D3.4 |
| `/connect/authorize` e o encerramento de sessão fora do desvio de navegação, com a conferência por curl | #180, comentário de 24/10 | D3.5 |
| endpoints novos no discovery sob o issuer e na lista esperada do boot | #179 D8 | D3.6 |
| durações: refresh ≤ 14 dias, ou subir a `MinimumAge` | #209 D3 | D3.7 |
| se o access token do usuário continua gravado | #209 D8 | D3.8 |
| `id_token_hint` com mais de 14 dias | #209 D8 | D3.9 |
| encerrar sessões e desativar revogam pelas autorizações (0,5 a 5,7 ms contra 43 a 251 ms) | #209, 3.6 | D3.10 |
| "último acesso" em campo próprio, sem escrever a cada requisição | #209 D8 | D3.11 |
| lista de sessões só com as vivas | #209 D9 | D3.10 |
| `503` com `Retry-After` é indisponibilidade passageira; só `401` encerra a sessão | #208 D4 | D3.12 |

**O protótipo** é o canvas "Protótipo de autenticação federada" (`claude.ai/artifact/H7dPBZafiSkdKd1hqesPZ6`), lido em 27/10/2026. Os quadros desta issue:

- **"Login — com provedor federado"**, estados `inicio`, `mfa`, `bloqueado` e `sessao-expirada`:
  - é um cartão só: e-mail e senha, e depois "Verificação em duas etapas", com o código de 6 dígitos no mesmo cartão;
  - o aviso de bloqueio diz "o acesso desta conta fica pausado por 5 minutos";
  - o de sessão expirada diz "Entre novamente para continuar de onde parou";
  - "Esqueceu a senha?" orienta a falar com o administrador.
- **"Login — sem provedor federado"**: o mesmo cartão, sem provedores.
- **"Editar usuário"**:
  - "Verificação em 2 etapas", com o botão "Redefinir verificação" para o admin;
  - "Sessões": "2 sessões ativas · última atividade ontem, 18:40", com "Encerrar todas as sessões";
  - "Desativar";
  - o aviso "Admin exige verificação em 2 etapas. Rafael ainda não configurou a dele e vai precisar fazer isso no próximo acesso".
- **"Usuários"**: colunas formas de entrada, papel, 2 etapas, estado e último acesso. A nota diz "Verificação em 2 etapas obrigatória para Admin".
- **"Aceitar convite sem provedor"** (#181): "Mínimo de 12 caracteres".

**Restrições do código que valem aqui**, herdadas da #179:
- validação de configuração só depois do `Build()`, porque o migration bundle roda o `Program.cs` até ali;
- nenhuma leitura de banco no boot, porque as fixtures sobem o host antes de migrar;
- o OpenIddict monta as opções em toda requisição, dentro do `UseAuthentication`;
- não há `ForwardedHeaders`: o `apps/api` só vê o IP do nginx do stack.

### Achados da fonte do OpenIddict 7.7.2

Lidos na tag `7.7.2` (`950cb13`), em 27/10/2026. Os caminhos são relativos a `src/`. Os fatos F1 a F15 do design da #209 continuam valendo. Os novos são:

| # | fato | onde |
|---|---|---|
| G1 | O authorize e o encerramento de sessão têm **modo passthrough**: o OpenIddict valida o pedido (cliente, `redirect_uri`, PKCE, escopos) e deixa a requisição seguir para uma rota do app, que decide e faz o `SignIn`/`SignOut`. Sem passthrough, o authorize não tem como autenticar a pessoa. | `OpenIddict.Server.AspNetCore/OpenIddictServerAspNetCoreBuilder.cs` l. 61–72 |
| G2 | `RequireProofKeyForCodeExchange()` torna o PKCE obrigatório para todo cliente. | `OpenIddict.Server/OpenIddictServerBuilder.cs` l. 1742 |
| G3 | O refresh token é **rotativo por padrão**: o apresentado vira `redeemed`. A expiração é **deslizante por padrão**: o novo refresh ganha a duração inteira. `DisableSlidingRefreshTokenExpiration()` fixa a expiração do primeiro. | l. 1544, 1561 |
| G4 | O access token e o id token carregam `oi_au_id`, o id da autorização, sempre incluído pelo filtro de claims. | `OpenIddictServerHandlers.cs` l. 3636 (access) e 4624 (id) |
| G5 | Durações por builder: `SetAccessTokenLifetime`, `SetIdentityTokenLifetime`, `SetRefreshTokenLifetime`. Os padrões são 1 h, 20 min e 14 dias (F14 da #209). | `OpenIddictServerBuilder.cs` l. 1763, 1795, 1817 |

**Verificado em 27/10/2026, fora do OpenIddict:**

- **Hash de senha (OWASP, Password Storage Cheat Sheet):** a ordem recomendada é:
  1. Argon2id, com mínimo de m = 19 MiB, t = 2, p = 1;
  2. scrypt;
  3. bcrypt, só para legado;
  4. PBKDF2-HMAC-SHA256 com 600.000 iterações, quando FIPS for exigido.

  Para hash legado, a recomendação é refazer no login. O PBKDF2 de 100.000 iterações do operador está seis vezes abaixo da recomendação atual para o próprio PBKDF2.
- **Argon2id em .NET 10:** o BCL não tem. Pacotes, conferidos no índice do NuGet:

  | pacote | versão | publicado | licença | alvo | observação |
  |---|---|---|---|---|---|
  | `NSec.Cryptography` | 26.4.0 | 30/04/2026 | MIT | `net9.0` | sobre `libsodium` [1.0.22, 1.0.23), ISC, publicada em 09/04/2026; nativo, com binários por runtime |
  | `Konscious.Security.Cryptography.Argon2` | 1.3.1 | 19/06/2024 | MIT | `net8.0` | C# puro; sem versão há 16 meses |
  | `Isopoh.Cryptography.Argon2` | 2.0.0 | 17/08/2023 | sem `licenseExpression` | `net7.0` | parada |
- **Rate limiting:** o middleware `Microsoft.AspNetCore.RateLimiting` faz parte do framework compartilhado do ASP.NET Core, sem pacote novo. Cookie e esquema de política também.

## Goals / Non-Goals

**Goals:**

- Uma pessoa com usuário local entra com e-mail e senha pelo fluxo do cliente `buteco-painel`, e recebe do emissor um access token válido nos três apps. O fluxo é o que o painel vai usar na #217. Isso é provado de ponta a ponta nos testes, com o artefato real.
- Senha guardada com Argon2id. O hash do operador de hoje não vira hash permanente de ninguém.
- O primeiro Admin nasce do bootstrap, sem tela.
- Força bruta e enumeração de contas limitadas, sem depender do IP, que o `apps/api` não vê.
- Cada PR deixa o `main` implantável. Esta change não troca o login do painel (D2).
- Decisões a–f da issue tomadas, com o que vai para cada issue nova marcado ("Tamanho e divisão").

**Non-Goals** (cada um com a issue de destino da divisão aceita em "Tamanho e divisão"):

- **O painel passar a entrar pelo emissor:** cliente OIDC no SPA, proxy do Vite, tratamento de `503`/`401` e o fim do `POST /auth/login`. Vai para a **#217**.
- **TOTP:** cadastro, verificação no login, códigos de recuperação e redefinição pelo admin. Vai para a **#218**.
- **Rotas de Minha conta e de Usuários:**
  - trocar senha, listar e encerrar sessões;
  - listar, editar, desativar e reativar usuários;
  - encerrar as sessões de outro usuário.

  Vão para a **#219**.
- **2 etapas obrigatórias para todo usuário da M1:** vai para a **#220**, depois da tela (#183).
- **Convites e redefinição por link:** #181. **Remoção do HMAC:** #182. **Papéis:** #184.
- **Telas:** #183.

## Decisions

### D1. (a) A tela de login fica no SPA; o `/connect/authorize` em passthrough autentica pelo cookie de sessão do emissor

O `/connect/authorize` precisa saber quem é a pessoa. As duas opções que a issue pede para comparar:

- **(i) tela servida pelo `apps/api`:** HTML do próprio emissor em `/conta/entrar`, com o cookie e o antiforgery do ASP.NET;
- **(ii) authorize em passthrough que manda para o SPA:** o authorize manda para a rota de login do SPA, o SPA autentica no `apps/api` por JSON e recebe o cookie de sessão do emissor, e volta ao authorize.

| critério | (i) tela no `apps/api` | (ii) passthrough + SPA |
|---|---|---|
| **Protótipo** | Os quadros de login (cartão único, passo das 2 etapas, avisos de bloqueio e de sessão expirada) teriam de ser reproduzidos em HTML do servidor, fora do Mantine e do design system do painel, com tema claro e escuro duplicados | Os quadros são telas React do painel, no mesmo sistema visual das outras |
| **O que fica para a #183** | A #183 não desenharia o login: ou leva `.cshtml` para o `apps/api`, contra "tela não leva backend", ou o login fica fora dela | A #183 desenha o login como todas as outras telas. O backend é só JSON (convenção 1) |
| **Cookie** | Mesma origem do emissor por construção | Mesma origem em produção pelo nginx. Em desenvolvimento, mesma origem pelo proxy do Vite (D3.4) |
| **CSRF** | Antiforgery do ASP.NET no formulário | Rota JSON com conferência de `Origin` (abaixo). Não há formulário HTML para forjar |
| **Superfície no `apps/api`** | Views, arquivos estáticos e localização no `apps/api`, que hoje não serve HTML | Duas rotas JSON e duas em passthrough |
| **M3 (federado)** | O retorno do Google cai no `apps/api` de qualquer jeito | O mesmo. O cookie externo é do servidor nos dois casos, e a recusa ("Conta sem acesso") é aviso na tela do SPA, como o protótipo desenha |
| **XSS no painel** | Com a sessão aberta, um script injetado no painel pede código e token pelo próprio fluxo | O mesmo. O cookie é `HttpOnly` nos dois casos. A diferença não está no lugar da tela |

**Escolhida: (ii).** O que decide é o protótipo e a fronteira da #183. Com (i), o login seria a única tela do produto fora do painel, desenhada em outra tecnologia, e a #183 deixaria de ser dona dela. Os critérios de segurança empatam: o cookie é `HttpOnly` nas duas, e o CSRF tem resposta nas duas.

**O fluxo:**

1. O SPA navega para `/connect/authorize` com:
   - `client_id=buteco-painel`;
   - `response_type=code`;
   - `scope=openid offline_access buteco-api buteco-inbox buteco-connectors`;
   - `code_challenge` (S256), `state` e `redirect_uri`.
2. O OpenIddict valida o pedido (G1). A rota `GET /connect/authorize` autentica o esquema de cookie `ButecoSession`:
   - **sem sessão:**
     - com `prompt=none`, devolve `login_required` ao `redirect_uri`;
     - sem `prompt=none`, `302` para `/entrar?retorno=<authorize original>`, um caminho relativo que vale em produção e no proxy de desenvolvimento. **O `retorno` é montado pelo servidor**, só com `Request.Path` e `Request.QueryString` do pedido, URL-codificado. Nunca leva esquema, host nem porta, e nunca sai de parâmetro do cliente nem do header `Host`;
   - **com sessão:** confere o usuário no banco (ativo, `security_stamp` igual ao do cookie), monta o principal e faz `SignIn` no esquema do OpenIddict. O resultado é `302` ao `redirect_uri` com o código.
3. A tela `/entrar` do SPA manda `POST /auth/session` com `{ email, senha }`:
   - se der certo, o `apps/api` grava o cookie e responde `204`;
   - o SPA então navega para o `retorno`. Ele só segue um `retorno` que comece por `/connect/authorize?`, e qualquer outro vai para a tela inicial, para não haver redirecionamento aberto. Isso fica registrado na #217 (comentário de 31/10/2026).
4. Na volta, `/entrar/retorno` troca o código por tokens em `POST /connect/token`, com o `code_verifier`. Os passos 1, 3 e 4 do lado do SPA são da **#217**.

**Cookie `buteco-sessao` (esquema `ButecoSession`):**
- `HttpOnly`, `SameSite=Strict`, `Path=/`, sem `Domain`;
- `Secure` sempre, menos em `Development`. O TLS termina fora do stack, e o `apps/api` vê HTTP, então `SameAsRequest` em produção nunca marcaria `Secure`. O prefixo `__Host-` fica de fora porque exige `Secure` também em desenvolvimento;
- vida de 5 minutos, sem renovação. Ele só existe para a passagem do login ao authorize, e mais tarde para o passo das 2 etapas (#218).
- **A sessão de verdade é a autorização do OpenIddict**, com o refresh token do painel. Não há uma segunda credencial longa a revogar junto: "encerrar todas as sessões" revoga as autorizações (D3.10) e mais nada.
- Consequência: abrir uma aba nova sem token pede login de novo. É o comportamento de hoje, com o `sessionStorage`. Se a #217 escolher guardar o token de outro jeito, isso muda lá.

**CSRF no `POST /auth/session`.** O ataque é o login CSRF: um site terceiro faz o browser da vítima entrar na conta do atacante. Três barreiras:

1. A rota só aceita `Content-Type: application/json`. Um formulário HTML de outro site não manda JSON, e um `fetch` de outra origem com JSON passa por preflight, que o CORS recusa.
2. O `Origin` precisa ser a origem do issuer, ou a de `Identity:PanelBaseUrl` (D3.4). Ausente ou diferente, a resposta é `403`. Isso cobre também uma configuração de CORS que alguém abra por engano.
3. O PKCE e o `state` ficam no SPA que iniciou o fluxo. Um authorize disparado pelo atacante entrega um código que o SPA da vítima não tem como trocar.

O `security_stamp` do usuário vai no cookie e é conferido no authorize. Desativar o usuário, ou trocar a senha (#219), invalida o cookie em voo.

**Alternativa descartada: (i)**, pelo motivo acima. **Também descartada: cookie de sessão longo do emissor, com SSO entre abas.** Ele seria uma segunda sessão paralela à autorização. Encerrar sessões teria de matar as duas, e a tela de sessões teria de mostrar as duas.

### D2. (b) Transição: esta change não troca o login do painel; o bootstrap cria o primeiro Admin

**Do merge da #180 ao merge da #217:**

- O painel continua com `POST /auth/login`, o HMAC e o operador da variável de ambiente, **sem nenhuma mudança**. O `main` fica implantável, e ninguém entra pelo fluxo novo, porque não há tela para isso.
- O fluxo novo existe e é provado pelos testes (D5) e por curl no fechamento.
- **Critério de aceite da #180, mudado pela divisão:** "o painel faz login e acessa uma rota do `apps/api`, uma do `apps/inbox` e a sincronização do `apps/connectors`" passa para a **#217**, que é quem troca o login do painel. Na #180, o critério é o mesmo, provado nos testes de acordo de `tests/` com o token de usuário do fluxo real.

**Da #217 em diante:**
- o painel entra pelo emissor;
- a #217 remove `POST /auth/login` e `Auth:OperatorUsername`;
- `Auth:OperatorPasswordHash` passa a servir só ao bootstrap;
- o HMAC continua só para os tokens de serviço, até a #182 removê-lo.

**Bootstrap do primeiro Admin.** Roda em segundo plano depois do start, no aquecimento, nunca no boot, como passo independente com nova tentativa no ciclo seguinte (D3.13). Usa o lock `bigint` `BUTECOU1` (`4779819235982398769`), na transação da criação:

| situação | comportamento |
|---|---|
| nenhum usuário, `Auth:BootstrapAdminEmail` e `Auth:OperatorPasswordHash` presentes e válidos | cria um usuário ativo com o e-mail, nome = parte local do e-mail e o hash PBKDF2 marcado como **legado de bootstrap**; log `Information` com o id, nunca com o hash |
| nenhum usuário, alguma das duas ausente ou inválida | não cria; log `Warning`: "nenhum usuário; defina Auth:BootstrapAdminEmail e Auth:OperatorPasswordHash para criar o primeiro Admin" |
| já há usuário | não faz nada; com as variáveis presentes, log `Information`: "bootstrap ignorado: já existem usuários". Nunca sobrescreve senha nem e-mail |
| duas instâncias ao mesmo tempo | uma cria, a outra vê o usuário criado (lock e índice único) |

A resposta à decisão em aberto da issue ("variável presente e já há usuários") é **ignorar, com log**. Recusar o boot não serve, porque a variável continua obrigatória no compose até a #217, para o `POST /auth/login`. E sobrescrever seria uma porta dos fundos permanente: quem tem o `.env` redefine a senha do Admin a cada reinício.

**O hash do operador, sem reaproveitamento inseguro.** Três usos seriam inseguros, e nenhum acontece:

1. **Guardar o PBKDF2-100k como hash permanente.** Não acontece. No primeiro login bem-sucedido, a senha em claro já está em mãos, e o hash é refeito em Argon2id na mesma transação. O marcador de legado sai.
2. **Aceitar o formato PBKDF2 para qualquer usuário.** Não acontece. O verificador legado só roda para a linha marcada como legado de bootstrap. Qualquer outra linha com esse formato é recusada como hash inválido, e o login falha.
3. **Reaplicar a variável a cada boot.** Não acontece, pela tabela acima.

**Por que reaproveitar, e não pedir uma senha nova:** o hash da variável já protege essa mesma conta hoje, com a mesma exposição (`.env.prod`). Usá-lo como hash inicial não baixa a segurança de nada, e o primeiro login a sobe. As alternativas:
- uma senha aleatória impressa no log põe um segredo no log;
- um link de primeiro acesso depende da #181, que vem depois;
- uma variável nova com hash Argon2id gerado offline faz o operador gerar outro hash, com uma ferramenta que não existe.

**O `docs/deployment.md` passa a orientar:** depois de a #217 entrar, e depois do primeiro login, a senha do Admin é trocada pela rota da #219, e `AUTH_OPERATOR_PASSWORD_HASH` pode sair do `.env.prod`.

**O `operator` dos outros apps:** o token do painel é mapeado para `operator` pela tabela de clientes, no `apps/api` (D3.3), no `apps/inbox` e no `apps/connectors` (#208 D6). Todo usuário tem o acesso do operador, como a issue decide para a M1.

### D3. (c) As heranças

#### D3.1 Cliente público `buteco-painel` (herança)

Sincronizado pelo `apps/api`, por um `IdentityClientCatalog` que roda no aquecimento, independente do anel, e de novo a cada tique da rotação (D3.13). Usa o lock `bigint` `BUTECOC1` (`4779819235982394161`) e não faz I/O no boot. O descritor é fixo no código:

| campo | valor |
|---|---|
| `ClientType` | `public`, sem segredo |
| `ConsentType` | `implicit`: não há tela de consentimento, porque o cliente é o próprio produto |
| permissões | endpoints de authorization, token e end session; grants `authorization_code` e `refresh_token`; response type `code`; escopos `openid`, `offline_access` e os três do D3.2 |
| requisito | PKCE (`ft:pkce`), além do `RequireProofKeyForCodeExchange()` global (G2) |
| `redirect_uris` | `{PanelBaseUrl}/entrar/retorno` |
| `post_logout_redirect_uris` | `{PanelBaseUrl}/entrar` |

- **Nunca** com `client_credentials`: um token com `client_id` `buteco-painel` só pode vir de uma pessoa (#208 D6).
- **A sincronização sobrescreve** qualquer divergência do descritor, inclusive um segredo ou um `client_credentials` posto à mão. O descritor do código é a fonte, e o banco é cópia.
- **O prefixo `buteco-` fica reservado** aos clientes declarados no código (`IdentityClients.ReservedPrefix`). A #186 recusa criar aplicação com esse prefixo. Um `buteco-*` no banco que o código não declara gera log `Error` na sincronização, e nada é apagado.
- Hoje a única entrada da tabela de clientes dos três apps é `buteco-painel`. Um `buteco-*` desconhecido recebe `403` nos três apps de qualquer jeito.
- **Muda um requisito da `oidc-issuer`**, "não cadastrar nenhum cliente, nem por seed no boot". A sincronização não é no boot, mas cria um cliente em produção. O requisito passa a nomear o único cliente que o próprio `apps/api` mantém.

#### D3.2 Três escopos e recursos (herança)

`buteco-api`, `buteco-inbox` e `buteco-connectors`, cada um com o recurso de mesmo nome, sincronizados pelo mesmo catálogo.

- O painel pede os três, e a `aud` do token vira array (F10 da #208).
- `openid` e `offline_access` são do próprio OpenIddict.
- Cada app exige só a sua `aud`.

#### D3.3 O `apps/api` aceita o próprio token; `libs/` continua sem autenticação (herança)

**Como:**

- **Esquema de política `Bearer` como padrão**, no molde da #208 (D2 de lá). JWS compacto (dois pontos) vai ao esquema `IssuerToken`, e o resto ao `OperatorToken` de hoje, sem mudança. Um esquema por requisição, sem queda de um no outro.
- **`JwtBearer` 10.0.13**, já central desde a #208. O que ele exige:
  - `ValidAlgorithms = [RS256]`, `ValidTypes = [at+jwt]`, `RequireExpirationTime` e `RequireSignedTokens`;
  - `ValidIssuer` = o issuer;
  - `ValidAudience = buteco-api` (`IssuerTokenAudience`);
  - tolerância de relógio de 30 s;
  - `MapInboundClaims = false`.
- **Chaves do anel em memória, sem HTTP.** `IssuerSigningKeyResolver` lê as chaves de validação do `IdentityKeyRing.Snapshot`, que já inclui a próxima e a anterior em retenção. Não há `IssuerKeysRetriever`, nem serviço de renovação, nem `503` próprio. O `IdentityKeyRingGate` já responde `503` a toda rota até o anel carregar, e a troca de chave já recarrega o snapshot (#179 D6 e D7).
- **Principal pela tabela de clientes**, como na #208 (D6 de lá):
  - `IssuerClientSubjects`: `buteco-painel` → `operator`;
  - `NameIdentifier` = o subject da tabela, ou nenhum, e aí `403`;
  - o `sub` (id do usuário) fica num claim próprio, para log e para as rotas da #219;
  - o `ServiceScopeAuthorizationHandler` não muda.
- **Boot:** `ValidateIssuerClientSubjects` confere que todo subject de destino existe em `ServiceScopeAuthorizationHandler`.

**`libs/`: não.** A pergunta é se os três consumidores justificam extrair o código de validação (convenção 2, e a régua de `libs/` do `docs/conventions.md`):

- **O terceiro consumidor não é a terceira cópia dos nove arquivos.** O `apps/api` não tem retriever, nem renovação, nem estado de "chaves obtidas", nem o `503` do desafio. As chaves estão no processo. O que é igual nos três é o núcleo de ~40 linhas: `TokenValidationParameters`, o seletor por forma e o mapeamento por cliente. É exatamente o que difere em valor entre os apps: a `aud`, a tabela de clientes e a tabela de subjects.
- **A duplicação não custa o que a convenção 2 pede.** Uma correção no núcleo não precisa ser aplicada igual nos três: cada app tem a própria `aud` e a própria tabela, e o teste de acordo de cada um (convenção 11) prova cada cópia contra o emissor real.
- **Isolamento entre apps.** Uma `libs/` de autenticação seria código comum de fronteira de segurança entre apps que o monorepo isola de propósito. Um defeito nela abriria os três ao mesmo tempo. Hoje, um defeito numa cópia fica naquele app, e o acordo dele o pega.
- **O código encolhe adiante.** A #182 remove o HMAC e o seletor dos três apps. O que sobra é a configuração do `JwtBearer`, que é configuração de biblioteca, e não código nosso.
- **Gatilho para voltar à pergunta:** um quarto consumidor, ou uma correção que precise ser aplicada idêntica nos três. Fica registrado no `02`.

**Alternativa descartada: `OpenIddict.Validation.AspNetCore` com `UseLocalServer()`.** É o idioma do OpenIddict para validar no mesmo processo, mas:
- poria no `apps/api` uma biblioteca de validação diferente da dos outros dois apps, para o mesmo token, com semântica diferente (claims, consulta opcional de entrada de token);
- abriria de novo a medição de dependências que a #179 e a #208 fizeram.

O JWKS é o contrato que separa quem emite de quem valida, e os três apps o leem do mesmo jeito.

#### D3.4 `PublicUrl:BaseUrl` obrigatória; o desenvolvimento usa o proxy do Vite (herança)

**O boot falha sem url pública.** `ValidateIdentityIssuerConfiguration` passa a reprovar `PublicUrl:BaseUrl` ausente, depois do `Build()`. O registro continua sem lançar, por causa do bundle. Em produção nada muda: o compose já a exige com `:?`.

**O que deixa de ser alcançável, e sai junto (convenção 25):**
- o ramo "emissor desligado" do `AddIdentityIssuer`;
- o `IdentityIssuerDisabledTests`;
- o `ApiFactoryWithoutPublicUrlFixture`;
- o cenário "Sem URL pública configurada, os endereços ficam ausentes" da `agent-catalog`, e o teste dele no `AgentA2AAddressesTests`. O ramo nulo do `AgentA2AAddressBuilder` também sai, porque nenhuma composição o alcança mais.
- O requisito "Sem url pública, o emissor fica desligado e o app sobe" da `oidc-issuer` vira "sem url pública, o boot falha".

**Desenvolvimento (a escolha que a issue deixou em aberto): proxy do Vite.**
- O SPA passa a chamar `/auth`, `/connect` e `/.well-known` por caminho relativo, e o `vite.config.ts` os encaminha a `http://localhost:5017`. É a mesma origem do nginx em produção.
- Chamar direto, com CORS, exigiria `AllowCredentials` no CORS do `apps/api` para o cookie. A navegação ao authorize e o cookie também ficariam em origens diferentes só em desenvolvimento.
- O proxy é código do painel, e entra na **#217**. Na #180 não há consumidor de navegador, e a conferência é por curl.

**`Identity:PanelBaseUrl`** (nova, opcional; ausente = `PublicUrl:BaseUrl`):
- é a base do `redirect_uri` e do `post_logout_redirect_uri` do cliente (D3.1), e a segunda origem aceita no CSRF (D1);
- em produção fica ausente, porque o painel e o emissor têm a mesma origem;
- em `appsettings.Development.json` vale `http://localhost:5173`, a origem do Vite.

Existe porque há cenário real com outro valor: o desenvolvimento, em que o painel e o `apps/api` estão em portas diferentes (convenção 2).

#### D3.5 nginx: blocos exatos para o authorize e o encerramento de sessão (herança)

- **`location = /connect/authorize`** e **`location = /connect/logout`**, com o mesmo `proxy_pass` e os mesmos headers, **sem** o desvio de navegação.
- O `location =` vence a regex `~ ^/(connect)(/|$)` pela ordem de casamento do nginx, então o `/connect/token` continua no bloco de prefixo com o desvio.
- **Por que exatos, e não uma exceção dentro do bloco de prefixo:** é o tratamento que a #179 deu ao `/.well-known` (D10 de lá). Cada caminho de navegação do emissor fica nomeado e visível, e um endpoint novo do OpenIddict sob `/connect` não ganha navegação direta por acidente.
- `/entrar` e `/entrar/retorno` não são prefixo de API e caem no SPA.
- `/auth/session` é `fetch`, então fica no bloco `/auth` existente.

**Conferência por curl** no stack local, no fechamento, com a saída no `tasks.md`. É a mesma forma da 6.3 da #179, com a troca do `resolver` do Podman (#212):

```
curl -si -H 'Sec-Fetch-Mode: navigate' 'http://localhost:<porta>/connect/authorize?client_id=buteco-painel&response_type=code&...'
  → 302 com Location: /entrar?retorno=...   (do apps/api, e não 200 com o HTML do SPA)
curl -si -H 'Sec-Fetch-Mode: navigate' 'http://localhost:<porta>/connect/logout'
  → resposta do apps/api, e não o shell do SPA
curl -si -H 'Sec-Fetch-Mode: navigate' 'http://localhost:<porta>/connect/token'
  → shell do SPA (o desvio continua para o token)
curl -si -X POST -H 'Content-Type: application/json' -H 'Origin: <issuer>' --data '{…}' 'http://localhost:<porta>/auth/session'
  → 204 com Set-Cookie: buteco-sessao (HttpOnly, SameSite=Strict)
```

#### D3.6 Endpoints novos: discovery sob o issuer, lista esperada e passthrough (herança)

- **Discovery:** os endpoints novos saem sob o issuer pelo `DiscoveryEndpointsFromIssuerHandler`, sem trabalho extra (#179 D8, correção). O teste de discovery já confere todo `*_endpoint`.
- **`IdentityIssuerEndpoints.Expected`** ganha:
  - `/connect/authorize`, com `AuthEntryPoint`;
  - `/connect/logout`, com um motivo novo, `SessionEnd`: o encerramento de sessão não é entrada de autenticação, e o enum documenta o porquê de cada rota anônima.
- **Correção na checagem (`ValidateIdentityIssuerEndpoints`).** Hoje ela reprova se uma URI do emissor coincidir com uma rota mapeada (#179 D8). Com o passthrough (G1), o authorize e o logout **precisam** ter rota mapeada, ou o pedido validado cai num `404`. A lista esperada passa a marcar cada endpoint como `Passthrough` ou não:
  - **passthrough:** precisa existir uma rota mapeada com o mesmo caminho, anônima e classificada com o mesmo motivo;
  - **não passthrough:** continua proibido coincidir com rota, como hoje.

  O teste da composição real ganha o caso "rota do passthrough removida → boot falha".
- **Sem userinfo, introspecção nem revogação.** O SPA tira nome e e-mail do id token, e a revogação é pelo encerramento de sessão (D3.10). Endpoint sem consumidor não entra (convenção 2).

#### D3.7 Durações (herança)

| token | duração | por quê |
|---|---|---|
| access | **15 min** | "sessão curta (ex.: 15 min)", da issue. É global no servidor, e o `client_credentials` (#186) também passa a 15 min. A premissa da #186, um pedido a cada 10 min, continua valendo |
| refresh | **14 dias, rotativo, deslizante** (G3) | É a idade mínima da limpeza, e a checagem de boot da #209 passa com folga zero (`MinimumAge` = duração). Rotativo é o padrão, e a detecção de reuso depende dele. Deslizante (**decisão do dono, 28/10/2026**): uma sessão em uso não expira, e uma parada por 14 dias exige login. A expiração absoluta (`DisableSlidingRefreshTokenExpiration`) foi descartada. A detecção de reuso vale nas duas |
| id token | padrão (20 min) | Só serve de `id_token_hint` (D3.9) e para o SPA ler nome e e-mail |
| código | padrão (5 min) | — |

**Retenção das chaves: de 24 h para 15 dias.** A premissa da #179 D8 se mostrou falsa para o refresh, conferida na fonte da 7.7.2 em 31/10/2026 a pedido do dono. A #179 dimensionou a retenção pelo access token, o único token que existia, e com o fluxo do usuário isso não basta:

- **A validação de todo token do próprio servidor usa as chaves de `options.SigningCredentials`:**
  - a configuração copia as chaves das credenciais para `TokenValidationParameters.IssuerSigningKeys` (`OpenIddictServerConfiguration.cs` l. 556–558);
  - o `ResolveTokenValidationParameters` usa esses parâmetros para todo tipo que não seja client assertion (`OpenIddictServerHandlers.Protection.cs` l. 104–113 e 189);
  - o `ValidateIdentityModelToken` valida com eles (l. 476). Chave fora da lista dá `SecurityTokenSignatureKeyNotFoundException` (l. 509), e o token é recusado.
- **O refresh, o código e o id token são assinados pela chave ativa.** O `AttachSecurityCredentials` assina todo token com `SigningCredentials.First()` (l. 1478–1486). O refresh é JWT autocontido, porque o servidor não usa `UseReferenceRefreshTokens`. Mesmo com referência, o conteúdo gravado seria o mesmo JWT, validado igual (l. 351–355 e 414–418).
- **O refresh e o hint usam os mesmos parâmetros.** O `ValidateRefreshToken` despacha o refresh para essa validação (`OpenIddictServerHandlers.cs` l. 2143–2152). O `id_token_hint` também, só com a validade desligada no authorize e no logout (l. 2059–2070). Sem validade conferida, a chave é o único limite de idade do hint.
- **A cifra não é problema.** Ela vem de `EncryptionCredentials` (`OpenIddictServerConfiguration.cs` l. 561–563, `Protection.cs` l. 1469–1476). A chave de cifra não roda: vale de quando nasce até `DateTimeOffset.MaxValue` (`IdentityKeyRotationPolicy.EncryptionKey`).

**Consequência com 24 h:** um refresh emitido pela chave anterior deixa de validar 24 h depois da troca, que acontece a cada 90 dias. Quem ficasse mais de um dia sem usar o painel nessa hora perderia a sessão antes dos 14 dias decididos.

**A decisão (dono, 31/10/2026):**

- `IdentityKeyRotationPolicy.Retention` passa a **15 dias**. A regra é "retenção ≥ maior duração (access ou refresh) + intervalo da verificação", e 14 d + 1 h cabe em 15 d.
- A checagem de boot da `ValidateIdentityIssuerConfiguration` passa a comparar a retenção com **a maior das durações globais**, access e refresh, mais o `CheckInterval`. Refresh sem prazo (nulo) reprova, como já faz a checagem da limpeza.
- **Teste novo** (`IdentityKeyRetentionRefreshTests`, integração): um refresh emitido com a chave anterior, apresentado 13 dias depois da troca, é aceito. **Guarda verificado:** com a retenção de volta a 24 h, o teste reprova.
- **Efeitos aceitos:**
  - a chave aposentada fica 15 dias no JWKS, e não 24 h. O JWKS nunca passa de duas chaves de assinatura, porque a publicação antecipada (7 dias) e a retenção (15 dias) somam menos que os 90 dias da rotação;
  - o `apps/inbox` e o `apps/connectors` aceitam a chave anterior por mais tempo, mas o access token vence em 15 min, e nenhum token vive mais do que vivia;
  - a rotação continua não sendo resposta a vazamento de chave, como na #179.
- **Testes atuais que mudam:**
  - `IdentityKeyRotationPolicyTests.AfterTheSwitch_TheNewKeySigns_TheOldOneStaysInTheRingFor24Hours` usa 24 h literais. Ele passa a usar a constante, com o nome novo.
  - A spec `identity-signing-keys` muda de "24 horas" para "15 dias".
- **O `id_token_hint` fica coberto.** Numa sessão viva, o id token mais recente tem no máximo a idade do refresh (D3.9). Com 15 dias, a assinatura dele ainda valida no logout.

**Duração por aplicação** (conferido na fonte da 7.7.2 em 28/10/2026, e registrado na #186):

- o descritor da aplicação aceita `Settings["tkn_lft:act"]` (`OpenIddictApplicationDescriptor.cs` l. 167–180);
- na emissão, a ordem é a duração do principal, depois a da aplicação, depois a global (`OpenIddictServerHandlers.cs` l. 3680–3703);
- a #186 pode, assim, dar à aplicação externa uma duração própria sem mexer na do painel.

A checagem de boot acima, porém, só lê as durações globais. Uma duração por aplicação maior que a retenção menos 1 h não aparece para ela. A #186 registra que, se usar duração por aplicação, a checagem passa a considerá-la (comentário de 31/10/2026).

#### D3.8 O access token do usuário não grava linha; o id token também não (herança)

- **A linha só teria uso se alguém validasse consultando o banco.** Os três apps validam pelo `JwtBearer` (D3.3), e nenhum lê a linha. Então o D1 da #209 se estende:
  - a nenhum access token, de qualquer grant;
  - ao id token (D3.9).
- O handler passa a se chamar `TokenEntryStorageHandler`, com a mesma ordem, calculada do descritor do OpenIddict.
- Continuam gravados o código de autorização (resgate único) e o refresh token (revogação, rotação e reuso).
- **Volume**, nas premissas da #209 (1 login por dia, 8 h de uso, renovação a cada 15 min): de ~100 para **~33 linhas por usuário por dia** (1 código e 32 refresh). Com M = 50, o estoque estável cai de ~70.200 para ~23.200 linhas.
- **Efeito declarado:** um access token não pode ser revogado sozinho. Na prática nunca pôde, porque a validação é pelo JWKS. A janela de um usuário desativado é de até 15 min com o access token já emitido. A renovação seguinte é recusada (D4).

#### D3.9 `id_token_hint` com mais de 14 dias (herança)

- **Resolvido pelo D3.8:** sem linha, o id token não carrega `oi_tkn_id`, e o OpenIddict não procura linha nenhuma para ele (F8 da #209). A assinatura e o issuer continuam conferidos. A #217 ainda guarda o id token mais recente, renovado a cada refresh, como o comentário da #209 sugere.
- **Por G4**, o id token carrega `oi_au_id`. Se o OpenIddict conferir a autorização ao validar o hint, um hint de sessão já revogada é recusado. A tarefa de verificação confere isso por teste antes de o código depender.
- **Nos dois casos, o encerramento de sessão sempre termina:**
  - a rota do passthrough apaga o cookie `buteco-sessao`;
  - revoga a autorização do hint, se o hint for válido;
  - redireciona para o `post_logout_redirect_uri` registrado, ou para `/entrar` quando o pedido vier sem um válido.
- Ela nunca redireciona para endereço não registrado.
- **Conferido na 2.4 (b) e corrigido na 6.6, 31/10/2026:**
  - **a autorização do hint não é conferida**, porque ela só é lida pela linha do token, que o id token não tem;
  - **o hint inválido não recusa o pedido**, porque é opcional (`OpenIddictServerHandlers.cs` l. 364–377). O pedido chega à rota com um principal vazio, e não nulo;
  - **o endereço não registrado é recusado antes da rota.** O `EnableErrorPassthrough` levaria à rota também os erros do authorize. Por isso esse caso fica num handler de `ApplyEndSessionResponseContext`, que apaga o cookie e redireciona para `/entrar`;
  - **na rota**, sem hint autenticado, o pedido vai a `/entrar`, mesmo com endereço registrado.

#### D3.10 Encerrar sessões e desativar: pelas autorizações (herança, e a #209 D9)

- "Encerrar todas as sessões" e desativar usuário chamam `IOpenIddictAuthorizationManager.RevokeBySubjectAsync(userId)`. A #209 mediu de 0,5 a 5,7 ms, contra 43 a 251 ms pelos tokens.
- O efeito é o mesmo: o próximo refresh é recusado, porque a autorização não está `valid` (F8 da #209).
- **Confirmado:** cada login abre uma autorização ad hoc (F6 da #209), então "sessão" = autorização ad hoc `valid` do usuário.
- **A lista de sessões vivas** sai de `openiddict_authorizations` com `subject` = id do usuário, `status = valid` e com refresh token vivo.
- **Nesta change**, o mecanismo é o estado do usuário: login e refresh de usuário desativado são recusados (D4). As ações "encerrar" e "desativar", com as rotas e a revogação acima, são da **#219**.
- O `security_stamp` muda junto na desativação. Isso invalida o cookie de 5 minutos em voo e, pelo D4, o refresh de antes.
- **O access token já emitido continua valendo até 15 min**, depois do logout ou da desativação, nos três apps. Ele não é gravado (D3.8) e é validado só pelo JWKS, sem consulta ao emissor. **Isso é aceito na M1** (pedido do dono, 31/10/2026). A revogação para as aplicações externas é da #186. O `01` (seção Autenticação) e o `docs/deployment.md` registram isso (tarefas 11.1 e 11.2).

#### D3.11 "Último acesso" (herança)

- Coluna `users.last_access_at`, atualizada:
  - no `POST /auth/session` bem-sucedido;
  - em cada grant `refresh_token` aceito, pelo handler de `HandleTokenRequestContext` que já confere o usuário (D4).
- Nunca por requisição de API: o `JwtBearer` não toca no banco.
- Com renovação a cada 15 min, são até 4 escritas por hora por usuário ativo, uma linha só, por `UPDATE` direto.
- A granularidade é a do protótipo ("ontem, 18:40"). Um usuário que só navega dentro de um access token aparece com até 15 min de atraso, e isso é declarado.

#### D3.12 `503` com `Retry-After` (herança)

O contrato não muda:
- o `apps/api` responde `503` só pelo gate do anel (#179 D6), inclusive no `/auth/session` e no authorize;
- o `apps/inbox` e o `apps/connectors` respondem `503` só antes da primeira obtenção das chaves (#208 D4).

O painel tratar `503` como indisponibilidade passageira (tentar de novo, sem deslogar) e só `401` como fim de sessão é da **#217**, junto do resto do cliente. As tentativas e a espera também: o design da #217 decide, como a issue pede.

#### D3.13 Aquecimento: anel, catálogo e bootstrap independentes (pedido do dono, 31/10/2026)

O aquecimento do `IdentityKeyRotationService` passa de um passo, o anel, a três:

1. carregar o anel;
2. sincronizar o catálogo de clientes e escopos (D3.1, D3.2);
3. rodar o bootstrap (D2).

**Cada passo tem o próprio `try/catch`, com `EventId` próprio** (convenção 4):

- **a falha de um não impede os outros.** O catálogo e o bootstrap não dependem do anel, só do banco, e rodam mesmo com o anel ainda sem carga;
- **o passo que falhou tenta de novo no ciclo seguinte** do aquecimento (`WarmupRetryInterval`, 5 s), e os que já concluíram não repetem;
- o aquecimento termina quando os três concluíram;
- **depois do aquecimento:**
  - o anel segue no tique da rotação, como hoje;
  - o catálogo roda de novo a cada tique do `CheckInterval`, em `try/catch` separado do da rotação, para corrigir divergência (D3.1);
  - o bootstrap não repete. "Concluir" é criar, ignorar porque já há usuário, ou registrar o aviso de configuração ausente.

**O gate do `503` não espera o catálogo.** Ele existe porque, sem chave, a montagem das opções do OpenIddict lança em qualquer rota (#179, F5). A falta do cliente não lança nada, e só afeta os pedidos com `client_id=buteco-painel`. Se o gate esperasse o catálogo, uma falha dele derrubaria com `503` o `POST /auth/login` do HMAC e todas as rotas, para proteger um fluxo que, até a #217, ninguém usa. O contrato do D3.12 não muda.

**O authorize antes da primeira sincronização:**

- o OpenIddict não acha o cliente e recusa com `invalid_request` ("The specified 'client_id' is invalid.", `OpenIddictServerHandlers.cs` l. 1037–1055);
- como o pedido foi recusado, o `redirect_uri` não é usado (`OpenIddictServerHandlers.Authentication.cs` l. 2133–2136), e a resposta é `400` local, sem código e sem redirecionar;
- o `/connect/token` com `buteco-painel` recebe `invalid_client`;
- a janela vai do start à primeira sincronização com o banco no ar, que é o primeiro ciclo.

A #217 trata esse `400` logo depois de um deploy como falha passageira. **Alternativa descartada:** um handler antes do `ValidateClientId` que responderia `503` enquanto o catálogo não sincronizou. Ela criaria uma segunda origem de `503` fora do gate, para uma janela de segundos.

### D4. (d) O usuário

**Entidade `User`** (`Users/User.cs`, tabela `users`):

| coluna | conteúdo |
|---|---|
| `id` | `uuid`, chave primária, imutável. É o `sub` dos tokens |
| `email` | como a pessoa digitou, sem espaços nas pontas |
| `normalized_email` | `email.ToLowerInvariant()`, índice único. É o identificador de login |
| `name` | texto |
| `status` | `active` ou `deactivated`, gravado como texto |
| `password_hash` | string PHC (`$argon2id$v=19$m=19456,t=2,p=1$…`) ou o formato legado de bootstrap; nula quando o usuário não tem senha (convite pendente na #181; só federado na M3) |
| `password_hash_is_bootstrap_legacy` | `bool`, verdadeiro só na linha criada pelo bootstrap, até o primeiro login |
| `security_stamp` | texto aleatório, trocado ao desativar, ao trocar a senha (#219) e ao redefinir as 2 etapas (#218) |
| `created_at`, `last_access_at`, `deactivated_at` | `timestamptz` |

- Trocar o e-mail (#219) não muda o `sub`.
- As colunas das 2 etapas entram com a #218.

**Hash: Argon2id com `NSec.Cryptography` 26.4.0** (verificado no Context):

- **Por que Argon2id:** é a primeira recomendação da OWASP na data de hoje, e resiste a GPU pelo custo de memória. O PBKDF2 está em quarto lugar, recomendado só quando FIPS é exigido, e o Buteco não tem esse requisito.
- **Por que NSec:**
  - está mantido (versão de abril de 2026, alvo `net9.0`, que roda no .NET 10);
  - usa a libsodium, implementação de referência auditada e mantida (1.0.22, abril de 2026);
  - tem licenças MIT e ISC.
  - O Konscious está sem versão há 16 meses, e o Isopoh está parado e sem licença declarada no pacote.
- **O custo da escolha:** é a primeira dependência nativa do `apps/api`. A `libsodium` traz binários por runtime. A tarefa de verificação confere, antes de qualquer código depender:
  - o restore sem `NU1605`/`NU1608` nem aviso de vulnerabilidade;
  - a carga do binário nativo na imagem `mcr.microsoft.com/dotnet/aspnet:10.0` (Ubuntu 24.04, glibc; conferido na 2.2) e no macOS de desenvolvimento;
  - o nome e a forma da API de Argon2id na versão resolvida, lidos na fonte da tag.
- **Parâmetros:** m = 19.456 KiB, t = 2, p = 1, o mínimo da OWASP. Sal de 16 bytes e hash de 32. Ficam em `PasswordHashPolicy` e vão dentro da string PHC. Mudar os parâmetros depois refaz o hash no login seguinte, pelo mesmo caminho do legado.
- **Alternativa: PBKDF2-HMAC-SHA256 com 600.000 iterações**, no BCL, sem pacote. É o caminho de "criptografia crua do BCL" da casa (`OperatorPasswordHasher`, `AesGcmMcpCredentialCipher`). Ela fica registrada como a saída se a dependência nativa reprovar na verificação: muda o formato do hash, e nada mais no desenho.
- **Custo por verificação:** ~19 MiB de memória e dezenas de ms de CPU. É vetor de negação de serviço, e o limite global abaixo existe também por isso.

**Verificação sem enumeração:**
- e-mail desconhecido verifica contra um hash fictício de mesmos parâmetros, para o tempo de resposta não dizer se a conta existe;
- a mensagem é uma só, "E-mail ou senha inválidos.", no idioma da mensagem de hoje;
- usuário desativado com a senha **certa** recebe `403` "Acesso desativado. Fale com o administrador." (o texto do protótipo). Com a senha errada, recebe a mensagem genérica. A mensagem distinta só aparece para quem já provou a senha (**decisão do dono, 28/10/2026**).

**Bloqueio progressivo, por e-mail, exista ou não a conta:**
- tabela `identity_login_throttle`, com `normalized_email` como chave, `consecutive_failures`, `last_lock_minutes` (0 enquanto não houve bloqueio), `locked_until` e `last_failure_at`;
- a chave é o e-mail, e não a linha do usuário. Assim, o aviso "acesso pausado" aparece igual para conta existente e inexistente, e não serve para descobrir quem tem conta.

| regra | valor |
|---|---|
| primeiro bloqueio | **5** falhas seguidas → **5 min** (texto do protótipo). A 5ª falha responde `401`, e a tentativa seguinte, `423` |
| reincidência | depois de um bloqueio vencido, **uma falha basta** para bloquear de novo, pelo **dobro** do último: 10, 20, 40 min, com **teto de 60 min** (**decisão do dono, 31/10/2026**) |
| volta ao início | **24 h sem falha** zeram a linha: contagem, último bloqueio e tudo. A regra é pelo tempo (`last_failure_at`), e não pela limpeza: uma linha com a última falha há mais de 24 h vale como inexistente, mesmo que o ciclo horário ainda não a tenha removido. Assim, o resultado não depende de quando a limpeza roda (**decisão do dono, 31/10/2026**) |
| durante o bloqueio | `423 Locked` com `Retry-After`, **sem verificar a senha**: nem o custo do Argon2 nem o resultado vazam. A tentativa não conta como falha e não estende o bloqueio |
| sucesso | zera a linha, inclusive o último bloqueio |
| desativado com a senha certa | o `403` **não conta como falha e não zera** a linha: a contagem fica como estava (**decisão do dono, 31/10/2026**). Com a senha errada, é uma falha como qualquer outra |
| limpeza | a linha sem falha há 24 h e fora de bloqueio sai no ciclo horário da `IdentityTokenPruneService`, ao lado das entradas de token, no mesmo serviço e no mesmo lock, sem serviço novo. É só arrumação: a regra acima já trata a linha como inexistente |

- **O `FakeTimeProvider` controla o relógio** do bloqueio nos testes.
- **Os parâmetros são decisão do dono (28/10/2026):** 5 falhas → 5 min, reincidência dobrando até 60 min, chave pelo e-mail mesmo para conta inexistente. O protótipo fixava só os 5 min. O dono aceitou o preço da chave por e-mail: um terceiro pode bloquear a conta de alguém por até 60 min, em troca de não revelar quem tem conta.

**Limite de tentativas, sem IP.** O `apps/api` não vê o IP do cliente: não há `ForwardedHeaders`, por decisão da #179, e o `RemoteIpAddress` é o do nginx do stack. Limite por IP aqui seria um limite global com outro nome. Então:

- **`RateLimiter` do ASP.NET Core** (framework compartilhado) no `POST /auth/session`:
  - **concorrência** de 4 verificações ao mesmo tempo, com fila de 16; acima disso, `429` com `Retry-After: 1`. Isso limita a memória do Argon2 a ~76 MiB;
  - **token bucket** de 10 por segundo, com rajada de 20.
- O bloqueio por e-mail cuida da força bruta numa conta. O limite global cuida da pulverização por muitas contas e do custo de CPU e memória.
- **Por IP é do proxy de fora** (nginx externo ou Cloudflare). O `docs/deployment.md` registra isso como recomendação de deploy, e não como garantia do código.

**Desativar:**
- `status = deactivated`, `deactivated_at`, `security_stamp` novo;
- o login com a senha certa recebe `403`;
- o authorize com cookie em voo é recusado (stamp);
- o refresh é recusado com `invalid_grant`, pelo handler do grant `refresh_token`, que relê o usuário;
- **o refresh carrega o `security_stamp`** (**pedido do dono, 31/10/2026**):
  - o principal do authorize leva o claim do stamp sem destino. O OpenIddict guarda no código e no refresh todos os claims do principal, e os destinos só filtram o access token e o id token. Então o stamp não sai em nenhum token legível pelo cliente;
  - a rotação herda o claim no refresh novo;
  - **todo token novo de usuário confere estado e stamp** (**decisão do dono, 31/10/2026**): o mesmo handler atende os grants `authorization_code` e `refresh_token`, e recusa com `invalid_grant` o inexistente, o desativado e o stamp do código ou do refresh diferente do gravado. Assim, um usuário desativado entre o authorize e a troca do código (até 5 min) não recebe tokens;
  - **por que o estado não basta:** desativar troca o stamp, e reativar (#219) não volta o antigo. Sem a comparação, um refresh de antes da desativação voltaria a renovar depois da reativação. Com ela, a reativação exige login novo. A troca de senha (#219) e a redefinição das 2 etapas (#218) também trocam o stamp, e derrubam as renovações pelo mesmo caminho, sem depender da revogação das autorizações;
- o usuário não é excluído (decisão da issue);
- a rota que desativa, e a revogação das autorizações (D3.10), são da #219. Aqui fica o comportamento do estado.

**2 etapas obrigatórias na M1, antes de existir papel: obrigatória para todo usuário, ligada só quando houver tela de cadastro** (**decisão do dono, 28/10/2026**: ligada na #220).

- **Por que todos:** a decisão é "obrigatória para Admin". Na M1, todo usuário tem o acesso do operador, que inclui gerir usuários (#219, #181). Todo usuário da M1 é Admin em poder, e o papel só nasce na M2. Exigir de "ninguém" até a M2 deixaria quem gere usuários com um fator só.
- **Por que não antes da tela:** ligar a obrigatoriedade sem a tela de cadastro (#183) faria todo usuário sem 2 etapas ficar sem entrar. A forma de apresentar o segredo, QR e texto, é decisão da #183. Então:
  - a #218 entrega o TOTP (cadastro, verificação, códigos de recuperação e redefinição pelo admin), valendo para quem o tiver ligado;
  - a **#220**, depois da #183, liga a obrigatoriedade. Quem não tiver as 2 etapas cadastra no próximo acesso, que é o fluxo do aviso do protótipo.
- **Entre a #217 e a #220,** o painel entra com e-mail e senha, como hoje entra com usuário e senha. Não piora nada, e cada PR fica implantável.
- **Na M2**, a #184 restringe a obrigatoriedade ao papel Admin ("opcional para Operador", no protótipo). A decisão em aberto dela, "Admin sem 2 etapas no momento em que a obrigatoriedade entra", já chega resolvida, porque na M1 todos terão.
- **Códigos de recuperação e "o que o admin faz quando a pessoa perde o dispositivo"** (decisão em aberto da issue): ficam na #218, com esta recomendação:
  - 10 códigos de uso único, mostrados uma vez e guardados só em hash;
  - "Redefinir verificação" do admin (protótipo, Editar usuário) apaga o segredo e os códigos, e troca o `security_stamp`;
  - a pessoa cadastra de novo no próximo acesso.

### D5. (e) Testes

Os guardas de cada linha são verificados contra o defeito (convenção 15): o defeito entre parênteses é reintroduzido, o teste reprova, e a árvore volta.

**`apps/api`, unidade:**

| teste | o que afirma | guarda verificado contra |
|---|---|---|
| `PasswordHasherTests` | Argon2id: ida e volta, PHC com os parâmetros; senha errada reprova; formato desconhecido reprova sem exceção | — |
| `BootstrapLegacyHashTests` | PBKDF2 legado verifica só com o marcador; sem o marcador, a mesma string reprova | verificador legado aceitando sem o marcador |
| `LoginThrottlePolicyTests` | 5 falhas → 5 min; depois de bloqueio vencido, uma falha bloqueia pelo dobro, até 60 min; 24 h sem falha voltam ao início, pela regra do tempo e sem a limpeza; sucesso zera (`FakeTimeProvider`) | política que exige 5 falhas de novo depois do bloqueio vencido (caso da reincidência); política que só volta ao início quando a linha é apagada (caso das 24 h) |
| `EmailNormalizationTests` | espaços nas pontas e caixa | — |
| `IdentityClientCatalogTests` | o descritor do `buteco-painel` é público, tem PKCE, não tem `client_credentials` e não tem segredo | descritor com `client_credentials` |
| `TokenEntryStorageHandlerTests` | a matriz da #209 cresce: nenhum access token, de nenhum grant, e nenhum id token grava linha; código e refresh gravam; a ordem é anterior ao `CreateTokenEntry` nas opções compostas | registro do handler comentado |
| `IssuerTokenSchemeSelectionTests` | forma → esquema; HMAC intacto; JWT inválido não chega ao `ITokenService` | seletor que tenta os dois esquemas |

**`apps/api`, integração (Testcontainers, `TestServer`):**

| teste | o que afirma | guarda verificado contra |
|---|---|---|
| `UserLoginFlowTests` | o fluxo inteiro sem navegador: `POST /auth/session` → cookie → `GET /connect/authorize` com PKCE → `302` com código → `POST /connect/token` → access token com `aud` [`buteco-api`, `buteco-inbox`, `buteco-connectors`], `client_id` `buteco-painel`, `sub` = id do usuário, `typ` `at+jwt`, verificável pelo JWKS | — |
| | authorize sem cookie → `302 /entrar?retorno=…`; com `prompt=none` → `login_required` no `redirect_uri` | — |
| | authorize sem `code_challenge` → erro, sem código | `RequireProofKeyForCodeExchange` retirado |
| | `code_verifier` errado → `invalid_grant` | — |
| | `redirect_uri` não registrado → erro, sem redirecionamento | — |
| | sem sessão e com `Host` de outro nome → o `retorno` decodificado começa por `/connect/authorize?`, traz a query original e não tem esquema nem host | `retorno` montado com `Request.GetDisplayUrl()` |
| | authorize antes da primeira sincronização do catálogo → `400` `invalid_request`, sem código e sem redirecionar; `GET /health` → `200`, sem `503` | gate esperando o catálogo |
| `SessionCsrfTests` | `Origin` de outra origem → `403`; sem `Origin` → `403`; `text/plain` ou formulário → `415`, sem cookie | conferência de `Origin` retirada |
| `LoginThrottleTests` | 5 falhas → `423` com `Retry-After`; a 6ª com a senha certa também `423`, sem verificar; e-mail inexistente bloqueia igual; sucesso depois do prazo zera | chave do bloqueio pela linha do usuário (o e-mail inexistente deixa de bloquear) |
| `LoginRateLimitTests` | acima da concorrência e da fila → `429` | — |
| `UserDeactivationTests` | desativado: login com a senha certa → `403` e com a errada → `401` genérico; com a senha certa, não conta falha nem zera a contagem; refresh → `invalid_grant`; desativado entre o authorize e a troca do código → `invalid_grant`; desativado e reativado → o refresh de antes recebe `invalid_grant`; cookie em voo recusado no authorize | releitura do usuário no handler do refresh retirada; comparação do stamp retirada (caso do reativado); comparação no grant `authorization_code` retirada (caso do desativado antes da troca do código); `403` tratado como sucesso no bloqueio (zera a contagem) |
| `RefreshRotationTests` | refresh → novo par, o anterior `redeemed`; reuso depois de 30 s revoga a cadeia; `last_access_at` muda no login e no refresh, e **não** numa chamada a `/agents` | — |
| `IssuerTokenAcceptanceTests` | token do fluxo → `GET /agents` `200`; token sem `buteco-api` → `401`; token de cliente fora da tabela → `403`; HMAC `operator` → `200`, como antes; `sub` `operator` forjado num cliente desconhecido → `403` | `IssuerClientSubjects` trocado por `sub` |
| `EndSessionTests` | logout com hint válido → autorização `revoked`, cookie apagado, `302` ao `post_logout_redirect_uri`; refresh da sessão recusado depois; hint inválido → cookie apagado e `302 /entrar` | — |
| `UserBootstrapTests` | sem usuário e com as variáveis → um Admin, hash legado marcado; o primeiro login refaz em Argon2id e tira o marcador; com usuário → nada muda e o log avisa; sem as variáveis → `Warning`; duas instâncias → um usuário | reaplicação a cada boot (a senha muda) |
| `IdentityWarmupTests` | o bootstrap falha no primeiro ciclo (falha injetada uma vez) → `Warning` com o `EventId` dele, o anel carrega e o catálogo sincroniza mesmo assim; no ciclo seguinte o usuário existe | o laço de hoje, que só repete enquanto o anel não carregou (o usuário nunca é criado) |
| `IdentityKeyRetentionRefreshTests` | refresh emitido pela chave anterior e apresentado 13 dias depois da troca → novo par | retenção de volta a 24 h |
| `IdentityClientCatalogSyncTests` | o aquecimento cria o cliente e os três escopos; um `buteco-painel` adulterado (confidencial, com `client_credentials`) volta ao descritor; um `buteco-x` desconhecido gera `Error` e fica | sincronização que só cria e não corrige |
| `TokenStorageTests` (existente, estendido) | depois do fluxo: código e refresh com linha; access e id sem linha | — |
| `IdentityIssuerStartupValidationTests` (estendido) | retenção menor que a duração do refresh + `CheckInterval` → boot falha, com a mensagem citando o refresh; sem `PublicUrl:BaseUrl` → boot falha; rota do passthrough removida → boot falha; URI não passthrough coincidindo com rota → boot falha, como hoje | remoção da chamada no `Program.cs` |

**`tests/` (acordo, convenção 11).** Nos dois projetos, o cliente confidencial `buteco-painel` de teste **deixa de existir**: a sincronização do D3.1 o sobrescreveria para público no primeiro aquecimento, e os testes atuais quebrariam. O que fica no lugar:

- **`InboxOrchestratorRoundTrip.Tests/IssuerTokenRoundTripTests`:**
  - a fixture cria um usuário pelo bootstrap (variáveis da fixture) e faz o fluxo real pelo `TestServer` do `apps/api` (sessão, authorize com PKCE e token);
  - manda o access token a `GET /channels` do `apps/inbox` e recebe `200`;
  - independência: com o `apps/api` inalcançável, a segunda chamada continua `200`.
- **`ApiConnectorsRoundTrip.Tests/IssuerTokenRoundTripTests`:**
  - o mesmo token em `GET /connectors/providers` recebe `200`;
  - a recusa por `aud` cruzada continua provada, com um cliente confidencial de teste de outro nome (`teste-so-inbox`), que não é `buteco-*` e pede só o escopo `buteco-inbox`.
- **O que isso prova com o artefato real:** que o token de usuário do fluxo real carrega o `client_id` e as audiences que os dois apps exigem. É o critério de aceite que a issue pede, menos a parte do painel (#217).

**Frontend e workers:** sem mudança de código, sem teste novo. A regra do schema pede teste unitário em todo app **tocado**. O `nginx.conf` é verificado à mão (D3.5), porque não há teste automatizado dele (#57).

### D6. (f) Migração e deploy

- **Duas migrations:**
  - **`AddLocalUsers`**, com `users`, `identity_login_throttle` e os índices. O `apps/workers` não mapeia nenhuma das duas, porque o espelho é por tabela usada (#179 D3);
  - **`ExtendIdentityKeyRetention`**, só de dados (D3.7). A janela da chave está gravada por linha (`identity_keys.validates_until`, criada como `signs_until + Retention`) e vira o `NotAfter` do certificado em memória (`IdentityKeyRing`). Mudar só a constante deixaria a chave ativa de hoje com 24 h, justo na primeira troca depois do deploy. A migration faz `UPDATE identity_keys SET validates_until = signs_until + interval '15 days' WHERE usage = 'Signing' AND validates_until < signs_until + interval '15 days'`. Ela só estende e nunca encurta, roda de novo sem efeito, e não toca a chave de cifra. Uma chave aposentada há menos de 15 dias volta ao anel e ao JWKS, o que só devolve o comportamento decidido. O `Down` não desfaz: encurtar recusaria refresh em voo.
- **`.env.prod`:** `AUTH_BOOTSTRAP_ADMIN_EMAIL`, nova. O `docker-compose.prod.yml` a passa ao `api` como `Auth__BootstrapAdminEmail`, sem `:?`, porque ela é opcional depois do bootstrap.
- **Ordem do deploy:**
  1. `migrator`;
  2. `api`;
  3. `frontend`, pelo nginx.

  O `inbox` e o `connectors` não mudam.
- **Rollback:** a imagem anterior do `api` e do `frontend`. As tabelas novas ficam sem efeito, e o cliente sincronizado também, porque a versão anterior não o lê. A versão anterior lê `validates_until` da linha, então mantém as janelas de 15 dias das chaves já gravadas, e as chaves que ela criar voltam a 24 h. O painel nunca deixou de usar o HMAC.

## Tamanho e divisão (aceita pelo dono em 28/10/2026)

**Conclusão: a #180, como está escrita, é grande demais para uma change revisável.**

**Âncoras decompostas** (convenção 18): só código, sem `openspec/` nem `.md`. A migration da #179 é gerada (~1.660 linhas) e fica fora da conta.

| PR | issue | código de produção | testes |
|---|---|---|---|
| #213 | #179 | ~1.300 linhas escritas, 21 arquivos | 1.572 linhas, 19 arquivos |
| #215 | #208 | 1.286 linhas, 26 arquivos (duas cópias) | 2.459 linhas, 29 arquivos |
| #216 | #209 | 273 linhas, 6 arquivos | 979 linhas, 10 arquivos |

**Projeção por componente**, sobre este design. Ainda é rascunho: a verificação de cada parte não fechou, e a projeção erra para baixo por construção.

| componente | produção | testes | onde fica |
|---|---|---|---|
| usuário, hash, bootstrap, bloqueio, limite, `/auth/session` | ~600 | ~900 | #180 |
| fluxo OIDC: passthrough do authorize e do logout, catálogo de cliente e escopos, handler do refresh, armazenamento, checagens de boot | ~500 | ~800 | #180 |
| o `apps/api` aceita o próprio token (seletor, `JwtBearer`, tabela de clientes) | ~200 | ~400 | #180 |
| acordo de `tests/` com o fluxo real | — | ~300 | #180 |
| **#180 recortada** | **~1.300** | **~2.400** | na ordem da #208 |
| TOTP: cadastro, verificação, recuperação, redefinição, cifra do segredo, migration | ~500 | ~700 | #218 |
| Minha conta e Usuários: 11 operações (perfil, trocar senha, sessões, encerrar; listar, detalhe, editar, desativar, reativar, encerrar de outro, redefinir 2 etapas). A régua é o custo por operação CQRS: ~4 arquivos e ~80 linhas cada | ~900 | ~900 | #219 |
| painel: cliente OIDC (PKCE, callback, renovação, `503`/`401`) nas 7 cópias do `request<T>`, `/entrar` mínimo, proxy do Vite, e a remoção do `POST /auth/login` no `apps/api` | ~500 | ~500 | #217 |
| obrigatoriedade das 2 etapas | ~50 | ~150 | #220 |
| **#180 inteira** | **~3.250** | **~4.650** | ~2,5 vezes a #179 e a #208 somadas |

**Por que dividir, além do tamanho:**

- **Mistura de apps.** O critério de aceite da #180 ("o painel faz login…") obriga a mexer no SPA: as 7 cópias do `request<T>`, o `/entrar` e o callback. Isso contraria "tela é change separada e vem depois do backend" (convenção 1) dentro da mesma change. O cliente OIDC do painel não é tela, mas também não é backend. Ele merece change própria, entre o backend e a #183.
- **Obrigatoriedade das 2 etapas sem tela:** ver D4. Ela não pode entrar no mesmo PR do TOTP sem deixar o `main` sem login para quem não tiver cadastro.
- **A transição decidida continua valendo:** "a #180 passa a emitir o token do operador pelo emissor" acontece, só que dividida. O backend emite na #180, e o painel passa a pedir na #217. A #182 só remove o HMAC depois que o painel deixou de usá-lo.

**Issues novas na M1** (convenção 23), criadas em 28/10/2026, com os `blocked-by` gravados e postas em `Ready` na ordem abaixo:

| pos. | issue | título proposto | apps | depende de (`blocked-by`) |
|---|---|---|---|---|
| 4 | **#180** (recortada) | Usuários locais e login com e-mail e senha pelo emissor (authorization code + PKCE) | api, deploy | #179 #208 #209 |
| 5 | **#217** | Painel entra pelo emissor: authorization code + PKCE, renovação, `503` passageiro, e o fim do `POST /auth/login` | frontend, api | #180 |
| 6 | **#218** | Verificação em 2 etapas (TOTP): cadastro, verificação no login, códigos de recuperação e redefinição pelo admin | api | #180 |
| 7 | **#219** | Rotas de Minha conta e de Usuários: senha, sessões, editar, desativar e reativar | api | #180 #218 |
| 8 | #181 | (sem mudança) Convites e redefinição de senha por link | api | #180 |
| 9 | #182 | (sem mudança de escopo) Remover o HMAC e a chave compartilhada | api, inbox, connectors, deploy | #180 **#217** |
| 10 | #211 | (sem mudança) Rota de leitura do emissor e das chaves | api | #179 |
| 11 | #183 | (sem mudança de escopo) Painel da M1 | frontend | #179 #208 #180 #181 #182 #211 **#217 #218 #219** |
| 12 | **#220** | 2 etapas obrigatórias para todo usuário da M1 | api | #183 |

**`blocked-by` que mudam fora da M1:**
- **#184** passa a depender também da **#217**. Ela recusa HMAC `operator` com `401` nos três apps, e o painel precisa estar fora do HMAC antes disso. Sem a #217, a #184 tiraria o painel do ar.
- A #184 não precisa da #220: a obrigatoriedade restrita ao Admin é dela, e na M1 todos já terão.

**O que muda no corpo das issues** se a divisão for aceita:
- **#180:** o título; o critério de aceite vai para a #217; "Verificação em 2 etapas", Minha conta e Usuários saem para a #218 e a #219; as heranças ficam.
- **#182:** "Pré-condição: o token do operador já sai do emissor (#180)" passa a citar a #217.
- **#183:** passa a depender da #217, da #218 e da #219.
- **#194:** a lista vai de 8 para 12 itens.
- **`02`:** a fila da M1 é atualizada.

**Alternativa considerada: a #217 dentro da #183**, com o painel trocando de login só com as telas definitivas. Ela inverteria a ordem decidida com a #182: a #183 depende da #182, e a #182 não pode remover o HMAC com o painel ainda nele. Seria preciso pôr a #183 antes da #182, e a #183 ficaria ainda maior.

**Alternativa para a #220: dentro da #183**, como exceção declarada a "tela não leva backend". É menor no board, mas abre o precedente. A proposta é a #220 separada.

## Árvore de pastas proposta (#180 recortada)

```
apps/api/src/Buteco.Api/
├── Users/                                          NOVO
│   ├── User.cs                                     entidade (D4)
│   ├── UserStatus.cs                               active | deactivated
│   ├── EmailNormalization.cs                       trim + ToLowerInvariant
│   ├── Passwords/
│   │   ├── PasswordHashPolicy.cs                   parâmetros do Argon2id (D4)
│   │   ├── PasswordHasher.cs                       Argon2id com NSec; PHC; necessidade de refazer
│   │   └── BootstrapLegacyPasswordVerifier.cs      PBKDF2 só com o marcador (D2)
│   ├── Bootstrap/
│   │   ├── UserBootstrapOptions.cs                 Auth:BootstrapAdminEmail (+ Auth:OperatorPasswordHash existente)
│   │   └── UserBootstrapper.cs                     cria o primeiro Admin sob o lock BUTECOU1 (D2)
│   └── Login/
│       ├── LoginThrottlePolicy.cs                  5 → 5 min, dobra até 60 min (D4)
│       ├── LoginThrottle.cs                        entidade de identity_login_throttle
│       ├── LoginThrottleStore.cs                   ler, registrar falha, zerar; limpeza chamada pelo ciclo horário
│       ├── SessionEndpoints.cs                     POST /auth/session (D1)
│       ├── SessionRequestGuards.cs                 JSON e Origin (D1, CSRF)
│       └── LoginRateLimiting.cs                    concorrência + token bucket (D4)
├── Identity/
│   ├── IdentityIssuerServiceCollectionExtensions.cs   + code, refresh, end session, passthrough, PKCE, durações (D3.7),
│   │                                                     esquema de cookie ButecoSession; sem o ramo "desligado" (D3.4)
│   ├── IdentityIssuerOptions.cs                    + PanelBaseUrl (D3.4)
│   ├── IdentityIssuerEndpoints.cs                  + authorize e logout, marcados Passthrough (D3.6)
│   ├── IdentityIssuerStartupValidation.cs          + PublicUrl obrigatória; passthrough ↔ rota mapeada (D3.6)
│   ├── Clients/                                    NOVO
│   │   ├── IdentityClients.cs                      descritor do buteco-painel, os três escopos, prefixo reservado (D3.1, D3.2)
│   │   └── IdentityClientCatalog.cs                sincronização no aquecimento, sob o lock BUTECOC1
│   ├── Flow/                                       NOVO
│   │   ├── AuthorizeEndpoint.cs                    GET /connect/authorize em passthrough (D1)
│   │   ├── EndSessionEndpoint.cs                   GET /connect/logout em passthrough (D3.9)
│   │   ├── UserPrincipalFactory.cs                 sub, e-mail, nome, escopos, recursos, destinos
│   │   └── UserGrantTokenHandler.cs                HandleTokenRequestContext do refresh: relê o usuário, último acesso (D3.11)
│   ├── Validation/                                 NOVO
│   │   ├── IssuerTokenAuthentication.cs            esquema de política + JwtBearer com as chaves do anel (D3.3)
│   │   ├── IssuerTokenAudience.cs                  "buteco-api"
│   │   ├── IssuerClientSubjects.cs                 buteco-painel → operator
│   │   └── IssuerTokenStartupValidation.cs         ValidateIssuerClientSubjects
│   ├── Storage/
│   │   ├── TokenEntryStorageHandler.cs             era ClientCredentialsAccessTokenStorageHandler (D3.8)
│   │   └── IdentityTokenPruner.cs                  + limpeza de identity_login_throttle (D4)
│   └── Keys/IdentityKeyRotationService.cs          o aquecimento em três passos independentes (D3.13)
├── Auth/
│   ├── AnonymousRouteClassification.cs             + SessionEnd
│   └── (OperatorTokenAuthenticationHandler, TokenService, /auth/login: sem mudança até a #217)
├── A2A/AgentA2AAddressBuilder.cs                   sem o ramo de url ausente (D3.4)
├── Infrastructure/
│   ├── AppDbContext.cs                             + DbSet<User>, DbSet<LoginThrottle>
│   ├── Migrations/<data>_AddLocalUsers.cs          NOVO
│   └── Migrations/<data>_ExtendIdentityKeyRetention.cs NOVO, só dados (D6)
├── Program.cs                                      esquema padrão Bearer, rotas novas, rate limiter, validações
├── appsettings.json                                "Auth": { "BootstrapAdminEmail": "" }
└── appsettings.Development.json                    Identity:PanelBaseUrl = http://localhost:5173; e-mail de bootstrap de dev

apps/api/tests/Buteco.Api.Tests/
├── Users/                                          NOVO: PasswordHasherTests, BootstrapLegacyHashTests, LoginThrottlePolicyTests,
│                                                         EmailNormalizationTests, UserBootstrapTests, LoginThrottleTests,
│                                                         LoginRateLimitTests, SessionCsrfTests, UserDeactivationTests
├── Identity/
│   ├── UserLoginFlowTests.cs, RefreshRotationTests.cs, EndSessionTests.cs           NOVOS
│   ├── IdentityClientCatalogTests.cs, IdentityClientCatalogSyncTests.cs              NOVOS
│   ├── IssuerTokenSchemeSelectionTests.cs, IssuerTokenAcceptanceTests.cs            NOVOS
│   ├── TokenEntryStorageHandlerTests.cs            era ClientCredentialsAccessTokenStorageHandlerTests
│   ├── IdentityTokenStorageTests.cs, IdentityIssuerStartupValidationTests.cs        estendidos
│   └── IdentityIssuerDisabledTests.cs              REMOVIDO (D3.4)
├── AgentA2AAddressesTests.cs                       sem o caso de url ausente
└── Support/
    ├── IdentityIssuerTestClient.cs                 + fluxo de usuário (sessão, authorize com PKCE, token)
    ├── ApiFactoryWithoutPublicUrlFixture.cs        REMOVIDO
    └── TestAuthentication.cs                       + Auth:BootstrapAdminEmail nos overrides

tests/InboxOrchestratorRoundTrip.Tests/IssuerTokenRoundTripTests.cs   fluxo real de usuário, sem cliente confidencial buteco-painel
tests/ApiConnectorsRoundTrip.Tests/IssuerTokenRoundTripTests.cs       idem; aud cruzada com teste-so-inbox
tests/*/Support/RoundTripFixture.cs                                    variáveis de bootstrap e cliente de fluxo

apps/frontend/deploy/nginx.conf                     + location = /connect/authorize e = /connect/logout (D3.5)
Directory.Packages.props                            + NSec.Cryptography 26.4.0
docker-compose.prod.yml                             + Auth__BootstrapAdminEmail no api
.env.prod.example                                   + AUTH_BOOTSTRAP_ADMIN_EMAIL
docs/configuration.md, docs/deployment.md, docs/development.md, docs/architecture.md, 01, 02, CHANGELOG.md
```

**Nada vai para `libs/`** (D3.3).

**O que cada parte afeta, por app** (#180 recortada):

| parte | `apps/api` | `apps/frontend` | `apps/inbox` / `apps/connectors` | `apps/workers` | `tests/` | deploy |
|---|---|---|---|---|---|---|
| usuário, hash, bootstrap, bloqueio, limite, sessão | código, migration, testes | — | — | — | — | variável nova |
| fluxo OIDC e cliente | código, testes | — | — | — | — | — |
| `apps/api` aceita o próprio token | código, testes | — | — | — | — | — |
| url pública obrigatória | código, testes removidos | — | — | — | — | — |
| nginx | — | `deploy/nginx.conf` | — | — | — | rebuild do `frontend` |
| acordo | — | — | só rodam | — | os dois projetos | — |

**Baseline** (`3bb8236`, registrada no `tasks.md` quando ele for escrito): `apps/api` e os dois projetos de `tests/`. O `apps/inbox` e o `apps/connectors` não entram, porque o código deles não muda. O `apps/workers` também não, porque não há espelho de tabela nova.

## Risks / Trade-offs

- **[Primeira dependência nativa do `apps/api` (libsodium)]** → verificação antes de qualquer código: restore, carga na imagem e no macOS, e API lida na fonte. Se reprovar, a saída é o PBKDF2-SHA256 de 600.000 iterações do BCL, que muda só o formato do hash (D4).
- **[Argon2 como vetor de negação de serviço]** → limite global de concorrência e de vazão no `/auth/session`. Durante o bloqueio, a senha não é verificada. Coberto por `LoginRateLimitTests` e `LoginThrottleTests`.
- **[Sem limite por IP no `apps/api`]** → declarado. É do proxy de fora, e o `docs/deployment.md` registra. O bloqueio por e-mail e o limite global não dependem de IP. A **#221** (aguardando gatilho) é o caminho para ler o IP real por cabeçalho de proxy confiável. O gatilho é o primeiro `429` em login legítimo, ou um pedido de limite por IP que o proxy de fora não atenda.
- **[A tabela de bloqueio cresce com e-mails inventados]** → limpeza no ciclo horário, para linhas paradas há 24 h. O volume máximo fica limitado pelo token bucket: 10 por segundo, ou ~864 mil por dia no pior caso, com a limpeza levando o que passou de 24 h. Coberto por teste no ciclo.
- **[Bloqueio por e-mail permite bloquear a conta de outra pessoa]** → é o preço do bloqueio sem enumeração. O teto de 60 min limita o efeito, e o dono da conta volta depois do prazo. Aceito pelo dono em 28/10/2026.
- **[Access token válido por até 15 min depois de desativar]** → declarado (D3.8). A renovação é recusada. Coberto por `UserDeactivationTests`.
- **[O passthrough exige rota mapeada, e a checagem de boot da #179 proibia isso]** → a checagem é corrigida para os dois casos, com teste da composição real (D3.6).
- **[A sincronização do cliente sobrescreve o que está no banco]** → é o comportamento desejado (D3.1). Um `buteco-*` desconhecido não é apagado, só logado.
- **[O hash legado do bootstrap vive no `.env.prod` até a #217]** → é a mesma exposição de hoje. O primeiro login refaz o hash. O `docs/deployment.md` orienta a troca de senha e a remoção da variável depois da #217.
- **[Os testes de acordo atuais usam um `buteco-painel` confidencial, que a sincronização sobrescreveria]** → os dois projetos passam ao fluxo real (D5). Os 12 e os 8 testes de hoje são comparados por nome no fechamento.
- **[`AccessTokenLifetime` de 15 min vale para todo fluxo]** → o `client_credentials` sem cliente em produção não é afetado. A #186 já supõe pedido a cada 10 min.
- **[Hint de sessão revogada no encerramento de sessão]** → o logout local acontece nos dois casos. Coberto por `EndSessionTests` (D3.9).

## Migration Plan

1. **Antes:** pôr `AUTH_BOOTSTRAP_ADMIN_EMAIL` no `.env.prod`, com o e-mail do primeiro Admin. `AUTH_OPERATOR_PASSWORD_HASH` continua igual.
2. **Deploy:**
   - o `migrator` cria `users` e `identity_login_throttle`;
   - o `api` sobe, e o aquecimento carrega o anel, sincroniza o cliente e os escopos, e cria o Admin;
   - o `frontend` recebe o nginx novo.
3. **Conferência pós-deploy:**
   - o log do `api` tem a criação do Admin, ou "bootstrap ignorado";
   - o discovery anuncia `authorization_endpoint` e `end_session_endpoint` em `https://`;
   - os quatro `curl` do D3.5 contra o domínio;
   - o painel continua entrando pelo HMAC, como antes.
4. **Rollback:** a imagem anterior do `api` e do `frontend`. As tabelas e o cliente ficam, sem efeito.

## Open Questions

Nenhuma. As quatro de produto foram resolvidas pelo dono em 28/10/2026 e viraram decisão:

- sessão: refresh deslizante de 14 dias (D3.7);
- bloqueio: 5 falhas → 5 min, reincidência dobrando até 60 min, chave pelo e-mail mesmo para conta inexistente (D4);
- desativado com a senha certa: "Acesso desativado. Fale com o administrador."; com a senha errada, a mensagem genérica (D4);
- 2 etapas obrigatórias para todo usuário da M1, ligadas na #220 (D4).
