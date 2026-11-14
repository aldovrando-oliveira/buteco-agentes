# panel-federated-accounts Specification

## Purpose
As contas de provedor federado no painel (#279): as formas de entrada em Minha conta, conectar e desconectar a própria conta, a volta do conectar em `/account`, e o Admin desconectando a conta de outra pessoa em Editar usuário, com a sessão dela acabando na próxima renovação.

## Requirements
### Requirement: Formas de entrada em Minha conta
`apps/frontend` SHALL mostrar em `/account`, entre os cartões "Dados" e "Senha", o cartão "Formas de entrada" do quadro `MinhaConta` (`entrega=m3`), com a tabela Forma · Identificação · Conectada em · Último uso · Ações. O cartão SHALL ler `GET /account` (`formasDeEntrada` e `contasConectadas`) e a lista anônima `GET /auth/federated/providers`.

O cartão SHALL aparecer só com a conta lida **e** pelo menos um destes: um provedor na lista, uma conta em `contasConectadas`, um aviso da volta do conectar ou do desconectar na tela, ou o formulário do conectar aberto (o `404` "Provedor não configurado." relê a lista, e o aviso dele está no formulário). Com o aviso e sem provedor nem conta, o cartão SHALL mostrar o aviso e só a linha "Senha". O aviso SHALL continuar até a página desmontar. Com `GET /account` em erro, o cartão SHALL NOT aparecer (o cartão "Dados" mostra o erro da mesma consulta). A lista dos provedores falhando, ou fora do formato de `panel-federated-login`, SHALL esconder só as linhas de provedor sem conta.

As linhas, nesta ordem:

| linha | quando | Identificação | Conectada em · Último uso | Ações |
|---|---|---|---|---|
| "Senha" | `formasDeEntrada` tem `"senha"` | o `email` da conta, em mono | "—" · "—" | "—" |
| a conta conectada | cada item de `contasConectadas` com `disponivel: true` | o `email` do item, em mono ("—" se nulo) | as datas, no formato de "Datas de Minha conta" (`panel-account-page`); `ultimoUso` nulo é "—" | "Desconectar" |
| o vínculo fora da configuração | cada item com `disponivel: false` | o `email` e o selo neutro "Provedor fora da configuração" | as datas | "Sem ação" |
| o provedor sem conta | cada provedor da lista, de tipo conhecido, cuja `chave` não está em nenhum item de `contasConectadas` | "Não conectada" | "—" · "—" | "Conectar Google" (azul) |

O rótulo da Forma SHALL vir do mapa de tipos conhecidos de `panel-federated-login` (`google` → "Google"); um `tipo` desconhecido SHALL sair cru, sem selo de sucesso. Um provedor da lista de tipo desconhecido SHALL NOT ganhar linha de conectar.

**A última forma de entrada** SHALL se contar como no backend: 1 pela `"senha"` em `formasDeEntrada`, e 1 por item de `contasConectadas` com `disponivel: true`. O vínculo com `disponivel: false` SHALL NOT contar. Com o total igual a 1, a conta conectada SHALL mostrar "Desconectar" desabilitado, com a dica "É a sua única forma de entrada.".

O rodapé do cartão SHALL ser "Cada forma de entrada leva ao mesmo usuário do Buteco. A conta Google só entra se estiver conectada aqui ou se você aceitou o convite com ela. A última forma de entrada não pode ser removida.".

#### Scenario: A senha e o Google conectado
- **WHEN** `GET /account` traz `formasDeEntrada: ["senha", "google"]` e uma conta `{ chave: "google", tipo: "google", email: "aldo@empresa.com", disponivel: true }`, e a lista traz o provedor `google`
- **THEN** o cartão mostra a linha "Senha" com o e-mail da conta e "—" nas Ações, e a linha "Google" com "aldo@empresa.com" e "Desconectar" habilitado, sem a linha "Não conectada"

#### Scenario: O provedor sem conta
- **WHEN** a conta tem só a senha, e a lista traz o provedor `google`
- **THEN** a linha "Google" mostra "Não conectada" e o botão "Conectar Google"

#### Scenario: Sem provedor e sem conta, o cartão não aparece
- **WHEN** a lista dos provedores é vazia e `contasConectadas` é vazia
- **THEN** a página não mostra o cartão "Formas de entrada"

#### Scenario: A recusa sem conta e sem lista mantém o cartão
- **WHEN** a pessoa chega a `/account?recusa=em-uso&provedor=google`, `contasConectadas` é vazia, e `GET /auth/federated/providers` falha por rede
- **THEN** o cartão "Formas de entrada" aparece com "Conta Google já conectada" e só a linha "Senha"

#### Scenario: Desconectar a única conta mantém o cartão com o aviso
- **WHEN** a lista dos provedores é vazia, a pessoa desconecta a única conta conectada, a rota responde `204`, e a releitura de `GET /account` traz `contasConectadas: []`
- **THEN** o cartão continua na tela com "Conta Google desconectada." e só a linha "Senha"

#### Scenario: A lista dos provedores falhando esconde só o conectar
- **WHEN** a conta tem a senha e uma conta Google conectada, e `GET /auth/federated/providers` falha por rede
- **THEN** o cartão mostra as linhas "Senha" e "Google" conectada, e nenhum "Conectar Google"

#### Scenario: O vínculo fora da configuração não tem ação
- **WHEN** a conta tem um item `{ chave: null, tipo: "google", email: "aldo@antiga.com.br", disponivel: false }`
- **THEN** a linha mostra "aldo@antiga.com.br", o selo "Provedor fora da configuração" e "Sem ação", sem botão

#### Scenario: Com uma forma só, Desconectar fica desabilitado
- **WHEN** `formasDeEntrada` é `["google"]` e `contasConectadas` tem uma conta disponível
- **THEN** a linha Google mostra "Desconectar" desabilitado com "É a sua única forma de entrada.", e não há linha "Senha"

#### Scenario: O vínculo indisponível não conta como forma
- **WHEN** `formasDeEntrada` é `["google"]`, e `contasConectadas` tem uma conta disponível e um vínculo com `disponivel: false`
- **THEN** a conta disponível mostra "Desconectar" desabilitado com "É a sua única forma de entrada."

#### Scenario: O tipo desconhecido sai cru
- **WHEN** a conta tem um item `{ chave: "sso", tipo: "keycloak", disponivel: true }`, e a lista traz um provedor de tipo `keycloak`
- **THEN** a linha da conta mostra "keycloak" na Forma, e nenhuma linha "Conectar" aparece para esse provedor

### Requirement: Conectar a conta do provedor
"Conectar Google" SHALL abrir, dentro do cartão "Formas de entrada" (sem modal), o formulário "Conectar a sua conta Google", com o texto "Você vai ao Google, escolhe a conta e volta para cá. Depois disso, pode entrar no painel também com o Google; a verificação em 2 etapas continua a mesma.", o campo da confirmação de quem chama (`panel-users-page`, "Senha de quem chama") com a ajuda "Para conectar, confirme que é você." no modo senha, "Continuar com Google" (azul) e "Cancelar". "Cancelar" SHALL fechar o formulário e limpar o campo.

"Continuar com Google" SHALL chamar `POST /account/federated/{chave}/connect` com `{ senha }` no modo senha e `{ codigo }` no modo código, nunca com os dois campos:

| resposta | a tela |
|---|---|
| `200 { url }` | "Redirecionando para o Google…" (`role="status"`) no lugar do formulário, e a navegação de página inteira ao `url` |
| `401` | o `title` no campo ("Senha inválida." ou "Código inválido."), com `aria-invalid="true"`, sem renovar a sessão |
| `404` com o `title` exatamente "Provedor não configurado." | o aviso vermelho "Provedor não configurado." no formulário; a lista dos provedores relida |
| outro `404` | o aviso vermelho "Não foi possível conectar a conta Google. Tente de novo."; a lista não é relida |
| `503` | o aviso vermelho "Não foi possível falar com o provedor. Tente de novo em instantes." |
| `423` | o aviso de bloqueio de "Senha de quem chama", e "Continuar com Google" desabilitado até o `Retry-After` |
| rede | o aviso "Não foi possível falar com o servidor." |
| outro (`403`, `500`) | o aviso de falha dos cartões, com o `title` |

#### Scenario: O formulário com a senha
- **WHEN** a pessoa com senha aciona "Conectar Google"
- **THEN** aparece "Conectar a sua conta Google", o campo "Sua senha" com "Para conectar, confirme que é você.", "Continuar com Google" e "Cancelar"

#### Scenario: O formulário com o código
- **WHEN** a pessoa sem senha aciona "Conectar Google"
- **THEN** o campo é "Código do app autenticador", com "Você entra só pelo Google: confirme com o código de 6 dígitos do app. O código de recuperação não vale aqui."

#### Scenario: O código vai como codigo
- **WHEN** a pessoa sem senha digita `123456` e aciona "Continuar com Google"
- **THEN** o corpo do `POST /account/federated/google/connect` é exatamente `{ "codigo": "123456" }`

#### Scenario: A senha vai como senha
- **WHEN** a pessoa com senha digita a senha e aciona "Continuar com Google"
- **THEN** o corpo é exatamente `{ "senha": "<a senha>" }`

#### Scenario: Redirecionando
- **WHEN** a rota responde `200 { url: "https://accounts.google.com/o/oauth2/v2/auth?…" }`
- **THEN** o cartão mostra "Redirecionando para o Google…", e o painel navega ao `url`

#### Scenario: Código errado fica no campo
- **WHEN** a rota responde `401` com `application/problem+json` e `title` "Código inválido."
- **THEN** o campo mostra "Código inválido." com `aria-invalid="true"`, fica vazio, e a sessão continua

#### Scenario: Provedor não configurado
- **WHEN** a rota responde `404` com `title` "Provedor não configurado."
- **THEN** o formulário mostra "Provedor não configurado.", e `GET /auth/federated/providers` é chamado de novo

#### Scenario: Outro 404
- **WHEN** a rota responde `404` sem corpo
- **THEN** o formulário mostra "Não foi possível conectar a conta Google. Tente de novo.", e a lista não é relida

#### Scenario: Provedor indisponível
- **WHEN** a rota responde `503`
- **THEN** o formulário mostra "Não foi possível falar com o provedor. Tente de novo em instantes."

#### Scenario: Conta pausada no conectar
- **WHEN** a rota responde `423` com `Retry-After: 300`
- **THEN** o formulário mostra "Muitas tentativas sem sucesso. Por segurança, a sua conta fica pausada por 5 minutos.", e "Continuar com Google" fica desabilitado

#### Scenario: Campo vazio não chama a rota
- **WHEN** a pessoa aciona "Continuar com Google" com o campo vazio
- **THEN** o campo mostra "Informe a sua senha." (ou "Informe o código."), e nenhum `POST …/connect` sai

### Requirement: A volta do conectar em /account
Ao montar, `/account` SHALL ler `conectado`, `recusa` e `provedor` da query e mostrar, no topo do cartão "Formas de entrada", o aviso:

| query | cor | título | texto |
|---|---|---|---|
| `conectado=<chave>` | verde (`role="status"`) | — | "Conta Google conectada. Da próxima vez, você também pode entrar com o Google." |
| `recusa=em-uso` | vermelho | "Conta Google já conectada" | "Esta conta Google já está conectada a outro usuário do Buteco Agentes. Escolha outra conta." |
| `recusa=ja-conectado` | amarelo | "Você já tem uma conta Google conectada" | "Para trocar de conta, desconecte a atual e conecte a outra." |
| `recusa=sessao-expirada` | amarelo | "A conexão expirou" | "A sua conta mudou enquanto você estava no Google: a senha, a verificação ou o acesso. Tente conectar de novo." |
| `recusa=dominio-nao-permitido` | vermelho | "Domínio não permitido" | "Esta conta Google não é do domínio permitido neste painel." |
| `recusa=falha` e qualquer outro valor de `recusa` | vermelho | — | "Não foi possível conectar a conta Google. Tente de novo." |

Depois de ler, a página SHALL trocar o endereço por `replace`, para `/account` sem `conectado`, `recusa` e `provedor`, **preservando o `state` da navegação**. O aviso SHALL ficar até a página desmontar. A página SHALL NOT chamar `POST /auth/federated/refusal`.

#### Scenario: A volta com a conta conectada
- **WHEN** a pessoa chega a `/account?conectado=google`
- **THEN** o cartão mostra "Conta Google conectada. Da próxima vez, você também pode entrar com o Google." em verde, e o endereço vira `/account`

#### Scenario: Conta Google já conectada a outro usuário
- **WHEN** a pessoa chega a `/account?recusa=em-uso&provedor=google`
- **THEN** o cartão mostra "Conta Google já conectada" e o texto do `em-uso`, em vermelho

#### Scenario: Já tem uma conta conectada
- **WHEN** a pessoa chega a `/account?recusa=ja-conectado&provedor=google`
- **THEN** o cartão mostra "Você já tem uma conta Google conectada" e o texto, em amarelo

#### Scenario: A conexão expirou
- **WHEN** a pessoa chega a `/account?recusa=sessao-expirada&provedor=google`
- **THEN** o cartão mostra "A conexão expirou" e "A sua conta mudou enquanto você estava no Google: a senha, a verificação ou o acesso. Tente conectar de novo.", em amarelo

#### Scenario: Domínio não permitido
- **WHEN** a pessoa chega a `/account?recusa=dominio-nao-permitido&provedor=google`
- **THEN** o cartão mostra "Domínio não permitido" e o texto, em vermelho

#### Scenario: Falha sem provedor
- **WHEN** a pessoa chega a `/account?recusa=falha`
- **THEN** o cartão mostra "Não foi possível conectar a conta Google. Tente de novo.", sem título

#### Scenario: Motivo desconhecido sai como falha
- **WHEN** a pessoa chega a `/account?recusa=outra-coisa&provedor=google`
- **THEN** o cartão mostra "Não foi possível conectar a conta Google. Tente de novo."

#### Scenario: Os parâmetros saem e o state fica
- **WHEN** a pessoa chega a `/account?recusa=em-uso&provedor=google` com um `state` de navegação
- **THEN** o endereço vira `/account` por `replace`, sem `recusa` nem `provedor`, e o `state` continua o mesmo

#### Scenario: Recarregar não repete o aviso
- **WHEN** a página foi aberta com `?conectado=google`, e a pessoa recarrega
- **THEN** a página abre sem o aviso

#### Scenario: Sem a rota do aviso
- **WHEN** a pessoa chega a `/account?recusa=em-uso&provedor=google`
- **THEN** nenhum `POST /auth/federated/refusal` sai

### Requirement: Desconectar a própria conta do provedor
"Desconectar" SHALL abrir a confirmação na célula de Ações da própria linha, com a linha em destaque: "Desconectar o Google (`<e-mail>`)? Você deixa de entrar com essa conta.", com "Desconectar" (perigo) e "Cancelar". "Desconectar" SHALL chamar `DELETE /account/federated/{chave}`, sem corpo:

| resposta | a tela |
|---|---|
| `204` | o aviso verde "Conta Google desconectada." no topo do cartão, e `GET /account` relido |
| `409` | na célula de Ações da mesma linha, "A última forma de entrada não pode ser removida." com "Fechar", e `GET /account` relido |
| `404` | o `title` ("Esta conta não está conectada a esse provedor.") no aviso do cartão, e `GET /account` relido |
| outro, rede | o aviso de falha dos cartões, com a confirmação aberta |

A sessão de quem desconecta SHALL continuar: nenhuma renovação, logout ou navegação SHALL acontecer pelo `204`.

#### Scenario: A confirmação na linha
- **WHEN** a pessoa aciona "Desconectar" na linha Google de "aldo@empresa.com"
- **THEN** a célula de Ações mostra "Desconectar o Google (aldo@empresa.com)? Você deixa de entrar com essa conta.", "Desconectar" e "Cancelar"

#### Scenario: Desconectada
- **WHEN** a confirmação é aceita e a rota responde `204`
- **THEN** o cartão mostra "Conta Google desconectada.", `GET /account` é chamado de novo, e a sessão continua, sem pedido ao `/connect/token`

#### Scenario: A última forma pela outra aba
- **WHEN** a rota responde `409` com `title` "A última forma de entrada não pode ser removida."
- **THEN** a linha mostra o texto com "Fechar", e `GET /account` é chamado de novo

#### Scenario: Já desconectada em outra aba
- **WHEN** a rota responde `404` com `title` "Esta conta não está conectada a esse provedor."
- **THEN** o cartão mostra o texto, e `GET /account` é chamado de novo

### Requirement: Desconectar pelo Admin em Editar usuário
Em Editar usuário de **outro** usuário (ativo ou desativado), a conta conectada disponível SHALL ter "Desconectar", que abre a confirmação na célula de Ações da linha: "Desconectar o Google de `<nome>`? A pessoa sai de todos os navegadores.", com "Desconectar" (perigo) e "Cancelar". Com a última forma (a regra de "Formas de entrada em Minha conta"), o botão SHALL ficar desabilitado com "É a única forma de entrada de `<nome>`.". No **próprio** usuário, a linha SHALL mostrar o link "Desconectar em Minha conta" (`/account`), e SHALL NOT mostrar "Desconectar". O vínculo fora da configuração SHALL mostrar o selo "Provedor fora da configuração" e "Sem ação".

"Desconectar" SHALL chamar `DELETE /users/{id}/federated/{chave}`, sem corpo:

| resposta | a tela |
|---|---|
| `204` | o aviso verde "Google desconectado. `<nome>` saiu de todos os navegadores." no cartão; o detalhe relido, e a lista de Usuários invalidada |
| `409` | na célula de Ações da mesma linha, "A última forma de entrada não pode ser removida." com "Fechar", no lugar de "Desconectar"; o detalhe relido |
| `404` com `title` "Usuário não encontrado." | o estado de usuário inexistente |
| outro `404` | o `title` no aviso do cartão; o detalhe relido |
| `403`, outro, rede | o aviso de falha dos cartões, com o `title` |

#### Scenario: O Admin desconecta o Google de outra pessoa
- **WHEN** o Admin aciona "Desconectar" na linha Google de Rafael Duarte
- **THEN** a célula mostra "Desconectar o Google de Rafael Duarte? A pessoa sai de todos os navegadores.", "Desconectar" e "Cancelar"

#### Scenario: Depois do 204
- **WHEN** a confirmação é aceita, a rota responde `204`, e a releitura traz `sessoes.ativas: 0` e sem a conta
- **THEN** o cartão mostra "Google desconectado. Rafael Duarte saiu de todos os navegadores.", e o cartão Sessões mostra "Nenhuma sessão ativa"

#### Scenario: A única forma de outra pessoa
- **WHEN** o detalhe de outro usuário tem `formasDeEntrada: ["google"]` e uma conta disponível
- **THEN** "Desconectar" está desabilitado com "É a única forma de entrada de Rafael Duarte."

#### Scenario: O próprio desconecta em Minha conta
- **WHEN** o Admin abre o próprio detalhe, com uma conta Google conectada
- **THEN** a linha Google mostra o link "Desconectar em Minha conta" para `/account`, e não mostra o botão "Desconectar"

#### Scenario: O desativado também desconecta
- **WHEN** o detalhe tem `estado: "desativado"` e uma conta Google conectada
- **THEN** a linha Google mostra "Desconectar" habilitado

#### Scenario: A última forma pela outra aba, no Admin
- **WHEN** a rota responde `409`
- **THEN** a célula de Ações da linha Google mostra "A última forma de entrada não pode ser removida." com "Fechar", e o detalhe é relido

### Requirement: A sessão de quem o Admin desconectou
Depois do desconectar pelo Admin, o backend revoga as sessões da pessoa e troca o stamp. O painel dessa pessoa SHALL NOT consultar nem esperar aviso: na próxima renovação, o `invalid_grant` SHALL encerrar a sessão e levar a `/entrar?motivo=sessao-expirada`, pelo `sessionFetch` de `panel-issuer-session`, como em toda sessão revogada.

#### Scenario: A renovação depois de desconectada
- **WHEN** a pessoa desconectada pelo Admin tem o painel aberto, e a renovação do token responde `400` com `error: "invalid_grant"`
- **THEN** o registro da sessão é apagado, e o painel vai a `/entrar?motivo=sessao-expirada`

