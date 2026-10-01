## Why

A coluna **Falhas** da tabela *"Consumo por agente"* perde falha por **dois
caminhos independentes**, e os dois foram medidos no banco de dev em 30/09/2026:

- **#86** — as linhas de falha são colapsadas num `Map` por `agentId`
  (`AgentConsumptionCard.tsx:65`). A rota agrupa por `(AgentId, Provider, Model)` e
  ordena `count desc`, então o `Map` **guarda a última, que é a de MENOR
  contagem**. É viés para baixo, sempre, e com número plausível;
- **#84** — as linhas nascem de `tokens.byAgent`, que é
  `provider_calls join task_executions`. **Agente que falhou sem chamar o provedor
  não tem linha**, e as falhas dele somem da tela junto com ela.

**O caso da #84 está ativo no banco de dev agora**, e não é hipótese: o agente
`4ab9739f` tem **2 execuções, as 2 recusadas, nenhuma chamada de provedor** —
`(null)`/`(null)` em provedor e modelo. Ele **não aparece na tabela**, e as 2
falhas dele não aparecem em lugar nenhum da página. O defeito esconde
preferencialmente a falha de **configuração**, que é a que mais interessa.

As duas entram na mesma change porque **corrigir uma deixa o número errado de
outro jeito**: somar sem trazer a linha mantém o agente invisível, e trazer a
linha sem somar faz a linha nova chegar com a contagem menor dele.

## What Changes

- **`apps/frontend`** — a população da tabela passa a ser a **união** das chaves de
  `tokens.byAgent` com as de `errors.byAgent`. Agente com falha e sem token entra
  com a célula de Tokens **vazia** — nunca `0`, que é o que a gramática de valores
  da tela já sabe apresentar;
- **`apps/frontend`** — as linhas de `errors.byAgent` passam a ser **somadas** por
  agente em vez de sobrescritas;
- **`apps/frontend`** — o destaque em vermelho (`maiorFalha`) passa a ser calculado
  sobre a **população nova**. Sem isso a linha que a #84 traz pode ter a maior
  contagem da coluna e **não** sair destacada;
- **o comentário da ausência que vira zero** (`AgentConsumptionCard.tsx:67-82`) é
  corrigido com a causa: ele justifica o zero dizendo *"um agente que não está em
  NENHUMA das duas listas simplesmente não tem linha"*, e a partir desta change a
  população **é** as duas listas — o raciocínio fica mais exato, e o texto precisa
  dizer isso;
- **`02-HISTORICO_E_STATUS.md`** — duas afirmações de estado sobre a #75 tornadas
  falsas pelo merge dela são corrigidas **com a causa** (convenção 9), nesta change
  e não num PR de cauda.

**Nenhuma mudança em `apps/api`.** A decisão e o motivo estão na D1 do `design.md`,
e ela foi tomada **depois** de medir, não antes.

### O que esta change NÃO faz, e é decisão e não esquecimento

- **A população não passa a ser "todos os agentes com execução na janela"**, que é
  o que a #84 propõe no corpo. Ela seria mais ampla que a união e exigiria
  `apps/api`. **O motivo de não fazer está na D1**, e o resumo é: a união já fecha
  o dano por construção — toda falha tem linha em `errors.byAgent` —, e a linha que
  a população mais ampla acrescentaria não teria nenhuma das três colunas da tabela
  para preencher, porque `Tasks` ficou fora por decisão (#67, fechada);
- **a coluna Falhas continua NÃO somando o mesmo que o KPI de falhas da página**, e
  isso é **achado desta change, declarado na D3**, com issue nova. Fazê-la fechar
  exigiria tirar `Rejected` da coluna, o que **desfaz a #84**.

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `system-insights-ui`: o requisito *"Consumo por agente cruza com o catálogo e
  leva ao diagnóstico"* passa a declarar **de que população a tabela é feita** — a
  união de quem consumiu com quem falhou — e que a contagem de falhas de um agente
  é a **soma** das linhas que a rota serve para ele, não uma delas.

## Impact

**Código**

- `apps/frontend/src/features/insights/components/AgentConsumptionCard.tsx` — a
  população das linhas, a soma das falhas, o cálculo do destaque e dois blocos de
  comentário;
- `apps/frontend/src/features/insights/components/AgentConsumptionCard.test.tsx` —
  guardas novos e adaptação dos que afirmam a população antiga.

**Contrato e rota**

- **Nenhuma alteração em `apps/api`**, em nenhuma rota, em nenhum tipo de resposta.
  `AgentFailures` e `AgentTokens` ficam como estão.

**Documentação**

- `02-HISTORICO_E_STATUS.md` — as duas correções de estado da #75, e a entrada
  desta change;
- `CHANGELOG.md`;
- `openspec/specs/system-insights-ui/spec.md`, pelo delta.

**Dependências:** nenhuma.
