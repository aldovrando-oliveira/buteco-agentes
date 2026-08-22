## Why

Hoje nenhuma API do projeto (`apps/api`, `apps/inbox`) exige autenticação —
pendência consciente registrada desde `backend-agente-a2a-mvp`, tolerável
enquanto o pior caso de exposição era cadastro de canal com credencial
write-only. A próxima fatia planejada (histórico de conversa por sessão na
UI do inbox) vai expor via API **conteúdo real de conversa de usuário
final** (mensagens de WhatsApp/Telegram, com identificação de contato) —
isso muda o perfil de risco de categoria. Autenticação precisa vir antes
dessa fatia, não depois, para não nascer já expondo dado sensível sem
proteção.

## What Changes

- **BREAKING**: `apps/api` passa a exigir um token válido (`Authorization:
  Bearer <token>`) em toda rota, exceto as três explicitamente
  classificadas como anônimas: `GET /health` (sonda de infraestrutura),
  `POST /auth/login` (ponto de entrada de autenticação — não pode exigir
  o que ele mesmo emite) e `GET
  /agents/{id}/.well-known/agent-card.json` (descoberta A2A pública,
  continua anônima).
- **BREAKING**: `apps/inbox` passa a exigir o mesmo tipo de token em toda
  rota, exceto as três explicitamente classificadas como anônimas:
  `GET /health` (sonda de infraestrutura), `POST /webhooks/{channelId}`
  (canais externos, não autenticável) e
  `POST /internal/push-notifications` (já protegida por um token de
  capacidade próprio, por `PendingDispatch` — mecanismo pré-existente,
  fora do escopo desta change).
- Novo endpoint `POST /auth/login` em `apps/api`: usuário único configurado
  via variável de ambiente (usuário + hash de senha), emite um token
  assinado de vida curta para o operador. Sem tabela de usuários, sem
  RBAC, sem cadastro, sem recuperação de senha, sem OAuth/SSO.
- `apps/inbox` passa a auto-emitir, no próprio processo, um token de
  serviço assinado com a mesma chave de assinatura compartilhada usada
  para validar o token do operador (`sub` distinto, sem interação humana)
  para autenticar suas chamadas a `apps/api` (`SendMessage` via
  `POST /agents/{id}/a2a`, `GET /agents/{id}`) — autorizado só para essas
  duas rotas, não para o restante da API.
- Checagem de integridade no startup de `apps/api` e `apps/inbox` — toda
  rota mapeada precisa estar classificada como autenticada ou anônima em
  uma allowlist explícita; o boot falha se sobrar rota não classificada
  (mesmo padrão de `ValidateChannelAdapterRegistrations` em
  `apps/inbox`).
- `apps/frontend` ganha uma tela de login e um módulo de token
  (leitura/anexação de header/limpeza em 401) importado por cada
  `request<T>` de feature — sem cliente HTTP compartilhado entre
  features (convenção existente preservada).
- `AgentCard` (protocolo A2A, `apps/api`) passa a declarar um
  `SecurityScheme`/`SecurityRequirement` HTTP Bearer para
  `/agents/{id}/a2a`, documentando de forma compatível com a spec A2A que
  o uso exige credencial, mesmo a descoberta sendo pública.

## Capabilities

### New Capabilities

- `operator-authentication`: login do operador único (credencial via env
  var, senha com hash) e emissão de um token assinado de vida curta em
  `apps/api`.
- `route-authentication`: enforcement de token nas rotas HTTP de
  `apps/api` e `apps/inbox` — allowlist explícita de rotas anônimas,
  checagem de integridade no startup (boot falha se houver rota não
  classificada), rejeição de requisição sem token ou com token inválido.
- `inbox-service-authentication`: `apps/inbox` autentica suas chamadas a
  `apps/api` com um token de serviço auto-emitido (mesmo mecanismo de
  assinatura do `operator-authentication`), escopado apenas às rotas que
  `apps/inbox` de fato consome.
- `operator-login-ui`: tela de login em `apps/frontend`, módulo de token
  por feature, redirecionamento e limpeza de sessão em resposta `401`.

### Modified Capabilities

- `a2a-agent-card`: o `AgentCard` retornado por
  `GET /agents/{id}/.well-known/agent-card.json` passa a declarar
  `SecuritySchemes`/`SecurityRequirements` (HTTP Bearer) para o endpoint
  A2A do agente.

## Impact

- **apps/api**: novo endpoint `POST /auth/login`; middleware de
  autenticação por token aplicado globalmente com allowlist de rotas
  anônimas; checagem de integridade das rotas no startup;
  `AgentCard` atualizado com `SecuritySchemes`/`SecurityRequirements`.
- **apps/inbox**: middleware de autenticação por token aplicado
  globalmente (com `POST /webhooks/{channelId}` e
  `POST /internal/push-notifications` fora do enforcement novo); checagem
  de integridade das rotas no startup; emissão do token de serviço no
  startup e anexação dele nos `HttpClient`s nomeados existentes
  (`AgentReferenceValidator`, `A2AClient`) usados para chamar `apps/api`.
- **apps/frontend**: nova feature de login (tela + módulo de token); as
  features existentes (`agents`, `mcp-servers`, `channels`) passam a
  anexar o header `Authorization` em cada `request<T>` e a tratar `401`
  (limpar token, redirecionar para login).
- **apps/workers**: sem impacto — confirmado que não expõe nenhum
  endpoint HTTP (Worker Service puro, consumidor RabbitMQ).
- **Configuração**: nova variável de ambiente de chave de assinatura de
  token, com o mesmo valor configurado independentemente em `apps/api` e
  `apps/inbox` (mesmo padrão já usado para as chaves de provedor de LLM
  entre `apps/api` e `apps/workers`); credencial do operador único
  (usuário + hash de senha) configurada em `apps/api`.
