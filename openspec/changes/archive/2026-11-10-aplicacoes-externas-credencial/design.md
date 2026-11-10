## Context

A #186 cria a aplicação externa: um cliente do emissor do `apps/api` (#179) que o Admin cadastra
e que obtém token por `client_credentials`. Qual agente cada aplicação alcança é da #187, e a
tela é da #188. Pela convenção 1, o backend desta change serve o que os quadros da #188 mostram
(ver "Os quadros do protótipo (h)").

### O código em `71046f6` (main depois da #185)

- **Autorização no `apps/api`**: `ServiceScopeAuthorizationHandler` (`Auth/ServiceScopeAuthorizationHandler.cs`)
  tem dois ramos. O subject `operator` vai à `RoleRoutes` pelo papel; os subjects de serviço vão
  à `ServiceRoutes` (`service:inbox` → `POST /agents/{id}/a2a` e `GET /agents/{id:guid}`;
  `service:connectors` → as cinco de `/sync/knowledge-bases/*`). Qualquer outro subject, ou
  nenhum, recebe `403`.
- **O subject sai da tabela fechada** `IssuerClientSubjects` (`Identity/Validation/IssuerClientSubjects.cs`):
  `buteco-painel`, `buteco-servico-inbox`, `buteco-servico-connectors`. Cliente fora dela
  autentica sem `NameIdentifier` (`IssuerTokenAuthentication.cs` l. 96–122) e recebe `403`.
- **Emissão**: `ClientCredentialsTokenHandler` põe `sub` = `client_id`, os escopos e a `aud` dos
  recursos. A duração é a global, `IdentityTokenLifetimes.AccessToken` = 15 min (l. 23).
- **Catálogo**: `IdentityClientCatalog` sincroniza os três clientes `buteco-*` do código e só loga
  cliente `buteco-*` não declarado; cliente fora do prefixo não é tocado.
- **Segredo de cliente no OpenIddict 7.7.2**: um por aplicação (`ClientSecret`), PBKDF2-HMAC-SHA256,
  10.000 iterações, sal de 128 bytes, chave de 32 bytes (`OpenIddictApplicationManager.cs`
  l. 1714–1722). Cliente confidencial sem segredo nem JWKS é recusado na gravação (l. 1286–1296).
  O servidor confere o segredo por `IOpenIddictApplicationManager.ValidateClientSecretAsync`
  (`OpenIddictServerHandlers.cs` l. 1224), que é `virtual` (`OpenIddictApplicationManager.cs` l. 1363)
  e substituível por `ReplaceApplicationManager` (`OpenIddictCoreBuilder.cs` l. 53).
- **Duração por token**: a duração posta no principal vale antes da da aplicação e da global
  (`OpenIddictServerHandlers.cs` l. 3680–3703; `principal.SetAccessTokenLifetime`,
  `OpenIddictExtensions.cs` l. 3182).
- **Retenção das chaves**: 15 dias (`IdentityKeyRotationPolicy.cs` l. 34). O boot exige
  `Retention ≥ max(access, refresh) + CheckInterval`, com as durações **globais**
  (`IdentityIssuerStartupValidation.cs` l. 68–83).
- **AgentCard**: anônimo, `securitySchemes` = `bearer` (`httpAuthSecurityScheme`), em
  `A2A/AgentCardEndpoints.cs` l. 73–92. O SDK `A2A` 1.0.0-preview2 tem `OAuth2SecurityScheme`,
  `OAuthFlows.ClientCredentials` e `ClientCredentialsOAuthFlow { TokenUrl, Scopes }`.
- **Senha de quem chama**: `Users/AccessLinks/CallerPassword.cs`, usada no convite, no novo link,
  no link de redefinição e na promoção a Admin. O motivo escrito lá: são as rotas que transformam
  um access token roubado (15 min) em acesso permanente.

## Goals / Non-Goals

**Goals:** cadastro de aplicação externa só do Admin; segredos gerados, com hash, validade,
sobreposição e revogação; emissão de token curta; o `apps/api` reconhecer o token da aplicação e
recusá-lo em toda rota até a #187; último uso; AgentCard com OAuth2; docs.

**Non-Goals:** qual agente e qual operação cada aplicação alcança (#187); a tela (#188);
aplicação federada (M4); introspecção; mudar o `apps/inbox` ou o `apps/connectors`.

## Decisions

### D1. (a) Por onde a aplicação externa entra

**Recomendação.** Rota `POST /agents/{id}/a2a`, a mesma do `apps/inbox` (todo método A2A —
`SendMessage`, `GetTask`, `CancelTask`, configuração de push — é JSON-RPC nessa rota). Subject
novo **`application`**. Tabela: a `ServiceRoutes` do `ServiceScopeAuthorizationHandler`, a dos
subjects sem papel, com uma entrada `application`. **Nesta change a lista dela é vazia** (D2); a
#187 acrescenta `("POST", "/agents/{id}/a2a")` junto com o filtro por agente.

**Como o `apps/api` sabe que o token é de uma aplicação externa, sem banco.** O
`ClientCredentialsTokenHandler`, que já roda no emissor e pode ler o banco, procura o `client_id`
em `external_applications`. Achou: põe no access token o claim `buteco_app` com o id da aplicação
(GUID) e a duração de 5 min (D5). No `MapPrincipalByClient`, a ordem fica:

1. `client_id` na tabela fechada `IssuerClientSubjects` → o subject dela (como hoje);
2. senão, claim `buteco_app` presente **e** `client_id` fora do prefixo `buteco-` → subject
   `application`, e o claim segue no principal para a #187;
3. senão → sem subject, `403` (como hoje).

O token de aplicação nunca traz `role`, e o `role` não é lido para `application` (o mesmo da regra
dos subjects de serviço).

**Alternativa descartada: subject por prefixo** (todo `client_id` fora de `buteco-` vira
`application`). Funciona sem claim, mas transforma em aplicação qualquer cliente do OpenIddict fora
do prefixo, inclusive o "cliente de teste fora do prefixo `buteco-`" que o acordo da
`issuer-token-validation` já cadastra, e um cliente criado à mão no banco. O claim só sai do
emissor para quem está no cadastro.

**Alternativa descartada: terceira tabela (`ApplicationRoutes`).** Seria um terceiro ramo no
handler e uma terceira fonte na checagem de boot, sem diferença de comportamento: a aplicação é,
para a autorização, um subject sem papel com lista própria, que é o que a `ServiceRoutes` já é
(convenção 2). O nome do campo e o comentário passam a dizer "subjects sem papel".

**A checagem de boot, nos dois sentidos.**

- **Rota → declaração** (`RouteDeclarationValidation`): não muda. `POST /agents/{id}/a2a` já está
  declarada pelo `service:inbox`; as oito rotas novas de `/applications/*` entram na `RoleRoutes`
  (D3).
- **Declaração → rota**: a lista de `application` passa pela mesma conferência das outras; vazia,
  não confere nada, e a #187 a enche com a conferência já pronta.
- **Novo**: o `ValidateIssuerClientSubjects` confere hoje que todo subject de destino da tabela de
  clientes existe na `ServiceRoutes`/`operator`. Passa a conferir também o subject fixo
  `application`, que não vem da tabela: sem a entrada na `ServiceRoutes`, o boot falha.
- **Novo também: lista vazia de subject.** Hoje nada confere lista vazia na `ServiceRoutes` (só na
  `RoleRoutes`, `RouteDeclarationValidation.cs` l. 75). Passa a derrubar o boot, com uma exceção
  nomeada: `application`, até a #187, que tira a exceção junto com a rota.

**Guarda de D2:** um teste chama toda rota autenticada mapeada do host com um token de aplicação
e afirma `403` em todas, inclusive na A2A (o mesmo molde do "Recusa vale para toda rota autenticada
mapeada" do `apps/inbox`). Ele reprova se alguém acrescentar a rota A2A à lista sem a #187.

**O `docs/a2a-integration.md` hoje.** Ensina o endpoint `POST /agents/{id}/a2a` com
`Authorization: Bearer <token>`, `SendMessage`, `GetTask`, push e o AgentCard com o esquema
`bearer`, e diz que não existe credencial para cliente externo (l. 74–82, reescrito na #184).
**Os exemplos não funcionam para ninguém de fora, em `85c1b35` nem em `71046f6`**: não há token que
um terceiro consiga obter e que essa rota aceite. Isso é verdade desde a #184 (`a00966a`,
09/11/2026), quando o token do painel passou a receber `403` na rota; entre a #217 (`649e582`,
01/11/2026) e a #184 ainda era possível usar o token do painel copiado do navegador. A #185
(`71046f6`) não tocou nada disso. **Efeito:** nenhum consumidor A2A externo funciona hoje; só o
`apps/inbox`. O guia já declara a limitação, então o efeito não é um contrato quebrado em
silêncio. **Mas há uma premissa falsa no próprio guia** (l. 70–72): "Só três rotas de `apps/api`
são anônimas: `/health`, `/auth/login` e o AgentCard". `POST /auth/login` não existe desde a
`649e582` (#217), e as anônimas hoje incluem também `/auth/session`, `/auth/session/second-factor`,
`/connect/*` e `/.well-known/*`. Efeito: quem segue o guia chama `/auth/login` e recebe `401`, a
resposta de rota inexistente sem token. Corrigido nesta change (k).

### D2. (b) O que a aplicação alcança entre a #186 e a #187

**Recomendação: nenhum agente.** Com a #186 em produção e a #187 não, a aplicação tem client ID,
segredo e obtém token; o token autentica no `apps/api` e recebe `403` em toda rota, inclusive na
A2A. O que a tela de Nova aplicação já diz ("A aplicação nasce sem nenhum agente liberado…") fica
verdadeiro desde o primeiro dia, e a #187 abre a rota e o filtro **no mesmo deploy**: a rota A2A
nunca fica aberta a uma aplicação sem o filtro por agente, em qualquer ordem de produção.

**Alternativa descartada: todos os agentes.** Seria a primeira vez em que um token de terceiro
alcança todo agente, mais `GetTask`/`CancelTask` de qualquer task pelo id (o handler não sabe quem
criou a task), por quanto tempo a #187 demorar. A aplicação cadastrada para testar viraria acesso
total sem ninguém decidir.

**Alternativa descartada: rota aberta, recusa dentro do handler A2A** (a aplicação passa na
autorização e o handler responde "agente não encontrado" a todo agente). Exige que a #186 invente a
forma da recusa por agente, que é da #187, e põe código na rota A2A que a #187 vai reescrever. O
`403` vazio da autorização é a mesma recusa de toda rota fora da lista.

**Consequência para o critério da #186:** a prova de ponta a ponta é "a aplicação obtém token no
emissor real e o `apps/api` a reconhece como `application`" (o `403` da A2A, e não `401`), não uma
chamada A2A atendida. Ela é da #187.

### D3. (c) O cadastro

- **Client ID**: `btc_app_` + 8 caracteres do alfabeto `0-9a-z` sem `0`, `1`, `l`, `o` (32
  símbolos, 40 bits), gerado pelo `RandomNumberGenerator`, como no quadro (`btc_app_7kq2m9x4`).
  Não é segredo: o tamanho é para não colidir, e o índice único com nova tentativa (até 3) fecha o
  resto. Imutável. *Descartado:* GUID (ilegível na lista e no `curl`), escolha do Admin (abre a
  porta ao prefixo reservado e a nomes que mudam de sentido).
- **Prefixo `buteco-` recusado**: nenhuma rota recebe `client_id` (é sempre gerado), e o gerador
  nunca produz `buteco-`. A recusa fica escrita num ponto só, `ExternalApplicationClientId.Ensure`,
  chamado antes de gravar: `client_id` com `buteco-` lança, e a criação responde `500` sem gravar.
  É defesa, não validação de entrada. Guarda: um gerador falso que devolve `buteco-x` faz a criação
  falhar sem nenhuma linha em `external_applications` nem em `openiddict_applications`.
- **Escopo**: o descritor da aplicação tem `ept:token`, `gt:client_credentials` e
  `scp:buteco-api`, e nada mais: **sem `gt:refresh_token`** (herança da #209, D2), sem `buteco-inbox`
  nem `buteco-connectors`. Pedir `scope=buteco-inbox` é recusado sem token, com `invalid_request`, como o do
  cliente de serviço (spec viva `oidc-issuer`, "Escopo de outro app é recusado"; a primeira redação
  deste design dizia `invalid_scope`, sem conferir a spec). Pedir sem `scope` emite um token sem `aud`, que o `apps/api` recusa com `401` — o guia
  manda `scope=buteco-api` (Q4).
- **Nome e descrição**: nome obrigatório, 1–80 caracteres sem espaço nas pontas, único entre todas
  as aplicações (ativas e desativadas) sem diferenciar maiúscula; descrição opcional, até 280. O
  nome vai também ao `DisplayName` do cliente no OpenIddict. Editar troca os dois; o client ID não
  muda. O "Responsável técnico" saiu do quadro na versão 60 (Q6).
- **Desativar em vez de excluir**: `POST /applications/{id}/deactivate` marca `deactivated_at` e
  **revoga todos os segredos vivos**; o emissor recusa a aplicação desativada mesmo assim (dupla
  barreira). `POST /applications/{id}/activate` volta a ativa **sem segredo**: o Admin gera um novo
  (com a senha, D4). Assim reativar não devolve credencial a ninguém e não precisa de senha.
  *Descartado:* reativar com os segredos de antes (um segredo que vazou e motivou a desativação
  voltaria a valer).
- **Quem pode**: só Admin, inclusive ler, nas oito rotas. Como em `/users/*`, exigem o papel
  `admin` **gravado** (`CurrentUser`), além do papel do token: o Admin rebaixado perde Aplicações na
  requisição seguinte, e não em até 15 min 30 s. Uma credencial permanente é o pior caso do token
  velho.
- **As rotas, todas `AdminOnly` na `RoleRoutes`:**

| método | padrão | o que faz | senha |
|---|---|---|---|
| `GET` | `/applications/` | lista; `?busca=` (nome ou client ID, parcial, sem maiúscula), `?estado=ativas\|desativadas` | — |
| `POST` | `/applications/` | cria; devolve a aplicação, o client ID, o segredo (uma vez) e o endpoint de token | sim |
| `GET` | `/applications/{id:guid}` | detalhe, com os segredos (sem hash nem texto) e o endpoint de token | — |
| `PUT` | `/applications/{id:guid}` | nome e descrição | — |
| `POST` | `/applications/{id:guid}/deactivate` | desativa e revoga os segredos vivos | — |
| `POST` | `/applications/{id:guid}/activate` | reativa, sem segredo | — |
| `POST` | `/applications/{id:guid}/secrets` | gera segredo; devolve o texto uma vez | sim |
| `POST` | `/applications/{id:guid}/secrets/{secretId:guid}/revoke` | revoga | — |

  Ordem da lista: ativas antes, depois nome. A resposta de lista e de detalhe traz `ultimoUso`
  (D6) e, no detalhe, `endpointToken` = `{issuer}connect/token`, a URL pública que o quadro
  mostra.

### D4. (d) Os segredos

- **Geração**: `btc_sk_` + 43 caracteres base64url de 32 bytes do `RandomNumberGenerator`
  (256 bits). Devolvido no corpo da criação da aplicação ou do segredo, **uma vez**; nenhuma outra
  resposta o traz (convenção 3; a exceção é a mesma do segredo do TOTP: sem o texto não há
  credencial). Guarda-se também o **sufixo** de 4 caracteres para a lista (`btc_sk_••••a91f`), 24 de
  256 bits.
- **Hash**: **SHA-256** do texto, em `bytea`, com índice único — o mesmo do `AccessLinkToken.Hash`
  e do `RecoveryCode.Hash` da casa. O segredo tem 256 bits de entropia gerados pelo Buteco, então um
  hash lento não acrescenta nada contra ataque fora de linha, e o índice permite achar o segredo
  pelo hash, sem percorrer os da aplicação. **Comparado com o dos clientes de serviço:** o OpenIddict
  guarda o `ClientSecret` com PBKDF2-HMAC-SHA256, 10.000 iterações (l. 1714–1722), pensado para
  segredo escolhido por gente; os segredos de serviço continuam lá, como estão. *Descartado:* o
  PBKDF2 do OpenIddict para os segredos de aplicação (vários por aplicação obrigariam a derivar a
  chave de cada um a cada pedido de token, sem ganho de segurança).
- **Onde ficam**: tabela própria `external_application_secrets`, fora do `ClientSecret`, porque o
  OpenIddict tem um segredo por aplicação. A linha da aplicação em `openiddict_applications` leva um
  **segredo de enchimento**: 32 bytes aleatórios, descartados na hora, que ninguém conhece — o
  OpenIddict exige segredo em cliente confidencial (l. 1286–1296). A conferência real é de um
  `ButecoApplicationManager : OpenIddictApplicationManager<…>` que sobrescreve
  `ValidateClientSecretAsync(application, secret)`: cliente que está em `external_applications`
  passa só pelos segredos dele (e nunca pelo de enchimento); qualquer outro cai no `base`, como hoje.
  *Descartado:* um handler próprio no lugar do `ValidateClientSecret` do servidor (mais acoplado à
  ordem dos handlers, e o gerente é o ponto que o OpenIddict documenta para isso).
- **Validade**: 365 dias a partir da criação (o quadro: 21/10/2026 → 21/10/2027). Constante, não
  opção nem campo da tela (convenção 2: ninguém pediu outro valor).
- **Rotação com sobreposição**: gerar um segredo novo deixa cada segredo vivo anterior **substituído**,
  com `expira_em = min(expira_em, agora + 7 dias)`. Os dois valem durante a sobreposição. **7 dias,
  constante** (convenção 2): a sobreposição só serve para a aplicação trocar sem parar, e nenhum
  cenário real pede outro valor; se pedir, vira opção. Para cortar antes, "Revogar agora".
- **Estados de um segredo** (derivados, nunca gravados como texto): `ativo`, `substituido` (vivo,
  dentro da sobreposição), `expirado`, `revogado`. **Limite**: 3 segredos vivos por aplicação;
  gerar o quarto responde `409` com "Revogue um segredo antes de gerar outro." (Q8).
- **Revogação imediata**: `revogado_em` gravado; o próximo pedido de token com aquele segredo recebe
  `invalid_client`. O token já emitido continua até o `exp` (D5).
- **Senha de quem chama**: **criar a aplicação e gerar segredo pedem a senha**, pelo
  `CallerPassword`, com o mesmo `401` com corpo, a mesma contagem de bloqueio e o `423` antes. É o
  critério escrito no próprio `CallerPassword`: as duas rotas transformam um access token roubado em
  acesso permanente. **Revogar, desativar, reativar e editar não pedem**: só tiram acesso, ou (a
  reativação) não devolvem credencial.

### D5. (e) "Revogar corta na hora" contra o token já emitido

**Recomendação: duração curta só para o token de aplicação — 5 min**, posta **no principal** pelo
`ClientCredentialsTokenHandler` (`identity.SetAccessTokenLifetime(...)`), a partir de uma constante
nova `IdentityTokenLifetimes.ApplicationAccessToken`. O painel continua com 15 min. O texto da tela
passa a dizer o prazo: "Revogar recusa o segredo na hora. Um token já emitido continua valendo por
até 5 minutos." (e o mesmo para desativar). A resposta do token já traz `expires_in` = 300.

**Por que no principal, e não no descritor** (`Settings["tkn_lft:act"]`): o valor do descritor mora
no banco, e a checagem de boot da retenção não o enxerga sem I/O — é o problema registrado na issue
em 31/10/2026. No principal, a duração sai de uma constante do código, que o boot lê, e uma linha
adulterada no banco não a muda (o principal vence o descritor, `OpenIddictServerHandlers.cs`
l. 3680–3681). Como nenhuma aplicação tem duração própria no banco, não há "maior duração entre as
aplicações" a procurar: ela é a constante.

**A checagem de boot da #179/#180** (`IdentityIssuerStartupValidation.cs` l. 68–83) passa a exigir
`Retention ≥ max(access global, access de aplicação, refresh) + CheckInterval`, nomeando o maior.
Hoje: 15 d ≥ max(15 min, 5 min, 14 d) + 1 h. **Guarda:** um caso que monta a checagem com a duração
de aplicação acima de `Retention − CheckInterval` e afirma a falha nomeando "access token de
aplicação"; ele reprova com a constante fora do `max` (convenção 15). Um segundo guarda afirma
`exp − iat` = 300 s no token emitido pelo host real, e 900 s no do painel.

**Alternativa descartada: introspecção.** Corta na hora, mas cada chamada A2A passa a depender de
uma consulta ao emissor (ou ao banco do emissor), contra o modelo da #208. E o endpoint de
introspecção seria um endpoint OIDC a mais na lista conferida no boot (`route-authentication`, o
cenário "Endpoint configurado fora da lista derruba o boot" cita justamente a introspecção).

**Por que 5 e não 10:** é o pior caso de "Revogar" que a tela tem de confessar, e o custo é um
pedido de token a cada ~5 min por aplicação ativa, sem linha gravada (#209 D1). Q5.

### D6. (f) Último uso

**Recomendação: gravado na emissão do token**, dentro do `ButecoApplicationManager`, quando um
segredo confere: `ultimo_uso_em` do segredo e da aplicação, num `UPDATE … WHERE ultimo_uso_em IS
NULL OR ultimo_uso_em < agora − 1 min` (no máximo uma escrita por minuto por segredo, mesmo com um
cliente que pede token a cada chamada).

- **Precisão:** com o token de 5 min, o último uso real de uma aplicação que chama fica entre o
  último token e 5 min depois dele; a tela mostra "há N min/h/dias" e "Nunca usada", que cabem nessa
  precisão. Um pedido de token sem chamada conta como uso (Q9, o texto).
- **Custo por requisição A2A: zero.** Nada entra no caminho da chamada. A arquitetura proíbe a
  requisição dos outros apps esperar o `apps/api`, e aqui nem o próprio `apps/api` espera: a escrita
  é na rota de token, uma vez a cada ~5 min por aplicação.
- **E é o único lugar possível nesta change:** pelo D2, nenhuma chamada da aplicação é atendida até
  a #187.

**Alternativa descartada: gravar na chamada A2A** (direto ou por fila em memória com escrita a cada
minuto). Precisão de segundo, mas põe escrita ou estado em toda requisição A2A, e na #186 nunca
dispararia. Se a #187 quiser "última chamada por agente", é dado novo dela.

### D7. (g) O AgentCard com `securitySchemes`

Cada card troca o esquema `bearer` por **`oauth2`**: `OAuth2SecurityScheme` com
`Flows.ClientCredentials = { TokenUrl = {issuer}connect/token, Scopes = { "buteco-api":
"Chamar agentes pelo A2A" } }` e `OAuth2MetadataUrl = {issuer}.well-known/openid-configuration`;
`securityRequirements` = `{ oauth2: ["buteco-api"] }`. É igual em todo agente (a credencial é da
aplicação, não do agente). **O card continua anônimo** e não diz quais aplicações têm acesso — isso
seria da #187, e revelaria clientes.

O `apps/inbox` chama a mesma rota com o token de serviço e não lê o card; o `apps/frontend` só
mostra a URL do card (`AgentA2ACard`). Nenhum consumidor interno muda.

*Descartado:* manter `bearer` ao lado do `oauth2` (as duas entradas descrevem o mesmo token; um
cliente A2A que escolhe o `bearer` não sabe onde obtê-lo). **Corrigido no apply (convenção 9, premissa 7):** a primeira redação
previa um card com `bearer` "sem `Identity:Issuer`, emissor desligado". No `apps/api` o issuer é a
`PublicUrl:BaseUrl` normalizada (`IdentityIssuerState`), e sem ela o boot falha ("Url pública
obrigatória"); `Identity:Issuer` é o nome da configuração do `apps/inbox` e do `apps/connectors`. O
estado não existe num app que atende, então o ramo e o cenário saíram; o card de todo app no ar
declara `oauth2`.

### D8. Testes

Molde da #184: integração com o host real e Postgres (Testcontainers), cada guarda reprovado contra
o defeito antes de contar (convenção 15). Os acordos: no `rt-connectors`, o token de uma aplicação
emitido pelo `apps/api` real recebe `401` no `apps/connectors` (aud só `buteco-api`); no `rt-inbox`, o
`service:inbox` continua passando na A2A com a entrada `application` presente.

### D9. Os guardas, cada um com o defeito que o reprova (decisão do dono, 09/11/2026)

Cada guarda só conta depois de reprovar contra o defeito nomeado, com o defeito introduzido de
propósito e desfeito em seguida (convenção 15). A lista é a mesma do `tasks.md`, seção 9.

| guarda (caso) | defeito introduzido | origem |
|---|---|---|
| os três clientes do Buteco obtêm token sem `buteco_app` | o emissor pondo `buteco_app` em todo `client_credentials`, sem consultar o cadastro | Q1 |
| `buteco-servico-inbox` com `buteco_app` forjado continua `service:inbox` | o `apps/api` mapeando `application` só pelo claim, sem conferir o `client_id` | Q1 |
| o segredo de enchimento recebe `invalid_client` | o gerente caindo no `base` quando nenhum segredo do cadastro confere | gerente |
| o cliente de serviço não toca o cadastro (`ultimoUso` do segredo de mesmo texto fica `null`) | o gerente procurando o hash no cadastro para todo cliente | gerente |
| segredo revogado e segredo vencido recebem `invalid_client` | o gerente sem o filtro de revogado e, em outra rodada, sem o de vencido | gerente |
| o pedido de token de uma aplicação desativada recebe `invalid_client` | a desativação sem a revogação dos segredos **e** o gerente sem o filtro de ativa (os dois juntos: cada barreira sozinha segura o caso) | Q13 |
| todo segredo de uma aplicação desativada fica `revogado` | a desativação sem a revogação | Q13 |
| toda rota autenticada recusa `application` com `403` | `("POST", "/agents/{id}/a2a")` acrescentada à lista de `application` | Q2 |
| lista vazia de subject derruba o boot | a checagem de lista vazia removida | Q2 |
| o boot falha com a duração de aplicação acima da retenção | a constante de aplicação fora do `max` da checagem | Q5 |
| `exp − iat` = 300 (aplicação) e 900 (painel) | o `SetAccessTokenLifetime` removido do handler; e, em outra rodada, aplicado a todo `client_credentials` | Q5 |
| prefixo `buteco-` não grava linha | o `ExternalApplicationClientId.Ensure` removido | D3 |
| a sobreposição limita a 7 dias e não estende | o `min` trocado por atribuição direta | D4 |
| o 4º segredo vivo recebe `409` | o limite removido | Q8 |

**A desativação revoga numa transação** (Q13): a gravação de `desativada_em` e o `UPDATE` dos
segredos vivos vão na mesma transação; um caso com o banco falhando entre os dois (interceptor) não
deixa a aplicação desativada com segredo vivo, nem o contrário.

### D10. O que o apply mudou (convenção 9, 09/11/2026)

- **O issuer do `apps/api` é a `PublicUrl:BaseUrl` (premissa 7, em "(k)").** O D7 previa um card com
  `bearer` "sem `Identity:Issuer`"; o estado não existe num app no ar. O ramo e o cenário saíram da
  delta `a2a-agent-card`; o `endpointToken` e o `tokenUrl` usam o issuer do `IdentityIssuerState`.
- **Gerar segredo confere a senha FORA da transação.** O `LoginThrottleStore` grava a falha da senha
  pelo mesmo `AppDbContext` (escopo da requisição); dentro da transação desfeita, a contagem do
  bloqueio voltaria junto, e a senha errada não contaria. A rota faz as conferências (404, 409 da
  desativada, 409 do limite) sem trava, depois a senha, e então abre a transação, trava a linha
  (`FOR UPDATE`) e confere de novo. O caso "Senha errada não gera" afirma a contagem em 1.
- **O guarda 3.2 conhece o enchimento sem ponto de teste em produção.** O tasks.md previa capturar o
  enchimento na criação; o caso regrava o `ClientSecret` do cliente com um texto conhecido pelo próprio
  OpenIddict (`UpdateAsync` com descritor, que grava o PBKDF2 dele). O defeito que ele pega é o mesmo:
  o gerente caindo no `base`, que aceitaria aquele texto.
- **A checagem da retenção virou um método público** (`IdentityIssuerStartupValidation.ValidateKeyRetention`
  e `Longest`), chamado pelo boot com as constantes. A constante de produção (5 min) não permite montar
  o caso de falha pelo host; o guarda 6.2 chama a mesma checagem com as durações passadas. **Fica pública** (revisão do dono,
  09/11/2026): o dono pediu `internal` com `InternalsVisibleTo` se o `apps/api` já tivesse o padrão, e
  ele não tem — nenhum `InternalsVisibleTo` no `Buteco.Api.csproj`, no `Directory.Build.props` nem em
  atributo de assembly; os pontos de teste da casa são públicos. Introduzir o padrão por uma checagem
  só mudaria mais que essa linha.
- **"Substituído" é ter um segredo com `criado_em` mais novo.** Dois segredos no mesmo instante não
  acontecem em produção (a geração trava a linha e lê o relógio depois), mas acontecem com o relógio
  parado dos testes: os casos que geram segredo avançam o relógio do host 1 s antes. Avançar é seguro
  para os tokens: o OpenIddict 7.7.2 não põe `nbf` (nenhum `NotBefore` em
  `OpenIddictServerHandlers.Protection.cs`), e o `JwtBearer` só confere o `exp`.
- **Guarda 3.8:** o defeito pôs o claim nos três clientes; o caso para no primeiro que o leva (o
  `buteco-painel`), e a mensagem nomeia só ele.

## Risks / Trade-offs

- **Um token de aplicação vale até 5 min depois de revogar ou desativar** → declarado na tela (D5) e
  na spec; cenário de spec com o prazo.
- **Segredo de enchimento no OpenIddict** → se o gerente for removido, ninguém autentica (ninguém
  conhece o enchimento), o que falha fechado. Guarda: o enchimento não autentica (um caso que
  autentica pelo `base` com o texto gravado e afirma `invalid_client`, com o enchimento capturado).
- **Lista vazia de `application`** → o guarda "toda rota autenticada recusa `application`" (D1).
- **Volta atrás** → ver "Migration Plan (j)".

## Migration Plan (j)

- **Migration** `ExternalApplications`: duas tabelas novas, `external_applications` (id, client_id
  único, nome com índice único sem maiúscula, descricao, criada_em, desativada_em, ultimo_uso_em) e
  `external_application_secrets` (id, application_id FK, hash `bytea` único, sufixo, criado_em,
  expira_em, revogado_em, ultimo_uso_em). Só acréscimo; nenhuma tabela existente muda.
- **O que o OpenIddict passa a guardar:** uma linha em `openiddict_applications` por aplicação
  criada: `client_id` `btc_app_…`, confidencial, consentimento implícito, `DisplayName` = nome,
  permissões `ept:token`, `gt:client_credentials`, `scp:buteco-api`, segredo de enchimento
  (PBKDF2 do OpenIddict), sem `Settings`. Nenhuma linha em `openiddict_tokens` (#209 D1) nem em
  `openiddict_authorizations` (sem refresh). O `IdentityClientCatalog` não as toca (fora do
  prefixo).
- **Volta atrás:** a imagem anterior não tem o gerente estendido, então confere o segredo de
  enchimento, que ninguém conhece: **toda aplicação passa a receber `invalid_client`**, e os tokens já
  emitidos morrem em até 5 min. E mesmo com um token, a imagem anterior não mapeia `buteco_app`, e o
  cliente fora da tabela recebe `403`. A volta atrás falha fechada, sem passo manual. O `down` da
  migration apaga as duas tabelas; as linhas `btc_app_%` em `openiddict_applications` ficam órfãs e
  inofensivas, e o `docs/deployment.md` dá o `DELETE` para quem quiser limpá-las. Voltar para a
  frente depois de um `down` perde os segredos: as aplicações precisam ser recriadas.
- **Sem variável de ambiente nova.**

## (h) Os quadros do protótipo

*A lista abaixo é a da versão `1794257562-e4a0`, como foi apresentada. O dono corrigiu os quadros na
versão 60; ver "Decisões do dono (09/11/2026)".*

Canvas `claude.ai/artifact/H7dPBZafiSkdKd1hqesPZ6`, versão `1794257562-e4a0`, lido em 09/11/2026
pela ferramenta Artifact: `AplicacoesLista`, `AplicacaoNova`, `AplicacaoCredencial`,
`AplicacaoDetalhe` (aba `identidade`, "Credencial e conexão"), e `AplicacaoNovaFederada` só para
separar o que é da M4. Na tabela, **serve** = a #186 entrega o dado; **#187** / **M4** = de outra
entrega; **não cumpre** = o quadro promete o que o backend não consegue.

**`AplicacoesLista`**

| estado | o que o quadro mostra | #186 |
|---|---|---|
| lista | subtítulo "…Cada aplicação só alcança os agentes liberados para ela." | **#187**: verdadeiro na #186 por D2 (nenhum), mas fala de liberar |
| lista | busca "Buscar por nome ou client ID", filtro Estado Todas/Ativas/Desativadas | serve (`?busca`, `?estado`) |
| lista | colunas Aplicação (nome, descrição), Identidade (client ID + "Credencial do Buteco"), Estado (Ativa/Desativada), Último uso ("há 3 min", "há 2 h", "há 41 dias", "Nunca usada") | serve |
| lista | Identidade "Federada · SSO Empresa", client ID `erp-pedidos` | **M4** |
| lista | coluna "Agentes liberados" com chips | **#187** |
| lista | rodapé "4 aplicações · as desativadas continuam listadas porque são autoras de tasks e métricas." | **não cumpre**: nenhuma task nem métrica registra a aplicação na #186 (nenhuma chamada é atendida); a autoria é da #187 |
| — | lista vazia, busca sem resultado, carregando, erro | **sem quadro** |

**`AplicacaoNova`** (`federadoDisponivel=false`)

| estado | o que o quadro mostra | #186 |
|---|---|---|
| formulário | Nome, Descrição | serve |
| formulário | "Responsável técnico" (e-mail) | **sem consumidor**: nenhum quadro o mostra depois (Q6) |
| formulário | "O Buteco vai gerar um client ID e um segredo… aparecem uma única vez… guarda só o hash do segredo." | serve |
| formulário | "A aplicação pede o token em `https://[URL_PUBLICA]/connect/token` (client credentials)." | serve (`endpointToken`) |
| formulário | aviso "A aplicação nasce sem nenhum agente liberado: até você liberar agentes na tela dela, toda chamada A2A é recusada." | metade: "toda chamada é recusada" vale (D2); "liberar agentes na tela dela" é **#187** |
| — | **pedir a senha** antes de criar (D4) | **sem quadro** |
| — | nome vazio, nome repetido, senha errada (`401`), bloqueio (`423`) | **sem quadro** |
| `federadoDisponivel=true` | escolha Credencial do Buteco / Federada, provedor, "Client ID no provedor" | **M4** |

**`AplicacaoCredencial`**

| estado | o que o quadro mostra | #186 |
|---|---|---|
| diálogo | "Aplicação criada", Client ID, Segredo (`btc_sk_…`), Endpoint de token, "Copiar" | serve |
| diálogo | "O segredo aparece só agora… guarda apenas o hash… gere um novo na aba Credencial." | serve; a aba se chama "Credencial e conexão" no detalhe |
| diálogo | "Guardei o segredo em um local seguro" habilita "Ir para a aplicação" | tela só |
| — | o mesmo diálogo para **"Gerar novo segredo"** no detalhe (o segredo novo também aparece uma vez) | **sem quadro** |

**`AplicacaoDetalhe`, aba "Credencial e conexão" (`aba=identidade`)**

| estado | o que o quadro mostra | #186 |
|---|---|---|
| cabeçalho | nome, "Ativa", "Editar", "Desativar" | serve; **sem quadro** da edição de nome/descrição |
| abas | "Agentes" com contador, e a aba Agentes inteira | **#187** |
| credencial | Client ID, Endpoint de token, "Gerar novo segredo" | serve |
| segredos | colunas Segredo (`btc_sk_••••a91f`), Estado (Ativo, Substituído), Criado em, Válido até, Último uso, Ações (Revogar, Revogar agora) | serve, com o sufixo de 4 (D4) |
| segredos | "Ao gerar um novo segredo, o anterior continua valendo por 7 dias para a aplicação trocar sem parar." | serve |
| segredos | **"Revogar corta na hora."** | **não cumpre**: o token já emitido vale até 5 min (D5). Texto proposto: "Revogar recusa o segredo na hora. Um token já emitido continua valendo por até 5 minutos." |
| conexão | `curl` com `-d scope=a2a` | **não cumpre**: o escopo é `buteco-api` (D3) |
| conexão | `curl` com `"method":"message/send"` | **não cumpre**: o método A2A 1.0 é `SendMessage` (`docs/a2a-integration.md`, "Método `SendMessage`") |
| conexão | "O endereço de cada agente liberado também está no AgentCard público" | metade: o card é público e de todo agente; "liberado" é **#187** |
| conexão | o `curl` da A2A como passo 2 | **não funciona até a #187** (D2): a chamada recebe `403` |
| — | segredo **Expirado** e **Revogado** na lista | **sem quadro** (só Ativo e Substituído) |
| — | confirmação de "Revogar", de "Desativar" (com o prazo de 5 min) | **sem quadro** |
| — | a aplicação **desativada**: selo, "Reativar", segredos todos revogados, "Gerar novo segredo" depois de reativar | **sem quadro** |
| — | limite de 3 segredos vivos (`409`) | **sem quadro** |
| — | senha antes de "Gerar novo segredo" | **sem quadro** |

**Versão m2:** sim, a #188 precisa de uma versão `m2` de `AplicacoesLista`, `AplicacaoNova` e
`AplicacaoDetalhe`: sem a coluna Identidade federada, sem a escolha de tipo, e com os textos de
"liberar agentes" condicionados à #187 — ou a #188 entra só depois da #187, e então a `m2` é só a
retirada do federado (Q10). `AplicacaoCredencial` não muda.

## (i) Tamanho (convenção 18)

Âncoras decompostas, só código: **#184** produção +943 / −77, testes +1.813 / −28, duplos +265 / −39,
59 casos; **#219** produção +765 / −31 (15 arquivos), testes +1.365 (17 arquivos), 11 operações,
~70 linhas de produção por operação.

| item | criados | modificados | linhas (prod.) |
|---|---|---|---|
| entidades e configuração EF | 3 | 1 (`AppDbContext`) | ~150 |
| client ID e segredo (geração, formato, hash, prefixo) | 2 | — | ~100 |
| `ButecoApplicationManager` (segredos, estados, último uso) | 1 | 1 (registro) | ~140 |
| as 8 rotas e o serviço (criar com a linha do OpenIddict na mesma transação, desativar, reativar, gerar, revogar), requests e responses | 3 | 2 (`Program.cs`, `RoleRoutes`) | ~600 (8 × ~70, mais os tipos) |
| emissão (claim, 5 min, comentário corrigido) | — | 2 (`ClientCredentialsTokenHandler`, `IdentityTokenLifetimes`) | ~50 |
| mapeamento e autorização (`application`, lista vazia, boot) | — | 4 | ~70 |
| retenção no boot | — | 1 | ~15 |
| AgentCard | — | 1 | ~35 |
| **produção** | **9** | **12** | **~1.160** (faixa 1.000–1.300), ~35 % de comentário |

Testes: **~55 casos** (cadastro 8, lista 4, detalhe 3, edição 3, desativar/reativar 4, segredos 7,
emissão 10, mapeamento 4, boot 3, card 2, acordos 2, matriz 1 alterada, mais os guardas), ~35
linhas por caso como na #184 → **~1.900**; **duplos ~200** (gerador falso de client ID e de segredo,
relógio, semeador de aplicação, cliente de token de aplicação para os acordos). Total de teste
**~2.100** (faixa 1.800–2.500). Docs ~+150. Gerado (migration) fora.

**Cabe numa change**, do tamanho da #184 (~3.300 contra ~3.000 de código). Se o dono preferir
dividir, a divisão que não deixa nada meio-aberto é: **186a** — emissor (tabelas, segredos,
gerente, emissão de 5 min, mapeamento `application` com lista vazia, boot, AgentCard), com as
aplicações semeadas nos testes; **186b** — as oito rotas do Admin. A 186a sozinha em produção não
cria aplicação nenhuma. Recomendo não dividir (Q11).

## (k) Premissas falsas (convenção 9)

1. **Corpo da #186, "Qual problema isso resolve"** (l. 3): "quem integra usa o token do operador, que
   dá acesso a tudo e vence em 30 minutos". Falsa: o token de operador (HMAC, 30 min) e o
   `POST /auth/login` saíram na #217 (`649e582`, 01/11/2026); o token do painel dura 15 min
   (`IdentityTokenLifetimes.cs` l. 23) e, desde a #184 (`a00966a`, 09/11/2026), recebe `403` em
   `POST /agents/{id}/a2a` com qualquer papel. Hoje **ninguém** de fora integra. Muda só o motivo, não
   o comportamento nem o contrato da #186: **não é parada.**
2. **Corpo, "Decisões em aberto"** (l. 53): "retenção das chaves (24 h) ≥ `AccessTokenLifetime` + 1 h"
   e "uma aplicação com mais de 23 h quebraria a desigualdade". Falsa desde a #180: a retenção é de
   15 dias (`IdentityKeyRotationPolicy.cs` l. 34) e a checagem é
   `Retention ≥ max(access, refresh) + CheckInterval` (`IdentityIssuerStartupValidation.cs`
   l. 68–83). O comentário de 31/10 corrige, o corpo não. Não muda o D5.
3. **Comentário de 27/10, item 1**: "continua aceito até o `exp` (1 h hoje)". Era verdade; desde a
   #180 são 15 min, e para a aplicação serão 5 (D5).
4. **`ClientCredentialsTokenHandler.cs` l. 19**: "Nenhum cliente é cadastrado nesta change, então em
   produção ele nunca chega a rodar." Falsa desde a #182: os dois clientes de serviço emitem por
   `client_credentials`. Corrigido nesta change.
5. **`docs/a2a-integration.md` l. 70–72**: "Só três rotas de `apps/api` são anônimas: `/health`,
   `/auth/login` e o AgentCard". Falsa desde a `649e582` (#217), ver D1. Corrigido nesta change.
6. **Quadro `AplicacaoDetalhe`, aba Credencial e conexão**: `scope=a2a`, `message/send` e "Revogar
   corta na hora" (h). Para corrigir no quadro.
7. **Achada no apply — D7 deste design**: "Sem `Identity:Issuer` (emissor desligado), o card fica com o
   `bearer`". No `apps/api` o issuer é a `PublicUrl:BaseUrl` normalizada (`IdentityIssuerState`,
   `IdentityIssuerServiceCollectionExtensions.cs` l. 36–46), e sem ela o boot falha (spec viva
   `oidc-issuer`, "Url pública obrigatória", desde a #180); `Identity:Issuer` é a configuração do
   `apps/inbox` e do `apps/connectors`. Não muda comportamento alcançável nem contrato de um app no ar:
   seguiu, e o cenário inalcançável saiu da delta (D10).

A 2 desfaz também a forma antiga da checagem de retenção repetida no pedido do dono de 09/11 (decisão do
dono: registrada aqui). A 4 e a 5 são corrigidas nesta change (`tasks.md`, seções 4 e 8); a 6, no
quadro, na versão 60.

**Nenhuma muda comportamento ou contrato da #186.** A que mais pesa é a 1, e ela aumenta a urgência
sem mudar o que a change faz.

## Baseline

No worktree limpo `../buteco-agentes-186-baseline`, em `71046f6`, sem edição nenhuma (0 arquivos
alterados no início de cada suíte), pelo `~/.cache/buteco-agents/bin/suite-186.sh` (cópia do
`suite-185.sh` com o lock de uma suíte por vez; só o cabeçalho muda). `podman ps` no início de cada
suíte: `postgres_1` e `rabbitmq_1`. Saídas e o relatório por nome (`nomes.txt`, 1.592 resultados,
GUID normalizado) em `~/.cache/buteco-agents/baseline-186/`.

| suíte | resultado | duração | CPU mínima | veredito |
|---|---|---|---|---|
| `api` | 1.565 aprovados, 1 ignorado | 403 s | 58 | APROVADA (verde conta com a CPU caindo) |
| `rt-inbox` | 15 aprovados | 68 s | 100 | APROVADA |
| `rt-connectors` | 11 aprovados | 36 s | 100 | APROVADA |

**Por que estas suítes:** `api`, onde mora tudo; `rt-inbox`, que passa pelo `client_credentials` do
emissor e pela `POST /agents/{agentId}/a2a` do `service:inbox`, rota que ganha um segundo tipo de
chamador; `rt-connectors`, que passa pelo `client_credentials` e pela tabela de clientes, e recebe o
acordo do token de aplicação recusado no `apps/connectors`. **Fora:** `inbox` e `connectors` (tabela
de clientes fechada, aplicação só com `buteco-api`, código intocado), `rt-crossapp` e `workers` (sem
token nem rota), `frontend` (as telas são da #188).

## Decisões do dono (09/11/2026)

Q1 a Q15 aceitas como recomendadas, com os acréscimos que viraram a D9: os guardas do claim
`buteco_app` (Q1), os do gerente estendido, os de duração (Q5) e o da desativação (Q13). A exceção
da lista vazia de `application` é temporária: registrada na #187, com o teste que lá muda de
asserção ([comentário](https://github.com/aldovrando-oliveira/buteco-agentes/issues/187#issuecomment-6751534670)).
Q4: o `docs/a2a-integration.md` explica o pedido sem `scope` (token sem `aud`, `401`).

**O protótipo, versão 60** (`1794258468-0769`, lido em 09/11/2026 pela ferramenta Artifact, conferido
em `AplicacaoDetalhe`, `AplicacaoNova`, `AplicacaoCredencial` e `AplicacoesLista`), corrigido pelo dono
a partir da lista do (h):

- `AplicacaoDetalhe`, Credencial e conexão: os quatro estados de segredo (Revogado e Expirado sem
  ação); o rodapé "Ao gerar um novo segredo, o anterior continua valendo por 7 dias para a aplicação
  trocar sem parar. Revogar recusa o segredo na hora; um token já emitido continua valendo por até 5
  minutos. Cada aplicação tem no máximo 3 segredos vivos, e cada segredo vale 1 ano."; o `curl` com
  `scope=buteco-api` e `"method":"SendMessage"`; "A chamada só é aceita para os agentes liberados na
  aba Agentes; para os outros, a resposta é recusada." (verdadeira com a #187);
- `AplicacaoNova`: sem "Responsável técnico"; "Até 80 caracteres, sem repetir o nome de outra
  aplicação." e "Opcional, até 280 caracteres.";
- `AplicacaoCredencial`: "gere um novo na aba Credencial e conexão";
- `AplicacoesLista`: prop `entrega` (`m2` por padrão, só "Credencial do Buteco"); rodapé "N aplicações ·
  as desativadas continuam listadas e podem ser reativadas.".

**Os estados sem quadro** do (h) são de tela e ficam para o design da #188. O contrato de cada um
(status, `title`, campo) está nas specs desta change: `external-applications` (lista vazia `200 []`;
`400` com `nome`/`descricao`/`estado`; `409` de nome repetido, de aplicação desativada e do limite;
`401` "Senha inválida."; `423`; `404` de aplicação e de segredo) e `oidc-issuer` (`invalid_client`,
`invalid_request`).

## Decisões (as recomendações, aceitas)

| # | pergunta | recomendação |
|---|---|---|
| Q1 | Subject e tabela da aplicação | subject `application`, entrada na `ServiceRoutes` (subjects sem papel), reconhecido pelo claim `buteco_app` do emissor (D1) |
| Q2 | O que a aplicação alcança entre a #186 e a #187 | nenhum agente: lista vazia, `403` em toda rota; a #187 abre a A2A com o filtro no mesmo deploy (D2) |
| Q3 | Formato do client ID | `btc_app_` + 8 de 32 símbolos, gerado, imutável (D3) |
| Q4 | Pedido de token sem `scope` | emite sem `aud`, e o `apps/api` recusa com `401`; o guia manda `scope=buteco-api` (D3). Alternativa: escopo padrão `buteco-api` quando ausente |
| Q5 | "Revogar corta na hora" | duração curta no principal, **5 min**, constante; texto "…um token já emitido continua valendo por até 5 minutos" (D5) |
| Q6 | "Responsável técnico" do quadro de Nova aplicação | fora, e sair do quadro: nenhum quadro o mostra depois |
| Q7 | Senha de quem chama | criar a aplicação e gerar segredo, sim; revogar, desativar, reativar e editar, não (D4) |
| Q8 | Segredos vivos por aplicação | no máximo 3; o quarto responde `409` (D4) |
| Q9 | "Último uso" | na emissão do token, por segredo e por aplicação, no máximo uma escrita por minuto; o rótulo fica "Último uso", e o pedido de token conta como uso (D6) |
| Q10 | Versão m2 dos quadros | `m2` de `AplicacoesLista`, `AplicacaoNova` e `AplicacaoDetalhe` sem o federado, e os textos de "liberar agentes" conforme a #188 entre antes ou depois da #187 (h) |
| Q11 | Divisão | uma change só, do tamanho da #184 (i) |
| Q12 | Validade e sobreposição | 365 dias e 7 dias, constantes, não opções (convenção 2) (D4) |
| Q13 | Desativar e reativar | desativar revoga todos os segredos vivos; reativar volta sem segredo e sem senha (D3) |
| Q14 | Nome único | sim, entre ativas e desativadas, sem diferenciar maiúscula (D3) |
| Q15 | AgentCard | `oauth2` com `clientCredentials` no lugar do `bearer`, em todo card (D7). *A segunda metade da recomendação original, "`bearer` só com o emissor desligado", saiu no apply: o estado não existe (premissa 7, D10).* |
