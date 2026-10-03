## MODIFIED Requirements

### Requirement: Autor é o subject de quem escreveu
O `author` de cada evento SHALL ser o subject do token autenticado que fez a
escrita, gravado como veio, sem tradução para rótulo. Os escritores são o
operador, cujo subject é `operator`, e a sincronização, cujo subject é
`service:connectors`. O rótulo de apresentação ("operador", "sincronização") é do
frontend.

#### Scenario: Escrita do operador grava author operator
- **WHEN** o operador, autenticado pelo login, cadastra, atualiza e exclui um
  documento
- **THEN** os três eventos têm `author` igual a `operator`

#### Scenario: Escrita da sincronização grava author service:connectors
- **WHEN** o subject `service:connectors` cria por upsert, atualiza por upsert e
  exclui por referência um documento de base `Synced`
- **THEN** os três eventos têm `author` igual a `service:connectors`

## ADDED Requirements

### Requirement: Upsert sem mudança de documento não registra evento
A sincronização SHALL registrar evento só quando o documento muda de fato.
Um upsert da sincronização cujo título e texto extraído sejam iguais aos gravados
NÃO SHALL registrar evento, mesmo quando o marcador de versão do provedor mudou. A
exclusão por referência de um documento que não existe NÃO SHALL registrar evento.

#### Scenario: Marcador novo com o mesmo texto
- **WHEN** o subject `service:connectors` faz upsert de um documento existente com
  `externalVersion` diferente, mesmo título e mesmo texto
- **THEN** a contagem de eventos da base não muda

#### Scenario: Exclusão de referência inexistente
- **WHEN** o subject `service:connectors` exclui uma referência que a base não tem
- **THEN** a contagem de eventos da base não muda
