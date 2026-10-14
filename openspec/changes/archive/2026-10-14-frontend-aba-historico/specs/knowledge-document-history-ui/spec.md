## ADDED Requirements

### Requirement: Aba de histórico de documentos no detalhe da base
O detalhe da base SHALL oferecer a aba `Histórico`, que lista os eventos de
`GET /knowledge-bases/{knowledgeBaseId}/document-events` (`knowledge-document-history`)
da base aberta. A aba SHALL consumir o contrato da rota como ele existe, sem
campo que a rota não devolve.

A rota SHALL ser chamada só com a aba `Histórico` ativa. A primeira página SHALL
ser pedida sem `cursor`.

#### Scenario: A aba lista os eventos da base
- **WHEN** o operador abre a aba `Histórico` de uma base cujos eventos a rota
  devolve
- **THEN** a aba exibe cada evento devolvido, uma vez cada, na ordem da resposta

#### Scenario: A primeira página é pedida sem cursor, e só com a aba ativa
- **WHEN** o operador abre o detalhe na aba `Documentos` e depois seleciona a aba
  `Histórico`
- **THEN** nenhuma requisição à rota de eventos sai antes da seleção, e depois
  dela sai exatamente uma, `GET /knowledge-bases/{id}/document-events` sem
  `cursor`, também com o painel montado em `StrictMode`

### Requirement: O que cada evento mostra
Cada evento SHALL exibir o horário em que ocorreu, o tipo, o título do documento
como o evento o gravou (`documentTitle`), o detalhe quando o tipo for `Updated`, e
o autor.

O tipo SHALL ser exibido como `Incluído` para `Created`, `Atualizado` para
`Updated` e `Excluído` para `Deleted`. Tipo que o painel não conhece SHALL ser
exibido num indicador neutro com o valor recebido, e SHALL NOT reaproveitar o
rótulo nem o indicador de nenhum tipo conhecido.

O detalhe de um `Updated` SHALL ser `Conteúdo alterado` quando só
`contentChanged` for verdadeiro, `Título alterado` quando só `titleChanged` for
verdadeiro, e `Conteúdo e título alterados` quando os dois forem. `Created` e
`Deleted` SHALL NOT exibir detalhe. O detalhe SHALL NOT nomear o título anterior
nem o motivo de uma exclusão, que a rota não devolve.

O título SHALL ser o `documentTitle` do próprio evento, mesmo quando o documento
foi renomeado depois ou já saiu da base: o evento mostra o documento como ele era
naquela escrita.

#### Scenario: Cada tipo tem o seu rótulo
- **WHEN** a rota devolve um evento `Created`, um `Updated` e um `Deleted`
- **THEN** a aba exibe `Incluído`, `Atualizado` e `Excluído`, cada um no seu
  evento

#### Scenario: Tipo desconhecido é neutro
- **WHEN** a rota devolve um evento com `type` igual a `Archived`
- **THEN** o evento exibe `Archived` num indicador neutro, e não exibe
  `Incluído`, `Atualizado` nem `Excluído`

#### Scenario: O detalhe da atualização diz o que mudou
- **WHEN** a rota devolve três `Updated`: só conteúdo, só título, e os dois
- **THEN** os detalhes são `Conteúdo alterado`, `Título alterado` e `Conteúdo e
  título alterados`, nessa correspondência

#### Scenario: Inclusão e exclusão não têm detalhe
- **WHEN** a rota devolve um `Created` e um `Deleted`
- **THEN** nenhum dos dois exibe `alterado` nem texto de motivo

#### Scenario: Evento de documento que já saiu da base mostra o título gravado
- **WHEN** a rota devolve, para o mesmo `documentId`, um `Created` com título
  `Prazos de envio` e um `Deleted` com título `Prazos de entrega`, e a listagem de
  documentos da base não tem esse documento
- **THEN** a aba exibe os dois eventos, cada um com o seu título, sem link e sem
  aviso de documento inexistente

### Requirement: Autor do evento
O autor SHALL ser exibido como `Operador` quando `author` for `operator`, e como
`Sincronização` quando for `service:connectors`. Qualquer outro valor não vazio
SHALL ser exibido num texto neutro que diz que o painel não reconhece o autor e
mostra o valor gravado, e SHALL NOT ser exibido como `Operador` nem como
`Sincronização`. `author` vazio SHALL ser exibido como autor não registrado.

#### Scenario: Operador e sincronização
- **WHEN** a rota devolve um evento de `author` `operator` e outro de `author`
  `service:connectors`
- **THEN** o primeiro exibe `Operador` e o segundo `Sincronização`

#### Scenario: Autor desconhecido não vira operador nem sincronização
- **WHEN** a rota devolve um evento de `author` `service:outro`
- **THEN** o evento exibe `service:outro` num texto que diz que o painel não
  reconhece o autor, e não exibe `Operador` nem `Sincronização`

#### Scenario: Autor vazio
- **WHEN** a rota devolve um evento de `author` vazio
- **THEN** o evento diz que o autor não foi registrado, e não exibe `Operador` nem
  `Sincronização`

### Requirement: Eventos agrupados por dia no fuso do navegador
Os eventos SHALL ser agrupados pelo dia local de `occurredAt` no fuso do
navegador, na ordem da resposta, com o cabeçalho de cada grupo no formato
`dd/mm/aaaa`, com ano. O horário de cada evento SHALL ser `HH:mm` no mesmo fuso. O
cabeçalho SHALL NOT dizer `Hoje` nem `Ontem`.

O agrupamento SHALL ser feito sobre todos os eventos carregados, e não por página:
eventos do mesmo dia que chegaram em páginas diferentes SHALL ficar no mesmo
grupo.

#### Scenario: O dia é o do fuso, não o do UTC
- **WHEN** um evento tem `occurredAt` `2026-09-26T02:30:00Z` e o fuso é
  `America/Sao_Paulo`
- **THEN** o evento fica no grupo `25/09/2026` com horário `23:30`, e no fuso `UTC`
  ficaria no grupo `26/09/2026` com horário `02:30`

#### Scenario: Um grupo por dia, na ordem da resposta
- **WHEN** a rota devolve dois eventos de 26/09/2026 e um de 23/09/2026, do mais
  recente para o mais antigo
- **THEN** a aba exibe o grupo `26/09/2026` com os dois, e depois o grupo
  `23/09/2026` com um

#### Scenario: Dia partido entre páginas fica num grupo só
- **WHEN** o último evento da primeira página e o primeiro da segunda são do mesmo
  dia, e o operador aciona `Carregar mais`
- **THEN** a aba exibe um único grupo daquele dia, com os eventos das duas páginas

### Requirement: Paginação por "Carregar mais"
Com `nextCursor` não nulo, a aba SHALL oferecer `Carregar mais`, que pede a
página seguinte com `?cursor=<nextCursor>` e acrescenta os eventos dela ao fim da
lista. Com `nextCursor` nulo, a aba SHALL NOT oferecer `Carregar mais`, e SHALL
dizer que não há eventos mais antigos.

Enquanto a página seguinte carrega, `Carregar mais` SHALL indicar carregamento e
não SHALL aceitar outro acionamento. Se a página seguinte falhar, os eventos já
exibidos SHALL continuar exibidos, a aba SHALL dizer que não foi possível carregar
mais eventos e SHALL oferecer tentar de novo, e SHALL NOT dizer que não há eventos
mais antigos.

#### Scenario: Carregar mais pede a página seguinte pelo cursor
- **WHEN** a primeira página responde com `nextCursor` `c1` e o operador aciona
  `Carregar mais`
- **THEN** a requisição seguinte é a rota de eventos com `cursor=c1`, e os eventos
  dela aparecem depois dos da primeira página

#### Scenario: Última página não oferece Carregar mais
- **WHEN** a página carregada responde com `nextCursor` nulo
- **THEN** a aba não exibe `Carregar mais`, e diz que não há eventos mais antigos

#### Scenario: Falha na página seguinte preserva o que foi carregado
- **WHEN** a primeira página respondeu e a página seguinte falha
- **THEN** os eventos da primeira página continuam exibidos, a aba diz que não foi
  possível carregar mais eventos e oferece tentar de novo, e não diz que não há
  eventos mais antigos

#### Scenario: Tentar de novo pede a mesma página
- **WHEN** a página seguinte falhou e o operador aciona tentar de novo
- **THEN** a requisição repete o mesmo `cursor`

### Requirement: Carregando, falha e vazio são três estados distintos
Enquanto a primeira página não respondeu, a aba SHALL indicar carregamento, e
SHALL NOT exibir lista, texto de histórico vazio nem texto de falha. Se a primeira
página falhar, a aba SHALL dizer que não foi possível carregar o histórico e
oferecer tentar de novo, e SHALL NOT dizer que a base não tem mudança registrada.
Se a primeira página responder com `items` vazio, a aba SHALL dizer que nenhuma
mudança foi registrada nesta base, e SHALL NOT exibir texto de falha nem
`Carregar mais`.

#### Scenario: Carregando não é vazio nem falha
- **WHEN** a primeira página ainda não respondeu
- **THEN** a aba indica carregamento, e não exibe o texto de histórico vazio nem o
  de falha

#### Scenario: Falha não é vazio
- **WHEN** a primeira página responde com erro
- **THEN** a aba diz que não foi possível carregar o histórico e oferece tentar de
  novo, e não exibe o texto de histórico vazio

#### Scenario: Vazio não é falha
- **WHEN** a primeira página responde `200` com `items` vazio e `nextCursor` nulo
- **THEN** a aba diz que nenhuma mudança foi registrada nesta base, e não exibe
  texto de falha nem `Carregar mais`

### Requirement: Nota sobre o começo do histórico
A aba SHALL dizer que o histórico só é registrado a partir da implantação dessa
funcionalidade, e que documentos incluídos antes dela não têm evento de inclusão.
A nota SHALL NOT citar data de implantação, que o painel não conhece. Em base
`Synced`, a aba SHALL dizer também que arquivos ignorados e falhas de acesso não
entram no histórico e aparecem no estado atual da base; em base `Manual`, SHALL
NOT dizer isso.

#### Scenario: Nota em base manual
- **WHEN** o operador abre a aba `Histórico` de uma base `Manual`
- **THEN** a aba exibe a nota sobre o começo do histórico, e não fala de arquivos
  ignorados

#### Scenario: Nota em base sincronizada
- **WHEN** o operador abre a aba `Histórico` de uma base `Synced`
- **THEN** a aba exibe a nota sobre o começo do histórico e a de que arquivos
  ignorados e falhas de acesso não entram nele

### Requirement: A aba funciona sem a sincronização
A aba SHALL funcionar em base `Manual` sem nenhuma requisição ao `apps/connectors`
e sem depender de `VITE_CONNECTORS_BASE_URL`.

#### Scenario: Base manual sem a variável dos conectores
- **WHEN** o painel não tem `VITE_CONNECTORS_BASE_URL` e o operador abre a aba
  `Histórico` de uma base `Manual`
- **THEN** a aba lista os eventos, e nenhuma requisição sai para o
  `apps/connectors`

#### Scenario: Base manual com a variável dos conectores
- **WHEN** o painel tem `VITE_CONNECTORS_BASE_URL` e o operador abre a aba
  `Histórico` de uma base `Manual`
- **THEN** a aba lista os eventos, e nenhuma requisição sai para o
  `apps/connectors`

### Requirement: Quando o histórico é relido
O histórico SHALL ser relido a partir da primeira página toda vez que o operador
entra na aba `Histórico`, quando aciona `Atualizar`, e quando um "Sincronizar
agora" pedido nesta visita termina com a aba aberta. A releitura SHALL pedir só a
primeira página, sem `cursor`, e SHALL descartar as páginas carregadas antes dela.
A aba SHALL NOT consultar a rota em intervalo, nem reler ao voltar o foco para a
janela. Eventos gravados com a aba aberta SHALL aparecer ao acionar `Atualizar` ou
ao voltar à aba.

`Atualizar` SHALL estar disponível quando a primeira página respondeu (lista,
vazio ou fim da lista), e SHALL indicar carregamento enquanto relê. SHALL NOT
aparecer enquanto a primeira página carrega nem quando ela falhou, que já oferece
tentar de novo. Durante a releitura, a aba SHALL exibir os eventos da primeira
página que já tinha, e SHALL NOT voltar ao estado de carregamento. Se a releitura
falhar, esses eventos SHALL continuar exibidos, e a aba SHALL dizer que não foi
possível atualizar o histórico, sem exibir o texto de falha da primeira página.

Mudar a base (desativar, ativar, editar) SHALL NOT reler o histórico: nenhuma
dessas escritas gera evento de documento.

#### Scenario: Atualizar volta à primeira página
- **WHEN** o operador carregou duas páginas e aciona `Atualizar`
- **THEN** a rota de eventos é chamada uma vez, sem `cursor`, e só os eventos da
  primeira página estão exibidos

#### Scenario: Atualizar não aparece antes da primeira página nem na falha dela
- **WHEN** a primeira página ainda não respondeu, ou respondeu com erro
- **THEN** a aba não exibe `Atualizar`

#### Scenario: Falha do Atualizar preserva a lista
- **WHEN** a primeira página respondeu com eventos e a releitura por `Atualizar`
  falha
- **THEN** os eventos continuam exibidos, a aba diz que não foi possível atualizar
  o histórico, e não exibe o texto de falha da primeira página

#### Scenario: Desativar a base não relê o histórico
- **WHEN** a aba `Histórico` está aberta com duas páginas carregadas e o operador
  desativa a base pelo cabeçalho
- **THEN** nenhuma requisição à rota de eventos é feita

#### Scenario: Voltar à aba recomeça da primeira página
- **WHEN** o operador carregou duas páginas, trocou para `Documentos` e voltou
  para `Histórico`
- **THEN** a rota de eventos é chamada de novo sem `cursor`, e só os eventos da
  primeira página estão exibidos

#### Scenario: O fim do Sincronizar agora relê o histórico aberto
- **WHEN** a aba `Histórico` de uma base `Synced` está aberta, o operador aciona
  "Sincronizar agora" e a base passa a ter `lastFinishedAt` novo
- **THEN** a rota de eventos é chamada de novo sem `cursor`

#### Scenario: Sem consulta em intervalo
- **WHEN** a aba `Histórico` está aberta, a primeira página respondeu, e o tempo
  passa sem ação do operador
- **THEN** a rota de eventos não é chamada de novo

### Requirement: Acessibilidade da aba e da lista
A aba SHALL ser alcançável e selecionável pelo teclado, como as outras da barra.
Cada grupo de dia SHALL ter um título que nomeia o dia, e os eventos de um dia
SHALL formar uma lista. O tipo SHALL estar em texto, e nenhum significado SHALL
depender só da cor ou do ícone. O horário SHALL carregar o instante completo
legível por máquina. `Carregar mais` e tentar de novo SHALL ser botões.

Depois que `Carregar mais` acrescenta eventos, o foco SHALL ir para o primeiro
evento acrescentado, inclusive quando `Carregar mais` deixa de existir por ter
chegado à última página.

#### Scenario: Grupo de dia é título e lista
- **WHEN** a aba exibe eventos de um dia
- **THEN** existe um título com a data do dia, e os eventos daquele dia são itens
  de uma lista

#### Scenario: O instante completo está no horário
- **WHEN** a aba exibe um evento
- **THEN** o horário exibido carrega o `occurredAt` completo como valor legível
  por máquina

#### Scenario: O foco vai para o primeiro evento novo
- **WHEN** o operador aciona `Carregar mais` e a página seguinte, a última, responde
- **THEN** o foco está no primeiro evento acrescentado
