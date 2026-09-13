## MODIFIED Requirements

### Requirement: Orientação e preview da descrição no formulário
O formulário SHALL apresentar a descrição em seção visualmente distinta dos
demais campos, com a orientação de como escrevê-la — que ela é lida pelo modelo
durante a conversa, e que deve dizer que assunto a base cobre e em que situação
consultá-la.

A orientação SHALL incluir o critério de **delimitação**: dizer também do que a
base **não** trata. E SHALL dizer que descrição genérica atrai perguntas que
pertencem a outras bases, porque o modelo escolhe entre as bases vinculadas ao
agente comparando as descrições entre si.

O critério não é ornamental e não é opinião de estilo: a rodada de medição de
roteamento entre bases mediu uma base com 5 intenções sendo consultada 24 vezes
enquanto a maior base, com 28 intenções, foi consultada 11 — os pares de erro
dominantes eram as vizinhas indo para a mais geral, e o braço de controle com a
ordem das tools invertida descartou viés de posição. Quando o roteamento erra
entre bases, não há recuperação.

O formulário SHALL exibir a contagem de caracteres da descrição e SHALL sinalizar
descrição curta. Esse sinal SHALL ser apresentado como **piso** — indicação de que
o campo mal foi preenchido — e SHALL NOT ser apresentado como aferição da
qualidade da descrição nem como garantia sobre a decisão do modelo. Comprimento
não é a dimensão que decide roteamento: as descrições medidas na rodada iam de
250 a 427 caracteres, e a que canibalizou as vizinhas tinha 421 — todas passariam
folgadas por qualquer limiar de comprimento.

O sistema SHALL NOT apresentar veredito automático sobre o quanto uma descrição é
específica ou genérica — nem aviso, nem selo, nem cor derivada do conteúdo do
texto. Nenhuma regra do sistema faz essa distinção, e apresentá-la afirmaria um
critério que o sistema não tem.

O formulário SHALL exibir, dentro dessa mesma seção, um preview do que o agente
recebe: o nome e a descrição. Com a descrição em branco, o preview SHALL dizer a
consequência — que, do que distingue esta base das demais, o modelo recebe só o
nome.

O preview SHALL declarar que o texto exibido é **o trecho escrito pelo operador**
dentro do texto que o agente recebe, e que o sistema o envolve em instruções
fixas sobre como ler o resultado da busca. O preview SHALL NOT se apresentar como
o texto completo entregue ao modelo, e SHALL NOT reproduzir as instruções fixas —
elas vivem em `apps/workers`, são iguais para toda base, e não são acionáveis pelo
operador.

O preview SHALL NOT exibir nome de ferramenta — nem o pretendido, nem o
pretendido acompanhado de ressalva. Três razões independentes o sustentam, e cada
uma basta:

1. O formato do nome não está fixado em spec nenhuma. A capability de execução da
   tool de conhecimento exige nomes distintos e ordem determinística independente
   do nome da base, e deliberadamente não fixa o formato — que é detalhe de
   implementação. Exibi-lo faria esta interface virar a definição de fato de um
   formato que nenhuma spec define.
2. Não há fonte reusável do lado do painel. O nome é construído em `apps/workers`
   por dois passos encadeados (remoção de diacríticos e kebab-case; depois
   substituição de caracteres fora do conjunto permitido e truncamento), e nenhuma
   rota o devolve. Recalculá-lo na tela seria uma segunda fonte de verdade.
3. O nome construído é o **pretendido**, não o efetivo. O conjunto final de tools
   do agente passa por deduplicação global, com precedência declarada em que a
   tool de conhecimento é sempre a renomeada, e a colisão só se resolve na
   execução — com o conjunto inteiro do agente, que o formulário de uma base não
   tem e não pode ter.

O formulário SHALL NOT exibir número de consultas, de invocações ou de uso da base
pelos agentes. O sistema não coleta essa contagem.

O formulário SHALL indicar que documentos são carregados depois, na tela de
detalhe da base.

#### Scenario: A descrição tem seção própria, com orientação
- **WHEN** o operador visualiza o formulário de base
- **THEN** a descrição aparece em seção distinta dos demais campos, com a
  orientação de que é o texto lido pelo modelo e do que deve conter

#### Scenario: A orientação pede delimitação, não só assunto
- **WHEN** o operador visualiza a orientação da descrição
- **THEN** ela pede que o texto diga também do que a base **não** trata

#### Scenario: A orientação diz que descrição genérica atrai perguntas de outras bases
- **WHEN** o operador visualiza a orientação da descrição
- **THEN** ela informa que uma descrição genérica faz o agente consultar esta base
  no lugar de outra

#### Scenario: Preview acompanha o que foi digitado
- **WHEN** o operador preenche nome e descrição
- **THEN** o preview exibe os dois valores

#### Scenario: Preview sem descrição diz a consequência
- **WHEN** o campo de descrição está em branco
- **THEN** o preview informa que, do que distingue esta base, o modelo recebe só o
  nome dela para decidir quando consultá-la

#### Scenario: Preview declara que o texto do operador é envolvido por instruções fixas
- **WHEN** o operador visualiza o preview
- **THEN** a tela informa que o sistema acrescenta instruções fixas sobre como ler
  o resultado da busca, e o preview não se apresenta como o texto completo

#### Scenario: Preview não reproduz as instruções fixas
- **WHEN** o operador visualiza o preview
- **THEN** o texto das instruções fixas de leitura do resultado não é exibido

#### Scenario: Preview não afirma nome de ferramenta
- **WHEN** o operador visualiza o preview
- **THEN** nenhum identificador de ferramenta é exibido, nem acompanhado de
  ressalva sobre renomeação

#### Scenario: Contagem de caracteres da descrição
- **WHEN** o operador visualiza o campo de descrição
- **THEN** a quantidade de caracteres é exibida, e recebe sinal quando a descrição
  é curta

#### Scenario: O sinal de descrição curta não afirma qualidade
- **WHEN** o operador escreve uma descrição abaixo do piso e lê o sinal
- **THEN** o sinal informa que o campo mal foi preenchido, e não afirma que a
  descrição é adequada nem que o modelo decidirá corretamente acima do piso

#### Scenario: Descrição longa não recebe selo de aprovação
- **WHEN** o operador escreve uma descrição acima do piso
- **THEN** nenhum sinal afirma que a descrição está boa, adequada ou suficiente

#### Scenario: Nenhum veredito de generalidade
- **WHEN** o operador escreve qualquer descrição
- **THEN** a tela não apresenta aviso, selo ou cor derivada de quão específica ou
  genérica a descrição é

#### Scenario: Nenhuma contagem de uso da base
- **WHEN** o operador visualiza o formulário de base
- **THEN** nenhum número de consultas, invocações ou uso da base é exibido

#### Scenario: Formulário indica onde os documentos entram
- **WHEN** o operador visualiza o formulário de base
- **THEN** a tela informa que documentos são carregados depois, na tela de
  detalhe

### Requirement: A descrição da base é apresentada como texto lido pelo modelo
O sistema SHALL apresentar a descrição da base em seção própria do detalhe,
rotulada de forma a identificá-la como o texto que o modelo lê para decidir se a
base é relevante para a pergunta, com a nota de que ela não é mostrada ao
cliente final.

A nota SHALL descrever a descrição como o texto que o modelo lê para decidir se a
pergunta pertence a esta base. A nota SHALL NOT afirmar que a descrição cadastrada
**é** a descrição da ferramenta entregue ao modelo: o sistema a compõe com um
prefixo que nomeia a base e com instruções fixas sobre como ler o resultado da
busca, iguais para toda base. A descrição cadastrada é a parte que o operador
escreve, não o todo.

A descrição SHALL NOT ser apresentada como subtítulo do cabeçalho de detalhe. A
posição é deliberada: como subtítulo ela lê como texto decorativo de UI, e ela é
campo de runtime (`design.md`, contexto e D7).

#### Scenario: Descrição aparece em seção própria e rotulada
- **WHEN** o operador visualiza o detalhe de uma base
- **THEN** a descrição aparece em seção própria, com rótulo que a identifica como
  o texto lido pelo modelo, e com a nota de que não é mostrada ao cliente

#### Scenario: A nota não afirma identidade com a descrição da ferramenta
- **WHEN** o operador lê a nota da seção de descrição
- **THEN** ela não afirma que o texto cadastrado é a descrição da ferramenta
  entregue ao modelo

#### Scenario: Descrição não é o subtítulo do cabeçalho
- **WHEN** o operador visualiza o cabeçalho de detalhe da base
- **THEN** o subtítulo do cabeçalho não é a descrição da base
