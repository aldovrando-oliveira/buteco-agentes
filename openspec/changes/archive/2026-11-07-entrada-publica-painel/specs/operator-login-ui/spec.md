## MODIFIED Requirements

### Requirement: Tela de login do operador
`apps/frontend` SHALL prover a tela `/entrar`, com os campos "E-mail" (`autocomplete="username"`) e "Senha", submetendo as credenciais a `POST /auth/session` do `apps/api`. Formulário com e-mail ou senha vazios SHALL mostrar "Informe e-mail e senha." sem enviar. Em caso de `204`, a tela SHALL seguir o fluxo do emissor (`panel-issuer-session`). Em caso de `200` com `{ "segundoPasso": "totp" }`, a tela SHALL trocar o formulário pelo passo do código ("Passo das 2 etapas no /entrar"), sem gravar sessão. Em qualquer outra resposta, a tela SHALL permanecer em `/entrar`, sem gravar sessão, mostrando:

| resposta | texto |
|---|---|
| `400` | o `title` do servidor, ou "Informe e-mail e senha." sem ele |
| `401` | o `title` do servidor ("E-mail ou senha inválidos.") |
| `403` | o `title` do servidor |
| `423` | o aviso `bloqueado`: "Muitas tentativas sem sucesso. Por segurança, o acesso desta conta fica pausado por N minutos.", com N igual ao `Retry-After` em minutos, arredondado para cima |
| `429` | "Muitas tentativas ao mesmo tempo. Tente de novo em instantes." |
| `503` | "O painel está iniciando. Tente de novo em alguns segundos." |
| falha de rede | "Não foi possível falar com o servidor." |

Depois de `429` e `503`, o botão "Entrar" SHALL ficar desabilitado até passar o `Retry-After`. A tela SHALL NOT repetir o envio sozinha.

Com `?motivo=sessao-expirada`, a tela SHALL mostrar o aviso `sessao-expirada`, com `role="status"`: "Sua sessão expirou. Entre novamente para continuar de onde parou.". Com `?motivo=falha-na-entrada`, que o `/entrar/retorno` usa quando o código é recusado, a tela SHALL mostrar o aviso genérico, com `role="alert"`: "Não foi possível concluir a entrada. Entre de novo.".

Ao lado do rótulo "Senha", a tela SHALL mostrar o botão "Esqueceu a senha?", que abre e fecha, abaixo do campo, o texto "Peça a um administrador do painel um link para criar uma nova senha. O link vale por 24 horas.". Abaixo do cartão, a tela SHALL mostrar "Ainda não tem acesso? Peça um convite ao administrador do painel.".

Quando chega pelo `state` do router com um e-mail (vinda das páginas do link, `panel-access-link-pages`), a tela SHALL abrir com o campo "E-mail" preenchido com ele, e com o aviso do status que o login recusou, pela tabela acima.

`/login` SHALL redirecionar para `/entrar`, com `replace`.

#### Scenario: Login bem-sucedido segue o fluxo do emissor
- **WHEN** o operador preenche e-mail e senha corretos e submete
- **THEN** o `POST /auth/session` leva `email` e `senha`, responde `204`, e o painel navega ao authorize

#### Scenario: Senha certa de quem tem as 2 etapas abre o passo do código
- **WHEN** o `POST /auth/session` responde `200` com `{ "segundoPasso": "totp" }`
- **THEN** a tela mostra o campo "Código" e "Verificar", o formulário de e-mail e senha some, nenhum registro de sessão é gravado, e o painel não navega ao authorize

#### Scenario: Credencial inválida exibe erro
- **WHEN** o `POST /auth/session` responde `401` com o `title` "E-mail ou senha inválidos."
- **THEN** a tela mostra "E-mail ou senha inválidos.", continua em `/entrar`, e nenhum registro de sessão é gravado

#### Scenario: Conta bloqueada mostra os minutos do Retry-After
- **WHEN** o `POST /auth/session` responde `423` com `Retry-After: 600`
- **THEN** a tela mostra o aviso `bloqueado` com "10 minutos"

#### Scenario: Bloqueio com segundos quebrados arredonda para cima
- **WHEN** o `POST /auth/session` responde `423` com `Retry-After: 61`
- **THEN** a tela mostra "2 minutos"

#### Scenario: Painel iniciando desabilita o botão até o Retry-After
- **WHEN** o `POST /auth/session` responde `503` com `Retry-After: 5`
- **THEN** a tela mostra "O painel está iniciando. Tente de novo em alguns segundos.", e o botão "Entrar" fica desabilitado por 5 segundos

#### Scenario: Sessão expirada mostra o aviso
- **WHEN** `/entrar?motivo=sessao-expirada` é aberto
- **THEN** a tela mostra "Sua sessão expirou. Entre novamente para continuar de onde parou." num elemento com `role="status"`

#### Scenario: Código recusado mostra o aviso genérico
- **WHEN** `/entrar?motivo=falha-na-entrada&voltarPara=%2Fagents` é aberto
- **THEN** a tela mostra "Não foi possível concluir a entrada. Entre de novo." num elemento com `role="alert"`

#### Scenario: Esqueceu a senha abre o texto na linha
- **WHEN** o operador aciona "Esqueceu a senha?"
- **THEN** a tela mostra "Peça a um administrador do painel um link para criar uma nova senha. O link vale por 24 horas.", sem navegar e sem enviar nada, e um segundo acionamento o esconde

#### Scenario: O convite ao administrador aparece abaixo do cartão
- **WHEN** `/entrar` é aberto
- **THEN** a tela mostra "Ainda não tem acesso? Peça um convite ao administrador do painel."

#### Scenario: Vindo do link com o login recusado, o e-mail chega preenchido
- **WHEN** `/entrar` é aberto com o `state` `{ email: "marcos.lima@empresa.com", status: 429 }`
- **THEN** o campo "E-mail" vem com "marcos.lima@empresa.com" e a tela mostra "Muitas tentativas ao mesmo tempo. Tente de novo em instantes."

#### Scenario: Link antigo para /login chega ao /entrar
- **WHEN** alguém navega para `/login`
- **THEN** a URL passa a `/entrar`

## ADDED Requirements

### Requirement: Passo das 2 etapas no /entrar
O passo do código SHALL ser um estado do `/entrar`, sem rota nem parâmetro de URL próprios: recarregar a página SHALL voltar a e-mail e senha. O passo SHALL mostrar "Verificação em duas etapas", "Digite o código de 6 dígitos do seu app autenticador. Sem o celular, use um dos seus códigos de recuperação.", o campo "Código", o botão "Verificar", "Voltar" e "Perdeu o celular e os códigos? Fale com o administrador.".

O campo "Código" SHALL ter `inputmode="text"`, `autocapitalize="characters"`, `autocomplete="one-time-code"`, `spellcheck="false"`, o placeholder "000 000" e a ajuda "Código de recuperação: 16 letras e números, como XXXX-XXXX-XXXX-XXXX.", ligada por `aria-describedby`. O painel SHALL tirar só os espaços das pontas e SHALL NOT validar a forma do código: a normalização é do servidor. Com o campo vazio, a tela SHALL mostrar "Informe o código." no campo, com `aria-invalid="true"`, sem enviar.

"Verificar" SHALL enviar `{ "codigo" }` em JSON a `POST /auth/session/second-factor`, sem token, e tratar a resposta assim:

| resposta | o que a tela faz |
|---|---|
| `204` | segue o fluxo do emissor exatamente como depois do `204` da senha, com o mesmo `retorno` e o mesmo `voltarPara` da URL |
| `401` com o `title` igual a "A entrada expirou. Informe e-mail e senha de novo." | volta a e-mail e senha, com esse texto num aviso com `role="status"` |
| `401` com qualquer outro `title` | "Código inválido." no campo, com `aria-invalid="true"` |
| `423` | o aviso `bloqueado` do `/entrar`, no passo, com N do `Retry-After` |
| `429` | "Muitas tentativas ao mesmo tempo. Tente de novo em instantes.", no passo |
| `503` | "O painel está iniciando. Tente de novo em alguns segundos.", no passo |
| `500` | o `title` do servidor, no passo, num aviso com `role="alert"` |
| falha de rede | "Não foi possível falar com o servidor.", no passo |

O texto do `401` de entrada expirada SHALL ser reconhecido por igualdade com uma constante do painel, que cita a origem no `apps/api`. Depois de `429` e `503`, "Verificar" SHALL ficar desabilitado até passar o `Retry-After`, e a tela SHALL NOT reenviar sozinha. "Voltar" SHALL voltar a e-mail e senha sem enviar nada.

Quando chega pelo `state` do router com `{ etapa: "codigo" }` (vinda das páginas do link, `panel-access-link-pages`), o `/entrar` SHALL abrir direto no passo do código, com o aviso "Senha salva. A sua conta usa verificação em 2 etapas: digite o código para entrar.", com `role="status"`.

#### Scenario: Código certo segue o fluxo do emissor
- **WHEN** o passo do código está aberto em `/entrar?voltarPara=%2Fagents` e o `POST /auth/session/second-factor` responde `204`
- **THEN** o painel navega ao authorize como depois do `204` da senha, e o login em andamento guarda `/agents` como `voltarPara`

#### Scenario: Código certo com retorno do authorize segue o retorno
- **WHEN** o passo do código está aberto em `/entrar?retorno=%2Fconnect%2Fauthorize%3Fclient_id%3Dbuteco-painel` e o `POST /auth/session/second-factor` responde `204`
- **THEN** o painel navega a `/connect/authorize?client_id=buteco-painel`

#### Scenario: Código errado aparece no campo
- **WHEN** o `POST /auth/session/second-factor` responde `401` com o `title` "Código inválido."
- **THEN** o campo mostra "Código inválido.", o `<input>` tem `aria-invalid="true"`, e a tela continua no passo

#### Scenario: Entrada expirada volta a e-mail e senha
- **WHEN** o `POST /auth/session/second-factor` responde `401` com o `title` "A entrada expirou. Informe e-mail e senha de novo."
- **THEN** a tela volta ao formulário de e-mail e senha e mostra "A entrada expirou. Informe e-mail e senha de novo." num elemento com `role="status"`

#### Scenario: Outro texto de 401 não é tomado por entrada expirada
- **WHEN** o `POST /auth/session/second-factor` responde `401` com o `title` "A entrada expirou."
- **THEN** a tela continua no passo e mostra "Código inválido." no campo

#### Scenario: Campo vazio não envia
- **WHEN** "Verificar" é acionado com o campo "Código" vazio ou só com espaços
- **THEN** nenhum pedido sai, e o campo mostra "Informe o código." com `aria-invalid="true"`

#### Scenario: Espaços nas pontas saem, o resto vai como está
- **WHEN** o campo tem "  7kq2 m9xd-4trb-pw6h  " e "Verificar" é acionado
- **THEN** o corpo do pedido é `{ "codigo": "7kq2 m9xd-4trb-pw6h" }`, com o espaço do meio, que é do servidor normalizar

#### Scenario: Bloqueio no passo mostra os minutos
- **WHEN** o `POST /auth/session/second-factor` responde `423` com `Retry-After: 600`
- **THEN** o passo mostra "Muitas tentativas sem sucesso. Por segurança, o acesso desta conta fica pausado por 10 minutos."

#### Scenario: Muitas tentativas desabilita Verificar até o Retry-After
- **WHEN** o `POST /auth/session/second-factor` responde `429` com `Retry-After: 1`
- **THEN** o passo mostra "Muitas tentativas ao mesmo tempo. Tente de novo em instantes.", e "Verificar" fica desabilitado por 1 segundo

#### Scenario: Painel iniciando no passo
- **WHEN** o `POST /auth/session/second-factor` responde `503` com `Retry-After: 5`
- **THEN** o passo mostra "O painel está iniciando. Tente de novo em alguns segundos.", e "Verificar" fica desabilitado por 5 segundos

#### Scenario: Chave trocada mostra o texto do servidor
- **WHEN** o `POST /auth/session/second-factor` responde `500` com o `title` "Não foi possível conferir o código do app autenticador. Use um código de recuperação ou fale com o administrador."
- **THEN** o passo mostra esse texto num elemento com `role="alert"`, e o campo continua disponível

#### Scenario: Sem conexão no passo do código
- **WHEN** o `fetch` do `POST /auth/session/second-factor` falha por rede
- **THEN** o passo mostra "Não foi possível falar com o servidor." num elemento com `role="alert"`, e o campo mantém o código digitado

#### Scenario: Voltar retorna a e-mail e senha
- **WHEN** "Voltar" é acionado no passo do código
- **THEN** a tela mostra o formulário de e-mail e senha, e nenhum pedido sai

#### Scenario: Recarregar volta a e-mail e senha
- **WHEN** a página é recarregada com o passo do código aberto
- **THEN** a tela mostra o formulário de e-mail e senha

#### Scenario: Vindo do link, o /entrar abre no passo do código
- **WHEN** `/entrar` é aberto com o `state` `{ etapa: "codigo" }`
- **THEN** a tela mostra o passo do código com "Senha salva. A sua conta usa verificação em 2 etapas: digite o código para entrar." num elemento com `role="status"`
