## 1. Tipos e API da feature `sessions` (apps/frontend)

- [x] 1.1 Criar `features/sessions/types/session.ts` com os tipos
      `ChannelSession`, `MessagePreview` e `SessionMessage` espelhando
      `ChannelSessionResponse`/`MessagePreviewResponse`/`MessageResponse`,
      com os quatro enums de mensagem como union de literais de string
      (`'Pending' | 'Dispatching' | 'Failed' | 'Completed'`, etc.) —
      pré-requisito `inbox-enums-json-string` já mesclada, sem
      mapeamento por índice.
- [x] 1.2 Criar `features/sessions/api/sessionsApi.ts` com
      `request<T>`/`ApiError` próprios (mesmo padrão de
      `channelsApi.ts`), `listChannelSessions(channelId)` e
      `getSessionMessages(sessionId)`.
- [x] 1.3 Criar `features/sessions/api/useSessions.ts` com
      `useChannelSessionsQuery(channelId)` (sem `refetchInterval`) e
      `useSessionMessagesQuery(sessionId)` (com `refetchInterval: 5000`).
- [x] 1.4 Teste de `useSessions.ts` com fake timers do Vitest:
      `useSessionMessagesQuery` reconsulta após 5s decorridos (mock de
      `getSessionMessages` chamado mais de uma vez); `useChannelSessionsQuery`
      **não** reconsulta pela passagem do tempo sozinha (mock de
      `listChannelSessions` chamado apenas uma vez após o mesmo intervalo)
      — o par "com/sem" que fixa a Decisão 4 do design.md.

## 2. Componentes apresentacionais (apps/frontend)

- [x] 2.1 Criar `features/sessions/components/SessionList.tsx` (recebe a
      lista de sessões, a sessão selecionada e um callback de seleção
      como props) — cada linha exibe nome de exibição (com fallback para
      `ContactExternalId`), prévia da última mensagem quando existir, e
      instante de última atividade; estado vazio "sem sessões".
- [x] 2.2 Testes de `SessionList.tsx`: renderiza nome de exibição quando
      existe; renderiza `ContactExternalId` quando `ContactDisplayName`
      é nulo; renderiza a prévia da última mensagem quando existe;
      renderiza o instante de última atividade; renderiza a linha
      normalmente (sem prévia, sem erro) quando `LastMessage` é nulo;
      canal sem sessão renderiza estado vazio.
- [x] 2.3 Criar `features/sessions/components/MessageBubble.tsx` (recebe
      uma mensagem como prop) — saída `Sent` renderiza um único
      indicador de envio; saída `Failed` renderiza indicador de erro com
      `DeliveryFailureReason` truncado e acessível; entrada renderiza
      indicador distinto para cada um dos quatro `DispatchStatus`; mídia
      (`ContentType` != `Text`) renderiza marcador com tipo, sem tag de
      mídia real; `DispatchStatus`/`DeliveryStatus`/`ContentType` fora
      dos valores conhecidos renderiza indicador neutro, nunca o
      indicador usado para `Completed`/`Sent`; `Direction` fora de
      `Inbound`/`Outbound` renderiza a mensagem em forma neutra, sem
      escolher lado e sem renderizar bloco de status de dispatch nem de
      delivery.
- [x] 2.4 Testes de `MessageBubble.tsx`: saída `Sent` renderiza
      exatamente um indicador de envio e o teste afirma a ausência de um
      segundo; saída `Failed` renderiza o indicador de erro com o motivo
      acessível; os quatro valores de `DispatchStatus` renderizam
      indicadores distinguíveis entre si; mensagem de mídia renderiza o
      marcador com o tipo, sem tentar carregar binário; um
      `DispatchStatus` fora do union conhecido renderiza o indicador
      neutro, não o indicador de `Completed`; um `DeliveryStatus` fora do
      union conhecido renderiza o indicador neutro, não o indicador de
      `Sent`; um `ContentType` fora do union conhecido renderiza o
      marcador genérico, não uma tag de mídia real; um `Direction` fora
      do union conhecido renderiza a mensagem em forma neutra, e o teste
      afirma a ausência tanto do bloco de status de dispatch quanto do de
      delivery.
- [x] 2.5 Criar `features/sessions/components/MessageTimeline.tsx`
      (recebe a lista ordenada de mensagens da sessão como prop) —
      renderiza uma `MessageBubble` por mensagem; estado vazio "sessão
      sem mensagens".
- [x] 2.6 Teste de `MessageTimeline.tsx`: sessão sem nenhuma mensagem
      renderiza o estado vazio, não erro nem tela em branco.

## 3. Integração na página de detalhe de canal (apps/frontend)

- [x] 3.1 Adicionar a rota `channels/:id/sessions/:sessionId` em
      `app/router.tsx`, apontando para o mesmo elemento
      `<ChannelDetailPage />` da rota `channels/:id`.
- [x] 3.2 Modificar `features/channels/pages/ChannelDetailPage.tsx`:
      envolver o conteúdo em `Tabs` do Mantine (`Sessões` como
      `defaultValue`, `Configuração` com o card hoje existente, sem
      alteração de comportamento); manter os botões `Editar`/
      `Ativar`/`Desativar` fora do `Tabs.Panel`, acima das abas.
- [x] 3.3 Na aba Sessões, buscar dados via `useChannelSessionsQuery`/
      `useSessionMessagesQuery` (feature `sessions`) e repassar como
      props a `SessionList`/`MessageTimeline` (convenção 7 — página
      busca, componente apresentacional recebe prop); layout
      master-detail lado a lado (lista ~1/3, timeline ~2/3), cada coluna
      com scroll independente; selecionar uma sessão navega para
      `channels/:id/sessions/:sessionId`.
- [x] 3.4 Estado vazio "selecione uma sessão para ver a conversa" na
      coluna da timeline quando a URL não tem `sessionId`.
- [x] 3.5 Loading e erro de `useChannelSessionsQuery` exibidos na coluna
      da lista; loading e erro de `useSessionMessagesQuery` exibidos na
      coluna da timeline, independentemente um do outro; `sessionId` da
      URL que não corresponde a nenhuma sessão (404 de
      `GET /sessions/{id}/messages`) reaproveita o mesmo estado de erro
      genérico da timeline, sem estado dedicado.
- [x] 3.6 Testes de `ChannelDetailPage.tsx`: aba Sessões é a aba padrão;
      aba Configuração renderiza o card existente sem alteração de
      comportamento; canal sem nenhuma sessão renderiza estado vazio na
      aba Sessões; `sessionId` inexistente na URL renderiza o estado de
      erro genérico da timeline, não uma tela em branco.

## 4. Documentação de achados (repo raiz)

- [x] 4.1 Registrar em `02-HISTORICO_E_STATUS.md`, na seção de itens em
      aberto, a ausência de paginação em
      `GET /channels/{id}/sessions` e `GET /sessions/{id}/messages`, no
      mesmo formato já usado para retenção de mensagens (bullet +
      `Gatilho:`).

## 5. Validação

- [x] 5.1 Rodar `openspec validate frontend-inbox-sessoes-historico
      --strict` e revisar a saída.
- [x] 5.2 Rodar a suíte de testes de `apps/frontend` (Vitest) e confirmar
      que todos os testes novos e existentes passam.
