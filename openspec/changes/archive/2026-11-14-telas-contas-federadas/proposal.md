**Issue:** #279 (M3 · 4 de 4; depende da #190, no `main` em `9ec078c`, o merge do PR #280). É a 190b: as contas
que a Q1 do design da `telas-login-google` tirou da #190 (decisão do dono de 14/11/2026).

## Why

O backend da M3 serve conectar e desconectar a conta Google, as contas conectadas de cada usuário, a confirmação
de quem chama com o código do app e a lista dos provedores para o Admin (#189, #274, #276, #277). Nada disso tem
tela. Quem já tem usuário não conecta o Google, o Admin não vê nem desconecta a conta de outra pessoa, e **o Admin
que entrou só pelo Google (aceite de convite, #274) não confirma nenhuma ação administrativa no painel**: os seis
formulários só pedem a senha, e o backend, para quem não tem senha, só aceita o código.

## What Changes

- **A confirmação de quem chama com o código (Q3):** o `useCallerPassword` lê `formasDeEntrada` de `GET /account`
  e, sem `"senha"`, troca o campo pelo "Código do app autenticador" e envia `{ codigo }` em vez de `{ senha }`, nos
  seis formulários (convite, novo link, redefinição, promoção a Admin, criar aplicação, gerar segredo) e no
  conectar. O código de recuperação não vale ali.
- **Minha conta, "Formas de entrada":** o card com a senha e as contas conectadas; "Conectar Google" com a
  confirmação no card e o redirecionamento; a volta `/account?conectado=` e `/account?recusa=` com os avisos, lidos e
  limpos por `replace`; o desconectar com a confirmação na linha, o `409`, o botão desabilitado na última forma
  (Q8) e o vínculo fora da configuração sem ação (Q9); para quem não tem senha, o card de senha dá lugar ao texto
  que pede o link a um administrador (Q10).
- **Editar usuário:** o `SignInFormsCard` com as contas conectadas de `GET /users/{id}`; o desconectar pelo Admin
  na linha, com as sessões da pessoa encerradas; "Gerar link de senha" para quem não tem senha; o próprio com o
  link para Minha conta; os textos da M3 na verificação e no rodapé.
- **Usuários:** o chip `google` vira "Google", no tom da "Senha"; os textos da M3 nos dois cards de baixo.
- **Identidade:** o card "Provedores federados", só leitura, de `GET /identity/federated-providers`, com Provedor,
  Usado para, Issuer, ID do cliente, URI de retorno e Vínculos, sem "Chaves" (Q11), e os estados vazio, carregando e
  erro.
- **Nada no backend.** Dado que a tela precisar e o backend não servir é achado (convenção 1). A volta do conectar que cai no
  `/entrar` (sem provedor configurado ou com o state ilegível) é a **#281**, de backend, que não bloqueia esta change.

## Capabilities

### New Capabilities

- `panel-federated-accounts`: as formas de entrada em Minha conta (conectar, a volta, desconectar) e em Editar
  usuário (o desconectar pelo Admin).

### Modified Capabilities

- `panel-account-page`: o card de senha de quem não tem senha; o subtítulo da M3.
- `panel-users-page`: a confirmação com o código no convite e no novo link; o chip "Google"; os textos da M3.
- `panel-user-detail-page`: a confirmação com o código na redefinição e na promoção; as formas de entrada com as
  contas conectadas; os textos da M3 na verificação e no e-mail.
- `panel-external-applications`: a confirmação com o código ao criar a aplicação e ao gerar o segredo.
- `panel-identity-page`: o card "Provedores federados".

## Impact

- **Código:** só o `apps/frontend` (`features/account`, `features/users`, `features/applications`, nos dois
  formulários com a confirmação, e `features/identity`).
- **Suítes:** `apps/frontend` (o `suite-279.sh`, cópia do `suite-190.sh`). Nenhuma rodada agora.
- **Protótipo:** a validação visual na homologação, contra os quadros `MinhaConta`, `UsuarioDetalhe`, `Usuarios` e
  `Provedores` com `entrega=m3`, na versão 82 (id de referência `1794678488-1a7d`).
