# knowledge-tool-execution Specification

## Purpose

Cobre a **consulta ao índice de conhecimento em tempo de execução**, em
`apps/workers`: quais bases de conhecimento vinculadas a um agente viram tool
oferecida ao modelo, o que a descrição dessa tool afirma e o que ela não pode
afirmar, como a busca por proximidade vetorial é feita e restrita à sua base,
qual é a forma do resultado devolvido ao modelo — incluindo a distância
explícita e a ausência deliberada de qualquer campo de relevância —, a distinção
entre "a base não tem conteúdo indexado" e "a busca não achou nada", e a
degradação da tool quando a consulta falha.

**Não cobre** o catálogo de bases nem de documentos (`knowledge-base-catalog`,
`knowledge-document-catalog`), a indexação (`knowledge-document-indexing`), o
cadastro do vínculo (`agent-knowledge-binding`) nem a unicidade de nome no
conjunto final de tools (`agent-tool-namespace`).

## Requirements

### Requirement: Uma tool de conhecimento por base vinculada e ativa

`apps/workers` SHALL resolver, para cada execução de agente, **uma tool por
`KnowledgeBase` vinculada a esse agente cuja `IsActive` seja verdadeira**.

Base vinculada mas **inativa** SHALL NOT produzir tool — o vínculo permanece
registrado e volta a produzir tool quando a base for reativada, sem nenhuma
alteração no cadastro de vínculo.

Agente **sem nenhuma base vinculada** SHALL receber o conjunto de tools que
receberia se esta capability não existisse — nenhuma tool a mais, nenhum erro,
nenhuma consulta desperdiçada.

A ordem de resolução SHALL ser determinística e independente do nome da base,
para que o conjunto de nomes entregue ao modelo não mude entre execuções com o
mesmo cadastro.

#### Scenario: Base vinculada e ativa produz uma tool
- **WHEN** um agente tem uma `KnowledgeBase` vinculada com `isActive: true`
- **THEN** o conjunto de tools entregue ao LLM contém exatamente uma tool
  correspondente a essa base

#### Scenario: Base vinculada e inativa não é oferecida
- **WHEN** um agente tem uma `KnowledgeBase` vinculada com `isActive: false`,
  com documentos indexados e fragmentos gravados
- **THEN** o conjunto de tools entregue ao LLM não contém nenhuma tool
  correspondente a essa base

#### Scenario: Duas bases vinculadas produzem duas tools distintas
- **WHEN** um agente tem duas `KnowledgeBase` vinculadas e ativas, com nomes
  diferentes
- **THEN** o conjunto de tools contém duas tools, com nomes distintos, e cada
  uma busca apenas na sua própria base

#### Scenario: Agente sem base vinculada não recebe tool de conhecimento
- **WHEN** um agente não tem nenhuma `AgentKnowledgeBase`
- **THEN** nenhuma tool de conhecimento é entregue ao LLM, e a execução segue
  normalmente

### Requirement: Descrição da tool é a `Description` da base

A descrição exposta ao modelo para uma tool de conhecimento SHALL conter a
`Description` cadastrada da `KnowledgeBase` — é o texto por onde o modelo decide
se aquela base é relevante para a pergunta, e é por isso que `apps/api` a exige
não-vazia.

A descrição SHALL também declarar, ao modelo, como ler o resultado:

- que a tool devolve **sempre** os trechos mais próximos, **inclusive quando
  nenhum deles responde** à pergunta;
- que a **distância ordena** os resultados, que menor significa mais próximo, e
  que ela **não é uma medida de acerto**;
- que decidir se um trecho responde é ler o trecho.

A descrição SHALL NOT conter nenhum **valor numérico de corte** de distância,
nem em prosa nem em exemplo. Expor a distância e ao mesmo tempo prescrever um
limiar em texto seria reintroduzir o limiar que foi reprovado por medição, sem o
benefício de ser verificável.

#### Scenario: Descrição da tool carrega a descrição cadastrada da base
- **WHEN** uma `KnowledgeBase` com `description` cadastrada produz uma tool
- **THEN** a descrição da tool exposta ao modelo contém o texto dessa
  `description`

#### Scenario: Descrição da tool não prescreve limiar de distância
- **WHEN** qualquer tool de conhecimento é resolvida
- **THEN** a descrição exposta ao modelo não contém nenhum valor numérico
  apresentado como corte de distância

### Requirement: Busca por proximidade vetorial, restrita à base da tool

A invocação de uma tool de conhecimento SHALL gerar o embedding do texto de
consulta recebido do modelo, com **o mesmo provedor, modelo e dimensão**
declarados na configuração de embedding, e SHALL recuperar fragmentos **apenas
da `KnowledgeBase` daquela tool**.

Os resultados SHALL ser ordenados por **distância de cosseno crescente** entre o
vetor da consulta e o vetor do fragmento, e SHALL ser limitados a **5**.

A ordenação SHALL ser propriedade da consulta emitida ao banco, não consequência
do plano de execução que o banco escolheu.

Fragmentos de outras bases, inclusive de bases vinculadas ao mesmo agente, SHALL
NOT aparecer no resultado de uma tool.

#### Scenario: Busca devolve no máximo cinco trechos, do mais próximo ao mais distante
- **WHEN** uma tool de conhecimento é invocada numa base com mais de cinco
  fragmentos indexados
- **THEN** o resultado contém exatamente cinco trechos, em ordem crescente de
  distância

#### Scenario: A ordenação está na consulta emitida
- **WHEN** uma tool de conhecimento é invocada
- **THEN** o comando SQL emitido ao banco ordena pela expressão de distância
  entre a coluna de embedding e o vetor da consulta, e limita o número de linhas

#### Scenario: Base com menos de cinco fragmentos devolve os que existem
- **WHEN** uma tool de conhecimento é invocada numa base com dois fragmentos
  indexados
- **THEN** o resultado contém os dois trechos, sem erro e sem preenchimento

#### Scenario: Fragmentos de outra base não vazam para o resultado
- **WHEN** um agente tem duas bases vinculadas e ativas, ambas com fragmentos
  indexados, e a tool de uma delas é invocada
- **THEN** todos os trechos devolvidos pertencem a documentos daquela base

### Requirement: Cada resultado traz documento, trecho e distância explícita

Cada item do resultado devolvido ao modelo SHALL conter três informações:

- o **título do documento** como cadastrado no catálogo — o nome que o operador
  controla e vê no painel;
- o **trecho** de texto do fragmento, exatamente como gravado no índice;
- a **distância** entre o vetor da consulta e o vetor do fragmento.

A distância SHALL ser exposta ao modelo em todos os resultados, sem exceção e
sem filtragem prévia por valor.

O resultado SHALL NOT conter nenhum campo que afirme relevância, acerto,
confiança ou que aquele trecho responde à pergunta — o sistema não sabe disso, e
nenhum dado coletado o sustenta.

#### Scenario: Resultado traz os três campos por trecho
- **WHEN** uma tool de conhecimento devolve trechos
- **THEN** cada trecho vem acompanhado do título do documento de origem e da sua
  distância

#### Scenario: Resultado não afirma relevância
- **WHEN** uma tool de conhecimento devolve trechos
- **THEN** o resultado não contém nenhum campo de relevância, acerto ou
  confiança

#### Scenario: Consulta sem nada relacionado na base devolve trechos mesmo assim
- **WHEN** uma tool de conhecimento é invocada com um texto de consulta sobre
  assunto que não existe na base, e a base tem fragmentos indexados
- **THEN** o resultado contém os trechos mais próximos com as suas distâncias, e
  não afirma que nenhum deles responde à pergunta

### Requirement: Base sem conteúdo indexado é distinta de busca sem resultado

A tool SHALL informar ao modelo que a base ainda não tem conteúdo indexado
quando a `KnowledgeBase` daquela tool **não tem nenhum fragmento indexado**.

A tool SHALL NOT relatar esse caso como "a busca não encontrou nada
relacionado": não havia onde procurar, e afirmar ter procurado seria o sistema
afirmando mais do que sabe.

#### Scenario: Base vinculada e ativa, mas sem nenhum documento indexado
- **WHEN** uma tool de conhecimento é invocada numa base cujos documentos estão
  todos pendentes ou falhos, sem nenhum fragmento gravado
- **THEN** o resultado informa que a base ainda não tem conteúdo indexado, e não
  afirma que a busca não encontrou nada relacionado

#### Scenario: Base com conteúdo e consulta irrelevante não é confundida com base vazia
- **WHEN** uma tool de conhecimento é invocada com consulta irrelevante numa
  base que **tem** fragmentos indexados
- **THEN** o resultado traz trechos com distância, e não informa que a base está
  sem conteúdo indexado

### Requirement: Falha na busca degrada a tool, não a execução do agente

Uma falha ao gerar o embedding da consulta ou ao consultar o índice SHALL ser
devolvida ao modelo como resultado de falha daquela invocação, e SHALL NOT
derrubar a execução da task nem impedir o agente de responder por outros meios.

A falha SHALL ser registrada em log com o agente e a base envolvidos.

#### Scenario: Provedor de embedding indisponível na chamada da tool
- **WHEN** a geração do embedding da consulta falha durante a invocação de uma
  tool de conhecimento
- **THEN** a tool devolve um resultado indicando que a consulta não pôde ser
  realizada, a task do agente conclui normalmente, e um registro de log nomeia o
  agente e a base
