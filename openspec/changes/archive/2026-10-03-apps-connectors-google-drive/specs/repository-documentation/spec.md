## MODIFIED Requirements

### Requirement: Porta de entrada única e enxuta no README

O repositório SHALL manter na raiz um `README.md` em inglês cuja única
responsabilidade é apresentar o projeto e encaminhar o leitor: o que o
sistema é, os cinco apps que o compõem, um quickstart mínimo e links para a
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
  os cinco apps e como subir o ambiente local, além de links para a
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

### Requirement: Documentação de arquitetura e premissas oficiais

O repositório SHALL documentar em `docs/` a arquitetura do sistema e as
premissas oficiais do projeto: os cinco apps e o papel de cada um, o modelo
de domínio e as regras de negócio, os frameworks e tecnologias adotados, e as
convenções de estrutura e código estabelecidas.

A documentação de arquitetura SHALL declarar a fronteira entre os dois apps que
falam com sistemas externos: canais de conversa (entrada e saída de mensagens)
pertencem ao `apps/inbox`; provedores de arquivos para bases de conhecimento
pertencem ao `apps/connectors`.

Essa documentação SHALL derivar de `01-ARQUITETURA_E_CONVENCOES.md` em
sentido único — o arquivo de arquitetura é fonte, nunca destino — e SHALL ser
escrita para quem nunca viu o sistema, sem pressupor conhecimento do
histórico do projeto.

#### Scenario: Todos os apps do monorepo estão documentados

- **WHEN** os diretórios presentes em `apps/` são comparados com os apps
  descritos em `docs/architecture.md`
- **THEN** todo app presente em `apps/` está descrito no documento, e todo
  app descrito no documento existe em `apps/`

#### Scenario: Fronteira entre canais e conectores declarada

- **WHEN** um contribuidor consulta `docs/architecture.md` para decidir onde
  colocar a integração com um sistema externo
- **THEN** encontra que canal de conversa vai para `apps/inbox` e provedor de
  arquivos de base de conhecimento vai para `apps/connectors`

#### Scenario: Convenções do projeto estão publicadas

- **WHEN** um contribuidor consulta a documentação de convenções antes de
  escrever código
- **THEN** encontra o isolamento estrito entre apps, a regra de criação de
  `libs/`, o Central Package Management, a degradação graciosa de
  dependências externas, a checagem de integridade no startup, o padrão de
  testes com infraestrutura real e as convenções de frontend

#### Scenario: Arquivos de trabalho do mantenedor ficam no lugar, e o fluxo para docs/ é de sentido único

- **WHEN** o repositório é comparado com o estado anterior a qualquer change
- **THEN** `01-ARQUITETURA_E_CONVENCOES.md` e `02-HISTORICO_E_STATUS.md`
  permanecem na raiz, no mesmo caminho, sem movimentação, renomeação ou
  divisão; o conteúdo deles pode mudar, e o fluxo entre eles e `docs/` é de
  sentido único: o `01` é fonte de `docs/`, nunca destino

### Requirement: Contexto do OpenSpec consistente com o sistema real

O bloco `context` de `openspec/config.yaml` SHALL descrever corretamente a
composição do monorepo, os adapters de canal e os conectores de provedor
efetivamente implementados, porque é lido por agentes ao gerar artefatos de toda
change futura.

#### Scenario: Contexto reflete os cinco apps, os canais e os conectores reais

- **WHEN** o bloco `context` de `openspec/config.yaml` é lido
- **THEN** ele descreve os cinco apps do monorepo (`apps/api`,
  `apps/workers`, `apps/frontend`, `apps/inbox`, `apps/connectors`), cita como
  adapters de canal apenas WAHA e Telegram e como conector de provedor apenas o
  Google Drive, sem mencionar canais ou provedores que nunca foram implementados
