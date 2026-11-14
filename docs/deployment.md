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
| `IDENTITY_SIGNING_KEY_ENCRYPTION_KEY` | `Identity__SigningKeyEncryptionKey` | api (cifra das chaves do emissor OIDC) | sim, fail-fast (o emissor está ligado, porque `PUBLIC_DOMAIN` é obrigatória) | N/A — só um processo. **Distinta** das outras chaves |
| `IDENTITY_TWO_FACTOR_ENCRYPTION_KEY` | `Identity__TwoFactorEncryptionKey` | api (cifra do segredo do TOTP de cada usuário, #218; a verificação é obrigatória desde a #220) | sim, fail-fast | N/A — só um processo. **Distinta** das outras chaves: o boot recusa a igual a `IDENTITY_SIGNING_KEY_ENCRYPTION_KEY` ou a `MCP_CREDENTIAL_ENCRYPTION_KEY` |
| `SERVICE_CLIENT_SECRET_INBOX` | `Identity__ServiceClients__Inbox__Secret` e `ServiceClient__Secret` | api (segredo do cliente `buteco-servico-inbox` do emissor, #234) e inbox (que pede com ele o token de serviço, #235) | sim, fail-fast | **Distinto** do outro segredo de serviço e das três chaves de cifra do api, e de `INBOX_CREDENTIAL_ENCRYPTION_KEY`: o boot recusa o igual, nomeando as duas chaves |
| `SERVICE_CLIENT_SECRET_CONNECTORS` | `Identity__ServiceClients__Connectors__Secret` e `ServiceClient__Secret` | api (segredo do cliente `buteco-servico-connectors`, #234) e connectors (#235) | sim, fail-fast | **Distinto** do outro segredo de serviço e das três chaves de cifra do api |
| `INBOX_CREDENTIAL_ENCRYPTION_KEY` | `Inbox__CredentialEncryptionKey` | inbox | sim | N/A — só um processo |
| `GOOGLE_DRIVE_SERVICE_ACCOUNT_KEY_BASE64` | `GoogleDrive__ServiceAccountKeyBase64` | connectors | não — vazia, o provedor `google-drive` não aparece; **inválida, o `apps/connectors` não sobe** | N/A |
| `AUTH_OPERATOR_PASSWORD_HASH` | `Auth__OperatorPasswordHash` | api | não (sem `:?` desde a #217); só o bootstrap a lê | N/A. Hash inicial do primeiro Admin. Ver "Primeiro Admin", abaixo. `AUTH_OPERATOR_USERNAME` não é mais lida desde a #217, e só volta num rollback para antes dela |
| `AUTH_BOOTSTRAP_ADMIN_EMAIL` | `Auth__BootstrapAdminEmail` | api | não (sem `:?`) | N/A. Ver "Primeiro Admin", abaixo |
| `STACK_HTTP_PORT` | (porta do host, não config do app) | frontend (nginx do stack) | sim | N/A |

O compose garante os valores idênticos via interpolação `${VAR}` — a
mesma variável do host referenciada em cada serviço, nunca copiada à
mão (`docker-compose.prod.yml`, design.md D7).

Três valores **não** vão no `.env.prod`, porque não são segredo nem variam por
ambiente; ficam fixos no compose: `Connectors__BaseUrl=http://connectors:8080`
no `api` (validação de pasta), `Api__BaseUrl=http://api:8080` no `connectors`
(ciclo de sincronização) e o build arg `VITE_CONNECTORS_BASE_URL=""` do
`frontend`, que só existe junto com o bloco `/connectors` do `nginx.conf`.

### Provedores federados (#189)

**O que muda:**

- o login ganha a **entrada pelo Google**, atrás do emissor do Buteco: o `api` é cliente OIDC do
  Google, e o painel continua falando só com o emissor. O token que os apps validam não muda;
- **o login pelo Google também pede o código das 2 etapas do Buteco**; quem nunca cadastrou a
  verificação cadastra no primeiro login;
- só entra quem tem a conta Google **conectada** a um usuário (Minha conta, com a senha). Não há
  entrada automática, nem por domínio; o aceite de convite pelo Google veio na #274 (abaixo);
- sem provedor configurado, **nada muda**: o login fica só com senha, como antes.

**Versão do Docker Compose: 2.24.0 ou mais novo.** O provedor chega ao contêiner por um `env_file`
opcional do `api` (`required: false`), que o Compose só aceita desde a 2.24.0. Antes do deploy, nos
servidores de produção e de homologação:

```bash
docker compose version   # precisa ser v2.24.0 ou mais nova
```

Numa versão anterior, o `docker-compose.prod.yml` **reprova no parse** (`env_file` com formato
inválido), sem subir nada: a falha é ruidosa, não silenciosa. Se não der para atualizar o Compose, a
alternativa vale em todo Compose v2: trocar a forma longa pela curta
(`env_file: ./federated-providers.env`) e manter o arquivo **sempre presente**, vazio quando não há
provedor, criado no primeiro deploy com `install -m 600 /dev/null federated-providers.env`. O custo é
o `up` reprovar se alguém apagar o arquivo.

**O cliente OAuth no Google (o dono cria; nenhum segredo no repositório):**

1. no Google Cloud Console, um projeto e a **tela de consentimento OAuth** (tipo "Interno" se a conta
   for do Google Workspace; "Externo" em teste, com os usuários de teste listados);
2. um **ID do cliente OAuth** do tipo "Aplicativo da Web", com a URI de redirecionamento autorizada
   `https://<PUBLIC_DOMAIN>/auth/federated/callback` (e, para desenvolvimento,
   `http://localhost:5173/auth/federated/callback`, que o proxy do Vite leva ao `api`). **Sem origem
   JavaScript**: o painel não fala com o Google;
3. o `ClientId` e o segredo no `federated-providers.env`, ao lado do `.env.prod`, com permissão 600
   (o exemplo é o `federated-providers.env.example`):

   ```bash
   Identity__FederatedProviders__corporativo__Type=google
   Identity__FederatedProviders__corporativo__ClientId=<o ID do cliente>
   Identity__FederatedProviders__corporativo__ClientSecret=<o segredo do cliente>
   # opcional: Identity__FederatedProviders__corporativo__HostedDomain=empresa.com
   ```

   A chave (`corporativo`) é sua; ela vai nas rotas e no motivo das recusas. O segredo só vale por
   variável de ambiente: num arquivo JSON de configuração, o boot reprova.

**Deploy:** a imagem do `api`, com a migration `AddUserFederatedIdentities` (a tabela
`user_federated_identities` e dois índices únicos; só acréscimo). O nginx já leva `/auth` ao `api`.

**Conferir:**

- `curl -s https://<PUBLIC_DOMAIN>/auth/federated/providers` lista a chave e o tipo, e nada mais;
- `curl -s -X POST https://<PUBLIC_DOMAIN>/auth/federated/refusal -H 'Content-Type: application/json'
  -H 'Origin: https://<PUBLIC_DOMAIN>' -d '{}'` responde `404` vazio (sem o cookie da recusa, a rota
  não revela nada);
- em Identidade (`GET /identity/federated-providers`, Admin), o provedor sem segredo, com a URI de
  retorno que foi registrada no Google;
- o checklist da conferência com o cliente real, no `tasks.md` da change `provedores-federados-google`
  (13.2 a 13.8): o login de uma conta conectada pedindo o código, os cookies em Chrome, Firefox e
  Safari, a conta sem acesso caindo em `sem-acesso`.

**Voltar atrás:** a imagem anterior do `api`. A tabela fica, ignorada, e os vínculos voltam a valer ao
subir de novo. Na #189 ninguém fica só com Google (quem cria essa pessoa é o aceite pelo Google, #274):
todo vinculado ainda entra com senha. O `federated-providers.env` pode ficar; a imagem anterior não
o lê.

#### Aceite de convite pelo Google (#274)

**O que muda:** quem recebe um convite pode aceitá-lo pelo Google, sem criar senha, a partir do link
do convite (`POST /auth/invitation/federated/<chave>/start`). O e-mail que o Google devolve
**verificado** tem de ser igual ao do convite; o aceite grava o vínculo e já abre o login, no cadastro
das 2 etapas. Com outro e-mail, e-mail não verificado, convite que deixou de valer ou conta Google já
de outro usuário, o painel volta ao convite com o motivo, e nada é gravado. A pessoa fica sem senha: a
primeira vem do link de redefinição que um Admin gera (o `password-reset-link` só serve Admin, acima).

**Também:** com a chave do provedor trocada entre o início e o retorno (renomeada e o `api`
reiniciado), o retorno respondia `500`; agora recusa com `falha` (#276), no login, no conectar e no
aceite.

**O log:** o OpenIddict gravava o `sub` e o e-mail do Google (o userinfo) e o `code_verifier` do pedido
de token em `Information`, também em produção (#277, desde a #189). A imagem nova traz o filtro
`Logging:LogLevel:OpenIddict` = `Warning` no `appsettings.json`. **Não sobrescreva esse nível para
`Information` ou menos** (por exemplo `Logging__LogLevel__OpenIddict` no `.env.prod`): o log volta a levar
dado pessoal. O log já gravado antes do deploy pode ter esses dados; trate-o conforme a retenção do host.

**Deploy:** só a imagem do `api`. Sem migration e sem variável nova: o provedor é o da #189; o filtro de
log vem na imagem.

**Conferir:**

- com um convite pendente, `POST /auth/invitation/federated/<chave>/start` com
  `{"token":"<token do link>"}`, `Content-Type: application/json` e `Origin: https://<PUBLIC_DOMAIN>`
  responde `200` com o `url` do Google; com um link vencido, o mesmo `404` "Este link não vale mais…"
  da consulta do convite;
- o roteiro com o cliente real, no `tasks.md` da change `aceite-convite-google` (10.2 a 10.5): o aceite
  com o e-mail certo caindo no cadastro das 2 etapas; outro e-mail voltando a
  `/convite?recusa=outro-email`; o cancelamento no Google; o convite cancelado no meio.

**Voltar atrás:** a imagem anterior do `api` (a da #189). Quem aceitou pelo Google continua ativo, sem
senha, com o vínculo, e **entra** pelo login pelo Google da #189. Um aceite em voo na hora da troca
cai, na imagem anterior, no login (a intenção `convite` é desconhecida para ela): sem vínculo e
convidado, a recusa é `convite-pendente`, sem sessão e com o convite intacto. Nada a desfazer no banco.

#### Telas do login e do convite pelo Google (#190)

**O que muda:** a M3 passa a ter tela. O `/entrar` mostra "Entrar com Google" quando há provedor
configurado (a lista vem de `GET /auth/federated/providers`; sem provedor, o login fica como antes, só com
senha) e os avisos de cada recusa; o `/convite` ganha "Continuar com Google" ao lado da senha. Minha conta
(conectar e desconectar), Usuários e Identidade, com as contas conectadas, são da #279.

**Deploy:** só a imagem do `frontend`. Sem variável nova; o provedor é o da #189.

**Conferir:** com provedor configurado, o botão aparece no `/entrar` e no `/convite`; o roteiro com o
cliente real, agora pelo painel, no `tasks.md` da change `telas-login-google` (10.5 a 10.7).

**Voltar atrás:** a imagem anterior do `frontend`. As rotas continuam no `api`; só os botões somem.

### Aplicações externas com credencial do Buteco (#186)

**O que muda:**

- o Admin passa a cadastrar **aplicações externas** (rotas `/applications/*` do `api`; a tela veio na
  #188, ver "Telas das aplicações externas"): cada uma recebe um client ID `btc_app_…` e segredos
  `btc_sk_…`, mostrados uma vez;
- a aplicação obtém token em `/connect/token` por `client_credentials`, com `scope=buteco-api`, e o
  token dura **5 minutos**; revogar o segredo ou desativar a aplicação recusa o próximo pedido de
  token na hora, e o token já emitido vale até vencer;
- **a aplicação ainda não alcança nenhum agente**: o token recebe `403` em toda rota do `api`,
  inclusive no A2A, até a #187 liberar agentes por aplicação;
- o AgentCard de cada agente passa a declarar o esquema OAuth2 do Buteco no lugar do `bearer`;
- só o `api` muda. O `inbox` e o `connectors` não aceitam o token da aplicação (a audience dele é só
  `buteco-api`), e não precisam de imagem nova.

**Antes do deploy (o dono):** backup do banco do `api`, como em toda migration:

```
docker compose -f docker-compose.prod.yml --env-file .env.prod exec -T postgres \
  sh -c 'pg_dump -U "$POSTGRES_USER" -Fc buteco_agents' > buteco_agents-antes-186.dump
```

**Deploy:** o `migrator` aplica a `ExternalApplications` (duas tabelas novas,
`external_applications` e `external_application_secrets`; nenhuma tabela existente muda) antes do
`api`, e depois:

```
docker compose -f docker-compose.prod.yml --env-file .env.prod up -d api
```

Sem variável de ambiente nova.

**O que o OpenIddict passa a guardar:** uma linha em `openiddict_applications` por aplicação
criada, com o `client_id` `btc_app_…`, as permissões do endpoint de token, do `client_credentials` e
do escopo `buteco-api`, e um **segredo de enchimento** que ninguém conhece (o OpenIddict exige um
segredo em cliente confidencial; quem confere os segredos da aplicação é o `api`). Nenhuma linha em
`openiddict_tokens` nem em `openiddict_authorizations`.

**Conferir:** com o Admin, `POST /applications/` cria a aplicação e devolve o segredo; o pedido de
token com ele responde `expires_in` `300`; e o AgentCard de um agente traz `securitySchemes.oauth2`
com o `tokenUrl` da url pública.

**Voltar atrás:** a imagem anterior do `api` não conhece os segredos das aplicações e confere o
segredo de enchimento, que ninguém conhece: **toda aplicação recebe `invalid_client`**, e os tokens
já emitidos vencem em até 5 minutos. O painel e os clientes de serviço não mudam. A volta falha
fechada e não precisa de passo manual. As tabelas novas ficam (a imagem anterior não as lê); para
apagá-las, o `down` da migration `ExternalApplications`. As linhas das aplicações em
`openiddict_applications` ficam órfãs e inofensivas; para limpá-las:

```
docker compose -f docker-compose.prod.yml --env-file .env.prod exec -T postgres \
  sh -c 'psql -U "$POSTGRES_USER" buteco_agents -c "DELETE FROM openiddict_applications WHERE \"ClientId\" LIKE '"'"'btc\_app\_%'"'"';"'
```

Voltar para a frente depois do `down` perde os segredos: as aplicações precisam ser recriadas.

### Agentes liberados por aplicação externa (#187)

**O que muda:**

- o Admin passa a liberar agentes para cada aplicação externa, com as operações por agente
  (rotas `/applications/{id}/agents` do `api`; a tela veio na #188). A aplicação nasce sem agentes;
- o token de aplicação passa a alcançar `POST /agents/{id}/a2a`, só nos agentes liberados: sem
  vínculo, a resposta passa de `403` a `404`; operação não liberada, `403`;
- cada task criada por uma aplicação guarda o dono, e a aplicação só vê as próprias;
- só o `api` muda. O `workers` e o `inbox` não precisam de imagem nova.

**Antes do deploy (o dono):** backup do banco do `api`, como em toda migration:

```
docker compose -f docker-compose.prod.yml --env-file .env.prod exec -T postgres \
  sh -c 'pg_dump -U "$POSTGRES_USER" -Fc buteco_agents' > buteco_agents-antes-187.dump
```

**Deploy:** o `migrator` aplica a `ExternalApplicationAgents`:

- a tabela nova `external_application_agents`;
- a coluna anulável `a2a_tasks.application_id`, sem default, que não reescreve a tabela;
- o índice e a FK dessa coluna. O índice é criado sem `CONCURRENTLY`, e trava as escritas em
  `a2a_tasks` enquanto é construído, por um tempo proporcional ao tamanho da tabela (não medido em
  produção; confira o `count(*)` de `a2a_tasks` antes, se quiser saber).

Durante a migration, o `api` e o `workers` antigos continuam de pé (a ordem do §2) e podem esperar
esse tempo numa gravação de task. Depois:

```
docker compose -f docker-compose.prod.yml --env-file .env.prod up -d api
```

Sem variável de ambiente nova. A ordem de deploy com a #188 (as telas) é livre: sem as telas,
nenhum vínculo é criado, e a aplicação só passa de `403` a `404` na A2A.

**Conferir:** com o Admin, `POST /applications/{id}/agents` libera um agente; o `SendMessage` da
aplicação nele cria a task, e num agente não liberado responde `404`.

**Liberar um agente sem a tela (alternativa).** Desde a #188, o caminho principal é a aba Agentes do
detalhe da aplicação no painel. O roteiro abaixo fica como alternativa (painel fora do ar, automação):
o Admin libera pela API, com o access token do painel dele. O token está no cabeçalho `Authorization` de qualquer
chamada do painel ao `api` (no navegador, em Ferramentas do desenvolvedor → Rede), e vale 15 minutos.
As rotas exigem o papel Admin gravado; não pedem senha.

```
TOKEN='<access token do painel do Admin>'
API=https://[URL_PUBLICA]

# os ids: a aplicação (GET /applications/) e o agente (GET /agents/)
curl -s "$API/applications/" -H "Authorization: Bearer $TOKEN"
curl -s "$API/agents/" -H "Authorization: Bearer $TOKEN"

# liberar, com as operações: enviar-mensagem, consultar-task, notificacao-push
curl -s -X POST "$API/applications/<id-da-aplicacao>/agents" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"agenteId":"<id-do-agente>","operacoes":["enviar-mensagem","consultar-task"]}'

# mudar as operações (a lista inteira) e retirar
curl -s -X PUT "$API/applications/<id-da-aplicacao>/agents/<id-do-agente>" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"operacoes":["consultar-task"]}'
curl -s -X DELETE "$API/applications/<id-da-aplicacao>/agents/<id-do-agente>" \
  -H "Authorization: Bearer $TOKEN"

# conferir
curl -s "$API/applications/<id-da-aplicacao>/agents" -H "Authorization: Bearer $TOKEN"
```

A liberação vale na próxima chamada da aplicação, sem token novo. Respostas de erro:
- `409`: o agente já está liberado (use o `PUT`);
- `400`: lista de operações vazia ou com valor desconhecido;
- `404`: a aplicação não existe, ou, no `PUT`/`DELETE`, o agente não está liberado.

**Voltar atrás:** a imagem da #186 tem a lista de rotas da aplicação vazia, e **a aplicação volta a
receber `403` em toda rota, inclusive na A2A**. O painel e o inbox não mudam. Os vínculos e o dono
das tasks ficam no banco, sem uso (a imagem anterior não os lê), e voltam a valer num novo deploy da
#187. Não precisa reverter a migration; se quiser, o `down` da `ExternalApplicationAgents` apaga a
tabela e a coluna.

### Telas das aplicações externas (#188)

**O que muda:** o painel ganha "Aplicações externas" na seção Acesso, só para o Admin: a lista, a
criação com a senha e o segredo mostrado uma vez, o detalhe com as abas Agentes e Credencial e
conexão (gerar, rotacionar e revogar segredos; liberar, editar e retirar agentes), a edição de nome e
descrição, e a desativação. No detalhe de cada agente, o card "Aplicações com acesso": a lista para o
Admin, só a contagem para Operador e Leitor. **Só o `frontend` muda**: nenhuma rota nem resposta do
`api` (as da #186 e da #187 servem tudo), sem migration e sem variável nova.

**Deploy:** só a imagem do `frontend`. A #186 e a #187 podem estar no ar antes; se forem juntas, a
ordem de sempre (o `migrator`, o `api`, depois o `frontend`):

```
docker compose -f docker-compose.prod.yml --env-file .env.prod up -d frontend
```

**Conferir:** como Admin, o item "Aplicações externas" entre Usuários e Identidade; criar uma
aplicação mostra o cartão "Aplicação criada" com o client ID, o segredo e o endpoint, e "Ir para a
aplicação" só depois de marcar "Guardei o segredo"; liberar um agente na aba Agentes; o card do
agente lista a aplicação. Como Operador ou Leitor, o menu sem a seção Acesso, `/applications`
digitado mostra "Você não tem acesso a esta página.", e o card do agente mostra só "Usado por N
aplicações externas ativas (M no total).".

**Voltar atrás:**
- **só o `frontend`:** seguro. Nada se grava diferente; aplicações, segredos e vínculos ficam no banco
  e continuam valendo. O Admin volta ao roteiro por `curl` ("Liberar um agente sem a tela") e o card
  do agente some;
- **o backend (#186/#187) com o `frontend` da #188 no ar:** o menu aparece e as rotas não existem — a
  lista mostra o erro de carga e o card do agente, o erro do card. Por isso o recuo do backend leva o
  `frontend` junto, como na #185.

### O nginx do painel encaminha `/applications` ao `api` (#268)

**O que muda:** a regex do bloco do `api` no `apps/frontend/deploy/nginx.conf` ganha `applications`.
Sem ela, o `GET /applications/` do painel caía no fallback do SPA e recebia o `index.html` (`200`,
`text/html`), e a lista de Aplicações externas não carregava: o defeito da homologação da M2. A #186
criou as rotas sem o prefixo no nginx, e a #188 fez a tela. A navegação real em `/applications/*`
continua no shell do SPA, pelo desvio de navegação que o bloco já tinha. Desde esta change, um teste
na suíte de cada app (`api`, `inbox`, `connectors`) compara as rotas do host com os blocos do
nginx, e uma rota nova sem prefixo reprova ali, e não na homologação.

**Deploy:** só a imagem do `frontend`, a qualquer momento depois da #188. O `api` já serve
`/applications`. **Na homologação, ela substitui a correção manual** (o `sed` que acrescentou
`applications` à regex dentro do contêiner): a imagem nova traz a mesma regex, sem passo extra.

```
docker compose -f docker-compose.prod.yml --env-file .env.prod up -d frontend
```

**Conferir:** como Admin, "Aplicações externas" carrega a lista. Pelo `curl`, com o token do painel,
`GET /applications/` responde `application/json`; sem token, `401` do `api`; com
`Sec-Fetch-Mode: navigate`, o `index.html` com `Cache-Control: no-store`.

**Voltar atrás:** a imagem anterior do `frontend`, que traz o defeito de volta (a lista recebe HTML).
Nada se grava diferente.

### Papéis Admin, Operador e Leitor (#184, #185)

**A #184 (backend) e a #185 (painel) vão juntas para produção** (decisão do dono, 08/11/2026).
Com a #184, o `papel` passa a ser obrigatório no convite, e o "Convidar" do painel de antes da
#185, que não o envia, recebe `400`; a #185 é o painel que escolhe o papel, esconde o que o papel
não pode fazer e trata o `403`.

**O que muda:**

- cada usuário tem um papel: **Admin** (gere usuários, convites e a identidade, e tudo o que o
  Operador faz), **Operador** (gere agentes, MCP, conhecimento, canais e conectores, e vê o
  histórico de conversas) e **Leitor** (lê agentes, MCP, conhecimento, canais e o Insights; não vê
  o histórico de conversas nem usa os conectores);
- a migration `AddUserRole` dá **Admin a todo usuário que já existe**, o do bootstrap e os convites
  pendentes inclusive: ninguém perde acesso no deploy;
- o papel vai no token, e os três apps (`api`, `inbox`, `connectors`) o conferem por rota. As
  rotas de sincronização e o A2A ficam só dos serviços;
- **o rebaixamento** vale na requisição seguinte em Usuários, convites, redefinições e Identidade, e
  em até 15 min 30 s no resto (a vida do access token já emitido). A promoção chega na renovação
  seguinte do painel;
- a verificação em 2 etapas continua obrigatória para todos os papéis;
- **no painel (#185):** a seção "Acesso" (Usuários e Identidade) só aparece para o Admin; o
  Leitor não vê os botões de escrita, o histórico de conversas nem o "Sincronizar agora", e abre
  o canal em Configuração; a URL de uma página que o papel não abre mostra "Você não tem acesso a
  esta página."; o rodapé mostra o papel na segunda linha, no lugar do e-mail. Quando o servidor
  recusa com `403`, o painel renova a sessão uma vez (sem repetir o pedido), e o menu se atualiza
  com o papel novo.

**Antes do deploy (o dono):**

1. backup do banco do `api`:

   ```
   docker compose -f docker-compose.prod.yml --env-file .env.prod exec -T postgres \
     sh -c 'pg_dump -U "$POSTGRES_USER" -Fc buteco_agents' > buteco_agents-antes-184.dump
   ```

2. as imagens do `api`, do `inbox`, do `connectors` **e do `frontend`** sobem juntas. Um `inbox` ou
   um `connectors` de antes continua aceitando o token do painel sem olhar o papel, o que é mais
   permissivo, e não quebra nada; um `frontend` de antes deixa o "Convidar" com `400`.

**Deploy:** o `migrator` aplica a `AddUserRole` antes do `api`, como sempre, e depois:

```
docker compose -f docker-compose.prod.yml --env-file .env.prod up -d api inbox connectors frontend
```

**Conferir:**

```
docker compose -f docker-compose.prod.yml --env-file .env.prod exec postgres \
  sh -c 'psql -U "$POSTGRES_USER" -d buteco_agents -c "SELECT role, count(*) FROM users GROUP BY role"'
```

- só `admin` na resposta;
- entrar no painel: o primeiro pedido de cada sessão aberta antes do deploy recebe `401` e o painel
  renova sozinho, sem voltar à tela de login (o token de antes não tem papel); o rodapé passa a
  dizer "Admin";
- em Usuários, a coluna Papel com "Admin" em todos;
- convidar alguém como **Leitor** (o "Convidar" pede o papel) e responder `201`; entrar com esse
  convite: o menu não tem "Acesso", a lista de agentes não tem "Novo agente", e `/users` digitado
  mostra "Você não tem acesso a esta página.". Depois, desativar ou cancelar esse usuário de teste.

**Depois do deploy, o dono rebaixa quem não deve ser Admin, em Editar usuário** (o cartão "Dados
e papel": escolher o papel e "Salvar alterações"; promover a Admin pede a sua senha). O rebaixado
perde Usuários e Identidade na hora, e o resto do painel muda em até 15 minutos, quando a sessão
dele renovar.

**Alternativa, pela API** (só se a tela estiver indisponível), com o access token de um Admin (no
navegador, as ferramentas do desenvolvedor → Application → Local Storage → `buteco.sessao` →
`accessToken`, que vale 15 min):

```
D=https://agentes.exemplo.com
T='<access token do Admin>'
# Rebaixar não pede senha. Papéis no corpo: admin, operador, leitor.
curl -s -X PUT -H "Authorization: Bearer $T" -H 'Content-Type: application/json' \
  -d '{"papel":"operador"}' $D/users/<id do usuário>/role
# Promover a Admin pede a senha de quem chama:
curl -s -X PUT -H "Authorization: Bearer $T" -H 'Content-Type: application/json' \
  -d '{"papel":"admin","senha":"<a sua senha>"}' $D/users/<id>/role
```

- `409` "A instalação precisa de pelo menos um Admin ativo." é o último Admin: não dá para rebaixá-lo;
- rebaixar a si mesmo é permitido enquanto houver outro Admin ativo;
- **o convite pendente** que não deve ser Admin: cancele (`POST /users/{id}/invitation/cancel`, ou
  em Usuários) e convide de novo com o papel certo. O link antigo deixa de valer.

**Voltar atrás:** a imagem anterior do `api`, do `inbox`, do `connectors` **e do `frontend`**,
juntas, **sem** rodar o `Down` da migration (ele apaga a coluna e com ela os papéis).

- **Só o `frontend`** (a #184 fica): seguro para os dados, porque nada se grava diferente; o
  "Convidar" volta a dar `400`, e o Operador e o Leitor voltam a ver todos os botões, recebendo o
  erro genérico da tela em cada um. Serve como recuo curto, se o defeito for só de tela.
- **Só o backend** (o `frontend` da #185 fica): **não faça.** A imagem anterior não emite o papel no
  token, e o painel da #185 trata o papel desconhecido como Leitor: ninguém gere usuários pela
  tela durante o recuo, embora o servidor deixasse. O recuo do backend leva o `frontend` junto.

**Antes do recuo**, porque a imagem anterior não tem papéis e devolve o poder de Admin a todo
usuário ativo:

1. liste os ativos que não são Admin:

   ```
   docker compose -f docker-compose.prod.yml --env-file .env.prod exec postgres \
     sh -c "psql -U \"\$POSTGRES_USER\" -d buteco_agents -c \"SELECT id, email, role FROM users WHERE role <> 'admin' AND status = 'active'\""
   ```

2. desative, pela rota de desativação (Editar usuário, ou `POST /users/{id}/deactivate` com o
   access token de um Admin), quem não pode ter o acesso de Admin durante o recuo. A desativação
   encerra as sessões da pessoa;
3. **depois da volta para a frente**, reative cada um (`POST /users/{id}/activate`): a pessoa entra
   de novo com a mesma senha e volta com o papel que já estava gravado.

Durante o recuo:

- a imagem anterior ignora a coluna `role`: o EF só lê as colunas do modelo dela;
- ela insere em `users` (convite, bootstrap) sem a coluna, e o banco põe o `DEFAULT 'reader'`: quem
  for convidado durante o recuo sai **Leitor**, e o dono o corrige pela rota acima quando voltar
  para a frente;
- os tokens com `role` passam na imagem anterior, que ignora o claim;
- **a imagem anterior não tem papéis**: durante o recuo, todo usuário ativo volta a ter o poder de
  Admin (um Leitor chama `GET /users` e recebe `200`), como na M1. Os papéis gravados voltam a valer
  quando a imagem da #184 voltar.

### Remoção do token antigo e da chave compartilhada (#182)

Último passo da troca do token de serviço (#234, #235, #236 e #182). Os três apps deixam de aceitar o token antigo, assinado com a chave que compartilhavam, e passam a aceitar só o access token do emissor. Desde a #235 nenhum serviço o manda, e desde a #217 nenhuma rota o emite.

- O compose deixa de entregar `Auth__TokenSigningKey` aos três serviços.
- O `inbox` e o `connectors` não sobem sem `Identity__Issuer`, e o `connectors` também não sobe sem `Api__BaseUrl`. Os dois chegam do compose: o primeiro de `PUBLIC_DOMAIN`, que o `api` já exige com `:?`, e o segundo fixo (`http://api:8080`).
- A requisição sem token recebe `401` com `WWW-Authenticate: Bearer`, também antes de o `inbox` e o `connectors` obterem as chaves. Quem traz token antes das chaves continua recebendo `503` com `Retry-After`.

**Nada a gerar, e sem migration.** **A `AUTH_TOKEN_SIGNING_KEY` fica no `.env.prod` por uma semana depois do deploy:** a imagem e o compose anteriores ainda a leem (o `api` com `:?`, e o `connectors` não sobe sem ela), e o rollback precisa dela.

**Ordem do deploy:** qualquer uma entre `api`, `inbox` e `connectors`. Ninguém manda o token antigo desde a #235, então cada app novo só deixa de aceitar o que já não chega.

```
git pull
$C build api inbox connectors
$C up -d api inbox connectors
```

**Conferência depois do deploy:**

```
# 1. O inbox e o connectors subiram com o emissor e obtiveram as chaves
$C logs inbox connectors | grep -E 'Validação do token do emissor ligada|Chaves do emissor obtidas'
# 2. Nenhum segredo de serviço divergente
$C logs inbox connectors | grep 'invalid_client'   # → nada
# 3. Sem token, cada app responde 401 com o desafio Bearer (pelo nginx do stack; de
#    fora, contra o domínio público, como fetch do painel)
H=https://<domínio público>
for p in /agents /channels /connectors/providers; do
  curl -s -o /dev/null -D - -H 'Sec-Fetch-Mode: cors' "$H$p" | grep -iE '^HTTP|^www-authenticate'
done
#    → "HTTP/2 401" e "www-authenticate: Bearer" nas três
```

- **O critério de aceite da #182:** o painel entra e abre Agentes (`apps/api`), Canais (`apps/inbox`) e "Sincronizar agora" numa base sincronizada (`apps/connectors`).
- **Os quatro fluxos entre serviços:** uma mensagem de canal chega ao agente (o cadastro do canal valida o agente, S1; o despacho chama o A2A, S2); a sincronização da base termina (S3); cadastrar uma base sincronizada valida a pasta (S4).

**Sintomas:**

| o que aparece | leitura |
|---|---|
| o `inbox` ou o `connectors` reinicia em laço, com `Identity:Issuer não configurado` | o `Identity__Issuer` não chegou ao contêiner (compose antigo, ou editado à mão). Conferir o `docker-compose.prod.yml` do checkout |
| o `connectors` reinicia com `Identity:Issuer está configurado, e Api:BaseUrl está ausente` | o `Api__BaseUrl` do `connectors` foi tirado do compose |
| `401` com `invalid_token` no log de algum app | alguém ainda manda o token antigo. Desde a #235 nenhum serviço o assina; procurar um cliente fora do stack |

**Rollback:** voltar o checkout do servidor ao commit anterior ao merge da #182 e subir as imagens anteriores (`$C build` e `$C up -d` dos três). A versão anterior aceita o token do emissor e o antigo, e lê a `AUTH_TOKEN_SIGNING_KEY`, que por isso continua no `.env.prod`.

**Uma semana depois do deploy, sem rollback**, tirar a chave do `.env.prod` e registrar a data no `02-HISTORICO_E_STATUS.md`:

```
cp .env.prod .env.prod.antes-da-retirada   # cópia local, fora do repo
sed -i '/^AUTH_TOKEN_SIGNING_KEY=/d' .env.prod
grep -c '^AUTH_TOKEN_SIGNING_KEY=' .env.prod   # → 0
$C config >/dev/null && echo 'compose ok'      # o compose atual não a lê
```

Nenhum serviço precisa subir de novo: o compose atual não entrega a variável. Depois da retirada, um rollback para antes da #182 exige recolocar a chave, que está na cópia. A cópia sai junto com a janela de rollback.

### Healthcheck do RabbitMQ (#251)

**O que muda:** o healthcheck do `rabbitmq` passa de `rabbitmq-diagnostics -q ping` (a cada
5 s) para uma conexão TCP na porta AMQP, feita dentro do contêiner pelo `bash` da imagem
(`["CMD", "bash", "-c", "</dev/tcp/127.0.0.1/5672"]`, `interval` 10s, `timeout` 5s,
`retries` 6, `start_period` 30s). Ele vale igual no compose de desenvolvimento. Nenhum app
muda, e não há migration.

**Por quê:** o `ping` sobe o runtime Erlang do CLI a cada execução, de 4 a 6 s de CPU, o que
dá cerca de um núcleo ocupado o tempo todo. Ele ainda dava `healthy` uns 3 s antes de o
listener AMQP abrir. Com a checagem TCP, o `api` e os `workers` (`depends_on: rabbitmq:
condition: service_healthy`) só sobem depois de o broker aceitar conexão.

**O deploy recria o broker, e o redeploy de rotina não o faz.** O passo 4 da seção 2 não
inclui o `rabbitmq`, então o healthcheck novo só vale com um passo explícito. Ele recria o
contêiner: são uns 12 s sem broker. As filas são duráveis e o volume
`buteco-rabbitmq-data-prod` fica. **Faça numa janela sem uso** (sem conversa em curso nos
canais nem indexação rodando):

```
docker compose --env-file .env.prod -f docker-compose.prod.yml up -d rabbitmq
```

**Conferência, nesta ordem:**

1. `docker compose --env-file .env.prod -f docker-compose.prod.yml ps`: o `rabbitmq` fica
   `healthy` (o boot leva uns 12 s, e a primeira checagem depois disso o marca);
2. `docker inspect --format '{{json .Config.Healthcheck}}' $(docker compose --env-file .env.prod -f docker-compose.prod.yml ps -q rabbitmq)`:
   o `Test` é o `bash` com o `/dev/tcp`;
3. `docker stats --no-stream`: a CPU do `rabbitmq` ocioso fica perto de zero, e não em
   um núcleo;
4. **uma task de agente de ponta a ponta:** mande uma mensagem por um canal (ou um
   `SendMessage` A2A a um agente) e confira a resposta. Ela prova que o consumidor dos
   `workers` voltou depois do reinício do broker. O `RabbitMQ.Client` 7.2.1 reconecta
   sozinho (`AutomaticRecoveryEnabled`, ligada por padrão), e os publishers do `api`
   reconectam no envio seguinte. Se a task ficar parada, `docker compose --env-file .env.prod -f docker-compose.prod.yml restart workers`
   e repita. A conexão com nova tentativa no boot do consumidor é a #253.

**Rollback:** devolva o bloco `healthcheck` anterior no `docker-compose.prod.yml`
(`["CMD", "rabbitmq-diagnostics", "-q", "ping"]`, 5s, 5s, 10) e rode o mesmo `up -d rabbitmq`,
com o mesmo reinício de uns 12 s e a mesma conferência. Nenhum dado muda.

**Se a imagem mudar:** a tag `rabbitmq:4.3-management` é móvel. Ao trocar a imagem ou a
tag, confira o `bash` antes do deploy (`docker run --rm <imagem> bash -c 'echo ok'`). A
variante `-alpine` não tem `bash`, e com ela o broker ficaria `unhealthy` (o `api` e os
`workers` não subiriam).

### Verificação em 2 etapas obrigatória e o comando de emergência (#220)

**O que muda:** a verificação em 2 etapas passa a ser obrigatória para todo usuário. Quem não
a tem cadastra um app autenticador no login, depois da senha e antes de entrar (o QR, o código
e os 10 códigos de recuperação). A opção do cadastro (`IDENTITY_TWO_FACTOR_ENROLLMENT_ENABLED`)
sai: o compose não a lê mais, e Minha conta deixa de ter "Ligar verificação" e "Remover
verificação". Sem migration. O `nginx.conf` não muda: as rotas novas ficam sob `/auth`. O
`inbox`, o `connectors` e o `workers` não mudam.

**Antes do deploy (o dono):**

1. conferir o NTP do host: `timedatectl` com "System clock synchronized: yes" (os códigos
   param de bater para todos com o relógio fora; ver a seção da #218);
2. **avisar a equipe**: depois do deploy, todos saem do painel em até 15 minutos e, ao entrar,
   cadastram um app autenticador (Google Authenticator, Microsoft Authenticator, 1Password) e
   guardam os 10 códigos de recuperação fora do celular. **Peça que entrem logo depois da
   virada**: até o primeiro login de cada pessoa, quem tiver a senha dela poderia cadastrar o
   próprio app antes;
3. ter o próprio celular à mão e o comando de emergência (abaixo) anotado.

**Deploy:** a imagem do `api` e a do `frontend`, juntas.

```
docker compose -f docker-compose.prod.yml --env-file .env.prod up -d api frontend
```

`IDENTITY_TWO_FACTOR_ENROLLMENT_ENABLED` pode sair do `.env.prod`; se ficar, nada a lê.

**Conferir:**

- o dono entra: senha → QR → código → 10 códigos → "Continuar" → painel;
- o login seguinte pede o código do app;
- em Usuários, a coluna da verificação vai ficando "Ativa" conforme as pessoas entram. Quem
  aparece "Não configurada" depois de ter entrado é sinal de cadastro feito por outra pessoa:
  redefina a verificação dela em Editar usuário;
- as rotas do cadastro chegam ao `apps/api` (401 do `apps/api`, e não 200 com o HTML do painel):

```
D=https://agentes.exemplo.com
curl -s -o /dev/null -w '%{http_code} %{content_type}\n' -X POST -H 'Content-Type: application/json' -H "Origin: $D" -d '{}' $D/auth/session/enrollment/setup
```

- o comando de emergência com um e-mail que não existe sai `1`, sem mudar nada.

**Cada usuário:** no próximo acesso, senha, QR, código e os códigos de recuperação guardados.

**O comando de emergência** (`two-factor-reset`), para quem perdeu o celular e os códigos de
recuperação, inclusive o último Admin, que não tem a quem pedir a redefinição:

```
docker compose -f docker-compose.prod.yml --env-file .env.prod run --rm --no-deps api two-factor-reset --email maria@empresa.com
```

- **o que faz:** apaga a verificação daquele e-mail, troca o carimbo de segurança e encerra
  todas as sessões da pessoa, como a redefinição pelo admin em Editar usuário (é a mesma
  operação). No próximo login, a pessoa cadastra o app de novo;
- **quem pode usar:** só quem roda `docker compose` no host do stack. Não há rota HTTP para
  isso, e o processo do comando não abre porta;
- **o registro:** uma linha `Warning` com o `EventId` 1196 na saída do comando, com o
  resultado, o e-mail, o id do usuário e as sessões encerradas. Nenhum segredo, código ou
  carimbo sai. Com `--rm`, o registro é o terminal de quem rodou: guarde-o se precisar;
- **saídas:**

| código | quando | efeito |
|---|---|---|
| `0` | redefinida | a verificação apagada, o carimbo trocado, as sessões encerradas |
| `1` | nenhum usuário com o e-mail | nenhum |
| `2` | usuário desativado | nenhum |
| `3` | usuário sem a verificação (inclusive convidado) | nenhum |
| `64` | uso errado (sem `--email`) | nenhum |
| `70` | falha ao falar com o banco, ou erro inesperado | nenhum: a transação não se completa. A linha traz só o tipo do erro |

- **não aplica migrations**: quem migra é o `migrator`.
- **a senha não entra aqui:** para o último Admin que perdeu a senha, o comando irmão é o
  `password-reset-link` ([seção abaixo](#comando-de-emergência-da-senha-password-reset-link-257)),
  com o roteiro de quem perdeu a senha e as 2 etapas.

**Voltar atrás:** a imagem anterior do `api` e do `frontend`.

- quem cadastrou continua com a verificação: a imagem anterior a pede no login (#218), e o
  painel anterior tem o passo do código (#245). Ninguém fica preso;
- quem não cadastrou volta a entrar só com a senha;
- com a variável do cadastro fora do `.env.prod`, Minha conta volta a dizer "cadastro
  indisponível" para quem não tem a verificação. Para reabri-lo, `IDENTITY_TWO_FACTOR_ENROLLMENT_ENABLED=true`
  e `up -d api` (a seção da #246, no histórico deste arquivo no git);
- o comando de emergência some com a imagem.

### Comando de emergência da senha: `password-reset-link` (#257)

**O que mudou:** o binário do `api` ganhou o comando irmão do `two-factor-reset`, para o último
Admin que perdeu a senha e não tem outro Admin a quem pedir o link em Editar usuário. Só a
imagem do `api`. Sem migration e sem variável nova.

```
docker compose -f docker-compose.prod.yml --env-file .env.prod run --rm --no-deps api password-reset-link --email maria@empresa.com
```

- **o que faz:** gera o link de redefinição de 24 h (`<painel>/redefinir-senha#<token>`), pela
  mesma operação do botão de Editar usuário: o link anterior daquela pessoa deixa de valer, e a
  senha atual, o carimbo de segurança e as sessões continuam até a senha nova ser criada;
- **quem ele atende:** só **Admin ativo**. Operador e Leitor pedem o link a um Admin no painel
  (sempre sobra um Admin ativo, #184); o convidado recebe um novo link do convite em Usuários;
- **quem só tem Google:** desde a #274, quem aceita o convite pelo Google fica **sem senha**. Um
  Operador ou um Leitor nessa situação que perde o acesso à conta Google **não é atendido pelo
  comando** (sai `4`): ganha a primeira senha pelo link de redefinição que um Admin gera no painel
  (Editar usuário, `POST /users/{id}/password-reset`), que grava a senha de quem não tinha. Só um Admin
  sem senha e sem Google, sem outro Admin, depende do comando;
- **quem pode usar:** só quem roda `docker compose` no host do stack. Não há rota HTTP, e o
  processo do comando não abre porta;
- **onde o link sai:** **uma vez, sozinho numa linha da saída padrão**. A linha `Warning` com o
  `EventId` 1197 vai à saída de erro, com o resultado, o e-mail, o id, o papel e o vencimento, e
  nunca com o link, o token ou o hash;
- **saídas:**

| código | quando | efeito |
|---|---|---|
| `0` | link gerado | o link na saída padrão; o anterior deixa de valer |
| `1` | nenhum usuário com o e-mail | nenhum |
| `2` | usuário desativado | nenhum |
| `3` | usuário convidado | nenhum: o link do convite dele continua valendo |
| `4` | usuário ativo que não é Admin | nenhum |
| `64` | uso errado (sem `--email`, vazio, argumento a mais) | nenhum |
| `70` | falha ao falar com o banco, ou erro inesperado | nenhum. A linha traz só o tipo do erro |
| `78` | url do painel ausente ou inválida (`PUBLIC_DOMAIN`) | nenhum, nem conexão com o banco. A linha cita só os nomes das chaves |

- **não aplica migrations**: quem migra é o `migrator`.

**O link é uma credencial. O cuidado de quem roda o comando:**

- **não cole o link em chat, ticket nem e-mail.** Entregue-o à própria pessoa;
- **antes de rodar, confira o driver de log do Docker:**

  ```
  docker info --format '{{.LoggingDriver}}'
  ```

  O compose não fixa um driver, e o comando passa pelo do daemon (`/etc/docker/daemon.json`).
  Com `json-file` ou `local`, o log do contêiner some com o `--rm`, ao sair. **Com outro
  (`journald`, `syslog`, `gelf`, `fluentd`, `awslogs`…), a saída fica gravada** depois do `--rm`;
- **com um driver que não seja `json-file` nem `local`, abra o link logo depois de gerá-lo e crie
  a senha:** usar o link apaga o hash no banco, e a cópia que ficou no log morre junto;
- **se a pessoa não for usar o link na hora, não guarde o link: rode o comando de novo quando ela
  for usar.** Cada execução invalida o link anterior;
- **se o link vazou**, rode o comando de novo: o vazado deixa de valer. Sem uso, ele vence em 24 h.

> **⚠ No roteiro abaixo, o link é credencial completa por 24 h.** Cada comando, sozinho, não dá
> acesso: o link de senha sem a redefinição das 2 etapas ainda esbarra no código do app, e a
> redefinição das 2 etapas sem a senha não abre sessão. **Depois dos dois, quem abre o link cria a
> senha, cadastra o próprio app autenticador e entra como Admin.** Rode o segundo comando só quando
> a pessoa estiver pronta para abrir o link, e entregue-o só a ela.

**O último Admin que perdeu a senha e as 2 etapas** (o celular e os códigos de recuperação).
**Primeiro as 2 etapas, depois a senha:**

1. `… run --rm --no-deps api two-factor-reset --email maria@empresa.com` → `0`. A verificação dela
   é apagada, o carimbo trocado e as sessões encerradas. Se ela já não tinha a verificação, sai `3`,
   e o roteiro segue igual. Redefinir as 2 etapas não toca o link de senha;
2. `… run --rm --no-deps api password-reset-link --email maria@empresa.com` → `0` e o link;
3. ela abre o link: "Criar nova senha", "Olá, Maria. Crie uma nova senha…". Cria a senha nova em
   "Salvar senha e entrar". Isso troca o
   carimbo, encerra as sessões, apaga o link e **zera o bloqueio** por tentativas erradas;
4. a página entra sozinha com a senha nova e abre o `/entrar` na etapa do cadastro: "Senha salva.
   Falta só cadastrar o app autenticador para entrar.";
5. QR e segredo → o código do app → os **10 códigos de recuperação**, para guardar fora do celular
   → "Continuar" → o painel.

Na ordem inversa também funciona, mas tropeça: depois de criar a senha, a página abre o passo do
código, que ela não tem como passar. Aí entra o `two-factor-reset` e um login a mais.

**Só a senha perdida** (com o celular): só o passo 2. O login seguinte pede o código, como sempre.

**Conferir depois do deploy:** `… run --rm --no-deps api password-reset-link --email <e-mail que não existe>`
sai `1`, sem mudar nada.

**Voltar atrás:** a imagem anterior do `api`. O comando some com ela; links já gerados por ele
continuam valendo até vencer, porque são links de redefinição comuns.

### Minha conta (#246) (histórico)

**O que mudou:** o `frontend` ganhou Minha conta (`/account`), aberta pelo nome no rodapé da
barra lateral: os dados, a troca de senha, a verificação em 2 etapas e as sessões. Sem
migration. O `nginx.conf` não mudou: `/account` já ia ao `apps/api` como fetch da SPA e abre o
painel como navegação (#218).

**Trocar a senha sai do painel:** depois de trocar a senha em Minha conta, a pessoa sai de
todos os navegadores, inclusive o atual, e volta ao `/entrar` com "Senha alterada. Entre com a
nova senha." (#219 D3).

**O cadastro das 2 etapas** ficava atrás de `IDENTITY_TWO_FACTOR_ENROLLMENT_ENABLED`, falsa por
padrão, com o passo de abrir, conferir e voltar atrás. A #220 removeu a opção com a
obrigatoriedade, e o cadastro passou ao login (seção acima).

### Entrada pública do painel (#245)

**Só o `frontend` muda.** Sem migration, sem variável nova, sem mudança no compose nem no
`nginx.conf`: `/convite` e `/redefinir-senha` já caem no `location /` (o `index.html`), e
`/auth/*` já vai ao `apps/api` (#181). O `api`, o `inbox`, o `connectors` e o `workers` não
mudam.

**O que muda para quem usa o painel:**

- o `/entrar` pede o código das 2 etapas depois da senha, para quem tem a verificação
  ligada (o código do app ou um código de recuperação, no mesmo campo). Na época, ninguém
  a tinha em produção: o cadastro ficava fechado, e a #246 trouxe a tela dele. Desde a #220 a
  verificação é obrigatória, e o cadastro é no login
  ([Verificação em 2 etapas obrigatória](#verificação-em-2-etapas-obrigatória-e-o-comando-de-emergência-220));
- os links de convite e de redefinição que o admin gera pela API passam a abrir a tela de
  criar a senha. O token sai do endereço ao abrir, e a página entra em seguida;
- "Esqueceu a senha?" orienta a pedir o link a um administrador, e o rodapé do `/entrar`
  orienta a pedir um convite.

**Conferência depois do deploy**, contra o domínio:

```
D=https://agentes.exemplo.com
# 1. As páginas do link: o HTML do painel, e não o apps/api
curl -s -o /dev/null -w '%{http_code} %{content_type}\n' -H 'Sec-Fetch-Mode: navigate' $D/convite
curl -s -o /dev/null -w '%{http_code} %{content_type}\n' -H 'Sec-Fetch-Mode: navigate' $D/redefinir-senha
```

No navegador: abrir `$D/convite#qualquer-coisa` mostra "Este link não vale mais", e a barra
fica em `$D/convite`, sem o fragmento. O login de sempre continua igual.

**Rollback:** a imagem anterior do `frontend`. Nada no banco. Com a imagem anterior, os
links de convite e de redefinição voltam a abrir a página de erro do roteador, e quem
tiver a verificação ligada volta a não entrar pelo painel (o cadastro fechado em
produção evita os dois casos).

### Emissor OIDC do `apps/api` (#179)

Com `PUBLIC_DOMAIN` definida, o `apps/api` é emissor OIDC, e o issuer é essa URL.

**Gerar a chave antes do deploy em que a #179 entra:**

```
openssl rand -base64 32   # → IDENTITY_SIGNING_KEY_ENCRYPTION_KEY no .env.prod
```

Use um valor novo, nunca o de `MCP_CREDENTIAL_ENCRYPTION_KEY` ou de
`INBOX_CREDENTIAL_ENCRYPTION_KEY`, e guarde-o junto do backup do banco. O
`migrator` cria as tabelas (`identity_keys` e as quatro `openiddict_*`), e o
`apps/api` cria a primeira chave de assinatura sozinho, na subida.

**Perder ou trocar a chave.** As chaves gravadas em `identity_keys` deixam de
abrir. O `apps/api` sobe, mas responde `503` em toda rota, inclusive `/health`,
e o aviso do log cita o `kid` que não decifrou. Recolocar a chave certa no
`.env.prod` e recriar o contêiner do `api` resolve; as linhas gravadas não mudam.
Se a chave estiver perdida de vez, apague as linhas de `identity_keys`: o
`apps/api` cria chaves novas, e os tokens emitidos com as antigas deixam de
validar. Desde a #208, o `apps/inbox` e o `apps/connectors` validam esses
tokens: um token assinado pela chave nova recebe `401` uma vez em cada um, que
busca as chaves de novo e aceita a próxima requisição (no máximo uma busca por
minuto). Desde a #180, apagar as chaves também invalida todo refresh token de
usuário, e cada usuário precisa entrar de novo. Desde a #217 o painel entra por
esse fluxo: apagar as chaves desloga todo mundo do painel.

**`503` até o primeiro acesso ao banco.** O anel de chaves carrega do banco
logo depois do start, em segundo plano, e tenta de novo a cada 5 s. Até
carregar, **toda** rota do `apps/api` responde `503` com `Retry-After: 5`.

**Efeito no healthcheck do compose.** A imagem do `apps/api` sonda com
`curl -f http://localhost:8080/health` (intervalo 10 s, `start-period` 10 s, 3
tentativas), e `curl -f` trata `503` como falha. Com o banco inalcançável, o
`api` fica `unhealthy` em cerca de 40 s, e o `connectors` e o `frontend`, que
declaram `depends_on: api: service_healthy`, não sobem. O `inbox` depende só do
`migrator` e sobe. É o comportamento certo: sem banco o `apps/api` não serve
nada. Um `api` `unhealthy` com o banco fora do ar não é defeito do emissor.

**Conferência depois do deploy**, contra o domínio público:

```
curl -s https://<PUBLIC_DOMAIN>/.well-known/openid-configuration   # JSON; issuer, jwks_uri e token_endpoint em https://<PUBLIC_DOMAIN>/
curl -s https://<PUBLIC_DOMAIN>/.well-known/jwks                   # JSON com ao menos uma chave RSA com kid
curl -s -X POST https://<PUBLIC_DOMAIN>/connect/token \
     -d grant_type=client_credentials -d client_id=x -d client_secret=y   # {"error":"invalid_client",...}
```

Se algum devolver HTML, o `nginx.conf` do `frontend` está sem os blocos do
emissor (imagem antiga do `frontend`).

### Usuários locais e login pelo emissor (#180) (histórico)

**Duas migrations**, aplicadas pelo `migrator`:
- `AddLocalUsers`: as tabelas `users` e `identity_login_throttle`;
- `ExtendIdentityKeyRetention`: só dados. Estende para `signs_until + 15 dias` a
  janela das chaves de assinatura já gravadas em `identity_keys`, sem encurtar
  nenhuma e sem tocar a chave de cifra. A retenção da chave aposentada passou de
  24 h para 15 dias, porque o refresh token de 14 dias é validado pelas mesmas
  chaves. Uma chave aposentada há menos de 15 dias volta ao JWKS.

**Ordem do deploy:** `migrator` → `api` → `frontend`. O `inbox` e o `connectors`
não mudam. O `frontend` muda só no `nginx.conf` (blocos novos de
`/connect/authorize` e `/connect/logout`), e o painel continua entrando pelo
HMAC: a troca do login é da #217.

**`PUBLIC_DOMAIN` é obrigatória** para o `apps/api` (sempre foi no compose, com
`:?`): sem url pública, o boot falha.

#### Primeiro Admin

No primeiro boot sem nenhum usuário, o bootstrap cria um usuário ativo com o
e-mail de `AUTH_BOOTSTRAP_ADMIN_EMAIL` e o hash de `AUTH_OPERATOR_PASSWORD_HASH`,
o mesmo PBKDF2 do operador de hoje, marcado como legado. O primeiro login dele
refaz o hash em Argon2id. Ele roda no aquecimento, logo depois do start, sem
bloquear o boot, e tenta de novo a cada 5 s se falhar.

```
$C logs api | grep -E 'Primeiro Admin criado|Bootstrap do primeiro Admin ignorado|Nenhum usuário; defina'
```

| linha | leitura |
|---|---|
| `Primeiro Admin criado pelo bootstrap, usuário <id>.` | certo: o usuário existe. O log nunca traz o hash |
| `Bootstrap do primeiro Admin ignorado: já existem usuários.` | certo, em todo boot seguinte com a variável ainda definida. Nada é sobrescrito |
| `Nenhum usuário; defina Auth:BootstrapAdminEmail e Auth:OperatorPasswordHash ...` | falta o e-mail, ou ele não é um endereço válido. Defina e recrie o `api` |

**Depois do primeiro login do Admin** (desde a #217, pelo `/entrar` do painel):
- **tire `AUTH_OPERATOR_PASSWORD_HASH` e `AUTH_BOOTSTRAP_ADMIN_EMAIL` do
  `.env.prod` e dos backups dele.** O primeiro login já refez o hash em Argon2id
  na linha do usuário, e a variável não é mais lida. Enquanto ela existir, cada
  cópia do `.env.prod` guarda o PBKDF2 de 100.000 iterações **da senha atual do
  Admin**, material de ataque offline contra ela;
- não é preciso trocar a senha para tirar as variáveis. A #219 entrega as rotas
  de conta, mas não a tela: **troque a senha do Admin quando a tela de Minha conta
  existir (#183)**, porque cópias antigas do `.env.prod` guardam o hash fraco da
  senha de hoje;
- o nome e o formato da variável estão em revisão na #225.

**Bloqueio e limite.** Cada e-mail é bloqueado por 5 min depois de 5 falhas
seguidas. Depois de um bloqueio vencido, uma falha dobra o próximo, até 60 min, e
24 h sem falha voltam ao início. O bloqueio vale também para e-mail sem conta,
para não revelar quem tem conta: um terceiro pode pausar o login de alguém por
até 60 min. O `apps/api` limita as verificações de senha globalmente (`429`), e
**não vê o IP real do cliente** (#221). **O limite por IP é do proxy de fora**
(nginx externo ou Cloudflare), e fica como recomendação de deploy, e não como
garantia do código.

**Logout e desativação: janela de até 15 min, aceita na M1.** O access token já
emitido não é gravado e é validado só pelas chaves, nos três apps, sem consulta
ao emissor. Depois do logout ou da desativação, ele continua valendo até vencer,
em no máximo 15 min, e a renovação seguinte é recusada. A revogação para as
aplicações externas é da #186.

**O cookie de sessão do emissor** (`buteco-sessao`, 5 min) é protegido pelo Data
Protection do ASP.NET Core, com as chaves dele dentro do contêiner do `api`. Com
uma instância, um reinício só invalida um login em andamento, e a pessoa entra de
novo. **Com mais de uma instância do `api` atrás de balanceador**, o cookie
gravado numa não seria lido na outra: as chaves do Data Protection teriam de ser
compartilhadas antes (#222, `aguardando gatilho`, com a segunda instância como gatilho).

**Conferência depois do deploy**, no stack (porta `STACK_HTTP_PORT`):

```
curl -si -H 'Sec-Fetch-Mode: navigate' 'http://localhost:<porta>/connect/authorize?client_id=buteco-painel&response_type=code&scope=openid&redirect_uri=https://<PUBLIC_DOMAIN>/entrar/retorno&code_challenge=<s256>&code_challenge_method=S256'
  → 302 com Location: /entrar?retorno=...   (do apps/api, e não 200 com o HTML do SPA)
curl -si -H 'Sec-Fetch-Mode: navigate' 'http://localhost:<porta>/connect/logout'
  → 302 com Location: /entrar   (do apps/api, e não o shell do SPA)
curl -si -H 'Sec-Fetch-Mode: navigate' 'http://localhost:<porta>/connect/token'
  → o shell do SPA (o desvio de navegação continua para o token)
curl -si -X POST -H 'Content-Type: application/json' -H 'Origin: https://<PUBLIC_DOMAIN>' \
     --data '{"email":"<admin>","senha":"<senha>"}' 'http://localhost:<porta>/auth/session'
  → 204 com Set-Cookie: buteco-sessao (HttpOnly, SameSite=Strict, Secure)
```

Se o authorize ou o logout devolverem HTML, o `frontend` está com o `nginx.conf`
antigo.

**Rollback:** a imagem anterior do `api` e do `frontend`.
- As tabelas novas ficam sem uso, e o cliente `buteco-painel` sincronizado também,
  porque a versão anterior não o lê.
- A janela de 15 dias gravada nas chaves continua, porque a versão anterior lê
  `validates_until` da linha; as chaves que ela criar voltam a 24 h.
- A versão anterior volta a gravar a linha do access token de usuário, que nenhum
  usuário emite com ela.
- A url pública continua definida, então a versão anterior sobe com o emissor
  ligado, como antes.
- O painel nunca deixou de usar o HMAC.

### O painel entra pelo emissor (#217) (histórico)

**Ordem do deploy:** `migrator` (sem migration nesta mudança) → `api` e
`frontend` juntos. O `inbox` e o `connectors` não mudam. Um `frontend` novo com o
`api` antigo, ou o contrário, não tem login que funcione: o painel novo não usa
mais o `POST /auth/login`, e o `api` novo não o tem.

**`.env.prod`:**
- `AUTH_OPERATOR_USERNAME` não é mais lida, e pode sair. **Guarde o valor até
  descartar o rollback** (abaixo);
- `AUTH_OPERATOR_PASSWORD_HASH` deixa de ser obrigatória no compose. Numa
  instalação que já tem o primeiro Admin, ela pode sair junto com o
  `AUTH_BOOTSTRAP_ADMIN_EMAIL` ("Primeiro Admin", acima).

**Quem estava com o painel aberto** continua com o token HMAC antigo até ele
vencer (no máximo 30 min depois do último login antes do deploy), e no primeiro
`401` vai ao `/entrar`. Cada pessoa entra uma vez com e-mail e senha.

**Janela do `400` logo depois do deploy.** Nos primeiros segundos depois do start
do `api`, antes de o catálogo de clientes sincronizar o `buteco-painel`, quem
entrar pode ver a página de texto `error:invalid_request` do `apps/api` em vez do
painel. É o primeiro ciclo do aquecimento, de segundos. Recarregar resolve. O
`503` do anel de chaves, que vem antes, o `/entrar` mostra como "O painel está
iniciando".

**"Sair" com o `apps/api` no `503`.** O "Sair" apaga a sessão no navegador e
navega ao `/connect/logout`. Se o `apps/api` estiver no `503` do anel de chaves
nesse instante, a navegação mostra a página de texto do `apps/api`, e não o
`/entrar`. A sessão local já foi apagada, mas a autorização no emissor continua
viva até a pessoa entrar e sair de novo, ou até vencer (14 dias de refresh).

**Conferência depois do deploy**, no stack (porta `STACK_HTTP_PORT`):

```
curl -si -H 'Sec-Fetch-Mode: navigate' http://localhost:<porta>/entrar
  → 200, shell do SPA, Cache-Control: no-store, Content-Security-Policy, Referrer-Policy: no-referrer
curl -si -H 'Sec-Fetch-Mode: navigate' 'http://localhost:<porta>/entrar/retorno?code=x&state=y'
  → 200, shell do SPA, os mesmos três cabeçalhos
curl -si -H 'Sec-Fetch-Mode: navigate' http://localhost:<porta>/agents
  → 200, shell do SPA, os mesmos três cabeçalhos
curl -si -X POST http://localhost:<porta>/auth/login -H 'Content-Type: application/json' -d '{}'
  → 401 do apps/api, com o corpo vazio e sem Content-Type: text/html
curl -si -H 'Sec-Fetch-Mode: navigate' 'http://localhost:<porta>/connect/authorize?client_id=buteco-painel&…'
  → 302 com Location: /entrar?retorno=…
```

- **`/auth/login` com `401`, e não `404`, é o certo:** sem token, o `apps/api`
  responde `401` a toda rota que não existe, para não revelar quais existem. O que
  reprova é `200` com HTML (o shell do SPA no lugar do `apps/api`) ou `200` com
  JSON (o `api` antigo, com a rota).
- **Sem a CSP ou o `Referrer-Policy` no shell**, o `frontend` está com o
  `nginx.conf` antigo. Os dois cabeçalhos aparecem também em `/index.html`.

**Web Analytics do Cloudflare: desligue a injeção automática na zona** para o
domínio do painel. O painel não o usa, e a CSP não libera nenhum host do
Cloudflare: o beacon (`https://static.cloudflareinsights.com/beacon.min.js`, que a
borda põe antes do `</body>` dos pedidos de navegador) é bloqueado. Enquanto a
injeção não for desligada, o console do navegador mostra a violação de CSP do
beacon em toda página, sem efeito no painel. Para conferir, o HTML com cabeçalhos
de navegador não deve trazer `cloudflareinsights`:

```
curl -s https://<PUBLIC_DOMAIN>/entrar -H 'Sec-Fetch-Mode: navigate' -H 'Accept: text/html' \
     -H 'User-Agent: Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36' \
  | grep -c cloudflareinsights      # 0 com a injeção desligada
```

Se um dia o Web Analytics for desejado no painel, libere na CSP do `nginx.conf`
(nos dois níveis, `server` e `location = /index.html`) **só o
`script-src https://static.cloudflareinsights.com`**. O envio do beacon vai a
`/cdn-cgi/rum` na própria origem, que o `connect-src 'self'` já cobre, enquanto o
`data-cf-beacon` do HTML trouxer `version` (medido em 01/11/2026). Só se ele vier
sem `version` o beacon envia a `https://cloudflareinsights.com/cdn-cgi/rum`, e aí
o `connect-src` precisa desse host.

Depois dos `curl`, o critério de aceite: entrar no painel e abrir a lista de
agentes (`apps/api`), a de canais (`apps/inbox`) e uma sincronização de base
(`apps/connectors`).

**Rollback:** a versão anterior do `api` e do `frontend` (o código, e com ele o
`docker-compose.prod.yml`), **com `AUTH_OPERATOR_USERNAME` e
`AUTH_OPERATOR_PASSWORD_HASH` de volta no `.env.prod`**: o compose anterior exige
as duas com `:?`, e o `POST /auth/login` confere o usuário e o hash.
- Quem entrou pelo emissor fica com a sessão no `localStorage`, que o painel
  anterior não lê, e entra de novo pelo `/login`.
- As autorizações do emissor ficam no banco sem uso, e a limpeza da #209 as
  remove quando vencerem.

### Verificação em 2 etapas (#218)

**Antes do deploy, gerar a chave nova:**

```
openssl rand -base64 32   # → IDENTITY_TWO_FACTOR_ENCRYPTION_KEY no .env.prod
```

- Um valor novo, nunca o de `IDENTITY_SIGNING_KEY_ENCRYPTION_KEY` nem o de
  `MCP_CREDENTIAL_ENCRYPTION_KEY`: com uma igual, o `api` não sobe, e a mensagem diz
  qual. Sem ela, o Compose nem processa o arquivo.
- **Guarde-a junto do backup do banco, e não a troque** sem antes redefinir a
  verificação de todo usuário que a tem ligada. Com outra chave, o segredo gravado não
  decifra: o código do app autenticador responde `500` ("Não foi possível conferir o
  código do app autenticador…"), com o log `TwoFactorSecretUnreadable` (evento 1194), e
  só os códigos de recuperação continuam valendo, porque são hash e não cifra. A saída é
  a redefinição pelo admin (`POST /users/{id}/two-factor/reset`), e a pessoa cadastra de
  novo. Não há recifragem dos segredos.

**O relógio do host precisa de NTP ativo.** O código vale 30 s, com um passo de
tolerância para cada lado. Um desvio maior que 30 s entre o relógio do host do stack (os
contêineres usam o dele) e o dos celulares faz os códigos não baterem **para todos os
usuários ao mesmo tempo**, e o sintoma parece "código errado". Conferir antes do deploy e
depois de qualquer manutenção da VM:

```
timedatectl   # "System clock synchronized: yes" e "NTP service: active"
```

**Migration** `AddTwoFactor`, aplicada pelo `migrator`, só aditiva: cinco colunas nulas
em `users` e a tabela `user_recovery_codes`.

**Ordem do deploy:** `migrator` → `api` → `frontend`. O `frontend` muda só no
`nginx.conf`: os prefixos `/account` e `/users` passam a ir ao `apps/api`. O `inbox`, o
`connectors` e o `workers` não mudam.

**O cadastro da verificação ficava fechado** (`Identity__TwoFactor__EnrollmentEnabled`,
falso por padrão) até a tela existir: a #245 trouxe o passo do código no `/entrar`, a #246 a
tela do cadastro em Minha conta e a variável no compose. **A #220 removeu a opção**: a
verificação passou a ser obrigatória, com o cadastro no login
([Verificação em 2 etapas obrigatória](#verificação-em-2-etapas-obrigatória-e-o-comando-de-emergência-220)).

**Conferência depois do deploy**, contra o domínio, como fetch da SPA:

```
D=https://agentes.exemplo.com
# 1. Os prefixos novos chegam ao apps/api: 401 sem corpo, e não 200 com o HTML do painel
curl -s -o /dev/null -w '%{http_code} %{content_type}\n' $D/account/two-factor
curl -s -o /dev/null -w '%{http_code} %{content_type}\n' -X POST $D/users/00000000-0000-0000-0000-000000000000/two-factor/reset
# 2. (Até a #220) Com o token de um usuário, o cadastro estava fechado: "cadastroDisponivel":false.
#    O campo saiu do contrato na #220.
```

**Rollback (da #218):** a imagem anterior do `api` e do `frontend`. As colunas e a tabela ficam sem
efeito, e a variável sobra. Como o cadastro estava fechado, ninguém ficava preso à
verificação. Depois da #220, ver o "Voltar atrás" da seção dela.

### Convite e redefinição de senha por link (#181)

**Só o `api` muda**, com a migration `AddUserAccessLinks`, aplicada pelo `migrator`, só
aditiva: quatro colunas nulas em `users` (`invited_by`, `access_link_hash`,
`access_link_purpose`, `access_link_expires_at`), o índice único do hash e a FK de
`invited_by`. Sem variável nova, sem mudança no compose nem no `nginx.conf`: `/auth` e
`/users` já vão ao `apps/api`, e `/convite` e `/redefinir-senha` são páginas do painel,
que caem no fallback do SPA. O `frontend`, o `inbox`, o `connectors` e o `workers` não
mudam.

**O link usa a url pública.** A base é `Identity__PanelBaseUrl` quando configurada (só em
desenvolvimento) e, sem ela, `PUBLIC_DOMAIN` (`PublicUrl__BaseUrl`): nunca o `Host` da
requisição. Se `PUBLIC_DOMAIN` estiver errado, os links saem com o domínio errado.

**O token vai no fragmento** (`https://agentes.exemplo.com/convite#<token>`), que o
navegador não envia ao servidor: os logs de acesso do nginx do stack e do proxy de fora
registram só `GET /convite`, e o painel manda o token no corpo de um `POST`. **Não troque o
`#` por `/`** ao colar o link em algum lugar que o reescreva.

**Orientação ao admin, a passar a quem gere usuários:**

- o Buteco **não envia e-mail**. O admin copia o link (mostrado uma vez) e o entrega pelo
  canal que quiser. Quem tem o link cria a senha da conta: **confira o destinatário por
  outro canal** antes de mandar, principalmente se mandar para o e-mail cadastrado, que
  qualquer usuário da M1 pode trocar;
- convidar, gerar novo link e gerar o link de redefinição **pedem a senha de quem faz**.
  Errar conta no bloqueio do e-mail de quem faz, como no login;
- o convite vale **72 h**, e a redefinição **24 h**; os dois, uma vez. Gerar outro invalida
  o anterior. Concluir a redefinição **encerra todas as sessões** da pessoa e **mantém as 2
  etapas**: quem perdeu a senha e o celular precisa das duas redefinições.

**Telas.** As páginas públicas do link (`/convite` e `/redefinir-senha`) existem desde a
#245. Convidar, gerar novo link, cancelar e gerar a redefinição ainda não têm tela: são da
#183, e até ela só pela API.

**Conferência depois do deploy**, contra o domínio:

```
D=https://agentes.exemplo.com
# 1. Sem token: 401 do apps/api, sem corpo
curl -s -o /dev/null -w '%{http_code} %{content_type}\n' -X POST $D/users/invitations
# 2. A consulta pública com um token qualquer: o 404 do link inválido, e não 200 com HTML
curl -s -X POST $D/auth/invitation -H 'Content-Type: application/json' -H "Origin: $D" -d '{"token":"x"}'
# 3. A página do link: o HTML do painel
curl -s -o /dev/null -w '%{http_code} %{content_type}\n' -H 'Sec-Fetch-Mode: navigate' $D/convite
```

**Rollback:** antes, **cancelar os convites pendentes** (`POST /users/{id}/invitation/cancel`)
e esperar os links de redefinição vencerem ou serem usados. Depois, a imagem anterior do
`api`; as colunas ficam e são ignoradas, sem migration a desfazer. Com um convite pendente
no banco, a versão anterior o leria como **desativado** (a leitura antiga trata todo estado
desconhecido assim), e reativá-lo o poria ativo sem senha: sem entrada possível, mas no
lugar errado da lista.

### Minha conta e Usuários (#219) (histórico)

**Só o `api` muda.** Sem migration, sem variável nova, sem mudança no compose nem no
`nginx.conf`: os prefixos `/account` e `/users` já vão ao `apps/api` desde a #218. O
`frontend`, o `inbox`, o `connectors` e o `workers` não mudam.

**O que muda para quem já está logado:**

- a sessão aberta antes do deploy continua; nada é revogado no deploy;
- a partir dele, trocar a senha, desativar alguém ou redefinir as 2 etapas de alguém
  **encerra as sessões da pessoa na hora** (as autorizações saem revogadas), e não só na
  próxima renovação;
- na renovação seguinte ao deploy, o id token passa a trazer o nome e o e-mail gravados
  (até aqui, os do login).

**Sem tela ainda.** As rotas existem para a #183; até ela, só pela API. Na M1 todo usuário
real chama todas, inclusive desativar e editar outro (a #184 restringe ao Admin). O HMAC
de operador recebe `403`.

**Conferência depois do deploy**, contra o domínio, como fetch da SPA:

```
D=https://agentes.exemplo.com
# 1. Sem token: 401 do apps/api, sem corpo, e não 200 com o HTML do painel
curl -s -o /dev/null -w '%{http_code} %{content_type}\n' $D/users
curl -s -o /dev/null -w '%{http_code} %{content_type}\n' $D/account/sessions
# 2. Com o token de um usuário (ver docs/development.md): a lista e a sessão atual marcada
curl -s -H "Authorization: Bearer $TOKEN" $D/users
curl -s -H "Authorization: Bearer $TOKEN" $D/account/sessions   # um item com "atual":true
```

**Rollback:** a imagem anterior do `api`. Não há schema a desfazer. As autorizações que
esta versão revogou continuam revogadas, que é o efeito pedido; a versão anterior volta a
trocar o stamp sem revogar, e a lista de sessões não existe nela.

### Serviços obtêm o token no emissor (#235) (histórico)

Segundo passo da troca do token de serviço (#182). Os três serviços deixam de assinar o token HMAC de serviço:

- o `inbox` e o `connectors` pedem o token ao emissor do `api`, pelo endereço interno (`http://api:8080/connect/token`), com o segredo de cada um;
- o `api` gera o seu no próprio processo, para o `connectors`.

O token vale 15 min e fica em cache até faltar 1 min. **O HMAC continua aceito** nos três apps, e a `AUTH_TOKEN_SIGNING_KEY` continua no `.env.prod` (sai na #182).

**Nada a gerar:** os dois segredos estão no `.env.prod` desde a #234. O compose passa a entregar `SERVICE_CLIENT_SECRET_INBOX` também ao `inbox` e `SERVICE_CLIENT_SECRET_CONNECTORS` também ao `connectors`, os dois como `ServiceClient__Secret` e com `:?`. Sem migration.

**Ordem do deploy:** qualquer uma entre `api`, `inbox` e `connectors`. Os receptores aceitam os dois formatos desde a #234. O `inbox` não depende do `api` para subir: com o `api` fora, as chamadas falham no pedido de token como falhavam por conexão recusada, e durante 10 s depois de cada falha falham na hora.

**Conferência depois do deploy:**

```
# 1. A linha do primeiro token, no inbox e no connectors (o inbox pede no primeiro
#    despacho ou cadastro de canal; o connectors, na primeira rodada do ciclo)
docker compose logs inbox | grep 'Token de serviço buteco-servico-inbox obtido'
docker compose logs connectors | grep 'Token de serviço buteco-servico-connectors obtido'
#    → "… obtido no emissor pelo Api:BaseUrl (/connect/token), válido até …"
# 2. Nenhum segredo divergente
docker compose logs inbox connectors | grep 'invalid_client'   # → nada
# 3. O token que o api gera para o connectors (no primeiro cadastro de base sincronizada)
docker compose logs api | grep 'Token de serviço buteco-servico-api gerado no processo'
```

- Uma mensagem de canal chega ao agente (o cadastro do canal valida o agente; o despacho chama o A2A).
- "Sincronizar agora" numa base sincronizada.
- Cadastrar uma base sincronizada.

**Sintomas:**

| no log | leitura |
|---|---|
| `Error` "recusado pelo emissor (/connect/token): invalid_client" | o `SERVICE_CLIENT_SECRET_*` do serviço difere do que o `api` carregou. Conferir o `.env.prod` e subir os dois. O cadastro de canal responde `502`, o despacho tenta de novo até o limite, e o "Sincronizar agora" responde `503` |
| `Warning` "não obtido no emissor (/connect/token): 503" ou "timeout" | o `api` está subindo ou fora do ar; passa sozinho |

**Rollback:** a imagem anterior de qualquer um dos três. A versão anterior volta a assinar HMAC, que continua aceito, e ignora `ServiceClient__Secret`. Os segredos ficam no `.env.prod`.

### Clientes de serviço do emissor (#234) (histórico)

Primeiro passo da troca do token de serviço (#182, dividida em #234, #235, #236 e #182). O emissor do `apps/api` passa a manter dois clientes confidenciais, `buteco-servico-inbox` e `buteco-servico-connectors`, só com `client_credentials` e o escopo `buteco-api`. O `apps/api` aceita o token deles nas rotas de `service:inbox` e `service:connectors`, e o `apps/connectors` aceita o do `buteco-servico-api` na rota de descrever pasta. **Ninguém manda o token novo ainda**: os três serviços continuam com o HMAC, que continua aceito.

**Antes do deploy, gerar os dois segredos**, um por serviço:

```
openssl rand -base64 32   # → SERVICE_CLIENT_SECRET_INBOX no .env.prod
openssl rand -base64 32   # → SERVICE_CLIENT_SECRET_CONNECTORS no .env.prod
```

- Dois valores novos, diferentes entre si e de `IDENTITY_SIGNING_KEY_ENCRYPTION_KEY`, de `IDENTITY_TWO_FACTOR_ENCRYPTION_KEY` e de `MCP_CREDENTIAL_ENCRYPTION_KEY`. Com um igual, o `api` não sobe, e a mensagem nomeia as duas chaves iguais, sem o valor. Sem eles, o Compose nem processa o arquivo.
- O banco guarda só o hash (PBKDF2 do OpenIddict). O valor fica no `.env.prod`, e a #235 o entrega também ao `inbox` e ao `connectors`.
- **Trocar um segredo:** mudar o valor no `.env.prod` e subir o `api`. O catálogo confere o segredo a cada sincronização (no aquecimento e a cada hora) e regrava o hash quando o configurado não valida, com o log "segredo do cliente … trocado". Nesta fase ninguém usa o cliente, então a troca não alcança nada.

**Ordem do deploy:** qualquer uma entre `api` e `connectors`. Não há migration: os clientes são dados que o aquecimento sincroniza. O `inbox`, o `workers` e o `frontend` não mudam.

**Conferência depois do deploy:**

```
# 1. O catálogo criou os dois clientes (uma vez, no primeiro aquecimento)
docker compose logs api | grep 'Catálogo de clientes do emissor sincronizado'
#    → "… cliente buteco-servico-inbox criado, cliente buteco-servico-connectors criado"
# 2. De dentro da rede do compose, cada segredo emite token. --data-urlencode, e não -d:
#    o base64 tem + e /, e o + sem codificar chega como espaço (invalid_client)
docker compose exec api curl -s -X POST http://localhost:8080/connect/token \
  --data-urlencode grant_type=client_credentials --data-urlencode client_id=buteco-servico-inbox \
  --data-urlencode client_secret="$SERVICE_CLIENT_SECRET_INBOX" --data-urlencode scope=buteco-api
#    → access_token; o payload tem client_id buteco-servico-inbox e aud buteco-api
# 3. O token de cada cliente só passa nas rotas do subject dele
#    inbox: GET /agents → 403; connectors: GET /agents → 403, GET /sync/knowledge-bases/ → 200
```

- O painel e os três fluxos de serviço funcionam como antes: uma mensagem de canal chega ao agente, o "Sincronizar agora" funciona, e uma base sincronizada se cadastra.
- Um item do catálogo que falhe gera `IdentityClientCatalogItemFailed` (evento 1195) com o cliente ou o escopo, sem o segredo. Os outros itens seguem, e o que falhou tenta de novo no próximo ciclo.

**Rollback:** a imagem anterior do `api` e do `connectors`. Os dois clientes ficam no banco sem efeito, e a versão anterior os loga como `buteco-*` não declarado (`IdentityClientUndeclared`, `Error`) a cada sincronização, **o que é esperado**, sem mexer neles. Os segredos podem ficar no `.env.prod`, porque a #235 os usa.

### Limpeza das entradas de token do emissor (#209)

**Nada a gerar:** sem variável nova, sem mudança no compose, sem migration. O
`apps/api` passa a não gravar o access token de `client_credentials` e a limpar
`openiddict_tokens` e `openiddict_authorizations` de hora em hora. O primeiro
ciclo é 1 h depois do start.

**Conferência depois do deploy**, passada 1 h da subida:

```
$C logs api | grep -E 'Limpeza das entradas de token (falhou|removeu)'
```

| linha | leitura |
|---|---|
| nenhuma | certo: o ciclo sem nada a remover, ou pulado por outra instância, loga em `Debug`, que o nível padrão não mostra. Hoje não há token emitido em produção |
| `Limpeza das entradas de token removeu N tokens e M autorizações.` | certo: removeu linhas mortas com mais de 14 dias |
| `Limpeza das entradas de token falhou neste ciclo; o próximo tenta de novo.` | o ciclo falhou, em geral por banco indisponível. O próximo tenta de novo em 1 h. Se repetir com o banco no ar, abra issue com a exceção do log |

**Rollback:** volte para a imagem anterior do `apps/api`. Não há schema nem
configuração a desfazer. A versão anterior volta a gravar o access token de
`client_credentials` e para de limpar as tabelas. Quando esta versão voltar,
ela remove as linhas que se acumularem.

### Validação do token do emissor no `apps/inbox` e no `apps/connectors` (#208)

Os dois recebem `Identity__Issuer: ${PUBLIC_DOMAIN}` do compose, a mesma
variável do `PublicUrl__BaseUrl` do `apps/api`, e obtêm as chaves pelo
`Api__BaseUrl` (`http://api:8080`). Nenhuma variável nova no `.env.prod`, e
nenhuma migration.

**Ordem de deploy:** qualquer uma. O `apps/api` não mudou naquela entrega, e
nenhum token do emissor circulava antes da #180. Os dois sobem com o `apps/api`
fora do ar, e atendem com as chaves que obtiverem depois.

**Conferência depois do deploy:**

```
$C logs inbox      | grep -E 'token do emissor (ligada|desligada)|chaves do emissor'
$C logs connectors | grep -E 'token do emissor (ligada|desligada)|chaves do emissor'
```

| O que aparece | O que significa |
|---|---|
| `Validação do token do emissor ligada: issuer https://<PUBLIC_DOMAIN>/, ...` seguido de `Chaves do emissor obtidas pelo Api:BaseUrl (/.well-known/jwks): kids ...` | certo: as chaves foram obtidas pelo endereço interno |
| o app não sobe, com `Identity:Issuer não configurado` | o `Identity__Issuer` não chegou ao contêiner (compose antigo). Desde a #182 o app não sobe sem ele |
| erro `O discovery do emissor lido pelo Api:BaseUrl declara o issuer '...', diferente de Identity:Issuer '...'` | o `PublicUrl__BaseUrl` do `api` e o `Identity__Issuer` do app divergem (as duas saem de `PUBLIC_DOMAIN`; ver se alguém as editou à mão no compose). Nenhuma chave é obtida |
| aviso `Busca das chaves do emissor pelo Api:BaseUrl (...) falhou` repetido a cada 10 s | o `apps/api` não responde no endereço interno. Normal enquanto ele sobe |

**`503` para o token do emissor antes da primeira obtenção.** Entre o start e a
primeira busca bem-sucedida (até 10 s depois de o `apps/api` responder), um
token do emissor recebe `503` com `Retry-After: 10`; com o issuer divergente, o
`503` continua até a configuração ser corrigida. A requisição sem token recebe
`401`, e não esse `503` (#182). Depois de obtidas, as chaves continuam valendo com o `apps/api` fora do
ar.

### Risco aceito: placeholder `changeme` não é rejeitado no boot

*(Premissa alterada em 19/09/2026: este aceite foi feito antes de haver
deploy atendendo tráfego real, e hoje vale contra um piloto em produção. A
decisão não foi revista aqui; está registrada como candidata em
[`02-HISTORICO_E_STATUS.md`](../02-HISTORICO_E_STATUS.md).)*

Nenhuma das chaves acima rejeita especificamente o valor `changeme*` do
`.env.prod.example` — a checagem de startup existente só cobre
ausência/vazio, não o conteúdo. Se `.env.prod` for copiado sem editar,
o stack sobe normalmente com segredos públicos e previsíveis (estão no
repo). **Confira manualmente, antes de cada deploy, que nenhum valor em
`.env.prod` começa com `changeme`** — decisão consciente de escopo desta
change, não mitigada em código.

### Risco encerrado: chave de assinatura compartilhada por três processos (#117)

Encerrado pela #182: só o `apps/api` assina token, cada serviço tem a sua credencial
no emissor, e nenhum processo guarda chave capaz de assinar um token de operador. O
aceite original foi da #119 (05/10/2026), e o gatilho dele, a entrada do segundo
cliente ou de dados mais sensíveis, era a M1.

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
