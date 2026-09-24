## ADDED Requirements

### Requirement: Ciclo de vida da issue pelo board documentado

O repositório SHALL documentar em `01-ARQUITETURA_E_CONVENCOES.md`, ao lado da
convenção que exige issue por change, o ciclo de vida da issue pelo board do
GitHub: as colunas existentes, o que cada uma significa, o gatilho de cada
movimento e **quem** o executa — pessoa ou workflow do Projects.

Os nomes das colunas SHALL ser escritos exatamente como o campo `Status` do
projeto os define, sem normalização de caixa.

A documentação SHALL declarar que o push e a abertura do PR só são permitidos
depois do archive da change, com o motivo escrito junto — change ativa é
proposta que ainda pode mudar, e o archive é o que move as decisões dos
artefatos da change para as specs vivas. A documentação SHALL registrar que essa
regra admite exceção explícita do dono, dita no momento.

A documentação SHALL separar o que é **automático e verificado**, o que é
**automático e inferido** e o que é **manual**, e SHALL NOT afirmar
comportamento de automação que não foi medido.

A documentação SHALL registrar que a regra do archive antes do push substitui o
padrão anterior de dois PRs — um de código e um `chore/archive-*` posterior —
para que a próxima change não abra o segundo por hábito.

#### Scenario: As colunas documentadas são as colunas que existem

- **WHEN** as opções do campo `Status` do board são comparadas com a tabela de
  colunas de `01-ARQUITETURA_E_CONVENCOES.md`
- **THEN** toda opção do campo aparece na tabela, e toda coluna da tabela existe
  como opção do campo, **com a mesma grafia** — incluindo a caixa da segunda
  palavra

#### Scenario: O gatilho de cada coluna diz quem move

- **WHEN** um contribuidor lê a tabela de colunas
- **THEN** encontra, para cada coluna, o evento que move a issue para lá e se
  quem move é uma pessoa ou um workflow do Projects

#### Scenario: A regra do archive antes do push carrega o motivo

- **WHEN** a regra "nenhum push e nenhum PR com a change ativa" é lida
- **THEN** o motivo está escrito na mesma passagem, e a exceção explícita do
  dono está declarada

#### Scenario: `In review` exige as três condições

- **WHEN** o gatilho da coluna `In review` é lido
- **THEN** ele nomeia as três condições — change arquivada, push feito, PR
  aberto — e não apenas a abertura do PR

#### Scenario: `Closes` está documentado com as duas precisões

- **WHEN** a regra do `Closes #<issue>` no corpo do PR é lida
- **THEN** ela declara que o fechamento acontece no **merge** e não na
  aprovação, e que o PR fecha **uma** issue — a que originou a change —,
  entrando como `Refs` qualquer achado que tenha virado issue própria

#### Scenario: O que não foi decidido está registrado como não decidido

- **WHEN** a documentação do fluxo é lida à procura do destino da issue cuja
  change é abandonada
- **THEN** encontra a pergunta registrada explicitamente como não decidida, em
  vez de ausente

### Requirement: Fonte autoritativa da fila declarada

O repositório SHALL declarar, na documentação de convenções, **qual artefato é
a fonte autoritativa da ordem de execução** das issues, e SHALL NOT descrever
como fonte um mecanismo cuja ordem não seja observável.

Enquanto a ordem manual do board não for visível em nenhuma view e o índice de
itens do projeto não listar todas as issues do board, a documentação SHALL
declarar que a fila ordenada mora em `02-HISTORICO_E_STATUS.md` e que o board
informa **em que coluna** cada issue está, não em que posição.

A documentação SHALL registrar que os campos `Priority` e `Iteration` ficam
vazios por decisão, com o motivo, e SHALL registrar o gatilho observável que
reabre a decisão.

A documentação SHALL declarar que a dependência entre issues é registrada pela
relação `blocked-by` nativa do GitHub, e que a issue bloqueante mora na mesma
coluna da bloqueada e acima dela.

#### Scenario: A documentação não aponta o board como fonte da fila

- **WHEN** um contribuidor procura onde está a ordem de execução das issues
- **THEN** a documentação o manda ao `02-HISTORICO_E_STATUS.md`, e declara
  explicitamente que o board não é fonte confiável da fila enquanto o índice de
  itens estiver incompleto

#### Scenario: Campo vazio por decisão tem motivo e gatilho escritos

- **WHEN** a passagem sobre `Priority` e `Iteration` é lida
- **THEN** ela diz que os campos ficam vazios por decisão, diz por quê, e nomeia
  a condição observável que reabre a decisão

#### Scenario: A dependência entre issues tem registro além da prosa

- **WHEN** a regra sobre issue bloqueada é lida
- **THEN** ela manda registrar a relação `blocked-by` no GitHub, e posiciona a
  bloqueante na mesma coluna da bloqueada, acima dela

### Requirement: Fluxo de trabalho mínimo alcançável pelo contexto do OpenSpec

O bloco `context` de `openspec/config.yaml` SHALL carregar o fluxo de trabalho
mínimo que diz respeito a quem escreve artefato de change, porque é o texto lido
ao gerar os artefatos de toda change futura e alcança quem abre a próxima change
sem ter lido `01-ARQUITETURA_E_CONVENCOES.md`.

O bloco SHALL declarar: que abrir a change move a issue para a coluna de
trabalho em andamento; que o archive precede o push e a abertura do PR; e que o
PR carrega `Closes #<issue>`, o que move a issue para a coluna de revisão.

O bloco SHALL apontar para a convenção correspondente em
`01-ARQUITETURA_E_CONVENCOES.md` em vez de repetir o motivo por extenso.

#### Scenario: O contexto carrega as três afirmações do fluxo

- **WHEN** o bloco `context` de `openspec/config.yaml` é lido
- **THEN** ele declara o gatilho de abertura da change, a precedência do archive
  sobre o push e o PR, e a regra do `Closes`

#### Scenario: O contexto aponta para a convenção em vez de duplicá-la

- **WHEN** o bloco `context` é comparado com a convenção correspondente em
  `01-ARQUITETURA_E_CONVENCOES.md`
- **THEN** o bloco cita a convenção pelo número e pelo arquivo, e não repete o
  motivo por extenso
