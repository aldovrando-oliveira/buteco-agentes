# Runbook de deploy — stack de servidor

Este documento cobre como subir e atualizar `docker-compose.prod.yml`
numa VM na cloud. Para o desenho e as decisões por trás disso, ver
[`openspec/changes/archive/2026-08-26-containerizacao-stack-servidor/design.md`](../openspec/changes/archive/2026-08-26-containerizacao-stack-servidor/design.md).

Documentos relacionados: [development.md](development.md) para construir as
imagens (o build context é sempre a raiz do monorepo) e
[configuration.md](configuration.md) para o inventário completo de variáveis
de ambiente.

> **Testando localmente com o compose de dev também rodando?** O nome de
> projeto do compose (Docker/Podman) é derivado do nome do diretório por
> padrão — `docker-compose.yml` (dev) e `docker-compose.prod.yml`, rodados
> da mesma pasta sem `-p`, geram os MESMOS nomes de container
> (`buteco-agents_postgres_1` etc.) e colidem: subir o prod pode parar/
> recriar os containers do dev (achado real durante a verificação desta
> change — os volumes de dev não foram perdidos, só os containers foram
> recriados apontando pro volume errado). Sempre use um nome de projeto
> isolado ao testar localmente: `docker compose -p buteco-prod-verify
> --env-file .env.prod -f docker-compose.prod.yml ...`. Numa VM de servidor
> de verdade, sem o compose de dev rodando ao lado, isso não se aplica.

> **Testando com `podman`/`podman-compose` em vez de `docker`?**
> Confirmado empiricamente: `podman build`/`podman-compose build` geram
> imagem em formato OCI por padrão, que **ignora silenciosamente** a
> instrução `HEALTHCHECK` (aviso explícito: "HEALTHCHECK is not
> supported for OCI image format and will be ignored"). O mecanismo
> funciona de verdade (`podman build --format docker` embute e executa o
> healthcheck normalmente, verificado rodando o container e observando
> `State.Health` real) — é só uma particularidade do formato default do
> podman. `docker build`/`docker compose` (o alvo real de produção) usam
> o formato nativo e não têm esse problema.

## 1. Primeiro deploy (VM do zero)

1. Copie [`.env.prod.example`](../.env.prod.example) para `.env.prod` na VM e
   preencha todos os valores `changeme` (ver seção "Variáveis por processo"
   abaixo e [configuration.md](configuration.md)). Nunca versionar
   `.env.prod`.
2. `docker compose --env-file .env.prod -f docker-compose.prod.yml up -d`

   > **`--env-file .env.prod` não é opcional, em nenhum comando.** O compose
   > interpola `${VAR}` mas não declara `env_file:` — o Compose, sozinho, lê
   > `.env`, nunca `.env.prod`.
   >
   > Vale para **todo** subcomando contra `docker-compose.prod.yml` — `build`,
   > `up`, `ps`, `stop`, `run` —, não só para o `up`: a interpolação avalia o
   > arquivo inteiro antes de o subcomando começar, então até um `ps` falha
   > sem a flag. Todos os comandos deste documento a trazem; copie-os inteiros.
   >
   > **Omiti-lo agora falha no processamento do arquivo**, nomeando a variável
   > que falta e como fornecê-la; nenhum serviço é criado. Antes desta proteção,
   > o mesmo comando subia o Postgres **sem senha configurada** quando não havia
   > `.env` na pasta, ou comia as variáveis de **dev** quando havia.
   >
   > Variável homônima **exportada no shell vence o `--env-file`**, e sem aviso.
   > Confira antes de subir:
   > ```bash
   > env | grep -E 'OPENAI|EMBEDDING|POSTGRES|RABBITMQ|AUTH_|MCP_|INBOX_|PUBLIC_DOMAIN|STACK_HTTP_PORT|GOOGLE_DRIVE|^TZ='
   > ```
   - `postgres`/`rabbitmq` sobem primeiro (healthcheck).
   - `migrator` roda depois de `postgres` saudável, aplica as migrations
     de `apps/api` e `apps/inbox` (cria `buteco_agents`/`buteco_inbox` se
     não existirem), e termina.
   - `apps/api`/`apps/inbox`/`apps/workers` sobem só depois do
     `migrator` completar com sucesso (`depends_on:
     service_completed_successfully`).
   - `apps/connectors` sobe depois do `apps/api` saudável (a primeira rodada
     do ciclo de sincronização chama o `apps/api` logo no boot). Sem banco,
     sem `migrator`.
   - `frontend` (nginx do stack) sobe depois de `apps/api` e `apps/inbox`
     saudáveis, publicando só a porta interna do host (`STACK_HTTP_PORT`,
     nunca 80/443). **Não** espera o `apps/connectors`: uma chave do Google
     inválida derruba só ele, e o painel continua no ar (#119).

   **Antes de preencher `GOOGLE_DRIVE_SERVICE_ACCOUNT_KEY_BASE64`** (opcional;
   vazia, o painel não oferece pasta do Drive), os pré-requisitos medidos na
   etapa 0 (#100):
   - a **Drive API ativada** no projeto do Google Cloud dono da service
     account. Sem ela, toda chamada responde `502` com o código
     `api-not-configured`;
   - a pasta **compartilhada com o e-mail da conta como Leitor**;
   - **download permitido para leitores** na pasta. Sem ele, os arquivos
     aparecem como ignorados, com o código `download-blocked`.
3. Confirme os healthchecks: `docker compose --env-file .env.prod -f
   docker-compose.prod.yml ps` — todos os serviços de longa duração devem
   estar `healthy`/`Up`.
4. Configure o nginx externo (fora deste stack, já existente) pra fazer
   `proxy_pass` pra `127.0.0.1:${STACK_HTTP_PORT}`.
5. Verificação manual (não automatizada — ver Non-Goals): acesse o
   domínio público, confirme o SPA carrega, faça login, e rode um
   round-trip real de webhook → resposta no canal.

## 2. Redeploy (atualização de imagem em produção)

**A sequência é a garantia, não um detalhe.** O índice único parcial de
`Session` (`sessions."ContactId" WHERE "ClosedAt" IS NULL`,
`inbox-session-indice-unico`) rejeita a rotação normal de sessão por
inatividade se o código antigo de `apps/inbox` continuar respondendo
tráfego enquanto uma migration nova já rodou contra o banco —
`DbUpdateException` não tratada no caminho mais comum do app (recepção de
webhook). `depends_on: service_completed_successfully` cobre só o
primeiro boot do stack a partir do zero — **não** cobre um redeploy, que
precisa ser feito manualmente nesta ordem.

Os quatro passos levam `--env-file .env.prod` (ver a nota do §1). Sem a flag,
cada passo falha sozinho, antes de agir, e isso é só um erro. O dano real é a
flag num passo e não no seguinte: com o `stop inbox` feito e o `run --rm
migrator` falhando, o `apps/inbox` fica parado e a migration não roda.

```
1. docker compose --env-file .env.prod -f docker-compose.prod.yml build          # rebuild das imagens alteradas
2. docker compose --env-file .env.prod -f docker-compose.prod.yml stop inbox      # para apps/inbox ANTES de migrar
3. docker compose --env-file .env.prod -f docker-compose.prod.yml run --rm migrator
4. docker compose --env-file .env.prod -f docker-compose.prod.yml up -d inbox api workers connectors frontend
```

`apps/workers`/`apps/api`/`apps/connectors`/`frontend` não têm essa
restrição — só `apps/inbox` precisa estar parado durante a migration, porque
só ele tem o índice único envolvido nesse caminho. O `apps/connectors` não tem
banco.

**Passo 0, só no primeiro deploy com a migração da #138**
(`AddKnowledgeIndexingRequests`). Ela grava um pedido de indexação para cada
documento `Pending`, e cada um que já tinha mensagem na fila é indexado de novo,
uma vez. Antes do passo 3, confira se ela já rodou e, se não, conte os
`Pending` e registre o número no `02` com a data do deploy:

```
docker compose --env-file .env.prod -f docker-compose.prod.yml exec -T postgres \
  sh -c 'psql -U "$POSTGRES_USER" -d buteco_agents -tA' <<'SQL'
SELECT count(*) FROM "__EFMigrationsHistory" WHERE "MigrationId" LIKE '%_AddKnowledgeIndexingRequests';
SELECT count(*) FROM knowledge_documents WHERE "IndexingStatus" = 'Pending';
SQL
```

A primeira linha da saída é `1` se a migração já rodou (pule o passo 0) e `0`
se não; a segunda é a contagem de `Pending` a registrar.

**`apps/api` e `apps/workers` sobem no mesmo passo 4, sempre** — e a partir da
migração `AddMcpServerAuthHeaderName` (#160) isso deixa de ser só costume. O
`apps/api` passa a gravar o tipo de autenticação `CustomHeader` em
`mcp_servers`, e um `apps/workers` de antes dessa versão, ao ler uma linha com
esse valor, derruba a resolução de tools inteira de todo agente vinculado ao
servidor (a leitura do enum lança, e ela acontece fora do `try` por servidor).
O valor só existe depois que um operador grava um servidor `CustomHeader`, então
a janela de um `up -d` conjunto é segura.

**Rollback para antes da #160:** antes de voltar o `apps/api` **ou** o
`apps/workers` para uma versão sem `CustomHeader`, converta ou remova os
servidores `CustomHeader`. O `apps/api` antigo também lança ao materializar essas
linhas, e a listagem de servidores MCP passa a falhar. O `Down` da migração só
roda junto com o rollback dos dois apps, porque os modelos novos dos dois mapeiam
a coluna `AuthHeaderName`:

```
docker compose --env-file .env.prod -f docker-compose.prod.yml exec -T postgres \
  sh -c 'psql -U "$POSTGRES_USER" -d buteco_agents -tA' <<'SQL'
SELECT "Id", "Name" FROM mcp_servers WHERE "AuthType" = 'CustomHeader';
SQL
```

Do `apps/workers` desta versão em diante, um `AuthType` desconhecido exclui só
aquele servidor da execução, com um aviso no log: rollbacks **só do
`apps/workers`** para versões que já têm essa tolerância são seguros para tipos
de autenticação futuros (D5 da `mcp-autenticacao-header-customizado`).

**Mudou o teto de conteúdo do `apps/api`** (`KnowledgeDocumentLimits`)? Rode
também `restart connectors` no mesmo deploy: o ciclo lembra em memória os
arquivos recusados, e o `up -d` não recria um serviço cuja imagem não mudou
(D3 da `ciclo-de-sincronizacao`).

### Redeploy só do frontend

Quando a mudança toca **só** `apps/frontend` (código do SPA ou
`apps/frontend/deploy/nginx.conf`), sem migration e sem os apps .NET, a
sequência acima não se aplica. Parar o `apps/inbox` nesse caso é risco
gratuito para um stack que está atendendo.

```
1. docker compose --env-file .env.prod -f docker-compose.prod.yml build frontend
2. docker compose --env-file .env.prod -f docker-compose.prod.yml up -d frontend
```

Sem `stop inbox` e sem `migrator`. Os outros serviços continuam no ar.

#### Verificação depois do redeploy do frontend

Rodar **logo depois** do `up -d`, de fora do servidor, contra o domínio
público. Cada checagem do shell roda **em duas formas**, `curl` simples e com
cabeçalhos de browser. O Cloudflare trata os dois pedidos por caminhos
diferentes: só o segundo recebe o beacon do Web Analytics injetado no HTML.

```
H=https://<domínio público>
UA='Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36'

# 1. Shell do SPA: as 8 linhas devem trazer "cache-control: no-store"
for p in / /index.html /agents /inventory; do
  curl -sI "$H$p" -H 'Sec-Fetch-Mode: navigate'                                             | grep -i '^cache-control'
  curl -sI "$H$p" -H 'Sec-Fetch-Mode: navigate' -H "User-Agent: $UA" -H 'Accept: text/html' | grep -i '^cache-control'
done

# 2. Assets: o JS atual, referenciado pelo shell
A=$(curl -s "$H/" | grep -oE '/assets/[^"]+\.js' | head -1)
curl -sI "$H$A" | grep -i '^cache-control'

# 3. Prefixo de API do apps/inbox e do apps/connectors, sem token, como fetch da SPA
curl -s -o /dev/null -w '%{http_code} %{content_type}\n' -H 'Sec-Fetch-Mode: cors' "$H/messages/summary"
curl -s -o /dev/null -w '%{http_code} %{content_type}\n' -H 'Sec-Fetch-Mode: cors' "$H/connectors/providers"
```

| Checagem | Aprovado quando |
|---|---|
| 1. Shell | `cache-control: no-store` nas **8** linhas (4 paths × 2 formas). Linha vazia ou outro valor reprova |
| 2. Assets | `cache-control: max-age=14400` (injetado pelo Cloudflare), **sem** `no-store`. Se aparecer `no-store`, o header vazou para os assets e o nginx está errado |
| 3. Prefixo de API | `401` **sem** `text/html` nas duas linhas. `200 text/html` quer dizer que o prefixo caiu no fallback de SPA: faltou prefixo no `nginx.conf`. `502` em `/connectors`, com a página de erro do nginx, é o `apps/connectors` fora do ar (ver a verificação dele, abaixo) |

A checagem 3 não depende das checagens 1 e 2. Ela prova o roteamento do
nginx sozinha. Para conferir **todos** os prefixos, e não só `/messages`, a
lista está nos blocos `location ~` do `nginx.conf`. Cada prefixo com
`Sec-Fetch-Mode: cors` e sem token tem de responder um status do app
(`401`/`400`/`404`), nunca `200 text/html`.

**Se a checagem 1 reprovar** (o `no-store` não chega ao browser): a causa
provável é a borda, não o nginx. O Browser Cache TTL do Cloudflare em valor fixo,
em vez de *"Respect Existing Headers"*, sobrescreve o header da origem. A
correção é uma Cache Rule de exceção para `text/html` (ou para o hostname) que
preserve o `Cache-Control` da origem. É **configuração de borda, fora do
repositório**. Depois de aplicá-la, repetir a checagem 1 e registrar a
configuração aplicada em `02-HISTORICO_E_STATUS.md`, porque é estado do ambiente
que o repositório não versiona. Contexto completo: V3 do `design.md` em
`openspec/changes/archive/2026-09-18-nginx-shell-sem-cache-e-prefixo-messages/`.

**Checagem no browser só confirma que o painel funciona, não o header.** Minutos
depois de um deploy, um browser passa igual com ou sem o `no-store`. O browser
reusa o `index.html` sem `Cache-Control` por 10% do intervalo desde o
`Last-Modified`, que é a hora do build, e essa janela é praticamente zero
logo depois do deploy. A prova do `no-store` é a checagem 1. Antes de julgar o
painel no browser, usar perfil limpo, janela anônima ou "Disable cache". Um
browser que gravou a cópia errada **antes** do deploy continua servindo-a até
um F5 na URL afetada ou um Clear site data. Fechar a aba não limpa o cache HTTP.

### Verificação depois de implantar o `apps/connectors`

Rodar depois do primeiro deploy com o `apps/connectors` (#119) e depois de
trocar a chave do Google. As checagens 1 e 2 vão de fora do servidor, contra o
domínio público, com o `H` da seção anterior; a 3 e a 4, no servidor.

```
C='docker compose --env-file .env.prod -f docker-compose.prod.yml'

# 1. Prefixo /connectors chega ao app, sem token, como fetch da SPA
curl -s -o /dev/null -w '%{http_code} %{content_type}\n' -H 'Sec-Fetch-Mode: cors' "$H/connectors/providers"

# 2. Navegação real em /connectors cai no shell do SPA
curl -sI "$H/connectors" -H 'Sec-Fetch-Mode: navigate' | grep -i -E '^(content-type|cache-control)'

# 3. Os dois endereços internos configurados (as duas linhas devem dar 0)
$C logs connectors | grep -c 'Api:BaseUrl não configurado'
$C logs api        | grep -c 'Connectors:BaseUrl não configurado'

# 4. Saída para o Google, de dentro do contêiner, por IPv4 e por IPv6
$C exec connectors sh -c 'for f in -4 -6; do curl $f -sS -o /dev/null --max-time 10 -w "$f %{http_code} %{time_total}s\n" -X POST https://oauth2.googleapis.com/token; done'
```

| Checagem | Aprovado quando |
|---|---|
| 1. Prefixo | `401` **sem** `text/html`. `200 text/html`: faltou o bloco no `nginx.conf`. **`502` com a página de erro do nginx (`text/html`, a mesma que `/agents` ou `/messages` dariam com o app deles fora): o nginx roteou, mas o `apps/connectors` não está no ar.** O que separa os dois casos é o status: `200` é o fallback, `502` é o upstream fora Ver `$C ps connectors` e `$C logs connectors`; a causa mais provável é a chave do Google presente e inválida, que reprova o boot nomeando a verificação que falhou (sem trecho do valor). O painel e as rotas dos outros apps continuam funcionando: o `frontend` não depende do `apps/connectors` |
| 2. Navegação | `content-type: text/html` e `cache-control: no-store` |
| 3. Endereços internos | `0` nas duas linhas. Diferente de `0`: o ciclo de sincronização está desligado, ou o cadastro de base sincronizada responde `503 connectors-not-configured` |
| 4. Saída para o Google | `-4` com `400` (pedido sem corpo) em menos de 1 s. Se `-6` **estourar os 10 s**, em vez de falhar na hora, a rede tem rota IPv6 sem conectividade, e o .NET tenta o IPv6 primeiro. A prova pelo próprio .NET é a checagem 5 |
| 5. Painel | em `/knowledge-bases/new`, com browser limpo: a origem "Sincronizada" habilitada, listando `google-drive` com o e-mail da conta; abrir o seletor de pasta lista as pastas em segundos |
| 6. Cadastro | criar uma base sincronizada numa pasta de teste responde com sucesso. **Nunca** `connectors-not-configured` nem `connectors-unavailable` |
| 7. Primeira sincronização | em até 5 minutos (ou logo depois de "Sincronizar agora"), o detalhe da base mostra uma sincronização concluída sem erro, e os documentos suportados da pasta aparecem na lista |

**Se a checagem 5 demorar perto de 30 s, ou responder `provider-unavailable`,
com a 4 mostrando o `-6` estourando:** acrescente
`DOTNET_SYSTEM_NET_DISABLEIPV6: "1"` ao `environment:` do `connectors`, rode
`$C up -d connectors` e repita a 5. Registre no `02`: é estado da rede do
servidor. O mesmo problema apareceu duas vezes na máquina de desenvolvimento
(D7 da `implantar-connectors-producao`).

**Erros da conta do Google, na checagem 5 ou 7:**
- `502 api-not-configured`: Drive API desativada no projeto da service
  account;
- arquivos ignorados com `download-blocked`: a pasta não permite download para
  leitores;
- `access-denied` numa pasta que **já foi compartilhada** com o e-mail da conta,
  sendo pasta de **Google Workspace** ou de **Drive Compartilhado**: a causa
  provável é a política de compartilhamento externo da organização, que não foi
  medida ([#114](https://github.com/aldovrando-oliveira/buteco-agentes/issues/114)).
  Confira, nesta ordem: (a) o e-mail no `detail` da recusa é o mesmo com que a
  pasta foi compartilhada; (b) a pasta aparece no nível de cima do seletor; (c)
  com o administrador do Workspace, se o compartilhamento com contas de fora da
  organização está permitido para a unidade organizacional do dono da pasta e,
  em Drive Compartilhado, se a opção de membros externos está ligada. Registre o
  resultado na #114.

## 3. Variáveis por processo

| Variável (host, `.env.prod`) | Chave de configuração | Processo(s) | Obrigatória | Idêntica entre processos? |
|---|---|---|---|---|
| `POSTGRES_USER`/`POSTGRES_PASSWORD` | (usada só na connection string, montada por serviço) | postgres, migrator, api, inbox, workers | sim | N/A — mesmo servidor, `Database=` muda por app |
| `RABBITMQ_USER`/`RABBITMQ_PASSWORD` | (usada só na connection string) | rabbitmq, api, workers | sim | N/A |
| `PUBLIC_DOMAIN` | `PublicUrl__BaseUrl` | api (externa — `AgentCard.SupportedInterfaces`), inbox (D8 — mesmo domínio, ver nota abaixo) | sim | mesmo valor nos dois, mas por motivos diferentes (não é o mesmo requisito, é coincidência desta change) |
| `TZ` | `TZ` (env var direta, não `IConfiguration`) | workers | sim, fail-fast | N/A |
| `OPENAI_BASE_URL`/`OPENAI_API_KEY` | `OpenAI__BaseUrl`/`OpenAI__ApiKey` | api (só checa presença), workers (usa de verdade) | sim (workers) | precisa ser a mesma chave configurada nos dois |
| `ANTHROPIC_API_KEY`/`GEMINI_API_KEY` | `Anthropic__ApiKey`/`Gemini__ApiKey` | api (só checa presença), workers (usa de verdade) | não | vazias, os provedores não aparecem em `GET /providers` |
| `EMBEDDING_PROVIDER`/`EMBEDDING_MODEL`/`EMBEDDING_DIMENSIONS` | `Embedding__Provider`/`__Model`/`__Dimensions` | workers | **sim** — o compose falha sem `MODEL` e `DIMENSIONS` | N/A. Sem elas o boot **passa** e a **primeira indexação** falha com modelo vazio e dimensão 0. A coluna do índice é `vector(4096)`, fixa na migration |
| `MCP_CREDENTIAL_ENCRYPTION_KEY` | `Mcp__CredentialEncryptionKey` | api (cifra), workers (decifra) | sim | **sim, byte-idêntica** |
| `INBOX_CREDENTIAL_ENCRYPTION_KEY` | `Inbox__CredentialEncryptionKey` | inbox | sim | N/A — só um processo |
| `AUTH_TOKEN_SIGNING_KEY` | `Auth__TokenSigningKey` | api, inbox, connectors | sim, fail-fast | **sim, byte-idêntica** (risco aceito abaixo, #117) |
| `GOOGLE_DRIVE_SERVICE_ACCOUNT_KEY_BASE64` | `GoogleDrive__ServiceAccountKeyBase64` | connectors | não — vazia, o provedor `google-drive` não aparece; **inválida, o `apps/connectors` não sobe** | N/A |
| `AUTH_OPERATOR_USERNAME`/`AUTH_OPERATOR_PASSWORD_HASH` | `Auth__OperatorUsername`/`Auth__OperatorPasswordHash` | api | sim, fail-fast | N/A |
| `STACK_HTTP_PORT` | (porta do host, não config do app) | frontend (nginx do stack) | sim | N/A |

O compose garante os valores idênticos via interpolação `${VAR}` — a
mesma variável do host referenciada em cada serviço, nunca copiada à
mão (`docker-compose.prod.yml`, design.md D7).

Três valores **não** vão no `.env.prod`, porque não são segredo nem variam por
ambiente; ficam fixos no compose: `Connectors__BaseUrl=http://connectors:8080`
no `api` (validação de pasta), `Api__BaseUrl=http://api:8080` no `connectors`
(ciclo de sincronização) e o build arg `VITE_CONNECTORS_BASE_URL=""` do
`frontend`, que só existe junto com o bloco `/connectors` do `nginx.conf`.

### Risco aceito: placeholder `changeme` não é rejeitado no boot

*(Premissa alterada em 19/09/2026: este aceite foi feito antes de haver
deploy atendendo tráfego real, e hoje vale contra um piloto em produção. A
decisão não foi revista aqui; está registrada como candidata em
[`02-HISTORICO_E_STATUS.md`](../02-HISTORICO_E_STATUS.md).)*

Nenhuma das chaves acima rejeita especificamente o valor `changeme*` do
`.env.prod.example` — a checagem de startup existente só cobre
ausência/vazio (`Auth__TokenSigningKey`, `Auth__OperatorUsername`/
`PasswordHash`), não o conteúdo. Se `.env.prod` for copiado sem editar,
o stack sobe normalmente com segredos públicos e previsíveis (estão no
repo). **Confira manualmente, antes de cada deploy, que nenhum valor em
`.env.prod` começa com `changeme`** — decisão consciente de escopo desta
change, não mitigada em código.

### Risco aceito: chave de assinatura compartilhada por três processos (#117)

*(Decisão do mantenedor na #119, 05/10/2026, registrada também na
[#117](https://github.com/aldovrando-oliveira/buteco-agentes/issues/117), que
continua aberta com `aguardando gatilho`.)*

O `apps/connectors` vai para produção com a mesma `Auth__TokenSigningKey` do
`apps/api` e do `apps/inbox`. Quem obtiver a chave de **qualquer um** dos três
processos consegue assinar um token de operador e tem o acesso inteiro. A
restrição de subject (#102, #103, #116) protege contra defeito de código, não
contra roubo da chave. O tipo de risco já existia: o `apps/inbox` guarda a chave
e é exposto pelos webhooks. O `apps/connectors` soma a credencial do Google e
fala com API externa. **Gatilho para rever:** a entrada do segundo cliente, ou
dados mais sensíveis no piloto, o que vier primeiro.

### Item futuro: `PublicUrl__BaseUrl` de `apps/inbox` com semântica dupla

Hoje `apps/inbox` usa o mesmo `PUBLIC_DOMAIN` de `apps/api`, porque o
requisito mais restritivo (o `setWebhook` do Telegram, que exige URL
pública HTTPS) domina. Na prática, isso faz `apps/workers` entregar push
notification pra `apps/inbox` saindo pela internet pública e voltando
pelo mesmo domínio, em vez de usar o nome de serviço interno
(`http://inbox:8080`) — funciona, mas depende de egress/DNS/Cloudflare
saudáveis para uma comunicação que poderia ser interna. Corrigir isso
exigiria separar `PublicUrl__BaseUrl` em duas variáveis (uma pro
provisionamento de webhook externo, outra pra entrega de push
notification interna) — reconhecido como a correção certa, mas **não
implementado nesta change**. Gatilho: latência perceptível ou instância
de falha de entrega de push notification atribuível a esse ir-e-volta.

## 4. Non-Goals explícitos desta change

- **CI/CD** — build/push de imagem é manual.
- **Orquestrador além do compose** (Kubernetes, Swarm, etc.).
- **Observabilidade/métricas** (Prometheus, dashboards, etc.).
- **Múltiplas réplicas de `apps/workers`/`apps/inbox`/`apps/connectors`** —
  nunca usar `replicas > 1` pra esses três serviços neste compose, cada um pelo
  próprio motivo. `apps/connectors`: o lock do ciclo de sincronização é **em
  memória** (#105); duas réplicas não corrompem dados, mas dobram o consumo de cota
  do Google e rodam dois ciclos da mesma base ao mesmo tempo. `apps/workers`:
  decisão de **disponibilidade**, em aberto na #48; a capacidade se conta em slots
  (`réplicas × TaskConsumer__MaxConcurrentExecutions`, default 4) e não é motivo
  para a réplica única. `apps/inbox`: decisão **operacional**, sem restrição técnica
  de coordenação, a rever na #165. O `pg_advisory_lock`/`xmin` dos dois últimos
  vive no Postgres compartilhado e funciona entre instâncias.
- **TLS/certificado** — resolvido por um nginx/Cloudflare já existentes,
  fora deste stack. `docker-compose.prod.yml` nunca publica 80/443
  diretamente.
- **WAHA no compose** — instância externa já existente, tratada como
  dependência, não como serviço deste stack.
- **Correção do setup desprotegido de `TaskJobConsumer`** (RabbitMQ) —
  item em aberto já registrado em
  [`02-HISTORICO_E_STATUS.md`](../02-HISTORICO_E_STATUS.md). Esta change
  previa que ele virasse change própria, sequenciada antes do deploy. A
  sequência não se concretizou: o stack de servidor está no ar sem essa
  correção, e o item continua aberto no `02`.
