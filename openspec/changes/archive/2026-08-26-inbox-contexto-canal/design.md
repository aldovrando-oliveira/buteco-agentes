## Context

`inbox-instante-mensagem` (arquivada) estabeleceu o mecanismo de
transporte: `Message.Metadata` (contrato A2A, `Dictionary<string,
JsonElement>?`) sobrevive de `apps/inbox` até `a2a_tasks.payload` sem
nenhuma mudança em `apps/api`, e `AgentExecutionService` já relê a
`AgentTask` inteira para extrair a última mensagem do usuário. Esta
change reaproveita esse mesmo mecanismo para duas chaves novas —
`channelType` e `contactExternalId` — sem construir transporte novo.

Uma rodada de `/opsx:explore` (`inbox-contexto-canal`) investigou também
trazer `Contact.DisplayName` (nome de exibição, texto livre do usuário
final) para o prompt, e a revisão decidiu não fazer isso nesta change —
ver Decision D1. As decisões D1-D3 abaixo são veredito de revisão humana
sobre essa exploração, não reabertas aqui.

## Goals / Non-Goals

**Goals:**
- Transportar `Channel.ChannelType` e `Contact.ExternalId` da sessão de
  origem, em `apps/inbox`, até um bloco de contexto de canal montado em
  `apps/workers` e concatenado às instruções do agente.
- Fazer isso sem campo novo em `TaskJobMessage` e sem mudança em
  `apps/api` — mesmos achados fechados de `inbox-instante-mensagem`,
  reconfirmados para este caso (`ExternalId`/`ChannelType` não são texto
  digitado pelo usuário; o transporte é o mesmo dicionário já testado).
- Tratar ausência de qualquer uma das duas chaves como caminho normal,
  nunca falha de task.
- Não afirmar mais do que o sistema sabe sobre `contactExternalId` — é um
  identificador atribuído pelo provedor do canal, não necessariamente um
  telefone formatado (WAHA usa dígitos de telefone; Telegram usa um
  inteiro de chat, sem relação com telefone).

**Non-Goals:**
- **`Contact.DisplayName` no prompt.** Decisão com gatilho (D1), não
  esquecimento.
- Quinto contrato de plugin de canal (`IContactDisplayFormatter` ou
  equivalente). Decisão com gatilho (D2).
- Fechar o item em aberto do `PushName` do WAHA
  (`WahaInboundWebhookHandler.cs:52` já lê
  `payload.Data?.Info?.PushName`) — confirmação contra instância real
  segue pendente, sem relação com esta change (que não usa `DisplayName`).
- Alterar o que a linha já fixou: bloco de contexto temporal em si
  (`TemporalContextBlockBuilder`), regra de precedência de expressões de
  tempo relativas, ponto único de montagem (`AgentExecutionService`),
  fuso e cultura do worker, limiar de defasagem.
- Propagar contexto de canal para tasks de delegação — o Target é tool
  call interna, sem superfície de saída própria para o cliente; quem fala
  com o canal é sempre o Source (já respondido pela exploração).
- Contexto como argumento estruturado de tool call MCP — linha própria,
  fora de escopo.
- Formatação de `contactExternalId` como telefone legível (ex. máscara
  `+55 11 91234-5678`) — o valor é passado cru; formatação por canal
  reabriria a pergunta do quinto contrato de plugin (D2), sem consumidor
  que justifique o custo agora.

## Decisions

### D1 — `Contact.DisplayName` fica fora do prompt (veredito da revisão, não reaberto)

O ganho que dependeria de texto livre do usuário final é um só (chamar a
pessoa pelo nome). Contra isso: sanitização por classe de caractere
protege contra ataque estrutural, não contra injeção semântica (uma
frase inteira feita só de letras e espaços passa ilesa por qualquer
allowlist); o único delimitador textual disponível no código
(`TemporalContextBlockBuilder.NotAUserMessageMarker`) nunca foi testado
sob conteúdo adversarial; e o dano alcançável por uma instrução injetada
não é hipotético — `IMcpToolSetResolver.ResolveAsync` resolve, por
agente, qualquer tool de qualquer servidor MCP vinculado via
`AgentMcpServer.AllowedTools`, sem distinção de leitura/escrita, e
delegação estende isso para as tools de outro agente. A relação
valor/risco não compensa nesta change.

**Alternativa considerada e não adotada**: usar `DisplayName` fora do
prompt — o sistema monta uma saudação em código (ex. "Olá, {nome}!" como
primeira mensagem de uma sessão nova) sem o modelo nunca ver o texto
livre. Eliminaria o ataque por construção, mas exigiria um caminho de
resposta que não passa pelo LLM (o desenho atual é o agente produzir o
texto inteiro da resposta), perderia naturalidade (o nome apareceria só
onde o código decidiu, não onde a conversa pediria) e não cabe no
processamento atual de `AgentExecutionService`, que não distingue
"primeira mensagem da sessão" de qualquer outra. Registrada como caminho
disponível se o produto decidir que "chamar pelo nome" é essencial o
suficiente para justificar esse desenho separado — não implementada
aqui.

**Gatilho para revisitar**: a justificativa para não fazer é sobre
custo/benefício e ausência de mitigação madura, não impossibilidade —
revisitar se surgir um mecanismo de defesa (ex. um segundo modelo
classificando o `DisplayName` como seguro antes de liberá-lo, ou uma
mudança de desenho que tire tools de escrita do alcance de agentes
voltados a canais de atendimento) que mude essa conta.

### D2 — Sem quinto contrato de plugin de canal (veredito da revisão, não reaberto)

`ChannelType` já é string simples, sem lógica por canal necessária para
usá-la no bloco de contexto. `ExternalId` cru (dígitos de telefone no
WAHA, inteiro de chat no Telegram) já serve ao propósito de "identificador
para o agente citar" sem precisar de formatação — o texto do bloco (ver
D4 abaixo) já deixa claro que não é necessariamente um telefone. Não há
lógica específica por canal a encapsular nesta change — convenção 2 (sem
abstração prematura) não é satisfeita.

**Custos registrados para quando a pergunta voltar** (ex. se um canal
futuro exigir formatação diferente de identidade):
- **Contrato opcional** (`IContactDisplayFormatter` ou equivalente):
  seguiria o molde de `IChannelWebhookProvisioner` — 1 interface + 2
  implementações (WAHA/Telegram) + registro DI keyed por adapter + 1
  dublê de teste (`TestContactDisplayFormatter`) — sem entrar na lista
  obrigatória de `ValidateChannelAdapterRegistrations`, só na checagem de
  "registrado sem os três contratos obrigatórios por trás".
- **Montagem genérica** (`if/else` por `ChannelType` fora da fronteira de
  adapter): mais barata agora, mas contraria o motivo explícito de
  `ChannelType` ser string aberta (seção "Contrato de plugin de canal",
  `01-ARQUITETURA_E_CONVENCOES.md`), e sem checagem de startup que torne
  um canal futuro sem branch auditável.

### D3 — Chaves escalares separadas, não um objeto

`Message.Metadata["channelType"]` e `Message.Metadata["contactExternalId"]`
como duas chaves escalares (string), não `Message.Metadata["channelContext"]
= { channelType, contactExternalId }`. Mesma disciplina de D2 de
`inbox-instante-mensagem`: um valor escalar elimina por construção a
variante séria do mecanismo em que um `JsonElement` já materializado
sobrevive à re-serialização de `PostgresTaskStore.SaveTaskAsync` sem
reaplicar naming policy (`push-notification-config-codec-encoder`, D6).
Com só dois campos, um objeto não economiza o suficiente para justificar
depender, de novo, de nunca esquecer `A2AJsonUtilities.DefaultOptions` no
ponto de escrita — disciplina que já falhou duas vezes nesta base
(`ConversationSessionCodec`, `PushNotificationConfigCodec`).

O ponto de escrita (`DebounceSweepService.BuildSendMessageRequest`, em
`apps/inbox`) usa `JsonSerializer.SerializeToElement(value,
A2AJsonUtilities.DefaultOptions)` para cada uma das duas chaves,
explicitamente — mesmo helper (duplicado por app, sem `ProjectReference`
cruzado) já usado por `messageInstant`.

### D4 — Nomes das chaves, formato de fio, e o que o bloco afirma sobre `contactExternalId`

Nomes: `channelType` e `contactExternalId`, seguindo o estilo já
estabelecido (`messageInstant`, `conversationSession`,
`pushNotificationConfig`) — camelCase, sem prefixo de app. Formato do
valor: string crua em ambos os casos — `channelType` é o valor de
`Channel.ChannelType` sem transformação (ex. `"waha"`, `"telegram"`);
`contactExternalId` é o valor de `Contact.ExternalId` sem transformação
(ex. `"5511912345678@c.us"` no WAHA, `"123456789"` no Telegram) — nenhum
parsing, nenhuma extração de telefone.

Isso é contrato de formato de fio (convenção 12), não detalhe de
implementação: um cliente A2A externo que queira preencher essas chaves
manualmente precisa saber que são strings cruas, não objetos
estruturados.

O texto do bloco de contexto de canal (`ChannelContextBlockBuilder`,
novo, mesmo espírito de `TemporalContextBlockBuilder` mas arquivo
separado — Non-Goal preserva o builder temporal intocado) nomeia o
identificador pelo que ele é, nunca por telefone:

```
Identificador do contato atribuído pelo canal: {contactExternalId}
```

em vez de "Telefone do contato" ou qualquer formulação que presuma
formato de telefone — convenção 13 (nunca afirmar mais do que o sistema
sabe) aplicada ao agente, não só à UI. `channelType` entra como:

```
Canal de origem desta conversa: {channelType}
```

### D5 — Bloco próprio, concatenado após o bloco temporal, sem tocar `TemporalContextBlockBuilder`

`ChannelContextBlockBuilder.Build(channelType, contactExternalId)` é uma
função pura nova, com seu próprio marcador de "não é mensagem do
usuário" (mesmo texto/padrão de `NotAUserMessageMarker`, mas constante
própria — os dois blocos não precisam compartilhar a constante para
funcionar, e manter separado evita qualquer acoplamento acidental entre
os dois builders). Diferente de `DisplayName` (D1), o conteúdo aqui é
**inteiramente controlado pelo sistema/provedor** (nunca texto digitado
pelo usuário final) — o marcador não testado sob condição adversarial
(achado da exploração) não é uma lacuna aqui, porque não há conteúdo
adversarial para ele delimitar.

Concatenação em `AgentExecutionService`: `Instructions` → bloco temporal
→ bloco de contexto de canal, reaproveitando
`TemporalContextBlockBuilder.Concatenate(string?, string)` **duas vezes**
em sequência (`Concatenate(Concatenate(agent.Instructions, temporalBlock),
channelBlock)`) em vez de estender sua assinatura — a função já é
genérica o suficiente (concatena dois textos com separador de linha em
branco dupla, tratando o primeiro nulo/vazio) para servir aos dois
pontos sem mudança. Preserva o Non-Goal de não tocar
`TemporalContextBlockBuilder`.

Bloco de contexto de canal omitido inteiramente quando **as duas** chaves
estão ausentes (nenhum canal a descrever) — ver D6 para os casos.

### D6 — Ausência é caminho normal; tipo errado é bug de produtor, com log de aviso

Distinção que a primeira versão deste documento não fazia: "chave não
mandada" e "chave mandada com o tipo errado" não são o mesmo caso. A
primeira é comportamento esperado de um cliente A2A que não conhece
essas chaves — silêncio é o correto. A segunda é sinal de bug em algum
produtor (a própria `apps/inbox` tem um teste de acordo garantindo que
nunca produz isso — Tarefa 3.1 —, mas um cliente A2A externo pode), e
merece visibilidade, exatamente como o mesmo caso já resolvido para
`messageInstant` em `inbox-instante-mensagem` (valor ilegível → trata
como ausência **e** loga aviso).

Por campo (`channelType`, `contactExternalId`), independentemente:

- **Ausência silenciosa**: `Message.Metadata` é `null`; a chave não está
  presente; ou a chave está presente com `JsonValueKind.String` e valor
  vazio (`""`). Caminho normal — sem log.
- **Tipo errado, com log de aviso**: a chave está presente com
  `JsonValueKind` diferente de `String` (número, objeto, array,
  booleano, ou `null` JSON explícito). Tratado como ausência para efeito
  do bloco (mesmo resultado visual que a ausência silenciosa), **mas**
  emite um log de nível aviso identificando a task e o nome do campo —
  mesmo padrão de `ExtractMessageInstant`, não um caso novo de
  instrumentação.

Composto sobre os dois campos, o bloco de contexto de canal tem três
formas, independentemente de qual dos dois motivos (ausência ou tipo
errado) levou cada campo a "sem valor":

1. Os dois campos sem valor — bloco inteiramente omitido (nenhuma
   linha).
2. Um campo com valor, o outro sem valor — bloco mostra só a linha do
   campo presente, sem mencionar o campo ausente (nenhum "canal
   desconhecido" ou placeholder — omissão de linha, não afirmação de
   desconhecido, convenção 13).
3. Os dois campos com valor — bloco completo, as duas linhas.

A extração das duas chaves vive num único método privado em
`AgentExecutionService` (mesmo estilo de `ExtractMessageInstant`),
chamado no mesmo ponto de montagem — sem caminho de extração separado
para tasks delegadas, porque contexto de canal não se propaga na
delegação (Non-Goals).

## Risks / Trade-offs

- **[Risco] Valor gravado fora do contrato de serialização** (mesma
  classe de defeito de `PushNotificationConfigCodec`) → **Mitigação**:
  D3 (escalares, elimina a variante séria por construção) + disciplina
  de sempre passar `A2AJsonUtilities.DefaultOptions` no ponto de escrita
  + teste de acordo real (convenção 11) afirmando o JSON bruto
  persistido.
- **[Risco] Chaves ausentes ou com valor inesperado quebram a task** →
  **Mitigação**: D6, ausência e tipo errado tratados por campo, compostos
  nas três formas do bloco (nada/parcial/completo), testados em pares
  (convenção 5).
- **[Risco] Chave gravada com tipo errado por um produtor com bug passa
  em silêncio, sem sinal para investigar** → **Mitigação**: D6, tipo
  JSON diferente de string emite log de nível aviso identificando task e
  campo — mesmo padrão já usado para `messageInstant` ilegível, com
  cenário de spec e teste próprios (não apenas "tratado como ausência").
- **[Risco] Agente afirmar que `contactExternalId` é um telefone quando
  não é** (ex. Telegram, onde é um inteiro de chat sem relação com
  telefone) → **Mitigação**: D4, o texto do bloco nomeia o valor como
  "identificador atribuído pelo canal", nunca "telefone" — com teste
  afirmando o conteúdo literal do bloco montado, não só que a chave foi
  lida.
- **[Risco] Injeção de prompt via texto livre do usuário final** → **Não
  se aplica a esta change**: `Contact.DisplayName` (o único candidato de
  texto livre) fica fora do prompt (D1). `channelType` é fechado pelo
  adapter; `contactExternalId` é atribuído pelo provedor, nunca digitado
  pela pessoa. Registrado explicitamente para não ser confundido com
  risco mitigado — é risco ausente por desenho, categoria diferente.
- **[Risco] Bloco de contexto de canal e bloco temporal divergindo em
  estilo/marcador ao longo do tempo** (dois builders separados, D5) →
  **Mitigação**: aceito conscientemente — o custo de compartilhar
  infraestrutura entre os dois agora (antes de haver um terceiro bloco)
  seria abstração prematura (convenção 2); revisitar se um terceiro
  bloco de contexto aparecer.

## Migration Plan

Nenhuma migração de banco — nenhum campo novo persistido, `channelType`
e `contactExternalId` são lidos de colunas já existentes
(`Channel.ChannelType`, `Contact.ExternalId`). Mudança de protocolo é
aditiva: duas chaves novas e opcionais em `Message.Metadata`, dicionário
já existente no wire shape do A2A — nenhum cliente A2A existente quebra
por não enviá-las, nenhum dado persistido precisa de backfill. Sem fases
de deploy — os dois apps tocados (`apps/inbox`, `apps/workers`) podem
subir na ordem normal.

## Open Questions

Nenhuma incerteza real de negócio/produto em aberto — D1, D2 e D3 são
veredito de revisão humana sobre a exploração prévia, não reabertos
nesta change. D4-D6 são decisões técnicas de implementação, fechadas
acima. O único item que poderia reabrir D1 (mecanismo de defesa que
mude a conta) está registrado como gatilho explícito em D1, não como
pergunta pendente desta change.

## Árvore de arquivos tocados

```
apps/inbox/src/Buteco.Inbox/
└── Orchestration/
    └── DebounceSweepService.cs          # BuildSendMessageRequest: seta
                                          # Message.Metadata["channelType"]
                                          # e ["contactExternalId"]

apps/inbox/tests/Buteco.Inbox.Tests/
└── Orchestration/
    └── DebounceSweepServiceTests.cs     # novo(s) caso(s) de teste

apps/workers/src/Buteco.Workers/
└── Agents/
    ├── AgentExecutionService.cs         # extração de channelType/
    │                                    # contactExternalId, concatenação
    │                                    # do bloco de contexto de canal
    └── ChannelContextBlockBuilder.cs    # NOVO — função pura, mesmo
                                         # espírito de
                                         # TemporalContextBlockBuilder

apps/workers/tests/Buteco.Workers.Tests/
└── Agents/
    ├── AgentExecutionServiceTests.cs         # novo(s) caso(s) de teste
    └── ChannelContextBlockBuilderTests.cs    # NOVO — testes da função pura

tests/InboxOrchestratorRoundTrip.Tests/
└── RoundTripTests.cs                    # novo cenário de acordo real
                                          # (convenção 11) afirmando
                                          # channelType/contactExternalId
                                          # no JSON bruto persistido
```

Sem mudança em `apps/api` e sem mudança em `apps/frontend` — portanto sem
testes novos nesses dois apps.
