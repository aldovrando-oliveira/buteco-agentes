**Issue:** #281 (bug do `apps/api`, achado no design da `telas-contas-federadas`, Q10). Sem milestone: saiu da M3
para o backlog em 17/11/2026 e voltou à fila no mesmo dia, por decisão do dono, para ser corrigida antes da M4
(#197). Envolvidas, sem reabrir (convenção 23): #189 (o retorno do cliente OIDC), #276 (a registração que saiu da
configuração) e #279 (a tela que mostra a recusa em `/account`).

## Why

Duas falhas do **conectar** (a intenção `conectar`, que começa em `POST /account/federated/{chave}/connect`) voltam
ao **login**, `302 /entrar?recusa=falha`, e não a Minha conta. A pessoa está logada no painel e lê, no `/entrar`,
"Não foi possível entrar com o Google. Tente de novo.", sobre uma ação que era conectar a conta.

1. **Nenhum provedor configurado na volta** (o único provedor saiu da configuração, com reinício, entre o início e
   a volta): `Identity/Federated/FederatedLoginEndpoints.cs` l. 133–137 responde antes de ler o state. Não há o que
   ler: sem provedor, o cliente OIDC nem é registrado (`IdentityIssuerServiceCollectionExtensions.cs` l. 159–165), e
   o esquema que o `AuthenticateAsync` da l. 139 pede não existe.
2. **O state sem as propriedades** (o state ausente, desconhecido, vencido ou já usado): o cliente OIDC recusa
   antes de restaurar as propriedades do Buteco, a intenção chega nula, e o `FailureRefusal` (l. 170–175) cai no
   ramo padrão, o do login.

O caso 1 é o que a #281 tem de real: é o mesmo caso da chave trocada (#276), que já volta a `/account`, só que com
zero provedores. O caso 2 só acontece com o state que o próprio cliente OIDC não reconhece, e ali a intenção não
tem fonte confiável (design, D2).

## What Changes

**`apps/api` — o caso 1 (design, D1):** o cliente OIDC passa a ser registrado **sempre**, com ou sem provedor. Sem
provedor, ele não tem registração nenhuma, e o retorno segue o caminho da chave trocada: o cliente confere o state
(cifrado e assinado pelo anel, que existe sem provedor), restaura as propriedades, resgata o state, e o
`FederatedMissingRegistrationHandler` recusa por falta da registração. A rota perde o desvio das l. 133–137 e
responde pela intenção do state: `/account?recusa=falha&provedor=<chave>` no conectar, `/entrar?recusa=falha&provedor=<chave>`
no login e `/convite?recusa=falha&provedor=<chave>` no aceite (Q1, decisão do dono, 17/11/2026). O log `1199` continua dizendo `sem_provedor_configurado` quando o catálogo está
vazio, agora com a chave do state. A checagem de boot do cliente (`IdentityIssuerStartupValidation.cs` l. 188) deixa
de depender do catálogo.

**`apps/api` — o caso 2 (design, D2; Q2, decisão do dono, 17/11/2026):** **não corrigir**. A intenção desconhecida continua indo ao
`/entrar?recusa=falha`, agora como regra escrita: o cookie do fluxo não traz a intenção (é só correlação, gravado
pelo OpenIddict e sem proteção), e as outras fontes custam um leitor próprio do state ou uma URI de retorno nova no
Google. A regra fica na spec, com um cenário por sub-caso: o state ausente, forjado, vencido e já usado.

**A segurança (design, D3):** a intenção sai só do state token do Buteco; o destino é um de três caminhos fixos (`/entrar`, `/account`, `/convite`, com `?recusa=falha`),
relativos, com a chave escapada; nenhum vínculo, sessão ou cookie novo; o log sem dado pessoal; o state forjado ou
ausente cai no `/entrar`.

**Os guardas (design, D4):** o caso 1 voltando ao `/entrar`, nas duas formas do defeito (o desvio da rota de volta e
o registro condicional do cliente de volta); a intenção desconhecida ou forjada indo ao `/account`; e um destino fora
dos fixos.

**A tarefa 0 (design, D0; condição do dono, 17/11/2026):** antes de qualquer código da correção, uma sonda
descartável prova as três premissas da A: o caso real de ponta a ponta no pod da 10.1 da #274 (o state decifrado
depois do reinício sem o provedor); nada muda sem provedor (o boot sem pedido de rede, a checagem de boot, o
`providers` vazio, a lista de endpoints igual antes e depois); e o guarda da #285 continua reprovando contra o
defeito dele, ajustado para os dois hosts. Qualquer uma que falhe é parada, de volta ao dono com a B. Os guardas
dessa tarefa entram na change (o boot fazendo pedido de rede, ou expondo rota nova).

**As conferências:** local, com o duplo do Google e um banco descartável, os dois casos por `curl` (D5); e na
homologação, depois do merge, o caso 1 com o Google real, tirando o provedor e reiniciando o `api` no meio do
conectar, no servidor de homologação e nunca no de produção, numa janela que o dono combina (D6).

**Specs:** `federated-login` — "Decisão do login no retorno" (o "sem nenhum provedor" deixa de ser exceção e vira o
caso da registração ausente, e a intenção desconhecida fica escrita como regra), "Conectar o provedor em Minha conta"
e "As recusas do aceite não criam sessão e voltam ao convite". `federated-providers` — o requisito
da seção ganha a regra do cliente OIDC sem provedor: registrado sem registração, sem pedido de rede, sem rota nova, e
a checagem de boot conferindo o cliente também. `server-deployment` — o guarda dos caminhos de navegação de topo
(#285) roda sobre os dois hosts, com e sem provedor.

**Documentação:** `CHANGELOG.md` e `02-HISTORICO_E_STATUS.md`; `docs/deployment.md` (a frase da falha do conectar
que ainda volta ao `/entrar`, e o cliente OIDC sem provedor).

**Fora:** o painel não muda (a #279 já mostra a recusa `falha` em `/account`, sem depender da lista de provedores;
D7). Nenhuma rota nova, nenhum cookie novo, nenhuma migration.

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `federated-login`: a falha do conectar sem provedor configurado volta a Minha conta; a regra da intenção
  desconhecida fica escrita.
- `federated-providers`: o cliente OIDC registrado também sem provedor, sem rede nem rota nova.
- `server-deployment`: o guarda dos caminhos de navegação de topo do `apps/api` sobre os dois hosts.

## Impact

- **App:** só o `apps/api`. Nenhuma referência nova entre apps; nada em `libs/`.
- **Arquivos (produção):** `Identity/IdentityIssuerServiceCollectionExtensions.cs`,
  `Identity/Federated/FederatedLoginEndpoints.cs`, `Identity/IdentityIssuerStartupValidation.cs` e os comentários
  que citam "o cliente só existe com provedor" (`FederatedMissingRegistrationHandler.cs`).
- **Arquivos (teste):** dois duplos novos (o host sem provedor no mesmo banco do host com provedor, e o contador de
  pedidos de saída), os casos do retorno, os do cliente sem provedor, o da checagem de boot e o
  `NginxNavigationEndpointsTests` em dois herdeiros.
- **Instalação sem provedor:** passa a ter os handlers do cliente OIDC no pipeline, sem registração; o login por
  senha, a lista anônima de provedores e o `404` do início não mudam (design, Risks).
- **Deploy:** só a imagem do `api`, sem configuração nova.
- **Suítes:** `api`, `rt-connectors` (o round-trip compõe o host do `apps/api` com o emissor) e `frontend` (o
  `nginxHeaders.test.ts` lê o `nginx.conf`, que não muda), uma por vez, com o lock.
