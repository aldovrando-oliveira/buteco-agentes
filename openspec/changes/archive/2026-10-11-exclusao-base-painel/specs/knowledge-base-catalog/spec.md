## MODIFIED Requirements

### Requirement: Pasta em uso recusada no cadastro com a base que a usa
O cadastro SHALL recusar com `409` a base `Synced` cujo provedor e id de pasta já
são usados por outra base, ativa ou inativa, respondendo com `ProblemDetails`, a extensão `code`
igual a `folder-in-use`, as extensões `knowledgeBaseId` e `knowledgeBaseName` com
a base que usa a pasta, e um `detail` que nomeia essa base, diz que a pasta
continua ocupada mesmo com a base inativa, e oferece as duas saídas, nesta ordem:
escolher outra pasta, ou desativar e excluir essa base para liberar a pasta,
avisando que a exclusão apaga os documentos dela e não pode ser desfeita. Nenhuma base SHALL ser criada.

A orientação existe porque a base tem exclusão (requisito "Exclusão de base de
conhecimento inativa") e o painel a oferece desde a change `exclusao-base-painel`
(#136). Até ali o `detail` era proibido de falar em excluir, remover ou apagar,
porque a orientação levaria a uma ação que nenhuma tela oferecia.

A pasta em uso SHALL ser detectada antes da chamada ao `apps/connectors`, e por
isso SHALL responder `409` mesmo com o `apps/connectors` fora do ar ou não
configurado. A corrida entre dois cadastros simultâneos da mesma pasta, que passa
pela detecção antes da gravação, SHALL ser resolvida pelo índice único da pasta:
a violação desse índice SHALL virar o mesmo `409`, com a base vencedora, e NUNCA
`500`.

#### Scenario: Pasta usada por base ativa
- **WHEN** um cliente cria uma base `Synced` com o provedor e a pasta de uma base
  ativa já existente
- **THEN** a API responde HTTP 409 com `code: "folder-in-use"`, o id e o nome da
  base existente, e não cria nenhum registro

#### Scenario: Pasta usada por base inativa
- **WHEN** a base que usa a pasta está inativa e um cliente cria outra base
  `Synced` com a mesma pasta
- **THEN** a API responde HTTP 409 com o nome da base inativa e um `detail` que diz
  que a pasta continua ocupada mesmo com a base inativa

#### Scenario: A mensagem orienta desativar e excluir a base
- **WHEN** a API responde 409 por pasta em uso
- **THEN** o `detail` nomeia a base que usa a pasta, oferece escolher outra pasta, e
  contém "desative", "exclua" e "não pode ser desfeita"

#### Scenario: Pasta em uso com o apps/connectors fora do ar
- **WHEN** a pasta já é usada por outra base e o `apps/connectors` não responde,
  ou `Connectors:BaseUrl` não está configurado
- **THEN** a API responde HTTP 409 com `code: "folder-in-use"`, e o
  `apps/connectors` não recebe nenhuma requisição

#### Scenario: Corrida de dois cadastros da mesma pasta
- **WHEN** dois cadastros `Synced` da mesma pasta passam juntos pela detecção de
  pasta em uso, com a validação no `apps/connectors` retida até os dois chegarem
- **THEN** exatamente um responde HTTP 201 e o outro HTTP 409 com o nome da base
  criada pelo primeiro, nenhum responde 500, há uma base só com aquela pasta no
  banco, e o evento de log de corrida rejeitada foi registrado uma vez

#### Scenario: Pastas que diferem só na caixa não conflitam
- **WHEN** existe uma base `Synced` com a pasta `AbC` e um cliente cria outra, do
  mesmo provedor, com a pasta `abc`
- **THEN** a segunda base é criada com HTTP 201
