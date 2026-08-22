## Why

Os quatro enums expostos em `MessageResponse` (`MessageDirection`,
`MessageDeliveryStatus`, `MessageDispatchStatus`, `MessageContentType`)
não têm `[JsonConverter(typeof(JsonStringEnumConverter<T>))]`, então
`GET /sessions/{id}/messages` serializa cada um pelo valor ordinal
(`"DispatchStatus": 2`), não pelo nome (`"Failed"`) — divergente do
padrão já estabelecido no repo para enums que atravessam a API
(`McpServerAuthType`, com o mesmo atributo, em `apps/api` e
`apps/workers`). `MessageDirection` também atravessa uma segunda rota:
`GET /channels/{channelId}/sessions` carrega a direção da última
mensagem dentro da prévia de cada sessão (`ChannelSessionResponse` →
`MessagePreviewResponse`). Isso foi um achado da exploração da change
`frontend-inbox-sessoes-historico`: qualquer consumidor HTTP desses
campos hoje precisa decodificar por índice numérico, frágil a uma
reordenação futura do enum no backend, que não quebraria nenhum teste
de `apps/inbox` (round-trip simétrico) nem, sem esta correção, do
frontend.

## What Changes

- Adicionar `[JsonConverter(typeof(JsonStringEnumConverter<T>))]` a
  `MessageDirection`, `MessageDeliveryStatus`, `MessageDispatchStatus` e
  `MessageContentType` em
  `apps/inbox/src/Buteco.Inbox/Messages/Entities/`.
- Nenhuma migração de banco: `AppDbContext` já mapeia os quatro enums
  como coluna de texto (`HasConversion<string>()`) — o gap é só na
  serialização JSON da API, não no armazenamento.
- **BREAKING** para qualquer consumidor HTTP existente de
  `GET /sessions/{id}/messages` que já decodifique esses campos como
  inteiro (nenhum conhecido além da própria change
  `frontend-inbox-sessoes-historico`, ainda não implementada — ver
  Impact).

## Capabilities

### New Capabilities
_Nenhuma._

### Modified Capabilities
- `inbox-message-history`: a consulta cronológica de mensagens de uma
  sessão passa a garantir que `Direction`, `ContentType`,
  `DeliveryStatus` e `DispatchStatus` são serializados como string
  (nome do enum), não como inteiro ordinal.
- `inbox-contact-session`: a consulta de sessões de um canal passa a
  garantir que a direção da prévia da última mensagem
  (`LastMessage.Direction`) é serializada como string, não como inteiro
  ordinal.

## Impact

- `apps/inbox`: quatro arquivos de enum em `Messages/Entities/` ganham
  um atributo cada; nenhuma mudança de schema, nenhuma mudança de
  handler ou endpoint. O mesmo atributo em `MessageDirection` corrige as
  duas rotas afetadas (`GET /sessions/{id}/messages` e
  `GET /channels/{channelId}/sessions`) de uma vez.
- Sequenciamento: esta change deve ser implementada e mesclada **antes**
  de `frontend-inbox-sessoes-historico`, que consome as duas rotas
  assumindo os campos de enum já como string.
- Nenhuma mudança em `apps/api`, `apps/workers` ou `apps/frontend`.
