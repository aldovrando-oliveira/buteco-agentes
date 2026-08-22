## 1. Configuração de base URL do apps/inbox

- [x] 1.1 (apps/frontend) Adicionar `VITE_INBOX_BASE_URL=http://localhost:5027` em `.env` e `.env.example`, com comentário análogo ao já existente para `VITE_API_BASE_URL`.

## 2. Tipos e cliente HTTP (`features/channels`)

- [x] 2.1 (apps/frontend) Criar `features/channels/types/channel.ts` com `ChannelType` (`'waha' | 'telegram'`), `Channel` (espelhando `ChannelResponse`: `id, channelType, name, agentId, isActive, createdAt, updatedAt, webhookUrl`), `CreateChannelInput`, `UpdateChannelInput`, `WahaCredentialInput { serviceUrl, sessionName, authToken }`, `TelegramCredentialInput { botToken }`.
- [x] 2.2 (apps/frontend) Criar `features/channels/api/channelsApi.ts` com `request<T>`/`ApiError` próprios (padrão de `agentsApi.ts`/`mcpServersApi.ts`), usando `VITE_INBOX_BASE_URL` como base, e uma interface de problem details com `detail?: string` além de `errors?: Record<string, string[]>` (Decision 4 do design.md).
- [x] 2.3 (apps/frontend) Implementar em `channelsApi.ts`: `listChannels`, `getChannel(id)`, `createChannel(input)`, `updateChannel(id, input)`, `activateChannel(id)`, `deactivateChannel(id)`.
- [x] 2.4 (apps/frontend) Criar `features/channels/api/useChannels.ts` com `useChannelsQuery`, `useChannelQuery(id)`, `useCreateChannelMutation`, `useUpdateChannelMutation`, `useActivateChannelMutation`, `useDeactivateChannelMutation` (React Query, mesmo padrão de invalidação/`setQueryData` de `useAgents.ts`).

## 3. Sub-formulários de credencial por tipo

- [x] 3.1 (apps/frontend) Criar `features/channels/components/WahaCredentialFields.tsx`: campos `serviceUrl`, `sessionName`, `authToken`, sempre iniciando em branco (write-only).
- [x] 3.2 (apps/frontend) Criar `features/channels/components/TelegramCredentialFields.tsx`: campo `botToken`, sempre iniciando em branco (write-only).
- [x] 3.3 (apps/frontend) Testes: `WahaCredentialFields.test.tsx` e `TelegramCredentialFields.test.tsx` cobrindo renderização dos campos e valor inicial vazio.

## 4. `ChannelForm`

- [x] 4.1 (apps/frontend) Criar `features/channels/components/ChannelForm.tsx` com campos comuns (nome, `AgentId` via `Select` alimentado por `useAgentsQuery()`, rótulo com sufixo `" (inativo)"` para agentes inativos — Decision 7/8 do design.md).
- [x] 4.2 (apps/frontend) Implementar `Select` de `ChannelType` com lista fixa `["waha", "telegram"]` (Decision 1), visível e editável apenas em modo criação; em modo edição, exibir o tipo como texto fixo.
- [x] 4.3 (apps/frontend) Implementar mapa `channelType -> componente de credencial` e cascata de troca de sub-formulário ao mudar `channelType` (Decision 2), mesma mecânica de `AgentForm` para Provider→Model.
- [x] 4.4 (apps/frontend) Implementar serialização do sub-formulário ativo em JSON (`JSON.stringify`) para o campo `credential` no submit (Decision 2).
- [x] 4.5 (apps/frontend) Implementar validação client-side tudo-ou-nada do sub-formulário de credencial em modo edição: todos os campos vazios omite `credential`; qualquer campo preenchido exige todos os campos obrigatórios do tipo antes de habilitar o submit (Decision 2, Risco 1).
- [x] 4.6 (apps/frontend) Exibir mensagens de erro de campo vindas de `errors?: Record<string,string>` (400) nos campos correspondentes; para `credential`, exibir a lista completa de mensagens recebidas (não só a primeira — Decision 5).
- [x] 4.7 (apps/frontend) Testes `ChannelForm.test.tsx`: criação WAHA com sucesso, criação Telegram com sucesso, troca de `channelType` troca o sub-formulário exibido, campo `AgentId` inclui agentes inativos com rótulo distinto, erro 400 de `credential` com múltiplas mensagens exibe todas, preenchimento parcial da credencial em edição bloqueia o submit.
- [x] 4.8 (apps/frontend) Teste explícito em `ChannelForm.test.tsx` para o cenário "Tipo de canal não é editável": em modo edição, `ChannelType` é renderizado como texto fixo, não como `Select` editável — `UpdateChannelRequest` não tem esse campo, então um `Select` editável por engano faria o backend ignorar silenciosamente a troca, criando a falsa impressão de que o tipo foi alterado.

## 5. Páginas

- [x] 5.1 (apps/frontend) Criar `features/channels/pages/ChannelListPage.tsx`: lista via `useChannelsQuery()`, resolve nome do agente via `useAgentsQuery()`, indicador de ativo/inativo, botão de novo canal, estado vazio, estado de erro.
- [x] 5.2 (apps/frontend) Criar `features/channels/components/ChannelTable.tsx` (tabela reaproveitada por `ChannelListPage`, mesmo padrão de `AgentTable`/`McpServerTable`).
- [x] 5.3 (apps/frontend) Criar `features/channels/pages/ChannelCreatePage.tsx`: usa `ChannelForm` em modo criação, trata erro 400 (por campo) e 502 (`Alert` dedicado com `problem.detail` — Decision 4), notificação de sucesso e redirecionamento para `ChannelDetailPage` (Decision 3).
- [x] 5.4 (apps/frontend) Criar `features/channels/pages/ChannelEditPage.tsx`: carrega canal via `useChannelQuery(id)`, usa `ChannelForm` em modo edição com `channelType` fixo, mesmos tratamentos de erro 400/502 de `ChannelCreatePage`.
- [x] 5.5 (apps/frontend) Criar `features/channels/pages/ChannelDetailPage.tsx`: dados completos (sem credencial), campo `webhookUrl` em destaque com botão de copiar, texto de instrução condicional por `ChannelType` (WAHA vs Telegram — Decision 3), ações de editar/ativar/desativar (confirmação só na desativação — mesmo padrão de Agent/McpServer).
- [x] 5.6 (apps/frontend) Testes `ChannelListPage.test.tsx`, `ChannelCreatePage.test.tsx`, `ChannelEditPage.test.tsx`, `ChannelDetailPage.test.tsx`, `ChannelTable.test.tsx` cobrindo os cenários de `specs/inbox-channel-catalog-ui/spec.md` (lista vazia/erro, cadastro por tipo, erro 400 por campo, erro 502 com mensagem própria, edição mantendo credencial em branco, instrução de webhook por tipo, canal não encontrado, ativar/desativar idempotente, agente inativo permitido).
- [x] 5.7 (apps/frontend) Teste explícito em `ChannelTable.test.tsx` para o cenário "Indicador distingue canal inativo na lista": um canal com `isActive: false` renderiza um indicador visual distinto do usado para canais com `isActive: true`.

## 6. Rotas e navegação

- [x] 6.1 (apps/frontend) Adicionar rotas `/channels`, `/channels/new`, `/channels/:id`, `/channels/:id/edit` em `app/router.tsx`, mesma estrutura aninhada de `agents`/`mcp-servers`.
- [x] 6.2 (apps/frontend) Adicionar item de navegação "Canais" em `components/layout/AppShell.tsx`, mesmo padrão de `NavLink` com `active={location.pathname.startsWith('/channels')}`.
- [x] 6.3 (apps/frontend) Atualizar `router.test.tsx` (se aplicável) para cobrir a nova rota, mesmo padrão dos mocks já existentes para `agents`/`mcp-servers`.

## 7. Verificação final

- [x] 7.1 (apps/frontend) Rodar a suíte de testes completa (Vitest) e o type-check do frontend, garantindo que nenhuma feature existente regrediu.
- [x] 7.2 (apps/frontend) Rodar a aplicação localmente (com `apps/inbox` no ar) e validar manualmente o fluxo completo: cadastro de canal WAHA, cadastro de canal Telegram, edição mantendo credencial, edição trocando credencial, ativar/desativar, exibição de `webhookUrl` por tipo.

## 8. Correção pós-implementação: CORS em apps/inbox (design.md, Decision 9)

- [x] 8.1 (apps/inbox) Criar `Options/CorsOptions.cs` espelhando `Buteco.Api.Options.CorsOptions`, e habilitar `AddCors`/`AddDefaultPolicy`/`app.UseCors()` em `Program.cs` — sem isso, todo preflight de `/channels` a partir do browser falha com 405/erro de CORS, em qualquer ambiente.
- [x] 8.2 (apps/inbox) Adicionar seção `Cors.AllowedOrigins` em `appsettings.json` (vazio) e `appsettings.Development.json` (`http://localhost:5173`, `http://localhost:3000`), mesmas origens já liberadas em `apps/api`.
- [x] 8.3 (apps/inbox) Confirmar `dotnet build` limpo. Suíte de testes de `apps/inbox` não pôde ser confirmada neste ambiente (Testcontainers exige Docker, indisponível no sandbox) — pré-existente, não causado por esta mudança (build limpo antes e depois do fix, falha é só `DockerUnavailableException` no fixture).

## 9. Correção pós-implementação: casing da credencial (design.md, Decision 2)

- [x] 9.1 (apps/frontend) Corrigir `buildCredential` em `ChannelForm.tsx` para serializar `ServiceUrl`/`SessionName`/`AuthToken`/`BotToken` (PascalCase), não `serviceUrl`/`sessionName`/`authToken`/`botToken` — o desserializador do backend (`WahaChannelConfigValidator`/`TelegramChannelConfigValidator`, sem `PropertyNameCaseInsensitive`) não casava com camelCase, gerando `"BotToken é obrigatório."` mesmo com o campo preenchido.
- [x] 9.2 (apps/frontend) Atualizar as asserções de payload de `ChannelForm.test.tsx`, `ChannelCreatePage.test.tsx` e `ChannelEditPage.test.tsx` para o JSON em PascalCase.
