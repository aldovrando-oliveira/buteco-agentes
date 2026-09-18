## Context

"Mensagens processadas" era **um** card no `design.md` de
`frontend-inventario-catalogos` (arquivada em 2026-09-17), que listou três
candidatos a quadro de atividade e deixou os três fora, cada um com o que exigia.
Aquele card se dividiu em três métricas distintas, e **esta change atende a
primeira**: mensagens recebidas.

O antecessor direto é `inbox-sessoes-por-periodo` (arquivada em 2026-09-17). A
**forma** vem inteira de lá — limites, validação, binding, agregação — e este
documento **não a redecide**. O que ele decide é só o que muda com o domínio.

Estado atual do `apps/inbox`, conferido no código:

| fato | onde |
|---|---|
| Nenhuma rota conta mensagens por intervalo | `MessageEndpoints.cs:12` é a única leitura de mensagens: lista completa de **uma** sessão, sem parâmetro de data |
| `/messages` de nível superior **não existe** | `Program.cs:132-138` — as sete registradoras, e nenhuma abre `/messages` |
| `Message.OccurredAt` é **write-once** | atribuído em `Message.cs:56` e `:77`, em nenhum outro lugar; único mutador público é `UpdateDispatchStatus()` |
| `OccurredAt` é o instante de **recebimento pelo servidor** | `TelegramInboundWebhookHandler.cs:105` e `WahaInboundWebhookHandler.cs:64` passam `DateTimeOffset.UtcNow`; Outbound em `PushNotificationEndpoints.cs:130` |
| Nenhum `ExecuteUpdate`/SQL cru toca `messages` em produção | `grep -rn "ExecuteUpdate\|ExecuteSql\|FromSql" apps/inbox/src` → zero |
| `messages` **tem** índice com `OccurredAt`, mas composto | `AppDbContext.cs:164` — `IX_messages_SessionId_OccurredAt`, `SessionId` como coluna líder |
| Dedup de webhook reentregue é real | `AppDbContext.cs:168-170` (único parcial) + `InboundMessageOrchestrator.cs:47,67` |
| Outbound é persistida também quando **falha** | `PushNotificationEndpoints.cs:163` (`Sent`) e `:182` (`Failed`) |

**A diferença estrutural com a change anterior, e é ela que encurta este
documento:** sessão tinha **três** instantes concorrentes (`StartedAt`,
`LastActivityAt`, `ClosedAt`) e precisou de uma tabela para eliminar dois.
`Message` tem **um** campo temporal, write-once. Não há definição concorrente a
descartar — então não há D3 análogo aqui, e inventar um seria cerimônia.

## Goals / Non-Goals

**Goals:**

- Uma rota de **contagem** — não de listagem — que responda "quantas mensagens de
  entrada no intervalo `[from, to]`", com custo independente de N.
- Um nome de campo que carregue a semântica sem ambiguidade de ponto de vista.
- **Afirmar na spec** que a unidade contada é a mensagem distinta, não a entrega
  de webhook — é comportamento observável, não detalhe de implementação.
- O que fica de fora fica **com gatilho declarado**, não omitido em silêncio —
  incluindo um gatilho que nenhuma decisão anterior previa (D5).

**Non-Goals:**

- Nenhuma UI. A tela que consome este número é change futura (convenção 1).
- Nenhum arquivo em `apps/api`, `apps/workers` ou `apps/frontend`.
- **Nenhum `outboundCount`** (D6) — adiado com motivo, aditivo quando voltar.
- Nenhuma migração, nenhuma coluna, nenhum índice novo (D10).
- Nenhum teto de intervalo (D9).
- Nenhum `TimeProvider` novo no `apps/inbox`, e nenhum período nomeado
  ("últimos 7 dias") resolvido no servidor — herdado de D5 da change anterior,
  sem redecidir.
- Nenhuma mudança em como `OccurredAt` é gravado. D5 **registra** o que ele é
  hoje e o gatilho que o ameaça; não o altera.

## Decisions

### D1 — Recurso de contagem, não listagem com contagem no cliente

O consumo final é um inteiro num card. A consulta é `COUNT` no banco —
**nunca** `Select().ToList().Count()`, que sai com o resultado **certo** e o
custo errado, e passa despercebido exatamente por isso.

O molde é o mesmo de `GetSessionPeriodSummaryQueryHandler` e, antes dele,
`GetKnowledgeBaseIndexingSummaryQueryHandler` (`apps/api`).

**Alternativa considerada e descartada: somar por sessão no cliente.** Pior aqui
do que era para sessões: exigiria uma chamada de `GET /sessions/{id}/messages`
por sessão, cada uma trazendo o **conteúdo** de todas as mensagens, para o
cliente filtrar por data e contar. Payload de ordem de grandeza maior que o da
alternativa já recusada na change anterior.

### D2 — `GET /messages/summary`, e aqui **sim** se inaugura namespace

**Esta é a decisão que mais diverge da anterior, e a conclusão é invertida.**

A D2 de `inbox-sessoes-por-periodo` gastou um parágrafo mostrando que a premissa
"esta é a primeira rota de nível superior" **não se sustentava**: o prefixo
`/sessions` já existia (`MessageEndpoints.cs:12`), e a rota nova não inaugurava
namespace — só precisava não colidir com a irmã `:guid`.

Aqui é o contrário, e conferido:

```
Registradoras em Program.cs:132-138 e os prefixos que abrem:

  MapChannelEndpoints          /channels
  MapContactEndpoints          /contacts
  MapChannelSessionEndpoints   /channels/{id}/sessions
  MapSessionSummaryEndpoints   /sessions/summary
  MapMessageEndpoints          /sessions/{sessionId:guid}/messages   ← aninhado sob /sessions
  MapPushNotificationEndpoints /push-notifications…
  MapWebhookEndpoints          /webhooks…

  /messages  →  NENHUMA
```

`MessageEndpoints` hoje só registra `/sessions/{sessionId:guid}/messages`:
mensagens sempre apareceram **aninhadas sob a sessão**. `GET /messages/summary` é
a **primeira rota de nível superior sob `/messages`** do `apps/inbox`.

**E isso não muda nada na classificação de autenticação.** `Program.cs:139-146`
classifica toda rota como autenticada **por padrão** e falha o startup para rota
não classificada; a lista passada a `ValidateRouteAuthenticationClassification` é
só das **anônimas**. Um prefixo novo não é um caso novo para esse mecanismo — a
rota nova não toca a lista, e se alguém a esquecer o startup reprova sozinho.

**A frase existe porque o silêncio seria pior que a redundância.** Quem ler esta
rota depois, vindo da anterior, vai encontrar lá um parágrafo dizendo "o prefixo
já existe, não inaugura namespace" e pode transportar a conclusão para cá sem
reconferir. Dizer explicitamente que aqui **inaugura**, e que mesmo assim não há
exceção de autenticação a declarar, fecha essa porta.

**Ordem de registro não precisa ser garantida, e o parágrafo da anterior não se
copia**: lá havia rota irmã (`/sessions/{sessionId:guid}/messages`) sob o mesmo
prefixo, e a defesa era sobre literal × parâmetro. Aqui **não há rota irmã sob
`/messages`** — não há nada contra o que defender a ordem.

A forma do sufixo copia o precedente agregado:

```
/knowledge-bases/indexing-summary     KnowledgeBaseEndpoints.cs:33   (apps/api)
/sessions/summary                     inbox-sessoes-por-periodo
/messages/summary                     esta change
```

### D3 — `OccurredAt` dentro de `[from, to]`, inclusivo nos dois limites

O critério é `OccurredAt >= from AND OccurredAt <= to`, com `>=` e `<=`, nunca
`>` ou `<`. Uma mensagem com `OccurredAt` exatamente igual a `from`, ou
exatamente igual a `to`, conta.

**Não há tabela de três definições aqui, e a ausência é decisão.** `Message` tem
um campo temporal só, e ele é write-once — conferido, não suposto:

```
Message.OccurredAt { get; private set; }
  ├── CreateInbound  (Message.cs:56)
  └── CreateOutbound (Message.cs:77)
                                     ← e mais nenhum site de atribuição

Único mutador público da entidade: UpdateDispatchStatus() → DispatchStatus, só.
Nenhum ExecuteUpdate / ExecuteSql / FromSql em apps/inbox/src.
```

Não é o defeito de `LastActivityAt`, que a change anterior teve de descartar
porque **reescreve o passado** (o mesmo intervalo fechado responderia números
diferentes conforme o dia da pergunta). Aqui o instante gravado nunca muda, então
o mesmo intervalo fechado responde sempre o mesmo número — **com a ressalva de
D5**, que é sobre o instante escolhido, não sobre reescrita.

### D4 — `inboundCount`, não `receivedCount` e não `count`

É a convenção 13 aplicada ao contrato da API, e o argumento **não é o mesmo** de
`startedCount`. Lá o problema era vagueza (`count` não afirma nada). Aqui o
problema é **ambiguidade de ponto de vista**:

| termo | do sistema | do contato |
|---|---|---|
| "mensagem recebida" | contato → nós (`Inbound`) | nós → contato (`Outbound`) |
| "mensagem enviada" | nós → contato (`Outbound`) | contato → nós (`Inbound`) |

`receivedCount` é exatamente re-lível ao contrário, e o rótulo de tela — que
seria o lugar natural de fixar o referencial — **ainda não existe**.
`inboundCount` herda o vocabulário de `MessageDirection.Inbound`
(`MessageDirection.cs`), que é definido em relação ao sistema e não admite a
inversão. O termo do produto, "mensagens recebidas", fica como rótulo de tela
quando ela existir.

Registro em objeto, e não inteiro nu no corpo, pelo mesmo motivo de D4 da change
anterior — e aqui o segundo campo tem nome e endereço conhecidos (D6).

### D5 — `OccurredAt` é o instante de **recebimento pelo servidor**, e isso é um gatilho novo

**Nenhuma decisão anterior previa este item.** Ele saiu da exploração desta
change e vale ser escrito porque a propriedade de que a spec depende é
**acidental**.

```
Telegram :105 ─┐
               ├─→ receivedAt = DateTimeOffset.UtcNow ──→ Message.OccurredAt
Waha     :64  ─┘        (relógio do SERVIDOR na chegada do webhook)

PushNotificationEndpoints:130 ─→ occurredAt = DateTimeOffset.UtcNow ──→ OccurredAt
```

`TelegramWebhookModels.cs` **nem desserializa** o campo `date` do Telegram: o
modelo é `TelegramUpdate(TelegramMessage? Message)`. O instante que o provedor
registrou no evento não entra no sistema hoje por caminho nenhum.

**Para esta métrica, isso é bom.** Nenhum provedor consegue inserir mensagem "no
passado", então um período **fechado** responde sempre o mesmo número. É a
estabilidade que faltou a `LastActivityAt`, obtida por outra via.

**Mas o nome mente, e a propriedade não está declarada em lugar nenhum.** Vem de
três `UtcNow` espalhados em adapters e num endpoint, não de decisão escrita.

> **Gatilho, novo:** se uma change futura passar a parsear o campo de instante do
> **provedor** (o `date` do Telegram, o timestamp do WAHA) para dentro de
> `OccurredAt` — o que é pedido natural de *"mostrar a mensagem no horário em que
> o contato realmente enviou"* —, então **esta rota se torna retroativamente
> backdatable**: uma mensagem que chega hoje pode passar a cair num período
> fechado já medido, e a estabilidade que a spec promete deixa de valer **sem
> ninguém precisar tocar este código**.
>
> Quem fizer aquela change decide entre: manter os dois instantes em campos
> separados (recebimento × emissão), ou aceitar a instabilidade e reescrever o
> requisito de estabilidade desta spec. O que não pode é acontecer em silêncio.

Esta change **não altera** como `OccurredAt` é gravado. Renomear o campo para
`ReceivedAt` seria mudança de modelo com migração, fora do escopo declarado — e
o gatilho acima é o registro que a torna uma decisão adiada em vez de um
esquecimento.

### D6 — `outboundCount` fora, com a pergunta que ele traz junto

O card irmão "mensagens enviadas" é métrica sequenciada depois, e **não** é só
trocar o valor do enum. Outbound é persistida em dois estados:

```
PushNotificationEndpoints.cs:163   sender.SendAsync OK    → DeliveryStatus = Sent
                          :182   sender.SendAsync lança → DeliveryStatus = Failed   ← linha gravada assim mesmo
```

Logo: *"uma entrega que falhou conta como enviada?"* — pergunta sem resposta
hoje, e que **não é desta change**. Incluir `outboundCount` agora obrigaria a
respondê-la de passagem, no pior momento possível: sem rótulo de tela, sem
consumidor, e enterrada numa change cujo assunto é outro.

**Adiar não custa contrato.** A resposta é objeto, não inteiro nu, exatamente
para isso: `{ "inboundCount": N }` vira `{ "inboundCount": N, "outboundCount": M }`
sem quebra. E quando voltar, o custo de consulta é quase nulo — o mesmo
varrimento pode produzir os dois com `COUNT(*) FILTER (WHERE …)` numa passada.
Nada nesta decisão fecha essa porta.

### D7 — A unidade contada é a mensagem distinta, não a entrega de webhook

Dedup de reentrega é **real e já implementada**, em duas camadas:

```
IsDuplicateAsync (InboundMessageOrchestrator.cs:47)
   │  consulta por (SessionId, ExternalId)
   ├── já existe ──────────────────────────────→ não insere
   └── não existe → INSERT ──┬─→ ok
                             └─→ DbUpdateException (violação única)  (:67)
                                    │  corrida entre a leitura e o SaveChanges
                                    └─→ detach, tratado como duplicata

IX_messages_SessionId_ExternalId  UNIQUE
   WHERE Direction='Inbound' AND ExternalId IS NOT NULL   (AppDbContext.cs:168-170)
```

Consequência: um webhook reentregue pelo provedor **não** cria segunda linha, e
portanto **não** incrementa a contagem.

**Qual das duas camadas é a que sustenta — medido na implementação, não suposto.**
`IsDuplicateAsync` é **fast path**: mutá-la para retornar sempre `false` deixa a
suíte inteira verde (`203/203`), porque o `INSERT` seguinte viola o índice único e
o `catch` converte a violação em "deduplicada". A garantia é do **índice mais o
`catch`**; a consulta de aplicação só evita o round-trip que falharia. Quem
mexer nessa área depois deve saber qual das três peças pode sair sem quebrar o
requisito desta spec: a primeira pode, as outras duas não.

**Isso vai para a spec, não só para os testes.** É comportamento observável de
quem chama a rota — a diferença entre "quantas mensagens recebi" e "quantas vezes
o provedor me chamou" —, e uma spec que o omitisse deixaria a rota parecer
ingênua para quem lê só o contrato. O cenário correspondente é o único que
distingue esta contagem de um `COUNT` sobre entregas.

### D8 — Limites, validação e binding: herdados, não redecididos

Vêm inteiros de `inbox-sessoes-por-periodo` (D6 e D7 de lá), e este documento
**não os reabre** — só registra o que se aplica:

- `from` e `to` **obrigatórios**, sem intervalo implícito nem período relativo.
- Entram como **`string?`** na assinatura, não `DateTimeOffset?`. Com o tipo de
  data, um valor malformado falha no **binding**, antes do método rodar, e o
  ASP.NET Core devolve um `400` de forma **diferente** do `ValidationProblem` do
  resto da casa — a rota teria duas formas de erro conforme o defeito.
- Parse por `DateTimeOffset.TryParse` com
  `DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal`. Os dois
  têm papéis distintos e a falha de omitir o segundo é **500, não número errado**,
  e só num servidor cujo TZ não seja UTC — `Npgsql` recusa offset != 0 para
  `timestamp with time zone`. Medido na change anterior; registrado em
  `02-HISTORICO_E_STATUS.md`. Não se remede aqui, se herda.
- Erros acumulados num único `Dictionary<string, string[]>`: dois parâmetros
  defeituosos saem numa resposta só.
- `to < from` verificado **depois** de os dois parsearem — limite malformado é
  reportado como malformado, nunca como intervalo invertido.

### D9 — Sem teto de intervalo, mesmo gatilho da change anterior

Qualquer teto que esta change escolhesse seria número sem estado medido ao lado —
convenção 22 na forma exata que ela nomeia.

> **Gatilho**, herdado sem reescrever: *"recalibrar quando o primeiro deploy em
> produção tiver volume real"*. Ver **Open Questions**: a condição pode já ter
> sido satisfeita, e isso é pendência externa a esta change.

### D10 — Sem índice novo, **mesma conclusão de D9 anterior por razão diferente**

**O texto de D9 da change anterior não se copia — copiado, seria falso.** Lá a
afirmação era *"`sessions` não tem nada em `StartedAt`"*. Aqui existe índice com
`OccurredAt`:

```
IX_messages_SessionId_OccurredAt   (SessionId, OccurredAt)    AppDbContext.cs:164
                                    ▲
                                    └── coluna LÍDER
```

A consulta desta rota é `WHERE OccurredAt BETWEEN from AND to AND Direction = 'Inbound'`,
**sem predicado de `SessionId`**. Com a coluna líder irrestrita, não há *range
seek* — o índice existe e **não serve a esta consulta**. E `Direction` não está
no índice, então satisfazer o filtro exigiria acesso ao heap de qualquer forma,
o que tira do planejador a razão para preferi-lo a um *seq scan*.

Ainda assim, **sem índice novo nesta change**, pelo mesmo gatilho de D9: a volume
de desenvolvimento o Postgres faz *seq scan* e ignoraria o índice.

**Com uma ressalva que a change anterior não tinha, e que aperta o gatilho:**
`messages` cresce por um múltiplo de `sessions` — há N mensagens por sessão. O
volume que torna o índice necessário chega **antes** aqui do que lá.

> **O argumento é estrutural, não medido.** Não há contagem de linhas de
> `messages` em desenvolvimento registrada nesta change, e nenhum `EXPLAIN` foi
> rodado contra a consulta. O que está afirmado acima é a forma do índice
> (conferida no código) e a consequência dela para um *range seek* — não um plano
> observado. Quem recalibrar mede as duas coisas.

O teto (D9) e o índice (D10) continuam sendo **a mesma pergunta** — "qual o
volume real?" — e se recalibram juntos, agora para duas rotas em vez de uma.

**Consequência direta: esta change não tem migração.**

### D11 — Arquivos sob `Messages/`, seguindo onde `Message` já vive

`Message` é entidade de `Buteco.Inbox.Messages.Entities` (`Message.cs:1`), e a
leitura de mensagens já existente está em `Buteco.Inbox.Messages.Endpoints`
(`MessageEndpoints.cs`). O código novo segue o mesmo namespace.

É a mesma regra de D10 da change anterior — **pasta segue o domínio, não a
URL** —, e aqui ela é mais fácil: lá a rota `/sessions/…` foi para `Contacts/`
porque `Session` vive sob `Contacts.Entities`; aqui rota e domínio coincidem.

Nome da pasta de query segue o vizinho `GetSessionMessages/`: nome da query sem
o sufixo `Query`.

## Árvore de arquivos

```
apps/inbox/src/Buteco.Inbox/
├── Messages/
│   ├── Endpoints/
│   │   ├── MessageEndpoints.cs                             (existente, inalterado)
│   │   └── MessageSummaryEndpoints.cs                      NOVO
│   ├── Entities/
│   │   ├── Message.cs                                      (existente, inalterado)
│   │   ├── MessageDirection.cs                             (existente, inalterado)
│   │   └── …                                               (inalterados)
│   ├── Queries/
│   │   ├── GetSessionMessages/                             (existente, inalterado)
│   │   └── GetMessagePeriodSummary/                        NOVO
│   │       ├── GetMessagePeriodSummaryQuery.cs             NOVO
│   │       └── GetMessagePeriodSummaryQueryHandler.cs      NOVO
│   └── Responses/
│       ├── MessageResponse.cs                              (existente, inalterado)
│       └── MessagePeriodSummaryResponse.cs                 NOVO
└── Program.cs                                              +1 linha (MapMessageSummaryEndpoints)

apps/inbox/tests/Buteco.Inbox.Tests/
└── MessageSummaryEndpointsTests.cs                         NOVO

CHANGELOG.md                                                +1 entrada
02-HISTORICO_E_STATUS.md                                    +1 seção (rota, gatilhos, D5)
```

Sem migração (D10). Sem `libs/` — nada aqui é compartilhado entre apps, e a régua
de isolamento não é tocada: nenhum arquivo fora de `apps/inbox`.

## Risks / Trade-offs

**[Testes da rota contam o sistema inteiro, e o banco é compartilhado pela
classe de teste] →** cada `[Fact]` usa **janela própria, distante e disjunta**
(anos diferentes, bem no passado), como `SessionSummaryEndpointsTests` já faz.
Os demais testes da suíte criam mensagens em "agora", então nenhuma janela no
passado os alcança. Sem isso, um teste contaria as mensagens de outro.

**[`Message.OccurredAt` é `private set`, atribuído nas factories] →** os testes
retrodatam por `ExecuteSqlInterpolatedAsync`, mesmo mecanismo que
`ContactSessionResolverTests.cs:57-62` e `SessionSummaryEndpointsTests.cs:404` já
usam. Não é conveniência: não há como escolher o instante pela API — o que é
justamente a propriedade de D3.

**[Trocar `Direction` no handler deixaria a suíte verde] →** o `[Fact]` de que
**Outbound dentro da janela não entra em `inboundCount`** é a guarda específica,
e ela é verificável por mutação: trocando `Inbound` por `Outbound` (ou removendo
o predicado), **só** ele reprova. É o análogo do teste de `LastActivityAt` da
change anterior — a asserção que trava a definição escolhida, sem a qual a
escolha é comentário.

**[Remover a dedup deixaria a suíte verde] →** o `[Fact]` de webhook reentregue
é a guarda, e trava comportamento que **não é desta change** (a dedup é de
`inbox-mensagens-persistidas`). É guarda de regressão sobre dependência, não
cobertura de código novo — vale escrever porque o requisito de spec que ela
sustenta é desta change.

**[`COUNT` sem índice cresce com o volume] →** aceito, com gatilho (D9/D10), e
com a ressalva de que aqui o volume chega antes. O custo hoje é de *seq scan* em
tabela de desenvolvimento.

**[As duas rotas de período respondem números que não se implicam] →** risco de
leitura, não de código: ninguém deve inferir "mensagens recebidas" de "sessões
iniciadas", nem o contrário. Mitigado por um cenário de spec **explícito** — uma
mensagem recebida dentro do intervalo, em sessão iniciada **antes** dele, é
contada. Ele não testa código novo além do já coberto; existe para deixar a
diferença afirmada no contrato em vez de subentendida.

**[O nome `OccurredAt` não descreve o que o campo guarda] →** aceito, registrado
em D5 com gatilho. Renomear é mudança de modelo com migração, desproporcional ao
que esta change entrega.

## Migration Plan

Não há. Nenhuma migração de banco (D10), nenhum dado a transformar, nenhuma
mudança de contrato existente. O deploy é aditivo: a rota passa a existir e
ninguém a chamava antes.

**Rollback**: remover a rota. Nenhum estado persistido é criado por esta change,
então não há nada a desfazer no banco.

## Open Questions

**Há um piloto em produção, e `02-HISTORICO_E_STATUS.md:4882` ainda afirma o
contrário.** Aquele arquivo diz, textualmente, *"Não existe ambiente de produção
hoje"*, e é dessa afirmação que pendem:

- o gatilho de teto de intervalo e índice de `inbox-sessoes-por-periodo`
  (`:5496`, D8/D9 de lá), que esta change herda em D9/D10 e **estende para uma
  segunda rota**;
- dois outros itens do checklist "Primeiro deploy em produção" (`:4880`) que
  **não são desta change**: aplicar a migration `AddUniqueOpenSessionIndex` e
  rodar o censo de colisão de nome de tool.

Ou seja: **a condição do gatilho pode já ter sido satisfeita**, mas a informação
que ele pedia — o volume real — continua desconhecida. Saber que produção existe
não basta para escolher um teto ou um índice; seria trocar um número sem medição
por outro.

**Esta change não resolve isso, deliberadamente**, e não edita
`02-HISTORICO_E_STATUS.md` para corrigir a linha 4882: essa atualização tem
alcance maior que esta change (três itens, dois deles alheios a ela) e acontece
**uma vez só**, fora daqui — não duas vezes em paralelo, por esta proposta e pela
próxima, com risco de divergirem.

Pendências, para quem fizer aquela atualização:

1. **Qual o volume real do piloto** — linhas em `sessions` e em `messages`. Sem
   os dois números, D9 e D10 continuam pendurados mesmo com produção existindo.
2. **A linha 4882 e o checklist de `:4880`** precisam refletir que produção
   existe, o que reabre os outros dois itens do checklist.
3. Com os números em mãos, **recalibrar teto e índice juntos** — agora para
   `/sessions/summary` **e** `/messages/summary`, que é a mudança que esta change
   faz no gatilho herdado.

**Não é pergunta aberta:** nenhuma versão de runtime, framework ou biblioteca.
Esta change não acrescenta dependência nenhuma — usa `.NET 10`, EF Core e
Mediator já presentes no `apps/inbox`.
