## Context

A linha de bases sincronizadas tem cinco peças. O catálogo do `apps/api` (#102) e o
`apps/connectors` com o conector Google Drive (#103) estão arquivados. Esta change
liga os dois: o operador cria uma base `Synced` pela rota que já existe, e o
`apps/api` valida a pasta no app que acessa o provedor.

O que foi lido antes de propor, e que decide o desenho:

- **A rota de hoje recusa `Synced` no endpoint, antes do comando.**
  `KnowledgeBaseEndpoints.ValidateContentMode` devolve `400` em `contentMode`, e
  `CreateKnowledgeBaseCommand` só carrega nome e descrição
  (`apps/api/src/Buteco.Api/KnowledgeBases/Endpoints/KnowledgeBaseEndpoints.cs`).
  `KnowledgeBase.CreateSynced(name, description, provider, folderId, folderName,
  folderUrl)` existe desde a #102 e só os testes o chamam; esta change é o
  consumidor de produção agendado na D11 dela (convenção 25).
- **O índice da pasta já existe:** `IX_knowledge_bases_SyncProvider_SyncFolderId`,
  único, parcial em `"ContentMode" = 'Synced'`, sem filtro por `IsActive`
  (migração `20261003045343_AddKnowledgeBaseSync`). Nenhuma migração nesta change.
- **A rota de descrição do `apps/connectors`:**
  `GET /connectors/providers/{providerKey}/folder?id=`, só para `service:api`,
  `200` com `{ "id", "name", "webUrl" }`. Falha em `ProblemDetails` com a extensão
  `code` e o detalhe em `detail`, status por natureza: `404`
  `provider-not-configured`; `422` `access-denied`, `not-a-folder`,
  `folder-trashed`; `502` `api-not-configured`, `provider-auth-failed`,
  `provider-error`; `503` `rate-limited`, `provider-unavailable`; `403` só para
  subject recusado; `400` sem `id` (`ConnectorEndpoints.StatusFor`). O
  `access-denied` leva o e-mail da conta no `detail`.
- **O limite de tempo do lado do `apps/connectors`** é de 30 s por chamada de
  metadado e também para a troca de token do Google (D2 da #103). Uma descrição
  de pasta com o token do Google expirado são duas chamadas: troca e `files.get`.
- **O molde da chamada:** o `apps/inbox` registra um `HttpClient` nomeado com
  `BaseAddress` de `Api:BaseUrl`, `Timeout` fixo de 5 s e o
  `ServiceTokenDelegatingHandler`, que assina `service:inbox` a cada requisição
  com TTL fixo de 5 min (`apps/inbox/src/Buteco.Inbox/Program.cs:82-88`,
  `Auth/ServiceTokenDelegatingHandler.cs`). Falha de transporte, timeout ou status
  inesperado do `apps/api` viram `502` no cadastro de canal
  (`AgentReferenceValidator`, `ChannelEndpoints.cs:52`). **Diferença:** o
  `apps/inbox` exige `Api:BaseUrl` no boot (`throw` no `Program.cs`); aqui a
  configuração precisa ser opcional (D1).
- **O idioma de corrida da casa** é índice único, `catch (DbUpdateException)` com
  `PostgresException { SqlState: UniqueViolation }` filtrado pelo
  `ConstraintName`, detach e uma releitura (D10 da #102, e `apps/inbox`
  `ContactSessionResolver`). Os eventos de log que provam a corrida usam ids
  `1021`, `1022` e `1023` no `apps/api`; o próximo livre é `1024`.
- **O nginx do painel não fixa `proxy_read_timeout`** em
  `apps/frontend/deploy/nginx.conf` (nenhuma diretiva `*_timeout` no arquivo),
  então vale o padrão do nginx, 60 s. Pesa na D2.
- **Nenhuma rota do `apps/api` repassa código de outro app hoje.** O `502` de
  validação de MCP (`AgentMcpBindingEndpoints.cs:68`) leva só título e detalhe.
  Esta change é a primeira a pôr a extensão `code` numa resposta do `apps/api`.

## Goals / Non-Goals

**Goals:**

- `POST /knowledge-bases` com `contentMode: "Synced"` cria a base com a pasta
  validada pelo `apps/connectors` e os snapshots vindos da resposta dele.
- Toda recusa do cadastro `Synced` é explícita e tem código; nenhuma é `500`, e
  nenhuma cria base.
- O `apps/api` sobe sem o `apps/connectors` configurado, sem mudar nenhuma outra
  rota.
- A chamada real entre os dois apps é provada por teste de ida e volta, sem
  Google.

**Non-Goals:**

- Telas (#106), ciclo (#105), exclusão de base (#108), implantação em produção do
  `apps/connectors` (#119).
- Qualquer mudança de código no `apps/connectors`.
- Traduzir código em frase: o texto é da #106.
- Revalidar a pasta depois do cadastro: a pasta é imutável, e o estado de acesso
  passa a ser do ciclo (#105).

## Decisions

### D1. `Connectors:BaseUrl` é opcional; sem ela, só o cadastro `Synced` é recusado, com `503` `connectors-not-configured`

Configuração nova do `apps/api`: **`Connectors:BaseUrl`** (`Connectors__BaseUrl`
no ambiente), no molde de `Api:BaseUrl` do `apps/inbox`.

| valor | boot | cadastro `Synced` | demais rotas |
|---|---|---|---|
| ausente ou vazio | sobe, com um aviso no log de boot | `503`, `code: "connectors-not-configured"`, sem chamada nenhuma | iguais |
| URI absoluta `http` ou `https` | sobe | chama o `apps/connectors` | iguais |
| presente e inválido (relativo, outro esquema, texto) | **falha**, nomeando a chave e sem ecoar o valor | — | — |

- **O valor é lido por `IOptions<ConnectorsOptions>` na requisição**, e o
  `HttpClient` nomeado é registrado sempre. Sem o valor, o `ConnectorsFolderClient`
  recusa antes de criar o `HttpClient`. *Corrigido na implementação:* a primeira
  redação dizia "o comando recusa"; a recusa ficou no cliente, que é o único lugar
  que lê o endereço, e o comando trata o resultado como qualquer outra falha de
  validação. O comportamento especificado não muda. O aviso de boot existe porque em produção, até a #119, esse é
  o estado esperado, e quem olha o log precisa saber por que o cadastro
  sincronizado está fechado.
- **A checagem de forma é de boot (convenção 8), sobre o host construído**, como
  `ValidateTimeZoneConfiguration`. Presente e inválido é erro de digitação, e a
  alternativa seria subir e recusar todo cadastro `Synced` com um código que diz
  "não configurado" quando está configurado errado. É o mesmo raciocínio da chave
  do Google presente e inválida na D3 da #103.
- **Vazio conta como ausente:** a interpolação `${VAR:-}` do compose entrega
  string vazia, e é o que a #119 vai fazer enquanto o operador não define a
  variável.
- **`appsettings.Development.json` traz `http://localhost:5037`** (porta do
  `apps/connectors` em desenvolvimento, conferida em
  `apps/connectors/src/Buteco.Connectors/Properties/launchSettings.json:8`), e o `appsettings.json` traz `""`.
  `ApiFactoryFixture` força o valor vazio, então **toda a suíte do `apps/api` roda
  sem o `apps/connectors` configurado**: todas as classes de contêiner provam, de
  graça, que nenhuma outra rota depende dele. Os testes do cadastro `Synced`
  derivam um host com o valor (D9).

**Status `503`.** O pedido está bem formado e a rota existe (o `Manual`
funciona); o que falta é uma dependência do servidor. `503` diz "o servidor não
atende isto agora", e o código distingue "não configurado" de "fora do ar".

- *Descartado, `Connectors:BaseUrl` obrigatória (fail-fast, como o
  `apps/inbox`):* o `apps/api` é implantado a cada deploy e o `apps/connectors`
  só chega com a #119; o próximo deploy derrubaria a produção.
- *Descartado, flag de recurso separada (`Connectors:Enabled`):* duas
  configurações para um estado só, e a combinação "habilitado sem endereço"
  precisaria de uma terceira regra.
- *Descartado, `400` ou manter a recusa da #102:* `400` diz que o cliente errou,
  e o mesmo corpo passa a ser aceito quando a variável chega.
- *Descartado, `501 Not Implemented`:* a rota implementa `Synced`; o que falta é
  configuração da implantação, e `501` lê como defeito permanente do método.
- *Descartado, esconder a falta de configuração atrás de `connectors-unavailable`:*
  o operador investigaria uma rede que não existe.

### D2. Limite de 35 s; falha de transporte é `503` `connectors-unavailable`, resposta fora do contrato é `502` `connectors-error`

O `HttpClient` nomeado do `apps/connectors` tem `Timeout` **fixo de 35 s**, no
código, sem configuração (convenção 2; o `apps/inbox` também fixa o dele).

Por que 35 s:

- **acima dos 30 s de metadado do `apps/connectors`:** um Google lento em
  `files.get` estoura primeiro do lado do `apps/connectors`, que devolve `503`
  `provider-unavailable`, e esse código, que é o mais preciso, chega ao cliente.
  Com um limite menor que 30 s, o `apps/api` cortaria antes e trocaria a causa
  real por `connectors-unavailable`;
- **abaixo dos 60 s do nginx:** o painel recebe o código do `apps/api`, e não um
  `504` do proxy sem corpo útil;
- **não cobre o pior caso de duas chamadas de 30 s** (troca de token mais
  `files.get`), de propósito: cobrir exigiria mais de 60 s e passaria do nginx.
  Nesse caso raro o resultado é `connectors-unavailable`, e a descrição é leitura
  sem efeito colateral, então cortar no meio não deixa nada pela metade.

| o que acontece | status do `apps/api` | `code` | `detail` |
|---|---|---|---|
| conexão recusada, DNS, `HttpRequestException`, timeout de 35 s | `503` | `connectors-unavailable` | `null` |
| resposta fora do contrato: `400`, `401`, `403`, `500`, outro status; erro sem `code` no formato; `200` sem `id`, `name` ou `webUrl`, com `webUrl` que não é URI absoluta `http(s)`, ou com `id` diferente do pedido | `502` | `connectors-error` | o status HTTP recebido, como texto (`"401"`) |

- **`401` e `403` do `apps/connectors` são `connectors-error`**, não repassados:
  significam chave de assinatura divergente ou subject fora da tabela, defeito da
  implantação, e repassar `401` faria o painel achar que o **operador** perdeu a
  sessão e deslogar (`apps/frontend/src/auth/token.ts` limpa a sessão em `401`).
- **Cancelamento do próprio cliente** (requisição abortada) propaga, como no
  `AgentReferenceValidator`: só o timeout do `HttpClient` vira código.
- Log de aviso com o status recebido e o código, nunca com o token.

- *Descartado, 5 s como o `apps/inbox`:* menor que o limite do próprio
  `apps/connectors`; trocaria `provider-unavailable` e `rate-limited` por
  `connectors-unavailable` sempre que o Google demorasse mais de 5 s.
- *Descartado, 65 s ou mais:* passa do nginx; o painel veria `504` sem código.
- *Descartado, limite configurável:* não há decisão de produto por ambiente; é
  amarrado aos 30 s do outro app e aos 60 s do proxy, e mudar um exige rever os
  três juntos (Risks).
- *Descartado, retentativa automática:* o cadastro é ação do operador, que vê a
  recusa e repete; retentar dentro de 35 s levaria a requisição além do proxy.

### D3. O código do `apps/connectors` passa como veio, e o status segue a natureza que o `apps/connectors` já deu

A falha de validação vira `ProblemDetails` do `apps/api` com a extensão **`code`**
igual ao código recebido e **`detail`** igual ao detalhe recebido (o e-mail da
conta no `access-denied`, `null` quando não há). O `title` é uma frase fixa
genérica em português, só para quem lê a resposta crua; o painel decide a
mensagem pelo `code` (#106), como na D1 da #102.

**O status é o do `apps/connectors`, com uma tradução só:**

| status do `apps/connectors` (com `code` válido) | status do `apps/api` |
|---|---|
| `422` (`access-denied`, `not-a-folder`, `folder-trashed`) | `422` |
| `404` (`provider-not-configured`) | **`422`** |
| `502` (`api-not-configured`, `provider-auth-failed`, `provider-error`) | `502` |
| `503` (`rate-limited`, `provider-unavailable`) | `503` |

- **`404` vira `422`** porque, num `POST` de coleção, `404` diz que a rota ou o
  recurso não existe. O provedor pedido é que não existe na implantação: o pedido
  está bem formado e não pode ser atendido, que é `422`.
- **"Código válido"** é o formato de `SyncCode` (`^[a-z0-9]+(-[a-z0-9]+)*\z`, até
  64 caracteres). O `apps/api` não tem lista fechada dos códigos do
  `apps/connectors`: um código novo de um conector futuro passa sem mudar o
  `apps/api`, pelo argumento da D1 da #102. Fora do formato, ou com status fora da
  tabela, é `connectors-error` (D2).
- **Os códigos próprios do `apps/api` levam o prefixo `connectors-`**
  (`connectors-not-configured`, `connectors-unavailable`, `connectors-error`),
  que diz que a falha é na ligação entre os dois apps, e não no provedor. Nenhum
  código do `apps/connectors` tem esse prefixo hoje.
- O detalhe é repassado sem filtro porque o `apps/connectors` já decide o que
  pode sair (D5 da #103: só o e-mail e o `reason` no formato de código).
- **Achado da implementação, sem mudar a decisão (tarefa 1.2):** sem detalhe, o
  `apps/connectors` **omite** a chave `detail`, em vez de mandar `null`
  (`ConnectorEndpointsTests.cs:134-136`). O cliente trata ausente como nulo, e os
  testes do `apps/api` forjam o erro com a chave omitida, como o outro app faz.

- *Descartado, tabela fechada de código para status no `apps/api`:* duplicaria a
  tabela do `apps/connectors` em outro app, sem `ProjectReference` possível, e um
  código novo exigiria mudar o `apps/api`.
- *Descartado, um status só (`422` ou `502`) para toda falha do provedor:* o
  painel e quem lê o log perderiam a natureza que o outro app já classificou;
  `rate-limited` como `422` afirmaria que o pedido não tem conserto.
- *Descartado, traduzir para frase no `apps/api`:* o texto é da tela (#106).
- *Descartado, repassar o corpo do `apps/connectors` inteiro:* o `title` e o tipo
  do outro app vazariam para o contrato do `apps/api`, e qualquer campo novo dele
  viraria contrato daqui sem decisão.

### D4. Token `service:api` por `DelegatingHandler` próprio, duplicado do `apps/inbox`

`apps/api/src/Buteco.Api/Auth/ServiceTokenDelegatingHandler.cs`, cópia de
`apps/inbox/src/Buteco.Inbox/Auth/ServiceTokenDelegatingHandler.cs` com o subject
trocado:

- constante `ApiSubject = "service:api"` no próprio handler. Ela **não** entra na
  tabela de `ServiceScopeAuthorizationHandler`, que é de quem pode chamar o
  `apps/api`: um token `service:api` que chegue ao `apps/api` recebe `403`, como
  qualquer subject desconhecido (teste negativo próprio);
- `ITokenService.Issue(ApiSubject, 5 min)`, mesma `Auth:TokenSigningKey`, TTL fixo
  de 5 minutos, um token novo a cada requisição, sem cache;
- `AddTransient` e `AddHttpMessageHandler` no `HttpClient` nomeado do
  `apps/connectors`, e em nenhum outro cliente.

**Onde está o par**, registrado num comentário de espelhamento nos dois handlers e
no `docs/architecture.md`:

| ponta | arquivo |
|---|---|
| quem assina `service:api` | `apps/api/src/Buteco.Api/Auth/ServiceTokenDelegatingHandler.cs` (novo) |
| molde, que assina `service:inbox` | `apps/inbox/src/Buteco.Inbox/Auth/ServiceTokenDelegatingHandler.cs` |
| quem aceita `service:api` | `apps/connectors/src/Buteco.Connectors/Auth/ConnectorsSubjectAuthorizationHandler.cs` (tabela da D1 da #103) |

Os dois handlers têm o mesmo nome de classe em apps diferentes, que é o padrão das
cópias de `TokenService` e `OperatorTokenAuthenticationHandler`.

- *Descartado, extrair para `libs/`:* registrado como alternativa da #117, que vai
  redesenhar a assinatura (chave por serviço ou assimétrica); extrair agora seria
  extrair o mecanismo que está para mudar (D1 da #103).
- *Descartado, assinar no comando, à mão:* o token sairia do fluxo do
  `HttpClient`, e um segundo consumidor do mesmo cliente teria de lembrar de
  assinar.
- *Descartado, reaproveitar o token do operador da requisição:* o
  `apps/connectors` recusa `operator` na rota de descrição (D1 da #103), e com
  razão: quem valida a pasta é o `apps/api`.

### D5. Pasta em uso: `409` com a base que a usa, por consulta antes e pelo índice depois

**Resposta:** `409` em `ProblemDetails`, com `code: "folder-in-use"`,
`knowledgeBaseId` e `knowledgeBaseName` como extensões, e um `detail` em
português que nomeia a base e diz que **a pasta continua ocupada mesmo com a base
inativa**. Exemplo: `A pasta já é usada pela base "Atendimento". Uma pasta
pertence a uma base só, e continua ocupada mesmo com a base inativa.`

**A mensagem não fala em excluir**, nem em "remover" ou "apagar": não existe rota
de exclusão de base até a #108. O teste afirma a ausência de `exclu`, `remov` e
`apag` no corpo inteiro (asserção negativa, convenção 13). Quando a #108 existir,
ela muda a mensagem e o teste.

**Dois pontos de detecção, com papéis diferentes:**

1. **Consulta antes da chamada externa.** Base `Synced` com o mesmo provedor e o
   mesmo id de pasta (comparado como veio, D5 da #102) → `409` sem chamar o
   `apps/connectors`. Responde `409` mesmo com o `apps/connectors` fora do ar ou
   não configurado, porque é fato do banco do `apps/api`, e poupa uma chamada ao
   Google.
2. **O índice, na gravação.** A consulta não fecha a corrida. O handler captura
   `DbUpdateException` com `PostgresException { SqlState: UniqueViolation,
   ConstraintName: "IX_knowledge_bases_SyncProvider_SyncFolderId" }`, desanexa a
   base `Added`, relê a vencedora por provedor e pasta e responde o mesmo `409`.
   Uma releitura basta: o Postgres só entrega a violação ao perdedor depois do
   commit do vencedor. Registra o evento de log **`1024`**
   (`ConcurrentSyncedKnowledgeBaseCreateRejected`).
3. Violação de **outro** índice não é capturada e segue o caminho de hoje.

**Como o teste sabe que viu a corrida:** o handler HTTP falso do `apps/connectors`
segura as duas descrições numa barreira até as duas chegarem. As duas requisições
passaram pela consulta (nenhuma base existia) antes de qualquer gravação, então
uma delas **precisa** cair no índice. O teste afirma exatamente um `201`, um
`409` com o nome da vencedora, nenhum `500`, uma base no banco, e o evento `1024`
contado uma vez. Sem a barreira, um laço que nunca intercala passaria igual.

- *Descartado, só o índice, sem consulta prévia:* toda tentativa com pasta em uso
  chamaria o Google, e com o `apps/connectors` fora do ar o operador receberia
  `503` em vez da informação útil de que a pasta já tem dono.
- *Descartado, só a consulta:* deixa a corrida virar `500` pelo índice.
- *Descartado, `SELECT ... FOR UPDATE` ou bloqueio consultivo:* não há linha para
  bloquear na inclusão; o índice já resolve (mesmo motivo da D10 da #102).
- *Descartado, mandar excluir a base antiga:* não há rota; a mensagem mandaria o
  operador procurar uma ação que não existe.

### D6. Forma validada no endpoint, antes de qualquer chamada, e a ordem das recusas

`CreateKnowledgeBaseRequest` ganha `Provider` e `FolderId`, strings anuláveis.
`folderName` e `folderUrl` não entram no request: o `System.Text.Json` os ignora,
como já ignora no `PUT` (D11 da #102).

| `contentMode` | `provider` | `folderId` |
|---|---|---|
| omitido ou `Manual` | **proibido**: qualquer valor não nulo, mesmo `""`, é `400`; `null` conta como ausente | **proibido**, idem |
| `Synced` | **obrigatório**, no formato de código (`^[a-z0-9]+(-[a-z0-9]+)*\z`, até 64) | **obrigatório**, não vazio nem só de espaços, até 256 caracteres, comparado e gravado como veio |
| outro valor | `400` em `contentMode`, como hoje | — |

- **`provider` no formato de código** porque vai no caminho da URL do
  `apps/connectors` e é a chave do conector (`google-drive`); o formato fecha a
  injeção de segmento (`../`) sem lista fechada de provedores.
- **`folderId` até 256** é teto contra abuso, não regra do Drive: os ids medidos
  nas respostas gravadas da etapa 0 (`~/.cache/buteco-agents/drive-0/out/`) têm
  33, 44 e 51 caracteres. Vai na query com `Uri.EscapeDataString`. Não é
  aparado nem normalizado, pela D5 da #102.
- **"Proibido em `Manual`" vale para todo valor não nulo, e `null` conta como
  ausente.** `"provider": ""` ou só de espaços numa base manual é `400`, porque o
  cliente achou que estava criando outra coisa. `"provider": null` é aceito: a
  desserialização do `System.Text.Json` num `record` com `string?` não distingue
  propriedade ausente de propriedade `null`, e o formulário da #106 pode mandar o
  campo nulo numa base manual. A spec diz o que a implementação consegue cumprir.
  *Descartado, distinguir ausente de `null`* (`JsonElement` ou um tipo
  "opcional" próprio no request): regra de fio diferente do resto dos requests
  da casa, para recusar um valor que não carrega intenção nenhuma.

**Ordem das recusas no cadastro `Synced`:**

1. forma (nome, descrição, tipo, provedor, pasta) → `400`, no endpoint;
2. pasta em uso, pela consulta → `409` (D5);
3. `Connectors:BaseUrl` ausente → `503` `connectors-not-configured` (D1);
4. chamada ao `apps/connectors` → `422`/`502`/`503` com código (D2, D3);
5. gravação → `201`, ou `409` pelo índice (D5).

Nenhum caminho de 1 a 4 grava nada.

- *Descartado, validar a forma no handler:* o `400` precisa vir antes da chamada
  externa, e a casa valida forma no endpoint (`ValidateShape`).
- *Descartado, ignorar `provider` e `folderId` em `Manual`:* criaria com `201` uma
  base diferente da pedida (o argumento da D11 da #102 contra ignorar `Synced`).
- *Descartado, aceitar a URL da pasta e extrair o id no `apps/api`:* formato de URL
  de provedor é conhecimento do conector; a #106 navega e envia o id.

### D7. Snapshots só da resposta, e o id gravado é o pedido, conferido contra o devolvido

`KnowledgeBase.CreateSynced(name, description, provider, folderId,
description.Name, description.WebUrl)`: nome e URL da pasta **sempre** da resposta
do `apps/connectors`. O id gravado é o `folderId` do pedido; a resposta precisa
trazer o mesmo id, comparado ordinalmente, senão é `502` `connectors-error` e nada
é gravado.

O teste envia `folderName` e `folderUrl` no corpo com valores diferentes dos que o
`apps/connectors` devolve, e afirma pelo banco que ficaram os do
`apps/connectors`, e que os do corpo não aparecem nem na resposta.

A base nasce com `syncState` de "nunca sincronizou" (instantes nulos,
`lastError: null`, `ignoredFiles: null`, D13 da #102). Nenhum evento, nenhuma
publicação na fila: base sem documento não indexa nada.

- *Descartado, gravar o id devolvido pelo `apps/connectors`:* se divergir, a base
  acompanharia uma pasta diferente da escolhida pelo operador, sem aviso.
- *Descartado, aceitar nome e URL do corpo como fallback:* é exatamente o que a
  issue descarta (o frontend não é fonte confiável).

### D8. Ida e volta: projeto novo em `tests/`, com o `apps/api` e o `apps/connectors` reais

`tests/ApiConnectorsRoundTrip.Tests`, no molde de
`tests/InboxOrchestratorRoundTrip.Tests`:

- `ProjectReference` para `Buteco.Api` e `Buteco.Connectors`, com `Aliases`
  (`ApiAssembly`, e `global,ConnectorsAssembly`), porque os dois têm `Program`
  público. O isolamento entre apps não muda: nenhum app referencia o projeto de
  volta. *Corrigido na implementação:* a primeira redação dava só alias próprio aos
  dois. O `FakeConnector.cs` linkado usa `Buteco.Connectors.Connectors` sem alias, e
  para compilar **sem mudar o arquivo** o assembly do `apps/connectors` precisa estar
  também no alias global; o do `apps/api` fica só no próprio, e é isso que evita a
  colisão dos dois `Program`.
- **Uma fixture, um contêiner:** Postgres `pgvector/pgvector:pg18` para o
  `apps/api`. O `apps/connectors` não tem banco. Sem RabbitMQ: o cadastro de base
  não publica nada. *Corrigido na implementação:* a primeira redação trocava também
  o publisher de indexação, como o `ApiFactoryFixture`; não foi preciso, porque
  nenhum caso da ida e volta cria documento, e o publisher só conecta quando publica.
- **A chamada é real, sem rede:** o handler primário do `HttpClient` nomeado do
  `apps/api` é o `TestServer` do `apps/connectors`
  (`ConnectorsFactory.Server.CreateHandler()`), como o `InboxOrchestratorRoundTrip`
  faz entre `apps/inbox` e `apps/api`. Passam pelo caminho real o
  `ServiceTokenDelegatingHandler` do `apps/api`, a autenticação e a tabela de
  subjects do `apps/connectors`, a rota de descrição e o formato de fio dos dois
  lados (convenção 11).
- **Mesma `Auth:TokenSigningKey` literal nos dois hosts.**
- **Conector falso do `apps/connectors`:** o arquivo
  `apps/connectors/tests/Buteco.Connectors.Tests/Support/FakeConnector.cs` entra no
  projeto por `<Compile Include=... Link=...>`, registrado com `AddConnector` (a
  extensão pública do `apps/connectors`) na chave `fake`. Nenhum Google: as
  credenciais do Google ficam ausentes, e o provedor `google-drive` não existe no
  host.

**Casos:**

| caso | o que prova |
|---|---|
| `POST` `Synced` com `provider: "fake"` | `201`, nome e URL do conector falso no banco; o `service:api` assinado pelo `apps/api` passou na tabela real do `apps/connectors` |
| conector falso lança `access-denied` com e-mail | `422`, `code` e `detail` atravessam os dois apps; nenhuma base |
| `provider: "google-drive"` (não configurado no host) | `422` `provider-not-configured` vindo do `apps/connectors` real |
| `apps/connectors` com chave de assinatura diferente (segundo host) | `502` `connectors-error` com `detail` `"401"`; prova que a assinatura é de fato conferida |

O sub do token é afirmado em dois lugares: no teste unitário do handler (D9),
lendo o `sub` do token capturado, e aqui pela consequência, já que a tabela do
`apps/connectors` só aceita `service:api` naquela rota.

**Classe nova com contêiner: autorizada pelo mantenedor em 03/10/2026**, na
revisão dos artefatos. É a única fonte de contêiner nova desta change.

- *Descartado, ida e volta sem banco (resolver o cliente do `apps/connectors` no DI
  do `apps/api` e chamá-lo direto, sem `POST`), que era o plano B:* sem contêiner,
  prova o contrato de fio e o token, mas não prova que o `POST` grava o que voltou.
  Descartado com a autorização da fonte de contêiner nova.
- *Descartado, acrescentar o caso ao `InboxOrchestratorRoundTrip.Tests`:* a
  fixture dele sobe dois Postgres, RabbitMQ e o `apps/workers` para nada, e o nome
  do projeto deixaria de dizer o que ele testa.
- *Descartado, referenciar o projeto `Buteco.Connectors.Tests`:* traria um projeto
  de teste inteiro como dependência, e as classes de teste dele seriam
  descobertas duas vezes.
- *Descartado, copiar o conector falso:* duas cópias divergem sem sintoma.

### D9. Onde os testes do `apps/api` moram

Nenhuma classe nova com contêiner no `apps/api`.

| arquivo | classe | contêiner | o quê |
|---|---|---|---|
| `Knowledge/KnowledgeBaseCatalogTests.SyncedCreation.cs` (novo, `partial`) | `KnowledgeBaseCatalogTests` (existe) | o da classe | cadastro `Synced` com sucesso e snapshots, `400` de forma, `409` (ativa, inativa, corrida), repasse de cada código, `apps/connectors` fora do ar e lento, sem configuração |
| `Knowledge/KnowledgeBaseCatalogTests.cs` | idem | idem | o cenário "Base sincronizada não é criada por esta rota" sai (requisito substituído) |
| `Support/FakeConnectorsHttpMessageHandler.cs` (novo) | apoio | — | responde por rota, segura chamadas numa barreira, captura o `Authorization` |
| `Support/ApiFactoryFixture.cs` | apoio | — | força `Connectors:BaseUrl` vazio |
| `KnowledgeSync/ConnectorsFolderClientTests.cs` (novo) | nova, **sem contêiner** | — | tabela da D2 e da D3 contra um `HttpClient` com handler falso, inclusive timeout com limite curto |
| `ServiceTokenDelegatingHandlerTests.cs` (novo) | nova, sem contêiner | — | `sub` igual a `service:api`, TTL de 5 min, token novo por requisição |
| `ConnectorsConfigurationStartupValidationTests.cs` (novo) | nova, sem contêiner | — | boot com valor inválido falha sem ecoar o valor; ausente e vazio sobem |
| `ServiceScopeAuthorizationTests.cs` | existe | o da classe | `service:api` recebe `403` nas rotas do `apps/api` |

Os testes do cadastro `Synced` derivam um host da fixture com
`WithWebHostBuilder`, pondo `Connectors:BaseUrl` e o handler falso no `HttpClient`
nomeado, o mesmo Postgres. É o molde do host derivado da D10 da #102. O caso
"lento" encurta o `Timeout` do cliente nesse host derivado
(`ConfigureHttpClient`), e um teste à parte afirma que o cliente registrado na
composição real tem 35 s.

A contagem de classes e fontes de contêiner é medida na abertura e no fim pelo
critério do `02`, com `git grep`, e não estimada aqui (memória do projeto: número
de suíte nunca sai de memória). O delta previsto é **zero** no `apps/api` e **+1**
fonte em `tests/` (D8), autorizada pelo mantenedor em 03/10/2026.

## Árvore de pastas proposta

```
apps/api/
├── src/Buteco.Api/
│   ├── Auth/
│   │   └── ServiceTokenDelegatingHandler.cs            (novo: service:api, D4)
│   ├── KnowledgeBases/
│   │   ├── Commands/CreateKnowledgeBase/
│   │   │   ├── CreateKnowledgeBaseCommand.cs           (alterado: provider e folderId opcionais)
│   │   │   ├── CreateKnowledgeBaseCommandHandler.cs    (alterado: consulta, chamada, gravação, corrida)
│   │   │   └── CreateKnowledgeBaseResult.cs            (novo: criada, pasta em uso, falha de validação)
│   │   ├── Endpoints/KnowledgeBaseEndpoints.cs         (alterado: forma, 409/422/502/503 com code)
│   │   └── Requests/CreateKnowledgeBaseRequest.cs      (alterado: Provider, FolderId)
│   ├── KnowledgeSync/
│   │   └── Connectors/                                 (novo)
│   │       ├── ConnectorsOptions.cs                    (Connectors:BaseUrl)
│   │       ├── ConnectorsConfigurationValidation.cs    (checagem de boot, D1)
│   │       ├── IConnectorsFolderClient.cs
│   │       ├── ConnectorsFolderClient.cs               (chamada, D2 e D3)
│   │       ├── ConnectorsFolderResult.cs               (pasta descrita, ou código, detalhe e status)
│   │       └── ConnectorsFailureCodes.cs               (connectors-not-configured, -unavailable, -error)
│   ├── Program.cs                                      (alterado: options, HttpClient nomeado, handler, checagem)
│   ├── appsettings.json                                (alterado: Connectors:BaseUrl vazio)
│   └── appsettings.Development.json                    (alterado: http://localhost:5037)
└── tests/Buteco.Api.Tests/
    ├── Knowledge/KnowledgeBaseCatalogTests.cs          (alterado: sai o cenário substituído)
    ├── Knowledge/KnowledgeBaseCatalogTests.SyncedCreation.cs  (novo, mesma classe)
    ├── KnowledgeSync/ConnectorsFolderClientTests.cs    (novo, sem contêiner)
    ├── ServiceTokenDelegatingHandlerTests.cs           (novo, sem contêiner)
    ├── ConnectorsConfigurationStartupValidationTests.cs (novo, sem contêiner)
    ├── ServiceScopeAuthorizationTests.cs               (alterado: service:api recebe 403)
    └── Support/
        ├── ApiFactoryFixture.cs                        (alterado: Connectors:BaseUrl vazio)
        └── FakeConnectorsHttpMessageHandler.cs         (novo)

tests/ApiConnectorsRoundTrip.Tests/                     (novo, D8; fonte de contêiner autorizada)
├── ApiConnectorsRoundTrip.Tests.csproj
├── FolderValidationRoundTripTests.cs
└── Support/RoundTripFixture.cs

apps/connectors/                                        (sem mudança)
```

Nada em `libs/`. Nenhum `ProjectReference` entre apps.

Documentação: `docs/configuration.md` (seção do `apps/api`), `.env.example`,
`docs/architecture.md` (o `apps/api` como cliente do `apps/connectors` e quem
assina `service:api`), `01` (Autenticação e Contrato de conector), `02`,
`CHANGELOG.md`, e a lista de verificações do `CONTRIBUTING.md` com o projeto novo
de `tests/`.

## Risks / Trade-offs

- **[Os 35 s dependem de dois números de outros lugares]** (30 s do
  `apps/connectors`, 60 s do nginx) → Mudar um sem os outros desfaz a D2 em
  silêncio. Mitigação: o comentário do limite nomeia os dois e os arquivos; a
  `docs/architecture.md` registra a cadeia. Verificável: teste que afirma 35 s no
  cliente da composição real. **Não testável** de forma automática a relação com
  o nginx, que não sobe na suíte; fica no comentário e na documentação.
- **[Corrida de dois cadastros da mesma pasta vira `500`]** → D5: captura pelo
  `ConstraintName`, releitura e `409`. Verificável: o teste com barreira e o
  evento `1024`.
- **[Mensagem do `409` manda excluir uma base que não pode ser excluída]** →
  D5. Verificável: asserção negativa sobre o corpo inteiro.
- **[Base criada com snapshot do frontend]** → D7. Verificável: corpo com nome e
  URL falsos, afirmação pelo banco.
- **[`apps/connectors` fora do ar, lento ou com resposta inesperada cria base ou
  devolve `500`]** → D2. Verificável: um caso por linha da tabela da D2, cada um
  afirmando o status, o código e que nenhuma base foi gravada.
- **[Deploy do `apps/api` sem a variável derruba a produção]** → D1: opcional.
  Verificável: toda a suíte do `apps/api` roda sem a variável, mais o teste de
  boot com ausente e vazio.
- **[Valor presente e inválido derruba o boot]** → É o efeito pretendido
  (convenção 8). Verificável: teste de boot, que também afirma que o valor não
  aparece na mensagem.
- **[Código fora do formato do `apps/connectors` chega ao painel]** → D3: só
  passa código no formato; o resto é `connectors-error`. Verificável: caso com
  código `"Sem acesso"`.
- **[`401` do `apps/connectors` desloga o operador]** → D2: vira `502`.
  Verificável: caso `401` afirmando `502` e `connectors-error`, e o caso de chave
  divergente na ida e volta.
- **[`service:api` assinado pelo `apps/api` ganha acesso ao próprio `apps/api`]**
  → D4: fica fora da tabela. Verificável: teste em `ServiceScopeAuthorizationTests`.
- **[Mais um processo assina token de serviço]** (#117) → O `apps/api` já guarda
  a chave e assina o token do operador, que é o de maior poder; assinar
  `service:api` não amplia o que um comprometimento do `apps/api` dá.
  Justificativa de não ser testável: é propriedade do modelo de chave
  compartilhada, que a #117 trata.
- **[O id da pasta muda de dono depois do cadastro, ou a pasta perde o
  compartilhamento]** → Fora desta change: o ciclo da #105 grava `access-denied`
  no estado da base. O cadastro valida uma vez, no momento da criação.

## Migration Plan

Sem migração de banco. Deploy do `apps/api` como qualquer outro:

1. **Antes da #119**, produção sobe sem `Connectors__BaseUrl`; o cadastro `Synced`
   responde `503` `connectors-not-configured`, e o painel ainda não o oferece
   (#106). Nenhuma rota existente muda.
2. **Com a #119**, o compose passa a entregar `Connectors__BaseUrl=http://connectors:8080`
   ao `apps/api` (porta interna dos apps no stack), registrada na #119 por
   comentário desta change.

**Rollback:** reverter o código volta o `400` da #102 para `Synced`. As bases
`Synced` criadas no intervalo continuam válidas no banco (o esquema é da #102) e
não dependem desta change para serem lidas.

## Open Questions

Nenhuma. A fonte de contêiner nova de `tests/ApiConnectorsRoundTrip.Tests` foi
autorizada pelo mantenedor em 03/10/2026 (D8).
