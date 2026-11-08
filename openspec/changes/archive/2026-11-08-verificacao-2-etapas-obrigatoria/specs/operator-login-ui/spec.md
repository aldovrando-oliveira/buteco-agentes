## MODIFIED Requirements

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

## ADDED Requirements

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
