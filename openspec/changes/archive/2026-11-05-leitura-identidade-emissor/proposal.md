**Issue:** #211 (M1 · 13 de 15; depende da #179, fechada; consumida pela #183)

## Why

A tela **Identidade** da #183 mostra o card do emissor e o card das chaves de assinatura. Não há fonte para nenhum dos dois. O discovery e o JWKS da #179 são públicos e feitos para máquina: o JWKS não diz qual chave assina, qual só valida nem quando a próxima entra, e nenhum dos dois traz data de criação nem duração de token. Sem uma rota própria, a tela teria que inventar esse estado ou montá-lo de dado parcial (convenção 13). A rota ficou fora da #179 por decisão do dono e fora da #183 porque tela não leva backend.

## What Changes

- **`GET /identity` no `apps/api`**, só de leitura. O contrato segue o quadro "Identidade" do protótipo, corrigido pelo dono em 04/11/2026 (design, D2):
  - **emissor:** issuer e os endereços absolutos de discovery, JWKS e token, montados como o discovery os anuncia. Também a validade do access token e a duração da sessão do painel (refresh deslizante, sem atividade);
  - **chaves de assinatura:** intervalo de troca, pré-publicação, retenção e intervalo da verificação. Por chave do anel: `kid`, estado (`em-uso`, `proxima`, `aposentando`), criada em, assina desde, assina até e sai da publicação em. O estado sai da `IdentityKeyRotationPolicy`, a mesma conta da rotação;
  - **nunca material de chave**, nem público nem privado. A consulta nem lê a coluna do material.
- **Acesso:** na M1, todo usuário real e ativo (`CurrentUser`). O token do painel sem usuário recebe `403` pela `CurrentUser`, e os tokens de serviço recebem `403` pela política de fallback. Na M2 a rota passa a ser só de Admin (#184, comentada nesta change).
- **Durações em constantes únicas** (decisão do dono, Q1):
  - access token, refresh token, id token e código de autorização ficam em `IdentityTokenLifetimes`, usados pelo registro do OpenIddict, pelo `ServiceAccessTokenIssuer` e pela rota;
  - o intervalo de troca, a pré-publicação, a retenção e a verificação continuam na `IdentityKeyRotationPolicy`, que já é a fonte única delas, e a rota as lê de lá.

  O id token e o código passam a ser registrados explicitamente, com os valores padrão de hoje (20 min e 5 min, `OpenIddictServerOptions.cs` l. 271 e 245, OpenIddict 7.7.2). Nenhuma duração muda.
- **A premissa "emissor desligado" da issue caiu.** Desde a #217, a url pública é obrigatória e o boot reprova sem ela (`IdentityIssuerStartupValidation.cs` l. 38). A rota não tem esse estado (design, K1 e D5). A rota é nova, então isso não muda comportamento nem contrato existente e não reduz cobertura.
- **Documentação:** a rota entra no `docs/architecture.md`. O `02-HISTORICO_E_STATUS.md` e o `CHANGELOG.md` são escritos antes do archive. No fechamento sai o comentário de entrega na #183: o quadro foi corrigido em 04/11/2026, o selo "Operando" é fixo e as divergências que restam (design, K2).

## Capabilities

### New Capabilities

- `identity-issuer-overview`: a rota de leitura do emissor e das chaves de assinatura para o painel. Cobre os campos, as durações tiradas das mesmas constantes que o emissor usa, o cálculo do estado pela política de rotação, a ausência de material de chave e o acesso.

### Modified Capabilities

Nenhuma. As durações não mudam, e o requisito "Refresh rotativo e deslizante de 14 dias" da `issuer-user-login` continua valendo como está. O `Purpose` da `identity-signing-keys` cita a #211 como dona da rota de leitura. No archive ele passa a apontar para a `identity-issuer-overview`, mas isso é texto, não requisito.

## Impact

- **App afetado:** só o `apps/api`. Nenhum outro app, nenhuma lib, nenhum pacote novo, nenhuma migration.
- **Código:**
  - `Identity/Overview/`, novo: endpoint e resposta;
  - `Identity/IdentityTokenLifetimes.cs`, novo;
  - `IdentityIssuerServiceCollectionExtensions` e `ServiceAccessTokenIssuer` passam a ler as constantes;
  - `IdentityKeyRotationPolicy` ganha a classificação. O `IdentityKeyRing` ganha a leitura sem material;
  - o `Program.cs` mapeia a rota.
- **Testes:** só na suíte `api` (`Buteco.Api.Tests`).
- **Issues:** comentário na #184 (a rota passa a ser só de Admin na M2). Comentário de entrega na #183 no fechamento.
