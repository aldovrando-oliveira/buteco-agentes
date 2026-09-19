## Why

A premissa "não existe ambiente de produção" foi corrigida no
`02-HISTORICO_E_STATUS.md` em 18/09/2026. Fora dele continua de pé: há texto em
`docs/`, `SECURITY.md` e `README.md` que afirma fatos que deixaram de ser
verdade, e aceites de risco cujo argumento valia contra um stack de
desenvolvimento e não vale contra um piloto que atende atrás do Cloudflare.
Junto, a sequência de redeploy de `docs/deployment.md` §2 omite
`--env-file .env.prod`. Desde `fix-stack-servidor-lacunas`, catorze variáveis
são obrigatórias na interpolação do compose, e a mistura atual de comandos com
e sem a flag ensina o operador a aplicá-la de forma inconsistente.

**Change só de documentação.** Nenhuma linha de código, nenhuma mudança de
comportamento, nenhuma decisão de segurança revista.

## What Changes

**R1: `--env-file .env.prod` uniforme em `docs/deployment.md`.**

- Todo comando `docker compose` contra `docker-compose.prod.yml` no documento
  passa `--env-file .env.prod`. Isso inclui os comandos que já estão certos, que
  são conferidos e não presumidos. **O requisito é a uniformidade, não o número
  de linhas.** Hoje há seis linhas erradas: o exemplo de teste local no
  cabeçalho (20-21), o `ps` do §1 passo 3 (65) e os quatro passos do §2
  (86-89).
- Por que correção parcial é pior que nenhuma: medido com `podman-compose`
  1.6.0, cada passo sem a flag falha na interpolação, **antes de agir**, e
  nenhum container é criado. O único dano real é o de improviso: o operador põe
  a flag no `stop inbox` e esquece no `run --rm migrator`. O `apps/inbox` fica
  parado, e o migrator não roda. Um documento misto ensina justamente esse
  improviso.
- Ressalva: `docker compose` não foi medido, porque não está na máquina de
  medição. Isso não afeta a correção, já que a flag é necessária em qualquer
  runtime. Afeta só o dimensionamento do dano, e é a mesma lacuna de paridade
  que o `02` já registra.

**R2 (a): afirmação factual falsa, corrigida.**

- `docs/a2a-integration.md`. A nota "nem o cadastro de agentes nem a rota A2A
  de nenhum agente exigem credencial hoje" é falsa desde `auth-login-e-servico`.
  Só `/health`, `/auth/login` e o agent card são anônimos
  (`apps/api/src/Buteco.Api/Program.cs:107-110`).
  - A conferência pedida antes de reescrever achou um **buraco funcional**, não
    só um texto errado. Não existe credencial emitida para cliente A2A externo.
    É limitação aceita na Decision 6 de `auth-login-e-servico` e **nunca
    registrada no `02`**.
  - O documento inteiro é um guia para integrador externo, e os três `curl`
    contra `/a2a` não mandam `Authorization`. Ele descrevia a única forma em
    que a integração funcionaria.
  - A correção torna o guia verdadeiro: exige Bearer, declara que não há
    credencial para terceiros e aponta a decisão. Também registra a limitação
    no `02` como item próprio. Não escreve frase por cima do buraco.
- `docs/deployment.md` §4 (Non-Goals, `TaskJobConsumer`) afirma "esta change
  assume que ela já rodou". Não rodou (`02`: "não corrigido ainda"), e o stack
  subiu sem ela. A frase passa a dizer que a suposição não se concretizou.
- `docker-compose.prod.yml:3` e `:8`, comentários de cabeçalho: um aponta para
  um caminho de change hoje no archive, e o outro para `deploy/runbook.md`, que
  não existe. As duas linhas são só comentário, sem efeito em comportamento.

**R2 (b): o argumento mudou; o texto só é marcado.**

- Em `docs/deployment.md` ("Risco aceito: placeholder `changeme`"),
  `SECURITY.md` (Webhook authenticity; Known Operational Risk), `README.md`
  (Project status, WAHA) e `docs/architecture.md` (tabela de adapters, WAHA),
  cada aceite recebe uma marca: ele foi feito antes de qualquer deploy atender
  tráfego real e está em reavaliação. **Nenhuma decisão muda.**

**Registro no `02`, em blocos separados.**

- **Correção de status, visivelmente separada do registro desta change** (mesma
  disciplina da 5a-4). Os dois itens que dizem que `Anthropic__ApiKey` e
  `Gemini__ApiKey` "não chegam a processo nenhum" no compose são falsos desde
  `268814d` (`fix-stack-servidor-lacunas`).
- **Fechamento** do item de `--env-file` em "Abertos por
  `nginx-shell-sem-cache-e-prefixo-messages`".
- **Três changes candidatas, ordenadas por exposição:** autenticidade do webhook
  do WAHA; rejeição de `changeme` no boot; vazamento de descritores com
  Claude/Gemini.
- **Item novo:** credencial para cliente A2A externo.
- **Marca** no item do `TaskJobConsumer`: o gatilho tem alvo real.
- **D3 aplicado ao próprio `02`.** O repositório é público. O domínio do piloto
  fica, mas a adjacência com "risco não mitigado **agora exposto**" é quebrada,
  e nenhuma outra correção de 18/09 é alterada.

## Sem delta de spec, e isso é deliberado

`server-deployment` não tem requisito sobre a sequência operacional de redeploy,
e **não deve ganhar um**.

- **A ordem não é garantia do sistema.** "Parar o `apps/inbox` antes de migrar"
  é invariante de procedimento. Escrever cenário para ela seria descrever um
  runbook em spec, o erro que `fix-stack-servidor-lacunas` apontou na spec dos
  prefixos, que passava porque descrevia o defeito.
- **O cenário existente segue certo.** "Ausência de segredo obrigatório falha o
  processamento do compose" continua correto, e a medição do R1 o confirma.
- **O que exigiria spec.** Se um dia a ordem virar garantia do sistema, com o
  migrator recusando rodar com o `apps/inbox` antigo de pé, isso é change de
  comportamento, com spec própria.

`a2a-agent-card` já especifica que o card declara o esquema Bearer. O
`docs/a2a-integration.md` passa a concordar com a spec, que não muda.

## Capabilities

### New Capabilities

(nenhuma)

### Modified Capabilities

(nenhuma. Change só de documentação, sem requisito ou cenário novo, alterado ou
removido. Ver "Sem delta de spec" acima.)

**Nota sobre `openspec validate --strict`.** Esta change segue o precedente de
`apps-api-cqrs-mediator` e `inbox-fix-concorrencia-orquestrador`, que mantiveram
`specs/` vazio de propósito. O schema `spec-driven` exige mecanicamente ao menos
um delta, e o erro de validação é o falso positivo conhecido para change que
genuinamente não altera requisito. `tasks.md` é o único artefato exigido para
aplicar.

## Fora de escopo, explicitamente

**Nenhuma das três candidatas é decidida aqui.** Reabrir qualquer uma
misturaria correção de documentação com mudança de comportamento em produção,
que é o que a régua da casa separa. Especificamente:

- rejeitar `changeme` no boot;
- verificar a autenticidade do webhook do WAHA;
- verificar o vazamento de descritores com credencial de Claude/Gemini;
- emitir credencial para cliente A2A externo.

Também fica fora o texto classificado (c) na exploração, que continua
verdadeiro e não é tocado.

## Impact

**Arquivos:**

| Arquivo | O que muda |
|---|---|
| `docs/deployment.md` | R1 e duas correções/marcas de R2 |
| `docs/a2a-integration.md` | nota operacional, seção do endpoint, três `curl` e o exemplo de card |
| `docs/architecture.md` | marca (b) |
| `SECURITY.md` | duas marcas (b) |
| `README.md` | marca (b) |
| `docker-compose.prod.yml` | só comentários, linhas 3 e 8 |
| `02-HISTORICO_E_STATUS.md` | correção de status, registro e itens |

- **Apps:** nenhum. `apps/api`, `apps/workers`, `apps/inbox` e `apps/frontend`
  não são tocados.
- **Operação:** nenhum deploy necessário.

**Verificação.**

- **Não há suíte que leia documentação.** `scripts/check-docs.py` cobre só
  `.md`: links relativos resolvem, referência a change arquivada usa o caminho
  do archive, apps documentados e `[Unreleased]` no CHANGELOG.
- **O que ele não vê:**
  - comandos de shell, por isso o R1 passa por ele;
  - arquivos `.yml`, por isso as referências mortas do compose passam;
  - caminho de repositório citado em crase fora de link Markdown.
- **O peso dessa lacuna:** é a mesma classe que interessa à change candidata do
  guarda de prefixos do nginx. Um guarda que não lê o artefato não pega o
  defeito do artefato.
