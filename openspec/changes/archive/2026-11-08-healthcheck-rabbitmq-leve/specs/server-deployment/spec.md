## ADDED Requirements

### Requirement: Healthcheck do RabbitMQ prova o listener AMQP sem subir o runtime do CLI
O serviço `rabbitmq` do `docker-compose.prod.yml` SHALL declarar o healthcheck como uma checagem TCP na porta AMQP, feita **dentro do contêiner**, sem subir o runtime Erlang do CLI do RabbitMQ:

```yaml
healthcheck:
  test: ["CMD", "bash", "-c", "</dev/tcp/127.0.0.1/5672"]
  interval: 10s
  timeout: 5s
  retries: 6
  start_period: 30s
```

- O healthcheck SHALL NOT usar `rabbitmq-diagnostics`, `rabbitmqctl` nem outro comando do CLI do RabbitMQ: cada execução deles sobe o runtime Erlang e entra na distribuição Erlang. Medido em 07/11/2026, isso custava de 4,1 a 5,7 s de CPU por execução, e se acumulava sob carga (#251).
- O `healthy` SHALL significar que o listener AMQP do broker aceita conexão TCP. Os serviços que declaram `depends_on: rabbitmq: condition: service_healthy` (o `api` e os `workers`) SHALL subir só depois disso, e não apenas depois de o runtime Erlang responder.
- O bloco de healthcheck do `rabbitmq` SHALL ser idêntico ao do `docker-compose.yml` (`local-dev-environment`). Divergência entre os dois SHALL ser tratada como defeito.
- A imagem do `rabbitmq` SHALL trazer o `bash` (base Debian ou Ubuntu, e não Alpine). Trocar a imagem ou a tag SHALL incluir a conferência `bash -c 'echo ok'` na imagem nova.

#### Scenario: healthy só depois de o listener aceitar
- **WHEN** o contêiner do `rabbitmq` é reiniciado
- **THEN** o estado `healthy` chega depois do instante em que a porta 5672 passa a aceitar conexão dentro do contêiner, e nunca antes

#### Scenario: A checagem não sobe o runtime do CLI
- **WHEN** o healthcheck do `rabbitmq` executa
- **THEN** o comando é a conexão TCP do `bash` com `127.0.0.1:5672`, e nenhum processo do runtime Erlang do CLI é criado por ela

#### Scenario: O app parado leva a unhealthy pela contagem de falhas
- **WHEN** o app RabbitMQ é parado dentro do contêiner (`rabbitmqctl stop_app`) com o contêiner `healthy` e a porta 5672 aceitando
- **THEN** a porta 5672 deixa de aceitar, nenhuma checagem passa depois disso, e o contêiner passa a `unhealthy` junto com a sexta falha seguida (`FailingStreak` igual aos `retries`, 6)
- **AND** o `unhealthy` chega entre 50 e 80 s depois da parada, como prazo de sanidade. O contrato é a contagem: o tempo depende de como o motor conta o intervalo (o Podman conta a partir do fim da checagem anterior, ~11 s com `interval` de 10 s; o Docker pode contar de outro jeito)

#### Scenario: O app de volta leva a healthy
- **WHEN** o app RabbitMQ é religado (`rabbitmqctl start_app`) com o contêiner `unhealthy`
- **THEN** o contêiner volta a `healthy` na primeira checagem depois de a porta 5672 aceitar

#### Scenario: Os dois composes declaram o mesmo healthcheck
- **WHEN** o `docker-compose.prod.yml` e o `docker-compose.yml` são comparados
- **THEN** o bloco `healthcheck` do `rabbitmq` é o mesmo nos dois: o `test`, o `interval`, o `timeout`, os `retries` e o `start_period`
