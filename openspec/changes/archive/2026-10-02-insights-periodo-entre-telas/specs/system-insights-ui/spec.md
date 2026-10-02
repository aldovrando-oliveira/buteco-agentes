## MODIFIED Requirements

### Requirement: Janela do período escolhida pelo operador

O sistema SHALL oferecer ao operador a escolha do período e SHALL calcular a
partir dela os dois limites que a rota exige — não existe período implícito.

A janela SHALL ser **rolante em instantes** — N × 24 h terminando no instante da
consulta — e SHALL NOT ser de calendário, que obrigaria a decidir de qual fuso é
a meia-noite e mudaria de tamanho com o horário de verão.

A janela SHALL ser calculada **no instante da consulta**, e SHALL NOT fazer parte
da chave de cache: uma janela calculada a cada render geraria chave nova a cada
render, e a página ficaria permanentemente em carregamento.

**A escolha do período SHALL ser endereçável.** O sistema SHALL ler o período do
endereço da página e SHALL escrevê-lo ali quando o operador o troca, de forma que
o período sobreviva a um recarregamento e viaje num endereço compartilhado. O
período SHALL NOT ser mantido apenas em estado local da tela, que não alcança nem
o recarregamento nem o link colado.

**O que o endereço carrega SHALL ser o NOME da janela, não os seus limites.** Um
endereço com instantes absolutos **congelaria** a janela — compartilhado amanhã,
responderia sobre o mesmo intervalo —, e um endereço com o nome da janela
**desliza**: compartilhado amanhã, responde sobre os últimos N dias de amanhã. O
sistema SHALL responder a segunda pergunta, que é a que a janela rolante já
define, e SHALL NOT oferecer a primeira enquanto não houver escolha de limites na
interface.

**Ausência do parâmetro SHALL ser a forma canônica do período padrão**, e valor
não reconhecido SHALL cair no padrão também. Nos dois casos o sistema SHALL NOT
reescrever o endereço, que só poluiria o histórico de navegação — é o mesmo
contrato que a identificação da aba do detalhe do agente já declara, pelo mesmo
motivo.

**A passagem do ranking de agentes para a superfície de diagnóstico SHALL levar o
período em vigor.** Ela atravessa para um endereço diferente, onde o parâmetro não
é herdado por si: sem levá-lo, o acionamento único entregaria a superfície certa
medindo outra janela, e comparar o número de destino com o do ranking seria errado
sem que nada avisasse.

#### Scenario: Trocar de período refaz a consulta
- **WHEN** o operador escolhe outro período
- **THEN** a página consulta a rota com a janela correspondente e apresenta os
  números do novo período

#### Scenario: Nova tentativa consulta a janela atualizada
- **WHEN** o operador aciona nova tentativa depois de uma falha
- **THEN** a janela é recalculada no instante dessa tentativa

#### Scenario: A página não fica presa em carregamento
- **WHEN** a página é renderizada repetidamente sem troca de período
- **THEN** apenas uma consulta é feita para aquele período

#### Scenario: O endereço decide o período de abertura
- **WHEN** a página é aberta num endereço que identifica um período reconhecido
- **THEN** a página abre nesse período, consulta a rota com a janela
  correspondente, e recarregar o mesmo endereço reabre o mesmo período

#### Scenario: Trocar de período escreve o endereço
- **WHEN** o operador escolhe outro período
- **THEN** o endereço da página passa a identificar o período escolhido

#### Scenario: Endereço sem período abre no padrão
- **WHEN** a página é aberta num endereço que **não** identifica período
- **THEN** a página abre no período padrão, e o endereço **não** é reescrito

#### Scenario: Período não reconhecido abre no padrão
- **WHEN** a página é aberta num endereço cuja identificação de período não
  corresponde a nenhuma das oferecidas
- **THEN** a página abre no período padrão, sem quebrar e sem exibir erro
- **AND** o endereço **não** é reescrito

#### Scenario: O link do ranking carrega o período em vigor
- **WHEN** o operador escolhe um período e aciona o nome de um agente na tabela
  de consumo por agente
- **THEN** o endereço de destino identifica **esse** período, junto da
  identificação da superfície de diagnóstico

#### Scenario: NEGATIVO — o link não carrega o período padrão quando o período em vigor é outro
- **WHEN** o período em vigor na página **não** é o padrão
- **THEN** **nenhum** link do ranking identifica o período padrão no endereço de
  destino
