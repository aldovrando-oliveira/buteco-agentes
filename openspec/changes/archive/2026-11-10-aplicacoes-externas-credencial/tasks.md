Um guarda marcado **(guarda)** só conta depois de ter reprovado contra o defeito que ele existe para pegar (convenção 15). Para cada um:

1. o `~/.cache/buteco-agents/bin/guarda-186.sh` reintroduz o defeito nomeado. Ele é cópia do `guarda-184.sh`, restrito ao `apps/api` e aos dois projetos de `tests/` da change, com o mesmo lock das suítes. A mutação toca só o trecho nomeado, e nunca restaura um diretório ao `HEAD`;
2. o teste roda e reprova;
3. a árvore volta pela cópia feita antes da mutação, com o sha conferido.

A tarefa registra o nome do teste, a mensagem da reprovação e o "voltou: <arquivo>", com o sha igual ao de antes. Uma mutação que quebra a compilação e reprova por "no tests" não vale: é descartada e refeita. As saídas ficam em `~/.cache/buteco-agents/186-guardas/<tarefa>`.

**As suítes** rodam pelo `~/.cache/buteco-agents/bin/suite-186.sh` (`api`, `rt-inbox`, `rt-connectors`), uma por vez, com o lock, e o RabbitMQ de pé (`PAUSAR_RABBIT=0`).
- Vale a regra assimétrica: uma rodada verde conta mesmo com a CPU caindo, e uma reprovação só conta com a CPU em 100 do início ao fim.
- Antes de cada rodada, o load de 1 e de 5 min fica abaixo de 8.
- O `rt-inbox` tem a intermitente conhecida da #176: se ela aparecer, o comentário vai na #176. Reprovação válida dela nos dois worktrees não bloqueia; só no da change, é parada.

**A baseline** está em `~/.cache/buteco-agents/baseline-186/`, em `71046f6`, com a árvore limpa: `apps/api` 1.565 + 1 ignorado (CPU mínima 58, verde conta), `rt-inbox` 15/15, `rt-connectors` 11/11, todas `APROVADA`, e o relatório por nome em `nomes.txt` (1.592 linhas). Detalhes no design ("Baseline").

**Parada** (convenção 9):
- **parar** quando uma premissa falsa mudar comportamento ou contrato, ou quando a correção reduzir cobertura;
- **seguir e registrar** quando a correção só acrescentar teste ou cenário para pôr um guarda no lugar certo, ou só tocar comentário ou documentação.

**Nada de commit parcial.** O trabalho fica na árvore até o dono pedir o commit.

**Casos que saem ou mudam de asserção**, porque o requisito sai ou muda nesta change (delta specs). Todo outro caso existente fica sem remoção, renome nem afrouxamento:
- `apps/api`, **muda de asserção e de nome:** `AgentCardSecuritySchemeTests.GetAgentCard_IncludesBearerHttpAuthSecurityScheme` passa a esperar só o esquema `oauth2` com `clientCredentials`, o `tokenUrl` público e o escopo `buteco-api` (ex.: `GetAgentCard_DeclaresTheButecoOAuth2ClientCredentialsScheme`); `AgentCardSecuritySchemeTests.GetAgentCard_IncludesSecurityRequirementReferencingBearerScheme` passa a esperar a referência a `oauth2` com `buteco-api` (ex.: `…ReferencingTheOAuth2Scheme`). Requisito: `a2a-agent-card`, "AgentCard declara o esquema de segurança do endpoint A2A";
- `apps/api`, **muda a matriz esperada:** `RoleMatrixTests.Expected` ganha as 8 linhas de `/applications/*` só com `admin`, e com elas os resultados de `TheProductionTable_IsTheExpectedMatrix` (mesma asserção) e `EachRoute_ForEachRole_FollowsTheTable` (24 resultados novos). Requisito: `route-roles`, "Matriz de papéis por rota nos três apps";
- `apps/api`, **muda o arranjo, asserção igual:** os casos de `RouteDeclarationValidationTests` que montam uma `ServiceRoutes` própria passam a incluir a chave `application`, que a checagem nova exige (`route-roles`, "Toda rota autenticada declara papel ou subject de serviço"); a mensagem esperada em `IdentityIssuerStartupValidationTests.RealComposition_WithRefreshTokensOutlivingTheKeyRetention_FailsNamingTheRefreshToken` continua citando o refresh token (`identity-signing-keys`, modificado: o `max` ganha um termo, e o refresh continua o maior).

Cada um é listado por nome na comparação da 10.3, com o requisito que o mudou. Um caso fora desta lista que precise sair ou afrouxar é parada.

## 0. Preparação

- [x] 0.1 (scripts locais) `~/.cache/buteco-agents/bin/guarda-186.sh`, cópia do `guarda-184.sh` restrita ao `apps/api`, ao `tests/InboxOrchestratorRoundTrip.Tests` e ao `tests/ApiConnectorsRoundTrip.Tests`, com o lock das suítes. Conferir com uma mutação nula (recusa com "defeito não aplicado") e uma real em `apps/api/src` (o arquivo volta com o mesmo sha).
  **Feito:** `guarda-186.sh` aceita vários pares arquivo/defeito (o 5.6, rodada 2, e o 3.8 tocam dois arquivos). Mutação nula: "defeito não aplicado"; real em `Identity/IdentityTokenLifetimes.cs`: `AdvisoryLockKeysTests` 6/6, voltou com o sha `6659e9a8…`. Saídas em `~/.cache/buteco-agents/186-guardas/0.1/`.
- [x] 0.2 Ler de novo, na tag 7.7.2 do OpenIddict (`~/.cache/buteco-agents/182-fonte/openiddict-core`), o `ValidateClientSecretAsync` público (`OpenIddictApplicationManager.cs` l. 1363), o `ReplaceApplicationManager` (`OpenIddictCoreBuilder.cs` l. 53) e a ordem da duração do access token (`OpenIddictServerHandlers.cs` l. 3680–3703), e registrar no design qualquer diferença (convenção 6).
  **Feito:** `ReplaceApplicationManager(typeof(...<>))` troca o gerente tipado, e o `IOpenIddictApplicationManager` do EF Core resolve por ele (`OpenIddictEntityFrameworkCoreBuilder.cs` l. 98–99); o servidor chama o `ValidateClientSecretAsync` virtual pela interface (l. 2045). Sem diferença do design. Achado: o OpenIddict não põe `nbf` (D10).

## 1. Banco e entidades (apps/api; `external-applications`)

- [x] 1.1 `ExternalApplications/ExternalApplication.cs` e `ExternalApplicationSecret.cs`, com a configuração EF e os `DbSet` no `AppDbContext`: nome com índice único sem diferenciar maiúscula, `client_id` único, hash `bytea` único, FK do segredo para a aplicação.
  **Feito:** `ExternalApplications/ExternalApplication.cs`, `ExternalApplicationSecret.cs`; `normalized_name` com índice único (maiúsculas), FK `Restrict`.
- [x] 1.2 Migration `ExternalApplications` (só acréscimo). Conferir o `down`.
  **Feito:** `20261109212048_ExternalApplications`; o `Down` apaga as duas tabelas, segredos antes.
- [x] 1.3 Estado do segredo derivado (`revogado`, `expirado`, `substituido`, `ativo`) numa função pura, com as constantes `SecretValidity` (365 d) e `SecretOverlap` (7 d), e os casos dos quatro estados.
  **Feito:** `StateAt`/`IsAliveAt`/`Supersede` puros; `ExternalApplicationSecretTests` (6 métodos, 7 resultados).

## 2. Client ID e segredo (apps/api; D3, D4)

- [x] 2.1 `ExternalApplicationClientId`: gerador (`btc_app_` + 8 do alfabeto de 32) atrás de uma interface trocável no teste, e `Ensure`, que recusa `buteco-`.
  **Feito.**
- [x] 2.2 **(guarda)** "Prefixo reservado nunca é gravado": o gerador falso devolve `buteco-x`, e não sobra linha em `external_applications` nem em `openiddict_applications`. **Defeito:** a chamada do `Ensure` removida.
  **Guarda feito:** `TheReservedPrefix_IsNeverWritten` reprovou com "criou com buteco-x859eac17" com as duas chamadas do `Ensure` removidas; voltou `ExternalApplicationEndpoints.cs` (`eedabe75…`). `~/.cache/buteco-agents/186-guardas/2.2.log`.
- [x] 2.3 `ExternalApplicationSecretValue`: gerar (`btc_sk_` + 43 base64url), SHA-256, sufixo de 4. Caso "Só o hash no banco", com a captura de log afirmando que o `valor` não aparece.
  **Feito** (em `ExternalApplicationSecretTests`; o `valor` fora do banco e das respostas está nos casos de criação e detalhe).

## 3. O gerente estendido e a emissão (apps/api; `oidc-issuer`, D4, D5)

- [x] 3.1 `ButecoApplicationManager : OpenIddictApplicationManager<…>`, registrado por `ReplaceApplicationManager`. `ValidateClientSecretAsync(application, secret)`: cliente no cadastro → só os segredos dele, vivos, com a aplicação ativa; nunca o `base`. Qualquer outro cliente → `base`, sem consultar o cadastro.
  **Feito:** `Identity/Clients/ButecoApplicationManager.cs`.
- [x] 3.2 **(guarda)** "Segredo de enchimento nunca vale": o teste captura o enchimento na criação (ponto de teste no serviço) e pede token com ele → `invalid_client`. **Defeito:** o gerente caindo no `base` quando nenhum segredo do cadastro confere.
  **Guarda feito:** `TheFillerSecret_IsNeverAccepted` reprovou (`Expected: "invalid_client" Actual: null`) com o gerente caindo no `base`; voltou `ButecoApplicationManager.cs` (`67e06478…`). O enchimento conhecido sai do próprio OpenIddict, sem ponto de teste em produção (D10).
- [x] 3.3 **(guarda)** "Cliente de serviço não passa pelo cadastro": um segredo de aplicação gravado com o mesmo texto do segredo do `buteco-servico-inbox`; o token do serviço sai, e o `ultimoUso` do segredo de aplicação continua `null`. **Defeito:** o gerente procurando o hash no cadastro para todo cliente.
  **Guarda feito:** `AServiceClient_DoesNotGoThroughTheRegistry` reprovou (`Assert.Null() … has a value 2026-11-09T21:38:39`) com o gerente procurando o hash para todo cliente; voltou `ButecoApplicationManager.cs` (`67e06478…`).
- [x] 3.4 **(guarda)** "Segredo revogado ou vencido": os dois recebem `invalid_client`. **Defeitos, em duas rodadas:** sem o filtro de revogado; sem o filtro de vencido.
  **Guarda feito, duas rodadas:** sem o filtro de revogado e sem o de vencido, `ARevokedSecret_AndAnExpiredSecret_AreInvalidClient` reprovou nas duas (`Expected: "invalid_client" Actual: null`); voltou `ButecoApplicationManager.cs` (`67e06478…`) nas duas. `~/.cache/buteco-agents/186-guardas/3.4-revogado.log`, `3.4-vencido.log`.
- [x] 3.5 Casos "Substituído vale dentro da sobreposição" (6 e 8 dias, com o relógio do teste) e "Segredo de outra aplicação".
  **Feito** (o fim da sobreposição trazido para trás de agora no banco, porque o relógio da classe não volta).
- [x] 3.6 Último uso no gerente, com `UPDATE … WHERE ultimo_uso_em IS NULL OR ultimo_uso_em < agora − 1 min`. Casos "Token emitido marca o uso" e "Rajada grava uma vez" (contando os `UPDATE` pelo interceptor de comandos).
  **Feito:** a rajada pré-grava o último uso há 30 s e afirma que cinco pedidos não o regravam; há 2 min, regrava (sem avançar o relógio).
- [x] 3.7 `IdentityTokenLifetimes.ApplicationAccessToken` (5 min, com o registro do porquê). No `ClientCredentialsTokenHandler`: cliente no cadastro → claim `buteco_app` (id da aplicação, destino access token) e `SetAccessTokenLifetime`; corrigir o comentário falso da l. 19 (premissa 4).
  **Feito**, com o comentário falso da l. 19 corrigido (premissa 4).
- [x] 3.8 **(guarda)** "Os clientes do Buteco nunca levam buteco_app": tokens do `buteco-painel`, do `buteco-servico-inbox` e do `buteco-servico-connectors` sem o claim. **Defeito:** o handler pondo `buteco_app` em todo `client_credentials` sem consultar o cadastro (para o painel, a mutação vai no ponto comum de montagem do principal).
  **Guarda feito:** com o claim posto em todo `client_credentials` e no principal do painel, `TheButecoClients_NeverCarryTheApplicationClaim` reprovou com "buteco-painel levou buteco_app"; voltaram `ClientCredentialsTokenHandler.cs` (`c2a0a7f1…`) e `Flow/UserPrincipalFactory.cs` (`ec6a4e9e…`).
- [x] 3.9 **(guarda)** "`exp − iat` = 300 e 900": o token da aplicação pelo host real com 300 s, o do painel com 900 s. **Defeitos, em duas rodadas:** o `SetAccessTokenLifetime` removido (a aplicação reprova com 900); aplicado a todo `client_credentials` e ao painel (o painel reprova com 300).
  **Guarda feito, duas rodadas:** sem o `SetAccessTokenLifetime`, `Expected: 300 Actual: 900`; aplicado a todo token, `Expected: 900 Actual: 300`. Voltaram `ClientCredentialsTokenHandler.cs` (`c2a0a7f1…`) e `UserPrincipalFactory.cs` (`ec6a4e9e…`).
- [x] 3.10 Casos "Escopo de outro app recusado para a aplicação" (`invalid_request`), "Sem refresh token" (nenhuma linha em `openiddict_tokens` nem `openiddict_authorizations`) e "Sem scope, o apps/api recusa" (`401`).
  **Feito.**

## 4. Reconhecer o token no apps/api (apps/api; `route-roles`, `route-authentication`, D1, D2)

- [x] 4.1 `ServiceScopeAuthorizationHandler`: `ApplicationSubject = "application"`, entrada vazia na `ServiceRoutes`, com o comentário que aponta a #187 e o comentário do campo dizendo "subjects sem papel".
  **Feito.**
- [x] 4.2 `IssuerTokenAuthentication.MapPrincipalByClient`: a ordem do D1 (tabela fechada → `application` pelo claim e fora do prefixo → nenhum), o `buteco_app` preservado no principal, o `role` não lido.
  **Feito.**
- [x] 4.3 **(guarda)** "buteco_app forjado num cliente da tabela fechada": um token do `buteco-servico-inbox` com `buteco_app` chama `GET /agents/{id}` → `200`, subject `service:inbox`. **Defeito:** o mapeamento conferindo o claim antes da tabela fechada (ou sem conferir o `client_id`).
  **Guarda feito:** com o mapeamento por claim antes da tabela, `AForgedApplicationClaim_OnAClosedTableClient_KeepsItsSubject` reprovou (`Expected: OK Actual: Forbidden`); voltou `IssuerTokenAuthentication.cs` (`481d9301…`).
- [x] 4.4 Caso "Cliente fora do prefixo sem buteco_app" → `403`; caso de `route-authentication` "Token de aplicação externa recusado em rota do painel".
  **Feito.**
- [x] 4.5 **(guarda)** `ExternalApplicationAuthorizationTests.ApplicationToken_OnEveryAuthenticatedRoute_ReturnsForbidden`: toda rota autenticada mapeada responde `403` ao token de aplicação, e nenhuma `401`; afirma que a lista percorrida não é vazia e inclui `POST /agents/{id}/a2a`. **Defeito:** `("POST", "/agents/{id}/a2a")` acrescentada à lista de `application`. É o teste que a #187 muda de asserção ([comentário na #187](https://github.com/aldovrando-oliveira/buteco-agentes/issues/187#issuecomment-6751534670)).
  **Guarda feito:** com a A2A na lista de `application`, `ApplicationToken_OnEveryAuthenticatedRoute_ReturnsForbidden` reprovou com "POST /agents/{id}/a2a: 200"; voltou `ServiceScopeAuthorizationHandler.cs` (`87a2c5fd…`).
- [x] 4.6 Caso "A2A recusada sem publicar job".
  **Feito.**
- [x] 4.7 `RouteDeclarationValidation` / `ServiceScopeRouteValidation`: lista vazia de subject derruba o boot, com a exceção nomeada `application` (comentário: temporária, sai na #187); `ValidateIssuerClientSubjects` exige a chave `application`. Ajustar o arranjo de `RouteDeclarationValidationTests` (lista do topo).
  **Feito** (o arranjo de `RouteDeclarationValidationTests`, lista do topo).
- [x] 4.8 **(guarda)** "Lista vazia de subject de serviço" derruba o boot nomeando `service:connectors`. **Defeito:** a checagem de lista vazia removida. Casos "A lista vazia de application passa, por exceção" e "Sem a entrada application".
  **Guarda feito:** sem a checagem de lista vazia, `AnEmptyServiceRouteList_Throws_NamingTheSubject` reprovou (`Assert.Throws() Failure: No exception was thrown`); voltou `RouteDeclarationValidation.cs` (`4db403dc…`).

## 5. As rotas do Admin (apps/api; `external-applications`, D3)

- [x] 5.1 `ExternalApplications/ExternalApplicationEndpoints.cs` e o serviço: as 8 rotas, as 8 entradas `AdminOnly` na `RoleRoutes`, `CurrentUser.FindAdminAsync` em todas, a ordem de conferência da spec (403 → 423 → 400 → 404/409 → 401). Mapear no `Program.cs`.
  **Feito.**
- [x] 5.2 Criar: aplicação, cliente do OpenIddict e primeiro segredo numa transação; casos "Criação devolve o client ID e o segredo uma vez", "Senha errada não cria", "Corpo inválido não conta no bloqueio", "Nome repetido, sem diferenciar maiúscula", "Bloqueado antes de tudo", "Client ID enviado no corpo é ignorado".
  **Feito.**
- [x] 5.3 Listar e detalhe: casos "Busca por client ID", "Desativadas continuam listadas, depois das ativas", "Estado desconhecido", "Os quatro estados", lista vazia `200 []`, `404` "Aplicação não encontrada.".
  **Feito.**
- [x] 5.4 Editar: caso "Renomear não muda a credencial" (com o `DisplayName` no OpenIddict); `400` e `409` da edição.
  **Feito.**
- [x] 5.5 Desativar e reativar, a desativação e a revogação dos segredos numa transação.
  **Feito.** Divergência: gerar segredo confere a senha fora da transação, e trava e confere de novo depois (D10).
- [x] 5.6 **(guarda)** "Aplicação desativada não obtém token" (`invalid_client`, segredos `revogado`). **Defeitos, em três rodadas:** a desativação sem a revogação (reprova a asserção de estado `revogado`); a desativação sem a revogação **e** o gerente sem o filtro de ativa (reprova o `invalid_client`); o `UPDATE` dos segredos fora da transação, com o interceptor falhando o segundo comando (reprova o caso da transação: aplicação desativada com segredo vivo).
  **Guarda feito, três rodadas:** (1) sem a revogação: `Assert.All() Failure: 2 out of 2 … Expected "revogado" Actual "ativo"/"substituido"`; (2) sem a revogação e sem o filtro de ativa: `Expected: "invalid_client" Actual: null`; (3) a revogação fora da transação: `ADeactivationThatFailsHalfway…` com `Assert.Null() … has a value`. Voltaram `ExternalApplicationEndpoints.cs` (`eedabe75…`) e, na (2), `ButecoApplicationManager.cs` (`67e06478…`).
- [x] 5.7 Casos "Reativar não devolve o segredo antigo" e "Excluir não existe".
  **Feito.**
- [x] 5.8 Gerar segredo: casos "O anterior vale mais 7 dias", "O anterior que vence antes não ganha prazo", "Senha errada não gera", o `409` de aplicação desativada.
  **Feito.**
- [x] 5.9 **(guarda)** "O anterior que vence antes não ganha prazo". **Defeito:** o `min` trocado por atribuição direta (`agora + 7 d`).
  **Guarda feito:** com o `min` trocado por atribuição, `GeneratingASecret_NeverExtendsAPreviousThatExpiresSooner` reprovou (`Expected 2026-11-11T21:49:29 Actual 2026-11-16T21:49:30`); voltou `ExternalApplicationSecret.cs` (`72827fe1…`).
- [x] 5.10 **(guarda)** "Quarto segredo vivo recusado" (`409`, nenhum segredo criado). **Defeito:** o limite removido.
  **Guarda feito:** sem o limite, `TheFourthAliveSecret_IsConflict_AndNothingIsCreated` reprovou (`Expected: Conflict Actual: Created`); voltou `ExternalApplicationEndpoints.cs` (`eedabe75…`).
- [x] 5.11 Revogar: casos "Revogado recusado no próximo pedido", "Segredo de outra aplicação", revogar o já revogado `200` sem mudança.
  **Feito.**
- [x] 5.12 Matriz: `RoleMatrixTests.Expected` com as 8 linhas (lista do topo); casos "Operador e Leitor não veem aplicações", "Só o Admin em Aplicações externas" e "Admin rebaixado perde Aplicações na requisição seguinte".
  **Feito.**

## 6. A retenção das chaves no boot (apps/api; `identity-signing-keys`, D5)

- [x] 6.1 `IdentityIssuerStartupValidation`: `max(access global, access de aplicação, refresh) + CheckInterval`, nomeando o maior ("access token de aplicação" quando for ele).
  **Feito** (`ValidateKeyRetention`/`Longest`, D10).
- [x] 6.2 **(guarda)** "Duração do token de aplicação acima da retenção derruba o boot": a checagem montada com a duração de aplicação acima de `Retention − CheckInterval` falha citando a retenção e o access token de aplicação. **Defeito:** a constante de aplicação fora do `max`.
  **Guarda feito:** sem a duração de aplicação no `max`, `AnApplicationTokenOutlivingTheKeyRetention_FailsNamingIt` reprovou (`No exception was thrown`); voltou `IdentityIssuerStartupValidation.cs` (`7053c997…`).

## 7. AgentCard (apps/api; `a2a-agent-card`, D7)

- [x] 7.1 `AgentCardEndpoints`: `oauth2` com `clientCredentials` (`tokenUrl`, escopo `buteco-api`, `OAuth2MetadataUrl`) a partir do issuer do `IdentityIssuerState`. Mudar os dois casos de `AgentCardSecuritySchemeTests` (lista do topo). *(Corrigido no apply, premissa 7: o requisito do card com `bearer` "emissor desligado" saiu da delta, porque o estado não existe num app no ar; o código não tem esse ramo, e o caso "Emissor desligado" nunca foi escrito, então nenhum caso existente sai por isso.)*
  **Feito.**

## 8. Acordos e docs

- [x] 8.1 (`tests/ApiConnectorsRoundTrip.Tests`) uma aplicação criada pela rota real do `apps/api` real obtém token por `client_credentials` e chama `GET /connectors/providers` no `apps/connectors` → `401` (`aud` só `buteco-api`); e o mesmo token chama `GET /agents/` no `apps/api` → `403`, não `401` (o `apps/api` a reconhece como `application`).
  **Feito:** `ExternalApplicationRoundTripTests` (as chaves do `apps/connectors` aquecidas antes com o token do Admin, para o `401` ser só da audience).
- [x] 8.2 (`tests/InboxOrchestratorRoundTrip.Tests`) nada novo: o `service:inbox` continua passando na A2A com a entrada `application` presente, pelos casos que já existem.
  **Feito** (nada novo).
- [x] 8.3 `docs/a2a-integration.md`: como obter a credencial (Admin, Aplicações externas), o pedido de token com `scope=buteco-api`, o pedido sem `scope` (token sem `aud`, `401`, Q4), os 5 min e o que "revogar" corta, o AgentCard com `oauth2`, e que a chamada A2A responde `403` até a #187; corrigir as rotas anônimas (premissa 5).
  **Feito.**
- [x] 8.4 `01-ARQUITETURA_E_CONVENCOES.md` (Autenticação: o subject `application`, os segredos próprios, os 5 min, a exceção temporária); `docs/deployment.md` (a volta atrás e o `DELETE` das linhas `btc_app_%`).
  **Feito.**
- [x] 8.5 Antes do archive: `02-HISTORICO_E_STATUS.md` e `CHANGELOG.md`, com a medição da convenção 18 contra a projeção do design (produção, testes, duplos e casos, em colunas separadas).
  **Feito:** `02-HISTORICO_E_STATUS.md` ("`aplicacoes-externas-credencial`") e `CHANGELOG.md` (Added e Changed).

## 9. Lista dos guardas (conferência)

| tarefa | caso | defeito |
|---|---|---|
| 2.2 | Prefixo reservado nunca é gravado | `Ensure` removido |
| 3.2 | Segredo de enchimento nunca vale | gerente cai no `base` |
| 3.3 | Cliente de serviço não passa pelo cadastro | gerente consulta o cadastro para todo cliente |
| 3.4 | Segredo revogado ou vencido | sem o filtro de revogado; sem o de vencido |
| 3.8 | Os clientes do Buteco nunca levam buteco_app | `buteco_app` em todo `client_credentials` |
| 3.9 | `exp − iat` = 300 e 900 | lifetime removido; lifetime em todo token |
| 4.3 | buteco_app forjado num cliente da tabela fechada | mapeamento só pelo claim |
| 4.5 | ApplicationToken_OnEveryAuthenticatedRoute_ReturnsForbidden | A2A na lista de `application` |
| 4.8 | Lista vazia de subject de serviço | checagem de lista vazia removida |
| 5.6 | Aplicação desativada não obtém token | desativação sem revogação; e sem o filtro de ativa; e fora da transação |
| 5.9 | O anterior que vence antes não ganha prazo | `min` trocado por atribuição |
| 5.10 | Quarto segredo vivo recusado | limite removido |
| 6.2 | Duração do token de aplicação acima da retenção | constante fora do `max` |

## 10. Fechamento

- [x] 10.1 `dotnet build` sem aviso novo; `openspec validate aplicacoes-externas-credencial --strict`.
  **Feito:** nenhum aviso novo (17 distintos na change contra 20 na baseline: saíram os 3 do caso do AgentCard reescrito); `openspec validate --strict` válido.
- [x] 10.2 Suítes da change pelo `suite-186.sh`, uma por vez, em `~/.cache/buteco-agents/186-fechamento/`.
  **Feito:** `api` 1ª rodada INCONCLUSIVA (400 reprovações por timeout, CPU mín. 23, `186-fechamento/`), 2ª rodada **APROVADA** 1640 + 1 ignorado, CPU mín. 62 (`186-fechamento-api2/`), com os 18 casos novos da inconclusiva todos aprovados; `rt-inbox` 15/15 APROVADA (CPU mín. 97); `rt-connectors` 12/12 APROVADA (CPU mín. 100). `podman ps` do início de cada uma: `postgres_1` e `rabbitmq_1`.
- [x] 10.3 Comparação por nome contra a baseline (`nomes-trx.py`, GUID normalizado, multiconjunto): nenhum caso some nem muda fora da lista do topo; cada novo ligado ao requisito.
  **Feito:** `186-fechamento-final/nomes.txt` (1.668): saem só os 2 de `AgentCardSecuritySchemeTests` da lista do topo; entram 78, todos aprovados, cada classe ligada ao requisito (no `02`). Nenhum caso fora da lista saiu nem mudou.
- [x] 10.4 O design corrigido com toda divergência achada no apply (convenção 9).
  **Feito:** D10 e a premissa 7 no design; a Q15 corrigida.

### Conferências da seção 10 (pedidas pelo dono, 09/11/2026)

- [x] 10.5 Conferências 1 a 8 com o `apps/api` local num banco descartável (`buteco_agents_conf186`, copiado por `pg_dump` pelo `template0`, migrado por `dotnet ef database update`), o Admin pelo `password-reset-link` e pelo login real com as 2 etapas, e o `curl` pelo `~/.cache/buteco-agents/bin/conferencia-186.py`; a 8 com a imagem de `71046f6` (`podman build`) sobre o banco migrado. Saídas em `~/.cache/buteco-agents/186-conferencia/`. O banco, o contêiner e a imagem foram apagados. Resultado no `02`.

