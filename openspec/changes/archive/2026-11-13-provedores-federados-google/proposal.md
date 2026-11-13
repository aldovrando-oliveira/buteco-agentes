## Why

Só existe login com senha (#180, #217), com a verificação em 2 etapas obrigatória para todos (#220). Quem
já usa conta Google precisa de mais uma senha, e o Admin não tem como oferecer a entrada pelo provedor que
a equipe já usa. A #189 abre a M3 (#196): **provedores federados por configuração**, com o tipo Google, o
login e o vínculo por eles, sem entrada automática e sem criar usuário.

A M3 foi dividida em duas changes de backend (Q10, decisão do dono de 13/11/2026): esta (a 189a) e a
**#274**, o aceite de convite pelo Google, que depende desta. A #190 (as telas) vem depois das duas.

## What Changes

- **O Google fica atrás do emissor do Buteco** (D1). O `apps/api` passa a ser também **cliente OIDC** do
  Google, pelo `OpenIddict.Client` 7.7.2 com o `OpenIddict.Client.WebIntegration`. O painel continua
  falando só com o emissor do Buteco, e os tokens que os três apps validam não mudam.
- **`Identity:FederatedProviders`**, um dicionário por chave neutra escolhida por quem instala, com o tipo
  `google`, o `ClientId`, o `HostedDomain` opcional e o segredo só por variável de ambiente (D2). Issuer,
  nome e botão são fixos pelo tipo. **Checagem de boot** com uma lista fechada de dez inconsistências
  (D3).
- **O cookie do fluxo externo** `buteco-fluxo-externo.<nonce>`, `SameSite=Lax`, preso ao caminho do
  retorno, com só o valor anti-falsificação; o resto vai no state token do cliente (D4).
- **O login pelo Google também pede o código do Buteco** (Q1, decisão do dono): o retorno grava o
  `buteco-sessao` **pendente** (continua `Strict`) e manda ao passo `totp` ou ao `cadastro` de hoje. O
  authorize, a troca do código, a renovação e a obrigatoriedade não mudam (D5).
- **O vínculo pelo par (`issuer`, `sub`)**, na tabela nova `user_federated_identities` (migration), nunca
  por e-mail (D6). **Conectar** em Minha conta, com a pessoa autenticada e reautenticada; **desconectar**
  pela própria pessoa e pelo Admin; a última forma de entrada não sai; a desconexão pelo Admin troca o
  stamp e revoga as sessões.
- **As recusas sem sessão**, com o código do motivo no retorno ao painel, o e-mail confirmado num cookie
  `buteco-recusa` de uso único lido por `POST /auth/federated/refusal`, e uma linha de log sem e-mail nem
  `sub` (D7).
- **O `CallerPassword` aceita o código do app autenticador de quem não tem senha** (Q1), no campo
  `codigo`; o código de recuperação não vale ali, e quem tem senha continua só com a senha (D8).
- **As rotas dos provedores**: `GET /identity/federated-providers` (Admin, só leitura, sem segredo) e
  `GET /auth/federated/providers` (anônima, os botões do login). O `formasDeEntrada` que `/users` já
  tem passa a trazer `"google"`, sem mudar de formato; `/users/{id}` e `/account` ganham
  `contasConectadas`, e `/account` também o `formasDeEntrada` (D9).
- **Nada de tela** (#190). O contrato que as telas leem fica nas specs.
- **Docs**: `docs/configuration.md`, `docs/deployment.md` (o cliente OAuth no Google, a URI, o arquivo
  opcional de ambiente com a versão mínima do Compose, a volta atrás), o `01`, o `02` e o `CHANGELOG.md`.

## Capabilities

### New Capabilities

- `federated-providers`: a seção `Identity:FederatedProviders`, o tipo Google, a checagem de boot e as
  rotas dos provedores.
- `federated-login`: o `apps/api` como cliente OIDC, o cookie do fluxo, o login federado, as recusas e o
  vínculo (`issuer`, `sub`), com conectar e desconectar.

### Modified Capabilities

- `issuer-user-login`: a sessão pendente também nasce do retorno do provedor.
- `user-access-links`: a confirmação de quem chama aceita o código do app de quem não tem senha.
- `user-management`: a promoção a Admin com a mesma confirmação; as formas de entrada na lista e no
  detalhe.
- `external-applications`: criar a aplicação e gerar segredo com a mesma confirmação.
- `user-account`: as formas de entrada em `/account`.
- `local-users`: o usuário ativo pode não ter senha.
- `route-roles`: as rotas novas na matriz.
- `route-authentication`: as rotas anônimas novas e o endpoint de retorno do cliente na conferência de
  boot.
- `issuer-token-storage`: o state token do cliente gera entrada de uso único.

## Impact

- **Código:** só o `apps/api` e os testes dele. O `apps/inbox`, o `apps/connectors`, o `apps/workers` e
  o painel não mudam.
- **Pacotes:** `OpenIddict.Client.AspNetCore`, `OpenIddict.Client.SystemNetHttp` e
  `OpenIddict.Client.WebIntegration`, 7.7.2, no `Directory.Packages.props`.
- **Banco:** a tabela `user_federated_identities` (migration aditiva).
- **Deploy:** um `env_file` opcional só do `api` (`federated-providers.env`, fora do repositório),
  Docker Compose 2.24.0 ou mais novo; sem o arquivo, nada muda. A URI de retorno a registrar no Google:
  `https://<PUBLIC_DOMAIN>/auth/federated/callback`.
- **Suítes:** `apps/api`, `tests/InboxOrchestratorRoundTrip.Tests`, `tests/ApiConnectorsRoundTrip.Tests`
  (`~/.cache/buteco-agents/bin/suite-189.sh`).
