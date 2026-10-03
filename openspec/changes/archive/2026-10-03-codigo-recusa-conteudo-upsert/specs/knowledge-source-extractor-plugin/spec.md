## MODIFIED Requirements

### Requirement: Contrato de extrator por tipo de origem
O sistema SHALL definir um contrato único de extração, `IKnowledgeSourceExtractor`,
resolvido via DI **keyed** pelo valor de `SourceType` — mesmo idioma dos
contratos de adapter de canal de `apps/inbox`.

`SourceType` SHALL ser string aberta validada em runtime contra os extratores
efetivamente registrados, NÃO enum fechado. Extensão de arquivo e `SourceType`
SHALL ser conceitos distintos: a extensão é sugestão do cliente, o `SourceType`
é o extrator a aplicar.

Nesta etapa SHALL existir exatamente um extrator registrado: `markdown`.

A falha de extração SHALL carregar, além da mensagem, um código de recusa no
formato `^[a-z0-9]+(-[a-z0-9]+)*\z`, que a resposta HTTP devolve na extensão
`code`. Um extrator NÃO SHALL poder declarar falha sem código.

#### Scenario: Documento com sourceType registrado é aceito
- **WHEN** um cliente cria um documento com `sourceType: "markdown"`
- **THEN** o extrator de markdown é aplicado ao conteúdo e o documento é criado

#### Scenario: Documento com sourceType desconhecido é rejeitado
- **WHEN** um cliente cria um documento com um `sourceType` para o qual não há
  extrator registrado (por exemplo `"pdf"`)
- **THEN** a API responde HTTP 400 com `ValidationProblemDetails` indicando os
  tipos suportados em `sourceType`, com `code` `unsupported-source-type`, e não
  cria nenhum registro

#### Scenario: Arquivo de texto sem marcação usa o extrator de markdown
- **WHEN** um cliente envia conteúdo de um arquivo `.txt` sem nenhuma marcação
  markdown, com `sourceType: "markdown"`
- **THEN** o documento é criado normalmente — texto puro é markdown válido e não
  é caso de erro

### Requirement: Extração de markdown preserva a marcação
O extrator de `markdown` SHALL preservar a sintaxe markdown do conteúdo. A
extração SHALL ser normalização, NÃO conversão para texto puro: a estratégia de
fragmentação da etapa de indexação divide por cabeçalho, e depende dos
cabeçalhos sobreviverem.

O extrator de `markdown` SHALL, e SHALL apenas: remover BOM UTF-8 inicial;
normalizar terminadores de linha `CRLF` e `CR` para `LF`; rejeitar conteúdo que
contenha o caractere NUL (U+0000), com o código `null-character`; e rejeitar
conteúdo vazio ou composto apenas de espaços em branco, com o código
`empty-content`.

#### Scenario: Cabeçalhos markdown sobrevivem à extração
- **WHEN** um documento é criado com conteúdo contendo cabeçalhos `#`, `##` e
  listas
- **THEN** o `extractedText` consultado depois contém os mesmos cabeçalhos e
  listas, com a marcação intacta

#### Scenario: BOM e terminadores de linha são normalizados
- **WHEN** um documento é criado com conteúdo iniciado por BOM UTF-8 e com
  terminadores `CRLF`
- **THEN** o `extractedText` persistido não contém BOM e usa apenas `LF`

#### Scenario: Conteúdo com caractere NUL é rejeitado
- **WHEN** um cliente cria um documento cujo conteúdo contém o caractere U+0000
- **THEN** a API responde HTTP 400 com `ValidationProblemDetails` e `code`
  `null-character`, e não cria nenhum registro, sem deixar o erro chegar ao banco
  como falha de coluna

#### Scenario: Conteúdo com acentos e emoji é aceito
- **WHEN** um cliente cria um documento com acentuação portuguesa e emoji
- **THEN** o documento é criado e o `extractedText` consultado preserva os
  caracteres exatamente

#### Scenario: Conteúdo só de espaços em branco é rejeitado
- **WHEN** um cliente cria um documento cujo conteúdo contém apenas espaços,
  tabulações e quebras de linha
- **THEN** a API responde HTTP 400 com `ValidationProblemDetails` e `code`
  `empty-content`, e não cria nenhum registro

#### Scenario: Conteúdo que só fica vazio depois de normalizar é rejeitado
- **WHEN** um cliente cria um documento cujo conteúdo é só o BOM UTF-8
- **THEN** a API responde HTTP 400 com `code` `empty-content`, e não cria nenhum
  registro
