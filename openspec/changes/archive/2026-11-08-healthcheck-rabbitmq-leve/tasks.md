Um guarda marcado **(guarda)** só conta depois de ter reprovado contra o defeito que ele existe para pegar (convenção 15). Para cada um:
1. o defeito é reintroduzido só no trecho nomeado do `docker-compose.yml`, a partir de uma cópia feita antes;
2. o contêiner é recriado, e o roteiro roda e reprova;
3. a árvore volta pela cópia, com o sha conferido, e o contêiner é recriado com o healthcheck da change.

A tarefa registra:
- o comando;
- a saída inteira, em `~/.cache/buteco-agents/251-medicao/`;
- o critério que reprovou;
- o "voltou: docker-compose.yml", com o sha igual ao de antes.

**O broker de desenvolvimento** roda como projeto `buteco-agents`, com o `.env` do repositório principal: as portas publicadas não são as padrão (a 5672 vai para a 15772). Toda recriação a partir do worktree usa:

```
podman compose -p buteco-agents --env-file /Users/aldovrando/Projetos/buteco-agents/.env \
  -f /Users/aldovrando/Projetos/buteco-agentes-251/docker-compose.yml up -d rabbitmq
```

Sem o `-p` e o `--env-file`, o compose criaria outro projeto ou outras portas, e os apps locais deixariam de conectar. Depois da change, o contêiner é recriado de novo **pelo compose do repositório principal**, e só quando o `main` tiver a mudança.

**A medição** é feita com o host assentado: CPU em 100 e load de 1 min abaixo de 6. A porta publicada no host **não** serve para medir, porque o encaminhador do Podman aceita na hora. Toda sondagem da 5672 é feita dentro do contêiner (`podman exec … bash -c '</dev/tcp/127.0.0.1/5672'`).

**A suíte** roda pelo `~/.cache/buteco-agents/bin/suite-251.sh`, com a regra assimétrica.

**Parada** (convenção 9):
- **parar** quando uma premissa falsa mudar comportamento ou contrato, ou quando a correção reduzir cobertura;
- **seguir e registrar** quando a correção só acrescentar medição ou cenário para pôr um guarda no lugar certo, ou só tocar comentário ou documentação.

## 0. Preparação

- [x] 0.1 (scripts locais) `~/.cache/buteco-agents/251-medicao/medir.sh <rótulo>`, a partir do `tempo2.py` e das medições da exploração. Ele grava em `251-medicao/<rótulo>/`:
  - o `podman inspect` do `Healthcheck` do contêiner;
  - o custo de 5 execuções do comando do healthcheck, relógio e CPU (`bash -c 'time …'` dentro do contêiner);
  - a CPU da VM em 12 amostras a cada 5 s, com o broker rodando, e depois em 12 com o broker pausado (`podman pause`, com `unpause` garantido por `trap`);
  - 3 restarts, com os instantes do fim do restart, da 5672 aceitando dentro do contêiner, do `healthy` e do "Time to start RabbitMQ" do log;
  - o `uptime` e o `CPU_Speed_Limit` no início e no fim.

  Conferido com uma rodada no estado de hoje (`antes`), que precisa reproduzir a ordem medida na exploração: o `healthy` antes do listener.
  **Feito (07/11/2026):** `251-medicao/medir.sh` → `medir.py`. **Duas correções no caminho**, as duas registradas:
  - **as fases passaram a esperar a CPU em 100 e o load de 1 min abaixo de 6, e a amostrar o `CPU_Speed_Limit` a cada 2 s.** A fase só vale com 100 do início ao fim, com até 3 tentativas. A primeira rodada (`antes-invalida-cpu23/`) foi feita com o host estrangulado (23): restarts de 45 a 60 s, e a VM com o broker pausado em ~47%;
  - **a métrica dos segundos de CPU do cgroup do contêiner** (`usage_usec` do `cpu.stat`, em 60 s) entrou porque a %CPU da VM no `top` é relativa ao núcleo desacelerado. Os `exec` do healthcheck entram nesse cgroup (conferido: um `ping` manual soma +2,5 s). **O relógio da VM estava 4,5 min atrasado** (o `--since` dos logs não achava o "Time to start"): o roteiro acerta o relógio no início, como o `suite-*.sh`, e lê os logs por `--tail`. A segunda rodada (`antes-r2-sem-cgroup/`) foi descartada por isso.
- [x] 0.2 (scripts locais) `~/.cache/buteco-agents/251-medicao/parar-app.sh <rótulo>`, para o guarda 2. Ele:
  1. confere o contêiner `healthy`;
  2. roda `rabbitmqctl stop_app` dentro do contêiner e registra o instante;
  3. sonda a 5672 dentro do contêiner e o estado de saúde a cada 1 s, até o `unhealthy` ou 120 s;
  4. roda `rabbitmqctl start_app` e sonda até o `healthy` ou 60 s.

  Grava os instantes e o veredito: **PASSA** quando o `unhealthy` vem entre 50 e 70 s depois do `stop_app` e o `healthy` volta até 15 s depois de a porta reabrir; senão, **REPROVA**, com o motivo. O `start_app` roda também em falha, pelo `trap`.
  **Feito:** `251-medicao/parar-app.sh` → `parar-app.py`. **Correção na primeira rodada do defeito:** com `["CMD", "true"]`, o contêiner fica `healthy` antes de o boot terminar, e o `stop_app` respondeu `rc=69` (`defeito-true-r1-boot-incompleto/`). O roteiro passou a esperar o `healthy` **e** a 5672 aceitando antes do `stop_app`, e a reprovar quando o `stop_app` falha.
- [x] 0.3 (scripts locais) `~/.cache/buteco-agents/bin/suite-251.sh`, cópia do `suite-250.sh`, com a pausa do broker opcional: `PAUSAR_RABBIT=0` não pausa, e o padrão continua pausando. O `-estado.txt` registra a escolha.

  **Feito:** `~/.cache/buteco-agents/bin/suite-251.sh`, com `PAUSAR_RABBIT` (o padrão `1` pausa; `0` roda com o broker de pé), registrado no `-estado.txt`.
## 1. Medição de antes

- [x] 1.1 (compose de desenvolvimento) `medir.sh antes`, com o healthcheck de hoje (`rabbitmq-diagnostics -q ping`, 5s/5s/10), no contêiner criado pelo compose do repositório principal. Ela confirma a medição da exploração (`251-medicao/antes-20261107.txt`), agora com o roteiro: o custo, a CPU da VM rodando e pausado, e o `healthy` antes do listener.

  **Feito**, em `251-medicao/antes/`, com todas as fases válidas (CPU em 100), às 20:05–20:10:
  - `ping`: 2,2 a 4,1 s de CPU por execução;
  - VM rodando 66,2% e pausado 15,2%;
  - cgroup: **34,2% de um núcleo**;
  - restarts: `healthy` em +7,9 a +8,1 s e listener em +9,1 a +9,9 s, ou seja, **`healthy` antes do listener nos 3**.
## 2. O healthcheck novo (D1, D3)

- [x] 2.1 (deploy) `docker-compose.prod.yml`: o bloco `healthcheck` do `rabbitmq` passa a `["CMD", "bash", "-c", "</dev/tcp/127.0.0.1/5672"]`, `interval: 10s`, `timeout: 5s`, `retries: 6`, `start_period: 30s`. Um comentário curto diz o porquê: o CLI sobe o runtime Erlang a cada execução, e o `healthy` passa a esperar o listener (#251).
  **Feito:** o bloco novo, com o comentário.
- [x] 2.2 (dev) `docker-compose.yml`: o mesmo bloco, idêntico, com o mesmo comentário.
  **Feito:** o mesmo bloco, idêntico (diff do trecho vazio).
- [x] 2.3 (deploy) A forma dos dois arquivos: o `podman compose … config` dos dois, com o `--env-file` do `.env.prod.example` no de produção, sem erro. O bloco do `rabbitmq` sai igual nos dois (diff do trecho).

  **Feito:** `podman compose … config` dos dois sem erro. O de desenvolvimento usa o `.env` principal (as portas 15772 e 15872 mantidas), e o de produção o `.env.prod.example`. O `healthcheck` sai igual nos dois.
## 3. Medição de depois (D4)

- [x] 3.1 (compose de desenvolvimento) O contêiner recriado com o compose do worktree (o comando do cabeçalho). O `podman inspect` mostra o `Test`, o `Interval` de 10 s, o `Timeout` de 5 s, os `Retries` de 6 e o `StartPeriod` de 30 s.
  **Feito:** `podman compose -p buteco-agents --env-file …/buteco-agents/.env -f …/buteco-agentes-251/docker-compose.yml up -d --force-recreate --no-deps rabbitmq`. O `inspect` mostra o `Test` novo, 10 s, 5 s, 6 e 30 s; as portas são as mesmas; o Postgres não foi tocado.
- [x] 3.2 (compose de desenvolvimento) `medir.sh depois`. **Critérios de aceite:**
  - o custo de cada execução, em milissegundos;
  - a CPU da VM com o broker rodando **perto da medida com o broker pausado** (hoje ~62% contra ~14%);
  - o `healthy` **depois** do listener nos 3 restarts.

  A diferença entre "rodando" e "pausado" no `depois` é o custo do broker ocioso, sem o healthcheck. Ela fica registrada.
  **Feito**, em `251-medicao/depois/`, com todas as fases válidas:
  - custo de **3 a 6 ms** por checagem;
  - VM rodando **23,6%** e pausado 15,8%;
  - cgroup: **1,4% de um núcleo**;
  - restarts: listener em +9,8 a +9,9 s e `healthy` em +10,6 a +11,0 s, ou seja, **`healthy` depois do listener nos 3**.

  Os três critérios de aceite foram atendidos. Os 7,8 pontos que sobram na VM (rodando contra pausado) são o broker ocioso.
- [x] 3.3 (compose de desenvolvimento) A tabela de antes e depois, lado a lado, no `tasks.md` e no design (D4).

  **Feito (07/11/2026):**

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

  A mesma tabela foi para o D4 do design.
## 4. Guardas (D6)

- [x] 4.1 (compose de desenvolvimento) **Guarda 2, a checagem reprova com o app parado:** `parar-app.sh depois` com o healthcheck da change → **PASSA**: a porta fecha, o `unhealthy` vem entre 50 e 70 s, e o `healthy` volta depois do `start_app`.
  **(guarda)** Defeito: o `test` trocado por `["CMD", "true"]` no `docker-compose.yml`, com o contêiner recriado. `parar-app.sh defeito-true` → **REPROVA** (o contêiner fica `healthy` com a porta fechada, e o `unhealthy` não vem em 120 s). Depois: a volta pela cópia, com o sha conferido, e o contêiner recriado.
  **PARADO (07/11/2026): premissa falsa no prazo do `unhealthy` (convenção 9).**
  - **O defeito foi visto reprovar** (`defeito-true/`, depois da correção da 0.2): `stop_app` com `rc=0`, a porta fechada em +1,8 s, e o contêiner **`healthy` por 120 s**. O roteiro reprovou com "o unhealthy não veio em 120 s". Voltou: `docker-compose.yml` (sha igual: `740816b4`), e o contêiner foi recriado com o healthcheck da change.
  - **O caso que deveria passar saiu fora da janela de 50 a 70 s numa de quatro rodadas:**

    | rodada | `unhealthy` depois do `stop_app` |
    |---|---|
    | `depois` (roteiro antigo) | 63,5 s |
    | `depois` | **71,6 s** (REPROVA) |
    | `depois-r2` | 64,8 s |
    | `depois-r3` | 60,0 s |

    Nas quatro, a porta fechou em 1,5 a 1,8 s, e o `healthy` voltou 9 a 10 s depois do `start_app`.
  - **A causa medida:** com `interval` de 10 s, o podman roda uma checagem a cada **11,0 s** (log de saúde: 10,84 / 11,00 / 11,00 / 11,00 s). O intervalo conta do fim da anterior, mais ~1 s. A janela de 50 a 70 s do design (D6) e do cenário "O app parado leva a unhealthy no prazo" da spec `server-deployment` foi escrita com 10 s exatos, e o teto fica apertado.
  - **Por que é parada:** a correção óbvia, alargar o teto, **afrouxa a tolerância** de uma asserção. Isso é decisão do dono (memória `criterio-de-parada-premissa-falsa`).
  **Feito, com a asserção pela contagem (decisão do dono, opção 1, depois da parada acima).** O `parar-app.py` passou a afirmar o mecanismo:
  - o `unhealthy` chega com o `FailingStreak` igual aos `Retries` do contêiner;
  - nenhuma checagem passa depois de a porta fechar: nenhuma entrada nova com código 0 no log de saúde, e o `FailingStreak` nunca volta a 0;
  - a janela de 50 a 80 s fica só como prazo de sanidade.

  O roteiro sonda tudo a cada 1 s e acerta o relógio da VM no início. A spec (cenário "O app parado leva a unhealthy pela contagem de falhas") e o D6 foram atualizados. A versão anterior do roteiro está em `parar-app.py.v2-janela`.
  - **Com o healthcheck da change** (`contagem-change/`): **PASSA**. A porta fechou em +3,1 s; o `FailingStreak` subiu 0→1 em +13,7 s e depois 2, 3, 4, 5 e 6 em +24,1 / +35,8 / +45,1 / +56,7 / +67,1 s; o `unhealthy` chegou com `FailingStreak` 6, em 67,1 s; nenhuma checagem passou depois da porta fechada; e o `healthy` voltou 9,6 s depois do `start_app`.
  - **(guarda) Visto reprovar** (`contagem-defeito-true/`): `["CMD", "true"]`. O `stop_app` deu `rc=0` e a porta fechou em +1,7 s, mas o contêiner ficou `healthy` por 120 s. **REPROVA nas duas asserções:** "o unhealthy não veio em 120 s (FailingStreak final 0)" e "checagem com código 0 depois de a porta fechar", com 11 entradas. Voltou: `docker-compose.yml` (sha igual: `740816b4`), e o contêiner foi recriado com o healthcheck da change (`inspect` conferido).
- [x] 4.2 (compose de desenvolvimento) **Guarda 1, o `ping` de volta.**
  **(guarda)** Defeito: o `test` de volta a `["CMD", "rabbitmq-diagnostics", "-q", "ping"]`, com `interval: 5s`, `timeout: 5s` e `retries: 10`, no `docker-compose.yml`, com o contêiner recriado. `medir.sh defeito-ping` **reprova nos dois critérios do 3.2**: o `healthy` antes do listener, e a CPU da VM rodando longe da pausada. Depois: a volta pela cópia, com o sha conferido, o contêiner recriado, e o `podman inspect` confere o healthcheck da change.

  **Feito.** **(guarda) Visto reprovar** (`defeito-ping/`, todas as fases válidas): com o bloco antigo de volta (o `ping`, 5s/5s/10, sem `start_period`) e o contêiner recriado, a medição reprova nos dois critérios do 3.2:
  - **o `healthy` antes do listener nos 3 restarts** (+9,2 contra +10,4; +8,8 contra +10,9; +8,7 contra +11,0 s);
  - **a CPU da VM rodando longe da pausada:** 62,9% contra 18,2%, com o cgroup a 36,8% de um núcleo.

  Voltou: `docker-compose.yml` (sha igual: `740816b4`). O contêiner foi recriado, e o `inspect` mostra de novo o `bash` com o `/dev/tcp`, 10 s, 5 s, 6 e 30 s.
## 5. A suíte com o broker de pé

- [x] 5.1 (apps/frontend) A suíte `frontend` do worktree da #251, com `PAUSAR_RABBIT=0` e o broker **de pé**, com o healthcheck novo. Ela tem de sair válida pela regra assimétrica; se sair inconclusiva, repete, até 3 vezes. O `-estado.txt` mostra o `rabbitmq` em `podman ps` e nenhuma pausa. É o cenário "Suíte válida com o broker de pé" da `local-dev-environment`. A #251 não muda código do `apps/frontend`, então a rodada mede só o ambiente; a comparação por nome contra a baseline da #250 (2.166 casos em `a852946`) confere que nada mudou.

  **Feito (07/11/2026):** `~/.cache/buteco-agents/251-frontend-broker-de-pe-r1/`, com `PAUSAR_RABBIT=0`. O `-estado.txt` mostra o `buteco-agents_rabbitmq_1` em `podman ps`, "PAUSAR_RABBIT=0 (broker running healthy)" e nenhuma linha de pausa.
  - **APROVADA na primeira rodada:** 2.166 de 2.166 em 168 arquivos, em 152 s. Começou com load 5,35 e CPU em 100; a CPU mínima foi 51.
  - **Por nome:** o `nomes.txt` é **idêntico** ao da rodada válida da baseline `a852946` da #250 (`250-alterna-20261107-1815/base-1/nomes.txt`). Nada sumiu, mudou de nome ou deixou de passar.
  - **O contraste:** com o `ping`, as duas rodadas da #250 com o broker de pé levaram 616 s e 606 s, com a CPU caindo até 23, e saíram inconclusivas. Com o broker pausado, 147 s e 160 s.
## 6. Documentação e deploy (D5)

- [x] 6.1 (docs) `docs/deployment.md`, seção nova da #251:
  - o passo explícito `docker compose --env-file .env.prod -f docker-compose.prod.yml up -d rabbitmq`, que recria o broker (~12 s sem ele, filas duráveis), numa janela sem uso (Q4);
  - a conferência: `ps` com o `rabbitmq` `healthy`; `docker inspect` do `Healthcheck`; `docker stats --no-stream` com a CPU do `rabbitmq` ociosa perto de zero; **uma task de agente de ponta a ponta**, que prova que o consumidor dos `workers` voltou, e o `docker compose restart workers` se não voltar;
  - o rollback: o bloco anterior e o `up -d rabbitmq` de novo;
  - a nota de que o redeploy de rotina (passo 4 da seção 2) não recria o `rabbitmq`.
  **Feito:** a seção "Healthcheck do RabbitMQ (#251)" no `docs/deployment.md`, antes da #246, com:
  - o passo `up -d rabbitmq` numa janela sem uso;
  - a conferência em 4 passos, com a task de ponta a ponta e o `restart workers` se ela ficar parada;
  - o rollback;
  - a conferência do `bash` ao trocar a imagem.
- [x] 6.2 (docs) `docs/development.md`: se ele citar o healthcheck do broker ou a pausa nas suítes, a linha é atualizada. Se não citar, a tarefa registra a conferência.
  **Feito (conferência):** o `docs/development.md` não cita o healthcheck do broker nem a pausa nas suítes; só as portas 15772 e 15872, que não mudam. Nada a atualizar.
- [x] 6.3 (docs) `CHANGELOG.md` (Changed: o healthcheck do RabbitMQ) e `02-HISTORICO_E_STATUS.md` (a entrada da change, com a medição de antes e depois), antes do archive.

  **Feito (07/11/2026):**
  - `CHANGELOG.md`: "O healthcheck do RabbitMQ deixa de ocupar um núcleo e passa a esperar o broker aceitar conexão" em Changed;
  - `02-HISTORICO_E_STATUS.md`: a entrada `healthcheck-rabbitmq-leve`, com as decisões, as premissas que caíram, a medição de antes e depois, os guardas, a suíte e o "Para as próximas".
## 7. Registros

- [x] 7.1 (issues) Com o ok do dono:
  - na **#226** e na **#176**: o healthcheck novo e a medição do depois, e o fim da necessidade de pausar o broker nas suítes;
  - na **#253**: o `healthy` agora espera o listener, o que reduz a janela que ela trata, sem eliminá-la.

  O PR leva `Closes #251`.
  **Feito (07/11/2026)**, com o ok do dono no pedido de fechamento:
  - na [#226](https://github.com/aldovrando-oliveira/buteco-agentes/issues/226#issuecomment-6714740105) e na [#176](https://github.com/aldovrando-oliveira/buteco-agentes/issues/176#issuecomment-6714740318): o healthcheck novo, a CPU do contêiner de 34,2% para 1,4%, a suíte aprovada com o broker de pé, e que não é mais preciso pausá-lo; o problema de cada issue continua de pé;
  - na [#253](https://github.com/aldovrando-oliveira/buteco-agentes/issues/253#issuecomment-6714740508): o `healthy` agora espera o listener, o que fecha a janela do boot do stack, mas não os outros casos dela. O número da diferença foi corrigido no próprio comentário: 1,1 a 2,0 s na medição de antes, 2,7 a 3,1 s na exploração.

  O PR leva `Closes #251`.
- [ ] 7.2 (compose de desenvolvimento) Depois do merge, o contêiner é recriado pelo compose do repositório principal, já com o `main` atualizado, e o `podman inspect` confere o healthcheck. É a limpeza pós-merge, registrada no fechamento.
