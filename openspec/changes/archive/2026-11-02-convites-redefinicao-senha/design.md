## Context

**Estado atual (`a4d3495`, merge do PR #230, 02/11/2026).** A #180, a #217, a #218 e a #219 estão em `main`:

- **`User`** (`Users/User.cs`, tabela `users`): `status` só tem `active` e `deactivated`. **`password_hash` já é anulável**, e o comentário da coluna já reserva o nulo para "convite pendente na #181" (#180 D4). `ChangePassword` troca o hash e o stamp; `Deactivate` troca o stamp; `Activate` não troca.
- **A conversão de `status`** (`AppDbContext.cs` l. 700–703) lê qualquer texto diferente de `active` como `Deactivated`. Um valor novo exige mudar os dois sentidos.
- **O login** (`Users/Login/SessionEndpoints.cs`): e-mail sem conta verifica contra o hash fictício (`PasswordHasher.VerifyAgainstDummy`), conta a falha e responde `401` "E-mail ou senha inválidos.". **Mas usuário com `password_hash` nulo cai no `PasswordHasher.Verify`, que devolve `Failed` na primeira linha, sem derivar nada** (`PasswordHasher.cs`, `if (string.IsNullOrEmpty(storedHash)) return Failed`). Hoje nenhum usuário tem senha nula; com o convite, o tempo de resposta diria "este e-mail tem convite pendente".
- **As barreiras do login** (`SessionRequestGuards`): só `application/json` (`415`) e `Origin` igual à do issuer ou à de `Identity:PanelBaseUrl` (`403`).
- **O limite global** (`LoginRateLimiting`): concorrência de 4 com fila de 16 e token bucket de 10/s, nas rotas marcadas com `LoginRateLimiting.Marker`. Existe pelo custo do Argon2 (~19 MiB e dezenas de ms por verificação).
- **O bloqueio por e-mail** (`LoginThrottleStore`): `GetLockRemainingAsync`, `RecordFailureAsync`, `ResetAsync`.
- **As rotas da #219** (`Users/Management/UserEndpoints.cs`): `CurrentUser` em todas; `PUT` com `409` pelo índice `IX_users_normalized_email`, inclusive na corrida; `activate` **aceita qualquer usuário não ativo e o põe ativo** (l. do `ActivateAsync`); `deactivate` sob o lock `BUTECOD1`.
- **A base do painel já tem uma regra**: `IdentityClientCatalog.PanelBase()` usa `Identity:PanelBaseUrl` e, sem ela, o issuer, que é `PublicUrl:BaseUrl` (obrigatória no boot desde a #180). É a mesma base dos endereços de retorno do `buteco-painel`.
- **Precedentes de segredo em hash e consumo atômico**: os códigos de recuperação da #218 são guardados como SHA-256 (`RecoveryCode.Hash`) e consumidos por `ExecuteUpdateAsync` condicional (`UsedAt == null`, `TwoFactorVerifier` l. 79–80).
- **Rotas anônimas** levam `AnonymousRouteClassification` (`AuthEntryPoint` para as de entrada).

**O que a issue e os registros já decidiram:**

| decisão | de onde |
|---|---|
| convite: nome e e-mail; link de uso único, 72 h, só o hash guardado, mostrado uma vez; "Gerar novo link" invalida o anterior; "Cancelar convite" invalida sem gerar outro; e-mail travado no aceite | corpo da #181 |
| redefinição: o admin gera link de uso único de 24 h; a senha antiga vale até a nova ser criada | corpo da #181 |
| sem envio de e-mail; sem entrada automática, nem por domínio; provedor federado nunca cria usuário | corpo da #181 |
| o papel no convite chega na M2 (#184, #185) | corpo da #181 |
| política de senha: 12 a 128 pelo comprimento, sem `Trim`, sem composição (`PasswordPolicy`) | #219 D3, comentário na #181 |
| toda troca do stamp revoga as autorizações da pessoa na mesma transação (`UserSessions.EndAllAsync`) | #219 D2 |
| o `409` do `PUT /users/{id}` confere só `users`; a unicidade com convites pendentes fica com a #181 | comentário da #219 na #181 |
| gatilho do P5: a primeira funcionalidade que **envie e-mail ao endereço cadastrado** passa a exigir a senha atual na troca do próprio e-mail | #219 D5, decisão do dono de 01/11/2026 |
| quem chama é o usuário real (`CurrentUser`); HMAC → `403`; todo usuário da M1 é Admin em poder | #218 D9, #219 D8, #180 D4 |
| usuário é desativado, nunca excluído | corpo da #180 |

**Decisões em aberto da issue:** convite expirado aparece como "expirado" ou some (D5, P4); concluir a redefinição encerra as sessões (D7: sim, pela #219); unicidade do e-mail entre usuários e convites pendentes (D2).

**O protótipo** (canvas `claude.ai/artifact/H7dPBZafiSkdKd1hqesPZ6`, versão `1793572637-a8aa`, lido em 02/11/2026 pela ferramenta Artifact):

- **"Usuários — convite com link para copiar"** (`project/Usuarios.dc.html`):
  - "Convidar usuário" abre um formulário no card (Nome, E-mail, Papel) com "O Buteco gera um link de uso único, válido por 72 horas.";
  - depois de gerar: "Convite criado para Marcos Lima", o link `https://[URL_PUBLICA]/convite/k7Qp2vX9mRt4LwZ8` com "Copiar link", "Vale até 25/10/2026 às 21:29 e funciona uma única vez", e "O Buteco não mostra este link de novo. Se ele se perder ou expirar, use Gerar novo link na linha do convite; o link anterior deixa de funcionar.";
  - a linha pendente: Formas "—", 2 etapas "—", Estado "Convite pendente" com "expira em 23/10, 18:02", Último acesso "—", e as ações **"Gerar novo link" e "Cancelar convite", sem "Editar"**;
  - o card "Acesso só por convite" e o card "Senha e recuperação" ("o administrador redefine, e o usuário cria outra no próximo acesso").
  - **Convite vencido não é desenhado.**
- **"Aceitar convite — sem provedor federado"** (`project/AceitarConviteSemProvedor.dc.html`): "Olá, Marcos", "Aldovrando Oliveira convidou você para o Buteco Agentes como Operador. Crie sua senha para entrar.", E-mail travado (`readonly`), Senha ("Mínimo de 12 caracteres"), Repita a senha, "Criar senha e entrar", e no rodapé "Este convite vale até 25/10/2026 às 21:29 e só pode ser usado uma vez." **Nenhum estado de link inválido é desenhado.**
- **"Aceitar convite — com provedor federado"** (`project/AceitarConvite.dc.html`): Google e SSO, "E-mail diferente do convite", "Conta ativada". É da M3; aqui só confirma que o aceite mostra quem convidou e a validade.
- **"Editar usuário"** (`project/UsuarioDetalhe.dc.html`): cabeçalho "Convidado por Aldovrando Oliveira em 12/08/2026 · último acesso ontem, 18:40"; na linha Senha, "Redefinir senha", que mostra "Link para redefinir a senha", `https://[URL_PUBLICA]/redefinir-senha/Hq8vN3pT6wYc2LxR`, "Copiar link", "Envie para o Rafael pelo canal que preferir. Vale por 24 horas e uma única vez. A senha atual continua funcionando até ele criar a nova.", e "Concluir". **Sem confirmação antes de gerar, e sem senha de quem gera.**
- **Não há quadro "Redefinir senha" (a tela pública de criar a nova senha)** entre os 16 do canvas, embora a #183 a liste. Registro na #183.

## Fronteira com a #183

**As telas públicas são da #183, e esta issue é só backend.** Pelos corpos:

- **#181**, "Telas do protótipo": "Aceitar convite sem provedor (e-mail travado, criar senha), Redefinir senha, Usuários (…) e Editar usuário (…): **servidas aqui, desenhadas na #183**". "Apps provavelmente envolvidos: apps/api".
- **#183**, "O que você imagina": lista "Aceitar convite sem provedor" e "Redefinir senha: criar a nova senha pelo link"; "Apps: apps/frontend"; depende da #181; e "Telas junto com o backend: contraria o sequenciamento".

Então não há divisão a propor por "tela não leva backend": a regra já está cumprida pelos corpos. O que esta change entrega à #183 é o contrato (D10) e as rotas do SPA que o link usa (`/convite`, `/redefinir-senha`, D3), que viram registro nela.

## Goals / Non-Goals

**Goals:**

- As rotas de convite e de redefinição que a #183 consome, com o contrato fechado aqui.
- O token fora dos logs de acesso, com uso único atômico e validade fixa.
- O convidado não entra, não aparece como ativo, e não revela que existe.
- As perguntas a–j do dono respondidas, com evidência; as que são dele, em "Open Questions", cada uma com a recomendação.

**Non-Goals** (cada um com o dono):

- **Telas** (#183), inclusive tirar o token do endereço com `history.replaceState`.
- **Papel no convite** e "Operador" no texto do aceite (#184, #185).
- **Envio de e-mail** (fora da evolução, issue com gatilho próprio).
- **Aceite por provedor federado**, "E-mail diferente do convite", "Conta ativada" (M3).
- **Restringir convite e redefinição ao Admin** (#184).
- **IP do cliente nos limites** (#221).

## Decisions

### D1. (g, h) Sem e-mail, e quem pode chamar

- **A M1 não envia e-mail.** O link volta na resposta da rota administrativa, e o admin o copia ("Copiar link" do protótipo) e o entrega pelo canal que quiser. Nenhuma linha desta change abre conexão SMTP, chama serviço de e-mail ou grava fila de envio.
- **Por isso o gatilho do P5 da #219 não dispara.** O gatilho é "a primeira funcionalidade que envie e-mail **ao endereço cadastrado**", porque a partir dali quem troca o e-mail decide para onde vai a recuperação. Aqui quem decide o destino do link é o admin, a cada link. A troca do próprio e-mail continua sem pedir a senha atual.
  - **O resíduo, registrado:** se o hábito do admin for mandar o link para o e-mail cadastrado, o campo e-mail vira canal de recuperação de fato, sem o sistema saber. Fica no `docs/` como orientação ao admin (conferir o destinatário por outro canal), e não como regra de código.
- **Na M1, todo usuário real chama as quatro rotas administrativas**, pelo `CurrentUser`. O HMAC de operador, o `sub` inexistente e o usuário desativado com access token válido recebem `403`, antes de qualquer outra resposta, como na #219 (D8).
- **As rotas públicas são anônimas** (`AnonymousRouteClassification(AuthEntryPoint)`), porque quem as chama ainda não tem sessão.
- **A senha de quem chama** (P10, decisão do dono de 02/11/2026): **criar o convite, gerar novo link do convite e gerar o link de redefinição** pedem a senha atual de quem chama, no corpo (`senha`). São as duas ações que transformam um access token roubado (15 min) em acesso permanente: por um usuário novo, ou pela senha de outra pessoa. **Cancelar o convite não pede**: só tira acesso.
  - **Como no `setup` das 2 etapas** (#218 P6, `AccountTwoFactorEndpoints.SetupAsync`): durante o bloqueio do e-mail de quem chama, `423` com `Retry-After`, **sem conferir a senha**; senha ausente ou errada → `401` "Senha inválida.", **contada no bloqueio do e-mail de quem chama** (`LoginThrottleStore.RecordFailureAsync`); acerto não zera a contagem (#219 D3).
  - **A ordem**, a do `setup`: `CurrentUser` (`403`) → bloqueio (`423`) → validação do corpo (`400`) → alvo (`404`, `403` a si mesmo, `409`) → senha (`401`) → grava. Nenhum Argon2 é gasto num pedido que reprovaria de qualquer jeito, e o que as respostas antes da senha revelam (o alvo existe, o e-mail está em uso) o token já lê pelo `GET /users`.
  - **Fora do limite global**, como as rotas autenticadas da #218 e da #219 que conferem senha (`setup`, `remove`, `password`): só o `POST /auth/session` e o segundo passo levam o `LoginRateLimiting.Marker`. O bloqueio por e-mail de quem chama limita a força bruta, e chamar exige um token válido.
  - **A #183 desenha o campo** de senha no formulário de convidar, na ação "Gerar novo link" e na de "Redefinir senha". O protótipo não tem nenhum dos três (convenção 17); registro na #183.
- **O que a #184 restringe**, registrado nela:
  - convidar, gerar novo link, cancelar convite e gerar link de redefinição passam a exigir **Admin**, e continuam pedindo a senha de quem chama onde pedem hoje;
  - o convite ganha o papel (#185), e o aceite o mostra ("como Operador");
  - **gerar link de redefinição continua sendo tomada de conta em poder** para quem tem a senha do Admin: a #184 decide se um Admin pode redefinir a senha de outro Admin.

### D2. (c) O convidado é uma linha de `users`, no estado `convidado`

**A decisão (P1):** convidar cria o `User` com `status = invited`, `password_hash` nulo, e o link pendente (D4). Aceitar o convite grava a senha e põe `status = active` na mesma linha. O `id` nasce no convite e é o `sub` para sempre.

| | (A) linha de `users` com estado novo | (B) tabela `invitations`, usuário criado no aceite |
|---|---|---|
| unicidade do e-mail com convites pendentes | **o índice único de `normalized_email` que já existe**; o `409` do `PUT` da #219 e o do convite são o mesmo, inclusive na corrida | duas tabelas, nenhum índice comum: precisa de lock ou de checagem dupla em toda escrita de e-mail (convite, aceite, `PUT`) |
| lista e detalhe da #219 | o convidado já sai da consulta; muda o `estado` | união de duas fontes, e uma linha sem `id` de usuário |
| aceitar | um `UPDATE` condicional numa linha (D6) | `INSERT` em `users` + consumo do convite, duas tabelas, ordem de lock |
| "Convidado por" depois do aceite | coluna `invited_by` na própria linha | copiar do convite no aceite |
| o que o resto do código precisa saber | `IsActive` falso: o login, o authorize, o `UserGrantTokenHandler` e o `CurrentUser` já recusam | nada |
| o que precisa de guarda novo | `activate` e `deactivate` da #219, e o login sem senha (abaixo) | nada |

**Recomendada (A).** A #180 já a antecipou (`password_hash` nulo "para convite pendente na #181"), e ela resolve a decisão em aberto da unicidade sem código: um convite pendente ocupa o e-mail, e o índice responde.

**O que o estado novo exige do código que já existe:**

- **Conversão de `status`:** `invited` ↔ `UserStatus.Invited`, e **texto desconhecido passa a lançar** em vez de virar `Deactivated` (hoje `value == "active" ? Active : Deactivated`). Assim um valor futuro não vira desativado em silêncio.
- **Login** (a): o convidado tem `password_hash` nulo. **O `PasswordHasher.Verify` passa a derivar contra o hash fictício quando o hash gravado é nulo ou vazio**, antes de devolver `Failed`. O login responde `401` genérico e conta a falha, como para e-mail sem conta. Sem isso, o convidado responde em ~0 ms e a conta inexistente em dezenas de ms: o tempo revela o convite pendente. Corrigir no `Verify`, e não no endpoint, cobre também a troca de senha da #219 e qualquer chamador futuro.
- **Authorize, renovação e rotas autenticadas:** já exigem `IsActive` (`AuthorizeEndpoint` l. 98, `UserGrantTokenHandler` l. 65, `CurrentUser`). O convidado nunca chega lá, porque não passa do login; nada muda.
- **`POST /users/{id}/activate` e `/deactivate`** no convidado → **`409`** "Este usuário ainda não aceitou o convite." Hoje o `activate` poria o convidado `active` sem senha, e o `deactivate` o tiraria da lista de pendentes sem invalidar o link. O convite sai pelo cancelamento (D5).
- **`PUT /users/{id}`** no convidado: permitido. Corrigir o e-mail de um convite com erro de digitação mantém o link, e o aceite mostra o e-mail novo. É o mesmo `409` da #219.
- **`POST /users/{id}/sessions/revoke`** no convidado: `200 { encerradas: 0 }`, sem regra nova. **`POST /users/{id}/two-factor/reset`**: `409` "não configurada", como hoje.
- **O bootstrap** só age com a tabela vazia; um convidado conta como linha. Sem mudança.
- **A regra do último ativo** (#219 D7) conta só `active`; o convidado não entra na conta. Sem mudança.

### D3. (a) O token no fragmento, fora dos logs

**O que cada lugar registra hoje**, conferido em 02/11/2026:

- **nginx do stack**: o `apps/frontend/deploy/nginx.conf` não declara `log_format` nem `access_log`, então vale o da imagem. Na `nginx:1.30-alpine` (a do `Dockerfile`), `/etc/nginx/nginx.conf` traz `log_format main '$remote_addr - $remote_user [$time_local] "$request" $status $body_bytes_sent "$http_referer" "$http_user_agent" …'` e `access_log /var/log/nginx/access.log main`, e `access.log` é link para `/dev/stdout` (conferido com `podman run --rm nginx:1.30-alpine`). **`$request` é a linha de requisição inteira, com caminho e query**, e vai para o log do contêiner, que o driver de log do host guarda. O `error_log` também cita `request: "GET …"` em erro de upstream.
- **nginx ou Cloudflare de fora** (o TLS, fora do stack, `docs/deployment.md`): fora do controle do repositório; o formato padrão de ambos registra caminho e query.
- **`apps/api`**: `Logging:LogLevel:Microsoft.AspNetCore = Warning` no `appsettings.json` (herdado por Development e Production). O "Request starting POST http://…/caminho?query" do `Microsoft.AspNetCore.Hosting.Diagnostics` é `Information`, então **hoje não sai**; mas qualquer um que suba esse nível para depurar passa a registrar caminho e query. Não há `UseHttpLogging` nem `EnableSensitiveDataLogging` (`grep` em `apps/api/src`). O SQL (`Database.Command`) sai em Information só fora de Production, com os parâmetros mascarados, e o parâmetro seria o hash, não o token.
- **Pré-visualização de link** (Slack, WhatsApp, Teams): ao colar o link numa conversa, o serviço faz `GET` no endereço para montar o cartão, e registra o endereço do lado dele.

| onde | (A) `/convite/<token>` ou `?token=` (protótipo) | (B) `/convite#<token>`, token no corpo de um `POST` |
|---|---|---|
| nginx do stack, navegação | `GET /convite/<token> HTTP/1.1` | `GET /convite HTTP/1.1` |
| nginx do stack, chamada da API | depende da rota; com `GET /auth/invitation/<token>`, de novo o token | `POST /auth/invitation HTTP/1.1`; o corpo não entra no formato `main` |
| nginx/Cloudflare de fora | o token | `/convite` |
| `apps/api` | hoje nada; com `Microsoft.AspNetCore` em Information, o token | caminho sem token em qualquer nível |
| robô de pré-visualização | busca e registra o endereço com o token | busca só `/convite` (o fragmento não vai na requisição) |
| histórico do navegador | o token | o token até o SPA trocar o endereço por `/convite` (`history.replaceState`, #183) |
| `Referer` | já fechado por `Referrer-Policy: no-referrer` (#217) | o fragmento nunca vai no `Referer` |
| a conversa onde o admin colou o link | o token | o token (inevitável; a validade e o uso único limitam) |

**Por que o fragmento não chega:** o navegador tira o fragmento antes de montar a requisição (RFC 3986 §3.5; no Fetch, a URL da requisição é serializada "with exclude fragment"). Não é o servidor que o descarta: ele nunca o recebe.

**A decisão (P3): (B).** O link é `<base do painel>/convite#<token>` e `<base do painel>/redefinir-senha#<token>`. O painel lê `location.hash`, troca o endereço por `/convite` e manda o token no corpo de `POST`s JSON. **Todas as rotas que recebem token são `POST` com corpo**, inclusive as consultas: um `GET` com o token no caminho anularia o fragmento.

- **Diverge do protótipo** (`/convite/k7Qp…`, `/redefinir-senha/Hq8v…`). Pela convenção 17, a regra escrita (o token fora dos logs, pedido do dono) vence, e a divergência vai ao registro da #183, com o `replaceState`.
- **`/convite` e `/redefinir-senha` não colidem** com prefixo nenhum de API do `nginx.conf`: caem no `location /` e recebem o `index.html`.
- **Alternativa considerada:** caminho com o token e `access_log off` (ou um `log_format` que corte o caminho) num `location` próprio. Resolve só o nginx do stack: o de fora, o robô de pré-visualização e um nível de log mais alto no `apps/api` continuam vendo o token.

### D4. (b, i) O token

- **256 bits** de `RandomNumberGenerator`, em base64url sem preenchimento: 43 caracteres.
- **Guardado só como SHA-256** (32 bytes), como os códigos de recuperação da #218. Hash rápido, e não Argon2, porque o token tem 256 bits de entropia: não há dicionário a atrasar, e a busca precisa ser determinística para usar o índice.
- **A busca é pelo índice do hash.** O tempo da busca depende do SHA-256 da entrada, que quem ataca não controla; não há comparação byte a byte de segredo a proteger.
- **Um link pendente por usuário**, em colunas da própria linha (`users`): `access_link_hash` (`bytea`, índice único; o PostgreSQL aceita vários nulos), `access_link_purpose` (`invitation` ou `password_reset`) e `access_link_expires_at`. Um convidado nunca tem redefinição (não tem senha), e um ativo nunca tem convite, então um lugar basta.
  - **Por que na linha, e não numa tabela `user_access_links`:** o consumo (D6) vira um `UPDATE` de uma linha só, e o cancelamento um `DELETE` da mesma linha. Com duas tabelas, aceitar (link, depois usuário) e cancelar (usuário, depois link, pela cascata) travariam em ordens opostas, e o PostgreSQL abortaria um dos dois com `40P01`. A tabela própria guardaria histórico, que nenhum quadro pede (convenção 25).
- **Validade, em constantes** (convenção 2, como o `LoginRateLimiting`): **convite 72 h** e **redefinição 24 h** (corpo da #181 e protótipo). Contadas do `TimeProvider` no momento em que o link é gerado.
- **O que invalida o link anterior:**

  | evento | efeito |
  |---|---|
  | gerar outro (convite ou redefinição) | sobrescreve o hash: o antigo deixa de achar a linha |
  | usar (aceitar ou concluir) | apaga o hash no mesmo `UPDATE` |
  | cancelar o convite | apaga a linha (D5) |
  | desativar | `User.Deactivate` apaga o link. Reativar não o devolve |
  | trocar a própria senha (#219) | `User.ChangePassword` apaga o link de redefinição (P6) |
  | vencer | a consulta e o consumo exigem `access_link_expires_at > agora`; o hash fica até ser sobrescrito ou apagado |
  | editar nome ou e-mail, redefinir as 2 etapas, encerrar sessões | nada: o link é da pessoa, não do e-mail |

- **(i) A base do link** é a do painel, pela regra que o `IdentityClientCatalog.PanelBase()` já usa: `Identity:PanelBaseUrl` e, sem ela, o issuer (`PublicUrl:BaseUrl`). A função sai do catálogo para uma peça compartilhada (dois consumidores reais: os endereços de retorno do `buteco-painel` e os links), e **nunca lê `Host`, `X-Forwarded-Host` nem `Request.Scheme`**. Um `Host` forjado numa requisição de convite não muda o link.

### D5. (c) Convite: criar, gerar de novo, cancelar, vencido

- **Criar** (`POST /users/invitations`, `{ nome, email, senha }`, com a senha de quem chama, D1): a validação do `PUT` da #219 (nome 1 a 200, e-mail por `MailAddress.TryCreate` até 254, `Trim`). `409` "Já existe um usuário com este e-mail." quando o e-mail normalizado já é de um ativo, desativado ou convidado, **inclusive na corrida**, pelo mesmo `catch` do `23505` no `IX_users_normalized_email`. Grava `invited_by` = quem chama. `created_at` é o momento do convite, e é a data do "Convidado por … em 12/08/2026".
  - **E-mail de um desativado → `409`.** O caminho é reativar; um convite novo criaria um segundo usuário para a mesma pessoa.
- **Gerar novo link** (`POST /users/{id}/invitation/renew`, `{ senha }`, D1): só no convidado (senão `409`), vencido ou não. Novo hash, nova validade de 72 h a partir de agora. O link anterior deixa de valer no mesmo `UPDATE`.
- **Cancelar** (`POST /users/{id}/invitation/cancel`, sem senha: só tira acesso, D1) **apaga a linha** (P2): `DELETE FROM users WHERE id = @id AND status = 'invited'`.
  - **Por que apagar, se "usuário é desativado, nunca excluído":** o convidado nunca foi usuário. Não entrou, nenhum token levou o `sub` dele, nada o referencia (sem sessões, sem códigos de recuperação, e ele não pode ter convidado ninguém). Um estado "cancelado" guardaria o e-mail para sempre, e o admin não conseguiria convidar a mesma pessoa de novo sem um caminho extra.
  - **A corrida com o aceite** cai no `WHERE`: se o aceite confirmou antes, o `status` já é `active`, o `DELETE` apaga 0 linhas, e a resposta é `409` "O convite já foi aceito.". Se o cancelamento confirmou antes, o `UPDATE` do aceite acha 0 linhas e responde o link inválido (D8). Uma linha, um lock: sem deadlock.
- **Convite vencido** (decisão em aberto da issue, P4): **continua na lista**, com `estado: "convidado"` e `convite.vencido: true`, até o admin gerar novo link ou cancelar. Sumir com ele esconderia a linha que ocupa o e-mail, e um convite novo para a mesma pessoa receberia `409` sem explicação visível. Não há limpeza automática.

### D6. (d) Aceitar o convite

**`POST /auth/invitation`** (consulta), `{ token }`:

- `200 { nome, email, convidadoPor, expiraEm }` só para link de convite válido: hash encontrado, `purpose = invitation`, `status = invited`, não vencido. `convidadoPor` é o nome de quem convidou (o protótipo: "Aldovrando Oliveira convidou você"); `expiraEm` alimenta "Este convite vale até …".
- Qualquer outro caso: a resposta única do D8. A consulta não grava nada.

**`POST /auth/invitation/accept`**, `{ token, senha }`:

1. barreiras de JSON e `Origin` (`SessionRequestGuards`), antes de tudo;
2. `PasswordPolicy` → `400` com a mensagem da política;
3. o link é procurado como na consulta; inválido → D8, **sem calcular o Argon2**;
4. o hash Argon2id da senha, fora de transação;
5. **o consumo, num `UPDATE` só**: `SET password_hash, status = 'active', access_link_* = NULL WHERE id = @id AND access_link_hash = @hash AND access_link_purpose = 'invitation' AND access_link_expires_at > @agora AND status = 'invited'`. Zero linhas (outro aceite, novo link ou cancelamento no meio) → D8;
6. o bloqueio do e-mail é zerado (`LoginThrottleStore.ResetAsync`, P8);
7. **`204`, sem sessão.**

**O aceite não abre sessão (P5).** O painel, com o `204`, chama o `POST /auth/session` com o e-mail que a consulta devolveu e a senha que a pessoa acabou de digitar, e segue o fluxo de sempre (authorize, código, tokens). É o "Criar senha e entrar" do protótipo em duas chamadas.

- **Por quê:** um caminho só para abrir sessão, com o bloqueio, o limite global, as 2 etapas, o último acesso e o cookie do emissor no lugar que já os testa. Uma sessão aberta pelo aceite precisaria repetir tudo isso.
- **Nada de sessão sem senha ou sem aceite explícito:** a consulta não abre nada; o aceite só grava a senha; a sessão só nasce do login com a senha recém-criada.
- **O stamp não muda no aceite.** O convidado nunca teve sessão, e o stamp gerado no convite nunca saiu em cookie ou token.

### D7. (e) Redefinir a senha pelo admin

**`POST /users/{id}/password-reset`**, `{ senha }` (a de quem chama, D1) → `200 { link, expiraEm }`:

- só no **ativo**. Convidado → `409` "Este usuário ainda não aceitou o convite." (o caminho é gerar novo link do convite); desativado → `409` "Usuário desativado.";
- **a si mesmo → `403`** "Use Minha conta para trocar a própria senha." (P7), antes de conferir a senha. Com a P10 o link para si já pediria a senha, mas seria um segundo caminho para o que o `POST /account/password` (#219 D3) faz, com um link de 24 h solto no meio. O mesmo critério da #219 (desativar a si → `403`) e da #218 (redefinir as próprias 2 etapas → `403`);
- **não toca a senha, o stamp nem as sessões.** A senha atual continua valendo até a nova ser criada (texto do protótipo). Gerar de novo sobrescreve o link (D4).

**`POST /auth/password-reset`** (consulta), `{ token }` → `200 { nome, email, expiraEm }` para link válido (`purpose = password_reset`, `status = active`, não vencido). O e-mail serve ao campo de usuário que o gerenciador de senhas espera ao lado da senha nova.

**`POST /auth/password-reset/complete`**, `{ token, senha }`, os mesmos passos 1 a 4 do aceite, e depois, **numa transação**:

- o `UPDATE` condicional: `SET password_hash, password_hash_is_bootstrap_legacy = false, security_stamp = <novo>, access_link_* = NULL WHERE id AND access_link_hash AND purpose = 'password_reset' AND expires_at > agora AND status = 'active'`. Zero linhas → D8;
- **`UserSessions.EndAllAsync`**: todas as autorizações da pessoa revogadas, como a troca de senha da #219 (D2 dela). Isso responde à decisão em aberto da issue: **concluir a redefinição encerra todas as sessões da pessoa**;
- o bloqueio do e-mail zerado (P8);
- `204`, sem sessão; o painel faz o login normal, como no aceite.

**As 2 etapas não são removidas.** Quem tinha a verificação ligada entra, depois da redefinição, com a senha nova **e** o código. Quem perdeu a senha e o celular precisa das duas redefinições do admin: a de senha (aqui) e a da verificação (#218). Isso entra no registro da #183, para a tela dizer.

### D8. (f) Força bruta e enumeração nas rotas do token

- **Força bruta no token:** 256 bits. Mesmo a 10⁹ tentativas por segundo, a chance de acertar algum dos links vivos é desprezível; não há contagem de falhas a fazer. **O bloqueio por e-mail não se aplica**: antes de achar o link não há e-mail a quem atribuir a falha, e depois de achar não há "falha" (a política reprovar não é palpite).
- **O limite global entra no aceite e na conclusão** (`LoginRateLimiting.Marker`), porque as duas calculam Argon2id; é a razão de ser do limite (#180 D4). **As duas consultas ficam fora**: fazem um SHA-256 e uma busca por índice, e pô-las na mesma cota de 4 deixaria uma enxurrada de consultas tirar o login do ar.
- **Barreiras de JSON e `Origin` nas quatro rotas públicas**, como no login: um formulário de outro site não aceita convite em nome de ninguém.
- **Uma resposta só para link inválido (P9):** inexistente, vencido, usado, cancelado, substituído, de propósito trocado (o de redefinição na rota do convite) ou de usuário desativado → **`404`** "Este link não vale mais. Se você já criou a senha, entre normalmente; se não, peça um novo link ao administrador." Mesmo status, mesmo corpo, mesma ordem de trabalho (o Argon2 só depois do link achado, em todos os casos inválidos igual).
  - Distinguir "vencido" de "usado" ajudaria pouco quem tem o link, e diria a quem tem um link velho (de uma conversa vazada) se ele chegou a ser usado. O texto único já cobre os dois caminhos da pessoa.

### D9. A forma do código

- **Classes estáticas de endpoint com o `AppDbContext`**, como a área `Users/` da #218 e da #219, e não comando/consulta com `Mediator`.
- **`Users/AccessLinks/`**, nova:
  - `AccessLinkToken`: gerar (256 bits, base64url) e o SHA-256; validade por propósito;
  - `AccessLinkUrls`: `<base do painel>/convite#…` e `/redefinir-senha#…`, a partir da base do D4;
  - `CallerPassword`: a senha de quem chama nas três rotas que a pedem (D1, P10);
  - `InvitationEndpoints` (as três administrativas) e `PasswordResetEndpoints` (a administrativa), no grupo `/users`;
  - `AccessLinkPublicEndpoints` (as quatro públicas, sob `/auth`).
- **`User`**: `CreateInvitation(email, nome, convidadoPor, agora)`, `IssueAccessLink(propósito, hash, expira)`; `Deactivate` e `ChangePassword` apagam o link. O consumo é `UPDATE` condicional fora da entidade, como o `TwoFactorVerifier` (#218).
- **`PanelBase`** sai de `IdentityClientCatalog` para `Identity/PanelBaseUrl.cs` (D4).

### D10. O contrato para a #183

**Administrativas** (grupo `/users`, `CurrentUser`; HMAC → `403`):

| rota | corpo | respostas |
|---|---|---|
| `POST /users/invitations` | `{ nome, email, senha }` | `201 { usuario: UserSummary, link, expiraEm }`; `400`; `409` e-mail em uso; `401` senha de quem chama; `423` + `Retry-After` |
| `POST /users/{id}/invitation/renew` | `{ senha }` | `200 { link, expiraEm }`; `404`; `409` não é convite; `401`; `423` |
| `POST /users/{id}/invitation/cancel` | — | `204`; `404`; `409` já aceito ou não é convite |
| `POST /users/{id}/password-reset` | `{ senha }` | `200 { link, expiraEm }`; `403` a si mesmo; `404`; `409` convidado ou desativado; `401`; `423` |

`senha` é sempre a de quem chama (D1). O `401` conta no bloqueio do e-mail de quem chama, e o `423` vem antes de qualquer conferência.

**Públicas** (anônimas, JSON, `Origin` conferida):

| rota | corpo | respostas |
|---|---|---|
| `POST /auth/invitation` | `{ token }` | `200 { nome, email, convidadoPor, expiraEm }`; `404` link inválido (D8) |
| `POST /auth/invitation/accept` | `{ token, senha }` | `204`; `400` política; `404`; `429` limite |
| `POST /auth/password-reset` | `{ token }` | `200 { nome, email, expiraEm }`; `404` |
| `POST /auth/password-reset/complete` | `{ token, senha }` | `204`; `400`; `404`; `429` |

**O que muda nas rotas da #219:**

- `estado` ganha **`convidado`** (além de `ativo` e `desativado`);
- **`UserSummary`** ganha `convite: { expiraEm, vencido } | null`, só no convidado; o `vencido` sai do relógio do servidor (convenção 13);
- **`UserDetail`** ganha `convidadoPor: { id, nome } | null` (nulo para o bootstrap) — o `criadoEm` já é a data do convite;
- para o convidado: `formasDeEntrada: []`, `duasEtapas: false`, `ultimoAcesso: null`;
- `activate` e `deactivate` no convidado → `409`.

### D11. (j) Testes

Os guardas de cada linha são verificados contra o defeito (convenção 15): o defeito entre parênteses é reintroduzido, o teste reprova **no componente que a correção toca**, e a árvore volta. Onde o efeito do defeito coincide às vezes com o do acerto (corrida), o guarda comportamental vai **pareado com a asserção sobre o SQL emitido** (quinta forma).

**`apps/api`, unidade:**

| teste | o que afirma | guarda verificado contra |
|---|---|---|
| `AccessLinkTokenTests` | 32 bytes aleatórios, 43 caracteres base64url sem `=`, `+` ou `/`; dois gerados diferem; o hash é o SHA-256 dos bytes ASCII do token | hash sobre o token decodificado num lado e o texto noutro (a consulta não acha o link gerado) |
| `AccessLinkUrlsTests` | `PanelBaseUrl` presente → base dela; ausente → o issuer; token só depois de `#`; caminho `/convite` e `/redefinir-senha` | token no caminho (`/convite/…`) |

**`apps/api`, integração (Testcontainers, `TestServer`, o login real da `IdentityIssuerTestClient`):**

| teste | o que afirma | guarda verificado contra |
|---|---|---|
| `InvitationCreateTests` | `201` com o link de base configurada e token no fragmento; o banco guarda só o SHA-256 (o token não aparece em coluna nenhuma); o convidado na lista com `estado: convidado`, `convite.expiraEm` = agora + 72 h, `formasDeEntrada: []`; e-mail de ativo, desativado ou convidado em outra caixa → `409`; `Host: evil.example` na requisição não muda o link | base lida do `Host` |
| | dois convites simultâneos para o mesmo e-mail → um `201` e um `409`, nunca `500` (20 rodadas), mais o determinístico com o `DbCommandInterceptor` da #219 | `catch` do `23505` retirado |
| `InvitationAcceptTests` | consulta → `200` com nome, e-mail, quem convidou e validade; aceite com senha de 11 → `400` e o link continua valendo; aceite certo → `204`, `estado: ativo`, e **o login com a senha nova → `204` e o authorize emite código**; o mesmo link de novo → `404` | `status = invited` retirado do `WHERE` (o segundo aceite troca a senha) |
| | **dois aceites simultâneos** → um `204` e um `404`; **o SQL emitido do consumo traz `access_link_hash`, `access_link_expires_at` e `status` no `WHERE` do mesmo `UPDATE`** | consumo em dois passos (lê e depois grava): a asserção do SQL reprova sempre; a corrida pode passar por sorte |
| | vencido (`FakeTimeProvider` + 72 h) → `404` na consulta e no aceite; depois de gerar novo, o antigo → `404` e o novo → `200`; cancelado → `404` | conferência de `expires_at` retirada |
| | o bloqueio do e-mail (5 falhas de login antes do aceite) é zerado pelo aceite, e o login seguinte → `204` | `ResetAsync` retirado (o login → `423`) |
| `InvitationLifecycleTests` | cancelar → `204`, a linha some, o e-mail volta a aceitar convite; cancelar o aceito → `409`; renovar o ativo → `409`; detalhe do aceito traz `convidadoPor` | `DELETE` sem `status = 'invited'` no `WHERE` (cancelar o aceito apaga um usuário ativo) |
| `PasswordResetTests` | gerar → `200` com link de 24 h; **a senha antiga continua entrando**; consulta → `200`; concluir com política ruim → `400`; concluir → `204`; a senha antiga → `401`, a nova → `204`; **toda renovação de antes → `invalid_grant` e a lista de sessões fica vazia**; as 2 etapas continuam ligadas e o login pede o código | revogação retirada (a lista mostra sessões); troca do stamp retirada (a renovação passa) |
| | a si mesmo → `403`; convidado e desativado → `409`; desativar depois de gerar → o link → `404`, e reativar não o devolve; trocar a própria senha (#219) depois de gerar → o link → `404` | `Deactivate` sem apagar o link |
| | duas conclusões simultâneas → um `204` e um `404`, com o mesmo par do SQL do aceite | o mesmo |
| | link de redefinição na rota do convite, e o inverso → `404` | `purpose` fora do `WHERE` |
| `AccessLinkPublicRoutesTests` | as quatro rotas: sem JSON → `415`; `Origin` de fora → `403`; **inexistente, vencido, usado, cancelado e de propósito trocado → o mesmo status e o mesmo corpo, byte a byte**; o aceite e a conclusão entram no limite global (a 5ª concorrente espera ou recebe `429`) e as consultas não | uma mensagem distinta para o vencido; `Marker` retirado do aceite |
| `AccessLinkCallerTests` | HMAC `operator` → `403` nas quatro administrativas, e nada é gravado; usuário desativado com access token válido → `403` | rota sem o `CurrentUser` |
| `AccessLinkCallerPasswordTests` (P10) | criar convite, gerar novo link e gerar redefinição: **sem `senha` ou com a errada → `401`, nada gravado** (nenhuma linha nova; o hash do link igual ao de antes; nenhum link de redefinição); a falha conta no bloqueio **do e-mail de quem chama**, e não no do alvo; a quinta falha bloqueia, e a sexta, com a senha certa → `423` com `Retry-After`, sem gravar; senha certa → `201`/`200`; **cancelar convite sem `senha` → `204`** | **a conferência da senha retirada, uma rota de cada vez** (criar, renovar, redefinir): o caso "senha errada → `401`" da rota reprova (sai `201`/`200`, com o link); a contagem atribuída ao alvo (a linha do bloqueio do alvo aparece, e a de quem chama não) |

**Os três achados no código atual**, cada um com o caso e o guarda (D2):

| achado | teste | o que afirma | guarda verificado contra |
|---|---|---|---|
| hash nulo responde sem derivar | `PasswordHasherTests` (estendido, unidade) | `Verify` com hash nulo e com hash vazio → `Failed`, e um dublê que conta as chamadas registra **uma** verificação contra o hash fictício em cada; com hash válido e senha errada, nenhuma | o `return Failed` imediato, como está hoje em `main` (o contador fica em zero) |
| | `InvitedLoginTests` (integração) | login com o e-mail do convidado → `401` "E-mail ou senha inválidos.", a falha contada, o corpo igual, byte a byte, ao de um e-mail sem conta, e uma chamada à verificação fictícia (o mesmo dublê, no host) | o `return Failed` imediato; uma recusa pelo `status` antes da senha (o convidado recebe o `403` "Acesso desativado", que revela a conta) |

**O tempo fica fora da suíte** (decisão do dono, 02/11/2026): um teste por tempo reprova sob carga, que é o problema da #226. O guarda confere o caminho (a verificação fictícia chamada), e a comparação de tempo do convidado contra a conta inexistente é conferência manual no fechamento, com os números registrados. Para o dublê, o `PasswordHasher` deixa de ser `sealed` e o `VerifyAgainstDummy` vira `virtual`.
| `activate` e `deactivate` aceitam o convidado | `InvitationLifecycleTests` | `activate` no convidado → `409`, e o `estado` continua `convidado`, sem `password_hash`; `deactivate` no convidado → `409`, e o link continua valendo (a consulta → `200`) | `activate` sem a recusa (sai `200` com `estado: ativo`); `deactivate` sem a recusa (sai `200`, e o convidado vira `desativado`) |
| `status` desconhecido vira `Deactivated` | `UserStatusConversionTests` (integração, Testcontainers) | uma linha com `status = 'invited'` volta `Invited`, e com `status = 'qualquer'` (gravada por SQL direto) a leitura lança `InvalidOperationException` com o valor na mensagem | a conversão de hoje (`value == "active" ? Active : Deactivated`): `invited` volta `Deactivated` e o texto desconhecido não lança |
| `AccessLinkLogTests` | com `Microsoft.AspNetCore` em `Information` no host de teste, um ciclo inteiro (convite, consulta, aceite) não deixa o token em nenhuma linha de log capturada | uma rota de consulta por `GET /auth/invitation/{token}` |

**`tests/`:** sem mudança. O `InboxOrchestratorRoundTrip.Tests` e o `ApiConnectorsRoundTrip.Tests` rodam na baseline e no fechamento, porque o login (`PasswordHasher`) e a conversão de `status` mudam.

**`apps/frontend`:** sem mudança de código e sem teste novo. Entra na baseline como referência.

## Árvore de pastas proposta

```
apps/api/src/Buteco.Api/
├── Users/
│   ├── User.cs                                   + InvitedBy, AccessLink*; CreateInvitation, IssueAccessLink;
│   │                                               Deactivate e ChangePassword apagam o link
│   ├── UserStatus.cs                             + Invited
│   ├── AccessLinks/                              NOVO
│   │   ├── AccessLinkToken.cs                    256 bits, base64url, SHA-256, validade por propósito (D4)
│   │   ├── AccessLinkPurpose.cs                  invitation | password_reset
│   │   ├── AccessLinkUrls.cs                     /convite#… e /redefinir-senha#… (D3, D4)
│   │   ├── CallerPassword.cs                     a senha de quem chama: bloqueio (423), conferência (401, contada) (D1, P10)
│   │   ├── InvitationEndpoints.cs                POST /users/invitations, …/invitation/renew, …/invitation/cancel (D5)
│   │   ├── PasswordResetEndpoints.cs             POST /users/{id}/password-reset (D7)
│   │   └── AccessLinkPublicEndpoints.cs          POST /auth/invitation[/accept], /auth/password-reset[/complete] (D6–D8)
│   ├── Management/UserEndpoints.cs               activate e deactivate recusam o convidado (D2)
│   ├── Management/UserResponses.cs               estado convidado, convite, convidadoPor (D10)
│   ├── Passwords/PasswordHasher.cs               hash nulo deriva contra o fictício (D2)
│   └── UserServiceCollectionExtensions.cs        (se alguma peça precisar de DI)
├── Identity/
│   ├── PanelBaseUrl.cs                           NOVO: a base do painel, extraída do catálogo (D4)
│   └── Clients/IdentityClientCatalog.cs          passa a usar PanelBaseUrl
├── Infrastructure/AppDbContext.cs                status com invited e texto desconhecido lançando; colunas e índice novos
├── Infrastructure/Migrations/                    + AddUserAccessLinks (invited_by, access_link_hash/purpose/expires_at, índice único)
└── Program.cs                                    + MapInvitationEndpoints, MapPasswordResetEndpoints, MapAccessLinkPublicEndpoints

apps/api/tests/Buteco.Api.Tests/
├── Users/AccessLinks/                            NOVO: AccessLinkTokenTests, AccessLinkUrlsTests, InvitationCreateTests,
│                                                       InvitationAcceptTests, InvitationLifecycleTests, PasswordResetTests,
│                                                       AccessLinkPublicRoutesTests, AccessLinkCallerTests,
│                                                       AccessLinkCallerPasswordTests, AccessLinkLogTests
├── Users/Login/InvitedLoginTests.cs              NOVO
├── Users/Passwords/PasswordHasherTests.cs        + hash nulo deriva
├── Users/UserStatusConversionTests.cs            NOVO
└── Support/                                      + criar convite e aceitar, se a IdentityIssuerTestClient não tiver

01-ARQUITETURA_E_CONVENCOES.md (Autenticação), 02-HISTORICO_E_STATUS.md, CHANGELOG.md, docs/architecture.md, docs/deployment.md (o link e o canal)
```

**Nada vai para `libs/`:** um consumidor só, o `apps/api`. **Uma migration** (colunas anuláveis e um índice em `users`, tabela de dezenas de linhas). **Nenhum pacote novo** (`RandomNumberGenerator` e `SHA256` são do BCL) e **nenhuma variável de configuração**. Só o `apps/api` tem código.

## Tamanho e divisão

**Âncoras medidas** (convenção 18), só código, sem `openspec/`, `.md` nem migrations, por `git diff --numstat`, testes = caminhos com `tests/`:

| PR | issue | produção | testes | projetado → entregue |
|---|---|---|---|---|
| #224 | #180 | +2.036 / −141, 45 arquivos | +2.984 / −237, 44 arquivos | +57 % / +24 % |
| #229 | #218 | +1.203 / −161, 28 arquivos | +1.473, 18 arquivos | +12 % / −15 % |
| #230 | #219 | **+765 / −31, 15 arquivos** (`80f6c9a..e0fb7e6`, medido hoje) | **+1.365, 17 arquivos** | 980 / 1.550 → **−22 % / −12 %** |

**Projeção por componente** (rascunho, convenção 18):

| componente | produção | testes |
|---|---|---|
| estado novo, conversão, colunas, `User`, migration (escrita) | ~90 | ~40 |
| token, url, `PanelBaseUrl` extraída | ~80 | ~80 |
| convite: criar, renovar, cancelar | ~170 | ~380 |
| redefinição: gerar | ~50 | (no `PasswordResetTests`) |
| públicas: duas consultas, aceite, conclusão | ~200 | ~450 |
| lista e detalhe, `activate`/`deactivate`, login sem senha | ~60 | ~150 |
| registro das rotas, apoio de teste | ~20 | ~100 |
| senha de quem chama nas três rotas (P10): uma conferência compartilhada, bloqueio, `401`/`423` | ~40 | ~150 |
| **total** | **~710** | **~1.350** |

Com o erro da #219 (a âncora mais parecida: mesma área, mesmo estilo), fica em ~550 / ~1.190; com o pior da série (a #180), ~1.110 / ~1.670. **Cabe numa change**, na faixa da #218 (1.203 / 1.473). Não proponho divisão. A P10 acrescentou ~40 / ~150 à projeção de 02/11 (~670 / ~1.200).

**A conferência da senha de quem chama** é uma peça só (`CallerPassword`, em `Users/AccessLinks/`), com três consumidores nesta change (criar, renovar, redefinir), conforme a convenção 2. O `setup`, o `remove` e o `recovery-codes` da #218 e o `password` da #219 têm cada um a sua forma (código, senha atual com a política) e **não** migram para ela nesta change: seria tocar quatro rotas que não são desta issue.

**Divisão possível, registrada e não recomendada**, na régua da #180: (1) convite (estado, token, url, as rotas de convite e as duas públicas dele) e (2) redefinição (a administrativa e as duas públicas). A (1) carregaria toda a infraestrutura do link e a (2) seria ~100 de produção; a #183 depende das duas.

## Risks / Trade-offs

- **[Token em log de acesso]** → fragmento e `POST` com corpo (D3). Coberto por `AccessLinkUrlsTests` (o token só depois de `#`) e `AccessLinkLogTests` (nenhuma linha de log com o token, com o nível de hosting em Information). O resíduo é a conversa onde o admin colou o link; a validade e o uso único o limitam.
- **[O SPA esquecer o `replaceState`]** → o token fica no histórico do navegador. É da #183; entra no registro.
- **[Convite pendente revelado pelo tempo do login]** → a derivação fictícia no `Verify` (D2). Coberto por `PasswordHasherTests` e `InvitedLoginTests`, pelo caminho (a verificação fictícia chamada), e pela conferência manual de tempo no fechamento.
- **[Convidado ativado sem senha pelo `activate` da #219]** → `409` (D2). Coberto por `InvitationLifecycleTests`.
- **[Dois aceites, ou aceite e cancelamento, ao mesmo tempo]** → um `UPDATE`/`DELETE` condicional numa linha só (D5, D6). Coberto pelo par comportamental + SQL do `InvitationAcceptTests` e do `PasswordResetTests`.
- **[Access token roubado vira acesso permanente]** → criar convite, gerar novo link e gerar redefinição pedem a senha de quem chama (P10, D1), com a falha no bloqueio do e-mail de quem chama. O token sozinho (15 min) não cria usuário nem troca a senha de ninguém. Coberto por `AccessLinkCallerPasswordTests`, com a conferência retirada rota a rota.
- **[O que o token sozinho ainda faz]** → cancelar convite (só tira acesso), desativar e encerrar sessões (#219), redefinir as 2 etapas de outro (#218). Nenhum dá entrada a quem tem o token. A #184 restringe tudo ao Admin.
- **[Quem tem a senha de um usuário real gera link para qualquer outro]** → é o "Admin em poder" da M1 (#180 D4); a #184 decide se um Admin redefine a senha de outro Admin.
- **[Link entregue pelo e-mail cadastrado vira recuperação de fato]** → orientação no `docs/` (D1); o gatilho do P5 continua sendo o envio pelo sistema.
- **[O `vencido` calculado no servidor diverge do relógio do navegador]** → o servidor manda `vencido` e `expiraEm`; a tela mostra o que ele diz (convenção 13).
- **[Rollback com convidados no banco]** → a imagem anterior lê `invited` como `Deactivated` (conversão antiga): o convidado aparece como desativado, e o `activate` antigo o poria ativo sem senha, sem entrada possível (o `Verify` antigo recusa hash nulo). Nenhuma conta abre; o Migration Plan manda cancelar os convites pendentes antes do rollback.

## Migration Plan

1. **Deploy:** só o `apps/api`, com a migration `AddUserAccessLinks` (colunas anuláveis e índice único em `access_link_hash`, aplicada pelo `deploy/migrate` como as anteriores). Sem variável, sem mudança no compose nem no nginx.
2. **Conferir:** `POST /users/invitations` com o token do painel → `201` com o link na base pública e o token depois de `#`; `GET /users` mostra a linha `convidado`; `POST /auth/invitation` com o token → `200`.
3. **Rollback:** antes, cancelar os convites pendentes (`POST /users/{id}/invitation/cancel`) e deixar vencer ou concluir os links de redefinição; depois, a imagem anterior. As colunas novas ficam e são ignoradas; a migration não precisa ser desfeita.

## Registros (convenção 23)

Autorizados pelo dono e publicados em 02/11/2026: [#183](https://github.com/aldovrando-oliveira/buteco-agentes/issues/183#issuecomment-6569746385), [#184](https://github.com/aldovrando-oliveira/buteco-agentes/issues/184#issuecomment-6569746639), [#181](https://github.com/aldovrando-oliveira/buteco-agentes/issues/181#issuecomment-6569746939).

- **#183:** o contrato (D10); o link com o token no fragmento e o `history.replaceState` ao abrir (D3, divergência do protótipo, convenção 17); as rotas do SPA `/convite` e `/redefinir-senha`; o `204` do aceite e da conclusão seguido do login normal com a senha recém-criada (D6); o texto do link inválido (D8); a redefinição encerra todas as sessões e mantém as 2 etapas (D7); o convite vencido na lista (D5); o quadro "Redefinir senha" que o canvas não tem; o `409` de `activate`/`deactivate` no convidado; **o campo de senha de quem chama** no formulário de convidar, em "Gerar novo link" e em "Redefinir senha", com o `401` e o `423` (P10, divergência do protótipo).
- **#184:** convidar, renovar, cancelar e gerar redefinição só para Admin, com a senha de quem chama onde ela já é pedida (P10); o papel no convite; a pergunta "Admin redefine a senha de outro Admin?".
- **#181:** o fechamento das três decisões em aberto, com o onde.

## Decisões do dono (02/11/2026)

Na revisão do proposal e do design:

| | pergunta | decisão | onde |
|---|---|---|---|
| P1 | O convite é uma linha de `users` no estado `convidado`, ou uma tabela própria? | linha de `users` (recomendada) | D2 |
| P2 | Cancelar o convite apaga a linha, ou a marca como cancelada? | apaga (recomendada) | D5 |
| P3 | O token vai no fragmento, contra o protótipo? | fragmento, com as consultas por `POST`; a divergência vai à #183 (recomendada) | D3 |
| P4 | Convite vencido aparece na lista ou some? | aparece, com `vencido: true` (recomendada) | D5 |
| P5 | O aceite abre a sessão? | não: `204`, e o painel faz o login normal (recomendada) | D6 |
| P6 | Trocar a própria senha invalida o link de redefinição pendente? | sim (recomendada) | D4 |
| P7 | O admin gera link de redefinição para si mesmo? | não, `403` (recomendada) | D7 |
| P8 | Aceitar e concluir zeram o bloqueio do e-mail? | sim (recomendada) | D6, D7 |
| P9 | Link inválido recebe uma resposta só? | sim, `404` com um texto (recomendada) | D8 |
| P10 | Gerar link pede a senha de quem chama? | **muda da recomendação**: criar convite, gerar novo link do convite e gerar link de redefinição pedem a senha atual de quem chama, no corpo; errada → `401`, contada no bloqueio do e-mail de quem chama, como o `setup` da #218 (P6 dela). Cancelar convite não pede. Motivo do dono: são as duas ações que transformam um access token roubado (15 min) em acesso permanente, por um usuário novo ou pela senha de outra pessoa | D1, D5, D7, D10, D11 |

**Complementos da mesma revisão**, já incorporados:

- os três achados no código atual entram na change, cada um com o caso e o guarda (D11): o hash fictício para a senha nula, o `409` do convidado em `activate` e `deactivate`, e o erro para `status` desconhecido;
- **na revisão das specs e do `tasks.md`**: o guarda do hash fictício confere o caminho (a verificação fictícia chamada, por um dublê que conta), e não o tempo; a comparação de tempo sai da suíte e vira conferência manual no fechamento (8.3), com os números registrados. Motivo: teste por tempo reprova sob carga (#226);
- as duas falhas da baseline viraram issues sem milestone (convenção 23), depois de conferir que a #210 não cobre nenhuma (ela trata de `KnowledgeDocumentCatalogTests.SweepService_…`): **#231** (`IdentityWarmupTests`) e **#232** (contagem de SQL, com a hipótese de serviço em segundo plano na janela capturada);
- os registros na #183, na #184 e na #181 autorizados.

## Open Questions

Nenhuma.

## Baseline

No worktree limpo `../buteco-agentes-181-baseline`, em `a4d3495`, sem edição nenhuma. O script `~/.cache/buteco-agents/bin/suite-181.sh` (cópia do `suite-219.sh`, só o nome muda) espera, antes de cada suíte, `CPU_Speed_Limit = 100` e os loads de 1 e de 5 min abaixo do limite (6 no frontend, 8 nas outras), acerta o relógio da VM e registra o `podman ps`. As saídas completas, com o relatório por nome (`.trx` e o JSON do Vitest), ficam em `~/.cache/buteco-agents/181-baseline/`.

**Resultado, 02/11/2026** (`git status` vazio em todas):

| suíte | resultado | duração | estado no início (load 1 / 5 min, CPU, `podman ps`) → fim |
|---|---|---|---|
| `apps/api`, 1ª | **1172 + 1 ignorado, 1 reprovado** (`IdentityWarmupTests.TheCatalogFailsOnce_…`, l. 91, `IsLoaded` falso, 38 ms) | 271 s | 3,54 / 2,43, CPU 100, vazio → 24,16 / 19,34, **CPU 69** |
| `apps/api`, 2ª (`api-2a/`) | **1172 + 1 ignorado, 1 reprovado** (`KnowledgeBaseIndexingSummaryTests.SummaryQuery_CostDoesNotGrowWithNumberOfBases`, "Expected: 3, Actual: 4" comandos) | 186 s | 2,69 / 6,55, CPU 100, vazio → 15,39 / 12,34, **CPU 53** |
| `tests/InboxOrchestratorRoundTrip.Tests` | **12/12** | 60 s | 2,05 / 7,67, CPU 100, vazio → 4,22 / 7,31, CPU 100 |
| `tests/ApiConnectorsRoundTrip.Tests` | **8/8** | 24 s | 4,22 / 7,31, CPU 100, vazio → 4,28 / 7,08, CPU 100 |
| `apps/frontend` | **1978/1978**, 154 arquivos | 101 s | 2,13 / 5,93, CPU 100, vazio → 21,26 / 11,43, CPU 100 |

- **Por nome** (`compare-trx.py` contra `219-fechamento/`): o `rt-inbox` e o `rt-connectors` são idênticos. No `apps/api`, cada rodada difere do fechamento da #219 **num teste só, e num teste diferente**: o reprovado de uma rodada passou na outra. Os outros 1172 + 1 têm o mesmo nome e o mesmo resultado.
- **Issues:** **#231** (`IdentityWarmupTests`, com a hipótese da corrida entre o aquecimento e a migration da fixture) e **#232** (`SummaryQuery_CostDoesNotGrowWithNumberOfBases`, com a hipótese de serviço em segundo plano executando comando na janela do `EmittedSqlCapture`). A #210 não cobre nenhuma.
- **As duas falhas ficam fora da área desta change** (aquecimento do emissor e custo da consulta de resumo da base de conhecimento), no mesmo commit, sem edição. **Não as classifico como ambientais** (convenção 19): falta a evidência. O que há: a classe `IdentityWarmupTests` passou **5 de 5** rodando isolada (`warmup/1..5.log`, CPU 100 e loads abaixo de 8 antes de cada uma), o que, pela convenção 15, não prova nada sobre a rodada completa. A segunda é contagem de comandos SQL, e não tempo, então a CPU não a explica sozinha.
- **No fechamento**, a comparação por nome vale contra as duas rodadas: um teste reprovado só conta como regressão desta change se reprovar em algum teste que passou nas duas.
- A primeira rodada do frontend não precisou de repetição: o `npm ci` rodou antes (`181-npm-ci-baseline.log`).
