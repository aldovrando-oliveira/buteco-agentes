## Context

`apps/frontend` consome `GET /agents/{id}` e `PUT /agents/{id}` desde a
change `frontend-cadastro-agentes`, quando `AgentResponse` ainda não tinha
`description` nem `skills`. A change `backend-agente-description-skills`
acrescentou os dois campos do lado da API, com uma semântica de PUT que é
"substituição do recurso inteiro": `UpdateAgentRequest` tem
`Description = null` e `Skills = null` como default, e o handler converte
`Skills` nulo em lista vazia. O frontend nunca foi atualizado, então hoje
todo `PUT` do painel zera esses campos.

`Skill` na API é `record Skill(string Name, string? Description)`. A
`description` da skill não é decorativa: `AgentSkillMapper` a leva para o
`AgentSkill.Description` do Agent Card A2A (com string vazia quando nula).
Qualquer modelo de skill no frontend que só carregue o nome perde essa
informação no round-trip.

O handoff de design (`design_handoff_painel_agentes_mcp`, seções 3.1 e 6)
descreve descrição e skills como campos novos do formulário e como
card "Skills" no detalhe, com skills representadas como chips só de nome.
Essa parte do handoff foi feita sem acesso ao shape real da API, e é aqui
ajustada (Decision 1).

## Goals / Non-Goals

**Goals:**
- Parar de apagar `description` e `skills` na edição pelo painel.
- Permitir cadastrar e editar descrição e skills (nome + descrição
  opcional) no formulário de agente.
- Exibir descrição e skills no detalhe do agente, em um componente que a
  change seguinte (detalhe em abas) possa reposicionar sem reescrever.
- Absorver os ajustes de forma do formulário descritos no handoff que não
  mudam comportamento (hints, contador, fonte mono nas instruções,
  selects lado a lado).

**Non-Goals:**
- Nenhuma mudança em `apps/api` ou `apps/workers`.
- Nenhuma reorganização do detalhe em abas/colunas — isso é a change
  seguinte; aqui `AgentSkillsCard` entra abaixo do card atual.
- Descrição na coluna "Agente" da listagem — fica com a change de listas
  (busca, filtro e colunas novas), que redesenha `AgentTable` inteira.
- Card A2A (URL do endpoint e do Agent Card) — change própria, porque
  exige expor as URLs em `AgentResponse`.
- Deduplicação de nomes de skill no cliente — o servidor não rejeita nomes
  repetidos (o Agent Card gera ids distintos para colisões), então o
  cliente não inventa uma regra que o servidor não tem.
- Nenhuma biblioteca nova.

## Estrutura de pastas proposta

```
apps/frontend/src/features/agents/
├── api/
│   ├── agentsApi.ts              # inalterado (body muda só pelo tipo)
│   └── useAgents.test.ts         # inalterado
├── components/
│   ├── AgentForm.tsx             # + Descrição, + <AgentSkillsFields>, hints,
│   │                             #   contador, mono nas instruções, selects
│   │                             #   lado a lado
│   ├── AgentForm.test.tsx        # + casos de descrição/skills
│   ├── AgentSkillsFields.tsx     # novo — lista editável de skills dentro
│   │                             #   do form (recebe o `form` do Mantine)
│   ├── AgentSkillsFields.test.tsx# novo
│   ├── AgentDetailCard.tsx       # + descrição sob o nome, renderiza
│   │                             #   <AgentSkillsCard> no fim
│   ├── AgentDetailCard.test.tsx  # + casos de descrição/skills
│   ├── AgentSkillsCard.tsx       # novo — presentational, recebe skills
│   └── AgentSkillsCard.test.tsx  # novo
├── pages/
│   ├── AgentCreatePage.tsx       # inalterado além do tipo do submit
│   ├── AgentCreatePage.test.tsx  # + body inclui description/skills
│   ├── AgentEditPage.tsx         # initialValues com description/skills
│   └── AgentEditPage.test.tsx    # + preservação sem tocar nos campos
└── types/
    └── agent.ts                  # + AgentSkill, description, skills nos
                                  #   tipos Agent e Create/UpdateAgentInput
```

Nenhum arquivo fora de `features/agents/`.

## Decisions

### Decision 1: Skill é nome + descrição opcional, não chip só de nome

O formulário modela cada skill como uma linha com dois campos: nome
(obrigatório) e descrição (opcional). O detalhe exibe o nome em destaque
e a descrição como texto secundário quando existir.

O handoff propõe chips só de nome com um input "Adicionar". Essa
representação descarta `Skill.Description`, que a API persiste e que
alimenta o Agent Card A2A. Um agente cadastrado via API com skills
descritas perderia as descrições na primeira edição pelo painel — o
mesmo tipo de perda silenciosa que esta change existe para corrigir.

**Alternativa descartada**: chips só de nome, preservando descrições
existentes "por baixo" (mantendo-as no estado e reenviando sem exibir).
Rejeitada porque cria dado invisível e ineditável: o operador não veria a
descrição, não poderia corrigi-la, e o comportamento de "remover e
readicionar uma skill apaga a descrição" seria inexplicável na interface.

### Decision 2: O formulário sempre envia `description` e `skills`, mesmo vazios

`CreateAgentInput`/`UpdateAgentInput` tornam `description` e `skills`
obrigatórios no tipo (não opcionais), e `AgentForm` sempre os inclui no
`onSubmit`: `description` como `null` quando em branco (após `trim`) e
`skills` como `[]` quando não há nenhuma. É a mesma disciplina já adotada
para `targetAgentIds` nas delegações ("array vazio explícito, nunca
omitido nem `null`").

A raiz do bug atual é justamente a omissão: o servidor trata campo
ausente como "limpar". Tornar os campos obrigatórios no tipo faz o
compilador impedir que uma página futura volte a omiti-los.

**Alternativa descartada**: manter os campos opcionais no tipo e só
preenchê-los na edição. Rejeitada porque deixa a porta aberta para o
mesmo bug em qualquer novo caller.

### Decision 3: Lista de skills via lista do `@mantine/form`, em componente próprio

`AgentSkillsFields` recebe o `form` (`UseFormReturnType`) e opera sobre
`form.values.skills` com `insertListItem`/`removeListItem`/
`getInputProps('skills.${i}.name')`. Fica em componente separado só para
manter `AgentForm` legível; não tem estado próprio nem hooks de dados.

Erros do servidor chegam como `skills[0].name` (formato do
`ValidateShape` da API). `AgentForm` já recebe `errors: Record<string,
string>` das páginas; a conversão de `skills[i].name` para a linha `i` é
feita em `AgentForm` antes de repassar para `AgentSkillsFields`, no mesmo
lugar onde hoje `errors?.name` é aplicado ao campo nome.

Validação client-side espelha o servidor: nome de skill obrigatório
(`"O nome da skill é obrigatório."`), mesma cópia da API. Nenhuma outra
regra.

### Decision 4: `AgentSkillsCard` é componente separado e presentational

O detalhe passa a renderizar a descrição dentro de `AgentDetailCard`
(abaixo do nome) e as skills em um `AgentSkillsCard` novo, recebendo
`skills: AgentSkill[]` via prop. Nesta change ele é renderizado logo após
`AgentDetailCard` em `AgentDetailPage`.

A separação existe por causa da change seguinte (detalhe em abas), cujo
layout de Visão geral coloca "Skills" como card independente na coluna
direita. Fazer o card nascer separado evita que aquela change tenha que
extrair código deste.

### Decision 5: Ajustes de forma do handoff entram aqui, os de comportamento não

Entram: hint da descrição, label das instruções com "system prompt,
aceita Markdown", contador de caracteres abaixo das instruções, fonte
mono no textarea de instruções, provedor e modelo em duas colunas
(`SimpleGrid cols={2}`), hint das skills. São mudanças de apresentação
no mesmo formulário que esta change já edita, sem novo estado nem nova
regra.

Não entram: densidade compacta/confortável, "Primeiros passos", busca e
filtro — decididos para changes posteriores.

## Risks / Trade-offs

- [Risco] Um agente com muitas skills deixa o formulário longo →
  [Mitigação] aceitável para o volume esperado (rótulos, não catálogo);
  nada de virtualização.
- [Risco] Testes existentes de `AgentForm`/`AgentCreatePage`/
  `AgentEditPage` que asseram o body exato do submit passam a falhar por
  causa dos campos novos → [Mitigação] esperado; os testes são atualizados
  para o body completo, e é exatamente a asserção que impede o bug de
  voltar.
- [Risco] O detalhe fica visualmente mais carregado antes da change de
  abas → [Mitigação] transitório e curto; a change seguinte reorganiza.

## Open Questions

(nenhuma)
