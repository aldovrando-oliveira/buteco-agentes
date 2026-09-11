# Configuração

Inventário das variáveis de ambiente do sistema, por processo.

Os arquivos de referência são [`.env.example`](../.env.example) (dependências
locais e os quatro apps em desenvolvimento) e
[`.env.prod.example`](../.env.prod.example) (stack de servidor, consumido por
`docker-compose.prod.yml`). Este documento explica o que cada variável faz e
quais restrições existem entre processos; os arquivos de exemplo continuam
sendo a fonte dos valores.

---

## Como a configuração é lida

Os apps .NET leem configuração via `IConfiguration`, combinando
`appsettings.json`, `appsettings.Development.json` e variáveis de ambiente.
A convenção `Chave__SubChave` (dois underscores) mapeia para `Chave:SubChave`
na configuração — `ConnectionStrings__Postgres` equivale a
`ConnectionStrings:Postgres`.

Há **duas exceções** a esse padrão:

- **`TZ`** (`apps/workers`) não é lida via `IConfiguration`. É uma variável do
  sistema operacional, lida diretamente pelo runtime do .NET
  (`TimeZoneInfo.Local` / `TimeProvider.System`).
- **`VITE_*`** (`apps/frontend`) são resolvidas em **tempo de build** pelo
  Vite e embutidas no bundle. Não existem em runtime.

Nenhum valor é fixado em código.

---

## Restrições entre processos

Três segredos precisam ter o **mesmo valor** em mais de um processo. Esta é a
fonte mais comum de falha de configuração no sistema, porque o sintoma
aparece longe da causa.

| Segredo | Processos | O que quebra se divergir |
|---|---|---|
| `Auth:TokenSigningKey` | `apps/api` + `apps/inbox` | Um token emitido no login por `apps/api` é rejeitado por `apps/inbox` e vice-versa. Também quebra o token de serviço que `apps/inbox` assina para chamar `apps/api` |
| `Mcp:CredentialEncryptionKey` | `apps/api` + `apps/workers` | `apps/api` cifra a credencial do servidor MCP ao salvar; `apps/workers` não consegue decifrá-la para conectar durante uma execução |
| Chaves de provedor de LLM | `apps/api` + `apps/workers` | `apps/api` anuncia o provedor como disponível em `GET /providers`, mas a task termina `failed` no worker |

`Inbox:CredentialEncryptionKey` é a exceção deliberada: é **própria** de
`apps/inbox` e nunca deve ter o mesmo valor de `Mcp:CredentialEncryptionKey`
— são segredos de domínios diferentes, cada um decifrável apenas pelo
processo que o gerou.

No stack de servidor, `docker-compose.prod.yml` garante essa igualdade por
construção: uma única variável do `.env.prod` é interpolada nos blocos
`environment:` dos dois serviços. Nunca copie o valor manualmente para
chaves separadas.

> **Perder uma chave de criptografia torna irrecuperável toda credencial já
> salva com ela.** A resposta operacional é reconfigurar as credenciais
> afetadas, não tentar recuperar as antigas.

---

## Variáveis obrigatórias

Ausentes, estes valores **derrubam o processo no boot** (fail-fast):

| Variável | Processo |
|---|---|
| `Auth__TokenSigningKey` | `apps/api`, `apps/inbox` |
| `Auth__OperatorUsername` | `apps/api` |
| `Auth__OperatorPasswordHash` | `apps/api` |
| `Api__BaseUrl` | `apps/inbox` |
| `TZ` | `apps/workers` |
| `VITE_API_BASE_URL` | `apps/frontend` (build) |
| `VITE_INBOX_BASE_URL` | `apps/frontend` (build) |

`TZ` merece nota: o processo falha se a variável estiver ausente, vazia, ou
se o fuso resolvido pelo sistema operacional não corresponder exatamente ao
valor declarado. Não é só uma checagem de presença — é uma checagem de
integridade no startup, o padrão descrito em [conventions.md](conventions.md).

---

## Dependências locais (`docker-compose.yml`)

Usadas apenas pelo compose de desenvolvimento, não pelos apps.

**A imagem do Postgres é `pgvector/pgvector:pg18`, não `postgres:18`** — a
indexação de bases de conhecimento usa a extensão `vector`. Atenção à leitura:
há **um servidor para dois bancos** (`buteco_agents`, de `apps/api` e
`apps/workers`, e `buteco_inbox`), então a imagem alcança também o banco de
`apps/inbox`. É inócuo, porque a extensão é criada por banco e só
`buteco_agents` a tem — mas o compose **não isola** os apps.

`CREATE EXTENSION vector` **exige superusuário**: a extensão não é `trusted`.
Funciona no compose porque o `migrator` conecta como `POSTGRES_USER`, que é o
superusuário de bootstrap. Num ambiente em que o app conecte com papel
restrito, a extensão precisa ser criada antes, por fora da migração.

| Variável | Default | Descrição |
|---|---|---|
| `POSTGRES_USER` | `buteco` | Usuário do Postgres local |
| `POSTGRES_PASSWORD` | `buteco_dev_password` | Senha do Postgres local |
| `POSTGRES_DB` | `buteco_agents` | Banco criado na subida |
| `POSTGRES_PORT` | `5432` | Porta publicada no host |
| `RABBITMQ_USER` | `buteco` | Usuário do RabbitMQ local |
| `RABBITMQ_PASSWORD` | `buteco_dev_password` | Senha do RabbitMQ local |
| `RABBITMQ_AMQP_PORT` | `5672` | Porta AMQP publicada no host |
| `RABBITMQ_MANAGEMENT_PORT` | `15672` | Porta da UI de management |
| `WAHA_PORT` | `3000` | Porta do WAHA, usado pelo adapter `waha` |

---

## `apps/api`

| Variável | Obrigatória | Descrição |
|---|---|---|
| `ConnectionStrings__Postgres` | sim | Banco compartilhado com `apps/workers` (`buteco_agents`) |
| `RabbitMq__Host` / `__Port` / `__Username` / `__Password` | sim | Broker para publicar jobs de task |
| `Auth__TokenSigningKey` | **fail-fast** | Chave HMAC de assinatura de token. Mesmo valor de `apps/inbox` |
| `Auth__OperatorUsername` | **fail-fast** | Operador único; não há tabela de usuários |
| `Auth__OperatorPasswordHash` | **fail-fast** | `{iterations}.{saltBase64}.{hashBase64}`, PBKDF2-HMAC-SHA256, gerado offline — ver [development.md](development.md#autenticação-em-desenvolvimento) |
| `Auth__OperatorTokenLifetime` | não | TTL do token de operador. Default de 30 min fixado no próprio tipo, não em `appsettings.json` |
| `Mcp__CredentialEncryptionKey` | sim | AES-256 (32 bytes) em base64. Mesmo valor de `apps/workers`. Gerar com `openssl rand -base64 32` |
| `PublicUrl__BaseUrl` | sim | URL pública da própria API, usada em `AgentCard.SupportedInterfaces` para descoberta A2A externa. Não afeta `SendMessage`/`GetTask` |
| `OpenAI__BaseUrl` / `OpenAI__ApiKey` / `OpenAI__Model` | não | Presença habilita o provedor em `GET /providers` |
| `Anthropic__ApiKey` | não | Idem, para Claude |
| `Gemini__ApiKey` | não | Idem, para Gemini |

`apps/api` **nunca chama o LLM** — apenas verifica a presença das chaves para
montar a lista de provedores disponíveis.

`PublicUrl__BaseUrl` controla a direção oposta de `Cors__AllowedOrigins`:
`Cors` define quem pode **chamar** a API (entrada); `PublicUrl` é a URL pela
qual a própria API é **alcançada** de fora, do ponto de vista do AgentCard.

---

## `apps/workers`

| Variável | Obrigatória | Descrição |
|---|---|---|
| `ConnectionStrings__Postgres` | sim | Mesmo banco de `apps/api`; o worker usa o schema, nunca o cria |
| `RabbitMq__Host` / `__Port` / `__Username` / `__Password` | sim | Broker de consumo dos jobs |
| `TZ` | **fail-fast** | Nome IANA canônico da tz database (ex. `America/Sao_Paulo`), sem o prefixo POSIX `:`. Sem default seguro possível — cada ambiente tem seu fuso real |
| `Mcp__CredentialEncryptionKey` | sim | Decifra a credencial do servidor MCP. Mesmo valor de `apps/api` |
| `OpenAI__BaseUrl` / `OpenAI__ApiKey` / `OpenAI__Model` | condicional | Necessária se algum agente usar o provedor |
| `Anthropic__ApiKey` | condicional | Idem |
| `Gemini__ApiKey` | condicional | Idem |
| `Embedding__Provider` | condicional | Provedor do modelo de embedding da indexação de bases de conhecimento. Hoje só `openai` é suportado — `anthropic` e `gemini` não expõem tipo de embedding no SDK referenciado |
| `Embedding__Model` | condicional | Modelo de embedding. **Não tem default**: foi escolhido por medição, e o modelo que um ambiente serve por padrão pode empatar com busca lexical |
| `Embedding__Dimensions` | condicional | Dimensão declarada, **conferida contra a que o provedor devolve no momento da gravação**. O gateway pode aceitar o parâmetro `dimensions` e ignorá-lo, e sem a conferência o índice seria gravado com vetores incompatíveis sem erro nenhum |

A seção `Embedding` **não tem credencial própria**: a chave e o endpoint vêm da
seção `OpenAI` acima, reusados de propósito — aquele endpoint já é no formato
OpenAI, e duplicar o segredo criaria duas fontes que divergem no primeiro
rodízio de chave.

`apps/workers` **falha o boot** se o provedor, o modelo ou a dimensão declarados
divergirem do que está gravado nos fragmentos já indexados. Vetores de modelos
diferentes são incomparáveis, e a busca continuaria devolvendo resultados
errados sem erro nenhum. Índice vazio sobe normalmente.

As chaves de provedor são "condicionais" no sentido de que só a do provedor
efetivamente referenciado pelo agente em execução é usada — mas precisam
existir aqui sempre que existirem em `apps/api`, senão a task falha em
runtime.

---

## `apps/inbox`

| Variável | Obrigatória | Descrição |
|---|---|---|
| `ConnectionStrings__Postgres` | sim | Banco **próprio** (`buteco_inbox`), no mesmo servidor Postgres mas sem tabela em comum com `apps/api`/`apps/workers`. Mesmo nome de variável dos outros apps, valor diferente — exporte um valor por processo, não um `.env` único compartilhado entre os três |
| `Inbox__CredentialEncryptionKey` | sim | AES-256 (32 bytes) em base64, para credenciais de canal. **Nunca** a mesma de `Mcp__CredentialEncryptionKey` |
| `Api__BaseUrl` | **fail-fast** | Onde `apps/api` escuta. Usada para validar `AgentId` (`GET /agents/{id}`) e como cliente A2A (`POST /agents/{id}/a2a`) |
| `PublicUrl__BaseUrl` | sim | URL pela qual este processo é alcançável a partir de `apps/api` e `apps/workers`. Vai no `pushNotificationConfig.url` de cada `SendMessage` — sem o valor correto, o round-trip nunca completa |
| `Auth__TokenSigningKey` | **fail-fast** | Mesmo valor de `apps/api`. Também usada para auto-assinar o token de serviço |
| `Debounce__Window` | não | Janela de agrupamento de mensagens da mesma sessão. Default `00:00:10` |
| `Debounce__SweepInterval` | não | Cadência de varredura do `BackgroundService` que dispara buffers vencidos. Default `00:00:02` |
| `Debounce__MaxDispatchAttempts` | não | Teto de retentativas em falha de transporte. Default `3` |
| `Session__InactivityTimeout` | não | Tempo de inatividade que fecha a fronteira de uma `Session`. Default `01:00:00` |

---

## `apps/frontend`

Ambas são resolvidas em **tempo de build** e embutidas no bundle. O build
**falha** se não forem passadas, mesmo que vazias.

| Variável | Descrição |
|---|---|
| `VITE_API_BASE_URL` | Base de `apps/api`. Vazio (`""`) significa caminho relativo |
| `VITE_INBOX_BASE_URL` | Base de `apps/inbox`. Vazio (`""`) significa caminho relativo |

Os valores vazios existem para o cenário do stack de servidor, em que o nginx
interno serve o SPA e faz proxy para os dois backends no mesmo domínio.

---

## Stack de servidor (`.env.prod.example`)

O compose de produção usa nomes de variável próprios, interpolados nos blocos
`environment:` de cada serviço. A tabela abaixo mapeia cada um para a chave
de configuração que o processo enxerga.

| Variável do `.env.prod` | Vira, no processo |
|---|---|
| `POSTGRES_USER` / `POSTGRES_PASSWORD` | credenciais do Postgres do stack (sem porta publicada no host) |
| `RABBITMQ_USER` / `RABBITMQ_PASSWORD` | credenciais do RabbitMQ do stack (sem porta publicada) |
| `PUBLIC_DOMAIN` | `PublicUrl__BaseUrl` de `apps/api` **e** de `apps/inbox` |
| `TZ` | `TZ` de `apps/workers` |
| `OPENAI_BASE_URL` / `OPENAI_API_KEY` | `OpenAI__BaseUrl` / `OpenAI__ApiKey` nos dois processos |
| `ANTHROPIC_API_KEY` / `GEMINI_API_KEY` | `Anthropic__ApiKey` / `Gemini__ApiKey` |
| `MCP_CREDENTIAL_ENCRYPTION_KEY` | `Mcp__CredentialEncryptionKey` — byte-idêntica entre `apps/api` e `apps/workers` |
| `INBOX_CREDENTIAL_ENCRYPTION_KEY` | `Inbox__CredentialEncryptionKey` — só `apps/inbox` |
| `AUTH_TOKEN_SIGNING_KEY` | `Auth__TokenSigningKey` — byte-idêntica entre `apps/api` e `apps/inbox` |
| `AUTH_OPERATOR_USERNAME` / `AUTH_OPERATOR_PASSWORD_HASH` | `Auth__OperatorUsername` / `Auth__OperatorPasswordHash` |
| `STACK_HTTP_PORT` | porta interna do host onde o nginx do stack escuta. Nunca 80 ou 443 diretamente — o proxy externo faz `proxy_pass` para cá |

Nenhum `.env` real é versionado; `.gitignore` cobre `.env` e `.env.*`,
com exceção explícita dos dois arquivos de exemplo.

A sequência de deploy e os riscos operacionais conhecidos estão em
[deployment.md](deployment.md).
