# knowledge-document-catalog-ui Specification

## Purpose

A gestão do **conteúdo** de uma base de conhecimento no painel do operador:
listar os documentos de uma base com o estado de indexação de cada um, adicionar
documentos por arquivo ou escrevendo à mão, atualizar, excluir e reindexar.
Consome as rotas de `knowledge-document-catalog` e a de reindexação de
`knowledge-document-indexing` em `apps/api`.

A capability existe por um motivo que não é CRUD: antes dela, a base existia, a
descrição que o modelo lê existia, o vínculo com o agente existia e o índice
existia — e o **conteúdo**, que é a razão de tudo isso, só entrava por `curl`.

Desde a etapa do diagnóstico do índice, tudo isso vive na aba `Documentos` do
detalhe da base — a aba canônica, a que abre sem parâmetro no endereço. A barra
de abas em si é especificada por `knowledge-base-catalog-ui`; o que está **dentro**
desta aba é o que esta capability cobre.

Duas regras atravessam a capability. A primeira é que a indexação é assíncrona e
a tela **acompanha a transição** em vez de pedir recarga: enquanto houver
documento não-terminal a listagem se refaz sozinha, e para quando todos chegam a
`Indexed` ou `Failed`. A segunda é a de não afirmar o que o sistema não sabe, e
aqui ela tem forma própria: a contagem de fragmentos é governada por `indexedAt`
e **nunca** por estado, porque documento que falhou ou está sendo reindexado
continua respondendo com os fragmentos anteriores — exibir zero ali afirmaria que
a indexação rodou e não achou nada. Não há progresso percentual, e nenhuma cópia
relaciona tamanho de documento a falha de indexação.

## Requirements

### Requirement: Listagem de documentos no detalhe da base
O sistema SHALL exibir, no detalhe de uma base de conhecimento em
`apps/frontend`, os documentos daquela base, consumindo
`GET /knowledge-bases/{knowledgeBaseId}/documents`. Cada linha SHALL exibir o
título do documento, o tipo de origem, o estado de indexação e as ações
disponíveis.

A ordem exibida SHALL ser a da resposta. `apps/api` já ordena por `CreatedAt`
com desempate por `Id` (`api-response-ordering`), e reordenar no cliente criaria
uma segunda ordenação com comparador diferente da do PostgreSQL.

Quando a listagem não puder ser carregada, a tela SHALL informar a falha e
SHALL NOT afirmar que a base não tem documentos — uma requisição que não
respondeu não é evidência de ausência.

#### Scenario: Base com documentos exibe uma linha por documento
- **WHEN** o operador visualiza o detalhe de uma base cuja listagem de documentos
  devolve três documentos
- **THEN** a tela exibe três linhas, cada uma com título, tipo de origem e estado
  de indexação

#### Scenario: Base sem documentos exibe estado vazio verificado
- **WHEN** o operador visualiza o detalhe de uma base cuja listagem devolve uma
  lista vazia
- **THEN** a tela informa que a base não tem nenhum documento e oferece a ação de
  adicionar o primeiro

#### Scenario: Falha ao listar não vira estado vazio
- **WHEN** a requisição da listagem de documentos falha
- **THEN** a tela informa que não foi possível carregar os documentos, e não
  afirma que a base está sem documentos

#### Scenario: A ordem da resposta é preservada
- **WHEN** a listagem devolve documentos numa ordem
- **THEN** a tela os exibe exatamente nessa ordem

### Requirement: A contagem de fragmentos é governada por `indexedAt`
O sistema SHALL exibir a contagem de fragmentos de um documento quando, e
somente quando, `indexedAt` não for nulo — **em qualquer um dos quatro estados de
indexação**. Quando `indexedAt` for nulo, a contagem SHALL ser omitida.

O sistema SHALL NOT exibir contagem zerada para documento cujo `indexedAt` seja
nulo. Zero ali é o default de uma coluna que ninguém escreveu, e exibi-lo
afirmaria que a indexação rodou e não encontrou nada.

O separador SHALL ser `indexedAt`, e SHALL NOT ser o estado de indexação.
Documento reindexado ou atualizado passa por `Pending` e `Indexing` com os
fragmentos anteriores vivos no índice, e documento que falhou depois de ter sido
indexado continua respondendo com eles — `RequestReindex()`, `Update()` e
`KnowledgeIndexingService.FailAsync` preservam `IndexedAt` e `FragmentCount`
(`design.md`, D4).

#### Scenario: Documento indexado exibe a contagem
- **WHEN** um documento está em `Indexed` com `indexedAt` preenchido e
  `fragmentCount` igual a 14
- **THEN** a linha exibe a contagem de 14 fragmentos

#### Scenario: Documento novo em `Pending` omite a contagem
- **WHEN** um documento está em `Pending` com `indexedAt` nulo
- **THEN** a linha não exibe contagem de fragmentos

#### Scenario: Documento que falhou sem nunca ter sido indexado omite a contagem
- **WHEN** um documento está em `Failed` com `indexedAt` nulo e `fragmentCount`
  igual a 0
- **THEN** a linha não exibe contagem de fragmentos, e em particular não exibe a
  cadeia `0 fragmentos`

#### Scenario: Documento que falhou depois de indexado exibe a contagem anterior
- **WHEN** um documento está em `Failed` com `indexedAt` preenchido e
  `fragmentCount` igual a 9
- **THEN** a linha exibe a contagem de 9 fragmentos

#### Scenario: Documento reindexado em `Pending` exibe a contagem anterior
- **WHEN** um documento está em `Pending` com `indexedAt` preenchido e
  `fragmentCount` igual a 5
- **THEN** a linha exibe a contagem de 5 fragmentos

#### Scenario: Documento reindexando exibe a contagem anterior
- **WHEN** um documento está em `Indexing` com `indexedAt` preenchido e
  `fragmentCount` igual a 11
- **THEN** a linha exibe a contagem de 11 fragmentos

### Requirement: Estado de indexação por documento
O sistema SHALL exibir, por documento, um dos quatro estados de
`KnowledgeIndexingStatus` lidos do campo `indexingStatus` do fio: `Pending`,
`Indexing`, `Indexed` e `Failed`, com rótulo em português e tom distinto por
estado.

O estado `Indexing` SHALL exibir indicador de atividade.

O sistema SHALL NOT exibir progresso percentual, barra de progresso ou fração de
trabalho concluído em nenhum estado. O sistema conhece o estado do documento e
não conhece o percentual: `indexingAttempts` conta execuções do consumidor, não
fração de trabalho, e não existe denominador em campo nenhum.

Valor de `indexingStatus` desconhecido SHALL renderizar indicador neutro, e SHALL
NOT reaproveitar o indicador de sucesso.

#### Scenario: Os quatro estados têm rótulo próprio
- **WHEN** a listagem contém um documento em cada um dos quatro estados
- **THEN** cada linha exibe o rótulo correspondente ao seu estado, e os quatro
  rótulos são distintos entre si

#### Scenario: `Indexing` exibe indicador de atividade
- **WHEN** um documento está em `Indexing`
- **THEN** a linha exibe um indicador de atividade junto ao estado

#### Scenario: Nenhum estado exibe progresso percentual
- **WHEN** a listagem contém documentos em `Pending` e em `Indexing`
- **THEN** nenhuma linha exibe percentual, barra de progresso ou fração de
  trabalho concluído

### Requirement: Faixa de falha com o motivo completo
O sistema SHALL exibir, sob a linha de um documento em `Failed`, uma faixa de
largura total contendo o `failureReason` devolvido pela API, **completo e sem
truncar**. É a única cópia do motivo da falha que a tela tem, e `apps/api`
garante que ele é texto legível por operador.

A faixa SHALL conter a ação de reindexar aquele documento.

O sistema SHALL NOT exibir a faixa para documentos que não estejam em `Failed`.

Quando um documento estiver em `Failed` e `failureReason` for nulo, a faixa SHALL
informar que o motivo não foi registrado, e SHALL NOT inventar um motivo.

#### Scenario: Documento que falhou exibe o motivo completo
- **WHEN** um documento está em `Failed` com um `failureReason` de várias frases
- **THEN** a faixa exibe o texto do `failureReason` inteiro, sem reticências e
  sem corte

#### Scenario: A faixa oferece reindexar
- **WHEN** um documento está em `Failed`
- **THEN** a faixa daquele documento contém a ação de reindexar

#### Scenario: Documentos que não falharam não têm faixa
- **WHEN** a listagem contém documentos em `Pending`, `Indexing` e `Indexed`
- **THEN** nenhum deles exibe faixa de falha

#### Scenario: Falha sem motivo registrado
- **WHEN** um documento está em `Failed` com `failureReason` nulo
- **THEN** a faixa informa que o motivo não foi registrado

### Requirement: Reindexar um documento
O sistema SHALL permitir reindexar um documento a partir da faixa de falha,
chamando `POST /knowledge-bases/{knowledgeBaseId}/documents/{id}/reindex`.

A resposta da rota já traz o documento no estado novo, e a tela SHALL
re-renderizar a linha a partir dela, sem uma segunda leitura.

Quando a requisição falhar, o sistema SHALL informar a falha e SHALL NOT alterar
o estado exibido da linha.

#### Scenario: Reindexar devolve o documento para `Pending`
- **WHEN** o operador aciona reindexar num documento em `Failed` e a API responde
  com o documento em `Pending`
- **THEN** a linha passa a exibir o estado `Pending` e a faixa de falha
  desaparece

#### Scenario: Falha ao reindexar preserva o estado exibido
- **WHEN** o operador aciona reindexar e a requisição falha
- **THEN** a tela informa a falha e a linha continua exibindo `Failed` com a
  faixa

### Requirement: A listagem acompanha a transição de indexação
O sistema SHALL recarregar a listagem de documentos periodicamente enquanto
houver ao menos um documento em `Pending` ou `Indexing`, e SHALL parar de
recarregar quando todos os documentos estiverem em estado terminal (`Indexed` ou
`Failed`).

A condição SHALL depender do conteúdo da resposta, e SHALL NOT depender apenas de
a tela estar aberta.

#### Scenario: Listagem com documento não-terminal recarrega
- **WHEN** a listagem contém ao menos um documento em `Pending` ou `Indexing`
- **THEN** a consulta é reagendada para recarregar periodicamente

#### Scenario: Listagem só com documentos terminais não recarrega
- **WHEN** todos os documentos da listagem estão em `Indexed` ou `Failed`
- **THEN** a consulta não é reagendada

#### Scenario: O recarregamento para quando o último documento fica terminal
- **WHEN** a listagem tinha um documento em `Indexing` e o recarregamento devolve
  esse documento em `Indexed`
- **THEN** a consulta deixa de ser reagendada

### Requirement: Resumo de documentos ainda não utilizáveis
O sistema SHALL exibir, acima da tabela, uma nota informando quantos documentos
ainda não estão utilizáveis, quando houver ao menos um em `Pending` ou
`Indexing`. A nota SHALL ser derivada da própria listagem, sem requisição
adicional.

O sistema SHALL NOT exibir a nota quando todos os documentos estiverem em estado
terminal.

#### Scenario: Nota aparece com documento não-terminal
- **WHEN** a listagem contém dois documentos em `Pending` ou `Indexing`
- **THEN** a tela exibe uma nota informando que dois documentos ainda não estão
  utilizáveis

#### Scenario: Nota não aparece com todos terminais
- **WHEN** todos os documentos estão em `Indexed` ou `Failed`
- **THEN** a nota não é exibida

### Requirement: Adicionar documentos por arquivo
O sistema SHALL permitir adicionar documentos a uma base subindo arquivos
`.md`, `.markdown` ou `.txt`, vários por vez, em modal sobre o detalhe da base.

O conteúdo SHALL ser lido como texto no cliente e enviado como string no corpo
JSON de `POST /knowledge-bases/{knowledgeBaseId}/documents`. O sistema SHALL NOT
usar `multipart/form-data`: markdown e texto são texto, e multipart obrigaria a
alterar o `Content-Type` fixo do `request<T>` de cada feature.

Cada arquivo aceito SHALL entrar como uma linha própria, com o título
pré-preenchido a partir do nome do arquivo — extensão removida, `-` e `_`
convertidos em espaço — e editável pelo operador. Cada linha SHALL poder ser
removida individualmente.

Arquivo de extensão não aceita SHALL entrar na lista com o motivo da recusa, e
SHALL NOT ser descartado em silêncio.

A lista SHALL preservar a ordem em que o operador selecionou os arquivos,
independentemente de aceite ou recusa.

O sistema SHALL oferecer duas entradas de arquivo — escolher pelo seletor e
arrastar sobre a área de soltar — e as duas SHALL produzir o mesmo resultado,
convergindo para a mesma função de tratamento. A convergência é o que permite que
o caminho do seletor, exercitado com arquivo real, cubra a lógica dos dois; sem
ela, metade do tratamento de arquivo ficaria sem cobertura automatizada.

Cada arquivo aceito SHALL virar um documento próprio, por uma chamada
independente ao `POST` unitário.

#### Scenario: Arquivos aceitos entram com título pré-preenchido
- **WHEN** o operador escolhe `politica-de_reembolso.md`
- **THEN** uma linha é adicionada com o título `politica de reembolso`, editável

#### Scenario: Arquivo de extensão não aceita entra com o motivo
- **WHEN** o operador escolhe `manual.pdf`
- **THEN** uma linha é adicionada informando que a extensão não é aceita, e o
  arquivo não é descartado em silêncio

#### Scenario: A ordem da seleção é preservada
- **WHEN** o operador escolhe, nessa ordem, um `.md` aceito, um `.txt` aceito e
  um `.pdf` recusado
- **THEN** as três linhas aparecem nessa mesma ordem

#### Scenario: Remoção individual de uma linha
- **WHEN** o operador remove uma das linhas da lista
- **THEN** apenas aquela linha sai, e as demais permanecem com os títulos que
  tinham

#### Scenario: Cada arquivo vira um documento próprio
- **WHEN** o operador confirma o envio com dois arquivos aceitos
- **THEN** são feitas duas chamadas independentes ao `POST` de criação de
  documento, uma por arquivo

#### Scenario: Título vazio impede o envio
- **WHEN** o operador apaga o título de uma das linhas e confirma o envio
- **THEN** o envio é bloqueado com a indicação de que todo documento precisa de
  título, e nenhuma chamada é feita

#### Scenario: Arrastar um arquivo e escolhê-lo pelo seletor produzem o mesmo resultado
- **WHEN** o mesmo arquivo chega por arrastar sobre a área de soltar e, noutra
  montagem, por escolha no seletor de arquivos
- **THEN** as duas entradas produzem a mesma linha, com o mesmo título
  pré-preenchido e o mesmo estado de aceite

### Requirement: Falha parcial no envio em lote
O sistema SHALL tratar o envio de vários arquivos como N operações
independentes, executadas em sequência na ordem da lista, com estado próprio por
linha.

Quando uma das chamadas falhar, o sistema SHALL manter criados os documentos das
chamadas que já tiveram sucesso, SHALL manter o modal aberto e SHALL exibir, na
linha correspondente, que aquele arquivo falhou e por quê.

Uma linha cujo documento já foi criado SHALL NOT ser reenviada num novo acionamento.

O sistema SHALL NOT afirmar, antes do envio, que os documentos serão criados como
conjunto. A contagem do que será tentado é verdade; afirmar o desfecho não é.

A listagem SHALL ser invalidada a cada criação bem-sucedida, e não apenas ao fim
do lote.

#### Scenario: Falha no meio do lote preserva o que já entrou
- **WHEN** o operador envia três arquivos e a segunda chamada falha
- **THEN** o primeiro documento permanece criado, a segunda linha exibe o motivo
  da falha, e o modal continua aberto

#### Scenario: Reenviar não recria o que já entrou
- **WHEN** depois de uma falha parcial o operador aciona o envio de novo
- **THEN** apenas as linhas que ainda não viraram documento são enviadas, e
  nenhuma chamada é repetida para as que já tiveram sucesso

#### Scenario: O rodapé não promete atomicidade
- **WHEN** o operador tem dois arquivos aceitos na lista e ainda não enviou
- **THEN** o texto do modal não afirma que os dois documentos serão criados como
  conjunto

### Requirement: Adicionar documento escrevendo manualmente
O sistema SHALL oferecer, no mesmo modal, o modo de escrever o documento
manualmente, com título, tipo de origem e conteúdo.

O campo de tipo de origem SHALL ser exibido desabilitado com o único valor
`markdown`, para tornar visível que o campo existe e que hoje não há escolha.

#### Scenario: Documento escrito manualmente é criado
- **WHEN** o operador preenche título e conteúdo no modo manual e confirma
- **THEN** é feita uma chamada ao `POST` de criação com o título, o conteúdo e o
  tipo de origem `markdown`

#### Scenario: Tipo de origem é exibido e desabilitado
- **WHEN** o operador abre o modo manual
- **THEN** o campo de tipo de origem aparece desabilitado com o valor `markdown`

#### Scenario: Título vazio bloqueia a criação manual
- **WHEN** o operador confirma sem preencher o título
- **THEN** o envio é bloqueado com a indicação de que o título é obrigatório

### Requirement: Atualizar um documento
O sistema SHALL permitir atualizar um documento existente em modal, por dois
modos: substituir o conteúdo por um arquivo — **um só**, preservando o título
atual — ou escrever manualmente, com o conteúdo atual já carregado.

O sistema SHALL informar o efeito do salvamento **nos dois casos**, comparando o
conteúdo carregado com o editado, porque a tela não sabe de antemão em qual deles
o operador está:

- **Conteúdo alterado**: SHALL avisar que o documento volta para `Pending` e será
  indexado de novo, e SHALL informar que o conteúdo indexado anteriormente
  continua respondendo até a nova indexação terminar com sucesso — **incluindo
  que ele permanece se a nova indexação falhar**.
- **Conteúdo idêntico**: SHALL informar que salvar **não** reindexa o documento e
  que o estado de indexação continua o mesmo. Omitir essa informação faria o
  operador que corrige apenas o título esperar uma reindexação que não ocorre, e
  ler a ausência de mudança de estado como defeito.

`apps/api` só devolve o documento para `Pending` quando o conteúdo extraído
difere do gravado; atualização que só troca o título preserva o estado de
indexação corrente.

O rótulo da ação de salvar SHALL indicar reindexação apenas quando o conteúdo
tiver mudado.

O sistema SHALL NOT afirmar que o documento sai das consultas do agente durante a
reindexação, nem que os fragmentos anteriores são descartados no momento do
salvamento. O conteúdo indexado anteriormente continua respondendo até a nova
indexação terminar com sucesso, e é substituído em transação única — se a
reindexação falhar, os fragmentos anteriores permanecem.

A comparação de conteúdo é feita no cliente porque `contentHash` não é devolvido
por nenhuma rota. Para documento criado antes da introdução do `contentHash`, a
comparação pode indicar que o conteúdo não mudou enquanto o servidor reindexa
assim mesmo; nesse caso o aviso SHALL faltar, e nunca sobrar.

#### Scenario: Modo manual carrega o conteúdo atual
- **WHEN** o operador aciona atualizar num documento existente
- **THEN** o modal abre no modo manual com o título e o conteúdo atuais
  preenchidos

#### Scenario: Substituir por arquivo preserva o título
- **WHEN** o operador escolhe um arquivo no modo de substituição e confirma
- **THEN** a chamada de atualização envia o conteúdo do arquivo e o título que o
  documento já tinha

#### Scenario: Conteúdo alterado avisa sobre a reindexação
- **WHEN** o operador altera o conteúdo no modo manual
- **THEN** o modal avisa que o documento volta para `Pending` e será indexado de
  novo, e a ação de salvar indica reindexação

#### Scenario: O aviso diz que o conteúdo anterior continua respondendo, inclusive se a nova indexação falhar
- **WHEN** o operador altera o conteúdo e lê o aviso
- **THEN** o texto informa que o conteúdo indexado anteriormente continua
  respondendo até a nova indexação terminar com sucesso, e que ele permanece caso
  ela falhe

#### Scenario: Alterar só o título informa que salvar não reindexa
- **WHEN** o operador altera apenas o título e deixa o conteúdo intacto
- **THEN** o modal informa que salvar não reindexa o documento e que o estado de
  indexação continua o mesmo

#### Scenario: Alterar só o título não menciona reindexar na ação de salvar
- **WHEN** o operador altera apenas o título e deixa o conteúdo intacto
- **THEN** a ação de salvar não menciona reindexar

#### Scenario: O aviso não afirma que o documento sai das consultas
- **WHEN** o operador altera o conteúdo e lê o aviso
- **THEN** o texto não afirma que o documento sai das consultas do agente, nem
  que os fragmentos anteriores são descartados naquele momento

### Requirement: Excluir um documento
O sistema SHALL permitir excluir um documento, chamando
`DELETE /knowledge-bases/{knowledgeBaseId}/documents/{id}`, sempre por
confirmação explícita.

A confirmação SHALL nomear os agentes que consultam a base, quando houver,
derivados no cliente a partir de `GET /agents` — a mesma derivação que o card de
agentes do detalhe já usa.

#### Scenario: Exclusão passa por confirmação
- **WHEN** o operador aciona excluir num documento
- **THEN** uma confirmação é exibida antes de qualquer requisição

#### Scenario: A confirmação nomeia os agentes afetados
- **WHEN** o operador aciona excluir num documento de uma base consultada por dois
  agentes
- **THEN** a confirmação nomeia os dois agentes

#### Scenario: Base sem agentes vinculados
- **WHEN** o operador aciona excluir num documento de uma base que nenhum agente
  consulta
- **THEN** a confirmação é exibida sem lista de agentes afetados

#### Scenario: Cancelar não exclui
- **WHEN** o operador cancela a confirmação
- **THEN** nenhuma requisição de exclusão é feita e o documento permanece na
  listagem

### Requirement: A interface não afirma o que o sistema não verifica
O sistema SHALL NOT relacionar tamanho de documento a probabilidade de falha de
indexação, em nenhuma superfície — nem como aviso, nem como texto estático de
ajuda. Nada no pipeline de indexação correlaciona tamanho com falha de embedding.

O sistema SHALL NOT afirmar que uma resposta de agente foi fundamentada em um
trecho de documento. O sistema registra o que a busca devolveu, não o que a
resposta usou.

O `failureReason` SHALL ser renderizado como veio da API, sem interpretação,
reescrita ou complemento.

#### Scenario: Nenhuma cópia relaciona tamanho a falha
- **WHEN** o operador percorre a listagem de documentos e os modais de adicionar
  e atualizar
- **THEN** nenhum texto afirma que documentos grandes tendem a falhar na
  indexação

#### Scenario: O motivo da falha não é complementado
- **WHEN** a API devolve um `failureReason`
- **THEN** a faixa exibe exatamente esse texto, sem acrescentar diagnóstico ou
  recomendação própria
