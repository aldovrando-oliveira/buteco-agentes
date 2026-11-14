# panel-external-applications Specification

## Purpose
Diz como o painel (`apps/frontend`) mostra as aplicações externas ao Admin: a lista em
`/applications`, a criação com a senha de quem chama e a credencial mostrada uma vez (sem modal, e com
o segredo só no estado do componente), o detalhe com a aba Credencial e conexão (gerar, rotacionar e
revogar segredos, desativar e reativar) e a edição de nome e descrição. O contrato é o de
`external-applications` (#186); a aba Agentes e o card do agente estão em
`panel-external-application-agents`. Criada pela change `aplicacoes-externas-painel` (#188; ver
`openspec/changes/archive/2026-11-11-aplicacoes-externas-painel/`).
## Requirements
### Requirement: Lista de aplicações externas
`apps/frontend` SHALL mostrar, em `/applications`, a lista de `GET /applications/` como o quadro `AplicacoesLista` (`entrega=m2`):

- o título "Aplicações externas", o subtítulo "Sistemas que chamam agentes pelo protocolo A2A. Cada aplicação só alcança os agentes liberados para ela." e "Nova aplicação" (azul), que leva a `/applications/new`;
- a busca "Buscar por nome ou client ID" e o filtro de estado, um controle segmentado "Todas | Ativas | Desativadas" com o nome acessível "Estado" e sem rótulo visível, na mesma linha da busca, como nas listas de agentes e de bases, enviados ao servidor como `?busca=` (depois de 300 ms sem digitar, aparada, ausente quando vazia) e `?estado=ativas|desativadas` (ausente em Todas), mantendo a lista anterior na tela enquanto a nova carrega;
- a linha da busca e do filtro SHALL aparecer só depois da primeira resposta com dados, e daí em diante SHALL continuar na tela nas buscas e trocas de filtro seguintes, inclusive enquanto a nova lista carrega e quando ela falha, porque a busca vai ao servidor e não pode sumir enquanto a pessoa digita; ela SHALL NOT aparecer na primeira carga, no erro da primeira carga nem na lista vazia, como nas listas de agentes e de bases;
- as colunas Aplicação (o nome, com link para `/applications/{id}`, e a descrição), Identidade (o `clientId` e "Credencial do Buteco"), Agentes liberados (um chip por item de `agentes`, ou "Nenhum" quando a lista vem vazia), Estado ("Ativa" ou "Desativada") e Último uso (relativo, "há 3 min"; "Nunca usada" quando `ultimoUso` é `null`);
- o rodapé "{N} aplicações · as desativadas continuam listadas e podem ser reativadas.", com "1 aplicação" no singular.

Os estados sem tabela SHALL ser:

| estado | quando | o que a tela mostra |
|---|---|---|
| vazia | `200 []` sem busca e com Todas | "Nenhuma aplicação externa cadastrada.", "Cadastre uma aplicação para um sistema de fora chamar agentes pelo A2A." e "Nova aplicação", sem a linha da busca e do filtro |
| busca sem resultado | `200 []` com busca ou filtro | "Nenhuma aplicação encontrada para "{busca}"." — com o filtro, terminando em " entre as ativas." ou " entre as desativadas." — e "Limpar busca", que esvazia a busca e volta o filtro a Todas |
| carregando | a primeira carga | as linhas fantasmas, com `role="status"`, sem a linha da busca e do filtro |
| erro | falha da carga que não é `403` | "Não foi possível carregar as aplicações." com "Tentar de novo", em `role="alert"`; sem a linha da busca e do filtro se a falha é a da primeira carga |

#### Scenario: A lista do quadro
- **WHEN** `GET /applications/` devolve "Portal do Parceiro" com dois agentes, "Chatbot do site" sem agentes e nunca usada, e "CRM legado" desativada
- **THEN** a tabela mostra os chips do primeiro, "Nenhum" e "Nunca usada" no segundo, "Desativada" no terceiro, e o rodapé "3 aplicações · as desativadas continuam listadas e podem ser reativadas."

#### Scenario: Uma aplicação no rodapé
- **WHEN** a lista tem uma aplicação
- **THEN** o rodapé começa por "1 aplicação ·"

#### Scenario: A busca vai ao servidor
- **WHEN** o Admin digita "erp" na busca e escolhe Ativas
- **THEN** a chamada seguinte é `GET /applications/?busca=erp&estado=ativas`, feita depois da espera, e nenhuma chamada sai a cada tecla

#### Scenario: O filtro de estado é o controle segmentado
- **WHEN** a lista tem aplicações
- **THEN** a linha da busca mostra o grupo de opções "Estado", com "Todas" (marcada), "Ativas" e "Desativadas", e nenhuma lista suspensa

#### Scenario: Vazia e sem resultado são estados diferentes
- **WHEN** a resposta é `200 []` sem busca e com Todas
- **THEN** a página mostra "Nenhuma aplicação externa cadastrada.", sem a busca e sem o filtro
- **WHEN** a lista tem aplicações, e a busca "erp" com o filtro Ativas responde `200 []`
- **THEN** a página mostra "Nenhuma aplicação encontrada para "erp" entre as ativas." com "Limpar busca", com a busca e o filtro na tela, e "Limpar busca" esvazia a busca e volta o filtro a Todas

#### Scenario: A primeira carga não mostra a busca nem o filtro
- **WHEN** a primeira carga ainda não respondeu, ou falhou com `500`
- **THEN** a página mostra as linhas fantasmas, ou o erro com "Tentar de novo", sem a busca e sem o filtro, e a linha aparece quando a lista chega

#### Scenario: Na busca seguinte, a busca e o filtro continuam na tela
- **WHEN** a lista já chegou, e o Admin digita na busca ou troca o filtro enquanto a nova lista carrega, ou a nova lista falha com `500`
- **THEN** a busca, com o texto digitado, e o filtro, com a opção escolhida, continuam na tela

#### Scenario: Erro de carga
- **WHEN** `GET /applications/` falha com `500`
- **THEN** a página mostra "Não foi possível carregar as aplicações." e "Tentar de novo" chama a rota de novo

### Requirement: Nova aplicação
`apps/frontend` SHALL mostrar, em `/applications/new`, o formulário do quadro `AplicacaoNova` (`federadoDisponivel=false`), sem escolha de tipo:

- "Identificação": Nome ("Até 80 caracteres, sem repetir o nome de outra aplicação.") e Descrição ("Opcional, até 280 caracteres.");
- "Credencial": "O Buteco vai gerar um **client ID** e um **segredo** para esta aplicação. Eles aparecem uma única vez, logo depois de criar; o Buteco guarda só o hash do segredo." e "O endereço em que a aplicação pede o token (client credentials) aparece depois de criar.", sem nenhuma URL;
- "Confirmação": o campo da confirmação de quem chama ("Senha de quem chama", `panel-users-page`, com o modo); no modo senha, "Sua senha", com a dica "Criar a aplicação gera uma credencial. Confirme com a sua senha.";
- o aviso "A aplicação nasce **sem nenhum agente liberado**: até você liberar agentes na tela dela, toda chamada A2A é recusada.";
- "Cancelar" (para `/applications`) e "Criar aplicação" (azul), que mostra "Criando…" desabilitado enquanto envia.

Antes de enviar, o painel SHALL conferir o nome aparado vazio ("Informe o nome."), o nome acima de 80 ("O nome tem no máximo 80 caracteres."), a descrição acima de 280 ("A descrição tem no máximo 280 caracteres.") e o campo da confirmação vazio (o texto do vazio do modo), sem chamar o servidor. `POST /applications/` SHALL receber `{ nome, descricao, senha }` no modo senha e `{ nome, descricao, codigo }` no modo código, com a descrição vazia enviada como `null`. As respostas:

| resposta | o que a tela mostra |
|---|---|
| `400` com `errors.nome` ou `errors.descricao` | a mensagem no campo |
| `409` | o `title` no campo Nome ("Já existe uma aplicação com este nome."), a senha mantida |
| `401` | o `title` no campo da confirmação ("Senha inválida." ou "Código inválido."), o campo esvaziado, sem renovar a sessão |
| `423` | o aviso amarelo do bloqueio, com o N do `Retry-After` ("…pausada por N minutos."), e "Criar aplicação" desabilitado até lá |
| `403` | o aviso de ação de `panel-roles` |

#### Scenario: Senha vazia não envia
- **WHEN** o Admin preenche o nome e aciona "Criar aplicação" sem a senha
- **THEN** o campo mostra "Informe a sua senha.", e nenhum `POST /applications/` é feito

#### Scenario: Nome repetido
- **WHEN** `POST /applications/` responde `409` com `title` "Já existe uma aplicação com este nome."
- **THEN** o campo Nome mostra o `title`, e o campo da senha continua preenchido

#### Scenario: Senha errada
- **WHEN** `POST /applications/` responde `401` com `title` "Senha inválida."
- **THEN** o campo da senha mostra "Senha inválida." e fica vazio, e a sessão não é renovada nem encerrada

#### Scenario: Bloqueado
- **WHEN** `POST /applications/` responde `423` com `Retry-After: 300`
- **THEN** a seção "Confirmação" mostra "Muitas tentativas sem sucesso. Por segurança, a sua conta fica pausada por 5 minutos." em amarelo, e "Criar aplicação" fica desabilitado

#### Scenario: Sem URL antes de criar
- **WHEN** o Admin abre `/applications/new`
- **THEN** a seção "Credencial" não contém `connect/token`, e nenhum `GET /identity` é feito

#### Scenario: Criar com o código
- **WHEN** o Admin sem senha preenche o nome, digita `123456` em "Código do app autenticador" e aciona "Criar aplicação"
- **THEN** `POST /applications/` recebe `codigo: "123456"`, sem `senha`

### Requirement: Credencial criada, mostrada uma vez
Depois do `201` de `POST /applications/`, `apps/frontend` SHALL trocar o formulário, na mesma página `/applications/new`, pelo cartão "Aplicação criada" do quadro `AplicacaoCredencial`, sem janela modal:

- "Entregue estes dados ao responsável pelo **{nome}** por um canal seguro.";
- Client ID (`clientId`), Segredo (`segredo.valor`) e Endpoint de token (`endpointToken`), cada um com "Copiar", que vira "Copiado" por 2 s;
- o aviso amarelo "O segredo aparece **só agora**. O Buteco guarda apenas o hash e não consegue mostrá-lo de novo. Se ele se perder, gere um novo na aba Credencial e conexão.";
- a caixa "Guardei o segredo em um local seguro";
- "Ir para a aplicação" (azul), **desabilitado enquanto a caixa não está marcada**, que leva a `/applications/{id}`.

Sair da página sem marcar a caixa SHALL NOT ser bloqueado. Recarregar a página SHALL mostrar o formulário vazio, sem o segredo.

#### Scenario: Ir para a aplicação só depois da caixa
- **WHEN** o cartão aparece e o Admin aciona "Ir para a aplicação" sem marcar a caixa
- **THEN** o botão está desabilitado e a página continua no cartão; marcada a caixa, o botão leva a `/applications/{id}`

#### Scenario: Os três valores com Copiar
- **WHEN** o `201` traz `clientId` `btc_app_7kq2m9x4`, `segredo.valor` `btc_sk_…` e `endpointToken`
- **THEN** o cartão mostra os três, e "Copiar" do segredo copia o `valor` e mostra "Copiado"

#### Scenario: Sair sem marcar não é bloqueado
- **WHEN** o cartão está na tela sem a caixa marcada e o Admin clica em "Usuários" no menu
- **THEN** a página de Usuários abre, sem confirmação

### Requirement: O segredo vive só no componente que o recebeu
O `valor` de um segredo (das respostas de `POST /applications/` e de `POST /applications/{id}/secrets`) SHALL existir só no estado do componente que fez a chamada, e SHALL sair dele ao concluir, ao desmontar e ao recarregar. Ele SHALL NOT ser gravado:

- no cache de consultas nem no cache de mutações do TanStack Query (as duas chamadas não usam `useMutation`, e nenhuma resposta delas vai a `setQueryData`);
- no `state` da navegação do router, nem em `window.history.state`;
- no `sessionStorage` nem no `localStorage`.

Depois do `201`, o painel SHALL só invalidar as consultas da lista e do detalhe.

#### Scenario: Nada nos caches do TanStack Query
- **WHEN** o Admin cria uma aplicação e, no detalhe, gera um segredo
- **THEN** a serialização de `queryClient.getQueryCache().getAll()` e de `queryClient.getMutationCache().getAll()` não contém `btc_sk_`

#### Scenario: Nada no histórico nem no armazenamento
- **WHEN** o Admin cria uma aplicação, marca a caixa e aciona "Ir para a aplicação"
- **THEN** `window.history.state`, o `sessionStorage` e o `localStorage` não contêm `btc_sk_`

#### Scenario: Voltar não traz o segredo
- **WHEN** depois de "Ir para a aplicação" o Admin volta pelo histórico a `/applications/new`
- **THEN** a página mostra o formulário vazio, e não o cartão nem o segredo

### Requirement: Detalhe da aplicação
`apps/frontend` SHALL mostrar, em `/applications/{id}`, o detalhe de `GET /applications/{id}` como o quadro `AplicacaoDetalhe`:

- o link "Aplicações externas" para a lista, o nome, o selo "Ativa" ou "Desativada", a descrição abaixo do nome (quando há), e, desativada, "Desativada em {data}";
- "Editar" (para `/applications/{id}/edit`) e "Desativar", ou "Reativar" quando desativada;
- as abas "Agentes", com o contador da lista de agentes liberados, e "Credencial e conexão", com a aba no parâmetro `tab` da URL (`?tab=credencial`; sem `tab`, "Agentes").

`404` SHALL mostrar "Aplicação não encontrada." e "Voltar para Aplicações externas", no lugar do conteúdo; outra falha que não `403`, "Não foi possível carregar a aplicação." com "Tentar de novo"; a carga, um esqueleto com `role="status"`.

#### Scenario: Aplicação inexistente
- **WHEN** `GET /applications/{id}` responde `404` com `title` "Aplicação não encontrada."
- **THEN** a página mostra "Aplicação não encontrada." e o link para a lista, sem as abas

#### Scenario: A aba na URL
- **WHEN** o Admin abre `/applications/a1?tab=credencial`
- **THEN** a aba "Credencial e conexão" aparece selecionada, e trocar para "Agentes" tira `tab` da URL

#### Scenario: Desativada no cabeçalho
- **WHEN** o detalhe traz `ativa` `false` e `desativadaEm` 08/11/2026
- **THEN** o cabeçalho mostra "Desativada", "Desativada em 08/11/2026" e "Reativar", e não "Desativar"

### Requirement: Desativar e reativar a aplicação
"Desativar" SHALL abrir, acima das abas e sem janela modal, a confirmação "Desativar {nome}? Todos os segredos dela são revogados e as chamadas A2A são recusadas na hora. Os agentes liberados ficam guardados. Ao reativar, você precisa gerar um segredo novo.", com "Desativar" vermelho e "Cancelar". Só a confirmação SHALL enviar `POST /applications/{id}/deactivate`; a falha SHALL aparecer na mesma confirmação. "Reativar" SHALL enviar `POST /applications/{id}/activate` sem confirmação e, no `200`, mostrar o aviso "Aplicação reativada. Gere um segredo para ela voltar a obter token.".

#### Scenario: Cancelar não desativa
- **WHEN** o Admin aciona "Desativar" e depois "Cancelar"
- **THEN** nenhum `POST …/deactivate` é feito e a aplicação continua "Ativa"

#### Scenario: Confirmar desativa
- **WHEN** o Admin confirma a desativação e a rota responde `200` com o detalhe desativado
- **THEN** o cabeçalho mostra "Desativada" e "Reativar", e os segredos aparecem como revogados

#### Scenario: Reativar sem confirmação
- **WHEN** o Admin aciona "Reativar"
- **THEN** `POST …/activate` é feito na hora, e a página mostra "Aplicação reativada. Gere um segredo para ela voltar a obter token."

### Requirement: Aba Credencial e conexão
A aba SHALL mostrar o cartão "Credencial do Buteco" com o Client ID, o Endpoint de token (`endpointToken`) e a tabela de `segredos`, do mais novo ao mais antigo, com as colunas Segredo (`btc_sk_••••{sufixo}`), Estado, Criado em, Válido até, Último uso ("há …", ou "Nunca usado") e Ações:

| `estado` | selo | ação |
|---|---|---|
| `ativo` | "Ativo", verde | "Revogar" |
| `substituido` | "Substituído", amarelo | "Revogar agora" |
| `revogado` | "Revogado", cinza | — |
| `expirado` | "Expirado", cinza | — |

Abaixo da tabela, o texto "Ao gerar um novo segredo, o anterior continua valendo por 7 dias para a aplicação trocar sem parar. Revogar recusa o segredo na hora; um token já emitido continua valendo por até 5 minutos. Cada aplicação tem no máximo 3 segredos vivos, e cada segredo vale 1 ano.", e o cartão "Como a aplicação se conecta" com o `curl` do quadro (`scope=buteco-api`, `"method":"SendMessage"`) e o texto do AgentCard.

Sem segredo `ativo` nem `substituido`, o cartão SHALL mostrar, em amarelo, "Nenhum segredo válido: a aplicação não consegue obter token. Gere um novo segredo." — exceto com a aplicação desativada, quando a dica do botão basta.

#### Scenario: Os quatro estados
- **WHEN** o detalhe traz um segredo de cada estado
- **THEN** a tabela mostra "Ativo" com "Revogar", "Substituído" com "Revogar agora", e "Revogado" e "Expirado" sem ação

#### Scenario: Reativada, sem segredo vivo
- **WHEN** a aplicação ativa só tem segredos `revogado`
- **THEN** o cartão mostra "Nenhum segredo válido: a aplicação não consegue obter token. Gere um novo segredo."

### Requirement: Gerar novo segredo
"Gerar novo segredo" SHALL abrir, dentro do cartão "Credencial do Buteco", o formulário com o campo da confirmação de quem chama ("Senha de quem chama", `panel-users-page`, com o modo; no modo senha, "Sua senha" e a dica "Gerar um segredo cria uma credencial nova. Confirme com a sua senha."), "Gerar segredo" (azul) e "Cancelar"; com segredo vivo, o formulário SHALL dizer também "O segredo atual continua valendo por até 7 dias, para a aplicação trocar sem parar.". O campo vazio SHALL mostrar o texto do vazio do modo, sem enviar. `POST /applications/{id}/secrets` SHALL receber `{ senha }` ou `{ codigo }`, pelo modo; o `401`, o `423` e o `403` como na Nova aplicação, e o `409` e o `404` com o `title` no formulário.

O botão SHALL ficar desabilitado, com a dica ao lado:
- com 3 segredos vivos (`ativo` ou `substituido`): "Já há 3 segredos válidos. Revogue um para gerar outro.";
- com a aplicação desativada: "Reative a aplicação para gerar um segredo.".

No `201`, o formulário SHALL dar lugar ao bloco "Segredo novo": o `valor` com "Copiar", o aviso "O segredo aparece **só agora**. O Buteco guarda apenas o hash e não consegue mostrá-lo de novo." — seguido de "O anterior continua valendo até {data}." quando, no detalhe recarregado, há segredo `substituido` —, a caixa "Guardei o segredo em um local seguro" e "Concluir", desabilitado enquanto a caixa não está marcada, que fecha o bloco. O bloco SHALL ficar no estado da página do detalhe, e continuar na tela ao trocar de aba e voltar.

#### Scenario: Gerar com 3 vivos
- **WHEN** a aplicação tem um segredo `ativo` e dois `substituido`
- **THEN** "Gerar novo segredo" está desabilitado com "Já há 3 segredos válidos. Revogue um para gerar outro."

#### Scenario: O limite pela corrida
- **WHEN** o formulário está aberto e `POST …/secrets` responde `409` com `title` "Revogue um segredo antes de gerar outro."
- **THEN** o formulário mostra o `title`, e o detalhe é recarregado

#### Scenario: Desativada não gera
- **WHEN** a aplicação está desativada
- **THEN** "Gerar novo segredo" está desabilitado com "Reative a aplicação para gerar um segredo."

#### Scenario: O segredo novo
- **WHEN** `POST …/secrets` responde `201` e o detalhe recarregado traz o anterior como `substituido` até 16/11/2026
- **THEN** o cartão mostra "Segredo novo" com o `valor`, "O anterior continua valendo até 16/11/2026.", e "Concluir" desabilitado até a caixa ser marcada

#### Scenario: Gerar com o código
- **WHEN** o Admin sem senha digita `123456` e aciona "Gerar segredo"
- **THEN** `POST /applications/{id}/secrets` recebe exatamente `{ "codigo": "123456" }`

### Requirement: Revogar com confirmação na linha
"Revogar" e "Revogar agora" SHALL abrir, numa faixa logo abaixo da linha do segredo e sem janela modal, a confirmação com o botão vermelho de mesmo nome e "Cancelar":

- `ativo`: "Revogar o segredo btc_sk_••••{sufixo}? A aplicação deixa de obter token com ele na hora; um token já emitido continua valendo por até 5 minutos." — mais " Ela fica sem segredo válido até você gerar outro." quando ele é o único vivo;
- `substituido`: "Revogar agora o segredo btc_sk_••••{sufixo}? Ele valeria até {validoAte}. A aplicação deixa de obter token com ele na hora; um token já emitido continua valendo por até 5 minutos."

Só a confirmação SHALL enviar `POST /applications/{id}/secrets/{secretId}/revoke`; o `200` atualiza a tabela, e a falha (o `404` "Segredo não encontrado.", entre outras) aparece na faixa.

#### Scenario: O único vivo
- **WHEN** a aplicação tem um só segredo vivo e o Admin aciona "Revogar"
- **THEN** a faixa termina com "Ela fica sem segredo válido até você gerar outro.", e nenhum `POST …/revoke` é feito antes de confirmar

#### Scenario: Revogar agora o substituído
- **WHEN** o Admin aciona "Revogar agora" num segredo `substituido` válido até 28/10/2026 e confirma
- **THEN** a faixa disse "Ele valeria até 28/10/2026.", e a linha passa a "Revogado"

### Requirement: Uma ação por vez no cartão da credencial
Enquanto o formulário de gerar, o bloco "Segredo novo" ou uma confirmação de revogar estiver aberto, as outras ações do cartão ("Gerar novo segredo", "Revogar", "Revogar agora") SHALL ficar desabilitadas.

#### Scenario: Revogar com o formulário aberto
- **WHEN** o formulário de gerar está aberto
- **THEN** "Revogar" e "Revogar agora" estão desabilitados

### Requirement: Editar nome e descrição
`apps/frontend` SHALL mostrar, em `/applications/{id}/edit`, o formulário do quadro `AplicacaoEditar`: o link para o detalhe, o título "Editar aplicação", "Identificação" com Nome e Descrição preenchidos do detalhe e as dicas da Nova aplicação, o texto "O client ID e os segredos não mudam ao editar.", "Cancelar" (para o detalhe) e "Salvar" (azul), que mostra "Salvando…" enquanto envia. Sem seção "Credencial" e sem senha. `PUT /applications/{id}` SHALL receber `{ nome, descricao }`, com a conferência de campos da Nova aplicação antes de enviar; o `200` volta ao detalhe, o `400` e o `409` vão ao campo, e o `404` mostra o `title`.

#### Scenario: Nome repetido ao editar
- **WHEN** o Admin troca o nome para "Chatbot do site" e `PUT` responde `409`
- **THEN** o campo Nome mostra "Já existe uma aplicação com este nome.", e a página continua em edição

#### Scenario: Salvar volta ao detalhe
- **WHEN** `PUT` responde `200`
- **THEN** a página vai a `/applications/{id}` com o nome novo, sem pedir senha

