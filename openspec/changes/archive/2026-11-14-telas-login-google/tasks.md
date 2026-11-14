Um guarda marcado **(guarda)** só conta depois de ter reprovado contra o defeito que ele existe para pegar (convenção 15). Para cada um:

1. o `~/.cache/buteco-agents/bin/guarda-190.sh` reintroduz o defeito nomeado. Ele é cópia do `guarda-188.sh` (o ramo `frontend`: os arquivos de teste do Vitest), com o mesmo lock das suítes. A mutação toca só o trecho nomeado e nunca restaura um diretório ao `HEAD`;
2. o teste roda e reprova;
3. a árvore volta pela cópia feita antes da mutação, com o sha conferido.

A tarefa registra o nome do teste, a mensagem da reprovação e o "voltou: <arquivo>", com o sha igual ao de antes. Uma mutação que quebra a compilação (o `tsc` do Vitest) e reprova por erro de tipo não vale: é descartada e refeita. As saídas ficam em `~/.cache/buteco-agents/190-guardas/<tarefa>`.

**As suítes** rodam pelo `~/.cache/buteco-agents/bin/suite-190.sh` (`frontend`), uma por vez, com o lock.
- Vale a regra assimétrica: uma rodada verde conta mesmo com a CPU caindo, e uma reprovação só conta com a CPU em 100 do início ao fim; com queda, é inconclusiva e se repete.
- Antes de cada rodada, o load de 1 e de 5 min fica abaixo de 6. Sob limite térmico, a variante `MAXWORKERS=4` (#220), registrada no `-estado.txt`.
- Um caso que **esta change modificou** e que reprova conta como "reprovada pelo <caso> (a → b), corrigida", mesmo com a CPU baixa, e não como inconclusiva.
- As intermitentes conhecidas são a #176 (`rt-inbox`) e a #232 (`apps/api`), que esta change não roda; uma intermitente nova do painel vira issue (convenção 23).
- Um build nunca roda junto de uma suíte.

**A baseline** está em `~/.cache/buteco-agents/190/baseline/`, em `0f44c10`, com a árvore limpa: `frontend` 2634/2634, 199 arquivos, CPU mínima 58, `APROVADA`, e o relatório por arquivo e nome em `nomes.txt`. Detalhes no design ("Baseline").

**Parada** (convenção 9):
- **parar** quando uma premissa falsa mudar comportamento ou contrato, ou quando a correção reduzir cobertura;
- **parar** quando a tela precisar de dado que o backend não serve (convenção 1);
- **seguir e registrar** quando a correção só acrescentar teste ou cenário para pôr um guarda no lugar certo, ou só tocar comentário ou documentação.

**Nada de commit** até o archive; um commit só, depois dele (decisão do dono, 14/11/2026).

**Só a 190a.** Minha conta, a confirmação com código, Editar usuário, Usuários e Identidade são da #279; nenhuma tarefa toca `features/account`, `features/users` nem `features/identity`.

**O pedido novo da lista dos provedores** (`GET /auth/federated/providers`) sai na montagem do `/entrar` (só com o contexto seguro, como o formulário) e do `/convite` (só depois da consulta válida do convite). **Casos que saem ou mudam de asserção** por causa dele, porque o requisito muda nesta change (delta `panel-federated-login` e `panel-access-link-pages`). Nenhum sai. Nenhuma asserção afrouxa: onde o caso conferia "nenhum pedido" ou a lista exata de pedidos, passa a conferir a lista exata **com** a lista dos provedores. Todo outro caso existente fica sem remoção, renome nem afrouxamento:
- `apps/frontend/src/features/auth/pages/EntrarPage.test.tsx`, **muda a lista esperada (exata)**:
  - `"Esqueceu a senha?" abre e fecha o texto na linha, sem enviar nada` e `e-mail ou senha vazios mostram "Informe e-mail e senha." sem enviar`: de `fetchMock` não chamado a "o único pedido é `GET /auth/federated/providers`";
  - `503 mostra "O painel está iniciando" e desabilita o Entrar até o Retry-After`: a contagem de `fetchMock` ganha o pedido da lista;
  - `a senha vai em JSON ao /auth/session, …`, `sessão completa com retorno fora da regra …`, `código certo segue o fluxo do emissor …` e `Continuar segue o fluxo do emissor …`: o `mock.calls[0]` passa a ser o pedido da lista, e a asserção confere a ordem `[lista, /auth/session, …]`;
  - **arranjo, nos 12 casos do `respond()`** (conferido em 14/11/2026, item 4 do dono): o `respond()` (l. 38–45) é `vi.fn(async () => response)` e devolve **a mesma instância** de `Response` a todo pedido. Não é uma fila, mas o corpo de uma `Response` só se lê uma vez: o pedido da lista, que sai na montagem, consumiria o corpo que o `/auth/session` lê depois, e o caso mudaria de sentido. O arranjo: o `respond()` passa a responder `GET /auth/federated/providers` com uma resposta própria (`{ "provedores": [] }`) e todo outro pedido com a resposta do caso, como hoje. As asserções ficam iguais (menos as já listadas acima, com a lista exata). Os casos:
    - `"Esqueceu a senha?" abre e fecha o texto na linha, sem enviar nada` (também na lista exata, acima);
    - `o POST /auth/session com 204, que não existe mais, cai no aviso genérico`;
    - `um segundo passo desconhecido não abre etapa nenhuma`;
    - `e-mail ou senha vazios mostram "Informe e-mail e senha." sem enviar` (também na lista exata, acima);
    - `401 mostra o title do servidor, continua no /entrar e não grava sessão`;
    - `403 mostra o title do servidor`;
    - `423 com Retry-After %s mostra o bloqueio com %s` (todas as linhas do `it.each`);
    - `429 mostra o texto de tentativas ao mesmo tempo`;
    - `503 mostra "O painel está iniciando" e desabilita o Entrar até o Retry-After` (também na contagem, acima);
    - `falha de rede mostra que não foi possível falar com o servidor` (a lista também falha por rede e some, Q5);
    - `sem Web Locks o formulário não envia, e o aviso de HTTPS aparece` (**sem mudança de asserção, conferido:** fora do contexto seguro a lista não é pedida, e o `not.toHaveBeenCalled` continua exato);
    - `some ao enviar, e o resultado do envio segue as regras`;
- `apps/frontend/src/features/auth/pages/EntrarPage.test.tsx`, bloco `passo das 2 etapas`, **arranjo** (acrescentados no apply, decisão do dono de 14/11/2026): o `respondCode()` (l. 378–389) é uma **fila**, e o pedido da lista consumiria a resposta do código; o arranjo responde a lista à parte, a fila fica só para o código, e as asserções ficam iguais:
  - `código certo com retorno do authorize segue o retorno`;
  - `código errado aparece no campo, com aria-invalid, e a tela continua no passo`;
  - `entrada expirada volta a e-mail e senha, com o aviso em role="status"`;
  - `outro texto de 401 não é tomado por entrada expirada`;
  - `bloqueio no passo mostra os minutos do Retry-After`;
  - `muitas tentativas desabilita Verificar até o Retry-After`;
  - `painel iniciando no passo desabilita Verificar até o Retry-After`;
  - `chave trocada mostra o texto do servidor em role="alert"`;
  - (o `código certo segue o fluxo do emissor com o voltarPara da URL`, já acima, também passa pelo `respondCode()`);
- `apps/frontend/src/features/auth/pages/AccessLinkPage.test.tsx`, ramo `redefinir-senha`, **lista exata, mais estrita** (acrescentado no apply): `o token nunca vai numa URL` segue até o `/entrar`, que pede a lista; a lista exata de pedidos ganha o `GET /auth/federated/providers` na posição dele;
- `apps/frontend/src/features/auth/pages/AccessLinkPage.test.tsx`, ramo `convite`, **lista exata, mais estrita** (acrescentado no apply, decisão do dono de 14/11/2026): `o StrictMode não perde o token` deixa o "todo pedido leva o token" e passa a conferir a lista exata de pedidos, na ordem: a consulta com `{ token: 'k7Qp2vX9' }`, o `GET /auth/federated/providers` sem corpo, e nenhum outro; mais: nenhuma URL de pedido contém o token, e a chave continua gravada. Um pedido a mais reprova;
- `apps/frontend/src/features/auth/pages/AccessLinkPage.test.tsx`, só no ramo `convite` do `describe.each`, **arranjo e lista esperada**: a fila do `respondByPath` ganha a resposta da lista, e `o endereço troca antes do primeiro pedido, …`, `o token nunca vai numa URL`, `recarregar reaproveita o token guardado`, `senha curta não envia`, `senha longa demais não envia`, `espaços contam como caracteres`, `senhas diferentes não enviam` e `entra com o e-mail da consulta e chega ao /entrar na etapa do cadastro, …` passam a contar o pedido da lista na posição dele (o `o token nunca vai numa URL` do `convite` também segue até o `/entrar`, que pede a lista de novo); **sem mudança, conferido:** `sem token não há pedido, e a página mostra o link inválido` (sem consulta válida, nada de lista);
- `apps/frontend/src/features/auth/loginFlow.integration.test.tsx`: **a conferir no apply**: os dois casos usam `arrayContaining` e devem passar sem mudança; se o duplo deles reprovar o pedido da lista, o arranjo ganha a resposta, e a asserção fica igual.

Cada um é listado por nome na comparação da 9.3, com o requisito que o mudou. **Um caso fora desta lista que precise sair, mudar de nome ou afrouxar é parada.**

## 0. Preparação

- [x] 0.1 (scripts locais) `~/.cache/buteco-agents/bin/guarda-190.sh`, cópia do `guarda-188.sh` com o cabeçalho da #190, e o lock das suítes. Conferir com uma mutação nula (recusa com "defeito não aplicado") e uma real em `apps/frontend/src` (o arquivo volta com o mesmo sha).
- [x] 0.2 Reler, no painel, o `EntrarPage`, o `EnrollmentForm`, o `SecondFactorForm`, o `AccessLinkPage`, o `linkToken.ts`, o `entryNotices.ts` e os duplos de `fetch` dos três arquivos de teste da lista do topo, e registrar no design qualquer diferença do D1 ao D3 (convenção 6).

## 1. A API federada do painel (`panel-federated-login`, `panel-access-link-pages`; D1 a D3)

- [x] 1.1 `features/auth/api/federatedApi.ts`: `listLoginProviders()` (`GET /auth/federated/providers`), `startFederatedLogin(chave, retorno)`, `startFederatedInvitation(chave, token)` e `readRefusalNotice()` (`POST /auth/federated/refusal`), com o `Origin` e o `Content-Type` dos pedidos de hoje, e o resultado tipado (`ok`, `http` com o `status` e o `title`, `network`). O mapa dos tipos conhecidos (`google` → "Google"). Nenhum token nem e-mail em URL ou log.
- [x] 1.2 Casos (`federatedApi.test.ts`): os corpos e os caminhos de cada pedido; o `404` vazio da rota do aviso; a falha de rede; o tipo desconhecido fora do mapa.
- [x] 1.3 O duplo: as rotas federadas no apoio dos testes do `auth` (a lista, os dois inícios e a rota do aviso de **uso único**: a segunda chamada responde `404`). Contado como duplo.

## 2. O login pelo provedor (`panel-federated-login`, "Os botões" e "O início"; D1)

- [x] 2.1 O `/entrar` lê a lista na montagem (com o contexto seguro) e mostra "ou entre com" e "Entrar com Google" abaixo do formulário; o tipo desconhecido não ganha botão; a lista falhando esconde o bloco, sem aviso.
- [x] 2.2 O início: o `POST` com o `retorno` conferido (ou `null`); "Redirecionando para o Google…" sem "Cancelar"; a navegação; o `503`; o `404` separado pelo `title` exato ("Provedor não configurado." → o aviso e a lista relida; qualquer outro `404` → o aviso da falha, sem reler); o `429`; a rede e o resto, com os avisos do D1. A lista fora do formato (o `200` sem `provedores`, ou com `provedores` que não é lista, ou com itens sem `chave` ou `tipo` em texto) cai na Q5.
- [x] 2.3 A lista do topo: os casos do `EntrarPage.test.tsx` com a lista exata nova, e o arranjo. Nenhum outro caso muda.
- [x] 2.4 Casos: um por cenário de "Os botões dos provedores no /entrar" e de "O início do login pelo provedor".
- [x] 2.5 **(guarda)** "Tipo desconhecido não ganha botão". **Defeito:** um botão por provedor da lista, sem o mapa dos tipos. **Registro (14/11/2026):** reprovou: `o tipo desconhecido não ganha botão` ("expected [ <button …>, … ] to have a length of 1 but got 2") e `listLoginProviders > pede GET …` (deep equal); voltou: `api/federatedApi.ts`, sha `0af862b7` igual.
- [x] 2.6 **(guarda)** "A lista falhando esconde o bloco sem aviso". **Defeito:** a falha da lista vira um aviso vermelho no `/entrar`. **Registro (14/11/2026):** reprovou: os cinco `a lista falhando (500 / a rede) …` e `a lista fora do formato (…) esconde o bloco sem aviso`; voltou: `pages/EntrarPage.tsx`, sha `60a8f14d` igual.
- [x] 2.7 **(guarda)** "Redirecionando para o Google": o corpo leva o `retorno` conferido. **Defeito:** o início sempre com `{ retorno: null }`. **Registro (14/11/2026):** reprovou: `redirecionando para o Google, com o retorno do authorize no corpo` ("expected { retorno: null } to deeply equal { Object (retorno) }"); voltou: `pages/EntrarPage.tsx`, sha `60a8f14d` igual.

## 3. A volta do provedor (`panel-federated-login`, "A volta"; D1)

- [x] 3.1 O `/entrar` lê `federado`, `segundoPasso` e `convite` da query: a etapa do código ou do cadastro, com os três avisos verdes do D1 (o do convite aceito sozinho); com `convite=aceito`, apaga `buteco.link.convite` antes de abrir o cadastro; depois de ler, `navigate(…, { replace: true })` sem `federado`, `segundoPasso` e `convite`, com o `retorno`.
- [x] 3.2 Casos: um por cenário de "A volta do provedor no /entrar".
- [x] 3.3 **(guarda)** "Volta do Google para o código": o endereço perde `segundoPasso`. **Defeito:** o `segundoPasso` lido e não removido da URL (sem o `replace`). **Registro (14/11/2026):** reprovou: `volta do Google para o código: … o endereço sem federado e segundoPasso` ("Received: /entrar?segundoPasso=totp&retorno=…") e mais quatro da volta; voltou: `federatedReturn.ts`, sha `5838ed05` igual.
- [x] 3.4 **(guarda)** "Convite aceito pelo Google": a chave não existe quando o cadastro aparece. **Defeito:** o `/entrar` não apaga `buteco.link.convite` no `convite=aceito` (o token sobra na aba depois do aceite). **Registro (14/11/2026):** reprovou: `convite aceito pelo Google: a chave sai antes do cadastro, …` ("expected 'k7Qp2vX9' to be null"); voltou: `pages/EntrarPage.tsx`, sha `60a8f14d` igual.
- [x] 3.5 **(guarda)** "Convite aceito pelo Google": o aviso é só o do convite. **Defeito:** os dois avisos somados (o do convite e o da volta para o cadastro). **Registro (14/11/2026):** reprovou: `convite aceito pelo Google: …, e o aviso é só o do convite` ("Unable to find an element with the text: Convite aceito. …"); voltou: `federatedReturn.ts`, sha `5838ed05` igual.

## 4. As recusas no login (`panel-federated-login`, "As recusas" e "O e-mail confirmado"; D2)

- [x] 4.1 O aviso por motivo, com a cor, o título, o texto e o rótulo do botão da tabela do D2; o motivo desconhecido como a `falha`; a limpeza da query com o `retorno`.
- [x] 4.2 A leitura do e-mail no `sem-acesso`: uma chamada só por montagem, com guarda contra o efeito duplo do `StrictMode` (um `ref` de uso único, não um estado); o `404` e a rede na variante sem o e-mail; os outros motivos sem chamada.
- [x] 4.3 Casos: um por cenário de "As recusas no /entrar" e de "O e-mail confirmado da recusa é lido uma vez", os de `StrictMode` montados sob `<StrictMode>`.
- [x] 4.4 **(guarda)** "Uma chamada só no StrictMode". **Defeito:** a guarda do `StrictMode` retirada (a segunda chamada responde `404` e apaga o e-mail). **Registro (14/11/2026):** reprovou: `uma chamada só no StrictMode, e o aviso mostra o e-mail` (o e-mail não aparece: a segunda chamada responde 404); voltou: `federatedHooks.ts`, sha `8724f5d6` igual.
- [x] 4.5 **(guarda)** "Motivo desconhecido sai como a falha". **Defeito:** o mapa dos motivos sem o caso padrão (o motivo desconhecido não mostra aviso). **Registro (14/11/2026):** reprovou: `motivo desconhecido sai como a falha, e não como tela sem aviso` ("Unable to find role=\"alert\""); voltou: `federatedNotices.ts`, sha `9fff4b1a` igual.
- [x] 4.6 **(guarda)** "A limpeza mantém o retorno". **Defeito:** o `replace` da limpeza sem o `retorno`. **Registro (14/11/2026):** reprovou: `a limpeza mantém o retorno, e recarregar abre o formulário sem o aviso` ("expected '/entrar' to be '/entrar?retorno=…'") e mais dois; voltou: `federatedReturn.ts`, sha `5838ed05` igual.
- [x] 4.7 **(guarda)** "Os outros motivos não leem o aviso". **Defeito:** a rota do aviso chamada em todo motivo. **Registro (14/11/2026):** reprovou: `os outros motivos não leem o aviso` ("expected [ '/auth/federated/providers', …(1) ] to deeply equal [ '/auth/federated/providers' ]") e os cinco motivos; voltou: `pages/EntrarPage.tsx`, sha `60a8f14d` igual.
- [x] 4.8 **(guarda)** "O aviso de outra recusa não empresta o e-mail" (revisão do dono, 14/11/2026, no apply: o `readRefusalEmail()` devolve `{ motivo, provedor, email }`, e o `useRefusalEmail` confere o motivo e, quando a query o trouxe, o provedor). **Defeito:** o e-mail usado sem conferir o motivo. **Registro (14/11/2026):** reprovou: `o aviso de outra recusa não empresta o e-mail` (`/entrar`; "sem aviso com \"Essa conta Google não tem acesso ao Buteco Agentes. …\"") e `o aviso de outra recusa não empresta o e-mail do provedor` (`/convite`); voltou: `federatedHooks.ts`, sha `d1178f56` igual. Depois da mudança do hook, a 4.4 (reprovou de novo em `uma chamada só no StrictMode, e o aviso mostra o e-mail`, sha `d1178f56` igual) e a 6.8 (reprovou de novo nos sete casos, sha `18c4faef` igual) rodaram de novo.

## 5. O convite com o provedor (`panel-access-link-pages`, "O convite com o provedor" e o token; D3)

- [x] 5.1 Depois da consulta válida, a lista; "Entre com uma conta Google", "Continuar com Google", a nota do e-mail e "ou crie uma senha"; o cabeçalho sem o papel; sem provedor ou com a lista falhando, a página de hoje.
- [x] 5.2 O início do convite: o `POST` com o token do estado no corpo; **a chave `buteco.link.convite` regravada com esse token antes do `location.assign`, sempre**; "Redirecionando para o Google…"; as respostas do D3, com os três `404` separados pelo `title` exato (o do link inválido → o cartão e a chave apagada; "Provedor não configurado." → o aviso e a lista relida; qualquer outro → o aviso da falha, sem apagar a chave e sem reler); o `503`; o `429`; a rede. A lista fora do formato cai na página de hoje.
- [x] 5.3 A lista do topo: os casos do `AccessLinkPage.test.tsx` (ramo `convite`) com a resposta da lista e a lista exata nova. Nenhum outro caso muda.
- [x] 5.4 Casos: um por cenário de "O convite com o provedor" e os quatro novos de "O token sai do endereço…" (com a "Segunda tentativa depois de uma recusa").
- [x] 5.5 **(guarda)** "A ida ao provedor não apaga o token". **Defeito:** a chave apagada antes da navegação ao `url`. **Registro (14/11/2026):** reprovou: `a ida ao provedor não apaga o token` ("expected null to be 'k7Qp2vX9'"); voltou: `pages/AccessLinkPage.tsx`, sha `cd143324` igual.
- [x] 5.6 **(guarda)** "O token não vai para a URL nem para o log". **Defeito:** (a) o token anexado à query do início (`?token=`); (b) um `console.info` com o token no clique do provedor. **Registro (14/11/2026):** (a) reprovou: `o token não vai para a URL nem para o log` ("expected '/auth/invitation/federated/corporativ…' not to contain 'k7Qp2vX9'") e `o do convite leva o token só no corpo`; voltou: `api/federatedApi.ts`, sha `0af862b7` igual. (b) reprovou: `o token não vai para a URL nem para o log` ("expected '[\"início do convite pelo provedor\",\"k…' not to contain 'k7Qp2vX9'"); voltou: `pages/AccessLinkPage.tsx`, sha `cd143324` igual.
- [x] 5.7 **(guarda)** "Segunda tentativa depois de uma recusa": no instante da navegação, a chave existe com o mesmo token. **Defeito:** o clique do provedor não regrava a chave (só a gravação ao abrir o link). **Registro (14/11/2026):** reprovou: `segunda tentativa depois de uma recusa: a chave regravada com o mesmo token na navegação` ("expected null to be 'k7Qp2vX9'"); voltou: `pages/AccessLinkPage.tsx`, sha `cd143324` igual.
- [x] 5.8 **(guarda)** "404 sem o title conhecido no convite": a chave continua. **Defeito:** todo `404` do início tratado como link inválido (o que apaga a chave). **Registro (14/11/2026):** reprovou: os dois `404 sem o title conhecido no convite (sem corpo / com outro title): a falha, a chave continua, …` ("Unable to find role=\"alert\""); voltou: `pages/AccessLinkPage.tsx`, sha `cd143324` igual.

## 6. As recusas que voltam ao convite (`panel-access-link-pages`, "As recusas que voltam ao convite"; D3)

- [x] 6.1 Com o token na chave: a consulta, o aviso da tabela do D3 e o formulário; o `outro-email` com o e-mail do provedor pela rota do aviso (uma chamada, a mesma guarda do D2); o `convite-invalido` no cartão de hoje; a chave apagada depois da consulta (e da leitura do aviso); o token do formulário só no estado.
- [x] 6.2 Sem o token na chave: "Convite", o aviso (o do `outro-email` sem e-mail), "Abra o link do convite que você recebeu de novo.", sem formulário e sem pedido.
- [x] 6.3 A limpeza da query (`recusa`, `provedor`) por `replace`; recarregar depois disso cai no estado de link inválido de hoje, sem pedido (decisão do dono, 14/11/2026).
- [x] 6.4 Casos: um por cenário de "As recusas que voltam ao convite". Com os dois cenários novos do apply ("O novo clique troca o aviso da recusa pelo do início" e "Toda resposta recusada do início troca o aviso da recusa"), sete casos.
- [x] 6.5 **(guarda)** "A recusa apaga o token". **Defeito:** a chave não é apagada na volta da recusa (o token sobra na aba depois da recusa). **Registro (14/11/2026):** reprovou: `a recusa apaga o token, e o formulário continua` ("expected 'k7Qp2vX9' to be null") e mais dois; voltou: `pages/AccessLinkPage.tsx`, sha `cd143324` igual.
- [x] 6.6 **(guarda)** "Outro e-mail sem o token": nenhum pedido sai. **Defeito:** a página faz a consulta mesmo sem o token (com o token vazio). **Registro (14/11/2026):** reprovou: `outro-email sem o token: nenhum pedido, …` e `falha sem o token: …` ("Unable to find … heading … Convite"); voltou: `pages/AccessLinkPage.tsx`, sha `cd143324` igual.
- [x] 6.7 **(guarda)** "Uma leitura só do aviso no convite". **Defeito:** a guarda do `StrictMode` retirada no convite. **Registro (14/11/2026): mutante equivalente**, decisão do dono. A mutação passou nos 30 casos (voltou `federatedHooks.ts`, sha `8724f5d6` igual) e numa sonda do clique com `503` depois da recusa `outro-email` (uma chamada à rota do aviso; voltou com o mesmo sha). No convite o hook só liga depois da consulta, quando a fase vira `formulario`, nunca na montagem, e não religa porque a fase continua `formulario`. A guarda do hook, que o login e o convite compartilham, continua provada pela 4.4; o caso "uma leitura só do aviso no convite, no StrictMode" fica como asserção sem guarda. Não entra na contagem.
- [x] 6.8 **(guarda)** "O novo clique troca o aviso da recusa pelo do início" (decisão do dono, 14/11/2026, no apply: o clique do provedor esconde o aviso da recusa, como o `recusaVisivel` do `/entrar`; regra e dois cenários na spec, sete casos). **Defeito:** o clique não esconde o aviso da recusa, e os dois avisos aparecem somados. **Registro (14/11/2026):** reprovou: `o novo clique troca o aviso da recusa pelo do início` ("expected [ <div …>, …(1) ] to have a length of 1 but got 2") e os seis `a recusa … some com … do início, e só o aviso dele fica`; voltou: `pages/AccessLinkPage.tsx`, sha `75c400ef` igual.

## 7. Docs

- [x] 7.1 `01-ARQUITETURA_E_CONVENCOES.md`, seção "Autenticação": o painel com o login pelo Google (os botões pela rota anônima, a volta pela query e a limpeza, as recusas, a leitura única do aviso) e o convite com provedor (o token em `buteco.link.convite`, apagado no aceite e na recusa).
- [x] 7.2 `docs/deployment.md`, na seção dos provedores federados: a M3 passa a ter tela para entrar e aceitar convite pelo Google; Minha conta, Usuários e Identidade são da #279.
- [x] 7.3 Antes do archive: `02-HISTORICO_E_STATUS.md` (a entrada da change, a fila da M3 com a #279, o checklist visual da 190a) e `CHANGELOG.md`, com a medição da convenção 18 contra a projeção do design, em colunas separadas para produção, testes, duplos e casos.

## 8. Lista dos guardas (conferência)

| tarefa | caso | defeito |
|---|---|---|
| 2.5 | tipo desconhecido não ganha botão | um botão por provedor, sem o mapa dos tipos |
| 2.6 | a lista falhando esconde o bloco sem aviso | a falha vira aviso vermelho |
| 2.7 | o início leva o `retorno` conferido | o início sempre com `retorno: null` |
| 3.3 | a volta tira o `segundoPasso` da URL | o `segundoPasso` lido e não removido |
| 3.4 | o convite aceito apaga o token antes do cadastro | o token sobra na aba depois do aceite |
| 3.5 | só o aviso do convite aceito | os dois avisos somados |
| 4.4 | uma chamada só no `StrictMode` (login) | a guarda retirada: a segunda chamada apaga o e-mail |
| 4.5 | o motivo desconhecido sai como a falha | o motivo desconhecido sem aviso |
| 4.6 | a limpeza mantém o `retorno` | o `replace` sem o `retorno` |
| 4.7 | os outros motivos não leem o aviso | a rota do aviso em todo motivo |
| 4.8 | o aviso de outra recusa não empresta o e-mail | o e-mail usado sem conferir o motivo |
| 5.5 | a ida ao provedor não apaga o token | a chave apagada antes da navegação |
| 5.6 | o token não vai para a URL nem para o log | (a) `?token=` no início; (b) `console.info` com o token |
| 5.7 | a segunda tentativa leva a chave regravada | o clique do provedor não regrava a chave |
| 5.8 | o `404` sem o `title` conhecido não apaga a chave | todo `404` do início tratado como link inválido |
| 6.5 | a recusa apaga o token | o token sobra na aba depois da recusa |
| 6.6 | sem o token, nenhum pedido | a consulta com o token vazio |
| ~~6.7~~ | uma leitura só do aviso no convite | a guarda retirada no convite: **mutante equivalente** (o hook do convite só liga depois da consulta e não religa); fora da contagem, a guarda segue provada pela 4.4 |
| 6.8 | o novo clique troca o aviso da recusa pelo do início | o clique não esconde o aviso da recusa: os dois avisos somados |

São 18 guardas e 19 mutações (a 6.7 saiu como mutante equivalente, e a 6.8 e a 4.8 entraram, decisões do dono de 14/11/2026, no apply). Os mínimos do dono (14/11/2026): o e-mail apagado pela segunda chamada do `StrictMode` (4.4; a 6.7 é equivalente no convite), o token que sobra na aba depois do aceite ou da recusa (3.4 e 6.5), o motivo desconhecido sem aviso (4.5), o `segundoPasso` lido e não removido (3.3), e o token na URL ou num log (5.6). Os da revisão de 14/11/2026: a segunda tentativa (5.7) e o `404` sem o `title` conhecido (5.8).

## 9. Fechamento

- [x] 9.1 `npm run lint` e `npm run build` (o `tsc -b` e o `vite build`) no `apps/frontend`, sem aviso novo contra a baseline; o Prettier nos arquivos tocados que já estavam formatados no `main` (a regra da #185); `openspec validate telas-login-google --strict`. **Registro (14/11/2026, refeito depois da 4.8):** `npm run lint` sem aviso na baseline e na branch; `npm run build` com o mesmo aviso de chunk acima de 500 kB nas duas (já existia; o `index` vai de 1.219,34 kB para 1.229,16 kB); `prettier --check` verde nos arquivos novos e no `SecondFactorForm.tsx`; `openspec validate telas-login-google --strict` válido. Saídas em `~/.cache/buteco-agents/190/lint-build/`.
- [x] 9.2 A suíte pelo `suite-190.sh`, em `~/.cache/buteco-agents/190/fechamento/`, com o `MAXWORKERS` padrão (ou `4` sob limite térmico, registrado). **Registro (14/11/2026, refeito depois da 4.8):** `frontend` 2733/2733, 202 arquivos, 152 s, `MAXWORKERS` padrão, CPU mínima 65 (32 amostras), `APROVADA`. Em `~/.cache/buteco-agents/190/fechamento-r2/`. A rodada anterior (2726/2726, CPU mínima 55, `APROVADA`) ficou em `fechamento/`.
- [x] 9.3 Comparação por arquivo e nome contra a baseline (o `compare-217.py` no JSON do Vitest). Nenhum caso some nem muda fora da lista do topo, e cada caso novo fica ligado ao requisito. **Registro (14/11/2026, refeito depois da 4.8):** baseline 2634, fechamento 2733; ausentes 0; novos 99, todos nos três arquivos novos (`federatedApi.test.ts` 20, `EntrarFederated.test.tsx` 40, `ConviteFederated.test.tsx` 39), cada `describe` com o nome do requisito; nenhum caso dos arquivos existentes sumiu ou mudou de nome. Relatório em `~/.cache/buteco-agents/190/fechamento-r2/nomes.txt`.
- [x] 9.4 O design corrigido com toda divergência achada no apply (convenção 9), numa seção "O que o apply mudou".

## 10. Validação visual e conferência real (homologação da M3, depois do merge; não prendem o PR)

**Validação visual** (a regra da M1 e da M2), quadro a quadro contra a versão `1794626941-64ac` (a 77). O mesmo checklist vai no `02` e no PR:

- [ ] 10.1 **`Main` (`entrega=m3`):** `inicio` com "ou entre com" e "Entrar com Google"; `redirecionando`; `recusa-sem-acesso` e `recusa-sem-acesso-sem-email`; `recusa-convite-pendente`; `recusa-nao-conectada`; `recusa-desativado`; `recusa-email-nao-verificado`; `recusa-dominio-nao-permitido`; `recusa-falha`; `recusa-desconhecida`; `google-indisponivel`; `google-nao-configurado`; `google-muitas-tentativas`; `google-falha-de-rede`; `google-outra-resposta`; `mfa-vindo-do-google`; `cadastro-vindo-do-google`; `cadastro-convite-aceito`.
- [ ] 10.2 **`LoginRecusado`:** os oito cartões (1, 1b, 2 a 7), com as cores e o rótulo do botão.
- [ ] 10.3 **`AceitarConvite` (`entrega=m3`):** `escolha`; `redirecionando`; `provedor-indisponivel`; `provedor-nao-configurado`; `provedor-muitas-tentativas`; `provedor-falha-de-rede`; `provedor-outra-resposta`; `recusa-outro-email`; `recusa-email-nao-verificado`; `recusa-em-uso`; `recusa-dominio-nao-permitido`; `recusa-falha`; `recusa-convite-invalido`; `recusa-sem-token-outro-email`; `recusa-sem-token-falha`.
- [ ] 10.4 **Os dados na aba**, pelas ferramentas do navegador (Armazenamento): `buteco.link.convite` some depois do aceite (antes do cadastro) e de cada recusa; nenhum token na URL em nenhum momento.

**Conferência com o Google real**, com o roteiro da #189 (13.2 a 13.8 do `tasks.md` de `2026-11-13-provedores-federados-google`) e da #274 (10.2 a 10.5 do `tasks.md` de `2026-11-14-aceite-convite-google`), **agora pelo painel**:

- [ ] 10.5 O login de uma conta conectada pelo botão "Entrar com Google": a volta no passo do código com o aviso verde, o código, o painel. Em **Chrome, Firefox e Safari**: o `buteco-sessao` gravado na resposta do retorno e indo no `POST` do segundo passo; o `buteco-recusa` indo no `POST /auth/federated/refusal` do `/entrar` e do `/convite`.
- [ ] 10.6 As recusas reais que se montam com as contas de teste: `sem-acesso` (com o e-mail, e sem ele depois de recarregar), `nao-conectada`, `convite-pendente`, `falha` (cancelar no Google).
- [ ] 10.7 O aceite pelo painel: o e-mail certo (o cadastro com "Convite aceito…"), outro e-mail (o aviso com os dois e-mails e o aceite por senha em seguida), cancelar no Google, o convite cancelado no meio.

Achado de homologação vira issue nova ligada à entrega (convenção 23).
