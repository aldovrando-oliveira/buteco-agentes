# Buteco Agentes — Arquitetura e Convenções

> Documento de referência estável. Atualizar só quando uma decisão
> arquitetural nova mudar algo aqui descrito — não a cada change aplicada
> (isso vai no arquivo de histórico/status, separado).

## O que é

Plataforma multi-agente com protocolo A2A (Agent-to-Agent), permitindo
cadastrar agentes de IA, vinculá-los a ferramentas externas (MCP),
delegação entre agentes, e integração com canais de entrada reais
(WhatsApp via WAHA, Telegram) através de um CRM mínimo de contatos e
sessões.

Workflow de desenvolvimento: spec-driven via OpenSpec
(`/opsx:explore` → `/opsx:propose` → revisão → `/opsx:apply` →
`/opsx:sync` → `/opsx:archive`). Specs vivos em
`openspec/specs/{capability}/spec.md`.

## Os quatro apps

| App | Papel | Stack | Banco |
|---|---|---|---|
| `apps/api` | CRUD de agentes, catálogo MCP, delegação, protocolo A2A (`SendMessage`/`GetTask`), AgentCard, push notification (emissor), login do operador e emissão de token | .NET 10, ASP.NET Core Minimal API, CQRS via `Mediator.Abstractions`+`SourceGenerator` v3.0.2, EF Core+Npgsql, `RabbitMQ.Client` (publisher), pacote `A2A` | Postgres compartilhado com `apps/workers` (mesmas tabelas, dois `AppDbContext` mantidos sincronizados por disciplina, não por schema separado) |
| `apps/workers` | Executa tasks: chama o LLM, resolve tools MCP, executa delegação, dispara push notification | .NET 10 Worker Service, `Microsoft.Agents.AI` 1.15.0 (`ChatClientAgent`, `Compaction`), EF Core mirror, consumidor RabbitMQ | Mesmo Postgres de `apps/api` |
| `apps/frontend` | UI de gestão (agentes, MCP, delegação, canais), tela de login, sessões e histórico de conversa por canal | React 19.2+, TypeScript, Vite, Mantine v9, `react-router` v8, `@tanstack/react-query` v5, `react-markdown`+`remark-gfm`, Vitest+Testing Library | — |
| `apps/inbox` | Catálogo de canais de entrada, CRM (Contact/Session), histórico de mensagens, orquestrador de debounce, adapters de canal (WAHA, Telegram) | .NET 10 Minimal API, CQRS próprio (Mediator), EF Core | Postgres **próprio** (`buteco_inbox`), isolado — sem tabela em comum com `apps/api`/`apps/workers` |

Isolamento estrito entre apps: nenhum `ProjectReference` cruzado.
Referências entre domínios de apps diferentes são sempre validadas via
HTTP (ex. `apps/inbox` valida `AgentId` chamando `GET /agents/{id}` em
`apps/api`), nunca por FK direta. Essas chamadas HTTP entre apps são
autenticadas (ver "Autenticação", abaixo).

## Modelo de domínio (estado atual)

**`Agent`** (`apps/api`): `Name`, `Instructions`, `IsActive`, `Provider`/
`Model` (nullable — agente "precisa de reconfiguração" quando nulos),
`Description` (nullable), `Skills` (jsonb, `{ Name, Description? }`),
vínculos N:N: `McpServers` (via `AgentMcpServer`, com `AllowedTools`
jsonb por vínculo) e `DelegatesTo` (via `AgentDelegation`, **unidirecional**
— A→B não implica B→A).

**`McpServer`** (`apps/api`): `Name`, `Description`, `Url`, `AuthType`
(`None`/`BearerToken`), `EncryptedCredential` (AES-GCM).

**`Channel`** (`apps/inbox`): `ChannelType` (string aberta, validada em
runtime contra adapters efetivamente registrados via DI — não enum
fechado), `Name`, `EncryptedCredentials` (AES-GCM, chave própria de
`apps/inbox`, shape opaco que varia por `ChannelType`), `AgentId` (Guid
opaco, validado via HTTP contra `apps/api`), `IsActive`, `WebhookUrl`
(computada, nunca persistida).

**`Contact`/`Session`** (`apps/inbox`, CRM): `Contact` identificado por
`(ChannelId, ExternalId)` único — mesmo `ExternalId` em canais diferentes
gera `Contact`s distintos, sem unificação de identidade entre canais.
`Contact` tem dois campos de origem externa com semânticas **opostas de
propósito**: `Metadata` (congelado na criação) e `DisplayName` (nullable,
reescrito a cada mensagem de entrada, extraído do `pushName` no WAHA e do
`username`/`first_name` no Telegram). `Session` amarra várias conversas
do mesmo `Contact` ao longo do tempo; fronteira por **inatividade
automática** (timeout configurável) — sem encerramento explícito, e o
estado aberta/encerrada não é exposto pela API (ver histórico).

**`PendingDispatch`** (`apps/inbox`): buffer de debounce persistido por
`Session`, mensagens agrupadas antes de disparar `SendMessage` real
contra `apps/api`. Concorrência otimista via `xmin` do Postgres. É
**buffer, não histórico**: some quando o ciclo de disparo termina.

> **Área sensível**: a coleção de mensagens do `PendingDispatch` é
> owned/JSON e já produziu perda silenciosa de mensagem sob concorrência
> real (aliasing de change tracker do EF Core após re-leitura na mesma
> instância de `DbContext`). Corrigido com detach+rebusca. A varredura
> feita em `inbox-mensagens-persistidas` classificou todas as superfícies
> owned/JSON do repo e confirmou que essa é a única com o padrão de risco
> — a distinção que importa é entre `OwnsMany().ToJson()` (snapshot
> estrutural por elemento) e `HasConversion`+`ValueComparer` (serializa o
> valor inteiro a cada `SaveChanges`, imune ao aliasing). Qualquer mudança
> que encoste nessa coleção, ou que re-leia a entidade depois de mutá-la,
> precisa de teste com concorrência de verdade — não caminho feliz.

**`Message`** (`apps/inbox`): histórico durável por `Session`, **tabela
relacional própria, deliberadamente separada do `PendingDispatch`** — não
o estende nem toca sua coleção owned/JSON, justamente para não reabrir a
área sensível acima. Guarda `Direction`, `Content`, `ContentType`
(`Text`/`Image`/`Audio`/`Document` — marcador de tipo; mídia binária não
é persistida), `OccurredAt`, e, conforme a direção:

- **entrada**: identificador externo da mensagem (índice único parcial,
  dedup de webhook reentregue) e `DispatchStatus`
  (`Pending`/`Dispatching`/`Failed`/`Completed`), espelhado dos pontos que
  mutam ou removem o `PendingDispatch` e **sobrevivendo à remoção dele** —
  é o que torna o silêncio de uma conversa legível na UI. `Failed` agrupa
  três causas distintas de "não haverá resposta" sob um valor só, decisão
  consciente;
- **saída**: `DeliveryStatus` (`Sent`/`Failed`, com motivo) — sucesso ou
  falha do **envio ao provedor**, nunca recibo de entrega ou leitura do
  destinatário final. Essa distinção é de contrato, não de UI: a interface
  renderiza um indicador só, jamais dois, porque o dado não existe.

Os quatro enums de `Message` atravessam a API como **string**
(`JsonStringEnumConverter` por enum, mesmo padrão de `McpServerAuthType`),
nunca como inteiro ordinal.

## Autenticação

**Token stateless assinado com HMAC**, sem biblioteca JWT e sem sessão em
banco. `apps/api` emite (login do operador e token de serviço); `apps/api`
e `apps/inbox` validam **localmente**, compartilhando apenas a chave de
assinatura via configuração — nenhuma chamada de rede entre os processos
para validar token. Foi o que permitiu autenticar dois apps com bancos
isolados sem introduzir store compartilhado.

- **Operador único**, credencial via variável de ambiente (usuário +
  hash PBKDF2), `POST /auth/login` em `apps/api`. Sem tabela de usuários,
  sem RBAC. TTL de 30 minutos por padrão, configurável — o default vive
  no tipo de Options, não no `appsettings.json`.
- **Token de serviço** (`apps/inbox` → `apps/api`): mesmo mecanismo de
  assinatura, `sub` distinto, assinado a cada requisição de saída com TTL
  fixo curto, **escopado** — só autoriza as rotas que `apps/inbox` de
  fato consome; qualquer outra responde `403`.
- **Enforcement por padrão**: toda rota HTTP de `apps/api` e `apps/inbox`
  exige token; as exceções vivem numa allowlist explícita com motivo
  classificado por enum, validada no startup (ver convenção 8).
- **Frontend**: módulo fino de token sobre `sessionStorage`
  (ler/anexar/limpar em `401`), importado por cada `request<T>` de
  feature — sem cliente HTTP compartilhado (convenção 7 preservada).

Rotas anônimas são decisão de segurança, não detalhe de implementação:
adicionar uma passa por revisão.

## Fuso horário do sistema (`apps/workers`)

Fuso e idioma usados pelo worker para renderizar data/hora são decisão de
sistema, não de agente: `TZ` do SO, único para o processo inteiro,
resolvido no startup — sem opção por agente, sem chave em
`appsettings.json`. `TZ` deve usar o nome IANA canônico da tz database
(ex. `America/Sao_Paulo`), sem o prefixo POSIX `:` — o `.NET` resolve o
fuso corretamente com o prefixo, mas o remove do identificador que expõe,
o que quebraria a checagem abaixo mesmo com a configuração correta. O
processo falha a inicialização se o fuso resolvido não corresponder
exatamente ao valor declarado (checagem de integridade no startup,
convenção 8) — cobre `TZ` ausente, vazia ou inválida com o mesmo erro.
Dia da semana em texto renderizado pelo worker é sempre pt-BR, pelo mesmo
motivo — nunca herdado de `CurrentCulture` do host.

## AgentCard / protocolo A2A

Cada agente expõe `GET /agents/{id}/.well-known/agent-card.json`,
montado a cada requisição a partir do estado atual (sem cache).
`Skills` do agente mapeadas para `AgentSkill` via slug determinístico
com dedupe. Push notification (`pushNotificationConfig` no `SendMessage`)
suportado ponta a ponta — `apps/workers` dispara webhook ao concluir uma
task, fire-and-forget, sem retry.

Descoberta é pública, uso não: o `AgentCard` continua acessível sem
token, mas declara `SecuritySchemes`/`SecurityRequirements` (HTTP Bearer)
para o endpoint A2A do agente — a exigência de credencial fica declarada
de forma compatível com a spec, não implícita.

## Contrato de plugin de canal (`apps/inbox`)

Três contratos obrigatórios em conjunto por `ChannelType`, resolvidos via
DI **keyed** (`AddKeyedSingleton`): `IChannelConfigValidator` (valida
credencial antes de criptografar), `IOutboundMessageSender` (entrega
resposta do agente ao canal), `IInboundWebhookHandler` (processa webhook
recebido em `POST /webhooks/{channelId}`, rota genérica única). Um quarto
contrato **opcional**, `IChannelWebhookProvisioner` (configura o webhook
automaticamente do lado externo no cadastro — só o Telegram implementa;
WAHA fica manual). Checagem de integridade no startup
(`ValidateChannelAdapterRegistrations`) garante que os contratos
obrigatórios (e o opcional, quando presente) estejam completos por tipo,
falhando o boot se não estiverem.

O contrato **não** carrega status de entrega: `IOutboundMessageSender` que
retorna sem lançar é sucesso, exceção é falha. Foi o que permitiu
persistir status de entrega sem tocar o contrato de plugin.

Adapters reais hoje: **WAHA** (self-hosted, engine GOWS, config manual do
webhook, sem verificação de autenticidade do webhook de entrada — risco
aceito, classificado explicitamente na allowlist de rotas anônimas) e
**Telegram** (Bot API, `setWebhook` automático com `secret_token` gerado
por canal, verificação nativa do webhook de entrada).

## Convenções estabelecidas (o "estilo da casa")

Essas regras não estão escritas em nenhum lugar do código — são o
padrão que se formou change após change. Vale checar contra elas antes
de propor algo nesta base:

1. **Sequenciamento**: catálogo/cadastro → vínculo → execução → UI,
   sempre nessa ordem, em toda linha de trabalho (MCP, delegação, canais,
   histórico de conversa). Corolário: se a etapa de UI descobrir que
   precisa de um dado que o backend não serve, isso é achado a reportar e
   sequenciar — nunca backend feito de improviso dentro da change de tela.
2. **Sem abstração prematura**: esperar 2-3 consumidores reais antes de
   extrair código compartilhado (`libs/ProviderCatalog` só nasceu com 2
   consumidores reais). O mesmo vale para configuração: uma opção só
   existe quando há cenário real de alguém precisar de outro valor — TTL
   do token de operador é configurável (política de produto, varia por
   ambiente), TTL do token de serviço não é (nunca sai do processo).
   Mesmo raciocínio decide jsonb vs. tabela relacional: jsonb quando não
   há necessidade de FK/consulta relacional, tabela quando os dois lados
   do vínculo são entidades com identidade própria.
3. **Credenciais**: sempre write-only, nunca retornadas em nenhuma
   resposta, criptografadas com AES-GCM (chave por domínio/app via env
   var), padrão "deixe em branco para manter a atual" na edição. Segredo
   compartilhado entre apps (chave de assinatura de token) vai por
   configuração com o mesmo valor nos dois, nunca por chamada entre
   processos.
4. **Degradação graciosa**: falha de dependência externa (MCP, alvo de
   delegação, entrega de webhook, envio ao canal) nunca derruba a task
   principal — é logada, não propagada. Quando a falha precisa ser visível
   para o operador, ela vira **estado persistido além do log**; a
   degradação continua graciosa, só deixa de ser invisível.
5. **Testes de integração com infraestrutura real** — Testcontainers
   Postgres/RabbitMQ, não mocks, para os caminhos principais; fakes só
   para dependências HTTP externas. Toda mudança de comportamento pede
   asserção explícita (nunca "processado corretamente" genérico), e todo
   caso "com item" ganha o par "sem item"/vazio testado.
6. **Nunca confiar em SDK de terceiro de memória** — decompilar
   (`ilspycmd`) ou buscar documentação real antes de codificar contra
   qualquer comportamento não verificado. Vale também para nomes de
   header, campos de payload e rotas de sistemas externos citados dentro
   de spec: requisito errado sobrevive ao archive.
7. **Frontend**: página busca dados e repassa como prop; componente
   apresentacional nunca importa hook de query/mutation diretamente.
   Cada feature mantém seu próprio `request<T>`/`ApiError` fino — sem
   cliente HTTP compartilhado entre features. Preocupação transversal
   (token) entra como módulo fino que cada `request<T>` importa, não como
   cliente comum. Feature se organiza por conceito de domínio, não por
   origem do dado — uma rota `/channels/{id}/algo` pode pertencer a outra
   feature que não `channels`.
8. **Checagem de integridade no startup** é o padrão para todo registro
   que possa ficar incompleto em silêncio — não documentação em prosa. Já
   aplicado a três casos de natureza diferente: DI keyed para pontos de
   extensão tipo-plugin (contrato de canal), classificação de rotas
   anônimas (autenticação), e configuração de fuso horário do sistema
   (`apps/workers` — valor resolvido vs. valor declarado em `TZ`, não só
   presença da variável). A checagem vale nos **dois sentidos** quando
   houver lista esperada e realidade mapeada: item declarado sem
   contraparte real é tão problema quanto o inverso. DI keyed continua
   sendo o padrão para pontos de extensão tipo-plugin.
9. **Toda vez que a implementação diverge do `design.md` aprovado**
   (um achado técnico durante a implementação muda a decisão), o
   `design.md` é corrigido pra refletir a causa real — nunca fica só
   registrado no resumo do chat.
10. **Todo risco nomeado na seção de Risks tem contraparte verificável**
    — cenário na spec e teste, ou uma justificativa explícita de por que
    não é testável. Risco listado e não coberto é o padrão de falha mais
    caro desta base, porque parece cuidado sem ser.
11. **Teste de acordo entre dois lados usa o artefato real produzido por
    um contra o outro** — nunca um artefato forjado no teste com a mesma
    configuração dos dois lados. Fixture montado pelo próprio teste passa
    igual com o comportamento certo e com o errado, e por isso não prova
    nada: já aconteceu com a chave de assinatura de token entre `apps/api`
    e `apps/inbox`, e com o formato de fio dos enums de `Message` entre
    `apps/inbox` e `apps/frontend`. Na prática: token emitido pela outra
    API de verdade, JSON bruto lido da resposta real em vez de round-trip
    pelo mesmo tipo, e — quando o ponto é provar independência — a outra
    ponta deliberadamente inalcançável durante o teste.
12. **Contrato entre apps inclui o formato de fio, não só os campos** —
    nome, tipo e forma de serialização (enum como string, nunca ordinal)
    fazem parte do requisito. Enum sem formato declarado já saiu como
    inteiro por omissão e obrigou o consumidor a decodificar por índice.
    Defeito de formato pertence a quem expõe, e se corrige lá, em change
    própria sequenciada antes — não se contorna no consumidor.
13. **A UI nunca afirma mais do que o sistema sabe** — se o dado não é
    coletado, a interface não o insinua. Um único indicador de envio,
    jamais dois checks, porque entrega e leitura são recibos que o desenho
    escolhido não coleta; valor de enum desconhecido renderiza indicador
    neutro, nunca reaproveita o indicador de sucesso. A asserção que
    protege isso é a **negativa** (afirmar a ausência do segundo
    indicador), porque é ela que impede a regressão bem-intencionada de
    "deixar parecido com o WhatsApp".