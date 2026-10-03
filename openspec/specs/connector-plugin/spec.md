# connector-plugin Specification

## Purpose

O contrato que todo conector de provedor de arquivos cumpre dentro do
`apps/connectors`: dois contratos registrados por chave (navegar e descrever pasta;
listar a raiz e entregar markdown) mais a conta do provedor, falhas sempre como
código, listagem completa ou erro, e a checagem de boot que impede um conector
incompleto de subir. Não descreve nenhum provedor específico; o Google Drive está em
`google-drive-connector`.

## Requirements

### Requirement: Contrato de conector com quatro operações em dois contratos keyed
Cada provedor SHALL ser registrado no DI sob uma chave (`google-drive`, por
exemplo) com dois contratos obrigatórios e um registro de conta:

- `IFolderNavigator`: navegar (o nível de cima, sem pasta, ou as subpastas de uma
  pasta) e descrever uma pasta (id, nome, URL web, verificando o acesso);
- `IFolderContentSource`: listar os arquivos da raiz de uma pasta, separando os
  suportados (referência externa, nome, versão externa) dos ignorados (referência
  externa, nome, código e detalhe), e entregar o markdown de um arquivo suportado;
- `ConnectorAccount`: o e-mail da conta com que o provedor acessa.

Toda falha de operação SHALL ser expressa como código no formato
`^[a-z0-9]+(-[a-z0-9]+)*$`, até 64 caracteres, mais detalhe opcional, nunca como
frase.

#### Scenario: Conector registrado completo é resolvido pela chave
- **WHEN** um conector registra os dois contratos e a conta sob a mesma chave
- **THEN** a rota de provedores lista a chave com o e-mail da conta, e as rotas de
  navegação resolvem o navegador por essa chave

#### Scenario: Chave não registrada
- **WHEN** uma rota recebe uma chave de provedor sem registro
- **THEN** a resposta é `404` com código `provider-not-configured`

### Requirement: Registro incompleto derruba o boot
Uma checagem de integridade SHALL rodar sobre a `IServiceCollection` depois de todos
os registros e antes do `Build()`, e derrubar o processo se alguma chave tiver um
dos três registros (`IFolderNavigator`, `IFolderContentSource`, `ConnectorAccount`)
sem os outros dois. A mensagem SHALL nomear a chave e o registro ausente. A
composição real de produção SHALL passar pela mesma checagem, verificada por um
teste que captura a `IServiceCollection` do host de teste.

#### Scenario: Conector sem a fonte de conteúdo
- **WHEN** uma chave registra `IFolderNavigator` e `ConnectorAccount` sem
  `IFolderContentSource`
- **THEN** a inicialização falha com mensagem que nomeia a chave e
  `IFolderContentSource`

#### Scenario: Conector sem conta
- **WHEN** uma chave registra os dois contratos sem `ConnectorAccount`
- **THEN** a inicialização falha com mensagem que nomeia a chave e
  `ConnectorAccount`

#### Scenario: A composição real sobe
- **WHEN** o host de produção é construído com a credencial do Google presente
- **THEN** a checagem passa, e a chave `google-drive` tem os três registros

#### Scenario: A composição real sobe sem credencial
- **WHEN** o host de produção é construído sem credencial do Google
- **THEN** a checagem passa, nenhuma chave é registrada, e
  `GET /connectors/providers` responde `200` com lista vazia

### Requirement: Listagem completa ou erro
`ListRootAsync` SHALL devolver a listagem de todas as páginas, ou falhar com
código. Uma falha em qualquer página SHALL fazer a operação inteira falhar, e a
operação SHALL NOT devolver listagem parcial.

#### Scenario: Falha na segunda página
- **WHEN** a primeira página da listagem responde com sucesso e a segunda falha
- **THEN** a operação falha com o código da segunda, sem devolver os itens da
  primeira

#### Scenario: Pasta vazia
- **WHEN** a pasta existe, é acessível e não tem arquivos
- **THEN** a operação devolve listagem vazia, sem erro

### Requirement: Conector falso só na composição de teste
O sistema SHALL prover um conector falso que implementa os dois contratos e a conta,
registrado apenas pela composição de teste. A composição de produção SHALL NOT
registrá-lo.

#### Scenario: Produção não lista o conector falso
- **WHEN** a composição de produção é construída
- **THEN** `GET /connectors/providers` não contém a chave do conector falso
