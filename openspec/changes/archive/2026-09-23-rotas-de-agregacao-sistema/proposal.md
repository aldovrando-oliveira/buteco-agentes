## Why

As etapas 1 e 2 da linha `metricas-de-operacao` gravam, e estão **medindo em
produção** — conversa desde 22/09/2026 01:21, embedding desde 23/09/2026 01:18,
`America/Sao_Paulo`. Nada disso é consultável: não há rota, e o catálogo de 27
métricas existe como referência viva no `02` sem uma superfície que o sirva.

Esta é a **etapa 3**, e é nela que janela, fuso e escopo deixam de ser suposição
e viram contrato. Ela vem agora porque a convenção 1 põe backend antes de UI, e
as etapas 4 e 5 (as duas telas, com protótipos já aprovados) não podem começar
sem uma rota que sirva o dado.

**E ela vem com um pré-requisito medido, não previsto.** `27,7%` das tasks de
`a2a_tasks` caem em **outro dia e outro dia da semana** se o balde sair em UTC
— remedido em 23/09/2026 sobre 260 linhas, carimbos de 01/08 a 22/09 (o 29,5%
que circulava é de antes de cinco changes, convenção 22). `apps/api` **não tem
fuso**: zero `TimeProvider`, zero `TimeZoneInfo`, e nenhum `TZ` no compose de
produção para o serviço `api` — enquanto `apps/workers` valida o seu no boot e
falha se divergir. Os dois processos **já discordam sobre que dia é hoje**; hoje
a divergência é latente porque nada em `apps/api` renderiza data local, e é
exatamente esta change que a ativa.

## What Changes

A change tem **dois escopos**, e eles estão declarados porque carregar uma
alteração de configuração de ferramenta junto de uma etapa de linha de trabalho
sem dizer é o que torna um diff ilegível.

### Escopo 1 — etapa 3 da linha `metricas-de-operacao` (`apps/api`, comportamento)

- **`apps/api` passa a ter fuso.** O mesmo `TZ` do compose é entregue também ao
  serviço `api`, com `:?`, e é lido **como configuração da agregação** — nunca
  via `TimeZoneInfo.Local`. `TimeProvider.System` é registrado (o primeiro do
  app). O boot reprova se o nome de fuso não resolver.
  - **Contraria a letra de uma decisão registrada** (convenção 9): ela dizia
    "nunca do `TZ` do processo de `apps/api`". A razão dela — o balde não
    depender de qual container respondeu — fica preservada pelo `:?`, que dá o
    mesmo valor a toda réplica; e resolve o que a alternativa deixava aberto:
    **não havia mecanismo algum fazendo o fuso da agregação e o do worker
    concordarem**.
- **Uma rota nova, `GET /insights/system`**, com `from`/`to`, devolvendo o
  agregado inteiro da página aprovada — as 27 métricas no escopo do sistema.
  Uma rota e não 27 porque **janela, fuso, nulo e "medindo desde" se decidem uma
  vez**; N rotas dão N chances de discordarem sobre o balde.
- **Balde em SQL**, `AT TIME ZONE '<nome IANA>'`, com o nome vindo de
  configuração. **Nenhum índice de expressão** — o filtro de janela é range
  sobre o instante e o agrupamento vem depois.
- **"Medindo desde" vira um mapa de regimes**, não um texto único: são **dois**
  hoje (conversa e embedding, com datas diferentes), e um texto só mentiria
  sobre um deles. A fonte é o instante do deploy, constante por regime — não
  `min(StartedAt)`, que marcaria como "sem medição" um período medido e vazio.
- **A série não emite `0` antes do início do regime** — ausente antes, `0` só
  dentro. É o que sustenta o estado "período maior que a medição" do protótipo.
- **A condição de remoção do detector de tasks não-terminais é reescrita**
  (convenção 9). A cláusula registrada — *"sai quando a rota M32 cobrir as duas
  populações"* — testa a coisa errada: o detector é **série amostrada**, M32 é
  **consulta sob demanda**, e `a2a_tasks` não guarda histórico de status. M32
  não reconstrói um pico passado, que é justamente o que a `replicas-de-worker`
  precisa. **O detector fica**, e a condição passa a ser o fim da decisão que ele
  serve.
- **Quatro lacunas do mapa das 27 ficam nomeadas aqui, não na etapa 4** — M29
  (as recusas de `apps/api` não têm fonte de motivo **nenhuma**), M27/M28
  (parciais pelo mesmo motivo), M21/M22 (parciais por anulabilidade de
  `SubmittedAt`), M25 (o rótulo afirma mais do que o resíduo sabe).

**Nenhum índice entra**, e **nada é removido**. Não há **BREAKING**: a rota é
nova e nenhum contrato existente muda.

### Escopo 2 — `openspec/config.yaml` (método de trabalho)

**Duas alterações, de origens diferentes**, no mesmo arquivo — e a distinção
importa porque só uma delas é trabalho desta change:

**(a) A instrução, acrescentada ao bloco `context:`:** *"Sempre utilize a
ferramenta codegraph"*. **Decisão do dono, feita à mão**, e já presente na árvore
quando esta change foi aberta. Ela entra aqui porque as duas alternativas são
piores: deixá-la de fora do commit a perde, e levá-la sem declaração a esconde num
diff cujo assunto é outro — e a tarefa de conferência de escopo a apontaria como
achado de origem desconhecida.

**(b) A remoção da prosa solta da primeira linha do arquivo** — `Crie (ou edite,
se já existir) o arquivo openspec/config.yaml com o seguinte conteúdo:`. Defeito
**pré-existente** (está em `HEAD`, anterior à edição do dono), e é **a única coisa
que esta change faz** no Escopo 2. É resto de instrução de criação que sobreviveu
porque o formato tolera: a mesma família de texto que parece dado e não é.

**Removida agora, e não registrada como item**, porque o arquivo já é o assunto
declarado deste escopo — e a alternativa é ela sobreviver indefinidamente, já que
nunca há motivo para abrir aquele arquivo.

**Conferido antes de decidir o que a remoção significa:** a linha **não** está
sendo injetada em lugar nenhum. O texto não aparece em campo algum do que
`openspec instructions` devolve para `proposal`, `design`, `specs` e `tasks` — o
`openspec` lê `schema`, `context` e `rules` e ignora a chave de topo estranha, e
`openspec validate --all` passa com ela presente. **Logo a remoção é higiene, sem
efeito observável no prompt de artefato nenhum** — e não correção de ruído, que é
o que teria sido se a conferência desse o contrário.

**Ela não altera nada do que o Escopo 1 entrega** — nem a rota, nem o contrato de
fuso e janela, nem o schema, nem o mapa das 27. É mudança de **como o trabalho é
feito**, e por isso vale a partir daqui **para toda change seguinte**, não só
para esta.

**Verificado que a instrução é cumprível, em vez de suposto** (o item 5 da
conferência): o bloco `context:` é injetado nas instruções de todo artefato —
conferido lendo o `context` devolvido por `openspec instructions`, e a frase está
lá. A ferramenta responde: CLI `codegraph` **1.5.0** e índice
`.codegraph/codegraph.db` presente, com `codegraph explore` devolvendo resultado.

**E ela tem um pré-requisito que não estava declarado — agora está.**
`.codegraph/` é ignorado por git **inteiro** (`*`, com exceção do próprio
`.gitignore`), e a CLI vive fora do repositório. Logo **nem o índice nem a
ferramenta acompanham um clone**: em máquina nova, ou em CI, a instrução é
inexecutável até alguém instalar a CLI e indexar. Isso é declarado no `02` junto
da instrução, e **não** é resolvido por esta change — versionar índice de 50 MB ou
acrescentar passo de bootstrap é decisão própria, com posição registrada.

## Capabilities

### New Capabilities

- `api-system-timezone`: fuso horário de `apps/api` — `TZ` entregue ao serviço,
  lido como configuração, `TimeProvider` registrado, e checagem de integridade no
  boot (convenção 8) que reprova nome de fuso que não resolva. Existe separada
  porque a change B herda este contrato sem reabri-lo, e porque a falha que ela
  guarda é de boot, não de consulta.
- `system-insights-aggregation`: a rota `GET /insights/system` — a janela
  (`from`/`to`, parse, validação, ausência de teto), o balde local, o agregado
  das 27 métricas no escopo do sistema, a preservação de nulo, e o mapa de
  regimes de "medindo desde".

### Modified Capabilities

Nenhuma. `agent-execution-metrics` e `knowledge-embedding-metrics` são **lidas**,
não alteradas; `workers-nonterminal-task-detection` não muda de comportamento —
a condição de remoção reescrita vive no `02` e no comentário do serviço, não nos
requisitos dela; e `route-authentication` não muda, porque rota autenticada nova
**não declara nada**.

## Impact

**`apps/api`** — todo o código desta change.

- `Program.cs`: registro de `TimeProvider.System`, checagem de fuso no boot,
  e o `app.MapInsightsEndpoints()`.
- Feature nova de leitura, no molde de `GetKnowledgeBaseIndexingSummary`
  (handler de Mediator + `AsNoTracking()` + agregação no banco).
- Suíte de teste: entra `Microsoft.Extensions.TimeProvider.Testing`, que **não**
  é referenciada hoje — custo nomeado da escolha de `TimeProvider` sobre `now()`
  no SQL.

**Infraestrutura** — `docker-compose.prod.yml` passa `TZ` ao serviço `api` com
`:?`, como já faz com `workers`. `docker-compose.yml` e `.env.example`
acompanham.

**Banco** — somente leitura. **Nenhuma migração**, nenhum índice, nenhuma coluna.
As consultas tocam `task_executions`, `provider_calls`, `delegation_outcomes`,
`knowledge_indexing_attempts`, `embedding_calls` e — pela primeira vez na linha —
`a2a_tasks`.

**Forma que decide o desenho das consultas:** três das cinco tabelas de métrica
**não têm coluna temporal própria** (`provider_calls` e `embedding_calls` não têm
nenhuma; `delegation_outcomes` tem só `LastObservedAt`, anulável, que é a última
leitura e não o instante do evento). Toda janela sobre elas é **join ao pai**, e
todos caem em PK ou índice existente. É a razão de a rota não poder ser "uma
consulta por tabela".

**`apps/workers`** — só o comentário de `NonTerminalTaskDetectorService` com a
condição de remoção corrigida. Nenhuma mudança de comportamento.

**`apps/frontend`** — nada. Etapas 4 e 5.

**Documentação** — `01` ganha a seção de fuso de `apps/api` ao lado da de
`apps/workers`; o `02` recebe as lacunas do mapa, os itens de índice com gatilho
e a condição de remoção reescrita.

**Método de trabalho (Escopo 2) — `openspec/config.yaml`, com duas alterações.**
Item próprio, e não diluído em "documentação", porque o alcance é diferente de
tudo acima: as mudanças de documentação descrevem **esta** change, e a instrução
descreve **como toda change seguinte é feita**. Um leitor que procure "o que
mudou no sistema" não deve encontrá-la; um que pergunte "desde quando se usa
`codegraph` aqui" deve.

**(a) A instrução** — alcance prático: o bloco `context:` é lido por
`openspec instructions` e entra nas instruções de **todos** os artefatos, de toda
change — proposta, design, specs e tasks. Nenhum efeito em runtime, em build, em
teste ou em deploy.

**(b) A remoção da prosa solta** — alcance prático: **nenhum**, e isso foi
verificado, não suposto. A linha não é injetada em artefato algum, e
`openspec validate --all` passa com e sem ela. O impacto é de legibilidade do
arquivo, não de comportamento de ferramenta.

**A distinção de origem é o que o `Impact` precisa carregar:** a (a) é decisão do
dono que a change apenas **declara**; a (b) é limpeza de defeito pré-existente que
a change **executa**. Só a segunda aparece como tarefa de implementação.

**Pré-requisito que passa a estar declarado:** a instrução só é cumprível numa
máquina que tenha a CLI `codegraph` instalada **e** o índice construído. Nenhum
dos dois acompanha o clone (`.codegraph/` é integralmente ignorado por git; a CLI
mora fora do repositório). Declarado no `02`; **resolver** fica como item com
posição própria.
