## MODIFIED Requirements

### Requirement: Worker Service mínimo
O sistema SHALL expor, em `apps/workers`, um projeto .NET Worker Service
mínimo, executável como serviço em background, servindo como base para
consumir o RabbitMQ e executar agentes via Microsoft Agent Framework. Antes
de iniciar o processamento, o worker SHALL validar que o fuso horário
resolvido pelo sistema operacional corresponde exatamente ao valor
declarado na variável de ambiente `TZ`, recusando a inicialização caso não
corresponda (variável ausente, vazia, ou com valor que não resolve para o
fuso esperado) — sem permitir que o processo suba silenciosamente operando
em um fuso diferente do declarado.

#### Scenario: Worker inicia e permanece em execução
- **WHEN** o Worker Service de `apps/workers` é iniciado via `dotnet run`
- **THEN** o processo sobe sem erros e permanece em execução até ser encerrado, registrando log de início do host

#### Scenario: Worker não inicia sem TZ definida
- **WHEN** o processo de `apps/workers` é iniciado sem a variável de
  ambiente `TZ` definida
- **THEN** o processo falha a inicialização com um erro explícito, em vez
  de subir normalmente operando no fuso que o sistema operacional
  escolher por padrão

#### Scenario: Worker não inicia com TZ definida para um valor que não resolve corretamente
- **WHEN** o processo de `apps/workers` é iniciado com `TZ` definida para
  um valor que o sistema operacional não reconhece como um fuso horário
  válido, ou que resolve para um fuso diferente do declarado
- **THEN** o processo falha a inicialização com um erro explícito
  apontando o valor declarado e o fuso efetivamente resolvido, em vez de
  subir silenciosamente operando em um fuso não declarado

#### Scenario: Worker inicia normalmente com TZ corretamente configurada e registra o fuso resolvido
- **WHEN** o processo de `apps/workers` é iniciado com `TZ` definida para
  um fuso horário válido reconhecido pelo sistema operacional, cujo
  identificador resolvido corresponde exatamente ao valor declarado
- **THEN** o processo inicia normalmente e registra, no log de
  inicialização, o identificador do fuso resolvido e o offset atual
