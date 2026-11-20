## ADDED Requirements

### Requirement: O paralelismo da suíte do apps/api é declarado no projeto e verificável na execução
O limite de paralelismo da suíte de `apps/api/tests` SHALL ser declarado num arquivo do próprio projeto de testes, que vale para qualquer forma de executar a suíte (o projeto, a solução, a IDE), sem depender de argumento de linha de comando. O valor efetivo com que uma execução rodou SHALL ser verificável no registro dessa execução, pela forma descrita em `docs/development.md`.

#### Scenario: A suíte roda com o limite declarado no projeto
- **WHEN** um desenvolvedor executa `dotnet test` da suíte de `apps/api/tests` sem nenhum argumento de paralelismo
- **THEN** a execução usa o limite declarado no arquivo do projeto, e não o padrão do runner

#### Scenario: O valor efetivo aparece no registro da execução
- **WHEN** alguém lê o registro de uma execução da suíte feita pela forma descrita em `docs/development.md`
- **THEN** o registro mostra o limite de paralelismo efetivo daquela execução

#### Scenario: Sem o arquivo, o registro mostra o padrão
- **WHEN** a suíte roda sem que o arquivo de configuração chegue à saída do projeto
- **THEN** o registro da execução mostra o limite padrão do runner, e não o declarado
