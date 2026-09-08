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
automática** (timeout configurável) — sem encerramento explícito. Ao
expirar, a `Session` anterior tem `ClosedAt` preenchido
(`inbox-session-indice-unico`) e um índice único parcial
(`sessions."ContactId" WHERE "ClosedAt" IS NULL`) garante, por banco, no
máximo uma `Session` aberta por `Contact` — mesmo idioma de índice único
+ catch + detach + re-busca já usado por `Contact`/`PendingDispatch`
abaixo. O estado aberta/encerrada é **derivável** por isso, mas ainda não
é promovido como contrato de API documentado (ver histórico).

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

**`KnowledgeBase`** (`apps/api`): `Name`, `Description`, `IsActive`.
`Description` **não é campo decorativo** — a partir da etapa de execução é o
texto que vira a descrição da tool exposta ao modelo, e é por ele que o modelo
decide se a base é relevante para a pergunta; por isso é obrigatória e não
vazia, ao contrário de `McpServer.Description`. Segue o padrão da casa:
`IsActive`, sem exclusão.

**`KnowledgeDocument`** (`apps/api`): `KnowledgeBaseId`, `Title`, `SourceType`,
`ExtractedText`, `ContentLengthBytes`, `IndexingStatus`, `IndexedAt` (nullable),
`FailureReason` (nullable), `ContentRevision`. Quatro coisas que não se
adivinham lendo os campos:

- **`ContentLengthBytes` é coluna gerada pelo Postgres**
  (`GENERATED ALWAYS AS (octet_length("ExtractedText")) STORED`), nunca escrita
  pela aplicação — não existe caminho de escrita de conteúdo que a deixe
  defasada. Está em **bytes UTF-8**, a mesma unidade do teto de 1 MiB validado
  no cadastro, e mede a mesma string que a validação mede (o texto **já
  extraído**: a extração remove BOM e normaliza `CRLF`, então validar a entrada
  crua faria os dois números medirem coisas diferentes). Coluna gerada em vez de
  projeção porque `string.Length` traduz para `length()`, que conta caracteres,
  e o provider Npgsql não tem mapeamento LINQ para `octet_length`.
- **`SourceType` é string aberta**, não enum fechado: identifica o extrator a
  aplicar, resolvido via DI **keyed**, com checagem de integridade bidirecional
  no startup — mesmo idioma de `Channel.ChannelType`. Extensão de arquivo e
  `SourceType` são conceitos distintos (o cliente sugere `markdown` para
  `.md`/`.markdown`/`.txt`; texto puro é markdown válido). A extração
  **preserva a marcação**: é normalização, não conversão para texto puro, porque
  a fragmentação da etapa de indexação divide por cabeçalho.
- **`IndexingStatus` tem exatamente quatro valores** (`Pending`/`Indexing`/
  `Indexed`/`Failed`) e **não** ganha um valor para reindexação: a distinção
  entre "nunca indexado" e "há conteúdo indexado respondendo agora" é carregada
  por `IndexedAt` (nulo × preenchido), em qualquer dos quatro estados. A regra
  para a UI é uma só: informação derivada da indexação aparece sempre que
  `IndexedAt` não for nulo, e é omitida quando for — nunca zerada, que afirmaria
  que a indexação rodou e não achou nada.
- **`ContentRevision` é coluna explícita, não `xmin`**, e incrementa **apenas**
  quando `ExtractedText` muda. O consumidor de indexação muta a própria linha ao
  transicionar de estado, e um token de linha invalidaria o próprio trabalho em
  curso; esta coluna, que ele nunca escreve, permanece estável ao longo das
  transições dele.

Na etapa de catálogo, `Indexing`/`Indexed`/`Failed`, `IndexedAt` e
`FailureReason` **nascem sem nenhum escritor** — não há fila nem consumidor, e
todo documento criado ou atualizado permanece `Pending` indefinidamente. Isso é
requisito declarado, não defeito: quem os escreve é o consumidor da etapa de
indexação.

**`KnowledgeDocument` é a única entidade do repositório com exclusão real**
(`DELETE`, primeiro `MapDelete` da base). Ver "Exclusão: catálogo × conteúdo",
abaixo.

## Exclusão: catálogo × conteúdo

O padrão da casa era, até aqui, **soft delete por `IsActive`** com o filtro
aplicado no momento da resolução — e não havia nenhum `MapDelete` no
repositório. Esse padrão não é uma regra sobre "tudo": ele se formou para
**entidades de catálogo com vínculos apontando para elas**. Um `McpServer`
desativado continua referenciado por `AgentMcpServer`, e o histórico de
execuções que o usou precisa continuar fazendo sentido; apagá-lo quebraria
leitura de passado.

`KnowledgeDocument` é a primeira entidade que não se encaixa nisso: é
**conteúdo**, nada aponta para ele além dos seus próprios fragmentos, e o caso
de uso concreto — o operador subiu o arquivo errado, ou um com dado que não
devia estar ali — é exatamente aquele em que "continua no banco, invisível" é a
resposta errada. Some-se o custo medido: ~60 MB de vetores por 7.500 fragmentos.

O critério, reutilizável para qualquer entidade futura, é esse: **catálogo
referenciado → `IsActive`; conteúdo sem referência → exclusão real.** Duas
consequências práticas registradas junto:

- A FK de `KnowledgeDocument` para `KnowledgeBase` usa **`Restrict`**, não o
  `Cascade` default do EF Core. Hoje é inerte (a base não tem exclusão); a
  diferença é qual das duas falha de forma segura se alguém adicionar exclusão
  de base um dia — `Restrict` obriga a decidir o destino dos documentos em vez
  de os apagar em silêncio.
- `DELETE` numa rota que não oferece o verbo responde **405**, não 404 — a
  distinção entre "recurso inexistente" e "operação não oferecida" é
  informação, e é ela que os testes afirmam.

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

Os dois endereços do agente — endpoint de execução e card de descoberta —
saem também na resposta de `GET /agents/{id}` e da listagem, montados no
servidor por um ponto único que o próprio card consome (`agente-enderecos-a2a`).
Nunca são construídos pelo consumidor a partir de host mais identificador: a
url pública é configuração do servidor, e o host de onde a página foi servida
não é o host público atrás de proxy. Sem url pública configurada, o bloco vem
**ausente** em vez de relativo — o card de descoberta ainda emite endereço
relativo nesse caso, e corrigir isso é change própria (ver "Itens em aberto"
no arquivo 02).

A re-serialização do `AgentTask` inteiro em `PostgresTaskStore.SaveTaskAsync`
(`Serialize(task, A2AJsonUtilities.DefaultOptions)`) **não é uma rede de
segurança genérica** para qualquer valor colocado em `AgentTask.Metadata`
fora do contrato A2A: ela só reaplica naming policy/`DefaultIgnoreCondition`
sobre objetos .NET serializados a fresco, não sobre um `JsonElement` já
materializado — esse é copiado verbatim. Um codec de `Metadata` que
serializa sem `A2AJsonUtilities.DefaultOptions` só tem o defeito
mascarado pela re-serialização se o valor nunca foi materializado como
`JsonElement` antes dela (ex.: uma string escalar); se já foi (ex.: um
objeto), o formato errado chega ao disco. Achado em
`push-notification-config-codec-encoder`, comparando com o defeito
benigno de `ConversationSessionCodec` corrigido em
`crossapp-session-codec-encoder`.

## Contexto do agente (blocos concatenados às `Instructions`)

`apps/workers` (`AgentExecutionService`) concatena às `Instructions`
cadastradas do agente um ou mais blocos de contexto, montados em memória a
cada execução — nunca persistidos em `Agent.Instructions`, nunca parte do
histórico de conversa. Dois blocos hoje: contexto temporal (instante de
processamento, instante da mensagem quando disponível, regra de
precedência para expressões de tempo relativas —
`apps-workers-contexto-temporal`, `inbox-instante-mensagem`) e contexto
de canal (tipo do canal, identificador do contato —
`inbox-contexto-canal`). Cada bloco é uma função pura em arquivo próprio
(`TemporalContextBlockBuilder`, `ChannelContextBlockBuilder`), com seu
próprio marcador delimitando "isto não é uma mensagem do usuário, não
responda a ele diretamente" — arquivos separados de propósito, sem
infraestrutura compartilhada entre eles antes de haver um terceiro bloco
(convenção 2).

**O que entra num bloco não é uma decisão só de utilidade — é uma decisão
de risco.** `inbox-contexto-canal` formulou a distinção, ao decidir manter
`Contact.DisplayName` fora do prompt: um valor **atribuído pelo
provedor/adapter do canal** (`Channel.ChannelType`, `Contact.ExternalId`)
não é a mesma categoria de dado que **texto livre digitado pelo usuário
final** (`Contact.DisplayName`). O primeiro pode entrar num bloco de
contexto sem abrir a classe de risco de injeção de prompt; o segundo abre
— e o marcador de "não é mensagem do usuário" usado pelos dois blocos
acima é uma dica textual, não uma fronteira estrutural: nunca foi testado
sob conteúdo adversarial, porque nunca carregou nenhum até agora. Vale
para qualquer dado futuro cogitado para entrar na janela de contexto do
agente, não só para os dois blocos existentes — a pergunta a fazer antes
de adicionar um valor novo é de onde ele vem, não só o que ele ajuda o
agente a fazer.

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
   No frontend a régua é a mesma e o gatilho é repetição **já observada**,
   nunca prevista: os três componentes visuais compartilhados do painel
   (card seccionado, rótulo de seção, cabeçalho de detalhe) saíram de cinco
   cópias idênticas, quatro estruturas iguais e três cabeçalhos repetidos —
   contados antes de extrair.
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
   degradação continua graciosa, só deixa de ser invisível. Todo
   `BackgroundService` deste monorepo captura suas próprias falhas
   recuperáveis por unidade de trabalho (mesmo nível de granularidade de
   `TaskJobConsumer`/`DebounceSweepService`) — nunca depende de
   `HostOptions.BackgroundServiceExceptionBehavior` para isso:
   `Ignore` não reinicia o serviço após a primeira exceção (fica "vivo
   mas morto", pior que o crash que evita), e é política de host, não de
   dependência específica (`inbox-sweep-service-resiliencia`). O defeito
   que essa degradação graciosa evita não é exclusivo de
   `BackgroundService`: já apareceu duas vezes com a mesma forma — um
   `try/catch` correto existe, mas a chamada que mais realisticamente
   falha (consulta ao banco, decifragem de credencial) está posicionada
   fora dele, então a exceção escapa antes de chegar à proteção
   (`DebounceSweepService`, um `BackgroundService`;
   `PushNotificationEndpoints.DeliverResponseAsync`, um handler de
   endpoint HTTP comum — `inbox-push-notification-decrypt-resiliente`).
   Ao revisar qualquer `try/catch` de degradação graciosa, checar se
   **todas** as chamadas capazes de falhar antes do resultado esperado
   estão dentro dele, não só a que motivou o `catch` originalmente — não
   é uma checagem restrita a `BackgroundService`.
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
   aplicado a quatro casos de natureza diferente: DI keyed para pontos de
   extensão tipo-plugin (contrato de canal), classificação de rotas
   anônimas (autenticação), configuração de fuso horário do sistema
   (`apps/workers` — valor resolvido vs. valor declarado em `TZ`, não só
   presença da variável), e extratores de conteúdo por `SourceType`
   (`apps/api`). A checagem vale nos **dois sentidos** quando houver lista
   esperada e realidade mapeada: item declarado sem contraparte real é tão
   problema quanto o inverso. DI keyed continua sendo o padrão para pontos
   de extensão tipo-plugin.

   **Existem duas formas do padrão, e a escolha entre elas tem
   consequência de teste.** Três das quatro checagens rodam sobre o **host
   construído** (`IHost`/`WebApplication`, depois do `Build()`) e derrubam
   o boot de verdade. A quarta — e qualquer outra que inspecione
   **descritores de DI keyed** — precisa rodar sobre a
   `IServiceCollection`, **antes** do `Build()`, porque é ali que as
   chaves registradas são enumeráveis; sobre o host seria preciso resolver
   os serviços para descobri-las. `ValidateChannelAdapterRegistrations` já
   tinha essa forma, sem que a distinção estivesse escrita.

   O custo da forma `IServiceCollection` é que testar a checagem
   diretamente verifica **a extensão**, não o caminho de boot: mover ou
   remover a chamada do `Program.cs` deixa esses testes verdes e a
   aplicação sobe com registro divergente. Quem usar essa forma paga um
   teste a mais, que captura a `IServiceCollection` **real** pelo
   `ConfigureServices` da `WebApplicationFactory` (que roda depois de
   todos os registros do `Program.cs`) e afirma a coerência ali, sobre a
   composição de produção. É barato — sem container, sub-segundo — e é o
   único que pega o caso "chamada removida **e** registro divergente":
   verificado reintroduzindo exatamente esse defeito, com os testes da
   extensão ficando verdes e só esse reprovando.
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
    própria sequenciada antes — não se contorna no consumidor. Vale
    também para as opções de serialização em si (encoder de escaping,
    naming policy, tratamento de null), não só a representação de um
    campo específico: dois sites que serializam o mesmo tipo com opções
    diferentes produzem payloads estruturalmente diferentes mesmo com os
    campos certos. Já aconteceu com um codec de `apps/workers`
    serializando payload A2A sem as opções (`A2AJsonUtilities.DefaultOptions`)
    que o resto do pipeline usa, divergindo em encoding de aspas de um
    jeito que só um teste de acordo real entre `apps/api` e
    `apps/workers` pegou (`crossapp-session-codec-encoder`).
    A cláusula de naming policy voltou a morder em `agente-enderecos-a2a`,
    de forma mais sutil: a política camelCase minúscula apenas a primeira
    letra, então uma propriedade `A2A` vai para o fio como `a2A` e o
    consumidor que lê `a2a` recebe campo ausente. **Teste que desserializa a
    resposta para o mesmo tipo é cego a isso** — a chave passa pela mesma
    política na ida e na volta e sempre casa. O teste que pega inspeciona o
    texto do JSON. Sempre que um nome de propriedade tiver sigla, número ou
    maiúsculas consecutivas, fixar o nome no fio explicitamente.
13. **A UI nunca afirma mais do que o sistema sabe** — se o dado não é
    coletado, a interface não o insinua. Um único indicador de envio,
    jamais dois checks, porque entrega e leitura são recibos que o desenho
    escolhido não coleta; valor de enum desconhecido renderiza indicador
    neutro, nunca reaproveita o indicador de sucesso. A asserção que
    protege isso é a **negativa** (afirmar a ausência do segundo
    indicador), porque é ela que impede a regressão bem-intencionada de
    "deixar parecido com o WhatsApp".

14. **Mudança visual só é verificada por olho humano** — a suíte roda em
    jsdom, que não enxerga cor, contraste nem layout. Uma mudança de tema
    ou de composição pode deixar a suíte inteira verde e o painel
    ilegível, e isso aconteceu: três etapas do redesenho passaram verde
    com defeitos que só a comparação com o protótipo pegou. Change que
    mexe em aparência traz conferência manual como tarefa própria, tela a
    tela, nos dois esquemas de cor — e a conferência é **iterativa**,
    porque cada correção muda o que fica visível (migrar o card revelou o
    divisor recuado, corrigir o divisor revelou a faixa desalinhada). O
    que a suíte pode cobrir é contrato: que o token vale o que a spec diz,
    que o componente recebe o que promete, que o link aponta para a rota
    certa.
15. **Um guarda só vale depois de ter falhado contra o defeito real** —
    escrever o teste, reintroduzir o defeito de propósito, ver reprovar, e
    só então manter a correção. **Quatro** guardas desta base passaram verde
    **com o defeito presente** antes de serem consertados: o que varre
    tons fixos de superfície (a expressão não cobria valor dentro de
    ternário, que era justamente a forma do caso real), o que prende a
    altura da faixa de cabeçalho, o que afirma o nome do campo no fio, e o de
    "Distinção de tools com nomes iguais entre servidores diferentes"
    (`mcp-tool-execution`), que cobria só servidores de nomes **diferentes**
    enquanto o requisito já estava violado por dois `McpServer.Name` que
    sanitizam para a mesma cadeia (`dedupe-global-nome-de-tool`).
    Guarda não verificado é pior que nenhum, porque dá impressão de
    cobertura sem ter.

    **E o erro tem uma segunda forma, que só aparece na implementação: guarda
    no lugar errado.** Um teste pode reprovar antes e depois da correção — e
    passar a impressão de estar funcionando na primeira metade — quando ele
    afirma a garantia no componente errado. Em `dedupe-global-nome-de-tool`
    três guardas reprovaram contra o defeito **e continuaram reprovando depois
    da correção**, porque afirmavam unicidade dentro de cada resolvedor,
    enquanto a correção é global no ponto que une os dois conjuntos. Ao escrever
    o guarda, checar não só que ele reprova, mas que ele reprova **no
    componente que a correção vai tocar**.
16. **Papel visual que troca de ponta da escala precisa de variável
    declarada por esquema** — `gray[n]` é claro nos dois esquemas e
    `dark[n]` é escuro nos dois, então um tom fixo usado como fundo de
    superfície funciona num tema e quebra no outro. O mesmo defeito
    apareceu três vezes no redesenho: fundo da página, faixa de cabeçalho
    de card e de tabela, e linha selecionada no histórico de sessões. A
    causa é conceitual — no tema claro a superfície sutil é *mais clara*
    que o card, no escuro é *mais escura* —, e a saída é uma variável
    declarada nos dois esquemas pelo resolver, ou um token da biblioteca
    que já troque sozinho. Fechado por guarda estático (ver 15).
17. **Contrariar o protótipo é resultado legítimo, e vira registro** —
    handoff de design feito sem acesso ao código diverge da realidade, e a
    divergência se resolve com decisão explícita, não com implementação
    silenciosa nem com fidelidade cega. No redesenho isso aconteceu cinco
    vezes: campo que a API não devolve, progresso que ninguém mede,
    contagem que exigiria segunda requisição, identidade de operador que o
    login não fornece, e um tom de rótulo que reprova no contraste mínimo
    que a própria identidade visual exige. Regra que o sistema já
    escreveu vence protótipo; a recusa vai para o `design.md` com o
    número que a sustenta.

18. **Estimar tamanho de change por diffstat de commit anterior engana de
    duas formas conhecidas**, e as duas foram medidas. Primeira: o headline de
    um commit inclui os artefatos OpenSpec — `b5df504` tem 30 arquivos / 1593
    linhas, mas 8 arquivos / 787 linhas são `openspec/`, então o código real
    foram 21 / 592. Comparar trabalho de código contra esse número subestima
    por construção. Segunda: cobertura de teste varia por uma ordem de
    grandeza entre changes (`b5df504` tem 1 arquivo de teste;
    `knowledge-base-catalogo-documentos` tem 11, com 42% de todo o trabalho
    manual), e diffstat não distingue.

    **E projetar só depois que a verificação fecha.** Uma projeção por componente
    feita antes do fim da verificação erra para baixo por construção, porque os
    componentes que a verificação ainda vai descobrir não estão nela para serem
    contados. Medido em `dedupe-global-nome-de-tool`: a projeção subiu ~15% entre
    a primeira redação do `design.md` e a revisão, sem nenhuma mudança de escopo
    — três itens (uma subclasse de wrapper do SDK, um cenário de comparação de
    caixa, um cenário de histórico persistido) só existiram depois de decompilar
    o SDK e consultar o banco. O número **não** é um fator a somar em projeções
    futuras; a lição é o momento de contar. Projeção feita durante a verificação
    é rascunho, não estimativa.

    E a régua que mais corrige a intuição: **contagem de arquivo é dirigida
    pelo número de operações CQRS, não por complexidade.** Numa change medida,
    24 arquivos de comando/handler/result somaram 450 linhas — média de 19
    linhas por arquivo. Uma etapa com muitas operações simples produz muitos
    arquivos minúsculos; uma com poucas operações e lógica pesada produz
    poucos arquivos longos. Projetar as duas com o mesmo fator é o erro.
    Estimar por componente (custo por operação CQRS, por cenário de teste, por
    grupo de endpoints), só sobre código, e citar a âncora **decomposta**, não
    o headline dela.

19. **"Pré-existente" e "ambiental" são conclusões que exigem a baseline, e a
    baseline não fecha sozinha.** Três vezes nesta base uma falha de teste foi
    classificada como pré-existente ou ambiental e a classificação estava errada,
    cada vez por um motivo diferente:

    - `TimeoutException` do `InboxOrchestratorRoundTrip` lido como "latência do
      `podman`", porque o tempo (~21s) parecia próximo do limite. O número não era
      evidência de nada: era o próprio timeout de 20s do teste cortando a espera.
    - `ObjectDisposedException` de `apps/inbox` lida como corrida de disposal do
      `WebApplicationFactory` entre classes. O mecanismo real era outro
      (`InboxFactoryFixture` construindo o host antes de migrar).
    - Em `dedupe-global-nome-de-tool`, `TaskJobConsumerTests` reprovando em bloco
      lido como "limite pré-existente de contenção de containers". **Desmentido
      por baseline**: `git worktree` limpo em `6956d79` passa 132/132 em paralelo.
      A 13ª classe de host era da própria change.

    Daí a regra: **antes de classificar uma falha como pré-existente ou ambiental,
    rodar a suíte contra a baseline num `git worktree` limpo** — é barato, não
    mexe na árvore de trabalho, e é a única coisa que separa "já estava assim" de
    "eu quebrei".

    **E a metade que a baseline não resolve:** uma baseline que *também* falha
    remove a hipótese de regressão, e **só** ela. Não promove o sintoma a
    "ambiental" nem dispensa achar a causa. Foi exatamente esse o erro da segunda
    leitura do `TimeoutException` (`inbox-instante-mensagem`): o bisect em
    worktree comparou base e HEAD, viu falha idêntica, e registrou "causa
    ambiental confirmada por evidência direta" — e a causa real só apareceu na
    terceira leitura. Baseline verde acusa regressão; baseline vermelha não
    absolve ninguém.
