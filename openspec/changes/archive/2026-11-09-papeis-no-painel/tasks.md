Um guarda marcado **(guarda)** só conta depois de ter reprovado contra o defeito que ele existe para pegar (convenção 15). Para cada um:

1. o `~/.cache/buteco-agents/bin/guarda-185.sh` reintroduz o defeito nomeado. A mutação toca só o trecho nomeado, e nunca restaura um diretório ao `HEAD`;
2. o teste roda e reprova;
3. a árvore volta pela cópia feita antes da mutação, com o sha conferido.

A tarefa registra:
- o nome do teste;
- a mensagem da reprovação;
- o "voltou: <arquivo>", com o sha igual ao de antes.

Uma mutação que quebra a compilação, ou que reprova por "no tests", não vale: é descartada e refeita. As saídas ficam em `~/.cache/buteco-agents/185-guardas/<tarefa>`.

**As suítes** rodam pelo `~/.cache/buteco-agents/bin/suite-185.sh`, **uma por vez, com o lock** (`suites.lock`; o guarda usa o mesmo).
- Só a `frontend`, com o RabbitMQ de pé (`PAUSAR_RABBIT=0`).
- Sob limite térmico, vale a variante `MAXWORKERS=4`, registrada no `-estado.txt`.
- Vale a regra assimétrica: uma rodada verde conta mesmo com a CPU caindo, e uma reprovação só conta com a CPU em 100 do início ao fim; senão é inconclusiva e se repete.
- Antes de cada rodada, o load de 1 e de 5 min fica abaixo de 6 (o script espera).

**A baseline** está em `~/.cache/buteco-agents/185-baseline/`, em `85c1b35`, com a árvore limpa:
- `APROVADA`: 2351 de 2351 casos em 185 arquivos, em 151 s, com a CPU mínima em 62;
- o relatório por nome está em `frontend-nomes.txt` (2351 nomes, 2347 distintos). A comparação final é por arquivo + nome.

**Parada** (convenção 9):
- **parar** quando uma premissa falsa mudar comportamento ou contrato, ou quando a correção reduzir cobertura;
- **seguir e registrar** quando a correção só acrescentar teste ou cenário para pôr um guarda no lugar certo, ou só tocar comentário ou documentação.

**Os textos** são os do protótipo `H7dPBZafiSkdKd1hqesPZ6`, na versão `1794205908-2666` (a "versão 57"), com `entrega=m2`. Texto que não couber no contrato para a tarefa e vai ao dono.

**Os testes de página rodam como Admin por padrão** (D11, condição do dono): o `test/setup.ts` grava uma sessão de Admin antes de cada caso. **O comportamento de Operador e Leitor é provado só pelos casos que gravam o papel explicitamente** (`signInAsRole`). Toda spec que fala de Operador ou Leitor tem pelo menos um caso com esse papel gravado; a 11.4 os conta.

## Casos que saem ou mudam de asserção

Nenhum caso existente é removido nem afrouxado. Estes mudam de asserção, porque o requisito muda nesta change, e cada um é registrado na tarefa que o muda:

| arquivo | caso | o que muda | requisito |
|---|---|---|---|
| `components/layout/AppShell.test.tsx` | "nome com duas palavras: avatar com as duas iniciais, o nome e, abaixo, o e-mail" | a segunda linha passa do e-mail ao papel | `frontend-app-shell`, rodapé |
| `components/layout/AppShell.test.tsx` | "sem nome no id token, o e-mail vai na primeira linha e a segunda fica vazia" | a segunda linha passa a ser o papel | idem |
| `components/layout/AppShell.test.tsx` | "nome de uma palavra: avatar com uma inicial" | só se a asserção citar o e-mail na segunda linha | idem |
| `features/users/userDetailTexts.test.ts` | o subtítulo "Primeiro administrador, criado na instalação · último acesso hoje, 14:14" | passa a "Criado na instalação · …" | `panel-user-detail-page`, cabeçalho |
| `features/users/pages/UserDetailPage.test.tsx` | o caso do próprio com "Para trocar de celular, peça a outro usuário…" | passa a "peça a outro Admin" | `panel-user-detail-page`, próprio |
| `features/account/components/TwoFactorCard.test.tsx` | o caso com "Trocou de celular… Peça a outro usuário do painel…" | passa a "Peça a um Admin do painel" | `panel-account-page`, verificação |
| `features/users/pages/UsersPage.test.tsx` | "título, subtítulo e os dois cartões de texto" | o texto de "Senha e recuperação" do `m2` | `panel-users-page`, página |
| `features/users/components/InviteForm.test.tsx` | "convite criado" | o corpo leva `papel` | `panel-users-page`, convidar |
| `features/users/components/UserDataCard.test.tsx` | os casos que leem o título "Dados" | passa a "Dados e papel" | `panel-user-detail-page`, cartão |
| `features/knowledge-bases/utils/connectorErrors.test.ts` | a linha `['403', connectorsError(403), /recusou esta consulta para o operador/]` e "401, 403 e código desconhecido reaproveitam as linhas da #106" | o `403` passa ao texto de ação recusada | `knowledge-base-sync-source-ui` |

**Mudam de arranjo, sem mudar a asserção:**
- os outros casos de `InviteForm.test.tsx` passam a escolher o papel antes de "Gerar convite";
- os casos que testam "sem sessão" passam a limpar a sessão-padrão do setup (0.2).

A lista é **projetada**: a rodada 0.3 a confirma por nome contra a baseline, e o que aparecer a mais entra aqui antes de seguir.

## 0. Preparação

- [x] 0.1 (scripts locais) `~/.cache/buteco-agents/bin/guarda-185.sh`: o cabeçalho e o lock do `guarda-257.sh`, mais o ramo `frontend` do `guarda-183.sh` (`npx vitest run <arquivos>`). Conferir com:
  - uma mutação nula, que o script recusa com "defeito não aplicado";
  - uma mutação real num arquivo de `apps/frontend/src`, que volta com o sha igual;
  - uma segunda chamada com o lock tomado por uma suíte, que é recusada com 75.
- [x] 0.2 (apps/frontend) **A sessão-padrão dos testes** (design, D11): `test/setup.ts` grava, num `beforeEach`, uma sessão de Admin, com `role` `admin` no id token e o access token vencendo bem depois do teste. Sem ela, toda página renderizada sem sessão seria Leitor (D1), e os casos de escrita das telas de domínio perderiam os botões. `test/authFakes.ts`:
  - `fakeJwt` continua igual;
  - `session()` e `tokenResponse()` ganham `role: 'admin'` no id token, com o parâmetro para trocar o papel;
  - o helper `signInAsRole(papel | null)` grava a sessão de outro papel, ou nenhuma.

  Os testes que precisam de "sem sessão" (`ProtectedRoute.test.tsx`, `router.test.tsx`, os de `features/auth/`) chamam o helper com `null`.
- [x] 0.3 (suíte) Rodada da `frontend` com só a 0.2 aplicada. Comparada por nome com `frontend-nomes.txt`: os 2351 continuam, todos aprovados. Um caso que reprova aqui é arranjo a corrigir na 0.2, e a lista do topo é revista.

## 1. O papel (apps/frontend, D1)

- [x] 1.1 `src/auth/roles.ts`:
  - o tipo `Papel` (`'admin' | 'operador' | 'leitor'`);
  - `papelDoClaim(role)` e `papelDaResposta(papel)`, que devolvem `null` fora da tabela;
  - `papelParaCorpo(p)` e `rotuloDoPapel(p)`;
  - `permissoes(papel | null)`, com `gerirAcesso`, `escrever`, `verHistorico` e `usarConectores`. O `null` dá as do Leitor.

  `roles.test.ts` escreve a tabela de `panel-roles` à parte do módulo e afirma os três pares nos dois sentidos, o rótulo, o `null` para `superuser`, para o `operator` lido como resposta e para não-string, e as permissões de cada papel e do `null`.
  - **(guarda)** Defeito: `papelDaResposta` aceita também o valor do claim (`operator` → Operador). Reprova "Valor desconhecido".
  - **(guarda)** Defeito: `permissoes(null)` devolve as do Admin (**o papel ausente tratado como Admin**). Reprova "o papel desconhecido tem as permissões do Leitor".
- [x] 1.2 `src/auth/session.ts`: `papelDaSessao(session)`, que lê o `role` do id token pelo `decodeJwtPayload` sem campo novo no `Session`, e `usePapel()`, sobre o `useSession`. `currentUserId.test.ts`, ou um `sessionRole.test.ts` novo, cobre:
  - o papel lido;
  - sem `role` → `null`;
  - id token ilegível → `null`;
  - o registro mudado por outra aba muda o `usePapel` (evento `storage`).
  - **(guarda)** Defeito: `papelDaSessao` com `?? 'admin'` quando o claim falta. Reprova "Id token sem role" (`panel-roles`).

## 2. O 403 no `sessionFetch` e na retentativa (apps/frontend, D3)

- [x] 2.1 `src/auth/sessionFetch.ts`. No `403`:
  - `renewal.renew(tokenQueFalhou)` uma vez e devolve o mesmo `403`;
  - `ended` → `/entrar?motivo=sessao-expirada`;
  - `unavailable` → o `403`, sem mais nada;
  - guarda o último access token que renovou por `403`, para não renovar de novo pelo mesmo token.

  `sessionFetch.test.ts`:
  - um `403` → uma renovação, um pedido à rota, o `403` devolvido;
  - dois `403` seguidos com o token novo → nenhuma renovação a mais;
  - o corpo com `senha` sai uma vez só;
  - `invalid_grant` → o registro apagado e o `/entrar`;
  - `unavailable` → o `403`, sem `/entrar`;
  - o `401` continua como antes (os casos existentes, sem mudança).
  - **(guarda)** Defeito: sem a marca do token, **o 403 renova duas vezes**. Reprova "Dois 403 seguidos, uma renovação".
  - **(guarda)** Defeito: depois da renovação, **o 403 repete a chamada** (`runtime.fetch` de novo com o token novo). Reprova "Um 403, uma renovação, nenhuma repetição" e "A senha não é reenviada".
- [x] 2.2 `src/auth/transient.ts`: o `queryRetry(failureCount, error)` não repete o `403`. `app/queryClient.test.ts`: consulta com `403` faz um pedido só; o `503` e os outros erros continuam como antes.
  - **(guarda)** Defeito: sem o corte do `403`. Reprova "Consulta com 403 não repete".
- [x] 2.3 A renovação que traz `role` novo redesenha a casca sem recarregar: um teste em `components/layout/AppShell.test.tsx` grava uma sessão de Admin, simula o `403` com renovação para `operator` e afirma a seção "Acesso" sumindo.

## 3. Guarda de rota, acesso negado e o 403 nas telas (apps/frontend, D3)

- [x] 3.1 `components/feedback/AccessDenied.tsx`, com os textos do quadro `AcessoNegado` (`estado=pagina`), e `components/feedback/ActionDenied.tsx` (`estado=acao`). Testes: os textos, o link para `/inventory`, `role="alert"` e a **negativa** de que nenhum dos dois contém "Admin", "Operador" ou "Leitor".
- [x] 3.2 `app/RequirePapel.tsx` e `app/routes.tsx`: a tabela de rotas de `panel-roles` ("Guarda de rota por papel"), escrita uma vez. `router.test.tsx`, com um `it.each` de rota × papel sobre a tabela escrita à parte, afirma o acesso negado **e** a ausência de chamada à rota da página, e a página carregada para o papel que entra.
  - **(guarda)** Defeito: `/users` com a lista de papéis `['admin', 'operador']` (**a guarda que deixa passar o Operador em `/users`**). Reprova "Operador em /users".
  - **(guarda)** Defeito: `/channels/:id/sessions/:sessionId` fora da tabela. Reprova "Leitor numa sessão de canal".
- [x] 3.3 O `403` na carga: em cada página com consulta principal (os detalhes e as listas de agentes, MCP, conhecimento, canais, Usuários, Editar usuário, Identidade), o `403` mostra `AccessDenied` no lugar do erro de carga. Um utilitário `is403(error)` em `utils/failureNotice.ts`, ao lado do `isApiFailure`. Um caso por feature com `403` sem corpo.
- [x] 3.4 O `403` na ação: as mutações das telas de domínio mostram `ActionDenied` no lugar do aviso genérico. O `403` com `title` mantém o `title` (Editar usuário). Um caso por feature, com o "Desativar" do agente como caso do quadro.

## 4. A casca (apps/frontend, `frontend-app-shell`)

- [x] 4.1 `components/layout/AppShell.tsx`: a seção "Acesso" só para `permissoes.gerirAcesso`; a segunda linha do rodapé com o `rotuloDoPapel`, vazia com papel desconhecido. Os comentários das l. 50–51 e 136–137 são corrigidos (premissas 4 e 5): "Aplicações externas" chega com a #188, e quem troca a segunda linha é a #185. `AppShell.test.tsx`: os casos do topo que mudam, mais:
  - "Operador e Leitor sem a seção";
  - "O papel na segunda linha", com a negativa do e-mail;
  - "Papel desconhecido".

## 5. Usuários (apps/frontend, `panel-users-page`)

- [x] 5.1 `features/users/api/usersApi.ts`:
  - `papel: string` no `UserSummary`;
  - `papel` no `InvitationRequest`;
  - `changeUserRole(id, { papel, senha? })` → `PUT /users/{id}/role`, com o `UserDetail`.

  `usersApi.test.ts`: o método, o caminho e o corpo (sem `senha` no rebaixamento). `test/fakeUsersApi.ts` ganha o `papel` nos usuários e a rota de papel.
- [x] 5.2 `components/UsersTable.tsx`: a coluna Papel, entre "Formas de entrada" e "Verificação em 2 etapas"; "—" para o desconhecido. Testes: "O papel do convidado", "Papel desconhecido sai neutro" (negativa dos três rótulos), e o caso existente "usuário ativo com a verificação ligada" com o "Operador".
- [x] 5.3 `components/InviteForm.tsx`:
  - o Papel sem valor inicial;
  - as descrições do quadro `Usuarios` (`m2`) e o texto sem escolha;
  - "Escolha o papel." sem enviar;
  - o `errors.papel` no campo;
  - o `403` com `ActionDenied`.

  O painel do link mostra "{e-mail} · {Papel}". Os casos do topo, mais "Sem papel escolhido" e "A descrição acompanha a escolha".
  - **(guarda)** Defeito: o envio sem papel não bloqueado (o `POST` sai com `papel: ''`). Reprova "Sem papel escolhido".
- [x] 5.4 `pages/UsersPage.tsx`: o texto do cartão "Senha e recuperação" do `m2` (o achado da #220: o de hoje ainda diz "ligada por cada pessoa em Minha conta"), e o `403` da lista com `AccessDenied`. O caso "título, subtítulo e os dois cartões de texto" com o texto novo e a negativa de "ligada por cada pessoa".

## 6. Editar usuário (apps/frontend, `panel-user-detail-page`)

- [x] 6.1 `userDetailTexts.ts`: a origem "Criado na instalação". `userDetailTexts.test.ts`: o caso do topo, com a negativa de "administrador".
- [x] 6.2 `components/UserTwoFactorCard.tsx`: o texto do próprio com "peça a outro Admin". O caso do topo em `UserDetailPage.test.tsx`.
- [x] 6.3 `components/UserDataCard.tsx` → "Dados e papel":
  - o seletor e as descrições do quadro `UsuarioDetalhe` (`m2`);
  - o desativado com o seletor desabilitado e o texto;
  - o bloco de promoção, pelo `useCallerPassword`;
  - a ordem dados → papel, sem o passo 2 depois de falha no passo 1;
  - as respostas e os textos do resultado, inclusive a gravação parcial.

  `UserDataCard.test.tsx`: os cenários do requisito "Cartão Dados de Editar usuário".
  - **(guarda)** Defeito: os dois `PUT` em ordem inversa (**o Salvar que grava o papel antes dos dados**). Reprova "Dados antes do papel".
  - **(guarda)** Defeito: o passo 2 sai mesmo com o `409` do passo 1. Reprova "Dados com falha não mudam o papel".
  - **(guarda)** Defeito: o texto longo do papel também quando só os dados mudam. Reprova "Salvar com sucesso" (a negativa do texto do papel).
- [x] 6.4 Rebaixar a si mesmo, em `UserDataCard.tsx` com a `UserDetailPage`:
  - a confirmação do quadro;
  - no `200`, `renewal.renew()`, nenhuma invalidação de `['users']` e `navigate('/account', { state: { papelNovo } })`;
  - o `409` do último Admin abaixo do Papel.

  Testes: os três cenários do requisito "Rebaixar a si mesmo". O "Rebaixado vai a Minha conta sem reler Usuários" conta os pedidos a `GET /users` e `GET /users/{id}` depois do `200` (zero).
  - **(guarda)** Defeito: a invalidação de `USERS_QUERY_KEY` e do detalhe depois do rebaixamento de si mesmo (**o rebaixamento de si mesmo que relê Usuários**). Reprova "Rebaixado vai a Minha conta sem reler Usuários".
  - **(guarda)** Defeito: o `PUT /users/{id}/role` antes da confirmação. Reprova "A confirmação antes da rota".

## 7. Minha conta (apps/frontend, `panel-account-page`)

- [x] 7.1 `components/AccountDataCard.tsx`: a linha "Papel" e o texto por `papel` do `GET /account`. `features/account/api/accountApi.ts`: `papel` no `Account`. `AccountPage.test.tsx`: os cenários do requisito "Cartão Dados", inclusive "O papel gravado vale sobre o do token".
  - **(guarda)** Defeito: o texto escolhido pelo papel do id token, e não pelo do `GET /account`. Reprova "O papel gravado vale sobre o do token".
- [x] 7.2 `accountTexts.ts`: "Peça a um Admin do painel…". O caso do topo em `TwoFactorCard.test.tsx`.
- [x] 7.3 `pages/AccountPage.tsx`: o aviso "O seu papel agora é {Papel}." pelo `location.state`, só para Operador e Leitor. Testes: "Chegada depois do rebaixamento" e "Entrada comum sem aviso".

## 8. Telas de domínio (apps/frontend, `panel-roles`, "O que cada papel vê")

- [x] 8.1 Agentes:
  - `AgentListPage`: "Novo agente";
  - `AgentDetailPage`: cabeçalho;
  - `AgentToolsTab`, `AgentKnowledgeTab`, `AgentDelegationsTab`: as caixas desabilitadas e os botões e barras escondidos para o Leitor.

  Um caso por item da tabela, com a **negativa** para o Leitor e a presença para o Operador.
- [x] 8.2 MCP: `McpServerListPage` ("Novo servidor MCP") e `McpServerDetailPage` ("Editar", "Ativar"/"Desativar", "Testar conexão"; "Ver tools" continua). Testes como na 8.1.
- [x] 8.3 Conhecimento:
  - `KnowledgeBaseListPage`: "Nova base", "Criar a primeira base";
  - `KnowledgeBaseDetailPage`: cabeçalho e "Excluir";
  - `KnowledgeBaseSyncOriginCard`: "Sincronizar agora";
  - `KnowledgeDocumentsCard`: "Adicionar documento", "Atualizar", "Excluir", "Reindexar documento";
  - `utils/connectorErrors.ts`: o `403` sem `code` passa ao texto de ação recusada (premissa 10).

  Testes como na 8.1, os casos do topo em `connectorErrors.test.ts`, e a negativa de "configuração da instalação".
- [x] 8.4 Canais: `ChannelListPage` ("Novo canal") e `ChannelDetailPage` (sem a aba Sessões, `defaultValue` "config", sem "Editar" e sem "Ativar"/"Desativar" para o Leitor). Testes: "Leitor abre o canal em Configuração", com a contagem de `GET /channels/{id}/sessions` (zero), e "Aba Sessões é a aba padrão" para o Operador.
  - **(guarda)** Defeito: `defaultValue="sessions"` para todo papel (**o Leitor abrindo o canal na aba Sessões**). Reprova "Leitor abre o canal em Configuração".

## 9. Premissas e textos fora do código de tela

- [x] 9.1 `openspec/specs/user-management/spec.md`, Purpose (premissa 7): tirar "Na M1, sem papéis, todo usuário real chama todas; a #184 restringe ao Admin" e dizer que as rotas são só do Admin (`route-roles`). O delta não alcança o Purpose, por isso a edição é direta e registrada aqui.
- [x] 9.2 Conferir que nenhuma das premissas 4, 5, 6 e 10 sobrou: `grep -rn "#184, #185\|a #184 troca\|sem coluna de papel\|para o operador. É configuração" apps/frontend/src openspec/specs` sem resultado depois do sync.

## 10. Documentação

- [x] 10.1 `docs/deployment.md`: a seção "Papéis Admin, Operador e Leitor (#184)" passa a "(#184, #185)", com as notas de D9:
  - as quatro imagens juntas;
  - o conferir pela tela;
  - o rebaixamento em Editar usuário, com o `curl` como alternativa;
  - a volta atrás com as três formas e o motivo de levar o `frontend` junto.
- [x] 10.2 `CHANGELOG.md` e `02-HISTORICO_E_STATUS.md`, **antes do archive** e dentro do PR. O 02 traz:
  - a baseline e a rodada final;
  - os guardas com a reprovação vista;
  - a projeção de D8 contra o medido, em três colunas (casos, linhas, duplos);
  - o checklist de validação visual (10.3).
- [x] 10.3 **Checklist de validação visual** (regra da M1: vai no `tasks.md`, no 02 e no PR, e a validação fica para a homologação, sem prender o PR), cada item nos **dois esquemas de cor**, contra o quadro com `entrega=m2`:
  - `Usuarios`: `lista` (coluna Papel, rodapé "Admin", menu sem "Aplicações externas"), `formulario` (sem escolha e com escolha), `papel-ausente`, `link-gerado`;
  - `UsuarioDetalhe`: `promover`, `promover-senha-errada`, `promover-senha-bloqueada`, `rebaixar-ultimo-admin`, `rebaixar-a-si-mesmo` (`alvo=proprio`), `papel-salvo`, `dados-salvos-papel-nao`, `alvo=desativado`, `alvo=proprio` (subtítulo e texto das 2 etapas);
  - `MinhaConta`: `papel=admin`, `papel=operador`, `papel=leitor` (menu, linha Papel, rodapé, texto dos Dados, texto do celular), `aviso=rebaixado`;
  - `AcessoNegado`: `estado=pagina` e `estado=acao`, com `papel=operador` e `papel=leitor`;
  - telas de domínio, sem quadro, pela tabela de `panel-roles`:
    - Leitor sem "Novo agente", sem o cabeçalho de escrita do agente, com as caixas das abas desabilitadas e sem barras de salvar;
    - sem "Novo servidor MCP" e sem "Testar conexão";
    - sem "Nova base", sem "Sincronizar agora" e sem as ações de documento;
    - sem "Novo canal" e sem a aba Sessões, abrindo em Configuração;
    - Operador com tudo isso, e sem a seção "Acesso".

## 11. Fechamento

- [x] 11.1 Rodada da `frontend` pelo `suite-185.sh`, com a árvore da change. Comparação por arquivo + nome com a baseline: nenhum caso some; os que mudam de asserção são só os do topo; os novos são listados.
- [x] 11.2 `openspec validate papeis-no-painel --strict`.
- [x] 11.3 A projeção de D8 medida (convenção 18), em três colunas, no 02.
- [x] 11.4 A contagem de casos por papel explícito (`signInAsRole('operador' | 'leitor' | 'admin')`), por spec: `panel-roles`, `panel-users-page`, `panel-user-detail-page`, `panel-account-page`, `inbox-channel-catalog-ui`, `knowledge-base-sync-source-ui`. Toda spec que fala de Operador ou Leitor com pelo menos um caso de cada um desses papéis gravado.
- [x] 11.5 **Conferência no navegador**, com o `apps/api` local, a migration aplicada num banco descartável, e o Vite:
  - um Admin, um Operador e um Leitor reais, criados pelo convite com papel;
  - o menu de cada um;
  - `/users` digitado pelo Operador;
  - o canal aberto pelo Leitor;
  - a promoção com senha;
  - o rebaixamento de si mesmo até Minha conta;
  - um Admin rebaixado por outro, recebendo o `403`, renovando, e o menu mudando sem recarregar.

  O que a aba de automação não conseguir conferir vai para o checklist visual (10.3), com o motivo. O banco descartável é apagado no fim.

## Registro do apply (09/11/2026)

**Rodadas** (`suite-185.sh`, `frontend`, uma por vez, com o lock; workers padrão):
- `185-r0`: só a 0.2 aplicada. **Reprovada por arranjo, corrigida:** 3 casos "sem sessão" viram a sessão-padrão (não é efeito de CPU). Corrigidos na 0.2 com `signOutForTest()`.
- `185-r0b`: só a 0.2. APROVADA, 2351/2351, CPU mínima 58.
- `185-r1`: a change inteira. **APROVADA, 2491/2491 em 190 arquivos, 178 s, CPU mínima 60** (100 no início, 72 no fim). Relatório por nome em `185-r1/frontend-nomes.txt`.
- `185-r2`: depois do ajuste do dono. **APROVADA, 2493/2493**, CPU mínima 58; contra a `185-r1`, nada saiu, entraram só os 2 casos do 403.

**Comparação por arquivo + nome contra a baseline:**
- saíram 3, todos da tabela do topo, e todos por renomeação:
  - `AppShell` "…abaixo, o e-mail" → "…abaixo, o papel";
  - `AppShell` "sem nome…, e a segunda fica vazia" → "…e o papel na segunda";
  - `userDetailTexts` "sem convidadoPor é o primeiro administrador" → "…é o criado na instalação, sem falar em administrador";
- entraram 143 (140 novos e as 3 renomeações). A lista por arquivo está no 02.

**Ajuste do dono (09/11/2026, premissa 3 no caminho do 403):** dois casos novos, "A renovação depois do 403 traz o id token novo e o papel derivado" (`sessionFetch.test.ts`) e "O 403 renova e a casca passa a Operador sem recarregar" (`AppShell.test.tsx`), com o guarda **2.1c** ("o 403 chama `renew()` sem passar o token atual"), que reprovou os dois.

**Os guardas: 17 no total**, cada um no 02: 1.1a, 1.1b, 1.2, 2.1a, 2.1b, 2.1c, 2.2, 3.2a, 3.2b, 5.3, 6.3a, 6.3b, 6.3c, 6.4a, 6.4b, 7.1 e 8.4.

**Os guardas** estão no 02, com o teste, a mensagem e o "voltou" com o sha. Os de `roles.ts`, `routes.tsx`, `UserDataCard.tsx` e `ChannelDetailPage.tsx` foram refeitos sobre a árvore final, depois do Prettier, em `185-guardas/final/`.

**Fora da tabela, sem sair, mudar de nome nem afrouxar:**
- `UsersPage.test.tsx` "convite pendente": os índices das células passam uma adiante com a coluna Papel, com as mesmas asserções;
- `usersApi.test.ts` "POST /users/invitations leva o corpo do contrato": o corpo esperado ganhou `papel` (a asserção ficou mais estrita), e duas chamadas tipadas ganharam `papel`;
- arranjo:
  - `InviteForm.test.tsx` e `InviteLinkPanel.test.tsx` escolhem o papel;
  - `AppShell.test.tsx` regrava a sessão de Admin depois do `localStorage.clear()`;
  - `ProtectedRoute.test.tsx`, `agentsApi.test.ts` e `knowledgeBasesApi.test.ts` limpam a sessão-padrão nos casos "sem sessão".

**A 9.2** fica verde no código (`apps/frontend/src` sem nenhuma das frases). Nas specs vivas, o texto velho de `panel-users-page` e de `frontend-app-shell` só sai no sync do archive.
