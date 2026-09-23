## ADDED Requirements

### Requirement: Linha por chamada ao gateway de embedding, com finalidade
`apps/workers` SHALL gravar em `embedding_calls` uma linha por chamada ao gerador
de embedding, com provedor, modelo e **dimensão declarada** em snapshot, a
duração da chamada, o número de entradas enviadas, os tokens reportados, se a
chamada falhou, e `Purpose` igual a `Indexing` para as chamadas da indexação de
documento e `Search` para as da busca em base de conhecimento.

A linha SHALL ser gravada tanto para a chamada que devolveu vetores quanto para a
que lançou.

#### Scenario: Indexação de documento produz linha por chamada
- **WHEN** um documento é indexado com sucesso
- **THEN** existe ao menos uma linha em `embedding_calls` com `Purpose = Indexing`, o `Provider`, o `Model` e a `Dimensions` configurados, `Failed = false` e `DurationMs > 0`

#### Scenario: Busca em base de conhecimento produz linha
- **WHEN** um agente em execução invoca a tool de busca de uma base de conhecimento
- **THEN** existe uma linha em `embedding_calls` com `Purpose = Search` e `InputCount = 1`

### Requirement: O grão da linha de indexação é o lote, não o documento
Uma indexação que gera embedding em mais de um lote SHALL produzir **mais de
uma** linha em `embedding_calls`, uma por chamada ao gerador, e a soma de
`InputCount` dessas linhas SHALL ser igual ao número de fragmentos do documento.
Uma indexação que cabe em um lote só SHALL produzir **exatamente uma** linha.

Quando uma chamada falha e a indexação do documento é abortada, as linhas das
chamadas **anteriores**, que já tinham sido concluídas, SHALL permanecer
gravadas.

#### Scenario: Documento com mais fragmentos que o lote gera mais de uma linha
- **WHEN** um documento produz mais fragmentos do que o tamanho do lote configurado — precondição afirmada explicitamente pelo cenário, para que ele não fique verde por nunca alcançar o segundo lote
- **THEN** existe mais de uma linha em `embedding_calls` para aquela tentativa, nenhuma com `InputCount` maior que o tamanho do lote, e a soma dos `InputCount` é igual ao número de fragmentos

#### Scenario: Documento menor que o lote gera exatamente uma linha
- **WHEN** um documento produz menos fragmentos do que o tamanho do lote configurado
- **THEN** existe **exatamente uma** linha em `embedding_calls` para aquela tentativa, com `InputCount` igual ao número de fragmentos

#### Scenario: Falha no segundo lote preserva a linha do primeiro
- **WHEN** a primeira chamada ao gerador devolve os vetores e a segunda lança
- **THEN** existe uma linha com `Failed = false` para a primeira chamada e uma com `Failed = true` para a segunda
- **AND** nenhum fragmento do documento é gravado

### Requirement: Status HTTP da falha do gateway é gravado quando o SDK o expõe tipado
`HttpStatus` SHALL ser gravado sempre que a exceção da chamada expuser o status
de forma tipada, **inclusive quando o tipo da exceção não derivar de
`HttpRequestException`** — caso do SDK do caminho `openai`, que lança
`ClientResultException`, derivada de `Exception`, com o status em `Status`.
`HttpStatus` SHALL permanecer nulo apenas quando o status realmente não estiver
disponível de forma tipada, nunca por consequência de como o SDK declarou o
tipo.

A exceção SHALL continuar propagando para quem chamou, com o mesmo efeito que
teria sem a coleta.

#### Scenario: Gateway responde com erro de servidor
- **WHEN** a chamada ao gerador lança a exceção tipada do SDK com status `502`
- **THEN** existe uma linha com `Failed = true` e `HttpStatus = 502`
- **AND** a indexação daquele documento falha exatamente como falharia sem a coleta

#### Scenario: Exceção sem status tipado
- **WHEN** a chamada ao gerador lança uma exceção que não expõe status de forma tipada
- **THEN** existe uma linha com `Failed = true` e `HttpStatus` nulo, sem status inventado

### Requirement: Token não reportado é nulo, nunca zero
`InputTokens` SHALL ser gravado nulo quando o provedor não reportar uso, ou
reportar uso sem a contagem de entrada, e SHALL NOT ser normalizado para zero em
nenhum ponto entre o gerador e o banco. Zero SHALL ser gravado somente quando o
provedor reportar zero.

`embedding_calls` SHALL NOT ter coluna de tokens de saída: embedding não produz
saída, e uma coluna sempre nula convida a somá-la.

#### Scenario: Provedor não reporta uso
- **WHEN** a resposta do gerador não traz uso
- **THEN** a linha tem `InputTokens` nulo
- **AND** a linha NÃO tem `InputTokens = 0`

#### Scenario: Provedor reporta zero
- **WHEN** a resposta do gerador traz contagem de entrada igual a zero
- **THEN** a linha tem `InputTokens = 0`

#### Scenario: Provedor reporta contagem de entrada
- **WHEN** a resposta do gerador traz contagem de entrada maior que zero
- **THEN** a linha tem `InputTokens` igual ao valor reportado

### Requirement: Linha de tentativa para toda indexação que conta tentativa
`apps/workers` SHALL gravar em `knowledge_indexing_attempts` uma linha por
execução de indexação que **contou tentativa**, com o documento, a base, a
revisão de conteúdo, o número da tentativa, o número máximo de tentativas, o
instante de início e de fim, e o desfecho — `Indexed`, `RetryScheduled`,
`Failed` ou `Discarded`.

A linha SHALL existir exatamente quando a tentativa foi contada, de modo que a
contagem de linhas de um documento seja reconciliável com `IndexingAttempts`
dele.

#### Scenario: Indexação bem-sucedida produz linha fechada
- **WHEN** um documento é indexado com sucesso na primeira tentativa
- **THEN** existe exatamente uma linha com `Attempt = 1`, `Outcome = Indexed`, `FailurePhase` nulo, `FragmentCount` igual ao número de fragmentos gravados e `EndedAt >= StartedAt`

#### Scenario: Três tentativas deixam três linhas consultáveis
- **WHEN** a indexação de um documento falha nas três tentativas
- **THEN** existem três linhas para aquele documento, com `Attempt` igual a 1, 2 e 3, as duas primeiras com `Outcome = RetryScheduled` e a última com `Outcome = Failed`

#### Scenario: Descarte antes de começar não produz linha
- **WHEN** a revisão pedida já não é a corrente no momento em que o trabalho é lido, antes de a tentativa ser contada
- **THEN** não existe nenhuma linha em `knowledge_indexing_attempts` para aquele trabalho
- **AND** não existe nenhuma linha em `embedding_calls` para ele

#### Scenario: Descarte depois do trabalho produz linha
- **WHEN** a revisão do documento muda durante a indexação e a gravação final afeta zero linhas
- **THEN** existe linha com `Outcome = Discarded`
- **AND** as linhas de `embedding_calls` das chamadas que aconteceram permanecem gravadas, porque o consumo aconteceu

### Requirement: O motivo da falha de indexação é a fase, nunca texto de exceção
`FailurePhase` SHALL ser um valor de vocabulário fechado, gravado como texto,
determinado pelo passo da indexação em que a falha ocorreu, e SHALL NOT ser
derivado do texto da mensagem de exceção nem do texto de operador exibido na
tela de documentos.

O texto de operador SHALL continuar sendo gravado em
`knowledge_documents.FailureReason` como antes desta coleta, e os dois SHALL NOT
se substituir.

#### Scenario: Valor gravado pertence ao vocabulário
- **WHEN** uma tentativa de indexação termina em `Failed` ou `RetryScheduled`
- **THEN** `FailurePhase` é um de `Chunking`, `ProviderResolution`, `EmbeddingGateway`, `VectorCountMismatch`, `DimensionMismatch` ou `Persistence`

#### Scenario: Falha do gateway é classificada pela fase
- **WHEN** a chamada ao gerador lança
- **THEN** a linha da tentativa tem `FailurePhase = EmbeddingGateway`

#### Scenario: Divergência de dimensão é classificada pela fase
- **WHEN** o provedor devolve vetor com dimensão diferente da declarada
- **THEN** a linha da tentativa tem `FailurePhase = DimensionMismatch`

#### Scenario: Falha sem chamada ao gateway é classificada pela fase
- **WHEN** a fragmentação de um documento **com conteúdo** devolve zero fragmentos — precondição afirmada explicitamente pelo cenário, para que ele não fique verde por vacuidade
- **THEN** a linha da tentativa tem `Outcome = Failed` e `FailurePhase = Chunking`
- **AND** não existe nenhuma linha em `embedding_calls` para aquela tentativa, porque o gerador não foi chamado

### Requirement: Cada linha de chamada pertence a exatamente um pai, determinado pela finalidade
A linha com `Purpose = Indexing` SHALL ter `KnowledgeIndexingAttemptId`
preenchido e `TaskId` nulo. A linha com `Purpose = Search` SHALL ter `TaskId`
preenchido com a task da execução em que a busca aconteceu e
`KnowledgeIndexingAttemptId` nulo. Nenhuma linha SHALL ter os dois preenchidos
nem os dois nulos.

Uma chamada ao gerador de embedding feita **fora** de qualquer execução de task e
fora de qualquer indexação SHALL NOT produzir linha, e SHALL NOT falhar por
isso.

#### Scenario: Busca dentro de execução é ligada à task
- **WHEN** um agente em execução invoca a tool de busca
- **THEN** a linha tem `Purpose = Search`, `TaskId` igual ao da execução e `KnowledgeIndexingAttemptId` nulo

#### Scenario: Indexação é ligada à tentativa
- **WHEN** um documento é indexado
- **THEN** toda linha daquela indexação tem `Purpose = Indexing`, `KnowledgeIndexingAttemptId` igual ao da linha de tentativa e `TaskId` nulo

#### Scenario: Chamada fora de execução e fora de indexação
- **WHEN** o gerador embrulhado é chamado sem execução de task em andamento e sem indexação em andamento
- **THEN** nenhuma linha é produzida e a chamada não falha por isso

### Requirement: Provedor, modelo e dimensão são snapshot, sem chave estrangeira para o catálogo
`Provider`, `Model` e `Dimensions` SHALL ser gravados na própria linha, e
nenhuma das duas tabelas de métrica de embedding SHALL ter chave estrangeira que
referencie `knowledge_bases`, `knowledge_documents` ou `agents`. Trocar o modelo
de embedding configurado, ou excluir uma base ou um documento, SHALL NOT alterar
nem remover linhas já gravadas.

#### Scenario: Nenhuma chave estrangeira para o catálogo
- **WHEN** o schema migrado é inspecionado
- **THEN** nenhuma das tabelas `embedding_calls` e `knowledge_indexing_attempts` tem chave estrangeira que referencie `knowledge_bases`, `knowledge_documents` ou `agents`

#### Scenario: Exclusão de base não apaga a métrica
- **WHEN** uma base de conhecimento com documentos indexados é excluída
- **THEN** a exclusão acontece normalmente
- **AND** as linhas de `embedding_calls` e de `knowledge_indexing_attempts` daquela base continuam existindo, com o consumo que foi medido

### Requirement: Vocabulário gravado como texto, nunca como ordinal
`Purpose`, `Outcome` e `FailurePhase` SHALL ser persistidos como texto no banco,
e SHALL NOT ser persistidos como inteiro ordinal.

#### Scenario: Valor lido do banco é o nome
- **WHEN** as colunas de vocabulário são lidas diretamente do banco
- **THEN** o valor é o nome — por exemplo `Indexing`, `Failed`, `EmbeddingGateway` — e não um número

### Requirement: Falha ao gravar métrica nunca muda o resultado da indexação nem da busca
Falha em qualquer escrita das tabelas de métrica de embedding SHALL ser
registrada em log de aviso e SHALL NOT alterar o desfecho da indexação, o estado
do documento, os fragmentos gravados, nem o resultado que a tool de busca
devolve ao modelo. A escrita SHALL acontecer depois de o estado do documento
estar gravado.

#### Scenario: Tabela de métrica indisponível durante a indexação
- **WHEN** as tabelas de métrica de embedding não existem no banco durante a indexação de um documento que seria indexado com sucesso
- **THEN** o documento termina `Indexed` com a contagem completa de fragmentos
- **AND** é emitido um log de aviso de falha de gravação de métrica com o identificador do documento

#### Scenario: Busca segue servindo o modelo
- **WHEN** a gravação da linha de busca falha
- **THEN** a tool devolve ao modelo o mesmo resultado que devolveria sem a coleta
