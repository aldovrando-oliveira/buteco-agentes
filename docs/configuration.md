# Configuração

Inventário das variáveis de ambiente do sistema, por processo.

Os arquivos de referência são [`.env.example`](../.env.example) (dependências
locais e os cinco apps em desenvolvimento) e
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

Estes segredos precisam ter o **mesmo valor** em mais de um processo. Esta é a
fonte mais comum de falha de configuração no sistema, porque o sintoma
aparece longe da causa.

| Segredo | Processos | O que quebra se divergir |
|---|---|---|
| `ServiceClient:Secret` do `apps/inbox` e `Identity:ServiceClients:Inbox:Secret` do `apps/api` | `apps/inbox` + `apps/api` | O `/connect/token` responde `invalid_client`, e o inbox loga `Error` com o `client_id`. A validação de agente no cadastro de canal responde `502`, e o despacho de mensagens falha como com o `apps/api` fora do ar, tentativa a tentativa (#235) |
| `ServiceClient:Secret` do `apps/connectors` e `Identity:ServiceClients:Connectors:Secret` do `apps/api` | `apps/connectors` + `apps/api` | O mesmo `invalid_client`, e o ciclo de sincronização e o "Sincronizar agora" falham como com o `apps/api` fora do ar (`503`) (#235) |
| `Mcp:CredentialEncryptionKey` | `apps/api` + `apps/workers` | `apps/api` cifra a credencial do servidor MCP ao salvar; `apps/workers` não consegue decifrá-la para conectar durante uma execução |
| Chaves de provedor de LLM | `apps/api` + `apps/workers` | `apps/api` anuncia o provedor como disponível em `GET /providers`, mas a task termina `failed` no worker |

Fora da tabela, porque não é segredo: `Identity:Issuer` do `apps/inbox` e do
`apps/connectors` precisa ser o `PublicUrl:BaseUrl` do `apps/api` (a barra final
não importa). Divergente, nenhuma chave do emissor é obtida, o log de erro traz os
dois valores, e todo token do emissor recebe `503`. Desde a #182, nenhum segredo
de assinatura de token é compartilhado: só o `apps/api` assina, e os outros apps
leem as chaves públicas do JWKS.

`Inbox:CredentialEncryptionKey` é a exceção deliberada: é **própria** de
`apps/inbox` e nunca deve ter o mesmo valor de `Mcp:CredentialEncryptionKey`
— são segredos de domínios diferentes, cada um decifrável apenas pelo
processo que o gerou.

No stack de servidor, `docker-compose.prod.yml` garante essa igualdade por
construção: uma única variável do `.env.prod` é interpolada nos blocos
`environment:` de cada serviço que a usa (três, no caso da chave de assinatura). Nunca copie o valor manualmente para
chaves separadas.

> **Perder uma chave de criptografia torna irrecuperável toda credencial já
> salva com ela.** A resposta operacional é reconfigurar as credenciais
> afetadas, não tentar recuperar as antigas.

---

## Variáveis obrigatórias

Ausentes, estes valores **derrubam o processo no boot** (fail-fast):

| Variável | Processo |
|---|---|
| `Identity__Issuer` | `apps/inbox`, `apps/connectors` (#182) |
| `Api__BaseUrl` | `apps/inbox`, `apps/connectors` (#182) |
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
| `POSTGRES_PORT` | `5432` | Porta publicada no host. **`.env.example` define `15532`**, que é o que os `appsettings.Development.json` versionados esperam — o default só vale se a variável estiver ausente |
| `RABBITMQ_USER` | `buteco` | Usuário do RabbitMQ local |
| `RABBITMQ_PASSWORD` | `buteco_dev_password` | Senha do RabbitMQ local |
| `RABBITMQ_AMQP_PORT` | `5672` | Porta AMQP publicada no host. **`.env.example` define `15772`**, idem acima |
| `RABBITMQ_MANAGEMENT_PORT` | `15672` | Porta da UI de management. **`.env.example` define `15872`** |
| `WAHA_PORT` | `3000` | Porta do WAHA, usado pelo adapter `waha` |

---

## `apps/api`

| Variável | Obrigatória | Descrição |
|---|---|---|
| `ConnectionStrings__Postgres` | sim | Banco compartilhado com `apps/workers` (`buteco_agents`) |
| `RabbitMq__Host` / `__Port` / `__Username` / `__Password` | sim | Broker para publicar jobs de task |
| `Auth__OperatorPasswordHash` | não (desde a #217) | Hash inicial do primeiro Admin, lido só pelo bootstrap: `{iterations}.{saltBase64}.{hashBase64}`, PBKDF2-HMAC-SHA256, gerado offline — ver [development.md](development.md#autenticação-em-desenvolvimento). Depois do primeiro login do Admin, pode sair, junto com o `Auth__BootstrapAdminEmail` ([deployment.md](deployment.md#primeiro-admin)). Nome e formato em revisão na #225 |
| `Auth__BootstrapAdminEmail` | não | E-mail do primeiro Admin (#180). Sem nenhum usuário, o bootstrap cria um usuário ativo com este e-mail e o hash de `Auth__OperatorPasswordHash`, marcado como legado; o primeiro login refaz em Argon2id. Com usuários, é ignorado, com log, e nunca sobrescreve senha nem e-mail. Vazio e sem usuário, o boot avisa no log. Ver [deployment.md](deployment.md#primeiro-admin) |
| `Mcp__CredentialEncryptionKey` | sim | AES-256 (32 bytes) em base64. Mesmo valor de `apps/workers`. Gerar com `openssl rand -base64 32` |
| `PublicUrl__BaseUrl` | **fail-fast** (desde a #180) | URL pública da própria API, usada em `AgentCard.SupportedInterfaces` para descoberta A2A externa e como **issuer do emissor OIDC** (#179). Não afeta `SendMessage`/`GetTask`. Ver abaixo |
| `Identity__SigningKeyEncryptionKey` | **fail-fast** | AES-256 (32 bytes) em base64, para cifrar em repouso as chaves de assinatura do emissor OIDC. **Distinta** de `Mcp__CredentialEncryptionKey`. Gerar com `openssl rand -base64 32`. Ausente, fora de base64 ou sem 32 bytes, derruba o boot. O `appsettings.Development.json` traz uma chave de desenvolvimento |
| `Identity__TwoFactorEncryptionKey` | **fail-fast** | AES-256 (32 bytes) em base64, para cifrar em repouso o segredo do TOTP de cada usuário (#218), com o id do usuário como dado associado. **Distinta** de `Identity__SigningKeyEncryptionKey` e de `Mcp__CredentialEncryptionKey`: igual a uma delas, o boot falha dizendo qual. Exigida sempre: a verificação é obrigatória para todo usuário (#220). Gerar com `openssl rand -base64 32`. Trocá-la torna os segredos gravados ilegíveis (ver [deployment.md](deployment.md#verificação-em-2-etapas-218)). O `appsettings.Development.json` traz uma chave de desenvolvimento |
| `Identity__ServiceClients__Inbox__Secret`, `Identity__ServiceClients__Connectors__Secret` | **fail-fast** | Segredos dos clientes de serviço do emissor, `buteco-servico-inbox` e `buteco-servico-connectors` (#234). Cada um em base64, com pelo menos 32 bytes, **distinto** do outro e de `Identity__SigningKeyEncryptionKey`, `Identity__TwoFactorEncryptionKey` e `Mcp__CredentialEncryptionKey`: igual a qualquer um, o boot falha nomeando as duas chaves, sem o valor. Obrigatórios mesmo sem `Connectors__BaseUrl`. O catálogo do emissor grava só o hash e regrava quando o configurado deixa de validar. Gerar com `openssl rand -base64 32`. O `appsettings.Development.json` traz valores de desenvolvimento |
| `Identity__PanelBaseUrl` | não | Base do painel quando ele não fica na origem do issuer (#180). Alimenta o `redirect_uri` e o `post_logout_redirect_uri` do cliente `buteco-painel`, e é a segunda origem aceita no `POST /auth/session`. Ausente, vale `PublicUrl__BaseUrl`, que é o caso de produção. Em desenvolvimento, `http://localhost:5173` (o Vite) |
| `OpenAI__BaseUrl` / `OpenAI__ApiKey` / `OpenAI__Model` | não | Presença habilita o provedor em `GET /providers` |
| `Anthropic__ApiKey` | não | Idem, para Claude |
| `Gemini__ApiKey` | não | Idem, para Gemini |
| `Connectors__BaseUrl` | não | Endereço do `apps/connectors`, usado só para validar a pasta no cadastro de base sincronizada. Em desenvolvimento, `http://localhost:5037`. Ver abaixo |

`Connectors__BaseUrl` tem três estados, e só um deles derruba o processo:

| valor | boot | cadastro de base `Synced` | demais rotas |
|---|---|---|---|
| ausente ou vazio | sobe, com um aviso no log | `503` com o código `connectors-not-configured`, sem chamada de rede | sem mudança |
| URI absoluta `http` ou `https` | sobe | valida a pasta no `apps/connectors` | sem mudança |
| presente e inválido | **falha**, nomeando a chave sem mostrar o valor | — | — |

O `apps/api` gera no processo o token do cliente `buteco-servico-api` para essa
chamada, com as chaves do próprio emissor (#235); não há segredo novo.

`apps/api` **nunca chama o LLM** — apenas verifica a presença das chaves para
montar a lista de provedores disponíveis.

`PublicUrl__BaseUrl` controla a direção oposta de `Cors__AllowedOrigins`:
`Cors` define quem pode **chamar** a API (entrada); `PublicUrl` é a URL pela
qual a própria API é **alcançada** de fora, do ponto de vista do AgentCard.

`PublicUrl__BaseUrl` também é o issuer do **emissor OIDC** (#179), e desde a
#180 é obrigatória, porque o login de usuário depende dele:

| valor | boot | emissor | demais rotas |
|---|---|---|---|
| ausente ou vazio | **falha**, com "PublicUrl:BaseUrl não configurada" | — | — |
| URI absoluta `http` ou `https`, sem query nem fragmento | sobe, se `Identity__SigningKeyEncryptionKey` for válida | ligado, com essa URL como issuer; todos os endereços do discovery saem dela | `503` até as chaves carregarem do banco (ver [deployment.md](deployment.md)) |
| qualquer outro valor | **falha**, com "PublicUrl:BaseUrl inválido" | — | — |

Até a #180, a url ausente subia o app com o emissor desligado e os endereços A2A
ausentes; esse estado deixou de existir.

### Provedores federados (`Identity:FederatedProviders`, #189)

O login pelo Google fica **atrás do emissor do Buteco**: o `apps/api` é cliente OIDC
do provedor, e o painel continua falando só com o emissor. Sem a seção, nada muda: o
login fica só com senha, e o cliente OIDC nem é registrado.

A seção é um dicionário por **chave neutra**, escolhida por quem instala (por
exemplo `corporativo`); a chave vai nas rotas (`/auth/federated/<chave>/…`) e no
motivo das recusas. Na M3 o único tipo é `google`:

| campo | | regra |
|---|---|---|
| a chave | obrigatória | de 2 a 32 caracteres, minúsculas, dígitos e hífen, começando por letra |
| `Type` | obrigatório | `google` |
| `ClientId` | obrigatório | o ID do cliente OAuth do Google; não é segredo |
| `ClientSecret` | obrigatório | **só por variável de ambiente**: `Identity__FederatedProviders__<chave>__ClientSecret` |
| `HostedDomain` | opcional | o claim `hd` do id token tem de ser igual, em todo login e todo conectar; é um filtro, nunca entrada automática |

**Fixos pelo tipo, sem campo:** o issuer `https://accounts.google.com/`, o nome
"Google", os escopos `openid email profile`, `prompt=select_account` e o modo de
resposta `query`. A URI de retorno é a base do painel (`Identity__PanelBaseUrl`, ou
`PublicUrl__BaseUrl` sem ela) mais `/auth/federated/callback`. Mudar a seção exige
reiniciar o `apps/api`; tirar uma entrada desliga o provedor, e os vínculos ficam
gravados, sem valer, até um provedor do mesmo issuer voltar.

**O boot reprova**, sem banco e sem rede, nomeando a chave e o campo, nunca o valor
do segredo:

1. chave fora do formato;
2. `Type` ausente;
3. `Type` diferente de `google`;
4. `ClientId` ausente ou em branco;
5. `ClientSecret` ausente ou em branco;
6. dois provedores com o mesmo issuer (o vínculo é pelo par `issuer`, `sub`);
7. `ClientSecret` cujo valor efetivo vem de um arquivo JSON de configuração;
8. campo fora de `Type`, `ClientId`, `ClientSecret` e `HostedDomain` (um `ClientID` com
   erro de digitação, que o binder ignoraria);
9. `HostedDomain` que não é um nome de domínio;
10. `ClientSecret` igual a uma das chaves de cifra ou a um dos segredos de cliente de
    serviço do `apps/api`.

Alcançar o Google não é condição de boot: com ele fora do ar, o `apps/api` sobe, e o
login pelo Google recusa com o motivo `falha`, com log.

O login pelo Google **também pede o código das 2 etapas do Buteco** (Q1 da #189): o
retorno grava a sessão pendente e manda ao passo do código, ou ao cadastro de quem
ainda não tem a verificação. O cliente OAuth no Google Cloud e o arquivo do segredo
no servidor estão em [deployment.md](deployment.md#provedores-federados-189).

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
| `Embedding__Provider` | condicional | Provedor do modelo de embedding das bases de conhecimento. Hoje só `openai` é suportado — `anthropic` e `gemini` não expõem tipo de embedding no SDK referenciado |
| `Embedding__Model` | condicional | Modelo de embedding. **Não tem default**: foi escolhido por medição, e o modelo que um ambiente serve por padrão pode empatar com busca lexical |
| `Embedding__Dimensions` | condicional | Dimensão declarada, **conferida contra a que o provedor devolve no momento da gravação**. O gateway pode aceitar o parâmetro `dimensions` e ignorá-lo, e sem a conferência o índice seria gravado com vetores incompatíveis sem erro nenhum |
| `Embedding__BatchSize` | não | Quantos fragmentos vão em **cada chamada** ao gerador de embedding. **A única das quatro com default** (250), e ele é **provisório**: medido no piloto de 20/09/2026, um documento de ~442 fragmentos falhou com `502 upstream_error` nas três tentativas e as duas metades dele (267 e 175) indexaram — o **formato** do teto do gateway (entradas, bytes ou tempo de resposta) não foi estabelecido. Baixe o valor se a indexação de documento grande falhar; variá-lo é como o teto vai ser descoberto. Valor `<= 0` **reprova o boot**, e não é corrigido em silêncio |
| `TaskConsumer__MaxConcurrentExecutions` | não | Quantas tasks **esta instância** executa ao mesmo tempo; o mesmo valor é o prefetch e a concorrência de despacho do canal de tasks, nunca separados (prefetch maior prenderia entregas no processo). **Default 4, NÃO MEDIDO**: escolha de forma (uma cadeia de delegação de profundidade 3, ou duas delegações ao mesmo tempo, numa instância), recalibrada pelo pico de conversas delegando ao mesmo tempo, na #48. Fora de `[1, 32]` **reprova o boot**, sem correção; `0` seria prefetch sem limite. O teto de 32 protege o **pool do Npgsql deste processo** (uma conexão dedicada ao lock de contexto mais uma de vida curta por execução, contra o `Maximum Pool Size` de 100). **Não protege o servidor:** o `max_connections` do Postgres é dividido com `apps/api` e `apps/inbox`, e a checagem de boot não o vê. **Subir `N` acima do valor verificado antes do deploy (registrado no `02`) exige refazer aquela verificação antes.** Rollback: `1`, o comportamento anterior, só reiniciando |

### Rotação de chave de provedor exige reiniciar o processo

**Trocar `OpenAI__ApiKey`, `Anthropic__ApiKey` ou `Gemini__ApiKey` não tem efeito
num processo já em execução.** É preciso reiniciar `apps/workers` (e `apps/api`,
que lê as mesmas seções para decidir quais provedores oferecer).

São duas razões independentes, e a primeira sozinha já basta:

1. As chaves são lidas via `IOptions<T>`, que resolve o valor **uma vez**; e na
   prática chegam por variável de ambiente, que um processo em execução não vê
   mudar de qualquer forma.
2. O client de LLM é **cacheado por `(provider, model)`** e captura a credencial
   no momento da construção (ver [architecture.md](architecture.md)).

Não há invalidação a acionar nem endpoint de recarga — a rotação é: atualizar a
variável e reiniciar. Está escrito aqui de propósito: "precisa reiniciar"
descoberto durante uma rotação de emergência é o pior momento possível para
descobrir.

> **Gatilho.** Se um dia `IOptionsMonitor<T>` substituir `IOptions<T>` em
> qualquer uma das três `Options` de provedor — para fazer configuração fluir a
> quente —, a razão 1 cai e a razão 2 **não**: o cache passa a precisar de
> invalidação na mudança, **descartando** o client removido. Sem isso a troca de
> chave ficaria silenciosamente sem efeito, que é pior que exigir restart.

**O modelo de embedding é usado em dois momentos, não só na indexação.** Ele
vetoriza cada fragmento quando um documento é indexado, e vetoriza a **consulta**
a cada vez que um agente chama uma tool de conhecimento — ou seja, **por
mensagem**, no caminho quente. Os dois lados precisam do mesmo modelo: vetores de
modelos diferentes são incomparáveis, e `apps/workers` recusa subir quando o
modelo declarado diverge do gravado no índice.

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
| `ServiceClient__Secret` | **fail-fast** | Segredo do cliente `buteco-servico-inbox` no emissor do `apps/api`: o mesmo valor de `Identity__ServiceClients__Inbox__Secret` lá. Com ele o inbox pede o token de serviço em `{Api__BaseUrl}/connect/token`, com cache até 1 min antes de vencer. Base64 com pelo menos 32 bytes, e **distinto** de `Inbox__CredentialEncryptionKey`: igual, o boot falha nomeando as duas chaves, sem o valor (#235). O `appsettings.Development.json` traz o valor de desenvolvimento |
| `Identity__Issuer` | **fail-fast** (#182) | Issuer do emissor OIDC do `apps/api`: o mesmo valor do `PublicUrl__BaseUrl` de lá. É o que o inbox aceita como token (audience `buteco-inbox`); sem ele o app não sobe. Ver "Validação do token do emissor", abaixo |
| `Debounce__Window` | não | Janela de agrupamento de mensagens da mesma sessão. Default `00:00:10` |
| `Debounce__SweepInterval` | não | Cadência de varredura do `BackgroundService` que dispara buffers vencidos. Default `00:00:02` |
| `Debounce__MaxDispatchAttempts` | não | Teto de retentativas em falha de transporte. Default `3` |
| `Debounce__SessionSerializationMaxWait` | não | Teto da serialização por sessão (#162): por quanto tempo, contado da última mensagem dele, um disparo em `Dispatching` segura a mensagem seguinte da mesma conversa. Passado o teto, ele deixa de segurar e continua com a reconciliação. Cobre uma resposta travada do provedor (400 s, #134) mais uma delegação (120 s), e é igual a `DispatchReconciliation__UntrackedDispatchMaxAge`; não foi medido como duração real de turno. Recalibrar se turnos passarem de 5 min, se a #134 mudar ou se `UntrackedDispatchMaxAge` mudar. **Fail-fast** se for menor ou igual a zero ou a `Debounce__Window`. Default `00:10:00` |
| `DispatchReconciliation__Interval` | não | Cadência da varredura que resolve disparos em `Dispatching` sem push (#47). Um `GetTask` em `apps/api` por linha por ciclo. Default `00:01:00` |
| `DispatchReconciliation__TerminalGrace` | não | Quanto tempo depois do estado terminal da task a varredura espera um push que ainda pode estar em processamento: 5 s do push + 100 s do envio ao canal. Recalibrar se algum desses timeouts mudar. Default `00:02:00` |
| `DispatchReconciliation__UntrackedDispatchMaxAge` | não | Idade da última mensagem além da qual um disparo em `Dispatching` **sem** `TaskId` é encerrado como perda, com aviso ao contato. Mede o `receivedAt` que o adapter passa, que tem de ser o relógio do inbox. Default `00:10:00` |
| `DispatchReconciliation__ClaimLease` | não | Prazo de posse de uma reivindicação da varredura: enquanto ele não vence, nenhuma outra instância reivindica o mesmo disparo. Mesma derivação de `TerminalGrace`. Default `00:02:00` |
| `Session__InactivityTimeout` | não | Tempo de inatividade que fecha a fronteira de uma `Session`. Default `01:00:00` |

---

## `apps/connectors`

Sem banco. Em desenvolvimento, `dotnet run` na porta 5037. A implantação no stack de
servidor ainda não existe (#119).

| Variável | Obrigatória | Descrição |
|---|---|---|
| `ServiceClient__Secret` | **fail-fast** | Segredo do cliente `buteco-servico-connectors` no emissor do `apps/api`: o mesmo valor de `Identity__ServiceClients__Connectors__Secret` lá. Base64, pelo menos 32 bytes, sem ecoar o valor (#235). Obrigatório porque o `Api__BaseUrl` é (#182). O `appsettings.Development.json` traz o valor de desenvolvimento |
| `GoogleDrive__ServiceAccountKeyBase64` | não | O arquivo JSON da chave da service account do Google, em base64 numa linha (`base64 -i chave.json \| tr -d '\n'`). **Ausente ou vazia, o provedor `google-drive` não existe** para o processo: o app sobe e `GET /connectors/providers` não o lista. **Presente e inválida, o boot falha**, com a verificação que falhou e sem trecho do valor. Só o `client_email` sai do processo, na rota de provedores |
| `Cors__AllowedOrigins` | não | Origens do painel autorizadas por CORS, no mesmo formato dos outros apps |
| `Api__BaseUrl` | **fail-fast** (#182) | Endereço do `apps/api`, usado pelo ciclo de sincronização e para obter as chaves do emissor. Em desenvolvimento, `http://localhost:5017`. Ver abaixo |
| `Identity__Issuer` | **fail-fast** (#182) | Issuer do emissor OIDC do `apps/api`, como no `apps/inbox` (audience `buteco-connectors`). Sem ele o app não sobe; com ele e sem `Api__BaseUrl`, o boot falha nomeando as duas chaves, porque não há de onde ler as chaves do emissor. Ver "Validação do token do emissor", abaixo |

`Api__BaseUrl`:

| valor | boot | ciclo periódico | "Sincronizar agora" |
|---|---|---|---|
| ausente ou vazio | **falha**, pela checagem do `Identity__Issuer`, nomeando as duas chaves (#182) | — | — |
| URI absoluta `http` ou `https` | sobe | roda a cada 5 minutos | `202` |
| outro valor | **falha**, nomeando a chave e sem ecoar o valor | — | — |

*(Até a #182 o `Api__BaseUrl` era opcional: ausente, o app subia com aviso, o ciclo
não rodava e o "Sincronizar agora" respondia `503 sync-not-configured`. Esse
tratamento ficou no código, inalcançável, e sai na #241.)*

A navegação e a descrição de pasta não dependem dele. O ciclo chama o `apps/api` como
`service:connectors` com o token do cliente `buteco-servico-connectors`, obtido no emissor
pelo `Api__BaseUrl` com o `ServiceClient__Secret` (#235).

A service account usa só o escopo `drive.readonly`. A Drive API precisa estar
**ativada no projeto do Google Cloud dono da service account**; sem isso, toda
chamada responde `502` com o código `api-not-configured`.

---

## Validação do token do emissor (`apps/inbox` e `apps/connectors`)

`Identity__Issuer` é, nos dois apps, o emissor cujo access token eles aceitam
(#208), e o único token aceito desde a #182.

| valor | boot | requisição com token | requisição sem token |
|---|---|---|---|
| ausente ou vazio | **falha**, nomeando `Identity:Issuer` (#182) | — | — |
| URI absoluta `http` ou `https`, sem query nem fragmento | sobe, sem nenhuma chamada ao `apps/api`; as chaves vêm em segundo plano | aceito com a `aud` do app; `503` com `Retry-After: 10` até a primeira obtenção das chaves | `401` com `WWW-Authenticate: Bearer`, com ou sem chaves |
| outro valor | **falha**, nomeando `Identity:Issuer` | — | — |

As chaves são lidas de `{Api__BaseUrl}/.well-known/openid-configuration` (só para
conferir o `issuer`) e `{Api__BaseUrl}/.well-known/jwks`, nunca do endereço
público. O subject do token sai do `client_id`, por uma tabela fixa no código
(`buteco-painel` → `operator`); cliente fora dela recebe `403`. Não há opção de
configuração para os intervalos (busca a cada 10 s até obter; renovação a cada
hora; `kid` desconhecido no máximo uma busca por minuto).

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

O login pelo emissor (`/auth/session`, `/connect/*` e `/.well-known/*`) usa
sempre caminho relativo, sem variável: em produção, o nginx leva esses prefixos
ao `apps/api`; em desenvolvimento, o proxy do `vite.config.ts` faz o mesmo
([development.md](development.md#autenticação-em-desenvolvimento)).

### `VITE_CONNECTORS_BASE_URL` (opcional)

Base de `apps/connectors`, também resolvida em build, mas **opcional**: o build
não falha sem ela. Três estados (change `frontend-cadastro-base-sincronizada`,
D1):

| Valor no build | Efeito no painel |
|---|---|
| ausente | a origem "Sincronizada" do cadastro de base aparece indisponível, com uma explicação, e o painel não faz nenhuma chamada a `apps/connectors` |
| `""` | caminho relativo (`/connectors/...` no mesmo domínio), como os outros dois |
| URL absoluta | aquele endereço; em desenvolvimento, `http://localhost:5037` |

No stack de servidor ela é `""`, fixa no `build.args` do compose, e existe só
junto com o bloco `/connectors` do nginx (#119): com `""` e sem o bloco, as
chamadas cairiam no SPA.

---

## Stack de servidor (`.env.prod.example`)

O compose de produção usa nomes de variável próprios, interpolados nos blocos
`environment:` de cada serviço. A tabela abaixo mapeia cada um para a chave
de configuração que o processo enxerga.

| Variável do `.env.prod` | Vira, no processo |
|---|---|
| `POSTGRES_USER` / `POSTGRES_PASSWORD` | credenciais do Postgres do stack (sem porta publicada no host) |
| `RABBITMQ_USER` / `RABBITMQ_PASSWORD` | credenciais do RabbitMQ do stack (sem porta publicada) |
| `PUBLIC_DOMAIN` | `PublicUrl__BaseUrl` de `apps/api` **e** de `apps/inbox`, e `Identity__Issuer` de `apps/inbox` **e** de `apps/connectors` |
| `TZ` | `TZ` de `apps/workers` |
| `OPENAI_BASE_URL` / `OPENAI_API_KEY` | `OpenAI__BaseUrl` / `OpenAI__ApiKey` nos dois processos |
| `ANTHROPIC_API_KEY` / `GEMINI_API_KEY` | `Anthropic__ApiKey` / `Gemini__ApiKey`, em `apps/api` **e** `apps/workers`. Opcionais: vazias, os provedores não aparecem em `GET /providers` |
| `EMBEDDING_PROVIDER` / `EMBEDDING_MODEL` / `EMBEDDING_DIMENSIONS` | `Embedding__Provider` / `__Model` / `__Dimensions` de `apps/workers`. **`MODEL` e `DIMENSIONS` são obrigatórias** — o compose falha ao processar o arquivo sem elas, porque a ausência não impede o boot e só aparece na primeira indexação |
| `EMBEDDING_BATCH_SIZE` | `Embedding__BatchSize` de `apps/workers`. **Opcional, default 250** — diferente das duas acima, a ausência não quebra nada. É o parâmetro a ajustar quando a indexação de documento grande falhar no gateway |
| `TASK_CONSUMER_MAX_CONCURRENT_EXECUTIONS` | `TaskConsumer__MaxConcurrentExecutions` de `apps/workers`. **Opcional, default 4, não medido**; fora de `[1, 32]` reprova o boot. Rollback: `1`. Subir acima do `N` verificado contra o `max_connections` do Postgres exige refazer a verificação antes (ver a linha da tabela de `apps/workers`) |
| `MCP_CREDENTIAL_ENCRYPTION_KEY` | `Mcp__CredentialEncryptionKey` — byte-idêntica entre `apps/api` e `apps/workers` |
| `IDENTITY_SIGNING_KEY_ENCRYPTION_KEY` | `Identity__SigningKeyEncryptionKey` — só `apps/api`, distinta das outras chaves |
| `IDENTITY_TWO_FACTOR_ENCRYPTION_KEY` | `Identity__TwoFactorEncryptionKey` — só `apps/api`, distinta das outras chaves; o boot recusa a igual |
| `SERVICE_CLIENT_SECRET_INBOX` | `Identity__ServiceClients__Inbox__Secret` no `apps/api` e `ServiceClient__Secret` no `apps/inbox`; distinto do outro segredo de serviço, das chaves de cifra do api e de `INBOX_CREDENTIAL_ENCRYPTION_KEY` |
| `SERVICE_CLIENT_SECRET_CONNECTORS` | `Identity__ServiceClients__Connectors__Secret` no `apps/api` e `ServiceClient__Secret` no `apps/connectors`; distinto do outro segredo de serviço e das chaves de cifra |
| `INBOX_CREDENTIAL_ENCRYPTION_KEY` | `Inbox__CredentialEncryptionKey` — só `apps/inbox` |
| `GOOGLE_DRIVE_SERVICE_ACCOUNT_KEY_BASE64` | `GoogleDrive__ServiceAccountKeyBase64` de `apps/connectors`. **Opcional**: vazia, o provedor `google-drive` não aparece. Inválida, só o `apps/connectors` não sobe; o painel continua no ar |
| `AUTH_OPERATOR_PASSWORD_HASH` / `AUTH_BOOTSTRAP_ADMIN_EMAIL` | `Auth__OperatorPasswordHash` / `Auth__BootstrapAdminEmail` de `apps/api`. **Opcionais** (sem `:?`): só o bootstrap do primeiro Admin as lê, e elas podem sair do `.env.prod` depois do primeiro login dele |
| `STACK_HTTP_PORT` | porta interna do host onde o nginx do stack escuta. Nunca 80 ou 443 diretamente — o proxy externo faz `proxy_pass` para cá |

Três valores do stack não vêm do `.env.prod`, porque não são segredo nem variam
por ambiente: `Connectors__BaseUrl` (`http://connectors:8080`, no `apps/api`),
`Api__BaseUrl` (`http://api:8080`, no `apps/connectors`, como no `apps/inbox`) e
`VITE_CONNECTORS_BASE_URL` (`""`, build do frontend). Ficam fixos no compose.

**Os provedores federados não vêm do `.env.prod`**: a chave é de quem instala, e o
compose não tem um nome fixo a interpolar. Eles vêm de um arquivo opcional de
ambiente **só do `api`**, `federated-providers.env`, ao lado do `.env.prod` e fora do
repositório, com as variáveis `Identity__FederatedProviders__<chave>__Type`,
`__ClientId`, `__HostedDomain` e `__ClientSecret` (o exemplo, sem segredo, é o
[`federated-providers.env.example`](../federated-providers.env.example)). Sem o arquivo,
nenhum provedor. Exige Docker Compose 2.24.0 ou mais novo (ver
[deployment.md](deployment.md#provedores-federados-189)).

Nenhum `.env` real é versionado; `.gitignore` cobre `.env` e `.env.*`,
com exceção explícita dos dois arquivos de exemplo, e cobre o
`federated-providers.env`.

A sequência de deploy e os riscos operacionais conhecidos estão em
[deployment.md](deployment.md).
