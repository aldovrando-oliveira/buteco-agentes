## MODIFIED Requirements

### Requirement: Período anterior ao regime é distinguível de período sem uso

A série diária SHALL omitir — nunca emitir `0` para — os dias anteriores ao
início do regime a que a métrica pertence. Dentro do regime, um dia sem ocorrência
SHALL emitir `0`.

É esta distinção que sustenta o estado de "período pedido maior que a medição" da
tela aprovada: sem ela, um período que antecede a coleta apareceria como um
período de inatividade real.

A série diária SHALL cobrir **todos** os dias entre o início efetivo da medição
na janela e o fim efetivo dela, sem buracos. A ausência de um dia na série SHALL
significar **uma** coisa: aquele dia não foi medido. Um consumidor SHALL conseguir
decidir o estado de um dia lendo a série, e SHALL NOT precisar cruzá-la com o
mapa de regimes para isso — o regime é configuração do servidor, e reconstruir no
cliente uma condição que o servidor já resolveu cria duas regras que divergem em
silêncio.

O fim efetivo da janela SHALL ser o mais cedo entre o limite pedido e o instante
da consulta. A série SHALL NOT conter dias posteriores ao instante da consulta:
emitir `0` para um dia que ainda não aconteceu afirma medição sobre o futuro, que
é o mesmo defeito que a omissão dos dias anteriores ao regime evita no passado.

**O mesmo tratamento SHALL valer para a agregação por dia da semana**, cuja
ausência hoje não distingue nada:

- um dia da semana que **ocorre** entre os dias medidos e teve ocorrência SHALL
  chegar com a sua contagem;
- um dia da semana que **ocorre** entre os dias medidos e não teve ocorrência
  SHALL chegar com `0`;
- um dia da semana que **não ocorre** entre os dias medidos SHALL ser **omitido**
  — nunca houve medição dele para dar zero.

O sistema SHALL NOT eleger dia da semana de pico quando nenhuma ocorrência foi
medida no período. Com a agregação por dia da semana passando a emitir `0`, uma
faixa medida sem nenhuma ocorrência produz contagens todas iguais a zero, e
eleger a primeira delas afirmaria um pico que não existe.

O contador de tokens de um dia SHALL permanecer anulável e SHALL chegar **nulo**
no dia medido e sem ocorrência. "Foram zero tasks" e "não há token a relatar" são
afirmações diferentes, e o `0` da contagem de tasks SHALL NOT ser propagado para
o contador de tokens.

#### Scenario: Janela que começa antes do regime

- **WHEN** a janela pedida começa antes do início do regime de uma métrica
- **THEN** os dias anteriores ao início não aparecem na série dessa métrica, e o
  cliente consegue distinguir esses dias dos dias medidos e vazios

#### Scenario: Dia medido e sem ocorrência aparece na série com zero

- **WHEN** a janela pedida está inteiramente dentro do regime e contém um dia sem
  nenhuma ocorrência, entre dias que tiveram
- **THEN** esse dia aparece na série com contagem `0`, e a série não tem buraco
  entre os dias que tiveram ocorrência

#### Scenario: O contador de tokens do dia vazio não vira zero

- **WHEN** um dia medido e sem ocorrência aparece na série
- **THEN** a contagem de tasks dele é `0` e o contador de tokens dele é nulo

#### Scenario: Dia posterior ao instante da consulta não entra na série

- **WHEN** a janela pedida termina depois do instante da consulta
- **THEN** a série termina no dia da consulta, e nenhum dia posterior a ele
  aparece com `0`

#### Scenario: Dia da semana medido e sem ocorrência chega com zero

- **WHEN** um dia da semana ocorre entre os dias medidos e não teve nenhuma
  ocorrência
- **THEN** ele aparece na agregação por dia da semana com contagem `0`

#### Scenario: Dia da semana que não ocorre entre os dias medidos é omitido

- **WHEN** a faixa medida é curta demais para conter algum dia da semana
- **THEN** esse dia da semana não aparece na agregação, e nenhum `0` é emitido
  para ele

#### Scenario: Período medido sem nenhuma ocorrência não tem dia de pico

- **WHEN** a janela pedida está inteiramente dentro do regime e não contém
  nenhuma ocorrência
- **THEN** o dia da semana de pico chega nulo, e nenhum dia da semana é eleito a
  partir de contagens todas iguais a zero
