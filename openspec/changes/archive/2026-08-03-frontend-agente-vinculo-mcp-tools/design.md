## Context

O backend expõe hoje dois endpoints que ainda não têm nenhum consumidor
na interface:

- `PUT /agents/{id}/mcp-servers` (change backend-mcp-selecao-tools) —
  substitui o conjunto completo de servidores MCP vinculados a um
  agente. Body: `{ "mcpServers": [ { "mcpServerId": "<guid>",
  "allowedTools": ["<string>"] } ] }`. `mcpServers: null` é erro de
  validação (400); para remover todos os vínculos o cliente deve enviar
  `mcpServers: []` explicitamente. A validação de `allowedTools` é feita
  por um handshake `tools/list` real contra cada `McpServer`
  referenciado, no momento da chamada — se qualquer handshake falhar
  (host inalcançável, credencial rejeitada etc.), a API responde
  `502 Bad Gateway` com um `ProblemDetails` cujo `title` já identifica o
  `McpServerId` (ex.: *"Não foi possível validar as tools do servidor
  MCP {id}."*) e `detail` traz o motivo. Nenhum vínculo do payload é
  aplicado nesse caso — atômico.
- `GET /mcp-servers/{id}/tools` — descoberta ao vivo. Só responde `404`
  se o `McpServer` não existe; qualquer falha de conexão volta como
  `200 OK` com `{ success: false, tools: null, failureReason, message }`
  — não é um erro HTTP nem de rede, é um resultado de negócio.

`AgentResponse.mcpServers` (já presente em `GET /agents/{id}` e
`GET /agents`) tem o shape `{ id, name, allowedTools }[]` — só isso, sem
`isActive`/`url`/`description` do servidor. Para saber o estado de um
servidor vinculado, ou para oferecer os servidores ainda não vinculados
como opção, a UI precisa cruzar esses ids com o catálogo completo já
disponível em `GET /mcp-servers` (consumido por
`features/mcp-servers/api/useMcpServers.ts` → `useMcpServersQuery`).

`features/agents` e `features/mcp-servers` hoje não têm nenhuma
dependência cruzada — cada feature só importa de dentro de si mesma
(além de módulos genéricos de `app/` e Mantine). Essa change introduz o
primeiro import cruzado entre as duas.

Padrões já estabelecidos que esta change segue: páginas dedicadas para
Detail/Edit por entidade (`AgentDetailPage` + `AgentEditPage`,
`McpServerDetailPage` + `McpServerEditPage`); um `request<T>` +
`ApiError`/`ValidationProblemDetails` por feature (`agentsApi.ts`,
`mcpServersApi.ts`), sem cliente HTTP compartilhado; hooks React Query
por feature em `api/useX.ts`; página só renderiza a UI de fato depois
que os dados de que depende terminaram de carregar (`AgentEditPage`
mostra loader até `useAgentQuery` resolver, e só então monta o form com
`initialValues` derivado dos dados já carregados — sem `useEffect` de
sincronização).

## Goals / Non-Goals

**Goals:**
- Permitir, pela interface, vincular um agente a um ou mais servidores
  MCP e escolher quais tools de cada servidor o agente pode usar,
  refletindo o vínculo atual ao abrir a tela.
- Descobrir as tools de um servidor sob demanda (não custear
  `tools/list` de servidores que o usuário não está avaliando).
- Tratar de forma diferenciada dois tipos de falha que parecem
  similares mas exigem UI distinta: falha ao *descobrir* tools de um
  servidor específico (não bloqueia a seleção, é recuperável com retry)
  vs. falha ao *validar/salvar* o vínculo inteiro (bloqueia o submit,
  atômica, precisa identificar o servidor culpado).

**Non-Goals:**
- Vincular servidores MCP durante o cadastro do agente
  (`AgentForm`/`AgentCreatePage` não mudam) — `PUT /agents/{id}/mcp-servers`
  exige um `agentId` existente.
- Indicador de "quantos MCPs vinculados" em `AgentTable`/`AgentListPage`
  — só a página de detalhe e a nova página de gestão mostram isso.
- Qualquer mudança em `apps/api` ou `apps/workers` — os dois endpoints
  consumidos já existem e estão aplicados.
- Extrair um módulo compartilhado entre `features/agents` e
  `features/mcp-servers` (ver Decision 1).

## Decisions

### 1. Import direto de `features/mcp-servers/` em vez de compartilhar módulo

`AgentMcpServersPage` importa `McpServer`, `useMcpServersQuery` e o
novo `useMcpServerToolsQuery` diretamente de `features/mcp-servers/`.

Esta é a primeira vez que uma feature de `apps/frontend` importa de
outra feature — não a terceira. O mesmo raciocínio já usado no backend
para não criar `libs/` prematuramente se aplica aqui: extrair um módulo
compartilhado (`features/shared/` ou promover tipo/hook para `app/`)
antes de existir um segundo consumidor cruzado é especular sobre uma
necessidade que ainda não apareceu, e adiciona uma camada de indireção
sem benefício demonstrado. Se uma terceira feature precisar do mesmo
tipo/hook no futuro, é o momento de extrair.

**Alternativa descartada**: extrair `McpServer`/`useMcpServersQuery`
para um módulo compartilhado desde já. Descartada por falta de
necessidade concreta — só há um consumidor cruzado até agora.

### 2. Página dedicada `AgentMcpServersPage`, não formulário inline

Nova página em `/agents/:id/mcp-servers`, acessada por um link a partir
de `AgentDetailPage`. Mesmo padrão já usado para separar `AgentEditPage`
de `AgentDetailPage`: a combinação de (a) selecionar múltiplos
servidores, (b) por servidor selecionado buscar e selecionar tools sob
demanda, e (c) tratar o erro atômico do PUT identificando o servidor
culpado, é complexidade suficiente para merecer uma página própria — um
modal ou seção inline dentro do detalhe do agente ficaria sobrecarregado
e competiria por espaço com as instruções do agente (que já usa a
maior parte da viewport).

`AgentDetailPage` ganha um bloco de resumo (nomes dos servidores MCP
vinculados, ou "Nenhum servidor MCP vinculado") + um botão/link "Gerenciar
servidores MCP" apontando para a nova página. O resumo entra em
`AgentDetailCard` (que já é o local dos dados descritivos do agente:
provider, model, datas), enquanto o link para gerenciar entra no
`Group` de ações do topo da página, ao lado de "Editar" — mesmo local
das outras ações de navegação/gestão, distinto de dados exibidos.

**Alternativa descartada**: formulário inline expansível dentro de
`AgentDetailPage`. Descartada por sobrecarregar uma página que já tem
um bloco de conteúdo extenso (instruções) e por misturar o modelo de
edição (seleção com submit explícito) com o de leitura (detalhe).

### 3. Descoberta de tools é lazy, por servidor, com cache que sobrevive a recolher/reabrir

`useMcpServerToolsQuery(mcpServerId, { enabled })` só dispara
`GET /mcp-servers/{id}/tools` quando o servidor correspondente está
selecionado/expandido na UI — implementado com a opção `enabled` do
React Query. Servidores não selecionados nunca disparam a query.

"Lazy" por si só (só a opção `enabled`) garante apenas a primeira busca
sob demanda — não garante que recolher e reabrir o mesmo servidor evite
uma segunda chamada. O componente de linha do servidor não desmonta o
`useMcpServerToolsQuery` ao recolher (só alterna `enabled` para
`false`); ao reabrir, `enabled` volta a `true` reaproveitando a mesma
instância do hook. Com o `staleTime` padrão do React Query (`0`), os
dados já buscados são considerados "stale" imediatamente, e reativar
uma query stale dispara um refetch em background — ou seja, sem uma
configuração explícita, recolher/reabrir repetiria a chamada. Por isso
`useMcpServerToolsQuery` usa `staleTime: Infinity`: o resultado da
descoberta de um servidor não muda durante a sessão da página a não ser
por ação explícita do usuário (o botão "Tentar novamente" da Decision
6, que chama `refetch()` manualmente, ignorando `staleTime`). Reabrir
um servidor cujas tools já foram descobertas com sucesso reutiliza o
dado em cache sem nova chamada de rede.

**Alternativa descartada**: buscar as tools de todos os servidores do
catálogo ao carregar a página (prefetch). Descartada por custar um
handshake MCP real por servidor cadastrado mesmo quando o usuário não
pretende alterar a maioria dos vínculos — desnecessário e mais lento
para abrir a página.

**Alternativa descartada**: manter o `staleTime` padrão (`0`) e evitar
refetch ao reabrir desmontando/remontando o componente de linha do
servidor (em vez de só alternar `enabled`). Descartada por ser mais
frágil — depende de garantir, em todo lugar que o componente é
renderizado, que a chave de montagem realmente muda; `staleTime:
Infinity` expressa a intenção diretamente na definição do hook, sem
depender de como cada consumidor o monta.

### 4. Estado inicial: pré-seleção a partir de `agent.mcpServers`, com reconciliação de drift

A página só renderiza a UI de seleção depois que `useAgentQuery(id)` e
`useMcpServersQuery()` resolvem (mesmo padrão de loader-até-pronto de
`AgentEditPage`). A partir daí, o estado local de seleção (quais
servidores estão marcados, e o `allowedTools` de cada um) é inicializado
uma única vez, via `useState` com inicializador preguiçoso, a partir de
`data.mcpServers` — sem `useEffect` de sincronização, porque o
componente só monta depois que os dados já chegaram:

```ts
const [selection, setSelection] = useState<Record<string, string[]>>(() =>
  Object.fromEntries(agent.mcpServers.map((m) => [m.id, m.allowedTools])),
);
```

Servidores presentes em `agent.mcpServers` aparecem marcados desde o
primeiro render. Servidores do catálogo ausentes de `agent.mcpServers`
aparecem desmarcados.

Quando o usuário expande um servidor (disparando a busca lazy da
Decision 3) e a busca retorna com sucesso, a lista de tools ao vivo
chega. Nesse momento a UI **reconcilia** o `allowedTools` local desse
servidor contra a lista viva: tools ainda oferecidas continuam
marcadas; uma tool que estava em `allowedTools` mas não consta mais na
resposta de `tools/list` (drift, já tolerado pelo backend) é removida
do estado local de seleção — ela simplesmente aparece desmarcada, e
deixa de fazer parte do payload que seria enviado no submit. Essa
reconciliação evita reenviar, sem intenção, uma tool que a própria API
rejeitaria com 400 ("tool inexistente no servidor") caso o usuário
submeta sem tocar naquele servidor específico.

**Alternativa descartada**: manter a tool "fantasma" no estado e deixar
a validação do PUT rejeitar no submit. Descartada porque o usuário não
teria como saber, sem tentar salvar, que uma seleção aparentemente
inalterada vai falhar — a reconciliação no momento da descoberta
(quando a informação já está disponível) é mais direta.

### 5. Erro atômico do PUT (502): usar `title` + `detail`, preservar seleção

**Verificado contra o código real** (`request<T>` em
`features/agents/api/agentsApi.ts`, linhas 27-48): o parsing do corpo
da resposta como `ValidationProblemDetails` acontece dentro de
`if (!response.ok)` — genérico para **qualquer** status de erro (400,
404, 502 etc.), não restrito a 400. A restrição a 400 existe só do lado
do *consumo*, em `fieldErrorsFrom` (`AgentEditPage`/`AgentCreatePage`),
que só olha `error.problem?.errors` quando `error.status === 400` — mas
isso é uma escolha de cada chamador, não uma limitação do `request<T>`.
Ou seja, `error.problem` já vem populado para um 502, com o corpo
inteiro do `ProblemDetails` retornado pela API — **nenhuma mudança é
necessária em `request<T>`**.

O único ajuste real é de tipo: a interface `ValidationProblemDetails`
hoje declara só `title`/`status`/`errors`, sem `detail` — o campo já
chega no objeto JSON parseado (`problem?.title ?? ...` não filtra
chaves), só não está tipado, então `error.problem?.detail` hoje é um
erro de compilação TypeScript, não um valor ausente em runtime. A
interface ganha `detail?: string`.

No tratamento de erro do submit, quando `error.status === 502`, a UI
exibe `error.problem?.title` (já identifica o `McpServerId`) junto com
`error.problem?.detail` (o motivo), como uma notificação/alerta que
deixa claro que **nada foi salvo** e que é preciso corrigir ou tentar
novamente. A seleção feita na tela (todos os servidores e tools já
marcados, incluindo os que não causaram o problema) permanece intacta —
o erro não reseta nem limpa nenhum estado local, só impede a navegação
de sucesso.

Como essa suposição nunca tinha sido verificada contra o código antes
desta revisão, um teste dedicado (`useAgents.test.ts`) mockando um 502
com `title`/`detail` no corpo trava esse comportamento — cobrindo tanto
o parsing genérico de `request<T>` quanto o campo `detail` agora
tipado.

**Alternativa descartada**: mostrar apenas uma mensagem genérica de
"erro ao salvar". Descartada porque o backend já identifica o servidor
específico (`McpServerId` no `title`) — esconder essa informação
obrigaria o usuário a adivinhar qual dos servidores selecionados está
com problema de conectividade.

### 6. Falha ao descobrir tools de um servidor (`success: false`): não bloquear, oferecer retry

Como `GET /mcp-servers/{id}/tools` responde `200` mesmo quando a
descoberta falha (ver Context), `useMcpServerToolsQuery` expõe
`data.success === false` como um resultado de dado normal — **não**
como `isError` do React Query. O componente de linha do servidor trata
três estados de dados distintos: carregando (`isLoading`), sucesso
(`data.success === true`, renderiza os checkboxes de tools) e falha de
descoberta (`data.success === false`, renderiza `data.message` com um
botão "Tentar novamente" que chama `refetch()`).

O usuário pode marcar o servidor como selecionado mesmo com a
descoberta falhando — o backend já aceita `allowedTools: []` como
vínculo válido. Só não é possível marcar tools individuais até a
descoberta ter sucesso (não há lista para marcar contra).

**Alternativa descartada**: bloquear a seleção do servidor inteiro
enquanto a descoberta de tools falhar. Descartada porque a falha pode
ser temporária (rede) e não impede um vínculo válido com
`allowedTools: []` — bloquear obrigaria o usuário a esperar o servidor
MCP voltar antes de conseguir vincular o agente a ele, mesmo sem
precisar de nenhuma tool específica ainda.

### 7. Body do PUT é sempre envelopado e explícito

`replaceAgentMcpServers(agentId, bindings)` sempre envia
`{ mcpServers: bindings }` — nunca um array solto, e nunca omite o
campo (que seria interpretado como `null` e rejeitado com 400). Remover
todos os vínculos de um agente é expresso enviando `bindings: []`
explicitamente.

## Risks / Trade-offs

- [Cruzamento de features aumenta acoplamento entre `agents` e
  `mcp-servers`] → aceito conscientemente (Decision 1); se um terceiro
  consumidor cruzado aparecer, é o sinal para extrair um módulo
  compartilhado, não antes.
- [Reconciliação de drift (Decision 4) só acontece quando o usuário
  expande um servidor já vinculado — se ele nunca expandir, uma tool
  "fantasma" permanece na seleção local até o submit] → aceitável: o
  submit envia exatamente o que o usuário via na tela; se ele nunca
  expandiu aquele servidor, não alterou nada nele, e reenviar o mesmo
  `allowedTools` que já estava persistido só falha se a tool
  específica tiver sumido do servidor *entre* o carregamento da página
  e o submit — janela pequena, e o erro 400 resultante já identifica a
  tool rejeitada.
- [Falha de descoberta (Decision 6) tratada como dado e não como erro
  do React Query] → exige atenção no hook para não usar `isError`/`retry`
  automático do React Query para esse caso, já que a falha de negócio
  vem dentro de uma resposta 200 — divergência do padrão usual de
  tratamento de erro das outras mutations/queries da aplicação, mas
  reflete o contrato real da API (mesmo formato de
  `McpConnectionTestResult` já usado em "Testar conexão").
