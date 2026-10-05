## Purpose

O detalhe de base de conhecimento sincronizada no painel: de onde vem o conteúdo
(pasta, provedor, link para a pasta), o estado da sincronização gravado pelo
`apps/api` (`syncState`, `knowledge-base-catalog`), o motivo da falha pelo código, os
arquivos da pasta que não entraram na base e o "Sincronizar agora", que chama o
`apps/connectors` (`knowledge-sync-cycle`) e acompanha o ciclo até o resultado ser
gravado.

A capability existe porque o ciclo roda em segundo plano e o operador só enxerga o
que ele gravou. Três regras a protegem: a tela nunca afirma mais do que o sistema
sabe ("nunca sincronizou" não é falha, lista de ignorados nula não é lista vazia, e
nenhum texto de conclusão aparece antes de `lastFinishedAt` mudar); o texto vem do
`code`, nunca do `detail` como frase; e nenhum texto manda excluir a base, que é
escopo da exclusão de base no painel.

## ADDED Requirements

### Requirement: Card de origem no detalhe de base sincronizada
O detalhe de uma base com `contentMode` `Synced`, em `apps/frontend`, SHALL exibir
um card de origem, entre a descrição e os agentes, com o nome da pasta
(`syncSource.folderName`), um link para `syncSource.folderUrl` que abre em nova
aba, o provedor pelo nome de exibição (a chave crua quando o painel não conhece o
provedor) e a última sincronização concluída (`syncState.lastCompletedAt`).

A base `Synced` SHALL ser considerada falhando quando, e somente quando,
`syncState.failingSince` não for nulo. A tela SHALL distinguir quatro estados:

- nunca sincronizou (`lastCompletedAt`, `lastFinishedAt` e `failingSince` nulos):
  texto de espera em tom neutro, sem tom de falha, sem alerta e sem a expressão
  "falha";
- em dia (`failingSince` nulo e `lastCompletedAt` preenchido): data e hora da
  última sincronização concluída e a indicação de que o último ciclo não teve
  erro;
- falhando com sincronização concluída antes: data e hora da última concluída,
  "Falhando desde" com o instante de `failingSince`, e o nome da pasta rotulado
  como nome na última sincronização concluída;
- falhando sem nunca ter concluído: a indicação de que nenhuma sincronização foi
  concluída, "Falhando desde", e o nome da pasta rotulado como nome no cadastro.

A tela SHALL NOT exibir `lastFinishedAt` como última sincronização. O card SHALL
NOT ser exibido em base `Manual`.

#### Scenario: Base sincronizada em dia
- **WHEN** o operador abre o detalhe de uma base `Synced` com `lastCompletedAt`
  preenchido e `failingSince` nulo
- **THEN** o card de origem exibe o nome da pasta, o link para a pasta, o provedor
  "Google Drive", a data e hora da última sincronização concluída e a indicação de
  que o último ciclo não teve erro, sem alerta de falha

#### Scenario: Nunca sincronizou não aparece como falha
- **WHEN** o operador abre o detalhe de uma base `Synced` com os três instantes
  nulos e `lastError` nulo
- **THEN** o card exibe o texto de espera pela primeira sincronização, e o card não
  contém "Falhando", "falha" nem alerta de falha

#### Scenario: Falha depois de uma sincronização concluída
- **WHEN** o operador abre o detalhe de uma base com `lastCompletedAt` e
  `failingSince` preenchidos
- **THEN** o card exibe a última concluída, "Falhando desde" com o instante de
  `failingSince`, e o rótulo da pasta diz que o nome é o da última sincronização
  concluída

#### Scenario: Falha sem nenhuma sincronização concluída
- **WHEN** o operador abre o detalhe de uma base com `failingSince` preenchido e
  `lastCompletedAt` nulo
- **THEN** o card diz que nenhuma sincronização foi concluída, exibe "Falhando
  desde", e o rótulo da pasta diz que o nome é o do cadastro, e não o da última
  sincronização concluída

#### Scenario: Provedor desconhecido
- **WHEN** a base tem `syncSource.provider` igual a uma chave que o painel não
  conhece
- **THEN** o provedor aparece como a própria chave, e o link para a pasta não
  nomeia o Google Drive

#### Scenario: Base manual não tem card de origem
- **WHEN** o operador abre o detalhe de uma base `Manual`
- **THEN** a tela não exibe o card de origem nem o card de arquivos ignorados

### Requirement: Alerta de falha da sincronização com o motivo pelo código
Com a base falhando, o card de origem SHALL exibir um alerta com o texto escolhido
pelo `syncState.lastError.code`, nunca pelo `detail` como frase, e SHALL usar do
`detail` só o e-mail da conta em `access-denied` e o motivo em `provider-error`.
Todo alerta SHALL dizer que nenhum documento saiu da base. Os textos SHALL seguir a
tabela "Falha da base" da D4 do `design.md` desta change.

O texto de `rate-limited` SHALL dizer que a falha é passageira e que a próxima
tentativa é automática, e SHALL NOT falar de acesso nem de compartilhamento. O
texto de `access-denied` SHALL mostrar o e-mail da conta e orientar compartilhar a
pasta como Leitor. O texto de `api-not-configured` SHALL dizer que é configuração
da instalação, e não da pasta. Código desconhecido SHALL cair num texto neutro com
o código, sem reaproveitar o texto de outro código. Nenhum texto SHALL conter
`exclu`, `remov` nem `apag`.

#### Scenario: Sem acesso à pasta mostra a conta e o caminho de recuperação
- **WHEN** a base falha com `lastError` `{ code: "access-denied", detail: "sync@exemplo.iam.gserviceaccount.com" }`
- **THEN** o alerta contém o e-mail, a orientação de compartilhar a pasta com essa
  conta como Leitor, a garantia de que nenhum documento saiu da base e a
  orientação de criar uma nova base com outra pasta se a pasta deixou de existir

#### Scenario: Cota estourada é passageira e não fala de acesso
- **WHEN** a base falha com `lastError.code` `rate-limited`
- **THEN** o alerta diz que a falha é passageira e que a próxima tentativa é
  automática, e não contém "acesso" nem "compartilh"

#### Scenario: Cada código de falha da base tem o seu texto
- **WHEN** a base falha, uma vez para cada código da tabela "Falha da base" da D4
- **THEN** o alerta exibe o texto daquele código, diferente do texto de qualquer
  outro código da tabela

#### Scenario: Código de falha desconhecido
- **WHEN** a base falha com `lastError.code` `codigo-novo`
- **THEN** o alerta exibe o texto neutro com `codigo-novo`, e não o texto de
  nenhum código conhecido

#### Scenario: Nenhum texto manda excluir
- **WHEN** os textos de todos os códigos de falha da base, de arquivo ignorado e
  de resposta do pedido de sincronização são montados
- **THEN** nenhum contém `exclu`, `remov` nem `apag`, sem distinção de caixa

### Requirement: Arquivos da pasta que não entraram na base
O detalhe de uma base `Synced` SHALL exibir, na aba de documentos, a lista de
arquivos da pasta que não entraram na base, com o nome e o motivo de cada um,
escolhido pelo código conforme a tabela "Arquivo ignorado" da D4 do `design.md`
desta change. O texto de `download-blocked` SHALL orientar permitir o download
para leitores naquele arquivo, e SHALL aparecer só na lista de arquivos, nunca
como falha da base.

`ignoredFiles` nulo e `ignoredFiles` vazio SHALL ter textos diferentes: nulo SHALL
dizer que a lista aparece depois da primeira sincronização concluída; vazio SHALL
dizer que nenhum arquivo foi recusado na última sincronização concluída. Com a
base falhando, a lista SHALL ser apresentada como a da última sincronização
concluída, com a data dela.

#### Scenario: Lista com arquivos ignorados
- **WHEN** a base tem `ignoredFiles` com uma planilha `unsupported-type` e um
  arquivo `too-large` com `detail` `"2097152"`
- **THEN** a lista exibe os dois nomes, o motivo de tipo não suportado e o de
  tamanho com `2097152` bytes, e o cabeçalho diz "2 arquivos"

#### Scenario: Lista nula não aparece como nenhum arquivo ignorado
- **WHEN** a base tem `ignoredFiles` nulo
- **THEN** a tela diz que a lista aparece depois da primeira sincronização
  concluída, e não contém "Nenhum arquivo"

#### Scenario: Lista vazia não aparece como lista ainda inexistente
- **WHEN** a base tem `ignoredFiles` igual a `[]`
- **THEN** a tela diz que nenhum arquivo foi recusado na última sincronização
  concluída, e não contém "A lista aparece"

#### Scenario: Cada código de arquivo ignorado tem o seu texto
- **WHEN** a lista traz um arquivo para cada código da tabela "Arquivo ignorado",
  inclusive `too-large`, `unsupported-source-type`, `null-character` e
  `empty-content`
- **THEN** cada linha exibe o texto do seu código

#### Scenario: Download bloqueado é motivo de arquivo
- **WHEN** a base está em dia e a lista traz um arquivo `download-blocked`
- **THEN** a linha orienta permitir o download para leitores naquele arquivo, e o
  card de origem não exibe alerta de falha

#### Scenario: Em falha, a lista é a da última sincronização concluída
- **WHEN** a base está falhando e tem `ignoredFiles` com um arquivo
- **THEN** o card diz que a lista é a da última sincronização concluída, com a
  data de `lastCompletedAt`

### Requirement: Sincronizar agora com acompanhamento
O card de origem SHALL oferecer "Sincronizar agora", que chama
`POST /connectors/knowledge-bases/{id}/sync` no `apps/connectors` pelo cliente da
feature de bases. Antes da chamada, a tela SHALL reler a base no `apps/api` e
guardar o `lastFinishedAt` lido como linha de base.

Com a resposta `202`, a tela SHALL exibir que a sincronização foi solicitada,
SHALL manter o botão desabilitado e SHALL repetir a consulta da base a cada 4
segundos enquanto `lastFinishedAt` for igual à linha de base e o pedido tiver
menos de 5 minutos. A tela SHALL NOT exibir texto de conclusão enquanto
`lastFinishedAt` não mudar.

Quando `lastFinishedAt` mudar, a tela SHALL parar a consulta periódica, SHALL
exibir a conclusão (ou o término com falha, com o alerta do código), SHALL
reabilitar o botão e SHALL atualizar a listagem de documentos. Quando 5 minutos
passarem sem mudança, a tela SHALL parar a consulta periódica, SHALL dizer que
nenhum resultado foi gravado no período sem afirmar sucesso nem falha, e SHALL
reabilitar o botão.

Quando a chamada falhar, a tela SHALL exibir o texto da tabela "Resposta do
Sincronizar agora" da D4 do `design.md` desta change, de forma persistente, e
SHALL NOT iniciar a consulta periódica. Falha de rede SHALL dizer que o serviço de
conectores pode estar fora do ar.

A condição da consulta periódica SHALL ser uma função pura exportada.

#### Scenario: O clique não exibe sucesso antes de o estado mudar
- **WHEN** o operador clica em "Sincronizar agora", o `apps/connectors` responde
  `202` e as consultas seguintes da base trazem o mesmo `lastFinishedAt`
- **THEN** a tela exibe que a sincronização foi solicitada, o botão fica
  desabilitado, e nenhum texto de conclusão aparece

#### Scenario: Mudança de lastFinishedAt encerra o acompanhamento
- **WHEN** depois do `202` uma consulta da base traz `lastFinishedAt` diferente da
  linha de base e `failingSince` nulo
- **THEN** a tela exibe a conclusão com a data da última sincronização concluída,
  o botão volta a ficar habilitado, a consulta periódica para e a listagem de
  documentos é refeita

#### Scenario: Ciclo pedido termina com falha
- **WHEN** depois do `202` uma consulta da base traz `lastFinishedAt` diferente da
  linha de base e `failingSince` preenchido
- **THEN** a tela diz que a sincronização terminou com falha e exibe o alerta do
  código, sem texto de conclusão bem-sucedida

#### Scenario: Limite do acompanhamento
- **WHEN** depois do `202` passam 5 minutos e `lastFinishedAt` continua igual à
  linha de base
- **THEN** a consulta periódica para, a tela diz que nenhum resultado foi gravado
  no período, e o botão volta a ficar habilitado

#### Scenario: A linha de base vem de uma releitura
- **WHEN** o operador clica em "Sincronizar agora" e a releitura da base traz um
  `lastFinishedAt` mais novo que o do carregamento da página
- **THEN** o acompanhamento compara com o valor da releitura, e não termina na
  primeira consulta depois do `202`

#### Scenario: apps/connectors fora do ar
- **WHEN** o operador clica em "Sincronizar agora" e a chamada ao
  `apps/connectors` falha por rede
- **THEN** a tela exibe que o serviço de conectores pode estar fora do ar, o botão
  volta a ficar habilitado e nenhuma consulta periódica começa

#### Scenario: Cada código de resposta tem o seu texto
- **WHEN** a chamada responde com `knowledge-base-not-found`, `sync-not-configured`,
  `sync-api-unavailable` e `sync-api-error`, uma de cada vez
- **THEN** a tela exibe o texto daquele código

#### Scenario: Função pura da consulta periódica
- **WHEN** a função recebe um pedido com linha de base `T1`, a base com
  `lastFinishedAt` `T1`, e um instante menor que 5 minutos depois do pedido
- **THEN** ela devolve 4000; com `lastFinishedAt` diferente de `T1`, ou sem pedido,
  ou com o instante além de 5 minutos, ela devolve `false`

### Requirement: Sincronizar agora sem o endereço do apps/connectors
Sem `VITE_CONNECTORS_BASE_URL` no build, o botão "Sincronizar agora" SHALL
aparecer desabilitado, com a explicação de que a sincronização manual não está
habilitada neste painel, e o painel SHALL NOT fazer nenhuma requisição ao
`apps/connectors`. O card de origem, o alerta de falha e a lista de arquivos
ignorados SHALL continuar sendo exibidos a partir da resposta do `apps/api`.

#### Scenario: Botão indisponível sem a variável
- **WHEN** o painel foi construído sem `VITE_CONNECTORS_BASE_URL` e o operador abre
  o detalhe de uma base `Synced` falhando com `access-denied`
- **THEN** o botão está desabilitado com a explicação, o alerta de falha com o
  e-mail da conta continua aparecendo, e o `fetch` interceptado, que registrou as
  chamadas ao `apps/api`, não registrou nenhuma chamada ao `apps/connectors`
