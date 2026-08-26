## Why

Um agente hoje não sabe por qual canal está conversando nem tem como
confirmar a identidade do contato sem adivinhar — ele responde no mesmo
tom para WhatsApp e Telegram, e não pode dizer "seu número terminado em
1234" para ajudar o cliente a confirmar que é ele mesmo. Uma exploração
prévia (`inbox-contexto-canal`) investigou trazer também o nome de
exibição do contato (`Contact.DisplayName`) para dentro do prompt, mas a
revisão concluiu que o ganho que dependeria de texto livre do usuário
final (chamar a pessoa pelo nome) é pequeno frente ao risco de injeção de
prompt que abriria — e que a maior parte do valor prático (ajustar tom
por canal, confirmar identidade) já está disponível em dois campos que
**não** são texto livre: o tipo do canal e o identificador externo do
contato, ambos atribuídos pelo provedor, nunca digitados pela pessoa.

## What Changes

- `apps/inbox` passa a incluir, em `Message.Metadata` do `SendMessage`
  disparado pelo debounce, duas chaves escalares novas:
  `Metadata["channelType"]` (o `Channel.ChannelType` da sessão de origem)
  e `Metadata["contactExternalId"]` (o `Contact.ExternalId` do contato de
  origem) — mesmo mecanismo já usado por `messageInstant`
  (`inbox-instante-mensagem`), sem campo novo em `TaskJobMessage` e sem
  mudança em `apps/api`.
- `apps/workers` passa a ler essas duas chaves da última mensagem do
  usuário no histórico da task e a incluir um bloco de contexto de canal
  nas instruções enviadas ao LLM, informando o tipo de canal e o
  identificador do contato como o que ele estruturalmente é (identificador
  atribuído pelo provedor, não necessariamente um telefone formatado) —
  nunca persistido em `Agent.Instructions`, nunca no histórico de
  conversa, mesmo ponto único de montagem já usado pelo bloco de contexto
  temporal.
- Ausência de qualquer uma das duas chaves (cliente A2A externo que não as
  envia, ou valor presente porém vazio) é tratada como caminho normal,
  nunca falha de task — o bloco de contexto de canal simplesmente não
  aparece ou aparece parcial.
- **`Contact.DisplayName` NÃO entra nesta change.** Decisão de escopo, não
  esquecimento: é o único campo de texto verdadeiramente livre do usuário
  final entre os candidatos investigados, e o valor que dependeria dele
  (chamar a pessoa pelo nome) não compensa a superfície de dano aberta
  (agentes com tools MCP de escrita vinculadas, ou delegação para agentes
  com suas próprias tools, alcançáveis por instrução injetada no prompt).
  Ver `design.md` para a decisão completa e a alternativa considerada.

## Capabilities

### New Capabilities

Nenhuma — esta change estende metadata já transportada pelo mecanismo
existente, mesmo padrão de `inbox-instante-mensagem`.

### Modified Capabilities

- `inbox-message-orchestration`: o disparo do debounce (`apps/inbox`)
  passa a incluir `channelType` e `contactExternalId` em
  `Message.Metadata`, além do `messageInstant` já existente.
- `a2a-task-lifecycle`: o processamento da task (`apps/workers`) passa a
  ler essas duas chaves e a incluir um bloco de contexto de canal nas
  instruções enviadas ao LLM, com o mesmo tratamento de ausência já
  estabelecido para `messageInstant`.

## Impact

- **`apps/inbox`**: `DebounceSweepService.BuildSendMessageRequest`
  (arquivo já tocado por `inbox-instante-mensagem`) ganha duas escritas
  escalares novas em `Message.Metadata`, lendo `Channel.ChannelType` e
  `Contact.ExternalId` da sessão resolvida.
- **`apps/workers`**: `AgentExecutionService` ganha a extração das duas
  chaves novas (mesmo método/estilo de `ExtractMessageInstant`) e repassa
  para um builder de bloco de contexto de canal, concatenado às
  instruções junto com o bloco temporal já existente.
- **`apps/api`**: nenhuma mudança — `Message.Metadata` já sobrevive sem
  alteração de código (confirmado por `inbox-instante-mensagem`).
- **`apps/frontend`**: nenhuma mudança.
- Sem migração de banco — nenhum campo novo persistido, só chaves
  adicionais num dicionário já existente no contrato A2A.
