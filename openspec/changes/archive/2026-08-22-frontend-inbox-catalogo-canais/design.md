## Context

`apps/inbox` já expõe o catálogo de canais de entrada completo
(`inbox-channel-catalog`, `inbox-channel-adapter-plugin`, mais os adapters
WAHA e Telegram): `POST/GET/PUT /channels`, `POST /channels/{id}/activate`,
`POST /channels/{id}/deactivate`. `apps/frontend` já tem duas features
irmãs consolidadas — `features/agents` e `features/mcp-servers` — com um
padrão de pastas, formulário, tratamento de erro e testes bem
estabelecido. Esta change adiciona `features/channels` seguindo
exatamente esse padrão, sem introduzir nenhuma biblioteca nova.

Investigação obrigatória (feita via `/opsx:explore` antes deste design)
confirmou três pontos que mudam decisões técnicas relevantes:

1. `ChannelResponse` tem `{ Id, ChannelType, Name, AgentId, IsActive,
   CreatedAt, UpdatedAt, WebhookUrl }` — nunca inclui credencial.
   `AgentId` é um `Guid` opaco (sem nome do agente embutido, `apps/inbox`
   não compartilha banco com `apps/api`).
2. O erro 502 (`ProvisioningFailed`, e o fallback de falha ao validar o
   agente) não é um `ValidationProblem` — é `ProblemDetails` padrão
   (`{ type, title, status, detail }`), mensagem única em `detail`, sem
   erros por campo. **Correção pós-investigação**: o texto original desta
   change afirmava que nenhuma página do frontend lia `detail` de uma
   resposta de erro, com base em checar só `AgentCreatePage`
   (`AgentValidationFailed`, também 502, que de fato cai num catch-all
   genérico sem ler o corpo) — sem checar `AgentMcpServersPage.tsx`.
   Durante a implementação (Decision 4) ficou confirmado que
   `AgentMcpServersPage.tsx` já lê `problem.detail` de um 502 e já
   renderiza via `<Alert title={...}>{detail ?? fallback}</Alert>` — o
   precedente real já existia, só não tinha sido encontrado pela
   investigação original.
3. Não existe endpoint de discovery de `ChannelType` em `apps/inbox` — só
   `/channels`, `/contacts`, `/webhooks/{channelId}` e push notifications.

Além disso, `apps/inbox` roda em `http://localhost:5027` em dev, porta
diferente de `apps/api` (`http://localhost:5017`, já usada por
`VITE_API_BASE_URL`) — não existe hoje nenhuma variável de ambiente
apontando para `apps/inbox` no frontend.

## Goals / Non-Goals

**Goals:**
- CRUD completo de canais em `apps/frontend`, consumindo só os endpoints
  já existentes de `apps/inbox`.
- Formulário de credencial específico por `ChannelType` (WAHA, Telegram),
  write-only, seguindo o padrão já estabelecido em `McpServerForm`.
- Exibição de `webhookUrl` com instrução por `ChannelType`.
- Tratamento visualmente distinto entre erro de validação (400, por
  campo) e falha de provisionamento (502, mensagem única).

**Non-Goals:**
- Nenhuma descoberta dinâmica de `ChannelType` (não existe endpoint hoje;
  ver Decision 1).
- Nenhuma mudança em `apps/inbox`, `apps/api` ou `apps/workers`, **exceto**
  a habilitação de CORS em `apps/inbox` (ver Decision 9) — descoberta como
  bloqueadora só ao rodar a aplicação de verdade no navegador, depois do
  design original ter sido aprovado sem esse item.
- Nenhuma biblioteca nova.
- Nenhum reprovisionamento manual disparável pela UI além de trocar a
  credencial no formulário de edição (o backend já reprovisiona
  automaticamente quando a credencial muda).
- Nenhuma tela de "sucesso" separada da página de detalhe (ver Decision 3).

## Decisions

### Decision 1 — Lista de `ChannelType` hardcoded no frontend

`ChannelForm` usa uma constante local (`["waha", "telegram"]`, com labels
`"WAHA"`/`"Telegram"`) para popular o `Select` de tipo de canal no
cadastro. Não existe, e não é criado, nenhum endpoint de discovery.

**Alternativa descartada**: endpoint `GET /channel-types` em `apps/inbox`
expondo `IChannelAdapterRegistry`. Descartada pelo mesmo raciocínio já
usado para rejeitar generalizar o registro de adapters no backend com só
dois consumidores reais — dois adapters não justificam manter um novo
contrato HTTP e um novo hook (`useChannelTypesQuery`) só para evitar uma
constante de duas strings. Se um terceiro adapter tornar isso doloroso,
revisitar então.

### Decision 2 — Sub-formulário de credencial por tipo, sem abstração prematura

`ChannelForm` mantém um mapa `channelType -> componente`:

```ts
const CREDENTIAL_FIELDS: Record<ChannelType, CredentialFieldsComponent> = {
  waha: WahaCredentialFields,
  telegram: TelegramCredentialFields,
};
```

Ao trocar `channelType` no `Select` (modo criação), `ChannelForm` troca o
sub-formulário exibido e reseta o estado de credencial — mesma cascata já
usada em `AgentForm` para Provider→Model (`form.setFieldValue('model',
'')` ao trocar `provider`).

Em **modo edição**, `channelType` é imutável (confirmado:
`UpdateChannelRequest` não tem esse campo, `Channel.UpdateDetails` não o
altera) — o formulário exibe o tipo como texto fixo (não um `Select`
editável) e renderiza só o sub-formulário correspondente ao tipo já
persistido.

Cada `*CredentialFields` componente controla campos tipados próprios
(`WahaCredentialFields`: `serviceUrl`, `sessionName`, `authToken`;
`TelegramCredentialFields`: `botToken`) e nunca é pré-preenchido — mesmo
princípio write-only de `McpServerForm`.

**Serialização para a wire**: `Credential` no `CreateChannelRequest`/
`UpdateChannelRequest` é uma **string JSON serializada** do objeto
esperado pelo validador do adapter (`WahaCredential(ServiceUrl,
SessionName, AuthToken)` / `TelegramCredential(BotToken)`) — não um campo
de texto livre. `ChannelForm.handleSubmit` monta o objeto a partir dos
campos do sub-formulário ativo e faz `JSON.stringify` antes de enviar.

**Correção pós-implementação: chaves em PascalCase, não camelCase — bug
da primeira implementação, só exposto ao cadastrar um canal de verdade,
não pelos testes (que mockam a chamada HTTP e nunca exercitam o
desserializador real do backend).** A primeira versão de
`ChannelForm.tsx` serializava com chaves camelCase
(`{ serviceUrl, sessionName, authToken }` / `{ botToken }}`), convenção
natural em TS mas que não bate com o que `WahaChannelConfigValidator`/
`TelegramChannelConfigValidator` (e os demais pontos que desserializam a
credencial: `TelegramWebhookProvisioner`, `TelegramOutboundMessageSender`,
`WahaOutboundMessageSender`) esperam: todos chamam
`JsonSerializer.Deserialize<T>(credential)` **sem**
`PropertyNameCaseInsensitive` — diferente, de propósito, dos parsers de
webhook externo (`TelegramInboundWebhookHandler`/`WahaInboundWebhookHandler`,
que definem `JsonOptions` com `PropertyNameCaseInsensitive = true` para os
payloads das plataformas externas). Com chave camelCase, o
`Deserialize<TelegramCredential>` não lança exceção — só deixa `BotToken`
null, e a validação de campo obrigatório acusa `"BotToken é obrigatório."`
mesmo com o token preenchido no formulário. Confirmado contra
`WahaChannelConfigValidatorTests.cs`/`TelegramChannelConfigValidatorTests.cs`,
que montam seus fixtures via `JsonSerializer.Serialize(new
WahaCredential(...))`/`new TelegramCredential(...)` — produzindo PascalCase
pelas propriedades do record, sem naming policy — confirmando que o
contrato de fato já testado no backend sempre foi PascalCase. Corrigido em
`buildCredential` (`ChannelForm.tsx`) para serializar `ServiceUrl`,
`SessionName`, `AuthToken`, `BotToken`; nenhuma mudança em `apps/inbox`
foi necessária desta vez — o contrato já estava certo no texto original
deste design (linha acima), só a implementação em código divergiu dele.

**Regra de atualização atômica (edição)**: como a credencial é um único
blob JSON opaco por trás do endpoint (`credential` omitido/null mantém a
credencial existente por inteiro — não há merge por subcampo no
backend), o sub-formulário de credencial em modo edição segue tudo-ou-nada:
- se **todos** os campos do sub-formulário ativo estiverem vazios,
  `credential` é omitido do `PUT /channels/{id}` (mantém a atual);
- se **qualquer** campo for preenchido, a validação do `ChannelForm`
  exige que **todos** os campos obrigatórios daquele tipo estejam
  preenchidos antes de habilitar o submit, e o blob inteiro substitui a
  credencial persistida.
Sem essa regra client-side, um preenchimento parcial (ex. só trocar o
`authToken` do WAHA) chegaria ao backend como um JSON incompleto e voltaria
como `InvalidCredential` com mensagens (`"serviceUrl deve ser uma URL
absoluta"`, `"sessionName é obrigatório"`) que confundiriam um usuário
que só queria trocar o token.

### Decision 3 — `webhookUrl`: só na página de detalhe, sem tela de sucesso separada

`ChannelDetailPage` exibe `webhookUrl` num campo somente-leitura em
destaque, com botão de copiar, e um texto de instrução que varia por
`ChannelType`:
- **WAHA**: instrução para configurar manualmente a sessão apontando essa
  URL como webhook (mesmo texto do checklist manual já documentado no
  backend, `inbox-adapter-waha`).
- **Telegram**: indicação de que o webhook já foi configurado
  automaticamente no cadastro, sem ação manual necessária.

`ChannelCreatePage` **não** ganha uma tela de sucesso intermediária.
Segue o mesmo padrão já usado por `AgentCreatePage`/`McpServerCreatePage`:
notificação de sucesso (`notifications.show`) e redirecionamento imediato
para `ChannelDetailPage`. Como o detalhe já exibe `webhookUrl` em
destaque, o redirecionamento imediato já entrega a informação acionável
sem duplicar UI ou introduzir um padrão de página novo só para este caso.

### Decision 4 — Erro 400 (`InvalidCredential`/`AgentNotFound`) vs 502 (`ProvisioningFailed`) tratados de forma distinta

`channelsApi.ts` estende o `ValidationProblemDetails` já usado nas outras
features com um campo `detail?: string` (o shape real do `ProblemDetails`
de 502 devolvido por `ChannelEndpoints`), e o `ApiError` passa a carregar
esse campo:

```ts
export interface ChannelProblemDetails {
  title?: string;
  status?: number;
  errors?: Record<string, string[]>; // presente só em 400 (ValidationProblem)
  detail?: string;                    // presente só em 502 (Problem)
}
```

`ChannelCreatePage`/`ChannelEditPage` distinguem os dois casos pelo
`status` da `ApiError`, não por um catch-all único:
- **400** → `fieldErrorsFrom` (mesmo padrão de `AgentForm`/`McpServerForm`)
  extrai `problem.errors` e passa para os campos do formulário.
- **502** → um `Alert` de erro dedicado, renderizado acima do formulário
  (não um toast, não um erro de campo), com o texto de `problem.detail`
  quando presente, e uma mensagem genérica de fallback quando `detail`
  vier ausente (ex. corpo não-JSON).

**Correção durante a implementação: padrão reaproveitado de
`AgentMcpServersPage.tsx`, não inventado — a investigação original desta
change não tinha encontrado esse precedente.** O texto acima, na versão
originalmente aprovada, descrevia este `Alert` como "um padrão novo neste
frontend — não existe hoje nenhuma tela consumindo `detail` de uma
resposta de erro". Isso partia de uma investigação incompleta: checou
`AgentCreatePage.tsx` (que trata seu 502 num catch-all genérico) mas não
checou `AgentMcpServersPage.tsx`, que já resolve exatamente esse
problema — um estado `{ title, detail }` setado no `onError` da mutation
quando `error.status === 502`, renderizado como
`<Alert color="red" title={submitError.title}>{submitError.detail ??
fallback}</Alert>`. A implementação de `ChannelCreatePage`/
`ChannelEditPage` seguiu esse padrão já existente ao pé da letra, em vez
de desenhar um formato próprio — mesma estrutura de estado, mesmo
componente `Alert`, mesmo fallback quando `detail` está ausente.

### Decision 5 — Mensagens de erro de credencial (campo único, múltiplas mensagens)

Os validadores de credencial no backend (`WahaChannelConfigValidator`,
`TelegramChannelConfigValidator`) sempre devolvem os erros sob uma única
chave `"credential"` no `errors` do 400 — nunca por subcampo
(`serviceUrl`/`sessionName`/`authToken`/`botToken`), e o WAHA pode
devolver várias mensagens simultâneas nesse array (ex. `serviceUrl`
inválido **e** `sessionName` vazio ao mesmo tempo).

O padrão `fieldErrorsFrom` já existente (`messages[0]` por campo) perderia
mensagens adicionais nesse cenário. `ChannelForm` trata `errors.credential`
como lista completa — todas as mensagens de `problem.errors.credential`
são exibidas juntas (lista, não só a primeira) num alerta abaixo do
sub-formulário de credencial ativo, já que o backend não dá granularidade
por subcampo para mapear em inputs individuais.

### Decision 6 — Nova variável de ambiente para a base URL de `apps/inbox`

`channelsApi.ts` introduz `VITE_INBOX_BASE_URL` (default
`http://localhost:5027`, mesmo padrão de fallback hardcoded já usado em
`agentsApi.ts`/`mcpServersApi.ts` para `VITE_API_BASE_URL`), com a
entrada correspondente adicionada em `.env` e `.env.example`. Não existe
hoje nenhuma variável apontando para `apps/inbox` no frontend — sem essa
variável o CRUD de canais simplesmente não teria como alcançar
`apps/inbox` em dev (porta `5027`, diferente da porta `5017` de
`apps/api` já usada pelas features existentes).

`channelsApi.ts` duplica seu próprio `request<T>`/`ApiError` local, assim
como `agentsApi.ts` e `mcpServersApi.ts` já fazem — não existe hoje um
cliente HTTP compartilhado entre features neste frontend, e introduzir um
agora (para só três consumidores) seria abstração prematura fora do
escopo desta change.

### Decision 7 — Resolução de `AgentId` para nome do agente

`ChannelListPage` e `ChannelDetailPage` cruzam o `AgentId` (Guid opaco)
de cada canal com `useAgentsQuery()` para exibir o nome do agente
responsável, no mesmo padrão de rótulo já usado em
`AgentDelegationsSection` (`agent.name`, com sufixo `" (inativo)"` quando
`isActive === false`). Diferente da delegação, não há exclusão por id
(um canal nunca referencia "a si mesmo").

### Decision 8 — Campo `AgentId` no formulário

`ChannelForm` usa um `Select` alimentado por `useAgentsQuery()`, mesmo
componente e padrão de opções de `Decision 7` (nome + sufixo
`" (inativo)"`). Vínculo a agente inativo é permitido — o backend não
valida `isActive` do agente ao cadastrar/atualizar um canal (design.md de
`inbox-channel-catalog`, Decision 7), então o frontend não bloqueia essa
seleção.

### Decision 9 — CORS em `apps/inbox`

**Correção pós-implementação: gap não previsto por nenhuma investigação
anterior — só apareceu ao rodar a aplicação de verdade no navegador,
depois do design original (sem este item) já ter sido aprovado.**
`apps/inbox` nunca teve nenhuma configuração de CORS — sem
`AddCors`/`UseCors`, sem seção `Cors` em `appsettings*.json`. Isso nunca
foi um problema porque, até esta change, `apps/inbox` não tinha nenhum
consumidor via browser: só `apps/api` (que já tem CORS configurado,
`Buteco.Api.Options.CorsOptions`, para servir `apps/frontend`) e
`apps/workers`/webhooks externos, nenhum dos quais passa por preflight.
`ChannelForm`/`ChannelListPage` são o primeiro caso em que um browser
chama `apps/inbox` diretamente — e sem CORS, o preflight `OPTIONS` de
`/channels` não bate com nenhum verbo mapeado pela Minimal API, a
requisição real nunca chega a sair, e o browser reporta isso como erro de
CORS (visto em produção deste change: 405 no preflight, "CORS error" no
fetch).

A correção espelha exatamente o padrão já existente em `apps/api`, sem
inventar nada novo: `Buteco.Inbox.Options.CorsOptions` (mesmo shape de
`Buteco.Api.Options.CorsOptions`), `AddCors`/`AddDefaultPolicy` lendo
`AllowedOrigins` da config, `app.UseCors()` antes do mapeamento dos
endpoints, `AllowedOrigins: []` em `appsettings.json` (prod segue
fechado) e `["http://localhost:5173", "http://localhost:3000"]` em
`appsettings.Development.json` — as mesmas origens já liberadas para
`apps/api`.

Isso significa que o Non-Goal "nenhuma mudança em apps/inbox" (Goals/
Non-Goals acima) não se sustentou por inteiro: a mudança em `apps/inbox`
foi mínima (config + wiring, nenhuma regra de negócio) e estritamente
necessária para o CRUD funcionar a partir do browser — sem ela, nenhum
endpoint de `/channels` é alcançável pelo frontend em nenhum ambiente.

## Risks / Trade-offs

- [Risco] Preenchimento parcial do sub-formulário de credencial na
  edição gera `InvalidCredential` confuso (usuário só queria trocar um
  campo) → Mitigação: regra tudo-ou-nada client-side da Decision 2.
- [Risco] `.env` local do desenvolvedor sem `VITE_INBOX_BASE_URL`
  quebraria silenciosamente as chamadas a `/channels` → Mitigação:
  default hardcoded para `http://localhost:5027` em `channelsApi.ts`
  (mesmo padrão de fallback já usado nas outras features) e atualização
  de `.env.example`.
- [Risco] Corpo do 502 não é JSON válido (gateway realmente fora do ar) →
  `detail` fica `undefined` → Mitigação: fallback do `ApiError` já
  existente (`.catch(() => undefined)` no parse do corpo) cobre isso; o
  `Alert` da Decision 4 cai numa mensagem genérica de fallback quando
  `detail` está ausente.
- [Risco] Múltiplas mensagens de erro de credencial (Decision 5) sendo
  tratadas com `messages[0]`, escondendo problemas adicionais →
  Mitigação: `ChannelForm` trata `credential` como lista completa, não
  reaproveita `fieldErrorsFrom` cegamente para esse campo específico.
- [Risco] `apps/inbox` sem CORS bloquearia toda chamada do browser com
  405 no preflight / erro de CORS, em qualquer ambiente, sem nenhuma
  mensagem de erro útil na tela (confirmado em produção deste change,
  antes da correção) → Mitigação: Decision 9, CORS habilitado em
  `apps/inbox` espelhando o padrão já existente em `apps/api`.
- [Risco] Casing incorreto (camelCase) na serialização do JSON de
  credencial passaria despercebido pela suíte de testes do frontend
  (mockam a chamada HTTP, nunca desserializam de verdade) e só apareceria
  ao cadastrar um canal contra `apps/inbox` real (confirmado em produção
  deste change, antes da correção: `"BotToken é obrigatório."` mesmo com
  o token preenchido) → Mitigação: `buildCredential` corrigido para
  PascalCase (`ServiceUrl`/`SessionName`/`AuthToken`/`BotToken`), conforme
  já descrito (mas não implementado) no texto original desta Decision.
