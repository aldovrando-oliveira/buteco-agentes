## Context

A #251 nasceu no fechamento da #250 (07/11/2026). Duas rodadas da suíte do `apps/frontend` saíram inconclusivas, com a VM do Podman a ~450% de CPU e o broker `unhealthy`. Com o broker pausado durante a suíte, as rodadas seguintes aprovaram em 147 s e 160 s.

**Fontes:**
- **a #251:** o corpo e o [complemento](https://github.com/aldovrando-oliveira/buteco-agentes/issues/251#issuecomment-6713394225) do custo em produção e da garantia do `service_healthy`;
- **os comentários na #226 e na #176:** a tabela das 174 rodadas;
- **os dois composes**, o `docs/deployment.md` e as specs `server-deployment` e `local-dev-environment`;
- **o código que conecta ao broker:**
  - `TaskJobConsumer` e `KnowledgeIndexingConsumer` (`apps/workers`) conectam uma vez, no `ExecuteAsync`;
  - os publishers do `api` e dos `workers` conectam sob demanda e reconectam;
  - todos usam o `RabbitMQ.Client` 7.2.1, que traz `AutomaticRecoveryEnabled` e `TopologyRecoveryEnabled` ligados por padrão (XML do pacote);
- **a documentação do RabbitMQ, "Monitoring"** (https://www.rabbitmq.com/docs/monitoring):
  - `#readiness-probes`: os probes pelo CLI têm impacto de desempenho, porque o CLI entra na distribuição Erlang a cada execução; o operador oficial para Kubernetes usa uma checagem TCP na porta AMQP, e a documentação a chama de boa prática;
  - `#stage-1`: o `ping` só confirma que o runtime está de pé;
  - `#stage-4`: o `check_port_connectivity` confere os listeners;
  - `#monitoring-frequency` e `#individual-checks`: 30 s (ou de 30 a 60 s) em produção, 5 s no mínimo em desenvolvimento, e as checagens seguem a mesma frequência;
- **o motor do compose:**
  - desenvolvimento: podman 5.8.3 com o podman-compose 1.6.0, que passa o `start_period` como `--health-start-period` e o `start_interval` como `--health-startup-interval`;
  - produção: Docker Compose (`docs/deployment.md`).

**Estado de hoje (`a852946`), igual em `docker-compose.yml` e `docker-compose.prod.yml`:**

```yaml
healthcheck:
  test: ["CMD", "rabbitmq-diagnostics", "-q", "ping"]
  interval: 5s
  timeout: 5s
  retries: 10
```

**Quem depende do `healthy`:**
- em produção, o `api` e os `workers` (`depends_on: rabbitmq: condition: service_healthy`);
- em desenvolvimento, ninguém: os apps rodam no host, por `dotnet run`.

### Medição de antes (07/11/2026, compose de desenvolvimento, nesta máquina)

| o que | medido | como |
|---|---|---|
| custo de uma execução do `ping` | relógio de 1,50 / 2,27 / 1,74 s; **CPU de 4,1 / 5,7 / 4,2 s** (user + sys) | `podman exec … bash -c 'time rabbitmq-diagnostics -q ping'`, contêiner `healthy`, host ocioso |
| custo de uma checagem TCP na 5672 | 0,006 / 0,003 / 0,002 s | `podman exec … bash -c 'time (</dev/tcp/127.0.0.1/5672)'` |
| CPU da VM, broker rodando e `healthy` | média ~62%, de 21 a 81% (12 amostras a cada 5 s) | `top -l 13 -s 5 -pid <VM>`, load 6,48, `CPU_Speed_Limit` 100 |
| CPU da VM, broker pausado | média ~14%, de 7 a 16% (12 amostras) | o mesmo, logo em seguida |
| CPU da VM no estado acumulado (sob carga) | ~423–450% | fechamento da #250, com o broker `unhealthy` |
| tempo até `healthy` depois de `podman restart` | **9,6 / 10,2 / 9,8 s** | `~/.cache/buteco-agents/251-medicao/tempo2.py`; saída em `antes-20261107.txt`, sondando a saúde a cada 0,2 s |
| tempo até a 5672 aceitar **dentro do contêiner** | **12,3 / 13,0 / 12,9 s** | o mesmo script, com `podman exec … bash -c '</dev/tcp/127.0.0.1/5672'` |
| "Time to start RabbitMQ" do log | 11.657 / 12.506 / 12.594 ms | `podman logs`; o listener `[::]:5672` abre no fim do boot |

**Duas leituras da tabela:**
- **O `healthy` de hoje chega 2,7 a 3,1 s antes de o broker aceitar AMQP.** O `ping` (stage 1) responde assim que o runtime Erlang está de pé, antes de o RabbitMQ terminar o boot e abrir o listener. Um `workers` que suba nesse intervalo falha na primeira conexão do consumidor, e o processo cai: o `restart: unless-stopped` o sobe de novo.
- **A porta publicada no host não serve para medir:** o encaminhador do Podman aceita a conexão na hora (0,0 s), com ou sem listener. A checagem tem de rodar dentro do contêiner.

O custo atribuível ao broker em si (o broker ocioso sem healthcheck) ainda não foi separado do custo do healthcheck. A medição do "depois" separa os dois (D4).

## Goals / Non-Goals

**Goals:**
- Um healthcheck que não suba o runtime do CLI, igual nos dois composes.
- O `healthy` significando "o listener AMQP aceita conexão", o que **fortalece** a garantia do `service_healthy` para o `api` e os `workers`.
- A medição antes e depois, no compose de desenvolvimento.
- As notas de deploy e de rollback de produção.

**Non-Goals:**
- **Alarmes de memória e de disco, vhosts, partição de cluster:** nenhuma das duas checagens os cobre hoje, e o broker é de nó único. Fica fora.
- **O healthcheck do `postgres`** (`pg_isready`, leve). Não é o problema medido.
- **A reconexão do consumidor dos `workers` no boot** (conectar com nova tentativa em vez de cair) é a #253, sem milestone (Q5). A checagem nova reduz a janela.
- **Tirar a pausa do broker do `suite-250.sh`:** é script local. Os `suite-*.sh` das próximas changes decidem com a medição do "depois".

## Decisions

### D1. A checagem: TCP na porta AMQP, dentro do contêiner, sem shell intermediário

```yaml
test: ["CMD", "bash", "-c", "</dev/tcp/127.0.0.1/5672"]
```

- **O `bash` existe na imagem, conferido na tag exata dos dois composes** (complemento do dono na Q1): `podman run --rm rabbitmq:4.3-management bash -c 'echo ok'` respondeu `ok`, com `/usr/bin/bash`, GNU bash 5.2.21, sobre **Ubuntu 24.04.4 LTS**, na imagem `1190aeb118e3…` (criada em 23/07/2026). Não é a variante Alpine (`…-management-alpine`), que os composes não usam.
- **A mecânica:** o `bash` abre uma conexão TCP com o listener AMQP e a fecha: código 0 quando o listener aceita; diferente de 0 quando recusa ou não há listener.
- **`CMD` com o `bash` explícito, e não `CMD-SHELL`:** sem o `sh -c` no meio. A redireção `/dev/tcp` é do `bash`, e o `sh` da imagem pode não ser o `bash`.
- **`127.0.0.1` e não `localhost`:** o listener é `[::]:5672`, que aceita IPv4 também (conferido na exploração: a checagem passa). `localhost` poderia resolver para `::1` primeiro, e isso não muda nada aqui, mas o endereço fixo evita a resolução.

**Alternativas:**

| checagem | custo | o que garante | por que não |
|---|---|---|---|
| `rabbitmq-diagnostics -q ping` (hoje) | 4–6 s de CPU por execução | o runtime Erlang de pé (stage 1) | o custo, e o `healthy` antes do listener |
| `rabbitmq-diagnostics -q check_port_connectivity` (stage 4) | o mesmo runtime do CLI | todos os listeners aceitam | resolve a garantia e não resolve o custo; com 30 s, cairia para 1/6 |
| HTTP do management (`/api/health/checks/port-listener/5672`) | um processo `curl` e uma chamada autenticada | o mesmo, pelo plugin de management | exige credencial no healthcheck e depende do plugin |
| **TCP na 5672 (recomendada, Q1)** | milissegundos | o listener AMQP aceita conexão | não autentica nem confere vhost (D2) |

### D2. A garantia do `service_healthy`: o que muda

```
 podman/docker restart
   │
   ├─ ~0 s     runtime Erlang sobe
   ├─ ~9–10 s  `ping` responde ............ HOJE: healthy aqui
   ├─ ~11–12 s boot do RabbitMQ termina (definições, usuário padrão, vhost "/")
   ├─ ~12–13 s listener AMQP [::]:5672 aceita
   │                                       NOVO: healthy na 1ª checagem depois daqui
   └─ api/workers sobem (depends_on: service_healthy)
```

| | hoje (`ping`) | nova (TCP) |
|---|---|---|
| o runtime Erlang de pé | sim | sim (o listener só existe com ele) |
| o boot do RabbitMQ terminado | **não**: `healthy` 2,7–3,1 s antes | **sim**: o listener abre no fim do boot ("Server startup complete" logo depois) |
| o listener AMQP aceita TCP | **não** | **sim** |
| o usuário do `.env.prod` autentica | não | **não**: a checagem não faz handshake AMQP. O usuário padrão é criado no boot, antes do listener, então na prática existe; mas a checagem não o prova |
| alarmes de memória e disco, vhost falho | não | não |
| depois do boot: o broker travado com o listener aberto | não (o runtime responderia) | não (a porta aceitaria) |

- **O ganho:** a ordem de subida do `api` e dos `workers` passa a esperar o broker aceitar conexão, e não só o runtime.
- **O que continua igual:** nenhuma das duas autentica, e nenhuma pega alarmes. A resiliência real continua sendo a do cliente (reconexão automática do `RabbitMQ.Client`) e o `restart: unless-stopped`.
- **O que piora:** nada medido. Um broker cujo runtime está de pé mas cujo listener caiu passa a ficar `unhealthy` (hoje ficaria `healthy`), e isso é o comportamento desejado.

### D3. Os parâmetros: intervalo curto, porque o custo deixou de existir

O custo da checagem nova é desprezível, então o intervalo deixa de ser o que pesa. O que pesa é quanto o `api` e os `workers` esperam no boot.

| proposta | `interval` | `timeout` | `retries` | `start_period` | `start_interval` | `healthy` no boot (estimado) | contra |
|---|---|---|---|---|---|---|---|
| **A (recomendada, Q2)** | 10s | 5s | 6 | 30s | — | listener + até 10 s ≈ 13 a 23 s | — |
| B (a da documentação) | 30s | 5s | 3 | 30s | 2s | listener + até 2 s ≈ 13 a 15 s | depende do `start_interval`: Docker Engine 25+ e Compose 2.20.2+ em produção (versão não conferida), e no podman ele vira `--health-startup-interval`, que é de outro mecanismo (o startup healthcheck) e pode ser ignorado sem um `--health-startup-cmd` |
| C (a de hoje, com TCP) | 5s | 5s | 10 | — | — | listener + até 5 s | nenhum, além de checar 6 vezes mais do que precisa |
| B sem `start_interval` | 30s | 5s | 3 | 30s | — | **até 30 s**, com a primeira checagem só aos 30 s | o boot do stack fica ~20 s mais lento |

- **Por que A:**
  - não depende de recurso de versão de motor;
  - funciona igual no Docker e no podman;
  - o custo de 6 checagens TCP por minuto é desprezível;
  - o `unhealthy` chega em ~1 min de listener fechado (6 × 10 s), parecido com os ~50 s de hoje (10 × 5 s).
- **A documentação recomenda 30 a 60 s** pensando no custo das checagens e da coleta de métricas. Com uma checagem TCP, esse custo não existe, e o intervalo de 10 s não contraria o motivo da recomendação. Isso fica registrado como escolha, não como divergência.
- **O `start_period` de 30 s** cobre o boot medido (~13 s) com folga: uma falha nesse período não conta para o `retries`.

### D4. A medição antes e depois, no compose de desenvolvimento

O script `~/.cache/buteco-agents/251-medicao/medir.sh` (local, fora do repositório) roda as três medições em cada estado e grava a saída inteira:

| medição | estado "antes" | estado "depois" |
|---|---|---|
| custo de uma execução do healthcheck (relógio e CPU, 5 execuções) | o `ping` | o TCP |
| CPU da VM, 12 amostras a cada 5 s | broker rodando com o `ping` / broker pausado | broker rodando com o TCP / broker pausado |
| tempo até `healthy` e até o listener aceitar, 3 restarts | o `ping` | o TCP |
| a ordem: `healthy` antes ou depois do listener | antes (2,7–3,1 s) | **depois** é o critério de aceite |

**Medido em 07/11/2026** (`~/.cache/buteco-agents/251-medicao/`; o roteiro e as correções dele estão no `tasks.md`, tarefas 0.1, 0.2 e 3.2):

| medição | antes (`ping`, 5s/5s/10) | depois (TCP, 10s/5s/6/30s) | guarda 1 (`ping` de volta) |
|---|---|---|---|
| custo de cada execução (CPU, 5 execuções) | 2,2 a 4,1 s | **0,004 a 0,006 s** | 2,3 a 2,6 s |
| relógio de cada execução | 0,83 a 1,75 s | **0,003 a 0,006 s** | 0,83 a 1,12 s |
| CPU da VM, broker de pé (média de 12 amostras) | 66,2% | **23,6%** | 62,9% |
| CPU da VM, broker pausado | 15,2% | 15,8% | 18,2% |
| CPU do contêiner (cgroup, 60 s ocioso) | 34,2% de um núcleo | **1,4%** | 36,8% |
| `healthy` depois do restart | +7,9 / +8,1 / +8,0 s | +11,0 / +10,6 / +10,7 s | +9,2 / +8,8 / +8,7 s |
| listener aceitando (dentro do contêiner) | +9,1 / +9,9 / +9,9 s | +9,9 / +9,8 / +9,9 s | +10,4 / +10,9 / +11,0 s |
| ordem | **`healthy` antes do listener** (3 de 3) | **`healthy` depois do listener** (3 de 3) | `healthy` antes (3 de 3) |

Todas as fases foram válidas, com o `CPU_Speed_Limit` em 100 do início ao fim. Saídas em `251-medicao/antes/`, `depois/` e `defeito-ping/`.

- **O estado "depois"** se aplica recriando o contêiner pelo podman-compose (`podman compose up -d rabbitmq`), com o healthcheck novo conferido no `podman inspect`.
- **O critério de aceite:**
  - a CPU da VM com o broker rodando fica perto da do broker pausado (hoje ~62% contra ~14%);
  - o `healthy` vem **depois** do listener nos três restarts;
  - uma suíte do `apps/frontend` com o broker **de pé** sai válida.
- **A medição também separa o custo do broker ocioso do custo do healthcheck:** é a diferença entre "rodando com o TCP" e "pausado".

### D5. O compose de produção, o deploy e o rollback

**O deploy:**
- **Mudar o `healthcheck` recria o contêiner do `rabbitmq`.** O redeploy de hoje (`docs/deployment.md`, passo 4: `up -d inbox api workers connectors frontend`) não inclui o `rabbitmq`, então o healthcheck novo só vale com um passo explícito:

  ```bash
  docker compose --env-file .env.prod -f docker-compose.prod.yml up -d rabbitmq
  ```

- **O efeito:** o broker reinicia, com ~12 s sem broker. As filas são `durable` e o volume `buteco-rabbitmq-data-prod` fica. Os publishers do `api` reconectam no próximo envio, e os consumidores dos `workers` reconectam pela recuperação automática do `RabbitMQ.Client` (ligada por padrão na 7.2.1). Se um consumidor não voltar, o `docker compose restart workers` o refaz. Isso fica na conferência do deploy (Q4).
- **Conferir:**
  - `docker compose … ps` mostra o `rabbitmq` `healthy`;
  - `docker inspect --format '{{json .Config.Healthcheck}}' <rabbitmq>` mostra o `bash` e o `/dev/tcp`;
  - `docker stats --no-stream` mostra a CPU do `rabbitmq` ociosa perto de zero;
  - uma task de agente de ponta a ponta prova que o consumidor voltou.

**O rollback:** devolver o bloco `healthcheck` anterior no `docker-compose.prod.yml` e rodar `up -d rabbitmq` de novo, com o mesmo reinício de ~12 s. Nenhum dado muda.

O `docs/deployment.md` ganha uma seção da #251 com esse passo, a conferência e o rollback.

### D6. Testes e guardas

**Não há teste na suíte nem CI que leia os arquivos de compose.** O repositório não tem `.github/workflows`, e nenhum teste lê `docker-compose*.yml`. Por decisão do dono (Q3), os guardas são **medições roteirizadas** (`~/.cache/buteco-agents/251-medicao/`). Cada uma é vista reprovar contra o defeito real, e a árvore volta pela cópia, com o sha conferido (convenção 15).

**Guarda 1, o `ping` de volta:**
- reintroduz o `rabbitmq-diagnostics -q ping` (5s/5s/10) no `docker-compose.yml` e recria o contêiner;
- a medição reprova em dois critérios: o `healthy` volta a vir **antes** do listener nos restarts, e a CPU da VM com o broker rodando volta a ficar longe da do broker pausado;
- depois, a árvore volta pela cópia, e o contêiner é recriado com o healthcheck novo.

**Guarda 2, a checagem reprova quando o broker para de aceitar** (complemento do dono na Q3; forma fechada na decisão de 07/11/2026, opção 1):

```
 healthy, 5672 aceitando ──rabbitmqctl stop_app──▶ o listener fecha (a 5672 recusa dentro do contêiner)
     │  cada checagem falha: FailingStreak 1, 2, … (nenhuma passa depois de a porta fechar)
     └─ FailingStreak = retries (6) ──▶ unhealthy       prazo de sanidade: 50 a 80 s
 unhealthy ──rabbitmqctl start_app──▶ o listener abre ──▶ healthy na checagem seguinte
```

- **O contrato é a contagem de falhas, e não o tempo:**
  - o `unhealthy` chega junto com a falha de número `retries` (`FailingStreak` = 6 no instante da transição);
  - nenhuma checagem passa depois de a porta fechar: nenhuma entrada nova do log de saúde com código 0, e o `FailingStreak` nunca volta a 0.

  O roteiro sonda o estado, o `FailingStreak` e a última entrada do log de saúde a cada 1 s, e a cadência das checagens é de ~11 s, então nenhuma transição escapa.
- **O tempo depende de como o motor conta o intervalo:**
  - **o Podman** conta a partir do **fim** da checagem anterior: com `interval` de 10 s, o log de saúde mostrou 10,84 / 11,00 / 11,00 / 11,00 s entre checagens. As 6 falhas levaram de 60,0 a 71,6 s em quatro rodadas, e a primeira janela do design (50 a 70 s), escrita com 10 s exatos, reprovou uma vez (71,6 s);
  - **o Docker**, que roda a produção, pode contar de outro jeito. Por isso o tempo não é o contrato, e a janela de **50 a 80 s** fica só como prazo de sanidade.
- **O defeito, para ver reprovar:** `["CMD", "true"]`. O contêiner fica `healthy` com a porta fechada, e o roteiro acusa as duas asserções: o `unhealthy` não vem (`FailingStreak` final 0), e checagens com código 0 aparecem depois de a porta fechar.

**Opções recusadas:**
- um teste estático num app (por exemplo, no `apps/api`) que lesse os composes: nenhum app é dono do arquivo;
- um script de conferência do compose: não há CI onde rodar sozinho.

## Árvore de arquivos proposta

```
docker-compose.yml          (o healthcheck do rabbitmq, D1 e D3)
docker-compose.prod.yml     (o mesmo bloco, D1 e D3)
docs/deployment.md          (a seção da #251: o passo `up -d rabbitmq`, a conferência e o rollback, D5)
docs/development.md         (uma linha, se ele citar o healthcheck ou a pausa nas suítes)
```

Nada em `apps/*` nem em `libs/`. Fora do repositório: `~/.cache/buteco-agents/251-medicao/` (os scripts e as saídas).

## Tamanho

Dois blocos de 5 linhas nos composes, uma seção no `deployment.md` e as entradas do `02` e do `CHANGELOG`. Cabe num PR pequeno, sem divisão.

## Risks / Trade-offs

- **[A checagem não autentica]** → Nem a de hoje autentica. O usuário padrão é criado no boot, antes do listener (D2). A falha de credencial aparece no log do `api` e dos `workers`, como hoje.
- **[A tag `rabbitmq:4.3-management` é móvel, e uma build futura poderia mudar de base]** → A checagem falha, e o broker fica `unhealthy`: o `api` e os `workers` não sobem, e isso é visível no `docker compose ps`. A conferência do deploy pega. Alternativa, se acontecer: o `check_port_connectivity` com 30 s.
- **[O reinício do broker no deploy]** → ~12 s sem broker, com filas duráveis e reconexão automática (D5). A conferência inclui uma task de ponta a ponta.
- **[O consumidor dos `workers` cai se a primeira conexão falhar]** → A checagem nova reduz a janela (o `healthy` só depois do listener), e o `restart: unless-stopped` cobre o resto. A resiliência no boot é a #253 (Q5).

## Migration Plan

Ver o D5. O desenvolvimento passa a usar o healthcheck novo no próximo `podman compose up -d rabbitmq`, que também recria o contêiner (~12 s).

## Decisões do dono (07/11/2026)

Na revisão do proposal e do design, todas como recomendadas, com dois complementos:

| | pergunta | decisão | onde |
|---|---|---|---|
| Q1 | Qual checagem? | TCP na 5672 dentro do contêiner, `["CMD", "bash", "-c", "</dev/tcp/127.0.0.1/5672"]`. **Complemento:** conferir o `bash` na tag exata da imagem dos dois composes (`podman run --rm <imagem> bash -c 'echo ok'`), e parar se a imagem fosse Alpine. Conferido: Ubuntu 24.04.4, bash 5.2.21 | D1 |
| Q2 | Quais parâmetros? | `interval` 10s, `timeout` 5s, `retries` 6, `start_period` 30s, sem `start_interval` | D3 |
| Q3 | Qual guarda? | a medição roteirizada (o `ping` de volta). **Complemento:** o guarda também prova que a checagem reprova: com `rabbitmqctl stop_app`, a porta fecha e o contêiner vai a `unhealthy` dentro do esperado (`interval × retries`); com `start_app`, volta a `healthy`. **Na implementação (opção 1):** o contrato do guarda 2 é a contagem (`FailingStreak` = `retries`, nenhuma checagem passando depois de a porta fechar), com 50 a 80 s só como prazo de sanidade, porque o Podman conta o intervalo do fim da checagem anterior (~11 s) e o Docker pode contar de outro jeito | D6 |
| Q4 | Quando, e com que conferência, o deploy de produção? | numa janela sem uso, com `up -d rabbitmq` explícito e uma task de agente de ponta a ponta para provar que o consumidor dos `workers` voltou | D5 |
| Q5 | A resiliência do consumidor dos `workers` no boot? | issue sem milestone: **#253** | Non-Goals |

## Open Questions

Nenhuma.
