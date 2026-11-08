## Context

É a segunda parte do passo C, dividido pelo dono em 07/11/2026 (Q1, opção A). A #250 (PR #252, mergeado em `9c49e4b`) entregou:
- a lista de Usuários, com os convites;
- a Identidade;
- a seção "Acesso";
- os avisos de falha e as datas num lugar comum.

Esta change entrega Editar usuário, o "Editar" na linha da lista e o link de Minha conta. Ela parte da [parte do design que ficou com a #183](https://github.com/aldovrando-oliveira/buteco-agentes/issues/183#issuecomment-6710514538), escrita na exploração da #250. Este design a confere contra o código e o quadro, e a completa.

**Fontes lidas para este design (07/11/2026):**
- **#183:** o corpo e todos os comentários, entre eles:
  - os contratos da #218, da #219, da #181 e da #211;
  - o inventário e as três releituras do passo C;
  - as entregas da #245, da #246 e da #250;
  - a parte do design que ficou com a #183 (D2, D6 a D9, D11, os guardas e o tamanho).
- **Designs arquivados:**
  - `2026-11-07-usuarios-convites-identidade-painel` (#250):
    - D1: `failureNotice`, `useBlockedUntil` e `panelDates` no lugar comum;
    - D3 e D4: o cartão como máquina de estados e a senha de quem chama;
    - D5: o link uma vez;
    - D8: a linha sem "Editar";
    - as decisões Q1 a Q10;
  - `2026-11-07-minha-conta-painel` (#246): D5 (o segredo fora do TanStack Query) e D10 (o `401` com problem+json devolvido à tela).
- **O código do `apps/api` em `9c49e4b`:**
  - `UserEndpoints.cs`, `UserResponses.cs` e `UserFields.cs`;
  - `PasswordResetEndpoints.cs`, `CallerPassword.cs` e `UserTwoFactorResetEndpoint.cs`;
  - `CurrentUser.cs` e `UserPrincipalFactory.cs`.
- **O código do `apps/frontend` em `9c49e4b`:**
  - `features/users/`, `features/account/components/AccountDataCard.tsx`;
  - `auth/sessionStore.ts`, `auth/session.ts` e `auth/sessionFetch.ts`;
  - `utils/failureNotice.ts`, `utils/panelDates.ts`, `hooks/useBlockedUntil.ts` e `components/layout/DetailHeader.tsx`.
- **O nginx** (`apps/frontend/deploy/nginx.conf`): `users(/|$)` já cobre `/users/<id>`, com o desvio de navegação.
- **O protótipo:** o canvas "Protótipo de autenticação federada" (`H7dPBZafiSkdKd1hqesPZ6`), lido pelo Artifact tool na versão **`1794066606-fba2`**. É a mesma versão da #250 e do registro da #183. Foram lidos:
  - `UsuarioDetalhe`, com todas as propriedades: `entrega`, `alvo` (6 valores), `verificacao` (2) e `acao` (11);
  - `Usuarios`, a célula Ações;
  - `MinhaConta`, o rodapé do cartão Dados;
  - `canvas.json`, cujas notas não trazem decisão para estes quadros.

**O que a conferência acrescentou ao registro de 07/11:**
- **A redefinição da verificação também encerra as sessões da pessoa** (`UserTwoFactorResetEndpoint`: `sessions.EndAllAsync`). Depois do `204`, o detalhe é relido: o selo e a contagem de sessões mudam juntos.
- **`POST /users/{id}/sessions/revoke` não recusa o próprio usuário.** O quadro não mostra o botão para ele, e a tela também não. Aqui o servidor não é guarda, e não precisa ser: é a própria sessão.
- **O `PUT /users/{id}` valida os campos antes de procurar o usuário.** Um `400` pode chegar antes do `404`.
- **Qualquer ação pode receber o `403` de `CurrentUser`**, "Esta rota exige o login de um usuário ativo.", se quem chama for desativado no meio. Ele vai no aviso, pelo `title`.
- **O painel do link de redefinição tem outra forma** que o do convite: sem e-mail, sem ícone, sem o aviso amarelo e com o texto num parágrafo só (Q11).
- **Para o próprio, "N sessões ativas, incluindo esta"** é inferência, e não campo. Pela Q12, a tela não a escreve.

**Estado do código (`9c49e4b`):**
- `/users/:id` não existe. A linha da lista não tem "Editar", e o guarda "a lista não tem link para o detalhe (/users/:id chega com a #183)" (`UsersPage.test.tsx`) impede o link sem destino;
- `useCallerPassword` e `CallerPasswordField` servem convidar e gerar novo link. "Redefinir senha" é o terceiro consumidor, na mesma feature;
- o `Session` não tem `sub`. O `decodeJwtPayload` é privado de `sessionStore.ts`;
- `AccountDataCard` mostra "use Usuários" sem link.

**Baseline do `apps/frontend`** (`~/.cache/buteco-agents/183-baseline/`, em `9c49e4b` com a árvore limpa, pelo `suite-183.sh` com o RabbitMQ de pé):
- 2248 de 2248 casos aprovados, em 177 arquivos, em 204 s (`APROVADA`);
- o relatório por nome está em `nomes.txt` (2248 linhas, nenhuma reprovada). Ele é **idêntico** ao da última rodada da #250 (`250-pos-rebase-r1/`), que é o mesmo código;
- a rodada começou às 23:26 de 07/11/2026, com load 2,10 (1 min) e 5,76 (5 min) e CPU em 100. A CPU caiu até 48, e pela regra assimétrica a rodada conta;
- o RabbitMQ ficou de pé (`PAUSAR_RABBIT=0`, `healthy`, o healthcheck TCP da #251), com `postgres` e `hopeful_williams` também no `podman ps`.

## Goals / Non-Goals

**Goals:**
- A rota `/users/:id` e todo estado do quadro `UsuarioDetalhe` (`entrega=m1`), cada um com a rota da API, os erros e os textos.
- O reconhecimento do próprio usuário pelo `sub`, sem mudar o formato do registro da sessão.
- A redefinição de senha com a senha de quem chama, com o `401` e o `423` sem deslogar, e o link mostrado uma vez.
- As confirmações de desativar, redefinir a verificação e encerrar as sessões. Reativar fica sem confirmação, como no quadro.
- O "Editar" na linha da lista e o link de Minha conta.

**Non-Goals:**
- **Papel, linhas Google/SSO, "Desconectar", "Conectada em" e "Último uso" por forma** (M2, M3). O quadro já os esconde com `entrega=m1`.
- **Mudar rota ou contrato do `apps/api`.** Nenhum campo do quadro ficou sem fonte que exija rota nova.
- **Guarda de rascunho ao sair da página** (o `useUnsavedChangesGuard`). Ele abre um diálogo modal, que o painel não usa em gestão, e o quadro não o desenha (Q14).
- **Extrair um cliente HTTP comum** (convenção 2).

## Decisions

### D1. Rota, entrada pela lista e por Minha conta

**A rota** `users/:id` → `UserDetailPage`, dentro do `ProtectedRoute` e da casca, ao lado de `users`.
- O item "Usuários" do menu fica ativo, pelo `startsWith` que a casca já usa.
- **O `nginx.conf` não muda:** `^/(…|users|…)(/|$)` já cobre `/users/<id>`. A navegação abre a página, e o `fetch` vai à API.

**O "Editar" na linha** (`UsersTable`, coluna Ações):
- é um link do router com a cara do `btn-row` do quadro (botão `default`, pequeno), com o rótulo "Editar" e o `aria-label` "Editar {nome}";
- aparece na linha do **ativo** e na do **desativado**. O convidado continua com "Gerar novo link" e "Cancelar convite" (`Usuarios`: `ativo: !u.pendente`);
- leva a `/users/{id}`, com o id codificado por `encodeURIComponent`.

**O guarda da #250 é trocado.** O caso "a lista não tem link para o detalhe (/users/:id chega com a #183)" sai, e entram dois:
- "a linha do ativo e a do desativado têm Editar para /users/{id}";
- "a linha do convidado não tem Editar".

A troca tira o caso antigo, e o requisito "Ações da linha sem destino inexistente" da `panel-users-page` muda na mesma change. Não é afrouxar guarda: o destino passa a existir.

**Minha conta** (D11 do registro): o texto continua "Para mudar o nome ou o e-mail, use Usuários.", e "Usuários" vira link do router para `/users/{id}`, com o `id` do `GET /account`, que o cartão já tem. É o `href="UsuarioDetalhe.dc.html"` do quadro `MinhaConta`. O registro de 07/11 na #246 falava em link "para a lista", e o quadro leva ao detalhe.

### D2. O próprio usuário (`alvo=proprio`)

Igual ao D2 do registro, conferido em `9c49e4b`:
- **A fonte é o `sub` do id token.** `UserPrincipalFactory` põe `sub = user.Id` no id token e no access token. O id é imutável, então o `sub` não envelhece, ao contrário do nome e do e-mail.
- **`currentUserId(session)`** lê o `sub` do `idToken` na hora, ao lado do `useSession`. O `decodeJwtPayload` de `sessionStore.ts` passa a ser exportado. **O `Session` não muda:** um `sub` obrigatório faria o `parseSession` recusar os registros gravados antes do deploy, e todo mundo seria deslogado.
- **A comparação ignora a caixa.** O `sub` é o `Guid.ToString()`, em minúsculas, e o `{id:guid}` do `apps/api` aceita qualquer caixa.
- **Sem `sub` legível, o usuário é tratado como outro.** O servidor é a guarda real: as três ações vetadas a si mesmo respondem `403` com `title`, e a tela mostra esse texto no aviso do cartão:

  | ação | `title` do `403` |
  |---|---|
  | desativar | "Você não pode desativar o próprio usuário." |
  | redefinir senha | "Use Minha conta para trocar a própria senha." |
  | redefinir verificação | "Para remover a sua verificação, use Minha conta, com um código." |

- **Trocar de conta em outra aba** regrava o registro. A mudança chega pelo `useSession`, e a página recalcula `proprio`.

### D3. A carga e o cabeçalho

`GET /users/{id}` → o resumo + `desativadoEm`, `convidadoPor: { id, nome } | null` e `sessoes: { ativas, ultimaAtividade }`. É uma consulta do TanStack Query com a chave `['users', id]`. A resposta não tem segredo.

| quadro | quando | a tela |
|---|---|---|
| `alvo=carregando` | o `GET` em voo | "Carregando o usuário…" (`role="status"`) |
| `alvo=erro` | `503`, rede, outro | o cartão "Não foi possível carregar o usuário agora." com "Tentar de novo" (`role="alert"`), depois do `retry` do QueryClient do painel |
| `alvo=inexistente` | `404`, inclusive o id que não é GUID (a rota do `apps/api` não casa) | "Usuário não encontrado", "O link pode estar errado, ou o convite foi cancelado." e "Voltar para Usuários" |
| (sem quadro) | `estado: convidado` | `Navigate` para `/users` com `replace` (Q4) |
| `alvo=outro` / `desativado` / `proprio` | `200` | o cabeçalho e os cartões |

- **O `404` não é repetido.** O `retry` padrão do painel (`app/queryClient.ts`, `queryRetry`) repete **todo** erro de consulta três vezes, e só o `503` espera o `Retry-After`. Com ele, o `404` ficaria uns 7 s em "Carregando o usuário…", pela espera exponencial, antes de "Usuário não encontrado". A consulta do detalhe declara um `retry` próprio: recusa o `404` (pelo `isApiFailure`) e segue o `queryRetry` no resto. O `503` e a rede continuam com as três tentativas. Guarda: "id inexistente faz um GET só".
- **Um `404` numa ação** (o usuário apagado no meio) leva a página a `inexistente`, pela invalidação do detalhe.
- **`estado` desconhecido** (convenção 13): o selo neutro com o valor cru, e a página como a do `outro`. O servidor decide o que cada ação aceita.

**O cabeçalho** é o `DetailHeader`, com volta para "Usuários":
- o título é o `nome`;
- o `status` reúne o selo do estado ("Ativo" verde, "Desativado" cinza) e, para o próprio, o selo "Você" (cinza);
- as `actions`: "Desativar" (perigo, contornado, só `outro` ativo) ou "Reativar" (primário, só `desativado`);
- a `description` é o subtítulo, que junta duas partes com " · ":

| parte | fonte | texto |
|---|---|---|
| a origem | `convidadoPor` com `criadoEm` | "Convidado por {convidadoPor.nome} em {dd/mm/aaaa}" |
| | `convidadoPor` nulo (o primeiro Admin, #181) | "Primeiro administrador, criado na instalação" |
| o estado | `desativado`, com `desativadoEm` | "desativado em {dd/mm/aaaa}" |
| | outro estado, com `ultimoAcesso` | "último acesso {formatRelative}" (Q3: "hoje, 14:15", não "agora") |
| | `ultimoAcesso` nulo | a parte some |

- **O "Primeiro administrador"** é o texto do quadro para `convidadoPor: null`. Na M1 não há papel, e o único usuário sem convite é o do bootstrap (#181). A #184 revê o texto quando houver papéis.
- **Os avisos da página** ficam entre o cabeçalho e os cartões:
  - a confirmação de desativar (D8);
  - "Reativação concluída…" (D8);
  - a nota azul do próprio.

**O próprio** (`alvo=proprio`):
- a nota azul "Este é você. A sua senha, a sua verificação em 2 etapas e as suas sessões ficam em Minha conta (link para `/account`)." (`role="note"`);
- sem "Desativar";
- a linha Senha com o link "Trocar em Minha conta";
- a verificação com "Gerencie a sua em Minha conta.", sem botão;
- as sessões com "{N} sessões ativas", "1 sessão ativa" ou "Nenhuma sessão ativa", sem "incluindo esta" (Q12), e o link "Ver em Minha conta";
- **os dados continuam editáveis**, como no quadro: o `PUT` aceita o próprio, e é por aqui que a pessoa muda o próprio nome e e-mail na M1.

### D4. Uma ação aberta por vez

O quadro tem uma só propriedade `acao`, e a tela segue isso. A página guarda **uma** ação aberta:

```
            ┌─────────── Cancelar / Concluir / abrir outra ──────────────┐
            ▼                                                            │
 nenhuma ──"Desativar"────────▶ desativar ──200──▶ nenhuma (página do desativado)
    │──"Redefinir senha"──────▶ redefinir-senha ──200──▶ link-gerado ──Concluir──▶ nenhuma
    │──"Redefinir verificação"▶ redefinir-2fa ──204/409──▶ nenhuma (detalhe relido)
    │──"Encerrar todas…"──────▶ encerrar-sessoes ──200──▶ sessoes-encerradas (aviso)
    └──"Reativar" (direto) ──200──▶ reativado (aviso)
```

- **Abrir uma ação fecha a anterior.** O link gerado, a senha digitada e as confirmações somem nessa hora.
- **Os avisos de sucesso** ("N sessões encerradas.", "Reativação concluída…") ficam até a próxima ação.
- **O cartão Dados é independente.** O rascunho dele não é uma ação, e abrir uma ação não o descarta: o quadro mostra os dois juntos (`sujo` e `acao` são estados separados).
- **Enquanto um pedido está em voo,** o botão dele mostra o carregando, e "Cancelar" fica desabilitado, como nos formulários da #250.
- **Os erros sem quadro** de cada ação aparecem no aviso do próprio cartão, pelo `failureNotice`: `403`, `409`, `429`, `503`, rede e o `title` de qualquer outro. A confirmação continua aberta para tentar de novo, exceto onde a tabela da ação diz outra coisa.

### D5. Dados

`PUT /users/{id}` com `{ nome, email }`. Os valores vêm do detalhe, e o formulário recomeça a cada leitura nova, quando não está sujo.

| estado | quando | a tela |
|---|---|---|
| limpo | início | "Salvar alterações" desabilitado |
| sujo | um campo difere do lido | "Salvar alterações" e "Descartar", que volta aos valores lidos |
| (salvo) | `200` detalhe | "Alterações salvas." ao lado do botão, até a próxima edição. O cache do detalhe recebe a resposta, e a lista é invalidada. Se é o próprio, a conta (`['account']`) também |
| `acao=email-em-uso` | `409` | "Já existe um usuário com este e-mail." no campo E-mail, com `aria-invalid` |
| — | `400` | o `errors.nome` e o `errors.email` do servidor nos campos |
| — | `404` | a página vai a `inexistente` |
| — | `403`, `429`, `503`, rede | o `failureNotice` no cartão |

- **Sem validação no navegador** além do `type="email"`. O servidor faz o `trim` e a conferência ("O nome precisa ter de 1 a 200 caracteres.", "Informe um e-mail válido."), como no convidar da #250.
- **A ajuda do e-mail** é "É o e-mail de entrada no painel." (versão `m1`).
- **"Sujo" compara com o lido,** e não com "houve digitação". Voltar o texto ao original desabilita o botão. O quadro marca sujo em qualquer `onChange`, e a comparação evita um `PUT` sem mudança.
- **A chamada vai pelo `useMutation`.** A resposta não tem segredo, ao contrário do link (D7).
- **O próprio** continua vendo o nome e o e-mail antigos no rodapé da casca até a renovação seguinte, em até 15 min (#219; Q13). Depois do `200`, a tela invalida o detalhe, a lista e a conta, e não força renovação.

### D6. Formas de entrada e "Redefinir senha"

**O cartão** tem só a linha "Senha", quando `formasDeEntrada` traz `senha`:
- a identificação é o `email`, monoespaçado;
- a coluna Ações depende do alvo:
  - `outro`: "Redefinir senha";
  - `proprio`: o link "Trocar em Minha conta";
  - `desativado`: "Reative para gerar um link";
- o rodapé é "Por enquanto, a senha é a única forma de entrada.".

Uma forma desconhecida vira linha com o valor cru, sem ação (convenção 13). Sem `senha` na lista (não acontece num ativo), a tabela fica sem linhas, sem texto inventado.

**Redefinir senha** (`POST /users/{id}/password-reset` com `{ senha }`):

| estado do quadro | quando | a tela |
|---|---|---|
| `redefinir-senha` | "Redefinir senha" | dentro do cartão, abaixo da tabela: "Gerar link para {nome} criar uma nova senha", "A senha atual continua valendo até a nova ser criada. Quando isso acontecer, a pessoa sai de todos os navegadores.", "Sua senha" (ajuda: "Para gerar o link, confirme que é você."), "Gerar link" e "Cancelar" |
| (sem quadro) | "Sua senha" vazia | "Informe a sua senha." no campo, sem enviar (Q6) |
| `senha-errada` | `401` "Senha inválida." | o `title` no campo, com `aria-invalid`; a senha é limpa |
| `senha-bloqueada` | `423` + `Retry-After` | o aviso amarelo acima do campo, com o N do `Retry-After` (`lockedText`). "Gerar link" fica desabilitado até passar |
| `link-gerado` | `200 { link, expiraEm }` | o painel do link (D7) |
| (sem quadro) | `403` | o `title` ("Use Minha conta para trocar a própria senha.") no aviso. Acontece quando o reconhecimento do D2 falhou |
| (sem quadro) | `409` "Usuário desativado." ou "Este usuário ainda não aceitou o convite." | o `title` no aviso, e o detalhe é relido. Alguém desativou a pessoa no meio |
| (sem quadro) | `404` | a página vai a `inexistente` |
| (sem quadro) | `429`, `503`, rede | o `failureNotice` no aviso |

- **O formulário é o `useCallerPassword` com o `CallerPasswordField`**, o mesmo da #250 (D4): o `401` com problem+json volta à tela pelo `sessionFetch`, sem renovar nem deslogar (#246 Q0).
- **A ordem do servidor** é: o bloqueio, o `403` a si mesmo, o estado do alvo, e só depois a senha. Com a conta pausada, nenhum outro erro aparece. O `403` e o `409` não contam no bloqueio.

### D7. O link de redefinição: uma vez, só na memória

**O painel** segue o quadro (`link-gerado`, Q11):
- o título "Link para redefinir a senha";
- a caixa com o link monoespaçado e "Copiar link", que troca o rótulo por "Copiado ✓" (o `CopyButton` do Mantine);
- o texto **"Envie o link à pessoa pelo canal que preferir. Vale até {expiraEm} e funciona uma única vez. O Buteco não mostra este link de novo; se ele se perder, gere outro, e o anterior deixa de funcionar."**. É o texto do quadro com a troca da Q5, e `expiraEm` sai como `dd/mm/aaaa às HH:MM` (`formatLinkExpiry`, o mesmo do convite);
- "Concluir".

**Fora dos caches**, pela mesma regra da #250 (D5):
- **a chamada é direta, sem `useMutation`**, e o `link` e o `expiraEm` ficam só no estado da página;
- **o cache não recebe a resposta.** Depois do `200`, a tela invalida o detalhe. O `GET` não traz nada do link;
- **nada em `sessionStorage`, `localStorage`, URL, `history.state` ou cache.** O token vai dentro do link, no fragmento (`/redefinir-senha#…`), e a tela não o separa.

**O link some:**
- no "Concluir";
- ao abrir outra ação (D4);
- ao sair da página (desmontagem).

Recarregar também o perde, e o texto do quadro já diz isso. Gerar outro link invalida o anterior no servidor (`AccessLinkHash` regravado).

### D8. Desativar e reativar

| estado do quadro | quando | a tela |
|---|---|---|
| `acao=desativar` | "Desativar" no cabeçalho | a confirmação acima dos cartões (`role="alert"`): "Desativar {nome}? A pessoa sai de todos os navegadores na hora e não consegue mais entrar. Você pode reativar depois, e ela vai precisar entrar de novo.", com "Desativar" (perigo) e "Cancelar" |
| `alvo=desativado` | `200` detalhe de `POST …/deactivate` | o cache recebe o detalhe, e a página vira a do desativado. O quadro não tem aviso de sucesso |
| `acao=reativado` | `200` de `POST …/activate`, sem confirmação | o cache recebe o detalhe, a página vira a do ativo, e aparece "Reativação concluída. {nome} pode entrar de novo com a senha que já tinha." (`role="status"`) |
| (sem quadro) | `403`, `409` | o `title` no aviso da confirmação (desativar) ou no topo (reativar) |
| (sem quadro) | `404` | `inexistente` |
| (sem quadro) | `429`, `503`, rede | o `failureNotice` no mesmo lugar |

- **Desativar e reativar são idempotentes** no servidor (`200` sem gravar nada). Um clique duplo ou outra aba não dão erro.
- **Os dois invalidam a lista** (`['users']`), cujo selo muda.
- **Desativar encerra as sessões** no servidor, e o detalhe que volta já traz `ativas: 0`.

### D9. Verificação em 2 etapas

`POST /users/{id}/two-factor/reset`, sem senha (o quadro não pede, e a rota não lê corpo).

| estado do quadro | quando | a tela |
|---|---|---|
| `verificacao=ativa` | `duasEtapas: true` | "Ativa" (verde), "Use Redefinir se a pessoa perdeu o celular e os códigos de recuperação." e "Redefinir verificação" |
| `verificacao=desligada` | `duasEtapas: false` | "Não configurada" (amarelo), "Cada pessoa liga a própria verificação em Minha conta." e o botão desabilitado |
| `acao=redefinir-2fa` | o clique | a confirmação no cartão: "Redefinir a verificação de {nome}? O app autenticador e os códigos de recuperação deixam de valer, e a pessoa sai de todos os navegadores. Ela pode ligar a verificação de novo em Minha conta.", com "Redefinir" (perigo) e "Cancelar" |
| (sem aviso) | `204` | o detalhe relido: o cartão vai a "Não configurada", e as sessões a "Nenhuma sessão ativa". O quadro não tem aviso de sucesso |
| (sem quadro) | `409` (já não configurada) | o detalhe relido, sem aviso. Outra aba redefiniu, ou a pessoa removeu |
| (sem quadro) | `403` | o `title` no aviso (D2) |
| (sem quadro) | `404` | `inexistente` |

- **O desativado com a verificação ativa** tem o botão habilitado, como no quadro: a rota aceita.
- **O próprio** não tem botão: "Gerencie a sua em Minha conta.".
- **O "desde …"** saiu do quadro (item 9 do inventário), e a tela não o mostra.

### D10. Sessões

`POST /users/{id}/sessions/revoke`, sem senha.

| estado | fonte | a tela |
|---|---|---|
| com sessões (`outro`) | `sessoes.ativas` ≥ 1 | "{N} sessões ativas · último uso {formatRelative(ultimaAtividade)}" (com 1, "1 sessão ativa · …"), a ajuda "Encerrar faz a pessoa precisar entrar de novo em todos os navegadores. Use se suspeitar de acesso indevido." e "Encerrar todas as sessões" |
| sem sessões | `ativas: 0` | "Nenhuma sessão ativa", com a mesma ajuda e **sem o botão**. É o que o quadro desenha no desativado e depois de encerrar |
| o próprio | `ativas` | "{N} sessões ativas", "1 sessão ativa" ou "Nenhuma sessão ativa", sem o último uso nem "incluindo esta" (Q12), a ajuda "As suas sessões aparecem em Minha conta, onde você pode encerrar cada uma." e "Ver em Minha conta" |
| `acao=encerrar-sessoes` | o clique | "Encerrar as {N} sessões de {nome}? A pessoa precisa entrar de novo em todos os navegadores." (com 1: "Encerrar a sessão de {nome}? A pessoa precisa entrar de novo."), com "Encerrar" (perigo) e "Cancelar" |
| `acao=sessoes-encerradas` | `200 { encerradas }` | "{N} sessões encerradas." (com 1, "1 sessão encerrada."; com 0, "Nenhuma sessão estava aberta.", no molde da #246 Q10), e o detalhe relido |
| (sem quadro) | `404` | `inexistente` |

- **`ultimaAtividade` nulo com sessões ativas** (não esperado) tira a parte " · último uso …", sem texto inventado.
- **Encerrar não pede senha.** Só tira acesso, como "Cancelar convite".

### D11. Onde mora o código

```
features/users/
  api/usersApi.ts               + UserDetail, getUser, updateUser, deactivateUser, activateUser,
                                  revokeUserSessions, requestPasswordReset, resetTwoFactor
  pages/UserDetailPage.tsx      a carga, o redirecionamento do convidado, o cabeçalho, os avisos da
                                  página e a ação aberta (D3, D4, D8)
  components/
    UserDataCard.tsx            Dados (D5)
    SignInFormsCard.tsx         Formas de entrada e o formulário "Redefinir senha" (D6)
    PasswordResetLinkPanel.tsx  o link uma vez (D7)
    CopyableLink.tsx            a caixa do link com "Copiar link", comum ao convite e à redefinição (Q11)
    UserTwoFactorCard.tsx       (D9)
    UserSessionsCard.tsx        (D10)
    UsersTable.tsx              + o "Editar" (D1)
    InviteLinkPanel.tsx         passa a usar o CopyableLink, sem mudar o que mostra (Q11)
  userDetailTexts.ts            o subtítulo, as contagens e os textos das confirmações
  queryKeys.ts                  + userQueryKey(id)
auth/
  sessionStore.ts               exporta decodeJwtPayload
  session.ts                    + currentUserId(session) e useCurrentUserId()
features/account/components/AccountDataCard.tsx   o link (D1)
app/routes.tsx                  + users/:id
```

- **O `CallerPasswordField` e o `useCallerPassword`** servem a redefinição sem mudança: é o terceiro consumidor, na mesma feature.

### D12. Testes e guardas

A suíte é a `frontend` (Vitest + Testing Library), com o `fakeUsersApi` da #250 estendido às rotas do detalhe. Cada guarda reintroduz só o defeito nomeado, é visto reprovar, e volta pela cópia, com o sha conferido (convenção 15). Os casos vão um por tarefa no `tasks.md`.

| defeito reintroduzido | o caso que reprova |
|---|---|
| o próprio reconhecido pelo e-mail ou pelo nome, e não pelo `sub` | "o próprio é o do sub, mesmo com o nome e o e-mail iguais aos de outro" |
| a comparação do `sub` sensível à caixa | "id da rota em maiúsculas ainda é o próprio" |
| `sub` obrigatório no `parseSession` | "registro gravado antes desta change continua válido" |
| "Desativar" visível para o próprio | "o próprio não tem Desativar" |
| "Encerrar todas as sessões" com 0 sessões | "sem sessões ativas, não há botão de encerrar" |
| "Redefinir verificação" habilitado com a verificação desligada | "com a verificação desligada, o botão fica desabilitado" |
| o link por `useMutation` | "o link não fica no MutationCache" |
| o link em storage | "o link não vai a storage" (lido por `length` e `key(i)`, afirmando que vê o `buteco.sessao`: a premissa que caiu na #250) |
| o link depois do "Concluir" ou de abrir outra ação | "Concluir tira o link" e "abrir outra ação tira o link" |
| o `401` "Senha inválida." da redefinição tratado como token | "senha errada na redefinição fica no campo, sem deslogar" |
| a senha vazia enviada | "senha vazia não chama a rota" |
| o convidado renderizado no detalhe | "o detalhe de um convidado volta para /users" |
| o `404` com a página de erro genérica | "id inexistente mostra Usuário não encontrado" |
| o `404` repetido pelo `retry` padrão | "id inexistente faz um GET só" |
| a redefinição da verificação sem reler as sessões | "depois de redefinir a verificação, as sessões vão a Nenhuma sessão ativa" |
| "Salvar" habilitado com os valores de volta ao original | "voltar ao valor lido desabilita Salvar" |
| "use Usuários" sem link ou para `/users` | "o link leva a /users/{id da conta}" |
| a linha do ativo sem "Editar" | "a linha do ativo e a do desativado têm Editar para /users/{id}" |
| o "Editar" no convidado | "a linha do convidado não tem Editar" |

O caso da #250 "a lista não tem link para o detalhe" sai (D1).

## Árvore de pastas proposta

```
apps/frontend/src/
├── app/
│   ├── routes.tsx                              (+ users/:id)
│   └── router.test.tsx                         (+ /users/:id exige sessão)
├── auth/
│   ├── sessionStore.ts                         (exporta decodeJwtPayload)
│   ├── sessionStore.test.ts                    (+ o registro antigo continua válido)
│   ├── session.ts                              (+ currentUserId, useCurrentUserId)
│   └── currentUserId.test.ts                   (novo)
└── features/
    ├── account/components/
    │   ├── AccountDataCard.tsx                 (o link, D1)
    │   └── AccountDataCard.test.tsx            (novo, ou o caso no AccountPage.test.tsx)
    └── users/
        ├── api/usersApi.ts, usersApi.test.ts   (+ as rotas do detalhe)
        ├── pages/
        │   ├── UserDetailPage.tsx              (novo)
        │   ├── UserDetailPage.test.tsx         (novo: carga, cabeçalho, próprio, desativar, reativar)
        │   └── UsersPage.test.tsx              (o guarda trocado, D1)
        ├── components/
        │   ├── UserDataCard.tsx, UserDataCard.test.tsx               (novos)
        │   ├── SignInFormsCard.tsx, SignInFormsCard.test.tsx         (novos)
        │   ├── PasswordResetLinkPanel.tsx, PasswordResetLinkPanel.test.tsx (novos)
        │   ├── CopyableLink.tsx                                      (novo)
        │   ├── UserTwoFactorCard.tsx, UserTwoFactorCard.test.tsx     (novos)
        │   ├── UserSessionsCard.tsx, UserSessionsCard.test.tsx       (novos)
        │   ├── UsersTable.tsx                                        (+ Editar)
        │   └── InviteLinkPanel.tsx                                   (usa o CopyableLink)
        ├── userDetailTexts.ts, userDetailTexts.test.ts               (novos)
        ├── queryKeys.ts                                              (+ userQueryKey)
        └── test/fakeUsersApi.ts, test/renderUserDetail.tsx           (o fake estendido; o render novo)
```

Nada em `libs/`. Nada fora do `apps/frontend`.

## Tamanho

**Âncora** (convenção 18): a #250, medida em 07/11/2026 pelo `git diff --numstat a852946 9c49e4b -- apps/frontend/src`, só código:
- **produção:** +1.327 / −40, em 26 arquivos;
- **testes:** +1.228 / −2, em 12 arquivos;
- **duplos:** +92, em 2 arquivos.

A #250 errou **+31% na produção** (projeção ~1.010), **+4% nos testes** (projeção ~1.180) e **−23% nos duplos** (projeção ~120).

**Projeção desta change**, por componente e com os duplos à parte:

| componente | produção | testes | duplos |
|---|---|---|---|
| `usersApi`: 7 chamadas e o `UserDetail` | ~60 | ~60 | — |
| `currentUserId`, o `sub` e o registro antigo (D2) | ~20 | ~60 | — |
| a página: carga, convidado, cabeçalho, subtítulo, próprio, ação aberta (D3, D4) | ~200 | ~240 | — |
| Dados (D5) | ~130 | ~170 | — |
| Formas de entrada, Redefinir senha, o link e o `CopyableLink` (D6, D7) | ~170 | ~230 | — |
| Verificação (D9) | ~80 | ~110 | — |
| Sessões (D10) | ~90 | ~120 | — |
| Desativar e reativar (D8) | ~60 | ~110 | — |
| os textos (`userDetailTexts`) | ~50 | ~60 | — |
| o "Editar", o guarda trocado e o link de Minha conta (D1) | ~25 | ~50 | — |
| o fake estendido e o render | — | — | ~70 |
| **total** | **~885** | **~1.210** | **~70** |

Com o erro de +31% da #250, a produção chega a ~1.160. Fica abaixo das duas últimas âncoras, cada uma revisada num PR só (#246: +1.516; #250: +1.327). **Não proponho divisão.** O ponto em que eu proporia, se a projeção passasse de ~1.500, seria separar Dados + o "Editar" + o link de Minha conta das ações (senha, verificação, sessões, desativar).

## Risks / Trade-offs

- **[O link na memória do navegador]** → Um XSS na origem lê o link, como lê os tokens da sessão (#217 D2). A defesa é a CSP, e o link não vai a storage, URL ou cache (D7, com os guardas).
- **[O reconhecimento do próprio falhar]** → A tela mostra ações que o servidor recusa, com `403` e `title` no aviso. O erro custa um aviso, nunca uma ação indevida (D2, com os guardas do `sub`).
- **[O rodapé com o nome antigo depois de editar o próprio]** → Até 15 min, até a renovação seguinte (#219). Aceito (Q13).
- **[O app autenticador com o e-mail antigo]** → Trocar o e-mail não muda o rótulo no app (#219), e os códigos continuam valendo. O quadro não fala disso, e a tela também não.
- **[A senha errada de quem chama pausa a conta inteira]** → É a regra do servidor (#181 P10), como no convidar. O `423` aparece no formulário, com o N do `Retry-After`. No desenvolvimento, o cabeçalho não fica legível, e o texto cai em "alguns minutos" (#246 D8).
- **[O rascunho de Dados perdido ao sair da página]** → Não há guarda (Q14). O formulário tem dois campos, e o quadro não desenha o aviso.
- **[Desativar a si mesmo pela API]** → O servidor recusa com `403`, e a tela não mostra o botão.
- **[Ação concorrente em outra aba]** → As ações são idempotentes ou respondem `409`/`404`. Nos dois casos, a tela relê o detalhe (D6, D8, D9).

## Migration Plan

- **Deploy:** só a imagem do `frontend`. As rotas estão em produção desde a #219, a #181 e a #218. Sem migration e sem variável nova.
- **Rollback:** a imagem anterior. `/users/:id` deixa de existir, a linha perde o "Editar", e "use Usuários" perde o link. Nada muda no servidor.

## Divergências do protótipo (convenção 17)

**De texto, já decididas** (Q3 e Q5 da #250):

| quadro | o quadro diz | a tela diz | decisão |
|---|---|---|---|
| `UsuarioDetalhe` `proprio`, subtítulo | "último acesso agora" | "último acesso hoje, 14:15" | Q3 |
| `UsuarioDetalhe` `link-gerado` | "Vale por 24 horas e uma única vez." | "Vale até {expiraEm} e funciona uma única vez." | Q5 |
| `UsuarioDetalhe` `senha-bloqueada` | "5 minutos" | o N do `Retry-After` | #217 |
| `UsuarioDetalhe` `proprio`, Sessões | "3 sessões ativas, incluindo esta" | "3 sessões ativas" (e "1 sessão ativa", "Nenhuma sessão ativa") | Q12 |

**Estados sem desenho, cada um com fonte no contrato** (a proposta está nos D3 a D10):
- "Sua senha" vazia (Q6);
- o convidado em `/users/:id` (Q4);
- `403`, `409`, `404`, `429`, `503` e rede nas ações;
- o `409` da verificação já redefinida;
- o `400` de Dados;
- "Nenhuma sessão estava aberta." (0 encerradas);
- os singulares: "1 sessão ativa", "Encerrar a sessão de {nome}?", "1 sessão encerrada.";
- o estado desconhecido;
- o singular e o zero das sessões do próprio (Q12).

**O que o quadro mostra sem campo na rota:**
- **"incluindo esta"**, nas sessões do próprio: o detalhe traz só `ativas` e `ultimaAtividade`, sem `atual`. Seria inferência, e falsa quando a sessão atual já foi revogada e o access token ainda vale (até 15 min). **A tela não a escreve** (Q12; ver a divergência abaixo);
- **"Primeiro administrador"**: o rótulo vem de `convidadoPor: null`, e não de um papel. Na M1, só o usuário do bootstrap não tem convite (#181);
- **"24 horas"** do link: a tela usa o `expiraEm` (Q5).

Nenhum outro texto ou número do quadro fica sem fonte.

## Validação visual

**Validação visual na homologação da M1**, como na #250 (decisão do dono, 08/11/2026). O dono valida quadro a quadro depois que todas as issues da entrega estiverem mergeadas, sem issue própria, e o PR é mergeado antes dela. O checklist vai igual no `tasks.md`, no `02` e no PR:

- `Usuarios` `entrega=m1`: o "Editar" na linha do ativo e na do desativado, e a ausência dele no convidado;
- `MinhaConta`: "Usuários" como link, levando ao detalhe do próprio;
- `UsuarioDetalhe` `entrega=m1`:
  - `alvo`: `outro`, `desativado`, `proprio`, `carregando`, `erro` e `inexistente`;
  - `verificacao`: `ativa` e `desligada`, em `outro` e em `desativado`;
  - `acao`: `nenhuma`, `email-em-uso`, `redefinir-senha`, `senha-errada`, `senha-bloqueada`, `link-gerado` (com "Copiado ✓"), `reativado`, `redefinir-2fa`, `encerrar-sessoes`, `sessoes-encerradas` e `desativar`;
  - os estados sem desenho da lista acima, conferidos contra o texto do design;
  - claro e escuro.

## Registros (convenção 23)

**No fechamento:**
- **na #183:** a entrega e a validação visual na homologação;
- **na #184:** que Editar usuário abre para todo usuário ativo na M1. O papel no cartão Dados ("Dados e papel"), "O novo papel vale…" e o "Primeiro administrador" voltam a ser revistos com ela;
- **na #194:** a M1 com 17 de 18 itens entregues.

## Decisões do dono (07/11/2026)

Na revisão do proposal e do design. Q1 a Q10 são da #250; Q4 e Q5 valem aqui.

| | pergunta | decisão | onde |
|---|---|---|---|
| Q11 | O painel do link de redefinição | **painel próprio, com a forma do quadro.** A caixa do link com "Copiar link" vira o `CopyableLink`, usado pelos dois painéis, sem mudar o que o painel do convite mostra | D7, D11 |
| Q12 | As sessões do próprio | **sem a dedução:** "{N} sessões ativas", "1 sessão ativa" e "Nenhuma sessão ativa", com "Ver em Minha conta". Motivo: "incluindo esta" pode ser falso quando a sessão atual já foi revogada e o access token ainda vale. Registrada como divergência do protótipo | D3, D10, Divergências |
| Q13 | O rodapé depois de editar o próprio | **o valor antigo por até 15 min**, como a #219 registrou. A tela invalida o detalhe, a lista e a conta, sem forçar renovação | D5, Risks |
| Q14 | O rascunho de Dados ao sair da página | **sem guarda** | Non-Goals, D4, Risks |

**A divergência de texto que estas decisões criam:** Q12, as sessões do próprio (listada em "Divergências do protótipo").

## Open Questions

Nenhuma.
