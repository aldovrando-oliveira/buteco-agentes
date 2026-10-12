## Context

**Issue:** #119. Bloqueadores fechados: #103, #116, #138 (`gh api
.../issues/119/dependencies/blocked_by`, conferido na abertura desta change).

O `apps/connectors` existe desde a #103 (`apps-connectors-google-drive`), com
`Dockerfile` e imagem `buteco-connectors`, mas fora do `docker-compose.prod.yml`.
A D10 daquela change adiou a implantação para cá por dois motivos: nenhum
consumidor em produção e o gatilho da #117 (terceiro processo com
`Auth__TokenSigningKey`). Os dois motivos se resolveram:

- **Consumidores na `main`:** #104 (`criacao-base-sincronizada`, o `apps/api`
  valida a pasta no `apps/connectors`), #105 (`ciclo-de-sincronizacao`, o
  `apps/connectors` escreve no `apps/api` pelas rotas de `/sync`), #106
  (`frontend-cadastro-base-sincronizada`, o painel chama `/connectors/...`), #107
  (detalhe da base sincronizada, "Sincronizar agora").
- **#138** (`indexacao-sem-job-orfao`): o upsert do ciclo não deixa mais
  documento `Pending` sem pedido de indexação quando o RabbitMQ não responde. Era
  o bloqueio declarado da #119 ("o ciclo não pode rodar em produção com este
  caminho aberto").
- **#117:** o mantenedor decidiu implantar com a chave compartilhada e aceitar o
  risco (opção 1). Registrado na #117
  ([comentário](https://github.com/aldovrando-oliveira/buteco-agentes/issues/117#issuecomment-5999044626))
  e no `docs/deployment.md`. A #117 continua aberta, `aguardando gatilho`.

### O que o stack tem hoje (lido na `main`, `043cf69`)

- **Rede do compose:** rede padrão do projeto; cada serviço é alcançável pelo nome
  da chave em `services:`. Os apps .NET escutam em **8080**: a imagem base
  `mcr.microsoft.com/dotnet/aspnet:10.0` define `ASPNETCORE_HTTP_PORTS=8080`
  (conferido com `podman image inspect` nesta máquina), nenhum serviço do compose
  sobrescreve, o `Dockerfile` do `apps/connectors` declara `EXPOSE 8080` e o
  `HEALTHCHECK` sonda `http://localhost:8080/health`. É o mesmo arranjo em que o
  `apps/inbox` já chama `Api__BaseUrl: "http://api:8080"` no piloto. Os nomes
  ficam, então, **`api:8080`** e **`connectors:8080`**; a tarefa 5.5 confere de
  dentro dos contêineres, e não por dedução.
- **Rotas do `apps/connectors`:** todas sob o grupo `/connectors`
  (`Endpoints/ConnectorEndpoints.cs:14`, `MapSyncEndpoints`), mais `/health`
  anônimo. O literal `"/sync/knowledge-bases"` do grep é chamada de **saída** para
  o `apps/api` (`Sync/SyncApiClient.cs:38`), e `"/providers"` é sub-rota do grupo.
- **CORS:** `Cors:AllowedOrigins` vazio por padrão (`appsettings.json`). Com
  `VITE_CONNECTORS_BASE_URL: ""`, o painel chama o mesmo domínio, sem preflight.
  Nada a configurar (#106, comentário na #119).
- **Nginx do stack:** dois blocos de prefixo (api, inbox), desvio de
  `Sec-Fetch-Mode: navigate`, `resolver 127.0.0.11`, **nenhuma diretiva
  `*_timeout`**: vale o padrão do nginx, 60 s de `proxy_read_timeout`.
- **Frontend:** `ARG VITE_CONNECTORS_BASE_URL` opcional no `Dockerfile`, sem
  `test -n` e sem `ENV` (#106, D1). Sem a variável, a origem "Sincronizada" fica
  indisponível; com `""`, chama `/connectors/...` no próprio domínio.

### Limites de tempo envolvidos (lidos no código)

| quem espera | por quem | limite | onde |
|---|---|---|---|
| `apps/connectors` | Google, metadado, listagem, troca de token | 30 s por chamada | `GoogleDriveHttp.MetadataTimeout` |
| `apps/connectors` | Google, exportação e download | **120 s** por chamada | `GoogleDriveHttp.ContentTimeout` |
| `apps/connectors` | `apps/api`, rotas de `/sync` | 30 s | `SyncApiClient.Timeout` |
| `apps/api` | `apps/connectors`, descrição de pasta | 35 s | D2 da #104 |
| nginx do stack | qualquer upstream | 60 s (padrão) | nenhuma diretiva no `nginx.conf` |

**Correção ao enunciado da tarefa:** a exportação de um Doc grande não leva "até
30 s" e **não atravessa o nginx**. Medida em 16,8 s a 31,1 s (tarefa 10.4 da #103),
o limite dela é de 120 s, e ela roda **dentro do ciclo**: `apps/connectors` →
Google, e depois `apps/connectors` → `apps/api` direto pela rede interna
(`http://api:8080`). Ver D4.

### Requisitos vindos dos comentários na #119

| # | origem | o que pede | onde esta change atende |
|---|---|---|---|
| R1 | #104, [comentário](https://github.com/aldovrando-oliveira/buteco-agentes/issues/119#issuecomment-5972499051) (D1 da `criacao-base-sincronizada`) | `Connectors__BaseUrl` no serviço `api`, apontando para o `apps/connectors` na rede interna; não é segredo, fica fixo no `environment:`, sem `.env.prod`; opcional para o boot, inválida derruba; `configuration.md` e `deployment.md` listam a variável; verificação pós-deploy com um cadastro `Synced` que **não** responda `connectors-not-configured`; nenhum segredo novo (o `apps/api` assina `service:api` com a mesma chave) | D2, D6; tarefas 2.2, 4.2, 4.4 |
| R2 | #105, [comentário](https://github.com/aldovrando-oliveira/buteco-agentes/issues/119#issuecomment-5974353340) (D1, D2, D10 da `ciclo-de-sincronizacao`) | `Api__BaseUrl` no serviço `connectors`, apontando para o `apps/api` na rede interna; não é segredo, fixo, sem `.env.prod`; sem ela o ciclo não roda; nenhum segredo novo (o `apps/connectors` assina `service:connectors`); uma réplica só; nginx sem nada novo além do prefixo `/connectors` (a rota `POST /connectors/knowledge-bases/{id}/sync` fica sob ele); docs; verificação pós-deploy de que o log do `apps/connectors` **não** traz o aviso de ciclo desligado | D1, D2, D3, D6; tarefas 2.1, 4.2, 4.4 |
| R3 | #105, [comentário](https://github.com/aldovrando-oliveira/buteco-agentes/issues/119#issuecomment-5974415449) | o `apps/connectors` entra na lista do `deployment.md` de serviços que nunca usam `replicas > 1`, ao lado do `apps/workers` e do `apps/inbox`, até existir lock distribuído | D1; tarefas 2.1, 4.3 |
| R4 | #106, [comentário](https://github.com/aldovrando-oliveira/buteco-agentes/issues/119#issuecomment-5975539376) (D1 da `frontend-cadastro-base-sincronizada`) | `VITE_CONNECTORS_BASE_URL: ""` em `build.args` do `frontend`, ao lado dos outros dois; **junto com o bloco `/connectors` do nginx, nunca antes**; não é segredo; docs; verificação pós-deploy de que, em `/knowledge-bases/new`, "Sincronizada" está habilitada e lista `google-drive` com o e-mail da conta; CORS desnecessário com caminho relativo | D2, D4, D6; tarefas 2.3, 3.1, 4.4 |
| R5 | #138 — **sem comentário na #119** (ver "Desvio" abaixo); vem do comentário na própria #138 e da D6 da `indexacao-sem-job-orfao` | o ciclo só roda em produção com a #138 implantada; a migração `AddKnowledgeIndexingRequests` recupera os órfãos gravando um pedido por documento `Pending`, e a contagem `SELECT count(*) FROM knowledge_documents WHERE "IndexingStatus" = 'Pending'` é medida **antes** do deploy e registrada no `02` junto com a implantação | D5; tarefa 4.3 |

**Desvio:** a tarefa pedia os comentários da #104, #105, #106 **e #138** na #119.
A #119 tem quatro comentários: um da #104, dois da #105 e um da #106. A #138 não
deixou comentário nela; deixou um na própria #138 ("Por isso esta issue bloqueia a
#119") e registrou o bloqueio por dependência. O requisito R5 foi tirado do
`design.md` arquivado da #138 (D6 e Migration Plan).

Requisitos do **corpo** da #119 (transcritos da #103) também valem e entram nas
decisões abaixo: serviço sem porta publicada, `frontend` dependendo de
`connectors: service_healthy` (**descartado na revisão**, D1), `.env.prod.example` com a variável nova e o comando
de geração, prefixo `/connectors` com desvio e `resolver`, `/providers` continua do
`apps/api`, pré-requisitos da etapa 0 e checagem do prefixo no `deployment.md`,
delta de `server-deployment`, verificação local com e sem a variável do Google.

## Goals / Non-Goals

**Goals:**

- O `docker-compose.prod.yml` sobe o `apps/connectors` saudável, em réplica única,
  com e sem a chave do Google.
- O painel em produção chega ao `apps/connectors` pelo nginx do stack, e o
  `apps/api` e o `apps/connectors` se alcançam pela rede interna.
- O roteiro de deploy (`docs/deployment.md`) cobre o serviço novo, a migração da
  #138 e uma verificação pós-deploy que prova cada ligação nova.
- O risco aceito da #117 fica registrado onde o operador lê antes de implantar.

**Non-Goals:**

- Nenhum deploy, nenhum acesso a servidor real. O deploy é do mantenedor.
- Nenhuma mudança de código em `apps/` além de configuração de deploy
  (`apps/frontend/deploy/nginx.conf`). Se faltar algo no código, a change para.
- Chave por serviço ou assinatura assimétrica (#117).
- Drive Compartilhado e política de compartilhamento externo de organização (#114):
  só o roteiro de diagnóstico entra.
- Várias réplicas do `apps/connectors` (D10 da #105).
- `DOTNET_SYSTEM_NET_DISABLEIPV6` preventivo (D7).
- Testes unitários: esta change não toca código de app, e não há unidade nova a
  testar. A verificação é de implantação, local e contra o compose real (tarefas
  5.x), e reusa as checagens de prefixo que o `deployment.md` já define.

## Decisions

### D1. Serviço `connectors` no compose: imagem do próprio `Dockerfile`, saúde da imagem, depois do `apps/api`, réplica única

```yaml
  connectors:
    build:
      context: .
      dockerfile: apps/connectors/Dockerfile
    restart: unless-stopped
    depends_on:
      api:
        condition: service_healthy
    environment:
      Auth__TokenSigningKey: ${AUTH_TOKEN_SIGNING_KEY}
      GoogleDrive__ServiceAccountKeyBase64: ${GOOGLE_DRIVE_SERVICE_ACCOUNT_KEY_BASE64:-}
      Api__BaseUrl: "http://api:8080"
    # Sem porta publicada.
```

- **Imagem:** `build` a partir de `apps/connectors/Dockerfile`, como os outros
  apps; o compose não tem registro de imagem (CI/CD é non-goal).
- **`healthcheck`:** o `HEALTHCHECK` do `Dockerfile` (`curl -f
  http://localhost:8080/health`, 10 s/3 s/10 s/3), como `api` e `inbox`, que
  também não o repetem no compose. Repetir criaria duas fontes do mesmo número.
- **Ordem de subida:** `depends_on: api: service_healthy`. A primeira rodada do
  ciclo roda logo depois do boot (D3 da #105); subir antes do `apps/api` faria
  essa rodada falhar e a próxima esperar 5 minutos. O `apps/api` **não** depende
  do `apps/connectors` para subir (`Connectors__BaseUrl` só é usado no cadastro),
  então não há ciclo de dependência.
- **O `frontend` NÃO depende do `connectors`** (ajuste do mantenedor na revisão):
  continua dependendo só de `api` e `inbox` saudáveis. O `apps/connectors` recusa
  subir com uma chave do Google inválida (D3 da #103); com uma dependência de
  saúde, um erro nessa chave derrubaria o painel inteiro, e não só a
  sincronização. Sem ela, o painel carrega, as rotas do `apps/api` e do
  `apps/inbox` respondem, e só o prefixo `/connectors` responde `502`, com a
  mesma página de erro do nginx dos outros dois blocos (o nginx
  resolve o upstream por requisição, pelo `resolver` e pela variável
  `$connectors_upstream`, e por isso sobe sem o `apps/connectors`; D3). O
  `deployment.md` diz o que esse `502` significa.
- **Sem `migrator`:** o `apps/connectors` não tem banco. A dependência transitiva
  vem pelo `api`.
- **Réplica única:** sem `deploy.replicas`; o comentário de non-goals do topo do
  compose e a lista do `deployment.md` passam a nomear o `apps/connectors` ao lado
  de `apps/workers` e `apps/inbox` (R3). Motivo: o lock que impede dois ciclos da
  mesma base é em memória e o ciclo roda no processo (D10 da #105).

Alternativas descartadas:

- *Sem `depends_on`:* a rodada de boot falharia contra um `apps/api` ainda
  migrando, e o primeiro deploy atrasaria a sincronização em 5 minutos sem
  motivo.
- *`depends_on: migrator`, como `api` e `inbox`:* o `apps/connectors` não lê
  banco; a dependência certa é de quem ele chama.
- *`frontend` com `depends_on: connectors: service_healthy`* (pedido no corpo da
  #119, transcrito da #103): descartado na revisão pelo risco acima. Uma chave do
  Google inválida, ou qualquer outra falha de boot do `apps/connectors`, deixaria o
  stack sem painel.
- *`healthcheck:` repetido no compose:* duas fontes para o mesmo intervalo, e o
  `workers` mostra que o compose só comenta, não define, saúde.

### D2. Variáveis: o que é segredo vem do `.env.prod`; endereço interno é fixo no compose

| serviço | chave | valor no compose | origem | obrigatória |
|---|---|---|---|---|
| `connectors` | `Auth__TokenSigningKey` | `${AUTH_TOKEN_SIGNING_KEY}` | `.env.prod`, a **mesma** variável de `api` e `inbox` | sim; o `:?` no `api` já reprova o processamento do arquivo sem ela, e o app reprova o boot (fail-fast) |
| `connectors` | `GoogleDrive__ServiceAccountKeyBase64` | `${GOOGLE_DRIVE_SERVICE_ACCOUNT_KEY_BASE64:-}` | `.env.prod`, nova | **não**: vazia, o provedor `google-drive` não existe e `GET /connectors/providers` responde `[]`; presente e inválida, o boot reprova |
| `connectors` | `Api__BaseUrl` | `"http://api:8080"` | fixo | não para o boot; sem ela o ciclo fica desligado (R2) — por isso vai **sempre** preenchida |
| `api` | `Connectors__BaseUrl` | `"http://connectors:8080"` | fixo | não para o boot; sem ela o cadastro `Synced` responde `503 connectors-not-configured` (R1) — vai **sempre** preenchida |
| `frontend` (build) | `VITE_CONNECTORS_BASE_URL` | `""` em `build.args` | fixo | não para o build; ausente desliga a origem "Sincronizada" (R4) |

- **`AUTH_TOKEN_SIGNING_KEY` sem `:?` no `connectors`**, igual ao `inbox`: a
  interpolação avalia o arquivo inteiro, e o `:?` do `api` já faz o compose
  reprovar sem a variável. Uma segunda mensagem para a mesma variável não muda o
  comportamento.
- **Chave do Google com `:-`**, opcional de propósito: o stack sobe sem
  conector configurado, e o operador liga o provedor quando tiver a service
  account. É o estado "sem a chave" que a verificação local cobre (tarefa 5.3).
- **`.env.prod.example`:** `GOOGLE_DRIVE_SERVICE_ACCOUNT_KEY_BASE64=` vazia, com
  comentário: o que é, o comando `base64 -i chave.json | tr -d '\n'`, os
  pré-requisitos da etapa 0, que vazia desliga o provedor e que a chave **nunca**
  é versionada. O comentário de `AUTH_TOKEN_SIGNING_KEY` passa a dizer "apps/api,
  apps/inbox e apps/connectors" e aponta o risco aceito (#117).
- **Endereços internos fixos, sem `.env.prod`:** não são segredo e não variam por
  ambiente; o padrão já existe (`Api__BaseUrl: "http://api:8080"` no `inbox`).
- **Nada de `Cors__AllowedOrigins`:** mesmo domínio (Context).

Alternativas descartadas:

- *Endereços internos no `.env.prod`:* um valor que nunca muda, aberto a erro de
  digitação que só aparece no cadastro de base.
- *Chave do Google obrigatória (`:?`):* derrubaria o processamento do arquivo
  inteiro para quem ainda não tem service account, e o app foi desenhado para
  subir sem ela (D3 da #103).
- *Montar o JSON da chave como arquivo (`secrets:`/volume):* o app lê base64 de
  variável (D3 da #103); mudar isso é código.

### D3. Nginx: bloco `/connectors` no molde dos outros dois, sem diretiva de tempo

```nginx
    location ~ ^/(connectors)(/|$) {
        if ($http_sec_fetch_mode = navigate) {
            rewrite ^ /index.html last;
        }
        set $connectors_upstream connectors:8080;
        proxy_pass http://$connectors_upstream;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    }
```

- **Desvio de navegação:** `connectors` não é rota de página hoje, mas entra com o
  desvio igual aos outros blocos (D9 da #103), para que uma rota de página futura
  com esse nome não quebre. Uma navegação real em `/connectors` cai no shell do
  SPA com `Cache-Control: no-store` (o rewrite reentra no `location =
  /index.html`).
- **`resolver` e variável de upstream:** mesmo motivo dos outros blocos; o nginx
  sobe mesmo com o `apps/connectors` fora do ar.
- **`(/|$)`:** `/connectorsxyz` não vai para o app.
- **`/providers` continua no bloco do `apps/api`.**
- **Comentário do topo do `nginx.conf`:** a lista de greps de reconferência ganha
  o `apps/connectors` e o ruído conhecido dele (`/sync/knowledge-bases` é chamada
  de saída; `/providers` é sub-rota do grupo `/connectors`).

**Limites de tempo, conferidos (Context):**

- **Cadastro de base sincronizada** (`POST /knowledge-bases`, bloco do
  `apps/api`): o `apps/api` espera até 35 s pelo `apps/connectors`, abaixo dos 60 s
  do nginx — a D2 da #104 foi calculada contra esse padrão, e por isso esta change
  **não** acrescenta `proxy_read_timeout`.
- **Exportação de Doc grande:** não atravessa o nginx (Context). O upsert que vem
  depois dela vai direto ao `apps/api` com limite de 30 s.
- **"Sincronizar agora"** (`POST /connectors/knowledge-bases/{id}/sync`): responde
  `202` e roda em segundo plano; não segura a conexão.
- **Navegação de pastas** (`GET /connectors/providers/{p}/folders`): troca de token
  (quando vencido) mais duas chamadas de metadado, cada uma com 30 s. Na prática
  responde em menos de 1 s; **o pior caso teórico (90 s) passa dos 60 s** e o painel
  receberia `504` do nginx em vez do `503 provider-unavailable`. Aceito: é leitura
  sem efeito colateral, o operador repete, e o caso exige o Google travado em mais
  de uma chamada seguida.

Alternativas descartadas:

- *Bloco sem desvio de navegação:* uma rota de página futura `/connectors` quebraria
  no refresh, como aconteceu com `/agents` (comentário do `nginx.conf`).
- *`proxy_read_timeout 95s` só no `/connectors`:* cobriria o pior caso da
  navegação, mas o proxy externo e o Cloudflare (100 s) têm os próprios limites,
  fora do repositório e não medidos; o operador esperaria minuto e meio sem retorno
  em vez de ver um erro e repetir; e a D2 da #104 está amarrada aos 60 s do bloco
  do `apps/api`, que não muda.
- *Subir o limite do servidor inteiro:* mexeria na conta da #104 sem medida nova.

### D4. Frontend: `VITE_CONNECTORS_BASE_URL: ""` no mesmo diff do bloco do nginx

A variável entra em `build.args` do `frontend` **na mesma tarefa** que o bloco
`/connectors` do nginx (R4), e a verificação local prova os dois juntos: o bundle
chama `/connectors/providers` no próprio domínio, e esse caminho responde `401` do
app, não `200 text/html` do fallback. A ordem inversa (variável sem bloco) faz o
painel mostrar erro de um serviço que não está ali.

- *Descartado, entrar com a variável numa change posterior:* deixaria o
  `apps/connectors` no ar sem o consumidor principal.

### D5. Redeploy: `connectors` na sequência, e a contagem de `Pending` antes da migração da #138

A sequência do `deployment.md` ganha o serviço no passo 4 e um passo **0**,
condicional, para o primeiro deploy que leva a migração
`AddKnowledgeIndexingRequests`:

```
0. (só se a migração da #138 ainda não rodou) contar os Pending e registrar no 02
1. build
2. stop inbox
3. run --rm migrator
4. up -d inbox api workers connectors frontend
```

- **Como saber se a migração já rodou:** `SELECT 1 FROM "__EFMigrationsHistory"
  WHERE "MigrationId" LIKE '%_AddKnowledgeIndexingRequests'` no `buteco_agents`.
- **A contagem:** `SELECT count(*) FROM knowledge_documents WHERE "IndexingStatus"
  = 'Pending'`, rodada no contêiner do `postgres` antes do passo 3. É o número
  máximo de indexações repetidas que a migração vai causar (D6 da #138); vai para o
  `02` com a data do deploy. Os dois comandos ficam no `deployment.md` prontos para
  copiar, com o `--env-file`.
- **Ordem de subida no passo 4:** o `depends_on` cuida de `connectors` depois de
  `api` saudável e de `frontend` depois de `api` e `inbox`.
- **`apps/connectors` não precisa estar parado na migração:** não tem banco. Fica
  só uma nota: uma mudança do teto de conteúdo do `apps/api` pede
  `restart connectors` no mesmo deploy (D3 da #105), porque o `up -d` não recria um
  serviço cuja imagem não mudou.
- **"Redeploy só do frontend"** continua valendo e não muda: o frontend não depende
  de variável nova além da de build.

Alternativas descartadas:

- *Contar depois da migração:* o número deixaria de distinguir órfãos de pedidos
  recém-gravados.
- *Parar o `apps/connectors` antes da migração:* não há índice nem tabela dele
  envolvidos; parar seria risco sem motivo, como o `deployment.md` já diz do
  `apps/api` e do `apps/workers`.

### D6. Verificação pós-deploy, no molde da que existe

Seção nova em `deployment.md` ("Verificação depois de implantar o
`apps/connectors`"), contra o domínio público, com o `H` e o `UA` da seção do
frontend:

| # | checagem | aprovado quando | prova |
|---|---|---|---|
| 1 | `curl -s -o /dev/null -w '%{http_code} %{content_type}\n' -H 'Sec-Fetch-Mode: cors' "$H/connectors/providers"` | `401` **sem** `text/html`. `502` (página de erro do nginx, igual à do `api` e do `inbox`) quer dizer que o nginx roteou mas o `apps/connectors` não está no ar: ver o log dele; a chave do Google inválida é a causa mais provável | bloco do nginx |
| 2 | `curl -sI "$H/connectors" -H 'Sec-Fetch-Mode: navigate' \| grep -i -E '^(content-type\|cache-control)'` | `text/html` e `cache-control: no-store` | desvio de navegação |
| 3 | log do `apps/connectors` desde o boot (`logs connectors \| grep 'Api:BaseUrl não configurado'`) e do `apps/api` (`grep 'Connectors:BaseUrl não configurado'`) | nenhuma linha nos dois | R1, R2 |
| 4 | troca de token com o Google **de dentro** do contêiner (D7) | `200` com o provedor; navegação de pastas responde em segundos | rede de saída e credencial |
| 5 | pré-requisitos da etapa 0 | Drive API ativa (sem ela, `502 api-not-configured`); pasta compartilhada com o e-mail da conta como **Leitor**; download permitido para leitores (sem ele, os arquivos aparecem como ignorados com o código `download-blocked`, conferido em `openspec/specs/google-drive-connector/spec.md`) | etapa 0 (#100) |
| 6 | painel em `/knowledge-bases/new`, browser limpo | "Sincronizada" habilitada, listando `google-drive` com o e-mail da conta | R4 |
| 7 | cadastro de base sincronizada numa pasta de teste | `201`, e **nunca** `503 connectors-not-configured` nem `connectors-unavailable` | R1, rede interna |
| 8 | primeira sincronização real | base `Succeeded` e documentos aparecendo; com `access-denied`, ver abaixo | R2, ciclo |

**`access-denied` numa pasta já compartilhada:** se a pasta é de Google Workspace
ou de Drive Compartilhado e já foi compartilhada com o e-mail da conta, a causa
provável é a **política de compartilhamento externo da organização**, que não foi
medida (#114). O roteiro manda conferir, nesta ordem: (a) o e-mail da conta no
`detail` da recusa é o mesmo da pasta compartilhada; (b) a pasta aparece no nível
de cima da navegação; (c) com o administrador do Workspace, se o compartilhamento
com contas externas à organização está permitido para aquela unidade
organizacional, e, em Drive Compartilhado, se a opção de membros externos está
ligada. Resultado vai para a #114, que é onde a medição falta.

**Na implementação (convenção 9):** a seção do `deployment.md` ficou com **sete**
checagens, e não oito. A checagem 5 desta tabela (pré-requisitos da etapa 0) não é
algo que se confere com um comando depois do deploy: foi para o §1, **antes** de
preencher a variável, e os sintomas de cada pré-requisito faltando
(`api-not-configured`, `download-blocked`) foram para uma lista de erros da conta
ao lado do roteiro do `access-denied`. A checagem 2 mostra só `content-type` e
`cache-control`; a do log (3) conta linhas com `grep -c` para dar um critério
numérico. A troca de token pelo .NET é provada pela checagem do painel (abrir o
seletor de pasta), não por uma rota chamada à mão.

- *Descartado, automatizar a verificação num script:* non-goal da containerização
  (verificação manual), e a checagem 6 é no browser.

### D7. IPv6: medir de dentro do contêiner, e o remédio documentado, sem ligar de antemão

O problema apareceu duas vezes nesta máquina de desenvolvimento (`curl -6` estoura,
o .NET tenta IPv6 primeiro, a troca de token esgota os 30 s): na indexação
(22/09) e na tarefa 10.4 da #103. A causa é a rede da máquina, com rota IPv6 e sem
conectividade. A rede do servidor não foi medida.

Checagem 4 do `deployment.md`, de dentro do contêiner (a imagem já tem `curl`):

```
docker compose --env-file .env.prod -f docker-compose.prod.yml exec connectors \
  sh -c 'for f in -4 -6; do curl $f -sS -o /dev/null --max-time 10 -w "$f %{http_code} %{time_total}s\n" https://oauth2.googleapis.com/token -X POST; done'
```

`-4` tem de responder `400` (pedido sem corpo) em menos de 1 s. Se `-6` estourar os
10 s em vez de falhar na hora, a rede tem rota IPv6 sem conectividade: o .NET vai
tentar por ali primeiro. A prova pelo próprio .NET é a navegação de pastas da
checagem 6 (troca de token real). Se ela demorar perto de 30 s ou responder `503
provider-unavailable`, o remédio é acrescentar `DOTNET_SYSTEM_NET_DISABLEIPV6: "1"`
ao `environment:` do `connectors` e recriar o serviço; registrar no `02`.

- *Descartado, ligar `DOTNET_SYSTEM_NET_DISABLEIPV6` de antemão:* esconderia uma
  condição de rede não medida no servidor, e desliga o IPv6 do processo inteiro,
  inclusive para o `apps/api`, sem necessidade comprovada.
- *Descartado, sondar com `curl` apenas:* o `curl` usa *happy eyeballs* e passa
  onde o .NET esgota o limite; só a chamada do app prova o caminho real.

### D8. Delta de `server-deployment`

O que saiu da #103 volta aqui, atualizado:

- **Compose de servidor completo** (MODIFIED): cinco apps, oito serviços
  (postgres, rabbitmq, migrator, api, inbox, workers, connectors, frontend);
  cenário do compose sem a chave do Google.
- **Nginx do stack** (MODIFIED): `apps/connectors` entre os apps roteados,
  cenários do prefixo `/connectors` e da navegação nele.
- **Segredos compartilhados** (MODIFIED): `Auth__TokenSigningKey` com fonte única
  para três processos, com o cenário dos tokens de serviço entre `apps/api` e
  `apps/connectors` nos dois sentidos (#104, #105).
- **Réplica única dos serviços com lock em processo** (ADDED): `apps/workers`,
  `apps/inbox`, `apps/connectors`.
- **Endereços internos entre apps** (ADDED): `Connectors__BaseUrl` e `Api__BaseUrl`
  pela rede do compose, sempre preenchidos em produção.
- **Variável de build do conector junto com o prefixo** (ADDED), da #106.

## Árvore de arquivos

```
docker-compose.prod.yml                       (serviço connectors; api + frontend; comentário de réplicas)
.env.prod.example                             (GOOGLE_DRIVE_SERVICE_ACCOUNT_KEY_BASE64; comentário da chave de assinatura)
apps/frontend/deploy/nginx.conf               (bloco /connectors; greps de reconferência)
docs/deployment.md                            (variáveis, risco aceito #117, pré-requisitos, redeploy, réplicas, verificação)
docs/configuration.md                         (seção do stack de servidor)
02-HISTORICO_E_STATUS.md                      (seção da change)
CHANGELOG.md                                  ([Unreleased])
openspec/changes/implantar-connectors-producao/
├── proposal.md
├── design.md
├── tasks.md
└── specs/server-deployment/spec.md
```

Nada em `libs/`. Nenhum `Dockerfile` muda.

## Risks / Trade-offs

- **[Risco aceito] Terceiro processo com a chave de assinatura** (#117). → Decisão
  do mantenedor, registrada na #117 e no `deployment.md`, com gatilho para rever.
  Não mitigado em código.
- **[Risco] Falha de boot do `apps/connectors` derrubar o painel.** → D1: o
  `frontend` não depende dele; tarefa 5.8 sobe o stack com chave inválida.
- **[Risco] Variável do frontend sem o bloco do nginx, ou o contrário.** → D4: um
  só diff; verificação local 5.4 prova os dois juntos; checagens 1 e 6 em produção.
- **[Risco] Segunda réplica do `apps/connectors`.** → D1: comentário no compose e
  lista do `deployment.md`. Não testável por cenário: depende de quem opera.
- **[Risco] Rede do servidor com IPv6 quebrado.** → D7: checagem 4 e remédio
  documentado.
- **[Risco] Pior caso da navegação passa dos 60 s do nginx.** → D3: aceito, leitura
  repetível. Verificável só com o Google travado; não reproduzido aqui.
- **[Risco] Proxy externo com limite menor que 35 s.** → Fora do repositório; a
  checagem 7 em produção prova o cadastro pelo caminho inteiro.
- **[Risco] `access-denied` em pasta de organização.** → D6: roteiro e #114.
- **[Risco] Indexação repetida na migração da #138.** → D5: contagem antes,
  registrada; custo de uma vez (D6 da #138).
- **[Risco] Verificação local com Podman diverge do Docker de produção.** → `--format
  docker` (o `deployment.md` explica), projeto isolado `-p buteco-prod-verify-119`;
  o que só o Docker prova fica para a verificação pós-deploy do mantenedor.

## Migration Plan

1. Merge desta change não muda produção sozinho: o deploy é manual.
2. O mantenedor segue o `deployment.md`: confere `.env.prod` (variável nova
   opcional), roda o passo 0 se a migração da #138 não rodou, a sequência de
   redeploy, e a verificação pós-deploy.
3. **Rollback:** voltar o compose e o `nginx.conf` à versão anterior e
   `up -d --remove-orphans`; o frontend reconstruído sem a variável volta a mostrar
   a origem "Sincronizada" indisponível. As bases sincronizadas criadas continuam
   no banco e param de sincronizar. A migração da #138 não se reverte com isso, nem
   precisa: é do `apps/api`.

## Open Questions

Nenhuma de produto. A decisão sobre a #117 foi tomada (opção 1).

## Achados da implementação

- **`502` do `/connectors` vem com `text/html` (decidido pelo mantenedor em 11/10/2026: comportamento igual ao do `api` e do `inbox`).**
  Com o `apps/connectors` fora do ar (chave do Google inválida, tarefa 5.8), o
  nginx responde `502` com a **página de erro padrão dele**, `text/html`, a mesma
  dos blocos do `apps/api` e do `apps/inbox`. O cenário "Painel funciona com o
  apps/connectors fora do ar" e a checagem 1 do `deployment.md` foram escritos
  como "502 sem `text/html`", e isso não é o que o arquivo faz. O que distingue
  o caso do fallback de SPA é o **status** (`502` contra `200`), não o tipo. O
  painel trata bem: só o cartão da origem mostra "Erro inesperado (status 502)".
  Havia duas saídas: ajustar o texto, ou dar ao bloco `/connectors` um
  `error_page 502` próprio, com corpo `application/problem+json`, que os outros
  dois blocos não têm. **Decisão:** o `/connectors` se comporta igual ao `/agents`
  e ao `/messages`, e o `nginx.conf` não muda. O cenário, a checagem 1 e a 3 do
  `deployment.md` e a linha 1 da tabela de verificação acima passaram a dizer
  "`502` com a página de erro do nginx, nunca `200 text/html`". A causa da
  divergência foi o texto, não o arquivo: a frase tentou dizer "não é o fallback
  de SPA" e acabou restringindo o tipo, quando quem separa os casos é o status.
- **D1, ordem de subida, não provada localmente.** O `podman-compose` 1.6.0 não
  espera as condições de `depends_on` (sobe tudo em sequência; o `migrator` iniciou
  depois do `api`). O cenário "apps/connectors sobe depois do apps/api" depende do
  Docker Compose de produção para ser conferido.
- **Verificação com Podman exige trocar o `resolver`.** `127.0.0.11` é o DNS
  embutido do Docker; na rede do Podman é o gateway (`10.89.5.1`). A verificação
  usou uma cópia do `nginx.conf` com só essa linha trocada, como a
  `roteamento-insights-no-nginx` já fazia. Nada muda no arquivo versionado.
- **D7, IPv6 no Podman.** Dentro do contêiner, `curl -6` falha na hora (`Could not
  resolve host`), sem estourar; a troca de token pelo .NET levou 1,39 s. O
  critério da checagem 4 ("estourar os 10 s" é o sinal ruim; "falhar na hora" não)
  se sustenta. A rede do servidor continua não medida.
- **Defeitos anteriores, achados aqui e sequenciados:** `/sync` do `apps/api` sem
  bloco no nginx (**#152**); `frontend` `unhealthy` porque o `HEALTHCHECK` vai em
  `::1` e o nginx escuta só IPv4 (**#153**); painel sem página de rota desconhecida
  (**#154**).
