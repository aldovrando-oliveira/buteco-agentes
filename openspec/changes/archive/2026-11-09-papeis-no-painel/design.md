## Context

A #184 (change `papeis-no-token`, `a00966a`) pôs o papel no token e a tabela de papéis por rota nos três apps. Esta change é a metade de tela (convenção 1, sequenciamento: a UI vem depois do backend).

**Fontes lidas:**
- **A #185:** o corpo e os sete comentários. Entre eles estão os da #184, de 08/11/2026: deploy junto (Q9), de onde o papel vem, Minha conta para quem não é Admin, o `403`/`401`, as 2 etapas (Q2) e a tabela papel × role. Os comentários de 08/11 na #184 (da #183 e da #220) também.
- **Specs vivas:** `route-roles`, `user-management`, `user-access-links`, `user-account` e `issuer-user-login` ("O papel no access token e no id token"); e, das telas, `frontend-app-shell`, `panel-users-page`, `panel-user-detail-page`, `panel-account-page` e `panel-issuer-session`.
- **O design da #184**, em `openspec/changes/archive/2026-11-09-papeis-no-token/design.md`: D3, D4, D9, Risks e Migration Plan. E o `docs/deployment.md`, "Papéis Admin, Operador e Leitor (#184)", l. 333–434.
- **O protótipo:** canvas `claude.ai/artifact/H7dPBZafiSkdKd1hqesPZ6`, lido em 09/11/2026 pela ferramenta Artifact duas vezes:
  - na versão `1794204605-0f5d` (a "versão 56"), para o primeiro design: `canvas.json`, `Usuarios`, `UsuarioDetalhe`, `MinhaConta`, `Main`, `AgenteDetalhe` e `LoginRecusado`;
  - na **versão `1794205908-2666`** (a "versão 57"), redesenhada pelo dono com o item (f): `canvas.json`, `Usuarios`, `UsuarioDetalhe`, `MinhaConta` e o quadro novo `AcessoNegado`. **É a referência dos textos**, com `entrega=m2`. Os quadros abrem em `m1`, pela homologação da M1.
- **O código em `85c1b35`** (`origin/main`, merge do PR #260).

### O código em `85c1b35`

**Painel**

- **Sessão** (`src/auth/sessionStore.ts`):
  - o registro guarda o `idToken` cru, mais `nome` e `email` decodificados (l. 13–22);
  - na renovação, o `id_token` novo substitui o anterior (`sessionFromTokenResponse`, l. 120–135);
  - o `useSession` relê o registro quando ele muda nesta aba ou noutra (`session.ts` l. 29–32). O `currentUserId` já lê o `sub` do id token na hora (l. 41–45).
- **Renovação** (`src/auth/renewal.ts`):
  - renova a 60 s do vencimento (`RENEW_MARGIN_MS`, l. 20);
  - o `renew(failedAccessToken)` reaproveita a renovação de outra aba (l. 95–101).
- **`sessionFetch`** (`src/auth/sessionFetch.ts` l. 57–83):
  - trata só o `401`: renova uma vez e repete uma vez. O `401` com `problem+json` é recusa da rota e volta sem renovar (l. 50–53);
  - o `403` volta direto à tela.
- **Retentativa** (`src/auth/transient.ts` l. 29–34): o `queryRetry` repete 3 vezes qualquer erro, `403` inclusive. São ~7 s em "Carregando…" antes do erro. Só o detalhe de Editar usuário corta o `404` (`features/users/detailRetry.ts`).
- **Menu** (`src/components/layout/AppShell.tsx`):
  - `navItems` (l. 32–48) e `accessItems` (l. 53–56), fixos, sem filtro;
  - rodapé com o `nome` e o `email` do id token (l. 164–181).
- **Usuários:**
  - `UserSummary` sem `papel` (`features/users/api/usersApi.ts` l. 79–89);
  - `InvitationRequest` sem `papel` (l. 101–105), e por isso o "Gerar convite" de hoje recebe `400`;
  - nenhuma chamada a `PUT /users/{id}/role`;
  - o cartão Dados (`components/UserDataCard.tsx`) só tem nome e e-mail;
  - o subtítulo "Primeiro administrador, criado na instalação" sai de `convidadoPor === null` (`userDetailTexts.ts` l. 14–16).
- **Minha conta:** "Para mudar o nome ou o e-mail, use Usuários." para todo papel (`features/account/components/AccountDataCard.tsx` l. 36–42).
- **Telas de domínio:** o inventário está em (b). Nenhuma cópia do `request<T>` tem ramo de `403`, e cada página mostra o seu erro genérico de carga ou de ação. O único ramo de `403` é o do `apps/connectors`: "O serviço de conectores recusou esta consulta para o operador. É configuração da instalação." (`features/knowledge-bases/utils/connectorErrors.ts` l. 168–169). Desde a #184, esse texto é falso (j).

**Backend**

- **O `403` da tabela de papéis:** corpo vazio, nos três apps (`route-roles`, "Guarda da matriz…").
- **O `403` das rotas de Admin do `apps/api`** depois de um rebaixamento: token ainda `admin`, papel gravado não. Vem de `CurrentUser.Forbidden()`, com `problem+json` e o título "Esta rota exige o login de um usuário ativo." (`apps/api/src/Buteco.Api/Users/CurrentUser.cs` l. 17 e 40–41). O texto foi escrito para o desativado e não descreve o rebaixado (g).
- **Os `403` "a si mesmo"**, também com `problem+json` e título próprio:
  - desativar: `UserEndpoints.cs` l. 133;
  - redefinir a senha: `PasswordResetEndpoints.cs` l. 65;
  - redefinir as 2 etapas: `UserTwoFactorResetEndpoint.cs` l. 52.

  Todos vêm **depois** da conferência de Admin, e nenhum grava nada.
- **`PUT /users/{id}/role`** (`Users/Management/UserRoleEndpoint.cs`):
  - `409` "Reative o usuário antes de mudar o papel." para o desativado (l. 62–65);
  - papel igual → `200`, sem gravar;
  - promover → `423` e `401` da senha de quem chama (l. 72–83);
  - `409` "A instalação precisa de pelo menos um Admin ativo." (l. 29 e 101–105);
  - `400` com `errors.papel`.

## Goals / Non-Goals

**Goals:**
- o painel só oferece o que o papel da sessão pode cumprir;
- o `403` vira tela ou aviso que diz o que se sabe;
- o Admin escolhe e muda o papel;
- a #184 e a #185 vão juntas para produção.

**Non-Goals:**
- mudar rota, resposta ou texto do backend;
- Aplicações externas (#188), o card do agente (#187), provedores federados (M3) e SSO (M4);
- mudar o papel de um convite pendente pela tela: o convite sai e entra de novo (o roteiro da #184).

## Revisão do dono (09/11/2026)

**Decididas:**
- Q1 a Q13 aceitas como recomendadas. Na Q3, a guarda de rota não substitui a recusa do servidor: ele continua sendo a autoridade, e a guarda é só para não carregar a página. A Q13 corrige o achado do canal: o Leitor abre em Configuração e não chama `GET /channels/{id}/sessions`;
- Q14 feita (versão 57);
- Q15: uma change só;
- Q16: o texto do quadro, "Alterações salvas. O novo papel chega ao painel de {nome} em até 15 minutos, quando a sessão renovar; um rebaixamento já vale na hora em Usuários e Identidade.". "15 minutos" vale para o painel: ele renova 60 s antes do vencimento, e o `403` da Q1 antecipa a renovação.

**O que a versão 57 mudou neste design:**
- **Rebaixar a si mesmo** (D4): "Salvar alterações" abre a confirmação do quadro, "Deixar de ser Admin? Usuários e Identidade saem do seu menu agora, e você vai para Minha conta. Para voltar a ser Admin, outro Admin precisa promover você.", com "Deixar de ser Admin" e "Cancelar". Ela substitui o texto que eu tinha proposto;
- **Minha conta** (D5): o cartão Dados ganha a linha "Papel" (o gravado), e o texto do celular passa a "Peça a um Admin do painel…";
- **Editar usuário, o próprio:** o texto das 2 etapas passa a "Para trocar de celular, peça a outro Admin que redefina a sua verificação.";
- **Usuários:** o texto sem escolha do Papel, "O que a pessoa pode fazer no painel. Dá para mudar depois em Editar usuário.", e o cartão "Senha e recuperação" corrigido desde a #220 (ver o achado abaixo);
- **`AcessoNegado`:** os textos que eu tinha proposto, sem mudança.

**Achado do dono, confirmado no painel:** o cartão "Senha e recuperação" de `apps/frontend/src/features/users/pages/UsersPage.tsx` l. 170–171 ainda diz "A verificação em 2 etapas é ligada por cada pessoa em Minha conta.". Isso deixou de ser verdade com a #220. Entra nesta change com o texto do `m2`: "Esqueceu a senha: um Admin gera um link de redefinição em Editar usuário. A verificação em 2 etapas é obrigatória para todos os papéis: cada pessoa cadastra o app autenticador no primeiro login." A spec viva `panel-users-page` só dizia "os textos do quadro na versão `m1`", e o quadro `m1` foi corrigido junto.

**Decisões desta revisão (convenção 17), sem pergunta nova que pare:**
- **O 403 com `title` mantém o `title` no aviso da ação.** A spec viva `panel-user-detail-page` ("Reconhecimento do próprio usuário") exige o `title` dos `403` "a si mesmo" quando o reconhecimento falha. Por isso, `ActionDenied` vale para o `403` **sem** corpo (a tabela de papéis, nos três apps), e o `403` com `title` mostra o `title`.

  O `403` do Admin rebaixado tem `title` ("Esta rota exige o login de um usuário ativo."), mas não chega a aparecer. O `sessionFetch` espera a renovação **antes** de devolver o `403`, e com o id token já `operator` a guarda de rota troca a página por `AccessDenied` no mesmo desenho.
- **O texto longo do "salvo" só quando o papel mudou.** O quadro `m2` da versão 57 usava o texto da Q16 também quando só nome e e-mail mudam, o que afirmaria um papel novo que não houve (convenção 13). O dono corrigiu o quadro na versão 58: só com dados, "Alterações salvas.".
- **O inventário de domínio fica numa spec só** (`panel-roles`, "O que cada papel vê nas telas de domínio"), em vez de um delta em cada uma das dez specs `*-ui`. Ganham delta só as duas cujo texto vivo contradiz a change:
  - `inbox-channel-catalog-ui`: "Sessões (padrão)";
  - `knowledge-base-sync-source-ui`: o texto do `403`.

  O mesmo vale para `panel-identity-page`, coberta pela guarda de rota de `panel-roles`.
- **D11. A sessão-padrão dos testes.** Com o papel desconhecido valendo Leitor (Q2), toda página renderizada sem sessão nos testes perderia os botões de escrita, e ~22 arquivos de teste de página reprovariam por arranjo. O `test/setup.ts` grava uma sessão de Admin num `beforeEach`. Os testes de "sem sessão" a limpam, e os de papel gravam a sua. **Condição do dono (09/11):** os testes de página rodam como Admin por padrão, e o comportamento de Operador e Leitor é provado **só** pelos casos que gravam o papel explicitamente. Toda spec que fala de Operador ou Leitor tem pelo menos um caso com esse papel gravado, e a 11 conta os casos por papel explícito, por spec. Alternativa descartada: semear a sessão arquivo por arquivo, porque são mais linhas e a regra fica sem um dono só.

**Registros, sem efeito nesta change:**
- **As descrições do Operador** divergiam entre os quadros; o dono as unificou na versão 58 (`Usuarios` e `UsuarioDetalhe` com "Agentes, MCP, conhecimento, canais e histórico de conversas. Não gerencia usuários e não vê Identidade."). Q17 fechada.
- **O título do `403` da redefinição das 2 etapas de si mesmo** ("Peça a outro usuário, ou use o comando de emergência no servidor.", `two-factor-totp`) ficou impreciso com os papéis: só um Admin redefine. É texto do `apps/api`, fora desta change (convenção 1). Fica para uma issue de texto, a abrir se o dono quiser.

## Decisions

### D1. (a) De onde o painel lê o papel

**Recomendação: do claim `role` do id token da sessão**, lido na hora, como o `currentUserId` já lê o `sub`. Um módulo só, `src/auth/roles.ts`, guarda:

| papel | `role` do id token | `papel` das respostas e dos corpos | rótulo |
|---|---|---|---|
| Admin | `admin` | `admin` | Admin |
| Operador | `operator` | `operador` | Operador |
| Leitor | `reader` | `leitor` | Leitor |

O módulo traz:
- o tipo `Papel` (o valor do fio em português, que é o da maioria dos usos);
- `papelDoClaim(role)` e `papelDaResposta(papel)`, que devolvem `null` para valor desconhecido;
- o rótulo;
- as permissões por papel (o que a tela de cada página consulta: `pode.gerirAcesso`, `pode.escrever`, `pode.verHistorico`, `pode.usarConectores`). Elas são escritas uma vez e testadas contra a tabela de (b).

O hook `usePapel()` sai de `useSession()` e, por isso, muda quando o registro muda.

- **Papel desconhecido ou ausente** (id token de antes do deploy, ou valor fora dos três): a tela trata como **Leitor**, o mínimo, até a renovação.
  - Isso dura um pedido: o access token de antes do deploy recebe `401` no primeiro pedido, e a renovação traz o id token novo com `role` (`route-roles`, "O token de antes do deploy renova e passa").
  - Descartada: tratar a ausência como Admin, que mostraria por um instante o que talvez não se possa.
- **Na renovação (promoção ou rebaixamento):** a renovação grava o id token novo, o `useSession` notifica, e o menu e os botões se atualizam **sem recarregar**, nesta aba e nas outras (evento `storage`). Isso acontece:
  - na renovação de rotina, a 60 s do vencimento, o que dá até ~14 min depois da mudança;
  - ou antes, pelo `403` (D3).
- **Exceção: Minha conta.** Ela mostra o papel **gravado**, do `GET /account`, que já carrega. O cartão Dados escolhe o texto pelo `papel` da resposta, e não pelo id token, porque fala do que o servidor faz agora.
- **Descartada: rota "quem sou eu" (`GET /account`) como fonte do menu.** Ela pede uma consulta antes do primeiro desenho do menu, um estado de carregamento na casca e um segundo dado que pode divergir do token que as rotas conferem. O id token é o que o emissor diz junto com o access token que as rotas leem.

### D2. (b) Inventário de tela por papel

**Regra de esconder × desabilitar:**
- **esconder** o que o papel **nunca** pode fazer;
- **desabilitar** só o que o papel pode, mas o **estado** do objeto impede. Isso já existe hoje, por exemplo "Excluir" com a base ativa, e o Admin com o desativado em Editar usuário, em D4;
- controle que **mostra estado** (a caixa marcada de um vínculo) fica visível e sem edição para o Leitor, porque escondê-lo esconderia o dado que o Leitor pode ler;
- a URL de uma página que o papel não abre mostra a tela de acesso negado (D3), sem chamar o servidor.

Coluna "linha da RoleRoutes" = a linha da tabela de `route-roles`:
- **api-L**: leituras A O L;
- **api-E**: escritas A O;
- **api-A**: `/users/*` e `/identity`, só A;
- **api-C**: `/account/*`, A O L;
- **inbox-L**: A O L;
- **inbox-E**: escritas, sessões e mensagens, A O;
- **conn**: A O.

A coluna "Leitor" vale para o Operador também, salvo quando a coluna "Operador" disser o contrário. O Admin vê tudo.

| página | ação ou elemento | chamada | linha | Operador | Leitor |
|---|---|---|---|---|---|
| menu | Inventário, Agentes, Servidores MCP, Conhecimento, Canais, Insights | — | — | vê | vê |
| menu | seção "Acesso": Usuários, Identidade | — | api-A | **esconde a seção** | **esconde a seção** |
| `/inventory` | os seis cartões | `GET /agents`, `/mcp-servers`, `/knowledge-bases`; inbox `GET /channels`, `/sessions/summary`, `/messages/summary` | api-L, inbox-L | vê | vê |
| `/insights` | página, período | `GET /insights/system`, `GET /agents` | api-L | vê | vê |
| `/agents` | lista | `GET /agents` | api-L | vê | vê |
| `/agents` | "Novo agente" | → `/agents/new` | api-E | vê | **esconde** |
| `/agents/new`, `/agents/:id/edit` | página | `POST`/`PUT /agents` | api-E | abre | **acesso negado** |
| `/agents/:id` | cabeçalho: "Editar", "Ativar", "Desativar" | `POST …/activate`, `…/deactivate` | api-E | vê | **esconde** |
| `/agents/:id` | aba Visão geral, A2A (copiar/abrir) | — | api-L | vê | vê |
| `/agents/:id` | aba Ferramentas: lista, "Ver tools" | `GET /mcp-servers`, `GET /mcp-servers/{id}/tools` | api-L | vê | vê |
| `/agents/:id` | aba Ferramentas: caixas de servidor e tool | — | — | edita | **visíveis, sem edição** (Q11) |
| `/agents/:id` | aba Ferramentas: "Salvar vínculo", barra de alterações, "Cadastrar servidor MCP" | `PUT /agents/{id}/mcp-servers` | api-E | vê | **esconde** |
| `/agents/:id` | aba Conhecimento: lista | `GET /knowledge-bases` | api-L | vê | vê |
| `/agents/:id` | aba Conhecimento: "Vincular base", "Desvincular", "Salvar bases de conhecimento" | `PUT /agents/{id}/knowledge-bases` | api-E | vê | **esconde** |
| `/agents/:id` | aba Delegação: caixas | — | — | edita | **visíveis, sem edição** (Q11) |
| `/agents/:id` | aba Delegação: "Salvar delegações" | `PUT /agents/{id}/delegations` | api-E | vê | **esconde** |
| `/agents/:id` | aba Insights | `GET /insights/agents/{id}` | api-L | vê | vê |
| `/mcp-servers` | lista | `GET /mcp-servers`, `GET /agents` | api-L | vê | vê |
| `/mcp-servers` | "Novo servidor MCP" | — | api-E | vê | **esconde** |
| `/mcp-servers/new`, `/mcp-servers/:id/edit` | página, "Testar conexão" | `POST`/`PUT /mcp-servers`, `POST /mcp-servers/test`, `POST /mcp-servers/{id}/test` | api-E | abre | **acesso negado** |
| `/mcp-servers/:id` | "Editar", "Ativar", "Desativar", **"Testar conexão"** | `…/activate`, `…/deactivate`, `POST …/{id}/test` | api-E | vê | **esconde** |
| `/mcp-servers/:id` | catálogo de tools, "Atualizar" | `GET /mcp-servers/{id}/tools` | api-L | vê | vê |
| `/knowledge-bases` | lista, resumo de indexação | `GET /knowledge-bases`, `…/indexing-summary`, `GET /agents` | api-L | vê | vê |
| `/knowledge-bases` | "Nova base", "Criar a primeira base" | — | api-E | vê | **esconde** |
| `/knowledge-bases/new` | página, origem Sincronizada, **seletor de pastas** | `POST /knowledge-bases`; conn `GET /connectors/providers`, `…/folders` | api-E, conn | abre | **acesso negado** |
| `/knowledge-bases/:id/edit` | página | `PUT /knowledge-bases/{id}` | api-E | abre | **acesso negado** |
| `/knowledge-bases/:id` | cabeçalho: "Editar", "Ativar", "Desativar", "Excluir" | `…/activate`, `…/deactivate`, `DELETE` | api-E | vê ("Excluir" desabilitado com a base ativa, como hoje) | **esconde** |
| `/knowledge-bases/:id` | cartão da origem sincronizada: estado | `GET /knowledge-bases/{id}` | api-L | vê | vê |
| `/knowledge-bases/:id` | **"Sincronizar agora"** / "Tentar sincronizar agora" | conn `POST /connectors/knowledge-bases/{id}/sync` | conn | vê | **esconde** |
| `/knowledge-bases/:id` | documentos: lista | `GET …/documents` | api-L | vê | vê |
| `/knowledge-bases/:id` | "Adicionar documento", "Atualizar" (do documento), "Excluir", **"Reindexar documento"** | `POST`/`PUT`/`DELETE …/documents`, `POST …/reindex` | api-E | vê | **esconde** |
| `/knowledge-bases/:id` | abas Histórico e Diagnóstico | `GET …/document-events`, `GET /knowledge-index/diagnostics` | api-L | vê | vê |
| `/channels` | lista | inbox `GET /channels`, `GET /agents` | inbox-L, api-L | vê | vê |
| `/channels` | "Novo canal" | — | inbox-E | vê | **esconde** |
| `/channels/new`, `/channels/:id/edit` | página | inbox `POST`/`PUT /channels` | inbox-E | abre | **acesso negado** |
| `/channels/:id` | cabeçalho: "Editar", "Ativar", "Desativar" | inbox `…/activate`, `…/deactivate` | inbox-E | vê | **esconde** |
| `/channels/:id` | **aba Sessões (histórico de conversas)** | inbox `GET /channels/{id}/sessions` | inbox-E | vê | **esconde a aba**; a página abre em Configuração (Q13) |
| `/channels/:id/sessions/:sid` | mensagens da sessão | inbox `GET /sessions/{sid}/messages` | inbox-E | vê | **acesso negado** |
| `/channels/:id` | aba Configuração, "Copiar" webhook | — | inbox-L | vê | vê |
| `/users` | página, lista | `GET /users` | api-A | **acesso negado** | **acesso negado** |
| `/users` | "Convidar usuário", "Gerar novo link", "Cancelar convite" | `POST /users/invitations`, `…/invitation/renew`, `…/invitation/cancel` | api-A | — | — |
| `/users/:id` | página e todos os cartões: Dados e papel, Formas de entrada (redefinir senha), Verificação (redefinir), Sessões (encerrar), Desativar/Reativar | `GET`/`PUT /users/{id}`, `PUT …/role`, `…/password-reset`, `…/two-factor/reset`, `…/sessions/revoke`, `…/deactivate`, `…/activate` | api-A | **acesso negado** | **acesso negado** |
| `/identity` | página | `GET /identity` | api-A | **acesso negado** | **acesso negado** |
| `/account` | página e todos os cartões | `/account/*` | api-C | vê, com o texto de D5 | vê, com o texto de D5 |
| rodapé | avatar, nome, papel, tema, Sair | — | — | vê | vê |

**Ações que a tabela não cobre (perguntas):**
- **Contatos (`/contacts/*`):** nenhuma tela chama. Fica fora sem pergunta.
- **Os resumos do Inventário para o Leitor:** `GET /sessions/summary` e `/messages/summary` são inbox-L e passam. Sem pergunta.
- **O "Desativar" da aba do agente para o Operador:** o aviso de "Desativar este agente interrompe… aplicações" do quadro `AgenteDetalhe` é da #187. Fora.
- **As caixas de vínculo do Leitor:** visíveis sem edição, ou só a lista do que está vinculado? **Q11.**
- **Aviso de "só leitura" para o Leitor:** **Q12.**

### D3. (c) O `403`

**Na carga de uma página** (a consulta principal recebe `403`):
- a página mostra o estado de **acesso negado** no lugar do conteúdo, com `role="alert"`;
- texto proposto: **"Você não tem acesso a esta página."** e, abaixo, **"Se o seu papel mudou há pouco, a mudança pode levar até 15 minutos para chegar a todo o painel."**, com o link "Ir para o Inventário";
- o texto não nomeia o papel, porque o papel do id token pode estar velho. O servidor recusou; é o que se sabe (convenção 13);
- o estado é **um componente só**, `AccessDenied`, usado pela guarda de rota e pelas páginas (Q3).

**Numa ação** (mutação ou consulta secundária recebe `403`):
- aviso no lugar onde a ação já mostra a falha: **"Você não tem permissão para esta ação."**, com a mesma frase do prazo, para o `403` sem corpo. O `403` com `title` mostra o `title` (revisão de 09/11);
- o botão não fica desabilitado: se o papel mudou, a renovação do `sessionFetch` (abaixo) já redesenha a tela sem o botão.

**A URL digitada direto** (`/users` para um Operador):
- uma guarda de rota por papel, `RequirePapel` em `routes.tsx`, desenha `AccessDenied` **sem chamar o servidor**, porque o id token diz que o papel não abre a página;
- se o id token disser que abre e o servidor recusar, a página cai no mesmo estado pela carga;
- descartada: deixar a página chamar e só reagir ao `403`. Seriam ~7 s de "Carregando…" hoje, e uma chamada que se sabe recusada.

**A consulta não repete o `403`:** o `queryRetry` corta o `403` como o `detailRetry` corta o `404`. O papel não muda entre as tentativas, e a espera dá ~7 s de "Carregando…".

**O papel que mudou no meio da sessão.** O rebaixamento já vale na requisição seguinte em `/users/*` e `/identity`, e em até 15 min 30 s no resto; a promoção vale na renovação.

- **Recomendação: o `sessionFetch` renova uma vez num `403` e NÃO repete a chamada.** A renovação traz o id token com o papel gravado, e o `useSession` redesenha menu e botões.
- **O que muda no `sessionFetch`:** depois do primeiro pedido, se o status é `403`:
  - chama `renewal.renew(tokenQueFalhou)` uma vez;
  - devolve **o mesmo** `403` à tela;
  - se a renovação disser `ended` (`invalid_grant`, o caso do desativado com access token válido), leva ao `/entrar?motivo=sessao-expirada`, como já faz no `401`;
  - se disser `unavailable`, devolve o `403` sem mais nada.
- **O `403` com corpo também renova.** O do rebaixado nas rotas de Admin tem corpo (`CurrentUser.Forbidden`), e é justamente o caso que se quer pegar. O preço é uma renovação a mais nos `403` "a si mesmo", que a tela já não oferece (o próprio não tem "Desativar" nem "Redefinir"). Distinguir pelo título seria frágil, e a #247 é quem vai trazer o campo de tipo.
- **Por que não repetir:**
  - a promoção já chegaria pela renovação de rotina antes de a tela oferecer o botão;
  - no rebaixamento, a repetição dá `403` de novo;
  - repetir uma mutação com senha (convite, redefinição, promoção) reenviaria a senha, e o `423`/`401` contaria em dobro se a ordem das conferências mudar.
- **Risco de laço:**
  - não há repetição, então não há laço de pedidos;
  - o risco que sobra é uma renovação por `403`. Uma página com dois `403` simultâneos gera duas chamadas a `renew`, e o lock e a releitura dentro dele (`renewal.ts` l. 95–101) fazem a segunda reaproveitar a primeira;
  - um `403` com o token **já renovado por um `403`** não renova de novo. O `sessionFetch` guarda o último access token que renovou por `403` e não renova duas vezes pelo mesmo motivo. Assim, um Operador que insiste em `/users` não força uma renovação por clique;
  - guarda: teste com o `403` permanente, que afirma exatamente uma renovação para duas chamadas seguidas.
- **Descartadas:**
  - renovar e repetir, como no `401`: pelo envio duplo de senha e porque não traz nada além da renovação;
  - não renovar: o menu ficaria errado até ~14 min, e um Admin rebaixado veria "Usuários" respondendo "sem acesso" sem a tela se corrigir.

### D4. (d) Usuários e Editar usuário com papel

- **O papel no convite (obrigatório):**
  - campo "Papel" no "Convidar usuário", com as três opções e a descrição do papel escolhido abaixo, como em Editar usuário (o corpo pede "a descrição de cada papel ao lado da escolha");
  - **sem valor inicial:** "Escolha o papel", e "Gerar convite" sem papel mostra "Escolha o papel." no campo, sem enviar (Q9; o quadro pré-seleciona Operador);
  - o `400` com `errors.papel` vai ao campo;
  - o painel de link gerado mostra "{e-mail} · {Papel}", como o quadro `completa`.
- **A coluna Papel:** entre "Formas de entrada" e "Verificação em 2 etapas", como no quadro `completa`, com o rótulo do papel. Valor desconhecido sai neutro, como "—" (convenção 13). A spec `panel-users-page` hoje diz "sem coluna de papel" (l. 31), e o delta troca.
- **"Dados e papel":**
  - o cartão se chama "Dados e papel", com o seletor "Papel" e a descrição do papel abaixo (textos do quadro, com o ajuste de (j) 8);
  - **um "Salvar alterações" só**, como no quadro. Com nome ou e-mail sujos, ele chama `PUT /users/{id}`; com o papel sujo, chama `PUT /users/{id}/role`. São duas chamadas em sequência, **dados primeiro**. Se a segunda falha, o cartão diz **"Nome e e-mail salvos. O papel não mudou: {motivo}."** e o papel fica sujo (Q5). Descartado: dois botões, que contrariam o quadro;
  - **promover a Admin** (papel novo Admin, alvo de outro papel; quem está na tela já é Admin, então nunca é o próprio) mostra o bloco do quadro: "Como Admin, {nome} passa a gerir usuários e a ver Identidade. Para promover, confirme com a sua senha." e o campo "Sua senha". Vale o `useCallerPassword`, no molde do convite: vazia não envia; `401` "Senha inválida." no campo; `423` no aviso amarelo, com o botão desabilitado até o `Retry-After`;
  - **o `409` do último Admin:** "A instalação precisa de pelo menos um Admin ativo." sob o seletor, como no quadro, e o papel volta ao gravado;
  - **o desativado:** seletor **desabilitado**, com "Reative o usuário antes de mudar o papel." (o texto do servidor) (Q7);
  - **sucesso:** "Alterações salvas." e, se o papel mudou, a frase do prazo de (j) 8.
- **Rebaixar a si mesmo:**
  - com outro papel escolhido no próprio Admin, "Salvar alterações" abre a confirmação do quadro (versão 57): **"Deixar de ser Admin? Usuários e Identidade saem do seu menu agora, e você vai para Minha conta. Para voltar a ser Admin, outro Admin precisa promover você."**, com "Deixar de ser Admin" e "Cancelar";
  - nenhuma rota é chamada antes de "Deixar de ser Admin";
  - no `200`: o painel **renova a sessão** (para o id token trazer o papel novo), **não relê** `GET /users` (que daria `403`) e vai a **`/account`** com o aviso "O seu papel agora é {Papel}." (Q6);
  - **divergência de implementação (convenção 9, 09/11/2026):** `renewal.renew()` sem argumento reaproveita o registro ainda válido e não busca id token novo (`renewal.ts`, a releitura dentro do lock). A renovação depois do rebaixamento passa o access token atual como "o que falhou" (`renewal.renew(sessionStore.read()?.accessToken)`), o que força o `/connect/token`. O teste "Rebaixado vai a Minha conta sem reler Usuários" conta exatamente um pedido ao emissor; **A mesma premissa vale para o `403` (D3):** a renovação do `sessionFetch` passa o access token que recebeu o `403`, e é isso que traz o id token novo. Os casos "A renovação depois do 403 traz o id token novo e o papel derivado" (`sessionFetch.test.ts`) e "O 403 renova e a casca passa a Operador sem recarregar" (`AppShell.test.tsx`) afirmam o id token trocado e o papel Operador, com o guarda "o 403 chama `renew()` sem passar o token atual", que reprova os dois;
  - o `409` do último Admin fica no seletor, como acima;
  - sem a renovação, a próxima chamada a Usuários daria `403`, e o `sessionFetch` de D3 renovaria do mesmo jeito. A ida a Minha conta evita mostrar o acesso negado a quem acabou de agir.
- **O subtítulo "Primeiro administrador, criado na instalação":**
  - sai de `convidadoPor === null`, e só o usuário do bootstrap não tem convite. Com papéis, ele pode ser rebaixado, e o subtítulo diria "administrador" ao lado de "Papel: Leitor";
  - recomendação: **"Criado na instalação"** (Q8). Descartada: decidir pelo papel, que mistura origem e papel na mesma frase.
- **Acesso:** `/users`, `/users/:id` e `/identity` passam pela guarda de rota de Admin (D3).

### D5. (e) O rodapé e Minha conta

- **Linha 2 do rodapé:** o rótulo do papel do id token ("Admin", "Operador", "Leitor"), no lugar do e-mail, como o quadro `completa` ("Admin"; o "· Google" do `AgenteDetalhe` é da M3).
  - Sem papel legível, a linha 2 fica vazia.
  - Sem nome, a linha 1 continua o e-mail.
  - O papel vem do id token, que é o que `frontend-app-shell` exige ("SHALL NOT exibir dado de identidade que não venha do id token").
  - O e-mail sai do rodapé, e continua em Minha conta.
- **Minha conta:** o cartão Dados escolhe o texto pelo `papel` do `GET /account` (o gravado, D1):
  - Admin: "Para mudar o nome ou o e-mail, use Usuários." (como hoje);
  - Operador e Leitor: **"Para mudar o nome ou o e-mail, peça a um Admin do painel."**, sem link, como o quadro `MinhaConta` com `papel` operador ou leitor;
  - `papel` desconhecido: o texto de quem não é Admin, sem o link que levaria ao `403`.
- **Descartado:** mostrar o papel em Minha conta como linha do cartão Dados. O quadro não desenha, e o rodapé já diz o papel.

### D6. (f) O protótipo

**Feito pelo dono na versão 57** (`1794205908-2666`), com a entrega `m2` em `Usuarios`, `UsuarioDetalhe` e `MinhaConta`, e o quadro novo `AcessoNegado` (`estado` `pagina`/`acao`, `papel` `operador`/`leitor`). Cada estado está no checklist de validação visual do `tasks.md` (10.3). As specs citam o quadro e o estado de cada texto.

A lista que esta seção pedia na primeira versão do design (17 linhas, por quadro e por estado) foi atendida inteira, com dois ajustes do dono:
- o rebaixamento de si mesmo com confirmação própria;
- a linha Papel em Minha conta.

As telas de domínio seguem só pela regra escrita (Q11 a Q13, aceitas), sem quadro.

### D7. (g) O que vem do backend

| a tela precisa | a #184 serve | onde |
|---|---|---|
| papel da sessão | claim `role` no id token, relido em toda renovação | `issuer-user-login`, "O papel no access token e no id token" |
| papel gravado de quem chama | `GET /account` → `papel` | `AccountEndpoints.cs` l. 29 |
| papel na lista e no detalhe | `GET /users`, `GET /users/{id}` → `papel` | `UserResponses.cs` l. 20 e 80 |
| mudar o papel | `PUT /users/{id}/role { papel, senha? }` | `UserEndpoints.cs` l. 40; `UserRoleEndpoint.cs` |
| convite com papel | `POST /users/invitations { nome, email, papel, senha }` | `InvitationEndpoints.cs` l. 38 |
| `401` que renova (token sem papel) | sem corpo, nos três apps | `route-roles` |
| `403` de papel | corpo vazio (tabela) ou `problem+json` (`CurrentUser`) | `route-roles`; `CurrentUser.cs` l. 40–41 |

**Nenhum dado falta.** Não há parada pela convenção 1. Dois registros, que não pedem backend:
1. **O título do `403` do rebaixado** nas rotas de Admin é "Esta rota exige o login de um usuário ativo.", escrito para o desativado. O painel não o mostra: o `403` tem texto próprio (D3). Se um dia outro cliente mostrar o título, é ajuste de texto no `apps/api`.
2. **O `403` de papel e o `403` "a si mesmo" não se distinguem por tipo**, só pelo título. O painel não precisa distinguir: a tela não oferece as ações vetadas a si mesmo, e a renovação a mais é inofensiva (D3). O campo de tipo é a #247.

### D8. (h) Tamanho (convenção 18)

**Âncoras decompostas** (só `apps/frontend`, sem `openspec/` nem `docs`):

| âncora | produção | teste |
|---|---|---|
| #217 `649e582` (painel entra pelo emissor) | 40 arq. +1382 −295 | 35 arq. +2091 −313 |
| #220 `ebf7b2d`, metade frontend | 17 arq. +560 −387 | 10 arq. +677 −252 |

**Projeção por componente.** É **rascunho**: a verificação fecha só com os quadros `m2` e as respostas das Q (convenção 18).

| componente | arq. prod | linhas prod | casos | duplo novo? |
|---|---|---|---|---|
| `auth/roles.ts` + `usePapel` + permissões | 2 | ~90 | ~10 | builder de id token com `role` em `test/authFakes.ts` (~40) |
| `sessionFetch`: renovar no `403`, uma vez por token | 1 | ~35 | ~6 | não (o fake de renovação existe) |
| `queryRetry` sem `403` | 1 | ~8 | ~2 | não |
| `AccessDenied` + `RequirePapel` + rotas | 3 | ~90 | ~8 | não |
| `AppShell`: menu e rodapé | 1 | ~25 | ~6 | não |
| Usuários: tipos, `changeUserRole`, convite com papel, coluna | 4 | ~110 | ~10 | `fakeUsersApi` com papel e a rota de papel (~40) |
| "Dados e papel", promoção com senha, `409`, parcial, desativado | 2 | ~180 | ~14 | não |
| rebaixar a si mesmo, subtítulo | 2 | ~50 | ~5 | não |
| Minha conta: texto por papel | 1 | ~12 | ~3 | não |
| agentes (lista, cabeçalho, 3 abas, rotas) | 6 | ~90 | ~10 | não |
| MCP (lista, detalhe, rotas) | 3 | ~35 | ~5 | não |
| conhecimento (lista, detalhe, documentos, sincronizar, texto do `403` do conectores) | 5 | ~75 | ~9 | não |
| canais (lista, detalhe sem Sessões, rotas) | 3 | ~45 | ~6 | não |
| `403` na carga e na ação das páginas de domínio (~12 páginas) | ~12 (sobrepostos às linhas acima) | ~100 | ~12 | não |
| **total** | **~35** | **~950** | **~106** | **~80** |

Teste: pela mistura da #217 (1,5 linha de teste por linha de produção), **~1.400–1.700 linhas**, em ~30 arquivos, mais os ~80 de duplo. Comentário: régua da convenção 18, ~26 linhas por razão citada. D3 e D4 carregam ~6 razões, ~150 linhas, dentro do total de produção.

**Cabe numa change?** Fica entre a #220 e a #217, mais perto da #217, que foi uma change só. **Recomendação: uma change** (Q15). Ela tem de ir à produção junta com a #184, e dividir alonga o tempo em que a #184 fica parada no `main`.

Se o dono preferir dividir:
- **185a:** papel, menu, rodapé, `403`, Usuários, Editar usuário e Minha conta. Pede os quadros `m2` e o `AcessoNegado`;
- **185b:** as páginas de domínio. Só a regra escrita.

A 185b vai junto da 185a para a produção, porque sem ela o Leitor vê os botões de escrita e recebe o `403` em cada um.

### D9. (i) Deploy da #184 com a #185

Nas notas do `docs/deployment.md`, a seção da #184 passa a ser "Papéis Admin, Operador e Leitor (#184, #185)":

- **Antes:** o backup do banco, como na #184. As imagens do `api`, do `inbox`, do `connectors` **e do `frontend`** sobem juntas. O `frontend` de antes, com a #184 no ar, tem o "Convidar" em `400`.
- **Deploy:** o `migrator` aplica a `AddUserRole`, e depois `up -d api inbox connectors frontend` (o comando já está no documento).
- **Conferir:**
  - `SELECT role, count(*) FROM users GROUP BY role` → só `admin`;
  - ao entrar no painel, o primeiro pedido de cada sessão aberta antes recebe `401` e renova sozinho;
  - o rodapé diz "Admin";
  - Usuários mostra a coluna Papel com "Admin" em todos;
  - convidar alguém como Leitor dá `201`;
  - entrar como esse Leitor: menu sem "Acesso", sem "Novo agente", e `/users` mostra "Você não tem acesso a esta página.".
- **Depois:** o dono rebaixa, em Editar usuário, quem não deve ser Admin. O roteiro por `curl` sai do caminho principal e fica só como alternativa.
- **Voltar atrás:**
  - **as quatro imagens juntas**, como na #184: antes, desativar os ativos que não são Admin; não rodar o `Down`;
  - **só o `frontend`** (a #184 fica): é seguro para os dados, porque nada se grava diferente, mas o "Convidar" volta a dar `400`, e o Operador e o Leitor voltam a ver tudo e a receber o `403` genérico. Serve como recuo curto, se o defeito for só de tela;
  - **só o backend** (o `frontend` da #185 fica): o painel lê o `role` do id token. A imagem anterior não o emite, então o papel é desconhecido, e o painel mostra a todo mundo o mínimo, o Leitor (D1). Ninguém consegue gerir usuários pela tela durante o recuo, embora o servidor deixasse. Por isso o recuo do backend leva o `frontend` junto.

### D10. Testes

- **Guarda do mapeamento:** a tabela de D1 escrita no teste, à parte do módulo (no molde do guarda da matriz da #184). Ela afirma os três pares nos dois sentidos e o `null` para valor desconhecido.
- **Guarda do inventário:** para cada linha de D2 com "esconde" ou "acesso negado", um caso por papel que afirma a **ausência** (convenção 13: a negativa é a que protege).
- **`sessionFetch`:**
  - um `403` → uma renovação e o mesmo `403` devolvido, **sem** segundo `fetch` da rota;
  - dois `403` com o mesmo token → uma renovação só;
  - `403` com `ended` → `/entrar?motivo=sessao-expirada`.
- **Promoção:** a renovação que traz `role` novo redesenha o menu sem recarregar.
- **Os guardas só contam depois de reprovar contra o defeito** (convenção 15), cada um rodado contra a versão sem a regra, com a reprovação registrada.

### D12. O que o apply mostrou (09/11/2026)

- **Os duplos de teste não respondiam ao emissor.** O `fakeAccountApi` (usado por Minha conta e Usuários) fazia `JSON.parse` do corpo de todo pedido. O `/connect/token` é `x-www-form-urlencoded`, e a renovação de um `403` lançava dentro do duplo e repetia por 25 s. O duplo passou a aceitar corpo que não é JSON e a responder o emissor por padrão. Isso é duplo, não produção.
- **O 403 nas ações das telas de domínio** passa por um `utils/notifyFailure.ts`: toda notificação vermelha das features usa o texto da tela, salvo o `403` sem corpo, que vira o aviso do quadro `AcessoNegado`. Um guarda estático (`app/forbiddenPages.test.tsx`) reprova se uma notificação vermelha escapar dele.
- **A primeira rodada com a sessão-padrão (`185-r0`) foi reprovada por arranjo, e corrigida:** os 3 casos "sem sessão" passaram a limpá-la; a rodada seguinte (`185-r0b`) aprovou 2351/2351.
- **O cartão do link do convite** mostra "{e-mail} · {Papel}" também no novo link (quadro `Usuarios`, `link-renovado`), e a spec `panel-users-page` ganhou essa frase.

## Risks / Trade-offs

- **[Papel velho no id token]** Até ~14 min, a tela pode oferecer ou esconder o que o servidor já mudou. → O `403` renova (D3); a frase do prazo na tela; o servidor é quem decide.
- **[Uma renovação por `403`]** Um Operador que insiste numa rota de Admin. → Uma renovação por access token (D3), com teste.
- **[Gravação parcial em "Dados e papel"]** Os dados salvam e o papel não. → Texto próprio e o papel continua sujo (D4, Q5).
- **[Recuo só do backend]** O painel mostra a todos o mínimo. → As notas mandam recuar o `frontend` junto (D9).
- **[Protótipo]** Os quadros `m2` e `AcessoNegado` existem desde a versão 57 (D6). A validação visual fica para a homologação, pelo checklist do `tasks.md` (10.3), sem prender o PR.

## Migration Plan

D9. Sem migration nova. A da #184 vai no mesmo deploy.

## (j) Premissas falsas (convenção 9)

1. **Corpo da #185:** "aviso de verificação em 2 etapas ao promover a Admin". A verificação é obrigatória para todo papel (Q2 da #184; o comentário da #220 de 08/11 na #184). O aviso não teria objeto, e o quadro já não o tem: o bloco de promoção pede só a senha. **Muda texto de tela, não contrato.** Segue sem o aviso.
2. **Corpo da #185, "Telas do protótipo":** "Estado de acesso negado (403)". O quadro não existe na versão `1794204605-0f5d` (`canvas.json`, 17 quadros, nenhum de `403`). Não muda comportamento. Vai para (f).
3. **Corpo da #185:** "Usuários e **Aplicações externas** só aparecem no menu para Admin". "Aplicações externas" não existe no painel. É a #188, que acrescenta o item já com a regra de Admin. Não muda comportamento.
4. **`apps/frontend/src/components/layout/AppShell.tsx` l. 50–51:** "'Aplicações externas' só chega com a M2 (#184, #185)". É da #188. Comentário, corrigido nesta change.
5. **`AppShell.tsx` l. 136–137 e `openspec/specs/frontend-app-shell/spec.md` l. 86–87:** "a #184 troca a segunda linha pelo papel". A #184 não tocou o `apps/frontend` (D9 dela). Quem troca é esta change. Comentário e spec, corrigidos pelo delta.
6. **`openspec/specs/panel-users-page/spec.md` l. 31:** "sem coluna de papel". Era verdade na M1; o delta troca (D4).
7. **`openspec/specs/user-management/spec.md` l. 5–6 (Purpose):** "Na M1, sem papéis, todo usuário real chama todas; a #184 restringe ao Admin". Os requisitos já dizem só Admin; o Purpose ficou velho. Texto de spec, não comportamento. Fica registrado, porque esta change não tem delta nessa capability.
8. **Protótipo, `UsuarioDetalhe`, `salvoTexto` do `completa`:** "O novo papel vale na hora em Usuários e Identidade, e em até 15 minutos no resto do painel."
   - Na **promoção**, isso é falso: o token do promovido ainda é `operator`, a tabela recusa `/users` até a renovação (`route-roles`, "A promoção chega na renovação"), e Usuários não vale "na hora";
   - "15 minutos" é 15 min 30 s.
   - **Muda texto de tela** (convenção 13/17). Texto proposto: **"Alterações salvas. Um rebaixamento vale na hora em Usuários e Identidade; o resto do painel de {nome} muda em até 15 minutos, quando a sessão dele renovar."** A recusa ao quadro fica registrada aqui (convenção 17).
   - No mesmo quadro, as descrições dos papéis citam "aplicações externas" (Admin e Operador), que só chegam com a #188. Na `m2`: "Tudo, inclusive usuários e Identidade." e "Agentes, MCP, conhecimento, canais e histórico de conversas. Não gerencia usuários e não vê Identidade.".
9. **Protótipo, `Usuarios`, `cardSenha` do `completa`:** "Verificação em 2 etapas obrigatória para Admin". Contraria a Q2 da #184 (obrigatória para todos). A `m2` usa o texto do `m1`.
10. **`apps/frontend/src/features/knowledge-bases/utils/connectorErrors.ts` l. 168–169:** "O serviço de conectores recusou esta consulta para o operador. É configuração da instalação." Desde a #184, o `403` do `apps/connectors` é o papel (A O), e não configuração. **Muda comportamento de tela** (texto), dentro do escopo desta change: passa ao texto de ação recusada de D3.

**Depois da versão 57:** a 8 e a 9 estão corrigidas no protótipo, e a 8 vale com o texto da Q16. As premissas 4, 5, 6 e 10 se corrigem nas tarefas 4.1, 5.2 (com o delta), 8.3 e 9.2; a 7 se corrige na 9.1. A **11**, do dono, é o cartão "Senha e recuperação" de `UsersPage.tsx` l. 170–171, que ficou velho desde a #220 (ver "Revisão do dono"); corrige-se na 5.4.

Nenhuma premissa muda **contrato** nem comportamento de backend. A 8 e a 10 mudam texto de tela, que é o que esta change entrega. **Não há parada.**

## Baseline

`suite-185.sh` (cópia do `suite-257.sh`, com o lock; diferença: o cabeçalho e o `MAXWORKERS`).

| suíte | entra? | por quê |
|---|---|---|
| `frontend` | **sim** | é o único app que a change toca |
| `api` | não | nenhuma rota, resposta nem texto do backend muda (g) |
| `inbox`, `connectors` | não | código intocado; a matriz de papéis deles foi medida na #184 |
| `rt-inbox`, `rt-connectors` | não | acordos do login e da matriz no backend; o painel não entra neles |
| `rt-crossapp`, `workers` | não | sem usuário nem rota |

**Rodada de 09/11/2026**:
- worktree `../buteco-agentes-185-baseline`, em `85c1b35`, com a árvore limpa (0 arquivos alterados);
- RabbitMQ e Postgres de pé no `podman ps` do início, `PAUSAR_RABBIT=0`, Vitest com os workers padrão;
- saída em `~/.cache/buteco-agents/185-baseline/`.

| suíte | resultado | arquivos | casos | duração | CPU mínima |
|---|---|---|---|---|---|
| `frontend` | **APROVADA** (exit 0) | 185/185 | 2351/2351 | 151 s | 62 (31 amostras de 5 s; 100 no início, 76 no fim) |

**Relatório por nome:** `frontend-nomes.txt`, com 2351 nomes completos, 2347 distintos. Quatro nomes se repetem entre arquivos, então a comparação final é por arquivo + nome.

## Open Questions

Q1 a Q16 foram decididas pelo dono em 09/11/2026 (ver "Revisão do dono"). Fica uma, que não bloqueia:

| # | pergunta | recomendação |
|---|---|---|
| Q17 | a descrição do Operador diverge entre os quadros: `Usuarios` diz "…Não gerencia usuários." e `UsuarioDetalhe` diz "…Não gerencia usuários e não vê Identidade." | unificar no texto do `UsuarioDetalhe`, que é o mais completo e verdadeiro nos dois lugares. Até a decisão, as specs seguem cada quadro como desenhado |
