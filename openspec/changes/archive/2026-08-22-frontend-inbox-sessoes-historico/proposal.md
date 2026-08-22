## Why

Hoje, quando o agente responsável por um canal não responde a uma mensagem
recebida, a conversa mostra a mensagem e depois nada — sem distinguir se
está em debounce, se o dispatch falhou, se a task falhou, ou se o envio da
resposta falhou. `apps/inbox` já persiste esse histórico e já serve as
duas consultas necessárias (`GET /channels/{id}/sessions`,
`GET /sessions/{id}/messages`) desde as duas etapas anteriores desta linha
de trabalho, mas não existe nenhuma tela que os exiba. Sem essa UI, o
silêncio de uma conversa continua ilegível para o operador.

**Pré-requisito**: esta change assume que `inbox-enums-json-string` (change
separada, ver `openspec/changes/inbox-enums-json-string/`) já está
implementada e mesclada — os quatro enums de `MessageResponse` chegam ao
frontend como string, não como inteiro ordinal.

## What Changes

- Página de detalhe de canal (`/channels/:id`) ganha duas abas: **Sessões**
  (padrão) e **Configuração** (o card hoje existente, intacto, sem
  redesenho). Botões `Editar`/`Desativar` continuam no nível do canal,
  acima das abas.
- Aba Sessões lista as sessões do canal por última atividade
  (`GET /channels/{id}/sessions`), exibindo nome de exibição do contato
  (ou `ExternalId` como fallback quando `DisplayName` é nulo), prévia da
  última mensagem e instante.
- Selecionar uma sessão abre sua timeline cronológica de mensagens
  (`GET /sessions/{id}/messages`) em `/channels/:id/sessions/:sessionId`,
  lado a lado com a lista (master-detail), sem perder o contexto da lista
  ao trocar de sessão.
- Timeline renderiza, por mensagem:
  - Saída: um único indicador de envio quando `DeliveryStatus: Sent`;
    ícone de erro com motivo acessível (truncado) quando `Failed`. Nunca
    dois indicadores — o backend só sabe se entregou ao provedor, não se
    foi entregue/lido ao destinatário final.
  - Entrada: indicador visível dos quatro valores de `DispatchStatus`
    (`Pending`/`Dispatching`/`Failed`/`Completed`), sem tentar diferenciar
    as três causas que `Failed` agrupa.
  - Mídia (`ContentType` diferente de `Text`): marcador textual com
    indicação do tipo, nunca uma tag de mídia apontando para lugar nenhum.
- Estados vazios cobertos: canal sem nenhuma sessão; sessão sem nenhuma
  mensagem (toda sessão anterior à etapa de persistência cai aqui);
  carregamento e erro de cada uma das duas consultas.
- Nova feature de frontend `sessions`, com seu próprio `request<T>`/
  `ApiError` (convenção 7 — duplicação deliberada, mesmo padrão de
  `channels`/`agents`/`mcp-servers`), consumindo as duas rotas já
  existentes de `apps/inbox`. Nenhuma rota nova é criada.
- Timeline aberta atualiza via `refetchInterval` de 5s (acima do sweep de
  debounce do backend, 2s); a lista de sessões não faz polling.
- Fora de escopo, deliberadamente: responder pela UI, editar/apagar
  mensagem, renderizar mídia real, caixa de entrada unificada entre
  canais.

**Achado registrado, não implementado nesta change:**
- Nenhuma das duas rotas consumidas pagina. Aceito nesta fatia; registrado
  como item em aberto em `02-HISTORICO_E_STATUS.md`, com gatilho.
- Mesmo com o pré-requisito acima resolvendo o formato de fio, um valor de
  enum desconhecido (ex. um nome novo adicionado no backend antes do
  frontend ser atualizado) ainda pode chegar à UI. Tratado como decisão de
  design (ver design.md), não como achado em aberto.

## Capabilities

### New Capabilities
- `inbox-message-history-ui`: listagem de sessões de um canal e timeline
  de mensagens de uma sessão, em `apps/frontend`.

### Modified Capabilities
- `inbox-channel-catalog-ui`: o Requirement "Detalhe de canal" passa a
  descrever a página com duas abas (Sessões padrão, Configuração com o
  card existente) em vez de uma página de card único.

## Impact

- `apps/frontend`: nova feature `features/sessions` (api, components,
  types — sem pasta `pages`; `ChannelDetailPage`, em `channels`, continua
  sendo a página que busca dados e monta a tela);
  `features/channels/pages/ChannelDetailPage.tsx` ganha abas;
  `app/router.tsx` ganha a rota `channels/:id/sessions/:sessionId`.
- Nenhuma mudança em `apps/api`, `apps/inbox` ou `apps/workers`.
- `02-HISTORICO_E_STATUS.md`: uma tarefa desta change registra o item em
  aberto de paginação, no mesmo formato já usado no documento (ex.
  retenção de mensagens) — documentação, não código.
