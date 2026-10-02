## MODIFIED Requirements

### Requirement: Janela, regime e cobertura da faixa medida na aba

O sistema SHALL apresentar, no topo da aba, a **janela efetivamente consultada** e
o instante de início do regime que governa os números da aba, e SHALL tomar esses
dois valores da **resposta**, não de cálculo local, sempre que a resposta tiver
chegado.

O operador SHALL poder escolher o período entre as mesmas opções já oferecidas na
página do escopo do sistema, e a janela SHALL ser recalculada no instante da
consulta, nunca no instante do render.

**O período da aba SHALL vir do endereço, e não de estado local da aba.** O
sistema SHALL lê-lo do endereço da página de detalhe e SHALL escrevê-lo ali quando
o operador o troca, com o mesmo vocabulário que a página do escopo do sistema usa:
o **nome** da janela, ausência do parâmetro como forma canônica do padrão, valor
não reconhecido caindo no padrão, e **nenhuma reescrita do endereço** em nenhum dos
dois casos.

**O período SHALL conviver com a identificação da aba no mesmo endereço**, e
nenhum dos dois SHALL apagar o outro.

**A janela SHALL sobreviver à travessia do ranking do escopo do sistema até esta
aba.** Quando o operador chega aqui por aquele acionamento, a aba SHALL abrir na
janela em que o ranking estava, e SHALL NOT abrir na janela padrão: a decisão de
manter as métricas por agente apenas nesta superfície se apoia na profundidade
estar a **um acionamento**, e um acionamento que troca a janela de medição em
silêncio entrega a superfície certa respondendo outra pergunta.

Quando a janela pedida começa **antes** do início da medição, o sistema SHALL
declarar quantos dos dias pedidos têm medida e quantos não existem, e SHALL
afirmar que os dias sem medida **não são dias sem uso**.

A cobertura por dia da semana SHALL ser decidida pela **série diária**: dia da
semana que ocorre entre os dias medidos e não tem ocorrência SHALL aparecer com
`0`; dia da semana que não ocorre entre os dias medidos SHALL aparecer sem
número, e SHALL NOT receber `0`.

A distinção entre o dia medido e vazio e o dia não medido SHALL ser legível na
**apresentação do próprio valor**, e SHALL NOT depender de texto explicativo ao
pé do agrupamento.

#### Scenario: A janela vem da resposta
- **WHEN** a resposta da aba chega
- **THEN** o período apresentado no topo é o que a resposta ecoa, e o fuso usado
  para formatá-lo é o que ela declara

#### Scenario: O endereço decide o período da aba
- **WHEN** a aba é aberta num endereço que identifica a aba e um período
  reconhecido
- **THEN** a aba abre nesse período e consulta a rota agregada do escopo do agente
  com a janela correspondente

#### Scenario: Aba sem período no endereço abre no padrão
- **WHEN** a aba é aberta num endereço que identifica a aba e **não** identifica
  período
- **THEN** a aba abre no período padrão, e o endereço **não** é reescrito

#### Scenario: Período não reconhecido na aba abre no padrão
- **WHEN** a aba é aberta num endereço cuja identificação de período não
  corresponde a nenhuma das oferecidas
- **THEN** a aba abre no período padrão, sem quebrar e sem exibir erro
- **AND** o endereço **não** é reescrito

#### Scenario: Trocar de período na aba escreve o endereço
- **WHEN** o operador escolhe outro período dentro da aba
- **THEN** o endereço passa a identificar o período escolhido, e continua
  identificando a aba

#### Scenario: A janela atravessa do ranking até a aba
- **WHEN** o operador escolhe um período na página do escopo do sistema e aciona
  o nome de um agente no ranking de consumo
- **THEN** a aba de Insights daquele agente abre ativa **e** no período escolhido,
  consultando a rota agregada do escopo do agente com aquela janela
- **AND** **nenhuma** consulta é feita com a janela do período padrão

#### Scenario: Janela maior que a medição é declarada
- **WHEN** a janela pedida começa antes do início do regime que governa a aba
- **THEN** a aba declara quantos dias têm medida e quantos não existem, e afirma
  que os que não existem **não** são dias sem uso

#### Scenario: Dia da semana fora da faixa medida não recebe zero
- **WHEN** um dia da semana não ocorre entre os dias medidos da janela
- **THEN** ele aparece sem número, e **nenhum** `0` é apresentado para ele

#### Scenario: A distinção não depende de prosa
- **WHEN** a janela cobre um dia da semana sem ocorrência e deixa outro fora da
  faixa medida
- **THEN** os dois são distinguíveis pela apresentação do valor — `0` num, nada
  no outro —, sem que nenhum texto ao pé do agrupamento precise explicá-la
