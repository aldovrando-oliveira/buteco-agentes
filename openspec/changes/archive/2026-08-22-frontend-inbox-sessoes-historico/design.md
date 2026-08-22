## Context

Três changes anteriores já entregam tudo que esta UI consome. As duas
primeiras não exigiram nenhuma alteração em `apps/inbox`; a terceira
exigiu uma correção pontual em `apps/inbox`, feita em change separada e
sequenciada antes desta (ver Pré-requisito abaixo) — nesta change em si
(`frontend-inbox-sessoes-historico`), nenhuma alteração adicional é feita
em `apps/inbox`:

- `inbox-channel-catalog` / `frontend-inbox-catalogo-canais`: canais
  cadastrados, com `ChannelDetailPage` já existente em
  `apps/frontend/src/features/channels/pages/ChannelDetailPage.tsx`,
  mostrando um único card de configuração (tipo, agente, webhook, datas).
- `inbox-mensagens-persistidas`: `apps/inbox` persiste `Message` (entrada
  e saída) por `Session`, e serve, autenticadas:
  - `GET /channels/{channelId}/sessions` → `ChannelSessionResponse[]`,
    ordenado por `LastActivityAt` desc, com `ContactExternalId`,
    `ContactDisplayName` (nullable) e `MessagePreviewResponse?` (direção,
    conteúdo, instante da última mensagem).
  - `GET /sessions/{sessionId}/messages` → `MessageResponse[]`, ordem
    cronológica, com `Direction`, `Content`, `ContentType`, `OccurredAt`,
    e, conforme a direção, `DeliveryStatus`/`DeliveryFailureReason`
    (saída) ou `DispatchStatus` (entrada).

Pré-requisito desta change: `inbox-enums-json-string` (change separada,
sequenciada antes desta) corrige o formato de fio dos quatro enums de
`MessageResponse` para string — este design assume que já está mesclada,
e os tipos do frontend usam union de literais de string, não índice
numérico.

Autenticação (token de operador, tratamento de 401) já é padrão
estabelecido desde `frontend-inbox-catalogo-canais` — cada feature
importa `getToken`/`clearToken` de `src/auth/token.ts` dentro do próprio
`request<T>`.

Stack já pinada no repo (nenhuma decisão de versão nova nesta change,
apenas uso do que já está em `apps/frontend/package.json`): React
19.2.7, TypeScript ~6.0.2, Mantine 9.4.2, `react-router` 8.3.0,
`@tanstack/react-query` 5.101.4, Vitest + Testing Library.

## Goals / Non-Goals

**Goals:**
- Tornar o silêncio de uma conversa legível: todo estado de
  `DispatchStatus` de uma mensagem de entrada é visível na timeline.
- Listar sessões de um canal e permitir abrir a timeline de qualquer uma,
  com URL própria (reload, back button e link compartilhável
  funcionando).
- Não mentir sobre o que o backend sabe: mensagem de saída nunca mostra
  confirmação de entrega/leitura que o sistema não coletou.
- Preservar a aba de configuração do canal exatamente como está hoje.

**Non-Goals:**
- Responder pela UI (envolveria `IOutboundMessageSender` e autoria de
  mensagem — change própria).
- Editar/apagar mensagem.
- Renderizar mídia real (binário não é persistido nesta fatia).
- Caixa de entrada unificada entre canais.
- Qualquer mudança em `apps/api`, `apps/inbox` ou `apps/workers`.
- Paginação (ver Riscos/Trade-offs).

## Decisions

### Árvore de pastas proposta

```
apps/frontend/src/
├── app/
│   └── router.tsx                          (MODIFICADO — nova rota aninhada)
└── features/
    ├── channels/
    │   └── pages/
    │       ├── ChannelDetailPage.tsx        (MODIFICADO — abas)
    │       └── ChannelDetailPage.test.tsx   (MODIFICADO)
    └── sessions/                            (NOVA feature)
        ├── api/
        │   ├── sessionsApi.ts               (request<T>/ApiError próprios)
        │   └── useSessions.ts               (hooks de react-query)
        ├── components/
        │   ├── SessionList.tsx
        │   ├── SessionList.test.tsx
        │   ├── MessageTimeline.tsx
        │   ├── MessageTimeline.test.tsx
        │   ├── MessageBubble.tsx
        │   └── MessageBubble.test.tsx
        └── types/
            └── session.ts
```

Sem página nova em `sessions/pages` — `ChannelDetailPage` (que já existe
em `channels`) continua sendo a página que busca dados e monta a tela;
`sessions` fornece os hooks de dados e os componentes apresentacionais
que ela consome. Isso já tem precedente direto: `ChannelDetailPage`
já importa `useAgentsQuery` de `features/agents/api/useAgents` para
resolver o nome do agente responsável — importar hooks de `sessions`
segue o mesmo padrão de composição entre features já em uso.

### Decisão 1 — Onde a feature mora: `sessions` nova, não extensão de `channels`

`GET /channels/{id}/sessions` até parece pertencer a `channels`, mas
`GET /sessions/{id}/messages` não carrega `channelId` nenhum — uma vez
que se tem o `sessionId`, a timeline não tem mais nada a ver com canal.
Colocar as duas em `channelsApi.ts` misturaria CRUD de `Channel` com
leitura de conversa, dois conceitos que crescem em direções diferentes
(a futura "responder pela UI", fora de escopo aqui, pertenceria
naturalmente a `sessions`, não a `channels`).

`channelsApi.ts` já documenta o padrão de duplicação deliberada de
`request<T>`/`INBOX_BASE_URL` por feature (`agentsApi.ts`,
`mcpServersApi.ts`, `channelsApi.ts` já fazem isso) — convenção 7 permite
essa duplicação e não permite é um cliente HTTP compartilhado. `sessions`
segue o mesmo padrão: seu próprio `request<T>`/`ApiError` em
`sessionsApi.ts`, mesmo `INBOX_BASE_URL`.

**Alternativa considerada**: estender `channelsApi.ts` com as duas
rotas. Rejeitada — `GetSessionMessages` não é dado de canal, e a
convenção já estabelecida no repo é isolar por conceito de domínio, não
por origem de dado.

### Decisão 2 — Roteamento da sessão selecionada: URL própria

`app/router.tsx` usa `<Routes>`/`<Route>` declarativo (não data router
com loaders), então a rota aninhada é direta:

```tsx
<Route path="channels">
  <Route index element={<ChannelListPage />} />
  <Route path="new" element={<ChannelCreatePage />} />
  <Route path=":id" element={<ChannelDetailPage />} />
  <Route path=":id/sessions/:sessionId" element={<ChannelDetailPage />} />
  <Route path=":id/edit" element={<ChannelEditPage />} />
</Route>
```

`ChannelDetailPage` lê `sessionId` como parâmetro opcional
(`useParams<{ id: string; sessionId?: string }>()`). Selecionar uma linha
da lista navega para `/channels/:id/sessions/:sessionId`; a URL
sobrevive a reload, funciona com o botão voltar do navegador, e é
compartilhável — os três ganhos que estado local em memória não dá.

A aba "Configuração" **não** ganha URL própria — nada no proposal pede
isso, e o valor da aba é estado local do `Tabs` do Mantine
(`uncontrolled`, `defaultValue="sessions"`), sem inventar rota para um
requisito que não existe.

**Alternativa considerada**: estado local (`useState<string | null>`)
para a sessão selecionada. Rejeitada pelos três motivos acima —
especialmente porque o operador plausivelmente quer voltar a uma
conversa específica após navegar para outro lugar (ex. abrir o agente
responsável e voltar).

### Decisão 3 — Layout: master-detail lado a lado

A tela atual (`ChannelDetailPage`, card único) já é claramente
desktop-first (`TextInput` de webhook com botão ao lado, sem nenhuma
adaptação mobile em nenhuma outra página do app). Lista→detalhe por
navegação perderia o contexto da lista a cada troca de sessão, que é
exatamente o padrão de uso esperado (operador comparando/alternando entre
conversas). `SessionList` (coluna esquerda, ~1/3) e `MessageTimeline`
(coluna direita, ~2/3) em duas colunas via `Grid`/`Flex` do Mantine, cada
uma com scroll independente. Sem sessão selecionada, a coluna direita
mostra um estado vazio próprio ("Selecione uma sessão para ver a
conversa").

### Decisão 4 — Atualização dos dados: refetchInterval só na timeline aberta

`DebounceOptions.SweepInterval` (backend) é 2s — não faz sentido a UI
pollar mais rápido que o próprio backend produz mudança de estado. Duas
políticas distintas, deliberadamente diferentes:

- **Lista de sessões**: sem `refetchInterval`. O refetch natural do
  react-query (foco de janela, remount ao trocar de aba) já cobre "abri
  a tela e vejo o estado atual"; pollar uma lista que ninguém está
  olhando fixamente é gasto sem propósito.
- **Timeline aberta** (`sessionId` presente na URL): `refetchInterval:
  5000` — acima do sweep de 2s do backend, ainda parece "ao vivo" para
  quem está acompanhando a conversa. `refetchIntervalInBackground` fica
  no default (`false`): pausa quando a aba perde foco, sem configuração
  extra.

O cenário "Timeline em segundo plano não atualiza" (Requirement
"Atualização periódica da timeline aberta",
`specs/inbox-message-history-ui/spec.md`) não tem task de teste
correspondente — decisão deliberada, não omissão. O comportamento vem
inteiramente do default `refetchIntervalInBackground: false` do
react-query, não de código escrito nesta change; escrever um teste para
ele seria testar a biblioteca, não esta feature (convenção 10, segunda
saída aceita: cenário sem teste com justificativa escrita).

**Alternativa considerada**: refresh manual (botão). Rejeitada — a
motivação original da change é visibilidade de silêncio em tempo
razoável; exigir ação manual reintroduz o mesmo problema que a change
resolve, só que sob o controle do operador esquecer de clicar.

### Decisão 5 — Paginação: aceitar nesta fatia, registrar item em aberto

Nenhuma das duas rotas pagina — confirmado lendo
`GetChannelSessionsQueryHandler` e `GetSessionMessagesQueryHandler`
(`apps/inbox`): ambos fazem `ToListAsync()` direto, sem `Skip`/`Take`.
Implementar paginação agora tocaria as duas pontas (backend com
skip/take, frontend com scroll infinito ou "carregar mais"), o que é
escopo de change própria e sem sinal de volume real hoje. Registrado em
`02-HISTORICO_E_STATUS.md` no mesmo formato já usado para retenção de
mensagens (bullet + `Gatilho:`) — ver tasks.md.

### Decisão 6 — `DeliveryFailureReason`: truncado, não cru nem mapeado

O campo carrega a mensagem de exceção do sender — pode conter host, URL
ou detalhe de biblioteca que não ajuda o operador e pode vazar
informação interna. Mapear exigiria uma taxonomia de causas que a etapa
de persistência conscientemente não modelou (mesmo espírito de
`DispatchStatus.Failed` agrupar três causas distintas). Solução:
truncar (limite de caracteres, quebras de linha colapsadas) e expor via
elemento acessível (ícone de erro com texto associado, não a mensagem
crua como primeira linha da bolha).

### Pré-requisito — formato de fio dos enums de `Message` resolvido em change separada

A exploração desta change encontrou os quatro enums de `MessageResponse`
(`MessageDirection`, `MessageDeliveryStatus`, `MessageDispatchStatus`,
`MessageContentType`) trafegando como inteiro ordinal — nenhum tinha
`[JsonConverter(typeof(JsonStringEnumConverter<T>))]`, diferente do
padrão já usado em `McpServerAuthType` (`apps/api`, `apps/workers`). Isso
é defeito de `inbox-mensagens-persistidas` (nunca especificou o formato
de fio desses campos), não requisito novo desta UI, e foi corrigido pela
change separada `inbox-enums-json-string`, sequenciada antes desta —
ver `openspec/changes/inbox-enums-json-string/`.

Consequência para este design: `types/session.ts` usa union de literais
de string (`'Pending' | 'Dispatching' | 'Failed' | 'Completed'`, etc.),
não mapeamento por índice numérico. O teste de "quatro valores de
`DispatchStatus` distinguíveis" (tasks.md) fixa os literais de string.

### Decisão 7 — Valor de enum desconhecido: indicador neutro, nunca sucesso silencioso

Resolver o formato de fio para string reduz o risco de um consumidor
decodificar errado, mas não elimina um cenário distinto: o backend
adiciona ou renomeia um valor de enum (`DispatchStatus`, `DeliveryStatus`,
`ContentType` ou `Direction`) antes do frontend ser atualizado para
reconhecê-lo. Sem tratamento explícito, um `switch`/mapeamento exaustivo
em TypeScript ou cai no `default` errado ou lança em runtime.

Decisão: os quatro enums de `MessageResponse` têm um branch explícito de
valor desconhecido, mas o comportamento não é uniforme:

- `DispatchStatus`, `DeliveryStatus` e `ContentType`: o branch renderiza
  um indicador neutro (ex. "status desconhecido", ícone neutro, nunca a
  cor ou o ícone usado para sucesso/`Completed`/`Sent`).
- `Direction`: é o campo que decide se a mensagem renderiza do lado de
  entrada ou do lado de saída — e, por consequência, qual bloco de
  status (`DispatchStatus` ou `DeliveryStatus`) a bolha usa. Um valor
  fora de `Inbound`/`Outbound` não pode cair em nenhum dos dois lados por
  default: isso renderizaria o bloco de status errado (dispatch numa
  mensagem que não se sabe se é de entrada, por exemplo) ou assumiria uma
  direção que o backend não confirmou. O branch explícito renderiza a
  mensagem em forma neutra — sem escolher lado e sem renderizar nenhum
  dos dois blocos de status.

O teste exercita cada um desses branches, passando um valor fora do union
conhecido como fixture e afirmando que o indicador neutro (ou, para
`Direction`, a forma neutra sem nenhum bloco de status) aparece, não um
dos valores reais — essa é a mitigação real e testável para o risco de um
enum crescer sem o frontend acompanhar (ver Risks).

**Alternativa considerada**: tratar valor desconhecido como erro de
render (lançar/`ErrorBoundary`). Rejeitada — um enum novo do backend não
deveria derrubar a timeline inteira; uma mensagem com indicador neutro
(ou forma neutra) é degradação aceitável, o resto da conversa continua
legível.

### Decisão 8 — `sessionId` inexistente na URL reaproveita o erro genérico da timeline

`sessionId` da URL inexistente (link antigo, sessão de outro canal):
reaproveita o mesmo estado de erro genérico de
`GET /sessions/{id}/messages` (a rota responde 404, capturado pelo mesmo
tratamento de erro da consulta — ver Requirement "Timeline de mensagens
de uma sessão com URL própria", cenário "URL aponta para sessão
inexistente", em `specs/inbox-message-history-ui/spec.md`). Não é um
estado dedicado; decisão explícita, não omissão.

## Risks / Trade-offs

- **[Risco] Valor de enum desconhecido chega antes do frontend ser
  atualizado** (`DispatchStatus`/`DeliveryStatus`/`ContentType`/`Direction`
  novo ou renomeado no backend) → mitigação: branch explícito de valor
  desconhecido renderiza indicador neutro (ou, para `Direction`, forma
  neutra sem lado nem bloco de status), nunca reaproveita o indicador de
  sucesso/`Completed`/`Sent` (Decisão 7); teste passa um valor fora do
  union conhecido como fixture e afirma que o indicador/forma neutra
  aparece.
- **[Risco] Sem paginação, sessão com histórico muito longo carrega tudo
  de uma vez** → mitigação: nenhuma nesta fatia; item em aberto com
  gatilho documentado (ver Decisão 5).
- **[Trade-off] `refetchInterval` de 5s na timeline aberta gera chamadas
  mesmo sem mensagem nova** → aceito: tráfego é uma consulta leve
  (`GetSessionMessagesQueryHandler` já é indexado por `SessionId`), e o
  ganho de visibilidade de silêncio é o objetivo central da change.

## Open Questions

Nenhuma.
