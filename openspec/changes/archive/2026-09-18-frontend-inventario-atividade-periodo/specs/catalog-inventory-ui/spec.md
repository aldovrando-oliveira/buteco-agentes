## MODIFIED Requirements

### Requirement: Inventário dos catálogos como entrada do painel

O sistema SHALL prover, em `apps/frontend`, uma tela de inventário que apresente
**um item para cada catálogo consumido pelo painel** e **um item para cada
contagem de atividade num período** — sessões iniciadas e mensagens de entrada
recebidas.

Cada item de catálogo SHALL apresentar o nome do catálogo e a contagem de
registros dele. Cada item de atividade SHALL apresentar o nome da contagem, a
contagem e o período a que ela se refere.

A rota raiz da aplicação SHALL levar a essa tela.

Cada contagem SHALL ter **uma única apuração**. Duas apurações do mesmo fato
divergiriam entre si, e a tela não teria como dizer qual está certa:

- a contagem de um catálogo SHALL ser derivada da mesma consulta que alimenta a
  listagem daquele catálogo, e o sistema SHALL NOT introduzir consulta, rota ou
  contagem paralela para ela;
- a contagem de atividade SHALL ser a que a rota agregada de `apps/inbox` devolve
  para o período. O sistema SHALL NOT recontá-la no cliente, nem derivá-la de
  outra rota ou de outra contagem — por exemplo, somando as sessões listadas por
  canal.

Um item SHALL oferecer atalho quando existe uma listagem cujo conjunto de
registros é **exatamente** o que a contagem dele conta, e SHALL NOT oferecer
atalho quando essa listagem não existe. Um atalho para uma listagem de outro
recorte levaria o operador a um número diferente do que o item afirmava.

#### Scenario: Cada catálogo aparece com a sua contagem
- **WHEN** o operador visualiza o inventário com os catálogos respondendo
- **THEN** cada catálogo consumido pelo painel aparece uma vez, com o seu nome e
  a sua contagem de registros

#### Scenario: Cada contagem de atividade aparece com a sua contagem
- **WHEN** o operador visualiza o inventário com as contagens de atividade
  respondendo
- **THEN** a contagem de sessões iniciadas e a de mensagens recebidas aparecem
  uma vez cada, com o seu nome e a sua contagem

#### Scenario: O atalho leva à listagem daquele catálogo
- **WHEN** o operador aciona o atalho do item de um catálogo
- **THEN** a aplicação navega para a listagem daquele catálogo

#### Scenario: O item de atividade não apresenta atalho
- **WHEN** o operador visualiza o item de uma contagem de atividade, em qualquer
  das situações de contagem
- **THEN** o item não apresenta atalho para nenhuma listagem

#### Scenario: A unidade acompanha a contagem em número
- **WHEN** o operador visualiza um item cuja contagem é exatamente um
- **THEN** a unidade que acompanha a contagem é apresentada no singular

#### Scenario: A rota raiz leva ao inventário
- **WHEN** o operador acessa a aplicação pela URL raiz
- **THEN** a aplicação apresenta o inventário, sem exibir uma página vazia

#### Scenario: A contagem do inventário e a da listagem não divergem
- **WHEN** o operador vê a contagem de um catálogo no inventário e navega para a
  listagem desse catálogo
- **THEN** a quantidade de registros apresentada na listagem corresponde à
  contagem que o inventário exibia

#### Scenario: A contagem de atividade é a da rota agregada
- **WHEN** a rota agregada de `apps/inbox` responde uma contagem para o período
- **THEN** o item de atividade apresenta exatamente essa contagem, e nenhuma
  listagem de sessões ou de mensagens é consultada para produzi-la

### Requirement: Proveniência da contagem de cada catálogo

O sistema SHALL distinguir, para cada item do inventário — de catálogo ou de
atividade —, **quatro** situações, e SHALL NOT apresentar qualquer uma delas com
a forma de outra:

1. **contagem apurada, maior que zero** — a consulta respondeu com registros;
2. **contagem apurada que deu zero** — a consulta respondeu e não há registros;
3. **contagem desconhecida** — a consulta não respondeu, e nada foi apurado;
4. **consulta em andamento** — ainda não há resposta.

A contagem apurada que deu zero SHALL ser afirmada como ausência de registros, e
SHALL NOT ser apresentada como contagem desconhecida: a consulta aconteceu e "não
há" é um fato medido. No item de atividade, a ausência é **no período**, e SHALL
ser dita como tal — nenhuma sessão iniciada, nenhuma mensagem recebida.

A contagem desconhecida SHALL NOT ser apresentada como zero nem como ausência de
registros, e SHALL ser acompanhada da razão pela qual não foi possível apurá-la.
Sem a razão, o símbolo de desconhecido é lido como zero.

Enquanto a consulta de um item estiver em andamento, o sistema SHALL NOT afirmar
contagem nem indisponibilidade para aquele item.

#### Scenario: Catálogo vazio afirma ausência, não desconhecimento
- **WHEN** o operador visualiza o inventário e a consulta de um catálogo responde
  sem nenhum registro
- **THEN** o item daquele catálogo afirma que não há registros, e não apresenta o
  símbolo de contagem desconhecida

#### Scenario: Catálogo vazio não apresenta a contagem como o algarismo zero
- **WHEN** o operador visualiza o item de um catálogo cuja consulta respondeu sem
  nenhum registro
- **THEN** o item não apresenta nenhuma contagem em número, e a ausência é dita
  por extenso

#### Scenario: Período sem atividade afirma ausência por extenso
- **WHEN** o operador visualiza o item de uma contagem de atividade cuja rota
  respondeu zero para o período
- **THEN** o item afirma por extenso que nada ocorreu no período, sem apresentar o
  algarismo zero nem o símbolo de contagem desconhecida

#### Scenario: Consulta que não respondeu não vira zero
- **WHEN** o operador visualiza o inventário e a consulta de um item não responde
- **THEN** aquele item não apresenta contagem alguma, nem afirma ausência de
  registros

#### Scenario: A indisponibilidade diz a razão
- **WHEN** um item está com a contagem desconhecida
- **THEN** o item informa por que não foi possível apurar a contagem

#### Scenario: Consulta em andamento não afirma nem contagem nem falha
- **WHEN** o operador visualiza o inventário enquanto a consulta de um item ainda
  está em andamento
- **THEN** aquele item não apresenta contagem, não afirma ausência de registros e
  não afirma indisponibilidade

### Requirement: Falha e nova tentativa independentes por catálogo

O sistema SHALL tratar a consulta de cada item do inventário — de catálogo ou de
atividade — de forma independente das demais: o estado de uma consulta SHALL NOT
determinar o que é apresentado para os outros itens. Isso vale também entre os
dois itens de atividade, que consultam o mesmo processo e ainda assim podem estar
em situações diferentes.

Um item cuja contagem esteja desconhecida SHALL oferecer uma nova tentativa, e
essa nova tentativa SHALL refazer **apenas** a consulta daquele item.

Enquanto a nova tentativa estiver em andamento, o item SHALL apresentar a
situação de consulta em andamento, e os demais itens SHALL permanecer inalterados.

#### Scenario: A falha de um catálogo não apaga os outros
- **WHEN** o operador visualiza o inventário com a consulta de um item falhando e
  as demais respondendo
- **THEN** os itens que responderam continuam apresentando as suas contagens, e
  apenas o que falhou fica com a contagem desconhecida

#### Scenario: Os dois itens de atividade não se arrastam
- **WHEN** a contagem de um item de atividade responde e a do outro falha
- **THEN** o que respondeu apresenta a sua contagem, e apenas o que falhou fica
  com a contagem desconhecida

#### Scenario: A consulta em andamento de um catálogo não segura os outros
- **WHEN** o operador visualiza o inventário e a consulta de um item demora mais
  que as demais
- **THEN** os itens que já responderam apresentam as suas contagens, sem esperar
  pelo que ainda está em andamento

#### Scenario: A nova tentativa refaz só a consulta daquele catálogo
- **WHEN** o operador aciona a nova tentativa num item com contagem desconhecida
- **THEN** apenas a consulta daquele item é refeita, e os demais itens continuam
  apresentando o que já apresentavam

#### Scenario: A nova tentativa mostra a consulta em andamento
- **WHEN** o operador aciona a nova tentativa e a consulta ainda não respondeu
- **THEN** aquele item apresenta a situação de consulta em andamento, em vez da
  contagem desconhecida

### Requirement: O inventário não reproduz a explicação das listagens

Cada item de catálogo SHALL apresentar o nome do catálogo, a sua situação de
contagem e o atalho para a listagem. Cada item de atividade SHALL apresentar o
nome da contagem, a sua situação de contagem e o período. **Nenhum** item SHALL
apresentar texto explicativo além disso — nem o que a listagem de um catálogo
apresenta sobre o que ele é ou para que serve, nem prosa própria sobre o que uma
contagem de atividade significa.

A explicação de cada catálogo tem um lugar só, que é a sua listagem. Repeti-la no
inventário criaria uma segunda superfície a manter verdadeira, que envelheceria
sem que a tela mudasse. A contagem de atividade não tem listagem; o que a
distingue de uma contagem parecida é o nome e o período, que já estão no item.

A verificação que protege este requisito SHALL ser **estrutural** — afirmar que o
inventário não reproduz o texto explicativo de nenhuma listagem, e que o item de
atividade não carrega texto além do nome, da situação de contagem e do período —,
e SHALL NOT fixar o teor atual do texto de nenhuma listagem, o que faria dela uma
segunda fonte de verdade daquele texto.

#### Scenario: O item do catálogo não repete a explicação da listagem
- **WHEN** o operador visualiza o inventário
- **THEN** nenhum item reproduz o texto explicativo que a listagem do seu
  catálogo apresenta

#### Scenario: O item de atividade não carrega prosa explicativa
- **WHEN** o operador visualiza o item de uma contagem de atividade
- **THEN** o item apresenta apenas o nome da contagem, a sua situação de contagem
  e o período

## ADDED Requirements

### Requirement: A contagem de atividade declara a sua janela

Cada item de atividade SHALL contar o que ocorreu nos **últimos 7 dias**: um
período rolante que termina no instante da consulta e começa 7 × 24 horas antes
dele. O período SHALL ser o mesmo para os dois itens de atividade, e SHALL NOT
ser escolhido pelo operador.

O período SHALL ser apresentado **em todas as situações de contagem** do item —
contagem apurada, zero medido, contagem desconhecida e consulta em andamento. O
período é propriedade da pergunta, não da resposta. Sem ele, um número de
atividade é ambíguo, e um "não sei" fica sem objeto.

O fim do período SHALL ser o instante em que a consulta é feita. Uma nova
tentativa ou um novo carregamento SHALL consultar o período que termina no
instante dessa nova consulta, e não repetir um período calculado antes.

#### Scenario: O item de atividade declara a janela junto da contagem
- **WHEN** o operador visualiza o item de uma contagem de atividade com a
  consulta respondida
- **THEN** o item apresenta, junto da contagem, o período a que ela se refere

#### Scenario: A janela aparece mesmo sem contagem
- **WHEN** o item de uma contagem de atividade está com a consulta em andamento
  ou com a contagem desconhecida
- **THEN** o item continua apresentando o período a que a consulta se refere

#### Scenario: A janela acompanha o zero medido
- **WHEN** o item de uma contagem de atividade afirma que nada ocorreu no período
- **THEN** o item apresenta o período junto dessa afirmação

#### Scenario: O período consultado são os 7 dias que terminam na consulta
- **WHEN** o inventário consulta uma contagem de atividade
- **THEN** o período enviado à rota termina no instante da consulta e começa
  exatamente 7 × 24 horas antes

#### Scenario: A nova tentativa consulta o período atualizado
- **WHEN** o operador aciona a nova tentativa num item de atividade algum tempo
  depois da consulta que falhou
- **THEN** o período enviado à rota termina no instante da nova tentativa, e não
  no da consulta anterior
