# agent-rejection-metrics Specification

## Purpose

Cobre a **coleta do motivo das recusas feitas por `apps/api`** — as que acontecem
**antes** de qualquer publicação de job, quando a task nem chega a ser executada.

É a fonte da métrica **M29** do catálogo, e ela não existia: quatro causas distintas
colapsavam num único estado `rejected`, sem coluna de motivo em lugar algum. Uma
contagem de recusas derivada das tabelas de execução não as vê, porque a linha de
execução nasce no consumo pelo worker.

O que esta capability fixa, e a ordem importa:

1. **quando a linha nasce** — uma por task recusada, gravada **depois** do estado
   terminal, e nunca no lugar de uma linha de execução, que descreveria uma execução
   que não houve;
2. **o que o motivo é** — valor de vocabulário fechado, gravado como texto e
   determinado pelo **ponto do código** que decidiu a recusa, nunca pelo texto de
   uma mensagem, que é de quem a escreve e muda sem aviso;
3. **o que a coleta não pode custar** — falha ao gravar a métrica não altera o estado
   em que a task terminou. Métrica que muda o resultado do que ela mede não é
   métrica;
4. **o que a linha deliberadamente não carrega** — provedor e modelo, porque duas
   das causas de recusa são justamente a ausência deles, e uma coluna preenchida em
   parte dos casos seria lida como distribuição.

A **exposição** desses números é de `system-insights-aggregation` e
`agent-insights-aggregation`; aqui fica só a coleta.

## Requirements
### Requirement: Linha de recusa para toda task recusada antes de publicar job

`apps/api` SHALL gravar uma linha por task que recusar antes de publicar job de
execução, com a task, o agente, o motivo e o instante da recusa.

A linha SHALL ser gravada **depois** de a task ter sido transicionada para o
estado terminal de recusa, e nunca antes: métrica que muda o resultado do que ela
mede não é métrica.

A task recusada SHALL continuar sem produzir linha em `task_executions` — a
execução não aconteceu, e inventar uma linha de execução para ela contaria uma
execução que não houve.

#### Scenario: Recusa por agente inativo produz linha
- **WHEN** um `SendMessage` é recusado porque o agente está inativo
- **THEN** existe exatamente uma linha de recusa com o `taskId` dela, o agente, o
  motivo da inatividade e o instante da recusa
- **AND** não existe nenhuma linha em `task_executions` com aquele `taskId`

#### Scenario: Recusa por provedor ou modelo ausentes produz linha
- **WHEN** um `SendMessage` é recusado porque o provedor ou o modelo do agente
  estão nulos
- **THEN** existe exatamente uma linha de recusa com o motivo da configuração
  ausente, distinto do motivo de agente inativo

#### Scenario: Recusa por provedor não configurado no ambiente produz linha
- **WHEN** um `SendMessage` é recusado porque o provedor do agente não está
  configurado no ambiente de `apps/api`
- **THEN** existe exatamente uma linha de recusa com o motivo do provedor não
  configurado, distinto dos outros dois

#### Scenario: Agente que não existe mais não é gravado como inativo
- **WHEN** um `SendMessage` chega para um agente cujo registro já não está no
  catálogo
- **THEN** a linha de recusa traz o motivo de agente não encontrado, e **não** o
  de agente inativo

#### Scenario: Task aceita não produz linha de recusa
- **WHEN** um `SendMessage` é aceito e o job é publicado
- **THEN** não existe nenhuma linha de recusa com o `taskId` dela

### Requirement: O motivo da recusa é a causa no código, com vocabulário fechado gravado como texto

O motivo SHALL ser um valor de **vocabulário fechado**, gravado como **texto** e
nunca como ordinal, determinado pelo **ponto do código** que decidiu a recusa.

O motivo SHALL NOT ser derivado do texto de nenhuma mensagem — nem de exceção, nem
da mensagem de status do protocolo, nem de prosa destinada ao cliente. Texto de
mensagem é de quem o escreve e muda sem aviso; a causa no código é estável por
construção.

A coluna do motivo SHALL ser obrigatória: não existe linha de recusa sem motivo,
porque a linha só nasce num dos pontos que decidem a recusa.

#### Scenario: Valor gravado pertence ao vocabulário
- **WHEN** qualquer task é recusada por `apps/api`
- **THEN** o motivo gravado é um dos valores do vocabulário fechado, e nenhum
  outro valor aparece na coluna

#### Scenario: O motivo é texto no banco
- **WHEN** o schema da tabela de recusas é inspecionado
- **THEN** a coluna do motivo é de tipo textual e obrigatória, e **não** é numérica

### Requirement: Falha ao gravar a métrica não altera a recusa

A gravação da linha de recusa SHALL NOT lançar para quem a chama, e uma falha nela
SHALL produzir registro em log com o identificador da task, sem alterar o estado
em que a task terminou nem fazer o job ser publicado.

#### Scenario: Gravação indisponível não muda o estado da task
- **WHEN** a gravação da linha de recusa falha
- **THEN** a task continua no estado de recusa, nenhum job é publicado, e a falha
  aparece em log

### Requirement: A recusa não é atribuída a provedor nem a modelo

A linha de recusa SHALL NOT carregar provedor nem modelo.

Das causas de recusa, duas acontecem **por não haver** provedor ou modelo válidos
para atribuir, e gravar o valor lido do agente nas outras criaria uma coluna
preenchida em parte dos casos, que qualquer agregação leria como distribuição.

#### Scenario: Nenhuma atribuição de provedor na recusa
- **WHEN** uma task é recusada por qualquer uma das causas
- **THEN** a linha de recusa não tem provedor nem modelo, e nenhuma agregação de
  recusa por provedor ou por modelo é possível a partir dela
