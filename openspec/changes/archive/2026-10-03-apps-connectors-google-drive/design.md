## Context

A linha de bases sincronizadas tem quatro peças. O catálogo do `apps/api` já existe
(#102, arquivada em `openspec/changes/archive/2026-10-03-catalogo-base-sincronizada/`).
Esta change é a segunda: o app que fala com o provedor. A criação de base pela rota
do operador (#104), o ciclo (#105) e as telas (#106, #107) vêm depois e consomem o
que esta change entrega.

O que foi lido antes de propor, e que muda decisões:

- **O molde de app satélite é o `apps/inbox`** (`apps/inbox/src/Buteco.Inbox/Program.cs`):
  esquema único `OperatorTokenAuthenticationHandler`, `TokenService` duplicado do
  `apps/api` sem `libs/`, `RouteAuthenticationExtensions.ValidateRouteAuthenticationClassification`
  sobre o host construído, adapters por `AddKeyedSingleton` e
  `ValidateChannelAdapterRegistrations` sobre a `IServiceCollection`, antes do
  `Build()`. **O defeito da #116 também está no molde:** a `FallbackPolicy` do
  `apps/inbox` é só `RequireAuthenticatedUser()` (`Program.cs:112-113`), e qualquer
  subject validamente assinado tem acesso de operador. O app novo copia a
  estrutura e **não** copia essa política.
- **A tabela de subjects do `apps/api`** (`apps/api/src/Buteco.Api/Auth/ServiceScopeAuthorizationHandler.cs`)
  e a checagem `ServiceScopeRouteValidation.ValidateServiceScopeRoutes` são o molde
  da autorização. Lá, `operator` passa em tudo. Aqui não pode ser assim (D1).
- **O teto de 1 MiB** é `KnowledgeDocumentLimits.MaxContentBytes`
  (`apps/api/src/Buteco.Api/KnowledgeDocuments/Options/KnowledgeDocumentLimits.cs:23`),
  medido em bytes UTF-8 sobre o texto **já extraído**
  (`KnowledgeContentProcessor.cs:43-48`). A recusa da rota de upsert de `/sync`
  sai como `400 ValidationProblem` com a chave `content` e uma **frase**
  (`KnowledgeSyncEndpoints.cs:118`), a mesma chave de outras falhas de extração.
  Ver D6 e a #120.
- **`/providers` já é prefixo do `apps/api`** (`GET /providers`, provedores de LLM)
  e está no bloco do `apps/api` em `apps/frontend/deploy/nginx.conf`. As rotas do
  conector não podem usar esse prefixo (D9).
- **Rotas de página da SPA** (`apps/frontend/src/app/routes.tsx`): `login`,
  `inventory`, `insights`, `agents`, `mcp-servers`, `knowledge-bases`, `channels`.
  Nenhuma é `connectors`.
- **Documentação do Google lida em 03/10/2026**, convenção 6:
  - erros da Drive API (`developers.google.com/workspace/drive/api/guides/handle-errors`,
    atualizada em 03/09/2026): corpo `{"error":{"code","errors":[{"domain","reason","message"}],"message"}}`;
    `404 notFound` ("File not found", sem acesso **ou** inexistente),
    `403 insufficientFilePermissions`, `403 userRateLimitExceeded`,
    `403 rateLimitExceeded`, `429 rateLimitExceeded`, `401 authError`;
  - OAuth de service account sem biblioteca
    (`developers.google.com/identity/protocols/oauth2/service-account`, atualizada
    em 23/03/2026): JWT `RS256` com `iss`, `scope`, `aud =
    https://oauth2.googleapis.com/token`, `iat`, `exp` (no máximo 1 h), trocado por
    `access_token` com `grant_type=urn:ietf:params:oauth:grant-type:jwt-bearer`;
  - NuGet, consultado na API do nuget.org: `Google.Apis.Auth` **1.77.0**
    (28/09/2026) depende de `Google.Apis` e `Google.Apis.Core` 1.77.0 e de
    `System.Management` 7.0.2; `Google.Apis.Core` depende de `Newtonsoft.Json`
    13.0.4; `Google.Apis.Drive.v3` **1.77.0.4276**.

### Divergências entre o corpo da #103 e os comentários, e como ficam

| ponto | corpo da #103 | o que vale |
|---|---|---|
| exportação de Google Doc | "exportação de Google Docs como `text/markdown`", entrega direta | comentário da etapa 0: as imagens vêm embutidas em base64 (99,8% dos bytes com uma imagem) e são **retiradas** antes da entrega (D4) |
| atalhos | "atalhos ignorados" | comentário: reconhecidos pelo `mimeType` e ignorados **sem resolver o destino** (D4) |
| tipo suportado | não dizia | comentário: por `mimeType`, nunca por extensão (D4) |
| erros `403` | não dizia | comentário: distinguidos pelo `reason` (D5) |
| `canDownload=false` | não dizia | comentário: ignorado com `download-blocked`, sem exportar (D4) |
| intervalo do ciclo | "Intervalo do ciclo de sincronização definido no `design.md`" | o ciclo é da #105 e está fora desta change; o comentário da etapa 0 na **#105** diz que "o `design.md` fixa o intervalo (a rodada sugere 5 minutos)". **Fica para o `design.md` da #105**, que é quem roda o ciclo |
| `VITE_CONNECTORS_BASE_URL` | obrigatória no build do frontend | entra com o primeiro consumidor, #106 (D8) |
| `docker-compose.prod.yml` e nginx | no escopo | **adiados para a #119** por decisão do mantenedor (D10); o `Dockerfile` e a imagem ficam aqui |

## Goals / Non-Goals

**Goals:**

- App `apps/connectors` no molde do `apps/inbox`, sem banco, com autorização por
  tabela de subjects desde o primeiro commit.
- Contrato de conector com as quatro operações, registro por chave e checagem no
  boot.
- Conector Google Drive com o que a etapa 0 mediu, testado contra respostas
  gravadas, sem chamada real ao Google na suíte.
- Rotas para o frontend (provedores, navegação) e para o `apps/api` (descrição de
  pasta).
- Documentação de cinco apps.

**Non-Goals:**

- Criar base sincronizada (#104), rodar o ciclo ou chamar `/sync` (#105), telas
  (#106, #107).
- Assinar `service:connectors` para chamar o `apps/api`: só a #105 chama, e o
  `DelegatingHandler` entra com ela. Esta change não tem `Api__BaseUrl`.
- Assinar `service:api` no `apps/api`: entra na #104. Esta change só reserva o
  subject do lado de quem valida.
- Chave de assinatura por serviço (#117) e restrição de subject no `apps/inbox`
  (#116).
- Drive Compartilhado medido (#114), OneDrive ou qualquer segundo provedor.
- Implantação em produção: compose, `.env.prod.example`, nginx do stack e
  `deployment.md` (#119, D10).

## Decisions

### D1. Autorização por tabela explícita de subjects, sem "operador em tudo"

| subject | quem assina | rotas | como assina |
|---|---|---|---|
| `operator` | `apps/api`, no login | `GET /connectors/providers`, `GET /connectors/providers/{providerKey}/folders` | já existe |
| `service:api` | `apps/api`, a cada requisição de saída ao `apps/connectors` | `GET /connectors/providers/{providerKey}/folder` | na #104: `DelegatingHandler` próprio no `apps/api`, mesma `Auth:TokenSigningKey`, TTL fixo de 5 min, no molde de `apps/inbox/src/Buteco.Inbox/Auth/ServiceTokenDelegatingHandler.cs` |
| qualquer outro, inclusive `service:inbox` e `service:connectors` | — | nenhuma: `403` | — |

**A diferença para o `apps/api` é deliberada: aqui o operador não passa em tudo.**
No `apps/api`, `operator` em tudo é a regra porque toda rota de negócio é dele e as
de serviço são exceção. Aqui a descrição de pasta existe para o `apps/api` validar a
pasta na #104 com resposta confiável, e o frontend não precisa dela: a navegação já
devolve nome e URL de cada pasta. Dar a rota ao operador não quebra nada hoje, mas
deixa a superfície maior que o uso e tira do teste negativo o caso "operador em rota
de serviço", que é o que o prompt e a #116 pedem para o app nascer protegido.

**Forma:** `ConnectorsSubjectAuthorizationHandler` com um requisito na
`FallbackPolicy` (`RequireAuthenticatedUser()` **mais** o requisito), igual ao
`apps/api`. A tabela é um dicionário `subject → [(método, padrão)]`, casando por
`RoutePattern.RawText`. Subject ausente da tabela não autoriza nada.

**Checagem de boot nos dois sentidos (convenção 8):** sobre o host construído,
depois dos `Map*`:

- toda entrada da tabela corresponde a um endpoint mapeado (o sentido da D6 da
  #102, que pegou a barra final de `MapGroup` na primeira execução);
- **todo endpoint autenticado mapeado aparece em pelo menos uma lista.** No
  `apps/api` esse sentido não precisa existir, porque o operador cobre a rota
  esquecida. Aqui uma rota nova fora da tabela daria `403` a todo mundo, e a
  checagem a pega no boot em vez de no primeiro uso.

Rotas anônimas (`/health`) ficam fora dos dois sentidos e são conferidas por
`ValidateRouteAuthenticationClassification`, copiado do `apps/inbox`.

**Testes:** matriz completa de subject × rota com token validamente assinado
(`operator`, `service:api`, `service:inbox`, `service:connectors`, `qualquer`),
afirmando `403` em toda célula fora da tabela; teste da extensão contra tabela com
entrada inexistente e contra endpoint sem dono; teste da composição real (o host
da `WebApplicationFactory` sobe e a checagem passou).

- *Descartado, `operator` em tudo, como no `apps/api`:* a rota de descrição ficaria
  aberta ao operador sem consumidor, e o caso negativo pedido ("o operador não
  acessa rota exclusiva do `apps/api`") deixaria de existir.
- *Descartado, `FallbackPolicy` só com `RequireAuthenticatedUser()`, como o
  `apps/inbox`:* é o defeito da #116.
- *Descartado, subject `service:api` sem rota nesta change (deixar para a #104):*
  a #104 mexeria na tabela de autorização de outro app só para acrescentar uma
  linha, e a rota de descrição nasceria sem nenhum subject autorizado, ou seja,
  `403` para todo mundo. A checagem do segundo sentido reprovaria o boot.
- *Descartado, extrair `TokenService` para `libs/` agora que há três cópias:*
  a convenção 2 pede dois ou três consumidores, e eles existem. Mas a extração
  mexeria em `apps/api` e `apps/inbox`, fora do escopo, e a #117 vai redesenhar a
  assinatura (chave por serviço ou assimétrica): extrair agora seria extrair o
  mecanismo que está para mudar. A terceira cópia carrega o comentário de
  espelhamento das outras duas, e a extração fica registrada como alternativa da
  #117.

### D2. Google por REST direto, sem biblioteca do Google

O conector chama a Drive API v3 com `HttpClient` e obtém o `access_token` por um
JWT `RS256` assinado com `System.Security.Cryptography.RSA` (`ImportFromPem` da
`private_key` da chave), trocado em `https://oauth2.googleapis.com/token`. O token é
guardado em memória até 5 minutos antes do `expires_in`, sob um `SemaphoreSlim`
para não trocar duas vezes em paralelo. Escopo fixo
`https://www.googleapis.com/auth/drive.readonly`, o mesmo da etapa 0.

Superfície: cinco chamadas (`files.get` de metadado, `files.list`, `drives.list`,
`files.export`, `files.get?alt=media`) mais a troca de token.

| | REST direto | `Google.Apis.Drive.v3` 1.77.0.4276 | só `Google.Apis.Auth` 1.77.0 + REST |
|---|---|---|---|
| pacotes novos | nenhum | `Drive.v3`, `Google.Apis`, `Google.Apis.Core`, `Google.Apis.Auth`, `Newtonsoft.Json` 13.0.4, `System.Management` 7.0.2 | os mesmos menos `Drive.v3` |
| código próprio | JWT (cabeçalho, claims, assinatura, base64url), cache do token, cinco chamadas, leitura do corpo de erro | configuração do serviço e do `HttpClientFactory` próprio da biblioteca para o teste | cinco chamadas e leitura do erro |
| teste com resposta gravada | `HttpMessageHandler` falso direto no `HttpClient` nomeado | handler injetado pelo `HttpClientFactory` da biblioteca, que não é o do ASP.NET | igual ao REST |
| erro por `reason` | lido do JSON | `GoogleApiException.Error.Errors[0].Reason` | lido do JSON |

**Escolhido REST direto** porque a superfície é pequena e fixa, a casa já assina
token sem biblioteca (HMAC, `TokenService`), e o teste contra resposta gravada é
o mesmo `HttpMessageHandler` que o `apps/inbox` já usa para fingir o `apps/api`
(`FakeAgentApiHttpMessageHandler`). O custo real é ser dono do código que toca a
chave privada: ele fica num tipo só (`GoogleServiceAccountTokenSource`), com teste
que valida a assinatura do JWT gerado com a chave pública do par de teste.

**Limites de tempo, corrigidos na implementação com a causa.** A primeira redação
usava um timeout fixo único de 30 s, "folgado para exportação". Medido contra o Drive
real (tarefa 10.4): exportar o Google Doc convertido do `02` (849.530 B de markdown)
levou 16,8 s, 17,0 s, 26,1 s, 31,1 s e, numa tentativa com o limite de 30 s,
estourou. Ficam dois limites fixos (convenção 2), aplicados por chamada no
`GoogleDriveClient`: **30 s** para leitura de metadado, listagem e troca de token,
porque a rota de navegação não pode esperar minutos; **120 s** para exportação e
download, quatro vezes o maior tempo observado. O limite documentado da exportação
(10 MB) não foi medido.

- *Descartado, subir o limite único para 120 s:* a navegação do operador esperaria
  dois minutos por um Google travado.

- *Descartado, biblioteca completa:* seis pacotes para cinco chamadas, entre eles
  `Newtonsoft.Json` (a casa usa `System.Text.Json`) e `System.Management`, que é
  Windows; e o teste passa pelo `HttpClientFactory` da própria biblioteca.
- *Descartado, só `Google.Apis.Auth`:* tira de nós o JWT, mas traz quatro pacotes
  pelo trecho de 40 linhas que é mais simples de verificar lendo do que de
  configurar.

### D3. Credencial em base64 numa variável de ambiente, e nada além do e-mail sai do processo

`GoogleDrive__ServiceAccountKeyBase64`: o arquivo JSON da chave, como o Google o
entrega, em base64 numa linha (`base64 -i chave.json | tr -d '\n'`).

- **Ausente ou vazia:** o provedor `google-drive` não é registrado, não aparece em
  `GET /connectors/providers` e as rotas com essa chave respondem `404`
  `provider-not-configured`. O app sobe. É o mesmo raciocínio dos provedores de
  LLM do `apps/api` (`GET /providers` só lista os que têm chave).
- **Presente e inválida** (base64 inválido, JSON inválido, `type` diferente de
  `service_account`, `client_email` ou `private_key` ausente, PEM que não importa):
  **o boot falha**, com mensagem que diz qual verificação falhou e **nunca** inclui
  o conteúdo. A checagem existe porque a alternativa é subir e falhar na primeira
  navegação do operador.
- **O que sai do processo:** só o `client_email`, na rota de provedores. A
  `private_key`, o `private_key_id` e o JSON inteiro ficam num tipo próprio
  (`GoogleServiceAccountKey`) sem `ToString` útil e que nunca é serializado em
  resposta. O `access_token` também não aparece em log.

**Como se prova:** o teste gera um par RSA por execução, monta a chave, roda as
rotas em sucesso e em falha (inclusive boot com chave inválida e troca de token
recusada pelo Google falso) com captura de log, e procura na saída capturada
(logs, corpos de resposta e mensagens de exceção) um trecho do corpo do PEM, o
valor base64 da variável e o `private_key_id`. Nenhum pode aparecer.

- *Descartado, JSON cru na variável:* o JSON do Google é multilinha e cheio de
  aspas, e o `.env.prod` passa por interpolação do Compose; o valor chegaria
  truncado ou com escape diferente, e o defeito apareceria como "PEM inválido".
- *Descartado, arquivo montado (`..._PATH`):* o stack não monta segredo como
  arquivo em lugar nenhum hoje; exigiria volume, permissão para o usuário `app`
  da imagem e um segundo mecanismo de segredo para documentar.
- *Descartado, OAuth do operador:* já descartado na #103 (callback, refresh token
  e verificação do app pelo Google).

### D4. Exportação e listagem, pelo resultado da etapa 0

**Tipo pelo `mimeType`, nunca pela extensão:**

| `mimeType` | o que o conector faz | código se ignorado |
|---|---|---|
| `application/vnd.google-apps.document` | exporta `text/markdown` e retira imagens embutidas | — |
| `text/x-markdown` (medido), `text/markdown` (não observado; tipo registrado do markdown) | baixa com `alt=media`, sem transformar | — |
| `application/vnd.google-apps.shortcut` | ignora sem ler `shortcutDetails` nem tocar o destino | `shortcut-not-followed` |
| `application/vnd.google-apps.folder` | ignora (só a raiz, #105) | `subfolder-not-synced` |
| qualquer outro | ignora | `unsupported-type`, detalhe = o `mimeType` |

**`canDownload=false`** (vem em `capabilities.canDownload` na listagem, pedido no
`fields`): arquivo de tipo suportado vira ignorado com `download-blocked`, sem
tentar exportar. A ordem é: atalho e subpasta primeiro (pela forma do item), depois
tipo, depois `canDownload`. Atalho também vem com `canDownload=false` (medido), e o
código certo para ele é `shortcut-not-followed`, não `download-blocked`.

**Imagens:** a exportação traz `![][imageN]` no corpo e `[imageN]: <data:image/…;base64,…>`
no fim (medido). O conector retira as definições de referência cujo destino é
`data:` (com ou sem `<…>`) e as referências de imagem que apontam para elas, e
também imagens inline `![…](data:…)`, que o CommonMark permite e a rodada não viu.
**No lugar de cada imagem não fica nada.**

- *Descartado, texto alternativo no lugar:* a rodada não tinha imagem com texto
  alternativo, então a forma em que a exportação o entrega não foi observada.
  Escrever parser para uma forma não vista é a convenção 6 ao contrário. Se o texto
  alternativo aparecer, ele é um acréscimo de uma linha à regra, com uma
  exportação real de exemplo.
- *Descartado, recusar o documento com imagem:* um Doc com uma imagem ficaria fora
  da base, quando o texto dele cabe.

**Escapes (`\_`, `\-`, `\=`, `\>`) e bloco de código sem cerca: não normaliza.**
Medido: 46 escapes em 5,7 KB, e na ida e volta do `02` de 4 para 713; exemplo
`def gerar\_arquivo\_texto\_1mb(caminho\_arquivo, tamanho\_mb=1.1):`, e o bloco de
código do Doc sai recuado e escapado, sem cerca. **O markdown renderiza certo** (o
escape mostra o caractere literal), então o painel exibe o texto fiel. O custo é na
busca: o índice recebe `gerar\_arquivo`.

- *Descartado, desescapar tudo:* `\*` e `\[` desescapados mudam o significado
  (ênfase, link), e o conector passaria a reescrever o texto do usuário por
  heurística.
- *Descartado, desescapar só `\_` entre caracteres de palavra* (seguro no
  CommonMark, porque `_` intrapalavra não abre ênfase): resolve o exemplo medido,
  mas o efeito na busca **não foi medido** (o tokenizador do lexical e o chunker
  podem já separar no `\`). A revisão fica na **#121** (`aguardando gatilho`:
  uma busca que falha num termo com `_` ou `-` presente num documento exportado
  do Google, observada num agente real).
- *Descartado, reconstruir cercas de código:* não há marcador na exportação que
  diga onde o bloco começa; seria adivinhar.

**`.md` baixado direto:** idêntico byte a byte ao original (medido). Decodificado
como UTF-8; BOM e quebras de linha ficam com o `apps/api`, que já os normaliza.

**Listagem completa ou erro.** `files.list` com `q = '<pasta>' in parents and
trashed = false`, todas as páginas. Qualquer página que falhe faz a operação
inteira falhar com o código do erro, **nunca** devolve lista parcial. A #105 exclui
documento por ausência, e o comentário da etapa 0 na #105 exige listagem completa.

**`ExternalVersion`** por arquivo suportado, como a D3 da #102: `modifiedTime` para
Google Doc, `md5Checksum` para `.md`.

### D5. Erros do Google pelo `reason`, mapeados para códigos da D1 da #102

O conector lê `error.errors[0].reason` do corpo; o status só desempata quando o
corpo não tem `reason`.

| resposta do Google | código | detalhe |
|---|---|---|
| `404 notFound` na pasta | `access-denied` | e-mail da conta |
| `403 insufficientFilePermissions` | `access-denied` | e-mail da conta |
| `403 accessNotConfigured` | `api-not-configured` | — |
| `403 userRateLimitExceeded`, `403 rateLimitExceeded`, `429 rateLimitExceeded` | `rate-limited` | — |
| `403 cannotExportFile` (na exportação) | `download-blocked` | — |
| `404 notFound` num arquivo (exportação ou download) | `file-not-found` | — |
| `401` de qualquer chamada, ou `400`/`401` da troca de token | `provider-auth-failed` | — |
| `5xx`, timeout, falha de rede | `provider-unavailable` | — |
| qualquer outro `4xx` | `provider-error` | o `reason`, se vier e casar com o formato |
| pasta que existe e não é pasta | `not-a-folder` | — |
| pasta com `trashed: true` | `folder-trashed` | — |

**`404 notFound` é "sem acesso" na pasta.** A documentação diz que é a resposta para
"no read access or file doesn't exist", e a service account não tem como distinguir
os dois. A #106 e a #107 precisam do e-mail para instruir o compartilhamento, e é o
caso real mais comum.

**Pasta sem acesso responde erro, nunca lista vazia.** `files.list` com
`'<pasta>' in parents` numa pasta inacessível devolve `200` com lista vazia, porque
é uma consulta, não uma leitura da pasta. Por isso **toda** operação sobre uma pasta
(navegar dentro dela, descrevê-la, listar a raiz) começa por `files.get` da pasta, e
só lista se ele responder.

Todo código casa `^[a-z0-9]+(-[a-z0-9]+)*\z`, até 64 caracteres, o formato que o
`apps/api` aceita (`SyncCode`). O detalhe de `provider-error` só leva o `reason` se
ele passar no mesmo formato depois de convertido de camelCase para kebab-case;
senão vai nulo. Nada do corpo do Google vai para o detalhe além disso.

- *Descartado, mapear pelo status:* `accessNotConfigured`, `insufficientFilePermissions`,
  cota e `cannotExportFile` são todos `403` e pedem ações opostas (ativar a API,
  compartilhar a pasta, esperar, liberar download).
- *Descartado, devolver a mensagem do Google como detalhe:* é texto em inglês,
  muda sem aviso e às vezes traz o nome do projeto do Google Cloud.

### D6. O conector não mede o teto de 1 MiB; o `apps/api` é a autoridade única

O conector entrega o markdown sem imagens e não compara com 1 MiB. Quem recusa é o
upsert do `apps/api`, que mede depois de normalizar (BOM, quebras de linha), sobre
o mesmo texto que grava.

- *Descartado, o conector medir também:* duas cópias de 1.048.576 em apps
  diferentes, sem `ProjectReference` possível, divergem sem sintoma; e mediriam
  textos diferentes (antes e depois da normalização do `apps/api`). Amarrar as
  duas exigiria um teste cruzado em `tests/` só para conferir uma constante.

**Achado para a #105, lido no código e não corrigido aqui:** a recusa por tamanho
no upsert de `/sync` sai como `400` com a chave `content` e uma frase
(`KnowledgeSyncEndpoints.cs:118`, a partir de `KnowledgeContentProcessor.cs:46-47`),
a mesma forma de outras recusas de conteúdo. A #105 vai precisar gravar `too-large`
na lista de ignorados e, com a resposta de hoje, só conseguiria lendo a frase. É
contrato do `apps/api` (convenção 12: defeito de formato se corrige em quem expõe).
**Registrado na #120**, em `Ready` logo acima da #105, que fica `blocked-by` ela.

### D7. Parâmetros de Drive Compartilhado em toda chamada que os aceita

`supportsAllDrives=true` em todo `files.get`, inclusive o download com `alt=media`;
`supportsAllDrives=true&includeItemsFromAllDrives=true` em todo `files.list`. O
`files.export` leva **só** `mimeType`, e `drives.list` não leva nenhum dos dois.

**Corrigido na implementação, com a causa:** a primeira redação mandava
`supportsAllDrives=true` também no `files.export`. A referência oficial do método
(https://developers.google.com/workspace/drive/api/reference/rest/v3/files/export,
consultada em 03/10/2026) lista `mimeType` como único parâmetro de query; o script
da etapa 0 também exportava sem ele. Pela convenção 6 vale a fonte, sem esperar
medição: um parâmetro inexistente que o Google tolere hoje pode ser recusado
amanhã. Decisão do mantenedor em 03/10/2026, com o requisito da spec
`google-drive-connector` alterado junto.
Medido na etapa 0: em pasta de Meu Drive os dois não mudam o resultado. **O
`corpora` não é passado**, porque nenhum valor dele foi medido. O acesso real em
Drive Compartilhado continua **não medido** (#114).

**Navegação, também não medida na etapa 0:** o nível de cima é a união de
`drives.list` (Drives Compartilhados de que a conta é membro) e `files.list` com
`sharedWithMe = true and mimeType = 'application/vnd.google-apps.folder' and
trashed = false` (pastas compartilhadas diretamente). Dentro de uma pasta ou drive,
`files.list` com `'<id>' in parents` e o `mimeType` de pasta. As três consultas são
documentadas, e a primeira redação as marcava como não observadas para service
account. **Medido na tarefa 10.4 (03/10/2026):** sem Drive Compartilhado, o
`drives.list` volta vazio e o nível de cima são as duas pastas compartilhadas
diretamente com a conta, vindas do `sharedWithMe`, ambas como `Folder`; dentro da
pasta principal, a navegação devolve só a subpasta. Drive Compartilhado continua não
medido (#114).

- *Descartado, passar os parâmetros só quando a pasta é de Drive Compartilhado:*
  exigiria saber de antemão onde a pasta está, e a etapa 0 mediu que eles não
  atrapalham no outro caso.

### D8. `VITE_CONNECTORS_BASE_URL` entra com o primeiro consumidor (#106)

A variável não entra nesta change. O `apps/frontend/Dockerfile` falha o build
quando um `VITE_*` declarado não é passado (`RUN test -n "${VITE_INBOX_BASE_URL+x}"`).
Declará-la agora tornaria obrigatório no build um valor que nenhum código lê, e o
build falharia por uma variável sem uso. É a convenção 25: declaração viva sem
consumidor.

- *Descartado, declarar agora como o corpo da #103 pede:* nenhum `request<T>` do
  frontend chama o `apps/connectors` nesta change.
- *Descartado, declarar agora como opcional:* mudaria a regra de todos os `VITE_*`
  de base URL só para esta, e a #106 teria de torná-la obrigatória de novo.

**Tarefa da #106, registrada aqui para não se perder:** variável no `Dockerfile` do
frontend, no `docker-compose.prod.yml` (`""`), no `vite-env.d.ts`,
`.env.example` do frontend e `configuration.md`.

### D9. Prefixo `/connectors` em todas as rotas

| método e rota | subject | o que faz | respostas |
|---|---|---|---|
| `GET /connectors/providers` | `operator` | provedores configurados, por chave | `200` |
| `GET /connectors/providers/{providerKey}/folders?parentId=` | `operator` | sem `parentId`: Drives Compartilhados e pastas compartilhadas com a conta; com `parentId`: subpastas | `200`, `404`, `422`, `502`, `503` |
| `GET /connectors/providers/{providerKey}/folder?id=` | `service:api` | descreve a pasta: id, nome, URL web | `200`, `400`, `404`, `422`, `502`, `503` |

- **Um prefixo só, que não é rota da SPA nem do `apps/api`.** `/providers` já é do
  `apps/api`. `connectors` não é rota de página (`routes.tsx`), então o bloco do
  nginx dispensaria o desvio de `Sec-Fetch-Mode: navigate`. Quando ele entrar, na
  #119 (D10), entra com o desvio igual aos outros blocos, para que uma rota de
  página futura com esse nome não quebre.
- **Id de pasta na query, não no caminho**, pelo mesmo motivo da D8 da #102:
  identificador opaco de um conjunto aberto de provedores.
- **Formato de fio (convenção 12):** `camelCase`, enum como string.
  - provedor: `{ "key": "google-drive", "accountEmail": "…" }`, ordenado por `key`;
  - pasta na navegação: `{ "id", "name", "kind": "SharedDrive" | "Folder", "webUrl" }`,
    ordenada por `name` com comparação ordinal e desempate por `id`;
  - pasta descrita: `{ "id", "name", "webUrl" }`;
  - erro: `ProblemDetails` com a extensão `code` (formato da D1) e `detail` com o
    detalhe da D5 quando houver; `title` em português.
- **Status por natureza, código como informação:**

  | status | códigos |
  |---|---|
  | `400` | parâmetro obrigatório ausente |
  | `403` | **só** autorização de subject (D1) |
  | `404` | `provider-not-configured` |
  | `422` | `access-denied`, `not-a-folder`, `folder-trashed` |
  | `502` | `api-not-configured`, `provider-auth-failed`, `provider-error` |
  | `503` | `rate-limited`, `provider-unavailable` |

  O `403` fica reservado à autorização do subject para não repetir a ambiguidade
  que a etapa 0 achou no Google, onde quatro situações diferentes voltam `403`.

- *Descartado, prefixo por provedor (`/drive/...`):* um prefixo novo no nginx a cada
  provedor.
- *Descartado, devolver `403` para pasta sem acesso:* o frontend não distinguiria
  "a conta não vê a pasta" de "o seu token não vale aqui".

### D10. Implantação em produção adiada para a #119

**Decisão do mantenedor, na revisão dos artefatos (03/10/2026): esta change não
implanta o app.** Saem dela o serviço no `docker-compose.prod.yml`, o
`.env.prod.example`, o prefixo `/connectors` no nginx do stack, a fonte única de
`Auth__TokenSigningKey` para o terceiro processo, o `deployment.md` e a verificação
pós-deploy. Tudo isso, com os requisitos correspondentes de `server-deployment`,
está transcrito na **#119** (`blocked-by` #103 e #116). Ficam aqui o `Dockerfile` do
app e o build da imagem `buteco-connectors`.

**Motivo:** hoje há um piloto em produção. Pôr `connectors` no compose de produção
faz o **próximo deploy** depois do merge (qualquer que seja o motivo dele, porque o
deploy é manual) subir um terceiro processo com `Auth__TokenSigningKey`, que é o
gatilho declarado da **#117**. E nada em produção consome o app antes da #104
(validação de pasta) e da #106 (seletor). Disparar o gatilho sem consumidor é
custo de segurança sem uso. Antes do primeiro deploy, a #119 pede ao mantenedor a
decisão sobre a #117: resolver, ou aceitar o risco com registro.

**Consequência nos artefatos:** o delta de `server-deployment` desta change só
modifica o requisito do `Dockerfile` por app. `docs/configuration.md` e
`docs/development.md` documentam o app em desenvolvimento; nada de produção.

- *Descartado, implantar junto com a #103:* dispararia o gatilho da #117 sem
  nenhum consumidor em produção.

### D11. Testes sem chamada real ao Google

- **`GoogleDriveFakeHandler`** (`HttpMessageHandler`) responde por método e URL com
  corpos gravados. Os corpos saem de `~/.cache/buteco-agents/drive-0/out/*.json` e
  `out/export/*.md`, **sanitizados**: e-mails, ids de pasta e de arquivo e nomes
  trocados por valores sintéticos, e o base64 da imagem encurtado para um PNG
  mínimo válido. Os arquivos gravados da etapa 0 têm e-mail de conta pessoal e não
  entram no repositório como estão.
- Casos medidos que viram teste: listagem da P3 (Docs, `.md`, planilha, atalho com
  `canDownload=false`, subpasta), exportação com imagem (P1), exportação com
  escapes e sem imagem, download de `.md`, `403 accessNotConfigured` (P2),
  `403 cannotExportFile` (acréscimo à P1). Casos documentados e não medidos:
  `404 notFound`, `403 insufficientFilePermissions`, `403 userRateLimitExceeded`,
  `429`, `401`, troca de token recusada.
- **Mesmo status, `reason` diferente, código diferente:** os quatro `403` lado a
  lado num `[Theory]`.
- **Verificação contra o Drive real é passo manual** (convenção 14, pela mesma
  razão: a suíte não alcança o comportamento): nas pastas de teste da etapa 0,
  com uma chave real fora do repositório, `GET /connectors/providers`, navegação
  pelo nível de cima e por uma pasta, descrição da pasta principal e de uma pasta
  não compartilhada. Listar a raiz e entregar o markdown não têm rota nesta change,
  então rodam por um teste `[Trait("Category", "Manual")]` que só executa com
  `GoogleDrive__ServiceAccountKeyBase64` e `CONNECTORS_MANUAL_FOLDER_ID` no
  ambiente, e é pulado na suíte normal. O resultado vai para o `02`, e o que
  divergir corrige o `design.md`.

### D12. Tamanho da change: uma change só

A pergunta é se a revisão cabe numa sessão. Contando unidades, não linhas
(convenção 18):

| parte | o que tem | custo de revisão |
|---|---|---|
| esqueleto | `Program.cs`, autenticação copiada do `apps/inbox`, classificação de rotas anônimas, CORS, `Dockerfile`, solução | baixo: é cópia de molde conhecido, e o que importa ler é a diferença (D1) |
| autorização | tabela, handler, checagem de boot nos dois sentidos, matriz de testes | médio |
| contrato e conector falso | dois contratos, registro, checagem de boot | baixo |
| conector Google | token, cinco chamadas, mapeamento de erros, imagens, listagem completa | **alto**: é onde está o risco (chave privada, regras da etapa 0) |
| rotas | três | baixo |
| documentação | cinco apps em seis documentos | médio, mecânico |

A divisão sugerida (esqueleto, contrato e falso numa change; Google noutra) tiraria
da primeira só a parte de custo alto, e a primeira entregaria um app cujo único
conector é de teste: a rota de provedores voltaria sempre vazia, e a documentação
descreveria um quinto app que não faz nada. A revisão mais cara continuaria
inteira na segunda. **Uma change só**, com as tarefas agrupadas para revisão por
parte.

- *Descartado, dividir:* dois ciclos de archive e PR sem reduzir a revisão da parte
  que importa.

## Árvore de pastas proposta

```
apps/connectors/
├── Connectors.sln
├── Dockerfile
├── src/Buteco.Connectors/
│   ├── Buteco.Connectors.csproj
│   ├── Program.cs
│   ├── appsettings.json
│   ├── appsettings.Development.json
│   ├── appsettings.Production.json
│   ├── Properties/launchSettings.json             (porta 5037)
│   ├── Auth/
│   │   ├── AnonymousRouteClassification.cs        (cópia do apps/inbox)
│   │   ├── ConnectorsSubjectAuthorizationHandler.cs  (tabela da D1)
│   │   ├── ConnectorsSubjectRouteValidation.cs    (boot, dois sentidos)
│   │   ├── ITokenService.cs                       (cópia)
│   │   ├── OperatorTokenAuthenticationHandler.cs  (cópia)
│   │   ├── RouteAuthenticationExtensions.cs       (cópia)
│   │   ├── TokenService.cs                        (cópia, comentário de espelhamento nos três)
│   │   ├── TokenSigningOptions.cs
│   │   └── TokenValidationResult.cs
│   ├── Options/CorsOptions.cs
│   ├── Connectors/
│   │   ├── IFolderNavigator.cs                    (navegar, descrever)
│   │   ├── IFolderContentSource.cs                (listar raiz, entregar markdown)
│   │   ├── ConnectorModels.cs                     (FolderEntry, FolderDescription, RootListing, RootFile, IgnoredFile, MarkdownResult)
│   │   ├── ConnectorFailure.cs                    (exceção com código e detalhe)
│   │   ├── ConnectorCodes.cs                      (constantes dos códigos e checagem de formato)
│   │   ├── IConnectorRegistry.cs
│   │   ├── ConnectorRegistry.cs                   (chaves configuradas, e-mail de cada uma)
│   │   ├── ConnectorRegistrationExtensions.cs     (ValidateConnectorRegistrations)
│   │   └── GoogleDrive/
│   │       ├── GoogleDriveOptions.cs
│   │       ├── GoogleServiceAccountKey.cs         (parse e validação, sem ToString)
│   │       ├── GoogleServiceAccountTokenSource.cs (JWT RS256 e cache)
│   │       ├── GoogleDriveClient.cs               (cinco chamadas e leitura de erro)
│   │       ├── GoogleDriveErrorMapper.cs          (D5)
│   │       ├── GoogleDriveMarkdown.cs             (retirada de imagens)
│   │       ├── GoogleDriveFolderNavigator.cs
│   │       ├── GoogleDriveFolderContentSource.cs
│   │       └── GoogleDriveRegistrationExtensions.cs
│   └── Endpoints/
│       ├── ConnectorEndpoints.cs
│       └── ConnectorResponses.cs
└── tests/Buteco.Connectors.Tests/
    ├── Buteco.Connectors.Tests.csproj
    ├── Support/
    │   ├── ConnectorsFactory.cs                   (WebApplicationFactory, conector falso, chave de teste)
    │   ├── FakeConnector.cs                       (implementa os dois contratos)
    │   ├── GoogleDriveFakeHandler.cs
    │   ├── TestServiceAccountKey.cs               (par RSA gerado por execução)
    │   ├── TestAuthentication.cs
    │   └── CapturingLoggerProvider.cs
    ├── Fixtures/GoogleDrive/                       (respostas gravadas sanitizadas)
    ├── SubjectAuthorizationTests.cs
    ├── SubjectRouteValidationTests.cs
    ├── RouteAuthenticationTests.cs
    ├── ConnectorRegistrationTests.cs
    ├── ConnectorEndpointsTests.cs
    ├── GoogleServiceAccountKeyTests.cs
    ├── GoogleServiceAccountTokenSourceTests.cs
    ├── GoogleDriveFolderNavigatorTests.cs
    ├── GoogleDriveFolderContentSourceTests.cs
    ├── GoogleDriveErrorMapperTests.cs
    ├── GoogleDriveMarkdownTests.cs
    ├── SecretLeakTests.cs
    └── HealthCheckTests.cs
```

Nada em `libs/`. Nenhum `ProjectReference` para fora de `apps/connectors`.

**Os dois contratos keyed** (chave `google-drive`):

- `IFolderNavigator`: `BrowseAsync(string? parentId)` e `DescribeFolderAsync(string folderId)`;
  consumido pelas rotas desta change.
- `IFolderContentSource`: `ListRootAsync(string folderId)` (arquivos suportados com
  `ExternalRef`, nome e `ExternalVersion`, e ignorados com código) e
  `GetMarkdownAsync(RootFile file)`; consumido pelo ciclo da #105.

A checagem `ValidateConnectorRegistrations` roda sobre a `IServiceCollection` antes
do `Build()` (descritores keyed, convenção 8): toda chave com um contrato tem o
outro, e o registro do e-mail da conta (`ConnectorAccount`) existe, uma vez só, para
toda chave. Mais o teste da composição real, que captura a coleção pelo
`ConfigureServices` da `WebApplicationFactory`.

**Divergência da implementação, com a causa:** a primeira redação dizia
`ConnectorAccount` keyed. Ele é um singleton **não keyed** que carrega a própria
chave (`ConnectorAccount(Key, Email)`), registrado como instância. Em tempo de
execução o contêiner não enumera as chaves dos serviços keyed, e a rota de provedores
precisa listá-las: `IEnumerable<ConnectorAccount>` é essa lista. A checagem de boot
continua conferindo os três registros por chave, lendo a chave da instância. Os
registros passam por `AddConnector(key, email, navegador, fonte)`, que faz os três.

O conector falso é registrado só pela fábrica de teste, com a chave `fake`. Na
composição de produção a única chave possível é `google-drive`, e só quando a
credencial está presente.

## Risks / Trade-offs

- **[Risco] `IFolderContentSource` nasce sem consumidor de produção (convenção 25).**
  Listar a raiz e entregar o markdown só são chamados pelo ciclo da #105; nesta
  change só os testes os chamam. → A #105 é a próxima da linha e já está aberta; a
  interface carrega no comentário o consumidor esperado e a issue. Contraparte
  verificável: a tarefa da #105 que chama `ListRootAsync`; até lá, o teste do
  contrato. Se a #105 for descartada, a interface sai.
- **[Risco] Navegação não medida para service account** (`drives.list`,
  `sharedWithMe`). → Verificação manual da D11, com resultado no `02`; divergência
  corrige o `design.md` antes do archive.
- **[Risco] Drive Compartilhado não medido** (#114). → Parâmetros sempre presentes
  (D7), e a #114 continua com o gatilho dela.
- **[Risco] Chave privada no código do app.** → Um tipo só a lê, nenhum
  `ToString`, teste de vazamento sobre logs, respostas e exceções (D3).
- **[Risco] Terceiro processo com a chave de assinatura** (#117). → D10: a
  implantação em produção saiu desta change para a #119, que pede a decisão
  sobre a #117 antes do primeiro deploy. Justificativa de não ser testável aqui:
  nesta change o app só roda em desenvolvimento e em teste.
- **[Risco] Recusa de tamanho sem código no `apps/api`** (D6). → #120, em `Ready`
  acima da #105, que fica `blocked-by` ela.
- **[Trade-off] Escapes ficam no texto indexado** (D4). → #121, `aguardando
  gatilho`.
- **[Trade-off] `404 notFound` vira `access-denied` mesmo para pasta inexistente**
  (D5). → A mensagem da #107 instrui o compartilhamento, que é o caso real; pasta
  excluída pelo dono e pasta não compartilhada são indistinguíveis para a conta.
- **[Trade-off] Autorização da descrição de pasta só para `service:api`** (D1). → Se
  a #106 precisar dela, a #106 acrescenta o operador à tabela, com teste.

## Migration Plan

Nada a migrar: app novo, sem banco e sem consumidor. Esta change não implanta nada
em produção (D10, #119).

## Open Questions

Nenhuma. A sequência da implantação em produção foi decidida (D10, #119), e a
recusa de tamanho com código virou a #120.

## Nota: cenário de arquivos protegidos corrigido no delta de `repository-documentation`

O requisito "Documentação de arquitetura e premissas oficiais" tinha o cenário
"Arquivos protegidos permanecem intocados": `01` e `02` "com o mesmo conteúdo, sem
edição". Ele vinha da change de documentação que criou `docs/`
(`documentacao-repositorio-open-source`), onde valia para aquela change, e foi
promovido a requisito permanente pelo archive. Como regra viva é falso: toda change
edita o `01` e o `02`, inclusive esta (tarefas 9.9 e 9.10). Esta change já reescreve
o requisito (cinco apps e a fronteira entre canais e conectores), então o cenário foi
corrigido aqui para a regra verdadeira: os dois arquivos ficam na raiz, sem
movimentação, renomeação ou divisão, e o fluxo para `docs/` é de sentido único (o
`01` é fonte, nunca destino).
