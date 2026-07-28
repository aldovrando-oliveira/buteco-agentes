## Context

`apps/frontend` (React 19 + TypeScript + Vite + Mantine 9, `react-router`
declarativo, `@tanstack/react-query`) consome hoje só três endpoints do
catálogo de agentes: `POST /agents`, `GET /agents`, `GET /agents/{id}`
(change `frontend-cadastro-agentes`, arquivada). `AgentDetailPage` é
somente leitura por decisão explícita daquela change ("Non-Goal: edição ou
exclusão de agente — endpoints não existem em `apps/api`").

Esse Non-Goal não se aplica mais: a change `apps-api-agent-update-status`
(arquivada) adicionou `PUT /agents/{id}`, `POST /agents/{id}/activate`,
`POST /agents/{id}/deactivate` e o campo `IsActive` em `AgentResponse`.
Verificado lendo o código-fonte real de `apps/api` (não assumido a partir
do proposal daquela change):

- `PUT /agents/{id}` valida `name`/`instructions` com a mesma regra de
  `POST /agents` (`ValidationProblem` 400), 404 se o id não existir —
  mesmo formato de erro já tratado hoje por `AgentCreatePage`.
- `activate`/`deactivate` são idempotentes (chamar duas vezes seguidas não
  é erro) e devolvem o `AgentResponse` atualizado; 404 se o id não
  existir.
- Um agente inativo faz `POST /agents/{id}/a2a` (`SendMessage`) responder
  com a task em `TaskState.Rejected` — um **estado terminal** do protocolo
  A2A. Isso importa para a Decision 3 abaixo.

Nenhum `Modal` ou fluxo de confirmação existe hoje em `apps/frontend`.
`@mantine/core` (Modal) e `@mantine/hooks` (`useDisclosure`) já são
dependências (`^9.4.2`); nenhuma versão nova a verificar, porque nenhuma
dependência nova é introduzida nesta fatia.

## Goals / Non-Goals

**Goals:**
- Consumir `PUT /agents/{id}`, `POST /agents/{id}/activate` e
  `POST /agents/{id}/deactivate` a partir de `AgentDetailPage` e de uma
  nova `AgentEditPage`, reaproveitando a infraestrutura já existente
  (`agentsApi.ts`/`useAgents.ts`, `AgentForm`, notificações).
- Tornar `isActive` visível onde o agente aparece (`AgentDetailCard`,
  `AgentTable`).
- Confirmar explicitamente antes de desativar um agente, dado o efeito
  observável e não totalmente reversível descrito na Decision 3.

**Non-Goals:**
- Exclusão de agente — não existe no backend.
- Ação em lote (ativar/desativar/editar múltiplos agentes de uma vez) —
  mesmo Non-Goal já adotado no backend.
- Concorrência otimista (ETag/If-Match) — último write vence, mesmo
  Non-Goal do backend.
- Biblioteca de ícones nova — mesma decisão já tomada em
  `frontend-cadastro-agentes` (toggle de tema com SVG inline em vez de
  `@tabler/icons-react`); os botões desta fatia usam texto, sem ícone.
- Qualquer mudança em `apps/api` ou `apps/workers`.

## Decisions

### 1. `AgentForm` ganha `initialValues` e `submitLabel` opcionais, sem duplicar o formulário

`AgentForm.tsx` passa a aceitar:
```ts
interface AgentFormProps {
  onSubmit: (values: CreateAgentInput) => void;
  errors?: Record<string, string>;
  submitting?: boolean;
  initialValues?: CreateAgentInput;   // novo, default { name: '', instructions: '' }
  submitLabel?: string;               // novo, default 'Criar agente'
}
```
`useForm({ initialValues: initialValues ?? { name: '', instructions: '' } })`
e o texto do `Button` passam a ler dessas props. Como ambas têm default
idêntico ao comportamento atual, **`AgentCreatePage.tsx` não muda uma
linha** — só `AgentEditPage.tsx` passa `initialValues` (o agente carregado)
e `submitLabel="Salvar alterações"`.

`AgentEditPage` só renderiza `AgentForm` depois que `useAgentQuery(id)`
resolve (mesmo gate de loading/erro já usado em `AgentDetailPage`) — o
`useForm` do Mantine não reage a mudança de `initialValues` depois do mount,
então o form só pode nascer com o agente já carregado, nunca em um estado
"carregando, mas já montado com valores vazios".

**Alternativa descartada**: um componente `AgentEditForm` separado
duplicando os mesmos campos e validação. Rejeitada porque a validação
(nome/instruções obrigatórios) é idêntica nos dois modos e duplicar um
formulário de 2 campos por causa de um rótulo de botão e valores iniciais
é a exata forma de abstração prematura que o projeto evita.

### 2. `UpdateAgentInput` como alias de `CreateAgentInput`, não tipo próprio

```ts
export type CreateAgentInput = { name: string; instructions: string };
export type UpdateAgentInput = CreateAgentInput;
```
Confirmado lendo `apps/api`: `CreateAgentRequest` e `UpdateAgentRequest`
já são o mesmo `record(string? Name, string? Instructions)` no backend —
o alias no frontend só espelha uma decisão que o backend já tomou. Se um
dos dois shapes divergir no futuro (ex.: `PUT` ganhar um campo que
`POST` não tem), o alias vira uma interface própria numa mudança de uma
linha, sem quebrar chamadores que importam `UpdateAgentInput` pelo nome.

**Alternativa descartada**: `interface UpdateAgentInput { name: string;
instructions: string }` duplicada. Rejeitada porque criaria dois tipos
estruturalmente idênticos sem nenhuma diferença de comportamento hoje —
não há nada para "atualizar" nesse tipo que o alias não capture, e o
alias já deixa o acoplamento (e o ponto de divergência futura) explícito
no código.

### 3. Desativar exige confirmação via `Modal` de `@mantine/core`; ativar não exige

**Escolha**: `AgentDetailPage` usa `useDisclosure(false)` (de
`@mantine/hooks`) para controlar um `Modal` (de `@mantine/core`) que só
aparece ao clicar em "Desativar". O modal explica a consequência
("mensagens enviadas a este agente enquanto ele estiver inativo serão
rejeitadas") e tem dois botões: "Cancelar" (fecha o modal, `close()`) e
"Confirmar desativação" (chama `deactivateAgent`, fecha o modal, notifica).
"Ativar" chama `activateAgent` diretamente, sem modal.

**Por que confirmar apesar de o toggle `isActive` ser reversível**: o
argumento "não precisa confirmar porque dá para reativar depois" só vale
para o campo `isActive` em si. Ele não vale para o efeito colateral que
acontece *durante* a janela em que o agente fica inativo: qualquer
`SendMessage` recebido nesse intervalo cai em `TaskState.Rejected` — um
estado terminal do protocolo A2A (ver Context). Reativar o agente depois
não reprocessa essas mensagens; quem as enviou precisa perceber a rejeição
e reenviar manualmente. Ou seja, o toggle é reversível, mas a rejeição que
ele causa enquanto está no ar não é — isso justifica uma pausa de
confirmação mesmo sem ser uma ação destrutiva de dado.

**Por que `Modal` + `useDisclosure` em vez de `@mantine/modals`**: mesmo
raciocínio já registrado em `frontend-cadastro-agentes` (Decision 4, ícone
do toggle de tema) — o projeto não introduz um pacote inteiro por uma
necessidade pontual quando o que já está instalado resolve. `Modal` já é
exportado por `@mantine/core` e `useDisclosure` por `@mantine/hooks`,
ambos dependências existentes; `@mantine/modals` (gerenciador de modais
imperativo, fila de modais, etc.) resolveria um problema que este caso —
um único modal, controlado localmente por uma página — não tem.

**Alternativa considerada e descartada**: nenhuma confirmação, com
"ativar" como a forma de desfazer. Descartada porque, como descrito acima,
"reversível" aqui é impreciso — o dado (`isActive`) volta, mas o efeito já
observado por terceiros (mensagens rejeitadas) não. Um clique acidental em
"Desativar" numa lista ou página de detalhe teria consequência silenciosa
para quem está integrado ao agente via A2A, sem qualquer chance de
interceptar antes do efeito acontecer.

**Alternativa considerada e descartada**: confirmação inline de dois
cliques (botão "Desativar" vira "Confirmar?" por alguns segundos, sem
modal). Descartada por ser um padrão de interação incomum para esta ação
e mais fácil de disparar por engano (dois cliques rápidos no mesmo alvo)
do que um modal, que exige mover o cursor para um alvo geometricamente
separado — a barreira física é parte do valor da confirmação aqui.

### 4. Ações de ativar/desativar/editar vivem na página, não em `AgentDetailCard`

`AgentDetailCard` continua recebendo só `agent: Agent` e permanece
puramente apresentacional — ganha a exibição do badge de `isActive`, mas
nenhuma prop de callback (`onActivate`, `onDeactivate`, `onEdit`) e nenhum
import de `api/` ou hooks de mutation. `AgentDetailPage` monta os botões
("Editar" como `Link`/`Button` para `/agents/:id/edit`, "Ativar" ou
"Desativar" conforme `data.isActive`) ao lado do card, com as mutations
(`useActivateAgentMutation`, `useDeactivateAgentMutation`) vivendo na
página. Mesma fronteira já estabelecida em
`frontend-cadastro-agentes` (Decision 2): componente apresentacional nunca
importa `api/` nem hooks de query/mutation.

### 5. Cache de mutations: mesmo padrão de `useCreateAgentMutation`

```ts
export function useUpdateAgentMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, input }: { id: string; input: UpdateAgentInput }) => updateAgent(id, input),
    onSuccess: (updated) => {
      queryClient.setQueryData(['agents', updated.id], updated);
      void queryClient.invalidateQueries({ queryKey: ['agents'] });
    },
  });
}
```
`useActivateAgentMutation`/`useDeactivateAgentMutation` seguem a mesma
forma (`mutationFn: (id: string) => activateAgent(id)` /
`deactivateAgent(id)`), mesmo `onSuccess`. `setQueryData` evita um GET
redundante ao voltar para `/agents/:id` depois de editar/ativar/desativar;
`invalidateQueries(['agents'])` mantém a listagem consistente na próxima
visita, sem refetch imediato forçado (mesma escolha já feita para
criação).

### 6. Indicador de `isActive`: `Badge` do Mantine, verde/cinza (não verde/vermelho)

`AgentDetailCard` e `AgentTable` exibem `<Badge color={agent.isActive ?
'green' : 'gray'}>{agent.isActive ? 'Ativo' : 'Inativo'}</Badge>`.
Vermelho fica reservado para erro (já é o `color` usado em
`notifications.show` e nos `Alert` de falha) — usar vermelho também para
"inativo" misturaria "estado neutro configurado pelo operador" com "algo
deu errado". Cinza comunica neutralidade sem sugerir falha.

### 7. Estrutura de pastas (arquivos novos/modificados)

```
apps/frontend/src/
├── app/
│   └── router.tsx                          (MODIFICADO — + rota agents/:id/edit)
└── features/agents/
    ├── api/
    │   ├── agentsApi.ts                    (MODIFICADO — updateAgent, activateAgent, deactivateAgent)
    │   └── useAgents.ts                    (MODIFICADO — 3 hooks de mutation novos)
    ├── components/
    │   ├── AgentForm.tsx                   (MODIFICADO — initialValues, submitLabel)
    │   ├── AgentForm.test.tsx              (MODIFICADO — cobre valores iniciais/rótulo)
    │   ├── AgentDetailCard.tsx             (MODIFICADO — badge de isActive)
    │   └── AgentTable.tsx                  (MODIFICADO — badge de isActive por linha)
    ├── pages/
    │   ├── AgentDetailPage.tsx             (MODIFICADO — deixa de ser somente leitura)
    │   ├── AgentDetailPage.test.tsx        (MODIFICADO — reescreve o teste que hoje afirma "sem edição/exclusão")
    │   ├── AgentEditPage.tsx               (NOVO)
    │   └── AgentEditPage.test.tsx          (NOVO)
    └── types/
        └── agent.ts                        (MODIFICADO — isActive em Agent, novo UpdateAgentInput)
```
Nenhum arquivo de `apps/api` ou `apps/workers` é tocado.

## Risks / Trade-offs

- **[Risco] O teste existente `AgentDetailPage.test.tsx` afirma
  literalmente "sem controles de edição/exclusão" e verifica a ausência
  dos botões "editar"/"excluir".** Essa asserção fica falsa com esta
  mudança. → Mitigação: o teste é reescrito (não só estendido) como parte
  desta fatia — registrado em `tasks.md`, para não ser esquecido por
  parecer "só mais um teste que já passa".
- **[Risco] `useForm` do Mantine não re-inicializa se `initialValues`
  mudar depois do primeiro render.** Se `AgentEditPage` renderizasse
  `AgentForm` antes do `GET /agents/{id}` resolver, o form nasceria vazio
  e ignoraria os dados reais do agente quando chegassem. → Mitigação:
  mesmo gate de `isLoading`/`error` já usado em `AgentDetailPage` — o
  `AgentForm` só monta depois que `data` existe.
- **[Trade-off] Confirmar a desativação adiciona um passo à UX de uma ação
  que tecnicamente não apaga dado nenhum.** Aceito: a justificativa da
  Decision 3 (rejeição de `SendMessage` é um efeito que não se desfaz
  reativando) pesa mais que o atrito de um clique extra num fluxo de
  operador, não de usuário final apressado.
- **[Trade-off] `UpdateAgentInput` como alias em vez de tipo próprio cria
  acoplamento estrutural com `CreateAgentInput`.** Aceito: reflete um
  acoplamento que já existe no backend (`UpdateAgentRequest`/
  `CreateAgentRequest` idênticos); revisar quando/se divergirem, não antes.

## Migration Plan

Sem dado persistido a migrar — mudança é só de `apps/frontend`, consumindo
endpoints já existentes e estáveis em `apps/api`. Deploy local/dev:

1. `npm run lint`, `npm run format:check` e a suíte Vitest depois de cada
   arquivo modificado da árvore acima.
2. Validação manual: `docker compose up -d` → `dotnet run` (`apps/api`) →
   `npm run dev` (`apps/frontend`) → editar um agente existente → ativar/
   desativar (conferindo o modal de confirmação na desativação) → conferir
   badge atualizado na lista e no detalhe.

Rollback: reverter o commit/branch — nenhuma migração de banco ou dado
persistido envolvida.
