**Issue:** #190 (a 190a da M3; depende da #185, da #189 e da #274, todas no `main` em `0f44c10`). As contas ficaram para a **#279** (Q1, decisão do dono de 14/11/2026).

## Why

O backend da M3 está pronto: o login pelo Google atrás do emissor, as recusas e o vínculo (#189), o aceite de
convite pelo Google (#274) e as correções #276 e #277. A entrada não tem tela: ninguém entra pelo Google no
painel, as recusas não aparecem, e o convite só se aceita com senha.

## What Changes

- **O login** (`/entrar`): "ou entre com" e o botão "Entrar com Google" para cada provedor configurado
  (`GET /auth/federated/providers`; o tipo desconhecido não ganha botão; a lista falhando esconde o bloco); o
  início (`POST /auth/federated/{chave}/start`) com o `503`, o `404` e o `429`; "Redirecionando para o Google…";
  a volta, que cai no passo do código ou no cadastro das 2 etapas de hoje, com os avisos verdes da volta do Google
  e do convite aceito, e a query limpa por `replace`, mantendo o `retorno`.
- **As recusas no login**: os sete motivos da #189, a variante sem e-mail do `sem-acesso` e o motivo desconhecido
  como `falha`, com os textos fixados pelo dono; o e-mail confirmado lido **uma vez** por
  `POST /auth/federated/refusal`, com guarda contra o efeito duplo do `StrictMode`.
- **O aceite de convite com provedor** (`/convite`): "Entre com uma conta Google", "Continuar com Google" e "ou
  crie uma senha"; o início (`POST /auth/invitation/federated/{chave}/start`) e as respostas; o token na aba na
  chave que a página já usa (`buteco.link.convite`), apagado no aceite e em toda recusa; as cinco recusas e o
  link inválido que voltam ao convite; a volta sem o token.
- **Fora, na #279:** Minha conta "Formas de entrada", a confirmação com o código para quem não tem senha, Editar
  usuário, Usuários e Identidade.
- **Nada no backend.** Dado que a tela precisar e o backend não servir é achado (convenção 1).

## Capabilities

### New Capabilities

- `panel-federated-login`: o login pelo provedor no `/entrar`, a volta e as recusas.

### Modified Capabilities

- `panel-access-link-pages`: o convite com o provedor, as recusas que voltam a ele, e o token apagado no aceite e
  na recusa.

## Impact

- **Código:** só o `apps/frontend` (`features/auth`).
- **Suítes:** `apps/frontend` (`~/.cache/buteco-agents/bin/suite-190.sh`).
- **Protótipo:** a validação visual na homologação, contra a versão `1794626941-64ac` (a 77) dos quadros `Main`
  (`entrega=m3`), `LoginRecusado` e `AceitarConvite`.
