**Issue:** #116

## Why

O `apps/inbox` autoriza qualquer token validamente assinado com a
`Auth:TokenSigningKey` como se fosse o operador: a `FallbackPolicy` é só
`RequireAuthenticatedUser()` (`apps/inbox/src/Buteco.Inbox/Program.cs:112-113`)
e ninguém lê o subject que `OperatorTokenAuthenticationHandler.cs:37` põe em
`ClaimTypes.NameIdentifier`. Um `service:inbox` (que o próprio app assina para
chamar o `apps/api`) ou um `service:connectors` (que a #103 introduz) vazado num
log abre canais, contatos, sessões e mensagens durante o tempo de vida do token.
O `apps/api` tinha a forma parecida do defeito e a fechou na #102 (D6 de
`catalogo-base-sincronizada`); o `apps/inbox` é o lado que ficou aberto, e a #103
é exatamente o segundo serviço com a chave.

## What Changes

- **`apps/inbox`, autorização:** a política padrão passa a exigir, além de
  usuário autenticado, o subject `operator`. Todo token válido com outro subject
  (`service:inbox`, `service:connectors` ou qualquer valor) recebe `403` nas doze
  rotas autenticadas do app. Token ausente ou inválido continua recebendo `401`.
- **`apps/inbox`, tabela de subjects:** começa com uma linha só, `operator` em
  todas as rotas autenticadas. Nenhum serviço chama rota autenticada do
  `apps/inbox` hoje (varredura no `design.md`), então não há lista de rotas por
  subject de serviço, nem checagem de boot dessa lista. O gatilho para criá-las
  está registrado.
- **`apps/inbox`, rotas anônimas:** `/health`, `/webhooks/{channelId:guid}` e
  `/internal/push-notifications` continuam como estão, sem token e com a mesma
  autenticação própria de hoje, inclusive quando a requisição traz um token de
  serviço.
- **Documentação:** `docs/architecture.md`, `01-ARQUITETURA_E_CONVENCOES.md`
  (seção Autenticação, que hoje afirma que o `apps/inbox` ainda não restringe
  subject), `02-HISTORICO_E_STATUS.md` e `CHANGELOG.md`.

Nada muda na emissão nem na validação de token, no `apps/api`, no
`apps/workers`, no `apps/frontend` ou nos webhooks de canal.

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `route-authentication`: acrescenta o requisito de que o `apps/inbox` só
  autoriza o subject `operator` nas rotas autenticadas e recusa com `403`
  qualquer outro subject válido, e de que as rotas anônimas não mudam com um
  token de serviço presente. O requisito equivalente do `apps/api` (#102) não
  muda.

## Impact

- **Código:** `apps/inbox/src/Buteco.Inbox/Program.cs` (política padrão) e um
  arquivo novo em `apps/inbox/src/Buteco.Inbox/Auth/` com a constante do
  subject. Nenhum arquivo fora do `apps/inbox`, nada em `libs/`.
- **Testes:** `apps/inbox/tests/Buteco.Inbox.Tests` ganha uma classe de teste
  de subject (um contêiner Postgres a mais na suíte).
  `tests/InboxOrchestratorRoundTrip.Tests` precisa continuar verde sem mudança:
  é ele que prova que o `operator` emitido pelo login do `apps/api` é o mesmo
  texto que o `apps/inbox` passa a exigir.
- **Chamadores:** o frontend usa o token do operador e não muda. Nenhum outro
  chamador usa rota autenticada do `apps/inbox`.
- **Fora do escopo:** chave de assinatura por serviço (#117); o `apps/api`.
- **Paralelo:** a #103 edita `docs/architecture.md`, `02-HISTORICO_E_STATUS.md`
  e `CHANGELOG.md`; as edições desta change são curtas e localizadas.
