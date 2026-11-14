**Issue:** #274 (M3 · 2 de 3; depende da #189, mergeada em `1475d35`, PR #275). Fecha também a **#276** (o retorno com a chave do provedor trocada no meio respondia `500`, achado no apply, D9) e a **#277** (o log do OpenIddict com o `sub`, o e-mail e o `code_verifier` em `Information`, achado na conferência local, D11).

## Why

Quem recebe um convite só pode aceitá-lo criando uma senha (#181). Com o login pelo Google (#189), quem já
usa a conta Google deveria aceitar o convite por ela, sem mais uma senha. A #189 deixou pronto tudo o que
o aceite precisa (o cliente OIDC, o cookie do fluxo, o vínculo (`issuer`, `sub`), a sessão pendente, o
`buteco-recusa`, o `CallerPassword` com o código para quem não tem senha) e tirou daqui só o aceite (Q10
do design `provedores-federados-google`, decisão do dono de 13/11/2026).

## What Changes

- **O início do aceite pelo Google**, a partir do link do convite: `POST /auth/invitation/federated/{chave}/start`
  com `{ token }` no corpo, com as barreiras do login e no limite global. Confere o convite (pendente, não
  vencido) antes de ir ao Google e responde `200 { url }` como o início do login. O state token leva a
  intenção `convite`, o id do convidado e o **hash** do link, nunca o link (D1).
- **O retorno com a intenção `convite`** (D2), na ordem: o domínio, o convite ainda pendente com o mesmo
  hash, o e-mail verificado, o e-mail igual ao do convite, o par sem vínculo; depois o **consumo pela mesma
  condição do `UPDATE` único do aceite por senha**, com o vínculo gravado na mesma transação. O convidado
  fica ativo **sem senha**, com o Google como forma de entrada.
- **O aceite já abre o login** (Q7 da #189): o retorno grava a sessão pendente e manda ao cadastro das 2
  etapas, como o login pelo Google.
- **As recusas sem sessão** (D3), de volta ao convite (`/convite?recusa=<motivo>&provedor=<chave>`): outro
  e-mail (com o e-mail confirmado no `buteco-recusa` da #189), e-mail não verificado, convite que deixou de
  valer, conta Google já vinculada a outro usuário, domínio fora do permitido e a `falha` do cliente OIDC.
  Linha de log sem e-mail nem `sub`.
- **A concorrência** (D4): o aceite por senha e o pelo Google, ou dois pelo Google, disputam a mesma linha;
  só um vence, sem vínculo órfão.
- **A chave do provedor trocada no meio deixa de dar `500`** (#276, D9): um handler do cliente OIDC rejeita o retorno cuja registração não existe mais, e a rota responde `falha` no login, no conectar e no aceite.
- **O log do OpenIddict em `Warning`** (#277, D11): a biblioteca gravava o userinfo do Google (`sub`, e-mail) e o `code_verifier` em `Information`, também em produção; o filtro vem no `appsettings.json`, e a captura dos testes passa a ver as categorias da biblioteca.
- **Nada de tela** (#190). Nada de migration.
- **O contrato do token na aba para a #190** (D5): o painel guarda o token em `sessionStorage`
  (`buteco.convite`) antes de ir ao Google e o apaga no aceite e em toda recusa; nunca na URL. É código
  da #190, registrado aqui e comentado lá.
- **Docs**: `docs/configuration.md` (o aceite pelo provedor; sem senha, a primeira vem do link do Admin),
  `docs/deployment.md` (na seção do `password-reset-link`: o comando só serve Admin, e um Operador ou
  Leitor só com Google ganha senha pelo link que um Admin gera no painel), o `01`, o `02` e o
  `CHANGELOG.md`.

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `federated-login`: o aceite de convite pelo provedor (o início, a decisão do retorno com a intenção
  `convite`, as recusas, os logs).
- `user-access-links`: o convite passa a ser aceito também pelo provedor, pela mesma condição de consumo
  do aceite por senha.
- `issuer-user-login`: a sessão pendente também nasce do aceite pelo provedor.
- `local-users`: o usuário ativo sem senha passa a nascer do aceite pelo provedor.
- `federated-login` e `issuer-user-login`: os logs sem dado pessoal valem para toda categoria, inclusive a da biblioteca.
- `route-authentication`: a rota anônima nova na lista conhecida.

## Impact

- **Código:** só o `apps/api` e os testes dele. O `apps/inbox`, o `apps/connectors`, o `apps/workers` e o
  painel não mudam.
- **Banco:** nada (a `user_federated_identities` e as colunas do link já existem).
- **Deploy:** nenhuma configuração nova; o provedor é o da #189.
- **Suítes:** `apps/api` e `tests/ApiConnectorsRoundTrip.Tests` (`~/.cache/buteco-agents/bin/suite-274.sh`).
