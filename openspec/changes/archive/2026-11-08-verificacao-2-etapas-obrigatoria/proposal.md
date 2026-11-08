**Issue:** #220 (M1 · 18 de 18; depende da #183, mergeada em `8471250`). Leva também o pedido da #218 ([comentário](https://github.com/aldovrando-oliveira/buteco-agentes/issues/220#issuecomment-6565822505)) e o da #246 ([comentário](https://github.com/aldovrando-oliveira/buteco-agentes/issues/220#issuecomment-6702276561)): remover `Identity:TwoFactor:EnrollmentEnabled`, a variável do compose e o estado "cadastro indisponível".

## Why

Na M1 todo usuário tem o poder de um Admin (gere usuários), e a decisão do dono (28/10/2026, #180 D4) é que a verificação em 2 etapas vale para todos até os papéis chegarem (#184). Hoje ela é opcional: quem não liga entra só com a senha, e o cadastro em produção ainda depende de uma variável.

**Premissa falsa no corpo da issue** (convenção 9): ele diz "Sem mudança de tela: a #183 já trata o passo de cadastro". Não trata. O cadastro do painel está em Minha conta (#246), atrás do access token, e o `/entrar` só conhece `{ "segundoPasso": "totp" }`: qualquer outro valor no `200` vira `{ kind: 'http', status: 200 }` e o aviso genérico (`apps/frontend/src/features/auth/api/sessionApi.ts` l. 59–63). Ligar a obrigatoriedade só no `apps/api` deixaria todo mundo do lado de fora. Esta change leva a tela, numa change só (decisão do dono, Q7, como na #217).

## What Changes

**`apps/api`**
- **BREAKING (contrato do login):** a senha certa de quem não tem a verificação responde `200 { "segundoPasso": "cadastro" }` com o cookie pendente, e não mais `204`. O `204` deixa de existir no `POST /auth/session`.
- **Cadastro pelo cookie pendente:** `POST /auth/session/enrollment/setup` e `POST /auth/session/enrollment/confirm`, anônimas como o segundo passo, presas ao usuário do cookie, sem pedir a senha de novo. O prazo é de 10 min a partir do primeiro `setup`, que nenhum `setup` seguinte estende. O `confirm` liga a verificação, apaga o pendente, devolve os 10 códigos e grava o cookie completo.
- **O logout no meio do cadastro** ("Voltar") descarta o pendente e troca o stamp, para o cookie deixar de valer no servidor.
- **O authorize e a renovação exigem a verificação:** sem ela, o authorize trata como sem sessão, e o `refresh_token` responde `invalid_grant`.
- **Comando de emergência** `two-factor-reset --email <e-mail>` no próprio binário do `apps/api`, rodado no servidor (`docker compose run --rm --no-deps api …`): apaga a verificação de um e-mail, troca o stamp e encerra as sessões, pela mesma implementação da redefinição pelo admin, com log `Warning` e `EventId`, sem segredo na saída, e código de saída diferente de zero para e-mail inexistente, desativado ou sem a verificação. Para quem perdeu o celular e os códigos, inclusive o último Admin.
- **Removidos:** `Identity:TwoFactor:EnrollmentEnabled` (`TwoFactorOptions`), o `cadastroDisponivel` do `GET /account/two-factor`, o `403` "O cadastro … ainda não está disponível.", e `POST /account/two-factor/remove`, `setup` e `confirm` por access token (Q5).
- **Texto novo** do `403` de redefinir a si mesmo: "Você não pode redefinir a própria verificação. Peça a outro usuário, ou use o comando de emergência no servidor." (Q8).

**`apps/frontend`**
- O `/entrar` ganha a etapa "cadastro" do quadro `Main` (versão 55 do protótipo): QR e segredo, o código, os 10 códigos de recuperação, e só então a sessão. "Voltar" passa pelo logout. O convite e a redefinição de senha seguem para ela.
- Minha conta fica só com gerar novos códigos: saem "Remover verificação", o cadastro e o estado `indisponivel`, e entram `nao-configurada` e o texto de quem trocou de celular.
- Editar usuário troca os textos que dizem "em Minha conta" pelos do protótipo ("no próximo login").

**`tests/`**: os dois acordos que passam pelo login (`IssuerUserFlow`) cadastram a verificação do Admin do bootstrap.

**Configuração e documentação:** sai `Identity__TwoFactor__EnrollmentEnabled` do `docker-compose.prod.yml`, do `.env.prod.example`, dos `appsettings*.json`, do `docs/deployment.md` (a seção da #246), do `docs/configuration.md` e do `docs/development.md`; entram o comando de emergência e as notas de deploy.

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `issuer-user-login`: o `POST /auth/session` sem a verificação passa a responder `200 { segundoPasso: "cadastro" }`; as rotas de cadastro pelo cookie; o authorize e a renovação exigem a verificação; o logout no meio do cadastro.
- `two-factor-totp`: saem "Cadastro fechado até a tela existir" e o cadastro por access token; a remoção pela própria pessoa sai; o `cadastroDisponivel` sai do `GET`; o texto do `403`; entra o comando de emergência.
- `operator-login-ui`: a etapa "cadastro" do `/entrar`.
- `panel-access-link-pages`: o `200 { segundoPasso: "cadastro" }` depois do aceite e da conclusão.
- `panel-account-page`: o cartão da verificação sem `indisponivel`, sem remoção e sem cadastro; o subtítulo da página.
- `panel-issuer-session`: o fluxo do emissor começa depois da sessão completa, e não do `204` do `POST /auth/session`.
- `user-access-links`: os cenários que esperavam o `204` do login depois do aceite, da conclusão e da geração do link.
- `panel-user-detail-page`: os textos do cartão da verificação e da confirmação de redefinir.

## Impact

- **Contrato:** o `204` do `POST /auth/session` some (breaking para qualquer cliente além do painel; não há outro, conferido nos três apps e em `tests/`). `cadastroDisponivel` sai do `GET /account/two-factor`.
- **Sessões em produção:** quem não tem a verificação perde a sessão na renovação seguinte, até 15 min depois do deploy (design, Q3).
- **Sem migration:** as colunas e a tabela são da #218.
- **Dependências:** nenhum pacote novo; o QR do painel continua com o `qrcode.react` da #246.
- **Suítes:** `apps/api`, `apps/frontend`, `tests/InboxOrchestratorRoundTrip.Tests` e `tests/ApiConnectorsRoundTrip.Tests` (estes pelo `IssuerUserFlow`).
