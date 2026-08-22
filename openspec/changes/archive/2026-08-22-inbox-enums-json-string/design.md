## Context

`GET /sessions/{id}/messages` (capability `inbox-message-history`) expõe
`MessageResponse` com quatro campos de enum
(`Direction`, `ContentType`, `DeliveryStatus`, `DispatchStatus`).
`MessageDirection` também atravessa `GET /channels/{channelId}/sessions`
(capability `inbox-contact-session`), dentro da prévia da última mensagem
de cada sessão (`ChannelSessionResponse.LastMessage.Direction`). Nenhum
dos quatro enums (`apps/inbox/src/Buteco.Inbox/Messages/Entities/`) tem
`[JsonConverter(typeof(JsonStringEnumConverter<T>))]`, e `Program.cs` de
`apps/inbox` não registra nenhum conversor global de enum→string. Sem
isso, `System.Text.Json` serializa cada enum pelo valor ordinal
(`0`, `1`, `2`...) nas duas rotas. O padrão já estabelecido no repo para
enums que atravessam a API é o atributo por enum: `McpServerAuthType`
(`apps/api/src/Buteco.Api/McpServers/Entities/McpServerAuthType.cs` e o
espelho em `apps/workers`) já o usa. `AppDbContext` já mapeia os quatro
enums de `Message` como coluna de texto
(`HasConversion<string>()`), então esta correção não tem nenhuma
implicação de armazenamento — é puramente a camada de serialização HTTP
que está fora do padrão da própria casa.

Change pequena e sequenciada antes de
`frontend-inbox-sessoes-historico`, que consome as duas rotas acima
assumindo os campos de enum já como string.

## Goals / Non-Goals

**Goals:**
- Os quatro enums de `Message` serializam como string (nome do enum),
  não como inteiro ordinal, em qualquer resposta HTTP de `apps/inbox`.
- Alinhar com o padrão já usado em `McpServerAuthType`.

**Non-Goals:**
- Mudar o armazenamento em banco (já é `HasConversion<string>()`).
- Adicionar validação ou normalização de valor desconhecido no lado do
  servidor — isso é responsabilidade do consumidor (ver
  `frontend-inbox-sessoes-historico`, Decisão sobre valor de enum
  desconhecido).
- Qualquer mudança em `apps/api`, `apps/workers` ou `apps/frontend`.

## Decisions

### Atributo por enum, não conversor global em `JsonSerializerOptions`

Adicionar `[JsonConverter(typeof(JsonStringEnumConverter<T>))]`
diretamente em cada um dos quatro enums, em vez de registrar um
`JsonStringEnumConverter` global via
`ConfigureHttpJsonOptions`/`AddJsonOptions` em `Program.cs`.

**Motivo**: é exatamente o padrão já em uso no repo para
`McpServerAuthType` — um conversor global mudaria o comportamento de
serialização de todo enum futuro de `apps/inbox` por default implícito,
divergindo da convenção já estabelecida de decisão explícita por tipo.
Manter os dois módulos (`api` e `inbox`) com a mesma abordagem também
evita que um novo enum em `apps/inbox` no futuro precise "descobrir" um
comportamento implícito de serialização só lendo `Program.cs`.

**Alternativa considerada**: conversor global. Rejeitada pelo motivo
acima — inconsistência de abordagem entre módulos do mesmo monorepo.

### Nenhuma migração de EF Core

`AppDbContext` já usa `HasConversion<string>()` nas quatro propriedades
(`Message.Direction`, `.ContentType`, `.DeliveryStatus`,
`.DispatchStatus`) — a mudança é restrita ao atributo de serialização
JSON, sem tocar mapeamento de coluna, então nenhuma migração é
necessária.

## Risks / Trade-offs

- **[Risco] BREAKING para qualquer consumidor que já decodifique os
  campos como inteiro** → mitigação: nenhum consumidor conhecido hoje
  além de `frontend-inbox-sessoes-historico`, ainda não implementada;
  sequenciar esta change antes dela elimina o risco na prática.
- **[Trade-off] Nenhum tratamento de valor desconhecido no servidor** →
  aceito: é responsabilidade do consumidor definir o que fazer diante de
  um nome de enum que não reconhece (ver capability consumidora); o
  servidor continua apenas emitindo o nome real do valor persistido.
