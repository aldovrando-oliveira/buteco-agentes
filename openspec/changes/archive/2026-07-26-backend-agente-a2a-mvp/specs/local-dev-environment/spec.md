## ADDED Requirements

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
