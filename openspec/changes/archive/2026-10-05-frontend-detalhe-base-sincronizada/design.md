## Context

O backend da linha de bases sincronizadas está pronto, e o painel já cria a base
(#106). O que esta tela consome:

- **#102** (`catalogo-base-sincronizada`): toda resposta de base traz
  `contentMode`, `syncSource` (`provider`, `folderId`, `folderName`, `folderUrl`) e
  `syncState`, inclusive em `GET /knowledge-bases`. Em base `Manual`, os dois são
  `null`. Em base `Synced`, `syncState` traz `lastCompletedAt`, `lastFinishedAt`,
  `failingSince`, `lastError` (`{ code, detail }` ou `null`) e `ignoredFiles`
  (`[{ externalRef, name, code, detail }]`). Os códigos saem como foram gravados.
  `ignoredFiles` é `null` até o primeiro ciclo bem-sucedido e lista, possivelmente
  vazia, depois. O sucesso grava `lastCompletedAt` e `lastFinishedAt`, limpa
  `lastError` e `failingSince` e troca nome, URL e ignorados; a falha grava
  `lastFinishedAt` e `lastError`, preenche `failingSince` só se estava nulo, e não
  toca `lastCompletedAt`, nome, URL nem ignorados. Criar, atualizar e excluir
  documento pelo operador em base `Synced` respondem `409`
  (`KnowledgeDocumentEndpoints.cs:79`, `:142`, `:198`). **Reindexar não tem essa
  guarda** (`:33`, sem `SyncedKnowledgeBaseConflict`).
- **#105** (`ciclo-de-sincronizacao`): rodada a cada 5 minutos
  (`SyncSchedulerService.cs:13`), inclusive sobre base inativa.
  `POST /connectors/knowledge-bases/{id}/sync`, só para `operator`, responde `202`
  sem corpo e roda o ciclo em segundo plano; com um ciclo da base já em curso,
  responde `202` sem disparar outro. Erros: `404` `knowledge-base-not-found`,
  `503` `sync-not-configured`, `503` `sync-api-unavailable`, `502`
  `sync-api-error`. Onde cada código cai está na D4 de lá: de arquivo vão para os
  ignorados, de base gravam `Failed`. **Há caminhos que não gravam resultado
  nenhum:** base com `404` ou `409` no meio do ciclo (D8 de lá), `apps/api` sem
  resposta ou com `401`/`403` (encerra a rodada sem gravar) e exceção inesperada
  (logada). Nesses, `lastFinishedAt` não muda. O risco "polling da #107 nunca
  terminar" de lá cita só o `404`; os outros dois também existem.
- **#120** (`codigo-recusa-conteudo-upsert`): `too-large`,
  `unsupported-source-type`, `null-character` e `empty-content` chegam como
  arquivo ignorado; o `too-large` traz `contentBytes` como detalhe, em texto
  (`SyncApiClient.cs:122`). O teto é `KnowledgeDocumentLimits.MaxContentBytes`,
  1 MiB (`KnowledgeDocumentLimits.cs:23`), e não vem no detalhe.
- **#106** (`frontend-cadastro-base-sincronizada`): `connectorsApi.ts` com
  `request<T>`, `ConnectorsError` (`kind: "network" | "http"`, `status`, `code`,
  `detail`) e `connectorsBaseUrl(): string | null`, que é `null` sem
  `VITE_CONNECTORS_BASE_URL` e não deixa nada sair para a rede. A tabela de
  textos por código é `connectorErrorMessage(error, context)` em
  `utils/connectorErrors.ts`, com os contextos `navigation` e `create`. O tipo
  `KnowledgeBase` tem `contentMode` e `syncSource`, e deixou `syncState` para esta
  change (D8 de lá, convenção 25). O comentário do módulo diz que
  `requestKnowledgeBaseSync` entra ali.

**Comentários da #107, que valem como requisito:** o download bloqueado é motivo
**por arquivo**, com o texto "o download está bloqueado para leitores; permita
download para leitores neste arquivo", nunca falha da base nem alerta de acesso;
`api-not-configured` é falha de configuração da implantação, e o texto diz isso;
`rate-limited` diz que a falha é passageira e que a próxima tentativa é
automática, para não ser lido como problema de acesso.

**O frontend hoje:** `KnowledgeBaseDetailPage` monta cabeçalho, descrição,
agentes e as abas Documentos e Diagnóstico, para qualquer base. O
`KnowledgeDocumentsCard` sempre mostra "Adicionar documento" e as ações
"Atualizar" e "Excluir" por linha; "Reindexar documento" fica na faixa de falha.
`KnowledgeBaseTable` tem cinco colunas com larguras fixas medidas. O filtro "Com
falha" usa `hasFailure` do resumo de indexação e, sem o resumo, fica desabilitado
e cai para "Todas" (`effectiveStatusFilter`). O painel já tem consulta periódica
condicional em função pura, de 4 s (`useKnowledgeDocuments.ts:24`,
`useKnowledgeIndex.ts:19`), pela convenção 20.

**Protótipo:** pranchas 1, 4a e 4b do canvas "Bases sincronizadas — protótipo",
copiadas em `design/` (ver `design/README.md`). Regra que o sistema já tem vence
o protótipo (convenção 17).

Três trabalhos correm em paralelo nesta máquina (#107 aqui, #138 e #47), e os três
podem editar `01`, `02`, `CHANGELOG.md` e `docs/`.

## Goals / Non-Goals

**Goals:**

- Detalhe de base sincronizada com origem, estado da sincronização, falha com
  caminho de recuperação, documentos somente leitura e arquivos ignorados.
- "Sincronizar agora" que não afirma sucesso antes de o estado mudar, e que não
  espera para sempre.
- Listagem com a origem de cada base, e filtro "Com falha" que inclui
  sincronização falhando.
- Base manual igual a hoje no detalhe.

**Non-Goals:**

- Aba Histórico e o rótulo do autor `service:connectors` (#101).
- Modal de documento e o texto das recusas de conteúdo nele (#131). Em base
  sincronizada o modal não abre mais; em base manual nada muda.
- Exclusão de base, o botão e a orientação de excluir em qualquer texto (#136).
- Qualquer mudança em `apps/api`, `apps/connectors`, `apps/workers` ou
  `apps/inbox`, e a implantação do `apps/connectors` em produção (#119).
- Paginação de documentos ("Mais 7 documentos" da prancha, C4).
- Busca por nome de pasta na listagem: a issue não pede, e a busca casa com nome e
  descrição por requisito.

**Versões:** nenhuma dependência nova nem troca de versão. `Alert`, `Badge`,
`Button`, `Tooltip` e `Loader` estão no `@mantine/core` 9 instalado; os ícones
`FileText`, `Folder` e `CircleAlert` vêm do `lucide-react` já usado na barra
lateral (`AppShell.tsx`).

## Decisions

### D1. Ações manuais e "Reindexar" numa base sincronizada

- **"Adicionar documento", "Atualizar" e "Excluir" não aparecem** em base
  `Synced`: o botão do cabeçalho do card some, e a coluna "Ações" sai da tabela
  (prancha 4a). O `apps/api` recusa as três com `409`, e o mantenedor viu os
  botões aparecendo na validação da #106. Em base `Manual`, tudo continua como
  hoje.
- **"Reindexar documento" continua**, na faixa de falha, como hoje. O `apps/api`
  a permite em base sincronizada, e é a única saída para um documento que falhou
  na indexação: a sincronização não reenvia um arquivo cujo marcador não mudou
  (#105, D6), então esperar a próxima rodada não resolve.
- **Onde a explicação aparece:** no cabeçalho do card de documentos, à direita,
  no lugar do botão, "Somente leitura — o conteúdo vem da pasta" (prancha 4a). No
  estado vazio, "Nenhum documento nesta base." continua, e a segunda linha passa a
  dizer que os documentos entram pela sincronização com a pasta, sem o convite a
  adicionar.
- **Mecanismo:** `KnowledgeDocumentsCard` ganha a prop `readOnly: boolean`,
  obrigatória, e a página passa `data.contentMode === 'Synced'`. Com `readOnly`,
  o card não renderiza os três controles e não recebe `onAdd`, `onUpdate` nem
  `onDelete` (a página não os passa). A página não monta o
  `KnowledgeDocumentModal` nem o modal de exclusão em base sincronizada.

- *Descartado, botões desabilitados com dica:* o operador veria ações que nunca
  vai poder usar, e a validação da #106 tratou a presença deles como defeito; a
  prancha também não os desenha.
- *Descartado, esconder também "Reindexar":* deixaria o documento em falha sem
  saída nenhuma na tela, para uma ação que o `apps/api` aceita.
- *Descartado, explicação num `Alert` acima da tabela:* repete a cada visita o
  que é propriedade permanente da base; o rótulo no cabeçalho diz o mesmo sem
  disputar atenção com a faixa de itens não utilizáveis.

### D2. Card de Origem e os estados da sincronização

Card "Origem — pasta sincronizada" (`KnowledgeBaseSyncOriginCard`,
apresentacional), entre a descrição e os agentes, só em base `Synced`. Três
colunas: **Pasta** (ícone, `folderName`, link "Abrir no {provedor} ↗" para
`folderUrl` em nova aba), **Provedor** (`providerLabel` da #106: chave crua para
provedor desconhecido, e então o link vira "Abrir a pasta ↗") e **Última
sincronização concluída**. "Sincronizar agora" fica no cabeçalho (D5).

O estado é lido de `syncState` por uma função pura, `syncStatus(syncState)`, em
`utils/syncState.ts`. **"Falhando" é `failingSince !== null`**, e só isso; é o
mesmo predicado do filtro da listagem (D6).

| estado | condição | coluna "Última sincronização concluída" | rótulo da pasta | alerta |
|---|---|---|---|---|
| nunca sincronizou | `lastFinishedAt`, `lastCompletedAt` e `failingSince` nulos | "Nenhuma ainda" e, abaixo, em `dimmed`: "A base entra na próxima rodada de sincronização, que roda a cada 5 minutos." | "Pasta" | nenhum |
| em dia | `failingSince` nulo, `lastCompletedAt` preenchido | data e hora, e abaixo, em verde: "Sem erros no último ciclo" | "Pasta" | nenhum |
| falhando, com sincronização concluída antes | `failingSince` e `lastCompletedAt` preenchidos | data e hora, e abaixo, em vermelho: "Falhando desde {data e hora}" | "Pasta — nome na última sincronização concluída" | o da D4 |
| falhando, sem nunca ter concluído | `failingSince` preenchido, `lastCompletedAt` nulo | "Nenhuma ainda" e "Falhando desde {data e hora}" | "Pasta — nome no cadastro" | o da D4 |

- **"Nunca sincronizou" não é falha:** nenhum vermelho, nenhum alerta, nenhum
  ícone de erro. O texto é de espera, em tom neutro. "A cada 5 minutos" é
  afirmação sobre outro app, e o comentário do componente leva
  `SyncSchedulerService.cs:13` e o gatilho (o intervalo mudar). A frase **não**
  promete prazo: a próxima rodada só começa quando a anterior termina, e sem
  `Api:BaseUrl` no `apps/connectors` nenhuma roda. O caso sem rodada aparece no
  "Sincronizar agora" (`sync-not-configured`, D4).
- **"Sem erros no último ciclo"** é verdade quando `failingSince` é nulo e
  `lastCompletedAt` existe: o último ciclo gravado foi um sucesso. Fica.
- **"Nome no cadastro"** no último estado: o `folderName` gravado é o da validação
  da pasta no cadastro (#104), porque nenhum sucesso o trocou. Chamá-lo de "nome na
  última sincronização concluída" afirmaria uma sincronização que não existiu.
- **Base inativa** mostra o mesmo card: a rodada sincroniza base inativa (#105).
- **Datas** em `toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' })`,
  no fuso do navegador, por uma função só (`formatSyncInstant`), usada também na
  listagem.
- **Erro em `lastError` sem `failingSince`** não é estado alcançável pela regra do
  `apps/api` (os dois são gravados e limpos juntos). A função trata como "em dia",
  porque o predicado de falha é um só, e o teste nomeia o caso.

- *Descartado, mostrar `lastFinishedAt` como "última sincronização":* em falha ele
  é o instante do último ciclo **falho**, e a coluna diria que a base sincronizou
  quando ela não sincronizou. `lastFinishedAt` serve só ao acompanhamento (D5).
- *Descartado, "nunca sincronizou" com o travessão:* travessão é "não sei"
  (convenção 13); aqui a resposta chegou e diz que ainda não houve ciclo, o
  quarto estado da convenção, que se diz com explicação.
- *Descartado, estado de sincronização numa aba ou na aba Diagnóstico:* a issue
  recusa, porque a aba trata do índice, e não da relação entre a pasta e a base.

### D3. Arquivos ignorados

Card "Arquivos da pasta que não entraram na base" (`KnowledgeBaseIgnoredFilesCard`,
apresentacional) na aba Documentos, depois do card de documentos (prancha 4a), só
em base `Synced`. Cada linha: nome do arquivo e motivo (texto da D4).

| `ignoredFiles` | o que significa | o que a tela diz |
|---|---|---|
| `null` | nenhum ciclo bem-sucedido foi gravado: a lista **não existe** | "A lista aparece depois da primeira sincronização concluída." Sem contagem no cabeçalho |
| `[]` | o último ciclo bem-sucedido não recusou nenhum arquivo | "Nenhum arquivo foi recusado na última sincronização concluída." Cabeçalho "Nenhum" |
| lista | os arquivos recusados no último ciclo bem-sucedido | uma linha por arquivo, na ordem da resposta; cabeçalho "1 arquivo" / "N arquivos" |

- **Nulo e vazio nunca têm o mesmo texto**, e a asserção que protege isso é
  negativa: com `null`, a tela não contém "Nenhum arquivo"; com `[]`, não contém
  "A lista aparece". O vazio diz "não foi recusado", e não "todos entraram":
  arquivo em contenção (`503`, #105 D7) nem entra nem é recusado.
- **Em falha**, a lista é a do último sucesso (a falha não a toca, #102). O card
  diz isso na primeira linha: "Lista da última sincronização concluída, em {data}."
  Sem essa linha, a lista seria lida como o estado da pasta agora.
- **Sem link para o arquivo:** `externalRef` é o id no provedor, e montar a URL do
  Drive a partir dele é conhecimento do conector que o painel não tem.
- **Contagem no cabeçalho** é medida (o tamanho de uma lista que chegou) e pode
  ser exibida; com `null`, não há o que contar, e o cabeçalho fica sem contagem.

- *Descartado, esconder o card enquanto `ignoredFiles` é nulo:* o operador não
  saberia se a tela tem a lista; a ausência diria tanto "nada recusado" quanto
  "ainda não sei".
- *Descartado, juntar os ignorados à tabela de documentos:* arquivo ignorado pode
  ter documento (a versão anterior aceita fica, #105 D6) e pode não ter; uma linha
  só misturaria o estado do documento com o do arquivo na pasta.

### D4. Texto por código

Três tabelas, todas em `apps/frontend/src/features/knowledge-bases/utils/`:

- **`connectorErrors.ts` (a tabela da #106) ganha o contexto `sync-request`**, para
  a resposta do `POST /connectors/knowledge-bases/{id}/sync`. Rede, `401`, `403`,
  código desconhecido e falta de código reaproveitam as linhas de lá sem mudar
  nada. Os quatro códigos da rota entram na mesma função.
- **`syncStateMessages.ts` (novo)** tem `syncFailureMessage(lastError)` e
  `ignoredFileReason(file)`, que leem um `{ code, detail }` gravado, e não um erro
  de requisição. Onde o código e a frase valem nos dois lugares, a frase vem de
  `connectorErrors.ts`, que passa a exportar `sharedCodeSentence(code, detail,
  providerKey)` para `provider-not-configured`, `api-not-configured`,
  `provider-auth-failed` e `provider-error`. A primeira frase de `access-denied`
  também é a de lá. O que muda é o remédio: no cadastro, "tente de novo" ou
  "escolha outra pasta"; aqui, a pasta não pode ser trocada e a próxima tentativa é
  automática.
- **Afirmações sobre outro app** (5 minutos, 1 MiB, Google Docs e `.md`) levam no
  comentário o arquivo e a linha de onde foram lidas e o gatilho (convenção 13).
- **Código desconhecido** cai num texto neutro com o código, nunca no texto de
  outro código.
- **Nenhum texto contém `exclu`, `remov` nem `apag`**, inclusive onde a frase
  descreve o que **não** aconteceu ou o que aconteceu com a pasta no Drive. A
  regra da #106 e da issue é que nenhum texto manda excluir; a asserção
  mecânica (substring) é a que protege isso, e escrever "nenhum documento foi
  excluído" a faria reprovar. Por isso "nenhum documento saiu da base" e "se a
  pasta deixou de existir no Drive" (C6).

  **Decisão do mantenedor (04/10/2026, revisão dos artefatos):** a proibição
  existe para impedir que a tela mande excluir a base, que é escopo da #136 (a
  rota existe desde a #108, mas o painel não oferece a ação). A garantia da issue
  ("nada foi excluído") foi reescrita para caber nela, e a varredura fica como
  está. **Quando a #136 chegar, ela revê esta regra junto com o botão de
  exclusão.**

**Falha da base** (`lastError.code`, alerta do card de Origem). Todo alerta tem
título, a frase do código, e duas frases fixas: a garantia e, quando cabe, o
remédio.

Frase de garantia, em todo código: "Nenhum documento saiu da base: ela continua
respondendo com o conteúdo da última sincronização concluída, que pode estar
desatualizado." Sem sincronização concluída (último estado da D2): "Nenhum
documento saiu da base, e nenhuma sincronização foi concluída ainda."

| `code` | título | texto |
|---|---|---|
| `access-denied` | Sem acesso à pasta | A conta {detail: e-mail} não tem acesso a esta pasta. ‹garantia› Para resolver, compartilhe a pasta com essa conta como Leitor. Se a pasta deixou de existir no Drive, crie uma nova base com outra pasta: esta base não pode ser apontada para outra pasta. |
| `folder-trashed` | Pasta na lixeira | A pasta está na lixeira do Drive. ‹garantia› Para resolver, restaure a pasta no Drive; a próxima sincronização volta a lê-la. |
| `not-a-folder` | A origem não é mais uma pasta | O item de origem desta base não é mais uma pasta no Drive. ‹garantia› Esta base não pode ser apontada para outra pasta: para sincronizar outra, crie uma nova base. |
| `rate-limited` | Limite de chamadas do Google | O Google limitou as chamadas por um momento. A falha é passageira, e a próxima tentativa é automática, na próxima rodada de sincronização (a cada 5 minutos). ‹garantia› |
| `provider-unavailable` | O Google não respondeu | O Google não respondeu durante a sincronização. A falha costuma ser passageira, e a próxima tentativa é automática, na próxima rodada de sincronização (a cada 5 minutos). ‹garantia› |
| `provider-error` | O Google recusou a operação | O Google recusou a operação ({detail}). A próxima tentativa é automática, na próxima rodada de sincronização (a cada 5 minutos). ‹garantia› |
| `api-not-configured` | Configuração da instalação | A Drive API não está ativada no projeto da conta de serviço. É configuração da instalação, não da pasta. ‹garantia› |
| `provider-auth-failed` | Configuração da instalação | O Google recusou a credencial da conta de serviço. É configuração da instalação, não da pasta. ‹garantia› |
| `provider-not-configured` | Configuração da instalação | O provedor {nome} não está configurado no serviço de conectores desta instalação. ‹garantia› |
| `sync-api-error` | Falha no servidor | O servidor respondeu de forma inesperada durante a sincronização. A próxima tentativa é automática, na próxima rodada de sincronização (a cada 5 minutos). ‹garantia› |
| desconhecido | Falha na sincronização | A sincronização falhou com um código que o painel não reconhece (`{code}`). ‹garantia› |

- `rate-limited` não fala de acesso, conta nem compartilhamento (asserção
  negativa: não contém "acesso" nem "compartilh").
- `access-denied` sem `detail` diz "A conta de serviço não tem acesso…"; o e-mail
  vem do `detail` gravado (`detail` igual ao e-mail, #105), sem chamar o
  `apps/connectors`, então aparece também sem `VITE_CONNECTORS_BASE_URL`.
- Os quatro primeiros têm remédio na pasta; os de "Configuração da instalação"
  não pedem nada ao operador da base além de levar o caso a quem administra.

**Arquivo ignorado** (`ignoredFiles[].code`, coluna de motivo):

| `code` | origem | texto |
|---|---|---|
| `shortcut-not-followed` | listagem | É um atalho, e a sincronização não segue atalhos. Coloque o arquivo em si na pasta. |
| `subfolder-not-synced` | listagem | É uma subpasta. Só os arquivos da raiz da pasta entram na base. |
| `unsupported-type` | listagem | Tipo não suportado ({detail: tipo MIME, quando houver}). Só Google Docs e arquivos .md entram na base. |
| `download-blocked` | listagem ou exportação | O download está bloqueado para leitores. Permita o download para leitores neste arquivo, no Drive. |
| `file-not-found` | download | O arquivo não foi encontrado na hora do download. Se ele continuar na pasta, a próxima sincronização tenta de novo. |
| `provider-error` | listagem ou download | O Google recusou a leitura deste arquivo ({detail}). A próxima sincronização tenta de novo. |
| `provider-unavailable` | download | O Google não respondeu ao exportar este arquivo. A próxima sincronização tenta de novo. |
| `access-denied` | download | A conta de serviço não tem acesso a este arquivo. Compartilhe o arquivo com a conta como Leitor. |
| `too-large` (#120) | upsert | Maior que o limite de 1 MiB depois da exportação ({detail} bytes). |
| `unsupported-source-type` (#120) | upsert | O servidor não aceitou o formato enviado pela sincronização. Não depende do arquivo: é falha da integração. |
| `null-character` (#120) | upsert | O conteúdo exportado tem um caractere nulo, que o servidor não aceita. Corrija o arquivo no Drive. |
| `empty-content` (#120) | upsert | O arquivo está vazio depois da exportação. Ele entra na base quando tiver conteúdo. |
| desconhecido | — | Motivo que o painel não reconhece (`{code}`). |

- `download-blocked` é o texto do comentário da etapa 0 na #107, e fica só na
  lista de arquivos, nunca no alerta da base.
- O `access-denied` de arquivo existe porque o `apps/connectors` mantém no
  arquivo a permissão negada de um arquivo individual (#105, D4, "fixado na
  implementação"); ele não usa a frase da pasta.
- Os quatro da #120 são lembrados pelo ciclo enquanto o marcador não muda
  (#105, D14): por isso o texto pede mudar o arquivo, e não "tentar de novo".
- `too-large` mostra o `detail` em bytes como veio, sem converter: a conversão
  afirmaria um arredondamento, e o número é o que o `apps/api` mediu.

**Resposta do "Sincronizar agora"** (`connectorErrorMessage(error, 'sync-request')`):

| status | `code` | texto |
|---|---|---|
| rede | — | (linha da #106) Não foi possível falar com o serviço de conectores. Ele pode estar fora do ar; tente de novo em instantes. |
| 404 | `knowledge-base-not-found` | O serviço de conectores não encontrou esta base entre as bases sincronizadas do servidor. É configuração da instalação. |
| 503 | `sync-not-configured` | O serviço de conectores desta instalação não está ligado ao servidor, e não sincroniza nenhuma base. É configuração da instalação. |
| 503 | `sync-api-unavailable` | O serviço de conectores não conseguiu falar com o servidor para confirmar a base. Tente de novo em instantes. |
| 502 | `sync-api-error` | O servidor respondeu de forma inesperada ao serviço de conectores. Tente de novo em instantes; se continuar, é configuração da instalação. |
| 401, 403, desconhecido, sem código | — | (linhas da #106, inalteradas) |

`sync-api-error` aparece nas duas tabelas com textos diferentes porque são dois
fatos: no `lastError` é o ciclo que recebeu resposta fora do contrato; no `502` do
pedido é a confirmação da base que falhou, antes de qualquer ciclo.

- *Descartado, um contexto novo `sync` dentro de `connectorErrorMessage` para os
  códigos gravados:* a função recebe um erro de requisição (`ConnectorsError` ou
  `ApiError`) e normaliza status e `detail`; o código gravado não tem status, e
  passá-lo por ela exigiria fabricar um erro.
- *Descartado, reaproveitar o texto do cadastro para `access-denied` e
  `folder-trashed`:* "tente de novo" e "escolha outra pasta" mandam o operador
  fazer o que a base sincronizada não permite.
- *Descartado, converter `contentBytes` em MiB:* ver acima.

### D5. "Sincronizar agora"

- **Cliente:** `requestKnowledgeBaseSync(id)` em `connectorsApi.ts`, sobre o
  `request<T>` da #106. A rota responde `202` **sem corpo**, e o `request<T>` de
  hoje faz `response.json()` em toda resposta `ok`; ele passa a devolver
  `undefined` para `202` e `204`. Hook `useRequestKnowledgeBaseSyncMutation()` em
  `useConnectors.ts`, sem `retry`.
- **Sem `VITE_CONNECTORS_BASE_URL`** (`connectorsBaseUrl() === null`): o botão
  aparece **desabilitado**, com a linha abaixo do card: "A sincronização manual
  não está habilitada neste painel: falta o endereço do serviço de conectores na
  construção do painel." O resto do card, o alerta e os ignorados continuam, porque
  vêm do `apps/api`. Nenhuma chamada sai para o `apps/connectors` (o
  `request<T>` já garante, e a tela nem chama).
- **Rótulo:** "Sincronizar agora"; com a base falhando, "Tentar sincronizar agora"
  (prancha 4b).
- **Fluxo, com o estado na página:**
  1. clique: o botão entra em carregamento; a página **refaz a consulta da base**
     (`refetch`) e guarda o `lastFinishedAt` dela como linha de base. A releitura
     é o que impede a linha de base velha: com o detalhe aberto há minutos, uma
     rodada periódica pode ter terminado desde a última leitura, e a primeira
     consulta depois do pedido acusaria um fim que não é o do pedido;
  2. `POST`; erro: `Alert` vermelho persistente no card de Origem com o texto da
     D4, botão de volta ao normal, nenhuma consulta periódica;
  3. `202`: estado "solicitada". O botão fica desabilitado, e o card mostra um
     `Loader` com "Sincronização solicitada. Aguardando o resultado…". **Nenhum
     texto de sucesso** neste estado;
  4. a consulta da base passa a se repetir a cada **4 s** enquanto
     `lastFinishedAt` for igual à linha de base e o pedido tiver menos de
     **5 minutos**;
  5. `lastFinishedAt` diferente da linha de base: fim. Com `failingSince` nulo,
     "Sincronização concluída em {data e hora de `lastCompletedAt`}." em verde; com
     falha, "A sincronização terminou com falha." e o alerta da D4, que a mesma
     resposta já atualizou. A página invalida a listagem de documentos e o resumo
     de indexação, porque o ciclo pode ter criado, atualizado ou tirado documentos;
  6. 5 minutos sem mudança: fim por limite. "Nenhum resultado foi gravado em 5
     minutos. A sincronização pode continuar rodando, ou ter terminado sem gravar
     resultado; o estado acima é o último gravado." em amarelo, e o botão volta.
- **A condição é função pura** (`syncWaitInterval(request, base, now)` em
  `utils/syncState.ts`), usada como `refetchInterval` por
  `useKnowledgeBaseQuery(id, { syncRequest })`, que ganha o parâmetro opcional;
  sem ele, a consulta é a de hoje. Guarda pareado da convenção 20: a asserção
  determinística sobre a opção real resolvida contra a query real do cache, e a
  comportamental com timers falsos.

  **Corrigido na implementação:** a primeira versão desta decisão passava ao hook
  uma função `refetchInterval` montada pela página. A causa da troca: com a
  condição montada fora do hook, o guarda pareado de `useKnowledgeBases.test.ts`
  teria de reconstruir no teste a função que a página passa, e a asserção
  determinística deixaria de ser sobre o artefato real (convenção 11). Com o
  pedido como parâmetro, o hook é o único lugar que liga a condição, e o guarda
  o exercita direto. Também entrou `syncRequestPhase` (`waiting`, `finished`,
  `timed-out`) ao lado da função, porque a página faz a mesma pergunta para
  escolher o texto do card.
- **Intervalo de 4 s:** o mesmo das outras duas consultas periódicas do detalhe,
  que já foram medidas em uso. Um ciclo sem mudança custa poucos segundos (o que
  restringe é baixar, cerca de 1.600 arquivos por minuto, etapa 0).
- **Limite de 5 minutos:** é o intervalo da rodada. Até ele, um ciclo de uma base
  com download grande (o export de um Doc tem teto de 120 s por arquivo, #105) tem
  tempo de terminar; e depois dele a próxima rodada periódica já teria gravado,
  então esperar mais não muda o que a tela sabe. São no máximo 75 consultas por
  pedido. O limite existe porque há caminhos que não gravam nada (Context).
- **Aba em segundo plano:** `refetchIntervalInBackground` fica no default
  (`false`), como nas outras; o limite é contado no relógio, então uma aba
  escondida por 5 minutos volta no estado "por limite", com o estado gravado
  atualizado pelo `refetchOnWindowFocus`.
- **Sair da página** descarta o acompanhamento: o estado é da página, e voltar
  mostra o estado gravado.

- *Descartado, mostrar sucesso no `202`:* a issue proíbe; o `202` diz só que o
  pedido foi aceito.
- *Descartado, acompanhar até `lastFinishedAt` passar do instante do clique:* o
  instante é do relógio do navegador, e o `lastFinishedAt` é do relógio do
  `apps/api`; qualquer diferença entre os dois faria o fim ser acusado cedo ou
  nunca.
- *Descartado, consulta periódica sem limite:* com o `apps/api` fora do ar no
  ciclo, ou exceção no ciclo, nada é gravado, e a tela esperaria para sempre com o
  botão desabilitado.
- *Descartado, linha de base pelo dado em cache:* ver o passo 1.

### D6. Listagem e filtro "Com falha"

- **Linha de origem** sob a descrição de cada base, na coluna Base (prancha 1),
  `xs` e `dimmed`: ícone de documento e "Manual", ou ícone de pasta e
  "{provedor} › {folderName}". O nome da pasta é texto, sem link (o link do Drive
  fica no detalhe).
- **Linha de falha** abaixo dela, só com `failingSince` preenchido: ícone de alerta
  e "Sincronização falhando desde {data e hora}", na cor de falha que a coluna
  Indexação já usa (`statusPresentation('Failed').color`), para falha ter a mesma
  aparência nas duas colunas.
- **"Nunca sincronizou" não tem linha na listagem:** não é problema, e a
  listagem só sinaliza o que pede atenção.
- **Filtro:** `matchesStatus` de `STATUS_FAILED` passa a ser
  `hasFailure(resumo) || isSyncFailing(base)`, com `isSyncFailing` em
  `utils/syncState.ts` (o mesmo predicado da D2). Inclui base inativa, como
  hoje.
- **Sem o resumo de indexação, o filtro continua desabilitado e cai para
  "Todas"**, mesmo havendo base com sincronização falhando (aceite da issue). Filtrar
  só pela sincronização esconderia em silêncio as falhas de indexação, e a opção
  diria "estas são as bases com falha" sem saber. O aviso de resumo indisponível
  continua o de hoje.

- *Descartado, coluna "Origem" nova:* as cinco larguras foram medidas contra o
  protótipo numa conferência (`KnowledgeBaseTable.tsx`), e a prancha põe a origem
  dentro da coluna Base.
- *Descartado, filtro só pela sincronização quando o resumo cai:* recusado pela
  issue, pelo motivo acima.
- *Descartado, badge "Falha" na coluna Estado:* a D9 da etapa da listagem já
  recusou o badge, por repetir em tom de alerta o que outra coluna diz.

### D7. Base manual continua igual

- **Detalhe:** nenhum componente novo renderiza com `contentMode === 'Manual'`
  (card de Origem, card de ignorados, linha de explicação), e o
  `KnowledgeDocumentsCard` recebe `readOnly={false}`, que é o comportamento de
  hoje. Os testes de detalhe de base manual que já existem continuam passando sem
  mudança, e um teste novo afirma a ausência dos dois cards e de qualquer chamada
  ao `apps/connectors` em base manual.
- **Listagem:** a base manual ganha só a linha "Manual" (D6), e nunca a linha de
  falha.
- **Tipo:** `KnowledgeBase` ganha `syncState: KnowledgeBaseSyncState | null`,
  obrigatório, como o fio. Os testes que montam `KnowledgeBase` à mão ganham
  `syncState: null` (manual) ou um estado (sincronizada).

### D8. Correções de protótipo

Divergências entre as pranchas 1, 4a e 4b e o código ou as regras de hoje, e o
que prevalece. Entram na lista viva "Correções de protótipo" do `02`.

| # | prancha | o protótipo | o que prevalece | por quê |
|---|---|---|---|---|
| C1 | 1 | "1 indexando · 1 na fila" na coluna Indexação | "em andamento", como hoje | o resumo não separa `Pending` de `Indexing` (regra já registrada em `indexingSummary.ts`) |
| C2 | 1 | "Atendente de Suporte, Triagem +5" | a lista inteira de nomes, como hoje | não é desta change; a coluna já foi conferida |
| C3 | 1 | "falhando desde 25/09, 09:10", sem ano; detalhe com ano | data e hora com ano nas duas telas, pela mesma função | uma falha antiga, de outro ano, seria lida como recente |
| C4 | 4a, 4b | "Mais 7 documentos" no fim da tabela | a lista inteira, como hoje | a rota não pagina, e a tabela de hoje não corta |
| C5 | 4a, 4b | aba Histórico | sem a aba | é da #101 |
| C6 | 4b | "Nenhum documento foi excluído" e "Se a pasta foi excluída" | "Nenhum documento saiu da base" e "Se a pasta deixou de existir no Drive" | asserção de que nenhum texto contém `exclu`, `remov` nem `apag` (D4), que existe para a tela não mandar excluir a base (#136); a garantia foi reescrita para caber nela, aprovado pelo mantenedor em 04/10/2026, e a #136 revê a regra junto com o botão |
| C7 | 4b | borda vermelha no card de Origem (`#e8a0a0`) e faixa `#fff5f5` | `Alert` do Mantine (variante `light`, troca sozinha de esquema) dentro do card, borda do tema | convenção 16: a paleta da prancha só existe no claro; o `Alert` já resolve o papel (precedente da faixa de falha de documento) |
| C8 | 4a | motivos "Tipo não suportado — só Google Docs e .md" e "Maior que 1 MiB depois da exportação" | os textos da D4, por código | a prancha desenha dois motivos; o sistema tem doze códigos de arquivo ignorado (mais o texto neutro do desconhecido), e o `too-large` traz o tamanho medido |
| C9 | 4b | só `access-denied` desenhado | um alerta por código, D4 | os outros códigos de base existem (#105) |
| C10 | — | não desenhados: nunca sincronizou, falhando sem nunca ter concluído, sincronização solicitada, concluída, terminada com falha, sem resultado no limite, erro do pedido, botão indisponível sem a variável, ignorados nulos e vazios, documentos vazios em base sincronizada | desenhados por esta change com os padrões do painel (`Alert`, `Loader`, texto `dimmed`) | estados que o sistema tem; a issue exige três deles (andamento, fora do ar, desabilitado) |
| C11 | 4b | rótulo "nome na última sincronização concluída" sempre que falha | "nome no cadastro" quando nenhuma sincronização foi concluída | D2: o nome é o da validação no cadastro |
| C12 | 4a | "Abrir no Google Drive ↗" fixo | "Abrir no {provedor} ↗", e "Abrir a pasta ↗" para provedor desconhecido | a #106 já fixou nome de exibição por mapa local (C6 de lá) |

A conferência visual (D9) acrescenta as correções que achar.

### D9. Conferência visual, no molde da #106

- **Painel:** `npm run dev` deste worktree numa porta livre da faixa
  **57100–57199** (conferida com `lsof -i :<porta>`), com `VITE_API_BASE_URL` e
  `VITE_CONNECTORS_BASE_URL` apontando para portas da mesma faixa **sem nenhum
  processo escutando**: toda resposta do `apps/api` e do `apps/connectors` é
  montada pelo CDP (`Fetch.enable` com os dois padrões, `Fetch.fulfillRequest`
  com cabeçalhos CORS e preflight `OPTIONS`; requisição pausada para
  "carregando"; `Fetch.failRequest` para "fora do ar"). Sem backend real. Token do
  operador gravado no `sessionStorage` pelo CDP. Portas usadas registradas no
  `tasks.md`.
- **Variável ausente:** uma segunda instância do Vite, sem
  `VITE_CONNECTORS_BASE_URL`, para o botão indisponível, conferindo pelo log do CDP
  que nenhuma requisição sai para o `apps/connectors`.
- **Acompanhamento:** a sequência "solicitada → concluída" montada trocando a
  resposta de `GET /knowledge-bases/{id}` entre consultas (o `lastFinishedAt` muda
  na terceira); o limite, com o relógio da página adiantado pelo CDP
  (`Emulation.setVirtualTimePolicy` ou troca de `Date.now`), registrado no
  `tasks.md`.
- **Chrome headless por CDP**, claro e escuro, a 1440px e a ~1860px.
- **Estados:** listagem com base manual, sincronizada em dia, sincronizada
  falhando e o filtro "Com falha" (1); detalhe em dia com ignorados (4a); sem
  acesso (4b); nunca sincronizou com ignorados nulos; falhando sem nunca ter
  concluído; `rate-limited`; ignorados vazios; documentos vazios em base
  sincronizada; solicitada; concluída; terminada com falha; sem resultado no
  limite; pedido com o `apps/connectors` fora do ar; botão indisponível; detalhe de
  base manual (3a, para afirmar que não mudou).
- **Dimensões comparadas** contra as pranchas: posição e altura do card de Origem,
  grade das três colunas, larguras da tabela de documentos sem a coluna Ações
  (56/16/18 na prancha), altura das linhas de ignorados, altura da linha de origem
  e da de falha na listagem, e que a linha da listagem não muda as larguras das
  colunas. Iterar até uma rodada sem achado; capturas em `design/capturas/`,
  nomeadas `<estado>_<esquema>_<largura>.png`, com as medidas em
  `design/capturas/medidas-r<N>.json`.
- **A validação manual pelo mantenedor fica pendente** (convenção 14), e o archive
  espera por ela.

**Resultado (04/10/2026): duas rodadas, a segunda sem achado.** 16 estados × 2
esquemas × 2 larguras = 64 capturas por rodada. O preflight `OPTIONS` foi
atendido pelo próprio `Fetch`. Nas quatro capturas da instância sem
`VITE_CONNECTORS_BASE_URL`, o log do `Fetch` registrou chamadas ao `apps/api` e
**nenhuma** ao `apps/connectors`. O "sem resultado no limite" foi produzido
encurtando, só naquela página, os `setTimeout` de 60 s ou mais para 1,5 s antes do
clique; o código da página não muda. As pranchas foram medidas no mesmo Chrome,
abertas como arquivo, para a régua de dimensão. O contraste de cada texto colorido
novo foi medido contra o fundo composto (com a transparência do `Alert`).

| # | o quê | medido | correção |
|---|---|---|---|
| R1-1 | coluna Documento estreita na tabela somente leitura | 581/280/321 px (49/24/27%) contra 692/221/244 na prancha 4a (60/19/21%): sem a coluna Ações, o layout automático deu largura demais a Fragmentos | larguras da prancha (56/16/18, `layout="fixed"`) só com `readOnly`; medido depois: 735/210/236 (62/18/20%) a 1440. A tabela da base manual não muda |
| R1-2 | verde de sucesso abaixo de 4,5:1 no claro | "Sem erros no último ciclo" e "Sincronização concluída em": **3,89:1** com `--mantine-color-green-text` (tom 6) | variável `--buteco-ok-text` no resolver do tema, tom 9 no claro e 4 no escuro (o `--ok` do protótipo; convenção 16, porque nenhum token troca entre esses dois); medido depois: 5,35:1 no claro e 7,62:1 no escuro |

Os dois acertam a tela **na direção da prancha**, e não a contrariam; por isso não
entram na D8. Medido e não corrigido, por ser o padrão da casa e não desta change:

- o card de Origem tem 124 px de altura contra 147 na prancha (157 e 264 px com
  status e com alerta). A diferença está toda no cabeçalho de 40 px e no `py="sm"`
  do `SectionedCard`, os mesmos de todos os cards do detalhe;
- "Somente leitura — o conteúdo vem da pasta" mede **4,14:1** no claro (4,6:1 no
  escuro). É o mesmo `xs` + `dimmed` sobre a faixa de cabeçalho do contador "2
  agentes" vizinho, que mede os mesmos 4,14:1 — defeito anterior a esta change,
  aberto como **#148** (convenção 23). Trocar o tom só neste card deixaria os
  cabeçalhos diferentes entre si;
- motivo de arquivo ignorado com tipo MIME longo quebra em duas linhas a 1440 (a
  linha fica com 58 px contra 42); a 1860 cabe numa. É texto que quebra, e não
  defeito; o tipo MIME fica, porque é o que diz ao operador qual arquivo é;
- "A base entra na próxima rodada de sincronização…" quebra em duas linhas a 1440,
  pelo mesmo motivo.

- *Descartado, subir `apps/api` e `apps/connectors` locais:* mesmos motivos da D7
  da #106, e o ciclo real precisaria do Google.

### D10. Onde fica cada peça

- **Página (`KnowledgeBaseDetailPage`)** guarda o pedido de sincronização
  (`{ baseline, requestedAt, phase }`), chama a mutação, passa o pedido à
  consulta da base, decide `readOnly` e não monta os modais de documento em base
  sincronizada. Os componentes novos são apresentacionais e não importam hook
  (convenção 7).
- **Testes da página em dois arquivos**, como na #106: o
  `KnowledgeBaseDetailPage.test.tsx` de hoje continua cobrindo a base manual; os
  aceites desta change ficam em `KnowledgeBaseDetailPage.sync.test.tsx`, com o
  `fetch` global interceptado e sem mock de módulo, porque a negativa "nenhuma
  chamada ao `apps/connectors`" afirma sobre tudo o que saiu, e cada uma confere
  antes que o interceptor viu as chamadas ao `apps/api`.

## Árvore de pastas proposta

```
apps/frontend/src/features/knowledge-bases/
├── api/
│   ├── connectorsApi.ts                       (+ requestKnowledgeBaseSync; 202/204 sem corpo)
│   ├── connectorsApi.test.ts                  (+ sync: URL, método, 202, erros, sem variável)
│   ├── useConnectors.ts                       (+ useRequestKnowledgeBaseSyncMutation)
│   ├── useConnectors.test.ts
│   ├── useKnowledgeBases.ts                   (+ syncRequest opcional em useKnowledgeBaseQuery)
│   └── useKnowledgeBases.test.ts              (+ guarda pareado da consulta periódica)
├── components/
│   ├── KnowledgeBaseSyncOriginCard.tsx        (novo: D2, alerta da D4, botão e acompanhamento da D5)
│   ├── KnowledgeBaseSyncOriginCard.test.tsx   (novo)
│   ├── KnowledgeBaseIgnoredFilesCard.tsx      (novo: D3)
│   ├── KnowledgeBaseIgnoredFilesCard.test.tsx (novo)
│   ├── KnowledgeDocumentsCard.tsx             (+ readOnly)
│   ├── KnowledgeDocumentsCard.test.tsx        (+ somente leitura)
│   ├── KnowledgeBaseTable.tsx                 (+ linha de origem e de falha)
│   └── KnowledgeBaseTable.test.tsx
├── pages/
│   ├── KnowledgeBaseDetailPage.tsx            (origem, ignorados, readOnly, pedido de sincronização)
│   ├── KnowledgeBaseDetailPage.test.tsx       (base manual: + syncState null, + nada novo aparece)
│   ├── KnowledgeBaseDetailPage.sync.test.tsx  (novo: fetch interceptado, aceites da issue)
│   ├── KnowledgeBaseListPage.tsx              (filtro com isSyncFailing)
│   └── KnowledgeBaseListPage.test.tsx         (+ filtro com sincronização falhando)
├── types/
│   └── knowledgeBase.ts                       (+ KnowledgeBaseSyncState, syncState)
└── utils/
    ├── connectorErrors.ts                     (+ contexto sync-request, sharedCodeSentence)
    ├── connectorErrors.test.ts
    ├── syncState.ts                           (novo: syncStatus, isSyncFailing, syncWaitInterval, formatSyncInstant)
    ├── syncState.test.ts                      (novo)
    ├── syncStateMessages.ts                   (novo: syncFailureMessage, ignoredFileReason)
    └── syncStateMessages.test.ts              (novo: um caso por código, negativas)

apps/frontend/src/
├── theme.ts                                   (+ --buteco-ok-text por esquema; achado R1-2 da D9)
└── theme.test.ts                              (+ a variável e o contraste do tom 9)

openspec/changes/frontend-detalhe-base-sincronizada/design/
├── README.md, Main.dc.html, Detalhe.dc.html, DetalheFalha.dc.html, canvas.json
└── capturas/                                  (conferência, D9)
```

Os testes que montam `KnowledgeBase` à mão em outras features (agentes, vínculo,
inventário) ganham `syncState`. Nada em `libs/`. `theme.ts` e `theme.test.ts`
entraram pela conferência visual (D9, R1-2), e não estavam nesta árvore aprovada.

## Risks / Trade-offs

- **[Risco] Uma rodada periódica terminar entre a releitura e o `POST`** → a tela
  acusaria o fim do ciclo periódico como o do pedido. A janela é a de uma
  requisição; o estado mostrado continua sendo o gravado de verdade, e o ciclo
  pedido grava depois, visível na próxima leitura. Aceito e nomeado.
- **[Risco] O acompanhamento esperar para sempre** → limite de 5 minutos (D5),
  com teste de timers falsos que avança o relógio além do limite e afirma que a
  consulta para e o botão volta.
- **[Risco] Sucesso antes do estado mudar** → teste com o `fetch` interceptado
  que responde `202` e mantém `lastFinishedAt`: nenhum texto de conclusão aparece
  e o botão continua desabilitado.
- **[Risco] Texto que manda excluir** → asserção sobre todos os textos das três
  tabelas e do alerta montado: nenhum contém `exclu`, `remov` nem `apag`.
- **[Risco] Nulo e vazio dos ignorados com o mesmo texto** → asserções negativas
  cruzadas (D3).
- **[Risco] Botões de documento voltarem em base sincronizada** → asserção
  negativa no componente e na página, e a positiva na base manual.
- **[Trade-off] Afirmações sobre outro app no texto** (5 minutos, 1 MiB, Docs e
  `.md`) → comentário com arquivo, linha e gatilho (convenção 13). Se o intervalo
  ou o teto mudar, o texto mente até alguém ler; é o custo de dar o número ao
  operador.
- **[Risco] Conflito de edição com a #138 e a #47** em `02`, `CHANGELOG.md` → seções
  acrescentadas, nunca trechos reescritos.

## Migration Plan

Sem migração. O deploy do frontend sem `VITE_CONNECTORS_BASE_URL` publica o
detalhe com o botão indisponível, e o estado gravado continua aparecendo. Reverter
é reverter o frontend.

## Open Questions

Nenhuma de produto. Os textos da D4, o intervalo e o limite da D5 passam pela
revisão do mantenedor junto com este documento.
