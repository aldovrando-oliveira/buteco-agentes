## Context

A #108 (`exclusao-base-conhecimento`) abriu `DELETE /knowledge-bases/{id}` e deixou
a tela para esta change (D9 de lá). O contrato da rota, lido em
`KnowledgeBaseEndpoints.cs:162-200`:

| situação | resposta |
|---|---|
| base inativa | `204`, sem corpo; documentos, fragmentos, eventos e vínculos vão junto |
| base ativa | `409`, `code: "knowledge-base-active"` |
| id inexistente | `404` |
| dois impasses seguidos | `503` com `Retry-After: 1`, nada apagado |

Três textos foram escritos **proibidos de falar em excluir** porque o painel não
oferecia a ação, e todos registram que esta issue os revê:

| onde | texto | teste que proíbe `exclu`, `remov` e `apag` |
|---|---|---|
| `apps/api`, `FolderInUse` (`KnowledgeBaseEndpoints.cs:83-102`) | `detail` do `409` `folder-in-use` (#104, D5; #108, D8) | `KnowledgeBaseCatalogTests.SyncedCreation.cs:274-306` |
| `apps/frontend`, `connectorErrors.ts:128-133` | `folder-in-use` no cadastro (#106, D3) | `connectorErrors.test.ts:99-113`, `KnowledgeBaseCreatePage.sync.test.tsx:548-551` |
| `apps/frontend`, `syncStateMessages.ts:18-22` | falha da base, arquivo ignorado, "Sincronizar agora" (#107, D4 e C6) | `syncStateMessages.test.ts:27-30, 197-228`, `KnowledgeBaseSyncOriginCard.test.tsx:146-149` |

O padrão a seguir é a exclusão de documento (`KnowledgeBaseDetailPage.tsx:490-528`):
`Modal` com título `Excluir "<título>"?`, uma frase do que sai e de que não há como
desfazer, os agentes afetados por `agentsConsultingBase` sobre `GET /agents`,
`Cancelar` e o botão vermelho com o nome da ação. **O protótipo não desenha a
exclusão de base** (D9 da #108), então a régua visual é esse modal.

Hoje o cabeçalho do detalhe tem `Editar` e `Ativar`/`Desativar`
(`KnowledgeBaseDetailPage.tsx:323-343`). A página já busca `GET /agents` e a
listagem de documentos da base, que é a lista inteira, sem paginação (C4 da #107).

A #119 corre em paralelo em outro worktree e edita o `02`, o `CHANGELOG.md` e
`docs/`: as edições desta change nesses arquivos são curtas e localizadas.

## Goals / Non-Goals

**Goals:**

- O operador exclui pelo painel uma base inativa, sabendo antes o que vai junto.
- Com a base ativa, a tela diz por que não dá para excluir.
- Cada resposta da rota tem texto próprio, escolhido pelo status e pelo `code`.
- As mensagens de pasta em uso, no `apps/api` e no painel, levam o operador à
  ação que agora existe.

**Non-Goals:**

- Excluir pela listagem de bases: só pelo detalhe.
- Mudar a rota, o status, o `code` ou as extensões de qualquer resposta do
  `apps/api`. Só o `detail` de `folder-in-use` muda.
- Ler o `Retry-After` (D3).
- Link para a base dona da pasta no texto de `folder-in-use` (D5).
- Mudar os textos de estado da sincronização (D6).
- Compose, nginx e `deployment.md` (da #119); `apps/connectors` e `apps/workers`.

## Decisions

### D1. O botão "Excluir" no cabeçalho: indisponível com explicação na base ativa

**`Excluir` entra no cabeçalho, depois de `Ativar`/`Desativar`, nos dois estados.**

- **Base inativa:** botão habilitado, vermelho, que abre a confirmação (D2).
- **Base ativa:** o mesmo botão, **indisponível** (`aria-disabled="true"` e o
  estilo de desabilitado do Mantine), dentro de um `Tooltip` que abre ao passar o
  mouse **e ao receber foco**, com o texto: "Só uma base inativa pode ser excluída.
  Desative a base antes; a desativação pode ser desfeita, a exclusão não." O clique
  não abre a confirmação e não chama a rota.

O botão indisponível **não usa o atributo `disabled` nativo**: elemento
`disabled` não recebe foco, e o `Tooltip` ficaria inalcançável pelo teclado, que é
o único caminho da explicação. O Mantine documenta esse caso com `data-disabled` e
o clique anulado; a implementação confere no pacote instalado (`@mantine/core`
`^9.4.2`) que o `Button` aceita `data-disabled` e que o `Tooltip` abre no foco
(convenção 6), e registra no `tasks.md` o que encontrou. Se o pacote não suportar,
a saída é o `Button` dentro de um `span` com `tabIndex={0}`, e a decisão não muda.

**Nome acessível "Excluir a base", texto visível `Excluir`.** *Achado da
implementação (convenção 9):* a tabela de documentos de base manual tem um botão
"Excluir" por linha, e o botão do cabeçalho com o mesmo nome acessível ficava
indistinguível para leitor de tela e para a consulta por papel e nome. O
`aria-label` "Excluir a base" contém o texto visível (o nome falado começa pelo que
se lê), e não colide com o "Excluir base" da confirmação. O texto visível continua o
da spec.

A condição é **`data.isActive`**, a mesma que já escolhe entre `Ativar` e
`Desativar`. Nenhuma outra (vínculos, documentos, tipo) bloqueia o botão: a rota
aceita base inativa com vínculos e com documentos (D2 da #108).

- *Descartado, esconder o botão na base ativa:* o operador não saberia que
  desativar é a pré-condição; a issue recusa essa forma.
- *Descartado, `disabled` nativo com a explicação numa linha abaixo, como o
  "Sincronizar agora" sem `VITE_CONNECTORS_BASE_URL` (#107, D5):* lá a linha cabe
  no card; aqui o botão está no cabeçalho, e uma linha fixa sob o título em toda
  base ativa — o estado normal — seria ruído permanente para o caso raro.
- *Descartado, botão habilitado que abre um modal só com a explicação:* um clique
  que leva a um beco, e a confirmação e a explicação passariam a dividir o mesmo
  gatilho.
- *Descartado, uma seção "Zona de perigo" no fim da página:* fica abaixo da tabela
  de documentos, que não pagina, e cria uma composição nova sem prancha.

### D2. A confirmação, no padrão da exclusão de documento, sem digitar o nome

`Modal` com título `Excluir "<nome da base>"?`, e as frases nesta ordem:

1. **O que vai junto e que não volta.** Pela listagem de documentos da página:
   - respondida com N > 0: "A base e os seus N documentos são excluídos, com os
     fragmentos indexados e o histórico. Não há como desfazer." (singular para 1);
   - respondida vazia: "A base é excluída, com o histórico. Ela não tem
     documentos. Não há como desfazer.";
   - carregando ou com falha: "A base e todos os documentos dela são excluídos,
     com os fragmentos indexados e o histórico. Não há como desfazer." — **sem
     número**.

   Proveniência do zero (convenção 13): o N é a contagem da listagem
   `GET /knowledge-bases/{id}/documents`, que devolve a lista inteira; zero ali é
   **medido**. Sem a listagem, a contagem é **desconhecida**, e a frase não afirma
   número nenhum.
2. **Os agentes.** Por `agentsConsultingBase(GET /agents, id)`:
   - com agentes: "Os vínculos com A e B saem junto. Esses agentes já não
     consultam esta base, porque ela está inativa." Afirmação sobre outro app, com
     origem e gatilho no comentário: a resolução de tools filtra por `IsActive`
     (`KnowledgeToolSetResolver.cs:87`, `apps/workers`); gatilho: a resolução
     deixar de filtrar. *Corrigido na implementação (convenção 9): a versão
     aprovada atribuía o arquivo ao `apps/api`, nas linhas 84-87 citadas pela D2
     da #108; ele está em `apps/workers/src/Buteco.Workers/Knowledge/Execution/`,
     e o filtro na linha 87;*
   - sem agentes: "Nenhum agente está vinculado a esta base.";
   - `GET /agents` carregando ou com falha: "Não foi possível conferir os agentes
     vinculados; os vínculos que existirem saem junto."

   **Diferente do modal de documento**, que omite a linha quando `GET /agents`
   falha: lá a ausência da linha é inofensiva (o documento não tem vínculo); aqui
   omitir leria como "nenhum agente", que é o terceiro estado da convenção 13
   (registro que não veio) colapsado no segundo.
3. **Só em base sincronizada:** "A pasta “<folderName>” fica livre para ser usada
   por outra base. Os arquivos no <provedor> não são tocados." O provedor pelo
   `providerLabel` da #106 (`connectorErrors.ts:38`), que cai na chave para
   provedor desconhecido.

Botões: `Cancelar` e `Excluir base` (vermelho, `loading` durante a requisição).
`Cancelar`, o `X` e o `Esc` fecham sem requisição; durante a requisição, o modal
não fecha.

**Sem digitar o nome para confirmar.** A barreira contra o engano já existe e é
melhor: a exclusão exige **desativar antes**, um passo próprio, com confirmação, e
reversível (D2 da #108). Digitar o nome seria um terceiro passo, e **o nome não
identifica a base**: o cadastro permite nomes repetidos (requisito "Nome
duplicado é permitido", `knowledge-base-catalog`), então digitar "Atendimento"
confirmaria qualquer das bases com esse nome.

- *Descartado, exigir digitar o nome:* acima.
- *Descartado, caixa "Entendo que não há como desfazer":* a mesma fricção sem
  informação nova; a frase 1 já diz.
- *Descartado, buscar a contagem no resumo de indexação
  (`GET /knowledge-bases/indexing-summary`):* uma segunda fonte para o mesmo número,
  que a página já tem pela listagem; e o resumo não é buscado no detalhe hoje.

### D3. As respostas da rota, cada uma com o seu texto

A mutação chama `DELETE /knowledge-bases/{id}` pelo `request` da feature
(`knowledgeBasesApi.ts`). O texto é escolhido pelo **status e pelo `code`**, nunca
pelo `title` nem pelo `detail` do `ProblemDetails`. Erro aparece **dentro da
confirmação**, num `Alert` vermelho acima dos botões, e não em notificação: a
notificação some sozinha, e o operador precisa ler o motivo antes de decidir o
próximo passo.

| resposta | texto | o que a tela faz |
|---|---|---|
| `204` | notificação verde "Base excluída": `"<nome>" foi excluída.` | navega para `/knowledge-bases`; depois remove do cache a base e os documentos dela, e invalida a listagem de bases, o resumo de indexação e `['agents']` (os vínculos mudaram) |
| `409` `knowledge-base-active` | "A base foi ativada depois que esta tela abriu, e só uma base inativa pode ser excluída. Nada foi excluído." | não navega; esconde `Excluir base` (fica `Fechar`); relê a base, e o cabeçalho volta a mostrar "Ativa" e o botão indisponível (D1) |
| `404` | "Esta base já tinha sido excluída, provavelmente em outra aba. Nada mais foi excluído." | não navega; esconde `Excluir base` e oferece `Voltar para as bases`; invalida só a listagem de bases (não a base, D3.1) |
| `503` | "Outra operação nesta base impediu a exclusão. Nada foi excluído; tente de novo em instantes." | não navega; `Excluir base` continua disponível para repetir |
| `409` com outro `code`, outro status | "A exclusão não foi concluída (status N). Recarregue a página para conferir o estado da base." | não navega; esconde `Excluir base` |
| falha de rede | "Não foi possível confirmar a exclusão: o servidor não respondeu. Recarregue a página para conferir se a base ainda existe." | não navega; esconde `Excluir base` |

**D3.1, a ordem no `204` e no `404`.** A página observa `['knowledge-bases', id]`.
Invalidar ou remover essa chave **antes** de sair da página faz a consulta repetir,
receber `404` e trocar o detalhe pelo "Base de conhecimento não encontrada" por um
instante (no `204`), ou desmontar a confirmação com o texto (no `404`). Por isso: no
`204`, navega e só tira a base do cache quando o detalhe desmonta; no `404`, invalida
só a listagem (`['knowledge-bases']` com `exact: true`).

> **Corrigido na implementação (convenção 9).** A versão aprovada dizia "navega
> primeiro e mexe no cache depois", na mesma função. Medido pelo teste do `204`
> (`getKnowledgeBase` chamado **2 vezes**, não 1): no data router o `navigate` é
> assíncrono, e o `removeQueries` logo depois dele — e também depois de esperar a
> promessa do `navigate` — ainda encontra o detalhe montado, que repete a consulta da
> base excluída. A remoção da base e dos documentos passou para o *cleanup* de um
> efeito da página, marcado por um `ref` com o id excluído: é o único momento em que
> nenhum observador sobra. A listagem, o resumo e `['agents']` continuam invalidados
> no `onSuccess`, porque o detalhe não os observa.

**O `Retry-After` não é lido.** A política de CORS do `apps/api` não expõe esse
cabeçalho (`Program.cs:135-136`: `WithOrigins(...).AllowAnyHeader().AllowAnyMethod()`,
sem `WithExposedHeaders`), e ele não está entre os cabeçalhos de resposta liberados
por padrão, então o `fetch` do painel, em outra origem, não o enxerga. O valor é
fixo em `1` (`KnowledgeBaseEndpoints.cs:196`), e "em instantes" diz o mesmo.

**Divergência com a issue, registrada:** a #136 diz "`404` (base já excluída) trata
como excluída". A tarefa desta change pede que o `404` mostre o seu texto e **não
navegue**. As duas cabem: o texto diz que a base já tinha sido excluída (trata como
excluída), e a volta é um botão, não uma navegação automática — o operador fica
sabendo que não foi ele quem excluiu, o que a navegação com "Base excluída"
esconderia.

- *Descartado, ler o `Retry-After`:* exigiria mudar o CORS do `apps/api` para um
  valor fixo de 1 segundo.
- *Descartado, tratar o `404` como `204` (notificação "Base excluída" e
  navegação):* afirmaria que esta ação excluiu a base.
- *Descartado, erros em notificação, como na exclusão de documento:* acima.
- *Descartado, no `409`, deixar o botão disponível para tentar de novo:* a base
  está ativa, e a nova tentativa recebe o mesmo `409`.

### D4. O `detail` do `409` `folder-in-use` no `apps/api` oferece as duas saídas

Novo `detail` (o `title`, o `code` e as extensões não mudam):

> A pasta já é usada pela base "<nome>". Uma pasta pertence a uma base só, e
> continua ocupada mesmo com a base inativa. Escolha outra pasta, ou desative e
> exclua a base "<nome>" para liberar esta; a exclusão apaga os documentos dela e
> não pode ser desfeita.

**As duas saídas, na ordem do painel (D5).** Escolher outra pasta vem primeiro: é o
caminho mais comum e o que não destrói nada. Quem chama a API direto, sem o painel,
veria só a opção de apagar se o `detail` oferecesse apenas a exclusão (ajuste da
revisão do mantenedor, 05/10/2026).

**Muda de propósito** o requisito "Pasta em uso recusada no cadastro com a base que
a usa" (`knowledge-base-catalog`, #104): a frase "A resposta NÃO SHALL mencionar
excluir, remover ou apagar a base, porque não há rota de exclusão de base" sai, e o
cenário "A mensagem não manda excluir a base" vira "A mensagem orienta desativar e
excluir a base", que afirma também a alternativa de escolher outra pasta. O motivo da proibição deixou de existir: a rota existe desde a
#108, e o botão chega com esta change.

O teste `CreateSynced_FolderAlreadyUsed_Returns409WithTheBaseThatUsesIt`
(`KnowledgeBaseCatalogTests.SyncedCreation.cs:283-306`) troca `AssertNoDeletionAdvice`
por asserções de que o `detail` contém "outra pasta", "desative", "exclua" e "não
pode ser desfeita", além do nome da base e de "inativa", que já afirmava. `DeletionWords` e
`AssertNoDeletionAdvice` saem. O comentário de `FolderInUse` passa a apontar esta
change.

A frase avisa que a exclusão não tem volta porque o `detail` também é lido fora do
painel (por quem chama a API direto), onde não há confirmação.

- *Descartado, manter o `detail` neutro e orientar só no painel:* o `detail` é a
  única explicação para quem chama a API, e a D8 da #108 já fixou que ele muda com
  o botão.
- *Descartado, uma extensão nova (por exemplo `deletable`):* contrato novo sem
  consumidor; o painel escolhe o texto pelo `code`.

### D5. O texto de `folder-in-use` no cadastro do painel (#106), a mesma orientação

Novo texto em `connectorErrors.ts`, contexto `create` (o "Nenhuma base foi criada."
continua sendo acrescentado depois, como hoje):

> Esta pasta já é usada pela base “<nome>”. Uma pasta alimenta uma base só, e
> continua ocupada mesmo com a base inativa. Escolha outra pasta, ou desative e
> exclua a base “<nome>” para liberar esta; a exclusão apaga os documentos dela e
> não pode ser desfeita.

Sem `knowledgeBaseName` na resposta (hoje cai em “sem nome”), a primeira frase diz
"Esta pasta já é usada por outra base" e a orientação diz "desative e exclua a base
que usa esta pasta", em vez de "a base “sem nome”" — uma base sem nome não existe,
porque o nome é obrigatório.

> **Corrigido na implementação (convenção 9).** A versão aprovada dizia "a base que
> usa esta pasta" **nas duas posições**; na primeira, a frase ficaria "Esta pasta já
> é usada pela base que usa esta pasta", que é tautologia. A primeira posição passou
> a "por outra base". O requisito não muda: ele exige "a base que usa esta pasta" e
> proíbe "sem nome", e as duas coisas valem.

Muda de propósito o requisito "Texto em português para cada código de erro"
(`knowledge-base-sync-source-ui`, #106) e o cenário "Pasta em uso no cadastro não
manda excluir". Os testes `connectorErrors.test.ts:99-113` e o ramo
`folder-in-use` de `KnowledgeBaseCreatePage.sync.test.tsx:548-551` trocam a
asserção negativa pela afirmação da orientação ("desative", "exclua", "não pode ser
desfeita") e do nome. O comentário de `connectorErrors.ts:128-131` é reescrito.

- *Descartado, um link para o detalhe da base dona da pasta:* `connectorErrorMessage`
  devolve texto, e o `Alert` do card de Origem o exibe como string; o link exigiria
  mudar o tipo de retorno e o card, e ler `knowledgeBaseId`, que o tipo
  `ValidationProblemDetails` nem declara. Se fizer falta, vira issue.

### D6. A proibição de `exclu` nos textos de estado da sincronização continua, com outro motivo

Os textos de falha da base, de arquivo ignorado e do "Sincronizar agora"
(`syncStateMessages.ts` e o contexto `sync-request` de `connectorErrors.ts`)
**continuam sem `exclu`, `remov` nem `apag`**, e a varredura de
`syncStateMessages.test.ts:197-228` fica como está. **O motivo muda**, e é o motivo
novo que fica escrito no comentário, no teste e no requisito:

- antes: "o painel não oferece a exclusão de base";
- agora: **uma falha de sincronização nunca se resolve excluindo a base.** O remédio
  de cada código é na pasta (compartilhar, restaurar), na instalação, ou é esperar
  a próxima rodada; e o alerta aparece também em base **ativa**, onde a exclusão
  nem está disponível. Um texto de falha que sugerisse excluir ofereceria uma ação
  irreversível como saída para um problema passageiro ou de configuração.

O caso da pasta que deixou de existir já tem o caminho certo no texto de hoje
("crie uma nova base com outra pasta"): a pasta nova tem outro id, e a base antiga
não a prende. Liberar a pasta **antiga** só importa quando ela volta a existir com
o mesmo id, e esse caminho é o do `folder-in-use` (D4, D5), que é onde o operador
o encontra.

A garantia "Nenhum documento saiu da base" **fica como está**: é verdadeira, e
trocar por "nenhum documento foi excluído" não diz nada a mais.

O que muda no código são só os comentários de `syncStateMessages.ts:18-22` e
`syncStateMessages.test.ts:27-29`, que dizem que a #136 revê a regra. No requisito
"Alerta de falha da sincronização com o motivo pelo código"
(`knowledge-base-sync-state-ui`), a frase da proibição ganha o motivo. A frase do
`Purpose` daquela spec ("nenhum texto manda excluir a base, que é escopo da exclusão
de base no painel") não é alcançada por delta: é corrigida à mão junto com a
documentação, antes do archive (tarefa 8.3).

- *Descartado, levantar a proibição:* nenhum texto passaria a usar a palavra, e a
  varredura deixaria de impedir a regressão bem-intencionada de "sugerir excluir e
  recriar" num alerta de base ativa.
- *Descartado, levantar só para frases que descrevem o que **não** aconteceu
  ("nenhum documento foi excluído"):* a varredura por substring não separa os dois
  usos, e a frase de hoje já diz o mesmo.

### D7. Conferência visual, no molde da #107, contra o modal de exclusão de documento

- **Painel:** `npm run dev` deste worktree numa porta livre da faixa
  **59100–59199** (conferida com `lsof -i :<porta>`), com `VITE_API_BASE_URL`
  apontando para outra porta da mesma faixa **sem nenhum processo escutando**:
  toda resposta do `apps/api` é montada pelo CDP (`Fetch.enable`,
  `Fetch.fulfillRequest` com cabeçalhos CORS e o preflight `OPTIONS`;
  `Fetch.failRequest` para a falha de rede). Sem backend real. Token gravado no
  `sessionStorage` pelo CDP. Portas registradas no `tasks.md`.
- **Chrome headless por CDP**, claro e escuro, a 1440px e a ~1860px.
- **Estados:** base ativa com o `Tooltip` aberto pelo foco; confirmação de base
  manual com dois agentes e documentos; de base sincronizada; sem agentes e sem
  documentos; com agentes e documentos desconhecidos (as duas consultas falhando);
  `409`, `404`, `503` e falha de rede na confirmação; listagem depois do `204`, com
  a notificação; cadastro sincronizado com o `409` `folder-in-use`; e, como régua,
  **a confirmação de exclusão de documento** capturada na mesma rodada.
- **Medido contra a régua:** largura do modal, posição e ordem dos botões, cor do
  botão de confirmação, espaçamento entre as frases; e o contraste de cada texto
  novo sobre o fundo composto (o `Alert` tem transparência), nos dois esquemas.
  Iterar até uma rodada sem achado; capturas em `design/capturas/`, nomeadas
  `<estado>_<esquema>_<largura>.png`, medidas em `design/capturas/medidas-r<N>.json`.
- **A validação manual pelo mantenedor fica pendente** (convenção 14), e o archive
  espera por ela.

**Correções de protótipo** (convenção 17), para a lista viva do `02`:

| # | prancha | o protótipo | o que prevalece | por quê |
|---|---|---|---|---|
| C1 | 3a, 4a | não desenha exclusão de base | botão no cabeçalho (D1) e confirmação no padrão da exclusão de documento (D2) | a rota existe desde a #108; o desenho reusa o padrão que o painel já tem |

- *Descartado, subir `apps/api` local:* exigiria Postgres e dados montados para
  cada estado; as respostas de erro (`409` por corrida, `503` por impasse) não se
  provocam pela tela.

**Resultado (06/10/2026): duas rodadas, a segunda sem achado.** 12 estados × 2
esquemas × 2 larguras = 48 capturas por rodada, em `design/capturas/`. Nenhuma
requisição ficou sem resposta montada. O `Tooltip` foi aberto por Tab de verdade
pelo CDP: o `.focus()` programático não o abre, porque o foco que conta é o
visível (o mesmo que o teclado produz).

**Contra a régua (confirmação de exclusão de documento), medido igual nas 4
combinações de esquema e largura:** modal de 440 px, centralizado (x = 500 a 1440
e 710 a 1860), título de 13 px, frases de 12 px a 46 px uma da outra, botões de
36 px de altura à direita, `Cancelar` antes do botão vermelho, o mesmo vermelho
(`rgb(207, 58, 43)` no claro, `rgb(240, 118, 106)` no escuro). Contraste das frases:
21:1 no claro e 14,02:1 no escuro; do `Alert` de erro, 18,37:1 e 13,73:1; do
`Tooltip`, 17,41:1 e 18,59:1; do alerta de `folder-in-use` no cadastro, 5,75:1 e
12,8:1.

| # | o quê | medido | correção |
|---|---|---|---|
| R1-1 | `Excluir` do cabeçalho em vermelho cheio | o elemento mais pesado do cabeçalho, acima de `Ativar`; texto branco sobre `rgb(240, 118, 106)` no escuro: **2,8:1** | `variant="outline"`, como o `Desativar`: o vermelho cheio fica só na confirmação. Medido depois: 4,49:1 no claro e 17,32:1 no escuro |

Medido e não corrigido, por ser o padrão da casa e não desta change:

- o **botão vermelho cheio** das confirmações destrutivas mede **2,8:1** no escuro,
  inclusive a régua ("Excluir documento"). Aberto como **#155** (convenção 23);
  corrigir só a confirmação nova deixaria dois vermelhos no painel;
- o **vermelho em `outline`** mede **4,49:1** no claro, a mesma cor sobre o mesmo
  fundo do `Desativar` que já existia. Acrescentado à #155;
- o `Excluir` indisponível mede 2,85:1 (claro) e 3,92:1 (escuro): é o estilo de
  controle desabilitado do Mantine, isento do mínimo de contraste, e o mesmo de
  todo botão desabilitado do painel.

## Testes de aceite

Todos em `apps/frontend` (Vitest + Testing Library, com `vi.mock` dos módulos de
API, como os testes vizinhos), salvo o último. "Nenhuma requisição" é afirmada
como `deleteKnowledgeBase` não chamada.

| teste | o que afirma, com a asserção negativa |
|---|---|
| base ativa | `Excluir` está no documento com `aria-disabled="true"`; o foco mostra a explicação; o clique **não** abre a confirmação e **nenhuma** requisição `DELETE` sai |
| base inativa | a confirmação nomeia a base, os dois agentes e "3 documentos" |
| sem documentos / sem agentes | "não tem documentos" e "Nenhum agente"; **nenhum** número de documentos |
| consultas falhando | sem número de documentos, e "Não foi possível conferir os agentes"; **não** contém "Nenhum agente" |
| base sincronizada | a pasta pelo nome e "não são tocados"; a frase **não** aparece em base manual |
| cancelar | `Cancelar` fecha e **nenhuma** requisição `DELETE` sai |
| `204` | a rota passa a ser `/knowledge-bases`, a notificação aparece, e a base **não** está na listagem que a tela pede de novo |
| `409` `knowledge-base-active`, `404`, `503`, rede | cada um mostra o seu texto, a rota **continua** a do detalhe, e nenhum `title`/`detail` do servidor aparece; no `503`, `Excluir base` segue disponível; no `409`, a base é relida |
| guarda (convenção 15) | trocar `data.isActive` pela constante `false` na condição do botão faz o teste da base ativa reprovar; resultado registrado no `tasks.md` |
| `folder-in-use` no painel | nomeia a base e contém "desative", "exclua" e "não pode ser desfeita"; sem nome, **não** contém "sem nome" |
| textos de estado | a varredura de `exclu` continua passando sobre todos os textos |
| `apps/api`: `409` `folder-in-use` | o `detail` nomeia a base e contém "inativa", "outra pasta", "desative", "exclua" e "não pode ser desfeita"; guarda: tirar a orientação do `detail` reprova o teste |

## Árvore de pastas proposta

```
apps/frontend/src/features/knowledge-bases/
  api/
    knowledgeBasesApi.ts                  (deleteKnowledgeBase; comentário "Não há DELETE" reescrito)
    knowledgeBasesApi.test.ts             (DELETE e o 204 sem corpo)
    useKnowledgeBases.ts                  (useDeleteKnowledgeBaseMutation, sem mexer no cache — D3.1)
    useKnowledgeBases.test.ts             (a mutação chama a rota e propaga o erro)
  components/
    KnowledgeBaseDeleteModal.tsx          (novo — D2, D3: frases e Alert; recebe dados e callbacks por prop, convenção 7)
    KnowledgeBaseDeleteModal.test.tsx     (novo — as frases por estado de dado)
  pages/
    KnowledgeBaseDetailPage.tsx           (botão e Tooltip — D1; mutação, navegação e cache — D3)
    KnowledgeBaseDetailPage.delete.test.tsx  (novo — testes de aceite da página)
    KnowledgeBaseCreatePage.sync.test.tsx (ramo folder-in-use — D5)
  utils/
    deleteKnowledgeBaseMessages.ts        (novo — texto por status e code, D3)
    deleteKnowledgeBaseMessages.test.ts   (novo)
    connectorErrors.ts                    (texto de folder-in-use — D5)
    connectorErrors.test.ts               (D5)
    syncStateMessages.ts                  (só o comentário — D6)
    syncStateMessages.test.ts             (só o comentário — D6)

apps/api/src/Buteco.Api/KnowledgeBases/Endpoints/KnowledgeBaseEndpoints.cs   (detail e comentário de FolderInUse — D4)
apps/api/tests/Buteco.Api.Tests/Knowledge/KnowledgeBaseCatalogTests.SyncedCreation.cs  (D4)

openspec/changes/exclusao-base-painel/design/capturas/   (D7)

01-ARQUITETURA_E_CONVENCOES.md   (as duas linhas que dizem que a mensagem espera a #136)
docs/architecture.md             (uma linha em "Pasta em uso")
02-HISTORICO_E_STATUS.md         (entrada da change e C1 em "Correções de protótipo")
CHANGELOG.md                     (Added / Changed)
```

Nada em `libs/`. Nada em `apps/workers`, `apps/connectors` nem `apps/inbox`.

O modal vira componente próprio, e não mais um bloco na página como o de documento,
porque tem três estados de dado por frase e quatro de resposta: testá-lo pela página
exigiria montar o detalhe inteiro para cada combinação. O texto por resposta fica em
`utils/`, como `connectorErrors.ts` e `syncStateMessages.ts`.

## Risks / Trade-offs

- **[Exclusão irreversível por engano]** → a rota só aceita base inativa, e a
  desativação é um passo com confirmação e reversível; a confirmação nomeia o que
  vai junto. Verificável: o teste da base ativa, com a guarda da convenção 15.
- **[A contagem da confirmação envelhece]** → numa base sincronizada, uma rodada
  pode incluir documentos entre a leitura e o clique, e o número exibido fica menor
  que o apagado. Aceito: a frase diz "a base e os seus N documentos", e a rota apaga
  todos; o número é o que o painel leu. Não há como fixar o número sem mudar a rota.
- **[Tela que mente depois do `204`, por cache]** → D3.1: navegar antes de mexer no
  cache. Verificável: o teste do `204` afirma a rota da listagem e a ausência da base
  na listagem pedida de novo.
- **[A orientação de excluir aparece onde a ação não está disponível]** → D6 mantém
  os textos de sincronização sem ela; D4 e D5 só aparecem no cadastro, onde a base
  dona da pasta é outra, e dizem "desative e exclua" — os dois passos que a tela
  oferece. Verificável: a varredura de `exclu` continua sobre os textos de estado.
- **[`data-disabled` e `Tooltip` no foco não se comportam como documentado na versão
  instalada]** → conferido no pacote antes de implementar (D1), com a saída do
  `span` focável. Verificável: o teste do foco mostrando a explicação.
- **[A #119 edita os mesmos documentos]** → edições curtas e localizadas no `02`, no
  `CHANGELOG.md` e em `docs/`; nada do compose, do nginx nem do `deployment.md`.

## Migration Plan

Sem migração. O deploy é do `apps/api` (só um texto) e do `apps/frontend`. A ordem
entre os dois não importa: o painel escolhe o texto de `folder-in-use` pelo `code`,
e o `detail` novo só é lido por quem chama a API direto. Rollback: reverter o código.

## Open Questions

Nenhuma de negócio. As decisões de produto (D1, D2, D6) passam pela revisão do
mantenedor antes do `/opsx:apply`.
