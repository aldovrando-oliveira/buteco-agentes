# local-dev-environment Specification

## Purpose

TBD - defined by change backend-agente-a2a-mvp. Update Purpose after archive.

## Requirements

### Requirement: Dependências de desenvolvimento via Docker Compose
O sistema SHALL prover, na raiz do monorepo, um `docker-compose.yml` capaz
de subir PostgreSQL e RabbitMQ (com plugin de management habilitado) como
dependências de desenvolvimento local, com volumes nomeados para persistir
dados entre reinícios dos containers.

#### Scenario: docker compose up sobe Postgres e RabbitMQ com credenciais de dev
- **WHEN** um desenvolvedor executa `docker compose up` a partir da raiz do
  monorepo, sem configuração adicional
- **THEN** os containers de PostgreSQL e RabbitMQ sobem com sucesso,
  usando credenciais padrão de desenvolvimento, com a porta do PostgreSQL,
  a porta AMQP e a porta da UI de management do RabbitMQ acessíveis no
  host

#### Scenario: Dados persistem entre restarts dos containers
- **WHEN** dados são gravados no PostgreSQL ou no RabbitMQ e os containers
  são reiniciados (`docker compose down` seguido de `docker compose up`,
  sem remover os volumes)
- **THEN** os dados gravados anteriormente continuam disponíveis após o
  restart

### Requirement: Configuração de conexão via variáveis de ambiente
O sistema SHALL exigir que `apps/api` e `apps/workers` leiam a connection
string do PostgreSQL e a URI do RabbitMQ exclusivamente via configuração
(`appsettings` combinado com variáveis de ambiente), nunca de valores
fixos no código-fonte.

#### Scenario: apps/api e apps/workers leem configuração de variáveis de ambiente
- **WHEN** `apps/api` ou `apps/workers` são iniciados com variáveis de
  ambiente definindo a connection string do PostgreSQL e a URI do RabbitMQ
- **THEN** a aplicação correspondente usa esses valores para conectar ao
  PostgreSQL e ao RabbitMQ, sem exigir alteração de código

#### Scenario: .env.example documenta as variáveis sem conter segredos reais
- **WHEN** um desenvolvedor consulta o arquivo `.env.example` na raiz do
  monorepo
- **THEN** todas as variáveis de ambiente usadas por `apps/api` e
  `apps/workers` para conectar ao PostgreSQL e ao RabbitMQ estão
  documentadas ali, com valores de exemplo/dev, e o arquivo `.env` real
  (com valores efetivos) não é versionado no controle de código

### Requirement: Arquivo versionado não carrega credencial de serviço externo
Nenhum arquivo versionado do repositório SHALL conter credencial funcional de
serviço externo — chave de API, token ou senha que autentique contra um endpoint
real. Vale para `appsettings*.json`, `.env*.example`, `docker-compose*.yml` e
qualquer outro artefato rastreado.

Arquivos de configuração de desenvolvimento versionados SHALL trazer o **nome** de
cada variável com valor vazio ou placeholder reconhecível, nunca o valor efetivo,
e a credencial SHALL ser fornecida por variável de ambiente pelo desenvolvedor.

Quando uma credencial for encontrada em arquivo versionado, removê-la do estado
atual SHALL NOT ser tratado como correção suficiente: a credencial permanece
recuperável no histórico do controle de versão, e a correção é a **rotação** no
provedor que a emitiu.

Este requisito existe porque uma chave de API funcional, com endpoint de gateway
interno, viveu commitada num `appsettings.Development.json`, entregue a todo
clone do repositório — e o contorno documentado para ela era sobrescrever o
endpoint na linha de comando a cada execução, o que preservava a chave no arquivo.

#### Scenario: Configuração de desenvolvimento não traz credencial funcional
- **WHEN** um arquivo `appsettings.Development.json` versionado é inspecionado
- **THEN** os campos de credencial estão vazios ou com placeholder reconhecível,
  e nenhum valor autentica contra um endpoint real

#### Scenario: Credencial exposta exige rotação, não apenas remoção
- **WHEN** uma credencial funcional é identificada em arquivo versionado
- **THEN** a resposta registrada inclui a rotação da credencial no provedor, e a
  remoção do arquivo **não** é declarada como resolução do problema

### Requirement: Exemplo de ambiente consistente com a configuração versionada
Os valores de `.env.example` SHALL ser consistentes com o que os arquivos de
configuração versionados dos apps esperam, de modo que seguir o procedimento
documentado de preparação do ambiente produza um sistema que conecta.

Divergência entre a porta publicada pelo compose de desenvolvimento e a porta
declarada nos `appsettings.Development.json` SHALL ser tratada como defeito de
configuração, não como ajuste esperado do desenvolvedor.

Este requisito existe porque `.env.example` publicava Postgres e RabbitMQ nas
portas padrão enquanto os `appsettings.Development.json` versionados apontavam
para portas não-padrão: copiar o exemplo como a documentação manda fazia os três
apps falharem a conexão, com sintoma de banco fora do ar.

#### Scenario: Procedimento documentado produz ambiente que conecta
- **WHEN** um desenvolvedor copia `.env.example` para `.env`, sobe as
  dependências com o compose de desenvolvimento e inicia os apps, sem editar
  nenhum dos dois arquivos
- **THEN** `apps/api`, `apps/workers` e `apps/inbox` conectam ao PostgreSQL e ao
  RabbitMQ sem ajuste manual de porta
