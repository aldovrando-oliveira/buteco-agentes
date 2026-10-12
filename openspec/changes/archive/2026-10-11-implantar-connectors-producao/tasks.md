Tudo roda no worktree `/Users/aldovrando/Projetos/buteco-agents-119`, branch
`feat/119-implantar-connectors`. Nenhuma tarefa acessa servidor ou ambiente real:
o deploy é do mantenedor. Nenhuma tarefa muda código de app; se uma delas achar
falta de código, para e reporta.

## 1. Registro da decisão da #117

- [x] 1.1 [GitHub, #117] Comentário com a decisão do mantenedor (opção 1: chave
  compartilhada, risco aceito, gatilho para rever). A #117 continua aberta, com
  `aguardando gatilho`. Feito na abertura da change:
  https://github.com/aldovrando-oliveira/buteco-agentes/issues/117#issuecomment-5999044626
- [x] 1.2 [documentação, `docs/deployment.md`] Seção "Risco aceito: chave de
  assinatura compartilhada por três processos (#117)", ao lado do risco aceito do
  `changeme`. Feito na abertura da change, a pedido do mantenedor.

## 2. Compose de produção e `.env.prod.example` (stack de deploy)

- [x] 2.1 [stack de deploy, `docker-compose.prod.yml`] Serviço `connectors` (D1,
  D2): `build` de `apps/connectors/Dockerfile`, `restart: unless-stopped`,
  `depends_on: api: service_healthy`, sem porta publicada, sem `deploy.replicas`;
  `Auth__TokenSigningKey: ${AUTH_TOKEN_SIGNING_KEY}`,
  `GoogleDrive__ServiceAccountKeyBase64: ${GOOGLE_DRIVE_SERVICE_ACCOUNT_KEY_BASE64:-}`,
  `Api__BaseUrl: "http://api:8080"`. Comentário de non-goals do topo: o
  `apps/connectors` entre os serviços que nunca usam `replicas > 1`, com o motivo
  (lock em memória do ciclo, #105).
- [x] 2.2 [stack de deploy, `docker-compose.prod.yml`, serviço `api`]
  `Connectors__BaseUrl: "http://connectors:8080"`, com comentário curto (R1).
- [x] 2.3 [stack de deploy, `docker-compose.prod.yml`, serviço `frontend`]
  `VITE_CONNECTORS_BASE_URL: ""` em `build.args`. **Sem** `depends_on` no
  `connectors` (D1, ajuste da revisão): o painel não depende da saúde dele. **No
  mesmo diff da 3.1** (D4).
- [x] 2.4 [stack de deploy, `.env.prod.example`]
  `GOOGLE_DRIVE_SERVICE_ACCOUNT_KEY_BASE64=` vazia, com comentário (o que é,
  `base64 -i chave.json | tr -d '\n'`, opcional, pré-requisitos da etapa 0, nunca
  versionar); comentário de `AUTH_TOKEN_SIGNING_KEY` com os três processos e o
  risco aceito (#117).
- [x] 2.5 [stack de deploy] `docker compose -f docker-compose.prod.yml config`
  (com o `.env` de teste da 5.1) processa sem erro e mostra os oito serviços; sem
  o `--env-file`, continua reprovando com a mensagem do `:?`.
  **Medido (podman-compose 1.6.0):** com o `.env` de teste, `exit=0` e os oito
  serviços (`api connectors frontend inbox migrator postgres rabbitmq workers`);
  `Connectors__BaseUrl`/`Api__BaseUrl` interpolados, chave do Google `''`,
  `VITE_CONNECTORS_BASE_URL=` no build. Sem o `--env-file`: `exit=1`, `required
  variable POSTGRES_USER is missing a value: defina POSTGRES_USER — use --env-file
  .env.prod`.

## 3. Nginx do stack (`apps/frontend`, configuração de deploy)

- [x] 3.1 [`apps/frontend`, `deploy/nginx.conf`] Bloco
  `location ~ ^/(connectors)(/|$)` para `connectors:8080`, com o desvio de
  `Sec-Fetch-Mode: navigate`, `set $connectors_upstream` e os mesmos
  `proxy_set_header` (D3). Sem diretiva de tempo.
- [x] 3.2 [`apps/frontend`, `deploy/nginx.conf`] Comentário do topo: três apps
  roteados, grep de reconferência do `apps/connectors`, ruído conhecido
  (`/sync/knowledge-bases` é saída; `/providers` é sub-rota do grupo
  `/connectors`), e a nota dos limites de tempo da D3 (35 s do `apps/api` contra os
  60 s do padrão).
- [x] 3.3 [`apps/frontend`] Reconferir os prefixos dos três apps pelos greps do
  comentário; todo prefixo servido, exceto `/health`, tem bloco.
  **Medido:** `apps/connectors` serve só `/connectors` (`/providers` é sub-rota do
  grupo, `/sync` é saída), coberto pelo bloco novo. `apps/inbox` sem mudança.
  `apps/api`: `/connectors` é chamada de saída (`ConnectorsFolderClient.cs:50`),
  mas **`/sync` é servido** (`KnowledgeSyncEndpoints.cs:33`, desde a #102) e não
  tem bloco. Defeito pré-existente, fora do escopo desta change (corrigir pede
  decidir entre exceção declarada na spec ou rota no nginx): **#152**. Nada quebra
  hoje: o único consumidor chama o `apps/api` pela rede interna.

## 4. Documentação

- [x] 4.1 [documentação, `docs/deployment.md`, §1] Primeiro deploy: o
  `connectors` na ordem de subida (depois do `api` saudável; `frontend` por
  último); a variável do Google opcional; pré-requisitos da etapa 0 (Drive API
  ativada no projeto da service account — sem ela `502 api-not-configured`; pasta
  compartilhada com o e-mail da conta como Leitor; download permitido para
  leitores — sem ele `download-blocked`).
- [x] 4.2 [documentação, `docs/deployment.md`, §3] Tabela de variáveis:
  `AUTH_TOKEN_SIGNING_KEY` em api, inbox e connectors; linha nova de
  `GOOGLE_DRIVE_SERVICE_ACCOUNT_KEY_BASE64`; nota dos dois endereços internos fixos
  (`Connectors__BaseUrl`, `Api__BaseUrl`) e de `VITE_CONNECTORS_BASE_URL`, que não
  vão no `.env.prod`. Edição curta.
- [x] 4.3 [documentação, `docs/deployment.md`, §2 e §4] Redeploy (D5): passo 0
  condicional com os dois comandos prontos (a migração da #138 já rodou? quantos
  `Pending`?) e registro no `02`; `connectors` no passo 4; nota do `restart
  connectors` quando o teto de conteúdo do `apps/api` mudar. Non-goals: o
  `apps/connectors` na lista de réplica única, com o motivo (R3).
- [x] 4.4 [documentação, `docs/deployment.md`, seção nova] "Verificação depois de
  implantar o `apps/connectors`" (D6): as oito checagens com o critério de
  aprovação; o comando da troca de token de dentro do contêiner e o remédio do IPv6
  (D7); o que significa `/connectors` respondendo `502` (o `apps/connectors` não
  subiu; ver o log dele; a chave do Google é a causa mais provável); o roteiro do
  `access-denied` em pasta de organização citando a #114.
  Checagem do prefixo `/connectors` também na lista da verificação do frontend.
- [x] 4.5 [documentação, `docs/configuration.md`] Seção "Stack de servidor":
  `AUTH_TOKEN_SIGNING_KEY` com três processos; linha da variável do Google;
  frase de que `Connectors__BaseUrl`, `Api__BaseUrl` e `VITE_CONNECTORS_BASE_URL`
  são fixos no compose. Restrições entre processos: o texto "dois serviços" do
  parágrafo do compose passa a cobrir três. O parágrafo de
  `VITE_CONNECTORS_BASE_URL` deixa de dizer "fica ausente até a #119".
- [x] 4.6 [documentação, `02-HISTORICO_E_STATUS.md` e `CHANGELOG.md`] Seção da
  change (o que entrou, o que foi medido na verificação local, o risco aceito,
  desvios) e entrada em `[Unreleased]`. Edições curtas, ao fim dos arquivos: a #136
  edita os mesmos.

## 5. Verificação local (stack de deploy)

Projeto isolado `-p buteco-prod-verify-119`, Podman com imagens no formato
`docker` (o `deployment.md` explica: o OCI ignora o `HEALTHCHECK`), portas só na
faixa 58100–58199 conferidas com `lsof -i :<porta>`, `load average` anotado
antes de cada medição e antes de classificar qualquer falha.

- [x] 5.1 [stack de deploy] `.env` de teste **fora do repositório** (no diretório
  de rascunho da sessão), com valores de teste: segredos gerados na hora, hash de
  operador de uma senha de teste (`docs/development.md`), `STACK_HTTP_PORT` livre na
  faixa, chave do Google **vazia**. Conferir com `env | grep` que nenhuma variável
  homônima exportada no shell vence o `--env-file`.
  **Feito:** `.env` de teste no diretório de rascunho da sessão, segredos de
  `openssl rand`, hash PBKDF2 de uma senha de teste, `STACK_HTTP_PORT=58119`
  (livre no `lsof`), chave do Google vazia. `env | grep` sem variável homônima
  exportada (só `SSH_AUTH_SOCK`, que casa com `AUTH_` e não é do compose).
- [x] 5.2 [stack de deploy] Build (`BUILDAH_FORMAT=docker` ou `--format docker`) e
  `up -d`: os oito serviços sobem, `api`, `inbox`, `connectors` e `frontend`
  `healthy`, `migrator` concluído, exatamente um contêiner `connectors`; o
  `connectors` só iniciou depois do `api` saudável (horários do `inspect`, com a
  ressalva do relógio da VM do Podman).
  **Medido (06/10/2026, podman 5 + podman-compose 1.6.0, `BUILDAH_FORMAT=docker`):**
  build `exit=0`, nenhum aviso de `HEALTHCHECK` ignorado; `up -d` `exit=0`, oito
  serviços; `api`, `inbox`, `connectors` `healthy`, `workers` `Up` (sem
  healthcheck), `migrator` `Exited (0)`, exatamente um contêiner `connectors`.
  **`frontend` `unhealthy`**, defeito anterior e fora do escopo: o `wget` do
  `HEALTHCHECK` vai em `::1` e o nginx escuta só `0.0.0.0:80` (`127.0.0.1` OK,
  `localhost` e `[::1]` recusam; o painel responde `200` pelo host) — **#153**.
  **A ordem de subida não pôde ser provada:** o `podman-compose` 1.6.0 sobe os
  serviços em sequência, a cada ~8 s, sem esperar as condições de `depends_on`
  (o `migrator` iniciou às 00:12:30, **depois** do `api`, às 00:12:05, que
  depende dele). Fica para o Docker do mantenedor. **Ajuste de ambiente,
  registrado:** o `resolver 127.0.0.11` do `nginx.conf` é o DNS embutido do
  Docker; na rede do Podman o DNS é o gateway (`10.89.5.1`), e sem trocá-lo todo
  prefixo respondia `502` (`connectors could not be resolved`). Mesma prática da
  `roteamento-insights-no-nginx`: cópia do arquivo com **só** o `resolver`
  trocado (`diff` de uma linha, `53c53`), posta no contêiner que criei e `nginx -s
  reload`; o arquivo da imagem era idêntico ao do repositório. `load average`
  11,5 na subida (carga externa: chegou a 94,9 durante a 5.3).
- [x] 5.3 [`apps/connectors` no stack] Sem a chave do Google: login no `apps/api`
  pelo nginx, `GET /connectors/providers` com o token de operador responde `200
  []`; sem token, `401` sem `text/html`; log do `connectors` sem o aviso de
  `Api:BaseUrl` e do `api` sem o de `Connectors:BaseUrl`.
  **Medido:** `200 []` com token (`application/json`), `401` sem token e sem
  `content-type`; avisos de `Api:BaseUrl` no `connectors` e de
  `Connectors:BaseUrl` no `api`: 0 e 0. `load average` 94,9 (externa).
- [x] 5.4 [`apps/frontend` no stack] Prefixo e navegação: `curl` com
  `Sec-Fetch-Mode: cors` em `/connectors/providers` → `401` sem `text/html`;
  `/connectorsxyz` → shell do SPA; navegação (`Sec-Fetch-Mode: navigate`) em
  `/connectors` e `/connectors/providers` → `index.html` com `cache-control:
  no-store`; navegação real num browser em `/connectors` carrega o painel, sem
  JSON na tela. Bundle: o JS servido contém `/connectors/providers` e **não**
  contém `localhost:5037`; no browser, `/knowledge-bases/new` faz a chamada para
  `/connectors/providers` no próprio domínio (aba de rede).
  **Medido:** `cors /connectors/providers` → `401` sem `text/html`;
  `/connectorsxyz` → `200 text/html` (shell); navegação em `/connectors` e
  `/connectors/providers` → `200`, `Content-Type: text/html`, `Cache-Control:
  no-store`. Bundle (`index-CbcWcRQa.js`, 1.053.809 B): `/connectors/providers` 2
  vezes, `localhost:5037` 0, nenhum `localhost:50xx`. No Chrome: `/connectors`
  carrega o painel (título "Buteco Agentes") com o 404 do react-router, idêntico a
  `/rota-inexistente-119` — o painel não tem página de rota desconhecida, defeito
  anterior: **#154**. Em `/knowledge-bases/new`, ao escolher "Sincronizada", o
  painel chama `GET http://127.0.0.1:58119/connectors/providers` → `200` no próprio
  domínio e mostra "Nenhum provedor está configurado". (O login pela tela não
  aceitou a digitação da ferramenta do browser; o token de teste emitido pelo
  `curl` foi posto na `sessionStorage`.)
- [x] 5.5 [`apps/api` ↔ `apps/connectors` no stack] Rede interna: de dentro do
  `api`, `curl http://connectors:8080/health` → `200`; de dentro do `connectors`,
  `curl http://api:8080/health` → `200`. Cadastro de base sincronizada pelo nginx,
  sem a chave do Google: o `apps/api` responde com o código vindo do conector
  (`provider-not-configured`), e **não** `connectors-not-configured` nem
  `connectors-unavailable`. Sentido inverso, com token: "Sincronizar agora"
  (`POST /connectors/knowledge-bases/{guid novo}/sync`) responde `404
  knowledge-base-not-found`, o que prova que o `apps/connectors` consultou o
  `apps/api` com `service:connectors` aceito; reprova com `503
  sync-not-configured`, `503 sync-api-unavailable` ou `502 sync-api-error`.
  **Medido:** `api` → `http://connectors:8080/health` `200`; `connectors` →
  `http://api:8080/health` `200`. Cadastro `Synced` sem chave → `422
  provider-not-configured`. "Sincronizar agora" com guid novo → `404
  knowledge-base-not-found`.
- [x] 5.6 [`apps/connectors` no stack, com a chave real] A chave da service
  account (`~/.config/buteco-agents/drive-0-sa.json`) entra **só como variável do
  shell** do comando `up` (vence o `--env-file`), nunca escrita em arquivo; recriar
  só o `connectors`. Medir: `providers` lista `google-drive` com o e-mail; o
  comando de IPv6 da D7 de dentro do contêiner (`-4` e `-6`, com tempos);
  navegação de pastas pelo nginx responde em segundos; cadastro de base
  sincronizada na pasta de teste da etapa 0 → `201`; a primeira rodada do ciclo
  grava os documentos no `apps/api` (lista de documentos da base). Somente
  leitura no Drive. Registrar no `02` sem nada da chave.
  **Enquanto a chave estiver na sessão:** NÃO rodar `docker compose config`,
  `podman inspect` do `connectors`, `podman exec ... env` nem nada que imprima o
  ambiente. A variável é apagada do shell (`unset`) assim que o `connectors`
  subir. No fim, procurar trechos da chave na saída registrada da sessão e nos
  logs do contêiner (`client_email` só onde a #103 permite, `private_key` nunca)
  e registrar que a busca não encontrou nada.
  **Medido (`load average` 7,7 → 4,8):** a chave entrou por `export` no mesmo
  comando do `up`, e o `unset` veio logo depois (`0 chars` conferido); a saída do
  `up` não ecoa ambiente (conferido antes, nas três subidas anteriores: nenhum
  `-e`, `Auth__` nem `GoogleDrive`). Nenhum `compose config`, `inspect` do
  `connectors` ou `exec env` com a chave na sessão. `providers` → `200` em 0,05 s
  com `google-drive` e o e-mail da conta (domínio `…iam.gserviceaccount.com`).
  IPv6 de dentro do contêiner: `-4` `400` em 0,26 s; `-6` falha na hora (`Could
  not resolve host`: a rede do Podman não resolve AAAA), sem estourar. Navegação do
  nível de cima pelo nginx (troca de token real pelo .NET) → `200` em 1,39 s, duas
  pastas. Cadastro na pasta principal da etapa 0 → `201` em 0,91 s. "Sincronizar
  agora" → `202`; a rodada terminou em segundos, `lastError: null`, **5
  documentos** gravados no `apps/api` (já em `Indexing`: fila e despacho da #138
  funcionando) e 3 ignorados com `shortcut-not-followed`, `unsupported-type` e
  `subfolder-not-synced`. Indexação não verificada (endpoint de embedding falso,
  fora do escopo). Passo 0 do `deployment.md` conferido no Postgres do stack: `1`
  (migração da #138 aplicada) e `0` `Pending`.
  **Busca da chave:** quatro trechos da `private_key`, o `private_key_id`, um
  trecho do base64 e o literal `"private_key"`: **0** ocorrências no log do
  `connectors`, nos 17 arquivos da verificação e nos arquivos de sessão do dia.
  `client_email`: 0 no log; 1 no rascunho (a resposta salva de
  `/connectors/providers`, onde a #103 permite); 0 nesta sessão.
- [x] 5.8 [stack de deploy, `apps/frontend` e `apps/connectors`] Chave do Google
  **inválida** (base64 de um JSON sem `private_key`), recriando só o
  `connectors`: ele não sobe (log com a verificação que falhou, sem trecho do
  valor); o painel carrega (`/` com `200 text/html`); `/agents` com token e
  `/messages/summary` sem token respondem dos apps (`200` JSON e `401`);
  `/connectors/providers` responde `502` com a página de erro do nginx, como o
  `api` e o `inbox` fora do ar (nunca `200 text/html`). Depois, voltar à chave
  vazia e conferir o `connectors` `healthy` de novo. Roda antes da 5.6 e da 5.7.
  **Medido em 06/10/2026, com a divergência resolvida em 11/10/2026:** o `connectors` não sobe
  (laço de `restart`, `GoogleDrive:ServiceAccountKeyBase64 não tem
  'private_key'`, sem trecho do valor nem o e-mail falso no log); painel `/` →
  `200 text/html`; `/agents` com token → `200` JSON; `/messages/summary` sem token
  → `401`. **`/connectors/providers` → `502 text/html`**: é a página de erro
  padrão do próprio nginx, a mesma que o `api` ou o `inbox` dariam fora do ar. O
  cenário "Painel funciona com o apps/connectors fora do ar" e a checagem 1 do
  `deployment.md` dizem "502 **sem** `text/html`". No painel, o cartão da origem
  mostra "Erro inesperado (status 502)" com "Tentar de novo", e o resto da tela
  funciona. De volta à chave vazia: `healthy` e `200 []`. **Decisão do
  mantenedor (11/10/2026):** o `/connectors` se comporta igual ao `api` e ao
  `inbox`; o `nginx.conf` não muda, e o cenário, a checagem do `deployment.md` e
  o `design.md` foram ajustados ao comportamento medido. O texto desta tarefa
  acima já está na forma nova.
- [x] 5.7 [stack de deploy] Limpeza: `down -v` do projeto, remoção das imagens e
  volumes com `119` no nome criados aqui, e do `.env` de teste; `podman ps -a`,
  `podman volume ls` e `podman images` sem nada do projeto; porta liberada
  (`lsof`). Nada que esta change não criou é tocado.
  **Prova:** `down -v` `exit=0`; seis imagens `buteco-prod-verify-119_*`
  removidas; contêineres, volumes, imagens e redes com `119`: 0, 0, 0, 0; porta
  58119 livre; os 7 contêineres que não são desta change continuam. Arquivos de
  teste do rascunho apagados. Imagens base usadas pelo build (`aspnet`, `sdk`,
  `node`, `nginx`, `pgvector`, `rabbitmq`) não foram removidas: compartilhadas,
  e não criadas por esta change.

## 6. Fechamento

- [x] 6.1 [documentação] `python3 scripts/check-docs.py` sem violação.
- [x] 6.2 `openspec validate implantar-connectors-producao --strict`.
- [x] 6.3 [documentação] Divergência entre o medido e o `design.md` corrige o
  `design.md` com a causa (convenção 9), antes do archive.
