## ADDED Requirements

### Requirement: Geração de embedding é loteada
A geração de embedding de um documento SHALL ser feita em **lotes de fragmentos**,
com tamanho configurável em `apps/workers`. Um documento com mais fragmentos que
o tamanho do lote SHALL produzir **mais de uma** chamada ao gerador de embedding;
um documento com no máximo esse número de fragmentos SHALL produzir **exatamente
uma**.

Os lotes SHALL ser gerados **sequencialmente**, um por vez.

A ordem dos vetores SHALL corresponder à ordem dos fragmentos **através das
fronteiras de lote**: o vetor gravado para cada fragmento é o que o provedor
devolveu para o texto **daquele** fragmento.

O número de vetores devolvidos SHALL ser conferido **por lote**, contra o número
de entradas enviadas naquele lote, e a divergência SHALL falhar a indexação
daquele documento sem gravar fragmento nenhum.

O **valor** do tamanho do lote deliberadamente NÃO faz parte desta spec, pelo
mesmo motivo que os parâmetros de fragmentação não fazem: o par medido que o
motivou (um lote de 442 falha, um de 267 passa, contra um gateway cujo teto não
foi estabelecido) decide grandeza, não ótimo. Afirmar aqui um número que nenhuma
medição otimizou seria requisito que passa verde sem provar nada.

#### Scenario: Documento com mais fragmentos que o lote é gerado em mais de uma chamada
- **WHEN** um documento produz mais fragmentos do que o tamanho do lote
  configurado
- **THEN** o gerador de embedding é chamado mais de uma vez, cada chamada com no
  máximo o tamanho do lote, a soma das entradas enviadas é igual ao número de
  fragmentos, e o documento termina `Indexed` com a contagem completa

#### Scenario: Documento menor que o lote é gerado em uma chamada só
- **WHEN** um documento produz menos fragmentos do que o tamanho do lote
  configurado
- **THEN** o gerador de embedding é chamado **exatamente uma** vez, com todos os
  fragmentos

#### Scenario: Documento com exatamente o tamanho do lote é gerado em uma chamada só
- **WHEN** um documento produz um número de fragmentos **igual** ao tamanho do
  lote configurado
- **THEN** o gerador de embedding é chamado **exatamente uma** vez

#### Scenario: Documento sem fragmento não chama o gerador
- **WHEN** a fragmentação de um documento **com conteúdo** devolve zero
  fragmentos — precondição afirmada explicitamente pelo cenário, para que ele não
  fique verde por nunca alcançar a chamada
- **THEN** o gerador de embedding **não** é chamado nenhuma vez, e nenhum
  fragmento é gravado

#### Scenario: O vetor de cada fragmento é o do próprio texto, através da fronteira de lote
- **WHEN** um documento é indexado atravessando mais de um lote
- **THEN** o vetor gravado de cada fragmento — inclusive o do primeiro fragmento
  do segundo lote — é o que o provedor devolveu para o texto daquele fragmento, e
  não o de outro

#### Scenario: Lote que devolve número de vetores diferente do enviado falha a indexação
- **WHEN** uma das chamadas devolve um número de vetores diferente do número de
  entradas enviadas naquele lote
- **THEN** a indexação daquele documento falha com motivo legível, e nenhum
  fragmento é gravado

### Requirement: Tamanho de lote inválido reprova o boot
`apps/workers` NÃO SHALL subir com tamanho de lote de embedding menor ou igual a
zero. A falha SHALL nomear o valor encontrado e o que se espera, e o valor NÃO
SHALL ser corrigido em silêncio.

O motivo de reprovar o boot em vez de falhar na primeira indexação é que este é
um parâmetro feito para ser **variado à mão em produção** — variá-lo é como o
teto do gateway vai ser descoberto. Um valor inválido que só aparecesse na
primeira indexação queimaria as três tentativas de cada documento e gravaria
`Failed` com motivo técnico em toda a base. E corrigir em silêncio faria a
medição seguinte ser feita sobre um número que ninguém escolheu.

#### Scenario: Tamanho de lote zero ou negativo reprova o boot
- **WHEN** a configuração declara tamanho de lote de embedding menor ou igual a
  zero
- **THEN** o processo não sobe, e a mensagem nomeia o valor encontrado

#### Scenario: Tamanho de lote válido sobe
- **WHEN** a configuração declara tamanho de lote de embedding maior que zero
- **THEN** o processo sobe normalmente
