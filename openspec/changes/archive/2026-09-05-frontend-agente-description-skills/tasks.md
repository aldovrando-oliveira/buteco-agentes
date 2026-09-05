## 1. Tipos (apps/frontend)

- [x] 1.1 Em `features/agents/types/agent.ts`, adicionar
  `AgentSkill { name: string; description: string | null }` e os campos
  `description: string | null` e `skills: AgentSkill[]` em `Agent`,
  espelhando `AgentResponse`/`SkillResponse` da API.
- [x] 1.2 No mesmo arquivo, adicionar `description: string | null` e
  `skills: AgentSkill[]` a `CreateAgentInput` (e, por alias, a
  `UpdateAgentInput`) como campos **obrigatórios** no tipo — nunca
  opcionais — para que nenhum caller possa omiti-los (Decision 2 do
  design.md).
- [x] 1.3 Atualizar as fixtures de `Agent` usadas nos testes existentes de
  `features/agents` para incluir `description` e `skills`, sem mudar o
  comportamento asserido por eles.

## 2. Formulário (apps/frontend)

- [x] 2.1 Criar `features/agents/components/AgentSkillsFields.tsx`:
  componente sem estado próprio que recebe o `form` do `@mantine/form` e
  renderiza `form.values.skills` como linhas com `TextInput` de nome
  (obrigatório), `TextInput` de descrição (opcional) e ação de remover,
  mais botão "Adicionar skill" (via `insertListItem`/`removeListItem`) e
  o hint "Rótulos do que o agente sabe fazer. Não afetam o runtime."
  (Decision 3 do design.md).
- [x] 2.2 Em `AgentForm.tsx`, adicionar `description` e `skills` aos
  `initialValues` (default `''` e `[]`), o campo **Descrição** (`Textarea`
  curta, opcional, hint "Uso interno: ajuda o operador a identificar o
  agente nas listas.") entre Nome e Instruções, e `<AgentSkillsFields>`
  após Provedor/Modelo.
- [x] 2.3 Em `AgentForm.tsx`, validar client-side que cada skill tem nome
  não vazio (`"O nome da skill é obrigatório."`, mesma cópia da API) e
  mapear erros de servidor com chave `skills[i].name` para o erro da
  linha `i` (`form.setFieldError('skills.i.name', ...)` ou equivalente),
  no mesmo ponto onde `errors?.name` já é aplicado.
- [x] 2.4 Em `AgentForm.tsx`, no `onSubmit`, enviar sempre
  `description` (texto com `trim`, ou `null` quando vazio) e `skills`
  (cada uma com `name` com `trim` e `description` com `trim` ou `null`;
  `[]` quando não há nenhuma) — nunca omitir os campos.
- [x] 2.5 Em `AgentForm.tsx`, aplicar os ajustes de forma do handoff sem
  mudança de comportamento (Decision 5): label das instruções indicando
  "system prompt, aceita Markdown", textarea de instruções com fonte mono
  e contador de caracteres abaixo, selects de Provedor e Modelo lado a
  lado em `SimpleGrid cols={2}`.
- [x] 2.6 Criar `AgentSkillsFields.test.tsx` cobrindo: adicionar linha,
  remover linha, edição de nome e descrição refletida em `form.values`.
- [x] 2.7 Atualizar `AgentForm.test.tsx` cobrindo: submit sem descrição e
  sem skills envia `description: null` e `skills: []`; submit com
  descrição e skills envia os valores com `description` da skill nula
  quando em branco; skill com nome vazio bloqueia o submit com mensagem
  na linha; erro de servidor `skills[0].name` aparece na primeira linha;
  `initialValues` com descrição e skills pré-preenche os campos.

## 3. Páginas de cadastro e edição (apps/frontend)

- [x] 3.1 Em `AgentEditPage.tsx`, passar `description: data.description ??
  ''` e `skills: data.skills` nos `initialValues` de `AgentForm`.
- [x] 3.2 Confirmar que `AgentCreatePage.tsx` não precisa de mudança além
  da tipagem (o `values` do `onSubmit` já carrega os campos novos).
- [x] 3.3 Atualizar `AgentCreatePage.test.tsx`: o body de `POST /agents`
  inclui `description` e `skills`.
- [x] 3.4 Atualizar `AgentEditPage.test.tsx` com o caso de regressão do
  bug: agente carregado com descrição e skills, usuário altera só o nome
  e salva → `PUT /agents/{id}` carrega `description` e `skills` iguais
  aos carregados. Mais o caso de limpar descrição e remover todas as
  skills → `description: null`, `skills: []`.

## 4. Detalhe (apps/frontend)

- [x] 4.1 Criar `features/agents/components/AgentSkillsCard.tsx`:
  presentational, recebe `skills: AgentSkill[]`, exibe cada skill com o
  nome em destaque (`Badge` ou equivalente) e a descrição como texto
  secundário quando não nula; com lista vazia exibe "Nenhuma skill
  declarada." (Decision 4).
- [x] 4.2 Em `AgentDetailCard.tsx`, exibir `agent.description` logo abaixo
  do nome, ou "Sem descrição." quando nula.
- [x] 4.3 Em `AgentDetailPage.tsx`, renderizar `<AgentSkillsCard
  skills={data.skills} />` imediatamente após `<AgentDetailCard>`.
- [x] 4.4 Criar `AgentSkillsCard.test.tsx` cobrindo: lista vazia, skill só
  com nome, skill com nome e descrição.
- [x] 4.5 Atualizar `AgentDetailCard.test.tsx` (descrição presente e
  ausente) e `AgentDetailPage.test.tsx` (card de skills renderizado após o
  card de detalhe).

## 5. Verificação (apps/frontend)

- [x] 5.1 Rodar a suíte de testes do frontend em `apps/frontend` e
  confirmar que todos os testes novos e existentes passam.
- [x] 5.2 Rodar lint e typecheck do frontend e confirmar que não há erros
  introduzidos pelas mudanças desta change.
