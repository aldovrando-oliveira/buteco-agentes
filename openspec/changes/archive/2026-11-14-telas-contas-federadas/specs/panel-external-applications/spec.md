## MODIFIED Requirements

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
