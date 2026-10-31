**Base:** `origin/main` em `3bb8236` (merge do PR #216), 27/10/2026.
- **Worktree da change:** `../buteco-agentes-180`, branch `feat/180-usuarios-locais-login-emissor`.
- **Baseline:** worktree limpo separado, `../buteco-agentes-180-baseline`, no mesmo commit, sem edição nenhuma.

**Saídas completas:** `.log` inteiro, `.trx` e estado da máquina colado ao número, nunca filtrados (convenção 19), em:
- `~/.cache/buteco-agents/180-baseline/`;
- `~/.cache/buteco-agents/180-verificacao/`;
- `~/.cache/buteco-agents/180-fechamento/`.

**Variáveis em toda rodada:** `DOCKER_HOST` do Podman, `TESTCONTAINERS_RYUK_DISABLED=true`, `DOTNET_SYSTEM_NET_DISABLEIPV6=1` e `TZ=America/Sao_Paulo`.

**Antes de cada suíte**, e não só da primeira, cada uma destas condições é conferida:
- `CPU_Speed_Limit = 100`;
- load de 1 min abaixo de 8;
- relógio da VM acertado;
- nenhum contêiner em `podman ps`.

**O que muda nesta change:**
- código no `apps/api`;
- o `apps/frontend` só no `deploy/nginx.conf`;
- os dois projetos de `tests/`, nas fixtures e nos testes de acordo.

O `apps/inbox`, o `apps/connectors` e o `apps/workers` não mudam. O espelho do `apps/workers` não mapeia as tabelas novas (design, "Árvore de pastas").

**Paradas obrigatórias:**
- **grupo 2:** qualquer verificação que reprovar;
- **em qualquer grupo:** qualquer premissa do design que se mostrar falsa (convenção 9).

Nos dois casos, a decisão volta ao dono antes de seguir.

## 1. Baseline, antes de tocar código

- [x] 1.1 (`apps/api`, `tests/InboxOrchestratorRoundTrip.Tests`, `tests/ApiConnectorsRoundTrip.Tests`) Baseline no worktree limpo em `3bb8236`, pelo protocolo da 1.1 da #179. Toda falha é investigada antes de ser chamada de pré-existente.

      **Resultado, 27/10/2026 (UTC), worktree `../buteco-agentes-180-baseline` em `3bb8236`, sem edição nenhuma.** Em cada suíte, na largada e no fim:
      - `CPU_Speed_Limit = 100`;
      - relógio da VM acertado antes da largada;
      - nenhum contêiner em `podman ps`.

      As três rodaram em sequência:

      | suíte | resultado | duração (vstest) | início → fim (UTC) | load de 1 min na largada → no fim |
      |---|---|---|---|---|
      | `apps/api` | **889/889 + 1 ignorado** (o de medição da #209, sem `BUTECO_MEDICAO=1`) | 1m44s | 05:15:34 → 05:17:46 | 2,43 → 11,18 |
      | `tests/InboxOrchestratorRoundTrip.Tests` | **12/12** | 18 s | 05:18:27 → 05:19:18 | 6,32 → 5,92 |
      | `tests/ApiConnectorsRoundTrip.Tests` | **8/8** | 6 s | 05:19:20 → 05:19:41 | 5,92 → 5,64 |

      São os mesmos números do fechamento da #209. As saídas estão em `~/.cache/buteco-agents/180-baseline/{api,inbox-roundtrip,connectors-roundtrip}.{log,trx}`, com o estado em `*-estado.txt`. O `apps/inbox` e o `apps/connectors` ficam fora, porque o código deles não muda.

## 2. Verificação antes de qualquer código (parada obrigatória se reprovar)

- [x] 2.1 (`apps/api`) **Restore com `NSec.Cryptography` 26.4.0.** Acrescentar o pacote ao `Directory.Packages.props` e ao `Buteco.Api.csproj` e rodar `dotnet restore --force` do `Buteco.Api.Tests`, com o log em `~/.cache/buteco-agents/180-verificacao/restore.log`. Registrar:
  - a versão resolvida da `libsodium`;
  - os pacotes novos no grafo;
  - a ausência de `NU1605`/`NU1608` e de aviso de vulnerabilidade novo, comparado à baseline. O `NU1903` do `SSH.NET` é o da #146.

  **Se houver conflito ou vulnerabilidade, parar.**

  **Resultado, 31/10/2026: passa.** Logs em `restore.log` e `restore-baseline.log`, este no worktree da baseline, com `exit 0` nos dois.
  - **Grafo:** entram só `NSec.Cryptography` 26.4.0 e `libsodium` 1.0.22. O NSec fixa `[1.0.22, 1.0.23)`. Nenhum pacote sai nem muda de versão, comparando os dois `project.assets.json`.
  - **Avisos:** os únicos são os dois `NU1903` do `SSH.NET` 2025.1.0 (#146), iguais nos dois lados. Não há `NU1605`/`NU1608`.
  - **Binários nativos:** a `libsodium` traz `osx-x64`, `osx-arm64`, `linux-x64`, `linux-arm64`, `linux-musl-*` e `win-*`.
- [x] 2.2 (fora do repositório) **Carga da libsodium e Argon2id de verdade.** Num projeto de console descartável no scratchpad, que referencia só o `NSec.Cryptography` 26.4.0, derivar um hash Argon2id com m = 19.456 KiB, t = 2, p = 1 e verificá-lo. Rodar:
  - (a) no macOS de desenvolvimento, com `dotnet run`;
  - (b) publicado (`linux-x64` e `linux-arm64`) e executado dentro de `mcr.microsoft.com/dotnet/aspnet:10.0` pelo Podman, a mesma base do `apps/api/Dockerfile`.

  Registrar a saída e o tempo por verificação em cada um. **Se o binário nativo não carregar em algum deles, parar.** A saída prevista no design (D4) é o PBKDF2-SHA256 de 600.000 iterações, e a escolha volta ao dono.

  **Resultado, 31/10/2026: passa nos três.** O projeto fica em `scratchpad/argon2-probe`. A senha vai em UTF-8 (2.3), e cada rodada faz uma derivação e cinco verificações com `FixedTimeEquals`:

  | ambiente | RID | primeira | verificações |
  |---|---|---|---|
  | macOS 15.7.9, x86_64 | `osx-x64` | 25,9 ms | 24,0 a 27,6 ms |
  | `aspnet:10.0`, Podman | `linux-x64` | 25,2 ms | 21,3 a 24,7 ms |
  | `aspnet:10.0` emulado (QEMU) | `linux-arm64` | 337,9 ms | 286 a 294 ms |

  O tempo do `arm64` é da emulação, não do hardware, e não vale como medida. Nos três, a senha certa confere e a errada não.

  **Correção do design:** a imagem `aspnet:10.0` é **Ubuntu 24.04.5**, e não Debian. O que importa ao binário é a glibc, e isso se mantém.

  **Interoperabilidade com a referência**, `argon2-cffi` 25.1.0, rodado num contêiner `python:3.13-slim`:
  - para o mesmo sal e a mesma senha, o hash bruto é idêntico;
  - a string PHC `$argon2id$v=19$m=19456,t=2,p=1$zDMapcVyaZsjkrkalXvucw$q2EOfrQAF9Dwnvxy4cMmdf6sQnbF0y7do6pwslBFeko`, da senha `senha de teste çã`, é aceita pelo `PasswordHasher().verify`. Ela vira o vetor conhecido do `PasswordHasherTests`.
- [x] 2.3 (fonte) **API do Argon2id lida na tag `26.4.0` do NSec.** Registrar com arquivo e linha:
  - o tipo e o construtor do algoritmo e dos parâmetros;
  - o limite de paralelismo que a libsodium impõe;
  - como o hash é produzido (derivação de chave, não string PHC: o PHC é montado por nós);
  - a comparação em tempo constante disponível.

  **Se a forma não permitir os parâmetros do design, parar.**

  **Resultado, 31/10/2026: passa.** A fonte é o tarball da tag em `scratchpad/nsec-26.4.0`.
  - **Tipo e parâmetros:** `PasswordBasedKeyDerivationAlgorithm.Argon2id(in Argon2Parameters)` (`PasswordBasedKeyDerivationAlgorithm.cs` l. 44–48) cria `Argon2id` (`Argon2id.cs` l. 77–110). O `Argon2Parameters` é uma struct com `DegreeOfParallelism`, `MemorySize` em KiB e `NumberOfPasses` (`Argon2Parameters.cs` l. 7–15).
  - **Limites:** `p` precisa ser 1 (`Argon2id.cs` l. 87–90), `m` ≥ 8 KiB (l. 91–95, `MEMLIMIT_MIN = 8192` bytes) e `t` ≥ 1 (l. 96–100). O sal é de exatamente 16 bytes (`Interop.Pwhash.Argon2id.cs` l. 14; construtor l. 79). Os parâmetros do design cabem.
  - **Saída:** bytes brutos, por `DeriveBytes(ReadOnlySpan<byte> password, salt, count)` (`PasswordBasedKeyDerivationAlgorithm.cs` l. 96–123), via `crypto_pwhash_argon2id` (`Argon2id.cs` l. 133–141). O PHC é montado por nós.
  - **Ressalva para o código:** o overload com `string` usa os bytes UTF-16 da senha, por `MemoryMarshal.AsBytes(password.AsSpan())` (l. 56–66). Ele não é usado, e a senha entra em UTF-8 pelo overload de `ReadOnlySpan<byte>`, como as outras implementações.
  - **Comparação:** o NSec não tem API própria. Ele mesmo usa `CryptographicOperations.FixedTimeEquals` do BCL (`HashAlgorithm.cs` l. 170), e nós também.
- [x] 2.4 (fonte, OpenIddict 7.7.2) Conferir por leitura, com arquivo e linha, antes de o código depender. Se algum desses pontos contradisser o design, parar:
  - (a) com o passthrough do authorize e do logout, um pedido inválido (`redirect_uri` não registrado, sem `code_challenge`) é recusado **antes** de chegar à rota;
  - (b) com `id_token_hint` sem `oi_tkn_id`, nenhuma linha de token é consultada, e se a autorização do `oi_au_id` é conferida (design D3.9). Registrar o que acontece com hint inválido no logout em passthrough, e se é preciso `EnableErrorPassthrough` para "sempre termina";
  - (c) a constante do requisito de PKCE por cliente e a de consentimento implícito no descritor da aplicação;
  - (d) a expiração deslizante do refresh como padrão (G3), e o efeito de `SetRefreshTokenLifetime(14 dias)` na checagem de boot da #209 (igualdade passa);
  - (e) que um `HandleTokenRequestContext` nosso nos grants `authorization_code` e `refresh_token` pode recusar com `invalid_grant` depois de o OpenIddict ter restaurado o principal, e que o claim sem destino do stamp chega restaurado nos dois;
  - (f) **conferido em 31/10/2026, premissa da #179 D8 falsa para o refresh** (design D3.7). O refresh, o código e o `id_token_hint` são validados com as chaves de `options.SigningCredentials`:
    - `OpenIddictServerConfiguration.cs` l. 556–558: as credenciais viram `IssuerSigningKeys`;
    - `OpenIddictServerHandlers.Protection.cs` l. 104–113 e 189: esses parâmetros valem para todo token do servidor; l. 476: a validação; l. 509: chave ausente recusa; l. 1478–1486: todo token é assinado pela ativa;
    - `OpenIddictServerHandlers.cs` l. 2143–2152: o refresh; l. 2059–2070: o hint, sem validade conferida.

    A correção entra na 6.9. Na leitura da (b), confirmar que nada além da assinatura limita a idade do hint no logout.

  **Resultado de (a) a (e), 31/10/2026: passa, com uma decisão de implementação na (b).** A fonte é o tarball da 7.7.2.
  - **(a)** O passthrough é ligado no evento `Handle*` (`OpenIddictServerAspNetCoreHandlers.Authentication.cs` l. 31, `Session.cs` l. 28, `AspNetCoreHandlers.cs` l. 858–897). A validação vem antes e retorna na recusa (`OpenIddictServerHandlers.Authentication.cs` l. 205–235 antes de l. 268; `Session.cs` l. 141–170 antes de l. 199). Pedido inválido não chega à rota, **desde que o `EnableErrorPassthrough` fique desligado**.
  - **(b)** Hint sem `oi_tkn_id` não consulta linha: o `RestoreTokenEntryProperties` sai cedo (`Protection.cs` l. 792–796). A autorização do `oi_au_id` **não** é conferida, porque o `AuthorizationId` só vem da linha do token (l. 857), e o `ValidateAuthorizationEntry` exige ele resolvido (l. 1400). Um hint de sessão já revogada continua válido, e a rota revoga de novo, sem efeito. O D3.9 previa os dois casos.

    **Corrigido na implementação (6.6, 31/10/2026), porque a primeira leitura estava errada:** o hint inválido **não** é recusado. No authorize e no logout o hint é opcional: `(RequireIdentityToken, ValidateIdentityToken, RejectIdentityToken) = (true, false, true, false)` (`OpenIddictServerHandlers.cs` l. 364–377). Com isso, a falha da validação só deixa o principal vazio (l. 2094–2105), e o pedido chega à rota. A rota, por sua vez, recebe do `AuthenticateAsync` um `Success` com principal **vazio**, e não nulo (`OpenIddictServerAspNetCoreHandler.cs` l. 207–209). O `EndSessionTests.InvalidHint_*` mostrou isso, redirecionando ao endereço registrado. Só o `post_logout_redirect_uri` não registrado é recusado na validação (`Session.cs` l. 541–609), e esse caso, sem `EnableErrorPassthrough`, viraria o erro local do OpenIddict sem apagar o cookie.

    **O `EnableErrorPassthrough` não serve:** a opção é global e mandaria à rota também os erros do authorize (`AspNetCoreHandlers.Authentication.cs` l. 42, `AspNetCoreHandlers.cs` l. 1223–1255), contra a (a).

    **Decisão de implementação:** a spec e o design não mudam, só o lugar de cada caso.
    - **Endereço não registrado:** um handler nosso de `ApplyEndSessionResponseContext`, ordenado logo depois do `AttachCacheControlHeader`. Ele apaga o cookie `buteco-sessao`, redireciona `302` para `/entrar` e encerra o pedido.
    - **Hint ausente ou inválido:** a rota só trata como hint o principal autenticado, apaga o cookie e vai a `/entrar`, mesmo com endereço registrado.
    - **Hint válido:** a rota revoga a autorização e segue o endereço registrado, ou `/entrar` se não houver endereço.
  - **(c)** `Requirements.Features.ProofKeyForCodeExchange = "ft:pkce"` e `ConsentTypes.Implicit = "implicit"` (`OpenIddictConstants.cs` l. 508–510 e 204–208).
  - **(d)** A expiração deslizante é o padrão: `DisableSlidingRefreshTokenExpiration` é falso (`OpenIddictServerOptions.cs` l. 408), e o refresh novo ganha a duração inteira (`OpenIddictServerHandlers.cs` l. 4473–4490). O padrão do refresh já é 14 dias (`Options.cs` l. 296), então a checagem da #209 (`MinimumAge < lifetime` reprova) passa na igualdade.
  - **(e)** O `AttachPrincipal`, de ordem `int.MinValue + 100_000`, restaura `AuthorizationCodePrincipal`/`RefreshTokenPrincipal` e usa como `Principal` (`Exchange.cs` l. 2095–2135). Um handler nosso de ordem padrão roda depois. `Reject` sem erro vira `invalid_grant` (`Exchange.cs` l. 236–243). O claim sem destino fica no código (`OpenIddictServerHandlers.cs` l. 3809) e no refresh (l. 4467), e só sai no access e no id token com destino (l. 3662 e 4648).

## 3. Usuário e senha (`apps/api`, D4)

- [x] 3.1 `Users/User.cs`, `UserStatus.cs` e `EmailNormalization.cs`. No `AppDbContext`: `DbSet<User>`, tabela `users` em snake_case e índice único em `normalized_email`. Mais a migration `AddLocalUsers`, gerada, com `users` e `identity_login_throttle` (3.3, com a coluna `last_lock_minutes`). Registrar o SQL gerado das duas tabelas.

  **Feito, 31/10/2026.** A migration é `20261031144517_AddLocalUsers`, e o SQL completo está em `180-verificacao/AddLocalUsers.sql`.
  - **`users`:** `id uuid` como PK, `email`, `normalized_email`, `name`, `status` (texto `active`/`deactivated`, por conversão), `password_hash` anulável, `password_hash_is_bootstrap_legacy boolean`, `security_stamp`, `created_at`, e `last_access_at` e `deactivated_at` anuláveis. Índice único `IX_users_normalized_email`.
  - **`identity_login_throttle`:** `normalized_email` como PK, `consecutive_failures`, `last_lock_minutes`, `locked_until` anulável e `last_failure_at`. Índice `IX_identity_login_throttle_last_failure_at`, para a limpeza.
- [x] 3.2 `Users/Passwords/PasswordHashPolicy.cs`, `PasswordHasher.cs` e `BootstrapLegacyPasswordVerifier.cs`:
  - o Argon2id grava e lê a string PHC;
  - o hasher diz quando o hash precisa ser refeito (legado ou parâmetros antigos);
  - o verificador PBKDF2 aceita o formato `{iterações}.{sal}.{hash}` só com o marcador;
  - formato desconhecido reprova sem exceção.

  Testes de unidade: `PasswordHasherTests`, `BootstrapLegacyHashTests` e `EmailNormalizationTests`. **Guarda verificado contra o defeito:** o verificador legado aceitando sem o marcador faz o `BootstrapLegacyHashTests` reprovar.

  **Feito, 31/10/2026.**
  - O `PasswordHasherTests` usa o vetor da 2.2, aceito pelo `argon2-cffi`.
  - Um hash com parâmetros antigos responde `SucceededNeedsRehash`.
  - Há um teto de sanidade: m ≤ 256 MiB e t ≤ 16. Acima disso, o hash reprova como formato desconhecido, para uma linha adulterada não travar o processo. O design não pedia o teto; ele foi acrescentado aqui.
  - **Guarda verificado:** com `isBootstrapLegacy || IsWellFormed(storedHash)` no lugar do marcador, `WithoutTheMarker_TheSameStringFails` reprova (1 de 10), e a árvore volta. O log está em `180-verificacao/testes/guarda-3.2-legado.log`.
- [x] 3.3 `Users/Login/LoginThrottlePolicy.cs`, `LoginThrottle.cs` e `LoginThrottleStore.cs`, pela tabela do D4:
  - 5 falhas → 5 min (a 5ª responde `401`);
  - depois de bloqueio vencido, uma falha bloqueia pelo dobro do último, até 60 min;
  - última falha há mais de 24 h vale como linha inexistente, pelo tempo e não pela limpeza;
  - sucesso zera a linha inteira;
  - tentativa durante o bloqueio não conta nem estende;
  - a chave é o e-mail normalizado.

  O desativado com a senha certa não toca na linha. Isso é da rota (5.2), e a política só expõe "falha" e "sucesso".

  O ciclo do `IdentityTokenPruner` passa a remover também as linhas de `identity_login_throttle` sem falha há 24 h e fora de bloqueio, no mesmo lock.

  Testes:
  - **`LoginThrottlePolicyTests`** (unidade, `FakeTimeProvider`), um caso por cenário de `local-users`. Guardas verificados:
    - uma política que exige 5 falhas de novo depois do bloqueio vencido faz o caso "uma falha bloqueia pelo dobro" reprovar;
    - uma política que só volta ao início quando a linha é apagada faz o caso das 24 h reprovar;
  - o caso da limpeza no `IdentityTokenPruneTests`.

  **Feito, 31/10/2026.**
  - **A limpeza ficou em classe própria**, `LoginThrottlePruneTests`, e não no `IdentityTokenPruneTests`. Aquele é um teste só, em passos, com o relógio da fixture andando para a frente, e uma contagem a mais mudaria o número dele. O `IdentityTokenPruneResult` ganha `LoginThrottles`, com padrão 0, e as igualdades existentes continuam valendo.
  - O `LoginThrottleStore` registra a falha sob `INSERT … ON CONFLICT DO NOTHING` seguido de `SELECT … FOR UPDATE`.
  - **Guardas verificados:**
    - a reincidência pelo ramo de contagem (`|| current.LockedUntil is not null`) reprova 4 de 8, entre eles `AfterAnExpiredLock_OneFailureLocksForDouble`;
    - o `Effective` sem as 24 h reprova 2 de 8, entre eles `TwentyFourHoursWithoutFailure_StartOver_EvenIfTheRowIsStillThere`;
    - nos dois, a árvore volta. Os logs estão em `guarda-3.3-*.log`.
  - Rodada do grupo: **40/40** (`180-verificacao/testes/g3.log`).

## 4. Bootstrap do primeiro Admin (`apps/api`, D2)

- [x] 4.1 `Users/Bootstrap/UserBootstrapOptions.cs` (`Auth:BootstrapAdminEmail`, mais o `Auth:OperatorPasswordHash` existente) e `UserBootstrapper.cs`:
  - usa o lock `bigint` `BUTECOU1` (`4779819235982398769`), com o comentário no formato das outras chaves;
  - cria só sem nenhum usuário;
  - nunca sobrescreve;
  - sem as configurações, só registra o aviso;
  - roda no aquecimento do `IdentityKeyRotationService` como passo independente (4.3), sem I/O no boot.

  **Feito, 31/10/2026.**
  - `IUserBootstrapper` com três resultados: `Created`, `Ignored` e `MissingConfiguration`.
  - O e-mail é validado por `MailAddress.TryCreate`, e o hash por `BootstrapLegacyPasswordVerifier.IsWellFormed`.
  - Os `EventId` 1188 a 1190 são o criado, o ignorado e o aviso.
  - O registro fica em `Users/UserServiceCollectionExtensions.AddLocalUsers`, chamado no `Program.cs`.
- [x] 4.2 `UserBootstrapTests` (integração), com os cenários de `local-users`:
  - banco vazio cria o usuário;
  - o primeiro login refaz em Argon2id;
  - com usuários, nada muda;
  - sem configuração, só o aviso;
  - duas instâncias criam um usuário só.

  **Guarda verificado:** reaplicar a configuração a cada boot faz o caso "com usuários, nada muda" reprovar.

  **Feito em 31/10/2026, menos o caso do primeiro login, que entra com a rota (5.4).** A fixture é `UserBootstrapFixture`, com o aquecimento a cada 200 ms. O caso "sem configuração" e o de "duas instâncias" ficam num teste só (`UserBootstrapWithoutEmailTests`), porque o segundo cria o usuário que o primeiro afirma não existir.

  **Guarda verificado:** com o ramo "já há usuário" trocando o hash, `WithUsers_AnotherConfigurationChangesNothing_AndLogsIgnored` reprova. O caso do banco vazio também reprova, porque o hash mudado na mesma fixture o derruba. A árvore volta (`guarda-4.2-reaplica.log`).
- [x] 4.3 Aquecimento em passos independentes (design D3.13). O `IdentityKeyRotationService` troca o `while (!ring.IsLoaded)` por um laço sobre os passos pendentes. Cada passo tem o próprio `try/catch` e `EventId`:
  - a carga do anel (`IdentityKeyWarmupFailed`, como hoje);
  - o bootstrap (evento novo).

  O catálogo entra como terceiro passo na 6.3. O passo que falha espera o `WarmupRetryInterval` e tenta de novo, e os concluídos não repetem. O tique da rotação fica como está.

  **Teste novo, `IdentityWarmupTests` (integração):** um `UserBootstrapper` que lança uma vez, injetado por DI. No primeiro ciclo:
  - o log tem o evento do bootstrap;
  - o anel carrega, e `GET /health` responde `200`;
  - depois do ciclo seguinte, o usuário existe.

  **Guarda verificado:** o laço de hoje, que só repete enquanto o anel não carregou, faz o teste reprovar, porque o usuário nunca é criado.

  **Feito, 31/10/2026.**
  - O passo que falha loga `Warning` com o `EventId` próprio: `IdentityKeyWarmupFailed` (1183) para o anel e `UserBootstrapWarmupFailed` (1187) para o bootstrap.
  - O `IdentityIssuerLogCapture` passa a capturar também `Buteco.Api.Users.*`.
  - **Guarda verificado:** sem o passo do bootstrap no laço, o teste reprova por prazo, porque o bootstrap nunca roda. A árvore volta (`guarda-4.3-laco-antigo.log`).
  - Rodada do grupo, com `IdentityKeyRingGateTests` e `IdentityKeyRotationTests`: **8/8** (`g4.log`).

## 5. Rota de login e sessão do emissor (`apps/api`, D1, D4)

- [x] 5.1 Esquema de cookie `ButecoSession` em `AddIdentityIssuer`. O cookie `buteco-sessao` leva:
  - `HttpOnly`, `SameSite=Strict`, `Path=/`;
  - `Secure` sempre fora de `Development`;
  - 5 minutos, sem renovação;
  - claims: id do usuário e `security_stamp`.
- [x] 5.2 `Users/Login/SessionEndpoints.cs` (`POST /auth/session`, anônima, `AuthEntryPoint`) e `SessionRequestGuards.cs`:
  - só JSON, `415`;
  - `Origin` igual à do issuer ou à de `Identity:PanelBaseUrl`, `403`;
  - nenhum dos dois verifica senha.

  A rota aplica o bloqueio (3.3), compara o hash (3.2) com o hash fictício para e-mail desconhecido, refaz o hash quando preciso, atualiza `last_access_at` e grava o cookie. Respostas:
  - `204` no sucesso;
  - `401` genérico;
  - `403` "Acesso desativado. Fale com o administrador." só com a senha certa;
  - `423` com `Retry-After` durante o bloqueio.
- [x] 5.3 `Users/Login/LoginRateLimiting.cs`, com o `RateLimiter` do framework na rota: concorrência 4 com fila 16, mais token bucket de 10/s com rajada de 20. Acima disso, `429` com `Retry-After`. Os valores são constantes, com o motivo no comentário (convenção 2).
- [x] 5.4 Testes de integração:
  - **`SessionCsrfTests`**. Guarda verificado: a conferência de `Origin` retirada faz o caso de outra origem reprovar;
  - **`LoginThrottleTests`**. Guarda verificado: a chave do bloqueio pela linha do usuário faz o caso do e-mail inexistente reprovar;
  - **`LoginRateLimitTests`**;
  - **`UserDeactivationTests`**, a parte do login: desativado com a senha certa e com a errada; resposta igual para conta inexistente e senha errada; o cenário "Desativado com a senha certa não mexe na contagem". Guarda verificado: o `403` tratado como sucesso no bloqueio (zerando a linha) faz esse cenário reprovar.

  **Feito, 31/10/2026.**
  - **Código.** O cookie fica em `Identity/Flow/IssuerSession.cs`. O `Secure` vem do `IHostEnvironment` por `AddOptions().Configure<IHostEnvironment>`, porque o registro não recebe o ambiente.
  - **Limite global.** É um `GlobalLimiter` encadeado (`PartitionedRateLimiter.CreateChained`): concorrência e token bucket só para a rota marcada com `LoginRateLimiting.Marker`, e `NoLimiter` nas outras. O `UseRateLimiter` vem depois do `UseAuthorization`.
  - **Rota.** `Identity:PanelBaseUrl` entra já no `IdentityIssuerOptions`, porque o guarda de `Origin` o lê. `/auth/session` entra na lista do `ValidateRouteAuthenticationClassification`.
  - **Fixture.** A `LocalUsersFixture` tem relógio controlado e **o limite desligado por padrão**, por `PostConfigure<RateLimiterOptions>`, para as rajadas sequenciais dos testes de bloqueio não virarem `429` por acaso. Só o `LoginRateLimitTests` liga o limite, com host próprio.
  - **Testes além da lista.** O `SessionLoginTests` cobre o cookie com os atributos, o `401` genérico, a resposta igual para conta inexistente e o formato de hash desconhecido. O `UserBootstrapTests` ganha o caso do primeiro login, que refaz o hash em Argon2id e aceita o login seguinte, e fecha a 4.2.
  - **Guardas verificados**, todos com a árvore de volta:
    - sem a conferência de `Origin`, o `SessionCsrfTests` reprova 2 de 4;
    - com a falha contada só quando a conta existe, `UnknownEmail_BlocksTheSame` reprova, e também `AttemptsDuringTheLock_DoNotExtendIt`, que usa e-mail sem conta;
    - com o `403` zerando a linha, `Deactivated_RightPassword_NeitherCountsNorZeroes` reprova.
  - Rodada do grupo, com `Users/` e `RouteAuthentication*`: **81/81** (`g5.log`).

## 6. Fluxo authorization code + PKCE (`apps/api`, D1, D3)

- [x] 6.1 `AddIdentityIssuer`. O servidor ganha:
  - `AllowAuthorizationCodeFlow()`, `AllowRefreshTokenFlow()` e `RequireProofKeyForCodeExchange()`;
  - `SetAuthorizationEndpointUris("connect/authorize")` e `SetEndSessionEndpointUris("connect/logout")`;
  - `EnableAuthorizationEndpointPassthrough()` e `EnableEndSessionEndpointPassthrough()`, **sem** `EnableErrorPassthrough` (2.4 (a) e (b));
  - `SetAccessTokenLifetime(15 min)` e `SetRefreshTokenLifetime(14 dias)`;
  - `RegisterScopes(openid, offline_access, buteco-api, buteco-inbox, buteco-connectors)`.

  Sai o ramo "desligado" (grupo 8). `IdentityIssuerOptions` ganha `PanelBaseUrl`.
- [x] 6.2 `IdentityIssuerEndpoints.Expected` ganha `/connect/authorize` (`AuthEntryPoint`, passthrough) e `/connect/logout` (`SessionEnd`, passthrough), e `AnonymousRouteReason` ganha `SessionEnd`. A `ValidateIdentityIssuerEndpoints` passa a conferir os quatro casos da `route-authentication`. A lista do `ValidateRouteAuthenticationClassification` no `Program.cs` ganha as três rotas novas. Testes no `IdentityIssuerStartupValidationTests`. **Guarda verificado:** a rota do authorize removida faz o boot falhar no teste da composição real.
- [x] 6.3 `Identity/Clients/IdentityClients.cs` (descritor do `buteco-painel`, os três escopos e `ReservedPrefix`) e `IdentityClientCatalog.cs`:
  - sincroniza no aquecimento, com o lock `BUTECOC1` (`4779819235982394161`), como terceiro passo independente da 4.3, com `try/catch` e `EventId` próprios;
  - sincroniza de novo a cada tique do `CheckInterval`, num `try/catch` separado do da rotação;
  - corrige divergências;
  - loga um `buteco-*` desconhecido, sem tocar nele.

  O `IdentityKeyRingGate` **não** passa a esperar o catálogo (D3.13). O `IdentityWarmupTests` ganha o caso do catálogo: falha uma vez, e o anel e o bootstrap concluem mesmo assim.

  Testes:
  - `IdentityClientCatalogTests` (unidade). Guarda verificado: o descritor com `client_credentials` faz o teste reprovar;
  - `IdentityClientCatalogSyncTests` (integração). Guarda verificado: uma sincronização que só cria e não corrige faz o caso do cliente adulterado reprovar.
- [x] 6.4 `Identity/Flow/UserPrincipalFactory.cs` e `AuthorizeEndpoint.cs`:
  - `GET /connect/authorize` autentica o cookie;
  - sem sessão, `302 /entrar?retorno=…`, ou `login_required` com `prompt=none`. O `retorno` é `Request.Path + Request.QueryString`, URL-codificado, e nunca `GetDisplayUrl()` nem nada do `Host`;
  - confere o usuário ativo e o `security_stamp`;
  - faz `SignIn` com `sub`, e-mail, nome, escopos, recursos e destinos;
  - o principal leva o claim do `security_stamp` **sem destino**, só no código e no refresh.
- [x] 6.5 `Identity/Flow/UserGrantTokenHandler.cs` (`HandleTokenRequestContext` dos grants `authorization_code` e `refresh_token`): todo token novo de usuário relê o usuário, e recusa com `invalid_grant` o inexistente, o desativado e o de `security_stamp` gravado diferente do claim do código ou do refresh. Atualiza `last_access_at` só no `refresh_token` (no login, quem atualiza é a 5.2).
- [x] 6.6 `Identity/Flow/EndSessionEndpoint.cs` (`GET /connect/logout`):
  - revoga a autorização do hint válido;
  - apaga o cookie;
  - redireciona ao endereço registrado, ou a `/entrar`.

  Mais `Identity/Flow/EndSessionErrorHandler.cs` (`ApplyEndSessionResponseContext`, 2.4 (b)): para hint inválido ou endereço não registrado, apaga o cookie e responde `302 /entrar`.
- [x] 6.7 `Identity/Storage/TokenEntryStorageHandler.cs` (renomeado de `ClientCredentialsAccessTokenStorageHandler`): `CreateTokenEntry = false` para todo access token e todo id token, com a mesma ordem. O teste de unidade vira `TokenEntryStorageHandlerTests`, com a matriz nova. O `IdentityTokenStorageTests` ganha o fluxo do usuário (só código e refresh com linha). **Guarda verificado:** o registro do handler comentado faz a contagem reprovar.
- [x] 6.8 Testes de integração do fluxo:
  - **`UserLoginFlowTests`**: fluxo completo, sem sessão, `prompt=none`, PKCE ausente, verifier errado, `redirect_uri` não registrado, o `retorno` com `Host` de outro nome, e o authorize antes da primeira sincronização (`400` `invalid_request`, `/health` `200`). Guardas verificados:
    - o `RequireProofKeyForCodeExchange` retirado faz o caso do PKCE ausente reprovar;
    - o `retorno` montado com `GetDisplayUrl()` faz o caso do `Host` reprovar;
    - o gate esperando o catálogo faz o caso do `/health` reprovar;
  - **`RefreshRotationTests`**: rotação, reuso, `last_access_at` no login e no refresh, e não em `GET /agents`;
  - **`UserDeactivationTests`**, a parte do refresh e do cookie em voo, com os cenários "Desativado entre o authorize e a troca do código não recebe tokens" e "Desativado e reativado não renova com o refresh de antes". A reativação é feita no banco, só com `status`, porque a rota é da #219. Guardas verificados:
    - a releitura do usuário retirada do handler faz o refresh do desativado reprovar;
    - a comparação do stamp retirada faz o caso do reativado reprovar;
    - a comparação retirada do grant `authorization_code` faz o caso "desativado entre o authorize e a troca do código" reprovar;
  - **o carimbo fora dos tokens do cliente:** o access token e o id token do fluxo não têm o claim do stamp, no `UserLoginFlowTests`;
  - **`EndSessionTests`**: hint válido, inválido e endereço não registrado.

  O suporte `IdentityIssuerTestClient` ganha o fluxo do usuário (sessão, authorize com PKCE e token).
- [x] 6.9 Retenção das chaves para o refresh (design D3.7, 2.4 (f)):
  - `IdentityKeyRotationPolicy.Retention` passa a 15 dias, com o comentário da constante explicando por que o refresh manda;
  - a `ValidateIdentityIssuerConfiguration` compara a retenção com `max(access, refresh) + CheckInterval`, com refresh nulo reprovando e a mensagem citando a duração que manda;
  - `IdentityKeyRotationPolicyTests.AfterTheSwitch_TheNewKeySigns_TheOldOneStaysInTheRingFor24Hours` vira `…StaysInTheRingForTheRetention`, com a constante no lugar das 24 h literais. Ele entra na lista de renomeados do fechamento por nome;
  - o `IdentityIssuerStartupValidationTests` ganha o caso "retenção menor que o refresh + intervalo → boot falha".
  - migration de dados `ExtendIdentityKeyRetention` (design D6), com o SQL gerado registrado. Teste de integração: uma linha de assinatura gravada com `validates_until = signs_until + 24 h` passa a `signs_until + 15 dias` depois da migration, e a de cifra fica igual. **Guarda verificado:** sem a migration, o caso reprova.

  **Teste novo, `IdentityKeyRetentionRefreshTests` (integração, `FakeTimeProvider`):**
  - o refresh emitido pela chave ativa;
  - o relógio leva até a troca, e a rotação roda;
  - mais 13 dias, e o refresh é apresentado: novo par, com o `kid` da chave nova.

  **Guarda verificado:** com a retenção de volta a 24 h, o teste reprova. Registrar se o `FakeTimeProvider` também governa a validade do token no OpenIddict. Se não governar, o teste ajusta a duração e não a premissa, e isso é dito no fechamento.

  **Grupo 6 feito, 31/10/2026.** O resultado por tarefa:

  - **6.1** O servidor ganha o authorize e o logout em passthrough, **sem** `EnableErrorPassthrough`; code, refresh e PKCE; 15 min e 14 dias; os cinco escopos. Entram o `UserGrantTokenHandler` e o `EndSessionErrorHandler`.
  - **6.2** `Expected` vira `ExpectedEndpoint(Reason, Passthrough)`. A rota do passthrough precisa existir, ser anônima e ter o mesmo motivo.
    - **O guarda pedido não reprovava como escrito.** Sem `MapAuthorizeEndpoint()`, quem reprova primeiro é a checagem de rotas anônimas, porque `/connect/authorize` está na lista dela. A mensagem dessa checagem não continha "emissor", e a asserção era estreita demais.
    - **Correção:** o `RealComposition_Boots_WithTheRealRoutesAndEndpoints` exige boot sem exceção nenhuma, e reprova sem a rota. Entra o `IdentityIssuerEndpointsPassthroughTests` (5 casos), sobre um `WebApplication` mínimo com o registro real. Sem o ramo do passthrough na checagem, 3 de 5 reprovam.
  - **6.3** `IIdentityClientCatalog`, com o passo próprio no aquecimento (`IdentityClientCatalogFailed`, 1193) e a ressincronização a cada tique, num `try/catch` separado. Os casos ficam em:
    - `IdentityClientCatalogTests` (4);
    - `IdentityClientCatalogSyncTests` (4): banco migrado só com o painel e os três escopos, painel adulterado que volta, `client_credentials` com `buteco-painel` sem token, e `buteco-outro` só logado;
    - `IdentityWarmupTests`, com o caso do catálogo que falha uma vez.

    **Guardas verificados:** `client_credentials` no descritor reprova o `Panel_IsPublic_*`, e a sincronização que só cria reprova o `ATamperedPanel_*`.
  - **6.4** `retorno = PathBase + Path + QueryString`, URL-codificado. O stamp sai sem destino.
  - **6.5** Token de usuário é o que traz o claim do stamp. Os refresh de cliente dos testes da #209 não trazem e não passam pelo handler: recusar todo grant sem usuário os quebraria.
  - **6.6** A rota mais o handler de erro, como corrigido na 2.4 (b). Dois achados na implementação, ambos na fonte:
    - o hint inválido **chega à rota** (`OpenIddictServerHandlers.cs` l. 364–377);
    - o `AuthenticateAsync` devolve um principal **vazio**, e não nulo (`OpenIddictServerAspNetCoreHandler.cs` l. 207–209).

    A rota só trata como hint o principal autenticado.
  - **6.7** O `IdentityTokenPruneTests` passa a contar 4 tokens, e não 5: o access da renovação também deixou de ter linha (D3.8). O número e o comentário mudaram. O `IdentityUserTokenStorageTests` (fluxo e renovação) fica no mesmo arquivo do `IdentityTokenStorageTests`. **Guarda verificado:** sem o registro do handler, ele reprova.
  - **6.8** `UserLoginFlowTests` (8), `AuthorizeBeforeCatalogTests` (1), `RefreshRotationTests` (3), `EndSessionTests` (4) e `UserDeactivationFlowTests` (4). A parte do refresh e do cookie em voo da `UserDeactivationTests` ficou em `Identity/UserDeactivationFlowTests`, para o nome não colidir com o de `Users/`. **Guardas verificados**, todos reprovando e com a árvore de volta:
    - o `retorno` por `GetDisplayUrl()`;
    - o gate exigindo o catálogo, que dá `503` no `/health`;
    - a comparação do stamp retirada;
    - o handler só no grant `refresh_token`.

    **Dois guardas são defesa em profundidade, e só reprovam com o defeito completo:**
    - **PKCE:** sem o `RequireProofKeyForCodeExchange` global, o caso passa, porque o descritor do painel exige `ft:pkce`. Sem os dois, reprova.
    - **Releitura:** tirar só a conferência do status não basta, porque desativar troca o stamp e a comparação recusa. Sem o handler registrado, `DeactivatedUser_CannotRefresh` reprova.
  - **6.9** A retenção vai a 15 dias, e a checagem de boot passa a usar a maior duração. A migration de dados é `20261031151210_ExtendIdentityKeyRetention`, com `Down` vazio de propósito.
    - **Testes atuais que mudaram:**
      - `AfterTheSwitch_*For24Hours` vira `*ForTheRetention`, um teste **renomeado** para a comparação por nome;
      - o `EmptyTable_PlansOneSigningKeyStartingNow_AndOneEncryptionKey` também tinha as 24 h literais e passa a esperar 15 dias;
      - o `RealComposition_WithRefreshTokensOutlivingThePruneMinimumAge_*` passa de 15 dias para 14 d 30 min, porque 15 dias agora reprovam na retenção, antes da limpeza.
    - **Testes novos:** `RealComposition_WithRefreshTokensOutlivingTheKeyRetention_*`, `Retention_CoversTheFourteenDayRefreshPlusTheCheckInterval` e `ExtendIdentityKeyRetentionMigrationTests`. Este último confirma que a migration **não toca** a chave com janela de exatamente 15 dias nem a de 20 dias, nem a de cifra. Foi o pedido 3 da rodada.
    - **O `FakeTimeProvider` governa a validade no OpenIddict.** O `IdentityKeyRetentionRefreshTests` roda com os 14 dias reais, sem ajustar a duração.
    - **Guardas verificados:**
      - **retenção:** com a de 24 h e a checagem de boot só pelo access (o estado de antes, inteiro), o refresh é recusado com `invalid_grant` / ID2090, "The signing key associated to the specified token was not found." É a premissa da 2.4 (f), vista na prática;
      - **migration:** sem o `UPDATE`, o teste dela reprova.
  - **Incidente no script de guarda, não no código.**
    - **O defeito:** o `guard.py` restaurava o arquivo com o `mtime` da cópia de backup, anterior ao build com o defeito. A rodada normal seguinte reaproveitava o binário do defeito.
    - **O efeito:** a rodada `g6` e a `g6-r2` falharam em 4 casos de refresh e desativação, porque estavam sem o `UserGrantTokenHandler` registrado, o último guarda da série.
    - **O que vale:** os guardas sim, porque cada um grava o defeito com `mtime` novo e o build compila o projeto inteiro. Não valem as rodadas normais feitas logo depois de um guarda sem outra mudança de fonte: a `g6`, a `g6-r2` e a `g62` (esta passou, contra o binário do guarda 6.7).
    - **A correção:** o script passa a tocar o arquivo depois de restaurar.
  - **Rodada do grupo depois da correção** (`g6-r3`, `Identity/`, `Users/`, `RouteAuthentication*` e `AuthLogin*`): **212 aprovados e 1 ignorado, de 216**. As 3 falhas são os `IdentityIssuerDisabledTests`, que o grupo 8 remove: com o emissor desligado, a rota do authorize não acha os serviços do OpenIddict.

## 7. O `apps/api` aceita o próprio token (`apps/api`, D3.3)

- [x] 7.1 `Identity/Validation/`:
  - **`IssuerTokenAuthentication.cs`:** esquema de política `Bearer` como padrão, com o seletor pela forma; `JwtBearer` `IssuerToken` com RS256, `at+jwt`, issuer, `aud` `buteco-api`, 30 s de tolerância e `MapInboundClaims = false`; `IssuerSigningKeyResolver` sobre as chaves de validação do `IdentityKeyRing.Snapshot`; no `OnTokenValidated`, o principal reconstruído pela tabela de clientes;
  - **`IssuerTokenAudience.cs`**, **`IssuerClientSubjects.cs`** e **`IssuerTokenStartupValidation.cs`** (`ValidateIssuerClientSubjects`).

  O `Program.cs` troca o `AddAuthentication(OperatorToken…)` pelo registro novo. O `OperatorTokenAuthenticationHandler` não muda.
- [x] 7.2 Testes:
  - **`IssuerTokenSchemeSelectionTests`** (unidade). Guarda verificado: um seletor que tenta os dois esquemas faz o caso "JWT inválido não chama o HMAC" reprovar;
  - **`IssuerTokenAcceptanceTests`** (integração): token do fluxo `200`; sem `buteco-api` `401`; cliente fora da tabela `403`; `sub` `operator` forjado `403`; HMAC `200` e `401` como antes. Guarda verificado: o subject tirado do `sub`, e não da tabela, faz o caso forjado reprovar;
  - **checagem da tabela de clientes** no `IdentityIssuerStartupValidationTests`.

  **Feito, 31/10/2026.**
  - **Código.** O desenho é o do `apps/inbox`, com o issuer vindo do `IdentityIssuerState` e as chaves pelo `IssuerSigningKeyResolver` sobre o `IdentityKeyRing.Snapshot`. O `Program.cs` troca o `AddAuthentication(OperatorToken)` por `AddIssuerTokenAuthentication()` e chama `ValidateIssuerClientSubjects()` depois do `Build()`. O `Buteco.Api.csproj` ganha a referência ao `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.13, já central.
  - **Testes.**
    - `IssuerTokenSchemeSelectionTests` (10 casos);
    - `IssuerTokenAcceptanceTests` (6 casos). O `sub` forjado é um cliente confidencial chamado `operator`, porque o `client_credentials` põe `sub = client_id`. O "JWT inválido não chega ao HMAC" usa um `ITokenService` que conta as chamadas;
    - `IssuerClientSubjectsStartupValidationTests` (2 casos), no arquivo do `IdentityIssuerStartupValidationTests`.
  - **Guardas verificados**, com a árvore de volta:
    - com o `sub` como `NameIdentifier`, `ForgedOperatorSub_IsNotTheOperator` reprova;
    - com o esquema do emissor chamando o validador HMAC na falha, que é o seletor tentando os dois, `InvalidJwt_Is401_WithoutCallingTheHmacValidator` reprova.
  - **Rodada do grupo**, com `RouteAuthentication*`, `AuthLogin*`, `ServiceScope*` e `IdentityIssuerCoexistenceTests`: **64/64** (`g7.log`).

## 8. Url pública obrigatória (`apps/api`, D3.4)

- [x] 8.1 A `ValidateIdentityIssuerConfiguration` reprova `PublicUrl:BaseUrl` ausente ou vazia, depois do `Build()`, citando a chave. O registro continua sem lançar. Sai o `IdentityIssuerState.Disabled` e o ramo do `UseIdentityKeyRingGate` que o lia.
- [x] 8.2 Sai o que deixou de ser alcançável (convenção 25):
  - o `IdentityIssuerDisabledTests`;
  - o `ApiFactoryWithoutPublicUrlFixture`;
  - o caso de url ausente do `AgentA2AAddressesTests`;
  - o ramo nulo do `AgentA2AAddressBuilder`.

  O tipo da resposta de agente **não** muda, para o contrato do painel não mudar. Registrar a lista de testes removidos, por nome, para o fechamento por nome. Teste novo: sem url pública, o boot falha, no `IdentityIssuerStartupValidationTests`.

  **Feito, 31/10/2026.**
  - **Código.**
    - O `IdentityIssuerState` vira `(Uri? Issuer)`, sem `Enabled` nem `Disabled`.
    - O registro não lança; com url ausente, vazia ou inválida, registra só o estado sem issuer.
    - O boot falha com "PublicUrl:BaseUrl não configurada" ou "PublicUrl:BaseUrl inválido".
    - Saem o ramo nulo do `UseIdentityKeyRingGate`, o `return` antecipado do `ValidateIdentityIssuerEndpoints` e o ramo nulo do `AgentA2AAddressBuilder`, que passa a devolver `AgentA2AAddresses` não anulável. O `AgentResponse.A2A` continua anulável, e o card usa `addresses.Url`.
  - **Testes removidos, por nome, para o fechamento:**
    - `Buteco.Api.Tests.Identity.IdentityIssuerDisabledTests.Discovery_IsNotServed`
    - `Buteco.Api.Tests.Identity.IdentityIssuerDisabledTests.Boot_WarnsThatTheIssuerIsOff`
    - `Buteco.Api.Tests.Identity.IdentityIssuerDisabledTests.Login_StillWorks`
    - `Buteco.Api.Tests.AgentA2AAddressesWithoutPublicUrlTests.WithoutPublicUrlConfigured_AddressesAreAbsent`

    Com eles saem os arquivos `IdentityIssuerDisabledTests.cs` e `Support/ApiFactoryWithoutPublicUrlFixture.cs`.
  - **Testes novos:**
    - `RealComposition_WithoutPublicUrl_FailsNamingTheSetting`, com vazio e com espaços;
    - `AgentA2AAddressesTests.Addresses_DoNotComeFromTheRequestHost`, o cenário novo da spec `agent-catalog`.
  - **Suíte inteira do `apps/api`, rodada intermediária e não a de fechamento:** **1009/1009 + 1 ignorado** (`api-inteira-1.log`).

## 9. Acordo em `tests/` (D5)

- [x] 9.1 (`tests/InboxOrchestratorRoundTrip.Tests`) A `RoundTripFixture` deixa de cadastrar o `buteco-painel` confidencial e passa a configurar o bootstrap (`Auth:BootstrapAdminEmail` e um hash PBKDF2 de teste). O `IssuerTokenRoundTripTests` faz o fluxo real (sessão, authorize com PKCE e token) pelo `TestServer` do `apps/api` e chama `GET /channels`: `200`, e `200` de novo com o `apps/api` inalcançável.
- [x] 9.2 (`tests/ApiConnectorsRoundTrip.Tests`) O mesmo para `GET /connectors/providers`. A recusa por `aud` cruzada usa um cliente confidencial de teste `teste-so-inbox`, fora do prefixo `buteco-`, com escopo `buteco-inbox`: `401`.

  **Feito, 31/10/2026.** As duas fixtures configuram `Auth:BootstrapAdminEmail`. A dos conectores ganha também o `Auth:OperatorPasswordHash`, que ela não tinha. Cada projeto ganha `Support/IssuerUserFlow.cs`, duplicado de propósito entre os dois, porque os projetos de `tests/` não compartilham código. O apoio faz o fluxo real só por HTTP: sessão, authorize com PKCE e troca do código.
  - **Asserções.** O `aud` passa a ser exato, `["buteco-inbox"]` e `["buteco-connectors"]`, porque cada token pede só o escopo do app. O inbox confere também que o `sub` é um id de usuário.
  - **Problema encontrado e corrigido no apoio, e não no código.** A primeira versão repetia o login até o bootstrap concluir. No inbox, as tentativas antes do bootstrap contaram como falhas de e-mail sem conta, e a quinta bloqueou o e-mail por 5 minutos: `423`, o bloqueio fazendo o que deve.
  - **Correção.** O apoio recebe um passo de aquecimento: o teste roda `IIdentityClientCatalog.SyncAsync` e `IUserBootstrapper.RunAsync` pelos serviços do `apps/api`, que são idempotentes. Depois disso, cada passo do fluxo vai uma vez só.
  - **Rodadas.** O `IssuerTokenRoundTripTests` passa nos dois projetos (`g9-inbox-2.log`, `g9-connectors-2.log`). A rodada anterior dos dois projetos inteiros deu 11/12 no inbox, com o bloqueio acima, e 8/8 nos conectores.

## 10. nginx, configuração e deploy

- [x] 10.1 (`apps/frontend`, `deploy/nginx.conf`) `location = /connect/authorize` e `location = /connect/logout`, sem o desvio de navegação, com o comentário do bloco `/connect` atualizado.
- [x] 10.2 (`apps/api`, deploy) Configuração:
  - `appsettings.json`: `Auth:BootstrapAdminEmail` vazio;
  - `appsettings.Development.json`: `Identity:PanelBaseUrl = http://localhost:5173` e um e-mail de bootstrap de desenvolvimento;
  - `docker-compose.prod.yml`: `Auth__BootstrapAdminEmail: ${AUTH_BOOTSTRAP_ADMIN_EMAIL}`, sem `:?`;
  - `.env.prod.example`: a variável nova, com comentário.

  **Feito, 31/10/2026.** O nginx ganha `location = /connect/authorize` e `location = /connect/logout` sem o desvio de navegação, e o comentário do bloco `/connect` passa a dizer que só o token sobra nele. O e-mail de bootstrap de desenvolvimento é `operador@buteco.local`. O `appsettings.json` ganha também `Identity:PanelBaseUrl` vazio, com o motivo no comentário. O `docker-compose.prod.yml` renderiza com o `.env.prod.example` (`podman-compose config`), com `Auth__BootstrapAdminEmail: ''`. A conferência por `curl` do nginx é da 12.2.

## 11. Documentação (antes do archive)

- [x] 11.1 `01-ARQUITETURA_E_CONVENCOES.md`, seção "Autenticação":
  - usuários locais, Argon2id e bootstrap;
  - sessão do emissor e fluxo do painel;
  - o `apps/api` aceitando o próprio token;
  - a resposta ao `libs/`, com o gatilho para voltar à pergunta;
  - a url pública obrigatória;
  - a retenção de 15 dias e o porquê (o refresh);
  - **depois do logout ou da desativação, o access token já emitido continua valendo até 15 min nos três apps**: não é gravado e é validado só pelo JWKS. Isso é aceito na M1, e a #186 trata a revogação das aplicações externas.
- [x] 11.2 Os documentos de `docs/`:
  - **`docs/architecture.md`:** o fluxo de login no diagrama de autenticação;
  - **`docs/configuration.md`:** `Auth:BootstrapAdminEmail` e `Identity:PanelBaseUrl`; `PublicUrl:BaseUrl` obrigatória;
  - **`docs/deployment.md`:** bootstrap, os quatro `curl` do D3.5, limite por IP no proxy de fora (com a #221), trocar a senha e tirar `AUTH_OPERATOR_PASSWORD_HASH` depois da #217, rollback, e a janela de até 15 min do access token depois do logout ou da desativação, aceita na M1, com a #186;
  - **`docs/development.md`:** o bootstrap em desenvolvimento.

  **Feito, 31/10/2026.** O `01` e o `docs/architecture.md` ganham a seção de usuários locais, sessão e fluxo, com diagrama do login. O `docs/deployment.md` ganha "Usuários locais e login pelo emissor (#180)", com as duas migrations, a ordem `migrator` → `api` → `frontend`, o "Primeiro Admin", o bloqueio, o limite por IP no proxy de fora, a janela de 15 min, o Data Protection com mais de uma instância, os quatro `curl` e o rollback.

  **Ordem do fechamento:** a 11.3 (`02` e `CHANGELOG`) é escrita depois da 12.1 e da 12.2, porque a entrada do `02` registra o que o fechamento mediu, e antes da 12.3 (sync e archive).
- [x] 11.3 `02-HISTORICO_E_STATUS.md`:
  - a entrada da change;
  - **a fila da M1 com 12 itens** (#180, #217, #218, #219, #181, #182, #211, #183, #220), a #184 dependendo também da #217, e a #221 entre as "aguardando gatilho";
  - o gatilho do `libs/`.

  Mais o `CHANGELOG.md`.

  **Feito, 31/10/2026, depois da 12.1 e da 12.2 e antes do archive.**
  - **`02`:**
    - entra a entrada da change, com os números do fechamento, os guardas, as divergências e o deploy;
    - a fila da evolução passa à M1 com 12 itens, com as dependências conferidas contra os `blocked-by` nativos do GitHub (iguais);
    - a #221 entra na tabela "aguardando gatilho";
    - o gatilho do `libs/` fica registrado.
  - **`CHANGELOG`:** três entradas em "Added" e cinco em "Changed".

## 12. Fechamento

- [x] 12.1 (`apps/api`, `tests/`) Suítes de fechamento, com o mesmo protocolo da 1.1 e o `CPU_Speed_Limit` conferido antes de cada uma. Comparação com a baseline **por nome**: novos, ausentes (os removidos na 8.2, nomeados) e com resultado mudado. Build `--no-incremental` dos três projetos de teste nos dois worktrees, com o mesmo conjunto de avisos, ou cada aviso novo explicado.
- [x] 12.2 Verificação manual no stack local (`dotnet run` do `apps/api`), com a saída em `~/.cache/buteco-agents/180-fechamento/manual/`:
  - o log do bootstrap;
  - o discovery com `authorization_endpoint` e `end_session_endpoint`;
  - o fluxo por `curl` até o token;
  - `GET /agents` com o token;
  - os quatro `curl` do D3.5 contra o nginx do stack local, com a troca do `resolver` do Podman (#212);
  - o painel continua entrando pelo HMAC.

  **12.1 feita, 31/10/2026, segunda rodada válida.**
  - **Protocolo:** o mesmo da 1.1, com o script `fechamento-180.sh`, que é o da baseline com o worktree trocado. As saídas estão em `180-fechamento/`.
  - **A primeira rodada (16:08–16:13Z) falhou por defeito meu de configuração**, e foi guardada em `180-fechamento/rodada-1-panelbaseurl/`. O `appsettings.Development.json` da 10.2 passou a ter `Identity:PanelBaseUrl = http://localhost:5173`, e as fixtures, que sobem em `Development`, herdavam o valor. O `redirect_uri` registrado virou o do Vite, e todo authorize de teste respondeu `400`: 21 falhas no `apps/api` e 1 em cada acordo.
  - **A correção:** o `TestAuthentication.ConfigOverrides` e as duas `RoundTripFixture` passam a deixar vazios `Identity:PanelBaseUrl` e `Auth:BootstrapAdminEmail`, como já faziam com a url pública e os provedores. Só a `UserBootstrapFixture` declara o e-mail.

  | suíte | baseline em `3bb8236` | fechamento | por nome | CPU e load (início → fim) |
  |---|---|---|---|---|
  | `apps/api` | 889/889 + 1 ignorado, 1m44s | **1009/1009 + 1 ignorado**, 1m57s | 18 ausentes (4 removidos e 14 renomeados), 138 novos (14 renomeados e 124 novos de verdade), 0 com resultado mudado | 100 → 100; 3,01 → 7,29 |
  | `tests/InboxOrchestratorRoundTrip.Tests` | 12/12, 18 s | **12/12**, 17 s | 1 renomeado (`RealIssuerToken_*` → `RealUserToken_*`) | 100 → 100; 7,29 → 5,20 |
  | `tests/ApiConnectorsRoundTrip.Tests` | 8/8, 6 s | **8/8**, 6 s | 1 renomeado (`RealIssuerToken_*` → `RealUserToken_*`) | 100 → 100; 5,20 → 5,00 |

  - **Comparação por nome** (`comparacao-*.txt`, `.trx` contra `.trx`):
    - **removidos, os 4 da 8.2:** `IdentityIssuerDisabledTests` (3 casos) e `AgentA2AAddressesWithoutPublicUrlTests.WithoutPublicUrlConfigured_AddressesAreAbsent`;
    - **renomeados, 14:** os 13 casos do `ClientCredentialsAccessTokenStorageHandlerTests`, que viraram `TokenEntryStorageHandlerTests` (12 casos da matriz com a regra nova e o de ordem), e `AfterTheSwitch_*For24Hours` → `*ForTheRetention`;
    - **novos:** distribuídos por 31 classes, todas listadas no `comparacao-api.txt`.
  - **Build `--no-incremental`** dos três projetos de teste, nos dois worktrees, com o mesmo conjunto de avisos: 9, 2 e 2 (`avisos-*.txt`). Os únicos de pacote são os `NU1903` do SSH.NET.
  - **Rodada com build limpo, a pedido do dono:**
    - **por quê:** os logs da rodada acima não provam que nenhum binário veio das rodadas de guarda. O `dotnet test` era incremental, e a linha `Buteco.Api -> …` não distingue compilado de reaproveitado;
    - **como:** as três suítes rodaram de novo, no mesmo protocolo (`fechamento-limpo-180.sh`, saídas em `180-fechamento-limpo/`). Antes de cada uma, `dotnet clean` e `dotnet build --no-incremental`, e depois `dotnet test --no-build`. O `Buteco.Api.dll` foi recompilado nas três vezes (13:46:03, 13:49:01 e 13:49:55, hora local), com o mesmo SHA-256 `a0ab69cfe52bd04729b78eb9094cd6a87018f55ef3736b4f898b477450098129`;
    - **resultado:** **1009/1009 + 1 ignorado**, **12/12** e **8/8**. Contra a rodada acima, por nome, nada ausente, nada novo e nada com resultado mudado; contra a baseline, a mesma diferença registrada;
    - **CPU:** 100 no início das três. No fim do `apps/api`, 72.

  **12.2 feita, 31/10/2026.** As saídas estão em `180-fechamento/manual/`.
  - **Migrations no banco de desenvolvimento** (pedido 3 do dono): `dotnet ef database update` aplicou `20261031144517_AddLocalUsers` e `20261031151210_ExtendIdentityKeyRetention` no `buteco_agents` do contêiner `buteco-agents_postgres_1`. **A migration de dados alterou uma linha só:**

    | `kid` | uso | `signs_until` | `validates_until` antes | depois |
    |---|---|---|---|---|
    | `fniY4qt8pBcSxBan9KihTw` | Signing | 2027-01-23 01:08:38+00 | 2027-01-24 01:08:38+00 (+24 h) | **2027-02-07 01:08:38+00 (+15 dias)** |
    | `8SqBDH2dRm13LBrkMTu2EQ` | Encryption | infinito | infinito | infinito, sem mudança |

    O `psql` avisou que o banco tem divergência de versão de collation (2.41 contra 2.36). O aviso é anterior à change e não tem relação com ela.
  - **`dotnet run` do `apps/api`** contra esse banco (`api-run.log`):
    - o log trouxe "Emissor OIDC ligado, issuer http://localhost:5017/";
    - o catálogo sincronizou: os 3 escopos e o `buteco-painel` criados;
    - o bootstrap logou "Primeiro Admin criado pelo bootstrap, usuário 01a192c3-…" (`EventId` 1188).
  - **Fluxo por `curl`** (`fluxo-curl.txt`):
    - **discovery:** `authorization_endpoint` e `end_session_endpoint` sob o issuer;
    - **sem sessão:** `302 /entrar?retorno=%2Fconnect%2Fauthorize%3F…`;
    - **sessão:** `POST /auth/session` deu `204`, com `buteco-sessao` `HttpOnly`, `samesite=strict` e 5 min. Sem `Secure`, porque o app roda em `Development`;
    - **com sessão:** `302` ao `redirect_uri` com o código;
    - **token:** access, refresh e id. O access tem `sub` = o id do usuário, `client_id` `buteco-painel`, `aud` `buteco-api`, `exp − iat` = 900 e nenhum `buteco_stamp`;
    - **rotas:** `GET /agents` com o token do emissor deu `200`, e o refresh deu `200`;
    - **logout com o hint:** `302` ao endereço registrado, com o cookie apagado. O refresh da sessão passou a dar `invalid_grant`;
    - **HMAC:** `POST /auth/login` e `GET /agents` com o token HMAC deram `200`, então o painel continua entrando como hoje;
    - **rehash:** depois do primeiro login, `users` tem o hash `$argon2id$v=19$m=19456,t=2,p=1$…`, com `password_hash_is_bootstrap_legacy = false`.
  - **Os quatro `curl` do D3.5 pelo nginx** (`nginx-curl.txt`). Usei `nginx:1.30-alpine`, a imagem final do `frontend`, com o `nginx.conf` versionado. A única mudança foi trocar `api:8080` pelo IP do host visto do contêiner (`192.168.127.254:5017`), porque o `apps/api` rodava fora de contêiner; com o IP literal, o `resolver` não entra, e é ele o problema da #212. No lugar do `dist`, um `index.html` marcador. O resultado:
    - o authorize respondeu `302 /entrar?retorno=…`, do `apps/api`;
    - o logout respondeu `302 /entrar`, do `apps/api`;
    - o `/connect/token` em navegação devolveu o shell do SPA, com o desvio mantido;
    - o `POST /auth/session` respondeu `204` com o cookie;
    - o controle `/entrar` devolveu o shell do SPA.
  - **No fim**, parei o `apps/api` e os contêineres de desenvolvimento, que estavam parados antes, e removi o contêiner do nginx.
- [x] 12.3 `/opsx:sync` e `/opsx:archive` dentro do worktree, com o `02` e o `CHANGELOG` escritos antes.

  **Feito, 31/10/2026**, com o `02` e o `CHANGELOG` escritos antes (11.3).
  - **Sync:** modificou 6 specs principais (`oidc-issuer`, `issuer-token-storage`, `route-authentication`, `issuer-token-validation`, `identity-signing-keys` e `agent-catalog`) e criou 2 (`local-users` e `issuer-user-login`, com `Purpose` escrito).
  - **`Purpose` corrigido:** o `oidc-issuer` e o `issuer-token-storage` ainda descreviam o estado de antes.
  - **Validação:** `openspec validate --all --strict` deu 81/81.
  - **Abertas de propósito:** a 12.4 (push e PR), que espera o ok do dono, e a 12.5.
- [ ] 12.4 Push e PR com `Closes #180`, só com autorização do dono naquela mensagem; mover a #180 para `In review`.
- [ ] 12.5 Depois do merge: remover os dois worktrees e a branch local.
