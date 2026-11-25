## ADDED Requirements

### Requirement: Os casos de round-trip esperam uma condição durável antes de afirmar o fim do despacho
Na suíte `tests/InboxOrchestratorRoundTrip.Tests`, um caso que acompanha um despacho do debounce até o fim SHALL esperar primeiro uma condição positiva e durável, e só depois afirmar a ausência do registro de despacho. A condição é uma task do agente do caso em estado final no store de tasks do `apps/api`, ou a resposta entregue à conversa.

Nenhum caso SHALL condicionar o progresso a um dado que existe só entre duas transições do despacho, como o identificador da task gravado no registro de despacho. Quando o caso precisa do identificador da task antes do estado final, SHALL lê-lo do store de tasks do `apps/api`, pelo agente do próprio caso.

#### Scenario: A task termina dentro de um único intervalo de consulta
- **WHEN** o despacho de um caso ganha o identificador da task e é fechado pelo push entre duas consultas do caso
- **THEN** o caso segue pela task do agente em estado final e pela ausência do registro, e passa

#### Scenario: Com atraso artificial no SendMessage, os casos continuam passando
- **WHEN** o `SendMessage` do `apps/api` ganha 8 ms de atraso antes da conferência do provedor, numa cópia descartável
- **THEN** os casos do projeto passam, enquanto a mesma cópia sem esta correção reprova os casos expostos na espera do identificador no registro

#### Scenario: A ausência do registro sozinha não basta
- **WHEN** o registro de despacho passa a ser criado depois de o recebimento voltar, e o caso não espera a condição positiva
- **THEN** o caso reprova, porque a ausência do registro vale antes de existir task

#### Scenario: A sentinela do reinício prova que passou pelo worker
- **WHEN** a conversa sentinela do caso de parada do worker fecha o despacho
- **THEN** o caso só segue depois de uma mensagem de saída estar entregue à conversa da sentinela, e essa mensagem é a resposta do agente

#### Scenario: A sentinela recusada sem task reprova
- **WHEN** o `apps/api` recusa o `SendMessage` da sentinela sem criar task, e o inbox entrega o aviso de falha
- **THEN** o caso reprova na asserção da resposta, com o aviso de falha no lugar dela

#### Scenario: Uma segunda task do agente reprova com a lista
- **WHEN** o agente de um caso de uma mensagem tem mais de uma task no store de tasks do `apps/api`
- **THEN** o caso reprova, com o identificador e o estado de cada task do agente na mensagem
