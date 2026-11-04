**Issue:** #236 (passo C da divisão da #182)

## Why

Desde a #235, nenhum serviço manda HMAC, mas quase toda a suíte de integração ainda se autentica com ele. As fixtures padrão dos três apps anexam o HMAC `operator`, e os testes de subject assinam HMAC com o subject que querem provar. Medido em `7dc7753` (design, "Medição"): com a validação do HMAC recusando tudo nos três apps, **729 casos reprovam**. Desses, 707 provam uma regra por meio do HMAC e migram, 20 provam só o HMAC, e 2 provam um estado que só o HMAC autentica e que a #182 elimina.

Se a #182 (passo D) remover o HMAC com as fixtures como estão, a remoção vira um diff de ~1.000 métodos. A revisão da remoção se afoga na migração mecânica, e não dá para separar o caso que perdeu só o token do caso que perdeu a regra. Este passo troca o token desses testes antes. Depois dele, o que ainda depende do HMAC é só o que prova o próprio HMAC, numa lista por nome que vira a lista de remoções da #182.

O desenho completo está no design da #182, change `remover-hmac-token-servico`, preservado [num comentário da #182](https://github.com/aldovrando-oliveira/buteco-agentes/issues/182#issuecomment-6593073248) (base `14d4ba9`). Aqui ele é o "design de referência". Esta change implementa a seção D7 (testes e fixtures) e o passo C de "Tamanho e divisão".

## What Changes

- **`apps/api`: `TestIssuerTokens`**, um helper de teste em `Support/` sobre a `IOpenIddictServerFactory`, separado do `ServiceAccessTokenIssuer` de produção, que continua estreito (decisão do dono, 02/11/2026). A `ApiFactoryFixture` e as derivadas passam a anexar por padrão um token `buteco-painel` com `sub` fixo que não é usuário, `aud` `buteco-api` e 2 h de validade. O comportamento é o do HMAC `operator` de hoje: passa nas rotas do catálogo e recebe `403` nas rotas de `CurrentUser`.
- **`apps/inbox` e `apps/connectors`:** as fixtures padrão sobem com o `TestIssuer` da #208 e anexam um token `buteco-painel`. Antes do primeiro cliente, elas esperam o `IssuerKeysState.Obtained`. No `apps/connectors`, o emissor ligado exige `Api:BaseUrl`, e a fixture tira o agendador do ciclo do DI quando o teste não pediu o endereço: é ajuste de teste, e não opção de produção.
- **Testes de subject** (tabela de subjects, `401` sem token, `403` de subject desconhecido, rota anônima): trocam o HMAC pelo token do cliente que produz aquele subject. Os casos cujo subject não sai de nenhum cliente migram com um cliente desconhecido da tabela e o mesmo nome (design, Q1, decisão do dono).
- **`tests/`:** o `RoundTripFixture.OperatorClient` do `ApiConnectorsRoundTrip.Tests` passa ao token do emissor. O `OperatorHmacToken_…` do `InboxOrchestratorRoundTrip.Tests` fica para a #182.
- **Ficam intocados** os casos que provam só o HMAC (aceitação pela chave compartilhada, seletor por forma, `operator` HMAC → `403` nas rotas de `CurrentUser`, HMAC com o emissor fora do ar e afins), e os dois casos do `apps/connectors` que provam o app sem `Api:BaseUrl`, estado que só o HMAC autentica e que a #182 elimina. A lista por nome (G2, varredura estática e esses dois) vai como comentário à #182 no fechamento.
- **Nenhum caso removido e nenhuma asserção afrouxada.** O caso que só troca de token mantém o nome. Se precisar mudar de nome, ele entra na lista de renomeados do design.
- **Sem mudança de código de produção.**

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

Nenhum requisito de produção muda. O `openspec validate --strict` exige delta (decisão do dono, Q5), e o único requisito que esta change estabelece é o da própria suíte, o critério de aceite da issue. Ele entra como requisito novo nas specs que já guardam os "testes automatizados" de cada app:

- `api-scaffold`: a suíte de integração do `apps/api` autentica pelo token do emissor; com o HMAC recusado, reprovam só os casos da lista da #182.
- `inbox-scaffold`: o mesmo para o `apps/inbox`, com o emissor de teste em memória.
- `connectors-scaffold`: o mesmo para o `apps/connectors`, mais a fábrica sem o agendamento do ciclo quando o caso não pede `Api:BaseUrl`.

Os cenários que citam o HMAC (`operator-authentication`, `route-authentication`, `connectors-scaffold` e os "HMAC de operador recebe `403`" das specs de usuário) saem na #182, com o HMAC.

## Impact

- **`apps/api/tests/Buteco.Api.Tests`:** `Support/TestIssuerTokens.cs` (novo), `Support/TestAuthentication.cs`, `Support/ApiFactoryFixture.cs` e as fixtures derivadas, e os arquivos que montam HMAC com subject de serviço.
- **`apps/inbox/tests/Buteco.Inbox.Tests`:** `Support/TestAuthentication.cs` e as três fixtures padrão (`InboxFactoryFixture`, `OrchestrationFactoryFixture`, `ReconciliationFactoryFixture`), mais os testes de subject.
- **`apps/connectors/tests/Buteco.Connectors.Tests`:** `Support/TestAuthentication.cs`, `Support/ConnectorsFactory.cs` e os testes que chamam `CreateClientAs`/`IssueToken`.
- **`tests/`:** `ApiConnectorsRoundTrip.Tests` (`RoundTripFixture.OperatorClient`). O `InboxOrchestratorRoundTrip.Tests` e o `CrossAppTaskStoreCompatibility.Tests` não mudam.
- **Sem mudança:** código de produção dos cinco apps, `apps/workers`, `apps/frontend`, deploy e configuração. Nenhum pacote novo.
- **Docs:** `02` e `CHANGELOG.md`. O `01` não muda, porque o contrato não muda.
- **Issues:** a lista "só prova o HMAC" vai como comentário à #182, e é a lista de remoções dela.
