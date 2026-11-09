**Issue:** #185 (M2 · 3 de 6; depende da #183 e da #184, as duas em `main`). Acompanhamento: #195.

## Why

A #184 deu papéis ao backend (Admin, Operador e Leitor), mas o painel ainda mostra o menu inteiro e todos os botões a qualquer papel. Com isso, o Operador vê Usuários e Identidade e recebe `403` ao abrir, e o Leitor recebe `403` em toda escrita, no histórico de conversas e nos conectores, sempre com o erro genérico da tela. O "Convidar" do painel não envia o `papel`, que agora é obrigatório, e recebe `400`. Por isso a #184 não vai para produção sem esta change (decisão do dono, Q9 da #184).

## What Changes

**`apps/frontend`** (é o único app que muda):
- **Um mapeamento só para o papel.** O claim `role` do id token (`admin`/`operator`/`reader`) e o campo `papel` das respostas (`admin`/`operador`/`leitor`) passam pelo mesmo módulo. A tela lê o papel do id token, que a renovação já atualiza.
- **Menu e ações conforme o papel.** O que o papel não pode fazer não aparece:
  - a seção "Acesso" (Usuários e Identidade) é só do Admin;
  - o Leitor não vê nenhuma escrita, nem o histórico de conversas, nem os conectores ("Sincronizar agora", o seletor de pastas);
  - a URL de uma página sem acesso mostra a tela de acesso negado, sem chamar o servidor.
- **O `403` do servidor.**
  - A tela de acesso negado na carga de uma página, e um aviso na ação recusada, com texto que não afirma mais do que se sabe (convenção 13).
  - O `sessionFetch` renova uma vez num `403` para pegar o papel novo, e não repete a chamada.
  - A consulta não repete o `403`.
- **Usuários e Editar usuário com papel:**
  - o papel obrigatório no convite, com a descrição de cada papel;
  - a coluna Papel na lista;
  - o cartão "Dados e papel", com a senha de quem chama ao promover a Admin (`401`/`423` no molde do convite) e o `409` do último Admin;
  - o rebaixamento de si mesmo, com confirmação, que leva a Minha conta;
  - o subtítulo "Criado na instalação";
  - o texto do cartão "Senha e recuperação", velho desde a #220.
- **Rodapé e Minha conta.** A linha 2 do rodapé passa do e-mail ao papel. Minha conta ganha a linha Papel, e, para quem não é Admin, diz para pedir a um Admin a mudança do nome ou do e-mail. O texto de trocar de celular passa a "um Admin do painel".
- **Comentários e Purpose:** as premissas 4 a 7 e 10 do design se corrigem dentro da change.

**Documentação:**
- `docs/deployment.md`: as notas de deploy da #184 e da #185 juntas, e a volta atrás;
- `CHANGELOG.md` e `02-HISTORICO_E_STATUS.md`.

**Fora:**
- qualquer rota ou resposta do backend: a #184 já serve tudo, ver o design, item g;
- Aplicações externas (#188) e o card "Aplicações com acesso" (#187);
- os provedores federados (M3) e o SSO (M4).

**Deploy:** a #184 e a #185 vão juntas para produção (decisão do dono).

## Capabilities

### New Capabilities

- `panel-roles`: o papel da sessão no painel, com o mapeamento e a regra de esconder ou desabilitar, a tela de acesso negado e o `403` no `sessionFetch`.

### Modified Capabilities

- `frontend-app-shell`: a seção "Acesso" só para o Admin; a linha 2 do rodapé com o papel.
- `panel-issuer-session`: o `403` renova uma vez sem repetir; a consulta não repete o `403`.
- `panel-users-page`: o papel no convite, a coluna Papel, o texto de "Senha e recuperação" desde a #220, e o acesso só do Admin.
- `panel-user-detail-page`: o cartão "Dados e papel", o rebaixamento de si mesmo, o subtítulo "Criado na instalação" e o texto das 2 etapas do próprio.
- `panel-account-page`: a linha Papel e o texto do cartão Dados por papel, o texto do celular, e o aviso de papel novo.
- `inbox-channel-catalog-ui`: o Leitor abre o canal em Configuração, sem a aba Sessões.
- `knowledge-base-sync-source-ui`: o `403` do `apps/connectors` é o papel, e não configuração.

O inventário das outras telas de domínio fica em `panel-roles`, sem delta nas specs `*-ui` delas (design, "Revisão do dono").

## Impact

- **Contrato:** nenhum. O painel passa a enviar `papel` em `POST /users/invitations` e a chamar `PUT /users/{id}/role`, que já existem.
- **Suíte:** `apps/frontend`.
- **Protótipo:** versão 57 (`1794205908-2666`), `entrega=m2` em `Usuarios`, `UsuarioDetalhe` e `MinhaConta`, e o quadro `AcessoNegado`. A validação visual fica para a homologação, pelo checklist do `tasks.md`.
- **Testes:** o setup passa a gravar uma sessão de Admin por padrão (design, D11).
