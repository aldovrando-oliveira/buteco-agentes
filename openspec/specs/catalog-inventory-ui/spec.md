# catalog-inventory-ui Specification

## Purpose

A tela de entrada do painel: um item por catálogo que o painel consome, com a
contagem daquele catálogo, o estado da consulta que a produziu, e o atalho para a
listagem correspondente. Define a proveniência de cada contagem — os quatro
estados que não podem ser colapsados um no outro —, o comportamento independente
de falha e de nova tentativa por catálogo, e a recusa de reproduzir a explicação
que cada listagem já dá.

## Requirements

### Requirement: Inventário dos catálogos como entrada do painel

O sistema SHALL prover, em `apps/frontend`, uma tela de inventário que apresente
**um item para cada catálogo consumido pelo painel**, cada item com o nome do
catálogo, a contagem de registros dele e um atalho para a listagem
correspondente.

A rota raiz da aplicação SHALL levar a essa tela.

A contagem SHALL ser derivada da mesma consulta que alimenta a listagem daquele
catálogo. O sistema SHALL NOT introduzir consulta, rota ou contagem paralela para
esta tela — duas apurações do mesmo fato divergiriam entre si e a tela não teria
como dizer qual está certa.

#### Scenario: Cada catálogo aparece com a sua contagem
- **WHEN** o operador visualiza o inventário com os catálogos respondendo
- **THEN** cada catálogo consumido pelo painel aparece uma vez, com o seu nome e
  a sua contagem de registros

#### Scenario: O atalho leva à listagem daquele catálogo
- **WHEN** o operador aciona o atalho do item de um catálogo
- **THEN** a aplicação navega para a listagem daquele catálogo

#### Scenario: A unidade acompanha a contagem em número
- **WHEN** o operador visualiza o item de um catálogo que tem exatamente um
  registro
- **THEN** a unidade que acompanha a contagem é apresentada no singular

#### Scenario: A rota raiz leva ao inventário
- **WHEN** o operador acessa a aplicação pela URL raiz
- **THEN** a aplicação apresenta o inventário, sem exibir uma página vazia

#### Scenario: A contagem do inventário e a da listagem não divergem
- **WHEN** o operador vê a contagem de um catálogo no inventário e navega para a
  listagem desse catálogo
- **THEN** a quantidade de registros apresentada na listagem corresponde à
  contagem que o inventário exibia

### Requirement: Proveniência da contagem de cada catálogo

O sistema SHALL distinguir, para cada catálogo, **quatro** situações, e SHALL NOT
apresentar qualquer uma delas com a forma de outra:

1. **contagem apurada, maior que zero** — a consulta respondeu com registros;
2. **contagem apurada que deu zero** — a consulta respondeu e não há registros;
3. **contagem desconhecida** — a consulta não respondeu, e nada foi apurado;
4. **consulta em andamento** — ainda não há resposta.

A contagem apurada que deu zero SHALL ser afirmada como ausência de registros, e
SHALL NOT ser apresentada como contagem desconhecida: a consulta aconteceu e "não
há" é um fato medido.

A contagem desconhecida SHALL NOT ser apresentada como zero nem como ausência de
registros, e SHALL ser acompanhada da razão pela qual não foi possível apurá-la.
Sem a razão, o símbolo de desconhecido é lido como zero.

Enquanto a consulta de um catálogo estiver em andamento, o sistema SHALL NOT
afirmar contagem nem indisponibilidade para aquele catálogo.

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

#### Scenario: Consulta que não respondeu não vira zero
- **WHEN** o operador visualiza o inventário e a consulta de um catálogo não
  responde
- **THEN** o item daquele catálogo não apresenta contagem alguma, nem afirma
  ausência de registros

#### Scenario: A indisponibilidade diz a razão
- **WHEN** o item de um catálogo está com a contagem desconhecida
- **THEN** o item informa por que não foi possível apurar a contagem

#### Scenario: Consulta em andamento não afirma nem contagem nem falha
- **WHEN** o operador visualiza o inventário enquanto a consulta de um catálogo
  ainda está em andamento
- **THEN** o item daquele catálogo não apresenta contagem, não afirma ausência de
  registros e não afirma indisponibilidade

### Requirement: Falha e nova tentativa independentes por catálogo

O sistema SHALL tratar a consulta de cada catálogo de forma independente das
demais: o estado de uma consulta SHALL NOT determinar o que é apresentado para os
outros catálogos.

Um item cuja contagem esteja desconhecida SHALL oferecer uma nova tentativa, e
essa nova tentativa SHALL refazer **apenas** a consulta daquele catálogo.

Enquanto a nova tentativa estiver em andamento, o item SHALL apresentar a
situação de consulta em andamento, e os demais itens SHALL permanecer inalterados.

#### Scenario: A falha de um catálogo não apaga os outros
- **WHEN** o operador visualiza o inventário com a consulta de um catálogo
  falhando e as demais respondendo
- **THEN** os catálogos que responderam continuam apresentando as suas contagens,
  e apenas o que falhou fica com a contagem desconhecida

#### Scenario: A consulta em andamento de um catálogo não segura os outros
- **WHEN** o operador visualiza o inventário e a consulta de um catálogo demora
  mais que as demais
- **THEN** os catálogos que já responderam apresentam as suas contagens, sem
  esperar pelo que ainda está em andamento

#### Scenario: A nova tentativa refaz só a consulta daquele catálogo
- **WHEN** o operador aciona a nova tentativa no item de um catálogo com contagem
  desconhecida
- **THEN** apenas a consulta daquele catálogo é refeita, e os demais itens
  continuam apresentando o que já apresentavam

#### Scenario: A nova tentativa mostra a consulta em andamento
- **WHEN** o operador aciona a nova tentativa e a consulta ainda não respondeu
- **THEN** o item daquele catálogo apresenta a situação de consulta em andamento,
  em vez da contagem desconhecida

### Requirement: O inventário não reproduz a explicação das listagens

Cada item do inventário SHALL apresentar o nome do catálogo, a sua situação de
contagem e o atalho para a listagem — e SHALL NOT reproduzir o texto explicativo
que a listagem daquele catálogo apresenta sobre o que ele é ou para que serve.

A explicação de cada catálogo tem um lugar só, que é a sua listagem. Repeti-la no
inventário criaria uma segunda superfície a manter verdadeira, que envelheceria
sem que a tela mudasse.

A verificação que protege este requisito SHALL ser **estrutural** — afirmar que o
inventário não reproduz o texto explicativo de nenhuma listagem —, e SHALL NOT
fixar o teor atual do texto de nenhuma listagem, o que faria dela uma segunda
fonte de verdade daquele texto.

#### Scenario: O item do catálogo não repete a explicação da listagem
- **WHEN** o operador visualiza o inventário
- **THEN** nenhum item reproduz o texto explicativo que a listagem do seu
  catálogo apresenta
