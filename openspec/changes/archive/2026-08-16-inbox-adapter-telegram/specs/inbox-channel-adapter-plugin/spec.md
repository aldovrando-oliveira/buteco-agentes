## ADDED Requirements

### Requirement: Provisionamento automático de configuração externa, opcional por adapter
O sistema SHALL suportar um quarto contrato de plugin, de implementação
opcional por `ChannelType` (ao contrário dos três contratos de
validação de credencial, envio e recepção de webhook, que SHALL
continuar obrigatórios em conjunto para todo adapter registrado). Um
adapter que registra esse contrato de provisionamento sob um dado
`ChannelType` SHALL, obrigatoriamente, também ter os três contratos
existentes registrados sob o mesmo `ChannelType` — o provisionamento
nunca substitui os contratos base. A checagem de composição do processo,
executada uma única vez no startup, SHALL identificar um `ChannelType`
que registra o contrato de provisionamento sem os três contratos
obrigatórios, impedindo a inicialização do processo. A ausência do
contrato de provisionamento para um `ChannelType` que tem os três
contratos obrigatórios SHALL ser aceita normalmente, sem impedir a
inicialização do processo.

#### Scenario: ChannelType com os três contratos obrigatórios e sem provisionamento inicializa normalmente
- **WHEN** o processo inicializa com um `ChannelType` que registra
  `IChannelConfigValidator`, `IOutboundMessageSender` e
  `IInboundWebhookHandler`, mas não registra o contrato de
  provisionamento
- **THEN** o processo inicializa normalmente, sem erro de composição

#### Scenario: ChannelType com os três contratos obrigatórios e com provisionamento inicializa normalmente
- **WHEN** o processo inicializa com um `ChannelType` que registra os
  três contratos obrigatórios e também o contrato de provisionamento
- **THEN** o processo inicializa normalmente, sem erro de composição

#### Scenario: ChannelType com provisionamento mas sem os três contratos obrigatórios impede a inicialização
- **WHEN** o processo inicializa com um `ChannelType` que registra o
  contrato de provisionamento, mas não tem os três contratos
  obrigatórios completos sob a mesma chave
- **THEN** o processo lança uma exceção na inicialização, identificando
  o `ChannelType` com composição incompleta
