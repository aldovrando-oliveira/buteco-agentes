## Context

O painel de inventário (`frontend-inventario-catalogos`, arquivada em
2026-09-17) tem quatro cards de catálogo e nenhum de atividade. O `design.md`
daquela change listou três candidatos a quadro de atividade — mensagens
processadas, sessões ativas, sessões por período — e deixou os três **fora, cada
um com o que exigia**. Esta change atende o terceiro.

Estado atual do `apps/inbox`, conferido no código:

| fato | onde |
|---|---|
| Nenhuma rota conta sessões por intervalo | `ChannelSessionEndpoints.cs:12`, `ContactEndpoints` — as duas devolvem lista completa |
| O prefixo `/sessions` de nível superior **já existe** | `MessageEndpoints.cs:12` (`/sessions/{sessionId:guid}/messages`) |
| O único `MapGet` agregado do monorepo | `KnowledgeBaseEndpoints.cs:33` (`/knowledge-bases/indexing-summary`) |
| Nenhum parâmetro de query string existe em `apps/inbox` **ou** `apps/api` | `grep -rn "FromQuery\|AsParameters" apps/inbox/src apps/api/src` → zero |
| `sessions` tem **um** índice não-PK, e não é por data | `AppDbContext.cs:100-102` — `IX_sessions_ContactId`, único parcial `WHERE "ClosedAt" IS NULL` |
| Não há relógio injetável no `apps/inbox` | `Session.cs:31` e `ContactSessionResolver.cs:57-59` leem `DateTimeOffset.UtcNow` direto; `apps/workers` registra `TimeProvider.System` (`Program.cs:33`), `apps/inbox` não |

**O achado que eliminou uma das três definições possíveis** está registrado fora
desta change, porque vale além dela: `02-HISTORICO_E_STATUS.md`, item
*"Encerramento explícito de sessão"*. Em uma linha: `ClosedAt = null` não
significa "sessão aberta", significa "ninguém voltou a escrever desde então".
Não se repete aqui por extenso — aquele item é o lugar canônico. O efeito sobre
esta change está em **D3**.

## Goals / Non-Goals

**Goals:**

- Uma rota de **contagem** — não de listagem — que responda "quantas sessões no
  intervalo `[from, to]`", com custo independente de N, no molde de
  `GetKnowledgeBaseIndexingSummaryQueryHandler`.
- Uma definição de "sessão no período" **escolhida com o motivo escrito**, e um
  nome de campo que carregue essa semântica para quem ler o contrato depois.
- Uma única forma de corpo de erro para todos os defeitos de parâmetro.
- O que fica de fora fica **com gatilho declarado**, não omitido em silêncio.

**Non-Goals:**

- Nenhuma UI. A tela que consome este número é change futura (convenção 1).
- Nenhum arquivo em `apps/api`, `apps/workers` ou `apps/frontend`.
- Nenhum `TimeProvider` novo no `apps/inbox` (D5).
- Nenhum período nomeado ("últimos 7 dias") resolvido no servidor (D5).
- Nenhum teto de intervalo (D8) e nenhum índice novo (D9) — os dois com gatilho.
- Nenhum encerramento explícito de sessão, e nenhum card "sessões ativas": os
  dois dependem do item aberto citado acima, e nenhum deles é atendido aqui.

## Decisions

### D1 — Recurso de contagem, não listagem com contagem no cliente

O consumo final é um inteiro num card. Uma rota que devolvesse a lista para o
cliente contar traria payload inteiro para exibir um número — o erro que a
proposta anterior já recusou para "mensagens processadas".

**Alternativa considerada e descartada: somar N chamadas por canal no cliente.**
Descartada por código, não por gosto: `InventoryPage.tsx:28-31` já escreveu que
um `apps/inbox` lento não pode segurar a tela, e o card de canais é o único dos
quatro que fala com outro processo. N chamadas multiplicariam exatamente esse
risco, e é a agregação entre processos que a convenção 22 já discutiu.

A consulta é `COUNT` no banco — **nunca** `Select().ToList().Count()`, que sai
com o resultado certo e o custo errado, e passa despercebido por isso.

### D2 — `GET /sessions/summary`, rota de nível superior

A premissa de que esta seria a primeira rota de nível superior do `apps/inbox`
**não se sustenta**: `MessageEndpoints.cs:12` já registra
`/sessions/{sessionId:guid}/messages`. O prefixo existe; a rota nova não
inaugura namespace.

A forma do sufixo copia o único precedente agregado do monorepo:

```
/knowledge-bases/indexing-summary     KnowledgeBaseEndpoints.cs:33
/sessions/summary                     esta change
```

**Ordem de registro não precisa ser garantida**, e a razão já está escrita em
`KnowledgeBaseEndpoints.cs:29-31`: a restrição `:guid` da rota vizinha exclui a
cadeia literal, e o roteamento classifica literal acima de parâmetro de qualquer
forma.

**Sem exceção de autenticação.** `Program.cs:139-146` classifica toda rota como
autenticada por padrão e falha o startup para rota não classificada; a lista
passada a `ValidateRouteAuthenticationClassification` é só das anônimas. A rota
nova não a toca — e se alguém a esquecer, o startup reprova.

### D3 — "Sessão no período" = `StartedAt` dentro de `[from, to]`, inclusivo nos dois limites

As três definições possíveis, sobre o mesmo `P = [10/09, 17/09]`:

| sessão | `StartedAt` | `LastActivityAt` | `ClosedAt` | começou em P | atividade em P | aberta durante P |
|---|---|---|---|---|---|---|
| S1 | 08/09 | 12/09 | `null` | ✗ | ✓ | ✓ |
| S2 | 12/09 | 13/09 | 14/09 | ✓ | ✓ | ✓ |
| S3 | 14/09 | 14/09 | `null` | ✓ | ✓ | ✓ |
| **S4** | **02/03** | **02/03** | **`null`** | ✗ | ✗ | **✓** |
| S5 | 09/09 | 09/09 | 09/09 | ✗ | ✗ | ✗ |
| | | | **2** | **3** | **4** | |

**Overlap descartada — o dado que ela exige não existe.** S4 é um contato que
escreveu uma vez em março e nunca voltou: `ClosedAt` nulo até hoje. A definição
de overlap (`StartedAt <= to AND (ClosedAt IS NULL OR ClosedAt >= from)`) a
contaria em **todo** período desde março. Não é caso de borda — é o
comportamento normal de qualquer contato que não voltou. Ver o item aberto
citado em *Context*.

**`LastActivityAt` descartada — reescreve o passado.** `LastActivityAt` é
reescrito a cada mensagem (`Session.cs:35-38`), então o mesmo intervalo
**fechado** responde números diferentes conforme o dia da pergunta:

```
consulta em 13/09, período [10/09, 17/09] → S1 conta (LastActivityAt = 12/09)
S1 recebe mensagem em 20/09
consulta em 21/09, MESMO período          → S1 não conta mais
```

Um card de "últimos 7 dias" mascara isso porque a janela anda junto; qualquer
comparação entre janelas, ou seletor de intervalo, expõe. E há um segundo
defeito de proveniência: `RegisterActivity()` só é chamado a partir de
`FindOrCreateSessionAsync`, que tem **um** chamador —
`InboundMessageOrchestrator.cs:32`, o caminho de **entrada**. Mensagem que o
agente envia não move `LastActivityAt`; "atividade" ali significa *"o contato
escreveu"*, não *"houve conversa"*.

**`StartedAt` escolhida — é a única que particiona.** Atribuído no construtor
(`Session.cs:31`) e nunca reescrito: períodos disjuntos somam sem dupla
contagem, e o número de um período passado **nunca muda**. Semanticamente é
*"conversas novas iniciadas no período"* — e essa leitura casa com o registro da
change anterior, que listou "sessões ativas" e "sessões por período" como itens
**separados** (`archive/2026-09-17-frontend-inventario-catalogos/proposal.md:52`):
"quantas estão vivas agora" é o outro card, bloqueado pelo item aberto.

**Inclusivo nos dois limites**: `StartedAt >= from AND StartedAt <= to`. Uma
sessão com `StartedAt` exatamente igual a `from`, ou exatamente igual a `to`,
conta. Escolhido por ser a leitura literal de "dentro do intervalo `[from, to]`"
e por ser a única regra que não precisa de nota de rodapé no contrato.

### D4 — O campo se chama `startedCount`, não `count`

```csharp
public sealed record SessionPeriodSummaryResponse(int StartedCount);   // → { "startedCount": N }
```

É a convenção 13 aplicada ao **contrato da API**, não à tela: a convenção diz
que a UI nunca afirma mais do que o sistema sabe, e um campo chamado `count`
numa rota chamada `/sessions/summary` não afirma nada — o que o torna
re-lível como "sessões ativas" pelo próximo consumidor. O rótulo da tela, que
seria o lugar natural de corrigir a leitura, **ainda não existe**; o nome do
campo é a única superfície disponível hoje para carregar a semântica.

Registro em objeto, e não um inteiro nu no corpo, pelo mesmo motivo: um `200` com
corpo `7` não tem onde pendurar o nome, e fecha a porta para um segundo campo
(p.ex. `closedCount`, se o encerramento explícito existir um dia) sem quebrar o
contrato.

Nome do record e da query seguem a **rota**, não o campo — é o que o precedente
faz (`/knowledge-bases/indexing-summary` ↔ `GetKnowledgeBaseIndexingSummaryQuery`):
`SessionPeriodSummaryResponse`, `GetSessionPeriodSummaryQuery`. Forma do record
(`public sealed record …Response(…)` em `Contacts/Responses/`) é a da casa
(`ChannelSessionResponse.cs`, `SessionResponse.cs`). Sem `FromEntity` — não há
entidade de origem, é uma agregação.

### D5 — `from` e `to` obrigatórios, vindos de quem chama

**Não há relógio injetável no `apps/inbox`.** `apps/workers` registra
`TimeProvider.System` (`Program.cs:33`) e o injeta em três serviços;
`apps/inbox` não registra nenhum, e `Session.cs:31` lê `DateTimeOffset.UtcNow`
direto. Resolver períodos nomeados no servidor ("últimas 24h") exigiria "agora"
testável, e o alcance não é só o handler novo: `ContactSessionResolverTests.cs:57-62`
hoje simula a passagem do tempo com `UPDATE ... SET "LastActivityAt"` via SQL
justamente por não haver relógio injetável.

Escopo a mais para adiantar uma decisão — **quais janelas expor** — que só a
change de tela vai poder tomar. Com limites explícitos, essa decisão fica onde
ela pertence, e o handler não precisa de relógio nenhum.

**Os dois são obrigatórios, e é consequência direta disso:** sem "agora" no
servidor não existe default sensato para um limite ausente. Ausência de qualquer
um é erro de validação, nunca período implícito.

**Pelo mesmo motivo, não se valida `from`/`to` contra o relógio do servidor.**
Rejeitar data futura exigiria exatamente o relógio que esta decisão evitou
introduzir. A única coerência validada é a **dos dois parâmetros entre si**
(D7).

### D6 — Binding manual: `string?` na assinatura, parse no handler

Com `DateTimeOffset` (ou `DateTimeOffset?`) direto na assinatura, `from=banana`
falha no **binding**, antes de o handler rodar, e o ASP.NET Core devolve um 400
com corpo próprio — forma diferente do `ValidationProblem` de todo o resto da
casa. A rota teria **duas formas de erro** conforme o defeito fosse "malformado"
ou "incoerente", e quem escrevesse o cliente descobriria isso em produção.

Parâmetros entram como `string?` e são parseados no handler, o que dá uma única
forma para os três casos de D7.

**O parse é determinístico e independente do fuso do servidor:**

```csharp
DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value)
```

**Correção desta decisão feita na implementação (convenção 9).** A proposta
afirmava que, sem esses estilos, *"a mesma requisição responderia números
diferentes num servidor com `TZ=America/Sao_Paulo` e num em UTC"*. **Isso estava
errado**, e a mutação que removeu os estilos mostrou a causa real: o `Npgsql`
**recusa** `DateTimeOffset` com offset diferente de zero para `timestamp with
time zone` —

```
System.ArgumentException: Cannot write DateTimeOffset with Offset=-03:00:00 to
PostgreSQL type 'timestamp with time zone', only offset 0 (UTC) is supported.
```

— então a falha é **500, não número errado em silêncio**. Pior de um jeito
diferente do que estava escrito: só acontece em servidor cujo `TZ` não seja UTC,
porque com `TZ=UTC` o offset local já é zero e o defeito fica invisível na
máquina de quem escreve.

Os dois estilos têm papéis **distintos**, e cada um tem hoje o seu próprio
`[Fact]`, cada um verificado por mutação:

| estilo | o que sustenta | some sem ele | guarda |
|---|---|---|---|
| `AssumeUniversal` | qual **instante** um valor sem offset significa | valor nu recebe o offset local → 500 em servidor não-UTC | `BoundsWithoutOffset_AreInterpretedAsUtc` |
| `AdjustToUniversal` | normalizar para offset 0 antes do banco | valor com offset **explícito** chega não-normalizado → 500 | `BoundsWithExplicitNonUtcOffset_AreNormalizedToUtc` |

O segundo `[Fact]` **não existia no plano original** e foi acrescentado na
implementação: o requisito de spec já exigia normalizar offset explícito, e
nenhum teste cobria esse caminho — os demais mandam `"O"` (que já sai `+00:00`)
ou valor nu. Sem ele, `AdjustToUniversal` era uma linha sem guarda (convenção 15).

### D7 — `ValidationProblem` para os três casos, mesma forma de corpo

`TypedResults.ValidationProblem(Dictionary<string, string[]>)` é a forma da casa
para 400 — `ChannelEndpoints.cs:40,49-50,86,96-97` —, com chave em camelCase
(`BuildAgentNotFoundErrors`, `:182-183`) e mensagem em pt-BR. Os três casos:

| caso | chave | 
|---|---|
| `from` e/ou `to` ausente | `from` / `to` |
| valor não parseável | `from` / `to` |
| `to < from` | `to` |

Um único `Dictionary` acumulado, como `ValidateShape` já faz
(`ChannelEndpoints.cs:135`): dois parâmetros malformados saem numa resposta só,
não na primeira que falhar.

**A comparação `to < from` só roda depois de os dois parsearem** — comparar
valores não parseados não tem sentido, e reportar "incoerente" sobre um valor
que o cliente escreveu errado esconde o defeito real.

### D8 — Sem teto de intervalo, **com gatilho declarado**

Qualquer teto que esta change escolhesse seria número sem estado medido ao lado
— a convenção 22 na forma exata que ela nomeia. O estado real, já escrito no
`design.md` da change anterior: **dev tem 10 sessões e não existe produção**.

> **Gatilho de recalibração**, herdado sem reescrever: *"recalibrar quando o
> primeiro deploy em produção tiver volume real"*, condição já rastreada no
> checklist de `02-HISTORICO_E_STATUS.md:4765`.

A ausência é **declarada**, não omitida: quem ler esta rota depois encontra a
decisão e o gatilho, em vez de concluir que ninguém pensou no assunto.

### D9 — Sem índice novo em `StartedAt`, **mesmo gatilho**

`sessions` tem exatamente um índice não-PK (`AppDbContext.cs:100-102`,
`IX_sessions_ContactId`, único parcial `WHERE "ClosedAt" IS NULL`) e nada em
`StartedAt`. Criar um agora seria a mesma referência não medida de D8 pelo outro
lado: a 10 linhas o Postgres faz seq scan e ignoraria o índice de qualquer
forma.

**O teto (D8) e o índice (D9) são a mesma pergunta — "qual o volume real?" — e
se recalibram juntos, pelo mesmo gatilho.** Separá-los em dois gatilhos criaria
duas referências a envelhecer em vez de uma.

Consequência direta: **esta change não tem migração**.

### D10 — Arquivos sob `Contacts/`, seguindo onde `Session` já vive

`Session` é entidade de `Buteco.Inbox.Contacts.Entities` (`Session.cs:1`), e as
rotas de sessão já existentes estão em `Buteco.Inbox.Contacts.Endpoints`
(`ChannelSessionEndpoints.cs:6`). O código novo segue o mesmo namespace —
**apesar de a rota ser `/sessions/…` e não `/contacts/…`**, porque a organização
em pasta segue o domínio, não a URL (é o que `ChannelSessionEndpoints` já faz:
namespace `Contacts`, rota `/channels/{id}/sessions`).

Nome da pasta de query segue `GetChannelSessions/` — pasta com o nome da query
sem o sufixo `Query`.

## Árvore de arquivos

```
apps/inbox/src/Buteco.Inbox/
├── Contacts/
│   ├── Endpoints/
│   │   ├── ChannelSessionEndpoints.cs                     (—) inalterado
│   │   └── SessionSummaryEndpoints.cs                     (A) MapSessionSummaryEndpoints,
│   │                                                          GET /sessions/summary, binding
│   │                                                          string? + ValidationProblem (D2, D6, D7)
│   ├── Queries/
│   │   ├── GetChannelSessions/                            (—) inalterado
│   │   └── GetSessionPeriodSummary/
│   │       ├── GetSessionPeriodSummaryQuery.cs            (A) record com from/to já parseados
│   │       └── GetSessionPeriodSummaryQueryHandler.cs     (A) CountAsync no banco (D1, D3)
│   └── Responses/
│       └── SessionPeriodSummaryResponse.cs                (A) record (int StartedCount) (D4)
└── Program.cs                                             (M) uma linha: MapSessionSummaryEndpoints()
                                                               entre os Map* existentes (:132-137),
                                                               antes de
                                                               ValidateRouteAuthenticationClassification

apps/inbox/tests/Buteco.Inbox.Tests/
└── SessionSummaryEndpointsTests.cs                        (A) integração, InboxFactoryFixture

openspec/changes/inbox-sessoes-por-periodo/
├── proposal.md
├── design.md
├── specs/inbox-session-period-summary/spec.md
└── tasks.md
```

Sem migração (D9). Sem `libs/` (nada é compartilhado entre apps). Nenhum arquivo
de `apps/api`, `apps/workers` ou `apps/frontend`.

## Risks / Trade-offs

**[A contagem é global e a suíte compartilha banco por classe de teste] →**
`InboxFactoryFixture` é `IClassFixture`: um Postgres por classe, compartilhado
por todos os `[Fact]` daquela classe. Como `/sessions/summary` conta o sistema
inteiro, uma sessão criada pelo teste A entra na contagem do teste B. **Mitigação
obrigatória, não opcional:** cada teste usa uma **janela própria, distante e
disjunta** (p.ex. anos diferentes, bem no passado), retrodatando `StartedAt` via
`ExecuteSqlInterpolatedAsync` — mesmo mecanismo de
`ContactSessionResolverTests.cs:57-62`, e necessário de qualquer forma porque
`Session.StartedAt` é `private set`, atribuído no construtor: **não há como
escolher o instante pela API**. Os demais testes da suíte criam sessões em
"agora", então nenhuma janela no passado os alcança.

**[Um `to` date-only exclui o dia inteiro que o cliente quis incluir] →**
`to=2026-09-17` parseia como `2026-09-17T00:00:00Z`, e uma sessão iniciada às
14h daquele dia **não** conta — correto pela regra de D3, surpreendente para
quem escreveu a data pensando no dia. Mitigação: é o consumidor que monta o
instante, e a change de tela envia limites explícitos; o requisito de spec fala
em **instante**, não em dia, e a inclusividade é literal sobre o instante
recebido. Não se compensa isso no servidor "arredondando" `to` para o fim do dia
— seria o servidor adivinhando fuso, exatamente o que D6 evita.

**[`COUNT` sem índice cresce com o volume] →** aceito, com gatilho (D8/D9).
Hoje são 10 sessões em dev e não há produção; o número que justificaria índice
ou teto não existe, e inventá-lo agora é a convenção 22 pelo avesso.

**[O nome `startedCount` não impede que a tela rotule errado] →** verdade, e é
por isso que ele é mitigação parcial: o contrato carrega a semântica, mas a
convenção 13 continua valendo para a change de tela, que precisa escrever o
rótulo contra **esta** definição — "conversas iniciadas", não "sessões ativas".
Registrado aqui para a change futura encontrar.

**[Primeira query string do monorepo] →** `grep` por `FromQuery`/`AsParameters`
em `apps/inbox/src` e `apps/api/src` dá zero: não há precedente a copiar, e o
que esta change escolher vira o precedente. Mitigação: D6 e D7 escolhem a forma
**mais próxima do que a casa já faz para corpo de requisição** (validação manual
acumulando num `Dictionary`, `ValidationProblem`), em vez de introduzir um
mecanismo novo.

## Migration Plan

**Sem migração de banco** (D9): nenhuma coluna, nenhum índice, nenhum
`dotnet ef migrations add`. O `AppDbContextModelSnapshot` não muda.

Deploy é o deploy normal do `apps/inbox`. A rota é **aditiva e sem consumidor**
— nenhuma tela a chama ainda —, então:

- **Ordem**: não há acoplamento com outro app; pode subir sozinho.
- **Rollback**: reverter o deploy. Nada a desfazer no banco, nenhum consumidor a
  quebrar, nenhum dado escrito (a rota é só leitura).

## Open Questions

Nenhuma.

A única pergunta de produto desta linha de trabalho — *qual das três definições
de "sessão no período"* — foi levantada na exploração e **fechada antes desta
proposta**: `StartedAt`, inclusivo nos dois limites (D3).

O que continua em aberto **não é desta change** e já está registrado no lugar
canônico (`02-HISTORICO_E_STATUS.md`): o encerramento explícito de sessão, de
que dependem tanto um futuro card "sessões ativas" quanto qualquer tela que
precise distinguir conversa viva de conversa morta.
