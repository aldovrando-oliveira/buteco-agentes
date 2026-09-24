## MODIFIED Requirements

### Requirement: O balde, a janela e os regimes são os mesmos dos dois escopos

O sistema SHALL agrupar por dia, por dia da semana e por calendário usando o **dia
no fuso configurado**, e NÃO SHALL usar o dia em UTC, exatamente como no escopo do
sistema.

O sistema SHALL interpretar limite sem deslocamento de fuso como UTC e SHALL
normalizar para deslocamento zero **todo** instante que chegue ao driver do banco
— inclusive os que vêm de **configuração**, e não apenas os que vêm da query
string. Um valor de configuração entra por um caminho que a interpretação dos
limites não cobre, e o driver recusa deslocamento diferente de zero para coluna de
instante.

O sistema SHALL devolver os instantes de início de medição como **mapa de
regimes**, e a série diária SHALL omitir — nunca emitir `0` para — os dias
anteriores ao início do regime a que cada métrica pertence.

**Dentro do regime, um dia sem ocorrência para o agente consultado SHALL emitir
`0`.** A série diária SHALL cobrir todos os dias entre o início efetivo da
medição na janela e o fim efetivo dela, sem buracos, e a ausência de um dia SHALL
significar **uma** coisa: aquele dia não foi medido. Um dia em que o sistema
mediu e o agente consultado não executou nada é um dia **medido**, e o `0` dele é
contagem feita.

O fim efetivo da janela SHALL ser o mais cedo entre o limite pedido e o instante
da consulta, e a série SHALL NOT conter dias posteriores ao instante da consulta.

**O mesmo tratamento SHALL valer para a agregação por dia da semana:** dia da
semana que ocorre entre os dias medidos chega com a sua contagem ou com `0`; dia
da semana que não ocorre entre os dias medidos é **omitido**.

O sistema SHALL NOT eleger dia da semana de pico quando nenhuma ocorrência do
agente foi medida no período.

O contador de tokens de um dia SHALL permanecer anulável e SHALL chegar **nulo**
no dia medido e sem ocorrência.

Estas regras SHALL ser as **mesmas** do escopo do sistema, e o escopo do agente
SHALL NOT ter comportamento próprio para elas. As duas rotas têm **consultas
distintas**, e é por isso que a regra precisa estar escrita nas duas capabilities
e guardada nas duas suítes: o verde de uma não cobre a outra.

#### Scenario: Instante noturno cai no dia local correto também por agente

- **WHEN** existe execução do agente cujo instante, no fuso configurado, é de um
  dia, e em UTC é do dia seguinte
- **THEN** ela é contada no dia local, e o guarda reprova se o agrupamento for
  feito em UTC

#### Scenario: Instante de regime com deslocamento não vira erro interno

- **WHEN** o início de regime configurado carrega deslocamento de fuso diferente de
  zero e é ele que delimita a consulta, por ser posterior ao início pedido
- **THEN** a resposta é `200`, e não um erro interno

#### Scenario: Janela que começa antes do regime, no escopo do agente

- **WHEN** a janela pedida para um agente começa antes do início do regime de uma
  métrica
- **THEN** os dias anteriores ao início não aparecem na série dessa métrica, e o
  cliente consegue distinguir esses dias dos dias medidos e vazios

#### Scenario: Dia medido sem ocorrência do agente aparece com zero

- **WHEN** a janela pedida para um agente está inteiramente dentro do regime e
  contém um dia em que aquele agente não executou nada, entre dias em que executou
- **THEN** esse dia aparece na série do agente com contagem `0`, e a série não tem
  buraco

#### Scenario: Dia em que só outro agente executou continua sendo dia medido

- **WHEN** num dia da janela houve execução de outro agente e nenhuma do agente
  consultado
- **THEN** a série do agente consultado traz esse dia com `0`, e não o omite

#### Scenario: Dia posterior ao instante da consulta não entra na série do agente

- **WHEN** a janela pedida para um agente termina depois do instante da consulta
- **THEN** a série termina no dia da consulta, e nenhum dia posterior a ele
  aparece com `0`

#### Scenario: Dia da semana sem ocorrência do agente chega com zero

- **WHEN** um dia da semana ocorre entre os dias medidos e o agente consultado não
  executou nada nele
- **THEN** ele aparece na agregação por dia da semana do agente com contagem `0`

#### Scenario: Dia da semana que não ocorre entre os dias medidos é omitido

- **WHEN** a faixa medida é curta demais para conter algum dia da semana
- **THEN** esse dia da semana não aparece na agregação do agente, e nenhum `0` é
  emitido para ele

#### Scenario: Agente sem nenhuma ocorrência no período não tem dia de pico

- **WHEN** a janela pedida está inteiramente dentro do regime e o agente não
  executou nada nela
- **THEN** o dia da semana de pico chega nulo, e nenhum dia da semana é eleito a
  partir de contagens todas iguais a zero
