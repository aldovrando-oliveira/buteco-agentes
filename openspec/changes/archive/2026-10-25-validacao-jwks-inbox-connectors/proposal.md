**Issue:** #208

## Why

Desde a #179 o `apps/api` é emissor OIDC, mas o `apps/inbox` e o `apps/connectors` só sabem validar o token HMAC da `Auth:TokenSigningKey`. Quando a #180 passar a emitir o token do operador pelo emissor, o painel, que chama os dois apps direto (canais no `apps/inbox`, pastas e "Sincronizar agora" no `apps/connectors`), quebraria contra eles até a #182 remover o HMAC. Esta change abre o período de convivência decidido em 23/10/2026: os dois apps aceitam o token do emissor **antes** de alguém passar a recebê-lo, e o `main` continua implantável (M1 · 2 de 8, acompanhamento #194).

## What Changes

- **`apps/inbox` e `apps/connectors` validam o access token do emissor** pelo JWKS, com `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.13: assinatura RS256 por `kid`, `iss` igual ao issuer configurado, `aud` própria de cada app (`buteco-inbox`, `buteco-connectors`) e `exp`.
- **As chaves vêm do endereço interno do `apps/api`** (`Api:BaseUrl`, `http://api:8080` no compose), nunca do `jwks_uri` público do discovery, e sem saída para a internet. O issuer público é valor fixo de configuração (`Identity:Issuer`) e é conferido contra o `issuer` do discovery lido pelo endereço interno.
- **Os dois apps sobem com o `apps/api` fora do ar.** As chaves são buscadas em segundo plano depois do start, são renovadas com no máximo cerca de 1h15 de idade enquanto o `apps/api` responde, são buscadas de novo diante de `kid` desconhecido (no máximo uma vez por minuto), e um token do emissor recebido antes de qualquer chave obtida recebe `503` com `Retry-After`. O HMAC nunca depende disso.
- **Convivência dos dois formatos por forma do token:** JWS compacto (três segmentos) vai para o esquema do emissor; qualquer outra coisa vai para o HMAC, que continua **exatamente** como hoje. Um token inválido de um formato nunca é tentado no outro.
- **O principal do emissor entra nas tabelas de subject existentes por uma tabela fechada de clientes**: `client_id` `buteco-painel` → `operator`. O `sub` do token do emissor nunca vira subject, então um token com `sub` `operator` não ganha nada. Cliente fora da tabela recebe `403`.
- **`apps/api`: nenhuma mudança de produção.** O access token já carrega `client_id` (RFC 9068), posto pelo próprio OpenIddict 7.7.2 em todo fluxo; um teste de contrato no `apps/api` passa a afirmá-lo, porque os dois apps dependem dele. (Correção na implementação: a primeira versão desta change acrescentava um handler para pôr o claim; ver design.md, D6 e F8.)
- **Checagens de boot (convenção 8)** nos dois apps: `Identity:Issuer` absoluto e sem query/fragmento, `Api:BaseUrl` obrigatório quando o issuer está ligado, e a tabela de clientes coerente com a tabela de subjects. Sem `Identity:Issuer`, o esquema do emissor fica desligado com aviso no boot, como o emissor do `apps/api` sem url pública.
- **Princípio do `01` reescrito:** "valida sem chamar o `apps/api`" passa a valer para o HMAC e para cada **requisição**; a obtenção das chaves é a única chamada, fora do caminho da requisição e sem efeito no HMAC.
- `docker-compose.prod.yml`: `Identity__Issuer: ${PUBLIC_DOMAIN}` no `inbox` e no `connectors`.
- **Nada é removido.** A validação HMAC e a `Auth:TokenSigningKey` saem na #182.

## Capabilities

### New Capabilities

- `issuer-token-validation`: como o `apps/inbox` e o `apps/connectors` aceitam o access token do emissor do `apps/api`: escolha do esquema pela forma do token, validação de `iss`/`aud`/`exp`/assinatura, origem e ciclo de vida das chaves (busca em segundo plano, cache, `kid` desconhecido, `503` sem chaves), mapeamento do cliente para o subject e checagens de boot.

### Modified Capabilities

- `route-authentication`: o requisito "Token de operador emitido por apps/api é aceito por apps/inbox sem chamada de rede entre os processos" passa a dizer que vale para o token HMAC também com o esquema do emissor ligado e as chaves nunca obtidas.
- `oidc-issuer`: o requisito "Token emitido é JWT assinado, verificável pelo JWKS, com aud, kid e iat" passa a exigir o claim `client_id` e o `typ` `at+jwt`, que o emissor já emite e de que os dois apps passam a depender.

## Impact

- **`apps/inbox`**: `Auth/` ganha o esquema do emissor, a seleção por forma de token, a obtenção das chaves, a tabela de clientes e as checagens; `Program.cs`; `appsettings*.json`; pacote `Microsoft.AspNetCore.Authentication.JwtBearer`.
- **`apps/connectors`**: o mesmo, copiado (sem `libs/`, design D10); `Program.cs`; `appsettings*.json`; mesmo pacote.
- **`apps/api`**: só o teste de contrato do `client_id` e do `typ` `at+jwt` no `IdentityIssuerTokenTests`. Nenhum código de produção, rota, tabela ou configuração muda.
- **`tests/InboxOrchestratorRoundTrip.Tests` e `tests/ApiConnectorsRoundTrip.Tests`**: um teste de acordo em cada, com o token emitido pelo `apps/api` real (convenção 11).
- **Raiz**: `Directory.Packages.props` (+ `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.13), `docker-compose.prod.yml`.
- **Documentação**: `01` (Autenticação), `02`, `docs/configuration.md`, `docs/deployment.md`, `docs/architecture.md`, `CHANGELOG.md`.
- **`apps/workers` e `apps/frontend`**: nenhuma mudança.
- **Sem migration, sem rota nova, sem mudança de contrato HTTP** para quem usa HMAC.
