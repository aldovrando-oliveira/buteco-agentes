## Purpose

A escolha da origem sincronizada no cadastro de base de conhecimento do painel:
provedor, conta de serviço, seletor de pasta, pasta em uso e o texto de cada código
de erro, sobre as rotas do operador no `apps/connectors` (`connectors-api`) e o
cadastro `Synced` do `apps/api` (`knowledge-sync-folder-validation`).

A capability existe porque o operador precisa ver o que a conta de serviço de fato
enxerga, em vez de colar um id de pasta, e porque o painel sai para produção antes
do `apps/connectors`: sem `VITE_CONNECTORS_BASE_URL` no build, a origem
sincronizada fica indisponível e nada é chamado. Duas regras a protegem: a tela
nunca afirma mais do que sabe (carregando não é erro, lista vazia não é travessão,
listagem de bases indisponível não marca pasta como livre), e o texto vem do
`code`, nunca do `title` do servidor.

## ADDED Requirements

### Requirement: Endereço do apps/connectors opcional no build do painel
O painel SHALL ler o endereço do `apps/connectors` de `VITE_CONNECTORS_BASE_URL`,
resolvida em tempo de build. Ausente (`undefined`), a opção Sincronizada do card
de Origem SHALL aparecer desabilitada, com uma explicação de que a sincronização
com pastas não está habilitada neste painel, e o painel SHALL NOT fazer nenhuma
requisição ao `apps/connectors`. A opção Manual SHALL continuar disponível e
marcada. String vazia SHALL significar caminho relativo ao próprio domínio, como
nos outros dois endereços de build. O build do frontend SHALL completar sem a
variável, inclusive o build da imagem pelo `apps/frontend/Dockerfile`.

#### Scenario: Sem a variável, só Manual
- **WHEN** o painel é construído sem `VITE_CONNECTORS_BASE_URL` e o operador abre
  `/knowledge-bases/new`
- **THEN** a opção Sincronizada está desabilitada e acompanhada da explicação, a
  opção Manual está marcada, e nenhuma requisição é feita ao `apps/connectors`
  durante o carregamento da tela e o cadastro manual

#### Scenario: Imagem construída sem a variável
- **WHEN** `docker build -f apps/frontend/Dockerfile` roda com
  `VITE_API_BASE_URL` e `VITE_INBOX_BASE_URL` e sem `VITE_CONNECTORS_BASE_URL`
- **THEN** o build completa, o bundle não contém o endereço de desenvolvimento
  do `apps/connectors`, e nessa imagem a opção Sincronizada de
  `/knowledge-bases/new` está desabilitada, com a explicação

#### Scenario: Com a variável vazia, caminho relativo
- **WHEN** o painel é construído com `VITE_CONNECTORS_BASE_URL=""`
- **THEN** a opção Sincronizada está habilitada e as requisições ao
  `apps/connectors` saem para `/connectors/...` no próprio domínio

### Requirement: Provedores e conta de serviço na origem sincronizada
Com a opção Sincronizada marcada, o painel SHALL consultar
`GET /connectors/providers` e SHALL NOT consultá-lo antes de a opção ser marcada.
O seletor de provedor SHALL listar só os provedores devolvidos, pelo nome de
exibição do provedor conhecido (`google-drive` como "Google Drive") e pela própria
chave quando o provedor não for conhecido do painel. Com um provedor só, ele SHALL
vir escolhido. O painel SHALL exibir o `accountEmail` do provedor escolhido, com
botão de copiar e a instrução de compartilhar a pasta com essa conta como Leitor.

Durante a consulta, o painel SHALL indicar carregamento e SHALL NOT exibir texto de
erro. Falha da consulta SHALL ser exibida como erro explícito, com o texto do
código (requisito "Texto em português para cada código de erro"), e a opção de
tentar de novo. Lista vazia SHALL ser exibida como fato conhecido, com a explicação
de que nenhum provedor está configurado no `apps/connectors` desta instalação, e
não como erro nem como travessão.

#### Scenario: Provedor configurado com e-mail
- **WHEN** o operador marca Sincronizada e o `apps/connectors` devolve
  `[{"key":"google-drive","accountEmail":"conta@projeto.iam.gserviceaccount.com"}]`
- **THEN** o provedor "Google Drive" aparece escolhido, o e-mail aparece com o botão
  de copiar, e a instrução diz para compartilhar a pasta como Leitor

#### Scenario: Provedores consultados só depois de marcar Sincronizada
- **WHEN** o painel tem `VITE_CONNECTORS_BASE_URL` e o operador abre o formulário
  sem marcar Sincronizada
- **THEN** nenhuma requisição é feita ao `apps/connectors`

#### Scenario: apps/connectors fora do ar ao listar provedores
- **WHEN** a requisição de provedores falha por rede
- **THEN** o painel exibe o erro de serviço de conectores fora do ar, com a opção de
  tentar de novo, e não exibe seletor de provedor

#### Scenario: Nenhum provedor configurado
- **WHEN** o `apps/connectors` devolve `[]`
- **THEN** o painel explica que nenhum provedor está configurado nesta instalação,
  não exibe texto de erro e não permite abrir o seletor de pasta

#### Scenario: Carregamento de provedores sem texto de erro
- **WHEN** a requisição de provedores ainda não respondeu
- **THEN** o card indica carregamento e não contém nenhum texto de erro

### Requirement: Pasta escolhida no formulário
Sem pasta escolhida, o formulário SHALL dizer que nenhuma pasta foi escolhida e
oferecer "Escolher pasta". Com pasta escolhida, SHALL mostrar o nome dela, o link
"Abrir no Drive" para o `webUrl` devolvido pela navegação, aberto em outra aba, e
"Trocar pasta". O formulário SHALL dizer que entram só os arquivos da raiz da
pasta, Google Docs e arquivos `.md`. Trocar de provedor SHALL limpar a pasta
escolhida. Criar base sincronizada sem pasta escolhida SHALL ser barrado no
cliente, com erro no campo da pasta, sem requisição de criação.

#### Scenario: Pasta escolhida com link
- **WHEN** o operador escolhe a pasta "FAQ Suporte" no seletor
- **THEN** o formulário mostra "FAQ Suporte", o link "Abrir no Drive" apontando para
  o `webUrl` da pasta com `target="_blank"`, e o botão "Trocar pasta"

#### Scenario: Sincronizada sem pasta é barrada
- **WHEN** o operador marca Sincronizada, preenche nome e descrição e tenta criar
  sem escolher pasta
- **THEN** o campo da pasta exibe erro e nenhuma requisição de criação é enviada

### Requirement: Navegação de pastas no seletor
O seletor SHALL abrir em modal, no nível de cima, consultando
`GET /connectors/providers/{providerKey}/folders` sem `parentId`, e SHALL agrupar
os itens em "Drives compartilhados" (`kind: "SharedDrive"`) e "Pastas
compartilhadas com a conta" (`kind: "Folder"`), cada grupo na ordem devolvida.
Entrar num item SHALL consultar a mesma rota com `parentId` igual ao id do item e
mostrar as subpastas. O seletor SHALL mostrar o caminho percorrido a partir de
"Início", e cada passo do caminho SHALL voltar àquele nível.

Pasta (`Folder`), em qualquer nível, SHALL poder ser escolhida e SHALL poder ser
aberta. Drive Compartilhado SHALL poder ser aberto e SHALL NOT poder ser escolhido
como pasta da base. O botão de confirmar SHALL ficar desabilitado sem pasta
escolhida, e com pasta escolhida SHALL nomeá-la. O seletor SHALL dizer que só
aparece o que foi compartilhado com o e-mail da conta, e SHALL oferecer recarregar
o nível atual.

#### Scenario: Nível de cima com as duas entradas
- **WHEN** a navegação sem `parentId` devolve um item `SharedDrive` "Suporte" e um
  item `Folder` "Guia do produto"
- **THEN** "Suporte" aparece sob "Drives compartilhados" sem opção de escolha, e
  "Guia do produto" aparece sob "Pastas compartilhadas com a conta" com opção de
  escolha

#### Scenario: Descida por subpastas e volta pelo caminho
- **WHEN** o operador abre "Suporte" e depois "Documentação"
- **THEN** o seletor consulta com `parentId` do Drive e depois com `parentId` da
  pasta, mostra o caminho "Início › Suporte › Documentação", e o clique em
  "Suporte" no caminho volta às subpastas do Drive

#### Scenario: Confirmar a pasta escolhida
- **WHEN** o operador escolhe "FAQ Suporte" e confirma
- **THEN** o botão de confirmar diz "Selecionar “FAQ Suporte”", o modal fecha, e o
  formulário mostra a pasta escolhida

#### Scenario: Recarregar o nível atual
- **WHEN** o operador aciona recarregar dentro de uma pasta
- **THEN** a rota é consultada de novo com o mesmo `parentId`

### Requirement: Estados de carregamento, vazio e erro do seletor
Enquanto a consulta de um nível não responde, o seletor SHALL indicar carregamento
e SHALL NOT exibir nenhum texto de erro nem de lista vazia. Lista vazia SHALL ser
dita como fato: no nível de cima, que nada foi compartilhado com a conta; dentro
de uma pasta, que ela não tem subpastas. Dentro de uma pasta com subpastas, o
seletor SHALL exibir a contagem devolvida. Falha da consulta SHALL ser exibida
como erro explícito, com o texto do código e a opção de tentar de novo, e SHALL
NOT ser exibida como lista vazia. As consultas ao `apps/connectors` SHALL NOT ser
repetidas automaticamente pela biblioteca de consulta antes de o erro aparecer.

#### Scenario: Carregamento sem texto de erro
- **WHEN** a consulta de pastas ainda não respondeu
- **THEN** o seletor indica carregamento, e o texto do modal não contém
  "Não foi possível", "erro", "fora do ar", "sem acesso" nem "Nenhuma"

#### Scenario: Pasta sem subpastas
- **WHEN** a consulta dentro de uma pasta devolve `[]`
- **THEN** o seletor diz que a pasta não tem subpastas, sem texto de erro

#### Scenario: apps/connectors fora do ar no seletor
- **WHEN** a consulta de pastas falha por rede
- **THEN** o seletor exibe o erro de serviço de conectores fora do ar e a opção de
  tentar de novo, e não exibe lista vazia

#### Scenario: Sem acesso a uma subpasta
- **WHEN** a consulta dentro de uma pasta responde `422` com
  `code: "access-denied"` e `detail` igual ao e-mail da conta
- **THEN** o seletor exibe o texto de `access-denied` com esse e-mail, e não exibe
  lista vazia

### Requirement: Pasta em uso desabilitada no seletor
O painel SHALL cruzar cada pasta do seletor com a listagem de bases
(`GET /knowledge-bases`): a pasta cujo provedor e id, comparados de forma exata e
sensível a caixa, são iguais ao `syncSource` de uma base existente, ativa ou
inativa, SHALL aparecer com a escolha desabilitada e com o texto "Já sincronizada
pela base “<nome>”", sem link. A pasta em uso SHALL continuar podendo ser aberta.
O cruzamento é ajuda visual: o `409` `folder-in-use` do `apps/api` continua sendo
a garantia. Se a listagem de bases falhar, o seletor SHALL dizer que não foi
possível conferir as pastas já usadas e que o cadastro confere ao criar, e SHALL
NOT marcar nenhuma pasta como em uso.

#### Scenario: Pasta usada por outra base
- **WHEN** a listagem de bases traz uma base "Políticas internas de RH" com
  `syncSource` `{ provider: "google-drive", folderId: "f-rh" }` e o seletor mostra a
  pasta `f-rh`
- **THEN** a pasta aparece com a escolha desabilitada e o texto "Já sincronizada
  pela base “Políticas internas de RH”", e continua podendo ser aberta

#### Scenario: Ids que diferem só na caixa não são a mesma pasta
- **WHEN** a base existente usa a pasta `AbC` e o seletor mostra a pasta `abc` do
  mesmo provedor
- **THEN** a pasta `abc` pode ser escolhida

#### Scenario: Listagem de bases indisponível
- **WHEN** `GET /knowledge-bases` falha e o seletor está aberto
- **THEN** o seletor avisa que não conferiu as pastas já usadas, e nenhuma pasta
  aparece desabilitada por uso

### Requirement: Texto em português para cada código de erro
O painel SHALL traduzir o `code` do `ProblemDetails` em texto próprio, pela tabela
da D3 do `design.md`, e SHALL NOT exibir o `title` nem o `detail` como mensagem,
exceto os dados que a tabela declara: o e-mail da conta em `access-denied`, o nome
da base em `folder-in-use` (de `knowledgeBaseName`), o status em
`connectors-error` e o motivo em `provider-error`. Código desconhecido no formato
de código SHALL ter texto genérico que o cita, e falha de rede SHALL ter texto
próprio de serviço fora do ar.

O erro do cadastro sincronizado SHALL ser exibido no card de Origem, mantendo o
que o operador preencheu, e SHALL dizer que nenhuma base foi criada. O texto de
`folder-in-use` SHALL nomear a base que usa a pasta e dizer que a pasta continua
ocupada mesmo com a base inativa, e SHALL NOT conter `exclu`, `remov` nem `apag`,
em qualquer caixa. O `401` do `apps/connectors` SHALL NOT encerrar a sessão do
operador nem redirecionar para o login.

#### Scenario: Cada código mostra o seu texto
- **WHEN** a navegação ou o cadastro respondem com cada código da tabela da D3
- **THEN** o painel exibe o texto daquele código, e nenhum `title` do
  `ProblemDetails` aparece na tela

#### Scenario: Pasta em uso no cadastro não manda excluir
- **WHEN** o `POST /knowledge-bases` responde `409` com `code: "folder-in-use"` e
  `knowledgeBaseName: "Políticas internas de RH"`
- **THEN** o card de Origem exibe um texto com "Políticas internas de RH" e que diz
  que a pasta continua ocupada mesmo com a base inativa, o texto não contém
  `exclu`, `remov` nem `apag`, e os campos preenchidos continuam na tela

#### Scenario: Sem acesso mostra o e-mail da conta
- **WHEN** o cadastro responde `422` com `code: "access-denied"` e `detail` igual a
  `conta@projeto.iam.gserviceaccount.com`
- **THEN** o texto exibido contém esse e-mail e a instrução de compartilhar a pasta
  como Leitor

#### Scenario: Cota passageira
- **WHEN** a navegação ou o cadastro respondem `503` com `code: "rate-limited"`
- **THEN** o texto diz que a falha é passageira e pede para tentar de novo, e não
  fala de acesso à pasta

#### Scenario: Código desconhecido
- **WHEN** o cadastro responde `422` com `code: "folder-too-deep"`
- **THEN** o painel exibe o texto genérico com `folder-too-deep`

#### Scenario: Token recusado pelo apps/connectors não desloga
- **WHEN** a consulta de pastas responde `401`
- **THEN** o token do operador continua guardado, o painel não navega para
  `/login`, e o seletor exibe o texto de sessão recusada pelo serviço de conectores

### Requirement: Cadastro de base sincronizada
Com Sincronizada marcada, provedor e pasta escolhidos, o formulário SHALL enviar
`POST /knowledge-bases` com `name`, `description`, `contentMode: "Synced"`,
`provider` igual à chave do provedor e `folderId` igual ao id da pasta escolhida,
sem `folderName` nem `folderUrl`. Em `201`, o operador SHALL ser levado ao
detalhe da base criada, com a notificação de cadastro de hoje.

#### Scenario: Corpo do cadastro sincronizado
- **WHEN** o operador cria uma base com o provedor `google-drive` e a pasta `f-faq`
- **THEN** o corpo do `POST /knowledge-bases` tem exatamente `name`, `description`,
  `contentMode` igual a `"Synced"`, `provider` igual a `"google-drive"` e
  `folderId` igual a `"f-faq"`

#### Scenario: Criação bem-sucedida
- **WHEN** o `apps/api` responde `201`
- **THEN** o operador é levado a `/knowledge-bases/{id}` da base criada
