# Changelog

Todas as mudanças relevantes deste projeto são registradas aqui.

O formato segue [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/) e
o versionamento pretende seguir
[Versionamento Semântico](https://semver.org/lang/pt-BR/).

> **Nenhuma versão oficial foi fechada ainda.** Todo o trabalho até aqui está
> em `[Unreleased]`, agrupado por linha de trabalho. O primeiro release
> receberá número e data quando for cortado.

---

## [Unreleased]

### Added

**Estrutura do monorepo**

- Monorepo com quatro apps isolados — `apps/api`, `apps/workers`,
  `apps/inbox` e `apps/frontend` — sem nenhuma referência de projeto cruzada.
- Central Package Management via `Directory.Packages.props`, com versão do
  SDK fixada em `global.json`.
- `libs/ProviderCatalog`, única biblioteca compartilhada, referenciada por
  `apps/api` e `apps/workers`.

**Catálogo de agentes**

- CRUD completo de agente, com ativação e desativação.
- CQRS via `Mediator` em `apps/api`.
- `Description` e `Skills` por agente.
- Painel web de cadastro, edição e listagem de agentes.

**Protocolo A2A**

- Endpoint A2A por agente (`/agents/{id}/a2a`) com `SendMessage` e `GetTask`.
- Store durável de tasks e eventos em Postgres — sem store em memória.
- `AgentCard` por agente em `GET /agents/{id}/.well-known/agent-card.json`,
  montado a cada requisição, com `SecuritySchemes` declarados.
- Push notification por webhook, disparada por `apps/workers` ao concluir uma
  task.
- Endereços A2A do agente expostos na resposta de `GET /agents/{id}` e da
  listagem, montados no servidor a partir da URL pública configurada.
- Guia de integração para clientes externos em
  [`docs/a2a-integration.md`](docs/a2a-integration.md).

**Provedores de LLM**

- Suporte a OpenAI, Anthropic (Claude) e Google (Gemini), com `Provider` e
  `Model` por agente.
- `GET /providers` listando apenas os provedores com credencial configurada.
- Catálogo de modelos e seleção em cascata no painel.

**Histórico de conversa**

- Sessão persistida por `contextId`.
- Resumo incremental via `CompactionProvider` quando a conversa cresce além
  do limite de histórico nativo.

**Integração MCP**

- Catálogo de servidores MCP, com credencial criptografada em AES-GCM.
- Vínculo agente-servidor com seleção granular de tools por vínculo.
- Execução real de tool call durante o processamento da task.
- Painel de catálogo de servidores, visão inversa de uso (quais agentes usam
  cada servidor, com quais tools) e catálogo de tools no detalhe.

**Delegação entre agentes**

- Vínculo unidirecional entre agentes, com controle de profundidade.
- Execução real: um agente chama outro como tool interna, no mesmo banco, sem
  HTTP externo.
- Seção de delegações inline no detalhe do agente.
- **A aba de delegações distingue recusa permanente de falha transitória.**
  Quando o servidor recusa o conjunto de agentes-alvo — por fechar ciclo, por
  auto-delegação, por id que não corresponde a agente, ou por conjunto ausente
  —, a tela passa a exibir **a mensagem que o servidor enviou**, num aviso que
  permanece, em vez do texto genérico *"Tente novamente"*. Para a recusa por
  ciclo isso significa mostrar o **caminho** que fecha o ciclo, pelos nomes dos
  agentes, que é a única informação que resolve o problema e que a tela
  descartava. Mandar repetir uma recusa permanente afirmava mais do que o
  sistema sabe: tentar de novo nunca ia funcionar. Falha de rede e erro de
  servidor continuam no aviso genérico, onde a instrução de tentar de novo está
  correta. A tela **não** detecta ciclo por conta própria e **não** sugere qual
  vínculo desfazer — ela conhece o caminho, não a preferência do operador.
- **Diagnóstico de delegação que não conclui.** Quando a tool de delegação
  desiste — por timeout ou porque o alvo terminou em falha —, o registro passa a
  identificar a delegação inteira (task e agente de origem, agente e task do
  alvo) e a carregar o **último estado observado** da task do alvo. É esse
  estado que separa as duas causas que antes produziam a linha idêntica:
  `Submitted` significa task publicada e **nunca consumida** — não havia
  instância livre —, e `Working` significa consumida e ainda em execução. O campo
  se chama "último observado" porque é o que ele é: entre a última leitura e a
  desistência cabe um intervalo de poll. Quando não houve leitura nenhuma, o
  registro diz isso em vez de apresentar um estado.
- **Varredura periódica de tasks que não alcançaram estado terminal**, em
  `apps/workers`, reportando a contagem **por estado** de tasks mais velhas que
  uma janela. A janela não é um valor novo: é o timeout de espera da delegação,
  lido em tempo de execução, então toda task reportada já sobreviveu a uma espera
  inteira. O registro descreve o que foi observado — estado, idade e janela — e
  **não** afirma que a task está travada, porque um turno com várias delegações
  ultrapassa a janela legitimamente e o sistema não distingue os dois casos.
  **Como ler a série**, e sem isto ela é lida errado: a leitura é global, então
  cada instância emite uma linha **idêntica** por ciclo e somá-las superestima
  pelo número de instâncias; e quem responde "quantas conversas delegavam ao
  mesmo tempo" é a contagem de `Submitted`, nunca a de `Working` — esta última
  subestima por construção, porque a janela é exatamente o ponto em que a origem
  desiste, e mostra perto de zero justamente quando há disputa.

**Caixas de entrada e canais**

- `apps/inbox` como quarto app, com banco próprio (`buteco_inbox`) isolado.
- Catálogo de canais de entrada, com `ChannelType` como string aberta
  validada contra os adapters efetivamente registrados.
- CRM próprio de `Contact` e `Session`, com fronteira de sessão por
  inatividade automática.
- Orquestrador de mensagens com debounce persistido, idempotente entre
  instâncias, e round-trip A2A completo com `apps/api`.
- Contrato de plugin de canal: três contratos obrigatórios por `ChannelType`
  mais um opcional de provisionamento de webhook.
- Adapter **WAHA** (WhatsApp HTTP API), com configuração manual de webhook.
- Adapter **Telegram** (Bot API), com `setWebhook` automático e `secret_token`
  por canal.
- Histórico durável de mensagens por sessão, em tabela relacional própria,
  com status de despacho e de entrega.
- `GET /sessions/summary?from=…&to=…` — contagem de sessões **iniciadas** no
  período (`startedCount`), agregada no banco. Conta por instante de início,
  não por atividade nem por sessão aberta durante o intervalo; os dois limites
  são obrigatórios e inclusivos.
- `GET /messages/summary?from=…&to=…` — contagem de mensagens **recebidas** no
  período (`inboundCount`), agregada no banco. Conta **só as de entrada**: as
  respostas enviadas pelo agente ficam de fora, em qualquer estado de entrega.
  A unidade contada é a mensagem distinta, não a entrega de webhook — um evento
  reentregue pelo provedor conta uma vez. Os dois limites são obrigatórios e
  inclusivos.
- Painel de catálogo de canais, e de sessões e histórico de conversa.

**Autenticação**

- Token stateless assinado com HMAC, sem biblioteca JWT e sem sessão em
  banco.
- Login de operador único em `POST /auth/login`, com credencial via variável
  de ambiente e hash PBKDF2.
- Token de serviço escopado para as chamadas de `apps/inbox` a `apps/api`.
- Enforcement por padrão em toda rota HTTP, com allowlist explícita de rotas
  anônimas validada no startup.
- Tela de login e módulo de token no painel.

**Contexto temporal e de canal**

- Bloco de contexto temporal concatenado às instruções do agente: instante de
  processamento, instante da mensagem e regra de precedência para expressões
  de tempo relativas.
- Bloco de contexto de canal: tipo do canal e identificador do contato.
- Fuso horário do sistema em `apps/workers` via `TZ`, com checagem de
  integridade no startup que compara o fuso resolvido com o declarado.

**Bases de conhecimento**

- Catálogo de bases de conhecimento e de documentos em `apps/api`, com
  espelho em `apps/workers`.
- Extração de markdown com preservação de marcação, resolvida por
  `SourceType` via DI keyed com checagem bidirecional no startup.
- Primeira entidade do repositório com exclusão real, sob o critério
  "catálogo referenciado usa `IsActive`; conteúdo sem referência usa exclusão
  real".
- Pipeline de indexação em `apps/workers`: fragmentação do conteúdo, geração de
  embedding e gravação dos vetores em `knowledge_fragments` (`pgvector`), por
  fila própria `knowledge-indexing`, com máquina de estados
  `Pending → Indexing → Indexed | Failed` e política de três tentativas.
- Reindexação de documento sob pedido do operador, por
  `POST /knowledge-bases/{id}/documents/{documentId}/reindex`. É a única entrada
  que reenfileira sem o conteúdo ter mudado — existe para o documento cujo
  conteúdo está correto e cuja indexação falhou por causa transitória. Abre uma
  rodada nova: limpa o motivo da falha e os contadores de tentativa, e preserva
  o conteúdo indexado anterior, que continua respondendo.
- **Consulta ao índice em tempo de execução**: cada base de conhecimento
  vinculada a um agente **e ativa** vira uma tool que o agente pode chamar. A
  descrição da tool é montada: um prefixo que nomeia a base, a descrição
  cadastrada, e um bloco fixo — igual para toda base — que ensina a ler o
  resultado. A parte cadastrada é o critério de escolha: é por ela que o modelo
  decide se aquela base é relevante. A busca é por proximidade vetorial dentro
  daquela base, devolve os cinco trechos mais próximos e traz **a distância de
  cada um**, sem nenhum limiar: a tool não filtra por relevância e não afirma
  que algum trecho responde à pergunta, porque o sistema não sabe isso — quem
  decide é o agente, lendo o trecho. Base vinculada **sem conteúdo indexado** é
  relatada como tal, e nunca como "a busca não encontrou nada": não havia onde
  procurar. Com isso, o conjunto de tools entregue ao LLM passa a ser a união de
  **três** conjuntos (MCP, delegação e conhecimento), com precedência declarada
  nessa ordem na resolução de colisões de nome.

- Resumo de indexação agregado por base, por
  `GET /knowledge-bases/indexing-summary`, com contagem de documentos,
  indexados e em falha. Uma requisição para o conjunto inteiro, com custo
  independente do número de bases. A resposta de base **não** carrega essas
  contagens, de propósito.
- Proveniência do índice de conhecimento, por
  `GET /knowledge-index/diagnostics`: as combinações de provedor, modelo e
  dimensão de embedding **gravadas nos fragmentos**, cada uma com a contagem de
  fragmentos que a usa. A rota é **global**, fora do grupo de bases, porque a
  proveniência é propriedade do sistema e não da base — a dimensão é fixada pelo
  tipo da coluna e a checagem de boot exige combinação única no índice inteiro.
  Devolve o que está **gravado**, nunca o que a configuração declara, e índice
  vazio responde lista vazia em vez de insinuar o modelo que seria usado. Mais de
  uma combinação é resposta válida, não erro: é o estado em que `apps/workers` se
  recusa a subir, e é nele que o operador abre o painel — a contagem por
  combinação é o que torna a reindexação decidível.
- Gestão de documentos no painel, dentro do detalhe da base: listagem com o
  estado de indexação de cada documento, adição por arquivo
  (`.md`/`.markdown`/`.txt`, vários por vez, com título sugerido a partir do nome
  e editável) ou escrevendo à mão, atualização, exclusão com confirmação, e
  reindexação a partir da faixa de falha.
- A listagem de documentos **acompanha a transição** sozinha: enquanto houver
  documento pendente ou indexando, ela se refaz periodicamente e para quando
  todos chegam a um estado terminal. Sem barra de progresso percentual — o
  sistema conhece o estado do documento, não o percentual.
- O motivo da falha de indexação é exibido **completo, sem truncar**, com a ação
  de reindexar ao lado. É a única cópia da falha que a tela tem.
- A contagem de fragmentos é exibida quando o documento já foi indexado alguma
  vez e **omitida** quando nunca foi — nunca zerada. Documento que falhou depois
  de indexado, ou que está sendo reindexado, continua mostrando a contagem
  anterior, porque os fragmentos antigos continuam respondendo.
- Envio de vários arquivos é tratado como N operações independentes, com estado
  por linha: se uma falhar, as que já entraram continuam criadas, o modal
  permanece aberto com o motivo, e um novo envio não recria o que já entrou.
- O catálogo de bases passa a exibir, por linha, **a contagem de documentos** e
  **o resumo de indexação** — indexados, em andamento e em falha —, e ganha a
  quarta opção de filtro, `Com falha`. Tudo a partir de **uma** requisição para o
  conjunto das bases, e não uma por base: era esse custo que mantinha as colunas
  de fora.
- A coluna de indexação **não distingue documento pendente de documento em
  indexação**: o resumo agrega os dois, e a tela não afirma uma separação que o
  dado não carrega. Só a parcela de falha recebe tom de alerta — documento em
  andamento é o funcionamento normal do pipeline.
- Base **sem documento nenhum** é exibida como tal, porque o zero do resumo é uma
  contagem medida. Base para a qual o resumo não respondeu fica com o dado
  marcado como desconhecido, e **nunca** zerado. Se o resumo não carregar, a
  listagem continua servindo e a opção `Com falha` aparece desabilitada, em vez
  de filtrar para o vazio e afirmar que nenhuma base tem falha.
- O detalhe da base passa a ter **duas abas**, `Documentos` e
  `Diagnóstico do índice`, com a aba ativa no endereço. A barra nasce agora, e não
  antes: com uma aba só ela afirmaria uma estrutura que a tela não tinha.
- A aba de diagnóstico exibe a **proveniência gravada do índice** — provedor,
  modelo, dimensão e fragmentos de cada combinação —, lida de
  `GET /knowledge-index/diagnostics`. Ela é apresentada como propriedade **do
  sistema**, em grupo próprio e rotulado como tal, separada do **volume desta
  base**, que é derivado da listagem de documentos que a tela já carrega — sem
  requisição adicional.
- Índice vazio é **explicado**, não preenchido: a tela diz que provedor, modelo e
  dimensão passam a existir quando o primeiro documento terminar de indexar, em
  qualquer base, e não insinua qual modelo seria usado. O gate é a vacuidade do
  índice **inteiro**, nunca a contagem da base — a proveniência é global, e negá-la
  numa base sem documento esconderia um fato que o sistema conhece.
- **Mais de uma combinação é nomeada como corrupção**, com a contagem de
  fragmentos de cada uma, que é o número que torna a reindexação decidível. A tela
  não elege nenhuma como a correta: `apps/api` não conhece a configuração
  declarada de embedding.
- Falha ao ler a proveniência é dita como falha, e **nunca** como índice vazio —
  uma requisição que não respondeu não é evidência de ausência.
- A contagem de fragmentos da base soma os documentos cujo `indexedAt` não é nulo,
  **qualquer que seja o estado**: documento que indexou e falhou ao reindexar
  continua com os fragmentos anteriores respondendo, e somar só os indexados
  informaria menos fragmentos do que o índice tem.
- A aba **não repete** a lista de documentos em falha: informa quantos são e leva à
  aba de documentos, onde o motivo completo e a ação de reindexar já vivem.
- A orientação da descrição no formulário de base passa a pedir **delimitação** —
  dizer também do que a base **não** trata — e a informar que, quando o agente tem
  mais de uma base, ele escolhe comparando as descrições, e a mais genérica atrai
  as perguntas que eram das outras. Não há aviso automático de generalidade:
  nenhuma regra do sistema distingue descrição específica de genérica, e a tela
  não afirma critério que o sistema não tem. O aviso de descrição curta continua,
  agora dito como **piso** de campo mal preenchido, e não como aferição de
  qualidade — comprimento não é a dimensão que decide roteamento entre bases.
- O preview do formulário e a seção de descrição do detalhe param de afirmar que
  a descrição cadastrada **é** a descrição da ferramenta: ela é a parte que o
  operador escreve dentro de um texto que o sistema monta. As duas telas passam a
  dizer que o sistema envolve esse texto em instruções fixas, **sem reproduzi-las**
  — elas vivem em `apps/workers` e são iguais para toda base.
- O formulário continua **não exibindo nome de ferramenta**, e agora por três
  razões verificadas: nenhuma spec fixa o formato; o painel não tem fonte para
  calculá-lo sem reimplementar regra que vive em `apps/workers`; e o nome
  calculável seria o **pretendido**, não o efetivo, porque a deduplicação global
  renomeia a tool de conhecimento na execução.
- Vínculo N:N entre agente e base de conhecimento em `apps/api`, definido por
  `PUT /agents/{id}/knowledge-bases` com substituição integral do conjunto.
  Base inativa continua vinculável, e agente inativo continua configurável.
- As respostas de agente passam a incluir `knowledgeBases`, com id e nome de
  cada base vinculada, ordenados por nome e desempatados por identificador.

**Entrega containerizada**

- `Dockerfile` multi-stage por app, com build context na raiz do monorepo.
- Migration bundle como serviço one-shot, executado antes de `apps/api` e
  `apps/inbox` subirem — nunca para `apps/workers`.
- `docker-compose.prod.yml` com Postgres e RabbitMQ próprios e nginx interno
  servindo o SPA e fazendo proxy.
- Runbook de deploy em [`docs/deployment.md`](docs/deployment.md).

**Painel — identidade visual e navegação**

- Tema próprio com paleta, tipografia, raios e sombras declarados, seguindo o
  esquema de cor do sistema operacional.
- Casca unificada com barra lateral, marca, navegação com ícones e controle
  de tema.
- Detalhe do agente em abas, com a aba ativa na URL e modelo de rascunho
  bloqueando navegação com pendência.
- Busca com normalização de acentos e filtro por estado nas listagens.
- Padrões visuais compartilhados: card seccionado, rótulo de seção e
  cabeçalho de detalhe.
- Marca do produto no painel: símbolo Robô-garçom na barra lateral e na tela
  de login, favicon e ícone de aplicativo próprios, e título de documento com
  o nome do produto — no lugar do quadrado com a letra "B", do favicon do
  scaffold e do título `frontend`. A marca é servida por um componente único,
  troca de cor junto com o esquema do painel e degrada para a peça legível
  quando o tamanho pedido fica abaixo do mínimo do manual.
- Tela de inventário dos catálogos em `/inventory`, que passa a ser a entrada
  do painel e ganha o quinto item da barra lateral: um item por catálogo —
  agentes, servidores MCP, bases de conhecimento e canais — com a contagem, o
  estado da consulta que a produziu e o atalho para a listagem correspondente.
  Cada item **consulta, falha e recarrega por conta própria**: um catálogo
  indisponível não impede os outros de exibir contagem, e oferece nova
  tentativa que refaz só a consulta dele. Nenhuma rota nova de backend — a
  contagem sai da mesma consulta que alimenta a listagem daquele catálogo, de
  modo que as duas telas não têm como divergir. Contagem apurada que deu zero
  é dita por extenso; consulta que não respondeu aparece como desconhecida,
  **com a razão** — nunca como zero, que afirmaria uma contagem que ninguém
  fez. O item não repete o texto explicativo da listagem: a explicação de cada
  catálogo continua tendo um lugar só.
- Atividade na tela de inventário: dois itens novos, **Sessões iniciadas** e
  **Mensagens recebidas**, cada um com a contagem dos **últimos 7 dias**,
  consumindo `GET /sessions/summary` e `GET /messages/summary`. A janela é
  rolante (as 168h que terminam no instante da consulta) e é apresentada **em
  todos os estados do item**, inclusive carregando e sem resposta: um número de
  atividade sem período é ambíguo, e um "não sei" sem período não diz sobre o
  quê. Os dois itens herdam o que os de catálogo já tinham (zero dito por
  extenso, falha com razão, nova tentativa que refaz só a própria consulta), mas
  **não têm atalho**, porque nenhuma listagem do painel conta o mesmo conjunto. A
  contagem é a que a rota agregada devolve, nunca recontada no painel. Sem
  seletor de período.

**Documentação e governança**

- Licenciamento sob Apache-2.0, com `LICENSE` e `NOTICE`.
- `README.md` como porta de entrada, e documentação técnica em
  [`docs/`](docs/README.md).
- `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md` e `SECURITY.md`.
- Script de verificação de integridade da documentação em
  `scripts/check-docs.py`.

### Changed

- Enums de `Message` passaram a atravessar a API como **string**, nunca como
  inteiro ordinal — formato de fio agora faz parte do contrato entre
  `apps/inbox` e `apps/frontend`.
- Roteamento do painel migrado para `createBrowserRouter` com
  `RouterProvider`, sem mudança visível, para viabilizar a interceptação de
  navegação.
- A página separada de vínculo MCP deixou de existir, absorvida como aba do
  detalhe do agente; a rota antiga sobrevive como redirect.
- **A rota raiz do painel passou a levar ao inventário**, e não mais à
  listagem de agentes — muda a tela de entrada de todo operador. Junto, o
  login passou a navegar para a raiz em vez de `/agents`, para que o destino
  da entrada tenha uma definição só, na árvore de rotas. A segunda troca não é
  cosmética: a rota tentada não é preservada no redirect para o login e toda
  resposta `401` força aquela tela, então o login é o caminho de entrada
  dominante — sem ela, a maior parte das entradas continuaria caindo na
  listagem de agentes.
- A grade da tela de inventário passou a ter **no máximo quatro colunas**. Com
  seis itens, a grade sem teto quebrava em 5 + 1 justamente na faixa de 1600 a
  1871px de janela; com o teto, fica em 4 + 2 (catálogos em cima, atividade
  embaixo). A regra vale para os seis itens, inclusive os quatro de catálogo. Os
  pontos de quebra abaixo disso não mudaram: 1328, 1056 e 784px.
- Explicação de falha no painel passou a usar `failureReason` em vez da
  mensagem crua da API.
- Imagens Docker passaram a usar a variante default de
  `mcr.microsoft.com/dotnet/*` em vez de `-alpine`, que não traz tz database
  nem ICU.
- Teste de conexão de servidor MCP no formulário passou a escolher o endpoint
  pelo estado da credencial — credencial em branco na edição significa
  "manter a atual".

### Fixed

- **Documento grande não indexava: a chamada de embedding ia inteira, sem
  teto**. `apps/workers` mandava **todos** os fragmentos do documento numa
  chamada só ao gerador de embedding. Medido no piloto de 20/09/2026 (gateway
  do `.env.prod`, modelo de 4.096 dimensões): um documento de ~442 fragmentos
  falhou com `502 upstream_error` nas **três** tentativas — inclusive isolado,
  sem outra indexação concorrendo —, enquanto as duas metades dele (267 e 175
  fragmentos) indexaram normalmente. A chamada passa a ser **loteada, em lotes
  sequenciais**, com o tamanho em `Embedding__BatchSize` (nova variável de
  ambiente `EMBEDDING_BATCH_SIZE`, **opcional**, padrão **250**). A persistência
  não mudou: os fragmentos continuam sendo substituídos integralmente numa
  transação única, e a retentativa continua sendo do documento inteiro, três
  execuções — uma execução continua sendo uma tentativa, que é o que faz a tela
  de documentos poder dizer "três tentativas" sem mentir.
  **O padrão 250 é provisório e está escrito como tal**: 267 fragmentos passaram
  e 442 falharam, e o *formato* do teto do gateway (por número de entradas, por
  bytes do corpo, ou por tempo de resposta) **não foi estabelecido**. Se a
  indexação de um documento grande falhar com `502`, baixe
  `EMBEDDING_BATCH_SIZE` e reindexe — variá-lo é como o teto é descoberto.
  Valor menor ou igual a zero **reprova o boot** de `apps/workers`, com o valor
  encontrado na mensagem, e **não** é corrigido em silêncio: um valor trocado
  automaticamente invalidaria a medição que o parâmetro existe para permitir.

- **Um ciclo de delegação `A→B→…→A` travava a conversa, e o cadastro o
  aceitava**: a task do agente de origem segura o lock que serializa a conversa
  (`pg_advisory_lock`) enquanto espera o alvo, e a task do **mesmo** agente mais
  adiante na cadeia bloqueia nesse mesmo lock. Medido com quatro instâncias de
  `apps/workers`: **acrescentar réplica não resolve**, porque o recurso disputado
  é o lock e não o consumidor — e o teto de profundidade da cadeia não cobre,
  porque a checagem roda antes da aquisição do lock e um ciclo de dois saltos
  trava bem abaixo do teto. `apps/api` recusava só a auto-delegação de um salto;
  o resto era requisito explícito de que ciclo era permitido.
  `PUT /agents/{id}/delegations` passa a responder **400** para qualquer conjunto
  que feche ciclo de qualquer comprimento, com o **caminho nomeado** na mensagem
  — porque o vínculo a desfazer pode estar em outro agente. A avaliação considera
  o grafo **como ele ficaria depois da substituição**, então desfazer um ciclo já
  cadastrado continua sendo aceito, e caminhos múltiplos sem ciclo continuam
  permitidos. A correção é só de cadastro: `apps/workers` não mudou, porque o
  cadastro é o único ponto que escreve os vínculos.
- **Uma task podia ficar presa em `working` para sempre, e a mensagem do
  usuário sumia sem erro nenhum**: `apps/workers` adquiria o lock que serializa
  a conversa (`pg_advisory_lock`) **fora** do `try` que trata falhas. O
  `pg_advisory_lock` não tem timeout, mas o `CommandTimeout` do Npgsql (30 s)
  tem — ao estourar, a exceção escapava do método inteiro, a mensagem do
  RabbitMQ era descartada e a task nunca alcançava estado terminal. Em cadeia, a
  `PendingDispatch` de `apps/inbox` ficava em `Dispatching` para sempre e o
  contato nunca recebia resposta. O caminho é o de um contato que manda a
  segunda mensagem enquanto o agente ainda responde a primeira, e **só existe a
  partir de duas instâncias** de `apps/workers` — que é o que a delegação entre
  agentes exige. A falha na aquisição passa a terminar a task em `failed`, o que
  dispara a push notification e resolve a `PendingDispatch` pelo caminho que já
  existia. Falhas que antes eram silenciosas passam a ser visíveis; elas não são
  novas.
- **Cada falha ao adquirir esse lock pendurava uma conexão Postgres**: o escopo
  de serviço criado para o lock não era descartado quando a aquisição falhava, e
  a conexão já aberta nunca voltava ao pool. Enquanto a falha era exceção não
  tratada isso era raro; ao virar caminho de operação, passaria a ser um
  vazamento por ocorrência, e o pool esgotado deixaria o worker sem conseguir
  nem gravar as próprias falhas.
- **A contagem de mensagens recebidas da tela de entrada nunca chegava ao
  `apps/inbox`**: o prefixo `messages`, que ganhou rota de nível superior com
  `GET /messages/summary`, não foi acrescentado ao nginx do stack de servidor. A
  chamada caía no fallback de SPA e respondia `200` com HTML, para todo mundo,
  em todo carregamento. É a quarta vez que um prefixo servido fica fora da lista
  do nginx. A nota de conferência do arquivo passa a listar também os quatro
  falsos positivos que os greps devolvem.
- **O browser podia servir o shell do SPA no lugar da resposta da API**: o nginx
  devolve, para a mesma URL (ex. `/agents`), o shell numa navegação e JSON num
  `fetch()`. O `index.html` saía sem `Cache-Control`, e o browser o guardava por
  heurística. Depois de um refresh em `/agents`, a lista de agentes deixava de
  carregar, com a requisição servida do cache de disco sem chegar ao servidor.
  O defeito só aparecia com o deploy já envelhecido. O shell passa a sair com
  `Cache-Control: no-store`, e os assets com hash de conteúdo continuam
  cacheáveis.
- **Três prefixos de API não eram roteados pelo nginx do stack de servidor, e
  caíam no fallback de SPA**: `internal`, `knowledge-bases` e `knowledge-index`
  respondiam `200` com HTML onde o consumidor esperava JSON — a resposta errada
  com o status certo. O mais caro era `internal`: `apps/workers` entrega a push
  notification em `/internal/push-notifications`, então a task completava, o
  agente respondia, e **nada saía no canal**, sem erro em lugar nenhum.
  `knowledge-bases` e `knowledge-index` quebravam o painel de bases e a aba de
  diagnóstico do índice. A lista de prefixos do nginx passa a ser derivada dos
  prefixos que `apps/api` e `apps/inbox` de fato servem, com o comando de
  conferência escrito no próprio arquivo, em vez de mantida de memória — foi por
  enumerar, e não por lembrar, que o terceiro apareceu.
- **O compose de produção não entregava a seção `Embedding` a `apps/workers`**:
  o boot passava, porque a checagem de consistência do índice não diverge de um
  índice vazio, e a **primeira indexação** falhava com modelo vazio e dimensão 0
  — erro longe da causa. `Embedding__Provider`, `__Model` e `__Dimensions`
  passam a ser interpoladas, e as duas últimas são obrigatórias.
- **`ANTHROPIC_API_KEY` e `GEMINI_API_KEY` não chegavam a processo nenhum no
  stack de servidor**: `.env.prod.example` as oferecia e `docs/configuration.md`
  prometia o mapeamento, mas nenhum serviço do compose as consumia — só OpenAI
  funcionava. Passam a ser interpoladas em `apps/api` e `apps/workers`.
- **Variável de ambiente ausente produzia configuração insegura em silêncio**:
  sem `--env-file .env.prod`, o Compose lia `.env` ou nada, e todo `${VAR}`
  virava string vazia — o Postgres subia **sem senha configurada**. As variáveis
  cuja ausência tem esse efeito passam a ser obrigatórias na interpolação, com
  mensagem que diz o que fazer, e o Compose falha ao processar o arquivo em vez
  de criar serviço nenhum.
- **Credencial funcional de serviço externo commitada em arquivo versionado**:
  `apps/workers/.../appsettings.Development.json` trazia endpoint de gateway
  interno e chave de API, entregues a todo clone do repositório. A chave foi
  rotacionada e os campos passam a `changeme`. O worker agora exige
  `OpenAI__BaseUrl`/`OpenAI__ApiKey` no ambiente para o ambiente híbrido de
  desenvolvimento, e `docs/development.md` registra que sem elas toda task
  termina `failed`.
- **`.env.example` publicava Postgres e RabbitMQ em portas que os
  `appsettings.Development.json` versionados não usavam**: copiar o exemplo como
  a documentação manda fazia os três apps falharem a conexão, com sintoma de
  banco fora do ar. O exemplo passa a trazer `15532`/`15772`/`15872`, que é o
  que os `appsettings` esperam.

- **Documentação do custo da busca vetorial citava um volume de disco como se
  fosse orçamento de latência**: `docs/architecture.md` registrava 7.500
  fragmentos como referência de armazenamento, e o gatilho do índice ANN falava
  em "p95 acima de 200 ms" sem volume nenhum — dois números sobre a mesma
  grandeza aparente, respondendo a perguntas diferentes. Medido agora, os mesmos
  7.500 fragmentos **numa única base** custam ~333 ms de busca exata, já acima do
  teto. A documentação passa a separar os dois: 7.500 é volume de **disco**; o
  volume de **latência** é ~4.300 fragmentos **na maior base** — e é a maior base
  que conta, porque a consulta filtra por `KnowledgeBaseId`. Junto, fica
  registrado que a primeira consulta após ociosidade custa ~265 ms mesmo com
  poucos fragmentos, por leitura de TOAST.

- **Vazamento de pool de conexões HTTP por mensagem em `apps/workers`**: o
  `IChatClient` era construído a cada mensagem e nunca descartado, e os SDKs de
  Gemini e de Anthropic instanciam um `HttpClient` próprio por client — então
  cada mensagem processada deixava para trás um pool de conexões inteiro.
  Medido: **~44 descritores de arquivo por mensagem, sem retorno**, em duas
  instâncias (322→366 e 323→367). O processo degradava com o tempo de execução
  e parava de responder depois de horas, com as chamadas estourando em 100 s
  esperando **conexão do pool**, não resposta do servidor. O SDK da OpenAI usa
  um `HttpClient` estático compartilhado e por isso não vazava — foi essa
  assimetria que manteve o defeito invisível, já que a indexação de bases de
  conhecimento só usa OpenAI e rodava sem sintoma enquanto o chat degradava. O
  client passa a ser resolvido uma vez por par `(provider, model)` e reutilizado
  pelo resto da vida do processo, que é o que a documentação do próprio
  `IChatClient` sempre pediu. Junto, entra o instrumento que faltava: a
  **duração de cada requisição ao provedor de LLM** passa a ser registrada em
  log, medindo a requisição em si e não o turno inteiro do agente — o
  diagnóstico deste defeito custou caro exatamente por não existir essa
  separação. **Rotação de chave de provedor exige reiniciar o processo**, agora
  registrado em `docs/configuration.md`.

- **Colisão silenciosa de nome de tool no conjunto entregue ao LLM**: as tools
  MCP e as de delegação eram concatenadas sem que nenhum dos dois lados
  soubesse que dividia espaço de nome com o outro, e o lado MCP não
  deduplicava nem contra si mesmo. Quando dois nomes coincidiam, o primeiro
  vencia sem erro e sem log — o operador cadastrava uma tool e o agente
  chamava outra, sem rastro. O caminho alcançável era entre servidores MCP:
  dois nomes que diferem só em pontuação sanitizam para a mesma cadeia, e dois
  nomes longos com o mesmo prefixo colidem na truncagem de 64 caracteres.
  Colisões passam a ser resolvidas renomeando (nunca descartando), com
  precedência declarada, aviso no log e conjunto de nomes estável entre
  execuções.
- **Sufixo de dedupe ultrapassando o limite de 64 caracteres** na resolução de
  tools de delegação, quando a base já estava no limite.
- **Ordem não determinística na resolução de tools MCP**: a consulta de
  vínculos não tinha ordenação, então a ordem vinha do plano do Postgres e com
  ela mudava qual tool ganhava o nome-base num desempate.
- **Perda silenciosa de dados na edição de agente pelo painel**: o `PUT` era
  enviado sem `description` e `skills`, e o endpoint trata ausência como
  "limpar", de modo que toda edição pelo painel apagava o que havia sido
  cadastrado via API. Os campos passaram a ser obrigatórios no tipo de
  entrada, para o compilador impedir a regressão.
- **Perda de mensagem sob concorrência no orquestrador de debounce**, causada
  por aliasing de change tracker do EF Core após releitura na mesma instância
  de `DbContext`.
- **Ausência de índice único protegendo `Session` contra concorrência**:
  índice único parcial (`sessions."ContactId" WHERE "ClosedAt" IS NULL`)
  passou a garantir no banco no máximo uma sessão aberta por contato.
- **Divergência de encoding entre apps** em dois codecs que serializavam
  payload A2A sem as opções usadas pelo resto do pipeline, produzindo
  payloads estruturalmente diferentes com os campos corretos.
- **Exceção escapando da degradação graciosa** em três pontos da mesma
  família, em que o `try/catch` existia mas a chamada que mais realisticamente
  falha estava posicionada fora dele — em um `BackgroundService` de varredura,
  na recepção de push notification e na decifragem de credencial.
- **Nome de propriedade com sigla saindo errado no fio**: a política camelCase
  minúscula apenas a primeira letra, então `A2A` ia como `a2A` e o consumidor
  que lia `a2a` recebia campo ausente.
- **Instabilidade da suíte do frontend** causada pelo prazo padrão de um
  segundo das consultas assíncronas, curto para dropdowns em portal sob
  paralelismo.
- **Tons fixos de superfície quebrando em um dos esquemas de cor**, em três
  pontos do painel — fundo da página, faixa de cabeçalho e linha selecionada
  — por usar valor que não troca de ponta da escala entre temas.

[Unreleased]: https://github.com/aldovrando-oliveira/buteco-agentes/commits/main
