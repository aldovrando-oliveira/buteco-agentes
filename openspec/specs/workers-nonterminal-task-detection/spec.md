# workers-nonterminal-task-detection Specification

## Purpose

Cobre a **varredura periódica de `apps/workers`** sobre o store durável de
tasks: encontrar as tasks deixadas em estado **não-terminal** (`Submitted` ou
`Working`) por mais tempo que uma janela, e registrar o que foi encontrado — a
contagem por estado, a idade da mais velha e a própria janela usada na medição.

A capability existe por uma pergunta de operação que nenhuma outra responde:
**quantas conversas estavam delegando ao mesmo tempo?** É a série dessas
leituras — nunca uma leitura isolada — que torna esse número observável, e é
dele que depende a decisão entre aumentar a capacidade de `apps/workers` e
redesenhar a retomada da delegação.

Três coisas que esta capability afirma e que são decisão, não detalhe:

- **A janela é derivada, não escolhida.** Ela é o timeout de espera da tool de
  delegação, lido da mesma configuração que a própria tool usa, de modo que toda
  task reportada já sobreviveu a uma espera de delegação inteira e que mudar o
  timeout mova a janela junto, sem recalibração manual. Por isso o registro
  carrega a janela ao lado do resultado: uma contagem sem a janela não diz sobre
  o quê foi medido.

- **A contagem é separada por estado, nunca um total único.** `Submitted`
  envelhecida é task publicada e **nunca consumida** por instância nenhuma;
  `Working` envelhecida é task **consumida e ainda em execução**. São duas
  causas diferentes, e um total único apagaria justamente a distinção que
  motivou a varredura.

- **A varredura se anuncia ao subir**, com a janela e o intervalo em vigor, para
  que a ausência de achados seja distinguível da ausência da própria varredura —
  nenhum registro é, sozinho, prova de que nada foi encontrado.

E uma coisa que ela recusa: **afirmar que a task está travada.** O registro
descreve o que foi observado — estado, idade e janela — e não emite veredito,
porque um turno de agente que encadeia várias chamadas de tool de delegação
ultrapassa a janela legitimamente, e o sistema não tem como separar isso de uma
task de fato presa.

**Não cobre** o registro de desistência da própria tool de delegação, que
pertence a `agent-delegation-execution`, nem métrica persistida, rota de
agregação ou tela.

## Requirements

### Requirement: Detecção periódica de task em estado não-terminal envelhecida
`apps/workers` SHALL varrer periodicamente o store durável de tasks e
registrar as tasks em estado **não-terminal** (`Submitted` ou `Working`)
cuja última transição de estado seja mais antiga que uma janela
configurada, reportando a **contagem por estado**, a idade da mais velha e
a própria janela usada na medição.

A varredura SHALL reportar a contagem separada por estado, e não um total
único: `Submitted` envelhecida significa task publicada e **nunca
consumida** por instância nenhuma, e `Working` envelhecida significa task
**consumida e ainda em execução** — duas causas diferentes que um total
único apagaria.

#### Scenario: Task Submitted mais velha que a janela é reportada como nunca consumida
- **WHEN** existe no store uma task em `Submitted` cuja última transição
  de estado é mais antiga que a janela, e a varredura roda
- **THEN** o registro produzido conta essa task sob o estado `Submitted`,
  separada de qualquer contagem de `Working`

#### Scenario: Task Working mais velha que a janela é reportada como em execução
- **WHEN** existe no store uma task em `Working` cuja última transição de
  estado é mais antiga que a janela, e a varredura roda
- **THEN** o registro produzido conta essa task sob o estado `Working`,
  separada de qualquer contagem de `Submitted`

#### Scenario: Store povoado sem nenhuma task envelhecida não reporta nada
- **WHEN** o store contém tasks em estado terminal e tasks não-terminais
  **mais novas** que a janela, e nenhuma task não-terminal mais velha que
  ela
- **THEN** a varredura não reporta nenhuma task, e o faz tendo de fato
  comparado linhas — não por o store estar vazio

#### Scenario: Task em estado terminal nunca é reportada, por mais velha que seja
- **WHEN** o store contém uma task em estado terminal (`Completed`,
  `Failed`, `Rejected` ou `Canceled`) muito mais velha que a janela
- **THEN** a varredura não a reporta, porque estado terminal não é
  condição observável de espera

### Requirement: A janela da detecção é derivada do timeout de delegação
A janela SHALL ser o timeout de espera da tool de delegação, lido da mesma
configuração que a própria tool usa, e não um valor independente — de modo
que toda task reportada já tenha sobrevivido a uma espera de delegação
inteira, e de modo que mudar o timeout mova a janela junto, sem
recalibração manual.

O registro produzido SHALL carregar a janela usada, junto do resultado, na
mesma emissão — um número de tasks envelhecidas sem a janela ao lado não
diz sobre o quê foi medido.

#### Scenario: A janela acompanha o timeout de delegação configurado
- **WHEN** o timeout de espera da tool de delegação está configurado com
  um valor, e a varredura roda
- **THEN** a varredura usa exatamente esse valor como janela, e o registro
  produzido o carrega junto do resultado

### Requirement: A detecção não afirma que a task está travada
O registro produzido SHALL descrever o que foi observado — estado, idade e
janela — e NÃO SHALL afirmar que a task está travada, presa ou com defeito.
Um turno de agente que encadeia várias chamadas de tool de delegação
ultrapassa a janela legitimamente, e o sistema não tem como distinguir isso
de uma task travada.

#### Scenario: O registro descreve a observação, não um veredito
- **WHEN** a varredura encontra tasks não-terminais mais velhas que a
  janela
- **THEN** o registro nomeia estado, idade e janela, e não classifica a
  task como travada, presa ou defeituosa

### Requirement: A presença da varredura é verificável sem esperar por um achado
`apps/workers` SHALL registrar, ao iniciar a varredura, a janela e o
intervalo em vigor, de forma que a ausência de achados seja distinguível da
ausência da própria varredura.

#### Scenario: O início da varredura é registrado com janela e intervalo
- **WHEN** o processo de `apps/workers` sobe
- **THEN** há um registro declarando que a varredura está ativa, com a
  janela e o intervalo em vigor, antes de qualquer achado

### Requirement: Falha da varredura não derruba o processo
A varredura SHALL capturar as próprias falhas por unidade de trabalho —
inclusive a falha da consulta ao banco — registrando-as e seguindo para o
próximo ciclo, sem derrubar o processo e sem interromper o consumo da fila
de tasks.

#### Scenario: Consulta que falha não interrompe a varredura nem o processo
- **WHEN** a consulta da varredura falha num ciclo
- **THEN** a falha é registrada, o processo continua de pé, o consumo da
  fila de tasks segue normal, e o ciclo seguinte da varredura acontece
