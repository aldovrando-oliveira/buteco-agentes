## Context

`apps/api` já expõe o catálogo de servidores MCP por completo (change
`backend-mcp-catalogo-vinculo`, arquivada; spec `mcp-server-catalog`):
`POST/GET/PUT /mcp-servers`, `GET /mcp-servers/{id}`,
`POST /mcp-servers/{id}/activate|deactivate`, `POST /mcp-servers/test` e
`POST /mcp-servers/{id}/test`. Não existe nenhuma interface para esse
catálogo em `apps/frontend` hoje.

`apps/frontend` já tem um precedente direto e recente para esse tipo de
tela: `features/agents/` (`agent-catalog-ui`) — CRUD completo com Mantine
(`@mantine/form`, `@mantine/notifications`, `@mantine/hooks`),
`@tanstack/react-query` para cache/mutations, roteamento via `react-router`
dentro de um único `AppShell`, e um cliente HTTP fino (`request<T>` em
`agentsApi.ts`) que já normaliza erros de validação (400) em
`ApiError.problem.errors`. Esta change replica esse padrão para
`McpServer`, sem introduzir nenhuma biblioteca nova.

Diferença central em relação a `Agent`: o campo `credential` é write-only
— a API nunca o devolve em nenhuma resposta (`McpServerResponse` não tem
esse campo) — o que exige uma UX específica no formulário de edição
(Decision 2).

## Goals / Non-Goals

**Goals:**
- Listar, cadastrar, editar, ativar/desativar e testar a conexão de
  servidores MCP pela interface, consumindo os endpoints já existentes.
- Manter paridade de padrão com `features/agents/`: mesma estrutura de
  pastas, mesmas bibliotecas, mesmo tratamento de erro de validação, mesmo
  uso de `notifications` para ações de estado (ativar/desativar) e mesma
  disciplina de testes (Vitest + Testing Library).

**Non-Goals:**
- Vínculo agente↔MCP e seleção de tools (`PUT /agents/{id}/mcp-servers`,
  `GET /mcp-servers/{id}/tools`) — fica para a próxima change, que também
  tocará `AgentForm`/`AgentDetailPage`.
- Qualquer mudança em `apps/api` ou `apps/workers`.
- Qualquer mudança em `AgentForm`, `AgentTable` ou `AgentDetailPage`.
- Nenhuma biblioteca nova (sem cliente MCP no browser, sem lib de forms
  além de `@mantine/form` já usada).
- Nenhum aviso adicional na UI quando trocar `AuthType` para `None` limpa
  a credencial persistida no backend — esse é o comportamento documentado
  do próprio catálogo (`mcp-server-catalog`), não uma perda acidental que
  esta fatia precise mitigar.

## Decisions

### Árvore de pastas

```
apps/frontend/src/features/mcp-servers/
├── api/
│   ├── mcpServersApi.ts        # request<T> fino, mesmo padrão de agentsApi.ts
│   ├── useMcpServers.ts        # queries + mutations via @tanstack/react-query
│   └── useMcpServers.test.ts
├── components/
│   ├── McpServerForm.tsx
│   ├── McpServerForm.test.tsx
│   ├── McpServerTable.tsx
│   ├── McpServerTable.test.tsx
│   ├── McpServerDetailCard.tsx
│   ├── McpServerDetailCard.test.tsx
│   ├── ConnectionTestResultAlert.tsx     # ver Decision 1
│   └── ConnectionTestResultAlert.test.tsx
├── pages/
│   ├── McpServerListPage.tsx
│   ├── McpServerListPage.test.tsx
│   ├── McpServerCreatePage.tsx
│   ├── McpServerCreatePage.test.tsx
│   ├── McpServerEditPage.tsx
│   ├── McpServerEditPage.test.tsx
│   ├── McpServerDetailPage.tsx
│   └── McpServerDetailPage.test.tsx
└── types/
    └── mcpServer.ts
```

Nenhum arquivo fora de `features/mcp-servers/` é criado além de
`src/app/router.tsx` (novas rotas) e
`src/components/layout/AppShell.tsx` (novo item de navegação) — ambos
editados, não recriados.

### Decision 1: "Testar conexão" é exibido inline, num componente compartilhado, e trata erro de validação (400) como resultado de falha

**Onde vive**: no `McpServerForm`, um botão "Testar conexão" ao lado dos
botões de submit/cancelar, usando os valores atuais do formulário
(`url`, `authType`, `credential`) via `POST /mcp-servers/test`. Na
`McpServerDetailPage`, um botão dedicado que chama
`POST /mcp-servers/{id}/test`.

**Como o resultado é exibido**: inline, com um `Alert` do Mantine dentro
da própria tela (verde para sucesso, vermelho para falha com o
`message` retornado por `McpConnectionTestResult`) — nunca como toast
(`notifications.show`) nem `Modal`. Diferente de ativar/desativar (que já
muda o estado persistido e por isso usa toast, seguindo o padrão de
`AgentDetailPage`), testar conexão é uma ação exploratória: a pessoa
provavelmente quer ajustar URL/credencial e tentar de novo olhando o
resultado anterior, não ser interrompida por um popup que desaparece.

**Componente compartilhado**: como a mesma lógica de exibição (cor por
`success`, texto do `message`) é usada em dois lugares (`McpServerForm` e
`McpServerDetailPage`), extrai-se um componente de apresentação puro
`ConnectionTestResultAlert` (recebe `{ success, message } | undefined`) em
vez de duplicar o JSX do `Alert` — reuso direto, não abstração
especulativa.

**Erro de validação (400) tratado como falha**: `POST /mcp-servers/test`
pode responder 400 (`ValidationProblemDetails`) quando `url` ou
`authType` estão ausentes/inválidos no formulário — o `McpServerForm` já
impede isso via validação client-side dos mesmos campos antes de habilitar
o cadastro, mas o botão "Testar conexão" não passa pela validação de
`name`/`description` (irrelevantes para o teste). Em vez de um caminho de
erro separado, um 400 nessa chamada é normalizado para o mesmo formato de
`ConnectionTestResultAlert` (`success: false`, mensagem = primeira
mensagem de cada campo em `problem.errors`, concatenadas) — mesma
aparência de uma falha de conectividade, sem introduzir um segundo tipo de
exibição de erro.

**Alternativas consideradas**: toast (rejeitado — perde o resultado
assim que a pessoa começa a editar o campo seguinte); modal (rejeitado —
interrompe a edição, e o resultado de um teste anterior costuma continuar
relevante enquanto se ajusta um campo).

### Decision 2: campo Credential é condicional e nunca pré-preenchido; em branco na edição mantém a credencial atual

O campo Credential só aparece no formulário quando `authType !=
"None"` (mesma cascata de Provider→Model em `AgentForm`: trocar
`AuthType` reage no formulário). No cadastro, é obrigatório sempre que
`authType != "None"` (validação client-side espelha a regra do servidor).
Na edição, o campo nunca é pré-preenchido (a API não devolve
`credential`) e usa um placeholder explícito — "Deixe em branco para
manter a credencial atual" — comunicando que vazio não é o mesmo que
"apagar a credencial".

Isso é possível sem nenhuma lógica extra no frontend porque o backend
real (`UpdateMcpServerCommandHandler`) já implementa exatamente essa
semântica: credencial em branco mantém a persistida quando `authType`
continua exigindo credencial; quando `authType` transiciona para
`"None"`, a credencial persistida é limpa (mesmo que o campo tivesse
algo digitado — ele é omitido do formulário nesse caso, então isso nunca
chega a acontecer via UI). Se `authType != "None"` e nunca houve
credencial salva (ex.: criado como `None` e agora mudando para
`BearerToken` sem informar credencial), a API responde 400 com
`{"credential": [...]}`, que o formulário exibe no campo via o mesmo
`fieldErrorsFrom`/`ApiError.problem.errors` já usado por `AgentForm`.

**Alternativas consideradas**: pré-preencher com um valor mascarado
fictício (ex. `"••••••••"`) — rejeitado, porque cria ambiguidade sobre se
aquele valor mascarado seria enviado de volta como credencial literal.

### Decision 3: McpServerTable tem 4 colunas — Nome, Url, Tipo de autenticação, Estado

Espelha fielmente o nível de detalhe do `AgentTable` real (Nome,
Provider, Model, Estado — não apenas nome+badge). Provider/Model são os
campos que identificam a configuração de um agente; para `McpServer`, os
campos equivalentes são Url e `AuthType`. Detalhe completo (Descrição,
datas) continua reservado à página de detalhe.

### Decision 4: mutations e cache seguem o mesmo padrão de `useAgents.ts`

`useMcpServers.ts` expõe `useMcpServersQuery`, `useMcpServerQuery(id)`,
`useCreateMcpServerMutation`, `useUpdateMcpServerMutation`,
`useActivateMcpServerMutation`, `useDeactivateMcpServerMutation`,
`useTestUnsavedMcpServerConnectionMutation` e
`useTestSavedMcpServerConnectionMutation`. As quatro primeiras seguem
exatamente o padrão de `useAgents.ts` (`setQueryData` +
`invalidateQueries` no `onSuccess`, chave `['mcp-servers']` /
`['mcp-servers', id]`). As duas últimas não tocam o cache do
`QueryClient` — o resultado do teste é efêmero por definição da própria
API (spec `mcp-server-catalog`: "Resultado do teste de conexão não é
persistido") e vive só como estado local do componente que disparou o
teste.

## Risks / Trade-offs

- [Risco] Confundir o resultado do teste anterior com o estado atual do
  formulário depois de uma edição subsequente (ex.: testar, depois trocar
  a URL, e o Alert antigo continuar visível como se fosse sobre a URL
  nova) → Mitigação: qualquer alteração em `url`, `authType` ou
  `credential` depois de um teste limpa o resultado exibido, forçando um
  novo teste antes de mostrar qualquer indicação de sucesso/falha para a
  configuração atual.
- [Risco] Duplicar a validação de `url`/`authType`/`credential` entre
  client-side (para habilitar submit) e o tratamento de 400 do botão
  "Testar conexão" (Decision 1) diverge sutilmente das mensagens da API
  com o tempo → Mitigação: o teste sempre usa a mensagem retornada pela
  API (seja do `McpConnectionTestResult.message`, seja de
  `problem.errors`), nunca uma mensagem client-side própria.
- [Trade-off] Extrair `ConnectionTestResultAlert` como componente
  compartilhado (em vez de duplicar o `Alert` duas vezes) adiciona um
  arquivo a mais em troca de manter a lógica de cor/mensagem em um único
  lugar — aceitável dado que já é usado em exatamente dois pontos com a
  mesma lógica.

## Migration Plan

Nenhuma migração de dados — mudança é puramente aditiva em
`apps/frontend` (novo diretório de feature, duas edições em arquivos
existentes de roteamento/navegação). Nenhum flag de feature necessário:
a rota `/mcp-servers` só aparece no `AppShell` quando este código for
implantado, sem afetar `/agents`. Rollback é reverter o deploy do
frontend — não há estado de servidor para desfazer.

## Open Questions

Nenhuma pendência de produto/negócio identificada — as três decisões que
motivaram este design.md foram confirmadas durante a exploração
(`/opsx:explore`), incluindo o único ponto onde a descrição inicial da
change divergia do código real (nível de detalhe do `McpServerTable`).
