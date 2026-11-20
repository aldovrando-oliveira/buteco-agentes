## ADDED Requirements

### Requirement: Host derivado compartilhado entre casos da suíte do apps/api repõe o estado e acusa vazamento
Na suíte de `apps/api/tests`, quando casos de uma mesma classe compartilham um host derivado da fixture, o estado de teste mutável que esse host carrega (duplos de serviço externo, contadores) SHALL ser reposto antes de cada caso. Estado de outro caso ainda em andamento SHALL fazer reprovar, com uma mensagem que o diga: no fim do caso que o deixou, e, se chegar ao caso seguinte, nesse caso, com o nome do caso anterior. Casos que não compartilham host derivado não estão sujeitos a este requisito.

#### Scenario: O estado do caso anterior não chega ao seguinte
- **WHEN** casos de uma classe compartilham um host derivado, e um caso termina depois de registrar requisições no duplo do serviço externo e de trocar a resposta dele
- **THEN** o caso seguinte encontra o duplo sem requisições registradas e com a resposta padrão

#### Scenario: O caso que deixa estado em andamento reprova no próprio fim
- **WHEN** um caso termina com uma requisição sua ao duplo do serviço externo ainda em andamento, num host derivado compartilhado
- **THEN** esse caso reprova ao descartar o seu acesso ao host, com a mensagem de estado em andamento

#### Scenario: Estado em andamento que chega ao caso seguinte reprova esse caso com o nome do anterior
- **WHEN** um caso pede o host derivado compartilhado e encontra uma requisição de outro caso ainda em andamento
- **THEN** o caso reprova na reposição do estado, com a mensagem de estado em andamento e o nome do caso que o deixou
