## Context

Segundo passo da divisão da #183 (decisão do dono, 05/11/2026): A é a entrada pública (#245, mergeada), **B é Minha conta (esta change)**, C são Usuários, Editar usuário e Identidade (#183). A #246 depende da #245, que entregou o `PasswordField`, a política da senha e o passo do código no `/entrar`.

**Fontes lidas para este design:**
- **#246:** o corpo e os três comentários. Pelo [de 05/11](https://github.com/aldovrando-oliveira/buteco-agentes/issues/246#issuecomment-6650024453), nenhuma divergência do protótipo resta. Pelo [de entrega da #245](https://github.com/aldovrando-oliveira/buteco-agentes/issues/246#issuecomment-6700633258), o aviso "Senha alterada." vem para cá, leitura e gravação (Q3 da #245).
- **Comentários de contrato na #183:**
  - da #218, as rotas de `/account/two-factor` ([comentário](https://github.com/aldovrando-oliveira/buteco-agentes/issues/183#issuecomment-6565822709));
  - da #219, `GET /account`, `POST /account/password` e as sessões ([comentário](https://github.com/aldovrando-oliveira/buteco-agentes/issues/183#issuecomment-6567559145));
  - da #217, o rodapé da casca ([comentário](https://github.com/aldovrando-oliveira/buteco-agentes/issues/183#issuecomment-6565065901)).
- **Designs arquivados:**
  - `2026-11-01-painel-entra-pelo-emissor` (#217): D3, o lock; D5, o `401`; D7, o "Sair";
  - `2026-11-07-entrada-publica-painel` (#245): `sessionApi`, `PasswordField`, `entryNotices`, o `/entrar`.
- **O código do `apps/api`**, conferido contra os comentários:
  - `AccountEndpoints.cs` e `AccountSessionEndpoints.cs`;
  - `AccountTwoFactorEndpoints.cs`;
  - `UserSessions.cs`, que ordena por `CreationDate` decrescente;
  - `EndSessionEndpoint.cs`.
- **O protótipo:** o canvas "Protótipo de autenticação federada" (`H7dPBZafiSkdKd1hqesPZ6`), lido em 06/11/2026 pelo Artifact tool, nos quadros `MinhaConta` e `Main` e no rodapé da casca.
  - **O canvas está na versão `1794015615-ba72`, mais nova que a `1793853618-1811` citada no pedido.** Não há cópia da versão anterior para comparar. Todos os estados que a #246 lista existem na atual, e nada do quadro diverge do contrato (ver "Divergências").

**Estado do código (`8884d89`):**
- não há `/account` no painel;
- o rodapé da casca (`AppShell.tsx` l. 114–176) mostra o avatar, o nome e o e-mail do id token, o tema e o "Sair";
- o `logout` (`src/auth/logout.ts`) pede o lock, lê o `idToken`, apaga o registro e navega ao `/connect/logout`;
- o `sessionFetch` (`src/auth/sessionFetch.ts`) renova e repete em todo `401` (D5 da #217).

### Achado da exploração: o `401` de senha ou código errado desloga (premissa falsa, decidido na Q0)

As rotas de Minha conta respondem `401` também quando a **senha** ou o **código** estão errados:
- "Senha atual inválida." no `POST /account/password`;
- "Senha inválida." no `setup`;
- "Código inválido." no `confirm`, no `recovery-codes` e no `remove`.

O `sessionFetch` trata todo `401` como token recusado. Ele renova, **repete o `POST` com a mesma senha errada** e, no segundo `401`, **encerra a sessão** e leva a `/entrar?motivo=sessao-expirada`. Uma senha digitada errado em Minha conta tiraria a pessoa do painel e contaria **duas** falhas no bloqueio do e-mail.

**Conferido contra o servidor real** (o `apps/api` deste worktree, 06/11/2026, `curl` com `Origin: http://localhost:5173`):

| caso | status | `Content-Type` | `WWW-Authenticate` |
|---|---|---|---|
| token inválido | `401` | — (sem corpo) | `Bearer error="invalid_token"` |
| senha atual errada | `401` | `application/problem+json` | — |

- O `WWW-Authenticate` **não fica legível pelo navegador no desenvolvimento**, que é cross-origin (`5173` → `5017`): não há `Access-Control-Expose-Headers`.
- O `Content-Type` fica legível nos dois ambientes: é um cabeçalho da lista segura do CORS. Em produção a origem é a mesma (`VITE_API_BASE_URL: ""`), e qualquer cabeçalho é legível.

O mesmo vale para as três ações administrativas da #183 (passo C) que pedem a senha de quem chama (convite, novo link, redefinição; #181 D2). A correção serve às duas.

**Decidido (Q0, 06/11/2026):** o `sessionFetch` passa a devolver à tela, sem renovar nem repetir, o `401` com `Content-Type: application/problem+json`. Esse `401` é a recusa da própria rota. O `401` sem corpo continua renovando e repetindo, como hoje.
- **Muda um requisito da `panel-issuer-session`** ("401 renova uma vez antes de encerrar a sessão"). Por isso foi pergunta, e o dono aprovou.
- **Complemento do dono:** em cada um dos três apps (api, inbox, connectors), um teste fixa que o `401` de token ausente ou inválido **não** traz corpo problem+json: `TokenRejection_401_HasNoProblemBody_ThePanelRenewsOnThis`. A #247 passa a cobrir também estas recusas, para o painel decidir pelo campo de tipo ([comentário](https://github.com/aldovrando-oliveira/buteco-agentes/issues/247#issuecomment-6701299874)).
- **A alternativa no `apps/api`** seria trocar o status dessas recusas, por exemplo para `400` ou `422`. Ela muda contrato publicado (#218, #219, #181) e os testes de lá, e a #183 já foi escrita sobre ele.

## Goals / Non-Goals

**Goals:**
- **A página `/account`**, com os quatro cartões do quadro `MinhaConta` (`entrega=m1`) e todos os estados das propriedades `verificacao`, `senha` e `sessoes`.
- **A troca de senha** seguida do logout do emissor, e o aviso `senha-alterada` no `/entrar`.
- **O cadastro das 2 etapas:**
  - o QR desenhado no navegador pelo `qrcode.react`;
  - o segredo com "Copiar";
  - os 10 códigos com "Copiar todos" e "Baixar arquivo .txt";
  - o segredo e os códigos só na memória da tela.
- **As sessões:** "Esta sessão", encerrar uma, encerrar as outras com confirmação, e encerrar a atual levando ao `/entrar`.
- **O rodapé da casca:** o nome e o e-mail viram o link para Minha conta.
- **As notas de deploy** para o dono abrir o cadastro em produção.

**Non-Goals:**
- Editar o próprio nome ou e-mail. Na M1 o quadro aponta para Usuários (decisão da #246).
- Usuários, Editar usuário, Identidade e a seção "Acesso" do menu: são da #183 (passo C).
- O papel na segunda linha do rodapé (#184).
- IP e navegador nas sessões. A fonte não existe (#221).
- Mudar rota ou contrato do `apps/api`.
- Remover a opção do cadastro (#220).
- Extrair um cliente HTTP compartilhado: a feature tem a sua cópia do `request<T>`, como as outras (convenção 2).

## Decisions

### D1. A rota `/account` e a entrada pelo rodapé

- **A rota:** `account`, dentro do `ProtectedRoute` e da casca, como as outras páginas. O nome em inglês segue o padrão das rotas do painel (`agents`, `channels`), e o `nginx.conf` já trata `/account` com o desvio de navegação (inventário da #183).
- **Nenhum item novo no menu** (decisão de navegação da #183). A entrada é o bloco do nome no rodapé, como no protótipo: `<a href="MinhaConta" title="Minha conta" aria-current="page">`.
- **O rodapé** (`frontend-app-shell`, requisito alterado):
  - o nome e a segunda linha (o e-mail; a #184 troca pelo papel) ficam dentro de um link do router para `/account`, com `title` "Minha conta";
  - o link leva `aria-current="page"` quando a rota atual começa com `/account`;
  - o avatar continua fora do link e decorativo, como no quadro;
  - o tema e o "Sair" continuam botões à parte, fora do link;
  - o link não muda a aparência do bloco: sem sublinhado e sem cor de link, com o hover da casca e o foco visível. A forma exata vai para a validação visual.
- **O que a página mostra antes dos dados:** um estado de carregamento por cartão. Um erro no `GET` mostra o aviso do cartão com "Tentar de novo" (ver D8, estados sem quadro).

### D2. Os cartões, a API e os estados

As rotas são as do `apps/api` em `8884d89`, conferidas no código. Todas são autenticadas pelo token do emissor, pela cópia do `request<T>` da feature (`features/account/api/accountApi.ts`, sobre o `sessionFetch`). `CurrentUser` responde `403` a token sem usuário real; para quem está logado no painel, isso não acontece.

**Dados** (`GET /account` → `{ id, nome, email, criadoEm, ultimoAcesso, duasEtapas }`):

| quadro | fonte |
|---|---|
| Nome | `nome` |
| E-mail | `email` |
| No painel desde | `criadoEm`, só a data (`dd/mm/aaaa`) |
| Último acesso | `ultimoAcesso`, pelo formatador relativo (D9) |
| "Para mudar o nome ou o e-mail, use Usuários." | texto fixo, sem link até a #183 (Q3) |

O cartão lê o `GET /account`, e não o id token. Depois de um admin editar o nome, o id token só muda na renovação seguinte (#219).

**Senha** (`POST /account/password` com `{ senhaAtual, novaSenha }`):

| estado do quadro | quando | o que a tela faz |
|---|---|---|
| `fechada` | início | "Você entra com `<e-mail>` e a sua senha." e o botão "Trocar senha" |
| `aberta` | "Trocar senha" | os três campos (`PasswordField`), o aviso de que a pessoa sai de todos os navegadores, "Trocar senha" e "Cancelar" |
| `senha-fora-da-regra` | a nova fora de 12 a 128 caracteres (`passwordFitsPolicy`) | erro no campo "Nova senha", sem envio |
| `senhas-diferentes` | a repetição diferente | erro em "Repita a nova senha", sem envio |
| `atual-errada` | `401` "Senha atual inválida." | erro no campo "Senha atual", com `aria-invalid` (D10) |
| `bloqueada` | `423` + `Retry-After` | o aviso do quadro, com o N do `Retry-After` (D8) |
| — | `400` (a política, se a validação local for contornada) | o `title` no campo "Nova senha" |
| — | `204` | D3 |

**Verificação em 2 etapas:** D4 e D5.

**Sessões:** D6.

### D3. A troca de senha, o logout e o aviso "Senha alterada."

**O que acontece no servidor:** trocar a senha grava um stamp novo e revoga **todas** as autorizações da pessoa, inclusive a desta aba (#219 D2/D3).

**O painel, depois do `204`:**
1. **grava o aviso** na chave `buteco.aviso.senha-alterada` do `sessionStorage` da aba (Q1);
2. chama o **mesmo `logout` do "Sair"** (`src/auth/logout.ts`, D7 da #217): o lock, o `idToken`, o registro apagado e o `/connect/logout` com `id_token_hint` e `post_logout_redirect_uri=<origin>/entrar`. Enquanto o lock não sai, o botão fica em "Saindo…", desabilitado;
3. o `apps/api` aceita o hint de uma autorização já revogada: revogar de novo não faz nada (`EndSessionEndpoint.cs`). Ele apaga o cookie e volta a `/entrar`. Sem hint válido, também volta a `/entrar`.

**O `/entrar`:**
- lê a chave **uma vez**, na montagem, e a apaga na mesma hora;
- com a chave, mostra "Senha alterada. Entre com a nova senha." (o estado `senha-alterada` do `Main`: verde, `role="status"`), acima do formulário;
- **o aviso tem precedência sobre "Sua sessão expirou".** Uma renovação desta aba em voo durante a troca recebe `invalid_grant` e pode chegar ao `/entrar?motivo=sessao-expirada` antes do logout. A chave já foi gravada antes, e o aviso certo é o da senha;
- some na primeira submissão, como os outros avisos.

**Por que o `sessionStorage` (Q1):** o `post_logout_redirect_uri` registrado é exatamente `<painel>/entrar` (`IdentityClients.cs`), e o aviso não pode ir na query desse endereço. Das três saídas analisadas na #245:

| opção | o que é | contra |
|---|---|---|
| **a) `sessionStorage` da aba (recomendada)** | a chave gravada antes do logout e lida no `/entrar` | nenhuma mudança no `apps/api`; o logout volta na mesma aba |
| b) o `state` do logout | o OpenIddict devolve o `state` na query do `post_logout_redirect_uri` | `/entrar?state=senha-alterada` vira um link que qualquer um monta, e mostra "Senha alterada" a quem não trocou nada |
| c) um segundo endereço registrado | `<painel>/entrar?aviso=senha-alterada` no cliente | muda o registro do cliente no `apps/api`, que esta change não toca |

**As outras abas:** recebem o evento `storage` do registro apagado e vão a `/entrar`, sem o aviso, que é da aba que trocou (D3 da #217). Se renovarem antes, recebem `invalid_grant` e vão a `sessao-expirada`. Os dois casos são aceitos: a pessoa sabe que trocou a senha.

**Correção (07/11/2026, achado da conferência no navegador, 11.4): a premissa "a chave gravada antes do logout chega ao `/entrar`" era falsa.** O `logout()` apaga o registro antes de navegar ao `/connect/logout`, e a página não é descarregada nesse instante. O `ProtectedRoute` via a sessão nula e levava o SPA a `/entrar?voltarPara=…` no documento antigo. Esse `/entrar` interno lia a chave e a apagava, duas vezes por causa do StrictMode, e só depois o navegador carregava o `/entrar` do logout, já sem a chave. Instrumentado no Chrome: o `removeItem` da chave rodou em `/entrar?voltarPara=%2Faccount` antes da carga nova. Os testes do `EntrarPage` passavam porque no jsdom o logout não navega. O mesmo intervalo fazia todo "Sair" mostrar o `/entrar` por um instante.

**Decisão do dono (opção 3, 06/11/2026):** o `createLogout` marca a aba como **saindo** (`leaving.mark()`, em `src/auth/logout.ts`) antes de apagar o registro. Enquanto ela está saindo, o `ProtectedRoute` mostra só "Saindo…", sem o `Navigate` para o `/entrar` interno, até o navegador sair para o `/connect/logout`. Vale para todo logout desta aba. As outras abas não têm a marcação: o evento `storage` as leva ao `/entrar` como antes. O `/entrar` continua lendo a chave uma vez e apagando. O teste `passwordChangeLogout.test.tsx` simula a navegação real (o `assign` só registra, e a página continua viva) e afirma que o `/entrar` interno não aparece e que a chave chega ao `/entrar` real. As alternativas recusadas foram apagar a chave só quando o documento foi carregado no `/entrar` (opção 1) e apagar só na primeira submissão (opção 2).

### D4. O cadastro das 2 etapas: as rotas e os estados

`GET /account/two-factor` → `{ ativa, ativadaEm, codigosRestantes, cadastroDisponivel }`.

| estado do quadro | quando | API | o que a tela faz |
|---|---|---|---|
| `indisponivel` | `ativa: false` e `cadastroDisponivel: false` | — | "Não configurada", "O cadastro da verificação em 2 etapas ainda não está disponível neste painel." e "Ligar verificação" desabilitado |
| `desligada` | `ativa: false` e `cadastroDisponivel: true` | — | "Não configurada", o texto do quadro e "Ligar verificação" |
| `senha` | "Ligar verificação" | — | "Sua senha" (`PasswordField`), "Continuar" e "Cancelar" |
| `senha-errada` | `401` "Senha inválida." | `POST …/setup` | erro no campo |
| `senha-bloqueada` | `423` | `POST …/setup` | o aviso do bloqueio |
| `qr` | `200 { segredo, otpauthUri }` | `POST …/setup` | o passo 1 (o QR e o segredo com "Copiar") e o passo 2 (o código de 6 dígitos, "Confirmar" e "Cancelar"), com "Este cadastro vale por 10 minutos" |
| `qr-codigo-invalido` | `401` | `POST …/confirm` | "Código inválido. Confira se o relógio do celular está certo." no campo (Q6) |
| `qr-vencido` | `400` "Não há cadastro em curso…" | `POST …/confirm` | "O cadastro venceu. Comece de novo para gerar outro QR." (Q6); "Cancelar" volta a `desligada` |
| `codigos` | `200 { codigosRecuperacao }` | `POST …/confirm` | "Verificação ligada.", os 10 códigos, "Copiar todos", "Baixar arquivo .txt" e "Concluir" |
| `ativa` | `ativa: true` | — | "Ativa", "desde `<ativadaEm>`", "N códigos de recuperação restantes.", "Gerar novos códigos" e "Remover verificação" |
| `poucos-codigos` | `ativa: true` e `codigosRestantes` de 1 a 3 (Q5) | — | "Restam N códigos de recuperação. Gere novos para não ficar sem acesso se perder o celular."; com 1, "Resta 1 código de recuperação. Gere novos…" |
| (sem quadro) | `ativa: true` e `codigosRestantes` 0 (Q5) | — | "Não restam códigos de recuperação. Gere novos para não ficar sem acesso se perder o celular." |
| `regenerar` | "Gerar novos códigos" | — | o campo "Código do app ou de recuperação" (`inputmode="text"`), "Gerar códigos" e "Cancelar" |
| `regenerar-codigo-invalido` / `regenerar-bloqueado` | `401` / `423` | `POST …/recovery-codes` | erro no campo / o aviso do bloqueio |
| `codigos` ("Novos códigos gerados.") | `200` | `POST …/recovery-codes` | como `codigos`, com o outro título |
| `remover` | "Remover verificação" | — | o campo do código, "Remover verificação" (perigo) e "Cancelar" |
| `remover-codigo-invalido` / `remover-bloqueado` | `401` / `423` | `POST …/remove` | erro no campo / o aviso do bloqueio |
| volta a `desligada` | `204` | `POST …/remove` | o `GET` relido |

- **Ativa com o cadastro fechado:** a regeneração e a remoção valem com qualquer valor da opção (`two-factor-totp`). O cartão `ativa` não olha `cadastroDisponivel`.
- **O campo do código** de `regenerar` e `remover` segue o do `/entrar`: aceita o código do app e o de recuperação, com `trim` só nas pontas. O do `qr` aceita só o do app, com `inputmode="numeric"`, como no quadro: o `confirm` confere só o pendente.
- **Depois de `codigos`, "Concluir"** relê o `GET` e vai a `ativa`.

### D5. QR, segredo e códigos: o pacote, a CSP e a memória

**O pacote:** `qrcode.react` **4.2.0**, exato no `package.json` (sem `^`).
- **Versão:** a última publicada no npm (11/12/2024), conferida no registro em 06/11/2026.
- **Licença:** ISC, Copyright (c) 2015 Paul O'Shannessy.
- **Dependências:** nenhuma. `peerDependencies`: `react` `^16.8 … ^19.0`. O painel usa o React 19.2.
- **Tamanho no bundle:** só o `QRCodeSVG`, com o React externo, medido com esbuild 0.24 em 06/11/2026: **16,6 kB minificado, 6,2 kB com gzip**. A medição final é a diferença do `vite build` contra a baseline (o bundle principal tem 1.097,9 kB hoje).

**O QR em SVG, sem `innerHTML`:**
- o `QRCodeSVG` monta, por `React.createElement`, um `<svg role="img">` com um `<title>` opcional e dois `<path>`. Na fonte (`lib/esm/index.js`) não há `innerHTML` nem `dangerouslySetInnerHTML`, e a `imageSettings`, a única que poria `<image>`, não é usada;
- `value` é o `otpauthUri` tal como veio; `size` de 164 px, como no quadro; `title` e `aria-label` "QR para o app autenticador";
- **módulos escuros sobre fundo branco também no tema escuro**, com margem: os leitores precisam de contraste e da zona quieta, e o QR invertido falha em parte dos apps. O cartão em volta segue o tema;
- **contra a CSP** (`default-src 'self'; script-src 'self' 'sha256-…'; style-src 'self' 'unsafe-inline'; img-src 'self' data:`):
  - SVG inline no DOM não é carregamento de imagem, e não passa por `img-src`;
  - não há script, `eval` nem estilo externo.

  Mesmo assim, a conferência é no navegador, com a CSP do `nginx.conf` aplicada (tarefa de conferência: o `vite build` servido pelo `vite preview` com o cabeçalho do `nginx.conf`, num arquivo de configuração temporário fora do repositório), sem nenhum aviso de CSP no console. Fica também um teste: o QR renderizado não tem `<image>`, `<foreignObject>` nem `<script>`.

**O segredo:**
- aparece em grupos de 4, como no quadro (`JBSW Y3DP …`);
- "Copiar" (`CopyButton` do Mantine, já usado no painel) copia o valor **sem os espaços**, como veio da API.

**Os códigos de recuperação:**
- "Copiar todos" copia um código por linha;
- **"Baixar arquivo .txt"** gera o arquivo no navegador, sem passar pelo servidor:
  1. `new Blob([texto], { type: 'text/plain;charset=utf-8' })`;
  2. `URL.createObjectURL`;
  3. um `<a download>` clicado;
  4. `URL.revokeObjectURL` na mesma volta.

  O conteúdo tem um cabeçalho ("Buteco Agentes — códigos de recuperação", o e-mail, a data) e os 10 códigos, um por linha. O nome do arquivo é `buteco-agentes-codigos-de-recuperacao.txt`. A CSP não se aplica a essa navegação, e a conferência no navegador confere o download.

**Só na memória da tela:**
- o `segredo`, o `otpauthUri` e os códigos ficam **no estado do componente do cartão**, e saem ao sair dele (desmontagem) ou ao avançar o passo (o `qr` limpa ao confirmar ou cancelar; os códigos limpam no "Concluir");
- **nada no `sessionStorage`, no `localStorage`, na URL ou no `state` do router**;
- **nem no cache do TanStack Query:** o `useMutation` guarda o `data` da mutação no `MutationCache` até o `gcTime` (5 min por padrão), mesmo depois de desmontar, e o observador o guarda enquanto o cartão está montado, mesmo depois de sair do passo. Por isso as quatro chamadas (`setup`, `confirm`, `recovery-codes`, `remove`) são **chamadas diretas**, sem `useMutation`, e o resultado vai só para o estado do componente. **Decidido na implementação (7.1, 07/11/2026):** o design dizia `gcTime: 0`, que não cobre o observador montado. Um teste confere que, depois de sair da página, nem o `MutationCache` nem o `QueryCache` guardam o `segredo` ou algum código;
- recarregar a página no meio do `qr` perde o segredo: o cadastro pendente fica no servidor até vencer (10 min), e "Ligar verificação" recomeça com a senha. É o comportamento esperado.

### D6. As sessões

`GET /account/sessions` → `[ { id, criadaEm, ultimoUso, atual } ]`, só as vivas, ordenadas por `criadaEm` decrescente (`UserSessions.ListLiveAsync`).

| estado do quadro | quando | o que a tela faz |
|---|---|---|
| `lista` | o `GET` | a tabela (Sessão, Começou em, Último uso, Ações), a linha `atual` com o selo "Esta sessão" e a ação "Encerrar e sair"; as outras com "Encerrar"; "Encerrar as outras" no cabeçalho do cartão quando há outra sessão; e a nota de rodapé do quadro |
| `confirmar-uma` | "Encerrar" numa linha | "Encerrar a Sessão N? Quem estiver usando esse navegador precisa entrar de novo." com "Encerrar" e "Cancelar" |
| (encerrada) | `POST /account/sessions/{id}/revoke` → `204` | relê a lista, sem aviso (o quadro não tem um) |
| `confirmar-outras` | "Encerrar as outras" | "Encerrar as outras N sessões? Quem estiver usando esses navegadores precisa entrar de novo." |
| `outras-encerradas` | `POST /account/sessions/revoke-others` → `200 { encerradas }` | "N sessões encerradas."; "1 sessão encerrada."; com 0, "Nenhuma outra sessão estava aberta." (Q10); e a lista relida |
| "Encerrar e sair" | a linha `atual` | o `logout` do D3, sem chamar a rota de revogação e sem confirmação, como no quadro (Q9) |

- **"Encerrar e sair" pelo logout, e não pela rota:** o logout revoga a autorização do hint, que é a mesma da sessão `atual` (o `oi_au_id` não muda nas renovações; `AccountSessionEndpoints.cs`). Ele apaga também o cookie do emissor e leva ao `/entrar`. A rota de revogação deixaria a aba logada até a renovação seguinte.
- **Os nomes "Sessão N":** a API não traz rótulo, só datas (#219; IP e navegador são a #221). O nome é a posição na lista, na ordem do servidor (Q4). A confirmação cita o nome da linha.
- **`404` ao encerrar uma:** a sessão já tinha saído, por exemplo em outra aba. A tela relê a lista, sem erro.
- **`400` "Não foi possível identificar a sessão atual."** em `revoke-others`: o token sem `oi_au_id`, o que não acontece com o token do painel. Fica o aviso genérico do cartão (D8).
- **"Último uso" da linha `atual`:** "agora", como no quadro (Q4). As outras usam o formatador do D9.

### D7. Onde mora o código

```
features/account/
  api/accountApi.ts          request<T> da feature + 10 chamadas e os tipos
  pages/AccountPage.tsx      o título, o subtítulo e os quatro cartões
  components/
    AccountDataCard.tsx
    AccountPasswordCard.tsx
    TwoFactorCard.tsx        a máquina de estados do D4
    TwoFactorQr.tsx          o QRCodeSVG, o segredo e "Copiar"
    RecoveryCodes.tsx        os códigos, "Copiar todos" e o .txt
    AccountSessionsCard.tsx
  recoveryCodesFile.ts       o texto e o download do .txt
  accountDates.ts            o formatador relativo (D9)
features/auth/
  passwordChangedNotice.ts   gravar, ler e apagar a chave do aviso (D3)
```

- **O nome `AccountSessionsCard`, e não `SessionsCard`:** `features/sessions` já existe e é das conversas do inbox.
- **A gravação e a leitura do aviso ficam em `features/auth`**, ao lado do `/entrar`, que é quem lê. Minha conta só chama `markPasswordChanged()`.

### D8. Avisos, bloqueio e estados sem quadro

- **O bloqueio (`423`):** o texto do quadro, "Muitas tentativas sem sucesso. Por segurança, a sua conta fica pausada por N minutos.", com N do `Retry-After` arredondado para cima (`lockMinutes` do `entryNotices`). O quadro diz "5 minutos" fixo, e isso já está registrado na #217. Sem `Retry-After` legível, o texto diz "alguns minutos". **No desenvolvimento, o `Retry-After` do `apps/api` não fica legível**, porque o pedido é cross-origin e o cabeçalho não está em `Access-Control-Expose-Headers`. Em produção a origem é a mesma. Fica registrado, sem mudança.
- **Estados que o quadro não desenha** (Q2), todos com fonte no contrato:

  | estado | onde |
  |---|---|
  | carregando e erro do `GET` | os três `GET` |
  | `429`, `503` e falha de rede | todos os `POST` |
  | `500` "Não foi possível conferir o código do app autenticador…" (a chave do TOTP trocada) | `regenerar` e `remover` |
  | `409` "já está ligada" / "não está ligada" (corrida com outra aba) | `setup`, `recovery-codes`, `remove` |
  | `403` cadastro fechado entre o `GET` e o `setup` | `setup`, `confirm` |

  **Proposta:**
  - um aviso vermelho dentro do cartão, com o `title` do servidor quando houver, ou o texto de rede e de indisponível do `/entrar` (`SEM_CONEXAO`, `INICIANDO`, `MUITAS_TENTATIVAS`), e o botão desabilitado até o `Retry-After` no `429` e no `503`;
  - o `409` e o `403` releem o `GET` do cartão, e o cartão vai ao estado que a resposta indicar;
  - o carregamento é um `Skeleton` por cartão, e o erro do `GET`, o aviso com "Tentar de novo".

### D9. Datas

- **"No painel desde"** e **"desde" (2 etapas):** `dd/mm/aaaa`, no fuso do navegador.
- **"Último acesso", "Começou em" e "Último uso":** "hoje, HH:MM", "ontem, HH:MM", ou `dd/mm/aaaa, HH:MM`, como no quadro, com "agora" só na linha `atual`.
- O formatador recebe o "agora" e o fuso, para o teste não depender da máquina, como o `formatLinkExpiry` da #245.

### D10. O `401` de recusa da rota (Q0)

`src/auth/sessionFetch.ts` muda assim:
- no `401`, **antes de renovar**, olha o `Content-Type`;
- com `application/problem+json`, devolve a resposta à tela, sem renovar, sem repetir e sem encerrar;
- sem ele, faz o que faz hoje.

A regra vale para os três apps, porque o `401` de autenticação do JwtBearer nunca traz corpo. **Teste de guarda:** o `401` com problem+json não chama a renovação, e o `fetch` é chamado uma vez só. O `401` sem corpo continua renovando, e os casos de hoje do `sessionFetch.test.ts` ficam como estão.

**O lado do servidor (complemento do dono):** um teste por app fixa a premissa de que o painel depende:
- `apps/api` (`IssuerTokenAcceptanceTests`), `apps/inbox` e `apps/connectors` (`IssuerTokenValidationTests`): `TokenRejection_401_HasNoProblemBody_ThePanelRenewsOnThis`;
- dois casos em cada um, sem token e com um token inválido: `401`, o `Content-Type` diferente de `application/problem+json` e o corpo vazio.

**Guarda, um por app:** um `OnChallenge` do JwtBearer que escreve um corpo problem+json no `401` faz o teste do app reprovar.

### D11. Testes e guardas

Cada guarda (convenção 15) reintroduz só o defeito nomeado, é visto reprovar e volta pela cópia, com o sha conferido. Os guardas previstos:

| defeito | o caso que reprova |
|---|---|
| o `sessionFetch` renovando no `401` problem+json (Q0) | "401 com problem+json volta à tela sem renovar nem repetir" (`sessionFetch.test.ts`) e "a senha atual errada fica no campo, sem deslogar" |
| o `apps/api`, o `apps/inbox` ou o `apps/connectors` com um corpo problem+json no `401` de token | `TokenRejection_401_HasNoProblemBody_ThePanelRenewsOnThis`, no app do defeito |
| o logout sem gravar o aviso antes | "depois do 204, o /entrar mostra Senha alterada" |
| o `/entrar` dando precedência a `sessao-expirada` | "a chave do aviso vence o motivo sessao-expirada" |
| a chave não apagada na leitura | "o aviso aparece uma vez só" |
| a mutação sem `gcTime: 0` | "nenhum segredo nem código fica no MutationCache depois de sair" |
| o segredo copiado com os espaços | "Copiar copia o segredo sem espaços" |
| o `.txt` sem `revokeObjectURL` | "o download libera a URL do blob" |
| "Encerrar e sair" chamando a rota de revogação em vez do logout | "encerrar a sessão atual vai ao logout do emissor" |
| "Encerrar" sem confirmação | "encerrar uma sessão pede confirmação" |
| o selo "Esta sessão" na primeira linha, e não na `atual` | "a sessão atual é a marcada, onde quer que esteja na lista" |
| o rodapé sem `aria-current` em `/account` | "o link do rodapé marca a página atual" |
| `indisponivel` com o botão habilitado | "com o cadastro fechado, Ligar verificação fica desabilitado" |
| o QR com `imageSettings` (um `<image>` no SVG) | "o QR não tem image, foreignObject nem script" |

**Os duplos de teste:**
- um `fetch` falso por caminho para o `sessionFetch`, no molde do `respondByPath` da #245, mas compartilhado pela pasta `features/account/test/`, porque cinco arquivos o usam;
- o `navigator.clipboard`;
- o `URL.createObjectURL` e o `revokeObjectURL`;
- o espião do `logout`;
- o `sessionStorage`, que o jsdom já tem.

## Árvore de pastas proposta

```
apps/frontend/
├── package.json                                (+ qrcode.react 4.2.0, exato)
├── package-lock.json
└── src/
    ├── app/
    │   ├── routes.tsx                          (+ rota account, D1)
    │   ├── ProtectedRoute.tsx                  ("Saindo…" no logout desta aba, D3 — Q11)
    │   └── router.test.tsx                     (+ /account exige sessão)
    ├── auth/
    │   ├── sessionFetch.ts                     (401 problem+json devolvido, D10)
    │   ├── logout.ts, session.ts               (a aba marcada como saindo, D3 — Q11)
    │   └── sessionFetch.test.ts
    ├── components/layout/
    │   ├── AppShell.tsx                        (o bloco do nome vira link, D1)
    │   └── AppShell.test.tsx
    └── features/
        ├── auth/
        │   ├── passwordChangedNotice.ts        (novo: gravar, ler e apagar, D3)
        │   ├── passwordChangedNotice.test.ts
        │   ├── messages.ts                     (+ SENHA_ALTERADA)
        │   └── pages/
        │       ├── EntrarPage.tsx              (o aviso senha-alterada)
        │       └── EntrarPage.test.tsx
        └── account/                            (novo)
            ├── api/
            │   ├── accountApi.ts
            │   └── accountApi.test.ts
            ├── pages/
            │   ├── AccountPage.tsx
            │   └── AccountPage.test.tsx
            ├── components/
            │   ├── AccountDataCard.tsx
            │   ├── AccountPasswordCard.tsx
            │   ├── AccountPasswordCard.test.tsx
            │   ├── TwoFactorCard.tsx
            │   ├── TwoFactorCard.test.tsx
            │   ├── TwoFactorQr.tsx
            │   ├── RecoveryCodes.tsx
            │   ├── AccountSessionsCard.tsx
            │   └── AccountSessionsCard.test.tsx
            ├── accountDates.ts
            ├── accountDates.test.ts
            ├── recoveryCodesFile.ts
            ├── recoveryCodesFile.test.ts
            └── test/
                └── fakeAccountApi.ts           (o fetch falso por caminho)
```

Fora do `apps/frontend`:

```
apps/api/tests/Buteco.Api.Tests/Identity/IssuerTokenAcceptanceTests.cs           (+ TokenRejection_401_…, D10)
apps/inbox/tests/Buteco.Inbox.Tests/IssuerTokenValidationTests.cs                (+ TokenRejection_401_…, D10)
apps/connectors/tests/Buteco.Connectors.Tests/IssuerTokenValidationTests.cs      (+ TokenRejection_401_…, D10)
docker-compose.prod.yml                                                          (+ Identity__TwoFactor__EnrollmentEnabled, Q8)
.env.prod.example                                                                (+ IDENTITY_TWO_FACTOR_ENROLLMENT_ENABLED comentada, Q8)
docs/deployment.md, docs/configuration.md                                        (a seção da #246 e o texto "até a #246")
```

Nada em `libs/`. Nenhum código de produção fora do `apps/frontend`: os três apps de backend ganham só testes.

## Tamanho

**Âncora** (convenção 18): a #245, medida em 06/11/2026, só código, pelo `git diff --numstat` contra `1131635`:
- **produção:** +1.044 / −127, em 21 arquivos;
- **testes:** +1.118 / −17, em 12 arquivos.

Lá a produção errou +13% sobre a projeção, e os testes −33%.

**Projeção**, com os duplos à parte (aprendizado da décima segunda medição):

| componente | produção | testes (casos) | duplos |
|---|---|---|---|
| `accountApi` (cópia do `request<T>`, 10 chamadas, tipos) | ~170 | ~180 | — |
| página, Dados, datas (D1, D2, D9) | ~150 | ~150 | — |
| Senha, logout e o aviso no `/entrar` (D2, D3) | ~210 | ~300 | — |
| 2 etapas: a máquina de estados, o QR, o segredo, os códigos e o `.txt` (D4, D5) | ~480 | ~620 | — |
| Sessões (D6) | ~200 | ~260 | — |
| rodapé e rota (D1) | ~35 | ~60 | — |
| `sessionFetch` (D10) | ~15 | ~40 | — |
| os três testes do `401` sem corpo (D10, um por app) | — | ~60 | — |
| `fetch` falso por caminho, clipboard, blob e logout | — | — | ~140 |
| **total** | **~1.260** | **~1.670** | **~140** |

O total (~1.260 / ~1.810) fica perto do teto do que já foi revisado num PR: a #217 teve +1.317 / +2.091. Com o erro da #245 (+13%), a produção pode chegar a ~1.420.

**Divisão possível, registrada e não adotada (Q7: um PR só):**
- **B1:** Dados, Senha com o aviso, Sessões, rodapé, rota e o `sessionFetch`. Cerca de ~780 / ~990;
- **B2:** as 2 etapas, com o `qrcode.react` e o deploy do cadastro. Cerca de ~480 / ~760.

Os dois teriam valor sozinhos, e o B2 é o que libera o cadastro em produção. Não é recomendada porque o B2 reusaria a página, o `accountApi` e os duplos do B1, e o cartão das 2 etapas, sozinho, é cerca de um terço do total. Nenhuma issue foi criada antes da decisão do dono.

## Risks / Trade-offs

- **[Q0 muda o `sessionFetch` de todo o painel]** → A regra pega só o `401` com `application/problem+json`, que o JwtBearer nunca produz. O guarda garante que o `401` sem corpo continua renovando, e os casos atuais do `sessionFetch.test.ts` ficam.
- **[O segredo e os códigos na memória do navegador]** → Um XSS na origem do painel os lê. Ele também lê os tokens da sessão (#217 D2). A CSP é a defesa, e nada vai para storage, URL ou cache.
- **[O `.txt` dos códigos fica nos Downloads]** → É o que a pessoa pediu ao clicar. O texto do quadro já orienta a guardar fora do celular.
- **[A troca de senha no meio de uma renovação de outra aba]** → A outra aba recebe `invalid_grant` e vai a `sessao-expirada`, sem o aviso, que é da aba que trocou (D3).
- **[Os nomes "Sessão N" mudam quando uma sessão sai]** → A confirmação cita o nome da linha no momento do clique, e a lista é relida depois de cada ação.
- **[O `Retry-After` ilegível no desenvolvimento]** → O texto cai em "alguns minutos" só no dev (D8). Em produção a origem é a mesma.

## Migration Plan

- **Deploy:** a imagem do `frontend` e uma linha no compose do `api` (Q8), que só lê a variável nova, falsa por padrão. Sem migration.
- **Abrir o cadastro em produção (a decisão é do dono, depois do deploy):** hoje o `docker-compose.prod.yml` **não passa** `Identity__TwoFactor__EnrollmentEnabled` ao `apps/api`, e o `IDENTITY_TWO_FACTOR_ENROLLMENT_ENABLED` citado no pedido não existia. Ele entra nesta change (Q8):

  ```yaml
  Identity__TwoFactor__EnrollmentEnabled: ${IDENTITY_TWO_FACTOR_ENROLLMENT_ENABLED:-false}
  ```

  com a linha comentada no `.env.prod.example`.
  - **Para abrir:** `IDENTITY_TWO_FACTOR_ENROLLMENT_ENABLED=true` no `.env.prod`, e `docker compose -f docker-compose.prod.yml --env-file .env.prod up -d api`.
  - **Para conferir:**
    - o `GET /account/two-factor` com um token do painel responde `"cadastroDisponivel": true`;
    - em Minha conta, "Ligar verificação" fica habilitado;
    - o cadastro de ponta a ponta com um app autenticador termina em "Verificação ligada.";
    - o login seguinte pede o código.
  - **Para voltar atrás:** `false` (ou remover a linha) e subir o `api` de novo. Quem já ligou a verificação continua com ela, porque a verificação no login, a regeneração e a remoção valem com qualquer valor. Só o cadastro novo fecha.
  - **A #220 remove a opção depois.**
- **Rollback da change:** a imagem anterior do `frontend`. Com ela, Minha conta some. Quem ligou a verificação continua entrando pelo passo do código da #245, mas perde a regeneração e a remoção pelo painel até o deploy voltar. A redefinição pelo admin continua pela API.

## Divergências do protótipo (convenção 17)

Nenhuma entre o quadro `MinhaConta` e o contrato. O que o quadro mostra sem fonte direta:
- **os nomes "Sessão 1/2/3":** derivados da posição na lista (Q4);
- **"agora" no último uso da sessão atual:** derivado de `atual` (Q4);
- **o limite de `poucos-codigos`:** o quadro mostra 2 restantes, e o contrato dá o número, mas não o limite (Q5).

O link "use Usuários" aponta para uma tela da #183 que ainda não existe (Q3). Nenhum campo foi inventado.

## Registros (convenção 23)

- **#183:** as ações com a senha de quem chama herdam a correção da Q0, e o "use Usuários" fica sem link até ela ([comentário](https://github.com/aldovrando-oliveira/buteco-agentes/issues/183#issuecomment-6701300111)).
- **#247:** passa a cobrir também os `401` de senha e código errados de Minha conta e de Usuários ([comentário](https://github.com/aldovrando-oliveira/buteco-agentes/issues/247#issuecomment-6701299874)).
- **#221:** IP e navegador nas sessões continuam fora.
- **#220:** remove a opção do cadastro depois desta change.

## Decisões do dono (06/11/2026; Q11 na implementação)

Na revisão do proposal e do design, todas como recomendadas, com um complemento na Q0:

| | pergunta | decisão | onde |
|---|---|---|---|
| Q0 | Como separar o `401` de senha ou código errado do `401` de token? | o `sessionFetch` devolve à tela, sem renovar, repetir nem encerrar, o `401` com `Content-Type: application/problem+json`. **Complemento:** em cada app (api, inbox, connectors), `TokenRejection_401_HasNoProblemBody_ThePanelRenewsOnThis` fixa que o `401` de token não traz corpo problem+json, com guarda; comentários na #247 e na #183 | Contexto, D10 |
| Q1 | Como o aviso "Senha alterada." atravessa o logout? | pelo `sessionStorage` da aba, lido uma vez e apagado, vencendo o "sessão expirada" | D3 |
| Q2 | Os estados que o quadro não desenha? | aviso dentro do cartão, com o texto do servidor ou os do `/entrar`, o botão desabilitado até o `Retry-After`, e o `GET` relido no `409` e no `403`; cada um listado para a validação visual | D8 |
| Q3 | O "use Usuários" antes da #183? | sem link; a #183 o transforma em link (registrado lá) | D2 |
| Q4 | Nome, ordem e "agora" das sessões? | a ordem do servidor, "Sessão N" pela posição, "agora" no último uso da atual | D6, D9 |
| Q5 | O limite de `poucos-codigos`? | 3 ou menos; o singular para 1; o texto próprio para 0 | D4 |
| Q6 | Os textos do QR: do quadro ou do servidor? | os do quadro | D4 |
| Q7 | Um PR ou dois? | um PR só | Tamanho |
| Q8 | A variável do cadastro em produção? | `Identity__TwoFactor__EnrollmentEnabled: ${IDENTITY_TWO_FACTOR_ENROLLMENT_ENABLED:-false}` no compose de produção, o `.env.prod.example` e as notas de abrir, conferir e voltar atrás | Migration Plan |
| Q9 | "Encerrar e sair" na sessão atual? | pelo logout do emissor, sem confirmação | D6 |
| Q10 | O texto do `{encerradas}` com 1 e 0? | "1 sessão encerrada." e "Nenhuma outra sessão estava aberta." | D6 |
| Q11 | O aviso "Senha alterada." era consumido pelo `/entrar` interno antes do `/entrar` do logout (achado da 11.4, 07/11/2026). Como corrigir? | **opção 3:** a aba que sai fica marcada, e a rota protegida mostra "Saindo…" em vez de ir ao `/entrar` interno; vale para todo "Sair"; as outras abas seguem pelo evento `storage`; o `/entrar` continua lendo a chave uma vez e apagando | D3 |

## Open Questions

Nenhuma.
