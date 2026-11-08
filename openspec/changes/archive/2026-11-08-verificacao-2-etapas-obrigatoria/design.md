## Context

Última issue da M1. A #218 entregou o TOTP valendo só para quem o liga, com o cadastro fechado em produção por `Identity:TwoFactor:EnrollmentEnabled` (D1). A #245 deu ao `/entrar` o passo do código, a #246 o cadastro em Minha conta e a variável no compose, e a #183 a redefinição pelo admin no painel. Esta change liga a obrigatoriedade.

**Fontes lidas:**
- **A #220:** o corpo e os quatro comentários, entre eles o da #218 ([remover a opção](https://github.com/aldovrando-oliveira/buteco-agentes/issues/220#issuecomment-6565822505)) e o da #246 ([a variável do compose, o `.env.prod.example`, as seções de `docs/` e o estado do `TwoFactorCard`](https://github.com/aldovrando-oliveira/buteco-agentes/issues/220#issuecomment-6702276561)).
- **Designs arquivados:**
  - `2026-11-02-verificacao-2-etapas-totp` (#218), D1 a D12;
  - `2026-11-07-entrada-publica-painel` (#245), D1 a D3;
  - `2026-11-07-minha-conta-painel` (#246), D4, D5 e o Migration Plan;
  - `2026-11-08-editar-usuario-painel` (#183), D2 e D9.
- **O protótipo:** o canvas `H7dPBZafiSkdKd1hqesPZ6`, lido pela ferramenta Artifact em 08/11/2026 duas vezes:
  - na versão `1794066606-fba2`, antes da revisão (não tinha cadastro no login);
  - na **versão `1794112579-6ff3`** (a "versão 55" do dono), corrigida depois do primeiro design, com os estados do cadastro no `Main`, o `MinhaConta` reduzido e os textos novos do `UsuarioDetalhe`. É a referência dos textos ("Estados do protótipo").

### O código em `8471250` (origin/main, merge do PR #256)

- **`POST /auth/session`** (`apps/api/src/Buteco.Api/Users/Login/SessionEndpoints.cs`):
  - com a verificação ligada, grava o cookie pendente e responde `200 { segundoPasso: "totp" }`, sem zerar a contagem (l. 134–139);
  - sem ela, zera a contagem, registra o acesso, grava o cookie **sem** o claim `buteco_2fa` e responde `204` (l. 141–145).
- **`POST /auth/session/second-factor`** (l. 148–205):
  - aceita só cookie válido de usuário ativo, com o stamp igual e a verificação ligada; fora disso, `401` "A entrada expirou…" (l. 178–182);
  - no sucesso, grava o cookie completo (`twoFactorDone: true`).
- **O authorize** (`Identity/Flow/AuthorizeEndpoint.cs` l. 98) aceita o usuário ativo com o stamp igual **e** `(!user.IsTwoFactorEnabled || twoFactorDone)`. Quem não tem a verificação passa com qualquer cookie.
- **A renovação** (`Identity/Flow/UserGrantTokenHandler.cs` l. 65) recusa só o inexistente, o desativado e o stamp diferente.
- **O logout** (`Identity/Flow/EndSessionEndpoint.cs`): revoga a autorização do hint, apaga o `buteco-sessao` e, sem hint, redireciona para `/entrar`. Não olha o cookie.
- **O cookie** (`Identity/Flow/IssuerSession.cs`): 5 min, `AllowRefresh = false`, `HttpOnly`, `SameSite=Strict`, com `sub` e `buteco_stamp`.
- **O cadastro** (`Users/TwoFactor/AccountTwoFactorEndpoints.cs`): `setup` (senha) e `confirm` (código) pelo access token, com `403` quando `EnrollmentEnabled` é falsa (l. 80–83, 124–127); `remove` com código (l. 182–203); `GET` com `cadastroDisponivel` (l. 66). O `User.StartTwoFactorEnrollment` grava o pendente e o início (`Users/User.cs` l. 209–213); o `EnableTwoFactor` move o pendente para o ativo e o apaga (l. 220–226).
- **A redefinição pelo admin** (`Users/TwoFactor/UserTwoFactorResetEndpoint.cs`): recusa a si mesmo com `403` "Para remover a sua verificação, use Minha conta, com um código." (l. 46–51), e apaga segredo e códigos, troca o stamp e encerra as sessões (`UserSessions.EndAllAsync`) numa transação (l. 63–70).
- **O painel:**
  - `createSession` reconhece só `segundoPasso === 'totp'`. Qualquer outro valor vira `{ kind: 'http', status: 200 }` e "Não foi possível entrar. Tente de novo." (`apps/frontend/src/features/auth/api/sessionApi.ts` l. 59–63; `entryNotices.ts` l. 40);
  - o cadastro existe só no `TwoFactorCard` de Minha conta, com `TwoFactorQr` e `RecoveryCodes` à parte;
  - o `AccessLinkPage` leva o `segundo-passo` ao `/entrar` na etapa do código (l. 118);
  - a renovação trata `400` com `error: invalid_grant` como sessão encerrada (`src/auth/renewal.ts` l. 75), e o `sessionFetch` leva a `/entrar?motivo=sessao-expirada` (`src/auth/sessionFetch.ts` l. 59). Os testes `renewal.test.ts` ("invalid_grant apaga o registro e encerra") e `sessionFetch.test.ts` já fixam isso.
- **Durações:** access token 15 min, refresh 14 dias, rotativo e deslizante (`Identity/IdentityTokenLifetimes.cs` l. 23 e 31).
- **Quem faz login em teste:** no `apps/api`, 39 arquivos passam pelo `IdentityIssuerTestClient.SignInAsync` (`Support/IdentityIssuerTestClient.cs` l. 230) ou pelo `SignedInUser`; em `tests/`, os dois `IssuerUserFlow` exigem o `204` (`tests/InboxOrchestratorRoundTrip.Tests/Support/IssuerUserFlow.cs` l. 41–45, e a cópia em `ApiConnectorsRoundTrip.Tests`).

### Premissa falsa no corpo da issue (convenção 9)

O corpo diz "Sem mudança de tela: a #183 já trata o passo de cadastro". **Não trata.** O cadastro do painel está em Minha conta, que exige sessão, e o `/entrar` não tem etapa de cadastro. Ligar a obrigatoriedade só no `apps/api` deixaria **todo** usuário sem entrar. A change leva a tela (Q7), e o dono desenhou a etapa no protótipo.

## Goals / Non-Goals

**Goals:**
- Ninguém recebe sessão do emissor sem a verificação em 2 etapas.
- Quem não tem a verificação cadastra no login, depois da senha certa e antes da sessão.
- Um caminho de emergência para quem perdeu o celular e os códigos, inclusive o último Admin, que só quem tem acesso ao servidor usa.
- Remover a opção do cadastro e tudo que só existia por ela.
- Nenhum merge deixa alguém do lado de fora.

**Non-Goals:**
- **Obrigatoriedade por papel:** é a #184, na M2.
- **Trocar de app autenticador sem perder a verificação:** quem troca de celular pede a redefinição a outro usuário, ou usa o comando de emergência. É o texto do protótipo em Minha conta e em Editar usuário.
- **Tabela de auditoria:** o comando de emergência registra em log (Q4).
- **O último Admin sem a senha:** é a [#257](https://github.com/aldovrando-oliveira/buteco-agentes/issues/257) (Risks).
- **Claim `amr`:** continua fora (#218 D10). A regra é aplicada antes de qualquer token.

## Decisions

### D1. (a) O cadastro forçado no login: rotas próprias, presas ao cookie pendente (Q1)

```
 POST /auth/session {email, senha}
   │  guardas → bloqueio → senha → estado (como hoje)
   ├── com 2 etapas ──▶ cookie PENDENTE, sem zerar ────────────────▶ 200 {"segundoPasso":"totp"}      (como hoje)
   └── sem 2 etapas ──▶ zera contagem (Q9), cookie PENDENTE ───────▶ 200 {"segundoPasso":"cadastro"}  (NOVO; o 204 some)

 POST /auth/session/enrollment/setup     (sem corpo)
   │  guardas (JSON, Origin) → cookie válido, ativo, stamp igual, SEM 2 etapas?  não ─▶ 401 "A entrada expirou…"
   │  bloqueio? ─▶ 423
   └── segredo pendente NOVO; prazo = 1º setup + 10 min (D2); cookie regravado até o prazo ─▶ 200 {segredo, otpauthUri}

 POST /auth/session/enrollment/confirm   {codigo}
   │  as mesmas guardas e o mesmo cookie
   │  sem pendente ou vencido ─▶ 401 "A entrada expirou…"
   │  código errado ─▶ falha contada ─▶ 401 "Código inválido."
   └── certo ─▶ [transação] liga, apaga o pendente, grava o passo, 10 códigos
              ─▶ zera contagem, último acesso, cookie COMPLETO (5 min) ─▶ 200 {codigosRecuperacao}

 GET /connect/logout   (o "Voltar" da etapa; D3)
   └── cookie pendente de usuário SEM 2 etapas ─▶ [transação] apaga o pendente, troca o stamp ─▶ apaga o cookie ─▶ 302 /entrar

 GET /connect/authorize
   só cookie COMPLETO de usuário COM 2 etapas ─▶ código
   qualquer outro ─▶ como sem sessão (302 /entrar, ou login_required)
```

**As três formas consideradas de dar o cadastro a quem ainda não tem sessão:**

| | (A) rotas novas sob `/auth/session/enrollment`, pelo cookie | (B) as rotas de `/account/two-factor` aceitando também o cookie | (C) o authorize emite um token restrito ao cadastro |
|---|---|---|---|
| prova de quem é | o cookie pendente, que só a senha certa grava | a mesma, por um segundo esquema na rota | o access token, com um escopo ou claim novo |
| CSRF | as guardas de hoje (só JSON, `Origin`), como o `second-factor` | rota com cookie precisa das guardas, que as rotas de `/account` não têm | — |
| quem confere a restrição | as duas rotas | as duas rotas, com política de dois esquemas | **as rotas dos três apps**: o token vale por cliente (`IssuerTokenAuthentication.MapPrincipalByClient`, `Identity/Validation/IssuerTokenAuthentication.cs` l. 74) |
| `nginx.conf` | nada: `/auth` já vai ao `apps/api` (l. 79) | nada | nada |

**Escolhida: (A)** (Q1, aceita). O cadastro do login é o irmão do `second-factor`: mesmo cookie, mesmas guardas, mesmo limite global (`LoginRateLimiting.Marker`), mesma classificação `AuthEntryPoint`.

**Regras fixadas pelo dono, e onde ficam nas specs (`issuer-user-login`):**
- **o cookie em cadastro não é aceito pelo authorize nem por nenhuma rota além de `setup`, `confirm` e do logout.** O authorize exige a verificação ligada (D5), e o `second-factor` já recusa quem não a tem (l. 178);
- **chamar o `setup` de novo troca o segredo pendente.** É o que o "Tentar de novo" do QR usa;
- **o `confirm` certo invalida o cadastro pendente e só então libera o fluxo normal:** o `EnableTwoFactor` apaga o pendente na mesma transação que liga a verificação, e o cookie completo é gravado depois do commit;
- **"Voltar" encerra o cookie pendente no servidor** (D3).

**Sem abrir o cadastro a quem tem o cookie pendente de outra pessoa:**
- as rotas não recebem usuário nem e-mail: atuam só no `sub` do cookie, com o stamp conferido, como o `second-factor` (l. 174–182);
- o cookie só existe para quem acertou a senha daquele e-mail, e não sai do navegador (`HttpOnly`, `SameSite=Strict`, `Secure` fora de desenvolvimento);
- o cookie pendente de quem já tem a verificação não cadastra: as duas rotas exigem `IsTwoFactorEnabled == false`;
- é menos poder do que hoje: em `8471250`, o mesmo cookie de quem não tem a verificação já **é** a sessão (o authorize o aceita, l. 98). Depois desta change, ele só cadastra;
- **sem pedir a senha de novo no `setup`** (Q1). Na #218 (D8, P6), a senha protegia o cadastro contra um access token roubado. Aqui o cookie acabou de provar a senha.

**O que o `POST /auth/session` responde:** `200 { "segundoPasso": "cadastro" }`. É o espaço que o P2 da #218 deixou. O `204` deixa de existir nessa rota.

### D2. O prazo do cadastro: 10 minutos do primeiro `setup`, sem esticar (Q2)

- **O problema:** o cookie pendente vive 5 min a partir da senha. Quem instala o app autenticador na hora passa disso.
- **O primeiro `setup`** grava no cookie um claim novo, `buteco_cadastro_ate` (o instante do fim do prazo, em segundos Unix), igual a agora + 10 min, e regrava o cookie com `ExpiresUtc` igual a esse instante. O cadastro pendente no banco ganha o início correspondente (`two_factor_pending_at` = fim − 10 min).
- **Um `setup` seguinte** lê o claim do cookie, troca o segredo pendente e regrava o cookie com o **mesmo** vencimento e o mesmo início. Nenhuma chamada estende o prazo (Q2, aceita com a condição do dono).
- **O `confirm`** depende do cookie, que vence no fim do prazo, e do pendente, que vence no mesmo instante. Os dois testes do D9 fixam o teto.
- **O `confirm` exige o `setup` do mesmo cookie** (o claim do prazo): o cookie pendente de um login seguinte, sem `setup`, recebe `401` "A entrada expirou…", mesmo com um pendente de um cookie anterior ainda no prazo. **Decidido na implementação** (premissa 4 do relatório do apply; contrato mais restrito, seguido sem parar e aceito pelo revisor em 08/11/2026), com cenário em `issuer-user-login` e o caso `ConfirmWithoutASetupOnThisCookie_TheEntryExpired`.
- **Evidência:** o handler do cookie só calcula o vencimento pelo `ExpireTimeSpan` quando a propriedade não vem preenchida (`dotnet/aspnetcore`, `release/10.0`, `src/Security/Authentication/Cookies/src/CookieAuthenticationHandler.cs` l. 323–325: `if (!signInContext.Properties.ExpiresUtc.HasValue) { … issuedUtc.Add(Options.ExpireTimeSpan); }`). O `IssuerSession.SignInAsync` ganha o vencimento e o claim opcionais; os chamadores de hoje não mudam.
- **O cookie é cifrado e assinado** pelo Data Protection, então o claim não se forja.
- **O completo,** gravado no `confirm`, volta aos 5 min. Quem demora mais de 5 min lendo os códigos cai no `/entrar` pelo authorize e entra com o código do app, que já tem.

### D3. "Voltar" na etapa: o logout encerra o cookie no servidor

- **O painel** navega a `GET /connect/logout` sem `id_token_hint`. Hoje essa chamada apaga o cookie e redireciona para `/entrar` (`EndSessionEndpoint.cs`), que abre em `inicio`. É o logout que o dono listou como a única rota, além das duas de cadastro, que aceita o cookie em cadastro.
- **O logout passa a ler o cookie do emissor.** Se ele for o pendente válido de um usuário **sem** a verificação, o logout, numa transação, apaga o cadastro pendente e troca o `security_stamp`, e então apaga o cookie.
- **Por que trocar o stamp:** o cookie não tem estado no servidor; apagar do navegador não invalida uma cópia. Trocar o stamp invalida qualquer cópia, porque as rotas de cadastro conferem o stamp. **O custo é nulo** para a pessoa: sem a verificação, ela não tem sessão que o stamp derrube (o authorize e a renovação já a recusam, D4 e D5).
- **Quem pode disparar:** só quem manda o cookie. Ele é `SameSite=Strict`, então uma navegação vinda de outro site não o leva.
- **O passo do código** (quem tem a verificação) continua com o "Voltar" da #245, que só troca a etapa.

### D4. (c) As sessões abertas no deploy: a renovação exige a verificação (Q3)

| | (1) continuam até vencer | (2) a renovação exige a verificação | (3) troca do stamp de todos sem a verificação, na migração |
|---|---|---|---|
| quando acaba a sessão sem a verificação | **talvez nunca**: o refresh é deslizante de 14 dias, e quem usa o painel todo dia renova para sempre | até 15 min depois do deploy | logo depois do deploy |
| código | nenhum | uma condição no `UserGrantTokenHandler` (l. 65) | uma migration de dados |
| volta atrás | — | a imagem anterior | não volta |

**Escolhida: (2)** (Q3, aceita). A recusa é a do OpenIddict: `400` com `{ "error": "invalid_grant" }`, que o painel já trata como sessão encerrada e leva ao `/entrar` com o aviso de sessão expirada, sem estado novo.

**O teste de contrato** (pedido do dono): no `apps/api`, a renovação de quem não tem a verificação responde `400`, `Content-Type` JSON e `error` igual a `invalid_grant`, exatamente o que `renewal.ts` l. 75 lê. O nome do teste aponta o consumidor (`Renewal_WithoutTwoFactor_IsInvalidGrant_ThePanelEndsTheSession`), como na #245 D1. No painel, os casos que já fixam esse formato (`renewal.test.ts` "invalid_grant apaga o registro e encerra"; `sessionFetch.test.ts` "invalid_grant na renovação encerra…") continuam sem mudança, e entram na comparação por nome do fechamento.

### D5. (d) O que muda no backend

- **`POST /auth/session`:** o ramo sem a verificação responde `200 { segundoPasso: "cadastro" }` com o cookie pendente (D1). O último acesso passa ao `confirm`.
- **Rotas novas:** `POST /auth/session/enrollment/setup` e `/confirm` (D1, D2). A lógica (segredo novo, URI, conferência do pendente, ligar, gerar os códigos) sai do `AccountTwoFactorEndpoints` para um `TwoFactorEnrollment`.
- **Authorize** (l. 98): `user.IsTwoFactorEnabled && twoFactorDone`.
- **Renovação** (D4): `!user.IsTwoFactorEnabled` → `invalid_grant`.
- **Logout** (D3).
- **Removidos:**
  - `TwoFactorOptions` e a seção `Identity:TwoFactor` dos dois `appsettings`, e o override de `Support/TestAuthentication.cs` (l. 43);
  - `cadastroDisponivel` do `GET /account/two-factor` e o `EnrollmentClosedMessage` (Q6: sai do contrato, com o painel no mesmo PR);
  - `POST /account/two-factor/remove`, `setup` e `confirm` por access token (Q5). Fica um caminho de cadastro só, o do login. Ficam o `GET` e a regeneração dos códigos;
  - `Identity__TwoFactor__EnrollmentEnabled` do `docker-compose.prod.yml` e o bloco do `.env.prod.example`.
- **O `403` da redefinição de si mesmo** (Q8): "Você não pode redefinir a própria verificação. Peça a outro usuário, ou use o comando de emergência no servidor.", o texto do `UsuarioDetalhe` (`acao=redefinir-2fa-proprio`).
- **A extração `TwoFactorReset`**, usada pela rota do admin e pelo comando (D6).

### D6. (b) O caminho de emergência: um comando no binário do `apps/api` (Q4)

```
 docker compose -f docker-compose.prod.yml --env-file .env.prod \
   run --rm --no-deps api two-factor-reset --email maria@empresa.com
```

- **O que é:** o `Program.cs` olha `args` antes de montar o host web. Com `two-factor-reset`, sobe um host mínimo (configuração, log, `AppDbContext`, `UserSessions`, `TwoFactorReset`), sem Kestrel, sem rotas e sem serviços em segundo plano, roda e sai. A imagem já tem `ENTRYPOINT ["dotnet", "Buteco.Api.dll"]` (`apps/api/Dockerfile`), e o `run` acrescenta os argumentos.
- **O que faz:** acha o usuário pelo e-mail normalizado (`EmailNormalization`) e chama o `TwoFactorReset`, **a mesma implementação da rota do admin**: apaga o segredo, o pendente, a ativação, o último passo e os códigos, troca o stamp e encerra as sessões, numa transação. Os testes cobrem as duas entradas sobre ele, e um compara o estado final das duas (D8).
- **Saídas** (`two-factor-totp`): `0` redefinida; `1` nenhum usuário com o e-mail; `2` usuário desativado; `3` usuário sem a verificação (inclusive convidado); `64` uso errado; **`70` falha ao falar com o banco ou erro inesperado** (decisão do dono, 08/11/2026), sem efeito, porque a transação não se completa. O `1` só sai de uma consulta que respondeu: uma exceção nunca vira "usuário inexistente", nem outro código de resultado. A linha do `70` traz o tipo do erro, e não a string de conexão nem a senha. Não pergunta nada.
- **Não aplica migrations.** O comando usa o `AppDbContext` só para ler e gravar; quem migra é o serviço `migrator` do compose.
- **O log:** uma linha `Warning` com `EventId` próprio, o resultado, o id do usuário, o e-mail e o número de sessões encerradas. Nenhuma saída leva segredo, código, hash ou stamp. Com `run --rm`, o registro é o terminal de quem rodou, e o efeito fica visível em Usuários ("Não configurada"). Sem tabela de auditoria (Q4).
- **Quem pode usar:** quem roda `docker compose` no host do stack. Essa pessoa já lê o `.env.prod`, com a senha do Postgres e as chaves. O comando não dá poder novo; dá o caminho certo (stamp e sessões juntos, que o SQL à mão esquece).
- **Por que não abre porta:** não é rota HTTP; os argumentos vêm do comando do contêiner, e o processo web em execução nunca os lê de requisição; o processo do comando não escuta porta. O `RouteAuthenticationTests` confere que as únicas rotas anônimas novas são as duas de cadastro.
- **Alternativa descartada: variável de ambiente lida no boot.** A casa já recusou o padrão no bootstrap: "reaplicar a configuração a cada boot seria uma porta dos fundos permanente para quem tem o `.env`" (`Users/Bootstrap/UserBootstrapper.cs`, comentário da classe).

### D7. (e) O que muda no painel

- **`sessionApi`:** `createSession` devolve `{ kind: 'segundo-passo', passo: 'totp' | 'cadastro' }`; entram `startEnrollment()` e `confirmEnrollment(codigo)`, sem token, como o `submitSecondFactor`. Um terceiro valor continua no aviso genérico.
- **`/entrar`, a etapa `"cadastro"`**, com os estados do quadro `Main` (ver "Estados do protótipo"):
  - abre chamando o `setup`: "Gerando o QR…", depois o QR e o segredo;
  - **falha do `setup`** (`429`, `503`, `500`, rede): "Não foi possível gerar o QR." com "Tentar de novo", o campo do código desabilitado. O `423` do `setup` mostra também o aviso de bloqueio. O `401` "A entrada expirou…" volta às credenciais com o `mfa-expirou`. **Decisão de leitura do quadro:** os avisos `cadastro-muitas-tentativas`, `cadastro-indisponivel` e `cadastro-falha-de-rede` são do `confirm`; o `setup` usa o `cadastro-qr-falhou`, que é o quadro que desenha a falha sem QR. Vai para a validação visual;
  - o `confirm` com os tratamentos da #245 (D1): os dois `401` separados pela constante `ENTRADA_EXPIROU`, `423`, `429` e `503` com o botão desabilitado até o `Retry-After`, rede mantendo o código;
  - os códigos ("Verificação ligada", "Copiar todos", "Baixar .txt", "Continuar"), e "Continuar" chama o `continuarEntrada()` da #245;
  - "Voltar" navega ao `/connect/logout` (D3).
- **Convite e redefinição** (`AccessLinkPage`): o `"cadastro"` leva ao `/entrar` na etapa do cadastro com "Senha salva. Falta só cadastrar o app autenticador para entrar." (`cadastro-apos-nova-senha`). Todo convidado novo passa por aqui.
- **O `state` `{ etapa: "cadastro" }` é limpo ao ser lido** (decisão do dono, 08/11/2026): o navegador guarda o `state` do router no histórico, e recarregar reabriria a etapa. O `/entrar` o lê uma vez e chama `navigate(location.pathname + location.search, { replace: true })`.
  - **Observação, sem mudança nesta change:** em `8471250`, o `/entrar` lê o `state` só na montagem e **nunca o limpa** (`EntrarPage.tsx` l. 47–48). Os dois `state` de hoje, `{ email, status }` (o login recusado depois do link) e `{ etapa: "codigo" }` (o passo do código depois do link), sobrevivem ao recarregar: o e-mail volta preenchido com o aviso, ou o passo do código reabre com o cookie pendente talvez vencido. Ficam como estão, por decisão do dono.
- **Componentes:** `TwoFactorQr` e `RecoveryCodes` saem de `features/account` para `features/two-factor/components/`. O `RecoveryCodes` recebe o título, o rótulo do download ("Baixar arquivo .txt" em Minha conta, "Baixar .txt" no login) e o botão final ("Concluir" ou "Continuar"), que são os textos de cada quadro.
- **Minha conta:** o cartão fica com `ativa`, `poucos-codigos`, `regenerar*`, `codigos` e `nao-configurada`. Saem a remoção, o cadastro e `indisponivel`; o subtítulo da página passa a "…a sua senha, os códigos de recuperação e os navegadores…".
- **Editar usuário:** os textos do `UserTwoFactorCard` e da confirmação (`userDetailTexts.ts`) passam aos do quadro.

### D8. Bloqueio e contagem

- **A senha certa sem a verificação zera a contagem, como hoje (Q9).** No cadastro não há segredo a adivinhar.
- **O `confirm` errado conta falha** e o `setup` respeita o `423`.

### D9. Testes

Os guardas de cada linha são conferidos contra o defeito (convenção 15).

**`apps/api`, integração:**

| teste | o que afirma | guarda contra |
|---|---|---|
| `TwoFactorRequiredLoginTests` | sem a verificação: senha → `200 {segundoPasso:"cadastro"}`; authorize com esse cookie → `302 /entrar`; `setup` → segredo e URI; `confirm` errado → `401`, contado; certo → 10 códigos, pendente apagado, cookie completo; authorize → código; token → access token | authorize sem a condição `IsTwoFactorEnabled` |
| | `setup` e `confirm` sem cookie, vencido, com stamp trocado, de desativado, ou de quem já tem a verificação → `401` "A entrada expirou…", sem contar falha | rota que aceita o cookie de quem já tem a verificação |
| | o cookie de A não cadastra B (o segredo de A com o cookie de B → `401`) | rota que lê usuário do corpo |
| | `setup` duas vezes: o código do primeiro segredo é recusado, o do segundo aceito | `setup` que mantém o segredo anterior |
| | **o teto do prazo:** `setup` no minuto 0 e no 8, `confirm` no 10,5 → `401`; `setup` no minuto 4 da senha e `confirm` no 13 → `200` (`FakeTimeProvider`) | `setup` que estende o prazo; `ExpiresUtc` não passado (vence aos 5) |
| | o cookie em cadastro no `second-factor` → `401` "A entrada expirou…" | — |
| | o código do `confirm` não serve no `second-factor` seguinte | — |
| `TwoFactorEnrollmentLogoutTests` | logout com o cookie em cadastro: pendente apagado, stamp trocado, cookie apagado, `302 /entrar`; um `confirm` com a cópia do cookie → `401`. Logout com o cookie completo de quem tem a verificação: stamp **não** muda | troca do stamp retirada (a cópia confirma) |
| `TwoFactorRequiredRenewalTests` | refresh de quem não tem a verificação → `400`, JSON, `error: invalid_grant` (`Renewal_WithoutTwoFactor_IsInvalidGrant_ThePanelEndsTheSession`); de quem tem → renova | condição retirada do `UserGrantTokenHandler` |
| `TwoFactorEmergencyResetTests` | pela mesma entrada do `Program.cs`, contra o Postgres do Testcontainers: `0` e o efeito; `1`, `2`, `3` e `64` sem efeito; a linha `Warning` com o `EventId`, e nenhum segredo, código ou stamp na saída capturada | troca do stamp retirada; ramo de `args` depois do `app.Run` |
| `TwoFactorResetParityTests` | o mesmo estado inicial redefinido pela rota e pelo comando termina igual (colunas, códigos, stamp trocado, autorizações revogadas) | uma das entradas sem o `TwoFactorReset` |
| `RouteAuthenticationTests` (existente) | as duas rotas novas classificadas como `AuthEntryPoint`; `remove`, `setup` e `confirm` por token fora do mapeamento | — |
| `TwoFactorAdminResetTests` (existente) | o `403` de si mesmo com o texto novo; o login seguinte do redefinido → `"cadastro"` | — |
| removidos | os casos de cadastro fechado e os de `setup`/`confirm`/`remove` por token. **Reduz cobertura de rota que sai**, e não de comportamento que fica | — |

- **O apoio de teste:** o `SessionCookieAsync` de `IdentityIssuerTestClient` passa a cadastrar quem não tem a verificação (`setup`, código pelo `Totp` e o `FakeTimeProvider`, `confirm`). Os 39 arquivos que entram pelo login continuam sem mudança.

**`apps/frontend` (Vitest):** a etapa do cadastro em todos os estados do quadro; "Voltar" navegando ao `/connect/logout`; o `AccessLinkPage` com o `"cadastro"`; o `createSession` com os dois valores e um desconhecido; o `TwoFactorCard` com os estados que ficam; os textos novos do `UserTwoFactorCard`; o segredo e os códigos fora do `QueryCache` e do `MutationCache` também no `/entrar`.

**`tests/`:** os dois `IssuerUserFlow` cadastram o Admin do bootstrap quando recebem `"cadastro"`, com um cálculo de TOTP próprio de cada projeto (convenção 11).

## Árvore de pastas proposta

```
apps/api/src/Buteco.Api/
├── Program.cs                                   + o ramo de args do comando, antes do host web
├── appsettings.json, appsettings.Development.json   − a seção Identity:TwoFactor
├── Identity/Flow/
│   ├── AuthorizeEndpoint.cs                     exige a verificação (D5)
│   ├── EndSessionEndpoint.cs                    o cookie em cadastro: apaga o pendente e troca o stamp (D3)
│   ├── IssuerSession.cs                         + vencimento e claim buteco_cadastro_ate opcionais (D2)
│   └── UserGrantTokenHandler.cs                 renovação exige a verificação (D4)
└── Users/
    ├── User.cs                                  StartTwoFactorEnrollment com o início dado (D2)
    ├── Login/SessionEndpoints.cs                200 {segundoPasso:"cadastro"}; + enrollment/setup e /confirm (D1)
    └── TwoFactor/
        ├── TwoFactorEnrollment.cs               NOVO: setup e confirm, extraídos (D5)
        ├── TwoFactorReset.cs                    NOVO: a redefinição, extraída (D6)
        ├── TwoFactorEmergencyCommand.cs         NOVO: o comando two-factor-reset (D6)
        ├── AccountTwoFactorEndpoints.cs         − cadastroDisponivel; − remove, setup, confirm
        ├── UserTwoFactorResetEndpoint.cs        sobre o TwoFactorReset; texto do 403
        └── TwoFactorOptions.cs                  REMOVIDO

apps/api/tests/Buteco.Api.Tests/
├── Support/IdentityIssuerTestClient.cs          cadastra no login quem não tem a verificação
├── Support/TestAuthentication.cs                − EnrollmentEnabled
└── Users/TwoFactor/
    ├── TwoFactorRequiredLoginTests.cs           NOVO
    ├── TwoFactorEnrollmentLogoutTests.cs        NOVO
    ├── TwoFactorRequiredRenewalTests.cs         NOVO
    ├── TwoFactorEmergencyResetTests.cs          NOVO
    ├── TwoFactorResetParityTests.cs             NOVO
    ├── TwoFactorEnrollmentTests.cs              − cadastro fechado; − rotas por token
    └── TwoFactorAdminResetTests.cs              texto do 403; login seguinte

apps/frontend/src/
├── features/two-factor/components/              NOVO: TwoFactorQr e RecoveryCodes, movidos de features/account
├── features/auth/
│   ├── api/sessionApi.ts                        passo "totp" | "cadastro"; startEnrollment, confirmEnrollment
│   ├── components/EnrollmentForm.tsx            NOVO: a etapa do cadastro
│   ├── messages.ts                              os textos da etapa
│   └── pages/EntrarPage.tsx, AccessLinkPage.tsx a etapa "cadastro"
├── features/account/
│   ├── api/accountApi.ts                        − cadastroDisponivel; − removeTwoFactor, setup, confirm
│   ├── components/TwoFactorCard.tsx             só ativa, poucos-codigos, regenerar*, codigos, nao-configurada
│   └── pages/AccountPage.tsx                    o subtítulo
└── features/users/
    ├── userDetailTexts.ts                       a confirmação
    └── components/UserTwoFactorCard.tsx         os textos do quadro

tests/InboxOrchestratorRoundTrip.Tests/Support/IssuerUserFlow.cs   cadastro do Admin do bootstrap
tests/ApiConnectorsRoundTrip.Tests/Support/IssuerUserFlow.cs       o mesmo, espelhado

docker-compose.prod.yml, .env.prod.example      − a variável do cadastro
docs/deployment.md                               − a seção da #246; + o comando de emergência e as notas da #220
docs/configuration.md, docs/development.md       − a opção; o cadastro em desenvolvimento passa pelo login
docs/architecture.md                             o diagrama do login (l. 1079 e 1172) com o "cadastro"
SECURITY.md, 01, 02, CHANGELOG.md
```

**Nada vai para `libs/`.** Os três apps não compartilham código de identidade.

## Tamanho e divisão (g)

**Âncoras medidas** (convenção 18, só código, sem migrations): PR #228 (#217), **+1.389 / −381** em produção e **+2.264 / −464** em testes; PR #224 (#180), +2.036 / +2.984.

**Projeção por componente** (rascunho, erra para baixo por construção):

| componente | produção | testes |
|---|---|---|
| login, rotas de cadastro, prazo, authorize, renovação, logout (D1 a D5) | +200 / −20 | +520 |
| extrações `TwoFactorEnrollment` e `TwoFactorReset` | +90 / −80 | +60 |
| comando de emergência (D6) | +110 | +170 |
| remoções (opção, campo, rotas) | −140 | −280 |
| apoio de teste no `apps/api` e os dois `IssuerUserFlow` | — | +200 |
| painel: `sessionApi`, etapa do cadastro, `AccessLinkPage`, componentes movidos | +300 / −20 | +460 |
| painel: Minha conta e Editar usuário | +20 / −240 | +40 / −250 |
| **total** | **~+720 / −500** | **~+1.450 / −530** |

Com o erro medido (+57 % na #180; +70 % nas linhas de teste da convenção 18), ~1.130 / ~2.470. **Uma change só** (Q7, decisão do dono), na faixa da #217.

**A ordem que nunca tranca ninguém:** tudo chega no mesmo merge. O painel recebe `"cadastro"` só depois de saber tratá-lo, e o servidor recusa a sessão sem a verificação só junto com o cadastro no login.

## Risks / Trade-offs

- **[Quem tem a senha de uma conta que ainda não cadastrou a verificação cadastra o próprio app antes do dono]** → a janela vai do deploy até o primeiro login de cada pessoa. **Não resolvido nesta change.** Mitigação existente: o admin vê "Não configurada" em Usuários e em Editar usuário e pode redefinir a verificação; a pessoa legítima percebe ao não conseguir entrar (o código não é o do app dela). Nota de deploy: avisar a equipe para entrar logo depois da virada.
- **[O último Admin perde a senha]** → **não coberto**: a redefinição de senha precisa de outro usuário (#181). É o mesmo problema do comando de emergência, do lado da senha, e está na **[#257](https://github.com/aldovrando-oliveira/buteco-agentes/issues/257)** (comando `password-reset-link`, no backlog, sem item na #194).
- **[Todo mundo sai até 15 min depois do deploy]** → é o custo da Q3. As notas de deploy avisam antes.
- **[O último Admin perde o celular e os códigos]** → o comando de emergência (D6).
- **[Relógio do host fora do NTP]** → os códigos param de bater para todos, inclusive no cadastro. O `docs/deployment.md` já exige NTP (#218 D3), e as notas de deploy repetem a conferência.
- **[A pessoa demora mais de 5 min no passo dos códigos]** → a sessão completa do `confirm` dura 5 min, e "Continuar" depois disso leva o authorize a mandar para `/entrar`. Mitigação: é a mesma regra do segundo passo, e os códigos já foram mostrados; a pessoa entra com a senha e o código do app que acabou de cadastrar (cenário em `operator-login-ui`).
- **[A etapa do login tem só um quadro de falha do QR]** → a leitura do D7 (o `setup` usa o `cadastro-qr-falhou`; os avisos são do `confirm`) vai para a validação visual.
- **[Rollback com usuários já cadastrados]** → a imagem anterior verifica o código no login (#218, #245): ninguém fica preso. Ver Migration Plan.

## Migration Plan (h)

**Antes do deploy (o dono):**
1. conferir o NTP do host: `timedatectl` com "System clock synchronized: yes";
2. **avisar a equipe:** depois do deploy, todos saem em até 15 min e, ao entrar, cadastram um app autenticador (Google Authenticator, Microsoft Authenticator, 1Password) e guardam os 10 códigos fora do celular. **Entrar logo depois da virada** fecha a janela do primeiro risco;
3. ter o próprio celular à mão e o comando de emergência anotado (D6).

**Deploy:** a imagem do `api` e a do `frontend`, juntas. Sem migration. `IDENTITY_TWO_FACTOR_ENROLLMENT_ENABLED` pode sair do `.env.prod`; se ficar, nada lê.

**Conferir:**
- o dono entra: senha → QR → código → 10 códigos → painel;
- o login seguinte pede o código;
- em Usuários, a coluna da verificação vai ficando "Ativa" conforme as pessoas entram;
- `curl -X POST <painel>/auth/session/enrollment/setup` sem cookie (com `Origin` e JSON) responde `401` do `apps/api`, e não `200` com HTML;
- o comando de emergência com um e-mail inexistente sai `1`, sem mudar nada.

**Cada usuário:** no próximo acesso, senha, QR, código e os códigos de recuperação guardados.

**Voltar atrás:** a imagem anterior do `api` e do `frontend`.
- quem cadastrou continua com a verificação, e a imagem anterior a pede no login (#218) e o painel anterior tem o passo do código (#245): ninguém fica preso;
- quem não cadastrou volta a entrar só com a senha;
- com a variável do cadastro fora do `.env.prod`, Minha conta volta a dizer "cadastro indisponível" para quem não tem. Para reabrir, `IDENTITY_TWO_FACTOR_ENROLLMENT_ENABLED=true` (#246);
- o comando de emergência some com a imagem.

## Estados do protótipo (f)

Desenhados pelo dono na versão `1794112579-6ff3`, depois da lista do primeiro design. São a referência dos textos.

**`Main` (`estado`):**

| item | estado | o que a tela mostra |
|---|---|---|
| a | `cadastro` | "Cadastre a verificação em 2 etapas", o texto da obrigatoriedade, o QR, o segredo com "Copiar", o campo, "Confirmar", "Voltar" |
| b | `cadastro-qr-carregando`, `cadastro-qr-falhou` | "Gerando o QR…"; "Não foi possível gerar o QR." com "Tentar de novo"; o campo desabilitado sem QR |
| c | `cadastro-codigo-vazio`, `cadastro-codigo-invalido` | "Informe o código."; "Código inválido. Confira se o relógio do celular está certo." |
| d | `cadastro-bloqueado`, `cadastro-muitas-tentativas`, `cadastro-indisponivel`, `cadastro-falha-de-rede` | os avisos do `423`, `429`, `503` e rede |
| e | `cadastro-codigos` | "Verificação ligada", os 10 códigos, "Copiar todos", "Baixar .txt", "Continuar" |
| f | "Voltar" | leva a `inicio` (pelo logout, D3) |
| g | `cadastro-apos-nova-senha` | "Senha salva. Falta só cadastrar o app autenticador para entrar." |
| — | `mfa-expirou` (existente) | o vencimento dos 10 min |

**`MinhaConta` (`verificacao`):** `ativa` (com "Trocou de celular ou perdeu o app?…" e sem "Remover verificação"), `poucos-codigos`, `regenerar`, `regenerar-codigo-invalido`, `regenerar-bloqueado`, `codigos` e `nao-configurada` ("Este painel passou a exigir a verificação em 2 etapas. Na próxima vez que você entrar, o login pede o cadastro do app autenticador.").

**`UsuarioDetalhe` (`entrega=m1`):** `verificacao=desligada` ("A pessoa cadastra a verificação no próximo login."); `ativa` ("Use Redefinir se a pessoa perdeu ou trocou o celular e os códigos de recuperação."); a confirmação de `redefinir-2fa` ("…No próximo login, ela cadastra o app autenticador de novo."); `alvo=proprio` ("Para trocar de celular, peça a outro usuário que redefina a sua verificação."); `acao=redefinir-2fa-proprio` (o aviso do `403`).

**Conferência contra o contrato:** todos os textos cabem. Três observações, nenhuma de parada:
- **`redefinir-2fa-proprio`:** o quadro mostra o aviso no layout do próprio, mas o próprio não tem o botão (`mfaBotaoAtivo: !proprio`). Na tela, o `403` só aparece quando o reconhecimento falha, e então o layout é o de "outro" (#183 D2). O texto é o mesmo.
- **`cadastro-bloqueado`** escreve "10 minutos" fixo; a tela usa o N do `Retry-After`, como no `mfa-bloqueado` (#217, convenção 17).
- **O nome do download** difere entre os quadros ("Baixar .txt" no login, "Baixar arquivo .txt" em Minha conta); a tela segue cada quadro.

## Validação visual

Na homologação da M1, sem parada (decisão do dono, 08/11/2026): o PR é mergeado antes, e o dono confere quadro a quadro depois. O checklist vai igual no `tasks.md`, no `02` e no PR:

- `Main`: os estados a–g da tabela acima, o `mfa-expirou` vindo da etapa do cadastro, e "Continuar" depois de 5 minutos no `cadastro-codigos`; a leitura do D7 sobre a falha do `setup`; claro e escuro, com o QR em fundo branco no escuro;
- `MinhaConta`: os sete estados de `verificacao` e o subtítulo novo;
- `UsuarioDetalhe` `entrega=m1`: `verificacao=desligada` e `ativa` em `outro` e `desativado`; `acao=redefinir-2fa`; `alvo=proprio`; o `403` de `redefinir-2fa-proprio` (com o reconhecimento forçado a falhar);
- `AceitarConviteSemProvedor` e `RedefinirSenha`: a chegada à etapa do cadastro.

## Baseline

No worktree limpo `../buteco-agentes-220-baseline`, em `8471250`, sem edição nenhuma, pelo `~/.cache/buteco-agents/bin/suite-220.sh` (cópia do `suite-183.sh` com as suítes `api frontend rt-inbox rt-connectors`; espera CPU 100 e load abaixo do limite antes de cada suíte, amostra a CPU durante, veredito assimétrico). Saídas completas e o relatório por nome (`nomes.txt`) em `~/.cache/buteco-agents/220-baseline/`.

| suíte | resultado | duração | estado (início → fim) | CPU mínima |
|---|---|---|---|---|
| `apps/api` | **1298/1298 + 1 ignorado** | 341 s | load 7,32, CPU 100 → load 34,71, CPU 53 | 51 |
| `apps/frontend` | **2329/2329**, 185 arquivos | 144 s | load 1,93, CPU 100 → load 23,50, CPU 72 | 60 |
| `tests/InboxOrchestratorRoundTrip.Tests` | **14/14** | 95 s | load 3,26, CPU 100 → load 14,72, CPU 100 | 62 |
| `tests/ApiConnectorsRoundTrip.Tests` | **10/10** | 39 s | load 3,16, CPU 100 → load 3,84, CPU 100 | 100 |

- **As quatro APROVADAS** (regra assimétrica da #181): a CPU caiu em três delas depois de largar em 100, e verde conta mesmo assim. As durações das três não servem de referência.
- `nomes.txt`: 3652 linhas (1299 do `apps/api`, com 1 GUID normalizado, 2329 do frontend, 14 e 10 dos acordos), a base da comparação por nome no fechamento.
- O frontend rodou com o broker de pé (`PAUSAR_RABBIT=0`, healthcheck TCP da #251).
- `apps/inbox`, `apps/connectors`, `apps/workers` e o `CrossAppTaskStoreCompatibility.Tests` não são medidos: o código deles não muda, e nenhum passa pelo `POST /auth/session`.

## Decisões do dono (08/11/2026)

Na revisão do proposal e do design, com o protótipo corrigido:

| | pergunta | decisão | onde |
|---|---|---|---|
| Q1 | Como o cadastro funciona sem sessão? | rotas presas ao cookie pendente, sem pedir a senha de novo; o cookie em cadastro só vale nelas e no logout; `setup` de novo troca o segredo; o `confirm` certo apaga o pendente antes de liberar o fluxo; "Voltar" encerra o cookie no servidor | D1, D3 |
| Q2 | Quanto dura o cookie no cadastro? | o `setup` regrava o cookie, mas o vencimento nunca passa do primeiro `setup` + 10 min, com teste | D2 |
| Q3 | As sessões abertas no deploy? | a renovação exige a verificação; teste de contrato do formato que o `sessionFetch` já trata, sem estado novo | D4 |
| Q4 | O caminho de emergência? | o comando, sem rota HTTP e sem tabela de auditoria, `Warning` com `EventId`; saída diferente de zero para inexistente ou desativado; nenhum segredo na saída; efeito idêntico ao do admin pelo `TwoFactorReset`, testado nas duas entradas. **Complemento:** o último Admin sem senha vira a #257 | D6, Risks |
| Q5 | Remover a própria verificação, e o cadastro em Minha conta? | um caminho só, o do login; saem `remove`, `setup` e `confirm` por token; Minha conta fica só com gerar novos códigos | D5, D7 |
| Q6 | `cadastroDisponivel`? | sai do contrato, com o painel no mesmo PR | D5 |
| Q7 | Uma change ou dividir? | uma change só, como na #217 | Tamanho |
| Q8 | O texto do `403` de redefinir a si mesmo? | o do protótipo | D5 |
| Q9 | A senha certa sem a verificação zera a contagem? | sim | D8 |

**Risco registrado por pedido do dono:** a conta sem a verificação cadastrada por quem tem a senha (Risks), com a nota de deploy.

## Registros (convenção 23)

- **#257** ([issue](https://github.com/aldovrando-oliveira/buteco-agentes/issues/257)), aberta em 08/11/2026: o comando `password-reset-link`, no backlog, sem item na #194.
- **No fechamento:** na #220, a entrega e a validação visual na homologação; na #194, a M1 com 18 de 18; na #184, que a obrigatoriedade vale para todos e o "Opcional para Operador" do `entrega=completa` volta com os papéis.

## Open Questions

Nenhuma.
