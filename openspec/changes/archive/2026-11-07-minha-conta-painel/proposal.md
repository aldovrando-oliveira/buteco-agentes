**Issue:** #246

## Why

O `apps/api` já tem as rotas de Minha conta (#219) e do cadastro das 2 etapas (#218), mas o painel não tem onde a pessoa troque a própria senha, ligue a verificação em 2 etapas ou veja e encerre as próprias sessões. Por isso o cadastro das 2 etapas segue fechado em produção (#218, D1). É o segundo passo da divisão da #183 (decisão do dono, 05/11/2026), e depende da #245, que entregou o passo do código no `/entrar` e o `PasswordField`.

## What Changes

- **Nova página `/account` (Minha conta)** no `apps/frontend`, aberta pelo bloco do nome no rodapé da casca, sem item novo no menu. Ela segue o quadro `MinhaConta` (`entrega=m1`) e tem quatro cartões:
  - **Dados:** nome, e-mail, no painel desde e último acesso, só leitura (`GET /account`);
  - **Senha:** a troca com a senha atual (`POST /account/password`). Depois do `204`, o painel encerra a sessão pelo logout do emissor, e o `/entrar` mostra "Senha alterada. Entre com a nova senha.";
  - **Verificação em 2 etapas** (`/account/two-factor`):
    - ligar: a senha, o QR com o segredo para digitar, o código e os 10 códigos de recuperação;
    - gerar novos códigos;
    - remover;
    - o estado de cadastro indisponível.

    O QR é desenhado no navegador pelo `qrcode.react`. O segredo e os códigos ficam só na memória da tela;
  - **Sessões** (`/account/sessions`): a lista com "Esta sessão", encerrar uma, encerrar as outras e encerrar a atual, que leva ao `/entrar`.
- **O rodapé da casca:** o nome e o e-mail viram o link para Minha conta, marcado como página atual quando ela está aberta.
- **O `/entrar` ganha o aviso "Senha alterada."** (estado `senha-alterada` do quadro `Main`), com a gravação antes do logout e a leitura no `/entrar`. O aviso veio da #245 por decisão do dono (Q3 dela).
- **Dependência nova no `apps/frontend`:** `qrcode.react` 4.2.0 (ISC, sem dependências).
- **Deploy:** a partir desta change o dono pode abrir o cadastro das 2 etapas em produção. As notas dizem como abrir, como conferir e como voltar atrás. A #220 remove a opção depois.

## Capabilities

### New Capabilities

- `panel-account-page`: a página Minha conta do painel. Cobre os quatro cartões, os estados e os erros de cada um, a troca de senha seguida do logout, o cadastro das 2 etapas com o QR e o segredo e os códigos só na memória, e as sessões.

### Modified Capabilities

- `frontend-app-shell`: o bloco do nome e do e-mail no rodapé vira o link para Minha conta, com o estado de página atual.
- `operator-login-ui`: o `/entrar` mostra "Senha alterada. Entre com a nova senha." quando chega do logout que segue a troca de senha.
- `panel-issuer-session`: o `401` com `application/problem+json` é a recusa da própria rota (senha ou código errado), e o `sessionFetch` o devolve à tela, sem renovar, repetir nem encerrar a sessão. Hoje uma senha errada em Minha conta deslogaria a pessoa (decisão do dono, Q0). E o logout desta aba marca a aba como saindo, para a rota protegida mostrar "Saindo…" em vez de passar pelo `/entrar` interno, que consumia o aviso de senha alterada (Q11).
- `issuer-token-validation`: o `401` de token ausente ou inválido, nos três apps (api, inbox, connectors), não traz corpo `application/problem+json`. É a premissa de que o painel depende, agora fixada por teste em cada app (complemento da Q0).

## Impact

- **`apps/frontend`:**
  - a pasta nova `features/account/`, com a API, a página e os cartões;
  - a rota `account` em `app/routes.tsx`;
  - o rodapé em `components/layout/AppShell.tsx`;
  - o aviso em `features/auth/` (gravação e leitura) e no `EntrarPage`;
  - `qrcode.react` no `package.json` e no lock.
- **`apps/api`, `apps/inbox`, `apps/connectors`:** nenhuma mudança de código de produção nem de contrato. Cada um ganha o teste `TokenRejection_401_HasNoProblemBody_ThePanelRenewsOnThis` (Q0).
- **Deploy:** o `docker-compose.prod.yml` passa a levar `Identity__TwoFactor__EnrollmentEnabled: ${IDENTITY_TWO_FACTOR_ENROLLMENT_ENABLED:-false}` ao `apps/api`, com a linha no `.env.prod.example` (Q8). Sem migration.
- **Docs:** a seção de deploy da change; o `configuration.md` e o `deployment.md`, onde dizem que o cadastro fica fechado "até a #246"; o `CHANGELOG.md` e o `02`.
