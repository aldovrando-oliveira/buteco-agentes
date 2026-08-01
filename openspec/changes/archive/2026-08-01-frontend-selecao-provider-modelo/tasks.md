## 1. Tipos

- [x] 1.1 (apps/frontend) Em `features/agents/types/agent.ts`, adicionar
      `provider: string | null` e `model: string | null` a `Agent`.
- [x] 1.2 (apps/frontend) Em `features/agents/types/agent.ts`, adicionar
      `provider: string` e `model: string` a `CreateAgentInput` (mantendo
      `UpdateAgentInput` como alias de `CreateAgentInput`).
- [x] 1.3 (apps/frontend) Criar o tipo `ProviderCatalogEntry { id: string;
      models: string[] }` em `features/agents/types/agent.ts` (ou em
      `providersApi.ts`, ver Decision 1 do design.md).

## 2. `GET /providers`: API client e hook

- [x] 2.1 (apps/frontend) Criar `features/agents/api/providersApi.ts` com
      `listProviders(): Promise<ProviderCatalogEntry[]>`, reaproveitando o
      `request<T>` já existente em `agentsApi.ts` (mesmo `API_BASE_URL`,
      mesmo tratamento de `ApiError`).
- [x] 2.2 (apps/frontend) Criar `features/agents/api/useProviders.ts` com
      `useProvidersQuery()`: `queryKey: ['providers']`,
      `staleTime: Infinity` (Decision 1 do design.md).
- [x] 2.3 (apps/frontend) Criar `features/agents/api/useProviders.test.ts`
      cobrindo sucesso e erro, no mesmo padrão de `useAgents.test.ts`
      (`vi.stubGlobal('fetch', ...)`, `QueryClientProvider` de teste).

## 3. `AgentForm`: Selects de Provider/Model

- [x] 3.1 (apps/frontend) Adicionar prop `providers: ProviderCatalogEntry[]`
      a `AgentFormProps`.
- [x] 3.2 (apps/frontend) Adicionar `provider`/`model` a `useForm<CreateAgentInput>`
      (`initialValues`) e dois campos `Select` (Provider, Model) usando
      `@mantine/core`, com `withAsterisk` e `error={errors?.campo ?? form.errors.campo}`,
      mesmo padrão dos campos existentes.
- [x] 3.3 (apps/frontend) Implementar filtragem do `data` do Select de Model
      pelas `models` do provider atualmente selecionado em `form.values.provider`.
- [x] 3.4 (apps/frontend) Implementar reset do campo Model ao trocar o
      Provider **depois do mount** (via `onChange` do Select de Provider,
      não em um `useEffect` de mount) — o mount inicial em modo edição não
      reseta nada, mesmo com `initialValues` stale (Decision 2 do design.md).
- [x] 3.5 (apps/frontend) Implementar a injeção da opção sintética
      desabilitada (`{ value, label: "${valor} (indisponível)", disabled: true }`)
      no `data` do Select de Provider quando `initialValues.provider` não
      está entre `providers.map(p => p.id)`, e no Select de Model quando
      `initialValues.model` não está entre os `models` do provider
      correspondente (Decision 2 do design.md).
- [x] 3.6 (apps/frontend) Adicionar validação client-side "obrigatório"
      para `provider` e `model` em `useForm`, mesmo padrão de
      `name`/`instructions`.
- [x] 3.7 (apps/frontend) Atualizar `AgentForm.test.tsx`: submit envia
      `provider`/`model`; Select de Model filtra pelas opções do Provider
      escolhido; trocar Provider reseta Model; **não** reseta Model no mount
      inicial com `initialValues` (incluindo um caso com par stale); erro de
      validação "obrigatório" para Provider/Model vazios; opção sintética
      desabilitada aparece e não é re-selecionável para um par fora das
      opções disponíveis.

## 4. `AgentCreatePage`

- [x] 4.1 (apps/frontend) Chamar `useProvidersQuery()` e passar
      `providers={data ?? []}` para `AgentForm`.
- [x] 4.2 (apps/frontend) Adicionar early-return de `<Alert color="yellow">`
      quando `providersQuery.data?.length === 0`, com a mensagem definida
      na Decision 3 do design.md, antes de renderizar `AgentForm`.
- [x] 4.3 (apps/frontend) Adicionar gate de loading combinando o loading de
      `useProvidersQuery()` com o já existente (mutation `isPending`),
      seguindo o mesmo estilo de early-return já usado em `AgentEditPage`.
- [x] 4.4 (apps/frontend) Atualizar `AgentCreatePage.test.tsx`: cenário de
      `GET /providers` vazio exibindo a mensagem de bloqueio, sem renderizar
      o formulário; cenário de submit incluindo `provider`/`model`.

## 5. `AgentEditPage`

- [x] 5.1 (apps/frontend) Chamar `useProvidersQuery()` e passar
      `providers={data ?? []}` para `AgentForm`, junto de `initialValues`
      incluindo `provider`/`model` do agente carregado.
- [x] 5.2 (apps/frontend) Combinar o gate de loading de `useAgentQuery` e
      `useProvidersQuery` — `AgentForm` só monta depois que as duas
      resolvem (Risco 1 do design.md, mesmo princípio já usado para
      `useAgentQuery` sozinho).
- [x] 5.3 (apps/frontend) Adicionar early-return de `<Alert color="yellow">`
      quando `providersQuery.data?.length === 0`, mesma mensagem da
      Decision 3, depois do gate de loading e antes de renderizar `AgentForm`.
- [x] 5.4 (apps/frontend) Atualizar `AgentEditPage.test.tsx`: formulário
      pré-preenchido inclui provider/model; submit envia provider/model
      alterados; cenário de `GET /providers` vazio bloqueando o formulário;
      cenário de agente com `provider` fora das opções disponíveis
      renderizando a opção sintética desabilitada (Decision 2).

## 6. `AgentDetailCard`

- [x] 6.1 (apps/frontend) Exibir `agent.provider`/`agent.model` como texto
      simples (ex.: `Provider: openai · Model: gpt-5.6-sol`), tratando o
      caso de ambos nulos.
- [x] 6.2 (apps/frontend) Exibir `<Badge color="yellow">Precisa de
      reconfiguração</Badge>` ao lado do badge de `isActive` quando
      `agent.provider === null || agent.model === null` (Decision 4 do
      design.md).
- [x] 6.3 (apps/frontend) Atualizar `AgentDetailCard.test.tsx`: provider/model
      exibidos corretamente; badge de reconfiguração aparece quando nulos e
      não aparece quando preenchidos.

## 7. `AgentTable`

- [x] 7.1 (apps/frontend) Adicionar colunas de Provider e Model à tabela,
      exibindo `agent.provider`/`agent.model` como texto simples.
- [x] 7.2 (apps/frontend) Exibir o mesmo badge de "Precisa de
      reconfiguração" por linha, mesma condição da Decision 4 do design.md.
- [x] 7.3 (apps/frontend) Atualizar `AgentTable.test.tsx`: provider/model
      exibidos corretamente por linha; badge de reconfiguração aparece
      quando nulos e não aparece quando preenchidos.

## 8. Verificação

- [x] 8.1 (apps/frontend) Rodar `npm run lint`, `npm run format:check` e a
      suíte Vitest completa.
- [x] 8.2 (apps/frontend) Validação manual: `docker compose up -d` →
      `dotnet run` (`apps/api`, com `OpenAI:ApiKey` configurado) →
      `npm run dev` → cadastrar um agente escolhendo Provider/Model →
      editar um agente sem `provider`/`model` (badge de reconfiguração
      visível antes, ausente depois de salvar com valores válidos).
- [x] 8.3 (apps/frontend) Validação manual do caso de lista vazia: remover
      temporariamente todas as chaves de provider configuradas em
      `apps/api` e confirmar que `AgentCreatePage`/`AgentEditPage` exibem a
      mensagem de bloqueio em vez do formulário.
