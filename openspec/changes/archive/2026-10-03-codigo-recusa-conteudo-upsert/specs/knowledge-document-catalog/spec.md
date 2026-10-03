## ADDED Requirements

### Requirement: Recusa de conteúdo carrega código estável
Toda recusa de **conteúdo** de documento SHALL responder HTTP 400 com
`ValidationProblemDetails` e a extensão `code` no nível de cima do corpo, nas
três rotas que gravam conteúdo: `POST /knowledge-bases/{kbId}/documents`,
`PUT /knowledge-bases/{kbId}/documents/{id}` e
`PUT /sync/knowledge-bases/{id}/documents`.

Os códigos SHALL ser:

| recusa | `code` |
|---|---|
| texto extraído acima de 1.048.576 bytes em UTF-8 | `too-large` |
| `sourceType` sem extrator registrado | `unsupported-source-type` |
| conteúdo com o caractere U+0000 | `null-character` |
| conteúdo vazio ou só de espaços em branco, depois da normalização | `empty-content` |

Todo código SHALL ter a forma `^[a-z0-9]+(-[a-z0-9]+)*\z`, até 64 caracteres, e
NÃO SHALL ser frase. A resposta SHALL manter o `title` e o `errors` (chave e
mensagem) que a recusa já tinha: o código é acréscimo.

A recusa de **forma** — campo obrigatório ausente no corpo — SHALL continuar
`ValidationProblemDetails` e NÃO SHALL ter a propriedade `code`. A presença de
`code` é o que distingue recusa de conteúdo de recusa de forma.

#### Scenario: A recusa do operador continua com errors e ganha o código
- **WHEN** um cliente envia `POST /knowledge-bases/{kbId}/documents` com conteúdo
  acima do teto
- **THEN** o texto da resposta tem status 400, a propriedade `code` com o valor
  `too-large`, `errors.content` com a mensagem e o `title` de antes

#### Scenario: A atualização do operador leva o mesmo código
- **WHEN** um cliente envia `PUT /knowledge-bases/{kbId}/documents/{id}` com
  conteúdo contendo U+0000
- **THEN** o texto da resposta tem status 400 e `code` `null-character`, e o
  documento continua como estava

#### Scenario: Recusa de forma do operador não tem código
- **WHEN** um cliente envia `POST /knowledge-bases/{kbId}/documents` sem título
- **THEN** a API responde HTTP 400 com `ValidationProblemDetails` em `title`, e o
  corpo não tem a propriedade `code`

#### Scenario: Todo código de recusa tem a forma de código
- **WHEN** os códigos de recusa de conteúdo declarados no `apps/api` são
  verificados contra o formato `^[a-z0-9]+(-[a-z0-9]+)*\z` de até 64 caracteres
- **THEN** todos passam

## MODIFIED Requirements

### Requirement: Cadastro de documento numa base
O sistema SHALL permitir, via `apps/api`, cadastrar um documento dentro de uma
base de conhecimento informando título, `sourceType` e conteúdo como texto.

O corpo SHALL ser JSON. NÃO SHALL haver rota multipart nesta etapa: os formatos
aceitos são texto, e o cliente envia o conteúdo já lido como string. O servidor
SHALL receber título explícito e NÃO SHALL derivá-lo do nome de arquivo — a
derivação é sugestão editável do cliente. O mesmo endpoint SHALL servir tanto o
caminho "arquivo subido" quanto o caminho "texto digitado": nada no contrato os
distingue.

O conteúdo SHALL passar pelo extrator do `sourceType` informado antes de ser
persistido em `extractedText`.

O conteúdo **ausente** do corpo SHALL ser recusa de forma, sem `code`. O conteúdo
presente e vazio ou só de espaços SHALL ser recusa de conteúdo, com `code`
`empty-content`, verificada depois da existência da base.

#### Scenario: Criar documento com conteúdo markdown
- **WHEN** um cliente envia `POST /knowledge-bases/{kbId}/documents` com título
  não vazio, `sourceType: "markdown"` e conteúdo não vazio, para uma base
  existente
- **THEN** a API responde HTTP 201 com o documento criado, incluindo id, título,
  `sourceType`, `indexingStatus: "Pending"`, `indexedAt: null`,
  `failureReason: null` e `contentRevision: 1`

#### Scenario: Criar documento em base inexistente retorna 404
- **WHEN** um cliente envia `POST /knowledge-bases/{kbId}/documents` para um
  `kbId` que não existe
- **THEN** a API responde HTTP 404 e nenhum documento é criado

#### Scenario: Criar documento em base inativa é permitido
- **WHEN** um cliente envia `POST /knowledge-bases/{kbId}/documents` para uma
  base desativada
- **THEN** a API cria o documento normalmente — desativar a base impede seu uso
  pelo agente, não a manutenção do seu conteúdo

#### Scenario: Criar documento sem título é rejeitado
- **WHEN** um cliente envia `POST /knowledge-bases/{kbId}/documents` sem título,
  ou com título vazio ou só de espaços
- **THEN** a API responde HTTP 400 com `ValidationProblemDetails` e não cria
  nenhum registro

#### Scenario: Criar documento com conteúdo vazio é rejeitado
- **WHEN** um cliente envia `POST /knowledge-bases/{kbId}/documents` com
  conteúdo vazio ou só de espaços
- **THEN** a API responde HTTP 400 com `ValidationProblemDetails` e `code`
  `empty-content`, e não cria nenhum registro

#### Scenario: Criar documento sem conteúdo é recusa de forma
- **WHEN** um cliente envia `POST /knowledge-bases/{kbId}/documents` sem o campo
  de conteúdo
- **THEN** a API responde HTTP 400 com `ValidationProblemDetails` em `content`, o
  corpo não tem a propriedade `code`, e nenhum registro é criado

#### Scenario: Conteúdo em branco é recusa de conteúdo, depois da base
- **WHEN** um cliente envia `POST /knowledge-bases/{kbId}/documents` com conteúdo
  só de espaços para um `kbId` que não existe
- **THEN** a API responde HTTP 404

#### Scenario: Título duplicado na mesma base é permitido
- **WHEN** um cliente cria dois documentos com o mesmo título na mesma base
- **THEN** a API cria os dois normalmente, cada um com identificador próprio

### Requirement: Teto de tamanho de conteúdo por documento
O sistema SHALL rejeitar conteúdo cujo tamanho em UTF-8 exceda 1 MiB
(1.048.576 bytes), respondendo HTTP 400 com `ValidationProblemDetails`. O limite
SHALL ser validado no handler, e NÃO SHALL depender do limite de corpo de
requisição do servidor web — que responderia HTTP 413 sem corpo interpretável
pela interface.

A recusa SHALL ter `code` `too-large`, e SHALL trazer o tamanho medido e o teto
como números, nas extensões `contentBytes` e `maxContentBytes`, e no `detail`. O
teto SHALL ser uma constante única no `apps/api`, e `maxContentBytes` SHALL ser
lido dela.

#### Scenario: Conteúdo dentro do teto é aceito
- **WHEN** um cliente envia um documento cujo conteúdo tem 1.048.576 bytes em
  UTF-8 ou menos
- **THEN** a API cria o documento normalmente

#### Scenario: Conteúdo acima do teto é rejeitado com 400
- **WHEN** um cliente envia um documento cujo conteúdo excede 1.048.576 bytes em
  UTF-8
- **THEN** a API responde HTTP 400 com `ValidationProblemDetails` identificando
  o campo de conteúdo, `code` `too-large`, `contentBytes` igual ao tamanho do
  texto extraído e `maxContentBytes` igual ao teto, e não cria nenhum registro

#### Scenario: O teto é medido depois da extração, não sobre a entrada crua
- **WHEN** um cliente envia um documento com BOM UTF-8 e terminadores `CRLF`
  cujo tamanho **bruto** excede 1.048.576 bytes, mas cujo texto já extraído
  (BOM removido, `CRLF` normalizado para `LF`) não excede
- **THEN** a API cria o documento normalmente, e o `contentLengthBytes`
  devolvido é o do texto extraído — o mesmo valor que a validação usou
