## Why

`apps/inbox` hoje é só um scaffold — health check, sem persistência, sem lógica
de negócio (change `estrutura-base-apps-inbox`, que deferiu deliberadamente
essa decisão). Antes de qualquer adapter de canal (WhatsApp, Telegram) ou do
CRM (Contact/Session) existir, é preciso um lugar para cadastrar quais canais
de entrada existem, com que credenciais e vinculados a qual agente — sem isso,
não há o que um adapter futuro consulte nem para onde rotear uma mensagem.

## What Changes

- Adiciona EF Core + Postgres a `apps/inbox` pela primeira vez: `AppDbContext`,
  banco próprio (`buteco_inbox`, mesmo servidor Postgres do `docker-compose.yml`,
  sem nenhuma tabela em comum com `apps/api`/`apps/workers`), migration inicial.
- Adiciona Mediator/CQRS a `apps/inbox` pela primeira vez (`Mediator.Abstractions`
  + `Mediator.SourceGenerator`, mesma versão já pinada em
  `Directory.Packages.props`), mesmo padrão já estabelecido em `apps/api`.
- Nova entidade `Channel` (`apps/inbox`): `Id`, `ChannelType` (enum fechado:
  `WhatsApp`, `Telegram`), `Name`, `EncryptedCredentials` (jsonb opaco,
  criptografado, write-only), `AgentId` (Guid opaco, validado via HTTP —
  ver Impact), `IsActive`, `CreatedAt`/`UpdatedAt`.
- Criptografia de credenciais via AES-GCM, mesma implementação já usada em
  `apps/api`/`apps/workers` (`AesGcmMcpCredentialCipher`), copiada para
  `apps/inbox` com chave própria (`Inbox:CredentialEncryptionKey`).
- Novos endpoints em `apps/inbox`: `POST /channels`, `GET /channels`,
  `GET /channels/{id}`, `PUT /channels/{id}`, `POST /channels/{id}/activate`,
  `POST /channels/{id}/deactivate` — mesmo padrão CQRS e mesmo formato de
  validação/idempotência já usado em `Agent`/`McpServer` (`apps/api`).
- `apps/inbox` passa a fazer sua primeira chamada de rede a `apps/api`
  (`GET /agents/{id}`), para validar `AgentId` no cadastro/atualização de um
  canal, de forma síncrona e fail-fast.

## Capabilities

### New Capabilities
- `inbox-channel-catalog`: cadastro, consulta, atualização e
  ativação/desativação de canais de entrada em `apps/inbox`, com credenciais
  criptografadas e vínculo validado a um agente de `apps/api`.

### Modified Capabilities

(nenhuma — `inbox-scaffold` não muda: seus requisitos, sobre `Inbox.sln`,
isolamento de projeto e health check, continuam válidos e intocados por esta
change)

## Impact

- **apps/inbox**: novo `AppDbContext`/migrations, novo pacote `Mediator.*`,
  nova entidade `Channel`, novos endpoints HTTP, novo `HttpClient` nomeado
  para chamar `apps/api`, nova configuração (`ConnectionStrings:Postgres`,
  `Inbox:CredentialEncryptionKey`, `Api:BaseUrl`).
- **apps/api**: nenhuma mudança de código — só passa a ser chamado (leitura)
  via `GET /agents/{id}`, endpoint já existente.
- **apps/workers**: nenhuma mudança.
- **docker-compose.yml / .env.example**: possível adição de variável para o
  nome do segundo banco (`buteco_inbox`) — detalhe de implementação em
  `design.md` (Decision 1).
- **Nenhuma mudança em apps/frontend.**
