## Context

**Issue:** #184, com os comentários das entregas da M1 (#217, #219, #181, #182, #211, #250, #183, #220). Base: `66ee635` (merge do PR #258, #220).

### O código em `66ee635`

- **`apps/api`.** Um esquema só, o `JwtBearer` (`Identity/Validation/IssuerTokenAuthentication.cs`). O principal sai da tabela de clientes (`IssuerClientSubjects`): `buteco-painel` → `operator`, `buteco-servico-inbox` → `service:inbox`, `buteco-servico-connectors` → `service:connectors`. O `sub` e o `oi_au_id` vão como estão (l. 84–112); nenhum outro claim passa. A `FallbackPolicy` exige autenticado mais o `ServiceScopeRequirement` (`Program.cs` l. 144–148), e o handler deixa o `operator` passar em **tudo** (`Auth/ServiceScopeAuthorizationHandler.cs` l. 64–68), inclusive `/sync/knowledge-bases/*` e `POST /agents/{id}/a2a`, que nenhuma tela chama. A lista dos subjects de serviço é conferida no boot contra os endpoints (`ServiceScopeRouteValidation`), num sentido só: entrada sem rota derruba; rota sem entrada, não.
- **`CurrentUser`** (`Users/CurrentUser.cs`) relê o usuário do `sub` **a cada requisição** (`FindAsync` por chave) e devolve `403` para o token cujo `sub` não é usuário ativo. Usam: `/account/*`, `/users/*`, convite, redefinição de senha e de 2 etapas, `/identity`.
- **`UserGrantTokenHandler`** relê o usuário na troca do código e em toda renovação, recusa o desativado, o de stamp diferente e o sem 2 etapas (l. 70), e na renovação troca no principal `name` e `email` pelos gravados, com destino só no id token (l. 86–91).
- **`UserPrincipalFactory`**: `sub` no access e no id token; `email` e `name` só no id token; stamp sem destino. Não há papel.
- **`users`**: `status` como texto (`UserStatusText`, `HasConversion`), sem coluna de papel. O bootstrap cria o primeiro usuário com `User.CreateBootstrap`.
- **Desativação** (`Users/Management/UserEndpoints.cs`): recusa a si mesmo (`403`), abre transação, pega o `pg_advisory_xact_lock(4779819235982394417)` (`BUTECOD1`, `UserAdministration.cs` l. 26) e relê quem chama ativo dentro do lock.
- **`apps/inbox`**: `FallbackPolicy` = autenticado + `NameIdentifier == operator` (`Auth/SubjectAuthorization.cs` l. 31–35). Tabela de clientes com uma entrada, `buteco-painel`. Sem acesso ao banco do `apps/api`.
- **`apps/connectors`**: tabela de subjects por rota (`Auth/ConnectorsSubjectAuthorizationHandler.cs` l. 42–56): `operator` em três rotas, `service:api` em uma. Sem banco.
- **O painel** pede um access token só, com `openid offline_access buteco-api buteco-inbox buteco-connectors` (`apps/frontend/src/auth/oauth.ts` l. 8): o mesmo token vai aos três apps. Ele lê do id token só `sub`, `name` e `email` (`auth/sessionStore.ts` l. 123–130); um claim a mais é ignorado.
- **Os testes**: o token padrão do painel nos testes do `apps/api` é do `buteco-painel` com `sub` `fixture-sem-usuario` (`tests/Buteco.Api.Tests/Support/TestIssuerTokens.cs`), anexado por `TestAuthentication.AttachPanelToken` em 17 arquivos dos três apps. `User.Create*` aparece em 136 sítios de teste.

## Goals / Non-Goals

**Goals:** papel no usuário e no token; matriz checada por rota nos três apps; checagem de boot nos dois sentidos; o último Admin protegido; o convite com papel; nenhum usuário existente perde acesso no deploy.

**Non-Goals:** tela (#185); o card "Aplicações com acesso" (#187, cuja rota já é decidida lá: contagem para todos, nomes só para o Admin); credencial de aplicação externa (#186); permissão fina por recurso.

## Decisions

### D1. (a) Inventário de rotas

Legenda: **A** Admin, **O** Operador, **L** Leitor. "+banco" = a rota relê o papel do usuário a cada requisição (D3). **painel** = token do `buteco-painel` (hoje, subject `operator`, que passa em tudo). A linha da matriz é a do corpo da #184; "—" é rota que a matriz não cobre, e virou pergunta (Q8), com a recomendação na coluna "depois".

**`apps/api`**

| método | caminho | quem chama hoje | depois | matriz |
|---|---|---|---|---|
| GET | `/agents/` | painel | A O L | agentes |
| GET | `/agents/{id:guid}` | painel, `service:inbox` | A O L; `service:inbox` | agentes |
| POST | `/agents/` | painel | A O | agentes |
| PUT | `/agents/{id:guid}` | painel | A O | agentes |
| POST | `/agents/{id:guid}/activate`, `/deactivate` | painel | A O | agentes |
| PUT | `/agents/{id:guid}/mcp-servers` | painel | A O | agentes |
| PUT | `/agents/{id:guid}/delegations` | painel | A O | agentes |
| PUT | `/agents/{id:guid}/knowledge-bases` | painel | A O | agentes |
| GET | `/providers` | painel (cadastro de agente) | A O L | agentes |
| POST | `/agents/{id}/a2a` | `service:inbox`; o `operator` passa, sem chamador | só `service:inbox` (Q8f) | — |
| GET | `/mcp-servers/`, `/mcp-servers/{id:guid}` | painel | A O L | MCP |
| POST | `/mcp-servers/` | painel | A O | MCP |
| PUT | `/mcp-servers/{id:guid}` | painel | A O | MCP |
| POST | `/mcp-servers/{id:guid}/activate`, `/deactivate` | painel | A O | MCP |
| GET | `/mcp-servers/{id:guid}/tools` | painel | A O L (Q8d) | — (chama o servidor com a credencial gravada) |
| POST | `/mcp-servers/{id:guid}/test` | painel | A O (Q8d) | — |
| POST | `/mcp-servers/test` | painel | A O (Q8d) | — (credencial no corpo) |
| GET | `/knowledge-bases/`, `/{id:guid}`, `/indexing-summary` | painel | A O L | conhecimento |
| POST | `/knowledge-bases/` | painel | A O | conhecimento |
| PUT | `/knowledge-bases/{id:guid}` | painel | A O | conhecimento |
| POST | `/knowledge-bases/{id:guid}/activate`, `/deactivate` | painel | A O | conhecimento |
| DELETE | `/knowledge-bases/{id:guid}` | painel | A O | conhecimento |
| GET | `/knowledge-bases/{kb}/documents/`, `/{id:guid}` | painel | A O L | conhecimento |
| GET | `/knowledge-bases/{kb}/document-events` | painel | A O L | conhecimento |
| POST | `/knowledge-bases/{kb}/documents/` | painel | A O | conhecimento |
| PUT | `/knowledge-bases/{kb}/documents/{id:guid}` | painel | A O | conhecimento |
| POST | `/knowledge-bases/{kb}/documents/{id:guid}/reindex` | painel | A O | conhecimento |
| DELETE | `/knowledge-bases/{kb}/documents/{id:guid}` | painel | A O | conhecimento |
| GET | `/knowledge-index/diagnostics` | painel | A O L | conhecimento |
| GET | `/insights/system`, `/insights/agents/{id:guid}` | painel (Insights) | A O L (Q8a) | — |
| GET, PUT, DELETE, POST | `/sync/knowledge-bases/*` (5 rotas) | `service:connectors`; o `operator` passa, sem chamador | só `service:connectors` (Q8f) | "não mudam" (corpo) |
| GET | `/identity` | painel, `CurrentUser` | A +banco | identidade |
| GET | `/users` | painel, `CurrentUser` | A +banco | usuários |
| GET | `/users/{id:guid}` | idem | A +banco | usuários |
| PUT | `/users/{id:guid}` | idem | A +banco | usuários |
| POST | `/users/{id:guid}/deactivate`, `/activate` | idem | A +banco, lock (D6) | usuários |
| POST | `/users/{id:guid}/sessions/revoke` | idem | A +banco | usuários |
| POST | `/users/{id:guid}/two-factor/reset` | idem | A +banco | usuários |
| POST | `/users/invitations` | idem | A +banco, com `papel` (D5) | usuários |
| POST | `/users/{id:guid}/invitation/renew`, `/cancel` | idem | A +banco | usuários |
| POST | `/users/{id:guid}/password-reset` | idem | A +banco (Q6) | usuários |
| **PUT** | **`/users/{id:guid}/role`** (nova) | — | A +banco, lock (D6) | usuários |
| GET | `/account` | painel, `CurrentUser` | A O L, com `papel` | (a própria pessoa, #219) |
| POST | `/account/password` | idem | A O L | idem |
| GET, POST | `/account/sessions`, `/{id}/revoke`, `/revoke-others` | idem | A O L | idem |
| GET, POST | `/account/two-factor`, `/recovery-codes` | idem | A O L | idem |

Anônimas, sem mudança: `/health`, `/auth/session` e as três de 2 etapas do login, `/auth/invitation[/accept]`, `/auth/password-reset[/complete]`, `/connect/authorize`, `/connect/logout`, o AgentCard. Os endpoints do OpenIddict (`/connect/token`, discovery, JWKS) não passam pelo roteamento.

**`apps/inbox`**

| método | caminho | quem chama hoje | depois | matriz |
|---|---|---|---|---|
| GET | `/channels/`, `/channels/{id:guid}` | painel | A O L | canais |
| POST | `/channels/` | painel | A O | canais |
| PUT | `/channels/{id:guid}` | painel | A O | canais |
| POST | `/channels/{id:guid}/activate`, `/deactivate` | painel | A O | canais |
| GET | `/channels/{channelId:guid}/sessions` | painel | A O | histórico |
| GET | `/sessions/{sessionId:guid}/messages` | painel | A O | histórico |
| GET | `/contacts/`, `/contacts/{id:guid}/sessions` | nenhum chamador no painel | A O (Q8c) | — |
| GET | `/sessions/summary`, `/messages/summary` | painel (Insights) | A O L (Q8b) | — (contagens, sem conteúdo) |

Anônimas, sem mudança: `/health`, `/webhooks/{channelId:guid}`, a de push notification.

**`apps/connectors`**

| método | caminho | quem chama hoje | depois | matriz |
|---|---|---|---|---|
| GET | `/connectors/providers` | painel | A O | conectores |
| GET | `/connectors/providers/{providerKey}/folders` | painel | A O | conectores |
| POST | `/connectors/knowledge-bases/{knowledgeBaseId:guid}/sync` | painel | A O | conectores |
| GET | `/connectors/providers/{providerKey}/folder` | `service:api` | só `service:api` | "não muda" (corpo) |

### D2. (b) A checagem de boot, nos três apps

**Recomendação: uma tabela de papéis por rota em cada app (`RoleRoutes`, `(método, RawText) → papéis`), ao lado da tabela de subjects de serviço, as duas lidas pelo mesmo handler da `FallbackPolicy`.**

- **O handler.** Subject do painel (`operator`): passa se o `role` do principal está entre os papéis da rota na `RoleRoutes`. Subject de serviço: passa se a rota está na lista dele, como hoje. Qualquer outro subject, ou rota sem entrada: `403`. O `operator` deixa de passar em tudo (`ServiceScopeAuthorizationHandler.cs` l. 64–68 sai).
  - **Rota inexistente com token válido continua `404`**: sem endpoint, o handler deixa passar o token do painel com papel válido, como hoje. Sem esse caso, o `404` viraria `403`.
- **A checagem de boot, nos dois sentidos**, sobre o host construído (a forma de `ValidateServiceScopeRoutes`):
  1. todo `RouteEndpoint` autenticado (sem `IAllowAnonymous`) está na `RoleRoutes` **ou** na lista de algum subject de serviço; senão o boot cai, com o método e o `RawText`;
  2. toda entrada das duas tabelas corresponde a um endpoint mapeado (o sentido que já existe para os serviços);
  3. nenhuma entrada da `RoleRoutes` tem lista de papéis vazia;
  4. o `apps/inbox` ganha a checagem que não tinha: hoje toda rota autenticada é do `operator` por política, sem lista.
- **Os tokens de serviço** (sem papel) seguem pelas tabelas de subjects, sem mudança: o handler olha o papel só quando o subject é o do painel (comentário da #182). Uma rota pode estar nas duas tabelas (`GET /agents/{id:guid}`: A O L e `service:inbox`).
- **O token do painel sem usuário** (o `fixture-sem-usuario` dos testes de `CurrentUser`): com `role`, passa nas rotas da `RoleRoutes` pelo papel e continua recebendo `403` nas rotas de `CurrentUser`, porque o `sub` não é usuário. **Sem `role`: `401`** (D3, Q12). Na fixture dos testes, `TestIssuerTokens.Panel` passa a emitir `role` `admin`, e os testes de negócio não mudam.
- **Molde da convenção 8, segunda forma:** além dos testes da extensão, um teste sobre a composição real (`WebApplicationFactory`) que afirma a coerência, e um que reintroduz uma rota sem entrada e vê o boot cair.

**Descartada: metadado na própria rota** (`.RequireRoles(...)` em cada `Map*`). A declaração fica junto da rota, mas a matriz se espalha por 22 arquivos de endpoint no `apps/api` e não se lê inteira num lugar só; o sentido "entrada sem rota" deixa de existir, e com ele a checagem que já pegou a barra final do `MapGroup` (01, "Tokens de serviço"). A tabela é o que se revisa contra a matriz da issue.

### D3. (c) Onde o papel mora e como chega ao token

- **Banco:** `users.role`, `text NOT NULL`, com `CHECK (role IN ('admin','operator','reader'))`, convertido por um `UserRoleText` no molde do `UserStatusText`. O convidado também tem papel (o do convite).
- **Token:** o claim **`role`** (`OpenIddictConstants.Claims.Role`), um valor só, no **access token e no id token**. Posto pelo `UserPrincipalFactory` no authorize e **trocado pelo gravado em toda renovação** no `UserGrantTokenHandler`, com os dois destinos gravados de novo (o mesmo cuidado de l. 86–91 com o nome e o e-mail). Os três apps copiam o `role` para o principal no `MapPrincipal`.
- **Nomes dos valores:** `admin`, `operator`, `reader` no banco e no claim, em inglês como os do `status`; no JSON das respostas e dos corpos, o campo `papel` com `admin`, `operador` e `leitor`, em português como o `estado` (`ativo`, `desativado`, `convidado`).

  **Correspondência dos valores** (decisão do dono, 08/11/2026; citada por `user-management`):

  | papel | banco (`users.role`) e claim `role` | JSON (`papel`) |
  |---|---|---|
  | Admin | `admin` | `admin` |
  | Operador | `operator` | `operador` |
  | Leitor | `reader` | `leitor` | O `operator` do papel e o `operator` do subject do painel são claims diferentes (`role` × `NameIdentifier`); o design registra a homonímia em comentário e não renomeia o subject, que está em 3 apps e nos acordos.

**"Papel no token" × "vale na próxima requisição".** O `apps/inbox` e o `apps/connectors` não leem o banco do `apps/api`, e a arquitetura proíbe a requisição esperar o `apps/api` (01, "Nenhuma requisição espera o `apps/api` para ser autenticada"). Neles, o papel só pode vir do token. O pior caso é o de quem guardou um access token emitido logo antes da mudança: 15 min de vida + 30 s de tolerância (`ClockSkew`).

| opção | pior caso no `apps/api` | pior caso no `apps/inbox`/`apps/connectors` | custo |
|---|---|---|---|
| consulta ao banco a cada requisição | 0 (a requisição seguinte) | **impossível** sem chamada ao `apps/api` por requisição; com o token: 15 min 30 s | uma consulta a mais por requisição no `apps/api`, que reprova os guardas que contam comandos por rota (ex.: `KnowledgeBaseIndexingSummaryTests.SummaryQuery_CostDoesNotGrowWithNumberOfBases`, `Assert.Equal(2, …)`); quebra a regra da arquitetura nos outros dois |
| revogação das sessões na mudança | 15 min 30 s (o access token não é gravado e não se revoga) | 15 min 30 s | a pessoa sai do painel a cada mudança de papel, inclusive numa promoção |
| vida curta do token (ex.: 5 min) | 5 min 30 s | 5 min 30 s | 3× as renovações; hoje a duração é uma só para todo fluxo (01, "Durações") |
| **recomendada: token + renovação + banco nas rotas de Admin** | **0 nas rotas de Admin**; 15 min 30 s nas outras | 15 min 30 s | nenhum: as rotas de Admin já leem o usuário a cada requisição (`CurrentUser`) |

**Recomendação:** papel no token, relido em toda renovação (já é o ponto que relê o usuário, comentário da #219), **e** conferido no banco nas rotas que já carregam o usuário a cada requisição (`/users/*`, convite, redefinição, `/identity`). O poder que um rebaixamento precisa tirar logo, gerir acesso, sai na requisição seguinte; o resto sai em até 15 min 30 s, o mesmo prazo já aceito na M1 para o access token depois da desativação (01, "O access token já emitido continua valendo até 15 min"). A promoção chega na renovação seguinte, em até ~14 min no painel (ele renova a 60 s do vencimento).

**Isso lê "vale na próxima requisição" como "vale na próxima requisição às rotas de Admin, e na próxima renovação nas outras".** Era decisão tomada no corpo; foi como risco e Q1, e o dono aceitou esta leitura (08/11/2026). A spec diz os dois prazos com números (`route-roles`, "Prazos da mudança de papel"), e o 01 os registra ao lado do prazo do access token depois da desativação.

**Token do painel sem `role` → `401`** (Q12), nos três apps, no `OnTokenValidated` (falha de autenticação, com `WWW-Authenticate: Bearer error="invalid_token"`). Os access tokens emitidos antes do deploy não têm papel; com `401`, o painel renova uma vez e repete (`sessionFetch`, 01 "`401` e `503`"), e a renovação já devolve o papel lido do banco. Com `403`, o painel não renova, e cada pessoa ficaria até 15 min sem acesso depois do deploy. Descartada: tratar a ausência como Leitor (concede acesso a quem não declarou papel).

### D4. (d) Migração dos usuários que já existem

- **Todo usuário existente recebe `admin`**, o do bootstrap inclusive, e também o convite pendente (`invited`). É o "Admin em poder" da M1 (#180 D4): ninguém perde acesso no deploy.
- **A migration:** `ADD COLUMN role text NOT NULL DEFAULT 'admin'` (preenche as linhas), depois `ALTER COLUMN role SET DEFAULT 'reader'`, e o `CHECK`. O default `reader` fica **só no SQL**, sem `HasDefaultValue` no modelo do EF: com default no modelo, o EF omite do `INSERT` a propriedade igual ao valor padrão do CLR, e um enum cujo primeiro membro fosse `Admin` gravaria `reader` sem erro. O default existe para a volta atrás (j).
- **O bootstrap** (`User.CreateBootstrap`) cria com `admin`. `User.Create` passa a exigir o papel, sem valor padrão: os 136 sítios de teste mudam (mecânico, D10).
- **Depois do deploy, o dono:** confere `SELECT role, count(*) FROM users GROUP BY role` (tudo `admin`); rebaixa quem não deve ser Admin por `PUT /users/{id}/role` com o access token dele (sem tela até a #185, roteiro no `docs/deployment.md`); e cancela e refaz o convite pendente que não deveria ser Admin.

### D5. O convite com papel

`POST /users/invitations` ganha `papel` (`admin`, `operador`, `leitor`), **obrigatório** (Q9, decidido). O aceite mantém o papel do convite. Descartada: papel opcional com `reader` por padrão, que deixaria um padrão de segurança vivo no contrato sem consumidor depois da #185 (convenção 25).

### D6. (f) O último Admin

- **`PUT /users/{id}/role`** (`{ "papel": "operator" }`), só do Admin, numa transação com o `pg_advisory_xact_lock` `BUTECOD1`:
  1. relê dentro do lock que quem chama é **Admin ativo** (e não só ativo);
  2. se o alvo é Admin e o papel novo não é, conta os Admins ativos **dentro do lock**; com um só, `409` "A instalação precisa de pelo menos um Admin ativo";
  3. papel igual ao atual responde `200` sem gravar (idempotente, como ativar e desativar).
- **A desativação** passa a reler, dentro do mesmo lock, que quem chama é Admin ativo. A contagem não entra nela: só um Admin desativa, ele não desativa a si mesmo, e então sobra ele. Uma contagem ali seria um guarda sem caminho que o reprove (convenção 15).
- **Rebaixar a si mesmo: permitido, se houver outro Admin ativo** (Q4). É o único caminho em que a contagem decide, e é o que a torna testável. Descartada: recusar como a desativação (`403`), que tornaria a regra do último Admin consequência, como na #219, e a contagem inútil; mas obrigaria a pedir a outro Admin um rebaixamento que a própria pessoa quer.
- **Concorrência:** A e B, únicos Admins, rebaixando um ao outro: o segundo espera o primeiro e relê quem chama já `operator` → `403`. Rebaixar e desativar se cruzam no mesmo lock.
- **Promover a Admin pede a senha de quem chama** (`CallerPassword`, Q5), pelo mesmo motivo da #181: um access token de Admin roubado não vira, sozinho, Admin permanente de uma conta que o atacante já tem. Rebaixar não pede.
- **Mudar o papel não troca o stamp nem revoga sessões** (D3): a renovação seguinte já traz o papel novo.
- **A #257 sobe de prioridade.** Hoje qualquer usuário ativo gera o link de redefinição de outro; com a #184, só o Admin. Numa instalação com um Admin e Operadores, o Admin que perde a senha não tem saída, porque os Operadores não podem ajudar. **Decidido (Q10):** a #257 entra na M2, logo depois da #184, numa change própria; fora desta change.

### D7. (e) A verificação em 2 etapas com os papéis (Q2, Q3)

**Decidido (Q2): continua obrigatória para todos.** O "obrigatória para Admin" do corpo é cumprido por inclusão, sem mudança de código. O "Opcional para Operador" do protótipo `entrega=completa` é só registro (convenção 17); o dono ajusta o protótipo antes da #185. A Q3 não se aplica. Sem divisão. Os dois lados, como foram apresentados:

- **Manter obrigatória para todos** (como a #220 deixou). A decisão "obrigatória para Admin" é cumprida por inclusão. Custo zero nesta change. A pergunta "Admin sem 2 etapas ao ser promovido" deixa de existir: ninguém obtém token sem a verificação (`UserGrantTokenHandler` l. 70, `AuthorizeEndpoint` l. 101). Contraria o "Opcional para Operador" do protótipo `entrega=completa`, que vira registro (convenção 17).
- **Abrir para Operador e Leitor.** Muda:
  - o authorize (`AuthorizeEndpoint` l. 100–101) e a renovação (`UserGrantTokenHandler` l. 70): a exigência passa a depender do papel lido do banco;
  - o login: `segundoPasso: "cadastro"` só para Admin; os outros sem a verificação recebem a sessão completa (o `204` que a #220 removeu volta, ou outro valor);
  - o cadastro e a remoção pela própria pessoa por access token, que a #220 tirou (`/account/two-factor/setup`, `/confirm`, `/remove`), voltam para quem é opcional;
  - os textos de Minha conta e Editar usuário ("Opcional para Operador", "Admin exige… no próximo acesso") e o painel do `/entrar` (#185);
  - o comando `two-factor-reset` continua valendo para qualquer usuário; muda só o texto de ajuda;
  - **promovido a Admin sem a verificação:** a renovação seguinte recusa (`invalid_grant`), a pessoa entra de novo e o login a leva ao cadastro. O aviso da promoção (#185) diz isso.
  - Tamanho: +~250 linhas de produção e +~450 de teste, e a change passaria a tocar o `apps/frontend`; nesse caso a recomendação é separar (D10).

### D8. (g) As perguntas dos comentários

- **Admin gera link de redefinição de senha para outro Admin (#181)? Recomendação: sim** (Q6). Recusar não protege: o Admin que quer entrar como outro rebaixa o alvo (D6), gera o link e promove de volta. A barreira real é a senha de quem chama (`CallerPassword`), que já existe. O mesmo vale para redefinir as 2 etapas de outro Admin. Descartada: `403` Admin→Admin, que só tira a saída de quem perdeu a senha numa instalação com dois Admins.
- **Operador edita o próprio nome e e-mail (#219)? Decidido (Q7): não entra.** O dono conferiu o protótipo: Minha conta não edita nome nem e-mail ("Para mudar o nome ou o e-mail, use Usuários."). Um `PUT /account` ficaria sem consumidor (convenção 25). Registrado na #185 que esse texto precisa mudar para quem não é Admin, porque o link leva a uma tela que responde `403`; o dono ajusta o quadro.
- **Card "Aplicações com acesso": fica na #187.** O corpo já decide que a rota que serve o card é da #187. A #184 só entrega o papel no principal, que a #187 lê.

### D9. (h) O limite entre a #184 e a #185

- **A #184 não toca o `apps/frontend`.** O papel sai no id token sem consumidor até a #185 (convenção 25), que é a próxima da fila e responde a decisão em aberto dela ("de onde o painel lê o papel") pelo id token, que o painel já lê e a renovação já atualiza. Descartada: deixar o claim para a #185, que faria backend dentro da change de tela (convenção 1).
- **Fica para a #185:** menu conforme o papel, a seção "Acesso", "Dados e papel" com o seletor, o papel no convite, a linha 2 do rodapé, o tratamento do `403`, o subtítulo "Primeiro administrador" (#183).
- **Entre o merge da #184 e o da #185:** todo usuário migrado é Admin, e o painel é o mesmo para ele. Operador e Leitor só existem se o dono os criar pela API. Se existirem, veem o menu inteiro: o Operador recebe `403` em Usuários e Identidade, o Leitor em toda ação de escrita, no histórico e no seletor de pastas, e o painel mostra o erro genérico da tela. **O efeito visível para o Admin** é o botão "Convidar" responder `400` (o `papel` obrigatório que o painel ainda não envia, Q9). **Decidido (Q9): a #184 e a #185 vão juntas para produção.** A #184 entra no `main`, mas não vai para produção sem a #185 (nota de deploy e comentário na #185).
- **A #184 não usa quadro do protótipo.** Validação visual: não se aplica.

### D10. (i) Divisão e tamanho

Unidade declarada antes: **caso = método `[Fact]`/`[Theory]`**. Âncoras medidas no `git show --numstat`, sem `openspec/` e sem `.md`:

| change | produção | teste | casos novos |
|---|---|---|---|
| #220 (`ebf7b2d`) | api +494/−216; frontend +560/−387 | api +1032/−275; rt +356/−20; frontend +677/−252 | +28 / −11 |
| #217 (`649e582`) | api +5/−83; frontend +1317/−290 | api +120/−112; frontend +2091/−313 | +3 / −5 (C#) |
| #219 (`e0fb7e6`, a mais parecida no `apps/api`) | api +765/−31 | api +1365 | +62 |

A #217 quase não toca backend e serve só para dizer que esta change, sem tela, não tem a metade frontend dela.

Projeção, com o recomendado em Q1–Q12 (2 etapas sem mudança):

| item | produção | teste (casos) | teste (linhas) | duplo novo |
|---|---|---|---|---|
| papel na entidade, `UserRoleText`, migration (sem o `Designer`) | ~110 | 3 | ~120 | — |
| claim no authorize e na renovação; `MapPrincipal` nos três apps | ~60 | 5 | ~200 | — |
| `RoleRoutes` + handler + checagem de boot, `apps/api` | ~230 | 8 | ~320 | captura de host (existe) |
| idem, `apps/inbox` | ~150 | 6 | ~220 | — |
| idem, `apps/connectors` | ~130 | 6 | ~200 | — |
| guarda da matriz (uma `[Theory]` por app sobre a tabela real × 3 papéis) | — | 3 | ~240 | token com papel: ~40 por app |
| rotas de Admin com o papel do banco (13 rotas) | ~60 | 6 | ~250 | — |
| `PUT /users/{id}/role`, lock, último Admin, senha na promoção | ~120 | 9 | ~380 | — |
| convite com papel; `papel` nas respostas | ~45 | 4 | ~150 | — |
| acordos de `tests/` (critério (a) e o papel do Admin) | — | 3 | ~120 | `IssuerUserFlow`: ~20 cada |
| modificado: 136 `User.Create*`, fixtures `Panel()` com papel, `IdentityOverviewEndpointTests`, testes de usuário que precisam de Admin, os 11 casos de A2A que chamavam `/a2a` com o token do painel (passam ao `service:inbox`, Q8f) | ~0 | 0 | ~+300/−170 | — |
| **total** | **~905** (faixa 760–1.100) + `Designer` gerado (~1.300) | **~53** | **~2.500** (faixa 2.100–3.000) | **~160** |

Comentário em produção: ~40% das linhas, pela régua da convenção 18 para mudança que registra mecanismo (o handler, a leitura do D3, o lock).

**Cabe numa change**, do tamanho da #220 sem a metade frontend. Com a Q2 decidida (obrigatória para todos), não há divisão.

### D11. Testes

- **Guarda da matriz por app:** uma `[Theory]` com um token por papel, afirmando o `403` vazio da autorização × qualquer outra resposta por rota, **contra uma matriz esperada escrita no teste**, e um caso que afirma a `RoleRoutes` real igual a ela. Reprovar contra o defeito: trocar um papel numa linha da tabela. *(Corrigido no apply, 3.3, convenção 9. Dizia "uma `[Theory]` sobre a `RoleRoutes` real": lida de lá, a expectativa mudava junto com o defeito, e os dois guardas da 3.3 passaram verdes com ele.)*
- **Checagem de boot:** rota sem entrada derruba; entrada sem rota derruba; lista vazia derruba; e a composição real. Reprovar contra: tirar uma entrada da tabela, remover a chamada do `Program.cs`.
- **`401` sem papel** nos três apps, e o painel renovando: o acordo de `tests/` emite pelo emissor real, então afirma o papel de verdade.
- **Último Admin:** auto-rebaixamento do único Admin → `409`; dois Admins rebaixando um ao outro em paralelo → sobra um (o teste de concorrência da #219 é o molde); desativação relê Admin dentro do lock.
- **Rebaixamento vale na requisição seguinte nas rotas de Admin**, com o access token antigo ainda dentro do prazo.

## Risks / Trade-offs

- **[Leitura de decisão tomada] "Papel novo vale na próxima requisição"** só vale literalmente nas rotas de Admin do `apps/api` (D3). Nas outras e nos outros dois apps, o rebaixamento leva até 15 min 30 s. → Q1; teste do D11 para o caso imediato, e a documentação diz o prazo do resto.
- **[Deploy] Todo access token vivo recebe `401` uma vez.** → o painel renova e repete; o acordo de `tests/` cobre um token sem papel renovado.
- **[Contrato] O `operator` deixa de passar em `/sync/*` e no `a2a`.** Nenhuma tela chama; quem usar o token do painel por fora perde. → Q8f, decidida, com requisito e cenário em `route-roles`. Onze casos do `apps/api` chamavam o `/a2a` com o token do painel e passam ao token do `service:inbox`, que é quem chama em produção, sem mudar asserção (tasks, topo).
- **[Segurança] Admin promovido sem a verificação**, se Q2 abrir. → D7.
- **[Saída] Último Admin que perde a senha** → #257 (D6).
- **[Volta atrás] A imagem anterior não tem papéis**: durante o recuo, todo usuário ativo volta a ter o poder de Admin (medido na 11.5: um Leitor recebe `200` em `GET /users`). → No `docs/deployment.md`, antes do recuo: listar os ativos que não são Admin (`SELECT id, email, role FROM users WHERE role <> 'admin' AND status = 'active'`), desativar pela rota de desativação quem não pode ter o acesso de Admin durante o recuo, e, depois da volta para a frente, reativar cada um, que volta com o papel que já estava gravado.
- **[Default] `DEFAULT 'reader'` no banco** deixa um padrão que só a volta atrás usa. → comentário na migration e teste que afirma que o código nunca o usa (o `INSERT` sempre leva o papel).

## Migration Plan (j)

- **A #184 entra no `main` e não vai para produção sem a #185** (decisão do dono, Q9): o `papel` obrigatório no convite deixa o "Convidar" do painel de hoje com `400`.
- **Antes:** backup do banco; conferir que `apps/api`, `apps/inbox` e `apps/connectors` sobem juntos (a tabela de papéis tem de estar nos três; um `apps/inbox` antigo continua aceitando todo `operator`, o que é mais permissivo, e não quebra).
- **Migration:** `AddUserRole`, pelo `migrator` do compose, antes do `apps/api`.
- **Conferir:** `SELECT role, count(*) FROM users GROUP BY role` → só `admin`; entrar no painel (um `401` e uma renovação no primeiro pedido, sem nova tela de login); `GET /account` com `"papel": "admin"`; um Leitor de teste recebe `403` em `GET /connectors/providers/{p}/folders` (critério (a)).
- **Voltar atrás:** antes do recuo, desativar os ativos que não são Admin e não podem ter o acesso de Admin durante ele (Risks, "Volta atrás"); reimplantar a imagem anterior **sem** rodar o `Down` (ele apaga a coluna e os papéis). A imagem anterior **ignora a coluna**: o EF só lê as colunas do modelo dela. Ela também **insere** em `users` (convite e bootstrap) sem a coluna, e é por isso que o default `reader` fica no banco: sem ele, o `INSERT` reprova pelo `NOT NULL`. Os tokens com `role` passam na imagem anterior, que descarta o claim no `MapPrincipal`. **A imagem anterior não tem papéis**: durante o recuo, todo usuário ativo volta a ter o poder de Admin, como na M1 (medido na 11.5: o token de uma Leitora recebe `200` em `GET /users`). Na volta para a frente, quem foi convidado durante o recuo é Leitor, e o dono corrige pelo `PUT /users/{id}/role`.

## (k) Premissas falsas (convenção 9)

1. **Comentário da #217:** "um caso de `tests/` ainda usa o HMAC de operador: `RoundTripTests.cs`, `OperatorHmacToken_SignedWithTheSharedKey_…`". O caso não existe mais: `grep -rn OperatorHmacToken tests apps` em `66ee635` não acha nada (saiu com a #182, `de183e2`). Não muda comportamento.
2. **Comentários da #219 e da #181:** "o HMAC de operador recebe `403`". Não há HMAC desde a #182; quem recebe `403` hoje é o token do painel cujo `sub` não é usuário (`Users/CurrentUser.cs` l. 21–27). Não muda comportamento.
3. **Corpo: "Também não mudam as rotas `/sync/knowledge-bases` do `apps/api`".** Vale para o `service:connectors`. Para o painel, hoje o `operator` passa nelas (`Auth/ServiceScopeAuthorizationHandler.cs` l. 64–68), e com a tabela de papéis deixa de passar. **Muda comportamento** (de uma rota sem chamador) → Q8f.
4. **Corpo: "Papel novo vale na próxima requisição"**, contra "Nenhuma requisição espera o `apps/api` para ser autenticada" (`01-ARQUITETURA_E_CONVENCOES.md`, "Autenticação"), e sem acesso ao banco de usuários no `apps/inbox` e no `apps/connectors`. **Muda contrato** → Q1.
5. **Matriz: Leitor "ler, sem credenciais"** sugere que alguém lê credenciais. Pela convenção 3 nenhuma resposta as devolve, para nenhum papel; a diferença do Leitor é só não escrever. Não muda comportamento.

## Baseline

`66ee635`, worktree `../buteco-agentes-184-baseline`, árvore limpa, `suite-184.sh` (cópia do `suite-220.sh`), `PAUSAR_RABBIT=0`, `buteco-agents_postgres_1` e `buteco-agents_rabbitmq_1` de pé no `podman ps` do início de cada suíte, 08/11/2026. Saídas em `~/.cache/buteco-agents/184/`; a aprovada consolidada, com o relatório por nome (`nomes.txt`, 1.983 resultados), em `baseline-aprovada/`.

| suíte | rodada | resultado | CPU mín. | duração | veredito |
|---|---|---|---|---|---|
| api | 1 | 1317/1319, 1 falha, 1 ignorado | 53 | 420 s | INCONCLUSIVA (`SummaryQuery_CostDoesNotGrowWithNumberOfBases`: 4 comandos na captura de base, contra 2) |
| api | 2 | **1318/1319**, 1 ignorado | 62 | 275 s | **APROVADA** |
| inbox | 1 | **317/317** | 100 | 78 s | **APROVADA** |
| connectors | 1 | **322/323**, 1 ignorado | 100 | 22 s | **APROVADA** |
| rt-inbox | 1 | 13/14 | 100 | 70 s | REPROVADA (válida): `MessageReceived_TriggersFullRoundTrip_…`, classe da #176 |
| rt-inbox | 2 | 13/14 | 100 | 70 s | REPROVADA (válida): `Source3_…`, a #176 |
| rt-inbox | 3 | **14/14** | 100 | 75 s | **APROVADA** |
| rt-connectors | 1 | **10/10** | 100 | 37 s | **APROVADA** |

As duas reprovações do rt-inbox foram registradas na #176 ([comentário](https://github.com/aldovrando-oliveira/buteco-agentes/issues/176#issuecomment-6730761280)). Pela regra da #181, reprovação válida da #176 nos dois worktrees não bloqueia; só no da change, para.

**Suítes da change** (`~/.cache/buteco-agents/184/change-aprovada/`), pelo `suite-184.sh`, com o postgres e o rabbitmq no `podman ps` do início de cada uma:

| suíte | rodada | resultado | CPU mín. | veredito |
|---|---|---|---|---|
| api | 1 | 1545/1547, 1 falha, 1 ignorado | 62 | **reprovou pela premissa 5** (abaixo) |
| api | 2 | **1546/1547**, 1 ignorado | 62 | **APROVADA** |
| inbox | 1 | **361/361** | 100 | **APROVADA** |
| connectors | 1 | **338/339**, 1 ignorado | 100 | **APROVADA** |
| rt-inbox | 1 | **15/15** | 100 | **APROVADA** |
| rt-connectors | 1 | **11/11** | 100 | **APROVADA** |

**A rodada 1 do api não foi inconclusiva por CPU**, embora o script a tenha marcado assim (a CPU caiu a 62): a reprovação era real e determinística. `AgentCardEndpointTests.GetExtendedAgentCard_ViaJsonRpc_RemainsUnsupported` chamava `POST /agents/{id}/a2a` com o token do painel, que a Q8f fecha (`403`); é a premissa 5 do relatório do apply (os chamadores de rota de serviço que a lista do topo não previa). O caso passou ao token do `service:inbox`, com a mesma asserção, e a rodada 2 aprovou. As outras quatro suítes da rodada 1 valem: a correção tocou só esse teste.

**Suítes da change e por quê:**
- **entram** `api`, `inbox`, `connectors` (os três apps que a change toca), `rt-inbox` e `rt-connectors` (passam pelo login do emissor, `IssuerUserFlow`, e pelas rotas que mudam de regra; o critério de aceite (a) vive no `rt-connectors`);
- **fora** `rt-crossapp` (acordo de banco entre `apps/api` e `apps/workers`; os `Authorization` que ele cita são do cadastro de MCP, não do token), `workers` (sem rota HTTP autenticada) e `frontend` (a change não toca o `apps/frontend`, D9; se passar a tocar, entra, com a variante `--maxWorkers=4` sob limite térmico).

## Decisões do dono (08/11/2026)

Q2 decidida pelo dono (obrigatória para todos), Q3 não se aplica, Q7 não entra, Q9 com deploy junto da #185, Q10 com a #257 na M2 depois da #184. As outras aceitas como recomendadas, e a Q12 com a condição do `401` sem corpo `application/problem+json`, com o caso no molde de `TokenRejection_401_HasNoProblemBody_ThePanelRenewsOnThis` nos três apps e o caso de renovação no acordo de `tests/`.

### A tabela, como foi apresentada

| # | pergunta | recomendação | alternativa descartada |
|---|---|---|---|
| Q1 | Como ler "papel novo vale na próxima requisição" | token + renovação + papel do banco nas rotas de Admin: imediato onde importa, 15 min 30 s no resto (D3) | consulta a cada requisição nos três apps (o `apps/inbox` e o `apps/connectors` teriam de chamar o `apps/api`) |
| Q2 | 2 etapas: obrigatória para todos ou só Admin | manter para todos nesta change; abrir, se o dono quiser, numa change própria (D7, D10) | abrir aqui (+~700 linhas e o painel) |
| Q3 | Promovido a Admin sem a verificação | com Q2 = todos: não acontece. Se abrir: a renovação recusa e o login leva ao cadastro | aceitar o Admin sem verificação até o próximo login |
| Q4 | Rebaixar a si mesmo | permitido com outro Admin ativo; a contagem no lock recusa o último (`409`) | `403` como na desativação |
| Q5 | Promover a Admin pede a senha de quem chama | sim (`CallerPassword`, como o convite) | sem senha |
| Q6 | Admin gera redefinição de senha (e de 2 etapas) de outro Admin | sim; recusar é contornável por rebaixar → gerar → promover | `403` Admin→Admin |
| Q7 | Operador edita o próprio nome e e-mail | sim, `PUT /account` nesta change, para todo papel (a conferir no protótipo de Minha conta) | deixar para a #185 achar |
| Q8a | `/insights/*` (`apps/api`) | A O L | só A O |
| Q8b | `/sessions/summary`, `/messages/summary` (`apps/inbox`, contagens) | A O L: são números, não conversa; o Insights do Leitor continua inteiro | tratar como histórico (Leitor perde parte do Insights) |
| Q8c | `/contacts/*` (`apps/inbox`, sem chamador no painel) | A O, como histórico (nome e telefone de quem conversa) | A O L |
| Q8d | MCP: `GET …/tools` (usa a credencial gravada), `POST …/test` e `POST /mcp-servers/test` | `tools`: A O L (é leitura); os dois `test`: A O | `tools` só A O |
| Q8f | `POST /agents/{id}/a2a` e `/sync/knowledge-bases/*`, onde o `operator` passa hoje sem chamador | só os serviços | manter A O |
| Q9 | `papel` no convite | obrigatório; o "Convidar" do painel responde `400` até a #185 se a #184 for sozinha para produção | opcional com `reader` por padrão |
| Q10 | A #257 (comando `password-reset-link`) | sobe para a M2, antes do deploy da entrega, fora desta change | continuar no backlog |
| Q11 | Valores e claim | `role` = `admin`, `operator`, `reader`; colunas e respostas com `papel` | nomes em português no claim |
| Q12 | Token do painel sem `role` | `401` (o painel renova e repete) | `403` (sem renovação, até 15 min sem acesso depois do deploy) |
