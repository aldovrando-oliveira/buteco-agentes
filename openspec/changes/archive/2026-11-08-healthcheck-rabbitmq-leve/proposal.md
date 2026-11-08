**Issue:** #251

## Why

O healthcheck do RabbitMQ, igual no compose de desenvolvimento e no de produção, roda `rabbitmq-diagnostics -q ping` a cada 5 s. Cada execução sobe o runtime Erlang do CLI e entra na distribuição Erlang. Medido em 07/11/2026:
- cada execução custa de **4,1 a 5,7 s de CPU**, o que, a cada 5 s, ocupa cerca de um núcleo inteiro com a máquina parada;
- sob carga, as execuções passam do timeout de 5 s e se acumulam: a VM do Podman ficou em ~450% de CPU, e as suítes do `apps/frontend` não tiveram rodada válida (#250).

**E o `healthy` de hoje chega antes de o broker aceitar conexões.** Na medição da exploração, o `ping` deu `healthy` cerca de 3 s antes de o listener AMQP abrir, nas três rodadas. O `api` e os `workers` de produção esperam esse `healthy` (`depends_on: … condition: service_healthy`), então a garantia que eles têm hoje é mais fraca do que parece.

## What Changes

- **O healthcheck do `rabbitmq` nos dois composes** troca o `rabbitmq-diagnostics -q ping` por uma checagem TCP na porta AMQP (5672), de dentro do contêiner, sem subir o runtime do CLI. É a checagem que a documentação do RabbitMQ aponta como boa prática para readiness.
- **`interval`, `timeout`, `retries` e `start_period`** novos, decididos no design (Q2), iguais nos dois arquivos.
- **A garantia do `service_healthy`** passa a ser "o listener AMQP aceita conexão TCP", em vez de "o runtime Erlang responde". O design diz o que isso garante a mais e o que continua sem garantia.
- **Notas de deploy e de rollback** no `docs/deployment.md`. A troca do healthcheck recria o contêiner do broker, e o redeploy de hoje (`up -d inbox api workers connectors frontend`) não o recria.
- **A pausa do broker nas suítes**, adotada no `suite-250.sh`, deixa de ser necessária depois desta change, o que a medição do "depois" confere.

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `server-deployment`: um requisito novo para o healthcheck do broker no compose de produção: o `healthy` significa listener AMQP aceitando conexão, sem subir o runtime do CLI.
- `local-dev-environment`: o mesmo healthcheck no compose de desenvolvimento.

## Impact

- **Arquivos:** `docker-compose.yml`, `docker-compose.prod.yml`, `docs/deployment.md` e, se o design pedir, o `docs/development.md`.
- **Apps:** nenhum código de `apps/*` muda. O `api` e os `workers` só sentem a ordem de subida em produção.
- **Deploy:** o contêiner do `rabbitmq` de produção é recriado uma vez (cerca de 12 s sem broker; as filas são duráveis).
- **Docs:** `CHANGELOG.md` e o `02` antes do archive.
