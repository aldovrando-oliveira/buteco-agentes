## MODIFIED Requirements

### Requirement: Execuções concorrentes não misturam métricas
As linhas de `provider_calls` e `delegation_outcomes` SHALL pertencer à execução
em que a requisição ou a delegação aconteceu, mesmo com várias execuções no
mesmo processo — inclusive várias execuções no **mesmo host**, que é o regime de
produção quando `TaskConsumer:MaxConcurrentExecutions` é maior que 1.

#### Scenario: Source e Target no mesmo processo de teste
- **WHEN** Source e Target executam em duas instâncias hospedadas no mesmo processo
- **THEN** toda linha de `provider_calls` com o `TaskId` do Target foi produzida pela execução do Target, e nenhuma linha de `delegation_outcomes` tem o `TaskId` do Target como `SourceTaskId`

#### Scenario: Duas execuções no mesmo host
- **WHEN** um único host com `N = 2` executa ao mesmo tempo duas tasks de agentes
  com provedores diferentes
- **THEN** toda linha de `provider_calls` de cada task tem o provedor do agente
  daquela task, e nenhuma linha de uma task aparece com o `TaskId` da outra
