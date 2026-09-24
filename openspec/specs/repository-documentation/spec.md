# repository-documentation Specification

## Purpose

TBD - defined by change documentacao-repositorio-open-source. Update Purpose after archive.

## Requirements

### Requirement: Porta de entrada única e enxuta no README

O repositório SHALL manter na raiz um `README.md` em inglês cuja única
responsabilidade é apresentar o projeto e encaminhar o leitor: o que o
sistema é, os quatro apps que o compõem, um quickstart mínimo e links para a
documentação detalhada. Instruções de desenvolvimento, configuração, deploy,
arquitetura e convenções SHALL residir em `docs/`, nunca no `README.md`.

O `README.md` SHALL declarar explicitamente que a documentação em `docs/` e
o guia de contribuição estão em português, e SHALL apresentar
`01-ARQUITETURA_E_CONVENCOES.md` e `02-HISTORICO_E_STATUS.md` como notas
internas de trabalho do mantenedor, distinguindo-os da documentação oficial
do projeto.

#### Scenario: Leitor identifica o projeto sem sair do README

- **WHEN** uma pessoa que nunca viu o projeto abre o `README.md`
- **THEN** ela encontra, sem rolar até o fim, o que o sistema faz, quais são
  os quatro apps e como subir o ambiente local, além de links para a
  documentação detalhada de cada assunto

#### Scenario: README não absorve documentação detalhada

- **WHEN** o `README.md` é inspecionado em busca de procedimentos de
  desenvolvimento, inventário de variáveis de ambiente, passos de deploy,
  descrição de arquitetura ou convenções de código
- **THEN** nenhum desses conteúdos está no `README.md`, e cada um deles é
  alcançável por link para o documento correspondente em `docs/`

#### Scenario: Fronteira de idioma é declarada, não descoberta

- **WHEN** um leitor de língua inglesa lê o `README.md`
- **THEN** o documento informa que `docs/` e `CONTRIBUTING.md` estão em
  português antes que o leitor precise clicar em um link para descobrir

### Requirement: Documentação técnica organizada por audiência em `docs/`

O repositório SHALL manter em `docs/` a documentação técnica em português,
com um documento por audiência e um índice navegável em `docs/README.md` que
liste todos eles. A documentação SHALL cobrir, em documentos distintos:
ambiente de desenvolvimento, inventário de configuração, deploy, arquitetura
e convenções do projeto.

Nenhum documento de `docs/` SHALL depender do `README.md` para ser
compreendido, e nenhum SHALL narrar histórico de changes ou decisões
superadas — `docs/` descreve o sistema como ele é no presente.

#### Scenario: Cada audiência tem um destino único

- **WHEN** alguém precisa subir o projeto localmente, consultar uma variável
  de ambiente, fazer deploy, entender a arquitetura ou conhecer as
  convenções de código
- **THEN** existe exatamente um documento em `docs/` responsável por aquele
  assunto, alcançável a partir do índice em `docs/README.md`

#### Scenario: Índice de docs lista todos os documentos existentes

- **WHEN** `docs/README.md` é comparado com os arquivos `.md` presentes em
  `docs/`
- **THEN** todo arquivo presente em `docs/` está listado no índice, e todo
  item listado no índice corresponde a um arquivo existente

#### Scenario: Documentação de desenvolvimento preserva o troubleshooting de Podman

- **WHEN** um desenvolvedor em máquina com Podman e sem Docker consulta
  `docs/development.md`
- **THEN** encontra as variáveis `DOCKER_HOST` e `TESTCONTAINERS_RYUK_DISABLED`,
  o trecho que as define condicionalmente, e a descrição de como cada uma
  falha quando ausente, incluindo o sinal que distingue falha de
  infraestrutura de falha de código

### Requirement: Documentação de arquitetura e premissas oficiais

O repositório SHALL documentar em `docs/` a arquitetura do sistema e as
premissas oficiais do projeto: os quatro apps e o papel de cada um, o modelo
de domínio e as regras de negócio, os frameworks e tecnologias adotados, e as
convenções de estrutura e código estabelecidas.

Essa documentação SHALL derivar de `01-ARQUITETURA_E_CONVENCOES.md` em
sentido único — o arquivo de arquitetura é fonte, nunca destino — e SHALL ser
escrita para quem nunca viu o sistema, sem pressupor conhecimento do
histórico do projeto.

#### Scenario: Todos os apps do monorepo estão documentados

- **WHEN** os diretórios presentes em `apps/` são comparados com os apps
  descritos em `docs/architecture.md`
- **THEN** todo app presente em `apps/` está descrito no documento, e todo
  app descrito no documento existe em `apps/`

#### Scenario: Convenções do projeto estão publicadas

- **WHEN** um contribuidor consulta a documentação de convenções antes de
  escrever código
- **THEN** encontra o isolamento estrito entre apps, a regra de criação de
  `libs/`, o Central Package Management, a degradação graciosa de
  dependências externas, a checagem de integridade no startup, o padrão de
  testes com infraestrutura real e as convenções de frontend

#### Scenario: Arquivos protegidos permanecem intocados

- **WHEN** o resultado desta change é comparado com o estado anterior do
  repositório
- **THEN** `01-ARQUITETURA_E_CONVENCOES.md` e `02-HISTORICO_E_STATUS.md`
  permanecem no mesmo caminho e com o mesmo conteúdo, sem edição,
  movimentação, renomeação ou divisão

### Requirement: Changelog em Keep a Changelog sem versão fechada

O repositório SHALL manter na raiz um `CHANGELOG.md` em português seguindo o
formato Keep a Changelog e versionamento semântico, contendo uma seção
`[Unreleased]` com o histórico do projeto agrupado em `Added`, `Changed` e
`Fixed`.

O agrupamento SHALL usar as linhas de trabalho do projeto como unidade
narrativa, não uma entrada por change arquivada. O `CHANGELOG.md` SHALL NOT
declarar nenhuma versão fechada enquanto nenhum release oficial existir.

#### Scenario: Changelog é legível por quem chega ao projeto

- **WHEN** uma pessoa abre o `CHANGELOG.md` para entender o que o projeto já
  entrega
- **THEN** encontra o histórico agrupado por linha de trabalho — catálogo de
  agentes, protocolo A2A, MCP, delegação, inbox e canais, autenticação,
  containerização, painel, base de conhecimento — e não uma lista de 55
  entradas correspondentes às changes arquivadas

#### Scenario: Nenhuma versão é fechada

- **WHEN** o `CHANGELOG.md` é inspecionado
- **THEN** existe a seção `[Unreleased]` e não existe nenhuma seção de versão
  datada

### Requirement: Integridade da documentação verificada por script

O repositório SHALL prover um script executável que verifique invariantes da
documentação e falhe com mensagem identificando o arquivo e o problema. O
script SHALL verificar, no mínimo: que todo link relativo em arquivo `.md`
resolve para um caminho existente; que nenhum link aponta para
`openspec/changes/<nome>/` quando a change correspondente está em
`openspec/changes/archive/`; que todo diretório em `apps/` aparece na
documentação de arquitetura; e que `CHANGELOG.md` contém a seção
`[Unreleased]`.

Manter a documentação correta SHALL NOT depender apenas de instrução em prosa
no guia de contribuição.

#### Scenario: Link relativo quebrado é detectado

- **WHEN** um arquivo `.md` do repositório contém um link relativo para um
  caminho que não existe
- **THEN** o script falha e identifica o arquivo de origem e o alvo
  inexistente

#### Scenario: Link para change arquivada sem prefixo archive é detectado

- **WHEN** um arquivo `.md` referencia `openspec/changes/<nome>/...` e a
  change `<nome>` está sob `openspec/changes/archive/`
- **THEN** o script falha e indica o caminho de `archive/` correto

#### Scenario: App não documentado é detectado nos dois sentidos

- **WHEN** um novo diretório é adicionado a `apps/` sem entrada
  correspondente na documentação de arquitetura, ou a documentação descreve
  um app que não existe em `apps/`
- **THEN** o script falha em ambos os casos, identificando qual lado está
  sem contraparte

#### Scenario: Documentação íntegra passa sem erro

- **WHEN** o script é executado sobre o repositório com a documentação
  correta
- **THEN** ele termina com sucesso e sem reportar violações

### Requirement: Contexto do OpenSpec consistente com o sistema real

O bloco `context` de `openspec/config.yaml` SHALL descrever corretamente a
composição do monorepo e os adapters de canal efetivamente implementados,
porque é lido por agentes ao gerar artefatos de toda change futura.

#### Scenario: Contexto reflete os quatro apps e os canais reais

- **WHEN** o bloco `context` de `openspec/config.yaml` é lido
- **THEN** ele descreve os quatro apps do monorepo (`apps/api`,
  `apps/workers`, `apps/frontend`, `apps/inbox`) e cita como adapters de
  canal apenas WAHA e Telegram, sem mencionar canais que nunca foram
  implementados

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
