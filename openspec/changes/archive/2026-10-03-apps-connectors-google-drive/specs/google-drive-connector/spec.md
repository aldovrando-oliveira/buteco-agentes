## Purpose

Como o conector Google Drive se comporta, a partir do que a etapa 0 (#100) mediu:
a credencial da service account e o que dela pode sair do processo, a autenticação
por JWT, o tipo de cada arquivo decidido pelo `mimeType`, a exportação sem imagens
embutidas, os erros do Google distinguidos pelo `reason`, a pasta sem acesso como
erro e nunca como lista vazia, e os parâmetros de Drive Compartilhado.

## ADDED Requirements

### Requirement: Credencial da service account por variável de ambiente
O conector Google Drive SHALL ler a chave da service account de
`GoogleDrive:ServiceAccountKeyBase64`, que contém o arquivo JSON da chave em base64.
Ausente ou vazia, o provedor `google-drive` SHALL NOT ser registrado e o processo
SHALL subir. Presente e inválida (base64 inválido, JSON inválido, `type` diferente
de `service_account`, `client_email` ou `private_key` ausentes, chave privada que não
importa), o boot SHALL falhar com mensagem que identifica a verificação que falhou
sem incluir nenhum trecho do valor.

#### Scenario: Credencial ausente
- **WHEN** o processo sobe sem `GoogleDrive__ServiceAccountKeyBase64`
- **THEN** o boot completa e `GET /connectors/providers` responde `200` sem a chave
  `google-drive`

#### Scenario: Credencial com JSON sem chave privada
- **WHEN** o processo sobe com um JSON válido em base64 sem `private_key`
- **THEN** o boot falha com mensagem que cita `private_key`, e a mensagem não
  contém o valor da variável

#### Scenario: Credencial válida
- **WHEN** o processo sobe com uma chave válida
- **THEN** `GET /connectors/providers` lista `google-drive` com o `client_email` da
  chave

### Requirement: Nada da chave sai do processo além do e-mail
Nenhuma resposta HTTP, linha de log ou mensagem de exceção SHALL conter a chave
privada, o `private_key_id`, o valor base64 da variável ou o `access_token` obtido do
Google. O `client_email` SHALL ser o único dado da chave exposto, e só na rota de
provedores e no detalhe de `access-denied`.

#### Scenario: Busca da chave na saída dos testes
- **WHEN** a suíte exercita boot com chave válida e inválida, navegação com
  sucesso, troca de token recusada e erros do Google, capturando logs, corpos de
  resposta e mensagens de exceção
- **THEN** nenhum texto capturado contém um trecho do corpo do PEM, o
  `private_key_id`, o valor base64 da variável nem o `access_token` devolvido pelo
  Google falso

### Requirement: Autenticação no Google por JWT da service account
O conector SHALL obter o `access_token` trocando, em
`https://oauth2.googleapis.com/token`, um JWT `RS256` assinado com a chave privada,
com `iss` igual ao `client_email`, `scope` igual a
`https://www.googleapis.com/auth/drive.readonly`, `aud` igual ao endpoint de token e
`exp` de no máximo uma hora após `iat`. O token SHALL ser reaproveitado até perto do
fim da validade, e chamadas concorrentes SHALL NOT trocar o token mais de uma vez.

#### Scenario: JWT verificável com a chave pública
- **WHEN** o conector pede um token ao endpoint falso
- **THEN** o `assertion` enviado tem cabeçalho `RS256`, as quatro claims com os
  valores acima, e a assinatura confere com a chave pública do par de teste

#### Scenario: Token reaproveitado
- **WHEN** duas operações rodam em sequência dentro da validade do token
- **THEN** o endpoint de token recebe uma única troca

#### Scenario: Troca recusada
- **WHEN** o endpoint de token responde `400` com `invalid_grant`
- **THEN** a operação falha com `provider-auth-failed`

### Requirement: Tipo suportado decidido pelo mimeType
O conector SHALL decidir o tratamento de cada item da raiz pelo `mimeType`, nunca
pela extensão do nome:

| `mimeType` | resultado |
|---|---|
| `application/vnd.google-apps.document` | suportado, entregue por exportação |
| `text/x-markdown`, `text/markdown` | suportado, entregue por download direto |
| `application/vnd.google-apps.shortcut` | ignorado, `shortcut-not-followed` |
| `application/vnd.google-apps.folder` | ignorado, `subfolder-not-synced` |
| qualquer outro | ignorado, `unsupported-type`, detalhe igual ao `mimeType` |

Item de tipo suportado com `capabilities.canDownload=false` SHALL ser ignorado com
`download-blocked`, sem chamada de exportação nem de download. Atalho SHALL ser
ignorado sem ler o destino e sem nenhuma chamada sobre ele.

#### Scenario: Atalho com nome de documento
- **WHEN** a raiz tem um atalho chamado `Curriculo.docx` cujo destino é um Google Doc
- **THEN** ele aparece em ignorados com `shortcut-not-followed`, e nenhuma chamada é
  feita ao id do destino

#### Scenario: Planilha, subpasta e download bloqueado
- **WHEN** a raiz tem uma planilha, uma subpasta e um Google Doc com
  `canDownload=false`
- **THEN** os três aparecem em ignorados com `unsupported-type`,
  `subfolder-not-synced` e `download-blocked`, e nenhuma exportação é chamada

#### Scenario: Suportados listados com versão
- **WHEN** a raiz tem um Google Doc e um `.md` com `canDownload=true`
- **THEN** os dois aparecem em suportados, o Doc com `modifiedTime` como versão
  externa e o `.md` com `md5Checksum`

#### Scenario: Lixeira fora da listagem
- **WHEN** a listagem é pedida
- **THEN** a consulta enviada ao Google filtra `trashed = false`

### Requirement: Markdown sem imagem embutida
O markdown de um Google Doc SHALL ser obtido por `files.export` com
`mimeType=text/markdown`. Antes da entrega o conector SHALL retirar as definições de
referência cujo destino é uma URI `data:`, as referências de imagem que apontam para
elas e as imagens inline com destino `data:`, sem deixar nada no lugar. O restante
do texto, inclusive escapes e blocos de código sem cerca, SHALL ser entregue como a
exportação o produziu. O markdown de `.md` SHALL ser o conteúdo baixado, sem
transformação. O conector SHALL NOT comparar o tamanho do markdown com o teto do
`apps/api`.

#### Scenario: Doc com imagem em estilo de referência
- **WHEN** a exportação contém `![][image1]` no corpo e
  `[image1]: <data:image/png;base64,…>` no fim
- **THEN** o markdown entregue não contém `data:image`, nem `![][image1]`, nem a
  definição `[image1]:`, e o texto restante é igual ao exportado

#### Scenario: Escapes preservados
- **WHEN** a exportação contém `gerar\_arquivo\_texto\_1mb`
- **THEN** o markdown entregue contém `gerar\_arquivo\_texto\_1mb`, sem alteração

#### Scenario: .md entregue byte a byte
- **WHEN** um `.md` é baixado
- **THEN** o markdown entregue é o conteúdo baixado decodificado como UTF-8, sem
  alteração

#### Scenario: Exportação bloqueada
- **WHEN** a exportação responde `403` com reason `cannotExportFile`
- **THEN** a operação falha com `download-blocked`

### Requirement: Erros do Google distinguidos pelo reason
O conector SHALL ler `error.errors[0].reason` do corpo de erro e mapear para
código, usando o status só quando o corpo não trouxer `reason`:

| resposta | código |
|---|---|
| `404 notFound` ao ler a pasta, `403 insufficientFilePermissions` | `access-denied`, detalhe igual ao e-mail da conta |
| `403 accessNotConfigured` | `api-not-configured` |
| `403 userRateLimitExceeded`, `403 rateLimitExceeded`, `429 rateLimitExceeded` | `rate-limited` |
| `403 cannotExportFile` | `download-blocked` |
| `404 notFound` ao ler um arquivo | `file-not-found` |
| `401` | `provider-auth-failed` |
| `5xx`, timeout, falha de rede | `provider-unavailable` |
| outro `4xx` | `provider-error` |

O detalhe de `provider-error` SHALL ser o `reason` convertido para kebab-case quando
o resultado casar com o formato de código, e nulo caso contrário. Nenhum outro texto
do corpo do Google SHALL ir para o detalhe.

#### Scenario: Mesmo status, reasons diferentes
- **WHEN** o Google responde `403` com `accessNotConfigured`,
  `insufficientFilePermissions`, `userRateLimitExceeded` e `cannotExportFile`, um
  por vez
- **THEN** os códigos são `api-not-configured`, `access-denied`, `rate-limited` e
  `download-blocked`, todos diferentes

#### Scenario: Reason desconhecido
- **WHEN** o Google responde `400` com reason `badRequest`
- **THEN** o código é `provider-error` com detalhe `bad-request`

### Requirement: Pasta sem acesso responde erro, nunca lista vazia
O conector SHALL começar toda operação sobre uma pasta (navegar dentro dela,
descrevê-la, listar a raiz) pela leitura da própria pasta, e SHALL falhar com o código da leitura
se ela falhar, sem consultar o conteúdo. Item que existe e não é pasta SHALL falhar
com `not-a-folder`; pasta com `trashed: true` SHALL falhar com `folder-trashed`.

#### Scenario: Pasta não compartilhada com a conta
- **WHEN** a leitura da pasta responde `404 notFound`
- **THEN** a operação falha com `access-denied` e o e-mail da conta, e nenhuma
  listagem é feita

#### Scenario: Id de arquivo no lugar da pasta
- **WHEN** a leitura do id responde um item com `mimeType` de Google Doc
- **THEN** a operação falha com `not-a-folder`

### Requirement: Parâmetros de Drive Compartilhado em toda chamada que os aceita
O conector SHALL enviar `supportsAllDrives=true` em toda leitura de metadado, listagem
de arquivos e download (`files.get`, inclusive com `alt=media`, e `files.list`), e
SHALL enviar também `includeItemsFromAllDrives=true` em toda listagem de arquivos. A
exportação (`files.export`) SHALL levar só `mimeType`, sem `supportsAllDrives`: a
referência oficial do método
(https://developers.google.com/workspace/drive/api/reference/rest/v3/files/export,
consultada em 03/10/2026) lista `mimeType` como único parâmetro de query. O conector
SHALL NOT enviar `corpora`.

#### Scenario: Listagem com os parâmetros
- **WHEN** o conector lista a raiz de uma pasta
- **THEN** a requisição ao Google contém `supportsAllDrives=true` e
  `includeItemsFromAllDrives=true`, e não contém `corpora`

#### Scenario: Exportação só com mimeType
- **WHEN** o conector exporta um Google Doc
- **THEN** a requisição ao Google contém `mimeType=text/markdown` e não contém
  `supportsAllDrives` nem outro parâmetro

### Requirement: Navegação por Drives Compartilhados e pastas compartilhadas com a conta
Sem pasta de partida, a navegação SHALL devolver os Drives Compartilhados de que a
conta é membro (`drives.list`) e as pastas compartilhadas diretamente com ela
(`files.list` com `sharedWithMe = true`, `mimeType` de pasta e `trashed = false`),
cada um com o seu tipo. Com pasta de partida, SHALL devolver as subpastas dela, depois
de ler a própria pasta. Todas as páginas SHALL ser lidas.

#### Scenario: Nível de cima
- **WHEN** a conta é membro de um Drive Compartilhado e tem uma pasta compartilhada
  diretamente
- **THEN** a navegação sem pasta devolve os dois, um com tipo `SharedDrive` e outro
  com tipo `Folder`

#### Scenario: Dentro de uma pasta sem subpastas
- **WHEN** a pasta existe, é acessível e não tem subpastas
- **THEN** a navegação devolve lista vazia, sem erro
