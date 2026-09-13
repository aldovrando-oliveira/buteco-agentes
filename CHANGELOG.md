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
  vinculada a um agente **e ativa** vira uma tool que o agente pode chamar, com
  a descrição cadastrada da base como descrição da tool — é por ela que o modelo
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
- A consulta do índice pelo agente ainda não existe: não há tool de busca.
- Vínculo N:N entre agente e base de conhecimento em `apps/api`, definido por
  `PUT /agents/{id}/knowledge-bases` com substituição integral do conjunto.
  Base inativa continua vinculável, e agente inativo continua configurável.
- As respostas de agente passam a incluir `knowledgeBases`, com id e nome de
  cada base vinculada, ordenados por nome e desempatados por identificador.
- O vínculo ainda não é oferecido ao agente em execução: não há tool nem
  resolvedor de conhecimento.

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
- Explicação de falha no painel passou a usar `failureReason` em vez da
  mensagem crua da API.
- Imagens Docker passaram a usar a variante default de
  `mcr.microsoft.com/dotnet/*` em vez de `-alpine`, que não traz tz database
  nem ICU.
- Teste de conexão de servidor MCP no formulário passou a escolher o endpoint
  pelo estado da credencial — credencial em branco na edição significa
  "manter a atual".

### Fixed

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
