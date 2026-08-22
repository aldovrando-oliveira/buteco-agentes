## Why

O backend do catálogo de canais de entrada (`apps/inbox`) já está completo:
cadastro, listagem, consulta, atualização, ativação/desativação, e
provisionamento automático de webhook para Telegram (`inbox-channel-catalog`,
`inbox-adapter-waha`, `inbox-adapter-telegram`). Não existe, porém, nenhuma
interface em `apps/frontend` para operar esse catálogo — hoje ele só é
acessível via chamadas HTTP diretas. Essa é a última peça pendente da linha
de trabalho de caixas de entrada: sem ela, ninguém consegue cadastrar ou
gerenciar um canal (WAHA ou Telegram) sem sair da aplicação.

## What Changes

- Adiciona a feature `channels` em `apps/frontend`, com CRUD completo de
  canais consumindo os endpoints já existentes de `apps/inbox`
  (`POST/GET/PUT /channels`, `POST /channels/{id}/activate`,
  `POST /channels/{id}/deactivate`).
- `ChannelListPage`, `ChannelCreatePage`, `ChannelEditPage`,
  `ChannelDetailPage`, `ChannelForm` — mesma estrutura de pastas e convenção
  de nomes de `features/agents`/`features/mcp-servers`.
- Formulário de credencial próprio por `ChannelType`
  (`WahaCredentialFields`/`TelegramCredentialFields`), sem abstração
  schema-driven — lista de tipos hardcoded no frontend
  (`["waha", "telegram"]`), sem endpoint de discovery (não existe hoje).
- Credencial write-only, nunca pré-preenchida na edição, mesmo padrão já
  usado em `McpServerForm`.
- Exibição de `webhookUrl` em destaque na página de detalhe, com texto de
  instrução que varia por `ChannelType`.
- Tratamento diferenciado de erro 400 (`InvalidCredential`/`AgentNotFound`,
  por campo) e 502 (`ProvisioningFailed`/falha de validação do agente,
  mensagem única em `detail`) — este último introduz um padrão novo no
  frontend, que hoje não lê `detail` de nenhuma resposta de erro.
- Nova variável de ambiente `VITE_INBOX_BASE_URL` (e entrada em
  `.env`/`.env.example`) para apontar ao `apps/inbox` (porta `5027` em
  dev), distinta de `VITE_API_BASE_URL` (porta `5017`, `apps/api`) já usada
  pelas features existentes.
- Rotas `/channels`, `/channels/new`, `/channels/:id`, `/channels/:id/edit`
  e item de navegação no `AppShell`.

## Capabilities

### New Capabilities
- `inbox-channel-catalog-ui`: interface em `apps/frontend` para
  listar, cadastrar, consultar, editar, ativar e desativar canais de
  entrada do catálogo de `apps/inbox`, com formulário de credencial
  específico por `ChannelType` (WAHA, Telegram) e exibição do `webhookUrl`
  provisionado por canal.

### Modified Capabilities
(nenhuma — não há mudança de requisito em `inbox-channel-catalog` nem em
nenhuma outra capability já existente; este change só adiciona consumo via
`apps/frontend`)

## Impact

- **apps/frontend**: nova feature `features/channels` (páginas,
  formulário, cliente HTTP, hooks React Query, tipos); novas rotas em
  `app/router.tsx`; novo item de navegação em
  `components/layout/AppShell.tsx`; nova variável de ambiente
  `VITE_INBOX_BASE_URL` em `.env`/`.env.example`. Nenhuma biblioteca nova.
- **apps/inbox**: nenhuma mudança — todos os endpoints consumidos já
  existem.
- **apps/api / apps/workers**: sem impacto.
