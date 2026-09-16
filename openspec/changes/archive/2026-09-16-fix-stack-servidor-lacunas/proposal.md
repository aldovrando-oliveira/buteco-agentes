## Why

Existe na raiz do repositório um `RUNBOOK-LOCAL.md` **não versionado**, escrito
por quem subiu o stack de servidor numa máquina real. Ele documenta dez contornos
manuais. **Oito são defeitos na origem** — o runbook está ensinando o operador a
consertar à mão, a cada instalação, coisas que o repositório deveria entregar
certas.

O critério desta change é esse: *"em vez de documentar para o usuário fazer à
mão, corrigir no ponto de origem"*. Nada aqui é funcionalidade nova. Tudo é
lacuna entre o que os apps servem e o que o stack entrega.

Três frentes, com urgências diferentes e independentes entre si.

### 1. Segredo commitado em arquivo versionado

`apps/workers/src/Buteco.Workers/appsettings.Development.json:18-19` traz um
endpoint de gateway interno e uma **chave de API com aparência de válida**, em
arquivo rastreado. Entrou em `19a5bec` (12/09/2026) e **está no histórico** —
removê-la do arquivo não a remove de lá.

O próprio runbook registra que *"essa chave vale rotacionar de todo jeito"*, e
descreve o contorno: sobrescrever `OpenAI__BaseUrl`/`OpenAI__ApiKey` na linha de
comando a cada `dotnet run`, porque o gateway commitado não é alcançável de fora
da rede onde foi escrito — sem isso, **toda task termina `failed`**.

É o item mais urgente da change e o único de segurança.

### 2. Três prefixos de API que o nginx do stack não roteia

`apps/frontend/deploy/nginx.conf` roteia `agents|providers|mcp-servers|auth` para
`apps/api` e `channels|contacts|sessions|webhooks` para `apps/inbox`. Os dois
apps servem mais que isso, e o que sobra cai no fallback de SPA e responde
**`200` com HTML** onde o consumidor espera JSON:

| Prefixo ausente | Destino | Consequência |
|---|---|---|
| `internal` | `apps/inbox` | **push notification nunca chega.** `apps/workers` entrega em `${PUBLIC_DOMAIN}/internal/push-notifications`; a task completa, o agente responde, e **nada sai no canal**. Falha silenciosa ponta a ponta |
| `knowledge-bases` | `apps/api` | o painel de bases de conhecimento recebe `index.html` em vez de JSON |
| `knowledge-index` | `apps/api` | a aba de diagnóstico do índice quebra — `apps/frontend/src/features/knowledge-bases/api/knowledgeIndexApi.ts:20` chama `/knowledge-index/diagnostics` |

**O terceiro não está no runbook.** Ele documenta um patch para dois prefixos; a
enumeração dos prefixos reais dos dois apps devolveu três. Aplicar o patch do
runbook como está deixaria a aba de diagnóstico quebrada — e é exatamente por isso
que a correção pertence à origem, onde se pode enumerar em vez de lembrar.

A requirement viva `server-deployment` **congelou a lista incompleta**: o cenário
*"Requisições de API são roteadas para o app correto"* enumera os mesmos oito
prefixos do `nginx.conf`. A spec foi escrita contra a implementação, e as duas
derivaram juntas do que os apps de fato servem.

### 3. Configuração que o compose de produção não entrega

- **`Embedding__*` não chega em `apps/workers`.** `docker-compose.prod.yml` não
  passa a seção, e `appsettings.json` (Production) não tem default — só o
  `appsettings.Development.json`, que não é lido no contêiner. O boot **passa**
  (índice vazio não diverge de nada) e a **primeira indexação** falha, com modelo
  vazio e dimensão 0.
- **`Anthropic__ApiKey` e `Gemini__ApiKey` não são interpolados.**
  `.env.prod.example:47-48` oferece as duas variáveis e `docs/configuration.md:246`
  promete o mapeamento, mas nenhum serviço do compose as consome. Já registrado
  como item aberto em `02-HISTORICO_E_STATUS.md:5266-5273`.
- **`docker-compose.prod.yml` não declara `env_file:`.** O Compose lê `.env`,
  nunca `.env.prod`. Sem `--env-file .env.prod` explícito, ou o stack de produção
  come as variáveis de dev, ou todo `${VAR}` vira string vazia e **o Postgres sobe
  sem senha configurada**. Não está em `docs/deployment.md`.
- **As portas de dev divergem entre arquivo versionado e exemplo.** Os
  `appsettings.Development.json` apontam Postgres `15532` e RabbitMQ `15772`; o
  `.env.example` traz `5432`/`5672`. Copiar o exemplo como manda
  `docs/development.md` quebra os três apps, e o sintoma parece banco fora do ar.

## What Changes

Código e configuração. Nenhuma funcionalidade nova, nenhuma rota nova.

- `apps/workers/src/Buteco.Workers/appsettings.Development.json` — o gateway e a
  chave saem do arquivo versionado. **A rotação da chave é passo operacional
  fora do repositório**, e a tarefa existe para não ser esquecida.
- `apps/frontend/deploy/nginx.conf` — os três prefixos entram, cada um no bloco
  do app que o serve.
- `docker-compose.prod.yml` — a seção `Embedding` e as duas chaves de provedor
  passam a ser interpoladas; `env_file:` passa a ser declarado.
- `.env.prod.example` e `.env.example` — as variáveis novas, e as portas
  alinhadas com o que os `appsettings` versionados esperam.
- `docs/deployment.md` e `docs/configuration.md` — o que a correção tornar
  verdadeiro, e só isso.

## Non-Goals

- **Reescrever ou versionar o `RUNBOOK-LOCAL.md`.** Ele é nota de máquina, e a
  decisão do dono é apagá-lo quando não servir mais. Esta change torna oito dos
  seus contornos desnecessários; o que sobrar continua sendo nota de máquina.
- **`/health` no nginx.** Os healthchecks rodam dentro de cada contêiner contra o
  próprio processo, não atravessam o nginx. Fora de escopo por não ser defeito.
- **Réplicas de `apps/workers`** — a contradição entre delegação exigir duas
  instâncias e o compose proibir réplica continua aberta
  (`02-HISTORICO_E_STATUS.md:5235-5264`). É decisão de arquitetura, não lacuna de
  entrega.
- **Purgar a chave do histórico do git.** Reescrever histórico é decisão do dono
  do repositório, com custo próprio. A change remove do estado atual e **exige a
  rotação**, que é o que efetivamente invalida a chave vazada.
- **O defeito D8 em `nginx.conf:5`** (caminho de change arquivada sem o prefixo
  `archive/`) é achado de documentação de outra linha de trabalho. Fica
  registrado, não corrigido aqui.

## Impact

- **Código/configuração:** `apps/frontend/deploy/nginx.conf`,
  `docker-compose.prod.yml`, `apps/workers/.../appsettings.Development.json`,
  `.env.prod.example`, `.env.example`, e os `appsettings.Development.json` de
  `apps/api`/`apps/inbox` se a decisão de portas for alinhá-los.
- **Specs:** `server-deployment` (dois requirements MODIFIED, um ADDED) e
  `local-dev-environment` (um ADDED).
- **Operacional, fora do repositório:** rotação da chave exposta. **Sem isso, a
  correção no arquivo não resolve nada** — a chave permanece válida e continua
  no histórico.
- **Risco de regressão:** baixo no nginx (adição de prefixo a regex existente),
  **médio no `env_file:`** — muda como o Compose resolve variáveis para quem já
  roda o stack com `--env-file`. Ver `design.md`.
