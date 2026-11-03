**Base:** `origin/main` em `a4d3495` (merge do PR #230, #219), 02/11/2026.
- **Worktree da change:** `../buteco-agentes-181`, branch `feat/181-convites-redefinicao-senha`.
- **Baseline:** worktree limpo separado, `../buteco-agentes-181-baseline`, no mesmo commit, sem edição nenhuma.

**Saídas completas:** `.log` inteiro, `.trx` ou relatório JSON do Vitest, e estado da máquina colado ao número, nunca filtrados (convenção 19), em:
- `~/.cache/buteco-agents/181-baseline/`;
- `~/.cache/buteco-agents/181-verificacao/` (e `guardas/` dentro dela);
- `~/.cache/buteco-agents/181-fechamento/`.

**Variáveis em toda rodada .NET:** `DOCKER_HOST` do Podman, `TESTCONTAINERS_RYUK_DISABLED=true`, `DOTNET_SYSTEM_NET_DISABLEIPV6=1` e `TZ=America/Sao_Paulo`.

**Antes de cada suíte**, e não só da primeira, o script `~/.cache/buteco-agents/bin/suite-181.sh` confere:
- `CPU_Speed_Limit = 100`;
- loads de 1 e de 5 min abaixo de 6 no `apps/frontend` e de 8 nas outras;
- relógio da VM acertado;
- nenhum contêiner em `podman ps`.

As rodadas filtradas usam `~/.cache/buteco-agents/bin/dt-181.sh`, com a mesma espera.

**O que muda nesta change:** só o `apps/api` tem código:
- `Users/` (entidade, `UserStatus`, `AccessLinks/` nova, `Management/`, `Passwords/PasswordHasher.cs`);
- `Identity/PanelBaseUrl.cs` (nova) e `Identity/Clients/IdentityClientCatalog.cs`;
- `Infrastructure/AppDbContext.cs` e uma migration;
- `Program.cs`.

Também muda a documentação. Sem pacote e sem configuração. O código do `apps/frontend`, do `apps/inbox`, do `apps/connectors`, do `apps/workers` e de `tests/` não muda.

**Convenção 15 em todo guarda:** escrever o teste, reintroduzir o defeito nomeado na tarefa, ver reprovar **no componente que a correção toca**, guardar a saída em `181-verificacao/guardas/<tarefa>-reprova.log`, voltar a árvore e ver aprovar em `<tarefa>-aprova.log`. A tarefa só é marcada com as duas saídas registradas nela. **A volta reintroduz só o defeito nomeado**, nunca um diretório inteiro ao `HEAD`; o que voltou é listado na tarefa.

**As duas corridas** (6.4 e 6.7) e a do convite (5.3) seguem a quinta forma da convenção 15: o guarda comportamental (`Task.WhenAll`) pode passar por sorte com o defeito presente. Por isso, cada uma tem **também** uma asserção determinística sobre o SQL emitido na requisição real, capturado pelo `EmittedSqlCapture`, ou o interceptor determinístico da #219. Essa asserção tem de reprovar em toda execução com o defeito, verificada **rodando a classe inteira três vezes** com o defeito, e não o caso isolado.

**Baseline com duas falhas intermitentes** (#231 e #232): uma falha só conta como regressão desta change se cair num teste que passou nas duas rodadas da baseline (`181-baseline/api.trx` e `181-baseline/api-2a/api.trx`).

**Commit:** nenhum commit parcial, nem só da documentação ou dos artefatos do OpenSpec. O único commit da change acontece depois do sync e do archive (8.6), com tudo junto. Push e PR só com o ok do dono naquela mensagem.

**Paradas obrigatórias:**
- **grupo 2:** qualquer verificação que reprovar;
- **em qualquer grupo:** premissa do design que se mostrar falsa (convenção 9) **e** mudar comportamento ou contrato, ou cuja correção **reduza cobertura** (teste removido, asserção afrouxada, tolerância maior, cenário retirado).

Achado que só **acrescenta** teste ou cenário para pôr um guarda no lugar certo, ou que só toca comentário ou documentação, é resolvido, registrado na tarefa com o motivo e listado no fechamento, sem parar (critério do dono, 02/11/2026). Nos casos de parada, a decisão volta ao dono antes de seguir.

## 1. Baseline, antes de tocar código

- [x] 1.1 (`apps/api`, `apps/frontend`, `tests/InboxOrchestratorRoundTrip.Tests`, `tests/ApiConnectorsRoundTrip.Tests`) Baseline no worktree limpo em `a4d3495`.

      **Resultado, 02/11/2026** (design, seção Baseline): `apps/api` 1172 + 1 ignorado com 1 reprovado em cada uma das duas rodadas, num teste diferente a cada vez (`IdentityWarmupTests.TheCatalogFailsOnce_…`, #231; `KnowledgeBaseIndexingSummaryTests.SummaryQuery_CostDoesNotGrowWithNumberOfBases`, #232); `rt-inbox` 12/12; `rt-connectors` 8/8; `apps/frontend` 1978/1978, 154 arquivos. Por nome, igual ao fechamento da #219 menos as duas.

## 2. Verificação antes de qualquer código (parada obrigatória se reprovar)

- [x] 2.1 (`apps/frontend/deploy`, D3) **O formato de log do nginx do stack.** O `nginx.conf` do repositório não declara `log_format` nem `access_log`; a imagem do `Dockerfile` (`nginx:1.30-alpine`) traz `log_format main '… "$request" …'` e `access_log /var/log/nginx/access.log main`, com o `access.log` ligado a `/dev/stdout`.

      **Resultado, 02/11/2026:** confirmado com `podman run --rm docker.io/library/nginx:1.30-alpine sh -c 'grep -n "log_format\|access_log" /etc/nginx/nginx.conf; ls -l /var/log/nginx/'`. O `$request` leva caminho e query. Saída no design, D3.
- [x] 2.2 (`apps/api`, D3, D11) **O log de hosting do teste mostra o caminho.** Num host de teste com `Logging:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics = Information`, fazer `GET /users/qualquer-coisa?x=segredo` e conferir que uma linha capturada pelo logger de teste contém `/users/qualquer-coisa?x=segredo`. Sem isso, o `AccessLinkLogTests` (6.9) não tem artefato para reprovar contra a rota por `GET`: parar e propor outro meio.

      **Resultado, 02/11/2026: confirmado.** Teste temporário `Verificacao181Tests` (cópia em `181-verificacao/2.x-teste-temporario.cs`), apagado depois; rodada final `181-verificacao/2.x-b.log` (CPU 100, load 2,52 / 1,95 no início), 7 de 7 aprovadas. Com `logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Information)` no host (o filtro mais específico vence o `Microsoft.AspNetCore = Warning` do `appsettings.json`), um provedor que captura todas as categorias recebeu:
      - `Microsoft.AspNetCore.Hosting.Diagnostics|Request starting HTTP/1.1 GET http://localhost/users/qualquer-coisa?x=segredo - - -`;
      - `…|Request finished HTTP/1.1 GET http://localhost/users/qualquer-coisa?x=segredo - 404 - - 586.0712ms`.

      O `POST` registra só o caminho (`Request starting HTTP/1.1 POST http://localhost/auth/session - application/json;+charset=utf-8 97`): o corpo não entra.
- [x] 2.3 (`apps/api`, D6, D7) **O consumo é um `UPDATE` só, e a contagem volta.** Num teste temporário, um `ExecuteUpdateAsync` sobre `users` com `WHERE id AND access_link_hash AND access_link_purpose AND access_link_expires_at > @agora AND status`, que grava `password_hash`, `status` e apaga o link:
      - o `EmittedSqlCapture` registra **um** `UPDATE users … WHERE` com as cinco condições;
      - a contagem devolvida é 1 com o link válido e 0 com o hash trocado.

      Registrar o SQL emitido aqui. Se o EF partir em dois comandos ou não devolver a contagem, o uso único atômico não é como desenhado: parar.

      **Resultado, 02/11/2026: confirmado**, com as colunas que já existem no lugar das do link (a migration só vem no 3.2): `security_stamp` como o hash, `created_at` como o vencimento. Mesma forma de comando. O `EmittedSqlCapture` registrou um comando por chamada:

      ```
      UPDATE users AS u
      SET password_hash = @p,
          status = @p4,
          deactivated_at = NULL
      WHERE u.id = @user_Id AND u.security_stamp = @user_SecurityStamp AND u.created_at < @AddHours AND u.status = 'active'
      ```

      Contagem devolvida: **0** com o valor trocado e **1** com o certo (`miss=0 ok=1`).
- [x] 2.4 (`apps/api`, D5) **O cancelamento condicional não trava contra o aceite.** Num teste temporário com duas conexões: a primeira abre transação e faz o `UPDATE` do aceite sem confirmar; a segunda faz `DELETE FROM users WHERE id = @id AND status = 'invited'` e espera. Confirmada a primeira, o `DELETE` apaga 0 linhas, sem `40P01`. E o inverso: `DELETE` primeiro, o `UPDATE` acha 0 linhas. Se houver deadlock ou o `DELETE` apagar o aceito, o D5 não fecha: parar.

      **Resultado, 02/11/2026: confirmado**, com `deactivated` no papel de `invited` (o valor ainda não existe). Dois `AppDbContext`, cada um na sua conexão:
      - `UPDATE … WHERE id AND status = 'deactivated'` primeiro, sem confirmar: o `DELETE … WHERE id AND status = 'deactivated'` **ainda espera depois de 500 ms**; confirmado o `UPDATE`, o `DELETE` apaga **0** linhas;
      - o inverso: o `UPDATE` espera, e acha **0** linhas depois do `DELETE` confirmado.

      Nenhum `40P01`. A primeira tentativa abriu `NpgsqlConnection` com a connection string do `AppDbContext`, que vem sem a senha (`2.x.log`); não é achado do desenho.
- [x] 2.5 (`apps/api`, D2) **Conversor que lança na leitura.** Com uma conversão temporária que lança `InvalidOperationException` para texto desconhecido, gravar `status = 'qualquer'` por SQL direto e ler por `dbContext.Users`. Registrar o tipo da exceção que chega ao chamador (a do conversor, ou embrulhada pelo EF) e a mensagem, que tem de citar o valor. O `UserStatusConversionTests` (3.1) afirma esse tipo.

      **Resultado, 02/11/2026: confirmado.** Um `AppDbContext` derivado com a conversão que lança: a leitura por `dbContext.Users` de uma linha com `status = 'qualquer'` faz chegar ao chamador a **própria `System.InvalidOperationException` do conversor, sem embrulho** (`InnerException` nula), com a mensagem `Estado de usuário desconhecido: 'qualquer'.`

      **Achado de implementação (não muda o desenho):** a conversão do EF é árvore de expressão e não aceita `throw`; a leitura passa por um método estático (`UserStatusText.Read`), como fez o teste.
- [x] 2.6 (`apps/api`, D2) **O ponto de observação da verificação fictícia.** Conferir que o `PasswordHasher` pode expor a verificação contra o hash fictício como ponto sobrescrevível (classe deixa de ser `sealed`, `VerifyAgainstDummy` vira `virtual`) sem mudar o registro em DI nem os chamadores, e que um dublê de teste registrado no lugar dele no host de teste é o que o `POST /auth/session` recebe. Se o dublê não chegar ao endpoint, o guarda de integração do 6.10 não tem como observar o caminho: parar e propor outro meio (decisão do dono, 02/11/2026: o guarda confere o caminho, não o tempo).

      **Resultado, 02/11/2026: confirmado.** O `PasswordHasher` deixou de ser `sealed` e o `VerifyAgainstDummy` virou `virtual` (a mudança do 3.4 feita aqui, sem efeito de comportamento). Um `CountingHasher` registrado no lugar do `PasswordHasher` num host derivado (`WithWebHostBuilder`) é o que o `POST /auth/session` recebe: login de e-mail sem conta → `401` e **1** chamada contada.

      **Números para a conferência manual do 8.3** (não entram na suíte): no código de hoje, mediana de 20 `Verify(senha, null, false)` contra 20 `VerifyAgainstDummy`, três rodadas, CPU 100: **0,0006 ms contra 27,35 / 27,92 / 27,10 ms**. É o vazamento de tempo que o achado 1 corrige.
- [x] 2.7 (`apps/api`, D8) **Como o limite global já é testado.** Ler o teste do `LoginRateLimiting` existente e registrar como ele força a concorrência (quantas chamadas, como segura a verificação). O 6.8 usa o mesmo arranjo para o aceite. Sem arranjo reaproveitável, registrar o que será feito; não é parada.

      **Resultado, 02/11/2026:** `Users/LoginRateLimitTests.cs`, host próprio com `KeepsLoginRateLimit => true` (o `LocalUsersFixture` desliga o limite por padrão), dispara **60** `POST` ao mesmo tempo, acima da concorrência (4) com a fila (16) e da rajada (20), e afirma que alguns recebem `429` com `Retry-After: 1`. O 6.8 usa o mesmo arranjo: 60 aceites com tokens inexistentes (o limitador age antes do endpoint) → algum `429`; 60 consultas → nenhum.

## 3. Peças de base (`apps/api`)

- [x] 3.1 (`apps/api`, D2, achado 3) **Estado `invited` e conversão que lança.** `UserStatus.Invited`; a conversão do `AppDbContext` passa a mapear os três textos e a lançar, citando o valor, para qualquer outro.

      Teste `UserStatusConversionTests` (integração): `status = 'invited'` gravado por SQL é lido como `Invited`; `status = 'qualquer'` faz a leitura lançar a exceção registrada no 2.5, com o valor na mensagem.

      **Guarda contra** a conversão de hoje (`value == "active" ? Active : Deactivated`): os dois casos reprovam (`invited` volta `Deactivated`; o desconhecido não lança). `guardas/3.1-reprova.log`, `3.1-aprova.log`.

      **Resultado, 02/11/2026.** `UserStatus.Invited`; a conversão passa por `UserStatusText.Write`/`Read` (método estático, achado do 2.5), e `Read` lança `InvalidOperationException` citando o valor. `UserStatusConversionTests` 2/2. **Guarda:** com a conversão de antes, os dois casos reprovaram (`guardas/3.1-reprova.log`: `Invited_IsReadAsInvited` e `UnknownText_FailsTheRead_CitingTheValue`); a volta conferida byte a byte; `3.1-aprova.log` 2/2.
- [x] 3.2 (`apps/api`, D4) **Migration `AddUserAccessLinks`.** Colunas anuláveis em `users`: `invited_by` (`uuid`, FK para `users.id`), `access_link_hash` (`bytea`, índice único), `access_link_purpose` (`text`), `access_link_expires_at` (`timestamptz`). Conferir o `Up` e o `Down` gerados, e que o índice único aceita vários nulos (dois usuários sem link gravados no mesmo teste).

      **Resultado, 02/11/2026.** `dotnet ef migrations add AddUserAccessLinks`: `Up` acrescenta as quatro colunas anuláveis, `IX_users_access_link_hash` (único), `IX_users_invited_by` e a FK `FK_users_users_invited_by` (`Restrict`); `Down` desfaz na ordem inversa. `dotnet ef migrations has-pending-model-changes` → "No changes have been made to the model since the last migration." (`181-verificacao/3.2-pending.log`). Os vários nulos no índice único: toda classe de integração grava dezenas de usuários sem link, e passaram (3.1, 4.x).
      - O propósito é gravado como texto (`invitation`, `password_reset`) por `AccessLinkPurposeText`, que lança para texto desconhecido, como o estado.
- [x] 3.3 (`apps/api`, D4, D9) **`User`**: `CreateInvitation(email, nome, convidadoPor, agora)` (estado convidado, sem senha), `IssueAccessLink(propósito, hash, vence)`; `Deactivate` e `ChangePassword` apagam o link. Testes de unidade da entidade:
      - o convidado nasce sem senha, com `InvitedBy`, e não ativo;
      - `Deactivate` e `ChangePassword` deixam as três colunas do link nulas.

      **Guarda contra** `Deactivate` sem apagar o link (o caso reprova). `guardas/3.3-reprova.log`, `3.3-aprova.log`. O mesmo defeito volta a ser visto no caso de integração do 6.6.

      **Resultado, 02/11/2026.** `UserAccessLinkTests` 4/4 (unidade), com o caso de `ChangePassword` também. **Guarda:** `Deactivate` sem `ClearAccessLink()` → `Deactivate_ClearsThePendingLink` reprovou (`guardas/3.3-reprova.log`, 3 de 4); volta conferida; `3.3-aprova.log` 4/4.
- [x] 3.4 (`apps/api`, D2, achado 1) **`PasswordHasher.Verify` com hash nulo ou vazio chama a verificação contra o fictício** (`VerifyAgainstDummy`) antes de devolver `Failed`.

      Teste em `PasswordHasherTests`, com um dublê que conta as chamadas de `VerifyAgainstDummy` (o ponto do 2.6): hash nulo e hash vazio → `Failed`, e uma chamada à verificação fictícia em cada; hash válido com senha errada → `Failed` e nenhuma chamada.

      **Guarda contra** o `return Failed` imediato de hoje, como está em `main`: o contador fica em zero e o caso reprova. `guardas/3.4-reprova.log`, `3.4-aprova.log`. **Sem asserção de tempo na suíte** (decisão do dono, 02/11/2026: teste por tempo reprova sob carga, o problema da #226); a comparação de tempo é a conferência manual do 8.3.

      **Resultado, 02/11/2026.** `Verify` com hash nulo ou vazio chama `VerifyAgainstDummy` e devolve `Failed`. Dublê `Support/CountingPasswordHasher.cs`, compartilhado com o 6.10. `PasswordHasherTests` 19/19. **Guarda:** com o `return Failed` imediato, os dois casos da teoria (`null` e `""`) reprovaram com o contador em zero (`guardas/3.4-reprova.log`, 17 de 19). `3.4-aprova.log` 19/19.
      - **O caso `Verify_NullHash_Fails` foi absorvido** pela teoria `Verify_WithoutStoredHash_Fails_AfterTheDummyVerification`, que afirma o mesmo `Failed` para o nulo e acrescenta o vazio e o contador. Não reduz cobertura: registrado, sem parada.
      - **Achado do script de guarda, não do código:** a primeira volta automática recusou porque o texto do defeito (`{ return Failed; }`) aparece em outros ramos do arquivo; a volta foi feita à mão pelo comentário que identifica o ramo e conferida pelo `git diff`. O script passou a recusar, ao aplicar, defeito cujo texto apareça mais de uma vez.
- [x] 3.5 (`apps/api`, D4) **`AccessLinkToken`**: gerar (32 bytes de `RandomNumberGenerator`, base64url sem preenchimento), o SHA-256 dos bytes ASCII do token, e as validades (`InvitationLifetime` = 72 h, `PasswordResetLifetime` = 24 h) em constantes.

      Teste `AccessLinkTokenTests`: 43 caracteres sem `=`, `+` nem `/`; dois gerados diferem; o hash de um token é igual ao `SHA256.HashData(Encoding.ASCII.GetBytes(token))`; as duas validades.

      **Guarda contra** o hash calculado sobre o token decodificado (bytes do base64url) em vez do texto: o caso do hash reprova. `guardas/3.5-reprova.log`, `3.5-aprova.log`.

      **Resultado, 02/11/2026.** `AccessLinkTokenTests` 4/4. **Guarda:** o hash sobre os bytes decodificados do base64url → `TheHash_IsTheSha256OfTheTokenText` reprovou (`guardas/3.5-reprova.log`, 3 de 4); `3.5-aprova.log` 4/4.
- [x] 3.6 (`apps/api`, D4) **`PanelBaseUrl`** extraída de `IdentityClientCatalog.PanelBase()` para `Identity/PanelBaseUrl.cs`, e o catálogo passa a usá-la, sem mudar o resultado. **`AccessLinkUrls`**: `<base>/convite#<token>` e `<base>/redefinir-senha#<token>`.

      Teste `AccessLinkUrlsTests`: `PanelBaseUrl` presente → base dela; ausente → o issuer; com e sem barra no fim da base, uma barra só; o token só depois de `#`.

      **Guarda contra** o token no caminho (`/convite/<token>`): o caso do fragmento reprova. `guardas/3.6-reprova.log`, `3.6-aprova.log`. Os testes do catálogo de clientes que já existem continuam passando (a extração não muda os endereços de retorno).

      **Resultado, 02/11/2026.** `Identity/PanelBaseUrl.Resolve` (estática), usada pelo `IdentityClientCatalog`; `AccessLinkUrls.For(propósito, base, token)`. `AccessLinkUrlsTests` 6/6, e os 8 testes de `IdentityClient*` continuam aprovados no mesmo filtro. **Guarda:** o token no caminho (`/convite/<token>`) → os seis casos do `AccessLinkUrlsTests` reprovaram, entre eles `TheToken_GoesOnlyInTheFragment` (`guardas/3.6-reprova.log`, 8 de 14); `3.6-aprova.log` 14/14.
- [x] 3.7 (`apps/api`, D1, P10) **`CallerPassword`**: dado quem chama e a `senha` do corpo, devolve `423` com `Retry-After` durante o bloqueio do e-mail de quem chama, sem conferir; `401` "Senha inválida." com a falha contada no e-mail de quem chama para senha ausente ou errada; `null` para seguir. O acerto não zera a contagem. Coberta pelos casos de integração do 5.5.


      **Resultado, 02/11/2026.** `Users/AccessLinks/CallerPassword.RejectAsync`: confere como o login (inclusive o hash legado do bootstrap), conta a falha no e-mail de quem chama e responde `401` "Senha inválida."; o `423` fica com o `LoginThrottleResponses.LockedAsync`, que já existe. Coberta pelo 5.5.
## 4. Usuários: o convidado nas rotas da #219 (`apps/api`, D2, D10, `user-management`, `user-account`)

- [x] 4.1 (`apps/api`, achado 2) **`activate` e `deactivate` recusam o convidado** com `409` "Este usuário ainda não aceitou o convite.", sem gravar nada.

      Casos em `InvitationLifecycleTests`:
      - `activate` no convidado → `409`; o `estado` continua `convidado`, sem `password_hash`;
      - `deactivate` no convidado → `409`; o link continua valendo (a consulta → `200`).

      **Guardas contra** `activate` sem a recusa (sai `200` com `estado: ativo`; `guardas/4.1-activate-reprova.log`) e `deactivate` sem a recusa (sai `200` e o convidado vira `desativado`; `guardas/4.1-deactivate-reprova.log`), um de cada vez. `4.1-aprova.log`.

      **Resultado, 02/11/2026.** `NotYetAccepted()` → `409` "Este usuário ainda não aceitou o convite." no `activate` e, dentro do lock, no `deactivate`. `InvitationLifecycleTests` 2/2, com convidados semeados pela entidade (`Support/InvitationSeed.cs`), porque as rotas de convite vêm no grupo 5. **"O link continua valendo"** é afirmado aqui pelo hash do link inalterado no banco; a consulta (`200`) entra quando a rota pública existir (6.2). **Guardas, um de cada vez:** `activate` sem a recusa → `Activate_OnTheInvited_…` reprovou (`guardas/4.1-activate-reprova.log`); `deactivate` sem a recusa → `Deactivate_OnTheInvited_…` reprovou (`guardas/4.1-deactivate-reprova.log`); `4.1-aprova.log` 2/2.
- [x] 4.2 (`apps/api`) **Lista e detalhe**: `estado` `convidado`; `convite: { expiraEm, vencido }` só no convidado, `vencido` pelo `TimeProvider`; `convidadoPor: { id, nome } | null` no detalhe; para o convidado, `formasDeEntrada: []`, `duasEtapas: false`, `ultimoAcesso: null`.

      Casos em `UserListTests` e num caso novo de detalhe:
      - um convite dentro da validade e um vencido (`FakeTimeProvider` + 72 h) → `vencido` falso e verdadeiro;
      - o aceito traz `convidadoPor` com o nome de quem convidou; o bootstrap traz nulo.

      **Guarda contra** `vencido` calculado como `expiraEm < criadoEm + validade` (sem o relógio): o caso do vencido reprova. `guardas/4.2-reprova.log`, `4.2-aprova.log`.

      **Resultado, 02/11/2026.** `UserSummary` ganha `Convite` (`InvitationSummary(ExpiraEm, Vencido)`, `vencido` = `expiraEm <= agora` pelo `TimeProvider`); `UserDetail` ganha `Convite` e `ConvidadoPor` (`UserReference(Id, Nome)`, uma leitura a mais no detalhe, só quando há quem convidou). Classe nova `Users/Management/UserListInvitationTests` (host próprio, porque avança o relógio) 3/3: pendente, detalhe com `convidadoPor` e vencido. **Guarda:** `vencido` = `expiraEm < criadoEm + validade` → `AnExpiredInvitation_StaysListed_AsExpired` reprovou (`guardas/4.2-reprova.log`, 19 de 20 no filtro com `UserListTests`, `UserDetailTests`, `UserActivationTests` e `UserDeactivationRoutesTests`, que continuam verdes); `4.2-aprova.log` 20/20.
      - "O aceito traz `convidadoPor`" é afirmado no 6.2, quando o aceite existir.
- [x] 4.3 (`apps/api`, P6) **Trocar a própria senha invalida o link de redefinição.** Caso em `PasswordResetTests` (6.6): gerar o link, trocar a própria senha por `POST /account/password`, e a consulta e a conclusão → `404`.

      **Guarda contra** `ChangePassword` sem apagar o link: o caso reprova no componente da entidade. `guardas/4.3-reprova.log`, `4.3-aprova.log`.


      **Resultado, 02/11/2026.** `PasswordResetTests.ChangingTheOwnPassword_InvalidatesThePendingLink`: depois da troca, a consulta e a conclusão → `404`. **Guarda:** `ChangePassword` sem apagar o link → o caso reprovou (`guardas/4.3-reprova.log`, 10 de 11) e, na entidade, `UserAccessLinkTests.ChangePassword_ClearsThePendingLink` (`4.3-entidade-reprova.log`, 3 de 4; `4.3-entidade-aprova.log` 4/4). A aprovação da classe é a do 6.6 (`6.6-stamp-aprova.log`).
## 5. Rotas administrativas (`apps/api`, D1, D5, D7, D10, `user-access-links`)

- [x] 5.1 (`apps/api`) **`POST /users/invitations`** com `{ nome, email, senha }`, na ordem do D1: `CurrentUser` → bloqueio → validação → `409` → senha → grava. `201 { usuario, link, expiraEm }`.

      Casos em `InvitationCreateTests`:
      - `201` com o link na base configurada, token depois de `#`, `expiraEm` = agora + 72 h;
      - o convidado na lista com `estado: convidado`;
      - **nenhuma coluna de `users` contém o token** (busca do texto do token em todas as colunas da linha); `access_link_hash` = SHA-256 dele;
      - e-mail de ativo, de desativado e de convidado, em outra caixa → `409`;
      - `Host: evil.example` → o link continua na base configurada.

      **Guarda contra** a base lida de `Request.Host`: o caso do `Host` reprova. `guardas/5.1-reprova.log`, `5.1-aprova.log`.

      **Resultado, 02/11/2026.** `Users/AccessLinks/InvitationEndpoints.cs` (`CreateAsync`), na ordem do D1; `201` com `Location: /users/{id}`. A validação de nome e e-mail e o `409` do índice saíram de `UserEndpoints` para `Users/Management/UserFields.cs` (dois consumidores: o `PUT` da #219 e o convite), sem mudar mensagem nem status; as classes da #219 continuam verdes (4.2, 8.1). `InvitationCreateTests` 8/8 (`181-verificacao/5.1-primeira.log`). **Guarda:** a base lida de `Request.Scheme`/`Request.Host` → `AForgedHost_DoesNotChangeTheLink` e `Invite_Returns201_…` reprovaram (`guardas/5.1-reprova.log`, 6 de 8).
      - "Nenhuma coluna contém o token": `row_to_json` da linha inteira, sem o texto do token.
- [x] 5.2 (`apps/api`) **`POST /users/{id}/invitation/renew`** (`{ senha }`) e **`POST /users/{id}/invitation/cancel`** (sem corpo; `DELETE … WHERE id AND status = 'invited'`).

      Casos em `InvitationLifecycleTests`:
      - renovar o vencido → `200`, o antigo → `404` e o novo → `200` na consulta;
      - renovar o ativo → `409`;
      - cancelar → `204`, a linha some, o link → `404`, e um convite novo para o mesmo e-mail → `201`;
      - cancelar o aceito → `409`, e ele continua ativo.

      **Guarda contra** o `DELETE` sem `status = 'invited'` no `WHERE`: cancelar o aceito apaga o usuário ativo e o caso reprova. `guardas/5.2-reprova.log`, `5.2-aprova.log`.

      **Resultado, 02/11/2026.** Renovar por `ExecuteUpdate` condicionado a `status = invited` (um aceite no meio faz responder `409`, sem gravar link num ativo); cancelar por `ExecuteDelete` condicionado ao mesmo estado, e `404`/`409` depois, pela existência. `InvitationLifecycleTests` 7/7 (com os dois do 4.1). **Guarda:** o `DELETE` sem `status = 'invited'` → `CancellingAnAcceptedInvitation_…` reprovou (`guardas/5.2-reprova.log`, 6 de 7); `5.2-aprova.log` 7/7.
      - **"O antigo → 404 e o novo → 200 na consulta"** e **"o link → 404" depois de cancelar** dependem da rota pública: aqui se afirma o hash novo no banco e a linha apagada, e a consulta entra no 6.5.
      - O convite vencido é semeado com o vencimento no passado (`InvitationSeed`, parâmetro novo), sem avançar o relógio da classe.
- [x] 5.3 (`apps/api`) **Corrida de dois convites para o mesmo e-mail**: um `201` e um `409`, nunca `500` (20 rodadas de `Task.WhenAll`), mais o determinístico com o `DbCommandInterceptor` da #219 segurando o `INSERT` de um depois da pré-checagem.

      **Guarda contra** o `catch` do `23505` retirado: o determinístico responde `500` em toda execução (classe inteira três vezes, `guardas/5.3-reprova-{1,2,3}.log`); o comportamental registra quantas das 20 rodadas reproduziram. `5.3-aprova.log`.

      **Resultado, 02/11/2026.** Comportamental: 20 de 20 rodadas com um `201` e um `409`, e **as 20 chegaram ao índice** (o `INSERT` recusado no log): a conferência da senha (Argon2) entre a pré-checagem e a escrita alarga a janela. Determinístico: `RivalInsertsFirstInterceptor` grava o rival em autocommit no `SavingChanges` do convite. **Guarda:** o `catch` do `23505` retirado → os dois casos reprovaram nas **três** execuções da classe inteira (`guardas/5.3-reprova-{1,2,3}.log`, 6 de 8 em cada); `5.3-aprova.log` 8/8.
- [x] 5.4 (`apps/api`, D7) **`POST /users/{id}/password-reset`** (`{ senha }`): só no ativo; a si mesmo → `403` antes de conferir a senha; convidado e desativado → `409`; não toca senha, stamp nem sessões.

      Casos em `PasswordResetTests`:
      - `200` com link de 24 h; **o login com a senha atual continua `204` e as sessões continuam renovando**;
      - a si mesmo → `403`, e nenhuma falha é contada no bloqueio;
      - convidado e desativado → `409`.

      **Guarda contra** a recusa de si mesmo retirada: o caso reprova (sai `200` com link). `guardas/5.4-reprova.log`, `5.4-aprova.log`.

      **Resultado, 02/11/2026.** `Users/AccessLinks/PasswordResetEndpoints.cs`; o `UPDATE` do link condicionado a `status = active`, para uma desativação no meio não deixar link num desativado que a reativação traria de volta. Convidado → `409` "Este usuário ainda não aceitou o convite."; desativado → `409` "Usuário desativado.". `PasswordResetTests` 4/4. **Guarda:** a recusa de si mesmo retirada → `ForOneself_Is403_AndNothingIsCounted` reprovou (`guardas/5.4-reprova.log`, 3 de 4). **Registro:** o caso manda a senha errada de propósito, então com o defeito a resposta foi `401` (e a falha contada), e não `200` com link como a tarefa previa; reprova do mesmo jeito e no mesmo ponto (o `403` antes de conferir a senha). `5.4-aprova.log` 4/4.
- [x] 5.5 (`apps/api`, P10) **A senha de quem chama nas três rotas.** `AccessLinkCallerPasswordTests`:
      - criar convite, renovar e gerar redefinição **sem `senha` e com a errada** → `401` "Senha inválida.", e nada gravado: nenhum usuário novo, o hash do link do convite igual ao de antes, nenhum link de redefinição no alvo;
      - a falha conta no bloqueio **do e-mail de quem chama**: a linha de `identity_login_throttle` de quem chama tem a contagem, e a do alvo não existe;
      - cinco falhas e a sexta com a senha certa → `423` com `Retry-After`, sem gravar;
      - senha certa → `201`/`200`;
      - **cancelar sem `senha` → `204`**.

      **Guardas, um de cada vez:**
      - a conferência da senha retirada de **criar** → o caso "senha errada → `401`" de criar reprova (sai `201` com link). `guardas/5.5-criar-reprova.log`;
      - retirada de **renovar** → o caso de renovar reprova (sai `200`, e o hash muda). `guardas/5.5-renovar-reprova.log`;
      - retirada de **gerar redefinição** → o caso de redefinir reprova (sai `200` com link). `guardas/5.5-redefinir-reprova.log`;
      - a falha contada no e-mail do **alvo** em vez do de quem chama → o caso da linha do bloqueio reprova. `guardas/5.5-alvo-reprova.log`.

      `5.5-aprova.log`.

      **Resultado, 02/11/2026.** `AccessLinkCallerPasswordTests` 6/6. **Guardas, um de cada vez** (`--no-approve`, e a aprovação depois): conferência retirada de criar → 3 reprovados, entre eles os dois da teoria e o do `423` (`guardas/5.5-criar-reprova.log`); de renovar → os dois da teoria (`5.5-renovar-reprova.log`); de redefinir → os dois da teoria e o da contagem (`5.5-redefinir-reprova.log`); falha gravada fora do e-mail de quem chama → `TheFailure_CountsOnTheCallersEmail_…` e o do `423` (`5.5-alvo-reprova.log`). `5.5-aprova.log` 6/6.
      - **Registro:** o defeito "contada no alvo" foi reintroduzido como "contada num e-mail que não é o de quem chama", porque o `CallerPassword` não recebe o alvo; o caso afirma as duas linhas (a de quem chama com 1, a do alvo ausente).
- [x] 5.6 (`apps/api`, D1) **Quem chama.** `AccessLinkCallerTests`: HMAC `operator` → `403` nas quatro administrativas, e nada é gravado (nenhum usuário, nenhum link, nenhuma linha de bloqueio); usuário desativado com access token válido → `403`.

      **Guarda contra** uma rota sem o `CurrentUser` (criar convite): o caso do HMAC reprova. `guardas/5.6-reprova.log`, `5.6-aprova.log`.


      **Resultado, 02/11/2026.** `AccessLinkCallerTests` 2/2: HMAC `operator` → `403` nas quatro, nenhum usuário, link ou linha de bloqueio gravados; desativado com access token válido → `403`. **Guarda:** criar convite sem o `CurrentUser` (quem chama lido como o primeiro usuário do banco) → os dois casos reprovaram (`guardas/5.6-reprova.log`); `5.6-aprova.log` 2/2.
## 6. Rotas públicas (`apps/api`, D3, D6–D8, `user-access-links`, `local-users`)

- [x] 6.1 (`apps/api`) **As quatro rotas sob `/auth`**, anônimas (`AnonymousRouteClassification(AuthEntryPoint)`), com `SessionRequestGuards` antes de tudo; aceite e conclusão com `LoginRateLimiting.Marker`, as consultas sem. Conferir que a checagem de boot das rotas anônimas aceita as quatro e que nenhuma outra classificação muda.

      **Resultado, 02/11/2026.** `AccessLinkPublicEndpoints`: as quatro rotas `POST` com `AllowAnonymous` e `AnonymousRouteClassification(AuthEntryPoint)`, `SessionRequestGuards` antes da leitura do corpo, `LoginRateLimiting.Marker` só no aceite e na conclusão. As quatro entram na lista do `ValidateRouteAuthenticationClassification` no `Program.cs`; nenhuma outra classificação mudou, e todo host de teste sobe com a checagem (as classes do 6.x rodam nele). As barreiras e o limite são afirmados no 6.8.
- [x] 6.2 (`apps/api`, D6) **Consulta e aceite do convite.** `InvitationAcceptTests`:
      - consulta → `200 { nome, email, convidadoPor, expiraEm }`, sem gravar nada;
      - aceite com senha de 11 → `400`, e o link continua valendo;
      - aceite certo → `204` **sem `Set-Cookie`**; `estado: ativo`; **o login com o e-mail e a senha nova → `204`, e o authorize emite código**;
      - o mesmo link de novo → `404`.

      **Guarda contra** `status = 'invited'` fora do `WHERE` do consumo, com o link regravado à mão depois do primeiro aceite: o segundo aceite troca a senha do ativo, e o caso reprova. `guardas/6.2-reprova.log`, `6.2-aprova.log`.

      **Resultado, 02/11/2026.** `InvitationAcceptTests` 9/9 (com os casos do 6.3 ao 6.5 e os de consulta do 4.1 e do 5.2): `TheLookup_ShowsNameEmailInviterAndExpiry_AndWritesNothing`, `APasswordOutsideThePolicy_Is400_AndTheLinkStaysValid`, `Accepting_Is204_WithoutASession_AndTheLoginThenOpensOne` (login `204` e authorize com código; o detalhe traz `convidadoPor`, o que fechou a pendência do 4.2), `ALinkRewrittenOnAnActive_IsNotAcceptedAgain`. **Guarda:** `status = 'invited'` fora do `WHERE` do consumo → `ALinkRewrittenOnAnActive_IsNotAcceptedAgain` e o do SQL do 6.4 reprovaram (`guardas/6.2-reprova.log`, 7 de 9). Aprovação da classe: `6.5-aprova.log` 9/9.
- [x] 6.3 (`apps/api`, P8) **O aceite zera o bloqueio.** Cinco falhas de login no e-mail do convidado, aceite, e o login com a senha nova → `204`.

      **Guarda contra** o `ResetAsync` retirado do aceite: o login → `423` e o caso reprova. `guardas/6.3-reprova.log`, `6.3-aprova.log`.

      **Resultado, 02/11/2026.** `InvitationAcceptTests.Accepting_ClearsTheEmailLock`. **Guarda:** o `ResetAsync` retirado do aceite → o caso reprovou com `423` (`guardas/6.3-reprova.log`, 8 de 9). Aprovação: `6.5-aprova.log`.
- [x] 6.4 (`apps/api`, D6) **Dois aceites concorrentes**: um `204` e um `404`, e só a senha do bem-sucedido entra (20 rodadas). **E o SQL emitido do aceite**: um único `UPDATE users` com `access_link_hash`, `access_link_purpose`, `access_link_expires_at` e `status` no mesmo `WHERE` (capturado pelo `EmittedSqlCapture` na requisição real).

      **Guarda contra** o consumo em dois passos (ler o link e depois gravar pela entidade): a asserção do SQL reprova em toda execução (classe inteira três vezes, `guardas/6.4-reprova-{1,2,3}.log`); o comportamental registra quantas rodadas reproduziram. `6.4-aprova.log`.

      **Resultado, 02/11/2026.** `InvitationAcceptTests.TwoConcurrentAccepts_OneWins_AndTheConsumptionIsOneConditionalUpdate`: com o código certo, 0 de 20 rodadas com os dois aceites aprovados, em todas as execuções; o SQL capturado traz os quatro fragmentos no `WHERE` de cada `UPDATE` de consumo. **Guarda (consumo em dois passos), classe inteira três vezes:** reprovou nas três (`guardas/6.4-reprova-{1,2,3}.log`, 8 de 9 cada). **Divergência registrada:** nas três foi a parte comportamental que reprovou primeiro (`[NoContent, NoContent]`), então a asserção do SQL não chegou a ser exercida sob o defeito; a corrida se reproduziu em toda execução, mas a garantia determinística da quinta forma vem do 2.3, onde a contagem do `UPDATE` condicional foi conferida. Aprovação: `6.5-aprova.log`. **Asserção do SQL isolada (02/11/2026, a pedido no fechamento):** um caso temporário `InvitationAcceptTests.SqlIsolada_Aceite` com um pedido só, sem corrida e sem a parte comportamental, e a mesma asserção do SQL; sem o defeito aprovou (`guardas/6.4-sql-isolada-aprova.log`, 1/1), com o consumo em dois passos reprovou pela asserção (`Assert.Contains`: `"WHERE id = @p5;"` sem `access_link_hash = @`, `guardas/6.4-sql-isolada-reprova.log`). O caso saiu e o arquivo voltou byte a byte; o teste da corrida ficou como estava.
- [x] 6.5 (`apps/api`, D4) **Vencimento e substituição.** Convite vencido (`FakeTimeProvider` + 72 h) → `404` na consulta e no aceite; redefinição vencida (+ 24 h) → `404`; depois de renovar, o antigo → `404`; cancelado → `404`.

      **Guarda contra** a conferência de `access_link_expires_at` retirada da consulta e do consumo: os casos de vencido reprovam. `guardas/6.5-reprova.log`, `6.5-aprova.log`.

      **Resultado, 02/11/2026.** `InvitationAcceptTests.AnExpiredInvitation_IsAnInvalidLink` (vencido por `FakeTimeProvider` + 72 h → `404` na consulta e no aceite) e `AfterRenewing_TheOldLinkIsInvalid_AndTheNewOneWorks_AndCancellingInvalidates`. **Guarda:** o vencimento fora da consulta e do consumo → o caso do vencido e o do SQL reprovaram (`guardas/6.5-reprova.log`, 7 de 9); `6.5-aprova.log` 9/9.
      - **Cenário acrescentado no fechamento:** a redefinição vencida não tinha caso. Entrou no `AccessLinkPublicRoutesTests.EveryInvalidLink_GetsTheSameStatusAndBody` (link com `expiresAt` no passado, na consulta e na conclusão). **Guarda:** o vencimento conferido só para o convite na consulta → `consulta da redefinição … → 200` (`guardas/6.5-redefinicao-reprova.log`, 1 de 2); `6.5-redefinicao-aprova.log` 2/2.
- [x] 6.6 (`apps/api`, D7) **Consulta e conclusão da redefinição.** `PasswordResetTests`:
      - consulta → `200 { nome, email, expiraEm }`;
      - conclusão com política ruim → `400`; conclusão → `204` sem `Set-Cookie`;
      - a senha antiga → `401`, a nova → `204`;
      - **toda renovação de antes → `invalid_grant`, e a lista de sessões vivas fica vazia**;
      - com as 2 etapas ligadas, o login com a senha nova → `200 { "segundoPasso": "totp" }`;
      - o bloqueio zerado pela conclusão (P8);
      - desativar depois de gerar → o link → `404`, e reativar não o devolve.

      **Guardas, um de cada vez:** a revogação (`UserSessions.EndAllAsync`) retirada → a lista mostra as sessões (`guardas/6.6-revoga-reprova.log`); a troca do stamp retirada → a renovação passa (`guardas/6.6-stamp-reprova.log`); `Deactivate` sem apagar o link (o defeito do 3.3) → o link desativado e reativado → `200` (`guardas/6.6-desativar-reprova.log`). `6.6-aprova.log`.

      **Resultado, 02/11/2026.** `PasswordResetTests` 12/12. **Guardas, um de cada vez:** a revogação retirada → `Completing_ChangesThePassword_EndsEverySession_AndOpensNone` reprovou (`guardas/6.6-revoga-reprova.log`, 10 de 11); `Deactivate` sem apagar o link → `Deactivating_InvalidatesTheLink_AndReactivatingDoesNotBringItBack` (`6.6-desativar-reprova.log`, 10 de 11).
      - **Premissa falsa, só acrescenta caso:** com a troca do stamp retirada, as 11 passaram (primeira `6.6-stamp-reprova.log`, sobrescrita): a revogação já derruba a renovação sozinha, como a #219 tinha registrado no `AccountPasswordTests`. O stamp guarda o cookie do emissor. Caso novo `Completing_RefusesASessionCookieInFlight` (o mesmo arranjo do `TheChange_RefusesASessionCookieInFlight` da #219): o cookie de antes, no authorize → redirect para `/entrar`. Com o defeito, só ele reprovou (`6.6-stamp-reprova.log`, 11 de 12); `6.6-stamp-aprova.log` 12/12.
- [x] 6.7 (`apps/api`, D7) **Duas conclusões concorrentes**: um `204` e um `404` (20 rodadas), com o mesmo par do SQL do 6.4 sobre o `UPDATE` da conclusão.

      **Guarda contra** o consumo em dois passos na conclusão: a asserção do SQL reprova em toda execução (classe inteira três vezes, `guardas/6.7-reprova-{1,2,3}.log`). `6.7-aprova.log`.

      **Resultado, 02/11/2026.** `PasswordResetTests.TwoConcurrentCompletions_OneWins_AndTheConsumptionIsOneConditionalUpdate`: 0 de 20 rodadas com as duas conclusões aprovadas, em todas as execuções; os quatro fragmentos no `WHERE`. **Guarda, classe três vezes:** reprovou nas três (`guardas/6.7-reprova-{1,2,3}.log`, 10 de 11 cada), também pela parte comportamental, como no 6.4 (a mesma divergência); `6.7-aprova.log` 11/11. **Asserção do SQL isolada (02/11/2026, a pedido no fechamento):** um caso temporário `PasswordResetTests.SqlIsolada_Redefinicao` com um pedido só, sem corrida e sem a parte comportamental, e a mesma asserção do SQL; sem o defeito aprovou (`guardas/6.7-sql-isolada-aprova.log`, 1/1), com o consumo em dois passos reprovou pela asserção (`Assert.Contains`: `"WHERE id = @p5;"` sem `access_link_hash = @`, `guardas/6.7-sql-isolada-reprova.log`). O caso saiu e o arquivo voltou byte a byte; o teste da corrida ficou como estava.
- [x] 6.8 (`apps/api`, D8, P9) **Barreiras, limite e resposta única.** `AccessLinkPublicRoutesTests`:
      - as quatro rotas: sem JSON → `415`; `Origin` de fora → `403`;
      - **inexistente, vencido, usado, cancelado e de outro propósito** (o de redefinição na rota do convite e o inverso), na consulta e no uso → **o mesmo status e o mesmo corpo, byte a byte**;
      - o aceite entra no limite global (o arranjo do 2.7: as excedentes esperam na fila ou recebem `429`), e a consulta não.

      **Guardas, um de cada vez:** uma mensagem distinta para o vencido → o caso da resposta única reprova (`guardas/6.8-mensagem-reprova.log`); o `purpose` fora do `WHERE` → o link de redefinição aceito na rota do convite (`guardas/6.8-proposito-reprova.log`); o `Marker` retirado do aceite → o caso do limite reprova (`guardas/6.8-limite-reprova.log`). `6.8-aprova.log`.

      **Resultado, 02/11/2026.** `AccessLinkPublicRoutesTests` 2/2 e `AccessLinkRateLimitTests` 1/1 (host próprio com o limite ligado, o arranjo do 2.7). **Guardas:** mensagem distinta para o vencido → `EveryInvalidLink_GetsTheSameStatusAndBody` reprovou (`guardas/6.8-mensagem-reprova.log`); o `purpose` fora do `WHERE` da busca → o mesmo caso (`6.8-proposito-reprova.log`; `6.8-proposito-aprova.log` 2/2); o `Marker` retirado do aceite → `ABurstOfAccepts_GetsSome429_AndABurstOfLookups_None` (`6.8-limite-reprova.log`; `6.8-limite-aprova.log` 1/1).
      - **Divergência:** o estado mascara o propósito (o aceite apaga o link, então um link de redefinição nunca está num convidado). Cenário acrescentado: um link de convite regravado à mão num ativo, usado na rota da redefinição. Sem ele, o guarda do propósito passaria.
      - Na primeira rodada o guarda do propósito não chegou a aplicar: o texto novo vazio fez a checagem de unicidade do script recusar antes de gravar (o mesmo nos dois do 6.6). Refeito com um comentário marcador no lugar da linha.
- [x] 6.9 (`apps/api`, D3) **O token fora dos logs.** `AccessLinkLogTests`: com `Microsoft.AspNetCore.Hosting.Diagnostics` em `Information` no host de teste (o arranjo do 2.2), um ciclo de convite (criar, consultar, aceitar) e um de redefinição (gerar, consultar, concluir) não deixam o token em nenhuma linha de log capturada.

      **Guarda contra** a consulta do convite por `GET /auth/invitation/{token}`, registrada temporariamente no lugar da `POST`: o caso reprova (a linha "Request starting" traz o token). `guardas/6.9-reprova.log`, `6.9-aprova.log`.

      **Resultado, 02/11/2026.** `AccessLinkLogTests` 2/2. **Guardas:** uma linha de log com o token → os dois casos reprovaram (`guardas/6.9-log-reprova.log`); a consulta por `GET /auth/invitation/{token}` → `NoRoute_TakesTheTokenInThePathOrQuery` reprovou (`6.9-get-reprova.log`); `6.9-get-aprova.log` 2/2.
      - **Divergência:** o ciclo usa `POST`, então uma rota `GET` registrada a mais não aparece no log do ciclo; o caso `NoRoute_TakesTheTokenInThePathOrQuery` (nenhum template de rota com `{token}`, nem query) entrou para que o defeito nomeado reprove.
- [x] 6.10 (`apps/api`, D2, achado 1, `local-users`) **O convidado no login.** `InvitedLoginTests`, com o dublê contador do 2.6 registrado no host: login com o e-mail do convidado e qualquer senha → `401` "E-mail ou senha inválidos.", com o corpo igual, byte a byte, ao de um e-mail sem conta, a falha contada no bloqueio do e-mail do convidado, e **uma chamada à verificação fictícia**, como no login de um e-mail sem conta.

      **Guardas, um de cada vez:** o `return Failed` imediato do `Verify` (o contador fica em zero; `guardas/6.10-caminho-reprova.log`); uma recusa pelo `status` antes da senha (o convidado recebe o `403` "Acesso desativado"; `guardas/6.10-status-reprova.log`). `6.10-aprova.log`.


      **Resultado, 02/11/2026.** `InvitedLoginTests` 1/1, com o `CountingPasswordHasher` no host: o convidado → `401` com o corpo igual ao do e-mail sem conta, a falha contada no e-mail do convidado, uma chamada à verificação fictícia. **Guardas:** o `return Failed` imediato (`guardas/6.10-caminho-reprova.log`); a recusa pelo `status` antes da senha (`6.10-status-reprova.log`); `6.10-status-aprova.log` 1/1. A comparação de tempo saiu da suíte (o ajuste do dono): é a medição manual do 8.3.
## 7. Documentação (antes do archive)

- [x] 7.1 `01-ARQUITETURA_E_CONVENCOES.md` (Autenticação): o convidado como estado de `users`; o link de acesso (256 bits, SHA-256, uso único por `UPDATE` condicional, 72 h e 24 h, o que invalida); o token no fragmento e o porquê (o `$request` do nginx); a senha de quem chama nas três rotas; o hash fictício para usuário sem senha.

      **Feito, 02/11/2026.** Bullet novo em Autenticação (o estado `invited`, o link de 256 bits com só o SHA-256 guardado, o uso único por `UPDATE` condicional, 72 h / 24 h e o que invalida, o token no fragmento por causa do `$request` do nginx, a senha de quem chama nas três rotas, a verificação fictícia para o usuário sem senha) e a nota de que o gatilho do P5 não disparou.
- [x] 7.2 `docs/architecture.md`: o mesmo, no nível do documento, com o fluxo do convite (criar → link → consultar → aceitar → login normal). `docs/deployment.md`: a migration, a orientação ao admin sobre o canal do link (D1, o resíduo do P5), a conferência por `curl` e o rollback (cancelar os convites pendentes antes).

      **Feito, 02/11/2026.** `docs/architecture.md`: a linha de estados de `users` e uma seção nova com o diagrama do fluxo (criar → link → consultar → aceitar → login normal; gerar → concluir → sessões encerradas). `docs/deployment.md`: seção "Convite e redefinição de senha por link (#181)", antes da #219, com a migration, a base do link, o fragmento, a orientação ao admin sobre o canal, a conferência por `curl` e o rollback (cancelar os convites pendentes antes).
- [x] 7.3 `02-HISTORICO_E_STATUS.md` e `CHANGELOG.md`, escritos antes do sync e do archive, com o que a implementação mediu:
      - as rodadas das corridas;
      - a medição do tamanho contra a projeção (convenção 18, só código, sem migrations);
      - as divergências do design (convenção 9);
      - os três achados no código e os guardas.


      **Feito, 02/11/2026.** Seção `convites-redefinicao-senha` no fim do `02` (as suítes por nome e o controle da baseline, as corridas, o tamanho, o tempo do login, as divergências, o stack e o pós-merge). `CHANGELOG`: entrada em Added depois da #219 e o achado 1 no topo do `### Fixed` existente.
## 8. Fechamento

- [x] 8.1 As quatro suítes da baseline no worktree da change, pelo mesmo script e protocolo, com `CPU_Speed_Limit = 100` e os dois loads conferidos antes de cada uma: `apps/api`, `apps/frontend` (com o JSON), `tests/InboxOrchestratorRoundTrip.Tests` e `tests/ApiConnectorsRoundTrip.Tests`. Comparação **por nome** contra as duas rodadas do `apps/api` e o `.trx`/JSON das outras (`compare-trx.py`): os testes de antes, todos com o mesmo resultado (as falhas de #231 e #232 só contam se caírem em teste que passou nas duas), e os novos listados um a um. `git diff` vazio em `tests/` e em `apps/frontend/`.

      **Resultado, 02/11/2026**, pelo `suite-181.sh` (espera `CPU_Speed_Limit = 100` e os loads de 1 e 5 min antes de cada suíte), comparado por nome com `compare-trx.py` e, no frontend, pelo JSON (arquivo relativo + nome do teste). Saídas em `181-verificacao/suites{,-2,-3}/` e `controle-base/`.

      | suíte | baseline (duas rodadas no api) | fechamento | por nome |
      |---|---|---|---|
      | `apps/api` | 1.172 aprovados + 1 ignorado + 1 falha (a #231 numa rodada, a #232 na outra) | **rodada 2: 1.244 aprovados + 1 ignorado, 0 falha** | contra as duas rodadas: 0 de antes com resultado mudado; `Verify_NullHash_Fails` absorvido na teoria do 3.4 (de propósito); 73 novos, todos aprovados. **A #231 e a #232 não reapareceram** (os dois testes passaram) |
      | `tests/ApiConnectorsRoundTrip.Tests` | 8/8 | **8/8** | igual |
      | `tests/InboxOrchestratorRoundTrip.Tests` | 12/12 | 11/12 nas rodadas 2 e 3 | `Source3_WorkerStopsBetweenTerminalWriteAndPush…` reprova, **e reprova igual no worktree da baseline** (controle, árvore limpa em `a4d3495`, mesmo horário) |
      | `apps/frontend` | 1.978/1.978 em 101 s | 32, 30 e 36 falhas, em ~480 s | as falhas são timeouts de teste, em arquivos que mudam de rodada para rodada; **o controle da baseline reprovou 27** nas mesmas condições |

      - **A máquina não sustentou o protocolo:** o `CPU_Speed_Limit` cai de 100 para 23 durante as rodadas (calor), e o load sobe a 22–38. A espera do script vale só para o início. A rodada 1 do api (37 min, load 22–25 no fim) teve 2 falhas fora da área da change (`IdentityKeyRingGateTests.WarmupFails_ThenRetries…`, `KnowledgeDocumentCatalogTests.AfterAFailedDispatch…`); a rodada 2 (16 min) passou inteira.
      - **O frontend e o rt-inbox não puderam ser fechados verdes nesta máquina hoje**, nem no código da baseline. `git diff` vazio em `apps/frontend/` e em `tests/`, e a change não toca o inbox. O `Source3` reprovando no código de `main` é um achado novo, fora das #231 e #232: fica para o dono decidir se vira issue.
      - A rodada 1 deixou um contêiner `pgvector` vivo (Testcontainers sem Ryuk), removido antes da rodada 2.
      - **Rodadas finais (02/11, à tarde, na tomada, `181-verificacao/validas/`)**, alternando baseline e change, com o `suite-181.sh` amostrando a CPU a cada 5 s e a regra de validade **assimétrica** do dono (verde conta mesmo com queda; reprovação só com CPU em 100 do início ao fim). `apps/frontend`: **1.978/1.978 nas 8 rodadas** (baseline 11:55, 12:01, 12:08, 12:15 com CPU mínima 60, 72, 62 e 62; change 12:22, 12:27, 12:34, 12:42 com 62 nas quatro), por nome 0 ausentes, 0 novos, 0 mudados; a própria suíte derruba o limite em 55 a 90 s (sugestão na #226). `InboxOrchestratorRoundTrip`: **12/12 nos dois** (baseline 12:47, change 12:48, CPU em 100 durante as duas). O `Source3` da manhã é a #176, anterior à change, e não achado novo; as 4 reprovações de hoje estão comentadas nela.
- [x] 8.2 Build `--no-incremental` do `apps/api` e do projeto de testes com o mesmo conjunto de avisos da baseline, por código, contagem, arquivo e mensagem.

      **Resultado, 02/11/2026.** `dotnet build --no-incremental` dos dois projetos nos dois worktrees (`181-verificacao/build/`): `Buteco.Api` sem aviso nos dois; o projeto de testes com os mesmos avisos, por código e contagem (CS8602 ×4, CS8604 ×4, EF1001 ×8, EF1002 ×12, NU1903 ×8), e as 16 linhas distintas de arquivo, código e mensagem iguais (`diff` vazio de `base-warnings.txt` e `change-warnings.txt`). Nenhum aviso novo nos arquivos da change.
- [x] 8.3 Stack local (`docker-compose.prod.yml`, como na 9.3 da #219), com a saída colada aqui:
      - login do bootstrap pelo painel;
      - `POST /users/invitations` pelo `curl` com o token e a senha → `201`, o link em `PUBLIC_DOMAIN/convite#…`; sem a senha → `401`;
      - `POST /auth/invitation` e `/accept` com o token e `Origin` do domínio → `200` e `204`; o login do convidado → `204`;
      - **o link aberto num navegador** (`/convite#<token>`): a linha do nginx do stack (`podman logs` do `frontend`) é `GET /convite HTTP/1.1`, sem o token; e nenhuma linha do `frontend` nem do `api` contém o token em todo o ciclo (`grep` do token nos dois logs, vazio);
      - redefinição: gerar, concluir, a sessão de antes → `invalid_grant`;
      - `GET /convite` sem `Sec-Fetch-Mode` e com `navigate` → o `index.html` do painel;
      - **conferência manual de tempo** (fora da suíte, decisão do dono): o tempo do `POST /auth/session` com o e-mail de um convidado contra o de um e-mail sem conta e o de uma senha errada, 20 de cada, intercalados, com `CPU_Speed_Limit` e load registrados antes; medianas e quartis colados aqui. Com o código de `main` para comparação, se o stack da baseline estiver de pé.

      **Resultado, 02/11/2026.** `docker-compose.prod.yml` em `http://localhost:18181`, projeto `buteco181`, segredos descartáveis (`181-verificacao/stack/`); a migration `AddUserAccessLinks` aplicada pelo `migrator`. **O `resolver 127.0.0.11` do nginx trocado pelo gateway da rede dentro do contêiner** (a #212, como na #219); o arquivo versionado não muda. Roteiro: `stack/fluxo.py`.

      - **Login do bootstrap pelo painel** (Chrome, `/entrar`): entrou no Inventário.
      - **O link aberto no Chrome** (`/convite#<token>`): a linha do nginx é `"GET /convite HTTP/1.1" 200 1396`, sem o token. O SPA mostra o 404 do roteador: a tela do convite é da #183.
      - **`grep` dos dois tokens** (convite e redefinição, 43 caracteres) em todo o log do `frontend` (360 linhas) e do `api` (480 linhas), depois do ciclo inteiro: **0 linhas nos dois**.

      ```
      login do admin (sessão, authorize, troca)                      → 204
      POST /users/invitations sem a senha                            → 401 {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.2","title":"Senha inválida.","status":401}
      POST /users/invitations com a senha errada                     → 401 {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.2","title":"Senha inválida.","status":401}
      POST /users/invitations com a senha                            → 201 {"usuario":{"id":"01a19c31-7be1-75b8-9cb5-4ba6768d2e92","nome":"Rafael Duarte","email":"convidado.c386f7@stack181.local","estado":"convidado","formasDeEntrada":[],"duasEtapas":false,"ultimoAcesso":null,"criadoEm":"2026-11-02T12:17:31.1056953+00:00","convite":{"expiraEm":"2026-11-05T12:17:31.1056953+00:00","vencido":false}},"link":"http://localhost:18181/convite#<token>","expiraEm":"2026-11-05T12:1…
      POST /auth/invitation (consulta)                               → 200 {"nome":"Rafael Duarte","email":"convidado.c386f7@stack181.local","convidadoPor":"admin","expiraEm":"2026-11-05T12:17:31.105695+00:00"}
      POST /auth/invitation de outra origem                          → 403 {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.4","title":"Origem não permitida.","status":403}
      POST /auth/invitation/accept com senha curta                   → 400 {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"A senha precisa ter de 12 a 128 caracteres.","status":400}
      POST /auth/invitation/accept                                   → 204 set-cookie=False
      o mesmo link de novo                                           → 404 {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.5","title":"Este link não vale mais. Se você já criou a senha, entre normalmente; se não, peça um novo link ao administrador.","status":404}
      login do convidado (sessão, authorize, troca)                  → 204 tokens emitidos
      POST /users/{id}/password-reset com a senha                    → 200 {"link":"http://localhost:18181/redefinir-senha#<token>","expiraEm":"2026-11-03T12:18:49.5563428+00:00"}
      POST /auth/password-reset (consulta)                           → 200 {"nome":"Rafael Duarte","email":"convidado.c386f7@stack181.local","expiraEm":"2026-11-03T12:18:49.556342+00:00"}
      POST /auth/password-reset/complete                             → 204 set-cookie=False
      renovação da sessão de antes                                   → 400 invalid_grant
      login com a senha antiga                                       → 401 {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.2","title":"E-mail ou senha inválidos.","status":401}
      login com a senha nova                                         → 204
      o mesmo link de novo                                           → 404 {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.5","title":"Este link não vale mais. Se você já criou a senha, entre normalmente; se não, peça um novo link ao administrador.","status":404}
      GET /convite Sec-Fetch-Mode=None                               → 200 content-type=text/html painel=True
      GET /convite Sec-Fetch-Mode=navigate                           → 200 content-type=text/html painel=True
      ```

      **Conferência manual de tempo** (`POST /auth/session`, 20 de cada grupo, intercalados; 5 e-mails × 4 tentativas, abaixo do bloqueio):

      ```
      antes: 2026-11-02 09:39:29   CPU_Speed_Limit = 100  load { 5.14 4.83 5.89 }
      tempo convidado (n=20, status [401])                           →  mediana 166.3 ms  Q1 40.8  Q3 180.4  min 32.1  max 268.4
      tempo sem conta (n=20, status [401])                           →  mediana 151.8 ms  Q1 39.9  Q3 177.4  min 31.6  max 267.1
      tempo senha errada (n=20, status [401])                        →  mediana 159.9 ms  Q1 39.3  Q3 183.6  min 31.6  max 311.1
      depois:  CPU_Speed_Limit = 23  load { 6.40 5.11 5.98 }
      antes (bateria 2): 2026-11-02 09:40:14   CPU_Speed_Limit = 100  load { 5.16 4.91 5.89 }
      tempo convidado (n=20, status [401])                           →  mediana 145.0 ms  Q1 138.6  Q3 154.0  min 37.7  max 175.5
      tempo sem conta (n=20, status [401])                           →  mediana 145.8 ms  Q1 133.8  Q3 159.3  min 38.0  max 178.9
      tempo senha errada (n=20, status [401])                        →  mediana 145.4 ms  Q1 140.0  Q3 150.9  min 37.7  max 184.9
      depois:  CPU_Speed_Limit = 23  load { 5.04 4.89 5.87 }
      ```
      - Na bateria 2 as três medianas ficam em 145,0 / 145,8 / 145,4 ms: **o convidado não se distingue do e-mail sem conta nem da senha errada.** A bateria 1 é bimodal (Q1 ~40 ms, mediana ~160 ms) nos três grupos igualmente: o `CPU_Speed_Limit` caiu de 100 para 23 durante a medição; intercalada, a queda atinge os três grupos.
      - Sem a comparação com o código de `main`: o stack da baseline não estava de pé.
      - As duas tentativas com a senha errada de quem chama (`401`) foram contadas no bloqueio do admin, como manda a P10; abaixo do limite.
- [x] 8.4 Medição do tamanho (convenção 18) contra a projeção do design (~710 / ~1.350), colada aqui e no `02`.

      **Resultado, 02/11/2026** (árvore contra `a4d3495`, sem `openspec/`, `.md` nem migrations; os arquivos novos contados por `wc -l`): **produção +971 / −66, 18 arquivos; testes +1.748 / −2, 19 arquivos.** Contra a projeção (~710 / ~1.350): **+37 % / +29 %**. Fica dentro da faixa do pior da série que o design calculou (~1.110 / ~1.670) na produção e um pouco acima dela nos testes.
      - Por componente, a produção que passou: as públicas (`AccessLinkPublicEndpoints` 266 contra ~200: a leitura do corpo com o `400` próprio e os dois `UPDATE` condicionais escritos por inteiro); o convite (`InvitationEndpoints` 191 + `CallerPassword` 30 contra ~210); `UserFields` (+51), extraído de `UserEndpoints` (−41), é movimento, não código novo, e não estava na projeção; a migration (+1.446 com o Designer, +29 no snapshot) fica fora da régua.
      - Nos testes, a diferença está no que entrou como divergência ou no fechamento: os casos de SQL das duas corridas, o propósito regravado à mão, `NoRoute_TakesTheTokenInThePathOrQuery`, o cookie em trânsito e a redefinição vencida, mais o apoio (`AccessLinkClient`, `InvitationSeed`, `CountingPasswordHasher`: 122 contra ~100).
      - As classes mais longas: `PasswordResetTests` 243, `InvitationCreateTests` 219, `InvitationAcceptTests` 213.
- [x] 8.5 `openspec validate convites-redefinicao-senha --strict`; `/opsx:sync` e `/opsx:archive` dentro do worktree, com o `02` e o `CHANGELOG` já escritos (7.3); `openspec validate --all --strict` depois. Sem validação visual: nenhuma tela muda.

      **Feito, 02/11/2026.** `openspec validate convites-redefinicao-senha --strict` válido; archive com o sync das quatro capacidades (`local-users`, `user-access-links`, nova, `user-account`, `user-management`), com o `02` e o `CHANGELOG` já escritos; `openspec validate --all --strict` depois. O 8.6 a 8.8 ficam abertos: dependem do ok do dono.
- [ ] 8.6 O único commit da change, depois do sync e do archive, com tudo junto (código, testes, docs e OpenSpec), só com o pedido do dono naquela mensagem e sem atribuição.
- [ ] 8.7 Push e PR com `Closes #181`, só com o ok do dono naquela mensagem; mover a #181 para `In review`.
- [ ] 8.8 Depois do merge: remover os dois worktrees, um de cada vez, e a branch local com `git branch -d`.
