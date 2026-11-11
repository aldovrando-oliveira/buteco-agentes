## Context

O código em `9f8580f` (main depois do PR #271, a #270):

- **`ApplicationsListPage`** chama o `useQuery` direto, sem hook próprio: `queryKey: applicationsListKey({ busca,
  estado })`, `placeholderData: keepPreviousData`. A linha dos controles aparece com `!vazia`, em que `vazia =
  query.isSuccess && apps.length === 0 && !filtrando`. Na primeira carga (`isPending`) e no erro, `vazia` é
  falsa, e a linha aparece.
- **`AgentListPage`** e **`KnowledgeBaseListPage`** mostram a linha com `hasAgents` / `hasKnowledgeBases`
  (a lista com itens). A busca delas é local, então a query não muda ao digitar, e a linha nunca some durante
  a busca.
- **O TanStack Query 5:** o `placeholderData` só vale enquanto a query da chave nova está `pending`. Durante a
  busca seguinte, `data` é a lista anterior (`isPlaceholderData`), e `isPending` é falso. Se a busca seguinte
  falhar, a query vai a `error` **sem `data`**: o placeholder deixa de valer.

## Goals / Non-Goals

**Goals:** a linha escondida na primeira carga e no erro dela, como nas outras listas; visível em toda busca
seguinte, inclusive durante a carga e no erro.

**Non-Goals:** mudar a busca no servidor, a espera, o `keepPreviousData` ou os estados da tabela.

## Decisions

### D1. Como a tela distingue a primeira carga das seguintes

A primeira carga é "nenhuma resposta com dados desde a montagem". A página guarda isso num estado,
`jaCarregou`, que vira `true` na primeira renderização com `query.isSuccess` e não volta mais. A atualização é
feita durante a renderização, no padrão do React para ajustar estado a partir de props: `if (query.isSuccess &&
!jaCarregou) setJaCarregou(true)`. Ela fica antes do retorno do `403`, para os hooks manterem a ordem.

`mostrarControles = (jaCarregou || query.isSuccess) && !vazia`. O `|| query.isSuccess` cobre a renderização em
que a primeira resposta chega, antes de o React repetir a renderização com o estado novo.

**Por que não `isPending`, nem `data !== undefined`:** com `keepPreviousData`, os dois distinguem a primeira
carga da busca seguinte **enquanto ela carrega**. Mas o erro de uma busca seguinte também fica sem `data`, e a
linha sumiria com o texto que a pessoa acabou de digitar. A conferência é o guarda 2.3, que troca a condição por
`query.data !== undefined` e reprova o caso do erro da busca seguinte.

**Por que não o cache do `QueryClient`:** ler `getQueriesData` na renderização não é reativo e acopla a tela à
forma das chaves. O estado local é o que a tela precisa saber.

Voltar à página com a lista no cache conta como já carregada: o `useQuery` responde `isSuccess` na primeira
renderização.

### D2. O erro de uma busca seguinte mantém a linha

A instrução nomeia o erro **da primeira carga**. No erro de uma busca seguinte, a pessoa precisa da busca para
corrigi-la, e o "Tentar de novo" repete a mesma busca. A linha fica, e o caso `o erro de uma busca seguinte
mantém a busca e o filtro na tela` a fixa.

## Risks / Trade-offs

- **[`setState` durante a renderização]** → é o padrão documentado do React para estado derivado que precisa
  de memória; é condicional e converge numa renderização. O `eslint` do projeto (`react-hooks` 7) aceita.
- **[O teste da busca seguinte fica verde por acaso se a linha nunca sumir]** → o guarda 2.2 esconde a linha em
  toda carga (`!query.isFetching`) e reprova esse caso.
