## Context

O código em `15dec2e` (main depois do PR #269):

- **`AgentListPage`** (l. 81–110) e **`KnowledgeBaseListPage`** (l. 135–160): `{hasX && (<Group gap="sm"
  wrap="nowrap">…)}` com o `TextInput` (`maw={340}`, `style={{ flex: 1 }}`, sem rótulo visível) e o
  `SegmentedControl` (`size="xs"`, `withItemsBorders={false}`, `style={{ border: '1px solid
  var(--mantine-color-default-border)', flexShrink: 0 }}`). A linha some quando a lista está vazia. Os testes
  acham as opções por `getByRole('radio', { name })`.
- **`ApplicationsListPage`** (l. 79–104): `Group gap="sm" wrap="wrap"` com o `TextInput` (`w={360}`,
  `maw="100%"`) e um `Group` interno com o `Text component="label"` "Estado" e o `NativeSelect`. A linha
  aparece em todos os estados. A busca e o filtro vão ao servidor (`useDebouncedValue` de 300 ms;
  `listApplications({ busca, estado })`), com `keepPreviousData`.
- O Mantine 9 dá ao `SegmentedControl` o papel `radiogroup` na raiz, e o `aria-label` passado a ele vira o nome
  acessível do grupo; cada opção é um `input type="radio"` com o rótulo como nome.

## Goals / Non-Goals

**Goals:** a lista de Aplicações externas no padrão das outras listas, como o quadro na versão 69; os testes
achando o controle pelo papel e pelo nome acessível.

**Non-Goals:** mudar a busca no servidor, a espera, o `?estado=`, os textos dos estados ou as outras listas.

## Decisions

### D1. O controle, com as props do `AgentListPage`

O `SegmentedControl` copia as props do `AgentListPage` e acrescenta `aria-label="Estado"`, que substitui o
rótulo visível e dá nome ao grupo para o leitor de tela. Os rótulos ficam no feminino, como nas Bases
("Todas", "Ativas"), e "Desativadas" acompanha o selo da aplicação ("Desativada"), não o "Inativas" das Bases.
Os valores continuam os do tipo `EstadoFiltro` (`todas`, `ativas`, `desativadas`), e o `?estado=` não muda.

### D2. A linha some só na `vazia`

A condição é a do bloco `vazia`: `query.isSuccess && apps.length === 0 && !filtrando`, numa variável usada
pelos dois. A busca sem resultado tem filtro, então a linha continua, e o Admin corrige a busca ali.

Diferença deliberada contra as outras listas: elas escondem a linha também durante a carga e no erro (o
`hasX` é falso sem dados). Aqui a instrução e o quadro nomeiam só a `vazia`. Na primeira carga, a linha aparece
e some se a resposta for `200 []`.

O contrato da spec ("vazia" e "busca sem resultado" são estados diferentes) continua igual: os dois textos,
as condições e o "Limpar busca" não mudam. O que muda é o caminho do teste e do cenário: antes, o caso partia
da `vazia` e digitava na busca; agora a `vazia` não tem busca, e a busca sem resultado parte de uma lista com
aplicações.

### D3. As outras listas (item 3 do pedido)

Conferido no `main` (`15dec2e`), todo `NativeSelect` e `Select` do `apps/frontend/src`:

| arquivo | rótulo | papel |
|---|---|---|
| `agents/components/AgentForm.tsx` | Provedor, Modelo | campo de formulário |
| `knowledge-bases/components/KnowledgeBaseOriginCard.tsx` | Provedor | campo de formulário |
| `knowledge-bases/components/KnowledgeDocumentModal.tsx` | Tipo de origem | campo de formulário |
| `users/components/UserDataCard.tsx`, `InviteForm.tsx` | Papel | campo de formulário |
| `applications/components/ApplicationAgentsTab.tsx` | Agente | escolha do agente a liberar |
| `channels/components/ChannelForm.tsx` | Agente responsável, Tipo de canal | campo de formulário |
| `applications/pages/ApplicationsListPage.tsx` | Estado | **filtro de estado** (esta change) |

Nenhuma outra lista usa lista suspensa como filtro de estado. As listas de canais e de servidores MCP não têm
filtro de estado, e a de usuários também não.

## Risks / Trade-offs

- **[A linha aparece e some na primeira carga de uma lista vazia]** → é o que a instrução pede (some na
  `vazia`); a conferência visual da homologação olha o estado `vazia`.
- **[Um teste que procure só por `radio` passaria com outro grupo de rádios na página]** → os casos procuram o
  `radiogroup` de nome "Estado" e as opções dentro dele; o guarda (tarefa 2.2) volta ao `NativeSelect` e
  confere a reprovação.
