## MODIFIED Requirements

### Requirement: Detalhe da aplicação com os segredos
`GET /applications/{id}` SHALL devolver os campos da lista, exceto `agentes` (os agentes liberados vêm de `GET /applications/{id}/agents`, `external-application-agents`), mais `endpointToken` e `segredos`: cada um com `{ id, sufixo, estado, criadoEm, validoAte, revogadoEm, ultimoUso }`, do mais novo para o mais antigo. `estado` SHALL ser derivado no momento da leitura: `revogado` se revogado; senão `expirado` se `validoAte` passou; senão `substituido` se existe segredo mais novo da mesma aplicação; senão `ativo`. Nenhuma resposta SHALL trazer o hash nem o texto do segredo. Aplicação inexistente SHALL responder `404` com `title` "Aplicação não encontrada.".

#### Scenario: Os quatro estados
- **WHEN** uma aplicação tem um segredo revogado, um vencido, um substituído dentro da sobreposição e o mais novo, e o Admin chama o detalhe
- **THEN** os estados são `revogado`, `expirado`, `substituido` e `ativo`, e nenhum segredo traz `valor` nem hash

#### Scenario: O detalhe não traz os agentes liberados
- **WHEN** a aplicação tem um agente liberado e o Admin chama o detalhe
- **THEN** a resposta não tem o campo `agentes`, e `GET /applications/{id}/agents` traz o agente
