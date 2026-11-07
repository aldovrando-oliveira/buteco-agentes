## ADDED Requirements

### Requirement: Página Minha conta em /account
`apps/frontend` SHALL prover a página `/account`, dentro da casca e da exigência de sessão, como as outras rotas autenticadas. Ela SHALL ter o título "Minha conta", o subtítulo "Os seus dados, a sua senha, a verificação em 2 etapas e os navegadores em que você está conectado." e, nesta ordem, os cartões "Dados", "Senha", "Verificação em 2 etapas" e "Sessões" do quadro `MinhaConta` (`entrega=m1`).
- A entrada para a página SHALL ser o link do rodapé (`frontend-app-shell`). Nenhum item de menu SHALL levar a ela.
- Cada cartão SHALL carregar os próprios dados, e SHALL mostrar um indicador de carregamento enquanto espera.
- Um erro no `GET` de um cartão SHALL mostrar dentro dele um aviso com o texto do erro (D8 do design) e o botão "Tentar de novo", sem afetar os outros cartões.

#### Scenario: Sem sessão, /account vai ao login
- **WHEN** alguém sem sessão abre `/account`
- **THEN** o painel vai a `/entrar` com `voltarPara=/account`

#### Scenario: Os quatro cartões, na ordem
- **WHEN** a pessoa abre `/account` com a sessão válida
- **THEN** a página mostra "Minha conta", o subtítulo e os cartões "Dados", "Senha", "Verificação em 2 etapas" e "Sessões", nesta ordem

#### Scenario: Erro num cartão não derruba os outros
- **WHEN** `GET /account/sessions` falha por rede, e as outras leituras respondem `200`
- **THEN** o cartão "Sessões" mostra o aviso com "Tentar de novo", e os outros três mostram os seus dados

#### Scenario: Tentar de novo relê o cartão
- **WHEN** o cartão mostra o aviso de erro, e a pessoa aciona "Tentar de novo"
- **THEN** o cartão chama o `GET` de novo, e mostra os dados quando ele responder `200`

### Requirement: Cartão Dados
O cartão "Dados" SHALL vir do `GET /account`, e não do id token, e SHALL mostrar, só para leitura:
- "Nome": `nome`;
- "E-mail": `email`, em fonte monoespaçada;
- "No painel desde": `criadoEm`, como `dd/mm/aaaa`;
- "Último acesso": `ultimoAcesso`, pelo formato relativo de datas desta capacidade.

Abaixo, SHALL mostrar o texto "Para mudar o nome ou o e-mail, use Usuários.", **sem link** enquanto a tela de Usuários não existir (#183). Nenhum campo do cartão SHALL ser editável.

#### Scenario: Os dados da conta
- **WHEN** `GET /account` responde `{ nome: "Ana Souza", email: "ana@exemplo.test", criadoEm: "2026-08-12T…", ultimoAcesso: <hoje às 14:15> }`
- **THEN** o cartão mostra "Ana Souza", "ana@exemplo.test", "12/08/2026" e "hoje, 14:15"

#### Scenario: "use Usuários" sem link
- **WHEN** o cartão é renderizado
- **THEN** o texto "Para mudar o nome ou o e-mail, use Usuários." aparece, e não há link nele

### Requirement: Troca da própria senha
O cartão "Senha" SHALL começar fechado, com "Você entra com `<e-mail>` e a sua senha." e o botão "Trocar senha". O botão SHALL abrir o formulário com:
- "Senha atual", "Nova senha" (com a ajuda "De 12 a 128 caracteres.") e "Repita a nova senha", os três pelo `PasswordField` (`frontend-password-field`);
- o aviso "Ao trocar, você sai de todos os navegadores, inclusive deste, e entra de novo com a nova senha.";
- "Trocar senha" e "Cancelar". "Cancelar" SHALL fechar o formulário e limpar os campos.

**Antes de enviar**, o painel SHALL conferir:
- a nova senha com a política (de 12 a 128 caracteres, contados sem `trim`). Fora dela: "A senha precisa ter de 12 a 128 caracteres." no campo "Nova senha", sem envio;
- a repetição igual à nova. Diferente: "As senhas não conferem." em "Repita a nova senha", sem envio.

**O `POST /account/password` com `{ senhaAtual, novaSenha }`:**
- `204`: o painel SHALL gravar o aviso de senha alterada (`operator-login-ui`) e, em seguida, chamar o mesmo logout do "Sair" (`panel-issuer-session`), com o botão desabilitado e em "Saindo…" até a navegação;
- `401`: o `title` do servidor ("Senha atual inválida.") SHALL ir no campo "Senha atual", com `aria-invalid="true"`, sem deslogar;
- `400`: o `title` SHALL ir no campo "Nova senha";
- `423`: o aviso de bloqueio desta capacidade, acima dos campos.

#### Scenario: Senha nova curta não envia
- **WHEN** a nova senha tem 11 caracteres, e a pessoa aciona "Trocar senha"
- **THEN** "A senha precisa ter de 12 a 128 caracteres." aparece em "Nova senha", e nenhum pedido sai

#### Scenario: Repetição diferente não envia
- **WHEN** a repetição difere da nova senha
- **THEN** "As senhas não conferem." aparece em "Repita a nova senha", e nenhum pedido sai

#### Scenario: Senha atual errada fica no campo, sem deslogar
- **WHEN** `POST /account/password` responde `401` com `application/problem+json` e `title` "Senha atual inválida."
- **THEN** "Senha atual inválida." aparece no campo "Senha atual", com `aria-invalid="true"`, o pedido saiu uma vez só, a sessão continua, e o painel não sai de `/account`

#### Scenario: Depois do 204, o aviso e o logout
- **WHEN** `POST /account/password` responde `204`
- **THEN** a chave `buteco.aviso.senha-alterada` é gravada no `sessionStorage`, e depois disso o logout do emissor é chamado

#### Scenario: Cancelar fecha e limpa
- **WHEN** a pessoa digita nos campos e aciona "Cancelar"
- **THEN** o cartão volta ao estado fechado, e reabrir o formulário mostra os campos vazios

### Requirement: Estado da verificação em 2 etapas
O cartão "Verificação em 2 etapas" SHALL vir do `GET /account/two-factor` (`{ ativa, ativadaEm, codigosRestantes, cadastroDisponivel }`):
- **`ativa: false` e `cadastroDisponivel: false`** (`indisponivel`): o selo "Não configurada", o texto "O cadastro da verificação em 2 etapas ainda não está disponível neste painel." e "Ligar verificação" **desabilitado**;
- **`ativa: false` e `cadastroDisponivel: true`** (`desligada`): o selo "Não configurada", o texto "Com a verificação ligada, além da senha você informa um código do app autenticador do celular." e "Ligar verificação" habilitado;
- **`ativa: true`** (`ativa`), com qualquer valor de `cadastroDisponivel`: o selo "Ativa", "desde `<ativadaEm>`" (`dd/mm/aaaa`), "N códigos de recuperação restantes.", "Gerar novos códigos" e "Remover verificação";
- **com `ativa: true` e `codigosRestantes` de 1 a 3** (`poucos-codigos`): também o aviso "Restam N códigos de recuperação. Gere novos para não ficar sem acesso se perder o celular.". Com 1, o texto SHALL ser "Resta 1 código de recuperação. Gere novos para não ficar sem acesso se perder o celular.";
- **com `ativa: true` e `codigosRestantes` 0**: o aviso "Não restam códigos de recuperação. Gere novos para não ficar sem acesso se perder o celular.".

Com 4 códigos ou mais, nenhum desses avisos SHALL aparecer.

#### Scenario: Cadastro fechado
- **WHEN** `GET /account/two-factor` responde `{ ativa: false, cadastroDisponivel: false }`
- **THEN** o cartão mostra "Não configurada" e o texto de cadastro indisponível, e "Ligar verificação" está desabilitado

#### Scenario: Cadastro aberto, verificação desligada
- **WHEN** `GET /account/two-factor` responde `{ ativa: false, cadastroDisponivel: true }`
- **THEN** "Ligar verificação" está habilitado

#### Scenario: Ativa com o cadastro fechado continua gerenciável
- **WHEN** `GET /account/two-factor` responde `{ ativa: true, codigosRestantes: 8, cadastroDisponivel: false }`
- **THEN** o cartão mostra "Ativa", "8 códigos de recuperação restantes.", "Gerar novos códigos" e "Remover verificação", habilitados

#### Scenario: Poucos códigos
- **WHEN** `codigosRestantes` é 3, 2, 1 ou 0
- **THEN** aparece o aviso com o texto do número (plural, singular para 1, o texto próprio para 0); com 4 ou mais, nenhum aviso

### Requirement: Ligar a verificação em 2 etapas
"Ligar verificação" SHALL levar ao passo da senha:
- o título "Ligar a verificação em 2 etapas" e o texto sobre o app autenticador;
- "Sua senha" (`PasswordField`, com a ajuda "Para começar, confirme que é você.");
- "Continuar" e "Cancelar".

**`POST /account/two-factor/setup` com `{ senha }`:**
- `200 { segredo, otpauthUri }`: o passo do QR;
- `401`: "Senha inválida." no campo;
- `423`: o aviso de bloqueio.

**O passo do QR:**
- o passo 1: o QR e o segredo (requisito "QR e segredo");
- o passo 2: o campo "Digite o código de 6 dígitos que o app mostra" (`inputmode="numeric"`, `autocomplete="one-time-code"`, placeholder `000 000`), a ajuda "Este cadastro vale por 10 minutos. Depois disso, comece de novo.", "Confirmar" e "Cancelar".

**`POST /account/two-factor/confirm` com `{ codigo }`:**
- `200 { codigosRecuperacao }`: o passo dos códigos, com o título "Verificação ligada.";
- `401`: "Código inválido. Confira se o relógio do celular está certo." no campo, com `aria-invalid="true"`. É o texto do quadro, e não o do servidor;
- `400`: o aviso "O cadastro venceu. Comece de novo para gerar outro QR.". É o texto do quadro, e não o do servidor;
- `423`: o aviso de bloqueio.

"Cancelar", em qualquer passo, SHALL voltar ao estado do `GET`. "Concluir", no passo dos códigos, SHALL reler o `GET` e mostrar o estado `ativa`.

#### Scenario: Senha errada no início do cadastro
- **WHEN** `POST /account/two-factor/setup` responde `401` "Senha inválida."
- **THEN** "Senha inválida." aparece no campo "Sua senha", sem deslogar

#### Scenario: Código errado no QR
- **WHEN** `POST /account/two-factor/confirm` responde `401`
- **THEN** "Código inválido. Confira se o relógio do celular está certo." aparece no campo do código, com `aria-invalid="true"`

#### Scenario: Cadastro vencido
- **WHEN** `POST /account/two-factor/confirm` responde `400`
- **THEN** o aviso "O cadastro venceu. Comece de novo para gerar outro QR." aparece no passo do QR

#### Scenario: Cadastro de ponta a ponta
- **WHEN** a senha é aceita, o código é aceito, e a pessoa aciona "Concluir"
- **THEN** o cartão mostra os 10 códigos com "Verificação ligada.", e depois de "Concluir" mostra "Ativa"

### Requirement: QR e segredo do cadastro
O QR SHALL ser desenhado no navegador pelo `QRCodeSVG` do `qrcode.react` 4.2.0, a partir do `otpauthUri` tal como veio da API:
- 164 px;
- `role="img"` e o nome acessível "QR para o app autenticador";
- módulos escuros sobre fundo branco, com margem, também no tema escuro;
- **sem `imageSettings`**: o SVG SHALL NOT conter `<image>`, `<foreignObject>` nem `<script>`;
- nenhum HTML vindo de dado entra por `innerHTML` (`panel-issuer-session`).

Ao lado do QR, SHALL aparecer:
- "Escaneie o QR com o app autenticador";
- "Ele aparece no app como **Buteco Agentes**, com o seu e-mail.";
- "Sem câmera? Digite este código no app:";
- o segredo em grupos de 4 caracteres separados por espaço, com "Copiar". "Copiar" SHALL copiar o segredo **sem os espaços**, como veio da API.

#### Scenario: O QR traz o otpauthUri
- **WHEN** `setup` responde `{ segredo: "JBSWY3DPEHPK3PXP", otpauthUri: "otpauth://totp/…" }`
- **THEN** o QR renderizado tem o nome acessível "QR para o app autenticador", e é o do `otpauthUri`

#### Scenario: O QR não traz imagem nem script
- **WHEN** o QR é renderizado
- **THEN** o SVG não contém `<image>`, `<foreignObject>` nem `<script>`

#### Scenario: Copiar copia sem espaços
- **WHEN** o segredo aparece como "JBSW Y3DP EHPK 3PXP", e a pessoa aciona "Copiar"
- **THEN** a área de transferência recebe "JBSWY3DPEHPK3PXP"

### Requirement: Códigos de recuperação mostrados uma vez
O passo dos códigos SHALL mostrar:
- os 10 códigos da resposta, em fonte monoespaçada;
- o texto de guardá-los fora do celular;
- "O Buteco não mostra estes códigos de novo.";
- "Copiar todos", "Baixar arquivo .txt" e "Concluir".

"Copiar todos" SHALL copiar os códigos, um por linha.

"Baixar arquivo .txt" SHALL gerar o arquivo **no navegador**, sem pedido ao servidor:
- um `Blob` `text/plain;charset=utf-8`, com o cabeçalho "Buteco Agentes — códigos de recuperação", o e-mail da conta, a data, e os códigos, um por linha;
- o nome `buteco-agentes-codigos-de-recuperacao.txt`;
- a URL do blob, criada para o download, SHALL ser liberada (`URL.revokeObjectURL`) logo depois.

#### Scenario: Copiar todos
- **WHEN** a pessoa aciona "Copiar todos"
- **THEN** a área de transferência recebe os 10 códigos, um por linha

#### Scenario: O .txt sai do navegador
- **WHEN** a pessoa aciona "Baixar arquivo .txt"
- **THEN** um arquivo `buteco-agentes-codigos-de-recuperacao.txt` é baixado com o cabeçalho e os 10 códigos, nenhum pedido sai ao servidor, e a URL do blob é liberada

### Requirement: Segredo e códigos só na memória da tela
O `segredo`, o `otpauthUri` e os códigos de recuperação SHALL ficar só no estado do componente do cartão:
- SHALL NOT ir para o `sessionStorage`, o `localStorage`, a URL, o `state` do router nem o cache do TanStack Query. As chamadas que os trazem ou os consomem (`setup`, `confirm`, `recovery-codes`, `remove`) SHALL NOT passar pelo `useMutation`: o `MutationCache` e o observador da mutação guardariam o resultado enquanto o cartão estivesse montado, e depois até o `gcTime`;
- SHALL sair ao deixar o passo (cancelar, confirmar, concluir) e ao sair da página.

#### Scenario: Nada no storage
- **WHEN** o cartão está no passo do QR ou no dos códigos
- **THEN** nem o `sessionStorage` nem o `localStorage` contêm o segredo ou algum código

#### Scenario: Nada no cache depois de sair
- **WHEN** a pessoa vê os códigos e sai de `/account`
- **THEN** o `MutationCache` e o `QueryCache` não contêm o segredo nem nenhum dos códigos

#### Scenario: Voltar à página não mostra os códigos
- **WHEN** a pessoa vê os códigos, sai de `/account` sem "Concluir" e volta
- **THEN** o cartão mostra o estado do `GET` (`ativa`), e não os códigos

### Requirement: Gerar novos códigos e remover a verificação
"Gerar novos códigos" SHALL abrir:
- o título "Gerar novos códigos de recuperação" e o texto "Os códigos atuais deixam de valer.";
- o campo "Código do app ou de recuperação" (`inputmode="text"`, `autocomplete="one-time-code"`, com `trim` só nas pontas);
- "Gerar códigos" e "Cancelar".

**`POST /account/two-factor/recovery-codes` com `{ codigo }`:**
- `200`: o passo dos códigos, com o título "Novos códigos gerados.";
- `401`: "Código inválido." no campo;
- `423`: o aviso de bloqueio.

"Remover verificação" SHALL abrir:
- o título "Remover a verificação em 2 etapas" e o texto "Depois disso, só a senha protege a sua conta, e os códigos de recuperação deixam de valer.";
- o mesmo campo;
- "Remover verificação" (ação de perigo) e "Cancelar".

**`POST /account/two-factor/remove` com `{ codigo }`:**
- `204`: relê o `GET`;
- `401`: "Código inválido." no campo;
- `423`: o aviso de bloqueio.

#### Scenario: Novos códigos
- **WHEN** `recovery-codes` responde `200` com 10 códigos
- **THEN** o cartão mostra "Novos códigos gerados." e os 10 códigos

#### Scenario: Código errado ao remover
- **WHEN** `remove` responde `401` "Código inválido."
- **THEN** "Código inválido." aparece no campo, a verificação continua ligada, e a sessão continua

#### Scenario: Remover desliga
- **WHEN** `remove` responde `204`, e o `GET` seguinte traz `ativa: false`
- **THEN** o cartão mostra "Não configurada"

### Requirement: Sessões da própria conta
O cartão "Sessões" SHALL vir do `GET /account/sessions`, na ordem do servidor, numa tabela com "Sessão", "Começou em", "Último uso" e "Ações":
- **o nome:** "Sessão N", pela posição na lista (1 para a primeira);
- **a sessão `atual: true`:** o selo "Esta sessão", "agora" no "Último uso" e a ação "Encerrar e sair". Ela é marcada pelo campo, e não pela posição;
- **as outras:** a ação "Encerrar";
- **"Começou em" e "Último uso"** (fora a atual): pelo formato relativo de datas;
- **"Encerrar as outras"**, no cabeçalho do cartão, só quando há outra sessão além da atual;
- **abaixo da tabela**, o texto do quadro sobre o que é uma sessão e a precisão de 15 minutos.

**Encerrar uma sessão:**
- "Encerrar" SHALL pedir confirmação: "Encerrar a Sessão N? Quem estiver usando esse navegador precisa entrar de novo.", com "Encerrar" e "Cancelar";
- confirmada, `POST /account/sessions/{id}/revoke`. No `204` ou no `404`, a lista SHALL ser relida, sem aviso de erro.

**Encerrar as outras:**
- "Encerrar as outras" SHALL pedir confirmação: "Encerrar as outras N sessões? Quem estiver usando esses navegadores precisa entrar de novo." Com uma outra sessão só, "Encerrar a outra sessão? Quem estiver usando esse navegador precisa entrar de novo.";
- confirmada, `POST /account/sessions/revoke-others`. No `200 { encerradas }`, o aviso SHALL ser "N sessões encerradas.", "1 sessão encerrada." ou, com 0, "Nenhuma outra sessão estava aberta.", e a lista SHALL ser relida.

**"Encerrar e sair"** SHALL chamar o logout do emissor (`panel-issuer-session`), sem confirmação e sem chamar a rota de revogação.

#### Scenario: A sessão atual é marcada pelo campo
- **WHEN** a lista tem três sessões, e a `atual` é a segunda
- **THEN** a segunda linha se chama "Sessão 2", tem "Esta sessão", "agora" e "Encerrar e sair", e as outras têm "Encerrar"

#### Scenario: Encerrar uma pede confirmação
- **WHEN** a pessoa aciona "Encerrar" na "Sessão 3"
- **THEN** aparece "Encerrar a Sessão 3? Quem estiver usando esse navegador precisa entrar de novo.", e nenhum pedido sai antes de "Encerrar"

#### Scenario: Sessão já encerrada
- **WHEN** a revogação responde `404`
- **THEN** a lista é relida, e nenhum aviso de erro aparece

#### Scenario: Encerrar as outras
- **WHEN** `revoke-others` responde `{ encerradas: 2 }`
- **THEN** aparece "2 sessões encerradas.", e a lista é relida

#### Scenario: A confirmação com uma outra sessão só
- **WHEN** a lista tem a sessão atual e mais uma, e a pessoa aciona "Encerrar as outras"
- **THEN** aparece "Encerrar a outra sessão? Quem estiver usando esse navegador precisa entrar de novo."

#### Scenario: Uma e nenhuma
- **WHEN** `revoke-others` responde `{ encerradas: 1 }` ou `{ encerradas: 0 }`
- **THEN** aparece "1 sessão encerrada." ou "Nenhuma outra sessão estava aberta."

#### Scenario: Sem outras sessões
- **WHEN** a lista tem só a sessão atual
- **THEN** "Encerrar as outras" não aparece

#### Scenario: Encerrar e sair
- **WHEN** a pessoa aciona "Encerrar e sair" na sessão atual
- **THEN** o logout do emissor é chamado, sem confirmação, e nenhum pedido sai a `/account/sessions/{id}/revoke`

### Requirement: Bloqueio e falhas passageiras nos cartões
**O bloqueio:** o `423` de qualquer rota de Minha conta SHALL mostrar, no cartão da ação, "Muitas tentativas sem sucesso. Por segurança, a sua conta fica pausada por N minutos.":
- N é o `Retry-After` arredondado para cima, em minutos, com o singular para 1;
- sem `Retry-After` legível, "alguns minutos";
- o botão da ação SHALL ficar desabilitado até o fim do `Retry-After`.

**Os estados que o quadro não desenha** SHALL aparecer como aviso dentro do cartão da ação, com o botão da ação desabilitado quando houver `Retry-After`:
- `429`: "Muitas tentativas ao mesmo tempo. Tente de novo em instantes.";
- `503`: "O painel está iniciando. Tente de novo em alguns segundos.";
- falha de rede: "Não foi possível falar com o servidor.";
- `500`, em "Gerar novos códigos" ou em "Remover verificação": o `title` do servidor ("Não foi possível conferir o código do app autenticador…");
- `409` ou `403` nas rotas das 2 etapas: o cartão SHALL reler o `GET` e ir ao estado que ele indicar.

Em todas essas falhas, o que a pessoa digitou SHALL continuar nos campos, e a sessão SHALL continuar.

#### Scenario: Bloqueio com o tempo do servidor
- **WHEN** `POST /account/password` responde `423` com `Retry-After: 300`
- **THEN** aparece "Muitas tentativas sem sucesso. Por segurança, a sua conta fica pausada por 5 minutos.", e "Trocar senha" fica desabilitado até passar o tempo

#### Scenario: Servidor indisponível no cadastro
- **WHEN** `POST /account/two-factor/setup` responde `503` com `Retry-After: 10`
- **THEN** o aviso "O painel está iniciando. Tente de novo em alguns segundos." aparece no cartão, e "Continuar" fica desabilitado por 10 segundos

#### Scenario: Cadastro fechado no meio do caminho
- **WHEN** `POST /account/two-factor/setup` responde `403`, e o `GET` seguinte traz `cadastroDisponivel: false`
- **THEN** o cartão mostra o estado de cadastro indisponível

#### Scenario: Chave do TOTP trocada ao gerar códigos
- **WHEN** `recovery-codes` responde `500` com o `title` "Não foi possível conferir o código do app autenticador. Use um código de recuperação ou fale com o administrador."
- **THEN** esse texto aparece no cartão, e o campo mantém o que foi digitado

### Requirement: Datas de Minha conta
- **"Último acesso", "Começou em" e "Último uso"** SHALL usar, no fuso do navegador:
  - "hoje, HH:MM" para hoje;
  - "ontem, HH:MM" para ontem;
  - `dd/mm/aaaa, HH:MM` para antes.
- **"No painel desde" e "desde"** (2 etapas) SHALL usar `dd/mm/aaaa`.
- O formatador SHALL receber o instante atual e o fuso, para não depender da máquina.

#### Scenario: Hoje, ontem e antes
- **WHEN** o agora é 06/11/2026 15:00 em `America/Sao_Paulo`, e as datas são 06/11 às 09:02, 05/11 às 21:15 e 28/10 às 18:30
- **THEN** o formatador devolve "hoje, 09:02", "ontem, 21:15" e "28/10/2026, 18:30"

#### Scenario: Virada do dia pelo fuso
- **WHEN** o agora é 06/11/2026 01:00 em `America/Sao_Paulo`, e a data é 05/11/2026 23:30 no mesmo fuso (06/11 02:30 UTC)
- **THEN** o formatador devolve "ontem, 23:30"
