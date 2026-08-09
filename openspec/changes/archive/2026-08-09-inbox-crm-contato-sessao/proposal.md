## Why

`apps/inbox` hoje (change arquivada `inbox-catalogo-canais`) só sabe cadastrar
canais de entrada — não existe nenhum conceito de "quem está falando" nem "isso
é uma conversa nova ou a continuação de uma já em andamento". Antes de qualquer
adapter real de canal ou do orquestrador (que vai bufferizar/debounçar mensagens
numa change futura) existir, é preciso um CRM mínimo: identificar o contato de
origem de cada mensagem e amarrar várias conversas (sessões) desse mesmo contato
ao longo do tempo, com uma fronteira clara de quando uma sessão termina e outra
começa.

## What Changes

- Novas entidades `Contact` (Id, ChannelId, ExternalId, CreatedAt) e `Session`
  (Id, ContactId, ContextId, StartedAt, LastActivityAt, ClosedAt nullable) no
  `AppDbContext` de `apps/inbox`, migration aditiva.
- `Contact.ChannelId` é FK real para `Channel.Id` — primeira referência entre
  entidades de `apps/inbox` que não precisa de validação HTTP, porque `Channel`
  já vive no mesmo banco/`AppDbContext` (diferente de `Channel.AgentId`, que
  aponta para `apps/api`, outro banco).
- Unique constraint em `Contact(ChannelId, ExternalId)` — primeiro índice único
  do projeto.
- Novo serviço interno `IContactSessionResolver.FindOrCreateSessionAsync(channelId, externalId)`:
  encontra ou cria o `Contact`; reaproveita a `Session` mais recente se dentro
  do timeout de inatividade configurado, senão cria uma nova. Sem endpoint HTTP
  de escrita — chamável direto por testes de integração e, mais adiante, pelo
  orquestrador (mesmo processo).
- Nova configuração `SessionOptions` (timeout de inatividade, `IOptions`,
  default 1 hora), parametrizável por ambiente.
- Novos endpoints só leitura em `apps/inbox`: `GET /contacts`,
  `GET /contacts/{id}/sessions`, para inspeção/auditoria.

## Capabilities

### New Capabilities
- `inbox-contact-session`: identificação de contatos de origem por canal e
  agrupamento de conversas (sessões) do mesmo contato ao longo do tempo, com
  fronteira de sessão por inatividade automática.

### Modified Capabilities

(nenhuma — `inbox-channel-catalog` não muda: seus requisitos sobre cadastro,
consulta, atualização e ativação/desativação de canais continuam válidos e
intocados por esta change)

## Impact

- **apps/inbox**: novas entidades `Contact`/`Session`, nova migration, novo
  serviço `IContactSessionResolver`, nova configuração `SessionOptions`, novos
  endpoints de leitura, novos testes de integração (mesmo `InboxFactoryFixture`).
- **apps/api**: nenhuma mudança de código, nenhuma chamada nova — diferente da
  change anterior (`Channel.AgentId`), `Contact`/`Session` não referenciam
  nenhuma entidade de `apps/api`.
- **apps/workers**: nenhuma mudança.
- **apps/frontend**: nenhuma mudança.
- **Nenhum adapter real de canal, nenhum orquestrador, nenhum debounce** —
  ficam para changes futuras, que vão consumir `IContactSessionResolver` já
  pronto.
