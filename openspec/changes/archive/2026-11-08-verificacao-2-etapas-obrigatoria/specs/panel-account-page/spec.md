## REMOVED Requirements

### Requirement: Ligar a verificação em 2 etapas
**Reason**: o cadastro passa à etapa do `/entrar`, o único caminho (decisão do dono, Q5). As rotas por access token saem do `apps/api`.
**Migration**: a etapa do cadastro de `operator-login-ui` ("Etapa do cadastro da verificação no /entrar"). Quem não tem a verificação cadastra no próximo login.

### Requirement: QR e segredo do cadastro
**Reason**: o QR e o segredo só aparecem na etapa do cadastro do `/entrar`.
**Migration**: as mesmas regras (o `QRCodeSVG` 4.2.0, 164 px, o nome acessível, o fundo branco, sem `<image>`, `<foreignObject>` nem `<script>`, "Copiar" sem espaços) estão em `operator-login-ui`, "Etapa do cadastro da verificação no /entrar".

## RENAMED Requirements

- FROM: `### Requirement: Gerar novos códigos e remover a verificação`
- TO: `### Requirement: Gerar novos códigos de recuperação`

## MODIFIED Requirements

### Requirement: Página Minha conta em /account
`apps/frontend` SHALL prover a página `/account`, dentro da casca e da exigência de sessão, como as outras rotas autenticadas. Ela SHALL ter o título "Minha conta", o subtítulo "Os seus dados, a sua senha, os códigos de recuperação e os navegadores em que você está conectado." e, nesta ordem, os cartões "Dados", "Senha", "Verificação em 2 etapas" e "Sessões" do quadro `MinhaConta` (`entrega=m1`).
- A entrada para a página SHALL ser o link do rodapé (`frontend-app-shell`). Nenhum item de menu SHALL levar a ela.
- Cada cartão SHALL carregar os próprios dados, e SHALL mostrar um indicador de carregamento enquanto espera.
- Um erro no `GET` de um cartão SHALL mostrar dentro dele um aviso com o texto do erro (D8 do design) e o botão "Tentar de novo", sem afetar os outros cartões.

#### Scenario: Sem sessão, /account vai ao login
- **WHEN** alguém sem sessão abre `/account`
- **THEN** o painel vai a `/entrar` com `voltarPara=/account`

#### Scenario: Os quatro cartões, na ordem
- **WHEN** a pessoa abre `/account` com a sessão válida
- **THEN** a página mostra "Minha conta", o subtítulo "Os seus dados, a sua senha, os códigos de recuperação e os navegadores em que você está conectado." e os cartões "Dados", "Senha", "Verificação em 2 etapas" e "Sessões", nesta ordem

#### Scenario: Erro num cartão não derruba os outros
- **WHEN** `GET /account/sessions` falha por rede, e as outras leituras respondem `200`
- **THEN** o cartão "Sessões" mostra o aviso com "Tentar de novo", e os outros três mostram os seus dados

#### Scenario: Tentar de novo relê o cartão
- **WHEN** o cartão mostra o aviso de erro, e a pessoa aciona "Tentar de novo"
- **THEN** o cartão chama o `GET` de novo, e mostra os dados quando ele responder `200`

### Requirement: Estado da verificação em 2 etapas
O cartão "Verificação em 2 etapas" SHALL vir do `GET /account/two-factor` (`{ ativa, ativadaEm, codigosRestantes }`):
- **`ativa: false`** (`nao-configurada`): o selo "Não configurada" e o texto "Este painel passou a exigir a verificação em 2 etapas. Na próxima vez que você entrar, o login pede o cadastro do app autenticador.", sem botão. É o intervalo em que o access token de uma sessão de antes da obrigatoriedade ainda vale;
- **`ativa: true`** (`ativa`): o selo "Ativa", "desde `<ativadaEm>`" (`dd/mm/aaaa`), "N códigos de recuperação restantes.", "Gerar novos códigos" e o texto "Trocou de celular ou perdeu o app? Peça a outro usuário do painel que redefina a sua verificação; no próximo login você cadastra o app de novo.". O cartão SHALL NOT ter "Remover verificação";
- **com `ativa: true` e `codigosRestantes` de 1 a 3** (`poucos-codigos`): também o aviso "Restam N códigos de recuperação. Gere novos para não ficar sem acesso se perder o celular.". Com 1, o texto SHALL ser "Resta 1 código de recuperação. Gere novos para não ficar sem acesso se perder o celular.";
- **com `ativa: true` e `codigosRestantes` 0**: o aviso "Não restam códigos de recuperação. Gere novos para não ficar sem acesso se perder o celular.".

Com 4 códigos ou mais, nenhum desses avisos SHALL aparecer.

#### Scenario: Sem a verificação, o cartão não oferece cadastro
- **WHEN** `GET /account/two-factor` responde `{ ativa: false, ativadaEm: null, codigosRestantes: 0 }`
- **THEN** o cartão mostra "Não configurada" e "Este painel passou a exigir a verificação em 2 etapas. Na próxima vez que você entrar, o login pede o cadastro do app autenticador.", sem nenhum botão

#### Scenario: Ativa, sem remover
- **WHEN** `GET /account/two-factor` responde `{ ativa: true, codigosRestantes: 8 }`
- **THEN** o cartão mostra "Ativa", "8 códigos de recuperação restantes.", "Gerar novos códigos" habilitado e o texto "Trocou de celular ou perdeu o app?…", e não há "Remover verificação"

#### Scenario: Poucos códigos
- **WHEN** `codigosRestantes` é 3, 2, 1 ou 0
- **THEN** aparece o aviso com o texto do número (plural, singular para 1, o texto próprio para 0); com 4 ou mais, nenhum aviso

### Requirement: Segredo e códigos só na memória da tela
Os códigos de recuperação gerados em Minha conta SHALL ficar só no estado do componente do cartão:
- SHALL NOT ir para o `sessionStorage`, o `localStorage`, a URL, o `state` do router nem o cache do TanStack Query. A chamada que os traz (`recovery-codes`) SHALL NOT passar pelo `useMutation`: o `MutationCache` e o observador da mutação guardariam o resultado enquanto o cartão estivesse montado, e depois até o `gcTime`;
- SHALL sair ao deixar o passo (cancelar, concluir) e ao sair da página.

#### Scenario: Nada no storage
- **WHEN** o cartão está no passo dos códigos
- **THEN** nem o `sessionStorage` nem o `localStorage` contêm algum código

#### Scenario: Nada no cache depois de sair
- **WHEN** a pessoa vê os códigos novos e sai de `/account`
- **THEN** o `MutationCache` e o `QueryCache` não contêm nenhum dos códigos

#### Scenario: Voltar à página não mostra os códigos
- **WHEN** a pessoa vê os códigos, sai de `/account` sem "Concluir" e volta
- **THEN** o cartão mostra o estado do `GET` (`ativa`), e não os códigos

### Requirement: Gerar novos códigos de recuperação
"Gerar novos códigos" SHALL abrir:
- o título "Gerar novos códigos de recuperação" e o texto "Os códigos atuais deixam de valer.";
- o campo "Código do app ou de recuperação" (`inputmode="text"`, `autocomplete="one-time-code"`, com `trim` só nas pontas);
- "Gerar códigos" e "Cancelar".

**`POST /account/two-factor/recovery-codes` com `{ codigo }`:**
- `200`: o passo dos códigos, com o título "Novos códigos gerados.";
- `401`: "Código inválido." no campo;
- `423`: o aviso de bloqueio.

#### Scenario: Novos códigos
- **WHEN** `recovery-codes` responde `200` com 10 códigos
- **THEN** o cartão mostra "Novos códigos gerados." e os 10 códigos

#### Scenario: Código errado ao gerar
- **WHEN** `recovery-codes` responde `401` "Código inválido."
- **THEN** "Código inválido." aparece no campo, e a sessão continua

### Requirement: Bloqueio e falhas passageiras nos cartões
**O bloqueio:** o `423` de qualquer rota de Minha conta SHALL mostrar, no cartão da ação, "Muitas tentativas sem sucesso. Por segurança, a sua conta fica pausada por N minutos.":
- N é o `Retry-After` arredondado para cima, em minutos, com o singular para 1;
- sem `Retry-After` legível, "alguns minutos";
- o botão da ação SHALL ficar desabilitado até o fim do `Retry-After`.

**Os estados que o quadro não desenha** SHALL aparecer como aviso dentro do cartão da ação, com o botão da ação desabilitado quando houver `Retry-After`:
- `429`: "Muitas tentativas ao mesmo tempo. Tente de novo em instantes.";
- `503`: "O painel está iniciando. Tente de novo em alguns segundos.";
- falha de rede: "Não foi possível falar com o servidor.";
- `500`, em "Gerar novos códigos": o `title` do servidor ("Não foi possível conferir o código do app autenticador…");
- `409` ou `403` em "Gerar novos códigos": o cartão SHALL reler o `GET` e ir ao estado que ele indicar.

Em todas essas falhas, o que a pessoa digitou SHALL continuar nos campos, e a sessão SHALL continuar.

#### Scenario: Bloqueio com o tempo do servidor
- **WHEN** `POST /account/password` responde `423` com `Retry-After: 300`
- **THEN** aparece "Muitas tentativas sem sucesso. Por segurança, a sua conta fica pausada por 5 minutos.", e "Trocar senha" fica desabilitado até passar o tempo

#### Scenario: Servidor indisponível ao gerar códigos
- **WHEN** `POST /account/two-factor/recovery-codes` responde `503` com `Retry-After: 10`
- **THEN** o aviso "O painel está iniciando. Tente de novo em alguns segundos." aparece no cartão, e "Gerar códigos" fica desabilitado por 10 segundos

#### Scenario: Verificação redefinida no meio do caminho
- **WHEN** `POST /account/two-factor/recovery-codes` responde `409`, e o `GET` seguinte traz `ativa: false`
- **THEN** o cartão mostra o estado `nao-configurada`

#### Scenario: Chave do TOTP trocada ao gerar códigos
- **WHEN** `recovery-codes` responde `500` com o `title` "Não foi possível conferir o código do app autenticador. Use um código de recuperação ou fale com o administrador."
- **THEN** esse texto aparece no cartão, e o campo mantém o que foi digitado
