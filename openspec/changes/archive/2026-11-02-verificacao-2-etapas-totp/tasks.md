**Base:** `origin/main` em `86abb5d` (merge do PR #228, #217), 01/11/2026.
- **Worktree da change:** `../buteco-agentes-218`, branch `feat/218-verificacao-2-etapas-totp`.
- **Baseline:** worktree limpo separado, `../buteco-agentes-218-baseline`, no mesmo commit, sem edição nenhuma.

**Saídas completas:** `.log` inteiro, `.trx` ou relatório JSON do Vitest, e estado da máquina colado ao número, nunca filtrados (convenção 19), em:
- `~/.cache/buteco-agents/218-baseline/`;
- `~/.cache/buteco-agents/218-verificacao/` (e `guardas/` dentro dela);
- `~/.cache/buteco-agents/218-fechamento/`.

**Variáveis em toda rodada .NET:** `DOCKER_HOST` do Podman, `TESTCONTAINERS_RYUK_DISABLED=true`, `DOTNET_SYSTEM_NET_DISABLEIPV6=1` e `TZ=America/Sao_Paulo`.

**Antes de cada suíte**, e não só da primeira, o script `~/.cache/buteco-agents/bin/suite-218.sh` confere:
- `CPU_Speed_Limit = 100`;
- load de 1 min abaixo de 6 no `apps/frontend` e de 8 nas outras;
- relógio da VM acertado;
- nenhum contêiner em `podman ps`.

**O que muda nesta change:**
- `apps/api`: `Users/` (entidade, `TwoFactor/`, `CurrentUser`), `Users/Login/SessionEndpoints.cs`, `Identity/Flow/` (`IssuerSession`, `AuthorizeEndpoint`), `Identity/IdentityIssuerOptions.cs` e a checagem de boot, o `AesGcmEnvelope` com os dois cifradores existentes sobre ele, `Program.cs`, `appsettings*.json`, a migration `AddTwoFactor`;
- `apps/frontend`: só o `deploy/nginx.conf`;
- o compose de produção e os `.env*.example`;
- a documentação.

O código do `apps/inbox`, do `apps/connectors`, do `apps/workers` e de `tests/` não muda.

**Convenção 15 em todo guarda:** escrever o teste, reintroduzir o defeito nomeado na tarefa, ver reprovar **no componente que a correção toca**, guardar a saída em `218-verificacao/guardas/<tarefa>-reprova.log`, voltar a árvore e ver aprovar em `<tarefa>-aprova.log`. A tarefa só é marcada com as duas saídas registradas nela.

**Commit:** nenhum commit parcial, nem só da documentação ou dos artefatos do OpenSpec. O único commit da change acontece depois do sync e do archive (13.6), com tudo junto. Push e PR só com o ok do dono naquela mensagem.

**Paradas obrigatórias:**
- **grupo 2:** qualquer verificação que reprovar;
- **em qualquer grupo:** premissa do design que se mostrar falsa (convenção 9) **e** mudar comportamento, contrato, escopo de teste ou decisão do design. Achado que só toca comentário ou documentação é resolvido, registrado na tarefa com o motivo e listado no fechamento, sem parar (critério do dono, 01/11/2026).

Nos dois casos de parada, a decisão volta ao dono antes de seguir.

## 1. Baseline, antes de tocar código

- [x] 1.1 (`apps/api`, `apps/frontend`, `tests/InboxOrchestratorRoundTrip.Tests`, `tests/ApiConnectorsRoundTrip.Tests`) Baseline no worktree limpo em `86abb5d`.

      **Resultado, 01/11/2026, worktree `../buteco-agentes-218-baseline`, sem edição nenhuma.** O script esperou `CPU_Speed_Limit = 100` e o load antes de cada suíte:

      | suíte | resultado | duração | estado (início → fim) |
      |---|---|---|---|
      | `apps/api` | **1007/1007 + 1 ignorado** | 237 s | CPU 100 → 46; load 5,60 → 23,97 |
      | `apps/frontend` | **1978/1978**, 154 arquivos | 177 s | CPU 100 → 51; load 5,95 → 28,86 |
      | `tests/InboxOrchestratorRoundTrip.Tests` | **12/12** | 53 s | CPU 100 → 100 |
      | `tests/ApiConnectorsRoundTrip.Tests` | **8/8** | 23 s | CPU 100 → 100 |

      O `apps/api` foi de 1009 para 1007 desde a baseline da #217, conferido por nome (`218-baseline/api.trx.names`): −7 + 5, todos da #217 (design, Baseline). As durações do `apps/api` e do `apps/frontend` não servem de referência, porque a CPU caiu durante as duas.

- [x] 1.2 (`tests/CrossAppTaskStoreCompatibility.Tests`) Baseline do acordo da cifra de MCP entre o `apps/api` e o `apps/workers`, que a extração (D5, P4) pôs no escopo. O script ganhou o caso `rt-crossapp`.

      **Resultado, 01/11/2026**, mesmo worktree e commit: **4/4**, 22 s, CPU 100 → 100, load 1,72 → 2,26, `podman ps` vazio (`218-baseline/rt-crossapp.*`).

## 2. Verificação antes de qualquer código (parada obrigatória se reprovar)

- [x] 2.1 (`apps/api`, D5) **Vetores fixos da cifra de hoje, antes de tocar nela.** Com o código de `86abb5d`, num teste temporário (apagado depois), cifrar um texto conhecido com o `AesGcmMcpCredentialCipher` e um material conhecido com o `IdentityKeyMaterialCipher`, cada um com a chave de desenvolvimento. Guardar chave, texto e saída em `218-verificacao/2.1-vetores.txt`; eles viram as constantes do `AesGcmEnvelopeTests` (4.1). Conferir que a saída de MCP decifra no `apps/workers` (`Buteco.Workers.Mcp.Security.AesGcmMcpCredentialCipher`, pelo mesmo teste temporário no projeto do acordo). Se não decifrar, a premissa "o formato é o mesmo nos dois apps" caiu: parar.

      **Resultado, 01/11/2026** (`218-verificacao/2.1.log`, `2.1-vetores.txt`). Teste temporário no `CrossAppTaskStoreCompatibility.Tests`, que referencia os dois apps, apagado depois:
      - MCP, chave de dev `NtxqjqnK…`, texto `segredo-de-teste-218` → `dapDH6JS17MTdL2BWxIyaH9nmjuc4mg/31J6Q8af4BYhryXGEveJ6a+5NLP7wi/a`; **o `AesGcmMcpCredentialCipher` do `apps/workers` decifra o mesmo texto**;
      - chaves do emissor, chave de dev `zQlhPZ0H…`, material ASCII `material-de-teste-218` → base64 `gZF1xyhRIjxvEWZEfO93FVuH3oOzLAOt3T0Ew3/lIaUjCYh0KKWhxcWq7EHCF1imBg==`.

      A premissa "o formato é o mesmo nos dois apps" se confirmou.

- [x] 2.2 (`apps/api`, D2) **O cookie regravado.** Num teste temporário: depois de `SignInAsync` com um principal sem `buteco_2fa`, um segundo `SignInAsync` no mesmo esquema, com o claim, substitui o cookie (um `Set-Cookie` só com o valor novo, expiração de 5 min a partir do segundo), e o `AuthenticateAsync` seguinte lê o claim. Se o cookie antigo continuar válido ao lado do novo, ou a expiração não for refeita, o D2 muda: parar.

      **Resultado, 01/11/2026** (`218-verificacao/2.2.log`, `2.2-cookie.txt`), teste temporário com `AddIssuerSessionCookie` e `FakeTimeProvider`, apagado depois:
      - o primeiro `SignInAsync` (sem o claim) às 12:00; o segundo, 3 min depois, com o cookie do primeiro: **um** `Set-Cookie`, e o principal anterior não tinha o claim;
      - com o cookie novo: `buteco_2fa=ok`, expiração **12:08** (5 min a partir do segundo);
      - aos 6 min: o cookie novo autentica, e o primeiro não (venceu às 12:05).

      O D2 se sustenta. O cookie pendente antigo, se alguém o guardasse, vale até vencer, mas sem o claim o authorize o trata como ausência de sessão.

- [x] 2.3 (`apps/api`, D3) **O `UPDATE` condicional sob corrida.** Num teste temporário com o Postgres do Testcontainers: duas conexões executam ao mesmo tempo `UPDATE users SET two_factor_last_step = @p WHERE id = @id AND (two_factor_last_step IS NULL OR two_factor_last_step < @p)` (pelo `ExecuteUpdateAsync` do EF Core), 20 rodadas. Afirmar que a soma das linhas afetadas é 1 em toda rodada. E, com a forma "ler e depois escrever", registrar se a corrida reproduz (quantas rodadas deram 2). Se não reproduzir em 20, o guarda da corrida em 7.3 fica só no caso sequencial, com o registro, como o design prevê.

      **Resultado, 01/11/2026** (`218-verificacao/2.3.log`, `2.3-race.txt`). Teste temporário com Postgres do Testcontainers e duas conexões Npgsql por rodada, apagado depois. O SQL é o que o `ExecuteUpdateAsync` emite: um `UPDATE` só.
      - **`UPDATE` condicional: soma de linhas afetadas = 1 em 20 de 20 rodadas.**
      - **"ler e depois escrever", com as duas leituras forçadas antes das escritas por uma barreira: as duas aceitaram em 20 de 20.** A corrida existe quando as leituras se intercalam. Se ela reproduz sem barreira, pelo HTTP, a 7.3 diz; se não reproduzir, o guarda fica só no caso sequencial, como o design prevê e o dono confirmou (01/11/2026).

- [x] 2.4 (`apps/api`, D8, D9) **O `sub` no principal das rotas.** Conferir, por teste temporário, que numa rota autenticada o principal do token do emissor tem `NameIdentifier` = `operator` e o claim `sub` com o id do usuário, e que o do HMAC `operator` não tem `sub` (`IssuerTokenAuthentication.MapPrincipalByClient`, `OperatorTokenAuthenticationHandler` l. 38). Se o HMAC trouxer `sub`, o `CurrentUser` muda: parar.

      **Resultado, 01/11/2026** (`218-verificacao/2.4.log`, `2.4-sub.txt`), teste temporário com um middleware de sonda, apagado depois:
      - token do emissor: `nameidentifier=operator; client_id=buteco-painel; sub=<id do usuário>`;
      - HMAC `operator`: só `nameidentifier=operator`, **sem `sub`**.

      O `CurrentUser` fica como o design diz.


## 3. TOTP e códigos de recuperação, sem banco (`apps/api`, D3, D4, D6)

- [x] 3.1 `Users/TwoFactor/Totp.cs` e `TotpTests`: HMAC-SHA1 do contador big-endian, truncagem dinâmica, módulo 10⁶ com zeros à esquerda, passo de 30 s pelo `TimeProvider`, comparação em tempo constante. Os seis vetores do Apêndice B em 6 dígitos (`two-factor-totp`, "Vetores do Apêndice B"). **Guardas:** contador em little-endian; truncagem sem `& 0x7f`; código sem preencher zeros (os casos `005924` e `081804` reprovam).

      **Resultado, 01/11/2026:** `Users/TwoFactor/Totp.cs`; `TotpTests` 13/13 (os seis vetores, a normalização e o tamanho do segredo). Guardas em `218-verificacao/guardas/`, cada um com `-defeito.diff`, `-reprova.log` e `-aprova.log`, pelo `~/.cache/buteco-agents/bin/guarda-218.sh`:
      - `3.1-little-endian`: os 6 vetores reprovam;
      - `3.1-sem-0x7f`: 4 dos 6 reprovam (59, 1111111111, 1234567890, 20000000000). Nos outros dois o bit alto do trecho truncado já é 0;
      - `3.1-sem-zeros`: os 3 com zero à esquerda reprovam (`081804`, `050471`, `005924`).

- [x] 3.2 `TotpWindowTests`: aceita T−1, T e T+1 e devolve o passo que bateu; recusa T−2 e T+2; espaços ignorados. **Guarda:** janela de ±2 (o caso de T±2 reprova).

      **Resultado, 01/11/2026:** `TotpWindowTests` 5/5. Guarda `3.2-janela-2`: os dois casos de T±2 reprovam.

- [x] 3.3 `Base32.cs` e `Base32Tests`: os vetores do RFC 4648 §10 sem padding; 20 bytes dão 32 caracteres.

      **Resultado, 01/11/2026:** `Base32.cs`; `Base32Tests` 8/8. Sem guarda, como o design (D12).

- [x] 3.4 `OtpAuthUri.cs` e `OtpAuthUriTests`: o URI do D8, com o issuer `Buteco Agentes` e o e-mail codificados no rótulo e no `issuer`. **Guarda:** e-mail sem codificar (o caso com `+` no e-mail reprova).

      **Resultado, 01/11/2026:** `OtpAuthUri.cs`; `OtpAuthUriTests` 2/2 (o URI inteiro com o vetor do RFC, e o `+`). Guarda `3.4-email-sem-codificar`: os dois reprovam.

- [x] 3.5 `RecoveryCode.cs` e `RecoveryCodeTests`: 16 caracteres de Crockford em grupos de quatro; a normalização (caixa, hífens, espaços, `I`/`L` → `1`, `O` → `0`); SHA-256 do normalizado; 10 distintos por geração. **Guarda:** normalização sem as trocas de Crockford (o caso com `O` no lugar de `0` reprova).

      **Resultado, 01/11/2026:** `RecoveryCode.cs`; `RecoveryCodeTests` 9/9. Guarda `3.5-sem-crockford`: `CodeTypedAnotherWay_IsTheSameCode` reprova, e só ele.

## 4. Cifra e chave (`apps/api`, D5)

- [x] 4.1 `Infrastructure/AesGcmEnvelope.cs` (`Seal`/`Open` sobre bytes, AAD opcional, `ParseKey(nome, valor)` com as mensagens de hoje) e `AesGcmEnvelopeTests`: os vetores da 2.1 abrem; com AAD, abre só com o mesmo AAD. **Guarda:** formato com a tag antes do texto (os vetores da 2.1 reprovam).

      **Resultado, 01/11/2026:** `Infrastructure/AesGcmEnvelope.cs`; `Infrastructure/AesGcmEnvelopeTests` 5/5, com os vetores da 2.1 como constantes. Guarda `4.1-tag-antes` (o `Open` lendo a tag logo depois do nonce): os dois vetores reprovam, e o caso do dado associado também.

- [x] 4.2 `AesGcmMcpCredentialCipher` e `IdentityKeyMaterialCipher` sobre o envelope, sem mudar o formato nem as mensagens. **Prova:** `AesGcmMcpCredentialCipherTests`, `IdentityKeyMaterialCipherTests` e os `Identity/IdentityKey*Tests` passam **sem nenhuma alteração nos arquivos de teste**: `git diff --stat 86abb5d -- apps/api/tests/Buteco.Api.Tests/AesGcmMcpCredentialCipherTests.cs apps/api/tests/Buteco.Api.Tests/Identity/IdentityKey*` vazio, colado aqui, e os nomes iguais aos da baseline no `.trx`. O `tests/CrossAppTaskStoreCompatibility.Tests` roda de novo: 4/4.

      **Resultado, 01/11/2026** (`218-verificacao/4.2-*`): os dois cifradores sobre o envelope, com as mesmas mensagens e a mesma API pública (`IdentityKeyMaterialCipher.ParseKey` e `KeySizeBytes`). O comentário do `IdentityKeyMaterialCipher` que dizia "segundo consumidor… a convenção 2 espera o terceiro" saiu: descreve só o estado atual.
      - `AesGcmMcpCredentialCipherTests`, `IdentityKeyMaterialCipherTests` e os `Identity/IdentityKey*Tests`: **36 de 36 nomes da baseline presentes e aprovados** no `4.2-api.trx`, mais os 5 do envelope (41/41);
      - `git diff --stat 86abb5d -- <os arquivos de teste> tests/CrossAppTaskStoreCompatibility.Tests`: **vazio** (`4.2-diff-testes.txt`, 0 bytes);
      - `CrossAppTaskStoreCompatibility.Tests`: **4/4**, os 4 nomes da baseline.

- [x] 4.3 `Users/TwoFactor/TwoFactorSecretCipher.cs` (envelope com AAD = id do usuário) e `TwoFactorSecretCipherTests`: ida e volta; o segredo de um usuário não abre com o id de outro; chave errada lança. **Guarda:** AAD retirado (o caso do outro usuário reprova).

      **Resultado, 01/11/2026:** `Users/TwoFactor/TwoFactorSecretCipher.cs`; `TwoFactorSecretCipherTests` 4/4. Guarda `4.3-sem-aad`: `SecretCopiedToAnotherUser_DoesNotOpen` reprova, e só ele.

- [x] 4.4 `Identity:TwoFactorEncryptionKey` em `IdentityIssuerOptions`, `appsettings.json` (vazia) e `appsettings.Development.json` (chave de dev, distinta das outras duas), e a checagem na `ValidateIdentityIssuerConfiguration`: ausente, malformada, com tamanho errado, ou igual a `Identity:SigningKeyEncryptionKey` ou a `Mcp:CredentialEncryptionKey` → boot falha com mensagem que nomeia a chave. `IdentityIssuerStartupValidationTests` estendido, pela composição real. `TestAuthentication` ganha a chave nos overrides. **Guarda:** a chamada da checagem retirada do `Program.cs` (o caso da chave ausente reprova); a comparação de igualdade retirada (o caso da chave igual reprova).

      **Resultado, 01/11/2026:** a chave em `IdentityIssuerOptions`, a seção `Identity:TwoFactor` nos dois `appsettings` (fechado no base, aberto em `Development`), a chave de teste e o cadastro aberto no `TestAuthentication.ConfigOverrides`. `IdentityIssuerStartupValidationTests` 14/14 (+5: ausente, base64 inválido, 16 bytes, igual à de assinatura, igual à de MCP). Guardas:
      - `4.4-sem-chamada` (`app.ValidateIdentityIssuerConfiguration()` fora do `Program.cs`): os 5 casos novos reprovam, com os 5 antigos da mesma checagem;
      - `4.4-sem-igualdade`: os 2 casos de chave igual reprovam, e só eles.

      Os projetos de `tests/` não sobrescrevem a chave e pegam a de desenvolvimento do `appsettings.Development.json` (a `WebApplicationFactory` roda em `Development`), então o código deles não muda; o fechamento confirma.

## 5. Modelo de dados (`apps/api`, D11)

- [x] 5.1 `User.cs`: as cinco colunas, e `StartTwoFactorEnrollment`, `EnableTwoFactor`, `RemoveTwoFactor` e `ResetTwoFactor` (este troca o stamp; os outros não). `UserRecoveryCode` e o `DbSet`. O mapeamento no `AppDbContext`, com o índice único (`user_id`, `code_hash`) e a FK.

      **Resultado, 01/11/2026:** `User` com as cinco propriedades, `IsTwoFactorEnabled`, `StartTwoFactorEnrollment`, `EnableTwoFactor` (grava o passo da confirmação), `RemoveTwoFactor` e `ResetTwoFactor` (o único que troca o stamp); `UserRecoveryCode`; o mapeamento em `AppDbContext`, com a FK `ON DELETE CASCADE` e o índice único (`user_id`, `code_hash`).

- [x] 5.2 Migration `AddTwoFactor`, só aditiva, gerada pelo EF; conferir o SQL (`dotnet ef migrations script`) em `218-verificacao/5.2-migration.sql`: nenhuma coluna existente alterada, nenhum `DROP`. O `apps/workers` não mapeia nada disso.

      **Resultado, 01/11/2026:** `20261102002816_AddTwoFactor`, gerada pelo `dotnet ef` (ferramenta 10.0.10, runtime 10.0.13: só o aviso de versão). O SQL (`218-verificacao/5.2-migration.sql`): 5 `ALTER TABLE users ADD` de colunas nulas, `CREATE TABLE user_recovery_codes` com a FK em cascata e o índice único. Nenhuma coluna existente alterada, nenhum `DROP`.

## 6. Quem chama (`apps/api`, D8, D9)

- [x] 6.1 `Users/CurrentUser.cs`: o usuário ativo do `sub` do token do emissor, ou `403`. `TwoFactorCallerTests`: HMAC `operator` em `GET /account/two-factor` → `403`; HMAC `operator` em `POST /users/{id}/two-factor/reset` → `403`, e o alvo continua com a verificação, os códigos e o mesmo stamp; usuário desativado com access token ainda válido → `403` nas duas (`two-factor-totp`, "As rotas de 2 etapas atendem só o usuário real do token"). **Guarda:** a rota de redefinição aceitando o HMAC (sem o `CurrentUser`, com o acesso do operador só pela política) → o caso do HMAC na redefinição reprova. Os casos ficam escritos aqui e rodam quando as rotas existirem (8.1 e 10.1); a tarefa é marcada com os logs do guarda depois da 10.1.

      **Resultado, 01/11/2026:** `Users/CurrentUser.cs` (7.2); `Users/TwoFactor/TwoFactorCallerTests` 3/3: HMAC em `GET /account/two-factor` → `403`; HMAC na redefinição → `403`, com o alvo igual (verificação, códigos e stamp); usuário desativado com access token válido → `403` nas duas. Guarda `6.1-redefinicao-aceita-hmac` (a redefinição sem o `CurrentUser`, com o acesso só pela política): reprovam o caso do HMAC na redefinição e o do desativado. Rodado depois da 10.1, como a tarefa previa.

## 7. O segundo passo no login (`apps/api`, D2, D3, D6, D7)

- [x] 7.1 `IssuerSession`: o claim `buteco_2fa`. `SessionEndpoints`: com a verificação ligada, a senha certa grava o cookie pendente e responde `200 { "segundoPasso": "totp" }`, **sem zerar a contagem e sem registrar o último acesso**; sem ela, como hoje. `AuthorizeEndpoint`: a regra pelo estado atual do usuário.

      **Resultado, 01/11/2026:** `IssuerSession.TwoFactorClaim` e `IssuerSession.SignInAsync` (o cookie de sessão gravado num lugar só, pelo login e pelo segundo passo); `SessionEndpoints` com o `200 { "segundoPasso": "totp" }`, sem zerar a contagem e sem último acesso; `AuthorizeEndpoint` com a regra pelo estado atual do usuário. **O cookie do login de quem não tem a verificação não leva o claim:** o claim quer dizer "passou o segundo passo", e é isso que faz o cookie de antes de ligar a verificação não valer depois (cenário "Cookie anterior à verificação").

- [x] 7.2 `POST /auth/session/second-factor` (guardas de JSON e `Origin`, `LoginRateLimiting.Marker`, `AuthEntryPoint`) e `TwoFactorVerifier` (código do app ou de recuperação pela forma; passo e `used_at` por `UPDATE` condicional; decifração dentro do `try`).

      **Resultado, 01/11/2026:** a rota em `SessionEndpoints` (`SecondFactorPath`), com os guardas, o `Marker` e a classificação; `Users/TwoFactor/TwoFactorVerifier.cs` (forma decide o tipo; os dois `UPDATE` condicionais; a decifração dentro do `try`, com o `EventId` 1194 `TwoFactorSecretUnreadable`, o seguinte livre depois do 1193); `Users/CurrentUser.cs`; e, para o resto da change, `AccountTwoFactorEndpoints`, `UserTwoFactorResetEndpoint`, `TwoFactorOptions` e o registro em `AddLocalUsers` e no `Program.cs`.

- [x] 7.3 `TwoFactorLoginTests`, um caso por cenário de `issuer-user-login` e de `two-factor-totp` sobre o login:
  - fluxo com a verificação (senha → `200`, authorize com o pendente → `302 /entrar`, código → `204`, authorize → código, token); sem a verificação, `204` como antes;
  - cookie completo gravado antes de ligar a verificação → authorize como sem sessão;
  - código de recuperação → `204`; o mesmo de novo → `401`;
  - o mesmo código TOTP duas vezes → `401` na segunda; T−1 depois de T aceito → `401`;
  - submissões simultâneas → um `204` e um `401` (se a 2.3 reproduziu a corrida);
  - sem cookie, vencido, ou stamp trocado no meio → `401` "A entrada expirou", sem contar falha; `Origin` de outro site → `403`.

  **Guardas:** authorize que não confere o claim (o caso do pendente reprova); regra pelo cookie e não pelo usuário (o caso do cookie anterior reprova); `used_at` sem `IS NULL` (o caso do código de recuperação repetido reprova); conferência sem `two_factor_last_step` (os dois casos de reuso reprovam); ler e depois escrever, sem o `UPDATE` condicional (o caso simultâneo reprova, se a 2.3 reproduziu).

      **Resultado, 01/11/2026:** `Users/TwoFactor/TwoFactorLoginTests` 10/10, com o apoio `Support/TwoFactorTestClient.cs`. Guardas:
      - `7.3-authorize-sem-claim`: reprovam o fluxo com a verificação (o pendente dá código) e o do cookie anterior;
      - `7.3-regra-pelo-cookie` (dois arquivos, pelo `guarda-218-multi.sh`: o login marca o cookie pendente, e o authorize recusa só a marca): reprova **só** o do cookie anterior;
      - `7.3-recuperacao-sem-is-null`: reprova o do código de recuperação repetido;
      - `7.3-sem-ultimo-passo`: reprovam o mesmo código duas vezes, o passo anterior e o simultâneo;
      - `7.3-ler-depois-escrever` (o passo conferido na entidade lida no início do pedido, e não no `UPDATE`): **o caso simultâneo reprovou em 20 de 20 rodadas** (`-reprova.log`). A corrida reproduz pelo HTTP, e o guarda dela fica, além do sequencial.

- [x] 7.4 `TwoFactorThrottleTests` (`local-users`, "Bloqueio progressivo"): senha certa e código errado alternados cinco vezes → a tentativa seguinte `423`; bloqueado, o segundo passo com código válido → `423` sem marcar o código; a senha certa sozinha não muda o último acesso. **Guarda:** a senha certa zerando a contagem com o código pendente (o caso alternado reprova).

      **Resultado, 01/11/2026:** `Users/TwoFactor/TwoFactorThrottleTests` 3/3 (o alternado bloqueia; bloqueado, o segundo passo dá `423` e o passo gravado não muda; a senha sozinha não grava último acesso). Guarda `7.4-senha-zera` (o `ResetAsync` de volta antes do `200`): o caso alternado reprova, e só ele.

- [x] 7.5 `RouteAuthenticationTests`: o segundo passo é anônimo e classificado; `GET /account/two-factor` sem token → `401`; a checagem de boot das rotas anônimas continua passando.

      **Resultado, 01/11/2026:** dois casos novos no `RouteAuthenticationTests`: o segundo passo sem token responde o `401` dele, com o título; `GET /account/two-factor`, `POST /account/two-factor/setup` e `POST /users/{id}/two-factor/reset` sem token → `401` vazio. O filtro `RouteAuthentication` (inclui `RouteAuthenticationStartupFailureTests`, a checagem de boot das rotas anônimas): 23/23. Sem guarda, como o design (D12).

## 8. Cadastro (`apps/api`, D1, D8)

- [x] 8.1 `TwoFactorOptions` (`Identity:TwoFactor:EnrollmentEnabled`, falsa por padrão, verdadeira em `appsettings.Development.json` e nas fixtures) e `AccountTwoFactorEndpoints`: `GET`, `setup` (com a senha, Argon2id, contada no bloqueio), `confirm`. Rotas no grupo `/account/two-factor`.

      **Resultado, 01/11/2026:** `TwoFactorOptions` (7.2), `AccountTwoFactorEndpoints` com `GET`, `setup` e `confirm` no grupo `/account/two-factor`. O `setup` confere, nesta ordem: cadastro aberto, usuário, bloqueio, já ligada (`409`), senha. O `confirm` grava a ativação e os 10 códigos numa transação.

- [x] 8.2 `TwoFactorEnrollmentTests`, um caso por cenário de "Cadastro em dois tempos", "Cadastro fechado até a tela existir" e "Situação da verificação": fechado → `403` em `setup` e `confirm`, sem pendente, e `cadastroDisponivel: false`; senha errada → `401` contada; segredo de 32 caracteres e URI; `confirm` errado → `401`; certo → 10 códigos e o login seguinte pede o segundo passo; vencido (10 min, `FakeTimeProvider`) → `400`; `setup` com a verificação ligada → `409`; o `GET` sem o segredo. **Guardas:** `confirm` que liga sem conferir o código (o caso do código errado reprova); pendente sem prazo (o caso vencido reprova); a opção ignorada no `setup` (o caso fechado reprova).

      **Resultado, 01/11/2026:** `TwoFactorEnrollmentTests` 13/13 (cadastro, situação, regeneração e remoção, com o apoio `TwoFactorAccountScenario.cs`) e `TwoFactorEnrollmentClosedTests` 5/5. Guardas:
      - `8.2-confirm-sem-codigo` (o `MatchPending` devolvendo o passo atual sem conferir): reprova `Confirm_WithAWrongCode_DoesNotTurnItOn`, e só ele;
      - `8.2-pendente-sem-prazo`: reprova `Confirm_AfterTenMinutes_Is400`, e só ele;
      - `8.2-opcao-ignorada` (o `setup` sem conferir a opção): reprova `Setup_IsClosed_AndNothingIsPending`, e só ele; o `confirm` fechado continua `403`, pela própria checagem.

- [x] 8.3 Com o cadastro fechado, a verificação no login e a redefinição continuam valendo: os dois cenários de "Cadastro fechado até a tela existir", com a verificação ligada por semente de teste.

      **Resultado, 01/11/2026:** em `TwoFactorEnrollmentClosedTests`, `TheVerification_StillAppliesAtLogin` (senha → `200` com o cadastro fechado) e `TheAdminReset_StillWorks` (`204`), com a verificação ligada por semente.

## 9. Regeneração e remoção (`apps/api`, D6, D8)

- [x] 9.1 `POST /account/two-factor/recovery-codes` e `POST /account/two-factor/remove`, com o código do app ou de recuperação, contados no bloqueio; `409` sem a verificação.

      **Resultado, 01/11/2026:** `recovery-codes` e `remove` em `AccountTwoFactorEndpoints`, pelo mesmo `VerifiedUserAsync` (usuário, `409` sem a verificação, bloqueio, código contado), cada um numa transação.

- [x] 9.2 Testes de "Regeneração dos códigos e remoção": regenerar invalida um código antigo ainda não usado; remover com código → `204` e o login volta ao `204`; código errado → `401`, contado, e a verificação continua. **Guarda:** regeneração que acrescenta sem apagar (o caso do código antigo reprova).

      **Resultado, 01/11/2026:** os casos estão no `TwoFactorEnrollmentTests` (8.2): regenerar invalida um código antigo; remover com código → `204`, o login volta ao `204` e os códigos saem; código errado → `401`, contado, e continua ligada; sem a verificação → `409` nas duas. Guarda `9.2-regenera-sem-apagar`: reprova `Regenerate_InvalidatesTheOldCodes`, e só ele.

## 10. Redefinição pelo admin (`apps/api`, D9)

- [x] 10.1 `UserTwoFactorResetEndpoint`: `POST /users/{id}/two-factor/reset` pelo `CurrentUser`, numa transação; `204`, `404`, `409` sem trocar o stamp, `403` para si mesmo.

      **Resultado, 01/11/2026:** `Users/TwoFactor/UserTwoFactorResetEndpoint.cs` (7.2), mapeado no `Program.cs`.

- [x] 10.2 `TwoFactorAdminResetTests`, um caso por cenário de "Redefinição da verificação pelo admin": o outro volta ao `204` e a renovação dele recebe `invalid_grant`; a si mesmo → `403`; sem a verificação → `409` e o stamp igual; inexistente → `404`. **Guardas:** troca do stamp retirada (o caso da renovação reprova); a recusa de si mesmo retirada (o caso de si mesmo reprova). Depois desta, marcar a 6.1 com os logs do guarda dela.

      **Resultado, 01/11/2026:** `TwoFactorAdminResetTests` 4/4. Guardas: `10.2-sem-troca-do-stamp` reprova o caso da renovação, e só ele; `10.2-sem-recusa-de-si` reprova `ResetYourself_Is403`, e só ele. A 6.1 foi marcada em seguida.

## 11. Chave trocada (`apps/api`, D5)

- [x] 11.1 `TwoFactorKeyChangeTests` ("Segredo que não decifra não bloqueia os códigos de recuperação"): segredo cifrado com outra chave → código do app `500` com a mensagem, sem contar falha, log `Error` com o `EventId` (pelo `IdentityIssuerLogCapture`); código de recuperação → `204`. **Guarda:** a decifração fora do `try` (o caso do log reprova: `500` genérico sem o `EventId`).

      **Resultado, 01/11/2026:** `TwoFactorKeyChangeTests` 2/2 (o segredo regravado com outra chave por `ExecuteUpdate`). Guarda `11.1-decifra-fora-do-try` (o `Unprotect` antes do `try`): reprova o caso do `500` com o log; o de recuperação continua aprovando.

## 12. Deploy e documentação (antes do archive)

- [x] 12.1 `docker-compose.prod.yml`: `Identity__TwoFactorEncryptionKey: ${IDENTITY_TWO_FACTOR_ENCRYPTION_KEY:?…}` no `api`, com a mensagem que diz como gerar. `.env.prod.example` e `.env.example`. Conferir `podman compose -f docker-compose.prod.yml --env-file <cópia sem a variável> config` falhando e citando a variável, colado aqui.

      **Resultado, 01/11/2026** (`218-verificacao/12.1/`): `Identity__TwoFactorEncryptionKey: ${IDENTITY_TWO_FACTOR_ENCRYPTION_KEY:?…}` no `api`, e o bloco no `.env.prod.example`. `podman compose -f docker-compose.prod.yml --env-file <cópia sem a variável> config` → exit 1, `required variable IDENTITY_TWO_FACTOR_ENCRYPTION_KEY is missing a value: defina IDENTITY_TWO_FACTOR_ENCRYPTION_KEY (openssl rand -base64 32, distinta das outras chaves) — use --env-file .env.prod`; com ela, exit 0. **Achado só de documentação (critério do dono):** o `.env.example` não tem nenhuma das chaves do emissor (o desenvolvimento usa as do `appsettings.Development.json`), então ele não muda; a tarefa o citava por engano.

- [x] 12.2 (`apps/frontend`) `deploy/nginx.conf`: `account|users` no bloco do `apps/api`, e o comentário do topo com os prefixos. Reconferir a lista com os `grep` do próprio comentário, colados aqui. O `nginxHeaders.test.ts` continua passando.

      **Resultado, 01/11/2026:** `account|users` no bloco do `apps/api`, e o comentário do topo com o ruído novo dos `grep` (`/setup`, `/confirm`, `/recovery-codes`, `/remove`, sub-rotas de `/account/two-factor`). Os `grep` do comentário (`218-verificacao/12.2-prefixos.txt`), `apps/api`: `/account /agents /auth /confirm /connect /connectors /entrar /health /indexing-summary /insights /knowledge-bases /knowledge-index /mcp-servers /providers /recovery-codes /remove /setup /sync /test /users`. Os servidos de primeiro nível estão todos no bloco; `/connect` tem blocos próprios, e `/connectors`, `/sync` e `/entrar` são ruído já registrado ou destino de redirecionamento. `nginxHeaders.test.ts` 5/5 (o worktree precisou de `npm ci`).

- [x] 12.3 `docs/deployment.md`: a chave nova (gerar, distinta, não trocar sem redefinir todos os usuários com a verificação); **NTP ativo no host, porque um desvio maior que 30 s faz os códigos não baterem para todos** (D3), com o `timedatectl`; o cadastro fechado até a #183 e o que acontece se abrir antes (D1).

      **Resultado, 01/11/2026:** `docs/deployment.md`, a linha da variável na tabela e a seção "Verificação em 2 etapas (#218)": gerar e guardar a chave, distinta; não trocar sem redefinir (o `500`, o evento 1194, a saída pela redefinição); **NTP ativo no host, com o `timedatectl`**; a migration; a ordem `migrator` → `api` → `frontend`; o cadastro fechado e por que não abrir antes da #183; os `curl` de conferência; o rollback.

- [x] 12.4 `docs/development.md`: **com o cadastro aberto em desenvolvimento, quem cadastrar o TOTP pela API não entra pelo painel até a #183; a saída é remover com um código (`POST /account/two-factor/remove`) ou pedir a redefinição a outro usuário** (D1); e o `curl` do cadastro e do segundo passo.

      **Resultado, 01/11/2026:** `docs/development.md`, "Verificação em 2 etapas em desenvolvimento": o aviso de que quem cadastrar pela API não entra pelo painel até a #183, com as duas saídas (remover com um código, ou a redefinição por outro usuário) e, sem nenhuma das duas, o `UPDATE` no banco local; o `curl` do cadastro, da confirmação, do segundo passo e da remoção, com uma função de shell que calcula o código (conferida na 13.3).

- [x] 12.5 `docs/configuration.md` (as duas configurações novas), `SECURITY.md` (segredo cifrado com AAD, códigos em hash, o bloqueio compartilhado, o segredo devolvido uma vez) e `01-ARQUITETURA_E_CONVENCOES.md` (Autenticação: as 2 etapas; o terceiro consumidor de AES-GCM e a extração, na convenção 2/3).

      **Resultado, 01/11/2026:** `docs/configuration.md` (as duas configurações na tabela do `apps/api` e a variável na do stack); `SECURITY.md` (a área "Two-step verification (TOTP)" e a exceção do segredo devolvido uma vez); `01` (a seção "Verificação em 2 etapas" em Autenticação, e o envelope e a exceção na convenção 3). `python3 -I scripts/check-docs.py`: sem violação.

      **Ordem:** a 12.6 fica para depois da 13.1–13.3, porque o `02` e o `CHANGELOG` registram o que o fechamento mediu; os dois continuam antes do sync e do archive.

- [x] 12.6 `02-HISTORICO_E_STATUS.md` e `CHANGELOG.md`, com o que a implementação mediu e as divergências do design (convenção 9), escritos antes do sync e do archive.

      **Resultado, 01/11/2026:** o `02` ganhou a seção `verificacao-2-etapas-totp` (decisões, o que entrou, o que o fechamento mediu, os 24 guardas, as divergências só de documentação, os registros e o deploy), com o tamanho entregue (+1.188 / −161 de produção, +1.473 de testes, contra ~1.070 / ~1.740 projetados); o `CHANGELOG` ganhou a entrada em Autenticação. Escritos depois da 13.1–13.3 e antes do sync e do archive. `check-docs.py`: OK.

## 13. Fechamento

- [x] 13.1 As cinco suítes da baseline no worktree da change, pelo mesmo script e protocolo, com `CPU_Speed_Limit = 100` conferido antes de cada uma: `apps/api`, `apps/frontend` (com o JSON), os dois de `tests/` do login e o `CrossAppTaskStoreCompatibility.Tests`. Comparação por nome contra a baseline: nenhum nome da baseline ausente, e os novos listados por classe.

      **Resultado, 01/11/2026** (`218-fechamento/`, comparação em `13.1-comparacao.txt`), worktree da change sobre `86abb5d`, 34 arquivos alterados, pelo `suite-218.sh`, com `CPU_Speed_Limit = 100` conferido antes de cada suíte:

      | suíte | baseline | fechamento | por nome | estado (início → fim) |
      |---|---|---|---|---|
      | `apps/api` | 1007 + 1 ignorado | **1102 + 1 ignorado**, 2m59s | 0 ausentes, 0 não aprovados, **95 novos**, todos das classes da change | CPU 100 → 65; load → 23,21 |
      | `apps/frontend` | 1978/1978 | **1978/1978**, 154 arquivos, 160 s (2ª rodada) | 0 ausentes, 0 não aprovados | load 4,24, CPU 100 → load 28,61, CPU 53 |
      | `tests/InboxOrchestratorRoundTrip.Tests` | 12/12 | **12/12** | iguais | CPU 100 → 100 |
      | `tests/ApiConnectorsRoundTrip.Tests` | 8/8 | **8/8** | iguais | CPU 100 → 100 |
      | `tests/CrossAppTaskStoreCompatibility.Tests` | 4/4 | **4/4** | iguais | CPU 100 → 100 |

      Os 95 novos do `apps/api`, por classe: `TotpTests` 13, `TwoFactorEnrollmentTests` 13, `TwoFactorLoginTests` 10, `RecoveryCodeTests` 9, `Base32Tests` 8, `TotpWindowTests` 5, `TwoFactorEnrollmentClosedTests` 5, `AesGcmEnvelopeTests` 5, `IdentityIssuerStartupValidationTests` 5, `TwoFactorAdminResetTests` 4, `TwoFactorSecretCipherTests` 4, `RouteAuthenticationTests` 4, `TwoFactorCallerTests` 3, `TwoFactorThrottleTests` 3, `OtpAuthUriTests` 2, `TwoFactorKeyChangeTests` 2.

      **A 1ª rodada do `apps/frontend` (`frontend-1a.*`) reprovou 30 de 1978, em 10 arquivos**, em 399 s: 21 com `Test timed out in 15000ms`, 6 esperas assíncronas (`findBy`/`waitFor`) vencidas, e 3 asserções com valor diferente, todas de digitação embaralhada pelo `userEvent` sob carga (`"êniorFina"` no lugar de `"Financeiro"`, `"Pn o(vroe-ntoomkeeando)"` no lugar de `"novo-token"`, `"m p á tico"` no lugar de `null`). **Corrigido em 01/11/2026, ao comentar na #226:** a primeira versão deste registro dizia "todos com timeout, sem asserção por conteúdo", lida só da primeira linha das mensagens; o log diz o contrário em 9 dos 30. Ela largou logo depois do `apps/api`: load de 1 min 5,49 (abaixo dos 6 do script) e **de 5 min 12,21**; o load chegou a 34,33. Nenhum código do `apps/frontend` mudou (só o `deploy/nginx.conf`, que nenhum desses testes lê). A 2ª rodada, na mesma árvore e sem nenhuma edição, deu 1978/1978. É a forma da convenção 19 já registrada (timeout sob carga que a própria suíte cria), e a #226 já trata o `KnowledgeBaseCreatePage.sync.test.tsx`, que concentra 11 dos 30; os outros 9 arquivos (formulários de agente, MCP, canal e base) são novos nessa forma. **Achado de registro, sem mudança de código:** o script só confere o load de 1 min, e a suíte do frontend logo depois da do `apps/api` larga com a máquina ainda carregada.

- [x] 13.2 Build `--no-incremental` do `apps/api` e do projeto de testes com o mesmo conjunto de avisos da baseline; `npm run build` do `apps/frontend` não é preciso (só o `nginx.conf` muda).

      **Resultado, 01/11/2026** (`218-fechamento/13.2-build-*.log`): `dotnet build --no-incremental` do `Buteco.Api.Tests` (com o `Buteco.Api`) nos dois worktrees, exit 0 nos dois, **18 avisos nos dois, e o mesmo conjunto** depois de normalizar os caminhos. O `apps/workers`, o `apps/inbox` e o `apps/connectors` não mudaram, e os projetos de `tests/` compilaram nas suítes da 13.1.

- [x] 13.3 Stack local (`docker-compose.prod.yml`, como na 11.3 da #217), com a saída colada aqui:
  - `GET /account/two-factor` e `POST /users/{id}/two-factor/reset` sem token → `401` do `apps/api`, sem `text/html`; `/accountxyz` → o shell do SPA;
  - o login de um usuário sem a verificação → `204`, e o fluxo de `curl` do `development.md` até o access token;
  - `GET /account/two-factor` com o token → `cadastroDisponivel: false`;
  - com o cadastro aberto **só nesse stack descartável** (`Identity__TwoFactor__EnrollmentEnabled=true`): `setup`, `confirm` com um código calculado por um script local a partir do segredo, login com `200 {segundoPasso}`, segundo passo `204`, authorize com código.

      **Resultado, 01/11/2026** (`218-fechamento/stack/`): `podman compose -p stack218 -f docker-compose.prod.yml --env-file <segredos descartáveis> up -d --build`, com `PUBLIC_DOMAIN=http://localhost:18218` e o bootstrap (`Primeiro Admin criado pelo bootstrap`). O migrator aplicou a `20261102002816_AddTwoFactor` (cinco colunas e `user_recovery_codes` conferidas por `psql`). O `resolver` do nginx foi trocado **só dentro do contêiner** para o `nameserver` do `resolv.conf` (`10.89.5.1`), o contorno da #212, e de novo depois de recriar o `api`, porque o podman-compose recriou o `frontend` junto mesmo com `--no-deps`. Stack derrubado com `down -v` no fim.

      **Cadastro fechado, como em produção** (`13.3-curl-fechado.txt`):
      ```
      GET  /account/two-factor                      → 401 content-type=[] bytes=0
      POST /account/two-factor/setup                → 401 content-type=[] bytes=0
      POST /users/{id}/two-factor/reset             → 401 content-type=[] bytes=0
      GET  /accountxyz (sem separador)              → 200 content-type=[text/html]
      GET  /usersxyz (sem separador)                → 200 content-type=[text/html]
      GET  /account/two-factor em navegação          → 200 content-type=[text/html]
      POST /auth/session (sem a verificação)        → 204; authorize → código de 43; token de 996; GET /agents → 200
      GET  /account/two-factor com o token          → {"ativa":false,"ativadaEm":null,"codigosRestantes":0,"cadastroDisponivel":false}
      POST /account/two-factor/setup com o token    → 403 "O cadastro da verificação em 2 etapas ainda não está disponível."
      ```

      **Cadastro aberto só neste stack** (override `cadastro-aberto.yml` com `Identity__TwoFactor__EnrollmentEnabled: "true"` no `api`; `13.3-fluxo-aberto.txt`), com o código calculado pela função `codigo()` do `docs/development.md`:
      ```
      setup com a senha             → segredo de 32 em base32; otpauth://totp/Buteco%20Agentes:admin%40stack218.local?secret=<segredo>&issuer=Buteco%20Agentes&algorithm=SHA1&digits=6&period=30
      confirm com o código          → 200, 10 códigos XXXX-XXXX-XXXX-XXXX; GET → {"ativa":true,"codigosRestantes":10,"cadastroDisponivel":true}
      POST /auth/session            → 200 {"segundoPasso":"totp"}
      authorize com o pendente      → 302 /entrar?retorno=%2Fconnect%2Fauthorize…
      second-factor com o código    → 204
      authorize com o completo      → 302 /entrar/retorno?code=…; token de 996; GET /agents → 200; GET /channels (apps/inbox) → 200
      o mesmo código de novo        → 401 "Código inválido."
      código de recuperação         → 204 (digitado em minúsculas e sem hífens); GET → codigosRestantes 9
      remove com outro de recuperação → 204; POST /auth/session → 204 de novo
      ```

- [x] 13.4 `openspec validate verificacao-2-etapas-totp --strict`.

      **Resultado, 01/11/2026:** `Change 'verificacao-2-etapas-totp' is valid`.

- [x] 13.5 `/opsx:sync` e `/opsx:archive` dentro do worktree, com o `02` e o `CHANGELOG` já escritos (12.6), e `openspec validate --all --strict` depois. Sem validação visual: nenhuma tela muda.

      **Resultado, 01/11/2026** (`218-fechamento/13.5-archive.log`): `openspec archive verificacao-2-etapas-totp -y` dentro do worktree, com o `02` e o `CHANGELOG` já escritos (12.6). As cinco deltas foram aplicadas: **+ 12, ~ 6, − 0**; a `two-factor-totp` foi criada, com o "Purpose" escrito depois (o archive o deixa TBD). A pasta saiu com a data UTC, `2026-11-02-verificacao-2-etapas-totp`. O aviso do archive ("mais de 10 deltas") é de contagem de requisitos, e as 4 tarefas abertas são as 13.6–13.8, que dependem do dono. `openspec validate --all --strict`: **82 passed, 0 failed**.

- [ ] 13.6 O único commit da change, depois do sync e do archive, com tudo junto (código, testes, docs e OpenSpec), só com o pedido do dono naquela mensagem e sem atribuição.
- [ ] 13.7 Push e PR com `Closes #218`, só com o ok do dono naquela mensagem; mover a #218 para `In review`.
- [ ] 13.8 Depois do merge: remover os dois worktrees, um de cada vez, e a branch local com `git branch -d`.
