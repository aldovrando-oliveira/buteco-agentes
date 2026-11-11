Um guarda marcado **(guarda)** só conta depois de ter reprovado contra o defeito que ele existe para pegar (convenção 15). Para cada um:

1. o `~/.cache/buteco-agents/bin/guarda-188.sh` reintroduz o defeito nomeado. A mutação toca só o trecho nomeado, e nunca restaura um diretório ao `HEAD`;
2. o teste roda e reprova;
3. a árvore volta pela cópia feita antes da mutação, com o sha conferido.

A tarefa registra:
- o nome do teste;
- a mensagem da reprovação;
- o "voltou: <arquivo>", com o sha igual ao de antes.

Uma mutação que quebra a compilação, ou que reprova por "no tests", não vale: é descartada e refeita. As saídas ficam em `~/.cache/buteco-agents/188-guardas/<tarefa>`.

**As suítes** rodam pelo `~/.cache/buteco-agents/bin/suite-188.sh`, **uma por vez, com o lock** (`suites.lock`; o guarda usa o mesmo).
- Só a `frontend`, com o RabbitMQ de pé (`PAUSAR_RABBIT=0`).
- Sob limite térmico, vale a variante `MAXWORKERS=4`, registrada no `-estado.txt`.
- Vale a regra assimétrica: uma rodada verde conta mesmo com a CPU caindo, e uma reprovação só conta com a CPU em 100 do início ao fim; senão é inconclusiva e se repete.
- Antes de cada rodada, o load de 1 e de 5 min fica abaixo de 6 (o script espera).

**A baseline** está em `~/.cache/buteco-agents/188-baseline/`, em `2b93837`, com a árvore limpa:
- `APROVADA`: 2493 de 2493 casos em 190 arquivos, em 219 s, com a CPU mínima em 60;
- o relatório por arquivo + nome está em `frontend-relatorio.tsv`. A comparação final é por arquivo + nome.

**Parada** (convenção 9):
- **parar** quando uma premissa falsa mudar comportamento ou contrato, ou quando a correção reduzir cobertura;
- **seguir e registrar** quando a correção só acrescentar teste ou cenário para pôr um guarda no lugar certo, ou só tocar comentário ou documentação.

**Os textos** são os do protótipo `H7dPBZafiSkdKd1hqesPZ6`, na versão `1794278589-23e3` (a "versão 65"), com `entrega=m2`, salvo as divergências registradas no design ("A versão 65"): o `409` de já liberado usa o `title` do servidor; o modal de desativar o agente mantém o título e o texto de hoje; o bloqueio usa o N do `Retry-After`; o "por até 7 dias" do formulário de gerar. Texto que não couber no contrato para a tarefa e vai ao dono.

**Os testes de página rodam como Admin por padrão** (`test/setup.ts`, desde a #185). O comportamento de Operador e Leitor é provado só pelos casos que gravam o papel (`signInAsRole`).

## Casos que saem ou mudam de asserção

Nenhum caso é afrouxado. Um sai, porque o requisito que ele afirma deixa de valer nesta change; os outros mudam de asserção. Cada um é registrado na tarefa que o muda.

| arquivo | caso | o que muda | requisito | tarefa |
|---|---|---|---|---|
| `components/layout/AppShell.test.tsx` | "o menu não tem Aplicações externas" | **sai**; no lugar entra "Aplicações externas ativo no detalhe de uma aplicação" | `frontend-app-shell`, "Seção Acesso" | 2.2 |
| `components/layout/AppShell.test.tsx` | "depois de Insights vêm o rótulo Acesso e os links Usuários e Identidade" | a fatia passa de 4 a 5 textos, com "Aplicações externas" entre os dois; o nome passa a "…Usuários, Aplicações externas e Identidade" | idem | 2.2 |
| `components/layout/AppShell.test.tsx` | "dá um ícone decorativo a cada item, sem roubar o nome acessível" (l. 123–146) | "Aplicações externas" entra na lista de rótulos, e `a svg` passa de 8 a 9 | idem, e "Navegação com ícone" | 2.2 |
| `components/layout/AppShell.test.tsx` | "em /users, o item Usuários fica ativo, e Identidade não" | afirma também que "Aplicações externas" não fica ativo (acréscimo de asserção) | idem | 2.2 |
| `components/layout/AppShell.test.tsx` | "Operador e Leitor sem a seção: %s" | afirma também a ausência de "Aplicações externas" (acréscimo de asserção) | idem | 2.2 |

**Mudam de arranjo, sem mudar asserção:** `features/agents/pages/AgentDetailPage.test.tsx` (49 casos) passa a fazer `vi.mock` do cliente de aplicações, com a contagem `{ total: 0, ativas: 0 }` e a lista vazia por padrão (0.3). `app/RequirePapel.test.tsx` só ganha linhas na tabela (2.3).

## 0. Preparação

- [x] 0.1 (scripts locais) `~/.cache/buteco-agents/bin/guarda-188.sh`, cópia do `guarda-185.sh` (o lock e o ramo `frontend`, `npx vitest run <arquivos>`). Conferir com:
  - uma mutação nula, recusada com "defeito não aplicado";
  - uma mutação real num arquivo de `apps/frontend/src`, que volta com o sha igual;
  - uma segunda chamada com o lock tomado por uma suíte, recusada com 75.
- [x] 0.2 (apps/frontend) `features/applications/test/`: as fixtures (`PORTAL`, `CHATBOT` sem agentes e nunca usada, `CRM` desativada; um segredo de cada estado; vínculos com agente ativo e inativo), por cima do `fakeAccountApi` (o `fetch` falso por caminho, com o `sessionFetch` real), e os `render` das páginas com a sessão de Admin.
- [x] 0.3 (apps/frontend) `AgentDetailPage.test.tsx`: o `vi.mock` do cliente de aplicações, com a contagem `{ total: 0, ativas: 0 }` e a lista `[]`. Rodar só esse arquivo: os 49 casos continuam aprovados, sem nenhuma asserção mudada.

## 1. Tipos, cliente e chaves (apps/frontend, design D2)

- [x] 1.1 `features/applications/types.ts`: `ApplicationSummary`, `ApplicationDetail`, `SecretSummary` (`estado: 'ativo' | 'substituido' | 'expirado' | 'revogado'`), `CreatedApplication`, `CreatedSecret`, `ApplicationAgent` (`operacoes` com os três valores), `AgentApplication`, `AgentApplicationsCount`, nos nomes de campo do fio.
- [x] 1.2 `features/applications/api/applicationsApi.ts`: as 14 chamadas (oito de `/applications/*`, quatro de `/applications/{id}/agents`, duas de `/agents/{id}/applications*`), com o `request<T>` e o `ApiFailure` da casa (o `problem+json` com `title`, `errors` e `Retry-After`). `listApplications({ busca, estado })` omite os parâmetros vazios. Casos: a URL e o corpo de cada uma; o `?busca=` aparado e codificado; o `204` do `DELETE` sem corpo.
- [x] 1.3 `features/applications/queryKeys.ts`, e `applicationsTexts.ts` com os textos do quadro (os singulares e plurais como funções).

## 2. Menu e rotas (apps/frontend, `frontend-app-shell`, `panel-roles`, D1)

- [x] 2.1 `app/routes.tsx`: `/applications`, `/applications/new`, `/applications/:id`, `/applications/:id/edit` dentro do `<RequirePapel papeis={SO_ADMIN} />`.
- [x] 2.2 `AppShell`: "Aplicações externas" (`Plug`) entre "Usuários" e "Identidade", só para o Admin. Os casos do topo mudam aqui, cada um registrado.
- [x] 2.3 `RequirePapel.test.tsx`: as quatro rotas na tabela, entrando só o Admin; o caso "Operador e Leitor numa aplicação externa".
- [x] 2.4 **(guarda)** "Operador em `/applications/:id` não chama o servidor": a sessão de Operador abre `/applications/a1` e vê "Você não tem acesso a esta página.", e nenhum `fetch` a `/applications/` é feito. **Defeito:** as rotas de `/applications` declaradas fora do `<RequirePapel papeis={SO_ADMIN} />`.

## 3. Lista (apps/frontend, `panel-external-applications`, "Lista")

- [x] 3.1 `ApplicationsListPage`: cabeçalho, busca com espera de 300 ms, filtro, a tabela, os chips ou "Nenhum", o último uso relativo (o `formatRelative` da casa), o rodapé com o singular.
- [x] 3.2 Os estados: vazia, busca sem resultado (com e sem filtro, "Limpar busca"), carregando, erro com "Tentar de novo", e o `403` na carga (tela de acesso negado).
- [x] 3.3 Casos: os cinco cenários da spec; a lista anterior na tela enquanto a busca carrega.

## 4. Nova aplicação e a credencial criada (apps/frontend, `panel-external-applications`, D3)

- [x] 4.1 `ApplicationForm` (reaproveitado pela 7.1): Nome e Descrição, a conferência antes de enviar, o `400` e o `409` no campo.
- [x] 4.2 `ApplicationCreatePage`: as seções "Identificação", "Credencial" (sem URL) e "Confirmação" (`CallerPasswordField`), o aviso de nascer sem agentes, "Criando…". A chamada vai pelo `useCallerPassword().enviar`, **sem `useMutation`**; no `201`, só `invalidateQueries` da lista.
- [x] 4.3 `CreatedCredentialCard`, no lugar do formulário: os três valores com "Copiar"/"Copiado", o aviso, a caixa, "Ir para a aplicação" desabilitado sem a caixa. O segredo fica no `useState` da página.
- [x] 4.4 Casos: os cenários de "Nova aplicação" e de "Credencial criada" (senha vazia sem envio, nome repetido com a senha mantida, `401` sem renovar, `423` com o N do `Retry-After`, sem `GET /identity`, sair sem marcar a caixa).
- [x] 4.5 **(guarda)** "Nada nos caches do TanStack Query": cria a aplicação e serializa `getQueryCache().getAll()` e `getMutationCache().getAll()` sem `btc_sk_`. **Defeito, em duas mutações, cada uma registrada:** (a) a criação por `useMutation` (o `MutationCache` guarda o `data` com o segredo); (b) `queryClient.setQueryData` do detalhe com a resposta do `201`.
- [x] 4.6 **(guarda)** "Nada no histórico nem no armazenamento": cria, marca a caixa, "Ir para a aplicação"; `window.history.state`, `sessionStorage` e `localStorage` sem `btc_sk_`. **Defeito:** `navigate('/applications/{id}', { state: { segredo } })`.
- [x] 4.7 **(guarda)** "Voltar não traz o segredo": depois de "Ir para a aplicação", voltar pelo histórico mostra o formulário vazio. **Defeito:** o segredo guardado numa variável de módulo e lido na montagem da página.
- [x] 4.8 **(guarda)** "Ir para a aplicação só depois da caixa": sem a caixa, o botão está desabilitado e o clique não navega. **Defeito:** o `disabled` do botão sem a condição da caixa.

## 5. Detalhe, cabeçalho e desativar (apps/frontend, `panel-external-applications`)

- [x] 5.1 `ApplicationDetailPage`: o cabeçalho (descrição, selo, "Desativada em", "Editar", "Desativar"/"Reativar"), as abas em `?tab=credencial`, carregando, erro, "Aplicação não encontrada." no `404`, o `403` na carga.
- [x] 5.2 Desativar: a `ConfirmBar` solta acima das abas, com o texto da spec (sem o prazo de 5 min); a falha na própria confirmação. Reativar sem confirmação, com o aviso.
- [x] 5.3 Casos: os cenários de "Detalhe" e de "Desativar e reativar".

## 6. Credencial e conexão (apps/frontend, `panel-external-applications`, D4)

- [x] 6.1 `ApplicationCredentialTab`: Client ID, endpoint, a tabela dos quatro estados, o rodapé, o cartão do `curl`, o aviso de sem segredo vivo.
- [x] 6.2 Gerar: o formulário dentro do cartão, pelo `useCallerPassword` (sem `useMutation`); o `401`, o `423`, os dois `409`; o botão desabilitado com 3 vivos e com a desativada, com a dica; o bloco "Segredo novo" no estado da página do detalhe, com "Concluir" depois da caixa.
- [x] 6.3 Revogar e "Revogar agora": a faixa abaixo da linha, com os textos e o do único vivo; o `404` na faixa. Uma ação por vez no cartão.
- [x] 6.4 Casos: os cenários de "Aba Credencial e conexão", "Gerar novo segredo", "Revogar" e "Uma ação por vez"; o bloco "Segredo novo" sobrevive à troca de aba; o segredo gerado entra na serialização dos caches da 4.5 (sem `btc_sk_`).
- [x] 6.5 **(guarda)** "Gerar com 3 vivos": um `ativo` e dois `substituido` deixam "Gerar novo segredo" desabilitado com a dica. **Defeito:** a contagem de vivos só com `estado === 'ativo'`.
- [x] 6.6 **(guarda)** "Revogar pede confirmação": "Revogar" abre a faixa e nenhum `POST …/revoke` sai antes de confirmar. **Defeito:** "Revogar" chama a rota direto.

## 7. Editar aplicação (apps/frontend, `panel-external-applications`, "Editar")

- [x] 7.1 `ApplicationEditPage` com o `ApplicationForm`: o texto "O client ID e os segredos não mudam ao editar.", "Salvando…", o `400`, o `409` e o `404`; o `200` volta ao detalhe.
- [x] 7.2 Casos: os dois cenários da spec, e o formulário preenchido do detalhe.

## 8. Aba Agentes (apps/frontend, `panel-external-application-agents`, D5)

- [x] 8.1 `ApplicationAgentsTab`: a grade, o selo "Inativo", o vazio, a nota da delegação, o contador da aba, carregando e erro.
- [x] 8.2 Adicionar dentro do cartão: o seletor (`GET /agents/` menos a grade, com " (inativo)"), as operações com o padrão, o aviso do inativo, "Marque ao menos uma operação.", sem agente disponível; o `201`, o `409` com o `title` do servidor, o `400 agenteId`.
- [x] 8.3 Editar na linha e o `404` com o aviso; remover com a faixa na linha; o `204` e o `404` do `DELETE` como sucesso; uma ação por vez; o aviso da aplicação desativada com tudo habilitado.
- [x] 8.4 Casos: os cenários da spec.
- [x] 8.5 **(guarda)** "Remover pede confirmação": "Remover" abre a faixa e nenhum `DELETE` sai antes de confirmar. **Defeito:** "Remover" chama o `DELETE` direto.
- [x] 8.6 **(guarda)** "Já retirado por outro Admin" (o caso próprio da Q9): `DELETE` com `404` tira a linha, recarrega a grade e não mostra erro. **Defeito:** o `404` tratado como as outras falhas, com o `title` na faixa.

## 9. Card do agente e o modal (apps/frontend, `panel-external-application-agents`, `agent-catalog-ui`, D7)

- [x] 9.1 `AgentApplicationsCard`: a rota pelo papel (lista para o Admin, contagem para Operador e Leitor), os textos de cada caso e os singulares, o rodapé, carregando, erro e o `403` no card. Entra no `AgentOverviewTab` por uma prop (um nó a mais na coluna da direita, abaixo de "Protocolo A2A"), para os testes do `AgentOverviewTab` não precisarem de `QueryClient`.
- [x] 9.2 O modal de desativar do `AgentDetailPage`: a frase "afeta N" abaixo do texto de hoje, com `ativas > 0`; sem a contagem, ou com falha, o modal de hoje.
- [x] 9.3 Casos: os cenários do card e os dois novos de `agent-catalog-ui`, com `signInAsRole('operador')` e `signInAsRole('leitor')` onde a spec nomeia o papel.
- [x] 9.4 **(guarda)** "Leitor vê só a contagem": com a sessão de Leitor, o card mostra a contagem, sem nomes nem links, e nenhum `GET /agents/{id}/applications` é feito. **Defeito:** a escolha da rota sem o papel (sempre a lista). Repetir a reprovação com a sessão de Operador.

## 10. Documentação

- [x] 10.1 `docs/deployment.md`: nas seções da #186 e da #187, a #188 (só a imagem do `frontend`, a ordem, o recuo só do `frontend` e o recuo do backend levando o `frontend`); o "Liberar um agente sem a tela" passa a alternativa.
- [x] 10.2 `CHANGELOG.md` e `02-HISTORICO_E_STATUS.md`, **antes do archive** e dentro do PR. O 02 traz a baseline e a rodada final, os guardas com a reprovação vista, a projeção do (i) contra o medido em três colunas (casos, linhas, duplos), as divergências da versão 65 e o checklist da 10.3.
- [x] 10.3 **Checklist de validação visual** (regra da M1: vai no `tasks.md`, no 02 e no PR, e a validação fica para a homologação, sem prender o PR), **cada item no esquema claro e no escuro**, contra a versão 68:
  - menu: "Aplicações externas" entre "Usuários" e "Identidade", ativo em `/applications` e no detalhe; ausente para Operador e Leitor;
  - `AplicacoesLista` (`carga`): `lista` (com "Nenhum" no "Chatbot do site"), `uma`, `vazia`, `busca-sem-resultado` ("erp" com Ativas), `carregando`, `erro`;
  - `AplicacaoNova` (`estado`): `formulario`, `nome-vazio`, `nome-longo`, `descricao-longa`, `nome-repetido`, `senha-vazia`, `senha-errada`, `bloqueado` (com o N do `Retry-After`), `enviando`;
  - `AplicacaoCredencial`: `copiado` = `nenhum`, `client`, `segredo`, `endpoint`; `guardei` falso e verdadeiro;
  - `AplicacaoDetalhe`, `aplicacao`: `ativa`, `confirmar-desativar`, `desativada`, `reativada`, `carregando`, `erro`, `nao-encontrada`;
  - `AplicacaoDetalhe`, `agentesExtra`: `agente-inativo`, `confirmar-remover`, `adicionar-ja-liberado` (com o `title` do servidor), `adicionar-agente-sumiu`, `sem-agente-disponivel`, `editar-vinculo-retirado`, `carregando`, `erro`; e `aplicacao=desativada` com o aviso na aba;
  - `AplicacaoDetalhe`, `credencial`: `lista`, `gerar-senha` (com "por até 7 dias"), `gerar-senha-errada`, `gerar-bloqueado`, `gerar-409-limite`, `segredo-novo`, `limite`, `confirmar-revogar`, `confirmar-revogar-agora`, `revogar-unico-vivo`, `sem-segredo-vivo`; e com `aplicacao=desativada` (todos revogados, a dica) e `aplicacao=reativada` (sem segredo vivo);
  - `AplicacaoDetalhe`, `estadoAgentes`: `lista`, `adicionando`, `editando`, e `recemCriada`; `AplicacaoDetalheAdicionar` e `AplicacaoDetalheEditar`;
  - `AplicacaoEditar` (`estado`): `formulario`, `nome-vazio`, `nome-repetido`, `salvando`;
  - `AgenteDetalhe`, `aplicacoes` × `papel`: `lista`, `uma` (o rodapé no singular, "…as tasks que ela abrir nele…"), `nenhuma`, `so-desativadas` (e o singular "Usado por 1 aplicação externa, desativada."), `carregando`, `erro`, cada um com `admin`, `operador` e `leitor`; o card abaixo de "Protocolo A2A"; `modalDesativar` com o título e o texto de hoje e a frase "afeta N", no plural e no singular.

## 11. Fechamento

- [x] 11.1 Rodada da `frontend` pelo `suite-188.sh`, com a árvore da change. Comparação por arquivo + nome com `frontend-relatorio.tsv`: nenhum caso some além do que o topo tira; os que mudam de asserção são só os do topo; os novos são listados.
- [x] 11.2 `openspec validate aplicacoes-externas-painel --strict`.
- [x] 11.3 A projeção do (i) medida (convenção 18), em três colunas, no 02.
- [x] 11.4 A contagem de casos com papel gravado por spec: `panel-roles`, `frontend-app-shell`, `panel-external-application-agents`, `agent-catalog-ui`, com pelo menos um caso de Operador e um de Leitor em cada uma que os nomeia.
- [x] 11.5 **Conferência no navegador**, com o `apps/api` local, num banco descartável migrado, e o Vite, como Admin real (e um Operador e um Leitor pelo convite com papel):
  - criar uma aplicação, copiar o segredo e obter um token com ele pelo `curl` do cartão;
  - liberar um agente e chamar `SendMessage` com o token;
  - gerar um segundo segredo (o anterior "Substituído"), revogar, chegar ao limite de 3;
  - desativar (a A2A com `404`), reativar sem segredo vivo;
  - o card do agente nos três papéis, e o modal de desativar com a frase;
  - depois de cada criação e geração, as ferramentas do navegador sem `btc_sk_` no `sessionStorage`, no `localStorage` e no histórico.

  O que a aba de automação não conseguir conferir vai para a 10.3, com o motivo. O banco descartável é apagado no fim.

## Registro do apply (10/11/2026)

- **11.1:** r1 (workers padrão), r2 e r3 (`MAXWORKERS=4`) **INCONCLUSIVAS**, com a CPU em 23 (limite térmico), cada uma reprovada por tempo num arquivo diferente que a change não toca (27, 1 e 1 casos; os de r2 e r3, sozinhos, passam). **r4 (`MAXWORKERS=4`, árvore final): APROVADA, 2628/2628, CPU em 100 do início ao fim.** Por arquivo + nome contra a baseline: saem só os 2 do topo, entram 137 (lista no 02).
- **Os guardas** (seções 2, 4, 6, 8 e 9): dez guardas, onze rodadas (o 4.5 em duas), todos reprovados pela asserção e com o sha de volta; refeitos depois do Prettier. Registro em `~/.cache/buteco-agents/188-guardas/registro.md`.
- **Achado na conferência (11.5)** — o detalhe sem `agentes` (design, D10.1): corrigido na tela, na fixture e na spec (`specs/external-applications`, delta nova). A fixture no formato do fio reprovou 46 casos contra o código antigo.
- **11.3:** produção +2.708 (projeção ~1.620), testes +1.945 (~2.150), duplos +284 (~200), 137 casos (~106); a explicação está no 02.
- **11.4:** casos com papel gravado — `panel-roles` (RequirePapel: Operador e Leitor nas quatro rotas e no caso próprio), `frontend-app-shell` (AppShell: Operador e Leitor sem a seção), `panel-external-application-agents` (card: Leitor e Operador, só desativadas e nenhuma com Operador), `agent-catalog-ui` (o modal com Operador; o Leitor não tem "Desativar").
