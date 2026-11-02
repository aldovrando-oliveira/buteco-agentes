## Context

**Estado atual (`80f6c9a`, merge do PR #229, 01/11/2026).** A #180, a #217 e a #218 estão em `main`:

- **`User`** (`Users/User.cs`, tabela `users`): `id` (o `sub`), `email`, `normalized_email` (índice único), `name`, `status` (`active`/`deactivated`), `password_hash`, `security_stamp`, `created_at`, `last_access_at`, `deactivated_at` e as colunas das 2 etapas. Já tem `Deactivate(now)`, que troca o stamp, e `ResetTwoFactor()`, que também troca. Não tem reativar, trocar senha nem editar.
- **`CurrentUser`** (`Users/CurrentUser.cs`, #218): o usuário do `sub` do access token do emissor, existente e ativo; senão `403` "Esta rota exige o login de um usuário ativo.". O HMAC `operator` não tem `sub` e cai no `403`.
- **Os grupos `/account` e `/users` existem** (#218, P12), com `account|users` no `deploy/nginx.conf`. Hoje servem `/account/two-factor/*` e `POST /users/{id}/two-factor/reset`.
- **O estilo da área `Users/`** é de classes estáticas de endpoint com o `AppDbContext` injetado (`AccountTwoFactorEndpoints`, `UserTwoFactorResetEndpoint`), e não o par comando/consulta com `Mediator` de `Agents/` e `McpServers/`.
- **Todo token novo de usuário relê estado e stamp** (`UserGrantTokenHandler`), nos grants `authorization_code` e `refresh_token`, e registra o último acesso na renovação.
- **O principal das rotas** é reconstruído em `IssuerTokenAuthentication.MapPrincipalByClient` só com `NameIdentifier` (pela tabela de clientes), `client_id` e `sub` (l. 89–112). Todo outro claim do access token é descartado.
- **O nome e o e-mail do painel** saem do id token (`apps/frontend/src/auth/sessionStore.ts` l. 121–122), e o `UserPrincipalFactory` os põe só no id token, a partir do usuário do authorize.
- **O bloqueio por e-mail** (`LoginThrottleStore`) conta as falhas de senha e de código das rotas autenticadas da #218 (`setup`, `remove`, `recovery-codes`).

**O que a issue e os registros já decidiram:**

| decisão | de onde |
|---|---|
| sessão = autorização ad hoc `valid` do usuário; encerrar e desativar revogam pelas autorizações (`RevokeBySubjectAsync`), 0,5 a 5,7 ms contra 43 a 251 ms pelos tokens | #180 D3.10, #209 3.6 |
| a lista mostra só as sessões vivas; histórico não é requisito | #209 D9 (dono, 26/10/2026) |
| `last_access_at` no login e na renovação, nunca por requisição | #180 D3.11 |
| desativar troca o stamp; reativar não devolve o antigo, e exige login novo | #180 D4 |
| trocar a senha, desativar e redefinir as 2 etapas trocam o stamp | corpo da #219, #180 D4 |
| a redefinição das 2 etapas é da #218 (`POST /users/{id}/two-factor/reset`); a lista e o detalhe leem `two_factor_enabled_at` | #218 D9, comentário na #219 |
| quem chama é o usuário real do token (`CurrentUser`); HMAC de operador → `403` | #218 D9 (revisão do dono, 01/11/2026) |
| política de senha: mínimo 12, máximo 128, sem regra de composição | corpo da #219 |
| usuário é desativado, nunca excluído | corpo da #180 |

**O que fica de outras issues**, lido nos corpos:

- **#181:** convite (nome, e-mail, link de 72 h, gerar novo link, cancelar) e redefinição de senha por link (24 h). Tem em aberto "se concluir a redefinição encerra as sessões ativas" e "unicidade do e-mail entre usuários e convites pendentes".
- **#184:** papéis Admin, Operador e Leitor; "usuários: gerir" só para Admin; "o último Admin não pode ser desativado nem rebaixado"; "papel novo vale na próxima requisição".
- **#221:** o IP real do cliente por cabeçalho de proxy confiável. Hoje o `apps/api` só vê o IP do nginx do stack.

**O protótipo** (canvas `claude.ai/artifact/H7dPBZafiSkdKd1hqesPZ6`, versão `1793572637-a8aa`, lido em 01/11/2026 pela ferramenta Artifact):

- **"Usuários"** (`project/Usuarios.dc.html`): uma tabela só, com ativos, desativados e convites pendentes juntos, nas colunas Usuário (nome e e-mail), Formas de entrada, Papel, Verificação em 2 etapas, Estado, Último acesso e Ações ("Editar"; "Gerar novo link" e "Cancelar convite" no pendente). **Não há filtro nem paginação.** "Convidar usuário" abre um formulário no card.
- **"Editar usuário"** (`project/UsuarioDetalhe.dc.html`):
  - cabeçalho: nome, selo do estado, "Desativar", e "Convidado por Aldovrando Oliveira em 12/08/2026 · último acesso ontem, 18:40";
  - **Dados e papel:** Nome, E-mail ("É o login com senha. Trocar não afeta as contas conectadas."), Papel, o aviso "Admin exige verificação em 2 etapas…", "Salvar alterações";
  - **Formas de entrada:** linhas Senha e Google, com Identificação, Conectada em, Último uso e as ações "Redefinir senha" e "Desconectar";
  - **Verificação em 2 etapas:** selo e "Redefinir verificação";
  - **Sessões:** "2 sessões ativas · última atividade ontem, 18:40", "Encerrar faz o Rafael precisar entrar de novo em todos os navegadores. Use se suspeitar de acesso indevido.", "Encerrar todas as sessões". **Não há lista de sessões de outro usuário**, só a contagem.
- **Não há quadro de Minha conta.**

### Achados da fonte do OpenIddict 7.7.2

Lidos na tag `7.7.2` (`950cb13`), clonada em 01/11/2026. Caminhos relativos a `src/`. Os fatos F1–F15 (#209) e G1–G5 (#180) continuam valendo.

| # | fato | onde |
|---|---|---|
| H1 | O access token **sempre** leva `oi_au_id`: o filtro de claims do access token inclui `Claims.Private.AuthorizationId` incondicionalmente (G4 da #180, reconferido). | `OpenIddict.Server/OpenIddictServerHandlers.cs` l. 3633–3638 (`PrepareAccessTokenPrincipal`, l. 3595) |
| H2 | **O `oi_au_id` vai ao JWT.** O `AttachTokenSubject` só exclui do payload `oi_crt_dt`, `oi_exp_dt`, `oi_iss`, `oi_tkn_typ`, e, no access token, `oi_aud` e `oi_scp` (que viram `aud` e `scope`). O `oi_au_id` passa. | `OpenIddictServerHandlers.Protection.cs` l. 1605–1618 |
| H3 | **A renovação mantém a mesma autorização.** O `AttachAuthorization` não cria outra quando o principal já traz `oi_au_id`, e no grant `refresh_token` o principal é o do refresh. Então o `oi_au_id` do access token identifica a sessão do login até ela acabar. | `OpenIddictServerHandlers.cs` l. 3545; `OpenIddictServerHandlers.Exchange.cs` l. 2132–2135 |
| H4 | **A autorização só tem `CreationDate`**; não há data de último uso. O token tem `CreationDate`, `ExpirationDate` e `RedemptionDate`. Como o refresh é rotativo (F9), cada renovação grava um refresh novo na mesma autorização: a maior `creation_date` dos tokens da autorização é a última renovação. | `OpenIddict.EntityFrameworkCore.Models/OpenIddictEntityFrameworkCoreAuthorization.cs` l. 54; `…Token.cs` l. 59, 64, 88 |
| H5 | **`RevokeBySubjectAsync` é um `ExecuteUpdateAsync` só**, que põe `revoked` em toda autorização do subject, sem filtrar estado nem tipo. Não há variante "todas menos uma". `TryRevokeAsync(authorização)` revoga uma, e trata conflito de concorrência como `false`. | `OpenIddict.EntityFrameworkCore/Stores/OpenIddictEntityFrameworkCoreAuthorizationStore.cs` l. 840–853; `OpenIddict.Core/Managers/OpenIddictAuthorizationManager.cs` l. 788–815 |
| H6 | **O store usa o `AppDbContext` do escopo da requisição.** `UseDbContext<TContext>()` registra `OpenIddictEntityFrameworkCoreContext<TContext>` como scoped, que recebe o `TContext` por construtor e o devolve. Então o `ExecuteUpdateAsync` do H5 roda na mesma conexão e **dentro da transação aberta** pelo endpoint no mesmo `AppDbContext`. | `OpenIddict.EntityFrameworkCore/OpenIddictEntityFrameworkCoreBuilder.cs` l. 124–128; `OpenIddictEntityFrameworkCoreContext.cs` l. 29–45; no `apps/api`, `IdentityIssuerServiceCollectionExtensions.cs` l. 65 |
| H7 | **O principal de um handler de `HandleTokenRequestContext` é o que assina.** O `AttachPrincipal` (ordem `int.MinValue + 100_000`) põe em `context.Principal` o principal do refresh, e o `HandleTokenRequest` despacha o `ProcessSignInContext` com `notification.Principal`. Um handler posterior que troque claims desse principal muda os tokens emitidos. | `OpenIddictServerHandlers.Exchange.cs` l. 2095–2135 e 244–248 |
| H8 | Índices das tabelas do OpenIddict: autorizações em (`application_id`, `status`, `subject`, `type`); tokens em (`application_id`, `status`, `subject`, `type`) e na FK `authorization_id`. O `subject` não é a primeira coluna. | `…/Configurations/OpenIddictEntityFrameworkCoreAuthorizationConfiguration.cs` l. 42–46; `…TokenConfiguration.cs` l. 48–52 |

## Goals / Non-Goals

**Goals:**

- As rotas que a #183 precisa para Minha conta, Usuários e Editar usuário, com o contrato fechado aqui.
- Uma sessão encerrada, por qualquer caminho, some da lista e não renova mais.
- Nenhuma corrida deixa a instalação sem usuário ativo.
- As perguntas a–h do dono respondidas, com evidência; as que são dele, em "Open Questions", cada uma com a recomendação.

**Non-Goals** (cada um com o dono):

- **Telas** (#183).
- **Convite, gerar novo link, cancelar convite, convite pendente na lista, "Convidado por", e a redefinição de senha por link** (#181).
- **Papel, a regra do último Admin, "papel novo vale na próxima requisição", restringir `/users` ao Admin** (#184, #185).
- **Formas de entrada federadas, "Desconectar", "Conectada em" e "Último uso" por forma, e o selo "No provedor"** (M3).
- **Redefinir as 2 etapas** (#218, já em `main`).
- **IP e navegador da sessão** (#221 para o IP; navegador não é pedido).
- **2 etapas obrigatórias** (#220).
- **Histórico de sessões encerradas** (#209 D9).

## Decisions

### D1. (a) As operações

**São 11, e não 10** (P1). A tabela da divisão da #180 contava 11 com "encerrar" como uma operação só, e a #218 tirou a redefinição: 10. O corpo da #219, que é o escopo, lista "encerrar uma sessão; encerrar todas" como duas. Contadas pelo corpo, sem a redefinição, são 11:

**Minha conta** (grupo `/account`):

| # | rota | corpo | respostas | grava |
|---|---|---|---|---|
| 1 | `GET /account` | — | `200 { id, nome, email, criadoEm, ultimoAcesso, duasEtapas }` | nada |
| 2 | `POST /account/password` | `{ senhaAtual, novaSenha }` | `204`; `400` política (D3); `401` senha atual errada, contada no bloqueio; `423` + `Retry-After` durante o bloqueio, sem conferir | `password_hash` (Argon2id), `password_hash_is_bootstrap_legacy = false`, `security_stamp` novo; revoga todas as autorizações (D2) |
| 3 | `GET /account/sessions` | — | `200 [ { id, criadaEm, ultimoUso, atual } ]`, só as vivas (D4) | nada |
| 4 | `POST /account/sessions/{id}/revoke` | — | `204`; `404` inexistente, de outra pessoa ou já não viva | a autorização `revoked` |
| 5 | `POST /account/sessions/revoke-others` | — | `200 { encerradas }` | as autorizações da pessoa, menos a atual, `revoked` |

**Usuários** (grupo `/users`):

| # | rota | corpo | respostas | grava |
|---|---|---|---|---|
| 6 | `GET /users` | — | `200 [ UserSummary ]` (D6) | nada |
| 7 | `GET /users/{id}` | — | `200 UserDetail`; `404` | nada |
| 8 | `PUT /users/{id}` | `{ nome, email }` | `200 UserDetail`; `400` validação; `404`; `409` e-mail em uso | `name`, `email`, `normalized_email` (D5) |
| 9 | `POST /users/{id}/deactivate` | — | `200 UserDetail`; `403` a si mesmo; `404` | `status`, `deactivated_at`, `security_stamp`; revoga todas as autorizações (D2, D7) |
| 10 | `POST /users/{id}/activate` | — | `200 UserDetail`; `404` | `status`, `deactivated_at = null` |
| 11 | `POST /users/{id}/sessions/revoke` | — | `200 { encerradas }`; `404` | todas as autorizações do usuário `revoked` |

- **Toda rota passa pelo `CurrentUser`**: HMAC de operador, `sub` inexistente ou usuário desativado com access token ainda válido → `403`, antes de qualquer outra resposta (g).
- **`POST` para ações**, como `McpServers` (`/{id}/activate`, `/{id}/deactivate`) e a #218 (`/two-factor/reset`); **`PUT` para editar**, como `PUT /mcp-servers/{id}`. Os nomes de campo seguem a #218 (português no corpo: `senhaAtual`, `nome`, `ativa`).
- **`activate`, e não `reactivate`**, pelo par de `McpServers` e `Agents`.
- **Desativar e reativar são idempotentes**, como em `McpServers`: o já desativado (ou o já ativo) responde `200` com o estado atual, **sem gravar nada** (sem stamp novo, sem revogação).
- **Responder `200` com o detalhe** nas ações sobre um usuário, como `McpServers`, para a #183 atualizar a tela sem segunda leitura.

**O que o protótipo mostra e não é desta issue:**

| no protótipo | dono |
|---|---|
| "Convidar usuário", o link, "Gerar novo link", "Cancelar convite", a linha "Convite pendente" e "expira em" | #181 |
| "Redefinir senha" na linha Senha, com o link de 24 h | #181 |
| "Convidado por Aldovrando Oliveira em 12/08/2026" | #181 (quem convidou só existe com o convite) |
| Papel (coluna e campo), descrição do papel, "Alterações salvas. O novo papel vale…" | #184, #185 |
| "Admin exige verificação em 2 etapas…" | #220, #184 |
| linhas Google e "SSO Empresa", "Desconectar", "Conectada em" e "Último uso" por forma, selo "No provedor" | M3 |
| "Redefinir verificação" | #218 |
| "Identidade" no menu | #211 |

### D2. Toda troca do stamp revoga as autorizações da pessoa

**O problema.** O stamp derruba a renovação seguinte (#180 D4), mas não toca as tabelas do OpenIddict. A autorização continua `valid`, e o refresh continua `valid` e não vencido. A lista de sessões vivas (D4) é uma consulta a essas tabelas, e o stamp não está em coluna nenhuma: ele vai dentro do JWT do refresh, que não é gravado (o servidor não usa refresh por referência, #180 D3.7). Então **uma sessão morta pelo stamp aparece viva na lista por até 14 dias.** Hoje isso já acontece na redefinição das 2 etapas da #218 (`UserTwoFactorResetEndpoint` troca o stamp e não revoga).

**A decisão:** trocar o stamp e revogar as autorizações andam juntos, numa transação, num método só (`UserSessions.EndAllAsync`), usado por:

- trocar a senha (#2);
- desativar (#9);
- **a redefinição das 2 etapas da #218, corrigida aqui** (P4). É uma linha no endpoint e um caso no teste dela.

A transação é a do `AppDbContext` do endpoint. Ou grava tudo, ou nada.

**A revogação é um `ExecuteUpdateAsync` próprio, e não o `RevokeBySubjectAsync`** (ajuste feito ao escrever as specs, 01/11/2026). O SQL é o do store (H5) com uma condição a mais, `status = valid`, sobre a entidade que o próprio `AppDbContext` mapeia (`openiddict_authorizations`). Dois motivos:

- **a contagem:** o `RevokeBySubjectAsync` devolve quantas linhas do subject casaram, inclusive as já revogadas e as de logins abandonados. As rotas #5 e #11 respondem `{ encerradas }` (contrato publicado na #183), e o número tem de ser o de autorizações que estavam `valid`;
- **um caminho só para "todas" e "as outras":** a #5 já precisava do comando próprio (H5 não tem "menos uma"), e as duas formas ficam no mesmo método, com e sem `id <> @atual`.

A medição da #209 (0,5 a 5,7 ms) vale: é o mesmo `UPDATE` por `subject`, com uma condição a mais na mesma linha. E o comando entra na transação do endpoint pelo mesmo motivo do H6: roda no `AppDbContext` do escopo.

**Por que não o contrário** (a lista filtrar as sessões mortas pelo stamp): exigiria gravar o stamp na autorização, em `properties`, e comparar na consulta. É uma segunda fonte de verdade sobre o mesmo fato, para evitar uma chamada de 0,5 a 5,7 ms (#209).

**O inverso não vale:** encerrar sessões (#4, #5, #11) **não** troca o stamp. O stamp é "a credencial ou o estado mudou"; encerrar sessão não muda nenhum dos dois. O que sobra sem o stamp é o cookie `buteco-sessao` em voo (até 5 min, só no navegador que acabou de entrar), que pode abrir uma autorização nova. Aceito: quem tem o cookie tem a senha, e a resposta a uma senha vazada é trocá-la (#2) ou a redefinição por link (#181), que trocam o stamp.

### D3. (c) Trocar a própria senha

- **Exige a senha atual**, conferida pelo `PasswordHasher` como no login (inclusive o hash legado do bootstrap). **Senha atual errada conta no bloqueio do e-mail** (`LoginThrottleStore.RecordFailureAsync`), como o `setup` da #218. Durante o bloqueio, `423` com `Retry-After`, sem conferir nada. Um access token roubado (15 min) não testa senhas sem limite.
- **Acerto não zera a contagem.** Zerar é do login completo (#218 D7); aqui a pessoa já está dentro, e zerar abriria o ciclo "acerta aqui, erra no login".
- **Política** (`PasswordPolicy`, nova, no `Users/Passwords/`): 12 a 128 caracteres pelo `string.Length`, sem `Trim` (espaço é caractere de senha), sem regra de composição. A nova igual à atual é aceita: recusar exigiria uma verificação Argon2 a mais para um ganho nulo. `400` com a mensagem da regra. A #181 usa a mesma classe na criação de senha do convite e da redefinição.
- **Hash novo em Argon2id**, e o marcador de legado sai.

**O que acontece com as sessões: duas opções.**

| | (A) troca o stamp e revoga todas, inclusive a atual | (B) revoga as outras e mantém a atual |
|---|---|---|
| a sessão atual | morre na próxima renovação (≤ 15 min); a autorização já sai revogada | continua |
| cookie em voo (5 min) noutro navegador | recusado no authorize (stamp) | **abre sessão nova** |
| código emitido e não trocado (5 min) | recusado (stamp e autorização) | recusado (autorização) |
| regra já escrita | "trocar a senha troca o stamp" (corpo da #219, #180 D4) | contraria o corpo |
| mecanismo | o mesmo da desativação e da redefinição (D2) | uma variante: revogar "menos a atual", sem stamp |
| custo no painel | depois do `204`, a #183 encerra a sessão (logout com `id_token_hint`) e leva ao `/entrar` com "Senha alterada. Entre com a nova senha." Sem isso, a pessoa cai no `401` numa renovação qualquer dos 15 minutos seguintes | nenhum |

**Recomendada: (A)** (P3). É a regra que a issue já decidiu, fecha o cookie em voo, e reaproveita o D2. O custo é a #183 tratar o `204` como "sair e entrar de novo", e entra no registro da #183. Isso responde à decisão em aberto da issue: **trocar a senha encerra todas as sessões da pessoa, inclusive a atual.**

### D4. (d) Sessões

**O que é uma sessão:** uma linha de `openiddict_authorizations` com `subject` = id do usuário, `type = ad-hoc` e `status = valid`, **que tenha um refresh vivo**: uma linha de `openiddict_tokens` da mesma autorização com `type = refresh_token`, `status = valid` e `expiration_date > now`. Com o D2, o "vivo" do banco coincide com o "renova" do emissor.

- **Por que o refresh vivo, e não só a autorização `valid`:** a autorização de um login abandonado (código nunca trocado) ou de uma sessão parada por mais de 14 dias continua `valid` até a limpeza (F11). Não renova, e não é sessão.
- **O que mostrar:**
  - **`criadaEm`** = `creation_date` da autorização, que é o login;
  - **`ultimoUso`** = a maior `creation_date` dos tokens da autorização (H4), que é a última renovação. **Não precisa de campo novo.** A granularidade é a do `last_access_at` (15 min), pelo mesmo motivo (#180 D3.11);
  - **`atual`** (só em Minha conta) = o id da autorização é o `oi_au_id` do access token de quem chama.
- **IP e navegador ficam de fora.** O `apps/api` só vê o IP do nginx do stack (#221), e o navegador não é pedido em nenhum quadro. Se a #183 quiser rotular as sessões, o registro na #183 diz que o único rótulo disponível é a data.
- **Ordem:** `criadaEm` decrescente, desempate pelo id (`api-response-ordering`).
- **Volume:** ~15 autorizações por usuário no estoque (#209, "Volume esperado"), e as consultas são por `subject`. O índice das autorizações não começa pelo `subject` (H8), e a #209 mediu a revogação por subject entre 0,5 e 5,7 ms com 150.000 linhas. A lista é a mesma varredura com um `EXISTS` por linha, na FK `authorization_id`, que é indexada.

**A sessão atual, a partir do access token:**

- o access token traz o `oi_au_id` (H1, H2), e ele é estável entre renovações (H3);
- **mas ele não chega ao principal do `apps/api`:** o `MapPrincipalByClient` reconstrói o principal só com `NameIdentifier`, `client_id` e `sub` (`IssuerTokenAuthentication.cs` l. 94–110). **Muda aqui:** o `oi_au_id` é copiado como está, ao lado do `sub`. É uma linha, e não muda a regra do subject;
- o HMAC para no `CurrentUser` (`403`) antes de chegar aqui. Um token de usuário sem o claim não é esperado (H1); se vier, nenhuma sessão sai `atual`, e o `revoke-others` responde `400` "Não foi possível identificar a sessão atual." em vez de encerrar todas.

**Encerrar:**

| operação | como | por quê |
|---|---|---|
| uma (#4) | `FindByIdAsync` + conferência de `subject` = quem chama + `TryRevokeAsync` (H5) | uma linha; a conferência do subject faz a sessão de outra pessoa responder `404`, como a inexistente |
| as outras (#5) | `ExecuteUpdateAsync` no `Set<OpenIddictEntityFrameworkCoreAuthorization>()` do `AppDbContext`: `subject = @usuário`, `status = valid` e `id <> @atual` (D2) | o OpenIddict não tem "todas menos uma" (H5). Um comando só, sem laço |
| todas de outro (#11) | o mesmo comando, sem `id <> @atual` (`UserSessions.EndAllAsync`, D2) | #180 D3.10, com a contagem só das `valid` |

- **Encerrar a própria sessão atual pela #4 é permitido** (P2): a renovação seguinte cai, como num logout sem o redirecionamento. A #183 decide se oferece; a alternativa, `409` "use Sair", criaria uma regra sem ganho de segurança.
- **A #11 no próprio usuário também é permitida:** encerra todas, inclusive a atual. Não abre nada que a #5 mais o logout não abram.
- **O access token já emitido vale até 15 minutos** depois de qualquer encerramento, como no logout e na desativação (#180 D3.10, aceito na M1).

**O detalhe de outro usuário** traz `sessoes: { ativas, ultimaAtividade }`, que é o que o quadro "Editar usuário" mostra ("2 sessões ativas · última atividade ontem, 18:40"). A lista das sessões de outro usuário não tem quadro e não entra (convenção 25).

### D5. (e) Editar usuário

- **`PUT /users/{id}` com `{ nome, email }`**, os dois obrigatórios, como o `PUT` de `McpServers`. Nome: sem espaços nas pontas, 1 a 200 caracteres. E-mail: o mesmo tratamento do bootstrap (`Trim`), formato conferido por `MailAddress.TryCreate`, até 254 caracteres.
- **Unicidade:** o e-mail é normalizado (`EmailNormalization.Normalize`) e conferido contra o índice único de `normalized_email`. Um usuário com outro id e o mesmo e-mail normalizado → `409` "Já existe um usuário com este e-mail.". **A corrida** (duas edições ou uma edição e uma criação simultâneas) cai no índice: `DbUpdateException` com `23505` naquele índice vira o mesmo `409`, no molde do `CreateKnowledgeBaseCommandHandler` (`IsFolderUniqueViolation`).
- **Mudar só a caixa do próprio e-mail** (`Ana@x` → `ana@x`) não colide consigo mesmo.
- **O `sub` não muda** (#180 D4).
- **Não troca o stamp.** O e-mail é o identificador do login, e não um segredo. Quem entrou com a senha certa continua sendo a mesma pessoa, e um admin corrigindo um e-mail digitado errado não deveria derrubar as sessões de ninguém. A próxima entrada usa o e-mail novo.
- **Trocar o próprio e-mail não exige a senha atual** (decisão em aberto da issue; P5). Na M1 não há recuperação por e-mail: o link de redefinição da #181 é gerado e mostrado ao admin, e não enviado. Com um access token roubado, trocar o e-mail da vítima não dá a conta a ninguém (a senha continua sendo dela), só a atrapalha, e qualquer usuário da M1 já pode fazer o mesmo com qualquer outro (`/users` sem papéis). **Gatilho para passar a exigir a senha atual** (decisão do dono, 01/11/2026): **a primeira funcionalidade que envie e-mail ao endereço cadastrado**, seja a redefinição de senha ou o convite por e-mail. Daí em diante, quem troca o e-mail passa a controlar para onde vai a recuperação, e a troca vira caminho de tomada de conta. O gatilho fica registrado na #181.
- **A linha do bloqueio é do e-mail antigo.** Trocar o e-mail de um usuário bloqueado o desbloqueia na prática. Aceito: só quem já está dentro troca o e-mail, e a linha antiga vence sozinha.
- **O rótulo do app autenticador** (#218 D8): o URI do cadastro levou o e-mail da época. **Só o rótulo muda**: o segredo continua, os códigos continuam valendo, e o app mostra o e-mail antigo até a pessoa recadastrar. Nada a fazer no `apps/api`; o registro na #183 cita.
- **O nome e o e-mail no painel.** O painel lê os dois do id token (Context), e o id token de cada renovação sai do principal do refresh, que é o do login (H7). **Sem mudança, o painel mostra o nome antigo até o próximo login**, que pode não acontecer nunca com o refresh deslizante. Duas saídas (P6):
  - **(A, recomendada)** o `UserGrantTokenHandler`, que já relê o usuário em toda renovação, troca no principal os claims `name` e `email` pelos vigentes (H7). O id token da renovação seguinte (≤ 15 min) já sai certo, e o refresh novo também. Cerca de 10 linhas e um caso de teste, sem consulta nova;
  - **(B)** a #183 lê o `GET /account` em vez do id token. Deixa o id token com um dado velho que o painel continua lendo para o cabeçalho.

### D6. (f) Listar e detalhe

**`UserSummary`** (lista):

| campo | fonte |
|---|---|
| `id` | `id` |
| `nome`, `email` | `name`, `email` |
| `estado` | `ativo` / `desativado` (`status`) |
| `formasDeEntrada` | `["senha"]` quando `password_hash` não é nulo; `[]` sem senha. Na M1 todo usuário tem senha; o campo existe porque a coluna do protótipo existe, e a M3 o estende |
| `duasEtapas` | `two_factor_enabled_at` não nulo (#218 D9) |
| `ultimoAcesso` | `last_access_at` |
| `criadoEm` | `created_at` |

**`UserDetail`** = o resumo + `desativadoEm` + `sessoes: { ativas, ultimaAtividade }` (D4). O `duasEtapasAtivadaEm` não entra: nenhum quadro o mostra.

- **Ordem:** nome, com desempate pelo id (`api-response-ordering`). O guarda do desempate segue a quinta forma da convenção 15: o comportamental mais a asserção sobre o SQL emitido.
- **Sem filtro por estado** (P7). O protótipo mostra ativos e desativados juntos, sem filtro. Filtro sem consumidor é o que a convenção 25 manda não declarar; com o volume abaixo, a #183 filtra no cliente se quiser.
- **Sem paginação.** O volume esperado é o das premissas da #209: M = 10 a 50 usuários, cada linha com ~200 bytes no fio, ~10 KB no pior caso. A paginação entra com um gatilho: uma instalação acima de 500 usuários.
- **Uma consulta só, sem N+1:** a lista lê só a tabela `users`. O detalhe faz duas: o usuário e a contagem de sessões (D4).

### D7. (b) Desativar e reativar

**Desativar:**

- `status = deactivated`, `deactivated_at`, stamp novo (`User.Deactivate`, que já existe) e a revogação do D2, numa transação;
- **a si mesmo → `403`** "Você não pode desativar o próprio usuário." (P8). Sem tela de Minha conta que o peça, a desativação de si só serve para o erro de quem clica na própria linha, e numa instalação de um usuário só ela trancaria todo mundo do lado de fora, sem bootstrap que salve (o bootstrap só age com a tabela vazia, #180 D2);
- **o último usuário ativo:** com a recusa a si mesmo, quem chama está ativo (`CurrentUser`) e é outra pessoa, então depois da desativação sobra pelo menos um ativo: quem chamou. **A regra do último ativo decorre da recusa a si mesmo, e não vira uma checagem própria**, que seria código sem caminho que a alcance (convenção 25);
- **mas só se quem chama ainda estiver ativo quando grava.** A corrida, com A e B os dois únicos ativos:

```
   A: POST /users/B/deactivate        B: POST /users/A/deactivate
   CurrentUser: A ativo ✓             CurrentUser: B ativo ✓
   alvo B ≠ A ✓                       alvo A ≠ B ✓
   grava B desativado                 grava A desativado
   ────────────────────────────────────────────────────────────
   resultado sem lock: ninguém ativo
```

**A decisão:** a desativação roda sob `pg_advisory_xact_lock(UserAdministration.AdvisoryLockKey)`, no molde do `IdentityClientCatalog` (l. 43–50), e, **dentro do lock**, relê quem chama e o alvo. Quem chama já desativado → `403` do `CurrentUser`. Com o lock, a segunda desativação espera a primeira confirmar e relê quem chama já desativado:

```
   A: lock ─ relê A ativo ─ grava B ─ commit (solta)
   B:        lock (espera) ─────────────────────────── relê B: desativado → 403
```

- **Por que o lock, e não `SERIALIZABLE` ou `SELECT … FOR UPDATE`:** a casa já usa `pg_advisory_xact_lock` em quatro lugares do `apps/api` (três do emissor e o bootstrap), com chave `bigint` de bytes ASCII. `FOR UPDATE` nas duas linhas (quem chama e o alvo) também serve, mas precisa de ordem fixa para não travar em deadlock (A trava A e espera B; B trava B e espera A). `SERIALIZABLE` faria a segunda falhar com `40001` e exigiria repetição. O lock é um ponto só, e a desativação é rara.
- **A chave nova é `BUTECOD1`** ("D" de desativação): os bytes ASCII lidos como inteiro big-endian, na forma `bigint`, como as outras. **`BUTECOU1` já está em uso** pelo `UserBootstrapper` (l. 49–55), conferido em 01/11/2026; a primeira redação deste design a propunha, e colidiria. As chaves do `apps/api`, conferidas por `grep` do prefixo `4779819235982` em `apps/` e `tests/`:

  | sigla | valor | dono |
  |---|---|---|
  | `BUTECOC1` | 4779819235982394161 | `IdentityClientCatalog` (sincronização do catálogo) |
  | **`BUTECOD1`** | **4779819235982394417** | **`UserAdministration` (desativação, esta change)** |
  | `BUTECOK1` | 4779819235982396209 | `IdentityKeyRotationPolicy` (rotação e carga do anel) |
  | `BUTECOP1` | 4779819235982397489 | `IdentityTokenPrunePolicy` (limpeza) |
  | `BUTECOU1` | 4779819235982398769 | `UserBootstrapper` (criação do primeiro Admin) |

  Os locks de dois `int4` (`pg_advisory_lock(hashtext(…), hashtext(…))`, delegação e `apps/workers`) vivem noutro espaço (`objsubid = 2` contra `1` do `bigint`), como os comentários das chaves já registram. Um teste de unidade afirma que as cinco constantes são distintas e que cada uma é a leitura da sua sigla.
- **A #184 herda o lock** para "o último Admin não pode ser desativado nem rebaixado", que aí sim é uma checagem própria (registro na #184).

**Reativar:**

| volta | não volta |
|---|---|
| `status = active`; `deactivated_at = null`; o login com a senha, que é a mesma; as 2 etapas como estavam (desativar não as toca); `last_access_at` e `created_at` como estavam | **as sessões antigas**: as autorizações saíram revogadas (D2), e os refresh de antes trazem o stamp antigo, que a desativação trocou e a reativação não devolve (#180 D4). A pessoa entra de novo |

- Reativar **não troca o stamp de novo**: o da desativação já invalidou tudo, e uma troca a mais não fecha nada.
- Reativar não precisa do lock: não reduz o número de ativos.
- A linha do bloqueio por e-mail não muda em nenhum dos dois.

### D8. (g) Quem pode chamar

- **Na M1, todo usuário real chama todas as rotas de `/account` e de `/users`**, pelo `CurrentUser`. O HMAC de operador, o `sub` inexistente e o usuário desativado com access token ainda válido recebem `403`, como na #218. É o "todo usuário da M1 é Admin em poder" (#180 D4).
- **Não há classificação nova** no `RouteAuthenticationClassification`: as rotas são autenticadas por padrão.
- **O que a #184 restringe**, registrado nela depois do ok do dono:
  - `/users/*` (as 6 rotas daqui e a redefinição da #218) passam a exigir **Admin**;
  - **`/account/*` continua para todo papel**: é a própria pessoa (senha, sessões, 2 etapas);
  - "o último Admin não pode ser desativado nem rebaixado" usa o lock do D7, com a contagem de Admins ativos dentro dele;
  - a recusa de desativar a si mesmo continua;
  - "papel novo vale na próxima requisição": o `UserGrantTokenHandler` já relê o usuário em toda renovação, e o mesmo ponto do D5 (A) pode levar o papel ao principal. A decisão em aberto da #184 sobre isso continua dela;
  - editar o próprio nome e e-mail: com `/users` só para Admin, um Operador não edita o próprio nome. Se a #185 quiser isso em Minha conta, é rota nova em `/account`, e não exceção em `/users`.

### D9. Forma do código

- **Classes estáticas de endpoint com o `AppDbContext`**, como a área `Users/` (`AccountTwoFactorEndpoints`, `UserTwoFactorResetEndpoint`), e não o par comando/consulta com `Mediator`. As regras daqui dependem do principal (quem chama, a sessão atual), e o `CurrentUser` já é chamado no endpoint pela #218. Dois estilos na mesma área seriam o pior dos dois.
- Três arquivos de endpoint (`AccountEndpoints`, `AccountSessionEndpoints`, `UserEndpoints`) e duas peças de apoio, cada uma com um consumidor real a mais de um (convenção 2):
  - **`UserSessions`**: a consulta das vivas e as três revogações, usada por Minha conta, pelo detalhe, por `/users/{id}/sessions/revoke`, pela troca de senha, pela desativação e pela redefinição da #218;
  - **`PasswordPolicy`**: usada aqui e, depois, pela #181.

### D10. (h) Testes

Os guardas de cada linha são verificados contra o defeito (convenção 15): o defeito entre parênteses é reintroduzido, o teste reprova **no componente que a correção toca**, e a árvore volta.

**`apps/api`, unidade:**

| teste | o que afirma | guarda verificado contra |
|---|---|---|
| `PasswordPolicyTests` | 11 e 129 recusados; 12 e 128 aceitos; espaços contam; sem regra de composição | limite com `<` no lugar de `<=` (o caso dos 128) |
| `IssuerPrincipalMappingTests` | o principal do token do emissor guarda `oi_au_id` ao lado do `sub`; `NameIdentifier` continua pela tabela de clientes | cópia do `oi_au_id` retirada |
| `AdvisoryLockKeysTests` | as cinco chaves de `pg_advisory_xact_lock` do `apps/api` são distintas, e cada uma é a leitura big-endian da sua sigla | a chave da desativação igual à do bootstrap (`BUTECOU1`, a colisão da primeira redação) |

**`apps/api`, integração (Testcontainers, `TestServer`, o fluxo real de login da `IdentityIssuerTestClient`):**

| teste | o que afirma | guarda verificado contra |
|---|---|---|
| `AccountProfileTests` | `GET /account` devolve os dados de quem chama; HMAC → `403` | — |
| `AccountPasswordTests` | sem a atual ou com a errada → `401`, contada; a quinta falha bloqueia, e a sexta, com a atual certa, → `423` sem conferir; nova fora da política → `400`; certo → `204`, o login com a antiga → `401` e com a nova → `204`; **toda renovação da pessoa, inclusive a da sessão que trocou, → `invalid_grant`; a lista de sessões fica vazia** | troca do stamp retirada (a renovação passa); revogação retirada (a lista mostra as sessões) |
| `AccountSessionsTests` | dois logins → duas sessões, a do token marcada `atual`; `ultimoUso` muda depois de uma renovação; autorização sem refresh (código não trocado) não aparece; refresh vencido (`FakeTimeProvider`, 14 dias) não aparece | filtro do refresh vivo retirado (o login abandonado aparece) |
| | encerrar uma → some da lista, e a renovação dela → `invalid_grant`; a de outra pessoa → `404`, e ela continua viva | conferência do `subject` retirada (a de outra pessoa é revogada) |
| | encerrar as outras → só a atual fica, e ela renova | `id <> @atual` retirado (a atual cai) |
| `UserListTests` | campos, `duasEtapas` pela coluna, ordem por nome com desempate pelo id; **o SQL emitido termina o `ORDER BY` no id** (log do `Executed DbCommand`) | `ThenBy` do id retirado (a asserção do SQL reprova em toda execução) |
| `UserEditTests` | nome e e-mail gravados, o `sub` igual; o login entra pelo e-mail novo e não pelo antigo; e-mail de outro em outra caixa → `409`; a própria caixa trocada → `200`; inválido → `400`; inexistente → `404`; **as sessões continuam renovando** | conferência por `email` em vez de `normalized_email` (a outra caixa passa e o índice responde `500`) |
| | **duas edições simultâneas para o mesmo e-mail** (`Task.WhenAll`, 20 rodadas) → um `200` e um `409`, nunca `500`; **e o caso determinístico**: um `DbCommandInterceptor` de teste segura o `UPDATE` de uma edição depois da pré-checagem, a outra grava o e-mail, e a primeira recebe `409` pelo índice | `catch` do `23505` retirado (o determinístico responde `500` em toda execução; o comportamental registra quantas rodadas reproduziram)
| `UserDeactivationRoutesTests` | desativar outro → `200`, a renovação dele → `invalid_grant`, o login → `403`, a lista dele vazia; desativar de novo → `200` sem trocar o stamp; a si mesmo → `403`; inexistente → `404` | revogação retirada (o detalhe mostra sessões) |
| | **A e B, únicos ativos, desativando um ao outro ao mesmo tempo** → um `200` e um `403`, e sobra um ativo; **o SQL emitido traz o `pg_advisory_xact_lock` antes do `UPDATE`** | lock retirado (a asserção do SQL reprova sempre; a corrida pode passar por sorte, quinta forma da convenção 15) |
| | reativar → `200`; o refresh de antes da desativação → `invalid_grant`; o login com a mesma senha → `204` e a sessão nova renova; as 2 etapas continuam ligadas | reativação que devolve o stamp antigo |
| `UserSessionsAdminTests` | o detalhe conta as sessões vivas e a última atividade; encerrar todas de outro → as renovações dele `invalid_grant`, `encerradas` = 2 | — |
| `UserRoutesCallerTests` | HMAC `operator` → `403` nas 11 rotas, e nada é gravado (alvo com o mesmo stamp e as mesmas sessões); usuário desativado com access token válido → `403` | rota sem o `CurrentUser` (o caso do HMAC numa rota de escrita) |
| `UserGrantClaimsRefreshTests` | depois de editar o nome, o id token da renovação seguinte traz o nome novo | troca dos claims retirada |
| `TwoFactorAdminResetTests` (existente, estendido) | depois da redefinição, a lista de sessões do alvo fica vazia | revogação retirada da redefinição |

A divisão final por classe (o detalhe, a reativação e a contagem do `encerradas` em classes próprias) e todos os guardas, com o defeito nomeado, estão no `tasks.md`.

**`tests/`:** sem mudança. O `InboxOrchestratorRoundTrip.Tests` e o `ApiConnectorsRoundTrip.Tests` rodam na baseline e no fechamento, porque o principal do `apps/api` muda (D4) e a renovação também (D5). O `CrossAppTaskStoreCompatibility.Tests` fica de fora: esta change não toca a cifra.

**`apps/frontend`:** sem mudança de código e sem teste novo. Entra na baseline só como referência; o `nginx.conf` não muda.

## Árvore de pastas proposta

```
apps/api/src/Buteco.Api/
├── Users/
│   ├── User.cs                                   + ChangePassword (hash, stamp), Activate, Rename/ChangeEmail
│   ├── UserAdministration.cs                     NOVO: AdvisoryLockKey (BUTECOD1) e a desativação sob o lock (D7)
│   ├── Account/                                  NOVO
│   │   ├── AccountEndpoints.cs                   GET /account, POST /account/password (D1, D3)
│   │   └── AccountSessionEndpoints.cs            GET /account/sessions, …/{id}/revoke, …/revoke-others (D4)
│   ├── Management/                               NOVO
│   │   ├── UserEndpoints.cs                      GET /users, GET/PUT /users/{id}, activate, deactivate, sessions/revoke (D1, D5–D7)
│   │   └── UserResponses.cs                      UserSummary, UserDetail
│   ├── Sessions/                                 NOVO
│   │   └── UserSessions.cs                       vivas, última atividade, revogar uma / as outras / todas (D2, D4)
│   ├── Passwords/PasswordPolicy.cs               NOVO: 12 a 128 (D3)
│   ├── TwoFactor/UserTwoFactorResetEndpoint.cs   + revoga as autorizações (D2)
│   └── UserServiceCollectionExtensions.cs        + UserSessions
├── Identity/
│   ├── Validation/IssuerTokenAuthentication.cs   + oi_au_id no principal (D4)
│   └── Flow/UserGrantTokenHandler.cs             + name/email vigentes no principal da renovação (D5)
└── Program.cs                                    + MapAccountEndpoints, MapAccountSessionEndpoints, MapUserEndpoints

apps/api/tests/Buteco.Api.Tests/
├── Users/Account/                                NOVO: AccountProfileTests, AccountPasswordTests, AccountSessionsTests
├── Users/Management/                             NOVO: UserListTests, UserEditTests, UserDeactivationRoutesTests,
│                                                       UserSessionsAdminTests, UserRoutesCallerTests
├── Users/Passwords/PasswordPolicyTests.cs        NOVO
├── Users/TwoFactor/TwoFactorAdminResetTests.cs   + a lista vazia depois da redefinição
├── Identity/IssuerPrincipalMappingTests.cs       NOVO
├── Identity/UserGrantClaimsRefreshTests.cs       NOVO
└── Support/                                      + login de um segundo usuário e leitura do oi_au_id, se a IdentityIssuerTestClient não tiver

01-ARQUITETURA_E_CONVENCOES.md (Autenticação), 02-HISTORICO_E_STATUS.md, CHANGELOG.md, docs/architecture.md
```

**Nada vai para `libs/`:** um consumidor só, o `apps/api`. **Nenhuma migration** (as colunas existem desde a #180 e a #218), **nenhum pacote novo**, **nenhuma variável de configuração**. Só o `apps/api` tem código; os outros apps não mudam.

## Tamanho e divisão

**Âncoras medidas** (convenção 18), só código, sem `openspec/` nem `.md`, sem migrations, por `git diff --numstat`:

| PR | issue | produção | testes | projetado → entregue |
|---|---|---|---|---|
| #224 | #180 | +2.036 / −141, 45 arquivos | +2.984 / −237, 44 arquivos | +57 % / +24 % |
| #228 | #217 | +1.389 / −381, 49 arquivos | +2.264 / −464, 48 arquivos | +14 % / +20 % |
| #229 | #218 | **+1.203 / −161, 28 arquivos** (`86abb5d..83023e0`, medido hoje) | **+1.473, 18 arquivos** | 1.070 / 1.740 → **+12 % / −15 %** |

**Projeção por componente** (rascunho, convenção 18; com P1 a P8 decididas):

| componente | produção | testes |
|---|---|---|
| Minha conta: perfil e senha (#1, #2), política | ~200 | ~300 |
| sessões: consulta, três revogações, `oi_au_id`, rotas #3–#5 | ~250 | ~400 |
| Usuários: lista, detalhe, edição, respostas (#6–#8) | ~280 | ~350 |
| desativar, reativar, lock, encerrar de outro (#9–#11) | ~200 | ~300 |
| stamp + revogação juntos, a correção da #218 (D2) | ~30 | ~40 |
| claims na renovação (D5) | ~20 | ~60 |
| apoio de teste | — | ~100 |
| **total** | **~980** | **~1.550** |

Com o erro da #218 (+12 % / −15 %, a âncora mais parecida: mesma área, mesmo estilo), fica em ~1.100 / ~1.300; com o pior da série (+57 % / +24 %, a #180), ~1.540 / ~1.920. **Cabe numa change**, na faixa da #218 e da #217. Não proponho divisão.

**Divisão possível, registrada e não recomendada**, na régua da #180: (1) Minha conta (#1–#5, o `oi_au_id`, a política) e (2) Usuários (#6–#11, o lock). As duas compartilham o D2 e o `UserSessions`, então a (1) carregaria a peça inteira e a (2) a consumiria, e a #183 depende das duas.

## Risks / Trade-offs

- **[Sessão morta pelo stamp aparecendo viva]** → o D2 junta a troca do stamp e a revogação, inclusive na redefinição da #218. Coberto por `AccountPasswordTests`, `UserDeactivationRoutesTests` e `TwoFactorAdminResetTests` (o caso da redefinição: com a revogação retirada do `UserTwoFactorResetEndpoint`, a lista do alvo mostra as sessões e o caso reprova).
- **[Corrida de duas desativações deixando a instalação sem ativo]** → o lock do D7, com o par determinístico sobre o SQL. Coberto por `UserDeactivationRoutesTests`.
- **[O `ExecuteUpdate` do "as outras" replica o filtro do store do OpenIddict]** → é a entidade e a tabela que o `AppDbContext` já mapeia, e um upgrade que mude o significado de `status` reprova o `AccountSessionsTests` ("as outras" encerradas não renovam; a atual renova).
- **[O `oi_au_id` deixar de vir no access token num upgrade]** → H1 é incondicional na 7.7.2; o `IssuerPrincipalMappingTests` e o caso `atual` do `AccountSessionsTests` reprovam se o claim sumir.
- **[Trocar a senha derruba a sessão atual]** → é a escolha (A) do D3; a #183 encerra a sessão logo depois do `204`. Sem isso, a pessoa cai no `401` em até 15 min. Registro na #183.
- **[Qualquer usuário da M1 desativa, edita e encerra as sessões de outro]** → é o "Admin em poder" da #180 D4; a #184 restringe (D8).
- **[O e-mail trocado desbloqueia a conta bloqueada]** → só quem está dentro troca, e a linha antiga vence sozinha (D5).
- **[Rótulo antigo no app autenticador]** → só o rótulo (D5, #218 D8).
- **[Access token válido por até 15 min depois de encerrar ou desativar]** → aceito na M1 (#180 D3.10).
- **[Sem paginação]** → gatilho de 500 usuários (D6).

## Migration Plan

1. **Deploy:** só o `apps/api`. Sem migration, sem variável, sem mudança no compose nem no nginx.
2. **Conferir:** `GET /users` com o token do painel → `200` com a lista; com o HMAC, se ainda houver algum → `403`; `GET /account/sessions` → a sessão do navegador marcada `atual`.
3. **Rollback:** a imagem anterior do `apps/api`. Nada de schema a desfazer. As autorizações revogadas por esta versão continuam revogadas, o que é o efeito pedido.

## Registros (convenção 23)

Publicados em 01/11/2026, com o ok do dono:

- **#184** ([comentário](https://github.com/aldovrando-oliveira/buteco-agentes/issues/184#issuecomment-6567558887)): o que ela restringe (D8): `/users/*` só para Admin, `/account/*` para todos, o último Admin dentro do lock `BUTECOD1` do D7, e o "papel na próxima requisição" pelo ponto do D5.
- **#183** ([comentário](https://github.com/aldovrando-oliveira/buteco-agentes/issues/183#issuecomment-6567559145)): o contrato das 11 rotas; trocar a senha encerra todas as sessões, inclusive a atual, e a tela sai e volta ao `/entrar` depois do `204` (D3); o único rótulo de sessão é a data (D4); o rótulo antigo do app autenticador depois de trocar o e-mail (D5); as colunas do protótipo sem fonte (D1).
- **#181** ([comentário](https://github.com/aldovrando-oliveira/buteco-agentes/issues/181#issuecomment-6567559376)): a `PasswordPolicy`; o precedente para "concluir a redefinição encerra as sessões?" (D2, D3); a unicidade entre usuários e convites continua dela; o gatilho do P5 (D5).
- **#221** ([comentário](https://github.com/aldovrando-oliveira/buteco-agentes/issues/221#issuecomment-6567559622)): a lista de sessões não mostra IP; com a #221, o IP precisaria de coluna própria.
- **#218** ([comentário](https://github.com/aldovrando-oliveira/buteco-agentes/issues/218#issuecomment-6567559879), issue fechada): a redefinição trocava o stamp sem revogar, a sessão aparecia viva por até 14 dias, e a correção da #219 com o caso e o guarda (D2).

## Decisões do dono (01/11/2026)

Na revisão do proposal e do design, o dono aceitou **P1 a P8 como recomendadas**:

| | pergunta | decisão | onde |
|---|---|---|---|
| P1 | Quantas operações? | **11**: o corpo lista "encerrar uma" e "encerrar todas" como duas, e "encerrar todas" vira "encerrar as outras" | D1 |
| P2 | Encerrar a própria sessão atual por "encerrar uma"? | permitido | D4 |
| P3 | O que trocar a senha faz com as sessões? | (A): troca o stamp e revoga todas, inclusive a atual; a #183 sai e volta ao `/entrar` depois do `204` | D3 |
| P4 | Toda troca do stamp revoga as autorizações? | sim, na mesma transação, inclusive na redefinição da #218, corrigida aqui | D2 |
| P5 | Trocar o próprio e-mail exige a senha atual? | não, até o gatilho do D5 (o primeiro envio de e-mail ao endereço cadastrado) | D5 |
| P6 | Como o nome e o e-mail editados chegam ao painel? | (A): a renovação troca os claims `name` e `email` pelos vigentes | D5 |
| P7 | Filtro por estado e paginação na lista? | nenhum dos dois; paginação com o gatilho de 500 usuários | D6 |
| P8 | Desativar a si mesmo? | `403`; o último ativo decorre disso sob o lock, sem checagem própria | D7 |

**Complementos da mesma revisão**, já incorporados:

- o gatilho do P5 no D5 e no registro da #181;
- o comentário na #218 sobre a redefinição que trocava o stamp sem revogar, e o caso da redefinição no teste do D2, com o guarda;
- a chave do lock documentada no formato das outras, com a colisão de `BUTECOU1` corrigida para `BUTECOD1` (D7).

## Open Questions

Nenhuma.

## Baseline

No worktree limpo `../buteco-agentes-219-baseline`, em `80f6c9a`, sem edição nenhuma (`git status` vazio). O script `~/.cache/buteco-agents/bin/suite-219.sh` (cópia do `suite-218.sh`, só o nome muda) espera, antes de cada suíte, `CPU_Speed_Limit = 100` e os loads de 1 e de 5 min abaixo do limite (6 no frontend, 8 nas outras), acerta o relógio da VM e registra o `podman ps`. As saídas completas, com o relatório por nome (`.trx` e o JSON do Vitest), ficam em `~/.cache/buteco-agents/219-baseline/`.

| suíte | resultado | duração | estado no início (load 1 / 5 min, CPU, `podman ps`) → fim |
|---|---|---|---|
| `apps/api` | **1102/1102 + 1 ignorado** | 298 s | 5,16 / 3,86, CPU 100, vazio → 22,47 / 20,31, **CPU 48** |
| `tests/InboxOrchestratorRoundTrip.Tests` | **12/12** | 53 s | 1,72 / 7,78, CPU 100, vazio → 3,17 / 7,22, CPU 100 |
| `tests/ApiConnectorsRoundTrip.Tests` | **8/8** | 24 s | 3,17 / 7,22, CPU 100, vazio → 5,52 / 7,45, CPU 100 |
| `apps/frontend` | **1978/1978**, 154 arquivos | 139 s | 2,74 / 5,70, CPU 100, vazio → 35,75 / 18,03, **CPU 55** |

- **Por nome:** o `apps/api`, o `rt-inbox` e o `rt-connectors` têm exatamente os mesmos testes, com o mesmo resultado, do fechamento da #218 (`compare-trx.py` contra `218-fechamento/*.trx`: 0 sem par, 0 novos).
- **A CPU caiu para 48 e 55 durante o `apps/api` e o `apps/frontend`**, sempre depois de largar em 100, como na #218. Os resultados não mudam; essas duas durações não servem de referência.
- **A primeira rodada do frontend saiu com `127`** (`vitest: command not found`): o worktree novo não tinha `node_modules`. Depois do `npm ci` (árvore rastreada continua vazia), a suíte rodou inteira. A saída da tentativa ficou em `219-baseline/frontend-1a/`.
- **O `CrossAppTaskStoreCompatibility.Tests` não entra**: esta change não toca a cifra (D10).
- `apps/inbox`, `apps/connectors` e `apps/workers` não são medidos: o código deles não muda.
