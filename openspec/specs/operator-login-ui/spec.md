# operator-login-ui Specification

## Purpose

TBD - defined by change auth-login-e-servico. Update Purpose after archive.
## Requirements
### Requirement: Tela de login do operador
`apps/frontend` SHALL prover a tela `/entrar`, com os campos "E-mail" (`autocomplete="username"`) e "Senha", submetendo as credenciais a `POST /auth/session` do `apps/api`. Formulário com e-mail ou senha vazios SHALL mostrar "Informe e-mail e senha." sem enviar. Em caso de `200` com `{ "segundoPasso": "totp" }`, a tela SHALL trocar o formulário pelo passo do código ("Passo das 2 etapas no /entrar"), sem gravar sessão. Em caso de `200` com `{ "segundoPasso": "cadastro" }`, a tela SHALL trocar o formulário pela etapa do cadastro ("Etapa do cadastro da verificação no /entrar"), sem gravar sessão. Um `200` com outro valor, ou sem corpo legível, SHALL ser tratado como resposta desconhecida. Em qualquer outra resposta, a tela SHALL permanecer em `/entrar`, sem gravar sessão, mostrando:

| resposta | texto |
|---|---|
| `400` | o `title` do servidor, ou "Informe e-mail e senha." sem ele |
| `401` | o `title` do servidor ("E-mail ou senha inválidos.") |
| `403` | o `title` do servidor |
| `423` | o aviso `bloqueado`: "Muitas tentativas sem sucesso. Por segurança, o acesso desta conta fica pausado por N minutos.", com N igual ao `Retry-After` em minutos, arredondado para cima |
| `429` | "Muitas tentativas ao mesmo tempo. Tente de novo em instantes." |
| `503` | "O painel está iniciando. Tente de novo em alguns segundos." |
| falha de rede | "Não foi possível falar com o servidor." |
| `200` desconhecido | "Não foi possível entrar. Tente de novo." |

Depois de `429` e `503`, o botão "Entrar" SHALL ficar desabilitado até passar o `Retry-After`. A tela SHALL NOT repetir o envio sozinha.

Com `?motivo=sessao-expirada`, a tela SHALL mostrar o aviso `sessao-expirada`, com `role="status"`: "Sua sessão expirou. Entre novamente para continuar de onde parou.". Com `?motivo=falha-na-entrada`, que o `/entrar/retorno` usa quando o código é recusado, a tela SHALL mostrar o aviso genérico, com `role="alert"`: "Não foi possível concluir a entrada. Entre de novo.".

Ao lado do rótulo "Senha", a tela SHALL mostrar o botão "Esqueceu a senha?", que abre e fecha, abaixo do campo, o texto "Peça a um administrador do painel um link para criar uma nova senha. O link vale por 24 horas.". Abaixo do cartão, a tela SHALL mostrar "Ainda não tem acesso? Peça um convite ao administrador do painel.".

Quando chega pelo `state` do router com um e-mail (vinda das páginas do link, `panel-access-link-pages`), a tela SHALL abrir com o campo "E-mail" preenchido com ele, e com o aviso do status que o login recusou, pela tabela acima.

`/login` SHALL redirecionar para `/entrar`, com `replace`.

#### Scenario: Senha certa sem a verificação abre o cadastro
- **WHEN** o operador preenche e-mail e senha corretos e o `POST /auth/session` responde `200` com `{ "segundoPasso": "cadastro" }`
- **THEN** a tela mostra a etapa do cadastro, o formulário de e-mail e senha some, nenhum registro de sessão é gravado, e o painel não navega ao authorize

#### Scenario: Senha certa de quem tem as 2 etapas abre o passo do código
- **WHEN** o `POST /auth/session` responde `200` com `{ "segundoPasso": "totp" }`
- **THEN** a tela mostra o campo "Código" e "Verificar", o formulário de e-mail e senha some, nenhum registro de sessão é gravado, e o painel não navega ao authorize

#### Scenario: Segundo passo desconhecido não abre etapa nenhuma
- **WHEN** o `POST /auth/session` responde `200` com `{ "segundoPasso": "webauthn" }`
- **THEN** a tela continua no formulário e mostra "Não foi possível entrar. Tente de novo."

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

### Requirement: Rotas da aplicação exigem sessão ativa
`apps/frontend` SHALL mandar para `/entrar` qualquer navegação para uma rota da aplicação quando não houver o registro da sessão do emissor (`panel-issuer-session`), guardando a rota pedida como `voltarPara`. `/entrar`, `/entrar/retorno` e `/login` SHALL ser as únicas rotas sem sessão.

#### Scenario: Acesso sem sessão redireciona para /entrar
- **WHEN** um usuário sem o registro da sessão navega para qualquer rota da aplicação diferente de `/entrar`, `/entrar/retorno` e `/login`
- **THEN** a interface vai a `/entrar`, sem renderizar o conteúdo da rota original

### Requirement: Resposta 401 encerra a sessão do operador
`apps/frontend` SHALL, ao receber `401 Unauthorized` de `apps/api` ou `apps/inbox` numa chamada autenticada, renovar a sessão uma vez e repetir a chamada. Se a repetição também responder `401`, ou se a renovação responder `invalid_grant`, SHALL apagar o registro da sessão e ir a `/entrar?motivo=sessao-expirada`, guardando a rota atual como `voltarPara`.

#### Scenario: 401 depois da renovação encerra a sessão
- **WHEN** uma chamada de uma feature autenticada (ex. listagem de agentes) recebe `401` de `apps/api`, e a repetição depois da renovação também recebe `401`
- **THEN** a interface apaga o registro da sessão e vai a `/entrar?motivo=sessao-expirada`

#### Scenario: 401 resolvido pela renovação não encerra
- **WHEN** a chamada recebe `401` e a repetição depois da renovação recebe `200`
- **THEN** a tela mostra o dado, e o registro da sessão continua

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

### Requirement: Aviso de senha alterada no /entrar
Depois da troca da própria senha em Minha conta (`panel-account-page`), o painel SHALL gravar a chave `buteco.aviso.senha-alterada` no `sessionStorage` da aba **antes** de chamar o logout do emissor. A chave SHALL chegar intacta ao `/entrar` carregado pelo `/connect/logout`: enquanto o logout desta aba está em andamento, a rota protegida não leva ao `/entrar` do próprio painel (`panel-issuer-session`, "Encerrar sessão revoga pelo emissor e avisa as outras abas"). Ao montar, o `/entrar` SHALL ler essa chave uma vez e apagá-la na mesma hora. Com a chave, SHALL mostrar acima do formulário de e-mail e senha o aviso "Senha alterada. Entre com a nova senha.", em `role="status"`, no lugar de "Sua sessão expirou. Entre novamente para continuar de onde parou.", mesmo quando a URL trouxer `motivo=sessao-expirada`. O aviso SHALL sumir na primeira submissão do formulário. Nada sobre a troca SHALL ir na URL, no `state` do router ou no `localStorage`.

#### Scenario: Depois da troca, o /entrar avisa
- **WHEN** a chave `buteco.aviso.senha-alterada` está no `sessionStorage`, e o `/entrar` abre
- **THEN** o aviso "Senha alterada. Entre com a nova senha." aparece em `role="status"`, e a chave sai do `sessionStorage`

#### Scenario: A chave sobrevive ao logout até o /entrar de verdade
- **WHEN** a troca responde `204`, o logout apaga o registro, e a página ainda não foi descarregada pela navegação ao `/connect/logout`
- **THEN** o `/entrar` do próprio painel não é renderizado, a chave continua no `sessionStorage`, e o `/entrar` carregado em seguida mostra o aviso e apaga a chave

#### Scenario: O aviso aparece uma vez só
- **WHEN** o `/entrar` mostrou o aviso, e a pessoa recarrega a página
- **THEN** o aviso não aparece de novo

#### Scenario: O aviso vence a sessão expirada
- **WHEN** a chave está no `sessionStorage`, e o `/entrar` abre com `?motivo=sessao-expirada`
- **THEN** aparece "Senha alterada. Entre com a nova senha.", e não "Sua sessão expirou…"

#### Scenario: Sem a chave, nada muda
- **WHEN** o `/entrar` abre sem a chave no `sessionStorage`
- **THEN** nenhum aviso de senha alterada aparece, e os outros avisos seguem as suas regras

#### Scenario: O aviso some ao enviar
- **WHEN** o aviso está na tela, e a pessoa envia e-mail e senha
- **THEN** o aviso sai, e o resultado do envio segue as regras do `/entrar`

### Requirement: Etapa do cadastro da verificação no /entrar
A etapa do cadastro SHALL ser um estado do `/entrar`, sem rota nem parâmetro de URL próprios: recarregar a página SHALL voltar a e-mail e senha. Ela SHALL mostrar o título "Cadastre a verificação em 2 etapas", o texto "Este painel exige a verificação em 2 etapas. Para entrar, cadastre um app autenticador no celular, como Google Authenticator, Microsoft Authenticator ou 1Password." e dois passos:

1. **"Escaneie o QR com o app"**: ao abrir a etapa, a tela SHALL chamar `POST /auth/session/enrollment/setup` sem corpo e sem token, e mostrar:
   - durante a chamada, "Gerando o QR…";
   - no `200`, o QR do `otpauthUri`, "Ele aparece no app como **Buteco Agentes**, com o seu e-mail.", "Sem câmera? Digite este código no app:", o segredo em grupos de 4 e "Copiar", com as regras do QR e do segredo abaixo;
   - em `429`, `503`, `500` ou falha de rede, "Não foi possível gerar o QR." com "Tentar de novo", que chama o `setup` de novo. Depois de `429` e `503`, "Tentar de novo" SHALL ficar desabilitado até passar o `Retry-After`.
2. **"Digite o código de 6 dígitos que o app mostra"**: o campo (`inputmode="numeric"`, `autocomplete="one-time-code"`, placeholder "000 000"), a ajuda "Este cadastro vale por 10 minutos. Depois disso, entre de novo com e-mail e senha.", "Confirmar" e "Voltar". Sem o QR (carregando ou falhou), o campo e "Confirmar" SHALL ficar desabilitados.

**O QR** SHALL ser o mesmo componente de Minha conta: o `QRCodeSVG` do `qrcode.react` 4.2.0, 164 px, `role="img"` com o nome acessível "QR para o app autenticador", módulos escuros sobre fundo branco com margem também no tema escuro, e sem `<image>`, `<foreignObject>` nem `<script>`. "Copiar" SHALL copiar o segredo sem os espaços.

**"Confirmar"** SHALL tirar os espaços das pontas e, com o campo vazio, mostrar "Informe o código." no campo, com `aria-invalid="true"`, sem enviar. Com o campo preenchido, SHALL enviar `{ "codigo" }` a `POST /auth/session/enrollment/confirm`, sem token, e tratar:

| resposta | o que a tela faz |
|---|---|
| `200 { codigosRecuperacao }` | o passo dos códigos |
| `401` com o `title` igual a "A entrada expirou. Informe e-mail e senha de novo." | volta a e-mail e senha, com esse texto num aviso com `role="status"` |
| `401` com qualquer outro `title` | "Código inválido. Confira se o relógio do celular está certo." no campo, com `aria-invalid="true"` |
| `423` | "Muitas tentativas sem sucesso. Por segurança, o acesso desta conta fica pausado por N minutos.", na etapa, com N do `Retry-After` |
| `429` | "Muitas tentativas ao mesmo tempo. Tente de novo em instantes.", na etapa |
| `503` | "O painel está iniciando. Tente de novo em alguns segundos.", na etapa |
| falha de rede | "Não foi possível falar com o servidor.", na etapa, mantendo o código digitado |

O `401` "A entrada expirou…" do `setup` SHALL ter o mesmo tratamento que no `confirm`. O `423` do `setup` SHALL mostrar o aviso de bloqueio da tabela e, no lugar do QR, "Não foi possível gerar o QR." com "Tentar de novo" desabilitado até passar o `Retry-After`. Depois de `429` e `503`, "Confirmar" SHALL ficar desabilitado até passar o `Retry-After`, e a tela SHALL NOT reenviar sozinha.

**O passo dos códigos** SHALL mostrar o título "Verificação ligada", "Guarde estes 10 códigos de recuperação num lugar seguro, fora do celular. Cada um vale uma vez e serve para entrar se você perder o app autenticador.", os 10 códigos em fonte monoespaçada, "O Buteco não mostra estes códigos de novo.", "Copiar todos", "Baixar .txt" e "Continuar". "Copiar todos" e "Baixar .txt" SHALL seguir as regras de Minha conta (um código por linha; o arquivo `buteco-agentes-codigos-de-recuperacao.txt` gerado no navegador, com a URL do blob liberada). "Continuar" SHALL seguir o fluxo do emissor exatamente como depois do `204` do segundo passo, com o mesmo `retorno` e o mesmo `voltarPara` da URL.

**"Voltar"** SHALL navegar a `/connect/logout`, sem `id_token_hint`, que encerra o cookie pendente no servidor e devolve a `/entrar` com e-mail e senha (`issuer-user-login`).

**O segredo, o `otpauthUri` e os códigos** SHALL ficar só no estado do componente da etapa: SHALL NOT ir para o `sessionStorage`, o `localStorage`, a URL, o `state` do router nem o cache do TanStack Query, e as chamadas `setup` e `confirm` SHALL NOT passar pelo `useMutation`.

Quando chega pelo `state` do router com `{ etapa: "cadastro" }` (vinda das páginas do link, `panel-access-link-pages`), o `/entrar` SHALL abrir direto na etapa do cadastro, com o aviso "Senha salva. Falta só cadastrar o app autenticador para entrar.", com `role="status"`. O `/entrar` SHALL ler esse `state` uma vez e limpá-lo com `navigate(…, { replace: true })`, mantendo a query: o navegador guarda o `state` no histórico, e recarregar a página SHALL NOT reabrir a etapa.

**A sessão completa do `confirm` dura 5 minutos**, como a do segundo passo. Quem aciona "Continuar" depois disso segue o fluxo do emissor, o authorize manda para `/entrar`, e a pessoa entra com a senha e o código do app que acabou de cadastrar. Os códigos de recuperação já mostrados continuam valendo.

#### Scenario: A etapa abre gerando o QR
- **WHEN** o `POST /auth/session` responde `200` com `{ "segundoPasso": "cadastro" }` e o `setup` ainda não respondeu
- **THEN** a tela mostra o título e o texto da etapa, "Gerando o QR…", e o campo do código e "Confirmar" desabilitados

#### Scenario: O QR aparece com o segredo
- **WHEN** o `setup` responde `200` com `{ segredo: "JBSWY3DPEHPK3PXP", otpauthUri: "otpauth://totp/…" }`
- **THEN** a tela mostra o QR com o nome acessível "QR para o app autenticador", o segredo como "JBSW Y3DP EHPK 3PXP", e o campo do código habilitado

#### Scenario: Falha ao gerar o QR e nova tentativa
- **WHEN** o `setup` falha por rede, e a pessoa aciona "Tentar de novo"
- **THEN** a tela mostra "Não foi possível gerar o QR." com "Tentar de novo" e o campo desabilitado, e o segundo acionamento faz um novo `POST /auth/session/enrollment/setup`

#### Scenario: Código certo mostra os códigos
- **WHEN** o `confirm` responde `200` com 10 códigos
- **THEN** a tela mostra "Verificação ligada", os 10 códigos, "Copiar todos", "Baixar .txt" e "Continuar", e o painel não navega sozinho

#### Scenario: Continuar segue o fluxo do emissor
- **WHEN** a etapa está aberta em `/entrar?voltarPara=%2Fagents`, o `confirm` respondeu `200`, e a pessoa aciona "Continuar"
- **THEN** o painel navega ao authorize como depois do `204` do segundo passo, e o login em andamento guarda `/agents` como `voltarPara`

#### Scenario: Código errado no cadastro
- **WHEN** o `confirm` responde `401` com o `title` "Código inválido."
- **THEN** o campo mostra "Código inválido. Confira se o relógio do celular está certo.", com `aria-invalid="true"`, e a tela continua na etapa com o QR

#### Scenario: Campo vazio não envia
- **WHEN** "Confirmar" é acionado com o campo vazio ou só com espaços
- **THEN** nenhum pedido sai, e o campo mostra "Informe o código." com `aria-invalid="true"`

#### Scenario: Prazo vencido volta a e-mail e senha
- **WHEN** o `confirm` responde `401` com o `title` "A entrada expirou. Informe e-mail e senha de novo."
- **THEN** a tela volta ao formulário de e-mail e senha e mostra esse texto num elemento com `role="status"`

#### Scenario: Bloqueio na etapa do cadastro
- **WHEN** o `confirm` responde `423` com `Retry-After: 600`
- **THEN** a etapa mostra "Muitas tentativas sem sucesso. Por segurança, o acesso desta conta fica pausado por 10 minutos."

#### Scenario: Muitas tentativas desabilita Confirmar até o Retry-After
- **WHEN** o `confirm` responde `429` com `Retry-After: 1`
- **THEN** a etapa mostra "Muitas tentativas ao mesmo tempo. Tente de novo em instantes.", e "Confirmar" fica desabilitado por 1 segundo

#### Scenario: Painel iniciando na etapa
- **WHEN** o `confirm` responde `503` com `Retry-After: 5`
- **THEN** a etapa mostra "O painel está iniciando. Tente de novo em alguns segundos.", e "Confirmar" fica desabilitado por 5 segundos

#### Scenario: Sem conexão no confirm
- **WHEN** o `fetch` do `confirm` falha por rede
- **THEN** a etapa mostra "Não foi possível falar com o servidor." num elemento com `role="alert"`, e o campo mantém o código digitado

#### Scenario: Voltar encerra o cadastro no servidor
- **WHEN** "Voltar" é acionado na etapa do cadastro
- **THEN** o painel navega a `/connect/logout`, sem `id_token_hint`

#### Scenario: Recarregar volta a e-mail e senha
- **WHEN** a página é recarregada com a etapa do cadastro aberta
- **THEN** a tela mostra o formulário de e-mail e senha

#### Scenario: Nada do cadastro fica guardado
- **WHEN** a etapa mostra o QR ou os códigos, e depois o painel segue o fluxo do emissor
- **THEN** nem o `sessionStorage`, nem o `localStorage`, nem o `QueryCache`, nem o `MutationCache` contêm o segredo, o `otpauthUri` ou algum código

#### Scenario: Continuar depois de 5 minutos
- **WHEN** o `confirm` respondeu `200`, mais de 5 minutos passam no passo dos códigos, e a pessoa aciona "Continuar"
- **THEN** o painel segue o fluxo do emissor, o authorize responde `302` para `/entrar`, e a pessoa entra com a senha e um código do app recém-cadastrado; um dos códigos de recuperação mostrados também é aceito no segundo passo

#### Scenario: Vindo do link, recarregar volta a e-mail e senha
- **WHEN** `/entrar?voltarPara=%2Fagents` é aberto com o `state` `{ etapa: "cadastro" }`, e a página é recarregada
- **THEN** a tela mostra o formulário de e-mail e senha, o `state` do histórico não traz mais a etapa, e a URL mantém `?voltarPara=%2Fagents`

#### Scenario: Vindo do link, o /entrar abre no cadastro
- **WHEN** `/entrar` é aberto com o `state` `{ etapa: "cadastro" }`
- **THEN** a tela mostra a etapa do cadastro com "Senha salva. Falta só cadastrar o app autenticador para entrar." num elemento com `role="status"`, e chama o `setup`

