## MODIFIED Requirements

### Requirement: Conector falso só na composição de teste
O sistema SHALL prover um conector falso que implementa os dois contratos e a conta,
registrado apenas pela composição de teste. A composição de produção SHALL NOT
registrá-lo.

A listagem da raiz e o markdown de cada arquivo do conector falso SHALL ser
configuráveis pelo teste, inclusive para lançar `ConnectorFailure` com um código, de
modo que o ciclo de sincronização seja testado sem o provedor real.

#### Scenario: Produção não lista o conector falso
- **WHEN** a composição de produção é construída
- **THEN** `GET /connectors/providers` não contém a chave do conector falso

#### Scenario: Listagem configurada pelo teste
- **WHEN** o teste configura o conector falso com dois arquivos suportados e um
  ignorado, e faz o markdown de um deles lançar `ConnectorFailure` com
  `download-blocked`
- **THEN** `ListRootAsync` devolve os três como configurados, e `GetMarkdownAsync`
  lança a falha com o código para aquele arquivo e devolve o markdown para o outro
