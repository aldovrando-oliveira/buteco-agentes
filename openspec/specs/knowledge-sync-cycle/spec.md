# knowledge-sync-cycle Specification

## Purpose

O ciclo de sincronização de base sincronizada no `apps/connectors`: a rodada
periódica e o "Sincronizar agora", a ordem dos passos de uma base, o que exclui e o
que nunca exclui, onde cada falha cai (arquivo, base ou rodada), a memória de recusas
determinísticas, a chamada ao `apps/api` como `service:connectors` e a configuração
opcional `Api:BaseUrl`. O `apps/api` continua sendo o único dono da escrita de
documentos.

## Requirements

### Requirement: Ciclo periódico sobre toda base sincronizada
Quando `Api:BaseUrl` estiver configurada, o `apps/connectors` SHALL rodar uma rodada
de sincronização logo depois do boot e a cada 5 minutos, sobre toda base devolvida
por `GET /sync/knowledge-bases` no `apps/api`, inclusive as inativas, uma base por
vez. Uma rodada SHALL começar só depois que a anterior terminar, e ticks perdidos
NÃO SHALL se acumular. O intervalo SHALL ser fixo.

#### Scenario: Primeira rodada no boot
- **WHEN** o `apps/connectors` sobe com `Api:BaseUrl` configurada e o `apps/api` tem
  duas bases `Synced`, uma delas inativa
- **THEN** uma rodada roda sem esperar o intervalo e sincroniza as duas bases

#### Scenario: Rodada seguinte depois do intervalo
- **WHEN** a primeira rodada termina e o relógio avança 5 minutos
- **THEN** uma segunda rodada roda, e nenhuma roda antes disso

#### Scenario: Rodada longa não acumula rodadas
- **WHEN** uma rodada ainda está rodando quando o relógio avança três intervalos
- **THEN** quando ela termina roda no máximo uma rodada seguinte, e nunca duas rodadas
  ao mesmo tempo

### Requirement: Ordem do ciclo de uma base
O ciclo de uma base SHALL, nesta ordem: descrever a pasta pelo `IFolderNavigator` do
provedor da base; listar a raiz pelo `IFolderContentSource`; ler as referências e
marcadores gravados (`GET /sync/knowledge-bases/{id}/documents`); para cada arquivo
suportado cuja referência não está gravada ou cujo marcador difere do gravado,
obter o markdown e enviá-lo por upsert com o nome do arquivo como título e
`sourceType` `markdown`; excluir por referência o que sumiu da pasta; e gravar o
resultado do ciclo. Arquivo com marcador igual ao gravado NÃO SHALL ser baixado nem
enviado.

#### Scenario: Marcador igual não é baixado
- **WHEN** a listagem traz um arquivo com o mesmo marcador gravado no `apps/api`
- **THEN** o markdown dele não é pedido ao conector e nenhum upsert é enviado para ele

#### Scenario: Marcador novo é baixado e enviado
- **WHEN** a listagem traz um arquivo com marcador diferente do gravado, e outro que o
  `apps/api` não tem
- **THEN** o markdown dos dois é pedido ao conector e os dois são enviados por upsert,
  com o nome do arquivo como título

#### Scenario: Ciclo bem-sucedido grava a pasta e os ignorados
- **WHEN** o ciclo de uma base termina sem falha de base
- **THEN** o resultado gravado é `Succeeded`, com o nome e a URL da descrição da pasta
  e a lista de ignorados, vazia se nenhum

#### Scenario: Mudança só de metadado não gera evento
- **WHEN** o compartilhamento de um Google Doc é alterado no Drive, o marcador muda, e
  o nome do arquivo e o markdown exportado são os mesmos
- **THEN** o ciclo envia o upsert com o marcador novo, o `apps/api` responde
  `Unchanged`, e o histórico da base não ganha evento

#### Scenario: Renomear o arquivo é mudança de título
- **WHEN** um Google Doc é renomeado no Drive e o markdown exportado é o mesmo
- **THEN** o upsert leva o nome novo como título, o `apps/api` registra um evento
  `Updated` com `titleChanged: true` e `contentChanged: false`, e não reindexa

### Requirement: Exclusão só do que sumiu, e só depois de ler tudo
O ciclo SHALL excluir por referência apenas as referências gravadas no `apps/api` que
não aparecem na listagem, nem entre os arquivos suportados nem entre os ignorados. A
exclusão SHALL acontecer apenas depois de a pasta ser descrita com sucesso, de a
listagem voltar completa e de os upserts da base terminarem. Uma base interrompida
antes do passo de exclusão NÃO SHALL excluir nenhum documento.

#### Scenario: Arquivo que sumiu é excluído
- **WHEN** o `apps/api` tem duas referências e a listagem completa traz só uma
- **THEN** a outra é excluída por referência, e só ela

#### Scenario: Pasta sem acesso não exclui nada
- **WHEN** a descrição da pasta falha com `access-denied`
- **THEN** nenhuma exclusão é enviada, e o resultado gravado é `Failed` com `code`
  `access-denied` e `detail` igual ao e-mail da conta

#### Scenario: Listagem que falha na segunda página não exclui nada
- **WHEN** a listagem da raiz falha numa página depois da primeira
- **THEN** nenhuma exclusão é enviada, nenhum upsert é enviado, e o resultado gravado
  não é `Succeeded`

#### Scenario: Arquivo ignorado que continua na pasta mantém o documento
- **WHEN** um arquivo que já tem documento passa a vir nos ignorados da listagem (por
  exemplo `download-blocked`), ou é recusado pelo `apps/api` com `too-large`
- **THEN** o documento não é excluído nem alterado, e o arquivo aparece nos ignorados
  com o código

#### Scenario: Referência comparada como veio
- **WHEN** o `apps/api` tem a referência `AbC` e a listagem traz `abc`
- **THEN** `AbC` é excluída e `abc` é enviada como nova

### Requirement: Falha de arquivo vira ignorado; falha de base vira Failed
Falha que é do arquivo SHALL fazer o arquivo entrar nos ignorados com o código, sem
alterar o documento existente e sem interromper o ciclo. São de arquivo: os ignorados
da listagem; `download-blocked`, `file-not-found`, `provider-error` e
`provider-unavailable` ao obter o markdown; e a recusa de conteúdo do upsert, `400`
**com** `code` (`too-large`, `unsupported-source-type`, `null-character`,
`empty-content`), com o `contentBytes` como detalhe no `too-large`.

Falha que é da base SHALL gravar `Failed` com o código e o detalhe, sem excluir nada,
e a rodada SHALL seguir para a próxima base. São de base: `access-denied`,
`not-a-folder`, `folder-trashed`, `provider-error`, `provider-unavailable` e
`api-not-configured` na descrição ou na listagem; `provider-auth-failed` em qualquer
chamada ao provedor; `provider-not-configured` quando o provedor da base não está
registrado no processo; e `sync-api-error` quando o `apps/api` responde fora do
contrato (inclusive `400` **sem** `code`). A recusa de forma NÃO SHALL entrar nos
ignorados.

#### Scenario: Recusa de conteúdo vira arquivo ignorado
- **WHEN** o upsert de um arquivo responde `400` com `code` `too-large`, e o de outro
  responde `400` com `code` `empty-content`
- **THEN** os dois entram nos ignorados com esses códigos, o `too-large` com o
  `contentBytes` no detalhe, os documentos existentes continuam como estavam, e o
  resultado gravado é `Succeeded`

#### Scenario: Ignorados da listagem
- **WHEN** a listagem traz um atalho, uma subpasta, uma planilha e um Doc com
  download bloqueado
- **THEN** os quatro entram nos ignorados com `shortcut-not-followed`,
  `subfolder-not-synced`, `unsupported-type` e `download-blocked`

#### Scenario: Falha ao baixar um arquivo não derruba a base
- **WHEN** obter o markdown de um arquivo falha com `file-not-found` e o de outro dá
  certo
- **THEN** o primeiro entra nos ignorados, o segundo é enviado, e o resultado gravado
  é `Succeeded`

#### Scenario: Recusa de forma não vira ignorado
- **WHEN** o upsert responde `400` sem `code`
- **THEN** o arquivo não entra nos ignorados, nenhuma exclusão é enviada, e o resultado
  gravado é `Failed` com `code` `sync-api-error`

#### Scenario: Provedor da base não registrado
- **WHEN** a base aponta para um provedor sem registro no processo
- **THEN** o resultado gravado é `Failed` com `code` `provider-not-configured`

### Requirement: Recusa determinística não é reenviada com o mesmo marcador
O ciclo SHALL guardar em memória do processo a base, a referência externa e o
marcador de todo arquivo cujo upsert for recusado com `too-large`,
`unsupported-source-type`, `null-character` ou `empty-content`, com o código e o
detalhe. No ciclo seguinte, se o marcador do arquivo for igual ao guardado, o ciclo
NÃO SHALL obter o markdown nem enviar o upsert, e SHALL repor o arquivo nos ignorados
com o mesmo código e detalhe. Marcador diferente SHALL invalidar a entrada, e o
arquivo SHALL ser tentado de novo. Falha transitória (`provider-unavailable`,
`provider-error`, `rate-limited`, `file-not-found` e a contenção `503`) NÃO SHALL ser
guardada. A entrada SHALL sair da memória quando a referência deixar de aparecer numa
listagem completa da base, ou quando a base deixar de existir. A memória vale para a
rodada periódica e para o "Sincronizar agora", e não sobrevive ao processo.

#### Scenario: Recusa determinística não é baixada de novo com o mesmo marcador
- **WHEN** o upsert de um arquivo é recusado com `too-large`, e o ciclo seguinte lista
  o arquivo com o mesmo marcador
- **THEN** no segundo ciclo o markdown dele não é pedido ao conector, nenhum upsert é
  enviado para ele, e ele continua nos ignorados gravados com `too-large` e o mesmo
  detalhe

#### Scenario: Marcador novo faz o arquivo recusado ser tentado de novo
- **WHEN** um arquivo recusado com `too-large` aparece no ciclo seguinte com marcador
  diferente
- **THEN** o markdown dele é pedido ao conector e o upsert é enviado

#### Scenario: Falha transitória não é lembrada
- **WHEN** obter o markdown de um arquivo falha com `provider-unavailable`, e o ciclo
  seguinte lista o arquivo com o mesmo marcador
- **THEN** no segundo ciclo o markdown dele é pedido de novo

#### Scenario: Referência que sumiu da pasta sai da memória
- **WHEN** um arquivo recusado com `too-large` some da listagem completa, e num ciclo
  posterior volta à pasta com o mesmo marcador
- **THEN** o markdown dele é pedido de novo, e não é reposto da memória

#### Scenario: Sincronizar agora respeita a memória
- **WHEN** o operador pede a sincronização de uma base com um arquivo já recusado com
  `too-large` e o mesmo marcador
- **THEN** o markdown dele não é pedido, e ele continua nos ignorados

### Requirement: Cota estourada grava Failed e encerra a rodada
Ao receber `rate-limited` de qualquer chamada ao provedor, o ciclo SHALL parar a base
em curso sem excluir nada, gravar `Failed` com `code` `rate-limited` nessa base, e
encerrar a rodada sem processar as bases restantes, que ficam para a próxima rodada.
O ciclo NÃO SHALL repetir a chamada dentro da rodada.

#### Scenario: Cota estourada no meio da base
- **WHEN** obter o markdown do segundo de três arquivos falha com `rate-limited`
- **THEN** nenhuma exclusão é enviada, o resultado gravado é `Failed` com `code`
  `rate-limited`, e a próxima base da rodada não é descrita

### Requirement: Base inexistente encerra só aquela base
O ciclo de uma base SHALL terminar sem nova tentativa, sem gravar resultado e sem
excluir quando qualquer rota de `/sync` responder `404` ou `409` para a base, e a
rodada SHALL seguir para a próxima base. Uma exceção inesperada no ciclo de uma
base SHALL ser logada sem interromper a rodada.

#### Scenario: Base com 404 não interrompe as outras
- **WHEN** a rodada tem três bases e a leitura das referências da segunda responde
  `404`
- **THEN** a segunda não recebe nenhuma outra chamada nem gravação de resultado, e a
  terceira é sincronizada

#### Scenario: Exceção inesperada numa base
- **WHEN** o conector lança uma exceção que não é falha de conector durante a
  listagem da primeira base
- **THEN** o erro é logado e a segunda base é sincronizada

### Requirement: apps/api fora do ar encerra a rodada
Quando o `apps/api` não responder, ou responder `401` ou `403`, o ciclo SHALL
encerrar a rodada sem gravar resultado, porque a falha alcança todas as bases.

#### Scenario: apps/api sem resposta
- **WHEN** a leitura das referências da primeira base não tem resposta
- **THEN** nenhuma base seguinte é descrita, e nada é excluído

### Requirement: Contenção não é repetida nem vira ignorado
Quando o upsert ou a exclusão responder `503`, o ciclo SHALL seguir sem repetir a
chamada, sem pôr o arquivo nos ignorados, e o resultado da base SHALL continuar
`Succeeded` se nada mais falhar.

#### Scenario: Upsert com 503
- **WHEN** o upsert de um arquivo responde `503` com `Retry-After`
- **THEN** o arquivo não está nos ignorados, nenhuma nova tentativa é enviada, e o
  resultado gravado é `Succeeded`

### Requirement: Chamada ao apps/api como service:connectors
Toda requisição do `apps/connectors` ao `apps/api` SHALL levar um token novo assinado
com `Auth:TokenSigningKey`, `sub` igual a `service:connectors` e validade fixa de 5
minutos. `service:connectors` NÃO SHALL entrar na tabela de subjects do
`apps/connectors`.

#### Scenario: O token enviado é service:connectors
- **WHEN** o ciclo faz qualquer chamada ao `apps/api`
- **THEN** o cabeçalho `Authorization` traz um token que o `TokenService` valida com
  `sub` igual a `service:connectors`

#### Scenario: O apps/api real aceita o token
- **WHEN** o ciclo roda contra o `apps/api` real
- **THEN** nenhuma das chamadas responde `401` nem `403`

### Requirement: Endereço do apps/api opcional
`Api:BaseUrl` SHALL ser opcional. Ausente ou vazia, o processo SHALL subir com um
aviso no log, nenhuma rodada SHALL rodar, e as rotas de navegação e de descrição
SHALL continuar funcionando. Presente e diferente de uma URI absoluta `http` ou
`https`, o boot SHALL falhar com mensagem que nomeia a chave e não ecoa o valor.

#### Scenario: Sem o endereço do apps/api
- **WHEN** o `apps/connectors` sobe sem `Api:BaseUrl`
- **THEN** o processo sobe, `GET /connectors/providers` responde `200` ao operador, e
  nenhuma chamada ao `apps/api` é feita

#### Scenario: Endereço inválido
- **WHEN** o `apps/connectors` sobe com `Api:BaseUrl` igual a `localhost`
- **THEN** a inicialização falha nomeando `Api:BaseUrl`, sem o valor na mensagem

### Requirement: Sincronizar agora
`POST /connectors/knowledge-bases/{id}/sync` SHALL confirmar a base em
`GET /sync/knowledge-bases` e, presente, disparar o ciclo dela em segundo plano e
responder `202`, inclusive para base inativa. Base ausente da lista SHALL responder
`404` com `code` `knowledge-base-not-found`. Sem `Api:BaseUrl`, SHALL responder `503`
com `code` `sync-not-configured`, sem chamada de rede. `apps/api` sem resposta SHALL
dar `503` `sync-api-unavailable`; resposta fora do contrato, inclusive `401` e `403`,
SHALL dar `502` `sync-api-error`.

Uma base SHALL ter no máximo um ciclo rodando por instância: com um ciclo dela em
curso, o pedido SHALL responder `202` sem disparar outro, e a rodada periódica SHALL
pular a base.

#### Scenario: Operador pede a sincronização
- **WHEN** o operador faz `POST /connectors/knowledge-bases/{id}/sync` para uma base
  `Synced`
- **THEN** a resposta é `202` e o ciclo daquela base roda e grava o resultado

#### Scenario: Base que não é sincronizada
- **WHEN** o operador pede a sincronização de um id que não está em
  `GET /sync/knowledge-bases`
- **THEN** a resposta é `404` com `code` `knowledge-base-not-found`

#### Scenario: Dois pedidos de sincronização simultâneos rodam um ciclo
- **WHEN** dois pedidos para a mesma base chegam enquanto o primeiro ciclo ainda lista
  a raiz
- **THEN** os dois respondem `202` e a raiz é listada uma vez só

#### Scenario: Rodada periódica pula a base em sincronização
- **WHEN** a rodada periódica chega a uma base cujo ciclo pedido pelo operador ainda
  roda
- **THEN** a rodada não inicia outro ciclo dela

#### Scenario: Sem o endereço do apps/api
- **WHEN** o operador pede a sincronização num processo sem `Api:BaseUrl`
- **THEN** a resposta é `503` com `code` `sync-not-configured`

#### Scenario: apps/api recusa o token
- **WHEN** a consulta da base no `apps/api` responde `401`
- **THEN** a resposta ao operador é `502` com `code` `sync-api-error`, e não `401`

### Requirement: Nenhum log com conteúdo, token ou chave
Os logs do ciclo e do "Sincronizar agora" SHALL conter id da base, contagens,
códigos e duração, e NÃO SHALL conter texto de documento, token, chave nem nome de
arquivo.

#### Scenario: Nenhum log contém conteúdo, token ou chave
- **WHEN** um ciclo envia um documento com um marcador de texto conhecido e grava o
  resultado
- **THEN** nenhuma mensagem de log capturada contém o marcador de texto, o token
  enviado, a chave de assinatura nem o nome do arquivo
