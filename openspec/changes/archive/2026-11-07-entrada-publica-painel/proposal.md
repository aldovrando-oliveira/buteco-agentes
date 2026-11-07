**Issue:** #245 (M1 · 14 de 17; passo A da divisão da #183; depende da #217, da #218 e da #181, todas em `main`; bloqueia a #246, que bloqueia a #183). Fecha a #170.

## Why

O backend da M1 já serve o passo das 2 etapas (#218), o convite e a redefinição de senha por link (#181), mas o painel não tem as telas públicas que os usam:

- o `/entrar` só aceita `204` do `POST /auth/session` (`sessionApi.ts` l. 33). O `200 { "segundoPasso": "totp" }` cai no `default` e mostra "Não foi possível entrar. Tente de novo.", então quem tem a verificação ligada não entra pelo painel;
- os links que o admin gera, `<painel>/convite#<token>` e `<painel>/redefinir-senha#<token>`, não casam com nenhuma rota do SPA (não há rota `*` nem `errorElement` em `routes.tsx`), e o React Router mostra a página de erro padrão dele;
- "Esqueceu a senha?" e o convite ao administrador ficaram fora do `/entrar` na #217, até existirem a redefinição e o convite (`operator-login-ui`, "SHALL NOT mostrar…");
- o `PasswordInput` do Mantine não põe `aria-invalid` no `<input>` com erro (#170), e esta change cria os primeiros campos de senha com validação própria desde a #168.

É o primeiro dos três passos da #183 (decisão do dono, 05/11/2026) porque o cadastro das 2 etapas, que vem no passo B (#246), sem o passo do código no `/entrar` tranca a pessoa do lado de fora (#218 D1).

## What Changes

No `apps/frontend`, e um teste renomeado no `apps/api`. Nenhuma rota do `apps/api` muda.

- **`/entrar`, o passo das 2 etapas** (quadro `Main`, estados `mfa*`): o `200 { segundoPasso: "totp" }` troca o formulário pelo campo "Código", que aceita o código do app (6 dígitos) ou um código de recuperação (16 caracteres) e o manda ao `POST /auth/session/second-factor`. Respostas:
  - `204` segue o fluxo do emissor como o `204` da senha;
  - `401` "Código inválido." aparece no campo;
  - `401` "A entrada expirou…" volta a e-mail e senha, com o aviso. Ele é reconhecido pela igualdade com uma constante do contrato (decisão do dono, Q1);
  - `423` mostra o bloqueio no próprio passo;
  - `500` mostra o texto da chave trocada;
  - `429` e `503` se comportam como no login.

  Campo vazio não envia. "Voltar" retorna ao formulário.
- **`/entrar`, "Esqueceu a senha?"** abre na linha o texto "Peça a um administrador do painel um link para criar uma nova senha. O link vale por 24 horas.". O rodapé fora do cartão diz "Ainda não tem acesso? Peça um convite ao administrador do painel.". **Muda um requisito da `operator-login-ui`**, que os proibia até a #181.
- **`/convite`** (quadro `AceitarConviteSemProvedor`):
  - lê o token do fragmento e troca o endereço por `/convite` antes de qualquer pedido;
  - guarda o token no `sessionStorage` da aba e o apaga no sucesso, no link inválido e ao sair da página (Q4);
  - consulta o link (`POST /auth/invitation`);
  - mostra quem convidou, o e-mail travado, "Senha" e "Repita a senha";
  - aceita (`POST /auth/invitation/accept`) e entra com o e-mail e a senha (`POST /auth/session`). Quando a conta tem as 2 etapas, leva ao `/entrar` já no passo do código (estado `mfa-apos-nova-senha`, Q2).

  Estados: `carregando`, `formulario`, `senhas-diferentes`, `senha-fora-da-regra`, `muitas-tentativas`, `indisponivel` e `link-invalido`, com o texto de link inválido próprio da página (Q5).
- **`/redefinir-senha`** (quadro `RedefinirSenha`): o mesmo molde, com `POST /auth/password-reset` e `…/complete`, e o aviso de que a redefinição encerra as sessões e mantém as 2 etapas.
- **`aria-invalid` central nos campos de senha** (#170):
  - um `PasswordField` em `components/forms`, que repassa o erro ao `aria-invalid` do `<input>`;
  - os campos novos nascem com ele, e WAHA, Telegram, o `EntrarForm` e o `McpServerForm` (que tinha o ajuste local da #168) passam a usá-lo;
  - uma guarda estática na suíte e uma regra `no-restricted-imports` no ESLint impedem o `PasswordInput` direto fora dele (Q6).

  O painel usa o Mantine 9.4.2, e o defeito continua na 9.7.5, a mais nova em 05/11/2026.
- **`apps/api`, só teste:** o teste que fixa o texto exato do `401` do pendente vencido (`TwoFactorLoginTests.ExpiredPendingSession_TheEntryExpired`) passa a se chamar `SecondFactor_ExpiredPending_KeepsTheMessageThePanelRecognizes`. Ele ganha um comentário que aponta a constante do painel que depende do texto (Q1). A asserção não muda.
- **Documentação:** o `02-HISTORICO_E_STATUS.md` e o `CHANGELOG.md` antes do archive. No fechamento, os comentários de entrega na #246 e na #183.

**Fora deste passo, por decisão do dono (Q3):** o aviso "Senha alterada. Entre com a nova senha." (leitura no `/entrar` e gravação no logout) vai para a #246, que tem o único chamador. Registrado na [#246](https://github.com/aldovrando-oliveira/buteco-agentes/issues/246#issuecomment-6649626655).

## Capabilities

### New Capabilities

- `panel-access-link-pages`: as telas públicas do link de uso único, `/convite` e `/redefinir-senha`. Cobre:
  - a leitura do token do fragmento, a troca do endereço e a guarda do token na aba;
  - a consulta e os estados de carregando, indisponível e link inválido;
  - a política de 12 a 128 caracteres, as senhas diferentes e o limite de tentativas;
  - o aceite ou a conclusão seguidos do login, e o passo das 2 etapas depois deles.
- `frontend-password-field`: o campo de senha do painel marca `aria-invalid` quando tem erro, e o `PasswordInput` do Mantine não é usado diretamente.

### Modified Capabilities

- `operator-login-ui`: o passo das 2 etapas depois da senha, com os estados do código; "Esqueceu a senha?" e o convite ao administrador passam a aparecer; o `/entrar` aberto direto no passo do código, vindo das páginas do link.

## Impact

- **`apps/frontend`:**
  - `features/auth`: o `/entrar`, o `sessionApi`, o passo do código, as páginas do link e o `accessLinkApi`;
  - `components/forms/PasswordField`;
  - `app/routes.tsx` (`/convite` e `/redefinir-senha`);
  - os campos de WAHA e Telegram, o `McpServerForm` e o `eslint.config.js`.
- **`apps/api`:** só o nome e o comentário de um teste em `tests/Buteco.Api.Tests/Users/TwoFactor/TwoFactorLoginTests.cs`, sem mudança de comportamento nem de cobertura. As rotas usadas são as do contrato da #218 e da #181. O `nginx.conf` já leva `/convite` e `/redefinir-senha` ao `location /` (#181 D3) e `/auth/*` ao `apps/api`.
- **Dependências:** nenhuma nova. O `qrcode.react` é do passo B.
- **Issues:**
  - fecha a #170;
  - a #247 (sem milestone, bloqueada por esta) troca a igualdade de texto por um campo de tipo nos `401` do segundo passo;
  - comentários de entrega na #246 e na #183.
