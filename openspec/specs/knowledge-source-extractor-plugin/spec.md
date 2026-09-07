# knowledge-source-extractor-plugin Specification

## Purpose

TBD - defined by change knowledge-base-catalogo-documentos. Update Purpose after archive.

## Requirements

### Requirement: Contrato de extrator por tipo de origem
O sistema SHALL definir um contrato único de extração, `IKnowledgeSourceExtractor`,
resolvido via DI **keyed** pelo valor de `SourceType` — mesmo idioma dos
contratos de adapter de canal de `apps/inbox`.

`SourceType` SHALL ser string aberta validada em runtime contra os extratores
efetivamente registrados, NÃO enum fechado. Extensão de arquivo e `SourceType`
SHALL ser conceitos distintos: a extensão é sugestão do cliente, o `SourceType`
é o extrator a aplicar.

Nesta etapa SHALL existir exatamente um extrator registrado: `markdown`.

#### Scenario: Documento com sourceType registrado é aceito
- **WHEN** um cliente cria um documento com `sourceType: "markdown"`
- **THEN** o extrator de markdown é aplicado ao conteúdo e o documento é criado

#### Scenario: Documento com sourceType desconhecido é rejeitado
- **WHEN** um cliente cria um documento com um `sourceType` para o qual não há
  extrator registrado (por exemplo `"pdf"`)
- **THEN** a API responde HTTP 400 com `ValidationProblemDetails` indicando os
  tipos suportados, e não cria nenhum registro

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
contenha o caractere NUL (U+0000); e rejeitar conteúdo vazio ou composto apenas
de espaços em branco.

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
- **THEN** a API responde HTTP 400 com `ValidationProblemDetails` e não cria
  nenhum registro, sem deixar o erro chegar ao banco como falha de coluna

#### Scenario: Conteúdo com acentos e emoji é aceito
- **WHEN** um cliente cria um documento com acentuação portuguesa e emoji
- **THEN** o documento é criado e o `extractedText` consultado preserva os
  caracteres exatamente

#### Scenario: Conteúdo só de espaços em branco é rejeitado
- **WHEN** um cliente cria um documento cujo conteúdo contém apenas espaços,
  tabulações e quebras de linha
- **THEN** a API responde HTTP 400 com `ValidationProblemDetails` e não cria
  nenhum registro

### Requirement: Checagem de integridade de extratores no startup
`apps/api` SHALL validar, no startup — antes de começar a servir requisições —,
que a lista de `SourceType` declarados e os extratores registrados via DI keyed
correspondem exatamente. A validação SHALL ser **bidirecional**: tipo declarado
sem extrator registrado é falha, e extrator registrado para tipo não declarado
também é falha.

A falha SHALL lançar `InvalidOperationException` com mensagem identificando o
tipo divergente, sem bypass e sem modo de tolerância.

O **momento exato no ciclo de vida do host** é decisão de design, não requisito:
o que esta spec exige é que a aplicação não sirva requisição alguma com registro
divergente. Ver a convenção 8 de `01-ARQUITETURA_E_CONVENCOES.md` para as duas
formas do padrão (sobre `IServiceCollection`, antes do `Build()`, para checagens
que inspecionam descritores de DI keyed — o caso desta; e sobre o host
construído, para as demais) e para o teste adicional que a primeira forma
obriga.

#### Scenario: Registro completo permite o boot
- **WHEN** todos os `SourceType` declarados têm extrator registrado e não há
  extrator registrado para tipo não declarado
- **THEN** a aplicação inicia normalmente

#### Scenario: Tipo declarado sem extrator registrado impede o boot
- **WHEN** um `SourceType` está declarado na lista esperada mas nenhum extrator
  foi registrado para ele
- **THEN** a inicialização falha com `InvalidOperationException` identificando o
  tipo, e a aplicação não sobe

#### Scenario: Extrator registrado para tipo não declarado impede o boot
- **WHEN** existe um extrator registrado via DI keyed para um valor que não está
  na lista de tipos declarados
- **THEN** a inicialização falha com `InvalidOperationException` identificando o
  valor, e a aplicação não sobe
