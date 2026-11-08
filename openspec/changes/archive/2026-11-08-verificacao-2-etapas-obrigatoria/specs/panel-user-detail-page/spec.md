## MODIFIED Requirements

### Requirement: Reconhecimento do próprio usuário
A página SHALL reconhecer o próprio usuário pelo `sub` do id token da sessão, comparado com o `:id` da rota sem diferença de caixa. O `sub` SHALL ser lido do id token na hora, e o registro da sessão SHALL NOT ganhar campo novo: um registro gravado antes desta change continua válido. Nome e e-mail SHALL NOT servir para o reconhecimento. Sem `sub` legível, o usuário SHALL ser tratado como outro. Quando o registro da sessão muda (outra aba), o reconhecimento SHALL ser refeito.

Para o próprio, a página SHALL mostrar:
- a nota "Este é você. A sua senha, a sua verificação em 2 etapas e as suas sessões ficam em Minha conta.", com "Minha conta" como link para `/account`, e `role="note"`;
- o selo "Você", e não "Desativar";
- na linha Senha, o link "Trocar em Minha conta", e não "Redefinir senha";
- na verificação, "Para trocar de celular, peça a outro usuário que redefina a sua verificação.", sem botão;
- nas sessões, a contagem do requisito "Sessões", a ajuda "As suas sessões aparecem em Minha conta, onde você pode encerrar cada uma." e o link "Ver em Minha conta", sem "Encerrar todas as sessões";
- o cartão Dados editável, como para outro usuário.

Os `403` do servidor para as ações vetadas a si mesmo SHALL aparecer pelo `title` no aviso da ação, caso o reconhecimento falhe. Na redefinição da verificação, o `title` é "Você não pode redefinir a própria verificação. Peça a outro usuário, ou use o comando de emergência no servidor." (`two-factor-totp`).

#### Scenario: O próprio é o do sub
- **WHEN** o `sub` do id token é `u-ana`, a rota é `/users/u-ana`, e outro usuário tem o mesmo nome e e-mail que constam na sessão
- **THEN** a página de `/users/u-ana` mostra o selo "Você" e a nota "Este é você…", e a página de qualquer outro id não mostra

#### Scenario: Id da rota em maiúsculas ainda é o próprio
- **WHEN** o `sub` é `3f2a…c1` e a rota é `/users/3F2A…C1`
- **THEN** a página mostra o selo "Você"

#### Scenario: Registro gravado antes desta change continua válido
- **WHEN** o registro da sessão no `localStorage` tem só os campos de antes desta change
- **THEN** o `parseSession` o aceita, e a sessão continua aberta

#### Scenario: Sem sub legível
- **WHEN** o id token da sessão não tem payload legível
- **THEN** a página trata o usuário como outro, sem erro

#### Scenario: O próprio não tem Desativar
- **WHEN** a página é a do próprio usuário
- **THEN** não há "Desativar", "Redefinir senha", "Redefinir verificação" nem "Encerrar todas as sessões", há os links para Minha conta, e a verificação mostra "Para trocar de celular, peça a outro usuário que redefina a sua verificação."

#### Scenario: 403 do servidor no aviso
- **WHEN** o reconhecimento falhou, e "Desativar" confirmado recebe `403` "Você não pode desativar o próprio usuário."
- **THEN** o texto aparece no aviso da confirmação, e a página não muda de estado

#### Scenario: 403 da redefinição de si mesmo
- **WHEN** o reconhecimento falhou, e "Redefinir" confirmado recebe `403` "Você não pode redefinir a própria verificação. Peça a outro usuário, ou use o comando de emergência no servidor."
- **THEN** o texto aparece no aviso da confirmação, e a verificação continua "Ativa"

### Requirement: Verificação em 2 etapas de outro usuário
O cartão "Verificação em 2 etapas" SHALL mostrar, por `duasEtapas`:
- `true`: o selo "Ativa" (verde), "Use Redefinir se a pessoa perdeu ou trocou o celular e os códigos de recuperação." e o botão "Redefinir verificação";
- `false`: o selo "Não configurada" (amarelo), "A pessoa cadastra a verificação no próximo login." e o botão "Redefinir verificação" desabilitado.

O desativado com a verificação ativa SHALL ter o botão habilitado. O cartão SHALL NOT mostrar a data de ativação.

"Redefinir verificação" SHALL abrir, no cartão, a confirmação "Redefinir a verificação de {nome}? O app autenticador e os códigos de recuperação deixam de valer, e a pessoa sai de todos os navegadores. No próximo login, ela cadastra o app autenticador de novo.", com "Redefinir" (perigo) e "Cancelar". "Redefinir" SHALL chamar `POST /users/{id}/two-factor/reset`, sem corpo:
- `204` ou `409`: a confirmação fecha, sem aviso, e o detalhe é relido; o cartão e as sessões passam a refletir a releitura;
- `403`: o `title` no aviso da confirmação;
- `404`: a página vai ao estado de usuário inexistente;
- `429`, `503` ou rede: o aviso de falha dos cartões, com a confirmação aberta.

#### Scenario: Verificação ativa
- **WHEN** o detalhe de outro usuário tem `duasEtapas: true`
- **THEN** o cartão mostra "Ativa", "Use Redefinir se a pessoa perdeu ou trocou o celular e os códigos de recuperação." e o botão "Redefinir verificação" habilitado

#### Scenario: Com a verificação desligada, o botão fica desabilitado
- **WHEN** o detalhe tem `duasEtapas: false`
- **THEN** o cartão mostra "Não configurada" e "A pessoa cadastra a verificação no próximo login.", e "Redefinir verificação" está desabilitado

#### Scenario: A confirmação diz o que acontece no próximo login
- **WHEN** "Redefinir verificação" é acionado para Rafael Duarte
- **THEN** a confirmação mostra "Redefinir a verificação de Rafael Duarte? O app autenticador e os códigos de recuperação deixam de valer, e a pessoa sai de todos os navegadores. No próximo login, ela cadastra o app autenticador de novo."

#### Scenario: Depois de redefinir a verificação, as sessões vão a Nenhuma sessão ativa
- **WHEN** a confirmação é aceita, `POST …/two-factor/reset` responde `204`, e a releitura traz `duasEtapas: false` e `sessoes.ativas: 0`
- **THEN** o cartão mostra "Não configurada", e o cartão Sessões mostra "Nenhuma sessão ativa", sem "Encerrar todas as sessões"

#### Scenario: Já redefinida em outra aba
- **WHEN** a rota responde `409`
- **THEN** a confirmação fecha sem aviso, e o detalhe é relido
