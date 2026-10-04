## MODIFIED Requirements

### Requirement: Eventos pertencem à base e morrem com ela
Os eventos de uma base SHALL ser removidos pelo banco quando a base é removida,
por FK com `ON DELETE CASCADE`. Desde a exclusão de base pela rota do operador, a
cascata SHALL ser alcançada também por `DELETE /knowledge-bases/{id}`, inclusive
numa base que ainda tem documentos. A exclusão da base NÃO SHALL registrar evento
`Deleted` para os documentos que leva junto, porque os eventos da base são
apagados no mesmo commit.

#### Scenario: Excluir a base remove os eventos dela e só os dela
- **WHEN** uma base teve documentos incluídos e depois excluídos, de modo que
  não tem documento mas tem eventos, e a linha dela é apagada de
  `knowledge_bases`
- **THEN** `knowledge_document_events` não tem mais nenhuma linha dessa base
- **AND** os eventos de outra base continuam intactos

#### Scenario: Excluir pela rota uma base que ainda tem documentos
- **WHEN** uma base inativa com documentos e eventos é excluída por
  `DELETE /knowledge-bases/{id}`
- **THEN** `knowledge_document_events` não tem mais nenhuma linha dessa base
- **AND** nenhum evento novo foi registrado em nenhuma base
- **AND** os eventos de outra base continuam intactos
