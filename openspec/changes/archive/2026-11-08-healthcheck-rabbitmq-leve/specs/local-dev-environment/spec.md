## ADDED Requirements

### Requirement: Healthcheck do RabbitMQ de desenvolvimento leve e igual ao de produção
O serviço `rabbitmq` do `docker-compose.yml` SHALL declarar o mesmo healthcheck do `docker-compose.prod.yml` (`server-deployment`): a checagem TCP na porta AMQP dentro do contêiner, com `interval` 10s, `timeout` 5s, `retries` 6 e `start_period` 30s.

- O healthcheck de desenvolvimento SHALL NOT subir o runtime Erlang do CLI do RabbitMQ. Na máquina de desenvolvimento, o compose divide a CPU com as suítes de teste, e o `ping` a cada 5 s ocupava cerca de um núcleo, chegando a ~450% de CPU da VM sob carga (#251, #250).
- Com o healthcheck leve, a suíte de testes SHALL poder rodar com o broker de desenvolvimento de pé, sem pausá-lo.

#### Scenario: Broker de desenvolvimento ocioso não ocupa a CPU
- **WHEN** o broker do compose de desenvolvimento está `healthy` e ninguém o usa
- **THEN** a CPU da VM do motor de contêiner com o broker rodando fica próxima da medida com o broker pausado, e não um núcleo acima dela

#### Scenario: Suíte válida com o broker de pé
- **WHEN** a suíte do `apps/frontend` roda com o broker do compose de desenvolvimento de pé e `healthy`
- **THEN** a rodada sai válida pela regra assimétrica, sem precisar pausar o broker
