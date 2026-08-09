## Context

`apps/inbox` hoje (change arquivada `inbox-catalogo-canais`) tem `Channel`
como única entidade: EF Core + Postgres próprio (`buteco_inbox`),
Mediator/CQRS, credenciais cifradas com AES-GCM, `AgentId` validado via HTTP
contra `apps/api` (bancos separados, sem FK possível). Não existe ainda
nenhum conceito de "quem está falando" nem de fronteira entre conversas —
isso é o que esta change resolve, antes de qualquer adapter real de canal ou
do orquestrador (que vai bufferizar/debounçar mensagens, change futura)
existir.

Investigação feita antes desta proposta (via `/opsx:explore`), lendo código
real:

- `Channel.AgentId` é `Guid` opaco porque `apps/api` está em outro banco —
  única forma de referência é HTTP (`IAgentReferenceValidator`). `Channel`
  em si, porém, já vive no mesmo `AppDbContext`/banco que `Contact`/`Session`
  vão viver: é a primeira vez que uma entidade nova de `apps/inbox` pode
  referenciar outra do mesmo app com FK real, não Guid opaco + validação de
  rede.
- Padrão CQRS já estabelecido (`Commands/<Op>/`, `Queries/<Op>/`,
  `Requests/`, `Responses/`, `Endpoints/<Feature>Endpoints.cs` com
  `TypedResults`/`Results<...>`), padrão de Options (`ApiOptions`,
  `InboxCryptoOptions`: classe `sealed`, `const string SectionName`,
  registrada via `builder.Services.Configure<T>(...)` em `Program.cs`) — ver
  `Channels/Commands/CreateChannel/*`, `Channels/Queries/ListChannels/*`,
  `Channels/Endpoints/ChannelEndpoints.cs`, `Options/ApiOptions.cs`.
- Constantes de domínio tipo limite/profundidade vivem como `private const`
  dentro da própria classe de serviço quando não precisam ser configuráveis
  (`AgentExecutionService.MaxHistoryMessages`/`DelegationDepthLimit`,
  `apps/workers`) — não é o caso do timeout de inatividade desta change, que
  precisa ser parametrizável (ver Decisão 3), então vai em `IOptions`, não em
  `const`.
- Não existe nenhuma abstração de relógio (`IClock`/`TimeProvider`) em lugar
  nenhum do projeto — toda entidade usa `DateTimeOffset.UtcNow` direto
  (`Channel.cs`). Testes que precisam de tempo controlado inserem timestamps
  diretamente via EF/SQL (`ExecuteSqlInterpolatedAsync`), não injetam
  relógio fake (`ConversationHistoryTests.cs`, `apps/workers`).
- `A2ATaskRecord.ContextId` (`apps/api/src/Buteco.Api/A2A/A2ATaskRecord.cs`)
  é `string`, não `Guid` — relevante porque `Session.ContextId` desta change
  precisa nascer já com o tipo certo, mesmo sem nenhuma chamada real ao
  protocolo A2A ainda.
- `AesGcmChannelCredentialCipher` (`Channels/Security/`) usa nonce aleatório
  por chamada — correto para credenciais write-only nunca comparadas por
  igualdade, mas incompatível com uma coluna que precisa ser buscável e
  única (`ExternalId`, ver Decisão 5).
- Nenhum `HasIndex(...).IsUnique()` existe em lugar nenhum do projeto hoje
  (`apps/api`, `apps/workers`, `apps/inbox`) — será o primeiro.
- `InboxFactoryFixture` (`apps/inbox/tests/Buteco.Inbox.Tests/Support/`) é o
  fixture real (WebApplicationFactory + Testcontainers Postgres) a
  reutilizar para os novos testes, sem nenhuma mudança nele mesmo — só novas
  classes de teste que o consomem.

## Goals / Non-Goals

**Goals:**
- `Contact` identifica de forma estável quem está falando, por canal.
- `Session` amarra várias conversas do mesmo `Contact` ao longo do tempo,
  com fronteira automática por inatividade.
- `IContactSessionResolver.FindOrCreateSessionAsync` como serviço interno
  reutilizável pelo futuro orquestrador (mesmo processo), testável
  diretamente por teste de integração.
- Toda a superfície nova coberta por teste automatizado (resolver, cifra/
  ausência dela, endpoints, migration).

**Non-Goals:**
- Nenhum adapter real de canal, nenhuma mensagem de verdade chegando.
- Nenhum orquestrador, nenhum debounce/buffer de mensagens — change futura.
- Nenhum encerramento explícito de sessão (agente ou humano marcando como
  resolvida) — decisão já confirmada em conversa anterior; fechamento por
  inatividade automática é o único mecanismo desta fatia.
- Nenhum campo de nome de exibição em `Contact` (Decisão 6).
- Nenhum endpoint de escrita para `Contact`/`Session` (Decisão 4).
- Nenhuma UI.
- Nenhuma mudança em `apps/api`, `apps/workers` ou `apps/frontend` —
  diferente da change anterior (`Channel.AgentId`), `Contact`/`Session` não
  fazem nenhuma chamada nova a `apps/api`.
- Nenhum conteúdo de mensagem armazenado em `Session` — só o ponteiro
  (`ContextId`); o conteúdo real da conversa continua vivendo só em
  `a2a_tasks`, em `apps/api`.

## Decisions

### Árvore de pastas proposta

```
apps/inbox/
├── src/
│   └── Buteco.Inbox/
│       ├── Contacts/
│       │   ├── Entities/
│       │   │   ├── Contact.cs
│       │   │   └── Session.cs
│       │   ├── ContactSessionResolver.cs
│       │   ├── IContactSessionResolver.cs
│       │   ├── SessionOptions.cs
│       │   ├── Queries/
│       │   │   ├── ListContacts/
│       │   │   │   ├── ListContactsQuery.cs
│       │   │   │   └── ListContactsQueryHandler.cs
│       │   │   └── GetContactSessions/
│       │   │       ├── GetContactSessionsQuery.cs
│       │   │       └── GetContactSessionsQueryHandler.cs
│       │   ├── Responses/
│       │   │   ├── ContactResponse.cs
│       │   │   └── SessionResponse.cs
│       │   └── Endpoints/
│       │       └── ContactEndpoints.cs
│       ├── Channels/                      (existente, inalterado)
│       ├── Infrastructure/
│       │   ├── AppDbContext.cs            (ganha DbSet<Contact>/DbSet<Session>)
│       │   └── Migrations/
│       │       └── <timestamp>_AddContactSessionCrm.cs (+ .Designer.cs, ModelSnapshot)
│       └── Program.cs                     (registra SessionOptions, IContactSessionResolver, MapContactEndpoints)
└── tests/
    └── Buteco.Inbox.Tests/
        ├── ContactSessionResolverTests.cs
        └── ContactEndpointsTests.cs
```

`IContactSessionResolver`/`ContactSessionResolver` ficam direto em
`Contacts/`, não em `Contacts/Services/` — mesmo nível de `Channels/Security/`
para a cifra: um único serviço por pasta de feature, sem subpasta extra para
um arquivo só. `Queries/` só tem leitura (Decisão 4); não existe `Commands/`
nesta fatia porque não há nenhum comando de escrita explícito — criar
`Contact`/`Session` é sempre efeito colateral de `FindOrCreateSessionAsync`,
que não é um `ICommandHandler` do Mediator (é chamado direto, ver Decisão 3),
não um endpoint.

### 1. `Contact` identificado por `(ChannelId, ExternalId)`, sem identidade unificada entre canais

O mesmo contato externo falando em canais diferentes (ex. mesmo número de
WhatsApp conversando com dois números seus, ou o mesmo usuário no WhatsApp e
no Telegram) vira dois registros de `Contact` distintos — sem nenhuma
tentativa de correlacionar identidade entre plataformas.

`Contact.ChannelId` é **FK real** para `Channel.Id` (`OnDelete(Restrict)`) —
diferente de `Channel.AgentId`, que é `Guid` opaco validado via HTTP porque
aponta para outro banco. `Channel` já vive no mesmo `AppDbContext` que
`Contact`, então não há razão para reproduzir o padrão de validação de
referência cruzada aqui; seria complexidade sem necessidade. `Channel` nunca
é deletado fisicamente (só `IsActive`, decisão da change anterior), então
`Restrict` nunca dispara na prática — é só a política mais segura por
padrão, não uma decisão com peso real.

**Alternativa considerada**: identidade unificada entre canais (um `Contact`
por pessoa real, não por par canal+identificador externo). Rejeitada — exigiria
alguma forma de correlacionar identidades de plataformas diferentes (ex.
número de telefone declarado em ambos os canais, confirmação manual), sem
nenhum sinal hoje para fazer isso com confiança; complexidade real sem
necessidade concreta.

### 2. `Session.ContextId` gerado localmente, tipado como `string`

`Session.ContextId` é gerado por `apps/inbox` (`Guid.NewGuid().ToString()`)
no momento da criação da sessão — não coordenado com `apps/api`, porque
nenhuma chamada `SendMessage` de fato acontece ainda (isso é do adapter,
changes futuras). O `contextId` "nasce" de verdade no protocolo A2A só
quando o adapter existir e chamar `apps/api`; aqui é só reservado com
antecedência, para a `Session` já ter um identificador de conversa estável
desde a primeira mensagem.

Tipado como `string`, não `Guid` nativo — `A2ATaskRecord.ContextId`
(`apps/api`) é `string`. Gerar como `Guid` agora criaria um mismatch de tipo
esperando para acontecer no dia em que o adapter futuro precisar passar esse
valor pro protocolo A2A. Barato de acertar agora, custoso de descobrir depois
(migração de coluna com dado em produção).

### 3. Fronteira de sessão por inatividade é lógica de domínio desta change, não do orquestrador

Dado quanto tempo se passou desde `LastActivityAt`, decidir se uma mensagem
nova reaproveita a sessão mais recente do `Contact` ou abre uma nova é lógica
de domínio sobre `Contact`/`Session` — não é lógica de conversar com
WhatsApp/Telegram (isso é do adapter) nem de bufferizar mensagens (isso é do
orquestrador, change futura). Exposta como serviço interno:

```csharp
public interface IContactSessionResolver
{
    Task<Session> FindOrCreateSessionAsync(Guid channelId, string externalId, CancellationToken cancellationToken);
}
```

Chamável direto por testes de integração contra `AppDbContext` real — sem
endpoint HTTP de escrita, já que não existe hoje nenhum chamador legítimo de
fora do processo (só o futuro orquestrador, que roda no mesmo app; ver
Decisão 4 sobre por que não expor `POST /sessions`).

Timeout de inatividade **parametrizado via `IOptions`**
(`SessionOptions.InactivityTimeout`, `TimeSpan`), mesmo molde de
`ApiOptions`/`InboxCryptoOptions` — `SectionName = "Session"`, registrado em
`Program.cs` via `builder.Services.Configure<SessionOptions>(...)`. Default
`01:00:00` (1 hora) em `appsettings.json`, sobrescrevível por ambiente
(`Session__InactivityTimeout`). Ainda global à aplicação — não configurável
por canal nesta fatia; se um canal precisar de timeout diferente, isso é
extensão futura, não antecipada aqui.

Algoritmo:
1. `Contact` é encontrado por `(channelId, externalId)`; se não existir, é
   criado (ver Decisão 7 sobre a corrida nesse passo).
2. A `Session` mais recente do `Contact` (por `StartedAt` desc) é buscada.
3. Se não existir nenhuma, ou se `UtcNow - LastActivityAt > InactivityTimeout`,
   uma nova `Session` é criada (`ContextId` novo, `StartedAt`/`LastActivityAt`
   = `UtcNow`).
4. Senão, a `Session` existente é reaproveitada: `LastActivityAt` é
   atualizado para `UtcNow`.

Nenhuma abstração de relógio (`IClock`/`TimeProvider`) é introduzida —
`DateTimeOffset.UtcNow` direto, mesmo padrão de `Channel.cs`. Testes que
precisam simular o timeout inserem a `Session` já com `LastActivityAt` no
passado via SQL direto (`ExecuteSqlInterpolatedAsync`), mesmo mecanismo já
usado em `ConversationHistoryTests.cs` (`apps/workers`) para simular estado
temporal sem tocar o relógio da aplicação.

**Alternativa considerada**: fronteira de sessão decidida pelo orquestrador,
`IContactSessionResolver` só expondo `FindOrCreateContact`. Rejeitada —
misturaria lógica de domínio (quando uma conversa "termina") com lógica de
transporte/buffer (como as mensagens chegam), contrariando a separação já
estabelecida pela decisão anterior de que debounce é do orquestrador e a
fronteira de sessão é desta change.

**Alternativa considerada**: timeout como `private const`, mesmo estilo de
`MaxHistoryMessages`/`DelegationDepthLimit`. Rejeitada — diferente daqueles
dois (nunca precisaram variar por ambiente), o timeout de sessão é um
parâmetro de produto plausível de ajustar sem recompilar (ex. staging com
timeout curto para testar a fronteira mais rápido); o custo de usar
`IOptions` em vez de `const` é mínimo e já é o padrão existente do projeto
para configuração.

### 4. Superfície HTTP desta fatia: só leitura

`GET /contacts` e `GET /contacts/{id}/sessions`, para inspeção/auditoria e
para dar algo testável via `WebApplicationFactory` além do serviço interno.

`GET /contacts/{id}/sessions` retorna `404` se o `Contact` não existe,
`200 OK` com lista vazia se existe mas ainda não tem nenhuma `Session` —
mesma distinção que `GetChannelByIdAsync` já faz para `Channel` inexistente
vs. existente.

Nenhum endpoint de escrita — criar `Contact`/`Session` é sempre efeito
colateral de uma mensagem chegando (via `FindOrCreateSessionAsync`, Decisão
3), nunca uma chamada de API arbitrária; expor `POST /sessions` modelaria
errado a operação, sugerindo que uma sessão pode ser criada "do nada", sem
uma mensagem real por trás.

### 5. `Contact.ExternalId` em texto plano, risco documentado conscientemente

`ExternalId` (número de telefone, `chat_id`) é dado potencialmente pessoal/
identificável, mas **não** recebe o mesmo tratamento de criptografia em
repouso já usado para `Channel.EncryptedCredentials`
(`AesGcmChannelCredentialCipher`).

Motivo técnico, não só de custo: `AesGcmChannelCredentialCipher` usa um nonce
aleatório de 96 bits a cada chamada — o mesmo texto plano produz ciphertext
diferente toda vez. Correto para credenciais write-only, nunca comparadas por
igualdade; incompatível com `ExternalId`, que precisa ser buscável e único
(`WHERE ChannelId = ... AND ExternalId = ...` em toda chamada de
`FindOrCreateSessionAsync`, mais o unique constraint da Decisão 7) — não é
possível indexar ou comparar por igualdade contra ciphertext não-
determinístico, e o Postgres não consegue enforçar unicidade sobre um valor
que só existe decifrado.

Decisão: `ExternalId` fica em texto plano, risco registrado aqui
explicitamente — não decidido por omissão.

**Alternativa considerada**: cifra determinística (ex. AES-SIV) ou HMAC-SHA256
como índice cego, guardado em coluna separada do valor cifrado exibível.
Rejeitada nesta fatia — resolveria o problema de busca/unicidade, mas
introduz um primitivo criptográfico novo no projeto (nenhum precedente de
cifra determinística ou índice cego hoje) e uma segunda coluna só para
suportar a primeira, complexidade real sem necessidade concreta ainda
comprovada; revisitar se o dado em texto plano se mostrar um problema
concreto (auditoria de segurança, requisito de compliance).

**Alternativa considerada**: armazenar só um hash (sem plaintext em lugar
nenhum). Rejeitada — inviabiliza a Decisão 4: `GET /contacts` existe
explicitamente para inspeção/auditoria, que precisa mostrar o identificador
real, não um hash opaco.

### 6. `Contact` sem campo de nome de exibição

Nenhum adapter de canal existe ainda para preencher um nome de perfil
(WhatsApp/Telegram) — adicionar o campo agora seria especular sobre um dado
que nenhum código do projeto pode fornecer de verdade ainda. Registrado como
Non-Goal explícito.

### 7. Concorrência em `FindOrCreateSessionAsync`: unique constraint + retry-as-find

`Contact(ChannelId, ExternalId)` recebe unique constraint no banco — primeiro
índice único do projeto. Necessário por corretude mesmo sem concorrência
(é o que torna "find or create" coerente), e é a defesa real contra duas
chamadas quase simultâneas para o mesmo `(ChannelId, ExternalId)` criarem
dois `Contact` quando deveriam reaproveitar um.

No handler, a tentativa de criar um `Contact` novo captura a exceção de
violação de unique constraint (`DbUpdateException` envolvendo a exceção de
índice único do Npgsql) e **re-consulta** — trata a corrida como um "find"
que só demorou a acontecer, não como erro. Protege o caminho real mesmo sem
nenhum chamador concorrente hoje (nenhum adapter, nenhum orquestrador),
porque o custo de implementar é baixo e evita uma falha 500 supérflua no dia
em que um chamador concorrente de fato existir.

O retry por si só não é suficiente: o EF Core não reverte automaticamente o
change tracker depois de um `SaveChangesAsync` malsucedido — a entidade
`Contact` que falhou continua rastreada no estado `Added`. Como
`ContactSessionResolver` reusa o mesmo `AppDbContext` (`Scoped`) para o resto
do método (a criação/atualização da `Session`, passos 3/4 do algoritmo
acima), um `SaveChangesAsync` subsequente no mesmo `DbContext` tentaria
reinserir esse `Contact` ainda rastreado como `Added` — uma segunda violação
de constraint, dessa vez sem captura, propagando como erro não tratado.
Por isso o handler precisa destacar explicitamente a entidade malsucedida do
change tracker (`dbContext.Entry(failedContact).State = EntityState.Detached`)
logo após capturar a exceção, antes de prosseguir para a re-consulta e para
a resolução da `Session`.

`Session` duplicada na mesma corrida (duas `Session` novas para o mesmo
`Contact` recém-criado) não recebe a mesma proteção — sem unique constraint,
sem retry. Aceito como risco: é uma falha menos grave (duas sessões em vez de
uma, corrigível reaproveitando a mais recente na próxima chamada) e, sem
nenhum chamador concorrente real hoje, a janela de corrida é teórica.
Revisitar quando o orquestrador de verdade existir e a concorrência deixar de
ser hipotética.

**Alternativa considerada**: nenhuma proteção agora, unique constraint
incluída (por corretude), mas sem captura/retry no handler — deixar
`DbUpdateException` borbulhar como 500. Rejeitada — o retry é barato
(algumas linhas), e evita que o primeiro chamador concorrente real (o
orquestrador futuro) encontre uma falha em produção por um caminho que já era
previsível nesta fatia.

## Risks / Trade-offs

- **[Risco] `Contact.ExternalId` em texto plano** (Decisão 5, aceito
  conscientemente) → Mitigação: nenhuma nesta fatia; reavaliar cifra
  determinística/índice cego se surgir necessidade concreta (auditoria de
  segurança, requisito de compliance).
- **[Risco] `Session` duplicada sob corrida real** (Decisão 7, aceito) →
  Mitigação: nenhuma nesta fatia; corrigível quando o orquestrador real
  existir e a corrida deixar de ser hipotética.
- **[Trade-off] Timeout de inatividade global, não por canal** → Aceito;
  sem necessidade concreta hoje de comportamento diferente por canal
  (WhatsApp vs. Telegram); extensível depois sem quebra (adicionar
  `Channel.SessionInactivityTimeoutOverride` nullable, se necessário).

## Migration Plan

Sem dado em produção para migrar — `Contact`/`Session` não existem hoje.
Passos de rollout:
1. Aplicar a migration aditiva (`Contact`, `Session`, unique constraint,
   FK para `Channel`) manualmente em cada ambiente, mesmo mecanismo já usado
   por `Channel` (`dotnet ef database update`, nunca automático em runtime).
2. Configurar `Session:InactivityTimeout` em cada ambiente antes do primeiro
   deploy que dependa dele — tem default (`01:00:00`) em
   `appsettings.json`, então a ausência de configuração explícita não quebra
   o processo (diferente de `Inbox:CredentialEncryptionKey`, que é
   obrigatório).

Rollback: reverter o commit; sem dado em produção, sem estado persistido
incompatível entre versões.

## Open Questions

(nenhuma — as três incertezas reais desta fatia, cifra de `ExternalId`,
timeout configurável vs. constante, e proteção de concorrência, foram
resolvidas nas Decisões 5, 3 e 7 respectivamente)
