## Context

`apps/frontend` (`react@19.2.7`, `@mantine/*@9.4.2`, `@tanstack/react-query@^5.101.4`,
`react-router@^8.3.0`, Vitest + Testing Library) já consome o catálogo de
agentes via `features/agents/api/agentsApi.ts` + `useAgents.ts`, com quatro
telas (`AgentListPage`, `AgentCreatePage`, `AgentDetailPage`,
`AgentEditPage`) e três componentes apresentacionais (`AgentForm`,
`AgentDetailCard`, `AgentTable`). Nenhuma dependência nova é necessária
nesta fatia — `@mantine/core@^9.4.2` (já instalada) inclui `Select`, e o
`data` desse componente já aceita itens no formato
`{ value, label, disabled }`, verificado na documentação da versão em uso.

A change `backend-multi-provedor-llm` (arquivada) tornou `provider`/`model`
obrigatórios em `POST /agents`/`PUT /agents/{id}` e adicionou
`GET /providers`. Lendo o código real de `apps/api` (não assumido):

- `GET /providers` → `ProviderResponse[] { id: string, models: string[] }`
  (`Providers/Responses/ProviderResponse.cs`), só provedores com API key
  configurada aparecem; lista vazia é `200 OK` válido, não erro
  (`NoProvidersConfiguredTests`).
- `POST /agents`/`PUT /agents/{id}` validam shape (`provider`/`model`
  presentes) e depois disponibilidade (`provider` configurado, `model` no
  catálogo desse `provider`) via `ProviderCatalogService`, devolvendo
  `ValidationProblem` com erro nas chaves exatas `"provider"` ou `"model"`
  (`AgentEndpoints.BuildProviderValidationErrors`) — o mesmo formato que
  `fieldErrorsFrom` (já implementado em `AgentCreatePage`/`AgentEditPage`)
  já sabe mapear para erro de campo, sem nenhuma mudança nesse mecanismo.
- `AgentResponse` já inclui `Provider`/`Model` (`string?`, nullable) —
  todo agente cadastrado antes desta change está com os dois nulos, porque
  a migration daquela change não fez backfill (Decision 6 do design.md
  arquivado). Esse é o estado "precisa de reconfiguração".

`GET /providers` só devolve o catálogo de modelos de provedores
**configurados no ambiente** — não existe nenhum endpoint que devolva o
catálogo completo de modelos de um provedor não-configurado. Isso limita o
que a interface pode oferecer quando o `provider` de um agente já
cadastrado saiu da lista de disponíveis (chave de ambiente removida depois
do cadastro): não há como propor um `model` alternativo para esse
`provider`, só o valor já persistido.

## Goals / Non-Goals

**Goals:**
- `AgentForm` permite selecionar Provider e Model, com Model filtrado pelo
  Provider escolhido, reutilizando `GET /providers` como única fonte de
  opções.
- Editar um agente cujo `provider`/`model` atual não está mais disponível
  não trava nem falha silenciosamente — o valor atual fica visível, mas
  não re-selecionável, forçando uma escolha nova e válida antes de salvar.
- Ambiente sem nenhum provedor configurado (`GET /providers` vazio) produz
  uma mensagem clara em vez de um formulário quebrado.
- `AgentDetailCard`/`AgentTable` tornam visível, sem round-trip nenhum via
  `SendMessage`, quando um agente precisa de reconfiguração
  (`provider`/`model` nulos).

**Non-Goals:**
- Nenhuma mudança em `apps/api` ou `apps/workers` — os três endpoints já
  existem e já validam corretamente.
- Busca/filtro textual nos `Select` de Provider/Model — listas curtas
  (3 provedores, poucos modelos cada) não justificam.
- Nome de exibição "bonito" por provedor (ex. "OpenAI" em vez de
  `"openai"`) — texto cru da API é aceitável nesta fatia.
- Edição de provider/model fora do formulário completo (ex. troca rápida
  inline na tela de detalhe).
- Indicador de "provider indisponível" (`provider` preenchido, mas fora da
  lista atual de `GET /providers`) em `AgentDetailCard`/`AgentTable`/
  `AgentListPage` — decidido explicitamente como fora desta fatia (ver
  Decision 4): exigiria `AgentListPage` também consumir
  `useProvidersQuery()` e propagar disponibilidade até `AgentTable`, escopo
  que vaza para além do que foi pedido. Fica registrado como candidato a
  uma fatia futura, se o produto pedir.
- Biblioteca de ícones nova para o badge de reconfiguração — reutiliza
  `Badge` do Mantine, mesmo padrão já usado para `isActive`.

## Decisions

### Decision 1 — `AgentForm` continua apresentacional; hook novo em `features/agents/api/`, não em `features/providers/`

`AgentCreatePage` e `AgentEditPage` chamam `useProvidersQuery()` e passam o
resultado (`providers: ProviderCatalogEntry[]`) como prop para `AgentForm`
— mesmo padrão já usado para `initialValues`/`submitLabel` (Decision 1 do
design.md de `frontend-editar-ativar-agente`). `AgentForm` continua sem
importar `api/` nem hooks de query/mutation, mantendo a fronteira já
estabelecida em `frontend-cadastro-agentes` (Decision 2): componente
apresentacional só recebe dados e callbacks via props.

**Alternativa considerada — `AgentForm` busca `GET /providers`
diretamente**: o argumento a favor seria que a lista de provedores é dado
de referência/configuração, não dado de negócio do agente, então não
precisaria seguir a mesma regra de "página busca, componente recebe".
Rejeitada porque a fronteira existente não distingue "dado de negócio" de
"dado de referência" — distingue "quem faz I/O" de "quem renderiza", e
`AgentForm` já teria que lidar com o estado de loading/erro de
`GET /providers` internamente, duplicando exatamente o tipo de lógica que
as páginas já centralizam para `GET /agents/{id}`. Manter a regra uniforme
(toda chamada HTTP vive em hook, chamado pela página) evita uma exceção
que só existiria para este caso.

**Local do hook**: `features/agents/api/providersApi.ts` (função
`listProviders()`) + `features/agents/api/useProviders.ts`
(`useProvidersQuery()`), não um diretório `features/providers/` novo.
Único consumidor é a feature de agentes — Non-Goals já fecham qualquer
outro caso de uso (não há tela de administração de provedores nesta
fatia). Um `features/` inteiro para um único hook de leitura seria a
mesma abstração prematura já evitada na Decision 2 de
`frontend-editar-ativar-agente` (`UpdateAgentInput` como alias em vez de
tipo próprio). Se um segundo consumidor real aparecer no futuro (ex. uma
tela de configuração de ambiente), a extração para `features/providers/`
vira um refactor de escopo conhecido, não uma migração especulativa.

```ts
// features/agents/api/providersApi.ts
export interface ProviderCatalogEntry {
  id: string;
  models: string[];
}
export function listProviders(): Promise<ProviderCatalogEntry[]> {
  return request<ProviderCatalogEntry[]>('/providers');
}

// features/agents/api/useProviders.ts
export function useProvidersQuery() {
  return useQuery({
    queryKey: ['providers'],
    queryFn: listProviders,
    staleTime: Infinity, // dado de configuração de ambiente, não de negócio do agente
  });
}
```

`staleTime: Infinity` (não um número arbitrário como 5 minutos): a lista de
provedores só muda quando alguém reconfigura variáveis de ambiente e
reinicia `apps/api` — o mesmo evento já invalida qualquer cache em memória
do browser (recarregar a página busca de novo). Não há necessidade de
revalidação automática dentro de uma sessão.

### Decision 2 — Provider/Model fora do catálogo atual: opção sintética desabilitada no `Select`, não confiar no comportamento default do componente

Quando `AgentForm` monta em modo edição e `initialValues.provider` não
está entre os `id` de `providers` (prop), ou `initialValues.model` não
está entre os `models` do provider correspondente, o componente injeta uma
opção extra no `data` do `Select` afetado:

```ts
const providerOptions = providers.map((p) => ({ value: p.id, label: p.id }));
if (initialValues?.provider && !providers.some((p) => p.id === initialValues.provider)) {
  providerOptions.push({
    value: initialValues.provider,
    label: `${initialValues.provider} (indisponível)`,
    disabled: true,
  });
}
```

Mesma técnica aplicada independentemente ao Model (contra os `models` do
provider atualmente selecionado — que pode ser o próprio provider stale,
sem nenhum model real disponível, ou um provider válido cujo model
persistido não consta mais no catálogo).

**Por que uma opção sintética desabilitada, e não confiar no `Select` para
exibir um `value` ausente de `data`**: o comportamento do `Select` da
Mantine quando `value`/`defaultValue` não corresponde a nenhum item de
`data` não é uma API documentada como estável — depende de detalhes
internos de resolução de label que podem mudar entre versões minor. Uma
opção sintética com `disabled: true` é determinística: sempre visível,
nunca re-selecionável (o usuário não consegue reabrir o dropdown e
clicar nela para "confirmar" a mesma escolha), e não exige nenhuma lógica
de renderização condicional separada (banner, texto auxiliar) — o próprio
`Select` já comunica o estado.

**Por que não bloquear o submit nem adicionar validação client-side
especial para esse caso**: se o usuário submeter sem alterar nada, o valor
stale (uma string não-vazia) passa na validação client-side "obrigatório"
sem erro — o formulário permite o submit. O servidor então rejeita com 400
no campo `provider` (mesma resposta de "provider não configurado" já
mapeada por `ProviderCatalogService.Validate`), e o mecanismo de
`fieldErrorsFrom` já existente aplica esse erro no `Select` certo. O
roundtrip que já existe para qualquer outro erro de validação cobre este
caso sem nenhum código novo — duplicar essa checagem no cliente só
adiantaria o mesmo feedback por uma requisição, sem justificar a
complexidade extra nesta fatia.

**Reset no mount é incondicional**: a regra "trocar o Provider reseta o
Model, exceto no mount inicial em modo edição" (Escopo funcional do
proposal) não distingue um par stale de um par válido — em nenhum dos dois
casos o `Model` é resetado só por causa do mount. O reset só acontece a
partir de uma mudança de Provider feita pelo usuário depois do mount. Isso
significa que um agente com `provider`/`model` stale mostra os dois valores
stale simultaneamente ao abrir o formulário de edição, até o usuário trocar
o Provider manualmente.

**Alternativa considerada — Select nasce vazio, forçando nova escolha
imediata**: mais simples de implementar, mas descarta informação real (qual
provider/model o agente tinha antes) sem necessidade — o usuário abre o
formulário de edição sem saber por que precisa reconfigurar, e sem
contexto do que estava configurado antes. Rejeitada por perder essa
informação sem ganho de simplicidade real (a lógica de "injetar opção
sintética" e a lógica de "detectar stale e não popular o campo" têm
complexidade comparável).

### Decision 3 — `GET /providers` vazio: bloquear o formulário inteiro (create e edit), sem modo parcial

`AgentCreatePage`/`AgentEditPage` fazem um early-return de `<Alert>` quando
`providersQuery.data?.length === 0`, no mesmo estilo dos early-returns de
loading/404/erro genérico que `AgentEditPage` já usa hoje:

```tsx
if (providersQuery.data?.length === 0) {
  return (
    <Alert color="yellow">
      Nenhum provedor de LLM configurado. Configure ao menos uma variável de
      ambiente antes de cadastrar agentes.
    </Alert>
  );
}
```

Nenhum formulário parcialmente funcional (nome/instruções editáveis,
Selects travados) — o agente não pode ser criado nem editado de forma
válida nesse estado de qualquer forma (o servidor rejeitaria qualquer
`provider`/`model` por definição, já que nenhum está configurado), então
exibir campos que não levam a lugar nenhum só adicionaria confusão. Em
`AgentEditPage`, sem link "voltar" adicional no `Alert` — mesma economia
visual já usada no `Alert` de agente não encontrado
(`<Alert color="red">Agente não encontrado.</Alert>`, sem CTA).

**Cor do Alert**: amarelo (estado de configuração ausente, ação do
operador do ambiente, não erro do usuário do formulário) em vez de
vermelho — vermelho já é usado para falhas reais (404, erro de rede/servidor)
em todo o restante da feature; usar a mesma cor aqui misturaria "faltou
configurar o ambiente" com "algo quebrou".

### Decision 4 — Indicador de "precisa de reconfiguração": só `provider`/`model` nulos nesta fatia

`AgentDetailCard` e `AgentTable` continuam recebendo só `agent`/`agents`
como prop (nenhuma mudança de assinatura além do necessário para exibir
`provider`/`model` como texto). O indicador é computado localmente, sem
nenhum fetch adicional:

```ts
const needsReconfiguration = agent.provider === null || agent.model === null;
```

Renderizado como `<Badge color="yellow">Precisa de reconfiguração</Badge>`
ao lado do badge de `isActive` já existente — reutiliza o padrão de cor já
estabelecido nesta fatia (Decision 3: amarelo = estado de configuração, não
erro). É o caso que afeta 100% dos agentes cadastrados antes desta change
(sem backfill de `provider`/`model`), então é o caso com maior valor
imediato para o usuário — e o único que não exige nenhuma chamada de rede
adicional em `AgentListPage`/`AgentDetailPage`.

**Decidido explicitamente com o usuário**: "provider indisponível"
(`provider` preenchido, mas fora da lista atual de `GET /providers`) fica
fora desta fatia — ver Non-Goals. Diferente do caso nulo, esse indicador
exigiria que `AgentTable` (hoje só recebe `agents: Agent[]`) também
recebesse a lista de providers disponíveis para comparar, o que por sua vez
exige que `AgentListPage` chame `useProvidersQuery()` e coordene um segundo
estado de loading (lista de agentes + lista de providers) só para colorir
um badge — escopo que o proposal original não previa tocar
(`AgentListPage.tsx` não está no "Escopo funcional").

### Decision 5 — Tipos: `Agent.provider`/`Agent.model` nullable; `CreateAgentInput`/`UpdateAgentInput` obrigatórios

```ts
export interface Agent {
  id: string;
  name: string;
  instructions: string;
  isActive: boolean;
  provider: string | null;
  model: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface CreateAgentInput {
  name: string;
  instructions: string;
  provider: string;
  model: string;
}

export type UpdateAgentInput = CreateAgentInput; // já era alias (Decision 2 de frontend-editar-ativar-agente); continua idêntico no shape
```

Espelha exatamente o contrato do backend: `AgentResponse.Provider`/`Model`
são `string?` (nullable, refletindo o estado "precisa de reconfiguração"),
enquanto `CreateAgentRequest`/`UpdateAgentRequest` exigem os dois presentes
na validação de shape (`AgentEndpoints.ValidateShape`) — o formulário nunca
submete um `provider`/`model` vazio com sucesso, então o tipo de input não
precisa ser nullable.

## Árvore de pastas (arquivos novos/modificados)

```
apps/frontend/src/features/agents/
├── api/
│   ├── agentsApi.ts               (inalterado)
│   ├── providersApi.ts            (NOVO — listProviders())
│   ├── useAgents.ts                (inalterado)
│   ├── useProviders.ts            (NOVO — useProvidersQuery())
│   └── useProviders.test.ts       (NOVO)
├── components/
│   ├── AgentForm.tsx               (MODIFICADO — Select de Provider/Model, opção sintética, props `providers`)
│   ├── AgentForm.test.tsx          (MODIFICADO — filtro de Model, reset de Model, validação, opção stale)
│   ├── AgentDetailCard.tsx         (MODIFICADO — texto de provider/model + badge de reconfiguração)
│   ├── AgentDetailCard.test.tsx    (MODIFICADO)
│   ├── AgentTable.tsx              (MODIFICADO — coluna de provider/model + badge de reconfiguração)
│   └── AgentTable.test.tsx         (MODIFICADO)
├── pages/
│   ├── AgentCreatePage.tsx         (MODIFICADO — useProvidersQuery(), Alert de lista vazia)
│   ├── AgentCreatePage.test.tsx    (MODIFICADO)
│   ├── AgentEditPage.tsx           (MODIFICADO — useProvidersQuery(), Alert de lista vazia)
│   └── AgentEditPage.test.tsx      (MODIFICADO — cenário de provider stale)
└── types/
    └── agent.ts                    (MODIFICADO — provider/model em Agent/CreateAgentInput; ProviderCatalogEntry novo)
```

Nenhum arquivo de `apps/api` ou `apps/workers` é tocado.

## Risks / Trade-offs

- **[Risco] `AgentEditPage` agora depende de duas queries (`useAgentQuery` +
  `useProvidersQuery`) antes de poder renderizar o formulário.** Um
  gate de loading incompleto (só esperar uma das duas) reintroduziria o
  mesmo bug já mitigado em `frontend-editar-ativar-agente` (Risco 2 daquele
  design.md: `useForm` não reage a `initialValues` que mudam pós-mount). →
  Mitigação: `AgentForm` só monta depois que **ambas** resolvem (`isLoading`
  combinado das duas queries), mesmo princípio já aplicado, só que agora
  para duas fontes em vez de uma.
- **[Risco] Opção sintética desabilitada depende de comparação exata de
  string (`provider`/`model`) entre o que veio de `GET /agents/{id}` e o
  que vem de `GET /providers`.** Se um dos dois lados tivesse
  case/whitespace divergente, a comparação falharia silenciosamente e o
  Select pareceria ter perdido o valor. → Mitigação: ambos os valores vêm
  do mesmo backend, gerados a partir do mesmo catálogo estático
  (`ProviderModelCatalog`/`LlmProviders`) — não há transformação de texto
  em nenhum dos dois caminhos, então não há divergência de formatação
  possível na prática.
- **[Trade-off] Nenhuma revalidação automática de `GET /providers` durante
  uma sessão (`staleTime: Infinity`).** Se um operador reconfigurar o
  ambiente enquanto um usuário já tem a UI aberta, o novo provider só
  aparece depois de um reload da página. Aceito: é o mesmo tipo de
  configuração que já exige reiniciar `apps/api` para ter efeito — um
  reload de página é uma expectativa razoável nesse fluxo, e adicionar
  polling/revalidação para um evento raro não se paga.
- **[Trade-off] Bloquear o formulário inteiro quando `GET /providers` está
  vazio (Decision 3) impede editar nome/instruções de um agente existente
  nesse estado**, mesmo que essa edição não dependesse de provider/model
  disponível. Aceito: simplicidade de um único caminho de bloqueio (sem
  modo parcial) pesa mais que esse caso — que já é uma configuração de
  ambiente incompleta, não um fluxo normal de operação.

## Migration Plan

Sem dado persistido a migrar — mudança é só de `apps/frontend`, consumindo
endpoints já existentes e estáveis em `apps/api`. Deploy local/dev:

1. `npm run lint`, `npm run format:check` e a suíte Vitest depois de cada
   arquivo modificado da árvore acima.
2. Validação manual: `docker compose up -d` → `dotnet run` (`apps/api`,
   com ao menos `OpenAI:ApiKey` configurado) → `npm run dev`
   (`apps/frontend`) → cadastrar um agente novo escolhendo Provider/Model →
   editar um agente existente sem `provider`/`model` (badge de
   reconfiguração visível antes de editar) → confirmar que some depois de
   salvar com Provider/Model válidos.
3. Validação manual do caso de lista vazia: remover temporariamente
   `OpenAI:ApiKey` (e não configurar Anthropic/Gemini) → confirmar que
   `AgentCreatePage`/`AgentEditPage` exibem a mensagem de bloqueio em vez do
   formulário.

Rollback: reverter o commit/branch — nenhuma migração de banco ou dado
persistido envolvida.
