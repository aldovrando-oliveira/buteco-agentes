Cada guarda marcado **(guarda)** só conta depois de visto reprovar contra o defeito que existe para pegar (convenção 15):

1. o defeito nomeado é reintroduzido pelo `~/.cache/buteco-agents/bin/guarda-211.sh` (cópia do `guarda-236.sh`), só no trecho nomeado;
2. o teste roda e reprova;
3. a árvore volta pela cópia feita antes da mutação, com o sha conferido.

Fica registrado na tarefa:

- o nome do teste;
- a mensagem da reprovação;
- o "voltou: <arquivo>", com o sha igual ao de antes.

Suítes pelo `~/.cache/buteco-agents/bin/suite-211.sh`, a `api` e, desde a 5.1b, a `frontend`, com a regra assimétrica: verde conta mesmo com a CPU caindo, e reprovação só conta com a CPU em 100 do início ao fim.

A baseline é `~/.cache/buteco-agents/211-baseline-914ca67/`, em `914ca67`: 1269 aprovados, 1 ignorado (`IdentityTokenPruneQueryPlanTests.FourQueries_With150kTokens_StayUnder100Ms`), com o relatório por nome em `nomes.txt` (1270 linhas).

**#176:** esta change não roda o `rt-inbox`. Se alguma rodada o incluir e o `Source3_…` reprovar, o caso é comentado na #176. Reprovação nos dois worktrees não bloqueia. Reprovação só no worktree da change: PARE.

**Parada:** quando uma premissa falsa mudar comportamento ou contrato, ou quando a correção reduzir cobertura. Correção que só toca comentário ou documentação segue e fica registrada. Nenhum caso existente é removido, nenhuma asserção é afrouxada e nenhum caso muda de nome.

## 1. Durações em constantes únicas (apps/api)

- [x] 1.1 (apps/api) `Identity/IdentityTokenLifetimes.cs` (design D6): `AccessToken` (15 min), `RefreshToken` (14 d), `IdentityToken` (20 min) e `AuthorizationCode` (5 min), com o comentário que cita a fonte dos dois padrões (`OpenIddictServerOptions.cs` l. 271 e 245, 7.7.2).
- [x] 1.2 (apps/api) `IdentityIssuerServiceCollectionExtensions`: `SetAccessTokenLifetime`, `SetRefreshTokenLifetime`, `SetIdentityTokenLifetime` e `SetAuthorizationCodeLifetime` lendo as constantes. `ServiceAccessTokenIssuer.Lifetime` passa a ser `IdentityTokenLifetimes.AccessToken`. O comentário de `IdentityKeyRotationPolicy.Retention` passa a citar `IdentityTokenLifetimes.RefreshToken`.
- [x] 1.3 (apps/api) `IdentityTokenLifetimesTests`: as `OpenIddictServerOptions` do host (`IOptionsMonitor<OpenIddictServerOptions>.CurrentValue`, com o anel carregado) têm as quatro durações iguais às constantes.
  **(guarda)** Defeito: `IdentityToken` = 21 min na constante, sem tocar o registro (o registro explícito retirado). Reprova o caso do id token.
  **(guarda)** Defeito: `SetAuthorizationCodeLifetime(TimeSpan.FromMinutes(6))` no registro, com a constante intacta. Reprova o caso do código.
  **Vistos reprovar** (`~/.cache/buteco-agents/211-guardas/`, `guarda-211-multi.sh`, 04/11/2026):
  - **Correção do registro (convenção 9):** o primeiro defeito, como estava escrito, não reprova: sem o registro explícito vale o padrão de 20 min, igual à constante. O defeito usado foi o literal num lado só, `SetIdentityTokenLifetime(TimeSpan.FromMinutes(21))` (g01) → `IdentityToken_IsTheConstant`, `Values differ`. Voltou: `IdentityIssuerServiceCollectionExtensions.cs` (sha igual ao de antes: `fb07c142…`).
  - g02 → `AuthorizationCode_IsTheConstant`, `Values differ`. Voltou: idem, sha igual.
- [x] 1.4 (apps/api) `ServiceAccessTokenIssuerTests` (l. 55): a asserção de 15 min fica, e entra ao lado a comparação de `ValidTo − IssuedAt` com o `AccessTokenLifetime` das opções do host.
  **(guarda)** Defeito: `ServiceAccessTokenIssuer.Lifetime = TimeSpan.FromMinutes(14)`, literal de volta. Reprovam as duas asserções.

  **Vistos reprovar** (`~/.cache/buteco-agents/211-guardas/`, `guarda-211-multi.sh`, 04/11/2026):
  - g03 → `ServiceAccessTokenIssuerTests.TheTokenGeneratedInProcess_HasTheSameShape_AsTheOneFromTheTokenEndpoint`, `Values differ`. Voltou: `ServiceAccessTokenIssuer.cs` (sha igual ao de antes: `e374e5b1…`).
## 2. Classificação pela política (apps/api)

- [x] 2.1 (apps/api) `IdentityKeyRotationPolicy` (D3): o record `IdentityKeyWindowsView` (`Kid`, `CreatedAt`, `SignsFrom`, `SignsUntil`, `ValidatesUntil`), os estados `em-uso`, `proxima` e `aposentando` e o `Describe(keys, now)`, com a escolha da `em-uso` igual à do `IdentityKeyRingSnapshot.Build` e a ordem da D2.
- [x] 2.2 (apps/api) `IdentityKeyRotationPolicyTests`, casos novos:
  - os três estados;
  - as três bordas (`== SignsFrom`, `== SignsUntil`, `== ValidatesUntil`);
  - a vencida fica fora;
  - a ordem;
  - a `em-uso` é a do `IdentityKeyRingSnapshot.Build` para o mesmo conjunto, com chaves de material real da `IdentityKeyFactory`.

  **(guarda)** Defeito: a `em-uso` escolhida pelo **menor** `SignsFrom`, num conjunto com duas `SignsAt` sobrepostas. Reprova o caso do snapshot.
  **(guarda)** Defeito: o filtro de `ValidatesUntil > now` retirado. Reprova o caso da vencida e o da borda de `ValidatesUntil`.
  **(guarda)** Defeito: `SignsFrom >= now` na condição da `proxima`. Reprova a borda de `SignsFrom`.
  **Vistos reprovar** (`~/.cache/buteco-agents/211-guardas/`, `guarda-211-multi.sh`, 04/11/2026):
  - g04 (menor `SignsFrom`) → `Describe_InUse_IsTheKeyTheRingSnapshotSignsWith`, `Strings differ`;
  - g05 (sem o filtro) → `Describe_LeavesOutTheKeyThatNoLongerValidates`, `Describe_AtTheExactBorders_FollowsTheRingOperators` e, na integração, `InUseNextAndRetiring_…` e `EncryptionKeyAndExpiredKey_StayOut`;
  - g06: **correção do registro.** A `Describe` usa o mesmo predicado do anel (`Signs`, que a implementação extraiu para as duas contas serem uma), então o defeito foi `signsFrom <= now` → `signsFrom < now` no predicado. Reprovam o `Describe_AtTheExactBorders_…` e mais 23 casos: a fixture parte com `now == signs_from`, e o anel deixa de achar quem assina. Era esperado de um predicado compartilhado.
  - Todos voltaram: `IdentityKeyRotationPolicy.cs` (sha igual ao de antes: `ed8c562c…`).
- [x] 2.3 (apps/api) `IdentityKeyRotationPolicyTests`: `Plan` com a ativa a `PrePublication − 1 s` do fim cria a próxima, e com `PrePublication + 1 s` não cria.
  **(guarda)** Defeito: o `Plan` compara com `TimeSpan.FromDays(6)` literal em vez de `PrePublication`. Reprova o primeiro caso.

  **Vistos reprovar** (`~/.cache/buteco-agents/211-guardas/`, `guarda-211-multi.sh`, 04/11/2026):
  - g07 → `Plan_CreatesTheNextKey_ExactlyAtThePrePublicationThreshold` (`Assert.NotNull() Failure`) e mais três casos existentes da política. Voltou: `IdentityKeyRotationPolicy.cs` (sha igual).
## 3. Leitura no anel, sem material (apps/api)

- [x] 3.1 (apps/api) `IdentityKeyRing.ListSigningKeysAsync` (D4): `AsNoTracking`, `Usage == Signing`, projeção direta para `IdentityKeyWindowsView` (o SQL não seleciona a coluna de material), sem lock e sem cache.
- [x] 3.2 (apps/api) Teste do SQL emitido (`EmittedSqlCapture`): a consulta da `ListSigningKeysAsync` não cita a coluna de material.
  **(guarda)** Defeito: `ToListAsync()` da entidade inteira, com a projeção feita em memória. Reprova.

  O caso ficou na classe `IdentityOverviewEndpointTests` (`ListSigningKeys_DoesNotSelectTheMaterialColumn`), que já tem o `SqlCapture` da fixture.
  **Vistos reprovar** (`~/.cache/buteco-agents/211-guardas/`, `guarda-211-multi.sh`, 04/11/2026):
  - g08 → `ListSigningKeys_DoesNotSelectTheMaterialColumn`, `Sub-string found`. Voltou: `IdentityKeyRing.cs` (sha igual ao de antes: `a6a90dd3…`).
## 4. A rota `GET /identity` (apps/api)

- [x] 4.1 (apps/api) `Identity/Overview/IdentityOverviewResponse.cs`: os records da D2, com `[JsonPropertyName]` em todo campo, e as durações em segundos inteiros.
- [x] 4.2 (apps/api) `Identity/Overview/IdentityOverviewEndpoint.cs`:
  - `MapGet("/identity")`, com a `CurrentUser` na entrada (`403` pela `CurrentUser.Forbidden()`);
  - os endereços pelo `IdentityIssuerState.Issuer` (com guarda, D5) e pelos relativos de `IdentityIssuerEndpoints`;
  - as durações de `IdentityTokenLifetimes` e da `IdentityKeyRotationPolicy` (`verificacaoSegundos` do `CheckInterval`, Q5 aceita);
  - as chaves pela `ListSigningKeysAsync` e pela `Describe`, no `TimeProvider` da requisição;
  - o mapeamento no `Program.cs`, ao lado das rotas de Usuários.
- [x] 4.3 (apps/api) `IdentityOverviewEndpointTests`, numa `LocalUsersFixture` própria da classe, com o usuário do fluxo real (`SignedInUser`). Acesso:
  - `200` para o usuário ativo;
  - `401` sem token;
  - `403` com `service:inbox` e com `service:connectors` (`TestIssuerTokens.ForSubject`);
  - `403` com o `TestIssuerTokens.Panel` (sem usuário), com o título da `CurrentUser`;
  - `403` para o usuário desativado com o token de antes.

  **(guarda)** Defeito: a checagem da `CurrentUser` retirada. Reprovam os casos do painel sem usuário e do desativado.
  **Vistos reprovar** (`~/.cache/buteco-agents/211-guardas/`, `guarda-211-multi.sh`, 04/11/2026):
  - g09 → `PanelTokenWithoutUser_Is403_FromCurrentUser` e `DeactivatedUser_WithTheTokenFromBefore_Is403`, `Values differ`. Voltou: `IdentityOverviewEndpoint.cs` (sha igual ao de antes: `950ced0a…`).
- [x] 4.4 (apps/api) `IdentityOverviewEndpointTests`, endereços: os quatro iguais aos do discovery buscado na mesma fixture, e com outro `Host` a resposta não contém o host da requisição.
  **(guarda)** Defeito: os endereços montados do `Request.Scheme` e do `Request.Host`. Reprova o caso do `Host`.
  **Vistos reprovar** (`~/.cache/buteco-agents/211-guardas/`, `guarda-211-multi.sh`, 04/11/2026):
  - g10 → `AnotherHost_DoesNotChangeTheAddresses` (`Sub-string found`) e `Addresses_AreTheOnesTheDiscoveryAnnounces` (`Strings differ`). Voltou: idem, sha igual.
- [x] 4.5 (apps/api) `IdentityOverviewEndpointTests`, durações (guarda do dono, D6): `validadeDoTokenSegundos` e `sessaoSemAtividadeSegundos` iguais ao `AccessTokenLifetime` e ao `RefreshTokenLifetime` das opções do host. Na chave `em-uso`, `assinaAte − assinaDesde` = `intervaloDeTrocaSegundos` e `saiDaPublicacaoEm − assinaAte` = `retencaoSegundos`.
  **(guarda)** Defeito: `SetAccessTokenLifetime(TimeSpan.FromMinutes(16))` no registro, com a constante intacta (constante trocada num lado só). Reprova.
  **(guarda)** Defeito: a rota escreve `validadeDoTokenSegundos = 900` literal e a constante vai a 16 min. Reprova.
  **(guarda)** Defeito: `RotationInterval` = 91 d só na rota (literal). Reprova o caso das janelas.
  `verificacaoSegundos` igual ao `CheckInterval` do `IdentityKeyRotationSchedule` do host (Q5).
  **(guarda)** Defeito: a rota escreve `verificacaoSegundos = 1800` literal. Reprova o caso da verificação.
  **Vistos reprovar** (`~/.cache/buteco-agents/211-guardas/`, `guarda-211-multi.sh`, 04/11/2026):
  - g11 (registro com 16 min) → `TokenDurations_AreTheOpenIddictOptionsOfTheHost`, `AccessToken_IsTheConstant` e o do token de serviço;
  - g12: **correção do registro.** "900 literal na rota e constante a 16 min" muda dois arquivos para o mesmo efeito de um. O defeito foi a rota com `TimeSpan.FromMinutes(10)` (o valor do protótipo antigo), e a constante intacta → `TokenDurations_…`, `Values differ`;
  - g13 → `RotationDurations_AreTheWindowsTheRotationWrote`;
  - g14 → `CheckInterval_IsTheTimerOfTheRotation`;
  - todos com `Values differ`, e todos voltaram com o sha igual (`fb07c142…` e `950ced0a…`).
- [x] 4.6 (apps/api) `IdentityOverviewEndpointTests`, chaves:
  - banco recém-subido: uma `em-uso`, `criadaEm` igual à gravada;
  - com uma `proxima` e uma `aposentando` gravadas pela `IdentityKeyFactory` (material real) ao lado da ativa: os três estados na ordem, com as datas iguais às colunas;
  - a chave de cifra fica fora.

  **(guarda)** Defeito: a rota ordena por `assinaDesde` decrescente sem o estado (`proxima` em cima). Reprova o caso dos três estados.
  **Vistos reprovar** (`~/.cache/buteco-agents/211-guardas/`, `guarda-211-multi.sh`, 04/11/2026):
  - g15 → `Describe_ListsInUseNextAndRetiring_InTheOrderOfTheIdentityBoard` e `InUseNextAndRetiring_InTheOrderOfTheBoard_WithTheStoredDates`, `Collections differ`. Voltou: `IdentityKeyRotationPolicy.cs` (sha igual).
- [x] 4.7 (apps/api) `IdentityOverviewEndpointTests`, material e nomes:
  - o JSON não tem nenhuma propriedade `d`, `p`, `q`, `dp`, `dq`, `qi`, `n`, `e`, `k` nem `x5c`;
  - o texto não contém o material cifrado de nenhuma linha de `identity_keys` (lido do banco, em base64 e em hex);
  - as propriedades de cada chave são exatamente as seis, e os nomes dos dois níveis são exatamente os da spec, conferidos no texto.

  **(guarda)** Defeito: o record da chave ganha `ProtectedMaterial` (a `ListSigningKeysAsync` devolvendo também a coluna). Reprovam o caso do material cifrado e o das seis propriedades.
  **(guarda)** Defeito: `[JsonPropertyName("saiDaPublicacaoEm")]` → `"saiDaPublicacao"`. Reprova o caso dos nomes.

  **Correção do registro:** o caso do material foi dividido em dois. Junto, ele reprovava na checagem das seis propriedades antes de chegar à busca do material cifrado. Ficaram `NoKeyParameterName_AndExactlyTheSixKeyProperties` e `NoEncryptedMaterialOfAnyRow_InTheResponse`.
  **Vistos reprovar** (`~/.cache/buteco-agents/211-guardas/`, `guarda-211-multi.sh`, 04/11/2026):
  - g16b, em três arquivos (a view com `ProtectedMaterial`, a projeção lendo a coluna e o record expondo `protectedMaterial`) → `NoEncryptedMaterialOfAnyRow_InTheResponse` (`DoesNotContain() Failure: Sub-string found`), `NoKeyParameterName_…`, `WireNames_…` e `ListSigningKeys_…`. Voltaram: `IdentityKeyRotationPolicy.cs` (`ed8c562c…`), `IdentityKeyRing.cs` (`a6a90dd3…`) e `IdentityOverviewResponse.cs` (`c9ad10d5…`), todos com o sha igual.
  - g17 → `WireNames_AreExactlyTheContract` e mais três. Voltou: `IdentityOverviewResponse.cs` (sha igual).
## 5. Documentação e issues

- [x] 5.1 (apps/api, docs) `docs/architecture.md`: `GET /identity` na lista de rotas do `apps/api`, com o acesso (usuário ativo; Admin na M2, #184), a ausência de material e o atraso de até `CheckInterval`.
- [x] 5.1b (apps/frontend, deploy) **Fora do plano, achado na implementação:** o `nginx.conf` do stack só manda ao `apps/api` os prefixos listados. Sem `identity`, o `GET /identity` cairia no fallback do SPA (`200` com HTML). `identity` entrou no bloco do `apps/api`, que tem o desvio de navegação, como a #179 e a #218 fizeram nas suas rotas. Não há rota `/identity` no SPA hoje. Premissa falsa ("só o `apps/api`") sem mudança de contrato nem de cobertura: segue registrada. A suíte `frontend` entra na medição, porque o `nginxHeaders.test.ts` lê o arquivo.
- [x] 5.2 (issues) Comentário na #184: a #211 criou `GET /identity`, aberto a todo usuário ativo na M1, e na M2 a rota passa a ser só de Admin. **Feito na proposta, 04/11/2026:** https://github.com/aldovrando-oliveira/buteco-agentes/issues/184#issuecomment-6648095901
- [x] 5.3 (docs) `02-HISTORICO_E_STATUS.md` e `CHANGELOG.md`, antes do archive. Entram:
  - a premissa "desligado" que caiu (K1);
  - as constantes (D6);
  - os números da medição e os guardas, por nome.
- [x] 5.4 (issues) No fechamento, o comentário de entrega na #183, com:
  - o contrato de `GET /identity`;
  - "o quadro Identidade foi corrigido em 04/11/2026";
  - o selo "Operando" fixo, porque o gate responde `503` antes;
  - a única divergência que resta é o selo "Operando" (design, K2).

  E o comentário de fechamento da #211, com o K1.
  **Feito, 05/11/2026:** https://github.com/aldovrando-oliveira/buteco-agentes/issues/183#issuecomment-6648947247 (cópia em `~/.cache/buteco-agents/211-verificacao/comentario-183.md`). O K1 vai no corpo do PR, que fecha a #211.

## 6. Medição de fechamento (apps/api)

- [x] 6.1 (apps/api, apps/frontend) **Resultado, 05/11/2026** (`211-fechamento/`): api 1.296 + 1 ignorado; os 1.270 nomes da baseline com o mesmo resultado, mais 27 novos. Frontend 1.978, os mesmos nomes e status da baseline (`211-baseline-914ca67/frontend.json`). Comando: `suite-211.sh <worktree da change> ~/.cache/buteco-agents/211-fechamento api`, e `nomes-trx.py` na saída. Por nome, os 1270 da baseline estão presentes com o mesmo resultado, mais os novos das tarefas 1 a 4, listados. Nenhum nome da baseline some ou muda.
- [x] 6.2 (apps/api) **Resultado:** APROVADA nas quatro rodadas, com o `CPU_Speed_Limit` mínimo em 23 e 46 (api) e em 34 e 62 (frontend). Veredito pela regra assimétrica. Reprovação inconclusiva se repete. Uma reprovação válida é investigada antes de qualquer outra tarefa.

## 7. Archive

- [x] 7.1 Archive com sync da delta `identity-issuer-overview`, e o `Purpose` da `identity-signing-keys` passa a apontar para ela. Antes do archive: 5.3 feita. Nenhum commit antes do archive. **Feito, 05/11/2026:** `openspec archive` com sync (`identity-issuer-overview`: + 6). O `Purpose` da nova spec foi escrito à mão no lugar do TBD do CLI, e o da `identity-signing-keys` passou a apontar para ela.
