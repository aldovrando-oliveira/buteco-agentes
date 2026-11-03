**Issue:** #181

## Why

Com a #180, a #218 e a #219, existe um usuário local que entra, liga as 2 etapas e é gerido, mas só um: o primeiro Admin do bootstrap. Ninguém consegue pôr a segunda pessoa para dentro, nem devolver o acesso de quem esqueceu a senha. O Buteco não envia e-mail e não pode ter entrada automática, então os dois caminhos são links de uso único que o admin gera, copia e entrega pelo canal que quiser. As telas que os consomem (#183) dependem desta issue.

## What Changes

- **Convite** (grupo `/users`, para o usuário real do token; HMAC de operador → `403`):
  - convidar com nome e e-mail: o Buteco cria o usuário no estado novo **convidado**, sem senha, gera um link de uso único válido por 72 h e devolve o link **uma vez**;
  - gerar novo link, que invalida o anterior, inclusive de um convite vencido;
  - cancelar o convite, que invalida o link e libera o e-mail.
- **A senha de quem chama** (decisão do dono, 02/11/2026): criar o convite, gerar novo link do convite e gerar o link de redefinição pedem a senha atual de quem chama, no corpo. Errada → `401`, contada no bloqueio do e-mail de quem chama, como o `setup` das 2 etapas (#218). Um access token roubado não vira, sozinho, um usuário novo nem a senha de outra pessoa. Cancelar o convite não pede.
- **Redefinição de senha pelo admin** (`/users`): gera um link de uso único válido por 24 h, devolvido uma vez. A senha atual continua valendo até a nova ser criada. Gerar outro invalida o anterior.
- **Rotas públicas do link** (sob `/auth`, anônimas, com as barreiras de JSON e `Origin` do login):
  - consultar o link do convite e o da redefinição: nome, e-mail e validade, só para link válido;
  - aceitar o convite criando a senha pela política de 12 a 128 (#219), o que ativa o usuário;
  - criar a nova senha pelo link da redefinição, o que troca o `security_stamp` e encerra todas as sessões da pessoa, como a troca de senha da #219;
  - link inexistente, vencido, usado, cancelado ou substituído recebe a mesma resposta.
- **O link leva o token no fragmento** (`<painel>/convite#<token>`), que o navegador não envia ao servidor; o painel o manda no corpo de um `POST`. O token fica fora dos logs de acesso do nginx e do `apps/api`. **Diverge do protótipo** (`/convite/<token>`), e vira registro na #183 (convenção 17).
- **O token**: 256 bits aleatórios, guardado só como SHA-256, consumido por um `UPDATE` condicional (uso único atômico), com a base do link vinda da configuração (`Identity:PanelBaseUrl` ou `PublicUrl:BaseUrl`), nunca do `Host` da requisição.
- **O convidado na lista e no detalhe da #219**: estado `convidado`, validade do convite e "convidado por". A unicidade do e-mail entre usuários e convites pendentes sai do índice único que já existe.
- **O login recusa o convidado** com a resposta genérica e **o mesmo custo de Argon2** de um e-mail sem conta. Hoje um usuário sem senha responderia mais rápido e revelaria o convite pendente.
- **Reativar e desativar recusam o convidado** (`409`): hoje reativar um convidado o deixaria ativo sem senha.
- **Sem envio de e-mail.** O gatilho do P5 da #219 (exigir a senha atual para trocar o próprio e-mail) não dispara nesta change.

## Capabilities

### New Capabilities

- `user-access-links`: convite e redefinição de senha por link de uso único: gerar, gerar de novo, cancelar, consultar, aceitar e concluir; o token, sua validade e o que o invalida; a base da url.

### Modified Capabilities

- `local-users`: o estado convidado; o login de quem não tem senha custa o mesmo Argon2 e responde como e-mail sem conta.
- `user-management`: a lista e o detalhe mostram o convidado, a validade do convite e quem convidou; desativar e reativar recusam o convidado.
- `user-account`: trocar a própria senha invalida o link de redefinição pendente (P6, decisão do dono).

## Impact

- **`apps/api`:** rotas novas em `Users/` (convite e redefinição, administrativas e públicas), uma migration em `users` (quem convidou e o link pendente), o estado novo na conversão de `status`, a verificação fictícia no login sem senha, as recusas em desativar e reativar. Sem pacote novo e sem variável de configuração.
- **Contrato HTTP:** oito rotas novas; o `estado` da lista e do detalhe da #219 ganha o valor `convidado` e dois campos novos. Registro na #183.
- **`deploy/nginx.conf`:** sem mudança. `/auth` e `/users` já vão ao `apps/api`, e `/convite` e `/redefinir-senha` caem no fallback do SPA.
- **`apps/frontend`, `apps/inbox`, `apps/connectors`, `apps/workers`:** sem mudança de código. As telas são da #183.
- **Documentação:** `01` (Autenticação), `02`, `CHANGELOG.md`, `docs/architecture.md`.
