## Context

Primeira parte do passo C da #183. A entrada pública (#245) e Minha conta (#246) já estão mergeadas. Em 07/11/2026 o dono dividiu o passo C em dois PRs (Q1, opção A):

- **esta change (#250):** Usuários (a lista, convidar, gerar novo link e cancelar), Identidade e a seção "Acesso" do menu;
- **a #183:** Editar usuário, o "Editar" na linha da lista e o link "use Usuários" de Minha conta. A parte do design escrita na exploração que vale para ela está [num comentário da #183](https://github.com/aldovrando-oliveira/buteco-agentes/issues/183#issuecomment-6710514538): o reconhecimento do próprio pelo `sub`, o detalhe, as ações e o link de Minha conta.

**Fontes lidas para este design (07/11/2026):**
- **#183:** o corpo e todos os comentários até a divisão:
  - o [inventário](https://github.com/aldovrando-oliveira/buteco-agentes/issues/183#issuecomment-6649327956) e as duas releituras do passo C;
  - os contratos da #218, da #219, da #181 e da #211;
  - os registros de entrega da #245 e da #246.
- **Designs arquivados:**
  - `2026-11-07-entrada-publica-painel` (#245): `PasswordField`, `entryNotices` e os textos de rede e de indisponível;
  - `2026-11-07-minha-conta-painel` (#246):
    - D5: o que é secreto fica fora do TanStack Query;
    - D8: os avisos dentro do cartão;
    - D9: as datas;
    - D10: o `401` com problem+json devolvido à tela;
    - o padrão de confirmação na linha.
- **O código do `apps/api` em `a852946`**, conferido contra os comentários: `UserEndpoints.cs`, `UserResponses.cs`, `UserFields.cs`, `InvitationEndpoints.cs`, `CallerPassword.cs`, `IdentityOverviewEndpoint.cs`.
- **O protótipo:** o canvas "Protótipo de autenticação federada" (`H7dPBZafiSkdKd1hqesPZ6`), lido em 07/11/2026 pelo Artifact tool. Foram lidos os quadros `Usuarios` e `Provedores`, com todas as propriedades.
  - **A versão lida é a `1794066606-fba2`.** A listagem dos arquivos, feita no mesmo minuto, ainda dizia `1794015615-ba72`. A última releitura registrada do passo C foi na `1793853618-1811`.
  - Não há cópia das versões anteriores para comparar. Todos os estados que o inventário lista para estes dois quadros existem na versão atual.

**Estado do código (`a852946`):**
- não há `/users` nem `/identity` no painel, e o menu não tem seção;
- o `sessionFetch` devolve à tela, sem renovar nem deslogar, o `401` com `application/problem+json` (#246 D10);
- `features/account/` tem `cardFailure.ts`, `useBlockedUntil.ts` e `accountDates.ts`:
  - o `cardFailure` reconhece o erro por `instanceof` do `ApiError` de Minha conta;
  - o `request<T>` tem sete cópias, uma por feature (convenção 2);
- o `AccountCard` (o `CardLoadError`) também usa o `failureText`.

**Baseline do `apps/frontend`** (`~/.cache/buteco-agents/250-baseline/`, em `a852946` com a árvore limpa):
- 2166 de 2166 casos aprovados, em 168 arquivos, em 207 s;
- o relatório por nome está em `nomes.txt` (2166 linhas, nenhuma reprovada);
- a rodada começou com load 5,38 e CPU em 100. A CPU caiu até 30, e pela regra assimétrica a rodada conta como `APROVADA`.

## Goals / Non-Goals

**Goals:**
- As rotas `/users` e `/identity`, e a seção "Acesso" do menu.
- Todo estado dos quadros `Usuarios` e `Provedores` (`entrega=m1`), cada um com a rota da API, os erros e os textos.
- As duas ações com a senha de quem chama (convidar e gerar novo link), com o `401` e o `423` no formulário, sem deslogar.
- Os links de convite mostrados uma vez, copiáveis e só na memória da tela.
- `failureNotice`, `useBlockedUntil` e as datas num lugar comum, com Minha conta igual.

**Non-Goals:**
- **Editar usuário, o "Editar" na linha e o link de Minha conta:** são da #183.
- **Papel, "Aplicações externas" e o card "Provedores federados"** (M2, M3). Os quadros já os escondem com `entrega=m1`.
- **Mudar rota ou contrato do `apps/api`.** Dado que a tela precise e o backend não sirva vira achado a sequenciar. Até aqui, nenhum.
- **Extrair um cliente HTTP comum:** cada feature tem a sua cópia do `request<T>` (convenção 2).

## Decisions

### D1. Rotas, menu e o que muda de lugar

**Rotas**, dentro do `ProtectedRoute` e da casca:

| rota | página | quadro |
|---|---|---|
| `/users` | `UsersPage` | `Usuarios` |
| `/identity` | `IdentityPage` | `Provedores` |

- **O `nginx.conf` não muda.** `/users` e `/identity` já caem no `apps/api` com o desvio de navegação (`Sec-Fetch-Mode`): a página abre, e o `fetch` vai à API (inventário da #183; #211).
- **`/users/:id` não existe nesta change** (#183). Um endereço `/users/<id>` digitado não casa nenhuma rota, como hoje.

**O menu** (`frontend-app-shell`, requisito novo):
- depois de "Insights", o rótulo de seção "Acesso", como o `.navsec` do quadro: maiúsculas e menor que os itens. O tom é o `dimmed` do `SectionLabel`, e não o terciário do protótipo, pelo mesmo motivo do `SectionLabel` (o contraste da `frontend-visual-theme`; ajuste na implementação, 2.2). O rótulo não é link nem item de navegação;
- abaixo dele, **Usuários** (ícone `Users` do lucide) e **Identidade** (ícone `Shield`), com o mesmo `NavLink` e o ativo por `startsWith`;
- **"Aplicações externas" não aparece:** o item só existe com `entrega=completa`.

**O que muda de lugar** (Q2):

| de | para | o que muda |
|---|---|---|
| `features/account/cardFailure.ts` | `utils/failureNotice.ts` | o `instanceof ApiError` vira `isApiFailure(error)`, que confere `status` (número), `retryAfter` e `title`. Assim os dois `ApiError` (o de Minha conta e o de Usuários) passam |
| `features/account/useBlockedUntil.ts` | `hooks/useBlockedUntil.ts` | nada |
| `features/account/accountDates.ts` | `utils/panelDates.ts` | ganha o `formatShortDateTime` (`dd/mm, HH:MM`, D2) |

- Os importadores de Minha conta (os cartões, o `AccountCard`, a página) passam a importar do lugar novo.
- Os testes de Minha conta não mudam de asserção. O `accountDates.test.ts` vai junto, como `panelDates.test.ts`, com os mesmos casos e o nome dos `describe` mantido.
- **Guarda da Q2:** um erro com a forma de `ApiError`, mas de outra classe (o de Usuários), recebe o texto do bloqueio, e não o "sem conexão".

### D2. Usuários: a lista

`GET /users` devolve `[ { id, nome, email, estado, formasDeEntrada, duasEtapas, ultimoAcesso, criadoEm, convite } ]`, por nome e sem paginação. O cartão é o `SectionedCard` "Usuários", com "Convidar usuário" pequeno e primário à direita da faixa.

| coluna | fonte | o que a tela mostra |
|---|---|---|
| Usuário | `nome`, `email` | o nome em negrito, com o e-mail monoespaçado abaixo |
| Formas de entrada | `formasDeEntrada` | `senha` vira um chip "Senha". Lista vazia (convidado): "—". Valor desconhecido: chip neutro com o valor cru (convenção 13) |
| Verificação em 2 etapas | `duasEtapas`, `estado` | `true`: "Ativa" (verde). `false`: "Não configurada" (amarelo). Convidado: "—" (neutro) |
| Estado | `estado`, `convite` | ver a tabela abaixo |
| Último acesso | `ultimoAcesso` | `null`: "—". Senão, o formato relativo de Minha conta (Q3): "hoje, 14:15", "ontem, 18:40", `dd/mm/aaaa, HH:MM` |
| Ações | `estado` | convidado: "Gerar novo link" e "Cancelar convite", com `aria-label` com o nome. **Os outros: nada nesta change** (D8, divergência temporária) |

**A coluna Estado:**

| `estado` | selo | linha abaixo do selo |
|---|---|---|
| `ativo` | "Ativo" (verde) | — |
| `desativado` | "Desativado" (cinza) | — |
| `convidado`, com `vencido: false` | "Convite pendente" (amarelo) | "vence em dd/mm, HH:MM" |
| `convidado`, com `vencido: true` | "Convite vencido" (cinza) | "venceu em dd/mm, HH:MM" |
| desconhecido | neutro, com o valor cru | — |

- **`vencido` vem do servidor.** A tela não o recalcula pelo relógio do navegador (`UserSummary`).
- **Os dois cartões de baixo** ("Acesso só por convite", "Senha e recuperação") são texto fixo do quadro, na versão `m1`.

**A carga:**

| quadro | quando | a tela |
|---|---|---|
| `carga=carregando` | o `GET` em voo | "Carregando os usuários…" (`role="status"`) no corpo do cartão |
| `carga=erro` | o `GET` falhou (`503`, rede, outro) | "Não foi possível carregar os usuários agora." e "Tentar de novo" (`role="alert"`) |

- **A lista vazia não acontece**, porque quem chama está nela. A tabela sairia sem linhas, sem texto inventado.

### D3. Usuários: o cartão como máquina de estados

Só um painel fica aberto por vez dentro do cartão, entre a faixa e a tabela, como no quadro. "Convidar usuário" fica desabilitado enquanto há um painel aberto.

```
  lista ──"Convidar usuário"──▶ convidar ──201──▶ link (gerado) ──Concluir──▶ lista
    │                              │ 400/409 no campo; vazio e 401 em "Sua senha"; 423/429/503/rede no aviso
    │                              └─Cancelar──▶ lista
    ├──"Gerar novo link" (linha)──▶ renovar(id) ──200──▶ link (renovado) ──Concluir──▶ lista
    │                                 │ vazio/401/423/429/503/rede: como no convidar
    │                                 ├─409──▶ lista relida + aviso azul (Q7)
    │                                 └─404──▶ lista relida, sem aviso (Q7)
    └──"Cancelar convite" (linha)──▶ cancelar(id), na própria linha ──Voltar──▶ lista
                                      ├─204──▶ lista relida
                                      ├─409──▶ lista relida + aviso azul (convite-aceito)
                                      └─404──▶ lista relida, sem aviso
```

**Convidar** (`POST /users/invitations` com `{ nome, email, senha }`):

| estado do quadro | quando | a tela |
|---|---|---|
| `formulario` | "Convidar usuário" | Nome, E-mail e "Sua senha" (D4); "O Buteco gera um link de uso único, válido por 72 horas…"; "Gerar convite" e "Cancelar" |
| (sem quadro) | "Sua senha" vazia | "Informe a sua senha." no campo, sem enviar (Q6) |
| — | `400` | o `errors.nome` e o `errors.email` do servidor nos campos |
| `email-em-uso` | `409` "Já existe um usuário com este e-mail." | o `title` no campo E-mail, com `aria-invalid`. O servidor confere o e-mail antes da senha, então a recusa não conta no bloqueio |
| `senha-errada` | `401` "Senha inválida." | o `title` no campo "Sua senha", com `aria-invalid` |
| `senha-bloqueada` | `423` + `Retry-After` | o aviso amarelo do quadro, acima do campo, com o N do `Retry-After` (`lockedText`). "Gerar convite" fica desabilitado até passar |
| (sem quadro) | `429`, `503`, rede | o `failureNotice` no mesmo lugar, como em Minha conta (#246 Q2) |
| `link-gerado` | `201 { usuario, link, expiraEm }` | o painel do link (D5): "Convite criado para {nome}" e o e-mail. A lista é relida, e a linha de `usuario.id` fica marcada enquanto o painel está aberto (Q8) |

**Gerar novo link** (`POST /users/{id}/invitation/renew` com `{ senha }`):

| estado do quadro | quando | a tela |
|---|---|---|
| `renovar` | "Gerar novo link" na linha | "Gerar novo link para {nome}", "O link anterior deixa de funcionar na hora, e o novo vale por 72 horas.", "Sua senha", "Gerar novo link" e "Cancelar" |
| (sem quadro) | vazio, `401`, `423`, `429`, `503`, rede | como no convidar (Q7). O quadro desenha a senha errada e o bloqueio só no convidar |
| `link-renovado` | `200 { link, expiraEm }` | o painel do link: "Novo link gerado para {nome}" e o e-mail da linha |
| (sem quadro) | `409` "Este usuário não tem convite pendente." | o convite foi aceito no meio. O painel fecha, a lista é relida, e aparece o aviso azul: "O convite de {nome} já foi aceito, então não há link a gerar. A lista foi atualizada." (Q7) |
| (sem quadro) | `404` | o convite foi cancelado no meio. O painel fecha e a lista é relida, sem aviso (Q7) |

**Cancelar convite** (`POST /users/{id}/invitation/cancel`, sem senha):

| estado do quadro | quando | a tela |
|---|---|---|
| `cancelar` | "Cancelar convite" na linha | na própria linha: "Cancelar o convite? O link deixa de funcionar.", "Cancelar convite" (perigo) e "Voltar" |
| (lista) | `204` | a lista relida, e a linha some |
| `convite-aceito` | `409` "O convite já foi aceito." | o aviso azul do quadro, "O convite de {nome} já foi aceito, então não há o que cancelar. A lista foi atualizada.", e a lista relida |
| (sem quadro) | `404` | a lista relida, sem aviso: o convite já tinha saído, por exemplo em outra aba |
| (sem quadro) | `429`, `503`, rede | o `failureNotice` na linha, e a confirmação continua aberta |

- **O aviso azul** (`role="status"`) fica no topo do cartão até a próxima ação.

### D4. A senha de quem chama: o `401` e o `423` no formulário, sem deslogar

As duas ações (convidar e gerar novo link) seguem o caminho herdado da #246 (Q0):

```
 POST com { senha } ──▶ sessionFetch
                          ├─ 401 com Content-Type application/problem+json ──▶ devolvido à tela
                          │      (sem renovar, sem repetir, sem encerrar) ──▶ "Senha inválida." no campo
                          ├─ 401 sem corpo (token) ──▶ renova uma vez e repete, como hoje
                          └─ 423 + Retry-After ──▶ aviso amarelo; botão desabilitado até passar
```

- **"Sua senha"** é o `CallerPasswordField`, um `PasswordField` com:
  - `autocomplete="current-password"`;
  - a ajuda do quadro ("Para convidar alguém, confirme que é você." ou "Para gerar um link, confirme que é você.");
  - o erro do campo.

  O `PasswordInput` direto reprova a suíte e o lint (#245 D6).
- **Vazia, não é enviada** (Q6). O servidor contaria a tentativa vazia no bloqueio do e-mail de quem chama (`CallerPassword.RejectAsync`).
- **O texto do `401` é o `title` do servidor, "Senha inválida."** (`CallerPassword.InvalidMessage`), igual ao do quadro.
- **O `423` vem antes da conferência da senha.** No convidar, vem antes até da validação dos campos (`InvitationEndpoints`).
- **O bloqueio é o do e-mail de quem chama**, o mesmo do login. O texto do quadro já fala em "a sua conta".
- **A senha só existe no estado do formulário.** Ela é limpa:
  - ao fechar;
  - no sucesso;
  - ao desmontar;
  - depois do `401`, com o foco de volta no campo, como "Senha atual" em Minha conta.

### D5. O link do convite: uma vez, copiável, só na memória

**O painel do link** (`link-gerado` e `link-renovado`):
- "Link do convite", com o link monoespaçado e "Copiar link";
- "Envie para a pessoa por e-mail, chat ou outro canal. Vale até **{expiraEm}** e funciona uma única vez.";
- o aviso amarelo "O Buteco não mostra este link de novo. Se ele se perder ou vencer, use **Gerar novo link** na linha do convite; o link anterior deixa de funcionar.";
- "Concluir".

**A data** sai do `expiraEm` da resposta, como `dd/mm/aaaa às HH:MM` no fuso do navegador, igual ao rodapé do `/convite` (#245 D5).

**Fora dos caches:**
- **as chamadas são diretas, sem `useMutation`.** É a lição da #246 (D5, 7.1): o `MutationCache` guarda o `data` até o `gcTime`, mesmo depois de desmontar, e o observador o guarda enquanto o componente está montado. O `link` e o `expiraEm` ficam só no estado do cartão;
- **o cache só recebe o que é público.** Depois do `201` e do `200`, a tela invalida o `GET /users`, sem `setQueryData` com a resposta.

**O link some:**
- no "Concluir";
- ao abrir outro painel no mesmo cartão;
- ao sair da página (desmontagem).

Recarregar também o perde, e isso é esperado: o texto diz que o Buteco não o mostra de novo.

**Nada em `sessionStorage`, `localStorage`, URL, `history.state` ou cache.** O token vai dentro do link, no fragmento (`/convite#…`), e a tela não o separa.

**"Copiar link"** usa o `CopyButton` do Mantine, já usado no painel:
- copia o `link` como veio;
- troca o rótulo por "Copiado ✓", como no quadro;
- sem `navigator.clipboard` (contexto inseguro), o link continua visível e selecionável.

### D6. Identidade

`GET /identity` → `{ emissor: {…}, chavesDeAssinatura: {…, chaves: [ … ] } }` (#211).

**Emissor do Buteco.** O selo "Operando" é fixo na resposta `200`: com o anel fora, o gate responde `503` antes da rota (#211).

| linha | fonte |
|---|---|
| Issuer, Discovery, Endpoint de token, Chaves públicas (JWKS) | `issuer`, `discovery`, `token`, `jwks`, monoespaçados, como vieram |
| Validade do token | "{validadeDoTokenSegundos} para usuários e módulos" (Q10) |
| Sessão do painel | "Renovada enquanto houver uso; termina depois de {sessaoSemAtividadeSegundos} sem atividade" |

**Chaves de assinatura:**
- na faixa, "Troca automática a cada {intervaloDeTrocaSegundos}";
- o texto do quadro, com `prePublicacaoSegundos` ("aparece 7 dias antes"), `retencaoSegundos` ("por 15 dias depois") e `verificacaoSegundos` ("em até 1 h").

**Durações:** `formatDuration(segundos)` usa a maior unidade que divide exatamente:
- dias ("1 dia", "N dias");
- horas ("N h");
- minutos ("N min");
- segundos ("N s"), quando nenhuma das anteriores divide.

Nenhum número da tela é escrito à mão. As constantes do emissor são a fonte, e um teste do `apps/api` amarra os dois lados (#211). Com os valores de hoje, a tela mostra "15 min", "14 dias", "90 dias", "7 dias", "15 dias" e "1 h".

**A tabela das chaves**, na ordem da resposta, que a tela não reordena:

| `estado` | selo | Próximo passo |
|---|---|---|
| `em-uso` | "Em uso" (verde) | "Assina desde {assinaDesde}; é substituída em {assinaAte}" |
| `proxima` | "Próxima" (azul) | "Já publicada; passa a assinar em {assinaDesde}" |
| `aposentando` | "Aposentando" (cinza) | "Não assina desde {assinaAte}; sai da publicação em {saiDaPublicacaoEm}" |
| desconhecido | neutro, com o valor cru | "—" (convenção 13) |

- **Os dois momentos do quadro** saem da mesma regra, só pela resposta:
  - `depois-da-troca`: em uso + aposentando;
  - `antes-da-troca`: em uso + próxima.
- **O momento sem quadro:** só a chave em uso, que é o caso na maior parte dos 90 dias. A tabela fica com uma linha.
- **As datas** (`criadaEm` e as do Próximo passo) saem como `dd/mm/aaaa`, no fuso do navegador (`formatDate`).

**A carga:**

| estado | quando | a tela |
|---|---|---|
| carregando (sem quadro) | o `GET` em voo | o `Skeleton` do cartão, como em Minha conta |
| `carga=erro` | `503`, rede ou outra falha | o cartão "Emissor do Buteco" com o selo amarelo "Indisponível", "Não foi possível carregar os dados do emissor agora. O painel tenta de novo sozinho; se continuar, confira se o api está no ar." e "Tentar de novo" |

**"Tenta de novo sozinho"** (Q9, convenção 20):
- é um `refetchInterval` por função pura exportada, `retryWhileFailing(query)`;
- devolve o `Retry-After` do `503` em ms, quando é legível, preso entre 1 e 30 s como no `queryRetryDelay` (`MIN_RETRY_AFTER_SECONDS`, `MAX_RETRY_AFTER_SECONDS`), ou 30 000 ms em qualquer outro erro;
- devolve `false` com dado e sem erro;
- o `retry` da consulta continua o do QueryClient do painel: até 3 tentativas, esperando o `Retry-After` no `503` (`app/queryClient.ts`, decisão P4 da #217). O cartão de erro aparece depois delas, e o polling vem em seguida;
- o polling para no `200` e ao sair da página (desmontagem).

### D7. Onde mora o código

```
features/users/
  api/usersApi.ts             request<T> da feature, GET /users, as três ações e os tipos
  pages/UsersPage.tsx         o título, o cartão Usuários e os dois cartões de texto
  components/
    UsersTable.tsx            as colunas do D2, com a linha marcada e a confirmação do cancelar
    InviteForm.tsx            convidar (D3)
    RenewInviteForm.tsx       gerar novo link (D3)
    InviteLinkPanel.tsx       o link uma vez, "Copiar link" e "Concluir" (D5)
    CallerPasswordField.tsx   "Sua senha": PasswordField, vazio e 401 (D4)
  usersTexts.ts               os textos do estado, do vencimento e dos avisos azuis
  test/fakeUsersApi.ts        o fetch falso por caminho
features/identity/
  api/identityApi.ts          request<T> da feature, GET /identity e os tipos
  pages/IdentityPage.tsx      a carga, o erro e o polling
  components/
    IssuerCard.tsx
    SigningKeysCard.tsx
  identityTexts.ts            formatDuration e o Próximo passo
  retryWhileFailing.ts        o intervalo do polling condicional (D6)
utils/failureNotice.ts        (de features/account/cardFailure.ts, D1)
utils/panelDates.ts           (de features/account/accountDates.ts, D1)
hooks/useBlockedUntil.ts      (de features/account/useBlockedUntil.ts, D1)
```

- **O `CallerPasswordField` fica na feature `users`.** Nesta change são dois consumidores, ambos dela. A #183 traz o terceiro ("Redefinir senha"), na mesma feature.

### D8. A linha sem "Editar" (divergência temporária)

- A coluna "Ações" continua no cabeçalho, porque o convidado tem ações. A célula dos ativos e dos desativados fica vazia.
- **Um guarda impede o link sem destino.** A lista não pode ter nenhum `href` para `/users/<id>` enquanto a rota não existir, e o caso diz isso no nome. A #183 o troca pelo caso do "Editar".
- **Na validação visual (convenção 14),** a falta do botão é conferida como divergência registrada, e não como defeito.

### D9. Testes e guardas

A suíte é a `frontend` (Vitest + Testing Library), com um `fetch` falso por caminho, no molde do `fakeAccountApi`. Cada guarda reintroduz só o defeito nomeado, é visto reprovar e volta pela cópia, com o sha conferido (convenção 15). Os casos estão no `tasks.md`, um por tarefa.

## Árvore de pastas proposta

```
apps/frontend/src/
├── app/
│   ├── routes.tsx                              (+ users, identity)
│   └── router.test.tsx                         (+ as duas exigem sessão)
├── components/layout/
│   ├── AppShell.tsx                            (seção "Acesso", D1)
│   └── AppShell.test.tsx                       (+ a seção e os dois itens)
├── hooks/
│   └── useBlockedUntil.ts                      (movido)
├── utils/
│   ├── failureNotice.ts                        (movido, checagem de forma, D1)
│   ├── failureNotice.test.ts                   (novo)
│   ├── panelDates.ts                           (movido, + formatShortDateTime)
│   └── panelDates.test.ts                      (movido de accountDates.test.ts, + o formato curto)
└── features/
    ├── account/                                (só importações, D1)
    │   ├── components/AccountCard.tsx, AccountDataCard.tsx, AccountPasswordCard.tsx,
    │   │   TwoFactorCard.tsx, AccountSessionsCard.tsx
    │   └── (cardFailure.ts, useBlockedUntil.ts, accountDates.ts e accountDates.test.ts saem)
    ├── users/                                  (novo, D7)
    │   ├── api/usersApi.ts, usersApi.test.ts
    │   ├── pages/UsersPage.tsx, UsersPage.test.tsx
    │   ├── components/UsersTable.tsx, InviteForm.tsx, InviteForm.test.tsx,
    │   │   RenewInviteForm.tsx, RenewInviteForm.test.tsx, InviteLinkPanel.tsx,
    │   │   InviteLinkPanel.test.tsx, CallerPasswordField.tsx
    │   ├── usersTexts.ts
    │   ├── callerPassword.ts                   (o hook do envio com a senha de quem chama; 5.1)
    │   ├── queryKeys.ts
    │   └── test/fakeUsersApi.ts, test/renderUsers.tsx
    └── identity/                               (novo, D7)
        ├── api/identityApi.ts                  (+ IDENTITY_QUERY_KEY)
        ├── pages/IdentityPage.tsx, IdentityPage.test.tsx
        ├── components/IssuerCard.tsx, SigningKeysCard.tsx
        ├── identityTexts.ts, identityTexts.test.ts
        └── retryWhileFailing.ts, retryWhileFailing.test.ts
```

Nada em `libs/`. Nada fora do `apps/frontend`.

## Tamanho

**Âncora** (convenção 18): a #246, medida em 07/11/2026 pelo `git diff --numstat 8884d89 a852946 -- apps/frontend/src`, só código:
- **produção:** +1.516 / −17, em 24 arquivos;
- **testes:** +1.439 / −2, em 15 arquivos, com 138 linhas de duplos.

A #246 errou **+20% na produção** (projeção ~1.260) e **−20% nos testes** (projeção ~1.670, sem os duplos).

**Projeção desta change**, com os duplos à parte:

| componente | produção | testes (casos) | duplos |
|---|---|---|---|
| `usersApi` (a cópia do `request<T>`, 4 chamadas, os tipos) | ~110 | ~110 | — |
| Usuários: página, tabela, carga e textos (D2) | ~230 | ~220 | — |
| convidar, gerar novo link, cancelar, `CallerPasswordField` e `InviteLinkPanel` (D3 a D5) | ~380 | ~520 | — |
| Identidade: durações, chaves, carga e polling (D6) | ~230 | ~260 | — |
| menu, rotas e a mudança de lugar (D1) | ~60 | ~70 | — |
| o `fetch` falso, o clipboard e os timers | — | — | ~120 |
| **total** | **~1.010** | **~1.180** | **~120** |

Com o erro da #246, a produção chega a ~1.210. Fica abaixo da âncora, como a Q1 previa.

## Risks / Trade-offs

- **[O link na memória do navegador]** → Um XSS na origem lê o link, como lê os tokens da sessão (#217 D2) e o segredo do TOTP (#246). A defesa é a CSP, e o link não vai para storage, URL ou cache (D5, com os guardas).
- **[O link perdido ao recarregar ou fechar o painel]** → É o desenho: o texto do quadro diz que o Buteco não o mostra de novo, e "Gerar novo link" invalida o anterior.
- **[A senha errada de quem chama pausa a conta inteira]** → É a regra do servidor (#181 P10). O `423` aparece no formulário, com o N do `Retry-After`. No desenvolvimento esse cabeçalho não fica legível (#246 D8), e o texto cai em "alguns minutos".
- **[A lista inteira relida a cada ação]** → Não há paginação na M1 (#219). A lista é de pessoas de uma instalação, e cada releitura é um `GET` pequeno.
- **[Polling da Identidade com o api fora]** → É um `GET` a cada 30 s, só com a página aberta e em erro. Ele para no `200` e ao sair (convenção 20).
- **[A linha sem "Editar" até a #183]** → Os ativos e os desativados ficam sem ação na lista. A edição continua só pela API até a #183, como hoje. O guarda do D8 impede um link sem destino.
- **[A mudança de lugar quebra Minha conta]** → Os testes de Minha conta rodam sem mudança de asserção. O relatório por nome, contra a baseline, confere que nenhum caso sumiu nem mudou de nome.

## Migration Plan

- **Deploy:** só a imagem do `frontend`. As rotas usadas estão em produção desde a #219, a #181 e a #211. Sem migration e sem variável nova.
- **Rollback:** a imagem anterior do `frontend`. Usuários e Identidade somem do menu, e nada muda no servidor.

## Divergências do protótipo (convenção 17)

**Temporária:** a linha da lista sem "Editar" (D8), resolvida pela #183.

**De texto, decididas pelo dono** (Q3 e Q10; a Q5 vale para a #183):

| quadro | o quadro diz | a tela diz | decisão |
|---|---|---|---|
| `Usuarios`, Último acesso | "agora", "há 2 h", "ontem", "há 41 dias" | "hoje, 14:15", "ontem, 18:40", `dd/mm/aaaa, HH:MM` | Q3 |
| `Provedores` `m1`, Validade do token | "15 min para usuários, aplicações e módulos" | "15 min para usuários e módulos" | Q10 |
| `UsuarioDetalhe`, link de redefinição (#183) | "Vale por 24 horas e uma única vez." | "Vale até {expiraEm} e funciona uma única vez." | Q5 |

**De comportamento, decididas pelo dono:** a linha nova depois do convite fica na ordem do servidor e marcada, e não no topo (Q8).

**Estados sem desenho, cada um com fonte no contrato** (a proposta está no D3 e no D6):
- "Sua senha" vazia (Q6);
- o `401` e o `423` em "Gerar novo link" (Q7);
- o `409` e o `404` em "Gerar novo link" (Q7);
- o `404` do cancelar;
- `429`, `503` e rede nas três ações;
- o carregando da Identidade;
- só a chave em uso.

**O que o quadro mostra sem fonte na rota:**
- "válido por 72 horas" (convidar) e "o novo vale por 72 horas" (renovar). Nenhuma rota de leitura traz essa duração antes do pedido, e a tela usa o texto do quadro. Depois do pedido, mostra o `expiraEm` real.
- "O painel tenta de novo sozinho" (Identidade): vira o polling condicional (Q9).

## Registros (convenção 23)

**Já publicados (07/11/2026):**
- a **#250** (esta), na M1, antes da #183, com o blocked-by da #246;
- a **#183**: o corpo reescrito para Editar usuário, o título "[feat] Painel: Editar usuário", o blocked-by da #250, o [registro da divisão](https://github.com/aldovrando-oliveira/buteco-agentes/issues/183#issuecomment-6710521021) e a [parte do design que fica com ela](https://github.com/aldovrando-oliveira/buteco-agentes/issues/183#issuecomment-6710514538);
- a **#194**: a lista com a #250 antes da #183, de 17 para 18 itens ([comentário](https://github.com/aldovrando-oliveira/buteco-agentes/issues/194#issuecomment-6710519644));
- a **#220**: "M1 · 18 de 18".

**No fechamento:**
- **na #183**, o que ela encontra pronto:
  - `failureNotice`, `useBlockedUntil` e `panelDates` no lugar comum;
  - o `CallerPasswordField`;
  - o `InviteLinkPanel`, que a redefinição pode reusar com o texto da Q5;
  - a linha sem "Editar", que ela completa, e o guarda do D8, que ela troca;
- **na #184**, que Usuários e Identidade abrem para todo usuário ativo na M1, e que a restrição ao Admin chega com ela.

## Decisões do dono (07/11/2026)

Na revisão do proposal e do design, todas como recomendadas:

| | pergunta | decisão | onde |
|---|---|---|---|
| Q1 | Um PR ou dividir? | **opção A, em dois PRs.** Esta change (#250): Usuários (lista, convidar, gerar novo link, cancelar), Identidade e a seção "Acesso". A #183: Editar usuário, o "Editar" na linha e o link de Minha conta, dependendo da #250. Na #250 a linha não mostra "Editar", como divergência temporária. A #194 vai de 17 para 18 itens | Context, D8 |
| Q2 | Como `cardFailure` e `useBlockedUntil` atendem dois `ApiError`? | vão para `utils/` e `hooks/`, e conferem os campos do erro, não a classe | D1 |
| Q3 | O formato do último acesso na lista | um formato só, o de Minha conta | D2 |
| Q4 | `/users/:id` de um convidado | redireciona para `/users` | #183 |
| Q5 | O texto da validade do link de redefinição | "Vale até {expiraEm}", da resposta | #183 |
| Q6 | "Sua senha" vazia | não é enviada; "Informe a sua senha." no campo | D3, D4 |
| Q7 | "Gerar novo link": os erros sem desenho | `401` e `423` como no convidar; `409` com o aviso azul e o texto ajustado; `404` relê a lista | D3 |
| Q8 | A linha nova depois do convite | na ordem do servidor, marcada enquanto o link estiver aberto | D3 |
| Q9 | "O painel tenta de novo sozinho" | polling só enquanto houver erro, a cada 30 s ou pelo `Retry-After` do `503`, parando no `200` (convenção 20) | D6 |
| Q10 | "15 min para usuários, aplicações e módulos" na M1 | "15 min para usuários e módulos" | D6 |

**As divergências de texto contra o protótipo** que estas decisões criam, listadas acima em "Divergências do protótipo":
- **Q3:** o último acesso da lista;
- **Q5:** a validade do link de redefinição, na #183;
- **Q10:** a validade do token.

## Open Questions

Nenhuma.
