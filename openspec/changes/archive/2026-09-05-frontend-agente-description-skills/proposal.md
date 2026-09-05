## Why

O backend já persiste e devolve `description` e `skills` do agente
(change `backend-agente-description-skills`, já aplicada): `AgentResponse`
tem os dois campos, `POST /agents` e `PUT /agents/{id}` os aceitam, e o
Agent Card do protocolo A2A (`GET /agents/{id}/.well-known/agent-card.json`)
é montado a partir deles. O `apps/frontend`, porém, não conhece nenhum dos
dois: o tipo `Agent` não os declara, o formulário não os oferece e o
detalhe não os exibe.

Isso não é só uma lacuna de exibição — é um bug de perda de dados. O
formulário de edição envia `PUT /agents/{id}` sem `description` e sem
`skills`; o endpoint trata ausência como "nenhuma skill" e "sem
descrição" (`ToSkills(null)` devolve lista vazia, `Description` vai
nulo). Resultado: **qualquer edição de agente feita pelo painel apaga
silenciosamente a descrição e as skills** que tenham sido cadastradas via
API, e com elas o conteúdo do Agent Card exposto a outros agentes.

Esta é a primeira change do redesenho do painel proposto no handoff
`design_handoff_painel_agentes_mcp` (Claude Design). Ela vem antes das
demais (abas no detalhe, visão inversa no servidor MCP, busca e filtros)
porque corrige o bug acima e entrega os campos que o card "Skills" e a
descrição no header do detalhe vão precisar.

## What Changes

- `Agent` (em `features/agents/types/agent.ts`) ganha `description:
  string | null` e `skills: AgentSkill[]`, com `AgentSkill { name: string;
  description: string | null }` espelhando `SkillResponse`. `CreateAgentInput`
  / `UpdateAgentInput` ganham `description` e `skills` — **sempre
  enviados**, nunca omitidos, para o servidor não zerar o que já existe.
- `AgentForm` ganha o campo **Descrição** (textarea curta, opcional, com
  hint "Uso interno: ajuda o operador a identificar o agente nas listas.")
  e a seção **Skills**: lista de linhas com nome (obrigatório) e descrição
  (opcional), botão "Adicionar skill" e remoção por linha, com hint
  "Rótulos do que o agente sabe fazer. Não afetam o runtime." Erros de
  validação do servidor no formato `skills[i].name` são exibidos na linha
  correspondente.
- Ajustes de forma no `AgentForm` vindos do handoff, sem mudança de
  comportamento: label das instruções passa a indicar "system prompt,
  aceita Markdown", textarea de instruções em fonte mono com contador de
  caracteres, selects de provedor e modelo lado a lado.
- `AgentEditPage` pré-preenche descrição e skills a partir do agente
  carregado, de forma que salvar sem tocar nesses campos os preserva.
- `AgentDetailCard` exibe a descrição logo abaixo do nome (ou "Sem
  descrição.") e um novo componente `AgentSkillsCard` lista as skills
  (nome + descrição quando houver) ou "Nenhuma skill declarada.". O card
  nasce como componente separado porque a change seguinte (detalhe em
  abas) o reposiciona na coluna direita da Visão geral sem reescrevê-lo.

## Capabilities

### New Capabilities
(nenhuma)

### Modified Capabilities
- `agent-catalog-ui`: os requisitos de cadastro, edição e detalhe de
  agente passam a cobrir `description` e `skills`; a edição passa a
  exigir preservação desses campos quando não alterados.

## Impact

- **apps/frontend**: `types/agent.ts`, `components/AgentForm.tsx` (+ novo
  `AgentSkillsFields.tsx`), `components/AgentDetailCard.tsx` (+ novo
  `AgentSkillsCard.tsx`), `pages/AgentEditPage.tsx` e os testes
  correspondentes (`AgentForm.test.tsx`, `AgentDetailCard.test.tsx`,
  `AgentCreatePage.test.tsx`, `AgentEditPage.test.tsx`). Nenhuma rota
  nova, nenhuma dependência nova, nenhuma função nova em `agentsApi.ts`
  (os endpoints são os mesmos; só o body muda pelo tipo).
- **apps/api / apps/workers**: nenhuma mudança — os endpoints já aceitam
  e devolvem os campos.
- **Fora de escopo** (changes seguintes do mesmo handoff): abas no
  detalhe, descrição na coluna "Agente" da listagem, card A2A, busca e
  filtros, densidade e "Primeiros passos".
