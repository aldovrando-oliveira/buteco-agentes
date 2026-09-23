## ADDED Requirements

### Requirement: Nível de log de produção vive no repositório, em fonte única
O nível de log que cada app usa em produção SHALL estar declarado em arquivo
versionado do próprio app, e SHALL NOT depender de ajuste feito no ambiente do
servidor. Em produção, o log de comando de banco do EF Core
(`Microsoft.EntityFrameworkCore.Database.Command`) SHALL estar em `Warning` nos
apps que usam EF Core, e a categoria `Default` SHALL permanecer em
`Information` — subi-la esconderia as linhas de início dos serviços de
varredura, que são a única prova de que estão registrados. Havendo configuração
equivalente no ambiente do servidor, ela SHALL ser removida no deploy desta
mudança: duas fontes do mesmo valor divergem.

#### Scenario: App sobe em produção sem configuração de log no ambiente
- **WHEN** um app que usa EF Core sobe com o ambiente de hospedagem `Production`
  e nenhuma variável de log definida no ambiente
- **THEN** o log de comando de banco do EF Core não aparece, e as linhas de
  nível `Information` dos serviços do app continuam aparecendo

#### Scenario: Desenvolvimento não é afetado
- **WHEN** o mesmo app sobe no ambiente de desenvolvimento
- **THEN** o nível de log continua o de desenvolvimento, sem herdar o silêncio
  configurado para produção
