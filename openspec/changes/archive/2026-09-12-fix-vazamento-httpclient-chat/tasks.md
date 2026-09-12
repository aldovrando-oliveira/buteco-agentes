Todas as tarefas de código rodam em **`apps/workers`**, com **uma exceção
nomeada**: a tarefa 6.5 edita um **comentário** em `apps/api` (a citação cruzada
que esta change torna falsa — C14). Zero linhas de comportamento, zero
`ProjectReference`, isolamento entre apps intacto. `apps/inbox` e `apps/frontend`
não são tocados.

## 1. Baseline (antes de qualquer edição)

- [x] 1.1 (`apps/workers`) Criar um `git worktree` limpo no `HEAD` atual e rodar
  a suíte completa de `apps/workers` lá, **guardando a saída inteira num arquivo
  fora do diretório de sessão** (convenção 19). Sem `| grep`, sem `| head`, sem
  `tee` para um pipe que feche: redirecionar tudo para o arquivo e só depois
  inspecionar. `DOCKER_HOST` precisa apontar para o Podman nesta máquina, ou a
  suíte de integração reprova inteira por motivo errado.
- [x] 1.2 (`apps/workers`) Registrar no arquivo de baseline o total de
  passa/reprova e, se houver reprova, o **nome** de cada teste — é o nome que
  separa "pré-existente" de "eu quebrei", e é ele que se perde quando a saída é
  filtrada antes de a rodada terminar.

## 2. Guardas primeiro, reprovando contra o defeito real (convenção 15)

Escritos **antes** da correção, para que a reprovação seja observada contra o
código atual, não relembrada depois.

- [x] 2.0 (`apps/workers`) **Conferir de novo, contra a árvore do momento da
  implementação, que nenhum teste afirma a propriedade antiga** ("construído por
  chamada, sem cache"). A conferência da proposta (C13) não achou nenhum —
  `Assert.Same`/`NotSame`/`ReferenceEquals` só aparece uma vez em
  `apps/workers/tests/`, sobre um `Task` sem relação —, mas a árvore pode ter
  mudado. **Se aparecer algum, ele é guarda invertido: reescrever para afirmar a
  propriedade nova, nunca deletar.** Guarda que reprova é o que a convenção 15
  existe para preservar, inclusive quando a reprovação está certa.

- [x] 2.1 (`apps/workers`) Em `tests/.../Agents/ChatClientResolverTests.cs`,
  acrescentar o guarda de **identidade**: resolver duas vezes o mesmo
  `(provider, model)` e afirmar `Assert.Same`. Rodar e **ver reprovar** contra o
  resolver atual.
- [x] 2.2 (`apps/workers`) No mesmo arquivo, o par da convenção 5: **models
  diferentes no mesmo provider** e **providers diferentes** recebem instâncias
  distintas (`Assert.NotSame`). Esses passam hoje — anotar isso, porque um guarda
  que já passa antes da correção não é evidência de nada sozinho; ele existe para
  impedir um cache chaveado só por `provider`.
- [x] 2.3 (`apps/workers`) No mesmo arquivo, o guarda de **falha não memorizada**
  (R3): resolver **duas vezes** um provider sem credencial e afirmar que as duas
  lançam `InvalidOperationException`. Passa hoje; existe para reprovar contra uma
  implementação futura com `Lazy<T>` ou `GetOrAdd` que memorize a exceção.
- [x] 2.4 (`apps/workers`) Em `tests/.../TaskJobConsumerTests.cs`, o guarda de R1:
  processar **duas** tasks em sequência contra o mesmo host e afirmar que a
  segunda chega a estado terminal. Passa hoje; existe para reprovar no dia em que
  alguém acrescentar `using` sobre o agente ou a cadeia e descartar o client
  compartilhado.
- [x] 2.5 (`apps/workers`) Registrar, ao lado de cada guarda de 2.2-2.4, que ele
  **não** reprova contra o defeito atual e **qual** defeito futuro ele fecha —
  senão a próxima leitura os toma por verificados pela convenção 15 quando só
  2.1 foi.

## 3. Cache do `IChatClient` (Decisões 1, 2 e 3)

- [x] 3.1 (`apps/workers`) Em `src/.../Agents/ChatClientResolver.cs`, acrescentar
  o dicionário por `(provider, model)` com leitura livre e **escrita sob lock**,
  com dupla checagem dentro do lock.
- [x] 3.1b (`apps/workers`) **No comentário do código, junto do lock**, escrever
  por que `Lazy<T>` e `GetOrAdd` foram recusados — não só no `design.md`, que é
  arquivado. As duas são as escolhas óbvias para "cache concorrente em C#", as
  duas parecem mais simples que um lock explícito, e é exatamente isso que alguém
  "simplificaria" numa revisão. O comentário nomeia o **efeito**, não a
  preferência: `Lazy<T>` memoriza a exceção e transformaria credencial ausente em
  falha permanente do processo (`TaskJobConsumerTests.Consumer_WhenAgentProviderNotConfiguredInWorkerEnvironment_TaskEndsFailed`
  exercita esse caminho com o resolver real); `GetOrAdd` pode executar a fábrica
  duas vezes e descartar o perdedor, que aqui é um pool de conexões — o próprio
  defeito desta change, reintroduzido em escala menor.
- [x] 3.2 (`apps/workers`) Escrever **junto da declaração da chave** o motivo de
  ela bastar: a credencial é por processo, não por agente (`Agent` não tem campo
  de credencial; as três `Options` vêm de `Program.cs:18-20`). E a consequência:
  se credencial por agente existir um dia, a chave muda.
- [x] 3.3 (`apps/workers`) Escrever, no mesmo lugar, que o dicionário **não
  desaloja de propósito** (Decisão 3) e que qualquer evicção futura obriga a
  descartar o item evictado — senão o vazamento volta mais devagar, que é pior de
  diagnosticar.
- [x] 3.4 (`apps/workers`) Rodar 2.1 e **ver passar**. Rodar 2.3 de novo e ver
  passar — é ele que prova que a construção que lança não gravou entrada.

## 4. Log de duração da chamada ao LLM (Decisão 4)

- [x] 4.1 (`apps/workers`) Criar
  `src/.../Agents/LlmCallDurationChatClient.cs`: um `DelegatingChatClient` que
  cronometra `GetResponseAsync` e `GetStreamingResponseAsync` e registra duração
  com `provider` e `model`.
- [x] 4.2 (`apps/workers`) Registrar a duração **também no caminho de falha,
  antes de a exceção propagar** — a chamada que estoura por espera de pool é
  exatamente o caso que não pode ficar sem medida. Usar `try/finally`, não só o
  caminho feliz.
- [x] 4.3 (`apps/workers`) Compor o wrapper **dentro do resolver, uma vez por
  entrada do cache**, envolvendo o client do SDK. Não por mensagem: o wrapper é
  um `DelegatingChatClient`, e `Dispose()` dele descartaria o client cacheado em
  cascata (C9).
- [x] 4.4 (`apps/workers`) Criar
  `tests/.../Agents/LlmCallDurationChatClientTests.cs` com `IChatClient` falso
  próprio: (a) sucesso registra duração; (b) falha registra duração **e** a
  exceção propaga; (c) `provider`/`model` aparecem na linha registrada;
  (d) o wrapper delega a resposta do inner sem alterá-la.
- [x] 4.5 (`apps/workers`) Confirmar por teste ou por leitura do código que o
  wrapper fica **na camada mais interna** — `ChatClientAgent` empilha
  `FunctionInvokingChatClient` por fora (C10), então a medida é da requisição ao
  provedor e **não** inclui execução de tools. Se a confirmação for por leitura,
  escrever isso no XML doc da classe, com o nome da extensão
  (`WithDefaultAgentMiddleware`).

## 5. Reintrodução do defeito (segunda metade da convenção 15)

- [x] 5.1 (`apps/workers`) Desfazer o cache de propósito (voltar a construir por
  chamada) e rodar a suíte inteira de `apps/workers`, guardando a saída completa
  em arquivo.
- [x] 5.2 (`apps/workers`) Verificar que **2.1 reprova** e que **nenhum teste de
  execução de agente reprova junto** — eles afirmam outra coisa, e quase todos
  usam `Mock<IChatClientResolver>`, cegos a isto por construção. Se algum
  reprovar, o guarda está no componente errado (R5) e precisa mudar de lugar, não
  de asserção.
- [x] 5.3 (`apps/workers`) Restaurar o cache e confirmar que a suíte volta ao
  estado da baseline.

## 6. Afirmações falsas no código (Decisão 6)

Cada uma é uma frase que esta change torna mentira, e `scripts/check-docs.py` não
pega frase que virou mentira.

- [x] 6.1 (`apps/workers`) `src/.../Agents/IChatClientResolver.cs:6-9` — trocar
  *"construído por chamada (sem cache entre execuções…)"* pela descrição real, e
  apontar para a Decisão 7 arquivada como o **gatilho que esta change disparou**,
  não como decisão contrariada.
- [x] 6.2 (`apps/workers`) `src/.../Agents/ChatClientResolver.cs:12-20` — a
  docstring diz "Fábrica de `IChatClient` por provedor"; passa a descrever o
  cache, a chave e o motivo da chave.
- [x] 6.3 (`apps/workers`) `src/.../Program.cs:73-75` — o comentário afirma que
  ser singleton "não implica reaproveitar nenhuma instância de `IChatClient`".
  É a correção mais importante das três: a frase deixa de ser falsa **e** o
  raciocínio sobre DI que ela carrega deixa de ser inválido.
- [x] 6.4 (`apps/workers`) `src/.../Agents/AgentExecutionService.cs:171` —
  acrescentar comentário dizendo que a instância é **compartilhada e não deve ser
  descartada**, apontando para o `await using` do `toolSet` em `:178` como o
  contraste deliberado (é a assimetria no mesmo bloco que hoje sugere a simetria
  errada). Nenhuma mudança de comportamento neste arquivo.
- [x] 6.5 (**`apps/api`** — a exceção nomeada no topo deste arquivo)
  `src/Buteco.Api/McpServers/Connectivity/IMcpConnectionTester.cs:5-11` — a
  docstring cita o resolver de `apps/workers` como precedente: *"mesmo espírito
  de `IChatClientResolver` em apps/workers — construído por chamada, sem cache
  entre chamadas"*. **A correção é remover a citação cruzada, não atualizá-la**:
  trocar "sem cache" por "com cache" deixaria em `apps/api` uma afirmação sobre o
  ciclo de vida de um componente de `apps/workers`, que é precisamente a forma que
  decaiu e vai decair de novo. A metade verdadeira (*"Interface existe para
  permitir substituição em teste"*) é sobre o próprio `IMcpConnectionTester` e
  fica. Conferir que a edição não toca nenhuma linha de código nem o `.csproj`.
- [x] 6.6 **Não editar** `openspec/changes/archive/2026-08-01-backend-multi-provedor-llm/design.md`.
  Change arquivada é registro histórico; o elo fica no código e no `design.md`
  desta change.

## 7. Documentação — um artefato por tarefa

A 2ª etapa desta linha de trabalho deixou três afirmações **falsas** no `README`,
em `docs/architecture.md` e no `CHANGELOG` por não ter uma tarefa por artefato.
"Conferido, nada a mudar" é resultado válido e deve ser registrado como tal.

- [x] 7.1 `README.md` — conferir. A conferência da proposta não achou afirmação
  sobre construção de client (linhas 63-64 e 76-79 falam de provedores e do papel
  de cada app, não de ciclo de vida). Registrar "conferido, sem mudança" se
  confirmar-se, em vez de deixar a dúvida aberta.
- [x] 7.2 `docs/architecture.md` — conferir. A ocorrência de "sem cache" em
  `:375` é sobre o **AgentCard**, não sobre o `IChatClient`; não confundir as
  duas. Avaliar se a descrição de `apps/workers` (`:71`) merece a linha sobre
  reuso de client por `(provider, model)`.
- [x] 7.3 `docs/configuration.md` — **este é o que ganha conteúdo novo**:
  registrar explicitamente que **rotação de chave de provedor exige reinício do
  processo**, com o motivo (`IOptions<T>` resolve uma vez; as credenciais chegam
  por variável de ambiente, que processo em execução não vê mudar) e com o
  **gatilho**: se `IOptionsMonitor<T>` entrar para qualquer uma das três
  `Options` de provedor, o cache passa a precisar de invalidação na mudança, com
  descarte do client removido.
- [x] 7.4 `CHANGELOG.md` — entrada em `[Unreleased]`, na seção correta (é
  correção de defeito, não `Added`), nomeando o vazamento e a medida (~44
  descritores por mensagem, sem retorno) em vez de "melhoria de performance".

## 8. Fechamento

- [x] 8.1 (`apps/workers`) Rodar a suíte completa de `apps/workers`, **guardando
  a saída inteira em arquivo**, com as mesmas regras da tarefa 1.1: sem filtrar
  antes de a rodada terminar, sem `| head`, e guardando o nome de qualquer teste
  que reprove. Comparar com o arquivo de baseline de 1.1/1.2.

  **Saídas completas guardadas em `~/.cache/buteco-agents/fix-vazamento-httpclient-chat/`**
  (fora do diretório de sessão, que é efêmero): `baseline-workers.txt` (212/212),
  `defeito-reintroduzido.txt` (1 reprovação em 223, só o guarda de identidade),
  `fechamento-workers.txt` (223/223), mais `baseline-RESUMO.txt` e as rodadas
  filtradas dos guardas. Nenhuma foi canalizada antes de a rodada terminar.
- [ ] 8.2 **NÃO EXECUTADA — pendente.** (`apps/workers`) Conferir descritores de
  arquivo na verificação manual:
  contar antes e depois de processar uma sequência de mensagens, com um agente
  **Gemini ou Anthropic**. Com OpenAI a medição não mostra nada nem antes nem
  depois (C4) — rodá-la só com OpenAI produziria um "corrigido" vazio.
  **Motivo de não ter sido feita:** exige credencial real de Gemini ou Anthropic
  e um worker rodando contra o provedor, que esta sessão não tem. A correção
  está provada por teste (guarda de identidade reprovando contra o defeito real,
  no componente certo — 1 reprovação em 223, nenhum teste de execução de agente
  junto) e por decompilação dos três SDKs, mas o **fechamento do ciclo contra a
  medida que originou a change continua em aberto**. Registrado no `02` com
  gatilho.
- [x] 8.3 Rodar `format:check`. **Achado ao executar, corrigindo o enunciado
  desta própria tarefa:** `format:check` é script npm de **`apps/frontend`**
  (prettier), não uma verificação do backend — as quatro reprovações anteriores
  são de frontend. Esta change **não toca nenhum arquivo de frontend**, então não
  pode acrescentar reprovação nova ali, e as quatro seguem intocadas. Para o
  backend não há `dotnet format` no fluxo documentado; o que se verificou foi a
  compilação e os avisos: fechamento com os **mesmos** avisos da baseline
  (2x `NU1903`, pré-existente), nenhum novo. `apps/api` compila com 0 erros.
- [x] 8.4 Conferir que `design.md` reflete a implementação real (convenção 9): se
  alguma decisão mudou por achado durante a implementação, corrigir o `design.md`,
  não só anotar no resumo.
- [x] 8.5 Registrar em `02-HISTORICO_E_STATUS.md`, **como itens de exploração
  própria e não como parte desta change**, os dois achados de fora de escopo:
  (a) a contradição de instâncias de `apps/workers` — `docker-compose.prod.yml`
  proíbe `replicas > 1` com justificativa **factualmente errada** ("lock não
  distribuído": `pg_advisory_lock` e `xmin` são distribuídos por construção),
  enquanto `AgentDelegationConcurrencyTests` usa **duas** instâncias para provar
  que a delegação funciona, e `WaitForTerminalStateAsync` de fato bloqueia o
  worker aguardando a task alvo; incluir o ponto **novo de largura** (N conversas
  concorrentes delegando travam N workers, independentemente da profundidade —
  o motivo escrito do `DelegationDepthLimit = 5` em
  `AgentExecutionService.cs:63-69` é só **profundidade**); (b)
  `docker-compose.prod.yml` não passa `Anthropic__ApiKey` nem `Gemini__ApiKey` a
  processo nenhum, contra `docs/configuration.md:213-214`. Cada um com gatilho.
- [x] 8.6 Registrar em `02-HISTORICO_E_STATUS.md`, **junto desta change**, que
  esta é a **primeira vez nesta jornada que um gatilho antigo é acionado
  exatamente como previsto**: a Decision 7 de `backend-multi-provedor-llm`
  escreveu *"simples de trocar por cache depois, se perfilamento mostrar
  necessidade"*, e o perfilamento mostrou. É evidência de que **gatilho escrito
  funciona quando é específico** — ele nomeia a condição (perfilamento), o que
  fazer (cache) e onde (o resolver), e por isso foi reconhecível quando a
  condição aconteceu. **Contrastar com o gatilho do carve de ordenação**, que
  apontava para uma tela que ninguém planejava: gatilho que depende de trabalho
  futuro não agendado não é gatilho, é intenção.
- [x] 8.7 Registrar no `02`, no mesmo item, a outra metade do achado: **a
  propriedade antiga nunca teve guarda**, e o arranjo de teste padrão da casa a
  **contradizia** — todo `Mock<IChatClientResolver>` devolve uma instância fixa
  em toda chamada, nos oito sítios de `apps/workers/tests/` e no round-trip de
  `tests/`. Decisão registrada só em docstring, testes exercitando a semântica
  oposta, e o custo (um pool de conexões por mensagem) atravessando desde
  `backend-multi-provedor-llm` sem ser notado. **Gatilho:** ao registrar um
  Non-Goal que é uma escolha de ciclo de vida ou de recurso (não uma ausência de
  funcionalidade), perguntar qual teste o afirma — se nenhum, ele é uma intenção
  em prosa, não uma propriedade do sistema.

## 9. Antes do archive

- [x] 9.1 Escrever o `Purpose` real de `a2a-task-lifecycle` **nesta passada**,
  antes do archive, em vez de deixar o passo `4d` do skill escrever o placeholder
  `TBD` sozinho. O estoque de `Purpose` placeholder está em 37 e não aumenta por
  causa desta change.
