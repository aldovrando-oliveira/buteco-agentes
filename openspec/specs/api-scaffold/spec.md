# api-scaffold Specification

## Purpose

TBD - defined by change estrutura-base-monorepo. Update Purpose after archive.
## Requirements
### Requirement: Solução .NET própria para apps/api
O sistema SHALL prover, em `apps/api`, uma solução .NET própria (`Api.sln`) com
pastas `src/` e `tests/`, isolada de `apps/workers` e `apps/frontend`, sem
nenhuma project reference para projetos desses outros apps.

#### Scenario: Build isolado da solução da API
- **WHEN** um desenvolvedor executa `dotnet build` a partir de `apps/api/Api.sln`
- **THEN** a solução compila com sucesso sem depender de nenhum projeto localizado em `apps/workers` ou `apps/frontend`

#### Scenario: Nenhuma referência cruzada de projeto
- **WHEN** os arquivos `.csproj` dentro de `apps/api/src` e `apps/api/tests` são inspecionados
- **THEN** nenhum deles contém `ProjectReference` apontando para caminhos fora de `apps/api`

### Requirement: Web API mínima com health check
O sistema SHALL expor, em `apps/api`, um projeto ASP.NET Core Web API mínimo
que responde a um endpoint de health check, servindo como base para receber o
`A2AServer` (Microsoft.Agents.AI.Hosting.A2A) em uma mudança futura.

#### Scenario: Health check responde OK
- **WHEN** a aplicação Web API de `apps/api` está em execução e uma requisição HTTP GET é feita ao endpoint de health check
- **THEN** a resposta retorna HTTP 200 com um corpo indicando o status saudável da aplicação

### Requirement: Testes automatizados para apps/api
O sistema SHALL incluir, em `apps/api/tests`, um projeto de testes automatizados
capaz de validar ao menos o endpoint de health check.

#### Scenario: Suíte de testes da API executa com sucesso
- **WHEN** um desenvolvedor executa `dotnet test` a partir de `apps/api/Api.sln`
- **THEN** o teste do endpoint de health check é executado e passa

### Requirement: A suíte de integração do apps/api autentica pelo token do emissor
As fixtures de integração de `apps/api/tests` SHALL anexar por padrão um access
token do emissor do próprio `apps/api` (cliente `buteco-painel`, `sub` que não é
usuário), e os casos que provam regra por subject SHALL obter o token do cliente
que a tabela de clientes do `apps/api` mapeia para aquele subject. Nenhum helper de
teste SHALL assinar token fora do emissor.

#### Scenario: Fixture padrão com o comportamento do operador sem usuário
- **WHEN** um caso usa o cliente padrão de uma fixture de integração
- **THEN** as rotas do catálogo respondem como ao operador, e as rotas que exigem
  um usuário respondem `403`

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
