## MODIFIED Requirements

### Requirement: Push notification config persistido pelo worker
O sistema SHALL, em `apps/workers`, persistir o `pushNotificationConfig`
recebido em `AgentTask.Metadata` ao transicionar a task para um estado
terminal que o worker processa (`completed` ou `failed`), codificado no
mesmo formato de fio que o restante do payload A2A persistido — camelCase
e sem campos opcionais ausentes serializados como `null`.

#### Scenario: Metadata contém o config após completed
- **WHEN** uma task com `pushNotificationConfig` registrado é processada
  com sucesso pelo worker
- **THEN** a task consultável via `GetTask` tem `Metadata` contendo o
  `pushNotificationConfig` registrado

#### Scenario: Metadata contém o config após failed
- **WHEN** uma task com `pushNotificationConfig` registrado falha durante
  o processamento pelo worker
- **THEN** a task consultável via `GetTask` tem `Metadata` contendo o
  `pushNotificationConfig` registrado

#### Scenario: Metadata persistida segue o formato de fio da spec A2A
- **WHEN** uma task com `pushNotificationConfig` registrado (incluindo
  `url` e, quando presentes, `token`/`authentication`) atinge um estado
  terminal processado pelo worker
- **THEN** o `pushNotificationConfig` dentro de `Metadata`, consultável
  via `GetTask`/`ListTasks`, usa os nomes de campo em camelCase (`url`,
  `token`, `authentication`, `id`) e omite qualquer campo opcional não
  fornecido — nunca grava esse campo como propriedade com valor `null`
