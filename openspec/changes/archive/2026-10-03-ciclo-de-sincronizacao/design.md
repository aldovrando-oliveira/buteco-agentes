## Context

A linha de bases sincronizadas tem cinco peças. Estão na `main` (`0499161`): o
catálogo e as rotas de `/sync` no `apps/api` (#102), o `apps/connectors` com o
conector Google Drive (#103), a criação da base com a pasta validada (#104) e o
código nas recusas de conteúdo (#120). Esta change é a quarta peça: o processo que
lê a pasta e escreve no `apps/api`. As telas são a #106 e a #107.

O que foi lido antes de propor, e que decide o desenho:

**O que o `apps/connectors` já oferece** (`apps/connectors/src/Buteco.Connectors/`):

- `IFolderNavigator.DescribeFolderAsync(folderId)` devolve `{ Id, Name, WebUrl }`
  depois de ler a pasta (`Connectors/IFolderNavigator.cs`).
- `IFolderContentSource.ListRootAsync(folderId)` devolve `RootListing(Files, Ignored)`
  e `GetMarkdownAsync(RootFile)` devolve o markdown. O contrato declara a #105 como
  consumidor esperado e "listagem completa ou falha"
  (`Connectors/IFolderContentSource.cs:8-16`). Só testes o chamam hoje.
- No Google, `ListRootAsync` lê a pasta antes de listar
  (`GoogleDriveFolderContentSource.cs:20`, `GoogleDriveFolders.ReadFolderAsync`) e
  pagina com `pageSize=1000` até o fim; qualquer página que falhe lança
  (`GoogleDriveClient.cs:46-64`). A classificação da listagem
  (`GoogleDriveFolderContentSource.cs:77-112`) põe nos ignorados, com o código:
  atalho (`shortcut-not-followed`), subpasta (`subfolder-not-synced`), tipo não
  suportado (`unsupported-type`, detalhe = `mimeType`), `canDownload=false`
  (`download-blocked`) e arquivo sem marcador (`provider-error`, detalhe
  `missing-modified-time` ou `missing-md5-checksum`).
- `RootFile.ExternalVersion` é `modifiedTime` para Doc e `md5Checksum` para `.md`
  (`:105`), o marcador opaco da D3 da #102.
- Toda falha sai como `ConnectorFailure(code, detail)`, com os códigos de
  `ConnectorCodes.cs` e o mapeamento de `GoogleDriveErrorMapper.cs`:
  `access-denied` (detalhe = e-mail da conta), `api-not-configured`, `rate-limited`,
  `download-blocked`, `file-not-found`, `provider-auth-failed`,
  `provider-unavailable`, `provider-error`, `not-a-folder`, `folder-trashed`.
- Limites por chamada ao Google: 30 s para metadado e listagem, 120 s para exportação
  e download (`GoogleDriveHttp.cs:20-24`). **Não há backoff** para cota em lugar
  nenhum do app.
- `TokenService.Issue(subject, lifetime)` já existe no app
  (`Auth/TokenService.cs:16`), cópia da do `apps/api`.
- A tabela de subjects tem `operator` e `service:api`, e é conferida no boot nos dois
  sentidos (`Auth/ConnectorsSubjectAuthorizationHandler.cs`,
  `ConnectorsSubjectRouteValidation`).
- O app não tem banco, nem `BackgroundService`, nem cliente HTTP de saída além do
  Google.

**O que o `apps/api` oferece em `/sync`** (D8 da #102, `KnowledgeSyncEndpoints.cs`),
todas para `service:connectors`:

| rota | o ciclo usa para |
|---|---|
| `GET /sync/knowledge-bases` | bases `Synced`, inclusive inativas: `id`, `provider`, `folderId`, `folderName`, `folderUrl`, `isActive` |
| `GET /sync/knowledge-bases/{id}/documents` | `externalRef`, `externalVersion`, `documentId`; `404` sem base, `409` em base `Manual` |
| `PUT /sync/knowledge-bases/{id}/documents` | upsert; `200` com `outcome` (`Created`, `Updated`, `Unchanged`); `400` de conteúdo **com** `code` e de forma **sem** `code` (#120); `404`; `409`; `503` com `Retry-After: 1` na contenção (D10) |
| `DELETE /sync/knowledge-bases/{id}/documents?externalRef=` | `204` (também para referência inexistente); `404`; `409`; `503` na contenção |
| `POST /sync/knowledge-bases/{id}/sync-results` | grava o desfecho: `Succeeded` com `folderName`, `folderUrl` e `ignoredFiles` (obrigatória, vazia se nada), ou `Failed` com `error: { code, detail }` e nada mais |

A regra do desfecho (D2 da #102) é do `apps/api`: sucesso troca nome, URL e lista de
ignorados e limpa o erro; falha preserva nome, URL, lista e a última concluída, grava
o erro e mantém o "falhando desde". **As duas atualizam `LastSyncFinishedAt`**, que é
o que o polling da #107 observa para saber que o ciclo pedido terminou.

**Medido na tarefa 1.2, sobre as respostas reais, sem divergência da tabela acima, e
com um acréscimo:** o upsert e o cadastro **publicam a indexação depois de gravar**, e
com o RabbitMQ fora do ar respondem **`500` com o documento já gravado** e sem job
(`UpsertSyncedDocumentCommandHandler.cs:104-106`). Na base sincronizada isso é
permanente: o upsert grava o documento **e o `externalVersion` novo** antes de a
publicação falhar, e no ciclo seguinte o marcador é igual ao gravado, então o arquivo
nem é reenviado (D6), e o documento fica sem job para sempre, sem sinal na tela. É
defeito do `apps/api`, fora desta change: **#138**, registrada como bloqueio da
**#119** (o ciclo só chega a produção com ela). Para o ciclo, o
`500` é "fora do contrato" (D4), e a base grava `Failed` com `sync-api-error`, que é
verdade: alguma coisa no `apps/api` falhou.

O `apps/api` decide se o conteúdo mudou (D3 da #102): marcador novo com título e texto
iguais é `Unchanged`, sem evento nem indexação. É o que cobre o aceite "mudança só de
metadado no Drive não gera evento": recompartilhar um Doc muda o `modifiedTime`
(P6 da etapa 0), o ciclo exporta, e o `apps/api` absorve. **Renomear não é esse caso**
(corrigido na verificação manual, tarefa 10.1): o título do documento é o nome do
arquivo (D13), então renomear muda o título, e a regra da D3 da #102 registra `Updated`
com `titleChanged: true` e `contentChanged: false`, sem reindexar.

**O molde do sentido inverso** (#104): `ServiceTokenDelegatingHandler` no `apps/api`
assina `service:api` a cada requisição, TTL fixo de 5 min, sem `libs/`
(`apps/api/src/Buteco.Api/Auth/ServiceTokenDelegatingHandler.cs`), espelho do do
`apps/inbox`. O endereço do outro app é opcional (`Connectors:BaseUrl`), validado no
boot sobre o host construído (`ConnectorsConfigurationValidation.cs`). O `apps/inbox`
chama o `apps/api` por `Api:BaseUrl`, obrigatória.

**Os testes de ida e volta** (`tests/ApiConnectorsRoundTrip.Tests`) têm uma classe,
`FolderValidationRoundTripTests`, e uma fixture, `RoundTripFixture`, com um Postgres
para o `apps/api` e o `TestServer` do `apps/connectors` como handler primário do
cliente do `apps/api` (`Support/RoundTripFixture.cs:115`). Só o sentido
`apps/api` → `apps/connectors` é provado hoje.

**Etapa 0** (`02-HISTORICO_E_STATUS.md`, seção da #100): um ciclo sem mudança custa
cerca de 105 unidades de cota por base de até 1000 arquivos; o teto é 325.000
unidades por minuto para a service account inteira; o que restringe é baixar, cerca
de 1.600 arquivos por minuto. Ausente da listagem é exclusão: lixeira, mover para
subpasta e descarte pelo dono fazem o arquivo sumir, sem marca. Cota estourada volta
`403`/`429`, e não é falha de acesso.

### Divergências entre o corpo da #105 e os comentários, e como ficam

| corpo da #105 | comentário da etapa 0 | como fica |
|---|---|---|
| "compara por `ExternalRef` e `ExternalVersion`, e baixa só o que mudou" | para Doc, o marcador (`modifiedTime`) muda com renomear e compartilhar sem mudar o conteúdo | o marcador é **filtro** de download: igual, não baixa; diferente, baixa e manda. Quem decide se mudou é o `apps/api` (D3 da #102), que responde `Unchanged`. Custa um export, não um evento |
| "arquivo ausente vira exclusão" | exclusão por ausência **só** com a listagem completa, todas as páginas | D6: e só com a pasta lida |
| (nada sobre cota) | cota é backoff, nunca erro de acesso, nunca exclusão | D5 |
| (nada sobre bloqueio de download) | `canDownload=false` vai para os ignorados com o motivo (também no comentário da #107: é por arquivo, não falha da base) | D4: já é o que a listagem da #103 faz |
| "falha ao obter a nova versão: o documento mantém o conteúdo anterior" | — | D4, e vale também para a recusa de conteúdo do `apps/api` (#120) |

Nenhum comentário contradiz o corpo; eles o restringem. Onde restringem, vale o
comentário.

## Goals / Non-Goals

**Goals:**

- Manter toda base `Synced` igual à raiz da pasta, a cada 5 minutos, sem o operador
  fazer nada, e sem nunca excluir documento por uma leitura incompleta.
- Deixar o desfecho de cada ciclo gravado no `apps/api`, com o código, para a #107.
- "Sincronizar agora" que a #107 possa acompanhar por polling.

**Non-Goals:**

- Telas (#106, #107), exclusão de base (#108), implantação em produção (#119),
  chave por serviço (#117).
- Subpastas, notificações push do Drive e Drive Compartilhado (#114).
- Várias instâncias coordenadas (D10).
- Qualquer mudança no `apps/api`.

## Decisions

### D1. O `apps/connectors` chama o `apps/api` com um `DelegatingHandler` próprio que assina `service:connectors`

`Auth/ServiceTokenDelegatingHandler.cs` no `apps/connectors`, terceira cópia do
molde (`apps/inbox` assina `service:inbox`, `apps/api` assina `service:api`): um token
novo a cada requisição de saída, com `TokenService.Issue("service:connectors",
5 min)`, a mesma `Auth:TokenSigningKey`, TTL fixo e não configurável pelo mesmo
motivo dos outros dois (o token nunca sai do processo). Sem `libs/`.

**O par:** o `apps/api` já aceita `service:connectors` nas cinco rotas de `/sync` e em
nenhuma outra (D6 da #102). Do lado do `apps/connectors`, `service:connectors`
continua **fora** da tabela de subjects: é o subject que este app assina, não o que
ele aceita; um token `service:connectors` que chegue aqui recebe `403`, como hoje.
O comentário do handler aponta para a tabela do `apps/api` e para os outros dois
handlers, como o da #104 faz.

**Ligação com a #117:** o `apps/connectors` já guarda a chave desde a #103; esta
change não acrescenta segredo, mas passa a **usá-la para escrever** no `apps/api`.
Comprometido o processo, a chave assina `operator` de qualquer jeito, então o risco
de hoje não muda de tamanho — muda de uso. Registrado aqui e na #119, que é onde a
decisão da #117 é cobrada antes do primeiro deploy.

- *Descartado, extrair o handler para `libs/`:* três cópias de 15 linhas, e a #117 vai
  redesenhar a assinatura (D1 da #103). Extrair agora seria extrair o que vai mudar.
- *Descartado, reaproveitar o token do operador no "Sincronizar agora":* o ciclo
  periódico não tem operador; e o `apps/api` daria ao ciclo o acesso inteiro do
  operador, onde a tabela da #102 dá cinco rotas.

### D2. `Api:BaseUrl` opcional: sem ela o ciclo fica desligado

O nome é o mesmo que o `apps/inbox` já usa para o mesmo papel (`Api__BaseUrl`), em
`ApiOptions` (`Options/ApiOptions.cs`). Três estados, como o `Connectors:BaseUrl` da
#104:

| `Api:BaseUrl` | boot | ciclo periódico | "Sincronizar agora" | navegação e descrição |
|---|---|---|---|---|
| ausente ou vazia | sobe, com aviso no log | não roda | `503` `sync-not-configured`, sem chamada de rede | sem mudança |
| URI absoluta `http`/`https` | sobe | roda | `202` | sem mudança |
| presente e inválida | **falha**, nomeando a chave e sem ecoar o valor | — | — | — |

Checagem sobre o host construído (`ValidateApiConfiguration`, convenção 8), com o
teste da composição real. Vazio conta como ausente, porque é o que `${VAR:-}` do
compose entrega.

**Por que opcional:** o `apps/connectors` já serve a #104 e a #106 sem o `apps/api`; a
validação de pasta e a navegação não podem cair porque o ciclo não foi configurado. E
em desenvolvimento quem sobe só o `apps/connectors` para navegar não precisa do
`apps/api` de pé.

**Em desenvolvimento**, `appsettings.Development.json` do `apps/connectors` ganha
`Api:BaseUrl` igual a `http://localhost:5017`, como o `apps/api` faz com
`Connectors:BaseUrl` e o `apps/inbox` com `Api:BaseUrl`: quem sobe os dois apps
localmente tem o ciclo rodando sem configurar nada. O `.env.example` documenta o
equivalente no bloco do `apps/connectors`; a variável `Api__BaseUrl` já está lá, para o
`apps/inbox`, com o mesmo valor.

**Consequência nos testes, e é a que mais custa se esquecida:** o `ConnectorsFactory`
sobe em `Development` (`Support/ConnectorsFactory.cs:19`, `:40`), então o valor do
arquivo de desenvolvimento valeria para toda a suíte atual, e cada teste ligaria o
agendamento contra `localhost:5017`. O factory passa a fixar `Api:BaseUrl` vazio por
`UseSetting` (vazio conta como ausente), e só os testes do ciclo o configuram. Um teste
afirma que, na composição padrão do factory, o `SyncSchedulerService` não está
registrado. O `RoundTripFixture` já fixa as configurações do `apps/connectors` por
`UseSetting` e passa a fixar esta também.

- *Descartado, obrigatória como no `apps/inbox`:* o `apps/inbox` não faz nada sem o
  `apps/api`; o `apps/connectors` faz. Exigir derrubaria a rota que a #104 consome por
  causa de uma configuração que ela não usa.
- *Descartado, `Sync:ApiBaseUrl`:* um segundo nome para o mesmo endereço que o
  `apps/inbox` já chama de `Api:BaseUrl`.
- *Descartado, deixar `Api:BaseUrl` fora do arquivo de desenvolvimento:* evitaria a
  consequência nos testes, mas quem sobe os apps localmente teria de descobrir que o
  ciclo está desligado pelo aviso no log, ao contrário dos outros dois endereços.

### D3. A cada 5 minutos, fixo; uma base por vez; sem sobreposição

- **Intervalo fixo de 5 minutos**, constante (`SyncSchedule.Interval`). É o da etapa
  0, e um ciclo sem mudança custa ~105 unidades por base: com 5 minutos, a cota
  sustenta milhares de bases. Não há cenário real de outro valor por ambiente
  (convenção 2); quem precisar de outro valor é o gatilho para torná-lo configurável.
- **Primeira rodada logo depois do boot**, e as seguintes a cada intervalo, com
  `PeriodicTimer(Interval, TimeProvider)` (conferido na DLL de referência do .NET
  10.0.9). Um deploy não atrasa a sincronização em 5 minutos.
- **Uma base por vez, na ordem de `GET /sync/knowledge-bases`.** Em sequência, a
  rodada nunca dispara chamadas em paralelo contra a mesma cota, e um `rate-limited`
  para tudo de uma vez (D5).
- **Sem sobreposição:** a próxima rodada só começa quando a anterior termina. O
  `PeriodicTimer` não acumula ticks: uma rodada que passa de 5 minutos é seguida pela
  próxima logo em seguida, e não por várias. Cada rodada registra a duração no log.
- **Gatilho para rever o paralelismo:** uma rodada que passe do intervalo de forma
  recorrente, observada nesse log.

- *Descartado, intervalo configurável:* sem cenário real (convenção 2).
- *Descartado, bases em paralelo com limite:* ganho só com muitas bases, e cada base
  em paralelo divide a mesma cota; com o piloto, a rodada sequencial é curta.
- *Descartado, primeira rodada só depois do primeiro intervalo:* cinco minutos sem
  sincronizar a cada deploy, sem ganho.

### D4. Falha de arquivo vai para os ignorados; falha de base vira `Failed`

O ciclo de uma base distingue três alcances:

| alcance | o que acontece |
|---|---|
| **arquivo** | o arquivo entra nos ignorados com o código; o documento existente continua como estava; o ciclo segue |
| **base** | grava `Failed` com o código e o detalhe; **nada é excluído**; a rodada segue para a próxima base |
| **rodada** | grava `Failed` na base em curso, quando cabe; a rodada **para**, e as bases restantes esperam a próxima |

Onde cai cada código:

| código | origem | alcance |
|---|---|---|
| `shortcut-not-followed`, `subfolder-not-synced`, `unsupported-type`, `download-blocked`, `provider-error` (sem marcador) | classificação da listagem (#103) | arquivo |
| `download-blocked` (`cannotExportFile`), `file-not-found` (sumiu entre listar e baixar), `provider-error`, `provider-unavailable` | `GetMarkdownAsync` | arquivo |
| `too-large`, `unsupported-source-type`, `null-character`, `empty-content` | `400` do upsert **com** `code` (#120) | arquivo; o `too-large` leva `contentBytes` no detalhe; lembrado pela D14 enquanto o marcador não mudar |
| `access-denied` (detalhe = e-mail), `not-a-folder`, `folder-trashed`, `provider-error`, `provider-unavailable`, `api-not-configured` | descrição da pasta ou listagem | base |
| `provider-auth-failed` | qualquer chamada ao Google | base |
| `provider-not-configured` | a base aponta para um provedor sem registro neste processo | base |
| `sync-api-error` | resposta do `apps/api` fora do contrato numa base (`400` **sem** `code`, `5xx` que não é a contenção, inclusive o `500` da publicação da indexação com o documento já gravado, e status inesperado) | base, gravado se a gravação passar |
| `rate-limited` | qualquer chamada ao Google | rodada (D5) |
| `apps/api` sem resposta, ou `401`/`403` | qualquer chamada ao `apps/api` | rodada, sem gravar (não há como) |
| `404` (ou `409`) do `apps/api` na base | qualquer rota da base | só aquela base, sem gravar (D8) |

**`provider-unavailable` num arquivo é de arquivo**, porque a pasta acabou de ser
lida e listada; um timeout de 120 s na exportação de um Doc grande é desse arquivo.
Na descrição ou na listagem, é de base.

**`provider-auth-failed` num arquivo é de base:** a credencial vale para todos os
arquivos. `api-not-configured` também, pelo mesmo motivo.

**Fixado na implementação, sem mudar a decisão:** ao obter o markdown, só
`rate-limited` (rodada), `provider-auth-failed` e `api-not-configured` (base) saem do
alcance de arquivo. **Qualquer outro código fica no arquivo**, inclusive um
`access-denied` de um arquivo individual (o `GoogleDriveErrorMapper` devolve
`insufficientFilePermissions` como `access-denied` também para arquivo): a pasta acabou
de ser lida, e o arquivo sem permissão própria é propriedade dele. A spec lista o
`access-denied` como de base só na descrição e na listagem
(`KnowledgeBaseSyncCycle.IsBaseLevelOnFile`).

**O `500` da publicação da indexação é `sync-api-error`, e não resolve o documento.**
Medido na 1.2: o `apps/api` grava o documento e o marcador novo e só então falha ao
publicar. A base grava `Failed`, mas no ciclo seguinte o marcador é igual e o arquivo
não é reenviado, então o documento fica sem job para sempre. O ciclo não tem como
corrigir isso sem decidir uma regra de indexação que é do `apps/api`. A correção é a
**#138**, registrada como **bloqueio da #119**: o ciclo não roda em produção com esse
caminho aberto (comentário na #138:
https://github.com/aldovrando-oliveira/buteco-agentes/issues/138#issuecomment-5974537045).

**`400` sem `code` no upsert é defeito do conector** (forma, #120), nunca
propriedade do arquivo, e por isso não vai para os ignorados: a lista diria ao
operador que o arquivo tem um problema que ele não tem.

- *Descartado, qualquer falha de arquivo derrubar a base:* um Doc com download
  bloqueado impediria toda a pasta de sincronizar, que é o oposto do que o
  comentário da #107 pede.
- *Descartado, `provider-unavailable` de arquivo derrubar a base:* um único export
  lento interromperia os outros arquivos, sem que a pasta esteja indisponível.

### D5. `rate-limited`: grava `Failed`, para a rodada, nunca exclui

Ao receber `rate-limited` de qualquer chamada ao Google, o ciclo:

1. para a base em curso sem excluir nada (o que já foi enviado por upsert fica, cada
   um é válido sozinho);
2. grava `Failed` com `rate-limited` nessa base;
3. **encerra a rodada**: a cota é da service account inteira, e a próxima base
   receberia o mesmo erro. As bases restantes ficam como estavam e entram na rodada
   seguinte, 5 minutos depois, que é o backoff.

**Por que gravar:** o "Sincronizar agora" da #107 acompanha `LastSyncFinishedAt`, e
só uma gravação o muda. Sem gravar, a tela esperaria para sempre um ciclo que já
acabou. E `rate-limited` não é `access-denied`: o código diz à #107 que não há nada a
compartilhar, só a esperar. Não toca a lista de ignorados nem o nome da pasta (regra
de `Failed` da D2 da #102).

**Efeito na #107, registrado lá:** a base fica com `failingSince` preenchido até o
ciclo seguinte dar certo, e aparece no filtro "Com falha". A mensagem da tela para
`rate-limited` precisa dizer que a falha é passageira e que a próxima tentativa é
automática
(https://github.com/aldovrando-oliveira/buteco-agentes/issues/107#issuecomment-5974415554).

- *Descartado, não gravar nada e tentar no próximo ciclo:* é o que a etapa 0 sugeria
  ("backoff"), mas o polling da #107 nunca terminaria, e o operador veria "última
  sincronização" envelhecer sem explicação (convenção 13).
- *Descartado, backoff exponencial dentro da rodada:* segura a rodada por minutos
  numa cota que se recompõe por minuto; o intervalo de 5 minutos já é o backoff.

### D6. Exclusão por ausência: só o que sumiu da listagem inteira, e só depois de ler tudo

O conjunto excluído é

```
referências gravadas no apps/api
  − arquivos suportados da listagem
  − arquivos ignorados da listagem
```

e a exclusão só acontece quando **todas** estas condições valem: a pasta foi descrita
com sucesso, a listagem voltou inteira (o contrato lança em qualquer página que
falhe), e a base não foi interrompida antes do passo de exclusão.

- **A exclusão vem depois dos upserts.** Uma base interrompida no meio (D4, D5) não
  exclui nada, nem o que já tinha sido decidido.
- **Arquivo que continua na pasta mas foi ignorado não é excluído.** Um `.md` que
  passou do teto, um Doc com download bloqueado ou um arquivo que virou tipo não
  suportado continuam na listagem, então **não sumiram**: o documento antigo fica, com
  o conteúdo da última versão aceita, e o arquivo aparece nos ignorados com o
  motivo. O mesmo para os que falharam no download (D4).
- **A comparação é ordinal**, como o `ExternalRef` no banco (D10 da #102: `AbC` e
  `abc` são referências diferentes).

- *Descartado, excluir também o que foi ignorado:* um bloqueio de download apagaria o
  documento da base, e o operador que liberasse o download receberia o documento de
  volta como novo, com histórico de exclusão e criação por uma configuração do
  Drive.
- *Descartado, excluir antes de baixar:* uma interrupção depois da exclusão deixaria
  a base com menos documentos e nenhuma das atualizações.

### D7. Contenção (`503` com `Retry-After`): sem repetir; fica para o próximo ciclo

O `503` do upsert ou da exclusão significa que outra escrita do mesmo documento
venceu duas vezes seguidas (D10 da #102). Com uma instância só (D10) e o lock por
base (D9), o operador não escreve em base `Synced` (`409` da D7 da #102) e nenhum
outro escritor existe: a contenção não é produzida por esta change.

Se acontecer, o arquivo **não** entra nos ignorados (não é propriedade dele), não é
repetido, e a base segue. O marcador no `apps/api` não muda, então o ciclo seguinte
baixa e envia de novo. O desfecho da base continua `Succeeded`. Fica um aviso no log
com a referência ausente do texto.

- *Descartado, repetir uma vez respeitando o `Retry-After`:* código e teste para um
  caso que, com uma instância, não acontece. Gatilho: a D10 mudar.
- *Descartado, gravar `Failed`:* a base inteira apareceria falhando por um documento
  que se resolve sozinho em 5 minutos.

### D8. `404` na base encerra só aquela base, sem gravar

Qualquer rota de `/sync` que responda `404` para a base (excluída entre a listagem
das bases e o passo em curso) encerra o ciclo daquela base: sem nova tentativa, sem
gravar resultado (não há onde) e sem excluir. A rodada segue para a próxima base. O
`409` de base `Manual` tem o mesmo tratamento: o tipo da base não muda depois de
criada (D11 da #102), então ele só aparece com um defeito, e a resposta segura é não
escrever.

Toda base roda dentro do próprio `try/catch` (convenção 4): uma exceção inesperada
numa base é logada e a rodada segue. **Todas** as chamadas da base ficam dentro dele,
inclusive a leitura das referências, que é a que mais realisticamente falha antes do
resto (o padrão de falha que a convenção 4 registra duas vezes).

### D9. "Sincronizar agora": `POST /connectors/knowledge-bases/{id}/sync`, só para `operator`

- **Rota:** `POST /connectors/knowledge-bases/{knowledgeBaseId:guid}/sync`, sob o
  prefixo da D9 da #103. Entra na tabela de subjects só para `operator`; `service:api`
  e qualquer outro recebem `403`.
- **Como confirma a base, sem guardar bases:** lê `GET /sync/knowledge-bases` no
  `apps/api` e procura o id. Ausente (inexistente ou `Manual`) → `404`
  `knowledge-base-not-found`. Presente → dispara o ciclo daquela base em segundo
  plano e responde `202` sem corpo. Funciona em base inativa, que está na lista.
- **Sem segundo ciclo simultâneo:** um registro em memória das bases em sincronização
  nesta instância (`SyncInProgress`, um `ConcurrentDictionary<Guid, byte>` com
  `TryAdd`/`TryRemove`), usado pelo "Sincronizar agora" **e** pela rodada periódica.
  Se a base já está sincronizando, a rota responde `202` sem disparar outro ciclo, e
  a rodada periódica pula a base. O ciclo em curso vai gravar o desfecho, que é o que
  a #107 espera.
- **Sem `Api:BaseUrl`:** `503` `sync-not-configured`, sem chamada de rede.
  `apps/api` sem resposta na consulta da base: `503` `sync-api-unavailable`. Resposta
  fora do contrato (inclusive `401`/`403`): `502` `sync-api-error` — nunca repassado
  com o mesmo status, para o painel não deslogar o operador (mesma razão da D2 da
  #104).
- **O ciclo em segundo plano** roda numa tarefa de vida própria, com o
  `CancellationToken` de parada da aplicação, e não com o da requisição.

Os códigos novos deste app são `knowledge-base-not-found`, `sync-not-configured`,
`sync-api-unavailable` e `sync-api-error`. O prefixo `sync-` diz que a falha é na
ligação com o `apps/api`; `api-not-configured` já existe e é a Drive API desligada no
projeto do Google, e não pode ser reaproveitado.

- *Descartado, `409` para base já sincronizando:* a tela mostraria erro para um pedido
  que vai ser atendido pelo ciclo em curso.
- *Descartado, confirmar a base por `GET /sync/knowledge-bases/{id}/documents`:*
  distingue `Manual` (`409`) de inexistente (`404`), mas traz todas as referências só
  para confirmar a existência; a #107 só oferece o botão em base `Synced`.
- *Descartado, guardar a lista de bases da última rodada:* uma base criada depois da
  rodada responderia `404` por até 5 minutos.
- *Descartado, a rota no `apps/api`:* o `apps/api` teria de chamar o `apps/connectors`
  para disparar, e a tabela de subjects do `apps/connectors` ganharia o `service:api`
  numa segunda rota, para o mesmo efeito.

### D10. Uma instância só, assumida e registrada

A primeira versão assume **um** processo do `apps/connectors`. Duas instâncias
rodariam cada uma a sua rodada e o seu lock, e as duas sincronizariam as mesmas bases:

- **dados:** seguro. O upsert é idempotente sob corrida e o `ContentRevision` é token
  de concorrência (D10 da #102); a exclusão de referência inexistente responde `204`;
- **custo:** o dobro da cota, e o "Sincronizar agora" deixa de impedir o segundo
  ciclo da mesma base;
- **contenção:** passa a acontecer, e a D7 deixa de ser hipotética.

**Gatilho para rever:** uma segunda réplica do `apps/connectors` em qualquer compose
(a #119 implanta uma). Quando acontecer, a D7, o lock da D9 e a memória da D14 são as
decisões a reabrir.

**Registrado para a implantação:** na #119, o `apps/connectors` entra na lista do
`docs/deployment.md` de serviços que nunca usam `replicas > 1`, ao lado do
`apps/workers` e do `apps/inbox`, até existir um lock distribuído
(https://github.com/aldovrando-oliveira/buteco-agentes/issues/119#issuecomment-5974415449).

- *Descartado, lock distribuído (no banco do `apps/api` ou num lease):* o
  `apps/connectors` não tem banco, e o `apps/api` ganharia estado de processo de
  outro app, que a D2 da #102 já descartou ("flag sincronizando agora... fica presa").

### D11. Testes: unitários sem contêiner no `apps/connectors`; ida e volta na fixture existente

**No `apps/connectors`, sem contêiner nenhum** (o app não tem banco):

- o ciclo contra o `FakeConnector`, com a listagem e o markdown configuráveis pelo
  teste (delta de `connector-plugin`), e contra um `apps/api` falso: um
  `HttpMessageHandler` que responde às cinco rotas de `/sync` e grava cada
  requisição, inclusive o cabeçalho `Authorization`;
- o agendamento com o `ManualTimeProvider` que já existe em `Support/`. **Corrigido na
  implementação, com a causa:** ele só sobrescrevia `GetUtcNow`, e um
  `PeriodicTimer(intervalo, relógio)` sobre ele cairia no timer real do sistema. Ganhou
  `CreateTimer`, com timers que só disparam em `Advance`, de forma síncrona, e a
  contagem de disparos. "Rodada longa não acumula" é provada sem espera real em duas
  partes: o `PeriodicTimer` sobre o relógio manual completa uma espera depois de três
  disparos e deixa a seguinte pendente (`IsCompleted`), e o agendamento com uma rodada
  falsa nunca tem duas rodadas ativas;
- a rota de "Sincronizar agora" pelo `ConnectorsFactory`;
- a checagem de `Api:BaseUrl` sobre a extensão e sobre a composição real;
- o token: o handler do `apps/api` falso valida o token com o `TokenService` do
  próprio app e afirma `sub` igual a `service:connectors`;
- os logs pelo `CapturingLoggerProvider` que já existe, na forma do `SecretLeakTests`.

**Ida e volta, no `tests/ApiConnectorsRoundTrip.Tests`:** o sentido
`apps/connectors` → `apps/api` com os dois apps reais e o Postgres da fixture que já
existe (convenção 11: o token é o que o `apps/connectors` assina, aceito pela tabela
real do `apps/api`; a recusa `too-large` é a resposta real da #120). A fixture passa a
ligar também o cliente do `apps/connectors` ao `TestServer` do `apps/api`, e remove o
serviço periódico da composição de teste, para o ciclo rodar só quando o teste pede.
**Corrigido na tarefa 1.2, com a causa:** a fixture não tem RabbitMQ, e o primeiro
upsert real respondeu `500` ao publicar a indexação (#138). O `apps/api` da fixture
passa a usar um publicador de indexação falso em memória, no molde do
`FakeKnowledgeIndexingJobPublisher` dos testes do `apps/api` (cópia, sem referência
entre projetos de teste); a ida e volta afirma também que cada documento criado ou
alterado gerou uma publicação.
**Os cenários entram num arquivo novo da classe `FolderValidationRoundTripTests`, que
passa a ser `partial`**: cada classe com `IClassFixture<RoundTripFixture>` subiria um
Postgres novo. **Nenhuma classe nova com contêiner.**

**Corrigido na implementação, com a causa:** "nenhuma chamada responde `401` nem `403`"
não prova o subject, porque o `apps/api` deixa `operator` passar em tudo (D6 da #102).
Medido no guarda 8.5: com o handler assinando `operator`, as chamadas do ciclo
responderam `200`. A ida e volta passou a validar o token que chegou ao `apps/api` com o
`ITokenService` do próprio `apps/api`, e a afirmar `sub` igual a `service:connectors`.

- *Descartado, uma classe nova `SyncCycleRoundTripTests` com a mesma fixture:* +1
  fonte de contêiner, que precisaria de autorização do mantenedor, só pelo nome da
  classe.
- *Descartado, provar o ciclo só com o `apps/api` falso:* o handler falso responderia
  o que o teste acha que o `apps/api` responde, e a forma real do `400` com `code`
  (#120) e a tabela real de subjects nunca seriam exercitadas (convenção 11).

### D12. Tamanho da change: uma change só

Contando unidades de revisão, não linhas (convenção 18):

| parte | o que tem | custo de revisão |
|---|---|---|
| configuração e boot | `ApiOptions`, checagem no boot, aviso | baixo, molde da #104 |
| cliente do `apps/api` | handler de token, cinco chamadas, leitura do `code` | médio |
| ciclo de uma base | passos, alcance de cada falha, conjunto de exclusão | **alto**: é onde está o risco (excluir documento) |
| agendamento | `BackgroundService`, `PeriodicTimer`, lock | médio |
| memória de recusas (D14) | entrada, invalidação por marcador, limpeza | médio |
| "Sincronizar agora" | rota, tabela de subjects, códigos | baixo |
| testes | unitários e ida e volta | alto, mas é a leitura que prova a parte de risco |
| documentação | seis documentos | médio, mecânico |

A divisão natural seria tirar o "Sincronizar agora". Sobrariam na primeira change o
ciclo, o agendamento e o lock — que existe por causa da rota —, e a segunda seria uma
rota de 30 linhas reabrindo a tabela de subjects. A parte cara continuaria inteira na
primeira. **Uma change só**, com as tarefas agrupadas por parte para a revisão.

- *Descartado, dividir em ciclo e "Sincronizar agora":* dois ciclos de archive e PR
  sem reduzir a revisão da parte que importa.

### D13. O que o ciclo envia, e o que nunca sai no log

- **Título** é o nome do arquivo no Drive, como veio. **Consequência medida na
  verificação manual (10.1):** renomear o arquivo no Drive é mudança de título para o
  `apps/api`, e registra `Updated` com `titleChanged: true`, `contentChanged: false`,
  sem reindexar e sem mudar a revisão. A primeira redação da spec dizia que renomear
  "não gera evento"; isso valia só para um título que não acompanhasse o nome. O caso
  sem evento é o de metadado que não é o nome, como alterar o compartilhamento. **`sourceType`** é sempre
  `markdown`: o conector entrega markdown para os dois tipos suportados. O nome do
  arquivo não é normalizado (a extensão `.md` fica): a tela mostra o que o operador vê
  no Drive.
- **Detalhe do `too-large`** nos ignorados é o `contentBytes` da resposta do
  `apps/api`, como texto decimal (o campo da D1 da #102 é texto). Os outros três
  códigos de conteúdo vão sem detalhe.
- **Logs:** id da base, contagens (criados, atualizados, inalterados, excluídos,
  ignorados, e quantos ignorados vieram da memória da D14), códigos e duração. **Nunca** o conteúdo de documento, o token, a chave
  nem o nome de arquivo. Teste dedicado.

### D14. Recusa determinística fica em memória, e não é baixada de novo com o mesmo marcador

**O problema:** o marcador gravado no `apps/api` só muda quando o upsert é aceito. Um
arquivo recusado com `too-large` fica com o marcador antigo (ou sem nenhum, se nunca
entrou), então o filtro da D6 o vê como "mudou" em todo ciclo: é exportado e enviado
de novo a cada 5 minutos, para sempre, e recusado de novo. A verificação contra o
Drive real da #103 mediu de 17 a 31 segundos para exportar um Doc grande (D2 da #103):
cada arquivo desses custaria até meio minuto de exportação e cota de download por
ciclo, sem resultado nenhum.

**A decisão:** quando a recusa é **determinística para aquele conteúdo**, o ciclo
guarda em memória do processo (`RefusalMemory`, singleton) a tripla **base,
referência externa e marcador**, com o código e o detalhe da recusa.

- **São determinísticas as quatro recusas de conteúdo da #120:** `too-large`,
  `unsupported-source-type`, `null-character` e `empty-content`. O mesmo conteúdo
  produz a mesma recusa, e o marcador do provedor muda quando o conteúdo muda (P6 da
  etapa 0: `md5Checksum` do `.md` muda com o conteúdo e só com ele; `modifiedTime` do
  Doc muda com o conteúdo e também com renomear e compartilhar).
- **No ciclo seguinte, marcador igual ao guardado:** o ciclo **não** baixa nem envia
  o arquivo, e repete a entrada nos ignorados com o mesmo código e detalhe. A lista
  gravada no `apps/api` continua dizendo a mesma coisa ao operador.
- **Marcador diferente invalida a entrada:** o arquivo é baixado e enviado de novo. É
  o caso do operador que reduziu o documento, e também o do Doc renomeado (o
  `modifiedTime` muda sem o conteúdo mudar): custa uma tentativa, e a recusa volta a
  ser lembrada com o marcador novo.
- **Falha transitória nunca entra na memória:** `provider-unavailable`,
  `provider-error`, `rate-limited`, `file-not-found` e a contenção `503`. Elas não dizem
  nada sobre o conteúdo, e lembrá-las impediria o arquivo de entrar quando o motivo
  passasse. O `download-blocked` vindo da exportação (`cannotExportFile`) também não
  entra: a listagem já põe nos ignorados, sem baixar, o arquivo com
  `canDownload=false` (D4), então o caso que sobra é raro e não custa um export longo.
- **Limpeza:** ao fim de cada base com listagem completa, saem da memória as
  entradas daquela base cuja referência não está mais na listagem (nem entre os
  suportados, nem entre os ignorados). Ao fim de cada rodada completa, saem as
  entradas de bases que não estão mais em `GET /sync/knowledge-bases`. O `404` numa
  base (D8) apaga as entradas dela na hora. Assim a memória nunca guarda mais do que
  os arquivos recusados que ainda estão em alguma pasta.
- **A memória é a mesma para a rodada periódica e para o "Sincronizar agora":** com o
  mesmo marcador, pedir a sincronização não força uma nova tentativa, porque o
  resultado seria o mesmo.

**Reiniciar o processo apaga a memória, e isso é aceitável:** depois de um restart,
cada arquivo recusado é tentado **uma** vez e volta a ser lembrado. O custo é uma
exportação por arquivo recusado por restart, e restart acontece em deploy, que é
manual e raro. Em troca, nenhum estado do `apps/connectors` sobrevive ao processo,
que é a premissa de um app sem banco.

**Gatilho para rever:** o teto do `apps/api` (`KnowledgeDocumentLimits.MaxContentBytes`)
ou a regra de uma das quatro recusas mudar. Até o `apps/connectors` reiniciar ou o
marcador mudar, o ciclo continuaria repetindo a recusa antiga. Como o teto é uma
constante e muda só por deploy do `apps/api`, a ação é reiniciar o `apps/connectors`
no mesmo deploy.

- *Descartado, gravar o marcador recusado no `apps/api`:* o `apps/api` só grava o
  marcador junto com o documento aceito (D3 da #102), e para um arquivo novo recusado
  não existe documento onde gravar. Exigiria mudar o modelo da #102 e o contrato de
  `/sync` (uma coluna ou uma tabela de recusas, e o upsert aceitar gravar sem
  conteúdo), para guardar um dado que só o conector usa.
- *Descartado, lembrar também as falhas transitórias por um tempo:* um TTL é uma
  segunda regra de "quando tentar de novo", ao lado do intervalo de 5 minutos, e o
  intervalo já é o backoff (D5).
- *Descartado, não lembrar nada e aceitar o custo:* meio minuto de exportação e cota
  de download por arquivo grande a cada 5 minutos, indefinidamente, com o mesmo
  resultado.

### Árvore de arquivos

```
apps/connectors/src/Buteco.Connectors/
├── Auth/
│   ├── ConnectorsSubjectAuthorizationHandler.cs     (rota nova para operator)
│   └── ServiceTokenDelegatingHandler.cs             (novo: assina service:connectors)
├── Options/
│   └── ApiOptions.cs                                (novo: Api:BaseUrl opcional)
├── Sync/                                            (novo)
│   ├── ApiConfigurationValidation.cs                (checagem no boot)
│   ├── SyncApiClient.cs                             (as cinco chamadas de /sync)
│   ├── SyncApiModels.cs                             (requisições e respostas)
│   ├── SyncCodes.cs                                 (os quatro códigos novos)
│   ├── KnowledgeBaseSyncCycle.cs                    (o ciclo de uma base)
│   ├── SyncRound.cs                                 (uma rodada sobre todas as bases)
│   ├── SyncInProgress.cs                            (o lock por base)
│   ├── RefusalMemory.cs                             (as recusas determinísticas, D14)
│   └── SyncSchedulerService.cs                      (BackgroundService, 5 min)
├── Endpoints/
│   └── SyncEndpoints.cs                             (novo: Sincronizar agora)
└── Program.cs

apps/connectors/tests/Buteco.Connectors.Tests/
├── Support/FakeConnector.cs                         (listagem e markdown configuráveis)
├── Support/FakeSyncApiHandler.cs                    (novo: apps/api falso)
├── KnowledgeBaseSyncCycleTests.cs                   (novo)
├── SyncRoundTests.cs                                (novo)
├── RefusalMemoryTests.cs                            (novo)
├── SyncSchedulerServiceTests.cs                     (novo)
├── SyncEndpointsTests.cs                            (novo)
├── ApiConfigurationValidationTests.cs               (novo)
├── ServiceTokenDelegatingHandlerTests.cs            (novo)
├── SubjectAuthorizationTests.cs                     (rota nova na matriz)
└── SecretLeakTests.cs                               (logs do ciclo)

tests/ApiConnectorsRoundTrip.Tests/
├── FolderValidationRoundTripTests.cs                (passa a ser partial)
├── FolderValidationRoundTripTests.SyncCycle.cs      (novo)
└── Support/RoundTripFixture.cs                      (cliente do apps/connectors → apps/api)
```

Nada em `libs/`. Nenhum pacote novo: `HttpClient`, `BackgroundService` e
`PeriodicTimer` são do framework.

## Risks / Trade-offs

- **[Excluir documento por uma leitura incompleta]**, o risco que importa → D6:
  exclusão só depois da pasta descrita, da listagem completa e dos upserts.
  Cenários: "Pasta sem acesso não exclui nada", "Listagem que falha na segunda
  página não exclui nada", "Base interrompida por cota não exclui nada".
- **[Excluir o documento de um arquivo que continua na pasta mas foi ignorado]** → D6.
  Cenário: "Arquivo ignorado que continua na pasta mantém o documento".
- **[Um arquivo com problema impedir a pasta inteira de sincronizar]** → D4.
  Cenários: "Recusa de conteúdo vira arquivo ignorado" e "Falha ao baixar um arquivo
  não derruba a base".
- **[O polling da #107 nunca terminar]** → D5: toda falha de base, inclusive
  `rate-limited`, grava; o `404` (base excluída) não grava, e é o único caso, porque
  não há base para a tela observar. Cenário: "Cota estourada grava Failed".
- **[Dois ciclos simultâneos da mesma base nesta instância]** → D9. Cenário: "Dois
  pedidos de sincronização simultâneos rodam um ciclo", com o conector falso segurando
  a listagem até o segundo pedido chegar, e a contagem de listagens igual a 1.
- **[Reexportar e reenviar para sempre um arquivo recusado]** → D14. Cenários:
  "Recusa determinística não é baixada de novo com o mesmo marcador" e "Marcador novo
  faz o arquivo recusado ser tentado de novo".
- **[A memória esconder um arquivo que passaria]** → D14: só as quatro recusas de
  conteúdo entram, e qualquer mudança de marcador invalida. Cenário:
  "Falha transitória não é lembrada".
- **[A memória crescer sem limite]** → D14, limpeza por referência e por base.
  Cenário: "Referência que sumiu da pasta sai da memória".
- **[Uma base falhando interromper as outras]** → D8. Cenário: "Base com 404 não
  interrompe as outras".
- **[Token com subject errado aceito no `apps/api`]** → D1. Cenário: "O token enviado
  ao apps/api é service:connectors", no unitário e na ida e volta contra a tabela
  real.
- **[Texto de documento, token ou chave no log]** → D13. Cenário: "Nenhum log contém
  conteúdo, token ou chave".
- **[`Api:BaseUrl` ausente derrubar a navegação]** → D2. Cenário: "Sem o endereço do
  apps/api, o app sobe e a navegação funciona".
- **[Guarda que passa com o defeito presente]** (convenção 15) → tarefa própria por
  guarda: excluir também os ignorados; excluir com listagem que falhou; não gravar no
  `rate-limited`; esquecer o lock; assinar `operator`.
- **[Várias instâncias]** → D10, assumido e registrado com gatilho. Não testável
  nesta change: não há como subir duas instâncias na suíte sem simular exatamente o
  que se quer provar.
- **[Comportamento real do Drive divergir do falso]** → tarefa de verificação manual
  contra o Drive real (convenção 14), com as regras de sigilo das changes anteriores.
- **[Rodada mais longa que o intervalo]** → D3: a duração vai para o log e o
  `PeriodicTimer` não acumula. Não testável por carga na suíte; o comportamento de não
  sobrepor é testado com o `ManualTimeProvider`.

## Migration Plan

Sem migração de dados e sem mudança no `apps/api`. Em desenvolvimento, o ciclo passa a
rodar quando `Api__BaseUrl` é configurada no `apps/connectors`. Em produção nada muda
até a #119, que vai precisar da variável nova (registrada lá). Reverter é reverter o
código: os documentos já sincronizados continuam no `apps/api`, e a base para de ser
atualizada.

## Open Questions

Nenhuma.
