## Context

A #186 (`7095350`) e a #187 (`4ccf2be`) entregaram no `apps/api` as aplicações externas e os agentes
liberados por aplicação; as telas ficaram para cá. Os dois designs deixaram, para este, a lista dos estados
sem quadro e o contrato de cada um nas specs vivas `external-applications` e `external-application-agents`
(design da #186, "(h) Os quadros do protótipo" e "Decisões do dono"; design da #187, "(f)" e "A versão 63").

### O código em `2b93837` (main depois da #187)

- **Rotas e guarda:** `app/routes.tsx` l. 71–75 põe `/users`, `/users/:id` e `/identity` sob
  `<RequirePapel papeis={SO_ADMIN} />`; `app/routeRoles.ts` tem `SO_ADMIN` e `ADMIN_E_OPERADOR`.
- **Casca:** a seção "Acesso" tem "Usuários" e "Identidade"; a spec viva `frontend-app-shell` l. 166 diz que
  ela "SHALL NOT ter o item 'Aplicações externas', que chega com a #188", com o cenário l. 184–186.
- **Senha de quem chama:** `features/users/callerPassword.ts` (`useCallerPassword`) faz a chamada como
  `async` comum, guarda a senha só no estado e a limpa no sucesso, no `401` e ao desmontar; o
  `CallerPasswordField` é o campo. O `sessionFetch` não renova num `401` com `problem+json`
  (`auth/sessionFetch.ts` l. 42–46, 93): o `401` "Senha inválida." chega à tela.
- **Falhas:** `utils/failureNotice.ts` dá o texto do `423` com o N do `Retry-After` (`lockedText`), o
  amarelo do `423`/`429`, e o `blockedUntil` que segura o botão. O `423` do `apps/api` traz `Retry-After`
  (`LoginThrottleResponses.cs` l. 19).
- **Confirmação na página:** `features/users/components/ConfirmBar.tsx` é a confirmação de Editar usuário
  (#183), sem modal: texto vermelho, botão de perigo, "Cancelar" e o aviso da falha abaixo; `solta` para
  acima dos cartões.
- **Detalhe do agente:** abas em `?tab=` (`AgentDetailPage.tsx` l. 67–68); a Visão geral tem, na coluna da
  direita, "Modelo", habilidades, "Protocolo A2A" (`AgentA2ACard`) e "Datas"
  (`AgentOverviewTab.tsx` l. 62–94). Desativar o agente confirma num `<Modal>` (`AgentDetailPage.tsx`
  l. 343–360).
- **Modais de gestão que já existem:** `KnowledgeDocumentModal` (adicionar/editar documento,
  `KnowledgeBaseDetailPage.tsx` l. 662), `KnowledgeBaseLinkModal`, `KnowledgeBaseDeleteModal`,
  `FolderPickerModal`, as confirmações de desativar em agente, MCP e canal, e o `UnsavedChangesModal` (ver
  premissa 2).
- **Duplo de teste:** `features/account/test/fakeAccountApi.ts` é o `fetch` falso genérico (método e
  caminho, fila de respostas, o `sessionFetch` real por cima), já reaproveitado por Usuários.

### O protótipo

Canvas `claude.ai/artifact/H7dPBZafiSkdKd1hqesPZ6`. O primeiro design leu a **versão `1794277324-9687`**, em 09/11/2026, pela
ferramenta Artifact; as specs são da versão 65 ("A versão 65", adiante) (a #187 leu a `1794271196-4a59`; a listagem de arquivos ainda mostrava essa, a leitura
devolveu a nova). Quadros: `AplicacoesLista` (`entrega=m2`), `AplicacaoNova` (`federadoDisponivel=false`),
`AplicacaoCredencial`, `AplicacaoDetalhe` (`aba=agentes|identidade`, `estadoAgentes`, `recemCriada`),
`AplicacaoDetalheAdicionar`, `AplicacaoDetalheEditar`, `AgenteDetalhe` (`papel`). Os textos conferem com o
que a #187 registrou da versão 63 (três operações, "Revogar recusa o segredo na hora; um token já emitido
continua valendo por até 5 minutos.", `scope=buteco-api`, `SendMessage`, o rodapé "N aplicações · as
desativadas continuam listadas e podem ser reativadas.").

## Goals / Non-Goals

**Goals:** as telas dos sete quadros contra o contrato da #186 e da #187; o segredo mostrado uma vez sem
modal e sem guardar em lugar nenhum além do componente; os estados que os quadros não desenham, listados
para o dono desenhar antes das specs.

**Non-Goals:** rota ou resposta nova no backend (b); aba Atividade, recusas e métricas por aplicação (#199);
aplicação federada (M4); cancelamento (#264); trocar os modais que o painel já tem (premissa 2).

## Decisions

### D1. (a) O menu e as rotas

- **Menu:** "Aplicações externas" na seção "Acesso", entre "Usuários" e "Identidade", como os quadros, com o
  ícone `Plug` do `lucide-react` (o do quadro é o plugue). Ativo enquanto a rota começar por `/applications`.
  A seção continua só do Admin: nada muda para Operador e Leitor.
- **Rotas**, todas sob `<RequirePapel papeis={SO_ADMIN} />`, na tabela da guarda (`panel-roles`):

| rota | tela |
|---|---|
| `/applications` | lista |
| `/applications/new` | nova aplicação, e a credencial criada no lugar do formulário (D3) |
| `/applications/:id` | detalhe; `?tab=credencial` abre "Credencial e conexão", sem `tab` é "Agentes" (como a Visão geral do agente) |
| `/applications/:id/edit` | editar nome e descrição (D6) |

- O caminho é o do `apps/api` (`/applications/*`), em inglês como `/users`, `/agents`, `/mcp-servers`.
  *Descartado:* `/external-applications` (mais longo, e diferente da API sem ganho); deixar a lista aberta ao
  Operador para leitura (contra a matriz da #184: `GET /applications/` é só do Admin, e o Operador veria a
  tela de acesso negado de qualquer jeito).
- A aba padrão é "Agentes", a primeira do quadro e a que a aplicação recém-criada precisa (nasce sem
  agentes). O card do agente e o "Gerenciar" levam a `/applications/:id` e a `/applications`.

### D2. (b) Cada tela contra o contrato

Todas as respostas abaixo existem em `2b93837`. Em toda carga: o `403` da consulta principal mostra a tela de
acesso negado, e o `403` de ação, o aviso da ação (`panel-roles`); rede, `5xx` e `429` vão ao aviso genérico
do `failureNotice`.

**`AplicacoesLista`**

| ação | rota | resposta → tela |
|---|---|---|
| abrir, buscar, filtrar | `GET /applications/?busca=&estado=` | `200 []` sem filtro → lista vazia; `200 []` com filtro → busca sem resultado; `200` → tabela (`nome`, `descricao`, `clientId`, `agentes[].nome`, `ativa`, `ultimoUso`); `400 estado` → não acontece (o filtro só manda `ativas`/`desativadas`) |

**`AplicacaoNova`**

| ação | rota | resposta → tela |
|---|---|---|
| "Criar aplicação" | `POST /applications/` `{ nome, descricao, senha }` | `201` → a credencial criada (D3); `400` `errors.nome`/`errors.descricao` → no campo; `409` "Já existe uma aplicação com este nome." → no campo Nome, a senha mantida (o `409` vem antes da senha e não conta no bloqueio); `401` "Senha inválida." → no campo da senha, que se esvazia; `423` → o aviso amarelo do `lockedText`, botão desabilitado até o `Retry-After` |

A ordem do servidor é `403`, `423`, `400`, `409`, `401`: o `423` aparece mesmo com o nome vazio. O painel
confere antes o nome vazio, os 80 e os 280 caracteres e a senha vazia ("Informe a sua senha.", sem enviar,
como o `useCallerPassword`), e o `400` do servidor cai nos mesmos campos.

**Credencial criada** — sem chamada: a resposta do `201` traz `clientId`, `segredo.valor`, `endpointToken`.

**`AplicacaoDetalhe`, cabeçalho**

| ação | rota | resposta → tela |
|---|---|---|
| abrir | `GET /applications/{id}` | `200` → nome, `ativa`, `desativadaEm`, `descricao`; `404` "Aplicação não encontrada." → o título no lugar do conteúdo, com o link para a lista |
| "Desativar" → confirmar | `POST /applications/{id}/deactivate` | `200` → selo "Desativada", segredos revogados, "Reativar"; `404` → o `title` no aviso |
| "Reativar" | `POST /applications/{id}/activate` | `200` → "Ativa", sem segredo vivo (D4) |
| "Editar" → salvar | `PUT /applications/{id}` `{ nome, descricao }` | `200` → volta ao detalhe; `400`/`409` como na criação; `404` → o `title` |

**Aba "Credencial e conexão"** (`clientId`, `endpointToken`, `segredos[]` do mesmo `GET`)

| ação | rota | resposta → tela |
|---|---|---|
| "Gerar novo segredo" → senha → "Gerar segredo" | `POST /applications/{id}/secrets` `{ senha }` | `201` `{ valor, sufixo, criadoEm, validoAte }` → o segredo novo no cartão (D3); `409` "Reative a aplicação antes de gerar um segredo." e `409` "Revogue um segredo antes de gerar outro." → o `title` no formulário; `401` → no campo; `423` → aviso amarelo; `404` → o `title` |
| "Revogar" / "Revogar agora" → confirmar | `POST /applications/{id}/secrets/{secretId}/revoke` | `200` (detalhe) → a linha passa a "Revogado"; `404` "Segredo não encontrado." → o `title` na linha |

**Aba "Agentes"**

| ação | rota | resposta → tela |
|---|---|---|
| abrir | `GET /applications/{id}/agents` | `200` → grade (`agenteId`, `nome`, `descricao`, `ativo`, `operacoes`); `[]` → "Nenhum agente liberado…" |
| "Adicionar agente" (seletor) | `GET /agents/` (catálogo existente) menos a grade | os agentes ainda não liberados, com "(inativo)" |
| "Adicionar" | `POST /applications/{id}/agents` `{ agenteId, operacoes }` | `201` → a linha nova; `400 operacoes` → "Marque ao menos uma operação." (só se o painel deixasse passar); `400 agenteId` "Agente não encontrado." → no seletor, e o catálogo se recarrega; `409` "Este agente já está liberado para a aplicação." → no formulário, e a grade se recarrega |
| "Editar" → "Salvar" | `PUT /applications/{id}/agents/{agentId}` `{ operacoes }` | `200` → a linha volta ao normal; `404` "Agente não liberado para esta aplicação." → aviso no cartão e a grade se recarrega |
| "Remover" → confirmar | `DELETE /applications/{id}/agents/{agentId}` | `204` → a linha sai; `404` → ver Q9 |

**`AgenteDetalhe`, card "Aplicações com acesso"**

| papel | rota | resposta → tela |
|---|---|---|
| Admin | `GET /agents/{id}/applications` | `200` → nome (link), "Desativada", operações por extenso, "aplicação usada há …"/"aplicação nunca usada"; o rodapé conta as `ativa` |
| Operador, Leitor | `GET /agents/{id}/applications/count` | `200 { total, ativas }` |
| Admin rebaixado no prazo do token | a lista | `403` → o aviso de ação no card (`panel-roles`), sem derrubar a página |

**Dado que a tela precisa e o backend não serve (convenção 1): nenhum.** Um único ponto pede escolha, sem ser
falta: o quadro `AplicacaoNova` mostra `https://[URL_PUBLICA]/connect/token` **antes** de criar, e o
`endpointToken` só vem no `201` e no detalhe. A spec `agent-catalog-ui` proíbe montar endereço a partir de
onde a página foi servida. O `GET /identity` (Admin) traz o `issuer`, de onde sai o endpoint; recomendo
**não** chamá-lo só para essa dica e trocar a frase (Q4).

### D3. (c) O segredo mostrado uma vez é gestão: sem modal

**Recomendação: é gestão, não exceção.** O modal do quadro não protege nada que um cartão não proteja: o que
importa é o segredo aparecer uma vez e a pessoa confirmar que o guardou, e isso cabe na página. A issue
proíbe modal para gestão, e criar uma credencial é o centro da gestão desta tela.

- **Criar:** em `/applications/new`, o `201` troca o formulário por um cartão **"Aplicação criada"** com o
  mesmo conteúdo do quadro: "Entregue estes dados ao responsável pelo **{nome}** por um canal seguro.",
  Client ID, Segredo e Endpoint de token, cada um com "Copiar"; o aviso amarelo "O segredo aparece só
  agora…"; a caixa "Guardei o segredo em um local seguro"; e "Ir para a aplicação" (azul), desabilitado até
  a caixa ser marcada, que leva a `/applications/:id` (aba Agentes, `recemCriada`).
- **"Gerar novo segredo":** o cartão "Credencial do Buteco" abre, **dentro dele**, abaixo da faixa, o
  formulário da senha (como "Adicionar agente"); o `201` troca o formulário pelo bloco **"Segredo novo"**
  (o valor, "Copiar", o aviso "O segredo aparece só agora… O anterior continua valendo até {data}." quando há
  substituído, a caixa "Guardei o segredo…" e "Concluir"). A tabela abaixo já mostra o novo como Ativo e o
  anterior como Substituído.
- **Onde o segredo vive:** só no `useState` do componente que fez a chamada — a página Nova, e a página do
  detalhe (não a aba, para sobreviver à troca de aba). Some ao concluir, ao sair da página e ao recarregar.
  - As duas chamadas são `async` comuns pelo `useCallerPassword`, **não** `useMutation`: o
    `MutationCache` do TanStack guarda o `data` da mutação até o `gcTime`, com o segredo dentro.
  - Nenhum `setQueryData` com a resposta (ela tem o segredo); depois do `201`, só `invalidateQueries` da
    lista e do detalhe.
  - Nada em `navigate(…, { state })`, `sessionStorage` ou `localStorage`.
  - **Guardas** (convenção 15, cada um reprovado antes contra o defeito): (1) depois de criar e de gerar,
    `queryClient.getMutationCache()` e `getQueryCache()` serializados não contêm `btc_sk_` — reprova com
    `useMutation`, e com `setQueryData` da resposta; (2) `sessionStorage`, `localStorage` e
    `window.history.state` não contêm `btc_sk_` — reprova com o segredo no `state` do `navigate`; (3) depois
    de "Ir para a aplicação" e "voltar" do navegador, a página Nova não mostra o segredo.
- **Sair sem marcar a caixa:** não se bloqueia (Q17). O texto avisa, e perder o segredo tem conserto (gerar
  outro e revogar o perdido).
- *Descartado:* o cartão no topo do detalhe recém-criado (o exemplo do pedido). Para chegar lá o segredo
  precisa atravessar a navegação, e os quatro lugares proibidos são justamente os que atravessam; sobraria um
  guardador em memória fora do router (contexto ou módulo), um segundo dono do segredo a esvaziar e a guardar
  em teste. No cartão na própria página Nova, o segredo morre com o componente que o recebeu. O custo: a URL
  fica `/applications/new` enquanto o cartão está na tela, e recarregar ali mostra o formulário vazio (o
  segredo se perde, como se perderia em qualquer opção; o `409` de nome impede a criação duplicada).
- *Descartado também:* manter o modal como exceção. Abriria a porta que a issue fecha, sem ganho de
  segurança.

### D4. (d) Rotação e revogação na aba "Credencial e conexão"

O contrato (#186): estados derivados `ativo`, `substituido`, `expirado`, `revogado`; 365 dias de validade; 7
dias de sobreposição; no máximo 3 vivos (não revogados e não vencidos); o token já emitido vale até 5 min
depois de revogar.

| estado | selo | Válido até | Último uso | ação |
|---|---|---|---|---|
| `ativo` | verde "Ativo" | a data | "há …" ou "Nunca usado" | "Revogar" |
| `substituido` | amarelo "Substituído" | a data (≤ 7 dias), que é quando ele para | idem | "Revogar agora" |
| `revogado` | cinza "Revogado" | a data original, como o quadro | idem | — |
| `expirado` | cinza "Expirado" | a data | idem | — |

- **Confirmação na linha, sem modal:** "Revogar"/"Revogar agora" põe, logo abaixo da linha, uma faixa
  (`ConfirmBar` dentro da tabela, numa linha de largura inteira) com o texto, o botão vermelho e "Cancelar";
  a falha aparece na mesma faixa. Enquanto ela está aberta, "Gerar novo segredo" e as outras ações da tabela
  ficam desabilitados (uma ação por vez, como a aba Agentes).
  - "Revogar": "Revogar o segredo btc_sk_••••{sufixo}? A aplicação deixa de obter token com ele na hora; um
    token já emitido continua valendo por até 5 minutos." — e, se for o único vivo: "Ela fica sem segredo
    válido até você gerar outro."
  - "Revogar agora": "Revogar agora o segredo btc_sk_••••{sufixo}? Ele valeria até {data}. A aplicação deixa
    de obter token com ele na hora; um token já emitido continua valendo por até 5 minutos."
- **Limite:** com 3 vivos, "Gerar novo segredo" fica **desabilitado** com a dica "Já há 3 segredos válidos.
  Revogue um para gerar outro." (o papel pode, o estado impede: `panel-roles`). O `409` do limite continua
  tratado (corrida entre duas abas).
- **Sem segredo vivo** (todos revogados ou vencidos, ou logo depois de reativar): aviso amarelo no cartão,
  "Nenhum segredo válido: a aplicação não consegue obter token. Gere um novo segredo."
- **Desativada:** "Gerar novo segredo" desabilitado com "Reative a aplicação para gerar um segredo."; todos
  os segredos aparecem revogados e sem ação.
- **Rodapé** e o `curl` como o quadro.
- *Descartado:* esconder "Revogar" do único segredo vivo (o Admin que suspeita de vazamento precisa cortar
  antes de gerar outro); mostrar "Revogado em …" no lugar de "Válido até" (o quadro escolheu a data original;
  fica para o dono, se quiser, no desenho dos estados).

### D5. (e) A aba Agentes

O padrão do quadro e da issue: "Adicionar agente" abre o formulário **dentro do cartão**, "Editar" troca só
a linha por caixas com "Salvar"/"Cancelar", e uma ação por vez desabilita as outras ("Adicionar agente"
desabilitado durante a edição; "Editar"/"Remover" desabilitados durante a adição).

- **Remover, com confirmação na linha** (o quadro remove direto): a faixa abaixo da linha, "Remover o acesso
  da aplicação a {agente}? A próxima chamada dela a este agente é recusada; as tasks já abertas não mudam.",
  "Remover" vermelho e "Cancelar".
- **Agente inativo na lista:** selo cinza "Inativo" ao lado do nome; as operações continuam editáveis (o
  backend aceita). *Descartado:* uma frase na linha (repete o aviso do formulário de adição em cada linha).
- **Aplicação desativada:** a grade continua editável — o vínculo sobrevive à desativação e o backend aceita
  liberar e editar (`external-application-agents`); o cartão ganha, abaixo da faixa, o aviso "Aplicação
  desativada: as chamadas A2A dela são recusadas. Os agentes liberados ficam guardados e voltam a valer ao
  reativar." **Nada fica bloqueado nesta aba** (Q8).
- **`409` de já liberado** (outro Admin liberou entre abrir e salvar): o `title` no formulário, a grade se
  recarrega (o agente aparece nela e sai do seletor), o formulário fica aberto com o seletor limpo.
- **`400 agenteId`** (agente apagado entre abrir e salvar): "Agente não encontrado." no seletor, catálogo
  recarregado.
- **`404` de vínculo retirado por outro Admin:** no "Salvar" da edição, aviso vermelho no cartão "Agente não
  liberado para esta aplicação. A lista foi atualizada." e a grade se recarrega; no "Remover", ver Q9.
- **Seletor sem agente disponível:** "Todos os agentes já estão liberados para esta aplicação." no lugar do
  seletor, "Adicionar" desabilitado.
- O padrão das caixas na adição: "Enviar mensagem" e "Consultar task" marcadas, como o quadro.

### D6. Editar nome e descrição

**Recomendação:** `/applications/:id/edit`, com o formulário da Nova sem a seção "Credencial" e sem a senha,
título "Editar aplicação", "Salvar" azul e "Cancelar" — como agentes, MCP, bases e canais editam. O cabeçalho
do detalhe passa a mostrar a descrição abaixo do nome (o quadro não a mostra, e sem isso a descrição só
aparece na lista). *Descartado:* editar no próprio cabeçalho (padrão novo, sem quadro, e o "sem modal" já se
cumpre com a página).

### D7. (f) O card "Aplicações com acesso"

Na Visão geral do agente, coluna da direita, logo abaixo de "Protocolo A2A" (o quadro põe os dois lado a
lado acima das abas, mas diz "Conteúdo das abas atuais do agente, sem mudança neste protótipo"; no painel, os
endereços já moram na Visão geral). Título "Aplicações com acesso".

- **Admin** (`GET /agents/{id}/applications`): faixa com "Gerenciar" (→ `/applications`); cada linha com o
  nome (→ `/applications/:id`), o selo "Desativada", as operações por extenso separadas por " · " e "aplicação
  usada há …"/"aplicação nunca usada".
- **Operador e Leitor** (`…/applications/count`): "Usado por **{ativas} aplicações externas ativas** ({total}
  no total).", sem "Gerenciar", sem nomes nem links. Singular: "1 aplicação externa ativa".
- **Rodapé**, para os três papéis, só com `ativas > 0`: "Desativar este agente afeta **{N} aplicações
  ativas**: as tasks que elas abrirem nele passam a terminar recusadas." (singular "1 aplicação ativa").
- **Nenhuma** (`total = 0`): Admin, "Nenhuma aplicação externa tem acesso a este agente." com "Gerenciar";
  Operador e Leitor, "Nenhuma aplicação externa usa este agente." Sem rodapé.
- **Só desativadas** (`total > 0`, `ativas = 0`): Operador e Leitor, "Usado por {total} aplicações externas,
  todas desativadas."; Admin, a lista com os selos. Sem rodapé.
- **Carregando e erro** no próprio card ("Não foi possível carregar as aplicações." com "Tentar de novo"),
  sem derrubar a página.
- **A confirmação de desativar o agente** (o modal que já existe): com `ativas > 0`, ganha a mesma frase do
  rodapé. "Desativar" fica no cabeçalho, e quem desativa estando noutra aba não vê o card (Q11).

### D8. (h) Validação visual

Regra da M1: na homologação, sem prender o PR. O checklist vai igual no `tasks.md`, no `02` e no PR, quadro a
quadro, claro e escuro, com os estados do (g) que o dono desenhar.

### D9. Testes

`fakeAccountApi` (o `fetch` falso por caminho) com fixtures de aplicação, segredo e vínculo; o `sessionFetch`
real por cima, para o `401` com corpo não renovar. Os três guardas do segredo (D3). Um guarda da guarda de
rota: o Operador em `/applications/:id` vê a tela de acesso negado e **nenhum** `GET /applications/…` é feito
(reprova sem a rota na tabela). Um guarda do card: o Leitor nunca chama `GET /agents/{id}/applications`
(reprova com a escolha da rota fora do papel).

### D10. O que o apply mudou (convenção 9, 10/11/2026)

1. **O detalhe não traz `agentes` (premissa falsa, achada na conferência no navegador).** A spec viva
   `external-applications` diz que `GET /applications/{id}` devolve "os campos da lista"; desde a #187 a
   lista traz `agentes`, e o `ApplicationDetail` do `apps/api` não
   (`ExternalApplicationResponses.cs` l. 42–53). O contador da aba lia `app.agentes.length` como reserva,
   e a tela quebrava ("Cannot read properties of undefined (reading 'length')"). A fixture dos testes
   seguia a spec, e não o fio; por isso a suíte passava. **Corrigido sem mudar contrato:** o tipo do
   detalhe sem `agentes`, o contador pela lista de `GET /applications/{id}/agents`, a fixture no formato
   do fio (com o código antigo, 46 casos reprovaram com o mesmo erro do navegador), e uma delta
   `MODIFIED` em `external-applications` que acerta a frase. Não muda comportamento do backend nem
   reduz cobertura: seguiu e foi registrado.
2. **"Há N min".** O painel só tinha o "hoje, HH:MM" de `panelDates`; o quadro usa distância ("há 3
   min", "há 41 dias", "há 1 ano"). Entrou `formatAgo`, na feature, com os casos dele.
3. **O singular do "todas desativadas".** O quadro só desenhava o plural; com `total = 1` e `ativas = 0`
   a tela diz "Usado por 1 aplicação externa, desativada.". Aceito pelo dono, e já no quadro.
4. **O pronome do rodapé no singular.** "Desativar este agente afeta **1 aplicação ativa**: as tasks que
   *elas* abrirem…", visto na conferência. O dono corrigiu o quadro na versão 68: "…afeta 1 aplicação
   ativa: as tasks que ela abrir nele passam a terminar recusadas.", no card e no modal; a tela, a spec e
   os casos do singular seguem a versão 68.
5. **`Textarea` com `autosize`** quebra no jsdom (o `Autosize` do Mantine lê `document.fonts`); a
   descrição usa `rows={3}`.
6. **O Prettier** reformatou os arquivos novos depois da primeira rodada dos guardas; as onze
   rodadas dos dez guardas foram refeitas contra a árvore final, com as âncoras de 6.6 e 8.5 refeitas.
7. **A medição (convenção 18):** produção +2.708 contra ~1.620 (+67 %). Dois motivos: o Prettier (largura
   100) quebra as props do Mantine em várias linhas, e as âncoras não eram formatadas assim; e as duas abas
   (585 e 421 linhas, contra ~330 e ~300) concentram a regra de uma ação por vez. A régua de tela deve
   medir a âncora formatada e contar cada tabela com edição e confirmação na linha à parte (~250–300
   linhas cada).
8. **A lição da fixture:** a do detalhe seguia a spec, e não a resposta real; 46 casos passavam com o
   defeito. Todas as fixtures novas foram conferidas contra o `apps/api` local (formatos iguais; só
   `descricao: null` e o formato de data foram acrescentados).
9. **Fora da change ([#266](https://github.com/aldovrando-oliveira/buteco-agentes/issues/266)):** o `password-reset-link` local não leu o `Identity:PanelBaseUrl` do
   `appsettings.Development.json` com `dotnet run --launch-profile http -- password-reset-link`
   ("a url do painel não está configurada"; `docs/development.md` l. 551 diz que basta isso); funcionou
   com `DOTNET_ENVIRONMENT=Development` e as duas URLs na linha de comando. Não investigado aqui.

## Risks / Trade-offs

- **Recarregar com o cartão da credencial perde o segredo** → por desenho; o aviso diz que é a única vez, e a
  aba Credencial tem o conserto.
- **Uma ação por vez na tabela de segredos** é mais rígido que o necessário → o mesmo padrão da aba Agentes,
  e a mesma regra para quem aprende uma tela.
- **A grade editável com a aplicação desativada** pode parecer estranha → o aviso no cartão explica, e é o
  que o backend faz (liberar antes de reativar).

## (g) Os estados de tela sem quadro

No formato da #220 (f): por quadro, o que já está desenhado e o que falta, com o texto proposto. **Falta** =
para o dono desenhar antes das specs. *Todos os "falta" abaixo foram desenhados pelo dono na versão 65; ver
"A versão 65". A tabela fica como registro do que foi pedido.*

**`AplicacoesLista`**

| item | estado | situação | o que a tela mostra |
|---|---|---|---|
| a | `lista` (3 ativas, 1 desativada) | desenhado | como o quadro |
| b | `vazia` | falta | "Nenhuma aplicação externa cadastrada." + "Cadastre uma aplicação para um sistema de fora chamar agentes pelo A2A." + "Nova aplicação" |
| c | `busca-sem-resultado` | falta | "Nenhuma aplicação encontrada para "{busca}"." (com o filtro: "…entre as {ativas\|desativadas}.") + "Limpar busca" |
| d | `carregando` | falta | linhas fantasmas da tabela |
| e | `erro` | falta | "Não foi possível carregar as aplicações." + "Tentar de novo" |
| f | aplicação sem agente na coluna "Agentes liberados" | falta | "Nenhum" em cinza (o quadro não tem linha sem chip) |
| g | rodapé no singular | falta | "1 aplicação · …" |

**`AplicacaoNova`**

| item | estado | situação | o que a tela mostra |
|---|---|---|---|
| a | `formulario` | desenhado | sem a URL antes de criar (Q4) |
| b | `senha` | falta | seção "Confirmação": "Sua senha", dica "Criar a aplicação gera uma credencial. Confirme com a sua senha." |
| c | `nome-vazio`, `nome-longo`, `descricao-longa` | falta | "Informe o nome."; "O nome tem no máximo 80 caracteres."; "A descrição tem no máximo 280 caracteres." |
| d | `nome-repetido` (`409`) | falta | "Já existe uma aplicação com este nome." no campo Nome |
| e | `senha-vazia`, `senha-errada` (`401`) | falta | "Informe a sua senha."; "Senha inválida." no campo, vazio |
| f | `bloqueado` (`423`) | falta | aviso amarelo "Muitas tentativas sem sucesso… pausada por N minutos.", botão desabilitado |
| g | `enviando` | falta | "Criar aplicação" carregando |

**`AplicacaoCredencial`**

| item | estado | situação | o que a tela mostra |
|---|---|---|---|
| a | `criada` | desenhado **como modal** | redesenhar como cartão no lugar do formulário, mesma ordem e textos (D3) |
| b | `copiado` | falta | "Copiado" no botão por 2 s |
| c | `segredo-novo` (gerar) | falta | o bloco "Segredo novo" dentro do cartão "Credencial do Buteco" (D3) |

**`AplicacaoDetalhe`, cabeçalho**

| item | estado | situação | o que a tela mostra |
|---|---|---|---|
| a | ativa | desenhado | + a descrição abaixo do nome (D6) |
| b | `confirmar-desativar` | falta | faixa solta acima das abas: "Desativar {nome}? Todos os segredos dela são revogados e as chamadas A2A são recusadas na hora. Os agentes liberados ficam guardados. Ao reativar, você precisa gerar um segredo novo." + "Desativar" vermelho + "Cancelar" |
| c | `desativada` | falta | selo "Desativada", "Desativada em {data}", "Reativar" no lugar de "Desativar" |
| d | `reativada` | falta | aviso "Aplicação reativada. Gere um segredo para ela voltar a obter token." |
| e | `carregando`, `erro`, `nao-encontrada` | falta | "Aplicação não encontrada." + "Voltar para Aplicações externas" |

**`AplicacaoDetalhe`, aba "Credencial e conexão"**

| item | estado | situação | o que a tela mostra |
|---|---|---|---|
| a | os quatro estados de segredo | desenhado | como o quadro |
| b | `gerar-senha` | falta | formulário dentro do cartão: "Sua senha", "Gerar segredo" azul, "Cancelar" |
| c | `gerar-senha-errada`, `gerar-bloqueado` | falta | como na Nova (e, f) |
| d | `limite` (3 vivos) | falta | botão desabilitado + "Já há 3 segredos válidos. Revogue um para gerar outro."; e o `409` da corrida no formulário |
| e | `segredo-novo` | falta | ver `AplicacaoCredencial` c |
| f | `confirmar-revogar`, `confirmar-revogar-agora`, `revogar-unico-vivo` | falta | as faixas do D4 |
| g | `sem-segredo-vivo` | falta | "Nenhum segredo válido: a aplicação não consegue obter token. Gere um novo segredo." |
| h | `desativada` | falta | todos "Revogado", botão desabilitado + "Reative a aplicação para gerar um segredo." |

**`AplicacaoDetalhe` / `AplicacaoDetalheAdicionar` / `AplicacaoDetalheEditar`, aba "Agentes"**

| item | estado | situação | o que a tela mostra |
|---|---|---|---|
| a | `lista`, `adicionando`, `editando`, `recemCriada`, sem operação | desenhado | como os quadros |
| b | `agente-inativo` na grade | falta | selo "Inativo" ao lado do nome |
| c | `aplicacao-desativada` | falta | o aviso do D5 abaixo da faixa |
| d | `confirmar-remover` | falta | a faixa do D5 abaixo da linha |
| e | `adicionar-ja-liberado` (`409`), `adicionar-agente-sumiu` (`400`) | falta | os textos do D5 no formulário |
| f | `editar-vinculo-retirado` (`404`) | falta | aviso vermelho no cartão (D5) |
| g | `sem-agente-disponivel` | falta | "Todos os agentes já estão liberados para esta aplicação." |
| h | `carregando`, `erro` da grade e do catálogo | falta | linhas fantasmas; "Não foi possível carregar os agentes." + "Tentar de novo" |

**`AgenteDetalhe`, card "Aplicações com acesso"**

| item | estado | situação | o que a tela mostra |
|---|---|---|---|
| a | `papel=admin` (lista), `papel=operador\|leitor` (contagem), rodapé | desenhado | como o quadro |
| b | `nenhuma` (os dois papéis) | falta | os textos do D7 |
| c | `so-desativadas` | falta | os textos do D7 |
| d | singulares | falta | "1 aplicação externa ativa", "1 aplicação ativa" |
| e | `carregando`, `erro` | falta | no card |
| f | lugar do card na Visão geral | falta | abaixo de "Protocolo A2A" (D7) |
| g | a frase no modal de desativar o agente | falta | Q11 |

**Editar aplicação** — sem quadro nenhum: o formulário da Nova sem "Credencial" nem senha (D6), com
`nome-vazio`, `nome-repetido` e `salvando`.

**Menu** — desenhado ("Aplicações externas" entre "Usuários" e "Identidade").

## (i) Tamanho (convenção 18)

Âncoras medidas, só `apps/frontend/src`, por `git show --numstat`:

| âncora | produção | teste | casos novos | duplos |
|---|---|---|---|---|
| #185 `24737bf` | 52 arq. +1.157 −324 | 36 arq. +1.544 −47 | 88 | 6 arq. +77 −12 |
| #217 `649e582` | 36 arq. +1.317 −290 | 35 arq. +2.091 −313 | 102 | — |

A #185 projetou ~950 de produção e ~106 casos; mediu +1.157 (+22 %) e 88 (−17 %).

| componente | arq. prod | linhas prod | casos |
|---|---|---|---|
| tipos, cliente das 14 chamadas, chaves de consulta, textos | 4 | ~260 | ~10 |
| menu, rotas, guarda | 3 | ~30 | ~5 |
| lista (busca com espera, filtro, chips, último uso relativo, estados) | 2 | ~180 | ~12 |
| nova, editar e o cartão da credencial | 4 | ~260 | ~16 |
| detalhe: cabeçalho, abas, desativar/reativar, 404 | 2 | ~150 | ~10 |
| Credencial e conexão: segredos, gerar com senha, segredo novo, revogar na linha, limite, sem vivo, desativada | 3 | ~300 | ~18 |
| Agentes: grade, adicionar, editar, remover na linha, inativo, desativada, 409/400/404 | 3 | ~330 | ~20 |
| card do agente e a frase no modal | 2 | ~110 | ~10 |
| guardas do segredo, da rota e do card | — | — | ~5 |
| **total** | **~23** | **~1.620** (faixa 1.400–1.900) | **~106** |

Teste: pela razão da #185 (1,33 linha por linha de produção), **~2.150** (faixa 1.900–2.500), em ~14
arquivos. **Duplos ~200**: fixtures de aplicação/segredo/vínculo e os `render` das páginas (o `fetch` falso
já existe). Docs ~+120.

**Cabe numa change**: ~20 % acima da #217, que foi uma só. **Recomendo não dividir** (Q13). Se o dono
preferir, sem nada meio-aberto: **188a** — menu, lista, nova, credencial criada, detalhe com "Credencial e
conexão", editar; **188b** — a aba Agentes e o card do agente. A 188a sozinha cria aplicações que não alcançam
nada (nascem sem agentes, A2A `404`) até a 188b, ou até o `curl` do `docs/deployment.md`.

## (j) Deploy

- **Só a imagem do `frontend`**, sem migration nem variável. A #186 e a #187 já podem estar no ar antes
  (`docs/deployment.md` diz que a ordem com a #188 é livre); se forem juntas, a ordem de sempre: `migrator`,
  `api`, depois `frontend`.
- **Recomendação:** as três juntas na homologação da M2, e o `frontend` por último. Separadas também funciona.
- **Voltar atrás só o `frontend`:** seguro. Nada se grava diferente; aplicações, segredos e vínculos ficam no
  banco e continuam valendo; o Admin volta ao roteiro por `curl` ("Liberar um agente sem a tela"), que fica no
  documento como alternativa. O card do agente some, sem efeito no backend.
- **Voltar atrás o backend com o `frontend` da #188:** o menu aparece e as rotas não existem — a lista dá o
  erro de carga e o card do agente, o erro do card. Por isso o recuo do backend leva o `frontend` junto, como
  na #185.

## (k) Premissas falsas (convenção 9)

1. **Corpo da #188, l. 9:** "Credencial criada: client ID, segredo e endpoint mostrados uma vez". Só o
   **segredo** aparece uma vez; o client ID e o endpoint ficam em "Credencial e conexão" (spec
   `external-applications`, "Detalhe da aplicação"; quadro `AplicacaoDetalhe`). Texto: **não é parada.**
2. **Corpo, l. 20 e l. 39:** "o padrão sem modais para gestão" e "como o card de documentos da base de
   conhecimento". O painel tem modais de gestão hoje, inclusive no card citado: "Adicionar documento" abre o
   `KnowledgeDocumentModal` (`KnowledgeBaseDetailPage.tsx` l. 662), e há `KnowledgeBaseLinkModal`,
   `KnowledgeBaseDeleteModal`, `FolderPickerModal` e as confirmações de desativar em `AgentDetailPage.tsx`
   l. 343, `McpServerDetailPage.tsx` e `ChannelDetailPage.tsx`. A regra vale para as telas novas; o card de
   documentos serve de modelo **visual** (faixa cinza, botão pequeno). Não muda o que esta change faz: **não é
   parada.** A frase nova do D7 entra num modal que já existe (Q11).
3. **Corpo, l. 12 e l. 42:** "só 'Usado por N aplicações externas'". O quadro (versão 63 em diante) e a #187
   (Q9) dizem "Usado por N aplicações externas ativas (M no total)". O quadro vence: **não é parada.**
4. **Comentário de 24/10/2026 01:40:** "inclusive o card 'Aplicações com acesso'… só aparecem para Admin".
   Desfeito pelo comentário das 01:57 e pelo corpo. Histórico: **não é parada.**
5. **Design da #186, l. 235 e l. 452:** a confirmação de desativar "com o prazo de 5 min" ("e o mesmo para
   desativar"). Falsa desde a #187 (Q6): desativada, a aplicação recebe `404` na A2A **na requisição
   seguinte**, mesmo com token no prazo (spec `external-applications`, "Desativar corta a A2A na hora"). O
   texto de desativar fica sem o prazo (g, cabeçalho b). **Não é parada.**
6. **Spec viva `frontend-app-shell` l. 166 e l. 184–186:** "SHALL NOT ter o item 'Aplicações externas'". Deixa
   de valer com esta change, como previsto nela: delta `MODIFIED`.
7. **Quadro `AplicacaoNova`:** o endpoint `https://[URL_PUBLICA]/connect/token` antes de criar. O backend não o
   serve antes do `201`, e o painel não monta endereço pela origem da página (`agent-catalog-ui`, "Endereços
   A2A"). Q4. **Não é parada.**
8. **Quadros `AplicacaoDetalhe`/`Adicionar`/`Editar`:** "Remover" sem confirmação. A issue pede confirmação na
   linha: estado a desenhar (g). **Não é parada.**
9. **Quadro `AgenteDetalhe`:** o card "Endereços A2A" com "Autenticação declarada", e as abas "Configuração,
   Servidores MCP…". O painel tem "Protocolo A2A" na Visão geral e as abas "Visão geral, Ferramentas,
   Conhecimento, Delegações, Insights"; o próprio quadro avisa que é esquemático. Q10. **Não é parada.**
10. **Script do `AplicacaoDetalhe`:** ainda carrega os dados da aba Atividade (`stTasks`, `recusas`), sem
    marcação. Já registrado na #187 (f); fora (#199).

**Nenhuma muda comportamento ou contrato.** Nenhuma parada.

## Baseline

Worktree limpo `../buteco-agentes-188-baseline`, em `2b93837` (0 arquivos alterados no início), pelo
`~/.cache/buteco-agents/bin/suite-188.sh` (cópia do `suite-185.sh`, com o lock e `MAXWORKERS`; só o cabeçalho
muda), `MAXWORKERS` padrão, broker de pé (`PAUSAR_RABBIT=0`). `podman ps` no início: `postgres_1` e
`rabbitmq_1`. Saídas em `~/.cache/buteco-agents/188-baseline/`; o relatório por arquivo + nome em
`frontend-relatorio.tsv` (2.493 linhas).

| suíte | resultado | duração | CPU mínima | veredito |
|---|---|---|---|---|
| `frontend` | 190 arquivos, 2.493 aprovados | 219 s | 60 | APROVADA (verde conta com a CPU caindo) |

**Por que só o `frontend`:** é o único app tocado. **Fora:** `api` (a #186 e a #187 servem as rotas, e a #188
não muda rota nem resposta; se o dono decidir algo que peça backend, a `api` entra com o motivo), `inbox`,
`connectors`, `rt-inbox` e `rt-connectors` (nem login do emissor nem rota deles mudam), `rt-crossapp` (acordo
de banco api↔workers, sem tela), `workers` (sem rota HTTP).

## A versão 65 (`1794278589-23e3`, lida em 09/11/2026)

Lida pela ferramenta Artifact nos oito quadros (`AplicacoesLista`, `AplicacaoNova`, `AplicacaoCredencial`,
`AplicacaoDetalhe`, `AplicacaoDetalheAdicionar`, `AplicacaoDetalheEditar`, `AplicacaoEditar`, novo, e
`AgenteDetalhe`). Cada estado do (g) tem prop:

- `AplicacoesLista`, `carga`: `lista`, `uma`, `vazia`, `busca-sem-resultado` ("erp" com Ativas), `carregando`, `erro`; "Nenhum" no "Chatbot do site";
- `AplicacaoNova`, `estado`: `formulario`, `nome-vazio`, `nome-longo`, `descricao-longa`, `nome-repetido`, `senha-vazia`, `senha-errada`, `bloqueado`, `enviando` ("Criando…"); a seção "Confirmação"; a frase da Q4;
- `AplicacaoCredencial`: o cartão "Aplicação criada" em `/applications/new`, sem modal; `copiado` (`nenhum`, `client`, `segredo`, `endpoint`) e `guardei`;
- `AplicacaoDetalhe`: `aplicacao` (`ativa`, `confirmar-desativar`, `desativada`, `reativada`, `carregando`, `erro`, `nao-encontrada`), `agentesExtra` (`agente-inativo`, `confirmar-remover`, `adicionar-ja-liberado`, `adicionar-agente-sumiu`, `sem-agente-disponivel`, `editar-vinculo-retirado`, `carregando`, `erro`) e `credencial` (`gerar-senha`, `gerar-senha-errada`, `gerar-bloqueado`, `gerar-409-limite`, `segredo-novo`, `limite`, `confirmar-revogar`, `confirmar-revogar-agora`, `revogar-unico-vivo`, `sem-segredo-vivo`);
- `AplicacaoEditar`, `estado`: `formulario`, `nome-vazio`, `nome-repetido`, `salvando`, com "O client ID e os segredos não mudam ao editar.";
- `AgenteDetalhe`: o card na Visão geral abaixo de "Protocolo A2A", sem "Autenticação declarada"; `aplicacoes` (`lista`, `uma`, `nenhuma`, `so-desativadas`, `carregando`, `erro`) × `papel`; `modalDesativar`.

**Conferência contra o contrato: todos os dados existem** (D2). **Divergências de texto, sem parada**
(decisão do dono: a tela usa o `title` do servidor e o texto atual do modal):

1. **`409` de já liberado.** O quadro diz "Este agente já está liberado para **esta** aplicação."; o servidor,
   "Este agente já está liberado para **a** aplicação." (`ExternalApplicationAgent.cs` l. 71,
   `AlreadyGrantedMessage`). A tela mostra o `title`.
2. **Modal de desativar o agente.** O quadro abre com "Desativar Atendimento Varejo?" e um marcador "[O texto
   atual do modal… continua aqui]". A tela mantém o título "Confirmar desativação" e o texto de hoje
   (`AgentDetailPage.tsx` l. 343–347) e acrescenta só a frase "afeta N".
3. **Bloqueio.** `bloqueado` e `gerar-bloqueado` escrevem "5 minutos" fixo; a tela usa o N do `Retry-After`
   (`lockedText`, como na #217 e na #220).
4. **"O segredo atual continua valendo por 7 dias"** (`gerar-senha`). Pelo contrato, o anterior vale
   `min(validoAte, agora + 7 dias)`: o que vence em 2 dias não ganha prazo (spec `external-applications`, "O
   anterior que vence antes não ganha prazo"), e sem segredo vivo não há "atual". A tela diz "por **até** 7
   dias" e só mostra a frase quando há segredo vivo (convenção 13: o texto não afirma mais do que se sabe).
   Achada nesta leitura; vai ao dono no relatório.

## Decisões do dono (09/11/2026)

- **Q1 a Q18 aceitas como recomendadas.**
- **Q2 e Q3:** os três guardas do D3, cada um reprovado antes contra o seu defeito (`tasks.md`, 4.5 a 4.7).
- **Q6:** conferido em `2b93837`: `ListAsync(string? busca, string? estado, …)`
  (`ExternalApplicationEndpoints.cs` l. 65–97) liga os dois parâmetros da query; `estado` aceita `ativas` e
  `desativadas` (outro valor, `400`), e `busca` filtra nome normalizado ou client ID, sem maiúscula. **Não é
  achado.**
- **Q9:** o `404` do `DELETE` vira sucesso e a grade se recarrega, com um caso próprio, que é guarda (`tasks.md`
  8.6).
- **Q17:** não bloquear a saída; o texto do cartão avisa.
- **Os guardas pedidos**, com o defeito nomeado no `tasks.md`: os três do segredo (4.5–4.7), "Ir para a
  aplicação" sem a caixa (4.8), a confirmação de remover pulada (8.5), "Gerar novo segredo" com 3 vivos (6.5) e
  a lista do card para Operador ou Leitor (9.4); e mais três: a guarda de rota (2.4), a confirmação de revogar
  pulada (6.6) e o `404` do `DELETE` (8.6).
- **Onde o card entra no código:** o `AgentOverviewTab` recebe o card por uma prop, montado pelo
  `AgentDetailPage`; assim os testes do `AgentOverviewTab` não passam a precisar de `QueryClient`, e os do
  `AgentDetailPage` só ganham o `vi.mock` do cliente (arranjo, sem asserção nova; `tasks.md` 0.3).

## Decisões (as recomendações, aceitas)

| # | pergunta | recomendação |
|---|---|---|
| Q1 | Menu e rotas | "Aplicações externas" entre Usuários e Identidade, ícone `Plug`; `/applications`, `/new`, `/:id` (`?tab=credencial`), `/:id/edit`, só Admin; aba padrão Agentes (D1) |
| Q2 | Credencial criada | cartão no lugar do formulário em `/applications/new`, "Ir para a aplicação" depois da caixa; sem modal (D3). Alternativa descartada: cartão no topo do detalhe |
| Q3 | Gerar novo segredo | senha e segredo novo dentro do cartão "Credencial do Buteco"; o segredo no estado da página do detalhe (D3) |
| Q4 | O endpoint antes de criar | tirar a URL: "O endpoint de token aparece junto com o client ID, logo depois de criar." Alternativa: `GET /identity` só para a dica |
| Q5 | Os guardas do segredo | os três do D3, cada um reprovado antes contra o defeito (`useMutation`, `setQueryData`, `state` do `navigate`) |
| Q6 | Busca e filtro | no servidor (`?busca` com espera de 300 ms, `?estado`), mantendo a lista anterior enquanto carrega. Alternativa: filtrar no cliente |
| Q7 | Rotação e revogação | a tabela e os textos do D4; "Gerar" desabilitado com 3 vivos e com a aplicação desativada; confirmação na linha |
| Q8 | Aba Agentes com a aplicação desativada | tudo editável, com o aviso no cartão (D5) |
| Q9 | `DELETE` com `404` (outro Admin já retirou) | tratar como concluído: a grade se recarrega e a linha some, sem erro (o estado pedido já vale). Alternativa: mostrar o `title` |
| Q10 | "Autenticação declarada" do quadro do agente | fora desta change; o card "Protocolo A2A" fica como está |
| Q11 | O card do agente e o modal de desativar | card na Visão geral abaixo de "Protocolo A2A"; a frase "afeta N aplicações ativas" também no modal de desativar quando `ativas > 0` (D7) |
| Q12 | Textos do card sem aplicação e só com desativadas | os do D7 |
| Q13 | Tamanho | uma change (~1.620 prod, ~106 casos); divisão 188a/188b se o dono preferir (i) |
| Q14 | Deploy | só o `frontend`; as três juntas na homologação da M2; recuo só do `frontend` seguro, recuo do backend leva o `frontend` (j) |
| Q15 | Editar nome e descrição | página `/applications/:id/edit` com o formulário da Nova; a descrição no cabeçalho do detalhe (D6) |
| Q16 | Desativar a aplicação | confirmação solta acima das abas, sem o prazo de 5 min (premissa 5); Reativar sem confirmação |
| Q17 | Sair do cartão da credencial sem marcar a caixa | não bloquear; o aviso basta |
| Q18 | Suítes | só `frontend` (Baseline) |
